using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppLifecycle;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Database;
using Starward.Features.GameLauncher;
using Starward.Features.HoYoPlay;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Starward.Features.PlayTime;

internal class PlayTimeRecordService
{

    private readonly ILogger<PlayTimeRecordService> _logger;

    private readonly HoYoPlayService _hoYoPlayService;

    private readonly PlayTimeStatsService _playTimeStatsService;



    public PlayTimeRecordService(ILogger<PlayTimeRecordService> logger, HoYoPlayService hoYoPlayService, PlayTimeStatsService playTimeStatsService)
    {
        _logger = logger;
        _hoYoPlayService = hoYoPlayService;
        _playTimeStatsService = playTimeStatsService;
    }



    public async Task LogPlayTimeAsync(GameBiz biz, int pid)
    {
        try
        {
            biz = biz.IsBilibili() ? $"{biz.Game}_cn" : biz;
            var instance = AppInstance.FindOrRegisterForKey($"playtime_{pid}");
            if (!instance.IsCurrent)
            {
                _logger.LogWarning("Game process ({biz}, {gamePid}) has been recorded by process ({playtimePid})", biz, pid, instance.ProcessId);
                return;
            }
            _logger.LogInformation("Start to log playtime ({biz}, {pid})", biz, pid);
            var process = Process.GetProcessById(pid);
            LogStartState(biz, process);
            var sw = Stopwatch.StartNew();
            long last = 0;
            while (true)
            {
                await Task.Delay(Random.Shared.Next(800, 1200));
                if (process.HasExited)
                {
                    var now = DateTimeOffset.Now;
                    Log(biz, pid, PlayTimeState.Stop, now.ToUnixTimeMilliseconds(), $"{process.ProcessName} [{now}]");
                    SavePlayTimeStats(biz, pid, process.StartTime, now.DateTime);
                    break;
                }
                else
                {
                    if (sw.ElapsedMilliseconds - last > 30000)
                    {
                        Log(biz, pid, PlayTimeState.Play);
                        last = sw.ElapsedMilliseconds;
                    }
                }
            }
            DatabaseService.SetValue(PlayTimeStatsService.TotalPlayTimeKey(biz), _playTimeStatsService.GetPlayTimeTotal(biz));
            _logger.LogInformation("End log playtime ({biz}, {pid})", biz, pid);
        }
        catch (Exception ex)
        {
            Log(biz, pid, PlayTimeState.Error, 0, ex.Message);
            _logger.LogError(ex, "Log play time: GameBiz {biz}, Pid {pid}", biz, pid);
        }
    }



    private void LogStartState(GameBiz biz, Process process)
    {
        var startTime = new DateTimeOffset(process.StartTime);
        Log(biz, process.Id, PlayTimeState.Start, startTime.ToUnixTimeMilliseconds(), $"{process.ProcessName} [{startTime}]");
        using var dapper = DatabaseService.CreateConnection();
        var last = dapper.QueryFirstOrDefault<PlayTimeItemStruct>("SELECT * FROM PlayTimeItem WHERE GameBiz = @biz AND Pid = @Id ORDER BY TimeStamp DESC LIMIT 1;", new { biz = biz.ToString(), process.Id });
        DateTimeOffset time = startTime;
        if (last.TimeStamp > startTime.ToUnixTimeMilliseconds())
        {
            time = DateTimeOffset.FromUnixTimeMilliseconds(last.TimeStamp);
        }

        var now = DateTimeOffset.Now;
        if (now - time >= TimeSpan.FromSeconds(60))
        {
            // 补全从开始游戏到开始记录游戏时间之间的记录
            List<PlayTimeItem> list = new List<PlayTimeItem>();
            while (true)
            {
                time = time.AddMilliseconds(Random.Shared.Next(30_000, 32_000));
                if (time < now)
                {
                    list.Add(new PlayTimeItem
                    {
                        TimeStamp = time.ToUnixTimeMilliseconds(),
                        GameBiz = biz,
                        Pid = process.Id,
                        State = PlayTimeState.Play,
                    });
                }
                else
                {
                    break;
                }
            }
            using var t = dapper.BeginTransaction();
            dapper.Execute("INSERT OR REPLACE INTO PlayTimeItem (TimeStamp, GameBiz, Pid, State, CursorPos, Message) VALUES (@TimeStamp, @GameBiz, @Pid, @State, @CursorPos, @Message);", list, t);
            t.Commit();
        }
    }



    private void Log(GameBiz biz, int pid, PlayTimeState state, long ts = 0, string? message = null)
    {
        try
        {
            using var dapper = DatabaseService.CreateConnection();
            var item = new PlayTimeItem
            {
                TimeStamp = ts == 0 ? DateTimeOffset.Now.ToUnixTimeMilliseconds() : ts,
                GameBiz = biz,
                Pid = pid,
                State = state,
                Message = message,
            };
            dapper.Execute("INSERT OR REPLACE INTO PlayTimeItem (TimeStamp, GameBiz, Pid, State, CursorPos, Message) VALUES (@TimeStamp, @GameBiz, @Pid, @State, @CursorPos, @Message);", item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Log play time: GameBiz {biz}, Pid {pid}, State {state}, Message {message}", biz, pid, state, message);
        }
    }



    private void SavePlayTimeStats(GameBiz biz, int pid, DateTime startTime, DateTime endTime)
    {
        try
        {
            using var dapper = DatabaseService.CreateConnection();
            var stats = new PlayTimeStats
            {
                GameBiz = biz,
                Pid = pid,
                StartTime = new DateTimeOffset(startTime).ToUnixTimeMilliseconds(),
                EndTime = new DateTimeOffset(endTime).ToUnixTimeMilliseconds(),
            };
            using var t = dapper.BeginTransaction();
            dapper.Execute("INSERT OR REPLACE INTO PlayTimeStats (GameBiz, Pid, StartTime, EndTime, Interruption, Type) VALUES (@GameBiz, @Pid, @StartTime, @EndTime, @Interruption, @Type);", stats);
            dapper.Execute("DELETE FROM PlayTimeItem WHERE GameBiz = @GameBiz AND Pid = @Pid AND TimeStamp >= @StartTime AND TimeStamp <= @EndTime;", stats);
            t.Commit();
            _logger.LogInformation("Save play time stats: GameBiz {biz}, Pid {pid}, StartTime {startTime}, EndTime {endTime}, Interruption {interruption}", biz, pid, startTime, endTime, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save play time stats: GameBiz {biz}, Pid {pid}, StartTime {startTime}, EndTime {endTime}, Interruption {interruption}", biz, pid, startTime, endTime, false);
        }
    }




    #region Start process to log playtime



    /// <summary>
    /// 启动进程记录游戏时间，返回游戏进程
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="launcherProcess">
    /// 拉起游戏的自定义启动程序进程。不为 <see langword="null"/> 时先在它存活期间持续查找游戏进程，
    /// 它退出后仍未找到再按固定时长（约 12 秒）查找。
    /// </param>
    /// <returns></returns>
    public async Task<Process?> StartProcessToLogAsync(GameId gameId, Process? launcherProcess = null)
    {
        try
        {
            var biz = gameId.GameBiz;
            string name = await GetGameExeNameWithoutExtensionAsync(gameId);
            if (launcherProcess is not null)
            {
                // 工具何时拉起游戏不确定（如胡桃工具箱要用户在工具里手动点启动），固定时长常常不够，工具还在就一直等
                _logger.LogInformation("Wait for game process ({biz}) while launcher process ({launcherPid}) is running", biz, launcherProcess.Id);
                while (IsProcessRunning(launcherProcess))
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    if (TryStartProcessToLog(biz, Process.GetProcessesByName(name)) is Process process)
                    {
                        return process;
                    }
                }
                _logger.LogInformation("Launcher process ({launcherPid}) exited before game process ({biz}) was found", launcherProcess.Id, biz);
            }
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(2000);
                var processes = Process.GetProcessesByName(name);
                if (processes.Length == 0)
                {
                    if (i < 5)
                    {
                        continue;
                    }
                    // 未找到游戏进程
                    _logger.LogWarning("Game process ({biz}, {name}) not found, play time will not be recorded", biz, name);
                    return null;
                }
                if (TryStartProcessToLog(biz, processes) is Process process)
                {
                    return process;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start process to log play time");
        }
        return null;
    }


    /// <summary>
    /// 从按进程名找到的候选进程中取当前会话的游戏进程，尚未被记录时启动子进程记录游戏时间。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="processes">按游戏进程名找到的候选进程。</param>
    /// <returns>当前会话的游戏进程（已被其他进程记录的也返回）；没有则返回 <see langword="null"/>。</returns>
    private Process? TryStartProcessToLog(GameBiz biz, Process[] processes)
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        foreach (var process in processes)
        {
            if (process.SessionId != sessionId)
            {
                // 游戏进程不在当前会话
                _logger.LogWarning("Game process ({biz}, {gamePid}) is not in the current session", biz, process.Id);
                continue;
            }
            var instance = App.FindInstanceForKey($"playtime_{process.Id}");
            if (instance != null)
            {
                // 已经有进程在记录该游戏（如首页与快捷方式先后启动、各自在等），不重复记录；
                // 但游戏确实已启动，照常返回，否则等待中的一方会一直等到启动程序退出
                _logger.LogInformation("Game process ({biz}, {gamePid}) has been recorded by process ({playtimePid})", biz, process.Id, instance.ProcessId);
                return process;
            }
            _logger.LogInformation("Start to log playtime ({biz}, {pid})", biz, process.Id);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppConfig.MoonwardExecutePath,
                Arguments = $"playtime --biz {biz} --pid {process.Id} {AppConfig.GetDataFolderArgument()}",
                CreateNoWindow = true,
            });
            return process;
        }
        return null;
    }


    /// <summary>
    /// 进程是否仍在运行。
    /// </summary>
    /// <param name="process">要检查的进程。</param>
    /// <returns>仍在运行返回 <see langword="true"/>；已退出或无法查询（如句柄无权限）返回 <see langword="false"/>，调用方随即改按固定时长查找。</returns>
    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 启动进程记录游戏时间
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="pid"></param>
    /// <returns></returns>
    public async Task StartProcessToLogAsync(GameId gameId, int pid)
    {
        try
        {
            Process process = Process.GetProcessById(pid);
            var biz = gameId.GameBiz;
            string name = await GetGameExeNameWithoutExtensionAsync(gameId);
            if (process.ProcessName != name)
            {
                _logger.LogWarning("Game process ({biz}, {gamePid}) is not the expected process ({name})", biz, pid, process.ProcessName);
                return;
            }
            var instance = App.FindInstanceForKey($"playtime_{pid}");
            if (instance != null)
            {
                _logger.LogWarning("Game process ({biz}, {gamePid}) has been recorded by process ({playtimePid})", biz, pid, instance.ProcessId);
                return;
            }

            Process? p = Process.Start(new ProcessStartInfo
            {
                FileName = AppConfig.MoonwardExecutePath,
                Arguments = $"playtime --biz {biz} --pid {process.Id} {AppConfig.GetDataFolderArgument()}",
                CreateNoWindow = true,
            });
            _logger.LogInformation("Start process to log play time: GameBiz {biz}, Pid {pid}, ProcessId {processId}", biz, pid, p?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start process to log play time: GameBiz {biz}, Pid {pid}", gameId.GameBiz, pid);
        }
    }




    /// <summary>
    /// 游戏进程名，不带 .exe 扩展名
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public async Task<string> GetGameExeNameWithoutExtensionAsync(GameId gameId)
    {
        string? name = GameLauncherService.GetGameExeName(gameId.GameBiz);
        if (string.IsNullOrWhiteSpace(name))
        {
            var config = await _hoYoPlayService.GetGameConfigAsync(gameId);
            name = config?.ExeFileName;
        }
        return name?.Replace(".exe", "") ?? throw new ArgumentOutOfRangeException($"Unknown game ({gameId.Id}, {gameId.GameBiz}).");
    }



    #endregion



}
