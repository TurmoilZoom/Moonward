using Starward.Core;
using Starward.Features.PlayTime;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 游戏时长：心跳记录切成会话（<see cref="PlayTimeStatsService.GetPlayTimeStats"/>）。
/// 游戏正常退出时的保存和未正常退出时的归档都靠它扣掉电脑睡眠造成的空档。
/// </summary>
public class PlayTimeSessionTests
{

    private static readonly GameBiz Biz = "hk4e_cn";

    private const int Pid = 1234;

    private const long Second = 1000;

    /// <summary>任意基准时间（Unix 毫秒）。</summary>
    private const long T0 = 1_760_000_000_000;


    private static PlayTimeItemStruct Item(long timeStamp, PlayTimeState state, int pid = Pid)
    {
        return new PlayTimeItemStruct { TimeStamp = timeStamp, GameBiz = Biz, Pid = pid, State = state };
    }


    /// <summary>从 <paramref name="from"/> 到 <paramref name="to"/>（含）每 30 秒一条 Play 心跳，与记录进程的间隔一致。</summary>
    private static IEnumerable<PlayTimeItemStruct> Heartbeats(long from, long to, int pid = Pid)
    {
        for (long t = from; t <= to; t += 30 * Second)
        {
            yield return Item(t, PlayTimeState.Play, pid);
        }
    }


    private static List<PlayTimeStats> Sessions(IEnumerable<PlayTimeItemStruct> items)
    {
        return PlayTimeStatsService.GetPlayTimeStats(items.ToList()).OrderBy(x => x.StartTime).ToList();
    }



    [Fact]
    public void ContinuousHeartbeats_OneSessionEndedByStop()
    {
        var items = new List<PlayTimeItemStruct> { Item(T0, PlayTimeState.Start) };
        items.AddRange(Heartbeats(T0 + 30 * Second, T0 + 600 * Second));
        items.Add(Item(T0 + 610 * Second, PlayTimeState.Stop));

        var sessions = Sessions(items);

        var session = Assert.Single(sessions);
        Assert.Equal(T0, session.StartTime);
        Assert.Equal(T0 + 610 * Second, session.EndTime);
        Assert.False(session.Interruption);
    }


    [Fact]
    public void SleepGap_SplitsSessionAndExcludesGap()
    {
        var items = new List<PlayTimeItemStruct> { Item(T0, PlayTimeState.Start) };
        items.AddRange(Heartbeats(T0 + 30 * Second, T0 + 300 * Second));
        // 睡眠约 55 分钟，没有心跳
        items.AddRange(Heartbeats(T0 + 3600 * Second, T0 + 3900 * Second));
        items.Add(Item(T0 + 3910 * Second, PlayTimeState.Stop));

        var sessions = Sessions(items);

        Assert.Equal(2, sessions.Count);
        Assert.Equal((T0, T0 + 300 * Second, true), (sessions[0].StartTime, sessions[0].EndTime, sessions[0].Interruption));
        Assert.Equal((T0 + 3600 * Second, T0 + 3910 * Second, false), (sessions[1].StartTime, sessions[1].EndTime, sessions[1].Interruption));
        Assert.Equal(610 * Second, sessions.Sum(x => x.EndTime - x.StartTime));
    }


    [Fact]
    public void StopRightAfterSleep_SessionEndsAtLastHeartbeat()
    {
        // 睡眠唤醒后游戏立即退出：Stop 与上一条心跳间隔过长，睡眠时间不计入
        var items = new List<PlayTimeItemStruct> { Item(T0, PlayTimeState.Start) };
        items.AddRange(Heartbeats(T0 + 30 * Second, T0 + 300 * Second));
        items.Add(Item(T0 + 7200 * Second, PlayTimeState.Stop));

        var sessions = Sessions(items);

        var session = Assert.Single(sessions);
        Assert.Equal(T0 + 300 * Second, session.EndTime);
        Assert.True(session.Interruption);
    }


    [Fact]
    public void GapOfExactlyOneMinute_DoesNotSplit()
    {
        var items = new List<PlayTimeItemStruct>
        {
            Item(T0, PlayTimeState.Start),
            Item(T0 + 60 * Second, PlayTimeState.Play),
            Item(T0 + 120 * Second, PlayTimeState.Stop),
        };

        var session = Assert.Single(Sessions(items));
        Assert.Equal((T0, T0 + 120 * Second, false), (session.StartTime, session.EndTime, session.Interruption));
    }


    [Fact]
    public void ErrorState_EndsSessionAsInterrupted()
    {
        var items = new List<PlayTimeItemStruct>
        {
            Item(T0, PlayTimeState.Start),
            Item(T0 + 30 * Second, PlayTimeState.Play),
            Item(T0 + 40 * Second, PlayTimeState.Error),
        };

        var session = Assert.Single(Sessions(items));
        Assert.Equal(T0 + 40 * Second, session.EndTime);
        Assert.True(session.Interruption);
    }


    [Fact]
    public void StartAgainForSameProcess_ClosesPreviousSession()
    {
        var items = new List<PlayTimeItemStruct>
        {
            Item(T0, PlayTimeState.Start),
            Item(T0 + 30 * Second, PlayTimeState.Play),
            Item(T0 + 60 * Second, PlayTimeState.Start),
            Item(T0 + 90 * Second, PlayTimeState.Play),
            Item(T0 + 120 * Second, PlayTimeState.Stop),
        };

        var sessions = Sessions(items);

        Assert.Equal(2, sessions.Count);
        Assert.Equal((T0, T0 + 30 * Second, true), (sessions[0].StartTime, sessions[0].EndTime, sessions[0].Interruption));
        Assert.Equal((T0 + 60 * Second, T0 + 120 * Second, false), (sessions[1].StartTime, sessions[1].EndTime, sessions[1].Interruption));
    }


    [Fact]
    public void NoStopRecord_ReturnsUnfinishedSessionAsInterrupted()
    {
        var items = new List<PlayTimeItemStruct> { Item(T0, PlayTimeState.Start) };
        items.AddRange(Heartbeats(T0 + 30 * Second, T0 + 90 * Second));

        var session = Assert.Single(Sessions(items));
        Assert.Equal((T0, T0 + 90 * Second, true), (session.StartTime, session.EndTime, session.Interruption));
    }


    [Fact]
    public void HeartbeatsWithoutStart_SessionBeginsAtFirstHeartbeat()
    {
        // 记录进程晚启动时没有 Start，只有补写的心跳
        var items = Heartbeats(T0, T0 + 60 * Second).ToList();
        items.Add(Item(T0 + 70 * Second, PlayTimeState.Stop));

        var session = Assert.Single(Sessions(items));
        Assert.Equal((T0, T0 + 70 * Second, false), (session.StartTime, session.EndTime, session.Interruption));
    }


    [Fact]
    public void DifferentProcesses_AreTrackedSeparately()
    {
        var items = new List<PlayTimeItemStruct>
        {
            Item(T0, PlayTimeState.Start, pid: 1),
            Item(T0 + 10 * Second, PlayTimeState.Start, pid: 2),
            Item(T0 + 30 * Second, PlayTimeState.Play, pid: 1),
            Item(T0 + 40 * Second, PlayTimeState.Play, pid: 2),
            Item(T0 + 50 * Second, PlayTimeState.Stop, pid: 1),
            Item(T0 + 70 * Second, PlayTimeState.Stop, pid: 2),
        };

        var sessions = Sessions(items);

        Assert.Equal(2, sessions.Count);
        Assert.Equal((1, T0, T0 + 50 * Second), (sessions[0].Pid, sessions[0].StartTime, sessions[0].EndTime));
        Assert.Equal((2, T0 + 10 * Second, T0 + 70 * Second), (sessions[1].Pid, sessions[1].StartTime, sessions[1].EndTime));
        Assert.All(sessions, x => Assert.False(x.Interruption));
    }

}
