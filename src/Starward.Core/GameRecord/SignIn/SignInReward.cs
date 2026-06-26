using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 本月签到奖励列表（luna/sol home 接口）
/// </summary>
public class SignInReward
{

    /// <summary>
    /// 月份
    /// </summary>
    [JsonPropertyName("month")]
    public int Month { get; set; }


    /// <summary>
    /// 每日奖励，按天顺序排列
    /// </summary>
    [JsonPropertyName("awards")]
    public List<SignInAward> Awards { get; set; } = new();


    /// <summary>游戏 biz 标识，如 hk4e_cn。</summary>
    [JsonPropertyName("biz")]
    public string? Biz { get; set; }


    /// <summary>
    /// 是否允许补签
    /// </summary>
    [JsonPropertyName("resign")]
    public bool Resign { get; set; }

}
