using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Controls;

/// <summary>
/// 为 <see cref="NavigationView"/> 提供可复用的流体交互动画效果（Composition 实现，非 Storyboard）。
/// <para>
/// 三种效果：
/// <list type="bullet">
/// <item>流体悬停高亮条：单一 <see cref="ShapeVisual"/> 圆角矩形挂在 overlay 宿主上，
/// 用弹簧动画改 <c>Offset</c> 在项之间滑动（而非每项各自闪背景）。</item>
/// <item>悬停文字右移：仅 <c>ContentPresenter</c> 的 <c>Translation.X</c> 做 Spring，不影响图标。</item>
/// <item>按下物理反馈：整项（LayoutRoot/Presenter）缩放 + Z 轴下沉，抬起 Spring 回弹。</item>
/// </list>
/// </para>
/// <para>
/// 架构要点：高亮条不画在 <see cref="NavigationViewItem"/> 上，而是画在独立 overlay
/// （如 <c>NavIndicatorHost</c>）的 Composition 子视觉树里，这样始终只有一个高亮对象可被移动。
/// 宿主须 <c>IsHitTestVisible="False"</c>，否则会抢走指针事件，下面的导航项收不到 Entered/Exited。
/// </para>
/// <para>
/// 用法：
/// <list type="number">
/// <item>NavigationView Resources 将内置 hover/pressed 背景置透明（效果接管）：
/// <c>NavigationViewItemBackgroundPointerOver</c> / <c>NavigationViewItemBackgroundPressed</c>。</item>
/// <item>在 NavigationView 同级放置 overlay Grid 作为宿主，Width 与 OpenPaneLength 一致，
/// <c>IsHitTestVisible="False"</c> 让事件穿透。</item>
/// <item>在 Loaded 中调用 <see cref="Attach"/>；在 Unloaded 中调用 <see cref="Detach"/>。</item>
/// </list>
/// </para>
/// </summary>
public sealed class FluidNavigationViewHoverEffect
{
    /// <summary>
    /// 悬停时文字向右平移量（像素），轻微即可。
    /// </summary>
    private const float TextHoverOffsetX = 3.2f;

    /// <summary>
    /// 按下时整体列表项的缩放系数（&lt;1 产生“压扁按下”感），与 Z 轴纵深按压配合。
    /// </summary>
    private const float ItemPressScale = 0.978f;

    /// <summary>
    /// 按下时沿 Z 轴（垂直于屏幕向内）的按压深度；负值表示向屏幕内部按入。
    /// </summary>
    private const float ItemPressDepthZ = -5.0f;

    /// <summary>
    /// 与宿主共享的 Composition 合成器；Detach 后为 null。
    /// </summary>
    private Compositor? _compositor;

    /// <summary>
    /// 当前附着的导航视图；用于收集菜单项、主题与 DispatcherQueue。
    /// </summary>
    private NavigationView? _navView;

    /// <summary>
    /// 高亮条 Composition 宿主（XAML Grid overlay）。高亮画在其 ElementChildVisual 上，而非导航项内部。
    /// </summary>
    private Grid? _host;

    /// <summary>
    /// 用于在 PointerExited 后延后判断是否真正离开导航区（防相邻项切换闪烁）。
    /// </summary>
    private DispatcherQueue? _dispatcherQueue;

    private ILogger? _logger;

    /// <summary>
    /// 当前承载高亮条与文字右移效果的导航项；为 null 表示鼠标不在任何已接线项上。
    /// </summary>
    private NavigationViewItem? _hoveredItem;

    /// <summary>
    /// 当前由鼠标左键按下的导航项。NavigationView 捕获指针后，仍用它恢复按压反馈。
    /// </summary>
    private NavigationViewItem? _pressedMouseItem;

    /// <summary>
    /// 当前鼠标左键交互的指针标识；null 表示无需在 PointerMoved 中补做命中检测。
    /// </summary>
    private uint? _pressedMousePointerId;

    /// <summary>
    /// 高亮条是否已完成首次定位。false 时直接设 Offset，避免从 (0,0) 滑入；true 后走弹簧动画。
    /// </summary>
    private bool _hoverPositioned;

    /// <summary>
    /// 共享的流体高亮条（圆角矩形 ShapeVisual）；始终只有一个实例在项之间移动。
    /// </summary>
    private ShapeVisual? _hoverVisual;

    /// <summary>
    /// 高亮条几何；Size 与当前悬停项的 LayoutRoot 对齐。
    /// </summary>
    private CompositionRoundedRectangleGeometry? _hoverGeometry;

    /// <summary>
    /// 高亮条填充色；主题切换时更新，不重建 visual。
    /// </summary>
    private CompositionColorBrush? _hoverBrush;

    /// <summary>
    /// 缓存每个导航项的文字 ContentPresenter，用于悬停时文字向右平移。
    /// </summary>
    private readonly Dictionary<NavigationViewItem, UIElement> _contentPresenters = new();

    /// <summary>
    /// 缓存每个导航项的按压反馈根容器（LayoutRoot 或 NavigationViewItemPresenter），
    /// 使缩放和 Z 轴按压作用于图标+文字整体。
    /// </summary>
    private readonly Dictionary<NavigationViewItem, UIElement> _pressRoots = new();

    /// <summary>
    /// 已附加事件处理程序的 NavigationViewItem，避免重复订阅（MenuItems + FooterMenuItems + PaneFooter）。
    /// </summary>
    private readonly HashSet<NavigationViewItem> _wiredItems = new();

    /// <summary>
    /// 是否已 Attach；重复 Attach 会先 Detach 再挂接。
    /// </summary>
    private bool _attached;


    /// <summary>
    /// 将流体效果附着到指定 NavigationView 与高亮宿主。可重复调用（会先清理旧附着）。
    /// </summary>
    /// <param name="navView">目标导航视图；其 MenuItems / FooterMenuItems / PaneFooter 内的项会被接线。</param>
    /// <param name="host">高亮条 overlay 宿主。应覆盖侧栏区域，且 IsHitTestVisible=False，
    /// 否则会拦截指针导致项收不到 Entered/Exited。</param>
    /// <param name="logger">可选日志；Composition 异常时写入，可为 null。</param>
    /// <exception cref="ArgumentNullException"><paramref name="navView"/> 或 <paramref name="host"/> 为 null。</exception>
    public void Attach(NavigationView navView, Grid host, ILogger? logger = null)
    {
        //防重复附着
        if (_attached)
        {
            Detach();
        }

        _navView = navView ?? throw new ArgumentNullException(nameof(navView));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger;
        _dispatcherQueue = navView.DispatcherQueue;

        // 从宿主取 Compositor：高亮条将作为 host 的 ElementChildVisual，与宿主同一合成上下文
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

        WireEvents();

        if (_navView is FrameworkElement fe)
        {
            fe.ActualThemeChanged += OnActualThemeChanged;
        }

        _attached = true;
    }


    /// <summary>
    /// 解除附着：注销事件、卸下 Composition 子视觉、清空缓存与状态。页面 Unloaded 时必须调用以免泄漏。
    /// </summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        UnwireEvents();

        if (_navView is FrameworkElement fe)
        {
            fe.ActualThemeChanged -= OnActualThemeChanged;
        }

        // 卸下 ElementChildVisual；宿主卸载后 Set 可能抛，吞掉即可
        if (_host is not null)
        {
            try
            {
                ElementCompositionPreview.SetElementChildVisual(_host, null);
            }
            catch { }
        }

        _hoverVisual = null;
        _hoverGeometry = null;
        _hoverBrush = null;
        _compositor = null;
        _hoveredItem = null;
        _pressedMouseItem = null;
        _pressedMousePointerId = null;
        _hoverPositioned = false;
        _contentPresenters.Clear();
        _pressRoots.Clear();
        _wiredItems.Clear();

        _navView = null;
        _host = null;
        _logger = null;
        _dispatcherQueue = null;
        _attached = false;
    }


    /// <summary>
    /// 在 Attach 时，把导航栏里“会出现指针交互”的每一项找出来，交给 WireHandlers 挂上事件。
    /// </summary>
    private void WireEvents()
    {
        if (_navView is null)
        {
            return;
        }

        // NavigationViewItem 按下后会捕获指针，兄弟项无法稳定收到 Entered；
        // 因此在父级以 handledEventsToo 监听 Moved，按实际坐标重新命中测试。
        _navView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(NavigationView_PointerMoved), true);

        // 设置页主要用 MenuItems；主页/工具箱等可能用 FooterMenuItems
        foreach (NavigationViewItem item in _navView.MenuItems.OfType<NavigationViewItem>())
        {
            WireHandlers(item);
        }
        foreach (NavigationViewItem item in _navView.FooterMenuItems.OfType<NavigationViewItem>())
        {
            WireHandlers(item);
        }

        // PaneFooter 不在 MenuItems/FooterMenuItems 集合里，需从视觉树找
        if (_navView.FindDescendant("PaneFooter") is FrameworkElement footer)
        {
            if (footer.FindDescendant<NavigationViewItem>() is NavigationViewItem fi)
            {
                WireHandlers(fi);
            }
        }
    }

    /// <summary>
    /// 为单个导航项附加指针事件；已在 <see cref="_wiredItems"/> 中的项跳过，避免重复订阅。
    /// </summary>
    /// <param name="item">要接线的导航项；不可为 null。</param>
    private void WireHandlers(NavigationViewItem item)
    {
        if (!_wiredItems.Add(item))
        {
            return;
        }
        item.PointerEntered += NavigationViewItem_PointerEntered;
        item.PointerExited += NavigationViewItem_PointerExited;
        // Pressed/Released 会被 NavigationViewItem 内部标为 Handled，普通 += 收不到；
        // handledEventsToo: true 才能驱动按压缩放/Z 轴动画
        item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(NavigationViewItem_PointerPressed), true);
        item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(NavigationViewItem_PointerReleased), true);
        item.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(NavigationViewItem_PointerReleased), true);
        item.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(NavigationViewItem_PointerReleased), true);
    }


    /// <summary>
    /// 注销所有已跟踪导航项上的事件处理程序。
    /// </summary>
    private void UnwireEvents()
    {
        if (_navView is null)
        {
            return;
        }

        _navView.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(NavigationView_PointerMoved));

        // 只按 _wiredItems 反注册（与 Attach 时收集的来源配对），不重新遍历 MenuItems
        foreach (NavigationViewItem item in _wiredItems.ToList())
        {
            UnwireHandlers(item);
        }
        _wiredItems.Clear();
    }

    /// <summary>
    /// 移除单个导航项上由本类注册的全部指针处理程序。
    /// </summary>
    /// <param name="item">要解除接线的导航项。</param>
    private void UnwireHandlers(NavigationViewItem item)
    {
        _wiredItems.Remove(item);
        item.PointerEntered -= NavigationViewItem_PointerEntered;
        item.PointerExited -= NavigationViewItem_PointerExited;
        item.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(NavigationViewItem_PointerPressed));
        item.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(NavigationViewItem_PointerReleased));
        item.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(NavigationViewItem_PointerReleased));
        item.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(NavigationViewItem_PointerReleased));
    }


    /// <summary>
    /// 指针进入导航项：移动高亮条并启动文字右移。
    /// </summary>
    /// <param name="sender">触发事件的 <see cref="NavigationViewItem"/>。</param>
    /// <param name="e">指针路由事件参数（当前未使用）。</param>
    private void NavigationViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is NavigationViewItem item)
        {
            SetHoveredItem(item);
        }
    }


    /// <summary>
    /// 指针离开导航项：恢复文字/按压，并延后判断是否离开整个导航区后再淡出高亮条。
    /// </summary>
    /// <param name="sender">触发事件的 <see cref="NavigationViewItem"/>。</param>
    /// <param name="e">指针路由事件参数（当前未使用）。</param>
    private void NavigationViewItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not NavigationViewItem item)
        {
            return;
        }

        // 按压回弹
        AnimateContentPress(item, false);
        AnimateContentHoverShift(item, false);

        // 相邻项切换顺序：Exited(旧) → Entered(新)。若此处立刻清空会闪一下。
        // 拖动期间由 PointerMoved 更新 _hoveredItem；延迟回调只清除仍停留在旧项的状态。
        _dispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_hoveredItem == item)
            {
                SetHoveredItem(null);
            }
        });
    }


    /// <summary>
    /// 指针在导航项上按下：触发整项缩放 + Z 轴下沉。
    /// </summary>
    /// <param name="sender">触发事件的 <see cref="NavigationViewItem"/>。</param>
    /// <param name="e">包含鼠标按键状态与指针标识的路由事件参数。</param>
    private void NavigationViewItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is NavigationViewItem item)
        {
            AnimateContentPress(item, true);

            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                && e.GetCurrentPoint(item).Properties.IsLeftButtonPressed)
            {
                _pressedMouseItem = item;
                _pressedMousePointerId = e.Pointer.PointerId;
            }
        }
    }


    /// <summary>
    /// 指针抬起/取消/丢失捕获：按压态弹簧回弹。Canceled 与 CaptureLost 与 Released 同路径，避免卡在按下态。
    /// </summary>
    /// <param name="sender">触发事件的 <see cref="NavigationViewItem"/>。</param>
    /// <param name="e">包含鼠标当前位置和指针标识的路由事件参数。</param>
    private void NavigationViewItem_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is NavigationViewItem item)
        {
            AnimateContentPress(item, false);

            if (_pressedMousePointerId == e.Pointer.PointerId)
            {
                _pressedMouseItem = null;
                _pressedMousePointerId = null;
                SetHoveredItem(FindNavigationViewItemAt(e.GetCurrentPoint(null).Position));
            }
        }
    }


    /// <summary>
    /// 在 NavigationView 捕获鼠标指针期间，根据鼠标实际位置更新高亮条和文字悬停效果。
    /// </summary>
    /// <param name="sender">触发路由事件的导航视图。</param>
    /// <param name="e">包含鼠标位置和按键状态的指针事件参数。</param>
    private void NavigationView_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedMousePointerId != e.Pointer.PointerId
            || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            return;
        }

        var pointerPoint = e.GetCurrentPoint(null);
        Point point = pointerPoint.Position;
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            // 某些中断路径可能只留下 Moved；主动回弹，避免按压视觉卡住。
            if (_pressedMouseItem is NavigationViewItem pressedItem)
            {
                AnimateContentPress(pressedItem, false);
            }
            _pressedMouseItem = null;
            _pressedMousePointerId = null;
        }

        SetHoveredItem(FindNavigationViewItemAt(point));
    }


    /// <summary>
    /// 将视觉悬停状态切换到指定导航项；传入 null 时恢复文字并淡出高亮条。
    /// </summary>
    /// <param name="item">鼠标当前命中的已接线导航项；没有命中时为 null。</param>
    private void SetHoveredItem(NavigationViewItem? item)
    {
        if (_hoveredItem == item)
        {
            return;
        }

        if (_hoveredItem is NavigationViewItem previousItem)
        {
            AnimateContentHoverShift(previousItem, false);
        }

        _hoveredItem = item;
        if (item is null)
        {
            HideHoverIndicator();
            return;
        }

        MoveHoverIndicatorTo(item);
        AnimateContentHoverShift(item, true);
    }


    /// <summary>
    /// 在窗口坐标中命中测试当前鼠标下的已接线导航项，忽略折叠、禁用或不可命中的项。
    /// </summary>
    /// <param name="point">相对于应用窗口左上角的鼠标坐标。</param>
    /// <returns>鼠标下的导航项；不在任一可交互导航项上时为 null。</returns>
    private NavigationViewItem? FindNavigationViewItemAt(Point point)
    {
        if (_navView is null)
        {
            return null;
        }

        foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(point, _navView, true))
        {
            for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is NavigationViewItem item
                    && _wiredItems.Contains(item)
                    && item.Visibility == Visibility.Visible
                    && item.IsEnabled
                    && item.IsHitTestVisible)
                {
                    return item;
                }
            }
        }

        return null;
    }


    /// <summary>
    /// 将共享高亮条对齐到目标导航项的高亮区域（模板中的 LayoutRoot），
    /// 已定位过则用弹簧动画移动 <c>Offset</c>，否则直接定位并显示。
    /// </summary>
    /// <param name="item">当前悬停的导航项；用其 LayoutRoot 的边界作为高亮框。</param>
    private void MoveHoverIndicatorTo(NavigationViewItem item)
    {
        try
        {
            EnsureHoverIndicator();
            if (_host is null)
            {
                return;
            }
            // 量尺寸
            FrameworkElement target = (item.FindDescendant("LayoutRoot") as FrameworkElement)
                                      ?? (item.FindDescendant<NavigationViewItemPresenter>() as FrameworkElement)
                                      ?? item;
            // 获取高亮条左上角坐标
            Point point = target.TransformToVisual(_host).TransformPoint(default);
            var size = new Vector2((float)target.ActualWidth, (float)target.ActualHeight);
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }
            // visual的大小
            _hoverVisual!.Size = size;
            //左上角对齐
            _hoverGeometry!.Size = size;
            var offset = new Vector3((float)point.X, (float)point.Y, 0);
            if (_hoverPositioned)
            {
                // 同一 ShapeVisual 改 Offset：看起来像高亮在列表上滑动，而非两项各自闪背景
                SpringVector3NaturalMotionAnimation spring = _compositor!.CreateSpringVector3Animation();
                spring.FinalValue = offset;
                spring.DampingRatio = 0.7f; // 略阻尼，少回弹
                spring.Period = TimeSpan.FromMilliseconds(45);
                _hoverVisual.StartAnimation(nameof(Visual.Offset), spring);
            }
            else
            {
                // 首次出现直接定位，避免从 host 左上角 (0,0) 滑入造成错觉
                _hoverVisual.Offset = offset;
                _hoverPositioned = true;
            }
            ShowHoverIndicator();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Move nav hover indicator.");
        }
    }


    /// <summary>
    /// 创建高亮条样式
    /// 1.圆角默认4f
    /// </summary>
    private void EnsureHoverIndicator()
    {
        if (_hoverVisual is not null || _compositor is null || _host is null)
        {
            return;
        }
        //  默认：4 像素圆角
        float radius = 4f;
        if (Application.Current.Resources.TryGetValue("ControlCornerRadius", out object? value) && value is CornerRadius cornerRadius)
        {
            //约定用左上角的弧度
            radius = (float)cornerRadius.TopLeft;
        }
        _hoverGeometry = _compositor.CreateRoundedRectangleGeometry();
        _hoverGeometry.CornerRadius = new Vector2(radius);
        _hoverBrush = _compositor.CreateColorBrush(GetHoverColor());
        CompositionSpriteShape shape = _compositor.CreateSpriteShape(_hoverGeometry);
        shape.FillBrush = _hoverBrush;
        _hoverVisual = _compositor.CreateShapeVisual();
        _hoverVisual.Shapes.Add(shape);
        _hoverVisual.Opacity = 0; // 创建后保持透明，由 ShowHoverIndicator 淡入
        // 挂到 overlay 的 Composition 子视觉树，不进入 XAML Children，故不参与布局/命中（宿主本身也 IsHitTestVisible=False）
        ElementCompositionPreview.SetElementChildVisual(_host, _hoverVisual);
    }


    /// <summary>
    /// 按当前实际主题返回高亮填充色：暗色半透明白、亮色半透明黑。
    /// </summary>
    /// <returns>用于 <see cref="_hoverBrush"/> 的 ARGB 颜色。</returns>
    private Color GetHoverColor()
    {
        var theme = _navView?.ActualTheme ?? ElementTheme.Default;
        if (theme == ElementTheme.Default)
        {
            // Default 表示跟随系统，再回退到宿主 ActualTheme
            theme = _host?.ActualTheme ?? ElementTheme.Light;
        }
        return theme == ElementTheme.Dark
            ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x16, 0x00, 0x00, 0x00);
    }


    /// <summary>
    /// 导航视图主题变更时同步高亮条颜色，避免明暗切换后对比度错误。
    /// </summary>
    /// <param name="sender">主题变更的元素（导航视图）。</param>
    /// <param name="args">事件参数（未使用）。</param>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_hoverBrush is not null)
        {
            try
            {
                _hoverBrush.Color = GetHoverColor();
            }
            catch { }
        }
    }


    /// <summary>
    /// 将高亮条透明度动画到 1（约 150ms）。不重建 visual，隐藏后再显示仍可走弹簧路径。
    /// </summary>
    private void ShowHoverIndicator()
    {
        if (_hoverVisual is null || _compositor is null)
        {
            return;
        }
        ScalarKeyFrameAnimation fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        _hoverVisual.StartAnimation(nameof(Visual.Opacity), fade);
    }


    /// <summary>
    /// 将高亮条透明度动画到 0。不销毁 visual、不清 <see cref="_hoverPositioned"/>，
    /// 以便再次进入时继续从上次位置弹簧过渡。
    /// </summary>
    private void HideHoverIndicator()
    {
        if (_hoverVisual is null || _compositor is null)
        {
            return;
        }
        ScalarKeyFrameAnimation fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        _hoverVisual.StartAnimation(nameof(Visual.Opacity), fade);
    }


    /// <summary>
    /// 悬停文字右移：仅移动项模板中的文字 <c>ContentPresenter</c>（Spring <c>Translation.X</c>），不影响图标。
    /// </summary>
    /// <param name="item">目标导航项。</param>
    /// <param name="hovered">true 移到 <see cref="TextHoverOffsetX"/>；false 弹簧回 0。</param>
    private void AnimateContentHoverShift(NavigationViewItem item, bool hovered)
    {
        try
        {
            if (GetContentPresenter(item) is not UIElement presenter)
            {
                return;
            }
            Visual visual = ElementCompositionPreview.GetElementVisual(presenter);
            if (_compositor is null)
            {
                return;
            }
            if (hovered)
            {
                SpringScalarNaturalMotionAnimation anim = _compositor.CreateSpringScalarAnimation();
                anim.FinalValue = TextHoverOffsetX;
                anim.DampingRatio = 0.65f;
                anim.Period = TimeSpan.FromMilliseconds(70);
                // 须先 SetIsTranslationEnabled，否则 Translation.X 动画无效（见 GetContentPresenter）
                visual.StartAnimation("Translation.X", anim);
            }
            else
            {
                SpringScalarNaturalMotionAnimation anim = _compositor.CreateSpringScalarAnimation();
                anim.FinalValue = 0f;
                anim.DampingRatio = 0.55f;
                anim.Period = TimeSpan.FromMilliseconds(55);
                visual.StartAnimation("Translation.X", anim);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Animate nav item text hover shift.");
        }
    }


    /// <summary>
    /// 整项按压反馈：按下时快速缩放 + Z 轴下沉；抬起时弹簧回弹。作用于 LayoutRoot/Presenter（图标+文字）。
    /// </summary>
    /// <param name="item">目标导航项。</param>
    /// <param name="pressed">true 压入；false 释放回弹。</param>
    private void AnimateContentPress(NavigationViewItem item, bool pressed)
    {
        try
        {
            if (GetPressRoot(item) is not UIElement root)
            {
                return;
            }
            Visual visual = ElementCompositionPreview.GetElementVisual(root);
            if (_compositor is null)
            {
                return;
            }
            if (pressed)
            {
                // 按下用短关键帧（响应快）；抬起用弹簧（回弹手感）
                Vector3KeyFrameAnimation pressScale = _compositor.CreateVector3KeyFrameAnimation();
                pressScale.InsertKeyFrame(1f, new Vector3(ItemPressScale, ItemPressScale, 1f));
                pressScale.Duration = TimeSpan.FromMilliseconds(65);
                visual.StartAnimation("Scale", pressScale);

                ScalarKeyFrameAnimation pressZ = _compositor.CreateScalarKeyFrameAnimation();
                pressZ.InsertKeyFrame(1f, ItemPressDepthZ);
                pressZ.Duration = TimeSpan.FromMilliseconds(65);
                visual.StartAnimation("Translation.Z", pressZ);
            }
            else
            {
                SpringVector3NaturalMotionAnimation releaseScale = _compositor.CreateSpringVector3Animation();
                releaseScale.FinalValue = new Vector3(1f, 1f, 1f);
                releaseScale.DampingRatio = 0.58f;
                releaseScale.Period = TimeSpan.FromMilliseconds(38);
                visual.StartAnimation("Scale", releaseScale);

                SpringScalarNaturalMotionAnimation releaseZ = _compositor.CreateSpringScalarAnimation();
                releaseZ.FinalValue = 0f;
                releaseZ.DampingRatio = 0.52f;
                releaseZ.Period = TimeSpan.FromMilliseconds(42);
                visual.StartAnimation("Translation.Z", releaseZ);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Animate nav item press.");
        }
    }


    /// <summary>
    /// 取得并缓存导航项的文字 ContentPresenter，并开启 Translation 以便 Composition 平移。
    /// 找不到模板部件时返回 null（自定义模板可能无此名称）。
    /// </summary>
    /// <param name="item">目标导航项。</param>
    /// <returns>文字 ContentPresenter；模板中无该名称时为 null。</returns>
    private UIElement? GetContentPresenter(NavigationViewItem item)
    {
        if (_contentPresenters.TryGetValue(item, out UIElement? cached))
        {
            return cached;
        }
        if (item.FindDescendant("ContentPresenter") is UIElement presenter)
        {
            // Translation.* 动画依赖此开关，否则属性通道不生效
            ElementCompositionPreview.SetIsTranslationEnabled(presenter, true);
            _contentPresenters[item] = presenter;
            return presenter;
        }
        return null;
    }


    /// <summary>
    /// 取得并缓存按压动画的根容器（优先 LayoutRoot，其次 Presenter，最后 item 自身），
    /// 并开启 Translation，使 Scale 与 Translation.Z 同时作用于图标和文字。
    /// </summary>
    /// <param name="item">目标导航项。</param>
    /// <returns>用于 Scale / Translation.Z 的根元素；理论上总有回退（item 自身）。</returns>
    private UIElement? GetPressRoot(NavigationViewItem item)
    {
        if (_pressRoots.TryGetValue(item, out UIElement? cached))
        {
            return cached;
        }
        UIElement? root = (item.FindDescendant("LayoutRoot") as UIElement)
                          ?? (item.FindDescendant<NavigationViewItemPresenter>() as UIElement)
                          ?? item;
        if (root is not null)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(root, true);
            _pressRoots[item] = root;
        }
        return root;
    }
}
