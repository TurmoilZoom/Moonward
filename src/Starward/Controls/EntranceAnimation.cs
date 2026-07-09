using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Starward.Controls;

/// <summary>
/// <see cref="Play(Panel)"/> 对面板的直接子元素逐个播放「上滑 + 淡入」（设置页内容区使用）；
/// <see cref="PlayFromRight"/> 对面板的直接子元素逐个播放「从右滑入 + 淡入」（月报页右侧内容区使用）；
/// <see cref="PlayItem"/> 对单个元素播放「从右滑入 + 淡入」并按索引错峰（ItemsControl 卡片逐个加载时使用）。
/// </summary>
public static class EntranceAnimation
{
    /// <summary>首个子项的起始延迟（毫秒）。</summary>
    private const int DefaultDelayMs = 10;

    /// <summary>由下方上滑的初始位移量（像素）。</summary>
    private const float DefaultFromOffsetY = 80f;

    /// <summary>由右侧滑入的初始位移量（像素）。</summary>
    private const float DefaultFromOffsetX = 80f;

    /// <summary>位移动画时长（毫秒）。</summary>
    private const int DefaultDurationMs = 1000;

    /// <summary>相邻子项之间的错峰间隔（毫秒）。</summary>
    private const int DefaultStaggerMs = 83;

    /// <summary>淡入时长占位移时长的比例。</summary>
    private const double FadeFraction = 0.33;

    private static UISettings? _uiSettings;


    /// <summary>
    /// 系统「显示动画」开关。关闭时不做隐藏/位移，直接保持内容可见（无障碍 / 减少动态效果）。
    /// </summary>
    public static bool AnimationsEnabled()
    {
        try
        {
            _uiSettings ??= new UISettings();
            return _uiSettings.AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }


    /// <summary>
    /// 对页面内容根面板播放级联入场动画。会自动定位 <c>ScrollViewer &gt; Panel</c> 或直接的 <c>Panel</c> 内容根。
    /// </summary>
    public static void Play(Page page)
    {
        if (page is null)
        {
            return;
        }
        Panel? panel = page.Content switch
        {
            ScrollViewer { Content: Panel p } => p,
            Panel p => p,
            _ => null,
        };
        if (panel is not null)
        {
            Play(panel);
        }
    }


    /// <summary>
    /// 对面板的直接子元素逐个播放「上滑 + 淡入」的错峰级联动画。
    /// </summary>
    public static void Play(Panel panel,
                            int delayMs = DefaultDelayMs,
                            float fromOffsetY = DefaultFromOffsetY,
                            int durationMs = DefaultDurationMs,
                            int staggerMs = DefaultStaggerMs)
    {
        if (panel is null || panel.Children.Count == 0)
        {
            return;
        }

        // 关闭系统动画时直接返回，保持内容默认可见。
        if (!AnimationsEnabled())
        {
            return;
        }

        Compositor compositor = ElementCompositionPreview.GetElementVisual(panel).Compositor;
        // Fluent 减速曲线
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
        int fadeDurationMs = Math.Max(1, (int)(durationMs * FadeFraction));
        int start = delayMs;

        foreach (UIElement child in panel.Children)
        {
            if (child is null)
            {
                continue;
            }
            Animate(compositor, child, new Vector3(0, fromOffsetY, 0), start, durationMs, fadeDurationMs, ease);
            start += staggerMs;
        }
    }


    /// <summary>
    /// 对面板的直接子元素逐个播放「从右滑入 + 淡入」的错峰级联动画。
    /// 米游社工具箱各功能页（月报 / 深渊 / 忘却之庭等）右侧内容区在列表选中、数据就绪后调用；
    /// 跳过 <see cref="Visibility.Collapsed"/> 子项，避免折叠元素卡在透明状态。
    /// </summary>
    /// <param name="panel">内容根面板（通常为右侧 ScrollViewer 内的 StackPanel）。</param>
    /// <param name="delayMs">首个子项的起始延迟（毫秒）。</param>
    /// <param name="fromOffsetX">由右侧滑入的初始位移量（像素）。</param>
    /// <param name="durationMs">位移动画时长（毫秒）。</param>
    /// <param name="staggerMs">相邻子项之间的错峰间隔（毫秒）。</param>
    public static void PlayFromRight(Panel panel,
                                     int delayMs = DefaultDelayMs,
                                     float fromOffsetX = DefaultFromOffsetX,
                                     int durationMs = DefaultDurationMs,
                                     int staggerMs = DefaultStaggerMs)
    {
        if (panel is null || panel.Children.Count == 0)
        {
            return;
        }

        // 关闭系统动画时直接返回，保持内容默认可见。
        if (!AnimationsEnabled())
        {
            return;
        }

        Compositor compositor = ElementCompositionPreview.GetElementVisual(panel).Compositor;
        // Fluent 减速曲线
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
        int fadeDurationMs = Math.Max(1, (int)(durationMs * FadeFraction));
        int start = delayMs;

        foreach (UIElement child in panel.Children)
        {
            if (child is null || child.Visibility == Visibility.Collapsed)
            {
                continue;
            }
            Animate(compositor, child, new Vector3(fromOffsetX, 0, 0), start, durationMs, fadeDurationMs, ease);
            start += staggerMs;
        }
    }


    /// <summary>
    /// 对单个元素播放「从右滑入 + 淡入」入场动画，按 <paramref name="index"/> 错峰延迟。
    /// 适用于 ItemsControl 中逐个加载的卡片：每张卡在自身 Loaded 时调用，索引决定其出场顺序（自左向右依次入场）。
    /// </summary>
    public static void PlayItem(UIElement element,
                                int index,
                                float fromOffsetX = DefaultFromOffsetX,
                                int durationMs = DefaultDurationMs,
                                int staggerMs = DefaultStaggerMs)
    {
        if (element is null)
        {
            return;
        }

        // 关闭系统动画时直接返回，保持内容默认可见。
        if (!AnimationsEnabled())
        {
            return;
        }

        Compositor compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        // Fluent 减速曲线
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
        int fadeDurationMs = Math.Max(1, (int)(durationMs * FadeFraction));
        int delayMs = DefaultDelayMs + Math.Max(0, index) * staggerMs;
        Animate(compositor, element, new Vector3(fromOffsetX, 0, 0), delayMs, durationMs, fadeDurationMs, ease);
    }


    /// <summary>
    /// 对单个元素播放「位移 + 淡入」：从「静置 Translation + fromOffset」滑回静置位，同时淡入。
    /// 预置初始状态并配合 <see cref="AnimationDelayBehavior.SetInitialValueBeforeDelay"/>，使元素在轮到自己之前保持隐藏（无首帧闪烁）。
    /// 位移走 Composition Translation（布局不管 Translation，ScrollViewer 尺寸不会被顶开）；终点取
    /// <see cref="UIElement.Translation"/> 静置值，保留 XAML 上为 <c>ThemeShadow</c> 写的 <c>Translation="0,0,16"</c>，
    /// 避免把 Z 清成 0 造成阴影塌陷/抖动。切勿用 <see cref="Visual.Offset"/>：布局会持续写 Offset，与动画互抢。
    /// </summary>
    /// <param name="compositor">合成器。</param>
    /// <param name="element">要播放入场动画的元素。</param>
    /// <param name="fromOffset">相对静置位的起始位移增量（如上滑为 (0,80,0)，右滑为 (80,0,0)）。</param>
    /// <param name="delayMs">动画开始前的延迟（毫秒）。</param>
    /// <param name="durationMs">位移动画时长（毫秒）。</param>
    /// <param name="fadeDurationMs">淡入时长（毫秒）。</param>
    /// <param name="ease">缓动函数。</param>
    private static void Animate(Compositor compositor,
                               UIElement element,
                               Vector3 fromOffset,
                               int delayMs,
                               int durationMs,
                               int fadeDurationMs,
                               CubicBezierEasingFunction ease)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            // 打断可能未完成的上一次入场，避免切月时叠动画造成抖动。
            visual.StopAnimation("Translation");
            visual.StopAnimation(nameof(Visual.Opacity));

            // 静置位：含 ThemeShadow 的 Z=16；入场只叠加 fromOffset，结束必须回到 rest 而非 Zero。
            Vector3 rest = element.Translation;
            Vector3 from = rest + fromOffset;

            // 预置初始状态，避免动画提交前的首帧闪烁（与 SetInitialValueBeforeDelay 配合）。
            visual.Properties.InsertVector3("Translation", from);
            visual.Opacity = 0;

            TimeSpan delay = TimeSpan.FromMilliseconds(delayMs);

            Vector3KeyFrameAnimation translate = compositor.CreateVector3KeyFrameAnimation();
            translate.InsertKeyFrame(0f, from);
            translate.InsertKeyFrame(1f, rest, ease);
            translate.Duration = TimeSpan.FromMilliseconds(durationMs);
            translate.DelayTime = delay;
            translate.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, ease);
            fade.Duration = TimeSpan.FromMilliseconds(fadeDurationMs);
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            visual.StartAnimation("Translation", translate);
            visual.StartAnimation(nameof(Visual.Opacity), fade);
        }
        catch
        {
            // 任意子项动画失败都不应让内容隐身：复位为可见与静置位移。
            visual.Opacity = 1;
            visual.Properties.InsertVector3("Translation", element.Translation);
        }
    }
}
