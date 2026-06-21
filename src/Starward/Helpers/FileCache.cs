using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Helpers;

/// <summary>
/// 远程文件（主要是图片资源）的本地磁盘缓存管理器。
/// 特性：
/// <list type="bullet">
/// <item>按 URI 计算稳定文件名（XxHash64 + MD5 混合哈希）。</item>
/// <item>并发去重：相同 URI 的多次请求会共享同一个下载 Task，避免重复下载。</item>
/// <item>支持断点续传（HTTP Range + _tmp 临时文件 + 原子 Move）。</item>
/// <item>基于文件最后写入时间 + <see cref="CacheDuration"/> 判断缓存是否有效（默认 90 天）。</item>
/// <item>可配置重试次数，默认 3 次。</item>
/// </list>
/// 主要由 <see cref="CachedImage"/> 使用，也可直接调用获取任意远程资源的本地缓存路径。
/// </summary>
internal static class FileCache
{

    /// <summary>
    /// 共享的 HttpClient。
    /// 配置了自动解压、HTTP/2+3 多连接、连接池生命周期 5 分钟等优化。
    /// </summary>
    private static readonly HttpClient _httpClient;

    /// <summary>
    /// 按缓存文件名（哈希结果）去重的并发下载任务字典。
    /// Key 为 <see cref="GetCacheFileName(Uri)"/> 的结果，Value 为正在进行的下载 Task。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task<string?>> _concurrentTasks;


    static FileCache()
    {
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        });
        _httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        _concurrentTasks = new();
    }


    /// <summary>下载失败时的最大重试次数（默认 3）。</summary>
    public static int RetryCount { get; set; } = 3;

    /// <summary>缓存有效期（默认 90 天）。超过此时间或文件为空则视为失效，会重新下载。</summary>
    public static TimeSpan CacheDuration { get; set; } = TimeSpan.FromDays(90);

    /// <summary>缓存根目录。必须先调用 <see cref="Initialize(string)"/> 才能使用。</summary>
    public static string CacheFolder { get; private set; }


    /// <summary>
    /// 初始化缓存目录。
    /// 应用启动时由 <see cref="AppConfig"/> 调用一次。
    /// </summary>
    /// <param name="folder">缓存根目录路径（通常是 CacheFolder/cache）。</param>
    /// <returns>初始化是否成功。</returns>
    public static bool Initialize(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            CacheFolder = folder;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing FileCache: {ex.Message}");
        }
        return false;
    }


    /// <summary>
    /// 获取远程 URI 对应资源的本地缓存文件路径（会自动下载并缓存）。
    /// </summary>
    /// <param name="uri">远程资源地址。</param>
    /// <param name="throwOnError">下载失败时是否抛出异常（默认 false，返回 null）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本地缓存文件完整路径；失败时返回 null（除非 throwOnError）。</returns>
    public static async Task<string?> GetFromCacheAsync(Uri uri, bool throwOnError = false, CancellationToken cancellationToken = default)
    {
        return await GetItemAsync(uri, throwOnError, cancellationToken);
    }


    /// <summary>
    /// 内部入口：实现并发去重 + 错误处理 + 清理字典。
    /// </summary>
    private static async Task<string?> GetItemAsync(Uri uri, bool throwOnError, CancellationToken cancellationToken)
    {
        string fileName = GetCacheFileName(uri);
        if (_concurrentTasks.TryGetValue(fileName, out var request))
        {
            return await request.ConfigureAwait(false);
        }

        request = GetFromCacheOrDownloadAsync(uri, fileName, cancellationToken);
        _concurrentTasks.TryAdd(fileName, request);

        try
        {
            return await request.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error retrieving file from cache: {ex.Message}");
            if (throwOnError)
            {
                throw;
            }
        }
        finally
        {
            _concurrentTasks.TryRemove(fileName, out _);
        }

        return null;
    }


    /// <summary>
    /// 检查本地缓存是否可用；不可用则下载（带重试）。
    /// </summary>
    private static async Task<string?> GetFromCacheOrDownloadAsync(Uri uri, string fileName, CancellationToken cancellationToken)
    {
        if (CacheFolder is null)
        {
            throw new DirectoryNotFoundException("Cache folder not initialized.");
        }

        string filePath = Path.Combine(CacheFolder, fileName);

        // 缓存命中检查放到线程池执行：避免在 UI 线程上做文件元数据 IO，同时去掉原先固定的 1ms 延迟
        // （该延迟会让每张图片——即使命中缓存——都至少多等一个计时器周期）。
        // 这个 await 也会立即让出控制权，使 GetItemAsync 能先把本任务登记进并发去重字典，再继续后续下载。
        if (await Task.Run(() => IsFileCacheAvailable(filePath, CacheDuration), CancellationToken.None).ConfigureAwait(false))
        {
            return filePath;
        }

        uint retries = 0;
        while (retries < RetryCount)
        {
            try
            {
                await DownloadFileAsync(uri, filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
            retries++;
        }

        return filePath;
    }


    /// <summary>
    /// 核心下载实现：支持断点续传。
    /// 使用 &lt;path&gt;_tmp 作为临时文件，先写入再原子 Move，避免损坏缓存。
    /// 通过 Range Header 实现续传（服务器支持时）。
    /// </summary>
    private static async Task DownloadFileAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        string path_tmp = path + "_tmp";
        using var fs = File.Open(path_tmp, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(fs.Length, null);
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentRange?.From > 0)
        {
            fs.Position = response.Content.Headers.ContentRange.From.Value;
        }

        using var hs = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await hs.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
        await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
        fs.Dispose();

        File.Move(path_tmp, path, true);
    }


    /// <summary>
    /// URI 字符串 → 缓存文件名 的记忆化映射。
    /// 同一图片 URL（如抽卡列表里反复出现的相同角色/武器图标）只在首次计算一次哈希，之后直接复用结果，
    /// 避免每条记录、每次重新实现（虚拟化滚动）都重算 XxHash64+MD5。
    /// 键为完整 URI 字符串、值为 48 字符文件名；条目小、按会话累积（去重后图片 URL 数量有限），无需淘汰。
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _cacheFileNames = new();


    /// <summary>
    /// 根据 URI 计算缓存文件名（记忆化：相同 URI 只计算一次哈希）。
    /// 使用 XxHash64（前 8 字节）+ MD5（后 16 字节）混合哈希，兼顾速度与碰撞抵抗。
    /// 返回 48 字符十六进制字符串。
    /// </summary>
    private static string GetCacheFileName(Uri uri)
    {
        return _cacheFileNames.GetOrAdd(uri.ToString(), static key => ComputeCacheFileName(key));
    }


    /// <summary>实际的哈希计算，仅在记忆化未命中时调用。输入为完整 URI 字符串（即 <see cref="Uri.ToString"/>）。</summary>
    private static string ComputeCacheFileName(string uri)
    {
        byte[] hashBytes = ArrayPool<byte>.Shared.Rent(24);
        try
        {
            ReadOnlySpan<byte> pathSpan = MemoryMarshal.AsBytes(uri.AsSpan());
            XxHash64.Hash(pathSpan, hashBytes.AsSpan(0, 8));
            MD5.HashData(pathSpan, hashBytes.AsSpan(8, 16));
            return Convert.ToHexString(hashBytes.AsSpan(0, 24));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(hashBytes);
        }
    }


    /// <summary>
    /// 判断指定路径的缓存文件是否仍然有效。
    /// 条件：文件存在 + 文件大小 &gt; 0 + 最后写入时间在 CacheDuration 之内。
    /// </summary>
    private static bool IsFileCacheAvailable(string path, TimeSpan duration)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            return fileInfo.Length > 0 && (DateTime.Now - fileInfo.LastWriteTime <= duration);
        }
        return false;
    }


    /// <summary>
    /// 异步删除指定 URI 对应的缓存文件（如果存在）。
    /// 通常在图片解码失败（BitmapImage_ImageFailed）时调用，用于清理损坏的缓存。
    /// </summary>
    public static async void DeleteCacheFile(Uri uri)
    {
        await Task.Run(() =>
        {
            string fileName = GetCacheFileName(uri);
            string filePath = Path.Join(CacheFolder, fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting cache file: {ex.Message}");
                }
            }
        }).ConfigureAwait(false);
    }

}
