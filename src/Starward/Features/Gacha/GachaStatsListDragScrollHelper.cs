using CommunityToolkit.WinUI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;


namespace Starward.Features.Gacha;

/// <summary>
/// 让抽卡统计卡片内的记录列表支持鼠标左键拖拽滚动：跟手拖动、松手惯性甩动、拖到边界时内容可被「拉出」一段距离并松手回弹。
/// <para>通过 <see cref="Bind"/> 为指定 <see cref="ScrollViewer"/> 注入该行为，返回绑定句柄（需在控件 Unloaded 时 Dispose）。</para>
/// <para>仅接管 <see cref="PointerDeviceType.Mouse"/> 的左键拖拽；触屏/笔的原生 manipulation 完全保留。</para>
/// <para>
/// 惯性不能交给 <see cref="ScrollViewer.ChangeView"/> 的原生缓动：拖拽跟手必须连续
/// <c>ChangeView(disableAnimation: true)</c>，紧接着再发一次带动画的 <c>ChangeView</c>
/// 会被 ScrollViewer 直接丢掉（返回 <c>false</c>）。因此甩动与跟手共用同一套虚拟偏移，
/// 由 <see cref="CompositionTarget.Rendering"/> 按帧积分。
/// </para>
/// <para>拖拽或甩动中一旦滚动滚轮，立即结束自管滚动、交给 ScrollViewer 原生滚轮动画，避免两边争抢偏移。</para>
/// <para>除抽卡记录列表外，米游社工具箱各页左侧的记录 <see cref="ListView"/> 也经 <see cref="BindSelectableList"/> 复用本行为（XAML 入口见 <c>GameRecordListDragScroll</c>）。</para>
/// </summary>
internal static class GachaStatsListDragScrollHelper
{
    /// <summary>回弹位移的渐近上限（像素）。实际拉出量由此与拖拽距离做橡皮筋阻尼：<c>offset*c/(offset+c)</c>。</summary>
    private const double MaxOverscrollPull = 72d;

    /// <summary>松手回弹动画时长（毫秒）。</summary>
    private const int SpringBackDurationMs = 300;

    /// <summary>甩动速度的指数衰减时间常数（秒）。剩余路程约为 <c>|vel| * tau</c>，值越大滑得越远。</summary>
    private const double FlingTimeConstant = 0.35;

    /// <summary>甩动触发的最低速度阈值（像素/秒）。低于此值视为停住，不触发惯性滚动。</summary>
    private const double MinFlingVelocity = 150d;

    /// <summary>甩动过程中速度衰减到此阈值（像素/秒）即停止，避免无限逼近。</summary>
    private const double FlingStopVelocity = 40d;

    /// <summary>判定仍存在过拉位移的最小绝对值（像素），避免浮点残差挡住甩动。</summary>
    private const double OverscrollEpsilon = 0.5;

    /// <summary>可点选列表中，按下后竖直移动超过此距离（DIP）才算拖拽；取系统拖放阈值 SM_CYDRAG 的默认值。</summary>
    private const double ItemClickDragThreshold = 4d;


    /// <summary>
    /// 为指定的 <see cref="ScrollViewer"/> 注入鼠标拖拽滚动 + 回弹行为。
    /// </summary>
    /// <param name="scrollViewer">包含记录列表的竖直滚动容器。</param>
    /// <param name="onDragEnd">拖拽结束（松手 / 捕获丢失）时、释放指针捕获之前回调（原地单击未移动不回调）。拖拽期间指针被捕获、记录项收不到 PointerExited，卡片在此按当前指针位置校正悬停高亮（保持 / 切换 / 清除）。</param>
    /// <param name="onDragSettled">内容彻底停稳（松手无甩动 / 回弹动画结束 / 甩动停止）后回调，供卡片按最终指针位置校正悬停高亮。</param>
    /// <returns>绑定句柄，须在控件 Unloaded 时调用 <see cref="GachaStatsListDragScrollBinding.Dispose"/> 解除绑定。</returns>
    public static GachaStatsListDragScrollBinding Bind(ScrollViewer scrollViewer, Action? onDragEnd = null, Action? onDragSettled = null)
    {
        return new GachaStatsListDragScrollBinding(scrollViewer, onDragEnd, onDragSettled);
    }


    /// <summary>
    /// 为可点选的 <see cref="ListViewBase"/> 注入同样的拖拽滚动 + 回弹：原地单击仍交给列表项完成选中，
    /// 按下后拖动超过 <see cref="ItemClickDragThreshold"/> 才接管为拖拽（此时列表项的按下被取消，不会选中）。
    /// </summary>
    /// <param name="listView">目标列表；须已应用模板（在 Loaded 及之后调用）。</param>
    /// <returns>绑定句柄，须在页面卸载时 Dispose；模板内找不到 <see cref="ScrollViewer"/> 时返回 <c>null</c>。</returns>
    public static GachaStatsListDragScrollBinding? BindSelectableList(ListViewBase listView)
    {
        if (listView.FindDescendant<ScrollViewer>() is not ScrollViewer scrollViewer || scrollViewer.Content is not FrameworkElement)
        {
            return null;
        }
        return new GachaStatsListDragScrollBinding(scrollViewer, itemsHandleClick: true);
    }


    /// <summary>橡皮筋阻尼：渐近接近 <paramref name="cap"/> 的平滑饱和曲线。</summary>
    private static double Rubber(double offset, double cap)
    {
        return offset * cap / (offset + cap);
    }


    internal sealed class GachaStatsListDragScrollBinding : IDisposable
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly FrameworkElement _content;
        private readonly Visual _contentVisual;
        private readonly Action? _onDragEnd;
        private readonly Action? _onDragSettled;
        private readonly bool _itemsHandleClick;
        private readonly PointerEventHandler _pressedHandler;
        private readonly PointerEventHandler _movedHandler;
        private readonly PointerEventHandler _releasedHandler;
        private readonly PointerEventHandler _captureLostHandler;
        private readonly PointerEventHandler _canceledHandler;
        private readonly PointerEventHandler _wheelHandler;
        private bool _disposed;

        // 可点选列表：已按下但还没拖过阈值，指针仍归列表项（等待判定单击还是拖拽）
        private bool _pressPending;
        private Point _pressPosition;
        private SelectorItem? _pressedItem;   // 收到这次按下的列表项容器（ListViewItem）

        // 可点选列表的悬停高亮：拖拽中指针被本容器捕获，列表项收不到进出事件，由这里按指针位置代为切换可视状态。
        // _hoverItem 是当前由本类设成 PointerOver 的项；停稳后指针第一次移动即交还列表项自己管理。
        private SelectorItem? _hoverItem;
        private Point? _lastWindowPoint;      // 指针最近位置（窗口坐标），用于命中测试
        private XamlRoot? _suppressedRoot;    // 拖拽时对其设了 Tooltip 抑制的 XamlRoot；null = 当前没有抑制
        private readonly PointerEventHandler _exitedHandler;

        // 拖拽状态
        private bool _isDragging;
        private bool _dragMoved;       // 本次按下后指针是否移动过；原地单击不算拖拽，不触发 _onDragEnd
        private Pointer? _capturedPointer;
        private Point _lastPosition;
        private long _lastTimestamp;   // Stopwatch.GetTimestamp()
        private double _velocity;      // px/s，EMA 平滑后的速度（指针向下为正）

        // 回弹位移（Content 的 Composition Translation.Y）：正 = 下拉（顶部拉出），负 = 上推（底部拉出）。
        private double _overscrollY;

        // 权威逻辑偏移：不每帧读 VerticalOffset（ChangeView 异步生效，会读到过期值导致抖动），
        // 而是自己维持一个与用户手指严格一致的虚拟偏移，再分摊给 ChangeView（clamped 部分）与 Composition Translation（overflow 部分）。
        private double _virtualOffset;
        private double _lastAppliedClamped;

        // 松手后的惯性积分
        private bool _flinging;
        private bool _renderingHooked;
        private TimeSpan _lastRenderTime;

        private static readonly double TickToSeconds = 1.0 / Stopwatch.Frequency;


        /// <summary>
        /// 创建绑定并挂上指针事件。
        /// </summary>
        /// <param name="scrollViewer">竖直滚动容器，其 Content 须为 <see cref="FrameworkElement"/>（过拉位移挂在它的 Translation 上）。</param>
        /// <param name="onDragEnd">见 <see cref="Bind"/>。</param>
        /// <param name="onDragSettled">见 <see cref="Bind"/>。</param>
        /// <param name="itemsHandleClick">
        /// 列表项自己要处理点击（如 <see cref="ListViewItem"/> 的选中）：按下时不抢指针捕获，拖过阈值才接管；
        /// 并以 handledEventsToo 监听按下 / 移动 / 松开，因为列表项会把这些事件置为 Handled。
        /// </param>
        /// <exception cref="InvalidOperationException"><paramref name="scrollViewer"/> 的 Content 不是 <see cref="FrameworkElement"/>。</exception>
        public GachaStatsListDragScrollBinding(ScrollViewer scrollViewer, Action? onDragEnd = null, Action? onDragSettled = null, bool itemsHandleClick = false)
        {
            _scrollViewer = scrollViewer;
            _content = scrollViewer.Content as FrameworkElement
                ?? throw new InvalidOperationException("ScrollViewer.Content must be a FrameworkElement.");
            _contentVisual = ElementCompositionPreview.GetElementVisual(_content);
            ElementCompositionPreview.SetIsTranslationEnabled(_content, true);
            _onDragEnd = onDragEnd;
            _onDragSettled = onDragSettled;
            _itemsHandleClick = itemsHandleClick;

            _pressedHandler = OnPointerPressed;
            _movedHandler = OnPointerMoved;
            _releasedHandler = OnPointerReleased;
            _captureLostHandler = OnPointerCaptureLost;
            _canceledHandler = OnPointerCanceled;
            // 抽卡记录项不处理指针，普通订阅即可；保持原样，免得记录里的可点控件（已置 Handled 的按下）也被当成拖拽起点。
            _scrollViewer.AddHandler(UIElement.PointerPressedEvent, _pressedHandler, itemsHandleClick);
            _scrollViewer.AddHandler(UIElement.PointerMovedEvent, _movedHandler, itemsHandleClick);
            _scrollViewer.AddHandler(UIElement.PointerReleasedEvent, _releasedHandler, itemsHandleClick);
            _scrollViewer.AddHandler(UIElement.PointerCaptureLostEvent, _captureLostHandler, itemsHandleClick);
            _scrollViewer.AddHandler(UIElement.PointerCanceledEvent, _canceledHandler, itemsHandleClick);
            _exitedHandler = OnPointerExited;
            if (itemsHandleClick)
            {
                _scrollViewer.AddHandler(UIElement.PointerExitedEvent, _exitedHandler, handledEventsToo: true);
            }
            // ScrollViewer 在自身 OnPointerWheelChanged 里处理滚轮并置 Handled，须 handledEventsToo 才收得到。
            _wheelHandler = OnPointerWheelChanged;
            _scrollViewer.AddHandler(UIElement.PointerWheelChangedEvent, _wheelHandler, handledEventsToo: true);
        }


        /// <summary>
        /// 是否正在鼠标左键拖拽滚动中。
        /// 供统计卡片在指针移动时区分「用户拖拽列表」（内容在指针下移动，不能按指针位置清除悬停高亮）与普通悬停移动。
        /// </summary>
        internal bool IsDragging => _isDragging;


        #region Pointer Events

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 仅接管鼠标左键；触控/笔保留原生滚动。
            if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                return;
            }
            var props = e.GetCurrentPoint(_scrollViewer).Properties;
            if (!props.IsLeftButtonPressed)
            {
                return;
            }
            // 可点选列表连已处理的按下也监听：滚动条（含拖动滑块）自己处理的按下也会冒泡到这里，
            // 不能当成拖拽起点，否则过阈值时会抢走滑块的捕获，滑块拖不动、内容还反向走。
            if (_itemsHandleClick && IsInsideScrollBar(e.OriginalSource as DependencyObject))
            {
                return;
            }

            // 新按下打断进行中的甩动，并以当前真实偏移重新锚定。
            bool interruptedFling = _flinging;
            StopFling();
            double seed = _scrollViewer.VerticalOffset;
            try
            {
                _scrollViewer.ChangeView(null, seed, null, disableAnimation: true);
            }
            catch { }

            _virtualOffset = seed;
            _lastAppliedClamped = seed;
            _lastPosition = e.GetCurrentPoint(_scrollViewer).Position;
            _lastTimestamp = Stopwatch.GetTimestamp();
            _velocity = 0;
            _dragMoved = false;

            if (_itemsHandleClick)
            {
                _lastWindowPoint = e.GetCurrentPoint(null).Position;
                _pressedItem = FindItemContainer(e.OriginalSource as DependencyObject);
                // 新的按下由列表项自己显示按下态；之前代管的悬停到此为止。
                _hoverItem = null;
            }

            // 可点选列表：先不抢捕获，让列表项照常走按下→松开的选中流程，拖过阈值再接管。
            // 甩动中按下只是想让列表停住（同触屏点停惯性），直接进入拖拽，不选中按下的那项。
            if (_itemsHandleClick && !interruptedFling)
            {
                _pressPosition = _lastPosition;
                _pressPending = true;
                return;
            }
            BeginDrag(e.Pointer);
        }


        /// <summary>捕获指针进入拖拽状态。可点选列表里会抢走列表项的捕获，使其取消按下、不再选中。</summary>
        /// <param name="pointer">要捕获的鼠标指针。</param>
        private void BeginDrag(Pointer pointer)
        {
            _pressPending = false;
            SelectorItem? pressedItem = _pressedItem;
            _pressedItem = null;
            if (pressedItem is not null)
            {
                // ListViewItem 被鼠标按下时只记「左键已按下」并排队一次点击，自己不捕获指针；本容器接管后它收不到松开，
                // 会一直停在 Pressed 外观（指针移回来也只显示按下），松开时还可能照样选中。
                // 让它拿到捕获再立即释放，触发它自己的 PointerCaptureLost：清掉按下 / 悬停标记并取消排队的点击。
                // 不能拿着捕获直接转给本容器：那样本容器捕获失败，松开仍落到列表项上并选中它。
                try
                {
                    if (pressedItem.CapturePointer(pointer))
                    {
                        pressedItem.ReleasePointerCapture(pointer);
                    }
                }
                catch { }
            }
            _capturedPointer = pointer;
            _scrollViewer.CapturePointer(pointer);
            _isDragging = true;
            // 拖拽滚动时隐藏记录项上的时间气泡，并避免列表移动时反复弹出。
            // 抑制是整个窗口生效的；记下设置时的 XamlRoot，卸载时元素的 XamlRoot 可能已取不到。
            _suppressedRoot = _scrollViewer.XamlRoot;
            InstantTooltip.SetSuppressed(_suppressedRoot, true);
            if (pressedItem is not null)
            {
                // 内容跟着指针走，被拖的那项一直在指针下：拖拽中保持它的悬停高亮（同抽卡记录列表）。
                SetHoverItem(pressedItem);
            }
        }


        /// <summary>从命中元素向上找到所属的列表项容器（<see cref="SelectorItem"/>），找不到返回 null。</summary>
        /// <param name="element">命中的元素（如指针事件的 OriginalSource）。</param>
        /// <returns>列表项容器，或 null（在列表空白处）。</returns>
        private SelectorItem? FindItemContainer(DependencyObject? element)
        {
            for (DependencyObject? current = element; current is not null && !ReferenceEquals(current, _scrollViewer); current = VisualTreeHelper.GetParent(current))
            {
                if (current is SelectorItem item)
                {
                    return item;
                }
            }
            return null;
        }


        /// <summary>命中元素是否位于本容器的滚动条内。</summary>
        /// <param name="element">指针按下事件的 OriginalSource。</param>
        /// <returns>在 <see cref="ScrollBar"/> 内返回 true。</returns>
        private bool IsInsideScrollBar(DependencyObject? element)
        {
            for (DependencyObject? current = element; current is not null && !ReferenceEquals(current, _scrollViewer); current = VisualTreeHelper.GetParent(current))
            {
                if (current is ScrollBar)
                {
                    return true;
                }
            }
            return false;
        }


        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_itemsHandleClick)
            {
                _lastWindowPoint = e.GetCurrentPoint(null).Position;
                if (!_isDragging && !_pressPending && _hoverItem is not null)
                {
                    // 停稳后指针第一次移动：列表项自己的进出事件已能正常判定（若还在该项内，它会收到 PointerEntered 自行点亮），
                    // 代管到此结束；指针已不在该项上时，它收不到 PointerExited，须在这里撤掉代设的高亮。
                    if (!IsPointerOver(_hoverItem, e))
                    {
                        ApplyRestState(_hoverItem);
                    }
                    _hoverItem = null;
                }
            }

            if (_pressPending)
            {
                var pending = e.GetCurrentPoint(_scrollViewer);
                if (!pending.Properties.IsLeftButtonPressed)
                {
                    _pressPending = false;
                    return;
                }
                // 只看竖直位移：横向抖动不该把一次单击变成拖拽（列表本身也不横向滚动）。
                if (Math.Abs(pending.Position.Y - _pressPosition.Y) < ItemClickDragThreshold)
                {
                    return;
                }
                // 过阈值：接管，并把按下以来的位移一并计入，内容仍贴着指针。
                BeginDrag(e.Pointer);
            }

            if (!_isDragging)
            {
                return;
            }
            var point = e.GetCurrentPoint(_scrollViewer);
            if (!point.Properties.IsLeftButtonPressed)
            {
                // 部分设备会在 PointerReleased 之前先送来「左键已抬起」的 Moved，这里按松手处理，否则会跳过甩动。
                TryFlingOrRestore();
                return;
            }

            Point pos = point.Position;
            if (pos != _lastPosition)
            {
                _dragMoved = true;
            }
            double deltaY = pos.Y - _lastPosition.Y;
            long now = Stopwatch.GetTimestamp();
            double dt = (now - _lastTimestamp) * TickToSeconds;
            if (dt > 0)
            {
                double instant = deltaY / dt;
                // EMA 平滑速度：α 越大跟手越快、抖动越明显。
                _velocity = _velocity * 0.7 + instant * 0.3;
            }

            _virtualOffset -= deltaY;
            ApplyVirtualOffset();

            _lastPosition = pos;
            _lastTimestamp = now;
        }


        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            // 没拖过阈值就松开：是一次单击，交给列表项完成选中。
            _pressPending = false;
            _pressedItem = null;
            if (_itemsHandleClick)
            {
                _lastWindowPoint = e.GetCurrentPoint(null).Position;
            }
            if (!_isDragging)
            {
                return;
            }
            TryFlingOrRestore();
        }


        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            // 可点选列表里，接管拖拽时列表项丢失捕获，其 PointerCaptureLost 会冒泡上来；只认本容器自己的捕获丢失。
            if (!ReferenceEquals(e.OriginalSource, _scrollViewer))
            {
                return;
            }
            OnPointerCanceled(sender, e);
        }


        private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            _pressPending = false;
            _pressedItem = null;
            if (!_isDragging)
            {
                return;
            }
            // 捕获丢失 / 取消与正常松手走同一条路径：有速度就甩，没有则回弹/停住。
            TryFlingOrRestore();
        }


        /// <summary>
        /// 指针离开列表：撤掉代设的悬停高亮。离开时不一定经过一次「在列表内」的移动，列表项也收不到 PointerExited，不撤会残留。
        /// </summary>
        /// <param name="sender">事件源（ScrollViewer）。</param>
        /// <param name="e">列表项的 PointerExited 也会冒泡到这里，按指针是否仍在本容器内区分。</param>
        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging || _hoverItem is null || IsPointerOver(_scrollViewer, e))
            {
                return;
            }
            ApplyRestState(_hoverItem);
            _hoverItem = null;
        }


        /// <summary>
        /// 拖拽或甩动中收到滚轮：结束自管滚动，由 ScrollViewer 原生滚轮动画接管。
        /// </summary>
        /// <remarks>
        /// 到这里时 ScrollViewer 已启动滚轮动画，且无动画的 <c>ChangeView</c> 取消不了它；若继续跟手 / 逐帧积分，
        /// 每次都会把内容拉回自管位置，与滚轮动画来回拉扯，记录之间出现残影。
        /// 拖拽中指针被本容器捕获，滚轮直接路由到 ScrollViewer，无法先于它吞掉，所以拖拽也让位给滚轮（不甩动）。
        /// </remarks>
        /// <param name="sender">事件源（ScrollViewer）。</param>
        /// <param name="e">滚轮路由事件参数（通常已被 ScrollViewer 置为 Handled）。</param>
        private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            // 按住未拖动时滚了滚轮：按下时记下的锚点已过期，这次按下不再转成拖拽。
            _pressPending = false;
            _pressedItem = null;
            if (_isDragging)
            {
                _velocity = 0;
                TryFlingOrRestore();
            }
            else if (_flinging)
            {
                StopFling();
                RestoreTooltip();
                // 甩动被滚轮打断：内容停在当前位置，直接按停稳处理。
                NotifySettled();
            }
        }

        #endregion


        #region Drag End — Fling / Overscroll Restore

        /// <summary>
        /// 结束拖拽：优先回弹（若存在过拉位移），否则按当前速度启动自管惯性积分。
        /// </summary>
        private void TryFlingOrRestore()
        {
            if (!_isDragging)
            {
                return;
            }

            // 拖拽期间指针被本容器捕获，记录项收不到 PointerExited，悬停状态只能靠回调校正：
            // 让卡片按当前指针位置命中，保持 / 切换 / 清除高亮。原地单击记录时指针没离开过该记录，高亮仍然有效，不回调。
            // 必须排在 EndDragSession 释放捕获之前：释放捕获时指针下方的记录可能补发 PointerEntered，
            // 回调先校正后，补发的 Entered 只会把高亮设到同一位置，不会与回调互相清掉。
            if (_dragMoved)
            {
                _onDragEnd?.Invoke();
            }
            EndDragSession();

            if (Math.Abs(_overscrollY) > OverscrollEpsilon)
            {
                PlaySpringBackAnimation();
                RestoreTooltip();
                return;
            }

            if (Math.Abs(_velocity) > MinFlingVelocity)
            {
                StartFling();
                return;
            }

            // 无甩动也无回弹：内容已停稳，通知回调方按最终位置校正。
            RestoreTooltip();
            NotifySettled();
        }


        /// <summary>结束拖拽会话：清状态并释放指针捕获。Tooltip 抑制保持到甩动/回弹结束。</summary>
        private void EndDragSession()
        {
            _isDragging = false;
            try
            {
                if (_capturedPointer is not null)
                {
                    _scrollViewer.ReleasePointerCapture(_capturedPointer);
                }
            }
            catch { }
            _capturedPointer = null;
        }

        #endregion


        #region Self-driven fling

        /// <summary>开始按帧积分甩动。必须自管：ScrollViewer 带动画的 ChangeView 在无动画 ChangeView 之后会被丢掉。</summary>
        private void StartFling()
        {
            _flinging = true;
            HookRendering();
        }


        /// <summary>停止甩动积分并卸掉渲染回调。</summary>
        private void StopFling()
        {
            _flinging = false;
            UnhookRendering();
        }


        private void HookRendering()
        {
            if (_renderingHooked)
            {
                return;
            }
            _renderingHooked = true;
            _lastRenderTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFlingRendering;
        }


        private void UnhookRendering()
        {
            if (!_renderingHooked)
            {
                return;
            }
            _renderingHooked = false;
            CompositionTarget.Rendering -= OnFlingRendering;
        }


        /// <summary>
        /// 每帧把速度按指数衰减积分进虚拟偏移，再写回 ScrollViewer。
        /// 冲出边界时把剩余位移交给橡皮筋，然后回弹。
        /// </summary>
        private void OnFlingRendering(object? sender, object e)
        {
            if (!_flinging || _disposed)
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

            // v(t)=v0*e^{-t/τ}，剩余路程 |v|τ，与原先 targetOffset = current - vel * tau 一致。
            _virtualOffset -= _velocity * dt;
            _velocity *= Math.Exp(-dt / FlingTimeConstant);
            ApplyVirtualOffset();

            double maxOffset = _scrollViewer.ScrollableHeight;
            bool pastEdge = _virtualOffset < 0 || _virtualOffset > maxOffset;
            if (pastEdge)
            {
                StopFling();
                _virtualOffset = Math.Clamp(_virtualOffset, 0, maxOffset);
                if (Math.Abs(_overscrollY) > OverscrollEpsilon)
                {
                    // 有回弹位移：停稳回调由回弹动画批次完成时补发。
                    PlaySpringBackAnimation();
                    RestoreTooltip();
                }
                else
                {
                    RestoreTooltip();
                    NotifySettled();
                }
                return;
            }

            if (Math.Abs(_velocity) < FlingStopVelocity)
            {
                StopFling();
                RestoreTooltip();
                NotifySettled();
            }
        }

        #endregion


        #region Virtual offset → ScrollViewer + overscroll

        /// <summary>
        /// 把 <see cref="_virtualOffset"/> 拆成 clamped（<see cref="ScrollViewer.ChangeView"/>）与 overflow（Composition Translation）。
        /// </summary>
        private void ApplyVirtualOffset()
        {
            double maxOffset = _scrollViewer.ScrollableHeight;
            double clamped = Math.Clamp(_virtualOffset, 0, maxOffset);
            double overflow = _virtualOffset - clamped;

            if (Math.Abs(clamped - _lastAppliedClamped) > 0.01)
            {
                try
                {
                    _scrollViewer.ChangeView(null, clamped, null, disableAnimation: true);
                    _lastAppliedClamped = clamped;
                }
                catch { }
            }

            _overscrollY = -Math.Sign(overflow) * Rubber(Math.Abs(overflow), MaxOverscrollPull);
            ApplyOverscrollInstant();
        }


        /// <summary>直接（无动画）将 <see cref="_overscrollY"/> 写到 Content 的 Composition Translation.Y。</summary>
        private void ApplyOverscrollInstant()
        {
            try
            {
                _contentVisual.Properties.InsertVector3("Translation", new Vector3(0, (float)_overscrollY, 0));
            }
            catch { }
        }


        /// <summary>无动画地将 Content Translation 归零，并重置内部过拉状态。</summary>
        private void ClearOverscrollInstant()
        {
            _overscrollY = 0;
            try
            {
                _contentVisual.Properties.InsertVector3("Translation", Vector3.Zero);
            }
            catch { }
        }


        /// <summary>
        /// 播放回弹动画：将 Content Translation 从当前过拉位置缓动回 Vector3.Zero。
        /// 动画使用 <see cref="CubicBezierEasingFunction"/> 减速曲线，与项目中其他 Composition 动画风格一致。
        /// 动画完成时通知停稳回调。
        /// </summary>
        private void PlaySpringBackAnimation()
        {
            double fromY = _overscrollY;
            _overscrollY = 0;

            try
            {
                Compositor compositor = _contentVisual.Compositor;
                CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0f, 0f), new Vector2(0f, 1f));

                Vector3KeyFrameAnimation anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertKeyFrame(0f, new Vector3(0, (float)fromY, 0));
                anim.InsertKeyFrame(1f, Vector3.Zero, ease);
                anim.Duration = TimeSpan.FromMilliseconds(SpringBackDurationMs);

                CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                _contentVisual.StartAnimation("Translation", anim);
                batch.End();
                batch.Completed += (_, _) =>
                {
                    if (!_disposed)
                    {
                        NotifySettled();
                    }
                };
            }
            catch
            {
                ClearOverscrollInstant();
                if (!_disposed)
                {
                    NotifySettled();
                }
            }
        }


        /// <summary>解除拖拽时设下的 Tooltip 抑制（未设过则不动）。</summary>
        private void RestoreTooltip()
        {
            if (_suppressedRoot is null)
            {
                return;
            }
            InstantTooltip.SetSuppressed(_suppressedRoot, false);
            _suppressedRoot = null;
        }

        #endregion


        #region Settled / item hover (selectable list)

        /// <summary>内容彻底停稳：通知回调方；可点选列表再按最终指针位置校正悬停高亮。</summary>
        private void NotifySettled()
        {
            if (_disposed)
            {
                return;
            }
            _onDragSettled?.Invoke();
            // 停稳回调到达时若已开始新的按下 / 拖拽，交给那一轮处理，免得把列表项的按下态改掉。
            if (_itemsHandleClick && !_isDragging && !_pressPending)
            {
                SyncHoverToPointer();
            }
        }


        /// <summary>
        /// 按指针最近位置命中列表项：命中的一项设为悬停，其余撤回静止态。
        /// 拖拽 / 回弹 / 甩动期间内容在指针下移动，列表项收不到进出事件，这里统一校正。
        /// </summary>
        private void SyncHoverToPointer()
        {
            SelectorItem? hit = null;
            try
            {
                if (_lastWindowPoint is Point point && _scrollViewer.XamlRoot is not null)
                {
                    // 只在视口内命中：滚出视口的项被裁剪但仍有布局位置，指针落在列表外时不能算到它们头上。
                    Rect viewport = _scrollViewer.TransformToVisual(null).TransformBounds(new Rect(0, 0, _scrollViewer.ActualWidth, _scrollViewer.ActualHeight));
                    if (viewport.Contains(point))
                    {
                        foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(point, _scrollViewer))
                        {
                            if (FindItemContainer(element) is SelectorItem item)
                            {
                                hit = item;
                                break;
                            }
                        }
                    }
                }
                foreach (SelectorItem item in EnumerateItemContainers(_content))
                {
                    if (!ReferenceEquals(item, hit))
                    {
                        ApplyRestState(item);
                    }
                }
            }
            catch { }
            _hoverItem = null;
            SetHoverItem(hit);
        }


        /// <summary>把 <paramref name="item"/> 设为代管的悬停项（PointerOver 可视状态），并撤掉上一个代管项的高亮。</summary>
        /// <param name="item">新的悬停项；null 只撤销。</param>
        private void SetHoverItem(SelectorItem? item)
        {
            if (_hoverItem is not null && !ReferenceEquals(_hoverItem, item))
            {
                ApplyRestState(_hoverItem);
            }
            _hoverItem = item;
            if (item is not null)
            {
                // 状态名与系统 ListViewItem 可视状态模板一致；用 ListViewItemPresenter 的默认模板没有这些状态，调用无效果。
                VisualStateManager.GoToState(item, item.IsSelected ? "PointerOverSelected" : "PointerOver", true);
            }
        }


        /// <summary>把列表项的 CommonStates 撤回静止态（选中 / 普通），不动其他状态组。</summary>
        /// <param name="item">列表项容器。</param>
        private static void ApplyRestState(SelectorItem item)
        {
            VisualStateManager.GoToState(item, item.IsSelected ? "Selected" : "Normal", true);
        }


        /// <summary>指针当前是否在 <paramref name="element"/> 的边界内。</summary>
        /// <param name="element">要判断的元素。</param>
        /// <param name="e">提供指针位置的路由事件参数。</param>
        /// <returns>在边界内返回 true；元素已离开视觉树等取不到坐标时返回 false。</returns>
        private static bool IsPointerOver(FrameworkElement element, PointerRoutedEventArgs e)
        {
            try
            {
                if (element.XamlRoot is null)
                {
                    return false;
                }
                Point p = e.GetCurrentPoint(element).Position;
                return p.X >= 0 && p.Y >= 0 && p.X < element.ActualWidth && p.Y < element.ActualHeight;
            }
            catch
            {
                return false;
            }
        }


        /// <summary>枚举 <paramref name="root"/> 下的列表项容器（不进入列表项内部）。</summary>
        /// <param name="root">滚动内容（ItemsPresenter）。</param>
        /// <returns>已生成的 <see cref="SelectorItem"/>。</returns>
        private static IEnumerable<SelectorItem> EnumerateItemContainers(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is SelectorItem item)
                {
                    yield return item;
                }
                else
                {
                    foreach (SelectorItem nested in EnumerateItemContainers(child))
                    {
                        yield return nested;
                    }
                }
            }
        }

        #endregion


        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            StopFling();

            _pressPending = false;
            _pressedItem = null;
            _hoverItem = null;
            _scrollViewer.RemoveHandler(UIElement.PointerPressedEvent, _pressedHandler);
            _scrollViewer.RemoveHandler(UIElement.PointerMovedEvent, _movedHandler);
            _scrollViewer.RemoveHandler(UIElement.PointerReleasedEvent, _releasedHandler);
            _scrollViewer.RemoveHandler(UIElement.PointerCaptureLostEvent, _captureLostHandler);
            _scrollViewer.RemoveHandler(UIElement.PointerCanceledEvent, _canceledHandler);
            _scrollViewer.RemoveHandler(UIElement.PointerExitedEvent, _exitedHandler);
            _scrollViewer.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelHandler);

            // 甩动中被卸载（如甩完立刻切到别的页面）时 _isDragging 已是 false，也要解除：抑制作用于整个窗口，
            // 留着会让窗口里所有即时提示失效，直到下一次拖拽结束。
            RestoreTooltip();
            _isDragging = false;

            // 回弹动画可能还在跑：元素离开视觉树前停掉，并把位移归零。
            try
            {
                _contentVisual.StopAnimation("Translation");
            }
            catch { }
            ClearOverscrollInstant();
        }

        #endregion
    }
}
