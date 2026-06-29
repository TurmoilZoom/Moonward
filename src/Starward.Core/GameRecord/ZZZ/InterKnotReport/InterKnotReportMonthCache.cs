namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报月度总量本地缓存行，对应 SQLite <c>ZZZInterKnotReportMonthData</c>。
/// 存储三类资源当月获取总量；名称字段来自 API <c>data_name</c>，供 UI 展示。
/// </summary>
public class InterKnotReportMonthCache
{

    /// <summary>游戏 UID。</summary>
    public long Uid { get; set; }

    /// <summary>月份，格式 <c>yyyyMM</c>。</summary>
    public string DataMonth { get; set; }

    /// <summary>当月菲林获取总量。</summary>
    public int PolychromeCount { get; set; }

    /// <summary>当月母带获取总量。</summary>
    public int MasterTapeCount { get; set; }

    /// <summary>当月邦布券获取总量。</summary>
    public int BooponCount { get; set; }

    /// <summary>菲林显示名称（API <c>data_name</c>）；为空时 UI 回退为 <see cref="DataType"/>。</summary>
    public string? PolychromeName { get; set; }

    /// <summary>母带显示名称（API <c>data_name</c>）。</summary>
    public string? MasterTapeName { get; set; }

    /// <summary>邦布券显示名称（API <c>data_name</c>）。</summary>
    public string? BooponName { get; set; }

}