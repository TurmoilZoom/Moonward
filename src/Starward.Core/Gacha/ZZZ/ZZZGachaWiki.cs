using System.Text.Json.Serialization;

namespace Starward.Core.Gacha.ZZZ;

public class ZZZGachaWiki
{

    [JsonPropertyName("game")]
    public string Game { get; set; }

    [JsonPropertyName("lang")]
    public string Language { get; set; }

    [JsonPropertyName("list")]
    public List<ZZZGachaInfo> List { get; set; }

    /// <summary>
    /// 实际返回数据的数据源地址，只在客户端拉取时填写，不参与序列化。
    /// </summary>
    [JsonIgnore]
    public string? SourceUrl { get; set; }

}
