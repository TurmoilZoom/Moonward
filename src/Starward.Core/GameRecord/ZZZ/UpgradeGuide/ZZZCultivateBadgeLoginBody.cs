using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.UpgradeGuide;

/// <summary>
/// 绝区零养成指南 badge 登录请求体。
/// POST common/badge/v1/login/account，响应 Set-Cookie 写入 <c>e_nap_token</c>（约 48h）。
/// </summary>
public class ZZZCultivateBadgeLoginBody
{

    [JsonPropertyName("game_biz")]
    public string GameBiz { get; set; }


    [JsonPropertyName("lang")]
    public string Lang { get; set; }


    [JsonPropertyName("region")]
    public string Region { get; set; }


    /// <summary>游戏 UID 字符串。</summary>
    [JsonPropertyName("uid")]
    public string Uid { get; set; }

}
