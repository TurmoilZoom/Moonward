using Starward.Core.HoYoPlay;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Starward.RPC.GameInstall;

internal class GameInstallFile
{

    public required GameInstallDownloadMode DownloadMode { get; set; }

    /// <summary>
    /// 完整路径
    /// </summary>
    public string FullPath { get; set; }

    /// <summary>
    /// 相对于游戏安装目录的路径
    /// </summary>
    public string File { get; set; }

    public string Url { get; set; }

    public string MD5 { get; set; }

    /// <summary>
    /// 文件大小，也有可能是 0
    /// </summary>
    public long Size { get; set; }

    public List<GameInstallCompressedPackage>? CompressedPackages { get; set; }

    public List<GameInstallFileChunk>? Chunks { get; set; }

    /// <summary>
    /// 更新时，若文件无任何变化，Path is null
    /// </summary>
    public GameInstallFilePatch? Patch { get; set; }

    /// <summary>
    /// 硬链接文件目标
    /// </summary>
    public string? HardLinkTarget { get; set; }


    public bool IsFinished { get; set; }



    public static GameInstallFile FromGamePackageResource(GamePackageResource resource, string installPath)
    {
        return new GameInstallFile
        {
            DownloadMode = GameInstallDownloadMode.CompressedPackage,
            Size = resource.GamePackages.Sum(x => x.Size),
            CompressedPackages = resource.GamePackages.Select(x => new GameInstallCompressedPackage
            {
                FullPath = Path.GetFullPath(Path.Combine(installPath, Path.GetFileName(x.Url))),
                Url = x.Url,
                MD5 = x.MD5,
                Size = x.Size,
                DecompressedSize = x.DecompressedSize,
            }).ToList(),
        };
    }


    public static GameInstallFile FromGamePackageFile(GamePackageFile file, string installPath)
    {
        return new GameInstallFile
        {
            DownloadMode = GameInstallDownloadMode.CompressedPackage,
            FullPath = Path.GetFullPath(Path.Combine(installPath, Path.GetFileName(file.Url))),
            Url = file.Url,
            MD5 = file.MD5,
            Size = file.Size,
            CompressedPackages = [new GameInstallCompressedPackage
            {
                FullPath = Path.GetFullPath(Path.Combine(installPath, Path.GetFileName(file.Url))),
                Url = file.Url,
                MD5 = file.MD5,
                Size = file.Size,
                DecompressedSize = file.DecompressedSize,
            }],
        };
    }


    /// <summary>
    /// 由 Chunk 清单中的文件生成任务文件，并标记可从本地旧版本复用的块。
    /// </summary>
    /// <param name="file">新版本清单中的文件</param>
    /// <param name="localFile">旧版本清单中的同路径文件，没有时为 <see langword="null"/></param>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="urlPrefix">块的下载地址前缀</param>
    /// <param name="removedFileChunks">新版本已移除的旧文件中的块，按解压后 MD5 索引；同路径找不到时再从这里找</param>
    /// <returns></returns>
    public static GameInstallFile FromSophonChunkFile(SophonChunkFile file, SophonChunkFile? localFile, string installPath, string urlPrefix, IReadOnlyDictionary<string, (SophonChunkFile File, SophonChunk Chunk)>? removedFileChunks = null)
    {
        GameInstallFile result = new GameInstallFile
        {
            DownloadMode = GameInstallDownloadMode.Chunk,
            FullPath = Path.GetFullPath(Path.Combine(installPath, file.File)),
            File = file.File,
            Size = file.Size,
            MD5 = file.Md5,
        };
        // 不要使用 ToDictionary，因为可能有重复的 UncompressedMd5
        Dictionary<string, SophonChunk> localChunkDict = new();
        if (localFile?.Chunks is not null)
        {
            foreach (var item in localFile.Chunks)
            {
                localChunkDict.TryAdd(item.UncompressedMd5, item);
            }
        }
        List<GameInstallFileChunk> chunks = new();
        foreach (SophonChunk? item in file.Chunks)
        {
            var chunk = new GameInstallFileChunk
            {
                Id = item.Id,
                Url = $"{urlPrefix.TrimEnd('/')}/{item.Id}",
                CompressedMD5 = item.CompressedMd5,
                CompressedSize = item.CompressedSize,
                UncompressedMD5 = item.UncompressedMd5,
                UncompressedSize = item.UncompressedSize,
                Offset = item.Offset,
            };
            if (localFile is not null && localChunkDict.TryGetValue(item.UncompressedMd5, out SophonChunk? localChunk) && item.UncompressedSize == localChunk.UncompressedSize)
            {
                SetOriginalFile(chunk, localFile, localChunk, installPath);
            }
            else if (removedFileChunks is not null && removedFileChunks.TryGetValue(item.UncompressedMd5, out var removed) && item.UncompressedSize == removed.Chunk.UncompressedSize)
            {
                // 崩坏3 的 asb 等资源内容一变就换文件名，同路径找不到，但块多半还在被移除的旧文件里。
                // 只从新版本已移除的文件复用：它们要等全部文件写完才删除，不会在更新途中被替换
                SetOriginalFile(chunk, removed.File, removed.Chunk, installPath);
            }
            chunks.Add(chunk);
        }
        result.Chunks = chunks;
        return result;
    }


    /// <summary>
    /// 记录块在本地旧文件中的位置，下载时从旧文件切出这一段而不是联网下载。
    /// </summary>
    /// <param name="chunk">任务文件中的块</param>
    /// <param name="originalFile">包含该块的旧版本文件</param>
    /// <param name="originalChunk">该块在旧版本文件中的记录</param>
    /// <param name="installPath">游戏安装目录</param>
    private static void SetOriginalFile(GameInstallFileChunk chunk, SophonChunkFile originalFile, SophonChunk originalChunk, string installPath)
    {
        chunk.OriginalFileName = originalFile.File;
        chunk.OriginalFileFullPath = Path.GetFullPath(Path.Combine(installPath, originalFile.File));
        chunk.OriginalFileSize = originalFile.Size;
        chunk.OriginalFileMD5 = originalChunk.UncompressedMd5;
        chunk.OriginalFileOffset = originalChunk.Offset;
    }


    public static GameInstallFile FromSophonPatchFile(SophonPatchFile file, string installPath, string localVersion, string urlPrefix)
    {
        GameInstallFile result = new GameInstallFile
        {
            DownloadMode = GameInstallDownloadMode.Patch,
            FullPath = Path.GetFullPath(Path.Combine(installPath, file.File)),
            File = file.File,
            Size = file.Size,
            MD5 = file.Md5,
        };
        if (file.Patches.FirstOrDefault(x => x.Tag == localVersion) is SophonPatchInfo sophonPatchInfo)
        {
            SophonPatch patch = sophonPatchInfo.Patch;
            result.Patch = new GameInstallFilePatch
            {
                Id = patch.Id,
                Url = $"{urlPrefix.TrimEnd('/')}/{patch.Id}",
                PatchFileSize = patch.PatchFileSize,
                PatchFileMD5 = patch.PatchFileMd5,
                PatchOffset = patch.PatchOffset,
                PatchLength = patch.PatchLength,
                OriginalFileName = patch.OriginalFileName,
                OriginalFileSize = patch.OriginalFileSize,
                OriginalFileMD5 = patch.OriginalFileMd5,
                OriginalFileFullPath = string.IsNullOrWhiteSpace(patch.OriginalFileName) ? "" : Path.GetFullPath(Path.Combine(installPath, patch.OriginalFileName)),
            };
        }
        return result;
    }


    public static GameInstallFile FromPkgVersionItem(PkgVersionItem item, string installPath, string urlPrefix)
    {
        return new GameInstallFile
        {
            DownloadMode = GameInstallDownloadMode.SingleFile,
            FullPath = Path.GetFullPath(Path.Combine(installPath, item.RemoteName)),
            File = item.RemoteName,
            Url = urlPrefix + '/' + item.RemoteName,
            MD5 = item.MD5,
            Size = item.FileSize,
        };
    }


}



internal class GameInstallCompressedPackage
{

    public required string FullPath { get; set; }

    public required string Url { get; set; }

    public required string MD5 { get; set; }

    public required long Size { get; set; }

    public required long DecompressedSize { get; set; }

}



internal class GameInstallFileChunk
{

    public required string Id { get; set; }

    public required string Url { get; set; }

    public required long Offset { get; set; }

    public required long CompressedSize { get; set; }

    public required long UncompressedSize { get; set; }

    public required string CompressedMD5 { get; set; }

    public required string UncompressedMD5 { get; set; }

    public string OriginalFileName { get; set; }

    public long OriginalFileSize { get; set; }

    public string OriginalFileMD5 { get; set; }

    /// <summary>
    /// 若本地版本存在相同的 chunk 文件，则为原始文件的完整路径
    /// </summary>
    public string OriginalFileFullPath { get; set; }

    public long OriginalFileOffset { get; set; }

}



internal class GameInstallFilePatch
{

    public required string Id { get; set; }

    public required string Url { get; set; }

    public required long PatchFileSize { get; set; }

    public required string PatchFileMD5 { get; set; }

    public required long PatchOffset { get; set; }

    public required long PatchLength { get; set; }

    public required string OriginalFileName { get; set; }

    public required long OriginalFileSize { get; set; }

    public required string OriginalFileMD5 { get; set; }

    public required string OriginalFileFullPath { get; set; }

    public bool Compression { get; set; }

}
