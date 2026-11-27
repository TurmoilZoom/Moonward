using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;


namespace Starward.Features.Gacha;

/// <summary>
/// 紧凑视图的图标方块面板：按 <see cref="GachaTileLayout"/> 排成网格，方块等宽、一行铺满卡片内容宽，高 = 宽 + 抽数条。
/// 卡片变宽（「每页卡池数」选得少）时方块跟着放大，右侧不再留一大片空白。
/// </summary>
/// <remarks>
/// <para>每行个数取所在卡片的 <see cref="IGachaStatsDragCard.CompactColumns"/>（0 = 自动）。设置变化时页面整卡重建，所以第一次测量时取一次即可。</para>
/// <para>方块模板不写死宽高，由本面板按算好的尺寸测量与排列。记录数有限，与原来的 WrapPanel 一样不做虚拟化。</para>
/// </remarks>
public sealed partial class GachaTilePanel : Panel
{

    /// <summary>所在卡片设置的每行个数；找到卡片前为 null（下次测量再找）。</summary>
    private int? _requestedColumns;


    /// <summary>按列数与方块尺寸测量子元素，返回占满可用宽度、按行数累加的高度。</summary>
    /// <param name="availableSize">可用尺寸；宽度为卡片记录区的内容宽（外层 ScrollViewer 禁用了横向滚动）。</param>
    /// <returns>期望尺寸。</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        (int columns, double itemWidth) = GachaTileLayout.Calculate(availableSize.Width, GetRequestedColumns());
        var itemSize = new Size(itemWidth, itemWidth + GachaTileLayout.ExtraHeight);
        int count = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(itemSize);
            if (child.Visibility == Visibility.Visible)
            {
                count++;
            }
        }
        int rows = (count + columns - 1) / columns;
        double height = rows == 0 ? 0 : rows * itemSize.Height + (rows - 1) * GachaTileLayout.Spacing;
        double width = double.IsInfinity(availableSize.Width)
            ? columns * itemWidth + (columns - 1) * GachaTileLayout.Spacing
            : availableSize.Width;
        return new Size(width, height);
    }


    /// <summary>按行优先把可见子元素排进网格；隐藏的子元素排成零尺寸。</summary>
    /// <param name="finalSize">最终尺寸。</param>
    /// <returns>最终尺寸。</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        (int columns, double itemWidth) = GachaTileLayout.Calculate(finalSize.Width, GetRequestedColumns());
        double itemHeight = itemWidth + GachaTileLayout.ExtraHeight;
        int index = 0;
        foreach (UIElement child in Children)
        {
            if (child.Visibility != Visibility.Visible)
            {
                child.Arrange(default);
                continue;
            }
            int row = index / columns;
            int column = index % columns;
            child.Arrange(new Rect(column * (itemWidth + GachaTileLayout.Spacing), row * (itemHeight + GachaTileLayout.Spacing), itemWidth, itemHeight));
            index++;
        }
        return finalSize;
    }


    /// <summary>沿视觉树向上找所在卡片，读出它的每行个数设置；找不到时按自动处理且下次再找。</summary>
    /// <returns>每行个数，0 = 自动。</returns>
    private int GetRequestedColumns()
    {
        if (_requestedColumns is int cached)
        {
            return cached;
        }
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is IGachaStatsDragCard card)
            {
                _requestedColumns = card.CompactColumns;
                return card.CompactColumns;
            }
        }
        return 0;
    }

}
