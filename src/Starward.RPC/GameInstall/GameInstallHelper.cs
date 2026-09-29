using Microsoft.Extensions.Logging;
using SharpSevenZip;
using SharpSevenZip.Exceptions;
using Snap.HPatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Vanara.PInvoke;
using ZstdSharp;

namespace Starward.RPC.GameInstall;

internal partial class GameInstallHelper
{

    private const int BUFFER_SIZE = 8192;

    private const int MD5_BUFFER_SIZE = 1 << 19;


    private readonly ILogger<GameInstallHelper> _logger;

    private readonly IHttpClientFactory _httpClientFactory;

    private TokenBucketRateLimiter _rateLimiter;



    private FileStreamOptions SequentialReadFileStreamOptions = new FileStreamOptions
    {
        Access = FileAccess.Read,
        Mode = FileMode.Open,
        Options = FileOptions.SequentialScan,
        Share = FileShare.ReadWrite | FileShare.Delete,
    };


    private FileStreamOptions MD5CheckFileStreamOptions = new FileStreamOptions
    {
        Access = FileAccess.Read,
        BufferSize = MD5_BUFFER_SIZE,
        Mode = FileMode.Open,
        Options = FileOptions.SequentialScan,
        Share = FileShare.ReadWrite | FileShare.Delete,
    };



    public GameInstallHelper(ILogger<GameInstallHelper> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            AutoReplenishment = true,
            QueueLimit = int.MaxValue,
            TokenLimit = int.MaxValue,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(100),
            TokensPerPeriod = int.MaxValue,
        });
    }



    /// <summary>
    /// 设置下载速率限制
    /// </summary>
    /// <param name="bytesPerSecond"></param>
    public int SetRateLimiter(int bytesPerSecond)
    {
        int result = 0;
        if (bytesPerSecond <= 0)
        {
            _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                AutoReplenishment = true,
                QueueLimit = int.MaxValue,
                TokenLimit = int.MaxValue,
                ReplenishmentPeriod = TimeSpan.FromMilliseconds(100),
                TokensPerPeriod = int.MaxValue,
            });
            result = 0;
        }
        else
        {
            int limit = Math.Clamp(bytesPerSecond / 10, BUFFER_SIZE, int.MaxValue);
            _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                AutoReplenishment = true,
                QueueLimit = int.MaxValue,
                TokenLimit = limit,
                ReplenishmentPeriod = TimeSpan.FromMilliseconds(100),
                TokensPerPeriod = limit,
            });
            result = Math.Clamp(limit, 0, int.MaxValue / 10) * 10;
        }
        _logger.LogInformation("Set downloading rate limiter: {bytesPerSecond} bytes/s", result);
        return result;
    }



    /// <summary>
    /// 检查文件MD5
    /// </summary>
    /// <param name="path"></param>
    /// <param name="size"></param>
    /// <param name="md5"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> CheckFileMD5Async(GameInstallContext task, string? path, long size, string md5, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        if (new FileInfo(path).Length != size)
        {
            return false;
        }
        using FileStream fs = File.Open(path, MD5CheckFileStreamOptions);
        byte[] buffer = new byte[MD5_BUFFER_SIZE];
        using MD5 md5Hash = MD5.Create();
        int read;
        while ((read = await fs.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5Hash.TransformBlock(buffer, 0, read, null, 0);
            Interlocked.Add(ref task.storageReadBytes, read);
        }
        md5Hash.TransformFinalBlock(buffer, 0, 0);
        if (md5Hash.Hash is null)
        {
            return false;
        }
        return string.Equals(md5, Convert.ToHexStringLower(md5Hash.Hash), StringComparison.OrdinalIgnoreCase);
    }



    /// <summary>
    /// 检查文件MD5
    /// </summary>
    /// <param name="task"></param>
    /// <param name="stream"></param>
    /// <param name="md5"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> CheckFileMD5Async(GameInstallContext task, Stream stream, string md5, CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[MD5_BUFFER_SIZE];
        using MD5 md5Hash = MD5.Create();
        int read;
        stream.Position = 0;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5Hash.TransformBlock(buffer, 0, read, null, 0);
            Interlocked.Add(ref task.storageReadBytes, read);
        }
        md5Hash.TransformFinalBlock(buffer, 0, 0);
        if (md5Hash.Hash is null)
        {
            return false;
        }
        return string.Equals(md5, Convert.ToHexStringLower(md5Hash.Hash), StringComparison.OrdinalIgnoreCase);
    }




    /// <summary>
    /// 检查文件MD5，并更新进度到下载进度
    /// </summary>
    /// <param name="task"></param>
    /// <param name="path"></param>
    /// <param name="size"></param>
    /// <param name="md5"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> CheckFileMD5InDownloadProgressAsync(GameInstallContext task, string? path, long size, string md5, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        if (new FileInfo(path).Length != size)
        {
            return false;
        }
        using FileStream fs = File.Open(path, MD5CheckFileStreamOptions);
        byte[] buffer = new byte[MD5_BUFFER_SIZE];
        using MD5 md5Hash = MD5.Create();
        int read;
        while ((read = await fs.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5Hash.TransformBlock(buffer, 0, read, null, 0);
            Interlocked.Add(ref task._progress_DownloadFinishBytes, read);
            Interlocked.Add(ref task.storageReadBytes, read);
        }
        md5Hash.TransformFinalBlock(buffer, 0, 0);
        if (md5Hash.Hash is null)
        {
            Interlocked.Add(ref task._progress_DownloadFinishBytes, -fs.Length);
            return false;
        }
        if (string.Equals(md5, Convert.ToHexStringLower(md5Hash.Hash), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        else
        {
            Interlocked.Add(ref task._progress_DownloadFinishBytes, -fs.Length);
            return false;
        }
    }



    /// <summary>
    /// 检查文件MD5，并更新进度到读取进度
    /// </summary>
    /// <param name="task"></param>
    /// <param name="path"></param>
    /// <param name="size"></param>
    /// <param name="md5"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> CheckFileMD5InReadProgressAsync(GameInstallContext task, string? path, long size, string md5, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        if (new FileInfo(path).Length != size)
        {
            return false;
        }
        using FileStream fs = File.Open(path, MD5CheckFileStreamOptions);
        byte[] buffer = new byte[MD5_BUFFER_SIZE];
        using MD5 md5Hash = MD5.Create();
        int read;
        while ((read = await fs.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5Hash.TransformBlock(buffer, 0, read, null, 0);
            Interlocked.Add(ref task._progress_ReadFinishBytes, read);
            Interlocked.Add(ref task.storageReadBytes, read);
        }
        md5Hash.TransformFinalBlock(buffer, 0, 0);
        if (md5Hash.Hash is null)
        {
            Interlocked.Add(ref task._progress_ReadFinishBytes, -fs.Length);
            return false;
        }
        if (string.Equals(md5, Convert.ToHexStringLower(md5Hash.Hash), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        else
        {
            Interlocked.Add(ref task._progress_ReadFinishBytes, -fs.Length);
            return false;
        }
    }



    /// <summary>
    /// 创建硬链接
    /// </summary>
    /// <param name="file"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> HardLinkAsync(GameInstallContext task, GameInstallFile file, CancellationToken cancellationToken = default)
    {
        if (!await CheckFileMD5Async(task, file.HardLinkTarget, file.Size, file.MD5, cancellationToken))
        {
            return false;
        }
        string temp = file.FullPath + ".link";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
        if (Kernel32.CreateHardLink(temp, file.HardLinkTarget!))
        {
            File.Move(temp, file.FullPath, true);
            return true;
        }
        else
        {
            return false;
        }
    }




    /// <summary>
    /// 以 Chunk 模式下载文件到本地。块按偏移顺序写入 <c>_tmp</c> 文件，文件长度就是写入进度，暂停后可按块续传；
    /// 后面需要联网的块由 <paramref name="scheduler"/> 限流后提前并行下载到内存，写到该块时直接解压。
    /// </summary>
    /// <param name="task"></param>
    /// <param name="file"></param>
    /// <param name="scheduler">本次下载共用的并发与内存限制</param>
    /// <param name="updateMode">是否在更新</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="Exception">整个文件的 MD5 校验失败</exception>
    public async Task DownloadChunksToFileAsync(GameInstallContext task, GameInstallFile file, ChunkDownloadScheduler scheduler, bool updateMode = false, CancellationToken cancellationToken = default)
    {
        long downloadBytes = file.Chunks?.Sum(x => x.CompressedSize) ?? 0, writeBytes = file.Size;
        if (file.IsFinished || await HardLinkAsync(task, file, cancellationToken) || await CheckFileMD5Async(task, file.FullPath, file.Size, file.MD5, cancellationToken))
        {
            Interlocked.Add(ref task._progress_WriteFinishBytes, writeBytes);
            if (!updateMode)
            {
                Interlocked.Add(ref task._progress_DownloadFinishBytes, downloadBytes);
            }
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
        string path_tmp = file.FullPath + "_tmp";
        using FileStream fs = File.Open(path_tmp, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        if (fs.Length < file.Size)
        {
            ChunkProgress progress = new(task);
            using HttpClient httpClient = _httpClientFactory.CreateClient();
            using CancellationTokenSource prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // 已发出的预读，按块下标索引，每个占一个预读名额
            Dictionary<int, Task<byte[]>> prefetched = new();
            // 当前块的下载：预读来的，或没抢到预读名额、写到这里才开始的
            Task<byte[]>? pending = null;
            bool pendingIsPrefetched = false;
            try
            {
                try
                {
                    List<GameInstallFileChunk> chunks = file.Chunks ?? [];
                    List<int> networkChunks = GetNetworkChunkIndexes(task, chunks, fs.Length);
                    int nextNetworkChunk = 0;
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        GameInstallFileChunk chunk = chunks[i];
                        // 根据当前文件的长度判断 chunk 是否已完成下载和解压
                        if (fs.Length >= chunk.Offset + chunk.UncompressedSize)
                        {
                            if (!updateMode)
                            {
                                progress.AddDownload(chunk.CompressedSize);
                            }
                            progress.AddWrite(chunk.UncompressedSize);
                            continue;
                        }

                        while (nextNetworkChunk < networkChunks.Count && networkChunks[nextNetworkChunk] < i)
                        {
                            nextNetworkChunk++;
                        }
                        if (prefetched.Remove(i, out pending))
                        {
                            pendingIsPrefetched = true;
                        }
                        else if (nextNetworkChunk < networkChunks.Count && networkChunks[nextNetworkChunk] == i)
                        {
                            pending = DownloadChunkAsync(task, httpClient, chunk, scheduler, progress, prefetchCts.Token);
                            pendingIsPrefetched = false;
                            nextNetworkChunk++;
                        }

                        // 写当前块之前，把后面要联网的块先下载起来
                        while (prefetched.Count < ChunkDownloadScheduler.MaxPrefetchedChunksPerFile
                            && nextNetworkChunk < networkChunks.Count
                            && scheduler.TryEnterPrefetch())
                        {
                            int index = networkChunks[nextNetworkChunk++];
                            prefetched[index] = DownloadChunkAsync(task, httpClient, chunks[index], scheduler, progress, prefetchCts.Token);
                        }

                        fs.Position = chunk.Offset;
                        if (pending is null && !await TryWriteLocalChunkAsync(task, fs, chunk, progress, cancellationToken))
                        {
                            // 本地数据校验不过，临时联网下载
                            pending = DownloadChunkAsync(task, httpClient, chunk, scheduler, progress, prefetchCts.Token);
                            pendingIsPrefetched = false;
                        }
                        if (pending is not null)
                        {
                            Task<byte[]> downloadTask = pending;
                            bool holdsPrefetchSlot = pendingIsPrefetched;
                            pending = null;
                            try
                            {
                                byte[] buffer = await downloadTask;
                                try
                                {
                                    await WriteCompressedChunkAsync(task, fs, buffer, chunk, progress, cancellationToken);
                                }
                                finally
                                {
                                    scheduler.BufferPool.Return(buffer);
                                }
                            }
                            finally
                            {
                                if (holdsPrefetchSlot)
                                {
                                    scheduler.ExitPrefetch();
                                }
                            }
                        }
                    }
                }
                finally
                {
                    // 出错或取消时停掉本文件剩余的下载，等它们结束后才能回滚进度，否则回滚后还会有下载字节计入
                    prefetchCts.Cancel();
                    if (pending is not null)
                    {
                        await DiscardChunkDownloadAsync(pending, pendingIsPrefetched, scheduler);
                    }
                    foreach (Task<byte[]> item in prefetched.Values)
                    {
                        await DiscardChunkDownloadAsync(item, true, scheduler);
                    }
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                progress.Rollback();
                throw ex.InnerException;
            }
            catch (Exception)
            {
                progress.Rollback();
                throw;
            }

        }
        await fs.DisposeAsync();

        if (await CheckFileMD5Async(task, path_tmp, file.Size, file.MD5, cancellationToken))
        {
            File.Move(path_tmp, file.FullPath, true);
        }
        else
        {
            // 校验失败，删除文件，回滚进度
            File.Delete(path_tmp);
            Interlocked.Add(ref task._progress_WriteFinishBytes, -writeBytes);
            if (!updateMode)
            {
                Interlocked.Add(ref task._progress_DownloadFinishBytes, -downloadBytes);
            }
            var ex = new Exception("MD5 not match.");
            _logger.LogError(ex, "MD5 not match.\nFile: {file}\nReal MD5: {realMD5}", path_tmp, file.MD5);
            throw ex;
        }

    }



    /// <summary>
    /// 预估尚未写入的块里哪些要联网下载。本地旧文件或预下载缓存大小对得上的块先按本地处理，
    /// 写到该块时再校验内容，校验不过才临时联网下载。
    /// </summary>
    /// <param name="task"></param>
    /// <param name="chunks">文件的全部块，按偏移排序</param>
    /// <param name="writtenLength"><c>_tmp</c> 文件已写入的长度</param>
    /// <returns>需要联网下载的块在 <paramref name="chunks"/> 中的下标，升序</returns>
    private static List<int> GetNetworkChunkIndexes(GameInstallContext task, List<GameInstallFileChunk> chunks, long writtenLength)
    {
        List<int> result = new();
        // 同一个旧文件会被很多块引用，文件长度只查一次
        Dictionary<string, long> originalFileLengths = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < chunks.Count; i++)
        {
            GameInstallFileChunk chunk = chunks[i];
            if (writtenLength >= chunk.Offset + chunk.UncompressedSize)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(chunk.OriginalFileFullPath))
            {
                if (!originalFileLengths.TryGetValue(chunk.OriginalFileFullPath, out long length))
                {
                    FileInfo info = new(chunk.OriginalFileFullPath);
                    length = info.Exists ? info.Length : -1;
                    originalFileLengths[chunk.OriginalFileFullPath] = length;
                }
                if (length == chunk.OriginalFileSize)
                {
                    continue;
                }
            }
            FileInfo cache = new(Path.Join(task.InstallPath, "chunk", chunk.Id));
            if (cache.Exists && cache.Length == chunk.CompressedSize)
            {
                continue;
            }
            result.Add(i);
        }
        return result;
    }



    /// <summary>
    /// 从本地旧版本文件或预下载缓存写入一个块，写入位置为 <paramref name="fs"/> 的当前位置。
    /// </summary>
    /// <param name="task"></param>
    /// <param name="fs">目标 <c>_tmp</c> 文件</param>
    /// <param name="chunk"></param>
    /// <param name="progress"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>本地没有可用数据（不存在或校验不过）时返回 <see langword="false"/>，此时未写入任何内容</returns>
    private async Task<bool> TryWriteLocalChunkAsync(GameInstallContext task, FileStream fs, GameInstallFileChunk chunk, ChunkProgress progress, CancellationToken cancellationToken)
    {
        if (File.Exists(chunk.OriginalFileFullPath) && new FileInfo(chunk.OriginalFileFullPath).Length == chunk.OriginalFileSize)
        {
            // 老版本文件存在相同的chunk
            using FileSliceStream ofs = new FileSliceStream(chunk.OriginalFileFullPath, chunk.OriginalFileOffset, chunk.UncompressedSize);
            if (!await CheckFileMD5Async(task, ofs, chunk.UncompressedMD5, cancellationToken))
            {
                return false;
            }
            ofs.Position = 0;
            await ofs.CopyToAsync(fs, cancellationToken);
            progress.AddWrite(chunk.UncompressedSize);
            Interlocked.Add(ref task.storageWriteBytes, chunk.UncompressedSize);
            return true;
        }
        string chunkCache = Path.Join(task.InstallPath, "chunk", chunk.Id);
        if (await CheckFileMD5Async(task, chunkCache, chunk.CompressedSize, chunk.CompressedMD5, cancellationToken))
        {
            // 存在预下载的chunk文件
            using FileStream cfs = File.Open(chunkCache, SequentialReadFileStreamOptions);
            using DecompressionStream ds = new(cfs);
            await ds.CopyToAsync(fs, cancellationToken);
            progress.AddDownload(chunk.CompressedSize);
            progress.AddWrite(chunk.UncompressedSize);
            Interlocked.Add(ref task.storageWriteBytes, chunk.UncompressedSize);
            return true;
        }
        return false;
    }



    /// <summary>
    /// 下载一个块的压缩数据到内存并校验 MD5。先占下载名额，读完响应才归还。
    /// </summary>
    /// <param name="task"></param>
    /// <param name="httpClient"></param>
    /// <param name="chunk"></param>
    /// <param name="scheduler"></param>
    /// <param name="progress"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>从 <see cref="ChunkDownloadScheduler.BufferPool"/> 租用的缓冲区，前 <see cref="GameInstallFileChunk.CompressedSize"/> 字节为块数据，用完须归还</returns>
    /// <exception cref="IOException">数据长度或 MD5 不符</exception>
    private async Task<byte[]> DownloadChunkAsync(GameInstallContext task, HttpClient httpClient, GameInstallFileChunk chunk, ChunkDownloadScheduler scheduler, ChunkProgress progress, CancellationToken cancellationToken)
    {
        await scheduler.WaitDownloadSlotAsync(cancellationToken);
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(chunk.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using Stream hs = await response.Content.ReadAsStreamAsync(cancellationToken);
            int size = checked((int)chunk.CompressedSize);
            byte[] buffer = scheduler.BufferPool.Rent(size);
            try
            {
                int total = 0;
                while (total < size)
                {
                    // 每次最多读 BUFFER_SIZE：限速时令牌上限最小就是 BUFFER_SIZE，一次申请更多会抛异常
                    int read = await hs.ReadAsync(buffer.AsMemory(total, Math.Min(BUFFER_SIZE, size - total)), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }
                    // RateLimiter 的等待队列已设置为 int.MaxValue，理论上不会出现获取令牌失败的情况
                    RateLimitLease lease = await _rateLimiter.AcquireAsync(read, cancellationToken);
                    while (!lease.IsAcquired)
                    {
                        await Task.Delay(1, cancellationToken);
                        lease = await _rateLimiter.AcquireAsync(read, cancellationToken);
                    }
                    total += read;
                    progress.AddDownload(read);
                    Interlocked.Add(ref task.networkDownloadBytes, read);
                }
                // 写入前先校验单个块，坏块只需重试这个文件剩下的部分，不必等整个文件校验失败后从头再来
                if (total != size || !string.Equals(Convert.ToHexString(MD5.HashData(buffer.AsSpan(0, size))), chunk.CompressedMD5, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Chunk data not match, chunk: {chunk.Id}, size: {size}, received: {total}.");
                }
                return buffer;
            }
            catch
            {
                scheduler.BufferPool.Return(buffer);
                throw;
            }
        }
        finally
        {
            scheduler.ReleaseDownloadSlot();
        }
    }



    /// <summary>
    /// 把内存中的块解压写入 <paramref name="fs"/> 中该块的位置。
    /// </summary>
    /// <param name="task"></param>
    /// <param name="fs">目标 <c>_tmp</c> 文件</param>
    /// <param name="buffer"><see cref="DownloadChunkAsync"/> 返回的缓冲区</param>
    /// <param name="chunk"></param>
    /// <param name="progress"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private static async Task WriteCompressedChunkAsync(GameInstallContext task, FileStream fs, byte[] buffer, GameInstallFileChunk chunk, ChunkProgress progress, CancellationToken cancellationToken)
    {
        fs.Position = chunk.Offset;
        using MemoryStream ms = new(buffer, 0, (int)chunk.CompressedSize, writable: false);
        using DecompressionStream ds = new(ms);
        await ds.CopyToAsync(fs, cancellationToken);
        long written = fs.Position - chunk.Offset;
        progress.AddWrite(written);
        Interlocked.Add(ref task.storageWriteBytes, written);
    }



    /// <summary>
    /// 丢弃一个不再写入的块下载：等它结束，归还缓冲区和预读名额。
    /// </summary>
    /// <param name="downloadTask"></param>
    /// <param name="holdsPrefetchSlot">是否占着预读名额</param>
    /// <param name="scheduler"></param>
    /// <returns></returns>
    private static async Task DiscardChunkDownloadAsync(Task<byte[]> downloadTask, bool holdsPrefetchSlot, ChunkDownloadScheduler scheduler)
    {
        try
        {
            byte[] buffer = await downloadTask;
            scheduler.BufferPool.Return(buffer);
        }
        catch
        {
            // 数据已经不要了，被取消或下载失败都不影响
        }
        finally
        {
            if (holdsPrefetchSlot)
            {
                scheduler.ExitPrefetch();
            }
        }
    }



    /// <summary>
    /// 单个文件在本次调用中计入任务进度的字节数，出错时据此回滚。预读的块在其他线程计入，所以用原子操作。
    /// </summary>
    private sealed class ChunkProgress
    {

        private readonly GameInstallContext _task;

        private long _download;

        private long _write;


        public ChunkProgress(GameInstallContext task)
        {
            _task = task;
        }


        public void AddDownload(long bytes)
        {
            Interlocked.Add(ref _task._progress_DownloadFinishBytes, bytes);
            Interlocked.Add(ref _download, bytes);
        }


        public void AddWrite(long bytes)
        {
            Interlocked.Add(ref _task._progress_WriteFinishBytes, bytes);
            Interlocked.Add(ref _write, bytes);
        }


        /// <summary>
        /// 从任务进度中减去本次计入的字节数
        /// </summary>
        public void Rollback()
        {
            Interlocked.Add(ref _task._progress_DownloadFinishBytes, -Interlocked.Exchange(ref _download, 0));
            Interlocked.Add(ref _task._progress_WriteFinishBytes, -Interlocked.Exchange(ref _write, 0));
        }

    }



    /// <summary>
    /// 下载到文件
    /// </summary>
    /// <param name="task"></param>
    /// <param name="item"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task DownloadToFileAsync(GameInstallContext task, GameInstallFile file, CancellationToken cancellationToken = default)
    {
        if (file.IsFinished || await HardLinkAsync(task, file, cancellationToken))
        {
            Interlocked.Add(ref task._progress_DownloadFinishBytes, file.Size);
        }
        else
        {
            await DownloadToFileAsync(task, file.FullPath, file.Url, file.Size, file.MD5, cancellationToken);
        }
    }



    /// <summary>
    /// 下载到文件
    /// </summary>
    /// <param name="task"></param>
    /// <param name="item"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task DownloadToFileAsync(GameInstallContext task, PredownloadFile item, CancellationToken cancellationToken = default)
    {
        await DownloadToFileAsync(task, item.FullPath, item.Url, item.Size, item.MD5, cancellationToken);
    }




    /// <summary>
    /// 下载到文件
    /// </summary>
    /// <param name="task"></param>
    /// <param name="path"></param>
    /// <param name="url"></param>
    /// <param name="size"></param>
    /// <param name="md5"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task DownloadToFileAsync(GameInstallContext task, string path, string url, long size, string md5, CancellationToken cancellationToken = default)
    {
        if (await CheckFileMD5InDownloadProgressAsync(task, path, size, md5, cancellationToken))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string path_tmp = path + "_tmp";

        using FileStream fs = File.Open(path_tmp, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (fs.Length < size)
        {
            try
            {
                using HttpClient httpClient = _httpClientFactory.CreateClient();
                HttpRequestMessage request = new(HttpMethod.Get, url) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
                request.Headers.Range = new RangeHeaderValue(fs.Length, null);
                using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentRange?.From is not null)
                {
                    // 文件链接支持断点续传
                    fs.Position = response.Content.Headers.ContentRange.From.Value;
                    Interlocked.Add(ref task._progress_DownloadFinishBytes, fs.Position);
                }
                byte[] buffer = new byte[BUFFER_SIZE];
                int read = 0;
                using Stream hs = await response.Content.ReadAsStreamAsync(cancellationToken);
                while ((read = await hs.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    RateLimitLease lease = await _rateLimiter.AcquireAsync(read, cancellationToken);
                    while (!lease.IsAcquired)
                    {
                        await Task.Delay(1, cancellationToken);
                        lease = await _rateLimiter.AcquireAsync(read, cancellationToken);
                    }
                    await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    Interlocked.Add(ref task._progress_DownloadFinishBytes, read);
                    Interlocked.Add(ref task.networkDownloadBytes, read);
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                Interlocked.Add(ref task._progress_DownloadFinishBytes, -fs.Position);
                throw ex.InnerException;
            }
            catch
            {
                Interlocked.Add(ref task._progress_DownloadFinishBytes, -fs.Position);
                throw;
            }
        }
        await fs.DisposeAsync();

        if (await CheckFileMD5Async(task, path_tmp, size, md5, cancellationToken))
        {
            File.Move(path_tmp, path, true);
        }
        else
        {
            File.Delete(path_tmp);
            Interlocked.Add(ref task._progress_WriteFinishBytes, -size);
            var ex = new Exception("MD5 not match.");
            _logger.LogError(ex, "MD5 not match.\nFile: {file}\nReal MD5: {realMD5}", path, md5);
            throw ex;
        }
    }




    /// <summary>
    /// 获取预下载文件
    /// </summary>
    /// <param name="task"></param>
    /// <returns></returns>
    public static List<PredownloadFile> GetPredownloadFiles(GameInstallContext task)
    {
        Dictionary<string, PredownloadFile> dict = new();
        foreach (GameInstallFile item in task.TaskFiles ?? [])
        {
            if (item.DownloadMode is GameInstallDownloadMode.SingleFile)
            {
                if (!dict.TryGetValue(item.FullPath, out _))
                {
                    dict.TryAdd(item.FullPath, new PredownloadFile
                    {
                        FullPath = item.FullPath,
                        Url = item.Url,
                        Size = item.Size,
                        MD5 = item.MD5,
                    });
                }
            }
            else if (item.DownloadMode is GameInstallDownloadMode.CompressedPackage)
            {
                foreach (GameInstallCompressedPackage package in item.CompressedPackages ?? [])
                {
                    if (!dict.TryGetValue(package.FullPath, out _))
                    {
                        dict.TryAdd(package.FullPath, new PredownloadFile
                        {
                            FullPath = package.FullPath,
                            Url = package.Url,
                            Size = package.Size,
                            MD5 = package.MD5,
                        });
                    }
                }
            }
            else if (item.DownloadMode is GameInstallDownloadMode.Chunk)
            {
                foreach (GameInstallFileChunk chunk in item.Chunks ?? [])
                {
                    if (!dict.TryGetValue(chunk.Id, out _) && string.IsNullOrWhiteSpace(chunk.OriginalFileFullPath))
                    {
                        string path = Path.Combine(task.InstallPath, "chunk", chunk.Id);
                        dict.TryAdd(chunk.Id, new PredownloadFile
                        {
                            FullPath = path,
                            Url = chunk.Url,
                            Size = chunk.CompressedSize,
                            MD5 = chunk.CompressedMD5,
                        });
                    }
                }
            }
            else if (item.DownloadMode is GameInstallDownloadMode.Patch && item.Patch is not null)
            {
                if (!dict.TryGetValue(item.Patch.Id, out _))
                {
                    string path = Path.Combine(task.InstallPath, "ldiff", item.Patch.Id);
                    dict.TryAdd(item.Patch.Id, new PredownloadFile
                    {
                        FullPath = path,
                        Url = item.Patch.Url,
                        Size = item.Patch.PatchFileSize,
                        MD5 = item.Patch.PatchFileMD5,
                    });
                }
            }
        }
        return dict.Values.ToList();
    }




    /// <summary>
    /// 解压压缩包
    /// </summary>
    /// <param name="task"></param>
    /// <param name="file"></param>
    /// <param name="percentRatio"></param>
    /// <param name="cancellationToken"></param>
    public async Task ExtractCompressedPackageAsync(GameInstallContext task, GameInstallFile file, double percentRatio, CancellationToken cancellationToken = default)
    {
        if (file.DownloadMode is not GameInstallDownloadMode.CompressedPackage)
        {
            return;
        }
        var files = file.CompressedPackages?.Select(x => x.FullPath).ToList();
        if (files is null || files.Count == 0)
        {
            return;
        }
        _logger.LogInformation("GameInstallTask ({GameBiz}): Extracting compressed package {file}", task.GameId.GameBiz, files[0]);
        using FileCombinedStream fs = new FileCombinedStream(files);
        using var archive = new SharpSevenZipExtractor(fs, leaveOpen: true);

        double lastPercent = task.Progress_Percent;
        double extractRatio = percentRatio;
        double halfRatio = percentRatio / 2;
        if (archive.ArchiveFileData.Any(x => x.FileName == "hdiffmap.json" || x.FileName == "hdifffiles.txt"))
        {
            // 存在有需要 hpatch 的文件时，解压进度和 hpatch 进度各占一半
            extractRatio = halfRatio;
        }
        archive.Extracting += (_, e) =>
        {
            task.Progress_Percent = lastPercent + e.FinishPercent * extractRatio;
        };
        try
        {
            Task extractTask = Task.Run(() => archive.ExtractArchive(task.InstallPath), cancellationToken);
            while (!extractTask.IsCompleted)
            {
                await Task.Delay(50, CancellationToken.None);
                if (cancellationToken.IsCancellationRequested)
                {
                    fs.Dispose();
                }
            }
            await extractTask;
        }
        catch (SharpSevenZipException ex) when (ex.HResult is -2146233088)
        {
            // ObjectDisposedException
            throw new OperationCanceledException("Decompress operation canceled.");
        }
        task.Progress_Percent = lastPercent + extractRatio;
        await PatchCompressedPackageDiffFilesAsync(task, file, halfRatio, cancellationToken);
        task.Progress_Percent = lastPercent + percentRatio;
        _logger.LogInformation("GameInstallTask ({GameBiz}): Extract compressed package finished {file}", task.GameId.GameBiz, files[0]);
    }




    /// <summary>
    /// 合并压缩包中的 diff 文件
    /// </summary>
    /// <param name="task"></param>
    /// <param name="file"></param>
    /// <param name="percentRatio"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    private async Task PatchCompressedPackageDiffFilesAsync(GameInstallContext task, GameInstallFile file, double percentRatio, CancellationToken cancellationToken = default)
    {

        Lock _lock = new();
        string hdiffmap = Path.Combine(task.InstallPath, "hdiffmap.json");
        if (File.Exists(hdiffmap))
        {
            string content = await File.ReadAllTextAsync(hdiffmap, cancellationToken);
            DiffMap? diffmap = JsonSerializer.Deserialize<DiffMap>(content);
            if (diffmap?.DiffMapItems is not null)
            {
                double increase = percentRatio / diffmap.DiffMapItems.Count;
                Parallel.ForEach(diffmap.DiffMapItems, new ParallelOptions { CancellationToken = cancellationToken }, item =>
                {
                    string source = Path.GetFullPath(Path.Join(task.InstallPath, item.SourceFileName));
                    string target = Path.GetFullPath(Path.Join(task.InstallPath, item.TargetFileName));
                    string diff = Path.GetFullPath(Path.Join(task.InstallPath, item.PatchFileName));
                    if (File.Exists(source) && File.Exists(diff))
                    {
                        using FileStream fs_source = File.OpenRead(source);
                        using FileStream fs_diff = File.OpenRead(diff);
                        string target_tmp = $"{target}_tmp";
                        using FileStream fs_target = File.Create(target_tmp);
                        bool result = HPatch.PatchZstandard(fs_source, fs_diff, fs_target);
                        if (result)
                        {
                            fs_target.Dispose();
                            File.Move(target_tmp, target, true);
                            if (File.Exists(diff))
                            {
                                File.Delete(diff);
                            }
                            if (source != target && File.Exists(source))
                            {
                                File.Delete(source);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Patch file failed.\nSource -> Target: {source} -> {target}", item.SourceFileName, item.TargetFileName);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Source file not found {file}", source);
                    }
                    lock (_lock)
                    {
                        task.Progress_Percent += increase;
                    }
                });
                _logger.LogInformation("GameInstallTask ({GameBiz}): Patching compressed package {count} diff files by hdiffmap.json", task.GameId.GameBiz, diffmap.DiffMapItems.Count);
            }
            File.Delete(hdiffmap);
        }

        var hdifffiles = Path.Combine(task.InstallPath, "hdifffiles.txt");
        if (File.Exists(hdifffiles))
        {
            using FileStream fs = File.OpenRead(hdifffiles);
            List<HDiffFile> files = await DeserilizerLinesAsync<HDiffFile>(fs, cancellationToken);
            double increase = percentRatio / files.Count;
            Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancellationToken }, item =>
            {
                string target = Path.GetFullPath(Path.Join(task.InstallPath, item.RemoteName));
                string diff = $"{target}.hdiff";
                if (File.Exists(target) && File.Exists(diff))
                {
                    using FileStream fs_source = File.OpenRead(target);
                    using FileStream fs_diff = File.OpenRead(diff);
                    string target_tmp = $"{target}_tmp";
                    using FileStream fs_target = File.Create(target_tmp);
                    bool result = HPatch.PatchZstandard(fs_source, fs_diff, fs_target);
                    if (result)
                    {
                        fs_target.Dispose();
                        File.Move(target_tmp, target, true);
                        if (File.Exists(diff))
                        {
                            File.Delete(diff);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Patch file failed.\nSource -> Target: {source} -> {target}", target, target);
                    }
                    lock (_lock)
                    {
                        task.Progress_Percent += increase;
                    }
                }
            });
            _logger.LogInformation("GameInstallTask ({GameBiz}): Patching compressed package {count} diff files by hdifffiles.txt", task.GameId.GameBiz, files.Count);
            File.Delete(hdifffiles);
        }

        var delete = Path.Combine(task.InstallPath, "deletefiles.txt");
        if (File.Exists(delete))
        {
            var deleteFiles = await File.ReadAllLinesAsync(delete, cancellationToken);
            int count = 0;
            foreach (string line in deleteFiles)
            {
                var target = Path.Combine(task.InstallPath, line);
                if (File.Exists(target))
                {
                    File.Delete(target);
                    count++;
                }
            }
            _logger.LogInformation("GameInstallTask ({GameBiz}): Deleting compressed package {count} files by deletefiles.txt", task.GameId.GameBiz, count);
            File.Delete(delete);
        }

    }



    /// <summary>
    /// 合并 patch 下载模式的 diff 文件
    /// </summary>
    /// <param name="task"></param>
    /// <param name="file"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task PatchDiffFileAsync(GameInstallContext task, GameInstallFile file, CancellationToken cancellationToken = default)
    {
        if (file.IsFinished || file.Patch is null)
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
        string ldiff = Path.Combine(task.InstallPath, "ldiff", file.Patch.Id);
        using FileSliceStream fs_ldiff = new FileSliceStream(ldiff, file.Patch.PatchOffset, file.Patch.PatchLength);
        string path_tmp = file.FullPath + "_tmp";
        using FileStream fs_tmp = File.Open(path_tmp, FileMode.Create, FileAccess.ReadWrite);
        if (string.IsNullOrWhiteSpace(file.Patch.OriginalFileFullPath))
        {
            bool result = false;
            if (file.Patch.Compression)
            {
                result = HPatch.PatchZstandard(null, fs_ldiff, fs_tmp);
            }
            else
            {
                await fs_ldiff.CopyToAsync(fs_tmp, cancellationToken);
                result = true;
            }
            if (result)
            {
                fs_tmp.Dispose();
                File.Move(path_tmp, file.FullPath, true);
            }
            else
            {
                _logger.LogWarning("Patch file failed. File: {file}", file.FullPath);
            }
        }
        else if (File.Exists(file.Patch.OriginalFileFullPath))
        {

            using FileStream fs_source = File.OpenRead(file.Patch.OriginalFileFullPath);
            bool result = HPatch.PatchZstandard(fs_source, fs_ldiff, fs_tmp);
            if (result)
            {
                fs_source.Dispose();
                fs_tmp.Dispose();
                File.Move(path_tmp, file.FullPath, true);
                if (file.Patch.OriginalFileFullPath != file.FullPath && File.Exists(file.Patch.OriginalFileFullPath))
                {
                    File.Delete(file.Patch.OriginalFileFullPath);
                }
            }
            else
            {
                _logger.LogWarning("Patch file failed. File: {file}", file.FullPath);
            }
        }
        else
        {
            _logger.LogWarning("Source file not found {file}", file.Patch.OriginalFileFullPath);
        }
    }



    /// <summary>
    /// 下载游戏渠道 SDK
    /// </summary>
    /// <param name="task"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task DownloadGameChannelSDKAsync(GameInstallContext task, CancellationToken cancellationToken = default)
    {
        if (task.GameChannelSDK is null)
        {
            return;
        }
        Version? version = null;
        string? configPath = Path.Join(task.InstallPath, "config.ini");
        if (File.Exists(configPath))
        {
            string text = await File.ReadAllTextAsync(configPath, cancellationToken);
            _ = Version.TryParse(SdkVersionRegex().Match(text).Groups[1].Value, out version);
        }
        if (version?.ToString() == task.GameChannelSDK.Version)
        {
            bool checkError = false;
            if (!string.IsNullOrWhiteSpace(task.GameChannelSDK.PkgVersionFileName))
            {
                string pkg_version = Path.Combine(task.InstallPath, task.GameChannelSDK.PkgVersionFileName);
                if (File.Exists(pkg_version))
                {
                    using FileStream fs_pkg = File.Open(pkg_version, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    foreach (PkgVersionItem item in await DeserilizerLinesAsync<PkgVersionItem>(fs_pkg, cancellationToken))
                    {
                        if (item is not null)
                        {
                            string file = Path.Combine(task.InstallPath, item.RemoteName);
                            if (!await CheckFileMD5Async(task, file, item.FileSize, item.MD5, cancellationToken))
                            {
                                checkError = true;
                            }
                        }
                    }
                }
                else
                {
                    checkError = true;
                }
            }
            if (!checkError)
            {
                return;
            }
        }

        long size = task.GameChannelSDK.ChannelSDKPackage.Size;
        string url = task.GameChannelSDK.ChannelSDKPackage.Url;
        string md5 = task.GameChannelSDK.ChannelSDKPackage.MD5;
        string path = Path.Combine(task.InstallPath, Path.GetFileName(url));
        await DownloadToFileAsync(task, path, url, size, md5, cancellationToken);
        using var archive = new SharpSevenZipExtractor(path);
        archive.ExtractArchive(task.InstallPath);
        archive.Dispose();
        File.Delete(path);
    }


    [GeneratedRegex(@"sdk_version=(.+)")]
    private static partial Regex SdkVersionRegex();



    public static async Task<List<T>> DeserilizerLinesAsync<T>(Stream stream, CancellationToken cancellationToken = default) where T : class
    {
        List<T> list = new();
        using StreamReader sr = new(stream, leaveOpen: true);
        string? line;
        while (!string.IsNullOrWhiteSpace(line = await sr.ReadLineAsync(cancellationToken)))
        {
            T? item = JsonSerializer.Deserialize<T>(line);
            if (item is not null)
            {
                list.Add(item);
            }
        }
        return list;
    }




    public async Task DownloadWPFPackageAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        if (context.WPFPackage is null)
        {
            return;
        }
        string config = Path.Combine(context.InstallPath, "config.ini");
        if (File.Exists(config))
        {
            string content = await File.ReadAllTextAsync(config, cancellationToken);
            string wpfVersion = Regex.Match(content, @"wpf_version=(.+)").Groups[1].Value.Trim();
            if (wpfVersion == context.WPFPackage.Version)
            {
                return;
            }
        }

        long size = context.WPFPackage.Size;
        string url = context.WPFPackage.Url;
        string md5 = context.WPFPackage.MD5;
        context.Progress_DownloadTotalBytes = size;
        context.Progress_DownloadFinishBytes = 0;
        context.Progress_WriteTotalBytes = 0;
        context.Progress_WriteFinishBytes = 0;
        string name = Path.GetFileName(url);
        int i = name.IndexOf('?');
        if (i > 0)
        {
            name = name[..i];
        }
        string path = Path.Combine(context.InstallPath, name);
        await DownloadToFileAsync(context, path, url, size, md5, cancellationToken);
        using var archive = new SharpSevenZipExtractor(path);
        archive.ExtractArchive(context.InstallPath);
        archive.Dispose();
        File.Delete(path);
    }



}
