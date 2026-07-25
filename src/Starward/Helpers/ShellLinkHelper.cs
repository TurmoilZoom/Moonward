using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Starward.Helpers;

/// <summary>
/// 通过 COM <c>IShellLink</c> 写入 <c>.lnk</c> 快捷方式（不依赖额外 NuGet）。
/// 免 UAC 游戏快捷方式目标为 <c>schtasks.exe</c>（非安装目录），避免 Velopack 扫描并改写图标。
/// </summary>
internal static class ShellLinkHelper
{

    /// <summary>ShowWindow: 隐藏窗口（减轻 schtasks 控制台闪烁）。</summary>
    public const int SW_HIDE = 0;


    /// <summary>
    /// 在 <paramref name="linkPath"/> 创建或覆盖一个 .lnk。
    /// </summary>
    /// <param name="linkPath">.lnk 完整路径。</param>
    /// <param name="targetPath">目标可执行文件路径。</param>
    /// <param name="arguments">命令行参数；可为 null。</param>
    /// <param name="workingDirectory">工作目录；可为 null。</param>
    /// <param name="iconPath">图标文件路径；可为 null（使用目标默认图标）。</param>
    /// <param name="iconIndex">图标索引。</param>
    /// <param name="description">说明文字；可为 null。</param>
    /// <param name="showCmd">启动时 ShowWindow 常量，默认 <see cref="SW_HIDE"/>。</param>
    public static void Create(
        string linkPath,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? iconPath = null,
        int iconIndex = 0,
        string? description = null,
        int showCmd = SW_HIDE)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        object? com = null;
        try
        {
            com = new ShellLinkCoClass();
            var link = (IShellLinkW)com;
            link.SetPath(targetPath);
            if (!string.IsNullOrEmpty(arguments))
            {
                link.SetArguments(arguments);
            }
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                link.SetWorkingDirectory(workingDirectory);
            }
            if (!string.IsNullOrEmpty(iconPath))
            {
                link.SetIconLocation(iconPath, iconIndex);
            }
            if (!string.IsNullOrEmpty(description))
            {
                link.SetDescription(description);
            }
            link.SetShowCmd(showCmd);

            var file = (IPersistFile)com;
            file.Save(linkPath, true);
        }
        finally
        {
            if (com is not null && Marshal.IsComObject(com))
            {
                Marshal.FinalReleaseComObject(com);
            }
        }
    }


    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }


    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }


    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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

}
