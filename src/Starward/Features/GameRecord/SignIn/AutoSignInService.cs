using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord.SignIn;

/// <summary>
/// 自动签到：主界面启动后批量为所有账号(cookie)下每个游戏角色静默签到；URL/CLI 带账号启动游戏时对指定角色补签一次。
/// 是否签到按游戏区分（<see cref="AppConfig.GetAutoSignInEnabled(GameBiz)"/>）；依赖服务器返回的 <see cref="Core.GameRecord.SignIn.SignInRewardInfo.IsSign"/> 天然去重，再加 10 分钟失败冷却避免出错时反复请求。
/// </summary>
internal class AutoSignInService
{

    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 启动后先缓冲一段时间再开始批量签到，避开启动高峰。
    /// </summary>
    private static readonly TimeSpan StartupBatchDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 相邻两个签到请求之间的随机间隔（秒），模拟真人操作节奏，避免短时间内大量请求。
    /// </summary>
    private const int MinRequestDelaySeconds = 3;
    private const int MaxRequestDelaySeconds = 8;


    private readonly ILogger<AutoSignInService> _logger;

    private readonly GameRecordService _gameRecordService;

    private readonly SignInService _signInService;

    /// <summary>
    /// 保证启动批量签到每次启动只执行一次。
    /// </summary>
    private int _startupBatchStarted;


    /// <summary>
    /// 初始化自动签到服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="gameRecordService">战绩门面，用于枚举角色与查询签到状态。</param>
    /// <param name="signInService">签到业务服务，执行实际签到请求。</param>
    public AutoSignInService(ILogger<AutoSignInService> logger, GameRecordService gameRecordService, SignInService signInService)
    {
        _logger = logger;
        _gameRecordService = gameRecordService;
        _signInService = signInService;
    }


    /// <summary>
    /// 指定游戏是否开启自动签到（按游戏区分）。
    /// </summary>
    /// <param name="biz">游戏业务线，如 hk4e_cn。</param>
    /// <returns>是否已开启自动签到。</returns>
    public bool IsEnabled(GameBiz biz) => AppConfig.GetAutoSignInEnabled(biz);

    /// <summary>
    /// 设置指定游戏的自动签到开关（按游戏区分）。
    /// </summary>
    /// <param name="biz">游戏业务线。</param>
    /// <param name="value">是否开启。</param>
    public void SetEnabled(GameBiz biz, bool value) => AppConfig.SetAutoSignInEnabled(biz, value);



    /// <summary>
    /// 启动游戏时对指定 UID 尝试自动签到（需该游戏已开启自动签到）。
    /// 用于 URL / CLI 等不经主界面的启动路径；静默执行（冷却 / 已签 / 失败均不打扰用户）。
    /// </summary>
    /// <param name="gameBiz">启动器侧游戏业务线（含 bilibili，内部映射到签到用 biz）。</param>
    /// <param name="uid">米游社工具箱中的游戏角色 UID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task TrySignInForLaunchAccountAsync(GameBiz gameBiz, long uid, CancellationToken cancellationToken = default)
    {
        if (uid <= 0)
        {
            return;
        }

        try
        {
            // 与 SignInButton 一致：bilibili 服角色与开关按国服 biz 存储
            GameBiz signInBiz = gameBiz.Server is "bilibili" ? $"{gameBiz.Game}_cn" : gameBiz;
            if (!GameFeatureConfig.FromGameBiz(signInBiz).SupportSignIn || !IsEnabled(signInBiz))
            {
                return;
            }

            GameRecordRole? role = _gameRecordService.GetGameRoles(signInBiz).FirstOrDefault(r => r.Uid == uid);
            if (role is null)
            {
                _logger.LogWarning("Auto sign-in on launch: role not found (biz {biz}, uid {uid}).", signInBiz, uid);
                return;
            }

            // 单次签到无需批量节奏延时
            await SignInRoleCoreAsync(role, static _ => Task.CompletedTask, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto sign-in on launch failed (biz {biz}, uid {uid}).", gameBiz, uid);
        }
    }


    /// <summary>
    /// 软件启动后批量签到：遍历所有账号(cookie)下每个支持且开启自动签到的游戏角色，按账号分组逐个签到，请求之间插入随机延时。
    /// 每次启动只执行一次，异常吞掉不打扰用户。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunStartupBatchAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _startupBatchStarted, 1) == 1)
        {
            return;
        }
        try
        {
            // 启动后缓冲，避开启动高峰
            await Task.Delay(StartupBatchDelay, cancellationToken);

            List<GameRecordRole> roles;
            try
            {
                roles = _gameRecordService.GetAllGameRoles()
                    .Where(r => GameFeatureConfig.FromGameBiz(r.GameBiz).SupportSignIn)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto sign-in batch: load game roles failed.");
                return;
            }
            if (roles.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Auto sign-in batch started for {count} role(s).", roles.Count);
            // 第一个请求不延时，之后每个请求前插入随机间隔，做到“一个接一个 + 请求间随机延时”
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

            foreach (GameRecordRole role in roles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsEnabled(role.GameBiz))
                {
                    // 该游戏未开启自动签到则跳过（每次循环重新读取，及时响应用户中途关闭）
                    continue;
                }
                try
                {
                    await SignInRoleCoreAsync(role, PaceAsync, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 单个角色失败（如 cookie 失效）不影响其余角色
                    _logger.LogError(ex, "Auto sign-in batch: role failed (biz {biz}, uid {uid}).", role.GameBiz, role.Uid);
                }
            }
            _logger.LogInformation("Auto sign-in batch finished.");
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto sign-in batch failed.");
        }
    }



    /// <summary>
    /// 单个角色签到核心：冷却检查 → 查询是否已签 → 未签则签到 → 记录成功 / 失败冷却。
    /// <paramref name="pace"/> 用于在每个网络请求前插入节奏延时。
    /// </summary>
    /// <param name="role">待签到的游戏角色。</param>
    /// <param name="pace">请求节奏控制委托，在每次 API 调用前执行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task SignInRoleCoreAsync(GameRecordRole role, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
    {
        string failureKey = $"auto_sign_in_last_failure_ticks_{role.GameBiz}_{role.Uid}";
        if (IsInFailureCooldown(failureKey))
        {
            return;
        }

        await pace(cancellationToken);
        var info = await _gameRecordService.GetSignInInfoAsync(role, cancellationToken);
        if (info.IsSign)
        {
            SetSettingValue(failureKey, "0");
            return;
        }

        await pace(cancellationToken);
        SignInActionResponse result = await _signInService.ClaimSignInAsync(role, cancellationToken);
        if (result.Kind is SignInActionResult.Success or SignInActionResult.AlreadySigned)
        {
            SetSettingValue(failureKey, "0");
            _logger.LogInformation("Auto sign-in succeeded (biz {biz}, uid {uid}, result {result})", role.GameBiz, role.Uid, result);
        }
        else
        {
            SetSettingValue(failureKey, DateTimeOffset.UtcNow.Ticks.ToString());
            _logger.LogInformation("Auto sign-in not completed (biz {biz}, uid {uid}, result {result})", role.GameBiz, role.Uid, result);
        }
    }



    /// <summary>
    /// 检查角色是否处于失败冷却期，避免短时间内重复请求。
    /// </summary>
    /// <param name="failureKey">Setting 表键名，按 biz + uid 区分。</param>
    /// <returns>冷却未结束时返回 true。</returns>
    private static bool IsInFailureCooldown(string failureKey)
    {
        if (long.TryParse(GetSettingValue(failureKey), out long ticks) && ticks != 0)
        {
            var lastFailure = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (DateTimeOffset.UtcNow - lastFailure < FailureCooldown)
            {
                return true;
            }
        }
        return false;
    }



    /// <summary>从 Setting 表读取键值。</summary>
    /// <param name="key">设置键。</param>
    /// <returns>存储的字符串，不存在时返回 null。</returns>
    private static string? GetSettingValue(string key)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<string>("SELECT Value FROM Setting WHERE Key = @key LIMIT 1;", new { key });
    }


    /// <summary>写入 Setting 表键值（INSERT OR REPLACE）。</summary>
    /// <param name="key">设置键。</param>
    /// <param name="value">设置值。</param>
    private static void SetSettingValue(string key, string value)
    {
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("INSERT OR REPLACE INTO Setting (Key, Value) VALUES (@key, @value);", new { key, value });
    }


}
