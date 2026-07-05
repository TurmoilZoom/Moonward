using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.SignIn;
using Starward.Core.HoYoPlay;
using Starward.Features.Setting;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord.SignIn;

/// <summary>
/// 游戏启动页上的每日签到入口：图标按钮 + Flyout 卡片（日历、签到 / 补签、自动签到开关）。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class SignInButton : UserControl
{

    private readonly ILogger<SignInButton> _logger = AppConfig.GetLogger<SignInButton>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();

    private readonly SignInService _signInService = AppConfig.GetService<SignInService>();

    private readonly AutoSignInService _autoSignInService = AppConfig.GetService<AutoSignInService>();


    /// <summary>最近一次拉取的补签信息，补签确认弹窗展示消耗用。</summary>
    private SignInResignInfo? _resignInfo;

    /// <summary>Flyout 是否已加载过签到状态，避免重复请求。</summary>
    private bool _statusLoaded;

    /// <summary>最近一次拉取的累计签到天数，语言切换时用于重算 <see cref="TotalSignDaysHint"/>。</summary>
    private int _totalSignDay;


    /// <summary>初始化控件，默认隐藏，待解析到有效角色后再显示。</summary>
    public SignInButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }


    /// <summary>
    /// 语言切换后刷新 x:Bind 绑定与代码格式化的签到文案。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="__">语言变更消息（未使用）。</param>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
        if (_statusLoaded)
        {
            TotalSignDaysHint = string.Format(Lang.SignInButton_SignedInForDays, _totalSignDay);
        }
    }



    /// <summary>
    /// 当前启动页所选游戏，变更时切换图标并重新解析角色。
    /// </summary>
    public GameId? CurrentGameId
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsGenshinEnabled));
                OnPropertyChanged(nameof(IsStarRailEnabled));
                OnPropertyChanged(nameof(IsZZZEnabled));
                OnPropertyChanged(nameof(IsBH3Enabled));
                if (IsLoaded)
                {
                    InitializeForCurrentGame();
                }
            }
        }
    }


    /// <summary>
    /// 签到图标按当前游戏（<see cref="GameBiz.Game"/>）切换，x:Load 仅实例化命中那张（绝区零是动图，按需加载）。
    /// </summary>
    /// <summary>是否显示原神签到图标。</summary>
    public bool IsGenshinEnabled => CurrentGameId?.GameBiz.Game is GameBiz.hk4e;

    /// <summary>是否显示星铁签到图标。</summary>
    public bool IsStarRailEnabled => CurrentGameId?.GameBiz.Game is GameBiz.hkrpg;

    /// <summary>是否显示绝区零签到图标（动图，按需 x:Load）。</summary>
    public bool IsZZZEnabled => CurrentGameId?.GameBiz.Game is GameBiz.nap;

    /// <summary>是否显示崩坏3签到图标。</summary>
    public bool IsBH3Enabled => CurrentGameId?.GameBiz.Game is GameBiz.bh3;


    /// <summary>当前用于签到的游戏角色（最近选中或首个）。</summary>
    private GameRecordRole? GameRecordRole { get; set => SetProperty(ref field, value); }


    /// <summary>本月奖励日历数据源。</summary>
    public ObservableCollection<SignInAwardView> Awards { get; } = new();


    /// <summary>「本月已签 X 天」提示文案。</summary>
    public string? TotalSignDaysHint { get; set => SetProperty(ref field, value); }


    /// <summary>今日是否已签到，控制状态标签与签到按钮可用性。</summary>
    public bool IsTodaySigned { get; set => SetProperty(ref field, value); }


    /// <summary>拉取状态失败时的错误提示。</summary>
    public string? ErrorMessage { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 当前签到对应的游戏（已处理 bilibili→cn 映射），自动签到开关按它区分存储。
    /// </summary>
    private GameBiz _signInGameBiz;

    private bool _autoSignInEnabled;

    /// <summary>
    /// 当前游戏的自动签到开关，双向绑定 ToggleSwitch。
    /// </summary>
    public bool AutoSignInEnabled
    {
        get => _autoSignInEnabled;
        set
        {
            if (SetProperty(ref _autoSignInEnabled, value) && !string.IsNullOrEmpty(_signInGameBiz.Value))
            {
                _autoSignInService.SetEnabled(_signInGameBiz, value);
            }
        }
    }



    /// <summary>控件加载时按当前游戏初始化可见性与角色。</summary>
    private void SignInButton_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeForCurrentGame();
    }



    /// <summary>控件卸载时清理状态，避免切换页面后残留旧数据。</summary>
    private void SignInButton_Unloaded(object sender, RoutedEventArgs e)
    {
        GameRecordRole = null;
        _resignInfo = null;
        _statusLoaded = false;
        Awards.Clear();
        ErrorMessage = null;
    }



    /// <summary>
    /// 解析当前游戏对应的角色，过 Feature 门控后显示按钮。
    /// </summary>
    private void InitializeForCurrentGame()
    {
        try
        {
            if (CurrentGameId is null)
            {
                this.Visibility = Visibility.Collapsed;
                return;
            }
            if (!GameFeatureConfig.FromGameId(CurrentGameId).SupportSignIn)
            {
                this.Visibility = Visibility.Collapsed;
                return;
            }
            GameBiz gameBiz = CurrentGameId.GameBiz;
            if (gameBiz.Server is "bilibili")
            {
                gameBiz = $"{gameBiz.Game}_cn";
            }
            // 自动签到开关按游戏区分：切换游戏时同步开关状态（直接写字段，避免把读到的值回写设置）
            _signInGameBiz = gameBiz;
            _autoSignInEnabled = _autoSignInService.IsEnabled(gameBiz);
            OnPropertyChanged(nameof(AutoSignInEnabled));
            _gameRecordService.IsHoyolab = gameBiz.Server is "global";
            GameRecordRole = _gameRecordService.GetLastSelectGameRecordRoleOrTheFirstOne(gameBiz);
            if (GameRecordRole is null)
            {
                this.Visibility = Visibility.Collapsed;
                return;
            }
            this.Visibility = Visibility.Visible;
            _statusLoaded = false;
            ErrorMessage = null;
            // 自动签到不再在切换游戏时触发，统一由软件启动后的批量任务完成（AutoSignInService.RunStartupBatchAsync）。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize sign-in button failed (Biz: {GameBiz})", CurrentGameId?.GameBiz);
        }
    }



    /// <summary>Flyout 首次打开时懒加载签到状态。</summary>
    private void Flyout_SignIn_Opened(object sender, object e)
    {
        if (!_statusLoaded)
        {
            RefreshSignInStatusCommand.Execute(null);
        }
    }



    /// <summary>拉取并刷新签到卡片数据（奖励日历、今日状态、补签信息）。</summary>
    [RelayCommand]
    private async Task RefreshSignInStatusAsync()
    {
        if (GameRecordRole is null)
        {
            return;
        }
        try
        {
            ErrorMessage = null;
            SignInStatus status = await _signInService.GetSignInStatusAsync(GameRecordRole);
            _resignInfo = status.ResignInfo;
            IsTodaySigned = status.Info.IsSign;
            _totalSignDay = status.Info.TotalSignDay;
            TotalSignDaysHint = string.Format(Lang.SignInButton_SignedInForDays, _totalSignDay);

            Awards.Clear();
            int day = 0;
            foreach (var award in status.Reward.Awards)
            {
                var view = SignInAwardView.Create(award, day);
                // 索引小于已签天数的天数标记为已领取
                view.IsClaimed = day < status.Info.TotalSignDay;
                Awards.Add(view);
                day++;
            }
            _statusLoaded = true;
        }
        catch (miHoYoApiException ex)
        {
            ErrorMessage = $"Error Code: {ex.ReturnCode}";
            _logger.LogError(ex, "Refresh sign-in status failed (Biz: {GameBiz}, Uid: {Uid})", GameRecordRole?.GameBiz, GameRecordRole?.Uid);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Refresh sign-in status failed (Biz: {GameBiz}, Uid: {Uid})", GameRecordRole?.GameBiz, GameRecordRole?.Uid);
        }
    }



    /// <summary>执行今日签到，成功后刷新卡片。</summary>
    [RelayCommand]
    private async Task SignInAsync()
    {
        if (GameRecordRole is null)
        {
            return;
        }
        try
        {
            SignInActionResult result = await _signInService.ClaimSignInAsync(GameRecordRole);
            ShowResultToast(result, isResign: false);
            if (result is SignInActionResult.Success or SignInActionResult.AlreadySigned)
            {
                await RefreshSignInStatusAsync();
            }
        }
        catch (Exception ex)
        {
            InAppToast.MainWindow?.Error(ex);
            _logger.LogError(ex, "Claim sign-in failed (Biz: {GameBiz}, Uid: {Uid})", GameRecordRole?.GameBiz, GameRecordRole?.Uid);
        }
    }



    /// <summary>确认消耗后执行补签，成功后刷新卡片。</summary>
    [RelayCommand]
    private async Task ReSignInAsync()
    {
        if (GameRecordRole is null)
        {
            return;
        }
        try
        {
            var dialog = new ContentDialog
            {
                Title = Lang.SignInButton_ReSign,
                Content = string.Format(Lang.SignInButton_ReSignConfirmContent, _resignInfo?.CoinCost ?? 0, _resignInfo?.CoinCount ?? 0),
                PrimaryButtonText = Lang.Common_Confirm,
                CloseButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };
            if (await dialog.ShowAsync() is not ContentDialogResult.Primary)
            {
                return;
            }
            SignInActionResult result = await _signInService.ClaimReSignInAsync(GameRecordRole);
            ShowResultToast(result, isResign: true);
            if (result is SignInActionResult.Success)
            {
                await RefreshSignInStatusAsync();
            }
        }
        catch (Exception ex)
        {
            InAppToast.MainWindow?.Error(ex);
            _logger.LogError(ex, "Claim re-sign-in failed (Biz: {GameBiz}, Uid: {Uid})", GameRecordRole?.GameBiz, GameRecordRole?.Uid);
        }
    }



    /// <summary>
    /// 根据结构化结果弹出 InAppToast 提示。
    /// </summary>
    /// <param name="result">签到 / 补签操作结果。</param>
    /// <param name="isResign">是否为补签操作，影响标题文案。</param>
    private static void ShowResultToast(SignInActionResult result, bool isResign)
    {
        var toast = InAppToast.MainWindow;
        if (toast is null)
        {
            return;
        }
        string actionTitle = isResign ? Lang.SignInButton_ReSign : Lang.SignInButton_SignIn;
        switch (result)
        {
            case SignInActionResult.Success:
                toast.Success(actionTitle, Lang.SignInButton_SignInSucceeded);
                break;
            case SignInActionResult.AlreadySigned:
                toast.Information(Lang.SignInButton_AlreadySignedToday);
                break;
            case SignInActionResult.CookieExpired:
                toast.Warning(Lang.Common_AccountError, Lang.SignInButton_LoginExpired);
                break;
            case SignInActionResult.RiskControl:
                toast.Warning(actionTitle, Lang.SignInButton_RiskControl);
                break;
            case SignInActionResult.NotEnoughCoin:
                toast.Warning(actionTitle, Lang.SignInButton_NotEnoughCoin);
                break;
            case SignInActionResult.ResignQuotaUsedUp:
                toast.Warning(actionTitle, Lang.SignInButton_ResignQuotaUsedUp);
                break;
            case SignInActionResult.NoResignDate:
                toast.Information(actionTitle, Lang.SignInButton_NoResignDate);
                break;
            case SignInActionResult.PleaseSignInFirst:
                toast.Warning(actionTitle, Lang.SignInButton_PleaseSignInFirst);
                break;
            default:
                toast.Error(actionTitle, Lang.SignInButton_SignInFailed);
                break;
        }
    }


}
