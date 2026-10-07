using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Starward.RPC.GameInstall;


/// <summary>
/// 基础资源安装更新、修复时，判断只属于完整资源的分类本地是否已有文件（例如游戏内另行下载过），有才继续处理该分类
/// </summary>
public static class ScenarioPackageFiles
{


    /// <summary>
    /// Chunk 清单里的文件本地是否至少存在一个
    /// </summary>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="files">新版本清单的文件</param>
    /// <param name="localFiles">本地版本清单的文件，没有时为 <see langword="null"/></param>
    /// <returns>存在任一文件时为 <see langword="true"/></returns>
    public static bool AnyFileExists(string installPath, IEnumerable<SophonChunkFile> files, IEnumerable<SophonChunkFile>? localFiles)
    {
        IEnumerable<SophonChunkFile> all = localFiles is null ? files : files.Concat(localFiles);
        return all.Any(x => !x.IsFolder && File.Exists(Path.Join(installPath, x.File)));
    }



    /// <summary>
    /// Patch 清单里的目标文件或要打补丁的原文件本地是否至少存在一个
    /// </summary>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="manifest">补丁清单</param>
    /// <param name="localVersion">本地游戏版本，用于找出对应的原文件</param>
    /// <returns>存在任一文件时为 <see langword="true"/></returns>
    public static bool AnyFileExists(string installPath, SophonPatchManifest manifest, string? localVersion)
    {
        foreach (SophonPatchFile item in manifest.Patches)
        {
            if (File.Exists(Path.Join(installPath, item.File)))
            {
                return true;
            }
            string? original = item.Patches.FirstOrDefault(x => x.Tag == localVersion)?.Patch?.OriginalFileName;
            if (!string.IsNullOrWhiteSpace(original) && File.Exists(Path.Join(installPath, original)))
            {
                return true;
            }
        }
        return false;
    }


}
