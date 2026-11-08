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
using Starward.RPC;
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

    /// <summary>右侧功能工具栏贴边浮出后自动回贴边的延迟。</summary>
    private static readonly TimeSpan RightToolbarCollapseDelay = TimeSpan.FromSeconds(5);

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _rightToolbarCollapseTimer;

    private readonly HashSet<FlyoutBase> _rightToolbarHookedFlyouts = new();

    private bool _rightToolbarInitialized;

    /// <summary>已按窗口尺寸恢复位置并显示；此前 Opacity=0，避免 Transform 原点在左上时首帧闪一下。</summary>
    private bool _rightToolbarLayoutRevealed;


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
        // 游戏时长卡片显隐由 UpdatePlayTimeCardVisual 驱动（隐藏要先播完卷起动画再收起），
        // 这里按设置直接落初始状态、不播动画。
        Button_PlayTime.Visibility = EnablePlayTime ? Visibility.Visible : Visibility.Collapsed;
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
        WeakReferenceMessenger.Default.Register<GameStartedMessage>(this, OnGameStarted);
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
        // 运行中的 System.Timers.Timer 由计时器队列持有，Elapsed 闭包又捕获了本页，
        // 不停表则切游戏后旧页会被长期 root；游戏退出检测另由 RunningGameService 负责，与本表无关。
        processTimer?.Stop();
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
    [NotifyPropertyChangedFor(nameof(IsDX12OptionVisible))]
    public partial GameState GameState { get; set; }

    /// <summary>
    /// 按钮状态变化时记录埋点（只在值真正改变时触发，进度定时器反复赋同值不会刷屏）
    /// </summary>
    /// <param name="value">新的按钮状态</param>
    partial void OnGameStateChanged(GameState value)
    {
        Telemetry.Track("launcher_button_state", CurrentGameBiz, ("state", value));
    }




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
    /// 当前显卡可切换 DX12，或者已经勾选过
    /// </summary>
    private bool IsDX12OptionAvailable { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(IsDX12OptionVisible)); } }


    /// <summary>
    /// 是否显示 DX12 选项。只在游戏已安装时显示，未安装时同一位置要显示定位游戏等提示
    /// </summary>
    public bool IsDX12OptionVisible => IsDX12OptionAvailable && GameState is GameState.StartGame or GameState.GameIsRunning or GameState.UpdateGame;


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
            // DX12 选项与版本无关，不等下面的版本检查：冷启动时要等好几个请求，请求失败或需要更新时还会一直不显示
            _ = CheckDX12ConfigAsync();
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
            // 先按上次的接口结果显示，接口慢或请求失败时选项也不会缺席
            IsDX12OptionAvailable = EnableDX12 || AppConfig.GetDX12SwitchAvailable(CurrentGameBiz);

            List<GameDXConfig> dxConfigs = await _hoYoPlayService.GetGameDXConfigsAsync([CurrentGameId]);
            _dxConfig = dxConfigs.FirstOrDefault(x => x.GameId == CurrentGameId);

            // 是否可切换取决于显卡，换了不支持的显卡时要能收起
            bool available = _dxConfig?.EnableDXSwitch is true;
            AppConfig.SetDX12SwitchAvailable(CurrentGameBiz, available);
            IsDX12OptionAvailable = EnableDX12 || available;
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
        try
        {
            // 选项可能是按本地记录显示的，此时配置还没取到或上次请求失败了，点击时再取一次
            if (_dxConfig is null)
            {
                await CheckDX12ConfigAsync();
            }
            if (_dxConfig is not null)
            {
                await new DX12IntroDialog { GameDXConfig = _dxConfig, XamlRoot = this.XamlRoot }.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show DX12 intro");
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
        if (_gameInstallTask?.State is GameInstallState.Stop or GameInstallState.Finish)
        {
            // 卸载前任务已被停止，但出错的任务计时器早已停下，不会自己清掉；
            // 留着的话点安装会走「继续旧任务」，跳过安装对话框直接往旧路径下载
            _dispatchTimer.Stop();
            _gameInstallTask = null;
        }
        CheckGameVersion();
    }




    /// <summary>
    /// 主窗口激活时刷新页面状态。版本检查保留 10 分钟 / 跨整点的冷却，但「游戏是否在运行」每次激活都补检一次：
    /// 隐藏到托盘期间游戏可能由快捷方式（另一个短命进程）拉起，本页不会拿到进程，
    /// 否则要等冷却过去才显示计时。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="message">主窗口状态变化消息。</param>
    private async void OnMainWindowStateChanged(object _, MainWindowStateChangedMessage message)
    {
        try
        {
            if (!message.Activate)
            {
                return;
            }
            if (message.ElapsedOver(TimeSpan.FromMinutes(10)) || message.IsCrossingHour)
            {
                CheckGameVersion();
            }
            else if (GameState is GameState.StartGame && GameProcess is null)
            {
                await CheckGameRunningAsync();
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

    /// <summary><see cref="CheckGameExited"/> 的累计次数，用来决定哪一次顺带读线程快照。</summary>
    private int _gameExitCheckCount;


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




    /// <summary>
    /// 响应「游戏已启动」通知。快捷方式启动的游戏由独立进程拉起，本页的 <see cref="GameProcess"/> 不会被填上，
    /// 需按通知补检一次，否则按钮会停在「开始游戏」而不显示计时。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="message">通知消息，含被启动游戏的区服。</param>
    private async void OnGameStarted(object _, GameStartedMessage message)
    {
        try
        {
            // 只关心当前页面对应的游戏；已经在计时中则无需重复检测
            if (message.GameBiz != CurrentGameBiz || GameProcess is { HasExited: false })
            {
                return;
            }
            await CheckGameRunningAsync();
        }
        catch { }
    }



    /// <summary>
    /// 每秒检查当前游戏进程是否还在运行。已退出或只剩挂起残留时，把按钮从「游戏正在运行」收回来。
    /// 退出码每秒查，线程快照每 <see cref="GameLauncherService.ProcessThreadCheckInterval"/> 秒查一次。
    /// </summary>
    private void CheckGameExited()
    {
        Process? process = GameProcess;
        if (process is null)
        {
            return;
        }

        // System.Timers.Timer 的回调可能重叠，计数用 Interlocked。IsProcessEnded 自己吞掉所有异常
        bool checkThreads = System.Threading.Interlocked.Increment(ref _gameExitCheckCount) % GameLauncherService.ProcessThreadCheckInterval == 0;
        if (GameLauncherService.IsProcessEnded(process, checkThreads))
        {
            // 计时器在线程池上。RunningGameInfo / RunningGameTime 绑着 x:Bind，必须回 UI 线程再清。
            DispatcherQueue?.TryEnqueue(() => ClearGameProcess(process));
            return;
        }

        TimeSpan? elapsed = null;
        try
        {
            elapsed = DateTime.Now - process.StartTime;
        }
        catch { }

        if (elapsed is TimeSpan time)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (ReferenceEquals(GameProcess, process))
                {
                    RunningGameTime = TimeSpanToString(time);
                }
            });
        }
    }


    /// <summary>
    /// 结束这一轮「游戏正在运行」。调用方必须在 UI 线程上执行。
    /// </summary>
    /// <param name="process">发起这次检查时的进程。期间若已换成另一个进程则不动。</param>
    private void ClearGameProcess(Process process)
    {
        if (!ReferenceEquals(GameProcess, process))
        {
            return;
        }
        RunningGameTime = null;
        GameProcess = null;
        CheckGameVersion();
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
                WeakReferenceMessenger.Default.Send(new GameStartedMessage(CurrentGameBiz));
            }
        }
        catch (FileNotFoundException)
        {
            CheckGameVersion();
        }
        catch (GameRunningException ex)
        {
            // 可预期：按钮状态没来得及刷新时又点了一次，或同名 exe 的另一个区服还开着，不留堆栈
            _logger.LogInformation("Start game: {message}", ex.Message);
            if (!await CheckGameRunningAsync())
            {
                // 运行中的是另一个区服，本页按钮不会切到「运行中」，不提示的话点了没有任何反应
                InAppToast.MainWindow?.Warning(null, Lang.LauncherPage_GameIsRunning);
            }
        }
        catch (ThirdPartyToolUrlNotRegisteredException ex)
        {
            // 可预期：自定义启动链接的程序没装（或已卸载），游戏不启动，提示去换启动程序
            _logger.LogWarning("Start game: {message}", ex.Message);
            InAppToast.MainWindow?.Warning(null, string.Format(Lang.LauncherPage_ThirdPartyToolUrlNotRegistered, ex.Url));
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
                TrackDownloadClick("install");
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
            TrackDownloadClick("resume_download");
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
                TrackDownloadClick("predownload");
                await new PreDownloadDialog { CurrentGameId = this.CurrentGameId, XamlRoot = this.XamlRoot }.ShowAsync();
            }
            else if (_gameInstallTask.Operation is GameInstallOperation.Predownload)
            {
                if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Paused or GameInstallState.Error or GameInstallState.Queueing)
                {
                    TrackDownloadClick("predownload_continue");
                    await _gameInstallService.ContinueTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else if (_gameInstallTask.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying)
                {
                    TrackDownloadClick("predownload_pause");
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
                TrackDownloadClick("update");
                List<OtherServerUpdate> linkedServers = await GetHardLinkedServersToUpdateAsync();
                if (linkedServers.Count > 0)
                {
                    if (AppConfig.UpdateHardLinkedGamesTogether)
                    {
                        // 已设置直接一并更新：正在运行的区服更新必然失败，跳过
                        linkedServers = linkedServers.Where(x => !x.IsGameRunning).ToList();
                        Telemetry.Track("update_linked_auto", CurrentGameBiz, ("servers", linkedServers.Select(x => x.GameId.GameBiz.Value).ToList()));
                    }
                    else
                    {
                        var dialog = new UpdateOtherServersDialog { CurrentGameBiz = CurrentGameBiz, Servers = linkedServers, XamlRoot = this.XamlRoot };
                        await dialog.ShowAsync();
                        if (!dialog.Confirmed)
                        {
                            return;
                        }
                        linkedServers = dialog.SelectedServers;
                    }
                }
                // RPC 同一时间只执行一个任务，其余按提交顺序排队：本体先于当前区服提交，通过硬链接产生的区服排在当前区服之后
                await StartLinkedServerUpdatesAsync(linkedServers.Where(x => x.IsHardLinkSource));
                AudioLanguage audio = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId, GameInstallPath);
                GameInstallContext? task = await _gameInstallService.StartUpdateAsync(CurrentGameId, GameInstallPath!, audio);
                if (task is not null)
                {
                    _gameInstallTask = task;
                    _dispatchTimer.Start();
                    await StartLinkedServerUpdatesAsync(linkedServers.Where(x => !x.IsHardLinkSource));
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



    /// <summary>
    /// 查找与当前区服硬链接、也可以一并更新的其他区服。查找失败不影响更新当前区服，按没有处理。
    /// </summary>
    /// <returns>可一并更新的区服，本体在前</returns>
    private async Task<List<OtherServerUpdate>> GetHardLinkedServersToUpdateAsync()
    {
        try
        {
            return await _gameInstallService.GetHardLinkedServersToUpdateAsync(CurrentGameId, GameInstallPath!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get hard linked servers to update {GameBiz}", CurrentGameBiz);
            return [];
        }
    }



    /// <summary>
    /// 为要一并更新的区服提交更新任务。单个区服提交失败只记日志，不影响其余区服。
    /// </summary>
    /// <param name="servers">要一并更新的区服</param>
    private async Task StartLinkedServerUpdatesAsync(IEnumerable<OtherServerUpdate> servers)
    {
        foreach (OtherServerUpdate server in servers)
        {
            try
            {
                AudioLanguage audio = await _gamePackageService.GetAudioLanguageAsync(server.GameId, server.InstallPath);
                await _gameInstallService.StartUpdateAsync(server.GameId, server.InstallPath, audio);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update hard linked server {GameBiz} together with {CurrentGameBiz}", server.GameId.GameBiz, CurrentGameBiz);
            }
        }
    }



    #endregion



    #region Game Install Task




    private GameInstallContext? _gameInstallTask;



    /// <summary>
    /// 记录首页下载相关按钮的点击，附带点击瞬间的任务操作与状态
    /// </summary>
    /// <param name="button">按钮：install / resume_download / update / continue / pause / predownload / predownload_continue / predownload_pause</param>
    private void TrackDownloadClick(string button)
    {
        Telemetry.Track("download_click", CurrentGameBiz, ("button", button), ("task_op", _gameInstallTask?.Operation), ("task_state", _gameInstallTask?.State));
    }



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
                    TrackDownloadClick("continue");
                    await _gameInstallService.ContinueTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else if (_gameInstallTask.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying)
                {
                    TrackDownloadClick("pause");
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
            IsWPFPackageRepairing = false;
            _dispatchTimer.Stop();
            return;
        }
        try
        {
            if (_gameInstallTask.Operation is GameInstallOperation.RepairWPFPackage)
            {
                UpdateWPFPackageRepairProgress(_gameInstallTask);
                if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Finish or GameInstallState.Error)
                {
                    // 结果提示与出错后停止任务都由 GameInstallService 负责，这里只收起进度
                    _dispatchTimer.Stop();
                    _gameInstallTask = null;
                }
                return;
            }
            // 同一游戏的千星沙箱修复被其他任务取代时，任务对象会原地变成新的操作
            IsWPFPackageRepairing = false;
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



    /// <summary>
    /// 千星沙箱是否正在后台修复，是则在开始游戏按钮旁显示进度
    /// </summary>
    public bool IsWPFPackageRepairing { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 千星沙箱修复进度：百分比，或等待中、排队中等状态
    /// </summary>
    public string? WPFPackageRepairText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 千星沙箱修复进度的悬停提示：千星沙箱 · 当前阶段
    /// </summary>
    public string? WPFPackageRepairTooltip { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 千星沙箱的官方图标，取不到时为 <see langword="null"/>，改用拼图图标
    /// </summary>
    public string? WPFPackageIconUrl { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasNoWPFPackageIcon)); } }

    public bool HasNoWPFPackageIcon => string.IsNullOrWhiteSpace(WPFPackageIconUrl);

    private bool _wpfPackageIconRequested;



    /// <summary>
    /// 刷新开始游戏按钮旁的千星沙箱修复进度。
    /// 与官方启动器一致，千星沙箱在后台修复，开始游戏按钮保持原状态，修复期间也能启动游戏。
    /// </summary>
    /// <param name="task">千星沙箱修复任务</param>
    private void UpdateWPFPackageRepairProgress(GameInstallContext task)
    {
        IsWPFPackageRepairing = task.State is not GameInstallState.Stop and not GameInstallState.Finish and not GameInstallState.Error;
        string stage = task.State switch
        {
            GameInstallState.Verifying => Lang.DownloadGamePage_Verifying,
            GameInstallState.Downloading => Lang.DownloadGamePage_Downloading,
            GameInstallState.Decompressing => Lang.DownloadGamePage_Decompressing,
            GameInstallState.Queueing => Lang.StartGameButton_InQueue,
            GameInstallState.Paused => Lang.DownloadGamePage_Paused,
            _ => Lang.StartGameButton_Waiting,
        };
        double? progress = task.State switch
        {
            GameInstallState.Verifying or GameInstallState.Decompressing => task.Progress_Percent,
            GameInstallState.Downloading => task.Progress_DownloadTotalBytes > 0 ? (double)task.Progress_DownloadFinishBytes / task.Progress_DownloadTotalBytes : 0,
            _ => null,
        };
        WPFPackageRepairText = progress is double value ? $"{value:P1}" : stage;
        WPFPackageRepairTooltip = $"{Lang.GameLauncherSettingDialog_MiliastraSandbox} · {stage}";
        if (!_wpfPackageIconRequested)
        {
            _wpfPackageIconRequested = true;
            _ = LoadWPFPackageIconAsync();
        }
    }



    /// <summary>
    /// 读取千星沙箱的官方图标（getGames 的 wpf_icon），只在第一次显示修复进度时读取
    /// </summary>
    /// <returns></returns>
    private async Task LoadWPFPackageIconAsync()
    {
        try
        {
            string? url = (await _hoYoPlayService.GetGameInfoAsync(CurrentGameId))?.Display?.WpfIcon?.Url;
            // 没有图标时 url 是空字符串，统一成 null，图片与拼图图标才不会同时显示
            WPFPackageIconUrl = string.IsNullOrWhiteSpace(url) ? null : url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get WPF icon ({biz})", CurrentGameBiz);
        }
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
        await GameLauncherSettingDialog.OpenAsync(CurrentGameId, this.XamlRoot);
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
        UpdatePlayTimeCardVisual(animate: true);
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

        // 拨钮行进方向：开→右(+1)、关→左(-1)。行程 7px 在 16px 图标上足够可感知。
        float dir = shown ? 1f : -1f;
        const float slide = 7f;
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


    /// <summary>
    /// 游戏时长卡片（右下角 ⏱ 胶囊）的揭示裁剪。仅在揭示/卷起动画期间挂到卡片 Visual 上
    /// （挂着 = 正在播，见 <see cref="UpdatePlayTimeCardVisual"/>）。
    /// 上、右、下三边固定外扩 <see cref="PlayTimeCardShadowExtent"/>，左边在 卡片宽 ↔ -外扩 之间动画：
    /// 贴边的裁剪会把 ThemeShadow 溢出边界外的投影裁掉，等动画播完卸下裁剪时投影才突然冒出来。
    /// </summary>
    private InsetClip? _playTimeCardClip;

    /// <summary>
    /// 卡片 ThemeShadow（Translation Z=16）溢出卡片边界的距离上限（DIP）。揭示裁剪四周外扩这么多，
    /// 完全展开时投影已整个露出，卸下裁剪不会再有跳变。实测投影只溢出约 6 DIP（左、下侧，上方几乎没有），
    /// 留一倍余量；外扩越大，揭示/卷起在左侧空白里走的路越长，内容动画的节奏就偏离原曲线越多。
    /// </summary>
    private const float PlayTimeCardShadowExtent = 16f;

    /// <summary>
    /// 显隐操作的代数。延迟揭示和动画完成回调只认发起那一次，连点时旧回调直接作废。
    /// </summary>
    private int _playTimeCardGeneration;

    /// <summary>
    /// 上次量到的卡片内容宽度（不含 Margin），折叠后 ActualWidth 为 0 时给 FLIP 用。
    /// </summary>
    private double _playTimeCardWidth;

    /// <summary>
    /// 已经 Visible、但揭示还要等进入可视树的那一帧同步完才开播。
    /// 这段时间 XAML Opacity 为 0，隐藏应直接收起，不能先把视觉透明度拉回 1。
    /// </summary>
    private bool _playTimeCardRevealPending;


    /// <summary>
    /// 取游戏时长卡片的揭示裁剪（首次调用时创建；挂载由调用方在动画起止时负责）。
    /// 上、右、下三边创建时就外扩到投影之外（InsetClip 允许负值），之后只动左边。
    /// </summary>
    private InsetClip GetPlayTimeCardClip()
    {
        _playTimeCardClip ??= ElementCompositionPreview.GetElementVisual(Button_PlayTime).Compositor.CreateInsetClip(
            -PlayTimeCardShadowExtent, -PlayTimeCardShadowExtent, -PlayTimeCardShadowExtent, -PlayTimeCardShadowExtent);
        return _playTimeCardClip;
    }


    /// <summary>
    /// 更新游戏时长卡片的显隐。显示和隐藏都在卡片留在可视树时播 Composition：
    /// 隐藏先卷回开始游戏按钮一侧再收起布局；显示先进入布局（XAML Opacity 保持 0，
    /// 避免框架在进入树时把 Visual.Opacity 写成 1），等这一帧同步过去后再反向擦出。
    /// 占位变化由 <see cref="StartPredownloadButtonFlip"/> 平滑。
    /// </summary>
    /// <param name="animate">true 播放动画（用户切换）；false 直接落到目标状态（不播）。</param>
    private void UpdatePlayTimeCardVisual(bool animate)
    {
        bool shown = EnablePlayTime;
        bool motion = animate && EntranceAnimation.AnimationsEnabled();
        int generation = ++_playTimeCardGeneration;
        bool revealWasPending = _playTimeCardRevealPending;
        _playTimeCardRevealPending = false;

        Visual card = ElementCompositionPreview.GetElementVisual(Button_PlayTime);
        InsetClip clip = GetPlayTimeCardClip();

        // 挂着裁剪 = 揭示或卷起播到一半（静止态会卸下裁剪）。这时不 Stop：直接起反向动画替换旧动画，
        // 起点用 this.StartingValue 接着合成器上的当前值（见 InsertStartKeyFrame）。
        // 其余情况结束上一段动画并复位；旧的完成回调由代数作废。预下载按钮的 FLIP 直接停掉归位，
        // 连点下的轻微跳变可接受，同 StartGameMenu。
        bool midFlight = card.Clip is not null;
        if (!motion || !midFlight)
        {
            card.StopAnimation(nameof(Visual.Opacity));
            clip.StopAnimation(nameof(InsetClip.LeftInset));
        }
        StopPredownloadButtonFlip();

        if (!motion)
        {
            Button_PlayTime.Opacity = 1;
            Button_PlayTime.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            clip.LeftInset = -PlayTimeCardShadowExtent;
            card.Opacity = 1;
            card.Clip = null;
            return;
        }

        if (shown)
        {
            // 还在树上（收起动画播到一半）：从当前裁剪直接反向擦出。
            // 从 Collapsed 进来要等框架写完 handout Visual 再开播，否则揭示会被盖掉。
            if (Button_PlayTime.Visibility == Visibility.Visible)
            {
                StartPlayTimeCardReveal(generation);
                return;
            }

            Button_PlayTime.Opacity = 0;
            Button_PlayTime.Visibility = Visibility.Visible;
            double width = MeasurePlayTimeCardWidth();
            if (width > 0)
            {
                _playTimeCardWidth = width;
                StartPredownloadButtonFlip(width + Button_PlayTime.Margin.Left);
            }
            SchedulePlayTimeCardReveal(generation);
            return;
        }

        if (revealWasPending)
        {
            // 揭示还没开播，卡片停在 XAML Opacity 0。直接收起，避免先把视觉透明度拉回 1 造成闪一下。
            double width = MeasurePlayTimeCardWidth();
            Button_PlayTime.Visibility = Visibility.Collapsed;
            Button_PlayTime.Opacity = 1;
            clip.LeftInset = -PlayTimeCardShadowExtent;
            card.Opacity = 1;
            card.Clip = null;
            if (width > 0)
            {
                StartPredownloadButtonFlip(-(width + Button_PlayTime.Margin.Left));
            }
            return;
        }

        // 从当前揭示进度卷回 + 淡出（静止态即从完全展开开始），播完再收起布局。
        double hideWidth = MeasurePlayTimeCardWidth();
        if (hideWidth > 0)
        {
            _playTimeCardWidth = hideWidth;
        }
        double delta = hideWidth + Button_PlayTime.Margin.Left;
        // 静止态从完全展开（裁剪左边外扩到投影之外，挂上时画面不变）开始；揭示播到一半则不写静态值，接着当前值卷回。
        if (!midFlight)
        {
            clip.LeftInset = -PlayTimeCardShadowExtent;
            card.Clip = clip;
            card.Opacity = 1;
        }

        CubicBezierEasingFunction ease = card.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));
        TimeSpan duration = TimeSpan.FromMilliseconds(180);

        // 卷到卡片右缘为止：右侧外扩区里剩下的那截投影此时已随透明度淡到 0。
        ScalarKeyFrameAnimation roll = card.Compositor.CreateScalarKeyFrameAnimation();
        InsertStartKeyFrame(roll, midFlight ? null : -PlayTimeCardShadowExtent);
        roll.InsertKeyFrame(1f, (float)hideWidth, ease);
        roll.Duration = duration;

        ScalarKeyFrameAnimation fade = card.Compositor.CreateScalarKeyFrameAnimation();
        InsertStartKeyFrame(fade, midFlight ? null : 1f);
        fade.InsertKeyFrame(1f, 0f, ease);
        fade.Duration = duration;

        CompositionScopedBatch batch = card.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        clip.StartAnimation(nameof(InsetClip.LeftInset), roll);
        card.StartAnimation(nameof(Visual.Opacity), fade);
        batch.Completed += (_, _) =>
        {
            // 完成回调不保证在 UI 线程；改 Visibility 须回到 DispatcherQueue。
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _playTimeCardGeneration)
                {
                    return;
                }
                if (!EnablePlayTime && Button_PlayTime.Visibility == Visibility.Visible)
                {
                    // 收起布局并复位裁剪/透明度（静止态卸下裁剪），随后预下载滑回贴住开始游戏按钮。
                    Button_PlayTime.Visibility = Visibility.Collapsed;
                    Button_PlayTime.Opacity = 1;
                    clip.LeftInset = -PlayTimeCardShadowExtent;
                    card.Opacity = 1;
                    card.Clip = null;
                    StartPredownloadButtonFlip(-delta);
                }
            });
        };
        batch.End();
    }


    /// <summary>
    /// 等卡片进入可视树的那一帧把 handout Visual 按 XAML 状态写完，再开始擦出。
    /// 同一次调用里开播会被这次写回停掉。
    /// </summary>
    /// <param name="generation">发起这次显示的代数。</param>
    private void SchedulePlayTimeCardReveal(int generation)
    {
        _playTimeCardRevealPending = true;
        void OnRendering(object? sender, object e)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            if (generation != _playTimeCardGeneration)
            {
                return;
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _playTimeCardGeneration || !EnablePlayTime || Button_PlayTime.Visibility != Visibility.Visible)
                {
                    return;
                }
                _playTimeCardRevealPending = false;
                double laidOut = Button_PlayTime.ActualWidth;
                if (laidOut > 0)
                {
                    _playTimeCardWidth = laidOut;
                }
                StartPlayTimeCardReveal(generation);
            });
        }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }


    /// <summary>
    /// 从当前裁剪进度擦出到完全展开。卡片须已经在可视树上。
    /// </summary>
    /// <param name="generation">发起这次显示的代数，完成回调用它丢弃过期收尾。</param>
    private void StartPlayTimeCardReveal(int generation)
    {
        Visual card = ElementCompositionPreview.GetElementVisual(Button_PlayTime);
        InsetClip clip = GetPlayTimeCardClip();
        double width = MeasurePlayTimeCardWidth();
        if (width <= 0)
        {
            Button_PlayTime.Opacity = 1;
            clip.LeftInset = -PlayTimeCardShadowExtent;
            card.Opacity = 1;
            card.Clip = null;
            return;
        }
        _playTimeCardWidth = width;

        // 静止或刚进入树：从完全裁住开始。收起播到一半（挂着裁剪、XAML Opacity 已是 1）则不写静态值，
        // 接着当前值反向展开。
        bool midFlight = card.Clip is not null;
        if (!midFlight)
        {
            card.Clip = clip;
            clip.LeftInset = (float)width;
            card.Opacity = 0;
            // Opacity 的依赖属性会同步写回 Visual。先回到 1（命中与之后的属性同步），再盖回隐藏态开播。
            Button_PlayTime.Opacity = 1;
            card.Clip = clip;
            clip.LeftInset = (float)width;
            card.Opacity = 0;
        }

        CubicBezierEasingFunction ease = card.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        // 刚进入树时关键帧写明起点：框架可能已把透明度写成 1、裁剪清掉，不能靠「当前值」。
        // 终点越过卡片左缘再外扩一截，把左侧投影也擦出来；投影随揭示边缘与透明度一起出现，播完卸下裁剪时画面不变。
        ScalarKeyFrameAnimation reveal = card.Compositor.CreateScalarKeyFrameAnimation();
        InsertStartKeyFrame(reveal, midFlight ? null : (float)width);
        reveal.InsertKeyFrame(1f, -PlayTimeCardShadowExtent, ease);
        reveal.Duration = TimeSpan.FromMilliseconds(280);

        // 淡入快于揭示：先让露出的内容尽快到全不透明，随后揭示边缘的扫动才不被半透明掩盖。
        ScalarKeyFrameAnimation fade = card.Compositor.CreateScalarKeyFrameAnimation();
        InsertStartKeyFrame(fade, midFlight ? null : 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(200);

        CompositionScopedBatch batch = card.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        clip.StartAnimation(nameof(InsetClip.LeftInset), reveal);
        card.StartAnimation(nameof(Visual.Opacity), fade);
        batch.Completed += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _playTimeCardGeneration)
                {
                    return;
                }
                if (EnablePlayTime && Button_PlayTime.Visibility == Visibility.Visible)
                {
                    // 静止态卸下裁剪：挂着裁剪是「正在播」的标记（反向时靠它判断要不要接着当前值）。
                    // XAML Opacity 回到 1，之后的属性同步才不会把卡片重新写成全透明。
                    clip.LeftInset = -PlayTimeCardShadowExtent;
                    card.Opacity = 1;
                    card.Clip = null;
                    Button_PlayTime.Opacity = 1;
                }
            });
        };
        batch.End();
    }


    /// <summary>
    /// 写入关键帧动画的起点。反向播放必须接着合成器上的当前值：UI 线程在 Stop 后读
    /// LeftInset / Opacity 拿到的是上次写入的静态值（动画起点）而非中途值（2026-11 实测，
    /// 显示后马上隐藏会让卡片整块消失），所以中途改用 this.StartingValue，由合成器在开播时取值。
    /// </summary>
    /// <param name="animation">要写起点的关键帧动画。</param>
    /// <param name="from">固定起点；为 null 时用 this.StartingValue（属性开播时的当前值，含旧动画播到的位置）。</param>
    private static void InsertStartKeyFrame(ScalarKeyFrameAnimation animation, float? from)
    {
        if (from is float value)
        {
            animation.InsertKeyFrame(0f, value);
        }
        else
        {
            animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        }
    }


    /// <summary>
    /// 卡片内容宽度（不含左侧 Margin）。已布局用 ActualWidth；否则在 Visible 下 Measure 一次。
    /// </summary>
    /// <returns>内容宽度；量不到时返回上次记下的宽度，仍没有则为 0。</returns>
    private double MeasurePlayTimeCardWidth()
    {
        if (Button_PlayTime.ActualWidth > 0)
        {
            return Button_PlayTime.ActualWidth;
        }
        if (Button_PlayTime.Visibility != Visibility.Visible)
        {
            return _playTimeCardWidth;
        }
        // 折叠元素的 Measure 被框架短路成 DesiredSize=(0,0)，调用方须先 Visible。
        Button_PlayTime.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Button_PlayTime.DesiredSize.Width - Button_PlayTime.Margin.Left;
        return width > 0 ? width : _playTimeCardWidth;
    }


    /// <summary>
    /// 游戏时长卡片占位变化时，让左侧的预下载按钮平滑滑到新位置（FLIP）：布局已一次性落位，
    /// 这里预置 Translation 让按钮先停在视觉原位，再动画归零。卡片在原位揭示、不平移——
    /// 预下载滑行距离（卡片占位宽，含边距）不小于卡片揭示宽度且同曲线、时长更短，
    /// 滑行右缘始终位于卡片揭示左缘左侧，不会与揭示中的卡片重叠；千星修复进度槽位在卡片之后，
    /// 面板原点平移与槽位平移相抵、全局位置不变，无需补偿。
    /// 下载进度信息框展示中时跳过 FLIP：Popup 跟随布局而非渲染位移，平移会造成气泡与按钮脱节，
    /// 此时按钮直接随布局跳位、与气泡保持同步。
    /// </summary>
    /// <param name="offset">本次布局后预下载按钮原点相对旧位置的横向位移：显示卡片为 +Δ（向左让位），收起为 -Δ（向右滑回）。</param>
    private void StartPredownloadButtonFlip(double offset)
    {
        if (offset == 0
            || Button_Predownload.Visibility != Visibility.Visible
            || Button_Predownload.IsProgressPopupOpen)
        {
            return;
        }
        Visual visual = ElementCompositionPreview.GetElementVisual(Button_Predownload);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        ElementCompositionPreview.SetIsTranslationEnabled(Button_Predownload, true);
        visual.Properties.InsertVector3("Translation", new Vector3((float)offset, 0, 0));
        Vector3KeyFrameAnimation flip = compositor.CreateVector3KeyFrameAnimation();
        flip.InsertKeyFrame(1f, Vector3.Zero, ease);
        flip.Duration = TimeSpan.FromMilliseconds(250);
        visual.StartAnimation("Translation", flip);
    }


    /// <summary>
    /// 停掉预下载按钮身上还在跑的 FLIP 动画并把 Translation 基础值归零。
    /// </summary>
    private void StopPredownloadButtonFlip()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(Button_Predownload);
        // 顺序不能反：Translation 属性必须先创建（SetIsTranslationEnabled + InsertVector3），
        // 否则 StopAnimation 找不到属性会抛 ArgumentException（E_INVALIDARG）。
        ElementCompositionPreview.SetIsTranslationEnabled(Button_Predownload, true);
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.StopAnimation("Translation");
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
        /// <summary>自由位置，完整展开。</summary>
        Expanded,

        /// <summary>拖拽中：保持完整高度并跟随指针。</summary>
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


    private RightToolbarState _rightToolbarState = RightToolbarState.Expanded;

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

    /// <summary>子按钮 Visibility 回调（元素 + 注销令牌），卸载时逐个注销。</summary>
    private readonly List<(UIElement Element, long Token)> _rightToolbarVisibilityCallbacks = new();

    /// <summary>进页时记下的 XamlRoot；卸载时 XamlRoot 可能已空，仍要靠它解除 Tooltip 抑制。</summary>
    private XamlRoot? _instantTooltipXamlRoot;


    /// <summary>
    /// 初始化右侧工具栏状态机：默认右上角展开，监听尺寸与 Flyout。
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
        // 整条折叠后 StackPanel 不再参与布局、收不到 SizeChanged，只能盯子项自身的 Visibility。
        foreach (UIElement child in StackPanel_RightToolbar.Children)
        {
            long token = child.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, RightToolbarChild_VisibilityChanged);
            _rightToolbarVisibilityCallbacks.Add((child, token));
        }
        UpdateRightToolbarEmptyState();
        // 有宽度则立刻定位；否则等 SizeChanged。Low 队列只做 Flyout 挂接与兜底，避免再露出左上角默认位移。
        TryRevealRightToolbarLayout();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            // 回调可能晚于 OnUnloaded 才跑：那时重挂 Flyout 会留下要等下次 teardown 才摘的处理器，
            // 还会把 _rightToolbarLayoutRevealed 置回 true，导致同一实例再进页时跳过位置恢复。
            if (!_rightToolbarInitialized)
            {
                return;
            }
            EnsureRightToolbarFlyoutHooks();
            // 尺寸未就绪时不弹引导：TryShowRightToolbarDragTip 会先抑制 Tooltip，而内层拿不到高度直接返回，抑制就留着了。
            if (TryRevealRightToolbarLayout())
            {
                TryShowRightToolbarDragTip();
            }
        });
    }


    /// <summary>
    /// 卸载时解除计时器、动画与事件，恢复 Tooltip。
    /// </summary>
    private void TeardownRightToolbarCollapse()
    {
        // 离开页面时持久化当前布局；未关掉的引导下次再出。
        DismissRightToolbarDragTip(markSeen: false);
        // 未 Restore 前 _rightToolbarX/Y 仍是 0，不能写回，否则会把上次位置覆盖成左上角。
        if (_rightToolbarLayoutRevealed)
        {
            SaveRightToolbarLayout();
        }
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
        foreach ((UIElement element, long token) in _rightToolbarVisibilityCallbacks)
        {
            element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token);
        }
        _rightToolbarVisibilityCallbacks.Clear();

        foreach (FlyoutBase flyout in _rightToolbarHookedFlyouts)
        {
            flyout.Opened -= RightToolbarFlyout_Opened;
            flyout.Closed -= RightToolbarFlyout_Closed;
        }
        _rightToolbarHookedFlyouts.Clear();
        _rightToolbarInitialized = false;
        _rightToolbarLayoutRevealed = false;
        Border_RightToolbar.Opacity = 0;
        Border_RightToolbar.IsHitTestVisible = false;
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

        if (!_rightToolbarLayoutRevealed)
        {
            if (TryRevealRightToolbarLayout())
            {
                TryShowRightToolbarDragTip();
            }
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
    /// 子项显隐变化时同步高度（始终按完整展开尺寸）。
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

            // 进页时按钮多是异步出现，整条先折叠着；等真正有高度后再补弹拖拽引导。
            if (_rightToolbarLayoutRevealed && e.NewSize.Height > 0 && !TeachingTip_RightToolbarDrag.IsOpen)
            {
                TryShowRightToolbarDragTip();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Right toolbar size changed");
        }
    }


    private void RightToolbarChild_VisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
        UpdateRightToolbarEmptyState();
    }


    /// <summary>
    /// 没有任何可见按钮（如星布谷地等暂无功能的游戏）时折叠整条工具栏，避免只剩 Padding 的空壳；
    /// 有按钮出现时再显示。
    /// </summary>
    private void UpdateRightToolbarEmptyState()
    {
        bool hasButtons = CountVisibleRightToolbarButtons() > 0;
        Visibility target = hasButtons ? Visibility.Visible : Visibility.Collapsed;
        if (Border_RightToolbar.Visibility == target)
        {
            return;
        }

        if (hasButtons)
        {
            Border_RightToolbar.Visibility = Visibility.Visible;
            // 高度按按钮数直接算，不等下一轮 SizeChanged，避免首帧沿用空态高度。
            SyncRightToolbarHeightForState(animate: false);
            return;
        }

        // 折叠后收不到 PointerExited，悬停 / 浮出 / 引导状态都要在这里收干净。
        DismissRightToolbarDragTip(markSeen: false);
        StopRightToolbarCollapseTimer();
        _rightToolbarPointerOver = false;
        if (_rightToolbarState is RightToolbarState.DockRevealed)
        {
            TransitionRightToolbar(RightToolbarState.Docked, animate: false);
        }
        Border_RightToolbar.Visibility = Visibility.Collapsed;
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
        // 拖拽中指针可能短暂离开视觉区域，不触发回贴边计时。
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
    /// 进入拖拽：从子 Button 夺过 Capture；保持完整高度；禁用按钮命中，抑制 Tooltip。
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
        ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate: false);
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
            case RightToolbarState.Expanded:
                _rightToolbarRevealInteractive = true;
                ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate);
                SetRightToolbarButtonsHitTestVisible(true);
                InstantTooltip.SetSuppressed(XamlRoot, false);
                break;

            case RightToolbarState.Dragging:
                _rightToolbarRevealInteractive = false;
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
        ApplyRightToolbarHeight(MeasureRightToolbarExpandedHeight(), animate);
    }


    /// <summary>
    /// 空闲 5 秒后：贴边浮出 → 回到贴边。
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

        if (_rightToolbarState is not RightToolbarState.DockRevealed)
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
        // 空条已折叠：没有可指向的目标。
        if (Border_RightToolbar.Visibility != Visibility.Visible)
        {
            return;
        }
        StopRightToolbarCollapseTimer();
        InstantTooltip.SetSuppressed(XamlRoot, true);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_rightToolbarInitialized || AppConfig.HasSeenRightToolbarDragHint)
            {
                return;
            }
            if (Border_RightToolbar.Visibility != Visibility.Visible || Border_RightToolbar.ActualHeight <= 0)
            {
                // 弹不出引导就别留着窗口级抑制，否则下侧工具栏 / 导航的 InstantTooltip 也会一起失效。
                if (!_rightToolbarDragging && !_rightToolbarPressed)
                {
                    InstantTooltip.SetSuppressed(XamlRoot, false);
                }
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
    /// TeachingTip 关闭后：按需记「已看过」，恢复 InstantTooltip，贴边浮出态允许空闲回贴边。
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
    }


    private void ApplyRightToolbarInteractionGate()
    {
        bool interactive = _rightToolbarState switch
        {
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


    /// <summary>
    /// 在 RootGrid 已有有效尺寸后恢复位置并显示工具栏。
    /// 未定位前 XAML 原点在左上，不能露出。
    /// </summary>
    /// <returns>已显示或本次成功恢复为 <see langword="true"/>；窗口尺寸尚未就绪为 <see langword="false"/>。</returns>
    private bool TryRevealRightToolbarLayout()
    {
        if (_rightToolbarLayoutRevealed)
        {
            return true;
        }

        if (RootGrid.ActualWidth <= 0 || RootGrid.ActualHeight <= 0)
        {
            return false;
        }

        RestoreRightToolbarLayout();
        UpdateRightToolbarPopupSide();
        Border_RightToolbar.Opacity = 1;
        Border_RightToolbar.IsHitTestVisible = true;
        _rightToolbarLayoutRevealed = true;
        return true;
    }


    /// <summary>默认放在窗口右上（对齐原 Margin 右 12、上 48）。</summary>
    private void PlaceRightToolbarDefaultPosition()
    {
        double width = MeasureRightToolbarWidth();
        double x = Math.Max(0, RootGrid.ActualWidth - RightToolbarDefaultMarginRight - width);
        double y = RightToolbarDefaultMarginTop;
        SetRightToolbarPosition(x, y, animate: false);
    }


    /// <summary>
    /// 从设置恢复工具栏位置与贴边状态；无记录时使用默认右上角展开。
    /// 布局串：<c>docked|left|y</c> / <c>docked|right|y</c> / <c>free|x|y</c>
    /// </summary>
    private void RestoreRightToolbarLayout()
    {
        string? raw = AppConfig.GameLauncherRightToolbarLayout;
        if (string.IsNullOrWhiteSpace(raw))
        {
            PlaceRightToolbarDefaultPosition();
            TransitionRightToolbar(RightToolbarState.Expanded, animate: false);
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
                TransitionRightToolbar(RightToolbarState.Expanded, animate: false);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore right toolbar layout");
        }

        PlaceRightToolbarDefaultPosition();
        TransitionRightToolbar(RightToolbarState.Expanded, animate: false);
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
        double height = Math.Max(GetRightToolbarCurrentHeight(), MeasureRightToolbarExpandedHeight());
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
                EasingMode = _rightToolbarState is RightToolbarState.Dragging
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
        return MeasureRightToolbarExpandedHeight();
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


    /// <summary>
    /// 取工具栏宽度。首次布局前、空条折叠时 ActualWidth 只有 0 或 Padding 宽，
    /// 这时按「按钮都是 <see cref="RightToolbarButtonSize"/> 宽」取下限，否则按空条宽度定的位置在按钮出现后会向右溢出；
    /// 若以后放进非该宽度的按钮，首次定位会偏。
    /// </summary>
    /// <returns>实际宽度，至少为一个按钮加 Padding 的宽度。</returns>
    private double MeasureRightToolbarWidth()
    {
        double padding = Border_RightToolbar.Padding.Left + Border_RightToolbar.Padding.Right;
        return Math.Max(Border_RightToolbar.ActualWidth, padding + RightToolbarButtonSize);
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
        if (_rightToolbarState is RightToolbarState.Docked)
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
