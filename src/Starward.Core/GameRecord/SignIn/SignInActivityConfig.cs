namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 各游戏「每日签到」活动配置（米游社 luna / HoYoLAB sol）。
/// <para>
/// act_id 与接口主机会随版本 / 活动轮换：
/// <list type="bullet">
/// <item>原神 (hk4e)、星铁 (hkrpg) 的 act_id 已在本仓库代码 / 参考项目中验证；</item>
/// <item>绝区零 (nap)、崩坏3 (bh3) 为最佳猜测，可能需要上线后用真实账号核对，届时仅改本文件即可。</item>
/// </list>
/// </para>
/// </summary>
public sealed class SignInActivityConfig
{

    /// <summary>
    /// 活动 id
    /// </summary>
    public required string ActId { get; init; }


    /// <summary>
    /// x-rpc-signgame 请求头的值
    /// </summary>
    public required string SignGame { get; init; }


    /// <summary>
    /// 接口基址，以 / 结尾，后接 home / info / sign / resign / resign_info
    /// </summary>
    public required string BaseUrl { get; init; }


    /// <summary>
    /// 可选的 Origin 请求头：绝区零 CN 的 act-nap-api 专用主机需要，其余游戏为 null（不添加）。
    /// </summary>
    public string? Origin { get; init; }


    /// <summary>本月奖励列表（home）接口 URL。</summary>
    /// <param name="lang">语言代码，如 zh-cn。</param>
    public string HomeUrl(string lang) => $"{BaseUrl}home?lang={lang}&act_id={ActId}";

    /// <summary>签到状态（info）接口 URL。</summary>
    /// <param name="lang">语言代码。</param>
    /// <param name="region">区服标识。</param>
    /// <param name="uid">角色 UID。</param>
    public string InfoUrl(string lang, string region, long uid) => $"{BaseUrl}info?lang={lang}&act_id={ActId}&region={region}&uid={uid}";

    /// <summary>补签信息（resign_info）接口 URL。</summary>
    /// <param name="lang">语言代码。</param>
    /// <param name="region">区服标识。</param>
    /// <param name="uid">角色 UID。</param>
    public string ResignInfoUrl(string lang, string region, long uid) => $"{BaseUrl}resign_info?lang={lang}&act_id={ActId}&region={region}&uid={uid}";

    /// <summary>今日签到（sign）接口 URL。</summary>
    public string SignUrl() => $"{BaseUrl}sign";

    /// <summary>补签（resign）接口 URL。</summary>
    public string ResignUrl() => $"{BaseUrl}resign";


    /// <summary>
    /// 根据游戏（<see cref="GameBiz.Game"/>）与是否国际服获取签到配置，不支持的游戏返回 null。
    /// </summary>
    /// <param name="game">GameBiz.Game，如 hk4e / hkrpg / nap / bh3。</param>
    /// <param name="isOversea">是否国际服（HoYoLAB）。</param>
    /// <returns>匹配的配置；不支持的游戏返回 null。</returns>
    public static SignInActivityConfig? FromGame(string? game, bool isOversea)
    {
        return (game, isOversea) switch
        {
            // 原神 —— 已验证
            (GameBiz.hk4e, false) => new() { ActId = "e202311201442471", SignGame = "hk4e", BaseUrl = "https://api-takumi.mihoyo.com/event/luna/" },
            (GameBiz.hk4e, true) => new() { ActId = "e202102251931481", SignGame = "hk4e", BaseUrl = "https://sg-hk4e-api.hoyoverse.com/event/sol/" },

            // 星穹铁道 —— CN 已验证（见 HyperionClient 注释），OS act_id 待核对
            (GameBiz.hkrpg, false) => new() { ActId = "e202304121516551", SignGame = "hkrpg", BaseUrl = "https://api-takumi.mihoyo.com/event/luna/" },
            (GameBiz.hkrpg, true) => new() { ActId = "e202303301540311", SignGame = "hkrpg", BaseUrl = "https://sg-public-api.hoyolab.com/event/luna/os/" },

            // 绝区零 —— CN 走 act-nap-api 专用主机并带 Origin 头，OS act_id 与官方 HoYoLAB 签到页一致
            (GameBiz.nap, false) => new() { ActId = "e202406242138391", SignGame = "zzz", BaseUrl = "https://act-nap-api.mihoyo.com/event/luna/zzz/", Origin = "https://act.mihoyo.com" },
            (GameBiz.nap, true) => new() { ActId = "e202406031448091", SignGame = "zzz", BaseUrl = "https://sg-public-api.hoyolab.com/event/luna/zzz/os/" },

            // 崩坏3 —— 走通用 luna(CN) / mani(OS) 接口，仅 act_id 需校正
            (GameBiz.bh3, false) => new() { ActId = "e202306201626331", SignGame = "bh3", BaseUrl = "https://api-takumi.mihoyo.com/event/luna/" },
            (GameBiz.bh3, true) => new() { ActId = "e202110291205111", SignGame = "bh3", BaseUrl = "https://sg-public-api.hoyolab.com/event/mani/" },

            _ => null,
        };
    }

}
