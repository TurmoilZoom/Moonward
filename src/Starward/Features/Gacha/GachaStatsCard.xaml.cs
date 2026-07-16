using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;


namespace Starward.Features.Gacha;

/// <summary>
/// 单卡池抽卡统计卡片（原神 / 星铁等非绝区零布局）。
/// <para>承载统计摘要、5/4 星记录列表切换、保底进度条，并实现 <see cref="IGachaStatsDragCard"/> 供页面级横向换位拖拽。</para>
/// <para>列表内鼠标拖拽滚动、分段列表联动、保底条动画分别由 Helper 注入，本类负责构造时 Bind、卸载时 Dispose。</para>
/// </summary>
public sealed partial class GachaStatsCard : UserControl, IGachaStatsDragCard
{

    /// <summary>5/4 星分段控件与对应 <see cref="ItemsRepeater"/> / <see cref="ScrollViewer"/> 的联动绑定。</summary>
    private GachaStatsSegmentedListHelper.GachaStatsSegmentedListBinding? _segmentedListBinding;

    /// <summary>5 星列表保底进度条的 Composition 动画绑定。</summary>
    private GachaPityBarAnimation.GachaPityBarBinding? _pityBarBinding;

    /// <summary>记录列表的鼠标拖拽滚动 + 顶底过拉回弹绑定。</summary>
    private GachaStatsListDragScrollHelper.GachaStatsListDragScrollBinding? _dragScrollBinding;


    /// <summary>
    /// 初始化卡片 XAML，并为分段列表、保底条、列表拖拽滚动注入行为。
    /// </summary>
    public GachaStatsCard()
    {
        this.InitializeComponent();
        // 三个 Bind 均返回需 Dispose 的句柄；卡片从视觉树移除时在 OnCardUnloaded 统一释放。
        _segmentedListBinding = GachaStatsSegmentedListHelper.Bind(Segmented_GachaItemList, ItemsRepeater_List_5, ItemsRepeater_List_4, ScrollViewer_GachaItemList);
        _pityBarBinding = GachaPityBarAnimation.Bind(ItemsRepeater_List_5);
        _dragScrollBinding = GachaStatsListDragScrollHelper.Bind(ScrollViewer_GachaItemList);
        Unloaded += OnCardUnloaded;
    }


    /// <summary>
    /// 卡片卸载时解除事件与 Helper 绑定，避免常驻卡片池复用时重复订阅或泄漏 Composition/指针句柄。
    /// </summary>
    /// <param name="sender">事件源（本控件）。</param>
    /// <param name="e">卸载路由事件参数。</param>
    private void OnCardUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnCardUnloaded;
        _segmentedListBinding?.Dispose();
        _segmentedListBinding = null;
        _pityBarBinding?.Dispose();
        _pityBarBinding = null;
        _dragScrollBinding?.Dispose();
        _dragScrollBinding = null;
    }


    /// <summary>拖拽手柄：tab 标签上方的卡池统计信息区域，按住此处可拖动整张卡片换位。</summary>
    public FrameworkElement DragHandle => DragHandleBorder;


    /// <summary>
    /// 本卡片绑定的卡池统计数据（抽数、UP 率、5/4 星列表等）。
    /// 页面复用卡片实例时会重新赋值；可为 null（未绑定数据时）。
    /// </summary>
    public GachaTypeStats WarpTypeStats
    {
        get { return (GachaTypeStats)GetValue(WarpTypeStatsProperty); }
        set { SetValue(WarpTypeStatsProperty, value); }
    }

    /// <summary><see cref="WarpTypeStats"/> 的依赖属性标识，供 XAML 绑定与样式使用。</summary>
    public static readonly DependencyProperty WarpTypeStatsProperty =
        DependencyProperty.Register("WarpTypeStats", typeof(GachaTypeStats), typeof(GachaStatsCard), new PropertyMetadata(null));


    /// <summary>
    /// 指针进入 5 星记录项时：按角色/武器名高亮同名项（设置 <see cref="GachaLogItemEx.IsPointerIn"/>），用于展开该行详情并弱化其余行。
    /// </summary>
    /// <param name="sender">触发进入的列表项根元素；其 <c>Tag</c> 应为 <see cref="GachaLogItemEx"/>。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Grid_Rarity5Item_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_5?.Any() ?? false)
                {
                    // 同名多条记录一并高亮（按 Name 匹配，非引用相等）。
                    foreach (var l5 in WarpTypeStats.List_5)
                    {
                        l5.IsPointerIn = (l5.Name == item.Name);
                    }
                }
            }
        }
        catch { }
    }


    /// <summary>
    /// 指针离开 5 星记录项时：清除列表上所有 <see cref="GachaLogItemEx.IsPointerIn"/>，恢复默认行布局。
    /// </summary>
    /// <param name="sender">触发离开的列表项根元素；其 <c>Tag</c> 应为 <see cref="GachaLogItemEx"/>。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Grid_Rarity5Item_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_5?.Any() ?? false)
                {
                    foreach (var l5 in WarpTypeStats.List_5)
                    {
                        l5.IsPointerIn = false;
                    }
                }
            }
        }
        catch { }
    }


    /// <summary>
    /// 指针进入 4 星记录项时：按名称高亮同名项（逻辑同 5 星，操作 <see cref="GachaTypeStats.List_4"/>）。
    /// </summary>
    /// <param name="sender">触发进入的列表项根元素；其 <c>Tag</c> 应为 <see cref="GachaLogItemEx"/>。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Grid_Rarity4Item_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_4?.Any() ?? false)
                {
                    foreach (var l5 in WarpTypeStats.List_4)
                    {
                        l5.IsPointerIn = (l5.Name == item.Name);
                    }
                }
            }
        }
        catch { }
    }


    /// <summary>
    /// 指针离开 4 星记录项时：清除 4 星列表上所有悬停高亮。
    /// </summary>
    /// <param name="sender">触发离开的列表项根元素；其 <c>Tag</c> 应为 <see cref="GachaLogItemEx"/>。</param>
    /// <param name="e">指针路由事件参数。</param>
    private void Grid_Rarity4Item_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_4?.Any() ?? false)
                {
                    foreach (var l5 in WarpTypeStats.List_4)
                    {
                        l5.IsPointerIn = false;
                    }
                }
            }
        }
        catch { }
    }


    /// <summary>
    /// 卡池名称文本被裁剪时的回调。历史上曾尝试逐级缩小字号，当前逻辑已禁用，保留空处理与 XAML 挂接以便日后恢复。
    /// </summary>
    /// <param name="sender">卡池名称 <see cref="TextBlock"/>。</param>
    /// <param name="args">是否发生裁剪的事件参数。</param>
    private void TextBlock_GachaTypeText_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        // 曾用 16→14→12 缩小字号适配长池名；现依赖 XAML 裁剪/布局，不再改 FontSize。
        //if (sender.FontSize == 16)
        //{
        //    sender.FontSize = 14;
        //}
        //if (sender.FontSize == 14)
        //{
        //    sender.FontSize = 12;
        //}
    }


    /// <summary>
    /// 将卡池名称字号重置为默认 16。卡片复用或数据刷新后若曾改过字号可调用此方法恢复。
    /// </summary>
    public void ResetGachaTypeTextFontSize()
    {
        TextBlock_GachaTypeText.FontSize = 16;
    }

}
