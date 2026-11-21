using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// passport V2 换票接口（account/ma-cn-session/app/exchange）的 token 类型。
/// </summary>
internal enum PassportTokenType
{
    /// <summary>stoken，换票的源凭据。</summary>
    SToken = 1,

    /// <summary>ltoken_v2。</summary>
    LToken = 2,

    /// <summary>cookie_token_v2。</summary>
    CookieToken = 4,
}


/// <summary>
/// passport V2 换票请求体。
/// </summary>
public class PassportExchangeRequest
{

    /// <summary>目标 token 类型（2 = ltoken_v2，4 = cookie_token_v2）。</summary>
    [JsonPropertyName("dst_token_type")]
    public int DstTokenType { get; set; }


    /// <summary>换票源 token（token 为 stoken，token_type 固定为 1）。</summary>
    [JsonPropertyName("src_token")]
    public PassportToken? SrcToken { get; set; }


    /// <summary>账号 mid。</summary>
    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

}


/// <summary>
/// passport V2 换票响应 data。
/// </summary>
public class PassportExchangeResult
{

    /// <summary>换得的 token（ltoken_v2 / cookie_token_v2）。</summary>
    [JsonPropertyName("token")]
    public PassportToken? Token { get; set; }


    /// <summary>账号信息（cookie_token 换票时含 aid）。</summary>
    [JsonPropertyName("user_info")]
    public PassportUserInfo? UserInfo { get; set; }

}
