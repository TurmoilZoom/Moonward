using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Controls;
using Starward.Core.GameRecord;
using Starward.Features.Notification;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Notifications;

namespace Starward.Features.GameRecord.DailyNote.Notify;

/// <summary>
/// 实时便笺「提醒设置」对话框：左侧列出所有支持实时便笺的账号（跨游戏），右侧编辑所选账号的提醒项目，
/// 底栏是对所有账号生效的检查间隔、游戏运行时不弹出、测试通知与系统通知状态。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class DailyNoteNotifyDialog : ContentDialog
{

    /// <summary>可选的检查间隔（分钟）。</summary>
    private static readonly int[] IntervalOptions = [30, 60, 120];

    /// <summary>账号列表里各游戏的先后。</summary>
    private static readonly string[] GameOrder = [GameBiz.hk4e, GameBiz.hkrpg, GameBiz.nap, GameBiz.bh3];


    private readonly ILogger<DailyNoteNotifyDialog> _logger = AppConfig.GetLogger<DailyNoteNotifyDialog>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();

    private readonly DailyNoteNotifyService _dailyNoteNotifyService = AppConfig.GetService<DailyNoteNotifyService>();

    private bool _intervalInitialized;

    /// <summary>最近一次测试通知发送失败；再发成功或系统状态变化时清除。</summary>
    private bool _sendFailed;


    public DailyNoteNotifyDialog()
    {
        this.InitializeComponent();
        _roleIndicator = new ListViewSlidingIndicator(ListView_Roles, Grid_RoleIndicatorHost, Grid_RoleIndicatorFollower, Border_RoleIndicator);
        this.Loaded += DailyNoteNotifyDialog_Loaded;
    }


    /// <summary>账号列表的滑动选中指示条；持有引用，事件订阅随列表加载 / 卸载自行收放。</summary>
    private readonly ListViewSlidingIndicator _roleIndicator;


    /// <summary>打开时默认选中的账号（首页便笺正在显示的那个）；为空时选列表第一个。</summary>
    public GameRecordRole? InitialRole { get; set; }


    /// <summary>所有支持实时便笺的账号。</summary>
    public ObservableCollection<DailyNoteNotifyRoleView> Roles { get; } = new();


    /// <summary>所选账号各可提醒项目的设置行。</summary>
    public ObservableCollection<DailyNoteNotifyItemView> Items { get; } = new();


    /// <summary>右侧正在编辑的账号。</summary>
    public DailyNoteNotifyRoleView? SelectedRole { get; set => SetProperty(ref field, value); }


    /// <summary>有游戏在运行时不弹出横幅，直接进通知中心。对所有账号生效。</summary>
    public bool SilentWhenGameRunning
    {
        get => AppConfig.DailyNoteNotifySilentWhenGameRunning;
        set
        {
            if (AppConfig.DailyNoteNotifySilentWhenGameRunning != value)
            {
                AppConfig.DailyNoteNotifySilentWhenGameRunning = value;
                OnPropertyChanged();
            }
        }
    }


    /// <summary>底栏的提示色文字：系统关闭了通知，或测试通知发送失败。</summary>
    public string WarningText { get; set => SetProperty(ref field, value); } = "";

    /// <summary>有提示色文字时显示它，否则显示运行条件说明。</summary>
    public Visibility WarningVisibility { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;

    /// <summary>运行条件说明的可见性，与 <see cref="WarningVisibility"/> 互斥。</summary>
    public Visibility HintVisibility { get; set => SetProperty(ref field, value); } = Visibility.Visible;



    private void DailyNoteNotifyDialog_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeRoles();
        InitializeIntervalOptions();
        UpdateStatus();
    }


    /// <summary>
    /// 列出所有支持实时便笺的账号（按游戏、区服排列），选中 <see cref="InitialRole"/>。
    /// </summary>
    private void InitializeRoles()
    {
        try
        {
            Roles.Clear();
            IEnumerable<GameRecordRole> roles = _gameRecordService.GetAllGameRoles()
                .Where(r => !string.IsNullOrWhiteSpace(r.GameBiz) && GameFeatureConfig.FromGameBiz(r.GameBiz).SupportDailyNote)
                .OrderBy(r => GetGameIndex(r.GameBiz))
                .ThenBy(r => r.GameBiz, StringComparer.Ordinal);
            foreach (GameRecordRole role in roles)
            {
                Roles.Add(new DailyNoteNotifyRoleView(role, DailyNoteNotifyConfigStore.Load(role.GameBiz, role.Uid).HasEnabledRule));
            }
            DailyNoteNotifyRoleView? initial = Roles.FirstOrDefault(x => x.IsSameRole(InitialRole)) ?? Roles.FirstOrDefault();
            if (initial is not null)
            {
                ListView_Roles.SelectedItem = initial;
                ListView_Roles.ScrollIntoView(initial);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize daily note notify roles failed.");
        }
    }


    /// <summary>
    /// 游戏在账号列表里的排序位置，未列出的游戏排最后。
    /// </summary>
    /// <param name="biz">战绩角色的区服。</param>
    /// <returns>排序位置。</returns>
    private static int GetGameIndex(string? biz)
    {
        int index = Array.IndexOf(GameOrder, new GameBiz(biz).Game);
        return index >= 0 ? index : GameOrder.Length;
    }


    /// <summary>
    /// 换了账号：右侧换成该账号的提醒项目。
    /// </summary>
    private void ListView_Roles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: DailyNoteNotifyRoleView role })
        {
            SelectedRole = role;
            LoadItems(role.Role);
            _ = EnsureDailyNoteAsync(role.Role);
        }
    }


    /// <summary>
    /// 读取账号的提醒配置，生成各项目的设置行。阈值上限与「当前」文字取自本次运行最近一次拿到的便笺。
    /// </summary>
    /// <param name="role">要编辑的账号。</param>
    private void LoadItems(GameRecordRole role)
    {
        Items.Clear();
        DailyNoteNotifyConfig config = DailyNoteNotifyConfigStore.Load(role.GameBiz, role.Uid);
        object? note = _dailyNoteNotifyService.GetLastSnapshot(role);
        foreach (DailyNoteNotifyItem item in DailyNoteNotifyItems.GetItems(role.GameBiz))
        {
            DailyNoteNotifyRule? rule = config.Rules.FirstOrDefault(x => x.Item == item);
            DailyNoteMetric? metric = DailyNoteNotifyItems.GetMetric(item, note);
            // 拿到了便笺却取不出数值 = 这个号还没解锁该项目（如尘歌壶），不显示；
            // 万一以前开过就照常显示，好让用户关掉。没有便笺时无从判断，先都显示
            if (note is not null && metric is null && rule?.Enabled != true)
            {
                continue;
            }
            // 回调绑定到这个账号：切换账号时旧行若还提交了一次值，也只写回它自己的账号
            Items.Add(new DailyNoteNotifyItemView(item, DailyNoteNotifyService.GetDisplayName(item), metric,
                                                  rule?.Enabled ?? false, rule?.Threshold, view => OnItemChanged(role, view)));
        }
    }


    /// <summary>
    /// 本次运行还没拿到过这个账号的便笺时（刚启动、后台还没查到它），拉一次再刷新右侧：
    /// 用来隐藏没解锁的项目，并补上「当前」数值与阈值上限。5 分钟内复用战绩服务的缓存。
    /// </summary>
    /// <param name="role">刚选中的账号。</param>
    private async Task EnsureDailyNoteAsync(GameRecordRole role)
    {
        if (_dailyNoteNotifyService.GetLastSnapshot(role) is not null)
        {
            return;
        }
        try
        {
            object note = await _dailyNoteNotifyService.GetDailyNoteAsync(role, forceUpdate: false);
            // 顺带交给提醒判定；达到阈值且开着提醒的，此时会发通知
            _dailyNoteNotifyService.ReportSnapshot(role, note);
            // 拉取期间用户可能已换了账号
            if (SelectedRole?.IsSameRole(role) == true)
            {
                LoadItems(role);
            }
        }
        catch (Exception ex)
        {
            // 登录失效、风控等拉不到时保持全部显示，不影响设置
            _logger.LogWarning(ex, "Fetch daily note for notify dialog failed ({biz}, {uid}).", role.GameBiz, role.Uid);
        }
    }


    /// <summary>
    /// 某一行的开关或阈值被改动：写回配置，并让后台提醒立即按新配置判定。
    /// </summary>
    /// <param name="role">这一行所属的账号。</param>
    /// <param name="view">被改动的设置行。</param>
    private void OnItemChanged(GameRecordRole role, DailyNoteNotifyItemView view)
    {
        try
        {
            DailyNoteNotifyConfig config = DailyNoteNotifyConfigStore.Load(role.GameBiz, role.Uid);
            DailyNoteNotifyRule rule = config.GetOrAddRule(view.Item);
            rule.Enabled = view.Enabled;
            rule.Threshold = view.ThresholdValue;
            DailyNoteNotifyConfigStore.Save(role.GameBiz, role.Uid, config);
            if (Roles.FirstOrDefault(x => x.IsSameRole(role)) is { } roleView)
            {
                roleView.HasReminder = config.HasEnabledRule;
            }
            _dailyNoteNotifyService.NotifyConfigChanged(role);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save daily note notify config failed ({biz}, {uid}).", role.GameBiz, role.Uid);
        }
    }


    /// <summary>
    /// 生成间隔选项并选中当前值；当前值不在选项里（手动改过数据库）时选最接近的。
    /// </summary>
    private void InitializeIntervalOptions()
    {
        _intervalInitialized = false;
        try
        {
            int current = AppConfig.DailyNoteNotifyIntervalMinutes;
            ComboBox_Interval.Items.Clear();
            int selected = 0;
            for (int i = 0; i < IntervalOptions.Length; i++)
            {
                ComboBox_Interval.Items.Add(new ComboBoxItem
                {
                    Content = string.Format(Lang.SystemNotificationSetting_IntervalMinutes, IntervalOptions[i]),
                    Tag = IntervalOptions[i],
                });
                if (Math.Abs(IntervalOptions[i] - current) < Math.Abs(IntervalOptions[selected] - current))
                {
                    selected = i;
                }
            }
            ComboBox_Interval.SelectedIndex = selected;
        }
        finally
        {
            _intervalInitialized = true;
        }
    }


    private void ComboBox_Interval_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_intervalInitialized && ComboBox_Interval.SelectedItem is ComboBoxItem { Tag: int minutes })
        {
            AppConfig.DailyNoteNotifyIntervalMinutes = minutes;
        }
    }


    /// <summary>
    /// 发一条测试通知；点它会打开主窗口。
    /// </summary>
    [RelayCommand]
    private void SendTestNotification()
    {
        _sendFailed = !SystemNotificationService.Show(new SystemNotification
        {
            Title = "Moonward",
            Lines = [Lang.SystemNotificationSetting_TestMessage],
            LaunchUri = NotificationActivation.BuildOpenUri(null, 0),
            Tag = "test",
        });
        UpdateStatus();
    }


    /// <summary>
    /// 打开 Windows「通知」设置页。
    /// </summary>
    [RelayCommand]
    private async Task OpenSystemNotificationSettingsAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("ms-settings:notifications"));
    }


    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }


    /// <summary>
    /// 底栏状态：测试失败或 Windows 关闭了本应用的通知时显示提示色文字，否则显示运行条件说明。
    /// </summary>
    private void UpdateStatus()
    {
        NotificationSetting? setting = SystemNotificationService.GetSetting();
        bool systemDisabled = setting is not null and not NotificationSetting.Enabled;
        WarningText = _sendFailed ? Lang.SystemNotificationSetting_SendFailed
                    : systemDisabled ? Lang.DailyNoteNotify_SystemDisabled
                    : "";
        bool warning = !string.IsNullOrEmpty(WarningText);
        WarningVisibility = warning ? Visibility.Visible : Visibility.Collapsed;
        HintVisibility = warning ? Visibility.Collapsed : Visibility.Visible;
    }

}
