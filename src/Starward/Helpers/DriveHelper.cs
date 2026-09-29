using System;
using System.IO;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Starward.Helpers;

internal abstract class DriveHelper
{


    public static unsafe bool IsDeviceRemovableOrOnUSB(string path)
    {
        try
        {
            DriveInfo drive = new DriveInfo(path);
            if (drive.DriveType is DriveType.Removable)
            {
                return true;
            }
            string fileName = $@"\\.\{drive.Name.Trim('\\')}";
            using Kernel32.SafeHFILE hDevice = Kernel32.CreateFile(fileName, 0, FileShare.ReadWrite | FileShare.Delete, null, FileMode.Open, 0, HFILE.NULL);
            if (hDevice.IsInvalid)
            {
                return false;
            }
            return TryGetBusType(hDevice, out Kernel32.STORAGE_BUS_TYPE busType) && busType == Kernel32.STORAGE_BUS_TYPE.BusTypeUsb;
        }
        catch { }
        return false;
    }



    /// <summary>
    /// 查询存储设备的总线类型（USB / SATA / NVMe 等）。
    /// </summary>
    /// <param name="hDevice">以 0 访问权限打开的卷设备句柄</param>
    /// <param name="busType">总线类型</param>
    /// <returns>查询是否成功</returns>
    private static unsafe bool TryGetBusType(Kernel32.SafeHFILE hDevice, out Kernel32.STORAGE_BUS_TYPE busType)
    {
        busType = default;
        STORAGE_PROPERTY_QUERY query = new()
        {
            PropertyId = Kernel32.STORAGE_PROPERTY_ID.StorageDeviceProperty,
            QueryType = Kernel32.STORAGE_QUERY_TYPE.PropertyStandardQuery,
        };
        Span<byte> buffer = stackalloc byte[512];
        fixed (byte* pBuffer = buffer)
        {
            bool result = Kernel32.DeviceIoControl(hDevice,
                                                   Kernel32.IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY,
                                                   (nint)(&query),
                                                   (uint)sizeof(STORAGE_PROPERTY_QUERY),
                                                   (nint)pBuffer,
                                                   (uint)buffer.Length,
                                                   out uint bytesReturned,
                                                   IntPtr.Zero);
            if (!result || bytesReturned < sizeof(STORAGE_DEVICE_DESCRIPTOR))
            {
                return false;
            }
            busType = ((STORAGE_DEVICE_DESCRIPTOR*)pBuffer)->BusType;
            return true;
        }
    }



    /// <summary>
    /// 获取路径所在磁盘的类型：网络位置、可移动存储，或按有无寻道惩罚区分的 SSD / HDD。
    /// </summary>
    /// <param name="path">文件或文件夹路径，可以尚不存在</param>
    /// <returns>识别结果；驱动器不存在、光驱或设备不支持查询时返回 <see cref="DiskMediaType.Unknown"/></returns>
    public static unsafe DiskMediaType GetDiskMediaType(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            // 按字面判断网络共享路径，不去访问可能已离线的服务器
            if (IsUncPath(fullPath))
            {
                return DiskMediaType.Network;
            }
            string? root = GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return DiskMediaType.Unknown;
            }
            switch ((DriveType)Kernel32.GetDriveType(root))
            {
                case DriveType.Network:
                    return DiskMediaType.Network;
                case DriveType.Removable:
                    return DiskMediaType.Removable;
                case DriveType.Fixed:
                    break;
                default:
                    // 光驱、内存盘、不存在的盘符
                    return DiskMediaType.Unknown;
            }
            string device = GetVolumeDevicePath(fullPath) ?? $@"\\.\{root.TrimEnd('\\')}";
            using Kernel32.SafeHFILE hDevice = Kernel32.CreateFile(device, 0, FileShare.ReadWrite | FileShare.Delete, null, FileMode.Open, 0, HFILE.NULL);
            if (hDevice.IsInvalid)
            {
                return DiskMediaType.Unknown;
            }
            // USB 外置硬盘在系统里是「固定磁盘」，与 IsDeviceRemovableOrOnUSB 一致按可移动处理，优先于 SSD / HDD
            if (TryGetBusType(hDevice, out Kernel32.STORAGE_BUS_TYPE busType) && busType == Kernel32.STORAGE_BUS_TYPE.BusTypeUsb)
            {
                return DiskMediaType.Removable;
            }
            STORAGE_PROPERTY_QUERY query = new()
            {
                PropertyId = Kernel32.STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty,
                QueryType = Kernel32.STORAGE_QUERY_TYPE.PropertyStandardQuery,
            };
            DEVICE_SEEK_PENALTY_DESCRIPTOR descriptor = default;
            bool result = Kernel32.DeviceIoControl(hDevice,
                                                   Kernel32.IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY,
                                                   (nint)(&query),
                                                   (uint)sizeof(STORAGE_PROPERTY_QUERY),
                                                   (nint)(&descriptor),
                                                   (uint)sizeof(DEVICE_SEEK_PENALTY_DESCRIPTOR),
                                                   out uint bytesReturned,
                                                   IntPtr.Zero);
            // 返回的数据至少要覆盖到 IncursSeekPenalty（偏移 8 的 1 字节）
            if (!result || bytesReturned < 9)
            {
                return DiskMediaType.Unknown;
            }
            return descriptor.IncursSeekPenalty != 0 ? DiskMediaType.HDD : DiskMediaType.SSD;
        }
        catch { }
        return DiskMediaType.Unknown;
    }



    /// <summary>
    /// 把路径解析为所在卷的设备路径（\\?\Volume{GUID}），挂载到文件夹的卷也能对应到正确的磁盘。
    /// </summary>
    /// <param name="path">文件或文件夹路径，可以尚不存在</param>
    /// <returns>卷设备路径；解析失败时返回 null，由调用方回退到盘符</returns>
    private static string? GetVolumeDevicePath(string path)
    {
        // 安装路径通常还没创建，向上找到最近一级已存在的目录再解析挂载点
        string? dir = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (string.IsNullOrEmpty(dir)
            || !Kernel32.GetVolumePathName(dir, out string? mountPoint)
            || string.IsNullOrWhiteSpace(mountPoint)
            || !Kernel32.GetVolumeNameForVolumeMountPoint(mountPoint, out string? volumeName)
            || string.IsNullOrWhiteSpace(volumeName))
        {
            return null;
        }
        // 带结尾反斜杠打开的是卷的根目录而不是卷设备，DeviceIoControl 会失败
        return volumeName.TrimEnd('\\');
    }



    /// <summary>
    /// 是否为网络共享路径（\\server\share 或 \\?\UNC\server\share）。
    /// </summary>
    /// <param name="fullPath">完整路径</param>
    /// <returns>是网络共享路径时为 true</returns>
    private static bool IsUncPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // \\?\ 与 \\.\ 开头的是本机设备路径，不是网络共享
        return fullPath.StartsWith(@"\\") && !fullPath.StartsWith(@"\\?\") && !fullPath.StartsWith(@"\\.\");
    }



    private static string? GetPathRoot(string path)
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(root) && !root.EndsWith('\\'))
        {
            root += '\\';
        }
        return root;
    }


    public static DriveType GetDriveType(string path)
    {
        string? root = GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return DriveType.Unknown;
        }
        return (DriveType)Kernel32.GetDriveType(root);
    }



    public static long GetDriveAvailableSpace(string path)
    {
        string? root = GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new DriveNotFoundException($"The path '{path}' does not have a valid root directory.");
        }
        else
        {
            if (!Kernel32.GetDiskFreeSpaceEx(root, out ulong freeBytesAvailable, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes))
            {
                Kernel32.GetLastError().ThrowIfFailed($"Failed to get disk free space information for path '{path}'.");
            }
            return (long)freeBytesAvailable;
        }
    }


    public static string? GetDriveFormat(string path)
    {
        string? root = GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }
        else
        {
            if (!Kernel32.GetVolumeInformation(root, out string? volumeName, out uint volumeSerialNumber, out uint maximumComponentLength, out Kernel32.FileSystemFlags fileSystemFlags, out string? fileSystemName))
            {
                Kernel32.GetLastError().ThrowIfFailed($"Failed to get volume information for path '{path}'.");
            }
            return fileSystemName;
        }
    }



    [StructLayout(LayoutKind.Sequential)]
    internal struct STORAGE_PROPERTY_QUERY
    {
        public Kernel32.STORAGE_PROPERTY_ID PropertyId;
        public Kernel32.STORAGE_QUERY_TYPE QueryType;
        public byte AdditionalParameters;
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct STORAGE_DEVICE_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        public byte DeviceType;
        public byte DeviceTypeModifier;
        public byte RemovableMedia;
        public byte CommandQueueing;
        public uint VendorIdOffset;
        public uint ProductIdOffset;
        public uint ProductRevisionOffset;
        public uint SerialNumberOffset;
        public Kernel32.STORAGE_BUS_TYPE BusType;
        public uint RawPropertiesLength;
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct DEVICE_SEEK_PENALTY_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        public byte IncursSeekPenalty;
    }

}