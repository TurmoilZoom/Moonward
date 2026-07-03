using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.StarRail.TrailblazeCalendar;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;


namespace Starward.Features.GameRecord.StarRail;

public sealed partial class TrailblazeCalendarPage : PageBase
{


    private readonly ILogger<TrailblazeCalendarPage> _logger = AppConfig.GetLogger<TrailblazeCalendarPage>();


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    public TrailblazeCalendarPage()
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
        SelectMonthData = null;
        MonthDataList = null!;
        SelectSeries = null;
        DayDataList = null!;
        _optionalMonths = null;
    }



    [ObservableProperty]
    private TrailblazeCalendarMonthData? selectMonthData;


    [ObservableProperty]
    private List<TrailblazeCalendarMonthData> monthDataList;


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
        ["daily_reward"] = Color.FromArgb(0xFF, 0xFE, 0xC6, 0x6F),
        ["space_reward"] = Color.FromArgb(0xFF, 0x44, 0xDD, 0x9C),
        ["event_reward"] = Color.FromArgb(0xFF, 0x47, 0xC6, 0xFD),
        ["adventure_reward"] = Color.FromArgb(0xFF, 0x88, 0x7F, 0xFE),
        ["abyss_reward"] = Color.FromArgb(0xFF, 0xDF, 0x53, 0xFE),
        ["mail_reward"] = Color.FromArgb(0xFF, 0xF8, 0x4E, 0x35),
        ["other"] = Color.FromArgb(0xFF, 0xFD, 0xEA, 0x60),
    };



    [RelayCommand]
    private async Task InitializeDataAsync()
    {
        await Task.Delay(16);
        await GetCurrentSummaryAsync();
        GetMonthDataList();
        // 若本地有统计数据，自动选中最新月份（列表已按 Month DESC 排序，首项即最新）
        if (MonthDataList?.Count > 0)
        {
            ListView_MonthDataList.SelectedItem = MonthDataList[0];
        }
    }




    private async Task GetCurrentSummaryAsync()
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            var summary = await _gameRecordService.GetTrailblazeCalendarSummaryAsync(gameRole);
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
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get realtime trailblaze calendar data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get realtime trailblaze calendar data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get realtime trailblaze calendar data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    private void GetMonthDataList()
    {
        try
        {
            SelectMonthData = null;
            MonthDataList = _gameRecordService.GetTrailblazeCalendarMonthDataList(gameRole);
            Image_Emoji.Visibility = MonthDataList.Any() ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load trailblaze calendar month data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
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
            // 若本地列表中已有该月摘要，跳过网络请求直接拉取明细
            if (MonthDataList?.Any(x => x.Month == month) != true)
            {
                await _gameRecordService.GetTrailblazeCalendarSummaryAsync(gameRole, month);
            }
            await _gameRecordService.GetTrailblazeCalendarDetailAsync(gameRole, month, 1);
            await _gameRecordService.GetTrailblazeCalendarDetailAsync(gameRole, month, 2);
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
            _logger.LogError(ex, "Get trailblaze calendar data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get trailblaze calendar data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get trailblaze calendar data details ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, month);
            InAppToast.MainWindow?.Error(ex);
        }
    }





    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is TrailblazeCalendarMonthData data)
            {
                SelectMonthData = data;
                // 当月存在于 API 可选列表中时显示刷新按钮
                IsRefreshButtonVisible = _optionalMonths?.Contains(data.Month) ?? false;
                SelectSeries = SelectMonthData.GroupBy.Select(x => new ColorRectChart.ChartLegend(x.ActionName, x.Percent, actionColorMap.GetValueOrDefault(x.Action))).ToList();
                RefreshDailyDataPlot(data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selection changed ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }



    private void RefreshDailyDataPlot(TrailblazeCalendarMonthData data)
    {
        try
        {
            // 一次查询所有类型的明细，再按 Type 分组，避免多次 DB 请求
            var allItems = _gameRecordService.GetTrailblazeCalendarDetailItems(data.Uid, data.Month);
            var items_jade = allItems.Where(x => x.Type == 1);
            var items_pass = allItems.Where(x => x.Type == 2);
            int days = DateTime.DaysInMonth(int.Parse(data.Month[..4]), int.Parse(data.Month[4..]));
            var x = Enumerable.Range(1, days).ToArray();

            var stats_jade = new int[days];
            foreach (var item in items_jade)
            {
                var day = item.Time.Day;
                if (day <= days)
                {
                    stats_jade[day - 1] += item.Number;
                }
            }

            var stats_pass = new int[days];
            foreach (var item in items_pass)
            {
                var day = item.Time.Day;
                if (day <= days)
                {
                    stats_pass[day - 1] += item.Number;
                }
            }

            double max_jade = stats_jade.Max();
            double max_pass = stats_pass.Max();
            max_jade = max_jade == 0 ? double.MaxValue : max_jade;
            max_pass = max_pass == 0 ? double.MaxValue : max_pass;
            var list = new List<CalendarDayData>(days);
            for (int i = 0; i < days; i++)
            {
                list.Add(new CalendarDayData
                {
                    Day = $"{data.Month[4..]}-{i + 1:D2}",
                    Jade = stats_jade[i],
                    Pass = stats_pass[i],
                    JadeProgress = stats_jade[i] / max_jade,
                    PassProgress = stats_pass[i] / max_pass,
                });
            }
            DayDataList = list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh daily data plot");
        }
    }




    public class CalendarDayData
    {

        public string Day { get; set; }

        public int Jade { get; set; }

        public int Pass { get; set; }

        public double JadeProgress { get; set; }

        public double PassProgress { get; set; }

    }


}
