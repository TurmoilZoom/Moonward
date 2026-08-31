using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using Starward.Core;
using Starward.Features.GameLauncher;
using Starward.Features.Overlay;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Starward.Features.Startup;

/// <summary>
/// 常驻实例（系统托盘）与「快捷方式启动游戏」短命进程之间的单向通知通道。
/// <para>
/// 常驻侧创建一个 message-only 窗口（父窗口为 <c>HWND_MESSAGE</c>），客户端用 <c>FindWindowEx</c> 找到它后
/// 以 <c>WM_COPYDATA</c> 投递「游戏已启动（game_biz, pid）」，常驻侧据此把游戏登记进
/// <see cref="RunningGameService"/>，全局热键截图与 GameBar 引导键接管才能生效。
/// </para>
/// <para>
/// 为什么不用 <c>AppInstance.RedirectActivationToAsync</c> 转发整个激活：免 UAC 快捷方式经计划任务以最高
/// 权限运行 Moonward，转发后游戏会改由普通权限的常驻实例以 <c>runas</c> 启动，UAC 弹窗回归，等于废掉免 UAC 功能。
/// </para>
/// <para>
/// 完整性级别：UIPI 允许「高 → 低」发送窗口消息，故提权的快捷方式进程可直接通知普通权限的托盘；
/// 反向（Moonward 本身以管理员运行、快捷方式进程为普通权限）需要接收方放行 <c>WM_COPYDATA</c>，
/// 见 <see cref="StartListening"/>。
/// </para>
/// </summary>
internal static partial class ResidentInstanceMessenger
{

    /// <summary>message-only 窗口的标题，客户端据此定位常驻实例。窗口句柄本身按会话隔离，无需再加用户/会话后缀。</summary>
    private const string WindowName = "Moonward.ResidentIpc";

    /// <summary>借用系统预定义的 STATIC 窗口类，省去注册自定义窗口类；行为由子类化过程接管。</summary>
    private const string WindowClass = "STATIC";

    private const uint WM_COPYDATA = 0x004A;

    /// <summary>message-only 窗口的父窗口伪句柄。</summary>
    private const nint HWND_MESSAGE = -3;

    /// <summary><c>COPYDATASTRUCT.dwData</c> 取值：游戏已启动，负载为 <c>{game_biz}|{pid}</c>。</summary>
    private const nint MSG_GAME_STARTED = 1;

    private const uint MSGFLT_ALLOW = 1;

    private const uint SMTO_ABORTIFHUNG = 0x0002;


    private static nint _windowHandle;

    /// <summary>子类化过程必须由托管侧持有引用，否则会被 GC 回收，回调时变成野指针。</summary>
    private static ComCtl32.SUBCLASSPROC? _subclassProc;


    /// <summary>
    /// 在当前线程（必须是有消息循环的 UI 线程）创建 message-only 窗口并开始接收通知。
    /// 幂等：重复调用只创建一次。
    /// </summary>
    public static void StartListening()
    {
        if (_windowHandle != 0)
        {
            return;
        }
        try
        {
            _windowHandle = CreateWindowEx(0, WindowClass, WindowName, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
            if (_windowHandle == 0)
            {
                Log.Warning("Create resident IPC window failed: {error}", Marshal.GetLastPInvokeError());
                return;
            }
            // 本进程以管理员运行时，普通权限的快捷方式进程发来的 WM_COPYDATA 会被 UIPI 拦下，需显式放行
            ChangeWindowMessageFilterEx(_windowHandle, WM_COPYDATA, MSGFLT_ALLOW, 0);
            _subclassProc = new ComCtl32.SUBCLASSPROC(SubclassProc);
            ComCtl32.SetWindowSubclass(_windowHandle, _subclassProc, 1003, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Start resident IPC listener");
        }
    }


    /// <summary>
    /// 销毁 message-only 窗口，停止接收通知。随常驻实例退出调用。
    /// </summary>
    public static void StopListening()
    {
        if (_windowHandle == 0)
        {
            return;
        }
        try
        {
            DestroyWindow(_windowHandle);
        }
        catch { }
        _windowHandle = 0;
        _subclassProc = null;
    }


    /// <summary>
    /// 当前系统会话中是否已有常驻实例在接收通知。
    /// </summary>
    public static bool IsResidentInstanceListening()
    {
        return FindResidentWindow() != 0;
    }


    /// <summary>
    /// 通知常驻实例：一个游戏进程已由本进程拉起，请登记为「运行中的游戏」。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="pid">游戏进程 Id。</param>
    /// <returns>已成功投递返回 <see langword="true"/>；未找到常驻实例或投递失败返回 <see langword="false"/>。</returns>
    public static bool NotifyGameStarted(GameBiz biz, int pid)
    {
        nint target = FindResidentWindow();
        if (target == 0)
        {
            return false;
        }
        nint buffer = 0;
        try
        {
            string payload = $"{biz}|{pid.ToString(CultureInfo.InvariantCulture)}";
            buffer = Marshal.StringToHGlobalUni(payload);
            var data = new COPYDATASTRUCT
            {
                dwData = MSG_GAME_STARTED,
                // 含结尾终止符，接收端按 cbData 反算字符数时一并裁掉
                cbData = (payload.Length + 1) * sizeof(char),
                lpData = buffer,
            };
            // WM_COPYDATA 必须同步发送（数据指针要在处理期间有效）；限时 3 秒，避免常驻实例卡住时拖住游戏启动
            SendMessageTimeout(target, WM_COPYDATA, 0, ref data, SMTO_ABORTIFHUNG, 3000, out _);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Notify resident instance that game started ({biz}, {pid})", biz, pid);
            return false;
        }
        finally
        {
            if (buffer != 0)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }


    private static nint FindResidentWindow()
    {
        try
        {
            return FindWindowEx(HWND_MESSAGE, 0, WindowClass, WindowName);
        }
        catch
        {
            return 0;
        }
    }


    private static IntPtr SubclassProc(HWND hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_COPYDATA)
        {
            try
            {
                COPYDATASTRUCT data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                if (data.dwData == MSG_GAME_STARTED && data.lpData != 0 && data.cbData > sizeof(char))
                {
                    string payload = Marshal.PtrToStringUni(data.lpData, data.cbData / sizeof(char) - 1) ?? "";
                    OnGameStartedMessageReceived(payload);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Handle resident IPC message");
            }
            return 1;
        }
        return ComCtl32.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }


    /// <summary>
    /// 处理「游戏已启动」通知：把该进程登记进 <see cref="RunningGameService"/>（顺带触发 GameBar 引导键接管），
    /// 并广播 <see cref="GameStartedMessage"/>，让主界面按「游戏启动后」设置作出响应。
    /// </summary>
    /// <param name="payload">负载，格式 <c>{game_biz}|{pid}</c>。</param>
    private static void OnGameStartedMessageReceived(string payload)
    {
        string[] parts = payload.Split('|');
        if (parts.Length != 2 || !GameBiz.TryParse(parts[0], out GameBiz biz) || !int.TryParse(parts[1], out int pid))
        {
            Log.Warning("Resident IPC: malformed game started payload {payload}", payload);
            return;
        }
        try
        {
            Process process = Process.GetProcessById(pid);
            RunningGameService.AddRuninngGame(biz, process);
            Log.Information("Resident IPC: game started by shortcut ({biz}, {pid})", biz, pid);
            WeakReferenceMessenger.Default.Send(new GameStartedMessage());
        }
        catch (Exception ex)
        {
            // 游戏进程可能已退出，或跨完整性级别打不开句柄；仅记录不打扰用户
            Log.Warning(ex, "Resident IPC: cannot track game process ({biz}, {pid})", biz, pid);
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public nint dwData;
        public int cbData;
        public nint lpData;
    }


    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
                                               int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint hWndParent, nint hWndChildAfter, string lpszClass, string lpszWindow);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static partial nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, ref COPYDATASTRUCT lParam,
                                                   uint fuFlags, uint uTimeout, out nint lpdwResult);

    [LibraryImport("user32.dll", EntryPoint = "ChangeWindowMessageFilterEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);

}
