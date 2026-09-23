using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.CloudGame;

/// <summary>
/// 自动领取云游戏每日免费时长：启动后先领一轮，之后常驻循环在日界（UTC+8 4:00）后再领。
/// 是否领取按游戏区分（<see cref="AppConfig.GetAutoCloudGameFreeTimeEnabled(GameBiz)"/>），开了就把该区服下**全部**通行证都领一遍；
/// 失败冷却 10 分钟，避免出错时反复请求。
/// </summary>
/// <remarks>
/// 与自动签到的差别有两处，其余调度结构刻意保持一致（见 <see cref="GameRecord.SignIn.AutoSignInService"/>）：
/// <list type="number">
/// <item>日界不是零点，而是 UTC+8 4:00，见 <see cref="CloudGameFreeTimeSchedule"/>。</item>
/// <item>
/// 接口不下发「今天是否已领」，服务端日期无从对账，只能按本机推算的日界记账（<see cref="AppConfig.GetCloudGameFreeTimeClaimedDate"/>）。
/// 好在免费时长整日都能领、重复登录也不会出错，本机时钟偏差最多让领取时刻前后挪动，不会漏掉某一天。
/// </item>
/// </list>
/// 启动后的第一轮排在自动签到首轮之后（见 <see cref="StartResident"/>），避免两边同时对米哈游接口发请求。
/// </remarks>
internal sealed class AutoCloudGameFreeTimeService
{

    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(10);

    /// <summary>签到首轮结束后再缓冲一段时间才开始领取，避开启动高峰。</summary>
    private static readonly TimeSpan StartupBatchDelay = TimeSpan.FromSeconds(10);

    /// <summary>等待自动签到首轮的上限。签到卡住不能把领取一起挂死。</summary>
    private static readonly TimeSpan StartupSignInWaitTimeout = TimeSpan.FromMinutes(15);

    /// <summary>长等待拆段的上限。相对 Delay 不计休眠，分段醒来用墙上时钟重算剩余。</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(15);

    /// <summary>当天仍有游戏没领成（断网等）时的重试间隔。</summary>
    private static readonly TimeSpan IncompleteRetry = TimeSpan.FromMinutes(30);

    /// <summary>登录态失效 / 风控：拉长间隔，避免连打。</summary>
    private static readonly TimeSpan BlockedRetry = TimeSpan.FromHours(2);

    /// <summary>相邻两个游戏的请求之间的随机间隔（秒），避免短时间内连打。</summary>
    private const int MinRequestDelaySeconds = 3;
    private const int MaxRequestDelaySeconds = 8;

    private const int MinResumeStaggerSeconds = 10;
    private const int MaxResumeStaggerSeconds = 90;

    /// <summary>失败时间记账的 Setting 键前缀。</summary>
    private const string FailureKeyPrefix = "auto_cloud_game_free_time_last_failure_ticks_";

    /// <summary>
    /// 已接入云游戏钱包的区服，即可以领免费时长的游戏。
    /// 从 <see cref="GameFeatureConfig"/> 推导，新游戏开了 <c>SupportCloudGameWallet</c> 就自动纳入，不必再改这里。
    /// </summary>
    private static readonly GameBiz[] CloudGameFreeTimeGames =
        [.. GameBiz.AllGameBizs.Where(biz => GameFeatureConfig.FromGameBiz(biz).SupportCloudGameWallet)];


    private readonly ILogger<AutoCloudGameFreeTimeService> _logger;

    private readonly CloudGameWalletService _walletService;

    /// <summary>保证常驻循环每个进程只启动一次。</summary>
    private int _residentStarted;

    /// <summary>
    /// 启动后第一轮领取结束（成功、失败、没有开启的游戏、等待阶段就退出都算）。
    /// 战绩自动更新等这一下再开，避免和领取抢请求。
    /// </summary>
    private readonly TaskCompletionSource _startupBatchCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>用户打开开关：必须带此标志，只 Wake 时 nextDue 可能已排到明天。</summary>
    private int _forceCheck;

    /// <summary>系统从休眠恢复。到点后的批量前用于错峰，未到点的唤醒会清掉以免误延后。</summary>
    private int _resumed;

    private readonly SemaphoreSlim _batchGate = new(1, 1);

    private readonly SemaphoreSlim _wake = new(0, 1);


    /// <summary>
    /// 初始化自动领取云游戏免费时长服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="walletService">云游戏钱包服务，执行实际的登录与领取。</param>
    public AutoCloudGameFreeTimeService(ILogger<AutoCloudGameFreeTimeService> logger, CloudGameWalletService walletService)
    {
        _logger = logger;
        _walletService = walletService;
    }


    /// <summary>
    /// 指定游戏是否开启自动领取免费时长（按游戏区分）。
    /// </summary>
    /// <param name="biz">游戏业务线，如 hk4e_cn。</param>
    /// <returns>是否已开启。</returns>
    public bool IsEnabled(GameBiz biz) => AppConfig.GetAutoCloudGameFreeTimeEnabled(biz);

    /// <summary>
    /// 设置指定游戏的自动领取免费时长开关（按游戏区分）。
    /// </summary>
    /// <param name="biz">游戏业务线。</param>
    /// <param name="value">是否开启。</param>
    public void SetEnabled(GameBiz biz, bool value) => AppConfig.SetAutoCloudGameFreeTimeEnabled(biz, value);


    /// <summary>
    /// 启动后第一轮领取已结束。战绩自动更新等这一下再开始。
    /// 没有开启的游戏、本轮失败也会完成；常驻循环若在首轮前退出同样完成，避免把后续补档挂死。
    /// </summary>
    internal Task StartupBatchCompleted => _startupBatchCompleted.Task;


    /// <summary>
    /// 启动常驻循环：等自动签到首轮打完再缓冲一段时间，领一轮，之后按绝对到期时刻重复。幂等。
    /// </summary>
    /// <param name="waitForSignInStartupBatch">
    /// 自动签到的首轮完成任务。免费时长的领取排在签到之后，两者不抢同一时刻的请求；
    /// 签到迟迟不结束时最多等 <see cref="StartupSignInWaitTimeout"/> 就照常开始。
    /// </param>
    public void StartResident(Task waitForSignInStartupBatch)
    {
        ArgumentNullException.ThrowIfNull(waitForSignInStartupBatch);
        if (Interlocked.Exchange(ref _residentStarted, 1) == 1)
        {
            return;
        }
        _ = Task.Run(() => RunResidentLoopAsync(waitForSignInStartupBatch, CancellationToken.None));
    }


    /// <summary>
    /// 打开开关后立即检查。必须置 ForceCheck，否则循环可能按已排到明天的 nextDue 继续睡。
    /// </summary>
    public void RequestImmediateCheck()
    {
        Interlocked.Exchange(ref _forceCheck, 1);
        Wake();
    }


    /// <summary>
    /// 系统从休眠/休眠到磁盘恢复。只唤醒等待去重算绝对时刻，未到点不跑批量。
    /// </summary>
    public void NotifySystemResumed()
    {
        Interlocked.Exchange(ref _resumed, 1);
        Wake();
    }


    /// <summary>
    /// 常驻循环：等签到首轮 → 启动缓冲 → 立刻领一轮 → 按聚合结果排 nextDue。
    /// 整轮（等待、错峰、批量）异常都只影响本轮并排下一次，不能让常驻领取在本进程内静默死掉。
    /// </summary>
    private async Task RunResidentLoopAsync(Task waitForSignInStartupBatch, CancellationToken cancellationToken)
    {
        try
        {
            await WaitForSignInStartupBatchAsync(waitForSignInStartupBatch, cancellationToken);
            await WaitOrWakeAsync(StartupBatchDelay, cancellationToken);
            DateTimeOffset nextDue = DateTimeOffset.UtcNow;
            while (!cancellationToken.IsCancellationRequested)
            {
                AutoCloudGameFreeTimeRoundOutcome outcome;
                try
                {
                    outcome = await RunRoundAsync(nextDue, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto cloud game free time round failed.");
                    outcome = AutoCloudGameFreeTimeRoundOutcome.Incomplete;
                }
                finally
                {
                    // 只关心启动这一轮已经打完；后续跨日 TrySetResult 是空操作
                    _startupBatchCompleted.TrySetResult();
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                nextDue = outcome switch
                {
                    AutoCloudGameFreeTimeRoundOutcome.Incomplete => CloudGameFreeTimeSchedule.GetRetryOrNextDay(now, IncompleteRetry),
                    AutoCloudGameFreeTimeRoundOutcome.Blocked => CloudGameFreeTimeSchedule.GetRetryOrNextDay(now, BlockedRetry),
                    _ => CloudGameFreeTimeSchedule.GetNextDailyDue(now),
                };
                _logger.LogInformation("Auto cloud game free time next due {due} (outcome {outcome}).", nextDue, outcome);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto cloud game free time resident loop failed.");
        }
        finally
        {
            // 首轮还没跑到（等待阶段就退出）也要放开，不能把后续补档挂死
            _startupBatchCompleted.TrySetResult();
        }
    }


    /// <summary>
    /// 等自动签到首轮打完；超时或门闩本身出错都不再等，照常开始领取。
    /// </summary>
    /// <param name="waitForSignInStartupBatch">自动签到首轮完成任务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WaitForSignInStartupBatchAsync(Task waitForSignInStartupBatch, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupSignInWaitTimeout);
        try
        {
            await waitForSignInStartupBatch.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!waitForSignInStartupBatch.IsCompleted)
            {
                _logger.LogWarning("Auto cloud game free time: auto sign-in startup batch did not finish within {timeout}; starting anyway.", StartupSignInWaitTimeout);
            }
        }
        catch (Exception ex)
        {
            // 门闩任务本身出错：一样只是不等了。放出去的话整轮领取都会被跳过，正好与「签到不能拖死领取」相反
            _logger.LogWarning(ex, "Auto cloud game free time: waiting for auto sign-in startup batch failed; starting anyway.");
        }
    }


    /// <summary>
    /// 一轮：等到 <paramref name="nextDue"/>（或 ForceCheck）→ 休眠唤醒错峰 → 跑批量。
    /// </summary>
    /// <param name="nextDue">本轮的到期时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮聚合结果。</returns>
    private async Task<AutoCloudGameFreeTimeRoundOutcome> RunRoundAsync(DateTimeOffset nextDue, CancellationToken cancellationToken)
    {
        bool force = await WaitUntilDueAsync(nextDue, cancellationToken);
        bool resumed = Interlocked.Exchange(ref _resumed, 0) == 1;
        if (resumed && !force && DateTimeOffset.UtcNow >= nextDue)
        {
            int seconds = Random.Shared.Next(MinResumeStaggerSeconds, MaxResumeStaggerSeconds + 1);
            _logger.LogInformation("Auto cloud game free time resume stagger {seconds}s.", seconds);
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        }
        return await RunBatchAsync(cancellationToken);
    }


    /// <summary>
    /// 等到 <paramref name="nextDue"/>，或收到 ForceCheck。分段 Delay，每次醒来用墙上时钟重算。
    /// </summary>
    /// <returns>因 ForceCheck 跳出时为 true。</returns>
    private async Task<bool> WaitUntilDueAsync(DateTimeOffset nextDue, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (Interlocked.Exchange(ref _forceCheck, 0) == 1)
            {
                return true;
            }
            if (now >= nextDue)
            {
                return false;
            }
            TimeSpan wait = nextDue - now;
            if (wait > WatchdogInterval)
            {
                wait = WatchdogInterval;
            }
            await WaitOrWakeAsync(wait, cancellationToken);
            // 没到点的唤醒只用于重算剩余，不要把 Resume 标志留到真正到期再错峰
            if (DateTimeOffset.UtcNow < nextDue)
            {
                Interlocked.Exchange(ref _resumed, 0);
            }
        }
    }


    /// <summary>
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> 与唤醒信号竞速。任一方完成后取消另一半，避免 Delay 泄漏。
    /// </summary>
    private async Task WaitOrWakeAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (wait <= TimeSpan.Zero)
        {
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delayTask = Task.Delay(wait, linked.Token);
        Task wakeTask = _wake.WaitAsync(linked.Token);
        Task completed = await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);
        linked.Cancel();
        try
        {
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 被 linked 取消的那一半；赢家已完成
        }
    }


    /// <summary>唤醒等待中的循环。信号量已满时忽略（已有一次待处理唤醒）。</summary>
    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // already signaled
        }
    }


    /// <summary>
    /// 跑一轮批量领取。入口串行化，防止 ForceCheck 与循环重叠进入。
    /// </summary>
    private async Task<AutoCloudGameFreeTimeRoundOutcome> RunBatchAsync(CancellationToken cancellationToken)
    {
        await _batchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunBatchCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _batchGate.Release();
        }
    }


    /// <summary>
    /// 遍历已接入云游戏钱包且开了开关的游戏，每个游戏再遍历其全部通行证，依次领取当日免费时长。
    /// 聚合 Incomplete &gt; Blocked &gt; Completed。
    /// </summary>
    private async Task<AutoCloudGameFreeTimeRoundOutcome> RunBatchCoreAsync(CancellationToken cancellationToken)
    {
        DateOnly today = CloudGameFreeTimeSchedule.GetServerDate(DateTimeOffset.UtcNow);
        bool sawIncomplete = false;
        bool sawBlocked = false;
        bool sawAny = false;

        // 节奏按「整轮」计，不是按游戏：一个游戏下可能有好几个通行证，要让所有请求都隔开
        bool pacingStarted = false;
        async Task PaceAsync(CancellationToken token)
        {
            if (pacingStarted)
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(MinRequestDelaySeconds, MaxRequestDelaySeconds + 1)), token);
            }
            else
            {
                pacingStarted = true;
            }
        }

        foreach (GameBiz biz in CloudGameFreeTimeGames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEnabled(biz))
            {
                continue;
            }
            sawAny = true;

            AutoCloudGameFreeTimeResult result;
            try
            {
                result = await ClaimGameCoreAsync(biz, today, PaceAsync, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto cloud game free time batch: game failed (biz {biz}).", biz);
                result = AutoCloudGameFreeTimeResult.Failed;
            }

            if (result is AutoCloudGameFreeTimeResult.Failed or AutoCloudGameFreeTimeResult.Cooldown)
            {
                sawIncomplete = true;
            }
            else if (result is AutoCloudGameFreeTimeResult.Blocked)
            {
                sawBlocked = true;
            }
        }

        if (!sawAny)
        {
            return AutoCloudGameFreeTimeRoundOutcome.Completed;
        }
        if (sawIncomplete)
        {
            return AutoCloudGameFreeTimeRoundOutcome.Incomplete;
        }
        if (sawBlocked)
        {
            return AutoCloudGameFreeTimeRoundOutcome.Blocked;
        }
        return AutoCloudGameFreeTimeRoundOutcome.Completed;
    }


    /// <summary>
    /// 单个游戏：列出该区服全部通行证，逐个领取。每个账号各自计冷却与当日记账，
    /// 一个账号凭证坏了不影响其他账号。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="today">本轮开始时本机推算的云游戏日历日。</param>
    /// <param name="pace">请求节奏控制，每次真正发请求前执行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该游戏对本轮聚合的贡献，取各账号中最严重的一个。</returns>
    private async Task<AutoCloudGameFreeTimeResult> ClaimGameCoreAsync(
        GameBiz biz,
        DateOnly today,
        Func<CancellationToken, Task> pace,
        CancellationToken cancellationToken)
    {
        List<CloudGameAccount> accounts = await _walletService.GetAccountsAsync(biz, cancellationToken);
        if (accounts.Count == 0)
        {
            _logger.LogInformation("Auto cloud game free time skipped, no credential from cloud game client (biz {biz}).", biz);
            return AutoCloudGameFreeTimeResult.NoAccount;
        }

        _logger.LogInformation("Auto cloud game free time: {count} account(s) to check (biz {biz}).", accounts.Count, biz);
        bool sawFailed = false;
        bool sawBlocked = false;
        bool sawCooldown = false;
        bool sawClaimed = false;

        foreach (CloudGameAccount account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutoCloudGameFreeTimeResult result = await ClaimAccountCoreAsync(biz, account, today, pace, cancellationToken);
            switch (result)
            {
                case AutoCloudGameFreeTimeResult.Failed:
                    sawFailed = true;
                    break;
                case AutoCloudGameFreeTimeResult.Blocked:
                    sawBlocked = true;
                    break;
                case AutoCloudGameFreeTimeResult.Cooldown:
                    sawCooldown = true;
                    break;
                case AutoCloudGameFreeTimeResult.Claimed:
                    sawClaimed = true;
                    break;
            }
        }

        if (sawFailed || sawCooldown)
        {
            return AutoCloudGameFreeTimeResult.Failed;
        }
        if (sawBlocked)
        {
            return AutoCloudGameFreeTimeResult.Blocked;
        }
        return sawClaimed ? AutoCloudGameFreeTimeResult.Claimed : AutoCloudGameFreeTimeResult.AlreadyClaimed;
    }


    /// <summary>
    /// 单个通行证：冷却 → 当日是否已领 → 未领则领取。
    /// 请求抛出的异常在此收口：一律记录失败时间进冷却，凭证失效按 <see cref="AutoCloudGameFreeTimeResult.Blocked"/> 处理。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="account">通行证账号。</param>
    /// <param name="today">本轮开始时本机推算的云游戏日历日。</param>
    /// <param name="pace">请求节奏控制。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该账号的领取结果。</returns>
    private async Task<AutoCloudGameFreeTimeResult> ClaimAccountCoreAsync(
        GameBiz biz,
        CloudGameAccount account,
        DateOnly today,
        Func<CancellationToken, Task> pace,
        CancellationToken cancellationToken)
    {
        // 今天这个账号领过就不再请求：一天内反复启动软件不应该反复登录云游戏
        if (AppConfig.GetCloudGameFreeTimeClaimedDate(biz, account.AccountId) is DateOnly claimed && claimed >= today)
        {
            return AutoCloudGameFreeTimeResult.AlreadyClaimed;
        }

        // 冷却按账号算：某个账号的票坏了，不该把同游戏其他账号一起挡在外面
        string failureKey = GetFailureKey(biz, account.AccountId);
        if (IsInFailureCooldown(failureKey))
        {
            return AutoCloudGameFreeTimeResult.Cooldown;
        }

        try
        {
            await pace(cancellationToken);
            CloudGameFreeTimeClaimResult? claim = await _walletService.ClaimDailyFreeTimeAsync(biz, account.AccountId, cancellationToken);
            if (claim is null)
            {
                return AutoCloudGameFreeTimeResult.NoAccount;
            }
            AppConfig.SetCloudGameFreeTimeClaimedDate(biz, claim.Summary.AccountId, today);
            _logger.LogInformation("Auto cloud game free time claimed (biz {biz}, account {account}, date {date}, free time {minutes} min, limit reached {reached}).",
                biz, account.AccountId, today, claim.Summary.FreeMinutes, claim.ReachedLimit);
            SetSettingValue(failureKey, "0");
            return AutoCloudGameFreeTimeResult.Claimed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetSettingValue(failureKey, DateTimeOffset.UtcNow.Ticks.ToString());
            // -100 = 客户端给的凭证失效。自己恢复不了，得等用户重新打开云游戏客户端登录，要用长间隔，别白耗
            bool credentialExpired = ex is miHoYoApiException { ReturnCode: -100 };
            _logger.LogWarning(ex, "Auto cloud game free time request failed (biz {biz}, account {account}, credential expired {expired}).",
                biz, account.AccountId, credentialExpired);
            return credentialExpired ? AutoCloudGameFreeTimeResult.Blocked : AutoCloudGameFreeTimeResult.Failed;
        }
    }


    /// <summary>
    /// 该游戏、该账号记录失败时间的 Setting 键。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="accountId">通行证账号 ID。</param>
    /// <returns>Setting 表键名。</returns>
    private static string GetFailureKey(GameBiz biz, string accountId) => $"{FailureKeyPrefix}{biz}_{accountId}";


    /// <summary>
    /// 检查该游戏是否处于失败冷却期，避免短时间内重复请求。
    /// </summary>
    /// <param name="failureKey">Setting 表键名。</param>
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
    private static string? GetSettingValue(string key)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<string>("SELECT Value FROM Setting WHERE Key = @key LIMIT 1;", new { key });
    }


    /// <summary>写入 Setting 表键值（INSERT OR REPLACE）。</summary>
    private static void SetSettingValue(string key, string value)
    {
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("INSERT OR REPLACE INTO Setting (Key, Value) VALUES (@key, @value);", new { key, value });
    }

}


/// <summary>一轮批量对调度的聚合结果。优先级 Incomplete &gt; Blocked &gt; Completed。</summary>
internal enum AutoCloudGameFreeTimeRoundOutcome
{
    /// <summary>开启的游戏都已领，或没有开启的游戏。排下一个日界 + jitter。</summary>
    Completed,

    /// <summary>仍有游戏因断网、通用失败或冷却没领成。当天 30 分钟再试。</summary>
    Incomplete,

    /// <summary>仅剩客户端凭证失效。得用户重新登录云游戏客户端才能好，2 小时再试，避免连打。</summary>
    Blocked,
}


/// <summary>单个游戏对本轮聚合的贡献。</summary>
internal enum AutoCloudGameFreeTimeResult
{
    Cooldown,

    /// <summary>当日已领过，本轮跳过。</summary>
    AlreadyClaimed,

    Claimed,

    /// <summary>本机没有可用凭证（没装云游戏客户端或没登录过）：不算失败，也不必重试。</summary>
    NoAccount,

    Failed,

    Blocked,
}
