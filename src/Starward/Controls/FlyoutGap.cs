using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.Foundation;

namespace Starward.Controls;

/// <summary>
/// 为首页右侧工具栏等 <see cref="Flyout"/> 校正弹出位置：
/// <list type="bullet">
/// <item>水平：与目标图标留出间距（WinUI 定位不吃 Presenter.Margin，用 <see cref="UIElement.Translation"/>）</item>
/// <item>垂直：保证相对窗口上下边缘有边距，避免贴顶 / 贴底；内容变高时会重算</item>
/// </list>
/// </summary>
/// <remarks>
/// XAML：<c>sc:FlyoutGap.Horizontal="12"</c>。启用后同时做垂直边距钳制（默认上下各 24 DIP）。
/// </remarks>
public static class FlyoutGap
{
    /// <summary>相对窗口上下边的最小留白（DIP）。</summary>
    private const double VerticalEdgeMargin = 24;

    /// <summary>已订阅 SizeChanged 的 Presenter → 所属 Flyout，便于内容异步增高后重算。</summary>
    private static readonly ConditionalWeakTable<FlyoutPresenter, Flyout> PresenterOwners = new();


    /// <summary>
    /// 水平间距（DIP）。左侧弹出时向左偏移、右侧弹出时向右偏移该值；非 0 时同时启用垂直边距校正。
    /// </summary>
    public static readonly DependencyProperty HorizontalProperty =
        DependencyProperty.RegisterAttached(
            "Horizontal",
            typeof(double),
            typeof(FlyoutGap),
            new PropertyMetadata(0d, OnHorizontalChanged));


    public static double GetHorizontal(Flyout flyout)
    {
        return (double)flyout.GetValue(HorizontalProperty);
    }


    public static void SetHorizontal(Flyout flyout, double value)
    {
        flyout.SetValue(HorizontalProperty, value);
    }


    private static void OnHorizontalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Flyout flyout)
        {
            return;
        }

        flyout.Opened -= Flyout_Opened;
        flyout.Closed -= Flyout_Closed;
        if (e.NewValue is double gap && gap != 0)
        {
            flyout.Opened += Flyout_Opened;
            flyout.Closed += Flyout_Closed;
        }
    }


    private static void Flyout_Opened(object sender, object e)
    {
        if (sender is not Flyout flyout || FindPresenter(flyout) is not FlyoutPresenter presenter)
        {
            return;
        }

        PresenterOwners.AddOrUpdate(presenter, flyout);
        presenter.SizeChanged -= Presenter_SizeChanged;
        presenter.SizeChanged += Presenter_SizeChanged;
        ApplyOffset(flyout, presenter);

        // 首帧布局未完成时 ActualHeight 可能为 0，下一帧再算一次
        presenter.DispatcherQueue.TryEnqueue(() =>
        {
            if (flyout.IsOpen)
            {
                ApplyOffset(flyout, presenter);
            }
        });
    }


    private static void Flyout_Closed(object? sender, object e)
    {
        if (sender is not Flyout flyout || FindPresenter(flyout) is not FlyoutPresenter presenter)
        {
            return;
        }

        presenter.SizeChanged -= Presenter_SizeChanged;
        PresenterOwners.Remove(presenter);
        // 复位，避免下次打开残留上次的 Y 偏移
        presenter.Translation = new Vector3(0, 0, presenter.Translation.Z);
    }


    private static void Presenter_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FlyoutPresenter presenter)
        {
            return;
        }
        if (!PresenterOwners.TryGetValue(presenter, out Flyout? flyout) || !flyout.IsOpen)
        {
            return;
        }
        // 仅高度变化时重算（异步加载列表会增高）
        if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 0.5)
        {
            return;
        }
        ApplyOffset(flyout, presenter);
    }


    /// <summary>
    /// 应用水平间距 + 垂直边距钳制。
    /// </summary>
    private static void ApplyOffset(Flyout flyout, FlyoutPresenter presenter)
    {
        double gap = GetHorizontal(flyout);
        // 左弹出向左让出间距；右弹出向右让出，与工具栏左右贴边对称。
        float offsetX = IsLeftPlacement(flyout.Placement)
            ? (float)-gap
            : IsRightPlacement(flyout.Placement) ? (float)gap : 0f;

        // 先只设水平偏移，再以此时的屏幕位置计算垂直修正
        presenter.Translation = new Vector3(offsetX, 0, presenter.Translation.Z);

        double height = presenter.ActualHeight;
        if (height <= 0 || presenter.XamlRoot is null)
        {
            return;
        }

        double rootH = presenter.XamlRoot.Size.Height;
        if (rootH <= 0)
        {
            return;
        }

        // 相对 XamlRoot 内容坐标（Flyout 与主界面同根）
        UIElement? rootVisual = presenter.XamlRoot.Content as UIElement;
        Point origin = rootVisual is null
            ? presenter.TransformToVisual(null).TransformPoint(new Point(0, 0))
            : presenter.TransformToVisual(rootVisual).TransformPoint(new Point(0, 0));

        double top = origin.Y;
        double bottom = top + height;
        double minTop = VerticalEdgeMargin;
        double maxBottom = rootH - VerticalEdgeMargin;
        double available = maxBottom - minTop;

        double dy;
        if (available <= 0)
        {
            // 窗口极矮：尽量居中
            dy = (rootH - height) / 2 - top;
        }
        else if (height >= available)
        {
            // 卡片高于可用区：在可用区内居中（上下边距尽量均分）
            dy = minTop + (available - height) / 2 - top;
        }
        else if (top < minTop)
        {
            // 贴顶 → 下移
            dy = minTop - top;
        }
        else if (bottom > maxBottom)
        {
            // 贴底 → 上移
            dy = maxBottom - bottom;
        }
        else
        {
            dy = 0;
        }

        if (Math.Abs(dy) < 0.5)
        {
            return;
        }

        presenter.Translation = new Vector3(offsetX, (float)dy, presenter.Translation.Z);
    }


    private static bool IsLeftPlacement(FlyoutPlacementMode placement)
    {
        return placement is FlyoutPlacementMode.Left
            or FlyoutPlacementMode.LeftEdgeAlignedTop
            or FlyoutPlacementMode.LeftEdgeAlignedBottom;
    }


    private static bool IsRightPlacement(FlyoutPlacementMode placement)
    {
        return placement is FlyoutPlacementMode.Right
            or FlyoutPlacementMode.RightEdgeAlignedTop
            or FlyoutPlacementMode.RightEdgeAlignedBottom;
    }


    private static FlyoutPresenter? FindPresenter(Flyout flyout)
    {
        if (flyout.Content is not FrameworkElement content)
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

}
