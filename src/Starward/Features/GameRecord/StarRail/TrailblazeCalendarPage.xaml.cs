using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.StarRail.TrailblazeCalendar;
using Starward.Features.Setting;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;


namespace Starward.Features.GameRecord.StarRail;

/// <summary>
/// 星穹铁道「开拓月历」页面。左侧为已缓存月份列表与「获取详情」按钮，右侧展示选中月的统计数据（资源总量 + 星琼收入构成）及按日聚合的明细。
/// 数据流：进入页面时拉取当前月汇总写入 SQLite 并默认选中当月；「获取详情」按月份拉汇总 + 星琼/星轨票明细分页并写入 SQLite；
/// 点击月份仅从本地缓存读取，不再发起网络请求。
/// </summary>
public sealed partial class TrailblazeCalendarPage : PageBase
{


    private readonly ILogger<TrailblazeCalendarPage> _logger = AppConfig.GetLogger<TrailblazeCalendarPage>();


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();

    /// <summary>本会话内已通过「获取详情」拉取过明细的月份（<c>yyyyMM</c>）。</summary>
    private readonly HashSet<string> _detailFetchedMonths = new();



    public TrailblazeCalendarPage()
    {
        this.InitializeComponent();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
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
        WeakReferenceMessenger.Default.Unregister<LanguageChangedMessage>(this);
        CurrentSummary = null!;
        SelectMonthData = null;
        MonthDataList = null!;
        SelectSeries = null!;
        SelectMonthAwards = null!;
        DayDataList = null!;
    }



    #region 绑定属性

    [ObservableProperty]
    private TrailblazeCalendarSummary currentSummary;

    [ObservableProperty]
    private TrailblazeCalendarMonthData? selectMonthData;

    [ObservableProperty]
    private List<TrailblazeCalendarSummaryMonth> monthDataList;

    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;

    [ObservableProperty]
    private List<MonthResourceAward>? selectMonthAwards;

    [ObservableProperty]
    private List<CalendarDayData> dayDataList;

    [ObservableProperty]
    private bool selectMonthHasDetail;

    #endregion



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



    private async Task InitializeDataAsync()
    {
        await GetCurrentSummaryAsync();
        GetMonthDataList();
        SelectCurrentMonth();
    }





    private async Task GetCurrentSummaryAsync()
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            CurrentSummary = await _gameRecordService.GetTrailblazeCalendarSummaryAsync(gameRole);
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



    private void GetMonthDataList(bool preserveSelectedMonth = false)
    {
        try
        {
            string? selectedMonth = preserveSelectedMonth ? SelectMonthData?.Month : null;
            if (!preserveSelectedMonth)
            {
                SelectMonthData = null;
                SelectMonthHasDetail = false;
            }
            MonthDataList = _gameRecordService.GetTrailblazeCalendarSummaryMonthList(gameRole);
            Image_Emoji.Visibility = MonthDataList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrEmpty(selectedMonth))
            {
                ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.DataMonth == selectedMonth);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load trailblaze calendar month data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }




    private void SelectCurrentMonth()
    {
        try
        {
            if (MonthDataList is null || MonthDataList.Count == 0)
            {
                return;
            }
            string? currentMonth = CurrentSummary?.DataMonth;
            if (string.IsNullOrEmpty(currentMonth))
            {
                currentMonth = DateTimeOffset.UtcNow.ToOffset(MonthlyReportHelpers.GetServerUtcOffset(gameRole)).ToString("yyyyMM");
            }
            ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.DataMonth == currentMonth);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Select current month ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
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
            await FetchMonthDataAsync(month);
            _detailFetchedMonths.Add(month);
            GetMonthDataList(preserveSelectedMonth: true);
            ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.DataMonth == month);
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





    [RelayCommand]
    private async Task RefreshMonthDataAsync()
    {
        try
        {
            if (gameRole is null || SelectMonthData is null)
            {
                return;
            }
            string month = SelectMonthData.Month;
            await FetchMonthDataAsync(month);
            GetMonthDataList(preserveSelectedMonth: true);
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Refresh trailblaze calendar month data ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.Month);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Refresh trailblaze calendar month data ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.Month);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh trailblaze calendar month data ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.Month);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    private async Task FetchMonthDataAsync(string month)
    {
        if (gameRole is null)
        {
            return;
        }
        await _gameRecordService.GetTrailblazeCalendarSummaryAsync(gameRole, month);
        await _gameRecordService.GetTrailblazeCalendarDetailAsync(gameRole, month, 1);
        await _gameRecordService.GetTrailblazeCalendarDetailAsync(gameRole, month, 2);
        var monthData = _gameRecordService.GetTrailblazeCalendarMonthData(gameRole.Uid, month);
        if (monthData is not null)
        {
            ApplySelectMonthSummary(monthData);
        }
        _detailFetchedMonths.Add(month);
    }



    private void ApplySelectMonthSummary(TrailblazeCalendarMonthData data)
    {
        SelectMonthData = data;
        SelectMonthAwards =
        [
            new MonthResourceAward
            {
                Image = new BitmapImage(new("ms-appx:///Assets/Image/900001.png")),
                Name = Lang.TrailblazeCalendarPage_StellarJade,
                Count = data.CurrentHcoin,
            },
            new MonthResourceAward
            {
                IsCompositePass = true,
                Name = Lang.TrailblazeCalendarPage_PassAndSpecialPass,
                Count = data.CurrentRailsPass,
            },
        ];
        SelectSeries = data.GroupBy
            .Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action)))
            .ToList();
        RefreshDailyDataPlot(data);
        UpdateSelectMonthHasDetail(data.Uid, data.Month);
    }



    private void UpdateSelectMonthHasDetail(long uid, string month)
    {
        SelectMonthHasDetail = _detailFetchedMonths.Contains(month)
            || _gameRecordService.HasTrailblazeCalendarDetail(uid, month);
    }





    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is TrailblazeCalendarSummaryMonth item)
            {
                var data = _gameRecordService.GetTrailblazeCalendarMonthData(item.Uid, item.DataMonth);
                if (data is null)
                {
                    return;
                }
                ApplySelectMonthSummary(data);
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
            var items = _gameRecordService.GetTrailblazeCalendarDetailItems(data.Uid, data.Month);
            int days = DateTime.DaysInMonth(int.Parse(data.Month[..4]), int.Parse(data.Month[4..]));
            TimeSpan serverOffset = MonthlyReportHelpers.GetServerUtcOffset(gameRole);

            var stats_jade = new int[days];
            var stats_pass = new int[days];
            foreach (var item in items)
            {
                var day = MonthlyReportHelpers.GetServerLocalDay(item.Time, serverOffset);
                if (day > days)
                {
                    continue;
                }
                int index = day - 1;
                switch (item.Type)
                {
                    case 1:
                        stats_jade[index] += item.Number;
                        break;
                    case 2:
                        stats_pass[index] += item.Number;
                        break;
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



    /// <summary>
    /// 语言切换时刷新饼图图例等已物化的本地化文案，并更新 x:Bind 绑定。
    /// </summary>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        if (SelectMonthData?.GroupBy is { } components)
        {
            SelectSeries = components
                .Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action)))
                .ToList();
        }
        this.Bindings.Update();
    }



    /// <summary>
    /// 将 API <c>group_by[].action</c> 映射为本地化显示名，供饼图图例使用。
    /// </summary>
    /// <param name="action">API 返回的 action 标识字符串。</param>
    /// <returns>本地化文案；未知 action 原样返回。</returns>
    public static string ActionName(string action)
    {
        return action switch
        {
            "daily_reward" => Lang.TrailblazeCalendarPage_DailyReward,
            "space_reward" => Lang.TrailblazeCalendarPage_SpaceReward,
            "event_reward" => Lang.TrailblazeCalendarPage_EventReward,
            "adventure_reward" => Lang.TrailblazeCalendarPage_AdventureReward,
            "abyss_reward" => Lang.TrailblazeCalendarPage_AbyssReward,
            "mail_reward" => Lang.TrailblazeCalendarPage_MailReward,
            "other" => Lang.TrailblazeCalendarPage_OtherReward,
            _ => action,
        };
    }




    public class MonthResourceAward
    {

        public BitmapImage? Image { get; set; }

        public bool IsCompositePass { get; set; }

        public string Name { get; set; }

        public int Count { get; set; }

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