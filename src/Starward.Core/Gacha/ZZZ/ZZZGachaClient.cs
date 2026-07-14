using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Starward.Core.Gacha.ZZZ;

/// <summary>
/// 绝区零（Zenless Zone Zero，GameBiz.nap）抽卡记录客户端。
/// 继承自 <see cref="GachaLogClient"/>，实现 ZZZ 特有的 URL 解析规则和接口调用方式。
/// 主要职责：
/// 1. 从游戏网页缓存或用户提供的「频段记录」URL 中解析出有效的 API 前缀（包含 authkey 等认证信息）。
/// 2. 通过 miHoYo/HoYoverse 公共 gacha_record 接口分页拉取抽卡记录（支持全量 / 增量）。
/// 3. 支持按单个频段或按 GachaLogQuery 精确查询。
/// </summary>
public class ZZZGachaClient : GachaLogClient
{

    /// <summary>
    /// ZZZ 支持查询的全部频段类型（用于 GetUidByGachaUrlAsync 和全量拉取时遍历）。
    /// 包含以下 6 种：
    /// <list type="bullet">
    /// <item><description>1 - 常驻频段（Standard Channel）</description></item>
    /// <item><description>2 - 独家频段（Exclusive Channel）</description></item>
    /// <item><description>3 - 音擎频段（W-Engine Channel）</description></item>
    /// <item><description>5 - 邦布频段（Bangboo Channel）</description></item>
    /// <item><description>102 - 独家重映（Exclusive Rescreening）</description></item>
    /// <item><description>103 - 音擎回响（W-Engine Reverberation）</description></item>
    /// </list>
    /// </summary>
    public override IReadOnlyCollection<IGachaType> QueryGachaTypes { get; init; } = new ZZZGachaType[] { 1, 2, 3, 5, 102, 103 }.Cast<IGachaType>().ToList().AsReadOnly();



    /// <summary>
    /// 初始化 <see cref="ZZZGachaClient"/> 实例。
    /// </summary>
    /// <param name="httpClient">
    /// 可选的 HttpClient 实例。
    /// 若为 null，基类会创建一个默认的 HttpClient（启用 AutomaticDecompression = All，并使用 RequestVersionOrHigher）。
    /// </param>
    public ZZZGachaClient(HttpClient? httpClient = null) : base(httpClient)
    {

    }


    /// <summary>
    /// 将游戏内「频段记录」页面的完整 URL 转换为可直接调用的 getGachaLog API 前缀。
    /// 这是 ZZZ 客户端最核心的 URL 解析逻辑。
    /// </summary>
    /// <param name="gachaUrl">
    /// 输入的原始 URL，通常来自：
    /// <list type="bullet">
    /// <item>游戏内「频段记录」页面复制的链接（包含 #/log）</item>
    /// <item>网页缓存文件（data_2）中匹配到的完整链接</item>
    /// <item>已经过一次处理的 public-operation-nap 链接</item>
    /// </list>
    /// </param>
    /// <param name="lang">可选的语言代码（如 zh-cn、en-us）。若提供，会替换或添加 &amp;lang= 参数。</param>
    /// <returns>
    /// 构造完成的 API 请求前缀，格式类似：
    /// https://public-operation-nap.mihoyo.com/common/gacha_record/api/getGachaLog?authkey=...&amp;game_biz=nap_cn...
    /// 已自动清理 real_gacha_type / page / size / end_id 等分页参数（若输入的是完整链接）。
    /// </returns>
    /// <exception cref="ArgumentException">无法识别 URL 来源（既不是 mihoyo webstatic、也不是 hoyoverse gs、也不是 public-operation-nap）时抛出。</exception>
    protected override string GetGachaUrlPrefix(string gachaUrl, string? lang = null)
    {
        // 情况1：国服网页缓存 / 复制链接（webstatic.mihoyo.com）
        var match = Regex.Match(gachaUrl, @"(https://webstatic\.mihoyo\.com[!-z]+)");
        if (match.Success)
        {
            gachaUrl = match.Groups[1].Value;
            var auth = gachaUrl.Substring(gachaUrl.IndexOf('?')).Replace("#/log", "");
            gachaUrl = API_PREFIX_ZZZ_CN + auth;
            if (!string.IsNullOrWhiteSpace(lang))
            {
                gachaUrl = Regex.Replace(gachaUrl, @"&lang=[^&]+", $"&lang={LanguageUtil.FilterLanguage(lang)}");
            }
            return gachaUrl;
        }

        // 情况2：国际服网页缓存 / 复制链接（gs.hoyoverse.com）
        match = Regex.Match(gachaUrl, @"(https://gs\.hoyoverse\.com[!-z]+)");
        if (match.Success)
        {
            gachaUrl = match.Groups[1].Value;
            var auth = gachaUrl.Substring(gachaUrl.IndexOf('?')).Replace("#/log", "");
            gachaUrl = API_PREFIX_ZZZ_OS + auth;
            if (!string.IsNullOrWhiteSpace(lang))
            {
                gachaUrl = Regex.Replace(gachaUrl, @"&lang=[^&]+", $"&lang={LanguageUtil.FilterLanguage(lang)}");
            }
            return gachaUrl;
        }

        // 情况3：已经过处理的 public-operation-nap 链接（直接来自缓存或上一次处理结果）
        match = Regex.Match(gachaUrl, @"(https://public-operation-nap[!-z]+)");
        if (match.Success)
        {
            gachaUrl = match.Groups[1].Value;
            // 清理可能残留的分页和类型参数，保证后续查询时由调用方控制
            gachaUrl = Regex.Replace(gachaUrl, @"&real_gacha_type=\d", "");
            gachaUrl = Regex.Replace(gachaUrl, @"&page=\d", "");
            gachaUrl = Regex.Replace(gachaUrl, @"&size=\d", "");
            gachaUrl = Regex.Replace(gachaUrl, @"&end_id=\d", "");
            if (!string.IsNullOrWhiteSpace(lang))
            {
                gachaUrl = Regex.Replace(gachaUrl, @"&lang=[^&]+", $"&lang={LanguageUtil.FilterLanguage(lang)}");
            }
            return gachaUrl;
        }

        throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL);
    }




    /// <summary>
    /// 拉取指定账号的全部频段抽卡记录（增量模式）。
    /// 会依次遍历 <see cref="QueryGachaTypes"/> 中的所有频段进行拉取。
    /// </summary>
    /// <param name="gachaUrl">从游戏网页缓存提取的完整抽卡记录 URL（必须包含有效的 authkey）。</param>
    /// <param name="endId">
    /// 增量起点：只拉取 Id 大于此值的记录。
    /// 传 0 表示拉取全部（受服务器最近 6 个月限制）。
    /// </param>
    /// <param name="lang">语言代码，用于接口返回的物品名称本地化。</param>
    /// <param name="progress">进度回调，报告当前正在获取的 (频段类型, 页码)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次获取到的所有 <see cref="GachaLogItem"/>（实际为 <see cref="ZZZGachaItem"/>）列表，按接口返回顺序排列。</returns>
    public override async Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        return await GetGachaLogAsync<ZZZGachaItem>(gachaUrl, endId, lang, progress, cancellationToken);
    }


    /// <summary>
    /// 仅拉取指定单个频段的抽卡记录（增量模式）。
    /// </summary>
    /// <param name="gachaUrl">从游戏网页缓存提取的完整抽卡记录 URL。</param>
    /// <param name="gachaType">要拉取的频段类型（必须是 <see cref="QueryGachaTypes"/> 中的有效值）。</param>
    /// <param name="endId">增量起点 Id（只返回大于此 Id 的记录）。</param>
    /// <param name="lang">语言代码。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指定频段下获取到的记录列表。</returns>
    public override async Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, IGachaType gachaType, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        return await GetGachaLogAsync<ZZZGachaItem>(gachaUrl, gachaType, endId, lang, progress, cancellationToken);
    }


    /// <summary>
    /// 使用显式的 <see cref="GachaLogQuery"/> 进行单次精确查询（不自动分页）。
    /// 通常用于高级场景或配合外部分页逻辑。
    /// </summary>
    /// <param name="gachaUrl">原始抽卡记录 URL。</param>
    /// <param name="query">
    /// 查询参数对象，包含：
    /// <list type="bullet">
    /// <item>GachaType（使用 real_gacha_type 序列化，ZZZ 特有）</item>
    /// <item>Page、Size、EndId</item>
    /// </list>
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>单页查询结果（最多 Size 条记录）。</returns>
    public override async Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, GachaLogQuery query, CancellationToken cancellationToken = default)
    {
        string prefix = GetGachaUrlPrefix(gachaUrl);
        return await GetGachaLogByQueryAsync<ZZZGachaItem>(prefix, query, cancellationToken);
    }


    /// <summary>
    /// ZZZ 特有的查询实现：在发起请求前会随机等待 200~300 毫秒（礼貌性限流）。
    /// 同时注意 ZZZ 接口使用 <c>real_gacha_type</c> 而非 <c>gacha_type</c> 参数（由 <see cref="GachaLogQuery.ToString"/> 自动处理）。
    /// </summary>
    /// <typeparam name="T">具体抽卡记录类型（通常为 <see cref="ZZZGachaItem"/>）。</typeparam>
    /// <param name="gachaUrlPrefix">已通过 <see cref="GetGachaUrlPrefix"/> 处理好的 API 前缀（包含认证信息）。</param>
    /// <param name="param">分页查询参数（GachaType / Page / Size / EndId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>单页数据列表。若接口返回 retcode != 0 会抛出 <see cref="GachaApiException"/>。</returns>
    protected override async Task<List<T>> GetGachaLogByQueryAsync<T>(string gachaUrlPrefix, GachaLogQuery param, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Random.Shared.Next(200, 300), cancellationToken);
        var url = $"{gachaUrlPrefix}&{param}";
        var wrapper = await _httpClient.GetFromJsonAsync(url, typeof(miHoYoApiWrapper<GachaLogResult<T>>), GachaLogJsonContext.Default, cancellationToken) as miHoYoApiWrapper<GachaLogResult<T>>;
        if (wrapper is null)
        {
            return new List<T>();
        }
        else if (wrapper.Retcode != 0)
        {
            throw new GachaApiException(wrapper.Retcode, wrapper.Message);
        }
        else
        {
            return wrapper.Data.List;
        }
    }

}
