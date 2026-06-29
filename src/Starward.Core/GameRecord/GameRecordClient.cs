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


    protected string CreateSecret()
    {
        var t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GetRandomString(t);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"salt={ApiSalt}&t={t}&r={r}"));
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
    /// 获取绝区零绳网月报月度汇总（<c>nap_ledger/month_info</c>）。
    /// </summary>
    /// <param name="role">游戏角色，需提供 <see cref="GameRecordRole.Uid"/>、<see cref="GameRecordRole.Region"/> 与 Cookie。</param>
    /// <param name="month">查询月份，格式 <c>yyyyMM</c>（如 <c>202409</c>）；为空时返回当前月汇总。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>含资源总量、收入构成及 <c>optional_month</c> 的汇总对象。</returns>
    public abstract Task<InterKnotReportSummary> GetInterKnotReportSummaryAsync(GameRecordRole role, string month = "", CancellationToken cancellationToken = default);


    /// <summary>
    /// 获取绳网月报收入明细分页（<c>nap_ledger/month_detail</c>）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="month">查询月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型，取值见 <see cref="InterKnotReportDataType"/>。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="page_size">每页条数，最大 100。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>单页明细及 <see cref="InterKnotReportDetail.Total"/>。</returns>
    public abstract Task<InterKnotReportDetail> GetInterKnotReportDetailByPageAsync(GameRecordRole role, string month, string type, int page, int page_size = 100, CancellationToken cancellationToken = default);


    /// <summary>
    /// 获取绳网月报某月某类资源的全部收入明细，自动翻页合并。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="month">查询月份，格式 <c>yyyyMM</c>。</param>
    /// <param name="type">资源类型，取值见 <see cref="InterKnotReportDataType"/>。</param>
    /// <param name="page_size">每页条数，会被限制在 20–100 之间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合并后的完整明细列表。</returns>
    public abstract Task<InterKnotReportDetail> GetInterKnotReportDetailAsync(GameRecordRole role, string month, string type, int page_size = 100, CancellationToken cancellationToken = default);




    /// <summary>
    /// 通过养成指南获取抽卡物品信息，不可用，返回未登录错误
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Obsolete("不可用，返回未登录错误", true)]
    public Task<ZZZGachaWiki> GetZZZGachaWikiAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
        //var items = await GetZZZUpgradeGuideItemListAsync(role, cancellationToken: cancellationToken);
        //var icons = await GetZZZUpgradeGuideIconInfoAsync(role, cancellationToken);
        //var wiki = new ZZZGachaWiki
        //{
        //    Avatar = items.AvatarList.Select(x => new ZZZGachaInfo { Id = x.Id, Name = x.Name, Rarity = x.Rarity }).ToList(),
        //    Weapon = items.Weapon.Select(x => new ZZZGachaInfo { Id = x.Id, Name = x.Name, Rarity = x.Rarity, Icon = x.Icon }).ToList(),
        //    Buddy = items.BuddyList.Select(x => new ZZZGachaInfo { Id = x.Id, Name = x.Name, Rarity = x.Rarity }).ToList()
        //};
        //foreach (var item in wiki.Avatar)
        //{
        //    if (icons.AvatarIcon.TryGetValue(item.Id, out UpgradeGuidIconInfoItem? info))
        //    {
        //        item.Icon = info.SquareAvatar;
        //    }
        //}
        //foreach (var item in wiki.Buddy)
        //{
        //    if (icons.BuddyIcon.TryGetValue(item.Id, out UpgradeGuidIconInfoItem? value))
        //    {
        //        item.Icon = value.SquareAvatar;
        //    }
        //}
        //return wiki;
    }



    /// <summary>
    /// 养成指南，不可用，返回未登录错误
    /// </summary>
    /// <param name="role"></param>
    /// <param name="avatar_id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Obsolete("不可用，返回未登录错误", true)]
    public abstract Task<UpgradeGuideItemList> GetZZZUpgradeGuideItemListAsync(GameRecordRole role, int avatar_id = 1011, CancellationToken cancellationToken = default);



    /// <summary>
    /// 养成指南，不可用，返回未登录错误
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Obsolete("不可用，返回未登录错误", true)]
    public abstract Task<UpgradeGuidIconInfo> GetZZZUpgradeGuideIconInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default);



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
