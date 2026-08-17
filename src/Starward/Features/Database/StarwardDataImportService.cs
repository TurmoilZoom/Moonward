using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Starward.Features.Setting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Database;

/// <summary>
/// 从上游 Starward 只读导入数据到 Moonward。
/// <para>
/// 源目录只读：用 SQLite Backup + <see cref="File.Copy"/>，绝不 Move/Delete/写打开源库，
/// 避免影响用户继续使用 Starward。
/// </para>
/// <para>
/// 库版本策略（两边会各自继续演进）：
/// 共同祖先是 <see cref="CommonUserVersion"/>（v1–v18 脚本一致）。此后编号分叉——
/// Starward v19 = ExtraStarNum，v20 = ZZZ HasHard；
/// Moonward v19 = GachaItemName，v20 = ExtraStarNum，v21 = DROP GameAccount。
/// 导入时在<b>副本</b>上回退 Starward 独有变更，把 USER_VERSION 拉回共同祖先，
/// 再交给 Moonward 的 <c>DatabaseSqls</c> 往下跑。两边都有的列（ExtraStarNum）保留，
/// 由 <see cref="DatabaseService"/> 在执行对应脚本前跳过，以免 ALTER 失败并丢掉已有数据。
/// </para>
/// <para>
/// 上游每新增一个 <c>Sql_vN</c>：在 <see cref="StarwardOnlyRollbacks"/> 补反向 SQL，
/// 并更新 <see cref="KnownMaxStarwardUserVersion"/>。更高未知版本拒绝导入。
/// 若该变更 Moonward 也需要，追加 Moonward 自己的新 <c>Sql_vN</c>（禁止改已发布脚本）。
/// 变基后若某段脚本重新对齐，提高 <see cref="CommonUserVersion"/> 并删掉对应回退项。
/// </para>
/// </summary>
internal static class StarwardDataImportService
{

    /// <summary>v1–v18 与上游 Starward 脚本一致。</summary>
    public const int CommonUserVersion = 18;

    /// <summary>当前已编写回退脚本的最高上游 USER_VERSION。</summary>
    public const int KnownMaxStarwardUserVersion = 20;

    private const string DatabaseFileName = "StarwardDatabase.db";

    private const string StarwardRegistryKey = @"Software\Starward";


    /// <summary>探测到的上游 Starward 安装/数据位置（只读描述，不含句柄）。</summary>
    public sealed class StarwardInstallInfo
    {
        public string? UserDataFolder { get; init; }

        public string? CacheFolder { get; init; }

        public string? DatabasePath { get; init; }

        public string? BackgroundFolder { get; init; }

        public bool HasDatabase => !string.IsNullOrWhiteSpace(DatabasePath) && File.Exists(DatabasePath);
    }


    public readonly record struct ImportResult(bool ImportedDatabase);


    /// <summary>
    /// 回退 Starward 在共同祖先之后、且 Moonward 脚本里没有对等编号的变更。
    /// ExtraStarNum（Starward v19 / Moonward v20）是两边共有列，不在此删除。
    /// </summary>
    private static readonly (int Version, string ReverseSql)[] StarwardOnlyRollbacks =
    [
        (20, """
            -- Starward v20：危局强袭战绝境列。完整数据在 Value JSON 里，删列不丢记录。
            ALTER TABLE ZZZDeadlyAssaultInfo DROP COLUMN HasHard;
            ALTER TABLE ZZZDeadlyAssaultInfo DROP COLUMN HardTotalScore;
            ALTER TABLE ZZZDeadlyAssaultInfo DROP COLUMN HardTotalStar;
            """),
    ];


    /// <summary>
    /// 自动探测本机 Starward 数据：注册表 <c>UserDataFolder</c>，以及 <c>%LocalAppData%\Starward</c>。
    /// 只读注册表，不创建 <c>Software\Starward</c>。
    /// </summary>
    public static bool TryDetect(out StarwardInstallInfo info)
    {
        var dataFolders = new List<string>();
        var extraRoots = new List<string>();
        string? userDataFolder = ReadStarwardUserDataFolder();
        if (!string.IsNullOrWhiteSpace(userDataFolder))
        {
            dataFolders.Add(userDataFolder);
        }
        string cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starward");
        extraRoots.Add(cacheFolder);
        dataFolders.Add(cacheFolder);
        info = BuildInstallInfo(dataFolders, extraRoots);
        return info.HasDatabase;
    }


    /// <summary>
    /// 从用户所选目录解析 Starward 数据。支持：
    /// 数据目录（内含 <c>StarwardDatabase.db</c>）、便携版安装根（<c>config.ini</c>）、
    /// Velopack 的 <c>current</c> 子目录、可移动设备上的 <c>.cache\config.ini</c>。
    /// </summary>
    public static bool TryResolveFromDirectory(string folder, out StarwardInstallInfo info)
    {
        info = new StarwardInstallInfo();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        string picked = Path.GetFullPath(folder);
        string? parent = Directory.GetParent(picked)?.FullName;
        var dataFolders = new List<string> { picked };
        var extraRoots = new List<string> { picked };

        foreach (string? root in new[] { picked, parent })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }
            extraRoots.Add(root);
            extraRoots.Add(Path.Combine(root, ".cache"));
            dataFolders.Add(root);
            dataFolders.Add(Path.Combine(root, ".cache"));
        }

        foreach (string configPath in EnumerateConfigIniPaths(picked, parent))
        {
            if (TryReadUserDataFolderFromConfig(configPath, out string? userData) && userData is not null)
            {
                dataFolders.Add(userData);
            }
        }

        info = BuildInstallInfo(dataFolders, extraRoots);
        return info.HasDatabase;
    }


    /// <summary>
    /// 把 Starward 数据复制到 <paramref name="target"/>，并在副本上做版本回退。
    /// 目标已有数据库则跳过库文件（避免覆盖本机旧版 Moonward 数据）。
    /// <paramref name="source"/> 为空时回退到 <see cref="TryDetect"/>。
    /// </summary>
    public static async Task<ImportResult> ImportAsync(string target, StarwardInstallInfo? source = null, IProgress<DataMigrationService.MigrationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        StarwardInstallInfo? install = source is { HasDatabase: true } ? source : null;
        if (install is null)
        {
            if (!TryDetect(out StarwardInstallInfo detected) || !detected.HasDatabase)
            {
                return new ImportResult(false);
            }
            install = detected;
        }

        return await Task.Run(() => ImportCore(install, target, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
    }


    private static ImportResult ImportCore(StarwardInstallInfo install, string target, IProgress<DataMigrationService.MigrationProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);

        long bgSize = install.BackgroundFolder is null ? 0 : GetDirectorySize(install.BackgroundFolder);
        long dbSize = SafeFileLength(install.DatabasePath!);
        long total = dbSize + bgSize;
        long done = 0;
        progress?.Report(new DataMigrationService.MigrationProgress(0, total, DatabaseFileName));

        bool importedDatabase = false;
        string destDb = Path.Combine(target, DatabaseFileName);
        if (!File.Exists(destDb))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyDatabaseReadOnly(install.DatabasePath!, destDb);
            ReconcileCopiedDatabase(destDb);
            importedDatabase = true;
        }
        done += dbSize;
        progress?.Report(new DataMigrationService.MigrationProgress(done, total, DatabaseFileName));

        if (install.BackgroundFolder is not null)
        {
            string destBg = Path.Combine(target, "bg");
            done = CopyDirectoryPreserveSource(install.BackgroundFolder, destBg, done, total, progress, cancellationToken);
        }

        progress?.Report(new DataMigrationService.MigrationProgress(total, total, string.Empty));
        return new ImportResult(importedDatabase);
    }


    /// <summary>
    /// 只读打开源库并 Backup 到目标。源连接 Mode=ReadOnly，不会写 WAL、不会改源文件。
    /// </summary>
    private static void CopyDatabaseReadOnly(string sourcePath, string destPath)
    {
        string tempPath = destPath + ".importing";
        TryDeleteSqliteFiles(tempPath);
        try
        {
            try
            {
                using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False;");
                source.Open();
                using var dest = new SqliteConnection($"Data Source={tempPath};Pooling=False;");
                dest.Open();
                source.BackupDatabase(dest);
                // 收成单文件再 Move，避免留下源侧未参与的 WAL 副本。
                using var cmd = dest.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=DELETE;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                throw new InvalidOperationException(Lang.WelcomeView_StarwardDatabaseInUse, ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(Lang.WelcomeView_StarwardDatabaseInUse, ex);
            }

            File.Move(tempPath, destPath, overwrite: false);
        }
        finally
        {
            TryDeleteSqliteFiles(tempPath);
        }
    }


    /// <summary>
    /// 在副本上回退 Starward 独有变更，并把 USER_VERSION 拉回共同祖先。
    /// 之后由 <see cref="DatabaseService.SetDatabase"/> 按 Moonward 脚本补齐。
    /// </summary>
    private static void ReconcileCopiedDatabase(string databasePath)
    {
        using var con = new SqliteConnection($"Data Source={databasePath};Pooling=False;");
        con.Open();
        int version = con.QueryFirstOrDefault<int>("PRAGMA USER_VERSION;");
        if (version > KnownMaxStarwardUserVersion)
        {
            throw new InvalidOperationException(string.Format(Lang.WelcomeView_StarwardDatabaseVersionTooNew, version, KnownMaxStarwardUserVersion));
        }

        using (var tx = con.BeginTransaction())
        {
            foreach ((int rollbackVersion, string sql) in StarwardOnlyRollbacks.OrderByDescending(x => x.Version))
            {
                if (version < rollbackVersion)
                {
                    continue;
                }
                ApplyStarwardRollback(con, rollbackVersion, sql);
            }

            // 不把 Starward 的米游社 / HoYoLAB Cookie 带进 Moonward，需在本应用内重新登录。
            ClearImportedCookies(con);

            con.Execute($"PRAGMA USER_VERSION = {CommonUserVersion};");
            con.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            tx.Commit();
        }

        SqliteConnection.ClearPool(con);
    }


    private static void ApplyStarwardRollback(SqliteConnection con, int version, string sql)
    {
        if (version == 20)
        {
            DropColumnIfExists(con, "ZZZDeadlyAssaultInfo", "HasHard");
            DropColumnIfExists(con, "ZZZDeadlyAssaultInfo", "HardTotalScore");
            DropColumnIfExists(con, "ZZZDeadlyAssaultInfo", "HardTotalStar");
            return;
        }

        con.Execute(sql);
    }


    private static void ClearImportedCookies(SqliteConnection con)
    {
        if (TableExists(con, "GameRecordUser"))
        {
            con.Execute("UPDATE GameRecordUser SET Cookie = NULL;");
        }
        if (TableExists(con, "GameRecordRole"))
        {
            con.Execute("UPDATE GameRecordRole SET Cookie = NULL;");
        }
    }


    private static bool TableExists(SqliteConnection con, string table)
    {
        return con.QueryFirstOrDefault<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;",
            new { table }) > 0;
    }


    private static void DropColumnIfExists(SqliteConnection con, string table, string column)
    {
        if (!ColumnExists(con, table, column))
        {
            return;
        }
        con.Execute($"ALTER TABLE {table} DROP COLUMN {column};");
    }


    private static bool ColumnExists(SqliteConnection con, string table, string column)
    {
        return con.QueryFirstOrDefault<int>(
            $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @column COLLATE NOCASE;",
            new { column }) > 0;
    }


    private static StarwardInstallInfo BuildInstallInfo(IEnumerable<string> dataFolders, IEnumerable<string> extraRoots)
    {
        List<string> uniqueFolders = dataFolders
            .Concat(extraRoots)
            .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? databasePath = uniqueFolders
            .Select(d => Path.Combine(d, DatabaseFileName))
            .FirstOrDefault(File.Exists);
        if (databasePath is null)
        {
            return new StarwardInstallInfo();
        }

        string dataFolder = Path.GetDirectoryName(databasePath)!;
        string? backgroundFolder = uniqueFolders
            .Select(d => Path.Combine(d, "bg"))
            .FirstOrDefault(Directory.Exists);

        return new StarwardInstallInfo
        {
            UserDataFolder = dataFolder,
            CacheFolder = uniqueFolders.FirstOrDefault(d => !string.Equals(d, dataFolder, StringComparison.OrdinalIgnoreCase)),
            DatabasePath = databasePath,
            BackgroundFolder = backgroundFolder,
        };
    }


    private static IEnumerable<string> EnumerateConfigIniPaths(string picked, string? parent)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? root in new[] { picked, parent })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }
            foreach (string path in new[]
                     {
                         Path.Combine(root, "config.ini"),
                         Path.Combine(root, ".cache", "config.ini"),
                     })
            {
                if (File.Exists(path) && seen.Add(path))
                {
                    yield return path;
                }
            }
        }
    }


    /// <summary>
    /// 读取便携版 / 可移动设备 <c>config.ini</c> 中的 UserDataFolder，相对路径相对 ini 所在目录解析。
    /// </summary>
    private static bool TryReadUserDataFolderFromConfig(string configPath, out string? folder)
    {
        folder = null;
        try
        {
            string text = File.ReadAllText(configPath);
            string raw = Regex.Match(text, @"^UserDataFolder=(.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }
            folder = Path.IsPathFullyQualified(raw)
                ? Path.GetFullPath(raw)
                : Path.GetFullPath(raw, Path.GetDirectoryName(configPath)!);
            return Directory.Exists(folder);
        }
        catch
        {
            return false;
        }
    }


    private static string? ReadStarwardUserDataFolder()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StarwardRegistryKey, writable: false);
            string? folder = (key?.GetValue("UserDataFolder") as string)?.Trim();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return null;
            }
            return Path.GetFullPath(folder);
        }
        catch
        {
            return null;
        }
    }


    private static string? CombineIfFolder(string? folder, string name)
    {
        return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, name);
    }


    private static string? FirstExistingFile(params string?[] paths)
    {
        return paths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }


    private static string? FirstExistingDirectory(params string?[] paths)
    {
        return paths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p));
    }


    private static long CopyDirectoryPreserveSource(string sourceDir, string destDir, long done, long total, IProgress<DataMigrationService.MigrationProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            long size = SafeFileLength(file);
            if (!File.Exists(dest))
            {
                CopyFileRetry(file, dest, cancellationToken);
            }
            done += size;
            progress?.Report(new DataMigrationService.MigrationProgress(done, total, rel));
        }
        return done;
    }


    private static void CopyFileRetry(string source, string dest, CancellationToken cancellationToken)
    {
        const int retries = 3;
        for (int i = 0; ; i++)
        {
            try
            {
                File.Copy(source, dest, overwrite: false);
                return;
            }
            catch (IOException) when (i < retries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(200);
            }
        }
    }


    private static void TryDeleteSqliteFiles(string path)
    {
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                string file = path + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch { }
        }
    }


    private static long GetDirectorySize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(SafeFileLength);
        }
        catch
        {
            return 0;
        }
    }


    private static long SafeFileLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch
        {
            return 0;
        }
    }

}
