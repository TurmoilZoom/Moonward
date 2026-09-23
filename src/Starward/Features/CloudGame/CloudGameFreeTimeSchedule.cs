using System;

namespace Starward.Features.CloudGame;

/// <summary>
/// 自动领取云游戏每日免费时长的调度用纯计算：日界、下次到期、当天重试。不联网、不进 DI。
/// </summary>
/// <remarks>
/// 日界不是零点：云·原神客户端内「了解云游戏」原文为「每日登录后可领取 15 分钟的免费时长（每日 4:00 UTC+8 刷新）」，
/// 云·绝区零同为每日登录领 15 分钟、上限 10 小时，同属一套云游戏平台，按同一日界处理。
/// <para>
/// 与每日签到不同，钱包接口不下发「今天」与「是否已领」，服务端日期无从对账，
/// 只能由本机时钟推算日界并记下已领的日子（见 <see cref="AppConfig.GetCloudGameFreeTimeClaimedDate"/>）。
/// 好在免费时长整日都能领，本机时钟略有偏差只是领取时刻前后挪动，不会漏掉某一天。
/// </para>
/// </remarks>
internal static class CloudGameFreeTimeSchedule
{

    /// <summary>云游戏日界时区（国服为 UTC+8）。</summary>
    public static readonly TimeSpan ServerOffset = TimeSpan.FromHours(8);

    /// <summary>每日免费时长的刷新时刻（UTC+8 4:00），日界按它划分而非零点。</summary>
    public const int ResetHour = 4;

    /// <summary>到期时刻附加的随机分钟数（含端点），避开整点齐发。</summary>
    public const int MinDailyJitterMinutes = 3;

    /// <summary>见 <see cref="MinDailyJitterMinutes"/>。</summary>
    public const int MaxDailyJitterMinutes = 8;


    /// <summary>
    /// 将 UTC 时刻换算成云游戏日历日：以 UTC+8 4:00 为日界，4:00 之前算前一天。
    /// </summary>
    /// <param name="utcNow">UTC 时刻。</param>
    /// <returns>该时刻所属的云游戏日历日。</returns>
    public static DateOnly GetServerDate(DateTimeOffset utcNow)
    {
        return DateOnly.FromDateTime(utcNow.ToOffset(ServerOffset).AddHours(-ResetHour).DateTime);
    }


    /// <summary>
    /// 下一个严格晚于 <paramref name="utcNow"/> 的日界（UTC+8 4:00），再加 3–8 分钟抖动。
    /// </summary>
    /// <param name="utcNow">UTC 时刻。</param>
    /// <returns>下次到期时刻。</returns>
    public static DateTimeOffset GetNextDailyDue(DateTimeOffset utcNow)
    {
        return GetNextResetTime(utcNow).AddMinutes(Random.Shared.Next(MinDailyJitterMinutes, MaxDailyJitterMinutes + 1));
    }


    /// <summary>
    /// 当天重试：重试时刻已跨过下一个日界时改排日界 + 抖动，否则 <paramref name="utcNow"/> + <paramref name="retry"/>。
    /// 免费时长整日都能领，不像签到那样需要「当天 23 点截止」的兜底。
    /// </summary>
    /// <param name="utcNow">调度时刻（UTC）。</param>
    /// <param name="retry">当天重试间隔。</param>
    /// <returns>下次到期时刻。</returns>
    public static DateTimeOffset GetRetryOrNextDay(DateTimeOffset utcNow, TimeSpan retry)
    {
        if (utcNow + retry >= GetNextResetTime(utcNow))
        {
            return GetNextDailyDue(utcNow);
        }
        return utcNow + retry;
    }


    /// <summary>
    /// 下一个严格晚于 <paramref name="utcNow"/> 的 UTC+8 4:00。
    /// </summary>
    private static DateTimeOffset GetNextResetTime(DateTimeOffset utcNow)
    {
        DateTimeOffset local = utcNow.ToOffset(ServerOffset);
        var todayReset = new DateTimeOffset(local.Year, local.Month, local.Day, ResetHour, 0, 0, ServerOffset);
        return todayReset > local ? todayReset : todayReset.AddDays(1);
    }

}
