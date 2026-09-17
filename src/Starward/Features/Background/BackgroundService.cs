using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Starward.Core.HoYoPlay;
using Starward.Features.Codec;
using Starward.Features.HoYoPlay;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Storage;

namespace Starward.Features.Background;

public partial class BackgroundService
{

    private readonly ILogger<BackgroundService> _logger;

    private readonly HoYoPlayService _hoYoPlayService;

    private readonly HttpClient _httpClient;



    public BackgroundService(ILogger<BackgroundService> logger, HoYoPlayService hoYoPlayService, HttpClient httpClient)
    {
        _logger = logger;
        _hoYoPlayService = hoYoPlayService;
        _httpClient = httpClient;
    }



    /// <summary>
    /// 获取背景图文件路径，保存在 CacheFolder\bg
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    [return: NotNullIfNotNull(nameof(name))]
    public static string? GetBgFilePath(string? name)
    {
        return Path.Join(AppConfig.CacheFolder, "bg", name);
    }


    /// <summary>
    /// 背景文件是否存在，规则同 <see cref="ResolveBackgroundFile"/>：同内容、不同编号的文件，以及只剩转码产物的 webm 都算存在。
    /// </summary>
    /// <param name="path">背景文件完整路径。</param>
    /// <returns>能找到可用的背景文件时返回 true。</returns>
    public static bool BackgroundFileExists([NotNullWhen(true)] string? path)
    {
        return ResolveBackgroundFile(path) is not null;
    }


    /// <summary>
    /// 找到背景文件实际可用的路径。设置里记录、下载链接拼出的始终是本区服的文件名，实际文件可能换了名字：
    /// <list type="bullet">
    /// <item>官方 CDN 的文件名是「内容 MD5_编号.扩展名」，各区服的同一张背景只是编号不同。被图库「删除重复文件」删掉其中几份后，
    /// 用剩下的那份，不必重新下载。</item>
    /// <item>解不动的 webm 转码成 H.264 后原片会被删掉，只剩「原名 + .mp4」的产物；返回的仍是原片路径，播放时由 <see cref="VideoTranscodeService"/> 换成产物。</item>
    /// </list>
    /// </summary>
    /// <param name="path">背景文件完整路径。</param>
    /// <returns>本文件或同内容文件的路径（webm 可能只剩转码产物）；都找不到时返回 null。</returns>
    public static string? ResolveBackgroundFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        if (IsBackgroundFileAvailable(path))
        {
            return path;
        }
        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !TryParseContentAddressedFileName(Path.GetFileName(path), out string? md5, out string? extension))
        {
            return null;
        }
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, $"{md5}_*"))
            {
                string candidate = file;
                if (extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) && candidate.EndsWith(".webm.mp4", StringComparison.OrdinalIgnoreCase))
                {
                    // 只剩转码产物：还原成原片路径，交给转码服务映射
                    candidate = candidate[..^".mp4".Length];
                }
                // 通配符只按前缀筛，扩展名与编号格式要再核对一遍
                if (TryParseContentAddressedFileName(Path.GetFileName(candidate), out string? otherMd5, out string? otherExtension)
                    && otherMd5.Equals(md5, StringComparison.OrdinalIgnoreCase)
                    && otherExtension.Equals(extension, StringComparison.OrdinalIgnoreCase)
                    && IsBackgroundFileAvailable(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目录不存在或读不了，当作没有
        }
        return null;
    }


    /// <summary>文件本身存在，或 webm 原片已删但有可用的转码产物。</summary>
    private static bool IsBackgroundFileAvailable(string path)
    {
        return File.Exists(path) || VideoTranscodeService.TryGetTranscodedFile(path, out _);
    }


    /// <summary>
    /// 解析官方 CDN 的背景文件名「内容 MD5_编号.扩展名」。MD5 相同的文件内容相同，可以互相替代。
    /// </summary>
    /// <param name="fileName">文件名（不含目录）。</param>
    /// <param name="md5">32 位十六进制的内容 MD5。</param>
    /// <param name="extension">扩展名，含点。</param>
    /// <returns>符合该格式时返回 true。</returns>
    internal static bool TryParseContentAddressedFileName(string? fileName, [NotNullWhen(true)] out string? md5, [NotNullWhen(true)] out string? extension)
    {
        Match match = ContentAddressedFileNameRegex().Match(fileName ?? string.Empty);
        md5 = match.Success ? match.Groups["md5"].Value : null;
        extension = match.Success ? match.Groups["ext"].Value : null;
        return match.Success;
    }


    /// <summary>官方 CDN 背景文件名：32 位小写十六进制的内容 MD5 + 下划线 + 数字编号 + 扩展名。</summary>
    [GeneratedRegex(@"^(?<md5>[0-9a-f]{32})_\d+(?<ext>\.[0-9A-Za-z]+)$")]
    private static partial Regex ContentAddressedFileNameRegex();


    /// <summary>
    /// 获取自定义背景图文件路径（按设置里的文件名拼出，未必是实际文件，打开前用 <see cref="ResolveBackgroundFile"/>）
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool TryGetCustomBgFilePath(GameId gameId, [NotNullWhen(true)] out string? path)
    {
        path = null;
        if (gameId is null)
        {
            return false;
        }
        if (AppConfig.GetEnableCustomBg(gameId.GameBiz))
        {
            path = GetBgFilePath(AppConfig.GetCustomBg(gameId.GameBiz));
            if (BackgroundFileExists(path))
            {
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// 文件是否是支持的视频格式，支持 mp4、mkv、flv、webm
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static bool FileIsSupportedVideo(string? name)
    {
        return Path.GetExtension(name) switch
        {
            ".mp4" or ".mkv" or ".webm" => true,
            _ => false,
        };
    }


    /// <summary>
    /// 获取已缓存的背景图文件路径
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetCachedBackgroundFile(GameId gameId)
    {
        if (gameId is null)
        {
            return null;
        }

        string? lastBg = AppConfig.GetBg(gameId.GameBiz);
        string? customBg = AppConfig.GetCustomBg(gameId.GameBiz);
        if (!(lastBg == customBg && !AppConfig.GetEnableCustomBg(gameId.GameBiz)))
        {
            if (ResolveBackgroundFile(GetBgFilePath(lastBg)) is string path)
            {
                return path;
            }
        }
        // 回退到自定义背景
        if (TryGetCustomBgFilePath(gameId, out string? custom))
        {
            return ResolveBackgroundFile(custom);
        }
        return null;
    }



    /// <summary>
    /// 背景图和版本海报链接
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<List<GameBackground>> GetGameBackgroundsAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        GameBackgroundInfo backgroundInfo = await _hoYoPlayService.GetGameBackgroundAsync(gameId, cancellationToken);
        List<GameBackground> backgrounds = backgroundInfo?.Backgrounds?.ToList() ?? [];
        GameInfo gameInfo = await _hoYoPlayService.GetGameInfoAsync(gameId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(gameInfo?.Display?.Background?.Url))
        {
            backgrounds.Add(GameBackground.FromPosterUrl(gameInfo.Display.Background.Url));
        }
        if (TryGetCustomBgFilePath(gameId, out string? path))
        {
            backgrounds.Add(GameBackground.FromCustomFile(path));
        }
        return backgrounds;
    }



    public async Task<GameBackground?> GetSuggestedGameBackgroundAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        string? lastBg = AppConfig.GetBg(gameId.GameBiz);
        string? customBg = AppConfig.GetCustomBg(gameId.GameBiz);
        // 上次使用的是自定义背景（或刚启用自定义背景尚未记录），直接返回，无需联网。
        if (TryGetCustomBgFilePath(gameId, out string? file) && (string.IsNullOrWhiteSpace(lastBg) || lastBg == customBg))
        {
            return GameBackground.FromCustomFile(file);
        }
        List<GameBackground> backgrounds = await GetGameBackgroundsAsync(gameId, cancellationToken);
        GameBackground? bg = null;
        string? lastBgIds = AppConfig.GetGameBackgroundIds(gameId.GameBiz);
        string firstBgId = backgrounds.FirstOrDefault()?.Id ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(lastBg) && (lastBgIds?.StartsWith(firstBgId) ?? false))
        {
            // 没有新背景
            if (backgrounds.FirstOrDefault(x => Path.GetFileName(x.Background.Url) == lastBg) is GameBackground bg1)
            {
                // 匹配到静态图文件名：旧逻辑下表示用户曾暂停官方视频（或仅使用静态图）。
                bg1.StopVideo = true;
                bg = bg1;
            }
            else if (backgrounds.Where(x => x.Video != null).FirstOrDefault(x => Path.GetFileName(x.Video.Url) == lastBg) is GameBackground bg2)
            {
                bg = bg2;
            }
        }
        else if (AppConfig.GetUseVersionPoster(gameId.GameBiz))
        {
            // 背景列表已更新：上次使用的是官方版本海报，则继续使用海报（默认第一张），
            // 与是否开启自定义背景无关——只要上次显示的是海报就保持海报。
            bg = backgrounds.FirstOrDefault(x => x.Type is GameBackground.BACKGROUND_TYPE_POSTER);
        }
        bg ??= backgrounds.FirstOrDefault();
        // 上次官方视频为暂停时，本次若落到官方视频背景（含列表更新后的新视频），保持暂停。
        // 用「或」合并：同列表下靠静态图文件名匹配已置 true 的路径不受影响；显式偏好覆盖「有新背景」等未匹配场景。
        if (bg?.Type is GameBackground.BACKGROUND_TYPE_VIDEO && AppConfig.GetStopOfficialVideo(gameId.GameBiz))
        {
            bg.StopVideo = true;
        }
        return bg;
    }



    /// <summary>
    /// 获取背景文件，本地没有才下载到 CacheFolder\bg。
    /// </summary>
    /// <param name="url">背景文件链接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 实际可用的文件路径。其他区服下载过同内容的文件、或 webm 原片已转码时，返回的文件名与链接里的不同；
    /// 要记入设置的文件名请用 <c>Path.GetFileName(url)</c>。
    /// </returns>
    public async Task<string> GetBackgroundFileAsync(string url, CancellationToken cancellationToken = default)
    {
        string name = Path.GetFileName(url);
        string file = GetBgFilePath(name);
        // 同内容的文件已在本地（其他区服下过、去重后只剩一份、原片已转码）就直接用，否则每次切换区服都要重下一遍
        if (ResolveBackgroundFile(file) is string existing)
        {
            return existing;
        }
        var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, bytes, cancellationToken);
        return file;
    }




    /// <summary>
    /// 获取默认的背景图文件路径
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetFallbackBackgroundImage(GameId gameId)
    {
        string? bg = ResolveBackgroundFile(GetBgFilePath(AppConfig.GetBg(gameId.GameBiz)));
        if (bg is null)
        {
            string baseFolder = AppContext.BaseDirectory;
            string path = Path.Combine(baseFolder, @"Assets\Image\UI_CutScene_1130320101A.png");
            bg = File.Exists(path) ? path : null;
        }
        return bg;
    }



    /// <summary>
    /// 更改自定义背景图文件，保存在 UserData\bg，返回文件名
    /// </summary>
    /// <param name="xamlRoot"></param>
    /// <returns></returns>
    public async Task<string?> ChangeCustomBackgroundFileAsync(XamlRoot xamlRoot)
    {
        string? file = await PickBackgroundFileAsync(xamlRoot);
        if (file is null)
        {
            return null;
        }
        await CheckBackgroundFileAvailableAsync(file);
        string bg = Path.Join(AppConfig.CacheFolder, "bg");
        string name = Path.GetFileName(file);
        string path = Path.Combine(bg, name);
        if (path != file)
        {
            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
            name = await ImportBackgroundFileAsync(name, fs);
        }
        return name;
    }



    /// <summary>
    /// 更改自定义背景图文件，保存在 UserData\bg，返回文件名
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    public static async Task<string?> ChangeCustomBackgroundFileAsync(StorageFile file)
    {
        string bg = Path.Join(AppConfig.CacheFolder, "bg");
        string name = file.Name;
        if (Path.GetDirectoryName(file.Path) != bg)
        {
            if (FileIsSupportedVideo(file.Name))
            {
                using var source = MediaSource.CreateFromStorageFile(file);
                await source.OpenAsync();
            }
            else
            {
                using var fs = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(fs);
            }
            {
                using var stream = await file.OpenReadAsync();
                name = await ImportBackgroundFileAsync(file.Name, stream.AsStream());
            }
        }
        return name;
    }


    /// <summary>
    /// 把导入的背景文件复制进 bg 目录，返回实际保存的文件名。从不覆盖 bg 里已有的文件（它可能正被别的游戏区服用作背景）：
    /// 已有同名文件且内容完全相同就直接复用；内容不同则依次另存为「名称 (1).扩展名」「名称 (2).扩展名」……
    /// </summary>
    /// <param name="name">导入文件的原始文件名。</param>
    /// <param name="source">导入文件的只读流，须支持 Seek 与 Length；不会被释放。</param>
    /// <returns>保存在 bg 目录中的文件名。</returns>
    /// <exception cref="IOException">复制失败（含这期间恰好出现同名文件），写了一半的文件已删除。</exception>
    private static async Task<string> ImportBackgroundFileAsync(string name, Stream source)
    {
        string bg = Path.Join(AppConfig.CacheFolder, "bg");
        Directory.CreateDirectory(bg);
        string baseName = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int i = 0; ; i++)
        {
            string candidate = i == 0 ? name : $"{baseName} ({i}){extension}";
            string path = Path.Combine(bg, candidate);
            if (File.Exists(path))
            {
                if (await IsSameContentAsync(source, path))
                {
                    return candidate;
                }
                continue;
            }
            if (Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase) && File.Exists(VideoTranscodeService.GetTranscodedFilePath(path)))
            {
                // 原片转码后已删、只剩有损的转码产物，比不了内容，当作名字已被占用
                continue;
            }
            source.Position = 0;
            // CreateNew：万一这期间有同名文件出现，宁可报错也不覆盖
            FileStream dest = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
            try
            {
                await using (dest)
                {
                    await source.CopyToAsync(dest);
                }
            }
            catch
            {
                // 别在图库里留下写了一半的文件
                try { File.Delete(path); } catch { }
                throw;
            }
            return candidate;
        }
    }


    /// <summary>
    /// 导入的文件与 bg 里已有文件的内容是否完全相同。
    /// </summary>
    /// <param name="source">导入文件的流，须支持 Seek 与 Length；比较前会回到开头。</param>
    /// <param name="file">bg 里已有文件的完整路径。</param>
    /// <returns>长度与每个字节都相同时返回 true。</returns>
    private static async Task<bool> IsSameContentAsync(Stream source, string file)
    {
        // 已有文件可能正被背景播放读着，放开共享以免打不开
        await using var existing = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length != existing.Length)
        {
            return false;
        }
        source.Position = 0;
        const int BufferSize = 1 << 16;
        byte[] sourceBuffer = new byte[BufferSize];
        byte[] existingBuffer = new byte[BufferSize];
        while (true)
        {
            int sourceRead = await source.ReadAtLeastAsync(sourceBuffer, BufferSize, throwOnEndOfStream: false);
            int existingRead = await existing.ReadAtLeastAsync(existingBuffer, BufferSize, throwOnEndOfStream: false);
            if (sourceRead != existingRead || !sourceBuffer.AsSpan(0, sourceRead).SequenceEqual(existingBuffer.AsSpan(0, existingRead)))
            {
                return false;
            }
            if (sourceRead < BufferSize)
            {
                return true;
            }
        }
    }


    /// <summary>
    /// 选择背景图文件
    /// </summary>
    /// <param name="xamlRoot"></param>
    /// <returns></returns>
    private async Task<string?> PickBackgroundFileAsync(XamlRoot xamlRoot)
    {
        var filter = new (string, string)[]
            {
                ("Image", ".bmp"),
                ("Image", ".jpg"),
                ("Image", ".png"),
                ("Image", ".webp"),
                ("Image", ".avif"),
                ("Image", ".jxl"),
                ("Video", ".mp4"),
                ("Video", ".mkv"),
                ("Video", ".webm"),
            };
        return await FileDialogHelper.PickSingleFileAsync(xamlRoot, filter);
    }



    /// <summary>
    /// 检查背景图文件是否可用
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    private static async Task CheckBackgroundFileAvailableAsync(string file)
    {
        if (FileIsSupportedVideo(file))
        {
            // 0xC00D36C4
            using var source = MediaSource.CreateFromUri(new Uri(file));
            await source.OpenAsync();
        }
        else
        {
            // 0x88982F8B
            using var fs = File.OpenRead(file);
            var decoder = await BitmapDecoder.CreateAsync(fs.AsRandomAccessStream());
        }
    }


}
