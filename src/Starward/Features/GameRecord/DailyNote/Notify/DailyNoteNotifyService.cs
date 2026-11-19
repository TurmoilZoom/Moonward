using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Features.Notification;
using Starward.Features.Overlay;
using Starward.Features.Startup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord.DailyNote.Notify;

/// <summary>
/// 实时便笺提醒（常驻）：按账号配置的规则检查便笺，达到阈值时发 Windows 系统通知。
/// <para>
/// 不按固定频率狂拉接口：平时按检查间隔（默认 60 分钟），有项目预计更早到点时，用接口给的恢复时间推算到点时刻，
/// 到点后再拉一次核对（玩家可能已经在游戏里花掉了）。首页便笺刷新拿到的数据也交给这里（<see cref="ReportSnapshot"/>），不额外请求。
/// </para>
/// <para>
/// 去重是边沿触发：达到阈值提醒一次，掉回阈值以下才重新生效；「已提醒」记在 Setting 表，重启软件不会重复弹。
/// </para>
/// </summary>
/// <remarks>
/// 调度结构与 <see cref="SignIn.AutoSignInService"/>、<see cref="CloudGame.AutoCloudGameFreeTimeService"/> 一致：
/// 绝对到期时刻 + 分段等待（休眠后用墙上时钟重算）+ 唤醒信号。启动后排在自动签到首轮之后。
/// </remarks>
internal sealed class DailyNoteNotifyService
{

    /// <summary>签到首轮结束后再缓冲一段时间才开始检查，避开启动高峰。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    /// <summary>等待自动签到首轮的上限。签到卡住不能把提醒一起挂死。</summary>
    private static readonly TimeSpan StartupSignInWaitTimeout = TimeSpan.FromMinutes(15);

    /// <summary>长等待拆段的上限。相对 Delay 不计休眠，分段醒来用墙上时钟重算剩余。</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(15);

    /// <summary>没有开启任何提醒时多久重扫一次配置；用户改配置会立即唤醒，这只是兜底。</summary>
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(30);

    /// <summary>推算到点后再等多久去核对，吸收线性折算的误差。</summary>
    private static readonly TimeSpan ConfirmDelay = TimeSpan.FromMinutes(1);

    /// <summary>同一账号两次检查的最小间隔。</summary>
    private static readonly TimeSpan MinCheckGap = TimeSpan.FromMinutes(3);

    /// <summary>首页刚刷新过便笺时，改配置直接用那份数据判定，不再请求。</summary>
    private static readonly TimeSpan SnapshotReuseWindow = TimeSpan.FromMinutes(5);

    /// <summary>请求失败（断网等）后的重试间隔。</summary>
    private static readonly TimeSpan FailureRetry = TimeSpan.FromMinutes(30);

    /// <summary>登录失效 / 风控：拉长间隔，避免连打；后台解不了验证，等用户在首页处理。</summary>
    private static readonly TimeSpan BlockedRetry = TimeSpan.FromHours(2);

    /// <summary>相邻两个账号的请求之间的随机间隔（秒）。</summary>
    private const int MinRequestDelaySeconds = 3;
    private const int MaxRequestDelaySeconds = 8;

    private const int MinIntervalMinutes = 15;
    private const int MaxIntervalMinutes = 240;


    private readonly ILogger<DailyNoteNotifyService> _logger;

    private readonly GameRecordService _gameRecordService;

    /// <summary>保护 <see cref="_nextCheck"/>、<see cref="_lastSnapshots"/> 与提醒状态的读改写。</summary>
    private readonly object _lock = new();

    /// <summary>各账号的下次检查时刻；没有记录的账号视为立即到期。</summary>
    private readonly Dictionary<(string Biz, long Uid), DateTimeOffset> _nextCheck = [];

    /// <summary>各账号最近一份便笺及其获取时刻。</summary>
    private readonly Dictionary<(string Biz, long Uid), (object Note, DateTimeOffset FetchedAt)> _lastSnapshots = [];

    private int _residentStarted;

    private readonly SemaphoreSlim _wake = new(0, 1);


    /// <summary>
    /// 初始化便笺提醒服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="gameRecordService">战绩服务，用于拉取便笺。</param>
    public DailyNoteNotifyService(ILogger<DailyNoteNotifyService> logger, GameRecordService gameRecordService)
    {
        _logger = logger;
        _gameRecordService = gameRecordService;
    }


    /// <summary>
    /// 启动常驻检查循环。幂等。
    /// </summary>
    /// <param name="waitForSignInStartupBatch">自动签到首轮完成任务；检查排在它之后，不与签到抢同一时刻的请求。</param>
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
    /// 用户改了某账号的提醒配置。首页刚刷新过便笺就直接用那份数据判定，否则让该账号立即到期。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    public void NotifyConfigChanged(GameRecordRole role)
    {
        var key = GetKey(role);
        object? note = null;
        DateTimeOffset fetchedAt = default;
        lock (_lock)
        {
            // 没有新鲜快照时不会立即判定，关掉的项目要在这里就清掉已提醒标记
            PruneState(role, DailyNoteNotifyConfigStore.Load(role.GameBiz, role.Uid));
            if (_lastSnapshots.TryGetValue(key, out var snapshot) && DateTimeOffset.UtcNow - snapshot.FetchedAt < SnapshotReuseWindow)
            {
                (note, fetchedAt) = snapshot;
            }
            else
            {
                _nextCheck.Remove(key);
            }
        }
        if (note is not null)
        {
            Process(role, note, fetchedAt, keepEarlierCheck: false);
        }
        Wake();
    }


    /// <summary>
    /// 首页便笺刷新成功：用这份数据判定，并在推算到点更早时提前下次检查。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    /// <param name="note">某游戏的便笺（<see cref="Core.GameRecord.Genshin.DailyNote.GenshinDailyNote"/> 等）。</param>
    public void ReportSnapshot(GameRecordRole role, object? note)
    {
        if (note is null)
        {
            return;
        }
        try
        {
            // 首页可能拿的是 5 分钟内的缓存，不能据此推迟已排好的检查，只能提前
            Process(role, note, DateTimeOffset.UtcNow, keepEarlierCheck: true);
            Wake();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Daily note notify: process snapshot failed ({biz}, {uid}).", role.GameBiz, role.Uid);
        }
    }


    /// <summary>
    /// 最近一次拿到的该账号便笺（后台检查或首页刷新）。提醒设置里给不在首页显示的账号取阈值上限用。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    /// <returns>便笺对象；本次运行还没拿到过时为 null。</returns>
    public object? GetLastSnapshot(GameRecordRole role)
    {
        lock (_lock)
        {
            return _lastSnapshots.TryGetValue(GetKey(role), out var snapshot) ? snapshot.Note : null;
        }
    }


    /// <summary>
    /// 系统从休眠恢复：唤醒等待，按墙上时钟重算哪些账号已到期。
    /// </summary>
    public void NotifySystemResumed()
    {
        Wake();
    }


    /// <summary>
    /// 常驻循环：等签到首轮 → 启动缓冲 → 检查到期账号 → 睡到最早的下次检查。单轮异常只影响本轮。
    /// </summary>
    private async Task RunResidentLoopAsync(Task waitForSignInStartupBatch, CancellationToken cancellationToken)
    {
        try
        {
            await WaitForSignInStartupBatchAsync(waitForSignInStartupBatch, cancellationToken);
            await WaitOrWakeAsync(StartupDelay, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset nextWake;
                try
                {
                    nextWake = await RunDueChecksAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Daily note notify round failed.");
                    nextWake = DateTimeOffset.UtcNow + FailureRetry;
                }
                await WaitUntilAsync(nextWake, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily note notify resident loop failed.");
        }
    }


    /// <summary>
    /// 等自动签到首轮打完；超时或门闩本身出错都不再等。
    /// </summary>
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
            _logger.LogWarning("Daily note notify: auto sign-in startup batch did not finish within {timeout}; starting anyway.", StartupSignInWaitTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Daily note notify: waiting for auto sign-in startup batch failed; starting anyway.");
        }
    }


    /// <summary>
    /// 检查所有到期账号（请求之间随机间隔），返回下次该醒来的时刻。
    /// </summary>
    /// <returns>最早的下次检查时刻；没有开启提醒的账号时为「现在 + 空闲重扫间隔」。</returns>
    private async Task<DateTimeOffset> RunDueChecksAsync(CancellationToken cancellationToken)
    {
        List<GameRecordRole> roles = GetActiveRoles();
        var activeKeys = roles.Select(GetKey).ToHashSet();
        List<GameRecordRole> due;
        lock (_lock)
        {
            // 关掉提醒或删掉账号后丢弃排期，再打开时立即检查
            foreach (var key in _nextCheck.Keys.Where(x => !activeKeys.Contains(x)).ToList())
            {
                _nextCheck.Remove(key);
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            due = roles.Where(r => !_nextCheck.TryGetValue(GetKey(r), out DateTimeOffset next) || next <= now).ToList();
        }

        bool paced = false;
        foreach (GameRecordRole role in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (paced)
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(MinRequestDelaySeconds, MaxRequestDelaySeconds + 1)), cancellationToken);
            }
            paced = true;
            await CheckRoleAsync(role, cancellationToken);
        }

        lock (_lock)
        {
            DateTimeOffset nextWake = DateTimeOffset.UtcNow + IdleInterval;
            foreach (var key in activeKeys)
            {
                if (_nextCheck.TryGetValue(key, out DateTimeOffset next) && next < nextWake)
                {
                    nextWake = next;
                }
            }
            return nextWake;
        }
    }


    /// <summary>
    /// 开了至少一条提醒、且所属游戏支持实时便笺的战绩角色。
    /// </summary>
    private List<GameRecordRole> GetActiveRoles()
    {
        return _gameRecordService.GetAllGameRoles()
            .Where(r => !string.IsNullOrWhiteSpace(r.GameBiz) && GameFeatureConfig.FromGameBiz(r.GameBiz).SupportDailyNote)
            .Where(r => DailyNoteNotifyConfigStore.Load(r.GameBiz, r.Uid).HasEnabledRule)
            .ToList();
    }


    /// <summary>
    /// 拉一次便笺并判定；失败时按错误类型排重试。
    /// </summary>
    private async Task CheckRoleAsync(GameRecordRole role, CancellationToken cancellationToken)
    {
        try
        {
            object note = await FetchDailyNoteAsync(role, cancellationToken);
            Process(role, note, DateTimeOffset.UtcNow, keepEarlierCheck: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            bool blocked = false;
            if (ex is miHoYoApiException or HttpRequestException)
            {
                // 登录失效、需要验证账号这类后台解不了的，拉长间隔；断网等按普通失败重试
                blocked = MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GameRecord).RecoveryAction is not MiHoYoApiRecoveryAction.None;
            }
            TimeSpan retry = blocked ? BlockedRetry : FailureRetry;
            _logger.LogWarning(ex, "Daily note notify: fetch failed ({biz}, {uid}), retry in {retry}.", role.GameBiz, role.Uid, retry);
            lock (_lock)
            {
                _nextCheck[GetKey(role)] = DateTimeOffset.UtcNow + retry;
            }
        }
    }


    /// <summary>
    /// 后台检查用：强制拉一次最新便笺，并登记为后台自动任务。
    /// </summary>
    private async Task<object> FetchDailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken)
    {
        using (BackgroundAutomationActivity.Enter())
        {
            return await GetDailyNoteAsync(role, forceUpdate: true, cancellationToken);
        }
    }


    /// <summary>
    /// 拉取角色所属游戏的便笺。不弹人机验证（调用方是后台或对话框，弹出来既打扰又多半解不开），
    /// 按角色自身区服选客户端，不碰共享的 <see cref="GameRecordService.IsHoyolab"/>。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    /// <param name="forceUpdate">为 false 时 5 分钟内复用战绩服务的缓存。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该角色所属游戏的便笺对象。</returns>
    /// <exception cref="NotSupportedException">角色所属游戏没有实时便笺。</exception>
    public async Task<object> GetDailyNoteAsync(GameRecordRole role, bool forceUpdate, CancellationToken cancellationToken = default)
    {
        using (_gameRecordService.SuppressInteractiveRiskChallenge())
        {
            GameBiz biz = role.GameBiz;
            if (!biz.IsGlobalServer())
            {
                await _gameRecordService.EnsureHyperionDeviceFpAsync(false, cancellationToken);
            }
            return biz.Game switch
            {
                GameBiz.hk4e => await _gameRecordService.GetGenshinDailyNoteAsync(role, forceUpdate, cancellationToken),
                GameBiz.hkrpg => await _gameRecordService.GetStarRailDailyNoteAsync(role, forceUpdate, cancellationToken),
                GameBiz.nap => await _gameRecordService.GetZZZDailyNoteAsync(role, forceUpdate, cancellationToken),
                GameBiz.bh3 => await _gameRecordService.GetBH3DailyNoteAsync(role, forceUpdate, cancellationToken),
                _ => throw new NotSupportedException($"Daily note is not supported for {role.GameBiz}."),
            };
        }
    }


    /// <summary>
    /// 用一份便笺判定该账号的全部规则：更新已提醒状态、排下次检查、需要时发通知。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    /// <param name="note">该角色所属游戏的便笺。</param>
    /// <param name="fetchedAt">便笺的获取时刻。</param>
    /// <param name="keepEarlierCheck">为 true 时只允许把下次检查提前，不推迟已排好的。</param>
    private void Process(GameRecordRole role, object note, DateTimeOffset fetchedAt, bool keepEarlierCheck)
    {
        var key = GetKey(role);
        DailyNoteNotifyConfig config = DailyNoteNotifyConfigStore.Load(role.GameBiz, role.Uid);
        var fired = new List<(DailyNoteNotifyItem Item, DailyNoteMetric Metric, DailyNoteNotifyDecision Decision)>();
        lock (_lock)
        {
            _lastSnapshots[key] = (note, fetchedAt);
            if (!config.HasEnabledRule)
            {
                PruneState(role, config);
                _nextCheck.Remove(key);
                return;
            }

            DailyNoteNotifyState state = DailyNoteNotifyConfigStore.LoadState(role.GameBiz, role.Uid);
            var notified = new HashSet<DailyNoteNotifyItem>(state.Notified);
            var etas = new List<TimeSpan>();
            var enabledItems = new HashSet<DailyNoteNotifyItem>();
            foreach (DailyNoteNotifyRule rule in config.Rules.Where(x => x.Enabled))
            {
                if (DailyNoteNotifyItems.GetMetric(rule.Item, note) is not DailyNoteMetric metric)
                {
                    continue;
                }
                enabledItems.Add(rule.Item);
                DailyNoteNotifyDecision decision = DailyNoteNotifyEvaluator.Evaluate(metric, rule.Threshold, notified.Contains(rule.Item));
                if (decision.ShouldNotify)
                {
                    fired.Add((rule.Item, metric, decision));
                }
                if (decision.Reached)
                {
                    notified.Add(rule.Item);
                }
                else
                {
                    notified.Remove(rule.Item);
                }
                if (decision.TimeToThreshold is TimeSpan eta)
                {
                    etas.Add(eta);
                }
            }
            // 关掉的项目清掉已提醒标记：重新打开时若已达到，就该提醒一次让用户知道开关生效了
            notified.IntersectWith(enabledItems);
            if (!notified.SetEquals(state.Notified))
            {
                state.Notified = [.. notified];
                DailyNoteNotifyConfigStore.SaveState(role.GameBiz, role.Uid, state);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset next = DailyNoteNotifyEvaluator.GetNextCheck(fetchedAt, now, GetInterval(), etas, ConfirmDelay, MinCheckGap);
            if (keepEarlierCheck && _nextCheck.TryGetValue(key, out DateTimeOffset scheduled) && scheduled < next)
            {
                next = scheduled;
            }
            _nextCheck[key] = next;
            _logger.LogInformation("Daily note notify: checked ({biz}, {uid}), fired {count}, next check {next}.", role.GameBiz, role.Uid, fired.Count, next);
        }
        if (fired.Count > 0)
        {
            ShowNotification(role, fired);
        }
    }


    /// <summary>
    /// 清掉已关闭项目的已提醒标记：重新打开时若已达到，就该提醒一次让用户知道开关生效了。调用方持有 <see cref="_lock"/>。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    /// <param name="config">该账号当前的提醒配置。</param>
    private static void PruneState(GameRecordRole role, DailyNoteNotifyConfig config)
    {
        DailyNoteNotifyState state = DailyNoteNotifyConfigStore.LoadState(role.GameBiz, role.Uid);
        int removed = state.Notified.RemoveAll(item => !config.Rules.Any(rule => rule.Item == item && rule.Enabled));
        if (removed > 0)
        {
            DailyNoteNotifyConfigStore.SaveState(role.GameBiz, role.Uid, state);
        }
    }


    /// <summary>
    /// 发一条通知，列出这次同时达到阈值的项目。
    /// </summary>
    private static void ShowNotification(GameRecordRole role, List<(DailyNoteNotifyItem Item, DailyNoteMetric Metric, DailyNoteNotifyDecision Decision)> fired)
    {
        GameBiz biz = role.GameBiz;
        string gameName = biz.ToGameName();
        string title = string.IsNullOrWhiteSpace(role.Nickname) ? gameName : $"{gameName} · {role.Nickname}";
        SystemNotificationService.Show(new SystemNotification
        {
            Title = title,
            Lines = fired.Select(x => FormatLine(x.Item, x.Metric, x.Decision)).ToList(),
            Attribution = $"UID {role.Uid}",
            LaunchUri = NotificationActivation.BuildOpenUri(biz, role.Uid),
            // 同一项目再次达到时替换掉通知中心里没看的旧通知
            Tag = "dn_" + string.Join("_", fired.Select(x => (int)x.Item)),
            Group = $"{role.GameBiz}_{role.Uid}",
            SuppressPopup = AppConfig.DailyNoteNotifySilentWhenGameRunning && RunningGameService.GetRunningGameCount() > 0,
        });
    }


    /// <summary>
    /// 一个项目的通知文案。
    /// </summary>
    private static string FormatLine(DailyNoteNotifyItem item, DailyNoteMetric metric, DailyNoteNotifyDecision decision)
    {
        string name = GetDisplayName(item);
        if (decision.Threshold >= metric.Max)
        {
            return string.Format(Lang.DailyNoteNotify_ItemFull, name, metric.Current, metric.Max);
        }
        return string.Format(Lang.DailyNoteNotify_ItemReached, name, metric.Current, metric.Max);
    }


    /// <summary>
    /// 便笺项目的显示名。
    /// </summary>
    /// <param name="item">便笺项目。</param>
    /// <returns>当前语言下的名称。</returns>
    public static string GetDisplayName(DailyNoteNotifyItem item) => item switch
    {
        DailyNoteNotifyItem.GenshinResin => Lang.DailyNoteNotify_GenshinResin,
        DailyNoteNotifyItem.GenshinHomeCoin => Lang.DailyNoteButton_Genshin_RealmCurrency,
        DailyNoteNotifyItem.StarRailStamina => Lang.DailyNoteNotify_StarRailStamina,
        DailyNoteNotifyItem.ZZZEnergy => Lang.DailyNoteButton_ZZZ_BatteryCharge,
        DailyNoteNotifyItem.BH3Stamina => Lang.DailyNoteNotify_BH3Stamina,
        _ => item.ToString(),
    };


    /// <summary>检查间隔，限制在合理范围内。</summary>
    private static TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(Math.Clamp(AppConfig.DailyNoteNotifyIntervalMinutes, MinIntervalMinutes, MaxIntervalMinutes));
    }


    private static (string Biz, long Uid) GetKey(GameRecordRole role) => (role.GameBiz, role.Uid);


    /// <summary>
    /// 等到 <paramref name="due"/> 或被唤醒。分段等待，每次醒来用墙上时钟重算；被唤醒就返回，由循环重新判断到期账号。
    /// </summary>
    private async Task WaitUntilAsync(DateTimeOffset due, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan wait = due - DateTimeOffset.UtcNow;
            if (wait <= TimeSpan.Zero)
            {
                return;
            }
            if (wait > WatchdogInterval)
            {
                wait = WatchdogInterval;
            }
            if (await WaitOrWakeAsync(wait, cancellationToken))
            {
                return;
            }
        }
    }


    /// <summary>
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> 与唤醒信号竞速。任一方完成后取消另一半，避免 Delay 泄漏。
    /// </summary>
    /// <returns>被唤醒信号打断时为 true。</returns>
    private async Task<bool> WaitOrWakeAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (wait <= TimeSpan.Zero)
        {
            return false;
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
        return completed == wakeTask;
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

}
