using Starward.Core.Localization;

namespace Starward.Core;

public class miHoYoApiException : Exception
{

    public int ReturnCode { get; init; }


    /// <summary>
    /// 创建米哈游 API 异常。
    /// </summary>
    /// <param name="returnCode">接口 retcode。</param>
    /// <param name="message">接口返回的原始文案；-100 / -101 时会被本地化文案覆盖。</param>
    public miHoYoApiException(int returnCode, string? message) : base(FormatMessage(returnCode, message))
    {
        ReturnCode = returnCode;
    }


    /// <summary>
    /// 格式化用户可见的异常消息。
    /// -100 / -101（未登录或登录态失效）使用 CoreLang 引导重新登录；其余为「原文案 (retcode)」。
    /// </summary>
    /// <param name="returnCode">接口 retcode。</param>
    /// <param name="message">接口原始文案，可空。</param>
    /// <returns>写入 <see cref="Exception.Message"/> 的最终字符串。</returns>
    private static string FormatMessage(int returnCode, string? message)
    {
        // Cookie 未登录 / 登录态失效：统一本地化提示（不硬编码）
        if (returnCode is -100 or -101)
        {
            return string.Format(CoreLang.miHoYoApi_PleaseReloginInMiyousheToolbox, returnCode);
        }
        return $"{message} ({returnCode})";
    }

}
