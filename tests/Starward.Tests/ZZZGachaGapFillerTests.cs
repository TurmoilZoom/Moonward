using Starward.Features.Gacha;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 用官方抽卡统计补全绝区零缺失记录的算法测试。
/// </summary>
public class ZZZGachaGapFillerTests
{

    private static readonly TimeSpan Cst = TimeSpan.FromHours(8);

    private const int Suffix = 12;


    /// <summary>
    /// 按完整历史生成数据：<paramref name="sPositions"/> 为 S 级所在序号，<paramref name="localPositions"/> 为本地保留的序号。
    /// 每抽间隔 1 分钟；<paramref name="timeJumpAfter"/> 之后整体推迟 180 天，模拟两次同步之间的断档。
    /// </summary>
    private static (List<ZZZGapLocalRecord> Local, List<ZZZGapOfficialS> Official) Build(
        int total, int[] sPositions, IEnumerable<int> localPositions, int timeJumpAfter = int.MaxValue, long idBase = 1736596800000300000)
    {
        var start = new DateTime(2025, 1, 1, 12, 0, 0);
        DateTime TimeOf(int pos) => start.AddMinutes(pos).AddDays(pos > timeJumpAfter ? 180 : 0);
        // 真实 Id 与时间同序，每抽 +100；断档后 Id 也跳一大段
        long IdOf(int pos) => idBase + 100L * pos + (pos > timeJumpAfter ? 100_000_000 : 0);
        var sSet = sPositions.ToHashSet();
        var local = localPositions.OrderBy(x => x)
            .Select(p => new ZZZGapLocalRecord(IdOf(p), sSet.Contains(p) ? 1000 + p : 2000, sSet.Contains(p) ? 4 : 3, TimeOf(p)))
            .ToList();
        var official = new List<ZZZGapOfficialS>();
        int last = 0;
        foreach (int p in sPositions.OrderBy(x => x))
        {
            official.Add(new ZZZGapOfficialS(p, 1000 + p, new DateTimeOffset(TimeOf(p), Cst).ToUnixTimeSeconds(), p - last));
            last = p;
        }
        return (local, official);
    }


    private static ZZZGapFillResult Run(List<ZZZGapLocalRecord> local, List<ZZZGapOfficialS> official, int total, HashSet<long>? occupied = null)
    {
        return ZZZGachaGapFiller.Compute(local, official, total, Cst, Suffix, 1736000000000000000, occupied ?? local.Select(x => x.Id).ToHashSet());
    }


    [Fact]
    public void NothingMissing_WhenCountsEqual()
    {
        var (local, official) = Build(30, [15], Enumerable.Range(1, 30));
        Assert.Equal(ZZZGapFillStatus.NothingMissing, Run(local, official, 30).Status);
    }


    [Fact]
    public void LeadingGap_FillsPlaceholdersBeforeFirstRecord()
    {
        // 前 10 抽超出保留期：本地从第 11 抽开始，S 级在第 25 抽（垫 25）
        var (local, official) = Build(40, [25], Enumerable.Range(11, 30));
        var result = Run(local, official, 40);

        Assert.Equal(ZZZGapFillStatus.Filled, result.Status);
        Assert.Equal(10, result.MissingCount);
        Assert.Equal(0, result.SRankCount);
        Assert.All(result.Records, r => Assert.True(r.Id < local[0].Id && r.Id % 100 == Suffix));
        Assert.Equal(Enumerable.Range(1, 10).Select(x => (long)x), result.Records.Select(x => x.Position));
        // Id 严格递增，占位时间取后面最近的已知记录
        Assert.True(result.Records.Zip(result.Records.Skip(1)).All(p => p.First.Id < p.Second.Id));
        Assert.All(result.Records, r => Assert.Equal(local[0].Time, r.Time));
    }


    [Fact]
    public void LeadingGap_RestoresMissingSRankAtExactPosition()
    {
        // 缺失的前 20 抽里有一个 S 级（第 6 抽），本地第一个 S 级在第 30 抽（垫 24）
        var (local, official) = Build(50, [6, 30], Enumerable.Range(21, 30));
        var result = Run(local, official, 50);

        Assert.Equal(ZZZGapFillStatus.Filled, result.Status);
        Assert.Equal(20, result.MissingCount);
        var s = Assert.Single(result.Records, r => r.IsSRank);
        Assert.Equal(6, s.Position);
        Assert.Equal(1006, s.ItemId);
        Assert.Equal(new DateTime(2025, 1, 1, 12, 6, 0), s.Time);
    }


    [Fact]
    public void MiddleGap_PlacesPlaceholdersAtTimeBreak()
    {
        // 本地有 1~20 和 41~60，中间 21~40 两次同步之间丢了；S 级在第 15、50 抽
        var (local, official) = Build(60, [15, 50], Enumerable.Range(1, 20).Concat(Enumerable.Range(41, 20)), timeJumpAfter: 40);
        var result = Run(local, official, 60);

        Assert.Equal(ZZZGapFillStatus.Filled, result.Status);
        Assert.Equal(20, result.MissingCount);
        Assert.Equal(Enumerable.Range(21, 20).Select(x => (long)x), result.Records.Select(x => x.Position));
        long before = local.First(x => x.Time.Minute == 20).Id;
        long after = local.First(x => x.Time.Day > 1).Id;
        Assert.All(result.Records, r => Assert.True(r.Id > before && r.Id < after && r.Id % 100 == Suffix));
    }


    [Fact]
    public void MiddleGap_InsertsMissingSRankByTime()
    {
        // 丢失的 21~40 里有 S 级（第 30 抽）；本地下一个 S 级在第 55 抽（垫 25）
        var (local, official) = Build(60, [10, 30, 55], Enumerable.Range(1, 20).Concat(Enumerable.Range(41, 20)), timeJumpAfter: 25);
        var result = Run(local, official, 60);

        Assert.Equal(ZZZGapFillStatus.Filled, result.Status);
        Assert.Equal(20, result.MissingCount);
        var s = Assert.Single(result.Records, r => r.IsSRank);
        Assert.Equal(30, s.Position);
    }


    [Fact]
    public void LocalBehind_WhenLastLocalSIsNotTheLastPull()
    {
        // 本地最后一条是第 40 抽的 S 级，官方却已有 45 抽：没同步完
        var (local, official) = Build(45, [40], Enumerable.Range(1, 40));
        Assert.Equal(ZZZGapFillStatus.LocalBehind, Run(local, official, 45).Status);
    }


    [Fact]
    public void LocalBehind_WhenOfficialHasNewerSRank()
    {
        // 官方第 44 抽出了 S 级，比本地最后一条（第 40 抽）还新
        var (local, official) = Build(45, [20, 44], Enumerable.Range(1, 40));
        Assert.Equal(ZZZGapFillStatus.LocalBehind, Run(local, official, 45).Status);
    }


    [Fact]
    public void LocalBehind_WhenGapIsAfterLastSRank()
    {
        // 最后一个 S 级之后少了 5 抽：可能是刚抽的还没同步到，不能当成老记录补
        var (local, official) = Build(35, [15], Enumerable.Range(1, 20).Concat(Enumerable.Range(26, 10)), timeJumpAfter: 25);
        Assert.Equal(ZZZGapFillStatus.LocalBehind, Run(local, official, 35).Status);
    }


    [Fact]
    public void LocalBehind_WhenNoSRankAndLocalExists()
    {
        // 整个频段都没出过 S 级，差额无法定位
        var (local, official) = Build(36, [], Enumerable.Range(1, 10));
        Assert.Equal(ZZZGapFillStatus.LocalBehind, Run(local, official, 36).Status);
    }


    /// <summary>本地最新一条记录之后 <paramref name="after"/> 的 UTC 时间。</summary>
    private static DateTime UtcAfterLast(List<ZZZGapLocalRecord> local, TimeSpan after) => DateTime.SpecifyKind(local[^1].Time - Cst + after, DateTimeKind.Utc);


    [Fact]
    public void OfficialBehind_WhenLocalHasMoreRecordsRightAfterPulling()
    {
        // 刚抽的 5 抽已同步到本地，官方统计（约 1 小时延迟）还停在第 25 抽
        var (local, official) = Build(30, [15], Enumerable.Range(1, 30));
        var result = ZZZGachaGapFiller.Compute(local, official, 25, Cst, Suffix, 1736000000000000000, local.Select(x => x.Id).ToHashSet(), UtcAfterLast(local, TimeSpan.FromMinutes(10)));
        Assert.Equal(ZZZGapFillStatus.OfficialBehind, result.Status);
    }


    [Fact]
    public void OfficialBehind_WhenNewSRankNotInStatsYet()
    {
        // 第 38 抽刚出的 S 级本地已有，官方统计只到第 35 抽
        var (local, official) = Build(40, [15, 38], Enumerable.Range(1, 40));
        var lagging = official.Where(x => x.Sort <= 35).ToList();
        var result = ZZZGachaGapFiller.Compute(local, lagging, 35, Cst, Suffix, 1736000000000000000, local.Select(x => x.Id).ToHashSet(), UtcAfterLast(local, TimeSpan.FromMinutes(30)));
        Assert.Equal(ZZZGapFillStatus.OfficialBehind, result.Status);
    }


    [Fact]
    public void Inconsistent_WhenMismatchPersistsAfterDelayWindow()
    {
        // 最新一条已是 3 小时前，官方仍对不上：不再归为延迟
        var (local, official) = Build(30, [15], Enumerable.Range(1, 30));
        var result = ZZZGachaGapFiller.Compute(local, official, 25, Cst, Suffix, 1736000000000000000, local.Select(x => x.Id).ToHashSet(), UtcAfterLast(local, TimeSpan.FromHours(3)));
        Assert.Equal(ZZZGapFillStatus.Inconsistent, result.Status);
    }


    [Fact]
    public void Inconsistent_WhenLocalHasMoreRecords()
    {
        var (local, official) = Build(30, [15], Enumerable.Range(1, 30));
        Assert.Equal(ZZZGapFillStatus.Inconsistent, Run(local, official, 25).Status);
    }


    [Fact]
    public void Inconsistent_WhenLocalSRankNotInOfficial()
    {
        var (local, official) = Build(40, [25], Enumerable.Range(11, 30));
        official[0] = official[0] with { ItemId = 9999 };
        Assert.Equal(ZZZGapFillStatus.Inconsistent, Run(local, official, 40).Status);
    }


    [Fact]
    public void Inconsistent_WhenPityDoesNotMatch()
    {
        // 官方说第 25 抽的 S 级垫了 20 抽（与序号推出的 25 不符，例如保底跨卡池继承），整频段放弃
        var (local, official) = Build(40, [25], Enumerable.Range(11, 30));
        official[0] = official[0] with { Count = 20 };
        Assert.Equal(ZZZGapFillStatus.Inconsistent, Run(local, official, 40).Status);
    }


    [Fact]
    public void NoIdSpace_WhenNeighborIdsTooClose()
    {
        // 两个 S 级之间丢了 5 抽，但两侧真实记录 Id 只差 100，放不下
        var (local, official) = Build(30, [10, 25], Enumerable.Range(1, 12).Concat(Enumerable.Range(18, 13)));
        var squeezed = local.Select((x, i) => x with { Id = 1736596800000300000 + 100L * i }).ToList();
        var result = Run(squeezed, official, 30);
        Assert.Equal(ZZZGapFillStatus.NoIdSpace, result.Status);
    }


    [Fact]
    public void NoIdSpace_WhenIdAlreadyOccupied()
    {
        var (local, official) = Build(40, [25], Enumerable.Range(11, 30));
        var occupied = local.Select(x => x.Id).ToHashSet();
        long expectedLast = local[0].Id - 100 + Suffix;
        occupied.Add(expectedLast);
        Assert.Equal(ZZZGapFillStatus.NoIdSpace, Run(local, official, 40, occupied).Status);
    }


    [Fact]
    public void EmptyLocal_FillsEverythingBelowUpperBound()
    {
        var (_, official) = Build(20, [8], []);
        var result = ZZZGachaGapFiller.Compute([], official, 20, Cst, Suffix, 1736000000000000000, new HashSet<long>());

        Assert.Equal(ZZZGapFillStatus.Filled, result.Status);
        Assert.Equal(20, result.MissingCount);
        Assert.Equal(8, Assert.Single(result.Records, r => r.IsSRank).Position);
        Assert.All(result.Records, r => Assert.True(r.Id < 1736000000000000000));
    }


    [Fact]
    public void DetectsTimeZoneFromLocalSRank()
    {
        // 美服 UTC-5：本地时间按 -5 存，官方给 Unix 时间；回退时区故意传 +8
        var (local, official) = Build(40, [25], Enumerable.Range(11, 30));
        var us = official.Select(x => x with { RecordTime = x.RecordTime + (long)TimeSpan.FromHours(13).TotalSeconds }).ToList();
        var result = ZZZGachaGapFiller.Compute(local, us, 40, Cst, Suffix, 1736000000000000000, local.Select(x => x.Id).ToHashSet());
        Assert.Equal(ZZZGapFillStatus.Filled, result.Status);
    }


    [Theory]
    [InlineData(1736596800000331700, 1, false)]
    [InlineData(1736596800000331712, 0, true)]
    [InlineData(1736596800000331700, 0, false)]
    [InlineData(1736596800000331712, 1, false)]
    public void IsFillRecord_RequiresZeroCountAndNonRoundId(long id, int count, bool expected)
    {
        Assert.Equal(expected, ZZZGachaGapFiller.IsFillRecord(id, count));
    }

}
