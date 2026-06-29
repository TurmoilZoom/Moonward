namespace Starward.Core.GameRecord.StarRail.TrailblazeCalendar;

/// <summary>
/// 开拓月历「月份列表项」的轻量投影，仅含左侧月份列表渲染所需字段（月份 + 当月星琼总量）。
/// 点击某月时再用 <see cref="Uid"/> + <see cref="DataMonth"/> 查询该月完整数据。
/// </summary>
public class TrailblazeCalendarSummaryMonth
{

    /// <summary>游戏 UID，点击列表项后据此查询该月完整汇总。</summary>
    public long Uid { get; set; }

    /// <summary>月份，格式 <c>yyyyMM</c>（如 <c>202506</c>）。</summary>
    public string DataMonth { get; set; }

    /// <summary>当月星琼总量，仅用于列表项展示。</summary>
    public int CurrentHcoin { get; set; }

}