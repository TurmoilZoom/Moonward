using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using System;
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
            _scrollViewer.PointerCanceled += OnPointerCaptureLost;
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

            // 新按下打断进行中的甩动，并以当前真实偏移重新锚定。
            StopFling();
            double seed = _scrollViewer.VerticalOffset;
            try
            {
                _scrollViewer.ChangeView(null, seed, null, disableAnimation: true);
            }
            catch { }

            _virtualOffset = seed;
            _lastAppliedClamped = seed;
            _capturedPointer = e.Pointer;
            _scrollViewer.CapturePointer(e.Pointer);
            _lastPosition = e.GetCurrentPoint(_scrollViewer).Position;
            _lastTimestamp = Stopwatch.GetTimestamp();
            _velocity = 0;
            _isDragging = true;
            // 拖拽滚动时隐藏记录项上的时间气泡，并避免列表移动时反复弹出。
            InstantTooltip.SetSuppressed(_scrollViewer.XamlRoot, true);
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
                // 部分设备会在 PointerReleased 之前先送来「左键已抬起」的 Moved，这里按松手处理，否则会跳过甩动。
                TryFlingOrRestore();
                return;
            }

            Point pos = point.Position;
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
            // 捕获丢失与正常松手走同一条路径：有速度就甩，没有则回弹/停住。
            TryFlingOrRestore();
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

            RestoreTooltip();
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
                    PlaySpringBackAnimation();
                }
                RestoreTooltip();
                return;
            }

            if (Math.Abs(_velocity) < FlingStopVelocity)
            {
                StopFling();
                RestoreTooltip();
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
                ClearOverscrollInstant();
            }
        }


        private void RestoreTooltip()
        {
            InstantTooltip.SetSuppressed(_scrollViewer.XamlRoot, false);
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

            _scrollViewer.PointerPressed -= OnPointerPressed;
            _scrollViewer.PointerMoved -= OnPointerMoved;
            _scrollViewer.PointerReleased -= OnPointerReleased;
            _scrollViewer.PointerCaptureLost -= OnPointerCaptureLost;
            _scrollViewer.PointerCanceled -= OnPointerCaptureLost;

            if (_isDragging)
            {
                InstantTooltip.SetSuppressed(_scrollViewer.XamlRoot, false);
                _isDragging = false;
            }

            if (_overscrollY != 0)
            {
                ClearOverscrollInstant();
            }
        }

        #endregion
    }
}
