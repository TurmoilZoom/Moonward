using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.MiyoLive;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.RedeemCode;

/// <summary>
/// 前瞻直播兑换码业务：发现 act_id、拉 index / refreshCode、组装展示快照。首页弹层与后台通知（<see cref="RedeemCodeNotifyService"/>）共用。
/// </summary>
internal partial class RedeemCodeService
{

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    /// <summary>解析到的 act_id 在内存里复用多久；期间只拉 index / refreshCode，不重跑发现（直播期间每分钟都要刷新）。</summary>
    private static readonly TimeSpan ActIdCacheDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 结束后多久内仍算「本期」：候选里优先返回这样的活动；发现路径都找不到时，用 Setting 里记下的 act_id 兜底
    /// （动态下沉、导航入口撤下后码可能还能兑）。
    /// </summary>
    private static readonly TimeSpan CurrentLiveWindow = TimeSpan.FromDays(3);

    /// <summary>index 返回业务 retcode（活动已下线）的 act_id 在内存里记多久，期间发现时直接跳过。</summary>
    private static readonly TimeSpan DeadActCacheDuration = TimeSpan.FromHours(12);

    /// <summary>一次发现最多校验几个候选 act_id（每个一次 index 请求，已下线的不计）。</summary>
    private const int MaxCandidates = 4;

    /// <summary>记下最近一期 act_id 的 Setting 键前缀，值为 <c>{act_id}|{结束时刻 Unix 秒}</c>。</summary>
    private const string LastLiveKeyPrefix = "redeem_code_last_live_";

    private readonly ILogger<RedeemCodeService> _logger;
    private readonly MiyoLiveClient _client;
    private readonly IMemoryCache _memoryCache;


    /// <summary>
    /// 初始化兑换码服务。
    /// </summary>
    public RedeemCodeService(ILogger<RedeemCodeService> logger, MiyoLiveClient client, IMemoryCache memoryCache)
    {
        _logger = logger;
        _client = client;
        _memoryCache = memoryCache;
    }


    /// <summary>
    /// 按当前游戏拉取并组装兑换码快照。国服与 B 服是同一场直播，按游戏共用缓存。
    /// </summary>
    /// <param name="gameBiz">当前启动页游戏。</param>
    /// <param name="forceRefresh">为 true 时跳过快照缓存（act_id 仍复用 <see cref="ActIdCacheDuration"/> 内的解析结果）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 展示用快照；加载失败时 <see cref="RedeemCodeSnapshot.LoadFailed"/> 为 true 且不写入缓存——
    /// 失败与「本期无码」必须分开，否则一次网络抖动会被固化成两分钟的空态。
    /// </returns>
    /// <exception cref="OperationCanceledException">调用方取消时抛出，由 UI 自行忽略。</exception>
    public async Task<RedeemCodeSnapshot> GetSnapshotAsync(GameBiz gameBiz, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        MiyoLiveGameConfig? config = MiyoLiveActivityConfig.FromGameBiz(gameBiz);
        if (config is null)
        {
            return new RedeemCodeSnapshot();
        }

        string cacheKey = $"RedeemCode:{gameBiz.Game}";
        if (!forceRefresh && _memoryCache.TryGetValue(cacheKey, out RedeemCodeSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            RedeemCodeSnapshot snapshot = await FetchSnapshotAsync(config.Value, gameBiz, start, cancellationToken);
            if (!snapshot.LoadFailed)
            {
                _memoryCache.Set(cacheKey, snapshot, CacheDuration);
            }
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消（关弹层 / 切游戏）：既不缓存也不改 UI
            throw;
        }
        catch (Exception ex)
        {
            // 网络 / 超时 / 反序列化失败：交给 UI 显示「加载失败」，不写缓存以便下次重试
            _logger.LogWarning(ex, "Redeem code load failed ({Biz}) in {Elapsed} ms.", gameBiz.Value, ElapsedMs(start));
            return new RedeemCodeSnapshot { LoadFailed = true };
        }
    }


    /// <summary>
    /// 实际拉取一次快照（不含缓存与顶层异常处理）。
    /// </summary>
    /// <param name="config">当前游戏的直播活动发现参数。</param>
    /// <param name="gameBiz">当前游戏。</param>
    /// <param name="start">本次加载的起始时间戳，用于统计总耗时。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>展示用快照。</returns>
    private async Task<RedeemCodeSnapshot> FetchSnapshotAsync(MiyoLiveGameConfig config, GameBiz gameBiz, long start, CancellationToken cancellationToken)
    {
        LiveResolution resolution = await ResolveLiveAsync(config, gameBiz.Game, cancellationToken);
        if (resolution.Live is not { } live || string.IsNullOrEmpty(resolution.ActId))
        {
            if (resolution.Failed)
            {
                // 发现路径或校验请求出了异常、又没拿到可用活动：判为加载失败，别把它当成「没有活动」缓存下来
                return new RedeemCodeSnapshot { LoadFailed = true };
            }
            _logger.LogInformation("Redeem code: no live act_id found ({Biz}, uid {Uid}, gids {Gids}) in {Elapsed} ms.",
                                   gameBiz.Value, config.Uid, config.Gids, ElapsedMs(start));
            return new RedeemCodeSnapshot();
        }

        string actId = resolution.ActId;
        string? title = live.Title?.Replace("特别节目", "", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(title))
        {
            title = live.Title;
        }
        DateTimeOffset? startTime = RedeemCodeNotifySchedule.ParseChinaTime(live.Start);
        DateTimeOffset? endTime = RedeemCodeNotifySchedule.ParseChinaTime(live.End);

        // 未到可领：remain > 0（与云崽一致）或当前时间早于 start
        if (live.Remain > 0 || DateTimeOffset.UtcNow < startTime)
        {
            _logger.LogInformation("Redeem code: live not started ({Biz}, act_id {ActId}, title {Title}, start {Start}, remain {Remain}).",
                                   gameBiz.Value, actId, title, live.Start, live.Remain);
            return new RedeemCodeSnapshot
            {
                ActId = actId,
                StartTime = startTime,
                EndTime = endTime,
                Title = title,
                NotStarted = true,
                StartTimeText = live.Start,
                IsEnded = live.IsEnd,
            };
        }

        if (string.IsNullOrEmpty(live.CodeVer))
        {
            _logger.LogInformation("Redeem code: live has no code_ver ({Biz}, act_id {ActId}, title {Title}, is_end {IsEnd}).",
                                   gameBiz.Value, actId, title, live.IsEnd);
            return new RedeemCodeSnapshot
            {
                ActId = actId,
                StartTime = startTime,
                EndTime = endTime,
                Title = title,
                IsEnded = live.IsEnd,
            };
        }

        var codes = new List<RedeemCodeItem>();
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            MiyoLiveCodeData codeData = await _client.RefreshCodeAsync(actId, live.CodeVer, now, cancellationToken);
            foreach (MiyoLiveCodeItem item in codeData.CodeList)
            {
                if (string.IsNullOrWhiteSpace(item.Code))
                {
                    continue;
                }
                codes.Add(new RedeemCodeItem
                {
                    Code = item.Code.Trim(),
                    RewardText = FormatReward(item.Title),
                });
            }
        }
        catch (miHoYoApiException ex)
        {
            // 活动结束 / 无码等业务 retcode：按「无码」处理，不向 UI 报错；retcode 记进日志便于事后排查
            _logger.LogInformation("Redeem code: refreshCode returned retcode {Code} ({Message}) for act_id {ActId} ({Biz}).",
                                   ex.ReturnCode, ex.Message, actId, gameBiz.Value);
        }

        _logger.LogInformation("Redeem code loaded ({Biz}): act_id {ActId} from {Source}, title {Title}, is_end {IsEnd}, {Count} code(s) in {Elapsed} ms.",
                               gameBiz.Value, actId, resolution.Source, title, live.IsEnd, codes.Count, ElapsedMs(start));

        return new RedeemCodeSnapshot
        {
            ActId = actId,
            StartTime = startTime,
            EndTime = endTime,
            Title = title,
            IsEnded = live.IsEnd,
            Codes = codes,
        };
    }


    /// <summary>
    /// 找到本期直播：先试内存里 10 分钟内解析过的 act_id，再依次校验首页导航、官号动态、上次记下的 act_id 这几路候选。
    /// </summary>
    /// <remarks>
    /// 每个候选都要用 index 校验：动态里留着往期讨论帖的 act_id（已下线的返回 -500007，没下线的会把往期的码当成本期），
    /// 只认第一个尚未结束或刚结束不久的活动；都不是就退而取开播最晚的那个，保持旧版「有什么显示什么」。
    /// </remarks>
    /// <param name="config">当前游戏的直播活动发现参数。</param>
    /// <param name="game">游戏（<see cref="GameBiz.Game"/>），作缓存与 Setting 键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果；没拿到可用活动、且途中有请求异常时 <c>Failed</c> 为 true。</returns>
    /// <exception cref="OperationCanceledException">调用方取消时抛出；不能吞掉，否则会被当成「没有活动」。</exception>
    private async Task<LiveResolution> ResolveLiveAsync(MiyoLiveGameConfig config, string game, CancellationToken cancellationToken)
    {
        string memoryKey = $"RedeemCodeAct:{game}";
        bool failed = false;
        var tried = new HashSet<string>(StringComparer.Ordinal);

        if (_memoryCache.TryGetValue(memoryKey, out string? cachedActId) && !string.IsNullOrEmpty(cachedActId))
        {
            tried.Add(cachedActId);
            CandidateResult cached = await TryLoadIndexAsync(cachedActId, cancellationToken);
            if (cached.Live is not null)
            {
                return new LiveResolution(cachedActId, cached.Live, "memory", false);
            }
            failed |= cached.Failed;
            _memoryCache.Remove(memoryKey);
        }

        var candidates = new List<(string ActId, string Source)>();
        failed |= await CollectCandidatesAsync("home_navigator", () => _client.GetActIdCandidatesFromHomeNavigatorAsync(config.Gids, cancellationToken), candidates, cancellationToken);
        failed |= await CollectCandidatesAsync("user_instant", () => _client.GetActIdCandidatesFromUserInstantAsync(config.Uid, cancellationToken), candidates, cancellationToken);
        if (GetLastLive(game) is { } last && DateTimeOffset.UtcNow <= last.Finish + CurrentLiveWindow)
        {
            candidates.Add((last.ActId, "last_live"));
        }

        LiveResolution? best = null;
        DateTimeOffset? bestStart = null;
        int checkedCount = 0;
        foreach ((string actId, string source) in candidates)
        {
            if (!tried.Add(actId) || _memoryCache.TryGetValue(DeadActKey(actId), out _))
            {
                continue;
            }
            if (++checkedCount > MaxCandidates)
            {
                break;
            }
            CandidateResult result = await TryLoadIndexAsync(actId, cancellationToken);
            failed |= result.Failed;
            if (result.Live is not { } live)
            {
                continue;
            }
            DateTimeOffset? start = RedeemCodeNotifySchedule.ParseChinaTime(live.Start);
            DateTimeOffset? end = RedeemCodeNotifySchedule.ParseChinaTime(live.End);
            if (RedeemCodeNotifySchedule.IsRecent(start, end, DateTimeOffset.UtcNow, CurrentLiveWindow))
            {
                _logger.LogInformation("Redeem code: act_id {ActId} resolved from {Source} (start {Start}, end {End}).", actId, source, live.Start, live.End);
                _memoryCache.Set(memoryKey, actId, ActIdCacheDuration);
                SetLastLive(game, actId, RedeemCodeNotifySchedule.GetFinish(start, end));
                return new LiveResolution(actId, live, source, false);
            }
            if (best is null || start > bestStart)
            {
                best = new LiveResolution(actId, live, source, false);
                bestStart = start;
            }
        }

        if (best is { } fallback)
        {
            _logger.LogInformation("Redeem code: no current live, falling back to act_id {ActId} from {Source} (start {Start}).",
                                   fallback.ActId, fallback.Source, fallback.Live?.Start);
            _memoryCache.Set(memoryKey, fallback.ActId, ActIdCacheDuration);
            return fallback;
        }
        return new LiveResolution(null, null, "", failed);
    }


    /// <summary>
    /// 跑一路发现，把候选追加到列表末尾。
    /// </summary>
    /// <param name="source">来源名，仅用于日志。</param>
    /// <param name="fetch">发现请求。</param>
    /// <param name="candidates">候选列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>请求异常时为 true。</returns>
    private async Task<bool> CollectCandidatesAsync(string source, Func<Task<List<string>>> fetch, List<(string ActId, string Source)> candidates, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            List<string> found = await fetch();
            candidates.AddRange(found.Select(x => (x, source)));
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redeem code: discover act_id from {Source} failed in {Elapsed} ms.", source, ElapsedMs(start));
            return true;
        }
    }


    /// <summary>
    /// 用 index 校验一个候选 act_id。
    /// </summary>
    /// <param name="actId">候选 act_id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>有效时带直播信息；业务 retcode（活动下线等）时两者皆空；网络等异常时 <c>Failed</c> 为 true。</returns>
    private async Task<CandidateResult> TryLoadIndexAsync(string actId, CancellationToken cancellationToken)
    {
        try
        {
            MiyoLiveIndexData index = await _client.GetLiveIndexAsync(actId, cancellationToken);
            return new CandidateResult(index.Live, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (miHoYoApiException ex)
        {
            // 活动已下线（-500007 / -500012 等）不会再复活：记住它，之后的发现直接跳过，免得每轮都为动态里的往期帖多打几次 index
            _memoryCache.Set(DeadActKey(actId), true, DeadActCacheDuration);
            _logger.LogInformation("Redeem code: index returned retcode {Code} ({Message}) for act_id {ActId}.", ex.ReturnCode, ex.Message, actId);
            return new CandidateResult(null, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redeem code: load index failed for act_id {ActId}.", actId);
            return new CandidateResult(null, true);
        }
    }


    /// <summary>已下线 act_id 的内存缓存键。</summary>
    private static string DeadActKey(string actId) => $"RedeemCodeDeadAct:{actId}";


    /// <summary>
    /// 读上次解析到的「本期」act_id。
    /// </summary>
    /// <param name="game">游戏。</param>
    /// <returns>act_id 与结束时刻；没有记录或记录损坏时为 null。</returns>
    private static (string ActId, DateTimeOffset Finish)? GetLastLive(string game)
    {
        string? value = AppConfig.GetValue<string>(default, LastLiveKeyPrefix + game);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string[] parts = value.Split('|');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long finish))
        {
            return null;
        }
        return (parts[0], DateTimeOffset.FromUnixTimeSeconds(finish));
    }


    /// <summary>
    /// 记下本期 act_id，供动态下沉、导航撤下后兜底。
    /// </summary>
    /// <param name="game">游戏。</param>
    /// <param name="actId">本期 act_id。</param>
    /// <param name="finish">结束时刻；不知道时不记。</param>
    private static void SetLastLive(string game, string actId, DateTimeOffset? finish)
    {
        if (finish is { } f)
        {
            AppConfig.SetValue($"{actId}|{f.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}", LastLiveKeyPrefix + game);
        }
    }


    /// <summary>
    /// 把奖励说明（每样奖励一个 <c>&lt;p&gt;</c>）整理成一行：去标签，段与段之间用顿号分隔，<c>*</c> 换成 <c>×</c>。
    /// </summary>
    /// <param name="html">refreshCode 的 title。</param>
    /// <returns>如「菲林×300、资深调查员记录×2」；空输入返回空串。</returns>
    internal static string FormatReward(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        string text = BlockBreakRegex().Replace(html, "\n");
        IEnumerable<string> parts = StripHtml(text).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("、", parts).Replace('*', '×');
    }


    /// <summary>
    /// 去掉 title 中的 HTML 标签与常见实体。
    /// </summary>
    internal static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        string text = HtmlTagRegex().Replace(html, "");
        text = text.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
                   .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
                   .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
                   .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
                   .Trim();
        return text;
    }


    /// <summary>
    /// 自 <paramref name="startTimestamp"/> 起经过的毫秒数，仅用于日志。
    /// </summary>
    /// <param name="startTimestamp">Stopwatch 时间戳。</param>
    /// <returns>毫秒数。</returns>
    private static long ElapsedMs(long startTimestamp) => (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;


    [GeneratedRegex("<.*?>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();


    /// <summary>段落结束与换行标签。</summary>
    [GeneratedRegex(@"</p\s*>|<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockBreakRegex();


    /// <summary>
    /// 本期直播的解析结果。
    /// </summary>
    /// <param name="ActId">act_id；没找到为 null。</param>
    /// <param name="Live">index 返回的直播信息。</param>
    /// <param name="Source">命中来源，仅用于日志。</param>
    /// <param name="Failed">没找到且途中有请求异常（区别于「确实没有活动」）。</param>
    private readonly record struct LiveResolution(string? ActId, MiyoLiveInfo? Live, string Source, bool Failed);


    /// <summary>
    /// 单个候选的 index 校验结果。
    /// </summary>
    /// <param name="Live">有效时的直播信息。</param>
    /// <param name="Failed">网络等异常。</param>
    private readonly record struct CandidateResult(MiyoLiveInfo? Live, bool Failed);

}
