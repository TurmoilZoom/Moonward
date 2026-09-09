using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using Windows.Foundation;
using Windows.Graphics;


namespace Starward.Features.Update;

/// <summary>
/// 检查更新结果的轻量提示：贴着托盘菜单弹出、不抢焦点、几秒后自动消失、点击可提前关闭。
/// <para>
/// 纯托盘驻留时主窗口可能压根没创建，<c>InAppToast.MainWindow</c> 为 <see langword="null"/>；
/// 托盘窗口自身又是「刚好包住菜单」的小窗口，任何 Popup 类提示都会被窗口边界裁掉，故用独立小窗口承载。
/// </para>
/// </summary>
[INotifyPropertyChanged]
public sealed partial class UpdateTipWindow : WindowEx
{

    /// <summary>卡片最大逻辑宽度。长错误文案靠它换行，而不是把窗口拉成横贯屏幕的一条。</summary>
    private const double TipMaxWidth = 340;

    /// <summary>无人打扰时的停留时长；指针悬停在卡片上会暂停计时。</summary>
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(4);

    /// <summary>当前提示窗实例，同一时刻只应有一个；窗口关闭后置空。只在 UI 线程访问。</summary>
    private static UpdateTipWindow? _window;

    private readonly ILogger<UpdateTipWindow> _logger = AppConfig.GetLogger<UpdateTipWindow>();

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

    private Visual? _cardVisual;


    public UpdateTipWindow()
    {
        InitializeComponent();
        InitializeWindow();
        _timer = DispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Interval = DisplayDuration;
        _timer.Tick += OnAutoCloseTick;
        // 不能删除，防止在 SW_SHOWNOACTIVATE 显示后没有文字
        this.Bindings.Update();
        this.Closed += UpdateTipWindow_Closed;
    }


    private void InitializeWindow()
    {
        SystemBackdrop = new TransparentBackdrop();
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            // SetBorderAndTitleBar(false, false) 之后仍会残留 WS_DLGFRAME 画出的一像素边框，去掉才是真无边框
            User32.WindowStyles style = (User32.WindowStyles)User32.GetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE);
            style &= ~User32.WindowStyles.WS_DLGFRAME;
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE, (nint)style);
            User32.WindowStylesEx styleEx = (User32.WindowStylesEx)User32.GetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE);
            styleEx |= User32.WindowStylesEx.WS_EX_TOPMOST;
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE, (nint)styleEx);
        }
    }


    /// <summary>
    /// 在托盘菜单原位弹出一条提示。同一时刻只有一个提示窗，重复调用复用同一实例并重置自动关闭计时。
    /// 只能在 UI 线程调用。
    /// </summary>
    /// <param name="title">主文本，例如「已更新到最新版」或错误信息。</param>
    /// <param name="detail">次要文本，例如当前版本号；为空则不显示该行。</param>
    /// <param name="isError">true 时用红色错误图标。</param>
    /// <param name="anchor">托盘菜单的屏幕矩形，提示窗以它的右下角为锚点弹出。</param>
    public static void Show(string title, string? detail, bool isError, RectInt32 anchor)
    {
        try
        {
            // 窗口关闭后 AppWindow 变 null，据此判断要不要重建（同 ScreenCaptureService 对截图信息窗的处理）
            if (_window?.AppWindow is null)
            {
                _window = new UpdateTipWindow();
            }
            _window.ShowTip(title, detail, isError, anchor);
        }
        catch (Exception ex)
        {
            // 提示本身失败不该影响检查更新的主流程；丢弃实例让下次重建
            AppConfig.GetLogger<UpdateTipWindow>().LogError(ex, "Show update tip");
            _window = null;
        }
    }


    public string TipTitle { get; private set => SetProperty(ref field, value); } = "";


    public string? TipDetail
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasDetail));
            }
        }
    }


    public bool HasDetail => !string.IsNullOrEmpty(TipDetail);


    public bool IsError { get; private set => SetProperty(ref field, value); }


    /// <summary>
    /// 供 x:Bind 函数绑定用的 bool→Visibility 映射。
    /// 不用 <c>BoolToVisibilityConverter</c>：Window 根上的 x:Bind 取不到 StaticResource 转换器。
    /// 也不能声明成 <c>static</c> —— x:Bind 生成的代码用实例引用调用它（CS0176）。
    /// </summary>
    private Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;


    /// <inheritdoc cref="ToVisibility"/>
    private Visibility ToVisibilityReversed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>
    /// 设好文案后按内容测量尺寸、贴到托盘菜单原位，并以不抢焦点的方式显示。
    /// </summary>
    private void ShowTip(string title, string? detail, bool isError, RectInt32 anchor)
    {
        try
        {
            bool wasVisible = AppWindow.IsVisible;
            RootGrid.RequestedTheme = ShouldSystemUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
            TipTitle = title;
            TipDetail = detail;
            IsError = isError;
            this.Bindings.Update();

            Size desired = MeasureTip();
            if (desired.Width < 1 || desired.Height < 1)
            {
                // 首次显示时 XAML 树还没实例化，Measure 会拿到空尺寸：先挪到屏幕外以不激活的方式显示一次把树建起来再量。
                // 这里是异步续体而非输入事件，改窗口位置是安全的（在指针/点击事件里同步改布局再挪窗口会 WinUI fail-fast 0xC000027B）。
                AppWindow.MoveAndResize(new RectInt32(-32000, -32000, 1, 1));
                AppWindow.Show(false);
                desired = MeasureTip();
            }
            if (desired.Width < 1 || desired.Height < 1)
            {
                _logger.LogWarning("Update tip measured empty size, skip showing.");
                return;
            }

            // 锚点取托盘菜单的右下角，并沿用菜单自己那套弹出定位（见 SystemTrayWindow.Show）：
            // 提示就落在刚才菜单所在的位置，任务栏停靠在屏幕哪一边都对，越界由系统翻转回工作区内。
            POINT anchorPoint = new(anchor.X + anchor.Width, anchor.Y + anchor.Height);
            double scale = GetScaleForPoint(anchorPoint);
            SIZE size = new()
            {
                Width = Math.Max(1, (int)Math.Ceiling(desired.Width * scale)),
                Height = Math.Max(1, (int)Math.Ceiling(desired.Height * scale)),
            };
            User32.CalculatePopupWindowPosition(anchorPoint,
                                                size,
                                                User32.TrackPopupMenuFlags.TPM_RIGHTALIGN | User32.TrackPopupMenuFlags.TPM_BOTTOMALIGN | User32.TrackPopupMenuFlags.TPM_WORKAREA,
                                                null,
                                                out RECT position);
            AppWindow.MoveAndResize(new RectInt32(position.X, position.Y, position.Width, position.Height));
            AppWindow.Show(false);
            if (!wasVisible)
            {
                StartShowAnimation();
            }

            _timer.Stop();
            _timer.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show update tip");
        }
    }


    /// <summary>
    /// 以逻辑像素测量卡片，宽度封顶后长文案换行。
    /// </summary>
    /// <returns>卡片的期望尺寸（逻辑像素，与 DPI 无关）。</returns>
    private Size MeasureTip()
    {
        // 复用同一实例时文案已经换过，显式作废上次的测量结果，避免 Measure 直接返回缓存尺寸
        RootGrid.InvalidateMeasure();
        RootGrid.Measure(new Size(TipMaxWidth, double.PositiveInfinity));
        return RootGrid.DesiredSize;
    }


    /// <summary>
    /// 取锚点所在显示器的缩放。用目标显示器的 DPI 换算，而不是本窗口的 <see cref="WindowEx.UIScale"/>：
    /// <see cref="MeasureTip"/> 得到的是与 DPI 无关的逻辑像素，而窗口此刻可能还停在另一块缩放不同的屏上。
    /// </summary>
    /// <param name="point">锚点的屏幕坐标。</param>
    /// <returns>目标显示器的缩放系数；取不到时回退本窗口的缩放。</returns>
    private double GetScaleForPoint(POINT point)
    {
        try
        {
            DisplayArea area = DisplayArea.GetFromPoint(new PointInt32(point.X, point.Y), DisplayAreaFallback.Nearest);
            nint monitor = Win32Interop.GetMonitorFromDisplayId(area.DisplayId);
            if (monitor != 0 && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) is 0 && dpiX is not 0)
            {
                return dpiX / 96d;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get dpi for update tip");
        }
        // 回退成常数 1 会在高缩放的屏上把卡片截掉一大块，用本窗口的缩放至少是同一量级
        return UIScale;
    }


    [LibraryImport("Shcore.dll")]
    private static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);


    /// <summary>
    /// 进场淡入。窗口尺寸正好等于卡片，任何位移动画都会被窗口矩形切出直边（圆角与描边被削平），故只做透明度变化。
    /// </summary>
    private void StartShowAnimation()
    {
        try
        {
            _cardVisual ??= ElementCompositionPreview.GetElementVisual(CardBorder);
            ScalarKeyFrameAnimation animation = _cardVisual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0f, 0f);
            animation.InsertKeyFrame(1f, 1f);
            animation.Duration = TimeSpan.FromMilliseconds(200);
            _cardVisual.StartAnimation(nameof(_cardVisual.Opacity), animation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update tip show animation");
        }
    }


    /// <summary>隐藏提示但保留实例，下次显示直接复用。</summary>
    private void HideTip()
    {
        _timer.Stop();
        AppWindow?.Hide();
    }


    private void OnAutoCloseTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        HideTip();
    }


    /// <summary>指针停在卡片上时不消失，方便读完较长的错误信息。</summary>
    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _timer.Stop();
    }


    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (AppWindow?.IsVisible == true)
        {
            _timer.Start();
        }
    }


    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        HideTip();
    }


    private void UpdateTipWindow_Closed(object sender, WindowEventArgs args)
    {
        _timer.Stop();
        _timer.Tick -= OnAutoCloseTick;
        if (ReferenceEquals(_window, this))
        {
            _window = null;
        }
        this.Closed -= UpdateTipWindow_Closed;
    }


}
