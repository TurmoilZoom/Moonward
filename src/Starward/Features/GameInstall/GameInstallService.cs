using CommunityToolkit.Mvvm.Messaging;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
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

    private readonly SemaphoreSlim _getProgressSemaphore = new(1);


    public GameInstallService(ILogger<GameInstallService> logger, RpcService rpcService, GameLauncherService gameLauncherService)
    {
        _logger = logger;
        _rpcService = rpcService;
        _gameLauncherService = gameLauncherService;
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
                            RequestedOperation = (GameInstallOperation)item.RequestedOperation,
                            AudioLanguage = (AudioLanguage)item.AudioLanguage,
                            PackageType = (GameScenarioPackageType)item.PackageType,
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
            // 按用户点的操作判断：硬链接时 RPC 会把安装、更新改成修复，那仍是安装或更新，不弹修复完成
            if (task.RequestedOperation is GameInstallOperation.Repair && task.State is GameInstallState.Finish)
            {
                ShowRepairFinishedToast(task, Lang.RepairGameDialog_GameRepairFinished);
            }
            else if (task.Operation is GameInstallOperation.RepairWPFPackage)
            {
                OnWPFPackageRepairStateChanged(task);
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
    /// 千星沙箱在后台修复，不占用开始游戏按钮，结果用应用内提示告知（与官方启动器一致）。
    /// 出错的任务直接停掉，否则会一直占着这个游戏的任务，影响预下载等操作；提示里的重试会重新开始修复。
    /// </summary>
    /// <param name="task">千星沙箱修复任务，在读取进度的后台线程上调用</param>
    private void OnWPFPackageRepairStateChanged(GameInstallContext task)
    {
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
            _ = StopFailedWPFPackageRepairAsync(task);
        }
    }



    /// <summary>
    /// 停掉出错的千星沙箱修复任务，失败只记日志
    /// </summary>
    /// <param name="task">出错的任务</param>
    /// <returns></returns>
    private async Task StopFailedWPFPackageRepairAsync(GameInstallContext task)
    {
        try
        {
            await StopTaskAsync(task);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stop failed WPF package repair task ({GameBiz})", task.GameId.GameBiz);
        }
    }



    /// <summary>
    /// 开始安装游戏
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath">安装目录</param>
    /// <param name="audioLanguage">语音语言</param>
    /// <param name="packageType">完整或基础资源，不区分资源场景的游戏为 <see cref="GameScenarioPackageType.Unknown"/></param>
    /// <param name="enableHardLink">本次安装是否硬链接到其他区服，<see langword="null"/> 时按该区服记录的选择（见 <see cref="ShouldUseHardLinkAsync"/>）</param>
    /// <returns></returns>
    public async Task<GameInstallContext?> StartInstallAsync(GameId gameId, string installPath, AudioLanguage audioLanguage, GameScenarioPackageType packageType = GameScenarioPackageType.Unknown, bool? enableHardLink = null)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.Install, gameId, installPath, audioLanguage, packageType, enableHardLink);
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
    /// 只在游戏支持硬链接时查找，只列出与当前区服实际共用文件（文件 ID 相同）的区服。
    /// 共用文件的区服（含当前区服）中安装目录创建得最早的视为本体，其余是后来通过硬链接产生的；
    /// 本体不是当前区服时排在最前，要先于当前区服更新。
    /// 已有安装任务（预下载、排队中的更新等）、游戏文件不完整、取不到最新版本的区服不列出。
    /// </summary>
    /// <param name="gameId">正在更新的区服</param>
    /// <param name="installPath">正在更新的区服的安装目录</param>
    /// <returns>可一并更新的区服，本体在前，其余按 <see cref="GameBiz.AllGameBizs"/> 的顺序排列</returns>
    public async Task<List<OtherServerUpdate>> GetHardLinkedServersToUpdateAsync(GameId gameId, string installPath)
    {
        if (!GameFeatureConfig.FromGameId(gameId).SupportHardLink)
        {
            return [];
        }
        string currentFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
        // 两个区服指向同一个目录时也只更新当前这一个，否则同一目录会被连续更新两次
        List<(GameId GameId, string InstallPath)> installed = GetOtherInstalledServers(gameId)
            .Where(x => !string.Equals(x.InstallPath, currentFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (installed.Count == 0)
        {
            return [];
        }

        // 要打开文件读文件 ID，放到后台线程
        List<(GameId GameId, string InstallPath)> linked = await Task.Run(() => GetHardLinkedServers(currentFolder, installed));
        if (linked.Count == 0)
        {
            return [];
        }
        string sourceFolder = GetHardLinkSourceFolder(linked.Select(x => x.InstallPath).Append(currentFolder));
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



    /// <summary>
    /// 同一游戏已安装的其他区服。可移动存储设备已移除的区服访问不到，不列出。
    /// </summary>
    /// <param name="gameId">当前区服</param>
    /// <returns>其他区服与去掉末尾分隔符的完整安装目录，按 <see cref="GameBiz.AllGameBizs"/> 的顺序排列；可能与当前区服的目录相同</returns>
    public static List<(GameId GameId, string InstallPath)> GetOtherInstalledServers(GameId gameId)
    {
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
            installed.Add((otherId, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))));
        }
        return installed;
    }



    /// <summary>
    /// 从其他区服中找出与当前目录通过硬链接共用文件（文件 ID 相同）的区服。要打开文件，调用方应放到后台线程。
    /// </summary>
    /// <param name="currentFolder">当前区服的安装目录</param>
    /// <param name="servers">其他区服与安装目录</param>
    /// <returns>共用文件的区服，保持传入的顺序</returns>
    public static List<(GameId GameId, string InstallPath)> GetHardLinkedServers(string currentFolder, IEnumerable<(GameId GameId, string InstallPath)> servers)
    {
        // 硬链接只能在同一个卷上，同一目录则是同一批文件，都不用比较
        List<(GameId GameId, string InstallPath)> candidates = servers
            .Where(x => string.Equals(Path.GetPathRoot(x.InstallPath), Path.GetPathRoot(currentFolder), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(x.InstallPath, currentFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }
        Dictionary<string, long> fileIds = HardLinkDetector.GetLinkedFileIds(currentFolder);
        return candidates.Where(x => HardLinkDetector.ContainsAnyFile(x.InstallPath, fileIds)).ToList();
    }



    /// <summary>
    /// 在共用文件的一组区服目录中推断本体。硬链接的各个名字地位相同，文件系统里没有来源记录，
    /// 只能靠目录创建时间：通过硬链接产生的目录一定晚于本体创建，同一卷内移动目录也不会改变创建时间。
    /// </summary>
    /// <param name="folders">共用文件的区服安装目录，至少一个</param>
    /// <returns>创建得最早的目录</returns>
    public static string GetHardLinkSourceFolder(IEnumerable<string> folders)
    {
        return folders.MinBy(Directory.GetCreationTimeUtc)!;
    }



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



    private async Task<GameInstallContext?> StartOrContinueTaskAsync(GameInstallOperation operation, GameId gameId, string installPath, AudioLanguage audioLanguage, GameScenarioPackageType packageType = GameScenarioPackageType.Unknown, bool? enableHardLink = null)
    {
        var request = new GameInstallRequest
        {
            GameBiz = gameId.GameBiz,
            GameId = gameId.Id,
            InstallPath = installPath,
            Operation = (int)operation,
            AudioLanguage = (int)audioLanguage,
            HardLinkPath = await GetHardLinkPathAsync(gameId, installPath, enableHardLink),
            PackageType = (int)packageType,
        };
        if (await _rpcService.EnsureRpcServerRunningAsync())
        {
            _logger.LogInformation("""
                Start game install task: 
                Operation: {operation}
                GameId: {gameId} {gameBiz}
                InstallPath: {installPath}
                AudioLanguage: {audioLanguage}
                PackageType: {packageType}
                HardLinkPath: {hardLinkPath}
                """, operation, gameId.Id, gameId.GameBiz, installPath, audioLanguage, packageType, request.HardLinkPath);
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



    /// <summary>
    /// 任务要硬链接的其他区服目录
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath">本区服安装目录</param>
    /// <param name="enableHardLink">是否硬链接，<see langword="null"/> 时按该区服记录的选择</param>
    /// <returns>不硬链接或没有可链接的区服时为 <see langword="null"/></returns>
    private async Task<string?> GetHardLinkPathAsync(GameId gameId, string installPath, bool? enableHardLink = null)
    {
        if (!(enableHardLink ?? await ShouldUseHardLinkAsync(gameId, installPath)))
        {
            return null;
        }
        return (await FindHardLinkTargetAsync(gameId, installPath))?.InstallPath;
    }



    /// <summary>
    /// 没有指定时决定任务是否硬链接：有记录（安装对话框里的选择）按记录；
    /// 没有记录（更早版本或官方启动器安装的）时看安装目录是否已与其他区服共用文件，已共用的继续共用
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath">本区服安装目录</param>
    /// <returns>是否硬链接</returns>
    private static async Task<bool> ShouldUseHardLinkAsync(GameId gameId, string installPath)
    {
        if (AppConfig.GetGameInstallHardLink(gameId.GameBiz) is bool value)
        {
            return value;
        }
        if (!GameFeatureConfig.FromGameId(gameId).SupportHardLink || !Directory.Exists(installPath))
        {
            return false;
        }
        string currentFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
        List<(GameId GameId, string InstallPath)> installed = GetOtherInstalledServers(gameId)
            .Where(x => !string.Equals(x.InstallPath, currentFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (installed.Count == 0)
        {
            return false;
        }
        // 要打开文件读文件 ID，放到后台线程；只抽查少量文件，开销很小
        return (await Task.Run(() => GetHardLinkedServers(currentFolder, installed))).Count > 0;
    }



    /// <summary>
    /// 查找可与该安装目录硬链接的其他区服：同一游戏、同一 NTFS 磁盘上正在安装 / 更新 / 修复或已安装的区服，
    /// 正在进行的任务优先，其余取版本最新的。不管是否要硬链接，只负责找目标
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath">本区服安装目录，可以尚不存在</param>
    /// <returns>游戏不支持硬链接或没有可链接的区服时为 <see langword="null"/></returns>
    public async Task<(GameBiz GameBiz, string InstallPath)?> FindHardLinkTargetAsync(GameId gameId, string installPath)
    {
        if (GameFeatureConfig.FromGameId(gameId).SupportHardLink)
        {
            string game = gameId.GameBiz.Game;
            Version? lastVersion = null;
            (GameBiz GameBiz, string InstallPath)? last = null;
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
                                return (biz, task.InstallPath);
                            }
                        }
                    }
                    string? path = GameLauncherService.GetGameInstallPath(biz);
                    if (!string.IsNullOrWhiteSpace(path) && Path.GetPathRoot(path) == Path.GetPathRoot(installPath) && DriveHelper.GetDriveFormat(path) is "NTFS")
                    {
                        Version? version = await _gameLauncherService.GetLocalGameVersionAsync(biz, path);
                        if (last is null || version > lastVersion)
                        {
                            lastVersion = version;
                            last = (biz, path);
                        }
                    }

                }
            }
            return last;
        }
        return null;
    }



}
