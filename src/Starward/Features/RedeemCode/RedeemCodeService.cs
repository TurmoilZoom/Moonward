using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.MiyoLive;
using System;
using System.Collections.Generic;
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
    /// <returns>展示用快照；不支持或无活动时返回空列表快照。</returns>
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

        string? actId = await ResolveActIdAsync(config.Value, cancellationToken);
        if (string.IsNullOrEmpty(actId))
        {
            var empty = new RedeemCodeSnapshot();
            _memoryCache.Set(cacheKey, empty, CacheDuration);
            return empty;
        }

        try
        {
            MiyoLiveIndexData index = await _client.GetLiveIndexAsync(actId, cancellationToken);
            MiyoLiveInfo? live = index.Live;
            if (live is null)
            {
                var empty = new RedeemCodeSnapshot();
                _memoryCache.Set(cacheKey, empty, CacheDuration);
                return empty;
            }

            string? title = live.Title?.Replace("特别节目", "", StringComparison.Ordinal).Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = live.Title;
            }

            // 未到可领：remain > 0（与云崽一致）或当前时间早于 start
            if (live.Remain > 0 || IsBeforeStart(live.Start))
            {
                var notStarted = new RedeemCodeSnapshot
                {
                    Title = title,
                    NotStarted = true,
                    StartTimeText = live.Start,
                    IsEnded = live.IsEnd,
                };
                _memoryCache.Set(cacheKey, notStarted, CacheDuration);
                return notStarted;
            }

            if (string.IsNullOrEmpty(live.CodeVer))
            {
                var empty = new RedeemCodeSnapshot
                {
                    Title = title,
                    IsEnded = live.IsEnd,
                };
                _memoryCache.Set(cacheKey, empty, CacheDuration);
                return empty;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            MiyoLiveCodeData codeData = await _client.RefreshCodeAsync(actId, live.CodeVer, now, cancellationToken);
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

            var snapshot = new RedeemCodeSnapshot
            {
                Title = title,
                IsEnded = live.IsEnd,
                Codes = codes,
            };
            _memoryCache.Set(cacheKey, snapshot, CacheDuration);
            return snapshot;
        }
        catch (miHoYoApiException ex)
        {
            // 活动结束 / 无码 / -50007 等业务 retcode：一律空快照，不向 UI 抛错（避免卡片红字）
            _logger.LogDebug(ex, "MiyoLive API unavailable (retcode {Code}, biz {Biz})", ex.ReturnCode, gameBiz.Value);
            var empty = new RedeemCodeSnapshot();
            _memoryCache.Set(cacheKey, empty, CacheDuration);
            return empty;
        }
    }


    private async Task<string?> ResolveActIdAsync(MiyoLiveGameConfig config, CancellationToken cancellationToken)
    {
        try
        {
            string? actId = await _client.TryGetActIdFromUserInstantAsync(config.Uid, cancellationToken);
            if (!string.IsNullOrEmpty(actId))
            {
                return actId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolve act_id from user instant failed (uid {Uid})", config.Uid);
        }

        try
        {
            return await _client.TryGetActIdFromHomeNavigatorAsync(config.Gids, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolve act_id from home navigator failed (gids {Gids})", config.Gids);
            return null;
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


    [GeneratedRegex("<.*?>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

}
