using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;

namespace Starward.Features;

/// <summary>
/// 标识米哈游 API 请求的鉴权与业务场景，避免将相同 retcode 跨接口误解。
/// </summary>
internal enum MiHoYoApiContext
{
    /// <summary>米游社或 HoYoLAB 战绩接口。</summary>
    GameRecord,
    /// <summary>每日签到与补签接口。</summary>
    SignIn,
    /// <summary>通过游戏 authkey 拉取祈愿记录的接口。</summary>
    GachaLog,
    /// <summary>自助查询链接接口。</summary>
    SelfQuery,
    /// <summary>游戏账号认证接口。</summary>
    AccountAuth,
    /// <summary>不需要玩家凭据的启动器公开接口。</summary>
    LauncherPublicApi,
    /// <summary>国服 passport 短信验证码登录（createLoginCaptcha / loginByMobileCaptcha / 换票）。</summary>
    PassportCaptcha,
}

/// <summary>
/// 标识错误提示可复用的站内恢复操作。
/// </summary>
internal enum MiHoYoApiRecoveryAction
{
    /// <summary>没有可执行的恢复操作。</summary>
    None,
    /// <summary>重新登录米游社或 HoYoLAB。</summary>
    Relogin,
    /// <summary>打开账号验证入口。</summary>
    VerifyAccount,
    /// <summary>重新输入或获取 authkey URL。</summary>
    RefreshUrl,
}

/// <summary>
/// 表示已分类的米哈游 API 错误提示。
/// </summary>
/// <param name="Severity">InfoBar 显示严重级别。</param>
/// <param name="Title">本地化标题。</param>
/// <param name="Message">本地化消息，包含需要展示的状态码。</param>
/// <param name="RecoveryAction">可选的站内恢复操作。</param>
internal sealed record MiHoYoApiErrorFeedback(InfoBarSeverity Severity, string Title, string Message, MiHoYoApiRecoveryAction RecoveryAction);

/// <summary>
/// 将米哈游 JSON retcode 和 HTTP 状态转换为统一、可本地化的 UI 反馈。
/// </summary>
internal static class MiHoYoApiErrorFeedbackFactory
{
    /// <summary>
    /// 根据异常和请求场景创建用户可见反馈。
    /// </summary>
    /// <param name="exception">请求失败时捕获的异常。</param>
    /// <param name="context">请求的鉴权和业务场景。</param>
    /// <returns>包含本地化文案、状态码和恢复动作的反馈。</returns>
    public static MiHoYoApiErrorFeedback Create(Exception exception, MiHoYoApiContext context)
    {
        // 验证码登录：取消极验 / 客户端参数校验也统一由此产出反馈
        if (context is MiHoYoApiContext.PassportCaptcha)
        {
            if (exception is OperationCanceledException)
            {
                return CreatePlainFeedback(InfoBarSeverity.Warning, GetResource("CaptchaLogin_GeetestCancelled"));
            }

            if (exception is ArgumentException argumentException)
            {
                var captchaValidation = CreatePassportCaptchaValidationFeedback(argumentException);
                if (captchaValidation is not null)
                {
                    return captchaValidation;
                }
            }
        }

        if (exception is GachaApiException gachaException)
        {
            return CreateGachaFeedback(gachaException);
        }

        if (exception is miHoYoApiException apiException)
        {
            return CreateApiFeedback(apiException, context);
        }

        if (exception is HttpRequestException httpException)
        {
            return CreateHttpFeedback(httpException, context);
        }

        return CreateUnknownFeedback(exception.Message, null);
    }

    /// <summary>
    /// 创建反馈并立即以主窗口 Toast 展示（业务层统一出口）。
    /// </summary>
    /// <param name="exception">捕获的异常。</param>
    /// <param name="context">业务场景。</param>
    /// <param name="onRecovery">可选恢复动作回调。</param>
    public static void Show(Exception exception, MiHoYoApiContext context, Action<MiHoYoApiRecoveryAction>? onRecovery = null)
    {
        Show(Create(exception, context), onRecovery);
    }

    /// <summary>
    /// 将反馈显示为主窗口 InAppToast，并在调用方提供时执行恢复动作。
    /// </summary>
    /// <param name="feedback">已分类的错误反馈。</param>
    /// <param name="onRecovery">用户点击恢复按钮时接收恢复类型的回调；可为 null。</param>
    public static void Show(MiHoYoApiErrorFeedback feedback, Action<MiHoYoApiRecoveryAction>? onRecovery = null)
    {
        var toast = InAppToast.MainWindow;
        if (toast is null)
        {
            return;
        }

        if (feedback.RecoveryAction is not MiHoYoApiRecoveryAction.None && onRecovery is not null)
        {
            toast.ShowWithButton(feedback.Severity, feedback.Title, feedback.Message, GetRecoveryButtonText(feedback.RecoveryAction), () => onRecovery(feedback.RecoveryAction));
            return;
        }

        switch (feedback.Severity)
        {
            case InfoBarSeverity.Error:
                toast.Error(feedback.Title, feedback.Message);
                break;
            case InfoBarSeverity.Informational:
                toast.Information(feedback.Title, feedback.Message);
                break;
            default:
                toast.Warning(feedback.Title, feedback.Message);
                break;
        }
    }

    /// <summary>
    /// 将各场景 retcode 映射为本地化反馈。
    /// </summary>
    /// <param name="exception">接口返回的业务异常。</param>
    /// <param name="context">调用接口的场景。</param>
    /// <returns>对应的错误反馈。</returns>
    private static MiHoYoApiErrorFeedback CreateApiFeedback(miHoYoApiException exception, MiHoYoApiContext context)
    {
        if (context is MiHoYoApiContext.PassportCaptcha)
        {
            return CreatePassportCaptchaApiFeedback(exception);
        }

        if (context is MiHoYoApiContext.GachaLog or MiHoYoApiContext.SelfQuery && exception.ReturnCode is -100 or -101 or -1)
        {
            return CreateKnownFeedback("MiHoYoApiError_AuthkeyExpired", exception.ReturnCode, MiHoYoApiRecoveryAction.RefreshUrl);
        }

        if (context is MiHoYoApiContext.GameRecord or MiHoYoApiContext.SignIn or MiHoYoApiContext.AccountAuth)
        {
            // 风控验证码集合与 miHoYoApiException.IsVerificationRequired 保持一致（国服恢复层会据此换票）。
            return exception.ReturnCode switch
            {
                -3 or -100 or -111 or 10001 or 1004 => CreateKnownFeedback("MiHoYoApiError_LoginExpired", exception.ReturnCode, MiHoYoApiRecoveryAction.Relogin),
                _ when exception.IsVerificationRequired => CreateKnownFeedback("MiHoYoApiError_VerificationRequired", exception.ReturnCode, MiHoYoApiRecoveryAction.VerifyAccount),
                -110 or 1028 or -500004 => CreateKnownFeedback("MiHoYoApiError_TooManyRequests", exception.ReturnCode),
                1008 or 1009 or -10002 => CreateKnownFeedback("MiHoYoApiError_GameRoleNotFound", exception.ReturnCode),
                10101 => CreateKnownFeedback("MiHoYoApiError_AccountQueryLimit", exception.ReturnCode),
                10102 => CreateKnownFeedback("MiHoYoApiError_DataNotPublic", exception.ReturnCode),
                10103 => CreateKnownFeedback("MiHoYoApiError_AccountServiceUnavailable", exception.ReturnCode),
                10104 => CreateKnownFeedback("MiHoYoApiError_RealTimeNotesUnavailable", exception.ReturnCode),
                -10001 or -101 or -502 or 1000 or 1002 or 10307 or -1 => CreateKnownFeedback("MiHoYoApiError_RequestRejected", exception.ReturnCode),
                _ => CreateUnknownFeedback(exception.ResponseMessage, exception.ReturnCode),
            };
        }

        return CreateUnknownFeedback(exception.ResponseMessage, exception.ReturnCode);
    }

    /// <summary>
    /// passport 短信验证码登录 retcode 映射（与战绩 retcode 语义分离）。
    /// </summary>
    /// <param name="exception">passport / 换票接口异常。</param>
    /// <returns>验证码登录场景的反馈。</returns>
    private static MiHoYoApiErrorFeedback CreatePassportCaptchaApiFeedback(miHoYoApiException exception)
    {
        // retcode 语义参考米哈游 passport 与社区文档；未知码保留服务端 message
        return exception.ReturnCode switch
        {
            // 验证码错误 / 无效
            -107 or -3202 or -3203 or -3206 => CreateCaptchaKnownFeedback("CaptchaLogin_Error_InvalidCode", exception.ReturnCode),
            // 验证码过期
            -3205 => CreateCaptchaKnownFeedback("CaptchaLogin_Error_CodeExpired", exception.ReturnCode),
            // 发送或校验过于频繁
            -110 or -3207 or -3208 or -500004 => CreateCaptchaKnownFeedback("CaptchaLogin_Error_TooFrequent", exception.ReturnCode),
            // 手机号未绑定 / 无效 / 未注册
            -3209 or -3210 or -3201 => CreateCaptchaKnownFeedback("CaptchaLogin_Error_PhoneInvalid", exception.ReturnCode),
            // 风控（aigis 已由 UI 处理；此处为剩余风控失败）
            -3101 or -3235 or 1034 or -3503 => CreateCaptchaKnownFeedback("CaptchaLogin_Error_RiskControl", exception.ReturnCode),
            // 换票 / 会话失败
            -100 or -111 or 10001 => CreateCaptchaKnownFeedback("CaptchaLogin_Error_SessionFailed", exception.ReturnCode),
            _ => CreateCaptchaUnknownFeedback(exception.ResponseMessage, exception.ReturnCode),
        };
    }

    /// <summary>
    /// 验证码登录客户端参数校验（抛 <see cref="ArgumentException"/> 时）。
    /// </summary>
    /// <param name="exception">参数异常。</param>
    /// <returns>可展示的反馈；无法识别时为 null，由上层走通用分支。</returns>
    private static MiHoYoApiErrorFeedback? CreatePassportCaptchaValidationFeedback(ArgumentException exception)
    {
        if (exception.ParamName is "phone"
            || ContainsIgnoreCase(exception.Message, "mobile")
            || ContainsIgnoreCase(exception.Message, "phone"))
        {
            return CreatePlainFeedback(InfoBarSeverity.Warning, GetResource("CaptchaLogin_InvalidPhone"));
        }

        if (exception.ParamName is "captcha")
        {
            return CreatePlainFeedback(InfoBarSeverity.Warning, GetResource("CaptchaLogin_CodeRequired"));
        }

        if (exception.ParamName is "actionType")
        {
            return CreatePlainFeedback(InfoBarSeverity.Warning, GetResource("CaptchaLogin_SendCodeFirst"));
        }

        return null;
    }

    /// <summary>
    /// 将祈愿记录 authkey 返回码映射为失效或未知错误反馈。
    /// </summary>
    /// <param name="exception">祈愿接口异常。</param>
    /// <returns>对应的错误反馈。</returns>
    private static MiHoYoApiErrorFeedback CreateGachaFeedback(GachaApiException exception)
    {
        if (exception.IsAuthkeyExpired || exception.ReturnCode is -100)
        {
            return CreateKnownFeedback("MiHoYoApiError_AuthkeyExpired", exception.ReturnCode, MiHoYoApiRecoveryAction.RefreshUrl);
        }

        if (exception.ReturnCode is -110 or 1028)
        {
            return CreateKnownFeedback("MiHoYoApiError_TooManyRequests", exception.ReturnCode);
        }

        return CreateUnknownFeedback(exception.ResponseMessage, exception.ReturnCode);
    }

    /// <summary>
    /// 将 HTTP 状态和网络异常映射为与请求场景匹配的反馈。
    /// </summary>
    /// <param name="exception">HTTP 请求异常。</param>
    /// <param name="context">调用接口的场景。</param>
    /// <returns>对应的错误反馈。</returns>
    private static MiHoYoApiErrorFeedback CreateHttpFeedback(HttpRequestException exception, MiHoYoApiContext context)
    {
        if (context is MiHoYoApiContext.PassportCaptcha)
        {
            // 验证码登录网络失败：固定标题 + 可带 HTTP 状态
            string message = exception.StatusCode is { } code
                ? string.Format(CultureInfo.CurrentCulture, GetResource("CaptchaLogin_Error_NetworkWithStatus"), (int)code)
                : GetResource("CaptchaLogin_Error_Network");
            return new MiHoYoApiErrorFeedback(InfoBarSeverity.Error, GetResource("CaptchaLogin_ErrorTitle"), message, MiHoYoApiRecoveryAction.None);
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized when context is MiHoYoApiContext.GameRecord or MiHoYoApiContext.SignIn or MiHoYoApiContext.AccountAuth
                => CreateKnownFeedback("MiHoYoApiError_LoginExpired", 401, MiHoYoApiRecoveryAction.Relogin),
            HttpStatusCode.Unauthorized when context is MiHoYoApiContext.GachaLog or MiHoYoApiContext.SelfQuery
                => CreateKnownFeedback("MiHoYoApiError_AuthkeyExpired", 401, MiHoYoApiRecoveryAction.RefreshUrl),
            HttpStatusCode.Forbidden => CreateKnownFeedback("MiHoYoApiError_AccessDenied", 403),
            HttpStatusCode.TooManyRequests => CreateKnownFeedback("MiHoYoApiError_TooManyRequests", 429),
            >= HttpStatusCode.InternalServerError and <= (HttpStatusCode)599 => CreateKnownFeedback("MiHoYoApiError_ServiceUnavailable", (int)exception.StatusCode.Value),
            _ => CreateKnownFeedback("MiHoYoApiError_NetworkRequestFailed", exception.StatusCode is null ? null : (int)exception.StatusCode.Value),
        };
    }

    /// <summary>
    /// 创建已确认语义的反馈，并只显示本地化文案和状态码。
    /// </summary>
    /// <param name="resourceKey">本地化资源键。</param>
    /// <param name="code">retcode 或 HTTP 状态码；可为 null。</param>
    /// <param name="recoveryAction">可选恢复动作。</param>
    /// <returns>已分类的反馈。</returns>
    private static MiHoYoApiErrorFeedback CreateKnownFeedback(string resourceKey, int? code, MiHoYoApiRecoveryAction recoveryAction = MiHoYoApiRecoveryAction.None)
    {
        string message = GetResource(resourceKey);
        if (code.HasValue)
        {
            message = string.Format(CultureInfo.CurrentCulture, "{0} ({1})", message, code.Value);
        }
        // 账号类错误不再使用「账号异常」类通用标题，仅展示具体本地化说明；网络类仍用 NetworkError 作标题。
        string title = resourceKey is "MiHoYoApiError_LoginExpired"
            or "MiHoYoApiError_VerificationRequired"
            or "MiHoYoApiError_GameRoleNotFound"
            or "MiHoYoApiError_AccountQueryLimit"
            or "MiHoYoApiError_DataNotPublic"
            or "MiHoYoApiError_AccountServiceUnavailable"
            or "MiHoYoApiError_RealTimeNotesUnavailable"
            or "MiHoYoApiError_AuthkeyExpired"
            or "MiHoYoApiError_AccessDenied"
            ? string.Empty
            : Lang.Common_NetworkError;
        return new MiHoYoApiErrorFeedback(InfoBarSeverity.Warning, title, message, recoveryAction);
    }

    /// <summary>
    /// 验证码登录已知业务错误：标题固定为「验证码登录失败」，正文为本地化说明 + retcode。
    /// </summary>
    private static MiHoYoApiErrorFeedback CreateCaptchaKnownFeedback(string resourceKey, int code)
    {
        string message = string.Format(CultureInfo.CurrentCulture, "{0} ({1})", GetResource(resourceKey), code);
        return new MiHoYoApiErrorFeedback(InfoBarSeverity.Error, GetResource("CaptchaLogin_ErrorTitle"), message, MiHoYoApiRecoveryAction.None);
    }

    /// <summary>
    /// 验证码登录未知 retcode：保留服务端文案便于排查。
    /// </summary>
    private static MiHoYoApiErrorFeedback CreateCaptchaUnknownFeedback(string? serverMessage, int code)
    {
        string message = string.IsNullOrWhiteSpace(serverMessage)
            ? string.Format(CultureInfo.CurrentCulture, GetResource("CaptchaLogin_Error_UnknownWithCode"), code)
            : string.Format(CultureInfo.CurrentCulture, GetResource("CaptchaLogin_Error_UnknownWithMessage"), code, serverMessage);
        return new MiHoYoApiErrorFeedback(InfoBarSeverity.Error, GetResource("CaptchaLogin_ErrorTitle"), message, MiHoYoApiRecoveryAction.None);
    }

    /// <summary>
    /// 仅消息、无标题的轻量反馈（校验失败、取消极验等）。
    /// </summary>
    private static MiHoYoApiErrorFeedback CreatePlainFeedback(InfoBarSeverity severity, string message)
    {
        return new MiHoYoApiErrorFeedback(severity, string.Empty, message, MiHoYoApiRecoveryAction.None);
    }

    /// <summary>
    /// 创建未知错误反馈，保留服务端原始消息和状态码以便排查。
    /// </summary>
    /// <param name="rawMessage">服务端或底层异常的原始消息；可为 null。</param>
    /// <param name="code">retcode 或 HTTP 状态码；可为 null。</param>
    /// <returns>未知错误反馈。</returns>
    private static MiHoYoApiErrorFeedback CreateUnknownFeedback(string? rawMessage, int? code)
    {
        string diagnostic = string.IsNullOrWhiteSpace(rawMessage) ? GetResource("MiHoYoApiError_NoDetails") : rawMessage;
        if (code.HasValue)
        {
            diagnostic = string.Format(CultureInfo.CurrentCulture, "{0} ({1})", diagnostic, code.Value);
        }
        return new MiHoYoApiErrorFeedback(InfoBarSeverity.Warning, string.Empty, string.Format(CultureInfo.CurrentCulture, GetResource("MiHoYoApiError_Unknown"), diagnostic), MiHoYoApiRecoveryAction.None);
    }

    /// <summary>
    /// 获取恢复操作对应的本地化按钮文案。
    /// </summary>
    /// <param name="action">恢复操作。</param>
    /// <returns>按钮显示文案。</returns>
    private static string GetRecoveryButtonText(MiHoYoApiRecoveryAction action)
    {
        return action switch
        {
            MiHoYoApiRecoveryAction.Relogin => GetResource("MiHoYoApiError_Relogin"),
            MiHoYoApiRecoveryAction.VerifyAccount => Lang.HoyolabToolboxPage_VerifyAccount,
            MiHoYoApiRecoveryAction.RefreshUrl => GetResource("MiHoYoApiError_RefreshUrl"),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// 从 Lang 资源中读取文案，使未翻译语言自动回退到默认资源。
    /// </summary>
    /// <param name="key">资源键。</param>
    /// <returns>本地化文案；资源缺失时返回键名以便开发阶段发现问题。</returns>
    private static string GetResource(string key)
    {
        return Lang.ResourceManager.GetString(key, Lang.Culture) ?? key;
    }

    private static bool ContainsIgnoreCase(string? text, string value)
    {
        return text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
    }
}
