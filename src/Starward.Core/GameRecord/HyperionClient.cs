using Starward.Core.GameRecord.BH3.DailyNote;
using Starward.Core.GameRecord.Genshin.DailyNote;
using Starward.Core.GameRecord.Genshin.ImaginariumTheater;
using Starward.Core.GameRecord.Genshin.SpiralAbyss;
using Starward.Core.GameRecord.Genshin.StygianOnslaught;
using Starward.Core.GameRecord.Genshin.TravelersDiary;
using Starward.Core.GameRecord.StarRail.ApocalypticShadow;
using Starward.Core.GameRecord.StarRail.ChallengePeak;
using Starward.Core.GameRecord.StarRail.DailyNote;
using Starward.Core.GameRecord.StarRail.ForgottenHall;
using Starward.Core.GameRecord.StarRail.PureFiction;
using Starward.Core.GameRecord.StarRail.SimulatedUniverse;
using Starward.Core.GameRecord.SignIn;
using Starward.Core.GameRecord.StarRail.TrailblazeCalendar;
using Starward.Core.GameRecord.ZZZ.DailyNote;
using Starward.Core.GameRecord.ZZZ.DeadlyAssault;
using Starward.Core.GameRecord.ZZZ.GachaRecord;
using Starward.Core.GameRecord.ZZZ.InterKnotReport;
using Starward.Core.GameRecord.ZZZ.ShiyuDefense;
using Starward.Core.GameRecord.ZZZ.ThresholdSimulation;
using Starward.Core.GameRecord.ZZZ.UpgradeGuide;
using System.Text;
using System.Text.Json;

namespace Starward.Core.GameRecord;


public class HyperionClient : GameRecordClient
{


    public override string UAContent => $"Mozilla/5.0 (Linux; Android 13; Pixel 5 Build/TQ3A.230901.001; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBS/{AppVersion}";

    public override string AppVersion => "2.90.1";

    protected override string ApiSalt => "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";

    protected override string ApiSalt2 => "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";



    public HyperionClient(HttpClient? httpClient = null) : base(httpClient)
    {

    }



    // https://webstatic.mihoyo.com/bbs/event/signin-ys/index.html?bbs_auth_required=true&act_id=e202009291139501&utm_source=bbs&utm_medium=mys&utm_campaign=icon
    // https://webstatic.mihoyo.com/ys/event/e20200709ysjournal/index.html?bbs_presentation_style=fullscreen&bbs_auth_required=true&utm_source=bbs&utm_medium=mys&utm_campaign=icon
    // https://webstatic.mihoyo.com/app/community-game-records/?game_id=2&utm_source=bbs&utm_medium=mys&utm_campaign=box
    // https://webstatic.mihoyo.com/bbs/event/signin/hkrpg/index.html?bbs_auth_required=true&act_id=e202304121516551&bbs_auth_required=true&bbs_presentation_style=fullscreen&utm_source=bbs&utm_medium=mys&utm_campaign=icon
    // https://webstatic.mihoyo.com/app/community-game-records/rpg/index.html?mhy_presentation_style=fullscreen&game_id=6&utm_source=bbs&utm_medium=mys&utm_campaign=icon
    // https://webstatic.mihoyo.com/sr/event/rpg-srledger/index.html?mhy_game_role_required=hkrpg_cn&mhy_presentation_style=fullscreen&utm_source=bbs&utm_medium=mys&utm_campaign=icon




    /// <summary>
    /// 米游社账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public override async Task<GameRecordUser> GetGameRecordUserAsync(string cookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new ArgumentNullException(nameof(cookie));
        }
        var request = new HttpRequestMessage(HttpMethod.Get, "https://bbs-api.miyoushe.com/user/wapi/getUserFullInfo");
        request.Headers.Add(Cookie, cookie);
        request.Headers.Add(Referer, "https://www.miyoushe.com/");
        request.Headers.Add(DS, CreateSecret());
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_client_type, "5");
        var data = await CommonSendAsync<GameRecordUserWrapper>(request, cancellationToken);
        data.User.Cookie = cookie;
        return data.User;
    }





    /// <summary>
    /// 所有游戏账号
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<List<GameRecordRole>> GetAllGameRolesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        Lock @lock = new();
        var list = new List<GameRecordRole>();
        await Parallel.ForEachAsync([GameBiz.bh3_cn, GameBiz.hk4e_cn, GameBiz.hkrpg_cn, GameBiz.nap_cn], cancellationToken, async (GameBiz gameBiz, CancellationToken token) =>
        {
            var roles = await GetGameRolesAsync(cookie, gameBiz, token);
            if (roles.Count > 0)
            {
                lock (@lock)
                {
                    list.AddRange(roles);
                }
            }
        });
        return list;
    }



    /// <summary>
    /// 获取游戏账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="gameBiz"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<List<GameRecordRole>> GetGameRolesAsync(string cookie, GameBiz gameBiz, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new ArgumentNullException(nameof(cookie));
        }
        string url = $"https://passport-api.mihoyo.com/binding/api/getUserGameRolesByCookieToken?game_biz={gameBiz}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, cookie);
        //request.Headers.Add(DS, CreateSecret2(url));
        //request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        //request.Headers.Add(x_rpc_app_version, AppVersion);
        //request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        var data = await CommonSendAsync<GameRecordRoleWrapper>(request, cancellationToken);
        if (data.List is not null)
        {
            foreach (var item in data.List)
            {
                item.Cookie = cookie;
                try
                {
                    item.HeadIcon = await GetGameRoleHeadIconAsync(item, cancellationToken);
                }
                catch (miHoYoApiException) { }
            }
        }
        return data.List ?? new List<GameRecordRole>();
    }



    /// <summary>
    /// 获取游戏账号头像
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected override async Task<string> GetGameRoleHeadIconAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        string url = role.GameBiz switch
        {
            GameBiz.bh3_cn => $"https://api-takumi-record.mihoyo.com/game_record/app/honkai3rd/api/index?server={role.Region}&role_id={role.Uid}",
            GameBiz.hk4e_cn => $"https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/index?avatar_list_type=1&server={role.Region}&role_id={role.Uid}",
            GameBiz.hkrpg_cn => $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/index?server={role.Region}&role_id={role.Uid}",
            GameBiz.nap_cn => $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/index?server={role.Region}&role_id={role.Uid}",
            _ => throw new ArgumentOutOfRangeException($"Unsupport GameBiz: {role.GameBiz}"),
        };
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        var data = await CommonSendAsync<GameRecordIndex>(request, cancellationToken);
        return data.HeadIcon;
    }




    /// <summary>
    /// 获取设备指纹信息
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<string> GetDeviceFpAsync(CancellationToken cancellationToken = default)
    {
        const string url = "https://public-data-api.mihoyo.com/device-fp/api/getFp";
        string productName = GenerateProductName();
        string postContent = $$"""
            {
                "device_id": "{{GenerateSeedId()}}",
                "seed_id": "{{Guid.NewGuid():D}}",
                "seed_time": "{{DateTimeOffset.Now.ToUnixTimeMilliseconds()}}",
                "platform": "2",
                "device_fp": "{{DeviceFp}}",
                "app_name": "bbs_cn",
                "ext_fields": "{\"proxyStatus\":0,\"isRoot\":0,\"romCapacity\":\"512\",\"deviceName\":\"Pixel5\",\"productName\":\"{{productName}}\",\"romRemain\":\"512\",\"hostname\":\"db1ba5f7c000000\",\"screenSize\":\"1080x2400\",\"isTablet\":0,\"aaid\":\"\",\"model\":\"Pixel5\",\"brand\":\"google\",\"hardware\":\"windows_x86_64\",\"deviceType\":\"redfin\",\"devId\":\"REL\",\"serialNumber\":\"unknown\",\"sdCapacity\":125943,\"buildTime\":\"1704316741000\",\"buildUser\":\"cloudtest\",\"simState\":0,\"ramRemain\":\"124603\",\"appUpdateTimeDiff\":1716369357492,\"deviceInfo\":\"google\\\/{{productName}}\\\/redfin:13\\\/TQ3A.230901.001\\\/2311.40000.5.0:user\\\/release-keys\",\"vaid\":\"\",\"buildType\":\"user\",\"sdkVersion\":\"33\",\"ui_mode\":\"UI_MODE_TYPE_NORMAL\",\"isMockLocation\":0,\"cpuType\":\"arm64-v8a\",\"isAirMode\":0,\"ringMode\":2,\"chargeStatus\":3,\"manufacturer\":\"Google\",\"emulatorStatus\":0,\"appMemory\":\"512\",\"osVersion\":\"13\",\"vendor\":\"unknown\",\"accelerometer\":\"\",\"sdRemain\":123276,\"buildTags\":\"release-keys\",\"packageName\":\"com.mihoyo.hyperion\",\"networkType\":\"WiFi\",\"oaid\":\"\",\"debugStatus\":1,\"ramCapacity\":\"125943\",\"magnetometer\":\"\",\"display\":\"TQ3A.230901.001\",\"appInstallTimeDiff\":1706444666737,\"packageVersion\":\"2.20.2\",\"gyroscope\":\"\",\"batteryStatus\":85,\"hasKeyboard\":10,\"board\":\"windows\"}",
                "bbs_device_id": "{{DeviceId}}"
            }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(postContent),
        };
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        var data = await CommonSendAsync<DeviceFpResult>(request, cancellationToken);
        if (data.Code != 200)
        {
            throw new miHoYoApiException(data.Code, data.Message);
        }
        DeviceFp = data.DeviceFp;
        return data.DeviceFp;
    }




    private static string GenerateSeedId()
    {
        var bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }



    private static string GenerateProductName()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        char[] name = Random.Shared.GetItems<char>(chars, 6);
        return new string(name);
    }




    /// <summary>
    /// 米游社签到请求头：DS(Gen1/LK2) + x-rpc-signgame + 设备指纹。
    /// </summary>
    /// <param name="request">待发送的 HTTP 请求。</param>
    /// <param name="config">签到活动配置，提供 signgame / origin。</param>
    /// <param name="signData">true 时附加 DS 签名（info / sign / resign 需要）。</param>
    protected override void AddSignInPlatformHeaders(HttpRequestMessage request, SignInActivityConfig config, bool signData)
    {
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add("x-rpc-signgame", config.SignGame);
        if (!string.IsNullOrEmpty(config.Origin))
        {
            // 绝区零 CN 的 act-nap-api 主机需要 Origin 头，否则会被风控拒绝
            request.Headers.Add("Origin", config.Origin);
        }
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        if (signData)
        {
            request.Headers.Add(DS, CreateSecret());
        }
    }





    #region BH3


    /// <summary>
    /// 崩坏3实时便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<BH3DailyNote> GetBH3DailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        string url = $"https://act-api-takumi.mihoyo.com/game_record/appv2/honkai3rd/api/note?server={role.Region}&role_id={role.Uid}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<BH3DailyNote>(request, cancellationToken);
    }



    #endregion





    #region Genshin


    /// <summary>
    /// 获取原神账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<List<GameRecordRole>> GetGenshinGameRolesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new ArgumentNullException(nameof(cookie));
        }
        var url = "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        var data = await CommonSendAsync<GameRecordRoleWrapper>(request, cancellationToken);
        data.List?.ForEach(x => x.Cookie = cookie);
        return data.List ?? new List<GameRecordRole>();
    }


    /// <summary>
    /// 深境螺旋
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<SpiralAbyssInfo> GetSpiralAbyssInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/spiralAbyss?schedule_type={schedule}&server={role.Region}&role_id={role.Uid}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var data = await CommonSendAsync<SpiralAbyssInfo>(request, cancellationToken);
        data.Uid = role.Uid;
        return data;
    }


    /// <summary>
    /// 旅行札记总览
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">0 当前月</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>

    public override async Task<TravelersDiarySummary> GetTravelsDiarySummaryAsync(GameRecordRole role, int month = 0, CancellationToken cancellationToken = default)
    {
        var url = $"https://hk4e-api.mihoyo.com/event/ys_ledger/monthInfo?month={month}&bind_uid={role.Uid}&bind_region={role.Region}&bbs_presentation_style=fullscreen&bbs_auth_required=true&utm_source=bbs&utm_medium=mys&utm_campaign=icon";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        return await CommonSendAsync<TravelersDiarySummary>(request, cancellationToken);
    }


    /// <summary>
    /// 旅行札记收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month"></param>
    /// <param name="type">1原石，2摩拉</param>
    /// <param name="page">从1开始</param>
    /// <param name="limit">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回一页收入记录</returns>
    public override async Task<TravelersDiaryDetail> GetTravelsDiaryDetailByPageAsync(GameRecordRole role, int month, int type, int page, int limit = 100, CancellationToken cancellationToken = default)
    {
        var url = $"https://hk4e-api.mihoyo.com/event/ys_ledger/monthDetail?page={page}&month={month}&limit={limit}&type={type}&bind_uid={role.Uid}&bind_region={role.Region}&bbs_presentation_style=fullscreen&bbs_auth_required=true&utm_source=bbs&utm_medium=mys&utm_campaign=icon";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var data = await CommonSendAsync<TravelersDiaryDetail>(request, cancellationToken);
        foreach (var item in data.List)
        {
            item.Type = type;
        }
        return data;
    }


    /// <summary>
    /// 旅行札记收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month"></param>
    /// <param name="type">1原石，2摩拉</param>
    /// <param name="limit">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回该月所有收入记录</returns>
    public override async Task<TravelersDiaryDetail> GetTravelsDiaryDetailAsync(GameRecordRole role, int month, int type, int limit = 100, CancellationToken cancellationToken = default)
    {
        var data = await GetTravelsDiaryDetailByPageAsync(role, month, type, 1, limit, cancellationToken);
        if (data.List.Count < limit)
        {
            return data;
        }
        for (int i = 2; ; i++)
        {
            var addData = await GetTravelsDiaryDetailByPageAsync(role, month, type, i, limit, cancellationToken);
            data.List.AddRange(addData.List);
            if (addData.List.Count < limit)
            {
                break;
            }
        }
        return data;
    }



    /// <summary>
    /// 幻想真境剧诗
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<List<ImaginariumTheaterInfo>> GetImaginariumTheaterInfosAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/role_combat?server={role.Region}&role_id={role.Uid}&active=1&need_detail=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var warpper = await CommonSendAsync<ImaginariumTheaterWarpper>(request, cancellationToken);
        foreach (var item in warpper.Data)
        {
            item.Uid = role.Uid;
            item.ScheduleId = item.Schedule.ScheduleId;
            item.StartTime = item.Schedule.StartDateTime;
            item.EndTime = item.Schedule.EndDateTime;
            item.DifficultyId = item.Stat.DifficultyId;
            item.MaxRoundId = item.Stat.MaxRoundId + item.Stat.TarotFinishedCnt;
            item.Heraldry = item.Stat.Heraldry;
            item.MedalNum = item.Stat.GetMedalRoundList.Count(x => x == 1);
        }
        return warpper.Data;
    }


    /// <summary>
    /// 幽境危战
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<List<StygianOnslaughtInfo>> GetStygianOnslaughtInfosAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/hard_challenge?server={role.Region}&role_id={role.Uid}&need_detail=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var warpper = await CommonSendAsync<StygianOnslaughtWrapper>(request, cancellationToken);
        foreach (var item in warpper.Data)
        {
            item.Uid = role.Uid;
            item.ScheduleId = item.Schedule.ScheduleId;
            item.StartDateTime = item.Schedule.StartDateTime;
            item.EndDateTime = item.Schedule.EndDateTime;
            item.Difficulty = item.SinglePlayer.Best?.Difficulty ?? 0;
            item.Second = item.SinglePlayer.Best?.Seconds ?? 0;
        }
        return warpper.Data;
    }



    /// <summary>
    /// 原神每日便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<GenshinDailyNote> GetGenshinDailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        string url = $"https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/dailyNote?server={role.Region}&role_id={role.Uid}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<GenshinDailyNote>(request, cancellationToken);
    }



    #endregion




    #region StarRail


    /// <summary>
    /// 获取星穹铁道账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException">输入的 <c>cookie</c> 为空</exception>
    public override async Task<List<GameRecordRole>> GetStarRailGameRolesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new ArgumentNullException(nameof(cookie));
        }
        const string url = "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hkrpg_cn";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        var data = await CommonSendAsync<GameRecordRoleWrapper>(request, cancellationToken);
        data.List?.ForEach(x => x.Cookie = cookie);
        return data.List ?? new List<GameRecordRole>();
    }


    /// <summary>
    /// 忘却之庭
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ForgottenHallInfo> GetForgottenHallInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/challenge?schedule_type={schedule}&server={role.Region}&role_id={role.Uid}&need_all=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var data = await CommonSendAsync<ForgottenHallInfo>(request, cancellationToken);
        data.Uid = role.Uid;
        return data;
    }


    /// <summary>
    /// 虚构叙事
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<PureFictionInfo> GetPureFictionInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/challenge_story?schedule_type={schedule}&server={role.Region}&role_id={role.Uid}&isPrev=1&need_all=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var data = await CommonSendAsync<PureFictionInfo>(request, cancellationToken);
        data.Uid = role.Uid;
        if (data.Metas?.Count > 0)
        {
            if (schedule == 1)
            {
                data.ScheduleId = data.Metas[0].ScheduleId;
                data.BeginTime = data.Metas[0].BeginTime;
                data.EndTime = data.Metas[0].EndTime;
            }
            if (schedule == 2 && data.Metas.Count > 1)
            {
                data.ScheduleId = data.Metas[1].ScheduleId;
                data.BeginTime = data.Metas[1].BeginTime;
                data.EndTime = data.Metas[1].EndTime;
            }
        }
        return data;
    }


    /// <summary>
    /// 末日幻影
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ApocalypticShadowInfo> GetApocalypticShadowInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/challenge_boss?schedule_type={schedule}&server={role.Region}&role_id={role.Uid}&isPrev=1&need_all=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var data = await CommonSendAsync<ApocalypticShadowInfo>(request, cancellationToken);
        data.Uid = role.Uid;
        if (data.Metas?.Count > 0)
        {
            if (schedule == 1)
            {
                data.ScheduleId = data.Metas[0].ScheduleId;
                data.BeginTime = data.Metas[0].BeginTime;
                data.EndTime = data.Metas[0].EndTime;
                data.UpperBossIcon = data.Metas[0].UpperBoss.Icon;
                data.LowerBossIcon = data.Metas[0].LowerBoss.Icon;
                data.TierceBossIcon = data.Metas[0].TierceBoss?.Icon;
            }
            if (schedule == 2 && data.Metas.Count > 1)
            {
                data.ScheduleId = data.Metas[1].ScheduleId;
                data.BeginTime = data.Metas[1].BeginTime;
                data.EndTime = data.Metas[1].EndTime;
                data.UpperBossIcon = data.Metas[1].UpperBoss.Icon;
                data.LowerBossIcon = data.Metas[1].LowerBoss.Icon;
                data.TierceBossIcon = data.Metas[1].TierceBoss?.Icon;
            }
        }
        return data;
    }


    /// <summary>
    /// 模拟宇宙
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<SimulatedUniverseInfo> GetSimulatedUniverseInfoAsync(GameRecordRole role, bool detail = false, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/rogue?role_id={role.Uid}&server={role.Region}&schedule_type=3&need_detail={detail.ToString().ToLower()}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var data = await CommonSendAsync<SimulatedUniverseInfo>(request, cancellationToken);
        return data;
    }


    /// <summary>
    /// 开拓月历总结
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">还不清楚规律，可能是 202304</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<TrailblazeCalendarSummary> GetTrailblazeCalendarSummaryAsync(GameRecordRole role, string month = "", CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi.mihoyo.com/event/srledger/month_info?uid={role.Uid}&region={role.Region}&month={month}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        return await CommonSendAsync<TrailblazeCalendarSummary>(request, cancellationToken);
    }


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
    public override async Task<TrailblazeCalendarDetail> GetTrailblazeCalendarDetailByPageAsync(GameRecordRole role, string month, int type, int page, int page_size = 100, CancellationToken cancellationToken = default)
    {
        // 
        var url = $"https://api-takumi.mihoyo.com/event/srledger/month_detail?uid={role.Uid}&region={role.Region}&month={month}&type={type}&current_page={page}&page_size={page_size}&total=0";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        var data = await CommonSendAsync<TrailblazeCalendarDetail>(request, cancellationToken);
        foreach (var item in data.List)
        {
            item.Type = type;
        }
        return data;
    }


    /// <summary>
    /// 开拓月历收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202304</param>
    /// <param name="type">1 星琼 2 星轨票</param>
    /// <param name="page_size">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回该月所有收入记录</returns>
    public override async Task<TrailblazeCalendarDetail> GetTrailblazeCalendarDetailAsync(GameRecordRole role, string month, int type, int page_size = 100, CancellationToken cancellationToken = default)
    {
        page_size = Math.Clamp(page_size, 20, 100);
        var data = await GetTrailblazeCalendarDetailByPageAsync(role, month, type, 1, page_size, cancellationToken);
        if (data.List.Count < page_size)
        {
            return data;
        }
        for (int i = 2; ; i++)
        {
            var addData = await GetTrailblazeCalendarDetailByPageAsync(role, month, type, i, page_size, cancellationToken);
            data.List.AddRange(addData.List);
            if (addData.List.Count < page_size)
            {
                break;
            }
        }
        return data;
    }



    /// <summary>
    /// 星穹铁道实时便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<StarRailDailyNote> GetStarRailDailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        string url = $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/note?server={role.Region}&role_id={role.Uid}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<StarRailDailyNote>(request, cancellationToken);
    }


    /// <summary>
    /// 星穹铁道异相仲裁
    /// </summary>
    /// <param name="role"></param>
    /// <param name="scheduleType">1 当期，3 最近三期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ChallengePeakData> GetStarRailChallengePeakDataAsync(GameRecordRole role, int scheduleType, CancellationToken cancellationToken = default)
    {
        string url = $"https://api-takumi-record.mihoyo.com/game_record/app/hkrpg/api/challenge_peak?server={role.Region}&role_id={role.Uid}&schedule_type={scheduleType}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<ChallengePeakData>(request, cancellationToken);
    }



    #endregion




    #region ZZZ



    /// <summary>
    /// 获取绝区零账号信息
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<List<GameRecordRole>> GetZZZGameRolesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new ArgumentNullException(nameof(cookie));
        }
        var url = "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=nap_cn";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        var data = await CommonSendAsync<GameRecordRoleWrapper>(request, cancellationToken);
        data.List?.ForEach(x => x.Cookie = cookie);
        return data.List ?? new List<GameRecordRole>();
    }


    /// <summary>
    /// 绝区零抽卡记录
    /// </summary>
    /// <param name="role"></param>
    /// <param name="gachaType"></param>
    /// <param name="endId">首次请求不传</param>
    /// <param name="language"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ZZZGachaRecordData> GetZZZGachaRecordAsync(GameRecordRole role, int gachaType, long? endId = null, string? language = null, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/gacha_record?uid={role.Uid}&region={role.Region}&gacha_type={gachaType}";
        long validEndId = endId.GetValueOrDefault();
        if (validEndId > 0)
        {
            url += $"&end_id={validEndId}";
        }
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<ZZZGachaRecordData>(request, cancellationToken);
    }


    /// <summary>
    /// 通过 stoken 生成原神/星铁等游戏的抽卡 authkey（Auth Key B）。
    /// 对齐 TeyvatGuide <c>takumiReq.bind.authKey</c> 与 UIGF 文档：POST binding/api/genAuthKey，DS 使用 LK2 Gen1。
    /// </summary>
    /// <param name="role">须含有效 stoken+mid 的 Cookie，以及 GameBiz / Uid / Region。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可用于 public-operation 抽卡接口的 authkey 结果。</returns>
    public override async Task<GameAuthKey> GenAuthKeyAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (string.IsNullOrWhiteSpace(role.Cookie))
        {
            throw new ArgumentException("Cookie is required.", nameof(role));
        }
        if (!TryBuildSTokenCookie(role.Cookie, out string stokenCookie))
        {
            throw new ArgumentException("Cookie must contain stoken and mid.", nameof(role));
        }
        if (string.IsNullOrWhiteSpace(role.GameBiz) || string.IsNullOrWhiteSpace(role.Region))
        {
            throw new ArgumentException("GameBiz and Region are required.", nameof(role));
        }

        // genAuthKey 固定要求 LK2 Gen1 DS（与战绩接口的 X4/X6 salt 不同）
        const string apiSaltLk2 = "d9200c846b10886e8c874fc33c8f308b";
        var body = new GenAuthKeyPostBody("webview_gacha", role.GameBiz, role.Uid, role.Region);
        string json = JsonSerializer.Serialize(body, typeof(GenAuthKeyPostBody), GameRecordJsonContext.Default);
        const string url = "https://api-takumi.mihoyo.com/binding/api/genAuthKey";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, Application_Json),
        };
        // 仅带 stoken+mid，与社区实现一致，避免无关 cookie 键干扰
        request.Headers.Add(Cookie, stokenCookie);
        request.Headers.Add(Referer, "https://app.mihoyo.com");
        request.Headers.Add(DS, CreateSecret(apiSaltLk2));
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<GameAuthKey>(request, cancellationToken);
    }


    /// <summary>
    /// 从完整 Cookie 串中提取 stoken 与 mid，拼成 genAuthKey 所需 Cookie。
    /// </summary>
    /// <param name="cookie">角色 Cookie 原文。</param>
    /// <param name="stokenCookie">输出 <c>stoken=...;mid=...</c>；失败时为空。</param>
    /// <returns>两者均非空时为 true。</returns>
    private static bool TryBuildSTokenCookie(string cookie, out string stokenCookie)
    {
        stokenCookie = "";
        string? stoken = null;
        string? mid = null;
        foreach (string part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            string key = part[..eq].Trim();
            string value = part[(eq + 1)..].Trim();
            if (key.Equals("stoken", StringComparison.OrdinalIgnoreCase))
            {
                stoken = value;
            }
            else if (key.Equals("mid", StringComparison.OrdinalIgnoreCase))
            {
                mid = value;
            }
        }
        if (string.IsNullOrWhiteSpace(stoken) || string.IsNullOrWhiteSpace(mid))
        {
            return false;
        }
        stokenCookie = $"stoken={stoken};mid={mid}";
        return true;
    }


    /// <summary>
    /// 式舆防卫战
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ShiyuDefenseWrapper> GetShiyuDefenseInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/hadal_info_v2?schedule_type={schedule}&server={role.Region}&role_id={role.Uid}&need_all=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        return await CommonSendAsync<ShiyuDefenseWrapper>(request, cancellationToken);
    }


    /// <summary>
    /// 危局强袭战
    /// </summary>
    /// <param name="role"></param>
    /// <param name="schedule">1当期，2上期</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<DeadlyAssaultInfo> GetDeadlyAssaultInfoAsync(GameRecordRole role, int schedule, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/hadal_mem_detail_v2?schedule_type={schedule}&region={role.Region}&uid={role.Uid}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(DS, CreateSecret2(url));
        request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
        return await CommonSendAsync<DeadlyAssaultInfo>(request, cancellationToken);
    }


    /// <summary>
    /// 绳网月报总结
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202409</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<InterKnotReportSummary> GetInterKnotReportSummaryAsync(GameRecordRole role, string month = "", CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi.mihoyo.com/event/nap_ledger/month_info?uid={role.Uid}&region={role.Region}&month={month}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<InterKnotReportSummary>(request, cancellationToken);
    }

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
    public override async Task<InterKnotReportDetail> GetInterKnotReportDetailByPageAsync(GameRecordRole role, string month, string type, int page, int page_size = 100, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi.mihoyo.com/event/nap_ledger/month_detail?uid={role.Uid}&region={role.Region}&month={month}&type={type}&current_page={page}&page_size={page_size}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<InterKnotReportDetail>(request, cancellationToken);
    }


    /// <summary>
    /// 绳网月报收入详情
    /// </summary>
    /// <param name="role"></param>
    /// <param name="month">202409</param>
    /// <param name="type"></param>
    /// <param name="page_size">最大100</param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回该月所有收入记录</returns>
    public override async Task<InterKnotReportDetail> GetInterKnotReportDetailAsync(GameRecordRole role, string month, string type, int page_size = 100, CancellationToken cancellationToken = default)
    {
        page_size = Math.Clamp(page_size, 20, 100);
        var data = await GetInterKnotReportDetailByPageAsync(role, month, type, 1, page_size, cancellationToken);
        if (data.List.Count < page_size)
        {
            return data;
        }
        for (int i = 2; ; i++)
        {
            var addData = await GetInterKnotReportDetailByPageAsync(role, month, type, i, page_size, cancellationToken);
            data.List.AddRange(addData.List);
            if (addData.List.Count < page_size)
            {
                break;
            }
        }
        return data;
    }



    /// <summary>
    /// 国服养成指南 badge 登录，换取 <c>e_nap_token</c>。
    /// 对齐 genshin.py：浏览器 UA、Gen2 DS、禁止 x-rpc-device_id（否则 -100 / 易触发 10035）。
    /// </summary>
    public override async Task<string> LoginZZZCultivateBadgeAsync(GameRecordRole role, string language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        string lang = LanguageUtil.FilterLanguage(language);
        var body = new ZZZCultivateBadgeLoginBody
        {
            GameBiz = "nap_cn",
            Lang = lang,
            Region = role.Region,
            Uid = role.Uid.ToString(),
        };
        const string url = "https://api-takumi.mihoyo.com/common/badge/v1/login/account";
        string json = JsonSerializer.Serialize(body, typeof(ZZZCultivateBadgeLoginBody), GameRecordJsonContext.Default);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, Application_Json),
        };
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/zzz/gt/character-builder-h/index.html");
        request.Headers.Add("Origin", "https://act.mihoyo.com");
        // genshin.py 对 CN act DS 头使用较低 app_version；过高 + BBS UA 易 10035
        request.Headers.Add(x_rpc_app_version, "2.11.1");
        request.Headers.Add(x_rpc_client_type, "5");
        if (!string.IsNullOrWhiteSpace(DeviceFp))
        {
            request.Headers.Add(x_rpc_device_fp, DeviceFp);
        }
        request.Headers.Add(DS, CreateSecret2(url, body));
        var (wrapper, response) = await SendCultivateBadgeLoginAsync(request, cancellationToken);
        if (wrapper is null)
        {
            throw new miHoYoApiException(-1, "Can not parse the response body.");
        }
        if (wrapper.Retcode != 0)
        {
            throw new miHoYoApiException(wrapper.Retcode, wrapper.Message);
        }
        return MergeCookieFromSetCookie(role.Cookie, response);
    }


    /// <summary>
    /// 养成指南 item_list（国服 api-takumi）。须已合并 <c>e_nap_token</c>；禁止 x-rpc-device_id。
    /// </summary>
    public override async Task<UpgradeGuideItemList> GetZZZUpgradeGuideItemListAsync(GameRecordRole role, string cookie, int avatar_id = 1011, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi.mihoyo.com/event/nap_cultivate_tool/user/item_list?uid={role.Uid}&region={role.Region}&avatar_id={avatar_id}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/zzz/gt/character-builder-h/index.html");
        request.Headers.Add("Origin", "https://act.mihoyo.com");
        request.Headers.Add(x_rpc_app_version, "2.11.1");
        request.Headers.Add(x_rpc_client_type, "5");
        if (!string.IsNullOrWhiteSpace(DeviceFp))
        {
            request.Headers.Add(x_rpc_device_fp, DeviceFp);
        }
        request.Headers.Add(DS, CreateSecret2(url));
        return await CommonSendCultivateAsync<UpgradeGuideItemList>(request, cancellationToken);
    }



    /// <summary>
    /// 养成指南 icon_info（国服 api-takumi）。
    /// </summary>
    public override async Task<UpgradeGuidIconInfo> GetZZZUpgradeGuideIconInfoAsync(GameRecordRole role, string cookie, CancellationToken cancellationToken = default)
    {
        var url = $"https://api-takumi.mihoyo.com/event/nap_cultivate_tool/user/icon_info?uid={role.Uid}&region={role.Region}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/zzz/gt/character-builder-h/index.html");
        request.Headers.Add("Origin", "https://act.mihoyo.com");
        request.Headers.Add(x_rpc_app_version, "2.11.1");
        request.Headers.Add(x_rpc_client_type, "5");
        if (!string.IsNullOrWhiteSpace(DeviceFp))
        {
            request.Headers.Add(x_rpc_device_fp, DeviceFp);
        }
        request.Headers.Add(DS, CreateSecret2(url));
        return await CommonSendCultivateAsync<UpgradeGuidIconInfo>(request, cancellationToken);
    }



    /// <summary>
    /// 绝区零实时便笺
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ZZZDailyNote> GetZZZDailyNoteAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        string url = $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/note?server={role.Region}&role_id={role.Uid}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<ZZZDailyNote>(request, cancellationToken);
    }



    /// <summary>
    /// 绝区零临界推演
    /// </summary>
    /// <param name="role"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ThresholdSimulationAbstractInfo> GetZZZThresholdSimulationAbstractInfoAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        string url = $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/void_front_battle_abstract_info?region={role.Region}&uid={role.Uid}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<ThresholdSimulationAbstractInfo>(request, cancellationToken);
    }



    /// <summary>
    /// 绝区零临界推演
    /// </summary>
    /// <param name="role"></param>
    /// <param name="void_front_id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ThresholdSimulationDetailInfo> GetZZZThresholdSimulationDetailInfoAsync(GameRecordRole role, int void_front_id, CancellationToken cancellationToken = default)
    {
        string url = $"https://api-takumi-record.mihoyo.com/event/game_record_zzz/api/zzz/void_front_battle_detail?region={role.Region}&uid={role.Uid}&void_front_id={void_front_id}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Cookie, role.Cookie);
        request.Headers.Add(Referer, "https://act.mihoyo.com/");
        request.Headers.Add(x_rpc_app_version, AppVersion);
        request.Headers.Add(x_rpc_client_type, "5");
        request.Headers.Add(x_rpc_device_id, DeviceId);
        request.Headers.Add(x_rpc_device_fp, DeviceFp);
        return await CommonSendAsync<ThresholdSimulationDetailInfo>(request, cancellationToken);
    }



    #endregion


}
