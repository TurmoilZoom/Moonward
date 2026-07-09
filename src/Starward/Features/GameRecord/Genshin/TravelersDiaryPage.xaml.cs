using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.Genshin.TravelersDiary;
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


namespace Starward.Features.GameRecord.Genshin;

public sealed partial class TravelersDiaryPage : PageBase
{
    /// <summary>
    /// 原神旅行者札记页面（月报数据）。显示当月/历史原石、摩拉收入及每日明细图表。
    /// 打开时会刷新当前月数据，即使本地已有缓存。
    /// </summary>

    private readonly ILogger<TravelersDiaryPage> _logger = AppConfig.GetLogger<TravelersDiaryPage>();


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();



    public TravelersDiaryPage()
    {
        this.InitializeComponent();
    }



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
        //await Task.Delay(16);
        await InitializeDataAsync();
    }


    protected override void OnUnloaded()
    {
        SelectMonthData = null;
        MonthDataList = null!;
        SelectSeries = null;
        WeekDateList = null!;
        WeeklyResourceRows = null!;
        _optionalMonths = null;
    }



    /// <summary>
    /// 当前选中的月份数据（用于右侧展示）。
    /// </summary>
    [ObservableProperty]
    private TravelersDiaryMonthData? selectMonthData;


    /// <summary>
    /// 历史月份数据列表（从本地数据库加载）。
    /// </summary>
    [ObservableProperty]
    private List<TravelersDiaryMonthData> monthDataList;


    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;


    // ===== 周表格相关状态（共享模型） =====

    /// <summary>
    /// 当前选中的周起始日期（周一）。
    /// </summary>
    [ObservableProperty]
    private DateOnly selectedWeekStart;

    /// <summary>
    /// 日期表头 7 列数据。
    /// </summary>
    [ObservableProperty]
    private List<WeekDateCell> weekDateList = [];

    /// <summary>
    /// 2 行资源数据（原石、摩拉）。
    /// </summary>
    [ObservableProperty]
    private List<WeeklyResourceRow> weeklyResourceRows = [];

    /// <summary>
    /// 当前周的日期范围显示文本。
    /// </summary>
    [ObservableProperty]
    private string weekRangeText = "";

    /// <summary>
    /// 是否可以切换到上一周。
    /// </summary>
    [ObservableProperty]
    private bool canGoPreviousWeek;

    /// <summary>
    /// 是否可以切换到下一周。
    /// </summary>
    [ObservableProperty]
    private bool canGoNextWeek;


    partial void OnSelectedWeekStartChanged(DateOnly value)
    {
        RefreshWeeklyDailyDataTable();
    }


    /// <summary>
    /// API 返回的可查询月份列表（月份数字 1-12），用于判断刷新按钮是否应显示。
    /// </summary>
    private List<int>? _optionalMonths;

    /// <summary>
    /// 当前选中的「统计数据」月份是否在服务器可查询列表中，控制刷新按钮可见性。
    /// </summary>
    [ObservableProperty]
    private bool isRefreshButtonVisible;


    private static readonly Dictionary<int, Color> actionColorMap = new Dictionary<int, Color>()
    {
        [0] = Color.FromArgb(0xFF, 0x72, 0xA7, 0xC6),
        [1] = Color.FromArgb(0xFF, 0xD4, 0x64, 0x63),
        [2] = Color.FromArgb(0xFF, 0x6F, 0xB0, 0xB2),
        [3] = Color.FromArgb(0xFF, 0xBC, 0x99, 0x59),
        [4] = Color.FromArgb(0xFF, 0x72, 0x98, 0x6F),
        [5] = Color.FromArgb(0xFF, 0x79, 0x6B, 0xA6),
        [6] = Color.FromArgb(0xFF, 0x59, 0x7D, 0x9F),
        [7] = Color.FromArgb(0xFF, 0x7A, 0x7C, 0xB2),
    };



    /// <summary>
    /// 初始化页面数据。
    /// 注意：即使本地数据库已有缓存，也会先调用 GetCurrentSummaryAsync 刷新当前月数据。
    /// </summary>
    [RelayCommand]
    private async Task InitializeDataAsync()
    {
        //await Task.Delay(16);
        await GetCurrentSummaryAsync();   // 总是请求当前月最新数据（含 OptionalMonth）
        InitializeSelectedWeek();         // 设置默认周为今天所在周
        GetMonthDataList();               // 从本地 DB 加载历史月份列表
        // 若本地有统计数据，自动选中最新月份（列表已按 Year DESC, Month DESC 排序，首项即最新）
        if (MonthDataList?.Count > 0)
        {
            ListView_MonthDataList.SelectedItem = MonthDataList[0];
        }
    }




    /// <summary>
    /// 获取当前月旅行者札记汇总数据。
    /// 同时填充可查询月份列表（用于顶部“获取详情”菜单和刷新按钮可见性判断）。
    /// </summary>
    private async Task GetCurrentSummaryAsync()
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            var summary = await _gameRecordService.GetTravelersDiarySummaryAsync(gameRole);
            // 缓存可查询月份列表，供刷新按钮可见性判断使用
            _optionalMonths = summary.OptionalMonth?.ToList();
            MenuFlyout_GetDetails.Items.Clear();
            foreach (int month in Enumerable.Reverse(summary.OptionalMonth))
            {
                MenuFlyout_GetDetails.Items.Add(new MenuFlyoutItem
                {
                    Text = new DateTime(2023, month, 1).ToString("MMM"),
                    Command = GetFullDataDetailsCommand,
                    CommandParameter = month,
                });
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get realtime traveler's diary data details ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get realtime traveler's diary data details ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get realtime traveler's diary data details ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 从本地数据库加载该角色的历史月份数据列表（Summary 级别）。
    /// </summary>
    private void GetMonthDataList()
    {
        try
        {
            SelectMonthData = null;
            MonthDataList = _gameRecordService.GetTravelersDiaryMonthDataList(gameRole);
            Image_Emoji.Visibility = MonthDataList.Any() ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load traveler's diary month data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }





    /// <summary>
    /// 获取指定月份的详细数据（增量更新）。
    /// 始终从服务器拉取最新的统计摘要（覆盖本地），再增量拉取每日明细。
    /// 拉取完成后刷新列表并选中该月。
    /// </summary>
    [RelayCommand]
    private async Task GetDataDetailsAsync(int month)
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            // 始终请求统计摘要（覆盖本地数据），不再跳过已有月份的 summary 请求
            await _gameRecordService.GetTravelersDiarySummaryAsync(gameRole, month);
            await _gameRecordService.GetTravelersDiaryDetailAsync(gameRole, month, 1);
            await _gameRecordService.GetTravelersDiaryDetailAsync(gameRole, month, 2);
            GetMonthDataList();
            // 获取完成后自动选中对应月份，触发右侧内容区展示
            var selected = MonthDataList?.FirstOrDefault(x => x.Month == month);
            if (selected != null)
            {
                ListView_MonthDataList.SelectedItem = selected;
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get traveler's diary data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get traveler's diary data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get traveler's diary data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
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
    private async Task GetFullDataDetailsAsync(int month)
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            // 始终请求统计摘要（覆盖本地数据）
            await _gameRecordService.GetTravelersDiarySummaryAsync(gameRole, month);
            // 全量覆盖每日明细（forceOverwrite: true）
            await _gameRecordService.GetTravelersDiaryDetailAsync(gameRole, month, 1, forceOverwrite: true);
            await _gameRecordService.GetTravelersDiaryDetailAsync(gameRole, month, 2, forceOverwrite: true);
            GetMonthDataList();
            var selected = MonthDataList?.FirstOrDefault(x => x.Month == month);
            if (selected != null)
            {
                ListView_MonthDataList.SelectedItem = selected;
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get traveler's diary full data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get traveler's diary full data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get traveler's diary full data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Error(ex);
        }
    }





    /// <summary>
    /// 月份列表选中变化：更新右侧展示数据，决定是否显示“刷新”按钮（仅服务器可选月份可刷新）。
    /// 同时设置选中月的默认周起始。
    /// </summary>
    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is TravelersDiaryMonthData data)
            {
                SelectMonthData = data;
                // 仅当该月在 API 返回的 OptionalMonth 中时才允许用户刷新（避免无效请求）。
                IsRefreshButtonVisible = _optionalMonths?.Contains(data.Month) ?? false;
                SelectSeries = SelectMonthData.PrimogemsGroupBy.Select(x => new ColorRectChart.ChartLegend(ActionName(x.ActionId, x.ActionName), x.Percent, actionColorMap.GetValueOrDefault(x.ActionId), x.Number)).ToList();

                // 切月重置默认周：当月用今天所在周，历史月用该月1号所在周。
                var today = DateOnly.FromDateTime(DateTime.Today);
                var defaultWeek = WeeklyDailyDataHelper.ComputeDefaultWeekStart(data.Year, data.Month, today);

                if (SelectedWeekStart == defaultWeek)
                {
                    RefreshWeeklyDailyDataTable();
                }
                else
                {
                    SelectedWeekStart = defaultWeek;
                }

                // 与设置页一致：在本帧同步预置 Opacity/Offset 再开启动画，避免 Low 优先级导致「先完整显示再隐藏」的闪烁。
                EntranceAnimation.PlayFromRight(StackPanel_RightContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selection changed ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }




    // ===== 周切换命令 =====

    /// <summary>
    /// 切换到上一周（守卫不可切换时）。
    /// </summary>
    [RelayCommand]
    private void PreviousWeek()
    {
        if (!CanGoPreviousWeek) return;
        SelectedWeekStart = SelectedWeekStart.AddDays(-7);
    }

    /// <summary>
    /// 切换到下一周（守卫不可切换时）。
    /// </summary>
    [RelayCommand]
    private void NextWeek()
    {
        if (!CanGoNextWeek) return;
        SelectedWeekStart = SelectedWeekStart.AddDays(7);
    }


    // ===== 拖拽切周 =====

    private double _pointerPressX;
    private bool _isPointerDragging;

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
                PreviousWeek();
            }
            else
            {
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


    /// <summary>
    /// 刷新周表格每日数据。
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

            var today = DateOnly.FromDateTime(DateTime.Today);
            var dates = WeeklyDailyDataHelper.GetWeekDates(SelectedWeekStart);

            WeekDateList = WeeklyDailyDataHelper.BuildWeekDateCells(dates, today);
            WeeklyResourceRows = BuildWeeklyResourceRows(SelectMonthData, dates, today);

            if (dates.Count > 0)
            {
                var start = dates[0];
                var end = dates[^1];
                WeekRangeText = $"{start:yyyy/MM/dd} - {end:yyyy/MM/dd}";
            }

            // 箭头：上一/下一周是否至少含选中月一天
            var firstDay = new DateOnly(SelectMonthData.Year, SelectMonthData.Month, 1);
            var lastDay = new DateOnly(SelectMonthData.Year, SelectMonthData.Month, DateTime.DaysInMonth(SelectMonthData.Year, SelectMonthData.Month));
            CanGoPreviousWeek = WeeklyDailyDataHelper.ComputeCanGoPrevious(SelectedWeekStart, firstDay);
            CanGoNextWeek = WeeklyDailyDataHelper.ComputeCanGoNext(SelectedWeekStart, lastDay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh weekly daily data table");
        }
    }

    /// <summary>
    /// 构建原神周资源表格（原石/摩拉 2 行）。
    /// 按 item.Time 本地日期聚合，不使用区服偏移。
    /// </summary>
    private List<WeeklyResourceRow> BuildWeeklyResourceRows(TravelersDiaryMonthData summary, IReadOnlyList<DateOnly> dates, DateOnly today)
    {
        // 可能跨月，但按现有月数据加载；为简单起见只查当前选中月明细（与旧逻辑一致）
        var allItems = _gameRecordService.GetTravelersDiaryDetailItems(summary.Uid, summary.Year, summary.Month);

        var dateSet = dates.ToHashSet();
        var map = allItems
            .Select(item => new
            {
                item.Type,
                Date = DateOnly.FromDateTime(item.Time.Date),  // 本地日期
                item.Number,
            })
            .Where(x => dateSet.Contains(x.Date))
            .GroupBy(x => (x.Type, x.Date))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Number));

        // 类型 1=原石，2=摩拉
        return new List<WeeklyResourceRow>
        {
            new WeeklyResourceRow
            {
                DataType = "1",
                Name = Lang.TravelersDiaryPage_Primogems,
                Icon = new BitmapImage(new Uri("ms-appx:///Assets/Image/UI_ItemIcon_201.png")),
                Cells = dates.Select(d => new WeeklyResourceCell
                {
                    Date = d,
                    Count = map.GetValueOrDefault((1, d)),
                    IsFuture = d > today,
                }).ToList(),
            },
            new WeeklyResourceRow
            {
                DataType = "2",
                Name = Lang.TravelersDiaryPage_Mora,
                Icon = new BitmapImage(new Uri("ms-appx:///Assets/Image/UI_ItemIcon_202.png")),
                Cells = dates.Select(d => new WeeklyResourceCell
                {
                    Date = d,
                    Count = map.GetValueOrDefault((2, d)),
                    IsFuture = d > today,
                }).ToList(),
            },
        };
    }


    /// <summary>
    /// 初始化选中周为今天所在周的周一。
    /// </summary>
    private void InitializeSelectedWeek()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        SelectedWeekStart = WeeklyDailyDataHelper.GetMonday(today);
    }


    /// <summary>
    /// 将旅行者札记收入类型映射为本地化名称（优先按 <paramref name="actionId"/>，不直接使用 API 的 action_name）。
    /// </summary>
    /// <param name="actionId">收入类型 ID（0–7）。</param>
    /// <param name="fallbackName">API 返回的原始名称，未知 ID 时回退使用。</param>
    /// <returns>本地化名称。</returns>
    public static string ActionName(int actionId, string? fallbackName)
    {
        return actionId switch
        {
            0 => Lang.TravelersDiaryPage_ActionDailyActivity,
            1 => Lang.TravelersDiaryPage_ActionSpiralAbyss,
            2 => Lang.TravelersDiaryPage_ActionImaginariumTheater,
            3 => Lang.TravelersDiaryPage_ActionEventRewards,
            4 => Lang.TravelersDiaryPage_ActionDailyCommission,
            5 => Lang.TravelersDiaryPage_ActionExploration,
            6 => Lang.TravelersDiaryPage_ActionMailRewards,
            7 => Lang.TravelersDiaryPage_ActionOther,
            _ => fallbackName ?? actionId.ToString(),
        };
    }


}
