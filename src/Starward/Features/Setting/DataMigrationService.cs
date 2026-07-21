using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Setting;

/// <summary>
/// 升级迁移服务：把分散在「旧版缓存根」（<c>%LocalAppData%\Moonward</c> / <c>.MoonwardCache</c> / 便携 <c>.cache</c>，
/// 在标准安装下同时是 Velopack 安装根）与「旧版 UserDataFolder」下的 Starward 数据，
/// 按<b>白名单</b>统一搬运到用户选择的新数据目录。
/// <para>
/// 搬运策略（智能分流）：同盘走 <see cref="Directory.Move(string, string)"/> 秒级重命名（不复制字节）；
/// 跨盘或目标已存在时逐文件复制并上报进度，全部成功后再删除源（copy-verify-delete，中途失败不破坏源）。
/// </para>
/// <para>
/// 白名单只搬 Starward 自己的数据，<b>绝不</b>触碰 Velopack 文件（current\、Update.exe、packages\、.portable 等）。
/// </para>
/// </summary>
public static class DataMigrationService
{

    /// <summary>
    /// 需要搬运的 Starward 数据子目录（白名单）。
    /// 须与所有 <c>Path.Combine(AppConfig.CacheFolder, "...")</c> 的子目录保持同步：
    /// bg（背景图）、cache（FileCache/截图缓存）、webview（WebView2）、log（日志）、game（游戏下载临时）、
    /// thumb（缩略图缓存，ImageThumbnail）、crash、update；外加 UserDataFolder 下的 DatabaseBackup。
    /// </summary>
    private static readonly string[] DataDirectoryNames =
    [
        "bg", "cache", "webview", "log", "game", "thumb", "crash", "update", "DatabaseBackup",
    ];

    /// <summary>
    /// 需要搬运的 Starward 数据文件（白名单，含 SQLite WAL/SHM）。
    /// </summary>
    private static readonly string[] DataFileNames =
    [
        "StarwardDatabase.db", "StarwardDatabase.db-wal", "StarwardDatabase.db-shm",
    ];


    /// <summary>
    /// 迁移进度：已完成字节、总字节、当前项。
    /// </summary>
    public readonly record struct MigrationProgress(long BytesDone, long BytesTotal, string CurrentItem);


    private record Artifact(string SourcePath, string Name, bool IsDirectory, long Size);


    /// <summary>
    /// 枚举若干源根目录下、实际存在的 Starward 数据项（白名单，按名称去重；与目标自身相同的项会被跳过）。
    /// 源根目录按传入顺序处理，因此应把权威的 UserDataFolder 放在缓存根之前（数据库以前者为准）。
    /// </summary>
    private static List<Artifact> EnumerateArtifacts(IEnumerable<string?> sourceRoots, string target)
    {
        var result = new List<Artifact>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in sourceRoots.Where(x => !string.IsNullOrWhiteSpace(x))
                                           .Select(x => x!)
                                           .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (string name in DataDirectoryNames)
            {
                string path = Path.Combine(root, name);
                if (Directory.Exists(path) && !PathEquals(path, Path.Combine(target, name)) && seenNames.Add(name))
                {
                    result.Add(new Artifact(path, name, true, GetDirectorySize(path)));
                }
            }
            foreach (string name in DataFileNames)
            {
                string path = Path.Combine(root, name);
                if (File.Exists(path) && !PathEquals(path, Path.Combine(target, name)) && seenNames.Add(name))
                {
                    result.Add(new Artifact(path, name, false, SafeFileLength(path)));
                }
            }
        }
        return result;
    }


    /// <summary>
    /// 是否存在可迁移到 <paramref name="target"/> 的旧数据。
    /// </summary>
    public static bool HasLegacyData(IEnumerable<string?> sourceRoots, string target)
    {
        return EnumerateArtifacts(sourceRoots, target).Count > 0;
    }


    /// <summary>
    /// 旧数据总字节数（用于进度预估与展示）。
    /// </summary>
    public static long GetLegacyDataSize(IEnumerable<string?> sourceRoots, string target)
    {
        return EnumerateArtifacts(sourceRoots, target).Sum(x => x.Size);
    }


    /// <summary>
    /// 执行迁移搬运到 <paramref name="target"/>。<paramref name="progress"/> 建议在 UI 线程创建
    /// （<see cref="Progress{T}"/> 会捕获同步上下文，回调自动切回 UI 线程，避免后台线程给绑定属性赋值崩溃）。
    /// </summary>
    public static async Task MigrateAsync(IReadOnlyList<string?> sourceRoots, string target, IProgress<MigrationProgress>? progress = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(target);
            List<Artifact> artifacts = EnumerateArtifacts(sourceRoots, target);
            long total = artifacts.Sum(x => x.Size);
            long done = 0;
            progress?.Report(new MigrationProgress(0, total, string.Empty));

            string targetRoot = Path.GetPathRoot(Path.GetFullPath(target)) ?? string.Empty;

            foreach (Artifact artifact in artifacts)
            {
                ct.ThrowIfCancellationRequested();
                string dest = Path.Combine(target, artifact.Name);
                bool sameVolume = string.Equals(Path.GetPathRoot(Path.GetFullPath(artifact.SourcePath)), targetRoot, StringComparison.OrdinalIgnoreCase);

                bool moved = false;
                if (sameVolume && !Directory.Exists(dest) && !File.Exists(dest))
                {
                    // 同盘且目标不存在：直接重命名，秒级完成，不复制字节。
                    // 若源文件被短暂占用（如重启切换数据目录时旧实例尚未释放数据库），Move 会抛异常，回退到逐文件复制（带重试）。
                    try
                    {
                        if (artifact.IsDirectory)
                        {
                            Directory.Move(artifact.SourcePath, dest);
                        }
                        else
                        {
                            File.Move(artifact.SourcePath, dest);
                        }
                        moved = true;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                if (moved)
                {
                    done += artifact.Size;
                    progress?.Report(new MigrationProgress(done, total, artifact.Name));
                }
                else if (artifact.IsDirectory)
                {
                    // 跨盘 / 目标已存在 / Move 失败：逐文件复制并上报进度，全部成功后删除源。
                    done = CopyDirectory(artifact.SourcePath, dest, done, total, progress, ct);
                    TryDeleteDirectory(artifact.SourcePath);
                }
                else
                {
                    CopyFile(artifact.SourcePath, dest, ct);
                    done += artifact.Size;
                    progress?.Report(new MigrationProgress(done, total, artifact.Name));
                    TryDeleteFile(artifact.SourcePath);
                }
            }
            progress?.Report(new MigrationProgress(total, total, string.Empty));
        }, ct);
    }


    private static long CopyDirectory(string sourceDir, string destDir, long done, long total, IProgress<MigrationProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            long size = SafeFileLength(file);
            // 目标已存在则跳过（冲突合并时保留先到者），否则复制。
            if (!File.Exists(dest))
            {
                CopyFile(file, dest, ct);
            }
            done += size;
            progress?.Report(new MigrationProgress(done, total, rel));
        }
        return done;
    }


    /// <summary>
    /// 复制单个文件，遇到 IO 占用时重试若干次（webview / game 缓存可能被其它进程短暂占用）。
    /// </summary>
    private static void CopyFile(string source, string dest, CancellationToken ct)
    {
        const int retries = 3;
        for (int i = 0; ; i++)
        {
            try
            {
                File.Copy(source, dest, true);
                return;
            }
            catch (IOException) when (i < retries)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(200);
            }
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


    private static bool PathEquals(string a, string b)
    {
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                             Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                             StringComparison.OrdinalIgnoreCase);
    }


    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch { }
    }


    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch { }
    }


}
