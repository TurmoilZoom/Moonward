using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 登录风控响应头 <c>x-rpc-aigis</c> 解析后的载荷。
/// </summary>
public class CaptchaAigis
{

    /// <summary>风控会话 id，回传 aigis 头时需原样带上。</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; }


    /// <summary>mmt 类型（服务端下发，客户端一般无需解释）。</summary>
    [JsonPropertyName("mmt_type")]
    public int MmtType { get; set; }


    /// <summary>
    /// 极验配置的 JSON 字符串（再反序列化为 Gt3/Gt4 参数）。
    /// 含 <c>challenge</c> 字段时走 Geetest v3，否则走 v4。
    /// </summary>
    [JsonPropertyName("data")]
    public string Data { get; set; }

}
