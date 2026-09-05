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


    public DailyNoteButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
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
            GameBiz gameBiz = CurrentGameId.GameBiz;
            if (gameBiz.Server is "bilibili")
            {
                gameBiz = $"{gameBiz.Game}_cn";
            }
            _gameRecordService.IsHoyolab = gameBiz.Server is "global";
            GameRecordRole = _gameRecordService.GetLastSelectGameRecordRoleOrTheFirstOne(gameBiz);
            if (GameRecordRole is not null)
            {
                this.Visibility = Visibility.Visible;
                ClearRecoveryState();
                await _gameRecordService.UpdateDeviceFpAsync();
                if (IsBH3Enabled)
                {
                    BH3DailyNote = await _gameRecordService.GetBH3DailyNoteAsync(GameRecordRole, forceUpdate.Value);
                }
                if (IsGenshinEnabled)
                {
                    GenshinDailyNote = await _gameRecordService.GetGenshinDailyNoteAsync(GameRecordRole, forceUpdate.Value);
                }
                if (IsStarRailEnabled)
                {
                    StarRailDailyNote = await _gameRecordService.GetStarRailDailyNoteAsync(GameRecordRole, forceUpdate.Value);
                }
                if (IsZZZEnabled)
                {
                    ZZZDailyNote = await _gameRecordService.GetZZZDailyNoteAsync(GameRecordRole, forceUpdate.Value);
                }
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
        return transformer?.RecoveryTime?.Reached is true ? Visibility.Visible : Visibility.Collapsed;
    }


    /// <summary>
    /// 参量质变仪冷却中。
    /// </summary>
    /// <param name="transformer">参量质变仪，未拥有时为 null。</param>
    /// <returns>冷却中时为 <see cref="Visibility.Visible"/>。</returns>
    public Visibility GenshinTransformerCoolingDownVisibility(Transformer? transformer)
    {
        return transformer?.RecoveryTime is { Reached: false } ? Visibility.Visible : Visibility.Collapsed;
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
