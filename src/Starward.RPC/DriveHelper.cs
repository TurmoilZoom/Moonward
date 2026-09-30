using System;
using System.IO;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Starward.RPC;

internal abstract class DriveHelper
{


    public static bool IsDeviceRemovableOrOnUSB(string path)
    {
        try
        {
            if (Directory.Exists(path))
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
                Kernel32.STORAGE_PROPERTY_QUERY query = new()
                {
                    PropertyId = Kernel32.STORAGE_PROPERTY_ID.StorageDeviceProperty,
                    QueryType = Kernel32.STORAGE_QUERY_TYPE.PropertyStandardQuery,
                };
                bool result = Kernel32.DeviceIoControl(hDevice, Kernel32.IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY, query, out Kernel32.STORAGE_DEVICE_DESCRIPTOR_MGD desc);
                if (result)
                {
                    if (desc.BusType == Kernel32.STORAGE_BUS_TYPE.BusTypeUsb)
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
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
            if (!Kernel32.GetVolumeInformation(root, out string volumeName, out uint volumeSerialNumber, out uint maximumComponentLength, out Kernel32.FileSystemFlags fileSystemFlags, out string fileSystemName))
            {
                Kernel32.GetLastError().ThrowIfFailed($"Failed to get volume information for path '{path}'.");
            }
            return fileSystemName;
        }
    }



    /// <summary>
    /// 路径是否在固态硬盘上。判定与界面上的磁盘类型标签（Starward.Helpers.DriveHelper.GetDiskMediaType）一致：
    /// 网络位置、可移动存储和 USB 外置硬盘都不算，本机固定磁盘按有无寻道惩罚区分。
    /// </summary>
    /// <param name="path">文件或文件夹路径，可以尚不存在</param>
    /// <returns>确认是固态硬盘时为 true，识别不了也返回 false</returns>
    public static unsafe bool IsSolidStateDrive(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            // 网络共享路径按字面判断，不去访问可能已离线的服务器；\\?\ 与 \\.\ 开头的是本机设备路径
            if ((fullPath.StartsWith(@"\\") && !fullPath.StartsWith(@"\\?\") && !fullPath.StartsWith(@"\\.\"))
                || fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string? root = GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root) || (DriveType)Kernel32.GetDriveType(root) is not DriveType.Fixed)
            {
                return false;
            }
            string device = GetVolumeDevicePath(fullPath) ?? $@"\\.\{root.TrimEnd('\\')}";
            using Kernel32.SafeHFILE hDevice = Kernel32.CreateFile(device, 0, FileShare.ReadWrite | FileShare.Delete, null, FileMode.Open, 0, HFILE.NULL);
            if (hDevice.IsInvalid)
            {
                return false;
            }
            // USB 外置硬盘在系统里是「固定磁盘」，界面上按可移动显示，这里同样不当作 SSD
            STORAGE_PROPERTY_QUERY query = new()
            {
                PropertyId = Kernel32.STORAGE_PROPERTY_ID.StorageDeviceProperty,
                QueryType = Kernel32.STORAGE_QUERY_TYPE.PropertyStandardQuery,
            };
            Span<byte> buffer = stackalloc byte[512];
            fixed (byte* pBuffer = buffer)
            {
                if (Kernel32.DeviceIoControl(hDevice, Kernel32.IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY, (nint)(&query), (uint)sizeof(STORAGE_PROPERTY_QUERY), (nint)pBuffer, (uint)buffer.Length, out uint bytesReturned, IntPtr.Zero)
                    && bytesReturned >= sizeof(STORAGE_DEVICE_DESCRIPTOR)
                    && ((STORAGE_DEVICE_DESCRIPTOR*)pBuffer)->BusType == Kernel32.STORAGE_BUS_TYPE.BusTypeUsb)
                {
                    return false;
                }
            }
            query.PropertyId = Kernel32.STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty;
            DEVICE_SEEK_PENALTY_DESCRIPTOR descriptor = default;
            bool result = Kernel32.DeviceIoControl(hDevice, Kernel32.IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY, (nint)(&query), (uint)sizeof(STORAGE_PROPERTY_QUERY), (nint)(&descriptor), (uint)sizeof(DEVICE_SEEK_PENALTY_DESCRIPTOR), out uint seekBytesReturned, IntPtr.Zero);
            // 返回的数据至少要覆盖到 IncursSeekPenalty（偏移 8 的 1 字节）
            return result && seekBytesReturned >= 9 && descriptor.IncursSeekPenalty == 0;
        }
        catch { }
        return false;
    }



    /// <summary>
    /// 把路径解析为所在卷的设备路径（\\?\Volume{GUID}），挂载到文件夹的卷也能对应到正确的磁盘。
    /// </summary>
    /// <param name="path">文件或文件夹路径，可以尚不存在</param>
    /// <returns>卷设备路径；解析失败时返回 null，由调用方回退到盘符</returns>
    private static string? GetVolumeDevicePath(string path)
    {
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



    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public Kernel32.STORAGE_PROPERTY_ID PropertyId;
        public Kernel32.STORAGE_QUERY_TYPE QueryType;
        public byte AdditionalParameters;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_DESCRIPTOR
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
    private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        public byte IncursSeekPenalty;
    }


}