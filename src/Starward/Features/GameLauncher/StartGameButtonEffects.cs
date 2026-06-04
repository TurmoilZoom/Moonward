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
/// 为「开始游戏」按钮提供一套可复用的酷炫 Composition 动效（仅在 <see cref="GameState.StartGame"/> 即“可开始游戏”时启用）：
/// <list type="bullet">
/// <item>呼吸光晕：胶囊外圈的强调色辉光，模糊半径 + 不透明度做正弦呼吸（DropShadow + 圆角遮罩）。</item>
/// <item>流光扫过：一道斜向高光每隔数秒从左扫到右（SpriteVisual + 线性渐变，整体被胶囊裁剪）。</item>
/// <item>指针跟随聚光灯：悬停时柔光跟随鼠标在按钮表面移动（CompositionRadialGradientBrush）。</item>
/// <item>点击光爆：按下瞬间从点击点扩散一圈光波（SpriteVisual + 径向渐变，缩放 + 淡出）。</item>
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

    /// <summary>呼吸光晕模糊半径范围（像素）。</summary>
    private const float GlowBlurMin = 10f;
    private const float GlowBlurMax = 24f;

    /// <summary>呼吸光晕不透明度范围。</summary>
    private const float GlowOpacityMin = 0.35f;
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

    /// <summary>点击光爆时长。</summary>
    private static readonly TimeSpan RippleDuration = TimeSpan.FromMilliseconds(560);


    private Compositor? _compositor;

    private Grid? _root;

    private Grid? _glowHost;

    private Grid? _effectHost;

    private Button? _actionButton;

    private CompositionEasingFunction? _easeOut;

    private CompositionEasingFunction? _easeInOut;

    // —— 呼吸光晕 ——
    private SpriteVisual? _glowVisual;
    private DropShadow? _glowShadow;
    private ShapeVisual? _glowMaskSource;
    private CompositionRoundedRectangleGeometry? _glowMaskGeometry;
    private CompositionVisualSurface? _glowSurface;

    // —— 流光 / 聚光 / 光爆 共用容器（带胶囊裁剪） ——
    private ContainerVisual? _overlayRoot;
    private CompositionPathGeometry? _clipGeometry;

    private SpriteVisual? _shineVisual;
    private CompositionLinearGradientBrush? _shineBrush;

    private SpriteVisual? _spotlightVisual;
    private CompositionRadialGradientBrush? _spotlightBrush;

    private float _width;
    private float _height;

    private bool _attached;

    /// <summary>是否启用呼吸光晕：仅「可开始游戏」(GameState.StartGame) 状态，作为“游戏就绪”的专属信号。</summary>
    private bool _glowActive;

    /// <summary>是否启用流光 / 聚光灯 / 点击光爆：所有显示强调色背景的可操作状态（开始 / 安装 / 更新 等）。</summary>
    private bool _ctaActive;

    /// <summary>主窗口当前是否可见（最小化 / 隐藏 / 锁屏时为 false）。</summary>
    private bool _windowVisible = true;

    /// <summary>指针是否位于按钮范围内。</summary>
    private bool _pointerInside;


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


    /// <summary>明暗主题切换时刷新各效果颜色。</summary>
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


    /// <summary>窗口状态变化：最小化 / 隐藏 / 锁屏时暂停循环动画，激活时恢复。</summary>
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
    private bool IsGlowRunning()
    {
        return _attached && _glowActive && _windowVisible && _glowVisual is not null;
    }


    /// <summary>流光是否应运行：处于强调色 CTA 状态 + 窗口可见 + 视觉已就绪。</summary>
    private bool IsShineRunning()
    {
        return _attached && _ctaActive && _windowVisible && _shineVisual is not null;
    }


    /// <summary>根据当前状态分别启停呼吸光晕与流光两条待机循环。</summary>
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


    private void StopGlow()
    {
        try { _glowShadow?.StopAnimation(nameof(DropShadow.BlurRadius)); } catch { }
        try { _glowShadow?.StopAnimation(nameof(DropShadow.Opacity)); } catch { }
    }


    private void StopShine()
    {
        try { _shineVisual?.StopAnimation(nameof(Visual.Offset)); } catch { }
    }


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


    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        HideSpotlight();
    }


    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_ctaActive || !_pointerInside)
        {
            return;
        }
        UpdateSpotlightPosition(e);
    }


    private void UpdateSpotlightPosition(PointerRoutedEventArgs e)
    {
        if (_spotlightBrush is null || _effectHost is null)
        {
            return;
        }
        Point p = e.GetCurrentPoint(_effectHost).Position;
        _spotlightBrush.EllipseCenter = new Vector2((float)p.X, (float)p.Y);
    }


    private void HideSpotlight()
    {
        if (_spotlightVisual is not null)
        {
            FadeOpacity(_spotlightVisual, 0f, 220);
        }
    }


    private void OnActionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_ctaActive || _compositor is null || _overlayRoot is null || _effectHost is null)
        {
            return;
        }
        Point p = e.GetCurrentPoint(_effectHost).Position;
        SpawnRipple(new Vector2((float)p.X, (float)p.Y));
    }


    /// <summary>在点击点生成一圈向外扩散并淡出的光波，动画结束后自动回收。</summary>
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


    private static Color GetAccentColor()
    {
        if (Application.Current.Resources["AccentFillColorDefaultBrush"] is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
    }


    private Color GetShineColor()
    {
        byte alpha = (byte)(IsDark() ? 0x96 : 0x6E);
        return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    }


    private Color GetSpotlightColor()
    {
        byte alpha = (byte)(IsDark() ? 0x70 : 0x55);
        return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    }


    private Color GetRippleColor()
    {
        byte alpha = (byte)(IsDark() ? 0xB0 : 0x90);
        return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    }


    private bool IsDark()
    {
        return (_root?.ActualTheme ?? ElementTheme.Default) == ElementTheme.Dark;
    }


    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }


}
