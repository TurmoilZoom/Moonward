using Starward.Core;
using Starward.Core.HoYoPlay;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC.GameInstall;

/// <summary>
/// 游戏安装上下文
/// </summary>
public class GameInstallContext
{

    public GameId GameId { get; init; }

    /// <summary>
    /// 安装路径
    /// </summary>
    public string InstallPath { get; init; }

    public GameInstallOperation Operation { get; set; }

    /// <summary>
    /// 发起任务时请求的操作。硬链接时 RPC 会把安装、更新改成修复（直接链接到另一个区服已是最新的文件），
    /// <see cref="Operation"/> 随之变成修复，这里保持用户点的操作，界面据此决定提示
    /// </summary>
    public GameInstallOperation RequestedOperation { get; set; }

    public AudioLanguage AudioLanguage { get; init; }

    /// <summary>
    /// 硬链接游戏的路径
    /// </summary>
    public string? HardLinkPath { get; init; }


    public long Timestamp { get; set; }

    public GameInstallState State { get; set; }

    public string? ErrorMessage { get; set; }

    public GameInstallDownloadMode DownloadMode { get; set; }

    /// <summary>
    /// 需要下载的总字节数
    /// </summary>
    public long Progress_DownloadTotalBytes { get; set; }
    /// <summary>
    /// 已下载的字节数
    /// </summary>
    public long Progress_DownloadFinishBytes { get => _progress_DownloadFinishBytes; set => _progress_DownloadFinishBytes = value; }
    internal long _progress_DownloadFinishBytes;

    /// <summary>
    /// 需要读取的总字节数，没用到
    /// </summary>
    public long Progress_ReadTotalBytes { get; set; }
    /// <summary>
    /// 已读取的字节数，没用到
    /// </summary>
    public long Progress_ReadFinishBytes { get => _progress_ReadFinishBytes; set => _progress_ReadFinishBytes = value; }
    internal long _progress_ReadFinishBytes;

    /// <summary>
    /// 需要写入的总字节数
    /// </summary>
    public long Progress_WriteTotalBytes { get; set; }
    /// <summary>
    /// 已写入的字节数
    /// </summary>
    public long Progress_WriteFinishBytes { get => _progress_WriteFinishBytes; set => _progress_WriteFinishBytes = value; }
    internal long _progress_WriteFinishBytes;

    /// <summary>
    /// 解压和合并时的百分比进度，最大值是 1
    /// </summary>
    public double Progress_Percent { get; set; }


    /// <summary>
    /// 网络下载速度，单位是字节每秒
    /// </summary>
    public long NetworkDownloadSpeed { get; set; }

    /// <summary>
    /// 存储读取速度，单位是字节每秒
    /// </summary>
    public long StorageReadSpeed { get; set; }

    /// <summary>
    /// 存储写入速度，单位是字节每秒
    /// </summary>
    public long StorageWriteSpeed { get; set; }

    /// <summary>
    /// 预计剩余时间，仅用于预计下载剩余时间，单位是秒
    /// </summary>
    public long RemainTimeSeconds { get; set; }

    /// <summary>
    /// 校验不通过、被重新下载或重建的文件数，重试时可能重复计数。修复结束时为 0 表示本地文件本来就完整
    /// </summary>
    public long RewrittenFileCount { get => _rewrittenFileCount; set => _rewrittenFileCount = value; }
    internal long _rewrittenFileCount;

    /// <summary>
    /// 修复时一并重新链接到修好文件的其他硬链接区服文件数，只用于日志与埋点
    /// </summary>
    internal long _sharedInstallRelinkedFileCount;

    /// <summary>
    /// 修复时判断过的其他硬链接区服目录，值为能否一并修复。同一目录的 config.ini 只读一次
    /// </summary>
    internal ConcurrentDictionary<string, bool> SharedInstallRoots { get; } = new(StringComparer.OrdinalIgnoreCase);



    internal string? LocalGameVersion { get; set; }

    internal string? LatestGameVersion { get; set; }

    internal string? PredownloadVersion { get; set; }



    internal GameConfig? GameConfig { get; set; }

    internal GamePackage? GamePackage { get; set; }

    internal GameSophonChunkBuild? GameSophonChunkBuild { get; set; }

    internal GameSophonChunkBuild? LocalVersionSophonChunkBuild { get; set; }

    internal GameSophonPatchBuild? GameSophonPatchBuild { get; set; }

    internal GameChannelSDK? GameChannelSDK { get; set; }

    internal GameDeprecatedFileConfig? DeprecatedFileConfig { get; set; }

    internal WPFPackage? WPFPackage { get; set; }



    internal List<SophonChunkFile>? SophonChunkFiles { get; set; }

    internal List<SophonChunkFile>? LocalVersionSophonChunkFiles { get; set; }

    /// <summary>
    /// Chunk 模式下旧版本有、新版本没有的文件（相对安装目录）。更新途中还要从中复用块，全部文件写完后才删除
    /// </summary>
    internal List<string>? SophonChunkDeleteFiles { get; set; }

    internal List<SophonPatchFile>? SophonPatchFiles { get; set; }

    internal List<SophonPatchDeleteFile>? SophonPatchDeleteFiles { get; set; }

    internal List<GameInstallFile>? TaskFiles { get; set; }



    public long networkDownloadBytes = 0;

    public long storageReadBytes = 0;

    public long storageWriteBytes = 0;

    private long lastNetworkBytes = 0;

    private long lastStorageReadBytes = 0;

    private long lastStorageWriteBytes = 0;

    private long lastTimestamp = 0;


    private CancellationTokenSource? _cancellationTokenSource;

    internal CancellationToken CancellationToken => GetCancellation();


    private CancellationToken GetCancellation()
    {
        if (_cancellationTokenSource is null or { IsCancellationRequested: true })
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }
        return _cancellationTokenSource.Token;
    }


    internal GameInstallState CancelState { get; private set; } = GameInstallState.Queueing;


    /// <summary>
    /// 最近一轮运行的协程，从未运行过为 null。取消只是发信号，要等它结束才算任务真正退出
    /// </summary>
    internal Task? RunTask { get; set; }


    /// <summary>
    /// 埋点流水号：同一上下文跨暂停 / 继续保持不变，换操作或重建任务才会变
    /// </summary>
    internal string TransactionId { get; } = Guid.NewGuid().ToString("N")[..12];


    private static long s_queueSequence;

    /// <summary>
    /// 创建顺序，排队的任务按它先进先出。一并更新硬链接的区服时要靠它保证本体先更新
    /// </summary>
    internal long QueueSequence { get; } = Interlocked.Increment(ref s_queueSequence);

    /// <summary>
    /// 本轮运行开始的时间戳（<see cref="Stopwatch"/>），埋点算耗时
    /// </summary>
    internal long RunStartTimestamp;

    /// <summary>
    /// 本轮运行开始时的累计网络字节数，埋点算本轮流量
    /// </summary>
    internal long RunStartNetworkBytes;

    /// <summary>
    /// 本轮运行最后进入的阶段，埋点记录任务停在哪一步
    /// </summary>
    internal GameInstallState RunStage;


    internal void Cancel(GameInstallState state)
    {
        CancelState = state;
        _cancellationTokenSource?.Cancel();
    }



    internal void RefreshSpeed()
    {
        long ts = Stopwatch.GetTimestamp();
        Timestamp = ts;
        if (ts - lastTimestamp < Stopwatch.Frequency)
        {
            // 每秒更新一次
            return;
        }

        long currentDownload = networkDownloadBytes;
        long currentRead = storageReadBytes;
        long currentWrite = storageWriteBytes;
        long time = ts - lastTimestamp;

        NetworkDownloadSpeed = (currentDownload - lastNetworkBytes) * Stopwatch.Frequency / time;
        StorageReadSpeed = (currentRead - lastStorageReadBytes) * Stopwatch.Frequency / time;
        StorageWriteSpeed = (currentWrite - lastStorageWriteBytes) * Stopwatch.Frequency / time;
        RemainTimeSeconds = NetworkDownloadSpeed > 0 ? (Progress_DownloadTotalBytes - Progress_DownloadFinishBytes) / NetworkDownloadSpeed : 0;

        lastTimestamp = ts;
        lastNetworkBytes = currentDownload;
        lastStorageReadBytes = currentRead;
        lastStorageWriteBytes = currentWrite;
    }


}



public partial class GameInstallContextDTO
{

    public GameId GetGameId() => new GameId { GameBiz = GameBiz, Id = GameId };


    public GameInstallContext UpdateTask(GameInstallContext? task = null)
    {
        task ??= new GameInstallContext
        {
            AudioLanguage = (AudioLanguage)AudioLanguage,
            GameId = GetGameId(),
            HardLinkPath = HardLinkPath,
            InstallPath = InstallPath,
        };
        task.Operation = (GameInstallOperation)Operation;
        task.RequestedOperation = (GameInstallOperation)RequestedOperation;
        task.Timestamp = Timestamp;
        task.State = (GameInstallState)State;
        task.Progress_DownloadTotalBytes = ProgressDownloadTotalBytes;
        task.Progress_DownloadFinishBytes = ProgressDownloadFinishBytes;
        task.Progress_ReadTotalBytes = ProgressReadTotalBytes;
        task.Progress_ReadFinishBytes = ProgressReadFinishBytes;
        task.Progress_WriteTotalBytes = ProgressWriteTotalBytes;
        task.Progress_WriteFinishBytes = ProgressWriteFinishBytes;
        task.Progress_Percent = ProgressPercent;
        task.ErrorMessage = ErrorMessage;
        task.NetworkDownloadSpeed = NetworkDownloadSpeed;
        task.StorageReadSpeed = StorageReadSpeed;
        task.StorageWriteSpeed = StorageWriteSpeed;
        task.RemainTimeSeconds = RemainTimeSeconds;
        task.RewrittenFileCount = RewrittenFileCount;
        task.DownloadMode = (GameInstallDownloadMode)DownloadMode;
        return task;
    }



    public static GameInstallContextDTO FromTask(GameInstallContext task) => new GameInstallContextDTO
    {
        AudioLanguage = (int)task.AudioLanguage,
        GameBiz = task.GameId.GameBiz,
        GameId = task.GameId.Id,
        HardLinkPath = task.HardLinkPath,
        InstallPath = task.InstallPath,
        Operation = (int)task.Operation,
        RequestedOperation = (int)task.RequestedOperation,
        Timestamp = task.Timestamp,
        State = (int)task.State,
        ProgressDownloadTotalBytes = task.Progress_DownloadTotalBytes,
        ProgressDownloadFinishBytes = task.Progress_DownloadFinishBytes,
        ProgressReadTotalBytes = task.Progress_ReadTotalBytes,
        ProgressReadFinishBytes = task.Progress_ReadFinishBytes,
        ProgressWriteTotalBytes = task.Progress_WriteTotalBytes,
        ProgressWriteFinishBytes = task.Progress_WriteFinishBytes,
        ProgressPercent = task.Progress_Percent,
        ErrorMessage = task.ErrorMessage,
        NetworkDownloadSpeed = task.NetworkDownloadSpeed,
        StorageReadSpeed = task.StorageReadSpeed,
        StorageWriteSpeed = task.StorageWriteSpeed,
        RemainTimeSeconds = task.RemainTimeSeconds,
        RewrittenFileCount = task.RewrittenFileCount,
        DownloadMode = (int)task.DownloadMode,
    };

}


public partial class GameInstallRequest
{

    public GameId GetGameId() => new GameId { GameBiz = GameBiz, Id = GameId };


    public GameInstallContext ToTask() => new GameInstallContext
    {
        AudioLanguage = (AudioLanguage)AudioLanguage,
        GameId = GetGameId(),
        HardLinkPath = HardLinkPath,
        InstallPath = InstallPath,
        Operation = (GameInstallOperation)Operation,
        RequestedOperation = (GameInstallOperation)Operation,
    };


    public static GameInstallRequest FromTask(GameInstallContext task)
    {
        return new GameInstallRequest
        {
            GameBiz = task.GameId.GameBiz,
            GameId = task.GameId.Id,
            InstallPath = task.InstallPath,
            Operation = (int)task.Operation,
            AudioLanguage = (int)task.AudioLanguage,
            HardLinkPath = task.HardLinkPath,
        };
    }


}