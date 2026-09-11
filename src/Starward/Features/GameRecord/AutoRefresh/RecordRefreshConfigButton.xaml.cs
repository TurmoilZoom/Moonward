using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core.GameRecord;
using Starward.Language;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 挂在各数据页右侧面板顶部那排小图标按钮最左边的「自动更新」配置按钮：一个按钮 + 一个配置浮层。
/// <para>
/// 配置粒度是「当前账号 + 本页数据板块」——同一个号的深渊和月报可以各配各的频率，
/// 不同号之间也互不影响。页面只需给 <see cref="GameRole"/> 赋值。
/// </para>
/// </summary>
[INotifyPropertyChanged]
public sealed partial class RecordRefreshConfigButton : UserControl
{

    private readonly ILogger<RecordRefreshConfigButton> _logger = AppConfig.GetLogger<RecordRefreshConfigButton>();

    private readonly AutoRecordRefreshService _service = AppConfig.GetService<AutoRecordRefreshService>();

    private RecordRefreshConfig _config = new();

    /// <summary>初始化期间不回写配置：ComboBox 在构建阶段会先抛出 -1 等过渡值。</summary>
    private bool _initialized;


    /// <summary>
    /// 后台自动更新成功写入本地库后触发，供所在数据页从数据库重载列表。
    /// </summary>
    public event EventHandler? LocalDataUpdated;


    public RecordRefreshConfigButton()
    {
        InitializeDayNames();
        InitializeComponent();
        Loaded += RecordRefreshConfigButton_Loaded;
        Unloaded += RecordRefreshConfigButton_Unloaded;
        _initialized = true;
    }


    private void RecordRefreshConfigButton_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Register<RecordRefreshCompletedMessage>(this, (_, message) => OnBackgroundCompleted(message));
    }


    private void RecordRefreshConfigButton_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<RecordRefreshCompletedMessage>(this);
    }


    /// <summary>
    /// 后台更新当前账号的本板块结束后：刷新红点；成功则通知页面重载本地数据。
    /// </summary>
    private void OnBackgroundCompleted(RecordRefreshCompletedMessage message)
    {
        if (GameRole is null || message.Uid != GameRole.Uid || message.GameBiz != GameRole.GameBiz || message.Item != Item)
        {
            return;
        }
        UpdateErrorState();
        if (message.Succeeded)
        {
            LocalDataUpdated?.Invoke(this, EventArgs.Empty);
        }
    }


    /// <summary>
    /// 当前页在前台时，后台自动更新成功后从本地库重载。页面卸载时自动取消。
    /// </summary>
    /// <param name="host">所在页面，用它的 Unloaded 解绑。</param>
    /// <param name="reloadFromLocal">只读本地库，不要再打网络。</param>
    public void WatchPage(FrameworkElement host, Action reloadFromLocal)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(reloadFromLocal);
        void OnUpdated(object? sender, EventArgs e) => reloadFromLocal();
        LocalDataUpdated += OnUpdated;
        RoutedEventHandler? onUnloaded = null;
        onUnloaded = (_, _) =>
        {
            host.Unloaded -= onUnloaded;
            LocalDataUpdated -= OnUpdated;
        };
        host.Unloaded += onUnloaded;
    }


    /// <summary>
    /// 本页对应的数据板块，在 XAML 上直接写死，如 <c>Item="ShiyuDefense"</c>。
    /// </summary>
    public RecordRefreshItem Item
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ItemName));
        }
    }


    /// <summary>
    /// 当前账号。页面在 <c>OnNavigatedTo</c> 里赋值；换号后配置会重新载入。
    /// </summary>
    public GameRecordRole? GameRole
    {
        get => field;
        set
        {
            field = value;
            LoadConfig();
        }
    }


    /// <summary>星期下拉项，索引与 <see cref="DayOfWeek"/> 一致（0 为周日）。</summary>
    public ObservableCollection<string> DayOfWeekNames { get; } = [];

    /// <summary>「几号」下拉项，索引 0 对应 1 号。</summary>
    public ObservableCollection<string> DayOfMonthNames { get; } = [];

    /// <summary>数据板块显示名，复用工具箱左侧导航的文案。</summary>
    public string ItemName => Item.GetDisplayName();

    /// <summary>配置归属的账号，展示成「昵称 · uid」。</summary>
    public string RoleName => GameRole is null ? string.Empty : $"{GameRole.Nickname} · {GameRole.Uid}";

    /// <summary>没有账号时不给配：配置本身就是按账号存的。</summary>
    public bool HasRole => GameRole is not null;

    /// <summary>这个账号的这个板块留下过异常记录：按钮右上角点个红点。</summary>
    public bool HasError { get; private set => SetProperty(ref field, value); }


    /// <summary>本账号在本板块上是否开启自动更新。</summary>
    public bool AutoRefreshEnabled
    {
        get => _config.Enabled;
        set
        {
            if (_config.Enabled == value)
            {
                return;
            }
            _config.Enabled = value;
            if (value)
            {
                _config.MarkScheduleChanged();
            }
            SaveConfig();
            OnPropertyChanged();
            UpdateScheduleTexts();
        }
    }


    /// <summary>频率模式下拉的选中项，与 <see cref="RecordRefreshMode"/> 的数值一致。</summary>
    public int SelectedModeIndex
    {
        get => (int)_config.Mode;
        set
        {
            if (value < 0 || !_initialized || (int)_config.Mode == value)
            {
                return;
            }
            _config.Mode = (RecordRefreshMode)value;
            // 改了频率就以今天为起点重排，「下次更新」立刻反映新设置，也不受今天已经更新过的影响
            _config.MarkScheduleChanged();
            SaveConfig();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEveryDaysMode));
            OnPropertyChanged(nameof(IsWeeklyMode));
            OnPropertyChanged(nameof(IsMonthlyMode));
            UpdateScheduleTexts();
        }
    }

    public bool IsEveryDaysMode => _config.Mode is RecordRefreshMode.EveryDays;

    public bool IsWeeklyMode => _config.Mode is RecordRefreshMode.Weekly;

    public bool IsMonthlyMode => _config.Mode is RecordRefreshMode.Monthly;


    /// <summary>「每隔 N 天」的天数。NumberBox 清空时会给出 NaN，直接忽略。</summary>
    public double IntervalDays
    {
        get => _config.IntervalDays;
        set
        {
            if (double.IsNaN(value) || !_initialized)
            {
                return;
            }
            int days = RecordRefreshSchedule.ClampIntervalDays((int)value);
            if (_config.IntervalDays == days)
            {
                return;
            }
            _config.IntervalDays = days;
            _config.MarkScheduleChanged();
            SaveConfig();
            OnPropertyChanged();
            UpdateScheduleTexts();
        }
    }


    /// <summary>「每周」模式选中的星期几。</summary>
    public int SelectedDayOfWeek
    {
        get => Math.Clamp(_config.DayOfWeek, 0, 6);
        set
        {
            if (value < 0 || !_initialized || _config.DayOfWeek == value)
            {
                return;
            }
            _config.DayOfWeek = value;
            _config.MarkScheduleChanged();
            SaveConfig();
            OnPropertyChanged();
            UpdateScheduleTexts();
        }
    }


    /// <summary>「每月」模式选中的日期，索引 0 对应 1 号。</summary>
    public int SelectedDayOfMonthIndex
    {
        get => RecordRefreshSchedule.ClampDayOfMonth(_config.DayOfMonth) - 1;
        set
        {
            if (value < 0 || !_initialized || _config.DayOfMonth == value + 1)
            {
                return;
            }
            _config.DayOfMonth = value + 1;
            _config.MarkScheduleChanged();
            SaveConfig();
            OnPropertyChanged();
            UpdateScheduleTexts();
        }
    }


    [ObservableProperty]
    private string _lastUpdateText = string.Empty;

    [ObservableProperty]
    private string _nextUpdateText = string.Empty;


    /// <summary>
    /// 按当前 UI 语言生成星期与「几号」下拉项。
    /// </summary>
    private void InitializeDayNames()
    {
        foreach (string name in CultureInfo.CurrentUICulture.DateTimeFormat.DayNames)
        {
            DayOfWeekNames.Add(name);
        }
        for (int i = 1; i <= 31; i++)
        {
            DayOfMonthNames.Add(string.Format(CultureInfo.CurrentUICulture, Lang.AutoRecordRefresh_DayOfMonthFormat, i));
        }
    }


    /// <summary>
    /// 载入当前账号在本板块上的配置，并刷新全部绑定。
    /// </summary>
    private void LoadConfig()
    {
        _config = GameRole is null ? new RecordRefreshConfig() : RecordRefreshConfigStore.Load(GameRole, Item);
        OnPropertyChanged(nameof(RoleName));
        OnPropertyChanged(nameof(HasRole));
        OnPropertyChanged(nameof(AutoRefreshEnabled));
        OnPropertyChanged(nameof(SelectedModeIndex));
        OnPropertyChanged(nameof(IsEveryDaysMode));
        OnPropertyChanged(nameof(IsWeeklyMode));
        OnPropertyChanged(nameof(IsMonthlyMode));
        OnPropertyChanged(nameof(IntervalDays));
        OnPropertyChanged(nameof(SelectedDayOfWeek));
        OnPropertyChanged(nameof(SelectedDayOfMonthIndex));
        UpdateScheduleTexts();
        UpdateErrorState();
    }


    /// <summary>
    /// 重新判断这个目标当前有没有异常记录。
    /// </summary>
    private void UpdateErrorState()
    {
        if (GameRole is null)
        {
            HasError = false;
            return;
        }
        try
        {
            HasError = _service.GetErrors().Exists(x => x.Uid == GameRole.Uid && x.GameBiz == GameRole.GameBiz && x.Item == Item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Check record refresh error state ({biz}, {uid}, {item}).", GameRole.GameBiz, GameRole.Uid, Item);
            HasError = false;
        }
    }


    /// <summary>
    /// 保存配置。没有账号时不落库：配置是按账号存的，没号就没地方存。
    /// </summary>
    private void SaveConfig()
    {
        if (GameRole is null)
        {
            return;
        }
        try
        {
            // 后台成功后只写 lastRun；浮层开着时这份 _config 可能是它之前读的，
            // 落库前把这个字段从库里取回来，别拿旧值把成功记录盖掉
            _config.LastRunTicks = RecordRefreshConfigStore.Load(GameRole, Item).LastRunTicks;
            RecordRefreshConfigStore.Save(GameRole.GameBiz, GameRole.Uid, Item, _config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save record refresh config ({biz}, {uid}, {item}).", GameRole.GameBiz, GameRole.Uid, Item);
        }
    }


    /// <summary>
    /// 刷新「上次更新 / 下次更新」两行文案。
    /// </summary>
    private void UpdateScheduleTexts()
    {
        LastUpdateText = _config.LastRunTime is DateTimeOffset time
            ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentUICulture)
            : Lang.AutoRecordRefresh_NeverUpdated;
        NextUpdateText = _config.Enabled
            ? _config.GetNextDueDate(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.CurrentUICulture)
            : string.Empty;
    }


    private void Flyout_Opening(object? sender, object e)
    {
        // 后台可能刚写过 lastRun / 清过异常；不重载的话会显示旧值，改频率还会把成功记录盖掉
        LoadConfig();
    }


    private async void Button_ViewErrors_Click(object sender, RoutedEventArgs e)
    {
        Button_Root.Flyout?.Hide();
        await new RecordRefreshErrorDialog { XamlRoot = this.XamlRoot }.ShowAsync();
        // 对话框里可能清空了记录，回来立刻把红点同步掉
        UpdateErrorState();
    }

}
