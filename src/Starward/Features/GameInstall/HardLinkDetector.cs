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
internal static class HardLinkDetector
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
        IEnumerable<FileInfo> files = new DirectoryInfo(folder).EnumerateFiles("*", _enumerationOptions)
            .OrderByDescending(x => x.Length)
            .Take(maxScan);
        foreach (FileInfo file in files)
        {
            if (file.Length == 0)
            {
                break;
            }
            if (TryGetFileId(file.FullName, out string? id, out uint links) && links > 1)
            {
                result.TryAdd(id, file.Length);
                if (result.Count >= maxCount)
                {
                    break;
                }
            }
        }
        return result;
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


}
