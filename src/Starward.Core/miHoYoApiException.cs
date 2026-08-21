using Starward.Core.GameRecord.Passport;

namespace Starward.Core;

public class miHoYoApiException : Exception
{

    public int ReturnCode { get; init; }


    /// <summary>
    /// 获取接口响应中的原始消息；可为 null。
    /// </summary>
    /// <remarks>
    /// UI 层使用该值在未知错误时保留服务端诊断信息，避免从 <see cref="Exception.Message"/> 反向解析状态码。
    /// </remarks>
    public string? ResponseMessage { get; }


    /// <summary>
    /// 响应头 <c>x-rpc-aigis</c> 解析结果。10035 / 10041 等风控码出现时，调用方应完成极验后重试。
    /// </summary>
    public CaptchaAigis? Aigis { get; }


    /// <summary>
    /// 获取当前返回码是否表示米游社登录态失效或缺少 Cookie。
    /// </summary>
    /// <remarks>
    /// 此属性只提供调用方恢复登录态的程序化判断，不包含任何用户可见文案。
    /// </remarks>
    public bool IsLoginExpired => ReturnCode is -100 or -111 or 10001;


    /// <summary>
    /// 创建米哈游 API 异常。
    /// </summary>
    /// <param name="returnCode">接口 retcode。</param>
    /// <param name="message">接口返回的原始文案；可为 null。</param>
    /// <param name="aigis">响应头中的极验载荷；没有则为 null。</param>
    public miHoYoApiException(int returnCode, string? message, CaptchaAigis? aigis = null) : base($"{message} ({returnCode})")
    {
        ReturnCode = returnCode;
        ResponseMessage = message;
        Aigis = aigis;
    }

}
