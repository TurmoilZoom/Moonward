using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.CloudGame;

/// <summary>
/// 云游戏钱包（<c>wallet/wallet/get</c> 的 data 节点），只取展示用到的字段。
/// 服务端把数值写成字符串（如 <c>"coin_num": "0"</c>），统一经 <see cref="LenientInt32JsonConverter"/> 读取。
/// </summary>
public class CloudGameWallet
{

    /// <summary>付费货币（云·绝区零为邦邦点）。</summary>
    [JsonPropertyName("coin")]
    public CloudGameCoin? Coin { get; set; }


    /// <summary>免费时长。</summary>
    [JsonPropertyName("free_time")]
    public CloudGameFreeTime? FreeTime { get; set; }


    /// <summary>畅玩卡。</summary>
    [JsonPropertyName("play_card")]
    public CloudGamePlayCard? PlayCard { get; set; }

}


/// <summary>
/// 云游戏付费货币。
/// </summary>
public class CloudGameCoin
{

    /// <summary>货币余额。</summary>
    [JsonPropertyName("coin_num")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int CoinNum { get; set; }


    /// <summary>多少货币折合 1 分钟游戏时长（目前为 10）。</summary>
    [JsonPropertyName("exchange")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int Exchange { get; set; }

}


/// <summary>
/// 云游戏免费时长，单位为分钟。
/// </summary>
public class CloudGameFreeTime
{

    /// <summary>
    /// 本次请求顺带发放的每日免费时长。大于 0 时，同一响应里的 <see cref="FreeTime"/> 可能还是发放前的值，需要复查一次。
    /// </summary>
    /// <remarks>
    /// 2026-09 两次客户端抓包（云·原神、云·绝区零）里这个字段始终为 0，免费时长是登录之后异步落账的，
    /// 所以不能拿它当作“今天领没领过”的依据；主动领取请走 <see cref="CloudGameClient.LoginGamerAsync"/>。
    /// </remarks>
    [JsonPropertyName("send_freetime")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int SendFreeTime { get; set; }


    /// <summary>当前免费时长。</summary>
    [JsonPropertyName("free_time")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int FreeTime { get; set; }


    /// <summary>
    /// 免费时长累积上限（原神与绝区零均为 600 分钟）。达到上限后每日登录不再发放，
    /// 自动领取据此把「领了但没涨」与「没领到」区分开，不至于把满仓当成失败反复重试。
    /// </summary>
    [JsonPropertyName("free_time_limit")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int FreeTimeLimit { get; set; }

}


/// <summary>
/// 云游戏畅玩卡：生效期间不限时长游玩，不消耗免费时长与付费货币。
/// </summary>
/// <remarks>
/// 接口另有 <c>expire</c>、<c>msg</c>、<c>play_card_limit</c> 等字段，含义未核实，暂不取。
/// </remarks>
public class CloudGamePlayCard
{

    /// <summary>剩余生效时长，单位为秒；未开通或已过期为 0。</summary>
    [JsonPropertyName("remaining_sec")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int RemainingSeconds { get; set; }


    /// <summary>
    /// 服务端下发的状态短文案，实测未开通时为「未开通」。
    /// 文案随 <c>x-rpc-language</c> 本地化（国服固定简体中文），与应用界面语言未必一致，
    /// 所以只在算不出剩余时长时兜底展示——它能区分「未开通」与「已过期」，而剩余秒数区分不了。
    /// </summary>
    [JsonPropertyName("short_msg")]
    public string? ShortMessage { get; set; }

}
