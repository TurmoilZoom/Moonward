using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Starward.Controls;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace Starward.Features.GameSelector;

/// <summary>
/// 游戏选择面板的卡片切换动画（仿官方启动器）：指针移入哪张卡片的命中区就选中哪张，选中的卡片以左上角为原点放大、
/// 描边从上下两条边的中点向两侧画出，同一行排在它后面的卡片右移让出放大多出的宽度，其余卡片压暗。
/// 指针移到面板空白处时保持最后一张为选中。
/// </summary>
/// <remarks>
/// <para>每张卡片外面包一层命中容器，左右各留半个间距：指针离开卡片走到间距正中时就切到相邻卡片（官方启动器逐帧实测如此），
/// 卡片本身的进出只负责悬停遮罩。平移挂在命中容器上（命中区跟着让位），缩放挂在卡片上。</para>
/// <para>卡片由 <c>ItemsControl</c> 测量，视觉只直接赋值 <see cref="CompositeTransform"/>、<see cref="UIElement.Opacity"/>
/// 与描边的虚线参数，过渡在 <see cref="CompositionTarget.Rendering"/> 里逐帧推进，不用 Storyboard、不写 Composition
/// （参照 <c>GachaStatsCardDragReorder</c>：被测量的元素上写 Composition 会在跨缩放换屏时崩溃）。</para>
/// <para>放大与让位都不改布局尺寸，不会触发重新换行；所以卡片列表右侧要留出让位的空白，下方要留出放大多出的高度。</para>
/// </remarks>
internal sealed class GameCardSwitchAnimator
{

    /// <summary>选中卡片的放大倍数，与官方启动器（约 123 → 139）一致。</summary>
    public const double SelectedScale = 1.12;

    /// <summary>卡片宽度，须与 <c>GameSelector.xaml</c> 中卡片的 <c>Width</c> 一致。</summary>
    public const double CardWidth = 144;

    /// <summary>卡片高度，须与 <c>GameSelector.xaml</c> 中卡片的 <c>Height</c> 一致。</summary>
    public const double CardHeight = 108;

    /// <summary>卡片圆角，须与卡片的 <c>CornerRadius</c> 一致。</summary>
    public const double CardCornerRadius = 8;

    /// <summary>描边粗细，须与描边 <c>Path</c> 的 <c>StrokeThickness</c> 一致。</summary>
    public const double BorderThickness = 2;

    /// <summary>选中卡片放大后多出的宽度，即同一行后面的卡片右移的距离。</summary>
    public const double SelectedExtraWidth = CardWidth * (SelectedScale - 1);

    /// <summary>
    /// 描边路径（沿卡片内缩半个线宽的圆角矩形）周长的一半。路径从右边中点起顺时针绕一圈，
    /// 上下两条边的中点相距正好半周，所以虚线周期取半周、虚线以下边中点为中心，就会从上下两个中点同时向两侧画出。
    /// </summary>
    private static readonly double BorderHalfPerimeter = GetHalfPerimeter();

    /// <summary>未选中卡片上压暗层的不透明度。</summary>
    private const double UnselectedDimOpacity = 0.45;

    /// <summary>缩放、让位、压暗过渡的平滑时间常数（秒）：约 3 倍时间常数（150ms）基本到位，与官方启动器的切换时长相当。</summary>
    private const double SmoothingTau = 0.05;

    /// <summary>描边从无到画满的时长（秒），官方启动器约 0.23 秒，上游 Starward 为 0.3 秒。</summary>
    private const double BorderTraceDuration = 0.28;

    /// <summary>位移离目标小于该距离（像素）就直接吸附。</summary>
    private const double TranslateSnapEpsilon = 0.3;

    /// <summary>缩放与不透明度离目标小于该值就直接吸附。</summary>
    private const double ScalarSnapEpsilon = 0.002;

    /// <summary>判断两张卡片在同一行时允许的纵向误差（像素）。</summary>
    private const double SameRowTolerance = 1;


    /// <summary>一张卡片的视觉元素与当前值、目标值。</summary>
    private sealed class Card
    {
        public Card(FrameworkElement hitArea, CompositeTransform hitAreaTransform, CompositeTransform cardTransform, UIElement? dim, Shape? border)
        {
            HitArea = hitArea;
            HitAreaTransform = hitAreaTransform;
            CardTransform = cardTransform;
            Dim = dim;
            Border = border;
        }

        public FrameworkElement HitArea { get; }

        public CompositeTransform HitAreaTransform { get; }

        public CompositeTransform CardTransform { get; }

        public UIElement? Dim { get; }

        public Shape? Border { get; }

        public double Scale { get; set; } = 1;

        public double TargetScale { get; set; } = 1;

        public double TranslateX { get; set; }

        public double TargetTranslateX { get; set; }

        public double DimOpacity { get; set; }

        public double TargetDimOpacity { get; set; }

        public double BorderOpacity { get; set; }

        public double TargetBorderOpacity { get; set; }

        /// <summary>描边已画出的时间（秒），不小于 <see cref="BorderTraceDuration"/> 即画满。</summary>
        public double TraceElapsed { get; set; } = BorderTraceDuration;
    }


    private readonly List<Card> _cards = new();

    private Card? _selected;

    private bool _renderingHooked;

    private TimeSpan _lastRenderTime;


    /// <summary>当前是否有选中的卡片。</summary>
    public bool HasSelection => _selected is not null;


    /// <summary>
    /// 登记一张卡片（在命中容器 <c>Loaded</c> 时调用）。两者的 <c>RenderTransform</c> 不是 <see cref="CompositeTransform"/> 时会替换为新建的。
    /// </summary>
    /// <param name="hitArea">卡片外层的命中容器（数据模板根元素），承担让位平移。</param>
    /// <param name="card">卡片本身，承担放大。</param>
    /// <param name="dim">未选中时显示的压暗层；为 <see langword="null"/> 时不压暗。</param>
    /// <param name="border">选中时画出的描边；为 <see langword="null"/> 时不显示描边。</param>
    public void Register(FrameworkElement hitArea, FrameworkElement card, UIElement? dim, Shape? border)
    {
        if (_cards.Exists(x => x.HitArea == hitArea))
        {
            return;
        }
        var c = new Card(hitArea, EnsureTransform(hitArea), EnsureTransform(card), dim, border);
        _cards.Add(c);
        Apply(c);
    }


    /// <summary>
    /// 注销一张卡片（在命中容器 <c>Unloaded</c> 时调用），并复位它的视觉。
    /// </summary>
    /// <param name="hitArea">卡片外层的命中容器。</param>
    public void Unregister(FrameworkElement hitArea)
    {
        int index = _cards.FindIndex(x => x.HitArea == hitArea);
        if (index < 0)
        {
            return;
        }
        Card card = _cards[index];
        _cards.RemoveAt(index);
        if (_selected == card)
        {
            _selected = null;
        }
        card.Scale = card.TargetScale = 1;
        card.TranslateX = card.TargetTranslateX = 0;
        card.DimOpacity = card.TargetDimOpacity = 0;
        card.BorderOpacity = card.TargetBorderOpacity = 0;
        card.TraceElapsed = BorderTraceDuration;
        Apply(card);
        if (_cards.Count == 0)
        {
            UnhookRendering();
        }
    }


    /// <summary>
    /// 选中指定卡片并播放切换过渡；已是选中的卡片时忽略。
    /// </summary>
    /// <param name="hitArea">要选中的卡片的命中容器；未登记时忽略。</param>
    public void Select(FrameworkElement hitArea)
    {
        Card? card = _cards.Find(x => x.HitArea == hitArea);
        if (card is null || card == _selected)
        {
            return;
        }
        _selected = card;
        // 新选中的卡片描边从头画起；旧卡片的描边保持画满，只淡出
        card.TraceElapsed = 0;
        card.BorderOpacity = 1;
        UpdateTargets(animate: true);
    }


    /// <summary>
    /// 选中第一张 <c>DataContext</c> 满足条件的卡片；没有满足条件的卡片时取消选中。不播放过渡。
    /// </summary>
    /// <param name="match">按卡片 <c>DataContext</c> 判断的条件。</param>
    public void SelectByData(Predicate<object?> match)
    {
        _selected = _cards.Find(x => match(x.HitArea.DataContext));
        UpdateTargets(animate: false);
    }


    /// <summary>
    /// 按当前布局重新计算各卡片的位置并直接到位（布局变化导致换行改变时调用，须在布局完成之后）。
    /// </summary>
    public void Refresh()
    {
        UpdateTargets(animate: false);
    }


    /// <summary>
    /// 按当前选中的卡片计算每张卡片的目标值。
    /// </summary>
    /// <param name="animate">是否逐帧过渡；为 <see langword="false"/> 或系统关闭了动画效果时直接到位。</param>
    private void UpdateTargets(bool animate)
    {
        Rect? selectedSlot = _selected is null ? null : GetSlot(_selected.HitArea);
        foreach (Card card in _cards)
        {
            bool isSelected = card == _selected;
            double translateX = 0;
            if (!isSelected && selectedSlot is Rect s && GetSlot(card.HitArea) is Rect slot)
            {
                // 只有同一行、排在选中卡片后面的卡片让位；其他行不受影响
                if (Math.Abs(slot.Y - s.Y) <= SameRowTolerance && slot.X > s.X)
                {
                    translateX = SelectedExtraWidth;
                }
            }
            card.TargetScale = isSelected ? SelectedScale : 1;
            card.TargetTranslateX = translateX;
            card.TargetDimOpacity = _selected is null || isSelected ? 0 : UnselectedDimOpacity;
            card.TargetBorderOpacity = isSelected ? 1 : 0;
            // 选中卡片放大后的下沿会压到下一行命中容器的上边距，提到上层以免被下一行抢走命中
            if (VisualTreeHelper.GetParent(card.HitArea) is UIElement container)
            {
                Canvas.SetZIndex(container, isSelected ? 1 : 0);
            }
        }

        if (animate && EntranceAnimation.AnimationsEnabled())
        {
            HookRendering();
        }
        else
        {
            UnhookRendering();
            foreach (Card card in _cards)
            {
                card.Scale = card.TargetScale;
                card.TranslateX = card.TargetTranslateX;
                card.DimOpacity = card.TargetDimOpacity;
                card.BorderOpacity = card.TargetBorderOpacity;
                card.TraceElapsed = BorderTraceDuration;
                Apply(card);
            }
        }
    }


    /// <summary>
    /// 取命中容器所在容器在面板里的布局槽位（不含渲染变换），尚未进入可视树时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="hitArea">卡片外层的命中容器。</param>
    /// <returns>布局槽位。</returns>
    private static Rect? GetSlot(FrameworkElement hitArea)
    {
        // 数据模板根元素外面是 ItemsControl 生成的 ContentPresenter，后者的槽位才是在 WrapPanel 里的位置
        if (VisualTreeHelper.GetParent(hitArea) is FrameworkElement container)
        {
            return LayoutInformation.GetLayoutSlot(container);
        }
        return null;
    }


    /// <summary>取元素的 <see cref="CompositeTransform"/>，没有时新建并赋给它。</summary>
    /// <param name="element">元素。</param>
    /// <returns>元素的渲染变换。</returns>
    private static CompositeTransform EnsureTransform(UIElement element)
    {
        if (element.RenderTransform is not CompositeTransform transform)
        {
            transform = new CompositeTransform();
            element.RenderTransform = transform;
        }
        return transform;
    }


    /// <summary>把卡片的当前值写到视觉上。</summary>
    /// <param name="card">卡片。</param>
    private static void Apply(Card card)
    {
        card.CardTransform.ScaleX = card.Scale;
        card.CardTransform.ScaleY = card.Scale;
        card.HitAreaTransform.TranslateX = card.TranslateX;
        if (card.Dim is not null)
        {
            card.Dim.Opacity = card.DimOpacity;
        }
        if (card.Border is not null)
        {
            card.Border.Opacity = card.BorderOpacity;
            ApplyBorderTrace(card.Border, card.TraceElapsed);
        }
    }


    /// <summary>
    /// 按已画时间设置描边的虚线：虚线周期为半周，虚线长度从 0 增长到半周，并始终以上下两条边的中点为中心。
    /// </summary>
    /// <param name="border">描边路径。</param>
    /// <param name="elapsed">已画出的时间（秒）。</param>
    private static void ApplyBorderTrace(Shape border, double elapsed)
    {
        if (elapsed >= BorderTraceDuration)
        {
            // 画满后去掉虚线，避免起点处留下接缝
            if (border.ReadLocalValue(Shape.StrokeDashArrayProperty) != DependencyProperty.UnsetValue)
            {
                border.ClearValue(Shape.StrokeDashArrayProperty);
                border.ClearValue(Shape.StrokeDashOffsetProperty);
            }
            return;
        }
        double p = Math.Clamp(elapsed / BorderTraceDuration, 0, 1);
        double eased = 1 - Math.Pow(1 - p, 3);
        double period = BorderHalfPerimeter;
        double dash = eased * period;
        // 虚线参数以线宽为单位；路径从右边中点起，下边中点位于四分之一周（半个周期）处，偏移使虚线以它为中心
        border.StrokeDashArray = new DoubleCollection { dash / BorderThickness, (period - dash) / BorderThickness };
        border.StrokeDashOffset = ((dash / 2) + (period / 2)) / BorderThickness;
    }


    /// <summary>计算描边路径周长的一半。</summary>
    /// <returns>半周长（像素）。</returns>
    private static double GetHalfPerimeter()
    {
        double w = CardWidth - BorderThickness;
        double h = CardHeight - BorderThickness;
        double r = CardCornerRadius - (BorderThickness / 2);
        return ((2 * (w - (2 * r))) + (2 * (h - (2 * r))) + (2 * Math.PI * r)) / 2;
    }


    private void HookRendering()
    {
        if (!_renderingHooked)
        {
            _renderingHooked = true;
            _lastRenderTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }
    }


    private void UnhookRendering()
    {
        if (_renderingHooked)
        {
            _renderingHooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }


    /// <summary>每帧把各卡片的当前值朝目标平滑逼近、推进描边，全部到位后停止。</summary>
    /// <param name="sender">渲染事件发送者；此处不使用。</param>
    /// <param name="e">渲染事件参数；提供本帧时间以计算步长。</param>
    private void OnRendering(object? sender, object e)
    {
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
        double factor = 1.0 - Math.Exp(-dt / SmoothingTau);
        bool settled = true;
        foreach (Card card in _cards)
        {
            card.Scale = Step(card.Scale, card.TargetScale, factor, ScalarSnapEpsilon, ref settled);
            card.TranslateX = Step(card.TranslateX, card.TargetTranslateX, factor, TranslateSnapEpsilon, ref settled);
            card.DimOpacity = Step(card.DimOpacity, card.TargetDimOpacity, factor, ScalarSnapEpsilon, ref settled);
            card.BorderOpacity = Step(card.BorderOpacity, card.TargetBorderOpacity, factor, ScalarSnapEpsilon, ref settled);
            if (card.TraceElapsed < BorderTraceDuration)
            {
                card.TraceElapsed = Math.Min(BorderTraceDuration, card.TraceElapsed + dt);
                settled = false;
            }
            Apply(card);
        }
        if (settled)
        {
            UnhookRendering();
        }
    }


    /// <summary>
    /// 把一个值朝目标逼近一步，足够接近时吸附到目标。
    /// </summary>
    /// <param name="current">当前值。</param>
    /// <param name="target">目标值。</param>
    /// <param name="factor">本帧逼近比例。</param>
    /// <param name="epsilon">吸附阈值。</param>
    /// <param name="settled">本帧后仍未到位时置为 <see langword="false"/>。</param>
    /// <returns>新的当前值。</returns>
    private static double Step(double current, double target, double factor, double epsilon, ref bool settled)
    {
        double diff = target - current;
        if (Math.Abs(diff) < epsilon)
        {
            return target;
        }
        settled = false;
        return current + (diff * factor);
    }

}
