using Starward.Core;
using Starward.Features.UrlProtocol;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 生成「任务栏启动方式」快捷方式（.lnk）并提供可选图标。
/// 快捷方式目标 = Starward.exe，参数 = <c>starward://startgame/{biz}[?profile={id}]</c>，
/// 由 App 启动时直接解析命令行参数（见 App.OnLaunched），无需依赖 URL 协议注册。
/// </summary>
public static class GameShortcutService
{


    /// <summary>
    /// 快捷方式图标来源：exe / dll / ico 文件路径 + 图标索引。
    /// </summary>
    public sealed class IconSource
    {
        /// <summary>图标资源文件路径，可包含环境变量（如 %SystemRoot%）。</summary>
        public required string Path { get; init; }

        /// <summary>图标在文件中的索引（.ico 用 0）。</summary>
        public int Index { get; init; }

        /// <summary>是否为用户上传的自定义 .ico。</summary>
        public bool IsCustom { get; init; }

        /// <summary>展开环境变量后的实际路径。</summary>
        public string ExpandedPath => Environment.ExpandEnvironmentVariables(Path);
    }


    /// <summary>
    /// 在桌面生成一个启动游戏的快捷方式，返回 .lnk 完整路径。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="gameName">游戏显示名（用于文件名）。</param>
    /// <param name="profileId">配置文件内部名；null = 跟随软件设置（URL 不带 profile 参数）。</param>
    /// <param name="profileDisplayName">配置文件显示名（用于文件名）。</param>
    /// <param name="icon">图标来源；null 时用 Starward.exe 图标。</param>
    public static string CreateStartGameShortcut(GameBiz biz, string gameName, string? profileId, string profileDisplayName, IconSource? icon)
    {
        string url = UrlProtocolService.BuildStartGameUrl(biz, profileId);
        string exe = AppConfig.StarwardExecutePath;
        // 默认创建到桌面
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string fileName = SanitizeFileName($"{gameName} - {profileDisplayName}") + ".lnk";
        string lnkPath = Path.Combine(dir, fileName);

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exe);
        link.SetArguments(url);
        string? workingDir = Path.GetDirectoryName(exe);
        if (!string.IsNullOrEmpty(workingDir))
        {
            link.SetWorkingDirectory(workingDir);
        }
        if (icon is not null)
        {
            link.SetIconLocation(icon.ExpandedPath, icon.Index);
        }
        else
        {
            link.SetIconLocation(exe, 0);
        }
        link.SetDescription($"Starward - {gameName}");

        // 给快捷方式设置独立的 AppUserModelID，避免 Windows 把该开始菜单快捷方式与
        // 正在运行的 Starward 窗口关联（按可执行文件路径匹配），进而用快捷方式的图标
        // 覆盖任务栏上正在运行程序的图标。显式 AUMID 与运行进程不同即可断开该关联。
        TrySetShortcutAppUserModelId(link, biz, profileId);

        ((IPersistFile)link).Save(lnkPath, true);
        return lnkPath;
    }


    /// <summary>
    /// 为快捷方式写入一个区别于运行进程的独立 AppUserModelID。
    /// </summary>
    private static void TrySetShortcutAppUserModelId(IShellLinkW link, GameBiz biz, string? profileId)
    {
        try
        {
            if (link is not IPropertyStore store)
            {
                return;
            }
            string aumid = $"Starward.GameShortcut.{biz}";
            if (!string.IsNullOrEmpty(profileId))
            {
                aumid += "." + profileId;
            }
            PROPERTYKEY key = PKEY_AppUserModel_ID;
            InitPropVariantFromString(aumid, out PROPVARIANT pv);
            try
            {
                store.SetValue(ref key, ref pv);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }
        catch
        {
            // 设置 AUMID 失败不影响快捷方式本身的生成。
        }
    }


    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }


    #region 游戏图标


    /// <summary>
    /// 该游戏快捷方式使用的 .ico 图标（src 目录下的 bh3/hk4e/hkrpg/nap.ico，随程序部署在 exe 同目录 Assets\Image 下）。
    /// 仅四个主要游戏有对应 .ico，其它返回 null（快捷方式回退到 Starward 程序图标）。
    /// </summary>
    public static IconSource? GetGameIconSource(GameBiz biz)
    {
        string? fileName = biz.Game switch
        {
            GameBiz.bh3 => "bh3.ico",
            GameBiz.hk4e => "hk4e.ico",
            GameBiz.hkrpg => "hkrpg.ico",
            GameBiz.nap => "nap.ico",
            _ => null,
        };
        if (fileName is null)
        {
            return null;
        }
        string? dir = Path.GetDirectoryName(AppConfig.StarwardExecutePath);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }
        string path = Path.Combine(dir, "Assets", "Image", fileName);
        return File.Exists(path) ? new IconSource { Path = path, Index = 0 } : null;
    }


    #endregion


    #region P/Invoke


    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }


    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, nint pfd, uint fFlags);
        void GetIDList(out nint ppidl);
        void SetIDList(nint pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(nint hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }


    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }


    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);
        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig]
        int Commit();
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }


    // PROPVARIANT 在 64 位下为 24 字节；显式指定大小避免 InitPropVariantFromString 写越界。
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public nint pointerValue;
    }


    // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
    private static PROPERTYKEY PKEY_AppUserModel_ID => new PROPERTYKEY
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };


    [DllImport("propsys.dll", PreserveSig = false)]
    private static extern void InitPropVariantFromString([MarshalAs(UnmanagedType.LPWStr)] string psz, out PROPVARIANT ppropvar);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);


    #endregion


}
