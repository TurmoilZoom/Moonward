using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 自定义功能栏对话框里的行拖动排序：按住一行上下拖，经过的行实时让位，松手时一次性换位。
/// </summary>
/// <remarks>
/// <para>系统拖放在管理员权限下用不了（microsoft-ui-xaml#7690），列表自带的 <c>CanReorderItems</c> 也就不能用，这里自己处理指针。</para>
/// <para>参照 <c>GachaStatsCardDragReorder</c>：拖动视觉只直接赋值 <see cref="TranslateTransform"/>、<see cref="UIElement.Opacity"/> 与
/// <see cref="Canvas.ZIndexProperty"/>，让位的过渡在 <see cref="CompositionTarget.Rendering"/> 里逐帧赋值，
/// 不用 Storyboard、不写 Composition（被测量的元素上写 Composition 会在跨缩放换屏时崩溃）。
/// 换位只在松手时用 <see cref="UIElementCollection.Move(uint, uint)"/> 做一次：它不重建可视树，行不会 Unloaded。</para>
/// </remarks>
internal sealed class RightToolbarRowDragReorder
{

    /// <summary>按下后移动超过该距离才算拖动。</summary>
    private const double DragThreshold = 4;

    /// <summary>让位过渡的平滑时间常数（秒），与抽卡卡片一致。</summary>
    private const double SmoothingTau = 0.045;

    /// <summary>离目标小于该距离就直接吸附。</summary>
    private const double SnapEpsilon = 0.5;

    /// <summary>被拖行的层级，压在其他行上面。</summary>
    private const int LiftZIndex = 100;


    private readonly StackPanel _panel;

    private readonly Action _onReordered;

    private readonly List<RightToolbarCustomizeRow> _rows = new();

    private RightToolbarCustomizeRow? _dragged;

    private Pointer? _pointer;

    private uint _pointerId;

    private Point _pressPoint;

    private bool _pressed;

    private bool _dragging;

    private bool _animate;

    private bool _renderingHooked;

    private TimeSpan _lastRenderTime;

    private int _originalSlot = -1;

    private int _targetSlot = -1;

    /// <summary>首行在面板里的 Y 与相邻两行上缘的间距，拖动开始时量一次。</summary>
    private double _firstRowY;

    private double _pitch;

    private double[] _currentY = [];

    private double[] _targetY = [];


    /// <summary>
    /// 创建拖动排序。
    /// </summary>
    /// <param name="panel">纵向排列各行的面板，行是它的直接子元素。</param>
    /// <param name="onReordered">换位提交后调用，此时面板子元素已是新顺序。</param>
    public RightToolbarRowDragReorder(StackPanel panel, Action onReordered)
    {
        _panel = panel;
        _onReordered = onReordered;
    }


    /// <summary>
    /// 让一行可以拖动。行加入面板时调用。
    /// </summary>
    /// <param name="row">行。</param>
    public void Attach(RightToolbarCustomizeRow row)
    {
        row.PointerPressed += OnPointerPressed;
        row.PointerMoved += OnPointerMoved;
        row.PointerReleased += OnPointerReleased;
        row.PointerCanceled += OnPointerCanceled;
        row.PointerCaptureLost += OnPointerCaptureLost;
    }


    /// <summary>
    /// 解除一行的拖动。行移出面板前调用；正拖着它时按取消处理。
    /// </summary>
    /// <param name="row">行。</param>
    public void Detach(RightToolbarCustomizeRow row)
    {
        row.PointerPressed -= OnPointerPressed;
        row.PointerMoved -= OnPointerMoved;
        row.PointerReleased -= OnPointerReleased;
        row.PointerCanceled -= OnPointerCanceled;
        row.PointerCaptureLost -= OnPointerCaptureLost;
        if (ReferenceEquals(row, _dragged))
        {
            FinishDrag(commit: false);
        }
    }



    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed || sender is not RightToolbarCustomizeRow row)
        {
            return;
        }
        if (row.IsInToggle(e.OriginalSource as DependencyObject))
        {
            return;
        }
        var point = e.GetCurrentPoint(_panel);
        // 鼠标只认左键；触摸 / 笔按主接触点即可
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        _dragged = row;
        _pointer = e.Pointer;
        _pointerId = e.Pointer.PointerId;
        _pressPoint = point.Position;
        _pressed = true;
        // 行里没有其他要抢指针的控件（开关已排除），按下就捕获，拖出行外移动与松手仍回到这一行
        row.CapturePointer(e.Pointer);
        e.Handled = true;
    }


    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || e.Pointer.PointerId != _pointerId)
        {
            return;
        }
        Point point = e.GetCurrentPoint(_panel).Position;
        if (!_dragging)
        {
            double dx = point.X - _pressPoint.X;
            double dy = point.Y - _pressPoint.Y;
            if ((dx * dx) + (dy * dy) < DragThreshold * DragThreshold)
            {
                return;
            }
            if (!BeginDrag())
            {
                FinishDrag(commit: false);
                return;
            }
        }
        UpdateDrag(point);
        e.Handled = true;
    }


    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || e.Pointer.PointerId != _pointerId)
        {
            return;
        }
        FinishDrag(commit: true);
        e.Handled = true;
    }


    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed && e.Pointer.PointerId == _pointerId)
        {
            FinishDrag(commit: false);
        }
    }


    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // 捕获被夺走视为中断：复位但不换位
        if (_pressed)
        {
            FinishDrag(commit: false);
        }
    }


    /// <summary>
    /// 进入拖动：收集各行、量几何（此时还没有任何变换），拎起被拖行。
    /// </summary>
    /// <returns>被拖行不在面板里时为 <see langword="false"/>。</returns>
    private bool BeginDrag()
    {
        _rows.Clear();
        foreach (UIElement child in _panel.Children)
        {
            if (child is RightToolbarCustomizeRow row && row.Visibility == Visibility.Visible)
            {
                _rows.Add(row);
            }
        }
        if (_dragged is null)
        {
            return false;
        }
        _originalSlot = _rows.IndexOf(_dragged);
        if (_originalSlot < 0)
        {
            return false;
        }
        MeasureGeometry();
        _targetSlot = _originalSlot;
        _currentY = new double[_rows.Count];
        _targetY = new double[_rows.Count];
        _animate = EntranceAnimation.AnimationsEnabled();
        if (_animate)
        {
            HookRendering();
        }
        _dragged.SetDragging(true);
        Canvas.SetZIndex(_dragged, LiftZIndex);
        _dragging = true;
        return true;
    }


    /// <summary>
    /// 量首行 Y 与行间距。只在拖动开始、各行还没有变换时调用。
    /// </summary>
    private void MeasureGeometry()
    {
        _firstRowY = 0;
        _pitch = _dragged is null ? 1 : Math.Max(1, _dragged.ActualHeight + _panel.Spacing);
        try
        {
            if (_rows.Count > 0)
            {
                _firstRowY = _rows[0].TransformToVisual(_panel).TransformPoint(default).Y;
            }
            if (_rows.Count > 1)
            {
                double y1 = _rows[1].TransformToVisual(_panel).TransformPoint(default).Y;
                if (y1 - _firstRowY > 1)
                {
                    _pitch = y1 - _firstRowY;
                }
            }
        }
        catch { }
    }


    /// <summary>
    /// 被拖行跟随指针（夹在首尾两行之间），按它的中心落在哪个槽位刷新其他行的让位目标。
    /// </summary>
    /// <param name="point">指针在面板里的位置。</param>
    private void UpdateDrag(Point point)
    {
        if (_dragged is null)
        {
            return;
        }
        double minOffset = -_originalSlot * _pitch;
        double maxOffset = (_rows.Count - 1 - _originalSlot) * _pitch;
        double offset = Math.Clamp(point.Y - _pressPoint.Y, minOffset, maxOffset);
        GetTransform(_dragged).Y = offset;
        double center = _firstRowY + (_originalSlot * _pitch) + offset + (_pitch / 2);
        int slot = Math.Clamp((int)Math.Floor((center - _firstRowY) / _pitch), 0, Math.Max(0, _rows.Count - 1));
        if (slot != _targetSlot)
        {
            _targetSlot = slot;
            ApplyVirtualLayout();
        }
    }


    /// <summary>
    /// 按插入语义算出其他各行的让位偏移：被拖行从原槽移到目标槽，中间的行整体挪一格。
    /// 开着动画时交给渲染回调逐帧滑过去，否则直接到位。
    /// </summary>
    private void ApplyVirtualLayout()
    {
        int from = _originalSlot;
        int to = _targetSlot;
        for (int s = 0; s < _rows.Count; s++)
        {
            if (s == from)
            {
                continue;
            }
            int virtualSlot = s;
            if (from < to && s > from && s <= to)
            {
                virtualSlot = s - 1;
            }
            else if (from > to && s >= to && s < from)
            {
                virtualSlot = s + 1;
            }
            _targetY[s] = (virtualSlot - s) * _pitch;
            if (!_animate)
            {
                _currentY[s] = _targetY[s];
                GetTransform(_rows[s]).Y = _targetY[s];
            }
        }
    }


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
    /// 每帧把让位行的 Y 朝目标指数逼近（直接赋值渲染变换，不参与测量）。
    /// </summary>
    private void OnRendering(object? sender, object e)
    {
        if (!_dragging)
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
        double factor = 1.0 - Math.Exp(-dt / SmoothingTau);
        for (int s = 0; s < _rows.Count; s++)
        {
            if (s == _originalSlot || _currentY[s] == _targetY[s])
            {
                continue;
            }
            double next = _currentY[s] + ((_targetY[s] - _currentY[s]) * factor);
            if (Math.Abs(_targetY[s] - next) < SnapEpsilon)
            {
                next = _targetY[s];
            }
            _currentY[s] = next;
            GetTransform(_rows[s]).Y = next;
        }
    }


    /// <summary>
    /// 结束拖动：复位视觉，按需提交新顺序，释放捕获。
    /// 先置 <c>_pressed=false</c>，释放捕获触发的 CaptureLost 不会重入。
    /// </summary>
    /// <param name="commit">是否提交换位。</param>
    private void FinishDrag(bool commit)
    {
        if (!_pressed)
        {
            return;
        }
        _pressed = false;
        bool wasDragging = _dragging;
        _dragging = false;
        UnhookRendering();
        RightToolbarCustomizeRow? dragged = _dragged;
        Pointer? pointer = _pointer;
        try
        {
            if (wasDragging)
            {
                foreach (RightToolbarCustomizeRow row in _rows)
                {
                    GetTransform(row).Y = 0;
                    Canvas.SetZIndex(row, 0);
                }
                dragged?.SetDragging(false);
                if (commit && dragged is not null && _targetSlot != _originalSlot && _targetSlot >= 0 && _targetSlot < _rows.Count)
                {
                    int fromChild = _panel.Children.IndexOf(dragged);
                    int toChild = _panel.Children.IndexOf(_rows[_targetSlot]);
                    if (fromChild >= 0 && toChild >= 0 && fromChild != toChild)
                    {
                        _panel.Children.Move((uint)fromChild, (uint)toChild);
                        _onReordered();
                    }
                }
            }
        }
        finally
        {
            try
            {
                if (dragged is not null && pointer is not null)
                {
                    dragged.ReleasePointerCapture(pointer);
                }
            }
            catch { }
            _rows.Clear();
            _dragged = null;
            _pointer = null;
            _pointerId = 0;
            _originalSlot = -1;
            _targetSlot = -1;
            _currentY = [];
            _targetY = [];
            _lastRenderTime = TimeSpan.Zero;
        }
    }


    /// <summary>
    /// 取元素上的 <see cref="TranslateTransform"/>，没有就挂一个。
    /// </summary>
    /// <param name="element">行。</param>
    /// <returns>平移变换。</returns>
    private static TranslateTransform GetTransform(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }
        transform = new TranslateTransform();
        element.RenderTransform = transform;
        return transform;
    }


}
