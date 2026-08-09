using System.Net;
using System.Net.Http.Json;

namespace Starward.Core.Blackboard;

/// <summary>
/// 米游社百科 blackboard 公开接口（无 Cookie / 无 DS）。
/// 数据以国服百科为准；国际服无同构接口。
/// </summary>
public class BlackboardClient
{

    private const string ApiHost = "https://api-static.mihoyo.com";

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


    private async Task<T> CommonGetAsync<T>(string url, System.Text.Json.Serialization.Metadata.JsonTypeInfo<miHoYoApiWrapper<T>> typeInfo, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
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
