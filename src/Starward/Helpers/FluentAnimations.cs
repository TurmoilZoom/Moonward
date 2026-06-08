using CommunityToolkit.WinUI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using System;
using System.Numerics;

namespace Starward.Helpers;

/// <summary>
/// <list type="bullet">
/// <item><see cref="PressScaleProperty"/>：指针按下时整体轻微缩小、松开弹性回弹。</item>
/// <item><see cref="HoverTranslateXProperty"/>：指针悬浮时仅内容横向位移（对 CheckBox/RadioButton 只移动文字，单选框/勾选框本身不动），列表项「字体右移」，离开回弹。</item>
/// <item><see cref="ExpandContractFlyoutProperty"/>：浮层从顶部中心一个「点」放大展开、关闭时收缩回该点。</item>
/// </list>
/// 均走 Composition（缩放 / Translation 外观属性），且只用于按钮、复选框「内容项」等**非 ItemsControl 被 Measure 的容器**，
/// 因此不会触发抽卡卡片那种跨 DPI 的 <c>MeasureOverride 0x80004005</c> 崩溃（参见 GachaStatsCardDragReorder 的说明）。
/// </summary>
public static class FluentAnimations
{

    #region PressScale —— 指针按下缩小、松开回弹

    public static readonly DependencyProperty PressScaleProperty =
        DependencyProperty.RegisterAttached("PressScale", typeof(double), typeof(FluentAnimations),
            new PropertyMetadata(double.NaN, OnPressScaleChanged));

    public static double GetPressScale(DependencyObject obj) => (double)obj.GetValue(PressScaleProperty);

    public static void SetPressScale(DependencyObject obj, double value) => obj.SetValue(PressScaleProperty, value);


    private static void OnPressScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el)
        {
            return;
        }
        el.PointerPressed -= OnPressPointerDown;
        el.PointerReleased -= OnPressPointerUp;
        el.PointerExited -= OnPressPointerUp;
        el.PointerCanceled -= OnPressPointerUp;
        el.PointerCaptureLost -= OnPressPointerUp;
        if (e.NewValue is double s && !double.IsNaN(s))
        {
            el.PointerPressed += OnPressPointerDown;
            el.PointerReleased += OnPressPointerUp;
            el.PointerExited += OnPressPointerUp;
            el.PointerCanceled += OnPressPointerUp;
            el.PointerCaptureLost += OnPressPointerUp;
        }
    }


    private static void OnPressPointerDown(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || !EntranceAnimation.AnimationsEnabled())
        {
            return;
        }
        float scale = (float)GetPressScale(el);
        Visual v = ElementCompositionPreview.GetElementVisual(el);
        v.CenterPoint = new Vector3((float)(el.ActualWidth / 2), (float)(el.ActualHeight / 2), 0);
        Vector3KeyFrameAnimation anim = v.Compositor.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(1f, new Vector3(scale, scale, 1f), v.Compositor.CreateLinearEasingFunction());
        anim.Duration = TimeSpan.FromMilliseconds(100);
        v.StartAnimation(nameof(Visual.Scale), anim);
    }


    private static void OnPressPointerUp(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement el)
        {
            return;
        }
        Visual v = ElementCompositionPreview.GetElementVisual(el);
        try
        {
            SpringVector3NaturalMotionAnimation spring = v.Compositor.CreateSpringVector3Animation();
            spring.FinalValue = Vector3.One;
            spring.DampingRatio = 0.4f;
            spring.Period = TimeSpan.FromMilliseconds(40);
            v.StartAnimation(nameof(Visual.Scale), spring);
        }
        catch
        {
            v.Scale = Vector3.One;
        }
    }

    #endregion


    #region HoverTranslateX —— 指针悬浮时内容横向位移（字体右移），离开回弹

    public static readonly DependencyProperty HoverTranslateXProperty =
        DependencyProperty.RegisterAttached("HoverTranslateX", typeof(double), typeof(FluentAnimations),
            new PropertyMetadata(double.NaN, OnHoverTranslateXChanged));

    public static double GetHoverTranslateX(DependencyObject obj) => (double)obj.GetValue(HoverTranslateXProperty);

    public static void SetHoverTranslateX(DependencyObject obj, double value) => obj.SetValue(HoverTranslateXProperty, value);


    private static void OnHoverTranslateXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el)
        {
            return;
        }
        el.PointerEntered -= OnHoverEnter;
        el.PointerExited -= OnHoverExit;
        el.PointerCanceled -= OnHoverExit;
        el.PointerCaptureLost -= OnHoverExit;
        if (e.NewValue is double s && !double.IsNaN(s))
        {
            el.PointerEntered += OnHoverEnter;
            el.PointerExited += OnHoverExit;
            el.PointerCanceled += OnHoverExit;
            el.PointerCaptureLost += OnHoverExit;
        }
    }


    /// <summary>
    /// 解析实际要应用 Translation 动画的目标元素。
    /// 对于 CheckBox / RadioButton，只平移其内部的 ContentPresenter（文字部分），
    /// 使「单选框/勾选框」本身在悬停时不发生位移。
    /// 其他元素则直接作用于宿主本身。
    /// </summary>
    private static UIElement GetHoverTranslationTarget(UIElement host)
    {
        if (host is CheckBox or RadioButton)
        {
            if (host.FindDescendant("ContentPresenter") is UIElement contentPresenter)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(contentPresenter, true);
                return contentPresenter;
            }
        }
        ElementCompositionPreview.SetIsTranslationEnabled(host, true);
        return host;
    }


    private static void OnHoverEnter(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement host || !EntranceAnimation.AnimationsEnabled())
        {
            return;
        }
        float offset = (float)GetHoverTranslateX(host);
        UIElement target = GetHoverTranslationTarget(host);
        Visual v = ElementCompositionPreview.GetElementVisual(target);
        Vector3KeyFrameAnimation anim = v.Compositor.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(1f, new Vector3(offset, 0, 0), v.Compositor.CreateLinearEasingFunction());
        anim.Duration = TimeSpan.FromMilliseconds(150);
        v.StartAnimation("Translation", anim);
    }


    private static void OnHoverExit(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement host)
        {
            return;
        }
        UIElement target = GetHoverTranslationTarget(host);
        Visual v = ElementCompositionPreview.GetElementVisual(target);
        try
        {
            SpringVector3NaturalMotionAnimation spring = v.Compositor.CreateSpringVector3Animation();
            spring.FinalValue = Vector3.Zero;
            spring.DampingRatio = 0.3f;
            spring.Period = TimeSpan.FromMilliseconds(40);
            v.StartAnimation("Translation", spring);
        }
        catch
        {
            v.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }

    #endregion


    #region ExpandContractFlyout —— 浮层从顶部中心一点展开 / 收缩关闭（1:1 移植 Character-Map 的 UseExpandContractAnimation）

    /// <summary>
    /// 挂在 <see cref="FlyoutBase"/>（如 <see cref="Flyout"/>）上：关闭内置开合动画，改用 Composition
    /// 让浮层从顶部中心的一个「点」放大展开（300ms 缓出），关闭时先收缩回该点（150ms 缓入）再真正隐藏，
    /// </summary>
    public static readonly DependencyProperty ExpandContractFlyoutProperty =
        DependencyProperty.RegisterAttached("ExpandContractFlyout", typeof(bool), typeof(FluentAnimations),
            new PropertyMetadata(false, OnExpandContractFlyoutChanged));

    public static bool GetExpandContractFlyout(DependencyObject obj) => (bool)obj.GetValue(ExpandContractFlyoutProperty);

    public static void SetExpandContractFlyout(DependencyObject obj, bool value) => obj.SetValue(ExpandContractFlyoutProperty, value);


    // 起点/终点缩放：缩到约 20px、且不超过原尺寸的 1%，等效于「从一个点展开 / 收缩到一个点」（this.Target 为 FlyoutPresenter 的 Visual）。
    private const string FlyoutSeedScaleExpression =
        "Vector3(Min(0.01, 20.0 / this.Target.Size.X), Min(0.01, 20.0 / this.Target.Size.Y), 1.0)";

    // 缩放原点固定在顶部中心，使浮层看起来从触发按钮处向下生长（对应 Character-Map ThemeFlyoutStyle 的 RenderTransformOrigin="0.5 0"）。
    private const string FlyoutTopCenterExpression = "Vector3(this.Target.Size.X * 0.5, 0, 0)";

    // 记在 FlyoutPresenter.Tag 上的动画状态机，用于「先播收缩动画、再真正关闭」。
    private const string FlyoutStateOpening = "ExpandContract.Opening";
    private const string FlyoutStateClosing = "ExpandContract.Closing";
    private const string FlyoutStateClosed = "ExpandContract.Closed";


    private static void OnExpandContractFlyoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlyoutBase flyout)
        {
            return;
        }
        flyout.Opened -= ExpandContractFlyout_Opened;
        flyout.Closing -= ExpandContractFlyout_Closing;
        if (e.NewValue is true)
        {
            flyout.Opened += ExpandContractFlyout_Opened;
            flyout.Closing += ExpandContractFlyout_Closing;
            // 关掉内置开合动画，完全交给下面的 Composition 动画接管。
            flyout.AreOpenCloseAnimationsEnabled = false;
        }
    }


    /// <summary>从 Flyout 内容沿可视树上溯，找到承载它的 <see cref="FlyoutPresenter"/>。</summary>
    private static FlyoutPresenter? FindFlyoutPresenter(FlyoutBase flyout)
    {
        if ((flyout as Flyout)?.Content is not FrameworkElement content)
        {
            return null;
        }
        DependencyObject? node = content;
        while (node is not null and not FlyoutPresenter)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        return node as FlyoutPresenter;
    }


    private static void ExpandContractFlyout_Opened(object? sender, object e)
    {
        if (sender is not FlyoutBase flyout || FindFlyoutPresenter(flyout) is not FlyoutPresenter presenter)
        {
            return;
        }
        Visual v = ElementCompositionPreview.GetElementVisual(presenter);
        presenter.Tag = FlyoutStateOpening;

        if (!EntranceAnimation.AnimationsEnabled())
        {
            v.Scale = Vector3.One;
            v.Opacity = 1;
            return;
        }

        // 1. 缩放原点固定顶部中心，用表达式实时跟随浮层尺寸。
        ExpressionAnimation center = v.Compositor.CreateExpressionAnimation(FlyoutTopCenterExpression);
        v.StopAnimation(nameof(Visual.CenterPoint));
        v.StartAnimation(nameof(Visual.CenterPoint), center);

        // 2. 还原透明度（收缩动画结束时会把它降到 0，复用同一 Visual 重开时需先恢复）。
        v.Opacity = 1;

        // 3. 从一个点放大到原始大小：与 Character-Map 同样的缓出曲线 (0.1,0.9)(0.2,1)、300ms。
        CubicBezierEasingFunction entrance = v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        Vector3KeyFrameAnimation scale = v.Compositor.CreateVector3KeyFrameAnimation();
        scale.InsertExpressionKeyFrame(0f, FlyoutSeedScaleExpression);
        scale.InsertKeyFrame(1f, Vector3.One, entrance);
        scale.Duration = TimeSpan.FromMilliseconds(300);
        v.StartAnimation(nameof(Visual.Scale), scale);
    }


    private static void ExpandContractFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
    {
        if (FindFlyoutPresenter(sender) is not FlyoutPresenter presenter)
        {
            return;
        }
        // 收缩动画已播完 → 放行，真正关闭。
        if (presenter.Tag as string == FlyoutStateClosed)
        {
            return;
        }
        // 系统/用户关闭了动画时直接放行（不取消、不动画）。
        if (!EntranceAnimation.AnimationsEnabled())
        {
            return;
        }
        // 取消默认关闭，改播收缩动画。
        args.Cancel = true;
        // 已经在收缩中 → 等当前动画结束即可，避免重复触发。
        if (presenter.Tag as string == FlyoutStateClosing)
        {
            return;
        }
        presenter.Tag = FlyoutStateClosing;

        Visual v = ElementCompositionPreview.GetElementVisual(presenter);
        CompositionScopedBatch batch = v.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        // 收缩回顶部中心的那个点：缓入曲线 (0.7,0)(1,0.5)、150ms。
        CubicBezierEasingFunction exit = v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));
        Vector3KeyFrameAnimation scale = v.Compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, Vector3.One);
        scale.InsertExpressionKeyFrame(1f, FlyoutSeedScaleExpression, exit);
        scale.Duration = TimeSpan.FromMilliseconds(150);
        v.StartAnimation(nameof(Visual.Scale), scale);

        // 透明度全程保持可见，仅在最后一帧瞬间归零（StepEasing），收缩过程不淡出。
        StepEasingFunction step = v.Compositor.CreateStepEasingFunction();
        step.IsFinalStepSingleFrame = true;
        ScalarKeyFrameAnimation opacity = v.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, 0f, step);
        opacity.Duration = TimeSpan.FromMilliseconds(130);
        v.StartAnimation(nameof(Visual.Opacity), opacity);

        batch.Completed += (_, _) =>
        {
            if (presenter.Tag as string == FlyoutStateClosing)
            {
                presenter.Tag = FlyoutStateClosed;
                sender.Hide();
            }
        };
        batch.End();
    }

    #endregion

}
