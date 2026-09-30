using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.GameSelector;
using Starward.Helpers;
using Starward.RPC;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace Starward.Features.GameInstall;

/// <summary>
/// 卸载游戏：列出当前区服与同一游戏已安装的其他区服，默认一并卸载；勾选「保留其他区服」则只卸载当前区服。
/// 默认全部卸载是因为硬链接的区服共用文件，只卸载其中一个几乎不释放空间。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class UninstallGameDialog : ContentDialog
{


    private readonly ILogger<UninstallGameDialog> _logger = AppConfig.GetLogger<UninstallGameDialog>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();



    public UninstallGameDialog()
    {
        this.InitializeComponent();
    }



    public GameId CurrentGameId { get; set; }

    /// <summary>
    /// 当前区服的安装目录，打开前由调用方确认存在
    /// </summary>
    public string InstallPath { get; set; } = "";

    /// <summary>
    /// 当前区服的图标，未知区服（新游戏）只能从游戏信息取
    /// </summary>
    public GameBizIcon? CurrentGameBizIcon { get; set; }


    /// <summary>
    /// 当前区服在第一行，其余按 <see cref="GameBiz.AllGameBizs"/> 的顺序
    /// </summary>
    public ObservableCollection<UninstallServerItem> Servers { get; } = new();

    public bool HasOtherServers { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 保留其他区服，只卸载当前区服（以及与它同一目录的区服）
    /// </summary>
    public bool KeepOtherServers
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RefreshState();
            }
        }
    }

    /// <summary>
    /// 没有在卸载，可以关闭对话框、改勾选
    /// </summary>
    public bool IsIdle
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RefreshState();
            }
        }
    } = true;

    /// <summary>
    /// 正在检查各区服的游戏是否在运行，查完前不能卸载
    /// </summary>
    private bool _isChecking = true;

    public bool CanUninstall => IsIdle && !_isChecking && Servers.Count > 0 && Servers.Where(x => x.WillUninstall(KeepOtherServers)).All(x => x.IsNotBlocked);

    /// <summary>
    /// 保留的区服里有与当前区服硬链接的，提示只卸载当前区服释放不了多少空间
    /// </summary>
    public bool ShowHardLinkHint => KeepOtherServers && Servers.Any(x => x.IsHardLinked && !x.WillUninstall(true));

    public string? ErrorMessage { get; set => SetProperty(ref field, value); }



    private async void ContentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (Servers.Count > 0)
        {
            return;
        }
        try
        {
            string currentFolder = NormalizeFolder(InstallPath);
            List<(GameId GameId, string InstallPath)> others = GameInstallService.GetOtherInstalledServers(CurrentGameId);
            Servers.Add(new UninstallServerItem(CurrentGameId, CurrentGameBizIcon ?? new GameBizIcon(CurrentGameId.GameBiz), currentFolder, true));
            foreach ((GameId gameId, string path) in others)
            {
                Servers.Add(new UninstallServerItem(gameId, new GameBizIcon(gameId.GameBiz), path, false)
                {
                    InCurrentFolder = IsSameOrSubFolder(path, currentFolder),
                    ShowDivider = true,
                });
            }
            HasOtherServers = others.Count > 0;
            foreach (UninstallServerItem item in Servers)
            {
                item.PathBlockReason = GetPathBlockReason(item.InstallPath, out string? telemetry);
                item.PathBlockTelemetry = telemetry;
            }
            await CheckRunningAsync();
            foreach (UninstallServerItem item in Servers.Where(x => x.IsBlocked))
            {
                Telemetry.Track("uninstall_blocked", item.GameId.GameBiz, ("reason", item.PathBlockTelemetry ?? "game_running"));
            }

            // 硬链接只用来显示标签和提示，不影响能否卸载，最后在后台查
            List<(GameId GameId, string InstallPath)> linked = await Task.Run(() => GameInstallService.GetHardLinkedServers(currentFolder, others));
            if (linked.Count > 0)
            {
                // 硬链接的各个名字地位相同，只标「硬链接」的话本体也会被标上；按目录创建时间区分本体与后来产生的区服
                List<UninstallServerItem> group = Servers.Where(x => x.IsCurrent || linked.Any(l => l.GameId.GameBiz == x.GameId.GameBiz)).ToList();
                string sourceFolder = GameInstallService.GetHardLinkSourceFolder(group.Select(x => x.InstallPath));
                foreach (UninstallServerItem item in group)
                {
                    item.IsHardLinked = !item.IsCurrent;
                    item.IsHardLinkSource = string.Equals(item.InstallPath, sourceFolder, StringComparison.OrdinalIgnoreCase);
                    item.IsHardLinkCopy = !item.IsHardLinkSource;
                }
            }
            RefreshState();
            Telemetry.Track("uninstall_confirm_show", CurrentGameId.GameBiz,
                ("task_state", _gameInstallService.GetGameInstallTask(CurrentGameId)?.State),
                ("others", others.Select(x => x.GameId.GameBiz.Value).ToList()),
                ("linked", linked.Select(x => x.GameId.GameBiz.Value).ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize uninstall game dialog ({GameBiz})", CurrentGameId.GameBiz);
            ErrorMessage = ex.Message;
        }
    }



    private void ContentDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // 卸载途中不能关闭（含按 Esc），否则看不到结果，删到一半也无从得知
        if (!IsIdle)
        {
            args.Cancel = true;
        }
    }



    /// <summary>
    /// 勾选或状态变化后刷新各行的透明度与按钮状态
    /// </summary>
    private void RefreshState()
    {
        foreach (UninstallServerItem item in Servers)
        {
            item.RowOpacity = item.WillUninstall(KeepOtherServers) ? 1 : 0.4;
        }
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(ShowHardLinkHint));
        UninstallCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }



    /// <summary>
    /// 检查各区服的游戏是否在运行，运行中的区服不能卸载
    /// </summary>
    /// <returns>要卸载的区服都没有在运行</returns>
    private async Task<bool> CheckRunningAsync()
    {
        _isChecking = true;
        RefreshState();
        try
        {
            foreach (UninstallServerItem item in Servers)
            {
                item.UpdateBlockReason(await _gameLauncherService.GetGameProcessAsync(item.GameId) is not null);
            }
        }
        finally
        {
            _isChecking = false;
            RefreshState();
        }
        return Servers.Where(x => x.WillUninstall(KeepOtherServers)).All(x => x.IsNotBlocked);
    }



    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        List<UninstallServerItem> targets = Servers.Where(x => x.WillUninstall(KeepOtherServers)).ToList();
        Telemetry.Track("game_setting_click", CurrentGameId.GameBiz,
            ("button", "uninstall_confirm"),
            ("keep_others", KeepOtherServers),
            ("servers", targets.Select(x => x.GameId.GameBiz.Value).ToList()),
            ("task_state", _gameInstallService.GetGameInstallTask(CurrentGameId)?.State));
        ErrorMessage = null;
        IsIdle = false;
        bool anyUninstalled = false;
        try
        {
            // 打开对话框后游戏可能刚被启动
            if (!await CheckRunningAsync())
            {
                Telemetry.Track("uninstall_blocked", CurrentGameId.GameBiz, ("reason", "game_running"));
                return;
            }
            // 一个区服的目录在另一个区服的目录里时先卸载里面的，否则外层删完后里层区服的截图没机会备份
            foreach (UninstallServerItem item in targets.OrderBy(x => targets.Any(o => o != x && IsSubFolder(x.InstallPath, o.InstallPath)) ? 0 : 1))
            {
                if (!await UninstallServerAsync(item))
                {
                    return;
                }
                anyUninstalled = true;
            }
            _logger.LogInformation("Uninstall game finished: {Servers}", targets.Select(x => x.GameId.GameBiz.Value));
            InAppToast.MainWindow?.Success(Lang.LauncherPage_UninstallationCompleted);
            IsIdle = true;
            this.Hide();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uninstall game failed {GameBiz}", CurrentGameId.GameBiz);
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (anyUninstalled)
            {
                WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
            }
            IsIdle = true;
        }
    }



    /// <summary>
    /// 卸载一个区服：停止它的安装任务，通过 RPC 删除目录，再清除保存的安装路径
    /// </summary>
    /// <param name="item">要卸载的区服</param>
    /// <returns>卸载成功；RPC 服务没有启动时为 <see langword="false"/>，并显示错误</returns>
    /// <exception cref="Exception">RPC 删除失败</exception>
    private async Task<bool> UninstallServerAsync(UninstallServerItem item)
    {
        long start = Stopwatch.GetTimestamp();
        item.IsFailed = false;
        item.IsRunning = true;
        try
        {
            // 与当前区服同一目录的区服，目录已经随前面的区服删掉，只需清除安装路径
            if (Directory.Exists(item.InstallPath))
            {
                if (_gameInstallService.GetGameInstallTask(item.GameId) is GameInstallContext task)
                {
                    // 先停止并移除安装任务（含已暂停的），否则首页仍显示暂停进度，点继续会在已删除的目录上复用旧的文件清单
                    await _gameInstallService.StopTaskAsync(task);
                }
                if (!await _gameInstallService.StartUninstallAsync(item.GameId, item.InstallPath))
                {
                    Telemetry.Track("uninstall_result", item.GameId.GameBiz, ("result", "rpc_unavailable"), ("duration_ms", Stopwatch.GetElapsedTime(start)));
                    item.IsFailed = true;
                    ErrorMessage = Lang.LauncherPage_UninstallationError;
                    return false;
                }
            }
            // 立即清除：可移动存储设备上的目录删掉后，读取路径时会被当成设备已移除而不会自动清除；
            // 其他区服卸载时也据此判断共用的日志目录是否还有人在用
            GameLauncherService.ChangeGameInstallPath(item.GameId, null);
            Telemetry.Track("uninstall_result", item.GameId.GameBiz, ("result", "success"), ("duration_ms", Stopwatch.GetElapsedTime(start)));
            _logger.LogInformation("Uninstalled {GameBiz}: {InstallPath}", item.GameId.GameBiz, item.InstallPath);
            item.IsDone = true;
            return true;
        }
        catch (Exception ex)
        {
            Telemetry.Track("uninstall_result", item.GameId.GameBiz, ("result", "error"), ("duration_ms", Stopwatch.GetElapsedTime(start)), ("error", ex.Message));
            item.IsFailed = true;
            throw;
        }
        finally
        {
            item.IsRunning = false;
        }
    }



    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Close()
    {
        this.Hide();
    }



    /// <summary>
    /// 目录本身能否删除：不能是驱动器根目录，不能包含本软件的数据文件夹或程序
    /// </summary>
    /// <param name="folder">规范化后的安装目录</param>
    /// <param name="telemetry">不能删除时的埋点原因</param>
    /// <returns>不能删除的原因；可以删除时为 <see langword="null"/></returns>
    private static string? GetPathBlockReason(string folder, out string? telemetry)
    {
        telemetry = null;
        if (string.Equals(Path.GetPathRoot(folder), folder, StringComparison.OrdinalIgnoreCase))
        {
            telemetry = "drive_root";
            return Lang.GameLauncherSettingDialog_CannotDeleteTheDriveRootDirectory;
        }
        if (Directory.Exists(AppConfig.UserDataFolder) && IsSameOrSubFolder(NormalizeFolder(AppConfig.UserDataFolder), folder))
        {
            telemetry = "data_folder_inside";
            return Lang.GameLauncherSettingDialog_UninstallGameUserDataFolderWarning;
        }
        if (IsSameOrSubFolder(NormalizeFolder(AppContext.BaseDirectory), folder))
        {
            telemetry = "program_inside";
            return Lang.GameLauncherSettingDialog_UninstallGameStarwardProgramFolderWarning;
        }
        return null;
    }


    private static string NormalizeFolder(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }


    /// <summary>
    /// <paramref name="path"/> 与 <paramref name="folder"/> 相同或位于其中
    /// </summary>
    private static bool IsSameOrSubFolder(string path, string folder)
    {
        return string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) || IsSubFolder(path, folder);
    }


    /// <summary>
    /// <paramref name="path"/> 位于 <paramref name="folder"/> 之中（不含相同）
    /// </summary>
    private static bool IsSubFolder(string path, string folder)
    {
        return path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }


}
