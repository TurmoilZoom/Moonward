using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Controls;

/// <summary>
/// 为 NavigationView 提供可复用的流体交互动画效果：
/// - 悬停高亮条（使用 Composition ShapeVisual + 圆角矩形 + Spring 动画跟随）
/// - 悬停时仅文字 ContentPresenter 向右轻移（SpringScalar）
/// - 按下时整个项（LayoutRoot 或 Presenter）缩放 + Z 轴深度按压（物理按钮感），抬起 Spring 回弹
/// 
/// 用法：
/// 1. NavigationView XAML 中设置 Resources 让内置 hover/pressed 背景透明（效果接管）：
///    &lt;SolidColorBrush x:Key="NavigationViewItemBackgroundPointerOver" Color="Transparent" /&gt;
///    &lt;SolidColorBrush x:Key="NavigationViewItemBackgroundPressed" Color="Transparent" /&gt;
/// 2. 在 NavigationView 同级（或合适层级）放置一个 overlay Grid 作为宿主：
///    &lt;Grid x:Name="NavIndicatorHost" Width="260" HorizontalAlignment="Left" IsHitTestVisible="False" /&gt;
///    （Width 应与 OpenPaneLength 一致，定位需保证能覆盖 pane 区域；IsHitTestVisible=False 让事件穿透）
/// 3. 在 Loaded 中调用 Attach(navView, host, logger)。
/// 4. 在 Unloaded 中调用 Detach()。
/// </summary>
public sealed class FluidNavigationViewHoverEffect
{
    /// <summary>
    /// 悬停时文字向右平移量（像素），轻微即可。
    /// </summary>
    private const float TextHoverOffsetX = 3.2f;

    /// <summary>
    /// 按下时整体列表项的缩放系数（&lt;1 产生“压扁按下”感），与 Z 轴纵深按压配合，模拟物理按钮受力后的轻微形变与后退。
    /// </summary>
    private const float ItemPressScale = 0.978f;

    /// <summary>
    /// 按下时整体列表项沿 Z 轴（垂直于屏幕向内）的按压深度（负值表示向屏幕内部按入），
    /// 模拟真实物理按钮被垂直按下的纵深运动，增强真实感和即时响应。
    /// </summary>
    private const float ItemPressDepthZ = -5.0f;

    private Compositor? _compositor;

    private NavigationView? _navView;

    private Grid? _host;

    private DispatcherQueue? _dispatcherQueue;

    private ILogger? _logger;

    private bool _pointerInsideAnyItem;

    private bool _hoverPositioned;

    private ShapeVisual? _hoverVisual;

    private CompositionRoundedRectangleGeometry? _hoverGeometry;

    private CompositionColorBrush? _hoverBrush;

    /// <summary>
    /// 缓存每个导航项的文字 ContentPresenter，用于悬停时文字向右平移。
    /// </summary>
    private readonly Dictionary<NavigationViewItem, UIElement> _contentPresenters = new();

    /// <summary>
    /// 缓存每个导航项的按压反馈根容器（LayoutRoot 或 NavigationViewItemPresenter），
    /// 使缩放和Z轴按压作用于图标+文字整体，实现列表项被“按下去”的物理感。
    /// </summary>
    private readonly Dictionary<NavigationViewItem, UIElement> _pressRoots = new();

    private bool _attached;


    public void Attach(NavigationView navView, Grid host, ILogger? logger = null)
    {
        if (_attached)
        {
            Detach();
        }

        _navView = navView ?? throw new ArgumentNullException(nameof(navView));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger;
        _dispatcherQueue = navView.DispatcherQueue;

        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

        WireEvents();

        if (_navView is FrameworkElement fe)
        {
            fe.ActualThemeChanged += OnActualThemeChanged;
        }

        _attached = true;
    }


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

        // 清理 composition visual
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
        _pointerInsideAnyItem = false;
        _hoverPositioned = false;
        _contentPresenters.Clear();
        _pressRoots.Clear();

        _navView = null;
        _host = null;
        _logger = null;
        _dispatcherQueue = null;
        _attached = false;
    }


    private void WireEvents()
    {
        if (_navView is null)
        {
            return;
        }

        foreach (NavigationViewItem item in _navView.MenuItems.OfType<NavigationViewItem>())
        {
            item.PointerEntered += NavigationViewItem_PointerEntered;
            item.PointerExited += NavigationViewItem_PointerExited;
            // 按下 / 抬起会被 NavigationViewItem 内部标记为已处理，需用 AddHandler(..., handledEventsToo: true) 才能收到
            item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(NavigationViewItem_PointerPressed), true);
            item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(NavigationViewItem_PointerReleased), true);
            item.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(NavigationViewItem_PointerReleased), true);
            item.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(NavigationViewItem_PointerReleased), true);
        }
    }


    private void UnwireEvents()
    {
        if (_navView is null)
        {
            return;
        }

        foreach (NavigationViewItem item in _navView.MenuItems.OfType<NavigationViewItem>())
        {
            item.PointerEntered -= NavigationViewItem_PointerEntered;
            item.PointerExited -= NavigationViewItem_PointerExited;
            item.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(NavigationViewItem_PointerPressed));
            item.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(NavigationViewItem_PointerReleased));
            item.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(NavigationViewItem_PointerReleased));
            item.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(NavigationViewItem_PointerReleased));
        }
    }


    private void NavigationViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideAnyItem = true;
        if (sender is NavigationViewItem item)
        {
            MoveHoverIndicatorTo(item);
            AnimateContentHoverShift(item, true);
        }
    }


    private void NavigationViewItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideAnyItem = false;
        if (sender is NavigationViewItem item)
        {
            AnimateContentPress(item, false);
            AnimateContentHoverShift(item, false);
        }
        // 在相邻项之间移动会先后触发 Exited(旧项) → Entered(新项)。
        // 延后到低优先级再判断：若期间进入了新项，则不淡出，避免闪烁。
        _dispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_pointerInsideAnyItem)
            {
                HideHoverIndicator();
            }
        });
    }


    private void NavigationViewItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is NavigationViewItem item)
        {
            AnimateContentPress(item, true);
        }
    }


    private void NavigationViewItem_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is NavigationViewItem item)
        {
            AnimateContentPress(item, false);
        }
    }


    /// <summary>
    /// 让流体高亮条以弹簧动画移动到目标导航项的高亮区域（即模板中的 LayoutRoot），
    /// 其尺寸与被选中项的高亮框完全一致。
    /// </summary>
    private void MoveHoverIndicatorTo(NavigationViewItem item)
    {
        try
        {
            EnsureHoverIndicator();
            if (_host is null)
            {
                return;
            }
            // LayoutRoot 即绘制悬停 / 选中背景的元素，其边界等于"被选中那一栏的高亮框"
            FrameworkElement target = (item.FindDescendant("LayoutRoot") as FrameworkElement)
                                      ?? (item.FindDescendant<NavigationViewItemPresenter>() as FrameworkElement)
                                      ?? item;
            Point point = target.TransformToVisual(_host).TransformPoint(default);
            var size = new Vector2((float)target.ActualWidth, (float)target.ActualHeight);
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }
            _hoverVisual!.Size = size;
            _hoverGeometry!.Size = size;
            var offset = new Vector3((float)point.X, (float)point.Y, 0);
            if (_hoverPositioned)
            {
                // 弹簧动画（Spring Animation）：高亮条在列表项之间平滑过渡
                SpringVector3NaturalMotionAnimation spring = _compositor!.CreateSpringVector3Animation();
                spring.FinalValue = offset;
                spring.DampingRatio = 0.7f;
                spring.Period = TimeSpan.FromMilliseconds(45);
                _hoverVisual.StartAnimation(nameof(Visual.Offset), spring);
            }
            else
            {
                // 首次出现：直接定位，避免从 (0,0) 滑入
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
    /// 惰性创建流体高亮条对应的 Composition 视觉对象。
    /// </summary>
    private void EnsureHoverIndicator()
    {
        if (_hoverVisual is not null || _compositor is null || _host is null)
        {
            return;
        }
        float radius = 4f;
        if (Application.Current.Resources.TryGetValue("ControlCornerRadius", out object? value) && value is CornerRadius cornerRadius)
        {
            radius = (float)cornerRadius.TopLeft;
        }
        _hoverGeometry = _compositor.CreateRoundedRectangleGeometry();
        _hoverGeometry.CornerRadius = new Vector2(radius);
        _hoverBrush = _compositor.CreateColorBrush(GetHoverColor());
        CompositionSpriteShape shape = _compositor.CreateSpriteShape(_hoverGeometry);
        shape.FillBrush = _hoverBrush;
        _hoverVisual = _compositor.CreateShapeVisual();
        _hoverVisual.Shapes.Add(shape);
        _hoverVisual.Opacity = 0;
        ElementCompositionPreview.SetElementChildVisual(_host, _hoverVisual);
    }


    /// <summary>
    /// 高亮条填充色：跟随明暗主题取半透明白 / 黑，作为轻量的悬停预览反馈。
    /// </summary>
    private Color GetHoverColor()
    {
        var theme = _navView?.ActualTheme ?? ElementTheme.Default;
        if (theme == ElementTheme.Default)
        {
            theme = _host?.ActualTheme ?? ElementTheme.Light;
        }
        return theme == ElementTheme.Dark
            ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x16, 0x00, 0x00, 0x00);
    }


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
    /// 悬停文字右移：鼠标悬停于导航项时，文字 ContentPresenter 向右轻微平移；
    /// 离开时以弹簧动画弹性恢复原位，整个过程流畅有弹性。
    /// 仅移动文字，不影响图标。
    /// </summary>
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
    /// 物理按压反馈动画：点击时让整个列表项（通过其根容器）轻微缩放 + 沿Z轴垂直按压，
    /// 产生“真实物理按钮被按下去”的即时响应感；抬起时弹簧回弹，增加弹性真实感。
    /// 按压方向为垂直于屏幕的Z轴（负Z向内），同时作用于图标和文字区域。
    /// </summary>
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
                // 快速压入（缩放 + Z轴向内下沉），模拟真实物理按钮被垂直按入屏幕的纵深形变
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
                // 弹簧释放：带阻尼的弹性回弹，带来真实按钮的回弹手感
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
    /// 取得（并缓存）导航项的文字 ContentPresenter，同时开启其 Translation 能力以便用 Composition 动画平移。
    /// 用于悬停右移效果。
    /// </summary>
    private UIElement? GetContentPresenter(NavigationViewItem item)
    {
        if (_contentPresenters.TryGetValue(item, out UIElement? cached))
        {
            return cached;
        }
        if (item.FindDescendant("ContentPresenter") is UIElement presenter)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(presenter, true);
            _contentPresenters[item] = presenter;
            return presenter;
        }
        return null;
    }


    /// <summary>
    /// 取得（并缓存）用于按压反馈的根容器，优先 "LayoutRoot"（高亮绘制区）或 NavigationViewItemPresenter，
    /// 这样 Scale 和 Translation.Z 会同时影响图标与文字，让用户感觉“整个列表项被按下去了”。
    /// </summary>
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
