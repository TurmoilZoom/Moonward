using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 国际服（HoYoverse）passport 客户端：账号密码登录、登录风控验证、stoken 换 ltoken / cookie_token。
/// </summary>
/// <remarks>
/// 接口与请求头对齐 HYP 国际服前端的账号 SDK（2026-11 读自 1.20.2 前端）与上游 Starward 0.19 的 PassportClient：
/// <c>x-rpc-app_id=ddxf6vlr1reo</c>、<c>client_type=3</c>、<c>game_biz=hyp_global</c>。
/// 签发的 stoken 绑定这个 app_id，换票与静默刷新也必须带同一组头。
/// </remarks>
public class HoyoversePassportClient
{

    /// <summary>passport 生产环境默认区（SG）。账号 SDK 另有 us / eu，但默认就是 SG。</summary>
    private const string PassportBase = "https://passport-api-sg.hoyoverse.com/";

    private const string PublicApiBase = "https://sg-public-api.hoyoverse.com/";

    private const string AppId = "ddxf6vlr1reo";

    private const string GameBiz = "hyp_global";

    private const string SdkVersion = "2.55.3";

    private const string PassportDsSalt = "IZPgfb0dRPtBeLuFkdDznSZ6f4wWt6y2";

    /// <summary>getBySToken 的目标 token 类型：ltoken_v2。</summary>
    public const int TokenTypeLToken = 2;

    /// <summary>getBySToken 的目标 token 类型：cookie_token_v2。</summary>
    public const int TokenTypeCookieToken = 4;


    private readonly HttpClient _httpClient;


    /// <summary>设备 id，写入 <c>x-rpc-device_id</c>。</summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("D");


    /// <summary>设备指纹，写入 <c>x-rpc-device_fp</c>；为空时不带这个头。</summary>
    public string? DeviceFp { get; set; }


    /// <summary>设备名，写入 <c>x-rpc-device_name</c>。</summary>
    public string DeviceName { get; set; } = Environment.MachineName;


    /// <summary>界面语言，决定服务端错误提示的语言。</summary>
    public string Language { get; set => field = LanguageUtil.FilterLanguage(value); } = "en-us";


    /// <summary>
    /// 创建国际服 passport 客户端。
    /// </summary>
    /// <param name="httpClient">共享 HttpClient；为 null 时内部新建。</param>
    public HoyoversePassportClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
    }


    /// <summary>
    /// 账号密码登录。业务失败不抛异常；风控时 <see cref="PassportSendResult{T}.Aigis"/> 或 <see cref="PassportSendResult{T}.Verify"/> 非空。
    /// </summary>
    /// <param name="account">邮箱或用户名（明文，方法内加密）。</param>
    /// <param name="password">密码（明文，方法内加密，不落日志）。</param>
    /// <param name="aigisHeader">极验完成后的 <c>x-rpc-aigis</c> 头；可为 null。</param>
    /// <param name="riskTicket">身份或人机验证完成后回传的 risk_ticket；可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>含 retcode、data 与风控头的结果。</returns>
    public Task<PassportSendResult<HoyoverseLoginResult>> LoginByPasswordAsync(string account, string password, string? aigisHeader = null, string? riskTicket = null, CancellationToken cancellationToken = default)
    {
        var body = new HoyoverseLoginByPasswordRequest
        {
            Account = PassportRsa.EncryptGlobal(account.Trim()),
            Password = PassportRsa.EncryptGlobal(password),
        };
        var request = CreateJsonRequest(PassportBase + "account/ma-passport/api/appLoginByPassword", body, GameRecordJsonContext.Default.HoyoverseLoginByPasswordRequest);
        AddPassportHeaders(request);
        if (!string.IsNullOrWhiteSpace(aigisHeader))
        {
            request.Headers.TryAddWithoutValidation("x-rpc-aigis", aigisHeader);
        }
        if (!string.IsNullOrWhiteSpace(riskTicket))
        {
            // 与账号 SDK 一致：带 risk_ticket 重发时不再带 aigis 头（调用方传 null）
            request.Headers.TryAddWithoutValidation("x-rpc-verify", JsonSerializer.Serialize(new HoyoverseRiskVerify { RiskTicket = riskTicket }, GameRecordJsonContext.Default.HoyoverseRiskVerify));
        }
        return SendPassportAsync<HoyoverseLoginResult>(request, cancellationToken);
    }


    /// <summary>
    /// 用风控票据换取人机验证参数（<c>x-rpc-verify</c> 的极验类）。
    /// </summary>
    /// <param name="ticket">风控详情里的 ticket。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>mmt_type 与极验参数。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0。</exception>
    public async Task<HoyoverseSmartCaptchaResult> CreateBySmartCaptchaTicketAsync(string ticket, CancellationToken cancellationToken = default)
    {
        var request = CreateJsonRequest(PassportBase + "account/ma-aigis/api/createBySmartCaptchaTicket", new HoyoverseSmartCaptchaRequest { Ticket = ticket }, GameRecordJsonContext.Default.HoyoverseSmartCaptchaRequest);
        AddPassportHeaders(request);
        return await SendThrowingAsync<HoyoverseSmartCaptchaResult>(request, cancellationToken);
    }


    /// <summary>
    /// 提交人机验证结果。
    /// </summary>
    /// <param name="ticket">风控详情里的 ticket。</param>
    /// <param name="geetestValidateJson">极验 <c>getValidate()</c> 结果的 JSON 文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="miHoYoApiException">retcode 非 0。</exception>
    public async Task CheckSmartCaptchaAsync(string ticket, string geetestValidateJson, CancellationToken cancellationToken = default)
    {
        var body = new HoyoverseSmartCaptchaRequest
        {
            Ticket = ticket,
            // 账号 SDK：check_data = btoa(JSON.stringify(validate))
            CheckData = Convert.ToBase64String(Encoding.UTF8.GetBytes(geetestValidateJson)),
        };
        var request = CreateJsonRequest(PassportBase + "account/ma-aigis/api/checkSmartCaptcha", body, GameRecordJsonContext.Default.HoyoverseSmartCaptchaRequest);
        AddPassportHeaders(request);
        // 只看 retcode，data 可能为空
        await SendThrowingAsync<JsonNode>(request, cancellationToken, allowNullData: true);
    }


    /// <summary>
    /// 用 stoken 换 ltoken_v2 与 cookie_token_v2。
    /// </summary>
    /// <param name="stoken">登录得到的 stoken。</param>
    /// <param name="mid">账号 mid。</param>
    /// <param name="aid">账号 aid（Cookie 里的 stuid）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>换票结果，用 <see cref="HoyoverseGetBySTokenResult.GetToken"/> 取值。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0。</exception>
    public async Task<HoyoverseGetBySTokenResult> GetTokensBySTokenAsync(string stoken, string mid, string aid, CancellationToken cancellationToken = default)
    {
        var payload = new HoyoverseGetBySTokenRequest { DstTokenTypes = [TokenTypeLToken, TokenTypeCookieToken] };
        string body = JsonSerializer.Serialize(payload, GameRecordJsonContext.Default.HoyoverseGetBySTokenRequest);
        var request = new HttpRequestMessage(HttpMethod.Post, PublicApiBase + "account/ma-passport/token/getBySToken")
        {
            // DS 按请求体原文签名，所以发送的正是签名用的这段字符串
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddPassportHeaders(request);
        request.Headers.TryAddWithoutValidation("DS", CreatePassportSecret(body));
        request.Headers.TryAddWithoutValidation("Cookie", $"stoken={stoken};mid={mid};stuid={aid}");
        return await SendThrowingAsync<HoyoverseGetBySTokenResult>(request, cancellationToken);
    }


    /// <summary>
    /// 写入 HYP 国际服账号 SDK 的公共请求头。
    /// </summary>
    /// <param name="request">待发送的请求。</param>
    private void AddPassportHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", AppId);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "3");
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", GameBiz);
        request.Headers.TryAddWithoutValidation("x-rpc-sdk_version", SdkVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-language", Language);
        request.Headers.TryAddWithoutValidation("x-rpc-aigis_v4", "true");
        request.Headers.TryAddWithoutValidation("x-rpc-age_gate", "true");
        request.Headers.TryAddWithoutValidation("x-rpc-age_gate_br", "true");
        request.Headers.TryAddWithoutValidation("x-rpc-age_gate_eu", "true");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", DeviceId);
        if (!string.IsNullOrWhiteSpace(DeviceFp))
        {
            request.Headers.TryAddWithoutValidation("x-rpc-device_fp", DeviceFp);
        }
        // HYP 把设备名、系统名做 URL 编码后再放进头里（抓包为 Windows%2011%20Version%202009）
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", Uri.EscapeDataString(DeviceName));
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", "x86_64");
        request.Headers.TryAddWithoutValidation("x-rpc-device_os", Uri.EscapeDataString(Environment.OSVersion.Version.Build >= 22000 ? "Windows 11 Version 2009" : "Windows 10 Version 1909"));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }


    /// <summary>
    /// 创建 JSON 请求。
    /// </summary>
    private static HttpRequestMessage CreateJsonRequest<T>(string url, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, typeInfo), Encoding.UTF8, "application/json"),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
    }


    /// <summary>
    /// 生成 passport DS 签名（salt&amp;t&amp;r&amp;b&amp;q，POST 无 query）。
    /// </summary>
    /// <param name="body">与请求体逐字节一致的 JSON 原文。</param>
    /// <returns><c>t,r,check</c> 形式的 DS 头值。</returns>
    private static string CreatePassportSecret(string body)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890", 6);
        byte[] md5 = MD5.HashData(Encoding.UTF8.GetBytes($"salt={PassportDsSalt}&t={t}&r={r}&b={body}&q="));
        return $"{t},{r},{Convert.ToHexStringLower(md5)}";
    }


    /// <summary>
    /// 发送请求并解析 retcode 与 <c>x-rpc-aigis</c> / <c>x-rpc-verify</c> 风控头，不因业务 retcode 抛错。
    /// </summary>
    private async Task<PassportSendResult<T>> SendPassportAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        CaptchaAigis? aigis = TryParseHeader(response, "x-rpc-aigis", GameRecordJsonContext.Default.CaptchaAigis);
        HoyoverseRiskVerify? verify = TryParseHeader(response, "x-rpc-verify", GameRecordJsonContext.Default.HoyoverseRiskVerify);

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        miHoYoApiWrapper<T>? wrapper;
        try
        {
            wrapper = JsonSerializer.Deserialize(content, typeof(miHoYoApiWrapper<T>), GameRecordJsonContext.Default) as miHoYoApiWrapper<T>;
        }
        catch (JsonException ex)
        {
            return new PassportSendResult<T> { Retcode = -1, Message = ex.Message, Aigis = aigis, Verify = verify };
        }
        if (wrapper is null)
        {
            return new PassportSendResult<T> { Retcode = -1, Message = "Can not parse the response body.", Aigis = aigis, Verify = verify };
        }
        return new PassportSendResult<T>
        {
            Retcode = wrapper.Retcode,
            Message = wrapper.Message ?? string.Empty,
            Data = wrapper.Data,
            Aigis = aigis,
            Verify = verify,
        };
    }


    /// <summary>
    /// 发送请求，retcode 非 0 时抛 <see cref="miHoYoApiException"/>。
    /// </summary>
    /// <param name="request">待发送的请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="allowNullData">为 true 时 data 为空不算失败（返回值可能为 null，调用方不应使用）。</param>
    private async Task<T> SendThrowingAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken, bool allowNullData = false) where T : class
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        var wrapper = JsonSerializer.Deserialize(content, typeof(miHoYoApiWrapper<T>), GameRecordJsonContext.Default) as miHoYoApiWrapper<T>;
        if (wrapper is null)
        {
            throw new miHoYoApiException(-1, "Can not parse the response body.");
        }
        if (wrapper.Retcode != 0)
        {
            throw new miHoYoApiException(wrapper.Retcode, wrapper.Message);
        }
        if (allowNullData)
        {
            return wrapper.Data!;
        }
        return wrapper.Data ?? throw new miHoYoApiException(-1, "Response data is null.");
    }


    /// <summary>
    /// 解析 JSON 形式的响应头；不存在或格式不对时返回 null。
    /// </summary>
    private static T? TryParseHeader<T>(HttpResponseMessage response, string name, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }
        string? raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize(raw, typeInfo);
        }
        catch (JsonException)
        {
            // 头格式变了也不要让整个登录失败，由调用方按 retcode 提示
            return null;
        }
    }

}
