using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using System;
using System.Collections.Generic;
using Windows.Foundation;


namespace Starward.Features.Gacha;


/// <summary>抽卡统计卡片公开给拖拽逻辑的接口：拖拽手柄 + 其对应的数据。</summary>
internal interface IGachaStatsDragCard
{
    /// <summary>可发起拖拽的区域（tab 标签上方的卡池统计信息部分）。</summary>
    FrameworkElement DragHandle { get; }

    /// <summary>卡片对应的卡池统计数据（页面复用卡片时会重新赋值）。</summary>
    GachaTypeStats WarpTypeStats { get; set; }
}


/// <summary>
/// 让抽卡统计卡片支持「按住统计区域拖动、拖到另一张卡片处松手即换位」。
/// <para>卡片由页面以「常驻卡片池 + 手动管理 <see cref="StackPanel"/> 子元素」方式承载（不再走 <see cref="ItemsControl"/>/集合绑定）
/// <item>拖动视觉只用 XAML <see cref="CompositeTransform"/>（RenderTransform）+ <see cref="UIElement.Opacity"/> + <see cref="Canvas.ZIndex"/>，全部<b>直接赋值</b>。
/// 受影响卡片滑向虚拟槽位的过渡用 <see cref="CompositionTarget.Rendering"/> 每帧把 <c>TranslateX</c> 朝目标平滑逼近 —— <b>仍是逐帧直接赋值 RenderTransform</b>，不是 Storyboard、也不是 Composition。
/// <item>拖动中可用滚轮横向滚动，以够到视口外的第 5、6 个卡池。</item>
/// 「可拖拽集合」= 面板里 <see cref="UIElement.Visibility"/> 为 Visible 的卡片（被筛选隐藏的卡片以 Collapsed 常驻面板尾部、不参与拖拽）。
/// </summary>
internal sealed class GachaStatsCardDragReorder
{
    /// <summary>起拖阈值（像素）：指针自按下点移动超过该距离才进入拖动，避免误触。</summary>
    private const double DragThreshold = 6d;

    /// <summary>拖动中卡片的放大比例（轻微「拎起」感）。</summary>
    private const double LiftScale = 1.03d;

    /// <summary>拖动中卡片的不透明度（半透明「拎起」感）。</summary>
    private const double LiftOpacity = 0.85d;

    /// <summary>拖动中卡片的层级，确保浮在其余卡片之上。</summary>
    private const int LiftZIndex = 100;

    /// <summary>相邻两卡左缘间距的兜底值：卡宽 262 + 间隔 12。</summary>
    private const double FallbackPitch = 274d;

    /// <summary>受影响卡片滑向「虚拟槽位」的指数平滑时间常数（秒），越小越快跟手。</summary>
    private const double ReorderSmoothingTau = 0.045d;

    /// <summary>滑动收敛阈值（像素）：与目标差值小于此即吸附，结束该卡的逐帧微动。</summary>
    private const double ReorderSnapEpsilon = 0.5d;


    private readonly ScrollViewer _scrollViewer;
    private readonly FrameworkElement _content;
    private readonly Panel _panel;
    private readonly Action? _onReordered;

    // 拖拽状态
    private readonly List<FrameworkElement> _cards = new();
    private FrameworkElement? _handle;
    private GachaTypeStats? _item;
    private FrameworkElement? _dragged;
    private Pointer? _pointer;
    private uint _pointerId;
    private bool _pressed;
    private bool _dragging;
    private Point _pressContent;
    private Point _lastContent;
    private double _lastOffsetX;
    private int _originalSlot = -1;
    private int _targetSlot = -1;
    private double _firstCardX;
    private double _pitch = FallbackPitch;
    // 受影响卡片「滑动换位」动画状态：渲染循环每帧把 _currentTx 朝 _targetTx 平滑逼近（仅逐帧直接赋值 RenderTransform，DPI 安全）。
    private double[] _currentTx = Array.Empty<double>();
    private double[] _targetTx = Array.Empty<double>();
    private bool _animateReorder;
    private bool _renderingHooked;
    private TimeSpan _lastRenderTime;


    public GachaStatsCardDragReorder(ScrollViewer scrollViewer, FrameworkElement content,
        Panel panel, Action? onReordered = null)
    {
        _scrollViewer = scrollViewer;
        _content = content;
        _panel = panel;
        _onReordered = onReordered;
    }


    /// <summary>是否正处于拖动中（供页面级滚轮处理判断是否接管）。</summary>
    public bool IsDragging => _dragging;


    /// <summary>为一张卡片的拖拽手柄挂接指针事件。重复挂接安全（先解绑再绑定）。</summary>
    public void Attach(FrameworkElement handle)
    {
        if (handle is null)
        {
            return;
        }
        Detach(handle);
        handle.PointerPressed += OnPointerPressed;
        handle.PointerMoved += OnPointerMoved;
        handle.PointerReleased += OnPointerReleased;
        handle.PointerCaptureLost += OnPointerCaptureLost;
        handle.PointerCanceled += OnPointerCaptureLost;
        handle.PointerWheelChanged += OnPointerWheelChanged;
    }


    /// <summary>解除挂接；若正拖着该手柄则先取消拖动。</summary>
    public void Detach(FrameworkElement handle)
    {
        if (handle is null)
        {
            return;
        }
        handle.PointerPressed -= OnPointerPressed;
        handle.PointerMoved -= OnPointerMoved;
        handle.PointerReleased -= OnPointerReleased;
        handle.PointerCaptureLost -= OnPointerCaptureLost;
        handle.PointerCanceled -= OnPointerCaptureLost;
        handle.PointerWheelChanged -= OnPointerWheelChanged;
        if (ReferenceEquals(handle, _handle))
        {
            FinishDrag(commit: false);
        }
    }


    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed || sender is not FrameworkElement handle)
        {
            return;
        }
        PointerPoint point = e.GetCurrentPoint(_content);
        // 鼠标仅响应左键；触摸/笔按主接触点即可。
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (handle.DataContext is not GachaTypeStats item)
        {
            return;
        }
        _handle = handle;
        _item = item;
        _pointer = e.Pointer;
        _pointerId = e.Pointer.PointerId;
        _pressContent = point.Position;
        _lastContent = point.Position;
        _lastOffsetX = _scrollViewer.HorizontalOffset;
        _pressed = true;
        // 提前捕获指针：即便指针移出手柄/越过其它卡片，移动与松手仍回到本手柄。
        handle.CapturePointer(e.Pointer);
    }


    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || e.Pointer.PointerId != _pointerId)
        {
            return;
        }
        Point content = e.GetCurrentPoint(_content).Position;
        _lastContent = content;
        _lastOffsetX = _scrollViewer.HorizontalOffset;
        if (!_dragging)
        {
            double dx = content.X - _pressContent.X;
            double dy = content.Y - _pressContent.Y;
            if ((dx * dx) + (dy * dy) < DragThreshold * DragThreshold)
            {
                return;
            }
            if (!BeginDrag())
            {
                return;
            }
        }
        UpdateDrag(content);
        e.Handled = true;
    }


    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || e.Pointer.PointerId != _pointerId)
        {
            return;
        }
        bool wasDragging = _dragging;
        FinishDrag(commit: true);
        e.Handled = wasDragging;
    }


    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // 捕获被夺走视为中断：复位但不换位，避免意外移动。
        if (_pressed)
        {
            FinishDrag(commit: false);
        }
    }


    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        HandleWheel(e);
    }


    /// <summary>
    /// 拖动中处理滚轮：横向滚动并让卡片继续贴着指针、刷新落点目标。返回是否已接管。
    /// 页面级滚轮处理器也会转发到这里，确保捕获指针未把滚轮投递到手柄时仍能滚动。
    /// </summary>
    public bool HandleWheel(PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return false;
        }
        int delta = e.GetCurrentPoint(_scrollViewer).Properties.MouseWheelDelta;
        double scrollable = Math.Max(0, _scrollViewer.ScrollableWidth);
        double newOffset = Math.Clamp(_scrollViewer.HorizontalOffset - delta, 0, scrollable);
        _scrollViewer.ChangeView(newOffset, null, null, true);
        // 指针在屏幕上未动，仅偏移变化：同一屏幕点的内容横坐标按偏移增量平移。
        Point content = new(_lastContent.X + (newOffset - _lastOffsetX), _lastContent.Y);
        _lastContent = content;
        _lastOffsetX = newOffset;
        UpdateDrag(content);
        e.Handled = true;
        return true;
    }


    /// <summary>进入拖动：缓存可见卡片、量取几何、置顶并施加「拎起」视觉。手柄不属于面板任何卡片则放弃。</summary>
    private bool BeginDrag()
    {
        CollectVisibleCards();
        _dragged = FindCardChild(_handle);
        if (_dragged is null)
        {
            return false;
        }
        _originalSlot = _cards.IndexOf(_dragged);
        if (_originalSlot < 0)
        {
            return false;
        }
        // 在施加任何变换前量取首卡内容 X 与 pitch（命中检测纯解析、拖动期间不再读布局）。
        MeasureGeometry();
        _targetSlot = _originalSlot;
        // 初始化受影响卡片的滑动动画状态；按系统「显示动画」开关决定平滑滑动还是直接吸附。
        _currentTx = new double[_cards.Count];
        _targetTx = new double[_cards.Count];
        _animateReorder = EntranceAnimation.AnimationsEnabled();
        if (_animateReorder)
        {
            HookRendering();
        }
        // 「拎起」被拖卡片：直接赋值缩放/不透明度/层级，不做动画（DPI 安全）。
        CompositeTransform t = GetTransform(_dragged);
        t.CenterX = _dragged.ActualWidth / 2;
        t.CenterY = _dragged.ActualHeight / 2;
        t.ScaleX = LiftScale;
        t.ScaleY = LiftScale;
        t.TranslateX = 0;
        t.TranslateY = 0;
        _dragged.Opacity = LiftOpacity;
        Canvas.SetZIndex(_dragged, LiftZIndex);
        _dragging = true;
        return true;
    }


    /// <summary>采集面板里当前可见（未被筛选隐藏）的卡片，按子元素次序构成「可拖拽集合」。</summary>
    private void CollectVisibleCards()
    {
        _cards.Clear();
        foreach (UIElement child in _panel.Children)
        {
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                _cards.Add(fe);
            }
        }
    }


    /// <summary>量取首卡内容 X 与相邻卡左缘间距 pitch（仅在拖动开始、变换为恒等时调用一次）。</summary>
    private void MeasureGeometry()
    {
        _firstCardX = 0;
        _pitch = FallbackPitch;
        try
        {
            if (_cards.Count > 0)
            {
                _firstCardX = _cards[0].TransformToVisual(_content).TransformPoint(default).X;
            }
            if (_cards.Count > 1)
            {
                double x0 = _cards[0].TransformToVisual(_content).TransformPoint(default).X;
                double x1 = _cards[1].TransformToVisual(_content).TransformPoint(default).X;
                double p = x1 - x0;
                if (p > 1)
                {
                    _pitch = p;
                }
            }
            else if (_cards.Count == 1 && _cards[0].ActualWidth > 1)
            {
                _pitch = _cards[0].ActualWidth + 12;
            }
        }
        catch { }
    }


    /// <summary>跟随指针更新平移，并按解析命中刷新落点目标（直接换位）。</summary>
    private void UpdateDrag(Point content)
    {
        if (_dragged is null)
        {
            return;
        }
        CompositeTransform t = GetTransform(_dragged);
        t.TranslateX = content.X - _pressContent.X;
        t.TranslateY = content.Y - _pressContent.Y;
        int count = _cards.Count;
        int slot = (int)Math.Floor((content.X - _firstCardX) / _pitch);
        slot = Math.Clamp(slot, 0, Math.Max(0, count - 1));
        if (slot != _targetSlot)
        {
            _targetSlot = slot;
            ApplyVirtualLayout();
        }
    }


    /// <summary>
    /// 计算每张非拖拽卡片在「虚拟次序」下应落到的槽位偏移，写入 <see cref="_targetTx"/>；
    /// 启用动画时由渲染循环逐帧平滑滑过去，否则直接吸附（无过渡）。插入语义：非交换。
    /// </summary>
    private void ApplyVirtualLayout()
    {
        int d = _originalSlot;
        int target = _targetSlot;
        for (int s = 0; s < _cards.Count; s++)
        {
            if (s == d)
            {
                continue;
            }
            int virt = s;
            if (d < target)
            {
                if (s > d && s <= target)
                {
                    virt = s - 1;
                }
            }
            else if (d > target)
            {
                if (s >= target && s < d)
                {
                    virt = s + 1;
                }
            }
            _targetTx[s] = (virt - s) * _pitch;
            // 关闭系统动画时直接吸附；否则交由 OnRendering 逐帧平滑滑动到该目标。
            if (!_animateReorder)
            {
                _currentTx[s] = _targetTx[s];
                ApplyTranslate(_cards[s], _targetTx[s]);
            }
        }
    }


    /// <summary>订阅每帧渲染回调，用于把受影响卡片平滑滑动到虚拟槽位。</summary>
    private void HookRendering()
    {
        if (_renderingHooked)
        {
            return;
        }
        _renderingHooked = true;
        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }


    /// <summary>取消每帧渲染回调（拖动结束/中断时调用，避免空转与泄漏）。</summary>
    private void UnhookRendering()
    {
        if (!_renderingHooked)
        {
            return;
        }
        _renderingHooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }


    /// <summary>
    /// 每帧把受影响卡片的 <c>TranslateX</c> 朝目标槽位指数平滑逼近（仅<b>逐帧直接赋值 RenderTransform</b>，渲染变换、不参与 Measure、DPI 安全），
    /// 使「换位」有可见的滑动过程而非瞬切。被拖卡片（原槽）由指针跟随单独驱动，这里跳过。
    /// </summary>
    private void OnRendering(object? sender, object e)
    {
        if (!_dragging || _currentTx.Length == 0)
        {
            return;
        }
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
        double factor = 1.0 - Math.Exp(-dt / ReorderSmoothingTau);
        for (int s = 0; s < _cards.Count; s++)
        {
            if (s == _originalSlot)
            {
                continue;
            }
            double cur = _currentTx[s];
            double tgt = _targetTx[s];
            double diff = tgt - cur;
            if (Math.Abs(diff) < ReorderSnapEpsilon)
            {
                if (cur == tgt)
                {
                    continue;
                }
                cur = tgt;
            }
            else
            {
                cur += diff * factor;
            }
            _currentTx[s] = cur;
            ApplyTranslate(_cards[s], cur);
        }
    }


    /// <summary>把卡片的水平位移直接写到其 <see cref="CompositeTransform"/>（缩放归一，仅渲染变换）。</summary>
    private static void ApplyTranslate(FrameworkElement card, double translateX)
    {
        CompositeTransform t = GetTransform(card);
        t.TranslateX = translateX;
        t.TranslateY = 0;
        t.ScaleX = 1;
        t.ScaleY = 1;
    }


    /// <summary>
    /// 结束拖动：可选地提交最终次序，随后复位所有视觉并释放捕获。
    /// 先置 <c>_pressed=false</c>，避免 <see cref="UIElement.ReleasePointerCapture"/> 触发的 CaptureLost 重入。
    /// </summary>
    private void FinishDrag(bool commit)
    {
        if (!_pressed)
        {
            return;
        }
        _pressed = false;
        bool wasDragging = _dragging;
        _dragging = false;
        // 先停掉滑动渲染循环，避免它与下面的复位/Move 抢着改变换。
        UnhookRendering();
        try
        {
            if (wasDragging)
            {
                CommitOrder(commit);
            }
        }
        catch { }
        finally
        {
            try
            {
                if (_handle is not null && _pointer is not null)
                {
                    _handle.ReleasePointerCapture(_pointer);
                }
            }
            catch { }
            ClearState();
        }
    }


    /// <summary>
    /// 先清掉所有卡片的变换/不透明度/层级，再用一次 <see cref="UIElementCollection.Move(uint, uint)"/> 重排面板子元素应用最终次序。
    /// <para>用 <c>Children.Move</c> 而非「移除集合项再插入」是关键：据官方文档它<b>不重建可视化树</b>（卡片不 Unloaded/不重新 inflate），
    /// 从而避免「松手时所有卡片重建」的卡顿。清变换与 Move 在同一同步块内完成（无中间帧→无闪烁）；<b>不调</b> <c>UpdateLayout</c>。</para>
    /// </summary>
    private void CommitOrder(bool commit)
    {
        foreach (FrameworkElement c in _cards)
        {
            ResetVisual(c);
        }
        if (!commit)
        {
            return;
        }
        if (_originalSlot == _targetSlot || _dragged is null)
        {
            return;
        }
        if (_targetSlot < 0 || _targetSlot >= _cards.Count)
        {
            return;
        }
        // 槽位是「可见卡片」的序号，换算成面板真实子元素索引（被拖卡片 → 目标槽位卡片当前所在位置）。
        int fromChild = _panel.Children.IndexOf(_dragged);
        int toChild = _panel.Children.IndexOf(_cards[_targetSlot]);
        if (fromChild < 0 || toChild < 0 || fromChild == toChild)
        {
            return;
        }
        _panel.Children.Move((uint)fromChild, (uint)toChild);
        _onReordered?.Invoke();
    }


    /// <summary>复位单张卡片的缩放/平移/不透明度/层级。</summary>
    private static void ResetVisual(FrameworkElement? c)
    {
        if (c is null)
        {
            return;
        }
        if (c.RenderTransform is CompositeTransform t)
        {
            t.ScaleX = 1;
            t.ScaleY = 1;
            t.TranslateX = 0;
            t.TranslateY = 0;
        }
        c.Opacity = 1;
        Canvas.SetZIndex(c, 0);
    }


    /// <summary>取卡片上的 <see cref="CompositeTransform"/>（RenderTransform），无则创建并挂上。</summary>
    private static CompositeTransform GetTransform(UIElement element)
    {
        if (element.RenderTransform is CompositeTransform ct)
        {
            return ct;
        }
        CompositeTransform t = new();
        element.RenderTransform = t;
        return t;
    }


    private void ClearState()
    {
        _handle = null;
        _item = null;
        _cards.Clear();
        _dragged = null;
        _pointer = null;
        _pointerId = 0;
        _originalSlot = -1;
        _targetSlot = -1;
        _currentTx = Array.Empty<double>();
        _targetTx = Array.Empty<double>();
        _animateReorder = false;
        _lastRenderTime = TimeSpan.Zero;
    }


    /// <summary>沿可视化树向上查找「手柄所属、且直接位于面板下」的卡片元素。</summary>
    private FrameworkElement? FindCardChild(DependencyObject? element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(current);
            if (parent is null)
            {
                return null;
            }
            if (ReferenceEquals(parent, _panel))
            {
                return current as FrameworkElement;
            }
            current = parent;
        }
        return null;
    }
}
