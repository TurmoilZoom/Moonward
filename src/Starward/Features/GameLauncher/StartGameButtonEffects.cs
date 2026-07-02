using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Features.ViewHost;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 为「开始游戏」按钮提供 Composition 动效（呼吸光晕 / 流光 / 聚光灯 / 点击光爆）。
/// <list type="bullet">
/// <item>呼吸光晕：胶囊外圈强调色辉光，<see cref="DropShadow.BlurRadius"/> 与 <see cref="DropShadow.Opacity"/> 循环变化（仅 <see cref="GameState.StartGame"/>）。</item>
/// <item>流光扫过：斜向高光周期性从左扫到右（所有强调色 CTA 状态）。</item>
/// <item>指针跟随聚光灯：悬停时柔光跟随鼠标（<see cref="CompositionRadialGradientBrush"/>）。</item>
/// <item>点击光爆：按下瞬间从点击点扩散光波并淡出。</item>
/// </list>
///
/// 设计要点（与 <see cref="Starward.Controls.FluidNavigationViewHoverEffect"/> 保持一致）：
/// - 纯 Microsoft.UI.Composition + Win2D（圆角裁剪），Attach/Detach 生命周期，惰性创建视觉对象；
/// - 循环动画在窗口最小化 / 隐藏 / 锁屏时暂停（订阅 <see cref="MainWindowStateChangedMessage"/>），避免空转耗电。
///
/// 用法：
/// 1. 在 StartGameButton.xaml 中放置两个 IsHitTestVisible="False" 的宿主 Grid：
///    Grid_GlowHost（位于胶囊 <c>Grid_Root</c> 之外、之下，承载会向外溢出的辉光，不能被圆角裁剪）；
///    Grid_EffectHost（位于强调色背景之上、文字之下，承载会被胶囊裁剪的流光 / 聚光 / 光爆）。
/// 2. Loaded 调用 <see cref="Attach"/>，Unloaded 调用 <see cref="Detach"/>。
/// 3. GameState / 可用状态变化时调用 <see cref="SetState"/>(glowActive, ctaActive)：
///    glowActive = GameState is GameState.StartGame；ctaActive = 是否显示强调色背景（开始 / 安装 / 更新 等）。
/// 4. 主题变化时调用 <see cref="OnThemeChanged"/>。
/// </summary>
public sealed class StartGameButtonEffects
{

    /// <summary>胶囊圆角半径，与 StartGameButton 的 CornerRadius 一致。</summary>
    private const float CornerRadius = 22f;

    /// <summary>呼吸光晕模糊半径下限（像素）。</summary>
    private const float GlowBlurMin = 10f;

    /// <summary>呼吸光晕模糊半径上限（像素）。</summary>
    private const float GlowBlurMax = 24f;

    /// <summary>呼吸光晕不透明度下限。</summary>
    private const float GlowOpacityMin = 0.35f;

    /// <summary>呼吸光晕不透明度上限。</summary>
    private const float GlowOpacityMax = 0.85f;

    /// <summary>一次呼吸（明→暗→明）的周期。</summary>
    private static readonly TimeSpan GlowPeriod = TimeSpan.FromSeconds(2.8);

    /// <summary>流光高光带宽度（像素）。</summary>
    private const float ShineBandWidth = 56f;

    /// <summary>流光倾斜角度（度），负值向左上倾斜，扫出斜向高光。</summary>
    private const float ShineTiltDegrees = -20f;

    /// <summary>流光一次完整循环时长（含扫掠后的停顿）。</summary>
    private static readonly TimeSpan ShineCycle = TimeSpan.FromSeconds(3.6);

    /// <summary>扫掠动作占整个循环的比例，其余时间为停顿。</summary>
    private const float ShineSweepRatio = 0.26f;

    /// <summary>聚光灯半径（像素）。</summary>
    private const float SpotlightRadius = 76f;

    /// <summary>点击光爆基准直径（像素）。</summary>
    private const float RippleBaseSize = 28f;

    /// <summary>点击光爆动画时长。</summary>
    private static readonly TimeSpan RippleDuration = TimeSpan.FromMilliseconds(560);


    /// <summary>从胶囊根元素取得的 Composition 合成器。</summary>
    private Compositor? _compositor;

    /// <summary>胶囊根 Grid（<c>Grid_Root</c>），用于量尺寸与指针事件。</summary>
    private Grid? _root;

    /// <summary>呼吸光晕宿主（<c>Grid_GlowHost</c>），位于胶囊外、不裁剪溢出辉光。</summary>
    private Grid? _glowHost;

    /// <summary>胶囊内动效宿主（<c>Grid_EffectHost</c>），承载流光 / 聚光 / 光爆。</summary>
    private Grid? _effectHost;

    /// <summary>主操作按钮，用于监听按下以触发点击光爆。</summary>
    private Button? _actionButton;

    /// <summary>缓出贝塞尔，用于流光扫掠与光爆扩散。</summary>
    private CompositionEasingFunction? _easeOut;

    /// <summary>缓入缓出贝塞尔，用于呼吸光晕循环。</summary>
    private CompositionEasingFunction? _easeInOut;

    /// <summary>承载 <see cref="DropShadow"/> 的精灵视觉，挂于 <see cref="_glowHost"/>。</summary>
    private SpriteVisual? _glowVisual;

    /// <summary>圆角胶囊形强调色辉光阴影。</summary>
    private DropShadow? _glowShadow;

    /// <summary>离屏圆角矩形，作为阴影遮罩的绘制来源。</summary>
    private ShapeVisual? _glowMaskSource;

    /// <summary>阴影遮罩的圆角矩形几何。</summary>
    private CompositionRoundedRectangleGeometry? _glowMaskGeometry;

    /// <summary>将 <see cref="_glowMaskSource"/> 光栅化为 <see cref="CompositionSurfaceBrush"/> 的中间表面。</summary>
    private CompositionVisualSurface? _glowSurface;

    /// <summary>流光 / 聚光 / 光爆的共用容器，带胶囊圆角裁剪。</summary>
    private ContainerVisual? _overlayRoot;

    /// <summary>Win2D 生成的圆角路径，用于 <see cref="_overlayRoot"/> 的几何裁剪。</summary>
    private CompositionPathGeometry? _clipGeometry;

    /// <summary>斜向线性渐变高光带，周期性横向扫掠。</summary>
    private SpriteVisual? _shineVisual;

    /// <summary>流光高光带的线性渐变画刷（两端透明、中间亮）。</summary>
    private CompositionLinearGradientBrush? _shineBrush;

    /// <summary>指针跟随的径向渐变柔光层。</summary>
    private SpriteVisual? _spotlightVisual;

    /// <summary>聚光灯径向渐变画刷，<see cref="CompositionRadialGradientBrush.EllipseCenter"/> 随指针更新。</summary>
    private CompositionRadialGradientBrush? _spotlightBrush;

    /// <summary>胶囊当前宽度（像素），与 <see cref="_root"/> 同步。</summary>
    private float _width;

    /// <summary>胶囊当前高度（像素），与 <see cref="_root"/> 同步。</summary>
    private float _height;

    /// <summary>是否已通过 <see cref="Attach"/> 挂接且尚未 <see cref="Detach"/>。</summary>
    private bool _attached;

    /// <summary>是否启用呼吸光晕：仅「可开始游戏」(GameState.StartGame) 状态，作为“游戏就绪”的专属信号。</summary>
    private bool _glowActive;

    /// <summary>是否启用流光 / 聚光灯 / 点击光爆：所有显示强调色背景的可操作状态（开始 / 安装 / 更新 等）。</summary>
    private bool _ctaActive;

    /// <summary>主窗口当前是否可见（最小化 / 隐藏 / 锁屏时为 false）。</summary>
    private bool _windowVisible = true;

    /// <summary>指针是否位于胶囊范围内。</summary>
    private bool _pointerInside;


    /// <summary>
    /// 挂接动效宿主与事件：惰性构建 Composition 视觉树，订阅尺寸变化、指针与窗口可见性。
    /// </summary>
    /// <param name="root">胶囊根 Grid（<c>Grid_Root</c>），不可为 <see langword="null"/>。</param>
    /// <param name="glowHost">呼吸光晕宿主（<c>Grid_GlowHost</c>），不可为 <see langword="null"/>。</param>
    /// <param name="effectHost">胶囊内动效宿主（<c>Grid_EffectHost</c>），不可为 <see langword="null"/>。</param>
    /// <param name="actionButton">主操作按钮，不可为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/> 时抛出。</exception>
    public void Attach(Grid root, Grid glowHost, Grid effectHost, Button actionButton)
    {
        if (_attached)
        {
            Detach();
        }
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _glowHost = glowHost ?? throw new ArgumentNullException(nameof(glowHost));
        _effectHost = effectHost ?? throw new ArgumentNullException(nameof(effectHost));
        _actionButton = actionButton ?? throw new ArgumentNullException(nameof(actionButton));
        _compositor = ElementCompositionPreview.GetElementVisual(root).Compositor;

        _easeOut = CompositionEasingFunction.CreateCubicBezierEasingFunction(_compositor, new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        _easeInOut = CompositionEasingFunction.CreateCubicBezierEasingFunction(_compositor, new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

        root.SizeChanged += OnRootSizeChanged;
        root.PointerEntered += OnPointerEntered;
        root.PointerExited += OnPointerExited;
        // PointerMoved 可能被子元素标记为已处理，用 handledEventsToo 确保仍能收到，驱动聚光灯跟随
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        actionButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnActionPointerPressed), true);

        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnMainWindowStateChanged);

        _attached = true;
        TryBuildVisuals();
    }


    /// <summary>
    /// 卸载动效：取消事件与消息订阅、移除子视觉、释放 Composition 对象并重置状态。
    /// </summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }
        WeakReferenceMessenger.Default.UnregisterAll(this);

        if (_root is not null)
        {
            _root.SizeChanged -= OnRootSizeChanged;
            _root.PointerEntered -= OnPointerEntered;
            _root.PointerExited -= OnPointerExited;
            _root.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved));
        }
        if (_actionButton is not null)
        {
            _actionButton.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnActionPointerPressed));
        }
        if (_glowHost is not null)
        {
            try { ElementCompositionPreview.SetElementChildVisual(_glowHost, null); } catch { }
        }
        if (_effectHost is not null)
        {
            try { ElementCompositionPreview.SetElementChildVisual(_effectHost, null); } catch { }
        }

        DisposeVisuals();

        _compositor = null;
        _root = null;
        _glowHost = null;
        _effectHost = null;
        _actionButton = null;
        _attached = false;
        _glowActive = false;
        _ctaActive = false;
        _pointerInside = false;
        _windowVisible = true;
    }


    /// <summary>
    /// 设置各动效的启用状态：
    /// <paramref name="glowActive"/> 控制呼吸光晕（仅「可开始游戏」）；
    /// <paramref name="ctaActive"/> 控制流光 / 聚光灯 / 点击光爆（所有显示强调色背景的可操作状态）。
    /// 其余状态（运行中 / 安装中等）保持安静，避免喧宾夺主。
    /// </summary>
    /// <param name="glowActive">是否启用呼吸光晕（<see cref="GameState.StartGame"/>）。</param>
    /// <param name="ctaActive">是否启用 CTA 动效（强调色底可见且按钮可操作）。</param>
    public void SetState(bool glowActive, bool ctaActive)
    {
        if (_glowActive == glowActive && _ctaActive == ctaActive)
        {
            return;
        }
        _glowActive = glowActive;
        _ctaActive = ctaActive;
        if (!_ctaActive)
        {
            HideSpotlight();
        }
        UpdateIdleAnimations();
    }


    /// <summary>明暗主题切换时刷新辉光、流光与聚光灯的颜色。</summary>
    public void OnThemeChanged()
    {
        if (_glowShadow is not null)
        {
            try { _glowShadow.Color = GetAccentColor(); } catch { }
        }
        if (_shineBrush is not null && _shineBrush.ColorStops.Count == 3)
        {
            Color sc = GetShineColor();
            _shineBrush.ColorStops[0].Color = WithAlpha(sc, 0);
            _shineBrush.ColorStops[1].Color = sc;
            _shineBrush.ColorStops[2].Color = WithAlpha(sc, 0);
        }
        if (_spotlightBrush is not null && _spotlightBrush.ColorStops.Count == 2)
        {
            Color pc = GetSpotlightColor();
            _spotlightBrush.ColorStops[0].Color = pc;
            _spotlightBrush.ColorStops[1].Color = WithAlpha(pc, 0);
        }
    }


    /// <summary>
    /// 胶囊尺寸变化：视觉尚未创建则惰性构建，否则仅更新各视觉对象的尺寸与裁剪路径。
    /// </summary>
    /// <param name="sender">胶囊根 Grid。</param>
    /// <param name="e">尺寸变更参数。</param>
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_overlayRoot is null)
        {
            TryBuildVisuals();
        }
        else
        {
            ResizeVisuals();
        }
    }


    /// <summary>
    /// 响应 <see cref="MainWindowStateChangedMessage"/>：最小化 / 隐藏 / 锁屏时暂停循环动画，激活时恢复。
    /// </summary>
    /// <param name="recipient">消息接收者（本类实例）。</param>
    /// <param name="message">窗口状态变更消息。</param>
    private void OnMainWindowStateChanged(object recipient, MainWindowStateChangedMessage message)
    {
        bool? visible = message switch
        {
            { Hide: true } => false,
            { SessionLock: true } => false,
            { Activate: true } => true,
            _ => null,
        };
        if (visible is null || visible.Value == _windowVisible)
        {
            return;
        }
        _windowVisible = visible.Value;
        _root?.DispatcherQueue?.TryEnqueue(UpdateIdleAnimations);
    }


    /// <summary>
    /// 在 <see cref="_root"/> 已有有效尺寸时惰性创建光晕与 overlay 视觉树，并启动待机循环。
    /// 若视觉已存在则仅调用 <see cref="ResizeVisuals"/>。
    /// </summary>
    private void TryBuildVisuals()
    {
        if (!_attached || _compositor is null || _root is null || _glowHost is null || _effectHost is null)
        {
            return;
        }
        _width = (float)_root.ActualWidth;
        _height = (float)_root.ActualHeight;
        if (_width <= 0 || _height <= 0)
        {
            return;
        }
        if (_overlayRoot is not null)
        {
            ResizeVisuals();
            return;
        }
        BuildGlow();
        BuildOverlay();
        ResizeVisuals();
        UpdateIdleAnimations();
    }


    /// <summary>
    /// 构建呼吸光晕：离屏圆角矩形 → VisualSurface 遮罩 → 强调色 <see cref="DropShadow"/> → 挂到 <see cref="_glowHost"/>。
    /// </summary>
    private void BuildGlow()
    {
        Compositor c = _compositor!;
        // 用一个离屏的圆角矩形 ShapeVisual 作为阴影遮罩来源，得到“圆角形状”的辉光
        _glowMaskGeometry = c.CreateRoundedRectangleGeometry();
        _glowMaskGeometry.CornerRadius = new Vector2(CornerRadius);
        CompositionSpriteShape maskShape = c.CreateSpriteShape(_glowMaskGeometry);
        maskShape.FillBrush = c.CreateColorBrush(WithAlpha(Color.FromArgb(255, 255, 255, 255), 255));
        _glowMaskSource = c.CreateShapeVisual();
        _glowMaskSource.Shapes.Add(maskShape);

        _glowSurface = c.CreateVisualSurface();
        _glowSurface.SourceVisual = _glowMaskSource;
        _glowSurface.SourceOffset = Vector2.Zero;
        CompositionSurfaceBrush maskBrush = c.CreateSurfaceBrush(_glowSurface);

        _glowShadow = c.CreateDropShadow();
        _glowShadow.Mask = maskBrush;
        _glowShadow.Color = GetAccentColor();
        _glowShadow.BlurRadius = GlowBlurMin;
        _glowShadow.Opacity = GlowOpacityMin;
        _glowShadow.Offset = Vector3.Zero;

        _glowVisual = c.CreateSpriteVisual();
        _glowVisual.Shadow = _glowShadow;
        _glowVisual.Opacity = 0f;
        ElementCompositionPreview.SetElementChildVisual(_glowHost!, _glowVisual);
    }


    /// <summary>
    /// 构建胶囊内 overlay：Win2D 圆角裁剪 + 流光带 + 聚光灯层，挂到 <see cref="_effectHost"/>。
    /// </summary>
    private void BuildOverlay()
    {
        Compositor c = _compositor!;
        _overlayRoot = c.CreateContainerVisual();
        // 胶囊裁剪：流光 / 聚光 / 光爆都被限制在按钮圆角内
        _clipGeometry = c.CreatePathGeometry();
        _overlayRoot.Clip = c.CreateGeometricClip(_clipGeometry);

        // —— 流光 ——
        Color sc = GetShineColor();
        _shineBrush = c.CreateLinearGradientBrush();
        _shineBrush.MappingMode = CompositionMappingMode.Relative;
        _shineBrush.StartPoint = new Vector2(0f, 0.5f);
        _shineBrush.EndPoint = new Vector2(1f, 0.5f);
        _shineBrush.ColorStops.Add(c.CreateColorGradientStop(0.0f, WithAlpha(sc, 0)));
        _shineBrush.ColorStops.Add(c.CreateColorGradientStop(0.5f, sc));
        _shineBrush.ColorStops.Add(c.CreateColorGradientStop(1.0f, WithAlpha(sc, 0)));
        _shineVisual = c.CreateSpriteVisual();
        _shineVisual.Brush = _shineBrush;
        _shineVisual.RotationAngleInDegrees = ShineTiltDegrees;
        _shineVisual.Opacity = 0f;
        _overlayRoot.Children.InsertAtTop(_shineVisual);

        // —— 聚光灯 ——
        Color pc = GetSpotlightColor();
        _spotlightBrush = c.CreateRadialGradientBrush();
        _spotlightBrush.MappingMode = CompositionMappingMode.Absolute;
        _spotlightBrush.EllipseRadius = new Vector2(SpotlightRadius);
        _spotlightBrush.ColorStops.Add(c.CreateColorGradientStop(0.0f, pc));
        _spotlightBrush.ColorStops.Add(c.CreateColorGradientStop(1.0f, WithAlpha(pc, 0)));
        _spotlightVisual = c.CreateSpriteVisual();
        _spotlightVisual.Brush = _spotlightBrush;
        _spotlightVisual.Opacity = 0f;
        _overlayRoot.Children.InsertAtTop(_spotlightVisual);

        ElementCompositionPreview.SetElementChildVisual(_effectHost!, _overlayRoot);
    }


    /// <summary>
    /// 将光晕、遮罩、裁剪路径与 overlay 子视觉的尺寸同步到 <see cref="_root"/> 当前实际大小；
    /// 光晕通过 <c>TransformToVisual</c> 对齐到胶囊坐标，外溢部分不被父级圆角裁剪。
    /// </summary>
    private void ResizeVisuals()
    {
        if (_compositor is null || _root is null)
        {
            return;
        }
        _width = (float)_root.ActualWidth;
        _height = (float)_root.ActualHeight;
        if (_width <= 0 || _height <= 0)
        {
            return;
        }
        var size = new Vector2(_width, _height);

        // 辉光：放在胶囊之外的宿主里，用坐标变换对齐到 Grid_Root，外溢部分不被裁剪
        if (_glowVisual is not null && _glowHost is not null)
        {
            Point p = _root.TransformToVisual(_glowHost).TransformPoint(default);
            _glowVisual.Offset = new Vector3((float)p.X, (float)p.Y, 0f);
            _glowVisual.Size = size;
        }
        if (_glowMaskSource is not null)
        {
            _glowMaskSource.Size = size;
        }
        if (_glowMaskGeometry is not null)
        {
            _glowMaskGeometry.Size = size;
        }
        if (_glowSurface is not null)
        {
            _glowSurface.SourceSize = size;
        }

        // 胶囊裁剪路径
        if (_clipGeometry is not null)
        {
            CanvasDevice device = CanvasDevice.GetSharedDevice();
            using CanvasGeometry geo = CanvasGeometry.CreateRoundedRectangle(device, 0, 0, _width, _height, CornerRadius, CornerRadius);
            _clipGeometry.Path = new CompositionPath(geo);
        }
        if (_overlayRoot is not null)
        {
            _overlayRoot.Size = size;
        }

        // 流光高光带：比胶囊更高，旋转后仍能覆盖整高
        if (_shineVisual is not null)
        {
            float bandHeight = _height * 2.4f;
            _shineVisual.Size = new Vector2(ShineBandWidth, bandHeight);
            _shineVisual.CenterPoint = new Vector3(ShineBandWidth / 2f, bandHeight / 2f, 0f);
        }
        if (_spotlightVisual is not null)
        {
            _spotlightVisual.Size = size;
        }

        // 尺寸变化后，若流光正在播放则重启依赖尺寸的扫掠动画
        if (IsShineRunning())
        {
            StartShineSweep();
        }
    }


    /// <summary>呼吸光晕是否应运行：可开始游戏 + 窗口可见 + 视觉已就绪。</summary>
    /// <returns>满足全部条件时为 <see langword="true"/>。</returns>
    private bool IsGlowRunning()
    {
        return _attached && _glowActive && _windowVisible && _glowVisual is not null;
    }


    /// <summary>流光是否应运行：处于强调色 CTA 状态 + 窗口可见 + 视觉已就绪。</summary>
    /// <returns>满足全部条件时为 <see langword="true"/>。</returns>
    private bool IsShineRunning()
    {
        return _attached && _ctaActive && _windowVisible && _shineVisual is not null;
    }


    /// <summary>根据 <see cref="_glowActive"/> / <see cref="_ctaActive"/> 与窗口可见性，启停呼吸光晕与流光并淡入淡出。</summary>
    private void UpdateIdleAnimations()
    {
        if (_compositor is null)
        {
            return;
        }
        // 呼吸光晕：仅「可开始游戏」
        if (IsGlowRunning())
        {
            StartGlowBreathing();
            FadeOpacity(_glowVisual!, 1f, 280);
        }
        else
        {
            StopGlow();
            if (_glowVisual is not null)
            {
                FadeOpacity(_glowVisual, 0f, 200);
            }
        }
        // 流光：所有强调色 CTA 状态（开始 / 安装 / 更新 等）
        if (IsShineRunning())
        {
            StartShineSweep();
            FadeOpacity(_shineVisual!, 1f, 280);
        }
        else
        {
            StopShine();
            if (_shineVisual is not null)
            {
                FadeOpacity(_shineVisual, 0f, 200);
            }
        }
    }


    /// <summary>
    /// 启动呼吸光晕循环：在 <see cref="GlowBlurMin"/>↔<see cref="GlowBlurMax"/> 与
    /// <see cref="GlowOpacityMin"/>↔<see cref="GlowOpacityMax"/> 之间以 <see cref="GlowPeriod"/> 周期往复。
    /// </summary>
    private void StartGlowBreathing()
    {
        if (_compositor is null || _glowShadow is null)
        {
            return;
        }
        ScalarKeyFrameAnimation blur = _compositor.CreateScalarKeyFrameAnimation();
        blur.Duration = GlowPeriod;
        blur.InsertKeyFrame(0.0f, GlowBlurMin, _easeInOut);
        blur.InsertKeyFrame(0.5f, GlowBlurMax, _easeInOut);
        blur.InsertKeyFrame(1.0f, GlowBlurMin, _easeInOut);
        blur.IterationBehavior = AnimationIterationBehavior.Forever;
        _glowShadow.StartAnimation(nameof(DropShadow.BlurRadius), blur);

        ScalarKeyFrameAnimation opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = GlowPeriod;
        opacity.InsertKeyFrame(0.0f, GlowOpacityMin, _easeInOut);
        opacity.InsertKeyFrame(0.5f, GlowOpacityMax, _easeInOut);
        opacity.InsertKeyFrame(1.0f, GlowOpacityMin, _easeInOut);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;
        _glowShadow.StartAnimation(nameof(DropShadow.Opacity), opacity);
    }


    /// <summary>
    /// 启动流光扫掠：高光带从胶囊左侧外移动到右侧外，扫掠占 <see cref="ShineSweepRatio"/>，整周期 <see cref="ShineCycle"/>。
    /// </summary>
    private void StartShineSweep()
    {
        if (_compositor is null || _shineVisual is null)
        {
            return;
        }
        float bandHeight = _shineVisual.Size.Y;
        float offsetY = (_height - bandHeight) / 2f;
        float start = -ShineBandWidth - 8f;
        float end = _width + 8f;
        Vector3KeyFrameAnimation sweep = _compositor.CreateVector3KeyFrameAnimation();
        sweep.Duration = ShineCycle;
        sweep.InsertKeyFrame(0.0f, new Vector3(start, offsetY, 0f));
        sweep.InsertKeyFrame(0.02f, new Vector3(start, offsetY, 0f));
        sweep.InsertKeyFrame(0.02f + ShineSweepRatio, new Vector3(end, offsetY, 0f), _easeOut);
        sweep.InsertKeyFrame(1.0f, new Vector3(end, offsetY, 0f));
        sweep.IterationBehavior = AnimationIterationBehavior.Forever;
        _shineVisual.StartAnimation(nameof(Visual.Offset), sweep);
    }


    /// <summary>停止呼吸光晕的模糊半径与不透明度动画。</summary>
    private void StopGlow()
    {
        try { _glowShadow?.StopAnimation(nameof(DropShadow.BlurRadius)); } catch { }
        try { _glowShadow?.StopAnimation(nameof(DropShadow.Opacity)); } catch { }
    }


    /// <summary>停止流光扫掠的位移动画。</summary>
    private void StopShine()
    {
        try { _shineVisual?.StopAnimation(nameof(Visual.Offset)); } catch { }
    }


    /// <summary>指针进入胶囊：标记在内并淡入聚光灯（仅 CTA 状态）。</summary>
    /// <param name="sender">胶囊根 Grid。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        if (!_ctaActive || _spotlightVisual is null)
        {
            return;
        }
        UpdateSpotlightPosition(e);
        FadeOpacity(_spotlightVisual, 1f, 160);
    }


    /// <summary>指针离开胶囊：标记在外并淡出聚光灯。</summary>
    /// <param name="sender">胶囊根 Grid。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        HideSpotlight();
    }


    /// <summary>指针在胶囊内移动时更新聚光灯中心（仅 CTA 状态）。</summary>
    /// <param name="sender">胶囊根 Grid。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_ctaActive || !_pointerInside)
        {
            return;
        }
        UpdateSpotlightPosition(e);
    }


    /// <summary>
    /// 将聚光灯径向渐变中心设为指针在 <see cref="_effectHost"/> 坐标系中的位置。
    /// </summary>
    /// <param name="e">指针路由事件参数。</param>
    private void UpdateSpotlightPosition(PointerRoutedEventArgs e)
    {
        if (_spotlightBrush is null || _effectHost is null)
        {
            return;
        }
        Point p = e.GetCurrentPoint(_effectHost).Position;
        _spotlightBrush.EllipseCenter = new Vector2((float)p.X, (float)p.Y);
    }


    /// <summary>淡出聚光灯（220ms）。</summary>
    private void HideSpotlight()
    {
        if (_spotlightVisual is not null)
        {
            FadeOpacity(_spotlightVisual, 0f, 220);
        }
    }


    /// <summary>主按钮按下时在点击位置生成点击光爆（仅 CTA 状态）。</summary>
    /// <param name="sender">主操作按钮。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnActionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_ctaActive || _compositor is null || _overlayRoot is null || _effectHost is null)
        {
            return;
        }
        Point p = e.GetCurrentPoint(_effectHost).Position;
        SpawnRipple(new Vector2((float)p.X, (float)p.Y));
    }


    /// <summary>在点击点生成一圈向外扩散并淡出的光波，动画结束后自动从视觉树移除并释放资源。</summary>
    /// <param name="center">光波中心，相对于 <see cref="_effectHost"/> 的坐标（像素）。</param>
    private void SpawnRipple(Vector2 center)
    {
        Compositor c = _compositor!;
        Color rc = GetRippleColor();
        CompositionRadialGradientBrush brush = c.CreateRadialGradientBrush();
        brush.MappingMode = CompositionMappingMode.Relative;
        brush.EllipseCenter = new Vector2(0.5f);
        brush.EllipseRadius = new Vector2(0.5f);
        // 环状光波：中心透明 → 中段亮 → 边缘透明
        brush.ColorStops.Add(c.CreateColorGradientStop(0.0f, WithAlpha(rc, 0)));
        brush.ColorStops.Add(c.CreateColorGradientStop(0.55f, WithAlpha(rc, 0)));
        brush.ColorStops.Add(c.CreateColorGradientStop(0.78f, rc));
        brush.ColorStops.Add(c.CreateColorGradientStop(1.0f, WithAlpha(rc, 0)));

        SpriteVisual ripple = c.CreateSpriteVisual();
        ripple.Size = new Vector2(RippleBaseSize);
        ripple.CenterPoint = new Vector3(RippleBaseSize / 2f, RippleBaseSize / 2f, 0f);
        ripple.Offset = new Vector3(center.X - RippleBaseSize / 2f, center.Y - RippleBaseSize / 2f, 0f);
        ripple.Brush = brush;
        _overlayRoot!.Children.InsertAtTop(ripple);

        float maxScale = Math.Max(_width, _height) * 2.4f / RippleBaseSize;
        Vector3KeyFrameAnimation scale = c.CreateVector3KeyFrameAnimation();
        scale.Duration = RippleDuration;
        scale.InsertKeyFrame(0f, new Vector3(0.2f, 0.2f, 1f));
        scale.InsertKeyFrame(1f, new Vector3(maxScale, maxScale, 1f), _easeOut);

        ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
        fade.Duration = RippleDuration;
        fade.InsertKeyFrame(0f, 0.75f);
        fade.InsertKeyFrame(1f, 0f, _easeOut);

        CompositionScopedBatch batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        ripple.StartAnimation(nameof(Visual.Scale), scale);
        ripple.StartAnimation(nameof(Visual.Opacity), fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            try
            {
                _overlayRoot?.Children.Remove(ripple);
                ripple.Dispose();
                brush.Dispose();
            }
            catch { }
        };
    }


    /// <summary>对指定视觉播放不透明度渐变动画。</summary>
    /// <param name="visual">目标 Composition 视觉。</param>
    /// <param name="to">目标不透明度，范围 0–1。</param>
    /// <param name="milliseconds">动画时长（毫秒）。</param>
    private void FadeOpacity(Visual visual, float to, double milliseconds)
    {
        if (_compositor is null)
        {
            return;
        }
        ScalarKeyFrameAnimation anim = _compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, to);
        anim.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation(nameof(Visual.Opacity), anim);
    }


    /// <summary>停止循环动画并释放所有 Composition 视觉、画刷与几何对象。</summary>
    private void DisposeVisuals()
    {
        StopGlow();
        StopShine();
        try { _overlayRoot?.Dispose(); } catch { }
        try { _shineVisual?.Dispose(); } catch { }
        try { _shineBrush?.Dispose(); } catch { }
        try { _spotlightVisual?.Dispose(); } catch { }
        try { _spotlightBrush?.Dispose(); } catch { }
        try { _clipGeometry?.Dispose(); } catch { }
        try { _glowVisual?.Dispose(); } catch { }
        try { _glowShadow?.Dispose(); } catch { }
        try { _glowSurface?.Dispose(); } catch { }
        try { _glowMaskSource?.Dispose(); } catch { }
        try { _glowMaskGeometry?.Dispose(); } catch { }

        _overlayRoot = null;
        _shineVisual = null;
        _shineBrush = null;
        _spotlightVisual = null;
        _spotlightBrush = null;
        _clipGeometry = null;
        _glowVisual = null;
        _glowShadow = null;
        _glowSurface = null;
        _glowMaskSource = null;
        _glowMaskGeometry = null;
        _easeOut = null;
        _easeInOut = null;
    }


    /// <summary>从主题资源读取系统强调色；资源不可用时回退为默认蓝色。</summary>
    /// <returns>强调色 <see cref="Color"/>。</returns>
    private static Color GetAccentColor()
    {
        if (Application.Current.Resources["AccentFillColorDefaultBrush"] is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
    }


    /// <summary>流光高光颜色：白色，明暗主题下使用不同 alpha。</summary>
    /// <returns>带透明度的白色。</returns>
    private Color GetShineColor()
    {
        byte alpha = (byte)(IsDark() ? 0x96 : 0x6E);
        return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    }


    /// <summary>聚光灯柔光颜色：白色，明暗主题下使用不同 alpha。</summary>
    /// <returns>带透明度的白色。</returns>
    private Color GetSpotlightColor()
    {
        byte alpha = (byte)(IsDark() ? 0x70 : 0x55);
        return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    }


    /// <summary>点击光波颜色：白色，明暗主题下使用不同 alpha。</summary>
    /// <returns>带透明度的白色。</returns>
    private Color GetRippleColor()
    {
        byte alpha = (byte)(IsDark() ? 0xB0 : 0x90);
        return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    }


    /// <summary>当前胶囊是否处于深色主题。</summary>
    /// <returns>深色主题为 <see langword="true"/>。</returns>
    private bool IsDark()
    {
        return (_root?.ActualTheme ?? ElementTheme.Default) == ElementTheme.Dark;
    }


    /// <summary>替换颜色的 alpha 通道，保留 RGB。</summary>
    /// <param name="color">原始颜色。</param>
    /// <param name="alpha">新的 alpha 值（0–255）。</param>
    /// <returns>仅 alpha 不同的新颜色。</returns>
    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }


}