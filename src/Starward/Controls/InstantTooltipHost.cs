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
/// 每个 <see cref="XamlRoot"/> 共享一个 Popup 实例，承载即时 Tooltip 的显示、定位与 Composition 动画。
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

    private readonly XamlRoot _xamlRoot;

    private readonly DispatcherQueue _dispatcherQueue;

    private readonly Popup _popup;

    private readonly Border _content;

    private readonly TextBlock _text;

    private readonly Compositor _compositor;

    private readonly FrameworkElement _themeSource;

    private readonly HashSet<FrameworkElement> _elements = [];

    private bool _pointerInsideAnyElement;

    private bool _hideScheduled;

    private FrameworkElement? _currentAnchor;

    private InstantTooltipPlacement _currentPlacement = InstantTooltipPlacement.Right;


    /// <summary>当前是否无任何挂接元素。</summary>
    public bool IsEmpty => _elements.Count == 0;


    /// <summary>
    /// 为指定视觉树根创建 Tooltip 宿主。
    /// </summary>
    /// <param name="xamlRoot">用于 Popup 与主题资源的 XamlRoot。</param>
    /// <param name="themeSource">用于解析 ThemeResource 的元素。</param>
    public InstantTooltipHost(XamlRoot xamlRoot, FrameworkElement themeSource)
    {
        _xamlRoot = xamlRoot;
        _dispatcherQueue = themeSource.DispatcherQueue;
        _themeSource = themeSource;

        _text = new TextBlock
        {
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
    /// 解除元素注册并清理事件订阅。
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
    /// 关闭 Popup 并清空全部挂接。
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


    private void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            InstantTooltip.OnElementUnloaded(element);
        }
    }


    private void Element_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideAnyElement = true;
        _hideScheduled = false;
        if (sender is FrameworkElement element)
        {
            ShowTooltip(element);
        }
    }


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
        PrepareShowVisual();
        _popup.IsOpen = true;
        PlayShowAnimation();
    }


    /// <summary>
    /// 按当前方位将 Popup 定位到锚点附近（窗口坐标系）。
    /// </summary>
    /// <param name="element">锚点元素。</param>
    private void UpdatePosition(FrameworkElement element)
    {
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tipWidth = Math.Max(_content.DesiredSize.Width, _content.ActualWidth);
        double tipHeight = Math.Max(_content.DesiredSize.Height, _content.ActualHeight);

        GeneralTransform transform = element.TransformToVisual(null);
        Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        switch (_currentPlacement)
        {
            case InstantTooltipPlacement.Left:
                _popup.HorizontalOffset = bounds.Left - tipWidth - Gap;
                _popup.VerticalOffset = bounds.Top + (bounds.Height - tipHeight) / 2;
                break;
            case InstantTooltipPlacement.Top:
                _popup.HorizontalOffset = bounds.Left + (bounds.Width - tipWidth) / 2;
                _popup.VerticalOffset = bounds.Top - tipHeight - Gap;
                break;
            case InstantTooltipPlacement.Bottom:
                _popup.HorizontalOffset = bounds.Left + (bounds.Width - tipWidth) / 2;
                _popup.VerticalOffset = bounds.Bottom + Gap;
                break;
            default:
                _popup.HorizontalOffset = bounds.Right + Gap;
                _popup.VerticalOffset = bounds.Top + (bounds.Height - tipHeight) / 2;
                break;
        }
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
    /// 播放从小到大的入场动画（scale 0.7→1 + 淡入，500ms smootherstep 缓动）。
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

        CompositionScopedBatch batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _hideScheduled = false;
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
    /// 从应用/页面资源字典解析主题画刷。
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
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Math.Max(_content.ActualWidth, _content.DesiredSize.Width);
        double height = Math.Max(_content.ActualHeight, _content.DesiredSize.Height);

        return _currentPlacement switch
        {
            InstantTooltipPlacement.Left => new Vector3((float)width, (float)(height / 2), 0f),
            InstantTooltipPlacement.Top => new Vector3((float)(width / 2), (float)height, 0f),
            InstantTooltipPlacement.Bottom => new Vector3((float)(width / 2), 0f, 0f),
            _ => new Vector3(0f, (float)(height / 2), 0f),
        };
    }
}