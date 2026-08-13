using System.Net;
using System.Net.Http.Json;
using System.Net.Http;

namespace Starward.Core.Blackboard;

/// <summary>
/// 米游社百科 blackboard 公开接口（无 Cookie，无需鉴权头）。
/// 数据以国服百科为准；国际服无同构接口。
/// </summary>
public class BlackboardClient
{

    private const string ApiHost = "https://api-static.mihoyo.com";

    /// <summary>
    /// 百科前端使用的静态 CDN（词条 / 频道列表）。
    /// </summary>
    private const string WikiStaticHost = "https://act-api-takumi-static.mihoyo.com";

    private readonly HttpClient _httpClient;


    /// <summary>
    /// 初始化百科 Client。
    /// </summary>
    /// <param name="httpClient">可选共享 HttpClient；为 null 时自建并开启自动解压。</param>
    public BlackboardClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
    }


    /// <summary>
    /// 按游戏取百科 app_sn（blackboard 路径与查询参数共用）。
    /// </summary>
    /// <param name="gameBiz">当前游戏业务标识。</param>
    /// <returns>app_sn；不支持时返回 null。</returns>
    public static string? GetAppSn(GameBiz gameBiz) => gameBiz.Game switch
    {
        GameBiz.hk4e => "ys_obc",
        GameBiz.hkrpg => "sr_wiki",
        GameBiz.nap => "zzz_wiki",
        _ => null,
    };


    /// <summary>
    /// 拉取卡池 / 调频 / 跃迁列表。
    /// </summary>
    /// <param name="appSn">百科应用标识（如 <c>ys_obc</c>）。</param>
    /// <param name="lang">语言参数，百科侧对卡池标题影响有限，默认 zh-cn。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>卡池列表数据。</returns>
    public async Task<BlackboardGachaPoolData> GetGachaPoolAsync(string appSn, string lang = "zh-cn", CancellationToken cancellationToken = default)
    {
        string url = $"{ApiHost}/common/blackboard/{appSn}/v1/gacha_pool?app_sn={Uri.EscapeDataString(appSn)}&lang={Uri.EscapeDataString(lang)}";
        return await CommonGetAsync(url, BlackboardJsonContext.Default.miHoYoApiWrapperBlackboardGachaPoolData, cancellationToken);
    }


    /// <summary>
    /// 拉取首页运营位树（绝区零热点活动等）。
    /// </summary>
    /// <param name="appSn">百科应用标识。</param>
    /// <param name="lang">语言参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运营位树。</returns>
    public async Task<BlackboardPositionData> GetHomePositionAsync(string appSn, string lang = "zh-cn", CancellationToken cancellationToken = default)
    {
        string url = $"{ApiHost}/common/blackboard/{appSn}/v1/home/position?app_sn={Uri.EscapeDataString(appSn)}&lang={Uri.EscapeDataString(lang)}";
        return await CommonGetAsync(url, BlackboardJsonContext.Default.miHoYoApiWrapperBlackboardPositionData, cancellationToken);
    }


    /// <summary>
    /// 拉取百科首页内容列表（频道树，含子频道条目）。
    /// </summary>
    /// <param name="appSn">百科应用标识（如 <c>zzz_wiki</c>）。</param>
    /// <param name="channelId">频道 id（绝区零档案为 13）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>频道树。</returns>
    public async Task<BlackboardContentListData> GetHomeContentListAsync(string appSn, int channelId, CancellationToken cancellationToken = default)
    {
        string url = $"{WikiStaticHost}/common/blackboard/{appSn}/v1/home/content/list?app_sn={Uri.EscapeDataString(appSn)}&channel_id={channelId}";
        return await CommonGetAsync(url, BlackboardJsonContext.Default.miHoYoApiWrapperBlackboardContentListData, cancellationToken);
    }


    /// <summary>
    /// 拉取百科词条正文（含富文本中的视频地址）。
    /// </summary>
    /// <param name="wikiApp">词条路径与 <c>x-rpc-wiki_app</c> 用的短名（绝区零为 <c>zzz</c>）。</param>
    /// <param name="appSn">百科应用标识。</param>
    /// <param name="entryPageId">词条 / 内容 id。</param>
    /// <param name="lang">语言参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>词条数据。</returns>
    public async Task<WikiEntryPageData> GetEntryPageAsync(string wikiApp, string appSn, int entryPageId, string lang = "zh-cn", CancellationToken cancellationToken = default)
    {
        string url = $"{WikiStaticHost}/hoyowiki/{wikiApp}/wapi/entry_page?entry_page_id={entryPageId}&lang={Uri.EscapeDataString(lang)}&app_sn={Uri.EscapeDataString(appSn)}";
        return await CommonGetAsync(
            url,
            BlackboardJsonContext.Default.miHoYoApiWrapperWikiEntryPageData,
            cancellationToken,
            [("x-rpc-wiki_app", wikiApp), ("Referer", "https://baike.mihoyo.com/")]);
    }


    private async Task<T> CommonGetAsync<T>(
        string url,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<miHoYoApiWrapper<T>> typeInfo,
        CancellationToken cancellationToken,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (extraHeaders is not null)
        {
            foreach ((string name, string value) in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
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
