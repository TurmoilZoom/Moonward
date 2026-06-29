namespace Starward.Core.GameRecord.Genshin.TravelersDiary;

/// <summary>
/// 旅行札记「月份列表项」的轻量投影，仅含左侧月份列表渲染所需字段（年月 + 当月原石总量）。
/// 点击某月时再用 <see cref="Uid"/> + <see cref="Year"/> + <see cref="Month"/> 查询该月完整数据。
/// </summary>
public class TravelersDiarySummaryMonth
{

    /// <summary>游戏 UID，点击列表项后据此查询该月完整汇总。</summary>
    public long Uid { get; set; }

    /// <summary>年份。</summary>
    public int Year { get; set; }

    /// <summary>月份（1–12）。</summary>
    public int Month { get; set; }

    /// <summary>当月原石总量，仅用于列表项展示。</summary>
    public int CurrentPrimogems { get; set; }

}