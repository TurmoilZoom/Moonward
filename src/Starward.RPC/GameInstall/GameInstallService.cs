using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.RPC.Env;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC.GameInstall;

internal class GameInstallService
{


    private readonly ILogger<GameInstallService> _logger;


    private readonly IServiceProvider _serviceProvider;


    private readonly ResiliencePipeline _polly;


    private readonly GameInstallHelper _gameInstallHelper;


    private readonly ConcurrentDictionary<GameId, GameInstallContext> _tasks = new();


    public event EventHandler<GameInstallContext>? TaskStateChanged;

    public GameInstallContext? CurrentTask { get; private set; }



    public GameInstallService(ILogger<GameInstallService> logger, IServiceProvider serviceProvider, GameInstallHelper gameInstallHelper)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _gameInstallHelper = gameInstallHelper;
        _gameInstallHelper = gameInstallHelper;
        _polly = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            BackoffType = DelayBackoffType.Linear
        }).Build();
        LifecycleManager.ParentProcessExited += LifecycleManager_ParentProcessExited;
    }



    /// <summary>
    /// 停止所有任务
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void LifecycleManager_ParentProcessExited(object? sender, Process e)
    {
        try
        {
            _logger.LogInformation("Parent process {name} ({pid}) exited, stop all game install tasks.", e.ProcessName, e.Id);
            foreach (var item in _tasks)
            {
                TrackCancel(item.Key, item.Value, GameInstallState.Stop, "parent_exit");
                item.Value.Cancel(GameInstallState.Stop);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancel all install task");
        }
    }




    public bool TryGetTask(GameId gameId, [NotNullWhen(true)] out GameInstallContext? context)
    {
        return _tasks.TryGetValue(gameId, out context);
    }



    /// <summary>
    /// 开始或继续任务
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public GameInstallContextDTO StartOrContinueTask(GameInstallRequest request)
    {
        if (_tasks.TryGetValue(request.GetGameId(), out GameInstallContext? context))
        {
            if (context.Operation != (GameInstallOperation)request.Operation)
            {
                // 操作不一样，则取消上次任务
                _logger.LogInformation("The new task operation is different from the previous task, cancel the previous task, GameBiz: {game_biz}, Operation: {operation}", context.GameId.GameBiz, context.Operation);
                TrackCancel(context.GameId, context, GameInstallState.Stop, "replace");
                context.Cancel(GameInstallState.Stop);
                _tasks.TryRemove(context.GameId, out _);
                context = request.ToTask();
                _tasks.TryAdd(context.GameId, context);
            }
        }
        else
        {
            context = request.ToTask();
            _tasks.TryAdd(context.GameId, context);
        }
        return StartOrContinueTask(context);
    }



    /// <summary>
    /// 开始或继续任务
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private GameInstallContextDTO StartOrContinueTask(GameInstallContext context)
    {
        if (context.State is GameInstallState.Stop && CurrentTask != null && CurrentTask != context)
        {
            // 第一次开始时，如果有其他任务在执行，排队
            _logger.LogInformation("Queueing GameInstallTask, GameBiz: {game_biz}, Operation: {operation}", context.GameId.GameBiz, context.Operation);
            context.State = GameInstallState.Queueing;
            Telemetry.Track("task_queued", context.GameId.GameBiz,
                ("op", context.Operation),
                ("txn", context.TransactionId),
                ("running_biz", CurrentTask.GameId.GameBiz.ToString()));
            return GameInstallContextDTO.FromTask(context);
        }
        if (CurrentTask != null && CurrentTask != context)
        {
            TrackCancel(CurrentTask.GameId, CurrentTask, GameInstallState.Queueing, "preempt");
            CurrentTask.Cancel(GameInstallState.Queueing);
            TaskStateChanged?.Invoke(this, CurrentTask);
        }
        CurrentTask = context;
        if (context.State is GameInstallState.Finish)
        {
            ChangeToAnotherTask(context);
            return GameInstallContextDTO.FromTask(context);
        }
        else if (context.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Verifying)
        {
            return GameInstallContextDTO.FromTask(context);
        }
        TrackStart(context);
        context.State = GameInstallState.Waiting;
        context.ErrorMessage = null;
        context.RunTask = PrepareGameInstallTaskAsync(context, context.CancellationToken);
        return GameInstallContextDTO.FromTask(context);
    }



    /// <summary>
    /// 暂停任务
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public GameInstallContextDTO PauseTask(GameInstallRequest request)
    {
        if (_tasks.TryGetValue(request.GetGameId(), out GameInstallContext? context))
        {
            TrackCancel(context.GameId, context, GameInstallState.Paused, "pause");
            context.Cancel(GameInstallState.Paused);
        }
        else
        {
            TrackCancel(request.GetGameId(), null, GameInstallState.Paused, "pause");
            context = request.ToTask();
            context.State = GameInstallState.Stop;
        }
        return GameInstallContextDTO.FromTask(context);
    }


    /// <summary>
    /// 停止任务
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public GameInstallContextDTO StopTask(GameInstallRequest request)
    {
        GameId gameId = request.GetGameId();
        TrackCancel(gameId, _tasks.GetValueOrDefault(gameId), GameInstallState.Stop, "stop");
        GameInstallContext context = StopTask(gameId) ?? request.ToTask();
        context.State = GameInstallState.Stop;
        return GameInstallContextDTO.FromTask(context);
    }



    /// <summary>
    /// 从任务表移除任务并置为 Stop，之后不会再被继续或排队调度。不记录埋点，由调用方按来源记录。
    /// 正在运行的协程只是收到取消信号，需要确认退出时用 <see cref="WaitTaskExitAsync"/>
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <returns>被移除的任务；没有任务时为 null</returns>
    internal GameInstallContext? StopTask(GameId gameId)
    {
        if (_tasks.TryRemove(gameId, out GameInstallContext? context))
        {
            // 已暂停的任务 CTS 早已取消，Cancel 只改 CancelState，所以状态要在这里直接置 Stop
            context.Cancel(GameInstallState.Stop);
            context.State = GameInstallState.Stop;
        }
        return context;
    }



    /// <summary>
    /// 等待任务最近一轮运行的协程退出；任务没在运行（已暂停、出错、排队）时立即返回
    /// </summary>
    /// <param name="context">已取消的任务</param>
    /// <param name="timeout">最长等待时间</param>
    /// <param name="cancellationToken">取消等待</param>
    /// <returns>协程已退出为 true，超时或等待被取消为 false</returns>
    internal static async Task<bool> WaitTaskExitAsync(GameInstallContext context, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (context.RunTask is not { IsCompleted: false } runTask)
        {
            return true;
        }
        // 用 WhenAny 而不是 WaitAsync：协程自身的异常已在内部记录，这里只关心它是否结束
        await Task.WhenAny(runTask, Task.Delay(timeout, cancellationToken));
        return runTask.IsCompleted;
    }




    /// <summary>
    /// 准备游戏任务
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task PrepareGameInstallTaskAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        Exception? error = null;
        try
        {
            _logger.LogInformation("""
                Start game install task: 
                Operation: {operation}
                GameId: {gameId} {gameBiz}
                InstallPath: {installPath}
                AudioLanguage: {audioLanguage}
                PackageType: {packageType}
                HardLinkPath: {hardLinkPath}
                """, context.Operation, context.GameId.Id, context.GameId.GameBiz, context.InstallPath, context.AudioLanguage, context.PackageType, context.HardLinkPath);
            Directory.CreateDirectory(context.InstallPath);
            GamePackageService gamePackageService = _serviceProvider.GetRequiredService<GamePackageService>();
            long prepareStart = Stopwatch.GetTimestamp();
            bool reused = context.TaskFiles is not null;
            if (context.AudioLanguage is not AudioLanguage.None)
            {
                await gamePackageService.SetAudioLanguageAsync(context.GameId, context.InstallPath, context.AudioLanguage, cancellationToken);
            }
            if (context.PackageType is not GameScenarioPackageType.Unknown)
            {
                // 先写入再准备文件：准备时按游戏目录里的记录决定跳过哪些分类，暂停后继续、硬链接改成修复时也能读到
                await gamePackageService.SetScenarioPackageTypeAsync(context.GameId, context.InstallPath, context.PackageType, cancellationToken);
            }
            if (context.TaskFiles is null)
            {
                await gamePackageService.PrepareGamePackageAsync(context, cancellationToken);
            }
            TrackPrepared(context, reused, Stopwatch.GetElapsedTime(prepareStart));

            foreach (string item in Directory.GetFiles(context.InstallPath, "*", SearchOption.AllDirectories))
            {
                // 设置所有文件为正常状态，防止遇到只读文件报错
                File.SetAttributes(item, FileAttributes.Normal);
            }

            if (context.Operation is GameInstallOperation.Install)
            {
                // 安装
                await ExecuteInstallTaskAsync(context, cancellationToken);
            }
            else if (context.Operation is GameInstallOperation.Update)
            {
                // 更新
                await ExecuteUpdateTaskAsnyc(context, cancellationToken);
            }
            else if (context.Operation is GameInstallOperation.Predownload)
            {
                // 预下载
                await ExecutePredownloadTaskAsync(context, cancellationToken);
            }
            else if (context.Operation is GameInstallOperation.Repair)
            {
                // 修复
                await ExecuteRepairTaskAsync(context, cancellationToken);
            }
            else if (context.Operation is GameInstallOperation.RepairWPFPackage)
            {
                // 只修复千星沙箱
                await ExecuteRepairWPFPackageTaskAsync(context, cancellationToken);
            }
            else
            {
                _logger.LogWarning("GameInstallTask ({GameBiz}): Unsupported Operation: {operation}", context.GameId.GameBiz, context.Operation);
            }

            ClearDeprecatedFiles(context);

            context.State = GameInstallState.Finish;
            _logger.LogInformation("GameInstallTask Finished, GameBiz: {game_biz}, Operation: {operation}", context.GameId.GameBiz, context.Operation);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "PrepareGameInstallTaskAsync");
            context.State = GameInstallState.Error;
            context.ErrorMessage = ex.InnerException.Message;
            error = ex.InnerException;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GameInstallTask canceled, GameBiz: {game_biz}, Operation: {operation}, CancelState: {state}", context.GameId.GameBiz, context.Operation, context.CancelState);
            context.State = context.CancelState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrepareGameInstallTaskAsync");
            context.State = GameInstallState.Error;
            context.ErrorMessage = ex.Message;
            error = ex;
        }
        TrackEnd(context, error);
        ChangeToAnotherTask(context);
    }




    /// <summary>
    /// 切换到另一个任务
    /// </summary>
    /// <param name="context"></param>
    private void ChangeToAnotherTask(GameInstallContext context)
    {
        if (context.State is GameInstallState.Stop or GameInstallState.Finish)
        {
            _tasks.TryRemove(context.GameId, out _);
        }
        TaskStateChanged?.Invoke(this, context);
        // 按创建顺序先进先出：字典的枚举顺序不固定，一并更新硬链接的区服时本体可能排到后面
        if (_tasks.Values.Where(x => x.State is not GameInstallState.Paused and not GameInstallState.Error && x != context).MinBy(x => x.QueueSequence) is GameInstallContext anotherTask)
        {
            CurrentTask = anotherTask;
            StartOrContinueTask(anotherTask);
        }
        else
        {
            CurrentTask = null;
        }
    }




    /// <summary>
    /// 开始安装
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private async Task ExecuteInstallTaskAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        if (context.DownloadMode is GameInstallDownloadMode.Chunk)
        {
            await ExecuteInstallTaskDownloadModeChunkAsync(context, cancellationToken: cancellationToken);
        }
        else if (context.DownloadMode is GameInstallDownloadMode.CompressedPackage)
        {
            await ExecuteInstallTaskDownloadModePackageAsync(context, cancellationToken);
        }

        await DownloadWPFPackageAsync(context, cancellationToken);
        await DownloadGameChannelSDKAsync(context, cancellationToken);
        await SetGameConfigIniAsync(context);
    }



    /// <summary>
    /// 安装游戏，下载模式为 Chunk
    /// </summary>
    /// <param name="context"></param>
    /// <param name="maxDegreeOfParallelism">同时处理（校验、写入）的文件数，-1 为 CPU 核数</param>
    /// <returns></returns>
    private async Task ExecuteInstallTaskDownloadModeChunkAsync(GameInstallContext context, int maxDegreeOfParallelism = -1, CancellationToken cancellationToken = default)
    {
        long downloadBytes = 0, writeBytes = 0;
        foreach (GameInstallFile item in context.TaskFiles ?? [])
        {
            writeBytes += item.Size;
            foreach (GameInstallFileChunk chunk in item.Chunks ?? [])
            {
                downloadBytes += chunk.CompressedSize;
            }
        }
        context.Progress_DownloadTotalBytes = downloadBytes;
        context.Progress_DownloadFinishBytes = 0;
        context.Progress_WriteTotalBytes = writeBytes;
        context.Progress_WriteFinishBytes = 0;

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading in mode chunk, max concurrent chunk downloads: {count}", context.GameId.GameBiz, ChunkDownloadScheduler.MaxConcurrentDownloads);
        EnterStage(context, GameInstallState.Downloading);
        using ChunkDownloadScheduler scheduler = new();
        ParallelOptions options = new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(context.TaskFiles ?? [], options, async (GameInstallFile file, CancellationToken token) =>
        {
            await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadChunksToFileAsync(context, file, scheduler, false, token), token);
            file.IsFinished = true;
        });
    }



    /// <summary>
    /// 安装游戏，下载模式为 CompressedPackage
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private async Task ExecuteInstallTaskDownloadModePackageAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        var packages = context.TaskFiles!.SelectMany(x => x.CompressedPackages!).ToList();
        context.Progress_DownloadTotalBytes = packages.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading in mode package", context.GameId.GameBiz);
        EnterStage(context, GameInstallState.Downloading);
        await Parallel.ForEachAsync(packages, cancellationToken, async (GameInstallCompressedPackage package, CancellationToken token) =>
        {
            await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadToFileAsync(context, package.FullPath, package.Url, package.Size, package.MD5, token), token);
        });

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start decompressing in mode package", context.GameId.GameBiz);
        EnterStage(context, GameInstallState.Decompressing);
        context.Progress_Percent = 0;
        double totalSize = packages.Sum(x => x.Size);
        foreach (GameInstallFile item in context.TaskFiles ?? [])
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Decompress operation was canceled.", cancellationToken);
            }
            double ratio = item.Size / totalSize;
            if (item.IsFinished)
            {
                context.Progress_Percent += ratio;
                continue;
            }
            // SevenZipExtractor 解压被取消不会抛出异常
            await _gameInstallHelper.ExtractCompressedPackageAsync(context, item, ratio, cancellationToken);
            item.IsFinished = true;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Decompress operation was canceled.", cancellationToken);
        }

    }




    /// <summary>
    /// 开始更新
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private async Task ExecuteUpdateTaskAsnyc(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        // 如果音频包缓存目录和资源目录不一样，则移动缓存目录的文件到资源目录
        if (!string.IsNullOrWhiteSpace(context.GameConfig?.AudioPackageCacheDir)
            && !string.IsNullOrWhiteSpace(context.GameConfig?.AudioPackageResDir)
            && context.GameConfig.AudioPackageCacheDir != context.GameConfig.AudioPackageResDir)
        {
            string cache = Path.GetFullPath(Path.Combine(context.InstallPath, context.GameConfig.AudioPackageCacheDir));
            string res = Path.GetFullPath(Path.Combine(context.InstallPath, context.GameConfig.AudioPackageResDir));
            if (Directory.Exists(cache))
            {
                string[] files = Directory.GetFiles(cache, "*", SearchOption.AllDirectories);
                foreach (string source in files)
                {
                    string relative = Path.GetRelativePath(cache, source);
                    string target = Path.GetFullPath(relative, res);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(source, target, true);
                }
                _logger.LogInformation("GameInstallTask ({GameBiz}): Move audio package cache files (Count: {count}) from {cacheDir} to res dir {resDir}", context.GameId.GameBiz, files.Length, cache, res);
            }
        }

        if (context.DownloadMode is GameInstallDownloadMode.Patch)
        {
            await ExecuteUpdateTaskDownloadPatchAsync(context, cancellationToken);
        }
        else if (context.DownloadMode is GameInstallDownloadMode.Chunk)
        {
            await ExecuteUpdateTaskDownloadModeChunkAsync(context, cancellationToken);
        }
        else if (context.DownloadMode is GameInstallDownloadMode.CompressedPackage)
        {
            await ExecuteUpdateTaskDownloadPackageAsync(context, cancellationToken);
        }

        await DownloadWPFPackageAsync(context, cancellationToken);
        await DownloadGameChannelSDKAsync(context, cancellationToken);
        await SetGameConfigIniAsync(context, ("predownload", null));
    }




    /// <summary>
    /// 开始更新，下载模式为 Patch
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private async Task ExecuteUpdateTaskDownloadPatchAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        List<PredownloadFile> files = GameInstallHelper.GetPredownloadFiles(context);

        context.Progress_DownloadTotalBytes = files.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;
        EnterStage(context, GameInstallState.Downloading);

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading in mode patch", context.GameId.GameBiz);
        await Parallel.ForEachAsync(files, cancellationToken, async (PredownloadFile item, CancellationToken token) =>
        {
            await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadToFileAsync(context, item, token), token);
        });

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start merging in mode patch, file count: {count}", context.GameId.GameBiz, context.TaskFiles?.Count);
        EnterStage(context, GameInstallState.Merging);
        context.Progress_Percent = 0;
        double totalCount = context.TaskFiles?.Count ?? 1;
        double increase = 1 / totalCount;
        Lock _lock = new();
        await Parallel.ForEachAsync(context.TaskFiles ?? [], cancellationToken, async (GameInstallFile item, CancellationToken token) =>
        {
            if (item.IsFinished)
            {
                lock (_lock)
                {
                    context.Progress_Percent += increase;
                }
                return;
            }
            await _gameInstallHelper.PatchDiffFileAsync(context, item, cancellationToken);
            context.Progress_Percent += increase;
            item.IsFinished = true;
        });

        if (context.SophonPatchDeleteFiles is not null)
        {
            foreach (SophonPatchDeleteFile item in context.SophonPatchDeleteFiles)
            {
                string path = Path.Combine(context.InstallPath, item.File);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            _logger.LogInformation("GameInstallTask ({GameBiz}): Delete files by SophonPatchDeleteFiles, file count: {count}", context.GameId.GameBiz, context.SophonPatchDeleteFiles.Count);
        }

    }



    /// <summary>
    /// 开始更新，下载模式为 Chunk
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private async Task ExecuteUpdateTaskDownloadModeChunkAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        long downloadBytes = 0, writeBytes = 0;
        foreach (GameInstallFile item in context.TaskFiles ?? [])
        {
            writeBytes += item.Size;
            foreach (GameInstallFileChunk chunk in item.Chunks ?? [])
            {
                if (string.IsNullOrWhiteSpace(chunk.OriginalFileFullPath))
                {
                    downloadBytes += chunk.CompressedSize;
                }
            }
        }
        context.Progress_DownloadTotalBytes = downloadBytes;
        context.Progress_DownloadFinishBytes = 0;
        context.Progress_WriteTotalBytes = writeBytes;
        context.Progress_WriteFinishBytes = 0;

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading in mode chunk, max concurrent chunk downloads: {count}", context.GameId.GameBiz, ChunkDownloadScheduler.MaxConcurrentDownloads);
        EnterStage(context, GameInstallState.Downloading);
        using ChunkDownloadScheduler scheduler = new();
        await Parallel.ForEachAsync(context.TaskFiles ?? [], cancellationToken, async (GameInstallFile file, CancellationToken token) =>
        {
            await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadChunksToFileAsync(context, file, scheduler, true, token), token);
            file.IsFinished = true;
        });
        DeleteSophonChunkRemovedFiles(context);
    }



    /// <summary>
    /// 删除 Chunk 模式下新版本已移除的旧文件。必须在全部文件写完后调用，
    /// 因为更新途中还要从这些旧文件里复用块；单个文件删不掉只记日志，不影响更新结果。
    /// </summary>
    /// <param name="context"></param>
    private void DeleteSophonChunkRemovedFiles(GameInstallContext context)
    {
        if (context.SophonChunkDeleteFiles is not { Count: > 0 } deleteFiles)
        {
            return;
        }
        string installPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.InstallPath)) + Path.DirectorySeparatorChar;
        int count = 0;
        foreach (string file in deleteFiles)
        {
            string path = Path.GetFullPath(Path.Combine(installPath, file));
            // 清单里的路径都应在安装目录内，仍做一次防护，避免误删目录外的文件
            if (!path.StartsWith(installPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                continue;
            }
            try
            {
                File.Delete(path);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "GameInstallTask ({GameBiz}): Failed to delete file removed in new version: {path}", context.GameId.GameBiz, path);
            }
        }
        _logger.LogInformation("GameInstallTask ({GameBiz}): Delete files removed in new version, deleted {count} of {total}", context.GameId.GameBiz, count, deleteFiles.Count);
    }



    /// <summary>
    /// 开始更新，下载模式为 CompressedPackage
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    /// <exception cref="OperationCanceledException"></exception>
    private async Task ExecuteUpdateTaskDownloadPackageAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        List<PredownloadFile> files = GameInstallHelper.GetPredownloadFiles(context);

        context.Progress_DownloadTotalBytes = files.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;
        EnterStage(context, GameInstallState.Downloading);

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading in mode package", context.GameId.GameBiz);
        await Parallel.ForEachAsync(files, cancellationToken, async (PredownloadFile item, CancellationToken token) =>
        {
            await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadToFileAsync(context, item, token), token);
        });

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start decompressing in mode package", context.GameId.GameBiz);
        EnterStage(context, GameInstallState.Decompressing);
        context.Progress_Percent = 0;
        double totalSize = files.Sum(x => x.Size);
        foreach (GameInstallFile item in context.TaskFiles ?? [])
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The operation was canceled.", cancellationToken);
            }
            double ratio = item.Size / totalSize;
            if (item.IsFinished)
            {
                context.Progress_Percent += ratio;
                continue;
            }
            // SevenZipExtractor 解压被取消不会抛出异常
            await _gameInstallHelper.ExtractCompressedPackageAsync(context, item, ratio, cancellationToken);
            item.IsFinished = true;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The operation was canceled.", cancellationToken);
        }
    }




    /// <summary>
    /// 开始预下载
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private async Task ExecutePredownloadTaskAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        List<PredownloadFile> files = GameInstallHelper.GetPredownloadFiles(context);

        context.Progress_DownloadTotalBytes = files.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;
        EnterStage(context, GameInstallState.Downloading);

        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading in mode predownload", context.GameId.GameBiz);
        await Parallel.ForEachAsync(files, cancellationToken, async (PredownloadFile item, CancellationToken token) =>
        {
            await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadToFileAsync(context, item, token), token);
        });
        GamePackageService gamePackageService = _serviceProvider.GetRequiredService<GamePackageService>();
        string value = $"{context.LocalGameVersion},{context.PredownloadVersion},{context.AudioLanguage}";
        await SetGameConfigIniAsync(context, ("predownload", value));
    }



    /// <summary>
    /// 开始修复
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task ExecuteRepairTaskAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        // 如果音频包缓存目录和资源目录不一样，则移动缓存目录的文件到资源目录
        if (!string.IsNullOrWhiteSpace(context.GameConfig?.AudioPackageCacheDir)
            && !string.IsNullOrWhiteSpace(context.GameConfig?.AudioPackageResDir)
            && context.GameConfig.AudioPackageCacheDir != context.GameConfig.AudioPackageResDir)
        {
            string cache = Path.GetFullPath(Path.Combine(context.InstallPath, context.GameConfig.AudioPackageCacheDir));
            string res = Path.GetFullPath(Path.Combine(context.InstallPath, context.GameConfig.AudioPackageResDir));
            if (Directory.Exists(cache))
            {
                string[] files = Directory.GetFiles(cache, "*", SearchOption.AllDirectories);
                foreach (string source in files)
                {
                    string relative = Path.GetRelativePath(cache, source);
                    string target = Path.GetFullPath(relative, res);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(source, target, true);
                }
                _logger.LogInformation("GameInstallTask ({GameBiz}): Move audio package cache files (Count: {count}) from {cacheDir} to res dir {resDir}", context.GameId.GameBiz, files.Length, cache, res);
            }
        }

        if (context.DownloadMode is GameInstallDownloadMode.Chunk)
        {
            await ExecuteInstallTaskDownloadModeChunkAsync(context, GetRepairParallelism(context, chunkMode: true), cancellationToken);
            // 本地版本落后时修复等同于更新，同样要清掉新版本已移除的文件；版本一致时列表为空
            DeleteSophonChunkRemovedFiles(context);
        }
        else if (context.DownloadMode is GameInstallDownloadMode.SingleFile)
        {
            context.Progress_DownloadTotalBytes = context.TaskFiles!.Sum(x => x.Size);
            context.Progress_DownloadFinishBytes = 0;

            _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading in mode single file", context.GameId.GameBiz);
            EnterStage(context, GameInstallState.Downloading);
            ParallelOptions options = new() { MaxDegreeOfParallelism = GetRepairParallelism(context, chunkMode: false), CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(context.TaskFiles!, options, async (GameInstallFile file, CancellationToken token) =>
            {
                await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadToFileAsync(context, file, token), token);
                file.IsFinished = true;
            });
        }
        long relinked = Interlocked.Read(ref context._sharedInstallRelinkedFileCount);
        if (relinked > 0)
        {
            _logger.LogInformation("GameInstallTask ({GameBiz}): Relinked {count} files of other hard-linked installs to repaired files", context.GameId.GameBiz, relinked);
        }

        // todo celar useless audio
        // 与官方启动器一致：修复游戏资源不校验千星沙箱的文件内容，只在版本落后时更新；沙箱损坏走单独的千星沙箱修复
        await DownloadWPFPackageAsync(context, cancellationToken);
        await DownloadGameChannelSDKAsync(context, cancellationToken);
        await SetGameConfigIniAsync(context);
    }



    /// <summary>
    /// 只修复 WPF 包（原神的千星沙箱）。完成后只更新 config.ini 的 wpf_version，游戏版本等记录保持不变。
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task ExecuteRepairWPFPackageTaskAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        await RepairWPFPackageAsync(context, cancellationToken);
        await UpdateGameConfigIniAsync(context, ("wpf_version", context.WPFPackage?.Version));
    }



    /// <summary>
    /// 校验并修复 WPF 包（原神的千星沙箱），做法与官方启动器一致：本地记录的版本与官方相同时，按包内的文件清单逐个核对大小和 MD5；
    /// 版本不同、清单缺失，或有文件缺失、损坏时，重新下载整个压缩包并解压覆盖。
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task RepairWPFPackageAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        if (context.WPFPackage is null)
        {
            return;
        }
        string? localVersion = await GameInstallHelper.GetLocalWPFVersionAsync(context.InstallPath, cancellationToken);
        if (localVersion == context.WPFPackage.Version)
        {
            _logger.LogInformation("GameInstallTask ({GameBiz}): Start verifying WPFPackage {version}", context.GameId.GameBiz, localVersion);
            EnterStage(context, GameInstallState.Verifying);
            if (await _gameInstallHelper.VerifyWPFPackageAsync(context, GetRepairParallelism(context, chunkMode: false), cancellationToken))
            {
                _logger.LogInformation("GameInstallTask ({GameBiz}): WPFPackage is intact", context.GameId.GameBiz);
                return;
            }
        }
        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading WPFPackage, local version: {local}, latest version: {latest}", context.GameId.GameBiz, localVersion, context.WPFPackage.Version);
        EnterStage(context, GameInstallState.Downloading);
        string path = await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadWPFPackageFileAsync(context, token), cancellationToken);
        EnterStage(context, GameInstallState.Decompressing);
        await _gameInstallHelper.ExtractWPFPackageAsync(context, path, cancellationToken);
    }



    /// <summary>
    /// 官方启动器 chunk 模式的校验线程数（sophon getParamsConfig 的 chunk_max_validation_threads，2026-09 实测 SSD 与机械硬盘都是 6）
    /// </summary>
    private const int OfficialChunkVerifyThreads = 6;

    /// <summary>
    /// 官方启动器整包 / 单文件模式的校验线程数（getParamsConfig 的 max_validation_threads，2026-09 实测机械硬盘与未识别的磁盘为 1，SSD 为 2）
    /// </summary>
    private const int OfficialFileVerifyThreads = 1;


    /// <summary>
    /// 修复时同时校验的文件数：游戏在固态硬盘上沿用原方案（CPU 核数），否则按官方启动器的线程数，
    /// 避免机械硬盘、可移动存储和网络位置被多路并发读取拖慢。
    /// </summary>
    /// <param name="context"></param>
    /// <param name="chunkMode">是否为 chunk 模式；千星沙箱和单文件模式按整包 / 单文件的线程数</param>
    /// <returns></returns>
    private int GetRepairParallelism(GameInstallContext context, bool chunkMode)
    {
        bool ssd = DriveHelper.IsSolidStateDrive(context.InstallPath);
        int threads = ssd ? Environment.ProcessorCount : chunkMode ? OfficialChunkVerifyThreads : OfficialFileVerifyThreads;
        _logger.LogInformation("GameInstallTask ({GameBiz}): Repair verify threads: {threads}, solid state drive: {ssd}, chunk mode: {chunk}", context.GameId.GameBiz, threads, ssd, chunkMode);
        Telemetry.Track("task_verify_threads", context.GameId.GameBiz,
            ("op", context.Operation),
            ("txn", context.TransactionId),
            ("ssd", ssd),
            ("chunk", chunkMode),
            ("threads", threads));
        return threads;
    }



    /// <summary>
    /// 下载游戏渠道 SDK
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task DownloadGameChannelSDKAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading GameChannelSDK", context.GameId.GameBiz);
        await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadGameChannelSDKAsync(context, token), cancellationToken);
    }




    private async Task DownloadWPFPackageAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        if (context.WPFPackage is not null)
        {
            _logger.LogInformation("GameInstallTask ({GameBiz}): Start downloading WPFPackage", context.GameId.GameBiz);
            var state = context.State;
            context.State = GameInstallState.Downloading;
            await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadWPFPackageAsync(context, token), cancellationToken);
            context.State = state;
        }
    }



    /// <summary>
    /// 清理文件
    /// </summary>
    /// <param name="context"></param>
    private void ClearDeprecatedFiles(GameInstallContext context)
    {
        // 只修复千星沙箱时没取预下载版本，照常清理会把预下载的 chunk 缓存当成残留删掉
        if (context.Operation is not GameInstallOperation.Predownload and not GameInstallOperation.RepairWPFPackage)
        {
            int count = 0;
            foreach (GameInstallFile item in context.TaskFiles ?? [])
            {
                foreach (GameInstallCompressedPackage package in item.CompressedPackages ?? [])
                {
                    if (File.Exists(package.FullPath))
                    {
                        File.Delete(package.FullPath);
                        count++;
                    }
                }
            }
            foreach (GameDeprecatedFile item in context.DeprecatedFileConfig?.DeprecatedFiles ?? [])
            {
                string path = Path.Combine(context.InstallPath, item.Name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    count++;
                }
            }
            if (context.PredownloadVersion is null)
            {
                foreach (string file in Directory.GetFiles(context.InstallPath, "*_tmp", SearchOption.AllDirectories))
                {
                    File.Delete(file);
                    count++;
                }
                foreach (string file in Directory.GetFiles(context.InstallPath, "*.hdiff", SearchOption.AllDirectories))
                {
                    File.Delete(file);
                    count++;
                }
                string chunk = Path.Combine(context.InstallPath, "chunk");
                if (Directory.Exists(chunk))
                {
                    Directory.Delete(chunk, true);
                }
                string ldiff = Path.Combine(context.InstallPath, "ldiff");
                if (Directory.Exists(ldiff))
                {
                    Directory.Delete(ldiff, true);
                }
                string staging = Path.Combine(context.InstallPath, "staging");
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
            _logger.LogInformation("GameInstallTask ({GameBiz}): Clear deprecated files, count: {count}", context.GameId.GameBiz, count);
        }
    }




    /// <summary>
    /// 设置游戏的 config.ini
    /// </summary>
    /// <param name="context"></param>
    /// <param name="keyValuePairs"></param>
    /// <returns></returns>
    private async Task SetGameConfigIniAsync(GameInstallContext context, params IEnumerable<(string Key, string? Value)> keyValuePairs)
    {
        string path = Path.Join(context.InstallPath, "config.ini");
        IConfigurationRoot config = await ReadGameConfigIniAsync(path);
        if (context.Operation is GameInstallOperation.Predownload)
        {
            config["game_version"] = context.LocalGameVersion;
        }
        else
        {
            config["game_version"] = context.LatestGameVersion;
        }
        if (context.GameId.GameBiz.Server is "cn")
        {
            config["channel"] = "1";
            config["sub_channel"] = "1";
            config["cps"] = "hyp_mihoyo";
        }
        else if (context.GameId.GameBiz.Server is "global")
        {
            config["channel"] = "1";
            config["sub_channel"] = "0";
            config["cps"] = "hyp_hoyoverse";
        }
        else if (context.GameId.GameBiz.Server is "bilibili")
        {
            config["channel"] = "14";
            config["sub_channel"] = "0";
            config["cps"] = "hyp_mihoyo";
        }
        config["sdk_version"] = context.GameChannelSDK?.Version ?? "";
        config["game_biz"] = context.GameId.GameBiz;
        if (context.WPFPackage is not null)
        {
            config["wpf_version"] = context.WPFPackage.Version;
        }

        foreach ((string key, string? value) in keyValuePairs)
        {
            config[key] = value;
        }
        await WriteGameConfigIniAsync(context, path, config);
    }



    /// <summary>
    /// 只改 config.ini 里指定的键，其余记录（游戏版本、渠道等）保持原样
    /// </summary>
    /// <param name="context"></param>
    /// <param name="keyValuePairs">要修改的键值，值为 null 的跳过</param>
    /// <returns></returns>
    private async Task UpdateGameConfigIniAsync(GameInstallContext context, params IEnumerable<(string Key, string? Value)> keyValuePairs)
    {
        string path = Path.Join(context.InstallPath, "config.ini");
        IConfigurationRoot config = await ReadGameConfigIniAsync(path);
        foreach ((string key, string? value) in keyValuePairs)
        {
            if (value is not null)
            {
                config[key] = value;
            }
        }
        await WriteGameConfigIniAsync(context, path, config);
    }



    /// <summary>
    /// 读取 config.ini 已有的键（包括官方启动器写的 uapc 等），不存在时返回空配置。节头去掉，键直接放在根上
    /// </summary>
    /// <param name="path">config.ini 路径</param>
    /// <returns></returns>
    private static async Task<IConfigurationRoot> ReadGameConfigIniAsync(string path)
    {
        // 先按键去重（后出现的覆盖前面的），重复的键会让 IniStream 解析时抛异常
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            foreach (string line in await File.ReadAllLinesAsync(path))
            {
                string text = line.Trim();
                int index = text.IndexOf('=');
                if (text.StartsWith('[') || index <= 0)
                {
                    continue;
                }
                values[text[..index].Trim()] = text[(index + 1)..].Trim();
            }
        }
        using MemoryStream ms = new MemoryStream();
        using (StreamWriter sw = new StreamWriter(ms, leaveOpen: true))
        {
            foreach ((string key, string value) in values)
            {
                sw.WriteLine($"{key}={value}");
            }
        }
        // 写完流停在末尾，不回到开头 IniStream 什么也读不到：原来的写法因此每次都把已有的键全丢了
        ms.Position = 0;
        return new ConfigurationBuilder().AddIniStream(ms).Build();
    }



    /// <summary>
    /// 把配置写回 config.ini，全部键放在 [General] 节下
    /// </summary>
    /// <param name="context"></param>
    /// <param name="path">config.ini 路径</param>
    /// <param name="config">要写入的配置</param>
    /// <returns></returns>
    private async Task WriteGameConfigIniAsync(GameInstallContext context, string path, IConfigurationRoot config)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[General]");
        foreach (KeyValuePair<string, string?> item in config.AsEnumerable())
        {
            sb.AppendLine($"{item.Key}={item.Value}");
        }
        Directory.CreateDirectory(context.InstallPath);
        await File.WriteAllTextAsync(path, sb.ToString());
        _logger.LogInformation("GameInstallTask ({GameBiz}): Set config.ini, path: {path}", context.GameId.GameBiz, path);
    }




    #region Telemetry



    /// <summary>
    /// 记录一轮运行开始（新任务或继续），并重置本轮计时与流量基线
    /// </summary>
    /// <param name="context">即将开始的任务</param>
    private static void TrackStart(GameInstallContext context)
    {
        context.RunStartTimestamp = Stopwatch.GetTimestamp();
        context.RunStartNetworkBytes = Interlocked.Read(ref context.networkDownloadBytes);
        context.RunStage = GameInstallState.Waiting;
        Telemetry.Track("task_start", context.GameId.GameBiz,
            ("op", context.Operation),
            ("txn", context.TransactionId),
            ("start_status", context.TaskFiles is null ? "new" : "continue"),
            ("prev_state", context.State),
            ("audio", context.AudioLanguage),
            ("hard_link", !string.IsNullOrWhiteSpace(context.HardLinkPath)));
    }



    /// <summary>
    /// 记录准备阶段的结果：版本、build、下载方式与文件数
    /// </summary>
    /// <param name="context">已准备好的任务</param>
    /// <param name="reused">true 表示沿用上一轮的文件清单，本轮没有重新拉取 build</param>
    /// <param name="elapsed">准备耗时</param>
    private static void TrackPrepared(GameInstallContext context, bool reused, TimeSpan elapsed)
    {
        Telemetry.Track("task_prepared", context.GameId.GameBiz,
            ("op", context.Operation),
            ("txn", context.TransactionId),
            ("reused", reused),
            ("duration_ms", elapsed),
            ("mode", context.DownloadMode),
            ("latest", context.LatestGameVersion),
            ("local", context.LocalGameVersion),
            ("predownload", context.PredownloadVersion),
            ("build", context.GameSophonChunkBuild?.BuildId),
            ("build_tag", context.GameSophonChunkBuild?.Tag),
            ("local_build_tag", context.LocalVersionSophonChunkBuild?.Tag),
            ("patch_build", context.GameSophonPatchBuild?.BuildId),
            ("package", context.GamePackage?.Main.Major?.Version),
            ("files", context.TaskFiles?.Count ?? 0),
            ("files_done", context.TaskFiles?.Count(x => x.IsFinished) ?? 0),
            ("write_bytes", context.TaskFiles?.Sum(x => x.Size) ?? 0),
            ("delete_files", context.SophonChunkDeleteFiles?.Count ?? 0));
    }



    /// <summary>
    /// 进入下载 / 解压 / 合并阶段，并记录该阶段开始时的总量与已完成文件数
    /// </summary>
    /// <param name="context">当前任务</param>
    /// <param name="stage">进入的阶段</param>
    private static void EnterStage(GameInstallContext context, GameInstallState stage)
    {
        context.State = stage;
        context.RunStage = stage;
        Telemetry.Track("task_stage", context.GameId.GameBiz,
            ("op", context.Operation),
            ("txn", context.TransactionId),
            ("stage", stage),
            ("mode", context.DownloadMode),
            ("download_total", context.Progress_DownloadTotalBytes),
            ("write_total", context.Progress_WriteTotalBytes),
            ("files", context.TaskFiles?.Count ?? 0),
            ("files_done", context.TaskFiles?.Count(x => x.IsFinished) ?? 0));
    }



    /// <summary>
    /// 记录一轮运行结束。结果取最终状态（Finish / Paused / Stop / Queueing / Error），并带本轮耗时、流量与进度
    /// </summary>
    /// <param name="context">刚结束的任务</param>
    /// <param name="error">出错时的异常，取消或成功为 null</param>
    private static void TrackEnd(GameInstallContext context, Exception? error)
    {
        TimeSpan elapsed = context.RunStartTimestamp > 0 ? Stopwatch.GetElapsedTime(context.RunStartTimestamp) : TimeSpan.Zero;
        long network = Interlocked.Read(ref context.networkDownloadBytes) - context.RunStartNetworkBytes;
        Telemetry.Track("task_end", context.GameId.GameBiz,
            ("op", context.Operation),
            ("txn", context.TransactionId),
            ("result", context.State),
            ("stage", context.RunStage),
            ("duration_ms", elapsed),
            ("network_bytes", network),
            ("avg_speed", elapsed.TotalSeconds > 0 ? (long)(network / elapsed.TotalSeconds) : 0L),
            ("download_finish", context.Progress_DownloadFinishBytes),
            ("download_total", context.Progress_DownloadTotalBytes),
            ("write_finish", context.Progress_WriteFinishBytes),
            ("write_total", context.Progress_WriteTotalBytes),
            ("files", context.TaskFiles?.Count ?? 0),
            ("files_done", context.TaskFiles?.Count(x => x.IsFinished) ?? 0),
            ("shared_relinked", Interlocked.Read(ref context._sharedInstallRelinkedFileCount)),
            ("error_type", error?.GetType().Name),
            ("error", error?.Message));
    }



    /// <summary>
    /// 记录一次取消请求（暂停、停止、卸载、被抢占等）
    /// </summary>
    /// <param name="gameId">请求针对的游戏</param>
    /// <param name="context">找到的任务，没找到为 null</param>
    /// <param name="cancelState">取消后期望进入的状态</param>
    /// <param name="source">取消来源：pause / stop / uninstall / replace / preempt / parent_exit</param>
    internal static void TrackCancel(GameId gameId, GameInstallContext? context, GameInstallState cancelState, string source)
    {
        // running=false 时任务本就没在跑，Cancel 只会改 CancelState、不会改变任务状态
        bool running = context?.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying;
        Telemetry.Track("task_cancel", gameId.GameBiz,
            ("source", source),
            ("cancel_state", cancelState),
            ("found", context is not null),
            ("op", context?.Operation),
            ("txn", context?.TransactionId),
            ("prev_state", context?.State),
            ("running", running));
    }



    #endregion



}


