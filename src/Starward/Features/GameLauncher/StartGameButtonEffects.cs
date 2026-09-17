using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Starward.Controls;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 为「开始游戏」按钮提供 Composition 动效（聚光灯 / 点击光爆）。
/// <list type="bullet">
/// <item>指针跟随聚光灯：悬停时柔光跟随鼠标（<see cref="CompositionRadialGradientBrush"/>）。</item>
/// <item>点击光爆：按下瞬间从点击点扩散光波并淡出。</item>
/// </list>
///
/// 设计要点（与 <see cref="Starward.Controls.FluidNavigationViewHoverEffect"/> 保持一致）：
/// - 纯 Microsoft.UI.Composition + Win2D（圆角裁剪），Attach/Detach 生命周期，惰性创建视觉对象；
/// - 只在指针交互时播放，没有常驻循环动画；
/// - 系统关闭「动画效果」时两种动效都不显示（每次交互时读取开关，切换后立即生效）。
///
/// 用法：
/// 1. 在 StartGameButton.xaml 中放置 IsHitTestVisible="False" 的宿主 Grid_EffectHost（位于强调色背景之上、文字之下，被胶囊圆角裁剪）。
/// 2. Loaded 调用 <see cref="Attach"/>，Unloaded 调用 <see cref="Detach"/>。
/// 3. GameState / 可用状态变化时调用 <see cref="SetState"/>(ctaActive)：ctaActive = 是否显示强调色背景（开始 / 安装 / 更新 等）。
/// 4. 主题变化时调用 <see cref="OnThemeChanged"/>。
/// </summary>
public sealed class StartGameButtonEffects
{

    /// <summary>胶囊圆角半径，与 StartGameButton 的 CornerRadius 一致。</summary>
    private const float CornerRadius = 22f;

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

    /// <summary>胶囊内动效宿主（<c>Grid_EffectHost</c>），承载聚光 / 光爆。</summary>
    private Grid? _effectHost;

    /// <summary>主操作按钮，用于监听按下以触发点击光爆。</summary>
    private Button? _actionButton;

    /// <summary>缓出贝塞尔，用于光爆扩散。</summary>
    private CompositionEasingFunction? _easeOut;

    /// <summary>聚光 / 光爆的共用容器，带胶囊圆角裁剪。</summary>
    private ContainerVisual? _overlayRoot;

    /// <summary>Win2D 生成的圆角路径，用于 <see cref="_overlayRoot"/> 的几何裁剪。</summary>
    private CompositionPathGeometry? _clipGeometry;

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

    /// <summary>是否启用聚光灯 / 点击光爆：所有显示强调色背景的可操作状态（开始 / 安装 / 更新 等）。</summary>
    private bool _ctaActive;

    /// <summary>聚光灯是否处于显示中；指针进入时按状态与系统开关决定，移动时只在显示中才跟随。</summary>
    private bool _spotlightShown;


    /// <summary>
    /// 挂接动效宿主与事件：惰性构建 Composition 视觉树，订阅尺寸变化与指针事件。
    /// </summary>
    /// <param name="root">胶囊根 Grid（<c>Grid_Root</c>），不可为 <see langword="null"/>。</param>
    /// <param name="effectHost">胶囊内动效宿主（<c>Grid_EffectHost</c>），不可为 <see langword="null"/>。</param>
    /// <param name="actionButton">主操作按钮，不可为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/> 时抛出。</exception>
    public void Attach(Grid root, Grid effectHost, Button actionButton)
    {
        if (_attached)
        {
            Detach();
        }
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _effectHost = effectHost ?? throw new ArgumentNullException(nameof(effectHost));
        _actionButton = actionButton ?? throw new ArgumentNullException(nameof(actionButton));
        _compositor = ElementCompositionPreview.GetElementVisual(root).Compositor;

        _easeOut = CompositionEasingFunction.CreateCubicBezierEasingFunction(_compositor, new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

        root.SizeChanged += OnRootSizeChanged;
        root.PointerEntered += OnPointerEntered;
        root.PointerExited += OnPointerExited;
        // PointerMoved 可能被子元素标记为已处理，用 handledEventsToo 确保仍能收到，驱动聚光灯跟随
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        actionButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnActionPointerPressed), true);

        _attached = true;
        TryBuildVisuals();
    }


    /// <summary>
    /// 卸载动效：取消事件订阅、移除子视觉、释放 Composition 对象并重置状态。
    /// </summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

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
        if (_effectHost is not null)
        {
            try { ElementCompositionPreview.SetElementChildVisual(_effectHost, null); } catch { }
        }

        DisposeVisuals();

        _compositor = null;
        _root = null;
        _effectHost = null;
        _actionButton = null;
        _attached = false;
        _ctaActive = false;
        _spotlightShown = false;
    }


    /// <summary>
    /// 设置动效启用状态：<paramref name="ctaActive"/> 控制聚光灯 / 点击光爆（所有显示强调色背景的可操作状态）。
    /// 其余状态（运行中 / 安装中等）保持安静，避免喧宾夺主。
    /// </summary>
    /// <param name="ctaActive">是否启用 CTA 动效（强调色底可见且按钮可操作）。</param>
    public void SetState(bool ctaActive)
    {
        if (_ctaActive == ctaActive)
        {
            return;
        }
        _ctaActive = ctaActive;
        if (!_ctaActive)
        {
            HideSpotlight();
        }
    }


    /// <summary>明暗主题切换时刷新聚光灯的颜色。</summary>
    public void OnThemeChanged()
    {
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
    /// 在 <see cref="_root"/> 已有有效尺寸时惰性创建 overlay 视觉树。
    /// 若视觉已存在则仅调用 <see cref="ResizeVisuals"/>。
    /// </summary>
    private void TryBuildVisuals()
    {
        if (!_attached || _compositor is null || _root is null || _effectHost is null)
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
        BuildOverlay();
        ResizeVisuals();
    }


    /// <summary>
    /// 构建胶囊内 overlay：Win2D 圆角裁剪 + 聚光灯层，挂到 <see cref="_effectHost"/>。
    /// </summary>
    private void BuildOverlay()
    {
        Compositor c = _compositor!;
        _overlayRoot = c.CreateContainerVisual();
        // 胶囊裁剪：聚光 / 光爆都被限制在按钮圆角内
        _clipGeometry = c.CreatePathGeometry();
        _overlayRoot.Clip = c.CreateGeometricClip(_clipGeometry);

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
    /// 将裁剪路径与 overlay 子视觉的尺寸同步到 <see cref="_root"/> 当前实际大小。
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
        if (_spotlightVisual is not null)
        {
            _spotlightVisual.Size = size;
        }
    }


    /// <summary>指针进入胶囊：CTA 状态且系统允许动画时淡入聚光灯。</summary>
    /// <param name="sender">胶囊根 Grid。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_ctaActive || _spotlightVisual is null || !EntranceAnimation.AnimationsEnabled())
        {
            return;
        }
        _spotlightShown = true;
        UpdateSpotlightPosition(e);
        FadeOpacity(_spotlightVisual, 1f, 160);
    }


    /// <summary>指针离开胶囊：淡出聚光灯。</summary>
    /// <param name="sender">胶囊根 Grid。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        HideSpotlight();
    }


    /// <summary>指针在胶囊内移动时更新聚光灯中心（仅聚光灯显示中）。</summary>
    /// <param name="sender">胶囊根 Grid。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_spotlightShown)
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
        _spotlightShown = false;
        if (_spotlightVisual is not null)
        {
            FadeOpacity(_spotlightVisual, 0f, 220);
        }
    }


    /// <summary>主按钮按下时在点击位置生成点击光爆（仅 CTA 状态且系统允许动画）。</summary>
    /// <param name="sender">主操作按钮。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void OnActionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_ctaActive || _compositor is null || _overlayRoot is null || _effectHost is null || !EntranceAnimation.AnimationsEnabled())
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


    /// <summary>对指定视觉播放不透明度渐变动画；系统关闭动画时直接设为目标值。</summary>
    /// <param name="visual">目标 Composition 视觉。</param>
    /// <param name="to">目标不透明度，范围 0–1。</param>
    /// <param name="milliseconds">动画时长（毫秒）。</param>
    private void FadeOpacity(Visual visual, float to, double milliseconds)
    {
        if (_compositor is null)
        {
            return;
        }
        if (!EntranceAnimation.AnimationsEnabled())
        {
            // 先停掉可能还在跑的渐变，否则直接赋值会被动画覆盖
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Opacity = to;
            return;
        }
        ScalarKeyFrameAnimation anim = _compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, to);
        anim.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation(nameof(Visual.Opacity), anim);
    }


    /// <summary>释放所有 Composition 视觉、画刷与几何对象。</summary>
    private void DisposeVisuals()
    {
        try { _overlayRoot?.Dispose(); } catch { }
        try { _spotlightVisual?.Dispose(); } catch { }
        try { _spotlightBrush?.Dispose(); } catch { }
        try { _clipGeometry?.Dispose(); } catch { }

        _overlayRoot = null;
        _spotlightVisual = null;
        _spotlightBrush = null;
        _clipGeometry = null;
        _easeOut = null;
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
