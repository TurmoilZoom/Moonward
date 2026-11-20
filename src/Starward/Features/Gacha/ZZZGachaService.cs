using Dapper;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.ZZZ;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.ZZZ.GachaRecord;
using Starward.Core.GameRecord.ZZZ.GachaStat;
using Starward.Features.Database;
using Starward.Features.Gacha.UIGF;
using Starward.Features.GameRecord;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

/// <summary>
/// 绝区零（Zenless Zone Zero，GameBiz.nap）抽卡记录服务。
/// 负责从网页缓存 URL 或官方战绩接口同步「频段」记录、落库、统计 pity / 出率 / UP 情况、UIGF 导入导出、名称本地化更新等。
///
/// 与原神/星铁的主要差异：
/// - 稀有度使用 S/A/B（内部映射为 RankType 4/3/2），统计字段中 Count_5_* 实际对应 S 级（RankType==4）。
/// - 支持通过 GameRecordService 从米游社/HoYoLAB 官方接口拉取记录（GetGachaLogByGameRecordAsync）。
/// - 部分限定频段（独家频段、音擎频段）存在“非UP”老角色/音擎，使用 GachaNoUp 规则判定 IsUp。
/// - 独家重映(102)/音擎回响(103) 与对应主频段共享非UP规则。
/// </summary>
internal class ZZZGachaService : GachaLogService
{


    /// <summary>当前游戏业务线，固定为 nap（绝区零）。</summary>
    protected override GameBiz CurrentGameBiz { get; } = GameBiz.nap;

    /// <summary>对应的数据库表名（ZZZGachaItem）。</summary>
    protected override string GachaTableName { get; } = "ZZZGachaItem";

    /// <summary>物品信息表名（ZZZGachaInfo），提供图标与名称。</summary>
    protected override string GachaInfoTableName { get; } = "ZZZGachaInfo";

    /// <summary>ZZZGachaInfo 与 ItemId 关联的主键列名。</summary>
    protected override string GachaInfoIdColumn { get; } = "Id";


    /// <summary>
    /// 用于从官方战绩（米游社/HoYoLAB）接口拉取抽卡记录。
    /// 仅 ZZZ 服务需要此依赖（原神/星铁主要走网页 URL 缓存方式）。
    /// </summary>
    private readonly GameRecordService _gameRecordService;



    /// <summary>
    /// 初始化 ZZZGachaService 实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="client">绝区零抽卡记录客户端，用于通过网页 URL 拉取数据。</param>
    /// <param name="gameRecordService">游戏战绩服务，用于通过官方米游社/HoYoLAB 接口同步记录（ZZZ 特有）。</param>
    public ZZZGachaService(ILogger<ZZZGachaService> logger, ZZZGachaClient client, GameRecordService gameRecordService) : base(logger, client)
    {
        _gameRecordService = gameRecordService;
    }


    /// <summary>
    /// 根据查询类型筛选该卡池的记录。
    /// ZZZ 各频段（1/2/3/5/102/103）之间不合并统计（与原神 301+400 需要合并不同）。
    /// </summary>
    /// <param name="items">完整的抽卡记录列表（已包含增强信息）。</param>
    /// <param name="type">要筛选的卡池类型（IGachaType）。</param>
    /// <returns>属于指定卡池类型的记录列表。</returns>
    protected override List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type)
    {
        return type switch
        {
            _ => items.Where(x => x.GachaType == type.Value).ToList(),
        };
    }



    /// <summary>
    /// 获取指定 UID 的完整抽卡记录（带 Icon、Index、Pity、IsUp/HasUpItem）。
    /// 重写基类实现以：
    /// 1. 关联 ZZZGachaInfo 表获取 Icon；
    /// 2. 使用 RankType==4 作为“5星”（S级）重置 pity；
    /// 3. 对支持非UP规则的频段（独家/音擎及其重映/回响）计算 IsUp 标记。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>按 Id 排序的增强抽卡记录列表（已计算 Index、Pity、IsUp 等）。</returns>
    public override List<GachaLogItemEx> GetGachaLogItemEx(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GachaLogItemEx>("""
            SELECT item.*, info.Icon FROM ZZZGachaItem item LEFT JOIN ZZZGachaInfo info ON item.ItemId=Info.Id WHERE Uid=@uid ORDER BY item.Id;
            """, new { uid }).ToList();
        FillMissingAgentIcons(list);
        foreach (var type in QueryGachaTypes)
        {
            var l = GetGachaLogItemsByQueryType(list, type);
            int index = 0;
            int pity = 0;
            // 检查该卡池类型是否配置了“非UP”老角色/音擎规则（见 GachaNoUp.AddGachaNoUpZZZ）
            bool hasNoUp = GachaNoUp.Dictionary.TryGetValue($"{CurrentGameBiz}{type.Value}", out var noUp);
            foreach (var item in l)
            {
                item.Index = ++index;
                item.Pity = ++pity;
                if (item.RankType == 4) // S级（对应统计中的“5星”）
                {
                    pity = 0;
                    item.HasUpItem = hasNoUp;
                    if (hasNoUp)
                    {
                        bool isUp = true;
                        if (noUp!.Items.TryGetValue(item.ItemId, out GachaNoUpItem? noUpItem))
                        {
                            // 落在 NoUpTimes 时间区间内的为“非UP”（常驻/老角色）
                            foreach ((DateTime start, DateTime end) in noUpItem.NoUpTimes)
                            {
                                if (item.Time >= start && item.Time <= end)
                                {
                                    isUp = false;
                                    break;
                                }
                            }
                        }
                        item.IsUp = isUp;
                    }
                }
            }
        }
        return list;
    }


    /// <summary>
    /// 物品信息表还没收录的代理人（版本更新当天、元数据源连不上）按 Id 推算头像地址，收录后以信息表为准。
    /// 音擎、邦布推不出地址，仍按无图标显示名称。
    /// </summary>
    /// <param name="list">已关联信息表图标的抽卡记录，就地补全 <see cref="GachaLogItemEx.Icon"/>。</param>
    private static void FillMissingAgentIcons(List<GachaLogItemEx> list)
    {
        if (!list.Exists(x => string.IsNullOrWhiteSpace(x.Icon) && ZZZGachaAgentIcon.IsAgentId(x.ItemId)))
        {
            return;
        }
        string template = ZZZGachaAgentIcon.GetUrlTemplate(list.Select(x => ((long)x.ItemId, (string?)x.Icon)));
        foreach (var item in list)
        {
            if (string.IsNullOrWhiteSpace(item.Icon) && ZZZGachaAgentIcon.GetIcon(item.ItemId, template) is string icon)
            {
                item.Icon = icon;
            }
        }
    }



    /// <summary>
    /// 通过网页缓存解析出的 gacha URL 拉取抽卡记录（客户端 API 方式）。
    /// 逻辑与基类基本一致：先取最后一条 Id 作为增量起点，调用客户端分页获取，插入后报告新增数量，
    /// 并按软件语言回写名称（与基类 <see cref="GachaLogService.EnsureLocalizedNamesAfterInsertAsync"/> 兜底一致）。
    /// </summary>
    /// <param name="url">从游戏网页缓存中提取的抽卡记录 URL。</param>
    /// <param name="all">是否拉取全部历史记录（true 时忽略本地最新 Id）。</param>
    /// <param name="lang">语言代码（可选），影响返回记录的名称本地化，并作为落库后名称回写目标语言。</param>
    /// <param name="progress">进度报告回调，用于向 UI 显示“正在获取第 X 页”等信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功获取的玩家 UID；若无法获取则返回 0。</returns>
    public override async Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
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
    /// 通过官方战绩接口（米游社 / HoYoLAB）同步抽卡记录。
    /// 这是 ZZZ 特有的同步方式（GachaLogPage 中对应“从米游社同步”/“从 HoYoLAB 同步”菜单）。
    ///
    /// 主要处理点：
    /// - 国际服（_global）：用当前 UI 语言作为 requestLanguage（HoYoLAB <c>X-Rpc-Language</c>），尽量让接口直接返回对应语言名称。
    /// - 国服（米游社）：战绩 gacha_record 通常仅返回中文名称，recordLanguage 固定 zh-cn（ItemType 中文映射）；
    ///   落库后按软件 UI 语言从 <c>GachaItemName</c> 回写 Name（与 UIGF 导入后的名称本地化一致）。
    /// - 按 QueryGachaTypes 依次拉取每个频段，使用 endId 增量 + 服务端 HasMore 分页。
    /// - 客户端侧去重：遇到 Id &lt;= 本地最后 Id 即停止。
    /// - 防御性检测分页不前进的情况，避免死循环。
    /// </summary>
    /// <param name="role">玩家角色信息（包含 Uid、GameBiz 等），用于构造官方接口请求。</param>
    /// <param name="all">是否同步全部记录（false 时只增量拉取本地最新 Id 之后的数据）。</param>
    /// <param name="lang">软件 UI 语言。国际服用于请求头；国服/国际服均用于落库后名称回写目标语言。</param>
    /// <param name="progress">进度报告回调（显示“正在获取 独家频段 第 X 页”等）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功同步的玩家 UID；失败或无记录时返回 0。</returns>
    public async Task<long> GetGachaLogByGameRecordAsync(GameRecordRole role, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (role is null)
        {
            throw new ArgumentNullException(nameof(role));
        }
        using var dapper = DatabaseService.CreateConnection();
        // 正在获取 uid
        progress?.Report(Lang.GachaLogService_GettingUid);
        long uid = role.Uid;
        if (uid == 0)
        {
            // 该账号最近6个月没有抽卡记录
            progress?.Report(Lang.GachaLogService_ThisAccountHasNoGachaRecordsInTheLast6Months);
            return 0;
        }

        // displayLanguage：软件 UI 语言，用于落库后把 Name 回写为当前界面语言。
        // requestLanguage：仅国际服传给 HoYoLAB（CommonSendAsync 附加语言头）；国服接口忽略语言参数。
        // recordLanguage：ToGachaLogItem 的 ItemType 映射与初始 Lang 标记；国服接口名称为中文，故固定 zh-cn。
        string displayLanguage = LanguageUtil.FilterLanguage(
            string.IsNullOrWhiteSpace(lang)
                ? System.Globalization.CultureInfo.CurrentUICulture.Name
                : lang);
        bool isGlobal = role.GameBiz?.EndsWith("_global", StringComparison.OrdinalIgnoreCase) ?? false;
        string recordLanguage = isGlobal ? displayLanguage : "zh-cn";
        string? requestLanguage = isGlobal ? displayLanguage : null;

        long endId = 0;
        if (!all)
        {
            endId = dapper.QueryFirstOrDefault<long>($"SELECT Id FROM {GachaTableName} WHERE Uid = @Uid ORDER BY Id DESC LIMIT 1;", new { Uid = uid });
            _logger.LogInformation("Last gacha log id of uid {Uid} is {EndId}", uid, endId);
        }

        var list = new List<GachaLogItem>();
        foreach (IGachaType queryType in QueryGachaTypes)
        {
            ZZZGachaType gachaType = queryType.Value;
            bool stop = false;
            long? queryEndId = null;
            int page = 1;
            while (!stop)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, queryType.ToLocalization(), page));
                var data = await _gameRecordService.GetZZZGachaRecordAsync(role, gachaType, queryEndId, requestLanguage, cancellationToken);
                var pageItems = data.GachaItemList ?? new List<ZZZGachaRecordItem>();
                if (pageItems.Count == 0)
                {
                    break;
                }
                foreach (var item in pageItems)
                {
                    if (!all && endId > 0 && item.Id <= endId)
                    {
                        stop = true;
                        break;
                    }
                    list.Add(ToGachaLogItem(uid, gachaType, item, recordLanguage));
                }
                if (stop || !data.HasMore)
                {
                    break;
                }
                long nextEndId = pageItems.Last().Id;
                if (nextEndId <= 0 || nextEndId == queryEndId.GetValueOrDefault())
                {
                    _logger.LogWarning("ZZZ gacha_record paging is not advancing (Uid: {Uid}, GachaType: {GachaType}, EndId: {EndId}). Stop paging to avoid infinite loop.", uid, gachaType, nextEndId);
                    break;
                }
                queryEndId = nextEndId;
                page++;
            }
        }

        var oldCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        if (list.Count == 0 && oldCount == 0)
        {
            // 与 URL 拉取一致：本地与接口都无记录时返回 0，避免 UI 创建空账号
            progress?.Report(Lang.GachaLogService_ThisAccountHasNoGachaRecordsInTheLast6Months);
            return 0;
        }
        InsertGachaLogItems(list);
        var newCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        // 获取 {list.Count} 条记录，新增 {newCount - oldCount} 条记录
        progress?.Report(string.Format(Lang.GachaLogService_GetGachaResult, list.Count, newCount - oldCount));
        // 国服接口名称为中文时，靠 EnsureLocalizedNamesAfterInsertAsync 按软件语言回写 Name。
        if (list.Count > 0)
        {
            await EnsureLocalizedNamesAfterInsertAsync(uid, displayLanguage, cancellationToken);
        }
        return uid;
    }



    /// <summary>
    /// 补全记录 Id 判定条件（SQL）：Count 为 0 且 Id 不是 100 的整数倍，见 <see cref="ZZZGachaGapFiller.IsFillRecord"/>。
    /// </summary>
    internal const string FillRecordSqlCondition = "(Count = 0 AND Id % 100 != 0)";


    /// <summary>
    /// 用官方抽卡统计（米游社 / HoYoLAB 抽卡记录活动页）补全本地缺失的调频记录。
    /// </summary>
    /// <remarks>
    /// 先做一次增量同步拉齐近期记录，剩下的差额就是超出记录接口保留期的老记录。
    /// S 级按官方物品与时间写入，A/B 级只写占位；逐频段计算，对不上的频段跳过且保留原有补全记录。
    /// 重复执行会先删掉该频段旧的补全记录再写入，结果不变。
    /// </remarks>
    /// <param name="role">已登录的绝区零角色。</param>
    /// <param name="lang">软件 UI 语言，用于同步与名称回写。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补全结果摘要。</returns>
    public async Task<ZZZGachaFillSummary> FillMissingFromOfficialStatsAsync(GameRecordRole role, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        long uid = role.Uid;
        string displayLanguage = LanguageUtil.FilterLanguage(
            string.IsNullOrWhiteSpace(lang)
                ? System.Globalization.CultureInfo.CurrentUICulture.Name
                : lang);

        // 补全只针对超出保留期的老记录，近期记录必须先同步齐，否则会把没同步的新记录当成缺口
        await GetGachaLogByGameRecordAsync(role, false, displayLanguage, progress, cancellationToken);

        progress?.Report(Lang.ZZZGachaService_GettingOfficialGachaStats);
        ZZZOfficialGachaStat stat = await _gameRecordService.GetZZZOfficialGachaStatAsync(role, displayLanguage, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        using var dapper = DatabaseService.CreateConnection();
        // 读本地记录、计算、写入放在同一个写事务里：期间若有导入等操作插入老记录，会排队等这里提交，不会让补全基于过期的本地数据
        using var t = dapper.BeginTransaction();
        var all = dapper.Query<ZZZGachaItem>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }, t).ToList();
        long minRealId = all.Where(x => !ZZZGachaGapFiller.IsFillRecord(x.Id, x.Count)).Select(x => x.Id).DefaultIfEmpty(100_000_000_000_000_000).Min();
        TimeSpan fallbackOffset = GetServerUtcOffset(role.Region);
        var itemTypeById = dapper.Query<(long ItemId, string ItemType)>(
            $"SELECT ItemId, ItemType FROM {GachaTableName} WHERE ItemId != 0 AND Count != 0 GROUP BY ItemId;", transaction: t)
            .ToDictionary(x => x.ItemId, x => x.ItemType);

        var lines = new List<string>();
        bool hasWarning = false;
        var clearTypes = new List<int>();
        var newItems = new List<ZZZGachaItem>();
        foreach (IGachaType queryType in QueryGachaTypes)
        {
            int gachaType = queryType.Value;
            int idSuffix = GetFillIdSuffix(gachaType);
            ZZZGachaTypeStat? typeStat = stat.Index.TypeStats.FirstOrDefault(x => ZZZGachaStatType.ToGachaType(x.GachaType) == gachaType);
            if (idSuffix == 0 || typeStat is null)
            {
                continue;
            }
            var localReal = all.Where(x => x.GachaType == gachaType && !ZZZGachaGapFiller.IsFillRecord(x.Id, x.Count))
                               .Select(x => new ZZZGapLocalRecord(x.Id, x.ItemId, x.RankType, x.Time))
                               .ToList();
            var officialRecords = stat.SRecords.GetValueOrDefault(gachaType) ?? [];
            var officialS = officialRecords.Select(x => new ZZZGapOfficialS(x.Sort, x.ItemId, x.RecordTime, x.Count)).ToList();
            var occupied = all.Where(x => !(x.GachaType == gachaType && ZZZGachaGapFiller.IsFillRecord(x.Id, x.Count))).Select(x => x.Id).ToHashSet();
            ZZZGapFillResult result = ZZZGachaGapFiller.Compute(localReal, officialS, typeStat.TotalCount, fallbackOffset, idSuffix, minRealId, occupied, DateTime.UtcNow);
            string typeName = queryType.ToLocalization();
            switch (result.Status)
            {
                case ZZZGapFillStatus.Filled:
                    clearTypes.Add(gachaType);
                    var officialBySort = officialRecords.GroupBy(x => x.Sort).ToDictionary(x => x.Key, x => x.First());
                    foreach (var record in result.Records)
                    {
                        newItems.Add(ToFillItem(uid, gachaType, record, officialBySort, itemTypeById, stat.Language));
                    }
                    lines.Add(string.Format(Lang.ZZZGachaService_FillResult, typeName, result.MissingCount, result.SRankCount));
                    _logger.LogInformation("ZZZ gacha fill: uid {Uid}, type {GachaType}, filled {Count} (S {SCount})", uid, gachaType, result.MissingCount, result.SRankCount);
                    break;
                case ZZZGapFillStatus.NothingMissing:
                    // 本地真实记录已齐（例如后来导入了老记录），旧的补全记录多余
                    clearTypes.Add(gachaType);
                    break;
                case ZZZGapFillStatus.LocalBehind:
                    hasWarning = true;
                    lines.Add(string.Format(Lang.ZZZGachaService_FillLocalBehind, typeName));
                    _logger.LogWarning("ZZZ gacha fill local behind: uid {Uid}, type {GachaType}, {Reason}", uid, gachaType, result.Reason);
                    break;
                case ZZZGapFillStatus.OfficialBehind:
                    hasWarning = true;
                    lines.Add(string.Format(Lang.ZZZGachaService_FillOfficialBehind, typeName));
                    _logger.LogWarning("ZZZ gacha fill official stats behind: uid {Uid}, type {GachaType}, {Reason}", uid, gachaType, result.Reason);
                    break;
                default:
                    hasWarning = true;
                    lines.Add(string.Format(Lang.ZZZGachaService_FillSkipped, typeName));
                    _logger.LogWarning("ZZZ gacha fill skipped: uid {Uid}, type {GachaType}, status {Status}, {Reason}", uid, gachaType, result.Status, result.Reason);
                    break;
            }
        }

        if (clearTypes.Count > 0)
        {
            dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid AND GachaType IN @clearTypes AND {FillRecordSqlCondition};", new { uid, clearTypes }, t);
            dapper.Execute("""
                INSERT OR REPLACE INTO ZZZGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
                VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
                """, newItems, t);
        }
        t.Commit();
        if (newItems.Exists(x => x.ItemId != 0))
        {
            // 官方名称是国服中文 / 国际服请求语言，按软件语言回写
            await EnsureLocalizedNamesAfterInsertAsync(uid, displayLanguage, cancellationToken);
        }
        if (lines.Count == 0)
        {
            lines.Add(Lang.ZZZGachaService_FillNothingMissing);
        }
        return new ZZZGachaFillSummary(uid, string.Join(Environment.NewLine, lines), hasWarning);
    }


    /// <summary>
    /// 把补全计算结果转成可落库的记录：S 级带官方物品与名称，占位物品 Id 为 0、稀有度为 0。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <param name="gachaType">频段。</param>
    /// <param name="record">补全记录。</param>
    /// <param name="officialBySort">官方 S 级（按 sort 索引），S 级的 Position 即 sort。</param>
    /// <param name="itemTypeById">本地已有记录的物品类型文本，优先沿用以保持一致。</param>
    /// <param name="language">官方名称所用语言。</param>
    /// <returns>Count 为 0 的补全记录。</returns>
    private static ZZZGachaItem ToFillItem(long uid, int gachaType, ZZZGapFillRecord record, Dictionary<long, ZZZGachaPoolStatItem> officialBySort, Dictionary<long, string> itemTypeById, string language)
    {
        var item = new ZZZGachaItem
        {
            Uid = uid,
            Id = record.Id,
            GachaType = gachaType,
            Time = record.Time,
            Count = 0,
            Lang = language,
            Name = "",
            ItemType = "",
        };
        if (record.IsSRank && officialBySort.TryGetValue(record.Position, out var official))
        {
            bool isChinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            item.ItemId = official.ItemId;
            item.Name = official.Name;
            item.RankType = 4;
            item.ItemType = itemTypeById.GetValueOrDefault(official.ItemId) ?? official.ItemType switch
            {
                "ItemType_Avatar" => isChinese ? "代理人" : "ITEM_TYPE_AVATAR",
                "ItemType_Weapon" => isChinese ? "音擎" : "ITEM_TYPE_WEAPON",
                "ItemType_Bangboo" => isChinese ? "邦布" : "ITEM_TYPE_BANGBOO",
                _ => official.ItemType,
            };
        }
        return item;
    }


    /// <summary>
    /// 各频段补全记录 Id 的后两位，互不相同，保证不同频段的补全记录不会撞 Id。
    /// </summary>
    /// <param name="gachaType">频段。</param>
    /// <returns>1~99；未知频段返回 0（不补全）。</returns>
    private static int GetFillIdSuffix(int gachaType) => gachaType switch
    {
        ZZZGachaType.StandardChannel => 11,
        ZZZGachaType.ExclusiveChannel => 12,
        ZZZGachaType.WEngineChannel => 13,
        ZZZGachaType.BangbooChannel => 15,
        ZZZGachaType.ExclusiveRescreening => 42,
        ZZZGachaType.WEngineReverberation => 43,
        _ => 0,
    };


    /// <summary>
    /// 区服的服务器时区，仅在无法从本地 S 级反推时区时使用。
    /// </summary>
    /// <param name="region">区服，如 prod_gf_cn。</param>
    /// <returns>相对 UTC 的偏移。</returns>
    private static TimeSpan GetServerUtcOffset(string? region) => region switch
    {
        "prod_gf_eu" => TimeSpan.FromHours(1),
        "prod_gf_us" or "prod_gf_usa" => TimeSpan.FromHours(-5),
        _ => TimeSpan.FromHours(8),
    };


    /// <summary>
    /// 计算各频段的统计数据（用于 ZZZGachaStatsCard 显示）。
    /// 重写基类版本，主要差异：
    /// - ZZZ 使用 RankType 4/3/2 分别代表 S/A/B，因此：
    ///   Count_5_* = RankType==4 (S), Count_4 = RankType==3 (A), Count_3 = RankType==2 (B)
    /// - List_5 / List_4 分别收集 S 级和 A 级记录。
    /// - 对 A 级（RankType==3）额外重新计算单次 pity（pity_4 变量）。
    /// - 当前 S 级 pity 进度与大小保底状态通过 GachaTypeStats 单独提供给统计区域展示。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>
    /// 元组：
    /// - GachaStats: 各频段的统计信息列表（包含出率、平均抽数、当前 pity、5星/4星列表等）。
    /// - ItemStats: 按角色/音擎/邦布分组的汇总统计（用于物品统计视图）。
    /// </returns>
    public override (List<GachaTypeStats> GachaStats, List<GachaLogItemEx> ItemStats) GetGachaTypeStats(long uid)
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
                    // ZZZ: S级映射为 RankType==4，统计上称为“5星”
                    Count_5_Up = list.Count(x => x.RankType == 4 && x.IsUp),
                    Count_5 = list.Count(x => x.RankType == 4),
                    Count_4 = list.Count(x => x.RankType == 3), // A级
                    Count_3 = list.Count(x => x.RankType == 2), // B级
                    StartTime = list.First().Time,
                    EndTime = list.Last().Time,
                    Pity_5_Max = GetPityLimit(type),
                    ShowPityProgress = ShouldShowPityProgress(type, list.Count),
                };
                stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
                // A/B 级出率不计占位：补全只知道缺了几抽，不知道其中 A/B 各几个
                int knownCount = list.Count(x => !ZZZGachaGapFiller.IsPlaceholder(x.Id, x.Count, x.RankType));
                stats.Ratio_4 = knownCount > 0 ? (double)stats.Count_4 / knownCount : 0;
                stats.Ratio_3 = knownCount > 0 ? (double)stats.Count_3 / knownCount : 0;
                stats.List_5 = list.Where(x => x.RankType == 4).Reverse().ToList();
                stats.List_4 = list.Where(x => x.RankType == 3).Reverse().ToList();
                stats.Pity_5 = list.Last().Pity;
                if (list.Last().RankType == 4)
                {
                    stats.Pity_5 = 0;
                }
                // 无 S 级样本时不计算平均，避免 NaN；展示侧用 Average_5_Text 显示「—」
                if (stats.Count_5 > 0)
                {
                    stats.Average_5 = (double)(stats.Count - stats.Pity_5) / stats.Count_5;
                }
                // A级 pity 计算（从后往前找最后一次 A）
                stats.Pity_4 = list.Count - 1 - list.FindLastIndex(x => x.RankType == 3);

                if (stats.Count_5_Up > 0)
                {
                    int c = stats.Count - stats.Pity_5;
                    stats.Average_5_Up = (double)c / stats.Count_5_Up;
                }

                // 「不歪概率」：仅 UP（限定）频段统计小保底 50/50 的不歪情况（ZZZ 中 S 级为 RankType==4）。
                stats.HasUpItem = GachaNoUp.Dictionary.ContainsKey($"{CurrentGameBiz}{type.Value}");
                if (stats.HasUpItem)
                {
                    (stats.FiftyFiftyCount, stats.FiftyFiftyNoUpCount, stats.MaxFiftyFiftyUpStreak, stats.MaxFiftyFiftyMissStreak, stats.IsNextPityGuaranteed) = CountFiftyFiftyNoUp(list, 4);
                }

                // 重新为列表中的每条 A 级记录计算“距离上次 A 级”的 pity 值（用于详情列表展示）
                int pity_4 = 0;
                foreach (var item in list)
                {
                    pity_4++;
                    if (item.RankType == 3)
                    {
                        item.Pity = pity_4;
                        pity_4 = 0;
                    }
                }

                statsList.Add(stats);
            }
            groupStats = allItems.Where(x => !ZZZGachaGapFiller.IsPlaceholder(x.Id, x.Count, x.RankType))
                                 .GroupBy(x => x.ItemId)
                                 .Select(x => { var item = x.First(); item.ItemCount = x.Count(); return item; })
                                 .OrderByDescending(x => x.RankType)
                                 .ThenByDescending(x => x.ItemCount)
                                 .ThenByDescending(x => x.Time)
                                 .ToList();
            AttachVersionGroups(statsList, uid);
        }
        return (statsList, groupStats);
    }




    /// <summary>
    /// 批量插入（或 REPLACE）抽卡记录到 ZZZGachaItem 表。
    /// 注意：ZZZ 表不包含 GachaId 字段（与星铁不同）。
    /// </summary>
    /// <param name="items">要插入的抽卡记录列表（已转换为 GachaLogItem 子类）。</param>
    /// <returns>受影响的行数（实际插入/替换的记录数量）。</returns>
    protected override int InsertGachaLogItems(List<GachaLogItem> items)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        int removed = RemoveFillRecordsInvalidatedBy(dapper, t, items);
        if (removed > 0)
        {
            _logger.LogInformation("Removed {Count} ZZZ gacha fill records because older real records were inserted", removed);
        }
        var affect = dapper.Execute("""
            INSERT OR REPLACE INTO ZZZGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, items, t);
        t.Commit();
        return affect;
    }


    /// <summary>
    /// 写入真实记录前：若有新记录插进了某频段已有历史的中间或前面（导入老记录、隔久了重新同步），
    /// 该频段的补全记录可能与真实记录重复计数，整频段删掉，需要时让用户重新补全。只在末尾追加的增量同步不受影响。
    /// </summary>
    /// <remarks>所有往 ZZZGachaItem 写真实记录的路径（同步、UIGF v3/v4 导入）都要在同一事务里先调用。</remarks>
    /// <param name="dapper">数据库连接。</param>
    /// <param name="transaction">与写入共用的事务。</param>
    /// <param name="items">即将写入的真实记录。</param>
    /// <returns>删除的补全记录条数。</returns>
    internal static int RemoveFillRecordsInvalidatedBy(System.Data.IDbConnection dapper, System.Data.IDbTransaction transaction, IEnumerable<GachaLogItem> items)
    {
        int total = 0;
        foreach (var group in items.GroupBy(x => (x.Uid, x.GachaType)))
        {
            var existing = dapper.Query<(long Id, int Count)>(
                "SELECT Id, Count FROM ZZZGachaItem WHERE Uid = @Uid AND GachaType = @GachaType;",
                new { group.Key.Uid, group.Key.GachaType }, transaction).ToList();
            if (!existing.Exists(x => ZZZGachaGapFiller.IsFillRecord(x.Id, x.Count)))
            {
                continue;
            }
            var existingIds = existing.Select(x => x.Id).ToHashSet();
            long maxRealId = existing.Where(x => !ZZZGachaGapFiller.IsFillRecord(x.Id, x.Count)).Select(x => x.Id).DefaultIfEmpty(long.MaxValue).Max();
            if (group.Any(x => !existingIds.Contains(x.Id) && x.Id < maxRealId))
            {
                total += dapper.Execute(
                    $"DELETE FROM ZZZGachaItem WHERE Uid = @Uid AND GachaType = @GachaType AND {FillRecordSqlCondition};",
                    new { group.Key.Uid, group.Key.GachaType }, transaction);
            }
        }
        return total;
    }


    /// <summary>
    /// 排除 A/B 级占位记录：它们的物品 Id 为 0，不应触发「有未收录物品」的物品信息刷新。
    /// </summary>
    protected override bool HasUnknownGachaItems(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<int>($"""
            SELECT EXISTS(
                SELECT 1 FROM {GachaTableName} item
                LEFT JOIN {GachaInfoTableName} info ON item.ItemId = info.{GachaInfoIdColumn}
                WHERE item.Uid = @uid AND info.{GachaInfoIdColumn} IS NULL
                  AND NOT (item.ItemId = 0 AND item.Count = 0 AND item.Id % 100 != 0)
            );
            """, new { uid }) == 1;
    }



    /// <summary>
    /// 导出指定 UID 的抽卡记录。
    /// 支持 "excel" 和默认的 JSON（UIGF v3 格式）。
    /// </summary>
    /// <param name="uid">要导出的玩家 UID。</param>
    /// <param name="file">目标文件完整路径。</param>
    /// <param name="format">导出格式： "excel" 表示 Excel，其它值表示 UIGF JSON。</param>
    /// <returns>异步任务。</returns>
    public override async Task ExportGachaLogAsync(long uid, string file, string format)
    {
        if (format is "excel")
        {
            await ExportAsExcelAsync(uid, file);
        }
        else
        {
            await ExportAsJsonAsync(uid, file);
        }
    }



    /// <summary>
    /// 导出为 UIGF v3 JSON 格式（使用 ZZZGachaItem 直接序列化）。
    /// Info 中仅包含基础字段（无 RegionTimeZone 等星铁/原神特有处理）。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <param name="output">输出文件完整路径。</param>
    /// <returns>异步任务。</returns>
    private async Task ExportAsJsonAsync(long uid, string output)
    {
        using var dapper = DatabaseService.CreateConnection();
        // 补全记录的 Id 是本地生成的，导出会污染其他工具的数据
        var list = dapper.Query<ZZZGachaItem>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid AND NOT {FillRecordSqlCondition} ORDER BY Id;", new { uid }).ToList();
        DateTimeOffset time = DateTimeOffset.Now;
        var uigfObj = new UIGF3File<ZZZGachaItem>
        {
            Info = new UIAF3FileInfo
            {
                Uid = uid,
                Lang = list.Last().Lang ?? "",
                ExportTimestamp = time.ToUnixTimeSeconds(),
                ExportTime = time.ToString("yyyy-MM-dd HH:mm:ss"),
                ExportAppVersion = AppConfig.AppVersion,
            },
            List = list,
        };
        using FileStream fs = File.Create(output);
        await JsonSerializer.SerializeAsync(fs, uigfObj, AppConfig.JsonSerializerOptions);
    }


    /// <summary>
    /// 导出为 Excel（扁平化列，GachaType 使用本地化名称）。
    /// 注意：使用了 GetGachaLogItemEx 以便带上 Index/Pity 等增强字段。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <param name="output">输出 Excel 文件完整路径。</param>
    /// <returns>异步任务。</returns>
    private async Task ExportAsExcelAsync(long uid, string output)
    {
        // 序号与垫抽按含补全记录的完整序列计算，但补全记录本身不导出
        var list = GetGachaLogItemEx(uid).Where(x => !ZZZGachaGapFiller.IsFillRecord(x.Id, x.Count)).ToList();
        var template = Path.Combine(AppContext.BaseDirectory, @"Assets\Template\ZZZGachaLog.xlsx");
        if (File.Exists(template))
        {
            await MiniExcel.SaveAsByTemplateAsync(output, template, new { list });
        }
    }




    /// <summary>
    /// 从 UIGF v3 JSON 文件导入调频记录。
    /// 会补全缺失的 Lang 和 Uid 字段，然后批量插入。
    /// 成功后弹出应用内 Toast（使用 ZZZ 专属文案）。
    /// </summary>
    /// <param name="file">UIGF v3 JSON 文件的完整路径。</param>
    /// <returns>成功导入的记录所属的 UID；解析失败时返回 0。</returns>
    public override long ImportGachaLog(string file)
    {
        var str = File.ReadAllText(file);
        var obj = JsonSerializer.Deserialize<UIGF3File<ZZZGachaItem>>(str);
        if (obj != null)
        {
            string lang = obj.Info.Lang ?? "";
            long uid = obj.Info.Uid;
            foreach (var item in obj.List)
            {
                if (item.Lang is null)
                {
                    item.Lang = lang;
                }
                if (item.Uid == 0)
                {
                    item.Uid = uid;
                }
            }
            var count = InsertGachaLogItems(obj.List.ToList<GachaLogItem>());
            // 成功导入调频记录 {count} 条
            InAppToast.MainWindow?.Success($"Uid {obj.Info.Uid}", string.Format(Lang.ZZZGachaService_ImportSignalSearchRecordsSuccessfully, count), 5000);
            return obj.Info.Uid;
        }
        return 0;
    }



    /// <summary>
    /// 从客户端接口拉取最新角色/音擎/邦布信息（名称、图标、稀有度、属性、职业），更新到 ZZZGachaInfo 表。
    /// 用于后续 ApplyGachaItemNamesAsync 修正历史记录中的名称，并写入多语言名称缓存。
    /// </summary>
    /// <param name="gameBiz">游戏业务线（nap / nap_global 等）。</param>
    /// <param name="lang">期望的语言代码（如 zh-cn, en-us）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际使用的语言代码（服务器返回的 Language）。</returns>
    public override async Task<string> UpdateGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default, bool onlyIfNewItems = false)
    {
        //全量获取语言包、图标
        var data = await _client.GetZZZGachaInfoAsync(gameBiz, lang, cancellationToken);
        // 导航刷新：仅当当前语言名称未覆盖本次拉取的物品 Id，或出现新代理人/音擎/邦布时才写入，避免每次导航都重写信息表与名称缓存。
        if (onlyIfNewItems && !ShouldWriteGachaInfo(data.Language, data.List.Select(x => (long)x.Id)))
        {
            return data.Language;
        }
        WriteZZZGachaInfoAndNameCache(data.List, data.Language);
        _logger.LogInformation("ZZZ gacha info updated, source {source}, {count} items", data.SourceUrl, data.List.Count);
        return data.Language;
    }


    /// <summary>
    /// 将物品列表写入 <c>ZZZGachaInfo</c>，并同步该语言的 <c>GachaItemName</c> 缓存。
    /// </summary>
    private void WriteZZZGachaInfoAndNameCache(IEnumerable<ZZZGachaInfo> list, string lang)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        const string insertSql = """
            INSERT OR REPLACE INTO ZZZGachaInfo (Id, Name, Icon, Rarity, ElementType, Profession)
            VALUES (@Id, @Name, @Icon, @Rarity, @ElementType, @Profession);
            """;
        dapper.Execute(insertSql, list, t);
        t.Commit();
        SaveItemNamesToCache(list.Select(x => ((long)x.Id, x.Name)), lang);
    }


    /// <summary>
    /// 先更新 GachaInfo，再用 info 表中的标准名称回写所有 ZZZGachaItem 的 Name 字段（按 ItemId 关联）。
    /// 返回实际使用的语言和受影响的记录数。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="lang">期望的语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>元组：(实际使用的语言, 受影响的抽卡记录条数)。</returns>
    protected override int RewriteRecordNamesFromCache(string lang, long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute("""
            INSERT OR REPLACE INTO ZZZGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            SELECT item.Uid, item.Id, n.Name, item.Time, item.ItemId, item.ItemType, item.RankType, item.GachaType, item.Count, @Lang
            FROM ZZZGachaItem item JOIN GachaItemName n ON item.ItemId = n.ItemId
            WHERE n.Game = @Game AND n.Lang = @Lang AND item.Uid = @Uid;
            """, new { Lang = lang, Uid = uid, Game = GameKey });
    }



    /// <summary>
    /// 将官方战绩接口返回的 ZZZGachaRecordItem 转换为可落库的 ZZZGachaItem。
    ///
    /// 关键转换：
    /// - Rarity: "S"→RankType 4, "A"→3, "B"→2（ZZZ 内部统一用此映射表示稀有度）。
    /// - ItemType: 国服（language 以 zh 开头）时映射为“代理人/音擎/邦布”，否则保留原始 ITEM_TYPE_* 字符串。
    /// - 其他字段直接映射，Count 固定为 1。
    ///
    /// 该方法在 GetGachaLogByGameRecordAsync 中被调用。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
    /// <param name="gachaType">当前频段类型（1/2/3/5/102/103）。</param>
    /// <param name="item">从官方接口获取的单条记录原始数据。</param>
    /// <param name="language">记录语言标记（决定 ItemType 是否映射为中文，以及后续名称显示）。</param>
    /// <returns>可直接插入数据库的 ZZZGachaItem 实例。</returns>
    /// <exception cref="ArgumentNullException">item 为 null。</exception>
    /// <exception cref="ArgumentException">uid、gachaType、item 的关键字段为 0，或 Rarity 非法，或必要字符串为空。</exception>
    private static GachaLogItem ToGachaLogItem(long uid, ZZZGachaType gachaType, ZZZGachaRecordItem item, string language)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (uid == 0)
        {
            throw new ArgumentException("Uid cannot be 0.", nameof(uid));
        }
        if (gachaType.Value == 0)
        {
            throw new ArgumentException("GachaType cannot be 0.", nameof(gachaType));
        }
        if (item.Id == 0)
        {
            throw new ArgumentException("Id cannot be 0.", nameof(item));
        }
        if (item.ItemId == 0)
        {
            throw new ArgumentException("ItemId cannot be 0.", nameof(item));
        }
        if (item.Date == default)
        {
            throw new ArgumentException("Date cannot be default value.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(item.ItemName))
        {
            throw new ArgumentException("ItemName cannot be null or empty.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(item.ItemType))
        {
            throw new ArgumentException("ItemType cannot be null or empty.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(item.Rarity))
        {
            throw new ArgumentException("Rarity cannot be null or empty.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("Language cannot be null or empty.", nameof(language));
        }

        int rankType = item.Rarity.ToUpperInvariant() switch
        {
            "S" => 4,
            "A" => 3,
            "B" => 2,
            _ => 0,
        };
        if (rankType == 0)
        {
            throw new ArgumentException($"Invalid rarity value: {item.Rarity}.", nameof(item));
        }

        bool isChinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        return new ZZZGachaItem
        {
            Uid = uid,
            Id = item.Id,
            GachaType = gachaType,
            Name = item.ItemName,
            ItemType = item.ItemType.ToUpperInvariant() switch
            {
                "ITEM_TYPE_AVATAR" => isChinese ? "代理人" : item.ItemType,
                "ITEM_TYPE_WEAPON" => isChinese ? "音擎" : item.ItemType,
                "ITEM_TYPE_BANGBOO" => isChinese ? "邦布" : item.ItemType,
                _ => item.ItemType,
            },
            RankType = rankType,
            Time = item.Date,
            ItemId = item.ItemId,
            Count = 1,
            Lang = language,
        };
    }




    // ============================================================
    // 以下为旧版 UIGF v1 兼容类（当前代码已全面使用 UIGF3File + UIAF3FileInfo）。
    // 保留这些类可能是为了向后兼容历史存档或旧导出文件，但 Import/Export 路径已不再引用它们。
    // 如确认无历史依赖，可安全删除。
    // ============================================================

    private class UIGFObj
    {
        public UIGFObj() { }

        public UIGFObj(long uid, List<ZZZGachaItem> list)
        {
            this.info = new UIGFInfo(uid, list);
            this.list = list;
        }

        public UIGFInfo info { get; set; }

        public List<ZZZGachaItem> list { get; set; }
    }


    private class UIGFInfo
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
        public long uid { get; set; }

        public string lang { get; set; }

        public int region_time_zone { get; set; } = 0;

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long export_timestamp { get; set; }

        public string export_app { get; set; } = "Moonward";

        public string export_app_version { get; set; } = AppConfig.AppVersion ?? "";

        public string uigf_version { get; set; } = "v1.0";

        public UIGFInfo() { }

        public UIGFInfo(long uid, List<ZZZGachaItem> list)
        {
            this.uid = uid;
            lang = list.FirstOrDefault()?.Lang ?? "";
            export_timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        }
    }


}



/// <summary>
/// 官方统计补全的结果摘要。
/// </summary>
/// <param name="Uid">玩家 UID。</param>
/// <param name="Message">逐频段结果（多行），无变化时为「无需补全」。</param>
/// <param name="HasWarning">有频段因本地未同步或对不上而跳过。</param>
public sealed record ZZZGachaFillSummary(long Uid, string Message, bool HasWarning);
