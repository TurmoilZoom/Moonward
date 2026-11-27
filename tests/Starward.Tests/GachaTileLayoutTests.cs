using Starward.Features.Gacha;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 紧凑视图图标方块排列（<see cref="GachaTileLayout"/>）：按设置的个数等宽铺满一行，
/// 放不下（方块窄于 36）时自动少放；未设置时每行 5 个。
/// </summary>
public class GachaTileLayoutTests
{

    /// <summary>卡片默认宽度 262 的内容宽 238：未设置时每行 5 个，方块约 44，与改动前一致。</summary>
    [Fact]
    public void Default_DefaultCardWidth_KeepsFivePerRow()
    {
        (int columns, double width) = GachaTileLayout.Calculate(238, 0);
        Assert.Equal(5, columns);
        Assert.Equal(44.4, width, 3);
    }


    /// <summary>按个数均分，卡片变宽时方块跟着拉宽，个数不变。</summary>
    [Theory]
    [InlineData(238, 4, 4, 56.5)]
    [InlineData(238, 6, 6, 36.333)]
    [InlineData(323, 5, 5, 61.4)]
    [InlineData(517, 5, 5, 100.2)]
    public void Requested_SplitsRowEvenly(double available, int requested, int expectedColumns, double expectedWidth)
    {
        (int columns, double width) = GachaTileLayout.Calculate(available, requested);
        Assert.Equal(expectedColumns, columns);
        Assert.Equal(expectedWidth, width, 3);
    }


    /// <summary>指定个数放不下（方块会窄于 36）时减到放得下的最大个数。</summary>
    [Fact]
    public void Requested_TooMany_FallsBackToMaxThatFits()
    {
        (int columns, double width) = GachaTileLayout.Calculate(238, 10);
        Assert.Equal(6, columns);
        Assert.True(width >= GachaTileLayout.MinItemWidth);
    }


    /// <summary>刚好放得下时不因浮点误差少一列：6×36 + 5×4 = 236。</summary>
    [Fact]
    public void Requested_ExactFit_NotLosingColumn()
    {
        (int columns, double width) = GachaTileLayout.Calculate(236, 6);
        Assert.Equal(6, columns);
        Assert.Equal(36, width, 3);
    }


    /// <summary>极窄时至少一列；宽度未约束时按原固定尺寸。</summary>
    [Theory]
    [InlineData(30, 0, 1, 30)]
    [InlineData(double.PositiveInfinity, 0, 5, 44)]
    [InlineData(double.PositiveInfinity, 7, 7, 44)]
    [InlineData(0, 0, 5, 44)]
    public void EdgeCases(double available, int requested, int expectedColumns, double expectedWidth)
    {
        (int columns, double width) = GachaTileLayout.Calculate(available, requested);
        Assert.Equal(expectedColumns, columns);
        Assert.Equal(expectedWidth, width, 3);
    }

}
