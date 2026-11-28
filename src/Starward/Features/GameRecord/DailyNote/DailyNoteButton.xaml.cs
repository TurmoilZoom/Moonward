using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.BH3.DailyNote;
using Starward.Core.GameRecord.Genshin.DailyNote;
using Starward.Core.GameRecord.StarRail.DailyNote;
using Starward.Core.GameRecord.ZZZ.DailyNote;
using Starward.Core.HoYoPlay;
using Starward.Features.GameRecord.DailyNote.Notify;
using Starward.Features.GameRecord.HomeRole;
using Starward.Features.Setting;
using System;
using System.Net.Http;
using System.Threading.Tasks;


namespace Starward.Features.GameRecord.DailyNote;

[INotifyPropertyChanged]
public sealed partial class DailyNoteButton : UserControl
{

    private readonly ILogger<DailyNoteButton> _logger = AppConfig.GetLogger<DailyNoteButton>();


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    private readonly DailyNoteNotifyService _dailyNoteNotifyService = AppConfig.GetService<DailyNoteNotifyService>();


    public DailyNoteButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
        HomeRolePicker_DailyNote.RoleSelected += HomeRolePicker_DailyNote_RoleSelected;
    }


    /// <summary>
    /// 语言切换后刷新 x:Bind 绑定的 Tooltip 与 Flyout 文案。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="__">语言变更消息（未使用）。</param>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
    }



    public GameId CurrentGameId
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsBH3Enabled));
                OnPropertyChanged(nameof(IsGenshinEnabled));
                OnPropertyChanged(nameof(IsStarRailEnabled));
                OnPropertyChanged(nameof(IsZZZEnabled));
            }
        }
    }



    public bool IsBH3Enabled => CurrentGameId?.GameBiz.Game is GameBiz.bh3;

    public bool IsGenshinEnabled => CurrentGameId?.GameBiz.Game is GameBiz.hk4e;

    public bool IsStarRailEnabled => CurrentGameId?.GameBiz.Game is GameBiz.hkrpg;

    public bool IsZZZEnabled => CurrentGameId?.GameBiz.Game is GameBiz.nap;



    private GameRecordRole? GameRecordRole { get; set => SetProperty(ref field, value); }


    public BH3DailyNote BH3DailyNote { get; set => SetProperty(ref field, value); }

    public GenshinDailyNote GenshinDailyNote { get; set => SetProperty(ref field, value); }

    public StarRailDailyNote StarRailDailyNote { get; set => SetProperty(ref field, value); }

    public ZZZDailyNote ZZZDailyNote { get; set => SetProperty(ref field, value); }


    public string? ErrorMessage { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 便签请求失败后由 Factory 给出的恢复动作；成功刷新或卸载时清空。
    /// </summary>
    private MiHoYoApiRecoveryAction RecoveryAction
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ShowVerifyAccount));
                OnPropertyChanged(nameof(ShowRelogin));
            }
        }
    }


    /// <summary>战绩风控（如 10035）时在 Flyout 显示「验证账号」。</summary>
    public bool ShowVerifyAccount => RecoveryAction is MiHoYoApiRecoveryAction.VerifyAccount;


    /// <summary>登录失效时在 Flyout 显示「重新登录」。</summary>
    public bool ShowRelogin => RecoveryAction is MiHoYoApiRecoveryAction.Relogin;


    /// <summary>重新登录按钮文案；该键未进 Designer，经 ResourceManager 读取。</summary>
    public string ReloginButtonText => Lang.ResourceManager.GetString("MiHoYoApiError_Relogin", Lang.Culture) ?? "MiHoYoApiError_Relogin";



    private void Button_DailyNote_Loaded(object sender, RoutedEventArgs e)
    {
        // 只在加载期间订阅：页面换掉（如切换游戏）后旧卡片的原生对象会被释放，而构造函数里的订阅要等 GC 才失效，
        // 消息一来就会在已释放的对象上访问属性，抛 ObjectDisposedException 导致崩溃
        if (!WeakReferenceMessenger.Default.IsRegistered<HomeGameRecordRoleChangedMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<HomeGameRecordRoleChangedMessage>(this, OnHomeRoleChanged);
        }
        // 静止显示首帧，避免 AutoPlay 常驻动画
        Lottie_DailyNote.SetProgress(0);
        if (CurrentGameId is null)
        {
            return;
        }
        if (!GameFeatureConfig.FromGameId(CurrentGameId).SupportDailyNote)
        {
            return;
        }
        RefreshDailyNoteCommand.Execute(false);
    }



    private void Button_DailyNote_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<HomeGameRecordRoleChangedMessage>(this);
        Lottie_DailyNote.Stop();
        GameRecordRole = null;
        BH3DailyNote = null!;
        GenshinDailyNote = null!;
        StarRailDailyNote = null!;
        ZZZDailyNote = null!;
        ClearRecoveryState();
    }


    /// <summary>
    /// 悬浮时播放一次 Lottie 后停在末帧；离开后回到首帧。
    /// </summary>
    private void Button_DailyNote_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = Lottie_DailyNote.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 指针离开后停止动画并回到首帧。
    /// </summary>
    private void Button_DailyNote_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Lottie_DailyNote.Stop();
        Lottie_DailyNote.SetProgress(0);
    }



    [RelayCommand]
    private async Task RefreshDailyNoteAsync(bool? forceUpdate)
    {
        try
        {
            if (!forceUpdate.HasValue)
            {
                forceUpdate = true;
            }
            GameBiz gameBiz = HomeGameRecordRole.GetRoleGameBiz(CurrentGameId.GameBiz);
            _gameRecordService.IsHoyolab = gameBiz.Server is "global";
            GameRecordRole? role = HomeGameRecordRole.Resolve(_gameRecordService, gameBiz);
            GameRecordRole = role;
            if (role is not null)
            {
                this.Visibility = Visibility.Visible;
                ClearRecoveryState();
                await _gameRecordService.UpdateDeviceFpAsync();
                // 请求期间可能已在首页卡片里换了账号（新的一次刷新已经开始），旧角色的结果不再写回，以免盖掉新角色的便笺
                if (IsBH3Enabled)
                {
                    BH3DailyNote result = await _gameRecordService.GetBH3DailyNoteAsync(role, forceUpdate.Value);
                    if (role != GameRecordRole)
                    {
                        return;
                    }
                    BH3DailyNote = result;
                }
                if (IsGenshinEnabled)
                {
                    GenshinDailyNote result = await _gameRecordService.GetGenshinDailyNoteAsync(role, forceUpdate.Value);
                    if (role != GameRecordRole)
                    {
                        return;
                    }
                    GenshinDailyNote = result;
                }
                if (IsStarRailEnabled)
                {
                    StarRailDailyNote result = await _gameRecordService.GetStarRailDailyNoteAsync(role, forceUpdate.Value);
                    if (role != GameRecordRole)
                    {
                        return;
                    }
                    StarRailDailyNote = result;
                }
                if (IsZZZEnabled)
                {
                    ZZZDailyNote result = await _gameRecordService.GetZZZDailyNoteAsync(role, forceUpdate.Value);
                    if (role != GameRecordRole)
                    {
                        return;
                    }
                    ZZZDailyNote = result;
                }
                // 交给便笺提醒判定，免得后台为同一份数据再请求一次
                _dailyNoteNotifyService.ReportSnapshot(role, GetCurrentDailyNote());
            }
        }
        catch (Exception ex)
        {
            if (ex is miHoYoApiException or HttpRequestException)
            {
                var feedback = MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GameRecord);
                ErrorMessage = feedback.Message;
                RecoveryAction = feedback.RecoveryAction;
            }
            _logger.LogError(ex, "Refresh daily note failed (Biz: {GameBiz}, Server: {GameServer}, Uid: {Uid})", CurrentGameId?.GameBiz, GameRecordRole?.Region, GameRecordRole?.Uid);
        }
    }


    /// <summary>
    /// 账号列表打开前按当前区服重读角色，并选中便笺正在显示的那个。
    /// </summary>
    private void Flyout_DailyNoteRole_Opening(object sender, object e)
    {
        if (CurrentGameId is null)
        {
            return;
        }
        try
        {
            HomeRolePicker_DailyNote.Load(_gameRecordService.GetGameRoles(HomeGameRecordRole.GetRoleGameBiz(CurrentGameId.GameBiz)), GameRecordRole?.Uid ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load home roles failed (Biz: {GameBiz})", CurrentGameId.GameBiz);
        }
    }


    /// <summary>
    /// 在账号列表里选了角色：收起列表，换了角色就保存为首页卡片的角色（便笺和签到随后各自重新加载）。
    /// </summary>
    private void HomeRolePicker_DailyNote_RoleSelected(object? sender, GameRecordRole role)
    {
        Flyout_DailyNoteRole.Hide();
        if (CurrentGameId is null || role.Uid == GameRecordRole?.Uid)
        {
            return;
        }
        HomeGameRecordRole.Select(HomeGameRecordRole.GetRoleGameBiz(CurrentGameId.GameBiz), role);
    }


    /// <summary>
    /// 首页卡片的角色换了（可能是在签到卡片里换的）：清掉上一个角色的便笺，按新角色重新加载。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="message">切换到的区服与角色。</param>
    private void OnHomeRoleChanged(object _, HomeGameRecordRoleChangedMessage message)
    {
        if (CurrentGameId is null || message.GameBiz != HomeGameRecordRole.GetRoleGameBiz(CurrentGameId.GameBiz) || message.Role.Uid == GameRecordRole?.Uid)
        {
            return;
        }
        BH3DailyNote = null!;
        GenshinDailyNote = null!;
        StarRailDailyNote = null!;
        ZZZDailyNote = null!;
        ClearRecoveryState();
        RefreshDailyNoteCommand.Execute(false);
    }


    /// <summary>
    /// 打开官方战绩页，使用触发失败的角色完成风控校验。
    /// </summary>
    [RelayCommand]
    private void VerifyAccount()
    {
        GameRecordAccountRecovery.RequestVerifyAccount(CurrentGameId?.GameBiz, GameRecordRole);
    }


    /// <summary>
    /// 打开战绩登录入口以恢复登录态。
    /// </summary>
    [RelayCommand]
    private void Relogin()
    {
        GameRecordAccountRecovery.RequestOpenLogin();
    }


    /// <summary>
    /// 清空错误文案与恢复按钮状态。
    /// </summary>
    private void ClearRecoveryState()
    {
        ErrorMessage = null;
        RecoveryAction = MiHoYoApiRecoveryAction.None;
    }


    /// <summary>
    /// 打开「提醒设置」对话框，默认选中首页便笺正在显示的账号。先收起浮层，对话框与浮层不叠在一起。
    /// </summary>
    [RelayCommand]
    private async Task OpenNotifySettingsAsync()
    {
        XamlRoot xamlRoot = this.XamlRoot;
        Button_DailyNote.Flyout?.Hide();
        try
        {
            await new DailyNoteNotifyDialog { InitialRole = GameRecordRole, XamlRoot = xamlRoot }.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open daily note notify dialog failed.");
        }
    }


    /// <summary>
    /// 当前游戏已加载的便笺。
    /// </summary>
    /// <returns>便笺对象；未加载时为 null。</returns>
    private object? GetCurrentDailyNote()
    {
        if (IsGenshinEnabled)
        {
            return GenshinDailyNote;
        }
        if (IsStarRailEnabled)
        {
            return StarRailDailyNote;
        }
        if (IsZZZEnabled)
        {
            return ZZZDailyNote;
        }
        if (IsBH3Enabled)
        {
            return BH3DailyNote;
        }
        return null;
    }


    public static string ZZZMemberCardRemainingDaysToString(int remainingDays)
    {
        return string.Format(Lang.DailyNoteButton_0DaySRemaining, remainingDays);
    }


    /// <summary>
    /// 参量质变仪已冷却完毕，可再次使用。
    /// </summary>
    /// <param name="transformer">参量质变仪，未拥有时为 null。</param>
    /// <returns>可使用时为 <see cref="Visibility.Visible"/>。</returns>
    /// <remarks>x:Bind 函数绑定返回 bool 再隐式转 Visibility 会生成无法编译的代码，只能直接返回 Visibility。</remarks>
    public Visibility GenshinTransformerReadyVisibility(Transformer? transformer)
    {
        // 未解锁时接口仍返回 reached=true 的空冷却时间，须先判 Obtained，否则低等级账号会只剩一行没有图标和名称的「可使用」
        return transformer is { Obtained: true, RecoveryTime.Reached: true } ? Visibility.Visible : Visibility.Collapsed;
    }


    /// <summary>
    /// 参量质变仪冷却中。
    /// </summary>
    /// <param name="transformer">参量质变仪，未拥有时为 null。</param>
    /// <returns>冷却中时为 <see cref="Visibility.Visible"/>。</returns>
    public Visibility GenshinTransformerCoolingDownVisibility(Transformer? transformer)
    {
        return transformer is { Obtained: true, RecoveryTime.Reached: false } ? Visibility.Visible : Visibility.Collapsed;
    }


    /// <summary>
    /// 参量质变仪剩余冷却时间的显示文案。
    /// </summary>
    /// <param name="transformer">参量质变仪，未拥有时为 null。</param>
    /// <returns>形如「还剩 6 天」的文案；已冷却完毕或数据缺失时为空字符串。</returns>
    public string GenshinTransformerRecoveryToString(Transformer? transformer)
    {
        // 接口只在天/时/分中填一个非零值，按粒度从大到小取
        if (transformer?.RecoveryTime is not { Reached: false } time)
        {
            return string.Empty;
        }
        if (time.Day > 0)
        {
            return string.Format(Lang.TimeNode_RemainingDays, time.Day);
        }
        if (time.Hour > 0)
        {
            return string.Format(Lang.TimeNode_RemainingHours, time.Hour);
        }
        return string.Format(Lang.TimeNode_RemainingMinutes, time.Minute);
    }





}
