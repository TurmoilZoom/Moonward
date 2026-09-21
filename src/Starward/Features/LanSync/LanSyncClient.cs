using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.LanSync;


/// <summary>
/// 局域网里一台正在共享的设备。
/// </summary>
/// <remarks>
/// 列表模板用它作 x:DataType，XAML 生成的类型信息会为可写属性生成赋值代码，
/// 所以属性只读、由构造函数赋值，不能用 required / init。
/// </remarks>
public sealed class LanSyncPeer
{

    public LanSyncPeer(string deviceName, IPEndPoint endPoint, string? appVersion, int protocol)
    {
        DeviceName = deviceName;
        EndPoint = endPoint;
        AppVersion = appVersion;
        Protocol = protocol;
    }


    public string DeviceName { get; }

    public IPEndPoint EndPoint { get; }

    public string? AppVersion { get; }

    public int Protocol { get; }

    /// <summary>列表第二行：地址与对方版本。</summary>
    public string Description
    {
        get
        {
            string address = LanSyncNetwork.FormatEndPoint(EndPoint.Address, EndPoint.Port);
            return string.IsNullOrWhiteSpace(AppVersion) ? address : $"{address}  ·  v{AppVersion}";
        }
    }

}



/// <summary>
/// 请求端：发现共享端、握手并下载快照。
/// </summary>
internal static class LanSyncClient
{

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>快照请求要等共享端导出并压缩整份快照，比握手慢得多。</summary>
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);



    /// <summary>
    /// 广播发现报文并收集回复。
    /// </summary>
    /// <param name="duration">等待回复的总时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按设备名排序的共享端，不含本机。</returns>
    public static async Task<List<LanSyncPeer>> DiscoverAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var peers = new Dictionary<IPEndPoint, LanSyncPeer>();
        var localAddresses = LanSyncNetwork.GetLocalIPv4Addresses().Select(x => x.Address).ToHashSet();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        LanSyncNetwork.DisableUdpConnectionReset(udp);
        var message = new LanSyncDiscoveryMessage { Magic = LanSyncProtocol.Magic, Protocol = LanSyncProtocol.Version };
        byte[] request = JsonSerializer.SerializeToUtf8Bytes(message, LanSyncJsonContext.Default.LanSyncDiscoveryMessage);
        List<IPEndPoint> targets = LanSyncNetwork.GetBroadcastEndPoints(LanSyncProtocol.DiscoveryPort);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(duration);
        Task receiving = ReceiveRepliesAsync(udp, peers, localAddresses, cts.Token);
        // UDP 可能丢包，隔一会儿重发
        for (int round = 0; round < 3 && !cts.IsCancellationRequested; round++)
        {
            foreach (IPEndPoint target in targets)
            {
                try
                {
                    await udp.SendAsync(request, target, cts.Token).ConfigureAwait(false);
                }
                // 个别网卡不允许发广播，跳过即可
                catch (SocketException) { }
                catch (OperationCanceledException) { }
            }
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }
        await receiving.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return peers.Values.OrderBy(x => x.DeviceName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }


    private static async Task ReceiveRepliesAsync(UdpClient udp, Dictionary<IPEndPoint, LanSyncPeer> peers, HashSet<IPAddress> localAddresses, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            // 本机自己在共享时也会回复，不列出
            if (localAddresses.Contains(result.RemoteEndPoint.Address))
            {
                continue;
            }
            LanSyncDiscoveryMessage? reply = ParseDiscoveryReply(result.Buffer);
            if (reply is null)
            {
                continue;
            }
            var endPoint = new IPEndPoint(result.RemoteEndPoint.Address, reply.Port);
            string deviceName = string.IsNullOrWhiteSpace(reply.DeviceName) ? endPoint.Address.ToString() : reply.DeviceName;
            peers[endPoint] = new LanSyncPeer(deviceName, endPoint, reply.AppVersion, reply.Protocol);
        }
    }


    private static LanSyncDiscoveryMessage? ParseDiscoveryReply(byte[] buffer)
    {
        if (buffer.Length == 0 || buffer.Length > LanSyncProtocol.MaxDiscoveryLength)
        {
            return null;
        }
        try
        {
            LanSyncDiscoveryMessage? message = JsonSerializer.Deserialize(buffer, LanSyncJsonContext.Default.LanSyncDiscoveryMessage);
            if (message?.Magic != LanSyncProtocol.Magic || message.Port is <= 0 or > 65535)
            {
                return null;
            }
            return message;
        }
        catch (JsonException)
        {
            return null;
        }
    }



    /// <summary>
    /// 与指定地址握手，取回对方设备名与版本。用于手动输入地址。
    /// </summary>
    /// <exception cref="LanSyncException">连接失败、协议不兼容或对方返回错误。</exception>
    public static async Task<LanSyncPeer> HelloAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        using TcpClient tcp = await ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        NetworkStream stream = tcp.GetStream();
        var request = new LanSyncRequest
        {
            Protocol = LanSyncProtocol.Version,
            Operation = LanSyncProtocol.OperationHello,
            DeviceName = Environment.MachineName,
        };
        LanSyncResponse response = await ExchangeAsync(stream, request, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        string deviceName = string.IsNullOrWhiteSpace(response.DeviceName) ? endPoint.Address.ToString() : response.DeviceName;
        return new LanSyncPeer(deviceName, endPoint, response.AppVersion, response.Protocol);
    }



    /// <summary>
    /// 凭验证码下载快照，校验后解压到临时文件。
    /// </summary>
    /// <param name="endPoint">共享端地址。</param>
    /// <param name="code">对方显示的验证码。</param>
    /// <param name="onReceived">收到数据时回调（已收字节，总字节），约每 100 ms 一次，在后台线程调用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解压后的快照路径，调用方用完删除。</returns>
    /// <exception cref="LanSyncException">连接失败、验证码错误、传输中断或校验失败。</exception>
    public static async Task<string> DownloadSnapshotAsync(IPEndPoint endPoint, string code, Action<long, long>? onReceived, CancellationToken cancellationToken)
    {
        using TcpClient tcp = await ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        NetworkStream stream = tcp.GetStream();
        var request = new LanSyncRequest
        {
            Protocol = LanSyncProtocol.Version,
            Operation = LanSyncProtocol.OperationSnapshot,
            Code = code,
            DeviceName = Environment.MachineName,
        };
        LanSyncResponse response = await ExchangeAsync(stream, request, ResponseTimeout, cancellationToken).ConfigureAwait(false);
        if (response.Length <= 0 || string.IsNullOrWhiteSpace(response.Sha256))
        {
            throw new LanSyncException(LanSyncErrorKind.Failed, "The snapshot header is invalid.");
        }

        string compressedFile = LanSyncSnapshot.CreateTempFilePath(".db.gz");
        string snapshotFile = LanSyncSnapshot.CreateTempFilePath(".db");
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TransferTimeout);
                try
                {
                    await ReceiveToFileAsync(stream, compressedFile, response.Length, onReceived, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new LanSyncException(LanSyncErrorKind.Failed, "Timed out while receiving the snapshot.", ex);
                }
                catch (IOException ex)
                {
                    throw new LanSyncException(LanSyncErrorKind.Failed, ex.Message, ex);
                }
            }

            string sha256;
            using (FileStream input = File.OpenRead(compressedFile))
            {
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            }
            if (!string.Equals(sha256, response.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LanSyncException(LanSyncErrorKind.Failed, "The snapshot checksum does not match.");
            }

            using (FileStream input = File.OpenRead(compressedFile))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (FileStream output = File.Create(snapshotFile))
            {
                await gzip.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            return snapshotFile;
        }
        catch
        {
            LanSyncSnapshot.DeleteTempFile(snapshotFile);
            throw;
        }
        finally
        {
            LanSyncSnapshot.DeleteTempFile(compressedFile);
        }
    }


    private static async Task ReceiveToFileAsync(NetworkStream stream, string file, long length, Action<long, long>? onReceived, CancellationToken cancellationToken)
    {
        using FileStream output = File.Create(file);
        byte[] buffer = new byte[81920];
        long received = 0;
        var stopwatch = Stopwatch.StartNew();
        onReceived?.Invoke(0, length);
        while (received < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - received)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new LanSyncException(LanSyncErrorKind.Failed, "The connection was closed before the snapshot was complete.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (stopwatch.Elapsed >= ProgressInterval || received == length)
            {
                onReceived?.Invoke(received, length);
                stopwatch.Restart();
            }
        }
    }



    private static async Task<TcpClient> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(endPoint.Address, endPoint.Port, timeout.Token).ConfigureAwait(false);
            return tcp;
        }
        catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            tcp.Dispose();
            throw new LanSyncException(LanSyncErrorKind.ConnectFailed, ex.Message, ex);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }


    /// <summary>
    /// 发送请求并读取响应帧，对方返回错误时转成 <see cref="LanSyncException"/>。
    /// </summary>
    private static async Task<LanSyncResponse> ExchangeAsync(NetworkStream stream, LanSyncRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        LanSyncResponse? response;
        try
        {
            await LanSyncFrame.WriteAsync(stream, request, LanSyncJsonContext.Default.LanSyncRequest, cts.Token).ConfigureAwait(false);
            response = await LanSyncFrame.ReadAsync(stream, LanSyncJsonContext.Default.LanSyncResponse, LanSyncProtocol.MaxResponseLength, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanSyncException(LanSyncErrorKind.ConnectFailed, "Timed out waiting for the other device.", ex);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or JsonException)
        {
            throw new LanSyncException(LanSyncErrorKind.Failed, ex.Message, ex);
        }
        if (response is null)
        {
            throw new LanSyncException(LanSyncErrorKind.Failed, "Empty response.");
        }
        if (!response.Ok)
        {
            throw response.Error switch
            {
                LanSyncErrorCodes.InvalidCode => new LanSyncException(LanSyncErrorKind.InvalidCode),
                LanSyncErrorCodes.Locked => new LanSyncException(LanSyncErrorKind.Locked),
                LanSyncErrorCodes.ProtocolMismatch => new LanSyncException(LanSyncErrorKind.ProtocolMismatch),
                _ => new LanSyncException(LanSyncErrorKind.Failed, response.Error),
            };
        }
        if (response.Protocol != LanSyncProtocol.Version)
        {
            throw new LanSyncException(LanSyncErrorKind.ProtocolMismatch);
        }
        return response;
    }

}
