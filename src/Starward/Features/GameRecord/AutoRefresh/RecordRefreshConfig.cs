using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 一个「账号 + 数据板块」的自动更新配置。整体序列化成 JSON 存进 Setting 表的一个键，
/// 避免每项配置各占一行导致键爆炸。
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
    /// 用户打开开关的时刻（UTC ticks）。只作记录，不参与排期：还没成功跑过就视为到期，下次启动先补档。
    /// </summary>
    [JsonPropertyName("enabledAt")]
    public long EnabledTicks { get; set; }


    /// <summary>上次成功自动更新的时刻，从未更新过时为 null。</summary>
    [JsonIgnore]
    public DateTimeOffset? LastRunTime => LastRunTicks > 0 ? new DateTimeOffset(LastRunTicks, TimeSpan.Zero) : null;


    /// <summary>
    /// 排期基准日：只用上次成功更新日。从未跑过时为 null，视为立即到期，先补一轮档案。
    /// </summary>
    /// <returns>上次成功更新的 UTC+8 日期；从未跑过时为 null。</returns>
    private DateOnly? GetBaselineDate()
    {
        if (LastRunTime is DateTimeOffset lastRun)
        {
            return RecordRefreshSchedule.GetServerDate(lastRun);
        }
        return null;
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
        return RecordRefreshSchedule.IsDue(ToSettings(), today, GetBaselineDate());
    }


    /// <summary>
    /// 下次该更新的日期。从未成功跑过时返回今天，与启动检查的「先补档」一致。
    /// </summary>
    /// <param name="utcNow">当前 UTC 时刻。</param>
    /// <returns>下次到期的日历日。</returns>
    public DateOnly GetNextDueDate(DateTimeOffset utcNow)
    {
        DateOnly today = RecordRefreshSchedule.GetServerDate(utcNow);
        return RecordRefreshSchedule.GetNextDueDate(ToSettings(), today, GetBaselineDate());
    }

}


/// <summary>
/// 自动更新配置的存取：按「游戏区服 + uid + 数据板块」一把钥匙，落在 Setting 表。
/// </summary>
public static class RecordRefreshConfigStore
{

    /// <summary>
    /// 读取指定账号在指定数据板块上的配置；没配置过时返回一份默认值（未启用）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="item">数据板块。</param>
    /// <returns>配置对象，永不为 null。</returns>
    public static RecordRefreshConfig Load(GameRecordRole role, RecordRefreshItem item)
    {
        return Load(role.GameBiz, role.Uid, item);
    }


    /// <summary>
    /// 读取指定账号在指定数据板块上的配置；没配置过时返回一份默认值（未启用）。
    /// </summary>
    /// <param name="biz">游戏业务线。</param>
    /// <param name="uid">角色 uid。</param>
    /// <param name="item">数据板块。</param>
    /// <returns>配置对象，永不为 null。</returns>
    public static RecordRefreshConfig Load(GameBiz biz, long uid, RecordRefreshItem item)
    {
        string? json = AppConfig.GetValue<string>(default, GetKey(biz, uid, item));
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
            AppConfig.GetLogger<RecordRefreshConfig>().LogWarning(ex, "Parse record refresh config failed ({biz}, {uid}, {item}).", biz, uid, item);
            return new RecordRefreshConfig();
        }
    }


    /// <summary>
    /// 保存指定账号在指定数据板块上的配置。
    /// </summary>
    /// <param name="biz">游戏业务线。</param>
    /// <param name="uid">角色 uid。</param>
    /// <param name="item">数据板块。</param>
    /// <param name="config">要保存的配置。</param>
    public static void Save(GameBiz biz, long uid, RecordRefreshItem item, RecordRefreshConfig config)
    {
        AppConfig.SetValue(JsonSerializer.Serialize(config), GetKey(biz, uid, item));
    }


    /// <summary>
    /// 配置在 Setting 表中的键名。
    /// </summary>
    private static string GetKey(GameBiz biz, long uid, RecordRefreshItem item)
    {
        return $"auto_record_refresh_{item}_{biz}_{uid}";
    }

}
