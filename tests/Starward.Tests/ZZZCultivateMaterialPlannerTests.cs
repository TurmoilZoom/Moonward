using Starward.Core.GameRecord.ZZZ.UpgradeGuide;
using Starward.Features.GameRecord.ZZZ;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 养成材料逐阶缺口计算。数据取自 2026-11-28 国服账号挽昼（含专武、优先技能 12 级）的预告结果。
/// </summary>
public class ZZZCultivateMaterialPlannerTests
{

    private static ZZZCultivateMaterial M(int id, string name, long count) => new() { Id = id, Name = name, Icon = "", Count = count };


    /// <summary>
    /// 挽昼全套：引燃芯片、锋御组件两条三阶材料线，加上丁尼与周本材料。
    /// </summary>
    private static ZZZCultivateCalcResult Sunbringer(long officialChipLack = 32) => new()
    {
        AvatarConsume = [M(10, "丁尼", 800000)],
        SkillConsume =
        [
            M(10, "丁尼", 1780000),
            M(100111, "基础引燃芯片", 25),
            M(100121, "进阶引燃芯片", 75),
            M(100131, "特化引燃芯片", 126),
            M(110012, "劫日凶刃", 9),
        ],
        WeaponConsume =
        [
            M(10, "丁尼", 400000),
            M(101016, "锋御组件", 4),
            M(101026, "增强型锋御组件", 32),
            M(101036, "特化型锋御组件", 30),
        ],
        UserOwnsMaterials = new Dictionary<string, long>
        {
            ["10"] = 9955105,
            ["100111"] = 2,
            ["100121"] = 155,
            ["100131"] = 70,
            ["101026"] = 105,
            ["101036"] = 25,
        },
        NeedGet =
        [
            M(10, "丁尼", 0),
            M(100131, "特化引燃芯片", officialChipLack),
            M(110012, "劫日凶刃", 9),
            M(101036, "特化型锋御组件", 0),
        ],
    };


    [Fact]
    public void ChipsAreCalculatedTierByTierWithUpwardSynthesis()
    {
        var lines = ZZZCultivateMaterialPlanner.Plan(Sunbringer()).ToDictionary(x => x.Id);

        // 基础：持有 2 不够 25，高阶不能拆，缺 23
        Assert.Equal(23, lines[100111].Lack);
        Assert.Equal(0, lines[100111].Synthesized);
        // 进阶：155 够 75，多出 80 → 合成 26 个特化
        Assert.Equal(0, lines[100121].Lack);
        // 特化：70 + 合成 26 = 96，仍缺 30
        Assert.Equal(26, lines[100131].Synthesized);
        Assert.Equal(30, lines[100131].Lack);
        Assert.All(new[] { 100111, 100121, 100131 }, id => Assert.True(lines[id].HasStatus));
    }


    [Fact]
    public void LowerTierShortageIsNotHiddenByHigherTierSurplus()
    {
        var lines = ZZZCultivateMaterialPlanner.Plan(Sunbringer()).ToDictionary(x => x.Id);

        // 官方折算为 0（高阶富余抵掉了低阶），但锋御组件一个没有，确实缺 4
        Assert.Equal(4, lines[101016].Lack);
        Assert.Equal(0, lines[101026].Lack);
        // 增强型多出 73 → 合成 24，特化型只需补 5
        Assert.Equal(5, lines[101036].Synthesized);
        Assert.Equal(0, lines[101036].Lack);
    }


    [Fact]
    public void FallsBackToOfficialValueWhenFoldingDisagrees()
    {
        var lines = ZZZCultivateMaterialPlanner.Plan(Sunbringer(officialChipLack: 99)).ToDictionary(x => x.Id);

        Assert.False(lines[100111].HasStatus);
        Assert.False(lines[100121].HasStatus);
        Assert.True(lines[100131].HasStatus);
        Assert.Equal(99, lines[100131].Lack);
        Assert.Equal(0, lines[100131].Synthesized);
    }


    [Fact]
    public void NonTieredMaterialsUseOfficialOrDirectComparison()
    {
        var lines = ZZZCultivateMaterialPlanner.Plan(Sunbringer()).ToDictionary(x => x.Id);

        Assert.Equal(2980000, lines[10].Need);
        Assert.Equal(0, lines[10].Lack);
        Assert.Equal(0, lines[110012].Owned);
        Assert.Equal(9, lines[110012].Lack);
    }


    [Fact]
    public void LackingFamiliesComeFirstAndTiersStayTogether()
    {
        var ids = ZZZCultivateMaterialPlanner.Plan(Sunbringer()).Select(x => x.Id).ToList();

        // 丁尼不缺，排到缺材料的各组之后；同一材料线按阶相邻
        Assert.Equal([100111, 100121, 100131, 110012, 101016, 101026, 101036, 10], ids);
    }


    [Theory]
    [InlineData(100131, true, 100101, 3)]
    [InlineData(100217, true, 100207, 1)]
    [InlineData(101026, true, 101006, 2)]
    [InlineData(100941, false, 0, 0)]
    [InlineData(300003, false, 0, 0)]
    [InlineData(110012, false, 0, 0)]
    public void TierIsReadFromTensDigit(int id, bool expected, int family, int tier)
    {
        Assert.Equal(expected, ZZZCultivateMaterialPlanner.TryGetTier(id, out int actualFamily, out int actualTier));
        Assert.Equal(family, actualFamily);
        Assert.Equal(tier, actualTier);
    }

}
