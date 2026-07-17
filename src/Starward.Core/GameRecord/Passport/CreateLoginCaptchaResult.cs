using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 发送登录短信验证码成功后的业务数据。
/// </summary>
/// <remarks>
/// 实际接口 <c>sent_new</c> 为 boolean、<c>countdown</c> 为 number；
/// TeyvatGuide 的 TS 类型写成 string，JS 侧不会做严格校验，C# 必须按真实类型反序列化。
/// </remarks>
public class CreateLoginCaptchaResult
{

    /// <summary>是否发送了新验证码。</summary>
    [JsonPropertyName("sent_new")]
    public bool SentNew { get; set; }


    /// <summary>建议倒计时秒数。</summary>
    [JsonPropertyName("countdown")]
    public int Countdown { get; set; }


    /// <summary>
    /// 后续 <c>loginByMobileCaptcha</c> 必须原样带回的操作类型。
    /// </summary>
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; }

}
