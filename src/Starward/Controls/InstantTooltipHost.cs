using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;

namespace Starward.Controls;

/// <summary>
/// 每个 <see cref="XamlRoot"/> 共享一个 <see cref="Popup"/>，承载即时 Tooltip 的显示、定位与 Composition 动画。
/// <para>
/// 由 <see cref="InstantTooltip"/> 按窗口创建与释放；多锚点注册指针事件后，悬停时复用同一气泡改文案与偏移。
/// 无 XAML 模板：UI 在构造函数中代码搭建（Border + TextBlock，可选操作链接：右下角或紧跟正文）。
/// 指针可移入气泡本身（便于点击操作）；仅当锚点与气泡都离开时才隐藏。
/// </para>
/// </summary>
internal sealed class InstantTooltipHost
{
    /// <summary>入场起始缩放（缩放原点随方位靠近锚点一侧）。</summary>
    private const float InitialScale = 0.7f;

    /// <summary>入场动画时长（毫秒）。</summary>
    private const int ShowDurationMs = 500;

    /// <summary>退场动画时长（毫秒）。</summary>
    private const int HideDurationMs = 150;

    /// <summary>
    /// 带操作按钮的可交互气泡：指针离开锚点后、进入气泡前的宽限（毫秒）。
    /// 仅 <see cref="_currentHasAction"/> 时使用，普通文案提示仍立即延后一拍关闭，不影响其它挂接。
    /// </summary>
    private const int InteractiveHideGraceMs = 450;

    /// <summary>提示与锚点元素的间距（像素）。</summary>
    private const double Gap = 8;

    /// <summary>本宿主所属的视觉树根（Popup 挂载点）。</summary>
    private readonly XamlRoot _xamlRoot;

    /// <summary>用于延后隐藏判断与动画完成后回 UI 线程关 Popup。</summary>
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>承载提示内容的轻量弹出层；全窗口唯一实例。</summary>
    private readonly Popup _popup;

    /// <summary>提示气泡容器（亚克力背景、圆角、内边距）。</summary>
    private readonly Border _content;

    /// <summary>
    /// 正文 + 可选操作按钮的布局根。
    /// 使用 StackPanel：Collapsed 子项不参与 Spacing，纯文案时不会在底部留出操作行空隙。
    /// </summary>
    private readonly StackPanel _body;

    /// <summary>提示正文；行内操作时 Inlines 为「正文 + Hyperlink」。</summary>
    private readonly TextBlock _text;

    /// <summary>正文 Run，纯文案与行内操作共用。</summary>
    private readonly Run _labelRun = new();

    /// <summary>行内操作链接的可见文案。</summary>
    private readonly Run _inlineActionRun = new();

    /// <summary>紧跟正文的操作链接；<see cref="InstantTooltip.ActionInlineProperty"/> 为 true 时使用。</summary>
    private readonly Hyperlink _inlineActionLink;

    /// <summary>可选操作链接（右下角）；无 ActionText 或行内模式时折叠。</summary>
    private readonly HyperlinkButton _actionButton;

    /// <summary>驱动 scale / opacity 关键帧动画的 Composition 合成器。</summary>
    private readonly Compositor _compositor;

    /// <summary>解析 ThemeResource 时优先查此元素的 Resources，再回退 Application.Resources。</summary>
    private readonly FrameworkElement _themeSource;

    /// <summary>已注册指针事件的锚点集合；用于去重与 Dispose 时批量解绑。</summary>
    private readonly HashSet<FrameworkElement> _elements = [];

    /// <summary>
    /// 锚点 Visibility 回调令牌；元素折叠时不会 Unloaded，需单独解绑。
    /// </summary>
    private readonly Dictionary<FrameworkElement, long> _visibilityTokens = [];

    /// <summary>
    /// 按下处理（handledEventsToo）：Button 会把 PointerPressed 标成已处理，CLR 的 += 收不到。
    /// </summary>
    private readonly PointerEventHandler _pointerPressedHandler;

    /// <summary>
    /// 点击触发模式的「点击别处收起」处理（挂到 <see cref="_clickDismissRoot"/>）。
    /// </summary>
    private readonly PointerEventHandler _rootPointerPressedHandler;

    /// <summary>
    /// 气泡自身的按下处理（handledEventsToo）：操作链接会把 PointerPressed 标已处理，普通 += 收不到。
    /// </summary>
    private readonly PointerEventHandler _contentPointerPressedHandler;

    /// <summary>
    /// 指针是否仍在任一已注册锚点内。
    /// 相邻项切换时 Exited→Entered 之间短暂为 false，配合延后隐藏避免闪烁。
    /// </summary>
    private bool _pointerInsideAnyElement;

    /// <summary>指针是否在气泡内容上（含操作按钮），为 true 时不关闭 Popup。</summary>
    private bool _pointerInsidePopup;

    /// <summary>是否已排队/正在执行隐藏流程，防止重复触发退场动画。</summary>
    private bool _hideScheduled;

    /// <summary>可交互气泡专用的隐藏宽限定时器（仅 Action 提示使用）。</summary>
    private readonly DispatcherQueueTimer _interactiveHideTimer;

    /// <summary>当前正在展示 Tooltip 的锚点；注销该锚点时需立即隐藏。</summary>
    private FrameworkElement? _currentAnchor;

    /// <summary>当前展示是否带可点击操作（打开/关闭时通知外层）。</summary>
    private bool _currentHasAction;

    /// <summary>当前展示所用的方位（影响偏移与缩放中心）。</summary>
    private InstantTooltipPlacement _currentPlacement = InstantTooltipPlacement.Right;

    /// <summary>
    /// 为 true 时不响应指针进入、不打开气泡（拖拽滚动等外部场景通过 <see cref="SetSuppressed"/> 设置）。
    /// </summary>
    private bool _suppressed;

    /// <summary>
    /// 用户在锚点上按下后，在指针离开该锚点前不再自动显示 Tooltip（点击打开 Flyout 时指针仍在按钮上，不会 Exited）。
    /// </summary>
    private FrameworkElement? _dismissedUntilLeaveAnchor;

    /// <summary>当前展示是否由点击触发；为 true 时指针进出不再关闭气泡。</summary>
    private bool _currentIsClickTriggered;

    /// <summary>点击触发展示期间监听「点击别处」的元素；未展示时为 <see langword="null"/>。</summary>
    private UIElement? _clickDismissRoot;

    /// <summary>
    /// 正在处理锚点上的这次按下：同一次事件随后冒泡到根，不能当成「点击别处」把刚开的气泡关掉。
    /// </summary>
    private bool _clickToggleInProgress;


    /// <summary>当前是否无任何挂接元素（为 true 时 <see cref="InstantTooltip"/> 可释放本 Host）。</summary>
    public bool IsEmpty => _elements.Count == 0;

    /// <summary>本宿主所属的视觉树根（与字典键一致）。</summary>
    public XamlRoot XamlRoot => _xamlRoot;

    /// <summary>指针当前是否停在气泡上（外层弹层判断外部点击是否落在提示里）。</summary>
    public bool IsPointerOverPopup => _pointerInsidePopup;

    /// <summary>指针是否仍在锚点或气泡内（用于延后隐藏）。</summary>
    private bool IsPointerOverTooltipSurface => _pointerInsideAnyElement || _pointerInsidePopup;


    /// <summary>
    /// 为指定视觉树根创建 Tooltip 宿主（搭建 Popup 视觉树，默认不打开）。
    /// </summary>
    /// <param name="xamlRoot">用于 Popup 挂载的 XamlRoot。</param>
    /// <param name="themeSource">用于解析 ThemeResource 与取得 <see cref="DispatcherQueue"/> 的元素。</param>
    public InstantTooltipHost(XamlRoot xamlRoot, FrameworkElement themeSource)
    {
        _xamlRoot = xamlRoot;
        _dispatcherQueue = themeSource.DispatcherQueue;
        _themeSource = themeSource;

        _text = new TextBlock
        {
            // 跟随系统文字缩放会改变测量尺寸，定位易抖，故关闭
            IsTextScaleFactorEnabled = false,
            MaxWidth = 280,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush"),
        };

        _inlineActionLink = new Hyperlink
        {
            UnderlineStyle = UnderlineStyle.None,
            Foreground = GetThemeBrush("AccentTextFillColorPrimaryBrush"),
        };
        _inlineActionLink.Inlines.Add(_inlineActionRun);
        _inlineActionLink.Click += InlineActionLink_Click;

        _actionButton = new HyperlinkButton
        {
            Padding = new Thickness(0),
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 12,
            Visibility = Visibility.Collapsed,
            Foreground = GetThemeBrush("AccentTextFillColorPrimaryBrush"),
        };
        _actionButton.Click += ActionButton_Click;

        // Spacing 仅作用于可见子项；Grid.RowSpacing 在第二行 Collapsed 时仍会占位，导致纯文案底部多出空行
        _body = new StackPanel { Spacing = 10 };
        _body.Children.Add(_text);
        _body.Children.Add(_actionButton);

        _content = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = GetThemeBrush("CustomOverlayAcrylicBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            // 默认不命中：纯文案提示应点击穿透，避免退场后透明 Popup 挡在下侧工具栏上。
            // 带操作按钮时由 ShowTooltip 打开命中，便于移入气泡点击。
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = _body,
        };
        _content.PointerEntered += Content_PointerEntered;
        _content.PointerExited += Content_PointerExited;
        _contentPointerPressedHandler = Content_PointerPressed;
        _content.AddHandler(UIElement.PointerPressedEvent, _contentPointerPressedHandler, handledEventsToo: true);

        _popup = new Popup
        {
            // 点击其它区域不自动关闭；由指针进出锚点/气泡控制显隐
            IsLightDismissEnabled = false,
            Child = _content,
            XamlRoot = xamlRoot,
        };

        _interactiveHideTimer = _dispatcherQueue.CreateTimer();
        _interactiveHideTimer.IsRepeating = false;
        _interactiveHideTimer.Interval = TimeSpan.FromMilliseconds(InteractiveHideGraceMs);
        _interactiveHideTimer.Tick += InteractiveHideTimer_Tick;

        _pointerPressedHandler = Element_PointerPressed;
        _rootPointerPressedHandler = Root_PointerPressed;
        _compositor = ElementCompositionPreview.GetElementVisual(_content).Compositor;
    }


    /// <summary>
    /// 将元素注册到本宿主；重复注册会被忽略。
    /// </summary>
    /// <param name="element">接收指针事件的锚点元素。</param>
    public void Register(FrameworkElement element)
    {
        if (!_elements.Add(element))
        {
            return;
        }

        element.PointerEntered += Element_PointerEntered;
        element.PointerExited += Element_PointerExited;
        // Button / ButtonBase 会在内部把 PointerPressed 标 Handled，普通 += 收不到，点击后提示关不掉。
        element.AddHandler(UIElement.PointerPressedEvent, _pointerPressedHandler, handledEventsToo: true);
        element.Unloaded += Element_Unloaded;
        _visibilityTokens[element] = element.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            OnElementVisibilityChanged);
    }


    /// <summary>
    /// 临时抑制本窗口内的 Tooltip 显示。
    /// 为 <see langword="true"/> 时立即强制关闭气泡；为 <see langword="false"/> 时仅恢复，不主动重新弹出。
    /// </summary>
    /// <param name="suppressed">是否抑制。</param>
    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed)
        {
            return;
        }

        _suppressed = suppressed;
        if (suppressed)
        {
            // 拖拽中指针仍可能落在锚点上，清掉“在表面内”状态，避免解除抑制后误判保持打开。
            Dismiss();
        }
    }


    /// <summary>
    /// 立即关闭当前气泡，不进入抑制；需新的 PointerEntered 才会再显示。
    /// </summary>
    public void Dismiss()
    {
        _pointerInsideAnyElement = false;
        _pointerInsidePopup = false;
        ForceClosePopup();
    }


    /// <summary>
    /// 解除元素注册并清理事件订阅；若正是当前展示锚点则立即关闭 Tooltip（不走退场动画）。
    /// </summary>
    /// <param name="element">待注销的锚点元素。</param>
    public void Unregister(FrameworkElement element)
    {
        if (!_elements.Remove(element))
        {
            return;
        }

        UnhookElement(element);

        if (ReferenceEquals(_dismissedUntilLeaveAnchor, element))
        {
            _dismissedUntilLeaveAnchor = null;
        }

        if (_currentAnchor == element)
        {
            // 页面导航时锚点直接卸树，收不到 PointerExited；必须立刻关 Popup。
            // 走 HideTooltip 退场动画会把已透明的 Popup 留在原处挡命中。
            _pointerInsideAnyElement = false;
            _pointerInsidePopup = false;
            ForceClosePopup();
            return;
        }

        // 其它锚点卸树时也清掉“仍在表面内”的误判，并收掉无主 Popup。
        if (_currentAnchor is null)
        {
            _pointerInsideAnyElement = false;
            _pointerInsidePopup = false;
            if (_popup.IsOpen)
            {
                ForceClosePopup();
            }
        }
    }


    /// <summary>
    /// 卸掉锚点上的指针、卸载与 Visibility 订阅（集合项本身由调用方移除）。
    /// </summary>
    private void UnhookElement(FrameworkElement element)
    {
        element.PointerEntered -= Element_PointerEntered;
        element.PointerExited -= Element_PointerExited;
        element.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressedHandler);
        element.Unloaded -= Element_Unloaded;
        if (_visibilityTokens.Remove(element, out long token))
        {
            element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token);
        }
    }


    /// <summary>
    /// 锚点被折叠时不会 Unloaded，PointerExited 也经常不发；当前气泡必须立刻关掉。
    /// </summary>
    private void OnElementVisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is not FrameworkElement element || element.Visibility == Visibility.Visible)
        {
            return;
        }

        if (ReferenceEquals(_dismissedUntilLeaveAnchor, element))
        {
            _dismissedUntilLeaveAnchor = null;
        }

        if (ReferenceEquals(_currentAnchor, element))
        {
            _pointerInsideAnyElement = false;
            _pointerInsidePopup = false;
            ForceClosePopup();
        }
    }


    /// <summary>
    /// 关闭 Popup、解绑全部锚点并清空状态（Host 从字典移除前调用）。
    /// </summary>
    public void Dispose()
    {
        foreach (FrameworkElement element in _elements)
        {
            UnhookElement(element);
        }

        _elements.Clear();
        _visibilityTokens.Clear();
        DetachClickDismissRoot();
        CancelInteractiveHideTimer();
        NotifyOpenChanged(false);
        _popup.IsOpen = false;
        _pointerInsideAnyElement = false;
        _pointerInsidePopup = false;
        _hideScheduled = false;
        _currentAnchor = null;
        _currentHasAction = false;
        _currentIsClickTriggered = false;
        _dismissedUntilLeaveAnchor = null;
        _actionButton.Click -= ActionButton_Click;
        _inlineActionLink.Click -= InlineActionLink_Click;
        _content.PointerEntered -= Content_PointerEntered;
        _content.PointerExited -= Content_PointerExited;
        _content.RemoveHandler(UIElement.PointerPressedEvent, _contentPointerPressedHandler);
        _interactiveHideTimer.Tick -= InteractiveHideTimer_Tick;
    }


    /// <summary>
    /// 锚点离开视觉树：先本宿主注销，再交给 <see cref="InstantTooltip.OnElementUnloaded"/>
    /// 处理 Host 生命周期与虚拟化复用后的重新挂接。
    /// </summary>
    /// <param name="sender">卸载的锚点。</param>
    /// <param name="e">路由事件参数。</param>
    private void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            // 不依赖 element.XamlRoot（卸载后可能已空），由本实例直接注销
            Unregister(element);
            InstantTooltip.OnElementUnloaded(element, this);
        }
    }


    /// <summary>
    /// 指针进入锚点：取消待隐藏并立即显示对应文案。
    /// 若刚在该锚点上点击过且尚未离开，则不重新弹出（配合 Flyout 点击后指针仍停在按钮上）。
    /// </summary>
    /// <param name="sender">锚点元素。</param>
    /// <param name="e">指针事件参数。</param>
    private void Element_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_suppressed)
        {
            return;
        }

        if (sender is not FrameworkElement element)
        {
            return;
        }

        // 点击触发的说明性提示不响应悬停，只认按下
        if (InstantTooltip.GetTrigger(element) is InstantTooltipTrigger.Click)
        {
            return;
        }

        // 父级已淡出/折叠时仍可能命中（如下侧工具栏取消固定后 Opacity=0），不要再弹出。
        if (IsEffectivelyHidden(element))
        {
            return;
        }

        // 点击后指针可能因 Flyout 打开发生短暂 Exit→Enter，在真正离开锚点前保持关闭
        if (ReferenceEquals(_dismissedUntilLeaveAnchor, element))
        {
            return;
        }

        // 从其它锚点移入时清掉旧的点击抑制
        _dismissedUntilLeaveAnchor = null;
        _pointerInsideAnyElement = true;
        CancelPendingHide();
        ShowTooltip(element);
    }


    /// <summary>
    /// 指针离开锚点：延后一拍再决定是否隐藏，避免相邻项切换或移入气泡时闪断。
    /// 点击后打开 Flyout 时可能短暂 Exit→Enter：延迟清除 dismissed，避免提示立刻又弹出来。
    /// </summary>
    /// <param name="sender">锚点元素。</param>
    /// <param name="e">指针事件参数。</param>
    private void Element_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        FrameworkElement? exited = sender as FrameworkElement;
        if (exited is not null && InstantTooltip.GetTrigger(exited) is InstantTooltipTrigger.Click)
        {
            return;
        }

        _pointerInsideAnyElement = false;
        ScheduleHideIfPointerLeftSurface();

        if (exited is not null && ReferenceEquals(_dismissedUntilLeaveAnchor, exited))
        {
            FrameworkElement dismissed = exited;
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                // 仍在同一锚点上（短暂 Exit→Enter）则保持抑制；真正离开后再允许下次悬停显示
                if (ReferenceEquals(_dismissedUntilLeaveAnchor, dismissed) && !ReferenceEquals(_currentAnchor, dismissed))
                {
                    _dismissedUntilLeaveAnchor = null;
                }
            });
        }
    }


    /// <summary>
    /// 在锚点上按下：点击触发的锚点在此开合，其余锚点立即关掉 Tooltip，
    /// 避免点开 Flyout 或按钮随后折叠后提示仍叠在原处。
    /// 经 AddHandler(handledEventsToo) 注册，才能收到 Button 已处理的 PointerPressed。
    /// </summary>
    private void Element_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (InstantTooltip.GetTrigger(element) is InstantTooltipTrigger.Click)
        {
            ToggleClickTooltip(element);
            return;
        }

        _dismissedUntilLeaveAnchor = element;
        _pointerInsideAnyElement = false;
        _pointerInsidePopup = false;
        ForceClosePopup();
    }


    /// <summary>
    /// 点击触发：同一锚点已展开则收起，否则展开并开始监听「点击别处」。
    /// </summary>
    /// <param name="element">被按下的点击触发锚点。</param>
    private void ToggleClickTooltip(FrameworkElement element)
    {
        // 这次按下随后会冒泡到根元素，标记一拍避免被 Root_PointerPressed 当成点击别处
        _clickToggleInProgress = true;
        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _clickToggleInProgress = false);

        _dismissedUntilLeaveAnchor = null;
        _pointerInsideAnyElement = false;
        _pointerInsidePopup = false;

        if (_popup.IsOpen && _currentIsClickTriggered && ReferenceEquals(_currentAnchor, element))
        {
            ForceClosePopup();
            return;
        }

        CancelPendingHide();
        ShowTooltip(element);
    }


    /// <summary>
    /// 点击触发展示期间，别处按下即收起。
    /// 气泡与锚点上的按下必须放行：Popup 子树的事件同样会冒泡到窗口根，
    /// 若在此关掉气泡，抬起时操作链接已消失，点了等于没点。
    /// </summary>
    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_clickToggleInProgress)
        {
            // 锚点上的这次按下已冒泡到此，标记就地清掉，不必等 Low 优先级那一拍
            _clickToggleInProgress = false;
            return;
        }

        if (!_currentIsClickTriggered)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && (IsSelfOrDescendantOf(source, _content)
                || (_currentAnchor is not null && IsSelfOrDescendantOf(source, _currentAnchor))))
        {
            return;
        }

        _pointerInsideAnyElement = false;
        _pointerInsidePopup = false;
        ForceClosePopup();
    }


    /// <summary>
    /// 判断 <paramref name="node"/> 是否为 <paramref name="ancestor"/> 自身或其视觉子孙。
    /// </summary>
    /// <param name="node">起点节点（通常是事件的 OriginalSource）。</param>
    /// <param name="ancestor">待比较的祖先。</param>
    /// <returns>是自身或子孙则为 <see langword="true"/>。</returns>
    private static bool IsSelfOrDescendantOf(DependencyObject node, DependencyObject ancestor)
    {
        DependencyObject? current = node;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }


    /// <summary>
    /// 监听锚点所在视觉树顶层的按下事件，用于「点击别处收起」。
    /// 顶层通常是窗口根（Popup 子树也会一路冒泡上去）；若走到 Popup 就停，则改挂其 Child。
    /// 点到外层弹层之外会先关掉弹层并卸载锚点，由 <see cref="Unregister"/> 收起气泡。
    /// </summary>
    /// <param name="element">当前展示的点击触发锚点。</param>
    private void AttachClickDismissRoot(FrameworkElement element)
    {
        UIElement? root = FindEventRoot(element);
        if (root is Popup popup && popup.Child is UIElement popupChild)
        {
            root = popupChild;
        }

        if (ReferenceEquals(root, _clickDismissRoot))
        {
            return;
        }

        DetachClickDismissRoot();
        if (root is null)
        {
            return;
        }

        _clickDismissRoot = root;
        root.AddHandler(UIElement.PointerPressedEvent, _rootPointerPressedHandler, handledEventsToo: true);
    }


    /// <summary>
    /// 解除「点击别处收起」监听。
    /// </summary>
    private void DetachClickDismissRoot()
    {
        if (_clickDismissRoot is null)
        {
            return;
        }

        _clickDismissRoot.RemoveHandler(UIElement.PointerPressedEvent, _rootPointerPressedHandler);
        _clickDismissRoot = null;
    }


    /// <summary>
    /// 向上找到锚点所在视觉树的最顶层 <see cref="UIElement"/>（页面根，或承载弹层的 Popup）。
    /// </summary>
    /// <param name="element">起点元素。</param>
    /// <returns>顶层元素；无法取得时为 <see langword="null"/>。</returns>
    private static UIElement? FindEventRoot(DependencyObject element)
    {
        UIElement? root = element as UIElement;
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement ui)
            {
                root = ui;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return root;
    }


    /// <summary>
    /// 指针进入气泡：保持打开（以便点击右下角操作）。
    /// </summary>
    private void Content_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsidePopup = true;
        CancelPendingHide();
    }


    /// <summary>
    /// 指针离开气泡：若也不在锚点上则延后隐藏。
    /// </summary>
    private void Content_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsidePopup = false;
        ScheduleHideIfPointerLeftSurface();
    }


    /// <summary>
    /// 在气泡上按下：补记「指针在气泡内」。触摸没有悬停阶段收不到 PointerEntered，
    /// 外层弹层（签到 Flyout）会把这次按下当成外部点击而关掉，操作链接就点不到了。
    /// </summary>
    private void Content_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsidePopup = true;
        CancelPendingHide();
    }


    /// <summary>
    /// 操作按钮：执行锚点回调后关闭气泡。
    /// </summary>
    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeCurrentAction();
    }


    /// <summary>
    /// 行内操作链接：与右下角按钮同一套回调。
    /// </summary>
    private void InlineActionLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        InvokeCurrentAction();
    }


    /// <summary>
    /// 执行当前锚点的操作回调。先关气泡，避免回调里再弹层时叠在提示上。
    /// </summary>
    private void InvokeCurrentAction()
    {
        FrameworkElement? anchor = _currentAnchor;
        Action? callback = anchor is null ? null : InstantTooltip.GetActionCallback(anchor);
        _pointerInsideAnyElement = false;
        _pointerInsidePopup = false;
        ForceClosePopup();
        callback?.Invoke();
    }


    /// <summary>
    /// 取消待隐藏（重新进入锚点/气泡时调用）。
    /// </summary>
    private void CancelPendingHide()
    {
        _hideScheduled = false;
        CancelInteractiveHideTimer();
    }


    /// <summary>
    /// 停止可交互气泡的隐藏宽限定时器。
    /// </summary>
    private void CancelInteractiveHideTimer()
    {
        if (_interactiveHideTimer.IsRunning)
        {
            _interactiveHideTimer.Stop();
        }
    }


    /// <summary>
    /// 相邻切换 / 移入气泡时 Exited→Entered 之间短暂为空，延后判断再隐藏。
    /// 带操作按钮时使用更长宽限，便于鼠标从锚点移入气泡；纯文案提示仍只延后一拍，行为不变。
    /// </summary>
    private void ScheduleHideIfPointerLeftSurface()
    {
        // 点击触发的提示只由再次点击 / 点击别处 / 锚点消失收起，指针进出不管
        if (_currentIsClickTriggered)
        {
            return;
        }

        if (_currentHasAction)
        {
            // 可交互：重启宽限，避免锚点与气泡间隙中气泡先消失
            CancelInteractiveHideTimer();
            _interactiveHideTimer.Start();
            return;
        }

        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!IsPointerOverTooltipSurface && !_hideScheduled)
            {
                _hideScheduled = true;
                HideTooltip();
            }
        });
    }


    /// <summary>
    /// 可交互气泡宽限结束：若指针仍未回到锚点/气泡则隐藏。
    /// </summary>
    private void InteractiveHideTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        CancelInteractiveHideTimer();
        if (!IsPointerOverTooltipSurface && !_hideScheduled)
        {
            _hideScheduled = true;
            HideTooltip();
        }
    }


    /// <summary>
    /// 显示并定位指定元素的 Tooltip，同时播放入场动画。
    /// </summary>
    /// <param name="element">当前悬停的锚点；自身或祖先已隐藏、文案为空时直接返回。</param>
    private void ShowTooltip(FrameworkElement element)
    {
        if (_suppressed || IsEffectivelyHidden(element))
        {
            return;
        }

        string? label = InstantTooltip.GetText(element);
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        bool wasOpenWithAction = _popup.IsOpen && _currentHasAction;
        FrameworkElement? previousAnchor = _currentAnchor;

        _currentAnchor = element;
        _currentPlacement = InstantTooltip.GetPlacement(element);

        string? actionText = InstantTooltip.GetActionText(element);
        bool hasAction = !string.IsNullOrEmpty(actionText) && InstantTooltip.GetActionCallback(element) is not null;
        bool actionInline = hasAction && InstantTooltip.GetActionInline(element);
        bool clickTriggered = InstantTooltip.GetTrigger(element) is InstantTooltipTrigger.Click;
        _currentHasAction = hasAction;
        _currentIsClickTriggered = clickTriggered;
        // 仅可交互 / 点击触发的气泡需要命中；纯悬停文案必须穿透，否则退场后透明层会挡住下方工具栏。
        _content.IsHitTestVisible = hasAction || clickTriggered;

        _text.Inlines.Clear();
        if (actionInline)
        {
            // 不换行空格：链接紧跟正文，避免单独掉到下一行
            AppendLabelInlines(label.TrimEnd() + "\u00A0");
            _inlineActionRun.Text = actionText;
            _text.Inlines.Add(_inlineActionLink);
            _actionButton.Content = null;
            _actionButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            AppendLabelInlines(label);
            if (hasAction)
            {
                _actionButton.Content = actionText;
                _actionButton.Visibility = Visibility.Visible;
            }
            else
            {
                _actionButton.Content = null;
                _actionButton.Visibility = Visibility.Collapsed;
            }
        }

        UpdatePosition(element);
        // 须在 IsOpen 前重置 visual，否则会先闪一帧完整大小
        PrepareShowVisual();
        _popup.IsOpen = true;
        PlayShowAnimation();

        if (clickTriggered)
        {
            AttachClickDismissRoot(element);
        }
        else
        {
            DetachClickDismissRoot();
        }

        // 可交互气泡：通知外层（如快速菜单）勿因指针移入气泡而关闭
        if (hasAction)
        {
            if (!wasOpenWithAction || !ReferenceEquals(previousAnchor, element))
            {
                InstantTooltip.GetOpenChangedCallback(element)?.Invoke(true);
            }
        }
        else if (wasOpenWithAction && previousAnchor is not null)
        {
            InstantTooltip.GetOpenChangedCallback(previousAnchor)?.Invoke(false);
        }
    }


    /// <summary>
    /// 把正文写入 <see cref="_text"/>，其中的换行符转成 <see cref="LineBreak"/>。
    /// 图表提示这类「日期 + 数值」两行文案，靠 Run 自带的换行符不一定断行。
    /// </summary>
    /// <param name="label">提示正文，可含换行符。</param>
    private void AppendLabelInlines(string label)
    {
        string[] lines = label.Replace("\r\n", "\n").Split('\n');
        _labelRun.Text = lines[0];
        _text.Inlines.Add(_labelRun);
        for (int i = 1; i < lines.Length; i++)
        {
            _text.Inlines.Add(new LineBreak());
            _text.Inlines.Add(new Run { Text = lines[i] });
        }
    }


    /// <summary>
    /// 元素或其祖先不可见、透明时视为已隐藏，不应再弹出 Tooltip。
    /// </summary>
    private static bool IsEffectivelyHidden(UIElement element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement ui && (ui.Visibility != Visibility.Visible || ui.Opacity <= 0))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }


    /// <summary>
    /// 立即关闭 Popup 并通知可交互打开状态结束（无退场动画）。
    /// </summary>
    private void ForceClosePopup()
    {
        CancelPendingHide();
        DetachClickDismissRoot();
        NotifyOpenChanged(false);
        _content.IsHitTestVisible = false;
        _popup.IsOpen = false;
        _currentAnchor = null;
        _currentHasAction = false;
        _currentIsClickTriggered = false;
    }


    /// <summary>
    /// 若当前展示带操作按钮，通知打开/关闭回调。
    /// </summary>
    private void NotifyOpenChanged(bool isOpen)
    {
        if (!_currentHasAction || _currentAnchor is null)
        {
            return;
        }
        InstantTooltip.GetOpenChangedCallback(_currentAnchor)?.Invoke(isOpen);
    }


    /// <summary>
    /// 测量提示内容尺寸。Popup 已打开时 <see cref="FrameworkElement.ActualWidth"/> 可能仍是上一段文案的布局结果，故只取 <see cref="FrameworkElement.DesiredSize"/>。
    /// </summary>
    /// <returns>当前文案对应的测量尺寸。</returns>
    private Size MeasureTooltipContent()
    {
        _content.InvalidateMeasure();
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _content.DesiredSize;
    }


    /// <summary>
    /// 按当前方位将 Popup 定位到锚点附近（窗口坐标系，经 <see cref="UIElement.TransformToVisual"/>）。
    /// 首选方位空间不足时会翻转（Top↔Bottom / Left↔Right），再钳位到 XamlRoot 可视区内，避免贴窗边被裁切。
    /// </summary>
    /// <param name="element">锚点元素。</param>
    private void UpdatePosition(FrameworkElement element)
    {
        Size tipSize = MeasureTooltipContent();
        double tipWidth = tipSize.Width;
        double tipHeight = tipSize.Height;

        GeneralTransform transform = element.TransformToVisual(null);
        Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        Size rootSize = _xamlRoot.Size;
        const double margin = 8;
        bool hasRoot = rootSize.Width > 0 && rootSize.Height > 0;

        // 首选方位放不下时翻到对侧，保证气泡完整可见（仍贴锚点）
        InstantTooltipPlacement placement = _currentPlacement;
        if (hasRoot)
        {
            placement = placement switch
            {
                InstantTooltipPlacement.Top when bounds.Top - Gap - tipHeight < margin
                    && bounds.Bottom + Gap + tipHeight + margin <= rootSize.Height
                    => InstantTooltipPlacement.Bottom,
                InstantTooltipPlacement.Bottom when bounds.Bottom + Gap + tipHeight > rootSize.Height - margin
                    && bounds.Top - Gap - tipHeight >= margin
                    => InstantTooltipPlacement.Top,
                InstantTooltipPlacement.Left when bounds.Left - Gap - tipWidth < margin
                    && bounds.Right + Gap + tipWidth + margin <= rootSize.Width
                    => InstantTooltipPlacement.Right,
                InstantTooltipPlacement.Right when bounds.Right + Gap + tipWidth > rootSize.Width - margin
                    && bounds.Left - Gap - tipWidth >= margin
                    => InstantTooltipPlacement.Left,
                _ => placement,
            };
            // 入场缩放原点随实际方位更新
            _currentPlacement = placement;
        }

        double x;
        double y;
        switch (placement)
        {
            case InstantTooltipPlacement.Left:
                x = bounds.Left - tipWidth - Gap;
                y = bounds.Top + (bounds.Height - tipHeight) / 2;
                break;
            case InstantTooltipPlacement.Top:
                x = bounds.Left + (bounds.Width - tipWidth) / 2;
                y = bounds.Top - tipHeight - Gap;
                break;
            case InstantTooltipPlacement.Bottom:
                x = bounds.Left + (bounds.Width - tipWidth) / 2;
                y = bounds.Bottom + Gap;
                break;
            default:
                // Right：导航 LeftCompact 侧栏默认，贴在锚点右侧垂直居中
                x = bounds.Right + Gap;
                y = bounds.Top + (bounds.Height - tipHeight) / 2;
                break;
        }

        // 水平/垂直钳位：靠近窗边时把气泡整体移入可视区（如 Top 居中超出右缘）
        if (hasRoot)
        {
            double maxX = Math.Max(margin, rootSize.Width - tipWidth - margin);
            double maxY = Math.Max(margin, rootSize.Height - tipHeight - margin);
            x = Math.Clamp(x, margin, maxX);
            y = Math.Clamp(y, margin, maxY);
        }

        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
    }


    /// <summary>
    /// 重置 Composition 视觉状态为入场起点（靠近锚点一侧缩放 0.7 + 透明）。
    /// </summary>
    private void PrepareShowVisual()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(_content);
        visual.CenterPoint = GetScaleCenterPoint();
        visual.Scale = new Vector3(InitialScale, InitialScale, 1f);
        visual.Opacity = 0f;
    }


    /// <summary>
    /// 播放从小到大的入场动画（scale 0.7→1 + 淡入，500ms 缓动）。
    /// 全局关闭动画时直接设为最终态。
    /// </summary>
    private void PlayShowAnimation()
    {
        if (!EntranceAnimation.AnimationsEnabled())
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(_content);
            visual.Scale = Vector3.One;
            visual.Opacity = 1f;
            return;
        }

        Visual v = ElementCompositionPreview.GetElementVisual(_content);
        v.CenterPoint = GetScaleCenterPoint();

        CubicBezierEasingFunction ease = _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

        Vector3KeyFrameAnimation scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(InitialScale, InitialScale, 1f));
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(ShowDurationMs);
        v.StartAnimation(nameof(Visual.Scale), scale);

        ScalarKeyFrameAnimation opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(1f, 1f, ease);
        opacity.Duration = TimeSpan.FromMilliseconds(ShowDurationMs);
        v.StartAnimation(nameof(Visual.Opacity), opacity);
    }


    /// <summary>
    /// 隐藏 Tooltip；有动画时先快速缩小淡出，动画结束后再关闭 Popup。
    /// 退场期间若指针再次进入锚点或气泡，则不关闭 Popup（避免打断新目标的展示）。
    /// </summary>
    private void HideTooltip()
    {
        if (!_popup.IsOpen)
        {
            _hideScheduled = false;
            return;
        }

        if (!EntranceAnimation.AnimationsEnabled())
        {
            ForceClosePopup();
            return;
        }

        Visual v = ElementCompositionPreview.GetElementVisual(_content);
        v.CenterPoint = GetScaleCenterPoint();

        CubicBezierEasingFunction ease = _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

        Vector3KeyFrameAnimation scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1f, new Vector3(InitialScale, InitialScale, 1f), ease);
        scale.Duration = TimeSpan.FromMilliseconds(HideDurationMs);

        ScalarKeyFrameAnimation opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, 0f, ease);
        opacity.Duration = TimeSpan.FromMilliseconds(HideDurationMs);

        // ScopedBatch：等 scale/opacity 都结束后再关 Popup，避免动画中途被拆掉
        CompositionScopedBatch batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _hideScheduled = false;
                // 退场过程中若已 Entered 新锚点或气泡，保留 Popup 由 ShowTooltip 接管。
                // 锚点已卸树（页面导航）时必须关，否则透明 Popup 会留在原处挡命中。
                bool anchorAlive = _currentAnchor is not null && _currentAnchor.XamlRoot is not null;
                if (!IsPointerOverTooltipSurface || !anchorAlive)
                {
                    ForceClosePopup();
                }
            });
        };
        v.StartAnimation(nameof(Visual.Scale), scale);
        v.StartAnimation(nameof(Visual.Opacity), opacity);
        batch.End();
    }


    /// <summary>
    /// 从元素局部资源或应用资源字典解析主题画刷。
    /// </summary>
    /// <param name="resourceKey">ThemeResource 键名。</param>
    /// <returns>解析到的画刷；失败时返回透明画刷。</returns>
    private Brush GetThemeBrush(string resourceKey)
    {
        if (_themeSource.Resources.TryGetValue(resourceKey, out object? local) && local is Brush localBrush)
        {
            return localBrush;
        }

        if (Application.Current.Resources.TryGetValue(resourceKey, out object? app) && app is Brush appBrush)
        {
            return appBrush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }


    /// <summary>
    /// 按当前方位取得缩放原点（靠近锚点的那条边中点，使提示从锚点方向展开）。
    /// </summary>
    /// <returns>Border 局部坐标系下的 Composition 中心点。</returns>
    private Vector3 GetScaleCenterPoint()
    {
        Size tipSize = MeasureTooltipContent();
        double width = tipSize.Width;
        double height = tipSize.Height;

        // Right → 左边中点；Left → 右边中点；Top/Bottom 同理取靠近锚点一侧
        return _currentPlacement switch
        {
            InstantTooltipPlacement.Left => new Vector3((float)width, (float)(height / 2), 0f),
            InstantTooltipPlacement.Top => new Vector3((float)(width / 2), (float)height, 0f),
            InstantTooltipPlacement.Bottom => new Vector3((float)(width / 2), 0f, 0f),
            _ => new Vector3(0f, (float)(height / 2), 0f),
        };
    }
}
