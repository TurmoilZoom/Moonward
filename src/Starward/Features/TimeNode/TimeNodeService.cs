using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Blackboard;
using Starward.Core.GameRecord;
using Starward.Features.GameRecord;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.TimeNode;

/// <summary>
/// 时间节点业务：拉取百科 blackboard、区服时区换算、组装展示快照。
/// </summary>
internal class TimeNodeService
{

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(20);

    private readonly ILogger<TimeNodeService> _logger;
    private readonly BlackboardClient _client;
    private readonly IMemoryCache _memoryCache;
    private readonly GameRecordService _gameRecordService;


    /// <summary>
    /// 初始化时间节点服务。
    /// </summary>
    public TimeNodeService(
        ILogger<TimeNodeService> logger,
        BlackboardClient client,
        IMemoryCache memoryCache,
        GameRecordService gameRecordService)
    {
        _logger = logger;
        _client = client;
        _memoryCache = memoryCache;
        _gameRecordService = gameRecordService;
    }


    /// <summary>
    /// 按当前游戏拉取并组装时间节点快照。
    /// </summary>
    /// <param name="gameBiz">当前启动页游戏。</param>
    /// <param name="forceRefresh">为 true 时跳过内存缓存。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>展示用快照；不支持的游戏返回空分段。</returns>
    public async Task<TimeNodeSnapshot> GetSnapshotAsync(GameBiz gameBiz, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        string? appSn = BlackboardClient.GetAppSn(gameBiz);
        if (appSn is null)
        {
            return new TimeNodeSnapshot();
        }

        int offsetHours = ResolveOffsetHours(gameBiz);
        string cacheKey = $"TimeNode:{gameBiz.Value}:{offsetHours}";

        if (!forceRefresh && _memoryCache.TryGetValue(cacheKey, out TimeNodeSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        var sections = new List<TimeNodeSection>();

        if (gameBiz.Game is GameBiz.nap)
        {
            try
            {
                BlackboardPositionData position = await GetPositionCachedAsync(appSn, forceRefresh, cancellationToken);
                TimeNodeSection? hot = BuildHotEventsSection(position, offsetHours);
                if (hot is not null && hot.Items.Count > 0)
                {
                    sections.Add(hot);
                }
            }
            catch (Exception ex)
            {
                // 热点失败不阻断调频
                _logger.LogWarning(ex, "Load ZZZ hot events failed (biz {Biz})", gameBiz.Value);
            }
        }

        BlackboardGachaPoolData pool = await GetGachaPoolCachedAsync(appSn, forceRefresh, cancellationToken);
        string gachaSectionTitle = gameBiz.Game switch
        {
            GameBiz.hk4e => Lang.TimeNode_Section_LimitedWish,
            GameBiz.hkrpg => Lang.TimeNode_Section_EventWarp,
            GameBiz.nap => Lang.TimeNode_Section_SignalSearch,
            _ => Lang.TimeNode_Section_LimitedWish,
        };
        TimeNodeSection gachaSection = BuildGachaSection(pool, gachaSectionTitle, offsetHours);
        if (gachaSection.Items.Count > 0)
        {
            sections.Add(gachaSection);
        }

        var snapshot = new TimeNodeSnapshot { Sections = sections };
        _memoryCache.Set(cacheKey, snapshot, CacheDuration);
        return snapshot;
    }


    /// <summary>
    /// 解析区服偏移；global 时尝试用最近角色 / 抽卡同步 UID。
    /// </summary>
    private int ResolveOffsetHours(GameBiz gameBiz)
    {
        GameBiz roleBiz = gameBiz;
        if (roleBiz.Server is "bilibili")
        {
            roleBiz = $"{roleBiz.Game}_cn";
        }

        long? uid = null;
        try
        {
            GameRecordRole? role = _gameRecordService.GetLastSelectGameRecordRoleOrTheFirstOne(roleBiz)
                ?? _gameRecordService.GetLastSelectGachaSyncRoleOrTheFirstOne(roleBiz);
            if (role is not null && role.Uid > 0)
            {
                uid = role.Uid;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Resolve UID for time node timezone failed (biz {Biz})", gameBiz.Value);
        }

        return TimeNodeTimeHelper.ResolveServerOffsetHours(gameBiz, uid);
    }


    private async Task<BlackboardGachaPoolData> GetGachaPoolCachedAsync(string appSn, bool forceRefresh, CancellationToken cancellationToken)
    {
        string key = $"Blackboard:GachaPool:{appSn}";
        if (!forceRefresh && _memoryCache.TryGetValue(key, out BlackboardGachaPoolData? data) && data is not null)
        {
            return data;
        }
        data = await _client.GetGachaPoolAsync(appSn, cancellationToken: cancellationToken);
        _memoryCache.Set(key, data, CacheDuration);
        return data;
    }


    private async Task<BlackboardPositionData> GetPositionCachedAsync(string appSn, bool forceRefresh, CancellationToken cancellationToken)
    {
        string key = $"Blackboard:Position:{appSn}";
        if (!forceRefresh && _memoryCache.TryGetValue(key, out BlackboardPositionData? data) && data is not null)
        {
            return data;
        }
        data = await _client.GetHomePositionAsync(appSn, cancellationToken: cancellationToken);
        _memoryCache.Set(key, data, CacheDuration);
        return data;
    }


    private static TimeNodeSection BuildGachaSection(BlackboardGachaPoolData pool, string title, int offsetHours)
    {
        var items = new List<TimeNodeItem>();
        foreach (BlackboardGachaPoolItem row in pool.List ?? [])
        {
            DateTimeOffset? start = TimeNodeTimeHelper.ParseChinaWallClockToServer(row.StartTime, offsetHours);
            DateTimeOffset? end = TimeNodeTimeHelper.ParseChinaWallClockToServer(row.EndTime, offsetHours);
            if (end is null)
            {
                continue;
            }

            var icons = new List<TimeNodeIcon>();
            foreach (BlackboardGachaPoolIcon icon in row.Pool ?? [])
            {
                if (string.IsNullOrWhiteSpace(icon.Icon))
                {
                    continue;
                }
                icons.Add(new TimeNodeIcon
                {
                    Url = icon.Icon!,
                    Level = TryParseLevel(icon.Ext),
                });
            }

            items.Add(new TimeNodeItem
            {
                Title = row.Title ?? "",
                LinkUrl = row.ActivityUrl,
                CountdownKind = TimeNodeCountdownKind.Precise,
                ContentBeforeAct = row.ContentBeforeAct,
                StartTime = start,
                EndTime = end.Value,
                Icons = icons,
            });
        }

        return new TimeNodeSection { Title = title, Items = items };
    }


    private static TimeNodeSection? BuildHotEventsSection(BlackboardPositionData position, int offsetHours)
    {
        var items = new List<TimeNodeItem>();
        foreach (BlackboardPositionItem row in EnumerateHitCardItems(position.List))
        {
            DateTimeOffset? end = TimeNodeTimeHelper.ParseUnixMsToServer(row.EndTime, offsetHours);
            if (end is null)
            {
                continue;
            }
            items.Add(new TimeNodeItem
            {
                Title = row.Title ?? "",
                Subtitle = row.Abstract,
                LinkUrl = row.Url,
                CoverIcon = row.Icon,
                CountdownKind = TimeNodeCountdownKind.Coarse,
                StartTime = null,
                EndTime = end.Value,
                Icons = [],
            });
        }

        if (items.Count == 0)
        {
            return null;
        }
        return new TimeNodeSection { Title = Lang.TimeNode_Section_HotEvents, Items = items };
    }


    /// <summary>
    /// 递归收集 display_type=hitCard 或名称含「活动日历」节点下的 list。
    /// </summary>
    private static IEnumerable<BlackboardPositionItem> EnumerateHitCardItems(IEnumerable<BlackboardPositionNode>? nodes)
    {
        if (nodes is null)
        {
            yield break;
        }
        foreach (BlackboardPositionNode node in nodes)
        {
            bool hit = IsHitCardNode(node);
            if (hit && node.List is { Count: > 0 })
            {
                foreach (BlackboardPositionItem item in node.List)
                {
                    yield return item;
                }
            }
            foreach (BlackboardPositionItem child in EnumerateHitCardItems(node.Children))
            {
                yield return child;
            }
        }
    }


    private static bool IsHitCardNode(BlackboardPositionNode node)
    {
        if (!string.IsNullOrEmpty(node.Name) && node.Name.Contains("活动日历", StringComparison.Ordinal))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(node.ChannelExt))
        {
            return false;
        }
        // ch_ext 为 JSON 数组字符串，宽松匹配 display_type
        return node.ChannelExt.Contains("hitCard", StringComparison.OrdinalIgnoreCase);
    }


    private static string? TryParseLevel(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
        {
            return null;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(ext);
            if (doc.RootElement.TryGetProperty("level", out JsonElement level) && level.ValueKind is JsonValueKind.String)
            {
                string? s = level.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s.ToLowerInvariant();
            }
        }
        catch
        {
            // ignore malformed ext
        }
        return null;
    }

}
