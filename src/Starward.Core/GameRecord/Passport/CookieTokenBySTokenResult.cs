using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 通过 stoken 换取 cookie_token 的响应 data。
/// </summary>
public class CookieTokenBySTokenResult
{

    /// <summary>账号 uid（可能是 number 或 string）。</summary>
    [JsonPropertyName("uid")]
    [JsonConverter(typeof(JsonPrimitiveStringConverter))]
    public string? Uid { get; set; }


    /// <summary>cookie_token 字符串。</summary>
    [JsonPropertyName("cookie_token")]
    public string CookieToken { get; set; }

}
