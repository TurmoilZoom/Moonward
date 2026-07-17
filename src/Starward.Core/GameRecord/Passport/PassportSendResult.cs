namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// passport 接口发送结果：不因业务 retcode 自动抛错，以便调用方处理 aigis 风控。
/// </summary>
/// <typeparam name="T">成功时 data 的类型。</typeparam>
public sealed class PassportSendResult<T> where T : class
{

    /// <summary>接口 retcode；0 表示成功。</summary>
    public int Retcode { get; init; }


    /// <summary>接口 message。</summary>
    public string Message { get; init; } = string.Empty;


    /// <summary>成功时的 data；失败时可能为 null。</summary>
    public T? Data { get; init; }


    /// <summary>
    /// 响应头 <c>x-rpc-aigis</c> 解析结果；出现时调用方应完成极验后重试。
    /// </summary>
    public CaptchaAigis? Aigis { get; init; }


    /// <summary>是否业务成功（retcode == 0 且 Data 非 null）。</summary>
    public bool IsSuccess => Retcode == 0 && Data is not null;

}
