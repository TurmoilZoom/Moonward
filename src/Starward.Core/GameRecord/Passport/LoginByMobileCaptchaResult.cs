using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 短信验证码登录成功后的业务数据。
/// </summary>
public class LoginByMobileCaptchaResult
{

    /// <summary>登录 token（通常为 stoken_v2）。</summary>
    [JsonPropertyName("token")]
    public PassportToken Token { get; set; }


    /// <summary>账号信息（至少含 aid / mid）。</summary>
    [JsonPropertyName("user_info")]
    public PassportUserInfo UserInfo { get; set; }


    /// <summary>登录 ticket（可选）。</summary>
    [JsonPropertyName("login_ticket")]
    public string? LoginTicket { get; set; }


    /// <summary>是否新用户。</summary>
    [JsonPropertyName("new_user")]
    public bool NewUser { get; set; }


    /// <summary>是否需要真人验证。</summary>
    [JsonPropertyName("need_realperson")]
    public bool NeedRealperson { get; set; }

}


/// <summary>
/// passport 登录返回的 token 结构。
/// </summary>
public class PassportToken
{

    /// <summary>token 类型（stoken 常见为 1）。</summary>
    [JsonPropertyName("token_type")]
    public int TokenType { get; set; }


    /// <summary>token 值，即 stoken。</summary>
    [JsonPropertyName("token")]
    public string Token { get; set; }

}


/// <summary>
/// passport 登录返回的用户信息（仅保留 Cookie 组装所需字段）。
/// </summary>
public class PassportUserInfo
{

    /// <summary>账号 id（aid），用作 account_id / ltuid / stuid。</summary>
    [JsonPropertyName("aid")]
    [JsonConverter(typeof(JsonPrimitiveStringConverter))]
    public string Aid { get; set; }


    /// <summary>mid，stoken_v2 换票时必填。</summary>
    [JsonPropertyName("mid")]
    [JsonConverter(typeof(JsonPrimitiveStringConverter))]
    public string Mid { get; set; }


    /// <summary>账号名（可能为空）。</summary>
    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }


    /// <summary>绑定手机号（可能脱敏）。</summary>
    [JsonPropertyName("mobile")]
    public string? Mobile { get; set; }

}
