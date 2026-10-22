using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Starward.Controls;
using Starward.Features.Feedback;
using Starward.Features.RPC;
using Starward.Features.Update;
using Starward.Frameworks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;


namespace Starward.Features.Setting;

public sealed partial class AboutSetting : PageBase, IEntranceAnimationElements
{


    private readonly ILogger<AboutSetting> _logger = AppConfig.GetLogger<AboutSetting>();


    public AboutSetting()
    {
        this.InitializeComponent();
        // 静默更新是推送更新的子选项；推送关闭时不应残留开启状态
        if (!EnableUpdateNotification && EnableSilentUpdate)
        {
            EnableSilentUpdate = false;
        }
    }


    /// <summary>静止显示首帧，避免 AutoPlay 常驻循环。</summary>
    protected override void OnLoaded()
    {
        Lottie_AboutLogo.SetProgress(0);
        // 主窗口激活、托盘常驻循环的后台检查与静默下载也会改变状态，页面开着时跟着刷新
        if (!WeakReferenceMessenger.Default.IsRegistered<UpdateStatusChangedMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<UpdateStatusChangedMessage>(this, (_, _) => DispatcherQueue.TryEnqueue(() => RefreshUpdateStatus()));
        }
        RefreshUpdateStatus();
    }


    /// <summary>
    /// 入场级联：左侧逐项上滑，致谢卡片紧随标题出场。
    /// 根 Grid 上并列可交互的致谢卡片与小猫命中层，通用推断会把整块滚动区当成一项，故在此显式列出；小猫为装饰，保持静止。
    /// </summary>
    /// <returns>按出场顺序排列的元素。</returns>
    public IEnumerable<UIElement?> GetEntranceAnimationElements()
    {
        var children = StackPanel_About.Children;
        for (int i = 0; i < children.Count; i++)
        {
            yield return children[i];
            if (i == 0)
            {
                yield return Border_Acknowledgements;
            }
        }
    }


    /// <summary>离开页面时停止动画，避免卸载后仍占用合成资源。</summary>
    protected override void OnUnloaded()
    {
        Lottie_AboutLogo.Stop();
        WeakReferenceMessenger.Default.Unregister<UpdateStatusChangedMessage>(this);
    }


    /// <summary>
    /// 悬浮时播放一次 Lottie 后停在末帧；离开后回到首帧。
    /// </summary>
    private void Lottie_AboutLogo_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = Lottie_AboutLogo.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 指针离开后停止动画并回到首帧。
    /// </summary>
    private void Lottie_AboutLogo_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Lottie_AboutLogo.Stop();
        Lottie_AboutLogo.SetProgress(0);
    }




    /// <summary>
    /// 预览版
    /// </summary>
    public bool EnablePreviewRelease
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnablePreviewRelease = value;
            }
        }
    } = AppConfig.EnablePreviewRelease;


    /// <summary>
    /// 是否在启动时自动推送新版本可用弹窗。
    /// </summary>
    public bool EnableUpdateNotification
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableUpdateNotification = value;
                if (!value && EnableSilentUpdate)
                {
                    EnableSilentUpdate = false;
                }
            }
        }
    } = AppConfig.EnableUpdateNotification;


    /// <summary>
    /// 是否后台下载更新并在退出后静默安装。
    /// </summary>
    public bool EnableSilentUpdate
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableSilentUpdate = value;
            }
        }
    } = AppConfig.EnableSilentUpdate;


    /// <summary>
    /// 更新状态文本：已是最新 / 有新版本 / 已下载待安装 / 检查失败；没有可显示的结果时为 null。
    /// </summary>
    public string? UpdateStatusText { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 已是最新时跟在状态文本后、以成功色显示的当前版本号；其他状态为 null。
    /// </summary>
    public string? LatestVersion { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 「上次检查：…」，取上次成功检查的时间（后台检查也算）；从未成功检查过为 null。
    /// </summary>
    public string? LastCheckedText { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 已下载、等待安装的新版本号；不为 null 时显示「立即重启安装」。
    /// </summary>
    public string? PendingInstallVersion { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 手动检查得到、但 <see cref="UpdateService"/> 不记录的提示（如非 Velopack 部署无法检查）。
    /// </summary>
    private string? _manualCheckMessage;


    /// <summary>
    /// 检查更新
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        var service = AppConfig.GetService<UpdateService>();
        _manualCheckMessage = null;
        UpdateStatusText = null;
        LatestVersion = null;
        LastCheckedText = null;
        PendingInstallVersion = null;
        try
        {
            var release = await service.GetLatestVersionAsync();
            if (release is not null)
            {
                // 与前台自动检查可能复用同一次网络请求并同时走到这里，已有窗口就不再开第二个。
                if (!UpdateWindow.TryActivateExisting())
                {
                    new UpdateWindow { NewVersion = release }.Activate();
                }
            }
            else if (!service.IsUpdaterAvailable)
            {
                // 非 Velopack 部署（开发态/裸发布目录），无法检查更新
                _manualCheckMessage = Lang.UpdateService_CannotUpdateAutomatically;
            }
        }
        catch (Exception ex)
        {
            // 向更新源检查失败时 UpdateService 已记下提示，其余异常（如创建更新管理器失败）在这里补上
            if (service.LastCheckErrorMessage is null)
            {
                _manualCheckMessage = UpdateService.GetDisplayErrorMessage(ex);
            }
            _logger.LogError(ex, "Check update");
        }
        RefreshUpdateStatus(ignoreRunning: true);
    }


    /// <summary>
    /// 按 <see cref="UpdateService"/> 记下的结果刷新状态区。优先级：已下载待安装 &gt; 手动检查的提示 &gt; 检查失败 &gt; 有新版本 &gt; 已是最新。
    /// </summary>
    /// <param name="ignoreRunning">手动检查结束时传 true；其余时候手动检查还在跑就不动，等它结束再刷新，免得状态与转圈同时出现。</param>
    private void RefreshUpdateStatus(bool ignoreRunning = false)
    {
        if (!ignoreRunning && CheckUpdateCommand.IsRunning)
        {
            return;
        }
        var service = AppConfig.GetService<UpdateService>();
        string? pendingVersion = service.GetPendingInstallVersion();
        string? statusText = null;
        string? latestVersion = null;
        if (pendingVersion is not null)
        {
            statusText = string.Format(Lang.AboutSetting_UpdateDownloaded, pendingVersion);
        }
        else if (_manualCheckMessage is not null)
        {
            statusText = _manualCheckMessage;
        }
        else if (service.LastCheckErrorMessage is not null)
        {
            statusText = string.Format(Lang.AboutSetting_CheckUpdateFailed, service.LastCheckErrorMessage);
        }
        else if (service.LastFoundVersion is not null)
        {
            statusText = string.Format(Lang.AboutSettingPage_CurrentVersionIsNotTheLatestVersion, service.LastFoundVersion);
        }
        else if (service.HasCheckedSuccessfully)
        {
            statusText = Lang.AboutSetting_UpdatedToTheLatestVersion;
            latestVersion = AppConfig.AppVersion;
        }
        UpdateStatusText = statusText;
        LatestVersion = latestVersion;
        PendingInstallVersion = pendingVersion;
        LastCheckedText = AppConfig.LastUpdateCheckTime is DateTimeOffset time ? string.Format(Lang.AboutSetting_LastChecked, FormatCheckTime(time)) : null;
    }


    /// <summary>
    /// 把检查时间格式化为「今天 14:32」「昨天 09:10」，更早的显示完整日期。
    /// </summary>
    /// <param name="time">检查时间。</param>
    /// <returns>本地时间文本。</returns>
    private static string FormatCheckTime(DateTimeOffset time)
    {
        DateTime local = time.LocalDateTime;
        string clock = local.ToString("HH:mm", CultureInfo.CurrentUICulture);
        if (local.Date == DateTime.Today)
        {
            return string.Format(Lang.AboutSetting_TodayAt, clock);
        }
        if (local.Date == DateTime.Today.AddDays(-1))
        {
            return string.Format(Lang.AboutSetting_YesterdayAt, clock);
        }
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentUICulture);
    }


    /// <summary>
    /// 安装已下载的更新并重启。关于页安装的多是静默下载的包，用户还没看过说明，重启后照常弹出「最近更新内容」。
    /// </summary>
    [RelayCommand]
    private void RestartToInstall()
    {
        try
        {
            // 确保 RPC 子进程随主进程退出，避免占用 current 目录导致 Velopack 替换文件失败。
            AppConfig.GetService<RpcService>().KeepRunningOnExited(false, noLongerChange: true);
            AppConfig.GetService<UpdateService>().ApplyAndRestart(showUpdateContentAfterRestart: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Apply update and restart");
            UpdateStatusText = ex.Message;
        }
    }


    /// <summary>
    /// 打开发行说明窗口。
    /// </summary>
    private void Button_ReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReleaseNotesWindow.ShowOrActivate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open release notes window");
        }
    }




    /// <summary>
    /// 打开预填诊断信息的 GitHub Bug 表单，并打开日志文件夹。
    /// </summary>
    private async void Hyperlink_ReportIssue_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        try
        {
            await AppConfig.GetService<IssueReportService>().ReportBugAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report issue");
        }
    }




}
