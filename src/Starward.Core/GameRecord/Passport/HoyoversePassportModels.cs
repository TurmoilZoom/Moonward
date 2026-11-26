using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;


/// <summary>
/// 国际服密码登录请求体（<c>account/ma-passport/api/appLoginByPassword</c>），两个字段都需先用国际服公钥 RSA 加密。
/// </summary>
public class HoyoverseLoginByPasswordRequest
{

    /// <summary>RSA 加密后的邮箱或用户名。</summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }


    /// <summary>RSA 加密后的密码。</summary>
    [JsonPropertyName("password")]
    public string Password { get; set; }

}


/// <summary>
/// 国际服密码登录响应 data。stoken 可能在 <c>token</c>，也可能在 <c>tokens</c> 列表里。
/// </summary>
public class HoyoverseLoginResult
{

    /// <summary>单个 token（常见为 stoken，token_type = 1）。</summary>
    [JsonPropertyName("token")]
    public PassportToken? Token { get; set; }


    /// <summary>token 列表。</summary>
    [JsonPropertyName("tokens")]
    public List<PassportToken>? Tokens { get; set; }


    /// <summary>账号信息（aid / mid）。</summary>
    [JsonPropertyName("user_info")]
    public PassportUserInfo? UserInfo { get; set; }


    /// <summary>
    /// 取出 stoken：优先 token_type 为 1 的项，其次单个 <see cref="Token"/>。
    /// </summary>
    /// <returns>stoken；响应里没有时为 null。</returns>
    public string? GetSToken()
    {
        if (Token is { TokenType: 1 } && !string.IsNullOrWhiteSpace(Token.Token))
        {
            return Token.Token;
        }
        string? fromList = Tokens?.FirstOrDefault(x => x.TokenType == 1 && !string.IsNullOrWhiteSpace(x.Token))?.Token;
        return fromList ?? (string.IsNullOrWhiteSpace(Token?.Token) ? null : Token.Token);
    }

}


/// <summary>
/// 国际服 passport 风控响应头 <c>x-rpc-verify</c>：需先完成 <see cref="VerifyStr"/> 描述的验证，
/// 再带 <c>x-rpc-verify: {"risk_ticket": ...}</c> 重发原请求。
/// </summary>
public class HoyoverseRiskVerify
{

    /// <summary>验证完成后重发原请求时回传的票据。</summary>
    [JsonPropertyName("risk_ticket")]
    public string? RiskTicket { get; set; }


    /// <summary>验证详情的 JSON 字符串，反序列化为 <see cref="HoyoverseRiskVerifyDetail"/>。</summary>
    [JsonPropertyName("verify_str")]
    public string? VerifyStr { get; set; }


    /// <summary>
    /// 解析 <see cref="VerifyStr"/>。
    /// </summary>
    /// <returns>验证详情；为空或格式不对时为 null。</returns>
    public HoyoverseRiskVerifyDetail? GetDetail()
    {
        if (string.IsNullOrWhiteSpace(VerifyStr))
        {
            return null;
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(VerifyStr, GameRecordJsonContext.Default.HoyoverseRiskVerifyDetail);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

}


/// <summary>
/// <see cref="HoyoverseRiskVerify.VerifyStr"/> 的内容。
/// </summary>
public class HoyoverseRiskVerifyDetail
{

    /// <summary>验证票据：极验类传给 smart captcha 接口，身份类传给账号安全验证页。</summary>
    [JsonPropertyName("ticket")]
    public string? Ticket { get; set; }


    /// <summary>验证类型，见 <see cref="HoyoverseRiskVerifyType"/>。</summary>
    [JsonPropertyName("verify_type")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int VerifyType { get; set; }

}


/// <summary>
/// 国际服 passport 风控验证类型（HYP 账号 SDK 的 GEETEST / IDENTITY）。
/// </summary>
public enum HoyoverseRiskVerifyType
{
    /// <summary>人机验证：smart captcha 票据换极验参数，完成后提交校验。</summary>
    Geetest = 1,

    /// <summary>身份验证（新设备、异地等）：在账号安全验证页完成邮箱验证码。</summary>
    Identity = 2,
}


/// <summary>
/// smart captcha 接口请求体（<c>account/ma-aigis/api/createBySmartCaptchaTicket</c> 只带 ticket，
/// <c>checkSmartCaptcha</c> 另带 check_data）。
/// </summary>
public class HoyoverseSmartCaptchaRequest
{

    /// <summary><see cref="HoyoverseRiskVerifyDetail.Ticket"/>。</summary>
    [JsonPropertyName("ticket")]
    public string Ticket { get; set; }


    /// <summary>Base64(极验 getValidate() 结果 JSON)；创建时为 null。</summary>
    [JsonPropertyName("check_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CheckData { get; set; }

}


/// <summary>
/// <c>createBySmartCaptchaTicket</c> 响应 data。
/// </summary>
public class HoyoverseSmartCaptchaResult
{

    /// <summary>0 = 无需验证，1 = 需要极验（<see cref="Data"/> 为极验参数 JSON），其他值未知。</summary>
    [JsonPropertyName("mmt_type")]
    public int MmtType { get; set; }


    /// <summary>极验参数 JSON 字符串。</summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

}


/// <summary>
/// 国际服 stoken 换票请求体（<c>account/ma-passport/token/getBySToken</c>）。
/// </summary>
public class HoyoverseGetBySTokenRequest
{

    /// <summary>目标 token 类型（2 = ltoken_v2，4 = cookie_token_v2）。</summary>
    [JsonPropertyName("dst_token_types")]
    public List<int> DstTokenTypes { get; set; } = [];

}


/// <summary>
/// 国际服 stoken 换票响应 data。
/// </summary>
public class HoyoverseGetBySTokenResult
{

    /// <summary>换得的 token 列表。</summary>
    [JsonPropertyName("tokens")]
    public List<PassportToken>? Tokens { get; set; }


    /// <summary>账号信息。</summary>
    [JsonPropertyName("user_info")]
    public PassportUserInfo? UserInfo { get; set; }


    /// <summary>
    /// 按类型取出 token。
    /// </summary>
    /// <param name="tokenType">token 类型。</param>
    /// <returns>token 值；没有时为 null。</returns>
    public string? GetToken(int tokenType)
    {
        return Tokens?.FirstOrDefault(x => x.TokenType == tokenType && !string.IsNullOrWhiteSpace(x.Token))?.Token;
    }

}
