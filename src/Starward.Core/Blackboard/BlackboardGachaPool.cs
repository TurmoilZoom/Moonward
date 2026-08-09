using System.Text.Json.Serialization;

namespace Starward.Core.Blackboard;

/// <summary>
/// 百科 <c>gacha_pool</c> 接口 <c>data</c> 节点。
/// </summary>
public class BlackboardGachaPoolData
{

    [JsonPropertyName("list")]
    public List<BlackboardGachaPoolItem> List { get; set; } = [];

}


/// <summary>
/// 单条卡池 / 调频 / 跃迁条目（含起止时间与 UP 图标）。
/// </summary>
public class BlackboardGachaPoolItem
{

    [JsonPropertyName("id")]
    public int Id { get; set; }


    [JsonPropertyName("title")]
    public string Title { get; set; } = "";


    [JsonPropertyName("activity_url")]
    public string? ActivityUrl { get; set; }


    /// <summary>
    /// 活动未开始时展示的固定文案（如「即将开始」「长期开放」）。
    /// </summary>
    [JsonPropertyName("content_before_act")]
    public string? ContentBeforeAct { get; set; }


    /// <summary>
    /// 开始时间墙钟字符串，语义为东八区 <c>yyyy-MM-dd HH:mm:ss</c>。
    /// </summary>
    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }


    /// <summary>
    /// 结束时间墙钟字符串，语义为东八区 <c>yyyy-MM-dd HH:mm:ss</c>。
    /// </summary>
    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }


    [JsonPropertyName("pool")]
    public List<BlackboardGachaPoolIcon> Pool { get; set; } = [];

}


/// <summary>
/// 卡池内单个角色 / 武器 / 音擎图标。
/// </summary>
public class BlackboardGachaPoolIcon
{

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }


    [JsonPropertyName("url")]
    public string? Url { get; set; }


    /// <summary>
    /// 扩展 JSON 字符串（绝区零常含 <c>type</c>/<c>level</c>）。
    /// </summary>
    [JsonPropertyName("ext")]
    public string? Ext { get; set; }

}
