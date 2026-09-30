using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.GameInstall;
using Starward.Features.GameSelector;
using Starward.Features.HoYoPlay;
using Starward.Features.UrlProtocol;
using Starward.Helpers;
using Starward.RPC;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

#pragma warning disable MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
#pragma warning disable MVVMTK0045 // Using [ObservableProperty] on fields is not AOT compatible for WinRT


namespace Starward.Features.GameLauncher;

[INotifyPropertyChanged]
public sealed partial class GameLauncherSettingDialog : ContentDialog
{


    private readonly ILogger<GameLauncherSettingDialog> _logger = AppConfig.GetLogger<GameLauncherSettingDialog>();


    private readonly HoYoPlayService _hoyoPlayService = AppConfig.GetService<HoYoPlayService>();


    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();


    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();


    public GameLauncherSettingDialog()
    {
        this.InitializeComponent();
        this.Loaded += GameLauncherSettingDialog_Loaded;
        this.Unloaded += GameLauncherSettingDialog_Unloaded;
    }



    public GameId CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; set; }




    private async void GameLauncherSettingDialog_Loaded(object sender, RoutedEventArgs e)
    {
        CurrentGameBiz = CurrentGameId?.GameBiz ?? GameBiz.None;
        WeakReferenceMessenger.Default.Register<AccentColorChangedMessage>(this, OnAccentColorChanged);
        CheckCanRepairGame();
        await InitializeBasicInfoAsync();
    }


    private void GameLauncherSettingDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }


    /// <summary>
    /// 自定义背景的强调色变化后，刷新对话框自身的视觉树，
    /// 使“选择”等使用强调色的控件实时生效
    /// </summary>
    private void OnAccentColorChanged(object _, AccentColorChangedMessage __)
    {
        try
        {
            if (this.Content is FrameworkElement ele)
            {
                ele.RequestedTheme = ele.ActualTheme switch
                {
                    ElementTheme.Light => ElementTheme.Dark,
                    ElementTheme.Dark => ElementTheme.Light,
                    _ => ElementTheme.Default,
                };
                ele.RequestedTheme = ElementTheme.Default;
            }
        }
        catch { }
    }




    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }





    #region 基本信息



    public GameBizIcon CurrentGameBizIcon { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 安装路径
    /// </summary>
    public string? InstallPath { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 文件夹大小
    /// </summary>
    public string? GameSize { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 是否可以卸载和修复
    /// </summary>
    public bool UninstallAndRepairEnabled { get; set => SetProperty(ref field, value); }

    private async Task InitializeBasicInfoAsync()
    {
        try
        {
            if (CurrentGameId.GameBiz.IsKnown())
            {
                CurrentGameBizIcon = new GameBizIcon(CurrentGameId.GameBiz);
            }
            else
            {
                var info = await _hoyoPlayService.GetGameInfoAsync(CurrentGameId);
                CurrentGameBizIcon = new GameBizIcon(info);
            }
            InstallPath = GameLauncherService.GetGameInstallPath(CurrentGameId, out bool storageRemoved);
            GameSize = GetSize(InstallPath);
            if (await _gameLauncherService.GetGameProcessAsync(CurrentGameId) is null)
            {
                UninstallAndRepairEnabled = InstallPath != null && !storageRemoved;
            }
            else
            {
                UninstallAndRepairEnabled = false;
            }
            // 版本信息要联网，不阻塞卸载、定位等等待基本信息刷新完的操作
            _ = InitializeVersionInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitializeBasicInfoAsync ({biz})", CurrentGameBiz);
        }
    }



    private static string? GetSize(string? path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }
        var size = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        return FormatSize(size);
    }



    /// <summary>
    /// 字节数格式化为 GB（按 1024 进位，与目录大小的显示一致）
    /// </summary>
    /// <param name="bytes">字节数</param>
    /// <returns></returns>
    private static string FormatSize(long bytes)
    {
        return $"{(double)bytes / (1 << 30):F2}GB";
    }



    /// <summary>
    /// 打开游戏安装文件夹
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task OpenInstalGameFolderAsync()
    {
        try
        {
            if (Directory.Exists(InstallPath))
            {
                await Launcher.LaunchUriAsync(new Uri(InstallPath));
            }
        }
        catch { }
    }


    /// <summary>
    /// 删除游戏安装路径
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task DeleteGameInstllPathAsync()
    {
        try
        {
            GameLauncherService.ChangeGameInstallPath(CurrentGameId, null);
            WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
            await InitializeBasicInfoAsync();
            await TryStopGameInstallTaskAsync();
        }
        catch { }
    }



    /// <summary>
    /// 定位游戏路径
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task LocateGameAsync()
    {
        try
        {
            string? previousInstallPath = InstallPath;
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (DriveHelper.GetDriveType(folder) is DriveType.Network && !new Uri(folder).IsUnc)
                {
                    TextBlock_NetworkDriveWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    TextBlock_NetworkDriveWarning.Visibility = Visibility.Collapsed;
                    GameLauncherService.ChangeGameInstallPath(CurrentGameId, folder);
                    await InitializeBasicInfoAsync();
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                    if (previousInstallPath != folder)
                    {
                        await TryStopGameInstallTaskAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Locate game failed {GameBiz}", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 检查是否可以修复游戏
    /// </summary>
    private void CheckCanRepairGame()
    {
        if (_gameInstallService.GetGameInstallTask(CurrentGameId) is GameInstallContext task)
        {
            if (task.State is not GameInstallState.Stop and not GameInstallState.Finish)
            {
                Button_RepairGame.IsEnabled = false;
            }
        }
    }



    /// <summary>
    /// 用户点了修复游戏时要接着打开的修复对话框，由 <see cref="OpenAsync"/> 在游戏设置关闭后打开
    /// </summary>
    private RepairGameDialog? _repairGameDialog;



    /// <summary>
    /// 打开游戏设置。用户点了修复游戏时，等游戏设置完全关闭后再打开修复对话框：ContentDialog 同一时间只能打开一个
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <param name="xamlRoot">对话框所在窗口</param>
    /// <returns></returns>
    public static async Task OpenAsync(GameId gameId, XamlRoot xamlRoot)
    {
        var dialog = new GameLauncherSettingDialog { CurrentGameId = gameId, XamlRoot = xamlRoot };
        await dialog.ShowAsync();
        if (dialog._repairGameDialog is RepairGameDialog repairGameDialog)
        {
            repairGameDialog.XamlRoot = xamlRoot;
            await repairGameDialog.ShowAsync();
        }
    }



    /// <summary>
    /// 修复游戏：关闭游戏设置，改为打开单独的修复对话框选择修复对象（与官方启动器一致）
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task RepairGameAsync()
    {
        try
        {
            if (!Directory.Exists(InstallPath))
            {
                return;
            }
            Telemetry.Track("game_setting_click", CurrentGameBiz, ("button", "repair"));
            bool hasWPFPackage = await HasWPFPackageAsync();
            _repairGameDialog = new RepairGameDialog
            {
                CurrentGameId = CurrentGameId,
                InstallPath = InstallPath,
                CurrentGameBizIcon = CurrentGameBizIcon,
                HasWPFPackage = hasWPFPackage,
                WPFIconUrl = hasWPFPackage ? await GetWPFIconUrlAsync() : null,
            };
            this.Hide();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repair game {GameBiz}", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 游戏是否带千星沙箱（WPF 包，目前只有原神）。取不到时按没有处理，修复对话框只列出游戏资源
    /// </summary>
    /// <returns></returns>
    private async Task<bool> HasWPFPackageAsync()
    {
        try
        {
            return await _hoyoPlayService.GetWPFPackageAsync(CurrentGameId) is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get WPF package ({biz})", CurrentGameBiz);
            return false;
        }
    }



    /// <summary>
    /// 千星沙箱的官方图标，取不到时为 <see langword="null"/>，修复对话框改用拼图图标
    /// </summary>
    /// <returns></returns>
    private async Task<string?> GetWPFIconUrlAsync()
    {
        try
        {
            return (await _hoyoPlayService.GetGameInfoAsync(CurrentGameId))?.Display?.WpfIcon?.Url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get WPF icon ({biz})", CurrentGameBiz);
            return null;
        }
    }




    public string? UninstallError { get; set => SetProperty(ref field, value); }



    [RelayCommand]
    private void ShowUninstallGameWarning()
    {
        try
        {
            Telemetry.Track("game_setting_click", CurrentGameBiz, ("button", "uninstall"));
            if (Directory.Exists(InstallPath))
            {
                string installPath = Path.GetFullPath(InstallPath);
                if (Path.GetPathRoot(InstallPath) == InstallPath)
                {
                    // 不能删除驱动器根目录
                    UninstallError = Lang.GameLauncherSettingDialog_CannotDeleteTheDriveRootDirectory;
                    TrackUninstallBlocked("drive_root");
                    return;
                }
                if (Directory.Exists(AppConfig.UserDataFolder))
                {
                    string userDataFolder = Path.GetFullPath(AppConfig.UserDataFolder);
                    if (userDataFolder.StartsWith(installPath))
                    {
                        // Starward 数据文件夹位于游戏文件夹内，删除游戏时会一并删除。请在设置页面修改数据文件夹位置后重试。
                        UninstallError = Lang.GameLauncherSettingDialog_UninstallGameUserDataFolderWarning;
                        TrackUninstallBlocked("data_folder_inside");
                        return;
                    }
                }
                string baseFolder = AppContext.BaseDirectory.TrimEnd('/', '\\');
                if (baseFolder.StartsWith(installPath))
                {
                    // Starward 程序位于游戏文件夹内，删除游戏时会一并被删除。请将程序移出游戏文件夹后重试。
                    UninstallError = Lang.GameLauncherSettingDialog_UninstallGameStarwardProgramFolderWarning;
                    TrackUninstallBlocked("program_inside");
                    return;
                }
                Grid_UninstallWarning.Visibility = Visibility.Visible;
                Telemetry.Track("uninstall_confirm_show", CurrentGameBiz, ("task_state", _gameInstallService.GetGameInstallTask(CurrentGameId)?.State));
            }
            else
            {
                TrackUninstallBlocked("path_missing");
                _ = InitializeBasicInfoAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show uninstall game warning {GameBiz}", CurrentGameBiz);
        }
    }



    [RelayCommand]
    private async Task UninstallGameAsync()
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            Telemetry.Track("game_setting_click", CurrentGameBiz, ("button", "uninstall_confirm"), ("task_state", _gameInstallService.GetGameInstallTask(CurrentGameId)?.State));
            UninstallError = null;
            if (await _gameLauncherService.GetGameProcessAsync(CurrentGameId) is not null)
            {
                UninstallError = Lang.LauncherPage_GameIsRunning;
                TrackUninstallBlocked("game_running");
                await InitializeBasicInfoAsync();
                return;
            }
            if (Directory.Exists(InstallPath))
            {
                if (_gameInstallService.GetGameInstallTask(CurrentGameId) is GameInstallContext task)
                {
                    // 先停止并移除安装任务（含已暂停的），否则首页仍显示暂停进度，点继续会在已删除的目录上复用旧的文件清单
                    await _gameInstallService.StopTaskAsync(task);
                }
                bool success = await _gameInstallService.StartUninstallAsync(CurrentGameId, InstallPath);
                Telemetry.Track("uninstall_result", CurrentGameBiz, ("result", success ? "success" : "rpc_unavailable"), ("duration_ms", Stopwatch.GetElapsedTime(start)));
                if (success)
                {
                    _logger.LogInformation("""
                        Uninstall game finished:
                        GameId: {gameId} {gameBiz}
                        InstallPath: {installPath}
                        """, CurrentGameId.Id, CurrentGameId.GameBiz, InstallPath);
                    Grid_UninstallWarning.Visibility = Visibility.Collapsed;
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                    CheckCanRepairGame();
                    await InitializeBasicInfoAsync();
                }
            }
            else
            {
                TrackUninstallBlocked("path_missing");
                await InitializeBasicInfoAsync();
            }
        }
        catch (Exception ex)
        {
            Telemetry.Track("uninstall_result", CurrentGameBiz, ("result", "error"), ("duration_ms", Stopwatch.GetElapsedTime(start)), ("error", ex.Message));
            UninstallError = ex.Message;
            _logger.LogError(ex, "Uninstall game failed {GameBiz}", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 记录卸载被前置检查拦下的原因
    /// </summary>
    /// <param name="reason">drive_root / data_folder_inside / program_inside / path_missing / game_running</param>
    private void TrackUninstallBlocked(string reason)
    {
        Telemetry.Track("uninstall_blocked", CurrentGameBiz, ("reason", reason));
    }



    private async Task TryStopGameInstallTaskAsync()
    {
        try
        {
            if (_gameInstallService.GetGameInstallTask(CurrentGameId) is GameInstallContext task)
            {
                if (task.State is not GameInstallState.Stop and not GameInstallState.Finish)
                {
                    await _gameInstallService.StopTaskAsync(task);
                    await Task.Delay(1000);
                    CheckCanRepairGame();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Try stop game install task {GameBiz}", CurrentGameBiz);
        }
    }




    #endregion




    #region 版本信息



    /// <summary>
    /// 每次刷新版本信息时递增，异步结果回来时与之比对，丢弃被新一轮刷新取代的旧结果
    /// </summary>
    private int _versionInfoToken;

    /// <summary>本地版本（config.ini 的 game_version）；未定位游戏时为 <see langword="null"/></summary>
    private string? _localVersionText;

    /// <summary>最新版本</summary>
    private string? _latestVersionText;

    /// <summary>最新版本旁的更新状态标签</summary>
    private string? _updateStatus;

    /// <summary>预下载版本</summary>
    private string? _predownloadVersionText;

    /// <summary>预下载版本旁的完成状态标签</summary>
    private string? _predownloadStatus;

    /// <summary>可用增量补丁的起点版本</summary>
    private string? _patchText;

    /// <summary>千星沙箱（WPF 包）的本地版本，有更新时带上最新版本</summary>
    private string? _wpfVersionText;

    /// <summary>千星沙箱版本旁的更新状态标签</summary>
    private string? _wpfStatus;

    /// <summary>主程序 MD5 与官方各版本记录的比对结果</summary>
    private string? _exeCheckText;

    /// <summary>主程序与本地版本不符或不在官方记录中</summary>
    private bool _exeCheckWarning;

    /// <summary>最新版本完整资源的解压后大小（含已装语音包）</summary>
    private string? _fullResourceText;

    /// <summary>
    /// 版本信息的各行，只含取到数据的项；一项都没有时为 <see langword="null"/>，整块隐藏
    /// </summary>
    public List<GameVersionInfoRow>? VersionInfoRows { get; set => SetProperty(ref field, value); }



    /// <summary>
    /// 刷新版本信息。各项独立获取，某项取不到时只隐藏对应的行。
    /// </summary>
    /// <returns></returns>
    private async Task InitializeVersionInfoAsync()
    {
        int token = ++_versionInfoToken;
        string? installPath = InstallPath;
        _localVersionText = null;
        _latestVersionText = null;
        _updateStatus = null;
        _predownloadVersionText = null;
        _predownloadStatus = null;
        _patchText = null;
        _wpfVersionText = null;
        _wpfStatus = null;
        _exeCheckText = null;
        _exeCheckWarning = false;
        _fullResourceText = null;

        Version? localVersion = null;
        GameBranch? branch = null;
        try
        {
            if (installPath is not null)
            {
                localVersion = await _gameLauncherService.GetLocalGameVersionAsync(CurrentGameId, installPath);
                if (token != _versionInfoToken)
                {
                    return;
                }
                // 已定位但没有 config.ini（例如下载到一半）时显示占位，与「未定位游戏」区分
                _localVersionText = localVersion?.ToString() ?? "-";
            }
            GameConfig? config = await _hoyoPlayService.GetGameConfigAsync(CurrentGameId);
            if (config?.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK or DownloadMode.DOWNLOAD_MODE_LDIFF)
            {
                branch = await _hoyoPlayService.GetGameBranchAsync(CurrentGameId);
            }
            if (token != _versionInfoToken)
            {
                return;
            }
            await InitializeLatestVersionAsync(token, installPath, localVersion, branch);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initialize version info ({biz})", CurrentGameBiz);
        }
        if (token != _versionInfoToken)
        {
            return;
        }
        // 对话框高度随行数变化，行分两批出现以减少跳动：先显示版本，千星沙箱、主程序校验与完整资源并行取完后再补上
        RefreshVersionInfoRows();
        await Task.WhenAll(InitializeWPFVersionAsync(token, installPath, localVersion), InitializeExeCheckAsync(token, installPath, localVersion), InitializeFullResourceSizeAsync(token, installPath, branch));
        if (token == _versionInfoToken)
        {
            RefreshVersionInfoRows();
        }
    }



    /// <summary>
    /// 最新版本、更新方式、预下载与增量补丁。
    /// </summary>
    /// <param name="token">本轮刷新的标记</param>
    /// <param name="installPath">游戏安装目录，未定位时为 <see langword="null"/></param>
    /// <param name="localVersion">本地版本</param>
    /// <param name="branch">Chunk 模式的游戏分支，压缩包模式或未发布安装包时为 <see langword="null"/></param>
    /// <returns></returns>
    private async Task InitializeLatestVersionAsync(int token, string? installPath, Version? localVersion, GameBranch? branch)
    {
        (Version? latestVersion, Version? predownloadVersion) = await _gameLauncherService.GetLatestGameVersionAsync(CurrentGameId);
        if (token != _versionInfoToken)
        {
            return;
        }
        _latestVersionText = latestVersion?.ToString();
        if (latestVersion is not null && localVersion is not null)
        {
            string status;
            if (localVersion >= latestVersion)
            {
                status = Lang.GameLauncherSettingDialog_UpToDate;
            }
            else if (branch is null)
            {
                status = Lang.GameLauncherSettingDialog_UpdateAvailable;
            }
            else
            {
                // 与更新任务的判断一致：本地版本在 diff_tags 里才有 ldiff 补丁，否则按块比对下载
                bool canPatch = branch.Main.DiffTags.Any(x => x == localVersion.ToString());
                status = $"{Lang.GameLauncherSettingDialog_UpdateAvailable} · {(canPatch ? Lang.GameLauncherSettingDialog_IncrementalPatch : Lang.GameLauncherSettingDialog_ChunkCompare)}";
            }
            _updateStatus = status;
        }
        if (branch is not null)
        {
            _patchText = branch.Main.DiffTags is { Count: > 0 } tags
                ? string.Format(Lang.GameLauncherSettingDialog_PatchFromVersions, string.Join(", ", tags))
                : Lang.GameLauncherSettingDialog_NoPatch;
        }
        if (predownloadVersion is not null && (localVersion is null || predownloadVersion > localVersion))
        {
            _predownloadVersionText = predownloadVersion.ToString();
            if (installPath is not null && localVersion is not null)
            {
                bool finished = await _gamePackageService.CheckPreDownloadFinishedAsync(CurrentGameId, installPath);
                if (token != _versionInfoToken)
                {
                    return;
                }
                _predownloadStatus = finished ? Lang.GameLauncherSettingDialog_PredownloadFinished : Lang.GameLauncherSettingDialog_PredownloadNotFinished;
            }
        }
    }



    /// <summary>
    /// 千星沙箱（WPF 包，目前只有原神有）的本地版本与是否最新。游戏没有 WPF 包、未定位或未装完时不显示。
    /// 判断「最新」与安装任务一致：本地记录的版本与官方当前版本字符串相同，否则下次安装、更新或修复时会重新下载。
    /// </summary>
    /// <param name="token">本轮刷新的标记</param>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="localVersion">本地游戏版本，为 <see langword="null"/> 时游戏还没装完</param>
    /// <returns></returns>
    private async Task InitializeWPFVersionAsync(int token, string? installPath, Version? localVersion)
    {
        try
        {
            if (installPath is null || localVersion is null)
            {
                return;
            }
            WPFPackage? package = await _hoyoPlayService.GetWPFPackageAsync(CurrentGameId);
            if (package is null || string.IsNullOrWhiteSpace(package.Version))
            {
                return;
            }
            string? localWpfVersion = await GameLauncherService.GetLocalWPFVersionAsync(installPath);
            if (token != _versionInfoToken)
            {
                return;
            }
            if (localWpfVersion is null)
            {
                _wpfVersionText = Lang.WelcomeView_NotInstalled;
            }
            else if (localWpfVersion == package.Version)
            {
                _wpfVersionText = localWpfVersion;
                _wpfStatus = Lang.GameLauncherSettingDialog_UpToDate;
            }
            else
            {
                _wpfVersionText = $"{localWpfVersion} → {package.Version}";
                _wpfStatus = Lang.GameLauncherSettingDialog_UpdateAvailable;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get WPF package version ({biz})", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 用官方记录的各版本主程序 MD5 校验本地主程序，能发现 config.ini 版本号与实际文件不符（例如手动覆盖过文件）。
    /// 官方没有该游戏的记录（如崩坏3）或主程序不存在时不显示。
    /// </summary>
    /// <param name="token">本轮刷新的标记</param>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="localVersion">本地版本</param>
    /// <returns></returns>
    private async Task InitializeExeCheckAsync(int token, string? installPath, Version? localVersion)
    {
        try
        {
            if (installPath is null)
            {
                return;
            }
            string exe = Path.Join(installPath, await _gameLauncherService.GetGameExeNameAsync(CurrentGameId));
            if (!File.Exists(exe))
            {
                return;
            }
            GameScanInfo? scanInfo = await _hoyoPlayService.GetGameScanInfoAsync(CurrentGameId);
            if (scanInfo?.GameExeList is not { Count: > 0 } exeList)
            {
                return;
            }
            string md5 = await Task.Run(async () =>
            {
                using FileStream fs = File.OpenRead(exe);
                return Convert.ToHexStringLower(await MD5.HashDataAsync(fs));
            });
            if (token != _versionInfoToken)
            {
                return;
            }
            GameScanInfoExe? matched = exeList.FirstOrDefault(x => string.Equals(x.MD5, md5, StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                _exeCheckText = Lang.GameLauncherSettingDialog_ExeUnknown;
                _exeCheckWarning = true;
            }
            else if (localVersion is null || (Version.TryParse(matched.Version, out Version? exeVersion) && exeVersion == localVersion))
            {
                _exeCheckText = string.Format(Lang.GameLauncherSettingDialog_ExeMatches, matched.Version);
                _exeCheckWarning = false;
            }
            else
            {
                _exeCheckText = string.Format(Lang.GameLauncherSettingDialog_ExeMismatch, matched.Version);
                _exeCheckWarning = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Check game exe ({biz})", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 最新版本完整资源的解压后大小：全部非语音分类，加上本地已装的语音包。
    /// </summary>
    /// <param name="token">本轮刷新的标记</param>
    /// <param name="installPath">游戏安装目录，未定位时只统计非语音分类</param>
    /// <param name="branch">Chunk 模式的游戏分支</param>
    /// <returns></returns>
    private async Task InitializeFullResourceSizeAsync(int token, string? installPath, GameBranch? branch)
    {
        try
        {
            if (branch is null)
            {
                return;
            }
            GameSophonChunkBuild? build = await _hoyoPlayService.GetGameSophonChunkBuildAsync(branch, branch.Main);
            if (build is null)
            {
                return;
            }
            long gameSize = 0;
            foreach (GameSophonChunkManifest manifest in build.Manifests)
            {
                if (manifest.MatchingField.Length is 5 or 10 && manifest.MatchingField.Contains('-'))
                {
                    // 跳过语音包 zh-cn or mini-zh-cn，与安装时的分类选取一致
                    continue;
                }
                gameSize += manifest.Stats.UncompressedSize;
            }
            long audioSize = 0;
            if (installPath is not null)
            {
                AudioLanguage audioLanguage = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId, installPath);
                foreach (AudioLanguage lang in (AudioLanguage[])[AudioLanguage.Chinese, AudioLanguage.English, AudioLanguage.Japanese, AudioLanguage.Korean])
                {
                    if (audioLanguage.HasFlag(lang) && build.Manifests.FirstOrDefault(x => x.MatchingField == lang.ToDescription()) is GameSophonChunkManifest audioManifest)
                    {
                        audioSize += audioManifest.Stats.UncompressedSize;
                    }
                }
            }
            if (token != _versionInfoToken)
            {
                return;
            }
            _fullResourceText = audioSize > 0
                ? string.Format(Lang.GameLauncherSettingDialog_ResourceSizeWithAudio, FormatSize(gameSize + audioSize), FormatSize(audioSize))
                : FormatSize(gameSize);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get full resource size ({biz})", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 按当前取到的数据重建版本信息的各行。数据分两批异步到达，每到一批重建一次；
    /// 只有前面有行时才画分隔线，这样隐藏的项不会留下多余的线。
    /// </summary>
    private void RefreshVersionInfoRows()
    {
        var rows = new List<GameVersionInfoRow>();
        void Add(string label, string? value, string? badge = null, bool isWarning = false)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new GameVersionInfoRow { Label = label, Value = value, Badge = badge, IsWarning = isWarning, ShowDivider = rows.Count > 0 });
            }
        }
        Add(Lang.GameLauncherSettingDialog_LocalVersion, _localVersionText);
        Add(Lang.GameLauncherSettingDialog_LatestVersion, _latestVersionText, _updateStatus);
        Add(Lang.LauncherPage_PreInstall, _predownloadVersionText, _predownloadStatus);
        Add(Lang.GameLauncherSettingDialog_IncrementalPatch, _patchText);
        Add(Lang.GameLauncherSettingDialog_MiliastraSandbox, _wpfVersionText, _wpfStatus);
        if (!string.IsNullOrWhiteSpace(_wpfVersionText))
        {
            // 紧跟千星沙箱版本，对应官方启动器千星沙箱弹窗里的「自动为我更新」
            rows.Add(new GameVersionInfoRow
            {
                Label = Lang.GameLauncherSettingDialog_AutoUpdateSandbox,
                HasToggle = true,
                IsToggleOn = AppConfig.GetAutoUpdateWPFPackage(CurrentGameBiz),
                ShowDivider = rows.Count > 0,
                ToggleChanged = OnWPFPackageAutoUpdateToggled,
            });
        }
        Add(Lang.GameLauncherSettingDialog_ExeCheck, _exeCheckText, isWarning: _exeCheckWarning);
        Add(Lang.GameLauncherSettingDialog_FullResources, _fullResourceText);
        Add(Lang.GameLauncherSettingDialog_LocalSize, GameSize);
        VersionInfoRows = rows.Count > 0 ? rows : null;
    }



    /// <summary>
    /// 切换千星沙箱自动更新；打开时如果官方有新版本，立即在后台开始更新
    /// </summary>
    /// <param name="isOn">是否自动更新</param>
    private void OnWPFPackageAutoUpdateToggled(bool isOn)
    {
        AppConfig.SetAutoUpdateWPFPackage(CurrentGameBiz, isOn);
        Telemetry.Track("game_setting_click", CurrentGameBiz, ("button", "wpf_auto_update"), ("on", isOn));
        if (isOn)
        {
            _ = StartWPFPackageAutoUpdateAsync();
        }
    }



    /// <summary>
    /// 满足自动更新条件时在后台更新千星沙箱，并通知首页显示进度
    /// </summary>
    /// <returns></returns>
    private async Task StartWPFPackageAutoUpdateAsync()
    {
        try
        {
            if (InstallPath is null)
            {
                return;
            }
            if (await _gameInstallService.TryStartWPFPackageAutoUpdateAsync(CurrentGameId, InstallPath) is GameInstallContext task)
            {
                WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
                CheckCanRepairGame();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto update WPF package ({biz})", CurrentGameBiz);
        }
    }



    #endregion





    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }


}



/// <summary>
/// 游戏设置对话框版本信息中的一行。XAML 类型信息会为它生成无参构造与属性 setter，所以不能用 required / init。
/// </summary>
public sealed class GameVersionInfoRow
{

    /// <summary>
    /// 左侧标签
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// 右侧取值
    /// </summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// 取值左边的状态标签，没有时为 <see langword="null"/>
    /// </summary>
    public string? Badge { get; set; }

    /// <summary>
    /// 取值是否用警示色
    /// </summary>
    public bool IsWarning { get; set; }

    /// <summary>
    /// 是否在行顶画分隔线（首行不画）
    /// </summary>
    public bool ShowDivider { get; set; }

    /// <summary>
    /// 右侧是开关而不是取值
    /// </summary>
    public bool HasToggle { get; set; }

    /// <summary>
    /// 开关状态，用户拨动时通过 <see cref="ToggleChanged"/> 通知
    /// </summary>
    public bool IsToggleOn
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                ToggleChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// 开关被拨动后的回调。用 internal：公开的委托属性会被 XAML 类型信息收录
    /// </summary>
    internal Action<bool>? ToggleChanged { get; set; }

    public bool IsNormal => !IsWarning && !HasToggle;

    public bool HasBadge => !string.IsNullOrEmpty(Badge);

}
