using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord;

/// <summary>
/// 通过 SToken 换取的国服 Cookie Token 信息。
/// </summary>
public class CookieTokenInfo
{

    /// <summary>
    /// 米游社通行证 UID。
    /// </summary>
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";

    /// <summary>
    /// 可用于 GameRecord 请求的 Cookie Token。
    /// </summary>
    [JsonPropertyName("cookie_token")]
    public string CookieToken { get; set; } = "";

}
