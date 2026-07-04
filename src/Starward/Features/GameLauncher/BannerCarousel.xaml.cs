using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
/// <item>单槽呈现：呈现区始终只放当前一张图，切页时新旧两张各自做平移推拉（逐帧插值），首尾连续无回滚。</item>
/// <item>可点击的 <see cref="PipsPager"/> 圆点指示器（替代原右下角「页数/总数」文字）。</item>
/// <item>悬停时淡入并放大的左右翻页按钮（VisualState Storyboard 动画）。</item>
/// <item>5 秒自动轮播，鼠标悬停或窗口隐藏时暂停。</item>
/// <item>滚轮/按钮/自动轮播共用同一过渡驱动；过渡中同向输入忽略，反向输入从当前视觉位置无缝反转。</item>
/// </list>
/// </summary>
public sealed partial class BannerCarousel : UserControl
{

    /// <summary>切页动画时长（毫秒）。</summary>
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

    /// <summary>是否正在进行切页过渡（逐帧插值驱动）。</summary>
    private bool _transitionActive;

    /// <summary>过渡起点下标（A 图）。</summary>
    private int _fromIndex;

    /// <summary>过渡终点下标（B 图）。</summary>
    private int _toIndex;

    /// <summary>推拉位移符号（+1 新图从右滑入，-1 从左滑入），由 <see cref="ComputeDirection"/> 推算。</summary>
    private int _direction;

    /// <summary>过渡进度 p∈[0,1]；0 停在 A，1 停在 B。</summary>
    private double _progress;

    /// <summary>过渡目标：1 朝 B 推进，0 朝 A 回退。</summary>
    private int _target = 1;

    /// <summary>当前过渡的逻辑朝向（+1 前进，-1 后退），用于判断同向输入是否应忽略。</summary>
    private int _scrollDir = 1;

    /// <summary>呈现区宽度缓存，过渡期间用于计算位移。</summary>
    private double _width;

    /// <summary>上一帧 <see cref="CompositionTarget.Rendering"/> 时间戳，用于计算 dt。</summary>
    private TimeSpan _lastRenderTime;

    /// <summary>是否已订阅 <see cref="CompositionTarget.Rendering"/>。</summary>
    private bool _renderingHooked;



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


    /// <summary>卸载时停止定时器并取消渲染订阅，避免回调持有已卸载元素。</summary>
    private void BannerCarousel_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        CancelTransition();
    }



    /// <summary>
    /// 根据 <see cref="Banners"/> 重建图片元素列表。一次性预创建所有 <see cref="CachedImage"/>，
    /// 切页时只在呈现区中移入 / 移出，避免每次翻页重新下载解码导致闪烁。
    /// </summary>
    private void BuildItems()
    {
        _timer.Stop();
        CancelTransition();
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
    /// 请求前进一步（+1）或后退一步（-1）。过渡中同向输入忽略；反向输入翻转 target 从当前视觉位置无缝反转。
    /// </summary>
    /// <param name="delta">+1 下一张，-1 上一张。</param>
    private void RequestStep(int delta)
    {
        int count = _imageElements.Count;
        if (count <= 1 || delta is not (1 or -1))
        {
            return;
        }

        if (_transitionActive)
        {
            // 当前逻辑朝向：target=1 朝 B（_scrollDir），target=0 朝 A（-_scrollDir）
            int logicalDir = _target == 1 ? _scrollDir : -_scrollDir;
            if (delta == logicalDir)
            {
                return;
            }
            _target = _target == 1 ? 0 : 1;
            return;
        }

        int toIndex = ((_currentIndex + delta) % count + count) % count;
        BeginTransition(_currentIndex, toIndex);
    }


    /// <summary>
    /// 开始从 <paramref name="from"/> 到 <paramref name="to"/> 的切页过渡；系统动画关闭时直接瞬切。
    /// </summary>
    /// <param name="from">起点下标。</param>
    /// <param name="to">终点下标。</param>
    private void BeginTransition(int from, int to)
    {
        int count = _imageElements.Count;
        if (count == 0 || from < 0 || from >= count || to < 0 || to >= count || from == to)
        {
            return;
        }

        if (!EntranceAnimation.AnimationsEnabled())
        {
            SnapToIndex(to);
            return;
        }

        _fromIndex = from;
        _toIndex = to;
        _direction = ComputeDirection(from, to, count);
        _scrollDir = _direction;
        _progress = 0;
        _target = 1;
        _width = GetPresenterWidth();
        _transitionActive = true;

        CachedImage fromElement = _imageElements[from];
        CachedImage toElement = _imageElements[to];

        PresenterGrid.Children.Clear();
        PresenterGrid.Children.Add(fromElement);
        PresenterGrid.Children.Add(toElement);
        ApplyTransitionPositions(_progress);
        HookRendering();
    }


    /// <summary>
    /// 切换到指定下标。下标自动按首尾循环取模；<paramref name="animate"/> 为 false 时瞬切，
    /// 为 true 时启动推拉过渡（圆点跳转等场景）。
    /// </summary>
    /// <param name="requestedIndex">目标下标，可为任意整数（内部取模）。</param>
    /// <param name="animate">为 true 且系统动画开启时执行推拉过渡；否则直接切换。</param>
    private void NavigateTo(int requestedIndex, bool animate)
    {
        int count = _imageElements.Count;
        if (count == 0)
        {
            return;
        }

        int newIndex = ((requestedIndex % count) + count) % count;
        if (newIndex == _currentIndex && !_transitionActive)
        {
            return;
        }

        if (_transitionActive)
        {
            CompleteTransition(_target == 1 ? _toIndex : _fromIndex);
            if (newIndex == _currentIndex)
            {
                return;
            }
        }

        if (!animate || _currentIndex < 0 || !EntranceAnimation.AnimationsEnabled())
        {
            SnapToIndex(newIndex);
            return;
        }

        BeginTransition(_currentIndex, newIndex);
    }



    /// <summary>订阅每帧渲染回调，推进切页过渡进度。</summary>
    private void HookRendering()
    {
        if (_renderingHooked)
        {
            return;
        }
        _renderingHooked = true;
        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }


    /// <summary>取消每帧渲染回调。</summary>
    private void UnhookRendering()
    {
        if (!_renderingHooked)
        {
            return;
        }
        _renderingHooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }


    /// <summary>
    /// 每帧按 dt 推进过渡进度；到达 0 或 1 时完成过渡。
    /// </summary>
    private void OnRendering(object? sender, object e)
    {
        if (!_transitionActive)
        {
            return;
        }

        double dt = 1.0 / 60;
        if (e is RenderingEventArgs args)
        {
            if (_lastRenderTime > TimeSpan.Zero)
            {
                dt = (args.RenderingTime - _lastRenderTime).TotalSeconds;
            }
            _lastRenderTime = args.RenderingTime;
        }
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60;
        }

        double dp = dt / (SlideDurationMs / 1000.0);
        if (_target == 1)
        {
            _progress += dp;
        }
        else
        {
            _progress -= dp;
        }

        ApplyTransitionPositions(_progress);

        if (_progress >= 1)
        {
            CompleteTransition(_toIndex);
        }
        else if (_progress <= 0)
        {
            CompleteTransition(_fromIndex);
        }
    }


    /// <summary>过渡完成：收敛到 <paramref name="finalIndex"/> 单槽中心，移除另一张图。</summary>
    /// <param name="finalIndex">最终停留的下标。</param>
    private void CompleteTransition(int finalIndex)
    {
        UnhookRendering();
        _transitionActive = false;

        int otherIndex = finalIndex == _toIndex ? _fromIndex : _toIndex;
        if (otherIndex >= 0 && otherIndex < _imageElements.Count)
        {
            CachedImage other = _imageElements[otherIndex];
            PresenterGrid.Children.Remove(other);
            SetTranslateX(other, 0);
        }

        _currentIndex = finalIndex;
        SyncPipsSelection(finalIndex);

        CachedImage current = _imageElements[finalIndex];
        if (!PresenterGrid.Children.Contains(current))
        {
            PresenterGrid.Children.Clear();
            PresenterGrid.Children.Add(current);
        }
        SetTranslateX(current, 0);
    }


    /// <summary>取消进行中的过渡并卸载渲染订阅（不更新 <see cref="_currentIndex"/>）。</summary>
    private void CancelTransition()
    {
        UnhookRendering();
        if (!_transitionActive)
        {
            return;
        }
        _transitionActive = false;

        foreach (UIElement child in PresenterGrid.Children)
        {
            SetTranslateX(child, 0);
        }
    }


    /// <summary>瞬切到指定下标，呈现区只保留该图。</summary>
    /// <param name="index">目标下标。</param>
    private void SnapToIndex(int index)
    {
        CancelTransition();
        if (index < 0 || index >= _imageElements.Count)
        {
            return;
        }

        CachedImage element = _imageElements[index];
        PresenterGrid.Children.Clear();
        SetTranslateX(element, 0);
        PresenterGrid.Children.Add(element);
        _currentIndex = index;
        SyncPipsSelection(index);
    }


    /// <summary>按当前进度 p 更新 A/B 两张图的水平位移。</summary>
    /// <param name="p">过渡进度，0 为起点 A，1 为终点 B。</param>
    private void ApplyTransitionPositions(double p)
    {
        double eased = Ease(Math.Clamp(p, 0, 1));
        CachedImage fromElement = _imageElements[_fromIndex];
        CachedImage toElement = _imageElements[_toIndex];
        // B 从 d*w 外滑到 0，A 从 0 滑到 -d*w 外
        SetTranslateX(toElement, _direction * _width * (1 - eased));
        SetTranslateX(fromElement, -_direction * _width * eased);
    }


    /// <summary>smootherstep 缓动：反转时位置连续，速度在反转瞬间反号。</summary>
    /// <param name="t">归一化进度，期望在 [0,1]。</param>
    /// <returns>缓动后的 [0,1] 值。</returns>
    private static double Ease(double t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }


    /// <summary>获取呈现区宽度；尚未量测完成时回退到默认值。</summary>
    private double GetPresenterWidth()
    {
        double width = PresenterGrid.ActualWidth;
        return width > 0 ? width : DefaultPresenterWidth;
    }



    /// <summary>自动轮播定时器 Tick：前进到下一张。</summary>
    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_imageElements.Count > 1)
        {
            RequestStep(+1);
        }
    }


    /// <summary>上一张按钮点击：后退一页。</summary>
    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        RequestStep(-1);
    }


    /// <summary>下一张按钮点击：前进一页。</summary>
    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        RequestStep(+1);
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
        RequestStep(delta < 0 ? +1 : -1);
        // 标记已处理，避免滚轮事件冒泡到祖先 ScrollViewer 同时滚动页面
        e.Handled = true;
    }


    /// <summary>呈现区尺寸变化时更新裁剪矩形，隐藏切页动画滑出视口的部分。</summary>
    private void PresenterGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 圆角由外层 Border 负责，此处仅做矩形裁剪
        PresenterGrid.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        if (_transitionActive)
        {
            _width = e.NewSize.Width > 0 ? e.NewSize.Width : DefaultPresenterWidth;
            ApplyTransitionPositions(_progress);
        }
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