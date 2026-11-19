using Starward.Core;
using Starward.Core.GameRecord.Genshin.DailyNote;
using Starward.Core.GameRecord.StarRail.DailyNote;
using Starward.Features.GameRecord.DailyNote.Notify;
using System;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 便笺提醒：阈值判定、边沿触发、到点推算与下次检查时刻（<see cref="DailyNoteNotifyEvaluator"/>），以及从便笺取数值（<see cref="DailyNoteNotifyItems"/>）。
/// </summary>
public class DailyNoteNotifyTests
{

    [Theory]
    [InlineData(null, 200, 200)]
    [InlineData(0, 200, 200)]
    [InlineData(-5, 200, 200)]
    [InlineData(160, 200, 160)]
    [InlineData(200, 200, 200)]
    [InlineData(260, 200, 200)]
    public void ResolveThreshold_FallsBackToMax(int? configured, int max, int expected)
    {
        Assert.Equal(expected, DailyNoteNotifyEvaluator.ResolveThreshold(configured, max));
    }


    [Fact]
    public void Evaluate_NotifiesOnlyOnRisingEdge()
    {
        var full = new DailyNoteMetric(200, 200, TimeSpan.Zero);

        var first = DailyNoteNotifyEvaluator.Evaluate(full, null, notifiedBefore: false);
        Assert.True(first.Reached);
        Assert.True(first.ShouldNotify);

        var again = DailyNoteNotifyEvaluator.Evaluate(full, null, notifiedBefore: true);
        Assert.True(again.Reached);
        Assert.False(again.ShouldNotify);
    }


    [Fact]
    public void Evaluate_BelowThreshold_EstimatesTimeLinearly()
    {
        // 原神树脂 8 分钟一点：差 40 点回满 = 320 分钟
        var metric = new DailyNoteMetric(160, 200, TimeSpan.FromMinutes(320));

        var toFull = DailyNoteNotifyEvaluator.Evaluate(metric, null, notifiedBefore: true);
        Assert.False(toFull.Reached);
        Assert.False(toFull.ShouldNotify);
        Assert.Equal(TimeSpan.FromMinutes(320), toFull.TimeToThreshold);

        var to180 = DailyNoteNotifyEvaluator.Evaluate(metric, 180, notifiedBefore: false);
        Assert.Equal(180, to180.Threshold);
        Assert.Equal(TimeSpan.FromMinutes(160), to180.TimeToThreshold);
    }


    [Fact]
    public void Evaluate_AboveMax_CountsAsReached()
    {
        // 树脂可以用道具超过上限
        var metric = new DailyNoteMetric(230, 200, TimeSpan.Zero);
        Assert.True(DailyNoteNotifyEvaluator.Evaluate(metric, null, false).ShouldNotify);
    }


    [Fact]
    public void EstimateTimeToThreshold_NoRecoveryInfo_ReturnsNull()
    {
        var metric = new DailyNoteMetric(100, 200, null);
        Assert.Null(DailyNoteNotifyEvaluator.EstimateTimeToThreshold(metric, 200));
        Assert.Null(DailyNoteNotifyEvaluator.EstimateTimeToThreshold(metric with { TimeToFull = TimeSpan.Zero }, 200));
    }


    [Fact]
    public void GetNextCheck_UsesEarliestEtaPlusConfirmDelay()
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var next = DailyNoteNotifyEvaluator.GetNextCheck(now, now, TimeSpan.FromHours(1),
                                                         [TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(20)],
                                                         TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));
        Assert.Equal(now.AddMinutes(21), next);
    }


    [Fact]
    public void GetNextCheck_NoEta_UsesInterval()
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var next = DailyNoteNotifyEvaluator.GetNextCheck(now, now, TimeSpan.FromHours(1), [], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));
        Assert.Equal(now.AddHours(1), next);
    }


    [Fact]
    public void GetNextCheck_NeverEarlierThanMinGap()
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        // 快照是 10 分钟前拿的，推算 2 分钟后到点：按原始时刻早已过点，也不能立刻再请求
        var next = DailyNoteNotifyEvaluator.GetNextCheck(now.AddMinutes(-10), now, TimeSpan.FromHours(1),
                                                         [TimeSpan.FromMinutes(2)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));
        Assert.Equal(now.AddMinutes(3), next);
    }


    [Fact]
    public void GetMetric_MapsNoteFields()
    {
        var genshin = new GenshinDailyNote { CurrentResin = 120, MaxResin = 200, ResinRecoveryTime = TimeSpan.FromMinutes(640), MaxHomeCoin = 0 };
        Assert.Equal(new DailyNoteMetric(120, 200, TimeSpan.FromMinutes(640)), DailyNoteNotifyItems.GetMetric(DailyNoteNotifyItem.GenshinResin, genshin));
        // 尘歌壶未解锁
        Assert.Null(DailyNoteNotifyItems.GetMetric(DailyNoteNotifyItem.GenshinHomeCoin, genshin));
        // 项目与便笺不属于同一游戏
        Assert.Null(DailyNoteNotifyItems.GetMetric(DailyNoteNotifyItem.StarRailStamina, genshin));

        var starRail = new StarRailDailyNote { CurrentStamina = 300, MaxStamina = 300, StaminaRecoverTime = TimeSpan.Zero };
        Assert.Equal(new DailyNoteMetric(300, 300, TimeSpan.Zero), DailyNoteNotifyItems.GetMetric(DailyNoteNotifyItem.StarRailStamina, starRail));
    }


    [Fact]
    public void GetItems_StarRailOnlyStamina()
    {
        // 后备开拓力已移除：接口只给「是否已满」，没法设阈值，存满要很久不上线，提醒意义不大
        Assert.Equal([DailyNoteNotifyItem.StarRailStamina], DailyNoteNotifyItems.GetItems(new GameBiz("hkrpg_cn")));
    }

}
