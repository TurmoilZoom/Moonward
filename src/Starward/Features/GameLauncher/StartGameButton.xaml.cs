using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.RPC.GameInstall;
using System;
using System.Numerics;
using System.Windows.Input;
using Windows.Foundation;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 首页「开始游戏」胶囊按钮：主操作区、汉堡快速启动菜单、安装进度展示，以及 Composition 动效编排入口。
/// 动效实现见 <see cref="StartGameButtonEffects"/>；本类负责状态绑定、悬停 Popup 与菜单开合动画。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class StartGameButton : UserControl
{


    private static Brush AccentFillColorDefaultBrush => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    private static Brush TextOnAccentFillColorDisabled => (Brush)Application.Current.Resources["TextOnAccentFillColorDisabledBrush"];
    private static Brush TextOnAccentFillColorPrimaryBrush => (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];


    /// <summary>胶囊按钮的呼吸光晕 / 流光 / 聚光 / 点击光爆 Composition 动效。</summary>
    private readonly StartGameButtonEffects _effects = new();


    /// <summary>初始化 XAML、订阅主题变化与 Loaded/Unloaded 生命周期。</summary>
    public StartGameButton()
    {
        this.InitializeComponent();
        this.ActualThemeChanged += StartGameButton_ActualThemeChanged;
        this.Loaded += StartGameButton_Loaded;
        this.Unloaded += StartGameButton_Unloaded;
    }


    /// <summary>
    /// 控件载入视觉树后挂接动效宿主、初始化快速菜单关闭定时器并订阅菜单事件。
    /// </summary>
    /// <param name="sender">事件源，即本控件。</param>
    /// <param name="e">路由事件参数。</param>
    private void StartGameButton_Loaded(object sender, RoutedEventArgs e)
    {
        _effects.Attach(Grid_Root, Grid_GlowHost, Grid_EffectHost, Button_GameAction);
        UpdateEffectsState();
        _menuCloseTimer = DispatcherQueue.CreateTimer();
        _menuCloseTimer.Interval = TimeSpan.FromMilliseconds(120);
        _menuCloseTimer.Tick += MenuCloseTimer_Tick;
        QuickMenu.RequestClose += CloseQuickMenu;
        QuickMenu.ChildPopupOpenChanged += QuickMenu_ChildPopupOpenChanged;
    }


    /// <summary>
    /// 同步动效启用状态：呼吸光晕仅在「可开始游戏」时亮；
    /// 流光 / 聚光灯 / 点击光爆在所有显示强调色背景的可操作状态（开始 / 安装 / 更新 等）都亮。
    /// </summary>
    private void UpdateEffectsState()
    {
        _effects.SetState(GameState is GameState.StartGame, IsAccentColorBackgroundVisible);
    }


    /// <summary>
    /// 控件卸载时释放动效、停止定时器并取消菜单相关事件订阅，避免泄漏与空转。
    /// </summary>
    /// <param name="sender">事件源，即本控件。</param>
    /// <param name="e">路由事件参数。</param>
    private void StartGameButton_Unloaded(object sender, RoutedEventArgs e)
    {
        _effects.Detach();
        if (_menuCloseTimer is not null)
        {
            _menuCloseTimer.Stop();
            _menuCloseTimer.Tick -= MenuCloseTimer_Tick;
        }
        DisableMenuPointerTracking();
        QuickMenu.RequestClose -= CloseQuickMenu;
        QuickMenu.ChildPopupOpenChanged -= QuickMenu_ChildPopupOpenChanged;
        Popup_GameInfoOrDownloadProgress.IsOpen = false;
    }




    #region 快速启动悬停菜单


    /// <summary>指针离开按钮与菜单区域后的延迟关闭定时器（120ms），避免在缝隙间误关。</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _menuCloseTimer;

    /// <summary>指针是否位于汉堡按钮上。</summary>
    private bool _pointerOverMenuButton;

    /// <summary>指针是否位于快速菜单 Popup 内容上。</summary>
    private bool _pointerOverMenuPopup;

    /// <summary>菜单内子级 Flyout（如下拉选择）是否打开；打开期间禁止自动关闭。</summary>
    private bool _menuChildPopupOpen;

    /// <summary>快速菜单是否正在播放收起动画（动画结束后才真正关闭 Popup）。</summary>
    private bool _quickMenuClosing;

    /// <summary>窗口根元素，用于全局 PointerMoved 追踪按钮与菜单之间的移动路径。</summary>
    private UIElement? _menuPointerTrackingRoot;

    /// <summary>
    /// 打开后的悬停宽限截止时间。WinUI 在 <see cref="Popup.IsOpen"/> 变为 true 时，
    /// 常对触发源合成 <c>PointerExited</c>（光标其实仍在按钮上）；宽限期内忽略自动关闭，
    /// 否则 120ms 定时器会在指针未移动时把刚打开的菜单关掉，表现为「要试几次才弹出」。
    /// </summary>
    private DateTimeOffset _menuHoverGuardUntil;

    /// <summary>打开 Popup 后忽略合成 PointerExited 的宽限时长。</summary>
    private static readonly TimeSpan MenuHoverGuardDuration = TimeSpan.FromMilliseconds(300);


    /// <summary>
    /// 打开快速启动菜单：首次打开时自底边向上平铺展开；已打开或正在收起时则取消收起。
    /// </summary>
    private void OpenQuickMenu()
    {
        _menuCloseTimer?.Stop();
        // 悬停/点击打开时指针应在按钮上；打开 Popup 后可能被合成 Exited 清掉，先置位并开宽限。
        _pointerOverMenuButton = true;
        _menuHoverGuardUntil = DateTimeOffset.UtcNow.Add(MenuHoverGuardDuration);

        if (!Popup_QuickMenu.IsOpen)
        {
            UpdateQuickMenuMaxHeight();
            QuickMenu.CurrentGameId = CurrentGameId;
            QuickMenu.OnOpening();
            _quickMenuClosing = false;
            // UIElement.Opacity 保持 1，仅用 Composition 做开合透明度，避免与 XAML Opacity 相乘导致偶发不可见。
            QuickMenuRoot.Opacity = 1;
            // 打开前先压成底部一条细线并隐藏，避免开场闪现整块（宽度保持满宽，仅高度塌缩）。
            Visual v = ElementCompositionPreview.GetElementVisual(QuickMenuRoot);
            v.Scale = new Vector3(1f, 0.01f, 1f);
            v.Opacity = 0;
            Popup_QuickMenu.IsOpen = true;
            PlayQuickMenuOpenAnimation(fromSeed: true);
        }
        else
        {
            // 已打开（或正在收起）时重新悬停：若在收起则展开回去，否则无操作。
            CancelQuickMenuClose();
        }

        SettingButtonPointerOver = true;
        EnableMenuPointerTracking();

        // Popup 打开会立刻改命中树；延后一拍纠正可能已被合成 PointerExited 清掉的标志，并取消误启的关闭定时器。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!Popup_QuickMenu.IsOpen)
            {
                return;
            }

            _pointerOverMenuButton = true;
            _menuCloseTimer?.Stop();
        });
    }


    /// <summary>
    /// 按窗口可用空间限制菜单高度，避免在底部区域弹出时超出上边界。
    /// </summary>
    private void UpdateQuickMenuMaxHeight()
    {
        if (XamlRoot is null || Button_Menu.ActualWidth <= 0)
        {
            ScrollViewer_QuickMenu.MaxHeight = double.PositiveInfinity;
            return;
        }

        var transform = Button_Menu.TransformToVisual(null);
        var buttonBounds = transform.TransformBounds(new Rect(0, 0, Button_Menu.ActualWidth, Button_Menu.ActualHeight));

        const double margin = 16;
        double spaceAbove = buttonBounds.Top - margin;
        double spaceBelow = XamlRoot.Size.Height - buttonBounds.Bottom - margin;
        double maxHeight = Math.Max(spaceAbove, spaceBelow);

        ScrollViewer_QuickMenu.MaxHeight = maxHeight > 120 ? maxHeight : double.PositiveInfinity;
    }


    /// <summary>
    /// 开始关闭快速菜单：若 Popup 已打开且未在收起中，则播放向下收起动画。
    /// </summary>
    private void CloseQuickMenu()
    {
        _menuCloseTimer?.Stop();
        if (!Popup_QuickMenu.IsOpen || _quickMenuClosing)
        {
            return;
        }

        _menuChildPopupOpen = false;
        _quickMenuClosing = true;
        PlayQuickMenuCloseAnimation();
    }


    /// <summary>
    /// 收起动画结束后真正关闭 Popup，并复位指针追踪与汉堡按钮悬停状态。
    /// </summary>
    private void FinalizeCloseQuickMenu()
    {
        _quickMenuClosing = false;
        DisableMenuPointerTracking();
        Popup_QuickMenu.IsOpen = false;
        SettingButtonPointerOver = false;
    }


    /// <summary>
    /// 取消正在进行的收起动画，从当前高度平滑向上展开回原始大小。
    /// </summary>
    private void CancelQuickMenuClose()
    {
        if (!_quickMenuClosing)
        {
            return;
        }

        _quickMenuClosing = false;
        // 从当前高度继续向上展开（不从底部细线重新开始，避免跳变）。
        PlayQuickMenuOpenAnimation(fromSeed: false);
        SettingButtonPointerOver = true;
    }


    /// <summary>启动延迟关闭定时器，在指针离开按钮与菜单后短暂等待再尝试关闭。</summary>
    private void ScheduleCloseQuickMenu()
    {
        // 宽限期内的「离开」多半是打开 Popup 时的合成事件，此时指针通常仍停在按钮上且未必再触发 Moved。
        if (DateTimeOffset.UtcNow < _menuHoverGuardUntil)
        {
            return;
        }

        _menuCloseTimer?.Stop();
        _menuCloseTimer?.Start();
    }


    /// <summary>
    /// 延迟关闭定时器到期：仅当指针不在按钮、菜单且子 Flyout 未打开时才关闭菜单。
    /// </summary>
    /// <param name="sender">定时器实例。</param>
    /// <param name="args">Tick 事件参数。</param>
    private void MenuCloseTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (DateTimeOffset.UtcNow < _menuHoverGuardUntil)
        {
            return;
        }

        if (!_pointerOverMenuButton && !_pointerOverMenuPopup && !_menuChildPopupOpen)
        {
            CloseQuickMenu();
        }
    }


    // 「从下往上平铺展开 + 淡入 / 向下收起 + 淡出」开合动效：宽度始终满宽，仅沿 Y 轴从底部锚点向上拉动（非放射放大）。
    // 缓出/缓入曲线与时长仍对齐 FluentAnimations.ExpandContractFlyout（300ms 开 / 150ms 关）。

    /// <summary>
    /// 种子缩放：X 保持 1（满宽平铺），Y 压到约 20px 且不超过原高度 1%，等效于底部一条细线。
    /// </summary>
    private const string QuickMenuSeedScaleExpression =
        "Vector3(1.0, Min(0.01, 20.0 / this.Target.Size.Y), 1.0)";

    /// <summary>缩放原点固定在底边中心，使菜单从汉堡按钮一侧自下向上生长。</summary>
    private const string QuickMenuBottomCenterExpression =
        "Vector3(this.Target.Size.X * 0.5, this.Target.Size.Y, 0)";


    /// <summary>
    /// 展开：自底边中心沿 Y 轴从细线拉满到原始高度，并同步淡入（300ms 缓出，(0.1,0.9)(0.2,1)）；X 始终为 1。
    /// </summary>
    /// <param name="fromSeed">
    /// 为 <see langword="true"/> 时从底部细线 + 透明起播；为 <see langword="false"/> 时从当前高度/透明度继续展开（收起途中重新悬停）。
    /// </param>
    private void PlayQuickMenuOpenAnimation(bool fromSeed)
    {
        Visual v = ElementCompositionPreview.GetElementVisual(QuickMenuRoot);

        v.StopAnimation(nameof(Visual.CenterPoint));
        ExpressionAnimation center = v.Compositor.CreateExpressionAnimation(QuickMenuBottomCenterExpression);
        v.StartAnimation(nameof(Visual.CenterPoint), center);

        // 先停掉收起中的透明度动画，再播淡入；fromSeed 时从 0 起，取消收起时从当前透明度续播，避免跳变。
        v.StopAnimation(nameof(Visual.Opacity));

        if (!EntranceAnimation.AnimationsEnabled())
        {
            v.StopAnimation(nameof(Visual.Scale));
            v.Scale = Vector3.One;
            v.Opacity = 1;
            return;
        }

        CubicBezierEasingFunction entrance = v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        Vector3KeyFrameAnimation scale = v.Compositor.CreateVector3KeyFrameAnimation();
        if (fromSeed)
        {
            scale.InsertExpressionKeyFrame(0f, QuickMenuSeedScaleExpression);
        }
        scale.InsertKeyFrame(1f, Vector3.One, entrance);
        scale.Duration = TimeSpan.FromMilliseconds(300);
        v.StartAnimation(nameof(Visual.Scale), scale);

        ScalarKeyFrameAnimation opacity = v.Compositor.CreateScalarKeyFrameAnimation();
        if (fromSeed)
        {
            opacity.InsertKeyFrame(0f, 0f);
        }
        opacity.InsertKeyFrame(1f, 1f, entrance);
        opacity.Duration = TimeSpan.FromMilliseconds(300);
        v.StartAnimation(nameof(Visual.Opacity), opacity);
    }


    /// <summary>
    /// 收起：沿 Y 轴收缩回底边细线并同步淡出（150ms 缓入，(0.7,0)(1,0.5)），结束后真正关闭 Popup。
    /// </summary>
    private void PlayQuickMenuCloseAnimation()
    {
        Visual v = ElementCompositionPreview.GetElementVisual(QuickMenuRoot);

        if (!EntranceAnimation.AnimationsEnabled())
        {
            FinalizeCloseQuickMenu();
            return;
        }

        // 收起时同样锚定底边，避免收缩时上下两侧同时内收。
        v.StopAnimation(nameof(Visual.CenterPoint));
        ExpressionAnimation center = v.Compositor.CreateExpressionAnimation(QuickMenuBottomCenterExpression);
        v.StartAnimation(nameof(Visual.CenterPoint), center);

        CompositionScopedBatch batch = v.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        CubicBezierEasingFunction exit = v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));
        Vector3KeyFrameAnimation scale = v.Compositor.CreateVector3KeyFrameAnimation();
        // 不固定 0f 起点，从当前高度收缩，避免「开场未结束就收起」时跳变。
        scale.InsertExpressionKeyFrame(1f, QuickMenuSeedScaleExpression, exit);
        scale.Duration = TimeSpan.FromMilliseconds(150);
        v.StartAnimation(nameof(Visual.Scale), scale);

        // 与高度收缩同步淡出（从当前透明度起，避免开场未结束就关时跳变）。
        ScalarKeyFrameAnimation opacity = v.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, 0f, exit);
        opacity.Duration = TimeSpan.FromMilliseconds(150);
        v.StartAnimation(nameof(Visual.Opacity), opacity);

        batch.Completed += (_, _) =>
        {
            // Composition 完成回调不保证在 UI 线程；改 Popup.IsOpen 须回到 DispatcherQueue。
            DispatcherQueue.TryEnqueue(() =>
            {
                // 若中途被重新悬停取消了收起（_quickMenuClosing=false），则不真正关闭。
                if (_quickMenuClosing)
                {
                    FinalizeCloseQuickMenu();
                }
            });
        };
        batch.End();
    }


    /// <summary>
    /// 菜单内子 Flyout 开关变化：子级打开时暂停自动关闭；关闭后假定指针仍在菜单内以便继续操作。
    /// </summary>
    /// <param name="isOpen">子 Flyout 是否打开。</param>
    private void QuickMenu_ChildPopupOpenChanged(bool isOpen)
    {
        _menuChildPopupOpen = isOpen;
        _menuCloseTimer?.Stop();
        if (!isOpen)
        {
            // 下拉关闭后 PointerEntered 不会重触发，先假定仍在菜单内以便继续操作（如点「应用」）。
            _pointerOverMenuPopup = true;
        }
    }


    /// <summary>在窗口根元素上订阅 PointerMoved，用于检测指针在按钮与菜单之间的移动路径。</summary>
    private void EnableMenuPointerTracking()
    {
        if (_menuPointerTrackingRoot is not null || XamlRoot?.Content is not UIElement root)
        {
            return;
        }

        _menuPointerTrackingRoot = root;
        root.PointerMoved += MenuPointerTrackingRoot_PointerMoved;
    }


    /// <summary>取消窗口根元素上的 PointerMoved 订阅。</summary>
    private void DisableMenuPointerTracking()
    {
        if (_menuPointerTrackingRoot is null)
        {
            return;
        }

        _menuPointerTrackingRoot.PointerMoved -= MenuPointerTrackingRoot_PointerMoved;
        _menuPointerTrackingRoot = null;
    }


    /// <summary>
    /// 全局指针移动：更新悬停标记；在按钮或菜单上则保持打开，否则调度延迟关闭。
    /// </summary>
    /// <param name="sender">窗口根元素。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void MenuPointerTrackingRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!Popup_QuickMenu.IsOpen || _menuChildPopupOpen)
        {
            return;
        }

        bool overButton = IsPointerOverElement(Button_Menu, e);
        bool overMenu = IsPointerOverElement(QuickMenuRoot, e);

        _pointerOverMenuButton = overButton;
        _pointerOverMenuPopup = overMenu;

        if (overButton || overMenu)
        {
            _menuCloseTimer?.Stop();
            CancelQuickMenuClose();
        }
        else
        {
            ScheduleCloseQuickMenu();
        }
    }


    /// <summary>
    /// 判断指针是否位于指定元素的边界矩形内。
    /// </summary>
    /// <param name="element">待检测的框架元素；宽高为 0 时视为不在其上。</param>
    /// <param name="e">指针路由事件参数。</param>
    /// <returns>指针在元素范围内为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
    private static bool IsPointerOverElement(FrameworkElement element, PointerRoutedEventArgs e)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        Point point = e.GetCurrentPoint(element).Position;
        return point.X >= 0 && point.Y >= 0
            && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }


    /// <summary>汉堡按钮点击：切换快速菜单的打开/关闭。</summary>
    /// <param name="sender">汉堡按钮。</param>
    /// <param name="e">路由事件参数。</param>
    private void Button_Menu_Click(object sender, RoutedEventArgs e)
    {
        if (Popup_QuickMenu.IsOpen)
        {
            CloseQuickMenu();
        }
        else
        {
            OpenQuickMenu();
        }
    }


    /// <summary>指针进入汉堡按钮：打开快速菜单。</summary>
    /// <param name="sender">汉堡按钮。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Button_Menu_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverMenuButton = true;
        OpenQuickMenu();
    }


    /// <summary>
    /// 指针离开汉堡按钮：调度延迟关闭（若指针移入菜单则由全局追踪保持打开）。
    /// <para>
    /// 注意：将 <see cref="Popup.IsOpen"/> 设为 true 时，WinUI 常对触发按钮合成一次
    /// <c>PointerExited</c>，此时坐标仍可能落在按钮矩形内；必须忽略，否则菜单会闪一下关掉。
    /// </para>
    /// </summary>
    /// <param name="sender">汉堡按钮。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Button_Menu_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 合成 Exited：光标其实还在按钮上
        if (IsPointerOverElement(Button_Menu, e))
        {
            _pointerOverMenuButton = true;
            return;
        }

        // 已移入菜单（含按钮与菜单之间的命中），保持打开
        if (Popup_QuickMenu.IsOpen && IsPointerOverElement(QuickMenuRoot, e))
        {
            _pointerOverMenuButton = false;
            _pointerOverMenuPopup = true;
            _menuCloseTimer?.Stop();
            return;
        }

        // 打开后的宽限：合成 Exited 有时给出按钮外的坐标，但指针并未真正离开
        if (DateTimeOffset.UtcNow < _menuHoverGuardUntil)
        {
            return;
        }

        _pointerOverMenuButton = false;
        ScheduleCloseQuickMenu();
    }


    /// <summary>指针进入快速菜单：停止关闭定时器并取消正在进行的收起动画。</summary>
    /// <param name="sender">菜单根 Border。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void QuickMenu_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverMenuPopup = true;
        _menuCloseTimer?.Stop();
        CancelQuickMenuClose();
    }


    /// <summary>指针离开快速菜单：调度延迟关闭。</summary>
    /// <param name="sender">菜单根 Border。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void QuickMenu_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 移回汉堡按钮时不要关
        if (IsPointerOverElement(Button_Menu, e))
        {
            _pointerOverMenuPopup = false;
            _pointerOverMenuButton = true;
            _menuCloseTimer?.Stop();
            return;
        }

        // 仍在菜单矩形内（子控件切换时的瞬时 Exited）则忽略
        if (IsPointerOverElement(QuickMenuRoot, e))
        {
            _pointerOverMenuPopup = true;
            return;
        }

        _pointerOverMenuPopup = false;
        ScheduleCloseQuickMenu();
    }


    #endregion


    /// <summary>主操作按钮点击时执行的命令，由 <see cref="GameLauncherPage"/> 绑定。</summary>
    public ICommand GameCommand { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 当前游戏，由首页绑定传入，转交悬停快速启动菜单。
    /// </summary>
    public GameId CurrentGameId { get; set => SetProperty(ref field, value); }


    /// <summary>游戏运行中时按钮上显示的已运行时长文本（hh:mm:ss），由页面每秒刷新。</summary>
    public string? RunningGameTime { get; set { field = value; OnPropertyChanged(nameof(StartGameButtonText)); } }


    /// <summary>游戏运行中时悬停 Popup 显示的信息文本（如进程/时长等）。</summary>
    public string? RunningGameInfo { get; set => SetProperty(ref field, value); }



    /// <summary>按钮所处游戏状态（开始 / 运行中 / 安装 / 更新等），变化时刷新文案、可用性与动效。</summary>
    public GameState GameState { get; set { if (SetProperty(ref field, value)) UpdateActionButtonState(); } }


    /// <summary>主操作区是否处于指针悬停，用于切换前景色与安装态悬停文案。</summary>
    public bool ActionButtonPointerOver { get; set { if (SetProperty(ref field, value)) UpdateActionButtonState(); } }


    /// <summary>汉堡设置按钮是否处于指针悬停，用于切换其前景色。</summary>
    public bool SettingButtonPointerOver { get; set { if (SetProperty(ref field, value)) UpdateButtonForeground(); } }


    /// <summary>当前是否处于安装中状态（<see cref="GameState.Installing"/>）。</summary>
    public bool GameStateIsInstalling => GameState is GameState.Installing;


    /// <summary>
    /// 是否显示强调色背景：按钮可用且非安装中（开始 / 安装游戏 / 更新等 CTA 状态）。
    /// </summary>
    public bool IsAccentColorBackgroundVisible => Button_GameAction.IsEnabled && GameState is not GameState.Installing;


    /// <summary>命令执行中（按钮禁用且非「游戏运行中」）时显示左侧转圈 ProgressRing。</summary>
    public bool IsGameActionCommandRunning => !Button_GameAction.IsEnabled && GameState is not GameState.GameIsRunning;


    /// <summary>根据 <see cref="GameState"/> 返回主按钮中央文案（已本地化）。</summary>
    public string StartGameButtonText => GameState switch
    {
        GameState.StartGame => Lang.LauncherPage_StartGame,
        GameState.GameIsRunning => RunningGameTime ?? "",
        GameState.InstallGame => Lang.LauncherPage_InstallGame,
        GameState.UpdateGame => Lang.LauncherPage_UpdateGame,
        GameState.UpdatePlugin => "Update Plugins",
        GameState.Installing => "",
        GameState.ResumeDownload => Lang.StartGameButton_ResumeDownload,
        GameState.ComingSoon => "Coming Soon",
        _ => "",
    };


    /// <summary>
    /// 主操作按钮前景色：禁用时为弱化色；无强调色底时悬停高亮强调色，有强调色底时用强调色上的主文字色。
    /// </summary>
    public Brush ActionButtonForeground => (Button_GameAction.IsEnabled, IsAccentColorBackgroundVisible, ActionButtonPointerOver) switch
    {
        (false, _, _) => TextOnAccentFillColorDisabled,
        (true, false, true) => AccentFillColorDefaultBrush,
        (true, false, false) => TextOnAccentFillColorDisabled,
        _ => TextOnAccentFillColorPrimaryBrush
    };


    /// <summary>汉堡按钮前景色：无强调色底时悬停高亮强调色，否则用强调色上的主/弱化文字色。</summary>
    public Brush SettingButtonForeground => (IsAccentColorBackgroundVisible, SettingButtonPointerOver) switch
    {
        (false, true) => AccentFillColorDefaultBrush,
        (false, false) => TextOnAccentFillColorDisabled,
        _ => TextOnAccentFillColorPrimaryBrush
    };



    /// <summary>
    /// <see cref="GameState"/> 变化时统一刷新按钮可用性、绑定属性、前景色与 Composition 动效状态。
    /// </summary>
    private void UpdateActionButtonState()
    {
        Button_GameAction.IsEnabled = GameState is not GameState.GameIsRunning and not GameState.ComingSoon;
        OnPropertyChanged(nameof(GameStateIsInstalling));
        if (GameStateIsInstalling)
        {
            UpdateActionButtonStateWhenInstalling();
        }
        OnPropertyChanged(nameof(IsAccentColorBackgroundVisible));
        OnPropertyChanged(nameof(IsGameActionCommandRunning));
        OnPropertyChanged(nameof(StartGameButtonText));
        UpdateButtonForeground();
        UpdateEffectsState();
        // 进入 Installing 即固定打开进度框；离开安装态时关闭（运行中改由悬停控制）。
        UpdateGameInfoOrDownloadProgressPopup();
    }


    /// <summary>
    /// 安装中固定展示进度信息框；游戏运行中仅在指针悬停根区域时展示；其余状态关闭。
    /// </summary>
    /// <param name="pointerOverRoot">
    /// 指针是否位于胶囊根区域。仅影响 <see cref="GameState.GameIsRunning"/>：
    /// 传入时按悬停开关；为 <see langword="null"/> 时不改运行中 Popup 状态
    /// （避免 <see cref="UpdateActionButtonState"/> 因按钮悬停刷新而误关）。
    /// </param>
    private void UpdateGameInfoOrDownloadProgressPopup(bool? pointerOverRoot = null)
    {
        if (GameState is GameState.Installing)
        {
            Popup_GameInfoOrDownloadProgress.IsOpen = true;
        }
        else if (GameState is GameState.GameIsRunning)
        {
            if (pointerOverRoot.HasValue)
            {
                Popup_GameInfoOrDownloadProgress.IsOpen = pointerOverRoot.Value;
            }
        }
        else
        {
            Popup_GameInfoOrDownloadProgress.IsOpen = false;
        }
    }



    /// <summary>通知 XAML 刷新主按钮与汉堡按钮的前景色绑定。</summary>
    private void UpdateButtonForeground()
    {
        OnPropertyChanged(nameof(ActionButtonForeground));
        OnPropertyChanged(nameof(SettingButtonForeground));
    }



    /// <summary>
    /// 主按钮 <see cref="UIElement.IsEnabled"/> 变化时刷新强调色可见性、转圈状态、前景色与动效。
    /// </summary>
    /// <param name="sender">主操作按钮。</param>
    /// <param name="e">依赖属性变更参数。</param>
    private void Button_GameAction_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsAccentColorBackgroundVisible));
        OnPropertyChanged(nameof(IsGameActionCommandRunning));
        OnPropertyChanged(nameof(ActionButtonForeground));
        OnPropertyChanged(nameof(SettingButtonForeground));
        UpdateEffectsState();
    }



    /// <summary>
    /// 胶囊根 Grid 或主按钮指针进入：运行中按悬停打开信息 Popup（安装中已固定展示）；主按钮进入时标记悬停。
    /// </summary>
    /// <param name="sender"><see cref="Grid_Root"/> 或 <see cref="Button_GameAction"/>。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Control_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender as Grid == Grid_Root)
        {
            UpdateGameInfoOrDownloadProgressPopup(pointerOverRoot: true);
        }
        else if (sender as Button == Button_GameAction)
        {
            ActionButtonPointerOver = true;
        }
    }


    /// <summary>
    /// 胶囊根 Grid 或主按钮指针离开：运行中关闭信息 Popup（安装中保持打开）；主按钮离开时取消悬停标记。
    /// </summary>
    /// <param name="sender"><see cref="Grid_Root"/> 或 <see cref="Button_GameAction"/>。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Control_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender as Grid == Grid_Root)
        {
            UpdateGameInfoOrDownloadProgressPopup(pointerOverRoot: false);
        }
        else if (sender as Button == Button_GameAction)
        {
            ActionButtonPointerOver = false;
        }

    }





    /// <summary>安装任务的细粒度状态，变化时刷新安装中 UI 绑定。</summary>
    public GameInstallState InstallState { get; set { if (SetProperty(ref field, value)) UpdateActionButtonStateWhenInstalling(); } }


    /// <summary>安装任务是否处于等待开始（不确定进度圈）。</summary>
    public bool InstallStateIsPending => InstallState is GameInstallState.Waiting;

    /// <summary>安装任务是否处于下载中。</summary>
    public bool InstallStateIsDownloading => InstallState is GameInstallState.Downloading;

    /// <summary>安装中且未悬停、正在下载时，「下载中 + 剩余时间」区块的不透明度（1 为显示）。</summary>
    public double Button_GameAction_DownloadingRemainTime_Opacity => !ActionButtonPointerOver && InstallState is GameInstallState.Downloading ? 1 : 0;

    /// <summary>安装中且未悬停、非下载时，状态文案区块的不透明度（1 为显示）。</summary>
    public double TextBlock_GameAction_InstallState_Opacity => !ActionButtonPointerOver && InstallState is not GameInstallState.Downloading ? 1 : 0;

    /// <summary>安装中悬停时，操作提示文案（暂停/继续/取消）的不透明度（1 为显示）。</summary>
    public double TextBlock_GameAction_PointerOver_Opacity => ActionButtonPointerOver ? 1 : 0;

    /// <summary>安装中未悬停时显示的状态文案（等待、解压、校验、暂停、错误等）。</summary>
    public string TextBlock_GameAction_InstallState_Text => (ActionButtonPointerOver, InstallState) switch
    {
        (false, GameInstallState.Waiting) => Lang.StartGameButton_Waiting,
        (false, GameInstallState.Decompressing) => Lang.DownloadGamePage_Decompressing,
        (false, GameInstallState.Merging) => Lang.DownloadGamePage_Merging,
        (false, GameInstallState.Verifying) => Lang.DownloadGamePage_Verifying,
        (false, GameInstallState.Paused) => Lang.DownloadGamePage_Paused,
        (false, GameInstallState.Error) => Lang.Common_Error,
        (false, GameInstallState.Queueing) => Lang.StartGameButton_InQueue,
        _ => ""
    };

    /// <summary>安装中悬停时显示的操作文案（暂停、继续、取消等）。</summary>
    public string TextBlock_GameAction_PointerOver_Text => (ActionButtonPointerOver, InstallState) switch
    {
        (true, GameInstallState.Waiting or GameInstallState.Downloading) => Lang.DownloadGamePage_Pause,
        (true, GameInstallState.Paused or GameInstallState.Error or GameInstallState.Queueing) => Lang.Common_Continue,
        (true, GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying) => Lang.Common_Cancel,
        _ => ""

    };

    /// <summary>安装状态的人类可读摘要，用于按钮内与悬停 Popup。</summary>
    public string InstallStateText => InstallState switch
    {
        GameInstallState.Waiting => Lang.StartGameButton_Waiting,
        GameInstallState.Downloading => Lang.DownloadGamePage_Downloading,
        GameInstallState.Decompressing => Lang.DownloadGamePage_Decompressing,
        GameInstallState.Merging => Lang.DownloadGamePage_Merging,
        GameInstallState.Verifying => Lang.DownloadGamePage_Verifying,
        GameInstallState.Paused => Lang.DownloadGamePage_Paused,
        GameInstallState.Finish => Lang.DownloadGamePage_Finished,
        GameInstallState.Error => Lang.DownloadGamePage_SomethingError,
        GameInstallState.Queueing => Lang.StartGameButton_InQueue,
        _ => "State Error"
    };


    /// <summary>通知 XAML 刷新所有与安装中状态相关的计算属性绑定。</summary>
    public void UpdateActionButtonStateWhenInstalling()
    {
        OnPropertyChanged(nameof(InstallState));
        OnPropertyChanged(nameof(InstallStateIsPending));
        OnPropertyChanged(nameof(InstallStateIsDownloading));
        OnPropertyChanged(nameof(Button_GameAction_DownloadingRemainTime_Opacity));
        OnPropertyChanged(nameof(TextBlock_GameAction_InstallState_Opacity));
        OnPropertyChanged(nameof(TextBlock_GameAction_PointerOver_Opacity));
        OnPropertyChanged(nameof(TextBlock_GameAction_InstallState_Text));
        OnPropertyChanged(nameof(TextBlock_GameAction_PointerOver_Text));
        OnPropertyChanged(nameof(InstallStateText));
    }




    /// <summary>安装进度环数值（0–100）。</summary>
    public int ProgressRingValue { get; set => SetProperty(ref field, value); }

    /// <summary>安装进度百分比文本，用于悬停 Popup。</summary>
    public string ProgressPercentText { get; set => SetProperty(ref field, value); }

    /// <summary>已下载/总下载字节数文本；无总量时为 <see langword="null"/>。</summary>
    public string? DownloadBytesText { get; set => SetProperty(ref field, value); }

    /// <summary>当前网络下载速度文本。</summary>
    public string? DownloadSpeedText { get; set => SetProperty(ref field, value); }

    /// <summary>已写入/总写入字节数文本。</summary>
    public string? InstallBytesText { get; set => SetProperty(ref field, value); }

    /// <summary>当前磁盘写入速度文本。</summary>
    public string? InstallSpeedText { get; set => SetProperty(ref field, value); }

    /// <summary>当前校验读取速度文本。</summary>
    public string? VerifySpeedText { get; set => SetProperty(ref field, value); }

    /// <summary>预计剩余时间文本；未知时为 <c>--:--:--</c>。</summary>
    public string? RemainTimeText { get; set => SetProperty(ref field, value); }

    /// <summary>安装任务错误信息，用于悬停 Popup。</summary>
    public string? ErrorMessage { get; set => SetProperty(ref field, value); }



    /// <summary>
    /// 根据 RPC 安装任务上下文更新安装状态、进度环、速度与悬停 Popup 文案。
    /// </summary>
    /// <param name="task">当前游戏的安装任务快照，不可为 <see langword="null"/>。</param>
    public void UpdateGameInstallTaskState(GameInstallContext task)
    {
        InstallState = task.State;
        DownloadBytesText = ToBytesText(task.Progress_DownloadFinishBytes, task.Progress_DownloadTotalBytes);
        InstallBytesText = ToBytesText(task.Progress_WriteFinishBytes, task.Progress_WriteTotalBytes);
        ErrorMessage = task.ErrorMessage;
        if (InstallState is GameInstallState.Downloading)
        {
            long total = task.Progress_DownloadTotalBytes;
            long finish = task.Progress_DownloadFinishBytes;
            double progress = (double)finish / total;
            DownloadSpeedText = ToSpeedText(task.NetworkDownloadSpeed);
            InstallSpeedText = ToSpeedText(task.StorageWriteSpeed);
            VerifySpeedText = ToSpeedText(task.StorageReadSpeed);
            RemainTimeText = ToRemainTimeText(task.RemainTimeSeconds);
            // Chunk 模式更新游戏：进度环按写入进度而非下载进度显示。
            if (task.Operation is GameInstallOperation.Update && task.DownloadMode is GameInstallDownloadMode.Chunk)
            {
                progress = (double)task.Progress_WriteFinishBytes / task.Progress_WriteTotalBytes;
                ProgressRingValue = (int)(progress * 100);
                ProgressPercentText = $"{progress:P1}";
            }
            else
            {
                ProgressRingValue = (int)(progress * 100);
                ProgressPercentText = $"{progress:P1}";
            }
        }
        else if (InstallState is GameInstallState.Decompressing or GameInstallState.Merging)
        {
            DownloadSpeedText = null;
            InstallSpeedText = null;
            VerifySpeedText = null;
            RemainTimeText = "--:--:--";
            ProgressRingValue = (int)(task.Progress_Percent * 100);
            ProgressPercentText = $"{task.Progress_Percent:P1}";
        }
        else if (InstallState is GameInstallState.Finish)
        {
            DownloadSpeedText = null;
            InstallBytesText = null;
            DownloadSpeedText = null;
            InstallSpeedText = null;
            VerifySpeedText = null;
            RemainTimeText = null;
            ErrorMessage = null;
            ProgressRingValue = 100;
            ProgressPercentText = "100%";
        }
        else
        {
            DownloadSpeedText = null;
            InstallSpeedText = null;
            VerifySpeedText = null;
            RemainTimeText = "--:--:--";
        }
        // 安装会话内始终固定展示；Finish 后 GameState 会离开 Installing 并由 UpdateActionButtonState 关闭。
        UpdateGameInfoOrDownloadProgressPopup();
    }


    /// <summary>
    /// 将已完成与总字节数格式化为 <c>xx/yy MB</c> 或 <c>xx/yy GB</c>。
    /// </summary>
    /// <param name="finish">已完成字节数。</param>
    /// <param name="total">总字节数；为 0 时返回 <see langword="null"/>。</param>
    /// <returns>格式化后的进度文本，或总量为 0 时的 <see langword="null"/>。</returns>
    private static string? ToBytesText(long finish, long total)
    {
        const double MB = 1 << 20;
        const double GB = 1 << 30;
        if (total == 0)
        {
            return null;
        }
        if (total >= GB)
        {
            return $"{finish / GB:F2}/{total / GB:F2} GB";
        }
        else
        {
            return $"{finish / MB:F2}/{total / MB:F2} MB";
        }
    }


    /// <summary>
    /// 将字节/秒格式化为 <c>KB/s</c> 或 <c>MB/s</c>。
    /// </summary>
    /// <param name="bytes">每秒字节数。</param>
    /// <returns>带单位的速度文本。</returns>
    private static string ToSpeedText(long bytes)
    {
        const double KB = 1 << 10;
        const double MB = 1 << 20;
        if (bytes >= MB)
        {
            return $"{bytes / MB:F2} MB/s";
        }
        else
        {
            return $"{bytes / KB:F2} KB/s";
        }
    }


    /// <summary>
    /// 将剩余秒数格式化为 <c>hh:mm:ss</c>；为 0 时返回占位 <c>--:--:--</c>。
    /// </summary>
    /// <param name="seconds">剩余秒数。</param>
    /// <returns>时间文本或占位符。</returns>
    private static string? ToRemainTimeText(long seconds)
    {
        if (seconds == 0)
        {
            return "--:--:--";
        }
        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }



    /// <summary>
    /// 明暗主题切换时刷新前景色绑定，并通知动效层更新高光/辉光颜色。
    /// </summary>
    /// <param name="sender">本控件。</param>
    /// <param name="args">主题变更事件参数。</param>
    private void StartGameButton_ActualThemeChanged(FrameworkElement sender, object args)
    {
        OnPropertyChanged(nameof(ActionButtonForeground));
        OnPropertyChanged(nameof(SettingButtonForeground));
        _effects.OnThemeChanged();
    }


}