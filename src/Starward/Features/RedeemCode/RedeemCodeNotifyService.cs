using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.MiyoLive;
using Starward.Features.Notification;
using Starward.Features.Overlay;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.RedeemCode;

/// <summary>
/// 前瞻直播兑换码发布通知（常驻）：按游戏设置通知方式（<see cref="RedeemCodeNotifyMode"/>），兑换码放出时发 Windows 系统通知，带「复制」按钮。
/// 码在直播中分几次放出：逐条模式每出一个发一条；合并模式等放够预设数量（<see cref="MiyoLiveGameConfig.ExpectedCodeCount"/>）再合成一条，每期一条。
/// <para>
/// 不按固定频率狂拉接口：平时每 <see cref="RedeemCodeNotifySchedule.DiscoveryInterval"/> 查一次有没有新一期直播；
/// 找到后按 index 给的开播时间睡到开播，开播到结束后半小时内每分钟查一次兑换码（码按 <c>to_get_time</c> 逐个放出，事先不知道时刻）。
/// </para>
/// <para>
/// 去重按 act_id 记在 Setting 表（<c>redeem_code_notified_{game}</c>），重启不重复弹。直播时没开 Moonward 的，
/// 结束 <see cref="RedeemCodeNotifySchedule.NotifyFreshWindow"/> 内启动时补发；更早的往期码不再通知。
/// </para>
/// </summary>
/// <remarks>
/// 调度结构与 <see cref="GameRecord.DailyNote.Notify.DailyNoteNotifyService"/> 一致：绝对到期时刻 + 分段等待（休眠后用墙上时钟重算）+ 唤醒信号。
/// 接口公开、不带 Cookie，没有风控顾虑；仍排在自动签到首轮之后，不与启动高峰抢请求。
/// </remarks>
internal sealed class RedeemCodeNotifyService
{

    /// <summary>支持兑换码通知的游戏（见 <see cref="MiyoLiveActivityConfig"/>），按游戏开关。</summary>
    public static IReadOnlyList<string> SupportedGames { get; } = [GameBiz.hk4e, GameBiz.hkrpg, GameBiz.nap, GameBiz.bh3];

    /// <summary>签到首轮结束后再缓冲一段时间才开始检查，避开启动高峰。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    /// <summary>等待自动签到首轮的上限。签到卡住不能把通知一起挂死。</summary>
    private static readonly TimeSpan StartupSignInWaitTimeout = TimeSpan.FromMinutes(15);

    /// <summary>长等待拆段的上限。相对 Delay 不计休眠，分段醒来用墙上时钟重算剩余。</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(15);

    /// <summary>没有开启任何游戏时多久重扫一次配置；用户改开关会立即唤醒，这只是兜底。</summary>
    private static readonly TimeSpan IdleInterval = TimeSpan.FromHours(1);

    /// <summary>不在直播期间请求失败（断网等）后的重试间隔；直播期间照常每分钟重试。</summary>
    private static readonly TimeSpan FailureRetry = TimeSpan.FromMinutes(15);

    /// <summary>已通知记录的 Setting 键前缀，值见 <see cref="RedeemCodeNotifySchedule.SerializeNotified"/>。</summary>
    private const string NotifiedKeyPrefix = "redeem_code_notified_";

    /// <summary>通知分组。</summary>
    private const string NotificationGroup = "redeem";

    /// <summary>「复制」按钮最多几个（Toast 上限 5 个，一期直播一般 1~3 个码）。</summary>
    private const int MaxCopyButtons = 3;

    /// <summary>Toast 的 Tag 最长 64 字符。</summary>
    private const int MaxTagLength = 64;


    private readonly ILogger<RedeemCodeNotifyService> _logger;

    private readonly RedeemCodeService _redeemCodeService;

    /// <summary>保护 <see cref="_nextCheck"/> 与 <see cref="_lastPhase"/>。</summary>
    private readonly object _lock = new();

    /// <summary>各游戏的下次检查时刻；没有记录的游戏视为立即到期。</summary>
    private readonly Dictionary<string, DateTimeOffset> _nextCheck = [];

    /// <summary>各游戏上次检查时所处的阶段，用于失败重试间隔与日志去重。</summary>
    private readonly Dictionary<string, RedeemLivePhase> _lastPhase = [];

    private int _residentStarted;

    private readonly SemaphoreSlim _wake = new(0, 1);


    /// <summary>
    /// 初始化兑换码通知服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="redeemCodeService">兑换码业务，与首页弹层共用快照缓存。</param>
    public RedeemCodeNotifyService(ILogger<RedeemCodeNotifyService> logger, RedeemCodeService redeemCodeService)
    {
        _logger = logger;
        _redeemCodeService = redeemCodeService;
    }


    /// <summary>
    /// 启动常驻检查循环。幂等。
    /// </summary>
    /// <param name="waitForSignInStartupBatch">自动签到首轮完成任务；检查排在它之后。</param>
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
    /// 用户改了某游戏的通知方式：让它立即到期并唤醒循环。打开时若本期已有码且结束不久，会按新方式马上判定一次。
    /// </summary>
    /// <param name="game">游戏，如 hk4e。</param>
    public void NotifyConfigChanged(string game)
    {
        lock (_lock)
        {
            _nextCheck.Remove(game);
        }
        Wake();
    }


    /// <summary>
    /// 系统从休眠恢复：唤醒等待，按墙上时钟重算哪些游戏已到期。
    /// </summary>
    public void NotifySystemResumed()
    {
        Wake();
    }


    /// <summary>
    /// 常驻循环：等签到首轮 → 启动缓冲 → 检查到期游戏 → 睡到最早的下次检查。单轮异常只影响本轮。
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
                    _logger.LogError(ex, "Redeem code notify round failed.");
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
            _logger.LogError(ex, "Redeem code notify resident loop failed.");
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
            _logger.LogWarning("Redeem code notify: auto sign-in startup batch did not finish within {timeout}; starting anyway.", StartupSignInWaitTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redeem code notify: waiting for auto sign-in startup batch failed; starting anyway.");
        }
    }


    /// <summary>
    /// 检查所有到期的游戏，返回下次该醒来的时刻。
    /// </summary>
    /// <returns>最早的下次检查时刻；没有开启任何游戏时为「现在 + 空闲重扫间隔」。</returns>
    private async Task<DateTimeOffset> RunDueChecksAsync(CancellationToken cancellationToken)
    {
        List<string> enabled = SupportedGames.Where(g => AppConfig.GetRedeemCodeNotifyMode(g) is not RedeemCodeNotifyMode.Off).ToList();
        List<string> due;
        lock (_lock)
        {
            // 关掉的游戏丢弃排期，再打开时立即检查
            foreach (string game in _nextCheck.Keys.Where(x => !enabled.Contains(x)).ToList())
            {
                _nextCheck.Remove(game);
                _lastPhase.Remove(game);
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            due = enabled.Where(g => !_nextCheck.TryGetValue(g, out DateTimeOffset next) || next <= now).ToList();
        }

        foreach (string game in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CheckGameAsync(game, cancellationToken);
        }

        lock (_lock)
        {
            DateTimeOffset nextWake = DateTimeOffset.UtcNow + IdleInterval;
            foreach (string game in enabled)
            {
                if (_nextCheck.TryGetValue(game, out DateTimeOffset next) && next < nextWake)
                {
                    nextWake = next;
                }
            }
            return nextWake;
        }
    }


    /// <summary>
    /// 查一次某游戏本期直播的兑换码：有没通知过的就通知，再按直播阶段排下次检查。
    /// </summary>
    /// <param name="game">游戏，如 hk4e。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task CheckGameAsync(string game, CancellationToken cancellationToken)
    {
        // 国服与 B 服同一场直播，用国服查即可；点通知时游戏选择器会保留 B 服的选择
        var biz = new GameBiz($"{game}_cn");
        RedeemLivePhase lastPhase;
        lock (_lock)
        {
            lastPhase = _lastPhase.GetValueOrDefault(game);
        }

        RedeemCodeSnapshot snapshot = await _redeemCodeService.GetSnapshotAsync(biz, forceRefresh: true, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (snapshot.LoadFailed)
        {
            // 失败原因已由 RedeemCodeService 记日志；直播期间不能等太久，照常每分钟重试
            SetNextCheck(game, lastPhase, now + (lastPhase is RedeemLivePhase.Live ? RedeemCodeNotifySchedule.LivePollInterval : FailureRetry));
            return;
        }

        RedeemLivePhase phase = string.IsNullOrEmpty(snapshot.ActId)
            ? RedeemLivePhase.Unknown
            : RedeemCodeNotifySchedule.GetPhase(snapshot.StartTime, snapshot.EndTime, now);
        if (phase != lastPhase)
        {
            _logger.LogInformation("Redeem code notify: {Game} phase {Phase} (act_id {ActId}, start {Start}, end {End}).",
                                   game, phase, snapshot.ActId, snapshot.StartTime, snapshot.EndTime);
        }

        if (snapshot.ActId is { Length: > 0 } actId
            && snapshot.Codes.Count > 0
            && RedeemCodeNotifySchedule.IsRecent(snapshot.StartTime, snapshot.EndTime, now, RedeemCodeNotifySchedule.NotifyFreshWindow))
        {
            int expected = MiyoLiveActivityConfig.FromGameBiz(biz)?.ExpectedCodeCount ?? 1;
            NotifyNewCodes(game, biz, actId, snapshot, AppConfig.GetRedeemCodeNotifyMode(game), phase, expected);
        }

        SetNextCheck(game, phase, RedeemCodeNotifySchedule.GetNextCheck(phase, snapshot.StartTime, now));
    }


    /// <summary>
    /// 记下某游戏的阶段与下次检查时刻。所有排期（直播期每分钟、睡到开播、失败重试、空闲）都在这里随机延后几秒。
    /// </summary>
    private void SetNextCheck(string game, RedeemLivePhase phase, DateTimeOffset next)
    {
        next = RedeemCodeNotifySchedule.AddJitter(next, Random.Shared.NextDouble());
        lock (_lock)
        {
            _lastPhase[game] = phase;
            _nextCheck[game] = next;
        }
    }


    /// <summary>
    /// 按通知方式发通知，发出后记进已通知列表：
    /// 逐条模式有新码就发一条、只列新码；合并模式等放出的码达到预设数量（或直播窗口已过）再发一条、列出本期全部码。
    /// </summary>
    /// <param name="game">游戏，如 hk4e。</param>
    /// <param name="biz">点通知时要切到的游戏区服。</param>
    /// <param name="actId">本期 act_id。</param>
    /// <param name="snapshot">本次拉到的快照。</param>
    /// <param name="mode">该游戏的通知方式。</param>
    /// <param name="phase">当前直播阶段。</param>
    /// <param name="expectedCount">本期预计的码数。</param>
    private void NotifyNewCodes(string game, GameBiz biz, string actId, RedeemCodeSnapshot snapshot, RedeemCodeNotifyMode mode, RedeemLivePhase phase, int expectedCount)
    {
        string key = NotifiedKeyPrefix + game;
        var notified = RedeemCodeNotifySchedule.ParseNotified(AppConfig.GetValue<string>(default, key));
        List<RedeemCodeItem> all = snapshot.Codes.DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
        List<string> newCodes = RedeemCodeNotifySchedule.GetNewCodes(actId, all.Select(x => x.Code), notified);
        if (!RedeemCodeNotifySchedule.ShouldNotify(mode, phase, all.Count, expectedCount, newCodes.Count))
        {
            return;
        }

        bool merged = mode is RedeemCodeNotifyMode.Merged;
        List<RedeemCodeItem> items = merged ? all : all.Where(x => newCodes.Contains(x.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        string title = string.IsNullOrWhiteSpace(snapshot.Title) ? biz.ToGameName() : snapshot.Title;
        // 合并：每期一条，预设偏少、之后又多出新码时用同一个 Tag 重发的完整列表替换旧条
        // 逐条：每批新码各占一条，后放出的码不替换通知中心里前一条还没复制的
        string tag = merged ? $"redeem_{game}_{actId}" : $"redeem_{game}_{newCodes[0]}";
        bool shown = SystemNotificationService.Show(new SystemNotification
        {
            Title = string.Format(Lang.RedeemCodeNotify_Title, title),
            Lines = items.Select(x => string.IsNullOrWhiteSpace(x.RewardText) ? x.Code : $"{x.Code} · {x.RewardText}").ToList(),
            Attribution = Lang.RedeemCodeButton_Title,
            LaunchUri = NotificationActivation.BuildOpenUri(biz, 0),
            Tag = tag.Length > MaxTagLength ? tag[..MaxTagLength] : tag,
            Group = NotificationGroup,
            SuppressPopup = IsSilent(),
            // 码比按钮多时（只在合并模式预设偏少时发生），按钮留给最后放出的几个
            Buttons = items.TakeLast(MaxCopyButtons)
                           .Select(x => new SystemNotificationButton(string.Format(Lang.RedeemCodeNotify_Copy, x.Code), NotificationActivation.BuildCopyUri(x.Code)))
                           .ToList(),
        });
        if (!shown)
        {
            // 发送出错（已记日志）不记已通知，下次检查再试
            return;
        }

        IEnumerable<string> notifiedCodes = string.Equals(notified.ActId, actId, StringComparison.Ordinal) ? notified.Codes.Concat(newCodes) : newCodes;
        AppConfig.SetValue(RedeemCodeNotifySchedule.SerializeNotified(actId, notifiedCodes), key);
        _logger.LogInformation("Redeem code notify ({Mode}): {Count} new code(s) for {Game} (act_id {ActId}, title {Title}).", mode, newCodes.Count, game, actId, snapshot.Title);
    }


    /// <summary>有游戏在运行且用户选了运行时不打扰：只进通知中心，不弹横幅。</summary>
    private static bool IsSilent() => AppConfig.DailyNoteNotifySilentWhenGameRunning && RunningGameService.GetRunningGameCount() > 0;


    /// <summary>
    /// 等到 <paramref name="due"/>；长等待拆段，每段醒来用墙上时钟重算（相对 Delay 不计休眠）。被唤醒信号打断时提前返回。
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
