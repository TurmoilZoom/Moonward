using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 一个自动更新任务的配置（账号 + 数据板块 + 月份）。整体序列化成 JSON 存进 Setting 表的一个键，
/// 避免每项配置各占一行导致键爆炸。
/// <para>
/// 月报类板块的「当月」「上月」各存一份，所以这里没有月份字段——月份是键的一部分，见
/// <see cref="RecordRefreshConfigStore"/>。
/// </para>
/// </summary>
public class RecordRefreshConfig
{

    /// <summary>是否对这个账号的这个数据板块自动更新。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>频率模式。</summary>
    [JsonPropertyName("mode")]
    public RecordRefreshMode Mode { get; set; }

    /// <summary>「每隔 N 天」的天数。</summary>
    [JsonPropertyName("interval")]
    public int IntervalDays { get; set; } = 3;

    /// <summary>「每周」模式的星期几，取值同 <see cref="System.DayOfWeek"/>（0 为周日）。</summary>
    [JsonPropertyName("dayOfWeek")]
    public int DayOfWeek { get; set; } = 1;

    /// <summary>「每月」模式的日期（1-31），超出当月天数时落到月末。</summary>
    [JsonPropertyName("dayOfMonth")]
    public int DayOfMonth { get; set; } = 1;

    /// <summary>上次成功自动更新的时刻（UTC ticks），0 表示从未更新过。</summary>
    [JsonPropertyName("lastRun")]
    public long LastRunTicks { get; set; }

    /// <summary>
    /// 最后一次改动排期设置（打开开关、换频率）的时刻（UTC ticks）。
    /// 改动之后一律从这天按新频率重排，当天若命中就算到期，不看当天是否已经更新过。
    /// JSON 键沿用旧的 <c>enabledAt</c>：老配置里存的正是打开开关的时刻，语义相容。
    /// </summary>
    [JsonPropertyName("enabledAt")]
    public long ScheduleBaseTicks { get; set; }


    /// <summary>上次成功自动更新的时刻，从未更新过时为 null。</summary>
    [JsonIgnore]
    public DateTimeOffset? LastRunTime => LastRunTicks > 0 ? new DateTimeOffset(LastRunTicks, TimeSpan.Zero) : null;


    /// <summary>
    /// 记下「排期设置刚被改过」：打开开关或改频率后调用，之后的排期从今天按新频率重算。
    /// </summary>
    public void MarkScheduleChanged()
    {
        ScheduleBaseTicks = DateTimeOffset.UtcNow.UtcTicks;
    }


    /// <summary>
    /// 排期基准：改设置之后又成功跑过就以那次成功为准（同一周期内不再重复跑），
    /// 否则以最后一次改设置的日子为准（当天命中就算到期）。
    /// </summary>
    /// <param name="today">当前 UTC+8 日期。</param>
    /// <returns>排期基准。</returns>
    private RecordRefreshBaseline GetBaseline(DateOnly today)
    {
        if (LastRunTime is DateTimeOffset lastRun && LastRunTicks >= ScheduleBaseTicks)
        {
            return new RecordRefreshBaseline(RecordRefreshSchedule.GetServerDate(lastRun), true);
        }
        long baseTicks = ScheduleBaseTicks > 0 ? ScheduleBaseTicks : LastRunTicks;
        if (baseTicks <= 0)
        {
            // 老配置里两个时刻都没有：当作今天刚改过，按所选频率往后排
            return new RecordRefreshBaseline(today, false);
        }
        DateOnly baseDate = RecordRefreshSchedule.GetServerDate(new DateTimeOffset(baseTicks, TimeSpan.Zero));
        // 时钟被往回调过：基准日跑到未来会让排期永远不到期，退回今天
        return new RecordRefreshBaseline(baseDate > today ? today : baseDate, false);
    }


    /// <summary>供排期计算使用的参数快照。</summary>
    /// <returns>排期参数。</returns>
    internal RecordRefreshSettings ToSettings()
    {
        return new RecordRefreshSettings(
            Mode,
            RecordRefreshSchedule.ClampIntervalDays(IntervalDays),
            (DayOfWeek)Math.Clamp(DayOfWeek, 0, 6),
            RecordRefreshSchedule.ClampDayOfMonth(DayOfMonth));
    }


    /// <summary>
    /// 今天是否该更新。未启用一律返回 false。
    /// </summary>
    /// <param name="utcNow">当前 UTC 时刻。</param>
    /// <returns>该更新返回 true。</returns>
    public bool IsDue(DateTimeOffset utcNow)
    {
        if (!Enabled)
        {
            return false;
        }
        DateOnly today = RecordRefreshSchedule.GetServerDate(utcNow);
        return RecordRefreshSchedule.IsDue(ToSettings(), today, GetBaseline(today));
    }


    /// <summary>
    /// 下次该更新的日期。与 <see cref="IsDue(DateTimeOffset)"/> 共用同一套算法与基准，
    /// 所以浮层里显示的日期就是启动检查会认的那天。
    /// </summary>
    /// <param name="utcNow">当前 UTC 时刻。</param>
    /// <returns>下次到期的日历日。</returns>
    public DateOnly GetNextDueDate(DateTimeOffset utcNow)
    {
        DateOnly today = RecordRefreshSchedule.GetServerDate(utcNow);
        return RecordRefreshSchedule.GetNextDueDate(ToSettings(), today, GetBaseline(today));
    }

}


/// <summary>
/// 自动更新配置的存取：按「游戏区服 + uid + 数据板块 + 月份」一把钥匙，落在 Setting 表。
/// </summary>
public static class RecordRefreshConfigStore
{

    /// <summary>
    /// 读取一个任务的配置；没配置过时返回一份默认值（未启用）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="item">数据板块。</param>
    /// <param name="monthTarget">月报类板块的月份；其余板块保持默认。</param>
    /// <returns>配置对象，永不为 null。</returns>
    public static RecordRefreshConfig Load(GameRecordRole role, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget = RecordRefreshMonthTarget.Current)
    {
        return Load(role.GameBiz, role.Uid, item, monthTarget);
    }


    /// <summary>
    /// 读取一个任务的配置；没配置过时返回一份默认值（未启用）。
    /// </summary>
    /// <param name="biz">游戏业务线。</param>
    /// <param name="uid">角色 uid。</param>
    /// <param name="item">数据板块。</param>
    /// <param name="monthTarget">月报类板块的月份；其余板块保持默认。</param>
    /// <returns>配置对象，永不为 null。</returns>
    public static RecordRefreshConfig Load(GameBiz biz, long uid, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget = RecordRefreshMonthTarget.Current)
    {
        string? json = AppConfig.GetValue<string>(default, GetKey(biz, uid, item, monthTarget));
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RecordRefreshConfig();
        }
        try
        {
            return JsonSerializer.Deserialize<RecordRefreshConfig>(json) ?? new RecordRefreshConfig();
        }
        catch (Exception ex)
        {
            AppConfig.GetLogger<RecordRefreshConfig>().LogWarning(ex, "Parse record refresh config failed ({biz}, {uid}, {item}, {month}).", biz, uid, item, monthTarget);
            return new RecordRefreshConfig();
        }
    }


    /// <summary>
    /// 保存一个任务的配置。
    /// </summary>
    /// <param name="biz">游戏业务线。</param>
    /// <param name="uid">角色 uid。</param>
    /// <param name="item">数据板块。</param>
    /// <param name="config">要保存的配置。</param>
    /// <param name="monthTarget">月报类板块的月份；其余板块保持默认。</param>
    public static void Save(GameBiz biz, long uid, RecordRefreshItem item, RecordRefreshConfig config, RecordRefreshMonthTarget monthTarget = RecordRefreshMonthTarget.Current)
    {
        AppConfig.SetValue(JsonSerializer.Serialize(config), GetKey(biz, uid, item, monthTarget));
    }


    /// <summary>
    /// 配置在 Setting 表中的键名。
    /// <para>
    /// 「当月」不加后缀，与月报拆成两个任务之前的键一字不差——老用户已经配好的那份自然变成当月任务，不需要迁移。
    /// </para>
    /// </summary>
    private static string GetKey(GameBiz biz, long uid, RecordRefreshItem item, RecordRefreshMonthTarget monthTarget)
    {
        string suffix = monthTarget is RecordRefreshMonthTarget.Current ? string.Empty : $"_{monthTarget}";
        return $"auto_record_refresh_{item}{suffix}_{biz}_{uid}";
    }

}
