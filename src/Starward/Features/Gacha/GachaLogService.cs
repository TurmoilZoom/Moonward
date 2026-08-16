using Dapper;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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

    /// <summary>当前游戏的物品信息表名（"GenshinGachaInfo" / "StarRailGachaInfo" / "ZZZGachaInfo"），提供图标与 ItemId 映射。</summary>
    protected abstract string GachaInfoTableName { get; }

    /// <summary>物品信息表里与 GachaItem.ItemId 关联的主键列名（原神/绝区零为 "Id"，星铁为 "ItemId"）。</summary>
    protected abstract string GachaInfoIdColumn { get; }

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
    /// 获取当前游戏所有拥有抽卡记录的 UID 列表（去重，排除 0）。
    /// </summary>
    /// <returns>UID 列表，按数据库 DISTINCT 查询顺序返回。</returns>
    public virtual List<long> GetUids()
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<long>($"SELECT DISTINCT Uid FROM {GachaTableName} WHERE Uid > 0;").ToList();
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
    /// 从网页缓存提取多个候选 URL，并依次用官方接口校验 authkey，返回第一个有效的 URL。
    /// 全部候选因 authkey 过期失败时抛出最后一次 <see cref="GachaApiException"/>，供 UI 引导清理缓存。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="path">游戏安装根目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验通过的 URL；无任何候选时返回 null。</returns>
    /// <exception cref="GachaApiException">所有候选均 authkey 超时时抛出。</exception>
    public virtual async Task<string?> GetValidatedGachaLogUrlFromWebCacheAsync(GameBiz gameBiz, string path, CancellationToken cancellationToken = default)
    {
        var candidates = GachaLogClient.GetGachaUrlCandidatesFromWebCache(gameBiz, path);
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
                // uid==0 仍表示 authkey 有效（近 6 个月无记录）；过期会抛 GachaApiException
                await _client.GetUidByGachaUrlAsync(url);
                return url;
            }
            catch (GachaApiException ex) when (ex.IsAuthkeyExpired)
            {
                _logger.LogInformation("Gacha authkey candidate expired, try next: {Message}", ex.Message);
                lastAuthError = ex;
            }
            catch (ArgumentException ex)
            {
                _logger.LogDebug(ex, "Skip unparsable gacha URL candidate");
            }
        }

        if (lastAuthError is not null)
        {
            throw lastAuthError;
        }
        return null;
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
            SaveGachaLogUrl(uid, url);
        }
        return uid;
    }


    /// <summary>
    /// 将抽卡记录 URL 按 UID 持久化到本地（不发起网络请求）。
    /// 用于米游社 genAuthKey 同步成功后缓存 URL，避免重复请求。
    /// </summary>
    /// <param name="uid">玩家 UID；须 &gt; 0。</param>
    /// <param name="url">含 authkey 的抽卡 API/页面 URL。</param>
    public virtual void SaveGachaLogUrl(long uid, string url)
    {
        if (uid <= 0 || string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("INSERT OR REPLACE INTO GachaLogUrl (GameBiz, Uid, Url, Time) VALUES (@GameBiz, @Uid, @Url, @Time);", new GachaLogUrl(CurrentGameBiz, uid, url));
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
    /// 删除本地保存的抽卡 URL（清理过期 authkey）。
    /// </summary>
    /// <param name="uid">可选 UID；为 null 时清除当前 GameBiz 下全部 URL。</param>
    public virtual void DeleteSavedGachaLogUrl(long? uid = null)
    {
        using var dapper = DatabaseService.CreateConnection();
        if (uid is > 0)
        {
            dapper.Execute("DELETE FROM GachaLogUrl WHERE Uid = @uid AND GameBiz = @GameBiz;", new { uid, GameBiz = CurrentGameBiz });
        }
        else
        {
            dapper.Execute("DELETE FROM GachaLogUrl WHERE GameBiz = @GameBiz;", new { GameBiz = CurrentGameBiz });
        }
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
    /// 落库后按软件语言回写物品名称（接口 lang 未生效或返回语言不一致时的兜底）。
    /// </summary>
    /// <param name="url">有效的抽卡记录 URL（必须包含 authkey 等必要参数）。</param>
    /// <param name="all">是否拉取全部历史记录。false 时仅获取本地最新 Id 之后的记录，实现增量更新。</param>
    /// <param name="lang">可选语言代码，影响返回记录中物品名称的本地化，并作为落库后名称回写目标语言。</param>
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
            if (list.Count > 0)
            {
                await EnsureLocalizedNamesAfterInsertAsync(uid, lang, cancellationToken);
            }
        }
        return uid;
    }



    /// <summary>
    /// 抽卡记录落库后的名称本地化兜底：先补未知物品信息（图标 + 名称缓存），再按目标语言从
    /// <c>GachaItemName</c> 回写该 UID 全部记录的 <c>Name</c>。
    /// <para>用于 URL 更新、战绩同步等路径——接口可能已带 lang，但 URL 缺 lang、服务端忽略语言、
    /// 或国服战绩固定中文时，仍保证展示跟随软件 UI 语言。失败仅记日志，不阻断已落库记录。</para>
    /// </summary>
    /// <param name="uid">玩家 UID；为 0 时直接返回。</param>
    /// <param name="lang">目标语言；为空时取当前 UI 语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected async Task EnsureLocalizedNamesAfterInsertAsync(long uid, string? lang, CancellationToken cancellationToken = default)
    {
        if (uid == 0)
        {
            return;
        }
        string displayLanguage = LanguageUtil.FilterLanguage(
            string.IsNullOrWhiteSpace(lang)
                ? CultureInfo.CurrentUICulture.Name
                : lang);
        await EnsureGachaInfoForUnknownItemsAsync(uid, displayLanguage, cancellationToken);
        try
        {
            await EnsureNameCacheAsync(displayLanguage, cancellationToken);
            RewriteRecordNamesFromCache(displayLanguage, uid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rewrite gacha item names after insert failed, game {Game}, uid {Uid}, lang {Lang}", GameKey, uid, displayLanguage);
        }
    }






    /// <summary>
    /// 计算指定 UID 的各卡池统计数据（用于统计卡片展示）和物品汇总数据。
    /// 统计内容包括：出货数量、5星/4星/3星数量及出率、平均出货抽数、当前 pity、保底状态、5星列表、4星列表等。
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
                    EndTime = list.Last().Time,
                    Pity_5_Max = GetPityLimit(type),
                    ShowPityProgress = ShouldShowPityProgress(type, list.Count),
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
                // 无 5 星样本时不计算平均，避免 NaN；展示侧用 Average_5_Text 显示「—」
                if (stats.Count_5 > 0)
                {
                    stats.Average_5 = (double)(stats.Count - stats.Pity_5) / stats.Count_5;
                }
                stats.Pity_4 = list.Count - 1 - list.FindLastIndex(x => x.RankType == 4);

                if (stats.Count_5_Up > 0)
                {
                    int c = stats.Count - stats.Pity_5;
                    stats.Average_5_Up = (double)c / stats.Count_5_Up;
                }

                // 「不歪概率」：统计小保底抽到当期 UP 的情况；角色池通常为 50/50，原神武器池为 75/25。
                stats.HasUpItem = GachaNoUp.Dictionary.ContainsKey($"{CurrentGameBiz}{type.Value}");
                if (stats.HasUpItem)
                {
                    (stats.FiftyFiftyCount, stats.FiftyFiftyNoUpCount, stats.MaxFiftyFiftyUpStreak, stats.MaxFiftyFiftyMissStreak, stats.IsNextPityGuaranteed) = CountFiftyFiftyNoUp(list, 5);
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
    /// 获取指定卡池最高稀有度的硬保底抽数。
    /// </summary>
    /// <param name="gachaType">当前游戏下的卡池类型。</param>
    /// <returns>武器、光锥、音擎及邦布卡池返回 80，其余卡池返回 90。</returns>
    protected int GetPityLimit(IGachaType gachaType)
    {
        if ((CurrentGameBiz == GameBiz.hk4e && gachaType.Value == GenshinGachaType.WeaponEventWish)
            || (CurrentGameBiz == GameBiz.hkrpg && (gachaType.Value is StarRailGachaType.LightConeEventWarp or StarRailGachaType.LightConeCollaborationWarp))
            || (CurrentGameBiz == GameBiz.nap && (gachaType.Value is ZZZGachaType.WEngineChannel or ZZZGachaType.WEngineReverberation or ZZZGachaType.BangbooChannel)))
        {
            return 80;
        }
        return 90;
    }


    /// <summary>
    /// 判断指定卡池是否应显示当前最高稀有度垫数进度。
    /// </summary>
    /// <param name="gachaType">当前游戏下的卡池类型。</param>
    /// <param name="count">该卡池已有的抽卡记录总数。</param>
    /// <returns>已抽满的一次性新手池返回 false，其余卡池返回 true。</returns>
    protected bool ShouldShowPityProgress(IGachaType gachaType, int count)
    {
        return !(CurrentGameBiz == GameBiz.hk4e && gachaType.Value == GenshinGachaType.NoviceWish && count == 20)
            && !(CurrentGameBiz == GameBiz.hkrpg && gachaType.Value == StarRailGachaType.DepartureWarp && count == 50);
    }


    /// <summary>
    /// 统计小保底抽到当期 UP 的情况。
    /// 按时间正序遍历卡池记录，维护「下一个最高稀有度是否为大保底」的状态：
    /// 在非大保底状态下抽出的最高稀有度记为一次小保底，其中 <see cref="GachaLogItemEx.IsUp"/> 为 true 的记为一次「不歪」；
    /// 若小保底歪了（非 UP），则下一个最高稀有度为大保底（必出 UP），不计入小保底统计。
    /// </summary>
    /// <param name="orderedList">按时间（Id）正序排列的卡池全部记录。</param>
    /// <param name="highestRankType">最高稀有度对应的 RankType（原神/星铁为 5，绝区零 S 级为 4）。</param>
    /// <returns>(小保底次数, 小保底不歪次数, 最多连续不歪次数, 最多连续歪次数, 下一次是否为大保底)。</returns>
    protected static (int Count, int NoUpCount, int MaxUpStreak, int MaxMissStreak, bool IsNextGuaranteed) CountFiftyFiftyNoUp(IEnumerable<GachaLogItemEx> orderedList, int highestRankType)
    {
        int count = 0;
        int noUpCount = 0;
        int upStreak = 0;
        int missStreak = 0;
        int maxUpStreak = 0;
        int maxMissStreak = 0;
        bool guaranteed = false;
        foreach (GachaLogItemEx item in orderedList)
        {
            if (item.RankType != highestRankType)
            {
                continue;
            }
            if (guaranteed)
            {
                // 大保底必出 UP，不计入小保底统计，也不打断连胜/连歪计数
                guaranteed = false;
            }
            else
            {
                count++;
                if (item.IsUp)
                {
                    noUpCount++;
                    upStreak++;
                    missStreak = 0;
                    maxUpStreak = Math.Max(maxUpStreak, upStreak);
                }
                else
                {
                    missStreak++;
                    upStreak = 0;
                    maxMissStreak = Math.Max(maxMissStreak, missStreak);
                    guaranteed = true;
                }
            }
        }
        return (count, noUpCount, maxUpStreak, maxMissStreak, guaranteed);
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
    /// <param name="onlyIfNewItems">
    /// 为 true 时先比对：仅当出现本地信息表未收录的新角色/物品，或当前语言名称缓存尚不存在时才写入；
    /// 否则跳过写入（用于导航到游戏时「有新内容才更新」的轻量刷新）。为 false（默认）时无条件写入。
    /// </param>
    /// <returns>实际使用的语言代码（服务器返回的 Language）。</returns>
    public abstract Task<string> UpdateGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default, bool onlyIfNewItems = false);



    /// <summary>当前游戏短键（"hk4e"/"hkrpg"/"nap"），作为多语言名称缓存表 GachaItemName 的 Game 列。</summary>
    protected string GameKey => CurrentGameBiz.Game;



    /// <summary>
    /// 把一批 (ItemId, Name) 写入多语言名称缓存表 GachaItemName（按当前游戏 + 规整后的语言）。
    /// 由各派生类在 <see cref="UpdateGachaInfoAsync"/> 下载 wiki 后调用，使软件可同时缓存多种语言的名称映射。
    /// </summary>
    /// <param name="items">物品 (ItemId, Name) 序列；ItemId 为 0 或名称为空的项会被忽略。</param>
    /// <param name="lang">语言代码（内部会用 <see cref="LanguageUtil.FilterLanguage"/> 规整）。</param>
    protected void SaveItemNamesToCache(IEnumerable<(long ItemId, string Name)> items, string lang)
    {
        lang = LanguageUtil.FilterLanguage(lang);
        string game = GameKey;
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        dapper.Execute("""
            INSERT OR REPLACE INTO GachaItemName (Game, ItemId, Lang, Name) VALUES (@Game, @ItemId, @Lang, @Name);
            """,
            items.Where(x => x.ItemId != 0 && !string.IsNullOrEmpty(x.Name))
                 .Select(x => new { Game = game, x.ItemId, Lang = lang, x.Name }),
            t);
        t.Commit();
    }



    /// <summary>本地是否已缓存指定语言（规整后）的名称映射。</summary>
    /// <param name="lang">语言代码。</param>
    /// <returns>已有缓存返回 true。</returns>
    protected bool HasNameCache(string lang)
    {
        lang = LanguageUtil.FilterLanguage(lang);
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<int>(
            "SELECT COUNT(*) FROM GachaItemName WHERE Game = @Game AND Lang = @Lang;",
            new { Game = GameKey, Lang = lang }) > 0;
    }



    /// <summary>
    /// 确保本地已缓存指定语言的名称映射；缺失则联网下载（<see cref="UpdateGachaInfoAsync"/> 同时刷新 GachaInfo 图标与名称缓存）。
    /// </summary>
    /// <param name="lang">语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task EnsureNameCacheAsync(string lang, CancellationToken cancellationToken = default)
    {
        lang = LanguageUtil.FilterLanguage(lang);
        if (!HasNameCache(lang))
        {
            await UpdateGachaInfoAsync(CurrentGameBiz, lang, cancellationToken);
        }
    }



    /// <summary>
    /// 比对：传入的物品 Id 集合中是否存在本地物品信息表（<see cref="GachaInfoTableName"/>）尚未收录的新 Id。
    /// </summary>
    /// <param name="incomingIds">联网获取到的全部角色/物品 Id（0 会被忽略）。</param>
    /// <returns>存在本地没有的新 Id 返回 true。</returns>
    protected bool HasNewInfoItems(IEnumerable<long> incomingIds)
    {
        var ids = incomingIds.Where(x => x != 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return false;
        }
        using var dapper = DatabaseService.CreateConnection();
        var existing = dapper.Query<long>($"SELECT {GachaInfoIdColumn} FROM {GachaInfoTableName};").ToHashSet();
        return ids.Any(id => !existing.Contains(id));
    }



    /// <summary>
    /// 比对：导航刷新时是否需要写入信息表/名称缓存。满足任一即需写入：
    /// 当前语言的名称缓存尚不存在（首次使用/换语言后未下载），或出现本地未收录的新角色/物品。
    /// </summary>
    /// <param name="lang">服务器实际返回的语言代码。</param>
    /// <param name="incomingIds">联网获取到的全部角色/物品 Id。</param>
    /// <returns>需要写入返回 true。</returns>
    protected bool ShouldWriteGachaInfo(string lang, IEnumerable<long> incomingIds)
    {
        return !HasNameCache(lang) || HasNewInfoItems(incomingIds);
    }



    /// <summary>
    /// 导航到该游戏（切换游戏 / 打开抽卡页）时调用：联网获取全部角色/物品信息并与本地信息表比对，
    /// 仅当出现新角色/新物品（或当前语言名称缓存缺失）时，才更新物品信息表（GachaInfo）与多语言名称缓存（GachaItemName）。
    /// </summary>
    /// <param name="lang">目标语言（跟随软件 UI 语言；内部会规整）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task RefreshGachaInfoIfNewItemsAsync(string lang, CancellationToken cancellationToken = default)
    {
        return UpdateGachaInfoAsync(CurrentGameBiz, lang, cancellationToken, onlyIfNewItems: true);
    }



    /// <summary>
    /// 指定 UID 是否存在「本地物品信息表里查不到」的记录（缺图标的新角色/物品；原神/星铁亦含 ItemId 仍为 0 的记录）。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>存在未收录记录返回 true。</returns>
    protected bool HasUnknownGachaItems(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<int>($"""
            SELECT EXISTS(
                SELECT 1 FROM {GachaTableName} item
                LEFT JOIN {GachaInfoTableName} info ON item.ItemId = info.{GachaInfoIdColumn}
                WHERE item.Uid = @uid AND info.{GachaInfoIdColumn} IS NULL
            );
            """, new { uid }) == 1;
    }



    /// <summary>
    /// 拉取记录后调用：若该 UID 出现本地未收录的新角色/物品，则静默联网刷新本游戏物品信息表
    /// 
    /// （<see cref="UpdateGachaInfoAsync"/> 会写入图标 + 多语言名称缓存，原神/星铁内部还会回填 ItemId），
    /// 使本次新增记录立即能正确显示图标与名称。
    /// 静默、容错：任何失败（含取消、网络错误）仅记日志，不影响已落库的抽卡记录。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <param name="lang">目标语言（跟随软件 UI 语言）；为空时取当前 UI 语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected async Task EnsureGachaInfoForUnknownItemsAsync(long uid, string? lang, CancellationToken cancellationToken = default)
    {
        try
        {
            if (uid == 0 || !HasUnknownGachaItems(uid))
            {
                return;
            }
            string language = string.IsNullOrWhiteSpace(lang)
                ? CultureInfo.CurrentUICulture.Name
                : lang;
            await UpdateGachaInfoAsync(CurrentGameBiz, language, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ensure gacha info for unknown items failed, uid {uid}", uid);
        }
    }



    /// <summary>
    /// 回写抽卡记录名称
    /// 
    /// 把当前游戏所有抽卡记录的名称按 ItemId 回写为指定语言（取自多语言缓存 GachaItemName）。
    /// 缺失该语言缓存时先联网下载；并按名称跨语言回填旧记录缺失的 ItemId（ZZZ 记录恒带 ItemId，回填为空操作）。
    /// 按 UID 分批回写以平滑上报进度。
    /// 
    /// </summary>
    /// <param name="lang">目标语言代码（跟随软件 UI 语言；内部会规整）。</param>
    /// <param name="progress">进度回调（已处理/总数/是否完成）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响的记录条数。</returns>
    public virtual async Task<int> ApplyGachaItemNamesAsync(string lang, IProgress<GachaNameProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        lang = LanguageUtil.FilterLanguage(lang);
        //某个游戏的抽卡记录总数
        int total;
        using (var counter = DatabaseService.CreateConnection())
        {
            total = counter.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName};");
        }

        // 处理进度条
        if (total > 0)
        {
            progress?.Report(new GachaNameProgress(0, total, false));
        }

        //确保语言包存在
        await EnsureNameCacheAsync(lang, cancellationToken);

        //兼容旧记录无 ItemId 的情况
        BackfillItemIdByNameFromCache();
        if (total == 0)
        {
            progress?.Report(new GachaNameProgress(0, 0, true));
            return 0;
        }

        using var dapper = DatabaseService.CreateConnection();
        var groups = dapper.Query<GachaUidCount>($"SELECT Uid, COUNT(*) AS Count FROM {GachaTableName} GROUP BY Uid;").ToList();
        int done = 0;
        int changed = 0;

        // 按 UID 分批回写名称，同时推送进度
        foreach (GachaUidCount group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed += RewriteRecordNamesFromCache(lang, group.Uid);
            done += group.Count;
            progress?.Report(new GachaNameProgress(Math.Min(done, total), total, false));
        }
        progress?.Report(new GachaNameProgress(total, total, true));
        return changed;
    }



    /// <summary>
    /// 按 ItemId 从多语言缓存 GachaItemName 把指定 UID 的记录名称回写为指定语言（派生类实现：列清单各游戏不同）。
    /// </summary>
    /// <param name="lang">规整后的语言代码。</param>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>受影响条数。</returns>
    protected abstract int RewriteRecordNamesFromCache(string lang, long uid);



    /// <summary>
    /// 按名称跨语言匹配，为 ItemId 为 0 的旧记录回填 ItemId（解决任意语言导入的旧文件无 item_id 问题）。
    /// ZZZ 记录恒带 ItemId，此操作匹配不到行，为空操作。
    /// </summary>
    protected void BackfillItemIdByNameFromCache()
    {
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute($"""
            UPDATE {GachaTableName}
            SET ItemId = (SELECT ItemId FROM GachaItemName WHERE Game = @Game AND Name = {GachaTableName}.Name LIMIT 1)
            WHERE ItemId = 0 AND EXISTS (SELECT 1 FROM GachaItemName WHERE Game = @Game AND Name = {GachaTableName}.Name);
            """, new { Game = GameKey });
    }



    /// <summary>用于按 UID 统计记录数以推进进度。</summary>
    private sealed class GachaUidCount
    {
        public long Uid { get; set; }

        public int Count { get; set; }
    }


}
