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
using System.Numerics;
using Windows.Foundation;

namespace Starward.Controls;

/// <summary>
/// 每个 <see cref="XamlRoot"/> 共享一个 <see cref="Popup"/>，承载即时 Tooltip 的显示、定位与 Composition 动画。
/// <para>
/// 由 <see cref="InstantTooltip"/> 按窗口创建与释放；多锚点注册指针事件后，悬停时复用同一气泡改文案与偏移。
/// 无 XAML 模板：UI 在构造函数中代码搭建（Border + TextBlock）。
/// </para>
/// </summary>
internal sealed class InstantTooltipHost
{
    /// <summary>入场起始缩放（缩放原点随方位靠近锚点一侧）。</summary>
    private const float InitialScale = 0.7f;

    /// <summary>入场动画时长（毫秒）。</summary>
    private const int ShowDurationMs = 500;

    /// <summary>退场动画时长（毫秒）。</summary>
    private const int HideDurationMs = 150;

    /// <summary>提示与锚点元素的间距（像素）。</summary>
    private const double Gap = 8;

    /// <summary>本宿主所属的视觉树根（Popup 挂载点）。</summary>
    private readonly XamlRoot _xamlRoot;

    /// <summary>用于延后隐藏判断与动画完成后回 UI 线程关 Popup。</summary>
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>承载提示内容的轻量弹出层；全窗口唯一实例。</summary>
    private readonly Popup _popup;

    /// <summary>提示气泡容器（亚克力背景、圆角、内边距）。</summary>
    private readonly Border _content;

    /// <summary>提示正文。</summary>
    private readonly TextBlock _text;

    /// <summary>驱动 scale / opacity 关键帧动画的 Composition 合成器。</summary>
    private readonly Compositor _compositor;

    /// <summary>解析 ThemeResource 时优先查此元素的 Resources，再回退 Application.Resources。</summary>
    private readonly FrameworkElement _themeSource;

    /// <summary>已注册指针事件的锚点集合；用于去重与 Dispose 时批量解绑。</summary>
    private readonly HashSet<FrameworkElement> _elements = [];

    /// <summary>
    /// 指针是否仍在任一已注册锚点内。
    /// 相邻项切换时 Exited→Entered 之间短暂为 false，配合延后隐藏避免闪烁。
    /// </summary>
    private bool _pointerInsideAnyElement;

    /// <summary>是否已排队/正在执行隐藏流程，防止重复触发退场动画。</summary>
    private bool _hideScheduled;

    /// <summary>当前正在展示 Tooltip 的锚点；注销该锚点时需立即隐藏。</summary>
    private FrameworkElement? _currentAnchor;

    /// <summary>当前展示所用的方位（影响偏移与缩放中心）。</summary>
    private InstantTooltipPlacement _currentPlacement = InstantTooltipPlacement.Right;


    /// <summary>当前是否无任何挂接元素（为 true 时 <see cref="InstantTooltip"/> 可释放本 Host）。</summary>
    public bool IsEmpty => _elements.Count == 0;

    /// <summary>本宿主所属的视觉树根（与字典键一致）。</summary>
    public XamlRoot XamlRoot => _xamlRoot;


    /// <summary>
    /// 为指定视觉树根创建 Tooltip 宿主（搭建 Popup 视觉树，默认不打开）。
    /// </summary>
    /// <param name="xamlRoot">用于 Popup 挂载的 XamlRoot。</param>
    /// <param name="themeSource">用于解析 ThemeResource 与取得 <see cref="DispatcherQueue"/> 的元素。</param>
    public InstantTooltipHost(XamlRoot xamlRoot, FrameworkElement themeSource)
    {
        _xamlRoot = xamlRoot;
        _dispatcherQueue = themeSource.DispatcherQueue;
        _themeSource = themeSource;

        _text = new TextBlock
        {
            // 跟随系统文字缩放会改变测量尺寸，定位易抖，故关闭
            IsTextScaleFactorEnabled = false,
            MaxWidth = 320,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush"),
        };

        _content = new Border
        {
            Padding = new Thickness(12, 6, 12, 6),
            Background = GetThemeBrush("CustomOverlayAcrylicBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Child = _text,
        };

        _popup = new Popup
        {
            // 点击其它区域不自动关闭；由指针进出锚点控制显隐
            IsLightDismissEnabled = false,
            Child = _content,
            XamlRoot = xamlRoot,
        };

        _compositor = ElementCompositionPreview.GetElementVisual(_content).Compositor;
    }


    /// <summary>
    /// 将元素注册到本宿主；重复注册会被忽略。
    /// </summary>
    /// <param name="element">接收指针事件的锚点元素。</param>
    public void Register(FrameworkElement element)
    {
        if (!_elements.Add(element))
        {
            return;
        }

        element.PointerEntered += Element_PointerEntered;
        element.PointerExited += Element_PointerExited;
        element.Unloaded += Element_Unloaded;
    }


    /// <summary>
    /// 解除元素注册并清理事件订阅；若正是当前展示锚点则隐藏 Tooltip。
    /// </summary>
    /// <param name="element">待注销的锚点元素。</param>
    public void Unregister(FrameworkElement element)
    {
        if (!_elements.Remove(element))
        {
            return;
        }

        element.PointerEntered -= Element_PointerEntered;
        element.PointerExited -= Element_PointerExited;
        element.Unloaded -= Element_Unloaded;

        if (_currentAnchor == element)
        {
            _pointerInsideAnyElement = false;
            HideTooltip();
        }
    }


    /// <summary>
    /// 关闭 Popup、解绑全部锚点并清空状态（Host 从字典移除前调用）。
    /// </summary>
    public void Dispose()
    {
        foreach (FrameworkElement element in _elements)
        {
            element.PointerEntered -= Element_PointerEntered;
            element.PointerExited -= Element_PointerExited;
            element.Unloaded -= Element_Unloaded;
        }

        _elements.Clear();
        _popup.IsOpen = false;
        _pointerInsideAnyElement = false;
        _hideScheduled = false;
        _currentAnchor = null;
    }


    /// <summary>
    /// 锚点离开视觉树：先本宿主注销，再交给 <see cref="InstantTooltip.OnElementUnloaded"/>
    /// 处理 Host 生命周期与虚拟化复用后的重新挂接。
    /// </summary>
    /// <param name="sender">卸载的锚点。</param>
    /// <param name="e">路由事件参数。</param>
    private void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            // 不依赖 element.XamlRoot（卸载后可能已空），由本实例直接注销
            Unregister(element);
            InstantTooltip.OnElementUnloaded(element, this);
        }
    }


    /// <summary>
    /// 指针进入锚点：取消待隐藏并立即显示对应文案。
    /// </summary>
    /// <param name="sender">锚点元素。</param>
    /// <param name="e">指针事件参数。</param>
    private void Element_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideAnyElement = true;
        _hideScheduled = false;
        if (sender is FrameworkElement element)
        {
            ShowTooltip(element);
        }
    }


    /// <summary>
    /// 指针离开锚点：延后一拍再决定是否隐藏，避免相邻项切换时气泡闪断。
    /// </summary>
    /// <param name="sender">锚点元素。</param>
    /// <param name="e">指针事件参数。</param>
    private void Element_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideAnyElement = false;
        // 相邻元素切换会先后触发 Exited(旧) → Entered(新)，延后判断避免闪烁。
        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_pointerInsideAnyElement && !_hideScheduled)
            {
                _hideScheduled = true;
                HideTooltip();
            }
        });
    }


    /// <summary>
    /// 显示并定位指定元素的 Tooltip，同时播放入场动画。
    /// </summary>
    /// <param name="element">当前悬停的锚点；不可见或文案为空时直接返回。</param>
    private void ShowTooltip(FrameworkElement element)
    {
        if (element.Visibility != Visibility.Visible)
        {
            return;
        }

        string? label = InstantTooltip.GetText(element);
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        _currentAnchor = element;
        _currentPlacement = InstantTooltip.GetPlacement(element);
        _text.Text = label;
        UpdatePosition(element);
        // 须在 IsOpen 前重置 visual，否则会先闪一帧完整大小
        PrepareShowVisual();
        _popup.IsOpen = true;
        PlayShowAnimation();
    }


    /// <summary>
    /// 测量提示内容尺寸。Popup 已打开时 <see cref="FrameworkElement.ActualWidth"/> 可能仍是上一段文案的布局结果，故只取 <see cref="FrameworkElement.DesiredSize"/>。
    /// </summary>
    /// <returns>当前文案对应的测量尺寸。</returns>
    private Size MeasureTooltipContent()
    {
        _content.InvalidateMeasure();
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _content.DesiredSize;
    }


    /// <summary>
    /// 按当前方位将 Popup 定位到锚点附近（窗口坐标系，经 <see cref="UIElement.TransformToVisual"/>）。
    /// 首选方位空间不足时会翻转（Top↔Bottom / Left↔Right），再钳位到 XamlRoot 可视区内，避免贴窗边被裁切。
    /// </summary>
    /// <param name="element">锚点元素。</param>
    private void UpdatePosition(FrameworkElement element)
    {
        Size tipSize = MeasureTooltipContent();
        double tipWidth = tipSize.Width;
        double tipHeight = tipSize.Height;

        GeneralTransform transform = element.TransformToVisual(null);
        Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        Size rootSize = _xamlRoot.Size;
        const double margin = 8;
        bool hasRoot = rootSize.Width > 0 && rootSize.Height > 0;

        // 首选方位放不下时翻到对侧，保证气泡完整可见（仍贴锚点）
        InstantTooltipPlacement placement = _currentPlacement;
        if (hasRoot)
        {
            placement = placement switch
            {
                InstantTooltipPlacement.Top when bounds.Top - Gap - tipHeight < margin
                    && bounds.Bottom + Gap + tipHeight + margin <= rootSize.Height
                    => InstantTooltipPlacement.Bottom,
                InstantTooltipPlacement.Bottom when bounds.Bottom + Gap + tipHeight > rootSize.Height - margin
                    && bounds.Top - Gap - tipHeight >= margin
                    => InstantTooltipPlacement.Top,
                InstantTooltipPlacement.Left when bounds.Left - Gap - tipWidth < margin
                    && bounds.Right + Gap + tipWidth + margin <= rootSize.Width
                    => InstantTooltipPlacement.Right,
                InstantTooltipPlacement.Right when bounds.Right + Gap + tipWidth > rootSize.Width - margin
                    && bounds.Left - Gap - tipWidth >= margin
                    => InstantTooltipPlacement.Left,
                _ => placement,
            };
            // 入场缩放原点随实际方位更新
            _currentPlacement = placement;
        }

        double x;
        double y;
        switch (placement)
        {
            case InstantTooltipPlacement.Left:
                x = bounds.Left - tipWidth - Gap;
                y = bounds.Top + (bounds.Height - tipHeight) / 2;
                break;
            case InstantTooltipPlacement.Top:
                x = bounds.Left + (bounds.Width - tipWidth) / 2;
                y = bounds.Top - tipHeight - Gap;
                break;
            case InstantTooltipPlacement.Bottom:
                x = bounds.Left + (bounds.Width - tipWidth) / 2;
                y = bounds.Bottom + Gap;
                break;
            default:
                // Right：导航 LeftCompact 侧栏默认，贴在锚点右侧垂直居中
                x = bounds.Right + Gap;
                y = bounds.Top + (bounds.Height - tipHeight) / 2;
                break;
        }

        // 水平/垂直钳位：靠近窗边时把气泡整体移入可视区（如 Top 居中超出右缘）
        if (hasRoot)
        {
            double maxX = Math.Max(margin, rootSize.Width - tipWidth - margin);
            double maxY = Math.Max(margin, rootSize.Height - tipHeight - margin);
            x = Math.Clamp(x, margin, maxX);
            y = Math.Clamp(y, margin, maxY);
        }

        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
    }


    /// <summary>
    /// 重置 Composition 视觉状态为入场起点（靠近锚点一侧缩放 0.7 + 透明）。
    /// </summary>
    private void PrepareShowVisual()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(_content);
        visual.CenterPoint = GetScaleCenterPoint();
        visual.Scale = new Vector3(InitialScale, InitialScale, 1f);
        visual.Opacity = 0f;
    }


    /// <summary>
    /// 播放从小到大的入场动画（scale 0.7→1 + 淡入，500ms 缓动）。
    /// 全局关闭动画时直接设为最终态。
    /// </summary>
    private void PlayShowAnimation()
    {
        if (!EntranceAnimation.AnimationsEnabled())
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(_content);
            visual.Scale = Vector3.One;
            visual.Opacity = 1f;
            return;
        }

        Visual v = ElementCompositionPreview.GetElementVisual(_content);
        v.CenterPoint = GetScaleCenterPoint();

        CubicBezierEasingFunction ease = _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

        Vector3KeyFrameAnimation scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(InitialScale, InitialScale, 1f));
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(ShowDurationMs);
        v.StartAnimation(nameof(Visual.Scale), scale);

        ScalarKeyFrameAnimation opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(1f, 1f, ease);
        opacity.Duration = TimeSpan.FromMilliseconds(ShowDurationMs);
        v.StartAnimation(nameof(Visual.Opacity), opacity);
    }


    /// <summary>
    /// 隐藏 Tooltip；有动画时先快速缩小淡出，动画结束后再关闭 Popup。
    /// 退场期间若指针再次进入任一锚点，则不关闭 Popup（避免打断新目标的展示）。
    /// </summary>
    private void HideTooltip()
    {
        if (!_popup.IsOpen)
        {
            _hideScheduled = false;
            return;
        }

        if (!EntranceAnimation.AnimationsEnabled())
        {
            _popup.IsOpen = false;
            _hideScheduled = false;
            _currentAnchor = null;
            return;
        }

        Visual v = ElementCompositionPreview.GetElementVisual(_content);
        v.CenterPoint = GetScaleCenterPoint();

        CubicBezierEasingFunction ease = _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

        Vector3KeyFrameAnimation scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1f, new Vector3(InitialScale, InitialScale, 1f), ease);
        scale.Duration = TimeSpan.FromMilliseconds(HideDurationMs);

        ScalarKeyFrameAnimation opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, 0f, ease);
        opacity.Duration = TimeSpan.FromMilliseconds(HideDurationMs);

        // ScopedBatch：等 scale/opacity 都结束后再关 Popup，避免动画中途被拆掉
        CompositionScopedBatch batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _hideScheduled = false;
                // 退场过程中若已 Entered 新锚点，保留 Popup 由 ShowTooltip 接管
                if (!_pointerInsideAnyElement)
                {
                    _popup.IsOpen = false;
                    _currentAnchor = null;
                }
            });
        };
        v.StartAnimation(nameof(Visual.Scale), scale);
        v.StartAnimation(nameof(Visual.Opacity), opacity);
        batch.End();
    }


    /// <summary>
    /// 从元素局部资源或应用资源字典解析主题画刷。
    /// </summary>
    /// <param name="resourceKey">ThemeResource 键名。</param>
    /// <returns>解析到的画刷；失败时返回透明画刷。</returns>
    private Brush GetThemeBrush(string resourceKey)
    {
        if (_themeSource.Resources.TryGetValue(resourceKey, out object? local) && local is Brush localBrush)
        {
            return localBrush;
        }

        if (Application.Current.Resources.TryGetValue(resourceKey, out object? app) && app is Brush appBrush)
        {
            return appBrush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }


    /// <summary>
    /// 按当前方位取得缩放原点（靠近锚点的那条边中点，使提示从锚点方向展开）。
    /// </summary>
    /// <returns>Border 局部坐标系下的 Composition 中心点。</returns>
    private Vector3 GetScaleCenterPoint()
    {
        Size tipSize = MeasureTooltipContent();
        double width = tipSize.Width;
        double height = tipSize.Height;

        // Right → 左边中点；Left → 右边中点；Top/Bottom 同理取靠近锚点一侧
        return _currentPlacement switch
        {
            InstantTooltipPlacement.Left => new Vector3((float)width, (float)(height / 2), 0f),
            InstantTooltipPlacement.Top => new Vector3((float)(width / 2), (float)height, 0f),
            InstantTooltipPlacement.Bottom => new Vector3((float)(width / 2), 0f, 0f),
            _ => new Vector3(0f, (float)(height / 2), 0f),
        };
    }
}
