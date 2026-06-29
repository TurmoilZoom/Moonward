namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报「月份列表项」的轻量投影，仅含左侧月份列表渲染所需字段（月份 + 当月菲林总量）。
/// 由 SQL 直接从 <c>ZZZInterKnotReportMonthData</c> 读取，不加载收入构成等详情；
/// 点击某月时再用 <see cref="Uid"/> + <see cref="DataMonth"/> 查询该月完整数据（收入构成、明细等）。
/// </summary>
public class InterKnotReportSummaryMonth
{

    /// <summary>游戏 UID，点击列表项后据此查询该月完整汇总。</summary>
    public long Uid { get; set; }

    /// <summary>月份，格式 <c>yyyyMM</c>（如 <c>202506</c>）。</summary>
    public string DataMonth { get; set; }

    /// <summary>当月菲林（Polychrome）总量，仅用于列表项展示。</summary>
    public int PolychromeCount { get; set; }

}
