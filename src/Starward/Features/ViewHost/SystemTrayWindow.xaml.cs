using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Starward.Features.GameRecord.SignIn;
using Starward.Features.Overlay;
using Starward.Features.Screenshot;
using Starward.Features.Setting;
using Starward.Features.Startup;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using Windows.Foundation;


namespace Starward.Features.ViewHost;

/// <summary>
/// 系统托盘窗口。除了托盘图标与右键菜单，它还是**常驻实例的宿主**：
/// 全局热键注册在本窗口上、<c>WM_HOTKEY</c> 由本窗口分发，快捷方式启动游戏的跨进程通知也由此接收。
/// <para>
/// 选它而不是主窗口，是因为它是常驻实例中唯一必然存在（<see cref="App.EnsureMainWindow"/> 与
/// <see cref="App.EnsureSystemTray"/> 两条路径都会创建）且永不销毁（<c>AppWindow.Closing</c> 恒取消）的窗口。
/// 挂在主窗口上会导致仅托盘驻留或快捷方式启动时热键完全缺席，见 issue #10。
/// </para>
/// </summary>
[INotifyPropertyChanged]
public sealed partial class SystemTrayWindow : WindowEx
{




    public SystemTrayWindow()
    {
        this.InitializeComponent();
        InitializeWindow();
        SetTrayIcon();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => this.Bindings.Update());
        // 常驻实例的对外通道与后台职责：先开 IPC 监听（快捷方式进程要靠它找到本实例），再拉起热键/手柄等
        ResidentInstanceMessenger.StartListening();
        ResidentHost.Start(WindowHandle, DispatcherQueue);
    }




    private unsafe void InitializeWindow()
    {
        new SystemBackdropHelper(this, SystemBackdropProperty.AcrylicDefault with
        {
            TintColorLight = 0xFFE7E7E7,
            TintColorDark = 0xFF404040
        }).TrySetAcrylic(true);

        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (s, e) => e.Cancel = true;
        this.Activated += SystemTrayWindow_Activated;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var flag = User32.GetWindowLongPtr(WindowHandle, User32.WindowLongFlags.GWL_STYLE);
        flag &= ~(nint)User32.WindowStyles.WS_CAPTION;
        flag &= ~(nint)User32.WindowStyles.WS_BORDER;
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE, flag);
        ApplyTopMostStyle();
        var p = DwmApi.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        DwmApi.DwmSetWindowAttribute(WindowHandle, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (nint)(&p), sizeof(DwmApi.DWM_WINDOW_CORNER_PREFERENCE));

        // 只创建 HWND，不走右键弹出路径（避免启动时抢前台或改溢出层）
        base.Show();
        Hide();
    }



    private void SetTrayIcon()
    {
        try
        {
            nint hInstance = Kernel32.GetModuleHandle(null).DangerousGetHandle();
            nint hIcon = User32.LoadIcon(hInstance, "#32512").DangerousGetHandle();
            trayIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
        }
        catch { }
    }




    private void SystemTrayWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState is WindowActivationState.Deactivated)
        {
            Hide();
        }
    }



    [RelayCommand]
    public override void Show()
    {
        // 设置页改键/删键不会通知托盘，每次弹出时兜底重读一遍
        RefreshHotkeyStates();
        RootGrid.RequestedTheme = ShouldSystemUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
        RootGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SIZE windowSize = new()
        {
            Width = (int)(RootGrid.DesiredSize.Width * UIScale),
            Height = (int)(RootGrid.DesiredSize.Height * UIScale)
        };
        User32.GetCursorPos(out POINT point);
        User32.CalculatePopupWindowPosition(point, windowSize, User32.TrackPopupMenuFlags.TPM_RIGHTALIGN | User32.TrackPopupMenuFlags.TPM_BOTTOMALIGN | User32.TrackPopupMenuFlags.TPM_WORKAREA, null, out RECT windowPos);
        User32.MoveWindow(WindowHandle, windowPos.X, windowPos.Y, windowPos.Width, windowPos.Height, true);
        ApplyTopMostStyle();

        HWND overflow = FindVisibleOverflow();
        // 只有右键落在溢出层里才叠上去。图标在任务栏上时，点图标等于点了溢出层外面，
        // Explorer 会收起溢出层；若此时把溢出层设成 owner，菜单会跟着一起被关掉。
        if ((nint)overflow != 0 && IsPointInWindow(overflow, point))
        {
            SetNoActivate(true);
            SetOverflowOwner(overflow);
            ShowWithoutActivating();
            StackAboveOverflow(overflow);
            StartOverflowOverlayWatch();
        }
        else
        {
            SetNoActivate(false);
            ClearOverflowOwner();
            StopOverflowOverlayWatch();
            base.Show();
        }
    }



    [RelayCommand]
    public override void Hide()
    {
        if (_hiding)
        {
            return;
        }
        _hiding = true;
        try
        {
            StopOverflowOverlayWatch();
            ClearOverflowOwner();
            SetNoActivate(false);
            base.Hide();
        }
        finally
        {
            _hiding = false;
        }
    }


    /// <summary>
    /// 写入 <c>WS_EX_TOPMOST</c> 并用 <c>SWP_FRAMECHANGED</c> 让样式生效。
    /// <see cref="OverlappedPresenter.IsAlwaysOnTop"/> 在 <c>Hide</c>/<c>Show</c> 后不一定还在，弹出前要重申。
    /// </summary>
    private void ApplyTopMostStyle()
    {
        try
        {
            nint ex = User32.GetWindowLongPtr(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE);
            ex |= (nint)User32.WindowStylesEx.WS_EX_TOPMOST;
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE, ex);
            User32.SetWindowPos(
                WindowHandle,
                HWND.HWND_TOPMOST,
                0, 0, 0, 0,
                User32.SetWindowPosFlags.SWP_NOMOVE
                | User32.SetWindowPosFlags.SWP_NOSIZE
                | User32.SetWindowPosFlags.SWP_NOACTIVATE
                | User32.SetWindowPosFlags.SWP_FRAMECHANGED);
        }
        catch { }
    }


    /// <summary>
    /// 查找当前可见的系统托盘溢出层。Win10 为 <c>NotifyIconOverflowWindow</c>，Win11 22H2+ 为 XAML Island。
    /// </summary>
    /// <returns>可见溢出窗的 HWND；未打开时为空。</returns>
    private static HWND FindVisibleOverflow()
    {
        HWND hwnd = User32.FindWindow("NotifyIconOverflowWindow", null);
        if ((nint)hwnd != 0 && User32.IsWindowVisible(hwnd))
        {
            return hwnd;
        }
        hwnd = User32.FindWindow("TopLevelWindowForOverflowXamlIsland", null);
        if ((nint)hwnd != 0 && User32.IsWindowVisible(hwnd))
        {
            return hwnd;
        }
        return default;
    }


    /// <summary>
    /// 不抢前台地显示菜单。激活会让 Explorer 把溢出层 light-dismiss 掉。
    /// </summary>
    private void ShowWithoutActivating()
    {
        AppWindow.Show(false);
        User32.ShowWindow(WindowHandle, ShowWindowCommand.SW_SHOWNOACTIVATE);
        User32.SetWindowPos(
            WindowHandle,
            HWND.HWND_TOPMOST,
            0, 0, 0, 0,
            User32.SetWindowPosFlags.SWP_NOMOVE
            | User32.SetWindowPosFlags.SWP_NOSIZE
            | User32.SetWindowPosFlags.SWP_NOACTIVATE
            | User32.SetWindowPosFlags.SWP_SHOWWINDOW);
    }


    /// <summary>
    /// 开关 <c>WS_EX_NOACTIVATE</c>。盖在溢出层上时必须打开，否则点菜单项也会把溢出层关掉。
    /// </summary>
    /// <param name="noActivate">为 <see langword="true"/> 时窗口可点但不会成为前台。</param>
    private void SetNoActivate(bool noActivate)
    {
        try
        {
            nint ex = User32.GetWindowLongPtr(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE);
            if (noActivate)
            {
                ex |= (nint)User32.WindowStylesEx.WS_EX_NOACTIVATE;
            }
            else
            {
                ex &= ~(nint)User32.WindowStylesEx.WS_EX_NOACTIVATE;
            }
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE, ex);
            User32.SetWindowPos(
                WindowHandle,
                HWND.HWND_TOPMOST,
                0, 0, 0, 0,
                User32.SetWindowPosFlags.SWP_NOMOVE
                | User32.SetWindowPosFlags.SWP_NOSIZE
                | User32.SetWindowPosFlags.SWP_NOACTIVATE
                | User32.SetWindowPosFlags.SWP_FRAMECHANGED);
        }
        catch { }
    }


    /// <summary>
    /// 把溢出层设为本窗 owner。owned 窗口在 z-order 上永远高于 owner，才能盖住 Shell 溢出层且不关掉它。
    /// 只在菜单可见期间设置：owner 销毁会连带销毁 owned 窗口，而本窗是常驻热键宿主。
    /// </summary>
    /// <param name="overflow">当前可见的溢出层 HWND。</param>
    private void SetOverflowOwner(HWND overflow)
    {
        try
        {
            _overflowHwnd = overflow;
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_HWNDPARENT, (nint)overflow);
        }
        catch { }
    }


    /// <summary>
    /// 解开与溢出层的 owner 关系。须在 <see cref="Hide"/> 里先于关窗调用。
    /// </summary>
    private void ClearOverflowOwner()
    {
        try
        {
            _overflowHwnd = default;
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_HWNDPARENT, 0);
        }
        catch { }
    }


    /// <summary>
    /// 插到溢出层正上方（<c>GW_HWNDPREV</c> 是溢出层前面那一扇窗）。
    /// </summary>
    /// <param name="overflow">溢出层 HWND。</param>
    private void StackAboveOverflow(HWND overflow)
    {
        try
        {
            HWND prev = User32.GetWindow(overflow, User32.GetWindowCmd.GW_HWNDPREV);
            if (prev == (HWND)WindowHandle)
            {
                return;
            }
            HWND insertAfter = (nint)prev == 0 ? HWND.HWND_TOPMOST : prev;
            User32.SetWindowPos(
                WindowHandle,
                insertAfter,
                0, 0, 0, 0,
                User32.SetWindowPosFlags.SWP_NOMOVE
                | User32.SetWindowPosFlags.SWP_NOSIZE
                | User32.SetWindowPosFlags.SWP_NOACTIVATE);
        }
        catch { }
    }


    /// <summary>
    /// 盖在溢出层上时：周期性把 z-order 按回去（Explorer 会把溢出层抬上来），并用鼠标钩子点外侧关闭。
    /// 不激活所以没有 <c>Deactivated</c>。
    /// </summary>
    private void StartOverflowOverlayWatch()
    {
        // 打开菜单的那次右键可能尚未完全结束，钩子先忽略按键未抬起的点击，避免刚弹出就被关掉
        _ignoreOutsideClicks = true;
        _mouseHookProc ??= MouseHookProc;
        if (_mouseHook.IsNull)
        {
            _mouseHook = User32.SetWindowsHookEx(
                User32.HookType.WH_MOUSE_LL,
                _mouseHookProc,
                Kernel32.GetModuleHandle(null),
                0);
        }
        _zOrderTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _zOrderTimer.Tick -= OnOverflowZOrderTick;
        _zOrderTimer.Tick += OnOverflowZOrderTick;
        _zOrderTimer.Start();
    }


    /// <summary>
    /// 停止溢出层叠放监视与点外侧钩子。
    /// </summary>
    private void StopOverflowOverlayWatch()
    {
        if (_zOrderTimer is not null)
        {
            _zOrderTimer.Stop();
            _zOrderTimer.Tick -= OnOverflowZOrderTick;
        }
        if (!_mouseHook.IsNull)
        {
            User32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = default;
        }
    }


    private void OnOverflowZOrderTick(object? sender, object e)
    {
        if ((nint)_overflowHwnd == 0 || !User32.IsWindow(_overflowHwnd) || !User32.IsWindowVisible(_overflowHwnd))
        {
            // 溢出层被 Explorer 收起时不要连带关菜单（否则任务栏图标右键会两个一起没）
            DetachOverflowKeepMenu();
            return;
        }
        StackAboveOverflow(_overflowHwnd);
    }


    /// <summary>
    /// 溢出层已关掉：解开 owner，菜单继续留着，并恢复可激活，以便点外侧走 <c>Deactivated</c> 关闭。
    /// </summary>
    private void DetachOverflowKeepMenu()
    {
        StopOverflowOverlayWatch();
        ClearOverflowOwner();
        SetNoActivate(false);
        try
        {
            AppWindow.Show(true);
            User32.SetForegroundWindow(WindowHandle);
        }
        catch { }
    }


    private nint MouseHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            if (_ignoreOutsideClicks)
            {
                if (!AnyMouseButtonDown())
                {
                    _ignoreOutsideClicks = false;
                }
            }
            else
            {
                uint msg = (uint)wParam;
                if (msg is (uint)User32.WindowMessage.WM_LBUTTONDOWN
                    or (uint)User32.WindowMessage.WM_RBUTTONDOWN
                    or (uint)User32.WindowMessage.WM_MBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<User32.MSLLHOOKSTRUCT>(lParam);
                    if (!IsPointInWindow(WindowHandle, info.pt))
                    {
                        DispatcherQueue.TryEnqueue(Hide);
                    }
                }
            }
        }
        return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }


    /// <summary>
    /// 左/右/中键是否仍按着。弹出瞬间用来跳过尚未结束的那次右键。
    /// </summary>
    private static bool AnyMouseButtonDown()
    {
        const short pressed = unchecked((short)0x8000);
        return (User32.GetAsyncKeyState((int)User32.VK.VK_LBUTTON) & pressed) != 0
            || (User32.GetAsyncKeyState((int)User32.VK.VK_RBUTTON) & pressed) != 0
            || (User32.GetAsyncKeyState((int)User32.VK.VK_MBUTTON) & pressed) != 0;
    }


    /// <summary>
    /// 屏幕坐标是否落在指定窗口矩形内（含非客户区）。
    /// </summary>
    /// <param name="hwnd">目标窗口。</param>
    /// <param name="pt">屏幕坐标。</param>
    /// <returns>在窗口矩形内为 <see langword="true"/>。</returns>
    private static bool IsPointInWindow(HWND hwnd, POINT pt)
    {
        if ((nint)hwnd == 0)
        {
            return false;
        }
        User32.GetWindowRect(hwnd, out RECT rect);
        return User32.PtInRect(rect, pt);
    }


    private bool _hiding;

    private bool _ignoreOutsideClicks;

    private HWND _overflowHwnd;

    /// <summary>须持有委托，否则钩子回调时已被 GC。</summary>
    private User32.HookProc? _mouseHookProc;

    private User32.HHOOK _mouseHook;

    private DispatcherTimer? _zOrderTimer;



    [RelayCommand]
    public void ShowMainWindow()
    {
        Hide();
        App.Current.EnsureMainWindow();
    }


    /// <summary>
    /// 「显示主窗口」热键当前是否启用。托盘菜单里以绿/红状态灯显示。
    /// </summary>
    public bool ShowMainWindowHotkeyEnabled { get; private set => SetProperty(ref field, value); }
        = HotkeyManager.IsEnabled(HotkeyManager.ShowMainWindow.Id);


    /// <summary>
    /// 「游戏截图」热键当前是否启用。托盘菜单里以绿/红状态灯显示。
    /// </summary>
    public bool ScreenshotHotkeyEnabled { get; private set => SetProperty(ref field, value); }
        = HotkeyManager.IsEnabled(HotkeyManager.ScreenshotCapture.Id);


    /// <summary>「显示主窗口」当前按键的可读文本，显示在行尾键帽里。</summary>
    public string ShowMainWindowHotkeyText { get; private set => SetProperty(ref field, value); }
        = HotkeyManager.GetHotkeyText(HotkeyManager.ShowMainWindow.Id);


    /// <summary>「游戏截图」当前按键的可读文本，显示在行尾键帽里。</summary>
    public string ScreenshotHotkeyText { get; private set => SetProperty(ref field, value); }
        = HotkeyManager.GetHotkeyText(HotkeyManager.ScreenshotCapture.Id);


    /// <summary>
    /// 供 x:Bind 函数绑定用的 bool→Visibility 映射。
    /// 不用 <c>BoolToVisibilityConverter</c>：Window 根上的 x:Bind 取不到 StaticResource 转换器。
    /// 也不能声明成 <c>static</c> —— x:Bind 生成的代码用实例引用调用它（CS0176）。
    /// </summary>
    private Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;


    /// <inheritdoc cref="ToVisibility"/>
    private Visibility ToVisibilityReversed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>关闭状态下把键帽淡化，与状态灯一起表达「这个键当前不生效」。</summary>
    private double ToOpacity(bool enabled) => enabled ? 1 : 0.4;


    /// <summary>
    /// 从 <see cref="HotkeyManager"/> 重新读取两个热键的启用状态与按键文本。
    /// 每次弹出菜单时调用：用户可能刚在设置页改过键，托盘收不到通知，靠这里兜底刷新。
    /// </summary>
    private void RefreshHotkeyStates()
    {
        ShowMainWindowHotkeyEnabled = HotkeyManager.IsEnabled(HotkeyManager.ShowMainWindow.Id);
        ScreenshotHotkeyEnabled = HotkeyManager.IsEnabled(HotkeyManager.ScreenshotCapture.Id);
        ShowMainWindowHotkeyText = HotkeyManager.GetHotkeyText(HotkeyManager.ShowMainWindow.Id);
        ScreenshotHotkeyText = HotkeyManager.GetHotkeyText(HotkeyManager.ScreenshotCapture.Id);
    }


    /// <summary>
    /// 切换「显示主窗口」热键：立即注册/注销并持久化，无需重启。
    /// </summary>
    [RelayCommand]
    private void ToggleShowMainWindowHotkey()
    {
        int id = HotkeyManager.ShowMainWindow.Id;
        HotkeyManager.SetEnabled(id, !HotkeyManager.IsEnabled(id));
        RefreshHotkeyStates();
    }


    /// <summary>
    /// 切换「游戏截图」热键：立即注册/注销并持久化，无需重启。
    /// </summary>
    [RelayCommand]
    private void ToggleScreenshotHotkey()
    {
        int id = HotkeyManager.ScreenshotCapture.Id;
        HotkeyManager.SetEnabled(id, !HotkeyManager.IsEnabled(id));
        RefreshHotkeyStates();
    }


    [RelayCommand]
    private void Exit()
    {
        App.Current.Exit();
    }


    private void WindowEx_Closed(object sender, WindowEventArgs args)
    {
        StopOverflowOverlayWatch();
        ClearOverflowOwner();
        ResidentInstanceMessenger.StopListening();
        trayIcon?.Dispose();
    }



    /// <summary>
    /// 全局热键分发。热键注册在本窗口上（见 <see cref="HotkeyManager.OwnerHandle"/>），
    /// 故 <c>WM_HOTKEY</c> 也投递到这里，而不是主窗口 —— 主窗口可能压根没创建。
    /// </summary>
    protected override unsafe IntPtr WindowSubclassProc(HWND hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == (uint)User32.WindowMessage.WM_HOTKEY)
        {
            if (wParam == 44444)
            {
                // 全局热键：打开游戏内覆盖层，失败则显示主窗口
                if (!RunningGameService.OpenOverlayWindow())
                {
                    App.Current.EnsureMainWindow();
                }
            }
            else if (wParam == 44445)
            {
                // 截图
                ScreenCaptureService.Capture();
            }
        }
        else if (uMsg == (uint)User32.WindowMessage.WM_POWERBROADCAST)
        {
            // 广播给所有顶层窗口，无需 RegisterPowerSettingNotification。不吞消息。
            var power = (User32.PowerBroadcastType)(int)wParam;
            if (power is User32.PowerBroadcastType.PBT_APMRESUMESUSPEND
                or User32.PowerBroadcastType.PBT_APMRESUMEAUTOMATIC
                or User32.PowerBroadcastType.PBT_APMRESUMECRITICAL)
            {
                AppConfig.GetService<AutoSignInService>().NotifySystemResumed();
            }
        }
        return base.WindowSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
    }


}
