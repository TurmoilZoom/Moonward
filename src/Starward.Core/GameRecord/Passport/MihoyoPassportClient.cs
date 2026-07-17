using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 国服米游社 passport 客户端：短信验证码登录及 stoken 换票。
/// 与战绩 <see cref="GameRecordClient"/> 分离，因需读取 <c>x-rpc-aigis</c> 且 app_id 不同。
/// </summary>
public class MihoyoPassportClient
{

    private const string PassportBase = "https://passport-api.mihoyo.com/";
    private const string AppIdCaptcha = "bll8iq97cem8";
    private const string AppVersion = "2.90.1";
    /// <summary>DS salt2 / X4，与 HyperionClient 一致。</summary>
    private const string ApiSalt2 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";

    private readonly HttpClient _httpClient;


    /// <summary>设备 id，发码/登录时写入 <c>x-rpc-device_id</c>。</summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("D");


    /// <summary>设备指纹，发码/登录时写入 <c>x-rpc-device_fp</c>。</summary>
    public string DeviceFp { get; set; } = "0000000000000";


    /// <summary>设备名（展示用请求头）。</summary>
    public string DeviceName { get; set; } = "Starward";


    /// <summary>设备型号（展示用请求头）。</summary>
    public string DeviceModel { get; set; } = "PC";


    /// <summary>
    /// 创建 passport 客户端。
    /// </summary>
    /// <param name="httpClient">共享 HttpClient；为 null 时内部新建。</param>
    public MihoyoPassportClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
    }


    /// <summary>
    /// 发送登录短信验证码。业务失败时不抛异常；若响应含 aigis 则填入 <see cref="PassportSendResult{T}.Aigis"/>。
    /// </summary>
    /// <param name="phone">11 位国区手机号（未加密明文）。</param>
    /// <param name="aigisHeader">可选的 <c>x-rpc-aigis</c> 值（极验通过后重试时传入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>含 retcode / action_type / aigis 的发送结果。</returns>
    public Task<PassportSendResult<CreateLoginCaptchaResult>> CreateLoginCaptchaAsync(
        string phone,
        string? aigisHeader = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, string>
        {
            ["area_code"] = PassportRsa.Encrypt("+86"),
            ["mobile"] = PassportRsa.Encrypt(phone),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, PassportBase + "account/ma-cn-verifier/verifier/createLoginCaptcha")
        {
            Content = JsonContent.Create(body, options: PassportJsonOptions.Default),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        AddCaptchaHeaders(request, aigisHeader, includeGameBiz: true);
        return SendPassportAsync<CreateLoginCaptchaResult>(request, cancellationToken);
    }


    /// <summary>
    /// 使用短信验证码登录。业务失败时不抛异常；若响应含 aigis 则填入结果。
    /// </summary>
    /// <param name="phone">11 位国区手机号。</param>
    /// <param name="captcha">用户输入的短信验证码。</param>
    /// <param name="actionType">发码成功返回的 action_type。</param>
    /// <param name="aigisHeader">可选 aigis 头。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>含 stoken / user_info 的登录结果。</returns>
    public Task<PassportSendResult<LoginByMobileCaptchaResult>> LoginByMobileCaptchaAsync(
        string phone,
        string captcha,
        string actionType,
        string? aigisHeader = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, string>
        {
            ["area_code"] = PassportRsa.Encrypt("+86"),
            ["mobile"] = PassportRsa.Encrypt(phone),
            ["action_type"] = actionType,
            ["captcha"] = captcha,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, PassportBase + "account/ma-cn-passport/app/loginByMobileCaptcha")
        {
            Content = JsonContent.Create(body, options: PassportJsonOptions.Default),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        AddCaptchaHeaders(request, aigisHeader, includeGameBiz: false);
        return SendPassportAsync<LoginByMobileCaptchaResult>(request, cancellationToken);
    }


    /// <summary>
    /// 通过 stoken 换取 ltoken（对齐 TeyvatGuide <c>getLTokenBySToken</c>）。
    /// </summary>
    /// <param name="stoken">登录得到的 stoken。</param>
    /// <param name="mid">账号 mid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>ltoken 字符串。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0。</exception>
    public async Task<string> GetLTokenBySTokenAsync(string stoken, string mid, CancellationToken cancellationToken = default)
    {
        // query 不预编码，与 TeyvatGuide transParams + DS 计算一致；由 HttpClient 负责传输编码
        string url = $"{PassportBase}account/auth/api/getLTokenBySToken?stoken={stoken}";
        var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        AddAuthBySTokenHeaders(request, stoken, mid, url);
        var data = await SendThrowingAsync<LTokenBySTokenResult>(request, cancellationToken);
        return data.LToken;
    }


    /// <summary>
    /// 通过 stoken 换取 cookie_token 与账号 uid（对齐 TeyvatGuide <c>getCookieAccountInfoBySToken</c>）。
    /// </summary>
    /// <param name="stoken">登录得到的 stoken。</param>
    /// <param name="mid">账号 mid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>含 <c>cookie_token</c> 与可选 <c>uid</c> 的换票结果。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0。</exception>
    public async Task<CookieTokenBySTokenResult> GetCookieAccountInfoBySTokenAsync(string stoken, string mid, CancellationToken cancellationToken = default)
    {
        string url = $"{PassportBase}account/auth/api/getCookieAccountInfoBySToken?stoken={stoken}";
        var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        AddAuthBySTokenHeaders(request, stoken, mid, url);
        return await SendThrowingAsync<CookieTokenBySTokenResult>(request, cancellationToken);
    }


    /// <summary>
    /// 通过 stoken 换取 cookie_token 字符串（便捷包装）。
    /// </summary>
    /// <param name="stoken">登录得到的 stoken。</param>
    /// <param name="mid">账号 mid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>cookie_token 字符串。</returns>
    /// <exception cref="miHoYoApiException">retcode 非 0。</exception>
    public async Task<string> GetCookieTokenBySTokenAsync(string stoken, string mid, CancellationToken cancellationToken = default)
    {
        CookieTokenBySTokenResult data = await GetCookieAccountInfoBySTokenAsync(stoken, mid, cancellationToken);
        return data.CookieToken;
    }


    /// <summary>
    /// 将极验验证结果格式化为 <c>x-rpc-aigis</c> 请求头值：<c>session_id;base64(json)</c>。
    /// </summary>
    /// <param name="aigis">服务端下发的 aigis 会话。</param>
    /// <param name="geetestValidateJson">极验 <c>getValidate()</c> 返回对象的 JSON 字符串。</param>
    /// <returns>可写入请求头的 aigis 字符串。</returns>
    public static string FormatAigisHeader(CaptchaAigis aigis, string geetestValidateJson)
    {
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(geetestValidateJson));
        return $"{aigis.SessionId};{b64}";
    }


    /// <summary>
    /// 由 aid / mid / stoken / ltoken / cookie_token 组装 GameRecord 可用的 Cookie 字符串。
    /// </summary>
    /// <param name="aid">账号 id。</param>
    /// <param name="mid">mid。</param>
    /// <param name="stoken">stoken。</param>
    /// <param name="ltoken">ltoken。</param>
    /// <param name="cookieToken">cookie_token。</param>
    /// <returns>分号分隔的 Cookie 串。</returns>
    public static string BuildCookieString(string aid, string mid, string stoken, string ltoken, string cookieToken)
    {
        // 同时写入 v1/v2 常见键名，兼容 Cookie 登录与 getUserGameRolesByCookieToken
        return string.Join(';',
            $"account_id={aid}",
            $"account_id_v2={aid}",
            $"ltuid={aid}",
            $"ltuid_v2={aid}",
            $"stuid={aid}",
            $"login_uid={aid}",
            $"mid={mid}",
            $"stoken={stoken}",
            $"ltoken={ltoken}",
            $"ltoken_v2={ltoken}",
            $"cookie_token={cookieToken}",
            $"cookie_token_v2={cookieToken}");
    }


    /// <summary>发码 / 登录用移动端 UA（对齐 TeyvatGuide <c>TGBbs.ua</c>）。</summary>
    private string MobileUA =>
        $"Mozilla/5.0 (Linux; Android 12) AppleWebKit/537.36 (KHTML, like Gecko) Mobile miHoYoBBS/{AppVersion}";


    /// <summary>换票用桌面端 UA（对齐 TeyvatGuide <c>TGBbs.uap</c> / getRequestHeader）。</summary>
    private string DesktopUA =>
        $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}";


    /// <summary>
    /// 写入发码 / 登录共用的 passport 请求头。
    /// </summary>
    private void AddCaptchaHeaders(HttpRequestMessage request, string? aigisHeader, bool includeGameBiz)
    {
        request.Headers.TryAddWithoutValidation("x-rpc-aigis", aigisHeader ?? "");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "2");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", AppIdCaptcha);
        request.Headers.TryAddWithoutValidation("x-rpc-device_fp", DeviceFp);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", DeviceName);
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", DeviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", DeviceModel);
        request.Headers.TryAddWithoutValidation("User-Agent", MobileUA);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (includeGameBiz)
        {
            // 仅 createLoginCaptcha 需要 referer + game_biz（与 TeyvatGuide 一致）
            request.Headers.TryAddWithoutValidation("Referer", "https://user.miyoushe.com/");
            request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "hk4e_cn");
        }
    }


    /// <summary>
    /// stoken 换 ltoken / cookie_token 的请求头（对齐 TeyvatGuide <c>getRequestHeader</c>）。
    /// </summary>
    private void AddAuthBySTokenHeaders(HttpRequestMessage request, string stoken, string mid, string url)
    {
        // cookie 键顺序与 TeyvatGuide transCookie 排序一致：mid;stoken
        request.Headers.TryAddWithoutValidation("Cookie", $"mid={mid};stoken={stoken}");
        request.Headers.TryAddWithoutValidation("DS", CreateSecret2(url));
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", DeviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_fp", DeviceFp);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "com.mihoyo.hyperion");
        request.Headers.TryAddWithoutValidation("Referer", "https://webstatic.mihoyo.com");
        request.Headers.TryAddWithoutValidation("User-Agent", DesktopUA);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }


    /// <summary>
    /// 发送请求并解析 retcode / aigis，不因业务 retcode 抛错。
    /// </summary>
    private async Task<PassportSendResult<T>> SendPassportAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken) where T : class
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        CaptchaAigis? aigis = null;
        if (response.Headers.TryGetValues("x-rpc-aigis", out var values))
        {
            string? raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    aigis = JsonSerializer.Deserialize(raw, typeof(CaptchaAigis), GameRecordJsonContext.Default) as CaptchaAigis;
                }
                catch
                {
                    // 头解析失败时仍返回 retcode/message，由上层展示
                }
            }
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        miHoYoApiWrapper<T>? wrapper;
        try
        {
            wrapper = JsonSerializer.Deserialize(content, typeof(miHoYoApiWrapper<T>), GameRecordJsonContext.Default) as miHoYoApiWrapper<T>;
        }
        catch (JsonException ex)
        {
            // 避免「短信已发出但 data 字段类型不匹配」时整段失败被当成业务错误刷屏
            return new PassportSendResult<T>
            {
                Retcode = -1,
                Message = ex.Message,
                Aigis = aigis,
            };
        }

        if (wrapper is null)
        {
            return new PassportSendResult<T>
            {
                Retcode = -1,
                Message = "Can not parse the response body.",
                Aigis = aigis,
            };
        }
        return new PassportSendResult<T>
        {
            Retcode = wrapper.Retcode,
            Message = wrapper.Message ?? string.Empty,
            Data = wrapper.Data,
            Aigis = aigis,
        };
    }


    /// <summary>
    /// 发送请求，retcode 非 0 时抛 <see cref="miHoYoApiException"/>。
    /// </summary>
    private async Task<T> SendThrowingAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken) where T : class
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
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
        return wrapper.Data ?? throw new miHoYoApiException(-1, "Response data is null.");
    }


    /// <summary>
    /// 生成 GET 请求 DS（salt2 / 含 query 排序）。
    /// </summary>
    private static string CreateSecret2(string url)
    {
        int t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = Random.Shared.Next(100000, 200000).ToString();
        string b = "";
        string q = "";
        string[] urls = url.Split('?');
        if (urls.Length == 2)
        {
            string[] queryParams = urls[1].Split('&').OrderBy(x => x).ToArray();
            q = string.Join("&", queryParams);
        }
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"salt={ApiSalt2}&t={t}&r={r}&b={b}&q={q}"));
        var check = Convert.ToHexString(bytes).ToLower();
        return $"{t},{r},{check}";
    }

}


/// <summary>
/// passport 请求体序列化选项（属性名即字典键，无需源生成 context）。
/// </summary>
file static class PassportJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
