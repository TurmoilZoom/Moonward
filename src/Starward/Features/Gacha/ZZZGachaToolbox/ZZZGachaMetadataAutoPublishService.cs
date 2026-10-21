using Microsoft.Extensions.Logging;
using Octokit;
using Starward.Core;
using Starward.Core.Gacha.ZZZ;
using Starward.Features.GameRecord;
using Starward.Features.Startup;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha.ZZZGachaToolbox;

/// <summary>
/// 维护者的定时提交：每天过了指定的 UTC+8 时刻，用已登录的绝区零战绩账号获取抽卡物品元数据并提交到 metadata 分支。
/// 只在本机存有可写入 metadata 仓库的 GitHub PAT 时可开启（开启时校验，见 <see cref="ZZZGachaMetadataPublishService.HasWritePermissionAsync"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 优先级低于其他后台自动任务：启动时排在「签到 → 云游戏免费时长 → 战绩」这条链之后；
/// 到点时它们若在跑就先等，自己逐个语言拉取的间隙也会让路（见 <see cref="BackgroundAutomationActivity"/>）。
/// </para>
/// <para>
/// 失败按 1、5、15、30 分钟、之后每小时重试；PAT 权限、没有可用账号、风控这类重试也好不了的问题改为每 2 小时再试。
/// 重试都不越过下一次到点。某个区服的账号失效或风控时只跳过这个区服，其余照常提交并算本期完成；
/// 同期缓存在「一个包都没拿到」或提交 GitHub 失败重试时才派上用场，重试只补没拉到的。
/// 软件没在运行（含托盘驻留）时不会提交，下次启动后补上。
/// </para>
/// </remarks>
internal sealed class ZZZGachaMetadataAutoPublishService
{

    /// <summary>等启动自动任务链的上限：链上某一环卡住不能把定时提交一起挂死。</summary>
    private static readonly TimeSpan StartupChainWaitTimeout = TimeSpan.FromMinutes(20);

    /// <summary>启动链结束后再缓冲一段时间，避开启动高峰。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    /// <summary>其他自动任务结束后还要安静多久才开始。</summary>
    private static readonly TimeSpan IdleQuietPeriod = TimeSpan.FromSeconds(30);

    /// <summary>语言之间让路时要求的安静时间，比开跑前短：已经在跑了，别让一轮拖太久。</summary>
    private static readonly TimeSpan IdleQuietPeriodBetweenRequests = TimeSpan.FromSeconds(10);

    /// <summary>等其他自动任务的上限。</summary>
    private static readonly TimeSpan IdleMaxWait = TimeSpan.FromMinutes(30);

    /// <summary>PAT 权限、没有账号、风控等自己好不了的问题的重试间隔。</summary>
    private static readonly TimeSpan BlockedRetry = TimeSpan.FromHours(2);

    /// <summary>长等待拆段的上限。相对 Delay 不计休眠，分段醒来用墙上时钟重算剩余。</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(15);

    /// <summary>已拉到的语言包在重试时可复用的时长。</summary>
    private static readonly TimeSpan FetchCacheLifetime = TimeSpan.FromHours(6);

    /// <summary>后台拉取相邻两个语言之间的随机间隔（毫秒），比手动操作慢，降低 act 接口风控概率。</summary>
    private const int MinBackgroundRequestDelayMilliseconds = 1000;
    private const int MaxBackgroundRequestDelayMilliseconds = 3000;

    /// <summary>手动「立即执行」时的间隔，与工具箱窗口「从已登录的账号获取」一致。</summary>
    private const int MinManualRequestDelayMilliseconds = 200;
    private const int MaxManualRequestDelayMilliseconds = 300;


    private readonly ILogger<ZZZGachaMetadataAutoPublishService> _logger;

    private readonly ZZZGachaMetadataPublishService _publishService;

    private readonly GameRecordService _gameRecordService;

    /// <summary>保证常驻循环每个进程只启动一次。</summary>
    private int _residentStarted;

    /// <summary>设置改了（开关、时刻、PAT）：必须带此标志，只 Wake 时循环会按旧排期继续睡。</summary>
    private int _forceCheck;

    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>串行化定时运行与手动「立即执行」，避免同一期提交两次。</summary>
    private readonly SemaphoreSlim _runGate = new(1, 1);

    /// <summary>失败后的重试时刻（UTC ticks），0 表示没有待重试。只存在内存里，重启后按排期重新判断。</summary>
    private long _retryDueTicks;

    /// <summary>本期已拉到的语言包，只在持有 <see cref="_runGate"/> 时读写。</summary>
    private FetchCache? _fetchCache;


    /// <summary>
    /// 初始化定时提交服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="publishService">拉取与提交的实际实现。</param>
    /// <param name="gameRecordService">战绩门面，用于后台抑制极验弹窗与还原 HoYoLAB 语言。</param>
    public ZZZGachaMetadataAutoPublishService(ILogger<ZZZGachaMetadataAutoPublishService> logger, ZZZGachaMetadataPublishService publishService, GameRecordService gameRecordService)
    {
        _logger = logger;
        _publishService = publishService;
        _gameRecordService = gameRecordService;
    }


    /// <summary>是否已开启定时提交。</summary>
    public bool Enabled => AppConfig.ZZZGachaMetadataAutoPublishEnabled;


    /// <summary>每天的提交时刻（UTC+8）。</summary>
    public TimeSpan TimeOfDay => ZZZGachaMetadataPublishSchedule.FromMinutes(AppConfig.ZZZGachaMetadataAutoPublishMinutes);


    /// <summary>当前是否正在获取或提交（定时或手动）。</summary>
    public bool IsRunning => _runGate.CurrentCount == 0;


    /// <summary>
    /// 下次运行时刻：有待重试时为重试时刻，否则按排期（本期还没成功就是现在）。未开启或没有 PAT 时为 null。
    /// </summary>
    /// <returns>下次运行时刻（UTC）。</returns>
    public DateTimeOffset? GetNextDue()
    {
        if (!Enabled || !_publishService.HasStoredPat)
        {
            return null;
        }
        long retryTicks = Interlocked.Read(ref _retryDueTicks);
        if (retryTicks != 0)
        {
            return new DateTimeOffset(retryTicks, TimeSpan.Zero);
        }
        return ZZZGachaMetadataPublishSchedule.GetNextDue(DateTimeOffset.UtcNow, TimeOfDay, AppConfig.ZZZGachaMetadataAutoPublishLastSuccessPeriod);
    }


    /// <summary>
    /// 保存开关与时刻，并让常驻循环按新设置重新排期。调用方负责在开启前校验 PAT 写入权限。
    /// </summary>
    /// <param name="enabled">是否开启。</param>
    /// <param name="timeOfDay">每天的提交时刻（UTC+8，取到分钟）。</param>
    public void ApplySettings(bool enabled, TimeSpan timeOfDay)
    {
        AppConfig.ZZZGachaMetadataAutoPublishEnabled = enabled;
        AppConfig.ZZZGachaMetadataAutoPublishMinutes = (int)timeOfDay.TotalMinutes;
        _logger.LogInformation("ZZZ gacha metadata auto publish settings changed (enabled {enabled}, time {time} UTC+8).", enabled, timeOfDay);
        RequestImmediateCheck();
    }


    /// <summary>
    /// 设置或 PAT 有变化时调用：丢掉旧的重试排期，按当前设置重新算。
    /// </summary>
    public void RequestImmediateCheck()
    {
        Interlocked.Exchange(ref _forceCheck, 1);
        Wake();
    }


    /// <summary>
    /// 系统从休眠恢复。只唤醒等待去重算剩余时间，未到点不跑。
    /// </summary>
    public void NotifySystemResumed()
    {
        Wake();
    }


    /// <summary>
    /// 启动常驻循环：等启动自动任务链跑完再按排期运行。幂等。
    /// </summary>
    /// <param name="waitForStartupChain">启动自动任务链的收尾任务（战绩自动更新的启动检查）。</param>
    public void StartResident(Task waitForStartupChain)
    {
        ArgumentNullException.ThrowIfNull(waitForStartupChain);
        if (Interlocked.Exchange(ref _residentStarted, 1) == 1)
        {
            return;
        }
        _ = Task.Run(() => RunResidentLoopAsync(waitForStartupChain, CancellationToken.None));
    }


    /// <summary>
    /// 手动「立即执行一次」：不等其他自动任务，直接获取并提交。成功同样算本期已完成。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果；已有一次在跑时为 <see cref="ZZZGachaMetadataAutoPublishOutcome.Busy"/>。</returns>
    public async Task<ZZZGachaMetadataAutoPublishResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new ZZZGachaMetadataAutoPublishResult(ZZZGachaMetadataAutoPublishOutcome.Busy, Lang.ZZZGachaInfoWindow_AutoPublishBusy);
        }
        try
        {
            return await RunCoreAsync(background: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runGate.Release();
        }
    }


    /// <summary>
    /// 常驻循环：等启动链 → 缓冲 → 按排期等到点 → 等其他自动任务空闲 → 获取并提交 → 按结果排下一次。
    /// 单轮异常只影响本轮并按失败重试，不能让循环在本进程内静默死掉。
    /// </summary>
    private async Task RunResidentLoopAsync(Task waitForStartupChain, CancellationToken cancellationToken)
    {
        try
        {
            await WaitForStartupChainAsync(waitForStartupChain, cancellationToken).ConfigureAwait(false);
            await WaitOrWakeAsync(StartupDelay, cancellationToken).ConfigureAwait(false);
            int consecutiveFailures = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (await WaitUntilDueAsync(GetNextDue(), cancellationToken).ConfigureAwait(false))
                    {
                        Interlocked.Exchange(ref _retryDueTicks, 0);
                        consecutiveFailures = 0;
                        continue;
                    }
                    if (!Enabled || !_publishService.HasStoredPat)
                    {
                        continue;
                    }
                    if (!ZZZGachaMetadataPublishSchedule.IsDue(DateTimeOffset.UtcNow, TimeOfDay, AppConfig.ZZZGachaMetadataAutoPublishLastSuccessPeriod))
                    {
                        // 手动「立即执行」已经完成了本期
                        Interlocked.Exchange(ref _retryDueTicks, 0);
                        consecutiveFailures = 0;
                        continue;
                    }

                    if (await BackgroundAutomationActivity.WaitForIdleAsync(IdleQuietPeriod, IdleMaxWait, cancellationToken).ConfigureAwait(false))
                    {
                        _logger.LogInformation("ZZZ gacha metadata auto publish waited for other background automations.");
                    }

                    ZZZGachaMetadataAutoPublishResult result;
                    await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        result = await RunCoreAsync(background: true, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _runGate.Release();
                    }
                    consecutiveFailures = ScheduleRetry(result.Outcome, consecutiveFailures);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ZZZ gacha metadata auto publish round failed.");
                    consecutiveFailures = ScheduleRetry(ZZZGachaMetadataAutoPublishOutcome.Failed, consecutiveFailures);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZZZ gacha metadata auto publish resident loop failed.");
        }
    }


    /// <summary>
    /// 按本轮结果排重试：成功或无需运行时清掉重试，失败按退避，自己好不了的问题按 <see cref="BlockedRetry"/>。
    /// </summary>
    /// <param name="outcome">本轮结果。</param>
    /// <param name="consecutiveFailures">本轮之前的连续失败次数。</param>
    /// <returns>本轮之后的连续失败次数。</returns>
    private int ScheduleRetry(ZZZGachaMetadataAutoPublishOutcome outcome, int consecutiveFailures)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset? retry = null;
        if (outcome is ZZZGachaMetadataAutoPublishOutcome.Failed)
        {
            consecutiveFailures++;
            retry = ZZZGachaMetadataPublishSchedule.GetRetry(now, TimeOfDay, ZZZGachaMetadataPublishSchedule.GetRetryDelay(consecutiveFailures));
        }
        else if (outcome is ZZZGachaMetadataAutoPublishOutcome.Blocked)
        {
            consecutiveFailures = 0;
            retry = ZZZGachaMetadataPublishSchedule.GetRetry(now, TimeOfDay, BlockedRetry);
        }
        else
        {
            consecutiveFailures = 0;
        }
        Interlocked.Exchange(ref _retryDueTicks, retry?.UtcTicks ?? 0);
        _logger.LogInformation("ZZZ gacha metadata auto publish next due {due} (outcome {outcome}, consecutive failures {failures}).",
            GetNextDue(), outcome, consecutiveFailures);
        return consecutiveFailures;
    }


    /// <summary>
    /// 等启动自动任务链跑完；超时或门闩本身出错都不再等。
    /// </summary>
    private async Task WaitForStartupChainAsync(Task waitForStartupChain, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupChainWaitTimeout);
        try
        {
            await waitForStartupChain.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!waitForStartupChain.IsCompleted)
            {
                _logger.LogWarning("ZZZ gacha metadata auto publish: startup automations did not finish within {timeout}; continuing anyway.", StartupChainWaitTimeout);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ZZZ gacha metadata auto publish: waiting for startup automations failed; continuing anyway.");
        }
    }


    /// <summary>
    /// 等到 <paramref name="due"/>，或收到设置变化。<paramref name="due"/> 为 null（未开启）时只等设置变化。
    /// 分段 Delay，每次醒来用墙上时钟重算。
    /// </summary>
    /// <returns>因设置变化跳出时为 true。</returns>
    private async Task<bool> WaitUntilDueAsync(DateTimeOffset? due, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _forceCheck, 0) == 1)
            {
                return true;
            }
            if (due is not DateTimeOffset dueTime)
            {
                await WaitOrWakeAsync(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                continue;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now >= dueTime)
            {
                return false;
            }
            TimeSpan wait = dueTime - now;
            await WaitOrWakeAsync(wait > WatchdogInterval ? WatchdogInterval : wait, cancellationToken).ConfigureAwait(false);
        }
    }


    /// <summary>
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> 与唤醒信号竞速。任一方完成后取消另一半，避免 Delay 泄漏。
    /// </summary>
    /// <param name="wait">等待时长；<see cref="Timeout.InfiniteTimeSpan"/> 表示只等唤醒。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WaitOrWakeAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (wait != Timeout.InfiniteTimeSpan && wait <= TimeSpan.Zero)
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
    /// 跑一次获取 + 提交，并记下结果。调用方必须持有 <see cref="_runGate"/>。
    /// </summary>
    /// <param name="background">定时运行为 true：节奏放慢并给其他自动任务让路；手动「立即执行」为 false。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果。</returns>
    private async Task<ZZZGachaMetadataAutoPublishResult> RunCoreAsync(bool background, CancellationToken cancellationToken)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        TimeSpan timeOfDay = TimeOfDay;
        DateOnly period = ZZZGachaMetadataPublishSchedule.GetCurrentPeriod(start, timeOfDay);
        // 等闸门期间手动「立即执行」可能已经完成了本期
        if (background && !ZZZGachaMetadataPublishSchedule.IsDue(start, timeOfDay, AppConfig.ZZZGachaMetadataAutoPublishLastSuccessPeriod))
        {
            return new ZZZGachaMetadataAutoPublishResult(ZZZGachaMetadataAutoPublishOutcome.NotDue, "");
        }

        _logger.LogInformation("ZZZ gacha metadata auto publish started (period {period}, background {background}).", period, background);
        ZZZGachaMetadataAutoPublishResult result;
        try
        {
            result = await FetchAndPublishAsync(period, background, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = ToFailureResult(ex);
            _logger.LogWarning(ex, "ZZZ gacha metadata auto publish failed (outcome {outcome}).", result.Outcome);
        }

        if (result.Outcome is ZZZGachaMetadataAutoPublishOutcome.Published or ZZZGachaMetadataAutoPublishOutcome.Unchanged)
        {
            AppConfig.ZZZGachaMetadataAutoPublishLastSuccessPeriod = period;
            Interlocked.Exchange(ref _retryDueTicks, 0);
        }
        AppConfig.ZZZGachaMetadataAutoPublishLastRunTicks = DateTimeOffset.UtcNow.UtcTicks;
        AppConfig.ZZZGachaMetadataAutoPublishLastRunMessage = result.Message;
        _logger.LogInformation("ZZZ gacha metadata auto publish finished (outcome {outcome}, period {period}).", result.Outcome, period);
        return result;
    }


    /// <summary>
    /// 按计划拉取语言包（复用本期缓存）。某个区服失败时跳过该区服，只要还有拿到的包就提交，并算本期完成；
    /// 一个包都没拿到时不提交。
    /// 「没有 PAT」「没有带 Cookie 的绝区零角色」按 <see cref="ZZZGachaMetadataAutoPublishOutcome.Blocked"/> 返回，不抛异常。
    /// </summary>
    /// <param name="period">当前期，用于缓存归属。</param>
    /// <param name="background">定时运行为 true：节奏放慢并给其他自动任务让路。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>有包时为已提交或内容一致；一个包都没拿到时为失败或受阻。</returns>
    private async Task<ZZZGachaMetadataAutoPublishResult> FetchAndPublishAsync(DateOnly period, bool background, CancellationToken cancellationToken)
    {
        if (!_publishService.HasStoredPat)
        {
            return new ZZZGachaMetadataAutoPublishResult(ZZZGachaMetadataAutoPublishOutcome.Blocked, Lang.ZZZGachaInfoWindow_GitHubPatNotConfigured);
        }
        List<ZZZGachaMetadataFetchRequest> plan = _publishService.GetFetchPlan();
        if (plan.Count == 0)
        {
            return new ZZZGachaMetadataAutoPublishResult(ZZZGachaMetadataAutoPublishOutcome.Blocked, Lang.GachaLogPage_PleaseLoginMiyousheAndAddZZZRole);
        }

        FetchRoundResult fetched = await FetchAllAsync(plan, period, background, cancellationToken).ConfigureAwait(false);
        if (fetched.Packages.Count == 0)
        {
            if (fetched.Failures.Count > 0)
            {
                // 不提交；缓存留着，重试只补没拉到的
                return CombineFailureResults(fetched.Failures);
            }
            return new ZZZGachaMetadataAutoPublishResult(ZZZGachaMetadataAutoPublishOutcome.Failed, Lang.GachaLogPage_UpdateIconsNoItemData);
        }

        ZZZGachaMetadataPublishResult publish = await _publishService.PublishAsync(fetched.Packages, preserveExistingItems: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        _fetchCache = null;
        string message = publish.Unchanged
            ? string.Format(Lang.ZZZGachaInfoWindow_PublishUnchanged, publish.FileCount)
            : string.Format(Lang.ZZZGachaInfoWindow_PublishSucceeded, publish.FileCount, publish.CommitSha);
        if (fetched.Failures.Count > 0)
        {
            var reasons = new List<string>(fetched.Failures.Count);
            foreach (Exception ex in fetched.Failures)
            {
                reasons.Add(ZZZGachaMetadataPublishService.FormatFetchError(ex));
            }
            message += " " + string.Format(Lang.ZZZGachaInfoWindow_PartialFetchFailed, fetched.MissingCount, JoinDistinct(reasons));
            _logger.LogInformation("ZZZ gacha metadata auto publish finished with a partial fetch ({packageCount} package(s) fetched, {missingCount} request(s) missing).",
                fetched.Packages.Count, fetched.MissingCount);
        }
        return new ZZZGachaMetadataAutoPublishResult(
            publish.Unchanged ? ZZZGachaMetadataAutoPublishOutcome.Unchanged : ZZZGachaMetadataAutoPublishOutcome.Published,
            message);
    }


    /// <summary>
    /// 逐个请求拉取语言包。同一期内已完成的请求直接复用缓存。
    /// 某个请求失败时记下异常，并跳过同一区服剩下的请求；失败或被跳过的请求不记入已完成。
    /// </summary>
    /// <param name="plan">获取计划。</param>
    /// <param name="period">当前期，用于缓存归属。</param>
    /// <param name="background">定时运行为 true。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已拿到的语言包、本轮失败的异常，以及计划里仍未完成的请求数。</returns>
    /// <exception cref="OperationCanceledException">调用方取消。接口错误不再抛出。</exception>
    private async Task<FetchRoundResult> FetchAllAsync(List<ZZZGachaMetadataFetchRequest> plan, DateOnly period, bool background, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_fetchCache is not { } cache || cache.Period != period || now - cache.CreatedAt > FetchCacheLifetime)
        {
            cache = new FetchCache(period, now);
            _fetchCache = cache;
        }

        var failures = new List<Exception>();
        // 每个区服只选一个角色：Cookie 失效或风控时，剩下的语言注定还是失败，没必要继续打接口
        var failedBizKeys = new HashSet<string>(StringComparer.Ordinal);
        // HoYoLAB 的语言是 HoyolabClient 上的共享状态，逐个语言切过去后要还原，免得界面之后的国际服请求变成最后一种语言
        string originalLanguage = _gameRecordService.Language;
        bool requested = false;
        try
        {
            // 后台遇到 aigis 不弹极验，直接按风控记下并稍后重试
            using (_gameRecordService.SuppressInteractiveRiskChallenge())
            {
                foreach (ZZZGachaMetadataFetchRequest request in plan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (cache.CompletedRequests.Contains(request.Key))
                    {
                        continue;
                    }
                    if (failedBizKeys.Contains(request.BizKey))
                    {
                        continue;
                    }
                    if (requested)
                    {
                        await PaceAsync(background, cancellationToken).ConfigureAwait(false);
                    }
                    requested = true;
                    try
                    {
                        (string key, List<ZZZGachaInfo>? list) = await _publishService.FetchPackageAsync(request, cancellationToken).ConfigureAwait(false);
                        if (list is not null)
                        {
                            cache.Packages[key] = list;
                        }
                        cache.CompletedRequests.Add(request.Key);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Fetch ZZZ gacha metadata package failed ({key}); skip the rest of {biz} this round.", request.Key, request.BizKey);
                        failures.Add(ex);
                        failedBizKeys.Add(request.BizKey);
                    }
                }
            }
        }
        finally
        {
            _gameRecordService.Language = originalLanguage;
        }

        int missingCount = 0;
        foreach (ZZZGachaMetadataFetchRequest request in plan)
        {
            if (!cache.CompletedRequests.Contains(request.Key))
            {
                missingCount++;
            }
        }
        return new FetchRoundResult(new Dictionary<string, IReadOnlyList<ZZZGachaInfo>>(cache.Packages, StringComparer.Ordinal), failures, missingCount);
    }


    /// <summary>
    /// 相邻两个请求之间的间隔。定时运行时间隔更长，且有其他自动任务开始跑就先让它们跑完。
    /// </summary>
    private static async Task PaceAsync(bool background, CancellationToken cancellationToken)
    {
        if (!background)
        {
            await Task.Delay(Random.Shared.Next(MinManualRequestDelayMilliseconds, MaxManualRequestDelayMilliseconds + 1), cancellationToken).ConfigureAwait(false);
            return;
        }
        await Task.Delay(Random.Shared.Next(MinBackgroundRequestDelayMilliseconds, MaxBackgroundRequestDelayMilliseconds + 1), cancellationToken).ConfigureAwait(false);
        await BackgroundAutomationActivity.WaitForIdleAsync(IdleQuietPeriodBetweenRequests, IdleMaxWait, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// 把异常归类：重试有望恢复的（断网、限流、分支被别人抢先更新等）为 Failed，
    /// PAT 失效或无权限、登录失效、风控为 Blocked。
    /// </summary>
    /// <param name="ex">获取或提交时抛出的异常。</param>
    /// <returns>结果与展示文案。</returns>
    private static ZZZGachaMetadataAutoPublishResult ToFailureResult(Exception ex)
    {
        return ex switch
        {
            miHoYoApiException api when ZZZGachaMetadataPublishService.IsCultivateToolRiskControl(api.ReturnCode)
                => new(ZZZGachaMetadataAutoPublishOutcome.Blocked, ZZZGachaMetadataPublishService.FormatFetchError(ex)),
            miHoYoApiException api
                => new(api.IsLoginExpired ? ZZZGachaMetadataAutoPublishOutcome.Blocked : ZZZGachaMetadataAutoPublishOutcome.Failed, ZZZGachaMetadataPublishService.FormatFetchError(ex)),
            // 限流是 403 的子类，要排在 ForbiddenException 前面；它等一等就会恢复
            RateLimitExceededException or SecondaryRateLimitExceededException
                => new(ZZZGachaMetadataAutoPublishOutcome.Failed, string.Format(Lang.ZZZGachaInfoWindow_PublishFailed, ex.Message)),
            AuthorizationException or ForbiddenException or NotFoundException
                => new(ZZZGachaMetadataAutoPublishOutcome.Blocked, string.Format(Lang.ZZZGachaInfoWindow_PublishUnauthorized, ex.Message)),
            _ => new(ZZZGachaMetadataAutoPublishOutcome.Failed, string.Format(Lang.ZZZGachaInfoWindow_PublishFailed, ex.Message)),
        };
    }


    /// <summary>
    /// 一个包都没拿到时，把本轮各次失败合成一个结果。
    /// </summary>
    /// <param name="failures">本轮实际抛出的异常。</param>
    /// <returns>任一为 <see cref="ZZZGachaMetadataAutoPublishOutcome.Failed"/> 时整体按它返回（重试更快），否则为受阻；文案去重后用分号连接。</returns>
    private static ZZZGachaMetadataAutoPublishResult CombineFailureResults(IReadOnlyList<Exception> failures)
    {
        var outcome = ZZZGachaMetadataAutoPublishOutcome.Blocked;
        var messages = new List<string>(failures.Count);
        foreach (Exception ex in failures)
        {
            ZZZGachaMetadataAutoPublishResult result = ToFailureResult(ex);
            // 只要有一次按退避能恢复，整体就按 Failed，重试比受阻更快
            if (result.Outcome == ZZZGachaMetadataAutoPublishOutcome.Failed)
            {
                outcome = ZZZGachaMetadataAutoPublishOutcome.Failed;
            }
            messages.Add(result.Message);
        }
        return new ZZZGachaMetadataAutoPublishResult(outcome, JoinDistinct(messages));
    }


    /// <summary>
    /// 按首次出现的顺序去重，再用 <c>; </c> 连成一句。
    /// </summary>
    /// <param name="messages">各次失败的说明。</param>
    /// <returns>去重后的说明。</returns>
    private static string JoinDistinct(IEnumerable<string> messages)
    {
        var distinct = new List<string>();
        foreach (string message in messages)
        {
            if (!distinct.Contains(message))
            {
                distinct.Add(message);
            }
        }
        return string.Join("; ", distinct);
    }


    /// <summary>
    /// 一轮拉取的结果。
    /// </summary>
    /// <param name="Packages">语言包键 → 物品列表（含本期缓存里已经拉到的）。</param>
    /// <param name="Failures">本轮实际失败的异常；同一区服只记第一次。</param>
    /// <param name="MissingCount">计划里本轮结束后仍未完成的请求数（失败的和被跳过的）。</param>
    private sealed record FetchRoundResult(Dictionary<string, IReadOnlyList<ZZZGachaInfo>> Packages, IReadOnlyList<Exception> Failures, int MissingCount);


    /// <summary>
    /// 一期内已拉到的语言包。
    /// </summary>
    /// <param name="Period">所属的期。</param>
    /// <param name="CreatedAt">开始缓存的时刻。</param>
    private sealed record FetchCache(DateOnly Period, DateTimeOffset CreatedAt)
    {
        /// <summary>语言包键 → 物品列表。</summary>
        public Dictionary<string, IReadOnlyList<ZZZGachaInfo>> Packages { get; } = new(StringComparer.Ordinal);

        /// <summary>已完成的请求键（含接口没有物品的语言），重试时跳过。</summary>
        public HashSet<string> CompletedRequests { get; } = new(StringComparer.Ordinal);
    }

}


/// <summary>定时提交一次运行的结果。</summary>
internal enum ZZZGachaMetadataAutoPublishOutcome
{
    /// <summary>已提交新的 commit。</summary>
    Published,

    /// <summary>内容与分支一致，没有提交。同样算本期完成。</summary>
    Unchanged,

    /// <summary>断网、限流、接口临时错误等，按退避重试。</summary>
    Failed,

    /// <summary>PAT 失效或无权限、没有可用账号、登录失效、风控，2 小时后再试。</summary>
    Blocked,

    /// <summary>手动执行时已有一次在跑。</summary>
    Busy,

    /// <summary>本期已经成功过（手动执行抢先完成），这次不用跑。</summary>
    NotDue,
}


/// <summary>
/// 定时提交一次运行的结果与展示文案。
/// </summary>
/// <param name="Outcome">结果分类。</param>
/// <param name="Message">已本地化的说明，给工具箱窗口状态栏用。</param>
internal sealed record ZZZGachaMetadataAutoPublishResult(ZZZGachaMetadataAutoPublishOutcome Outcome, string Message);
