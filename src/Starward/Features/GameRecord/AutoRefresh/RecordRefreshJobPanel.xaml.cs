using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Starward.Core.GameRecord;
using Starward.Language;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 一个自动更新任务（账号 + 数据板块 + 月份）的设置块：开关、频率、上次 / 下次更新。
/// <para>
/// 月报类板块在配置浮层里放两份（当月、上月），两份各读各的配置、各存各的键，互不影响；
/// 其余板块只放一份（月份恒为 <see cref="RecordRefreshMonthTarget.Current"/>，界面上不出现月份字样）。
/// </para>
/// </summary>
[INotifyPropertyChanged]
public sealed partial class RecordRefreshJobPanel : UserControl
{

    private readonly ILogger<RecordRefreshJobPanel> _logger = AppConfig.GetLogger<RecordRefreshJobPanel>();

    private RecordRefreshConfig _config = new();

    /// <summary>初始化期间不回写配置：ComboBox 在构建阶段会先抛出 -1 等过渡值。</summary>
    private bool _initialized;


    public RecordRefreshJobPanel()
    {
        InitializeDayNames();
        InitializeComponent();
        _initialized = true;
    }


    /// <summary>
    /// 本任务对应的数据板块，由外层配置按钮赋值。
    /// </summary>
    public RecordRefreshItem Item
    {
        get => field;
        set
        {
            field = value;
            LoadConfig();
        }
    }


    /// <summary>
    /// 本任务对应的月份。非月报板块保持默认的当月，在 XAML 上写死，如 <c>MonthTarget="Previous"</c>。
    /// </summary>
    public RecordRefreshMonthTarget MonthTarget
    {
        get => field;
        set
        {
            field = value;
            LoadConfig();
        }
    }


    /// <summary>
    /// 当前账号。外层按钮在自己的 <c>GameRole</c> 变化时转发过来；换号后配置会重新载入。
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


    /// <summary>
    /// 是否在任务名旁边显示「不保证」的问号图标。同一个浮层里只让第一个任务显示，免得同一句话说两遍。
    /// </summary>
    public bool ShowHint
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }


    /// <summary>星期下拉项，索引与 <see cref="DayOfWeek"/> 一致（0 为周日）。</summary>
    public ObservableCollection<string> DayOfWeekNames { get; } = [];

    /// <summary>「几号」下拉项，索引 0 对应 1 号。</summary>
    public ObservableCollection<string> DayOfMonthNames { get; } = [];

    /// <summary>任务标题：月报类是「更新当月 / 更新上月」，其余板块是「启用自动更新」。</summary>
    public string Title => Item.GetJobTitle(MonthTarget);

    /// <summary>没有账号时不给配：配置本身就是按账号存的。</summary>
    public bool HasRole => GameRole is not null;


    /// <summary>本任务是否开启自动更新。</summary>
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
    /// 重新从库里读一遍配置并刷新全部绑定。浮层打开时由外层按钮调用：
    /// 后台可能刚写过 lastRun，不重载会显示旧值，改频率还会把成功记录盖掉。
    /// </summary>
    public void Reload()
    {
        LoadConfig();
    }


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
    /// 载入本任务的配置，并刷新全部绑定。
    /// </summary>
    private void LoadConfig()
    {
        _config = GameRole is null ? new RecordRefreshConfig() : RecordRefreshConfigStore.Load(GameRole, Item, MonthTarget);
        OnPropertyChanged(nameof(Title));
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
            _config.LastRunTicks = RecordRefreshConfigStore.Load(GameRole, Item, MonthTarget).LastRunTicks;
            RecordRefreshConfigStore.Save(GameRole.GameBiz, GameRole.Uid, Item, _config, MonthTarget);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save record refresh config ({biz}, {uid}, {item}, {month}).", GameRole.GameBiz, GameRole.Uid, Item, MonthTarget);
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

}
