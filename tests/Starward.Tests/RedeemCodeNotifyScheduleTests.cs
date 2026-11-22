using Starward.Features.RedeemCode;
using System;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 兑换码通知的纯计算（<see cref="RedeemCodeNotifySchedule"/>）：东八区时间解析、直播阶段、下次检查时刻、补发窗口、新码判定与记录序列化。
/// 时间取自绝区零 3.4 前瞻实测（start 18:00、end 19:45，唯一的码 18:56:30 放出）。
/// </summary>
public class RedeemCodeNotifyScheduleTests
{

    private static readonly DateTimeOffset Start = new(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(8));

    private static readonly DateTimeOffset End = new(2026, 11, 20, 19, 45, 0, TimeSpan.FromHours(8));


    [Fact]
    public void ParseChinaTime_TreatsWallClockAsUtcPlus8()
    {
        DateTimeOffset? parsed = RedeemCodeNotifySchedule.ParseChinaTime("2026-11-20 18:00:00");
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), parsed);
        Assert.Equal(TimeSpan.FromHours(8), parsed!.Value.Offset);
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a time")]
    public void ParseChinaTime_InvalidIsNull(string? text)
    {
        Assert.Null(RedeemCodeNotifySchedule.ParseChinaTime(text));
    }


    [Fact]
    public void GetPhase_FollowsStartEndAndTail()
    {
        Assert.Equal(RedeemLivePhase.Upcoming, RedeemCodeNotifySchedule.GetPhase(Start, End, Start.AddDays(-4)));
        Assert.Equal(RedeemLivePhase.Upcoming, RedeemCodeNotifySchedule.GetPhase(Start, End, Start.AddSeconds(-1)));
        Assert.Equal(RedeemLivePhase.Live, RedeemCodeNotifySchedule.GetPhase(Start, End, Start));
        Assert.Equal(RedeemLivePhase.Live, RedeemCodeNotifySchedule.GetPhase(Start, End, new DateTimeOffset(2026, 11, 20, 18, 56, 30, TimeSpan.FromHours(8))));
        Assert.Equal(RedeemLivePhase.Live, RedeemCodeNotifySchedule.GetPhase(Start, End, End + RedeemCodeNotifySchedule.LiveTail));
        Assert.Equal(RedeemLivePhase.Ended, RedeemCodeNotifySchedule.GetPhase(Start, End, End + RedeemCodeNotifySchedule.LiveTail + TimeSpan.FromSeconds(1)));
    }


    [Fact]
    public void GetPhase_WithoutEndUsesDefaultDuration()
    {
        DateTimeOffset cutoff = Start + RedeemCodeNotifySchedule.DefaultLiveDuration + RedeemCodeNotifySchedule.LiveTail;
        Assert.Equal(RedeemLivePhase.Live, RedeemCodeNotifySchedule.GetPhase(Start, null, cutoff));
        Assert.Equal(RedeemLivePhase.Ended, RedeemCodeNotifySchedule.GetPhase(Start, null, cutoff.AddSeconds(1)));
    }


    [Fact]
    public void GetPhase_WithoutStartIsUnknown()
    {
        Assert.Equal(RedeemLivePhase.Unknown, RedeemCodeNotifySchedule.GetPhase(null, End, End));
        Assert.Equal(RedeemLivePhase.Unknown, RedeemCodeNotifySchedule.GetPhase(null, null, End));
    }


    [Fact]
    public void GetNextCheck_PollsDuringLive()
    {
        DateTimeOffset now = Start.AddMinutes(10);
        Assert.Equal(now + RedeemCodeNotifySchedule.LivePollInterval, RedeemCodeNotifySchedule.GetNextCheck(RedeemLivePhase.Live, Start, now));
    }


    [Fact]
    public void GetNextCheck_UpcomingSleepsUntilStartButNoLongerThanDiscoveryInterval()
    {
        DateTimeOffset soon = Start.AddHours(-1);
        Assert.Equal(Start, RedeemCodeNotifySchedule.GetNextCheck(RedeemLivePhase.Upcoming, Start, soon));

        DateTimeOffset early = Start.AddDays(-2);
        Assert.Equal(early + RedeemCodeNotifySchedule.DiscoveryInterval, RedeemCodeNotifySchedule.GetNextCheck(RedeemLivePhase.Upcoming, Start, early));
    }


    // 枚举是 internal，公开的测试方法不能拿它当参数，用 int 转
    [Theory]
    [InlineData((int)RedeemLivePhase.Ended)]
    [InlineData((int)RedeemLivePhase.Unknown)]
    public void GetNextCheck_IdleUsesDiscoveryInterval(int phase)
    {
        DateTimeOffset now = End.AddDays(1);
        Assert.Equal(now + RedeemCodeNotifySchedule.DiscoveryInterval, RedeemCodeNotifySchedule.GetNextCheck((RedeemLivePhase)phase, Start, now));
    }


    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 5)]
    [InlineData(1.0, 10)]
    [InlineData(-1.0, 0)]
    [InlineData(2.0, 10)]
    public void AddJitter_DelaysUpToMaxJitter(double sample, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(10), RedeemCodeNotifySchedule.MaxJitter);
        Assert.Equal(Start.AddSeconds(expectedSeconds), RedeemCodeNotifySchedule.AddJitter(Start, sample));
    }


    [Fact]
    public void IsRecent_CoversUpcomingLiveAndFreshWindowOnly()
    {
        TimeSpan window = RedeemCodeNotifySchedule.NotifyFreshWindow;
        Assert.True(RedeemCodeNotifySchedule.IsRecent(Start, End, Start.AddDays(-1), window));
        Assert.True(RedeemCodeNotifySchedule.IsRecent(Start, End, Start.AddMinutes(30), window));
        Assert.True(RedeemCodeNotifySchedule.IsRecent(Start, End, End + window, window));
        Assert.False(RedeemCodeNotifySchedule.IsRecent(Start, End, End + window + TimeSpan.FromSeconds(1), window));
    }


    [Fact]
    public void IsRecent_WithoutAnyTimeIsFalse()
    {
        Assert.False(RedeemCodeNotifySchedule.IsRecent(null, null, End, RedeemCodeNotifySchedule.NotifyFreshWindow));
    }


    [Fact]
    public void ShouldNotify_MergedWaitsForExpectedCountDuringLive()
    {
        // 原神 / 星铁预设 3 个：直播中放出第 1、2 个都不发，第 3 个时合成一条
        Assert.False(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Live, releasedCount: 1, expectedCount: 3, newCount: 1));
        Assert.False(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Live, releasedCount: 2, expectedCount: 3, newCount: 2));
        Assert.True(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Live, releasedCount: 3, expectedCount: 3, newCount: 3));
        // 绝区零预设 1 个：放出就发
        Assert.True(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Live, releasedCount: 1, expectedCount: 1, newCount: 1));
    }


    [Fact]
    public void ShouldNotify_NothingNewNeverNotifies()
    {
        Assert.False(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Live, releasedCount: 3, expectedCount: 3, newCount: 0));
        Assert.False(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Ended, releasedCount: 3, expectedCount: 3, newCount: 0));
    }


    [Fact]
    public void ShouldNotify_MoreThanExpectedNotifiesAgain()
    {
        // 预设偏少：发过 3 个之后又多出第 4 个
        Assert.True(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Live, releasedCount: 4, expectedCount: 3, newCount: 1));
    }


    [Theory]
    [InlineData((int)RedeemLivePhase.Ended)]
    [InlineData((int)RedeemLivePhase.Unknown)]
    public void ShouldNotify_AfterLiveWindowSendsWhatever(int phase)
    {
        // 预设偏多（只放了 2 个），或直播时没开 Moonward：直播窗口过了就不再等
        Assert.True(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, (RedeemLivePhase)phase, releasedCount: 2, expectedCount: 3, newCount: 2));
    }


    [Fact]
    public void ShouldNotify_ExpectedBelowOneTreatedAsOne()
    {
        Assert.True(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Merged, RedeemLivePhase.Live, releasedCount: 1, expectedCount: 0, newCount: 1));
    }


    [Fact]
    public void ShouldNotify_EachCodeNotifiesEveryNewCode()
    {
        Assert.True(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.EachCode, RedeemLivePhase.Live, releasedCount: 1, expectedCount: 3, newCount: 1));
        Assert.True(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.EachCode, RedeemLivePhase.Live, releasedCount: 2, expectedCount: 3, newCount: 1));
        Assert.False(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.EachCode, RedeemLivePhase.Live, releasedCount: 2, expectedCount: 3, newCount: 0));
    }


    [Fact]
    public void ShouldNotify_OffNeverNotifies()
    {
        Assert.False(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Off, RedeemLivePhase.Live, releasedCount: 3, expectedCount: 3, newCount: 3));
        Assert.False(RedeemCodeNotifySchedule.ShouldNotify(RedeemCodeNotifyMode.Off, RedeemLivePhase.Ended, releasedCount: 3, expectedCount: 3, newCount: 3));
    }


    [Fact]
    public void GetNewCodes_DifferentActTreatsAllAsNew()
    {
        var notified = RedeemCodeNotifySchedule.ParseNotified("ea_old|AAA,BBB");
        Assert.Equal(["AAA", "CCC"], RedeemCodeNotifySchedule.GetNewCodes("ea_new", ["AAA", "CCC"], notified));
    }


    [Fact]
    public void GetNewCodes_SameActSkipsNotifiedIgnoringCase()
    {
        var notified = RedeemCodeNotifySchedule.ParseNotified("ea1|WANZHOU1202");
        Assert.Empty(RedeemCodeNotifySchedule.GetNewCodes("ea1", ["wanzhou1202"], notified));
        Assert.Equal(["SECOND"], RedeemCodeNotifySchedule.GetNewCodes("ea1", ["WANZHOU1202", "SECOND"], notified));
    }


    [Fact]
    public void GetNewCodes_DropsBlankAndDuplicate()
    {
        var notified = RedeemCodeNotifySchedule.ParseNotified(null);
        Assert.Equal(["A1", "B2"], RedeemCodeNotifySchedule.GetNewCodes("ea1", ["A1", "", "  ", "a1", "B2"], notified));
    }


    [Fact]
    public void Notified_RoundTrips()
    {
        string value = RedeemCodeNotifySchedule.SerializeNotified("ea202611191659397698", ["WANZHOU1202", "SECOND"]);
        Assert.Equal("ea202611191659397698|WANZHOU1202,SECOND", value);

        var parsed = RedeemCodeNotifySchedule.ParseNotified(value);
        Assert.Equal("ea202611191659397698", parsed.ActId);
        Assert.Equal(["WANZHOU1202", "SECOND"], parsed.Codes);
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("|AAA")]
    public void ParseNotified_InvalidIsEmpty(string? value)
    {
        var parsed = RedeemCodeNotifySchedule.ParseNotified(value);
        Assert.Null(parsed.ActId);
        Assert.Empty(parsed.Codes);
    }


    [Fact]
    public void ParseNotified_ActWithoutCodes()
    {
        var parsed = RedeemCodeNotifySchedule.ParseNotified("ea1|");
        Assert.Equal("ea1", parsed.ActId);
        Assert.Empty(parsed.Codes);
    }

}
