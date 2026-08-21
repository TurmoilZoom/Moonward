using Starward.Core.GameRecord.Genshin.SpiralAbyss;
using Starward.Core.GameRecord.Genshin.TravelersDiary;
using Starward.Core.GameRecord.StarRail.ForgottenHall;
using Starward.Core.GameRecord.StarRail.PureFiction;
using Starward.Core.GameRecord.StarRail.ApocalypticShadow;
using Starward.Core.GameRecord.StarRail.SimulatedUniverse;
using Starward.Core.GameRecord.StarRail.TrailblazeCalendar;
using System.Net;
using Starward.Core.GameRecord.Genshin.ImaginariumTheater;
using Starward.Core.GameRecord.ZZZ.ShiyuDefense;
using Starward.Core.GameRecord.ZZZ.DeadlyAssault;
using Starward.Core.GameRecord.ZZZ.InterKnotReport;
using Starward.Core.Gacha.ZZZ;
using Starward.Core.GameRecord.ZZZ.UpgradeGuide;
using Starward.Core.GameRecord.SignIn;
using Starward.Core.GameRecord.Genshin.DailyNote;
using Starward.Core.GameRecord.StarRail.DailyNote;
using Starward.Core.GameRecord.ZZZ.DailyNote;
using Starward.Core.GameRecord.BH3.DailyNote;
using Starward.Core.GameRecord.Genshin.StygianOnslaught;
using Starward.Core.GameRecord.ZZZ.ThresholdSimulation;
using Starward.Core.GameRecord.StarRail.ChallengePeak;
using Starward.Core.GameRecord.ZZZ.GachaRecord;
using Starward.Core.GameRecord.Passport;





#if !DEBUG
using System.Net.Http.Json;
#endif
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Starward.Core.GameRecord;

public abstract class GameRecordClient
{


    #region Constant

    protected const string Accept = "Accept";
    protected const string Cookie = "Cookie";
    protected const string UserAgent = "User-Agent";
    protected const string X_Request_With = "X-Requested-With";
    protected const string DS = "DS";
    protected const string Referer = "Referer";
    protected const string Origin = "Origin";
    protected const string Application_Json = "application/json";
    protected const string com_mihoyo_hyperion = "com.mihoyo.hyperion";
    protected const string com_mihoyo_hoyolab = "com.mihoyo.hoyolab";
    protected const string x_rpc_app_version = "x-rpc-app_version";
    protected const string x_rpc_device_id = "x-rpc-device_id";
    protected const string x_rpc_device_fp = "x-rpc-device_fp";
    protected const string x_rpc_device_name = "x-rpc-device_name";
    protected const string x_rpc_client_type = "x-rpc-client_type";
    protected const string x_rpc_language = "X-Rpc-Language";
    protected const string x_rpc_lang = "x-rpc-lang";
    protected const string x_rpc_platform = "x-rpc-platform";
    protected const string x_rpc_sys_version = "x-rpc-sys_version";
    protected const string x_rpc_page = "x-rpc-page";
    protected const string x_rpc_tool_verison = "x-rpc-tool_verison";
    protected const string x_rpc_lrsag = "x-rpc-lrsag";
    protected const string x_rpc_geetest_ext = "x-rpc-geetest_ext";
    protected const string x_rpc_aigis = "x-rpc-aigis";
    protected const string x_rpc_challenge = "x-rpc-challenge";

    /// <summary>绝区零战绩 H5（<c>mihoyo-zzz-game-record</c>）版本，对齐米游社 2.112.0 的 <c>x-op-env</c>。</summary>
    protected const string ZzzGameRecordH5Version = "v3.0.10";

    /// <summary>绝区零战绩首页的 <c>x-rpc-page</c>。</summary>
    public const string ZzzGameRecordH5Page = "v3.0.10_#/zzz";

    /// <summary>绝区零绳网月报页的 <c>x-rpc-page</c>。</summary>
    public const string ZzzNotebookH5Page = "v3.0.10_#/zzz/notebook";

    /// <summary>绝区零在米游社的 gameId（战绩 H5 / <c>x-rpc-geetest_ext</c>）。</summary>
    protected const int ZzzGameId = 8;

    /// <summary>与 <see cref="UAContent"/> 中 Pixel 5 / Android 13 对齐，供战绩 H5 头使用。</summary>
    public const string RpcDeviceName = "Pixel 5";

    /// <summary>与 <see cref="UAContent"/> 中 Android 13 对齐。</summary>
    public const string RpcSysVersion = "13";

    /// <summary>
    /// 养成指南 H5 接口使用桌面浏览器 UA（勿用 BBS 手机 UA，易触发 10035 极验风控）。
    /// </summary>
    protected const string CultivateToolUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    #endregion



    public abstract string UAContent { get; }

    public abstract string AppVersion { get; }

    /// <summary>国服/国际服 BBS Gen1 DS 盐。</summary>
    protected abstract string ApiSalt { get; }

    /// <summary>国服/国际服战绩 Gen2 DS 盐（salt2，按 URL query 签名）。</summary>
    protected abstract string ApiSalt2 { get; }

    /// <summary>
    /// H5 活动页默认 Origin（对齐米游社 / HoYoLAB WebView：act.mihoyo.com / act.hoyolab.com）。
    /// </summary>
    protected abstract string ActOrigin { get; }

    /// <summary>
    /// <c>X-Requested-With</c> 包名（国服 <c>com.mihoyo.hyperion</c>，国际服 <c>com.mihoyo.hoyolab</c>）。
    /// </summary>
    protected abstract string XRequestedWithValue { get; }

    /// <summary>
    /// 战绩 H5 的 <c>x-rpc-lang</c> / <c>x-rpc-language</c>。国服固定 zh-cn；国际服用当前界面语言。
    /// </summary>
    protected virtual string RpcLanguage => "zh-cn";

    public string DeviceId { get; set; } = Guid.NewGuid().ToString("D");

    public string DeviceFp { get; set; } = "0000000000000";

    /// <summary>getFp 的 seed_id，需与 <see cref="DeviceFp"/> 一起稳定复用，并写入 DEVICEFP_SEED_ID Cookie。</summary>
    public string DeviceFpSeedId { get; set; } = "";

    /// <summary>getFp 的 seed_time（毫秒时间戳字符串）。</summary>
    public string DeviceFpSeedTime { get; set; } = "";

    /// <summary>getFp 体里的 16 位 hex device_id（模拟 ANDROID_ID），跨刷新保持不变。</summary>
    public string DeviceAndroidId { get; set; } = "";

    /// <summary>极验通过后重试战绩请求时带上的 <c>x-rpc-aigis</c>。</summary>
    public string? RiskAigisHeader { get; set; }

    /// <summary>极验通过后重试战绩请求时带上的 <c>x-rpc-challenge</c>。</summary>
    public string? RiskChallenge { get; set; }


    protected readonly HttpClient _httpClient;




    public GameRecordClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
    }




    #region Dynamic Secret


    private static string GetRandomString(int timestamp)
    {
        var sb = new StringBuilder(6);
        var random = new Random(timestamp);
        for (int i = 0; i < 6; i++)
        {
            int v8 = random.Next(0, 32768) % 26;
            int v9 = 87;
            if (v8 < 10)
            {
                v9 = 48;
            }
            _ = sb.Append((char)(v8 + v9));
        }
        return sb.ToString();
    }


    /// <summary>
    /// 生成 Gen1 DS 签名（salt&amp;t&amp;r，无 body/query），使用默认 <see cref="ApiSalt"/>。
    /// </summary>
    protected string CreateSecret()
    {
        return CreateSecret(ApiSalt);
    }


    /// <summary>
    /// 生成 Gen1 DS 签名（salt&amp;t&amp;r），使用指定 salt。
    /// </summary>
    protected string CreateSecret(string salt)
    {
        var t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GetRandomString(t);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"salt={salt}&t={t}&r={r}"));
        var check = Convert.ToHexString(bytes).ToLower();
        return $"{t},{r},{check}";
    }


    /// <summary>
    /// 生成 Gen2 DS 签名（salt&amp;t&amp;r&amp;b&amp;q），query 按键排序。对齐上游 Starward。
    /// </summary>
    protected string CreateSecret2(string url)
    {
        string q = "";
        string[] urls = url.Split('?');
        if (urls.Length == 2)
        {
            string[] queryParams = urls[1].Split('&').OrderBy(x => x).ToArray();
            q = string.Join("&", queryParams);
        }
        return CreateSecret2Parts(q, "");
    }


    /// <summary>
    /// 生成带 POST body 的 Gen2 DS 签名。
    /// </summary>
    protected string CreateSecret2<T>(string url, T postBody)
    {
        string b = JsonSerializer.Serialize(postBody, typeof(T), GameRecordJsonContext.Default);
        string q = "";
        string[] urls = url.Split('?');
        if (urls.Length == 2)
        {
            string[] queryParams = urls[1].Split('&').OrderBy(x => x).ToArray();
            q = string.Join("&", queryParams);
        }
        return CreateSecret2Parts(q, b);
    }


    /// <summary>
    /// 给米游社 H5 JS 桥 <c>getDS</c> 用的 Gen1 DS。空串会被 v7 战绩页当成旧客户端，映射为 retcode -10001（「请更新至 V2.10 以上」）。
    /// </summary>
    public string CreateJsBridgeSecret() => CreateSecret();


    /// <summary>
    /// 给米游社 H5 JS 桥 <c>getDS2</c> 用的 Gen2 DS。
    /// </summary>
    /// <param name="query">已按官方规则排好序的 <c>k=v&amp;k=v</c>；无 query 传空串。</param>
    /// <param name="body">POST JSON 原文；GET 传空串。</param>
    public string CreateJsBridgeSecret2(string query, string body) => CreateSecret2Parts(query, body);


    /// <summary>
    /// 用已拆好的 query / body 生成 Gen2 DS。
    /// </summary>
    private string CreateSecret2Parts(string query, string body)
    {
        int t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = Random.Shared.Next(100000, 200000).ToString();
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"salt={ApiSalt2}&t={t}&r={r}&b={body}&q={query}"));
        var check = Convert.ToHexString(bytes).ToLower();
        return $"{t},{r},{check}";
    }


    #endregion




    #region Common Method




    /// <summary>
    /// 补齐与官方 H5 一致的 Origin / X-Requested-With；已存在则不覆盖（如签到 nap 专用 Origin）。
    /// </summary>
    protected void EnsureActWebViewHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(Origin, ActOrigin);
        request.Headers.TryAddWithoutValidation(X_Request_With, XRequestedWithValue);
    }


    /// <summary>
    /// 对齐米游社战绩 H5 的 <c>game_record/app</c> 请求头：Cookie + Gen2 DS + Referer + 设备指纹 + page/tool。
    /// 缺 <c>x-rpc-page</c> / <c>platform</c> 等时服务端易返回 10035。
    /// </summary>
    protected void AddGameRecordAppHeaders(HttpRequestMessage request, string? cookie, string url, string referer = "https://webstatic.mihoyo.com/")
    {
        request.Headers.Add(Cookie, MergeDeviceFpCookies(cookie));
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, referer);
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.TryAddWithoutValidation(x_rpc_platform, "5");
        request.Headers.TryAddWithoutValidation(x_rpc_sys_version, RpcSysVersion);
        // 官方 H5 对空格做百分号编码（如 Vivo%20V2329A）
        request.Headers.TryAddWithoutValidation(x_rpc_device_name, Uri.EscapeDataString(RpcDeviceName));
        ResolveGameRecordH5Identity(url, out string toolVersion, out string page);
        request.Headers.TryAddWithoutValidation(x_rpc_tool_verison, toolVersion);
        request.Headers.TryAddWithoutValidation(x_rpc_page, page);
        TryAddOriginFromReferer(request, referer);
        request.Headers.TryAddWithoutValidation(X_Request_With, XRequestedWithValue);
    }


    /// <summary>
    /// 按战绩接口路径选择官方 H5 的 <c>x-rpc-tool_verison</c> / <c>x-rpc-page</c>（字段名官方拼写就是 verison）。
    /// </summary>
    private static void ResolveGameRecordH5Identity(string url, out string toolVersion, out string page)
    {
        // 星铁：抓包米游社 2.112.0 打开 rpg/index.html 时为 v4.4.0
        if (url.Contains("/hkrpg/", StringComparison.OrdinalIgnoreCase))
        {
            toolVersion = "v4.4.0";
            page = "v4.4.0_#/rpg";
            return;
        }
        // 原神统一战绩页 v7.0.0-gr-cn
        if (url.Contains("/genshin/", StringComparison.OrdinalIgnoreCase))
        {
            toolVersion = "v7.0.0-gr-cn";
            page = "v7.0.0-gr-cn_#";
            return;
        }
        if (url.Contains("zzz", StringComparison.OrdinalIgnoreCase))
        {
            toolVersion = ZzzGameRecordH5Version;
            page = ZzzGameRecordH5Page;
            return;
        }
        toolVersion = "v7.0.0-gr-cn";
        page = "v7.0.0-gr-cn_#";
    }


    /// <summary>
    /// 从 Referer 推导 Origin，对齐官方 H5（星铁战绩 Origin 为 webstatic，而非 act）。
    /// </summary>
    private static void TryAddOriginFromReferer(HttpRequestMessage request, string referer)
    {
        if (Uri.TryCreate(referer, UriKind.Absolute, out Uri? uri))
        {
            request.Headers.TryAddWithoutValidation(Origin, uri.GetLeftPart(UriPartial.Authority));
        }
    }


    /// <summary>
    /// 对齐米游社绝区零战绩 H5（<c>act.mihoyo.com/app/mihoyo-zzz-game-record</c>）WebView 注入的请求头。
    /// 官方请求不带 DS / <c>x-rpc-client_type</c> / <c>x-rpc-tool_verison</c>；带 <c>x-rpc-geetest_ext</c> 与 <c>x-rpc-platform: 2</c>。
    /// 继续按原神/星铁战绩头发送时，服务端易返回 10041（账号存在风险）。
    /// </summary>
    /// <param name="request">待发送的 HTTP 请求。</param>
    /// <param name="role">当前游戏角色（Cookie、区服用于 geetest_ext）。</param>
    /// <param name="requestUrl">用于选择 <c>x-rpc-page</c> 的 URL；空则用 <paramref name="request"/> 的 URI。</param>
    protected void AddZZZGameRecordH5Headers(HttpRequestMessage request, GameRecordRole role, string? requestUrl = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(role);
        request.Headers.TryAddWithoutValidation(Cookie, MergeDeviceFpCookies(role.Cookie));
        string? url = requestUrl ?? request.RequestUri?.OriginalString;
        foreach (var header in GetZZZGameRecordH5InjectHeaders(role, url))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }


    /// <summary>
    /// 绝区零战绩 H5 由米游社客户端注入的请求头（不含 Cookie，Cookie 由调用方或 WebView 携带）。
    /// </summary>
    /// <param name="role">当前游戏角色。</param>
    /// <param name="requestUrl">请求 URL，用于区分首页与绳网月报的 <c>x-rpc-page</c>。</param>
    /// <returns>按官方抓包顺序的请求头。</returns>
    public IReadOnlyList<KeyValuePair<string, string>> GetZZZGameRecordH5InjectHeaders(GameRecordRole role, string? requestUrl = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        string page = ResolveZZZGameRecordH5Page(requestUrl);
        string referer = ActOrigin.TrimEnd('/') + "/";
        string origin = Uri.TryCreate(referer, UriKind.Absolute, out Uri? uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : ActOrigin;
        string viewUid = GetBbsUidFromCookie(role.Cookie);
        string server = role.Region ?? "";
        string geetestExt = $$"""{"viewUid":"{{JsonEscapeHeaderValue(viewUid)}}","server":"{{JsonEscapeHeaderValue(server)}}","gameId":{{ZzzGameId}},"page":"{{JsonEscapeHeaderValue(page)}}","isHost":1,"viewSource":3,"actionSource":127}""";
        return
        [
            new(Referer, referer),
            new(Origin, origin),
            new(x_rpc_app_version, AppVersion),
            new(x_rpc_device_id, DeviceId),
            new(x_rpc_device_fp, DeviceFp),
            new(x_rpc_device_name, Uri.EscapeDataString(RpcDeviceName)),
            new(x_rpc_sys_version, RpcSysVersion),
            new(x_rpc_platform, "2"),
            new(x_rpc_page, page),
            new(x_rpc_lang, RpcLanguage),
            new(x_rpc_language, RpcLanguage),
            new(x_rpc_lrsag, ""),
            new(X_Request_With, XRequestedWithValue),
            new(x_rpc_geetest_ext, geetestExt),
        ];
    }


    /// <summary>
    /// 绳网月报走 notebook 页；其余绝区零战绩接口走首页。
    /// </summary>
    private static string ResolveZZZGameRecordH5Page(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && url.Contains("nap_ledger", StringComparison.OrdinalIgnoreCase))
        {
            return ZzzNotebookH5Page;
        }
        return ZzzGameRecordH5Page;
    }


    /// <summary>
    /// 从 Cookie 取米游社通行证 UID，供 <c>x-rpc-geetest_ext.viewUid</c> 使用；缺失时与官方 notebook 请求一致填 <c>0</c>。
    /// </summary>
    private static string GetBbsUidFromCookie(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return "0";
        }
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        foreach (string key in new[] { "account_id_v2", "account_id", "ltuid_v2", "ltuid", "stuid" })
        {
            if (map.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return "0";
    }


    /// <summary>
    /// 转义写入 JSON 请求头的字符串，避免 Cookie 异常值破坏 geetest_ext。
    /// </summary>
    private static string JsonEscapeHeaderValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }


    protected virtual async Task<T> CommonSendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken = default) where T : class
    {
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        request.Headers.Add(Accept, Application_Json);
        request.Headers.Add(UserAgent, UAContent);
        TryApplyRiskControlHeaders(request);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
#if DEBUG
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseData = JsonSerializer.Deserialize(content, typeof(miHoYoApiWrapper<T>), GameRecordJsonContext.Default) as miHoYoApiWrapper<T>;
#else
        var responseData = await response.Content.ReadFromJsonAsync(typeof(miHoYoApiWrapper<T>), GameRecordJsonContext.Default, cancellationToken) as miHoYoApiWrapper<T>;
#endif
        if (responseData is null)
        {
            throw new miHoYoApiException(-1, "Can not parse the response body.", TryParseAigis(response));
        }
        if (responseData.Retcode != 0)
        {
            throw new miHoYoApiException(responseData.Retcode, responseData.Message, TryParseAigis(response));
        }
        return responseData.Data;
    }


    /// <summary>
    /// 极验通过后把 aigis / challenge 带到下一次战绩请求。
    /// </summary>
    private void TryApplyRiskControlHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(RiskAigisHeader))
        {
            request.Headers.TryAddWithoutValidation(x_rpc_aigis, RiskAigisHeader);
        }
        if (!string.IsNullOrWhiteSpace(RiskChallenge))
        {
            request.Headers.TryAddWithoutValidation(x_rpc_challenge, RiskChallenge);
        }
    }


    /// <summary>
    /// 解析风控响应头 <c>x-rpc-aigis</c>。没有或无法解析时返回 null。
    /// </summary>
    private static CaptchaAigis? TryParseAigis(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(x_rpc_aigis, out var values))
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
            return JsonSerializer.Deserialize(raw, typeof(CaptchaAigis), GameRecordJsonContext.Default) as CaptchaAigis;
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// 把 getFp 得到的指纹写入 Cookie，对齐官方战绩 H5 携带的 DEVICEFP / _MHYUUID。
    /// </summary>
    protected string MergeDeviceFpCookies(string? cookie)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            foreach (string part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
            }
        }
        if (!string.IsNullOrWhiteSpace(DeviceFp) && DeviceFp is not "0000000000000")
        {
            map["DEVICEFP"] = DeviceFp;
        }
        if (!string.IsNullOrWhiteSpace(DeviceFpSeedId))
        {
            map["DEVICEFP_SEED_ID"] = DeviceFpSeedId;
        }
        if (!string.IsNullOrWhiteSpace(DeviceFpSeedTime))
        {
            map["DEVICEFP_SEED_TIME"] = DeviceFpSeedTime;
        }
        if (!string.IsNullOrWhiteSpace(DeviceId))
        {
            map["_MHYUUID"] = DeviceId;
        }
        return string.Join("; ", map.Select(static kv => $"{kv.Key}={kv.Value}"));
    }



    protected virtual async Task CommonSendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        _ = await CommonSendAsync<object>(request, cancellationToken);
    }


    /// <summary>
    /// 养成指南 / badge 接口发送：使用浏览器 UA，避免 BBS 客户端标识触发 act 风控。
    /// </summary>
    protected async Task<T> CommonSendCultivateAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken = default) where T : class
    {
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        request.Headers.TryAddWithoutValidation(Accept, Application_Json);
        // 覆盖或补齐为桌面浏览器 UA（勿用 miHoYoBBS 手机 UA）
        request.Headers.Remove(UserAgent);
        request.Headers.TryAddWithoutValidation(UserAgent, CultivateToolUserAgent);
        EnsureActWebViewHeaders(request);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
#if DEBUG
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseData = JsonSerializer.Deserialize(content, typeof(miHoYoApiWrapper<T>), GameRecordJsonContext.Default) as miHoYoApiWrapper<T>;
#else
        var responseData = await response.Content.ReadFromJsonAsync(typeof(miHoYoApiWrapper<T>), GameRecordJsonContext.Default, cancellationToken) as miHoYoApiWrapper<T>;
#endif
        if (responseData is null)
        {
            throw new miHoYoApiException(-1, "Can not parse the response body.");
        }
        if (responseData.Retcode != 0)
        {
            throw new miHoYoApiException(responseData.Retcode, responseData.Message);
        }
        return responseData.Data;
    }


    /// <summary>
    /// 养成指南 badge 登录发送：与 <see cref="CommonSendCultivateAsync{T}"/> 相同 UA，并返回响应以便合并 Set-Cookie。
    /// </summary>
    protected async Task<(miHoYoApiWrapper<object>? Wrapper, HttpResponseMessage Response)> SendCultivateBadgeLoginAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        request.Headers.TryAddWithoutValidation(Accept, Application_Json);
        request.Headers.Remove(UserAgent);
        request.Headers.TryAddWithoutValidation(UserAgent, CultivateToolUserAgent);
        EnsureActWebViewHeaders(request);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var wrapper = JsonSerializer.Deserialize(content, typeof(miHoYoApiWrapper<object>), GameRecordJsonContext.Default) as miHoYoApiWrapper<object>;
        return (wrapper, response);
    }


    #endregion






    /// <summary>
    /// 米游社账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <returns></returns>
    public abstract Task<GameRecordUser> GetGameRecordUserAsync(string cookie, CancellationToken cancellationToken = default);



    /// <summary>
    /// 所有游戏账号
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<List<GameRecordRole>> GetAllGameRolesAsync(string cookie, CancellationToken cancellationToken = default);


    /// <summary>
    /// 获取游戏账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="gameBiz"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<List<GameRecordRole>> GetGameRolesAsync(string cookie, GameBiz gameBiz, CancellationToken cancellationToken = default);



    /// <summary>
    /// 获取游戏账号头像
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected abstract Task<string> GetGameRoleHeadIconAsync(GameRecordRole role, CancellationToken cancellationToken = default);


    /// <summary>
    /// 更新游戏账号头像
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<GameRecordRole> UpdateGameRoleHeadIconAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        role.HeadIcon = await GetGameRoleHeadIconAsync(role, cancellationToken);
        return role;
    }



    /// <summary>
    /// 获取设备指纹信息
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<string> GetDeviceFpAsync(CancellationToken cancellationToken = default);




    #region BH3


    /// <summary>
    /// 崩坏3实时便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<BH3DailyNote> GetBH3DailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default);




    #endregion




    #region Genshin


    /// <summary>
    /// 获取原神账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<List<GameRecordRole>> GetGenshinGameRolesAsync(string cookie, CancellationToken cancellationToken = default);


    /// <summary>
    /// 深境螺旋
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<SpiralAbyssInfo> GetSpiralAbyssInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default);


    /// <summary>
    /// 旅行札记总览
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">0 当前月</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>

    public abstract Task<TravelersDiarySummary> GetTravelsDiarySummaryAsync(GameRecordRole role, int month = 0, CancellationToken cancellationToken = default);


    /// <summary>
    /// 旅行札记收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month"></param>
    /// <param name="type"></param>
    /// <param name="page">从1开始</param>
    /// <param name="limit">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回一页收入记录</returns>
    public abstract Task<TravelersDiaryDetail> GetTravelsDiaryDetailByPageAsync(GameRecordRole role, int month, int type, int page, int limit = 100, CancellationToken cancellationToken = default);


    /// <summary>
    /// 旅行札记收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month"></param>
    /// <param name="type"></param>
    /// <param name="limit">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回该月所有收入记录</returns>
    public abstract Task<TravelersDiaryDetail> GetTravelsDiaryDetailAsync(GameRecordRole role, int month, int type, int limit = 100, CancellationToken cancellationToken = default);



    /// <summary>
    /// 幻想真境剧诗
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<List<ImaginariumTheaterInfo>> GetImaginariumTheaterInfosAsync(GameRecordRole role, CancellationToken cancellationToken = default);



    /// <summary>
    /// 幽境危战
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<List<StygianOnslaughtInfo>> GetStygianOnslaughtInfosAsync(GameRecordRole role, CancellationToken cancellationToken = default);



    /// <summary>
    /// 原神每日便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<GenshinDailyNote> GetGenshinDailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default);



    #endregion




    #region StarRail


    /// <summary>
    /// 获取星穹铁道账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException">输入的 <c>cookie</c> 为空</exception>
    public abstract Task<List<GameRecordRole>> GetStarRailGameRolesAsync(string cookie, CancellationToken cancellationToken = default);


    /// <summary>
    /// 忘却之庭
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ForgottenHallInfo> GetForgottenHallInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default);


    /// <summary>
    /// 虚构叙事
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<PureFictionInfo> GetPureFictionInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default);


    /// <summary>
    /// 末日幻影
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ApocalypticShadowInfo> GetApocalypticShadowInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default);


    /// <summary>
    /// 模拟宇宙
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<SimulatedUniverseInfo> GetSimulatedUniverseInfoAsync(GameRecordRole role, bool detail = false, CancellationToken cancellationToken = default);


    /// <summary>
    /// 开拓月历总结
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">还不清楚规律，可能是 202304</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<TrailblazeCalendarSummary> GetTrailblazeCalendarSummaryAsync(GameRecordRole role, string month = "", CancellationToken cancellationToken = default);


    /// <summary>
    /// 开拓月历收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202304</param>
    /// <param name="type">1 星琼 2 星轨票</param>
    /// <param name="page">从1开始</param>
    /// <param name="page_size">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回一页收入记录</returns>
    public abstract Task<TrailblazeCalendarDetail> GetTrailblazeCalendarDetailByPageAsync(GameRecordRole role, string month, int type, int page, int page_size = 100, CancellationToken cancellationToken = default);


    /// <summary>
    /// 开拓月历收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202304</param>
    /// <param name="type">1 星琼 2 星轨票</param>
    /// <param name="page_size">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回该月所有收入记录</returns>
    public abstract Task<TrailblazeCalendarDetail> GetTrailblazeCalendarDetailAsync(GameRecordRole role, string month, int type, int page_size = 100, CancellationToken cancellationToken = default);



    /// <summary>
    /// 星穹铁道实时便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<StarRailDailyNote> GetStarRailDailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default);


    /// <summary>
    /// 星穹铁道异相仲裁
    /// </summary>
    /// <param name="role"></param>
    /// <param name="scheduleType">1 当期，3 最近三期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ChallengePeakData> GetStarRailChallengePeakDataAsync(GameRecordRole role, int scheduleType, CancellationToken cancellationToken = default);



    #endregion




    #region ZZZ


    /// <summary>
    /// 式舆防卫战
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ShiyuDefenseWrapper> GetShiyuDefenseInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default);


    /// <summary>
    /// 危局强袭战
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<DeadlyAssaultInfo> GetDeadlyAssaultInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default);


    /// <summary>
    /// 获取绝区零账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<List<GameRecordRole>> GetZZZGameRolesAsync(string cookie, CancellationToken cancellationToken = default);


    /// <summary>
    /// 绝区零抽卡记录
    /// </summary>
    /// <param name="role"></param>
    /// <param name="gachaType">ZZZGachaType</param>
    /// <param name="endId">首次请求不传</param>
    /// <param name="language"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ZZZGachaRecordData> GetZZZGachaRecordAsync(GameRecordRole role, int gachaType, long? endId = null, string? language = null, CancellationToken cancellationToken = default);


    /// <summary>
    /// 通过 stoken 生成抽卡记录 authkey（Auth Key B，auth_appid=webview_gacha）。
    /// 国服需 Cookie 含有效 stoken+mid；国际服当前社区无稳定支持，子类可抛 <see cref="NotSupportedException"/>。
    /// </summary>
    /// <param name="role">游戏角色（Uid / GameBiz / Region / Cookie）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>含 authkey、authkey_ver、sign_type 的结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="role"/> 为空。</exception>
    /// <exception cref="ArgumentException">Cookie 缺少 stoken 或 mid。</exception>
    /// <exception cref="NotSupportedException">当前平台不支持 stoken 换 authkey。</exception>
    /// <exception cref="miHoYoApiException">协议业务失败。</exception>
    public abstract Task<GameAuthKey> GenAuthKeyAsync(GameRecordRole role, CancellationToken cancellationToken = default);


    /// <summary>
    /// 绳网月报总结
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202409</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<InterKnotReportSummary> GetInterKnotReportSummaryAsync(GameRecordRole role, string month = "", CancellationToken cancellationToken = default);


    /// <summary>
    /// 绳网月报收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202409</param>
    /// <param name="type"></param>
    /// <param name="page">从1开始</param>
    /// <param name="page_size">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回一页收入记录</returns>
    public abstract Task<InterKnotReportDetail> GetInterKnotReportDetailByPageAsync(GameRecordRole role, string month, string type, int page, int page_size = 100, CancellationToken cancellationToken = default);


    /// <summary>
    /// 绳网月报收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202409</param>
    /// <param name="type"></param>
    /// <param name="page_size">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回该月所有收入记录</returns>
    public abstract Task<InterKnotReportDetail> GetInterKnotReportDetailAsync(GameRecordRole role, string month, string type, int page_size = 100, CancellationToken cancellationToken = default);




    /// <summary>
    /// 通过养成指南 <c>icon_info</c> + <c>item_list</c> 获取绝区零抽卡物品元数据（代理人/音擎/邦布：名称、图标、稀有度等）。
    /// 需有效 Cookie 的战绩角色；会先 badge 登录换 <c>e_nap_token</c>。目录数据与 UID 无关。
    /// </summary>
    /// <remarks>
    /// 对齐 genshin.py：养成接口拒绝携带 <c>x-rpc-device_id</c>（会直接 -100），且必须先 POST badge/login 取得 <c>e_nap_token</c>。
    /// </remarks>
    /// <param name="role">已登录的绝区零角色（Uid / Region / Cookie）。</param>
    /// <param name="language">期望语言（如 zh-cn、en-us）；国际服生效，国服通常仅返回中文名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合并后的 <see cref="ZZZGachaWiki"/>（含 Language 与 List）。</returns>
    public async Task<ZZZGachaWiki> GetZZZGachaWikiAsync(GameRecordRole role, string? language = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        string lang = LanguageUtil.FilterLanguage(language);
        // badge 登录拿 e_nap_token；-100 时再刷一次 token 后重试（与 genshin.py 一致）
        string cookie = await LoginZZZCultivateBadgeAsync(role, lang, cancellationToken);
        try
        {
            return await FetchZZZGachaWikiWithCookieAsync(role, cookie, lang, cancellationToken);
        }
        catch (miHoYoApiException ex) when (ex.IsLoginExpired)
        {
            cookie = await LoginZZZCultivateBadgeAsync(role, lang, cancellationToken);
            return await FetchZZZGachaWikiWithCookieAsync(role, cookie, lang, cancellationToken);
        }
    }


    private async Task<ZZZGachaWiki> FetchZZZGachaWikiWithCookieAsync(GameRecordRole role, string cookie, string lang, CancellationToken cancellationToken)
    {
        var items = await GetZZZUpgradeGuideItemListAsync(role, cookie, cancellationToken: cancellationToken);
        var icons = await GetZZZUpgradeGuideIconInfoAsync(role, cookie, cancellationToken);
        return MergeZZZGachaWiki(items, icons, lang);
    }


    /// <summary>
    /// 养成指南 badge 登录：POST <c>common/badge/v1/login/account</c>，把响应 <c>Set-Cookie</c>（含 <c>e_nap_token</c>）合并进 Cookie。
    /// </summary>
    /// <param name="role">已登录角色。</param>
    /// <param name="language">语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合并 e_nap_token 后的 Cookie 串。</returns>
    public abstract Task<string> LoginZZZCultivateBadgeAsync(GameRecordRole role, string language, CancellationToken cancellationToken = default);


    /// <summary>
    /// 将响应 <c>Set-Cookie</c> 中的键值合并进基础 Cookie 串（同名覆盖）。
    /// </summary>
    protected static string MergeCookieFromSetCookie(string? baseCookie, HttpResponseMessage response)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(baseCookie))
        {
            foreach (string part in baseCookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq > 0)
                {
                    map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
                }
            }
        }
        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            foreach (string sc in setCookies)
            {
                // 仅取第一段 name=value，忽略 Path/Expires 等属性
                string segment = sc.Split(';', 2)[0];
                int eq = segment.IndexOf('=');
                if (eq > 0)
                {
                    map[segment[..eq].Trim()] = segment[(eq + 1)..].Trim();
                }
            }
        }
        return string.Join("; ", map.Select(kv => $"{kv.Key}={kv.Value}"));
    }


    /// <summary>
    /// 将养成指南 item_list 与 icon_info 合并为抽卡物品信息列表。
    /// </summary>
    /// <param name="items">item_list 响应。</param>
    /// <param name="icons">icon_info 响应。</param>
    /// <param name="language">写入 <see cref="ZZZGachaWiki.Language"/> 的语言代码。</param>
    /// <returns>合并后的 wiki。</returns>
    protected static ZZZGachaWiki MergeZZZGachaWiki(UpgradeGuideItemList items, UpgradeGuidIconInfo icons, string language)
    {
        var list = new List<ZZZGachaInfo>();
        Dictionary<string, UpgradeGuidIconInfoItem>? avatarIcons = icons.AvatarIcon;
        Dictionary<string, UpgradeGuidIconInfoItem>? buddyIcons = icons.BuddyIcon;

        if (items.AvatarList is not null)
        {
            foreach (var item in items.AvatarList)
            {
                var info = new ZZZGachaInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    Rarity = MapZZZCultivateRarity(item.Rarity),
                    ElementType = item.ElementType,
                    Profession = item.AvatarProfession,
                    Icon = "",
                };
                if (avatarIcons is not null && avatarIcons.TryGetValue(item.Id.ToString(), out UpgradeGuidIconInfoItem? icon))
                {
                    info.Icon = icon.SquareAvatar ?? "";
                }
                list.Add(info);
            }
        }

        if (items.Weapon is not null)
        {
            foreach (var item in items.Weapon)
            {
                list.Add(new ZZZGachaInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    Icon = item.Icon ?? "",
                    Rarity = MapZZZCultivateRarity(item.Rarity),
                    Profession = item.Profession,
                });
            }
        }

        if (items.BuddyList is not null)
        {
            foreach (var item in items.BuddyList)
            {
                var info = new ZZZGachaInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    Rarity = MapZZZCultivateRarity(item.Rarity),
                    Icon = "",
                };
                if (buddyIcons is not null && buddyIcons.TryGetValue(item.Id.ToString(), out UpgradeGuidIconInfoItem? icon))
                {
                    info.Icon = icon.SquareAvatar ?? "";
                }
                list.Add(info);
            }
        }

        return new ZZZGachaWiki
        {
            Game = GameBiz.nap,
            Language = language,
            List = list,
        };
    }


    /// <summary>
    /// 养成指南稀有度字符串映射为抽卡记录内部 Rank（S→4, A→3, B→2）。
    /// </summary>
    private static int MapZZZCultivateRarity(string? rarity) => rarity switch
    {
        "S" or "s" => 4,
        "A" or "a" => 3,
        "B" or "b" => 2,
        _ => 0,
    };


    /// <summary>
    /// 养成指南 item_list：代理人/音擎/邦布等物品列表（含名称、稀有度；音擎含 icon）。
    /// </summary>
    /// <param name="role">已登录的绝区零角色（提供 Uid/Region）。</param>
    /// <param name="cookie">含 <c>e_nap_token</c> 的完整 Cookie（由 <see cref="LoginZZZCultivateBadgeAsync"/> 合并）。</param>
    /// <param name="avatar_id">请求参数 avatar_id，默认 1011（安比）；接口返回全量列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>item_list 数据。</returns>
    public abstract Task<UpgradeGuideItemList> GetZZZUpgradeGuideItemListAsync(GameRecordRole role, string cookie, int avatar_id = 1011, CancellationToken cancellationToken = default);



    /// <summary>
    /// 养成指南 icon_info：代理人/邦布方形头像等图标 URL。
    /// </summary>
    /// <param name="role">已登录的绝区零角色（提供 Uid/Region）。</param>
    /// <param name="cookie">含 <c>e_nap_token</c> 的完整 Cookie。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>icon_info 数据。</returns>
    public abstract Task<UpgradeGuidIconInfo> GetZZZUpgradeGuideIconInfoAsync(GameRecordRole role, string cookie, CancellationToken cancellationToken = default);



    /// <summary>
    /// 绝区零实时便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ZZZDailyNote> GetZZZDailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default);



    /// <summary>
    /// 绝区零临界推演
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ThresholdSimulationAbstractInfo> GetZZZThresholdSimulationAbstractInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default);



    /// <summary>
    /// 绝区零临界推演
    /// </summary>
    /// <param name="role"></param>
    /// <param name="void_front_id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<ThresholdSimulationDetailInfo> GetZZZThresholdSimulationDetailInfoAsync(GameRecordRole role, int void_front_id, CancellationToken cancellationToken = default);




    #endregion




    #region SignIn


    /// <summary>
    /// 签到接口使用的语言（CN 固定 zh-cn；OS 由 HoyolabClient 的语言头决定）
    /// </summary>
    protected virtual string SignInLanguage => "zh-cn";


    /// <summary>
    /// 获取当前角色对应的签到活动配置，不支持的游戏抛出异常。
    /// </summary>
    /// <param name="role">游戏角色，用于解析 <see cref="GameRecordRole.GameBiz"/>。</param>
    /// <returns>该游戏 + 区服对应的签到活动配置。</returns>
    /// <exception cref="miHoYoApiException">当前游戏不支持签到时抛出。</exception>
    protected static SignInActivityConfig GetSignInConfigOrThrow(GameRecordRole role)
    {
        GameBiz biz = role.GameBiz;
        bool isOversea = biz.Server is "global";
        SignInActivityConfig? config = SignInActivityConfig.FromGame(biz.Game, isOversea);
        if (config is null)
        {
            throw new miHoYoApiException(-1, $"Sign-in is not supported for game biz: {role.GameBiz}");
        }
        return config;
    }


    /// <summary>
    /// 为签到请求添加平台相关请求头（signgame / Origin / 设备信息等）。
    /// </summary>
    /// <param name="request">待发送的 HTTP 请求。</param>
    /// <param name="config">签到活动配置，提供 signgame / origin 等字段。</param>
    /// <param name="signData">是否为数据接口（home 为 false；info/sign/resign 为 true）。</param>
    protected abstract void AddSignInPlatformHeaders(HttpRequestMessage request, SignInActivityConfig config, bool signData);


    /// <summary>
    /// 本月签到奖励列表（home 接口）。
    /// </summary>
    /// <param name="role">游戏角色，提供 cookie / region / uid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当月每日奖励列表。</returns>
    public async Task<SignInReward> GetSignInRewardAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        SignInActivityConfig config = GetSignInConfigOrThrow(role);
        var request = new HttpRequestMessage(HttpMethod.Get, config.HomeUrl(SignInLanguage));
        request.Headers.Add(Cookie, role.Cookie);
        AddSignInPlatformHeaders(request, config, signData: false);
        return await CommonSendAsync<SignInReward>(request, cancellationToken);
    }


    /// <summary>
    /// 当前签到状态（已签天数、今日是否已签等，info 接口）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签到状态信息。</returns>
    public async Task<SignInRewardInfo> GetSignInInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        SignInActivityConfig config = GetSignInConfigOrThrow(role);
        var request = new HttpRequestMessage(HttpMethod.Get, config.InfoUrl(SignInLanguage, role.Region, role.Uid));
        request.Headers.Add(Cookie, role.Cookie);
        AddSignInPlatformHeaders(request, config, signData: true);
        return await CommonSendAsync<SignInRewardInfo>(request, cancellationToken);
    }


    /// <summary>
    /// 补签信息（剩余补签次数、消耗货币等，resign_info 接口）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补签配额与货币信息。</returns>
    public async Task<SignInResignInfo> GetSignInResignInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        SignInActivityConfig config = GetSignInConfigOrThrow(role);
        var request = new HttpRequestMessage(HttpMethod.Get, config.ResignInfoUrl(SignInLanguage, role.Region, role.Uid));
        request.Headers.Add(Cookie, role.Cookie);
        AddSignInPlatformHeaders(request, config, signData: true);
        return await CommonSendAsync<SignInResignInfo>(request, cancellationToken);
    }


    /// <summary>
    /// 执行今日签到，成功返回 retcode 0；今日已签返回 -5003（由上层处理）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签到结果，含风控字段。</returns>
    public async Task<SignInResult> SignInAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        return await PostSignInAsync(role, resign: false, cancellationToken);
    }


    /// <summary>
    /// 执行补签。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补签结果，含风控字段。</returns>
    public async Task<SignInResult> ReSignInAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        return await PostSignInAsync(role, resign: true, cancellationToken);
    }


    /// <summary>
    /// 签到 / 补签的 POST 公共实现。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="resign">true 走补签接口，false 走签到接口。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>接口返回的签到结果。</returns>
    private async Task<SignInResult> PostSignInAsync(GameRecordRole role, bool resign, CancellationToken cancellationToken)
    {
        SignInActivityConfig config = GetSignInConfigOrThrow(role);
        var body = new SignInPostBody(config.ActId, role.Region, role.Uid.ToString());
        string json = JsonSerializer.Serialize(body, typeof(SignInPostBody), GameRecordJsonContext.Default);
        string url = resign ? config.ResignUrl() : config.SignUrl();
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, Application_Json),
        };
        request.Headers.Add(Cookie, role.Cookie);
        AddSignInPlatformHeaders(request, config, signData: true);
        return await CommonSendAsync<SignInResult>(request, cancellationToken);
    }


    #endregion




    // 寰宇蝗灾
    // https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/rogue_locust?server=prod_gf_cn&role_id={uid}&need_detail=true

    // 黄金与机械
    // https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/rogue_nous?server=prod_gf_cn&role_id={uid}&need_detail=true

    // 幻想真境剧诗
    // https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/role_combat?server=cn_gf01&role_id={uid}&active=1&need_detail=true


}
