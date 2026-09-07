using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.HoYoPlay;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameInstall;

internal partial class GamePackageService
{

    private readonly ILogger<GamePackageService> _logger;

    private readonly HoYoPlayService _hoYoPlayService;

    private readonly GameLauncherService _gameLauncherService;


    public GamePackageService(ILogger<GamePackageService> logger, HoYoPlayService hoYoPlayService, GameLauncherService gameLauncherService)
    {
        _logger = logger;
        _hoYoPlayService = hoYoPlayService;
        _gameLauncherService = gameLauncherService;
    }



    /// <summary>
    /// 获取游戏安装路径
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetGameInstallPath(GameId gameId)
    {
        return GameLauncherService.GetGameInstallPath(gameId);
    }



    /// <summary>
    /// 本地游戏版本
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<Version?> GetLocalGameVersionAsync(GameId gameId, string? installPath = null)
    {
        return await _gameLauncherService.GetLocalGameVersionAsync(gameId, installPath);
    }



    /// <summary>
    /// 检查预下载是否完成。
    /// Moonward 完成后会在 config.ini 写入 <c>predownload=本地版本,预下载版本,语音</c>；
    /// 官方启动器不写该字段，而是把补丁放到安装目录的 ldiff（文件名为 id 或 id_md5），
    /// 因此还要对照 getPatchBuild 里当前版本的 compressed_size 合计。
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> CheckPreDownloadFinishedAsync(GameId gameId, string? installPath = null, CancellationToken cancellationToken = default)
    {
        installPath ??= GameLauncherService.GetGameInstallPath(gameId);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return false;
        }
        string? predownloadVersion = null;
        GameConfig? gameConfig = await _hoYoPlayService.GetGameConfigAsync(gameId, cancellationToken);
        if (gameConfig is null)
        {
            throw new ArgumentOutOfRangeException($"Game config is null ({gameId.Id}, {gameId.GameBiz}).");
        }
        GameBranch? gameBranch = null;
        GamePackage? package = null;
        bool isChunkMode = gameConfig.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK or DownloadMode.DOWNLOAD_MODE_LDIFF;
        if (isChunkMode)
        {
            gameBranch = await _hoYoPlayService.GetGameBranchAsync(gameId, cancellationToken);
            if (gameBranch is null)
            {
                throw new ArgumentOutOfRangeException($"Game branch is null ({gameId.Id}, {gameId.GameBiz}).");
            }
            if (gameBranch.PreDownload is null)
            {
                return false;
            }
            predownloadVersion = gameBranch.PreDownload.Tag;
        }
        else
        {
            package = await _hoYoPlayService.GetGamePackageAsync(gameId, cancellationToken);
            if (package.PreDownload.Major is null)
            {
                return false;
            }
            predownloadVersion = package.PreDownload.Major.Version;
        }

        var config = Path.Join(installPath, "config.ini");
        if (!File.Exists(config))
        {
            return false;
        }
        var str = await File.ReadAllTextAsync(config, cancellationToken);
        string? localVersion = null;
        var matches = GameVersionRegex().Matches(str);
        if (matches.Count > 0)
        {
            localVersion = matches[^1].Groups[1].Value.Trim();
        }
        string? predownload = PreDownloadRegex().Match(str).Groups[1].Value.Trim();
        AudioLanguage lang = await GetAudioLanguageAsync(gameId, installPath);
        if (predownload == $"{localVersion},{predownloadVersion},{lang}")
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(localVersion) || string.IsNullOrWhiteSpace(predownloadVersion))
        {
            return false;
        }
        try
        {
            if (isChunkMode)
            {
                return await CheckSophonPredownloadFilesAsync(installPath, gameConfig, gameBranch!, localVersion, lang, cancellationToken);
            }
            return CheckPackagePredownloadFiles(installPath, package!, localVersion, lang);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Check predownload files of ({GameBiz})", gameId.GameBiz);
            return false;
        }
    }


    [GeneratedRegex(@"game_version=(.+)")]
    private static partial Regex GameVersionRegex();

    [GeneratedRegex(@"predownload=(.+)")]
    private static partial Regex PreDownloadRegex();


    /// <summary>
    /// 对照官方/Moonward 落到磁盘上的 Sophon 预下载文件是否已经下完。
    /// </summary>
    private async Task<bool> CheckSophonPredownloadFilesAsync(string installPath, GameConfig gameConfig, GameBranch gameBranch, string localVersion, AudioLanguage lang, CancellationToken cancellationToken)
    {
        GameBranchPackage? pre = gameBranch.PreDownload;
        if (pre is null)
        {
            return false;
        }
        // 当前版本在 diff_tags 里时官方走 ldiff；Moonward 同样把补丁写到 ldiff/
        if (!pre.DiffTags.Any(x => x == localVersion))
        {
            return false;
        }
        GameSophonPatchBuild? build = await _hoYoPlayService.GetGameSophonPatchBuildAsync(gameBranch, pre, cancellationToken);
        if (build is null)
        {
            return false;
        }
        List<string> ignoreMatchingFields = PreDownloadDialog.GetIgnoreMatchingFields(installPath, gameConfig);
        List<GameSophonPatchManifest> manifests = PreDownloadDialog.GetAvaliableGameSophonPatchManifests(build, lang, ignoreMatchingFields);
        long expectedBytes = 0;
        int expectedChunks = 0;
        foreach (GameSophonPatchManifest manifest in manifests)
        {
            if (manifest.Stats is not null && manifest.Stats.TryGetValue(localVersion, out GameSophonManifestStats? stats))
            {
                expectedBytes += stats.CompressedSize;
                expectedChunks += stats.ChunkCount;
            }
        }
        if (expectedBytes <= 0 || expectedChunks <= 0)
        {
            return false;
        }
        string ldiff = Path.Join(installPath, "ldiff");
        (long localBytes, int localFiles) = SumPredownloadDirectory(ldiff);
        return localBytes >= expectedBytes && localFiles >= expectedChunks;
    }


    /// <summary>
    /// 压缩包模式下，预下载 zip 是否都已按大小落在安装目录。
    /// </summary>
    private static bool CheckPackagePredownloadFiles(string installPath, GamePackage package, string localVersion, AudioLanguage lang)
    {
        GamePackageResource? resource = package.PreDownload.Patches?.FirstOrDefault(x => x.Version == localVersion)
            ?? package.PreDownload.Major;
        if (resource is null)
        {
            return false;
        }
        List<GamePackageFile> files = [.. resource.GamePackages];
        foreach (AudioLanguage item in Enum.GetValues<AudioLanguage>())
        {
            if (item is AudioLanguage.None or AudioLanguage.All)
            {
                continue;
            }
            if (lang.HasFlag(item)
                && resource.AudioPackages.FirstOrDefault(x => x.Language == item.ToDescription()) is GamePackageFile audio)
            {
                files.Add(audio);
            }
        }
        if (files.Count == 0)
        {
            return false;
        }
        foreach (GamePackageFile file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Url))
            {
                return false;
            }
            string path = Path.Join(installPath, Path.GetFileName(file.Url));
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size)
            {
                return false;
            }
        }
        return true;
    }


    /// <summary>
    /// 统计预下载目录中已完成文件的总大小与数量（忽略未下完的 *_tmp）。
    /// 官方文件名是 <c>id_md5</c>，Moonward 是 <c>id</c>，都算。
    /// </summary>
    private static (long Bytes, int Files) SumPredownloadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }
        long bytes = 0;
        int files = 0;
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (file.EndsWith("_tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            bytes += new FileInfo(file).Length;
            files++;
        }
        return (bytes, files);
    }



    /// <summary>
    /// 获取语音包语言
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<AudioLanguage> GetAudioLanguageAsync(GameId gameId, string? installPath = null)
    {
        GameConfig? config = await _hoYoPlayService.GetGameConfigAsync(gameId);
        if (string.IsNullOrWhiteSpace(config?.AudioPackageScanDir))
        {
            return AudioLanguage.None;
        }
        installPath ??= GameLauncherService.GetGameInstallPath(gameId);
        AudioLanguage flag = AudioLanguage.None;
        string file = Path.Join(installPath, config.AudioPackageScanDir);
        if (File.Exists(file))
        {
            var lines = await File.ReadAllLinesAsync(file);
            if (lines.Any(x => x.Contains("Chinese"))) { flag |= AudioLanguage.Chinese; }
            if (lines.Any(x => x.Contains("English(US)"))) { flag |= AudioLanguage.English; }
            if (lines.Any(x => x.Contains("Japanese"))) { flag |= AudioLanguage.Japanese; }
            if (lines.Any(x => x.Contains("Korean"))) { flag |= AudioLanguage.Korean; }
        }
        return flag;
    }



    /// <summary>
    /// 设置语音包语言
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath"></param>
    /// <param name="lang"></param>
    /// <returns></returns>
    public async Task SetAudioLanguageAsync(GameId gameId, string installPath, AudioLanguage lang)
    {
        GameConfig? config = await _hoYoPlayService.GetGameConfigAsync(gameId);
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
        await File.WriteAllLinesAsync(file, lines);
    }


}
