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
        if (_tasks.TryGetValue(gameId, out GameInstallContext? task))
        {
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
        return task;
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



    public async Task<GameInstallContext?> StartRepairAsync(GameId gameId, string installPath, AudioLanguage audioLanguage)
    {
        return await StartOrContinueTaskAsync(GameInstallOperation.Repair, gameId, installPath, audioLanguage);
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
