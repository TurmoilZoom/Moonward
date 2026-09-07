using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Helpers;

/// <summary>
/// 窗口不可见（隐藏到托盘 / 最小化）时把内存交还系统。
/// <para>
/// 2026-09-07 对切换数次游戏后驻留托盘的进程实测：私有内存 731MB 里，102 个 NT 堆段占 353MB
///（同机上游 Starward 为 49 段 203MB），托管堆只有 20MB——内存大头在本机堆与显存侧，
/// 单靠 <see cref="GC.Collect()"/> 收不回什么。故这里在托管回收之外多做两件事：
/// 请系统把各堆的空闲块与 LFH 缓存交还（能退多少取决于碎片程度，切换游戏留下的堆段多半退不掉，
/// 真正的解法是少产生那些大块分配），以及立刻清空工作集——隐藏后系统本来也会慢慢削，
/// 这里只是不必等它。
/// </para>
/// </summary>
public static partial class MemoryTrimmer
{

    /// <summary>隐藏后延迟多久执行，等 MediaPlayer / Win2D / 页面的释放真正落地再回收。</summary>
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(3);

    /// <summary>两次回收的最小间隔，避免反复开合窗口时空转。</summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(20);

    /// <summary>上次回收完成的时间戳（<see cref="Environment.TickCount64"/>），0 表示还没回收过。</summary>
    private static long _lastTrimTick;

    /// <summary>回收进行中的标记，0 空闲 / 1 进行中。</summary>
    private static int _running;


    /// <summary>
    /// 延迟执行一次内存回收。须在 UI 线程调用：延迟结束后先回到 UI 线程询问
    /// <paramref name="canTrim"/>，确认窗口仍不可见才真正回收（其间用户可能已经把窗口开回来）。
    /// </summary>
    /// <param name="canTrim">延迟结束后的确认回调，返回 false 则放弃本次回收；为 null 时不确认。</param>
    /// <param name="delay">延迟时长，默认 <see cref="DefaultDelay"/>。</param>
    public static void TrimLater(Func<bool>? canTrim = null, TimeSpan? delay = null)
    {
        _ = TrimLaterAsync(canTrim, delay);
    }


    /// <summary>
    /// <see cref="TrimLater"/> 的实现：等待 → 确认 → 到线程池执行回收。
    /// </summary>
    /// <param name="canTrim">延迟结束后的确认回调。</param>
    /// <param name="delay">延迟时长。</param>
    private static async Task TrimLaterAsync(Func<bool>? canTrim, TimeSpan? delay)
    {
        try
        {
            if (InCooldown())
            {
                return;
            }
            // 不加 ConfigureAwait(false)：要回到调用方（UI 线程）才能安全读取窗口状态。
            await Task.Delay(delay ?? DefaultDelay);
            if (canTrim?.Invoke() is false)
            {
                return;
            }
            // 堆整理会逐个锁住进程内的堆，放到线程池执行，别卡住 UI 线程。
            await Task.Run(Trim).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GetLogger().LogError(ex, "Trim process memory");
        }
    }


    /// <summary>
    /// 立即执行一次内存回收：压缩托管堆 → 归还本机堆空闲块与 LFH 缓存 → 清空工作集。
    /// 同一时刻只会有一次在跑，且与上次至少间隔 <see cref="MinInterval"/>。
    /// </summary>
    public static void Trim()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) is not 0)
        {
            return;
        }
        try
        {
            if (InCooldown())
            {
                return;
            }
            long before = GetPrivateBytes();
            long stamp = Stopwatch.GetTimestamp();

            // 第一趟回收 + 等待终结器：WinRT / COM 包装器要在终结之后才会释放它们持有的本机内存，
            // 少了这一步，紧接着的堆整理看到的仍是「还被引用着」的块。
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            OptimizeNativeHeaps();
            TrimWorkingSet();
            _lastTrimTick = Environment.TickCount64;

            long after = GetPrivateBytes();
            GetLogger().LogInformation("Trim process memory: private {before} MB -> {after} MB, elapsed {ms} ms",
                                       before >> 20, after >> 20, (int)Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            GetLogger().LogError(ex, "Trim process memory");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }


    /// <summary>
    /// 是否处于两次回收之间的冷却期。<see cref="_lastTrimTick"/> 为 0（从未回收）时直接放行，
    /// 不能拿它去做减法——那样第一次调用就会被当成刚回收过而跳过。
    /// </summary>
    /// <returns>距上次回收不足 <see cref="MinInterval"/> 时为 true。</returns>
    private static bool InCooldown()
    {
        return _lastTrimTick is not 0 && Environment.TickCount64 - _lastTrimTick < MinInterval.TotalMilliseconds;
    }


    /// <summary>
    /// 让进程内所有堆整理 LFH 缓存并把能退的空闲块解除提交（<c>HeapHandle</c> 传 NULL 即作用于全部堆）。
    /// 这是本机堆段唯一的归还途径，托管侧的 GC 管不到。
    /// </summary>
    private static void OptimizeNativeHeaps()
    {
        var info = new HEAP_OPTIMIZE_RESOURCES_INFORMATION { Version = HEAP_OPTIMIZE_RESOURCES_CURRENT_VERSION };
        if (!HeapSetInformation(IntPtr.Zero, HeapOptimizeResources, in info, (nuint)Marshal.SizeOf<HEAP_OPTIMIZE_RESOURCES_INFORMATION>()))
        {
            GetLogger().LogDebug("HeapSetInformation(HeapOptimizeResources) failed: {error}", Marshal.GetLastPInvokeError());
        }
    }


    /// <summary>
    /// 清空工作集，把已提交但用不到的页交还给系统（窗口不可见，重新换入的代价可以接受）。
    /// </summary>
    private static void TrimWorkingSet()
    {
        if (!EmptyWorkingSet(GetCurrentProcess()))
        {
            GetLogger().LogDebug("EmptyWorkingSet failed: {error}", Marshal.GetLastPInvokeError());
        }
    }


    /// <summary>
    /// 读取当前进程的私有提交内存，用于记录回收前后的差值。
    /// </summary>
    /// <returns>私有内存字节数；读取失败返回 0。</returns>
    private static long GetPrivateBytes()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64;
        }
        catch
        {
            return 0;
        }
    }


    /// <summary>缓存的日志记录器，首次使用时创建。</summary>
    private static ILogger? _logger;


    /// <summary>
    /// 取本类的日志记录器。静态类不能作为 <see cref="ILogger{T}"/> 的类型参数，故按名称创建。
    /// </summary>
    private static ILogger GetLogger()
    {
        return _logger ??= AppConfig.GetService<ILoggerFactory>().CreateLogger(nameof(MemoryTrimmer));
    }


    #region Native

    /// <summary>HEAP_INFORMATION_CLASS.HeapOptimizeResources</summary>
    private const int HeapOptimizeResources = 3;

    /// <summary>HEAP_OPTIMIZE_RESOURCES_INFORMATION.Version 当前唯一合法值。</summary>
    private const uint HEAP_OPTIMIZE_RESOURCES_CURRENT_VERSION = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct HEAP_OPTIMIZE_RESOURCES_INFORMATION
    {
        public uint Version;
        public uint Flags;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HeapSetInformation(IntPtr heapHandle,
                                                  int heapInformationClass,
                                                  in HEAP_OPTIMIZE_RESOURCES_INFORMATION heapInformation,
                                                  nuint heapInformationLength);

    [LibraryImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr process);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    #endregion

}
