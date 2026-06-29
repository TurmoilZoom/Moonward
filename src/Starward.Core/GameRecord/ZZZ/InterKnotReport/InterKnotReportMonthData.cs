using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报查询月的统计数据，对应 API <c>month_data</c>。
/// <see cref="List"/> 提供三类资源月度总量，<see cref="IncomeComponents"/> 提供菲林收入构成（饼图）。
/// </summary>
public class InterKnotReportMonthData
{
    /// <summary>三类资源（菲林 / 母带 / 邦布券）的月度获取总量列表。</summary>
    [JsonPropertyName("list")]
    public List<InterKnotReportSummaryAward> List { get; set; }

    /// <summary>菲林按收入来源分组的构成与占比，供饼图展示。</summary>
    [JsonPropertyName("income_components")]
    public List<InterKnotReportIncomeComponent> IncomeComponents { get; set; }
}