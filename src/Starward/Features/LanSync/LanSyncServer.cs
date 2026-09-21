using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.LanSync;


/// <summary>
/// 共享端事件类型。
/// </summary>
internal enum LanSyncServerEventKind
{
    /// <summary>已向一台设备发送快照。</summary>
    Sent,

    /// <summary>验证码错误次数过多，已停止共享。</summary>
    Locked,
}


/// <summary>
/// 共享端事件。
/// </summary>
/// <param name="DeviceName">请求方自报的设备名。</param>
/// <param name="Address">请求方 IP。</param>
internal readonly record struct LanSyncServerEvent(LanSyncServerEventKind Kind, string? DeviceName, string? Address);



/// <summary>
/// 共享端：共享期间监听 TCP（发快照）与 UDP（应答发现），凭验证码把本机快照发给请求方。
/// </summary>
/// <remarks>
/// 只在共享窗口打开期间运行，关闭即 <see cref="Dispose"/>。服务端只读，不接收、不写入对方的任何数据。
/// </remarks>
internal sealed class LanSyncServer : IDisposable
{

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(10);


    private readonly ILogger _logger;

    private readonly TcpListener _tcpListener;

    private readonly UdpClient? _discoveryClient;

    private readonly CancellationTokenSource _cts = new();

    /// <summary>同一时间只导出一份快照，避免多个请求方同时读整库。</summary>
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);

    private int _failedAttempts;

    private int _stopped;


    /// <summary>本次共享的 6 位验证码。</summary>
    public string Code { get; }

    /// <summary>实际监听的 TCP 端口。</summary>
    public int Port { get; }

    public string DeviceName { get; }

    /// <summary>UDP 发现端口是否监听成功；失败时对方只能手动输入地址。</summary>
    public bool IsDiscoveryAvailable => _discoveryClient is not null;

    /// <summary>共享事件，在线程池线程上触发，订阅方需自行切回 UI 线程。</summary>
    public event Action<LanSyncServerEvent>? EventRaised;



    private LanSyncServer(ILogger logger, TcpListener tcpListener, UdpClient? discoveryClient)
    {
        _logger = logger;
        _tcpListener = tcpListener;
        _discoveryClient = discoveryClient;
        Port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        DeviceName = Environment.MachineName;
        Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }



    /// <summary>
    /// 开始共享。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <returns>运行中的共享端，停止共享时 Dispose。</returns>
    /// <exception cref="SocketException">TCP 监听失败。</exception>
    public static LanSyncServer Start(ILogger logger)
    {
        TcpListener tcpListener = StartTcpListener();
        UdpClient? discoveryClient = TryStartDiscoveryListener(logger);
        var server = new LanSyncServer(logger, tcpListener, discoveryClient);
        _ = server.AcceptLoopAsync(server._cts.Token);
        if (discoveryClient is not null)
        {
            _ = server.DiscoveryLoopAsync(discoveryClient, server._cts.Token);
        }
        logger.LogInformation("LAN sync sharing started on TCP port {port}, discovery available: {discovery}.", server.Port, discoveryClient is not null);
        return server;
    }


    private static TcpListener StartTcpListener()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, LanSyncProtocol.DefaultServicePort);
            listener.Start();
            return listener;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            // 固定端口被占用或落在系统保留范围里：改用系统分配的端口，请求方从发现回复里拿到实际端口
            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            return listener;
        }
    }


    private static UdpClient? TryStartDiscoveryListener(ILogger logger)
    {
        try
        {
            var udp = new UdpClient(new IPEndPoint(IPAddress.Any, LanSyncProtocol.DiscoveryPort));
            LanSyncNetwork.DisableUdpConnectionReset(udp);
            return udp;
        }
        catch (SocketException ex)
        {
            // 发现端口不可用时仍能共享，只是对方需要手动输入地址
            logger.LogWarning(ex, "LAN sync discovery port {port} is unavailable.", LanSyncProtocol.DiscoveryPort);
            return null;
        }
    }



    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                _logger.LogWarning(ex, "LAN sync accept failed.");
                continue;
            }
            _ = HandleClientAsync(client, cancellationToken);
        }
    }



    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        string address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
        try
        {
            using (client)
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                LanSyncRequest? request;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(RequestTimeout);
                    request = await LanSyncFrame.ReadAsync(stream, LanSyncJsonContext.Default.LanSyncRequest, LanSyncProtocol.MaxRequestLength, timeout.Token).ConfigureAwait(false);
                }
                if (request is null)
                {
                    return;
                }
                if (request.Protocol != LanSyncProtocol.Version)
                {
                    await WriteErrorAsync(stream, LanSyncErrorCodes.ProtocolMismatch, cancellationToken).ConfigureAwait(false);
                    return;
                }
                switch (request.Operation)
                {
                    case LanSyncProtocol.OperationHello:
                        await LanSyncFrame.WriteAsync(stream, CreateResponse(), LanSyncJsonContext.Default.LanSyncResponse, cancellationToken).ConfigureAwait(false);
                        break;
                    case LanSyncProtocol.OperationSnapshot:
                        await SendSnapshotAsync(client, stream, request, address, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        await WriteErrorAsync(stream, LanSyncErrorCodes.BadRequest, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException or JsonException)
        {
            // 对端断开、超时或发来非法数据，只影响这一次连接
            _logger.LogInformation("LAN sync connection from {address} ended: {message}", address, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LAN sync connection from {address} failed.", address);
        }
    }



    /// <summary>
    /// 校验验证码后导出、压缩并发送快照。
    /// </summary>
    private async Task SendSnapshotAsync(TcpClient client, NetworkStream stream, LanSyncRequest request, string address, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _failedAttempts) >= LanSyncProtocol.MaxFailedAttempts)
        {
            await WriteErrorAsync(stream, LanSyncErrorCodes.Locked, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!IsCodeValid(request.Code))
        {
            int failed = Interlocked.Increment(ref _failedAttempts);
            bool locked = failed >= LanSyncProtocol.MaxFailedAttempts;
            _logger.LogWarning("LAN sync rejected a wrong code from {address} ({failed}/{max}).", address, failed, LanSyncProtocol.MaxFailedAttempts);
            await WriteErrorAsync(stream, locked ? LanSyncErrorCodes.Locked : LanSyncErrorCodes.InvalidCode, cancellationToken).ConfigureAwait(false);
            if (locked)
            {
                // 防止在局域网里穷举验证码：锁定后停止监听，需要用户重新打开共享拿新码
                StopListening();
                EventRaised?.Invoke(new LanSyncServerEvent(LanSyncServerEventKind.Locked, request.DeviceName, address));
            }
            return;
        }

        string snapshotFile = LanSyncSnapshot.CreateTempFilePath(".db");
        string compressedFile = snapshotFile + ".gz";
        await _snapshotLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                LanSyncSnapshot.Export(snapshotFile, DeviceName);
                Compress(snapshotFile, compressedFile);
            }, cancellationToken).ConfigureAwait(false);

            string sha256;
            using (FileStream input = File.OpenRead(compressedFile))
            {
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            }
            LanSyncResponse response = CreateResponse();
            response.Length = new FileInfo(compressedFile).Length;
            response.Sha256 = sha256;
            await LanSyncFrame.WriteAsync(stream, response, LanSyncJsonContext.Default.LanSyncResponse, cancellationToken).ConfigureAwait(false);

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (FileStream input = File.OpenRead(compressedFile))
            {
                timeout.CancelAfter(TransferTimeout);
                await input.CopyToAsync(stream, timeout.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            // 半关闭发送方向，让请求方读到流末尾，而不是等连接超时
            client.Client.Shutdown(SocketShutdown.Send);
            _logger.LogInformation("LAN sync snapshot ({length} bytes) sent to {device} ({address}).", response.Length, request.DeviceName, address);
            EventRaised?.Invoke(new LanSyncServerEvent(LanSyncServerEventKind.Sent, request.DeviceName, address));
        }
        catch (Exception ex) when (ex is not (IOException or SocketException or OperationCanceledException or ObjectDisposedException))
        {
            _logger.LogError(ex, "LAN sync failed to export snapshot for {address}.", address);
            await WriteErrorAsync(stream, LanSyncErrorCodes.Internal, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _snapshotLock.Release();
            LanSyncSnapshot.DeleteTempFile(snapshotFile);
            LanSyncSnapshot.DeleteTempFile(compressedFile);
        }
    }


    private bool IsCodeValid(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != Code.Length)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code), Encoding.ASCII.GetBytes(Code));
    }


    private static void Compress(string source, string destination)
    {
        using FileStream input = File.OpenRead(source);
        using FileStream output = File.Create(destination);
        using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        input.CopyTo(gzip);
    }


    private LanSyncResponse CreateResponse()
    {
        return new LanSyncResponse
        {
            Ok = true,
            Protocol = LanSyncProtocol.Version,
            DeviceName = DeviceName,
            AppVersion = AppConfig.AppVersion,
        };
    }


    private static Task WriteErrorAsync(NetworkStream stream, string error, CancellationToken cancellationToken)
    {
        var response = new LanSyncResponse
        {
            Ok = false,
            Error = error,
            Protocol = LanSyncProtocol.Version,
        };
        return LanSyncFrame.WriteAsync(stream, response, LanSyncJsonContext.Default.LanSyncResponse, cancellationToken);
    }



    private async Task DiscoveryLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        var message = new LanSyncDiscoveryMessage
        {
            Magic = LanSyncProtocol.Magic,
            Protocol = LanSyncProtocol.Version,
            DeviceName = DeviceName,
            Port = Port,
            AppVersion = AppConfig.AppVersion,
        };
        byte[] reply = JsonSerializer.SerializeToUtf8Bytes(message, LanSyncJsonContext.Default.LanSyncDiscoveryMessage);
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                continue;
            }
            if (!IsDiscoveryRequest(result.Buffer))
            {
                continue;
            }
            try
            {
                await udp.SendAsync(reply, result.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }


    private static bool IsDiscoveryRequest(byte[] buffer)
    {
        if (buffer.Length == 0 || buffer.Length > LanSyncProtocol.MaxDiscoveryLength)
        {
            return false;
        }
        try
        {
            LanSyncDiscoveryMessage? message = JsonSerializer.Deserialize(buffer, LanSyncJsonContext.Default.LanSyncDiscoveryMessage);
            return message?.Magic == LanSyncProtocol.Magic;
        }
        catch (JsonException)
        {
            return false;
        }
    }



    /// <summary>
    /// 停止监听，已建立的连接各自结束。
    /// </summary>
    private void StopListening()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }
        _cts.Cancel();
        try
        {
            _tcpListener.Stop();
        }
        catch { }
        _discoveryClient?.Dispose();
        _logger.LogInformation("LAN sync sharing stopped.");
    }


    public void Dispose()
    {
        StopListening();
    }

}
