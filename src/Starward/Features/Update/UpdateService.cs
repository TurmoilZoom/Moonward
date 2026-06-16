using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Starward.Features.Update;

/// <summary>
/// 基于 Velopack 的应用自更新服务，更新源为 GitHub Releases（<see cref="RepoUrl"/>）。
/// 「加入预览更新」对应 GitHub 的 pre-release（<see cref="AppConfig.EnablePreviewRelease"/> → GithubSource 的 prerelease 标志）。
/// 增量(delta)更新由 Velopack 自动处理。
/// </summary>
internal class UpdateService
{

    /// <summary>
    /// 更新源仓库地址。
    /// </summary>
    public const string RepoUrl = "https://github.com/TurmoilZoom/Starward";


    private readonly ILogger<UpdateService> _logger;


    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
    }



    private UpdateManager? _manager;

    private bool _managerPrerelease;

    private UpdateInfo? _downloadedUpdate;

    private bool _isUpdating;

    private CancellationTokenSource? _cancellationTokenSource;



    public static bool UpdateFinished { get; private set; }

    public UpdateState State { get; private set; }

    public long Progress_TotalBytes { get; private set; }

    private long _progress_DownloadBytes;
    public long Progress_DownloadBytes => _progress_DownloadBytes;

    public int Progress_Percent { get; private set; }

    public string? ErrorMessage { get; private set; }



    /// <summary>
    /// 当前是否由 Velopack 安装/便携部署（即是否可自动更新）。开发态(F5/裸发布目录)为 false。
    /// </summary>
    public bool IsUpdaterAvailable
    {
        get
        {
            try
            {
                return GetManager().IsInstalled;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Check updater available");
                return false;
            }
        }
    }



    private UpdateManager GetManager()
    {
        bool prerelease = AppConfig.EnablePreviewRelease;
        if (_manager is null || _managerPrerelease != prerelease)
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease));
            _managerPrerelease = prerelease;
        }
        return _manager;
    }



    /// <summary>
    /// 检查更新（尊重「忽略此版本」设置）。返回可更新的 <see cref="UpdateInfo"/>，否则 null。
    /// </summary>
    public async Task<UpdateInfo?> CheckUpdateAsync(bool disableIgnore = false)
    {
        UpdateInfo? info = await GetLatestVersionAsync();
        if (info is null)
        {
            return null;
        }
        if (!disableIgnore)
        {
            _ = NuGetVersion.TryParse(AppConfig.IgnoreVersion, out var ignoreVersion);
            _ = NuGetVersion.TryParse(info.TargetFullRelease?.Version.ToString(), out var newVersion);
            if (ignoreVersion is not null && newVersion is not null && newVersion <= ignoreVersion)
            {
                return null;
            }
        }
        return info;
    }



    /// <summary>
    /// 获取最新版本信息（不过滤「忽略此版本」），无更新或不可更新时返回 null。
    /// </summary>
    public async Task<UpdateInfo?> GetLatestVersionAsync(CancellationToken cancellation = default)
    {
        var manager = GetManager();
        if (!manager.IsInstalled)
        {
            _logger.LogInformation("Not a Velopack install, skip update check.");
            return null;
        }
        var info = await manager.CheckForUpdatesAsync();
        _logger.LogInformation("Current version: {currentVersion}, latest version: {latestVersion}.", manager.CurrentVersion, info?.TargetFullRelease?.Version);
        return info;
    }



    /// <summary>
    /// 下载更新包（含增量），完成后置 <see cref="UpdateState.Finish"/>，等待调用 <see cref="ApplyAndRestart"/>。
    /// </summary>
    public async Task StartUpdateAsync(UpdateInfo release)
    {
        if (_isUpdating || UpdateFinished)
        {
            State = UpdateFinished ? UpdateState.Finish : State;
            return;
        }
        try
        {
            ClearState();
            _isUpdating = true;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            State = UpdateState.Pending;

            var manager = GetManager();
            if (!manager.IsInstalled)
            {
                // 非 Velopack 部署，无法自动更新（需手动下载安装包）。
                ErrorMessage = Lang.UpdateService_CannotUpdateAutomatically;
                State = UpdateState.NotSupport;
                return;
            }

            Progress_TotalBytes = release.TargetFullRelease?.Size ?? 0;
            _progress_DownloadBytes = 0;
            Progress_Percent = 0;
            State = UpdateState.Downloading;
            await manager.DownloadUpdatesAsync(release, OnDownloadProgress, _cancellationTokenSource.Token);
            _downloadedUpdate = release;

            await Task.Delay(500, _cancellationTokenSource.Token);
            State = UpdateState.Finish;
            UpdateFinished = true;
            _logger.LogInformation("Update downloaded: {version}", release.TargetFullRelease?.Version);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update canceled.");
            State = UpdateState.Stop;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start update");
            State = UpdateState.Error;
            ErrorMessage = ex.Message;
        }
        finally
        {
            _isUpdating = false;
        }
    }



    private void OnDownloadProgress(int percent)
    {
        Progress_Percent = percent;
        if (Progress_TotalBytes > 0)
        {
            _progress_DownloadBytes = (long)(Progress_TotalBytes * (percent / 100.0));
        }
    }



    /// <summary>
    /// 应用已下载的更新并重启应用（由 Velopack 的 Update.exe 完成文件替换后重启）。
    /// 调用前应确保后台子进程（如 RPC）会随主进程退出。
    /// </summary>
    public void ApplyAndRestart()
    {
        var manager = GetManager();
        if (!manager.IsInstalled)
        {
            return;
        }
        // _downloadedUpdate 为 null 时（如重开更新窗口）传 null，Velopack 会应用已下载/暂存的最新包。
        _logger.LogInformation("Apply update and restart: {version}", _downloadedUpdate?.TargetFullRelease?.Version);
        manager.ApplyUpdatesAndRestart(_downloadedUpdate?.TargetFullRelease);
    }



    public void StopUpdate()
    {
        _cancellationTokenSource?.Cancel();
    }



    private void ClearState()
    {
        State = UpdateState.Stop;
        Progress_TotalBytes = 0;
        _progress_DownloadBytes = 0;
        Progress_Percent = 0;
        ErrorMessage = null;
    }



}
