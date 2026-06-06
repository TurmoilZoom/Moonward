using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Starward.Controls;
using System;
using System.Numerics;


namespace Starward.Features.Gacha;

/// <summary>
/// 将 <see cref="Segmented"/> 与两个互斥的列表绑定：选中项切换时，旧列表向一侧滑出并淡出、
/// 新列表自另一侧滑入并淡入，形成左右方向的「衔接」过渡
/// 切换方向跟随选中索引：索引增大时新内容自右进入、旧内容向左离场；索引减小则相反。
/// </summary>
internal static class GachaStatsSegmentedListHelper
{
    /// <summary>水平滑入/滑出的位移量（像素）。</summary>
    private const float SlideOffsetX = 80f;

    /// <summary>位移动画时长（毫秒）。</summary>
    private const int SlideDurationMs = 350;

    /// <summary>淡入/淡出时长（毫秒），略短于位移以贴合 Fluent「共享横轴」观感。</summary>
    private const int FadeDurationMs = 200;


    public static void Bind(Segmented segmented, ItemsRepeater firstList, ItemsRepeater secondList)
    {
        // 记录上一次选中索引，用于推断滑动方向。
        int previousIndex = Math.Max(0, segmented.SelectedIndex);
        // 代际计数：仅最后一次切换负责收尾（折叠旧列表），避免快速来回切换时误折叠。
        int generation = 0;

        void Apply(bool animate)
        {
            int index = Math.Max(0, segmented.SelectedIndex);
            bool showFirst = index == 0;
            ItemsRepeater incoming = showFirst ? firstList : secondList;
            ItemsRepeater outgoing = showFirst ? secondList : firstList;

            if (!animate || !EntranceAnimation.AnimationsEnabled())
            {
                ShowInstant(incoming, outgoing);
                previousIndex = index;
                return;
            }

            float direction = index >= previousIndex ? 1f : -1f;
            previousIndex = index;
            int myGeneration = ++generation;
            AnimateSwap(incoming, outgoing, direction, () => generation == myGeneration);
        }

        segmented.RegisterPropertyChangedCallback(Selector.SelectedIndexProperty, (_, _) => Apply(true));
        Apply(false);
    }


    /// <summary>无动画地直接切换显示，并复位两个列表的视觉状态。</summary>
    private static void ShowInstant(UIElement incoming, UIElement outgoing)
    {
        incoming.Visibility = Visibility.Visible;
        outgoing.Visibility = Visibility.Collapsed;
        ResetVisual(incoming);
        ResetVisual(outgoing);
    }


    /// <summary>
    /// 旧列表向 <c>-direction</c> 滑出并淡出，新列表自 <c>+direction</c> 滑入并淡入。
    /// 旧列表从当前实际位置（可能是上一次切换的中途）继续过渡，因此快速来回切换不会突跳。
    /// 动画结束后，若本次切换仍是最新一次（<paramref name="isCurrent"/>），则折叠旧列表并复位。
    /// </summary>
    private static void AnimateSwap(UIElement incoming, UIElement outgoing, float direction, Func<bool> isCurrent)
    {
        Visual inVisual = ElementCompositionPreview.GetElementVisual(incoming);
        Visual outVisual = ElementCompositionPreview.GetElementVisual(outgoing);
        Compositor compositor = inVisual.Compositor;
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(incoming, true);
            ElementCompositionPreview.SetIsTranslationEnabled(outgoing, true);

            // Fluent 减速曲线，与 EntranceAnimation 保持一致的运动语言。
            CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
            TimeSpan slide = TimeSpan.FromMilliseconds(SlideDurationMs);
            TimeSpan fade = TimeSpan.FromMilliseconds(FadeDurationMs);

            // 预置新列表初始态：偏移到一侧且透明，随后滑入淡入（避免提交前的首帧闪烁）。
            incoming.Visibility = Visibility.Visible;
            inVisual.Properties.InsertVector3("Translation", new Vector3(direction * SlideOffsetX, 0, 0));
            inVisual.Opacity = 0;

            Vector3KeyFrameAnimation inSlide = compositor.CreateVector3KeyFrameAnimation();
            inSlide.InsertKeyFrame(1f, Vector3.Zero, ease);
            inSlide.Duration = slide;

            ScalarKeyFrameAnimation inFade = compositor.CreateScalarKeyFrameAnimation();
            inFade.InsertKeyFrame(1f, 1f, ease);
            inFade.Duration = fade;

            // 旧列表不预置基线，从当前值滑出/淡出，保证连续切换平滑。
            Vector3KeyFrameAnimation outSlide = compositor.CreateVector3KeyFrameAnimation();
            outSlide.InsertKeyFrame(1f, new Vector3(-direction * SlideOffsetX, 0, 0), ease);
            outSlide.Duration = slide;

            ScalarKeyFrameAnimation outFade = compositor.CreateScalarKeyFrameAnimation();
            outFade.InsertKeyFrame(1f, 0f, ease);
            outFade.Duration = fade;

            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            inVisual.StartAnimation("Translation", inSlide);
            inVisual.StartAnimation(nameof(Visual.Opacity), inFade);
            outVisual.StartAnimation("Translation", outSlide);
            outVisual.StartAnimation(nameof(Visual.Opacity), outFade);
            batch.End();

            batch.Completed += (_, _) =>
            {
                if (!isCurrent())
                {
                    return;
                }
                outgoing.Visibility = Visibility.Collapsed;
                ResetVisual(outgoing);
                ResetVisual(incoming);
            };
        }
        catch
        {
            ShowInstant(incoming, outgoing);
        }
    }


    /// <summary>复位元素的不透明度与位移到默认值（完全可见、无偏移）。</summary>
    private static void ResetVisual(UIElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = 1;
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
        catch { }
    }

}
