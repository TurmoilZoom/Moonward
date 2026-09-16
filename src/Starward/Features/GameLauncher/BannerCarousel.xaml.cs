using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using Starward.Core.HoYoPlay;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 软件首页的游戏轮播图控件，PanelSlideshow：
/// <list type="bullet">
/// <item>单槽呈现：呈现区始终只放当前一张图，切页时新旧两张各自做合成线程平移推拉，首尾连续无回滚。</item>
/// <item>可点击的 <see cref="PipsPager"/> 圆点指示器（替代原右下角「页数/总数」文字）。</item>
/// <item>悬停时淡入并放大的左右翻页按钮（VisualState Storyboard 动画）。</item>
/// <item>5 秒自动轮播，鼠标悬停或窗口隐藏时暂停。</item>
/// <item>滚轮/按钮/自动轮播共用同一过渡驱动；过渡中忽略所有翻页输入，不打断进行中的动画。</item>
/// </list>
/// </summary>
public sealed partial class BannerCarousel : UserControl
{

    /// <summary>切页动画时长（毫秒）。</summary>
    private const double SlideDurationMs = 500;

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

    /// <summary>是否正在进行切页过渡（合成线程 Composition 驱动）。</summary>
    private bool _transitionActive;

    /// <summary>过渡起点下标（A 图）。</summary>
    private int _fromIndex;

    /// <summary>过渡终点下标（B 图）。</summary>
    private int _toIndex;

    /// <summary>推拉位移符号（+1 新图从右滑入，-1 从左滑入），由 <see cref="ComputeDirection"/> 推算。</summary>
    private int _direction;

    /// <summary>过渡代数；每次启动 / 取消 / 完成递增，用于让旧的 <see cref="CompositionScopedBatch.Completed"/> 回调失效。</summary>
    private int _generation;

    /// <summary>是否已挂接 <see cref="HookHandlers"/> 中的全部事件。</summary>
    private bool _handlersHooked;



    /// <summary>初始化控件、创建自动轮播定时器并订阅 Loaded / Unloaded。</summary>
    public BannerCarousel()
    {
        this.InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.IsRepeating = true;
        this.Loaded += BannerCarousel_Loaded;
        this.Unloaded += BannerCarousel_Unloaded;
        // 首次布局会先于 Loaded 触发 PresenterGrid.SizeChanged，故构造期就挂上
        HookHandlers();
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



    /// <summary>控件加载完成后重新挂接事件并尝试启动自动轮播（需满足窗口激活、无悬停、多张图等条件）。</summary>
    private void BannerCarousel_Loaded(object sender, RoutedEventArgs e)
    {
        HookHandlers();
        MaybeStartAutoPlay();
    }


    /// <summary>卸载时停止定时器、取消渲染订阅并退订全部事件，避免回调持有已卸载元素。</summary>
    private void BannerCarousel_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        CancelTransition();
        UnhookHandlers();
        // 不带过渡地回到 Normal：让翻页按钮的悬停 Storyboard 立刻结束，
        // 否则停在 HoldEnd 的动画会由本机计时管理器继续持有目标元素。
        VisualStateManager.GoToState(this, "Normal", false);
    }


    /// <summary>
    /// 成对挂接本控件用到的全部事件。
    /// 这些事件原先写在 XAML 里（<c>Click=</c> / <c>SizeChanged=</c> 等），声明式订阅没有退订入口，
    /// 卸载后仍留着一份本机侧注册，实测会让整棵控件树留在内存里（切一次游戏漏一个实例）。
    /// </summary>
    private void HookHandlers()
    {
        if (_handlersHooked)
        {
            return;
        }
        _handlersHooked = true;
        RootGrid.PointerEntered += RootGrid_PointerEntered;
        RootGrid.PointerExited += RootGrid_PointerExited;
        RootGrid.PointerWheelChanged += RootGrid_PointerWheelChanged;
        PresenterGrid.SizeChanged += PresenterGrid_SizeChanged;
        PreviousButton.Click += PreviousButton_Click;
        NextButton.Click += NextButton_Click;
        BannerPipsPager.SelectedIndexChanged += BannerPipsPager_SelectedIndexChanged;
        _timer.Tick += Timer_Tick;
    }


    /// <summary>退订 <see cref="HookHandlers"/> 挂接的全部事件；与之严格成对。</summary>
    private void UnhookHandlers()
    {
        if (!_handlersHooked)
        {
            return;
        }
        _handlersHooked = false;
        RootGrid.PointerEntered -= RootGrid_PointerEntered;
        RootGrid.PointerExited -= RootGrid_PointerExited;
        RootGrid.PointerWheelChanged -= RootGrid_PointerWheelChanged;
        PresenterGrid.SizeChanged -= PresenterGrid_SizeChanged;
        PreviousButton.Click -= PreviousButton_Click;
        NextButton.Click -= NextButton_Click;
        BannerPipsPager.SelectedIndexChanged -= BannerPipsPager_SelectedIndexChanged;
        _timer.Tick -= Timer_Tick;
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
    /// 请求前进一步（+1）或后退一步（-1）。过渡进行中忽略所有翻页输入，不打断动画。
    /// </summary>
    /// <param name="delta">+1 下一张，-1 上一张。</param>
    private void RequestStep(int delta)
    {
        int count = _imageElements.Count;
        if (count <= 1 || delta is not (1 or -1) || _transitionActive)
        {
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
        // 位移只在启动时取一次：两段合成动画的终点已提交给合成线程，过渡中改尺寸不会再重定目标
        float width = (float)GetPresenterWidth();
        _transitionActive = true;

        CachedImage fromElement = _imageElements[from];
        CachedImage toElement = _imageElements[to];

        // 先挂 B 图并在动画启动前就把它放到屏幕外，避免首帧布局 / 栅格化造成跳变
        PresenterGrid.Children.Clear();
        PresenterGrid.Children.Add(fromElement);
        SetTranslation(fromElement, 0);
        PresenterGrid.Children.Add(toElement);
        SetTranslation(toElement, _direction * width);

        StartSlidePair(-_direction * width, 0);
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
            CompleteTransition(_toIndex);
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



    /// <summary>
    /// 启动一组合成线程位移动画：A、B 两张图各自滑向目标 X，并以 <see cref="CompositionScopedBatch"/>
    /// 统一收尾。旧的批次 Completed 回调靠 <see cref="_generation"/> 判废。
    /// </summary>
    /// <param name="fromTargetX">A 图目标位移。</param>
    /// <param name="toTargetX">B 图目标位移。</param>
    private void StartSlidePair(float fromTargetX, float toTargetX)
    {
        Visual fromVisual = GetVisual(_imageElements[_fromIndex]);
        Visual toVisual = GetVisual(_imageElements[_toIndex]);
        Compositor compositor = fromVisual.Compositor;
        TimeSpan duration = TimeSpan.FromMilliseconds(SlideDurationMs);

        Vector3KeyFrameAnimation fromSlide = CreateSlideAnimation(compositor, fromTargetX, duration);
        Vector3KeyFrameAnimation toSlide = CreateSlideAnimation(compositor, toTargetX, duration);

        int myGeneration = ++_generation;

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        fromVisual.StartAnimation("Translation", fromSlide);
        toVisual.StartAnimation("Translation", toSlide);
        batch.End();

        batch.Completed += (_, _) =>
        {
            if (!_transitionActive || _generation != myGeneration)
            {
                return;
            }
            CompleteTransition(_toIndex);
        };
    }


    /// <summary>
    /// 创建「从当前视觉位置滑到目标 X」的减速位移动画；关键帧 0 用 <c>this.StartingValue</c> 表达式，
    /// 保证动画从提交前的预置位置起步，不必在 UI 线程逐帧插值。
    /// </summary>
    /// <param name="compositor">合成器。</param>
    /// <param name="targetX">目标水平位移（像素）。</param>
    /// <param name="duration">动画时长。</param>
    private static Vector3KeyFrameAnimation CreateSlideAnimation(Compositor compositor, float targetX, TimeSpan duration)
    {
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        animation.InsertKeyFrame(1f, new Vector3(targetX, 0, 0), ease);
        animation.Duration = duration;
        return animation;
    }


    /// <summary>过渡完成：收敛到 <paramref name="finalIndex"/> 单槽中心，移除另一张图。</summary>
    /// <param name="finalIndex">最终停留的下标。</param>
    private void CompleteTransition(int finalIndex)
    {
        _transitionActive = false;
        // 使仍在途的批次 Completed 回调失效
        _generation++;

        int otherIndex = finalIndex == _toIndex ? _fromIndex : _toIndex;
        if (otherIndex >= 0 && otherIndex < _imageElements.Count)
        {
            CachedImage other = _imageElements[otherIndex];
            PresenterGrid.Children.Remove(other);
            ResetTranslation(other);
        }

        _currentIndex = finalIndex;
        SyncPipsSelection(finalIndex);

        CachedImage current = _imageElements[finalIndex];
        if (!PresenterGrid.Children.Contains(current))
        {
            PresenterGrid.Children.Clear();
            PresenterGrid.Children.Add(current);
        }
        ResetTranslation(current);
    }


    /// <summary>取消进行中的过渡并停掉合成动画（不更新 <see cref="_currentIndex"/>）。</summary>
    private void CancelTransition()
    {
        if (!_transitionActive)
        {
            return;
        }
        _transitionActive = false;
        _generation++;

        foreach (UIElement child in PresenterGrid.Children)
        {
            ResetTranslation(child);
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
        ResetTranslation(element);
        PresenterGrid.Children.Add(element);
        _currentIndex = index;
        SyncPipsSelection(index);
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


    /// <summary>
    /// 取得元素的 Composition 视觉并启用 Translation。启用后即可用 <c>Translation</c> 属性做合成线程位移动画。
    /// </summary>
    /// <param name="element">目标 UI 元素。</param>
    private static Visual GetVisual(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        return ElementCompositionPreview.GetElementVisual(element);
    }


    /// <summary>设置元素水平平移量（不带动画，直接落到合成视觉上）。</summary>
    /// <param name="element">目标 UI 元素。</param>
    /// <param name="x">目标水平位移（像素）。</param>
    private static void SetTranslation(UIElement element, float x)
    {
        GetVisual(element).Properties.InsertVector3("Translation", new Vector3(x, 0, 0));
    }


    /// <summary>停止元素的位移动画并把水平平移复位为 0。</summary>
    /// <param name="element">目标 UI 元素。</param>
    private static void ResetTranslation(UIElement element)
    {
        Visual visual = GetVisual(element);
        try
        {
            // 未启用 Translation 时 StopAnimation("Translation") 会抛 E_INVALIDARG，先启用再停
            visual.StopAnimation("Translation");
        }
        catch { }
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }


}