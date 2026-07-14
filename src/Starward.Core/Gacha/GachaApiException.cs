namespace Starward.Core.Gacha;

/// <summary>
/// 表示通过 authkey 调用抽卡记录接口时返回的 API 错误。
/// </summary>
public class GachaApiException : Exception
{

    /// <summary>
    /// 获取抽卡记录接口返回的 retcode；未指定时为 0。
    /// </summary>
    public int ReturnCode { get; init; }


    /// <summary>
    /// 获取抽卡记录接口响应中的原始消息；可为 null。
    /// </summary>
    public string? ResponseMessage { get; }


    /// <summary>
    /// 获取当前错误是否表示抽卡记录 URL 中的 authkey 已失效。
    /// 原神和星穹铁道使用 -101，绝区零使用 -1。
    /// </summary>
    public bool IsAuthkeyExpired => ReturnCode is -101 or -1;


    /// <summary>
    /// 创建不含接口状态码的抽卡 API 异常。
    /// </summary>
    public GachaApiException()
    {
    }


    /// <summary>
    /// 创建不含接口状态码、带指定消息的抽卡 API 异常。
    /// </summary>
    /// <param name="message">描述异常原因的消息；可为 null。</param>
    public GachaApiException(string? message) : base(message)
    {
    }


    /// <summary>
    /// 创建不含接口状态码、带指定消息和内部异常的抽卡 API 异常。
    /// </summary>
    /// <param name="message">描述异常原因的消息；可为 null。</param>
    /// <param name="innerException">导致当前异常的内部异常；可为 null。</param>
    public GachaApiException(string? message, Exception? innerException) : base(message, innerException)
    {
    }


    /// <summary>
    /// 创建包含抽卡记录接口状态码的异常。
    /// </summary>
    /// <param name="returnCode">接口返回的 retcode。</param>
    /// <param name="message">接口返回的原始消息；可为 null。</param>
    public GachaApiException(int returnCode, string? message) : base(FormatMessage(returnCode, message))
    {
        ReturnCode = returnCode;
        ResponseMessage = message;
    }


    /// <summary>
    /// 将接口原始消息和状态码组合为异常消息，不按米游社 Cookie 状态解释状态码。
    /// </summary>
    /// <param name="returnCode">接口返回的 retcode。</param>
    /// <param name="message">接口返回的原始消息；可为 null。</param>
    /// <returns>写入 <see cref="Exception.Message"/> 的异常消息。</returns>
    private static string FormatMessage(int returnCode, string? message)
    {
        return $"{message} ({returnCode})";
    }

}
