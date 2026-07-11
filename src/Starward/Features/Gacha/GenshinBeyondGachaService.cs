using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Localization;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

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
        return dapper.Query<long>($"SELECT DISTINCT Uid FROM {GachaTableName};").ToList();
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
    /// <exception cref="miHoYoApiException">全部候选 authkey 过期时抛出。</exception>
    public async Task<string?> GetValidatedGachaLogUrlFromWebCacheAsync(GameBiz gameBiz, string path, CancellationToken cancellationToken = default)
    {
        var candidates = GenshinBeyondGachaClient.GetGachaUrlCandidatesFromWebCache(gameBiz, path);
        if (candidates.Count == 0)
        {
            return null;
        }

        miHoYoApiException? lastAuthError = null;
        foreach (var url in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client.GetUidByGachaUrlAsync(url);
                return url;
            }
            catch (miHoYoApiException ex) when (ex.ReturnCode is -101 or -1)
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



    public GenshinBeyondGachaTypeStats? GetGachaTypeStatsType1000(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GenshinBeyondGachaItemEx>("""
            SELECT item.*, info.Icon FROM GenshinBeyondGachaItem item LEFT JOIN GenshinBeyondGachaInfo info
            ON item.ItemId = info.Id WHERE Uid = @uid AND OpGachaType = 1000 ORDER BY item.Id;
            """, new { uid }).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        int index = 0;
        int pity = 0;
        foreach (var item in list)
        {
            item.Index = ++index;
            item.Pity = ++pity;
            if (item.RankType == 4)
            {
                pity = 0;
            }
        }

        var stats = new GenshinBeyondGachaTypeStats
        {
            GachaType = 1000,
            GachaTypeText = CoreLang.GachaType_StandardOde,
            Count = list.Count,
            Count_5 = list.Count(x => x.RankType == 5),
            Count_4 = list.Count(x => x.RankType == 4),
            Count_3 = list.Count(x => x.RankType == 3),
            Count_2 = list.Count(x => x.RankType == 2),
            StartTime = list.First().Time,
            EndTime = list.Last().Time,
        };
        stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
        stats.Ratio_4 = (double)stats.Count_4 / stats.Count;
        stats.Ratio_3 = (double)stats.Count_3 / stats.Count;
        stats.Ratio_2 = (double)stats.Count_2 / stats.Count;
        stats.List_5 = list.Where(x => x.RankType == 5).Reverse().ToList();
        stats.List_4 = list.Where(x => x.RankType == 4).Reverse().ToList();
        stats.List_3 = list.Where(x => x.RankType == 3).Reverse().ToList();

        stats.Pity_4 = list.Last().Pity;
        if (list.Last().RankType == 4)
        {
            stats.Pity_4 = 0;
        }
        stats.Average_4 = (double)(stats.Count - stats.Pity_4) / stats.Count_4;
        stats.Pity_3 = list.Count - 1 - list.FindLastIndex(x => x.RankType == 3);
        int pity_3 = 0;
        foreach (var item in list)
        {
            pity_3++;
            if (item.RankType == 3)
            {
                item.Pity = pity_3;
                pity_3 = 0;
            }
        }

        stats.List_4.Insert(0, new GenshinBeyondGachaItemEx
        {
            OpGachaType = 1000,
            ItemName = Lang.GachaStatsCard_Pity,
            Pity = stats.Pity_4,
            Time = list.Last().Time,
        });
        stats.List_3.Insert(0, new GenshinBeyondGachaItemEx
        {
            OpGachaType = 1000,
            ItemName = Lang.GachaStatsCard_Pity,
            Pity = stats.Pity_3,
            Time = list.Last().Time,
        });

        return stats;
    }


    public GenshinBeyondGachaTypeStats? GetGachaTypeStatsType2000(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GenshinBeyondGachaItemEx>("""
            SELECT item.*, info.Icon FROM GenshinBeyondGachaItem item LEFT JOIN GenshinBeyondGachaInfo info
            ON item.ItemId = info.Id WHERE Uid = @uid AND OpGachaType != 1000 ORDER BY item.Id;
            """, new { uid }).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        int index = 0;
        int pity = 0;
        foreach (var item in list)
        {
            item.Index = ++index;
            item.Pity = ++pity;
            if (item.RankType == 5)
            {
                pity = 0;
            }
        }

        var stats = new GenshinBeyondGachaTypeStats
        {
            GachaType = 2000,
            GachaTypeText = CoreLang.GachaType_EventOde,
            Count = list.Count,
            Count_5 = list.Count(x => x.RankType == 5),
            Count_4 = list.Count(x => x.RankType == 4),
            Count_3 = list.Count(x => x.RankType == 3),
            Count_2 = list.Count(x => x.RankType == 2),
            StartTime = list.First().Time,
            EndTime = list.Last().Time,
        };
        stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
        stats.Ratio_4 = (double)stats.Count_4 / stats.Count;
        stats.Ratio_3 = (double)stats.Count_3 / stats.Count;
        stats.Ratio_2 = (double)stats.Count_2 / stats.Count;
        stats.List_5 = list.Where(x => x.RankType == 5).Reverse().ToList();
        stats.List_4 = list.Where(x => x.RankType == 4).Reverse().ToList();
        stats.List_3 = list.Where(x => x.RankType == 3).Reverse().ToList();

        stats.Pity_5 = list.Last().Pity;
        if (list.Last().RankType == 5)
        {
            stats.Pity_5 = 0;
        }
        stats.Average_5 = (double)(stats.Count - stats.Pity_5) / stats.Count_5;
        stats.Pity_4 = list.Count - 1 - list.FindLastIndex(x => x.RankType == 4);
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

        stats.List_5.Insert(0, new GenshinBeyondGachaItemEx
        {
            OpGachaType = 2000,
            ItemName = Lang.GachaStatsCard_Pity,
            Pity = stats.Pity_5,
            Time = list.Last().Time,
        });
        stats.List_4.Insert(0, new GenshinBeyondGachaItemEx
        {
            OpGachaType = 2000,
            ItemName = Lang.GachaStatsCard_Pity,
            Pity = stats.Pity_4,
            Time = list.Last().Time,
        });

        return stats;
    }


    public List<GenshinBeyondGachaItemEx>? GetGachaItemStats(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GenshinBeyondGachaItemEx>("""
            SELECT item.*, info.Icon FROM GenshinBeyondGachaItem item LEFT JOIN GenshinBeyondGachaInfo info
            ON item.ItemId = info.Id WHERE Uid = @uid ORDER BY item.Id;
            """, new { uid }).ToList();
        if (list.Count == 0)
        {
            return null;
        }
        return list.GroupBy(x => x.ItemId)
                   .Select(x => { var item = x.First(); item.Count = x.Count(); return item; })
                   .OrderByDescending(x => x.RankType)
                   .ThenByDescending(x => x.Count)
                   .ThenByDescending(x => x.Time)
                   .ToList();
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



    public async Task UpdateGachaInfoAsync(CancellationToken cancellationToken = default)
    {
        var data = await _client.GetGenshinBeyondGachaInfoAsync(cancellationToken);
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        const string insertSql = """INSERT OR REPLACE INTO GenshinBeyondGachaInfo (Id, Name, Rank, Icon) VALUES (@Id, @Name, @Rank, @Icon);""";
        dapper.Execute(insertSql, data, t);
        t.Commit();
    }



    /// <summary>
    /// 确保本地已有千星奇域物品信息（图标）：表为空时才全量下载 <see cref="UpdateGachaInfoAsync"/>。
    /// 软件首次启动由 <see cref="GachaItemNameService"/> 调用完成全量更新（失败则打开页面/下次启动时重试）；
    /// 此后版本更新带来的新物品由更新记录时的 <see cref="HasUnknownItems"/> 检测按需增量补全。
    /// </summary>
    public async Task EnsureGachaInfoAsync(CancellationToken cancellationToken = default)
    {
        using (var dapper = DatabaseService.CreateConnection())
        {
            if (dapper.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM GenshinBeyondGachaInfo;") > 0)
            {
                return;
            }
        }
        await UpdateGachaInfoAsync(cancellationToken);
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


public partial class GenshinBeyondGachaItemEx : GenshinBeyondGachaItem
{
    /// <summary>
    /// 相同保底卡池中的顺序
    /// </summary>
    public int Index { get; set; }

    public int Pity { get; set; }

    public string Icon { get; set; }

    public int Count { get; set; }

}



public class GenshinBeyondGachaTypeStats
{

    public int GachaType { get; set; }

    public string GachaTypeText { get; set; }

    public int Count { get; set; }

    public int Pity_5 { get; set; }

    public int Pity_4 { get; set; }

    public int Pity_3 { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public int Count_5 { get; set; }

    public int Count_4 { get; set; }

    public int Count_3 { get; set; }

    public int Count_2 { get; set; }

    public double Ratio_5 { get; set; }

    public double Ratio_4 { get; set; }

    public double Ratio_3 { get; set; }

    public double Ratio_2 { get; set; }

    public double Average_5 { get; set; }

    public double Average_4 { get; set; }

    public List<GenshinBeyondGachaItemEx> List_5 { get; set; }

    public List<GenshinBeyondGachaItemEx> List_4 { get; set; }

    public List<GenshinBeyondGachaItemEx> List_3 { get; set; }

}


public partial class GenshinBeyondGachaPityProgressBackgroundBrushConverter : IValueConverter
{
    private static Color Red = Color.FromArgb(0xFF, 0xC8, 0x3C, 0x23);
    private static Color Green = Color.FromArgb(0xFF, 0x00, 0xE0, 0x79);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is GenshinBeyondGachaItemEx item)
        {
            int pity = item.Pity;
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), Opacity = 0.4 };
            int point = 64;
            double guarantee = 70;
            double offset = pity / guarantee;
            if (pity < point)
            {
                brush.GradientStops.Add(new GradientStop { Color = Green, Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Green, Offset = offset });
                brush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = offset });
            }
            else
            {
                brush.GradientStops.Add(new GradientStop { Color = Red, Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Red, Offset = offset });
                brush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = offset });
            }
            return brush;
        }
        return null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}