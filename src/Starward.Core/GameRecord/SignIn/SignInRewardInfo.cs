using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 每日签到状态信息（luna/sol info 接口）
/// </summary>
public class SignInRewardInfo
{

    /// <summary>
    /// 本月已签到天数
    /// </summary>
    [JsonPropertyName("total_sign_day")]
    public int TotalSignDay { get; set; }


    /// <summary>
    /// 服务器当天日期 yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("today")]
    public string? Today { get; set; }


    /// <summary>
    /// 今日是否已签到
    /// </summary>
    [JsonPropertyName("is_sign")]
    public bool IsSign { get; set; }


    /// <summary>是否已订阅签到提醒（米游社/HoYoLAB 功能）。</summary>
    [JsonPropertyName("is_sub")]
    public bool IsSub { get; set; }


    /// <summary>区服标识。</summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }


    /// <summary>
    /// 本月漏签天数
    /// </summary>
    [JsonPropertyName("sign_cnt_missed")]
    public int SignCountMissed { get; set; }


    /// <summary>连续签到天数（短签计数）。</summary>
    [JsonPropertyName("short_sign_day")]
    public int ShortSignDay { get; set; }

}
