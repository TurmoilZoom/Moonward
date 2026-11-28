using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Starward.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.SignIn;
using Starward.Core.HoYoPlay;
using Starward.Features.GameRecord.HomeRole;
using Starward.Features.Setting;
using Starward.Features.ViewHost;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
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

    /// <summary>当前显示的签到状态（日历、今日已签、累计天数）属于哪个角色；没有显示时为 0。</summary>
    private long _statusRoleUid;

    /// <summary>最近一次拉取的累计签到天数，语言切换时用于重算 <see cref="TotalSignDaysHint"/>。</summary>
    private int _totalSignDay;

    /// <summary>问号提示（含右下角超链接）打开时，阻止签到 Flyout 被外层点击关掉。</summary>
    private bool _keepFlyoutOpenForTooltip;


    /// <summary>初始化控件，默认隐藏，待解析到有效角色后再显示。</summary>
    public SignInButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
        HomeRolePicker_SignIn.RoleSelected += HomeRolePicker_SignIn_RoleSelected;
        InstantTooltip.SetActionCallback(Border_AutoSignInHint, OpenStartAtLoginSetting);
        InstantTooltip.SetOpenChangedCallback(Border_AutoSignInHint, OnAutoSignInHintTooltipOpenChanged);
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


    /// <summary>正在拉取签到状态 / 奖励列表，控制日历区加载动画。</summary>
    public bool IsLoading { get; set => SetProperty(ref field, value); }


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
                if (value)
                {
                    _autoSignInService.RequestImmediateCheck();
                }
            }
        }
    }



    /// <summary>
    /// 标题栏签到通知铃铛的图标：不通知时显示划掉的铃铛，不点开菜单也能看出状态。
    /// </summary>
    public string AutoSignInNotifyGlyph => AppConfig.AutoSignInNotifyMode is AutoSignInNotifyMode.Off ? "" : "";



    /// <summary>
    /// 签到通知铃铛的悬停提示，带上当前选项（如「签到通知 · 仅失败时」）。
    /// </summary>
    public string AutoSignInNotifyTooltip => $"{Lang.SignInButton_Notify} · {GetNotifyModeText(AppConfig.AutoSignInNotifyMode)}";



    /// <summary>
    /// 通知方式对应的菜单文字。
    /// </summary>
    /// <param name="mode">通知方式。</param>
    /// <returns>本地化后的选项文字。</returns>
    private static string GetNotifyModeText(AutoSignInNotifyMode mode) => mode switch
    {
        AutoSignInNotifyMode.Off => Lang.SignInButton_NotifyOff,
        AutoSignInNotifyMode.All => Lang.SignInButton_NotifyAll,
        _ => Lang.SignInButton_NotifyFailures,
    };



    /// <summary>
    /// 签到通知菜单打开时按当前设置勾选对应项。
    /// </summary>
    private void MenuFlyout_SignInNotify_Opening(object sender, object e)
    {
        AutoSignInNotifyMode mode = AppConfig.AutoSignInNotifyMode;
        RadioMenuFlyoutItem_NotifyOff.IsChecked = mode is AutoSignInNotifyMode.Off;
        RadioMenuFlyoutItem_NotifyFailures.IsChecked = mode is AutoSignInNotifyMode.FailuresOnly;
        RadioMenuFlyoutItem_NotifyAll.IsChecked = mode is AutoSignInNotifyMode.All;
    }



    /// <summary>
    /// 选中签到通知方式（对所有游戏生效）：保存设置并刷新铃铛图标与提示。菜单项的 Tag 是 <see cref="AutoSignInNotifyMode"/> 的取值。
    /// </summary>
    private void RadioMenuFlyoutItem_SignInNotify_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }
            && int.TryParse(tag, out int value)
            && Enum.IsDefined((AutoSignInNotifyMode)value)
            && (AutoSignInNotifyMode)value != AppConfig.AutoSignInNotifyMode)
        {
            AppConfig.AutoSignInNotifyMode = (AutoSignInNotifyMode)value;
            OnPropertyChanged(nameof(AutoSignInNotifyGlyph));
            OnPropertyChanged(nameof(AutoSignInNotifyTooltip));
        }
    }



    /// <summary>控件加载时按当前游戏初始化可见性与角色。</summary>
    private void SignInButton_Loaded(object sender, RoutedEventArgs e)
    {
        // 只在加载期间订阅：页面换掉（如切换游戏）后旧卡片的原生对象会被释放，而构造函数里的订阅要等 GC 才失效，
        // 消息一来就会在已释放的对象上访问属性，抛 ObjectDisposedException 导致崩溃
        if (!WeakReferenceMessenger.Default.IsRegistered<HomeGameRecordRoleChangedMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<HomeGameRecordRoleChangedMessage>(this, OnHomeRoleChanged);
        }
        // 静止显示首帧，避免 AutoPlay 常驻动画
        Lottie_SignIn.SetProgress(0);
        InitializeForCurrentGame();
    }


    /// <summary>控件卸载时清理状态，避免切换页面后残留旧数据。</summary>
    private void SignInButton_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<HomeGameRecordRoleChangedMessage>(this);
        Lottie_SignIn.Stop();
        GameRecordRole = null;
        _resignInfo = null;
        _statusLoaded = false;
        _statusRoleUid = 0;
        Awards.Clear();
        ErrorMessage = null;
        IsLoading = false;
    }


    /// <summary>
    /// 悬浮时播放一次 Lottie 后停在末帧；离开后回到首帧。
    /// </summary>
    private void Button_SignIn_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = Lottie_SignIn.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 指针离开后停止动画并回到首帧。
    /// </summary>
    private void Button_SignIn_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Lottie_SignIn.Stop();
        Lottie_SignIn.SetProgress(0);
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
            GameBiz gameBiz = HomeGameRecordRole.GetRoleGameBiz(CurrentGameId.GameBiz);
            // 自动签到开关按游戏区分：切换游戏时同步开关状态（直接写字段，避免把读到的值回写设置）
            _signInGameBiz = gameBiz;
            _autoSignInEnabled = _autoSignInService.IsEnabled(gameBiz);
            OnPropertyChanged(nameof(AutoSignInEnabled));
            _gameRecordService.IsHoyolab = gameBiz.Server is "global";
            GameRecordRole = HomeGameRecordRole.Resolve(_gameRecordService, gameBiz);
            if (GameRecordRole is null)
            {
                this.Visibility = Visibility.Collapsed;
                return;
            }
            this.Visibility = Visibility.Visible;
            _statusLoaded = false;
            ErrorMessage = null;
            IsLoading = false;
            // 自动签到由常驻循环统一调度，切换游戏只同步开关状态，不在这里触发。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize sign-in button failed (Biz: {GameBiz})", CurrentGameId?.GameBiz);
        }
    }



    /// <summary>
    /// 账号列表打开前按当前区服重读角色，并选中签到卡片正在显示的那个。
    /// </summary>
    private void Flyout_SignInRole_Opening(object sender, object e)
    {
        if (string.IsNullOrEmpty(_signInGameBiz.Value))
        {
            return;
        }
        try
        {
            HomeRolePicker_SignIn.Load(_gameRecordService.GetGameRoles(_signInGameBiz), GameRecordRole?.Uid ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load home roles failed (Biz: {GameBiz})", _signInGameBiz);
        }
    }


    /// <summary>
    /// 在账号列表里选了角色：收起列表，换了角色就保存为首页卡片的角色（便笺和签到随后各自重新加载）。
    /// </summary>
    private void HomeRolePicker_SignIn_RoleSelected(object? sender, GameRecordRole role)
    {
        Flyout_SignInRole.Hide();
        if (string.IsNullOrEmpty(_signInGameBiz.Value) || role.Uid == GameRecordRole?.Uid)
        {
            return;
        }
        HomeGameRecordRole.Select(_signInGameBiz, role);
    }


    /// <summary>
    /// 首页卡片的角色换了（可能是在便笺卡片里换的）：重新解析角色，卡片开着就立即按新角色加载，否则等下次打开。
    /// </summary>
    /// <remarks>
    /// 显示中的签到状态先留着，等新角色的状态到了再整体替换：多数时候两个账号今天的状态一样，
    /// 先清空的话签到按钮、已签标记和累计天数会闪一下又变回原样。新角色加载失败时再清掉（见 <see cref="ClearStatusOfOtherRole"/>）。
    /// </remarks>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="message">切换到的区服与角色。</param>
    private void OnHomeRoleChanged(object _, HomeGameRecordRoleChangedMessage message)
    {
        if (message.GameBiz != _signInGameBiz || message.Role.Uid == GameRecordRole?.Uid)
        {
            return;
        }
        // 补签确认框要显示的消耗属于上一个角色，不能沿用（不影响界面）
        _resignInfo = null;
        InitializeForCurrentGame();
        if (Flyout_SignIn.IsOpen)
        {
            RefreshSignInStatusCommand.Execute(null);
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


    /// <summary>
    /// 问号提示打开时，点击提示气泡会被当成 Flyout 外部点击：仅这种情况取消关闭，以便点到右下角超链接。
    /// 提示改为点击触发后不会随指针移开自行消失，点其它位置时必须连同提示一起收起，否则 Flyout 永远关不掉。
    /// </summary>
    private void Flyout_SignIn_Closing(object sender, FlyoutBaseClosingEventArgs e)
    {
        if (!_keepFlyoutOpenForTooltip)
        {
            return;
        }

        if (InstantTooltip.IsPointerOverTooltip(XamlRoot))
        {
            e.Cancel = true;
            return;
        }

        InstantTooltip.Dismiss(XamlRoot);
    }


    /// <summary>
    /// 同步问号提示（含超链接）的打开状态，供 Flyout 决定是否拦截关闭。
    /// </summary>
    /// <param name="isOpen">提示是否打开。</param>
    private void OnAutoSignInHintTooltipOpenChanged(bool isOpen)
    {
        _keepFlyoutOpenForTooltip = isOpen;
    }


    /// <summary>
    /// 关闭签到卡片并跳转到设置「常规」页的开机启动一节。
    /// </summary>
    private void OpenStartAtLoginSetting()
    {
        _keepFlyoutOpenForTooltip = false;
        Flyout_SignIn.Hide();
        WeakReferenceMessenger.Default.Send(new MainViewNavigateMessage(typeof(SettingPage), SettingPage.StartAtLoginSection));
    }



    /// <summary>拉取并刷新签到卡片数据（奖励日历、今日状态、补签信息）。</summary>
    [RelayCommand]
    private async Task RefreshSignInStatusAsync()
    {
        if (GameRecordRole is not { } role || IsLoading)
        {
            return;
        }
        IsLoading = true;
        try
        {
            ErrorMessage = null;
            SignInStatus status = await _signInService.GetSignInStatusAsync(role);
            // 请求期间在首页卡片里换了账号：旧角色的状态不再写回，以免盖掉新角色的
            if (role != GameRecordRole)
            {
                return;
            }
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
            _statusRoleUid = role.Uid;
            _statusLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh sign-in status failed (Biz: {GameBiz}, Uid: {Uid})", role.GameBiz, role.Uid);
            // 请求期间已换了账号：旧角色的失败不再显示
            if (role != GameRecordRole)
            {
                return;
            }
            ErrorMessage = ex switch
            {
                miHoYoApiException apiException => GameRecordPage.GetMiHoYoApiExceptionMessage(apiException),
                HttpRequestException httpException => MiHoYoApiErrorFeedbackFactory.Create(httpException, MiHoYoApiContext.SignIn).Message,
                _ => ex.Message,
            };
            ClearStatusOfOtherRole(role);
        }
        finally
        {
            IsLoading = false;
        }
    }


    /// <summary>
    /// 显示中的签到状态不属于这个角色时清空（切换账号后为了不闪而保留的上一个角色的状态），
    /// 免得新角色加载失败时把上一个角色的签到情况当成它的。
    /// </summary>
    /// <param name="role">当前角色。</param>
    private void ClearStatusOfOtherRole(GameRecordRole role)
    {
        if (_statusRoleUid == role.Uid)
        {
            return;
        }
        _resignInfo = null;
        Awards.Clear();
        IsTodaySigned = false;
        TotalSignDaysHint = null;
        _statusRoleUid = 0;
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
            SignInActionResponse result = await _signInService.ClaimSignInAsync(GameRecordRole);
            ShowResultToast(result, isResign: false);
            if (result.Kind is SignInActionResult.Success or SignInActionResult.AlreadySigned)
            {
                await RefreshSignInStatusAsync();
            }
        }
        catch (HttpRequestException ex)
        {
            ShowApiFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.SignIn), _signInGameBiz);
            _logger.LogError(ex, "Claim sign-in failed (Biz: {GameBiz}, Uid: {Uid})", GameRecordRole?.GameBiz, GameRecordRole?.Uid);
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
            SignInActionResponse result = await _signInService.ClaimReSignInAsync(GameRecordRole);
            ShowResultToast(result, isResign: true);
            if (result.Kind is SignInActionResult.Success)
            {
                await RefreshSignInStatusAsync();
            }
        }
        catch (HttpRequestException ex)
        {
            ShowApiFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.SignIn), _signInGameBiz);
            _logger.LogError(ex, "Claim re-sign-in failed (Biz: {GameBiz}, Uid: {Uid})", GameRecordRole?.GameBiz, GameRecordRole?.Uid);
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
    private void ShowResultToast(SignInActionResponse result, bool isResign)
    {
        var toast = InAppToast.MainWindow;
        if (toast is null)
        {
            return;
        }
        string actionTitle = isResign ? Lang.SignInButton_ReSign : Lang.SignInButton_SignIn;
        GameBiz biz = _signInGameBiz;
        switch (result.Kind)
        {
            case SignInActionResult.Success:
                toast.Success(actionTitle, Lang.SignInButton_SignInSucceeded);
                break;
            case SignInActionResult.AlreadySigned:
                toast.Information(actionTitle, FormatResultMessage(Lang.SignInButton_AlreadySignedToday, result.ReturnCode));
                break;
            case SignInActionResult.CookieExpired:
                ShowApiFeedback(MiHoYoApiErrorFeedbackFactory.Create(new miHoYoApiException(result.ReturnCode ?? SignInReturnCode.NotLoggedIn, result.ResponseMessage), MiHoYoApiContext.SignIn), biz);
                break;
            case SignInActionResult.RiskControl:
                // 捕获当前角色，避免 Toast 停留期间切换游戏后打开错误账号
                GameRecordRole? roleForVerify = GameRecordRole;
                toast.ShowWithButton(InfoBarSeverity.Warning, actionTitle, Lang.SignInButton_RiskControl, Lang.HoyolabToolboxPage_VerifyAccount, () => GameRecordAccountRecovery.RequestVerifyAccount(biz, roleForVerify));
                break;
            case SignInActionResult.NotEnoughCoin:
                toast.Warning(actionTitle, FormatResultMessage(Lang.SignInButton_NotEnoughCoin, result.ReturnCode));
                break;
            case SignInActionResult.ResignQuotaUsedUp:
                toast.Warning(actionTitle, FormatResultMessage(Lang.SignInButton_ResignQuotaUsedUp, result.ReturnCode));
                break;
            case SignInActionResult.NoResignDate:
                toast.Information(actionTitle, FormatResultMessage(Lang.SignInButton_NoResignDate, result.ReturnCode));
                break;
            case SignInActionResult.PleaseSignInFirst:
                toast.Warning(actionTitle, FormatResultMessage(Lang.SignInButton_PleaseSignInFirst, result.ReturnCode));
                break;
            default:
                ShowApiFeedback(MiHoYoApiErrorFeedbackFactory.Create(new miHoYoApiException(result.ReturnCode ?? -1, result.ResponseMessage ?? Lang.SignInButton_SignInFailed), MiHoYoApiContext.SignIn), biz);
                break;
        }
    }



    /// <summary>
    /// 显示签到请求的统一 API 反馈，并复用战绩登录和账号验证入口。
    /// </summary>
    /// <param name="feedback">已分类的 API 错误反馈。</param>
    /// <param name="preferredBiz">验证账号时优先使用的游戏区服。</param>
    /// <param name="preferredRole">触发错误时的角色。</param>
    private void ShowApiFeedback(MiHoYoApiErrorFeedback feedback, GameBiz? preferredBiz = null, GameRecordRole? preferredRole = null)
    {
        preferredRole ??= GameRecordRole;
        MiHoYoApiErrorFeedbackFactory.Show(feedback, action =>
        {
            if (action is MiHoYoApiRecoveryAction.Relogin)
            {
                GameRecordAccountRecovery.RequestOpenLogin();
            }
            else if (action is MiHoYoApiRecoveryAction.VerifyAccount)
            {
                GameRecordAccountRecovery.RequestVerifyAccount(preferredBiz, preferredRole);
            }
        });
    }



    /// <summary>
    /// 将已知签到结果的本地化文案附加 retcode，便于用户反馈问题。
    /// </summary>
    /// <param name="message">本地化结果文案。</param>
    /// <param name="returnCode">接口返回码；可为 null。</param>
    /// <returns>包含状态码的显示文案。</returns>
    private static string FormatResultMessage(string message, int? returnCode)
    {
        return returnCode.HasValue ? $"{message} ({returnCode.Value})" : message;
    }


}
