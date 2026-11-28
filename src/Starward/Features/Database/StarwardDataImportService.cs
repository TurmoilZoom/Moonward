using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Starward.Core;
using Starward.Features.Setting;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
/// Starward v19 = ExtraStarNum，v20 = ZZZ HasHard，v21 = PlayTimeStats，
/// v22 = Setting 拆成 (Key, GameId)，v23 = CacheEntry / GameEntry / PassportUserInfo，v24 = GameRecordRole.Aid；
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
    public const int KnownMaxStarwardUserVersion = 24;

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
        (22, """
            -- Starward v22：Setting 改成 (Key, GameId) 双主键，每游戏的键改成 PascalCase。
            -- 按 v22 拆分规则的逆过程拼回 Moonward 的单列 Key（Bg + hk4e_cn-1Z8W5NHUQb → bg_hk4e_cn），
            -- 需要按行解析 GameId，由 RevertSettingGameIdSplit 执行。
            """),
        (23, """
            -- Starward v23：缓存、游戏条目、通行证登录表。安装路径与固定图标先由 MoveGameEntriesToSetting 写回 Setting。
            DROP TABLE IF EXISTS CacheEntry;
            DROP TABLE IF EXISTS GameEntry;
            DROP TABLE IF EXISTS PassportUserInfo;
            """),
        (24, """
            -- Starward v24：米游社角色关联通行证账号的 Aid 列。导入时账号本来就会清空。
            ALTER TABLE GameRecordRole DROP COLUMN Aid;
            """),
    ];


    /// <summary>
    /// Starward v22 起每游戏设置的键名 → Moonward 键前缀（后接 <c>_{biz}</c>）。
    /// 前 9 个是 v22 脚本里的改名表；其余是 v22 丢弃、Starward 0.19 代码又按新键重写、
    /// 且与 Moonward 语义相同的用户设置。缓存类的键（VersionPoster、GameBackgroundIds 等）不转换，由 Moonward 重新获取。
    /// </summary>
    private static readonly Dictionary<string, string> StarwardPerGameSettingKeys = new(StringComparer.Ordinal)
    {
        ["Bg"] = "bg",
        ["CustomBg"] = "custom_bg",
        ["EnableThirdPartyTool"] = "enable_third_party_tool",
        ["ThirdPartyToolPath"] = "third_party_tool_path",
        ["StartArgument"] = "start_argument",
        ["ExternalScreenshotFolder"] = "external_screenshot_folder",
        ["EnableDX12"] = "enable_dx12",
        ["LastGachaUid"] = "last_gacha_uid",
        ["DisplayGachaBanners"] = "display_gacha_banners",
        ["UseVersionPoster"] = "use_version_poster",
        ["EnableCustomBg"] = "enable_custom_bg",
        ["InstallPath"] = "install_path",
        ["InstallPathRemovable"] = "install_path_removable",
    };


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
            if (!moonwardMigrated)
            {
                // 已是 Moonward 的库（用户选错了目录）不回退：Moonward 的 v22 与 Starward 的 v22 不是同一个变更。
                // 从高往低回退：v23 要先把 GameEntry 写进 v22 结构的 Setting，再由 v22 一并转换。
                foreach ((int rollbackVersion, string sql) in StarwardOnlyRollbacks.OrderByDescending(x => x.Version))
                {
                    if (version < rollbackVersion)
                    {
                        continue;
                    }
                    ApplyStarwardRollback(con, rollbackVersion, sql);
                }

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


    /// <summary>
    /// 执行一条 Starward 独有变更的回退。按 schema 探测执行，上次回退到一半的副本再跑一遍也不会出错。
    /// </summary>
    /// <param name="con">导入副本的连接，调用方已开启事务。</param>
    /// <param name="version">要回退的 Starward USER_VERSION。</param>
    /// <param name="sql"><see cref="StarwardOnlyRollbacks"/> 里对应的反向 SQL。</param>
    private static void ApplyStarwardRollback(SqliteConnection con, int version, string sql)
    {
        switch (version)
        {
            case 20:
                DropColumnIfExists(con, "ZZZDeadlyAssaultInfo", "HasHard");
                DropColumnIfExists(con, "ZZZDeadlyAssaultInfo", "HardTotalScore");
                DropColumnIfExists(con, "ZZZDeadlyAssaultInfo", "HardTotalStar");
                return;
            case 22:
                RevertSettingGameIdSplit(con);
                return;
            case 23:
                MoveGameEntriesToSetting(con);
                con.Execute(sql);
                return;
            case 24:
                DropColumnIfExists(con, "GameRecordRole", "Aid");
                return;
        }

        con.Execute(sql);
    }


    /// <summary>
    /// 把 Starward v23 的 GameEntry 里的安装路径与固定图标写回 Setting。
    /// 此时 Setting 仍是 v22 的 (Key, GameId) 结构，随后的 v22 回退会把它们转成
    /// Moonward 的 <c>install_path_{biz}</c>、<c>install_path_removable_{biz}</c> 与 <c>SelectedGameBizs</c>。
    /// </summary>
    /// <param name="con">导入副本的连接。</param>
    private static void MoveGameEntriesToSetting(SqliteConnection con)
    {
        if (!TableExists(con, "GameEntry") || !ColumnExists(con, "Setting", "GameId"))
        {
            return;
        }

        // Starward 0.19 起安装路径只记在 GameEntry，Setting 里的旧值已被 v22 丢弃，以 GameEntry 为准
        con.Execute("""
            INSERT OR REPLACE INTO Setting (Key, GameId, Value)
            SELECT 'InstallPath', GameId, InstallPath FROM GameEntry
            WHERE InstallPath IS NOT NULL AND TRIM(InstallPath) <> '';
            INSERT OR REPLACE INTO Setting (Key, GameId, Value)
            SELECT 'InstallPathRemovable', GameId, CASE WHEN Removable <> 0 THEN 'True' ELSE 'False' END FROM GameEntry
            WHERE InstallPath IS NOT NULL AND TRIM(InstallPath) <> '';
            """);

        // 固定图标同理只在 GameEntry；Moonward 仍用逗号分隔的 SelectedGameBizs，崩坏3国际服各区共用 bh3_global 一个图标。
        // Starward 0.19 里没固定过游戏时保留旧的 SelectedGameBizs（v22 原样留下了它）。
        List<string> pinned = con.Query<string>("SELECT GameId FROM GameEntry WHERE IsPinned <> 0 ORDER BY PinnedOrder;")
            .Select(GetGameBizOfGameId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (pinned.Count > 0)
        {
            con.Execute("INSERT OR REPLACE INTO Setting (Key, GameId, Value) VALUES ('SelectedGameBizs', '', @value);", new { value = string.Join(',', pinned) });
        }
    }


    /// <summary>
    /// 反向执行 Starward v22：把 (Key, GameId) 结构的 Setting 拼回 Moonward 的单列 Key。
    /// 全局键（GameId 为空）原样保留；每游戏的键只转换 <see cref="StarwardPerGameSettingKeys"/> 里的，
    /// 其余是 Starward 0.19 新增、Moonward 不读的，直接丢弃。
    /// </summary>
    /// <param name="con">导入副本的连接。</param>
    private static void RevertSettingGameIdSplit(SqliteConnection con)
    {
        if (!ColumnExists(con, "Setting", "GameId"))
        {
            return;
        }

        var rows = con.Query<(string Key, string GameId, string? Value)>("SELECT Key, IFNULL(GameId, ''), Value FROM Setting;").ToList();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
        // 全局键先写，每游戏键后写：撞键时以 Starward 按游戏存的新值为准
        foreach ((string key, string gameId, string? value) in rows.OrderBy(x => x.GameId.Length == 0 ? 0 : 1))
        {
            if (TryConvertStarwardSettingKey(key, gameId, out string? moonwardKey))
            {
                settings[moonwardKey] = value;
            }
        }
        // Starward 0.19 改用 CurrentGameId 记当前游戏，Moonward 仍读 CurrentGameBiz；旧的 CurrentGameBiz 可能已过时
        if (settings.TryGetValue("CurrentGameId", out string? currentGameId)
            && GetGameBizOfGameId(currentGameId ?? string.Empty) is { Length: > 0 } currentGameBiz)
        {
            settings["CurrentGameBiz"] = currentGameBiz;
        }

        con.Execute("""
            DROP TABLE IF EXISTS Setting_moonward_tmp;
            CREATE TABLE Setting_moonward_tmp
            (
                Key   TEXT NOT NULL PRIMARY KEY,
                Value TEXT
            );
            """);
        con.Execute("INSERT INTO Setting_moonward_tmp (Key, Value) VALUES (@Key, @Value);", settings.Select(x => new { x.Key, x.Value }));
        con.Execute("""
            DROP TABLE Setting;
            ALTER TABLE Setting_moonward_tmp RENAME TO Setting;
            """);
    }


    /// <summary>
    /// 把 Starward v22 结构的一行设置换算成 Moonward 的键。
    /// </summary>
    /// <param name="key">Starward 的键名。</param>
    /// <param name="gameId">Starward 的 GameId 列，全局键为空字符串。</param>
    /// <param name="moonwardKey">换算后的 Moonward 键。</param>
    /// <returns>Moonward 不使用这一行时返回 false。</returns>
    private static bool TryConvertStarwardSettingKey(string key, string gameId, [NotNullWhen(true)] out string? moonwardKey)
    {
        bool perGameKey = StarwardPerGameSettingKeys.TryGetValue(key, out string? prefix);
        if (gameId.Length == 0)
        {
            // v22 把不带区服后缀的旧键（如 bg）也改了名、GameId 留空
            moonwardKey = perGameKey ? prefix : key;
            return true;
        }
        if (perGameKey && GetSettingSuffixOfGameId(gameId) is string suffix)
        {
            moonwardKey = $"{prefix}_{suffix}";
            return true;
        }
        moonwardKey = null;
        return false;
    }


    /// <summary>
    /// v22 拆分规则的逆映射：GameId → 旧键的区服后缀。
    /// GameId 形如 <c>{GameBiz}-{Id}</c>；崩坏3国际服各区 GameBiz 都是 bh3_global，要按 Id 区分。
    /// 只有游戏名、没有 Id 的（如 hk4e）是按游戏存的键，原样作后缀。
    /// </summary>
    /// <param name="gameId">Starward Setting 表的 GameId 列。</param>
    /// <returns>区服后缀；无法识别的崩坏3国际服 Id 返回 null。</returns>
    private static string? GetSettingSuffixOfGameId(string gameId)
    {
        int dash = gameId.IndexOf('-');
        if (dash < 0)
        {
            return gameId;
        }
        string gameBiz = gameId[..dash];
        if (gameBiz != GameBiz.bh3_global)
        {
            return gameBiz;
        }
        return gameId[(dash + 1)..] switch
        {
            "5TIVvvcwtM" => GameBiz.bh3_global,
            "g0mMIvshDb" => GameBiz.bh3_jp,
            "uxB4MC7nzC" => GameBiz.bh3_kr,
            "bxPTXSET5t" => GameBiz.bh3_os,
            "wkE5P5WsIf" => GameBiz.bh3_asia,
            _ => null,
        };
    }


    /// <summary>
    /// 取 <c>{GameBiz}-{Id}</c> 形式 GameId 中的 GameBiz。
    /// </summary>
    /// <param name="gameId">Starward 记录的 GameId 字符串。</param>
    /// <returns>GameBiz 字符串；不是 GameId 形式时返回空字符串。</returns>
    private static string GetGameBizOfGameId(string gameId)
    {
        int dash = gameId.IndexOf('-');
        return dash > 0 ? gameId[..dash] : string.Empty;
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
