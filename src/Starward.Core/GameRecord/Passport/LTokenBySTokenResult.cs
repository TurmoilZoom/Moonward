using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 通过 stoken 换取 ltoken 的响应 data。
/// </summary>
public class LTokenBySTokenResult
{

    /// <summary>ltoken 字符串。</summary>
    [JsonPropertyName("ltoken")]
    public string LToken { get; set; }

}
