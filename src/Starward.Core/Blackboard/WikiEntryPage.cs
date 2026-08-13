using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Starward.Core.Blackboard;

/// <summary>
/// 百科词条 <c>hoyowiki/.../wapi/entry_page</c> 接口 <c>data</c> 节点。
/// </summary>
public class WikiEntryPageData
{

    [JsonPropertyName("page")]
    public WikiEntryPage Page { get; set; } = new();

}


/// <summary>
/// 百科词条正文（模块化富文本）。
/// </summary>
public class WikiEntryPage
{

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";


    [JsonPropertyName("name")]
    public string Name { get; set; } = "";


    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }


    [JsonPropertyName("modules")]
    public List<WikiEntryModule> Modules { get; set; } = [];

}


/// <summary>
/// 词条模块。
/// </summary>
public class WikiEntryModule
{

    [JsonPropertyName("name")]
    public string? Name { get; set; }


    [JsonPropertyName("components")]
    public List<WikiEntryComponent> Components { get; set; } = [];

}


/// <summary>
/// 词条组件；<see cref="Data"/> 为 JSON 字符串（如 <c>{"rich_text":"..."}</c>）。
/// </summary>
public class WikiEntryComponent
{

    [JsonPropertyName("component_id")]
    public string? ComponentId { get; set; }


    [JsonPropertyName("data")]
    public string? Data { get; set; }

}


/// <summary>
/// 从词条富文本中提取视频地址。
/// </summary>
public static partial class WikiEntryVideo
{

    [GeneratedRegex(@"https:[^""\\\s]+\.mp4", RegexOptions.IgnoreCase)]
    private static partial Regex Mp4UrlRegex();


    /// <summary>
    /// 在词条各组件的 <c>data</c> 中查找第一个 mp4 地址。
    /// </summary>
    /// <param name="page">词条正文。</param>
    /// <returns>视频 URL；未找到则为 null。</returns>
    public static string? ExtractMp4Url(WikiEntryPage? page)
    {
        if (page?.Modules is null)
        {
            return null;
        }
        foreach (WikiEntryModule module in page.Modules)
        {
            foreach (WikiEntryComponent component in module.Components ?? [])
            {
                if (string.IsNullOrWhiteSpace(component.Data))
                {
                    continue;
                }
                Match match = Mp4UrlRegex().Match(component.Data);
                if (match.Success)
                {
                    return match.Value;
                }
            }
        }
        return null;
    }

}
