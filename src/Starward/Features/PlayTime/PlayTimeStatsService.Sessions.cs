using Starward.Core;
using System.Collections.Generic;

namespace Starward.Features.PlayTime;

// 只依赖心跳记录本身、不碰数据库与 WinUI 的纯计算放在这里，测试项目 tests/Starward.Tests 直接链接本文件。
internal sealed partial class PlayTimeStatsService
{

    private const long MAX_INTERVAL = 60_000;



    /// <summary>
    /// 把心跳记录按 (GameBiz, Pid) 切成游戏会话。
    /// 相邻两条心跳间隔超过 <see cref="MAX_INTERVAL"/>（如电脑睡眠）即切断，空档不计入时长；
    /// 同一进程再次出现 Start 时结束上一段；没有收尾记录的会话也会返回，并标记为中断。
    /// </summary>
    /// <param name="items">按时间升序的心跳记录。</param>
    /// <returns>会话列表；<see cref="PlayTimeStats.Interruption"/> 为 true 表示不是由 Stop 正常结束。</returns>
    public static List<PlayTimeStats> GetPlayTimeStats(List<PlayTimeItemStruct> items)
    {
        List<PlayTimeStats> list = new();
        Dictionary<(GameBiz, int), PlayTimeStats> statsSession = new();
        foreach (var item in items)
        {
            var key = (item.GameBiz, item.Pid);
            if (statsSession.TryGetValue((item.GameBiz, item.Pid), out PlayTimeStats? stats))
            {
                if (item.State is PlayTimeState.Start)
                {
                    stats.Interruption = true;
                    list.Add(stats);
                    statsSession[key] = new PlayTimeStats
                    {
                        GameBiz = item.GameBiz,
                        Pid = item.Pid,
                        StartTime = item.TimeStamp,
                        EndTime = item.TimeStamp,
                    };
                    continue;
                }
                if (item.TimeStamp - stats.EndTime > MAX_INTERVAL)
                {
                    stats.Interruption = true;
                    list.Add(stats);
                    statsSession.Remove((item.GameBiz, item.Pid));
                    if (item.State is PlayTimeState.Play)
                    {
                        statsSession[key] = new PlayTimeStats
                        {
                            GameBiz = item.GameBiz,
                            Pid = item.Pid,
                            StartTime = item.TimeStamp,
                            EndTime = item.TimeStamp,
                        };
                    }
                }
                else if (item.State is PlayTimeState.Play)
                {
                    stats.EndTime = item.TimeStamp;
                    continue;
                }
                else if (item.State is PlayTimeState.Stop or PlayTimeState.Error)
                {
                    stats.Interruption = item.State is PlayTimeState.Error;
                    stats.EndTime = item.TimeStamp;
                    list.Add(stats);
                    statsSession.Remove((item.GameBiz, item.Pid));
                    continue;
                }
            }
            else if (item.State is PlayTimeState.Start or PlayTimeState.Play)
            {
                statsSession[key] = new PlayTimeStats
                {
                    GameBiz = item.GameBiz,
                    Pid = item.Pid,
                    StartTime = item.TimeStamp,
                    EndTime = item.TimeStamp,
                };
            }
        }
        foreach (var item in statsSession)
        {
            item.Value.Interruption = true;
            list.Add(item.Value);
        }
        statsSession.Clear();
        return list;
    }

}
