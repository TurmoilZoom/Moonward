using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;

namespace Starward.Core.CloudGame;

/// <summary>
/// 米哈游云游戏业务接口。鉴权只靠请求头 <c>x-rpc-combo_token</c>。
/// </summary>
/// <remarks>
/// 凭证形如 <c>bi=hk4e_cn;ai=4;ci=1;ct=…;oi=…;si=…</c>：<c>ct</c> 是 combo_token 本体，<c>oi</c> 是米哈游通行证账号 ID，
/// <c>si</c> 是按客户端内置密钥算出的签名（见 <see cref="BuildComboToken"/>），整串原样作为请求头发送。
/// </remarks>
public class CloudGameClient
{

    private readonly HttpClient _httpClient;


    /// <summary>
    /// 初始化云游戏 Client。
    /// </summary>
    /// <param name="httpClient">可选共享 HttpClient；为 null 时自建并开启自动解压。</param>
    public CloudGameClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
    }


    /// <summary>
    /// 查询云游戏钱包（免费时长、付费货币、畅玩卡）。
    /// </summary>
    /// <param name="config">区服接口配置。</param>
    /// <param name="comboToken">凭证，即请求头 <c>x-rpc-combo_token</c> 的值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>钱包数据。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0，凭证失效时为 -100。</exception>
    /// <exception cref="HttpRequestException">网络错误或 HTTP 状态码表示失败。</exception>
    public async Task<CloudGameWallet> GetWalletAsync(CloudGameApiConfig config, string comboToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, config.WalletUrl);
        SetBusinessHeaders(request, config, comboToken);
        return await CommonSendAsync(request, CloudGameJsonContext.Default.miHoYoApiWrapperCloudGameWallet, cancellationToken);
    }


    /// <summary>
    /// 登录云游戏。每日免费时长是「登录后领取」，领取前必须先调用本接口。
    /// </summary>
    /// <remarks>
    /// 官方客户端每次启动都是先 <c>gamer/api/login</c> 再查钱包；
    /// 云·原神抓包中免费时长在登录之后、首次钱包查询之后才到账，说明发放由登录触发而非查询，
    /// 因此自动领取不能只查钱包。请求体固定为空对象，账号信息全在凭证里。
    /// </remarks>
    /// <param name="config">区服接口配置。</param>
    /// <param name="comboToken">凭证，即请求头 <c>x-rpc-combo_token</c> 的值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录结果。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0，凭证失效时为 -100。</exception>
    /// <exception cref="HttpRequestException">网络错误或 HTTP 状态码表示失败。</exception>
    public async Task<CloudGameGamerLoginResult> LoginGamerAsync(CloudGameApiConfig config, string comboToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, config.GamerLoginUrl)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        SetBusinessHeaders(request, config, comboToken);
        return await CommonSendAsync(request, CloudGameJsonContext.Default.miHoYoApiWrapperCloudGameGamerLoginResult, cancellationToken);
    }


    /// <summary>
    /// 按 combo_token 本体与通行证账号 ID 拼装完整凭证（含计算 <c>si</c> 签名）。
    /// </summary>
    /// <remarks>
    /// 两个入参只能来自本机云游戏客户端，**不能用米游社通行证的 stoken 去换**。
    /// 2026-09-23 实测：stoken 按签发它的 <c>x-rpc-app_id</c> 绑定，米游社域（<c>ddxf5dufpuyo</c>）签发的 stoken
    /// 拿到云游戏客户端的 app_id 下校验即为 -100；SDK 的 <c>combo/granter/login/v2/login</c> 校验的是请求体里的游戏
    /// app_id（原神 4 / 绝区零 12），两个游戏一律回 -114，补任何请求头都无效。官方桥接
    /// <c>mdk/shield/api/loginByAuthTicket</c> 亦被 -464 挡死（auth ticket 本身能正常签出）。
    /// </remarks>
    /// <param name="config">区服接口配置。</param>
    /// <param name="comboToken">combo_token 本体。</param>
    /// <param name="openId">米哈游通行证账号 ID。</param>
    /// <returns>完整凭证，可直接作为 <c>x-rpc-combo_token</c> 请求头。</returns>
    public static string BuildComboToken(CloudGameApiConfig config, string comboToken, string openId)
    {
        string sign = ComputeSign(config.AppKey, $"app_id={config.AppId}&channel_id={config.ChannelId}&combo_token={comboToken}&open_id={openId}");
        return $"bi={config.CloudGameBiz};ai={config.AppId};ci={config.ChannelId};ct={comboToken};oi={openId};si={sign}";
    }


    /// <summary>
    /// 填入云游戏业务接口共用的请求头：凭证与三个业务标识。
    /// </summary>
    /// <param name="request">待发送的请求。</param>
    /// <param name="config">区服接口配置。</param>
    /// <param name="comboToken">凭证。</param>
    private static void SetBusinessHeaders(HttpRequestMessage request, CloudGameApiConfig config, string comboToken)
    {
        request.Headers.TryAddWithoutValidation("x-rpc-combo_token", comboToken);
        request.Headers.TryAddWithoutValidation("x-rpc-cg_game_biz", config.CloudGameBiz);
        request.Headers.TryAddWithoutValidation("x-rpc-op_biz", config.OperationBiz);
        request.Headers.TryAddWithoutValidation("x-rpc-language", config.Language);
    }


    /// <summary>
    /// 对按字母排序拼接的键值对执行 HMAC-SHA256，返回小写十六进制字符串。
    /// </summary>
    /// <param name="appKey">客户端内置密钥。</param>
    /// <param name="content">待签名的原文。</param>
    /// <returns>签名小写十六进制字符串。</returns>
    private static string ComputeSign(string appKey, string content)
    {
        byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appKey), Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }


    /// <summary>
    /// 发送请求并解包 <see cref="miHoYoApiWrapper{T}"/>。
    /// </summary>
    /// <typeparam name="T">data 节点类型。</typeparam>
    /// <param name="request">已填好请求头的请求。</param>
    /// <param name="typeInfo">源生成的类型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>data 节点。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0，或响应无法解析、data 为空。</exception>
    private async Task<T> CommonSendAsync<T>(HttpRequestMessage request, JsonTypeInfo<miHoYoApiWrapper<T>> typeInfo, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        miHoYoApiWrapper<T>? wrapper = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
        if (wrapper is null)
        {
            throw new miHoYoApiException(-1, "Can not parse the response body.");
        }
        if (wrapper.Retcode != 0)
        {
            throw new miHoYoApiException(wrapper.Retcode, wrapper.Message);
        }
        return wrapper.Data ?? throw new miHoYoApiException(-1, "Response data is null.");
    }

}
