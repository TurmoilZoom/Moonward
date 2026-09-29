using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.HoYoPlay;
using Starward.Helpers;
using Starward.RPC;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;


namespace Starward.Features.GameInstall;

[INotifyPropertyChanged]
public sealed partial class PreDownloadDialog : ContentDialog
{

    private const double GB = 1 << 30;


    private readonly ILogger<PreDownloadDialog> _logger = AppConfig.GetLogger<PreDownloadDialog>();

    private readonly HoYoPlayService _hoYoPlayService = AppConfig.GetService<HoYoPlayService>();

    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    private readonly HttpClient _httpClient = AppConfig.GetService<HttpClient>();

    public PreDownloadDialog()
    {
        this.InitializeComponent();
        this.Loaded += PreDownloadDialog_Loaded;
    }


    public GameId CurrentGameId { get; set; }



    private void PreDownloadDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (CurrentGameId is null)
        {
            _logger.LogWarning("CurrentGameId is null.");
            this.Hide();
            return;
        }
        Telemetry.Track("predownload_dialog_show", CurrentGameId.GameBiz);
        _ = GetGamePackageAsync();
    }




    private string _installationPath;

    private string _localGameVersion;

    /// <summary>
    /// 有补丁包
    /// </summary>
    private bool _hasPatch;

    /// <summary>
    /// 可以下载补丁包
    /// </summary>
    private bool _canPatch;

    private GamePackage? _gamePackage;

    private GameSophonChunkBuild? _gameSophonChunkBuild;

    /// <summary>
    /// 本地版本在预下载分支上的 Chunk 清单，用于统计增量；为 null 时只能整包下载
    /// </summary>
    private GameSophonChunkBuild? _localVersionSophonChunkBuild;

    private GameSophonPatchBuild? _gameSophonPatchBuild;

    private AudioLanguage _audioLanguage;

    private List<string> _ignoreMatchingFields;

    private async Task GetGamePackageAsync()
    {
        try
        {
            ErrorMessage = null;
            // 安装路径
            string? installPath = GamePackageService.GetGameInstallPath(CurrentGameId);
            if (installPath is null)
            {
                _logger.LogWarning("InstallPath of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                return;
            }
            _installationPath = installPath;
            // 本地版本
            Version? version = await _gamePackageService.GetLocalGameVersionAsync(CurrentGameId, _installationPath);
            if (version is null)
            {
                _logger.LogWarning("LocalGameVersion of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                return;
            }
            _localGameVersion = version.ToString();
            // 游戏配置
            GameConfig? config = await _hoYoPlayService.GetGameConfigAsync(CurrentGameId);
            if (config is null)
            {
                _logger.LogWarning("GameConfig of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                return;
            }
            _ignoreMatchingFields = GetIgnoreMatchingFields(_installationPath, config);
            if (config.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK or DownloadMode.DOWNLOAD_MODE_LDIFF)
            {
                GameBranch? gameBranch = await _hoYoPlayService.GetGameBranchAsync(CurrentGameId);
                if (gameBranch?.PreDownload is null)
                {
                    _logger.LogWarning("GameBranch.PreDownload of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                    TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                    return;
                }
                _hasPatch = gameBranch.PreDownload.DiffTags.Count > 0;
                _canPatch = gameBranch.PreDownload.DiffTags.Any(x => x == _localGameVersion);
                if (_hasPatch && !_canPatch)
                {
                    TextBlock_NoPatches.Visibility = Visibility.Visible;
                }
                if (_canPatch)
                {
                    _gameSophonPatchBuild = await _hoYoPlayService.GetGameSophonPatchBuildAsync(gameBranch, gameBranch.PreDownload);
                }
                if (_gameSophonPatchBuild is null)
                {
                    // 没有 ldiff 补丁时（崩坏3 的预下载一直如此）走 Chunk 模式，与 RPC 一样用本地版本的清单做块级去重
                    _gameSophonChunkBuild = await _hoYoPlayService.GetGameSophonChunkBuildAsync(gameBranch, gameBranch.PreDownload);
                    if (_gameSophonChunkBuild is not null)
                    {
                        _localVersionSophonChunkBuild = await _hoYoPlayService.GetGameSophonChunkBuildAsync(gameBranch, gameBranch.PreDownload, _localGameVersion);
                        if (_localVersionSophonChunkBuild is null)
                        {
                            // 本地版本过旧，无法增量，只能下载完整资源
                            TextBlock_NoPatches.Visibility = Visibility.Visible;
                        }
                    }
                }
                if (_gameSophonPatchBuild is null && _gameSophonChunkBuild is null)
                {
                    // Chunk 模式的预下载任务不会回退压缩包，这里也不回退，避免显示一个实际无法开始的预下载
                    _logger.LogWarning("Sophon build of ({GameBiz}) predownload is unavailable.", CurrentGameId.GameBiz);
                    TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                    return;
                }
            }
            else
            {
                GamePackage? package = await _hoYoPlayService.GetGamePackageAsync(CurrentGameId);
                if (package?.PreDownload.Major is null)
                {
                    _logger.LogWarning("PreDownloadMajor of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                    TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                    return;
                }
                _gamePackage = package;
            }
            _audioLanguage = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId);
            await ComputePackageSizeAsync();
            CheckCanPreDownload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game package.");
            ErrorMessage = GetMiHoYoRequestErrorMessage(ex);
        }
    }




    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackageSizeText))]
    public partial long PackageSizeBytes { get; set; }

    public string PackageSizeText => PackageSizeBytes == 0 ? "..." : $"{PackageSizeBytes / GB:F2} GB";



    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnzipSpaceText))]
    public partial long UnzipSpaceBytes { get; set; }

    public string UnzipSpaceText => UnzipSpaceBytes == 0 ? "..." : $"{UnzipSpaceBytes / GB:F2} GB";


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableSpaceText))]
    public partial long AvailableSpaceBytes { get; set; }

    public string AvailableSpaceText => AvailableSpaceBytes == 0 ? "..." : $"{AvailableSpaceBytes / GB:F2} GB";



    /// <summary>拉取预下载信息失败时显示的错误文案。</summary>
    public string? ErrorMessage { get; set => SetProperty(ref field, value); }



    /// <summary>
    /// 将启动器公开接口的 API 或 HTTP 异常转换为预下载对话框可显示的错误文案。
    /// </summary>
    /// <param name="exception">拉取预下载包信息时捕获的异常。</param>
    /// <returns>本地化 API 文案或原始非 API 异常消息。</returns>
    private static string GetMiHoYoRequestErrorMessage(Exception exception)
    {
        return exception is miHoYoApiException or HttpRequestException
            ? MiHoYoApiErrorFeedbackFactory.Create(exception, MiHoYoApiContext.LauncherPublicApi).Message
            : exception.Message;
    }



    private async Task ComputePackageSizeAsync()
    {
        try
        {
            AvailableSpaceBytes = DriveHelper.GetDriveAvailableSpace(_installationPath);
            long size = 0, unzipSize = 0;
            if (_gamePackage is not null)
            {
                if (_gamePackage.PreDownload.Patches.FirstOrDefault(x => x.Version == _localGameVersion) is GamePackageResource patch)
                {
                    size += patch.GamePackages.Sum(x => x.Size);
                    unzipSize += patch.GamePackages.Sum(x => x.DecompressedSize);

                    foreach (var lang in Enum.GetValues<AudioLanguage>())
                    {
                        if (_audioLanguage.HasFlag(lang))
                        {
                            if (patch.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                            {
                                size += packageFile.Size;
                                unzipSize += packageFile.DecompressedSize;
                            }
                        }
                    }
                }
                else if (_gamePackage.PreDownload.Major is not null)
                {
                    size += _gamePackage.PreDownload.Major.GamePackages.Sum(x => x.Size);
                    unzipSize += _gamePackage.PreDownload.Major.GamePackages.Sum(x => x.DecompressedSize);

                    foreach (var lang in Enum.GetValues<AudioLanguage>())
                    {
                        if (_audioLanguage.HasFlag(lang))
                        {
                            if (_gamePackage.PreDownload.Major.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                            {
                                size += packageFile.Size;
                                unzipSize += packageFile.DecompressedSize;
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("PreDownloadMajor of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                    TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                }
            }
            else if (_gameSophonChunkBuild is not null)
            {
                GameSophonChunkBuild build = _gameSophonChunkBuild;
                GameSophonChunkBuild? localBuild = _localVersionSophonChunkBuild;
                // 清单可能有数十万个块，解析和比对放到后台线程
                (size, unzipSize) = await Task.Run(() => ComputeSophonChunkDownloadSizeAsync(build, localBuild));
            }
            else if (_gameSophonPatchBuild is not null)
            {
                List<GameSophonPatchManifest> manifests = GetAvaliableGameSophonPatchManifests(_gameSophonPatchBuild, _audioLanguage, _ignoreMatchingFields);
                foreach (GameSophonPatchManifest manifest in manifests)
                {
                    bool isGameOrAudio = manifest.MatchingField is "game" or "zh-cn" or "en-us" or "ja-jp" or "ko-kr";
                    if (isGameOrAudio)
                    {
                        if (manifest.Stats.TryGetValue(_localGameVersion, out var stats))
                        {
                            size += stats.CompressedSize;
                            unzipSize += stats.UncompressedSize;
                        }
                    }
                    else
                    {
                        SophonPatchManifest patchManifest = await GetSophonPatchManifestAsync(manifest);
                        List<SophonPatch> patches = new();
                        foreach (SophonPatchFile item in patchManifest.Patches)
                        {
                            if (item.Patches.FirstOrDefault(x => x.Tag == _localGameVersion) is SophonPatchInfo info)
                            {
                                if (string.IsNullOrWhiteSpace(info.Patch?.OriginalFileName))
                                {
                                    string path = Path.Join(_installationPath, item.File);
                                    if (File.Exists(path) && new FileInfo(path).Length == item.Size)
                                    {
                                        // 排除已存在的文件
                                        continue;
                                    }
                                }
                                if (info.Patch is not null)
                                {
                                    patches.Add(info.Patch);
                                }
                            }
                        }
                        long patchSize = patches.DistinctBy(x => x.Id).Sum(x => x.PatchFileSize);
                        size += patchSize;
                        unzipSize += patchSize;
                    }
                }
            }
            PackageSizeBytes = size;
            UnzipSpaceBytes = unzipSize;
            if (AvailableSpaceBytes > 0 && UnzipSpaceBytes > AvailableSpaceBytes)
            {
                TextBlock_AvailableSpace.Foreground = App.Current.Resources["SystemFillColorCautionBrush"] as Brush;
            }
            else
            {
                TextBlock_AvailableSpace.Foreground = App.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compute package size.");
            // 统计增量需要下载清单文件，失败时给出原因，而不是停在「...」
            ErrorMessage = GetMiHoYoRequestErrorMessage(ex);
        }
    }




    private void CheckCanPreDownload()
    {
        try
        {
            if (_gamePackage is not null || _gameSophonChunkBuild is not null || _gameSophonPatchBuild is not null)
            {
                if (Path.IsPathFullyQualified(_installationPath) && !string.IsNullOrWhiteSpace(_localGameVersion))
                {
                    if (PackageSizeBytes > 0 && UnzipSpaceBytes > 0)
                    {
                        Button_StartPredownload.IsEnabled = true;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check can start predownload.");
        }
        Button_StartPredownload.IsEnabled = false;
    }




    [RelayCommand]
    private async Task StartPredownloadAsync()
    {
        try
        {
            Telemetry.Track("predownload_dialog_click", CurrentGameId.GameBiz, ("button", "start"), ("audio", _audioLanguage));
            GameInstallContext? task = await _gameInstallService.StartPredownloadAsync(CurrentGameId, _installationPath, _audioLanguage);
            if (task is not null && task.State is not GameInstallState.Stop and not GameInstallState.Error)
            {
                _predownloadStarted = true;
                WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
                Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start predownload.");
        }
    }





    /// <summary>
    /// 预下载已发起；之后的关闭是程序自动关，不计入用户点击
    /// </summary>
    private bool _predownloadStarted;


    /// <summary>
    /// 关闭对话框
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        if (!_predownloadStarted)
        {
            Telemetry.Track("predownload_dialog_click", CurrentGameId?.GameBiz.ToString(), ("button", "close"));
        }
        this.Hide();
    }





    public static List<string> GetIgnoreMatchingFields(string installPath, GameConfig gameConfig)
    {
        List<string> ignoreMatchingFields = new List<string>();
        if (gameConfig is not null)
        {
            string file = Path.Join(installPath, gameConfig.ResCategoryDir);
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


    /// <summary>
    /// 按 RPC 预下载任务的规则统计 Chunk 模式要下载的大小：块优先在旧版本同路径文件中找，其次在新版本已移除的旧文件中找，
    /// 解压后 MD5 与大小都相同就直接复用，其余块按 Id 去重后累加
    /// （对应 RPC GamePackageService.PrepareSophonChunkFilesWithLocalVersionAsync 与 GameInstallHelper.GetPredownloadFiles）。
    /// </summary>
    /// <param name="build">预下载版本的清单</param>
    /// <param name="localBuild">本地版本的清单；为 <see langword="null"/> 时无法增量，按整包统计</param>
    /// <param name="cancellationToken"></param>
    /// <returns>下载大小（压缩后）与解压后大小，单位字节</returns>
    private async Task<(long Size, long UnzipSize)> ComputeSophonChunkDownloadSizeAsync(GameSophonChunkBuild build, GameSophonChunkBuild? localBuild, CancellationToken cancellationToken = default)
    {
        List<GameSophonChunkManifest> manifests = GetAvailableGameSophonChunkManifests(build, _audioLanguage, _ignoreMatchingFields);
        if (localBuild is null)
        {
            return (manifests.Sum(x => x.Stats.CompressedSize), manifests.Sum(x => x.Stats.UncompressedSize));
        }
        List<SophonChunkFile> files = new();
        // 与 RPC 一致：路径不区分大小写
        Dictionary<string, SophonChunkFile> localFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameSophonChunkManifest manifest in manifests)
        {
            files.AddRange((await GetSophonChunkFilesAsync(manifest, cancellationToken)).Where(x => !x.IsFolder));
            if (localBuild.Manifests.FirstOrDefault(x => x.MatchingField == manifest.MatchingField) is GameSophonChunkManifest localManifest)
            {
                foreach (SophonChunkFile item in await GetSophonChunkFilesAsync(localManifest, cancellationToken))
                {
                    if (!item.IsFolder)
                    {
                        localFiles.TryAdd(item.File, item);
                    }
                }
            }
        }
        HashSet<string> newFiles = files.Select(x => x.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<(string, long)> removedChunks = localFiles.Values
            .Where(x => !newFiles.Contains(x.File))
            .SelectMany(x => x.Chunks)
            .Select(x => (x.UncompressedMd5, x.UncompressedSize))
            .ToHashSet();
        long size = 0, unzipSize = 0;
        HashSet<string> chunkIds = new();
        foreach (SophonChunkFile file in files)
        {
            HashSet<(string, long)>? localChunks = localFiles.TryGetValue(file.File, out SophonChunkFile? localFile)
                ? localFile.Chunks.Select(x => (x.UncompressedMd5, x.UncompressedSize)).ToHashSet()
                : null;
            foreach (SophonChunk chunk in file.Chunks)
            {
                (string, long) key = (chunk.UncompressedMd5, chunk.UncompressedSize);
                if (localChunks?.Contains(key) is true || removedChunks.Contains(key))
                {
                    continue;
                }
                if (chunkIds.Add(chunk.Id))
                {
                    size += chunk.CompressedSize;
                    unzipSize += chunk.UncompressedSize;
                }
            }
        }
        return (size, unzipSize);
    }



    /// <summary>
    /// 下载并解析 Chunk 模式的文件清单。
    /// </summary>
    /// <param name="manifest"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<List<SophonChunkFile>> GetSophonChunkFilesAsync(GameSophonChunkManifest manifest, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await EnsureSophonManifestFileAsync(manifest.ManifestDownload, manifest.Manifest, cancellationToken);
        return SophonChunkManifest.Parser.ParseFrom(bytes).Chuncks.ToList();
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
            _logger.LogWarning("Manifest file ({Id}) checksum mismatch, re-downloading.", manifestFile.Id);
            fs.Dispose();
            File.Delete(file);
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



}
