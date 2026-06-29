using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报收入明细分页响应，对应 <c>month_detail</c> 接口。
/// 同一月份、同一 <see cref="DataType"/> 下可能有多页，Client 层负责自动翻页合并 <see cref="List"/>。
/// </summary>
public class InterKnotReportDetail : IJsonOnDeserialized
{

    /// <summary>游戏 UID。</summary>
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Uid { get; set; }


    /// <summary>角色所在服务器 region 标识。</summary>
    [JsonPropertyName("region")]
    public string Region { get; set; }


    /// <summary>查询月份，格式 <c>yyyyMM</c>。</summary>
    [JsonPropertyName("data_month")]
    public string DataMonth { get; set; }


    /// <summary>当前页码，从 1 开始。</summary>
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }


    /// <summary>本页明细列表；翻页合并后即为该月该类型的全部记录。</summary>
    [JsonPropertyName("list")]
    public List<InterKnotReportDetailItem> List { get; set; }

    /// <summary>当月该资源类型的记录总数，用于判断本地缓存是否已完整。</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }


    /// <summary>资源显示名称（由 API 返回）。</summary>
    [JsonPropertyName("data_name")]
    public string DataName { get; set; }


    /// <summary>资源类型标识，取值见 <see cref="InterKnotReportDataType"/>。</summary>
    [JsonPropertyName("data_type")]
    public string DataType { get; set; }


    /// <summary>
    /// 反序列化后为每条明细填充父级上下文（UID、月份、资源类型），便于直接写入 SQLite。
    /// </summary>
    public void OnDeserialized()
    {
        foreach (var item in List)
        {
            item.Uid = this.Uid;
            item.DataMonth = this.DataMonth;
            item.DataType = this.DataType;
        }
    }

}