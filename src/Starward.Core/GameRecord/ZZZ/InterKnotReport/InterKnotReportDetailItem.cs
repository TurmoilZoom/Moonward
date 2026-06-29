using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报单条收入明细，对应 <c>month_detail</c> 接口 <c>list[]</c> 元素。
/// 反序列化后由 <see cref="InterKnotReportDetail.OnDeserialized"/> 填充 <see cref="Uid"/>、
/// <see cref="DataMonth"/>、<see cref="DataType"/>，再写入 SQLite <c>ZZZInterKnotReportDetailItem</c>。
/// </summary>
public class InterKnotReportDetailItem
{

    /// <summary>游戏 UID；由父对象 <see cref="InterKnotReportDetail"/> 在反序列化后赋值，非 API 字段。</summary>
    [JsonIgnore]
    public long Uid { get; set; }

    /// <summary>明细记录 ID，同一 UID 下唯一，用作数据库主键之一。</summary>
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Id { get; set; }

    /// <summary>所属月份，格式 <c>yyyyMM</c>；由父对象赋值，非 API 字段。</summary>
    [JsonIgnore]
    public string DataMonth { get; set; }

    /// <summary>资源类型，取值见 <see cref="InterKnotReportDataType"/>；由父对象赋值，非 API 字段。</summary>
    [JsonIgnore]
    public string DataType { get; set; }


    /// <summary>收入来源 action 标识（如 <c>daily_activity_rewards</c>），与汇总 <c>income_components[].action</c> 一致。</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; }


    /// <summary>
    /// 获取时刻，API 返回 Unix 时间戳（秒），经 <see cref="TimestampStringJsonConverter"/> 转为 UTC 的 <see cref="DateTimeOffset"/>。
    /// UI 按日聚合时需换算到游戏服务器时区后再取日历日，见 <c>InterKnotMonthlyReportPage.RefreshDailyDataPlot</c>。
    /// </summary>
    [JsonPropertyName("time")]
    [JsonConverter(typeof(TimestampStringJsonConverter))]
    public DateTimeOffset Time { get; set; }


    /// <summary>本次获取的资源数量。</summary>
    [JsonPropertyName("num")]
    public int Number { get; set; }

}