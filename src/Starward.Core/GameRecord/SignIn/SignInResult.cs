using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 签到 / 补签接口返回结果
/// </summary>
public class SignInResult
{

    /// <summary>业务状态码字符串（部分活动返回）。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }


    /// <summary>
    /// 风控代码，非 0 表示触发风控
    /// </summary>
    [JsonPropertyName("risk_code")]
    public int RiskCode { get; set; }


    /// <summary>极验 gt 参数，触发风控时非空。</summary>
    [JsonPropertyName("gt")]
    public string? Gt { get; set; }


    /// <summary>极验 challenge 参数。</summary>
    [JsonPropertyName("challenge")]
    public string? Challenge { get; set; }


    /// <summary>
    /// 1 表示需要极验验证（风控）
    /// </summary>
    [JsonPropertyName("success")]
    public int Success { get; set; }


    /// <summary>是否被标记为风控请求。</summary>
    [JsonPropertyName("is_risk")]
    public bool IsRisk { get; set; }

}
