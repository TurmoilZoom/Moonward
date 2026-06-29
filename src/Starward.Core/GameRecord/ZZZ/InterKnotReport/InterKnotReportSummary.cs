using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报汇总中单项资源的月度总量（菲林 / 母带 / 邦布券）。
/// 对应 API <c>month_data.list[]</c> 元素。
/// </summary>
public class InterKnotReportSummaryAward
{
    /// <summary>资源类型标识，取值见 <see cref="InterKnotReportDataType"/>。</summary>
    [JsonPropertyName("data_type")]
    public string DataType { get; set; }

    /// <summary>该资源在查询月的获取总量。</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>资源显示名称（由 API 返回，通常与 <see cref="DataType"/> 对应）。</summary>
    [JsonPropertyName("data_name")]
    public string DataName { get; set; }
}

/// <summary>
/// 绳网月报月度汇总，对应米游社工具箱「绳网月报」<c>month_info</c> 接口响应体。
/// 包含当月/历史月的资源总量、收入构成及可查询月份列表；本地缓存以结构化表存储（<c>ZZZInterKnotReportMonthData</c>、<c>ZZZInterKnotReportIncomeComponent</c>）。
/// </summary>
public class InterKnotReportSummary
{

    /// <summary>游戏 UID。</summary>
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Uid { get; set; }


    /// <summary>角色所在服务器 region 标识（如 <c>prod_gf_cn</c>、<c>prod_gf_us</c>）。</summary>
    [JsonPropertyName("region")]
    public string Region { get; set; }


    /// <summary>当前自然月，格式 <c>yyyyMM</c>（如 <c>202506</c>）。</summary>
    [JsonPropertyName("current_month")]
    public string CurrentMonth { get; set; }


    /// <summary>本次查询的目标月份，格式 <c>yyyyMM</c>；不传 <c>month</c> 参数时与 <see cref="CurrentMonth"/> 相同。</summary>
    [JsonPropertyName("data_month")]
    public string DataMonth { get; set; }


    /// <summary>查询月的资源总量与菲林收入构成（饼图数据）。</summary>
    [JsonPropertyName("month_data")]
    public InterKnotReportMonthData MonthData { get; set; }


    /// <summary>用户可手动拉取明细的历史月份列表，格式均为 <c>yyyyMM</c>。</summary>
    [JsonPropertyName("optional_month")]
    public List<string> OptionalMonth { get; set; }


    /// <summary>角色昵称与头像，供 UI 展示。</summary>
    [JsonPropertyName("role_info")]
    public InterKnotReportRoleInfo RoleInfo { get; set; }


    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }

}