using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 签到奖励物品
/// </summary>
public class SignInAward
{

    /// <summary>
    /// 图标
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }


    /// <summary>
    /// 名称
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }


    /// <summary>
    /// 数量
    /// </summary>
    [JsonPropertyName("cnt")]
    public int Count { get; set; }

}
