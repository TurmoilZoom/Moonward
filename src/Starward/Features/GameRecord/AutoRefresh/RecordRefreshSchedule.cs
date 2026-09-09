using System;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 自动更新战绩的频率模式。存库时按枚举名写入 Setting 表。
/// </summary>
public enum RecordRefreshMode
{

    /// <summary>每隔 N 天，以「上次更新日期」为基准往后滚。</summary>
    EveryDays = 0,

    /// <summary>每周固定星期几。</summary>
    Weekly = 1,

    /// <summary>每月固定日期；该日期超出当月天数时落到月末。</summary>
    Monthly = 2,

}


/// <summary>
/// 一次自动更新的排期参数快照。
/// </summary>
/// <param name="Mode">频率模式。</param>
/// <param name="IntervalDays">每隔 N 天模式的天数。</param>
/// <param name="DayOfWeek">每周模式的星期几。</param>
/// <param name="DayOfMonth">每月模式的日期（1-31）。</param>
internal readonly record struct RecordRefreshSettings(RecordRefreshMode Mode, int IntervalDays, DayOfWeek DayOfWeek, int DayOfMonth);


/// <summary>
/// 自动更新战绩的排期计算：只回答「今天该不该跑」与「下一次是哪天」，不联网、不进 DI。
/// <para>
/// 日期一律按 UTC+8 日历日，与自动签到（<see cref="SignIn.SignInSchedule"/>）保持同一套「天」的定义。
/// 战绩接口本身不分日界，这里只是需要一个不随用户时区漂移的排期基准。
/// </para>
/// <para>
/// 判定只在软件启动时做一次，所以这里没有「下一个检查点」的概念：到期日错过了就等下次启动补上。
/// </para>
/// </summary>
internal static class RecordRefreshSchedule
{

    /// <summary>排期使用的时区偏移。</summary>
    public static readonly TimeSpan ServerOffset = TimeSpan.FromHours(8);

    /// <summary>「每隔 N 天」允许的最小天数。</summary>
    public const int MinIntervalDays = 1;

    /// <summary>「每隔 N 天」允许的最大天数。</summary>
    public const int MaxIntervalDays = 30;


    /// <summary>
    /// 将 UTC 时刻换算成排期用的日历日（UTC+8 的日期）。
    /// </summary>
    /// <param name="utcNow">UTC 时刻。</param>
    /// <returns>UTC+8 日历日。</returns>
    public static DateOnly GetServerDate(DateTimeOffset utcNow)
    {
        return DateOnly.FromDateTime(utcNow.ToOffset(ServerOffset).DateTime);
    }


    /// <summary>
    /// 今天是否该跑一轮。
    /// <para>
    /// <paramref name="lastRun"/> 为空（从未跑过 / 刚开启开关）时一律返回 true：先补一轮把数据种上，
    /// 之后再按所选频率对齐到星期几或几号。
    /// </para>
    /// </summary>
    /// <param name="settings">排期参数。</param>
    /// <param name="today">当前 UTC+8 日期。</param>
    /// <param name="lastRun">上次成功跑完一轮的 UTC+8 日期，从未跑过时为 null。</param>
    /// <returns>该跑返回 true。</returns>
    public static bool IsDue(RecordRefreshSettings settings, DateOnly today, DateOnly? lastRun)
    {
        if (lastRun is not DateOnly last)
        {
            return true;
        }
        // 用户把系统时间往回调过：上次运行日期在未来，按「已跑过」处理，等日期追上来
        if (last > today)
        {
            return false;
        }
        return settings.Mode switch
        {
            RecordRefreshMode.EveryDays => today >= last.AddDays(ClampIntervalDays(settings.IntervalDays)),
            RecordRefreshMode.Weekly => last < GetLastWeeklyDate(settings.DayOfWeek, today),
            RecordRefreshMode.Monthly => last < GetLastMonthlyDate(settings.DayOfMonth, today),
            _ => false,
        };
    }


    /// <summary>
    /// 下一次该跑的日期。今天就该跑时返回今天。
    /// </summary>
    /// <param name="settings">排期参数。</param>
    /// <param name="today">当前 UTC+8 日期。</param>
    /// <param name="lastRun">上次成功跑完一轮的 UTC+8 日期，从未跑过时为 null。</param>
    /// <returns>下次到期的日历日。</returns>
    public static DateOnly GetNextDueDate(RecordRefreshSettings settings, DateOnly today, DateOnly? lastRun)
    {
        if (IsDue(settings, today, lastRun))
        {
            return today;
        }
        return settings.Mode switch
        {
            RecordRefreshMode.EveryDays => lastRun!.Value.AddDays(ClampIntervalDays(settings.IntervalDays)),
            RecordRefreshMode.Weekly => GetLastWeeklyDate(settings.DayOfWeek, today).AddDays(7),
            RecordRefreshMode.Monthly => GetNextMonthlyDate(settings.DayOfMonth, GetLastMonthlyDate(settings.DayOfMonth, today)),
            _ => today,
        };
    }


    /// <summary>把「每隔 N 天」的天数夹到合法区间。</summary>
    /// <param name="days">用户配置的天数。</param>
    /// <returns>1–30 之间的天数。</returns>
    public static int ClampIntervalDays(int days) => Math.Clamp(days, MinIntervalDays, MaxIntervalDays);


    /// <summary>把「每月几号」夹到 1–31。</summary>
    /// <param name="day">用户配置的日期。</param>
    /// <returns>1–31 之间的日期。</returns>
    public static int ClampDayOfMonth(int day) => Math.Clamp(day, 1, 31);


    /// <summary>
    /// 今天（含）之前最近一个指定星期几的日期。
    /// </summary>
    private static DateOnly GetLastWeeklyDate(DayOfWeek dayOfWeek, DateOnly today)
    {
        int back = ((int)today.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return today.AddDays(-back);
    }


    /// <summary>
    /// 今天（含）之前最近一个指定「几号」的日期；该号数超出当月天数时取月末。
    /// </summary>
    private static DateOnly GetLastMonthlyDate(int dayOfMonth, DateOnly today)
    {
        DateOnly thisMonth = GetClampedMonthDate(today.Year, today.Month, dayOfMonth);
        if (thisMonth <= today)
        {
            return thisMonth;
        }
        DateOnly previous = today.AddMonths(-1);
        return GetClampedMonthDate(previous.Year, previous.Month, dayOfMonth);
    }


    /// <summary>
    /// <paramref name="reference"/> 之后的下一个「几号」。
    /// </summary>
    private static DateOnly GetNextMonthlyDate(int dayOfMonth, DateOnly reference)
    {
        DateOnly next = reference.AddMonths(1);
        return GetClampedMonthDate(next.Year, next.Month, dayOfMonth);
    }


    /// <summary>
    /// 指定年月的「几号」，超出当月天数时取月末（例如 2 月的 31 号取 28/29 号）。
    /// </summary>
    private static DateOnly GetClampedMonthDate(int year, int month, int dayOfMonth)
    {
        int day = Math.Min(ClampDayOfMonth(dayOfMonth), DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }

}
