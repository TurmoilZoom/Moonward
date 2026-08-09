using System.Text.Json.Serialization;

namespace Starward.Core.Blackboard;

/// <summary>
/// 百科 <c>home/position</c> 接口 <c>data</c> 节点（运营位树）。
/// </summary>
public class BlackboardPositionData
{

    [JsonPropertyName("list")]
    public List<BlackboardPositionNode> List { get; set; } = [];

}


/// <summary>
/// 运营位树节点；热点活动嵌在子节点的 <see cref="List"/> 中。
/// </summary>
public class BlackboardPositionNode
{

    [JsonPropertyName("id")]
    public int Id { get; set; }


    [JsonPropertyName("name")]
    public string Name { get; set; } = "";


    [JsonPropertyName("parent_id")]
    public int ParentId { get; set; }


    [JsonPropertyName("depth")]
    public int Depth { get; set; }


    /// <summary>
    /// 频道扩展属性 JSON 数组字符串（含 <c>display_type</c> 等）。
    /// </summary>
    [JsonPropertyName("ch_ext")]
    public string? ChannelExt { get; set; }


    [JsonPropertyName("children")]
    public List<BlackboardPositionNode> Children { get; set; } = [];


    [JsonPropertyName("list")]
    public List<BlackboardPositionItem> List { get; set; } = [];

}


/// <summary>
/// 运营位推荐条目（热点活动卡片）。
/// </summary>
public class BlackboardPositionItem
{

    [JsonPropertyName("recommend_id")]
    public int RecommendId { get; set; }


    [JsonPropertyName("content_id")]
    public int ContentId { get; set; }


    [JsonPropertyName("title")]
    public string Title { get; set; } = "";


    [JsonPropertyName("abstract")]
    public string? Abstract { get; set; }


    [JsonPropertyName("icon")]
    public string? Icon { get; set; }


    [JsonPropertyName("url")]
    public string? Url { get; set; }


    /// <summary>
    /// 结束时间：毫秒 Unix 时间戳字符串；无倒计时时常为 <c>"0"</c>。
    /// </summary>
    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }


    [JsonPropertyName("type")]
    public int Type { get; set; }

}
