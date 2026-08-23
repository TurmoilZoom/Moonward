using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Starward.Features.RPC;
using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Starward.Features.Update;

/// <summary>
/// 基于 Velopack 的应用自更新服务，更新包下载源为 CNB Releases（<see cref="RepoUrl"/>）。
/// 「加入预览更新」对应 CNB 的 pre-release（<see cref="AppConfig.EnablePreviewRelease"/> → <see cref="CnbSource"/> 的 prerelease 标志）。
/// 发行说明仍由 <c>ReleaseClient</c> 从 GitHub 拉取。增量(delta)更新由 Velopack 自动处理。
/// </summary>
internal class UpdateService
{

    /// <summary>
    /// CNB 更新源仓库地址。
    /// </summary>
    public const string RepoUrl = "https://cnb.cool/TurmoilZoom/Starward";

    /// <summary>
    /// GitHub 更新源仓库地址（Velopack <see cref="GithubSource"/>）。
    /// </summary>
    public const string GitHubRepoUrl = "https://github.com/TurmoilZoom/Starward";


    private readonly ILogger<UpdateService> _logger;


    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
    }



    private UpdateManager? _cnbManager;

    private UpdateManager? _githubManager;

    private bool _managerPrerelease;

    private UpdateDownloadSource _lastDownloadSource = UpdateDownloadSource.Cnb;

    private UpdateInfo? _downloadedUpdate;

    private bool _isUpdating;

    private bool _applyOnExitScheduled;

    private CancellationTokenSource? _cancellationTokenSource;



    public static bool UpdateFinished { get; private set; }

    /// <summary>
    /// 当前是否正在下载更新包（含静默下载）。
    /// </summary>
    public bool IsUpdating => _isUpdating;

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



    /// <summary>
    /// 获取指定下载源的 <see cref="UpdateManager"/>；检查更新始终使用 CNB。
    /// </summary>
    /// <param name="source">下载源；默认 CNB。</param>
    /// <returns>与 <paramref name="source"/> 及当前预览开关绑定的管理器实例。</returns>
    private UpdateManager GetManager(UpdateDownloadSource source = UpdateDownloadSource.Cnb)
    {
        bool prerelease = AppConfig.EnablePreviewRelease;
        if (_managerPrerelease != prerelease)
        {
            _cnbManager = null;
            _githubManager = null;
            _managerPrerelease = prerelease;
        }

        if (source is UpdateDownloadSource.GitHub)
        {
            _githubManager ??= new UpdateManager(new GithubSource(GitHubRepoUrl, null, prerelease));
            return _githubManager;
        }

        _cnbManager ??= new UpdateManager(new CnbSource(RepoUrl, null, prerelease));
        return _cnbManager;
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
    /// <param name="release">检查更新阶段得到的版本信息（CNB 源）；GitHub 源时会重新拉取与该源绑定的 <see cref="UpdateInfo"/>。</param>
    /// <param name="source">下载源，默认 CNB。</param>
    public async Task StartUpdateAsync(UpdateInfo release, UpdateDownloadSource source = UpdateDownloadSource.Cnb)
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

            var manager = GetManager(source);
            if (!manager.IsInstalled)
            {
                // 非 Velopack 部署，无法自动更新（需手动下载安装包）。
                ErrorMessage = Lang.UpdateService_CannotUpdateAutomatically;
                State = UpdateState.NotSupport;
                return;
            }

            // UpdateInfo 与 IUpdateSource 绑定；切换 GitHub 时需重新 CheckForUpdatesAsync。
            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
            var updateInfo = source is UpdateDownloadSource.GitHub
                ? await manager.CheckForUpdatesAsync()
                : release;
            if (updateInfo is null)
            {
                ErrorMessage = Lang.UpdateService_CannotUpdateAutomatically;
                State = UpdateState.Error;
                return;
            }

            Progress_TotalBytes = updateInfo.TargetFullRelease?.Size ?? 0;
            _progress_DownloadBytes = 0;
            Progress_Percent = 0;
            State = UpdateState.Downloading;
            await manager.DownloadUpdatesAsync(updateInfo, OnDownloadProgress, _cancellationTokenSource.Token);
            _downloadedUpdate = updateInfo;
            _lastDownloadSource = source;

            await Task.Delay(500, _cancellationTokenSource.Token);
            State = UpdateState.Finish;
            UpdateFinished = true;
            // 已下载待安装的版本不应再被「忽略此版本」挡住后续检查
            AppConfig.IgnoreVersion = null;
            // 退出时需要替换 current\，RPC 不能在主进程退出后继续占用文件
            AppConfig.GetService<RpcService>().KeepRunningOnExited(false, noLongerChange: true);
            _logger.LogInformation("Update downloaded from {source}: {version}", source, updateInfo.TargetFullRelease?.Version);
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
    /// 后台下载已检查到的更新（不弹窗）。下载完成后由 <see cref="ApplySilentlyOnExit"/> 在退出时静默安装，
    /// 并置位 <see cref="AppConfig.PendingSilentUpdateContent"/>，下次启动弹出更新内容。
    /// 若进程未走到退出钩子，下次启动时 Velopack 默认也会自动应用已下载的包。
    /// </summary>
    /// <param name="release">检查更新阶段得到的版本信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task TryStartSilentUpdateAsync(UpdateInfo release, CancellationToken cancellationToken = default)
    {
        if (!AppConfig.EnableUpdateNotification || !AppConfig.EnableSilentUpdate || UpdateFinished || _isUpdating)
        {
            return;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (!IsUpdaterAvailable)
        {
            return;
        }
        try
        {
            _logger.LogInformation("Start silent update: {version}", release.TargetFullRelease?.Version);
            await StartUpdateAsync(release);
            if (UpdateFinished)
            {
                AppConfig.PendingSilentUpdateContent = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Silent update");
        }
    }


    /// <summary>
    /// 应用已下载的更新并重启应用（由 Velopack 的 Update.exe 完成文件替换后重启）。
    /// 调用前应确保后台子进程（如 RPC）会随主进程退出。
    /// </summary>
    public void ApplyAndRestart()
    {
        var manager = GetManager(_lastDownloadSource);
        if (!manager.IsInstalled)
        {
            return;
        }
        // 从更新窗口重启视为手动更新，不在下次启动弹出更新内容
        AppConfig.PendingSilentUpdateContent = false;
        // _downloadedUpdate 为 null 时（如重开更新窗口）传 null，Velopack 会应用已下载/暂存的最新包。
        _logger.LogInformation("Apply update and restart: {version}", _downloadedUpdate?.TargetFullRelease?.Version);
        manager.ApplyUpdatesAndRestart(_downloadedUpdate?.TargetFullRelease);
    }


    /// <summary>
    /// 若已下载更新，通知 Update.exe 在本进程退出后静默安装（不重启、不显示进度窗口）。
    /// Update.exe 最多等待 60 秒；应在真正退出前调用。
    /// </summary>
    public void ApplySilentlyOnExit()
    {
        if (_applyOnExitScheduled || !UpdateFinished || !AppConfig.EnableSilentUpdate)
        {
            return;
        }
        try
        {
            var manager = GetManager(_lastDownloadSource);
            if (!manager.IsInstalled)
            {
                return;
            }
            AppConfig.GetService<RpcService>().KeepRunningOnExited(false, noLongerChange: true);
            _logger.LogInformation("Apply silent update on exit: {version}", _downloadedUpdate?.TargetFullRelease?.Version);
            _applyOnExitScheduled = true;
            manager.WaitExitThenApplyUpdates(_downloadedUpdate?.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            _applyOnExitScheduled = false;
            _logger.LogWarning(ex, "Apply silent update on exit");
        }
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
