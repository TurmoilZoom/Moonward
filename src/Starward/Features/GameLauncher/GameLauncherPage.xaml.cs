using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.CloudGame;
using Starward.Features.GameInstall;
using Starward.Features.HoYoPlay;
using Starward.Features.Overlay;
using Starward.Features.Setting;
using Starward.Features.ViewHost;
using Starward.Frameworks;
using Starward.Helpers;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;


namespace Starward.Features.GameLauncher;

public sealed partial class GameLauncherPage : PageBase
{


    private readonly ILogger<GameLauncherPage> _logger = AppConfig.GetLogger<GameLauncherPage>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();

    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();

    private readonly BackgroundService _backgroundService = AppConfig.GetService<BackgroundService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    private readonly HoYoPlayService _hoYoPlayService = AppConfig.GetService<HoYoPlayService>();


    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _dispatchTimer;

    /// <summary>右侧功能工具栏离开后自动收起 / 回贴边的延迟。</summary>
    private static readonly TimeSpan RightToolbarCollapseDelay = TimeSpan.FromSeconds(5);

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _rightToolbarCollapseTimer;

    private readonly HashSet<FlyoutBase> _rightToolbarHookedFlyouts = new();

    private bool _rightToolbarInitialized;


    public GameLauncherPage()
    {
        this.InitializeComponent();
        _dispatchTimer = DispatcherQueue.CreateTimer();
        _dispatchTimer.Interval = TimeSpan.FromMilliseconds(100);
        _dispatchTimer.Tick += UpdateGameInstallTaskProgress;
        _rightToolbarCollapseTimer = DispatcherQueue.CreateTimer();
        _rightToolbarCollapseTimer.IsRepeating = false;
        _rightToolbarCollapseTimer.Interval = RightToolbarCollapseDelay;
        _rightToolbarCollapseTimer.Tick += RightToolbarCollapseTimer_Tick;
        if (AppConfig.ToolbarPinned)
        {
            IsToolbarPinned = true;
            ToolbarPinTooltip = Lang.GameLauncherPage_UnpinToolbar;
        }
        else
        {
            IsToolbarPinned = false;
            ToolbarPinTooltip = Lang.GameLauncherPage_PinToolbar;
        }
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }


    /// <summary>
    /// 语言切换后刷新启动页 x:Bind 绑定与工具栏 Tooltip。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="__">语言变更消息（未使用）。</param>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
        ToolbarPinTooltip = IsToolbarPinned
            ? Lang.GameLauncherPage_UnpinToolbar
            : Lang.GameLauncherPage_PinToolbar;
    }



    protected override void OnLoaded()
    {
        InitializeGameFeature();
        CheckGameVersion();
        UpdateGameInstallTask();
        CheckCloudGame();
        // 工具栏始终可用；不要等背景列表异步返回才 Visible，否则 InstantTooltip 可能错过 Loaded 挂接。
        Border_SwitchBackgroundImage.Visibility = Visibility.Visible;
        SetBottomToolbarRevealed(AppConfig.ToolbarPinned);
        InitializeRightToolbarCollapse();
        _ = InitializeGameServerAsync();
        _ = InitializeBackgameImageSwitcherAsync();
        WeakReferenceMessenger.Default.Register<GameInstallPathChangedMessage>(this, OnGameInstallPathChanged);
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnMainWindowStateChanged);
        WeakReferenceMessenger.Default.Register<RemovableStorageDeviceChangedMessage>(this, OnRemovableStorageDeviceChanged);
        WeakReferenceMessenger.Default.Register<GameInstallTaskStartedMessage>(this, OnGameInstallTaskStarted);
        WeakReferenceMessenger.Default.Register<BackgroundChangedMessage>(this, OnBackgroundChanged);
        WeakReferenceMessenger.Default.Register<BackgroundDisplayedMessage>(this, OnBackgroundDisplayed);
    }



    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _dispatchTimer.Tick -= UpdateGameInstallTaskProgress;
        _dispatchTimer.Stop();
        TeardownRightToolbarCollapse();
        BackgroundImages = null!;
    }




    /// <summary>
    /// 当前游戏是否在下侧工具栏显示好感壁纸入口。
    /// </summary>
    public bool ShowFavorWallpaper { get; set => SetProperty(ref field, value); }


    private void InitializeGameFeature()
    {
        GameFeatureConfig feature = GameFeatureConfig.FromGameId(CurrentGameId);
        ShowFavorWallpaper = feature.SupportFavorWallpaper;
        if (feature.SupportCloudGame)
        {
            Button_CloudGame.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }



    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstalledLocateGameEnabled))]
    public partial GameState GameState { get; set; }




    [RelayCommand]
    private async Task ClickStartGameButtonAsync()
    {
        await Task.Delay(1);
        switch (GameState)
        {
            case GameState.None:
                break;
            case GameState.StartGame:
                await StartGameAsync();
                break;
            case GameState.GameIsRunning:
            case GameState.InstallGame:
                await InstallGameAsync();
                break;
            case GameState.Installing:
                await ChangeGameInstallTaskStateAsync();
                break;
            case GameState.UpdateGame:
                await UpdateGameAsync();
                break;
            case GameState.UpdatePlugin:
            case GameState.ResumeDownload:
                await ResumeDownloadAsync();
                break;
            case GameState.ComingSoon:
                break;
            default:
                break;
        }
    }




    #region Game Server


    public List<GameServerConfig>? GameServers { get; set => SetProperty(ref field, value); }

    [ObservableProperty]
    public partial GameServerConfig? SelectedGameServer { get; set; }
    partial void OnSelectedGameServerChanged(GameServerConfig? oldValue, GameServerConfig? newValue)
    {
        if (oldValue is not null && newValue is not null)
        {
            AppConfig.LastGameIdOfBH3Global = newValue.GameId;
            WeakReferenceMessenger.Default.Send(new BH3GlobalGameServerChangedMessage(newValue.GameId));
        }
    }


    /// <summary>
    /// 初始化区服选项，仅崩坏三国际服使用
    /// </summary>
    /// <returns></returns>
    private async Task InitializeGameServerAsync()
    {
        try
        {
            GameInfo? gameInfo;
            if (CurrentGameBiz == GameBiz.bh3_global)
            {
                gameInfo = await _hoYoPlayService.GetGameInfoAsync(GameId.FromGameBiz(GameBiz.bh3_global)!);
            }
            else
            {
                gameInfo = await _hoYoPlayService.GetGameInfoAsync(CurrentGameId);
            }
            if (gameInfo?.GameServerConfigs?.Count > 0)
            {
                GameServers = gameInfo.GameServerConfigs;
                if (GameServers.FirstOrDefault(x => x.GameId == CurrentGameId.Id) is GameServerConfig config)
                {
                    SelectedGameServer = config;
                }
                else
                {
                    SelectedGameServer = GameServers.FirstOrDefault();
                    if (SelectedGameServer is not null)
                    {
                        CurrentGameId.Id = SelectedGameServer.GameId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize game server");
        }
    }



    #endregion




    #region Game Version


    public string? GameInstallPath { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 可移动存储设备提示
    /// </summary>
    public bool IsInstallPathRemovableTipEnabled { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 已安装？定位游戏
    /// </summary>
    public bool InstalledLocateGameEnabled => GameState is GameState.InstallGame && !IsInstallPathRemovableTipEnabled;

    /// <summary>
    /// 预下载按钮是否可用
    /// </summary>
    public bool IsPredownloadButtonEnabled { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 预下载是否完成
    /// </summary>
    public bool IsPredownloadFinished { get; set => SetProperty(ref field, value); }


    private Version? localGameVersion;


    private Version? latestGameVersion;


    private Version? predownloadGameVersion;


    private bool isGameExeExists;


    /// <summary>
    /// 是否显示 DX12 选项
    /// </summary>
    public bool IsDX12OptionVisible { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// DX12 配置
    /// </summary>
    private GameDXConfig? _dxConfig;


    /// <summary>
    /// 启用 DX12
    /// </summary>
    public bool EnableDX12
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.SetEnableDX12(CurrentGameBiz, value);
            }
        }
    }


    private async void CheckGameVersion()
    {
        try
        {
            GameInstallPath = GameLauncherService.GetGameInstallPath(CurrentGameId, out bool storageRemoved);
            IsInstallPathRemovableTipEnabled = storageRemoved;
            if (GameInstallPath is null || storageRemoved)
            {
                GameState = GameState.InstallGame;
                return;
            }
            isGameExeExists = await _gameLauncherService.IsGameExeExistsAsync(CurrentGameId);
            localGameVersion = await _gameLauncherService.GetLocalGameVersionAsync(CurrentGameId);
            if (isGameExeExists && localGameVersion != null)
            {
                GameState = GameState.StartGame;
            }
            else
            {
                GameState = GameState.ResumeDownload;
                return;
            }
            await CheckGameRunningAsync();
            (latestGameVersion, predownloadGameVersion) = await _gameLauncherService.GetLatestGameVersionAsync(CurrentGameId);
            if (latestGameVersion > localGameVersion)
            {
                GameState = GameState.UpdateGame;
                return;
            }
            if (predownloadGameVersion > localGameVersion)
            {
                IsPredownloadButtonEnabled = true;
                IsPredownloadFinished = await _gamePackageService.CheckPreDownloadFinishedAsync(CurrentGameId);
            }
            _ = CheckDX12ConfigAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check game version");
        }
    }



    /// <summary>
    /// 检查 DX12 配置
    /// </summary>
    /// <returns></returns>
    private async Task CheckDX12ConfigAsync()
    {
        try
        {
            EnableDX12 = AppConfig.GetEnableDX12(CurrentGameBiz);
            if (EnableDX12)
            {
                IsDX12OptionVisible = true;
            }

            List<GameDXConfig> dxConfigs = await _hoYoPlayService.GetGameDXConfigsAsync([CurrentGameId]);
            _dxConfig = dxConfigs.FirstOrDefault(x => x.GameId == CurrentGameId);

            if (_dxConfig?.EnableDXSwitch is true)
            {
                IsDX12OptionVisible = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check DX12 config");
        }
    }


    /// <summary>
    /// 获取 DX12 启动参数
    /// </summary>
    /// <returns></returns>
    public string? GetDX12LaunchArgument()
    {
        if (EnableDX12 && _dxConfig is not null)
        {
            return _dxConfig.CmdArgs;
        }
        return null;
    }


    /// <summary>
    /// 显示 DX12 说明对话框
    /// </summary>
    private async void Hyperlink_DX12Intro_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender, Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        if (_dxConfig is not null)
        {
            await new DX12IntroDialog { GameDXConfig = _dxConfig, XamlRoot = this.XamlRoot }.ShowAsync();
        }
    }



    /// <summary>
    /// 定位游戏路径
    /// </summary>
    /// <returns></returns>
    private async Task LocateGameAsync()
    {
        try
        {
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (DriveHelper.GetDriveType(folder) is DriveType.Network && !new Uri(folder).IsUnc)
                {
                    InAppToast.MainWindow?.Warning(null, Lang.InstallGameDialog_MappedNetworkDrivesAreNotSupportedPleaseUseANetworkSharePathStartingWithDoubleBackslashes, 0);
                }
                else
                {
                    GameLauncherService.ChangeGameInstallPath(CurrentGameId, folder);
                    CheckGameVersion();
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Locate game");
        }
    }



    /// <summary>
    /// 定位游戏路径
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    private async void Hyperlink_LocateGame_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender, Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        await LocateGameAsync();
    }




    private void OnGameInstallPathChanged(object _, GameInstallPathChangedMessage message)
    {
        CheckGameVersion();
    }




    private void OnMainWindowStateChanged(object _, MainWindowStateChangedMessage message)
    {
        try
        {
            if (message.Activate && (message.ElapsedOver(TimeSpan.FromMinutes(10)) || message.IsCrossingHour))
            {
                CheckGameVersion();
            }
        }
        catch { }
    }




    private void OnRemovableStorageDeviceChanged(object _, RemovableStorageDeviceChangedMessage message)
    {
        try
        {
            CheckGameVersion();
        }
        catch { }
    }




    #endregion




    #region Start Game




    private Timer processTimer;


    [ObservableProperty]
    private partial Process? GameProcess { get; set; }
    partial void OnGameProcessChanged(Process? oldValue, Process? newValue)
    {
        processTimer?.Stop();
        if (processTimer is null)
        {
            processTimer = new(1000);
            processTimer.Elapsed += (_, _) => CheckGameExited();
        }
        if (newValue != null)
        {
            processTimer?.Start();
            RunningGameInfo = $"{newValue.ProcessName}.exe ({newValue.Id})";
            RunningGameService.AddRuninngGame(CurrentGameBiz, newValue);
        }
        else
        {
            RunningGameInfo = null;
            _logger.LogInformation("Game process exited");
        }
    }



    public string? RunningGameInfo { get; set => SetProperty(ref field, value); }


    public string? RunningGameTime { get; set => SetProperty(ref field, value); }


    private async Task<bool> CheckGameRunningAsync()
    {
        try
        {
            GameProcess = await _gameLauncherService.GetGameProcessAsync(CurrentGameId);
            if (GameProcess != null)
            {
                GameState = GameState.GameIsRunning;
                RunningGameTime = TimeSpanToString(DateTime.Now - GameProcess.StartTime);
                _logger.LogInformation("Game is running ({name}, {pid})", GameProcess.ProcessName, GameProcess.Id);
                return true;
            }
        }
        catch { }
        return false;
    }




    private void CheckGameExited()
    {
        try
        {
            if (GameProcess != null)
            {
                if (GameProcess.HasExited)
                {
                    DispatcherQueue.TryEnqueue(() => RunningGameTime = null);
                    DispatcherQueue.TryEnqueue(CheckGameVersion);
                    GameProcess = null;
                }
                else
                {
                    DispatcherQueue?.TryEnqueue(() => RunningGameTime = TimeSpanToString(DateTime.Now - GameProcess.StartTime));
                }
            }
        }
        catch { }
    }



    private static string TimeSpanToString(TimeSpan value)
    {
        return $"{value.Days * 24 + value.Hours:D2}:{value.Minutes:D2}:{value.Seconds:D2}";
    }



    [RelayCommand]
    private async Task StartGameAsync()
    {
        try
        {
            // 点击开始游戏按当前生效的启动方式：「无」不依赖启动参数配置；config1 用 legacy 键；其余用额外配置。
            AppConfig.ResolveLaunchProfile(CurrentGameBiz, AppConfig.GetActiveLaunchProfileId(CurrentGameBiz), out bool useNone, out GameLaunchProfile? profile);
            var process = await _gameLauncherService.StartGameAsync(CurrentGameId, null, profile, useNone);
            if (process is not null)
            {
                GameState = GameState.GameIsRunning;
                GameProcess = process;
                WeakReferenceMessenger.Default.Send(new GameStartedMessage());
            }
        }
        catch (FileNotFoundException)
        {
            CheckGameVersion();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start game");
        }
    }




    #endregion




    #region Install Game




    private async Task InstallGameAsync()
    {
        try
        {
            if (_gameInstallTask is null)
            {
                await new InstallGameDialog { CurrentGameId = CurrentGameId, XamlRoot = this.XamlRoot, }.ShowAsync();
            }
            else
            {
                await ChangeGameInstallTaskStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install game {GameBiz}", CurrentGameBiz);
        }
    }



    private async Task ResumeDownloadAsync()
    {
        try
        {
            if (!Directory.Exists(GameInstallPath))
            {
                CheckGameVersion();
                return;
            }
            AudioLanguage audio = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId, GameInstallPath);
            var task = await _gameInstallService.StartInstallAsync(CurrentGameId, GameInstallPath, audio);
            if (task is not null)
            {
                _gameInstallTask = task;
                _dispatchTimer.Start();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume download {GameBiz}", CurrentGameBiz);
        }
    }






    #endregion




    #region Predownload




    [RelayCommand]
    private async Task PredownloadAsync()
    {
        try
        {
            if (_gameInstallTask is null)
            {
                await new PreDownloadDialog { CurrentGameId = this.CurrentGameId, XamlRoot = this.XamlRoot }.ShowAsync();
            }
            else if (_gameInstallTask.Operation is GameInstallOperation.Predownload)
            {
                if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Paused or GameInstallState.Error or GameInstallState.Queueing)
                {
                    await _gameInstallService.ContinueTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else if (_gameInstallTask.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying)
                {
                    await _gameInstallService.PauseTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else
                {
                    // GameInstallState.Stop
                    CheckGameVersion();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, nameof(PredownloadAsync));
            if (_gameInstallTask?.Operation is GameInstallOperation.Predownload)
            {
                _gameInstallTask.State = GameInstallState.Error;
                _gameInstallTask.ErrorMessage = ex.Message;
            }
        }
    }





    #endregion



    #region Update



    private async Task UpdateGameAsync()
    {
        try
        {
            if (localGameVersion is not null && latestGameVersion > localGameVersion)
            {
                AudioLanguage audio = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId, GameInstallPath);
                GameInstallContext? task = await _gameInstallService.StartUpdateAsync(CurrentGameId, GameInstallPath!, audio);
                if (task is not null)
                {
                    _gameInstallTask = task;
                    _dispatchTimer.Start();
                }
            }
            else
            {
                CheckGameVersion();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update game {GameBiz}", CurrentGameBiz);
        }
    }



    #endregion



    #region Game Install Task




    private GameInstallContext? _gameInstallTask;



    private async Task ChangeGameInstallTaskStateAsync()
    {
        try
        {
            if (_gameInstallTask is null)
            {
                CheckGameVersion();
            }
            else if (_gameInstallTask.Operation is not GameInstallOperation.Predownload)
            {
                if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Paused or GameInstallState.Error or GameInstallState.Queueing)
                {
                    await _gameInstallService.ContinueTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else if (_gameInstallTask.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying)
                {
                    await _gameInstallService.PauseTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else
                {
                    // GameInstallState.Stop
                    CheckGameVersion();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change game install task state {GameBiz}", CurrentGameBiz);
        }
    }



    private void UpdateGameInstallTask()
    {
        try
        {
            _gameInstallTask ??= _gameInstallService.GetGameInstallTask(CurrentGameId);
            if (_gameInstallTask is not null)
            {
                if (_gameInstallTask.Operation is GameInstallOperation.Predownload)
                {
                    IsPredownloadButtonEnabled = true;
                }
                _dispatchTimer.Start();
            }
        }
        catch { }
    }



    private void OnGameInstallTaskStarted(object _, GameInstallTaskStartedMessage message)
    {
        if (message.InstallTask.GameId == CurrentGameId)
        {
            _gameInstallTask = message.InstallTask;
            _dispatchTimer.Start();
        }
    }



    private void UpdateGameInstallTaskProgress(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_gameInstallTask is null)
        {
            _dispatchTimer.Stop();
            return;
        }
        try
        {
            if (_gameInstallTask.Operation is GameInstallOperation.Predownload)
            {
                Button_Predownload.UpdateGameInstallTaskState(_gameInstallTask);
            }
            else
            {
                GameState = GameState.Installing;
                Button_StartGame.UpdateGameInstallTaskState(_gameInstallTask);
            }
            if (_gameInstallTask.State is GameInstallState.Error)
            {
                _dispatchTimer.Stop();
            }
            else if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Finish)
            {
                _dispatchTimer.Stop();
                _gameInstallTask = null;
                CheckGameVersion();
            }
        }
        catch { }
    }




    #endregion




    #region Drop Background File




    private void RootGrid_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            Border_BackgroundDragIn.Opacity = 1;
        }
    }




    private async void RootGrid_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        Border_BackgroundDragIn.Opacity = 0;
        var defer = e.GetDeferral();
        try
        {
            if ((await e.DataView.GetStorageItemsAsync()).FirstOrDefault() is StorageFile file)
            {
                string? name = await BackgroundService.ChangeCustomBackgroundFileAsync(file);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }
                AppConfig.SetCustomBg(CurrentGameBiz, name);
                AppConfig.SetEnableCustomBg(CurrentGameBiz, true);
                AppConfig.SetBg(CurrentGameBiz, name);
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
            }
        }
        catch (COMException ex)
        {
            InAppToast.MainWindow?.Error(Lang.GameLauncherSettingDialog_CannotDecodeFile);
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            InAppToast.MainWindow?.Error(Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs);
            _logger.LogError(ex, "Change custom background failed");
        }
        defer.Complete();
    }



    private void RootGrid_DragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        Border_BackgroundDragIn.Opacity = 0;
    }



    #endregion




    #region Game Setting



    [RelayCommand]
    private async Task OpenGameLauncherSettingDialogAsync()
    {
        await new GameLauncherSettingDialog { CurrentGameId = this.CurrentGameId, XamlRoot = this.XamlRoot }.ShowAsync();
    }



    /// <summary>
    /// 打开自定义背景对话框（由背景工具栏的图标触发）
    /// </summary>
    [RelayCommand]
    private async Task OpenCustomBackgroundDialogAsync()
    {
        await new CustomBackgroundDialog { CurrentGameId = this.CurrentGameId, XamlRoot = this.XamlRoot }.ShowAsync();
    }


    /// <summary>
    /// 打开好感壁纸对话框（独立图标）；所选视频仍写入自定义背景。
    /// </summary>
    [RelayCommand]
    private async Task OpenFavorWallpaperDialogAsync()
    {
        await new FavorWallpaperDialog { CurrentGameId = this.CurrentGameId, XamlRoot = this.XamlRoot }.ShowAsync();
    }




    #endregion



    #region Switch Background Image


    private const string PlayIcon = "\uF5B0";

    private const string PauseIcon = "\uE62E";


    public List<GameBackground> BackgroundImages { get; set => SetProperty(ref field, value); }

    public bool CanStopVideo { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 是否可切换背景图（多张背景时才显示页码指示器与分隔符）。
    /// </summary>
    public bool CanSwitchBackgroundImage { get; set => SetProperty(ref field, value); }

    public string StartStopButtonIcon { get; set => SetProperty(ref field, value); }

    public bool IsToolbarPinned { get; set => SetProperty(ref field, value); }

    public string ToolbarPinTooltip { get; set => SetProperty(ref field, value); }


    private int currentBackgroundImageIndex;
    public int CurrentBackgroundImageIndex
    {
        get => currentBackgroundImageIndex;
        set
        {
            if (SetProperty(ref currentBackgroundImageIndex, value))
            {
                ChangeBackgroundImageIndex(value);
            }
        }
    }


    private void OnBackgroundChanged(object _, BackgroundChangedMessage message)
    {
        if (message.GameBackground is null)
        {
            _ = InitializeBackgameImageSwitcherAsync();
        }
    }


    /// <summary>
    /// 避免出现“显示静态海报但仍有播放/暂停按钮”的不一致状态。
    /// </summary>
    private void OnBackgroundDisplayed(object _, BackgroundDisplayedMessage message)
    {
        try
        {
            if (BackgroundImages is null)
            {
                return;
            }
            GameBackground? actual = message.GameBackground;
            if (actual is not null && BackgroundImages.FirstOrDefault(x => x.Id == actual.Id) is GameBackground match)
            {
                int index = BackgroundImages.IndexOf(match);
                if (index >= 0 && index != currentBackgroundImageIndex)
                {
                    currentBackgroundImageIndex = index;
                    OnPropertyChanged(nameof(CurrentBackgroundImageIndex));
                }
                CanStopVideo = match.Type is GameBackground.BACKGROUND_TYPE_VIDEO;
                if (CanStopVideo)
                {
                    match.StopVideo = actual.StopVideo;
                    StartStopButtonIcon = actual.StopVideo ? PlayIcon : PauseIcon;
                }
            }
            else
            {
                // 当前显示的是静态海报、回退图片或自定义图片等，没有可播放的视频。
                CanStopVideo = false;
            }
        }
        catch { }
    }


    private async Task InitializeBackgameImageSwitcherAsync()
    {
        try
        {
            CanStopVideo = false;
            BackgroundImages = await _backgroundService.GetGameBackgroundsAsync(CurrentGameId);
            // 工具栏始终可用（承载「显示游戏公告」开关等），仅页码指示器与分隔符随是否多图显隐。
            CanSwitchBackgroundImage = BackgroundImages.Count > 1;
            Border_SwitchBackgroundImage.Visibility = Visibility.Visible;
            SetBottomToolbarRevealed(AppConfig.ToolbarPinned);
            if (CanSwitchBackgroundImage)
            {
                GameBackground? currentBackground = await _backgroundService.GetSuggestedGameBackgroundAsync(CurrentGameId);
                if (currentBackground != null && BackgroundImages.FirstOrDefault(x => x.Id == currentBackground.Id) is GameBackground current)
                {
                    currentBackgroundImageIndex = Math.Clamp(BackgroundImages.IndexOf(current), 0, BackgroundImages.Count - 1);
                    OnPropertyChanged(nameof(CurrentBackgroundImageIndex));
                    CanStopVideo = current.Type is GameBackground.BACKGROUND_TYPE_VIDEO;
                    if (CanStopVideo)
                    {
                        current.StopVideo = currentBackground.StopVideo;
                        StartStopButtonIcon = current.StopVideo ? PlayIcon : PauseIcon;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize background image switcher {GameBiz}", CurrentGameBiz);
        }
    }


    private void ChangeBackgroundImageIndex(int index)
    {
        try
        {
            if (index < 0 || index >= BackgroundImages.Count)
            {
                return;
            }
            GameBackground current = BackgroundImages[index];
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage(current));
            CanStopVideo = current.Type is GameBackground.BACKGROUND_TYPE_VIDEO;
            if (CanStopVideo)
            {
                StartStopButtonIcon = current.StopVideo ? PlayIcon : PauseIcon;
            }
        }
        catch { }
    }


    private void Border_SwitchBackgroundImage_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetBottomToolbarRevealed(true);
    }


    private void Border_SwitchBackgroundImage_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!AppConfig.ToolbarPinned)
        {
            SetBottomToolbarRevealed(false);
        }
    }


    [RelayCommand]
    private void PinToolbar()
    {
        AppConfig.ToolbarPinned = !AppConfig.ToolbarPinned;
        IsToolbarPinned = AppConfig.ToolbarPinned;
        if (AppConfig.ToolbarPinned)
        {
            ToolbarPinTooltip = Lang.GameLauncherPage_UnpinToolbar;
            SetBottomToolbarRevealed(true);
        }
        else
        {
            ToolbarPinTooltip = Lang.GameLauncherPage_PinToolbar;
            // 指针多半还停在图钉上：先藏栏并关掉命中，避免透明按钮继续弹出 InstantTooltip。
            SetBottomToolbarRevealed(false);
            InstantTooltip.Dismiss(XamlRoot);
        }
    }


    /// <summary>
    /// 下侧工具栏显隐。隐藏时先关掉按钮命中，避免透明按钮仍弹出 InstantTooltip。
    /// 不在此处 Dismiss：页面加载 / 指针离开时关气泡会误伤右侧工具栏和左侧导航正在显示的提示。
    /// </summary>
    /// <param name="revealed">是否以可见、可交互状态展示工具栏。</param>
    private void SetBottomToolbarRevealed(bool revealed)
    {
        if (revealed)
        {
            Border_SwitchBackgroundImage.Opacity = 1;
            StackPanel_SwitchBackgroundImage.IsHitTestVisible = true;
            return;
        }

        StackPanel_SwitchBackgroundImage.IsHitTestVisible = false;
        Border_SwitchBackgroundImage.Opacity = 0;
    }


    int _switchBackgroundTotalDelta = 0;

    private void Border_SwitchBackgroundImage_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        _switchBackgroundTotalDelta += delta;
        if (_switchBackgroundTotalDelta <= -120)
        {
            CurrentBackgroundImageIndex++;
            _switchBackgroundTotalDelta = 0;
        }
        else if (_switchBackgroundTotalDelta >= 120)
        {
            CurrentBackgroundImageIndex--;
            _switchBackgroundTotalDelta = 0;
        }
    }


    [RelayCommand]
    public void OpenBackgroundViewWindow()
    {
        try
        {
            new BackgroundViewWindow().Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open background view window.");
        }
    }


    [RelayCommand]
    private void StartOrStopVideoBackground()
    {
        try
        {
            GameBackground current = BackgroundImages[CurrentBackgroundImageIndex];
            if (current.Type is GameBackground.BACKGROUND_TYPE_VIDEO)
            {
                current.StopVideo = !current.StopVideo;
                StartStopButtonIcon = current.StopVideo ? PlayIcon : PauseIcon;
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage(current));
            }
        }
        catch { }
    }



    #endregion



    #region Game Announcement


    /// <summary>
    /// 是否在首页显示游戏公告（Banner 与帖子）。原位于「游戏设置-基本信息」，现移到壁纸下方工具栏。
    /// </summary>
    public bool EnableBannerAndPost
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableBannerAndPost = value;
                OnPropertyChanged(nameof(BannerAndPostButtonIcon));
                OnPropertyChanged(nameof(BannerAndPostTooltip));
                // GameBannerAndPost 监听此消息，读取 AppConfig 后自行显隐与刷新内容。
                WeakReferenceMessenger.Default.Send(new GameAnnouncementSettingChangedMessage());
            }
        }
    } = AppConfig.EnableBannerAndPost;


    /// <summary>
    /// 工具栏「显示游戏公告」按钮图标：显示时为带斜杠的闭眼，隐藏时为睁眼。
    /// </summary>
    public string BannerAndPostButtonIcon => EnableBannerAndPost ? "\uED1A" : "\uE890";


    /// <summary>
    /// 工具栏公告开关 Tooltip：已显示时为「隐藏」，已隐藏时为「显示」（与图钉文案同一套动作语义）。
    /// </summary>
    public string BannerAndPostTooltip => EnableBannerAndPost
        ? Lang.LauncherPage_HideGameAnnouncement
        : Lang.LauncherPage_ShowGameAnnouncement;


    [RelayCommand]
    private void ToggleBannerAndPost()
    {
        EnableBannerAndPost = !EnableBannerAndPost;
    }


    #endregion


    #region Play Time Visibility


    /// <summary>
    /// 是否在首页右下角显示游戏时长按钮。由壁纸下方工具栏开关控制，持久化到设置。
    /// </summary>
    public bool EnablePlayTime
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnablePlayTime = value;
                OnPropertyChanged(nameof(PlayTimeVisibilityTooltip));
            }
        }
    } = AppConfig.EnablePlayTime;


    /// <summary>
    /// 工具栏游戏时长开关 Tooltip：已显示时为「隐藏游戏时长」，已隐藏时为「显示游戏时长」。
    /// </summary>
    public string PlayTimeVisibilityTooltip => EnablePlayTime
        ? Lang.LauncherPage_HidePlayTime
        : Lang.LauncherPage_ShowPlayTime;


    [RelayCommand]
    private void TogglePlayTimeVisibility()
    {
        EnablePlayTime = !EnablePlayTime;
        UpdatePlayTimeToggleVisual(animate: true);
    }


    private void Grid_PlayTimeToggle_Loaded(object sender, RoutedEventArgs e)
    {
        // 加载时直接落到当前状态，不播过渡动画（避免开机/切页时无谓的拨动）。
        UpdatePlayTimeToggleVisual(animate: false);
    }


    /// <summary>
    /// 更新工具栏「显示 / 隐藏游戏时长」的两枚拨动图标：显示态亮 ToggleRight（拨到开）、隐藏态亮 ToggleLeft（拨到关）。
    /// 用户点击时以「横向滑动 + 交叉淡化」过渡——新图标从行进反侧滑入、旧图标顺行进方向滑出——合起来读成拨钮从一端滑到另一端。
    /// </summary>
    /// <param name="animate">true 播放过渡动画（用户切换）；false 直接落到目标状态（加载）。</param>
    private void UpdatePlayTimeToggleVisual(bool animate)
    {
        if (Icon_PlayTimeShown is null || Icon_PlayTimeHidden is null)
        {
            return;
        }
        bool shown = EnablePlayTime;
        FontIcon incoming = shown ? Icon_PlayTimeShown : Icon_PlayTimeHidden;
        FontIcon outgoing = shown ? Icon_PlayTimeHidden : Icon_PlayTimeShown;

        // 用 Composition 的 Translation（叠加在布局位置上，不会像直接改 Visual.Offset 那样破坏 Grid 居中）。
        ElementCompositionPreview.SetIsTranslationEnabled(incoming, true);
        ElementCompositionPreview.SetIsTranslationEnabled(outgoing, true);
        Visual vin = ElementCompositionPreview.GetElementVisual(incoming);
        Visual vout = ElementCompositionPreview.GetElementVisual(outgoing);

        if (!animate || !EntranceAnimation.AnimationsEnabled())
        {
            vin.Opacity = 1;
            vout.Opacity = 0;
            vin.Properties.InsertVector3("Translation", Vector3.Zero);
            vout.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        // 拨钮行进方向：开→右(+1)、关→左(-1)。
        float dir = shown ? 1f : -1f;
        const float slide = 4f;
        TimeSpan duration = TimeSpan.FromMilliseconds(250);
        Compositor compositor = vin.Compositor;
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        // 新图标：从行进反侧滑入 + 淡入。
        vin.Properties.InsertVector3("Translation", new Vector3(-dir * slide, 0, 0));
        Vector3KeyFrameAnimation inMove = compositor.CreateVector3KeyFrameAnimation();
        inMove.InsertKeyFrame(1f, Vector3.Zero, ease);
        inMove.Duration = duration;
        vin.StartAnimation("Translation", inMove);
        ScalarKeyFrameAnimation inFade = compositor.CreateScalarKeyFrameAnimation();
        inFade.InsertKeyFrame(1f, 1f, ease);
        inFade.Duration = duration;
        vin.StartAnimation(nameof(Visual.Opacity), inFade);

        // 旧图标：顺行进方向滑出 + 淡出。
        Vector3KeyFrameAnimation outMove = compositor.CreateVector3KeyFrameAnimation();
        outMove.InsertKeyFrame(1f, new Vector3(dir * slide, 0, 0), ease);
        outMove.Duration = duration;
        vout.StartAnimation("Translation", outMove);
        ScalarKeyFrameAnimation outFade = compositor.CreateScalarKeyFrameAnimation();
        outFade.InsertKeyFrame(1f, 0f, ease);
        outFade.Duration = duration;
        vout.StartAnimation(nameof(Visual.Opacity), outFade);
    }


    #endregion


    #region Cloud Game


    private void CheckCloudGame()
    {
        try
        {
            Process? process = CloudGameService.GetCloudGameProcess(CurrentGameId);
            if (process is not null)
            {
                RunningGameService.AddRuninngGame(CurrentGameBiz, process);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check cloud game {GameBiz}", CurrentGameBiz);
        }
    }



    #endregion


    #region Right Toolbar State Machine


    /// <summary>工具栏按钮统一边长（与 XAML 中 Width/Height 一致）。</summary>
    private const double RightToolbarButtonSize = 40;

    /// <summary>贴边收纳时统一露出的边缘条宽度（各边一致）。</summary>
    private const double RightToolbarDockPeek = 6;

    /// <summary>自由态相对窗口的默认边距（右 / 上）。</summary>
    private const double RightToolbarDefaultMarginRight = 12;

    private const double RightToolbarDefaultMarginTop = 48;

    /// <summary>按住后移动超过该距离才进入拖拽，避免纯点击被吃掉。</summary>
    private const double RightToolbarDragStartDistance = 4;

    /// <summary>高度 / 贴边位移动画时长。</summary>
    private static readonly TimeSpan RightToolbarAnimDuration = TimeSpan.FromMilliseconds(280);


    /// <summary>右侧工具栏视觉状态。</summary>
    private enum RightToolbarState
    {
        /// <summary>自由位置，仅显示首个按钮。</summary>
        Collapsed,

        /// <summary>自由位置，完整展开。</summary>
        Expanded,

        /// <summary>拖拽中：保持当前高度并跟随指针（不收缩）。</summary>
        Dragging,

        /// <summary>贴边收纳：完整展开尺寸，仅漏出统一宽度的边缘条；不可点功能。</summary>
        Docked,

        /// <summary>自贴边完整浮出，可点功能 / 提示 / 再拖拽。</summary>
        DockRevealed,
    }


    /// <summary>贴边方向。</summary>
    private enum RightToolbarDockEdge
    {
        None,
        Left,
        Top,
        Right,
        Bottom,
    }


    private RightToolbarState _rightToolbarState = RightToolbarState.Collapsed;

    private RightToolbarDockEdge _rightToolbarDockEdge = RightToolbarDockEdge.None;

    private bool _rightToolbarPointerOver;

    private bool _rightToolbarPressed;

    private bool _rightToolbarDragging;

    /// <summary>贴边浮出动画完成前为 false，禁止点击与拖拽。</summary>
    private bool _rightToolbarRevealInteractive;

    /// <summary>
    /// 贴边后需等指针先离开再重新进入才允许浮出，避免松手时指针仍在工具栏上立刻弹出。
    /// </summary>
    private bool _rightToolbarDockAwaitPointerLeave;

    /// <summary>拖拽松手后短时内禁止按钮点击 / Flyout，避免误触功能。</summary>
    private bool _rightToolbarSuppressClick;

    /// <summary>TeachingTip.Closed 时是否记「已看过」；卸载强关为 false。</summary>
    private bool _rightToolbarDragTipMarkSeenOnClose = true;

    private uint _rightToolbarPointerId;

    private Point _rightToolbarPressRootPoint;

    private Point _rightToolbarGrabOffset;

    private double _rightToolbarX;

    private double _rightToolbarY;

    private Storyboard? _rightToolbarHeightStoryboard;

    private Storyboard? _rightToolbarMoveStoryboard;

    // 必须用同一委托实例 RemoveHandler；handledEventsToo 才能在子 Button Handled/捕获后仍收到事件。
    private PointerEventHandler? _rightToolbarPointerPressedHandler;
    private PointerEventHandler? _rightToolbarPointerMovedHandler;
    private PointerEventHandler? _rightToolbarPointerReleasedHandler;
    private PointerEventHandler? _rightToolbarPointerCanceledHandler;
    private PointerEventHandler? _rightToolbarPointerCaptureLostHandler;
    private PointerEventHandler? _rightToolbarRootMovedHandler;
    private PointerEventHandler? _rightToolbarRootReleasedHandler;
    private bool _rightToolbarRootHandlersAttached;

    /// <summary>进页时记下的 XamlRoot；卸载时 XamlRoot 可能已空，仍要靠它解除 Tooltip 抑制。</summary>
    private XamlRoot? _instantTooltipXamlRoot;


    /// <summary>
    /// 初始化右侧工具栏状态机：默认右上角收起，监听尺寸与 Flyout。
    /// </summary>
    private void InitializeRightToolbarCollapse()
    {
        if (_rightToolbarInitialized)
        {
            return;
        }

        _rightToolbarInitialized = true;
        _instantTooltipXamlRoot = XamlRoot;
        StackPanel_RightToolbar.SizeChanged += StackPanel_RightToolbar_SizeChanged;
        Border_RightToolbar.SizeChanged += Border_RightToolbar_SizeChanged;
        RootGrid.SizeChanged += RootGrid_RightToolbar_SizeChanged;
        // 子级 ButtonBase 会把 Pressed 标 Handled 并 Capture，普通 XAML 路由到不了 Border。
        _rightToolbarPointerPressedHandler = Border_RightToolbar_PointerPressed;
        _rightToolbarPointerMovedHandler = Border_RightToolbar_PointerMoved;
        _rightToolbarPointerReleasedHandler = Border_RightToolbar_PointerReleased;
        _rightToolbarPointerCanceledHandler = Border_RightToolbar_PointerCanceled;
        _rightToolbarPointerCaptureLostHandler = Border_RightToolbar_PointerCaptureLost;
        Border_RightToolbar.AddHandler(UIElement.PointerPressedEvent, _rightToolbarPointerPressedHandler, handledEventsToo: true);
        Border_RightToolbar.AddHandler(UIElement.PointerMovedEvent, _rightToolbarPointerMovedHandler, handledEventsToo: true);
        Border_RightToolbar.AddHandler(UIElement.PointerReleasedEvent, _rightToolbarPointerReleasedHandler, handledEventsToo: true);
        Border_RightToolbar.AddHandler(UIElement.PointerCanceledEvent, _rightToolbarPointerCanceledHandler, handledEventsToo: true);
        Border_RightToolbar.AddHandler(UIElement.PointerCaptureLostEvent, _rightToolbarPointerCaptureLostHandler, handledEventsToo: true);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            EnsureRightToolbarFlyoutHooks();
            RestoreRightToolbarLayout();
            UpdateRightToolbarPopupSide();
            TryShowRightToolbarDragTip();
        });
    }


    /// <summary>
    /// 卸载时解除计时器、动画与事件，恢复 Tooltip。
    /// </summary>
    private void TeardownRightToolbarCollapse()
    {
        // 离开页面时持久化当前布局；未关掉的引导下次再出。
        DismissRightToolbarDragTip(markSeen: false);
        SaveRightToolbarLayout();
        _rightToolbarCollapseTimer.Tick -= RightToolbarCollapseTimer_Tick;
        _rightToolbarCollapseTimer.Stop();
        _rightToolbarHeightStoryboard?.Stop();
        _rightToolbarHeightStoryboard = null;
        _rightToolbarMoveStoryboard?.Stop();
        _rightToolbarMoveStoryboard = null;
        InstantTooltip.SetSuppressed(_instantTooltipXamlRoot ?? XamlRoot, false);
        _instantTooltipXamlRoot = null;
        DetachRightToolbarRootPointerHandlers();
        if (_rightToolbarInitialized)
        {
            StackPanel_RightToolbar.SizeChanged -= StackPanel_RightToolbar_SizeChanged;
            Border_RightToolbar.SizeChanged -= Border_RightToolbar_SizeChanged;
            RootGrid.SizeChanged -= RootGrid_RightToolbar_SizeChanged;
            if (_rightToolbarPointerPressedHandler is not null)
            {
                Border_RightToolbar.RemoveHandler(UIElement.PointerPressedEvent, _rightToolbarPointerPressedHandler);
                Border_RightToolbar.RemoveHandler(UIElement.PointerMovedEvent, _rightToolbarPointerMovedHandler!);
                Border_RightToolbar.RemoveHandler(UIElement.PointerReleasedEvent, _rightToolbarPointerReleasedHandler!);
                Border_RightToolbar.RemoveHandler(UIElement.PointerCanceledEvent, _rightToolbarPointerCanceledHandler!);
                Border_RightToolbar.RemoveHandler(UIElement.PointerCaptureLostEvent, _rightToolbarPointerCaptureLostHandler!);
            }
        }

        foreach (FlyoutBase flyout in _rightToolbarHookedFlyouts)
        {
            flyout.Opened -= RightToolbarFlyout_Opened;
            flyout.Closed -= RightToolbarFlyout_Closed;
        }
        _rightToolbarHookedFlyouts.Clear();
        _rightToolbarInitialized = false;
    }


    /// <summary>
    /// 窗口尺寸变化：贴边态重新对齐边缘 peek，自由态保证不完全丢失在界外。
    /// </summary>
    private void RootGrid_RightToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        if (_rightToolbarState is RightToolbarState.Docked)
        {
            ApplyRightToolbarDockedPosition(animate: false);
        }
        else if (_rightToolbarState is RightToolbarState.DockRevealed)
        {
            Point revealed = GetRightToolbarRevealedPosition(_rightToolbarDockEdge);
            SetRightToolbarPosition(revealed.X, revealed.Y, animate: false);
        }
        else if (_rightToolbarState is not RightToolbarState.Dragging)
        {
            ClampRightToolbarIntoSoftBounds();
            SetRightToolbarPosition(_rightToolbarX, _rightToolbarY, animate: false);
        }
    }


    /// <summary>
    /// 子项显隐变化时同步高度（贴边始终展开；自由态保持当前展开/收起意图）。
    /// </summary>
    private void StackPanel_RightToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            if (_rightToolbarHeightStoryboard is not null || _rightToolbarState is RightToolbarState.Dragging)
            {
                return;
            }

            EnsureRightToolbarFlyoutHooks();
            SyncRightToolbarHeightForState(animate: false);
            UpdateRightToolbarPopupSide();

            if (_rightToolbarState is RightToolbarState.Docked)
            {
                ApplyRightToolbarDockedPosition(animate: false);
            }
            else if (_rightToolbarState is RightToolbarState.DockRevealed)
            {
                Point revealed = GetRightToolbarRevealedPosition(_rightToolbarDockEdge);
                SetRightToolbarPosition(revealed.X, revealed.Y, animate: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Right toolbar size changed");
        }
    }


    private void Border_RightToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            UpdateRightToolbarClip(e.NewSize.Width, e.NewSize.Height);
        }
    }


    #region Pointer


    private void Border_RightToolbar_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // 贴边松手后指针往往还在条上：等先离开再进入才浮出，否则会「收不进边界、立刻弹出」。
        if (_rightToolbarState is RightToolbarState.Docked && _rightToolbarDockAwaitPointerLeave)
        {
            return;
        }

        _rightToolbarPointerOver = true;
        StopRightToolbarCollapseTimer();

        switch (_rightToolbarState)
        {
            case RightToolbarState.Collapsed:
                TransitionRightToolbar(RightToolbarState.Expanded, animate: true);
                break;
            case RightToolbarState.Docked:
                // 贴边时悬停只做浮出；浮出完成前不可点功能。
                TransitionRightToolbar(RightToolbarState.DockRevealed, animate: true);
                break;
            case RightToolbarState.DockRevealed:
            case RightToolbarState.Expanded:
            case RightToolbarState.Dragging:
                break;
        }
    }


    private void Border_RightToolbar_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 拖拽中指针可能短暂离开视觉区域，不触发收起计时。
        if (_rightToolbarDragging || _rightToolbarPressed)
        {
            return;
        }

        _rightToolbarPointerOver = false;
        // 指针真正离开贴边条后，允许下一次进入触发浮出。
        if (_rightToolbarState is RightToolbarState.Docked)
        {
            _rightToolbarDockAwaitPointerLeave = false;
        }
        ScheduleRightToolbarIdleCollapse();
    }


    private void Border_RightToolbar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 贴边未浮出完成：忽略按下（仅允许悬停触发浮出）。
        if (_rightToolbarState is RightToolbarState.Docked
            || (_rightToolbarState is RightToolbarState.DockRevealed && !_rightToolbarRevealInteractive))
        {
            return;
        }

        if (!e.GetCurrentPoint(Border_RightToolbar).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 仅左键；触摸/笔亦可（IsLeftButtonPressed 对触控主触点通常为 true）。
        _rightToolbarPressed = true;
        _rightToolbarDragging = false;
        _rightToolbarPointerId = e.Pointer.PointerId;
        _rightToolbarPressRootPoint = e.GetCurrentPoint(RootGrid).Position;
        // 抓取点相对工具栏左上角（布局坐标 + 当前位移）。
        Point rootPoint = _rightToolbarPressRootPoint;
        _rightToolbarGrabOffset = new Point(rootPoint.X - _rightToolbarX, rootPoint.Y - _rightToolbarY);
        // 按住即抑制自定义悬浮提示；未过拖拽阈值前不 Capture，保留按钮点击。
        InstantTooltip.SetSuppressed(XamlRoot, true);
    }


    private void Border_RightToolbar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightToolbarPressed || e.Pointer.PointerId != _rightToolbarPointerId)
        {
            return;
        }

        // 子 Button 可能已 Capture；事件靠 handledEventsToo 冒泡到此。
        Point rootPoint = e.GetCurrentPoint(RootGrid).Position;
        if (!_rightToolbarDragging)
        {
            double dx = rootPoint.X - _rightToolbarPressRootPoint.X;
            double dy = rootPoint.Y - _rightToolbarPressRootPoint.Y;
            if ((dx * dx) + (dy * dy) < RightToolbarDragStartDistance * RightToolbarDragStartDistance)
            {
                return;
            }

            BeginRightToolbarDrag(e.Pointer);
        }

        // 跟随指针：左上角 = 指针位置 - 按下时的抓取偏移。
        SetRightToolbarPosition(rootPoint.X - _rightToolbarGrabOffset.X, rootPoint.Y - _rightToolbarGrabOffset.Y, animate: false);
        e.Handled = true;
    }


    private void Border_RightToolbar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightToolbarPressed && !_rightToolbarDragging)
        {
            return;
        }
        if (e.Pointer.PointerId != _rightToolbarPointerId)
        {
            return;
        }

        bool wasDragging = _rightToolbarDragging;
        EndRightToolbarPointer(e.Pointer);
        // 阻止子 Button 在拖拽松手时再走 Click / 打开 Flyout。
        if (wasDragging)
        {
            e.Handled = true;
        }
    }


    private void Border_RightToolbar_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _rightToolbarPointerId)
        {
            return;
        }
        EndRightToolbarPointer(e.Pointer);
    }


    private void Border_RightToolbar_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // 仅当我们自己 Capture 后丢失时才结束；拖拽开始前由 Button 持有 Capture，其 CaptureLost 不归我们管。
        if (_rightToolbarDragging && e.Pointer.PointerId == _rightToolbarPointerId)
        {
            EndRightToolbarPointer(e.Pointer, captureAlreadyLost: true);
        }
    }


    /// <summary>
    /// 进入拖拽：从子 Button 夺过 Capture；保持当前高度不收缩；禁用按钮命中，抑制 Tooltip。
    /// </summary>
    private void BeginRightToolbarDrag(Pointer pointer)
    {
        _rightToolbarDragging = true;
        _rightToolbarSuppressClick = true;
        StopRightToolbarCollapseTimer();
        DismissRightToolbarDragTip(markSeen: true);
        CloseAnyRightToolbarFlyout();
        InstantTooltip.SetSuppressed(XamlRoot, true);
        // 先禁用子按钮命中，再 Capture，避免 Button 继续抢指针 / 松手误点。
        SetRightToolbarButtonsHitTestVisible(false);
        _rightToolbarState = RightToolbarState.Dragging;
        _rightToolbarRevealInteractive = false;
        // 拖动过程中不收缩，保持进入拖拽前的高度（通常为展开）。
        if (GetRightToolbarCurrentHeight() < MeasureRightToolbarExpandedHeight() - 0.5)
        {
            ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate: false);
        }
        try
        {
            Border_RightToolbar.CapturePointer(pointer);
        }
        catch
        {
            // ignore
        }

        AttachRightToolbarRootPointerHandlers();
    }


    /// <summary>拖拽时在 RootGrid 上再挂一层 handledEventsToo，防止丢跟手。</summary>
    private void AttachRightToolbarRootPointerHandlers()
    {
        if (_rightToolbarRootHandlersAttached)
        {
            return;
        }

        _rightToolbarRootMovedHandler ??= RootGrid_RightToolbarDrag_PointerMoved;
        _rightToolbarRootReleasedHandler ??= RootGrid_RightToolbarDrag_PointerReleased;
        RootGrid.AddHandler(UIElement.PointerMovedEvent, _rightToolbarRootMovedHandler, handledEventsToo: true);
        RootGrid.AddHandler(UIElement.PointerReleasedEvent, _rightToolbarRootReleasedHandler, handledEventsToo: true);
        _rightToolbarRootHandlersAttached = true;
    }


    private void DetachRightToolbarRootPointerHandlers()
    {
        if (!_rightToolbarRootHandlersAttached)
        {
            return;
        }

        if (_rightToolbarRootMovedHandler is not null)
        {
            RootGrid.RemoveHandler(UIElement.PointerMovedEvent, _rightToolbarRootMovedHandler);
        }
        if (_rightToolbarRootReleasedHandler is not null)
        {
            RootGrid.RemoveHandler(UIElement.PointerReleasedEvent, _rightToolbarRootReleasedHandler);
        }
        _rightToolbarRootHandlersAttached = false;
    }


    private void RootGrid_RightToolbarDrag_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightToolbarDragging || e.Pointer.PointerId != _rightToolbarPointerId)
        {
            return;
        }

        Point rootPoint = e.GetCurrentPoint(RootGrid).Position;
        SetRightToolbarPosition(rootPoint.X - _rightToolbarGrabOffset.X, rootPoint.Y - _rightToolbarGrabOffset.Y, animate: false);
        e.Handled = true;
    }


    private void RootGrid_RightToolbarDrag_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if ((!_rightToolbarPressed && !_rightToolbarDragging) || e.Pointer.PointerId != _rightToolbarPointerId)
        {
            return;
        }
        EndRightToolbarPointer(e.Pointer);
    }


    /// <summary>
    /// 结束按住 / 拖拽：松手时若工具栏已越出窗口边界则贴边收纳，否则在落点展开；
    /// 拖拽松手不触发按钮功能。
    /// </summary>
    private void EndRightToolbarPointer(Pointer pointer, bool captureAlreadyLost = false)
    {
        if (!_rightToolbarPressed && !_rightToolbarDragging)
        {
            return;
        }

        bool wasDragging = _rightToolbarDragging;
        _rightToolbarPressed = false;
        _rightToolbarDragging = false;

        DetachRightToolbarRootPointerHandlers();

        if (!captureAlreadyLost)
        {
            try
            {
                Border_RightToolbar.ReleasePointerCapture(pointer);
            }
            catch
            {
                // ignore
            }
        }

        if (!wasDragging)
        {
            InstantTooltip.SetSuppressed(XamlRoot, false);
            _rightToolbarSuppressClick = false;
            // 纯点击：恢复按钮命中，保持当前展开/浮出态。
            ApplyRightToolbarInteractionGate();
            return;
        }

        // 拖拽松手：始终先关掉可能误开的 Flyout，并保持按钮不可点直到本帧事件结束。
        CloseAnyRightToolbarFlyout();
        SetRightToolbarButtonsHitTestVisible(false);
        InstantTooltip.SetSuppressed(XamlRoot, true);

        double width = MeasureRightToolbarWidth();
        double height = MeasureRightToolbarExpandedHeight();
        RightToolbarDockEdge edge = DetectRightToolbarDockEdge(_rightToolbarX, _rightToolbarY, width, height);
        if (edge is not RightToolbarDockEdge.None)
        {
            _rightToolbarDockEdge = edge;
            // 松手时指针多半还在工具栏上，禁止立刻浮出，需先离开再进入。
            _rightToolbarDockAwaitPointerLeave = true;
            _rightToolbarPointerOver = false;
            TransitionRightToolbar(RightToolbarState.Docked, animate: true);
            SaveRightToolbarLayout();
        }
        else
        {
            _rightToolbarDockEdge = RightToolbarDockEdge.None;
            _rightToolbarDockAwaitPointerLeave = false;
            ClampRightToolbarIntoSoftBounds();
            SetRightToolbarPosition(_rightToolbarX, _rightToolbarY, animate: false);
            TransitionRightToolbar(RightToolbarState.Expanded, animate: true);
            // Expanded 的 Transition 会打开按钮命中——拖拽后需再关一次，延后到 Released 冒泡结束再恢复。
            SetRightToolbarButtonsHitTestVisible(false);
            InstantTooltip.SetSuppressed(XamlRoot, true);
            SaveRightToolbarLayout();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_rightToolbarState is not RightToolbarState.Expanded || _rightToolbarDragging)
                {
                    return;
                }

                CloseAnyRightToolbarFlyout();
                _rightToolbarSuppressClick = false;
                InstantTooltip.SetSuppressed(XamlRoot, false);
                ApplyRightToolbarInteractionGate();
                if (!_rightToolbarPointerOver)
                {
                    ScheduleRightToolbarIdleCollapse();
                }
            });
            return;
        }

        // 贴边：再关一次 Flyout（松手同步触发的打开）。
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            CloseAnyRightToolbarFlyout();
            _rightToolbarSuppressClick = false;
        });
    }


    #endregion


    #region State transitions


    /// <summary>
    /// 切换工具栏状态并应用高度、位置与交互门禁。
    /// </summary>
    private void TransitionRightToolbar(RightToolbarState state, bool animate)
    {
        _rightToolbarState = state;

        switch (state)
        {
            case RightToolbarState.Collapsed:
                _rightToolbarRevealInteractive = true;
                _rightToolbarDockEdge = RightToolbarDockEdge.None;
                ApplyRightToolbarHeight(MeasureRightToolbarCollapsedHeight(), animate);
                SetRightToolbarButtonsHitTestVisible(true);
                InstantTooltip.SetSuppressed(XamlRoot, false);
                break;

            case RightToolbarState.Expanded:
                _rightToolbarRevealInteractive = true;
                ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate);
                SetRightToolbarButtonsHitTestVisible(true);
                InstantTooltip.SetSuppressed(XamlRoot, false);
                break;

            case RightToolbarState.Dragging:
                _rightToolbarRevealInteractive = false;
                // 拖动保持展开高度，不收缩。
                ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate);
                SetRightToolbarButtonsHitTestVisible(false);
                InstantTooltip.SetSuppressed(XamlRoot, true);
                break;

            case RightToolbarState.Docked:
                _rightToolbarRevealInteractive = false;
                ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate);
                SetRightToolbarButtonsHitTestVisible(false);
                // 贴边只关本条按钮命中；不要保持窗口级抑制，
                // 否则下侧工具栏 / 导航栏的 InstantTooltip 也会一起消失。
                InstantTooltip.SetSuppressed(XamlRoot, false);
                ApplyRightToolbarDockedPosition(animate);
                break;

            case RightToolbarState.DockRevealed:
                _rightToolbarRevealInteractive = false;
                ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate);
                SetRightToolbarButtonsHitTestVisible(false);
                Point revealed = GetRightToolbarRevealedPosition(_rightToolbarDockEdge);
                SetRightToolbarPosition(revealed.X, revealed.Y, animate, onCompleted: () =>
                {
                    if (_rightToolbarState is RightToolbarState.DockRevealed)
                    {
                        _rightToolbarRevealInteractive = true;
                        SetRightToolbarButtonsHitTestVisible(true);
                    }
                });
                if (!animate)
                {
                    _rightToolbarRevealInteractive = true;
                    SetRightToolbarButtonsHitTestVisible(true);
                }
                break;
        }

        UpdateRightToolbarPopupSide();
    }


    /// <summary>
    /// 按当前状态刷新高度（子按钮异步显隐后调用）。
    /// </summary>
    private void SyncRightToolbarHeightForState(bool animate)
    {
        switch (_rightToolbarState)
        {
            case RightToolbarState.Collapsed:
                ApplyRightToolbarHeight(MeasureRightToolbarCollapsedHeight(), animate);
                break;
            case RightToolbarState.Dragging:
            case RightToolbarState.Expanded:
            case RightToolbarState.Docked:
            case RightToolbarState.DockRevealed:
                ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate);
                break;
        }
    }


    /// <summary>
    /// 空闲 5 秒后：贴边浮出 → 回到贴边；自由展开 → 单按钮收起。
    /// </summary>
    private void ScheduleRightToolbarIdleCollapse()
    {
        StopRightToolbarCollapseTimer();
        if (_rightToolbarPointerOver
            || _rightToolbarDragging
            || _rightToolbarPressed
            || TeachingTip_RightToolbarDrag.IsOpen
            || IsAnyRightToolbarFlyoutOpen())
        {
            return;
        }

        if (_rightToolbarState is RightToolbarState.Collapsed or RightToolbarState.Docked or RightToolbarState.Dragging)
        {
            return;
        }

        _rightToolbarCollapseTimer.Interval = RightToolbarCollapseDelay;
        _rightToolbarCollapseTimer.Start();
    }


    private void StopRightToolbarCollapseTimer()
    {
        _rightToolbarCollapseTimer.Stop();
    }


    /// <summary>
    /// 首次进入且功能条在自由态时展开并弹出拖拽引导；已看过或已贴边则跳过。
    /// </summary>
    private void TryShowRightToolbarDragTip()
    {
        if (!_rightToolbarInitialized || AppConfig.HasSeenRightToolbarDragHint)
        {
            return;
        }
        if (_rightToolbarState is RightToolbarState.Docked or RightToolbarState.DockRevealed)
        {
            return;
        }
        if (_rightToolbarState is RightToolbarState.Collapsed)
        {
            TransitionRightToolbar(RightToolbarState.Expanded, animate: false);
        }
        StopRightToolbarCollapseTimer();
        InstantTooltip.SetSuppressed(XamlRoot, true);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_rightToolbarInitialized || AppConfig.HasSeenRightToolbarDragHint)
            {
                return;
            }
            if (Border_RightToolbar.ActualHeight <= 0)
            {
                return;
            }
            TeachingTip_RightToolbarDrag.IsOpen = true;
        });
    }


    /// <summary>
    /// 关掉拖拽引导。<paramref name="markSeen"/> 为 true 时写入设置，卸载离开时传 false 以便下次再出。
    /// </summary>
    /// <param name="markSeen">是否记为已看过。</param>
    private void DismissRightToolbarDragTip(bool markSeen)
    {
        if (!TeachingTip_RightToolbarDrag.IsOpen)
        {
            if (markSeen)
            {
                AppConfig.HasSeenRightToolbarDragHint = true;
            }
            return;
        }
        _rightToolbarDragTipMarkSeenOnClose = markSeen;
        TeachingTip_RightToolbarDrag.IsOpen = false;
    }


    /// <summary>
    /// TeachingTip 关闭后：按需记「已看过」，恢复 InstantTooltip，并允许空闲收起。
    /// </summary>
    private void TeachingTip_RightToolbarDrag_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        if (_rightToolbarDragTipMarkSeenOnClose)
        {
            AppConfig.HasSeenRightToolbarDragHint = true;
        }
        _rightToolbarDragTipMarkSeenOnClose = true;
        if (!_rightToolbarDragging && !_rightToolbarPressed)
        {
            InstantTooltip.SetSuppressed(XamlRoot, false);
        }
        ScheduleRightToolbarIdleCollapse();
    }


    private void RightToolbarCollapseTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_rightToolbarPointerOver
            || _rightToolbarDragging
            || _rightToolbarPressed
            || TeachingTip_RightToolbarDrag.IsOpen
            || IsAnyRightToolbarFlyoutOpen())
        {
            return;
        }

        if (_rightToolbarState is RightToolbarState.DockRevealed)
        {
            TransitionRightToolbar(RightToolbarState.Docked, animate: true);
            SaveRightToolbarLayout();
        }
        else if (_rightToolbarState is RightToolbarState.Expanded)
        {
            TransitionRightToolbar(RightToolbarState.Collapsed, animate: true);
            SaveRightToolbarLayout();
        }
    }


    private void ApplyRightToolbarInteractionGate()
    {
        bool interactive = _rightToolbarState switch
        {
            RightToolbarState.Collapsed => true,
            RightToolbarState.Expanded => true,
            RightToolbarState.DockRevealed => _rightToolbarRevealInteractive,
            _ => false,
        };
        SetRightToolbarButtonsHitTestVisible(interactive);
        // 仅拖拽时窗口级抑制 Tooltip（指针会划过下侧工具栏 / 导航）。
        // 贴边不可点靠 IsHitTestVisible=false 即可，不能用 SetSuppressed。
        if (_rightToolbarState is not RightToolbarState.Dragging)
        {
            InstantTooltip.SetSuppressed(XamlRoot, false);
        }
    }


    private void SetRightToolbarButtonsHitTestVisible(bool visible)
    {
        StackPanel_RightToolbar.IsHitTestVisible = visible;
    }


    #endregion


    #region Position / Dock


    /// <summary>默认放在窗口右上（对齐原 Margin 右 12、上 48）。</summary>
    private void PlaceRightToolbarDefaultPosition()
    {
        double width = MeasureRightToolbarWidth();
        double x = Math.Max(0, RootGrid.ActualWidth - RightToolbarDefaultMarginRight - width);
        double y = RightToolbarDefaultMarginTop;
        SetRightToolbarPosition(x, y, animate: false);
    }


    /// <summary>
    /// 从设置恢复工具栏位置与贴边状态；无记录时使用默认右上角收起。
    /// 布局串：<c>docked|left|y</c> / <c>docked|right|y</c> / <c>free|x|y</c>
    /// </summary>
    private void RestoreRightToolbarLayout()
    {
        string? raw = AppConfig.GameLauncherRightToolbarLayout;
        if (string.IsNullOrWhiteSpace(raw))
        {
            PlaceRightToolbarDefaultPosition();
            TransitionRightToolbar(RightToolbarState.Collapsed, animate: false);
            return;
        }

        try
        {
            string[] parts = raw.Split('|');
            if (parts.Length >= 3
                && parts[0].Equals("docked", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(parts[1], ignoreCase: true, out RightToolbarDockEdge edge)
                && edge is RightToolbarDockEdge.Left or RightToolbarDockEdge.Right
                && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dockY))
            {
                _rightToolbarDockEdge = edge;
                _rightToolbarY = dockY;
                _rightToolbarDockAwaitPointerLeave = false;
                ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate: false);
                ApplyRightToolbarDockedPosition(animate: false);
                TransitionRightToolbar(RightToolbarState.Docked, animate: false);
                return;
            }

            if (parts.Length >= 3
                && parts[0].Equals("free", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double freeX)
                && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double freeY))
            {
                _rightToolbarDockEdge = RightToolbarDockEdge.None;
                SetRightToolbarPosition(freeX, freeY, animate: false);
                ClampRightToolbarIntoSoftBounds();
                SetRightToolbarPosition(_rightToolbarX, _rightToolbarY, animate: false);
                TransitionRightToolbar(RightToolbarState.Collapsed, animate: false);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore right toolbar layout");
        }

        PlaceRightToolbarDefaultPosition();
        TransitionRightToolbar(RightToolbarState.Collapsed, animate: false);
    }


    /// <summary>
    /// 持久化工具栏自由位置或左右贴边状态（Y 坐标）。
    /// </summary>
    private void SaveRightToolbarLayout()
    {
        try
        {
            if (_rightToolbarState is RightToolbarState.Docked or RightToolbarState.DockRevealed
                && _rightToolbarDockEdge is RightToolbarDockEdge.Left or RightToolbarDockEdge.Right)
            {
                string edge = _rightToolbarDockEdge is RightToolbarDockEdge.Left ? "left" : "right";
                AppConfig.GameLauncherRightToolbarLayout =
                    $"docked|{edge}|{_rightToolbarY.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                return;
            }

            if (_rightToolbarState is RightToolbarState.Dragging)
            {
                return;
            }

            AppConfig.GameLauncherRightToolbarLayout =
                $"free|{_rightToolbarX.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{_rightToolbarY.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save right toolbar layout");
        }
    }


    private void SetRightToolbarPosition(double x, double y, bool animate, Action? onCompleted = null)
    {
        _rightToolbarX = x;
        _rightToolbarY = y;

        _rightToolbarMoveStoryboard?.Stop();
        _rightToolbarMoveStoryboard = null;

        void Finish()
        {
            UpdateRightToolbarPopupSide();
            onCompleted?.Invoke();
        }

        if (!animate || !EntranceAnimation.AnimationsEnabled())
        {
            Transform_RightToolbar.X = x;
            Transform_RightToolbar.Y = y;
            Finish();
            return;
        }

        double fromX = Transform_RightToolbar.X;
        double fromY = Transform_RightToolbar.Y;

        var animX = new DoubleAnimation
        {
            From = fromX,
            To = x,
            Duration = RightToolbarAnimDuration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var animY = new DoubleAnimation
        {
            From = fromY,
            To = y,
            Duration = RightToolbarAnimDuration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animX, Transform_RightToolbar);
        Storyboard.SetTargetProperty(animX, "X");
        Storyboard.SetTarget(animY, Transform_RightToolbar);
        Storyboard.SetTargetProperty(animY, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animX);
        storyboard.Children.Add(animY);
        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(_rightToolbarMoveStoryboard, storyboard))
            {
                Transform_RightToolbar.X = x;
                Transform_RightToolbar.Y = y;
                _rightToolbarMoveStoryboard = null;
                Finish();
            }
        };
        _rightToolbarMoveStoryboard = storyboard;
        storyboard.Begin();
    }


    /// <summary>
    /// 工具栏在窗口左侧时，Flyout / Tooltip 向右弹出；在右侧时向左弹出（左右贴边对称）。
    /// </summary>
    private void UpdateRightToolbarPopupSide()
    {
        bool onLeft = IsRightToolbarOnLeftSide();
        FlyoutPlacementMode flyoutPlacement = onLeft
            ? FlyoutPlacementMode.RightEdgeAlignedTop
            : FlyoutPlacementMode.LeftEdgeAlignedTop;
        InstantTooltipPlacement tipPlacement = onLeft
            ? InstantTooltipPlacement.Right
            : InstantTooltipPlacement.Left;

        foreach (UIElement child in StackPanel_RightToolbar.Children)
        {
            if (child is not FrameworkElement fe)
            {
                continue;
            }

            Button? button = fe as Button ?? fe.FindDescendant<Button>();
            if (button is null)
            {
                continue;
            }

            if (button.Flyout is Flyout flyout)
            {
                flyout.Placement = flyoutPlacement;
            }

            // 图标按钮上的即时提示；内部子项（如签到格子）的 Top 提示不改。
            if (!string.IsNullOrEmpty(InstantTooltip.GetText(button)))
            {
                InstantTooltip.SetPlacement(button, tipPlacement);
            }
        }
    }


    /// <summary>
    /// 贴边左 / 自由位置中心在窗口左半 → 视为左侧；否则右侧。
    /// </summary>
    private bool IsRightToolbarOnLeftSide()
    {
        if (_rightToolbarState is RightToolbarState.Docked or RightToolbarState.DockRevealed)
        {
            return _rightToolbarDockEdge is RightToolbarDockEdge.Left;
        }

        double rootW = RootGrid.ActualWidth;
        if (rootW <= 0)
        {
            return false;
        }

        double centerX = _rightToolbarX + MeasureRightToolbarWidth() / 2;
        return centerX < rootW / 2;
    }


    /// <summary>
    /// 按贴边方向把工具栏移到「仅漏出 <see cref="RightToolbarDockPeek"/> 宽」的位置。
    /// 始终使用展开尺寸，保证各边露出宽度统一。
    /// </summary>
    private void ApplyRightToolbarDockedPosition(bool animate)
    {
        if (_rightToolbarDockEdge is RightToolbarDockEdge.None)
        {
            return;
        }

        double width = MeasureRightToolbarWidth();
        double height = MeasureRightToolbarExpandedHeight();
        double rootW = RootGrid.ActualWidth;
        double rootH = RootGrid.ActualHeight;
        double x = _rightToolbarX;
        double y = _rightToolbarY;

        switch (_rightToolbarDockEdge)
        {
            case RightToolbarDockEdge.Left:
                x = RightToolbarDockPeek - width;
                y = Clamp(y, 0, Math.Max(0, rootH - height));
                break;
            case RightToolbarDockEdge.Right:
                x = rootW - RightToolbarDockPeek;
                y = Clamp(y, 0, Math.Max(0, rootH - height));
                break;
            case RightToolbarDockEdge.Top:
                x = Clamp(x, 0, Math.Max(0, rootW - width));
                y = RightToolbarDockPeek - height;
                break;
            case RightToolbarDockEdge.Bottom:
                x = Clamp(x, 0, Math.Max(0, rootW - width));
                y = rootH - RightToolbarDockPeek;
                break;
        }

        SetRightToolbarPosition(x, y, animate);
    }


    /// <summary>
    /// 贴边浮出后的完整可见位置（贴在对应边内侧，保留默认边距）。
    /// </summary>
    private Point GetRightToolbarRevealedPosition(RightToolbarDockEdge edge)
    {
        double width = MeasureRightToolbarWidth();
        double height = MeasureRightToolbarExpandedHeight();
        double rootW = RootGrid.ActualWidth;
        double rootH = RootGrid.ActualHeight;
        const double inset = 12;
        double x = _rightToolbarX;
        double y = _rightToolbarY;

        switch (edge)
        {
            case RightToolbarDockEdge.Left:
                x = inset;
                y = Clamp(y, inset, Math.Max(inset, rootH - height - inset));
                break;
            case RightToolbarDockEdge.Right:
                x = rootW - inset - width;
                y = Clamp(y, inset, Math.Max(inset, rootH - height - inset));
                break;
            case RightToolbarDockEdge.Top:
                x = Clamp(x, inset, Math.Max(inset, rootW - width - inset));
                y = inset;
                break;
            case RightToolbarDockEdge.Bottom:
                x = Clamp(x, inset, Math.Max(inset, rootW - width - inset));
                y = rootH - inset - height;
                break;
            default:
                x = Clamp(x, 0, Math.Max(0, rootW - width));
                y = Clamp(y, 0, Math.Max(0, rootH - height));
                break;
        }

        return new Point(x, y);
    }


    /// <summary>
    /// 仅当工具栏已越出窗口左右边界一定距离时才贴边（只支持左/右，不支持上下）。
    /// 内侧贴近不收纳，避免默认靠右就误贴边；取左右越界更深的一侧。
    /// </summary>
    private RightToolbarDockEdge DetectRightToolbarDockEdge(double x, double y, double width, double height)
    {
        double rootW = RootGrid.ActualWidth;
        if (rootW <= 0)
        {
            return RightToolbarDockEdge.None;
        }

        // 仅左右越界深度：>0 表示该侧已伸出窗口外。
        double overflowLeft = Math.Max(0, -x);
        double overflowRight = Math.Max(0, x + width - rootW);

        // 至少伸出这么多才收纳，避免擦边误触。
        const double minOverflow = 8;
        if (overflowLeft < minOverflow && overflowRight < minOverflow)
        {
            return RightToolbarDockEdge.None;
        }

        return overflowLeft >= overflowRight
            ? RightToolbarDockEdge.Left
            : RightToolbarDockEdge.Right;
    }


    /// <summary>
    /// 自由态时把工具栏至少保留一截在窗口内，避免完全拖丢。
    /// </summary>
    private void ClampRightToolbarIntoSoftBounds()
    {
        double width = MeasureRightToolbarWidth();
        double height = Math.Max(GetRightToolbarCurrentHeight(), MeasureRightToolbarCollapsedHeight());
        double rootW = RootGrid.ActualWidth;
        double rootH = RootGrid.ActualHeight;
        const double keep = 24;
        _rightToolbarX = Clamp(_rightToolbarX, keep - width, rootW - keep);
        _rightToolbarY = Clamp(_rightToolbarY, keep - height, rootH - keep);
    }


    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }
        return Math.Min(max, Math.Max(min, value));
    }


    #endregion


    #region Height / Measure


    private void ApplyRightToolbarHeight(double height, bool animate)
    {
        if (height <= 0 || double.IsNaN(height))
        {
            return;
        }

        double current = GetRightToolbarCurrentHeight();
        double width = Math.Max(Border_RightToolbar.ActualWidth, MeasureRightToolbarWidth());
        if (Math.Abs(current - height) < 0.5)
        {
            Border_RightToolbar.Height = height;
            UpdateRightToolbarClip(width, height);
            return;
        }

        _rightToolbarHeightStoryboard?.Stop();
        _rightToolbarHeightStoryboard = null;

        if (!animate || !EntranceAnimation.AnimationsEnabled())
        {
            Border_RightToolbar.Height = height;
            UpdateRightToolbarClip(width, height);
            return;
        }

        Border_RightToolbar.Height = current;
        var animation = new DoubleAnimation
        {
            From = current,
            To = height,
            Duration = RightToolbarAnimDuration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase
            {
                EasingMode = _rightToolbarState is RightToolbarState.Collapsed or RightToolbarState.Dragging
                    ? EasingMode.EaseIn
                    : EasingMode.EaseOut,
            },
        };
        Storyboard.SetTarget(animation, Border_RightToolbar);
        Storyboard.SetTargetProperty(animation, "Height");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(_rightToolbarHeightStoryboard, storyboard))
            {
                Border_RightToolbar.Height = height;
                UpdateRightToolbarClip(Math.Max(Border_RightToolbar.ActualWidth, MeasureRightToolbarWidth()), height);
                _rightToolbarHeightStoryboard = null;
            }
        };
        _rightToolbarHeightStoryboard = storyboard;
        storyboard.Begin();
    }


    private double GetRightToolbarCurrentHeight()
    {
        if (!double.IsNaN(Border_RightToolbar.Height) && Border_RightToolbar.Height > 0)
        {
            return Border_RightToolbar.Height;
        }
        if (Border_RightToolbar.ActualHeight > 0)
        {
            return Border_RightToolbar.ActualHeight;
        }
        return MeasureRightToolbarCollapsedHeight();
    }


    private void UpdateRightToolbarClip(double width, double height)
    {
        if (width <= 0)
        {
            width = MeasureRightToolbarWidth();
        }
        if (height <= 0)
        {
            return;
        }

        // 贴边时用完整矩形，peek 靠位移实现，避免再被 clip 裁成非统一宽度。
        Border_RightToolbar.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, width, height),
        };
    }


    private int CountVisibleRightToolbarButtons()
    {
        int count = 0;
        foreach (UIElement child in StackPanel_RightToolbar.Children)
        {
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                count++;
            }
        }
        return count;
    }


    private double MeasureRightToolbarWidth()
    {
        double padding = Border_RightToolbar.Padding.Left + Border_RightToolbar.Padding.Right;
        if (Border_RightToolbar.ActualWidth > 0)
        {
            return Border_RightToolbar.ActualWidth;
        }
        return padding + RightToolbarButtonSize;
    }


    private double MeasureRightToolbarCollapsedHeight()
    {
        double padding = Border_RightToolbar.Padding.Top + Border_RightToolbar.Padding.Bottom;
        return padding + RightToolbarButtonSize;
    }


    private double MeasureRightToolbarExpandedHeight()
    {
        double padding = Border_RightToolbar.Padding.Top + Border_RightToolbar.Padding.Bottom;
        int visible = CountVisibleRightToolbarButtons();
        if (visible <= 0)
        {
            return padding + RightToolbarButtonSize;
        }

        return padding
               + visible * RightToolbarButtonSize
               + Math.Max(0, visible - 1) * StackPanel_RightToolbar.Spacing;
    }


    #endregion


    #region Flyout hooks


    private bool IsAnyRightToolbarFlyoutOpen()
    {
        foreach (UIElement child in StackPanel_RightToolbar.Children)
        {
            if (child is not FrameworkElement fe || fe.Visibility != Visibility.Visible)
            {
                continue;
            }

            Button? button = fe as Button ?? fe.FindDescendant<Button>();
            if (button?.Flyout is FlyoutBase flyout && flyout.IsOpen)
            {
                return true;
            }
        }
        return false;
    }


    private void CloseAnyRightToolbarFlyout()
    {
        foreach (UIElement child in StackPanel_RightToolbar.Children)
        {
            if (child is not FrameworkElement fe)
            {
                continue;
            }

            Button? button = fe as Button ?? fe.FindDescendant<Button>();
            if (button?.Flyout is FlyoutBase flyout && flyout.IsOpen)
            {
                flyout.Hide();
            }
        }
    }


    private void EnsureRightToolbarFlyoutHooks()
    {
        foreach (UIElement child in StackPanel_RightToolbar.Children)
        {
            if (child is not FrameworkElement fe)
            {
                continue;
            }

            Button? button = fe as Button ?? fe.FindDescendant<Button>();
            if (button?.Flyout is not FlyoutBase flyout || _rightToolbarHookedFlyouts.Contains(flyout))
            {
                continue;
            }

            flyout.Opened += RightToolbarFlyout_Opened;
            flyout.Closed += RightToolbarFlyout_Closed;
            _rightToolbarHookedFlyouts.Add(flyout);
        }
    }


    private void RightToolbarFlyout_Opened(object? sender, object e)
    {
        // 拖拽松手同一帧可能误开 Flyout：立刻关掉。
        if (_rightToolbarSuppressClick || _rightToolbarDragging || _rightToolbarState is RightToolbarState.Dragging or RightToolbarState.Docked)
        {
            if (sender is FlyoutBase flyout)
            {
                flyout.Hide();
            }
            return;
        }

        DismissRightToolbarDragTip(markSeen: true);
        StopRightToolbarCollapseTimer();
        if (_rightToolbarState is RightToolbarState.Collapsed)
        {
            TransitionRightToolbar(RightToolbarState.Expanded, animate: true);
        }
        else if (_rightToolbarState is RightToolbarState.Docked)
        {
            // 贴边等待离开期间不允许借 Flyout 浮出。
            if (_rightToolbarDockAwaitPointerLeave)
            {
                if (sender is FlyoutBase flyout)
                {
                    flyout.Hide();
                }
                return;
            }
            TransitionRightToolbar(RightToolbarState.DockRevealed, animate: true);
        }
    }


    private void RightToolbarFlyout_Closed(object? sender, object e)
    {
        ScheduleRightToolbarIdleCollapse();
    }


    #endregion


    #endregion


}
