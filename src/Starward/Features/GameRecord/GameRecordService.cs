using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.BH3.DailyNote;
using Starward.Core.GameRecord.Genshin.DailyNote;
using Starward.Core.GameRecord.Genshin.ImaginariumTheater;
using Starward.Core.GameRecord.Genshin.SpiralAbyss;
using Starward.Core.GameRecord.Genshin.StygianOnslaught;
using Starward.Core.GameRecord.Genshin.TravelersDiary;
using Starward.Core.GameRecord.StarRail.ApocalypticShadow;
using Starward.Core.GameRecord.StarRail.ChallengePeak;
using Starward.Core.GameRecord.StarRail.DailyNote;
using Starward.Core.GameRecord.StarRail.ForgottenHall;
using Starward.Core.GameRecord.StarRail.PureFiction;
using Starward.Core.GameRecord.StarRail.SimulatedUniverse;
using Starward.Core.GameRecord.StarRail.TrailblazeCalendar;
using Starward.Core.GameRecord.ZZZ.DailyNote;
using Starward.Core.GameRecord.ZZZ.DeadlyAssault;
using Starward.Core.GameRecord.ZZZ.GachaRecord;
using Starward.Core.GameRecord.SignIn;
using Starward.Core.GameRecord.ZZZ.InterKnotReport;
using Starward.Core.GameRecord.ZZZ.ShiyuDefense;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord;

internal class GameRecordService
{


    private readonly ILogger<GameRecordService> _logger;

    private readonly HyperionClient _hyperionClient;

    private readonly HoyolabClient _hoyolabClient;

    private GameRecordClient _gameRecordClient;

    private readonly IMemoryCache _memoryCache;


    public string Language { get => _hoyolabClient.Language; set => _hoyolabClient.Language = value; }


    private bool isHoyolab;
    public bool IsHoyolab
    {
        get => isHoyolab;
        set
        {
            if (value)
            {
                _gameRecordClient = _hoyolabClient;
            }
            else
            {
                _gameRecordClient = _hyperionClient;
            }
            isHoyolab = value;
        }
    }


    public GameRecordService(ILogger<GameRecordService> logger, HyperionClient hyperionClient, HoyolabClient hoyolabClient, IMemoryCache memoryCache)
    {
        _logger = logger;
        _hyperionClient = hyperionClient;
        _hoyolabClient = hoyolabClient;
        _gameRecordClient = hyperionClient;
        _memoryCache = memoryCache;
    }





    /// <summary>
    /// 更新设备指纹信息
    /// </summary>
    /// <param name="forceUpdate"></param>
    /// <returns></returns>
    public async Task UpdateDeviceFpAsync(bool forceUpdate = false, CancellationToken cancellationToken = default)
    {
        if (IsHoyolab)
        {
            return;
        }
        string? id = AppConfig.HyperionDeviceId;
        string? fp = AppConfig.HyperionDeviceFp;
        DateTimeOffset lastUpdateTime = AppConfig.HyperionDeviceFpLastUpdateTime;
        if (!forceUpdate && !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(fp))
        {
            _gameRecordClient.DeviceId = id;
            _gameRecordClient.DeviceFp = fp;
        }
        if (forceUpdate || DateTimeOffset.Now - lastUpdateTime > TimeSpan.FromDays(3))
        {
            await _gameRecordClient.GetDeviceFpAsync(cancellationToken);
            AppConfig.HyperionDeviceId = _gameRecordClient.DeviceId;
            AppConfig.HyperionDeviceFp = _gameRecordClient.DeviceFp;
            AppConfig.HyperionDeviceFpLastUpdateTime = DateTimeOffset.Now;
        }
    }





    #region Game Role



    public async Task<GameRecordUser> AddRecordUserAsync(string cookie, CancellationToken cancellationToken = default)
    {
        var user = await _gameRecordClient.GetGameRecordUserAsync(cookie, cancellationToken);
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("""
            INSERT OR REPLACE INTO GameRecordUser (Uid, IsHoyolab, Nickname, Avatar, Introduce, Gender, AvatarUrl, Pendant, Cookie)
            VALUES (@Uid, @IsHoyolab, @Nickname, @Avatar, @Introduce, @Gender, @AvatarUrl, @Pendant, @Cookie);
            """, user);
        return user;
    }



    public List<GameRecordUser> GetRecordUsers()
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GameRecordUser>("SELECT * FROM GameRecordUser WHERE IsHoyolab = @IsHoyolab;", new { IsHoyolab });
        return list.ToList();
    }



    public async Task<List<GameRecordRole>> AddGameRolesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        var list = await _gameRecordClient.GetAllGameRolesAsync(cookie, cancellationToken);
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        dapper.Execute("""
            INSERT OR REPLACE INTO GameRecordRole (Uid, GameBiz, Nickname, Level, Region, RegionName, Cookie, HeadIcon)
            VALUES (@Uid, @GameBiz, @Nickname, @Level, @Region, @RegionName, @Cookie, @HeadIcon);
            """, list, t);
        t.Commit();
        return list;
    }




    public List<GameRecordRole> GetGameRoles(GameBiz gameBiz)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GameRecordRole>("SELECT * FROM GameRecordRole WHERE GameBiz = @gameBiz;", new { gameBiz });
        return list.ToList();
    }



    /// <summary>
    /// 数据库中全部游戏角色（跨所有账号 cookie 与游戏），按账号(cookie)再按游戏排序，供自动签到批量遍历。
    /// </summary>
    public List<GameRecordRole> GetAllGameRoles()
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GameRecordRole>("SELECT * FROM GameRecordRole ORDER BY Cookie, GameBiz;");
        return list.ToList();
    }



    public GameRecordRole? GetLastSelectGameRecordRoleOrTheFirstOne(GameBiz gameBiz)
    {
        using var dapper = DatabaseService.CreateConnection();
        var role = dapper.QueryFirstOrDefault<GameRecordRole>("""
            SELECT r.* FROM GameRecordRole r INNER JOIN Setting s ON s.Value = r.Uid WHERE r.GameBiz = @gameBiz AND s.Key = @key LIMIT 1;
            """, new { gameBiz, key = $"last_select_game_record_role_{gameBiz}" });
        return role ??= dapper.QueryFirstOrDefault<GameRecordRole>("SELECT * FROM GameRecordRole WHERE GameBiz = @gameBiz LIMIT 1;", new { gameBiz });
    }



    public void SetLastSelectGameRecordRole(GameBiz gameBiz, GameRecordRole role)
    {
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("INSERT OR REPLACE INTO Setting (Key, Value) VALUES (@key, @value);", new { key = $"last_select_game_record_role_{gameBiz}", value = role.Uid.ToString() });
    }


    public GameRecordRole? GetLastSelectGachaSyncRoleOrTheFirstOne(GameBiz gameBiz)
    {
        using var dapper = DatabaseService.CreateConnection();
        GameRecordRole? role = dapper.QueryFirstOrDefault<GameRecordRole>("""
            SELECT r.* FROM GameRecordRole r INNER JOIN Setting s ON s.Value = r.Uid WHERE r.GameBiz = @gameBiz AND s.Key = @key LIMIT 1;
            """, new { gameBiz, key = $"last_select_gacha_sync_role_{gameBiz}" });
        if (role is not null)
        {
            return role;
        }
        role = dapper.QueryFirstOrDefault<GameRecordRole>("""
            SELECT r.* FROM GameRecordRole r INNER JOIN Setting s ON s.Value = r.Uid WHERE r.GameBiz = @gameBiz AND s.Key = @key LIMIT 1;
            """, new { gameBiz, key = $"last_select_game_record_role_{gameBiz}" });
        if (role is not null)
        {
            dapper.Execute("INSERT OR REPLACE INTO Setting (Key, Value) VALUES (@key, @value);", new { key = $"last_select_gacha_sync_role_{gameBiz}", value = role.Uid.ToString() });
            return role;
        }
        return dapper.QueryFirstOrDefault<GameRecordRole>("SELECT * FROM GameRecordRole WHERE GameBiz = @gameBiz LIMIT 1;", new { gameBiz });
    }


    public void SetLastSelectGachaSyncRole(GameBiz gameBiz, GameRecordRole role)
    {
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("INSERT OR REPLACE INTO Setting (Key, Value) VALUES (@key, @value);", new { key = $"last_select_gacha_sync_role_{gameBiz}", value = role.Uid.ToString() });
    }


    public GameRecordUser? GetGameRecordUser(GameRecordRole? role)
    {
        if (role is null)
        {
            return null;
        }
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<GameRecordUser>("SELECT * FROM GameRecordUser WHERE Cookie = @Cookie LIMIT 1;", new { role.Cookie });
    }



    public async Task RefreshAllGameRolesInfoAsync()
    {
        var users = GetRecordUsers();
        foreach (var user in users)
        {
            await AddRecordUserAsync(user.Cookie!);
            await AddGameRolesAsync(user.Cookie!);
        }
    }


    public async Task RefreshGameRoleInfoAsync(GameRecordRole role)
    {
        await AddRecordUserAsync(role.Cookie!);
        await AddGameRolesAsync(role.Cookie!);
    }



    public async Task UpdateGameRoleHeadIconAsync(GameRecordRole role)
    {
        string key = $"game_record_role_head_icon_{role.GameBiz}_{role.Region}_{role.Uid}";
        if (!_memoryCache.TryGetValue(key, out bool _))
        {
            role = await _gameRecordClient.UpdateGameRoleHeadIconAsync(role);
            using var dapper = DatabaseService.CreateConnection();
            dapper.Execute("""
                INSERT OR REPLACE INTO GameRecordRole (Uid, GameBiz, Nickname, Level, Region, RegionName, Cookie, HeadIcon)
                VALUES (@Uid, @GameBiz, @Nickname, @Level, @Region, @RegionName, @Cookie, @HeadIcon);
                """, role);
            _memoryCache.Set(key, true, TimeSpan.FromMinutes(5));
        }
    }



    /// <summary>
    /// 删除游戏角色，返回是否删除全部账号
    /// </summary>
    /// <param name="role"></param>
    /// <returns></returns>
    public bool DeleteGameRole(GameRecordRole role)
    {
        bool deletedUser = false;
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        dapper.Execute("DELETE FROM GameRecordRole WHERE GameBiz = @GameBiz AND Uid = @Uid;", role, t);
        _logger.LogInformation("Deleted game roles with ({nickname}, {gameBiz}, {uid}).", role.Nickname, role.GameBiz, role.Uid);
        if (dapper.QueryFirstOrDefault<int>("SELECT Count(*) FROM GameRecordRole WHERE Cookie = @Cookie;", role, t) == 0)
        {
            dapper.Execute("DELETE FROM GameRecordUser WHERE Cookie = @Cookie;", role, t);
            _logger.LogInformation("Deleted all relative accounts of ({nickname}, {gameBiz}, {uid})", role.Nickname, role.GameBiz, role.Uid);
            deletedUser = true;
        }
        t.Commit();
        return deletedUser;
    }



    #endregion




    #region Spiral Abyss


    /// <summary>
    /// 深境螺旋
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SpiralAbyssInfo> RefreshSpiralAbyssInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var info = await _gameRecordClient.GetSpiralAbyssInfoAsync(role, schedule);
        var obj = new
        {
            info.Uid,
            info.ScheduleId,
            info.StartTime,
            info.EndTime,
            info.TotalBattleCount,
            info.TotalWinCount,
            info.MaxFloor,
            info.TotalStar,
            Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
        };
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("""
            INSERT OR REPLACE INTO GenshinSpiralAbyssInfo (Uid, ScheduleId, StartTime, EndTime, TotalBattleCount, TotalWinCount, MaxFloor, TotalStar, Value)
            VALUES (@Uid, @ScheduleId, @StartTime, @EndTime, @TotalBattleCount, @TotalWinCount, @MaxFloor, @TotalStar, @Value);
            """, obj);
        return info;
    }




    public List<SpiralAbyssInfo> GetSpiralAbyssInfoList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<SpiralAbyssInfo>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<SpiralAbyssInfo>("""
            SELECT Uid, ScheduleId, StartTime, EndTime, TotalBattleCount, TotalWinCount, MaxFloor, TotalStar FROM GenshinSpiralAbyssInfo WHERE Uid = @Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public SpiralAbyssInfo? GetSpiralAbyssInfo(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM GenshinSpiralAbyssInfo WHERE Uid = @Uid And ScheduleId = @scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var info = JsonSerializer.Deserialize<SpiralAbyssInfo>(value);
        if (info != null)
        {
            info.Floors = info.Floors.Where(x => x.Index > 8).OrderByDescending(x => x.Index).ToList();
        }
        return info;
    }


    #endregion




    #region Traveler's Diary



    /// <summary>
    /// 从 API 拉取旅行札记汇总并写入本地结构化缓存（<c>GenshinTravelersDiaryMonthData</c>、<c>GenshinTravelersDiaryIncomeComponent</c>）。
    /// </summary>
    /// <param name="role">游戏角色；为 null 时由调用方自行处理。</param>
    /// <param name="month">查询月份（1–12）；为 0 时拉取当前月。</param>
    /// <returns>API 返回的汇总对象（已持久化）。</returns>
    public async Task<TravelersDiarySummary> GetTravelersDiarySummaryAsync(GameRecordRole role, int month = 0)
    {
        var summary = await _gameRecordClient.GetTravelsDiarySummaryAsync(role, month);
        if (summary.MonthData is null)
        {
            return summary;
        }
        using var dapper = DatabaseService.CreateConnection();
        SaveTravelersDiarySummaryCache(dapper, summary.MonthData);
        return summary;
    }


    /// <summary>
    /// 从本地 SQLite 读取该角色所有已缓存月份的轻量投影，供左侧月份列表绑定。
    /// </summary>
    /// <param name="role">游戏角色；为 null 时返回空列表。</param>
    /// <returns>各月轻量投影列表，按年月降序。</returns>
    public List<TravelersDiarySummaryMonth> GetTravelersDiarySummaryMonthList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<TravelersDiarySummaryMonth>();
        }
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<TravelersDiarySummaryMonth>("""
            SELECT Uid, Year, Month, CurrentPrimogems
            FROM GenshinTravelersDiaryMonthData
            WHERE Uid = @Uid
            ORDER BY Year DESC, Month DESC;
            """, new { role.Uid }).ToList();
    }


    /// <summary>
    /// 从本地结构化缓存组装指定月份的完整汇总数据（含收入构成），供右侧统计区绑定。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="year">年份。</param>
    /// <param name="month">月份（1–12）。</param>
    /// <returns>月度完整数据；本地无缓存时返回 null。</returns>
    public TravelersDiaryMonthData? GetTravelersDiaryMonthData(long uid, int year, int month)
    {
        using var dapper = DatabaseService.CreateConnection();
        var data = dapper.QueryFirstOrDefault<TravelersDiaryMonthData>(
            "SELECT * FROM GenshinTravelersDiaryMonthData WHERE Uid = @uid AND Year = @year AND Month = @month LIMIT 1;",
            new { uid, year, month });
        if (data is null)
        {
            return null;
        }
        var incomeComponents = dapper.Query<TravelersDiaryIncomeComponentCache>(
            "SELECT * FROM GenshinTravelersDiaryIncomeComponent WHERE Uid = @uid AND Year = @year AND Month = @month ORDER BY Percent DESC;",
            new { uid, year, month }).ToList();
        data.PrimogemsGroupBy = incomeComponents
            .Select(x => new TravelersDiaryPrimogemsMonthGroupStats
            {
                ActionId = x.ActionId,
                Number = x.Num,
                Percent = x.Percent,
            })
            .ToList();
        return data;
    }


    /// <summary>
    /// 将 API 汇总写入结构化本地缓存表。
    /// </summary>
    /// <param name="dapper">已打开的数据库连接。</param>
    /// <param name="monthData">API 返回的月度数据。</param>
    private static void SaveTravelersDiarySummaryCache(System.Data.IDbConnection dapper, TravelersDiaryMonthData monthData)
    {
        dapper.Execute("""
            INSERT OR REPLACE INTO GenshinTravelersDiaryMonthData
            (Uid, Year, Month, CurrentPrimogems, CurrentMora, LastPrimogems, LastMora, CurrentPrimogemsLevel, PrimogemsChangeRate, MoraChangeRate, PrimogemsGroupBy)
            VALUES (@Uid, @Year, @Month, @CurrentPrimogems, @CurrentMora, @LastPrimogems, @LastMora, @CurrentPrimogemsLevel, @PrimogemsChangeRate, @MoraChangeRate, @PrimogemsGroupBy);
            """, monthData);
        dapper.Execute(
            "DELETE FROM GenshinTravelersDiaryIncomeComponent WHERE Uid = @Uid AND Year = @Year AND Month = @Month;",
            new { monthData.Uid, monthData.Year, monthData.Month });
        var components = (monthData.PrimogemsGroupBy ?? [])
            .Select(x => new TravelersDiaryIncomeComponentCache
            {
                Uid = monthData.Uid,
                Year = monthData.Year,
                Month = monthData.Month,
                ActionId = x.ActionId,
                Num = x.Number,
                Percent = x.Percent,
            })
            .ToList();
        if (components.Count > 0)
        {
            dapper.Execute("""
                INSERT OR REPLACE INTO GenshinTravelersDiaryIncomeComponent (Uid, Year, Month, ActionId, Num, Percent)
                VALUES (@Uid, @Year, @Month, @ActionId, @Num, @Percent);
                """, components);
        }
    }



    /// <summary>
    /// 拉取并缓存指定月份、指定资源类型的旅行札记明细。
    /// 本地条数已与 API <c>total</c> 一致时，仅请求并更新最后一条记录；
    /// 本地条数较少时，增量拉取「原末条 + 新增条」；本地条数较多时先按时间删除末尾多余记录再更新末条。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="month">目标月份（1–12）。</param>
    /// <param name="type">资源类型：1 原石，2 摩拉。</param>
    /// <param name="limit">分页大小，最大 100。</param>
    /// <returns>本次新增写入的明细条数；仅更新已有记录或 API 无数据时返回 0。</returns>
    public async Task<int> GetTravelersDiaryDetailAsync(GameRecordRole role, int month, int type, int limit = 100)
    {
        int total = (await _gameRecordClient.GetTravelsDiaryDetailByPageAsync(role, month, type, 1, 1)).Total;
        if (total == 0)
        {
            return 0;
        }
        using var dapper = DatabaseService.CreateConnection();
        // 明细行的 Year 取自该月汇总缓存；汇总尚未写入时由 API 明细首条时间推断。
        int year = dapper.QuerySingleOrDefault<int>(
            "SELECT Year FROM GenshinTravelersDiaryMonthData WHERE Uid = @Uid AND Month = @month ORDER BY Year DESC LIMIT 1;",
            new { role.Uid, month });
        if (year == 0)
        {
            var probe = await _gameRecordClient.GetTravelsDiaryDetailByPageAsync(role, month, type, 1, 1);
            year = probe.List?.FirstOrDefault()?.Time.Year ?? DateTime.UtcNow.Year;
        }

        int existCount = dapper.QuerySingleOrDefault<int>(
            "SELECT COUNT(*) FROM GenshinTravelersDiaryAwardItem WHERE Uid = @Uid AND Year = @year AND Month = @month AND Type = @type;",
            new { role.Uid, year, month, type });
        int addedCount = Math.Max(0, total - existCount);

        if (existCount > total)
        {
            int excess = existCount - total;
            dapper.Execute("""
                DELETE FROM GenshinTravelersDiaryAwardItem
                WHERE rowid IN (
                    SELECT rowid FROM GenshinTravelersDiaryAwardItem
                    WHERE Uid = @Uid AND Year = @year AND Month = @month AND Type = @type
                    ORDER BY Time DESC
                    LIMIT @excess
                );
                """, new { role.Uid, year, month, type, excess });
            existCount = total;
            addedCount = 0;
        }

        if (existCount == total)
        {
            await UpsertTravelersDiaryDetailLastItemAsync(role, year, month, type, total, limit, dapper);
            return addedCount;
        }

        int startRecord = existCount > 0 ? existCount : 1;
        var items = await FetchTravelersDiaryDetailRangeAsync(role, month, type, startRecord, total, limit);
        if (existCount > 0)
        {
            dapper.Execute("""
                DELETE FROM GenshinTravelersDiaryAwardItem
                WHERE rowid IN (
                    SELECT rowid FROM GenshinTravelersDiaryAwardItem
                    WHERE Uid = @Uid AND Year = @year AND Month = @month AND Type = @type
                    ORDER BY Time ASC
                    LIMIT 1 OFFSET @offset
                );
                """, new { role.Uid, year, month, type, offset = existCount - 1 });
        }
        if (items.Count > 0)
        {
            foreach (var item in items)
            {
                item.Year = year;
                item.Month = month;
            }
            dapper.Execute("""
                INSERT INTO GenshinTravelersDiaryAwardItem (Uid, Year, Month, Type, ActionId, ActionName, Time, Number)
                VALUES (@Uid, @Year, @Month, @Type, @ActionId, @ActionName, @Time, @Number);
                """, items);
        }
        return addedCount;
    }



    /// <summary>
    /// 拉取 API 明细列表中 [<paramref name="startRecord"/>, <paramref name="endRecord"/>] 闭区间内的记录（1-based，与分页顺序一致）。
    /// </summary>
    private async Task<List<TravelersDiaryAwardItem>> FetchTravelersDiaryDetailRangeAsync(
        GameRecordRole role, int month, int type, int startRecord, int endRecord, int limit)
    {
        const int pageSize = 100;
        limit = Math.Clamp(limit, 1, pageSize);
        int startPage = (startRecord - 1) / limit + 1;
        int endPage = (endRecord - 1) / limit + 1;
        var items = new List<TravelersDiaryAwardItem>();
        for (int page = startPage; page <= endPage; page++)
        {
            var pageData = await _gameRecordClient.GetTravelsDiaryDetailByPageAsync(role, month, type, page, limit);
            int pageStart = (page - 1) * limit + 1;
            for (int i = 0; i < pageData.List.Count; i++)
            {
                int recordIndex = pageStart + i;
                if (recordIndex >= startRecord && recordIndex <= endRecord)
                {
                    items.Add(pageData.List[i]);
                }
            }
        }
        return items;
    }



    /// <summary>
    /// 请求 API 最后一条明细并替换 SQLite 中对应类型的末条记录。
    /// </summary>
    private async Task UpsertTravelersDiaryDetailLastItemAsync(
        GameRecordRole role, int year, int month, int type, int total, int limit, System.Data.IDbConnection dapper)
    {
        var items = await FetchTravelersDiaryDetailRangeAsync(role, month, type, total, total, limit);
        var lastItem = items.FirstOrDefault();
        if (lastItem is null)
        {
            return;
        }
        dapper.Execute("""
            DELETE FROM GenshinTravelersDiaryAwardItem
            WHERE rowid IN (
                SELECT rowid FROM GenshinTravelersDiaryAwardItem
                WHERE Uid = @Uid AND Year = @year AND Month = @month AND Type = @type
                ORDER BY Time DESC
                LIMIT 1
            );
            """, new { role.Uid, year, month, type });
        lastItem.Year = year;
        lastItem.Month = month;
        dapper.Execute("""
            INSERT INTO GenshinTravelersDiaryAwardItem (Uid, Year, Month, Type, ActionId, ActionName, Time, Number)
            VALUES (@Uid, @Year, @Month, @Type, @ActionId, @ActionName, @Time, @Number);
            """, lastItem);
    }



    /// <summary>
    /// 从本地 SQLite 读取指定月份全部资源类型的旅行札记明细，按时间升序。
    /// 供「每日数据」按日聚合使用，一次查询替代按 Type 分两次读取。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="year">年份。</param>
    /// <param name="month">月份（1–12）。</param>
    /// <returns>该月全部明细列表；未拉取过详情时为空列表。</returns>
    public List<TravelersDiaryAwardItem> GetTravelersDiaryDetailItems(long uid, int year, int month)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<TravelersDiaryAwardItem>(
            "SELECT * FROM GenshinTravelersDiaryAwardItem WHERE Uid=@uid AND Year=@year AND Month=@month ORDER BY Time;",
            new { uid, year, month }).ToList();
    }


    /// <summary>
    /// 从本地 SQLite 读取指定月份、指定资源类型的旅行札记明细，按时间升序。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="year">年份。</param>
    /// <param name="month">月份（1–12）。</param>
    /// <param name="type">资源类型：1 原石，2 摩拉。</param>
    /// <returns>明细列表；未拉取过详情时为空列表。</returns>
    public List<TravelersDiaryAwardItem> GetTravelersDiaryDetailItems(long uid, int year, int month, int type)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<TravelersDiaryAwardItem>("SELECT * FROM GenshinTravelersDiaryAwardItem WHERE Uid=@uid AND Year=@year AND Month=@month AND Type=@type ORDER BY Time;", new { uid, year, month, type }).ToList();
    }


    /// <summary>
    /// 判断指定月份是否已拉取过旅行札记明细（本地 <c>GenshinTravelersDiaryAwardItem</c> 中存在至少一条记录）。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="year">年份。</param>
    /// <param name="month">月份（1–12）。</param>
    /// <returns>本地已有明细缓存时返回 true。</returns>
    public bool HasTravelersDiaryDetail(long uid, int year, int month)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QuerySingleOrDefault<int>(
            "SELECT COUNT(*) FROM GenshinTravelersDiaryAwardItem WHERE Uid = @uid AND Year = @year AND Month = @month;",
            new { uid, year, month }) > 0;
    }




    #endregion




    #region Imaginarium Theater



    /// <summary>
    /// 幻想真境剧诗
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task RefreshImaginariumTheaterInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        var infos = await _gameRecordClient.GetImaginariumTheaterInfosAsync(role, cancellationToken);
        if (infos.Count == 0)
        {
            return;
        }
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        foreach (var info in infos)
        {
            var obj = new
            {
                info.Uid,
                info.ScheduleId,
                info.StartTime,
                info.EndTime,
                info.DifficultyId,
                info.MaxRoundId,
                info.Heraldry,
                info.MedalNum,
                Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
            };
            dapper.Execute("""
            INSERT OR REPLACE INTO GenshinImaginariumTheaterInfo (Uid, ScheduleId, StartTime, EndTime, DifficultyId, MaxRoundId, Heraldry, MedalNum, Value)
            VALUES (@Uid, @ScheduleId, @StartTime, @EndTime, @DifficultyId, @MaxRoundId, @Heraldry, @MedalNum, @Value);
            """, obj, t);
        }
        t.Commit();
    }




    public List<ImaginariumTheaterInfo> GetImaginariumTheaterInfoList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<ImaginariumTheaterInfo>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<ImaginariumTheaterInfo>("""
            SELECT Uid, ScheduleId, StartTime, EndTime, DifficultyId, MaxRoundId, Heraldry, MedalNum FROM GenshinImaginariumTheaterInfo WHERE Uid = @Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public ImaginariumTheaterInfo? GetImaginariumTheaterInfo(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM GenshinImaginariumTheaterInfo WHERE Uid = @Uid And ScheduleId = @scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return JsonSerializer.Deserialize<ImaginariumTheaterInfo>(value);
    }




    #endregion




    #region Simulated Universe



    public async Task<SimulatedUniverseInfo> GetSimulatedUniverseInfoAsync(GameRecordRole role, bool detail)
    {
        var info = await _gameRecordClient.GetSimulatedUniverseInfoAsync(role, detail);
        if (detail)
        {
            using var dapper = DatabaseService.CreateConnection();
            using var t = dapper.BeginTransaction();
            var obj = new
            {
                role.Uid,
                info.LastRecord.Basic.ScheduleId,
                info.LastRecord.Basic.FinishCount,
                info.LastRecord.Basic.ScheduleBegin,
                info.LastRecord.Basic.ScheduleEnd,
                info.LastRecord.HasData,
                Value = JsonSerializer.Serialize(info.LastRecord, AppConfig.JsonSerializerOptions),
            };
            dapper.Execute("""
                INSERT OR REPLACE INTO StarRailSimulatedUniverseRecord (Uid, ScheduleId, FinishCount, ScheduleBegin, ScheduleEnd, HasData, Value)
                VALUES (@Uid, @ScheduleId, @FinishCount, @ScheduleBegin, @ScheduleEnd, @HasData, @Value);
                """, obj, t);
            obj = new
            {
                role.Uid,
                info.CurrentRecord.Basic.ScheduleId,
                info.CurrentRecord.Basic.FinishCount,
                info.CurrentRecord.Basic.ScheduleBegin,
                info.CurrentRecord.Basic.ScheduleEnd,
                info.CurrentRecord.HasData,
                Value = JsonSerializer.Serialize(info.CurrentRecord, AppConfig.JsonSerializerOptions),
            };
            dapper.Execute("""
                INSERT OR REPLACE INTO StarRailSimulatedUniverseRecord (Uid, ScheduleId, FinishCount, ScheduleBegin, ScheduleEnd, HasData, Value)
                VALUES (@Uid, @ScheduleId, @FinishCount, @ScheduleBegin, @ScheduleEnd, @HasData, @Value);
                """, obj, t);
            t.Commit();
        }
        return info;
    }



    public List<SimulatedUniverseRecordBasic> GetSimulatedUniverseRecordBasics(GameRecordRole role)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<SimulatedUniverseRecordBasic>("""
            SELECT ScheduleId, FinishCount, ScheduleBegin, ScheduleEnd FROM StarRailSimulatedUniverseRecord WHERE Uid=@Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid }).ToList();
    }



    public SimulatedUniverseRecord? GetSimulatedUniverseRecord(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM StarRailSimulatedUniverseRecord WHERE Uid=@Uid AND ScheduleId=@scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return JsonSerializer.Deserialize<SimulatedUniverseRecord>(value);
    }



    #endregion




    #region Forgotten Hall



    public async Task<ForgottenHallInfo> RefreshForgottenHallInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var info = await _gameRecordClient.GetForgottenHallInfoAsync(role, schedule);
        var obj = new
        {
            info.Uid,
            info.ScheduleId,
            info.BeginTime,
            info.EndTime,
            info.StarNum,
            info.ExtraStarNum,
            info.MaxFloor,
            info.BattleNum,
            info.HasData,
            Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
        };
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("""
            INSERT OR REPLACE INTO StarRailForgottenHallInfo (Uid, ScheduleId, BeginTime, EndTime, StarNum, ExtraStarNum, MaxFloor, BattleNum, HasData, Value)
            VALUES (@Uid, @ScheduleId, @BeginTime, @EndTime, @StarNum, @ExtraStarNum, @MaxFloor, @BattleNum, @HasData, @Value);
            """, obj);
        return info;
    }



    public List<ForgottenHallInfo> GetForgottenHallInfoList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<ForgottenHallInfo>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<ForgottenHallInfo>("""
            SELECT Uid, ScheduleId, BeginTime, EndTime, StarNum, ExtraStarNum, MaxFloor, BattleNum, HasData FROM StarRailForgottenHallInfo WHERE Uid = @Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public ForgottenHallInfo? GetForgottenHallInfo(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM StarRailForgottenHallInfo WHERE Uid = @Uid And ScheduleId = @scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return JsonSerializer.Deserialize<ForgottenHallInfo>(value);
    }



    #endregion




    #region Pure Fiction



    public async Task<PureFictionInfo> RefreshPureFictionInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var info = await _gameRecordClient.GetPureFictionInfoAsync(role, schedule);
        if (info.ScheduleId == 0)
        {
            return info;
        }
        var obj = new
        {
            info.Uid,
            info.ScheduleId,
            info.BeginTime,
            info.EndTime,
            info.StarNum,
            info.ExtraStarNum,
            info.MaxFloor,
            info.BattleNum,
            info.HasData,
            Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
        };
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("""
            INSERT OR REPLACE INTO StarRailPureFictionInfo (Uid, ScheduleId, BeginTime, EndTime, StarNum, ExtraStarNum, MaxFloor, BattleNum, HasData, Value)
            VALUES (@Uid, @ScheduleId, @BeginTime, @EndTime, @StarNum, @ExtraStarNum, @MaxFloor, @BattleNum, @HasData, @Value);
            """, obj);
        return info;
    }



    public List<PureFictionInfo> GetPureFictionInfoList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<PureFictionInfo>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<PureFictionInfo>("""
            SELECT Uid, ScheduleId, BeginTime, EndTime, StarNum, ExtraStarNum, MaxFloor, BattleNum, HasData FROM StarRailPureFictionInfo WHERE Uid = @Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public PureFictionInfo? GetPureFictionInfo(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM StarRailPureFictionInfo WHERE Uid = @Uid And ScheduleId = @scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return JsonSerializer.Deserialize<PureFictionInfo>(value);
    }



    #endregion




    #region Apocalyptic Shadow



    public async Task<ApocalypticShadowInfo> RefreshApocalypticShadowInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var info = await _gameRecordClient.GetApocalypticShadowInfoAsync(role, schedule);
        if (info.ScheduleId == 0)
        {
            return info;
        }
        var obj = new
        {
            info.Uid,
            info.ScheduleId,
            info.BeginTime,
            info.EndTime,
            info.UpperBossIcon,
            info.LowerBossIcon,
            info.TierceBossIcon,
            info.StarNum,
            info.ExtraStarNum,
            info.MaxFloor,
            info.BattleNum,
            info.HasData,
            Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
        };
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("""
            INSERT OR REPLACE INTO StarRailApocalypticShadowInfo (Uid, ScheduleId, BeginTime, EndTime, UpperBossIcon, LowerBossIcon, TierceBossIcon, StarNum, ExtraStarNum, MaxFloor, BattleNum, HasData, Value)
            VALUES (@Uid, @ScheduleId, @BeginTime, @EndTime, @UpperBossIcon, @LowerBossIcon, @TierceBossIcon, @StarNum, @ExtraStarNum, @MaxFloor, @BattleNum, @HasData, @Value);
            """, obj);
        return info;
    }



    public List<ApocalypticShadowInfo> GetApocalypticShadowInfoList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<ApocalypticShadowInfo>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<ApocalypticShadowInfo>("""
            SELECT Uid, ScheduleId, BeginTime, EndTime, UpperBossIcon, LowerBossIcon, TierceBossIcon, StarNum, ExtraStarNum, MaxFloor, BattleNum, HasData FROM StarRailApocalypticShadowInfo WHERE Uid = @Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public ApocalypticShadowInfo? GetApocalypticShadowInfo(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM StarRailApocalypticShadowInfo WHERE Uid = @Uid And ScheduleId = @scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return JsonSerializer.Deserialize<ApocalypticShadowInfo>(value);
    }



    #endregion




    #region Trailblaze Calendar



    /// <summary>
    /// 从 API 拉取开拓月历汇总并写入本地结构化缓存（<c>StarRailTrailblazeCalendarMonthData</c>、<c>StarRailTrailblazeCalendarIncomeComponent</c>）。
    /// </summary>
    /// <param name="role">游戏角色；为 null 时由调用方自行处理。</param>
    /// <param name="month">查询月份，格式 <c>yyyyMM</c>；为空时拉取当前月。</param>
    /// <returns>API 返回的汇总对象（已持久化）。</returns>
    public async Task<TrailblazeCalendarSummary> GetTrailblazeCalendarSummaryAsync(GameRecordRole role, string month = "")
    {
        var summary = await _gameRecordClient.GetTrailblazeCalendarSummaryAsync(role, month);
        if (summary.MonthData is null)
        {
            return summary;
        }
        using var dapper = DatabaseService.CreateConnection();
        SaveTrailblazeCalendarSummaryCache(dapper, summary.MonthData);
        return summary;
    }


    /// <summary>
    /// 从本地 SQLite 读取该角色所有已缓存月份的轻量投影，供左侧月份列表绑定。
    /// </summary>
    /// <param name="role">游戏角色；为 null 时返回空列表。</param>
    /// <returns>各月轻量投影列表，按月份降序。</returns>
    public List<TrailblazeCalendarSummaryMonth> GetTrailblazeCalendarSummaryMonthList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<TrailblazeCalendarSummaryMonth>();
        }
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<TrailblazeCalendarSummaryMonth>("""
            SELECT Uid, Month AS DataMonth, CurrentHcoin
            FROM StarRailTrailblazeCalendarMonthData
            WHERE Uid = @Uid
            ORDER BY Month DESC;
            """, new { role.Uid }).ToList();
    }


    /// <summary>
    /// 从本地结构化缓存组装指定月份的完整月度数据（含收入构成），供右侧统计区绑定。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="dataMonth">月份，格式 <c>yyyyMM</c>。</param>
    /// <returns>月度完整数据；本地无缓存时返回 null。</returns>
    public TrailblazeCalendarMonthData? GetTrailblazeCalendarMonthData(long uid, string dataMonth)
    {
        using var dapper = DatabaseService.CreateConnection();
        var data = dapper.QueryFirstOrDefault<TrailblazeCalendarMonthData>(
            "SELECT * FROM StarRailTrailblazeCalendarMonthData WHERE Uid = @uid AND Month = @dataMonth LIMIT 1;",
            new { uid, dataMonth });
        if (data is null)
        {
            return null;
        }
        var incomeComponents = dapper.Query<TrailblazeCalendarIncomeComponentCache>(
            "SELECT * FROM StarRailTrailblazeCalendarIncomeComponent WHERE Uid = @uid AND DataMonth = @dataMonth ORDER BY Percent DESC;",
            new { uid, dataMonth }).ToList();
        data.GroupBy = incomeComponents
            .Select(x => new TrailblazeCalendarMonthDataGroupBy
            {
                Action = x.Action,
                Num = x.Num,
                Percent = x.Percent,
            })
            .ToList();
        return data;
    }


    /// <summary>
    /// 将 API 汇总写入结构化本地缓存表。
    /// </summary>
    /// <param name="dapper">已打开的数据库连接。</param>
    /// <param name="monthData">API 返回的月度数据。</param>
    private static void SaveTrailblazeCalendarSummaryCache(System.Data.IDbConnection dapper, TrailblazeCalendarMonthData monthData)
    {
        dapper.Execute("""
            INSERT OR REPLACE INTO StarRailTrailblazeCalendarMonthData (Uid, Month, CurrentHcoin, CurrentRailsPass, LastHcoin, LastRailsPass, HcoinRate, RailsRate, GroupBy)
            VALUES (@Uid, @Month, @CurrentHcoin, @CurrentRailsPass, @LastHcoin, @LastRailsPass, @HcoinRate, @RailsRate, @GroupBy);
            """, monthData);
        dapper.Execute(
            "DELETE FROM StarRailTrailblazeCalendarIncomeComponent WHERE Uid = @Uid AND DataMonth = @DataMonth;",
            new { monthData.Uid, DataMonth = monthData.Month });
        var components = (monthData.GroupBy ?? [])
            .Select(x => new TrailblazeCalendarIncomeComponentCache
            {
                Uid = monthData.Uid,
                DataMonth = monthData.Month,
                Action = x.Action,
                Num = x.Num,
                Percent = x.Percent,
            })
            .ToList();
        if (components.Count > 0)
        {
            dapper.Execute("""
                INSERT OR REPLACE INTO StarRailTrailblazeCalendarIncomeComponent (Uid, DataMonth, Action, Num, Percent)
                VALUES (@Uid, @DataMonth, @Action, @Num, @Percent);
                """, components);
        }
    }



    /// <summary>
    /// 拉取并缓存指定月份、指定资源类型的开拓月历明细。
    /// 本地条数已与 API <c>total</c> 一致时，仅请求并更新最后一条记录；
    /// 本地条数较少时，增量拉取「原末条 + 新增条」；本地条数较多时先按时间删除末尾多余记录再更新末条。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型：1 星琼，2 星轨票。</param>
    /// <returns>本次新增写入的明细条数；仅更新已有记录或 API 无数据时返回 0。</returns>
    public async Task<int> GetTrailblazeCalendarDetailAsync(GameRecordRole role, string month, int type)
    {
        int total = (await _gameRecordClient.GetTrailblazeCalendarDetailByPageAsync(role, month, type, 1, 1)).Total;
        if (total == 0)
        {
            return 0;
        }
        using var dapper = DatabaseService.CreateConnection();
        int existCount = dapper.QuerySingleOrDefault<int>(
            "SELECT COUNT(*) FROM StarRailTrailblazeCalendarDetailItem WHERE Uid = @Uid AND Month = @month AND Type = @type;",
            new { role.Uid, month, type });
        int addedCount = Math.Max(0, total - existCount);

        if (existCount > total)
        {
            int excess = existCount - total;
            dapper.Execute("""
                DELETE FROM StarRailTrailblazeCalendarDetailItem
                WHERE rowid IN (
                    SELECT rowid FROM StarRailTrailblazeCalendarDetailItem
                    WHERE Uid = @Uid AND Month = @month AND Type = @type
                    ORDER BY Time DESC
                    LIMIT @excess
                );
                """, new { role.Uid, month, type, excess });
            existCount = total;
            addedCount = 0;
        }

        if (existCount == total)
        {
            await UpsertTrailblazeCalendarDetailLastItemAsync(role, month, type, total, dapper);
            return addedCount;
        }

        int startRecord = existCount > 0 ? existCount : 1;
        var items = await FetchTrailblazeCalendarDetailRangeAsync(role, month, type, startRecord, total);
        if (existCount > 0)
        {
            dapper.Execute("""
                DELETE FROM StarRailTrailblazeCalendarDetailItem
                WHERE rowid IN (
                    SELECT rowid FROM StarRailTrailblazeCalendarDetailItem
                    WHERE Uid = @Uid AND Month = @month AND Type = @type
                    ORDER BY Time ASC
                    LIMIT 1 OFFSET @offset
                );
                """, new { role.Uid, month, type, offset = existCount - 1 });
        }
        if (items.Count > 0)
        {
            dapper.Execute("""
                INSERT INTO StarRailTrailblazeCalendarDetailItem (Uid, Month, Type, Action, ActionName, Time, Number)
                VALUES (@Uid, @Month, @Type, @Action, @ActionName, @Time, @Number);
                """, items);
        }
        return addedCount;
    }



    /// <summary>
    /// 拉取 API 明细列表中 [<paramref name="startRecord"/>, <paramref name="endRecord"/>] 闭区间内的记录（1-based，与分页顺序一致）。
    /// </summary>
    private async Task<List<TrailblazeCalendarDetailItem>> FetchTrailblazeCalendarDetailRangeAsync(
        GameRecordRole role, string month, int type, int startRecord, int endRecord)
    {
        const int pageSize = 100;
        int startPage = (startRecord - 1) / pageSize + 1;
        int endPage = (endRecord - 1) / pageSize + 1;
        var items = new List<TrailblazeCalendarDetailItem>();
        for (int page = startPage; page <= endPage; page++)
        {
            var pageData = await _gameRecordClient.GetTrailblazeCalendarDetailByPageAsync(role, month, type, page, pageSize);
            int pageStart = (page - 1) * pageSize + 1;
            for (int i = 0; i < pageData.List.Count; i++)
            {
                int recordIndex = pageStart + i;
                if (recordIndex >= startRecord && recordIndex <= endRecord)
                {
                    items.Add(pageData.List[i]);
                }
            }
        }
        return items;
    }



    /// <summary>
    /// 请求 API 最后一条明细并替换 SQLite 中对应类型的末条记录。
    /// </summary>
    private async Task UpsertTrailblazeCalendarDetailLastItemAsync(
        GameRecordRole role, string month, int type, int total, System.Data.IDbConnection dapper)
    {
        var items = await FetchTrailblazeCalendarDetailRangeAsync(role, month, type, total, total);
        var lastItem = items.FirstOrDefault();
        if (lastItem is null)
        {
            return;
        }
        dapper.Execute("""
            DELETE FROM StarRailTrailblazeCalendarDetailItem
            WHERE rowid IN (
                SELECT rowid FROM StarRailTrailblazeCalendarDetailItem
                WHERE Uid = @Uid AND Month = @month AND Type = @type
                ORDER BY Time DESC
                LIMIT 1
            );
            """, new { role.Uid, month, type });
        dapper.Execute("""
            INSERT INTO StarRailTrailblazeCalendarDetailItem (Uid, Month, Type, Action, ActionName, Time, Number)
            VALUES (@Uid, @Month, @Type, @Action, @ActionName, @Time, @Number);
            """, lastItem);
    }



    /// <summary>
    /// 从本地 SQLite 读取指定月份全部资源类型的开拓月历明细，按时间升序。
    /// 供「每日数据」按日聚合使用，一次查询替代按 Type 分两次读取。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <returns>该月全部明细列表；未拉取过详情时为空列表。</returns>
    public List<TrailblazeCalendarDetailItem> GetTrailblazeCalendarDetailItems(long uid, string month)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<TrailblazeCalendarDetailItem>(
            "SELECT * FROM StarRailTrailblazeCalendarDetailItem WHERE Uid=@uid AND Month=@month ORDER BY Time;",
            new { uid, month }).ToList();
    }


    /// <summary>
    /// 从本地 SQLite 读取指定月份、指定资源类型的开拓月历明细，按时间升序。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型：1 星琼，2 星轨票。</param>
    /// <returns>明细列表；未拉取过详情时为空列表。</returns>
    public List<TrailblazeCalendarDetailItem> GetTrailblazeCalendarDetailItems(long uid, string month, int type)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<TrailblazeCalendarDetailItem>("SELECT * FROM StarRailTrailblazeCalendarDetailItem WHERE Uid=@uid AND Month=@month AND Type=@type ORDER BY Time;", new { uid, month, type }).ToList();
    }


    /// <summary>
    /// 判断指定月份是否已拉取过开拓月历明细（本地 <c>StarRailTrailblazeCalendarDetailItem</c> 中存在至少一条记录）。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <returns>本地已有明细缓存时返回 true。</returns>
    public bool HasTrailblazeCalendarDetail(long uid, string month)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QuerySingleOrDefault<int>(
            "SELECT COUNT(*) FROM StarRailTrailblazeCalendarDetailItem WHERE Uid = @uid AND Month = @month;",
            new { uid, month }) > 0;
    }




    #endregion




    // 绝区零绳网月报：月度总量与收入构成存结构化表，明细分条存 ZZZInterKnotReportDetailItem。
    #region Inter Knot Report



    /// <summary>
    /// 从 API 拉取绳网月报汇总并写入本地结构化缓存（<c>ZZZInterKnotReportMonthData</c>、<c>ZZZInterKnotReportIncomeComponent</c>）。
    /// </summary>
    /// <param name="role">游戏角色；为 null 时由调用方自行处理。</param>
    /// <param name="month">查询月份，格式 <c>yyyyMM</c>；为空时拉取当前月。</param>
    /// <returns>API 返回的汇总对象（已持久化）。</returns>
    public async Task<InterKnotReportSummary> GetInterKnotReportSummaryAsync(GameRecordRole role, string month = "")
    {
        var summary = await _gameRecordClient.GetInterKnotReportSummaryAsync(role, month);
        using var dapper = DatabaseService.CreateConnection();
        SaveInterKnotReportSummaryCache(dapper, summary);
        return summary;
    }


    /// <summary>
    /// 读取指定角色所有已缓存月份的绳网月报「列表项」轻量投影（仅月份 + 当月菲林总量），按 <c>DataMonth</c> 降序。
    /// </summary>
    /// <param name="role">游戏角色；为 null 时返回空列表。</param>
    /// <returns>各月轻量投影列表，按月份降序。</returns>
    public List<InterKnotReportSummaryMonth> GetInterKnotReportSummaryMonthList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<InterKnotReportSummaryMonth>();
        }
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<InterKnotReportSummaryMonth>("""
            SELECT Uid, DataMonth, PolychromeCount
            FROM ZZZInterKnotReportMonthData
            WHERE Uid = @Uid
            ORDER BY DataMonth DESC;
            """, new { role.Uid }).ToList();
    }


    /// <summary>
    /// 从本地结构化缓存组装指定月份汇总（含资源总量与菲林收入构成），供点击月份后展示。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="dataMonth">月份，格式 <c>yyyyMM</c>（如 <c>202506</c>）。</param>
    /// <returns>汇总视图对象；本地无缓存时返回 null。</returns>
    public InterKnotReportSummary? GetInterKnotReportSummary(long uid, string dataMonth)
    {
        using var dapper = DatabaseService.CreateConnection();
        var month = dapper.QueryFirstOrDefault<InterKnotReportMonthCache>(
            "SELECT * FROM ZZZInterKnotReportMonthData WHERE Uid = @uid AND DataMonth = @dataMonth LIMIT 1;",
            new { uid, dataMonth });
        if (month is null)
        {
            return null;
        }
        var incomeComponents = dapper.Query<InterKnotReportIncomeComponentCache>(
            "SELECT * FROM ZZZInterKnotReportIncomeComponent WHERE Uid = @uid AND DataMonth = @dataMonth ORDER BY Percent DESC;",
            new { uid, dataMonth }).ToList();
        return BuildInterKnotReportSummary(month, incomeComponents);
    }



    /// <summary>
    /// 将 API 汇总写入结构化本地缓存表。
    /// </summary>
    /// <param name="dapper">已打开的数据库连接。</param>
    /// <param name="summary">API 返回的汇总；<see cref="InterKnotReportSummary.MonthData"/> 为 null 时跳过。</param>
    private static void SaveInterKnotReportSummaryCache(System.Data.IDbConnection dapper, InterKnotReportSummary summary)
    {
        if (summary.MonthData is null)
        {
            return;
        }
        var monthCache = new InterKnotReportMonthCache
        {
            Uid = summary.Uid,
            DataMonth = summary.DataMonth,
        };
        foreach (var item in summary.MonthData.List ?? [])
        {
            switch (item.DataType)
            {
                case InterKnotReportDataType.PolychromesData:
                    monthCache.PolychromeCount = item.Count;
                    monthCache.PolychromeName = item.DataName;
                    break;
                case InterKnotReportDataType.MatserTapeData:
                    monthCache.MasterTapeCount = item.Count;
                    monthCache.MasterTapeName = item.DataName;
                    break;
                case InterKnotReportDataType.BooponsData:
                    monthCache.BooponCount = item.Count;
                    monthCache.BooponName = item.DataName;
                    break;
            }
        }
        dapper.Execute("""
            INSERT OR REPLACE INTO ZZZInterKnotReportMonthData
            (Uid, DataMonth, PolychromeCount, MasterTapeCount, BooponCount, PolychromeName, MasterTapeName, BooponName)
            VALUES (@Uid, @DataMonth, @PolychromeCount, @MasterTapeCount, @BooponCount, @PolychromeName, @MasterTapeName, @BooponName);
            """, monthCache);
        dapper.Execute(
            "DELETE FROM ZZZInterKnotReportIncomeComponent WHERE Uid = @Uid AND DataMonth = @DataMonth;",
            new { summary.Uid, summary.DataMonth });
        var components = (summary.MonthData.IncomeComponents ?? [])
            .Select(x => new InterKnotReportIncomeComponentCache
            {
                Uid = summary.Uid,
                DataMonth = summary.DataMonth,
                Action = x.Action,
                Num = x.Num,
                Percent = x.Percent,
            })
            .ToList();
        if (components.Count > 0)
        {
            dapper.Execute("""
                INSERT OR REPLACE INTO ZZZInterKnotReportIncomeComponent (Uid, DataMonth, Action, Num, Percent)
                VALUES (@Uid, @DataMonth, @Action, @Num, @Percent);
                """, components);
        }
    }



    /// <summary>
    /// 由本地结构化缓存行组装 UI 绑定的 <see cref="InterKnotReportSummary"/> 视图。
    /// </summary>
    /// <param name="month">月度总量缓存行。</param>
    /// <param name="incomeComponents">该月菲林收入构成列表。</param>
    /// <returns>仅含展示所需字段的汇总对象。</returns>
    private static InterKnotReportSummary BuildInterKnotReportSummary(
        InterKnotReportMonthCache month,
        List<InterKnotReportIncomeComponentCache> incomeComponents)
    {
        return new InterKnotReportSummary
        {
            Uid = month.Uid,
            DataMonth = month.DataMonth,
            MonthData = new InterKnotReportMonthData
            {
                List =
                [
                    new InterKnotReportSummaryAward
                    {
                        DataType = InterKnotReportDataType.PolychromesData,
                        Count = month.PolychromeCount,
                        DataName = month.PolychromeName ?? InterKnotReportDataType.PolychromesData,
                    },
                    new InterKnotReportSummaryAward
                    {
                        DataType = InterKnotReportDataType.MatserTapeData,
                        Count = month.MasterTapeCount,
                        DataName = month.MasterTapeName ?? InterKnotReportDataType.MatserTapeData,
                    },
                    new InterKnotReportSummaryAward
                    {
                        DataType = InterKnotReportDataType.BooponsData,
                        Count = month.BooponCount,
                        DataName = month.BooponName ?? InterKnotReportDataType.BooponsData,
                    },
                ],
                IncomeComponents = incomeComponents
                    .Select(x => new InterKnotReportIncomeComponent
                    {
                        Action = x.Action,
                        Num = x.Num,
                        Percent = x.Percent,
                    })
                    .ToList(),
            },
        };
    }



    /// <summary>
    /// 拉取并缓存指定月份、指定资源类型的绳网月报明细。
    /// 本地条数已与 API <c>total</c> 一致时，仅请求并更新最后一条记录；
    /// 本地条数较少时，增量拉取「原末条 + 新增条」并 UPSERT；本地条数较多时先按时间删除末尾多余记录再更新末条。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型，取值见 <see cref="InterKnotReportDataType"/>。</param>
    /// <returns>本次新增写入的明细条数；仅更新已有记录或 API 无数据时返回 0。</returns>
    public async Task<int> GetInterKnotReportDetailAsync(GameRecordRole role, string month, string type)
    {
        int total = (await _gameRecordClient.GetInterKnotReportDetailByPageAsync(role, month, type, 1, 1)).Total;
        if (total == 0)
        {
            return 0;
        }
        using var dapper = DatabaseService.CreateConnection();
        int existCount = dapper.QuerySingleOrDefault<int>("SELECT COUNT(*) FROM ZZZInterKnotReportDetailItem WHERE Uid = @Uid AND DataMonth = @month AND DataType = @type;", new { role.Uid, month, type });
        int addedCount = Math.Max(0, total - existCount);

        // 本地条数多于 API 时，按时间倒序删除末尾多余记录（API 仅减少条数的常见情况）。
        if (existCount > total)
        {
            int excess = existCount - total;
            dapper.Execute("""
                DELETE FROM ZZZInterKnotReportDetailItem
                WHERE rowid IN (
                    SELECT rowid FROM ZZZInterKnotReportDetailItem
                    WHERE Uid = @Uid AND DataMonth = @month AND DataType = @type
                    ORDER BY Time DESC
                    LIMIT @excess
                );
                """, new { role.Uid, month, type, excess });
            existCount = total;
            addedCount = 0;
        }

        if (existCount == total)
        {
            await UpsertInterKnotReportDetailLastItemAsync(role, month, type, total, dapper);
            return addedCount;
        }

        // existCount < total：从 API 第 existCount 条（原末条）拉到第 total 条，补全新增并刷新原末条。
        int startRecord = existCount > 0 ? existCount : 1;
        var items = await FetchInterKnotReportDetailRangeAsync(role, month, type, startRecord, total);
        if (items.Count > 0)
        {
            dapper.Execute("""
                INSERT OR REPLACE INTO ZZZInterKnotReportDetailItem (Uid, Id, DataMonth, DataType, Action, Time, Number)
                VALUES (@Uid, @Id, @DataMonth, @DataType, @Action, @Time, @Number);
                """, items);
        }
        return addedCount;
    }



    /// <summary>
    /// 拉取 API 明细列表中 [<paramref name="startRecord"/>, <paramref name="endRecord"/>] 闭区间内的记录（1-based，与分页顺序一致）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型。</param>
    /// <param name="startRecord">起始条序号（从 1 开始）。</param>
    /// <param name="endRecord">结束条序号（从 1 开始）。</param>
    /// <returns>区间内明细列表。</returns>
    private async Task<List<InterKnotReportDetailItem>> FetchInterKnotReportDetailRangeAsync(GameRecordRole role, string month, string type, int startRecord, int endRecord)
    {
        const int pageSize = 100;
        int startPage = (startRecord - 1) / pageSize + 1;
        int endPage = (endRecord - 1) / pageSize + 1;
        var items = new List<InterKnotReportDetailItem>();
        for (int page = startPage; page <= endPage; page++)
        {
            var pageData = await _gameRecordClient.GetInterKnotReportDetailByPageAsync(role, month, type, page, pageSize);
            int pageStart = (page - 1) * pageSize + 1;
            for (int i = 0; i < pageData.List.Count; i++)
            {
                int recordIndex = pageStart + i;
                if (recordIndex >= startRecord && recordIndex <= endRecord)
                {
                    items.Add(pageData.List[i]);
                }
            }
        }
        return items;
    }



    /// <summary>
    /// 请求 API 最后一条明细并 UPSERT 到 SQLite。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型。</param>
    /// <param name="total">API 返回的该类型记录总数。</param>
    /// <param name="dapper">已打开的数据库连接。</param>
    private async Task UpsertInterKnotReportDetailLastItemAsync(GameRecordRole role, string month, string type, int total, System.Data.IDbConnection dapper)
    {
        var lastPage = await _gameRecordClient.GetInterKnotReportDetailByPageAsync(role, month, type, total, 1);
        var lastItem = lastPage.List.FirstOrDefault();
        if (lastItem is null)
        {
            return;
        }
        dapper.Execute("""
            INSERT OR REPLACE INTO ZZZInterKnotReportDetailItem (Uid, Id, DataMonth, DataType, Action, Time, Number)
            VALUES (@Uid, @Id, @DataMonth, @DataType, @Action, @Time, @Number);
            """, lastItem);
    }



    /// <summary>
    /// 判断指定月份是否已拉取过绳网月报明细（本地 <c>ZZZInterKnotReportDetailItem</c> 中存在至少一条记录）。
    /// 用于控制「统计数据」行刷新按钮的可见性：仅已「获取详情」的月份显示。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <returns>本地已有明细缓存时返回 true。</returns>
    public bool HasInterKnotReportDetail(long uid, string month)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QuerySingleOrDefault<int>(
            "SELECT COUNT(*) FROM ZZZInterKnotReportDetailItem WHERE Uid = @uid AND DataMonth = @month;",
            new { uid, month }) > 0;
    }



    /// <summary>
    /// 从本地 SQLite 读取指定月份全部资源类型的绳网月报明细，按时间升序。
    /// 供「每日数据」按日聚合使用，一次查询替代按 <see cref="InterKnotReportDataType"/> 分三次读取。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <returns>该月全部明细列表；未拉取过详情时为空列表。</returns>
    public List<InterKnotReportDetailItem> GetInterKnotReportDetailItems(long uid, string month)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<InterKnotReportDetailItem>("SELECT * FROM ZZZInterKnotReportDetailItem WHERE Uid=@uid AND DataMonth=@month ORDER BY Time;", new { uid, month }).ToList();
    }


    /// <summary>
    /// 从本地 SQLite 读取指定月份、指定资源类型的绳网月报明细，按时间升序。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型，取值见 <see cref="InterKnotReportDataType"/>。</param>
    /// <returns>明细列表；未拉取过详情时为空列表。</returns>
    public List<InterKnotReportDetailItem> GetInterKnotReportDetailItems(long uid, string month, string type)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<InterKnotReportDetailItem>("SELECT * FROM ZZZInterKnotReportDetailItem WHERE Uid=@uid AND DataMonth=@month AND DataType=@type ORDER BY Time;", new { uid, month, type }).ToList();
    }


    public async Task<ZZZGachaRecordData> GetZZZGachaRecordAsync(GameRecordRole role, int gachaType, long? endId = null, string? language = null, CancellationToken cancellationToken = default)
    {
        if (role is null)
        {
            throw new ArgumentNullException(nameof(role));
        }
        bool isHoyolab = role.GameBiz?.EndsWith("_global", StringComparison.OrdinalIgnoreCase) ?? false;
        IsHoyolab = isHoyolab;
        if (isHoyolab && !string.IsNullOrWhiteSpace(language))
        {
            // HoYoLAB 语言由请求头决定，统一通过 HoyolabClient.Language 生效。
            Language = language;
        }
        if (!isHoyolab)
        {
            await UpdateDeviceFpAsync(cancellationToken: cancellationToken);
        }
        return await _gameRecordClient.GetZZZGachaRecordAsync(role, gachaType, endId, language, cancellationToken);
    }




    #endregion




    #region Shiyu Defense



    public async Task<ShiyuDefenseWrapper> RefreshShiyuDefenseInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var wrapper = await _gameRecordClient.GetShiyuDefenseInfoAsync(role, schedule);
        if (wrapper.HadalVer is "v1" && wrapper.InfoV1 is not null)
        {
            var info = wrapper.InfoV1;
            if (info.HasData)
            {
                var obj = new
                {
                    role.Uid,
                    info.ScheduleId,
                    info.BeginTime,
                    info.EndTime,
                    info.Version,
                    info.HasData,
                    info.MaxRating,
                    info.MaxRatingTimes,
                    info.MaxLayer,
                    Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
                };
                using var dapper = DatabaseService.CreateConnection();
                dapper.Execute("""
                    INSERT OR REPLACE INTO ZZZShiyuDefenseInfo (Uid, ScheduleId, BeginTime, EndTime, Version, HasData, MaxRating, MaxRatingTimes, MaxLayer, Value)
                    VALUES (@Uid, @ScheduleId, @BeginTime, @EndTime, @Version, @HasData, @MaxRating, @MaxRatingTimes, @MaxLayer, @Value);
                    """, obj);
            }
        }
        else if (wrapper.HadalVer is "v2" && wrapper.InfoV2 is not null)
        {
            if (wrapper.InfoV2.Brief is not null)
            {
                var info = wrapper.InfoV2;
                if (info.PassFifthFloor)
                {
                    var obj = new
                    {
                        role.Uid,
                        info.ScheduleId,
                        info.BeginTime,
                        info.EndTime,
                        info.Version,
                        info.HasData,
                        info.MaxRating,
                        info.V2Score,
                        Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
                    };
                    using var dapper = DatabaseService.CreateConnection();
                    dapper.Execute("""
                        INSERT OR REPLACE INTO ZZZShiyuDefenseInfo (Uid, ScheduleId, BeginTime, EndTime, Version, HasData, MaxRating, V2Score, Value)
                        VALUES (@Uid, @ScheduleId, @BeginTime, @EndTime, @Version, @HasData, @MaxRating, @V2Score, @Value);
                        """, obj);
                }
            }
        }
        return wrapper;
    }



    public List<ShiyuDefenseInfo> GetShiyuDefenseInfoList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<ShiyuDefenseInfo>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<ShiyuDefenseInfo>("""
            SELECT Uid, ScheduleId, BeginTime, EndTime, Version, HasData, MaxRating, MaxRatingTimes, MaxLayer, V2Score FROM ZZZShiyuDefenseInfo WHERE Uid = @Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public ShiyuDefenseInfoBase? GetShiyuDefenseInfo(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        (string version, string value) = dapper.QueryFirstOrDefault<(string Version, string Value)>("""
            SELECT Version, Value FROM ZZZShiyuDefenseInfo WHERE Uid = @Uid And ScheduleId = @scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (version is "v1")
        {
            return JsonSerializer.Deserialize<ShiyuDefenseInfo>(value);
        }
        else if (version is "v2")
        {
            return JsonSerializer.Deserialize<ShiyuDefenseInfoV2>(value);
        }
        return null;
    }



    #endregion




    #region Deadly Assault



    public async Task<DeadlyAssaultInfo> RefreshDeadlyAssaultInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var info = await _gameRecordClient.GetDeadlyAssaultInfoAsync(role, schedule);
        if (!info.HasData)
        {
            return info;
        }
        var obj = new
        {
            role.Uid,
            info.ZoneId,
            info.StartTime,
            info.EndTime,
            info.HasData,
            info.RankPercent,
            info.TotalScore,
            info.TotalStar,
            Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
        };
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("""
            INSERT OR REPLACE INTO ZZZDeadlyAssaultInfo (Uid, ZoneId, StartTime, EndTime, HasData, RankPercent, TotalScore, TotalStar, Value)
            VALUES (@Uid, @ZoneId, @StartTime, @EndTime, @HasData, @RankPercent, @TotalScore, @TotalStar, @Value);
            """, obj);
        return info;
    }



    public List<DeadlyAssaultInfo> GetDeadlyAssaultInfoList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<DeadlyAssaultInfo>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<DeadlyAssaultInfo>("""
            SELECT Uid, ZoneId, StartTime, EndTime, HasData, RankPercent, TotalScore, TotalStar FROM ZZZDeadlyAssaultInfo WHERE Uid = @Uid ORDER BY ZoneId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public DeadlyAssaultInfo? GetDeadlyAssaultInfo(GameRecordRole role, int zoneId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM ZZZDeadlyAssaultInfo WHERE Uid = @Uid And ZoneId = @zoneId LIMIT 1;
            """, new { role.Uid, zoneId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return JsonSerializer.Deserialize<DeadlyAssaultInfo>(value);
    }



    #endregion




    #region Daily Note



    public async Task<BH3DailyNote> GetBH3DailyNoteAsync(GameRecordRole role, bool forceUpdate = false, CancellationToken cancellationToken = default)
    {
        string key = $"{nameof(BH3DailyNote)}_{role.Region}_{role.Uid}";
        if (forceUpdate || !_memoryCache.TryGetValue(key, out BH3DailyNote? note))
        {
            note = await _gameRecordClient.GetBH3DailyNoteAsync(role, cancellationToken);
            _memoryCache.Set(key, note, TimeSpan.FromMinutes(5));
        }
        return note!;
    }



    public async Task<GenshinDailyNote> GetGenshinDailyNoteAsync(GameRecordRole role, bool forceUpdate = false, CancellationToken cancellationToken = default)
    {
        string key = $"{nameof(GenshinDailyNote)}_{role.Region}_{role.Uid}";
        if (forceUpdate || !_memoryCache.TryGetValue(key, out GenshinDailyNote? note))
        {
            note = await _gameRecordClient.GetGenshinDailyNoteAsync(role, cancellationToken);
            _memoryCache.Set(key, note, TimeSpan.FromMinutes(5));
        }
        return note!;
    }



    public async Task<StarRailDailyNote> GetStarRailDailyNoteAsync(GameRecordRole role, bool forceUpdate = false, CancellationToken cancellationToken = default)
    {
        string key = $"{nameof(StarRailDailyNote)}_{role.Region}_{role.Uid}";
        if (forceUpdate || !_memoryCache.TryGetValue(key, out StarRailDailyNote? note))
        {
            note = await _gameRecordClient.GetStarRailDailyNoteAsync(role, cancellationToken);
            _memoryCache.Set(key, note, TimeSpan.FromMinutes(5));
        }
        return note!;
    }


    public async Task<ZZZDailyNote> GetZZZDailyNoteAsync(GameRecordRole role, bool forceUpdate = false, CancellationToken cancellationToken = default)
    {
        string key = $"{nameof(ZZZDailyNote)}_{role.Region}_{role.Uid}";
        if (forceUpdate || !_memoryCache.TryGetValue(key, out ZZZDailyNote? note))
        {
            note = await _gameRecordClient.GetZZZDailyNoteAsync(role, cancellationToken);
            _memoryCache.Set(key, note, TimeSpan.FromMinutes(5));
        }
        return note!;
    }




    #endregion




    #region Stygian Onslaught


    public async Task<List<StygianOnslaughtInfo>> RefreshStygianOnslaughtInfosAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        var infos = await _gameRecordClient.GetStygianOnslaughtInfosAsync(role, cancellationToken);
        if (infos.Count == 0)
        {
            return infos;
        }
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        foreach (var info in infos)
        {
            var obj = new
            {
                info.Uid,
                info.ScheduleId,
                info.StartDateTime,
                info.EndDateTime,
                info.Difficulty,
                info.Second,
                Value = JsonSerializer.Serialize(info, AppConfig.JsonSerializerOptions),
            };
            dapper.Execute("""
            INSERT OR REPLACE INTO GenshinStygianOnslaughtInfo (Uid, ScheduleId, StartDateTime, EndDateTime, Difficulty, Second, Value)
            VALUES (@Uid, @ScheduleId, @StartDateTime, @EndDateTime, @Difficulty, @Second, @Value);
            """, obj, t);
        }
        t.Commit();
        return infos;
    }



    public List<StygianOnslaughtInfo> GetStygianOnslaughtInfoList(GameRecordRole role)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<StygianOnslaughtInfo>("""
            SELECT Uid, ScheduleId, StartDateTime, EndDateTime, Difficulty, Second FROM GenshinStygianOnslaughtInfo WHERE Uid = @Uid ORDER BY ScheduleId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public StygianOnslaughtInfo? GetStygianOnslaughtInfo(GameRecordRole role, int scheduleId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var value = dapper.QueryFirstOrDefault<string>("""
            SELECT Value FROM GenshinStygianOnslaughtInfo WHERE Uid = @Uid And ScheduleId = @scheduleId LIMIT 1;
            """, new { role.Uid, scheduleId });
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return JsonSerializer.Deserialize<StygianOnslaughtInfo>(value);
    }



    #endregion




    #region Star Rail Challenge Peak




    public List<ChallengePeakData> GetStarRailChallengePeakDataList(GameRecordRole role)
    {
        if (role is null)
        {
            return new List<ChallengePeakData>();
        }
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<ChallengePeakData>("""
            SELECT Uid, GroupId, GameVersion, BossStars, MobStars, BossIcon FROM StarRailChallengePeakData WHERE Uid = @Uid ORDER BY GroupId DESC;
            """, new { role.Uid });
        return list.ToList();
    }



    public async Task RefreshStarRailChallengePeakDataAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        using var dapper = DatabaseService.CreateConnection();

        var data = await _gameRecordClient.GetStarRailChallengePeakDataAsync(role, 1, cancellationToken);
        if (data.ChallengePeakRecords?.Count == 1)
        {
            var record = data.ChallengePeakRecords[0];
            var obj = new
            {
                role.Uid,
                record.Group.GroupId,
                record.Group.GameVersion,
                record.BossStars,
                record.MobStars,
                BossIcon = record.BossInfo.Icon,
                Value = JsonSerializer.Serialize(data, AppConfig.JsonSerializerOptions),
            };
            dapper.Execute("""
                INSERT OR REPLACE INTO StarRailChallengePeakData (Uid, GroupId, GameVersion, BossStars, MobStars, BossIcon, Value)
                VALUES (@Uid, @GroupId, @GameVersion, @BossStars, @MobStars, @BossIcon, @Value);
                """, obj);
        }

        data = await _gameRecordClient.GetStarRailChallengePeakDataAsync(role, 3, cancellationToken);
        foreach (var record in data.ChallengePeakRecords.ToList())
        {
            data.ChallengePeakRecords.Clear();
            var queryData = dapper.QueryFirstOrDefault<ChallengePeakData>("""
                SELECT BossStars, MobStars FROM StarRailChallengePeakData WHERE Uid = @Uid AND GroupId = @GroupId LIMIT 1;
                """, new { role.Uid, record.Group.GroupId });
            if (queryData is null)
            {
                data.ChallengePeakRecords.Add(record);
                data.ChallengePeakBestRecordBrief = new ChallengePeakBestRecordBrief
                {
                    BossStars = record.BossStars,
                    MobStars = record.MobStars,
                };
                var obj = new
                {
                    role.Uid,
                    record.Group.GroupId,
                    record.Group.GameVersion,
                    record.BossStars,
                    record.MobStars,
                    BossIcon = record.BossInfo.Icon,
                    Value = JsonSerializer.Serialize(data, AppConfig.JsonSerializerOptions),
                };
                dapper.Execute("""
                    INSERT OR REPLACE INTO StarRailChallengePeakData (Uid, GroupId, GameVersion, BossStars, MobStars, BossIcon, Value)
                    VALUES (@Uid, @GroupId, @GameVersion, @BossStars, @MobStars, @BossIcon, @Value);
                    """, obj);
            }
            else if (record.BossStars > queryData.BossStars || record.MobStars > queryData.MobStars)
            {
                var queryText = dapper.QueryFirstOrDefault<string>("""
                    SELECT Value FROM StarRailChallengePeakData WHERE Uid = @Uid AND GroupId = @GroupId LIMIT 1;
                    """, new { role.Uid, record.Group.GroupId });
                if (!string.IsNullOrWhiteSpace(queryText))
                {
                    var queryValue = JsonSerializer.Deserialize<ChallengePeakData>(queryText);
                    if (queryValue is not null)
                    {
                        queryValue.ChallengePeakRecords.Clear();
                        queryValue.ChallengePeakRecords.Add(record);
                        queryValue.ChallengePeakBestRecordBrief ??= new();
                        queryValue.ChallengePeakBestRecordBrief.BossStars = record.BossStars;
                        queryValue.ChallengePeakBestRecordBrief.MobStars = record.MobStars;

                        var obj = new
                        {
                            role.Uid,
                            record.Group.GroupId,
                            record.Group.GameVersion,
                            record.BossStars,
                            record.MobStars,
                            BossIcon = record.BossInfo.Icon,
                            Value = JsonSerializer.Serialize(queryValue),
                        };
                        dapper.Execute("""
                            INSERT OR REPLACE INTO StarRailChallengePeakData (Uid, GroupId, GameVersion, BossStars, MobStars, BossIcon, Value)
                            VALUES (@Uid, @GroupId, @GameVersion, @BossStars, @MobStars, @BossIcon, @Value);
                            """, obj);
                    }
                }
            }
        }
    }



    public ChallengePeakData? GetStarRailChallengePeakData(GameRecordRole role, int groupId)
    {
        using var dapper = DatabaseService.CreateConnection();
        var queryText = dapper.QueryFirstOrDefault<string>("""
                    SELECT Value FROM StarRailChallengePeakData WHERE Uid = @Uid AND GroupId = @groupId LIMIT 1;
                    """, new { role.Uid, groupId });
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            return JsonSerializer.Deserialize<ChallengePeakData>(queryText);
        }
        return null;
    }




    #endregion




    #region Sign In


    /// <summary>
    /// 签到前准备：按角色区服切换 CN/OS Client，国服同步设备指纹。
    /// </summary>
    /// <param name="role">游戏角色，用于判断 global / cn。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task PrepareSignInClientAsync(GameRecordRole role, CancellationToken cancellationToken)
    {
        IsHoyolab = role.GameBiz?.EndsWith("_global", StringComparison.OrdinalIgnoreCase) ?? false;
        if (!IsHoyolab)
        {
            await UpdateDeviceFpAsync(cancellationToken: cancellationToken);
        }
    }


    /// <summary>
    /// 本月签到奖励列表。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当月每日奖励。</returns>
    public async Task<SignInReward> GetSignInRewardAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        await PrepareSignInClientAsync(role, cancellationToken);
        return await _gameRecordClient.GetSignInRewardAsync(role, cancellationToken);
    }


    /// <summary>
    /// 当前签到状态。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已签天数、今日是否已签等。</returns>
    public async Task<SignInRewardInfo> GetSignInInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        await PrepareSignInClientAsync(role, cancellationToken);
        return await _gameRecordClient.GetSignInInfoAsync(role, cancellationToken);
    }


    /// <summary>
    /// 补签信息。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补签次数与货币消耗。</returns>
    public async Task<SignInResignInfo> GetSignInResignInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        await PrepareSignInClientAsync(role, cancellationToken);
        return await _gameRecordClient.GetSignInResignInfoAsync(role, cancellationToken);
    }


    /// <summary>
    /// 执行今日签到。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签到结果。</returns>
    public async Task<SignInResult> SignInAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        await PrepareSignInClientAsync(role, cancellationToken);
        return await _gameRecordClient.SignInAsync(role, cancellationToken);
    }


    /// <summary>
    /// 执行补签。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补签结果。</returns>
    public async Task<SignInResult> ReSignInAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        await PrepareSignInClientAsync(role, cancellationToken);
        return await _gameRecordClient.ReSignInAsync(role, cancellationToken);
    }


    #endregion


}
