using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;


namespace Starward.Features.Gacha;

/// <summary>
/// 让抽卡统计卡片内的记录列表支持鼠标左键拖拽滚动：跟手拖动、松手惯性甩动、拖到边界时内容可被「拉出」一段距离并松手回弹。
/// <para>通过 <see cref="Bind"/> 为指定 <see cref="ScrollViewer"/> 注入该行为，返回绑定句柄（需在控件 Unloaded 时 Dispose）。</para>
/// <para>仅接管 <see cref="PointerDeviceType.Mouse"/> 的左键拖拽；触屏/笔的原生 manipulation 完全保留。</para>
/// </summary>
internal static class GachaStatsListDragScrollHelper
{
    /// <summary>回弹位移的渐近上限（像素）。实际拉出量由此与拖拽距离做橡皮筋阻尼：<c>offset*c/(offset+c)</c>。</summary>
    private const double MaxOverscrollPull = 72d;

    /// <summary>松手回弹动画时长（毫秒）。</summary>
    private const int SpringBackDurationMs = 300;

    /// <summary>甩动目标的外推时间常数（秒）。<c>targetOffset = currentOffset - vel * tau</c>，值越大惯性距离越长。</summary>
    private const double FlingTimeConstant = 0.3;

    /// <summary>甩动触发的最低速度阈值（像素/秒）。低于此值视为停住，不触发惯性滚动。</summary>
    private const double MinFlingVelocity = 150d;


    /// <summary>
    /// 为指定的 <see cref="ScrollViewer"/> 注入鼠标拖拽滚动 + 回弹行为。
    /// </summary>
    /// <param name="scrollViewer">包含记录列表的竖直滚动容器。</param>
    /// <returns>绑定句柄，须在控件 Unloaded 时调用 <see cref="GachaStatsListDragScrollBinding.Dispose"/> 解除绑定。</returns>
    public static GachaStatsListDragScrollBinding Bind(ScrollViewer scrollViewer)
    {
        return new GachaStatsListDragScrollBinding(scrollViewer);
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
        private bool _disposed;

        // 拖拽状态
        private bool _isDragging;
        private Point _lastPosition;
        private long _lastTimestamp;   // Stopwatch.GetTimestamp()
        private double _velocity;      // px/s，EMA 平滑后的速度

        // 回弹位移（Content 的 Composition Translation.Y）：正 = 下拉（顶部拉出），负 = 上推（底部拉出）。
        private double _overscrollY;

        // 权威逻辑偏移：不每帧读 VerticalOffset（ChangeView 异步生效，会读到过期值导致抖动），
        // 而是自己维持一个与用户手指严格一致的虚拟偏移，再分摊给 ChangeView（clamped 部分）与 Composition Translation（overflow 部分）。
        private double _virtualOffset;

        // ----- 常量 -----
        private static readonly double TickToSeconds = 1.0 / Stopwatch.Frequency;


        public GachaStatsListDragScrollBinding(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _content = scrollViewer.Content as FrameworkElement
                ?? throw new InvalidOperationException("ScrollViewer.Content must be a FrameworkElement.");
            _contentVisual = ElementCompositionPreview.GetElementVisual(_content);
            ElementCompositionPreview.SetIsTranslationEnabled(_content, true);

            _scrollViewer.PointerPressed += OnPointerPressed;
            _scrollViewer.PointerMoved += OnPointerMoved;
            _scrollViewer.PointerReleased += OnPointerReleased;
            _scrollViewer.PointerCaptureLost += OnPointerCaptureLost;
        }


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

            double seed = _scrollViewer.VerticalOffset;
            try
            {
                // 鼠标按下时，立即停止 ScrollViewer 的原生缓动（若存在），并记录当前 offset 作为虚拟偏移的起点。
                _scrollViewer.ChangeView(null, seed, null, disableAnimation: true);
            }
            catch { }

            _virtualOffset = seed;
            //scrollViewer独占鼠标指针，避免拖拽过程中鼠标离开 ScrollViewer 导致 PointerCaptureLost。
            _scrollViewer.CapturePointer(e.Pointer);
            _lastPosition = e.GetCurrentPoint(_scrollViewer).Position;
            _lastTimestamp = Stopwatch.GetTimestamp();
            _velocity = 0;
            _isDragging = true;
        }


        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }
            var point = e.GetCurrentPoint(_scrollViewer);
            if (!point.Properties.IsLeftButtonPressed)
            {
                // 左键意外松开则终止拖拽
                FinishDrag(restoreOverscroll: true);
                return;
            }

            Point pos = point.Position;
            // 单位：像素
            //鼠标相对上一采样点，指针在竖直方向上移动了多少（屏幕坐标：Y 向下为正）
            double deltaY = pos.Y - _lastPosition.Y;
            long now = Stopwatch.GetTimestamp();
            //GetTimestamp是硬件/高精度计数器的 tick，所以必须除以 Frequency（每秒多少 tick）才得到秒
            double dt = (now - _lastTimestamp) * TickToSeconds;
            if (dt > 0)
            {
                double instant = deltaY / dt;
                // EMA 平滑速度，计算公式：v = v * (1 - α) + v_instant * α，α 越大响应越快但抖动越明显，α 越小响应越慢但平滑。
                _velocity = _velocity * 0.7 + instant * 0.3;
            }

            double maxOffset = _scrollViewer.ScrollableHeight;

            //开始计算累计值
            // 不相信 ScrollViewer 自己返回的 VerticalOffset，因为它更新是异步的，读回来经常是旧值，会导致画面抖动
            // 鼠标移动的方向和_scrollViewer相反
            // 可以为负数，表示本次采样点相对于上次采样点的变化过程
            //_virtualOffset为累计采样，deltaY为瞬时变化
            _virtualOffset -= deltaY;
            //_virtualOffset为相对偏移，相对于ScrollViewer的起点0的总偏移
            double clamped = Math.Clamp(_virtualOffset, 0, maxOffset);
            double overflow = _virtualOffset - clamped; // >0 底部过拉，<0 顶部过拉。溢出的像素点个数

            // 计算上次采样点的 clamped 值
            double prevScrollOffset = _virtualOffset + deltaY; 
            double prevClamped = Math.Clamp(prevScrollOffset, 0, maxOffset);

            //鼠标光点击但不动时，无需响应
            if (Math.Abs(clamped - prevClamped) > 0.01)
            {
                try
                {
                    //前提：ScrollViewer.ChangeView() 不允许传负数或超过最大值
                    //每次采样都让 ScrollViewer 立即跳到 clamped 位置
                    _scrollViewer.ChangeView(null, clamped, null, disableAnimation: true);
                }
                catch { }
            }

            // 过拉的情况下，计算阻尼之后的真实位移
            _overscrollY = -Math.Sign(overflow) * Rubber(Math.Abs(overflow), MaxOverscrollPull);
            ApplyOverscrollInstant();

            _lastPosition = pos;
            _lastTimestamp = now;
        }


        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }
            TryFlingOrRestore();
        }


        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }
            // 捕获丢失视为中断：必须恢复回弹位移。
            FinishDrag(restoreOverscroll: true);
        }

        #endregion


        #region Drag End — Fling / Overscroll Restore

        /// <summary>
        /// 结束拖拽：优先回弹（若存在过拉位移），否则尝试惯性甩动。
        /// </summary>
        private void TryFlingOrRestore()
        {
            _isDragging = false;
            try
            {
                //释放指针捕获
                _scrollViewer.ReleasePointerCapture(null);
            }
            catch { }

            // 存在回弹位移，忽略速度，播放回弹动画
            if (_overscrollY != 0)
            {
                PlaySpringBackAnimation();
                return;
            }

            // 无回弹，尝试惯性
            double absVel = Math.Abs(_velocity);
            if (absVel > MinFlingVelocity)//阈值：150 px/s
            {
                double current = _scrollViewer.VerticalOffset;
                double target = current - _velocity * FlingTimeConstant;
                double max = _scrollViewer.ScrollableHeight;
                target = Math.Clamp(target, 0, max);
                // 逐个采样点控制
                try
                {
                    _scrollViewer.ChangeView(null, target, null, disableAnimation: false);
                }
                catch { }
            }
        }


        /// <summary>强制结束拖拽，确保回弹位移被还原。</summary>
        private void FinishDrag(bool restoreOverscroll)
        {
            _isDragging = false;
            try
            {
                _scrollViewer.ReleasePointerCapture(null);
            }
            catch { }

            if (restoreOverscroll && _overscrollY != 0)
            {
                PlaySpringBackAnimation();
            }
        }

        #endregion


        #region Overscroll Visuals

        /// <summary>直接（无动画）将 <see cref="_overscrollY"/> 写到 Content 的 Composition Translation.Y。</summary>
        private void ApplyOverscrollInstant()
        {
            try
            {
                //底层视觉对象 composition api
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

                _contentVisual.StartAnimation("Translation", anim);
            }
            catch
            {
                // 回退：直接赋值
                ClearOverscrollInstant();
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

            _scrollViewer.PointerPressed -= OnPointerPressed;
            _scrollViewer.PointerMoved -= OnPointerMoved;
            _scrollViewer.PointerReleased -= OnPointerReleased;
            _scrollViewer.PointerCaptureLost -= OnPointerCaptureLost;

            // 若有残留过拉位移，直接清零
            if (_overscrollY != 0)
            {
                ClearOverscrollInstant();
            }
        }

        #endregion
    }
}
