using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 补签信息（luna/sol resign_info 接口）
/// </summary>
public class SignInResignInfo
{

    /// <summary>今日已补签次数。</summary>
    [JsonPropertyName("resign_cnt_daily")]
    public int ResignCountDaily { get; set; }


    /// <summary>本月已补签次数。</summary>
    [JsonPropertyName("resign_cnt_monthly")]
    public int ResignCountMonthly { get; set; }


    /// <summary>每日补签次数上限。</summary>
    [JsonPropertyName("resign_limit_daily")]
    public int ResignLimitDaily { get; set; }


    /// <summary>每月补签次数上限。</summary>
    [JsonPropertyName("resign_limit_monthly")]
    public int ResignLimitMonthly { get; set; }


    /// <summary>
    /// 本月漏签天数
    /// </summary>
    [JsonPropertyName("sign_cnt_missed")]
    public int SignCountMissed { get; set; }


    /// <summary>
    /// 当前拥有的补签货币数量
    /// </summary>
    [JsonPropertyName("coin_cnt")]
    public int CoinCount { get; set; }


    /// <summary>
    /// 单次补签消耗的货币数量
    /// </summary>
    [JsonPropertyName("coin_cost")]
    public int CoinCost { get; set; }


    /// <summary>今日是否已签到。</summary>
    [JsonPropertyName("signed")]
    public bool Signed { get; set; }


    /// <summary>本月已签到天数。</summary>
    [JsonPropertyName("sign_days")]
    public int SignDays { get; set; }


    /// <summary>补签消耗（与 coin_cost 含义相近，以接口返回为准）。</summary>
    [JsonPropertyName("cost")]
    public int Cost { get; set; }

}
