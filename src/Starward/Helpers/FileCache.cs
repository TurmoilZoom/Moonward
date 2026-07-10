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
/// <item>磁盘下载不绑定调用方 CancellationToken：UI（CachedImage Source 切换）取消不应打断可共享的缓存写入，
/// 否则大图容易留下 0 字节 _tmp 且永远下不完。</item>
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
    /// <param name="cancellationToken">
    /// 仅用于在「已有结果」后让调用方协作取消等待语义由上层处理。
    /// <b>不会</b>取消正在进行的磁盘下载：同一 URL 的缓存写入应对所有 CachedImage 共享，
    /// 单个控件 Source 切换不应留下 0 字节 _tmp 或中断他人可用的缓存。
    /// </param>
    /// <returns>本地缓存文件完整路径；失败时返回 null（除非 throwOnError）。</returns>
    public static async Task<string?> GetFromCacheAsync(Uri uri, bool throwOnError = false, CancellationToken cancellationToken = default)
    {
        return await GetItemAsync(uri, throwOnError, cancellationToken);
    }


    /// <summary>
    /// 内部入口：实现并发去重 + 错误处理 + 清理字典。
    /// 使用 GetOrAdd 保证同一文件名只启动一个下载 Task，避免竞态下多路写同一 _tmp。
    /// </summary>
    private static async Task<string?> GetItemAsync(Uri uri, bool throwOnError, CancellationToken cancellationToken)
    {
        string fileName = GetCacheFileName(uri);

        // 下载 Task 不捕获调用方 token，避免 ImageEx 取消把共享下载一起掐断。
        Task<string?> request = _concurrentTasks.GetOrAdd(fileName, static (name, state) =>
        {
            var (u, _) = state;
            return GetFromCacheOrDownloadAsync(u, name);
        }, (uri, 0));

        try
        {
            // 始终等下载结束，使缓存有机会落盘；CachedImage 在返回后自行检查 token 决定是否贴图。
            string? path = await request.ConfigureAwait(false);
            // 若调用方已取消且仍想快速退出：有有效路径时仍返回路径（便于上层丢弃展示但磁盘已缓存）。
            _ = cancellationToken;
            return path;
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
            // 仅当字典中仍是本次 Task 时移除，避免误删后到的新一轮下载。
            if (_concurrentTasks.TryGetValue(fileName, out Task<string?>? current) && ReferenceEquals(current, request))
            {
                _concurrentTasks.TryRemove(fileName, out _);
            }
        }

        return null;
    }


    /// <summary>
    /// 检查本地缓存是否可用；不可用则下载（带重试）。
    /// 下载过程不接受外部 CancellationToken（见类型注释）。
    /// </summary>
    /// <param name="uri">远程资源地址。</param>
    /// <param name="fileName">缓存文件名（哈希）。</param>
    /// <returns>有效本地路径；全部失败时返回 null。</returns>
    private static async Task<string?> GetFromCacheOrDownloadAsync(Uri uri, string fileName)
    {
        if (CacheFolder is null)
        {
            throw new DirectoryNotFoundException("Cache folder not initialized.");
        }

        string filePath = Path.Combine(CacheFolder, fileName);

        // 缓存命中检查放到线程池执行：避免在 UI 线程上做文件元数据 IO。
        // 这个 await 也会立即让出控制权，使 GetItemAsync 能先把本任务登记进并发去重字典，再继续后续下载。
        if (await Task.Run(() => IsFileCacheAvailable(filePath, CacheDuration)).ConfigureAwait(false))
        {
            return filePath;
        }

        uint retries = 0;
        while (retries < RetryCount)
        {
            try
            {
                await DownloadFileAsync(uri, filePath).ConfigureAwait(false);
                // 成功落盘则立刻返回，避免原实现「成功后仍再下 2 次」导致多余 _tmp / 二次取消窗口。
                if (IsFileCacheAvailable(filePath, CacheDuration))
                {
                    return filePath;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"FileCache HTTP error ({uri}): {ex.Message}");
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"FileCache IO error ({uri}): {ex.Message}");
            }
            retries++;
        }

        // 全部失败：若正式文件仍无效，清掉 0 字节/残缺 _tmp，避免下次误判或脏状态残留。
        if (!IsFileCacheAvailable(filePath, CacheDuration))
        {
            TryDeleteFile(filePath + "_tmp");
            return null;
        }

        return filePath;
    }


    /// <summary>
    /// 核心下载实现：支持断点续传。
    /// 使用 &lt;path&gt;_tmp 作为临时文件，先写入再原子 Move，避免损坏缓存。
    /// 仅当临时文件已有字节时才发 Range（空文件不带 Range，减少部分 CDN 边界问题）。
    /// </summary>
    /// <param name="uri">远程资源地址。</param>
    /// <param name="path">目标缓存文件完整路径（无 _tmp 后缀）。</param>
    private static async Task DownloadFileAsync(Uri uri, string path)
    {
        string path_tmp = path + "_tmp";

        // 流作用域结束并释放句柄后再 Move，避免文件占用。
        try
        {
            // 独占写入：同一 URI 仅有一个下载 Task，避免多写交错。
            await using FileStream fs = new FileStream(path_tmp, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            // 仅续传时带 Range；0 长度表示全量下载，不发 bytes=0-。
            if (fs.Length > 0)
            {
                request.Headers.Range = new RangeHeaderValue(fs.Length, null);
            }
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            // 不使用外部 CancellationToken：控件取消 ≠ 取消缓存写入。
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);

            // 续传时若服务器返回 416（Range 无效），抛出让 finally 删 _tmp，上层重试全量下。
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                throw new HttpRequestException($"Range not satisfiable for {uri}", null, response.StatusCode);
            }

            response.EnsureSuccessStatusCode();

            // 全量响应（200）时应覆盖重写，而不是在旧残数据后追加。
            if (response.StatusCode == HttpStatusCode.OK)
            {
                fs.SetLength(0);
                fs.Position = 0;
            }
            else if (response.Content.Headers.ContentRange?.From is long from && from > 0)
            {
                fs.Position = from;
            }

            await using Stream hs = await response.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
            await hs.CopyToAsync(fs, CancellationToken.None).ConfigureAwait(false);
            await fs.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            TryDeleteFile(path_tmp);
            throw;
        }

        if (!File.Exists(path_tmp) || new FileInfo(path_tmp).Length == 0)
        {
            TryDeleteFile(path_tmp);
            throw new HttpRequestException($"Downloaded empty body for {uri}");
        }

        File.Move(path_tmp, path, true);
    }


    /// <summary>尽量删除临时/损坏文件，忽略常见 IO 竞争错误。</summary>
    /// <param name="path">要删除的文件路径。</param>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting file {path}: {ex.Message}");
        }
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
            TryDeleteFile(filePath);
            TryDeleteFile(filePath + "_tmp");
        }).ConfigureAwait(false);
    }

}
