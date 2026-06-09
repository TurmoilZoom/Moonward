using Dapper;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;


/// <summary>
/// 抽卡记录服务抽象基类。
/// 为原神（Genshin）、星穹铁道（Star Rail）、绝区零（ZZZ）等游戏提供统一的抽卡记录获取、持久化、统计、导入导出能力。
/// 具体游戏通过派生类实现卡池类型映射、数据库表名、按查询类型分组等游戏特有逻辑。
/// 内部依赖 GachaLogClient（Core 层）负责通过游戏内网页缓存 URL 调用官方 API，
/// 并使用 Dapper + SQLite 进行本地存储与增量同步。
/// </summary>
internal abstract class GachaLogService
{


    /// <summary>日志记录器，用于记录获取进度、最后 Id 等关键信息。</summary>
    protected readonly ILogger<GachaLogService> _logger;


    /// <summary>底层抽卡客户端，负责解析 URL 并分页调用官方 gacha log API。</summary>
    protected readonly GachaLogClient _client;


    /// <summary>
    /// 初始化 GachaLogService 基类实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="client">抽卡记录客户端（GenshinGachaClient / StarRailGachaClient / ZZZGachaClient 等）。</param>
    protected GachaLogService(ILogger<GachaLogService> logger, GachaLogClient client)
    {
        _logger = logger;
        _client = client;
    }



    /// <summary>当前游戏业务标识（GameBiz.hk4e / hkrpg / nap 等），由派生类提供。</summary>
    protected abstract GameBiz CurrentGameBiz { get; }

    /// <summary>当前游戏对应的数据库表名（如 "GenshinGachaItem" / "StarRailGachaItem" / "ZZZGachaItem"）。</summary>
    protected abstract string GachaTableName { get; }

    /// <summary>
    /// 根据卡池查询类型（IGachaType）从完整记录列表中筛选出属于该类型（或该类型组）的记录。
    /// 例如原神需要把 301+400 合并统计，ZZZ 各频段独立。
    /// </summary>
    /// <param name="items">已加载的完整增强抽卡记录列表。</param>
    /// <param name="type">目标卡池类型。</param>
    /// <returns>筛选后的记录列表（用于 pity 计算和统计）。</returns>
    protected abstract List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type);

    /// <summary>
    /// 当前游戏支持的全部卡池类型集合（来自底层客户端）。
    /// 例如原神：常驻、角色活动、武器活动、新手等。
    /// </summary>
    public IReadOnlyCollection<IGachaType> QueryGachaTypes => _client.QueryGachaTypes;



    /// <summary>
    /// 根据游戏业务线返回本地化的“抽卡记录”文案。
    /// </summary>
    /// <param name="biz">游戏业务线（hk4e/hkrpg/nap）。</param>
    /// <returns>
    /// 原神返回“祈愿记录”，星铁返回“跃迁记录”，绝区零返回“调频记录”，其他返回空字符串。
    /// </returns>
    public static string GetGachaLogText(GameBiz biz)
    {
        return biz.ToGame().Value switch
        {
            GameBiz.hk4e => Lang.GachaLogService_WishRecords,
            GameBiz.hkrpg => Lang.GachaLogService_WarpRecords,
            GameBiz.nap => Lang.GachaLogService_SignalSearchRecords,
            _ => ""
        };
    }



    /// <summary>
    /// 获取当前游戏所有拥有抽卡记录的 UID 列表（去重）。
    /// </summary>
    /// <returns>UID 列表，按数据库 DISTINCT 查询顺序返回。</returns>
    public virtual List<long> GetUids()
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<long>($"SELECT DISTINCT Uid FROM {GachaTableName};").ToList();
    }



    /// <summary>
    /// 获取指定 UID 的完整抽卡记录，并计算每条记录的 Index（序号）和 Pity（保底进度）。
    /// 计算逻辑：遍历各卡池类型，连续计数，5星（RankType==5）后 pity 重置为 0。
    /// 注意：此方法返回的基础版本，派生类（尤其是 ZZZ）会重写以关联 Icon、处理非UP标记、调整 RankType 含义。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>按 Id 升序排列的增强记录列表（已填充 Index、Pity 字段）。</returns>
    public virtual List<GachaLogItemEx> GetGachaLogItemEx(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GachaLogItemEx>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }).ToList();
        foreach (IGachaType type in QueryGachaTypes)
        {
            var l = GetGachaLogItemsByQueryType(list, type);
            int index = 0;
            int pity = 0;
            foreach (var item in l)
            {
                item.Index = ++index;
                item.Pity = ++pity;
                if (item.RankType == 5)
                {
                    pity = 0;
                }
            }
        }
        return list;
    }



    /// <summary>
    /// 从游戏安装目录的网页缓存文件中尝试提取抽卡记录 URL。
    /// 实际委托给 <see cref="GachaLogClient.GetGachaUrlFromWebCache"/>。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="path">游戏安装根目录路径（可为空，内部会尝试常见位置）。</param>
    /// <returns>成功提取到的 gacha log URL；未找到或文件不存在时返回 null。</returns>
    public virtual string? GetGachaLogUrlFromWebCache(GameBiz gameBiz, string path)
    {
        return GachaLogClient.GetGachaUrlFromWebCache(gameBiz, path);
    }




    /// <summary>
    /// 通过抽卡记录 URL 调用官方接口获取当前玩家 UID，并将该 URL 持久化到本地 GachaLogUrl 表（用于后续快速获取）。
    /// </summary>
    /// <param name="url">从网页缓存提取的有效抽卡记录 URL（包含 authkey 等参数）。</param>
    /// <returns>成功获取到的 UID；若接口未返回任何记录则返回 0。</returns>
    public virtual async Task<long> GetUidFromGachaLogUrl(string url)
    {
        long uid = await _client.GetUidByGachaUrlAsync(url);
        if (uid > 0)
        {
            using var dapper = DatabaseService.CreateConnection();
            dapper.Execute("INSERT OR REPLACE INTO GachaLogUrl (GameBiz, Uid, Url, Time) VALUES (@GameBiz, @Uid, @Url, @Time);", new GachaLogUrl(CurrentGameBiz, uid, url));
        }
        return uid;
    }



    /// <summary>
    /// 根据 UID 从本地数据库查询最近一次使用的抽卡记录 URL（按 GameBiz 过滤）。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>之前保存的 URL；不存在时返回 null。</returns>
    public virtual string? GetGachaLogUrlByUid(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<string>("SELECT Url FROM GachaLogUrl WHERE Uid = @uid AND GameBiz = @GameBiz LIMIT 1;", new { uid, GameBiz = CurrentGameBiz });
    }



    /// <summary>
    /// 将一批抽卡记录批量插入（或 REPLACE）到当前游戏的抽卡表中。
    /// 由派生类实现具体表结构和事务逻辑，并可能触发额外处理（如 UpdateGachaItemId）。
    /// </summary>
    /// <param name="items">从客户端获取的原始抽卡记录列表。</param>
    /// <returns>受影响的行数。</returns>
    protected abstract int InsertGachaLogItems(List<GachaLogItem> items);



    /// <summary>
    /// 核心方法：通过网页缓存中的抽卡记录 URL，从官方接口拉取记录并增量写入本地数据库。
    /// 支持全量（all=true）或增量同步（使用本地最新 Id 作为 endId 起点）。
    /// 内部会先获取 UID，再决定是否分页拉取，最后报告获取结果。
    /// </summary>
    /// <param name="url">有效的抽卡记录 URL（必须包含 authkey 等必要参数）。</param>
    /// <param name="all">是否拉取全部历史记录。false 时仅获取本地最新 Id 之后的记录，实现增量更新。</param>
    /// <param name="lang">可选语言代码，影响返回记录中物品名称的本地化。</param>
    /// <param name="progress">进度报告回调，用于 UI 显示“正在获取 UID”、“正在获取 角色活动祈愿 第 3 页”等信息。</param>
    /// <param name="cancellationToken">取消令牌，支持中途取消拉取。</param>
    /// <returns>成功处理的玩家 UID；若无法获取 UID 或无记录则返回 0。</returns>
    /// <exception cref="TaskCanceledException">当 cancellationToken 被触发时抛出。</exception>
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

            var internalProgress = new Progress<(IGachaType GachaType, int Page)>((x) => progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, x.GachaType.ToLocalization(), x.Page)));
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
        }
        return uid;
    }






    /// <summary>
    /// 计算指定 UID 的各卡池统计数据（用于统计卡片展示）和物品汇总数据。
    /// 统计内容包括：出货数量、5星/4星/3星数量及出率、平均出货抽数、当前 pity、5星列表、4星列表等。
    /// 同时会为每个卡池在 List_5 / List_4 开头插入一个“保底”占位项（显示当前距离上一个5星/4星的抽数）。
    /// 新手池/始发池达到固定次数后不会插入该占位。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>
    /// 元组：
    /// - GachaStats: 各卡池的统计信息列表（GachaTypeStats）。
    /// - ItemStats: 按 ItemId 分组的物品汇总列表（含 ItemCount），按稀有度、数量、时间排序。
    /// </returns>
    public virtual (List<GachaTypeStats> GachaStats, List<GachaLogItemEx> ItemStats) GetGachaTypeStats(long uid)
    {
        var statsList = new List<GachaTypeStats>();
        var groupStats = new List<GachaLogItemEx>();
        using var dapper = DatabaseService.CreateConnection();
        var allItems = GetGachaLogItemEx(uid);
        if (allItems.Count > 0)
        {
            foreach (IGachaType type in QueryGachaTypes)
            {
                var list = GetGachaLogItemsByQueryType(allItems, type);
                if (list.Count == 0)
                {
                    continue;
                }
                var stats = new GachaTypeStats
                {
                    GachaType = type.Value,
                    GachaTypeText = type.ToLocalization(),
                    Count = list.Count,
                    Count_5_Up = list.Count(x => x.RankType == 5 && x.IsUp),
                    Count_5 = list.Count(x => x.RankType == 5),
                    Count_4 = list.Count(x => x.RankType == 4),
                    Count_3 = list.Count(x => x.RankType == 3),
                    StartTime = list.First().Time,
                    EndTime = list.Last().Time
                };
                stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
                stats.Ratio_4 = (double)stats.Count_4 / stats.Count;
                stats.Ratio_3 = (double)stats.Count_3 / stats.Count;
                stats.List_5 = list.Where(x => x.RankType == 5).Reverse().ToList();
                stats.List_4 = list.Where(x => x.RankType == 4).Reverse().ToList();
                stats.Pity_5 = list.Last().Pity;
                if (list.Last().RankType == 5)
                {
                    stats.Pity_5 = 0;
                }
                stats.Average_5 = (double)(stats.Count - stats.Pity_5) / stats.Count_5;
                stats.Pity_4 = list.Count - 1 - list.FindLastIndex(x => x.RankType == 4);

                if (stats.Count_5_Up > 0)
                {
                    int c = stats.Count - stats.Pity_5;
                    stats.Average_5_Up = (double)c / stats.Count_5_Up;
                }

                int pity_4 = 0;
                foreach (var item in list)
                {
                    pity_4++;
                    if (item.RankType == 4)
                    {
                        item.Pity = pity_4;
                        pity_4 = 0;
                    }
                }

                statsList.Add(stats);
                if (CurrentGameBiz == GameBiz.hk4e && type.Value == GenshinGachaType.NoviceWish && stats.Count == 20)
                {
                    continue;
                }
                else if (CurrentGameBiz == GameBiz.hkrpg && type.Value == StarRailGachaType.DepartureWarp && stats.Count == 50)
                {
                    continue;
                }
                else
                {
                    stats.List_5.Insert(0, new GachaLogItemEx
                    {
                        GachaType = type.Value,
                        Name = Lang.GachaStatsCard_Pity,
                        Pity = stats.Pity_5,
                        Time = list.Last().Time,
                        HasUpItem = GachaNoUp.Dictionary.TryGetValue($"{CurrentGameBiz}{type.Value}", out _),
                    });
                    stats.List_4.Insert(0, new GachaLogItemEx
                    {
                        GachaType = type.Value,
                        Name = Lang.GachaStatsCard_Pity,
                        Pity = stats.Pity_4,
                        Time = list.Last().Time,
                        HasUpItem = GachaNoUp.Dictionary.TryGetValue($"{CurrentGameBiz}{type.Value}", out _),
                    });
                }
            }
            groupStats = allItems.GroupBy(x => x.ItemId)
                                 .Select(x => { var item = x.First(); item.ItemCount = x.Count(); return item; })
                                 .OrderByDescending(x => x.RankType)
                                 .ThenByDescending(x => x.ItemCount)
                                 .ThenByDescending(x => x.Time)
                                 .ToList();
        }
        return (statsList, groupStats);
    }






    /// <summary>
    /// 删除指定 UID 在当前游戏下的所有抽卡记录。
    /// </summary>
    /// <param name="uid">要删除的玩家 UID。</param>
    /// <returns>受影响的行数（删除的记录条数）。</returns>
    public virtual int DeleteUid(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid;", new { uid });
    }



    /// <summary>
    /// 按时间区间删除指定 UID 的部分抽卡记录（包含边界）。
    /// 常用于“删除选定时间范围内的记录”功能。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <param name="begin">起始时间（含）。</param>
    /// <param name="end">结束时间（含）。</param>
    /// <returns>受影响的行数（删除的记录条数）。</returns>
    public virtual int DeleteGachaLogByTime(long uid, DateTime begin, DateTime end)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid AND Time >= @begin AND Time <= @end;", new { uid, begin, end });
    }



    /// <summary>
    /// 导出指定 UID 的抽卡记录到文件。
    /// 支持的格式由派生类决定（通常包括 "excel" 和 UIGF JSON）。
    /// </summary>
    /// <param name="uid">要导出的玩家 UID。</param>
    /// <param name="file">目标文件完整路径。</param>
    /// <param name="format">导出格式标识（如 "excel" 或其他表示 JSON）。</param>
    /// <returns>异步任务。</returns>
    public abstract Task ExportGachaLogAsync(long uid, string file, string format);




    /// <summary>
    /// 从 UIGF（或兼容格式）文件导入抽卡记录。
    /// 派生类负责解析具体格式、补全 Lang/Uid 字段、调用 InsertGachaLogItems，并显示导入成功提示。
    /// </summary>
    /// <param name="file">导入文件完整路径（JSON）。</param>
    /// <returns>成功导入记录所属的 UID；解析失败或无数据时返回 0。</returns>
    public abstract long ImportGachaLog(string file);




    /// <summary>
    /// 从官方接口拉取最新角色/武器/音擎等信息（名称、图标、稀有度等），更新到对应的 GachaInfo 表。
    /// 用于后续修正历史记录的本地化名称。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="lang">期望的语言代码（如 zh-cn、en-us）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际使用的语言代码（服务器返回的 Language）。</returns>
    public abstract Task<string> UpdateGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default);



    /// <summary>
    /// 先调用 UpdateGachaInfoAsync 更新信息表，再用标准名称回写所有历史记录的 Name 字段（按 ItemId 关联）。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="lang">期望的语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>元组：(实际使用的语言, 受影响的抽卡记录条数)。</returns>
    public abstract Task<(string Language, int Count)> ChangeGachaItemNameAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default);


}
