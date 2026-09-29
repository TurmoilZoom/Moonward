using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC.GameInstall;

/// <summary>
/// 一次 Chunk 模式下载共用的并发与内存限制。
/// 同一文件的块仍按偏移顺序写入（<c>_tmp</c> 文件长度即写入进度，暂停后可按块续传），
/// 但后面需要联网的块会提前并行下载到内存，写到该块时直接解压。
/// 这样同时下载的块数不再等于同时处理的文件数，只剩几个大文件时也能跑满带宽。
/// </summary>
internal sealed class ChunkDownloadScheduler : IDisposable
{

    /// <summary>
    /// 同时进行的块下载请求数。官方启动器由 getParamsConfig 下发 chunk_max_concurrent_tasks=12
    /// </summary>
    public const int MaxConcurrentDownloads = 16;

    /// <summary>
    /// 所有文件合计最多预读（下载中或已下载、尚未写入）的块数，用来限制内存占用
    /// </summary>
    public const int MaxPrefetchedChunks = 32;

    /// <summary>
    /// 单个文件最多预读的块数，避免一个文件占满全部预读名额
    /// </summary>
    public const int MaxPrefetchedChunksPerFile = 16;

    /// <summary>
    /// 缓冲区池里单个数组的最大长度；块解压前不超过 4 MiB，更大的数组不进池
    /// </summary>
    private const int MaxBufferLength = 16 << 20;


    private readonly SemaphoreSlim _downloadSlots = new(MaxConcurrentDownloads);

    private readonly SemaphoreSlim _prefetchSlots = new(MaxPrefetchedChunks);


    /// <summary>
    /// 块下载缓冲区。随本次下载一起释放，不留在进程级共享池里
    /// </summary>
    public ArrayPool<byte> BufferPool { get; } = ArrayPool<byte>.Create(MaxBufferLength, MaxPrefetchedChunks + MaxConcurrentDownloads);



    /// <summary>
    /// 等待一个下载名额，拿到后必须调用 <see cref="ReleaseDownloadSlot"/>
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task WaitDownloadSlotAsync(CancellationToken cancellationToken)
    {
        return _downloadSlots.WaitAsync(cancellationToken);
    }


    /// <summary>
    /// 归还下载名额
    /// </summary>
    public void ReleaseDownloadSlot()
    {
        _downloadSlots.Release();
    }


    /// <summary>
    /// 尝试占用一个预读名额，成功后必须调用 <see cref="ExitPrefetch"/>。
    /// 不能等待：等待的文件手里可能已有预读的块，所有文件都在等名额时就没有文件写入并释放名额了
    /// </summary>
    /// <returns>拿不到名额时返回 <see langword="false"/>，调用方不预读，写到该块时再下载</returns>
    public bool TryEnterPrefetch()
    {
        return _prefetchSlots.Wait(0);
    }


    /// <summary>
    /// 归还预读名额
    /// </summary>
    public void ExitPrefetch()
    {
        _prefetchSlots.Release();
    }


    public void Dispose()
    {
        _downloadSlots.Dispose();
        _prefetchSlots.Dispose();
    }

}
