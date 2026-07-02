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

    private readonly ILogger<InterKnotMonthlyReportPage> _logger = AppConfig.GetLogger<InterKnotMonthlyReportPage>();


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    public InterKnotMonthlyReportPage()
    {
        this.InitializeComponent();
    }



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
        CurrentSummary = null!;
        SelectMonthData = null;
        MonthDataList = null!;
        CurrentSeries = null!;
        SelectSeries = null!;
        DayDataList = null!;
    }


    [ObservableProperty]
    private InterKnotReportSummary currentSummary;


    [ObservableProperty]
    private InterKnotReportSummary? selectMonthData;


    [ObservableProperty]
    private List<InterKnotReportSummary> monthDataList;


    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? currentSeries;


    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;


    [ObservableProperty]
    private List<CalendarDayData> dayDataList;



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



    [RelayCommand]
    private async Task InitializeDataAsync()
    {
        await Task.Delay(16);
        await GetCurrentSummaryAsync();
        GetMonthDataList();
    }




    private async Task GetCurrentSummaryAsync()
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            CurrentSummary = await _gameRecordService.GetInterKnotReportSummaryAsync(gameRole);
            MenuFlyout_GetDetails.Items.Clear();
            foreach (string monthStr in CurrentSummary.OptionalMonth)
            {
                if (DateTime.TryParseExact(monthStr, "yyyyMM", null, System.Globalization.DateTimeStyles.None, out DateTime time))
                {
                    MenuFlyout_GetDetails.Items.Add(new MenuFlyoutItem
                    {
                        Text = time.ToString("MMM"),
                        Command = GetDataDetailsCommand,
                        CommandParameter = monthStr,
                    });
                }
                else
                {
                    MenuFlyout_GetDetails.Items.Add(new MenuFlyoutItem
                    {
                        Text = monthStr,
                        Command = GetDataDetailsCommand,
                        CommandParameter = monthStr,
                    });
                }
            }
            CurrentSeries = CurrentSummary.MonthData.IncomeComponents.Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action))).ToList();
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





    [RelayCommand]
    private async Task GetDataDetailsAsync(string month)
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            // 若本地列表中已有该月摘要，直接复用，避免多余的网络请求
            var summary = MonthDataList?.FirstOrDefault(x => x.DataMonth == month)
                ?? await _gameRecordService.GetInterKnotReportSummaryAsync(gameRole, month);
            foreach (var item in summary.MonthData.List)
            {
                await _gameRecordService.GetInterKnotReportDetailAsync(gameRole, month, item.DataType);
            }
            GetMonthDataList();
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





    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is InterKnotReportSummary data)
            {
                SelectMonthData = data;
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
            var items_poly = _gameRecordService.GetInterKnotReportDetailItems(data.Uid, data.DataMonth, InterKnotReportDataType.PolychromesData);
            var items_tape = _gameRecordService.GetInterKnotReportDetailItems(data.Uid, data.DataMonth, InterKnotReportDataType.MatserTapeData);
            var items_boopon = _gameRecordService.GetInterKnotReportDetailItems(data.Uid, data.DataMonth, InterKnotReportDataType.BooponsData);
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
