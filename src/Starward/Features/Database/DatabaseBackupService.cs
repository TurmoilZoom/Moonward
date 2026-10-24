using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SharpSevenZip;
using Starward.Features.Background;
using Starward.Features.GameLauncher;
using Starward.Features.LanSync;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Vanara.PInvoke;

namespace Starward.Features.Database;

/// <summary>
/// 数据库的备份与还原。
/// </summary>
/// <remarks>
/// <para>
/// 备份是数据库的副本：按 <see cref="BackupDataPolicy"/> 去掉登录凭证、设备指纹与本机缓存后压缩成 7z，可以拿到别的电脑上还原。
/// 自定义背景等文件不在数据库里，不进备份。
/// </para>
/// <para>
/// 还原有两种：覆盖（回到备份时的状态）要重启，在启动时、设置缓存加载之前完成；合并（只补回本机缺少的记录）直接复用局域网同步的合并。
/// 两种都不碰本机的凭证与缓存，所以还原前自动做的那份备份同样不带凭证，也足以撤销这次还原。
/// </para>
/// </remarks>
internal static class DatabaseBackupService
{

    /// <summary>
    /// 默认备份目录名，位于数据目录下；更换数据目录时随数据一起搬走。
    /// </summary>
    public const string DefaultFolderName = "DatabaseBackup";

    /// <summary>
    /// 命令行：重启后要覆盖还原的副本（由 <see cref="PrepareRestore"/> 准备）。
    /// </summary>
    public const string RestoreArgument = "--restore-backup";

    /// <summary>
    /// 命令行：先等这个进程（发起还原的旧实例）退出，再开始还原。
    /// </summary>
    public const string WaitPidArgument = "--wait-pid";

    private const string BackupSchema = "bak";

    /// <summary>
    /// KVT：上次手动备份的文件。新版存完整路径，旧版只存文件名（位于默认备份目录）。
    /// </summary>
    private const string LastBackupFileKey = "LastBackupDatabase";

    /// <summary>
    /// KVT：上次手动备份时的数据指纹，用来判断数据有没有变化。
    /// </summary>
    private const string LastBackupFingerprintKey = "LastBackupFingerprint";


    private static string TempFolder => Path.Combine(Path.GetTempPath(), "Moonward", "Backup");

    private static string RestoreFolder => Path.Combine(Path.GetTempPath(), "Moonward", "Restore");


    private static ILogger Logger => AppConfig.GetService<ILoggerFactory>().CreateLogger(typeof(DatabaseBackupService).FullName!);



    #region Folder



    /// <summary>
    /// 默认备份目录：数据目录下的 <see cref="DefaultFolderName"/>。
    /// </summary>
    public static string DefaultBackupFolder => Path.Join(AppConfig.UserDataFolder, DefaultFolderName);


    /// <summary>
    /// 当前备份目录：设置里选过、且目录还在时用它，否则用默认目录（比如选的是移动硬盘、现在没插）。
    /// </summary>
    /// <remarks>直接查 Setting 表：升级前备份发生在迁移脚本执行之前，那时还不能加载设置缓存。</remarks>
    /// <returns>备份目录完整路径。</returns>
    public static string GetBackupFolder()
    {
        try
        {
            using var con = DatabaseService.CreateConnection();
            string? folder = con.QueryFirstOrDefault<string>("SELECT Value FROM Setting WHERE Key = @key LIMIT 1;", new { key = nameof(AppConfig.BackupFolder) });
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return folder;
            }
        }
        catch { }
        return DefaultBackupFolder;
    }


    /// <summary>
    /// 所选目录能否用作备份目录：不能是驱动器根目录，也不能在程序目录下（更新时会被清掉）。
    /// </summary>
    /// <param name="folder">所选目录。</param>
    /// <returns>可以使用时返回 true。</returns>
    public static bool IsValidBackupFolder(string folder)
    {
        string full = Path.GetFullPath(folder);
        return full != Path.GetPathRoot(full) && !IsInFolder(full, AppContext.BaseDirectory);
    }



    #endregion




    #region Backup



    /// <summary>
    /// 手动备份。数据和上次手动备份一样、且那份备份还在时不新建。
    /// </summary>
    /// <returns>备份文件、时间，以及是否因为没有变化而沿用了上次的备份。</returns>
    public static ManualBackupResult BackupManually()
    {
        string fingerprint;
        using (var con = DatabaseService.CreateConnection())
        {
            fingerprint = ComputeFingerprint(con, "main");
        }
        if (TryGetLastBackup(out string? lastFile, out DateTime lastTime, out string? lastFingerprint) && lastFingerprint == fingerprint)
        {
            return new ManualBackupResult(lastFile, lastTime, true);
        }
        DateTime time = DateTime.Now;
        string file = CreateBackup(BackupKind.Manual, time);
        DatabaseService.SetValue(LastBackupFileKey, file, time);
        DatabaseService.SetValue(LastBackupFingerprintKey, fingerprint, time);
        return new ManualBackupResult(file, time, false);
    }


    /// <summary>
    /// 读取上次手动备份的记录。
    /// </summary>
    /// <param name="file">备份文件完整路径。</param>
    /// <param name="time">备份时间。</param>
    /// <param name="fingerprint">备份时的数据指纹；旧版备份没有记录时为 null。</param>
    /// <returns>有记录且文件还在时返回 true。</returns>
    public static bool TryGetLastBackup([NotNullWhen(true)] out string? file, out DateTime time, out string? fingerprint)
    {
        fingerprint = null;
        if (!DatabaseService.TryGetValue(LastBackupFileKey, out file, out time) || string.IsNullOrWhiteSpace(file))
        {
            file = null;
            return false;
        }
        if (!Path.IsPathFullyQualified(file))
        {
            file = Path.Join(DefaultBackupFolder, file);
        }
        if (!File.Exists(file))
        {
            file = null;
            return false;
        }
        DatabaseService.TryGetValue(LastBackupFingerprintKey, out fingerprint, out _);
        return true;
    }


    /// <summary>
    /// 当前数据和上次手动备份是否一致。要读整个库，在后台线程调用。
    /// </summary>
    /// <returns>一致为 true，有变化为 false；没有上次备份、或上次是不记录指纹的旧版备份时为 null。</returns>
    public static bool? IsSameAsLastBackup()
    {
        if (!TryGetLastBackup(out _, out _, out string? fingerprint) || string.IsNullOrEmpty(fingerprint))
        {
            return null;
        }
        using var con = DatabaseService.CreateConnection();
        return ComputeFingerprint(con, "main") == fingerprint;
    }


    /// <summary>
    /// 创建一份不带凭证与本机缓存的备份，并清理同类型超出保留份数的旧备份。
    /// </summary>
    /// <param name="kind">备份类型。</param>
    /// <returns>备份文件完整路径。</returns>
    public static string CreateBackup(BackupKind kind)
    {
        return CreateBackup(kind, DateTime.Now);
    }


    /// <summary>
    /// 创建备份。
    /// </summary>
    /// <param name="kind">备份类型。</param>
    /// <param name="time">写进文件名的时间。</param>
    /// <returns>备份文件完整路径。</returns>
    private static string CreateBackup(BackupKind kind, DateTime time)
    {
        string folder = GetBackupFolder();
        Directory.CreateDirectory(folder);
        string archive = Path.Combine(folder, DatabaseBackupNaming.GetFileName(time, kind));
        string work = Path.Combine(TempFolder, Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(work);
        // 压缩包里的库文件沿用备份名，解压到别处也认得出
        string db = Path.Combine(work, Path.ChangeExtension(Path.GetFileName(archive), ".db"));
        try
        {
            using (var con = DatabaseService.CreateUnpooledConnection())
            {
                // VACUUM INTO 得到一致的快照，主库处于 WAL 也不用另外处理 -wal 文件
                con.Execute($"VACUUM INTO {QuoteLiteral(db)};");
            }
            using (var con = OpenFile(db))
            {
                StripLocalData(con, kind);
            }
            string temp = archive + "_tmp";
            File.Delete(temp);
            new SharpSevenZipCompressor().CompressFiles(temp, db);
            File.Move(temp, archive, true);
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch { }
        }
        PruneBackups(folder, kind);
        return archive;
    }


    /// <summary>
    /// 在备份副本上删掉凭证与本机缓存，写入备份信息，再整理文件，免得删掉的内容留在空闲页里。
    /// </summary>
    /// <param name="con">备份副本的连接。</param>
    /// <param name="kind">备份类型，写进备份信息。</param>
    private static void StripLocalData(SqliteConnection con, BackupKind kind)
    {
        // 压缩前只能有一个文件，副本不用 WAL
        con.Execute("PRAGMA journal_mode = DELETE;");
        using (var transaction = con.BeginTransaction())
        {
            foreach (string table in GetTables(con, "main", transaction))
            {
                if (BackupDataPolicy.ClassifyTable(table) is BackupTableKind.Local)
                {
                    con.Execute($"DELETE FROM main.{Quote(table)};", transaction: transaction);
                }
            }
            List<string> keys = con.Query<string>("SELECT Key FROM main.Setting;", transaction: transaction)
                                   .Where(key => !BackupDataPolicy.IsSettingBackedUp(key))
                                   .ToList();
            con.Execute("DELETE FROM main.Setting WHERE Key = @key;", keys.Select(key => new { key }), transaction);

            string fingerprint = ComputeFingerprint(con, "main", transaction);
            int userVersion = con.QueryFirst<int>("PRAGMA user_version;", transaction: transaction);
            con.Execute($"CREATE TABLE main.{BackupDataPolicy.InfoTable} (Key TEXT NOT NULL PRIMARY KEY, Value TEXT);", transaction: transaction);
            con.Execute($"INSERT INTO main.{BackupDataPolicy.InfoTable} (Key, Value) VALUES (@Key, @Value);", new[]
            {
                new { Key = "CreatedAt", Value = DateTimeOffset.Now.ToString("O") },
                new { Key = "AppVersion", Value = AppConfig.AppVersion },
                new { Key = "MachineName", Value = Environment.MachineName },
                new { Key = "UserVersion", Value = userVersion.ToString(CultureInfo.InvariantCulture) },
                new { Key = "Kind", Value = kind.ToString() },
                new { Key = "Fingerprint", Value = fingerprint },
            }, transaction);
            transaction.Commit();
        }
        // DELETE 只是把页标记为空闲，Cookie 原文还在文件里，整理一遍才真正去掉
        con.Execute("VACUUM;");
    }


    /// <summary>
    /// 删除同类型超出保留份数的新格式备份，旧格式文件不动。
    /// </summary>
    /// <param name="folder">备份目录。</param>
    /// <param name="kind">备份类型。</param>
    private static void PruneBackups(string folder, BackupKind kind)
    {
        try
        {
            IEnumerable<string> names = Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>();
            foreach (string name in DatabaseBackupNaming.GetFilesToPrune(names, kind))
            {
                try
                {
                    File.Delete(Path.Combine(folder, name));
                }
                catch { }
            }
        }
        catch { }
    }


    /// <summary>
    /// 列出备份目录（以及默认备份目录）里的备份，新的在前。
    /// </summary>
    /// <returns>备份文件列表。</returns>
    public static List<BackupFileInfo> ListBackups()
    {
        var list = new List<BackupFileInfo>();
        IEnumerable<string> folders = new[] { GetBackupFolder(), DefaultBackupFolder }
            .Select(x => Path.TrimEndingDirectorySeparator(Path.GetFullPath(x)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (DatabaseBackupNaming.TryParse(Path.GetFileName(file), out DateTime time, out BackupKind kind, out bool legacy))
                {
                    list.Add(new BackupFileInfo(file, time, kind, legacy, new FileInfo(file).Length));
                }
            }
        }
        return list.OrderByDescending(x => x.Time).ToList();
    }



    #endregion




    #region Fingerprint



    /// <summary>
    /// 数据指纹：会进备份的表与设置逐行求 SHA256。表按名字、行按全部列排序，内容相同则指纹相同，与行的物理顺序无关。
    /// </summary>
    /// <param name="con">数据库连接。</param>
    /// <param name="schema">库名，主库为 <c>main</c>。</param>
    /// <param name="transaction">所在事务。</param>
    /// <returns>十六进制的 SHA256。</returns>
    public static string ComputeFingerprint(SqliteConnection con, string schema, SqliteTransaction? transaction = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string table in GetTables(con, schema, transaction).Order(StringComparer.Ordinal))
        {
            BackupTableKind kind = BackupDataPolicy.ClassifyTable(table);
            if (kind is BackupTableKind.Local or BackupTableKind.Ignored)
            {
                continue;
            }
            AppendBytes(hash, 0xFE, Encoding.UTF8.GetBytes(table));
            if (kind is BackupTableKind.Setting)
            {
                foreach ((string key, string? value) in con.Query<(string, string?)>($"SELECT Key, Value FROM {schema}.Setting ORDER BY Key;", transaction: transaction))
                {
                    if (BackupDataPolicy.IsSettingBackedUp(key))
                    {
                        AppendValue(hash, key);
                        AppendValue(hash, (object?)value ?? DBNull.Value);
                    }
                }
                continue;
            }
            int columnCount = GetColumns(con, schema, table, transaction).Count;
            if (columnCount == 0)
            {
                continue;
            }
            using SqliteCommand command = con.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT * FROM {schema}.{Quote(table)} ORDER BY {string.Join(", ", Enumerable.Range(1, columnCount))};";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    AppendValue(hash, reader.GetValue(i));
                }
                hash.AppendData([0xFF]);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }


    /// <summary>
    /// 把一个字段值连同类型写进哈希，避免不同类型、不同切分得到相同的字节流。
    /// </summary>
    private static void AppendValue(IncrementalHash hash, object value)
    {
        Span<byte> buffer = stackalloc byte[9];
        switch (value)
        {
            case DBNull:
                hash.AppendData([0]);
                break;
            case long number:
                buffer[0] = 1;
                BinaryPrimitives.WriteInt64LittleEndian(buffer[1..], number);
                hash.AppendData(buffer);
                break;
            case double real:
                buffer[0] = 2;
                BinaryPrimitives.WriteDoubleLittleEndian(buffer[1..], real);
                hash.AppendData(buffer);
                break;
            case byte[] bytes:
                AppendBytes(hash, 4, bytes);
                break;
            default:
                AppendBytes(hash, 3, Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
                break;
        }
    }


    /// <summary>
    /// 写入「类型 + 长度 + 内容」。
    /// </summary>
    private static void AppendBytes(IncrementalHash hash, byte tag, ReadOnlySpan<byte> bytes)
    {
        Span<byte> header = stackalloc byte[5];
        header[0] = tag;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..], bytes.Length);
        hash.AppendData(header);
        hash.AppendData(bytes);
    }



    #endregion




    #region Restore



    /// <summary>
    /// 把备份解压或复制到临时目录，校验后升级到当前数据库版本，供两种还原使用。
    /// </summary>
    /// <param name="file">备份文件（<c>.7z</c> 或 <c>.db</c>）。</param>
    /// <returns>准备好的副本与备份信息；不再使用时调用 <see cref="DeletePrepared"/>。</returns>
    /// <exception cref="DatabaseBackupException">不是可用的备份。</exception>
    public static PreparedBackup PrepareRestore(string file)
    {
        Directory.CreateDirectory(RestoreFolder);
        string db = Path.Combine(RestoreFolder, $"{Guid.CreateVersion7():N}.db");
        try
        {
            CopyDatabase(file, db);
            using var con = OpenFile(db);
            Dictionary<string, string?> info;
            try
            {
                // 不是 SQLite 文件时在这里抛 SqliteException
                if (con.QueryFirstOrDefault<string>("PRAGMA quick_check;") is not "ok")
                {
                    throw new DatabaseBackupException(DatabaseBackupErrorKind.Corrupted);
                }
                if (!TableExists(con, "main", "Setting"))
                {
                    throw new DatabaseBackupException(DatabaseBackupErrorKind.InvalidFile);
                }
                int version = con.QueryFirst<int>("PRAGMA user_version;");
                if (version > DatabaseService.CurrentUserVersion)
                {
                    throw new DatabaseBackupException(DatabaseBackupErrorKind.NewerVersion);
                }
                // 上游 Starward v18 之后的库与 Moonward 编号分叉，要走欢迎页的导入流程回退
                if (version > StarwardDataImportService.CommonUserVersion && !TableExists(con, "main", "GachaItemName"))
                {
                    throw new DatabaseBackupException(DatabaseBackupErrorKind.StarwardDatabase);
                }
                info = TableExists(con, "main", BackupDataPolicy.InfoTable)
                    ? con.Query<(string Key, string? Value)>($"SELECT Key, Value FROM {BackupDataPolicy.InfoTable};").ToDictionary(x => x.Key, x => x.Value)
                    : [];
            }
            catch (SqliteException ex)
            {
                throw new DatabaseBackupException(DatabaseBackupErrorKind.InvalidFile, ex.Message, ex);
            }
            // 拷来的原始库可能是 WAL，转成单文件，之后 ATTACH 时不依赖 -wal
            con.Execute("PRAGMA journal_mode = DELETE;");
            DatabaseService.MigrateSchema(con);

            DateTimeOffset? createdAt = DateTimeOffset.TryParse(info.GetValueOrDefault("CreatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value) ? value : null;
            if (createdAt is null && DatabaseBackupNaming.TryParse(Path.GetFileName(file), out DateTime fileTime, out _, out _))
            {
                createdAt = new DateTimeOffset(fileTime);
            }
            return new PreparedBackup
            {
                SourceFile = file,
                DatabaseFile = db,
                CreatedAt = createdAt ?? new DateTimeOffset(File.GetLastWriteTime(file)),
                AppVersion = info.GetValueOrDefault("AppVersion"),
                MachineName = info.GetValueOrDefault("MachineName"),
                IsLegacy = info.Count == 0,
            };
        }
        catch
        {
            DeletePrepared(db);
            throw;
        }
    }


    /// <summary>
    /// 从备份文件取出数据库：7z 解压其中唯一的 .db，其他文件直接复制（连同 -wal）。
    /// </summary>
    /// <param name="file">备份文件。</param>
    /// <param name="target">目标路径。</param>
    /// <exception cref="DatabaseBackupException">压缩包打不开，或里面不是一个 .db 文件。</exception>
    private static void CopyDatabase(string file, string target)
    {
        if (!Path.GetExtension(file).Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(file, target);
            if (File.Exists($"{file}-wal"))
            {
                File.Copy($"{file}-wal", $"{target}-wal");
            }
            return;
        }
        try
        {
            using var extractor = new SharpSevenZipExtractor(file);
            var entries = extractor.ArchiveFileData.Where(x => !x.IsDirectory && x.FileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count != 1)
            {
                throw new DatabaseBackupException(DatabaseBackupErrorKind.InvalidFile);
            }
            using var fs = File.Create(target);
            extractor.ExtractFile(entries[0].Index, fs);
        }
        catch (Exception ex) when (ex is not DatabaseBackupException and not IOException and not UnauthorizedAccessException)
        {
            throw new DatabaseBackupException(DatabaseBackupErrorKind.InvalidFile, ex.Message, ex);
        }
    }


    /// <summary>
    /// 删除准备好的副本，失败时忽略（在临时目录里）。
    /// </summary>
    /// <param name="file">副本路径。</param>
    public static void DeletePrepared(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }
        foreach (string path in new[] { file, $"{file}-wal", $"{file}-shm", $"{file}-journal" })
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }


    /// <summary>
    /// 带上准备好的副本重启，由新进程在启动时覆盖还原（<see cref="ApplyRestoreOnStartup"/>）。
    /// 调用后当前进程应立即退出。
    /// </summary>
    /// <param name="prepared">准备好的副本。</param>
    public static void RestartToRestore(PreparedBackup prepared)
    {
        var info = new ProcessStartInfo { FileName = AppConfig.MoonwardExecutePath };
        info.ArgumentList.Add(RestoreArgument);
        info.ArgumentList.Add(prepared.DatabaseFile);
        info.ArgumentList.Add(WaitPidArgument);
        info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Process.Start(info);
    }


    /// <summary>
    /// 启动时覆盖还原。必须在设置缓存加载、各服务启动之前调用，否则内存里的旧设置会在之后写回去。
    /// 失败时弹窗提示，然后照常启动。
    /// </summary>
    /// <param name="file">准备好的副本，只接受 <see cref="PrepareRestore"/> 放在临时目录里的文件。</param>
    /// <param name="waitPid">发起还原的旧进程，先等它退出。</param>
    public static void ApplyRestoreOnStartup(string file, string? waitPid)
    {
        string full;
        try
        {
            full = Path.GetFullPath(file);
        }
        catch
        {
            return;
        }
        if (!IsInFolder(full, RestoreFolder) || !File.Exists(full))
        {
            return;
        }
        // 旧实例退出时可能还在写库
        if (int.TryParse(waitPid, out int pid))
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                process.WaitForExit(TimeSpan.FromSeconds(15));
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        try
        {
            ApplyOverwrite(full);
            Logger.LogInformation("Restored database from backup.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Restore database from backup");
            User32.MessageBox(HWND.NULL, string.Format(Lang.DatabaseBackup_RestoreFailedOnStartup, ex.Message), "Moonward", User32.MB_FLAGS.MB_OK | User32.MB_FLAGS.MB_ICONWARNING);
        }
        finally
        {
            DeletePrepared(full);
        }
    }


    /// <summary>
    /// 覆盖还原：业务表整表换成备份里的，设置按 <see cref="BackupDataPolicy"/> 合并，凭证与缓存保持本机的。
    /// 先自动备份一次，失败则不还原。
    /// </summary>
    /// <param name="db">准备好的副本。</param>
    private static void ApplyOverwrite(string db)
    {
        CreateBackup(BackupKind.BeforeRestore);
        // 不进连接池，关闭即释放附加的副本
        using var con = DatabaseService.CreateUnpooledConnection();
        con.Execute($"ATTACH DATABASE @file AS {BackupSchema};", new { file = db });
        using var transaction = con.BeginTransaction();
        HashSet<string> backupTables = GetTables(con, BackupSchema, transaction).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string table in GetTables(con, "main", transaction))
        {
            if (BackupDataPolicy.ClassifyTable(table) is not BackupTableKind.Data || !backupTables.Contains(table))
            {
                continue;
            }
            HashSet<string> backupColumns = GetColumns(con, BackupSchema, table, transaction).ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<string> columns = GetColumns(con, "main", table, transaction).Where(backupColumns.Contains).ToList();
            con.Execute($"DELETE FROM main.{Quote(table)};", transaction: transaction);
            if (columns.Count > 0)
            {
                string columnList = string.Join(", ", columns.Select(Quote));
                con.Execute($"INSERT INTO main.{Quote(table)} ({columnList}) SELECT {columnList} FROM {BackupSchema}.{Quote(table)};", transaction: transaction);
            }
        }
        RestoreSettings(con, transaction);
        // 时长总计是 KVT 里的缓存，删掉后首页按钮会按还原后的记录重算
        con.Execute(@"DELETE FROM main.KVT WHERE Key LIKE 'playtime\_total\_%' ESCAPE '\';", transaction: transaction);
        transaction.Commit();
    }


    /// <summary>
    /// 按 <see cref="BackupSettingKind"/> 合并设置后整表写回。
    /// </summary>
    /// <param name="con">已附加副本的连接。</param>
    /// <param name="transaction">所在事务。</param>
    private static void RestoreSettings(SqliteConnection con, SqliteTransaction transaction)
    {
        Dictionary<string, string?> local = con.Query<(string Key, string? Value)>("SELECT Key, Value FROM main.Setting;", transaction: transaction)
                                               .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        // 旧版整库备份里还有凭证，这里按规则再筛一遍
        Dictionary<string, string?> backup = con.Query<(string Key, string? Value)>($"SELECT Key, Value FROM {BackupSchema}.Setting;", transaction: transaction)
                                                .Where(x => BackupDataPolicy.IsSettingBackedUp(x.Key))
                                                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var installGroups = new HashSet<string>(StringComparer.Ordinal);
        var backgroundGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in local.Keys.Union(backup.Keys))
        {
            switch (BackupDataPolicy.ClassifySetting(key, out string? group))
            {
                case BackupSettingKind.Normal:
                    // 备份里没有的键不写，等于恢复默认值
                    if (backup.TryGetValue(key, out string? value))
                    {
                        result[key] = value;
                    }
                    break;
                case BackupSettingKind.Local:
                    result[key] = local[key];
                    break;
                case BackupSettingKind.Path:
                    if (local.TryGetValue(key, out string? localPath) && !string.IsNullOrWhiteSpace(localPath))
                    {
                        result[key] = localPath;
                    }
                    else if (backup.TryGetValue(key, out string? backupPath) && PathExists(backupPath))
                    {
                        result[key] = backupPath;
                    }
                    else if (local.ContainsKey(key))
                    {
                        result[key] = localPath;
                    }
                    break;
                case BackupSettingKind.InstallPath:
                    installGroups.Add(group!);
                    break;
                case BackupSettingKind.CustomBackground:
                    backgroundGroups.Add(group!);
                    break;
            }
        }
        foreach (string biz in installGroups)
        {
            // 以安装目录为准：本机装着就不动，本机没装且备份里的目录在本机存在才整组用备份的
            string key = BackupDataPolicy.InstallPathPrefix + biz;
            bool useBackup = string.IsNullOrWhiteSpace(local.GetValueOrDefault(key)) && PathExists(backup.GetValueOrDefault(key));
            CopyGroup(useBackup ? backup : local, result, BackupSettingKind.InstallPath, biz);
        }
        foreach (string biz in backgroundGroups)
        {
            // 背景文件不进备份，只有备份里的文件本机也有时，才整对用备份的
            bool useBackup = BackgroundFileExists(backup.GetValueOrDefault(BackupDataPolicy.CustomBackgroundFilePrefix + biz));
            CopyGroup(useBackup ? backup : local, result, BackupSettingKind.CustomBackground, biz);
        }

        con.Execute("DELETE FROM main.Setting;", transaction: transaction);
        con.Execute("INSERT INTO main.Setting (Key, Value) VALUES (@Key, @Value);", result.Select(x => new { x.Key, x.Value }), transaction);
    }


    /// <summary>
    /// 把某区服一组键从 <paramref name="source"/> 抄进结果。
    /// </summary>
    private static void CopyGroup(Dictionary<string, string?> source, Dictionary<string, string?> result, BackupSettingKind kind, string biz)
    {
        foreach ((string key, string? value) in source)
        {
            if (BackupDataPolicy.ClassifySetting(key, out string? group) == kind && group == biz)
            {
                result[key] = value;
            }
        }
    }


    /// <summary>
    /// 路径（文件或目录）在本机是否存在；便携版在可移动存储上时可能是相对路径。
    /// </summary>
    private static bool PathExists([NotNullWhen(true)] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        try
        {
            string full = GameLauncherService.GetFullPathIfRelativePath(path);
            return Directory.Exists(full) || File.Exists(full);
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 自定义背景文件在本机 bg 目录里是否还在；早期版本可能存的是完整路径。
    /// </summary>
    private static bool BackgroundFileExists([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        try
        {
            return BackgroundService.BackgroundFileExists(Path.IsPathFullyQualified(name) ? name : BackgroundService.GetBgFilePath(name));
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 合并还原：只补回本机缺少的抽卡、工具箱、游戏时长记录，不改设置。先自动备份一次。
    /// </summary>
    /// <param name="db">准备好的副本。</param>
    /// <returns>各类别新增的记录数。</returns>
    public static LanSyncCounts ApplyMerge(string db)
    {
        CreateBackup(BackupKind.BeforeRestore);
        string snapshot = LanSyncSnapshot.CreateTempFilePath(".db");
        try
        {
            LanSyncSnapshot.Export(snapshot, Environment.MachineName, db);
            return AppConfig.GetService<LanSyncService>().MergeSnapshot(snapshot, LanSyncCategories.All);
        }
        finally
        {
            LanSyncSnapshot.DeleteTempFile(snapshot);
        }
    }



    #endregion




    #region Helpers



    private static SqliteConnection OpenFile(string file)
    {
        var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        con.Open();
        return con;
    }


    private static List<string> GetTables(SqliteConnection con, string schema, SqliteTransaction? transaction)
    {
        return con.Query<string>($"SELECT name FROM {schema}.sqlite_master WHERE type = 'table';", transaction: transaction).ToList();
    }


    private static List<string> GetColumns(SqliteConnection con, string schema, string table, SqliteTransaction? transaction)
    {
        return con.Query<string>("SELECT name FROM pragma_table_info(@table, @schema);", new { table, schema }, transaction).ToList();
    }


    private static bool TableExists(SqliteConnection con, string schema, string table)
    {
        return con.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type = 'table' AND name = @table;", new { table }) > 0;
    }


    private static string Quote(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }


    private static string QuoteLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }


    /// <summary>
    /// <paramref name="path"/> 是否就是 <paramref name="folder"/> 或在它下面。
    /// </summary>
    private static bool IsInFolder(string path, string folder)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }



    #endregion


}



/// <summary>
/// 手动备份的结果。
/// </summary>
/// <param name="File">备份文件完整路径。</param>
/// <param name="Time">备份时间。</param>
/// <param name="Unchanged">数据没有变化，沿用了上次的备份。</param>
internal readonly record struct ManualBackupResult(string File, DateTime Time, bool Unchanged);



/// <summary>
/// 备份目录里的一个备份文件。
/// </summary>
/// <param name="Path">完整路径。</param>
/// <param name="Time">备份时间（取自文件名）。</param>
/// <param name="Kind">备份类型。</param>
/// <param name="IsLegacy">旧格式的整库备份，里面带着凭证（还原时会跳过）。</param>
/// <param name="Size">文件大小（字节）。</param>
internal sealed record BackupFileInfo(string Path, DateTime Time, BackupKind Kind, bool IsLegacy, long Size);



/// <summary>
/// 解压、校验并升级到当前版本的备份副本。
/// </summary>
internal sealed class PreparedBackup
{
    /// <summary>用户选的备份文件。</summary>
    public required string SourceFile { get; init; }

    /// <summary>临时目录里的副本。</summary>
    public required string DatabaseFile { get; init; }

    /// <summary>备份时间：取自备份信息，旧备份取自文件名或修改时间。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>做备份的程序版本，旧备份没有。</summary>
    public string? AppVersion { get; init; }

    /// <summary>做备份的电脑名，旧备份没有。</summary>
    public string? MachineName { get; init; }

    /// <summary>没有备份信息：旧版整库备份，或从别处拷来的数据库文件。</summary>
    public bool IsLegacy { get; init; }
}



/// <summary>
/// 备份文件不能用来还原的原因。
/// </summary>
internal enum DatabaseBackupErrorKind
{
    /// <summary>不是 Moonward 的数据库或备份。</summary>
    InvalidFile,

    /// <summary>数据库文件已损坏。</summary>
    Corrupted,

    /// <summary>来自更新版本的 Moonward。</summary>
    NewerVersion,

    /// <summary>上游 Starward 的数据库，编号与 Moonward 分叉。</summary>
    StarwardDatabase,
}



internal sealed class DatabaseBackupException : Exception
{

    public DatabaseBackupErrorKind Kind { get; }


    public DatabaseBackupException(DatabaseBackupErrorKind kind, string? message = null, Exception? innerException = null) : base(message ?? kind.ToString(), innerException)
    {
        Kind = kind;
    }

}
