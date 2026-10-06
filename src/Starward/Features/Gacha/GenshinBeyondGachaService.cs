using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Localization;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

internal class GenshinBeyondGachaService
{


    private readonly ILogger<GenshinBeyondGachaService> _logger;

    private readonly GenshinBeyondGachaClient _client;


    private const string GachaTableName = "GenshinBeyondGachaItem";


    public GenshinBeyondGachaService(ILogger<GenshinBeyondGachaService> logger, GenshinBeyondGachaClient client)
    {
        _logger = logger;
        _client = client;
    }



    public virtual List<long> GetUids()
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<long>($"SELECT DISTINCT Uid FROM {GachaTableName} WHERE Uid > 0;").ToList();
    }



    /// <summary>
    /// 从网页缓存提取 Beyond 抽卡 URL（第一个候选）。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="path">游戏安装根目录。</param>
    /// <returns>URL 或 null。</returns>
    public string? GetGachaLogUrlFromWebCache(GameBiz gameBiz, string path)
    {
        return GenshinBeyondGachaClient.GetGachaUrlFromWebCache(gameBiz, path);
    }



    /// <summary>
    /// 从网页缓存提取候选 URL 并校验 authkey，返回第一个有效 URL。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="path">游戏安装根目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>有效 URL；无候选时 null。</returns>
    /// <exception cref="GachaApiException">全部候选 authkey 过期时抛出。</exception>
    public async Task<string?> GetValidatedGachaLogUrlFromWebCacheAsync(GameBiz gameBiz, string path, CancellationToken cancellationToken = default)
    {
        var candidates = GenshinBeyondGachaClient.GetGachaUrlCandidatesFromWebCache(gameBiz, path);
        if (candidates.Count == 0)
        {
            return null;
        }

        GachaApiException? lastAuthError = null;
        foreach (var url in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client.GetUidByGachaUrlAsync(url);
                return url;
            }
            catch (GachaApiException ex) when (ex.IsAuthkeyExpired)
            {
                _logger.LogInformation("Beyond gacha authkey candidate expired, try next: {Message}", ex.Message);
                lastAuthError = ex;
            }
            catch (ArgumentException ex)
            {
                _logger.LogDebug(ex, "Skip unparsable beyond gacha URL candidate");
            }
        }

        if (lastAuthError is not null)
        {
            throw lastAuthError;
        }
        return null;
    }



    /// <summary>
    /// 通过 URL 获取 UID 并持久化到 GachaLogUrl（GameBiz=hk4eugc）。
    /// </summary>
    /// <param name="url">抽卡 URL。</param>
    /// <returns>UID；无记录时为 0。</returns>
    public virtual async Task<long> GetUidFromGachaLogUrl(string url)
    {
        long uid = await _client.GetUidByGachaUrlAsync(url);
        if (uid > 0)
        {
            using var dapper = DatabaseService.CreateConnection();
            dapper.Execute("INSERT OR REPLACE INTO GachaLogUrl (GameBiz, Uid, Url, Time) VALUES (@GameBiz, @Uid, @Url, @Time);", new GachaLogUrl("hk4eugc", uid, url));
        }
        return uid;
    }



    /// <summary>
    /// 按 UID 查询已保存的 Beyond 抽卡 URL。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>URL 或 null。</returns>
    public virtual string? GetGachaLogUrlByUid(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<string>("SELECT Url FROM GachaLogUrl WHERE Uid = @uid AND GameBiz = @GameBiz LIMIT 1;", new { uid, GameBiz = "hk4eugc" });
    }



    /// <summary>
    /// 删除本地保存的 Beyond 抽卡 URL。
    /// </summary>
    /// <param name="uid">可选 UID；为 null 时清除 hk4eugc 下全部 URL。</param>
    public void DeleteSavedGachaLogUrl(long? uid = null)
    {
        using var dapper = DatabaseService.CreateConnection();
        if (uid is > 0)
        {
            dapper.Execute("DELETE FROM GachaLogUrl WHERE Uid = @uid AND GameBiz = @GameBiz;", new { uid, GameBiz = "hk4eugc" });
        }
        else
        {
            dapper.Execute("DELETE FROM GachaLogUrl WHERE GameBiz = @GameBiz;", new { GameBiz = "hk4eugc" });
        }
    }



    private int InsertGachaLogItems(List<GenshinBeyondGachaItem> items)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        int count = dapper.Execute("""
            INSERT OR REPLACE INTO GenshinBeyondGachaItem(Uid, Id, Region, OpGachaType, ScheduleId, ItemType, ItemId, ItemName, RankType, IsUp, Time)
            VALUES (@Uid, @Id, @Region, @OpGachaType, @ScheduleId, @ItemType, @ItemId, @ItemName, @RankType, @IsUp, @Time);
            """, items, t);
        t.Commit();
        return count;
    }



    public virtual async Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var dapper = DatabaseService.CreateConnection();
        // 正在获取 uid
        progress?.Report(Lang.GachaLogService_GettingUid);
        var uid = await _client.GetUidByGachaUrlAsync(url);
        if (uid == 0)
        {
            // 该账号最近6个月没有抽卡记录
            progress?.Report(Lang.GachaLogService_ThisAccountHasNoGachaRecordsInTheLast6Months);
        }
        else
        {
            long endId = 0;
            if (!all)
            {
                endId = dapper.QueryFirstOrDefault<long>($"SELECT Id FROM {GachaTableName} WHERE Uid = @Uid ORDER BY Id DESC LIMIT 1;", new { Uid = uid });
                _logger.LogInformation($"Last gacha log id of uid {uid} is {endId}");
            }

            var internalProgress = new Progress<(int GachaType, int Page)>((x) => progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, x.GachaType == 1000 ? CoreLang.GachaType_StandardOde : CoreLang.GachaType_EventOde, x.Page)));
            var list = (await _client.GetGachaLogAsync(url, endId, lang, internalProgress, cancellationToken)).ToList();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException();
            }
            var oldCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
            InsertGachaLogItems(list);
            var newCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
            // 获取 {list.Count} 条记录，新增 {newCount - oldCount} 条记录
            progress?.Report(string.Format(Lang.GachaLogService_GetGachaResult, list.Count, newCount - oldCount));
            // 本次确有拉取到记录时，检测是否出现本地未收录的新物品，若有则静默联网补全物品信息（图标）。
            if (list.Count > 0 && HasUnknownItems(uid))
            {
                try
                {
                    await UpdateGachaInfoAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ensure beyond gacha info for unknown items failed, uid {uid}", uid);
                }
            }
        }
        return uid;
    }



    /// <summary>
    /// 卡池筛选可选的卡池分组：常驻颂愿、活动颂愿。
    /// </summary>
    public IReadOnlyCollection<IGachaType> QueryGachaTypes { get; } =
    [
        new GenshinBeyondGachaType(GenshinBeyondGachaType.StandardOde),
        new GenshinBeyondGachaType(GenshinBeyondGachaType.EventOde),
    ];



    /// <summary>
    /// 统计指定 UID 的常驻 / 活动颂愿，转成普通抽卡页同款 <see cref="GachaTypeStats"/>，让统计卡片、拖拽排序与分享图直接复用。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>有记录的卡池统计（常驻在前）；按物品汇总的出货次数，无记录时为 null。</returns>
    public (List<GachaTypeStats> TypeStats, List<GachaLogItemEx>? ItemStats) GetGachaTypeStats(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        // 活动颂愿各期 op_gacha_type 不同，统一并到 2000（与 UIGF 导出一致）
        var all = dapper.Query<GachaLogItemEx>($"""
            SELECT item.Uid, item.Id, item.ItemId, item.ItemName AS Name, item.ItemType, item.RankType, item.Time,
                   CASE WHEN item.OpGachaType = 1000 THEN 1000 ELSE 2000 END AS GachaType, info.Icon
            FROM {GachaTableName} item LEFT JOIN GenshinBeyondGachaInfo info ON item.ItemId = info.Id
            WHERE item.Uid = @uid ORDER BY item.Id;
            """, new { uid }).ToList();
        var statsList = new List<GachaTypeStats>();
        if (all.Count == 0)
        {
            return (statsList, null);
        }

        // 常驻颂愿最高只出 4★，保底与列表都按 4★ / 3★ 计
        if (BuildGachaTypeStats(all.Where(x => x.GachaType == GenshinBeyondGachaType.StandardOde).ToList(), GenshinBeyondGachaType.StandardOde, 4) is GachaTypeStats standard)
        {
            statsList.Add(standard);
        }
        if (BuildGachaTypeStats(all.Where(x => x.GachaType == GenshinBeyondGachaType.EventOde).ToList(), GenshinBeyondGachaType.EventOde, 5) is GachaTypeStats eventOde)
        {
            statsList.Add(eventOde);
        }

        var itemStats = all.GroupBy(x => x.ItemId)
                           .Select(x => { var item = x.First(); item.ItemCount = x.Count(); return item; })
                           .OrderByDescending(x => x.RankType)
                           .ThenByDescending(x => x.ItemCount)
                           .ThenByDescending(x => x.Time)
                           .ToList();
        return (statsList, itemStats);
    }



    /// <summary>
    /// 计算单个颂愿分组的统计，并回写每条记录的序号与抽数。
    /// </summary>
    /// <param name="list">该分组的记录（按 Id 升序）。</param>
    /// <param name="gachaType">分组：<see cref="GenshinBeyondGachaType.StandardOde"/> 或 <see cref="GenshinBeyondGachaType.EventOde"/>。</param>
    /// <param name="topRarity">该分组统计的最高星级（常驻 4，活动 5），保底均为 70 抽。</param>
    /// <returns>统计；无记录时为 null。</returns>
    private static GachaTypeStats? BuildGachaTypeStats(List<GachaLogItemEx> list, int gachaType, int topRarity)
    {
        if (list.Count == 0)
        {
            return null;
        }
        int secondRarity = topRarity - 1;

        int index = 0;
        int pity = 0;
        foreach (var item in list)
        {
            item.Index = ++index;
            item.Pity = ++pity;
            if (item.RankType == topRarity)
            {
                pity = 0;
            }
        }

        var stats = new GachaTypeStats
        {
            GachaType = gachaType,
            GachaTypeText = new GenshinBeyondGachaType(gachaType).ToLocalization(),
            TopRarity = topRarity,
            Pity_5_Max = 70,
            Count = list.Count,
            Count_5 = list.Count(x => x.RankType == topRarity),
            Count_4 = list.Count(x => x.RankType == secondRarity),
            Count_3 = list.Count(x => x.RankType == secondRarity - 1),
            StartTime = list[0].Time,
            EndTime = list[^1].Time,
        };
        stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
        stats.Ratio_4 = (double)stats.Count_4 / stats.Count;
        stats.Ratio_3 = (double)stats.Count_3 / stats.Count;
        stats.List_5 = list.Where(x => x.RankType == topRarity).Reverse().ToList();
        stats.List_4 = list.Where(x => x.RankType == secondRarity).Reverse().ToList();

        // 须在下面回写次一级抽数之前取：最后一条若是次一级，它的 Pity 会被改掉
        stats.Pity_5 = list[^1].RankType == topRarity ? 0 : list[^1].Pity;
        // 无最高星级样本时不计算平均，避免 NaN；展示侧用 Average_5_Text 显示「—」
        if (stats.Count_5 > 0)
        {
            stats.Average_5 = (double)(stats.Count - stats.Pity_5) / stats.Count_5;
        }
        stats.Pity_4 = list.Count - 1 - list.FindLastIndex(x => x.RankType == secondRarity);
        int pitySecond = 0;
        foreach (var item in list)
        {
            pitySecond++;
            if (item.RankType == secondRarity)
            {
                item.Pity = pitySecond;
                pitySecond = 0;
            }
        }
        return stats;
    }



    /// <summary>
    /// 指定 UID 全部记录的抽取时间，供按时间段删除对话框统计条数。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>抽取时间列表。</returns>
    public List<DateTime> GetGachaLogTimes(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<DateTime>($"SELECT Time FROM {GachaTableName} WHERE Uid = @uid;", new { uid }).ToList();
    }


    public virtual int DeleteUid(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid;", new { uid });
    }



    public virtual int DeleteGachaLogByTime(long uid, DateTime begin, DateTime end)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid AND Time >= @begin AND Time <= @end;", new { uid, begin, end });
    }



    /// <summary>
    /// 两次检查物品信息更新的最小间隔；有 ETag 时多为 304，开销很小。
    /// </summary>
    private static readonly TimeSpan GachaInfoCheckInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// 串行化物品信息更新：软件启动与打开页面可能同时触发。
    /// </summary>
    private readonly SemaphoreSlim _gachaInfoLock = new(1, 1);



    /// <summary>
    /// 立即联网检查千星奇域物品信息（图标）：对上次成功的数据源发送条件请求，未修改时不写库。
    /// 更新记录时发现本地未收录的新物品（<see cref="HasUnknownItems"/>）会调用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本地物品信息是否有变化。</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">所有数据源都不可用时抛出。</exception>
    public async Task<bool> UpdateGachaInfoAsync(CancellationToken cancellationToken = default)
    {
        await _gachaInfoLock.WaitAsync(cancellationToken);
        try
        {
            return await UpdateGachaInfoCoreAsync(cancellationToken);
        }
        finally
        {
            _gachaInfoLock.Release();
        }
    }



    /// <summary>
    /// 确保本地千星奇域物品信息（图标）可用且较新：表为空时立即下载，否则距上次检查满 <see cref="GachaInfoCheckInterval"/> 才联网检查。
    /// 由软件启动（<see cref="GachaItemNameService"/>）、打开千星奇域页面与 UIGF 导入调用；失败则下次调用时重试。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本地物品信息是否有变化。</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">需要联网但所有数据源都不可用时抛出。</exception>
    public async Task<bool> EnsureGachaInfoAsync(CancellationToken cancellationToken = default)
    {
        await _gachaInfoLock.WaitAsync(cancellationToken);
        try
        {
            // 在锁内判断：并发的第二次调用会看到第一次刚写入的检查时间而直接返回
            if (HasGachaInfo() && DateTimeOffset.Now - AppConfig.GenshinBeyondGachaInfoLastCheckTime < GachaInfoCheckInterval)
            {
                return false;
            }
            return await UpdateGachaInfoCoreAsync(cancellationToken);
        }
        finally
        {
            _gachaInfoLock.Release();
        }
    }



    /// <summary>
    /// 联网获取物品信息并写库；调用方须持有 <see cref="_gachaInfoLock"/>。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本地物品信息是否有变化。</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">所有数据源都不可用时抛出。</exception>
    private async Task<bool> UpdateGachaInfoCoreAsync(CancellationToken cancellationToken)
    {
        // 本地表为空时不发条件请求，确保拿到完整数据
        string? etag = HasGachaInfo() ? AppConfig.GenshinBeyondGachaInfoETag : null;
        var result = await _client.GetGenshinBeyondGachaInfoAsync(AppConfig.GenshinBeyondGachaInfoSource, etag, cancellationToken);
        AppConfig.GenshinBeyondGachaInfoLastCheckTime = DateTimeOffset.Now;
        if (result.NotModified)
        {
            _logger.LogInformation("Beyond gacha info not modified, source {Source}", result.SourceUrl);
            return false;
        }
        if (result.Items.Count > 0)
        {
            using var dapper = DatabaseService.CreateConnection();
            using var t = dapper.BeginTransaction();
            const string insertSql = """INSERT OR REPLACE INTO GenshinBeyondGachaInfo (Id, Name, Rank, Icon) VALUES (@Id, @Name, @Rank, @Icon);""";
            dapper.Execute(insertSql, result.Items, t);
            t.Commit();
        }
        AppConfig.GenshinBeyondGachaInfoSource = result.SourceUrl;
        AppConfig.GenshinBeyondGachaInfoETag = result.ETag;
        _logger.LogInformation("Beyond gacha info updated, source {Source}, {Count} items", result.SourceUrl, result.Items.Count);
        return result.Items.Count > 0;
    }



    /// <summary>
    /// 本地物品信息表（GenshinBeyondGachaInfo）是否已有数据。
    /// </summary>
    /// <returns>有数据为 true。</returns>
    private static bool HasGachaInfo()
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM GenshinBeyondGachaInfo;") > 0;
    }



    /// <summary>该 UID 是否存在本地物品信息表（GenshinBeyondGachaInfo）收录不到的记录（缺图标的新物品）。</summary>
    private bool HasUnknownItems(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<int>("""
            SELECT EXISTS(
                SELECT 1 FROM GenshinBeyondGachaItem item
                LEFT JOIN GenshinBeyondGachaInfo info ON item.ItemId = info.Id
                WHERE item.Uid = @uid AND info.Id IS NULL
            );
            """, new { uid }) == 1;
    }


}
