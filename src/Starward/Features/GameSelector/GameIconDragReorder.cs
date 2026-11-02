using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Starward.Controls;
using System;
using Vanara.PInvoke;
using Windows.Foundation;


namespace Starward.Features.GameSelector;


/// <summary>
/// 让横排游戏图标支持「按住拖动、松手换位」。鼠标移动超过阈值即开始拖动，触摸和笔需要先长按。
/// <para>不用 <see cref="ListViewBase.CanReorderItems"/>：它走系统拖放，非打包应用提权后系统拖放不可用
/// （microsoft-ui-xaml issue 7690，崩溃或拖拽不启动），而本应用常以管理员身份运行。</para>
/// <para>拖动视觉只用 <see cref="CompositeTransform"/> + <see cref="UIElement.Opacity"/> + <see cref="Canvas.ZIndex"/> 直接赋值，
/// 其余图标的滑动在 <see cref="CompositionTarget.Rendering"/> 里逐帧赋值（跨 DPI 安全，见 GachaStatsCardDragReorder）。
/// 图标只沿横向移动且不超出首尾槽位，因为列表内部的 ScrollViewer 会裁剪超出视口的内容。</para>
/// </summary>
internal sealed class GameIconDragReorder
{
    /// <summary>起拖阈值（DIP）：指针自按下点移动超过该距离才进入拖动，避免干扰点击。</summary>
    private const double DragThreshold = 6d;

    /// <summary>拖动中图标的放大比例。</summary>
    private const double LiftScale = 1.1d;

    /// <summary>拖动中图标的不透明度。</summary>
    private const double LiftOpacity = 0.9d;

    /// <summary>拖动中图标的层级，确保浮在其余图标之上。</summary>
    private const int LiftZIndex = 100;

    /// <summary>其余图标滑向虚拟槽位的指数平滑时间常数（秒）。</summary>
    private const double SmoothingTau = 0.045d;

    /// <summary>滑动收敛阈值（DIP）：与目标差值小于此即吸附。</summary>
    private const double SnapEpsilon = 0.5d;

    /// <summary>自动滚动热区宽度（DIP），约为一个图标宽。</summary>
    private const double AutoScrollEdgeZone = 48d;

    /// <summary>指针位于热区最边缘时的最大自动滚动速度（DIP/秒）。</summary>
    private const double AutoScrollMaxSpeed = 480d;

    /// <summary>松手后查询光标是否已离开图标的间隔。</summary>
    private static readonly TimeSpan LingerCheckInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>松手后最多查询的次数。约 2 秒后新容器早已收到 PointerEntered（实测约 0.6 秒），之后离开会有 PointerExited。</summary>
    private const int LingerCheckCount = 20;


    private readonly ListView _listView;
    private readonly Action<int, int> _moveItem;
    private readonly DispatcherQueueTimer _lingerTimer;

    private ScrollViewer? _scrollViewer;
    // 松手时指针还停在上面的图标及其区域（相对 ScrollViewer）
    private GameBizIcon? _lingeringIcon;
    private Rect _lingeringBounds;
    private int _lingerChecksLeft;
    private FrameworkElement? _dragged;
    private Pointer? _pointer;
    private bool _pressed;
    private bool _dragging;
    private Point _pressViewport;
    // 拖动期间不读 HorizontalOffset：ChangeView 的回写可能晚于下一帧，用逻辑偏移保证坐标连续
    private double _offsetX;
    private double _viewportX;
    private double _pressContentX;
    private double _pitch;
    private Point _draggedOrigin;
    private int _originalSlot = -1;
    private int _targetSlot = -1;
    private FrameworkElement?[] _containers = [];
    private double[] _currentTx = [];
    private double[] _targetTx = [];
    private bool _animate;
    private bool _renderingHooked;
    private TimeSpan _lastRenderTime;


    /// <param name="listView">承载图标的列表。</param>
    /// <param name="moveItem">松手后提交换位，参数为旧索引、新索引。</param>
    public GameIconDragReorder(ListView listView, Action<int, int> moveItem)
    {
        _listView = listView;
        _moveItem = moveItem;
        _lingerTimer = listView.DispatcherQueue.CreateTimer();
        _lingerTimer.Interval = LingerCheckInterval;
        _lingerTimer.Tick += LingerTimer_Tick;
        // ListViewItem 会把指针事件标记为已处理，必须用 handledEventsToo 才收得到
        listView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        listView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        listView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        listView.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerCanceled), true);
        listView.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), true);
        listView.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        listView.AddHandler(UIElement.HoldingEvent, new HoldingEventHandler(OnHolding), true);
    }


    /// <summary>是否正处于拖动中。</summary>
    public bool IsDragging => _dragging;


    /// <summary>一次拖动结束（含中断）时触发，换位已提交；参数是结束拖动的指针事件，可取松手位置。</summary>
    public event Action<PointerRoutedEventArgs>? DragEnded;


    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            return;
        }
        // 上一次按下可能在列表外松开、没收到松手事件，这里总是重新开始
        ClearState();
        // 按下时框架已重新判定指针所在元素，之后的进出事件是可靠的；再拖动时也不能让定时查询把图标压暗
        StopLingering();
        _scrollViewer ??= _listView.FindDescendant<ScrollViewer>();
        if (_scrollViewer is null || FindContainer(e.OriginalSource as DependencyObject) is not ListViewItem container)
        {
            return;
        }
        PointerPoint point = e.GetCurrentPoint(_scrollViewer);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        _dragged = container;
        // 这里不能捕获指针：会打断 ListViewItem 的按压，单击图标就切换不了游戏了，开始拖动时才捕获
        _pointer = e.Pointer;
        _pressViewport = point.Position;
        _pressed = true;
    }


    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || _scrollViewer is null || e.Pointer.PointerId != _pointer?.PointerId)
        {
            return;
        }
        PointerPoint point = e.GetCurrentPoint(_scrollViewer);
        bool isMouse = e.Pointer.PointerDeviceType == PointerDeviceType.Mouse;
        if (isMouse && !point.Properties.IsLeftButtonPressed)
        {
            // 在列表外松开时收不到松手事件：未拖动就是过期的按下，拖动中就是捕获已失效
            FinishDrag(commit: false, e);
            return;
        }
        if (!_dragging)
        {
            // 触摸和笔由 OnHolding 开始拖动，长按前的移动留给列表滚动
            if (!isMouse)
            {
                return;
            }
            double dx = point.Position.X - _pressViewport.X;
            double dy = point.Position.Y - _pressViewport.Y;
            if ((dx * dx) + (dy * dy) < DragThreshold * DragThreshold)
            {
                return;
            }
            if (!BeginDrag())
            {
                ClearState();
                return;
            }
        }
        _viewportX = point.Position.X;
        UpdateDrag();
        e.Handled = true;
    }


    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || e.Pointer.PointerId != _pointer?.PointerId)
        {
            return;
        }
        if (_dragging)
        {
            e.Handled = true;
        }
        FinishDrag(commit: true, e);
    }


    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _pointer?.PointerId)
        {
            FinishDrag(commit: false, e);
        }
    }


    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // 子元素（如 ListViewItem）丢失捕获也会冒泡到这里，开始拖动时 ListViewItem 就会丢失一次，只响应列表自身
        if (_dragging && ReferenceEquals(e.OriginalSource, _listView))
        {
            FinishDrag(commit: false, e);
        }
    }


    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            // 指针被列表捕获后滚轮到不了内部的 ScrollViewer，由这里滚动
            ScrollTo(_offsetX - e.GetCurrentPoint(_listView).Properties.MouseWheelDelta);
            e.Handled = true;
        }
    }


    private void OnHolding(object sender, HoldingRoutedEventArgs e)
    {
        if (!_pressed || _dragging || _dragged is null || e.HoldingState != HoldingState.Started
            || e.PointerDeviceType == PointerDeviceType.Mouse)
        {
            return;
        }
        // 否则之后的移动会被 ScrollViewer 当成滑动拿走，拖动随之中断
        _dragged.CancelDirectManipulations();
        if (BeginDrag())
        {
            _viewportX = _pressViewport.X;
        }
        else
        {
            ClearState();
        }
    }


    /// <summary>进入拖动：捕获指针、记录各槽位几何并施加「拎起」视觉。</summary>
    /// <returns>是否成功进入拖动；只有一个图标或捕获失败时返回 <see langword="false"/>。</returns>
    private bool BeginDrag()
    {
        if (_dragged is null || _pointer is null || _scrollViewer is null)
        {
            return false;
        }
        int count = _listView.Items.Count;
        int slot = _listView.IndexFromContainer(_dragged);
        if (count < 2 || slot < 0 || _dragged.ActualWidth <= 1 || !_listView.CapturePointer(_pointer))
        {
            return false;
        }
        _containers = new FrameworkElement?[count];
        for (int i = 0; i < count; i++)
        {
            _containers[i] = _listView.ContainerFromIndex(i) as FrameworkElement;
        }
        _originalSlot = slot;
        _targetSlot = slot;
        // 图标等宽且没有间距，容器宽度就是槽位间距
        _pitch = _dragged.ActualWidth;
        _currentTx = new double[count];
        _targetTx = new double[count];
        _animate = EntranceAnimation.AnimationsEnabled();
        _offsetX = _scrollViewer.HorizontalOffset;
        _pressContentX = _pressViewport.X + _offsetX;
        // 须在施加变换前量取
        _draggedOrigin = _dragged.TransformToVisual(_scrollViewer).TransformPoint(default);
        CompositeTransform t = GetTransform(_dragged);
        t.CenterX = _dragged.ActualWidth / 2;
        t.CenterY = _dragged.ActualHeight / 2;
        t.ScaleX = LiftScale;
        t.ScaleY = LiftScale;
        _dragged.Opacity = LiftOpacity;
        Canvas.SetZIndex(_dragged, LiftZIndex);
        _dragging = true;
        _renderingHooked = true;
        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
        return true;
    }


    /// <summary>让被拖图标跟随指针，并按其中心落在哪个槽位刷新目标次序。</summary>
    private void UpdateDrag()
    {
        if (_dragged is null)
        {
            return;
        }
        double min = -_originalSlot * _pitch;
        double max = (_containers.Length - 1 - _originalSlot) * _pitch;
        double translateX = Math.Clamp(_viewportX + _offsetX - _pressContentX, min, max);
        GetTransform(_dragged).TranslateX = translateX;
        int slot = _originalSlot + (int)Math.Round(translateX / _pitch, MidpointRounding.AwayFromZero);
        if (slot != _targetSlot)
        {
            _targetSlot = slot;
            ApplyVirtualLayout();
        }
    }


    /// <summary>
    /// 计算其余图标在目标次序下应有的位移。开启动画时由 <see cref="OnRendering"/> 逐帧滑过去，否则直接到位。
    /// </summary>
    private void ApplyVirtualLayout()
    {
        for (int s = 0; s < _containers.Length; s++)
        {
            if (s == _originalSlot)
            {
                continue;
            }
            int virtualSlot = s;
            if (s > _originalSlot && s <= _targetSlot)
            {
                virtualSlot = s - 1;
            }
            else if (s < _originalSlot && s >= _targetSlot)
            {
                virtualSlot = s + 1;
            }
            _targetTx[s] = (virtualSlot - s) * _pitch;
            if (!_animate)
            {
                _currentTx[s] = _targetTx[s];
                ApplyTranslate(s, _targetTx[s]);
            }
        }
    }


    /// <summary>每帧按指针位置自动滚动，并把其余图标的位移朝目标平滑逼近。</summary>
    /// <param name="sender">渲染事件发送者；此处不使用。</param>
    /// <param name="e">渲染事件参数；提供本帧时间以计算步长。</param>
    private void OnRendering(object? sender, object e)
    {
        double dt = 1.0 / 60;
        if (e is RenderingEventArgs args)
        {
            if (_lastRenderTime > TimeSpan.Zero)
            {
                dt = (args.RenderingTime - _lastRenderTime).TotalSeconds;
            }
            _lastRenderTime = args.RenderingTime;
        }
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60;
        }
        TryAutoScroll(dt);
        if (!_animate)
        {
            return;
        }
        double factor = 1.0 - Math.Exp(-dt / SmoothingTau);
        for (int s = 0; s < _containers.Length; s++)
        {
            double diff = _targetTx[s] - _currentTx[s];
            if (s == _originalSlot || diff == 0)
            {
                continue;
            }
            _currentTx[s] = Math.Abs(diff) < SnapEpsilon ? _targetTx[s] : _currentTx[s] + (diff * factor);
            ApplyTranslate(s, _currentTx[s]);
        }
    }


    /// <summary>指针进入视口左右边缘的热区时滚动列表，越靠近边缘越快；图标放不下时才会发生。</summary>
    /// <param name="elapsedSeconds">距上一帧的时间（秒）。</param>
    private void TryAutoScroll(double elapsedSeconds)
    {
        if (_scrollViewer is null || _scrollViewer.ScrollableWidth <= 0)
        {
            return;
        }
        double viewportWidth = _scrollViewer.ViewportWidth;
        double zone = Math.Min(AutoScrollEdgeZone, viewportWidth / 2);
        if (zone <= 0)
        {
            return;
        }
        double left = Math.Clamp((zone - _viewportX) / zone, 0, 1);
        double right = Math.Clamp((_viewportX - (viewportWidth - zone)) / zone, 0, 1);
        double intensity = Math.Max(left, right);
        if (intensity > 0)
        {
            double direction = right > 0 ? 1 : -1;
            ScrollTo(_offsetX + (direction * AutoScrollMaxSpeed * intensity * intensity * elapsedSeconds));
        }
    }


    /// <summary>把列表滚动到目标偏移，并让被拖图标继续贴着指针。</summary>
    /// <param name="requestedOffset">目标横向偏移；会被限制在可滚动范围内。</param>
    private void ScrollTo(double requestedOffset)
    {
        if (_scrollViewer is null)
        {
            return;
        }
        double target = Math.Clamp(requestedOffset, 0, Math.Max(0, _scrollViewer.ScrollableWidth));
        if (Math.Abs(target - _offsetX) >= 0.01 && _scrollViewer.ChangeView(target, null, null, disableAnimation: true))
        {
            _offsetX = target;
            UpdateDrag();
        }
    }


    /// <summary>
    /// 结束按下或拖动：复位视觉、释放捕获，需要时提交换位。
    /// 先把状态置否，因为释放捕获会同步触发 CaptureLost。
    /// </summary>
    /// <param name="commit">是否提交换位；中断时传 <see langword="false"/>，图标回到原位。</param>
    /// <param name="e">结束拖动的指针事件。</param>
    private void FinishDrag(bool commit, PointerRoutedEventArgs e)
    {
        bool wasDragging = _dragging;
        int from = _originalSlot;
        int to = _targetSlot;
        _pressed = false;
        _dragging = false;
        if (wasDragging)
        {
            if (_renderingHooked)
            {
                _renderingHooked = false;
                CompositionTarget.Rendering -= OnRendering;
            }
            foreach (FrameworkElement? container in _containers)
            {
                ResetVisual(container);
            }
            try
            {
                if (_pointer is not null)
                {
                    _listView.ReleasePointerCapture(_pointer);
                }
            }
            catch { }
            int finalSlot = commit ? to : from;
            SettleHover(finalSlot, e);
            if (finalSlot != from)
            {
                MoveWithoutTransitions(from, finalSlot);
            }
        }
        ClearState();
        if (wasDragging)
        {
            DragEnded?.Invoke(e);
        }
    }


    /// <summary>
    /// 提交换位。图标此时已在最终位置，要暂时停用容器过渡：
    /// 否则过渡会从布局上的旧位置重新滑过来，松手瞬间图标先跳回去再滑一遍。
    /// </summary>
    /// <param name="from">旧索引。</param>
    /// <param name="to">新索引。</param>
    private void MoveWithoutTransitions(int from, int to)
    {
        TransitionCollection transitions = _listView.ItemContainerTransitions;
        _listView.ItemContainerTransitions = new TransitionCollection();
        _moveItem(from, to);

        void OnLayoutUpdated(object? sender, object e)
        {
            _listView.LayoutUpdated -= OnLayoutUpdated;
            // 布局回调里不改布局相关属性，推到这轮布局之后
            _listView.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _listView.ItemContainerTransitions = transitions;
                // 旧容器移除时会收到 PointerExited 盖上遮罩，新容器约 0.6 秒后才收到 PointerEntered（实测），这里补上，避免松手后闪暗
                if (_lingeringIcon is { IsSelected: false } icon)
                {
                    icon.MaskOpacity = 0;
                }
            });
        }
        // 必须等这次换位的布局走完才恢复过渡。低优先级任务不保证排在布局之后，直接排队时实测约两成会回弹：
        // 过渡照常播放，图标跳回原位再滑过去，被拖的图标消失半秒以上
        _listView.LayoutUpdated += OnLayoutUpdated;
    }


    /// <summary>
    /// 拖动结束时校正被拖图标的悬停遮罩。拖动期间指针被列表捕获，松手后图标不一定等得到 PointerExited
    /// （指针在窗口外或标题栏拖动区、甩到行外马上松手、换位换掉了容器），遮罩会一直亮着。
    /// 指针已离开就直接复位；还在图标上就定时查光标位置，等它离开时复位。这里靠不了事件：
    /// 新容器收到 PointerEntered 之前，指针直接移出窗口时列表收不到任何事件。
    /// </summary>
    /// <param name="finalSlot">图标最终所在的槽位。</param>
    /// <param name="e">结束拖动的指针事件。</param>
    private void SettleHover(int finalSlot, PointerRoutedEventArgs e)
    {
        if (_scrollViewer is null || _dragged is not Control container || _listView.ItemFromContainer(container) is not GameBizIcon icon)
        {
            return;
        }
        // 槽位在内容坐标里等距排列，换算回视口要算上开始拖动以来滚动的距离
        double slotX = _pressContentX - _pressViewport.X + _draggedOrigin.X + ((finalSlot - _originalSlot) * _pitch) - _offsetX;
        Thickness padding = container.Padding;
        Rect bounds = new(slotX + padding.Left,
                          _draggedOrigin.Y + padding.Top,
                          Math.Max(0, _pitch - padding.Left - padding.Right),
                          Math.Max(0, container.ActualHeight - padding.Top - padding.Bottom));
        // 手指抬起后指针就不存在了，也算离开
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch && bounds.Contains(e.GetCurrentPoint(_scrollViewer).Position))
        {
            _lingeringIcon = icon;
            _lingeringBounds = bounds;
            _lingerChecksLeft = LingerCheckCount;
            _lingerTimer.Start();
        }
        else if (!icon.IsSelected)
        {
            icon.MaskOpacity = 1;
        }
    }


    /// <summary>定时检查：光标离开松手时停留的图标就复位遮罩；查询次数用完后交给图标自己的进出事件。</summary>
    /// <param name="sender">定时器；此处不使用。</param>
    /// <param name="args">事件参数；此处不使用。</param>
    private void LingerTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        bool left = !IsCursorOver(_lingeringBounds);
        if (left && _lingeringIcon is { IsSelected: false } icon)
        {
            icon.MaskOpacity = 1;
        }
        if (left || --_lingerChecksLeft <= 0)
        {
            StopLingering();
        }
    }


    /// <summary>判断光标此刻是否在给定区域内。</summary>
    /// <param name="bounds">相对 ScrollViewer 的区域（DIP）。</param>
    /// <returns>在区域内时返回 <see langword="true"/>；取不到光标位置时返回 <see langword="false"/>。</returns>
    private bool IsCursorOver(Rect bounds)
    {
        if (_scrollViewer?.XamlRoot is not XamlRoot root || !User32.GetCursorPos(out var point))
        {
            return false;
        }
        User32.ScreenToClient((nint)root.ContentIslandEnvironment.AppWindowId.Value, ref point);
        Point origin = _scrollViewer.TransformToVisual(null).TransformPoint(default);
        double scale = root.RasterizationScale;
        return bounds.Contains(new Point((point.X / scale) - origin.X, (point.Y / scale) - origin.Y));
    }


    /// <summary>结束对松手时停留的图标的跟踪。</summary>
    private void StopLingering()
    {
        _lingerTimer.Stop();
        _lingeringIcon = null;
    }


    /// <summary>把位移写到指定槽位的容器；被虚拟化的容器为 null，跳过。</summary>
    /// <param name="slot">槽位。</param>
    /// <param name="translateX">横向位移（DIP）。</param>
    private void ApplyTranslate(int slot, double translateX)
    {
        if (_containers[slot] is FrameworkElement container)
        {
            GetTransform(container).TranslateX = translateX;
        }
    }


    /// <summary>复位单个容器的缩放、位移、不透明度与层级。</summary>
    /// <param name="container">容器；被虚拟化时为 null。</param>
    private static void ResetVisual(FrameworkElement? container)
    {
        if (container is null)
        {
            return;
        }
        if (container.RenderTransform is CompositeTransform t)
        {
            t.ScaleX = 1;
            t.ScaleY = 1;
            t.TranslateX = 0;
        }
        container.Opacity = 1;
        Canvas.SetZIndex(container, 0);
    }


    /// <summary>取元素上的 <see cref="CompositeTransform"/>，没有则创建并挂上。</summary>
    /// <param name="element">目标元素。</param>
    /// <returns>元素当前的变换。</returns>
    private static CompositeTransform GetTransform(UIElement element)
    {
        if (element.RenderTransform is CompositeTransform existing)
        {
            return existing;
        }
        CompositeTransform t = new();
        element.RenderTransform = t;
        return t;
    }


    /// <summary>沿可视化树向上查找所属的列表项；点在滚动条等非列表项上时返回 null。</summary>
    /// <param name="element">指针事件的原始来源。</param>
    /// <returns>所属列表项；没有时为 <see langword="null"/>。</returns>
    private ListViewItem? FindContainer(DependencyObject? element)
    {
        while (element is not null && !ReferenceEquals(element, _listView))
        {
            if (element is ListViewItem item)
            {
                return item;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }


    private void ClearState()
    {
        _pressed = false;
        _dragged = null;
        _pointer = null;
        _originalSlot = -1;
        _targetSlot = -1;
        _containers = [];
        _currentTx = [];
        _targetTx = [];
        _lastRenderTime = TimeSpan.Zero;
    }
}
