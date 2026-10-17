using Starward.Features.Gacha;
using System.Collections.Generic;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 抽卡统计：小保底「不歪」次数、连续不歪 / 连续歪、下一次是否大保底（<see cref="GachaLogService.CountFiftyFiftyNoUp"/>）。
/// </summary>
public class GachaFiftyFiftyTests
{

    private static GachaLogItemEx Top(bool up, int rank = 5) => new() { RankType = rank, IsUp = up };

    private static GachaLogItemEx Lower(int rank) => new() { RankType = rank };


    private static (int Count, int NoUpCount, int MaxUpStreak, int MaxMissStreak, bool IsNextGuaranteed) Count(IEnumerable<GachaLogItemEx> items, int highestRankType = 5)
    {
        return GachaLogService.CountFiftyFiftyNoUp(items, highestRankType);
    }



    [Fact]
    public void Empty_AllZero()
    {
        Assert.Equal((0, 0, 0, 0, false), Count([]));
    }


    [Fact]
    public void AllUp_EveryPullCounted()
    {
        Assert.Equal((3, 3, 3, 0, false), Count([Top(true), Top(true), Top(true)]));
    }


    [Fact]
    public void PullAfterMiss_IsGuaranteedAndNotCounted()
    {
        // 歪 → 大保底（不计入，哪怕标了 UP）→ 小保底不歪
        Assert.Equal((2, 1, 1, 1, false), Count([Top(false), Top(true), Top(true)]));
    }


    [Fact]
    public void GuaranteedPull_DoesNotBreakStreaks()
    {
        // 不歪、不歪、歪、（大保底）、歪、（大保底）、不歪
        var items = new[] { Top(true), Top(true), Top(false), Top(true), Top(false), Top(true), Top(true) };
        Assert.Equal((5, 3, 2, 2, false), Count(items));
    }


    [Fact]
    public void EndsWithMiss_NextIsGuaranteed()
    {
        Assert.Equal((2, 1, 1, 1, true), Count([Top(true), Top(false)]));
    }


    [Fact]
    public void LowerRanks_Ignored()
    {
        Assert.Equal((1, 1, 1, 0, false), Count([Lower(4), Lower(3), Top(true), Lower(4)]));
    }


    [Fact]
    public void ZenlessZoneZero_UsesRankFourAsHighest()
    {
        // 绝区零 S 级的 RankType 是 4，A 级是 3
        Assert.Equal((2, 1, 1, 1, true), Count([Top(true, rank: 4), Lower(3), Top(false, rank: 4)], highestRankType: 4));
    }

}
