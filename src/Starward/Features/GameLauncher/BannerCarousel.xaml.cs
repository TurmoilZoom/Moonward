using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Starward.Controls;
using Starward.Core.HoYoPlay;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 软件首页的游戏轮播图控件，PanelSlideshow：
/// <list type="bullet">
/// <item>单槽呈现：呈现区始终只放当前一张图，切页时新旧两张各自做 Storyboard 平移（推拉效果），首尾连续无回滚。</item>
/// <item>可点击的 <see cref="PipsPager"/> 圆点指示器（替代原右下角「页数/总数」文字）。</item>
/// <item>悬停时淡入并放大的左右翻页按钮（VisualState Storyboard 动画）。</item>
/// <item>5 秒自动轮播，鼠标悬停或窗口隐藏时暂停。</item>
/// </list>
/// </summary>
public sealed partial class BannerCarousel : UserControl
{

    /// <summary>切页 Storyboard 动画时长（毫秒）。</summary>
    private const double SlideDurationMs = 600;

    /// <summary>呈现区尚未完成布局量测时的回退宽度，用于计算推拉位移。</summary>
    private const double DefaultPresenterWidth = 380;


    /// <summary>5 秒间隔的自动轮播定时器。</summary>
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

    /// <summary>与 <see cref="Banners"/> 一一对应的预创建图片元素，切页时复用避免重复下载解码。</summary>
    private readonly List<CachedImage> _imageElements = new();

    /// <summary>当前显示的下标；-1 表示尚未初始化。</summary>
    private int _currentIndex = -1;

    /// <summary>指针是否悬停在控件上；悬停时暂停自动轮播并显示翻页按钮。</summary>
    private bool _isPointerOver;

    /// <summary>主窗口是否处于可交互状态；隐藏或锁屏时由父控件置 false 以暂停轮播。</summary>
    private bool _isWindowActive = true;

    /// <summary>程序性更新 <see cref="PipsPager.SelectedPageIndex"/> 时抑制回调，避免与 <see cref="NavigateTo"/> 互相触发。</summary>
    private bool _suppressPipsCallback;

    /// <summary>进行中的切页 Storyboard；快速连续翻页时需先 Stop 再归位。</summary>
    private Storyboard? _runningStoryboard;

    /// <summary>动画结束后待从呈现区移除的旧图元素。</summary>
    private UIElement? _pendingOldElement;



    /// <summary>初始化控件、创建自动轮播定时器并订阅 Loaded / Unloaded。</summary>
    public BannerCarousel()
    {
        this.InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.IsRepeating = true;
        _timer.Tick += Timer_Tick;
        this.Loaded += BannerCarousel_Loaded;
        this.Unloaded += BannerCarousel_Unloaded;
    }



    /// <summary>
    /// 轮播图数据源，由 <see cref="GameBannerAndPost"/> 单向绑定传入。
    /// </summary>
    public List<GameBanner>? Banners
    {
        get => (List<GameBanner>?)GetValue(BannersProperty);
        set => SetValue(BannersProperty, value);
    }

    /// <summary><see cref="Banners"/> 依赖属性；值变化时触发 <see cref="BuildItems"/> 重建呈现区。</summary>
    public static readonly DependencyProperty BannersProperty =
        DependencyProperty.Register(nameof(Banners), typeof(List<GameBanner>), typeof(BannerCarousel), new PropertyMetadata(null, OnBannersChanged));

    /// <summary>
    /// <see cref="BannersProperty"/> 变更回调。
    /// </summary>
    /// <param name="d">目标 <see cref="BannerCarousel"/> 实例。</param>
    /// <param name="e">新旧值；新值为 null 或空列表时清空呈现区并隐藏指示器。</param>
    private static void OnBannersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BannerCarousel)d).BuildItems();
    }



    /// <summary>
    /// 暂停自动轮播（窗口隐藏 / 锁屏 / 内容不可见时调用）。
    /// </summary>
    public void PauseAutoPlay()
    {
        _isWindowActive = false;
        _timer.Stop();
    }

    /// <summary>
    /// 恢复自动轮播（窗口激活 / 内容重新可见时调用）。
    /// </summary>
    public void ResumeAutoPlay()
    {
        _isWindowActive = true;
        MaybeStartAutoPlay();
    }



    /// <summary>控件加载完成后尝试启动自动轮播（需满足窗口激活、无悬停、多张图等条件）。</summary>
    private void BannerCarousel_Loaded(object sender, RoutedEventArgs e)
    {
        MaybeStartAutoPlay();
    }


    /// <summary>卸载时停止定时器并收敛进行中的切页动画，避免 Storyboard 持有已卸载元素。</summary>
    private void BannerCarousel_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        FinalizeRunningTransition();
    }



    /// <summary>
    /// 根据 <see cref="Banners"/> 重建图片元素列表。一次性预创建所有 <see cref="CachedImage"/>，
    /// 切页时只在呈现区中移入 / 移出，避免每次翻页重新下载解码导致闪烁。
    /// </summary>
    private void BuildItems()
    {
        _timer.Stop();
        FinalizeRunningTransition();
        PresenterGrid.Children.Clear();
        foreach (CachedImage image in _imageElements)
        {
            image.Tapped -= Image_Tapped;
        }
        _imageElements.Clear();
        _currentIndex = -1;

        List<GameBanner>? banners = Banners;
        if (banners is null || banners.Count == 0)
        {
            BannerPipsPager.NumberOfPages = 0;
            PipsPagerBorder.Visibility = Visibility.Collapsed;
            UpdateNavButtonsState();
            return;
        }

        foreach (GameBanner banner in banners)
        {
            CachedImage image = new()
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsRightTapEnabled = false,
                DataContext = banner,
                RenderTransform = new TranslateTransform(),
            };
            PointerCursor.SetCursorShape(image, InputSystemCursorShape.Hand);
            if (!string.IsNullOrWhiteSpace(banner.Image?.Url))
            {
                image.Source = banner.Image.Url;
            }
            image.Tapped += Image_Tapped;
            _imageElements.Add(image);
        }

        BannerPipsPager.NumberOfPages = _imageElements.Count;
        PipsPagerBorder.Visibility = _imageElements.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavButtonsState();

        NavigateTo(0, animate: false);
        MaybeStartAutoPlay();
    }



    /// <summary>
    /// 切换到指定下标。下标自动按首尾循环取模；切页方向由新旧下标推算（含首↔尾的环绕方向），
    /// 使「最后一张 → 第一张」与普通前进方向一致，实现首尾连续播放。
    /// </summary>
    /// <param name="requestedIndex">目标下标，可为任意整数（内部取模）。</param>
    /// <param name="animate">为 true 且存在旧图且系统动画开启时执行推拉 Storyboard；否则直接切换。</param>
    private void NavigateTo(int requestedIndex, bool animate)
    {
        int count = _imageElements.Count;
        if (count == 0)
        {
            return;
        }

        int newIndex = ((requestedIndex % count) + count) % count;
        if (newIndex == _currentIndex)
        {
            return;
        }

        // 打断进行中的切页动画，把呈现区收敛到当前单张
        FinalizeRunningTransition();

        int oldIndex = _currentIndex;
        UIElement? oldElement = oldIndex >= 0 && oldIndex < count ? _imageElements[oldIndex] : null;
        CachedImage newElement = _imageElements[newIndex];

        _currentIndex = newIndex;
        SyncPipsSelection(newIndex);

        bool doAnimate = animate && oldElement is not null && EntranceAnimation.AnimationsEnabled();
        if (!doAnimate)
        {
            PresenterGrid.Children.Clear();
            SetTranslateX(newElement, 0);
            PresenterGrid.Children.Add(newElement);
            return;
        }

        double width = PresenterGrid.ActualWidth;
        if (width <= 0)
        {
            width = DefaultPresenterWidth;
        }
        int direction = ComputeDirection(oldIndex, newIndex, count);

        // 推拉：新图从 direction*width 滑到 0，旧图从 0 滑到 -direction*width，二者始终铺满视口，无背景缝隙
        SetTranslateX(newElement, direction * width);
        if (!PresenterGrid.Children.Contains(newElement))
        {
            PresenterGrid.Children.Add(newElement);
        }
        SetTranslateX(oldElement!, 0);

        Storyboard storyboard = new();
        storyboard.Children.Add(CreateSlideAnimation(GetTranslate(newElement), 0));
        storyboard.Children.Add(CreateSlideAnimation(GetTranslate(oldElement!), -direction * width));

        _runningStoryboard = storyboard;
        _pendingOldElement = oldElement;
        storyboard.Completed += Storyboard_Completed;
        storyboard.Begin();
    }



    /// <summary>切页 Storyboard 完成回调；必须经 <see cref="FinalizeRunningTransition"/> 释放 HoldEnd 锁定的变换值。</summary>
    private void Storyboard_Completed(object? sender, object e)
    {
        // Storyboard 默认 HoldEnd 会锁住变换值，必须 Stop 后再直接赋值才生效，否则复用的图片下次会卡在屏幕外
        FinalizeRunningTransition();
    }


    /// <summary>
    /// 立即结束进行中的切页动画：停止 Storyboard、移除待移除的旧图、把当前图归位到中心。
    /// 用于快速连续翻页或重建数据时收敛状态。
    /// </summary>
    private void FinalizeRunningTransition()
    {
        if (_runningStoryboard is not null)
        {
            _runningStoryboard.Completed -= Storyboard_Completed;
            _runningStoryboard.Stop();
            _runningStoryboard = null;
        }
        RemovePendingOldElement();
        SnapCurrentToCenter();
    }


    /// <summary>从呈现区移除动画结束后的旧图，并将其平移归零以便下次复用。</summary>
    private void RemovePendingOldElement()
    {
        if (_pendingOldElement is not null)
        {
            PresenterGrid.Children.Remove(_pendingOldElement);
            SetTranslateX(_pendingOldElement, 0);
            _pendingOldElement = null;
        }
    }


    /// <summary>将当前显示的图片平移归零，确保呈现区收敛到单槽中心状态。</summary>
    private void SnapCurrentToCenter()
    {
        if (_currentIndex >= 0 && _currentIndex < _imageElements.Count)
        {
            SetTranslateX(_imageElements[_currentIndex], 0);
        }
    }



    /// <summary>自动轮播定时器 Tick：前进到下一张（带动画）。</summary>
    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_imageElements.Count > 1)
        {
            NavigateTo(_currentIndex + 1, animate: true);
        }
    }


    /// <summary>上一张按钮点击：后退一页。</summary>
    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_currentIndex - 1, animate: true);
    }


    /// <summary>下一张按钮点击：前进一页。</summary>
    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_currentIndex + 1, animate: true);
    }


    /// <summary>圆点指示器选中项变化：跳转到对应页（用户点击圆点时触发，非程序性同步）。</summary>
    private void BannerPipsPager_SelectedIndexChanged(PipsPager sender, PipsPagerSelectedIndexChangedEventArgs args)
    {
        if (_suppressPipsCallback)
        {
            return;
        }
        NavigateTo(sender.SelectedPageIndex, animate: true);
    }


    /// <summary>点击 Banner 图片：在系统默认浏览器中打开 <see cref="GameBanner.Image"/> 配置的跳转链接。</summary>
    private async void Image_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is GameBanner banner && !string.IsNullOrWhiteSpace(banner.Image?.Link))
            {
                await Launcher.LaunchUriAsync(new Uri(banner.Image.Link));
            }
        }
        catch { }
    }


    /// <summary>指针进入：暂停自动轮播，切换到 PointerOver 视觉状态以淡入翻页按钮。</summary>
    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        _timer.Stop();
        VisualStateManager.GoToState(this, "PointerOver", true);
    }


    /// <summary>指针离开：恢复 Normal 视觉状态，并在条件允许时重启自动轮播。</summary>
    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        VisualStateManager.GoToState(this, "Normal", true);
        MaybeStartAutoPlay();
    }


    /// <summary>
    /// 鼠标滚轮在轮播图上滚动也能翻页（向下 = 下一张，向上 = 上一张）。
    /// </summary>
    private void RootGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_imageElements.Count <= 1)
        {
            return;
        }
        PointerPoint point = e.GetCurrentPoint((UIElement)sender);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }
        NavigateTo(_currentIndex + (delta < 0 ? 1 : -1), animate: true);
        // 标记已处理，避免滚轮事件冒泡到祖先 ScrollViewer 同时滚动页面
        e.Handled = true;
    }


    /// <summary>呈现区尺寸变化时更新裁剪矩形，隐藏切页动画滑出视口的部分。</summary>
    private void PresenterGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 圆角由外层 Border 负责，此处仅做矩形裁剪
        PresenterGrid.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }



    /// <summary>
    /// 在已加载、窗口激活、无指针悬停且多于一张图时启动自动轮播；否则停止定时器。
    /// </summary>
    private void MaybeStartAutoPlay()
    {
        if (IsLoaded && _isWindowActive && !_isPointerOver && _imageElements.Count > 1)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }


    /// <summary>将 <see cref="PipsPager"/> 选中项与当前下标同步，期间抑制 <see cref="BannerPipsPager_SelectedIndexChanged"/> 回调。</summary>
    /// <param name="index">当前页下标。</param>
    private void SyncPipsSelection(int index)
    {
        _suppressPipsCallback = true;
        if (index >= 0 && index < BannerPipsPager.NumberOfPages)
        {
            BannerPipsPager.SelectedPageIndex = index;
        }
        _suppressPipsCallback = false;
    }


    /// <summary>仅多张图时显示左右翻页按钮；单张或空列表时折叠。</summary>
    private void UpdateNavButtonsState()
    {
        Visibility visibility = _imageElements.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviousButtonRoot.Visibility = visibility;
        NextButtonRoot.Visibility = visibility;
    }



    /// <summary>
    /// 根据新旧下标推算切页方向（+1 新图从右滑入，-1 从左滑入）。
    /// 首尾环绕时「最后→第一」视为前进，「第一→最后」视为后退，保证方向与自动轮播一致。
    /// </summary>
    /// <param name="oldIndex">切换前的下标。</param>
    /// <param name="newIndex">切换后的下标。</param>
    /// <param name="count">图片总数。</param>
    /// <returns>+1 或 -1，作为推拉位移的符号。</returns>
    private static int ComputeDirection(int oldIndex, int newIndex, int count)
    {
        bool isBackward = (newIndex < oldIndex && !(newIndex == 0 && oldIndex == count - 1))
                          || (newIndex == count - 1 && oldIndex == 0);
        return isBackward ? -1 : 1;
    }


    /// <summary>创建沿 X 轴平移的切页动画。</summary>
    /// <param name="target">目标 <see cref="TranslateTransform"/>。</param>
    /// <param name="to">动画终点 X 值。</param>
    /// <returns>已绑定目标与属性的 <see cref="DoubleAnimation"/>。</returns>
    private static DoubleAnimationUsingKeyFrames CreateSlideAnimation(TranslateTransform target, double to)
    {
        // https://easings.net/#easeInQuint
        KeySpline customEase = new()
        {
            ControlPoint1 = new Point(0.64, 0),
            ControlPoint2 = new Point(0.78, 1),
        };

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(SlideDurationMs)),
        };

        // 关键帧，KeyTime 就是动画总时长，Value 就是目标位置
        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(SlideDurationMs)),
            Value = to,
            KeySpline = customEase   
        });

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "X");
        return animation;
    }


    /// <summary>获取元素的 <see cref="TranslateTransform"/>；不存在时创建并挂到 <see cref="UIElement.RenderTransform"/>。</summary>
    /// <param name="element">目标 UI 元素。</param>
    /// <returns>可用于读写 X 的变换对象。</returns>
    private static TranslateTransform GetTranslate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }
        TranslateTransform created = new();
        element.RenderTransform = created;
        return created;
    }


    /// <summary>设置元素水平平移量。</summary>
    /// <param name="element">目标 UI 元素。</param>
    /// <param name="x">TranslateTransform.X 值。</param>
    private static void SetTranslateX(UIElement element, double x)
    {
        GetTranslate(element).X = x;
    }


}
