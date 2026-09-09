using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.UI;

namespace Starward.Frameworks;

public abstract partial class WindowEx : Window
{


    public IntPtr WindowHandle { get; private init; }

    private IntPtr InputSiteHandle { get; init; }


    public double UIScale => User32.GetDpiForWindow(WindowHandle) / 96d;


    public static Microsoft.UI.WindowId MainWindowId { get; protected set; }



    public WindowEx()
    {
        WindowHandle = (IntPtr)AppWindow.Id.Value;
        HWND bridge = (IntPtr)User32.FindWindowEx(WindowHandle, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        InputSiteHandle = (IntPtr)User32.FindWindowEx(bridge, IntPtr.Zero, "InputSiteWindowClass", null);
        windowSubclassProc = new(WindowSubclassProc);
        inputSiteSubclassProc = new(InputSiteSubclassProc);
        ComCtl32.SetWindowSubclass(WindowHandle, windowSubclassProc, 1001, IntPtr.Zero);
        ComCtl32.SetWindowSubclass(InputSiteHandle, inputSiteSubclassProc, 1002, IntPtr.Zero);
    }




    #region Message Loop



    private readonly ComCtl32.SUBCLASSPROC windowSubclassProc;

    private readonly ComCtl32.SUBCLASSPROC inputSiteSubclassProc;



    protected virtual unsafe IntPtr WindowSubclassProc(HWND hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        return ComCtl32.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }



    protected virtual unsafe IntPtr InputSiteSubclassProc(HWND hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        return ComCtl32.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }




    #endregion




    #region Window Method



    public virtual void Show()
    {
        AppWindow.Show(true);
        AppWindow.MoveInZOrderAtTop();
        User32.ShowWindow(WindowHandle, ShowWindowCommand.SW_SHOWNORMAL);
        User32.SetForegroundWindow(WindowHandle);
    }



    public virtual void Hide()
    {
        AppWindow.Hide();
    }



    public virtual void Minimize()
    {
        User32.ShowWindow(WindowHandle, ShowWindowCommand.SW_MINIMIZE);
    }



    /// <summary>
    /// 取用于「窗口居中」的显示器：优先跟随主窗口，让新窗口出现在用户正在看的那块屏上。
    /// <para>
    /// <see cref="MainWindowId"/> 只在 <c>MainWindow</c> 构造时赋值。纯托盘驻留（<c>--hide</c>、快捷方式启动游戏）
    /// 与环境检查阶段（<c>WelcomeWindow</c> / <c>NoPermissionWindow</c>）主窗口尚未创建，它仍是默认值 0，
    /// 拿去查显示器得不到预期结果。此时不能退回本窗口自身 —— 新窗口还没定过位，会落在系统默认那块屏；
    /// 改用光标所在显示器，用户刚在那块屏上操作过。
    /// </para>
    /// </summary>
    /// <returns>用于居中计算的显示区域，恒不为 <see langword="null"/>。</returns>
    protected static DisplayArea GetDisplayAreaForCentering()
    {
        if (MainWindowId.Value is not 0)
        {
            return DisplayArea.GetFromWindowId(MainWindowId, DisplayAreaFallback.Nearest);
        }
        if (User32.GetCursorPos(out POINT point))
        {
            return DisplayArea.GetFromPoint(new PointInt32(point.X, point.Y), DisplayAreaFallback.Nearest);
        }
        return DisplayArea.Primary;
    }


    public virtual void CenterInScreen(int? width = null, int? height = null)
    {
        width = width <= 0 ? null : width;
        height = height <= 0 ? null : height;
        DisplayArea display = GetDisplayAreaForCentering();
        double scale = UIScale;
        int w = (int)((width * scale) ?? AppWindow.Size.Width);
        int h = (int)((height * scale) ?? AppWindow.Size.Height);
        int x = display.WorkArea.X + (display.WorkArea.Width - w) / 2;
        int y = display.WorkArea.Y + (display.WorkArea.Height - h) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }



    public void SetIcon(string? iconPath = null)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            nint hInstance = Kernel32.GetModuleHandle(null).DangerousGetHandle();
            nint hIcon = User32.LoadIcon(hInstance, "#32512").DangerousGetHandle();
            AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(hIcon));
        }
        else
        {
            AppWindow.SetIcon(iconPath);
        }
    }



    #endregion




    #region Theme



    public void SetDragRectangles(params RectInt32[] value)
    {
        if (AppWindowTitleBar.IsCustomizationSupported() && AppWindow.TitleBar.ExtendsContentIntoTitleBar == true)
        {
            AppWindow.TitleBar.SetDragRectangles(value);
        }
    }


    public void AdaptTitleBarButtonColorToActuallTheme()
    {
        if (AppWindowTitleBar.IsCustomizationSupported() && AppWindow.TitleBar.ExtendsContentIntoTitleBar == true)
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            if (Content is FrameworkElement element)
            {
                switch (element.ActualTheme)
                {
                    case ElementTheme.Default:
                        break;
                    case ElementTheme.Light:
                        titleBar.ButtonForegroundColor = Colors.Black;
                        titleBar.ButtonHoverForegroundColor = Colors.Black;
                        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, 0x00, 0x00, 0x00);
                        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x99, 0x99, 0x99);
                        break;
                    case ElementTheme.Dark:
                        titleBar.ButtonForegroundColor = Colors.White;
                        titleBar.ButtonHoverForegroundColor = Colors.White;
                        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
                        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x99, 0x99, 0x99);
                        break;
                    default:
                        break;
                }
            }
        }
    }



    public virtual void ChangeAccentColor(Color? backColor = null, Color? foreColor = null)
    {
        if (Content is FrameworkElement element)
        {
            if (element.ActualTheme is ElementTheme.Dark)
            {
                element.RequestedTheme = ElementTheme.Light;
                element.RequestedTheme = ElementTheme.Dark;
            }
            if (element.ActualTheme is ElementTheme.Light)
            {
                element.RequestedTheme = ElementTheme.Dark;
                element.RequestedTheme = ElementTheme.Light;
            }
        }
    }



    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("uxtheme.dll", EntryPoint = "#132", SetLastError = true)]
    protected static partial bool ShouldAppsUseDarkMode();


    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("uxtheme.dll", EntryPoint = "#138", SetLastError = true)]
    protected static partial bool ShouldSystemUseDarkMode();



    #endregion


}
