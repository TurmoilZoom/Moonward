using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Vanara.PInvoke;

namespace Starward.RPC;

internal static class AppConfig
{

    public static string? AppVersion { get; private set; }


    public static bool IsAdmin { get; private set; }


    public static string MutexAndPipeName { get; private set; }


    public const string StartupMagic = "zb8L3ShgFjeyDxeA";


    public static bool IsPortable { get; private set; }


    public static bool IsAppInRemovableStorage { get; private set; }


    public static string CacheFolder { get; private set; }




    static AppConfig()
    {
        AppVersion = typeof(AppConfig).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        MutexAndPipeName = $"Starward.RPC/{Process.GetCurrentProcess().SessionId}/{AppVersion}";
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        IsAppInRemovableStorage = IsDeviceRemovableOrOnUSB(AppContext.BaseDirectory);
        // Velopack 部署结构：<root>/current/Starward.RPC.exe、<root>/Update.exe；便携版 <root> 下有 .portable 标记。
        string? rootFolder = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName;
        bool isVelopackInstall = rootFolder is not null && File.Exists(Path.Combine(rootFolder, "Update.exe"));
        IsPortable = isVelopackInstall && File.Exists(Path.Combine(rootFolder!, ".portable"));

        // 统一数据目录：主程序启动 RPC 子进程时通过 --data-folder 传入，确保 RPC 的日志与游戏缓存与主程序同目录。
        string? cmdDataFolder = GetCommandLineArgValue("--data-folder");
        if (!string.IsNullOrWhiteSpace(cmdDataFolder) && Path.IsPathFullyQualified(cmdDataFolder))
        {
            CacheFolder = cmdDataFolder;
        }
        else if (IsAppInRemovableStorage && IsPortable)
        {
            CacheFolder = Path.Combine(rootFolder!, ".cache");
        }
        else if (IsAppInRemovableStorage)
        {
            CacheFolder = Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory)!, ".StarwardCache");
        }
        else
        {
            CacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starward");
        }
        Directory.CreateDirectory(CacheFolder);
    }




    /// <summary>
    /// 从命令行参数读取指定开关后紧跟的值（如 <c>--data-folder "D:\xxx"</c>）。未找到返回 null。
    /// </summary>
    private static string? GetCommandLineArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1]?.Trim();
            }
        }
        return null;
    }




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
                STORAGE_DEVICE_DESCRIPTOR* desc = (STORAGE_DEVICE_DESCRIPTOR*)pBuffer;
                return desc->BusType == Kernel32.STORAGE_BUS_TYPE.BusTypeUsb;
            }
        }
        catch { }
        return false;
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


}
