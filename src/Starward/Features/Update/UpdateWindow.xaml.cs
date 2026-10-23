using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NuGet.Versioning;
using Starward.Features.RPC;
using Starward.Features.Setting;
using Starward.Frameworks;
using Starward.Helpers;
using Starward.Setup.Core;
using Starward.Setup.Core.Github;
using Velopack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.System;


namespace Starward.Features.Update;

[INotifyPropertyChanged]
public sealed partial class UpdateWindow : WindowEx
{


    private readonly ILogger<UpdateWindow> _logger = AppConfig.GetLogger<UpdateWindow>();

    private readonly ReleaseNotesService _releaseNotesService = AppConfig.GetService<ReleaseNotesService>();

    private readonly UpdateService _updateService = AppConfig.GetService<UpdateService>();


    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

    /// <summary>
    /// 本窗口是否发起了下载；关闭时仅在此情况下取消，避免打断后台静默更新。
    /// </summary>
    private bool _startedDownloadInThisWindow;

    private bool _closed;

    /// <summary>
    /// 静默更新内容窗口的起始版本（构造时快照）。避免启动检查随后把 <see cref="AppConfig.LastAppVersion"/> 写成当前版本后，发行说明区间为空。
    /// </summary>
    private readonly string? _recentContentFromVersion;



    /// <summary>
    /// 当前已打开的更新窗口，同一时刻只应有一个；关闭时置空。只在 UI 线程访问。
    /// </summary>
    public static UpdateWindow? Current { get; private set; }


    /// <summary>
    /// 已有更新窗口开着时把它带到前台并返回 <see langword="true"/>，调用方据此跳过再开一个。
    /// <para>
    /// 前台自动检查与关于页手动检查会复用同一次网络请求（<see cref="UpdateService.GetLatestVersionAsync"/> 合并并发调用），
    /// 两边各自拿到结果后都会弹窗，不去重就会同时冒出两个更新窗口。
    /// </para>
    /// </summary>
    /// <returns>已有窗口并已激活为 <see langword="true"/>；没有窗口（或它正在关闭）为 <see langword="false"/>。</returns>
    public static bool TryActivateExisting()
    {
        if (Current is null)
        {
            return false;
        }
        try
        {
            Current.Activate();
            return true;
        }
        catch (Exception ex)
        {
            // 窗口可能正处在关闭途中，此时 Activate 会抛；当作没有窗口，让调用方新开一个。
            AppConfig.GetLogger<UpdateWindow>().LogWarning(ex, "Activate existing update window");
            Current = null;
            return false;
        }
    }


    public UpdateWindow()
    {
        this.InitializeComponent();
        _recentContentFromVersion = AppConfig.LastAppVersion;
        InitializeWindow();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += _timer_Tick;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => this.Bindings.Update());
        this.Closed += UpdateWindow_Closed;
        Current = this;
    }



    private void InitializeWindow()
    {
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        Title = "Moonward · Update";
        RootGrid.RequestedTheme = ShouldAppsUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AdaptTitleBarButtonColorToActuallTheme();
        SetIcon();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
        }
    }



    /// <summary>
    /// 把更新窗口放到当前工作区居中，并限制为明显小于主窗口的默认尺寸。
    /// 小屏 / 高 DPI 时按原比例缩小到工作区宽 80% / 高 90% 以内，避免铺满屏幕或把横窗翻成竖窗。
    /// </summary>
    private void CenterInScreen()
    {
        RectInt32 workArea = GetDisplayAreaForCentering().WorkArea;
        int widthDip;
        int heightDip;
        if (NewVersion is null)
        {
            Grid_Update.Visibility = Visibility.Collapsed;
            Title = $"Moonward · {Lang.UpdateContentWindow_RecentlyUpdatedContent}";
            widthDip = 800;
            heightDip = 700;
        }
        else
        {
            Button_RemindLatter.Visibility = Visibility.Collapsed;
            widthDip = 880;
            heightDip = 600;
        }

        double scale = UIScale;
        int w = (int)(widthDip * scale);
        int h = (int)(heightDip * scale);
        int maxW = (int)(workArea.Width * 0.8);
        int maxH = (int)(workArea.Height * 0.9);
        if (w > maxW || h > maxH)
        {
            double fit = Math.Min((double)maxW / w, (double)maxH / h);
            w = Math.Max(1, (int)(w * fit));
            h = Math.Max(1, (int)(h * fit));
        }

        int x = workArea.X + (workArea.Width - w) / 2;
        int y = workArea.Y + (workArea.Height - h) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }



    public new void Activate()
    {
        CenterInScreen();
        base.Activate();
    }



    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (NewVersion is not null && !_updateService.IsUpdaterAvailable)
        {
            // 非 Velopack 部署（开发态/裸发布目录），无法自动更新，需手动下载安装包。
            IsUpdateNowEnabled = false;
            ErrorMessage = Lang.UpdatePage_YouNeedToManuallyDownloadTheNewVersionPackage;
        }
        if (UpdateService.UpdateFinished)
        {
            Finish(skipRestart: true);
        }
        else if (_updateService.IsUpdating)
        {
            // 静默下载已在进行：同步显示进度，不另起一份下载
            IsUpdateNowEnabled = false;
            _timer.Start();
        }
        _ = LoadUpdateContentAsync();
    }



    private void UpdateWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
        _timer.Stop();
        _timer.Tick -= _timer_Tick;
        if (_startedDownloadInThisWindow)
        {
            _updateService.StopUpdate();
        }
        if (NewVersion is null)
        {
            MarkRecentUpdateContentShown();
        }
        WeakReferenceMessenger.Default.UnregisterAll(this);
        this.Closed -= UpdateWindow_Closed;
        _closed = true;
    }




    public UpdateInfo? NewVersion
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(NewVersionText));
                OnPropertyChanged(nameof(ChannelText));
            }
        }
    }


    /// <summary>
    /// 新版本号文本。
    /// </summary>
    public string? NewVersionText => NewVersion?.TargetFullRelease?.Version?.ToString();


    /// <summary>
    /// 当前进程架构（Velopack 更新包按架构区分渠道）。
    /// </summary>
    public string ArchitectureText => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();


    /// <summary>
    /// 更新渠道/类型文本（稳定版 / 预览版）。优先根据 NewVersion 的实际版本号判断（NuGet 语义化版本的 IsPrerelease），
    /// 这样当「加入预览更新」开启但最新可用仍是正式版时，弹窗不会把正式版误判为预览版。
    /// 仅在无 NewVersion 时回退到 AppConfig 设置。
    /// </summary>
#if DEBUG
    public string ChannelText => Lang.UpdatePage_DevChannel;
#else
    public string ChannelText
    {
        get
        {
            if (NuGetVersion.TryParse(NewVersionText, out var v))
            {
                return v.IsPrerelease ? Lang.UpdatePage_PreviewChannel : Lang.UpdatePage_StableChannel;
            }
            return AppConfig.EnablePreviewRelease ? Lang.UpdatePage_PreviewChannel : Lang.UpdatePage_StableChannel;
        }
    }
#endif



    private async void HyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && NewVersion != null)
            {
                var url = fe.Tag switch
                {
                    "release" => $"https://github.com/{ReleaseClient.Repository}/releases/tag/{NewVersionText}",
                    _ => null,
                };
                _logger.LogInformation("Open url: {url}", url);
                if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
                {
                    await Launcher.LaunchUriAsync(uri);
                }
            }
        }
        catch { }
    }





    #region Update



    public bool IsUpdateNowEnabled { get; set => SetProperty(ref field, value); } = true;

    public bool IsUpdateRemindLatterEnabled { get; set => SetProperty(ref field, value); } = true;

    public bool IsProgressTextVisible { get; set => SetProperty(ref field, value); }

    public bool IsProgressBarVisible { get; set => SetProperty(ref field, value); }

    public string ProgressBytesText { get; set => SetProperty(ref field, value); }

    public string ProgressPercentText { get; set => SetProperty(ref field, value); }

    public string? ErrorMessage { get; set => SetProperty(ref field, value); }



    public bool AutoRestartWhenUpdateFinished
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.AutoRestartWhenUpdateFinished = value;
            }
        }
    } = AppConfig.AutoRestartWhenUpdateFinished;


    /// <summary>
    /// 更新渠道下拉框选中索引（0=GitHub，1=CNB）；仅本会话有效，默认 CNB。
    /// 检查更新因 CNB 限流回落到 GitHub 时默认选中 GitHub —— 此时的 <see cref="NewVersion"/> 本就绑定在 GitHub 源上，
    /// 再选 CNB 会立刻重新向被限流的 CNB 发一次检查。
    /// </summary>
    public int SelectedUpdateSourceIndex { get; set => SetProperty(ref field, value); }
        = AppConfig.GetService<UpdateService>().LastCheckSource is UpdateDownloadSource.GitHub ? 0 : 1;


    /// <summary>
    /// 当前选中的更新下载源。
    /// </summary>
    private UpdateDownloadSource SelectedUpdateSource =>
        SelectedUpdateSourceIndex == 0 ? UpdateDownloadSource.GitHub : UpdateDownloadSource.Cnb;



    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        try
        {
            ErrorMessage = null;
            IsUpdateNowEnabled = false;
            IsUpdateRemindLatterEnabled = false;


            if (NewVersion != null)
            {
                // 统一走 Velopack：下载更新包（含增量），由 _timer 轮询进度/状态。
                _startedDownloadInThisWindow = !_updateService.IsUpdating;
                _timer.Start();
                await _updateService.StartUpdateAsync(NewVersion, SelectedUpdateSource);
            }
        }
        catch (OperationCanceledException)
        {
            Button_UpdateNow.IsEnabled = true;
            Button_RemindLatter.IsEnabled = true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ErrorMessage = Lang.GachaLogPage_OperationCanceled;
            Button_UpdateNow.IsEnabled = true;
            Button_RemindLatter.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update now");
            Button_UpdateNow.IsEnabled = true;
            Button_RemindLatter.IsEnabled = true;
        }
    }




    private void UpdateProgressState()
    {
        if (_updateService.State is UpdateState.Pending)
        {
            IsProgressTextVisible = true;
            IsProgressBarVisible = true;
            ProgressBar_Update.IsIndeterminate = true;
            UpdateProgressValue();
        }
        else if (_updateService.State is UpdateState.Downloading)
        {
            IsUpdateNowEnabled = false;
            IsUpdateRemindLatterEnabled = false;
            IsProgressBarVisible = true;
            IsProgressTextVisible = true;
            ProgressBar_Update.IsIndeterminate = false;
            UpdateProgressValue();
        }
        else if (_updateService.State is UpdateState.Finish)
        {
            IsProgressTextVisible = false;
            ProgressBar_Update.IsIndeterminate = false;
            ProgressBar_Update.Value = 100;
        }
        else if (_updateService.State is UpdateState.Stop)
        {
            IsUpdateNowEnabled = true;
            IsUpdateRemindLatterEnabled = true;
            IsProgressTextVisible = false;
            IsProgressBarVisible = false;
            ErrorMessage = null;
        }
        else if (_updateService.State is UpdateState.Error)
        {
            IsUpdateNowEnabled = true;
            IsUpdateRemindLatterEnabled = true;
            IsProgressTextVisible = false;
            IsProgressBarVisible = false;
            ErrorMessage = _updateService.ErrorMessage;
        }
        else if (_updateService.State is UpdateState.NotSupport)
        {
            IsUpdateNowEnabled = false;
            IsUpdateRemindLatterEnabled = true;
            IsProgressTextVisible = false;
            IsProgressBarVisible = false;
            ErrorMessage = _updateService.ErrorMessage;
        }
    }



    private void UpdateProgressValue()
    {
        int percent = _updateService.Progress_Percent;
        ProgressBar_Update.Value = percent;
        ProgressPercentText = $"{percent / 100.0:P0}";
        if (_updateService.Progress_TotalBytes > 0)
        {
            const double mb = 1 << 20;
            ProgressBytesText = $"{_updateService.Progress_DownloadBytes / mb:F2}/{_updateService.Progress_TotalBytes / mb:F2} MB";
        }
        else
        {
            ProgressBytesText = "";
        }
    }




    private void _timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {

        try
        {
            UpdateProgressState();
            if (_updateService.State is UpdateState.Finish)
            {
                _timer.Stop();
                Finish();
            }
            else if (_updateService.State is UpdateState.Stop or UpdateState.Error or UpdateState.NotSupport)
            {
                _timer.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update progress");
        }
    }



    private void Finish(bool skipRestart = false)
    {
        AppConfig.IgnoreVersion = null;
        Button_UpdateNow.Visibility = Visibility.Collapsed;
        Button_Restart.Visibility = Visibility.Visible;
        AppConfig.GetService<RpcService>().KeepRunningOnExited(false, noLongerChange: true);
        if (AutoRestartWhenUpdateFinished && !skipRestart)
        {
            Restart();
        }
    }



    [RelayCommand]
    private void Restart()
    {
        try
        {
            // 确保 RPC 子进程随主进程退出，避免占用 current 目录导致 Velopack 替换文件失败。
            AppConfig.GetService<RpcService>().KeepRunningOnExited(false, noLongerChange: true);
            // 由 Velopack 的 Update.exe 在主进程退出后替换文件并重启。
            _updateService.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Apply update and restart");
            ErrorMessage = ex.Message;
        }
    }



    [RelayCommand]
    private void RemindMeLatter()
    {
        this.Close();
    }



    [RelayCommand]
    private void IgnoreThisVersion()
    {
        if (NewVersion is null)
        {
            MarkRecentUpdateContentShown();
        }
        else
        {
            AppConfig.IgnoreVersion = NewVersionText;
        }
        this.Close();
    }


    /// <summary>
    /// 将当前版本记为已读，避免静默更新内容窗口重复弹出。
    /// </summary>
    private static void MarkRecentUpdateContentShown()
    {
        AppConfig.LastAppVersion = AppConfig.AppVersion;
        AppConfig.PendingSilentUpdateContent = false;
    }




    private void Button_UpdateRemindLatter_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Button_UpdateRemindLatter.Opacity = 1;
    }


    private void Button_UpdateRemindLatter_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Button_UpdateRemindLatter.Opacity = 0;
    }



    #endregion





    #region Update Content





    private async Task LoadUpdateContentAsync()
    {
        try
        {
            StackPanel_Loading.Visibility = Visibility.Visible;
            StackPanel_Error.Visibility = Visibility.Collapsed;

            List<GithubRelease> releases = await GetReleaseContentAsync();
            if (_closed) return;
            View_ReleaseNotes.Releases = releases;
            View_ReleaseNotes.Visibility = Visibility.Visible;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            if (NewVersion is null)
            {
                MarkRecentUpdateContentShown();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or TaskCanceledException)
        {
            _logger.LogError(ex, "Load recent update content");
            if (_closed) return;
            // 取不到说明（含 GitHub 限流）时直接打开发布页：网页不占 API 配额。不内嵌，GitHub 发行页带搜索/登录框，会激活 Chromium TSF（issue #1）
            string tag = NewVersionText ?? AppConfig.AppVersion;
            try
            {
                _ = Launcher.LaunchUriAsync(new Uri($"https://github.com/{ReleaseClient.Repository}/releases/tag/{tag}"));
            }
            catch { }
            TextBlock_Error.Text = ReleaseNotesService.GetDisplayErrorMessage(ex);
            View_ReleaseNotes.Visibility = Visibility.Collapsed;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            StackPanel_Error.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load recent update content");
            if (_closed) return;
            TextBlock_Error.Text = Lang.DownloadGamePage_UnknownError;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            StackPanel_Error.Visibility = Visibility.Visible;
        }
    }



    /// <summary>
    /// 取要显示的发行版本：有新版本时为当前版本之后到新版本（含），静默更新后为上次版本之后到当前版本；最多 10 个。
    /// </summary>
    /// <returns>发行版本，新到旧。</returns>
    private async Task<List<GithubRelease>> GetReleaseContentAsync()
    {
        bool showPrerelease = false;
        NuGetVersion? startVersion, endVersion;
        if (NewVersion is null)
        {
            _ = NuGetVersion.TryParse(_recentContentFromVersion, out startVersion);
            _ = NuGetVersion.TryParse(AppConfig.AppVersion, out endVersion);
        }
        else
        {
            _ = NuGetVersion.TryParse(AppConfig.AppVersion, out startVersion);
            _ = NuGetVersion.TryParse(NewVersionText, out endVersion);
        }
        startVersion ??= new NuGetVersion(0, 0, 0);
        endVersion ??= new NuGetVersion(int.MaxValue, int.MaxValue, int.MaxValue);
        if (endVersion.IsPrerelease)
        {
            showPrerelease = true;
            if (startVersion.IsPrerelease)
            {
                if (startVersion.Patch - 1 >= 0)
                {
                    startVersion = new NuGetVersion(startVersion.Major, startVersion.Minor, startVersion.Patch - 1);
                }
                else if (startVersion.Minor - 1 >= 0)
                {
                    startVersion = new NuGetVersion(startVersion.Major, startVersion.Minor - 1, int.MaxValue);
                }
                else if (startVersion.Major - 1 >= 0)
                {
                    startVersion = new NuGetVersion(startVersion.Major - 1, int.MaxValue, int.MaxValue);
                }
            }
        }

        string targetTag = NewVersionText ?? AppConfig.AppVersion;
        // 与发行说明窗口共用落盘缓存；缓存里没有目标版本（如刚发布的新版本）时才重新请求
        List<GithubRelease> releases = await _releaseNotesService.GetReleasesAsync(targetTag);
        var selected = new List<GithubRelease>();
        foreach (var release in releases)
        {
            if (NuGetVersion.TryParse(release.TagName, out var version))
            {
                if (version > startVersion && version <= endVersion)
                {
                    // 只显示最新的几个连续的预览版，最新稳定版之前的预览版不显示
                    if (!version.IsPrerelease && !release.Prerelease)
                    {
                        showPrerelease = false;
                    }
                    if (!(showPrerelease ^ version.IsPrerelease))
                    {
                        selected.Add(release);
                    }
                }
            }
            else
            {
                selected.Add(release);
            }
            if (selected.Count >= 10)
            {
                break;
            }
        }
        if (selected.Count == 0)
        {
            // 版本区间里一个都没有（如版本号解析不了）：显示目标版本，没有就显示最新的
            GithubRelease? fallback = releases.FirstOrDefault(x => ReleaseNotesFormatter.ContainsTag([x], targetTag)) ?? releases.FirstOrDefault();
            if (fallback is not null)
            {
                selected.Add(fallback);
            }
        }
        return selected;
    }



    [RelayCommand]
    private async Task RetryAsync()
    {
        await LoadUpdateContentAsync();
    }




    #endregion





    #region Converter



    public static string ByteLengthToString(long byteLength)
    {
        double length = byteLength;
        return length switch
        {
            >= (1 << 30) => $"{length / (1 << 30):F2} GB",
            >= (1 << 20) => $"{length / (1 << 20):F2} MB",
            _ => $"{length / (1 << 10):F2} KB",
        };
    }



    public static Visibility StringToVisibility(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }


    #endregion



}
