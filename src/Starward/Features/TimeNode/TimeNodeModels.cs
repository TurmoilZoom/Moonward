using System;
using System.Collections.Generic;

namespace Starward.Features.TimeNode;

/// <summary>
/// 时间节点整页快照（按游戏组装后的展示模型）。
/// </summary>
internal sealed class TimeNodeSnapshot
{

    public IReadOnlyList<TimeNodeSection> Sections { get; init; } = [];

}


/// <summary>
/// 一个展示分段（限时祈愿 / 活动跃迁 / 热点活动 / 调频）。
/// </summary>
internal sealed class TimeNodeSection
{

    public required string Title { get; init; }

    public IReadOnlyList<TimeNodeItem> Items { get; init; } = [];

}


/// <summary>
/// 倒计时展示粒度。
/// </summary>
public enum TimeNodeCountdownKind
{
    /// <summary>还有 X 天 Y 小时 Z 分钟 W 秒。</summary>
    Precise = 0,

    /// <summary>还剩 X 天 Y 小时（不足 1 小时显示分钟）。</summary>
    Coarse = 1,
}


/// <summary>
/// 单条时间节点（卡池或热点活动）。
/// </summary>
internal sealed class TimeNodeItem
{

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string? LinkUrl { get; init; }

    /// <summary>
    /// 封面图（热点活动左侧大图）；卡池条目可为空。
    /// </summary>
    public string? CoverIcon { get; init; }

    public TimeNodeCountdownKind CountdownKind { get; init; }

    /// <summary>
    /// 未开始时优先显示的固定文案（百科 <c>content_before_act</c>）。
    /// </summary>
    public string? ContentBeforeAct { get; init; }

    /// <summary>
    /// 已换算到目标区服的开始时刻；热点活动可为空。
    /// </summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>
    /// 已换算到目标区服的结束时刻。
    /// </summary>
    public DateTimeOffset EndTime { get; init; }

    public IReadOnlyList<TimeNodeIcon> Icons { get; init; } = [];

}


/// <summary>
/// 条目内小图标（角色 / 武器等）。
/// </summary>
public sealed class TimeNodeIcon
{

    public string Url { get; set; } = "";

    /// <summary>
    /// 可选稀有度角标（如 s / a）。
    /// </summary>
    public string? Level { get; set; }

}
