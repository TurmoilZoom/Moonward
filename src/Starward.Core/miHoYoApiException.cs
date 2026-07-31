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
    /// 获取当前返回码是否表示米游社登录态失效或缺少 Cookie。
    /// </summary>
    /// <remarks>
    /// 此属性只提供调用方恢复登录态的程序化判断，不包含任何用户可见文案。
    /// </remarks>
    public bool IsLoginExpired => ReturnCode is -100 or -111 or 10001;


    /// <summary>
    /// 获取当前返回码是否表示账号/设备/网络需要验证或触发战绩侧风控（如 10041）。
    /// </summary>
    /// <remarks>
    /// 与 UI「校验账号」映射一致；国服请求恢复层可在此类错误下尝试 stoken 换票后重试。
    /// 此属性不包含任何用户可见文案。
    /// </remarks>
    public bool IsVerificationRequired => ReturnCode is -3503 or 1034 or 5003 or 10035 or 10041;


    /// <summary>
    /// 创建米哈游 API 异常。
    /// </summary>
    /// <param name="returnCode">接口 retcode。</param>
    /// <param name="message">接口返回的原始文案；可为 null。</param>
    public miHoYoApiException(int returnCode, string? message) : base($"{message} ({returnCode})")
    {
        ReturnCode = returnCode;
        ResponseMessage = message;
    }

}
