using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.Passport;
using Starward.Features.Database;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord;

/// <summary>
/// 协调国服 GameRecord Cookie 的账号级静默刷新：凭 stoken 换取 ltoken / cookie_token 等键值，
/// 并保证同一账号只执行一次 Token 交换。
/// </summary>
/// <remarks>
/// 依赖验证码登录或手动 Cookie 中已包含有效的 <c>stoken</c>（或 <c>stoken_v2</c>）与 <c>mid</c>。
/// 换票接口对齐社区文档（TeyvatGuide / UIGF mihoyo-api-collect）的
/// <c>getLTokenBySToken</c> 与 <c>getCookieAccountInfoBySToken</c>。
/// </remarks>
internal class GameRecordCookieRefreshService
{

    private readonly ILogger<GameRecordCookieRefreshService> _logger;

    private readonly MihoyoPassportClient _passportClient;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _refreshedCookies = new(StringComparer.Ordinal);


    /// <summary>
    /// 初始化 GameRecord Cookie 刷新服务。
    /// </summary>
    /// <param name="logger">日志记录器，不得记录 Cookie 或 Token 明文。</param>
    /// <param name="passportClient">国服 passport 客户端，用于 stoken 换票。</param>
    public GameRecordCookieRefreshService(ILogger<GameRecordCookieRefreshService> logger, MihoyoPassportClient passportClient)
    {
        _logger = logger;
        _passportClient = passportClient;
    }


    /// <summary>
    /// 刷新尚未绑定到具体角色的国服 Cookie，例如验证码登录或手动输入 Cookie 后的首次账号加载。
    /// </summary>
    /// <param name="cookie">待刷新的完整 Cookie，必须包含有效的 stoken_v2/stoken 与 mid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新后的完整 Cookie；缺少长期凭据或长期登录态已失效时返回 null。</returns>
    public Task<string?> RefreshCookieAsync(string cookie, CancellationToken cancellationToken = default)
    {
        return RefreshCookieCoreAsync(cookie, null, cancellationToken);
    }


    /// <summary>
    /// 刷新指定国服游戏角色所属账号的 Cookie。
    /// </summary>
    /// <param name="role">发生登录失效的国服角色，Cookie 不能为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新后的完整 Cookie；缺少长期凭据或长期登录态已失效时返回 null。</returns>
    public Task<string?> RefreshCookieAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role.Cookie))
        {
            return Task.FromResult<string?>(null);
        }
        return RefreshCookieCoreAsync(role.Cookie, role, cancellationToken);
    }


    /// <summary>
    /// 在账号锁内重新检查持久化 Cookie，必要时换票并原子回写关联记录。
    /// </summary>
    /// <param name="cookie">触发登录失效的 Cookie。</param>
    /// <param name="role">可选角色；非空时用于精确读取当前持久化 Cookie。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可重试请求的新 Cookie；无法刷新时返回 null。</returns>
    private async Task<string?> RefreshCookieCoreAsync(string cookie, GameRecordRole? role, CancellationToken cancellationToken)
    {
        if (_refreshedCookies.TryGetValue(cookie, out string? cachedCookie))
        {
            if (role is not null)
            {
                role.Cookie = cachedCookie;
            }
            return cachedCookie;
        }

        Dictionary<string, string> initialCookies = ParseCookie(cookie);
        if (!TryGetRefreshCredentials(initialCookies, out string stoken, out string mid))
        {
            return null;
        }

        string accountKey = GetAccountKey(initialCookies, mid);
        SemaphoreSlim semaphore = _accountLocks.GetOrAdd(accountKey, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // 等锁期间可能已有请求完成刷新；先采用缓存中的新 Cookie，避免重复交换。
            if (_refreshedCookies.TryGetValue(cookie, out string? completedCookie))
            {
                if (role is not null)
                {
                    role.Cookie = completedCookie;
                }
                return completedCookie;
            }

            string currentCookie = role is null ? cookie : GetCurrentCookie(role) ?? cookie;
            if (!string.Equals(currentCookie, cookie, StringComparison.Ordinal))
            {
                if (role is not null)
                {
                    role.Cookie = currentCookie;
                }
                return currentCookie;
            }

            Dictionary<string, string> currentCookies = ParseCookie(currentCookie);
            if (!TryGetRefreshCredentials(currentCookies, out stoken, out mid))
            {
                return null;
            }

            SyncPassportDevice();

            string ltoken;
            CookieTokenBySTokenResult cookieAccount;
            try
            {
                // 对齐验证码登录：stoken 同时换 ltoken 与 cookie_token
                ltoken = await _passportClient.GetLTokenBySTokenAsync(stoken, mid, cancellationToken);
                cookieAccount = await _passportClient.GetCookieAccountInfoBySTokenAsync(stoken, mid, cancellationToken);
            }
            catch (miHoYoApiException ex)
            {
                // SToken 本身失效时不能继续静默恢复，调用方应保留原登录失效提示。
                _logger.LogInformation(ex, "GameRecord cookie refresh rejected because the long-term token is no longer valid.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(ltoken) || string.IsNullOrWhiteSpace(cookieAccount.CookieToken))
            {
                _logger.LogWarning("GameRecord cookie refresh returned incomplete data.");
                return null;
            }

            string newCookie = MergeTokens(currentCookies, ltoken, cookieAccount);
            PersistCookie(currentCookie, newCookie);
            _refreshedCookies[cookie] = newCookie;
            if (role is not null)
            {
                role.Cookie = newCookie;
            }

            string accountUid = cookieAccount.Uid ?? GetFirstValue(currentCookies, "account_id", "account_id_v2", "stuid", "ltuid", "ltuid_v2");
            _logger.LogInformation("GameRecord cookie refreshed for account {uid}.", string.IsNullOrWhiteSpace(accountUid) ? "(unknown)" : accountUid);
            return newCookie;
        }
        finally
        {
            semaphore.Release();
        }
    }


    /// <summary>
    /// 将 AppConfig 中的国服设备标识同步到 passport 客户端（换票请求需要 device_id / device_fp）。
    /// </summary>
    private void SyncPassportDevice()
    {
        if (!string.IsNullOrWhiteSpace(AppConfig.HyperionDeviceId))
        {
            _passportClient.DeviceId = AppConfig.HyperionDeviceId;
        }
        if (!string.IsNullOrWhiteSpace(AppConfig.HyperionDeviceFp))
        {
            _passportClient.DeviceFp = AppConfig.HyperionDeviceFp;
        }
    }


    /// <summary>
    /// 查找账号当前已持久化的 Cookie，用于识别其他并发请求已经完成的刷新。
    /// </summary>
    /// <param name="role">按角色主键查询当前 Cookie 的国服角色。</param>
    /// <returns>数据库中的当前 Cookie；找不到账号时返回 null。</returns>
    private static string? GetCurrentCookie(GameRecordRole role)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<string>("SELECT Cookie FROM GameRecordRole WHERE Uid = @Uid AND GameBiz = @GameBiz LIMIT 1;", role);
    }


    /// <summary>
    /// 在同一事务中替换账号及其全部角色保存的旧 Cookie。
    /// </summary>
    /// <param name="oldCookie">数据库中待替换的旧 Cookie。</param>
    /// <param name="newCookie">包含新 Token 的完整 Cookie。</param>
    private static void PersistCookie(string oldCookie, string newCookie)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var transaction = dapper.BeginTransaction();
        dapper.Execute("UPDATE GameRecordUser SET Cookie = @newCookie WHERE Cookie = @oldCookie AND IsHoyolab = 0;", new { oldCookie, newCookie }, transaction);
        dapper.Execute("UPDATE GameRecordRole SET Cookie = @newCookie WHERE Cookie = @oldCookie AND GameBiz LIKE '%_cn';", new { oldCookie, newCookie }, transaction);
        transaction.Commit();
    }


    /// <summary>
    /// 解析 Cookie 字符串；重复键以后出现的值为准，值中的等号保持不变。
    /// </summary>
    /// <param name="cookie">分号分隔的 Cookie 字符串。</param>
    /// <returns>不区分键名大小写的键值集合。</returns>
    internal static Dictionary<string, string> ParseCookie(string cookie)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string segment in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int index = segment.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            string key = segment[..index].Trim();
            if (key.Length == 0)
            {
                continue;
            }
            values[key] = segment[(index + 1)..].Trim();
        }
        return values;
    }


    /// <summary>
    /// 从 Cookie 中提取 Token 刷新所需的 SToken 与 MiHoYo ID。
    /// </summary>
    /// <param name="cookies">已解析的 Cookie。</param>
    /// <param name="stoken">返回 stoken_v2 或兼容的旧 stoken。</param>
    /// <param name="mid">返回 mid 或兼容的 V2 MiHoYo ID。</param>
    /// <returns>两个凭据均存在时返回 true。</returns>
    private static bool TryGetRefreshCredentials(Dictionary<string, string> cookies, out string stoken, out string mid)
    {
        stoken = GetFirstValue(cookies, "stoken_v2", "stoken");
        mid = GetFirstValue(cookies, "mid", "account_mid_v2", "ltmid_v2");
        return !string.IsNullOrWhiteSpace(stoken) && !string.IsNullOrWhiteSpace(mid);
    }


    /// <summary>
    /// 获取用于账号级并发去重的稳定标识。
    /// </summary>
    /// <param name="cookies">已解析的 Cookie。</param>
    /// <param name="mid">已提取的 MiHoYo ID；可为空。</param>
    /// <returns>优先为 mid，其次为账号 UID；都缺失时使用空字符串。</returns>
    private static string GetAccountKey(Dictionary<string, string> cookies, string mid)
    {
        string resolvedMid = string.IsNullOrWhiteSpace(mid) ? GetFirstValue(cookies, "mid", "account_mid_v2", "ltmid_v2") : mid;
        if (!string.IsNullOrWhiteSpace(resolvedMid))
        {
            return $"mid:{resolvedMid}";
        }

        string accountId = GetFirstValue(cookies, "account_id", "account_id_v2", "stuid", "ltuid", "ltuid_v2");
        return string.IsNullOrWhiteSpace(accountId) ? "" : $"uid:{accountId}";
    }


    /// <summary>
    /// 按优先级读取第一个非空 Cookie 值。
    /// </summary>
    /// <param name="cookies">已解析的 Cookie。</param>
    /// <param name="keys">候选键名，按优先级排序。</param>
    /// <returns>首个非空值；不存在时返回空字符串。</returns>
    private static string GetFirstValue(Dictionary<string, string> cookies, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (cookies.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return "";
    }


    /// <summary>
    /// 将新的 ltoken / cookie_token 与账号 UID 合并到原 Cookie，保留 stoken 和其他无关键。
    /// </summary>
    /// <param name="cookies">原 Cookie 键值集合（会被就地修改）。</param>
    /// <param name="ltoken">stoken 换得的 ltoken。</param>
    /// <param name="cookieAccount">stoken 换得的 cookie_token 与 uid。</param>
    /// <returns>可直接用于请求头和数据库保存的完整 Cookie。</returns>
    internal static string MergeTokens(Dictionary<string, string> cookies, string ltoken, CookieTokenBySTokenResult cookieAccount)
    {
        // 与 MihoyoPassportClient.BuildCookieString 保持同一套 v1/v2 键名
        cookies["ltoken"] = ltoken;
        cookies["ltoken_v2"] = ltoken;
        cookies["cookie_token"] = cookieAccount.CookieToken;
        cookies["cookie_token_v2"] = cookieAccount.CookieToken;

        if (!string.IsNullOrWhiteSpace(cookieAccount.Uid))
        {
            string uid = cookieAccount.Uid;
            cookies["account_id"] = uid;
            cookies["account_id_v2"] = uid;
            cookies["ltuid"] = uid;
            cookies["ltuid_v2"] = uid;
            cookies["stuid"] = uid;
            cookies["login_uid"] = uid;
        }

        return string.Join("; ", cookies.Select(static x => $"{x.Key}={x.Value}"));
    }

}
