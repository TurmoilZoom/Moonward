using CommunityToolkit.WinUI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;
using Windows.Foundation;

namespace Starward.Controls;

/// <summary>
/// 列表的滑动选中指示条：换选中项时，竖条从旧项滑到新项，途中略微拉长、到位收回。
/// <para>
/// XAML 结构：列表上方叠一个与列表同位置同大小的宿主（<c>IsHitTestVisible=False</c>，负责裁剪），宿主里放一层跟随滚动的容器，
/// 容器里放指示条。列表自带的指示条要在 <c>ListView.Resources</c> 里把 <c>ListViewItemSelectionIndicatorVisualEnabled</c> 设为 False。
/// </para>
/// </summary>
/// <remarks>
/// 跟随滚动用表达式动画把容器的 Translation 绑到列表 ScrollViewer 的滚动位移，指示条自己只在内容坐标里滑动：
/// 选中末项时列表会顺手把它滚进视野，若在滚动事件里重新对位，会把刚开始的滑动打断（实测）。
/// 只在这两个独立元素的 Visual 上做 Composition（位移走 Translation，拉伸走 Scale），不碰被测量的列表项容器，定位只读容器的布局位置。
/// 跨缩放换屏（XamlRoot 变化）时直接对位；系统关闭动画效果时也直接对位。
/// </remarks>
internal sealed class ListViewSlidingIndicator
{

    /// <summary>滑动时长，与 WinUI 导航栏选中指示条的观感接近。</summary>
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(350);

    /// <summary>途中最多拉长到原高度的倍数。</summary>
    private const float MaxStretch = 2.2f;

    /// <summary>与自带指示条一致：画在选中背景内侧，距列表项左缘 4（125% 缩放下实测 5 物理像素）。</summary>
    private const float IndicatorInset = 4f;


    private readonly ListView _list;

    private readonly FrameworkElement _host;

    private readonly FrameworkElement _indicator;

    private readonly Visual _visual;

    private readonly Visual _followerVisual;

    private readonly Compositor _compositor;

    private ScrollViewer? _scrollViewer;

    private XamlRoot? _xamlRoot;

    /// <summary>已经对过位：首次定位不播动画，免得指示条从顶部飞下来。</summary>
    private bool _positioned;

    /// <summary>选中项的容器还没生成（首次布局前），等下一次 LayoutUpdated 再定位。</summary>
    private bool _pendingLayout;


    /// <summary>
    /// 把指示条挂到列表上。元素显式传入：页面构造时还取不到父元素（实测 Parent 为 null）。
    /// </summary>
    /// <param name="list">列表。</param>
    /// <param name="host">覆盖在列表上、与列表同位置同大小的宿主，超出部分被它裁掉。</param>
    /// <param name="follower">宿主里跟随列表滚动的容器，指示条的父元素。</param>
    /// <param name="indicator">指示条元素，左上对齐。</param>
    public ListViewSlidingIndicator(ListView list, FrameworkElement host, FrameworkElement follower, FrameworkElement indicator)
    {
        _list = list;
        _host = host;
        _indicator = indicator;
        ElementCompositionPreview.SetIsTranslationEnabled(follower, true);
        ElementCompositionPreview.SetIsTranslationEnabled(indicator, true);
        _followerVisual = ElementCompositionPreview.GetElementVisual(follower);
        _visual = ElementCompositionPreview.GetElementVisual(indicator);
        _compositor = _visual.Compositor;
        _visual.Opacity = 0;
        _list.SelectionChanged += List_SelectionChanged;
        _list.Loaded += List_Loaded;
        _list.Unloaded += List_Unloaded;
        _host.SizeChanged += Host_SizeChanged;
    }


    private void List_Loaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = _list.FindDescendant<ScrollViewer>();
        if (_scrollViewer is not null)
        {
            // 滚动位移是负的内容偏移，跟随容器同步平移
            CompositionPropertySet scroller = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(_scrollViewer);
            ExpressionAnimation follow = _compositor.CreateExpressionAnimation("Vector3(0, scroller.Translation.Y, 0)");
            follow.SetReferenceParameter("scroller", scroller);
            _followerVisual.StartAnimation("Translation", follow);
        }
        _xamlRoot = _list.XamlRoot;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed += XamlRoot_Changed;
        }
        Enqueue(animate: false);
    }


    private void List_Unloaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = null;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= XamlRoot_Changed;
            _xamlRoot = null;
        }
        StopLayoutWait();
        // 离开视觉树时停掉还在跑的动画，免得合成器继续持有它们
        _followerVisual.StopAnimation("Translation");
        _visual.StopAnimation("Translation");
        _visual.StopAnimation("Scale");
        _positioned = false;
    }


    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 换选中项时容器的选中态与布局可能还没更新，排到本轮布局之后再取位置
        Enqueue(animate: true);
    }


    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        Enqueue(animate: false);
    }


    /// <summary>
    /// 宿主大小变化：更新裁剪区，指示条随列表滚出可视区时不露到卡片边距上；再对一次位。
    /// 只改 Clip 与合成层位移，不动布局，可以在 SizeChanged 里做。
    /// </summary>
    private void Host_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _host.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        Enqueue(animate: false);
    }


    /// <summary>
    /// 推到本轮布局之后再定位，不在布局回调里直接动合成层。
    /// </summary>
    private void Enqueue(bool animate)
    {
        _list.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () => Move(animate));
    }


    /// <summary>
    /// 把指示条对到当前选中项的左侧中线（内容坐标，滚动由跟随容器负责）。没有选中项时隐藏。
    /// </summary>
    /// <param name="animate">是否从当前位置滑过去。</param>
    private void Move(bool animate)
    {
        if (!_list.IsLoaded || _list.SelectedItem is null)
        {
            _visual.Opacity = 0;
            return;
        }
        if (_list.ContainerFromItem(_list.SelectedItem) is not FrameworkElement { IsLoaded: true } container || container.ActualHeight <= 0)
        {
            // 首次打开时容器在下一次布局才生成
            WaitForLayout();
            return;
        }
        StopLayoutWait();

        // 布局位置是相对可视区的，加回当前滚动偏移换成内容坐标
        Point origin = container.TransformToVisual(_host).TransformPoint(new Point(0, 0));
        double scrollOffset = _scrollViewer?.VerticalOffset ?? 0;
        float height = (float)_indicator.ActualHeight;
        var target = new Vector3((float)origin.X + IndicatorInset, (float)(origin.Y + scrollOffset + (container.ActualHeight - height) / 2), 0);
        _visual.CenterPoint = new Vector3((float)_indicator.ActualWidth / 2, height / 2, 0);

        if (!animate || !_positioned || !EntranceAnimation.AnimationsEnabled())
        {
            // 先停动画再赋值，否则会被还在跑的动画覆盖
            _visual.StopAnimation("Translation");
            _visual.StopAnimation("Scale");
            _visual.Properties.InsertVector3("Translation", target);
            _visual.Scale = Vector3.One;
        }
        else
        {
            _visual.Properties.TryGetVector3("Translation", out Vector3 from);
            float distance = MathF.Abs(target.Y - from.Y);
            CubicBezierEasingFunction easing = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f));

            Vector3KeyFrameAnimation slide = _compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(1f, target, easing);
            slide.Duration = SlideDuration;
            _visual.StartAnimation("Translation", slide);

            // 拉伸量随距离增长：相邻两项轻微拉长，跨好几项时更明显
            float stretch = height > 0 ? Math.Min(1f + distance / (height * 4), MaxStretch) : 1f;
            Vector3KeyFrameAnimation scale = _compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0.35f, new Vector3(1f, stretch, 1f), easing);
            scale.InsertKeyFrame(1f, Vector3.One, easing);
            scale.Duration = SlideDuration;
            _visual.StartAnimation("Scale", scale);
        }
        _positioned = true;
        _visual.Opacity = 1;
    }


    private void WaitForLayout()
    {
        if (!_pendingLayout)
        {
            _pendingLayout = true;
            _list.LayoutUpdated += List_LayoutUpdated;
        }
    }


    private void StopLayoutWait()
    {
        if (_pendingLayout)
        {
            _pendingLayout = false;
            _list.LayoutUpdated -= List_LayoutUpdated;
        }
    }


    private void List_LayoutUpdated(object? sender, object e)
    {
        // LayoutUpdated 在布局过程中触发，推到之后再定位
        StopLayoutWait();
        Enqueue(animate: false);
    }

}
