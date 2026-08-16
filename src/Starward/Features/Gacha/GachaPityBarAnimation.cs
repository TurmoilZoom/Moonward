using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using System;
using System.Numerics;


namespace Starward.Features.Gacha;

/// <summary>
/// （柱条自基线「生长」而出，缓动用 <c>ExponentialOut</c>、时长 800ms，参见 LiveChartsCore 主题默认值）。
/// 柱条本身是一条左对齐的渐变 <see cref="Border"/>（彩色区间从 offset 0 起向右延伸到 pity/保底）。
/// 因此只需把整条 Border 的 Composition 缩放以**左边缘为支点**从 <c>Scale.X=0</c> 放大到 1，
/// 缩放走 Composition（渲染变换），不参与布局，故不影响行高 / 不引发滚动跳动。
/// <para>
/// 入场由附加属性 <see cref="AnimateOnLoadProperty"/> 在元素 <see cref="FrameworkElement.Loaded"/> 时触发。
/// 动画提交推迟到当前布局之后，避免在 Measure 过程中触碰 Composition 造成布局重入。
/// </para>
/// <para>类型必须为 public：WinUI 运行时按 XamlTypeInfo 赋值附加属性，internal 类会报 0x802B000A（Failed to assign to property）。</para>
/// </summary>
public static class GachaPityBarAnimation
{
    /// <summary>柱条生长时长（毫秒）。对应 LiveCharts 主题默认 <c>AnimationsSpeed</c> = 800ms。</summary>
    private const int DurationMs = 800;

    /// <summary>相邻行之间的错峰间隔（毫秒），自上而下形成轻微的级联涟漪。</summary>
    private const int StaggerMs = 28;

    /// <summary>错峰延迟上限（毫秒），避免长列表整体拖得过久。</summary>
    private const int MaxDelayMs = 400;

    /// <summary>对 ExponentialOut 曲线采样的关键帧数量（越多越平滑）。</summary>
    private const int SampleCount = 24;


    /// <summary>
    /// 为 true 时，柱条进入视觉树后播放一次自左生长动画。
    /// 用于非虚拟化列表（<see cref="ItemsControl"/>）：每项只 Loaded 一次，无需 ItemsRepeater 的 ElementPrepared。
    /// </summary>
    public static readonly DependencyProperty AnimateOnLoadProperty =
        DependencyProperty.RegisterAttached(
            "AnimateOnLoad",
            typeof(bool),
            typeof(GachaPityBarAnimation),
            new PropertyMetadata(false, OnAnimateOnLoadChanged));


    /// <summary>取得 <see cref="AnimateOnLoadProperty"/>。</summary>
    public static bool GetAnimateOnLoad(DependencyObject element)
    {
        return (bool)element.GetValue(AnimateOnLoadProperty);
    }


    /// <summary>设置 <see cref="AnimateOnLoadProperty"/>。</summary>
    public static void SetAnimateOnLoad(DependencyObject element, bool value)
    {
        element.SetValue(AnimateOnLoadProperty, value);
    }


    private static void OnAnimateOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement bar)
        {
            return;
        }
        bar.Loaded -= OnBarLoaded;
        bar.Unloaded -= OnBarUnloaded;
        if (e.NewValue is true)
        {
            bar.Loaded += OnBarLoaded;
            bar.Unloaded += OnBarUnloaded;
        }
    }


    private static void OnBarLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement bar)
        {
            return;
        }
        int index = GetItemIndex(bar);
        // 推迟到本次布局/绘制之后再开动画，避免在 ItemsControl 生成子项的 Measure 栈上启动 Composition。
        bar.DispatcherQueue.TryEnqueue(() =>
        {
            if (bar.XamlRoot is null)
            {
                return;
            }
            Play(bar, index);
        });
    }


    private static void OnBarUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement bar)
        {
            ResetBar(bar);
        }
    }


    /// <summary>
    /// 沿视觉树向上找到 <see cref="ItemsControl"/> 的 <see cref="StackPanel"/> 面板，用其中的序号做错峰。
    /// </summary>
    private static int GetItemIndex(FrameworkElement bar)
    {
        DependencyObject current = bar;
        while (VisualTreeHelper.GetParent(current) is DependencyObject parent)
        {
            if (parent is StackPanel panel)
            {
                int index = panel.Children.IndexOf((UIElement)current);
                return index < 0 ? 0 : index;
            }
            current = parent;
        }
        return 0;
    }


    /// <summary>停止柱条动画并复位缩放。</summary>
    private static void ResetBar(FrameworkElement bar)
    {
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

}
