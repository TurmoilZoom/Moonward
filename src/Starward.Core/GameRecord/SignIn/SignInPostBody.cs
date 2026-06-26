using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 签到 / 补签接口的请求体
/// </summary>
public class SignInPostBody
{

    /// <summary>活动 id，与 <see cref="SignInActivityConfig.ActId"/> 一致。</summary>
    [JsonPropertyName("act_id")]
    public string ActId { get; set; }


    /// <summary>区服标识，如 cn_gf01。</summary>
    [JsonPropertyName("region")]
    public string Region { get; set; }


    /// <summary>角色 UID 字符串。</summary>
    [JsonPropertyName("uid")]
    public string Uid { get; set; }


    /// <summary>
    /// 构造签到 / 补签 POST 请求体。
    /// </summary>
    /// <param name="actId">活动 id。</param>
    /// <param name="region">区服标识。</param>
    /// <param name="uid">角色 UID。</param>
    public SignInPostBody(string actId, string region, string uid)
    {
        ActId = actId;
        Region = region;
        Uid = uid;
    }

}
