using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.MiyoLive;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.RedeemCode;

/// <summary>
/// 前瞻直播兑换码业务：发现 act_id、拉 index / refreshCode、组装展示快照。
/// </summary>
internal partial class RedeemCodeService
{

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

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
    /// 按当前游戏拉取并组装兑换码快照。
    /// </summary>
    /// <param name="gameBiz">当前启动页游戏。</param>
    /// <param name="forceRefresh">为 true 时跳过内存缓存。</param>
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

        string cacheKey = $"RedeemCode:{gameBiz.Value}";
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
            // 网络 / HttpClient 超时 / 反序列化失败：交给 UI 显示「加载失败」，不写缓存以便下次重试
            _logger.LogWarning(ex, "Redeem code load failed ({Biz}) in {Elapsed} ms.", gameBiz.Value, ElapsedMs(start));
            return new RedeemCodeSnapshot { LoadFailed = true };
        }
    }


    /// <summary>
    /// 实际拉取一次快照（不含缓存与顶层异常处理）。
    /// </summary>
    /// <param name="config">当前游戏的直播活动发现参数。</param>
    /// <param name="gameBiz">当前游戏，仅用于日志。</param>
    /// <param name="start">本次加载的起始时间戳，用于统计总耗时。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>展示用快照。</returns>
    private async Task<RedeemCodeSnapshot> FetchSnapshotAsync(MiyoLiveGameConfig config, GameBiz gameBiz, long start, CancellationToken cancellationToken)
    {
        ActIdResolution resolution = await ResolveActIdAsync(config, cancellationToken);
        if (string.IsNullOrEmpty(resolution.ActId))
        {
            if (resolution.Failed)
            {
                // 两条发现路径都异常：判为加载失败，别把它当成「没有活动」缓存下来
                return new RedeemCodeSnapshot { LoadFailed = true };
            }
            _logger.LogInformation("Redeem code: no live act_id found ({Biz}, uid {Uid}, gids {Gids}) in {Elapsed} ms.",
                                   gameBiz.Value, config.Uid, config.Gids, ElapsedMs(start));
            return new RedeemCodeSnapshot();
        }

        try
        {
            MiyoLiveIndexData index = await _client.GetLiveIndexAsync(resolution.ActId, cancellationToken);
            MiyoLiveInfo? live = index.Live;
            if (live is null)
            {
                _logger.LogInformation("Redeem code: act_id {ActId} has no live info ({Biz}).", resolution.ActId, gameBiz.Value);
                return new RedeemCodeSnapshot();
            }

            string? title = live.Title?.Replace("特别节目", "", StringComparison.Ordinal).Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = live.Title;
            }

            // 未到可领：remain > 0（与云崽一致）或当前时间早于 start
            if (live.Remain > 0 || IsBeforeStart(live.Start))
            {
                _logger.LogInformation("Redeem code: live not started ({Biz}, act_id {ActId}, title {Title}, start {Start}, remain {Remain}).",
                                       gameBiz.Value, resolution.ActId, title, live.Start, live.Remain);
                return new RedeemCodeSnapshot
                {
                    Title = title,
                    NotStarted = true,
                    StartTimeText = live.Start,
                    IsEnded = live.IsEnd,
                };
            }

            if (string.IsNullOrEmpty(live.CodeVer))
            {
                _logger.LogInformation("Redeem code: live has no code_ver ({Biz}, act_id {ActId}, title {Title}, is_end {IsEnd}).",
                                       gameBiz.Value, resolution.ActId, title, live.IsEnd);
                return new RedeemCodeSnapshot
                {
                    Title = title,
                    IsEnded = live.IsEnd,
                };
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            MiyoLiveCodeData codeData = await _client.RefreshCodeAsync(resolution.ActId, live.CodeVer, now, cancellationToken);
            var codes = new List<RedeemCodeItem>();
            foreach (MiyoLiveCodeItem item in codeData.CodeList)
            {
                if (string.IsNullOrWhiteSpace(item.Code))
                {
                    continue;
                }
                codes.Add(new RedeemCodeItem
                {
                    Code = item.Code.Trim(),
                    RewardText = StripHtml(item.Title),
                });
            }

            _logger.LogInformation("Redeem code loaded ({Biz}): act_id {ActId} from {Source}, title {Title}, is_end {IsEnd}, {Count} code(s) in {Elapsed} ms.",
                                   gameBiz.Value, resolution.ActId, resolution.Source, title, live.IsEnd, codes.Count, ElapsedMs(start));

            return new RedeemCodeSnapshot
            {
                Title = title,
                IsEnded = live.IsEnd,
                Codes = codes,
            };
        }
        catch (miHoYoApiException ex)
        {
            // 活动结束 / 无码 / -50007 等业务 retcode：按「无码」处理，不向 UI 报错；retcode 记进日志便于事后排查
            _logger.LogInformation("Redeem code: miyolive returned retcode {Code} ({Message}) for act_id {ActId} ({Biz}).",
                                   ex.ReturnCode, ex.Message, resolution.ActId, gameBiz.Value);
            return new RedeemCodeSnapshot();
        }
    }


    /// <summary>
    /// 依次尝试官方动态列表与米游社首页导航解析 act_id。
    /// </summary>
    /// <param name="config">当前游戏的直播活动发现参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果；两条路径都没拿到且至少一条抛了异常时 <c>Failed</c> 为 true。</returns>
    /// <exception cref="OperationCanceledException">调用方取消时抛出；不能吞掉，否则会被当成「没有活动」。</exception>
    private async Task<ActIdResolution> ResolveActIdAsync(MiyoLiveGameConfig config, CancellationToken cancellationToken)
    {
        bool failed = false;

        long start = Stopwatch.GetTimestamp();
        try
        {
            string? actId = await _client.TryGetActIdFromUserInstantAsync(config.Uid, cancellationToken);
            if (!string.IsNullOrEmpty(actId))
            {
                _logger.LogInformation("Redeem code: act_id {ActId} resolved from user instant (uid {Uid}) in {Elapsed} ms.", actId, config.Uid, ElapsedMs(start));
                return new ActIdResolution(actId, "user_instant", false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failed = true;
            _logger.LogWarning(ex, "Resolve act_id from user instant failed (uid {Uid}, bbs-api.mihoyo.com) in {Elapsed} ms", config.Uid, ElapsedMs(start));
        }

        start = Stopwatch.GetTimestamp();
        try
        {
            string? actId = await _client.TryGetActIdFromHomeNavigatorAsync(config.Gids, cancellationToken);
            if (!string.IsNullOrEmpty(actId))
            {
                _logger.LogInformation("Redeem code: act_id {ActId} resolved from home navigator (gids {Gids}) in {Elapsed} ms.", actId, config.Gids, ElapsedMs(start));
                return new ActIdResolution(actId, "home_navigator", false);
            }
            return new ActIdResolution(null, "", failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolve act_id from home navigator failed (gids {Gids}, bbs-api.miyoushe.com) in {Elapsed} ms", config.Gids, ElapsedMs(start));
            return new ActIdResolution(null, "", true);
        }
    }


    private static bool IsBeforeStart(string? startText)
    {
        if (string.IsNullOrWhiteSpace(startText))
        {
            return false;
        }
        // 官方 start 多为东八区墙钟
        if (!DateTime.TryParse(startText, out DateTime startLocal))
        {
            return false;
        }
        try
        {
            var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            DateTimeOffset start = new(startLocal, china.GetUtcOffset(startLocal));
            return DateTimeOffset.UtcNow < start;
        }
        catch (TimeZoneNotFoundException)
        {
            // 回退：按本地时间比较
            return DateTime.Now < startLocal;
        }
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


    /// <summary>
    /// act_id 解析结果。
    /// </summary>
    /// <param name="ActId">解析到的 act_id；未找到为 null。</param>
    /// <param name="Source">命中来源，仅用于日志。</param>
    /// <param name="Failed">是否因异常而失败（区别于「确实没有进行中的活动」）。</param>
    private readonly record struct ActIdResolution(string? ActId, string Source, bool Failed);

}
