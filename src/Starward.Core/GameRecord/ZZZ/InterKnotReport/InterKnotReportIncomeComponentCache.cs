namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报菲林收入构成本地缓存行，对应 SQLite <c>ZZZInterKnotReportIncomeComponent</c>。
/// </summary>
public class InterKnotReportIncomeComponentCache
{

    /// <summary>游戏 UID。</summary>
    public long Uid { get; set; }

    /// <summary>月份，格式 <c>yyyyMM</c>。</summary>
    public string DataMonth { get; set; }

    /// <summary>收入来源 action 标识（如 <c>daily_activity_rewards</c>）。</summary>
    public string Action { get; set; }

    /// <summary>该来源在查询月获得的菲林数量。</summary>
    public int Num { get; set; }

    /// <summary>该来源占当月菲林总量的百分比，单位 %。</summary>
    public int Percent { get; set; }

}