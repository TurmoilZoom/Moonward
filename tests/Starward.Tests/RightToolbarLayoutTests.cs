using Starward.Features.GameLauncher;
using System.Linq;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 首页右侧功能栏排列（<see cref="RightToolbarLayout"/>）：设置值的解析与容错、序列化往返、隐藏、部分项重排。
/// </summary>
public class RightToolbarLayoutTests
{

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_Empty_IsDefault(string? value)
    {
        RightToolbarLayout layout = RightToolbarLayout.Parse(value);
        Assert.True(layout.IsDefault);
        Assert.Equal(RightToolbarLayout.DefaultOrder, layout.Order);
    }


    [Fact]
    public void SerializeThenParse_RoundTrips()
    {
        RightToolbarLayout layout = RightToolbarLayout.Parse("SignIn,DailyNote,-CloudGame,TimeNode,MapTool,-RedeemCode");
        Assert.Equal("SignIn,DailyNote,-CloudGame,TimeNode,MapTool,-RedeemCode", layout.Serialize());
        Assert.True(layout.IsHidden(RightToolbarItem.CloudGame));
        Assert.True(layout.IsHidden(RightToolbarItem.RedeemCode));
        Assert.False(layout.IsHidden(RightToolbarItem.SignIn));
        Assert.False(layout.IsDefault);
    }


    [Fact]
    public void Parse_SkipsUnknownAndDuplicates()
    {
        RightToolbarLayout layout = RightToolbarLayout.Parse("Removed,SignIn,-SignIn,3,-,DailyNote");
        Assert.True(layout.Order.ToList().IndexOf(RightToolbarItem.SignIn) < layout.Order.ToList().IndexOf(RightToolbarItem.DailyNote));
        // 第一次出现的是显示，后面重复的隐藏标记不算
        Assert.False(layout.IsHidden(RightToolbarItem.SignIn));
        Assert.Equal(RightToolbarLayout.DefaultOrder.Count, layout.Order.Count);
        Assert.Equal(RightToolbarLayout.DefaultOrder.OrderBy(x => x), layout.Order.OrderBy(x => x));
    }


    [Fact]
    public void Parse_InsertsMissingItemsAfterDefaultPredecessor()
    {
        // 设置里没有 MapTool（比如它是后来新增的）：默认顺序里它跟在 TimeNode 后面
        RightToolbarLayout layout = RightToolbarLayout.Parse("SignIn,TimeNode,DailyNote,CloudGame,RedeemCode");
        Assert.Equal(
        [
            RightToolbarItem.SignIn,
            RightToolbarItem.TimeNode,
            RightToolbarItem.MapTool,
            RightToolbarItem.DailyNote,
            RightToolbarItem.CloudGame,
            RightToolbarItem.RedeemCode,
        ], layout.Order);
        Assert.False(layout.IsHidden(RightToolbarItem.MapTool));
    }


    [Fact]
    public void Parse_MissingFirstDefaultItem_GoesToFront()
    {
        RightToolbarLayout layout = RightToolbarLayout.Parse("SignIn,DailyNote");
        Assert.Equal(RightToolbarItem.CloudGame, layout.Order[0]);
        Assert.Equal(RightToolbarItem.SignIn, layout.Order[1]);
        Assert.Equal(RightToolbarItem.DailyNote, layout.Order[2]);
    }


    [Fact]
    public void SetHidden_TogglesAndRestoresDefault()
    {
        RightToolbarLayout layout = RightToolbarLayout.CreateDefault();
        layout.SetHidden(RightToolbarItem.MapTool, true);
        Assert.True(layout.IsHidden(RightToolbarItem.MapTool));
        Assert.False(layout.IsDefault);
        layout.SetHidden(RightToolbarItem.MapTool, false);
        Assert.True(layout.IsDefault);
    }


    [Fact]
    public void ReorderSubset_KeepsOtherItemsInPlace()
    {
        // 崩坏3 只有便笺、兑换码、签到：重排它们，其余项的位置不变
        RightToolbarLayout layout = RightToolbarLayout.CreateDefault();
        layout.ReorderSubset([RightToolbarItem.SignIn, RightToolbarItem.DailyNote, RightToolbarItem.RedeemCode]);
        Assert.Equal(
        [
            RightToolbarItem.CloudGame,
            RightToolbarItem.SignIn,
            RightToolbarItem.TimeNode,
            RightToolbarItem.MapTool,
            RightToolbarItem.DailyNote,
            RightToolbarItem.RedeemCode,
        ], layout.Order);

        layout.ReorderSubset([RightToolbarItem.DailyNote, RightToolbarItem.RedeemCode, RightToolbarItem.SignIn]);
        Assert.True(layout.IsDefault);
    }


}
