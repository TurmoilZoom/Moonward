using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Starward.Features.RPC;
using System;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
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
    public const string RepoUrl = "https://cnb.cool/TurmoilZoom/Moonward";

    /// <summary>
    /// GitHub 更新源仓库地址（Velopack <see cref="GithubSource"/>）。
    /// </summary>
    public const string GitHubRepoUrl = "https://github.com/TurmoilZoom/Moonward";


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

    /// <summary>正在进行的检查更新任务，用于合并并发调用（首页启动检查与关于页手动检查可能同时发生）。</summary>
    private Task<UpdateInfo?>? _checkingTask;

    private readonly Lock _checkingLock = new();

    /// <summary>
    /// GitHub 回落检查的等待上限。Velopack 下载器的默认超时长达 30 分钟，
    /// 而 GitHub 在部分网络环境下会长时间无响应，不设上限会把「检查更新」一直挂住。
    /// </summary>
    private static readonly TimeSpan GitHubFallbackTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 两次检查更新之间的最小间隔。主窗口与后台常驻循环共用同一个时钟（<see cref="IsCheckDue"/>），
    /// 避免两处入口各按各的节流、在同一小时里各查一遍。
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>常驻循环启动后的缓冲，避开启动阶段的网络与磁盘高峰。</summary>
    private static readonly TimeSpan ResidentStartupDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 上次向更新源发起检查的时刻（UTC ticks），0 表示尚未检查过。
    /// 主窗口（UI 线程）与常驻循环（线程池）都会读写它，故用 <see cref="Interlocked"/> 存取——
    /// <see cref="DateTimeOffset"/> 是多字段结构体，直接赋值可能被撕裂读成远未来的时刻，
    /// 那样 <see cref="IsCheckDue"/> 会永远为 false，检查更新就此停摆。
    /// </summary>
    private long _lastCheckTicks;

    /// <summary>常驻静默更新循环的启动标记，保证每个进程只启动一次。</summary>
    private int _residentStarted;



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
    /// 最近一次检查更新实际使用的源。CNB 被限流时会自动回落到 GitHub，
    /// 此时得到的 <see cref="UpdateInfo"/> 只能由 GitHub 源下载（Velopack 的资产与源强绑定）。
    /// </summary>
    public UpdateDownloadSource LastCheckSource { get; private set; } = UpdateDownloadSource.Cnb;

    /// <summary>
    /// 距上次检查是否已超过 <see cref="CheckInterval"/>。
    /// </summary>
    public bool IsCheckDue => DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref _lastCheckTicks) > CheckInterval.Ticks;



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
        // 进入即记时：非 Velopack 部署、断网或被限流同样要计入节流，
        // 否则常驻循环会立刻重试、窗口每次激活也会重跑一遍。
        Interlocked.Exchange(ref _lastCheckTicks, DateTimeOffset.UtcNow.UtcTicks);
        var manager = GetManager();
        if (!manager.IsInstalled)
        {
            _logger.LogInformation("Not a Velopack install, skip update check.");
            return null;
        }
        // 一次检查会向 CNB 打出多个请求，而其匿名接口限流为 20 次/分钟；
        // 并发调用共用同一个任务，避免两处入口各查一遍直接把配额打满（见 CnbSource.TrimToRequiredReleases）。
        Task<UpdateInfo?> task;
        lock (_checkingLock)
        {
            task = _checkingTask ??= CheckForUpdatesCoreAsync(manager);
        }
        try
        {
            return await task;
        }
        finally
        {
            lock (_checkingLock)
            {
                if (_checkingTask == task)
                {
                    _checkingTask = null;
                }
            }
        }
    }


    /// <summary>
    /// 真正向更新源发起一次检查，并记录当前/最新版本。CNB 被限流(429)时自动回落到 GitHub 源。
    /// </summary>
    private async Task<UpdateInfo?> CheckForUpdatesCoreAsync(UpdateManager manager)
    {
        UpdateInfo? info;
        try
        {
            info = await manager.CheckForUpdatesAsync();
            LastCheckSource = UpdateDownloadSource.Cnb;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests)
        {
            // CNB 匿名接口限流 20 次/分钟，被限流时整个检查会失败；GitHub 源的资产托管在 CDN 上，可作为备用。
            _logger.LogWarning(ex, "CNB update source is rate limited, fall back to GitHub.");
            info = await CheckFromGitHubOrRethrowAsync(ex);
        }
        _logger.LogInformation("Current version: {currentVersion}, latest version: {latestVersion}, source: {source}.",
                               manager.CurrentVersion, info?.TargetFullRelease?.Version, LastCheckSource);
        return info;
    }


    /// <summary>
    /// 用 GitHub 源重新检查一次更新；若 GitHub 也失败，则抛回 CNB 的原始异常。
    /// </summary>
    /// <param name="cnbException">CNB 源抛出的限流异常。</param>
    /// <returns>GitHub 源的检查结果。</returns>
    private async Task<UpdateInfo?> CheckFromGitHubOrRethrowAsync(Exception cnbException)
    {
        try
        {
            Task<UpdateInfo?> checkTask = GetManager(UpdateDownloadSource.GitHub).CheckForUpdatesAsync();
            if (await Task.WhenAny(checkTask, Task.Delay(GitHubFallbackTimeout)) != checkTask)
            {
                // CheckForUpdatesAsync 不接受取消令牌，超时后只能弃用该任务，这里顺手观察它的异常。
                _ = checkTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("Checking updates from GitHub timed out.");
            }
            UpdateInfo? info = await checkTask;
            LastCheckSource = UpdateDownloadSource.GitHub;
            return info;
        }
        catch (Exception ex)
        {
            // 回落也失败（多为断网）时，展示 CNB 的限流提示比展示 GitHub 的网络错误更贴近真实原因。
            _logger.LogWarning(ex, "Fall back to GitHub update source");
            ExceptionDispatchInfo.Capture(cnbException).Throw();
            throw;
        }
    }


    /// <summary>
    /// 把更新过程中的异常转换为可展示的文案：被限流(429)时给出友好提示，其余保留原始信息。
    /// </summary>
    /// <param name="ex">检查或下载更新时捕获的异常。</param>
    /// <returns>可直接显示给用户的错误文本。</returns>
    public static string GetDisplayErrorMessage(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
        {
            return Lang.UpdateService_TooManyRequests;
        }
        return ex.Message;
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

            // UpdateInfo 与 IUpdateSource 绑定；下载源与检查时用的源不一致（手动切换，或检查被限流回落到 GitHub）时需重新 CheckForUpdatesAsync。
            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
            var updateInfo = source == LastCheckSource
                ? release
                : await manager.CheckForUpdatesAsync();
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
            ErrorMessage = GetDisplayErrorMessage(ex);
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
            _logger.LogInformation("Start silent update: {version} from {source}", release.TargetFullRelease?.Version, LastCheckSource);
            // 跟随检查时实际使用的源：检查因限流回落到 GitHub 时，这里再走 CNB 只会重新触发一次限流。
            await StartUpdateAsync(release, LastCheckSource);
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
    /// 把下一次检查推迟 <paramref name="delay"/>，用于刚展示过更新说明、不必立即再查的情形。
    /// </summary>
    /// <param name="delay">距下一次可检查的时长。</param>
    public void PostponeCheck(TimeSpan delay)
    {
        Interlocked.Exchange(ref _lastCheckTicks, (DateTimeOffset.UtcNow - CheckInterval + delay).UtcTicks);
    }


    /// <summary>
    /// 启动后台常驻的静默更新循环：仅托盘驻留（<c>--hide</c>）或主窗口长期最小化时，
    /// 也按 <see cref="CheckInterval"/> 检查并后台下载更新。
    /// <para>
    /// 只下载、不弹窗——「展示更新说明」与「提示有新版本」仍归主窗口，窗口激活时才触发。
    /// 幂等：多次调用只生效一次。
    /// </para>
    /// </summary>
    public void StartResidentSilentUpdate()
    {
#if DEBUG || DONOT_CHECK_UPDATE
        return;
#endif
#pragma warning disable CS0162 // 检测到无法访问的代码
        if (Interlocked.Exchange(ref _residentStarted, 1) == 1)
        {
            return;
        }
        _ = Task.Run(RunResidentSilentUpdateLoopAsync);
#pragma warning restore CS0162 // 检测到无法访问的代码
    }


    /// <summary>
    /// 常驻静默更新循环：缓冲后每 <see cref="CheckInterval"/> 醒一次，到点且开启静默更新时检查并下载。
    /// 单轮异常只记日志并排下一轮，不能让循环在本进程内静默死掉。
    /// </summary>
    private async Task RunResidentSilentUpdateLoopAsync()
    {
        await Task.Delay(ResidentStartupDelay).ConfigureAwait(false);
        while (true)
        {
            try
            {
                // 已下载待安装，或不是 Velopack 部署（开发态 / 裸发布目录），再轮询也不会有结果。
                if (UpdateFinished || !IsUpdaterAvailable)
                {
                    return;
                }
                // 两个开关都能在设置页随时改，故每轮重新判断。未开静默更新时后台查没有意义：
                // 弹窗要等主窗口激活，那时 MainView 自己会查。
                if (AppConfig.EnableUpdateNotification && AppConfig.EnableSilentUpdate && IsCheckDue)
                {
                    UpdateInfo? release = await CheckUpdateAsync().ConfigureAwait(false);
                    if (release is not null)
                    {
                        await TryStartSilentUpdateAsync(release).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resident silent update");
            }
            await Task.Delay(CheckInterval).ConfigureAwait(false);
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
