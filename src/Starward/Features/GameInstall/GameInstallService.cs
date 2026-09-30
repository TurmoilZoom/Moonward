using CommunityToolkit.Mvvm.Messaging;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.HoYoPlay;
using Starward.Features.RPC;
using Starward.Helpers;
using Starward.RPC;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameInstall;

internal class GameInstallService
{

    private readonly ILogger<GameInstallService> _logger;

    private readonly RpcService _rpcService;

    private readonly GameInstaller.GameInstallerClient _gameInstallerClient;

    private readonly GameLauncherService _gameLauncherService;

    private readonly HoYoPlayService _hoYoPlayService;

    private readonly SemaphoreSlim _getProgressSemaphore = new(1);


    public GameInstallService(ILogger<GameInstallService> logger, RpcService rpcService, GameLauncherService gameLauncherService, HoYoPlayService hoYoPlayService)
    {
        _logger = logger;
        _rpcService = rpcService;
        _gameLauncherService = gameLauncherService;
        _hoYoPlayService = hoYoPlayService;
        _gameInstallerClient = RpcService.CreateRpcClient<GameInstaller.GameInstallerClient>();
    }




    private ConcurrentDictionary<GameId, GameInstallContext> _tasks = new();





    public GameInstallContext? GetGameInstallTask(GameId gameId)
    {
        return _tasks.GetValueOrDefault(gameId);
    }





    public async Task SyncGameInstallTasksFromRPCAsync()
    {
        try
        {
            if (RpcService.CheckRpcServerRunning())
            {
                GameInstallContextList list = await _gameInstallerClient.SyncGameInstallContextAsync(new EmptyMessage(), deadline: DateTime.UtcNow.AddSeconds(1));
                Dictionary<GameId, GameInstallContext> tasks = _tasks.Values.ToDictionary(x => x.GameId);
                _tasks.Clear();
                foreach (GameInstallContextDTO? item in list.List)
                {
                    GameId gameId = new() { GameBiz = item.GameBiz, Id = item.GameId };
                    if (tasks.Remove(gameId, out GameInstallContext? task))
                    {
                        task.Timestamp = item.Timestamp;
                        task.State = (GameInstallState)item.State;
                        task.ErrorMessage = item.ErrorMessage;
                        task.Progress_DownloadTotalBytes = item.ProgressDownloadTotalBytes;
                        task.Progress_DownloadFinishBytes = item.ProgressDownloadFinishBytes;
                        task.Progress_ReadTotalBytes = item.ProgressReadTotalBytes;
                        task.Progress_ReadFinishBytes = item.ProgressReadFinishBytes;
                        task.Progress_WriteTotalBytes = item.ProgressWriteTotalBytes;
                        task.Progress_WriteFinishBytes = item.ProgressWriteFinishBytes;
                        task.Progress_Percent = item.ProgressPercent;
                        task.NetworkDownloadSpeed = item.NetworkDownloadSpeed;
                        task.StorageReadSpeed = item.StorageReadSpeed;
                        task.StorageWriteSpeed = item.StorageWriteSpeed;
                        task.RemainTimeSeconds = item.RemainTimeSeconds;
                        task.RewrittenFileCount = item.RewrittenFileCount;
                        task.DownloadMode = (GameInstallDownloadMode)item.DownloadMode;
                    }
                    else
                    {
                        task = new GameInstallContext
                        {
                            GameId = gameId,
                            InstallPath = item.InstallPath,
                            Operation = (GameInstallOperation)item.Operation,
                            AudioLanguage = (AudioLanguage)item.AudioLanguage,
                            HardLinkPath = item.HardLinkPath,
                            Timestamp = item.Timestamp,
                            State = (GameInstallState)item.State,
                            ErrorMessage = item.ErrorMessage,
                            Progress_DownloadTotalBytes = item.ProgressDownloadTotalBytes,
                            Progress_DownloadFinishBytes = item.ProgressDownloadFinishBytes,
                            Progress_ReadTotalBytes = item.ProgressReadTotalBytes,
                            Progress_ReadFinishBytes = item.ProgressReadFinishBytes,
                            Progress_WriteTotalBytes = item.ProgressWriteTotalBytes,
                            Progress_WriteFinishBytes = item.ProgressWriteFinishBytes,
                            Progress_Percent = item.ProgressPercent,
                            NetworkDownloadSpeed = item.NetworkDownloadSpeed,
                            StorageReadSpeed = item.StorageReadSpeed,
                            StorageWriteSpeed = item.StorageWriteSpeed,
                            RemainTimeSeconds = item.RemainTimeSeconds,
                            RewrittenFileCount = item.RewrittenFileCount,
                            DownloadMode = (GameInstallDownloadMode)item.DownloadMode,
                        };
                    }
                    _tasks.TryAdd(gameId, task);
                }
                foreach (var item in tasks.Values)
                {
                    item.State = GameInstallState.Stop;
                }
                if (!_tasks.IsEmpty)
                {
                    StartUpdateTaskProgress();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync game install tasks");
        }
    }





    public async void StartUpdateTaskProgress()
    {
        if (_getProgressSemaphore.CurrentCount == 0)
        {
            return;
        }
        try
        {
            await _getProgressSemaphore.WaitAsync().ConfigureAwait(false);
            if (RpcService.CheckRpcServerRunning())
            {
                using var call = _gameInstallerClient.GetTaskProgress(new EmptyMessage());
                await foreach (GameInstallContextDTO item in call.ResponseStream.ReadAllAsync().ConfigureAwait(false))
                {
                    AddOrUpdateTask(item);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTaskProgress");
        }
        finally
        {
            _getProgressSemaphore.Release();
            if (!_tasks.IsEmpty)
            {
                if (RpcService.CheckRpcServerRunning())
                {
                    await Task.Delay(1000);
                    StartUpdateTaskProgress();
                }
                else
                {
                    _logger.LogWarning("RPC server is not running, set {count} running install tasks state to error.", _tasks.Count);
                    foreach (GameInstallContext item in _tasks.Values)
                    {
                        item.State = GameInstallState.Error;
                        item.ErrorMessage = Lang.RPCServiceExitedUnexpectedly;
                        item.NetworkDownloadSpeed = 0;
                        item.StorageReadSpeed = 0;
                        item.StorageWriteSpeed = 0;
                        item.RemainTimeSeconds = 0;
                    }
                }
            }
        }
    }




    private GameInstallContext AddOrUpdateTask(GameInstallContextDTO dto)
    {
        GameId gameId = dto.GetGameId();
        GameInstallState? previousState = null;
        if (_tasks.TryGetValue(gameId, out GameInstallContext? task))
        {
            previousState = task.State;
            dto.UpdateTask(task);
        }
        else
        {
            task = dto.UpdateTask();
            _tasks.TryAdd(gameId, task);
        }
        if (task.State is GameInstallState.Stop or GameInstallState.Finish)
        {
            _tasks.TryRemove(gameId, out _);
        }
        // 已结束的任务 RPC 还会再推送一次，那时任务已不在表里，previousState 为 null，不会重复提示
        if (previousState is not null && previousState != task.State)
        {
            if (task.Operation is GameInstallOperation.Repair && task.State is GameInstallState.Finish)
            {
                ShowRepairFinishedToast(task, Lang.RepairGameDialog_GameRepairFinished);
            }
            else if (task.Operation is GameInstallOperation.RepairWPFPackage or GameInstallOperation.UpdateWPFPackage)
            {
                OnWPFPackageTaskStateChanged(task);
            }
        }
        return task;
    }



    /// <summary>
    /// 修复完成的提示：没有文件需要重新下载或重建时提示本地资源完整，与真正修复了文件区分开（与官方启动器一致）
    /// </summary>
    /// <param name="task">已完成的修复任务</param>
    /// <param name="repairedMessage">修复了文件时的提示</param>
    private static void ShowRepairFinishedToast(GameInstallContext task, string repairedMessage)
    {
        InAppToast.MainWindow?.Success(task.RewrittenFileCount == 0 ? Lang.RepairGameDialog_ResourcesIntact : repairedMessage);
    }



    /// <summary>
    /// 千星沙箱在后台修复或更新，不占用开始游戏按钮。修复的结果用应用内提示告知（与官方启动器一致），提示里的重试会重新修复；
    /// 自动更新成功失败都不提示，失败了下次打开游戏页再试。
    /// 出错的任务直接停掉，否则会一直占着这个游戏的任务，影响预下载等操作。
    /// </summary>
    /// <param name="task">千星沙箱修复或更新任务，在读取进度的后台线程上调用</param>
    private void OnWPFPackageTaskStateChanged(GameInstallContext task)
    {
        if (task.State is GameInstallState.Error)
        {
            _ = StopFailedWPFPackageTaskAsync(task);
        }
        if (task.Operation is GameInstallOperation.UpdateWPFPackage)
        {
            return;
        }
        if (task.State is GameInstallState.Finish)
        {
            ShowRepairFinishedToast(task, Lang.RepairGameDialog_SandboxRepairFinished);
        }
        else if (task.State is GameInstallState.Error)
        {
            GameId gameId = task.GameId;
            string installPath = task.InstallPath;
            InAppToast.MainWindow?.ShowWithButton(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, Lang.RepairGameDialog_SandboxRepairFailed, task.ErrorMessage, Lang.DownloadGamePage_Retry, async () =>
            {
                try
                {
                    GameInstallContext? retry = await StartRepairWPFPackageAsync(gameId, installPath);
                    if (retry is not null)
                    {
                        WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(retry));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retry repair WPF package ({GameBiz})", gameId.GameBiz);
                }
            }, duration: 10000);
        }
    }



    /// <summary>
    /// 停掉出错的千星沙箱修复或更新任务，失败只记日志
    /// </summary>
    /// <param name="task">出错的任务</param>
    /// <returns></returns>
    private async Task StopFailedWPFPackageTaskAsync(GameInstallContext task)
    {
        try
        {
            await StopTaskAsync(task);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stop failed {Operation} task ({GameBiz})", task.Operation, task.GameId.GameBiz);
        }
    }



    public async Task<GameInstallContext?> StartInstallAsync(GameId gameId, string installPath, AudioLanguage audioLanguage)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.Install, gameId, installPath, audioLanguage);
    }



    public async Task<GameInstallContext?> StartPredownloadAsync(GameId gameId, string installPath, AudioLanguage audioLanguage)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.Predownload, gameId, installPath, audioLanguage);
    }



    public async Task<GameInstallContext?> StartUpdateAsync(GameId gameId, string installPath, AudioLanguage audioLanguage)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.Update, gameId, installPath, audioLanguage);
    }



    /// <summary>
    /// 查找与当前区服通过硬链接共用文件、也有新版本的其他区服，用于更新当前区服时一并更新。
    /// 只在开启硬链接且游戏支持硬链接时查找，只列出与当前区服实际共用文件（文件 ID 相同）的区服。
    /// 共用文件的区服（含当前区服）中安装目录创建得最早的视为本体，其余是后来通过硬链接产生的；
    /// 本体不是当前区服时排在最前，要先于当前区服更新。
    /// 已有安装任务（预下载、排队中的更新等）、游戏文件不完整、取不到最新版本的区服不列出。
    /// </summary>
    /// <param name="gameId">正在更新的区服</param>
    /// <param name="installPath">正在更新的区服的安装目录</param>
    /// <returns>可一并更新的区服，本体在前，其余按 <see cref="GameBiz.AllGameBizs"/> 的顺序排列</returns>
    public async Task<List<OtherServerUpdate>> GetHardLinkedServersToUpdateAsync(GameId gameId, string installPath)
    {
        if (!AppConfig.EnableHardLink || !GameFeatureConfig.FromGameId(gameId).SupportHardLink)
        {
            return [];
        }
        string currentFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
        List<(GameId GameId, string InstallPath)> installed = new();
        foreach (GameBiz biz in GameBiz.AllGameBizs)
        {
            if (biz == gameId.GameBiz || biz.Game != gameId.GameBiz.Game || GameId.FromGameBiz(biz) is not GameId otherId)
            {
                continue;
            }
            if (biz == GameBiz.bh3_global && AppConfig.LastGameIdOfBH3Global is string bh3GlobalId && !string.IsNullOrWhiteSpace(bh3GlobalId))
            {
                // 崩坏3国际服同一 GameBiz 下有多个区服，与主界面一样取上次选择的那个，任务才能和它的页面对上
                otherId.Id = bh3GlobalId;
            }
            string? path = GameLauncherService.GetGameInstallPath(otherId, out bool storageRemoved);
            if (path is null || storageRemoved)
            {
                continue;
            }
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            // 硬链接只能在同一个卷上；两个区服指向同一个目录时也只更新当前这一个，否则同一目录会被连续更新两次
            if (!string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(currentFolder), StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, currentFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            installed.Add((otherId, path));
        }
        if (installed.Count == 0)
        {
            return [];
        }

        // 要打开文件读文件 ID，放到后台线程
        List<(GameId GameId, string InstallPath)> linked = await Task.Run(() =>
        {
            Dictionary<string, long> fileIds = HardLinkDetector.GetLinkedFileIds(currentFolder);
            return installed.Where(x => HardLinkDetector.ContainsAnyFile(x.InstallPath, fileIds)).ToList();
        });
        if (linked.Count == 0)
        {
            return [];
        }
        // 通过硬链接产生的目录一定晚于本体创建；同一卷内移动目录不会改变创建时间
        string sourceFolder = linked.Select(x => x.InstallPath).Append(currentFolder).MinBy(Directory.GetCreationTimeUtc)!;
        _logger.LogInformation("Hard linked servers of {GameBiz}: {Servers}, source folder: {Source}", gameId.GameBiz, linked.Select(x => x.GameId.GameBiz.ToString()), sourceFolder);

        List<(GameId GameId, string InstallPath, Version LocalVersion)> candidates = new();
        foreach ((GameId otherId, string path) in linked)
        {
            if (GetGameInstallTask(otherId) is { State: not GameInstallState.Stop and not GameInstallState.Finish })
            {
                continue;
            }
            if (!await _gameLauncherService.IsGameExeExistsAsync(otherId, path)
                || await _gameLauncherService.GetLocalGameVersionAsync(otherId.GameBiz, path) is not Version localVersion)
            {
                continue;
            }
            candidates.Add((otherId, path, localVersion));
        }

        Task<OtherServerUpdate?>[] checks = candidates.Select(async item =>
        {
            try
            {
                (Version? latestVersion, _) = await _gameLauncherService.GetLatestGameVersionAsync(item.GameId);
                if (latestVersion is null || latestVersion <= item.LocalVersion)
                {
                    return null;
                }
                bool running = await _gameLauncherService.GetGameProcessAsync(item.GameId) is not null;
                bool isSource = string.Equals(item.InstallPath, sourceFolder, StringComparison.OrdinalIgnoreCase);
                return new OtherServerUpdate(item.GameId, item.InstallPath, item.LocalVersion, latestVersion, running, isSource);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Check update of hard linked server ({GameBiz})", item.GameId.GameBiz);
                return null;
            }
        }).ToArray();
        // 其他区服的接口可能很慢（例如在国内访问国际服），不能因此拖住当前区服的更新，超时未返回的区服不列出
        Task allChecks = Task.WhenAll(checks);
        if (await Task.WhenAny(allChecks, Task.Delay(OtherServerCheckTimeout)) != allChecks)
        {
            _logger.LogWarning("Check update of hard linked servers timed out, skip {Count} of {Total}.", checks.Count(x => !x.IsCompleted), checks.Length);
        }
        return checks.Where(x => x.IsCompletedSuccessfully)
            .Select(x => x.Result)
            .OfType<OtherServerUpdate>()
            .OrderByDescending(x => x.IsHardLinkSource)
            .ToList();
    }


    /// <summary>
    /// 更新时查找其他区服最新版本的最长等待时间。
    /// </summary>
    private static readonly TimeSpan OtherServerCheckTimeout = TimeSpan.FromSeconds(5);



    public async Task<GameInstallContext?> StartRepairAsync(GameId gameId, string installPath, AudioLanguage audioLanguage)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.Repair, gameId, installPath, audioLanguage);
    }



    /// <summary>
    /// 只修复千星沙箱（WPF 包），不校验游戏资源。
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <param name="installPath">游戏安装目录</param>
    /// <returns>RPC 服务没运行时为 <see langword="null"/></returns>
    public async Task<GameInstallContext?> StartRepairWPFPackageAsync(GameId gameId, string installPath)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.RepairWPFPackage, gameId, installPath, AudioLanguage.None);
    }



    /// <summary>
    /// 在后台把千星沙箱（WPF 包）更新到官方最新版本，本地已是最新时 RPC 什么也不做。
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <param name="installPath">游戏安装目录</param>
    /// <returns>RPC 服务没运行时为 <see langword="null"/></returns>
    public async Task<GameInstallContext?> StartUpdateWPFPackageAsync(GameId gameId, string installPath)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.UpdateWPFPackage, gameId, installPath, AudioLanguage.None);
    }



    /// <summary>
    /// 开启了自动更新千星沙箱、且官方有新版本时，在后台开始更新（与官方启动器的「自动为我更新」一致）。
    /// 这个游戏已有其他任务、游戏本体有更新（更新游戏时会一并更新千星沙箱）或游戏正在运行（文件可能被占用）时不更新。
    /// 本地没有千星沙箱版本记录时同样会下载安装。
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <param name="installPath">游戏安装目录</param>
    /// <returns>开始了更新时为该任务，否则为 <see langword="null"/></returns>
    public async Task<GameInstallContext?> TryStartWPFPackageAutoUpdateAsync(GameId gameId, string installPath)
    {
        if (!AppConfig.GetAutoUpdateWPFPackage(gameId.GameBiz) || GetGameInstallTask(gameId) is not null)
        {
            return null;
        }
        if (await _hoYoPlayService.GetWPFPackageAsync(gameId) is not WPFPackage package || string.IsNullOrWhiteSpace(package.Version))
        {
            return null;
        }
        string? localWpfVersion = await GameLauncherService.GetLocalWPFVersionAsync(installPath);
        if (localWpfVersion == package.Version)
        {
            return null;
        }
        Version? localVersion = await _gameLauncherService.GetLocalGameVersionAsync(gameId, installPath);
        (Version? latestVersion, _) = await _gameLauncherService.GetLatestGameVersionAsync(gameId);
        if (localVersion is null || latestVersion > localVersion)
        {
            return null;
        }
        // 上面的检查要联网，期间可能已经开始了别的任务或启动了游戏
        if (GetGameInstallTask(gameId) is not null || await _gameLauncherService.GetGameProcessAsync(gameId) is not null)
        {
            return null;
        }
        _logger.LogInformation("Auto update WPF package ({GameBiz}): {Local} -> {Latest}", gameId.GameBiz, localWpfVersion, package.Version);
        Telemetry.Track("wpf_auto_update", gameId.GameBiz, ("local", localWpfVersion), ("latest", package.Version));
        return await StartUpdateWPFPackageAsync(gameId, installPath);
    }



    private async Task<GameInstallContext?> StartOrContinueTaskAsync(GameInstallOperation operation, GameId gameId, string installPath, AudioLanguage audioLanguage)
    {
        var request = new GameInstallRequest
        {
            GameBiz = gameId.GameBiz,
            GameId = gameId.Id,
            InstallPath = installPath,
            Operation = (int)operation,
            AudioLanguage = (int)audioLanguage,
            HardLinkPath = await GetHardLinkPathAsync(gameId, installPath),
            SkipWpfPackageUpdate = !AppConfig.GetAutoUpdateWPFPackage(gameId.GameBiz),
        };
        if (await _rpcService.EnsureRpcServerRunningAsync())
        {
            _logger.LogInformation("""
                Start game install task: 
                Operation: {operation}
                GameId: {gameId} {gameBiz}
                InstallPath: {installPath}
                AudioLanguage: {audioLanguage}
                HardLinkPath: {hardLinkPath}
                """, operation, gameId.Id, gameId.GameBiz, installPath, audioLanguage, request.HardLinkPath);
            GameInstallContextDTO dto;
            try
            {
                dto = await _gameInstallerClient.StartOrContinueTaskAsync(request, deadline: DateTime.UtcNow.AddSeconds(3));
            }
            catch (Exception ex)
            {
                TrackRpcFailed("start", gameId, operation, ex);
                throw;
            }
            StartUpdateTaskProgress();
            return AddOrUpdateTask(dto);
        }
        else
        {
            _logger.LogInformation("RPC server is not running, can't start game install task ({GameBiz}, {Operation}).", gameId.GameBiz, operation);
            TrackRpcFailed("start", gameId, operation);
            return null;
        }
    }



    public async Task<GameInstallContext> PauseTaskAsync(GameInstallContext task)
    {
        if (RpcService.CheckRpcServerRunning())
        {
            var request = GameInstallRequest.FromTask(task);
            await _rpcService.EnsureRpcServerRunningAsync();
            _logger.LogInformation("Pause game install task: {gameId} {gameBiz}", task.GameId.Id, task.GameId.GameBiz);
            GameInstallContextDTO dto;
            try
            {
                dto = await _gameInstallerClient.PauseTaskAsync(request, deadline: DateTime.UtcNow.AddSeconds(3));
            }
            catch (Exception ex)
            {
                TrackRpcFailed("pause", task.GameId, task.Operation, ex);
                throw;
            }
            task = AddOrUpdateTask(dto);
            StartUpdateTaskProgress();
        }
        else
        {
            TrackRpcFailed("pause", task.GameId, task.Operation);
            task.State = GameInstallState.Stop;
            task.ErrorMessage = Lang.RPCServiceExitedUnexpectedly;
        }
        return task;
    }



    public async Task<GameInstallContext> ContinueTaskAsync(GameInstallContext task)
    {
        var request = GameInstallRequest.FromTask(task);
        request.SkipWpfPackageUpdate = !AppConfig.GetAutoUpdateWPFPackage(task.GameId.GameBiz);
        if (await _rpcService.EnsureRpcServerRunningAsync())
        {
            _logger.LogInformation("Continue game install task: {gameId} {gameBiz}", task.GameId.Id, task.GameId.GameBiz);
            GameInstallContextDTO dto;
            try
            {
                dto = await _gameInstallerClient.StartOrContinueTaskAsync(request, deadline: DateTime.UtcNow.AddSeconds(3));
            }
            catch (Exception ex)
            {
                TrackRpcFailed("continue", task.GameId, task.Operation, ex);
                throw;
            }
            task = AddOrUpdateTask(dto);
            StartUpdateTaskProgress();
        }
        else
        {
            TrackRpcFailed("continue", task.GameId, task.Operation);
        }
        return task;
    }



    public async Task<GameInstallContext> StopTaskAsync(GameInstallContext task)
    {
        if (RpcService.CheckRpcServerRunning())
        {
            var request = GameInstallRequest.FromTask(task);
            await _rpcService.EnsureRpcServerRunningAsync();
            _logger.LogInformation("Stop game install task: {gameId} {gameBiz}", task.GameId.Id, task.GameId.GameBiz);
            GameInstallContextDTO dto;
            try
            {
                dto = await _gameInstallerClient.StopTaskAsync(request, deadline: DateTime.UtcNow.AddSeconds(3));
            }
            catch (Exception ex)
            {
                TrackRpcFailed("stop", task.GameId, task.Operation, ex);
                throw;
            }
            task = AddOrUpdateTask(dto);
            StartUpdateTaskProgress();
        }
        else
        {
            TrackRpcFailed("stop", task.GameId, task.Operation);
            task.State = GameInstallState.Stop;
            task.ErrorMessage = Lang.RPCServiceExitedUnexpectedly;
        }
        return task;
    }



    public async Task<bool> StartUninstallAsync(GameId gameId, string installPath)
    {
        if (await _rpcService.EnsureRpcServerRunningAsync())
        {
            _logger.LogInformation("""
                Start uninstall game:
                GameId: {gameId} {gameBiz}
                InstallPath: {installPath}
                """, gameId.Id, gameId.GameBiz, installPath);
            string? sharedServer = GetInstalledServerSharingLogFolders(gameId.GameBiz, installPath);
            if (sharedServer is not null)
            {
                _logger.LogInformation("Keep shared log folders of {gameBiz} because {otherBiz} is still installed.", gameId.GameBiz, sharedServer);
            }
            var request = new UninstallGameRequest
            {
                GameBiz = gameId.GameBiz,
                GameId = gameId.Id,
                InstallPath = installPath,
                UserDataFolder = AppConfig.UserDataFolder,
                ScreenshotFolder = AppConfig.ScreenshotFolder,
                GameExeName = GameLauncherService.GetGameExeName(gameId.GameBiz),
                KeepSharedLogFolders = sharedServer is not null,
            };
            var response = await _gameInstallerClient.UninstallGameAsync(request);
            if (response.Success)
            {
                return true;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }
        else
        {
            return false;
        }
    }



    /// <summary>
    /// 查找与本区服共用日志目录、且仍安装着的其他区服。
    /// Unity 游戏的注册表键与 LocalLow 数据目录都由同一组公司名/产品名生成（国服与 B 服同为「miHoYo\原神」等），
    /// 所以注册表键相同即日志目录相同。
    /// </summary>
    /// <param name="gameBiz">要卸载的区服</param>
    /// <param name="installPath">要卸载的安装目录，与它相同的路径不算另一个区服</param>
    /// <returns>仍安装着的其他区服；没有时返回 <see langword="null"/></returns>
    private static string? GetInstalledServerSharingLogFolders(GameBiz gameBiz, string installPath)
    {
        if (!gameBiz.IsKnown())
        {
            return null;
        }
        string registryKey = gameBiz.GetGameRegistryKey();
        string ownPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
        foreach (GameBiz biz in GameBiz.AllGameBizs)
        {
            if (biz == gameBiz || biz.GetGameRegistryKey() != registryKey)
            {
                continue;
            }
            string? path = GameLauncherService.GetGameInstallPath(biz);
            if (path is not null && !string.Equals(Path.TrimEndingDirectorySeparator(path), ownPath, StringComparison.OrdinalIgnoreCase))
            {
                return biz;
            }
        }
        return null;
    }



    /// <summary>
    /// 记录调用 RPC 失败（服务没起来或调用抛异常）；调用成功时由 RPC 进程自己记录任务事件
    /// </summary>
    /// <param name="call">调用：start / pause / continue / stop</param>
    /// <param name="gameId">任务对应的游戏</param>
    /// <param name="operation">任务操作</param>
    /// <param name="error">调用异常；为 null 表示 RPC 服务不可用</param>
    private static void TrackRpcFailed(string call, GameId gameId, GameInstallOperation operation, Exception? error = null)
    {
        Telemetry.Track("install_rpc_failed", gameId.GameBiz,
            ("call", call),
            ("op", operation),
            ("reason", error is null ? "rpc_unavailable" : "error"),
            ("error_type", error?.GetType().Name),
            ("error", error?.Message));
    }



    private async Task<string?> GetHardLinkPathAsync(GameId gameId, string installPath)
    {
        if (!AppConfig.EnableHardLink)
        {
            return null;
        }
        if (GameFeatureConfig.FromGameId(gameId).SupportHardLink)
        {
            string game = gameId.GameBiz.Game;
            Version? lastVersion = null;
            string? lastPath = null;
            foreach (string server in new[] { "cn", "bilibili", "global", })
            {
                string biz = $"{game}_{server}";
                if (gameId.GameBiz != biz)
                {
                    if (_tasks.Values.FirstOrDefault(x => x.GameId.GameBiz == biz) is GameInstallContext task)
                    {
                        if (task.Operation is GameInstallOperation.Install or GameInstallOperation.Update or GameInstallOperation.Repair)
                        {
                            if (!string.IsNullOrWhiteSpace(task.InstallPath) && Path.GetPathRoot(task.InstallPath) == Path.GetPathRoot(installPath) && DriveHelper.GetDriveFormat(installPath) is "NTFS")
                            {
                                return task.InstallPath;
                            }
                        }
                    }
                    string? path = GameLauncherService.GetGameInstallPath(biz);
                    if (!string.IsNullOrWhiteSpace(path) && Path.GetPathRoot(path) == Path.GetPathRoot(installPath) && DriveHelper.GetDriveFormat(path) is "NTFS")
                    {
                        Version? version = await _gameLauncherService.GetLocalGameVersionAsync(biz, path);
                        if (lastPath is null)
                        {
                            lastVersion = version;
                            lastPath = path;
                        }
                        else if (version > lastVersion)
                        {
                            lastVersion = version;
                            lastPath = path;
                        }
                    }

                }
            }
            return lastPath;
        }
        return null;
    }



}
