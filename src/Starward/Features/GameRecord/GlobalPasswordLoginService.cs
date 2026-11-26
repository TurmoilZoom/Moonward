using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core;
using Starward.Core.GameRecord.Passport;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord;

/// <summary>
/// 国际服（HoYoverse）账号密码登录编排：登录 → 按风控要求做极验或身份验证后重试 → stoken 换票拼 Cookie。
/// </summary>
internal class GlobalPasswordLoginService
{

    /// <summary>
    /// 登录返回 <c>x-rpc-aigis</c> 时，由 UI 完成极验并返回 <c>x-rpc-aigis</c> 头值；取消或失败返回 null。
    /// </summary>
    public delegate Task<string?> ResolveAigisAsync(CaptchaAigis aigis, CancellationToken cancellationToken);

    /// <summary>
    /// <c>x-rpc-verify</c> 要求人机验证时，由 UI 完成极验并返回 <c>getValidate()</c> 结果 JSON；取消或失败返回 null。
    /// </summary>
    public delegate Task<string?> ResolveGeetestAsync(string geetestConfigJson, CancellationToken cancellationToken);

    /// <summary>
    /// <c>x-rpc-verify</c> 要求身份验证时，由 UI 打开账号中心安全验证页；验证成功返回 true。
    /// </summary>
    public delegate Task<bool> ResolveIdentityAsync(string actionTicket, CancellationToken cancellationToken);


    /// <summary>与 <see cref="HoyoversePassportClient"/> 的请求头一致，身份验证页要同一组 app_id / game_biz。</summary>
    public const string PassportAppId = "ddxf6vlr1reo";

    /// <summary>同 <see cref="PassportAppId"/>。</summary>
    public const string PassportGameBiz = "hyp_global";

    /// <summary>米哈游 SDK 记录设备标识的位置（官方启动器 / 游戏写入）。只读，不改它。</summary>
    private const string MiHoYoSdkKey = @"HKEY_CURRENT_USER\Software\miHoYoSDK";

    /// <summary>风控可能连续出现（先极验再身份验证），限制总轮数避免死循环。</summary>
    private const int MaxLoginAttempts = 4;


    private readonly ILogger<GlobalPasswordLoginService> _logger;

    private readonly HoyoversePassportClient _passportClient;


    /// <summary>
    /// 初始化国际服密码登录服务。
    /// </summary>
    /// <param name="logger">日志，不得记录账号、密码或 token。</param>
    /// <param name="passportClient">国际服 passport 客户端。</param>
    public GlobalPasswordLoginService(ILogger<GlobalPasswordLoginService> logger, HoyoversePassportClient passportClient)
    {
        _logger = logger;
        _passportClient = passportClient;
    }


    /// <summary>
    /// 账号密码登录，换齐 ltoken / cookie_token 后返回 Cookie 字符串（含 stoken，供之后静默刷新）。
    /// </summary>
    /// <param name="account">邮箱或用户名。</param>
    /// <param name="password">密码。</param>
    /// <param name="resolveAigis">极验回调（<c>x-rpc-aigis</c>）。</param>
    /// <param name="resolveGeetest">极验回调（<c>x-rpc-verify</c> 的人机验证）。</param>
    /// <param name="resolveIdentity">身份验证回调（<c>x-rpc-verify</c> 的新设备等）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可供 <see cref="GameRecordService.AddRecordUserAsync"/> 使用的 Cookie 串。</returns>
    /// <exception cref="ArgumentException">账号或密码为空。</exception>
    /// <exception cref="miHoYoApiException">登录或换票业务失败。</exception>
    /// <exception cref="OperationCanceledException">用户取消验证或令牌取消。</exception>
    public async Task<string> LoginAsync(
        string account,
        string password,
        ResolveAigisAsync resolveAigis,
        ResolveGeetestAsync resolveGeetest,
        ResolveIdentityAsync resolveIdentity,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            throw new ArgumentException("Account is required.", nameof(account));
        }
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }
        ArgumentNullException.ThrowIfNull(resolveAigis);
        ArgumentNullException.ThrowIfNull(resolveGeetest);
        ArgumentNullException.ThrowIfNull(resolveIdentity);
        PrepareClient();

        string? aigisHeader = null;
        string? riskTicket = null;
        HoyoverseLoginResult? login = null;
        for (int attempt = 0; attempt < MaxLoginAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _passportClient.LoginByPasswordAsync(account, password, aigisHeader, riskTicket, cancellationToken);
            if (result.IsSuccess && result.Data is not null)
            {
                login = result.Data;
                break;
            }

            if (result.Verify is not null)
            {
                riskTicket = await ResolveRiskVerifyAsync(result.Verify, resolveGeetest, resolveIdentity, attempt, cancellationToken);
                // 账号 SDK：带 risk_ticket 重发时去掉 aigis 头
                aigisHeader = null;
                continue;
            }

            if (result.Aigis is not null && !string.IsNullOrWhiteSpace(result.Aigis.Data))
            {
                _logger.LogInformation("Global password login requires aigis (attempt {attempt}).", attempt + 1);
                aigisHeader = await resolveAigis(result.Aigis, cancellationToken);
                if (string.IsNullOrWhiteSpace(aigisHeader))
                {
                    throw new OperationCanceledException("Geetest verification was cancelled.");
                }
                continue;
            }

            _logger.LogWarning("Global password login failed: {retcode} {message}", result.Retcode, result.Message);
            throw new miHoYoApiException(result.Retcode, result.Message);
        }

        if (login is null)
        {
            throw new miHoYoApiException(-1, "Risk verification failed too many times.");
        }

        string? stoken = login.GetSToken();
        string? aid = login.UserInfo?.Aid;
        string? mid = login.UserInfo?.Mid;
        if (string.IsNullOrWhiteSpace(stoken) || string.IsNullOrWhiteSpace(aid) || string.IsNullOrWhiteSpace(mid))
        {
            throw new miHoYoApiException(-1, "Login response is incomplete.");
        }

        _logger.LogInformation("Global password login succeeded for aid {aid}, exchanging tokens.", aid);
        try
        {
            var (ltoken, cookieToken) = await ExchangeTokensAsync(stoken, mid, aid, cancellationToken);
            // 与国服验证码登录同一套 v1/v2 键名；国际服战绩接口认 ltoken_v2 / ltuid_v2 / ltmid_v2
            return MihoyoPassportClient.BuildCookieString(aid, mid, stoken, ltoken, cookieToken);
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogWarning("Global password login token exchange failed: retcode {retcode}, message {message}.", ex.ReturnCode, ex.ResponseMessage);
            throw;
        }
    }


    /// <summary>
    /// 用国际服 stoken 换 ltoken_v2 与 cookie_token_v2（登录与 Cookie 静默刷新共用）。
    /// </summary>
    /// <param name="stoken">密码登录得到的 stoken。</param>
    /// <param name="mid">账号 mid。</param>
    /// <param name="aid">账号 aid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>ltoken 与 cookie_token。</returns>
    /// <exception cref="miHoYoApiException">换票失败或响应缺字段。</exception>
    internal async Task<(string LToken, string CookieToken)> ExchangeTokensAsync(string stoken, string mid, string aid, CancellationToken cancellationToken = default)
    {
        PrepareClient();
        var tokens = await _passportClient.GetTokensBySTokenAsync(stoken, mid, aid, cancellationToken);
        string? ltoken = tokens.GetToken(HoyoversePassportClient.TokenTypeLToken);
        string? cookieToken = tokens.GetToken(HoyoversePassportClient.TokenTypeCookieToken);
        if (string.IsNullOrWhiteSpace(ltoken) || string.IsNullOrWhiteSpace(cookieToken))
        {
            throw new miHoYoApiException(-1, "Exchange response is incomplete.");
        }
        return (ltoken, cookieToken);
    }


    /// <summary>
    /// 按 <c>x-rpc-verify</c> 完成人机或身份验证，返回重发登录用的 risk_ticket。
    /// </summary>
    /// <param name="verify">响应头解析结果。</param>
    /// <param name="resolveGeetest">极验回调。</param>
    /// <param name="resolveIdentity">身份验证回调。</param>
    /// <param name="attempt">当前轮次（日志用）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>risk_ticket。</returns>
    /// <exception cref="OperationCanceledException">用户取消验证。</exception>
    /// <exception cref="miHoYoApiException">验证类型未知或验证接口失败。</exception>
    private async Task<string> ResolveRiskVerifyAsync(HoyoverseRiskVerify verify, ResolveGeetestAsync resolveGeetest, ResolveIdentityAsync resolveIdentity, int attempt, CancellationToken cancellationToken)
    {
        HoyoverseRiskVerifyDetail? detail = verify.GetDetail();
        if (string.IsNullOrWhiteSpace(verify.RiskTicket) || string.IsNullOrWhiteSpace(detail?.Ticket))
        {
            throw new miHoYoApiException(-1, "Risk verification response is incomplete.");
        }

        _logger.LogInformation("Global password login requires risk verification type {type} (attempt {attempt}).", detail.VerifyType, attempt + 1);
        switch ((HoyoverseRiskVerifyType)detail.VerifyType)
        {
            case HoyoverseRiskVerifyType.Geetest:
                var captcha = await _passportClient.CreateBySmartCaptchaTicketAsync(detail.Ticket, cancellationToken);
                if (captcha.MmtType == 1)
                {
                    string? validate = await resolveGeetest(captcha.Data ?? "", cancellationToken);
                    if (string.IsNullOrWhiteSpace(validate))
                    {
                        throw new OperationCanceledException("Geetest verification was cancelled.");
                    }
                    await _passportClient.CheckSmartCaptchaAsync(detail.Ticket, validate, cancellationToken);
                }
                else if (captcha.MmtType != 0)
                {
                    // 0 = 无需验证；其他值账号 SDK 也当作未知错误
                    throw new miHoYoApiException(-1, $"Unknown smart captcha type {captcha.MmtType}.");
                }
                break;
            case HoyoverseRiskVerifyType.Identity:
                if (!await resolveIdentity(detail.Ticket, cancellationToken))
                {
                    throw new OperationCanceledException("Identity verification was cancelled.");
                }
                break;
            default:
                throw new miHoYoApiException(-1, $"Unknown risk verification type {detail.VerifyType}.");
        }
        return verify.RiskTicket;
    }


    /// <summary>
    /// 设置客户端的设备标识与语言。
    /// </summary>
    private void PrepareClient()
    {
        // 优先沿用官方启动器 / 游戏在本机写下的设备标识：同一台电脑上的登录更不容易被当成新设备
        string? sdkDeviceId = ReadMiHoYoSdkValue("MIHOYOSDK_DEVICE_ID");
        if (!string.IsNullOrWhiteSpace(sdkDeviceId))
        {
            _passportClient.DeviceId = sdkDeviceId;
            _passportClient.DeviceFp = ReadMiHoYoSdkValue("MIHOYOSDK_DEVICE_FP");
        }
        else
        {
            string? deviceId = AppConfig.HoyoversePassportDeviceId;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Guid.NewGuid().ToString("D");
                AppConfig.HoyoversePassportDeviceId = deviceId;
            }
            _passportClient.DeviceId = deviceId;
            _passportClient.DeviceFp = null;
        }
        _passportClient.Language = CultureInfo.CurrentUICulture.Name;
    }


    /// <summary>
    /// 读取米哈游 SDK 注册表值。
    /// </summary>
    /// <param name="name">值名。</param>
    /// <returns>值；不存在或读取失败时为 null。</returns>
    private static string? ReadMiHoYoSdkValue(string name)
    {
        try
        {
            return Registry.GetValue(MiHoYoSdkKey, name, null) as string;
        }
        catch
        {
            return null;
        }
    }

}
