using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.LanSync;

/// <summary>
/// 局域网同步用到的网卡、地址与套接字工具。
/// </summary>
internal static class LanSyncNetwork
{

    /// <summary>
    /// 本机可用于局域网通信的 IPv4 地址：网卡已启用、非回环、非隧道。
    /// 私有网段排最前，其次是带默认网关的网卡：代理软件的 TUN 网卡（常见 198.18.x.x）也带网关，只按网关排会把它排到真实局域网地址前面。
    /// </summary>
    /// <returns>地址与子网掩码。</returns>
    public static List<(IPAddress Address, IPAddress Mask)> GetLocalIPv4Addresses()
    {
        var list = new List<(IPAddress Address, IPAddress Mask, bool HasGateway)>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }
            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            bool hasGateway = properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !x.Address.Equals(IPAddress.Any));
            foreach (UnicastIPAddressInformation info in properties.UnicastAddresses)
            {
                if (info.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(info.Address))
                {
                    list.Add((info.Address, info.IPv4Mask, hasGateway));
                }
            }
        }
        return list.OrderBy(x => GetAddressRank(x.Address))
                   .ThenByDescending(x => x.HasGateway)
                   .Select(x => (x.Address, x.Mask))
                   .ToList();
    }


    /// <summary>
    /// 地址排序权重：私有网段 0，其他 1，链路本地（169.254.x.x，没拿到 DHCP 地址）2。
    /// </summary>
    private static int GetAddressRank(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168))
        {
            return 0;
        }
        return b[0] == 169 && b[1] == 254 ? 2 : 1;
    }


    /// <summary>
    /// 发现广播的目标：受限广播加上每块网卡的定向广播。
    /// 多网卡时 255.255.255.255 只会从其中一块网卡发出，其余网段靠定向广播补齐。
    /// </summary>
    /// <param name="port">目标端口。</param>
    public static List<IPEndPoint> GetBroadcastEndPoints(int port)
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach ((IPAddress address, IPAddress mask) in GetLocalIPv4Addresses())
        {
            if (mask is null || mask.Equals(IPAddress.Any))
            {
                continue;
            }
            byte[] bytes = address.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(bytes[i] | ~maskBytes[i]);
            }
            addresses.Add(new IPAddress(bytes));
        }
        return addresses.Select(x => new IPEndPoint(x, port)).ToList();
    }


    /// <summary>
    /// 关闭 Windows UDP 套接字的 SIO_UDP_CONNRESET。
    /// 否则向已关闭的端口发过包、收到 ICMP 端口不可达后，下一次接收会抛 10054，发现循环就断了。
    /// </summary>
    public static void DisableUdpConnectionReset(UdpClient udp)
    {
        const int SIO_UDP_CONNRESET = -1744830452;
        try
        {
            udp.Client.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
        }
        catch (SocketException) { }
        catch (PlatformNotSupportedException) { }
    }


    /// <summary>
    /// 解析手动输入的地址：IPv4、IPv4:端口，或计算机名（经系统名称解析取第一个 IPv4）。省略端口时用默认端口。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>共享端的 TCP 终结点。</returns>
    /// <exception cref="LanSyncException">地址格式不对或解析不到 IPv4。</exception>
    public static async Task<IPEndPoint> ResolveAsync(string input, CancellationToken cancellationToken)
    {
        string text = input.Trim();
        string host = text;
        int port = LanSyncProtocol.DefaultServicePort;
        int colon = text.LastIndexOf(':');
        // 只认一个冒号的 host:port；IPv6 不支持，交给后面按主机名解析失败
        if (colon > 0 && text.IndexOf(':') == colon)
        {
            if (!int.TryParse(text[(colon + 1)..], out port) || port is <= 0 or > 65535)
            {
                throw new LanSyncException(LanSyncErrorKind.InvalidAddress);
            }
            host = text[..colon];
        }
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new LanSyncException(LanSyncErrorKind.InvalidAddress);
        }
        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new LanSyncException(LanSyncErrorKind.InvalidAddress);
            }
            return new IPEndPoint(ip, port);
        }
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
            if (addresses.Length > 0)
            {
                return new IPEndPoint(addresses[0], port);
            }
        }
        catch (SocketException) { }
        catch (ArgumentException) { }
        throw new LanSyncException(LanSyncErrorKind.InvalidAddress);
    }


    /// <summary>
    /// 地址的显示形式：默认端口时只显示 IP，方便对方照抄。
    /// </summary>
    public static string FormatEndPoint(IPAddress address, int port)
    {
        return port == LanSyncProtocol.DefaultServicePort ? address.ToString() : $"{address}:{port}";
    }

}
