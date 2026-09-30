using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Starward.Features.GameInstall;

/// <summary>
/// 按文件 ID 判断两个游戏目录是否通过硬链接共用文件。
/// 硬链接的多个路径指向同一份数据，文件 ID（卷序列号 + 文件号）相同，与文件名、所在目录无关，
/// 所以原神国服的 YuanShen_Data 与国际服的 GenshinImpact_Data 也能对上。
/// </summary>
internal static partial class HardLinkDetector
{


    private static readonly EnumerationOptions _enumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // 不跟随符号链接和挂载点，避免绕回自身或跑到别的卷
        AttributesToSkip = FileAttributes.ReparsePoint,
    };



    /// <summary>
    /// 取目录中体积最大、并且存在其他硬链接的若干文件的文件 ID。
    /// 只看大文件：资源包按内容去重后几乎都会被链接，几个就足够判断，不必打开整个目录的文件。
    /// </summary>
    /// <param name="folder">游戏目录</param>
    /// <param name="maxCount">最多取几个文件</param>
    /// <param name="maxScan">最多检查几个文件，目录里没有硬链接时也能很快结束</param>
    /// <returns>文件 ID 到文件大小的映射；没有硬链接时为空</returns>
    public static Dictionary<string, long> GetLinkedFileIds(string folder, int maxCount = 16, int maxScan = 512)
    {
        Dictionary<string, long> result = new();
        foreach ((FileInfo file, string id) in EnumerateLinkedFiles(folder, maxScan))
        {
            result.TryAdd(id, file.Length);
            if (result.Count >= maxCount)
            {
                break;
            }
        }
        return result;
    }



    /// <summary>
    /// 列出目录中若干大文件在目录之外的硬链接路径，也就是通过硬链接共用这些文件的其他游戏目录中的文件。
    /// 与 <see cref="GetLinkedFileIds"/> 一样只看体积最大、并且存在其他硬链接的文件。
    /// </summary>
    /// <param name="folder">游戏目录</param>
    /// <param name="maxCount">最多取几个文件</param>
    /// <param name="maxScan">最多检查几个文件，目录里没有硬链接时也能很快结束</param>
    /// <returns>目录之外的硬链接完整路径；没有硬链接时为空</returns>
    public static List<string> GetOutsideLinkPaths(string folder, int maxCount = 16, int maxScan = 512)
    {
        List<string> result = new();
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        int count = 0;
        foreach (var (file, _) in EnumerateLinkedFiles(folder, maxScan))
        {
            foreach (string path in GetLinkNames(file.FullName))
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(path);
                }
            }
            if (++count >= maxCount)
            {
                break;
            }
        }
        return result;
    }



    /// <summary>
    /// 按体积从大到小列出目录中存在其他硬链接的文件。
    /// 只看大文件：资源包按内容去重后几乎都会被链接，几个就足够判断，不必打开整个目录的文件。
    /// </summary>
    /// <param name="folder">游戏目录</param>
    /// <param name="maxScan">最多检查几个文件</param>
    /// <returns>文件与文件 ID</returns>
    private static IEnumerable<(FileInfo File, string Id)> EnumerateLinkedFiles(string folder, int maxScan)
    {
        IEnumerable<FileInfo> files = new DirectoryInfo(folder).EnumerateFiles("*", _enumerationOptions)
            .OrderByDescending(x => x.Length)
            .Take(maxScan);
        foreach (FileInfo file in files)
        {
            if (file.Length == 0)
            {
                yield break;
            }
            if (TryGetFileId(file.FullName, out string? id, out uint links) && links > 1)
            {
                yield return (file, id);
            }
        }
    }



    /// <summary>
    /// 目录中是否有文件与给定的文件 ID 相同，即两个目录共用同一份数据。
    /// 硬链接的大小必然相同，先按大小筛选，只打开大小对得上的文件。
    /// </summary>
    /// <param name="folder">要检查的游戏目录</param>
    /// <param name="fileIds"><see cref="GetLinkedFileIds"/> 的结果</param>
    /// <returns>至少有一个文件相同时为 <see langword="true"/></returns>
    public static bool ContainsAnyFile(string folder, IReadOnlyDictionary<string, long> fileIds)
    {
        if (fileIds.Count == 0)
        {
            return false;
        }
        HashSet<long> sizes = fileIds.Values.ToHashSet();
        foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", _enumerationOptions))
        {
            if (sizes.Contains(file.Length) && TryGetFileId(file.FullName, out string? id, out _) && fileIds.ContainsKey(id))
            {
                return true;
            }
        }
        return false;
    }



    /// <summary>
    /// 读取文件 ID 与硬链接数。游戏正在运行等原因打不开的文件直接跳过。
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="id">卷序列号与文件号拼成的十六进制串</param>
    /// <param name="links">指向这份数据的硬链接数</param>
    /// <returns>读取成功时为 <see langword="true"/></returns>
    private static bool TryGetFileId(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? id, out uint links)
    {
        id = null;
        links = 0;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var standardInfo = Kernel32.GetFileInformationByHandleEx<Kernel32.FILE_STANDARD_INFO>(handle, Kernel32.FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo);
            var idInfo = Kernel32.GetFileInformationByHandleEx<Kernel32.FILE_ID_INFO>(handle, Kernel32.FILE_INFO_BY_HANDLE_CLASS.FileIdInfo);
            id = Convert.ToHexString(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref idInfo, 1)));
            links = standardInfo.NumberOfLinks;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }



    /// <summary>
    /// 列出文件的全部硬链接路径（含自身）。
    /// FindFirstFileNameW 返回不带盘符的卷内路径（如 \Games\hk4e_cn\...），硬链接都在同一卷上，拼上文件所在盘的根目录即可。
    /// </summary>
    /// <param name="path">文件完整路径</param>
    /// <returns>硬链接完整路径；读取失败时为空</returns>
    private static unsafe List<string> GetLinkNames(string path)
    {
        List<string> result = new();
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return result;
        }
        // 盘符根目录是 D:\，TrimEndingDirectorySeparator 不会去掉根目录的分隔符，要手动去掉
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // 按最长路径分配，不用处理 ERROR_MORE_DATA
        char[] buffer = new char[short.MaxValue + 1];
        fixed (char* linkName = buffer)
        {
            uint length = (uint)buffer.Length;
            nint handle = FindFirstFileName(path, 0, ref length, linkName);
            if (handle == -1)
            {
                return result;
            }
            try
            {
                do
                {
                    result.Add(root + new string(linkName));
                    length = (uint)buffer.Length;
                }
                while (FindNextFileName(handle, ref length, linkName));
            }
            finally
            {
                FindClose(handle);
            }
        }
        return result;
    }



    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static unsafe partial nint FindFirstFileName(string lpFileName, uint dwFlags, ref uint stringLength, char* linkName);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool FindNextFileName(nint hFindStream, ref uint stringLength, char* linkName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint hFindFile);


}
