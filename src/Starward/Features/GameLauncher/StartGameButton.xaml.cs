using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.RPC.GameInstall;
using System;
using System.Numerics;
using System.Windows.Input;
using Windows.Foundation;


namespace Starward.Features.GameLauncher;

[INotifyPropertyChanged]
public sealed partial class StartGameButton : UserControl
{


    private static Brush AccentFillColorDefaultBrush => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    private static Brush TextOnAccentFillColorDisabled => (Brush)Application.Current.Resources["TextOnAccentFillColorDisabledBrush"];
    private static Brush TextOnAccentFillColorPrimaryBrush => (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];


    private readonly StartGameButtonEffects _effects = new();


    public StartGameButton()
    {
        this.InitializeComponent();
        this.ActualThemeChanged += StartGameButton_ActualThemeChanged;
        this.Loaded += StartGameButton_Loaded;
        this.Unloaded += StartGameButton_Unloaded;
    }


    private void StartGameButton_Loaded(object sender, RoutedEventArgs e)
    {
        _effects.Attach(Grid_Root, Grid_GlowHost, Grid_EffectHost, Button_GameAction);
        UpdateEffectsState();
        _menuCloseTimer = DispatcherQueue.CreateTimer();
        _menuCloseTimer.Interval = TimeSpan.FromMilliseconds(120);
        _menuCloseTimer.Tick += MenuCloseTimer_Tick;
        QuickMenu.RequestClose += CloseQuickMenu;
        QuickMenu.ChildPopupOpenChanged += QuickMenu_ChildPopupOpenChanged;
    }


    /// <summary>
    /// 同步动效启用状态：呼吸光晕仅在「可开始游戏」时亮；
    /// 流光 / 聚光灯 / 点击光爆在所有显示强调色背景的可操作状态（开始 / 安装 / 更新 等）都亮。
    /// </summary>
    private void UpdateEffectsState()
    {
        _effects.SetState(GameState is GameState.StartGame, IsAccentColorBackgroundVisible);
    }


    private void StartGameButton_Unloaded(object sender, RoutedEventArgs e)
    {
        _effects.Detach();
        if (_menuCloseTimer is not null)
        {
            _menuCloseTimer.Stop();
            _menuCloseTimer.Tick -= MenuCloseTimer_Tick;
        }
        DisableMenuPointerTracking();
        QuickMenu.RequestClose -= CloseQuickMenu;
        QuickMenu.ChildPopupOpenChanged -= QuickMenu_ChildPopupOpenChanged;
    }




    #region 快速启动悬停菜单


    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _menuCloseTimer;

    private bool _pointerOverMenuButton;

    private bool _pointerOverMenuPopup;

    private bool _menuChildPopupOpen;

    private bool _quickMenuClosing;

    private UIElement? _menuPointerTrackingRoot;


    private void OpenQuickMenu()
    {
        _menuCloseTimer?.Stop();

        if (!Popup_QuickMenu.IsOpen)
        {
            UpdateQuickMenuMaxHeight();
            QuickMenu.CurrentGameId = CurrentGameId;
            QuickMenu.OnOpening();
            _quickMenuClosing = false;
            // 打开前先把菜单缩成一个点并隐藏，避免开场闪现整块。
            Visual v = ElementCompositionPreview.GetElementVisual(QuickMenuRoot);
            v.Scale = new Vector3(0.01f, 0.01f, 1f);
            v.Opacity = 0;
            Popup_QuickMenu.IsOpen = true;
            PlayQuickMenuOpenAnimation(fromSeed: true);
        }
        else
        {
            // 已打开（或正在收起）时重新悬停：若在收起则展开回去，否则无操作。
            CancelQuickMenuClose();
        }

        SettingButtonPointerOver = true;
        EnableMenuPointerTracking();
    }


    /// <summary>
    /// 按窗口可用空间限制菜单高度，避免在底部区域弹出时超出上边界。
    /// </summary>
    private void UpdateQuickMenuMaxHeight()
    {
        if (XamlRoot is null || Button_Menu.ActualWidth <= 0)
        {
            ScrollViewer_QuickMenu.MaxHeight = double.PositiveInfinity;
            return;
        }

        var transform = Button_Menu.TransformToVisual(null);
        var buttonBounds = transform.TransformBounds(new Rect(0, 0, Button_Menu.ActualWidth, Button_Menu.ActualHeight));

        const double margin = 16;
        double spaceAbove = buttonBounds.Top - margin;
        double spaceBelow = XamlRoot.Size.Height - buttonBounds.Bottom - margin;
        double maxHeight = Math.Max(spaceAbove, spaceBelow);

        ScrollViewer_QuickMenu.MaxHeight = maxHeight > 120 ? maxHeight : double.PositiveInfinity;
    }


    private void CloseQuickMenu()
    {
        _menuCloseTimer?.Stop();
        if (!Popup_QuickMenu.IsOpen || _quickMenuClosing)
        {
            return;
        }

        _menuChildPopupOpen = false;
        _quickMenuClosing = true;
        PlayQuickMenuCloseAnimation();
    }


    private void FinalizeCloseQuickMenu()
    {
        _quickMenuClosing = false;
        DisableMenuPointerTracking();
        Popup_QuickMenu.IsOpen = false;
        SettingButtonPointerOver = false;
    }


    private void CancelQuickMenuClose()
    {
        if (!_quickMenuClosing)
        {
            return;
        }

        _quickMenuClosing = false;
        // 从当前缩放展开回原始大小（不从「点」重新开始，避免跳变）。
        PlayQuickMenuOpenAnimation(fromSeed: false);
        SettingButtonPointerOver = true;
    }


    private void ScheduleCloseQuickMenu()
    {
        _menuCloseTimer?.Stop();
        _menuCloseTimer?.Start();
    }


    private void MenuCloseTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!_pointerOverMenuButton && !_pointerOverMenuPopup && !_menuChildPopupOpen)
        {
            CloseQuickMenu();
        }
    }


    // 「从一个点放大 / 收缩」开合动效，与抽卡记录页筛选卡池浮层一致（1:1 复刻 FluentAnimations.ExpandContractFlyout 的曲线与时长）。
    // 区别：本菜单在按钮上方向上生长，缩放原点取「底部中心」（浮层版取顶部中心）。

    // 缩到约 20px、且不超过原尺寸 1%，等效于「一个点」。
    private const string QuickMenuSeedScaleExpression =
        "Vector3(Min(0.01, 20.0 / this.Target.Size.X), Min(0.01, 20.0 / this.Target.Size.Y), 1.0)";

    // 缩放原点固定底部中心，使菜单看起来从汉堡按钮处向上生长。
    private const string QuickMenuBottomCenterExpression = "Vector3(this.Target.Size.X * 0.5, this.Target.Size.Y, 0)";


    /// <summary>
    /// 展开：从底部中心的一个「点」放大到原始大小（300ms 缓出，(0.1,0.9)(0.2,1)）。
    /// <paramref name="fromSeed"/> 为 false 时从当前缩放展开回去（用于收起途中重新悬停）。
    /// </summary>
    private void PlayQuickMenuOpenAnimation(bool fromSeed)
    {
        Visual v = ElementCompositionPreview.GetElementVisual(QuickMenuRoot);

        v.StopAnimation(nameof(Visual.CenterPoint));
        ExpressionAnimation center = v.Compositor.CreateExpressionAnimation(QuickMenuBottomCenterExpression);
        v.StartAnimation(nameof(Visual.CenterPoint), center);

        // 停掉可能仍在进行的收起透明度动画（其末帧会把透明度瞬置 0），否则取消收起后菜单仍会消失。
        v.StopAnimation(nameof(Visual.Opacity));
        v.Opacity = 1;

        if (!EntranceAnimation.AnimationsEnabled())
        {
            v.StopAnimation(nameof(Visual.Scale));
            v.Scale = Vector3.One;
            return;
        }

        CubicBezierEasingFunction entrance = v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        Vector3KeyFrameAnimation scale = v.Compositor.CreateVector3KeyFrameAnimation();
        if (fromSeed)
        {
            scale.InsertExpressionKeyFrame(0f, QuickMenuSeedScaleExpression);
        }
        scale.InsertKeyFrame(1f, Vector3.One, entrance);
        scale.Duration = TimeSpan.FromMilliseconds(300);
        v.StartAnimation(nameof(Visual.Scale), scale);
    }


    /// <summary>
    /// 收起：收缩回底部中心的那个「点」（150ms 缓入，(0.7,0)(1,0.5)），透明度仅末帧瞬间归零，结束后真正关闭 Popup。
    /// </summary>
    private void PlayQuickMenuCloseAnimation()
    {
        Visual v = ElementCompositionPreview.GetElementVisual(QuickMenuRoot);

        if (!EntranceAnimation.AnimationsEnabled())
        {
            FinalizeCloseQuickMenu();
            return;
        }

        CompositionScopedBatch batch = v.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        CubicBezierEasingFunction exit = v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));
        Vector3KeyFrameAnimation scale = v.Compositor.CreateVector3KeyFrameAnimation();
        // 不固定 0f 起点，从当前缩放收缩，避免「开场未结束就收起」时跳变。
        scale.InsertExpressionKeyFrame(1f, QuickMenuSeedScaleExpression, exit);
        scale.Duration = TimeSpan.FromMilliseconds(150);
        v.StartAnimation(nameof(Visual.Scale), scale);

        StepEasingFunction step = v.Compositor.CreateStepEasingFunction();
        step.IsFinalStepSingleFrame = true;
        ScalarKeyFrameAnimation opacity = v.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, 0f, step);
        opacity.Duration = TimeSpan.FromMilliseconds(130);
        v.StartAnimation(nameof(Visual.Opacity), opacity);

        batch.Completed += (_, _) =>
        {
            // 若中途被重新悬停取消了收起（_quickMenuClosing=false），则不真正关闭。
            if (_quickMenuClosing)
            {
                FinalizeCloseQuickMenu();
            }
        };
        batch.End();
    }


    private void QuickMenu_ChildPopupOpenChanged(bool isOpen)
    {
        _menuChildPopupOpen = isOpen;
        _menuCloseTimer?.Stop();
        if (!isOpen)
        {
            // 下拉关闭后 PointerEntered 不会重触发，先假定仍在菜单内以便继续操作（如点「应用」）。
            _pointerOverMenuPopup = true;
        }
    }


    private void EnableMenuPointerTracking()
    {
        if (_menuPointerTrackingRoot is not null || XamlRoot?.Content is not UIElement root)
        {
            return;
        }

        _menuPointerTrackingRoot = root;
        root.PointerMoved += MenuPointerTrackingRoot_PointerMoved;
    }


    private void DisableMenuPointerTracking()
    {
        if (_menuPointerTrackingRoot is null)
        {
            return;
        }

        _menuPointerTrackingRoot.PointerMoved -= MenuPointerTrackingRoot_PointerMoved;
        _menuPointerTrackingRoot = null;
    }


    private void MenuPointerTrackingRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!Popup_QuickMenu.IsOpen || _menuChildPopupOpen)
        {
            return;
        }

        bool overButton = IsPointerOverElement(Button_Menu, e);
        bool overMenu = IsPointerOverElement(QuickMenuRoot, e);

        _pointerOverMenuButton = overButton;
        _pointerOverMenuPopup = overMenu;

        if (overButton || overMenu)
        {
            _menuCloseTimer?.Stop();
            CancelQuickMenuClose();
        }
        else
        {
            ScheduleCloseQuickMenu();
        }
    }


    private static bool IsPointerOverElement(FrameworkElement element, PointerRoutedEventArgs e)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        Point point = e.GetCurrentPoint(element).Position;
        return point.X >= 0 && point.Y >= 0
            && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }


    private void Button_Menu_Click(object sender, RoutedEventArgs e)
    {
        if (Popup_QuickMenu.IsOpen)
        {
            CloseQuickMenu();
        }
        else
        {
            OpenQuickMenu();
        }
    }


    private void Button_Menu_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverMenuButton = true;
        OpenQuickMenu();
    }


    private void Button_Menu_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverMenuButton = false;
        ScheduleCloseQuickMenu();
    }


    private void QuickMenu_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverMenuPopup = true;
        _menuCloseTimer?.Stop();
        CancelQuickMenuClose();
    }


    private void QuickMenu_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverMenuPopup = false;
        ScheduleCloseQuickMenu();
    }


    #endregion


    public ICommand GameCommand { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 当前游戏，由首页绑定传入，转交悬停快速启动菜单。
    /// </summary>
    public GameId CurrentGameId { get; set => SetProperty(ref field, value); }


    public string? RunningGameInfo { get; set => SetProperty(ref field, value); }



    public GameState GameState { get; set { if (SetProperty(ref field, value)) UpdateActionButtonState(); } }


    public bool ActionButtonPointerOver { get; set { if (SetProperty(ref field, value)) UpdateActionButtonState(); } }


    public bool SettingButtonPointerOver { get; set { if (SetProperty(ref field, value)) UpdateButtonForeground(); } }


    public bool GameStateIsInstalling => GameState is GameState.Installing;


    public bool IsAccentColorBackgroundVisible => Button_GameAction.IsEnabled && GameState is not GameState.Installing;


    public bool IsGameActionCommandRunning => !Button_GameAction.IsEnabled && GameState is not GameState.GameIsRunning;


    public string StartGameButtonText => GameState switch
    {
        GameState.StartGame => Lang.LauncherPage_StartGame,
        GameState.GameIsRunning => Lang.LauncherPage_GameIsRunning,
        GameState.InstallGame => Lang.LauncherPage_InstallGame,
        GameState.UpdateGame => Lang.LauncherPage_UpdateGame,
        GameState.UpdatePlugin => "Update Plugins",
        GameState.Installing => "",
        GameState.ResumeDownload => Lang.StartGameButton_ResumeDownload,
        GameState.ComingSoon => "Coming Soon",
        _ => "",
    };


    public Brush ActionButtonForeground => (Button_GameAction.IsEnabled, IsAccentColorBackgroundVisible, ActionButtonPointerOver) switch
    {
        (false, _, _) => TextOnAccentFillColorDisabled,
        (true, false, true) => AccentFillColorDefaultBrush,
        (true, false, false) => TextOnAccentFillColorDisabled,
        _ => TextOnAccentFillColorPrimaryBrush
    };


    public Brush SettingButtonForeground => (IsAccentColorBackgroundVisible, SettingButtonPointerOver) switch
    {
        (false, true) => AccentFillColorDefaultBrush,
        (false, false) => TextOnAccentFillColorDisabled,
        _ => TextOnAccentFillColorPrimaryBrush
    };



    private void UpdateActionButtonState()
    {
        Button_GameAction.IsEnabled = GameState is not GameState.GameIsRunning and not GameState.ComingSoon;
        OnPropertyChanged(nameof(GameStateIsInstalling));
        if (GameStateIsInstalling)
        {
            UpdateActionButtonStateWhenInstalling();
        }
        OnPropertyChanged(nameof(IsAccentColorBackgroundVisible));
        OnPropertyChanged(nameof(IsGameActionCommandRunning));
        OnPropertyChanged(nameof(StartGameButtonText));
        UpdateButtonForeground();
        UpdateEffectsState();
    }



    private void UpdateButtonForeground()
    {
        OnPropertyChanged(nameof(ActionButtonForeground));
        OnPropertyChanged(nameof(SettingButtonForeground));
    }



    private void Button_GameAction_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsAccentColorBackgroundVisible));
        OnPropertyChanged(nameof(IsGameActionCommandRunning));
        OnPropertyChanged(nameof(ActionButtonForeground));
        OnPropertyChanged(nameof(SettingButtonForeground));
        UpdateEffectsState();
    }



    private void Control_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender as Grid == Grid_Root)
        {
            if (GameState is GameState.GameIsRunning or GameState.Installing)
            {
                Popup_GameInfoOrDownloadProgress.IsOpen = true;
            }
        }
        else if (sender as Button == Button_GameAction)
        {
            ActionButtonPointerOver = true;
        }
    }


    private void Control_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender as Grid == Grid_Root)
        {
            Popup_GameInfoOrDownloadProgress.IsOpen = false;
        }
        else if (sender as Button == Button_GameAction)
        {
            ActionButtonPointerOver = false;
        }

    }





    public GameInstallState InstallState { get; set { if (SetProperty(ref field, value)) UpdateActionButtonStateWhenInstalling(); } }


    public bool InstallStateIsPending => InstallState is GameInstallState.Waiting;

    public bool InstallStateIsDownloading => InstallState is GameInstallState.Downloading;

    public double Button_GameAction_DownloadingRemainTime_Opacity => !ActionButtonPointerOver && InstallState is GameInstallState.Downloading ? 1 : 0;

    public double TextBlock_GameAction_InstallState_Opacity => !ActionButtonPointerOver && InstallState is not GameInstallState.Downloading ? 1 : 0;

    public double TextBlock_GameAction_PointerOver_Opacity => ActionButtonPointerOver ? 1 : 0;

    public string TextBlock_GameAction_InstallState_Text => (ActionButtonPointerOver, InstallState) switch
    {
        (false, GameInstallState.Waiting) => Lang.StartGameButton_Waiting,
        (false, GameInstallState.Decompressing) => Lang.DownloadGamePage_Decompressing,
        (false, GameInstallState.Merging) => Lang.DownloadGamePage_Merging,
        (false, GameInstallState.Verifying) => Lang.DownloadGamePage_Verifying,
        (false, GameInstallState.Paused) => Lang.DownloadGamePage_Paused,
        (false, GameInstallState.Error) => Lang.Common_Error,
        (false, GameInstallState.Queueing) => Lang.StartGameButton_InQueue,
        _ => ""
    };

    public string TextBlock_GameAction_PointerOver_Text => (ActionButtonPointerOver, InstallState) switch
    {
        (true, GameInstallState.Waiting or GameInstallState.Downloading) => Lang.DownloadGamePage_Pause,
        (true, GameInstallState.Paused or GameInstallState.Error or GameInstallState.Queueing) => Lang.Common_Continue,
        (true, GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying) => Lang.Common_Cancel,
        _ => ""

    };

    public string InstallStateText => InstallState switch
    {
        GameInstallState.Waiting => Lang.StartGameButton_Waiting,
        GameInstallState.Downloading => Lang.DownloadGamePage_Downloading,
        GameInstallState.Decompressing => Lang.DownloadGamePage_Decompressing,
        GameInstallState.Merging => Lang.DownloadGamePage_Merging,
        GameInstallState.Verifying => Lang.DownloadGamePage_Verifying,
        GameInstallState.Paused => Lang.DownloadGamePage_Paused,
        GameInstallState.Finish => Lang.DownloadGamePage_Finished,
        GameInstallState.Error => Lang.DownloadGamePage_SomethingError,
        GameInstallState.Queueing => Lang.StartGameButton_InQueue,
        _ => "State Error"
    };


    public void UpdateActionButtonStateWhenInstalling()
    {
        OnPropertyChanged(nameof(InstallState));
        OnPropertyChanged(nameof(InstallStateIsPending));
        OnPropertyChanged(nameof(InstallStateIsDownloading));
        OnPropertyChanged(nameof(Button_GameAction_DownloadingRemainTime_Opacity));
        OnPropertyChanged(nameof(TextBlock_GameAction_InstallState_Opacity));
        OnPropertyChanged(nameof(TextBlock_GameAction_PointerOver_Opacity));
        OnPropertyChanged(nameof(TextBlock_GameAction_InstallState_Text));
        OnPropertyChanged(nameof(TextBlock_GameAction_PointerOver_Text));
        OnPropertyChanged(nameof(InstallStateText));
    }




    public int ProgressRingValue { get; set => SetProperty(ref field, value); }

    public string ProgressPercentText { get; set => SetProperty(ref field, value); }

    public string? DownloadBytesText { get; set => SetProperty(ref field, value); }

    public string? DownloadSpeedText { get; set => SetProperty(ref field, value); }

    public string? InstallBytesText { get; set => SetProperty(ref field, value); }

    public string? InstallSpeedText { get; set => SetProperty(ref field, value); }

    public string? VerifySpeedText { get; set => SetProperty(ref field, value); }

    public string? RemainTimeText { get; set => SetProperty(ref field, value); }

    public string? ErrorMessage { get; set => SetProperty(ref field, value); }



    public void UpdateGameInstallTaskState(GameInstallContext task)
    {
        InstallState = task.State;
        DownloadBytesText = ToBytesText(task.Progress_DownloadFinishBytes, task.Progress_DownloadTotalBytes);
        InstallBytesText = ToBytesText(task.Progress_WriteFinishBytes, task.Progress_WriteTotalBytes);
        ErrorMessage = task.ErrorMessage;
        if (InstallState is GameInstallState.Downloading)
        {
            long total = task.Progress_DownloadTotalBytes;
            long finish = task.Progress_DownloadFinishBytes;
            double progress = (double)finish / total;
            DownloadSpeedText = ToSpeedText(task.NetworkDownloadSpeed);
            InstallSpeedText = ToSpeedText(task.StorageWriteSpeed);
            VerifySpeedText = ToSpeedText(task.StorageReadSpeed);
            RemainTimeText = ToRemainTimeText(task.RemainTimeSeconds);
            if (task.Operation is GameInstallOperation.Update && task.DownloadMode is GameInstallDownloadMode.Chunk)
            {
                progress = (double)task.Progress_WriteFinishBytes / task.Progress_WriteTotalBytes;
                ProgressRingValue = (int)(progress * 100);
                ProgressPercentText = $"{progress:P1}";
            }
            else
            {
                ProgressRingValue = (int)(progress * 100);
                ProgressPercentText = $"{progress:P1}";
            }
        }
        else if (InstallState is GameInstallState.Decompressing or GameInstallState.Merging)
        {
            DownloadSpeedText = null;
            InstallSpeedText = null;
            VerifySpeedText = null;
            RemainTimeText = "--:--:--";
            ProgressRingValue = (int)(task.Progress_Percent * 100);
            ProgressPercentText = $"{task.Progress_Percent:P1}";
        }
        else if (InstallState is GameInstallState.Finish)
        {
            DownloadSpeedText = null;
            InstallBytesText = null;
            DownloadSpeedText = null;
            InstallSpeedText = null;
            VerifySpeedText = null;
            RemainTimeText = null;
            ErrorMessage = null;
            ProgressRingValue = 100;
            ProgressPercentText = "100%";
            Popup_GameInfoOrDownloadProgress.IsOpen = false;
        }
        else
        {
            DownloadSpeedText = null;
            InstallSpeedText = null;
            VerifySpeedText = null;
            RemainTimeText = "--:--:--";
        }
    }


    private static string? ToBytesText(long finish, long total)
    {
        const double MB = 1 << 20;
        const double GB = 1 << 30;
        if (total == 0)
        {
            return null;
        }
        if (total >= GB)
        {
            return $"{finish / GB:F2}/{total / GB:F2} GB";
        }
        else
        {
            return $"{finish / MB:F2}/{total / MB:F2} MB";
        }
    }


    private static string ToSpeedText(long bytes)
    {
        const double KB = 1 << 10;
        const double MB = 1 << 20;
        if (bytes >= MB)
        {
            return $"{bytes / MB:F2} MB/s";
        }
        else
        {
            return $"{bytes / KB:F2} KB/s";
        }
    }


    private static string? ToRemainTimeText(long seconds)
    {
        if (seconds == 0)
        {
            return "--:--:--";
        }
        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }



    private void StartGameButton_ActualThemeChanged(FrameworkElement sender, object args)
    {
        OnPropertyChanged(nameof(ActionButtonForeground));
        OnPropertyChanged(nameof(SettingButtonForeground));
        _effects.OnThemeChanged();
    }


}
