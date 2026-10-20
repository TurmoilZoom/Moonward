using Starward.Features.Gacha.ZZZGachaToolbox;
using System;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 定时提交绝区零抽卡元数据的排期（<see cref="ZZZGachaMetadataPublishSchedule"/>）：按 UTC+8 时刻划期、过点补跑、重试不越过下次到点。
/// </summary>
public class ZZZGachaMetadataPublishScheduleTests
{

    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    /// <summary>UTC+8 下的时刻，换成比较用的 UTC。</summary>
    private static DateTimeOffset Cst(int month, int day, int hour, int minute = 0) => new DateTimeOffset(2026, month, day, hour, minute, 0, TimeSpan.FromHours(8)).ToUniversalTime();



    [Fact]
    public void Period_BeforeTime_BelongsToPreviousDay()
    {
        Assert.Equal(new DateOnly(2026, 10, 19), ZZZGachaMetadataPublishSchedule.GetCurrentPeriod(Cst(10, 20, 11, 59), Noon));
        Assert.Equal(new DateOnly(2026, 10, 20), ZZZGachaMetadataPublishSchedule.GetCurrentPeriod(Cst(10, 20, 12, 0), Noon));
    }


    [Fact]
    public void Period_UsesUtcPlus8_NotLocalOrUtc()
    {
        // UTC 10-20 04:30 = UTC+8 10-20 12:30，已过 12:00
        var utc = new DateTimeOffset(2026, 10, 20, 4, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 10, 20), ZZZGachaMetadataPublishSchedule.GetCurrentPeriod(utc, Noon));
        // 同一时刻换成 UTC+9 表示，结果不变
        Assert.Equal(new DateOnly(2026, 10, 20), ZZZGachaMetadataPublishSchedule.GetCurrentPeriod(utc.ToOffset(TimeSpan.FromHours(9)), Noon));
    }


    [Fact]
    public void Due_NeverSucceeded_RunsNow()
    {
        DateTimeOffset now = Cst(10, 20, 8);
        Assert.True(ZZZGachaMetadataPublishSchedule.IsDue(now, Noon, null));
        Assert.Equal(now, ZZZGachaMetadataPublishSchedule.GetNextDue(now, Noon, null));
    }


    [Fact]
    public void Due_PassedTimeWithoutSuccess_CatchesUp()
    {
        // 昨天那期成功过，今天 12:00 时软件没开，15:00 启动时补跑
        DateTimeOffset now = Cst(10, 20, 15);
        Assert.Equal(now, ZZZGachaMetadataPublishSchedule.GetNextDue(now, Noon, new DateOnly(2026, 10, 19)));
    }


    [Fact]
    public void Due_SucceededThisPeriod_WaitsForNextSlot()
    {
        DateTimeOffset now = Cst(10, 20, 15);
        Assert.False(ZZZGachaMetadataPublishSchedule.IsDue(now, Noon, new DateOnly(2026, 10, 20)));
        Assert.Equal(Cst(10, 21, 12), ZZZGachaMetadataPublishSchedule.GetNextDue(now, Noon, new DateOnly(2026, 10, 20)));
    }


    [Fact]
    public void Due_BeforeTodaysSlot_WaitsForToday()
    {
        // 早上 8 点，昨天那期已成功：等今天 12:00
        DateTimeOffset now = Cst(10, 20, 8);
        Assert.Equal(Cst(10, 20, 12), ZZZGachaMetadataPublishSchedule.GetNextDue(now, Noon, new DateOnly(2026, 10, 19)));
    }


    [Fact]
    public void NextSlot_ExactlyAtTime_IsTomorrow()
    {
        Assert.Equal(Cst(10, 21, 12), ZZZGachaMetadataPublishSchedule.GetNextSlot(Cst(10, 20, 12), Noon));
    }


    [Fact]
    public void Retry_DoesNotPassNextSlot()
    {
        Assert.Equal(Cst(10, 20, 13), ZZZGachaMetadataPublishSchedule.GetRetry(Cst(10, 20, 12), Noon, TimeSpan.FromHours(1)));
        // 次日 11:30 失败、2 小时后重试会越过 12:00：改在 12:00 跑新的一期
        Assert.Equal(Cst(10, 21, 12), ZZZGachaMetadataPublishSchedule.GetRetry(Cst(10, 21, 11, 30), Noon, TimeSpan.FromHours(2)));
    }


    [Fact]
    public void RetryDelay_BacksOffThenStaysHourly()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), ZZZGachaMetadataPublishSchedule.GetRetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(5), ZZZGachaMetadataPublishSchedule.GetRetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(30), ZZZGachaMetadataPublishSchedule.GetRetryDelay(4));
        Assert.Equal(TimeSpan.FromHours(1), ZZZGachaMetadataPublishSchedule.GetRetryDelay(5));
        Assert.Equal(TimeSpan.FromHours(1), ZZZGachaMetadataPublishSchedule.GetRetryDelay(50));
    }


    [Fact]
    public void FromMinutes_OutOfRange_FallsBackToDefault()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), ZZZGachaMetadataPublishSchedule.FromMinutes(90));
        Assert.Equal(ZZZGachaMetadataPublishSchedule.DefaultTimeOfDay, ZZZGachaMetadataPublishSchedule.FromMinutes(-1));
        Assert.Equal(ZZZGachaMetadataPublishSchedule.DefaultTimeOfDay, ZZZGachaMetadataPublishSchedule.FromMinutes(24 * 60));
    }

}
