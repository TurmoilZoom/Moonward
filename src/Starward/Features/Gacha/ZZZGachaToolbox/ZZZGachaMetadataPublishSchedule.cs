using System;

namespace Starward.Features.Gacha.ZZZGachaToolbox;

/// <summary>
/// 定时提交绝区零抽卡元数据的排期纯计算：每天在用户指定的 UTC+8 时刻跑一次，
/// 过了这个时刻还没成功（软件当时没开、跑失败了）就随时补跑，失败按退避重试且不越过下一次到点。不联网、不进 DI。
/// </summary>
/// <remarks>
/// 「期」= 某个到点时刻所在的 UTC+8 日期。到点前仍属于前一天那期，例如设定 12:00 时，
/// 10 月 20 日 11:59 属于 10 月 19 日那期、12:00 起属于 10 月 20 日那期。每期成功一次即可。
/// </remarks>
internal static class ZZZGachaMetadataPublishSchedule
{

    /// <summary>用户设定时刻所用的时区（UTC+8）。</summary>
    public static readonly TimeSpan ServerOffset = TimeSpan.FromHours(8);

    /// <summary>未设置时的默认时刻：12:00，避开绝区零版本更新维护（通常 6:00–11:00）。</summary>
    public static readonly TimeSpan DefaultTimeOfDay = TimeSpan.FromHours(12);

    /// <summary>连续失败的重试间隔，次数超出后一直用最后一档。</summary>
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
    ];


    /// <summary>
    /// 由「一天中的第几分钟」得到时刻；超出 0–1439 的值取默认时刻。
    /// </summary>
    /// <param name="minutes">设置里存的分钟数。</param>
    /// <returns>UTC+8 下的一天中的时刻（精确到分钟）。</returns>
    public static TimeSpan FromMinutes(int minutes)
    {
        return minutes is >= 0 and < 24 * 60 ? TimeSpan.FromMinutes(minutes) : DefaultTimeOfDay;
    }


    /// <summary>
    /// <paramref name="utcNow"/> 所属的期：已过当天的到点时刻算当天，否则算前一天。
    /// </summary>
    /// <param name="utcNow">当前时刻。</param>
    /// <param name="timeOfDay">UTC+8 到点时刻。</param>
    /// <returns>期对应的 UTC+8 日期。</returns>
    public static DateOnly GetCurrentPeriod(DateTimeOffset utcNow, TimeSpan timeOfDay)
    {
        DateTimeOffset local = utcNow.ToOffset(ServerOffset);
        var today = DateOnly.FromDateTime(local.DateTime);
        return local.TimeOfDay >= timeOfDay ? today : today.AddDays(-1);
    }


    /// <summary>
    /// 当前这期是否还没成功过。
    /// </summary>
    /// <param name="utcNow">当前时刻。</param>
    /// <param name="timeOfDay">UTC+8 到点时刻。</param>
    /// <param name="lastSuccessPeriod">上次成功的期；从未成功为 null。</param>
    /// <returns>需要跑时为 true。</returns>
    public static bool IsDue(DateTimeOffset utcNow, TimeSpan timeOfDay, DateOnly? lastSuccessPeriod)
    {
        return lastSuccessPeriod is not DateOnly last || last < GetCurrentPeriod(utcNow, timeOfDay);
    }


    /// <summary>
    /// 下一个严格晚于 <paramref name="utcNow"/> 的到点时刻。
    /// </summary>
    /// <param name="utcNow">当前时刻。</param>
    /// <param name="timeOfDay">UTC+8 到点时刻。</param>
    /// <returns>下次到点。</returns>
    public static DateTimeOffset GetNextSlot(DateTimeOffset utcNow, TimeSpan timeOfDay)
    {
        DateOnly next = GetCurrentPeriod(utcNow, timeOfDay).AddDays(1);
        return new DateTimeOffset(next.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay)), ServerOffset);
    }


    /// <summary>
    /// 下次应当运行的时刻：本期还没成功就是现在，否则是下次到点。
    /// </summary>
    /// <param name="utcNow">当前时刻。</param>
    /// <param name="timeOfDay">UTC+8 到点时刻。</param>
    /// <param name="lastSuccessPeriod">上次成功的期；从未成功为 null。</param>
    /// <returns>下次运行时刻。</returns>
    public static DateTimeOffset GetNextDue(DateTimeOffset utcNow, TimeSpan timeOfDay, DateOnly? lastSuccessPeriod)
    {
        return IsDue(utcNow, timeOfDay, lastSuccessPeriod) ? utcNow : GetNextSlot(utcNow, timeOfDay);
    }


    /// <summary>
    /// 失败后的重试时刻：<paramref name="utcNow"/> + <paramref name="delay"/>，但不晚于下次到点
    /// （到点后进入新的一期，本来就要重新跑）。
    /// </summary>
    /// <param name="utcNow">失败的时刻。</param>
    /// <param name="timeOfDay">UTC+8 到点时刻。</param>
    /// <param name="delay">重试间隔。</param>
    /// <returns>下次尝试时刻。</returns>
    public static DateTimeOffset GetRetry(DateTimeOffset utcNow, TimeSpan timeOfDay, TimeSpan delay)
    {
        DateTimeOffset retry = utcNow + delay;
        DateTimeOffset nextSlot = GetNextSlot(utcNow, timeOfDay);
        return retry < nextSlot ? retry : nextSlot;
    }


    /// <summary>
    /// 第 <paramref name="consecutiveFailures"/> 次连续失败后的重试间隔：1、5、15、30 分钟，之后每小时一次。
    /// </summary>
    /// <param name="consecutiveFailures">连续失败次数（从 1 起）。</param>
    /// <returns>重试间隔。</returns>
    public static TimeSpan GetRetryDelay(int consecutiveFailures)
    {
        return RetryDelays[Math.Clamp(consecutiveFailures - 1, 0, RetryDelays.Length - 1)];
    }

}
