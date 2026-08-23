using System.Text.Json.Serialization;

namespace Starward.Core.Blackboard;

/// <summary>
/// 百科 <c>home/content/list</c> 接口 <c>data</c> 节点（频道树 + 条目）。
/// </summary>
public class BlackboardContentListData
{

    [JsonPropertyName("list")]
    public List<BlackboardContentChannel> List { get; set; } = [];


    /// <summary>
    /// 在频道树中按 id 查找节点（含子节点）。
    /// </summary>
    /// <param name="channelId">频道 id。</param>
    /// <returns>命中的频道；未找到则为 null。</returns>
    public BlackboardContentChannel? FindChannel(int channelId)
    {
        return FindChannel(List, channelId);
    }


    private static BlackboardContentChannel? FindChannel(List<BlackboardContentChannel>? nodes, int channelId)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return null;
        }
        foreach (BlackboardContentChannel node in nodes)
        {
            if (node.Id == channelId)
            {
                return node;
            }
            BlackboardContentChannel? child = FindChannel(node.Children, channelId);
            if (child is not null)
            {
                return child;
            }
        }
        return null;
    }

}


/// <summary>
/// 百科首页内容频道（可嵌套）。
/// </summary>
public class BlackboardContentChannel
{

    [JsonPropertyName("id")]
    public int Id { get; set; }


    [JsonPropertyName("name")]
    public string Name { get; set; } = "";


    [JsonPropertyName("children")]
    public List<BlackboardContentChannel> Children { get; set; } = [];


    [JsonPropertyName("list")]
    public List<BlackboardContentItem> List { get; set; } = [];

}


/// <summary>
/// 频道下列表条目（好感壁纸 / 角色视频等）。
/// </summary>
public class BlackboardContentItem
{

    [JsonPropertyName("content_id")]
    public int ContentId { get; set; }


    [JsonPropertyName("title")]
    public string Title { get; set; } = "";


    [JsonPropertyName("icon")]
    public string? Icon { get; set; }


    [JsonPropertyName("summary")]
    public string? Summary { get; set; }


    /// <summary>
    /// 角标（如 <c>Pre</c> 预告、<c>Encore</c> 复刻）。无角标时为 <c>None</c>。
    /// </summary>
    [JsonPropertyName("corner_mark")]
    public string? CornerMark { get; set; }


    /// <summary>
    /// 角色简称（如「浅羽 悠真」对应「悠真」），用于封面匹配。
    /// </summary>
    [JsonPropertyName("alias_name")]
    public string? AliasName { get; set; }

}
