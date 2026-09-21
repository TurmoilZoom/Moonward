using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Features.Database;
using Starward.Features.PlayTime;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.LanSync;


/// <summary>
/// 拉取进度所处阶段。
/// </summary>
public enum LanSyncStage
{
    /// <summary>正在接收快照。</summary>
    Receiving,

    /// <summary>正在备份本机数据库并合并记录。</summary>
    Merging,
}


/// <summary>
/// 拉取进度。
/// </summary>
public readonly record struct LanSyncProgress(LanSyncStage Stage, long BytesReceived, long BytesTotal);



/// <summary>
/// 局域网同步：开始共享本机数据，或从另一台设备拉取记录合并到本机。
/// </summary>
/// <remarks>
/// 同步只把对方有、本机没有的记录加到本机，不修改或删除任何设备上的已有记录；
/// 米游社 / HoYoLAB 账号的 Cookie 与设备指纹不在同步范围内（见 <see cref="LanSyncSnapshot"/>）。
/// </remarks>
internal sealed class LanSyncService
{

    private static readonly TimeSpan DiscoveryDuration = TimeSpan.FromSeconds(2);


    private readonly ILogger<LanSyncService> _logger;

    private readonly PlayTimeStatsService _playTimeStatsService;

    /// <summary>同一时间只允许一次拉取合并。</summary>
    private readonly SemaphoreSlim _pullLock = new(1, 1);



    public LanSyncService(ILogger<LanSyncService> logger, PlayTimeStatsService playTimeStatsService)
    {
        _logger = logger;
        _playTimeStatsService = playTimeStatsService;
    }



    /// <summary>
    /// 开始共享本机数据。
    /// </summary>
    /// <returns>运行中的共享端，停止共享时 Dispose。</returns>
    /// <exception cref="System.Net.Sockets.SocketException">端口监听失败。</exception>
    public LanSyncServer StartSharing()
    {
        return LanSyncServer.Start(_logger);
    }


    /// <summary>
    /// 在后台统计本机可共享的记录数。
    /// </summary>
    public Task<LanSyncCounts> GetLocalSummaryAsync()
    {
        return Task.Run(LanSyncSnapshot.GetLocalSummary);
    }


    /// <summary>
    /// 在局域网里搜索正在共享的设备，约 2 秒。
    /// </summary>
    public Task<List<LanSyncPeer>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return LanSyncClient.DiscoverAsync(DiscoveryDuration, cancellationToken);
    }


    /// <summary>
    /// 按手动输入的地址连接共享端，取回设备名与版本。
    /// </summary>
    /// <param name="address">IP、IP:端口或计算机名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="LanSyncException">地址无效或连接失败。</exception>
    public async Task<LanSyncPeer> ConnectAsync(string address, CancellationToken cancellationToken = default)
    {
        IPEndPoint endPoint = await LanSyncNetwork.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
        return await LanSyncClient.HelloAsync(endPoint, cancellationToken).ConfigureAwait(false);
    }



    /// <summary>
    /// 从共享端拉取快照，备份本机数据库后把本机没有的记录合并进来。
    /// </summary>
    /// <param name="peer">共享端。</param>
    /// <param name="code">对方显示的验证码。</param>
    /// <param name="categories">要合并的类别。</param>
    /// <param name="progress">进度；在后台线程调用 <see cref="IProgress{T}.Report"/>，传 <see cref="Progress{T}"/> 可自动切回 UI 线程。</param>
    /// <param name="cancellationToken">只在接收阶段生效，开始合并后不再中断，避免事务做到一半。</param>
    /// <returns>各类别新增的记录数。</returns>
    /// <exception cref="LanSyncException">连接、验证码或传输失败。</exception>
    public async Task<LanSyncCounts> PullAsync(LanSyncPeer peer, string code, LanSyncCategories categories, IProgress<LanSyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _pullLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? snapshotFile = null;
        try
        {
            snapshotFile = await LanSyncClient.DownloadSnapshotAsync(peer.EndPoint, code, (received, total) =>
            {
                progress?.Report(new LanSyncProgress(LanSyncStage.Receiving, received, total));
            }, cancellationToken).ConfigureAwait(false);

            progress?.Report(new LanSyncProgress(LanSyncStage.Merging, 0, 0));
            string file = snapshotFile;
            LanSyncCounts result = await Task.Run(() =>
            {
                LanSyncSnapshot.BackupBeforeMerge();
                LanSyncCounts merged = LanSyncSnapshot.Merge(file, categories, out List<string> playTimeBizs);
                if (merged.PlayTime > 0)
                {
                    RefreshPlayTimeTotals(playTimeBizs);
                }
                return merged;
            }, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("LAN sync from {device} ({address}) added {gacha} gacha, {record} game record and {playtime} playtime rows.",
                peer.DeviceName, peer.EndPoint, result.Gacha, result.GameRecord, result.PlayTime);
            return result;
        }
        finally
        {
            LanSyncSnapshot.DeleteTempFile(snapshotFile);
            _pullLock.Release();
        }
    }


    /// <summary>
    /// 合并了新会话后重算缓存的总时长，首页按钮读的是这份缓存。
    /// </summary>
    private void RefreshPlayTimeTotals(IEnumerable<string> bizs)
    {
        foreach (string value in bizs)
        {
            if (GameBiz.TryParse(value, out GameBiz biz) && biz.IsKnown())
            {
                DatabaseService.SetValue(PlayTimeStatsService.TotalPlayTimeKey(biz), _playTimeStatsService.GetPlayTimeTotal(biz));
            }
        }
    }

}
