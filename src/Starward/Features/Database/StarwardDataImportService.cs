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
/// Starward v19 = ExtraStarNum，v20 = ZZZ HasHard，v21 = PlayTimeStats；
/// Moonward v19 = GachaItemName，v20 = ExtraStarNum，v21 = DROP GameAccount，v22 = PlayTimeStats。
/// 先只读探测源库：无 GachaItemName 且 USER_VERSION &gt; <see cref="KnownMaxStarwardUserVersion"/> 视为未知 Starward，拒绝导入且不落副本。
/// 导入时在<b>副本</b>上回退 Starward 独有变更；仅当副本版本 &gt; 共同祖先时才把 USER_VERSION 盖回祖先（绝不把 v1–v17 盖成 18），
/// 再交给 Moonward 的 <c>DatabaseSqls</c> 往下跑。两边都有的列（ExtraStarNum）保留，
/// 由 <see cref="DatabaseService"/> 在执行对应脚本前跳过，以免 ALTER 失败并丢掉已有数据。
/// </para>
/// <para>
/// 上游每新增一个 <c>Sql_vN</c>：在 <see cref="StarwardOnlyRollbacks"/> 补反向 SQL，
/// 并更新 <see cref="KnownMaxStarwardUserVersion"/>。更高未知版本（无 GachaItemName 且版本 &gt; KnownMax）拒绝导入。
/// 若该变更 Moonward 也需要，追加 Moonward 自己的新 <c>Sql_vN</c>（禁止改已发布脚本）。
/// 变基后若某段脚本重新对齐，提高 <see cref="CommonUserVersion"/> 并删掉对应回退项。
/// </para>
/// </summary>
internal static class StarwardDataImportService
{

    /// <summary>v1–v18 与上游 Starward 脚本一致。</summary>
    public const int CommonUserVersion = 18;

    /// <summary>当前已编写回退脚本的最高上游 USER_VERSION。</summary>
    public const int KnownMaxStarwardUserVersion = 21;

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
    /// ExtraStarNum（Starward v19 / Moonward v20）与 PlayTimeStats（Starward v21 / Moonward v22）
    /// 是两边共有的表/列，保留数据不在此回退。
    /// 保留版本须用 <c>import-keep: N</c> 标出，供 CI 对照上游，不要只改 <see cref="KnownMaxStarwardUserVersion"/>。
    /// </summary>
    // import-keep: 19
    // import-keep: 21
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
    /// 自动探测本机 Starward 数据：注册表 <c>UserDataFolder</c>、
    /// <c>%LocalAppData%\Starward</c>，以及 <c>我的文档\Starward</c>。
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
        string documentsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Starward");
        extraRoots.Add(documentsFolder);
        dataFolders.Add(documentsFolder);
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
            // 过新源必须在拷库前拒绝，否则欢迎页会留下一份无法回退的 dest。
            ThrowIfUnknownStarwardDatabase(install.DatabasePath!);
            CopyAndReconcileFromSource(install.DatabasePath!, destDb);
            importedDatabase = true;
        }
        else if (IsIncompleteStarwardImport(destDb))
        {
            // 上次回退失败时副本已落盘；过新则删掉，错误盖成 v18 的必须重拷，其余补完回退。
            cancellationToken.ThrowIfCancellationRequested();
            (int destVersion, bool destMoonward) = ProbeDatabaseReadOnly(destDb);
            if (IsUnknownStarwardDatabase(destVersion, destMoonward))
            {
                TryDeleteSqliteFiles(destDb);
                throw CreateVersionTooNewException(destVersion);
            }
            if (IsBrokenAncestorStamp(destDb))
            {
                TryDeleteSqliteFiles(destDb);
                ThrowIfUnknownStarwardDatabase(install.DatabasePath!);
                CopyAndReconcileFromSource(install.DatabasePath!, destDb);
            }
            else
            {
                try
                {
                    ReconcileCopiedDatabase(destDb);
                }
                catch
                {
                    TryDeleteSqliteFiles(destDb);
                    throw;
                }
            }
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
    /// 拷库后回退；失败则删掉 dest（含 -wal/-shm），避免欢迎页带着半成品库启动。
    /// </summary>
    private static void CopyAndReconcileFromSource(string sourcePath, string destPath)
    {
        try
        {
            CopyDatabaseReadOnly(sourcePath, destPath);
            ReconcileCopiedDatabase(destPath);
        }
        catch
        {
            TryDeleteSqliteFiles(destPath);
            throw;
        }
    }


    /// <summary>
    /// 只读打开库，返回 USER_VERSION 以及是否已有 Moonward 的 GachaItemName。
    /// </summary>
    private static (int Version, bool HasGachaItemName) ProbeDatabaseReadOnly(string databasePath)
    {
        try
        {
            using var con = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False;");
            con.Open();
            int version = con.QueryFirstOrDefault<int>("PRAGMA USER_VERSION;");
            bool hasGachaItemName = TableExists(con, "GachaItemName");
            return (version, hasGachaItemName);
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException(Lang.WelcomeView_StarwardDatabaseInUse, ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(Lang.WelcomeView_StarwardDatabaseInUse, ex);
        }
    }


    /// <summary>
    /// 未知 Starward：无 GachaItemName 且版本高于已编写回退的上限。
    /// </summary>
    private static bool IsUnknownStarwardDatabase(int version, bool hasGachaItemName)
    {
        return !hasGachaItemName && version > KnownMaxStarwardUserVersion;
    }


    private static void ThrowIfUnknownStarwardDatabase(string databasePath)
    {
        (int version, bool hasGachaItemName) = ProbeDatabaseReadOnly(databasePath);
        if (IsUnknownStarwardDatabase(version, hasGachaItemName))
        {
            throw CreateVersionTooNewException(version);
        }
    }


    private static InvalidOperationException CreateVersionTooNewException(int version)
    {
        return new InvalidOperationException(string.Format(Lang.WelcomeView_StarwardDatabaseVersionTooNew, version, KnownMaxStarwardUserVersion));
    }


    /// <summary>
    /// 上次从 Starward 拷库后回退未完成：还没有 Moonward 的 GachaItemName，
    /// 但 USER_VERSION 仍是 Starward 的编号、仍留着 HasHard，或被错误盖成共同祖先版本。
    /// </summary>
    private static bool IsIncompleteStarwardImport(string databasePath)
    {
        using var con = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False;");
        con.Open();
        if (TableExists(con, "GachaItemName"))
        {
            return false;
        }
        int version = con.QueryFirstOrDefault<int>("PRAGMA USER_VERSION;");
        if (version > KnownMaxStarwardUserVersion)
        {
            return true;
        }
        if (version > CommonUserVersion)
        {
            return true;
        }
        if (ColumnExists(con, "ZZZDeadlyAssaultInfo", "HasHard"))
        {
            return true;
        }
        // 旧逻辑会把 v1–v17 盖成 18；真 v18 这三张表都在，缺任一就不能只靠再 Reconcile。
        return version == CommonUserVersion && !HasCommonAncestorSchema(con);
    }


    /// <summary>
    /// 被错误盖上 USER_VERSION=18 的祖先库：schema 还没跑到 v18，只能删 dest 后从源重拷。
    /// </summary>
    private static bool IsBrokenAncestorStamp(string databasePath)
    {
        using var con = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False;");
        con.Open();
        if (TableExists(con, "GachaItemName"))
        {
            return false;
        }
        int version = con.QueryFirstOrDefault<int>("PRAGMA USER_VERSION;");
        return version == CommonUserVersion && !HasCommonAncestorSchema(con);
    }


    /// <summary>
    /// 真正跑完 v18 的共同祖先应同时有这三张表。
    /// </summary>
    private static bool HasCommonAncestorSchema(SqliteConnection con)
    {
        return TableExists(con, "StarRailForgottenHallInfo")
            && TableExists(con, "GenshinBeyondGachaInfo")
            && TableExists(con, "StarRailChallengePeakData");
    }


    /// <summary>
    /// 在副本上回退 Starward 独有变更，并把高于共同祖先的 USER_VERSION 拉回祖先。
    /// 之后由 <see cref="DatabaseService.SetDatabase"/> 按 Moonward 脚本补齐。
    /// </summary>
    private static void ReconcileCopiedDatabase(string databasePath)
    {
        using var con = new SqliteConnection($"Data Source={databasePath};Pooling=False;");
        con.Open();
        int version = con.QueryFirstOrDefault<int>("PRAGMA USER_VERSION;");
        bool moonwardMigrated = TableExists(con, "GachaItemName");
        if (IsUnknownStarwardDatabase(version, moonwardMigrated))
        {
            throw CreateVersionTooNewException(version);
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

            if (!moonwardMigrated)
            {
                // 不把 Starward 的米游社 / HoYoLAB 账号带进 Moonward，需在本应用内重新登录。
                // 只清空 Cookie 会留下「已登录」角色行，刷新战绩时 Headers.Add(Cookie, null) 会 FormatException。
                RemoveImportedAccounts(con);
                if (version > CommonUserVersion)
                {
                    // 只把分叉后的 Starward 编号拉回祖先；v1–v17 必须留给 InitializeDatabase 接着跑。
                    con.Execute($"PRAGMA USER_VERSION = {CommonUserVersion};");
                }
            }

            tx.Commit();
        }

        // 写事务未提交时 checkpoint 会 SQLITE_LOCKED（Error 6）。
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
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


    private static void RemoveImportedAccounts(SqliteConnection con)
    {
        if (TableExists(con, "GameRecordRole"))
        {
            con.Execute("DELETE FROM GameRecordRole;");
        }
        if (TableExists(con, "GameRecordUser"))
        {
            con.Execute("DELETE FROM GameRecordUser;");
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
