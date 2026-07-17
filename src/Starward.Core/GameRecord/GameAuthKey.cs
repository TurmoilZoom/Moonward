using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord;

/// <summary>
/// 通过 stoken 调用 <c>binding/api/genAuthKey</c> 返回的抽卡 authkey（Auth Key B）。
/// 文档见 UIGF mihoyo-api-collection；社区实现见 TeyvatGuide / Snap.Hutao。
/// </summary>
public class GameAuthKey
{

    /// <summary>用于 public-operation 抽卡接口的 authkey。</summary>
    [JsonPropertyName("authkey")]
    public string Authkey { get; set; } = "";


    /// <summary>authkey 版本，通常为 1。</summary>
    [JsonPropertyName("authkey_ver")]
    public int AuthkeyVer { get; set; }


    /// <summary>签名类型，通常为 2。</summary>
    [JsonPropertyName("sign_type")]
    public int SignType { get; set; }

}



/// <summary>
/// <c>binding/api/genAuthKey</c> 请求体（auth_appid=webview_gacha 用于游戏抽卡记录）。
/// </summary>
public class GenAuthKeyPostBody
{

    /// <summary>
    /// 初始化 genAuthKey 请求体。
    /// </summary>
    /// <param name="authAppId">用途标识；抽卡记录固定为 <c>webview_gacha</c>。</param>
    /// <param name="gameBiz">游戏业务线，如 <c>hk4e_cn</c> / <c>hkrpg_cn</c>。</param>
    /// <param name="gameUid">游戏 UID。</param>
    /// <param name="region">服务器 region，如 <c>cn_gf01</c> / <c>prod_gf_cn</c>。</param>
    public GenAuthKeyPostBody(string authAppId, string gameBiz, long gameUid, string region)
    {
        AuthAppId = authAppId;
        GameBiz = gameBiz;
        GameUid = gameUid;
        Region = region;
    }


    /// <summary>用途标识；抽卡记录为 <c>webview_gacha</c>。</summary>
    [JsonPropertyName("auth_appid")]
    public string AuthAppId { get; set; }


    /// <summary>游戏业务线（绑定接口返回的 game_biz）。</summary>
    [JsonPropertyName("game_biz")]
    public string GameBiz { get; set; }


    /// <summary>游戏 UID。</summary>
    [JsonPropertyName("game_uid")]
    public long GameUid { get; set; }


    /// <summary>服务器 region。</summary>
    [JsonPropertyName("region")]
    public string Region { get; set; }

}
