using Starward.Core.GameRecord.Genshin.SpiralAbyss;
using Starward.Core.GameRecord.Genshin.TravelersDiary;
using Starward.Core.GameRecord.StarRail.ForgottenHall;
using Starward.Core.GameRecord.StarRail.PureFiction;
using Starward.Core.GameRecord.StarRail.ApocalypticShadow;
using Starward.Core.GameRecord.StarRail.SimulatedUniverse;
using Starward.Core.GameRecord.StarRail.TrailblazeCalendar;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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





#if !DEBUG
using System.Net.Http.Json;
#endif
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
    protected const string Application_Json = "application/json";
    protected const string com_mihoyo_hyperion = "com.mihoyo.hyperion";
    protected const string com_mihoyo_hoyolab = "com.mihoyo.hoyolab";
    protected const string x_rpc_app_version = "x-rpc-app_version";
    protected const string x_rpc_device_id = "x-rpc-device_id";
    protected const string x_rpc_device_fp = "x-rpc-device_fp";
    protected const string x_rpc_client_type = "x-rpc-client_type";
    protected const string x_rpc_language = "X-Rpc-Language";

    /// <summary>
    /// 养成指南 H5 接口使用桌面浏览器 UA（勿用 BBS 手机 UA，易触发 10035 极验风控）。
    /// </summary>
    protected const string CultivateToolUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>
    /// 国际服 act 接口 Gen1 DS 盐（与 BBS/HoyolabClient.ApiSalt 不同）。
    /// </summary>
    protected const string CultivateToolDsSaltOverseas = "6s25p5ox5y14umn1p61aqyyvbvvl3lrt";

    #endregion



    public abstract string UAContent { get; }

    public abstract string AppVersion { get; }

    public string DeviceId { get; set; } = Guid.NewGuid().ToString("D");

    public string DeviceFp { get; set; } = "0000000000000";




    #region Dynamic Secret


    protected abstract string ApiSalt { get; }

    protected abstract string ApiSalt2 { get; }


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
    /// <returns>形如 <c>t,r,md5</c> 的 DS 头值。</returns>
    protected string CreateSecret()
    {
        return CreateSecret(ApiSalt);
    }


    /// <summary>
    /// 生成 Gen1 DS 签名（salt&amp;t&amp;r），使用指定 salt。
    /// 用于 genAuthKey 等需 LK2 salt 的接口（与 BBS 默认 X6 salt 不同）。
    /// </summary>
    /// <param name="salt">DS 盐值（如 LK2）。</param>
    /// <returns>形如 <c>t,r,md5</c> 的 DS 头值。</returns>
    protected string CreateSecret(string salt)
    {
        var t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GetRandomString(t);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"salt={salt}&t={t}&r={r}"));
        var check = Convert.ToHexString(bytes).ToLower();
        return $"{t},{r},{check}";
    }


    protected string CreateSecret2(string url)
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
        string result = $"{t},{r},{check}";
        return result;
    }


    protected string CreateSecret2<T>(string url, T postBody)
    {
        int t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = Random.Shared.Next(100000, 200000).ToString();
        string b = JsonSerializer.Serialize(postBody, typeof(T), GameRecordJsonContext.Default);
        string q = "";
        string[] urls = url.Split('?');
        if (urls.Length == 2)
        {
            string[] queryParams = urls[1].Split('&').OrderBy(x => x).ToArray();
            q = string.Join("&", queryParams);
        }
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"salt={ApiSalt2}&t={t}&r={r}&b={b}&q={q}"));
        var check = Convert.ToHexString(bytes).ToLower();
        string result = $"{t},{r},{check}";
        return result;
    }


    #endregion




    protected readonly HttpClient _httpClient;




    public GameRecordClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
    }




    #region Common Method




    protected virtual async Task<T> CommonSendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken = default) where T : class
    {
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        request.Headers.Add(Accept, Application_Json);
        request.Headers.Add(UserAgent, UAContent);
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
    /// 为签到请求添加平台相关请求头（CN 加 DS / signgame，OS 不加）。
    /// </summary>
    /// <param name="request">待发送的 HTTP 请求。</param>
    /// <param name="config">签到活动配置，提供 signgame / origin 等字段。</param>
    /// <param name="signData">是否需要 DS 数据签名（home 接口不需要）。</param>
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
        // home 接口不需要 DS 签名
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
