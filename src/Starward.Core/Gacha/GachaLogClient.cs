using Microsoft.Win32;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace Starward.Core.Gacha;

/// <summary>
/// 抽卡记录客户端抽象基类。
/// 为原神（Genshin Impact）、星穹铁道（Honkai: Star Rail）、绝区零（Zenless Zone Zero）等 miHoYo/Hoyoverse 游戏提供统一的抽卡记录获取能力。
/// 
/// 核心职责：
/// 1. 从游戏内「祈愿记录/跃迁记录/频段记录」页面的 URL（或网页缓存 data_2 文件）中解析出包含 authkey 的 API 请求前缀。
/// 2. 通过官方 public-operation-* 接口分页拉取抽卡记录，支持全量获取和增量同步（通过 endId）。
/// 3. 提供通用的 UID 获取、网页缓存扫描、游戏安装路径推断等工具方法。
/// 
/// 派生类需实现：
/// <list type="bullet">
/// <item><see cref="QueryGachaTypes"/>：游戏支持的全部卡池/频段类型。</item>
/// <item><see cref="GetGachaUrlPrefix(string, string?)"/>：游戏特有的 URL 解析与参数清理规则。</item>
/// <item>三个 <see cref="GetGachaLogAsync"/> 重载的抽象实现（通常委托给泛型保护方法）。</item>
/// </list>
/// </summary>
public abstract class GachaLogClient
{


    #region 原神（Genshin Impact）相关常量

    /// <summary>原神国服（含 B 服）网页缓存相对路径（相对于游戏安装目录）。</summary>
    protected const string WEB_CACHE_PATH_YS_CN = @"YuanShen_Data\webCaches\Cache\Cache_Data\data_2";
    /// <summary>原神国际服网页缓存相对路径。</summary>
    protected const string WEB_CACHE_PATH_YS_OS = @"GenshinImpact_Data\webCaches\Cache\Cache_Data\data_2";

    /// <summary>原神国服抽卡记录 API 前缀（getGachaLog）。</summary>
    protected const string API_PREFIX_YS_CN = "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog";
    /// <summary>原神国际服抽卡记录 API 前缀。</summary>
    protected const string API_PREFIX_YS_OS = "https://public-operation-hk4e-sg.hoyoverse.com/gacha_info/api/getGachaLog";

    /// <summary>用于从网页缓存二进制文件中匹配原神国服抽卡记录 URL 的前缀字节序列（LastIndexOf 匹配）。</summary>
    protected static ReadOnlySpan<byte> SPAN_WEB_PREFIX_YS_CN => "https://webstatic.mihoyo.com/hk4e/event/e20190909gacha"u8;
    /// <summary>用于从网页缓存二进制文件中匹配原神国际服抽卡记录 URL 的前缀字节序列。</summary>
    protected static ReadOnlySpan<byte> SPAN_WEB_PREFIX_YS_OS => "https://gs.hoyoverse.com/genshin/event/e20190909gacha"u8;

    #endregion



    #region 星穹铁道（Honkai: Star Rail）相关常量

    /// <summary>星穹铁道网页缓存相对路径（国服/国际服/B 服共用）。</summary>
    protected const string WEB_CACHE_SR_PATH = @"StarRail_Data\webCaches\Cache\Cache_Data\data_2";

    /// <summary>星穹铁道国服跃迁记录 API 前缀。</summary>
    protected const string API_PREFIX_SR_CN = "https://public-operation-hkrpg.mihoyo.com/common/gacha_record/api/getGachaLog";
    /// <summary>星穹铁道国际服跃迁记录 API 前缀。</summary>
    protected const string API_PREFIX_SR_OS = "https://public-operation-hkrpg-sg.hoyoverse.com/common/gacha_record/api/getGachaLog";

    /// <summary>用于从网页缓存匹配星穹铁道国服跃迁记录 URL 的前缀。</summary>
    protected static ReadOnlySpan<byte> SPAN_WEB_PREFIX_SR_CN => "https://webstatic.mihoyo.com/hkrpg/event/e20211215gacha"u8;
    /// <summary>用于从网页缓存匹配星穹铁道国际服跃迁记录 URL 的前缀。</summary>
    protected static ReadOnlySpan<byte> SPAN_WEB_PREFIX_SR_OS => "https://gs.hoyoverse.com/hkrpg/event/e20211215gacha"u8;

    #endregion


    #region 绝区零（Zenless Zone Zero）相关常量

    /// <summary>绝区零网页缓存相对路径（国服/国际服/B 服共用）。</summary>
    protected const string WEB_CACHE_ZZZ_PATH = @"ZenlessZoneZero_Data\webCaches\Cache\Cache_Data\data_2";

    /// <summary>绝区零国服频段记录 API 前缀。</summary>
    protected const string API_PREFIX_ZZZ_CN = "https://public-operation-nap.mihoyo.com/common/gacha_record/api/getGachaLog";
    /// <summary>绝区零国际服频段记录 API 前缀。</summary>
    protected const string API_PREFIX_ZZZ_OS = "https://public-operation-nap-sg.hoyoverse.com/common/gacha_record/api/getGachaLog";

    /// <summary>用于从网页缓存匹配绝区零国服频段记录 URL 的前缀。</summary>
    protected static ReadOnlySpan<byte> SPAN_WEB_PREFIX_ZZZ_CN => "https://webstatic.mihoyo.com/nap/event/e20230424gacha"u8;
    /// <summary>用于从网页缓存匹配绝区零国际服频段记录 URL 的前缀。</summary>
    protected static ReadOnlySpan<byte> SPAN_WEB_PREFIX_ZZZ_OS => "https://gs.hoyoverse.com/nap/event/e20230424gacha"u8;

    #endregion





    #region 崩坏3 注册表键（供历史/派生代码参考）

    protected const string REG_KEY_BH3_CN = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\崩坏3";
    protected const string REG_KEY_BH3_OS = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Honkai Impact 3";
    protected const string REG_KEY_BH3_GL = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Honkai Impact 3rd";
    protected const string REG_KEY_BH3_TW = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\崩壊3rd";
    protected const string REG_KEY_BH3_KR = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\붕괴3rd";
    protected const string REG_KEY_BH3_JP = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\崩壊3rd";

    #endregion


    /// <summary>
    /// 内部使用的 HttpClient 实例。
    /// 默认构造时启用 AutomaticDecompression = All，并设置 DefaultVersionPolicy = RequestVersionOrHigher。
    /// </summary>
    protected readonly HttpClient _httpClient;


    /// <summary>
    /// 当前游戏支持查询的全部卡池/频段类型集合（只读）。
    /// 用于 <see cref="GetUidByGachaUrlAsync"/> 和全量获取时遍历所有类型。
    /// 由派生类在 init 时提供，例如 Genshin: 100,200,301,302,500。
    /// </summary>
    public abstract IReadOnlyCollection<IGachaType> QueryGachaTypes { get; init; }



    /// <summary>
    /// 初始化 <see cref="GachaLogClient"/> 基类实例。
    /// </summary>
    /// <param name="httpClient">
    /// 可选的外部 HttpClient。
    /// 若为 null，内部会创建一个默认的 HttpClient（启用 AutomaticDecompression = All，并使用 RequestVersionOrHigher）。
    /// </param>
    public GachaLogClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
            {
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };
        }
        else
        {
            _httpClient = httpClient;
        }
    }



    #region public method



    /// <summary>
    /// 通过抽卡记录 URL 获取当前玩家的 UID。
    /// 内部会尝试使用 <see cref="QueryGachaTypes"/> 中的每个类型发起一次最小查询（page=1, size=1），取返回记录中的 Uid。
    /// </summary>
    /// <param name="gachaUrl">从游戏网页缓存或「记录」页面获取的完整抽卡记录 URL（必须包含有效的 authkey 等参数）。</param>
    /// <returns>成功时返回玩家 UID；若所有类型均无记录（最近 6 个月无抽卡）则返回 0。</returns>
    public async Task<long> GetUidByGachaUrlAsync(string gachaUrl)
    {
        var prefix = GetGachaUrlPrefix(gachaUrl);
        foreach (var gachaType in QueryGachaTypes)
        {
            var param = new GachaLogQuery(gachaType, 1, 1, 0);
            var list = await GetGachaLogByQueryAsync<GachaLogItem>(prefix, param);
            if (list.Count != 0)
            {
                return list.First().Uid;
            }
        }
        return 0;
    }



    /// <summary>
    /// 拉取指定账号的全部抽卡记录（按 <see cref="QueryGachaTypes"/> 遍历所有类型）。
    /// 这是最常用的全量/增量获取入口。
    /// </summary>
    /// <param name="gachaUrl">抽卡记录 URL（来自网页缓存或手动复制）。</param>
    /// <param name="endId">增量起点 Id：只返回 Id &gt; endId 的记录。传 0 表示拉取全部（受服务器最近约 6 个月限制）。</param>
    /// <param name="lang">可选语言代码，用于接口返回物品名称的本地化（例如 zh-cn, en-us）。派生类会在 URL 解析时应用。</param>
    /// <param name="progress">进度回调，每处理一页报告一次 (当前卡池类型, 页码)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次获取到的所有 <see cref="GachaLogItem"/>（实际类型由派生类决定，如 GenshinGachaItem）。</returns>
    public abstract Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default);


    /// <summary>
    /// 仅拉取指定单个卡池/频段类型的抽卡记录（增量模式）。
    /// </summary>
    /// <param name="gachaUrl">抽卡记录 URL。</param>
    /// <param name="gachaType">要拉取的具体类型（必须属于 <see cref="QueryGachaTypes"/>）。</param>
    /// <param name="endId">增量起点 Id（只返回大于此 Id 的记录）。</param>
    /// <param name="lang">语言代码。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指定类型下获取到的记录列表。</returns>
    public abstract Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, IGachaType gachaType, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default);


    /// <summary>
    /// 使用显式的 <see cref="GachaLogQuery"/> 进行单次精确查询（不自动分页）。
    /// 通常用于高级场景或配合外部分页逻辑。派生类实现时需正确调用带前缀的保护方法。
    /// </summary>
    /// <param name="gachaUrl">原始抽卡记录 URL。</param>
    /// <param name="query">查询参数（包含 GachaType、Page、Size、EndId）。注意 ZZZ 使用 real_gacha_type。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>单页查询结果（最多 Size 条）。</returns>
    public abstract Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, GachaLogQuery query, CancellationToken cancellationToken = default);




    /// <summary>
    /// 从游戏安装目录的网页缓存文件中尝试提取抽卡记录 URL。
    /// 这是最常用的“自动获取 URL”入口（被 GachaLogService 进一步包装）。
    /// </summary>
    /// <param name="gameBiz">游戏业务线（hk4e / hkrpg / nap 等）。</param>
    /// <param name="installPath">游戏安装根目录。若为 null，内部 GetGachaCacheFilePath 仍会扫描常见 webCaches 位置。</param>
    /// <returns>成功提取到的 gacha log URL（包含 authkey）；文件不存在或未匹配到时返回 null。</returns>
    public static string? GetGachaUrlFromWebCache(GameBiz gameBiz, string? installPath = null)
    {
        var file = GetGachaCacheFilePath(gameBiz, installPath);
        if (File.Exists(file))
        {
            return FindMatchStringFromFile(file, GetGachaUrlPattern(gameBiz));
        }
        return null;
    }



    /// <summary>
    /// 根据游戏业务线计算 webCaches 文件夹的完整路径。
    /// 主要供 UI 或外部工具使用，用于让用户手动选择缓存目录。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="installPath">游戏安装根目录（可为 null，此时返回相对路径拼接结果）。</param>
    /// <returns>webCaches 文件夹完整路径。</returns>
    public static string GetWebCachesFolderPath(GameBiz gameBiz, string? installPath)
    {
        string prefix = gameBiz.Value switch
        {
            GameBiz.hk4e_cn or GameBiz.hk4e_bilibili => @"YuanShen_Data\webCaches",
            GameBiz.hk4e_global => @"GenshinImpact_Data\webCaches",
            GameBiz.hkrpg_cn or GameBiz.hkrpg_global or GameBiz.hkrpg_bilibili => @"StarRail_Data\webCaches",
            GameBiz.nap_cn or GameBiz.nap_global or GameBiz.nap_bilibili => @"ZenlessZoneZero_Data\webCaches",
            _ => throw new ArgumentOutOfRangeException($"Unknown region {gameBiz}"),
        };
        return Path.Join(installPath, prefix);
    }



    /// <summary>
    /// 获取指定游戏当前应该读取的 gacha cache 文件完整路径。
    /// 逻辑：
    /// 1. 先根据 gameBiz 确定基础 data_2 路径。
    /// 2. 比较基础文件与 webCaches 下所有子目录中更新的 data_2，取最后写入时间最新的那个。
    /// </summary>
    /// <param name="gameBiz">游戏业务线。</param>
    /// <param name="installPath">游戏安装根目录。</param>
    /// <returns>最终选定的 data_2 文件完整路径（即使文件不存在也会返回路径）。</returns>
    public static string GetGachaCacheFilePath(GameBiz gameBiz, string? installPath)
    {
        string file = gameBiz.Value switch
        {
            GameBiz.hk4e_cn or GameBiz.hk4e_bilibili => Path.Join(installPath, WEB_CACHE_PATH_YS_CN),
            GameBiz.hk4e_global => Path.Join(installPath, WEB_CACHE_PATH_YS_OS),
            GameBiz.hkrpg_cn or GameBiz.hkrpg_global or GameBiz.hkrpg_bilibili => Path.Join(installPath, WEB_CACHE_SR_PATH),
            GameBiz.nap_cn or GameBiz.nap_global or GameBiz.nap_bilibili => Path.Join(installPath, WEB_CACHE_ZZZ_PATH),
            _ => throw new ArgumentOutOfRangeException($"Unknown region {gameBiz}"),
        };
        DateTime lastWriteTime = DateTime.MinValue;
        if (File.Exists(file))
        {
            lastWriteTime = File.GetLastWriteTime(file);
        }
        string webCache = GetWebCachesFolderPath(gameBiz, installPath);
        if (Directory.Exists(webCache))
        {
            foreach (var item in Directory.GetDirectories(webCache))
            {
                string target = Path.Join(item, @"Cache\Cache_Data\data_2");
                if (File.Exists(target))
                {
                    DateTime targetLastWriteTime = File.GetLastWriteTime(target);
                    if (targetLastWriteTime > lastWriteTime)
                    {
                        file = target;
                        lastWriteTime = targetLastWriteTime;
                    }
                }
            }
        }
        return file;
    }



    /// <summary>
    /// 根据 gameBiz 返回用于二进制匹配的 URL 前缀字节序列。
    /// 仅供 <see cref="GetGachaUrlFromWebCache"/> 和 <see cref="GetGachaCacheFilePath"/> 内部使用。
    /// </summary>
    private static ReadOnlySpan<byte> GetGachaUrlPattern(GameBiz gameBiz)
    {
        return gameBiz.Value switch
        {
            GameBiz.hk4e_cn or GameBiz.hk4e_bilibili => SPAN_WEB_PREFIX_YS_CN,
            GameBiz.hk4e_global => SPAN_WEB_PREFIX_YS_OS,
            GameBiz.hkrpg_cn or GameBiz.hkrpg_bilibili => SPAN_WEB_PREFIX_SR_CN,
            GameBiz.hkrpg_global => SPAN_WEB_PREFIX_SR_OS,
            GameBiz.nap_cn or GameBiz.nap_bilibili => SPAN_WEB_PREFIX_ZZZ_CN,
            GameBiz.nap_global => SPAN_WEB_PREFIX_ZZZ_OS,
            _ => throw new ArgumentOutOfRangeException($"Unknown region {gameBiz}"),
        };
    }



    #endregion




    #region protected method



    /// <summary>
    /// 通用的 GET + miHoYoApiWrapper 反序列化 + 错误处理方法。
    /// 主要供「Gacha Info」（wiki/角色列表等）接口使用。
    /// </summary>
    /// <typeparam name="T">期望的数据类型（Data 字段的内容）。</typeparam>
    /// <param name="url">完整请求 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>wrapper.Data 的强类型结果。</returns>
    /// <exception cref="miHoYoApiException">retcode != 0 或响应体为 null 时抛出。</exception>
    protected async Task<T> CommonGetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        var wrapper = await _httpClient.GetFromJsonAsync(url, typeof(miHoYoApiWrapper<T>), GachaLogJsonContext.Default, cancellationToken) as miHoYoApiWrapper<T>;
        if (wrapper is null)
        {
            throw new miHoYoApiException(-1, "Response body is null");
        }
        else if (wrapper.Retcode != 0)
        {
            throw new miHoYoApiException(wrapper.Retcode, wrapper.Message);
        }
        else
        {
            return wrapper.Data;
        }
    }



    /// <summary>
    /// 将用户提供的抽卡记录页面 URL 转换为可直接调用的 getGachaLog API 前缀 URL。
    /// 这是各游戏客户端最核心的差异化实现。
    /// </summary>
    /// <param name="gachaUrl">原始 URL（可能包含 #/log、各种域名等）。</param>
    /// <param name="lang">可选语言代码。若提供，派生类实现通常会替换 URL 中的 lang 参数。</param>
    /// <returns>包含 authkey 等必要查询参数的 API 前缀（不含 gacha_type/page/size/end_id）。</returns>
    /// <exception cref="ArgumentException">无法识别 URL 来源时抛出（不同游戏消息略有不同）。</exception>
    protected abstract string GetGachaUrlPrefix(string gachaUrl, string? lang = null);



    /// <summary>
    /// 保护方法：遍历 <see cref="QueryGachaTypes"/> 拉取所有类型的记录（泛型实现）。
    /// 供派生类的公开 GetGachaLogAsync 重载调用。
    /// </summary>
    /// <typeparam name="T">具体游戏的 GachaLogItem 派生类型（如 GenshinGachaItem）。</typeparam>
    /// <param name="gachaUrl">原始抽卡记录 URL。</param>
    /// <param name="endId">增量起点。</param>
    /// <param name="lang">语言。</param>
    /// <param name="progress">进度。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合并后的记录列表。</returns>
    protected async Task<List<T>> GetGachaLogAsync<T>(string gachaUrl, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default) where T : GachaLogItem
    {
        endId = Math.Clamp(endId, 0, long.MaxValue);
        var prefix = GetGachaUrlPrefix(gachaUrl, lang);
        var result = new List<T>();
        foreach (var gachaType in QueryGachaTypes)
        {
            result.AddRange(await GetGachaLogByTypeAsync<T>(prefix, gachaType, endId, progress, cancellationToken));
        }
        return result;
    }




    /// <summary>
    /// 保护方法：拉取单个指定类型的记录（泛型实现）。
    /// </summary>
    protected async Task<List<T>> GetGachaLogAsync<T>(string gachaUrl, IGachaType gachaType, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default) where T : GachaLogItem
    {
        endId = Math.Clamp(endId, 0, long.MaxValue);
        var prefix = GetGachaUrlPrefix(gachaUrl, lang);
        return await GetGachaLogByTypeAsync<T>(prefix, gachaType, endId, progress, cancellationToken);
    }




    /// <summary>
    /// 实际执行单页查询的核心虚方法。
    /// 默认实现包含 200~300ms 随机延迟（礼貌限流），然后拼接 URL 并反序列化。
    /// ZZZGachaClient 重写了此方法以使用 real_gacha_type 并保持相同延迟行为。
    /// </summary>
    /// <typeparam name="T">记录类型。</typeparam>
    /// <param name="gachaUrlPrefix">已解析好的 API 前缀（含 authkey）。</param>
    /// <param name="param">分页查询参数（GachaLogQuery 负责序列化为 gacha_type=...&amp;page=... 等）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前页的数据列表。若 retcode != 0 抛出 miHoYoApiException。</returns>
    protected virtual async Task<List<T>> GetGachaLogByQueryAsync<T>(string gachaUrlPrefix, GachaLogQuery param, CancellationToken cancellationToken = default) where T : GachaLogItem
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
            throw new miHoYoApiException(wrapper.Retcode, wrapper.Message);
        }
        else
        {
            return wrapper.Data.List;
        }
    }




    /// <summary>
    /// 按指定类型分页拉取直到没有更多数据或达到 endId。
    /// 每页固定 size=20，报告进度，自动更新 EndId 实现增量。
    /// </summary>
    protected async Task<List<T>> GetGachaLogByTypeAsync<T>(string prefix, IGachaType gachaType, long endId = 0, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default) where T : GachaLogItem
    {
        var param = new GachaLogQuery(gachaType, 1, 20, 0);
        var result = new List<T>();
        while (true)
        {
            progress?.Report((gachaType, param.Page));
            var list = await GetGachaLogByQueryAsync<T>(prefix, param, cancellationToken);
            result.AddRange(list);
            if (list.Count == 20 && list.Last().Id > endId)
            {
                param.Page++;
                param.EndId = list.Last().Id;
            }
            else
            {
                break;
            }
        }
        return result;
    }


    /// <summary>
    /// 从 Windows 注册表读取某游戏启动器的 InstallPath，再从其 config.ini 解析实际的 game_install_path。
    /// 主要用于老游戏或特定场景下的自动定位（当前主要供崩坏3相关代码或历史逻辑）。
    /// </summary>
    /// <param name="regKey">注册表卸载项完整路径。</param>
    /// <returns>成功解析到的游戏安装目录完整路径；失败返回 null。</returns>
    [SupportedOSPlatform("windows")]
    protected static string? GetGameInstallPathFromRegistry(string regKey)
    {
        var launcherPath = Registry.GetValue(regKey, "InstallPath", null) as string;
        var configPath = Path.Join(launcherPath, "config.ini");
        if (File.Exists(configPath))
        {
            var str = File.ReadAllText(configPath);
            var installPath = Regex.Match(str, @"game_install_path=(.+)").Groups[1].Value.Trim();
            if (Directory.Exists(installPath))
            {
                return Path.GetFullPath(installPath);
            }
        }
        return null;
    }


    /// <summary>
    /// 在指定二进制缓存文件中从后向前查找包含特定前缀的第一个完整 URL 字符串。
    /// 查找逻辑：LastIndexOf(prefix) 后，向后找到第一个 \0 或 " 作为结束。
    /// </summary>
    /// <param name="path">缓存文件路径（通常是 data_2）。</param>
    /// <param name="prefix">要匹配的 URL 前缀字节序列（来自 SPAN_WEB_PREFIX_*）。</param>
    /// <returns>提取到的完整 URL 字符串；未找到返回 null。</returns>
    protected static string? FindMatchStringFromFile(string path, ReadOnlySpan<byte> prefix)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var ms = new MemoryStream();
        fs.CopyTo(ms);
        var span = ms.ToArray().AsSpan();
        var index = span.LastIndexOf(prefix);
        if (index >= 0)
        {
            var length = span[index..].IndexOfAny("\0\""u8);
            return Encoding.UTF8.GetString(span.Slice(index, length));
        }

        return null;
    }


    #endregion




    #region Gacha Info（物品/卡池元数据，用于名称修正和展示）


    /// <summary>
    /// 获取原神卡池/角色/武器信息（GachaWiki）。
    /// 国服中文走 miHoYo 内部接口，其他走 hoyolab 模拟器接口。
    /// </summary>
    /// <param name="gameBiz">hk4e_cn / hk4e_global 等。</param>
    /// <param name="lang">期望语言（会被 <see cref="LanguageUtil.FilterLanguage"/> 规范化）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含角色、武器、卡池配置的 <see cref="GenshinGachaWiki"/>。</returns>
    public async Task<GenshinGachaWiki> GetGenshinGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        lang = LanguageUtil.FilterLanguage(lang);
        GenshinGachaWiki wiki;
        if (gameBiz.IsChinaServer() && lang is "zh-cn")
        {
            const string url = "https://api-takumi.mihoyo.com/event/platsimulator/config?gids=2&game=hk4e";
            wiki = await CommonGetAsync<GenshinGachaWiki>(url, cancellationToken);
        }
        else
        {
            string url = $"https://sg-public-api.hoyolab.com/event/simulatoros/config?lang={lang}";
            wiki = await CommonGetAsync<GenshinGachaWiki>(url, cancellationToken);
        }
        wiki.Language = lang;
        return wiki;
    }


    /// <summary>
    /// 获取星穹铁道角色/光锥/卡池信息。
    /// 国服中文走内部 simulator 接口；国际服通过分页接口分别拉取 Avatar 和 Equipment 列表（每页 100 条，最多 10 页）。
    /// </summary>
    /// <param name="gameBiz">hkrpg 相关业务线。</param>
    /// <param name="lang">语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns><see cref="StarRailGachaWiki"/>（已填充 Avatar / Equipment 列表）。</returns>
    public async Task<StarRailGachaWiki> GetStarRailGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        lang = LanguageUtil.FilterLanguage(lang);
        StarRailGachaWiki wiki;
        if (gameBiz.IsChinaServer() && lang is "zh-cn")
        {
            const string url = "https://api-takumi.mihoyo.com/event/rpgsimulator/config?game=hkrpg";
            wiki = await CommonGetAsync<StarRailGachaWiki>(url, cancellationToken);
        }
        else
        {
            wiki = new StarRailGachaWiki { Game = "hkrpg", Avatar = new List<StarRailGachaInfo>(), Equipment = new List<StarRailGachaInfo>() };
            for (int i = 1; i <= 10; i++)
            {
                string url = $"https://sg-public-api.hoyolab.com/event/rpgcalc/avatar/list?game=hkrpg&lang={lang}&tab_from=TabAll&page={i}&size=100";
                var wrapper = await CommonGetAsync<StarRailGachaInfoWrapper>(url, cancellationToken);
                wiki.Avatar.AddRange(wrapper.List);
                if (wrapper.List.Count != 100)
                {
                    break;
                }
            }
            for (int i = 1; i <= 10; i++)
            {
                string url = $"https://sg-public-api.hoyolab.com/event/rpgcalc/equipment/list?game=hkrpg&lang={lang}&tab_from=TabAll&page={i}&size=100";
                var wrapper = await CommonGetAsync<StarRailGachaInfoWrapper>(url, cancellationToken);
                wiki.Equipment.AddRange(wrapper.List);
                if (wrapper.List.Count != 100)
                {
                    break;
                }
            }
        }
        wiki.Language = lang;
        return wiki;
    }


    /// <summary>
    /// 获取绝区零频段记录相关物品信息（角色、音擎、邦布等）。
    /// 国服中文使用内置静态 JSON；其他语言使用对应语言的静态资源。
    /// </summary>
    /// <param name="gameBiz">nap 相关业务线。</param>
    /// <param name="lang">语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns><see cref="ZZZGachaWiki"/>。</returns>
    public async Task<ZZZGachaWiki> GetZZZGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        lang = LanguageUtil.FilterLanguage(lang);
        ZZZGachaWiki wiki;
        if (gameBiz.IsChinaServer() && lang is "zh-cn")
        {
            const string url = $"https://starward-static.scighost.com/metadata/v1/zzz/ZZZGachaInfo.nap_cn.zh-cn.json";
            wiki = await CommonGetAsync<ZZZGachaWiki>(url, cancellationToken);
        }
        else
        {
            string url = $"https://starward-static.scighost.com/metadata/v1/zzz/ZZZGachaInfo.nap_global.{lang}.json";
            wiki = await CommonGetAsync<ZZZGachaWiki>(url, cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(wiki.Language))
        {
            wiki.Language = lang;
        }
        return wiki;
    }



    #endregion



}
