using Dapper;
using Microsoft.Data.Sqlite;
using Starward.Features.Database;
using Starward.Features.PlayTime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Starward.Features.LanSync;


/// <summary>
/// 局域网同步的数据类别。
/// </summary>
[Flags]
public enum LanSyncCategories
{
    None = 0,

    /// <summary>抽卡记录。</summary>
    Gacha = 1,

    /// <summary>米游社 / HoYoLAB 工具箱数据（深渊、札记、月报等）。</summary>
    GameRecord = 2,

    /// <summary>游戏时长。</summary>
    PlayTime = 4,

    All = Gacha | GameRecord | PlayTime,
}



/// <summary>
/// 按类别统计的记录数：合并结果里是本机新增的行数，共享摘要里是本机可共享的行数。
/// </summary>
/// <param name="PlayTime">游戏时长按会话计。</param>
public readonly record struct LanSyncCounts(int Gacha, int GameRecord, int PlayTime)
{
    public int Total => Gacha + GameRecord + PlayTime;
}



/// <summary>
/// 同步快照：共享端把白名单表拷进一份独立的 SQLite 文件，请求端 ATTACH 后逐表只插入本机没有的行。
/// </summary>
/// <remarks>
/// 只拷白名单里的表，快照不含 Setting / KVT / 米游社账号表，Cookie 与设备指纹不会离开本机。
/// 合并只做 INSERT，不 UPDATE、不 DELETE：本机已有的同一条记录保持原样，对方的数据也不受影响。
/// </remarks>
internal static class LanSyncSnapshot
{

    private const string SnapshotSchema = "snap";

    private const string MetaTable = "LanSyncMeta";

    private const string BackupFilePrefix = "StarwardDatabase_BeforeLanSync_";

    /// <summary>同步前备份保留的份数，不压缩，份数多了占空间。</summary>
    private const int BackupKeepCount = 3;


    /// <summary>
    /// 参与同步的表。
    /// </summary>
    /// <param name="Name">表名。</param>
    /// <param name="Category">所属类别。</param>
    /// <param name="MatchColumns">
    /// 为空时靠主键 / 唯一索引去重（INSERT OR IGNORE）。
    /// 自增 Id 的明细表在两台设备上 Id 对不上，改用这些列判断本机是否已有：
    /// 与刷新接口的增量逻辑一致，同一账号、月份、类型下以 Time 区分记录。
    /// </param>
    /// <param name="AutoIncrementId">Id 是本机自增列，不能跨设备复制。</param>
    private sealed record SyncTable(string Name, LanSyncCategories Category, string[]? MatchColumns = null, bool AutoIncrementId = false);


    private static readonly SyncTable[] Tables =
    [
        new("GenshinGachaItem", LanSyncCategories.Gacha),
        new("StarRailGachaItem", LanSyncCategories.Gacha),
        new("ZZZGachaItem", LanSyncCategories.Gacha),
        new("GenshinBeyondGachaItem", LanSyncCategories.Gacha),

        new("GenshinSpiralAbyssInfo", LanSyncCategories.GameRecord),
        new("GenshinImaginariumTheaterInfo", LanSyncCategories.GameRecord),
        new("GenshinStygianOnslaughtInfo", LanSyncCategories.GameRecord),
        new("GenshinTravelersDiaryMonthData", LanSyncCategories.GameRecord),
        new("GenshinTravelersDiaryAwardItem", LanSyncCategories.GameRecord, ["Uid", "Year", "Month", "Type", "Time"], AutoIncrementId: true),
        new("StarRailForgottenHallInfo", LanSyncCategories.GameRecord),
        new("StarRailPureFictionInfo", LanSyncCategories.GameRecord),
        new("StarRailApocalypticShadowInfo", LanSyncCategories.GameRecord),
        new("StarRailSimulatedUniverseRecord", LanSyncCategories.GameRecord),
        new("StarRailChallengePeakData", LanSyncCategories.GameRecord),
        new("StarRailTrailblazeCalendarMonthData", LanSyncCategories.GameRecord),
        new("StarRailTrailblazeCalendarDetailItem", LanSyncCategories.GameRecord, ["Uid", "Month", "Type", "Time"], AutoIncrementId: true),
        new("ZZZShiyuDefenseInfo", LanSyncCategories.GameRecord),
        new("ZZZDeadlyAssaultInfo", LanSyncCategories.GameRecord),
        new("ZZZInterKnotReportSummary", LanSyncCategories.GameRecord),
        new("ZZZInterKnotReportDetailItem", LanSyncCategories.GameRecord),

        // 心跳表 PlayTimeItem 本身不同步（主键只有时间戳，跨设备会互相覆盖）；其中已结束的会话在导出时折算进快照的这张表
        new("PlayTimeStats", LanSyncCategories.PlayTime, AutoIncrementId: true),
    ];



    /// <summary>
    /// 快照等临时文件所在目录。
    /// </summary>
    private static string TempFolder => Path.Combine(Path.GetTempPath(), "Moonward", "LanSync");


    /// <summary>
    /// 生成一个不存在的临时文件路径。
    /// </summary>
    /// <param name="extension">扩展名（含点）。</param>
    public static string CreateTempFilePath(string extension)
    {
        Directory.CreateDirectory(TempFolder);
        return Path.Combine(TempFolder, $"{Guid.CreateVersion7():N}{extension}");
    }


    /// <summary>
    /// 删除临时文件，失败时忽略（文件在临时目录，系统会清理）。
    /// </summary>
    public static void DeleteTempFile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }
        foreach (string path in new[] { file, $"{file}-journal" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }
    }



    /// <summary>
    /// 把本机的白名单表导出到新的快照文件。
    /// </summary>
    /// <param name="file">快照路径，必须尚不存在（ATTACH 会新建）。</param>
    /// <param name="deviceName">写进快照元数据的设备名，仅供排查。</param>
    public static void Export(string file, string deviceName)
    {
        // 不进连接池，关闭即释放快照文件，调用方随后要压缩并删除它
        using var con = DatabaseService.CreateUnpooledConnection();
        con.Execute($"ATTACH DATABASE @file AS {SnapshotSchema};", new { file });
        // 延迟事务：主库只读，拿到跨表一致的读快照即可，不去抢主库的写锁
        using var transaction = con.BeginTransaction(deferred: true);
        foreach (SyncTable table in Tables)
        {
            if (TableExists(con, "main", table.Name, transaction))
            {
                con.Execute($"CREATE TABLE {SnapshotSchema}.{Quote(table.Name)} AS SELECT * FROM main.{Quote(table.Name)};", transaction: transaction);
            }
        }
        if (TableExists(con, SnapshotSchema, "PlayTimeStats", transaction))
        {
            List<PlayTimeStats> sessions = GetUnarchivedSessions(con, transaction);
            con.Execute($"""
                INSERT INTO {SnapshotSchema}.PlayTimeStats (GameBiz, Pid, StartTime, EndTime, Interruption, Type)
                VALUES (@GameBiz, @Pid, @StartTime, @EndTime, @Interruption, @Type);
                """, sessions, transaction);
        }
        con.Execute($"CREATE TABLE {SnapshotSchema}.{MetaTable} (Key TEXT NOT NULL PRIMARY KEY, Value TEXT);", transaction: transaction);
        con.Execute($"INSERT INTO {SnapshotSchema}.{MetaTable} (Key, Value) VALUES (@Key, @Value);", new[]
        {
            new { Key = "Protocol", Value = LanSyncProtocol.Version.ToString() },
            new { Key = "AppVersion", Value = AppConfig.AppVersion },
            new { Key = "DeviceName", Value = deviceName },
            new { Key = "UserVersion", Value = DatabaseService.CurrentUserVersion.ToString() },
            new { Key = "CreatedAt", Value = DateTimeOffset.Now.ToString("O") },
        }, transaction);
        transaction.Commit();
    }



    /// <summary>
    /// 心跳表 PlayTimeItem 里已经结束、但本机还没归档成会话的时长。
    /// 归档只在打开时长统计时发生，不补上的话，没打开过统计的设备只能同步出很少的时长。
    /// </summary>
    private static List<PlayTimeStats> GetUnarchivedSessions(SqliteConnection con, SqliteTransaction? transaction)
    {
        if (!TableExists(con, "main", "PlayTimeItem", transaction))
        {
            return [];
        }
        List<PlayTimeItemStruct> items = con.Query<PlayTimeItemStruct>("SELECT TimeStamp, GameBiz, Pid, State FROM main.PlayTimeItem ORDER BY TimeStamp;", transaction: transaction).ToList();
        return PlayTimeStatsService.GetFinishedStats(items);
    }



    /// <summary>
    /// 统计本机可共享的记录数。共享窗口展示它，让用户确认共享的是哪份数据。
    /// </summary>
    public static LanSyncCounts GetLocalSummary()
    {
        using var con = DatabaseService.CreateConnection();
        int gacha = 0, gameRecord = 0, playTime = 0;
        foreach (SyncTable table in Tables)
        {
            if (!TableExists(con, "main", table.Name, null))
            {
                continue;
            }
            int count = con.QueryFirst<int>($"SELECT COUNT(*) FROM main.{Quote(table.Name)};");
            switch (table.Category)
            {
                case LanSyncCategories.Gacha:
                    gacha += count;
                    break;
                case LanSyncCategories.GameRecord:
                    gameRecord += count;
                    break;
                case LanSyncCategories.PlayTime:
                    playTime += count;
                    break;
            }
        }
        playTime += GetUnarchivedSessions(con, null).Count;
        return new LanSyncCounts(gacha, gameRecord, playTime);
    }



    /// <summary>
    /// 把快照里本机没有的记录插入本机，全部在一个事务里完成。
    /// </summary>
    /// <param name="file">已解压并校验过的快照文件。</param>
    /// <param name="categories">要合并的类别。</param>
    /// <param name="playTimeBizs">快照里出现过的时长 GameBiz，调用方据此重算缓存的总时长。</param>
    /// <returns>各类别新增的行数。</returns>
    /// <exception cref="LanSyncException">文件不是 Moonward 快照。</exception>
    public static LanSyncCounts Merge(string file, LanSyncCategories categories, out List<string> playTimeBizs)
    {
        playTimeBizs = [];
        // 不进连接池，关闭即释放附加的快照文件
        using var con = DatabaseService.CreateUnpooledConnection();
        con.Execute($"ATTACH DATABASE @file AS {SnapshotSchema};", new { file });
        if (!TableExists(con, SnapshotSchema, MetaTable, null))
        {
            throw new LanSyncException(LanSyncErrorKind.Failed, "The received file is not a Moonward snapshot.");
        }

        int gacha = 0, gameRecord = 0, playTime = 0;
        using var transaction = con.BeginTransaction();
        foreach (SyncTable table in Tables)
        {
            if (!categories.HasFlag(table.Category))
            {
                continue;
            }
            int added = MergeTable(con, table, transaction);
            switch (table.Category)
            {
                case LanSyncCategories.Gacha:
                    gacha += added;
                    break;
                case LanSyncCategories.GameRecord:
                    gameRecord += added;
                    break;
                case LanSyncCategories.PlayTime:
                    playTime += added;
                    break;
            }
        }
        if (categories.HasFlag(LanSyncCategories.PlayTime) && TableExists(con, SnapshotSchema, "PlayTimeStats", transaction))
        {
            playTimeBizs = con.Query<string>($"SELECT DISTINCT GameBiz FROM {SnapshotSchema}.PlayTimeStats WHERE GameBiz IS NOT NULL;", transaction: transaction).ToList();
        }
        transaction.Commit();
        return new LanSyncCounts(gacha, gameRecord, playTime);
    }



    /// <summary>
    /// 把一张表里本机没有的行插入本机。
    /// </summary>
    /// <returns>新增的行数；两边任一缺表，或对方缺少本机的必填列时为 0。</returns>
    private static int MergeTable(SqliteConnection con, SyncTable table, SqliteTransaction transaction)
    {
        List<TableColumn> localColumns = GetColumns(con, "main", table.Name, transaction);
        List<TableColumn> snapshotColumns = GetColumns(con, SnapshotSchema, table.Name, transaction);
        if (localColumns.Count == 0 || snapshotColumns.Count == 0)
        {
            return 0;
        }

        var snapshotNames = snapshotColumns.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = new List<string>();
        foreach (TableColumn column in localColumns)
        {
            if (table.AutoIncrementId && string.Equals(column.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (snapshotNames.Contains(column.Name))
            {
                columns.Add(column.Name);
            }
            else if (column.IsNotNull != 0 && column.DefaultValue is null)
            {
                // 对方库版本较旧、缺少本机的必填列：整表跳过，不拿猜的值填进去
                return 0;
            }
        }
        if (table.MatchColumns is not null && !table.MatchColumns.All(x => columns.Contains(x, StringComparer.OrdinalIgnoreCase)))
        {
            return 0;
        }

        string target = $"main.{Quote(table.Name)}";
        string source = $"{SnapshotSchema}.{Quote(table.Name)}";
        string columnList = string.Join(", ", columns.Select(Quote));
        string sql;
        if (table.MatchColumns is null)
        {
            sql = $"INSERT OR IGNORE INTO {target} ({columnList}) SELECT {columnList} FROM {source};";
        }
        else
        {
            string selectList = string.Join(", ", columns.Select(x => $"s.{Quote(x)}"));
            string match = string.Join(" AND ", table.MatchColumns.Select(x => $"m.{Quote(x)} IS s.{Quote(x)}"));
            // SELECT 读到目标表时，SQLite 会先把结果算完再插入，所以快照里同一时间的多条记录会一起插入，不会被刚插入的第一条挡住
            sql = $"INSERT INTO {target} ({columnList}) SELECT {selectList} FROM {source} AS s WHERE NOT EXISTS (SELECT 1 FROM {target} AS m WHERE {match});";
        }
        return con.Execute(sql, transaction: transaction);
    }



    /// <summary>
    /// 合并前把本机数据库备份到 DatabaseBackup，只保留最近几份。
    /// </summary>
    public static void BackupBeforeMerge()
    {
        string folder = Path.Join(AppConfig.UserDataFolder, "DatabaseBackup");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"{BackupFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss}.db");
        using (var backup = new SqliteConnection($"DataSource={file};Pooling=False;"))
        {
            backup.Open();
            using var con = DatabaseService.CreateConnection();
            con.BackupDatabase(backup);
        }
        // 文件名里的时间可按字典序排序
        foreach (string old in Directory.GetFiles(folder, $"{BackupFilePrefix}*.db").OrderByDescending(x => x, StringComparer.Ordinal).Skip(BackupKeepCount))
        {
            try
            {
                File.Delete(old);
            }
            catch { }
        }
    }



    private sealed class TableColumn
    {
        public string Name { get; set; } = "";

        public long IsNotNull { get; set; }

        public string? DefaultValue { get; set; }
    }


    private static List<TableColumn> GetColumns(SqliteConnection con, string schema, string table, SqliteTransaction? transaction)
    {
        // NOTNULL 是 SQLite 关键字，列名与别名都不能裸写
        return con.Query<TableColumn>("""SELECT name AS Name, "notnull" AS IsNotNull, dflt_value AS DefaultValue FROM pragma_table_info(@table, @schema);""", new { table, schema }, transaction).ToList();
    }


    private static bool TableExists(SqliteConnection con, string schema, string table, SqliteTransaction? transaction)
    {
        return con.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type = 'table' AND name = @table;", new { table }, transaction) > 0;
    }


    private static string Quote(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

}
