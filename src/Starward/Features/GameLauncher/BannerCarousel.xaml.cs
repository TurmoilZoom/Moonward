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

    private const double SlideDurationMs = 350;

    private const double DefaultPresenterWidth = 380;


    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

    private readonly List<CachedImage> _imageElements = new();

    private int _currentIndex = -1;

    private bool _isPointerOver;

    private bool _isWindowActive = true;

    private bool _suppressPipsCallback;

    private Storyboard? _runningStoryboard;

    private UIElement? _pendingOldElement;



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

    public static readonly DependencyProperty BannersProperty =
        DependencyProperty.Register(nameof(Banners), typeof(List<GameBanner>), typeof(BannerCarousel), new PropertyMetadata(null, OnBannersChanged));

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



    private void BannerCarousel_Loaded(object sender, RoutedEventArgs e)
    {
        MaybeStartAutoPlay();
    }


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



    private void Storyboard_Completed(object? sender, object e)
    {
        // 走 FinalizeRunningTransition 的「Stop + 归位」逻辑：Storyboard 默认 HoldEnd 会锁住
        // 变换值，必须 Stop 后再直接赋值才生效，否则复用的图片下次会卡在屏幕外。
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


    private void RemovePendingOldElement()
    {
        if (_pendingOldElement is not null)
        {
            PresenterGrid.Children.Remove(_pendingOldElement);
            SetTranslateX(_pendingOldElement, 0);
            _pendingOldElement = null;
        }
    }


    private void SnapCurrentToCenter()
    {
        if (_currentIndex >= 0 && _currentIndex < _imageElements.Count)
        {
            SetTranslateX(_imageElements[_currentIndex], 0);
        }
    }



    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_imageElements.Count > 1)
        {
            NavigateTo(_currentIndex + 1, animate: true);
        }
    }


    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_currentIndex - 1, animate: true);
    }


    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_currentIndex + 1, animate: true);
    }


    private void BannerPipsPager_SelectedIndexChanged(PipsPager sender, PipsPagerSelectedIndexChangedEventArgs args)
    {
        if (_suppressPipsCallback)
        {
            return;
        }
        NavigateTo(sender.SelectedPageIndex, animate: true);
    }


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


    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        _timer.Stop();
        VisualStateManager.GoToState(this, "PointerOver", true);
    }


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


    private void PresenterGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 裁剪掉切页时滑出视口的部分（圆角由外层 Border 负责）
        PresenterGrid.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }



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


    private void SyncPipsSelection(int index)
    {
        _suppressPipsCallback = true;
        if (index >= 0 && index < BannerPipsPager.NumberOfPages)
        {
            BannerPipsPager.SelectedPageIndex = index;
        }
        _suppressPipsCallback = false;
    }


    private void UpdateNavButtonsState()
    {
        Visibility visibility = _imageElements.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviousButtonRoot.Visibility = visibility;
        NextButtonRoot.Visibility = visibility;
    }



    private static int ComputeDirection(int oldIndex, int newIndex, int count)
    {
        bool isBackward = (newIndex < oldIndex && !(newIndex == 0 && oldIndex == count - 1))
                          || (newIndex == count - 1 && oldIndex == 0);
        return isBackward ? -1 : 1;
    }


    private static DoubleAnimation CreateSlideAnimation(TranslateTransform target, double to)
    {
        DoubleAnimation animation = new()
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(SlideDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "X");
        return animation;
    }


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


    private static void SetTranslateX(UIElement element, double x)
    {
        GetTranslate(element).X = x;
    }


}
