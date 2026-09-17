using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace Starward.Features.HoYoPlay;

public class HoYoPlayService
{


    private readonly ILogger<HoYoPlayService> _logger;

    private readonly HoYoPlayClient _client;

    private readonly HttpClient _httpClient;

    private readonly IMemoryCache _memoryCache;


    public HoYoPlayService(ILogger<HoYoPlayService> logger, HoYoPlayClient client, HttpClient httpClient, IMemoryCache memoryCache)
    {
        _logger = logger;
        _client = client;
        _httpClient = httpClient;
        _memoryCache = memoryCache;
    }





    public async Task<GameInfo> GetGameInfoAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameInfo)}_{gameId.Id}", out GameInfo? info))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            var list = await _client.GetGameInfoAsync(LauncherId.FromGameId(gameId)!, lang, cancellationToken);
            foreach (var item in list)
            {
                _memoryCache.Set($"{nameof(GameInfo)}_{item.Id}", item, TimeSpan.FromMinutes(10));
            }
            info = list.FirstOrDefault(x => x == gameId);
        }
        return info!;
    }



    public async Task<List<GameInfo>> UpdateGameInfoListAsync(CancellationToken cancellationToken = default)
    {
        if (_memoryCache is MemoryCache cache)
        {
            cache.Clear();
        }
        List<GameInfo> infos = new List<GameInfo>();
        string lang = CultureInfo.CurrentUICulture.Name;
        if (LanguageUtil.FilterLanguage(lang) is "zh-cn")
        {
            infos.AddRange(await _client.GetGameInfoAsync(LauncherId.ChinaOfficial, lang, cancellationToken));
            infos.AddRange(await _client.GetGameInfoAsync(LauncherId.GlobalOfficial, lang, cancellationToken));
        }
        else
        {
            infos.AddRange(await _client.GetGameInfoAsync(LauncherId.GlobalOfficial, lang, cancellationToken));
            infos.AddRange(await _client.GetGameInfoAsync(LauncherId.ChinaOfficial, lang, cancellationToken));
        }
        foreach ((GameBiz _, string launcherId) in LauncherId.GetBilibiliLaunchers())
        {
            infos.AddRange(await _client.GetGameInfoAsync(launcherId, lang, cancellationToken));
        }
        foreach (var item in infos)
        {
            _memoryCache.Set($"{nameof(GameInfo)}_{item.Id}", item, TimeSpan.FromMinutes(10));
        }
        string json = JsonSerializer.Serialize(infos);
        AppConfig.CachedGameInfo = json;
        _ = DownloadGameVersionPosterAsync(infos);
        return infos;
    }



    private async Task DownloadGameVersionPosterAsync(List<GameInfo> infos)
    {
        try
        {
            if (AppConfig.CacheFolder is not null)
            {
                string bg = Path.Combine(AppConfig.CacheFolder, "bg");
                Directory.CreateDirectory(bg);
                // 各区服海报内容相同、文件名编号不同：按内容分组，每组只下载一份。
                // 逐个区服并发的话，新版本或新装时本地一份都没有，几个区服会同时判断为缺失、各下一份。
                var groups = infos.Select(x => (x.GameBiz, Url: x.Display.Background?.Url ?? ""))
                                  .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                                  .GroupBy(x => GetPosterContentKey(x.Url), StringComparer.OrdinalIgnoreCase);
                await Parallel.ForEachAsync(groups, async (group, _) =>
                {
                    // 组内按接口返回的顺序（界面语言对应的区服在前）尝试，一个链接下载失败就换同组其他区服的链接
                    foreach ((GameBiz _, string url) in group)
                    {
                        string path = Path.Combine(bg, Path.GetFileName(url));
                        // 同内容的已下载过（含去重后留下的那份、首页背景刚下好的）就不再下载
                        if (BackgroundService.BackgroundFileExists(path))
                        {
                            break;
                        }
                        try
                        {
                            byte[] bytes = await _httpClient.GetByteArrayAsync(url);
                            await BackgroundService.WriteBackgroundFileAsync(path, bytes);
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Download image: {url}", url);
                        }
                    }
                    foreach ((GameBiz gameBiz, string url) in group)
                    {
                        try
                        {
                            // 记本区服链接里的文件名，与原来一致；实际文件可能是同组其他区服的
                            string name = Path.GetFileName(url);
                            if (BackgroundService.BackgroundFileExists(Path.Combine(bg, name)))
                            {
                                AppConfig.SetVersionPoster(gameBiz, name);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Set version poster: {url}", url);
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, nameof(DownloadGameVersionPosterAsync));
        }
    }


    /// <summary>
    /// 海报分组用的键：官方命名「内容 MD5_编号.扩展名」取 MD5 与扩展名，同内容的各区服海报键相同；其他名字按文件名本身。
    /// </summary>
    /// <param name="url">海报链接。</param>
    /// <returns>分组键。</returns>
    private static string GetPosterContentKey(string url)
    {
        string name = Path.GetFileName(url);
        return BackgroundService.TryParseContentAddressedFileName(name, out string? md5, out string? extension)
            ? $"md5:{md5}{extension}"
            : $"name:{name}";
    }



    public async Task<GameBackgroundInfo> GetGameBackgroundAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameBackgroundInfo)}_{gameId.Id}", out GameBackgroundInfo? background))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            var list = await _client.GetGameBackgroundAsync(LauncherId.FromGameId(gameId)!, lang, cancellationToken);
            foreach (var item in list)
            {
                _memoryCache.Set($"{nameof(GameBackgroundInfo)}_{item.GameId.Id}", item, TimeSpan.FromMinutes(1));
            }
            background = list.FirstOrDefault(x => x.GameId == gameId);
        }
        return background!;
    }



    public async Task<GameContent> GetGameContentAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameContent)}_{gameId.Id}", out GameContent? content))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            content = await _client.GetGameContentAsync(LauncherId.FromGameId(gameId)!, lang, gameId, cancellationToken);
            _memoryCache.Set($"{nameof(GameContent)}_{content.GameId.Id}", content, TimeSpan.FromMinutes(1));
        }
        return content!;
    }



    /// <summary>
    /// 游戏安装包信息
    /// </summary>
    /// <returns>尚未发布安装包的游戏（如已在启动器中展示但未上线的新游戏）不在接口返回中，此时为 null</returns>
    public async Task<GamePackage?> GetGamePackageAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GamePackage)}_{gameId.Id}", out GamePackage? package))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            var list = await _client.GetGamePackageAsync(LauncherId.FromGameId(gameId)!, lang, cancellationToken);
            foreach (var item in list)
            {
                _memoryCache.Set($"{nameof(GamePackage)}_{item.GameId.Id}", item, TimeSpan.FromMinutes(1));
            }
            package = list.FirstOrDefault(x => x.GameId == gameId);
        }
        return package;
    }



    public async Task<GameConfig?> GetGameConfigAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameConfig)}_{gameId.Id}", out GameConfig? config))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            var list = await _client.GetGameConfigAsync(LauncherId.FromGameId(gameId)!, lang, cancellationToken);
            foreach (var item in list)
            {
                _memoryCache.Set($"{nameof(GameConfig)}_{item.GameId.Id}", item, TimeSpan.FromMinutes(1));
            }
            config = list.FirstOrDefault(x => x.GameId == gameId);
        }
        // 仅星穹铁道强制使用 Chunk 作为默认下载模式
        if (config is not null && config.GameId.GameBiz.Value is GameBiz.hkrpg)
        {
            config.DefaultDownloadMode = DownloadMode.DOWNLOAD_MODE_CHUNK;
        }
        return config;
    }



    public async Task<List<GameDeprecatedFile>> GetGameDeprecatedFilesAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        var launcherId = LauncherId.FromGameId(gameId);
        if (launcherId is not null)
        {
            var fileConfig = await _client.GetGameDeprecatedFileConfigAsync(launcherId, "en-us", gameId, cancellationToken);
            if (fileConfig != null)
            {
                return fileConfig.DeprecatedFiles;
            }
        }
        return [];
    }



    public async Task<GameChannelSDK?> GetGameChannelSDKAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameChannelSDK)}_{gameId.Id}", out GameChannelSDK? sdk))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            var list = await _client.GetGameChannelSDKAsync(LauncherId.FromGameId(gameId)!, lang, cancellationToken);
            foreach (var item in list)
            {
                _memoryCache.Set($"{nameof(GameChannelSDK)}_{item.GameId.Id}", item, TimeSpan.FromMinutes(1));
            }
            sdk = list.FirstOrDefault(x => x.GameId == gameId);
        }
        return sdk;
    }



    public async Task<GameBranch?> GetGameBranchAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameBranch)}_{gameId.Id}", out GameBranch? branch))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            var list = await _client.GetGameBranchAsync(LauncherId.FromGameId(gameId)!, lang, cancellationToken);
            foreach (var item in list)
            {
                _memoryCache.Set($"{nameof(GameBranch)}_{item.GameId.Id}", item, TimeSpan.FromMinutes(1));
            }
            branch = list.FirstOrDefault(x => x.GameId == gameId);
        }
        return branch;
    }




    public async Task<GameSophonChunkBuild?> GetGameSophonChunkBuildAsync(GameBranch gameBranch, GameBranchPackage gameBranchPackage, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameSophonChunkBuild)}_{gameBranchPackage.PackageId}_{gameBranchPackage.Branch}", out GameSophonChunkBuild? build))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            build = await _client.GetGameSophonChunkBuildAsync(gameBranch, gameBranchPackage, gameBranchPackage.Tag, cancellationToken);
            _memoryCache.Set($"{nameof(GameSophonChunkBuild)}_{gameBranchPackage.PackageId}_{gameBranchPackage.Branch}", build, TimeSpan.FromMinutes(1));
        }
        return build;
    }




    public async Task<GameSophonPatchBuild?> GetGameSophonPatchBuildAsync(GameBranch gameBranch, GameBranchPackage gameBranchPackage, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameSophonPatchBuild)}_{gameBranchPackage.PackageId}_{gameBranchPackage.Branch}", out GameSophonPatchBuild? build))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            build = await _client.GetGameSophonPatchBuildAsync(gameBranch, gameBranchPackage, cancellationToken);
            _memoryCache.Set($"{nameof(GameSophonPatchBuild)}_{gameBranchPackage.PackageId}_{gameBranchPackage.Branch}", build, TimeSpan.FromMinutes(1));
        }
        return build;
    }



    public async Task<List<GameDXConfig>> GetGameDXConfigsAsync(IEnumerable<GameId> gameIds, CancellationToken cancellationToken = default)
    {
        string key = $"{nameof(GameDXConfig)}_{string.Join(',', gameIds.Select(x => x.Id))}";
        if (!_memoryCache.TryGetValue(key, out List<GameDXConfig>? dxConfigs))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            var launcherId = LauncherId.FromGameId(gameIds.First())!;
            var gpuInfos = GetGPUInfos();
            dxConfigs = await _client.GetDXConfigsAsync(launcherId, lang, gameIds, gpuInfos, cancellationToken);
            _memoryCache.Set(key, dxConfigs, TimeSpan.FromMinutes(5));
        }
        return dxConfigs!;
    }


    private static List<GPUInfo> GetGPUInfos()
    {
        var gpuInfos = new List<GPUInfo>();
        try
        {
            using SetupAPI.SafeHDEVINFO devInfo = SetupAPI.SetupDiGetClassDevs(SetupAPI.GUID_DEVCLASS_DISPLAY, null, HWND.NULL, SetupAPI.DIGCF.DIGCF_PRESENT);
            if (!devInfo.IsInvalid)
            {
                foreach (SetupAPI.SP_DEVINFO_DATA devInfoData in SetupAPI.SetupDiEnumDeviceInfo(devInfo))
                {
                    string? name = GetDeviceName(devInfo, devInfoData);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = GetDeviceProperty(devInfo, devInfoData, SetupAPI.DEVPKEY_Device_FriendlyName);
                    }
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }
                    string? version = GetDeviceProperty(devInfo, devInfoData, SetupAPI.DEVPKEY_Device_DriverVersion);
                    gpuInfos.Add(new GPUInfo
                    {
                        Name = name,
                        DriverVersion = version ?? "",
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        return gpuInfos;
    }


    private static unsafe string? GetDeviceName(SetupAPI.SafeHDEVINFO devInfo, SetupAPI.SP_DEVINFO_DATA devInfoData)
    {
        string? value = null;
        SetupAPI.SetupDiGetDeviceRegistryProperty(devInfo, devInfoData, SetupAPI.SPDRP.SPDRP_DEVICEDESC, out _, nint.Zero, 0, out uint requiredSize);
        if (requiredSize > 0)
        {
            void* buffer = NativeMemory.Alloc(requiredSize);
            if (SetupAPI.SetupDiGetDeviceRegistryProperty(devInfo, devInfoData, SetupAPI.SPDRP.SPDRP_DEVICEDESC, out _, (nint)buffer, requiredSize, out requiredSize))
            {
                value = Encoding.Unicode.GetString(new ReadOnlySpan<byte>(buffer, (int)requiredSize)).TrimEnd('\0');
            }
            NativeMemory.Free(buffer);
        }
        return value;
    }


    private static unsafe string? GetDeviceProperty(SetupAPI.SafeHDEVINFO devInfo, SetupAPI.SP_DEVINFO_DATA devInfoData, SetupAPI.DEVPROPKEY propKey)
    {
        string? value = null;
        SetupAPI.SetupDiGetDeviceProperty(devInfo, devInfoData, propKey, out _, nint.Zero, 0, out uint requiredSize);
        if (requiredSize > 0)
        {
            void* buffer = NativeMemory.Alloc(requiredSize);
            if (SetupAPI.SetupDiGetDeviceProperty(devInfo, devInfoData, propKey, out _, (nint)buffer, requiredSize, out requiredSize))
            {
                value = Encoding.Unicode.GetString(new ReadOnlySpan<byte>(buffer, (int)requiredSize)).TrimEnd('\0');
            }
            NativeMemory.Free(buffer);
        }
        return value;
    }


}
