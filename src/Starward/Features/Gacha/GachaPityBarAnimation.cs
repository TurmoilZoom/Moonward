using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.Foundation;


namespace Starward.Features.Gacha;

/// <summary>
/// （柱条自基线「生长」而出，缓动用 <c>ExponentialOut</c>、时长 800ms，参见 LiveChartsCore 主题默认值）。
/// 柱条本身是一条左对齐的渐变 <see cref="Border"/>（彩色区间从 offset 0 起向右延伸到 pity/保底）。
/// 因此只需把整条 Border 的 Composition 缩放以**左边缘为支点**从 <c>Scale.X=0</c> 放大到 1，
/// 缩放走 Composition（渲染变换），不参与布局，故不影响行高 / 不引发滚动跳动。
/// </summary>
internal static class GachaPityBarAnimation
{
    /// <summary>柱条生长时长（毫秒）。对应 LiveCharts 主题默认 <c>AnimationsSpeed</c> = 800ms。</summary>
    private const int DurationMs = 800;

    /// <summary>相邻行之间的错峰间隔（毫秒），自上而下形成轻微的级联涟漪。</summary>
    private const int StaggerMs = 28;

    /// <summary>错峰延迟上限（毫秒），避免长列表整体拖得过久。</summary>
    private const int MaxDelayMs = 400;

    /// <summary>对 ExponentialOut 曲线采样的关键帧数量（越多越平滑）。</summary>
    private const int SampleCount = 24;

    /// <summary>柱条 <see cref="Border"/> 在数据模板中的 x:Name。</summary>
    private const string DefaultBarName = "PityBar";

    private static readonly ConditionalWeakTable<FrameworkElement, PendingElement> PendingElements = new();


    /// <summary>
    /// 绑定一个 <see cref="ItemsRepeater"/>：其每个表项被实现（<see cref="ItemsRepeater.ElementPrepared"/>）时，
    /// 找到名为 <paramref name="barName"/> 的柱条并播放「自左生长」入场动画，按表项索引错峰。
    /// 返回的句柄须在控件 <c>Unloaded</c> 时调用 <see cref="GachaPityBarBinding.Dispose"/> 解除绑定。
    /// </summary>
    public static GachaPityBarBinding Bind(ItemsRepeater repeater, string barName = DefaultBarName)
    {
        return new GachaPityBarBinding(repeater, barName);
    }


    private static void OnElementPrepared(ItemsRepeaterElementPreparedEventArgs e, string barName)
    {
        if (e.Element is not FrameworkElement root)
        {
            return;
        }
        int index = e.Index;
        // 模板树通常此刻已实现，直接定位柱条；个别情况下延迟到 Loaded 再找一次，避免漏播。
        if (FindByName(root, barName) is FrameworkElement bar)
        {
            Play(bar, index);
            return;
        }
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            CleanupPending(root);
            if (FindByName(root, barName) is FrameworkElement b)
            {
                Play(b, index);
            }
        }
        PendingElements.Add(root, new PendingElement(OnLoaded, barName));
        root.Loaded += OnLoaded;
    }


    private static void OnElementClearing(ItemsRepeaterElementClearingEventArgs e, string barName)
    {
        if (e.Element is not FrameworkElement root)
        {
            return;
        }
        CleanupPending(root);
        ResetBar(root, barName);
    }


    private static void CleanupPending(FrameworkElement root)
    {
        if (PendingElements.TryGetValue(root, out PendingElement? pending))
        {
            root.Loaded -= pending.LoadedHandler;
            PendingElements.Remove(root);
        }
    }


    /// <summary>停止柱条动画并复位缩放，供元素回收或控件卸载时调用。</summary>
    private static void ResetBar(FrameworkElement root, string barName)
    {
        if (FindByName(root, barName) is not FrameworkElement bar)
        {
            return;
        }
        Visual visual = ElementCompositionPreview.GetElementVisual(bar);
        try
        {
            visual.StopAnimation(nameof(Visual.Scale));
        }
        catch { }
        visual.Scale = Vector3.One;
    }


    /// <summary>
    /// 让柱条以左边缘为支点，水平缩放从 0 生长到 1，缓动复刻 LiveCharts 的 <c>ExponentialOut</c>。
    /// </summary>
    public static void Play(FrameworkElement bar, int index)
    {
        if (bar is null)
        {
            return;
        }
        Visual visual = ElementCompositionPreview.GetElementVisual(bar);
        try
        {
            // 关闭系统动画时保持柱条完整可见（无障碍 / 减少动态效果）；同时复位被回收元素可能残留的缩放。
            if (!EntranceAnimation.AnimationsEnabled())
            {
                visual.Scale = Vector3.One;
                return;
            }

            Compositor compositor = visual.Compositor;
            // 支点设在左上角（X=0），水平缩放即以左边缘为锚点向右生长。
            visual.CenterPoint = Vector3.Zero;
            // 预置 0 宽度，避免动画提交前首帧闪现完整柱条。
            visual.Scale = new Vector3(0f, 1f, 1f);

            // 用线性插值连接 ExponentialOut 采样点，得到与 LiveCharts 一致的缓动曲线。
            LinearEasingFunction linear = compositor.CreateLinearEasingFunction();
            Vector3KeyFrameAnimation grow = compositor.CreateVector3KeyFrameAnimation();
            for (int i = 1; i <= SampleCount; i++)
            {
                float t = i / (float)SampleCount;
                float scaleX = EaseOutExpo(t);
                grow.InsertKeyFrame(t, new Vector3(scaleX, 1f, 1f), linear);
            }
            grow.Duration = TimeSpan.FromMilliseconds(DurationMs);

            int delay = Math.Min(MaxDelayMs, Math.Max(0, index) * StaggerMs);
            if (delay > 0)
            {
                grow.DelayTime = TimeSpan.FromMilliseconds(delay);
                grow.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            }

            visual.StartAnimation(nameof(Visual.Scale), grow);
        }
        catch
        {
            // 动画失败不应让柱条隐身：复位为完整可见。
            visual.Scale = Vector3.One;
        }
    }


    /// <summary>
    /// LiveCharts <c>ExponentialEasingFunction.Out</c> 的等价实现（端点已归一化：Out(0)=0、Out(1)=1）。
    /// </summary>
    private static float EaseOutExpo(float t)
    {
        return (float)(1.0 - (Math.Pow(2, -10 * t) - 0.0009765625) * 1.0009775171065494);
    }


    /// <summary>在可视化树中按名称递归查找子元素。</summary>
    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
            {
                return fe;
            }
            if (FindByName(child, name) is FrameworkElement found)
            {
                return found;
            }
        }
        return null;
    }


    private sealed class PendingElement(RoutedEventHandler loadedHandler, string barName)
    {
        public RoutedEventHandler LoadedHandler { get; } = loadedHandler;

        public string BarName { get; } = barName;
    }


    internal sealed class GachaPityBarBinding : IDisposable
    {
        private readonly ItemsRepeater _repeater;
        private readonly string _barName;
        private readonly TypedEventHandler<ItemsRepeater, ItemsRepeaterElementPreparedEventArgs> _preparedHandler;
        private readonly TypedEventHandler<ItemsRepeater, ItemsRepeaterElementClearingEventArgs> _clearingHandler;
        private bool _disposed;

        internal GachaPityBarBinding(ItemsRepeater repeater, string barName)
        {
            _repeater = repeater;
            _barName = barName;
            _preparedHandler = (_, e) => OnElementPrepared(e, barName);
            _clearingHandler = (_, e) => OnElementClearing(e, barName);
            _repeater.ElementPrepared += _preparedHandler;
            _repeater.ElementClearing += _clearingHandler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _repeater.ElementPrepared -= _preparedHandler;
            _repeater.ElementClearing -= _clearingHandler;
            _repeater.ItemsSource = null;
        }
    }

}