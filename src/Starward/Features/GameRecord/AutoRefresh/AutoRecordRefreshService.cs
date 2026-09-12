using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Features.ViewHost;
using System;
using System.Collections.Generic;
using System.Globalization;
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
/// 排期单位是「任务」而不是「板块」：月报类板块拆成「当月」「上月」两个任务，各自开关、各自频率、
/// 各自「上次更新」与异常记录（当月适合几天一次跟进度，上月适合月初跑一次归档）。
/// </para>
/// <para>
/// 设计上刻意「不努力」：
/// 启动后至少缓冲 45 秒，且要等自动签到的第一轮批量打完再检查，避免和签到抢请求（签到卡住则 15 分钟后照样开刷）；
/// 只在启动时判一次到期，进程常驻期间不再跨日重判（错过的到期日等下次启动补上）；
/// 某个板块出错（风控、需要验证、断网）就记一条异常直接跳过，不重试、不自愈、不弹窗，
/// 用户在数据页的配置浮层里点进异常记录自己看。
/// </para>
/// </summary>
internal class AutoRecordRefreshService
{

    /// <summary>
    /// 等自动签到首轮批量的上限。签到请求若卡住，不能把战绩补档挂到进程退出。
    /// </summary>
    private static readonly TimeSpan StartupSignInWaitTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// 启动后至少缓冲这么久再开刷。签到无角色 / 全部关闭时首轮瞬间就完成，
    /// 那时首屏渲染、更新检查、抽卡名称迁移还都挤在启动这几十秒里，不能跟着一起打。
    /// </summary>
    private static readonly TimeSpan MinStartupDelay = TimeSpan.FromSeconds(45);

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
    /// 启动检查：等自动签到首轮批量结束后，再跑一轮到期的更新。幂等，每个进程只生效一次。
    /// </summary>
    /// <param name="waitForStartupBatch">
    /// 通常是自动签到的 <c>StartupBatchCompleted</c>。无角色或签到全关时它也会很快完成。
    /// </param>
    public void StartStartupCheck(Task waitForStartupBatch)
    {
        ArgumentNullException.ThrowIfNull(waitForStartupBatch);
        if (Interlocked.Exchange(ref _startupCheckStarted, 1) == 1)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await WaitForStartupBatchAsync(waitForStartupBatch);
                await RunBatchAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto record refresh startup check failed.");
            }
        });
    }


    /// <summary>
    /// 等到签到首轮打完；超时或门闩本身出错都不再等。签到刚结束时再隔 3–8 秒，避免和最后一次签到请求连打，
    /// 最后补足 <see cref="MinStartupDelay"/> 的启动缓冲。
    /// </summary>
    /// <param name="waitForStartupBatch">自动签到首轮完成任务。</param>
    private async Task WaitForStartupBatchAsync(Task waitForStartupBatch)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        bool batchSettled = true;
        using var timeout = new CancellationTokenSource(StartupSignInWaitTimeout);
        try
        {
            await waitForStartupBatch.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // 超时瞬间签到刚完成：不算超时，接着隔一档再刷
            batchSettled = waitForStartupBatch.IsCompleted;
            if (!batchSettled)
            {
                _logger.LogWarning("Auto record refresh: auto sign-in startup batch did not finish within {timeout}; starting anyway.", StartupSignInWaitTimeout);
            }
        }
        catch (Exception ex)
        {
            // 门闩任务本身出错（当前的 TaskCompletionSource 不会走到这里）：一样只是不等了。
            // 这里若把异常放出去，补档整轮都会被跳过，正好与「签到不能拖死补档」相反
            _logger.LogWarning(ex, "Auto record refresh: waiting for auto sign-in startup batch failed; starting anyway.");
        }

        if (batchSettled)
        {
            int seconds = Random.Shared.Next(MinRequestDelaySeconds, MaxRequestDelaySeconds + 1);
            _logger.LogInformation("Auto record refresh: delaying {seconds}s after the auto sign-in startup batch.", seconds);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
        }

        // 签到很快完成时上面这点间隔远不够避开启动高峰，补足下限；已经等超时的自然不再等
        TimeSpan remaining = MinStartupDelay - (DateTimeOffset.UtcNow - start);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
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
        var targets = new List<(GameRecordRole Role, RecordRefreshItem Item, RecordRefreshMonthTarget MonthTarget)>();
        foreach (GameRecordRole role in roles)
        {
            foreach (RecordRefreshItem item in RecordRefreshItemExtensions.GetItems(role.GameBiz))
            {
                // 月报类在这里展开成「当月」「上月」两个独立任务，两者的开关与到期各判各的
                foreach (RecordRefreshMonthTarget monthTarget in item.GetMonthTargets())
                {
                    RecordRefreshConfig config = RecordRefreshConfigStore.Load(role, item, monthTarget);
                    if (!config.Enabled)
                    {
                        continue;
                    }
                    if (!config.IsDue(now))
                    {
                        continue;
                    }
                    targets.Add((role, item, monthTarget));
                }
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
            foreach ((GameRecordRole role, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RefreshItemAsync(role, item, monthTarget, PaceAsync, cancellationToken);
                    // 这一轮跑了几分钟，其间用户可能在浮层里改过频率：只把 lastRun 写回最新那份，别拿轮次开始时的旧配置盖掉
                    RecordRefreshConfig latest = RecordRefreshConfigStore.Load(role.GameBiz, role.Uid, item, monthTarget);
                    latest.LastRunTicks = DateTimeOffset.UtcNow.UtcTicks;
                    RecordRefreshConfigStore.Save(role.GameBiz, role.Uid, item, latest, monthTarget);
                    RemoveError(role, item, monthTarget);
                    NotifyCompleted(role, item, monthTarget, succeeded: true);
                    success++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (miHoYoApiException ex)
                {
                    // 风控 / 需要验证 / Cookie 失效：后台解不开，记一条给用户看，直接跳过这个任务
                    _logger.LogWarning(ex, "Auto record refresh failed (biz {biz}, uid {uid}, item {item}, month {month}, retcode {code}).", role.GameBiz, role.Uid, item, monthTarget, ex.ReturnCode);
                    RecordError(role, item, monthTarget, ex.ReturnCode, ex.Message);
                    NotifyCompleted(role, item, monthTarget, succeeded: false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Auto record refresh failed (biz {biz}, uid {uid}, item {item}, month {month}).", role.GameBiz, role.Uid, item, monthTarget);
                    RecordError(role, item, monthTarget, 0, ex.Message);
                    NotifyCompleted(role, item, monthTarget, succeeded: false);
                }
            }
        }

        _logger.LogInformation("Auto record refresh finished, {success}/{total} succeeded.", success, targets.Count);
        return success;
    }


    /// <summary>
    /// 通知当前打开的数据页：红点与列表必须在 UI 线程更新。
    /// </summary>
    private static void NotifyCompleted(GameRecordRole role, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget, bool succeeded)
    {
        var message = new RecordRefreshCompletedMessage(role.GameBiz, role.Uid, item, monthTarget, succeeded);
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
    /// 跑一个任务。月报类按任务取当月或上月，再往前的月份留给用户手动补；
    /// 明细与页面「获取详情」一样全量覆盖（先删后写），不走已停用的增量探针。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="item">数据板块。</param>
    /// <param name="monthTarget">要取的月份；非月报板块恒为当月且不参与分支。</param>
    /// <param name="pace">请求节奏控制委托，在每次 API 调用前执行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task RefreshItemAsync(GameRecordRole role, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
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
                    // 先拿一次当月：月份要以接口返回的 data_month 为准而不是本机日期（账号服务器与本机可能差一天），
                    // 顺带把当月摘要写库、拿到 optional_month
                    await pace(cancellationToken);
                    var summary = await _gameRecordService.GetTravelersDiarySummaryAsync(role);
                    int month = summary.DataMonth;
                    if (monthTarget is RecordRefreshMonthTarget.Previous)
                    {
                        month = GetPreviousMonth(summary.DataMonth, summary.OptionalMonth);
                        if (month <= 0)
                        {
                            LogPreviousMonthUnavailable(role, item, summary.DataMonth.ToString(CultureInfo.InvariantCulture));
                            break;
                        }
                        // 上月的统计摘要也要覆盖一遍，否则本地库里那个月还停在它当月时的半程快照
                        await pace(cancellationToken);
                        await _gameRecordService.GetTravelersDiarySummaryAsync(role, month);
                    }
                    if (month > 0)
                    {
                        await pace(cancellationToken);
                        await _gameRecordService.GetTravelersDiaryDetailAsync(role, month, 1, forceOverwrite: true, cancellationToken: cancellationToken);
                        await pace(cancellationToken);
                        await _gameRecordService.GetTravelersDiaryDetailAsync(role, month, 2, forceOverwrite: true, cancellationToken: cancellationToken);
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
                    string? month = summary.DataMonth;
                    if (monthTarget is RecordRefreshMonthTarget.Previous)
                    {
                        month = GetPreviousMonth(summary.DataMonth, summary.OptionalMonth);
                        if (month is null)
                        {
                            LogPreviousMonthUnavailable(role, item, summary.DataMonth);
                            break;
                        }
                        await pace(cancellationToken);
                        await _gameRecordService.GetTrailblazeCalendarSummaryAsync(role, month);
                    }
                    if (!string.IsNullOrWhiteSpace(month))
                    {
                        await pace(cancellationToken);
                        await _gameRecordService.GetTrailblazeCalendarDetailAsync(role, month, 1, forceOverwrite: true, cancellationToken: cancellationToken);
                        await pace(cancellationToken);
                        await _gameRecordService.GetTrailblazeCalendarDetailAsync(role, month, 2, forceOverwrite: true, cancellationToken: cancellationToken);
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
                    if (monthTarget is RecordRefreshMonthTarget.Previous)
                    {
                        string? previous = GetPreviousMonth(summary.DataMonth, summary.OptionalMonth);
                        if (previous is null)
                        {
                            LogPreviousMonthUnavailable(role, item, summary.DataMonth);
                            break;
                        }
                        // 明细要按目标月自己的 month_data 列数据类型，不能沿用当月那一份
                        await pace(cancellationToken);
                        summary = await _gameRecordService.GetInterKnotReportSummaryAsync(role, previous);
                    }
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
    /// 「上月」模式要拉的月份（原神用月份号 1–12）。
    /// <para>
    /// 一律从接口返回的当月往前推一个月，再用 <c>optional_month</c> 复核：那个列表就是接口愿意受理的月份，
    /// 不在里面的月份请求了也拿不到数据（新号开服首月就是这种情况）。
    /// </para>
    /// </summary>
    /// <param name="dataMonth">接口返回的当月月份号。</param>
    /// <param name="optionalMonths">接口给出的可查询月份；为空时不复核。</param>
    /// <returns>要拉的月份号；查不到时返回 0。</returns>
    private static int GetPreviousMonth(int dataMonth, List<int>? optionalMonths)
    {
        if (dataMonth is < 1 or > 12)
        {
            return 0;
        }
        int previous = dataMonth == 1 ? 12 : dataMonth - 1;
        if (optionalMonths is { Count: > 0 } && !optionalMonths.Contains(previous))
        {
            return 0;
        }
        return previous;
    }


    /// <summary>
    /// 「上月」模式要拉的月份（星铁 / 绝区零用 <c>yyyyMM</c>）。判定规则同 <see cref="GetPreviousMonth(int, List{int})"/>。
    /// </summary>
    /// <param name="dataMonth">接口返回的当月，形如 <c>202609</c>。</param>
    /// <param name="optionalMonths">接口给出的可查询月份；为空时不复核。</param>
    /// <returns>要拉的月份；查不到时返回 null。</returns>
    private static string? GetPreviousMonth(string? dataMonth, List<string>? optionalMonths)
    {
        if (!DateTime.TryParseExact(dataMonth, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime month))
        {
            return null;
        }
        string previous = month.AddMonths(-1).ToString("yyyyMM", CultureInfo.InvariantCulture);
        if (optionalMonths is { Count: > 0 } && !optionalMonths.Contains(previous))
        {
            return null;
        }
        return previous;
    }


    /// <summary>
    /// 选了「上月」但接口不给那个月：记一条日志就算这轮做完，不当失败也不写异常记录
    /// ——重试一百次结果也一样，留给下个月自然恢复。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="item">数据板块。</param>
    /// <param name="dataMonth">接口返回的当月。</param>
    private void LogPreviousMonthUnavailable(GameRecordRole role, RecordRefreshItem item, string? dataMonth)
    {
        _logger.LogInformation("Auto record refresh: previous month is not available (biz {biz}, uid {uid}, item {item}, data month {month}).", role.GameBiz, role.Uid, item, dataMonth);
    }


    /// <summary>
    /// 记录一条异常。同一个任务只保留最新一条，整体最多 <see cref="MaxErrorCount"/> 条。
    /// </summary>
    /// <param name="role">出错的角色。</param>
    /// <param name="item">出错的数据板块。</param>
    /// <param name="monthTarget">出错的月份任务。</param>
    /// <param name="returnCode">米哈游接口返回码；非接口错误为 0。</param>
    /// <param name="message">异常消息，保留服务端原文。</param>
    private void RecordError(GameRecordRole role, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget, int returnCode, string? message)
    {
        try
        {
            List<RecordRefreshError> errors = GetErrors();
            errors.RemoveAll(x => x.Uid == role.Uid && x.GameBiz == role.GameBiz && x.Item == item && x.MonthTarget == monthTarget);
            errors.Insert(0, new RecordRefreshError
            {
                GameBiz = role.GameBiz,
                Uid = role.Uid,
                Nickname = role.Nickname,
                Item = item,
                MonthTarget = monthTarget,
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
    /// 这个任务这次更新成功了，清掉它上次留下的异常记录。
    /// </summary>
    /// <param name="role">更新成功的角色。</param>
    /// <param name="item">更新成功的数据板块。</param>
    /// <param name="monthTarget">更新成功的月份任务。</param>
    private void RemoveError(GameRecordRole role, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget)
    {
        try
        {
            List<RecordRefreshError> errors = GetErrors();
            if (errors.RemoveAll(x => x.Uid == role.Uid && x.GameBiz == role.GameBiz && x.Item == item && x.MonthTarget == monthTarget) > 0)
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
