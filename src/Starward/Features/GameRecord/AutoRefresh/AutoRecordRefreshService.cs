using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Features.ViewHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 自动更新战绩：软件启动时检查一次，把到期的「账号 + 数据板块」在后台拉一遍写进本地库。
/// <para>
/// 存在的意义是补齐档案而不是省一次点击：米游社的战绩接口只给「当期 + 上期」，月报只给
/// <c>optional_month</c> 里那几个月，超窗口没刷过的那一期在本地库里就是永久空缺。
/// </para>
/// <para>
/// 设计上刻意「不努力」：
/// 只在启动时判一次到期，进程常驻期间不再跨日重判（错过的到期日等下次启动补上）；
/// 某个板块出错（风控、需要验证、断网）就记一条异常直接跳过，不重试、不自愈、不弹窗，
/// 用户在数据页的配置浮层里点进异常记录自己看。
/// </para>
/// </summary>
internal class AutoRecordRefreshService
{

    /// <summary>
    /// 启动后先缓冲一段时间再检查，避开启动高峰；比自动签到的 10 秒更靠后，两者不抢同一个时间点。
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    /// <summary>
    /// 相邻两个请求之间的随机间隔（秒），与自动签到同一套节奏：宁可一轮跑几分钟，也不要一次性打完。
    /// </summary>
    private const int MinRequestDelaySeconds = 3;

    /// <summary>见 <see cref="MinRequestDelaySeconds"/>。</summary>
    private const int MaxRequestDelaySeconds = 8;

    /// <summary>异常记录最多保留条数，超出丢弃最旧的。</summary>
    private const int MaxErrorCount = 50;

    /// <summary>异常记录在 Setting 表中的键名。</summary>
    private const string ErrorsSettingKey = "auto_record_refresh_errors";


    private readonly ILogger<AutoRecordRefreshService> _logger;

    private readonly GameRecordService _gameRecordService;

    /// <summary>保证启动检查每个进程只跑一次。</summary>
    private int _startupCheckStarted;


    /// <summary>
    /// 初始化自动更新战绩服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="gameRecordService">战绩门面，用于枚举角色与调用各记录的刷新方法。</param>
    public AutoRecordRefreshService(ILogger<AutoRecordRefreshService> logger, GameRecordService gameRecordService)
    {
        _logger = logger;
        _gameRecordService = gameRecordService;
    }


    /// <summary>
    /// 启动检查：缓冲一段时间后跑一轮到期的更新。幂等，每个进程只生效一次。
    /// </summary>
    public void StartStartupCheck()
    {
        if (Interlocked.Exchange(ref _startupCheckStarted, 1) == 1)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StartupDelay);
                await RunBatchAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto record refresh startup check failed.");
            }
        });
    }


    /// <summary>
    /// 读取全部异常记录，最新的在前。
    /// </summary>
    /// <returns>异常记录列表；没有记录时为空列表。</returns>
    public List<RecordRefreshError> GetErrors()
    {
        string? json = AppConfig.GetValue<string>(default, ErrorsSettingKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<RecordRefreshError>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto record refresh: parse error list failed.");
            return [];
        }
    }


    /// <summary>
    /// 清空异常记录。
    /// </summary>
    public void ClearErrors()
    {
        AppConfig.SetValue<string>(null, ErrorsSettingKey);
    }


    /// <summary>
    /// 遍历所有角色的所有数据板块，逐个更新已启用且到期的那些。
    /// <para>
    /// 每个板块独立成败：出错只记一条异常并跳过，不影响同一角色的其他板块，也不影响其他角色。
    /// 只有成功的板块才写「上次更新」，失败的下次启动还会再试一次。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新成功的板块数。</returns>
    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        List<GameRecordRole> roles;
        try
        {
            roles = _gameRecordService.GetAllGameRoles();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto record refresh: load game roles failed.");
            return 0;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var targets = new List<(GameRecordRole Role, RecordRefreshItem Item, RecordRefreshConfig Config)>();
        foreach (GameRecordRole role in roles)
        {
            foreach (RecordRefreshItem item in RecordRefreshItemExtensions.GetItems(role.GameBiz))
            {
                RecordRefreshConfig config = RecordRefreshConfigStore.Load(role, item);
                if (!config.Enabled)
                {
                    continue;
                }
                if (!config.IsDue(now))
                {
                    continue;
                }
                targets.Add((role, item, config));
            }
        }
        if (targets.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Auto record refresh started for {count} target(s).", targets.Count);

        bool pacingStarted = false;
        async Task PaceAsync(CancellationToken token)
        {
            if (pacingStarted)
            {
                int seconds = Random.Shared.Next(MinRequestDelaySeconds, MaxRequestDelaySeconds + 1);
                await Task.Delay(TimeSpan.FromSeconds(seconds), token);
            }
            else
            {
                pacingStarted = true;
            }
        }

        int success = 0;
        // 遇到 aigis 不要弹极验；月报翻页也走同一套 3–8 秒间隔，避免一页接一页连打
        using (_gameRecordService.SuppressInteractiveRiskChallenge())
        using (_gameRecordService.UseRequestPacing(PaceAsync))
        {
            foreach ((GameRecordRole role, RecordRefreshItem item, RecordRefreshConfig config) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RefreshItemAsync(role, item, PaceAsync, cancellationToken);
                    config.LastRunTicks = DateTimeOffset.UtcNow.UtcTicks;
                    RecordRefreshConfigStore.Save(role.GameBiz, role.Uid, item, config);
                    RemoveError(role, item);
                    NotifyCompleted(role, item, succeeded: true);
                    success++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (miHoYoApiException ex)
                {
                    // 风控 / 需要验证 / Cookie 失效：后台解不开，记一条给用户看，直接跳过这个板块
                    _logger.LogWarning(ex, "Auto record refresh failed (biz {biz}, uid {uid}, item {item}, retcode {code}).", role.GameBiz, role.Uid, item, ex.ReturnCode);
                    RecordError(role, item, ex.ReturnCode, ex.Message);
                    NotifyCompleted(role, item, succeeded: false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Auto record refresh failed (biz {biz}, uid {uid}, item {item}).", role.GameBiz, role.Uid, item);
                    RecordError(role, item, 0, ex.Message);
                    NotifyCompleted(role, item, succeeded: false);
                }
            }
        }

        _logger.LogInformation("Auto record refresh finished, {success}/{total} succeeded.", success, targets.Count);
        return success;
    }


    /// <summary>
    /// 通知当前打开的数据页：红点与列表必须在 UI 线程更新。
    /// </summary>
    private static void NotifyCompleted(GameRecordRole role, RecordRefreshItem item, bool succeeded)
    {
        var message = new RecordRefreshCompletedMessage(role.GameBiz, role.Uid, item, succeeded);
        var dispatcher = MainWindow.Current?.DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }
        if (dispatcher.HasThreadAccess)
        {
            WeakReferenceMessenger.Default.Send(message);
            return;
        }
        dispatcher.TryEnqueue(() => WeakReferenceMessenger.Default.Send(message));
    }


    /// <summary>
    /// 更新一个数据板块。月报类只取当月，往前的月份留给用户手动补；
    /// 明细与页面「获取详情」一样全量覆盖（先删后写），不走已停用的增量探针。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="item">数据板块。</param>
    /// <param name="pace">请求节奏控制委托，在每次 API 调用前执行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task RefreshItemAsync(GameRecordRole role, RecordRefreshItem item, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
    {
        switch (item)
        {
            case RecordRefreshItem.SpiralAbyss:
                await pace(cancellationToken);
                await _gameRecordService.RefreshSpiralAbyssInfoAsync(role, 1, cancellationToken);
                await pace(cancellationToken);
                await _gameRecordService.RefreshSpiralAbyssInfoAsync(role, 2, cancellationToken);
                break;

            case RecordRefreshItem.ImaginariumTheater:
                await pace(cancellationToken);
                await _gameRecordService.RefreshImaginariumTheaterInfoAsync(role, cancellationToken);
                break;

            case RecordRefreshItem.StygianOnslaught:
                await pace(cancellationToken);
                await _gameRecordService.RefreshStygianOnslaughtInfosAsync(role, cancellationToken);
                break;

            case RecordRefreshItem.TravelersDiary:
                {
                    await pace(cancellationToken);
                    var summary = await _gameRecordService.GetTravelersDiarySummaryAsync(role);
                    if (summary.DataMonth > 0)
                    {
                        await pace(cancellationToken);
                        await _gameRecordService.GetTravelersDiaryDetailAsync(role, summary.DataMonth, 1, forceOverwrite: true, cancellationToken: cancellationToken);
                        await pace(cancellationToken);
                        await _gameRecordService.GetTravelersDiaryDetailAsync(role, summary.DataMonth, 2, forceOverwrite: true, cancellationToken: cancellationToken);
                    }
                    break;
                }

            case RecordRefreshItem.ForgottenHall:
                await pace(cancellationToken);
                await _gameRecordService.RefreshForgottenHallInfoAsync(role, 1, cancellationToken);
                await pace(cancellationToken);
                await _gameRecordService.RefreshForgottenHallInfoAsync(role, 2, cancellationToken);
                break;

            case RecordRefreshItem.PureFiction:
                await pace(cancellationToken);
                await _gameRecordService.RefreshPureFictionInfoAsync(role, 1, cancellationToken);
                await pace(cancellationToken);
                await _gameRecordService.RefreshPureFictionInfoAsync(role, 2, cancellationToken);
                break;

            case RecordRefreshItem.ApocalypticShadow:
                await pace(cancellationToken);
                await _gameRecordService.RefreshApocalypticShadowInfoAsync(role, 1, cancellationToken);
                await pace(cancellationToken);
                await _gameRecordService.RefreshApocalypticShadowInfoAsync(role, 2, cancellationToken);
                break;

            case RecordRefreshItem.ChallengePeak:
                await pace(cancellationToken);
                await _gameRecordService.RefreshStarRailChallengePeakDataAsync(role, cancellationToken);
                break;

            case RecordRefreshItem.SimulatedUniverse:
                await pace(cancellationToken);
                await _gameRecordService.GetSimulatedUniverseInfoAsync(role, true);
                break;

            case RecordRefreshItem.TrailblazeCalendar:
                {
                    await pace(cancellationToken);
                    var summary = await _gameRecordService.GetTrailblazeCalendarSummaryAsync(role);
                    if (!string.IsNullOrWhiteSpace(summary.DataMonth))
                    {
                        await pace(cancellationToken);
                        await _gameRecordService.GetTrailblazeCalendarDetailAsync(role, summary.DataMonth, 1, forceOverwrite: true, cancellationToken: cancellationToken);
                        await pace(cancellationToken);
                        await _gameRecordService.GetTrailblazeCalendarDetailAsync(role, summary.DataMonth, 2, forceOverwrite: true, cancellationToken: cancellationToken);
                    }
                    break;
                }

            case RecordRefreshItem.ShiyuDefense:
                await pace(cancellationToken);
                await _gameRecordService.RefreshShiyuDefenseInfoAsync(role, 1, cancellationToken);
                await pace(cancellationToken);
                await _gameRecordService.RefreshShiyuDefenseInfoAsync(role, 2, cancellationToken);
                break;

            case RecordRefreshItem.DeadlyAssault:
                await pace(cancellationToken);
                await _gameRecordService.RefreshDeadlyAssaultInfoAsync(role, 1, cancellationToken);
                await pace(cancellationToken);
                await _gameRecordService.RefreshDeadlyAssaultInfoAsync(role, 2, cancellationToken);
                break;

            case RecordRefreshItem.InterKnotReport:
                {
                    await pace(cancellationToken);
                    var summary = await _gameRecordService.GetInterKnotReportSummaryAsync(role);
                    if (!string.IsNullOrWhiteSpace(summary.DataMonth) && summary.MonthData?.List is { Count: > 0 } list)
                    {
                        foreach (var detail in list)
                        {
                            await pace(cancellationToken);
                            await _gameRecordService.GetInterKnotReportDetailAsync(role, summary.DataMonth, detail.DataType, forceOverwrite: true, cancellationToken: cancellationToken);
                        }
                    }
                    break;
                }
        }
    }


    /// <summary>
    /// 记录一条异常。同一「账号 + 板块」只保留最新一条，整体最多 <see cref="MaxErrorCount"/> 条。
    /// </summary>
    /// <param name="role">出错的角色。</param>
    /// <param name="item">出错的数据板块。</param>
    /// <param name="returnCode">米哈游接口返回码；非接口错误为 0。</param>
    /// <param name="message">异常消息，保留服务端原文。</param>
    private void RecordError(GameRecordRole role, RecordRefreshItem item, int returnCode, string? message)
    {
        try
        {
            List<RecordRefreshError> errors = GetErrors();
            errors.RemoveAll(x => x.Uid == role.Uid && x.GameBiz == role.GameBiz && x.Item == item);
            errors.Insert(0, new RecordRefreshError
            {
                GameBiz = role.GameBiz,
                Uid = role.Uid,
                Nickname = role.Nickname,
                Item = item,
                Time = DateTimeOffset.UtcNow,
                ReturnCode = returnCode,
                Message = message,
            });
            if (errors.Count > MaxErrorCount)
            {
                errors.RemoveRange(MaxErrorCount, errors.Count - MaxErrorCount);
            }
            AppConfig.SetValue(JsonSerializer.Serialize(errors), ErrorsSettingKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto record refresh: save error list failed.");
        }
    }


    /// <summary>
    /// 这个板块这次更新成功了，清掉它上次留下的异常记录。
    /// </summary>
    /// <param name="role">更新成功的角色。</param>
    /// <param name="item">更新成功的数据板块。</param>
    private void RemoveError(GameRecordRole role, RecordRefreshItem item)
    {
        try
        {
            List<RecordRefreshError> errors = GetErrors();
            if (errors.RemoveAll(x => x.Uid == role.Uid && x.GameBiz == role.GameBiz && x.Item == item) > 0)
            {
                AppConfig.SetValue(JsonSerializer.Serialize(errors), ErrorsSettingKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto record refresh: clear error entry failed.");
        }
    }

}
