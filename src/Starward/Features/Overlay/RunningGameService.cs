using Starward.Core;
using Starward.Features.GameLauncher;
using Starward.Features.GamepadControl;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Timers;
using Vanara.PInvoke;

namespace Starward.Features.Overlay;

internal static class RunningGameService
{

    private static readonly Lock _runningGameLock = new();


    private static readonly List<RunningGame> _runningGames = new();


    /// <summary>
    /// 轮询游戏进程是否退出。必须显式给周期：无参构造默认 100ms，会在首次启动游戏后留下一个终生高频轮询。
    /// </summary>
    private static readonly System.Timers.Timer _timer = new(1000);


    private static RunningGame? _latestActiveGame;


    private static readonly User32.WinEventProc hookProc;


    static RunningGameService()
    {
        _timer.Elapsed += CheckGameExit;
        hookProc = new User32.WinEventProc(WinEventProc);
    }



    public static void AddRuninngGame(GameBiz gameBiz, Process process)
    {
        try
        {
            lock (_runningGameLock)
            {
                if (_runningGames.FirstOrDefault(x => x.Pid == process.Id) is not RunningGame runningGame)
                {
                    runningGame = new RunningGame(gameBiz, process);
                    runningGame.WinEventHook = User32.SetWinEventHook(User32.EventConstant.EVENT_SYSTEM_FOREGROUND, User32.EventConstant.EVENT_SYSTEM_FOREGROUND, HINSTANCE.NULL, hookProc, (uint)process.Id, 0, User32.WINEVENT.WINEVENT_OUTOFCONTEXT);
                    _runningGames.Add(runningGame);
                    _latestActiveGame = runningGame;
                    Debug.WriteLine($"Added running game: {runningGame.Name} ({runningGame.Pid})");
                }
                GamepadController.DisableGamepadGuideButtonForGameBarBecauseOfGameStart();
                _timer.Start();
            }
        }
        catch { }
    }



    public static RunningGame? GetLatestActiveGame()
    {
        return _latestActiveGame;
    }


    public static int GetRunningGameCount()
    {
        return _runningGames.Count;
    }


    /// <summary><see cref="CheckGameExit"/> 的累计次数（只在锁内读写），用来决定哪一次顺带读线程快照。</summary>
    private static int _checkGameExitCount;


    /// <summary>
    /// 每秒移除已结束的游戏，全部结束后停表、关悬浮窗、恢复手柄引导键。
    /// 退出码每秒查，线程快照每 <see cref="GameLauncherService.ProcessThreadCheckInterval"/> 秒查一次。
    /// </summary>
    /// <param name="sender">计时器（未使用）。</param>
    /// <param name="e">计时参数（未使用）。</param>
    private static void CheckGameExit(object? sender, ElapsedEventArgs e)
    {
        lock (_runningGameLock)
        {
            bool checkThreads = ++_checkGameExitCount % GameLauncherService.ProcessThreadCheckInterval == 0;
            for (int i = 0; i < _runningGames.Count; i++)
            {
                RunningGame runningGame = _runningGames[i];
                // 与首页按钮用同一套判定：只剩挂起线程的残留也算结束，否则首页已收回、这里仍当游戏在跑。
                // 不直接用 HasExited：读不到句柄时它会抛异常，这里没有 try，这一轮直接中断、该游戏永远移除不掉。
                if (GameLauncherService.IsProcessEnded(runningGame.Process, checkThreads))
                {
                    User32.UnhookWinEvent(runningGame.WinEventHook);
                    _runningGames.RemoveAt(i);
                    if (_latestActiveGame?.Pid == runningGame.Pid)
                    {
                        _latestActiveGame = null;
                    }
                    i--; // Adjust index after removal
                    Debug.WriteLine($"Running game exited: {runningGame.Name} ({runningGame.Pid})");
                }
            }
            if (_runningGames.Count == 0)
            {
                // 没有游戏在跑就停表，避免空转（下次 AddRuninngGame 会重新 Start）
                _timer.Stop();
                _overlayWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_overlayWindow?.AppWindow is not null)
                    {
                        _overlayWindow.Close();
                    }
                });
                GamepadController.RestoreGamepadGuideButtonForGameBarBecauseOfGameExit();
            }
        }
    }



    private static OverlayWindow? _overlayWindow;


    public static bool OpenOverlayWindow()
    {
        return false;
        if (_latestActiveGame is null || _latestActiveGame.WindowHandle is 0 || User32.IsIconic(_latestActiveGame.WindowHandle))
        {
            return false;
        }
        if (_overlayWindow?.AppWindow is null)
        {
            _overlayWindow = new OverlayWindow();
        }
        _overlayWindow.ShowActive(_latestActiveGame);
        return true;
    }



    private static void WinEventProc(User32.HWINEVENTHOOK hWinEventHook, User32.EventConstant winEvent, HWND hwnd, User32.ObjectIdentifier idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (_runningGames.FirstOrDefault(x => x.WinEventHook == hWinEventHook) is RunningGame runningGame)
        {
            _latestActiveGame = runningGame;
            Debug.WriteLine($"Set to foreground: {runningGame.Name}");
        }
    }


}
