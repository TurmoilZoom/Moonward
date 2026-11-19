using System;
using System.Collections.Generic;

namespace Starward.Features.GameRecord.DailyNote.Notify;

/// <summary>
/// 便笺里一个可提醒数值的快照。
/// </summary>
/// <param name="Current">当前值。</param>
/// <param name="Max">自然恢复的上限。</param>
/// <param name="TimeToFull">接口给的「距离回满」时长；不会自然恢复或接口不给时为 null。</param>
public readonly record struct DailyNoteMetric(int Current, int Max, TimeSpan? TimeToFull);


/// <summary>
/// 一条提醒规则对一份快照的判定结果。
/// </summary>
/// <param name="Reached">当前是否已达到阈值。</param>
/// <param name="ShouldNotify">这次是否该发通知：刚达到且之前没提醒过。</param>
/// <param name="Threshold">实际生效的阈值。</param>
/// <param name="TimeToThreshold">推算还要多久达到阈值；已达到或无法推算时为 null。</param>
public readonly record struct DailyNoteNotifyDecision(bool Reached, bool ShouldNotify, int Threshold, TimeSpan? TimeToThreshold);


/// <summary>
/// 便笺提醒的纯计算部分：阈值判定、到点推算、下次检查时刻。不依赖数据库与 WinUI，单元测试直接链接本文件。
/// </summary>
public static class DailyNoteNotifyEvaluator
{

    /// <summary>
    /// 实际生效的阈值：没设置、非正数或不小于上限时一律按上限（「满了提醒」）。超过上限的数值永远等不到自然恢复，按上限处理。
    /// </summary>
    /// <param name="configured">用户设置的阈值，null 表示上限。</param>
    /// <param name="max">当前上限。</param>
    /// <returns>生效阈值。</returns>
    public static int ResolveThreshold(int? configured, int max)
    {
        return configured is int value && value > 0 && value < max ? value : max;
    }


    /// <summary>
    /// 判定一条规则：达到阈值且此前没提醒过才通知（边沿触发），没达到时给出推算的到点时长。
    /// </summary>
    /// <param name="metric">数值快照。</param>
    /// <param name="configuredThreshold">用户设置的阈值，null 表示上限。</param>
    /// <param name="notifiedBefore">上次达到阈值后是否已提醒过、且之后一直没掉回阈值以下。</param>
    /// <returns>判定结果。</returns>
    public static DailyNoteNotifyDecision Evaluate(DailyNoteMetric metric, int? configuredThreshold, bool notifiedBefore)
    {
        int threshold = ResolveThreshold(configuredThreshold, metric.Max);
        bool reached = metric.Max > 0 && metric.Current >= threshold;
        TimeSpan? eta = reached ? null : EstimateTimeToThreshold(metric, threshold);
        return new DailyNoteNotifyDecision(reached, reached && !notifiedBefore, threshold, eta);
    }


    /// <summary>
    /// 推算还要多久达到阈值。接口只给「距离回满」，按剩余缺口线性折算；
    /// 当前这一点已恢复的进度未知，误差不超过一点的恢复时长，所以到点后要再拉一次核对。
    /// </summary>
    /// <param name="metric">数值快照。</param>
    /// <param name="threshold">生效阈值。</param>
    /// <returns>到点时长；已达到为 <see cref="TimeSpan.Zero"/>；无法推算为 null。</returns>
    public static TimeSpan? EstimateTimeToThreshold(DailyNoteMetric metric, int threshold)
    {
        if (metric.Current >= threshold)
        {
            return TimeSpan.Zero;
        }
        if (metric.TimeToFull is not { } toFull || toFull <= TimeSpan.Zero || metric.Max <= metric.Current)
        {
            return null;
        }
        double ticksPerPoint = (double)toFull.Ticks / (metric.Max - metric.Current);
        return TimeSpan.FromTicks((long)(ticksPerPoint * (threshold - metric.Current)));
    }


    /// <summary>
    /// 下次检查时刻：平时按检查间隔，有项目预计更早到点时提前到「到点 + 确认延迟」，但不早于「现在 + 最小间隔」。
    /// </summary>
    /// <param name="fetchedAt">快照的获取时刻，推算的到点时长从它起算。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="interval">平时的检查间隔。</param>
    /// <param name="timesToThreshold">各项目推算的到点时长。</param>
    /// <param name="confirmDelay">到点后再等多久去核对，吸收线性折算的误差。</param>
    /// <param name="minGap">两次检查之间的最小间隔，防止推算偏早时连续请求。</param>
    /// <returns>下次检查时刻。</returns>
    public static DateTimeOffset GetNextCheck(DateTimeOffset fetchedAt, DateTimeOffset now, TimeSpan interval,
                                              IEnumerable<TimeSpan> timesToThreshold, TimeSpan confirmDelay, TimeSpan minGap)
    {
        DateTimeOffset next = now + interval;
        foreach (TimeSpan eta in timesToThreshold)
        {
            DateTimeOffset candidate = fetchedAt + eta + confirmDelay;
            if (candidate < next)
            {
                next = candidate;
            }
        }
        DateTimeOffset earliest = now + minGap;
        return next < earliest ? earliest : next;
    }

}
