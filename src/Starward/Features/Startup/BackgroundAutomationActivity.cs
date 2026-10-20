using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// 后台自动任务的活动登记，用来排优先级：自动签到、云游戏免费时长、战绩自动更新跑批量时登记一下，
/// 优先级更低的任务（定时提交绝区零抽卡元数据）在它们跑的时候让路，等跑完再安静一小会儿才开始。
/// </summary>
/// <remarks>
/// 只管「此刻有没有人在跑」，不预约排期：高优先级任务的到期时刻由各自的常驻循环决定，这里不感知。
/// </remarks>
internal static class BackgroundAutomationActivity
{

    /// <summary>等待空闲时的轮询间隔。低优先级任务一天才跑一次，轮询足够，不必为此引入事件。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static int _activeCount;

    /// <summary>最近一次有任务结束的时刻（UTC ticks），0 表示进程内还没有任务跑过。</summary>
    private static long _lastEndedTicks;


    /// <summary>当前是否有已登记的后台自动任务在跑。</summary>
    public static bool IsBusy => Volatile.Read(ref _activeCount) > 0;


    /// <summary>
    /// 登记一个正在运行的高优先级后台任务，释放返回值即结束登记。
    /// </summary>
    /// <returns>结束登记的作用域；重复释放无副作用。</returns>
    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _activeCount);
        return new ActivityScope();
    }


    /// <summary>
    /// 等到没有已登记的任务在跑、且距最近一个任务结束已过 <paramref name="quietPeriod"/>。
    /// 等满 <paramref name="maxWait"/> 仍不空闲就不再等：某个任务卡住不能把低优先级任务一起挂死。
    /// </summary>
    /// <param name="quietPeriod">任务结束后还要再安静多久，避免紧接着别人的最后一个请求连打。</param>
    /// <param name="maxWait">最长等待时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>确实等过（调用时并不空闲）为 true。</returns>
    public static async Task<bool> WaitForIdleAsync(TimeSpan quietPeriod, TimeSpan maxWait, CancellationToken cancellationToken = default)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        bool waited = false;
        while (!IsIdleFor(quietPeriod))
        {
            if (DateTimeOffset.UtcNow - start >= maxWait)
            {
                break;
            }
            waited = true;
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
        return waited;
    }


    /// <summary>
    /// 没有任务在跑，且最近一个任务已结束至少 <paramref name="quietPeriod"/>。
    /// </summary>
    private static bool IsIdleFor(TimeSpan quietPeriod)
    {
        if (IsBusy)
        {
            return false;
        }
        long ended = Interlocked.Read(ref _lastEndedTicks);
        return ended == 0 || DateTimeOffset.UtcNow - new DateTimeOffset(ended, TimeSpan.Zero) >= quietPeriod;
    }


    /// <summary>
    /// <see cref="Enter"/> 的作用域：释放时记下结束时刻并减计数。
    /// </summary>
    private sealed class ActivityScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }
            // 先记时刻再减计数：等待方看到计数归零时，结束时刻一定已是这次的
            Interlocked.Exchange(ref _lastEndedTicks, DateTimeOffset.UtcNow.UtcTicks);
            Interlocked.Decrement(ref _activeCount);
        }
    }

}
