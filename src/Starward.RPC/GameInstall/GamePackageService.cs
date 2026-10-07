using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.HoYoPlay;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using ZstdSharp;

namespace Starward.RPC.GameInstall;

internal partial class GamePackageService
{


    private readonly ILogger<GamePackageService> _logger;

    private readonly HoYoPlayClient _hoyoplayClient;

    private readonly HttpClient _httpClient;


    public GamePackageService(ILogger<GamePackageService> logger, HoYoPlayClient hoYoPlayClient, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _hoyoplayClient = hoYoPlayClient;
    }





    /// <summary>
    /// 本地游戏版本
    /// </summary>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<Version?> GetLocalGameVersionAsync(string installPath)
    {
        var config = Path.Join(installPath, "config.ini");
        if (File.Exists(config))
        {
            var str = await File.ReadAllTextAsync(config);
            var matches = GameVersionRegex().Matches(str);
            Version? version = null;
            if (matches.Count > 0)
            {
                _ = Version.TryParse(matches[^1].Groups[1].Value, out version);
            }
            return version;
        }
        else
        {
            _logger.LogWarning("config.ini not found: {path}", config);
            return null;
        }
    }


    [GeneratedRegex(@"game_version=(.+)")]
    private static partial Regex GameVersionRegex();





    /// <summary>
    /// 设置语音包语言
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath"></param>
    /// <param name="lang"></param>
    /// <returns></returns>
    public async Task SetAudioLanguageAsync(GameId gameId, string installPath, AudioLanguage lang, CancellationToken cancellationToken = default)
    {
        GameConfig? config = await GetGameConfigAsync(gameId, cancellationToken);
        if (string.IsNullOrWhiteSpace(config?.AudioPackageScanDir))
        {
            return;
        }
        string file = Path.Join(installPath, config.AudioPackageScanDir);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var lines = new List<string>(4);
        if (lang.HasFlag(AudioLanguage.Chinese)) { lines.Add("Chinese"); }
        if (lang.HasFlag(AudioLanguage.English)) { lines.Add("English(US)"); }
        if (lang.HasFlag(AudioLanguage.Japanese)) { lines.Add("Japanese"); }
        if (lang.HasFlag(AudioLanguage.Korean)) { lines.Add("Korean"); }
        await File.WriteAllLinesAsync(file, lines, cancellationToken);
    }



    /// <summary>
    /// 把安装时选择的资源场景（完整或基础资源）写入游戏目录，游戏和官方启动器都读这个文件
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="type">资源场景</param>
    /// <param name="cancellationToken"></param>
    public async Task SetScenarioPackageTypeAsync(GameId gameId, string installPath, GameScenarioPackageType type, CancellationToken cancellationToken = default)
    {
        GameConfig? config = await GetGameConfigAsync(gameId, cancellationToken);
        await GameScenarioPackage.SetLocalPackageTypeAsync(installPath, config, type, cancellationToken);
    }



    /// <summary>
    /// 安装的是基础资源时，记下只属于完整资源的分类，准备文件时据此跳过
    /// </summary>
    /// <param name="context"></param>
    /// <param name="package">要安装的分支（更新、修复为当前版本，预下载为预下载分支）</param>
    private void SetBaseExcludedMatchingFields(GameInstallContext context, GameBranchPackage? package)
    {
        GameScenarioPackageType type = context.PackageType is not GameScenarioPackageType.Unknown
            ? context.PackageType
            : GameScenarioPackage.GetLocalPackageType(context.InstallPath, context.GameConfig);
        context.BaseExcludedMatchingFields = type is GameScenarioPackageType.Base ? GameScenarioPackage.GetFullOnlyMatchingFields(package) : [];
        if (type is not GameScenarioPackageType.Unknown)
        {
            _logger.LogInformation("Scenario package of ({GameBiz}): {Type}, {Count} full-only categories.", context.GameId.GameBiz, type, context.BaseExcludedMatchingFields.Count);
        }
    }



    /// <summary>
    /// 准备游戏包
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task PrepareGamePackageAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            context.GameConfig = await GetGameConfigAsync(context.GameId, cancellationToken);
            if (context.GameConfig is null)
            {
                _logger.LogWarning("GameConfig of ({GameBiz}) is null.", context.GameId.GameBiz);
                throw new ArgumentNullException($"GameConfig of ({context.GameId.GameBiz}) is null.");
            }
            if (context.Operation is GameInstallOperation.RepairWPFPackage)
            {
                await PrepareForRepairWPFPackageAsync(context, cancellationToken);
                return;
            }
            if (context.Operation is GameInstallOperation.Predownload or GameInstallOperation.Update)
            {
                await PrepareForPredownloadOrUpdateAsync(context, cancellationToken);
            }
            else if (context.Operation is GameInstallOperation.Install or GameInstallOperation.Repair)
            {
                await PrepareForInstallOrRepairAsync(context, cancellationToken);
            }
            context.WPFPackage = (await _hoyoplayClient.GetWPFPackageAsync(LauncherId.FromGameId(context.GameId)!, "en-us", context.GameId, cancellationToken))?.WPFPackage;
            if (context.GameConfig.EnableResourceBlacklist && context.TaskFiles?.Count > 0)
            {
                string blacklistPath = Path.Join(context.InstallPath, context.GameConfig.BlacklistDir);
                if (File.Exists(blacklistPath))
                {
                    using var fs = File.OpenRead(blacklistPath);
                    var items = await GameInstallHelper.DeserilizerLinesAsync<BlacklistItem>(fs, cancellationToken);
                    foreach (var item in items)
                    {
                        if (context.TaskFiles.FirstOrDefault(x => x.File == item.FileName) is GameInstallFile taskFile)
                        {
                            context.TaskFiles.Remove(taskFile);
                        }
                    }
                }
            }
            context.GameChannelSDK = await GetGameChannelSDKAsync(context.GameId, cancellationToken);
            context.DeprecatedFileConfig = await GetGameDeprecatedFileAsync(context.GameId, cancellationToken);

            _logger.LogInformation("""
                Prepare game package ({GameBiz}) finished:
                Operation: {Operation}
                DefaultDownloadMode: {DefaultDownloadMode}
                LatestGameVersion: {LatestGameVersion}
                PredownloadVersion: {PredownloadVersion}
                LocalGameVersion: {LocalGameVersion}
                GameSophonChunkBuild: {GameSophonChunkBuild}
                LocalVersionSophonChunkBuild: {LocalVersionSophonChunkBuild}
                GameSophonPatchBuild: {GameSophonPatchBuild}
                GamePackage: {GamePackage}
                GameChannelSDK: {GameChannelSDK}
                DeprecatedFileConfig: {DeprecatedFileConfig}
                TaskFiles: {TaskFiles}
                DownloadMode: {DownloadMode}
                WPFVersion: {WPFVersion}
                """,
                context.GameId.GameBiz,
                context.Operation,
                context.GameConfig.DefaultDownloadMode,
                context.LatestGameVersion,
                context.PredownloadVersion,
                context.LocalGameVersion,
                context.GameSophonChunkBuild?.Tag,
                context.LocalVersionSophonChunkBuild?.Tag,
                context.GameSophonPatchBuild?.Tag,
                context.GamePackage?.Main.Major?.Version,
                context.GameChannelSDK?.Version,
                context.DeprecatedFileConfig?.DeprecatedFiles?.Count,
                context.TaskFiles?.Count,
                context.DownloadMode,
                context.WPFPackage?.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Prepare game package ({GameBiz})", context.GameId.GameBiz);
            throw;
        }
    }


    /// <summary>
    /// 准备只修复 WPF 包（千星沙箱）：只需要游戏配置和 WPF 包信息，不取游戏资源清单。
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException">该游戏没有 WPF 包</exception>
    private async Task PrepareForRepairWPFPackageAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        context.WPFPackage = (await _hoyoplayClient.GetWPFPackageAsync(LauncherId.FromGameId(context.GameId)!, "en-us", context.GameId, cancellationToken))?.WPFPackage;
        if (context.WPFPackage is null)
        {
            _logger.LogWarning("WPFPackage of ({GameBiz}) is null.", context.GameId.GameBiz);
            throw new NotSupportedException($"WPFPackage of ({context.GameId.GameBiz}) is null.");
        }
        // 空列表表示准备完成，继续任务时不再重复准备
        context.TaskFiles = [];
        _logger.LogInformation("Prepare WPFPackage ({GameBiz}) finished: {WPFVersion}", context.GameId.GameBiz, context.WPFPackage.Version);
    }


    /// <summary>
    /// 准备安装或修复
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task PrepareForInstallOrRepairAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        GameId gameId = context.GameId;
        if (context.GameConfig!.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK or DownloadMode.DOWNLOAD_MODE_LDIFF)
        {
            var branch = await _hoyoplayClient.GetGameBranchAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
            if (branch is not null)
            {
                context.LatestGameVersion = branch.Main.Tag;
                context.PredownloadVersion = branch.PreDownload?.Tag;
                SetBaseExcludedMatchingFields(context, branch.Main);
                context.GameSophonChunkBuild = await GetGameSophonChunkBuildAsync(branch, branch.Main, "", cancellationToken);
                Version? localVersion = await GetLocalGameVersionAsync(context.InstallPath);
                if (localVersion is not null)
                {
                    context.LocalVersionSophonChunkBuild = await GetGameSophonChunkBuildAsync(branch, branch.Main, localVersion.ToString(), cancellationToken);
                }
            }
        }
        if (context.GameSophonChunkBuild is null)
        {
            context.GamePackage = await GetGamePackageAsync(gameId, cancellationToken);
            context.LatestGameVersion = context.GamePackage?.Main.Major?.Version;
            context.PredownloadVersion = context.GamePackage?.PreDownload.Major?.Version;
        }
        await PrepareGameInstallContextFilesAsync(context, null, cancellationToken);
    }


    /// <summary>
    /// 准备预下载或更新
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    private async Task PrepareForPredownloadOrUpdateAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        GameId gameId = context.GameId;
        // 本地游戏版本
        Version? localVersion = await GetLocalGameVersionAsync(context.InstallPath);
        if (localVersion is null)
        {
            _logger.LogWarning("LocalGameVersion of ({GameBiz}) is null.", gameId.GameBiz);
            throw new ArgumentNullException($"LocalGameVersion of ({gameId.GameBiz}) is null.");
        }
        context.LocalGameVersion = localVersion.ToString();
        if (context.GameConfig!.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK or DownloadMode.DOWNLOAD_MODE_LDIFF)
        {
            GameBranch? branch = await _hoyoplayClient.GetGameBranchAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
            if (branch is null)
            {
                _logger.LogWarning("GameBranch of ({GameBiz}) is null.", gameId.GameBiz);
                throw new ArgumentNullException($"GameBranch of ({gameId.GameBiz}) is null.");
            }
            context.LatestGameVersion = branch.Main.Tag;
            SetBaseExcludedMatchingFields(context, context.Operation is GameInstallOperation.Update || branch.PreDownload is null ? branch.Main : branch.PreDownload);
            if (context.Operation is GameInstallOperation.Update || branch.PreDownload is null)
            {
                // 更新
                // 本地游戏版本是否有补丁
                bool canPatch = branch.Main.DiffTags.Any(x => x == context.LocalGameVersion);
                if (canPatch)
                {
                    context.GameSophonPatchBuild = await GetGameSophonPatchBuildAsync(branch, branch.Main, cancellationToken);
                }
                if (context.GameSophonPatchBuild is null)
                {
                    context.GameSophonChunkBuild = await GetGameSophonChunkBuildAsync(branch, branch.Main, "", cancellationToken);
                    context.LocalVersionSophonChunkBuild = await GetGameSophonChunkBuildAsync(branch, branch.Main, localVersion.ToString(), cancellationToken);
                }
            }
            else
            {
                // 预下载
                context.PredownloadVersion = branch.PreDownload.Tag;
                bool canPatch = branch.PreDownload.DiffTags.Any(x => x == context.LocalGameVersion);
                if (canPatch)
                {
                    context.GameSophonPatchBuild = await GetGameSophonPatchBuildAsync(branch, branch.PreDownload, cancellationToken);
                }
                if (context.GameSophonPatchBuild is null)
                {
                    context.GameSophonChunkBuild = await GetGameSophonChunkBuildAsync(branch, branch.PreDownload, "", cancellationToken);
                    context.LocalVersionSophonChunkBuild = await GetGameSophonChunkBuildAsync(branch, branch.PreDownload, localVersion.ToString(), cancellationToken);
                }
            }
            if (context.GameSophonPatchBuild is null && context.GameSophonChunkBuild is null)
            {
                // Chunk 模式不会取压缩包；两种清单都没有时继续执行会得到 0 个文件，预下载会被误记为已完成
                _logger.LogWarning("Sophon build of ({GameBiz}) {Operation} is unavailable.", gameId.GameBiz, context.Operation);
                throw new InvalidOperationException($"Sophon build of ({gameId.GameBiz}) {context.Operation} is unavailable.");
            }
        }
        else
        {
            GamePackage? package = await GetGamePackageAsync(gameId, cancellationToken);
            if (package is null)
            {
                _logger.LogError("GamePackage of ({GameBiz}) is null.", gameId.GameBiz);
                throw new ArgumentNullException($"GamePackage of ({gameId.GameBiz}) is null.");
            }
            if (context.Operation is GameInstallOperation.Predownload && package.PreDownload.Major is null)
            {
                _logger.LogError("Predownload package of ({GameBiz}) is null.", gameId.GameBiz);
                throw new NotSupportedException($"Predownload package of ({gameId.GameBiz}) is null.");
            }
            context.LatestGameVersion = package.Main.Major?.Version;
            if (context.Operation is GameInstallOperation.Update || package.PreDownload.Major is null)
            {
                // 更新
                context.GamePackage = package;
            }
            else
            {
                // 预下载
                context.PredownloadVersion = package.PreDownload.Major.Version;
                context.GamePackage = package;
            }
        }
        await PrepareGameInstallContextFilesAsync(context, localVersion.ToString(), cancellationToken);
    }




    /// <summary>
    /// 准备游戏任务的所有文件信息
    /// </summary>
    /// <param name="context"></param>
    /// <param name="localVersion"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task PrepareGameInstallContextFilesAsync(GameInstallContext context, string? localVersion = null, CancellationToken cancellationToken = default)
    {
        List<GameInstallFile> taskFiles = new();
        if (context.Operation is GameInstallOperation.Predownload or GameInstallOperation.Update && !string.IsNullOrWhiteSpace(localVersion))
        {
            // 预下载或更新
            if (context.GameSophonPatchBuild is not null)
            {
                context.DownloadMode = GameInstallDownloadMode.Patch;
                List<SophonPatchFile> patches = new();
                List<SophonPatchDeleteFile> deletes = new();
                List<string> ignoreMatchingFields = GetIgnoreMatchingFields(context);
                List<GameSophonPatchManifest> manifests = GetAvaliableGameSophonPatchManifests(context.GameSophonPatchBuild, context.AudioLanguage, ignoreMatchingFields);
                foreach (GameSophonPatchManifest manifest in manifests)
                {
                    bool compression = manifest.DiffDownload.Compression is not 0;
                    bool isGameOrAudio = manifest.MatchingField is "game" or "zh-cn" or "en-us" or "ja-jp" or "ko-kr";
                    SophonPatchManifest patchManifest = await GetSophonPatchManifestAsync(manifest, cancellationToken);
                    if (context.BaseExcludedMatchingFields.Contains(manifest.MatchingField)
                        && !ScenarioPackageFiles.AnyFileExists(context.InstallPath, patchManifest, localVersion))
                    {
                        // 基础资源：本地没有的完整资源分类不更新，否则一次更新就补成完整资源
                        continue;
                    }
                    patches.AddRange(patchManifest.Patches);
                    if (patchManifest.DeleteTags.FirstOrDefault(x => x.Tag == localVersion) is SophonPatchDeleteTag deleteTag)
                    {
                        deletes.AddRange(deleteTag.DeleteCollection.DeleteFiles);
                    }
                    foreach (SophonPatchFile item in patchManifest.Patches)
                    {
                        var file = GameInstallFile.FromSophonPatchFile(item, context.InstallPath, localVersion, manifest.DiffDownload.UrlPrefix);
                        if (!isGameOrAudio && string.IsNullOrWhiteSpace(file.Patch?.OriginalFileName))
                        {
                            if (File.Exists(file.FullPath) && new FileInfo(file.FullPath).Length == file.Size)
                            {
                                // todo 存在少数文件大小一致但内容不同
                                // 排除已存在的文件
                                continue;
                            }
                        }
                        if (compression && file.Patch is not null)
                        {
                            file.Patch.Compression = true;
                        }
                        taskFiles.Add(file);
                    }
                }
                // 排除不需要删除的文件
                var dict = new Dictionary<string, SophonPatchFile>();
                foreach (var item in patches)
                {
                    dict.TryAdd(item.File, item);
                }
                for (int i = 0; i < deletes.Count; i++)
                {
                    if (dict.ContainsKey(deletes[i].File))
                    {
                        deletes.RemoveAt(i);
                        i--;
                    }
                }
                context.SophonPatchFiles = patches;
                context.SophonPatchDeleteFiles = deletes;
            }
            else if (context.GameSophonChunkBuild is not null)
            {
                context.DownloadMode = GameInstallDownloadMode.Chunk;
                taskFiles.AddRange(await PrepareSophonChunkFilesWithLocalVersionAsync(context, cancellationToken));
            }
            else if (context.GamePackage is not null)
            {
                context.DownloadMode = GameInstallDownloadMode.CompressedPackage;
                if (context.Operation is GameInstallOperation.Update || context.GamePackage.PreDownload.Major is null)
                {
                    if (context.GamePackage.Main.Patches.FirstOrDefault(x => x.Version == localVersion) is GamePackageResource patch)
                    {
                        taskFiles.Add(GameInstallFile.FromGamePackageResource(patch, context.InstallPath));
                        foreach (var lang in Enum.GetValues<AudioLanguage>())
                        {
                            if (context.AudioLanguage.HasFlag(lang))
                            {
                                if (patch.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                                {
                                    taskFiles.Add(GameInstallFile.FromGamePackageFile(packageFile, context.InstallPath));
                                }
                            }
                        }
                    }
                    else
                    {
                        taskFiles.Add(GameInstallFile.FromGamePackageResource(context.GamePackage.Main.Major!, context.InstallPath));
                        foreach (var lang in Enum.GetValues<AudioLanguage>())
                        {
                            if (context.AudioLanguage.HasFlag(lang))
                            {
                                if (context.GamePackage.Main.Major!.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                                {
                                    taskFiles.Add(GameInstallFile.FromGamePackageFile(packageFile, context.InstallPath));
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (context.GamePackage.PreDownload.Patches.FirstOrDefault(x => x.Version == localVersion) is GamePackageResource patch)
                    {
                        taskFiles.Add(GameInstallFile.FromGamePackageResource(patch, context.InstallPath));
                        foreach (var lang in Enum.GetValues<AudioLanguage>())
                        {
                            if (context.AudioLanguage.HasFlag(lang))
                            {
                                if (patch.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                                {
                                    taskFiles.Add(GameInstallFile.FromGamePackageFile(packageFile, context.InstallPath));
                                }
                            }
                        }
                    }
                    else
                    {
                        taskFiles.Add(GameInstallFile.FromGamePackageResource(context.GamePackage.PreDownload.Major!, context.InstallPath));
                        foreach (var lang in Enum.GetValues<AudioLanguage>())
                        {
                            if (context.AudioLanguage.HasFlag(lang))
                            {
                                if (context.GamePackage.PreDownload.Major!.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                                {
                                    taskFiles.Add(GameInstallFile.FromGamePackageFile(packageFile, context.InstallPath));
                                }
                            }
                        }
                    }
                }
            }
        }
        else if (context.Operation is GameInstallOperation.Install)
        {
            // 安装
            if (context.GameSophonChunkBuild is not null)
            {
                context.DownloadMode = GameInstallDownloadMode.Chunk;
                List<SophonChunkFile> chunks = new();
                List<string> ignoreMatchingFields = GetIgnoreMatchingFields(context);
                // 基础资源：不下载只属于完整资源的分类
                ignoreMatchingFields.AddRange(context.BaseExcludedMatchingFields);
                List<GameSophonChunkManifest> manifests = GetAvailableGameSophonChunkManifests(context.GameSophonChunkBuild, context.AudioLanguage, ignoreMatchingFields);
                foreach (GameSophonChunkManifest manifest in manifests)
                {
                    List<SophonChunkFile> items = await GetSophonChunkFilesAsync(manifest, cancellationToken);
                    chunks.AddRange(items);
                    foreach (SophonChunkFile item in items)
                    {
                        if (!item.IsFolder)
                        {
                            taskFiles.Add(GameInstallFile.FromSophonChunkFile(item, null, context.InstallPath, manifest.ChunkDownload.UrlPrefix));
                        }
                    }
                }
                context.SophonChunkFiles = chunks;
            }
            else if (context.GamePackage is not null)
            {
                context.DownloadMode = GameInstallDownloadMode.CompressedPackage;
                taskFiles.Add(GameInstallFile.FromGamePackageResource(context.GamePackage.Main.Major!, context.InstallPath));
                foreach (AudioLanguage lang in Enum.GetValues<AudioLanguage>())
                {
                    if (context.AudioLanguage.HasFlag(lang))
                    {
                        if (context.GamePackage.Main.Major?.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile gamePackageFile)
                        {
                            taskFiles.Add(GameInstallFile.FromGamePackageFile(gamePackageFile, context.InstallPath));
                        }
                    }
                }
            }
        }
        else if (context.Operation is GameInstallOperation.Repair)
        {
            // 修复
            if (context.GameSophonChunkBuild is not null)
            {
                context.DownloadMode = GameInstallDownloadMode.Chunk;
                taskFiles.AddRange(await PrepareSophonChunkFilesWithLocalVersionAsync(context, cancellationToken));
            }
            else if (context.GamePackage is not null)
            {
                if (!string.IsNullOrWhiteSpace(context.GamePackage.Main.Major?.ResListUrl))
                {
                    string prefix = context.GamePackage.Main.Major.ResListUrl.TrimEnd('/');
                    context.DownloadMode = GameInstallDownloadMode.SingleFile;
                    var pkg_versions = await GetPkgVersionItemsAsync(context, cancellationToken);
                    foreach (PkgVersionItem item in pkg_versions)
                    {
                        taskFiles.Add(GameInstallFile.FromPkgVersionItem(item, context.InstallPath, prefix));
                    }
                }
            }
        }

        // 硬链接
        if (Directory.Exists(context.HardLinkPath)
            && Path.GetFullPath(context.InstallPath) != Path.GetFullPath(context.HardLinkPath)
            && Path.GetPathRoot(context.HardLinkPath) == Path.GetPathRoot(context.InstallPath)
            && DriveHelper.GetDriveFormat(context.InstallPath) is "NTFS")
        {
            if (context.Operation is GameInstallOperation.Update && context.DownloadMode is GameInstallDownloadMode.Patch)
            {
                Version? hardLinkVersion = await GetLocalGameVersionAsync(context.HardLinkPath);
                if (hardLinkVersion is not null && context.LatestGameVersion == hardLinkVersion.ToString())
                {
                    // patch 更新模式下，如果硬链接目录版本和最新版本一致，则直接硬链接
                    _logger.LogInformation("Change patch update operation of {GameBiz} to repair because of hard link, path: {path}.", context.GameId.GameBiz, context.HardLinkPath);
                    context.Operation = GameInstallOperation.Repair;
                    await PrepareGamePackageAsync(context, cancellationToken);
                    return;
                }
            }
            GameBiz gameBiz = context.GameId.GameBiz;
            if (gameBiz.Game is GameBiz.hk4e)
            {
                const string YuanShen_Data = nameof(YuanShen_Data);
                const string GenshinImpact_Data = nameof(GenshinImpact_Data);
                string source = gameBiz.Server switch
                {
                    "cn" => "YuanShen_Data",
                    "global" => "GenshinImpact_Data",
                    "bilibili" => "YuanShen_Data",
                    _ => "",
                };
                string target = "";
                foreach (var dir in Directory.GetDirectories(context.HardLinkPath, "*_Data"))
                {
                    string name = Path.GetFileName(dir);
                    if (name is "YuanShen_Data" or "GenshinImpact_Data")
                    {
                        target = name;
                        break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
                {
                    if (source == target)
                    {
                        foreach (var item in taskFiles)
                        {
                            if (!string.IsNullOrWhiteSpace(item.File))
                            {
                                item.HardLinkTarget = Path.GetFullPath(Path.Join(context.HardLinkPath, item.File));
                            }
                        }
                    }
                    else
                    {
                        foreach (var item in taskFiles)
                        {
                            if (!string.IsNullOrWhiteSpace(item.File))
                            {
                                item.HardLinkTarget = Path.GetFullPath(Path.Join(context.HardLinkPath, item.File.Replace(source, target)));
                            }
                        }
                    }
                }
                _logger.LogInformation("Hard link {GameBiz} link target path: {Target}", context.GameId.GameBiz, context.HardLinkPath);
            }
            else if (gameBiz.Game is GameBiz.hkrpg && context.GameConfig?.DefaultDownloadMode is not DownloadMode.DOWNLOAD_MODE_CHUNK)
            {
                Version? hardLinkVersion = await GetLocalGameVersionAsync(context.HardLinkPath);
                if (hardLinkVersion is not null && context.LatestGameVersion == hardLinkVersion.ToString())
                {
                    if (context.Operation is GameInstallOperation.Install or GameInstallOperation.Update)
                    {
                        // 星穹铁道硬链接时，需要改为修复模式
                        _logger.LogInformation("Change {GameBiz} operation to repair because of hard link.", context.GameId.GameBiz);
                        context.Operation = GameInstallOperation.Repair;
                        await PrepareGameInstallContextFilesAsync(context, localVersion, cancellationToken);
                        return;
                    }
                    else if (context.Operation is GameInstallOperation.Repair)
                    {
                        string folder = Path.GetFullPath(Path.Combine(context.HardLinkPath, context.GameConfig!.AudioPackageResDir));
                        if (Directory.Exists(folder))
                        {
                            string targetFolder = Path.GetFullPath(Path.Combine(context.InstallPath, context.GameConfig.AudioPackageResDir));
                            string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                            foreach (string item in files)
                            {
                                string target = item.Replace(folder, targetFolder);
                                if (File.Exists(target))
                                {
                                    File.Delete(target);
                                }
                                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                Kernel32.CreateHardLink(target, item);
                            }
                            _logger.LogInformation("Hard link {count} audio files ({GameBiz}) from {path}", files.Length, context.GameId.GameBiz, targetFolder);
                        }
                        foreach (var item in taskFiles)
                        {
                            if (!string.IsNullOrWhiteSpace(item.File))
                            {
                                item.HardLinkTarget = Path.GetFullPath(Path.Join(context.HardLinkPath, item.File));
                            }
                        }
                        _logger.LogInformation("Hard link {GameBiz} link target path: {Target}", context.GameId.GameBiz, context.HardLinkPath);
                    }
                }
            }
            else
            {
                foreach (var item in taskFiles)
                {
                    if (!string.IsNullOrWhiteSpace(item.File))
                    {
                        item.HardLinkTarget = Path.GetFullPath(Path.Join(context.HardLinkPath, item.File));
                    }
                }
                _logger.LogInformation("Hard link {GameBiz} link target path: {Target}", context.GameId.GameBiz, context.HardLinkPath);
            }
        }

        context.TaskFiles = taskFiles;
    }



    /// <summary>
    /// 准备 Chunk 模式下有本地旧版本时（更新、预下载、修复）的任务文件。
    /// 块优先从旧版本同路径文件复用，其次从新版本已移除的旧文件复用；已移除的文件记入
    /// <see cref="GameInstallContext.SophonChunkDeleteFiles"/>，等全部文件写完后再删除。
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>任务文件，不含文件夹</returns>
    private async Task<List<GameInstallFile>> PrepareSophonChunkFilesWithLocalVersionAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        List<string> ignoreMatchingFields = GetIgnoreMatchingFields(context);
        List<GameSophonChunkManifest> manifests = GetAvailableGameSophonChunkManifests(context.GameSophonChunkBuild!, context.AudioLanguage, ignoreMatchingFields);
        List<(GameSophonChunkManifest Manifest, List<SophonChunkFile> Files)> newManifests = new();
        List<SophonChunkFile> chunks = new();
        List<SophonChunkFile> localChunks = new();
        int skippedCategories = 0;
        foreach (GameSophonChunkManifest manifest in manifests)
        {
            List<SophonChunkFile> items = await GetSophonChunkFilesAsync(manifest, cancellationToken);
            List<SophonChunkFile>? localItems = null;
            if (context.LocalVersionSophonChunkBuild?.Manifests.FirstOrDefault(x => x.MatchingField == manifest.MatchingField) is GameSophonChunkManifest localManifest)
            {
                localItems = await GetSophonChunkFilesAsync(localManifest, cancellationToken);
            }
            if (context.BaseExcludedMatchingFields.Contains(manifest.MatchingField)
                && !ScenarioPackageFiles.AnyFileExists(context.InstallPath, items, localItems))
            {
                // 基础资源：本地没有的完整资源分类不处理，否则更新、修复会补成完整资源；游戏内另行下载过的分类照常处理
                skippedCategories++;
                continue;
            }
            newManifests.Add((manifest, items));
            chunks.AddRange(items);
            if (localItems is not null)
            {
                localChunks.AddRange(localItems);
            }
        }
        if (skippedCategories > 0)
        {
            _logger.LogInformation("Skip {Count} full-only categories not installed locally ({GameBiz}).", skippedCategories, context.GameId.GameBiz);
        }

        // Windows 路径不区分大小写：只改了大小写的文件仍是同一个文件，不能算作已移除，否则更新完会把新文件删掉
        Dictionary<string, SophonChunkFile> localFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (SophonChunkFile item in localChunks)
        {
            if (!item.IsFolder)
            {
                localFiles.TryAdd(item.File, item);
            }
        }
        HashSet<string> newFiles = chunks.Where(x => !x.IsFolder).Select(x => x.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (SophonChunkFile File, SophonChunk Chunk)> removedFileChunks = new();
        List<string> deleteFiles = new();
        foreach (SophonChunkFile item in localFiles.Values)
        {
            if (!newFiles.Contains(item.File))
            {
                deleteFiles.Add(item.File);
                foreach (SophonChunk chunk in item.Chunks)
                {
                    removedFileChunks.TryAdd(chunk.UncompressedMd5, (item, chunk));
                }
            }
        }

        List<GameInstallFile> taskFiles = new();
        foreach ((GameSophonChunkManifest manifest, List<SophonChunkFile> items) in newManifests)
        {
            foreach (SophonChunkFile item in items)
            {
                if (!item.IsFolder)
                {
                    localFiles.TryGetValue(item.File, out SophonChunkFile? localFile);
                    taskFiles.Add(GameInstallFile.FromSophonChunkFile(item, localFile, context.InstallPath, manifest.ChunkDownload.UrlPrefix, removedFileChunks));
                }
            }
        }
        context.SophonChunkFiles = chunks;
        if (localChunks.Count > 0)
        {
            context.LocalVersionSophonChunkFiles = localChunks;
        }
        context.SophonChunkDeleteFiles = deleteFiles;
        _logger.LogInformation("Prepare sophon chunk files ({GameBiz}): {Count} files, {DeleteCount} old files removed in new version.", context.GameId.GameBiz, taskFiles.Count, deleteFiles.Count);
        return taskFiles;
    }




    public static List<string> GetIgnoreMatchingFields(GameInstallContext context)
    {
        List<string> ignoreMatchingFields = new List<string>();
        if (context.GameConfig is not null)
        {
            string file = Path.Join(context.InstallPath, context.GameConfig.ResCategoryDir);
            if (File.Exists(file))
            {
                string[] lines = File.ReadAllLines(file);
                // eg. {"category":"10302","is_delete":true}
                foreach (string line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var obj = JsonSerializer.Deserialize<IgnoreMatchingField>(line);
                        if (obj?.IsDelete is true && !string.IsNullOrWhiteSpace(obj.Category))
                        {
                            ignoreMatchingFields.Add(obj.Category);
                        }
                    }
                }
            }
        }
        return ignoreMatchingFields;
    }



    private class IgnoreMatchingField
    {
        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("is_delete")]
        public bool IsDelete { get; set; }
    }



    public static List<GameSophonChunkManifest> GetAvailableGameSophonChunkManifests(GameSophonChunkBuild build, AudioLanguage audioLanguage, IEnumerable<string> ignoreMatchingFields)
    {
        List<GameSophonChunkManifest> manifests = new();
        foreach (GameSophonChunkManifest manifest in build.Manifests)
        {
            if (ignoreMatchingFields.Contains(manifest.MatchingField))
            {
                continue;
            }
            if (manifest.MatchingField.Length is 5 or 10 && manifest.MatchingField.Contains('-'))
            {
                // 跳过语音包 zh-cn or mini-zh-cn
                continue;
            }
            manifests.Add(manifest);
        }
        foreach (AudioLanguage lang in Enum.GetValues<AudioLanguage>())
        {
            if (audioLanguage.HasFlag(lang))
            {
                if (build.Manifests.FirstOrDefault(x => x.MatchingField == lang.ToDescription()) is GameSophonChunkManifest audioManifest)
                {
                    manifests.Add(audioManifest);
                }
            }
        }
        return manifests;
    }



    public static List<GameSophonPatchManifest> GetAvaliableGameSophonPatchManifests(GameSophonPatchBuild build, AudioLanguage audioLanguage, IEnumerable<string> ignoreMatchingFields)
    {
        List<GameSophonPatchManifest> manifests = new();
        foreach (GameSophonPatchManifest manifest in build.Manifests)
        {
            if (ignoreMatchingFields.Contains(manifest.MatchingField))
            {
                continue;
            }
            if (manifest.MatchingField.Length is 5 or 10 && manifest.MatchingField.Contains('-'))
            {
                // 跳过语音包 zh-cn or mini-zh-cn
                continue;
            }
            manifests.Add(manifest);
        }
        foreach (AudioLanguage lang in Enum.GetValues<AudioLanguage>())
        {
            if (audioLanguage.HasFlag(lang))
            {
                if (build.Manifests.FirstOrDefault(x => x.MatchingField == lang.ToDescription()) is GameSophonPatchManifest audioManifest)
                {
                    manifests.Add(audioManifest);
                }
            }
        }
        return manifests;
    }




    private async Task<List<SophonChunkFile>> GetSophonChunkFilesAsync(GameSophonChunkManifest manifest, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await EnsureSophonManifestFileAsync(manifest.ManifestDownload, manifest.Manifest, cancellationToken);
        SophonChunkManifest manifest1 = SophonChunkManifest.Parser.ParseFrom(bytes);
        return manifest1.Chuncks.ToList();
    }



    private async Task<SophonPatchManifest> GetSophonPatchManifestAsync(GameSophonPatchManifest manifest, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await EnsureSophonManifestFileAsync(manifest.ManifestDownload, manifest.Manifest, cancellationToken);
        return SophonPatchManifest.Parser.ParseFrom(bytes);
    }



    private async Task<byte[]> EnsureSophonManifestFileAsync(GameSophonManifestUrl manifestUrl, GameSophonManifestFile manifestFile, CancellationToken cancellationToken = default)
    {
        string cache = Path.Combine(AppConfig.CacheFolder, "game");
        Directory.CreateDirectory(cache);
        string file = Path.Combine(cache, manifestFile.Id);
        bool needDownload = true;
        if (File.Exists(file) && new FileInfo(file).Length == manifestFile.CompressedSize)
        {
            using FileStream fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using DecompressionStream ds = new DecompressionStream(fs);
            using MemoryStream ms = new MemoryStream();
            await ds.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            byte[] md5 = await MD5.HashDataAsync(ms, cancellationToken);
            if (string.Equals(Convert.ToHexString(md5), manifestFile.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                return ms.ToArray();
            }
        }
        if (needDownload)
        {
            using FileStream fs = File.Open(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            string url = $"{manifestUrl.UrlPrefix.TrimEnd('/')}/{manifestFile.Id}";
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using Stream hs = await response.Content.ReadAsStreamAsync(cancellationToken);
            await hs.CopyToAsync(fs, cancellationToken);
        }
        {
            using FileStream fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using DecompressionStream ds = new DecompressionStream(fs);
            using MemoryStream ms = new MemoryStream();
            await ds.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            byte[] md5 = await MD5.HashDataAsync(ms, cancellationToken);
            if (string.Equals(Convert.ToHexString(md5), manifestFile.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                return ms.ToArray();
            }
        }
        throw new Exception($"Download manifest file ({manifestFile.Id}) failed.");
    }



    private async Task<List<PkgVersionItem>> GetPkgVersionItemsAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        List<PkgVersionItem> list = new();
        if (context.GamePackage is not null)
        {
            string? prefix = context.GamePackage.Main.Major!.ResListUrl;
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                list.AddRange(await DownloadAndParsePkgVersionAsync(context.InstallPath, prefix, "pkg_version", cancellationToken));
                if (context.AudioLanguage.HasFlag(AudioLanguage.Chinese))
                {
                    list.AddRange(await DownloadAndParsePkgVersionAsync(context.InstallPath, prefix, "Audio_Chinese_pkg_version", cancellationToken));
                }
                if (context.AudioLanguage.HasFlag(AudioLanguage.English))
                {
                    list.AddRange(await DownloadAndParsePkgVersionAsync(context.InstallPath, prefix, "Audio_English(US)_pkg_version", cancellationToken));
                }
                if (context.AudioLanguage.HasFlag(AudioLanguage.Japanese))
                {
                    list.AddRange(await DownloadAndParsePkgVersionAsync(context.InstallPath, prefix, "Audio_Japanese_pkg_version", cancellationToken));
                }
                if (context.AudioLanguage.HasFlag(AudioLanguage.Korean))
                {
                    list.AddRange(await DownloadAndParsePkgVersionAsync(context.InstallPath, prefix, "Audio_Korean_pkg_version", cancellationToken));
                }
            }
        }
        return list;
    }



    private async Task<List<PkgVersionItem>> DownloadAndParsePkgVersionAsync(string installPath, string prefix, string name, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(installPath, name);
        string url = prefix.TrimEnd('/') + '/' + name;
        using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }
        response.EnsureSuccessStatusCode();
        using Stream hs = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream ms = new();
        await hs.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;
        List<PkgVersionItem> list = await GameInstallHelper.DeserilizerLinesAsync<PkgVersionItem>(ms, cancellationToken);
        using FileStream fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        ms.Position = 0;
        await ms.CopyToAsync(fs, cancellationToken);
        return list;
    }




    #region HoYoPlay API




    private async Task<GameConfig?> GetGameConfigAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        GameConfig? config = await _hoyoplayClient.GetGameConfigAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
        // 仅星穹铁道强制使用 Chunk 作为默认下载模式
        if (config is not null && config.GameId.GameBiz.Value is GameBiz.hkrpg)
        {
            config.DefaultDownloadMode = DownloadMode.DOWNLOAD_MODE_CHUNK;
        }
        return config;
    }



    private async Task<GamePackage?> GetGamePackageAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        return await _hoyoplayClient.GetGamePackageAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
    }



    /// <summary>
    /// 获取 Chunk 模式文件清单。
    /// </summary>
    /// <param name="gameBranch">游戏分支</param>
    /// <param name="gameBranchPackage">正式或预下载分支</param>
    /// <param name="tag">版本号，为空时取该分支的当前版本</param>
    /// <param name="cancellationToken"></param>
    /// <returns>文件清单；该分支没有这个版本或清单为空时返回 <see langword="null"/></returns>
    private async Task<GameSophonChunkBuild?> GetGameSophonChunkBuildAsync(GameBranch gameBranch, GameBranchPackage gameBranchPackage, string tag = "", CancellationToken cancellationToken = default)
    {
        try
        {
            GameSophonChunkBuild build = await _hoyoplayClient.GetGameSophonChunkBuildAsync(gameBranch, gameBranchPackage, tag, cancellationToken);
            if (IsEmptySophonChunkBuild(build) && string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(gameBranchPackage.Tag))
            {
                // 预下载窗口内新版本只能不带 tag 取到（带 tag 返回 -202，见上游 #1484/#1563/#1898 日志）；
                // 窗口结束后 predownload 分支不带 tag 反而返回 retcode 0 的空清单，此时按分支自身版本号再取一次
                _logger.LogWarning("Sophon chunk build of ({GameBiz}) branch {Branch} without tag is empty, retry with tag {Tag}.", gameBranch.GameId.GameBiz, gameBranchPackage.Branch, gameBranchPackage.Tag);
                build = await _hoyoplayClient.GetGameSophonChunkBuildAsync(gameBranch, gameBranchPackage, gameBranchPackage.Tag, cancellationToken);
            }
            if (IsEmptySophonChunkBuild(build))
            {
                _logger.LogWarning("Sophon chunk build of ({GameBiz}) branch {Branch} tag {Tag} is empty.", gameBranch.GameId.GameBiz, gameBranchPackage.Branch, tag);
                return null;
            }
            return build;
        }
        catch (miHoYoApiException ex)
        {
            // not found (-202)：该分支没有这个版本的清单，例如本地版本过旧
            _logger.LogWarning("Sophon chunk build of ({GameBiz}) branch {Branch} tag {Tag} is unavailable: {Message}", gameBranch.GameId.GameBiz, gameBranchPackage.Branch, tag, ex.Message);
            return null;
        }
    }


    /// <summary>
    /// 清单是否为空（接口返回 retcode 0 但没有 build_id 或文件清单）。
    /// </summary>
    /// <param name="build"></param>
    /// <returns></returns>
    private static bool IsEmptySophonChunkBuild(GameSophonChunkBuild? build)
    {
        return build is null || string.IsNullOrWhiteSpace(build.BuildId) || build.Manifests is not { Count: > 0 };
    }



    private async Task<GameSophonPatchBuild?> GetGameSophonPatchBuildAsync(GameBranch gameBranch, GameBranchPackage gameBranchPackage, CancellationToken cancellationToken = default)
    {
        try
        {
            GameSophonPatchBuild build = await _hoyoplayClient.GetGameSophonPatchBuildAsync(gameBranch, gameBranchPackage, cancellationToken);
            if (string.IsNullOrWhiteSpace(build.BuildId))
            {
                return null;
            }
            else
            {
                return build;
            }
        }
        catch (miHoYoApiException)
        {
            return null;
        }
    }



    private async Task<GameChannelSDK?> GetGameChannelSDKAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        return await _hoyoplayClient.GetGameChannelSDKAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
    }



    private async Task<GameDeprecatedFileConfig?> GetGameDeprecatedFileAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        return await _hoyoplayClient.GetGameDeprecatedFileConfigAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
    }



    #endregion




}
