using System;


namespace Starward.Features.Gacha;

/// <summary>
/// 紧凑视图图标方块的排列计算：按可用宽度与「每行图标数」设置算出列数与方块宽度，让方块等宽、一行正好铺满。
/// 纯计算、不依赖 WinUI，供紧凑视图面板（GachaTilePanel）与单元测试共用。
/// </summary>
internal static class GachaTileLayout
{

    /// <summary>方块之间的横向与纵向间距。</summary>
    public const double Spacing = 4;


    /// <summary>自动时方块的最小宽度（即原来固定的方块宽度）：先按它算一行放得下几个，再均分拉宽铺满。</summary>
    public const double AutoMinItemWidth = 44;


    /// <summary>指定每行个数时方块的最小宽度：再窄 UP 角标会盖住大半图标、抽数也挤，放不下就自动少放几个。</summary>
    public const double MinItemWidth = 36;


    /// <summary>方块高度比宽度多出的部分：图标与抽数条的间距 2 + 抽数条 16。</summary>
    public const double ExtraHeight = 18;


    /// <summary>可用宽度无效（未约束）时的列数，与原来卡片默认宽度下每行 5 个一致。</summary>
    private const int FallbackColumns = 5;


    /// <summary>
    /// 计算一行的列数与方块宽度。
    /// </summary>
    /// <param name="availableWidth">可用宽度（DIP）；无穷大或非正数时按原固定尺寸排。</param>
    /// <param name="requestedColumns">设置的每行个数；0 或负数 = 自动。</param>
    /// <returns>列数（至少 1）与方块宽度（DIP）。</returns>
    public static (int Columns, double ItemWidth) Calculate(double availableWidth, int requestedColumns)
    {
        if (!(availableWidth > 0) || double.IsInfinity(availableWidth))
        {
            return (requestedColumns > 0 ? requestedColumns : FallbackColumns, AutoMinItemWidth);
        }
        int columns;
        if (requestedColumns > 0)
        {
            columns = Math.Min(requestedColumns, FitColumns(availableWidth, MinItemWidth));
        }
        else
        {
            columns = FitColumns(availableWidth, AutoMinItemWidth);
        }
        double itemWidth = (availableWidth - Spacing * (columns - 1)) / columns;
        return (columns, itemWidth);
    }


    /// <summary>方块不窄于 <paramref name="minItemWidth"/> 时一行最多放几个（至少 1）。</summary>
    /// <param name="availableWidth">可用宽度。</param>
    /// <param name="minItemWidth">方块最小宽度。</param>
    /// <returns>列数。</returns>
    private static int FitColumns(double availableWidth, double minItemWidth)
    {
        // 加一个间距再除：n 个方块只有 n-1 个间距；留 0.01 容差，免得浮点误差让刚好放得下的少一列
        return Math.Max(1, (int)Math.Floor((availableWidth + Spacing + 0.01) / (minItemWidth + Spacing)));
    }

}
