namespace Starward.Core.CloudGame;

/// <summary>
/// 各云游戏接口的主机、业务请求头与凭证签名参数。易变常量集中于此，给新的云游戏开功能 = 在 <see cref="FromGameBiz"/> 加一条。
/// </summary>
/// <remarks>
/// 鉴权只靠请求头 <c>x-rpc-combo_token</c>，凭证只能从本机云游戏客户端读取，不能用米游社通行证换取，原因见
/// <see cref="CloudGameClient.BuildComboToken"/>。这里的 <see cref="AppId"/> / <see cref="ChannelId"/> / <see cref="AppKey"/>
/// 仅用于把客户端给出的 combo_token 与 open_id 签名拼成完整凭证。
/// <para>
/// 2026-09-22 按云·绝区零、2026-09-23 按云·原神 PC 客户端抓包核对：钱包查询与云游戏登录只带凭证与下面几个业务头即可，
/// 不需要设备 ID、客户端版本号或请求签名；其余 x-rpc-* 头是客户端自带的，这里不照抄，免得版本号过期。
/// </para>
/// </remarks>
public sealed class CloudGameApiConfig
{

    /// <summary>云游戏业务接口前缀，形如 <c>https://api-cloudgame.mihoyo.com/hk4e_cg_cn</c>，各业务路径拼在其后。</summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>
    /// 云游戏登录地址。每日免费时长是「登录后领取」，领取前先调它，
    /// 只查钱包未必触发发放（云·原神抓包里发放晚于首次钱包查询，且 <c>send_freetime</c> 始终为 0）。
    /// </summary>
    public string GamerLoginUrl => $"{ApiBaseUrl}/gamer/api/login";

    /// <summary>钱包查询地址（含客户端固定携带的查询参数）。</summary>
    public string WalletUrl => $"{ApiBaseUrl}/wallet/wallet/get?cost_method=COST_METHOD_UNSPECIFIED&get_type=GET_TYPE_DEFAULT";

    /// <summary>请求头 <c>x-rpc-cg_game_biz</c>，也是凭证里 <c>bi=</c> 字段的值。</summary>
    public required string CloudGameBiz { get; init; }

    /// <summary>请求头 <c>x-rpc-op_biz</c>。</summary>
    public required string OperationBiz { get; init; }

    /// <summary>请求头 <c>x-rpc-language</c>；只影响服务端文案，国服固定简体中文。</summary>
    public required string Language { get; init; }


    /// <summary>游戏 SDK App ID，凭证里 <c>ai=</c> 字段的值（原神为 4，绝区零为 12）。</summary>
    public required string AppId { get; init; }

    /// <summary>渠道 ID，凭证里 <c>ci=</c> 字段的值（米哈游官方服固定为 1）。</summary>
    public string ChannelId { get; init; } = "1";

    /// <summary>客户端内置 HMAC-SHA256 签名密钥，用于算出凭证里的 <c>si=</c> 签名。</summary>
    public required string AppKey { get; init; }


    /// <summary>
    /// 按游戏区服获取云游戏接口配置。
    /// </summary>
    /// <param name="gameBiz">游戏区服。</param>
    /// <returns>对应配置；该区服没有云游戏或尚未接入时返回 null。</returns>
    public static CloudGameApiConfig? FromGameBiz(GameBiz gameBiz)
    {
        return gameBiz.Value switch
        {
            GameBiz.hk4e_cn => hk4e_cn,
            GameBiz.nap_cn => nap_cn,
            _ => null,
        };
    }


    private static readonly CloudGameApiConfig hk4e_cn = new()
    {
        // 云·原神的业务路径前缀是 hk4e_cg_cn，与云·绝区零的 nap_cn/cg 形状不同，不能按 biz 推导
        ApiBaseUrl = "https://api-cloudgame.mihoyo.com/hk4e_cg_cn",
        CloudGameBiz = "hk4e_cn",
        OperationBiz = "clgm_cn",
        Language = "zh-cn",
        AppId = "4",
        ChannelId = "1",
        AppKey = "d0d3a7342df2026a70f650b907800111",
    };


    private static readonly CloudGameApiConfig nap_cn = new()
    {
        ApiBaseUrl = "https://cg-nap-api.mihoyo.com/nap_cn/cg",
        CloudGameBiz = "nap_cn",
        OperationBiz = "clgm_nap-cn",
        Language = "zh-cn",
        AppId = "12",
        ChannelId = "1",
        AppKey = "8844b676f3268c082a56021d9f47a206",
    };

}
