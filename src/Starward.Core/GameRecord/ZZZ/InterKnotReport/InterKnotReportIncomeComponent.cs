using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报菲林收入构成单项，对应 API <c>month_data.income_components[]</c>。
/// 仅统计菲林（<see cref="InterKnotReportDataType.PolychromesData"/>）的来源分布。
/// </summary>
public class InterKnotReportIncomeComponent
{

    /// <summary>收入来源 action 标识（如 <c>daily_activity_rewards</c>）。</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; }


    /// <summary>该来源在查询月获得的菲林数量。</summary>
    [JsonPropertyName("num")]
    public int Num { get; set; }

    /// <summary>该来源占当月菲林总量的百分比，单位 %。</summary>
    [JsonPropertyName("percent")]
    public int Percent { get; set; }

}