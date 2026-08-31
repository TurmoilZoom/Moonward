using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.Database;
using Starward.Features.GameRecord.Share;
using Starward.Features.GameSelector;
using Starward.Features.Screenshot;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;


namespace Starward.Features.PlayTime;

[INotifyPropertyChanged]
public sealed partial class PlayTimeStatsDialog : ContentDialog
{


    private readonly ILogger<PlayTimeStatsDialog> _logger = AppConfig.GetLogger<PlayTimeStatsDialog>();

    private readonly PlayTimeStatsService _playTimeStatsService = AppConfig.GetService<PlayTimeStatsService>();

    private Dictionary<DateOnly, long> _playTimePerDay = [];



    public PlayTimeStatsDialog()
    {
        this.InitializeComponent();
        this.Loaded += PlayTimeStatsDialog_Loaded;
        this.Unloaded += PlayTimeStatsDialog_Unloaded;
    }



    public GameId CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; set; }




    private void PlayTimeStatsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _playTimeStatsService.ConvertItemToStats();
            InitializeGameSwitcher();
            InitializeBarRangeOptions();
            LoadPlayTimeStats();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load play time stats: GameBiz {biz}", CurrentGameBiz);
        }
        _playTimeLoaded = true;
    }


    private void PlayTimeStatsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Loaded -= PlayTimeStatsDialog_Loaded;
        this.Unloaded -= PlayTimeStatsDialog_Unloaded;
        StatCards?.Clear();
        GameBizIcons.Clear();
        PlayTimeBarChart.Items = null;
        PlayTimeHeatmap.Days = null;
        _playTimeLoaded = false;
    }




    #region 游戏切换


    private bool _suppressGameSelection;


    /// <summary>
    /// 当前展示的游戏。默认取打开对话框的游戏，可在标题栏切换；B 服已折算成官服。
    /// </summary>
    public GameBiz SelectedGameBiz { get; private set => SetProperty(ref field, value); }


    /// <summary>标题栏切换按钮上显示的游戏图标与名称。</summary>
    public GameBizIcon? SelectedGameIcon { get; set => SetProperty(ref field, value); }


    /// <summary>有游戏时长记录的游戏列表（含当前游戏）。</summary>
    public ObservableCollection<GameBizIcon> GameBizIcons { get; } = [];


    /// <summary>
    /// 构建游戏切换列表：数据库中有时长记录的游戏，外加当前游戏（可能还没有记录）。
    /// </summary>
    private void InitializeGameSwitcher()
    {
        _suppressGameSelection = true;
        try
        {
            SelectedGameBiz = PlayTimeStatsService.NormalizeBiz(CurrentGameBiz);
            List<GameBiz> bizs = _playTimeStatsService.GetRecordedGameBizs();
            if (!bizs.Contains(SelectedGameBiz))
            {
                bizs.Insert(0, SelectedGameBiz);
            }
            GameBizIcons.Clear();
            foreach (GameBiz biz in bizs)
            {
                // 未适配的 GameBiz 没有本地图标与名称，不进切换列表
                if (biz.IsKnown() && GameId.FromGameBiz(biz) is not null)
                {
                    GameBizIcons.Add(new GameBizIcon(biz));
                }
            }
            SelectedGameIcon = GameBizIcons.FirstOrDefault(x => x.GameBiz == SelectedGameBiz);
            if (ListView_Game is not null)
            {
                ListView_Game.SelectedItem = SelectedGameIcon;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize play time game switcher");
        }
        finally
        {
            _suppressGameSelection = false;
        }
    }


    private void ListView_Game_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGameSelection || (sender as ListView)?.SelectedItem is not GameBizIcon icon)
        {
            return;
        }
        Flyout_Game?.Hide();
        if (icon.GameBiz == SelectedGameBiz)
        {
            return;
        }
        SelectedGameBiz = icon.GameBiz;
        SelectedGameIcon = icon;
        LoadPlayTimeStats();
    }


    #endregion


    private bool _playTimeLoaded;


    /// <summary>
    /// 总时长文本
    /// </summary>
    public string TotalTimeText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 启动次数
    /// </summary>
    public string StartUpCountText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 每日平均游戏时间
    /// </summary>
    public string AverageDayTimeText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 游玩天数
    /// </summary>
    public string PlayDaysText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 最长连续游玩天数
    /// </summary>
    public int LongestContinuousDays { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 最长连续游玩天数文本
    /// </summary>
    public string LongestContinuousDaysText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 最长单次游玩时长文本
    /// </summary>
    public string LongestRunTimeText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 最长单次游玩起始日期文本
    /// </summary>
    public string LongestRunStartText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 上一次游玩时长文本
    /// </summary>
    public string LastPlayDurationText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 上一次游玩起始时间文本
    /// </summary>
    public string LastPlayTimeText { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 单日最长游玩时长文本
    /// </summary>
    public string MaxDayPlayTimeText { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 单日最长游玩起始日期文本
    /// </summary>
    public string MaxDayPlayDateText { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 总时长文本
    /// </summary>
    public string BarTotalText { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 统计卡片数据项
    /// </summary>
    public List<StatCardItem> StatCards { get; set => SetProperty(ref field, value); }


    /// <summary>分享图渲染中时禁用分享按钮，避免重复触发。</summary>
    public bool IsNotSharingImage { get; set => SetProperty(ref field, value); } = true;



    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }



    /// <summary>
    /// 加载全部统计：单次查询会话区间，内存中派生所有属性与图表数据
    /// </summary>
    private void LoadPlayTimeStats()
    {
        var biz = SelectedGameBiz;
        try
        {
            var sessions = _playTimeStatsService.GetPlayTimeInRange(biz, default, DateTimeOffset.Now);
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            // 总时长
            long totalMs = 0;
            // 最长单次游玩
            long longestSpan = 0, longestStart = 0;
            // 上一次游玩
            long lastStart = 0, lastSpan = 0;
            // 每日时长
            Dictionary<DateOnly, long> timePerDay = new Dictionary<DateOnly, long>();

            foreach (var session in sessions)
            {
                long span = session.EndTime - session.StartTime;
                totalMs += span;
                if (span > longestSpan)
                {
                    longestSpan = span;
                    longestStart = session.StartTime;
                }

                // 游戏关闭后60s才会被认为是上一次启动
                if (session.StartTime > lastStart && now - session.EndTime > 60_000)
                {
                    lastStart = session.StartTime;
                    lastSpan = span;
                }

                DateTimeOffset startTime = DateTimeOffset.FromUnixTimeMilliseconds(session.StartTime).ToLocalTime();
                DateTimeOffset endTime = DateTimeOffset.FromUnixTimeMilliseconds(session.EndTime).ToLocalTime();

                // 计算每日时长，把数据添加到 timePerDay 字典中
                for (DateTime day = startTime.Date; day <= endTime.Date; day = day.AddDays(1))
                {
                    DateTimeOffset dayStart = day == startTime.Date ? startTime : day;
                    DateTimeOffset dayEnd = day == endTime.Date ? endTime : day.AddDays(1).AddTicks(-1);
                    var duration = (long)(dayEnd - dayStart).TotalMilliseconds;
                    var dateOnly = DateOnly.FromDateTime(day);
                    if (timePerDay.ContainsKey(dateOnly))
                    {
                        timePerDay[dateOnly] += duration;
                    }
                    else
                    {
                        timePerDay[dateOnly] = duration;
                    }
                }
            }

            _playTimePerDay = timePerDay;
            UpdateYearOptions();

            TotalTimeText = TimeSpanToString(TimeSpan.FromMilliseconds(totalMs));
            StartUpCountText = totalMs > 0 ? string.Format(Lang.PlayTimeStatsDialog_Started0Times, sessions.Count) : "";
            // 键必须与 PlayTimeButton 读取的一致（B 服归一化到官服），否则按钮显示 0h 0m。
            DatabaseService.SetValue(PlayTimeStatsService.TotalPlayTimeKey(biz), TimeSpan.FromMilliseconds(totalMs));

            AverageDayTimeText = timePerDay.Count > 0 ? TimeSpanToString(TimeSpan.FromMilliseconds(totalMs / timePerDay.Count)) : "-";
            PlayDaysText = timePerDay.Count > 0 ? string.Format(Lang.PlayTimeStatsDialog_PlayedFor0Days, timePerDay.Count) : "";

            // 最长连续游玩天数和起止日期
            int longestContinuousDays = 0;
            DateOnly? longestContinuousStart = null;
            DateOnly? longestContinuousEnd = null;
            var orderedDays = timePerDay.Keys.OrderBy(d => d).ToList();

            DateOnly? currentStart = null;
            DateOnly? previousDay = null;
            int currentStreak = 0;

            foreach (var orderedDay in orderedDays)
            {
                if (previousDay.HasValue && orderedDay == previousDay.Value.AddDays(1))
                {
                    // 与前一天相邻，延长当前连续段
                    currentStreak++;
                }
                else
                {
                    // 与前一天不相邻，开始新的连续段
                    currentStart = orderedDay;
                    currentStreak = 1;
                }

                if (currentStreak > longestContinuousDays)
                {
                    longestContinuousDays = currentStreak;
                    longestContinuousStart = currentStart;
                    longestContinuousEnd = orderedDay;
                }

                previousDay = orderedDay;
            }

            if (longestContinuousDays > 0)
            {
                LongestContinuousDays = longestContinuousDays;
                LongestContinuousDaysText = $"{longestContinuousStart:yyyy/MM/dd} - {longestContinuousEnd:yyyy/MM/dd}";
            }
            else
            {
                LongestContinuousDays = 0;
                LongestContinuousDaysText = "";
            }

            // 单日最长游玩时长和日期
            long maxDayMs = 0;
            DateOnly maxDayDate = default;
            foreach (var (day, ms) in timePerDay)
            {
                if (ms > maxDayMs)
                {
                    maxDayMs = ms;
                    maxDayDate = day;
                }
            }

            if (maxDayMs > 0)
            {
                MaxDayPlayTimeText = TimeSpanToString(TimeSpan.FromMilliseconds(maxDayMs));
                MaxDayPlayDateText = maxDayDate.ToString("yyyy-MM-dd");
            }
            else
            {
                MaxDayPlayTimeText = "-";
                MaxDayPlayDateText = "";
            }

            LongestRunTimeText = TimeSpanToString(TimeSpan.FromMilliseconds(longestSpan));
            LongestRunStartText = longestSpan > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(longestStart).LocalDateTime.ToString("yyyy-MM-dd") : "";
            if (lastStart > 0)
            {
                LastPlayDurationText = TimeSpanToString(TimeSpan.FromMilliseconds(Math.Max(lastSpan, 60_000)));
                LastPlayTimeText = DateTimeOffset.FromUnixTimeMilliseconds(lastStart).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                LastPlayDurationText = "-";
                LastPlayTimeText = "";
            }

            StatCards =
            [
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_TotalPlaytime, Value = TotalTimeText,SubText = StartUpCountText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_AverageDailyPlaytime, Value = AverageDayTimeText,SubText= PlayDaysText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_LongestStreak, Value = string.Format(Lang.PlayTimeStatsDialog_0Days,LongestContinuousDays), SubText = LongestContinuousDaysText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_LongestSession, Value = LongestRunTimeText, SubText = LongestRunStartText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_LongestDailyPlaytime, Value = MaxDayPlayTimeText, SubText = MaxDayPlayDateText },
                new StatCardItem { Title = Lang.PlayTimeButton_LastStartup, Value = LastPlayDurationText, SubText = LastPlayTimeText },
            ];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load play time stats: GameBiz {biz}", biz);
        }

        try
        {
            BuildBarChart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build bar chart: GameBiz {biz}, range {range}", SelectedGameBiz, Segmented_BarRange.SelectedIndex);
        }

        try
        {
            BuildHeatmap();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build heatmap: GameBiz {biz}", SelectedGameBiz);
        }


        if (Lang.PlayTimeStatsDialog_AverageDailyPlaytime.Length > 22)
        {
            UniformGridLayout_StatCards.MinItemHeight = 98;
            Grid_BarChartSwitcher.Margin = new Thickness(4, 16, 4, 0);
            PlayTimeHeatmap.Margin = new Thickness(0, 20, 0, 0);
        }

    }



    private void BuildBarChart()
    {
        int range = Segmented_BarRange.SelectedIndex;
        var today = DateTime.Today;
        if (range == BarRangeCustom)
        {
            BuildCustomBarChart();
        }
        else if (range == 1)
        {
            // 最近 12 个自然周（周一 ～ 周日为一周）：以今天所在的周为最后一周，往前共 12 组
            int sinceMonday = ((int)today.DayOfWeek + 6) % 7; // 本周已过天数（0 = 周一）
            var firstMonday = today.AddDays(-sinceMonday - 11 * 7);
            var items = new List<BarChartItem>(12);
            long total = 0;
            for (int w = 0; w < 12; w++)
            {
                var weekStart = firstMonday.AddDays(w * 7);
                var weekEnd = weekStart.AddDays(6);
                var actualLast = weekEnd <= today ? DateOnly.FromDateTime(weekEnd) : DateOnly.FromDateTime(today);
                long sum = SumDayRange(DateOnly.FromDateTime(weekStart), DateOnly.FromDateTime(weekEnd));
                total += sum;
                items.Add(new BarChartItem
                {
                    Label = weekStart.ToString("MM-dd"),
                    Value = sum / 60_000.0,
                    Tooltip = $"{weekStart:MM/dd} - {actualLast:MM/dd}\n{TimeSpanToString(TimeSpan.FromMilliseconds(sum))}",
                });
            }
            PlayTimeBarChart.Items = items;
            BarTotalText = TimeSpanToString(TimeSpan.FromMilliseconds(total));
        }
        else if (range == 2)
        {
            // 最近 12 个月：从上月月初（共 12 个自然月对齐）到今天的每日数据按月聚合
            var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
            var items = new List<BarChartItem>(12);
            long total = 0;
            for (int m = 0; m < 12; m++)
            {
                var monthStart = firstMonth.AddMonths(m);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                long sum = SumDayRange(DateOnly.FromDateTime(monthStart), DateOnly.FromDateTime(monthEnd));
                total += sum;
                items.Add(new BarChartItem
                {
                    Label = monthStart.ToString("MMM", CultureInfo.CurrentUICulture),
                    Value = sum / 60_000.0,
                    Tooltip = $"{monthStart:yyyy-MM}\n{TimeSpanToString(TimeSpan.FromMilliseconds(sum))}",
                });
            }
            PlayTimeBarChart.Items = items;
            BarTotalText = TimeSpanToString(TimeSpan.FromMilliseconds(total));
        }
        else
        {
            var firstDay = today.AddDays(-14);
            var items = new List<BarChartItem>(15);
            long total = 0;
            for (int i = 0; i < 15; i++)
            {
                var d = firstDay.AddDays(i);
                long ms = _playTimePerDay.GetValueOrDefault(DateOnly.FromDateTime(d));
                total += ms;
                items.Add(new BarChartItem
                {
                    Label = d.ToString("MM-dd"),
                    Value = Math.Max(0, ms / 60_000.0),
                    Tooltip = $"{d:yyyy-MM-dd}\n{TimeSpanToString(TimeSpan.FromMilliseconds(ms))}",
                });
            }
            PlayTimeBarChart.Items = items;
            BarTotalText = TimeSpanToString(TimeSpan.FromMilliseconds(total));
        }
    }


    /// <summary>柱状图「自定义」模式在 <see cref="Segmented_BarRange"/> 中的下标。</summary>
    private const int BarRangeCustom = 3;


    /// <summary>恢复设置、切换游戏时会改动下拉框，期间不回写设置也不重建图表。</summary>
    private bool _suppressBarRangeSave;


    /// <summary>
    /// 自定义模式：月份选「全年」时按该年 12 个月聚合，否则展示该月每一天。
    /// </summary>
    private void BuildCustomBarChart()
    {
        int year = ComboBox_BarYear.SelectedItem is int y ? y : DateTime.Today.Year;
        int month = ComboBox_BarMonth.SelectedItem is BarMonthOption option ? option.Month : 0;
        var items = new List<BarChartItem>();
        long total = 0;
        if (month is 0)
        {
            string[] monthNames = CultureInfo.CurrentUICulture.DateTimeFormat.AbbreviatedMonthNames;
            for (int m = 1; m <= 12; m++)
            {
                var monthStart = new DateOnly(year, m, 1);
                long sum = SumDayRange(monthStart, monthStart.AddMonths(1).AddDays(-1));
                total += sum;
                items.Add(new BarChartItem
                {
                    Label = monthNames[m - 1],
                    Value = Math.Max(0, sum / 60_000.0),
                    Tooltip = $"{year}-{m:D2}\n{TimeSpanToString(TimeSpan.FromMilliseconds(sum))}",
                });
            }
        }
        else
        {
            int days = DateTime.DaysInMonth(year, month);
            for (int d = 1; d <= days; d++)
            {
                var day = new DateOnly(year, month, d);
                long ms = _playTimePerDay.GetValueOrDefault(day);
                total += ms;
                items.Add(new BarChartItem
                {
                    Label = d.ToString(CultureInfo.CurrentCulture),
                    Value = Math.Max(0, ms / 60_000.0),
                    Tooltip = $"{day:yyyy-MM-dd}\n{TimeSpanToString(TimeSpan.FromMilliseconds(ms))}",
                });
            }
        }
        PlayTimeBarChart.Items = items;
        BarTotalText = TimeSpanToString(TimeSpan.FromMilliseconds(total));
    }


    /// <summary>
    /// 恢复上次选择的柱状图模式与参数（年份列表要等每日数据算出来后才知道范围，见 <see cref="UpdateYearOptions"/>）。
    /// </summary>
    private void InitializeBarRangeOptions()
    {
        _suppressBarRangeSave = true;
        try
        {
            var months = new List<BarMonthOption> { new(0, Lang.PlayTimeStatsDialog_WholeYear) };
            string[] monthNames = CultureInfo.CurrentUICulture.DateTimeFormat.MonthNames;
            for (int m = 1; m <= 12; m++)
            {
                months.Add(new BarMonthOption(m, monthNames[m - 1]));
            }
            ComboBox_BarMonth.ItemsSource = months;
            ComboBox_BarMonth.SelectedIndex = Math.Clamp(AppConfig.PlayTimeStatsBarMonth, 0, 12);
            Segmented_BarRange.SelectedIndex = Math.Clamp(AppConfig.PlayTimeStatsBarRange, 0, BarRangeCustom);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize bar range options");
        }
        finally
        {
            _suppressBarRangeSave = false;
            // 控件此时才齐全，这里补上 SelectionChanged 里没能生效的那次可见性同步
            UpdateCustomRangeVisibility();
        }
    }


    /// <summary>
    /// 年份候选：从有记录的最早一年到今年，并保证上次选中的年份仍在列表里。
    /// </summary>
    private void UpdateYearOptions()
    {
        _suppressBarRangeSave = true;
        try
        {
            int currentYear = DateTime.Today.Year;
            int selected = ComboBox_BarYear.SelectedItem is int y ? y : AppConfig.PlayTimeStatsBarYear;
            if (selected <= 0)
            {
                selected = currentYear;
            }
            int minYear = Math.Min(currentYear, selected);
            if (_playTimePerDay.Count > 0)
            {
                minYear = Math.Min(minYear, _playTimePerDay.Keys.Min().Year);
            }
            // 时间戳异常的记录可能落在很久以前，最多回溯 20 年，避免下拉框被撑爆
            minYear = Math.Clamp(minYear, currentYear - 20, currentYear);
            var years = new List<int>();
            for (int year = currentYear; year >= minYear; year--)
            {
                years.Add(year);
            }
            ComboBox_BarYear.ItemsSource = years;
            ComboBox_BarYear.SelectedItem = years.Contains(selected) ? selected : currentYear;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update bar chart year options");
        }
        finally
        {
            _suppressBarRangeSave = false;
        }
    }


    /// <summary>
    /// 同步自定义模式下年月下拉框的显隐。
    /// Segmented 首项的 <c>IsSelected="True"</c> 在 <c>InitializeComponent</c> 解析到该控件时就会触发
    /// SelectionChanged，而两个下拉框声明在它后面、字段尚未赋值，所以这里必须容忍 null——
    /// 那一次跳过没关系，<see cref="InitializeBarRangeOptions"/> 会在控件齐全后再同步一次。
    /// </summary>
    private void UpdateCustomRangeVisibility()
    {
        if (ComboBox_BarYear is null || ComboBox_BarMonth is null)
        {
            return;
        }
        Visibility visibility = Segmented_BarRange?.SelectedIndex == BarRangeCustom ? Visibility.Visible : Visibility.Collapsed;
        ComboBox_BarYear.Visibility = visibility;
        ComboBox_BarMonth.Visibility = visibility;
    }


    private void SaveBarRangeSetting()
    {
        AppConfig.PlayTimeStatsBarRange = Segmented_BarRange.SelectedIndex;
        if (ComboBox_BarYear.SelectedItem is int year)
        {
            AppConfig.PlayTimeStatsBarYear = year;
        }
        if (ComboBox_BarMonth.SelectedItem is BarMonthOption option)
        {
            AppConfig.PlayTimeStatsBarMonth = option.Month;
        }
    }


    private void Segmented_BarRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCustomRangeVisibility();
        if (!_playTimeLoaded)
        {
            return;
        }
        try
        {
            SaveBarRangeSetting();
            BuildBarChart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rebuild bar chart: GameBiz {biz}", SelectedGameBiz);
        }
    }


    private void ComboBox_BarYear_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OnCustomRangeChanged();
    }


    private void ComboBox_BarMonth_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OnCustomRangeChanged();
    }


    private void OnCustomRangeChanged()
    {
        if (_suppressBarRangeSave || !_playTimeLoaded)
        {
            return;
        }
        try
        {
            SaveBarRangeSetting();
            BuildBarChart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rebuild custom bar chart: GameBiz {biz}", SelectedGameBiz);
        }
    }


    /// <summary>柱状图当前区间的标题文本，用于分享图。</summary>
    private string GetBarRangeTitle()
    {
        if (Segmented_BarRange.SelectedIndex == BarRangeCustom)
        {
            int year = ComboBox_BarYear.SelectedItem is int y ? y : DateTime.Today.Year;
            int month = ComboBox_BarMonth.SelectedItem is BarMonthOption option ? option.Month : 0;
            return month is 0 ? year.ToString(CultureInfo.CurrentCulture) : $"{year}-{month:D2}";
        }
        return Segmented_BarRange.SelectedIndex switch
        {
            1 => Lang.PlayTimeStatsDialog_Last12Weeks,
            2 => Lang.PlayTimeStatsDialog_Last12Months,
            _ => Lang.PlayTimeStatsDialog_Last15Days,
        };
    }


    /// <summary>月份下拉项，<see cref="Month"/> 为 0 表示「全年」。</summary>
    public sealed class BarMonthOption
    {
        public int Month { get; }

        public string Text { get; }

        public BarMonthOption(int month, string text)
        {
            Month = month;
            Text = text;
        }
    }



    private void BuildHeatmap()
    {
        // 最近一年，52个自然周
        var today = DateTime.Today;
        // 先回溯到本周周一（周一 = 0），再往前 51 周，保证首日一定是周一。
        // 注意不能用 ((int)DayOfWeek - 1) % 6：C# 取余保留被除数符号，周日会得到 -1 而反向偏移一天。
        int sinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var firstDay = today.AddDays(-sinceMonday - 51 * 7);
        int totalDays = (today - firstDay).Days + 1;
        var items = new List<HeatmapDayItem>(totalDays);
        long total = 0;
        for (int i = 0; i < totalDays; i++)
        {
            var d = firstDay.AddDays(i);
            long ms = _playTimePerDay.GetValueOrDefault(DateOnly.FromDateTime(d));
            total += ms;
            items.Add(new HeatmapDayItem
            {
                Date = DateOnly.FromDateTime(d),
                Value = Math.Max(0, ms / 60_000.0),
                Tooltip = $"{d:yyyy-MM-dd}\n{TimeSpanToString(TimeSpan.FromMilliseconds(ms))}",
            });
        }
        PlayTimeHeatmap.Days = items;
    }



    /// <summary>
    /// 累加 [start, end] 日期区间内的每日游戏毫秒数
    /// </summary>
    private long SumDayRange(DateOnly start, DateOnly end)
    {
        long sum = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            sum += _playTimePerDay.GetValueOrDefault(d);
        }
        return sum;
    }


    /// <summary>
    /// 用 Win2D 离屏渲染当前统计（卡片 + 柱状图 + 热力图）并用内置看图窗口打开。
    /// </summary>
    [RelayCommand]
    private async Task ShareImageAsync()
    {
        if (!IsNotSharingImage)
        {
            return;
        }
        try
        {
            IsNotSharingImage = false;
            PlayTimeShareSnapshot data = BuildShareSnapshot();
            string? backgroundFile = await PrepareShareBackgroundAsync();
            // 强调色只能在 UI 线程读，Win2D 绘制放后台
            Color accentColor = GameRecordShareHelper.GetAccentColor();
            string file = await Task.Run(async () => await PlayTimeShareRenderer.RenderAndSaveAsync(data, backgroundFile, accentColor));
            await new ImageViewWindow2().ShowWindowAsync(this.XamlRoot.ContentIslandEnvironment.AppWindowId, file, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Share play time stats image: GameBiz {biz}", SelectedGameBiz);
            InAppToast.MainWindow?.Error(ex);
        }
        finally
        {
            IsNotSharingImage = true;
        }
    }


    private PlayTimeShareSnapshot BuildShareSnapshot()
    {
        return new PlayTimeShareSnapshot
        {
            FileStem = "playtime_stats",
            Title = Lang.PlayTimeStatsDialog_PlaytimeStatistics,
            GameName = SelectedGameIcon?.GameName ?? SelectedGameBiz.ToGameName(),
            ServerName = SelectedGameIcon?.ServerName ?? SelectedGameBiz.ToGameServerName(),
            Cards = StatCards?.Select(x => new PlayTimeShareCard
            {
                Title = x.Title,
                Value = x.Value,
                SubText = x.SubText,
            }).ToList() ?? [],
            BarTitle = GetBarRangeTitle(),
            BarTotalText = $"{Lang.PlayTimeStatsDialog_Total} {BarTotalText}",
            Bars = PlayTimeBarChart.Items?.Select(x => new PlayTimeShareBar
            {
                Label = x.Label,
                Minutes = x.Value,
            }).ToList() ?? [],
            HeatmapDays = PlayTimeHeatmap.Days?.Select(x => new PlayTimeShareHeatmapDay
            {
                Date = x.Date,
                Minutes = x.Value,
            }).ToList() ?? [],
        };
    }


    /// <summary>
    /// 分享图背景：当前游戏用启动器正在显示的壁纸（视频背景抓当前帧），
    /// 切到其他游戏时只能用它的缓存壁纸，视频没有可抓的帧就退回渐变底。
    /// </summary>
    private async Task<string?> PrepareShareBackgroundAsync()
    {
        if (SelectedGameBiz == PlayTimeStatsService.NormalizeBiz(CurrentGameBiz))
        {
            return await GameRecordShareHelper.PrepareBackgroundFileAsync(CurrentGameBiz);
        }
        if (GameId.FromGameBiz(SelectedGameBiz) is GameId gameId)
        {
            string? file = BackgroundService.GetCachedBackgroundFile(gameId);
            return file is not null && BackgroundService.FileIsSupportedVideo(file) ? null : file;
        }
        return null;
    }


    public static string TimeSpanToString(TimeSpan timeSpan)
    {
        int totalMinutes = (int)Math.Round(timeSpan.TotalMinutes);
        if (totalMinutes < 1)
        {
            return "0m";
        }
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }
        int hours = totalMinutes / 60, minutes = totalMinutes % 60;
        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }


}