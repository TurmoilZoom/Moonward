using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.ZZZ.InterKnotReport;
using Starward.Features.GameRecord.Share;
using Starward.Features.GameRecord.WeeklyDailyData;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;


namespace Starward.Features.GameRecord.ZZZ;

public sealed partial class InterKnotMonthlyReportPage : PageBase
{
    /// <summary>
    /// 绝区零绳网月报页面（月报数据）。显示当月/历史聚货、音像带、博识等收入及每日明细图表。
    /// 打开时会刷新当前月数据，即使本地已有缓存。
    /// </summary>

    private readonly ILogger<InterKnotMonthlyReportPage> _logger = AppConfig.GetLogger<InterKnotMonthlyReportPage>();


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    public InterKnotMonthlyReportPage()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// 菲林图标缓存；切周重建表格时复用同一实例，避免 Image 重新解码导致闪烁。
    /// </summary>
    private static readonly BitmapImage PolychromesIcon = new(new Uri("ms-appx:///Assets/Image/IconCurrency.png"));

    /// <summary>
    /// 加密母带图标缓存；切周重建表格时复用同一实例，避免 Image 重新解码导致闪烁。
    /// </summary>
    private static readonly BitmapImage MasterTapeIcon = new(new Uri("ms-appx:///Assets/Image/GachaTicket2Big.png"));

    /// <summary>
    /// 邦布券图标缓存；切周重建表格时复用同一实例，避免 Image 重新解码导致闪烁。
    /// </summary>
    private static readonly BitmapImage BooponsIcon = new(new Uri("ms-appx:///Assets/Image/GachaTicket3Big.png"));



    /// <summary>
    /// 当前游戏角色，从导航参数传入。
    /// </summary>
    private GameRecordRole gameRole;



    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is GameRecordRole role)
        {
            gameRole = role;
        }
    }


    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        await InitializeDataAsync();
    }



    protected override void OnUnloaded()
    {
        base.OnUnloaded();
        SelectMonthData = null;
        MonthDataList = null!;
        SelectSeries = null!;
        WeekDateList = null!;
        WeeklyResourceRows = null!;
        _optionalMonths = null;
    }


    /// <summary>
    /// 当前选中的月份数据（用于右侧展示）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShareRecordImageCommand))]
    private InterKnotReportSummary? selectMonthData;


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShareRecordImageCommand))]
    private bool isSharingRecordImage;


    /// <summary>
    /// 历史月份数据列表（从本地数据库加载，存储为序列化 JSON）。
    /// </summary>
    [ObservableProperty]
    private List<InterKnotReportSummary> monthDataList;


    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;


    // ===== 新增：周表格相关状态 =====

    /// <summary>
    /// 当前选中的周起始日期（固定为周一）。
    /// </summary>
    [ObservableProperty]
    private DateOnly selectedWeekStart;

    /// <summary>
    /// 日期表头 7 列数据。
    /// </summary>
    [ObservableProperty]
    private List<WeekDateCell> weekDateList = [];

    /// <summary>
    /// 3 行资源数据（菲林、加密母带、邦布券）。
    /// </summary>
    [ObservableProperty]
    private List<WeeklyResourceRow> weeklyResourceRows = [];

    /// <summary>
    /// 当前周的日期范围显示文本（例如 2026/07/06 - 2026/07/12）。
    /// </summary>
    [ObservableProperty]
    private string weekRangeText = "";

    /// <summary>
    /// 是否可以切换到上一周（上一周至少包含选中月一天时为 true）。
    /// </summary>
    [ObservableProperty]
    private bool canGoPreviousWeek;

    /// <summary>
    /// 是否可以切换到下一周（下一周至少包含选中月一天时为 true）。
    /// </summary>
    [ObservableProperty]
    private bool canGoNextWeek;


    partial void OnSelectedWeekStartChanged(DateOnly value)
    {
        RefreshWeeklyDailyDataTable();
    }


    /// <summary>
    /// API 返回的可查询月份列表（格式 yyyyMM），用于判断刷新按钮是否应显示。
    /// </summary>
    private List<string>? _optionalMonths;

    // 拖拽切周状态
    private double _pointerPressX;
    private bool _isPointerDragging;

    /// <summary>
    /// 当前选中的「统计数据」月份是否在服务器可查询列表中，控制刷新按钮可见性。
    /// </summary>
    [ObservableProperty]
    private bool isRefreshButtonVisible;


    private static readonly Dictionary<string, Color> actionColorMap = new Dictionary<string, Color>()
    {
        ["daily_activity_rewards"] = Color.FromArgb(0xFF, 0x5C, 0xC8, 0x3D),
        ["growth_rewards"] = Color.FromArgb(0xFF, 0xA2, 0xD1, 0x04),
        ["event_rewards"] = Color.FromArgb(0xFF, 0xFF, 0xDE, 0x00),
        ["hollow_rewards"] = Color.FromArgb(0xFF, 0xFF, 0x44, 0x83),
        ["shiyu_rewards"] = Color.FromArgb(0xFF, 0x57, 0xBF, 0xF7),
        ["mail_rewards"] = Color.FromArgb(0xFF, 0xC9, 0x2A, 0xDE),
        ["other_rewards"] = Color.FromArgb(0xFF, 0xF1, 0xAD, 0x3D),
    };


    private static readonly Dictionary<string, TimeSpan> interKnotServerOffsetMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prod_gf_cn"] = TimeSpan.FromHours(8),
        ["prod_gf_jp"] = TimeSpan.FromHours(8),
        ["prod_gf_sg"] = TimeSpan.FromHours(8),
        ["prod_gf_eu"] = TimeSpan.FromHours(1),
        ["prod_gf_usa"] = TimeSpan.FromHours(-5),
    };



    /// <summary>
    /// 初始化页面数据。
    /// 注意：即使本地数据库已有缓存，也会先调用 GetCurrentSummaryAsync 刷新当前月数据。
    /// </summary>
    [RelayCommand]
    private async Task InitializeDataAsync()
    {
        await Task.Delay(16);
        await GetCurrentSummaryAsync();   // 总是请求当前月最新数据（含 OptionalMonth）
        InitializeSelectedWeek();         // 设置默认周为服务器今天所在周（周一）
        GetMonthDataList();               // 从本地 DB 加载历史月份列表
        // 若本地有统计数据，自动选中最新月份（列表已按 DataMonth DESC 排序，首项即最新）
        if (MonthDataList?.Count > 0)
        {
            ListView_MonthDataList.SelectedItem = MonthDataList[0];
        }
    }




    /// <summary>
    /// 获取当前月绳网月报汇总数据。
    /// 同时填充可查询月份列表（用于顶部“获取详情”菜单和刷新按钮可见性）。
    /// </summary>
    private async Task GetCurrentSummaryAsync()
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            var summary = await _gameRecordService.GetInterKnotReportSummaryAsync(gameRole);
            // 缓存可查询月份列表，供刷新按钮可见性判断使用
            _optionalMonths = summary.OptionalMonth?.ToList();
            MenuFlyout_GetDetails.Items.Clear();
            foreach (string monthStr in summary.OptionalMonth)
            {
                if (DateTime.TryParseExact(monthStr, "yyyyMM", null, System.Globalization.DateTimeStyles.None, out DateTime time))
                {
                    MenuFlyout_GetDetails.Items.Add(new MenuFlyoutItem
                    {
                        Text = time.ToString("MMM"),
                        Command = GetFullDataDetailsCommand,
                        CommandParameter = monthStr,
                    });
                }
                else
                {
                    MenuFlyout_GetDetails.Items.Add(new MenuFlyoutItem
                    {
                        Text = monthStr,
                        Command = GetFullDataDetailsCommand,
                        CommandParameter = monthStr,
                    });
                }
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get realtime inter knot report data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            GameRecordPage.HandleMiHoYoApiException(ex, preferredRole: gameRole);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get realtime inter knot report data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            GameRecordPage.HandleMiHoYoHttpException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get realtime inter knot report data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 从本地数据库加载该角色的历史月份数据列表（Summary 级别，存储为序列化对象）。
    /// </summary>
    private void GetMonthDataList()
    {
        try
        {
            SelectMonthData = null;
            MonthDataList = _gameRecordService.GetInterKnotReportSummaryList(gameRole);
            Image_Emoji.Visibility = MonthDataList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load inter knot report month data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }





    /// <summary>
    /// 获取指定月份的详细数据（增量更新）。
    /// 始终从服务器拉取最新的统计摘要（覆盖本地），再增量拉取每日明细。
    /// 拉取完成后刷新列表并选中该月。
    /// </summary>
    [RelayCommand]
    private async Task GetDataDetailsAsync(string month)
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            // 始终请求统计摘要（覆盖本地数据），不再复用本地缓存的摘要
            var summary = await _gameRecordService.GetInterKnotReportSummaryAsync(gameRole, month);
            foreach (var item in summary.MonthData.List)
            {
                await _gameRecordService.GetInterKnotReportDetailAsync(gameRole, month, item.DataType);
            }
            GetMonthDataList();
            // 获取完成后自动选中对应月份，触发右侧内容区展示
            var selected = MonthDataList?.FirstOrDefault(x => x.DataMonth == month);
            if (selected != null)
            {
                ListView_MonthDataList.SelectedItem = selected;
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get inter knot report data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            GameRecordPage.HandleMiHoYoApiException(ex, preferredRole: gameRole);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get inter knot report data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            GameRecordPage.HandleMiHoYoHttpException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get inter knot report data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Error(ex);
        }
    }


    /// <summary>
    /// 获取指定月份的全部详细数据（全量覆盖）。
    /// 始终从服务器拉取最新的统计摘要（覆盖本地），再全量拉取每日明细（先删后写）。
    /// 与 <see cref="GetDataDetailsAsync"/> 的区别在于每日明细使用全量覆盖而非增量更新。
    /// 拉取完成后刷新列表并选中该月。
    /// </summary>
    [RelayCommand]
    private async Task GetFullDataDetailsAsync(string month)
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            // 始终请求统计摘要（覆盖本地数据）
            var summary = await _gameRecordService.GetInterKnotReportSummaryAsync(gameRole, month);
            // 全量覆盖每日明细（forceOverwrite: true）
            foreach (var item in summary.MonthData.List)
            {
                await _gameRecordService.GetInterKnotReportDetailAsync(gameRole, month, item.DataType, forceOverwrite: true);
            }
            GetMonthDataList();
            var selected = MonthDataList?.FirstOrDefault(x => x.DataMonth == month);
            if (selected != null)
            {
                ListView_MonthDataList.SelectedItem = selected;
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get inter knot report full data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            GameRecordPage.HandleMiHoYoApiException(ex, preferredRole: gameRole);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get inter knot report full data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            GameRecordPage.HandleMiHoYoHttpException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get inter knot report full data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Error(ex);
        }
    }





    /// <summary>
    /// 月份列表选中变化：更新右侧展示数据，决定是否显示“刷新”按钮（仅服务器可选月份可刷新）。
    /// </summary>
    // ===== 周切换命令 =====

    /// <summary>
    /// 切换到上一周（若不可切换则忽略）。
    /// </summary>
    [RelayCommand]
    private void PreviousWeek()
    {
        if (!CanGoPreviousWeek) return;
        SelectedWeekStart = SelectedWeekStart.AddDays(-7);
        // OnSelectedWeekStartChanged 会自动调用 Refresh
    }

    /// <summary>
    /// 切换到下一周（若不可切换则忽略）。
    /// </summary>
    [RelayCommand]
    private void NextWeek()
    {
        if (!CanGoNextWeek) return;
        SelectedWeekStart = SelectedWeekStart.AddDays(7);
    }



    // ===== 拖拽切周 =====

    private void DailyTable_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            var point = e.GetCurrentPoint(el);
            _pointerPressX = point.Position.X;
            _isPointerDragging = true;
            el.CapturePointer(e.Pointer);
        }
    }

    private void DailyTable_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerDragging) return;
        _isPointerDragging = false;

        if (sender is UIElement el)
        {
            el.ReleasePointerCapture(e.Pointer);
        }

        var point = e.GetCurrentPoint(sender as UIElement);
        double delta = point.Position.X - _pointerPressX;

        const double threshold = 80.0;
        if (Math.Abs(delta) > threshold)
        {
            if (delta > 0)
            {
                // 向右拖 → 上一周
                PreviousWeek();
            }
            else
            {
                // 向左拖 → 下一周
                NextWeek();
            }
        }
    }

    private void DailyTable_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isPointerDragging = false;
        if (sender is UIElement el)
        {
            el.ReleasePointerCapture(e.Pointer);
        }
    }




    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is InterKnotReportSummary data)
            {
                SelectMonthData = data;
                // 仅当该月在 API 返回的 OptionalMonth 中时才允许用户刷新。
                IsRefreshButtonVisible = _optionalMonths?.Contains(data.DataMonth) ?? false;
                SelectSeries = SelectMonthData.MonthData.IncomeComponents.Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action), x.Num)).ToList();

                // 切月时重置为选中月的默认周：
                // 当前服务器月 → 今天所在周；历史月 → 该月第一天所在周。
                // 赋值后若未变化则显式刷新（避免 OnChanged 不触发）。
                var serverToday = GetServerToday(data.Region);
                DateOnly defaultWeek;
                if (DateTime.TryParseExact(data.DataMonth, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    int y = dt.Year;
                    int m = dt.Month;
                    defaultWeek = WeeklyDailyDataHelper.ComputeDefaultWeekStart(y, m, serverToday);
                }
                else
                {
                    defaultWeek = WeeklyDailyDataHelper.GetMonday(serverToday);
                }

                if (SelectedWeekStart == defaultWeek)
                {
                    RefreshWeeklyDailyDataTable();
                }
                else
                {
                    SelectedWeekStart = defaultWeek;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selection changed ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }



    /// <summary>
    /// 刷新周表格每日数据（仅使用数据库缓存，不发起网络请求）。
    /// 根据 SelectedWeekStart + 当前选中月份的 region 重新计算 7 天表头和 3 行资源。
    /// 同时根据选中月计算箭头是否可切换。
    /// </summary>
    private void RefreshWeeklyDailyDataTable()
    {
        try
        {
            if (SelectMonthData is null)
            {
                WeekDateList = [];
                WeeklyResourceRows = [];
                WeekRangeText = "";
                CanGoPreviousWeek = false;
                CanGoNextWeek = false;
                return;
            }

            var serverOffset = GetInterKnotReportServerOffset(SelectMonthData.Region);
            var serverToday = GetServerToday(SelectMonthData.Region);
            var dates = WeeklyDailyDataHelper.GetWeekDates(SelectedWeekStart);

            WeekDateList = WeeklyDailyDataHelper.BuildWeekDateCells(dates, serverToday);
            WeeklyResourceRows = BuildWeeklyResourceRows(SelectMonthData, dates, serverOffset, serverToday);

            // 设置周范围显示（使用短横线分隔）
            if (dates.Count > 0)
            {
                var start = dates[0];
                var end = dates[^1];
                WeekRangeText = $"{start:yyyy/MM/dd} - {end:yyyy/MM/dd}";
            }

            // 计算箭头可见性：上一周/下一周至少包含选中月一天
            if (DateTime.TryParseExact(SelectMonthData.DataMonth, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                int y = dt.Year;
                int m = dt.Month;
                var firstDay = new DateOnly(y, m, 1);
                var lastDay = new DateOnly(y, m, DateTime.DaysInMonth(y, m));
                CanGoPreviousWeek = WeeklyDailyDataHelper.ComputeCanGoPrevious(SelectedWeekStart, firstDay);
                CanGoNextWeek = WeeklyDailyDataHelper.ComputeCanGoNext(SelectedWeekStart, lastDay);
            }
            else
            {
                CanGoPreviousWeek = false;
                CanGoNextWeek = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh weekly daily data table");
        }
    }

    /// <summary>
    /// 构建周资源表格的 3 行数据。
    /// 跨月周会查询涉及的多个月份的 DB 缓存。
    /// 未来日期 Count 显示为空白。
    /// </summary>
    private List<WeeklyResourceRow> BuildWeeklyResourceRows(
        InterKnotReportSummary summary,
        IReadOnlyList<DateOnly> dates,
        TimeSpan serverOffset,
        DateOnly serverToday)
    {
        // 计算该周涉及的月份（yyyyMM）
        var months = dates
            .Select(d => d.ToString("yyyyMM", CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

        // 从 DB 读取这些月份的所有明细（不触发网络）
        var allItems = new List<InterKnotReportDetailItem>();
        foreach (var month in months)
        {
            allItems.AddRange(_gameRecordService.GetInterKnotReportDetailItems(summary.Uid, month));
        }

        // 仅保留落在本周日期范围内的项，按 (DataType, Date) 聚合 Number
        var dateSet = dates.ToHashSet();
        var map = allItems
            .Select(item => new
            {
                item.DataType,
                Date = DateOnly.FromDateTime(item.Time.ToOffset(serverOffset).Date),
                item.Number,
            })
            .Where(x => dateSet.Contains(x.Date))
            .GroupBy(x => (x.DataType, x.Date))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Number));

        // 固定顺序：菲林、加密母带、邦布券（注意保持 MatserTapeData 拼写）
        string[] resourceTypes =
        [
            InterKnotReportDataType.PolychromesData,
            InterKnotReportDataType.MatserTapeData,
            InterKnotReportDataType.BooponsData,
        ];

        return resourceTypes.Select(type => new WeeklyResourceRow
        {
            DataType = type,
            Name = DataTypeToName(type),
            Icon = DataTypeToImage(type),
            Cells = dates.Select(date => new WeeklyResourceCell
            {
                Date = date,
                Count = map.GetValueOrDefault((type, date)),
                IsFuture = date > serverToday,
            }).ToList(),
        }).ToList();
    }



    private static TimeSpan GetInterKnotReportServerOffset(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return TimeSpan.FromHours(8);
        }
        if (interKnotServerOffsetMap.TryGetValue(region, out var offset))
        {
            return offset;
        }
        if (region.Contains("usa", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(-5);
        }
        if (region.Contains("eu", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(1);
        }
        return TimeSpan.FromHours(8);
    }



    private static void AggregateDailyStats(int[] target, IEnumerable<InterKnotReportDetailItem> source, int year, int month, TimeSpan serverOffset)
    {
        foreach (var item in source)
        {
            var serverTime = item.Time.ToOffset(serverOffset);
            if (serverTime.Year != year || serverTime.Month != month)
            {
                continue;
            }
            int day = serverTime.Day;
            if ((uint)(day - 1) < (uint)target.Length)
            {
                target[day - 1] += item.Number;
            }
        }
    }



    /// <summary>
    /// 根据 region 获取服务器“今天”的本地日期（DateOnly）。
    /// </summary>
    private static DateOnly GetServerToday(string? region)
    {
        var offset = GetInterKnotReportServerOffset(region);
        return DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(offset).Date);
    }

    /// <summary>
    /// 初始化为服务器今天所在周的周一（初始加载时使用；切月逻辑由 SelectionChanged 负责重置）。
    /// </summary>
    private void InitializeSelectedWeek()
    {
        if (gameRole is null)
        {
            SelectedWeekStart = DateOnly.FromDateTime(DateTime.Today);
            return;
        }
        var serverToday = GetServerToday(gameRole.Region);
        SelectedWeekStart = WeeklyDailyDataHelper.GetMonday(serverToday);
    }




    // CalendarDayData 已移除（不再使用）



    // 模型已移至 Starward.Features.GameRecord.WeeklyDailyData（共享）

    /// <summary>
    /// 将绳网月报数据类型映射为本地化显示名称（不直接使用 API 返回的 <c>data_name</c>，避免非中文界面仍显示中文）。
    /// </summary>
    /// <param name="dataType">数据类型常量，见 <see cref="InterKnotReportDataType"/>。</param>
    /// <returns>本地化名称；未知类型时原样返回 <paramref name="dataType"/>。</returns>
    public static string DataTypeToName(string dataType)
    {
        return dataType switch
        {
            InterKnotReportDataType.PolychromesData => Lang.InterKnotMonthlyReportPage_Polychromes,
            InterKnotReportDataType.MatserTapeData => Lang.InterKnotMonthlyReportPage_MasterTape,
            InterKnotReportDataType.BooponsData => Lang.InterKnotMonthlyReportPage_Boopons,
            _ => dataType,
        };
    }



    /// <summary>
    /// 将绳网月报数据类型映射为缓存的图标实例（复用 BitmapImage，避免切周闪烁）。
    /// </summary>
    /// <param name="type">数据类型常量，见 <see cref="InterKnotReportDataType"/>。</param>
    /// <returns>对应图标；未知类型时返回 null。</returns>
    public static BitmapImage? DataTypeToImage(string type)
    {
        return type switch
        {
            InterKnotReportDataType.PolychromesData => PolychromesIcon,
            InterKnotReportDataType.MatserTapeData => MasterTapeIcon,
            InterKnotReportDataType.BooponsData => BooponsIcon,
            _ => null,
        };
    }




    public static string ActionName(string action)
    {
        return action switch
        {
            "daily_activity_rewards" => Lang.InterKnotMonthlyReportPage_DailyActivityRewardeds,
            "growth_rewards" => Lang.InterKnotMonthlyReportPage_DevelopmentRewards,
            "event_rewards" => Lang.InterKnotMonthlyReportPage_EventRewards,
            "hollow_rewards" => Lang.InterKnotMonthlyReportPage_HollowZeroRewards,
            "shiyu_rewards" => Lang.InterKnotMonthlyReportPage_ShiyuDefenseRewards,
            "mail_rewards" => Lang.InterKnotMonthlyReportPage_MailRewards,
            "other_rewards" => Lang.InterKnotMonthlyReportPage_OtherRewards,
            _ => action,
        };
    }




    public static int GetPolychromeCount(InterKnotReportMonthData monthData)
    {
        return monthData?.List?.FirstOrDefault(a => a.DataType == InterKnotReportDataType.PolychromesData)?.Count ?? 0;
    }


    /// <summary>将当前月绳网月报绘制为分享图。</summary>
    [RelayCommand(CanExecute = nameof(CanShareRecordImage))]
    private async Task ShareRecordImageAsync()
    {
        if (SelectMonthData is null)
        {
            return;
        }

        MonthlyReportShareSnapshot data = new()
        {
            FileStem = "interknot_report",
            Title = $"{Lang.TravelersDiaryPage_HistoricalData}  {SelectMonthData.DataMonth}",
            Currencies = (SelectMonthData.MonthData?.List ?? [])
                .Select(a => new MonthlyReportShareCurrency
                {
                    Icon = DataTypeToImage(a.DataType)?.UriSource?.OriginalString ?? "",
                    Name = DataTypeToName(a.DataType),
                    Value = a.Count.ToString(CultureInfo.CurrentCulture),
                })
                .ToList(),
            SourcesTitle = Lang.InterKnotMonthlyReportPage_PolychromeRevenueStreams,
            Sources = MonthlyReportShareSnapshot.CaptureSources(SelectSeries),
            DailyTitle = $"{Lang.HoyolabToolboxPage_DailyData}  {WeekRangeText}",
            Days = MonthlyReportShareSnapshot.CaptureDays(WeekDateList),
            Rows = MonthlyReportShareSnapshot.CaptureRows(WeeklyResourceRows),
        };
        await GameRecordShareHelper.ShareAsync(
            this,
            gameRole,
            _logger,
            busy => IsSharingRecordImage = busy,
            (bg, accent) => MonthlyReportShareRenderer.RenderAndSaveAsync(data, gameRole.Uid, bg, accent));
    }


    private bool CanShareRecordImage()
        => !IsSharingRecordImage && gameRole is not null && SelectMonthData is not null;



    // 日期单元格视觉样式已移至 WeeklyDailyDataHelper（XAML 直接引用 wdd:WeeklyDailyDataHelper.GetDateCell*）




}
