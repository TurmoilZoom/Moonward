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



    /// <summary>
    /// 获取游戏各版本主程序的 MD5。
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <param name="cancellationToken"></param>
    /// <returns>各版本主程序 MD5；接口没有该游戏的条目时（如崩坏3）返回 <see langword="null"/></returns>
    public async Task<GameScanInfo?> GetGameScanInfoAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        string key = $"{nameof(GameScanInfo)}_{gameId.Id}";
        if (!_memoryCache.TryGetValue(key, out GameScanInfo? info))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            info = await _client.GetGameScanInfosAsync(LauncherId.FromGameId(gameId)!, lang, gameId, cancellationToken);
            // 各版本的 MD5 发布后不会变，缓存久一点；没有条目时不缓存，下次再查
            if (info is not null)
            {
                _memoryCache.Set(key, info, TimeSpan.FromMinutes(10));
            }
        }
        return info;
    }




    /// <summary>
    /// 获取游戏附带的 WPF 包（原神的千星沙箱）。
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <param name="cancellationToken"></param>
    /// <returns>WPF 包；该游戏没有时（目前除原神外都没有）返回 <see langword="null"/></returns>
    public async Task<WPFPackage?> GetWPFPackageAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        string launcherId = LauncherId.FromGameId(gameId)!;
        string key = $"{nameof(WPFPackageInfo)}_{launcherId}";
        // 按启动器缓存整张列表：大多数游戏没有 WPF 包，按游戏缓存的话查不到的游戏每次都要重新请求
        if (!_memoryCache.TryGetValue(key, out List<WPFPackageInfo>? list))
        {
            string lang = CultureInfo.CurrentUICulture.Name;
            list = await _client.GetWPFPackagesAsync(launcherId, lang, cancellationToken);
            _memoryCache.Set(key, list, TimeSpan.FromMinutes(1));
        }
        return list?.FirstOrDefault(x => x.GameId == gameId)?.WPFPackage;
    }




    /// <summary>
    /// 获取 Chunk 模式文件清单，取法与 RPC 安装任务一致，保证对话框统计的就是实际要下载的清单。
    /// </summary>
    /// <param name="gameBranch">游戏分支</param>
    /// <param name="gameBranchPackage">正式或预下载分支</param>
    /// <param name="tag">版本号，为 <see langword="null"/> 时取该分支的当前版本</param>
    /// <param name="cancellationToken"></param>
    /// <returns>文件清单；该分支没有这个版本或清单为空时返回 <see langword="null"/></returns>
    /// <exception cref="miHoYoApiException">除「不存在」以外的接口错误</exception>
    public async Task<GameSophonChunkBuild?> GetGameSophonChunkBuildAsync(GameBranch gameBranch, GameBranchPackage gameBranchPackage, string? tag = null, CancellationToken cancellationToken = default)
    {
        string key = $"{nameof(GameSophonChunkBuild)}_{gameBranchPackage.PackageId}_{gameBranchPackage.Branch}_{tag}";
        if (!_memoryCache.TryGetValue(key, out GameSophonChunkBuild? build))
        {
            try
            {
                build = await _client.GetGameSophonChunkBuildAsync(gameBranch, gameBranchPackage, tag ?? "", cancellationToken);
                if (IsEmptySophonChunkBuild(build) && tag is null && !string.IsNullOrWhiteSpace(gameBranchPackage.Tag))
                {
                    // 预下载窗口内新版本只能不带 tag 取到（原先带 tag 请求会 -202，崩坏3 每次预下载都卡在这里）；
                    // 窗口结束后 predownload 分支不带 tag 反而返回空清单，此时按分支自身版本号再取一次
                    build = await _client.GetGameSophonChunkBuildAsync(gameBranch, gameBranchPackage, gameBranchPackage.Tag, cancellationToken);
                }
            }
            catch (miHoYoApiException ex) when (ex.ReturnCode is -202)
            {
                // not found：该分支没有这个版本的清单（例如本地版本过旧），按无清单处理，与 RPC 一致
                _logger.LogWarning("Sophon chunk build of ({GameBiz}) branch {Branch} tag {Tag} not found.", gameBranch.GameId.GameBiz, gameBranchPackage.Branch, tag);
                return null;
            }
            if (IsEmptySophonChunkBuild(build))
            {
                _logger.LogWarning("Sophon chunk build of ({GameBiz}) branch {Branch} tag {Tag} is empty.", gameBranch.GameId.GameBiz, gameBranchPackage.Branch, tag);
                return null;
            }
            _memoryCache.Set(key, build, TimeSpan.FromMinutes(1));
        }
        return build;
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




    /// <summary>
    /// 获取增量补丁（ldiff）文件清单。
    /// </summary>
    /// <param name="gameBranch">游戏分支</param>
    /// <param name="gameBranchPackage">正式或预下载分支</param>
    /// <param name="cancellationToken"></param>
    /// <returns>补丁清单；没有补丁时返回 <see langword="null"/></returns>
    /// <exception cref="miHoYoApiException">除「不存在」以外的接口错误</exception>
    public async Task<GameSophonPatchBuild?> GetGameSophonPatchBuildAsync(GameBranch gameBranch, GameBranchPackage gameBranchPackage, CancellationToken cancellationToken = default)
    {
        if (!_memoryCache.TryGetValue($"{nameof(GameSophonPatchBuild)}_{gameBranchPackage.PackageId}_{gameBranchPackage.Branch}", out GameSophonPatchBuild? build))
        {
            try
            {
                build = await _client.GetGameSophonPatchBuildAsync(gameBranch, gameBranchPackage, cancellationToken);
            }
            catch (miHoYoApiException ex) when (ex.ReturnCode is -202)
            {
                _logger.LogWarning("Sophon patch build of ({GameBiz}) branch {Branch} not found.", gameBranch.GameId.GameBiz, gameBranchPackage.Branch);
                return null;
            }
            // 没有补丁时接口返回 retcode 0 的空 build_id，按无补丁处理以便回退 Chunk 模式，与 RPC 一致
            if (string.IsNullOrWhiteSpace(build?.BuildId))
            {
                return null;
            }
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
