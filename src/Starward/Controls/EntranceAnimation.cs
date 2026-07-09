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
/// <see cref="PlayFromRight(Panel)"/> 对面板的直接子元素逐个播放「从右滑入 + 淡入」（米游社工具箱右侧内容区使用）；
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
    /// 对页面内容根面板播放「从右滑入 + 淡入」级联入场动画。
    /// 会自动定位 <c>ScrollViewer &gt; Panel</c> 或直接的 <c>Panel</c> 内容根。
    /// </summary>
    /// <param name="page">目标页面；为 null 或内容非 Panel 时直接返回。</param>
    public static void PlayFromRight(Page page)
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
            PlayFromRight(panel);
        }
    }


    /// <summary>
    /// 对面板的直接子元素逐个播放「从右滑入 + 淡入」的错峰级联动画。
    /// 与 <see cref="Play(Panel)"/> 对称，仅将位移方向由 Y 改为 X。
    /// </summary>
    /// <param name="panel">内容根面板；为 null 或无子元素时直接返回。</param>
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
            if (child is null)
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
    /// 对单个元素播放「位移 + 淡入」：从 <paramref name="fromOffset"/> 滑到原位，同时淡入。
    /// 预置初始状态并配合 <see cref="AnimationDelayBehavior.SetInitialValueBeforeDelay"/>，使元素在轮到自己之前保持隐藏（无首帧闪烁）。
    /// 位移走 Composition Translation，不影响布局，因此 ScrollViewer 的内容尺寸保持正确（不会跳动）。
    /// </summary>
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

            // 预置初始状态，避免动画提交前的首帧闪烁（与 SetInitialValueBeforeDelay 配合）。
            visual.Properties.InsertVector3("Translation", fromOffset);
            visual.Opacity = 0;

            TimeSpan delay = TimeSpan.FromMilliseconds(delayMs);

            Vector3KeyFrameAnimation translate = compositor.CreateVector3KeyFrameAnimation();
            translate.InsertKeyFrame(0f, fromOffset);
            translate.InsertKeyFrame(1f, Vector3.Zero, ease);
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
            // 任意子项动画失败都不应让内容隐身：复位为可见。
            visual.Opacity = 1;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }
}
