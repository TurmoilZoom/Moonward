using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.LanSync;


/// <summary>
/// 局域网同步的传输约定：端口、帧格式与消息体。
/// </summary>
/// <remarks>
/// 协议版本与数据库 USER_VERSION 无关：快照是只含同步表的 SQLite 文件，合并时按列名取两边都有的列，
/// 两台设备的库版本不同也能同步。只有帧格式或消息语义改变时才提高 <see cref="Version"/>。
/// </remarks>
internal static class LanSyncProtocol
{

    /// <summary>协议版本，两端不一致时拒绝同步。</summary>
    public const int Version = 1;

    /// <summary>共享端优先监听的 TCP 端口；被占用时改用系统分配的端口，并在发现回复里告知对方。</summary>
    public const int DefaultServicePort = 47651;

    /// <summary>共享端监听的 UDP 发现端口，请求方向它广播。</summary>
    public const int DiscoveryPort = 47652;

    /// <summary>帧头与发现报文里的魔数，用来尽早拒绝不是 Moonward 的连接。</summary>
    public const string Magic = "MWLS";

    /// <summary>验证码位数。</summary>
    public const int CodeLength = 6;

    /// <summary>验证码最多错几次，达到后共享端停止本次共享。</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>取对方设备名与版本，不需要验证码。</summary>
    public const string OperationHello = "hello";

    /// <summary>凭验证码拉取快照。</summary>
    public const string OperationSnapshot = "snapshot";

    /// <summary>请求帧上限：请求只有几个短字段，超过即视为异常连接。</summary>
    public const int MaxRequestLength = 4 << 10;

    /// <summary>响应帧上限。</summary>
    public const int MaxResponseLength = 64 << 10;

    /// <summary>发现报文上限。</summary>
    public const int MaxDiscoveryLength = 2 << 10;

}



/// <summary>
/// 请求方发给共享端的请求。
/// </summary>
internal sealed class LanSyncRequest
{

    public int Protocol { get; set; }

    /// <summary><see cref="LanSyncProtocol.OperationHello"/> 或 <see cref="LanSyncProtocol.OperationSnapshot"/>。</summary>
    public string? Operation { get; set; }

    public string? Code { get; set; }

    /// <summary>请求方设备名，共享端用来提示「已向谁发送」。</summary>
    public string? DeviceName { get; set; }

}



/// <summary>
/// 共享端的响应。快照请求成功时，响应帧之后紧跟 <see cref="Length"/> 字节的 gzip 快照。
/// </summary>
internal sealed class LanSyncResponse
{

    public bool Ok { get; set; }

    /// <summary>失败原因，取值见 <see cref="LanSyncErrorCodes"/>。</summary>
    public string? Error { get; set; }

    public int Protocol { get; set; }

    public string? DeviceName { get; set; }

    public string? AppVersion { get; set; }

    public long Length { get; set; }

    /// <summary>gzip 快照的 SHA-256（十六进制），请求方收完后校验。</summary>
    public string? Sha256 { get; set; }

}



/// <summary>
/// UDP 发现报文。请求只带魔数与协议版本，共享端的回复再带上设备名与 TCP 端口。
/// </summary>
internal sealed class LanSyncDiscoveryMessage
{

    public string? Magic { get; set; }

    public int Protocol { get; set; }

    public string? DeviceName { get; set; }

    public int Port { get; set; }

    public string? AppVersion { get; set; }

}



/// <summary>
/// 响应里的错误码。只在两端之间传递，界面文案由请求方映射。
/// </summary>
internal static class LanSyncErrorCodes
{

    public const string ProtocolMismatch = "protocol_mismatch";

    public const string InvalidCode = "invalid_code";

    public const string Locked = "locked";

    public const string BadRequest = "bad_request";

    public const string Internal = "internal";

}



[JsonSerializable(typeof(LanSyncRequest))]
[JsonSerializable(typeof(LanSyncResponse))]
[JsonSerializable(typeof(LanSyncDiscoveryMessage))]
internal partial class LanSyncJsonContext : JsonSerializerContext;



/// <summary>
/// 局域网同步失败的类型，界面据此选择提示文案。
/// </summary>
internal enum LanSyncErrorKind
{
    /// <summary>手动输入的地址无法解析。</summary>
    InvalidAddress,

    /// <summary>连不上对方，或连接超时。</summary>
    ConnectFailed,

    InvalidCode,

    /// <summary>对方因验证码错误次数过多已停止共享。</summary>
    Locked,

    ProtocolMismatch,

    /// <summary>传输中断、校验失败或对方内部错误。</summary>
    Failed,
}



/// <summary>
/// 局域网同步的可预期失败。
/// </summary>
internal sealed class LanSyncException : Exception
{

    public LanSyncErrorKind Kind { get; }


    public LanSyncException(LanSyncErrorKind kind, string? message = null, Exception? innerException = null) : base(message ?? kind.ToString(), innerException)
    {
        Kind = kind;
    }

}



/// <summary>
/// 帧格式：4 字节魔数 + 4 字节小端长度 + UTF-8 JSON。
/// </summary>
internal static class LanSyncFrame
{

    private static ReadOnlySpan<byte> MagicBytes => "MWLS"u8;


    /// <summary>
    /// 写入一帧并立即刷新。
    /// </summary>
    public static async Task WriteAsync<T>(Stream stream, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        byte[] frame = new byte[8 + body.Length];
        MagicBytes.CopyTo(frame);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// 读取一帧。
    /// </summary>
    /// <param name="maxLength">允许的最大正文长度，防止对端用超长帧占满内存。</param>
    /// <exception cref="InvalidDataException">魔数不符或长度越界。</exception>
    /// <exception cref="EndOfStreamException">对端在帧读完前断开。</exception>
    public static async Task<T?> ReadAsync<T>(Stream stream, JsonTypeInfo<T> typeInfo, int maxLength, CancellationToken cancellationToken)
    {
        byte[] header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Unexpected LAN sync frame header.");
        }
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length <= 0 || length > maxLength)
        {
            throw new InvalidDataException($"Unexpected LAN sync frame length {length}.");
        }
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body, typeInfo);
    }

}
