using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.ZZZ.InterKnotReport;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
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
        DayDataList = null!;
        _optionalMonths = null;
    }


    /// <summary>
    /// 当前选中的月份数据（用于右侧展示）。
    /// </summary>
    [ObservableProperty]
    private InterKnotReportSummary? selectMonthData;


    /// <summary>
    /// 历史月份数据列表（从本地数据库加载，存储为序列化 JSON）。
    /// </summary>
    [ObservableProperty]
    private List<InterKnotReportSummary> monthDataList;


    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;


    [ObservableProperty]
    private List<CalendarDayData> dayDataList;


    /// <summary>
    /// API 返回的可查询月份列表（格式 yyyyMM），用于判断刷新按钮是否应显示。
    /// </summary>
    private List<string>? _optionalMonths;

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
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get realtime inter knot report data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
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
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get inter knot report data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
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
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get inter knot report full data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
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
    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is InterKnotReportSummary data)
            {
                SelectMonthData = data;
                // 仅当该月在 API 返回的 OptionalMonth 中时才允许用户刷新。
                IsRefreshButtonVisible = _optionalMonths?.Contains(data.DataMonth) ?? false;
                SelectSeries = SelectMonthData.MonthData.IncomeComponents.Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action))).ToList();
                RefreshDailyDataPlot(data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selection changed ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }



    private void RefreshDailyDataPlot(InterKnotReportSummary data)
    {
        try
        {
            // 一次查询所有类型的明细，再按 DataType 分组，避免多次 DB 请求
            var allItems = _gameRecordService.GetInterKnotReportDetailItems(data.Uid, data.DataMonth);
            var items_poly = allItems.Where(x => x.DataType == InterKnotReportDataType.PolychromesData);
            var items_tape = allItems.Where(x => x.DataType == InterKnotReportDataType.MatserTapeData);
            var items_boopon = allItems.Where(x => x.DataType == InterKnotReportDataType.BooponsData);
            int year = int.Parse(data.DataMonth[..4], CultureInfo.InvariantCulture);
            int month = int.Parse(data.DataMonth[4..], CultureInfo.InvariantCulture);
            int days = DateTime.DaysInMonth(year, month);
            TimeSpan serverOffset = GetInterKnotReportServerOffset(data.Region);

            var stats_poly = new int[days];
            AggregateDailyStats(stats_poly, items_poly, year, month, serverOffset);

            var stats_tape = new int[days];
            AggregateDailyStats(stats_tape, items_tape, year, month, serverOffset);

            var stats_boopon = new int[days];
            AggregateDailyStats(stats_boopon, items_boopon, year, month, serverOffset);

            double max_poly = stats_poly.Max();
            double max_tape = stats_tape.Max();
            double max_boopon = stats_boopon.Max();
            max_poly = max_poly == 0 ? double.MaxValue : max_poly;
            max_tape = max_tape == 0 ? double.MaxValue : max_tape;
            max_boopon = max_boopon == 0 ? double.MaxValue : max_boopon;
            var list = new List<CalendarDayData>(days);
            for (int i = 0; i < days; i++)
            {
                list.Add(new CalendarDayData
                {
                    Day = $"{data.DataMonth[4..]}-{i + 1:D2}",
                    Poly = stats_poly[i],
                    Tape = stats_tape[i],
                    Boopon = stats_boopon[i],
                    PolyProgress = stats_poly[i] / max_poly,
                    TapeProgress = stats_tape[i] / max_tape,
                    BooponProgress = stats_boopon[i] / max_boopon,
                });
            }
            DayDataList = list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh daily data plot");
        }
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




    public class CalendarDayData
    {

        public string Day { get; set; }

        public int Poly { get; set; }

        public int Tape { get; set; }

        public int Boopon { get; set; }

        public double PolyProgress { get; set; }

        public double TapeProgress { get; set; }

        public double BooponProgress { get; set; }

    }





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



    public static BitmapImage? DataTypeToImage(string type)
    {
        return type switch
        {
            InterKnotReportDataType.PolychromesData => new BitmapImage(new("ms-appx:///Assets/Image/IconCurrency.png")),
            InterKnotReportDataType.MatserTapeData => new BitmapImage(new("ms-appx:///Assets/Image/GachaTicket2Big.png")),
            InterKnotReportDataType.BooponsData => new BitmapImage(new("ms-appx:///Assets/Image/GachaTicket3Big.png")),
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




}
