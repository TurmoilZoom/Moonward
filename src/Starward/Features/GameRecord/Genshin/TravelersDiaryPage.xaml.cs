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
using Starward.Core.GameRecord.Genshin.TravelersDiary;
using Starward.Features.Setting;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;


namespace Starward.Features.GameRecord.Genshin;

/// <summary>
/// 原神「旅行者札记」页面。左侧为已缓存月份列表与「获取详情」按钮，右侧展示选中月的统计数据（资源总量 + 原石收入构成）及按日聚合的明细。
/// 数据流：进入页面时拉取当前月汇总写入 SQLite 并默认选中当月；「获取详情」按月份拉汇总 + 原石/摩拉明细分页并写入 SQLite；
/// 点击月份仅从本地缓存读取，不再发起网络请求。
/// </summary>
public sealed partial class TravelersDiaryPage : PageBase
{


    private readonly ILogger<TravelersDiaryPage> _logger = AppConfig.GetLogger<TravelersDiaryPage>();


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();

    /// <summary>本会话内已通过「获取详情」拉取过明细的月份键（<c>yyyy-MM</c>）。</summary>
    private readonly HashSet<string> _detailFetchedMonths = new();



    public TravelersDiaryPage()
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
    private TravelersDiarySummary currentSummary;

    [ObservableProperty]
    private TravelersDiaryMonthData? selectMonthData;

    [ObservableProperty]
    private List<TravelersDiarySummaryMonth> monthDataList;

    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;

    [ObservableProperty]
    private List<MonthResourceAward>? selectMonthAwards;

    [ObservableProperty]
    private List<DiaryDayData> dayDataList;

    [ObservableProperty]
    private bool selectMonthHasDetail;

    #endregion



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
            CurrentSummary = await _gameRecordService.GetTravelersDiarySummaryAsync(gameRole);
            MenuFlyout_GetDetails.Items.Clear();
            foreach (int month in Enumerable.Reverse(CurrentSummary.OptionalMonth))
            {
                MenuFlyout_GetDetails.Items.Add(new MenuFlyoutItem
                {
                    Text = new DateTime(2023, month, 1).ToString("MMM"),
                    Command = GetDataDetailsCommand,
                    CommandParameter = month,
                });
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Get realtime traveler's diary data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Get realtime traveler's diary data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get realtime traveler's diary data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    private void GetMonthDataList(bool preserveSelectedMonth = false)
    {
        try
        {
            int? selectedYear = preserveSelectedMonth ? SelectMonthData?.Year : null;
            int? selectedMonth = preserveSelectedMonth ? SelectMonthData?.Month : null;
            if (!preserveSelectedMonth)
            {
                SelectMonthData = null;
                SelectMonthHasDetail = false;
            }
            MonthDataList = _gameRecordService.GetTravelersDiarySummaryMonthList(gameRole);
            Image_Emoji.Visibility = MonthDataList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (selectedYear is not null && selectedMonth is not null)
            {
                ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.Year == selectedYear && x.Month == selectedMonth);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load traveler's diary month data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
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
            int year;
            int month;
            if (CurrentSummary?.MonthData is not null)
            {
                year = CurrentSummary.MonthData.Year;
                month = CurrentSummary.MonthData.Month;
            }
            else
            {
                var serverNow = DateTimeOffset.UtcNow.ToOffset(GetServerUtcOffset(gameRole));
                year = serverNow.Year;
                month = serverNow.Month;
            }
            ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.Year == year && x.Month == month);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Select current month ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }





    [RelayCommand]
    private async Task GetDataDetailsAsync(int month)
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            var monthData = await FetchMonthDataAsync(month);
            GetMonthDataList(preserveSelectedMonth: true);
            if (monthData is not null)
            {
                ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.Year == monthData.Year && x.Month == monthData.Month);
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




    [RelayCommand]
    private async Task RefreshMonthDataAsync()
    {
        try
        {
            if (gameRole is null || SelectMonthData is null)
            {
                return;
            }
            await FetchMonthDataAsync(SelectMonthData.Month, SelectMonthData.Year);
            GetMonthDataList(preserveSelectedMonth: true);
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Refresh traveler's diary month data ({gameBiz}, {uid}, {year}-{month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.Year, SelectMonthData?.Month);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Refresh traveler's diary month data ({gameBiz}, {uid}, {year}-{month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.Year, SelectMonthData?.Month);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh traveler's diary month data ({gameBiz}, {uid}, {year}-{month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.Year, SelectMonthData?.Month);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    private async Task<TravelersDiaryMonthData?> FetchMonthDataAsync(int month, int year = 0)
    {
        if (gameRole is null)
        {
            return null;
        }
        var summary = await _gameRecordService.GetTravelersDiarySummaryAsync(gameRole, month);
        var monthData = summary.MonthData;
        if (monthData is null)
        {
            return null;
        }
        if (year > 0)
        {
            monthData.Year = year;
        }
        await _gameRecordService.GetTravelersDiaryDetailAsync(gameRole, month, 1);
        await _gameRecordService.GetTravelersDiaryDetailAsync(gameRole, month, 2);
        ApplySelectMonthSummary(monthData);
        _detailFetchedMonths.Add($"{monthData.Year}-{monthData.Month:D2}");
        return monthData;
    }



    private void ApplySelectMonthSummary(TravelersDiaryMonthData data)
    {
        SelectMonthData = data;
        SelectMonthAwards =
        [
            new MonthResourceAward
            {
                Image = new BitmapImage(new("ms-appx:///Assets/Image/UI_ItemIcon_201.png")),
                Name = Lang.TravelersDiaryPage_Primogems,
                Count = data.CurrentPrimogems,
            },
            new MonthResourceAward
            {
                Image = new BitmapImage(new("ms-appx:///Assets/Image/UI_ItemIcon_202.png")),
                Name = Lang.TravelersDiaryPage_Mora,
                Count = data.CurrentMora,
            },
        ];
        SelectSeries = data.PrimogemsGroupBy
            .Select(x => new ColorRectChart.ChartLegend(ActionName(x.ActionId), x.Percent, actionColorMap.GetValueOrDefault(x.ActionId)))
            .ToList();
        RefreshDailyDataPlot(data);
        UpdateSelectMonthHasDetail(data.Uid, data.Year, data.Month);
    }



    private void UpdateSelectMonthHasDetail(long uid, int year, int month)
    {
        string key = $"{year}-{month:D2}";
        SelectMonthHasDetail = _detailFetchedMonths.Contains(key)
            || _gameRecordService.HasTravelersDiaryDetail(uid, year, month);
    }




    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is TravelersDiarySummaryMonth item)
            {
                var data = _gameRecordService.GetTravelersDiaryMonthData(item.Uid, item.Year, item.Month);
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




    private void RefreshDailyDataPlot(TravelersDiaryMonthData data)
    {
        try
        {
            var items = _gameRecordService.GetTravelersDiaryDetailItems(data.Uid, data.Year, data.Month);
            int days = DateTime.DaysInMonth(data.Year, data.Month);
            TimeSpan serverOffset = GetServerUtcOffset(gameRole);

            var stats_primogems = new int[days];
            var stats_mora = new int[days];
            foreach (var item in items)
            {
                var day = new DateTimeOffset(item.Time, TimeSpan.Zero).ToOffset(serverOffset).Day;
                if (day > days)
                {
                    continue;
                }
                int index = day - 1;
                switch (item.Type)
                {
                    case 1:
                        stats_primogems[index] += item.Number;
                        break;
                    case 2:
                        stats_mora[index] += item.Number;
                        break;
                }
            }

            double max_primogems = stats_primogems.Max();
            double max_mora = stats_mora.Max();
            max_primogems = max_primogems == 0 ? double.MaxValue : max_primogems;
            max_mora = max_mora == 0 ? double.MaxValue : max_mora;
            var list = new List<DiaryDayData>(days);
            for (int i = 0; i < days; i++)
            {
                list.Add(new DiaryDayData
                {
                    Day = $"{data.Month:D2}-{i + 1:D2}",
                    Primogems = stats_primogems[i],
                    Mora = stats_mora[i],
                    PrimogemsProgress = stats_primogems[i] / max_primogems,
                    MoraProgress = stats_mora[i] / max_mora,
                });
            }
            DayDataList = list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh daily data plot");
        }
    }



    private static TimeSpan GetServerUtcOffset(GameRecordRole role)
    {
        return role?.Region switch
        {
            "prod_gf_us" => TimeSpan.FromHours(-5),
            "prod_gf_eu" => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(8),
        };
    }



    /// <summary>
    /// 语言切换时刷新饼图图例等已物化的本地化文案，并更新 x:Bind 绑定。
    /// </summary>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        if (SelectMonthData?.PrimogemsGroupBy is { } components)
        {
            SelectSeries = components
                .Select(x => new ColorRectChart.ChartLegend(ActionName(x.ActionId), x.Percent, actionColorMap.GetValueOrDefault(x.ActionId)))
                .ToList();
        }
        this.Bindings.Update();
    }



    /// <summary>
    /// 将 API <c>group_by[].action_id</c> 映射为本地化显示名，供饼图图例使用。
    /// </summary>
    /// <param name="actionId">API 返回的 action_id。</param>
    /// <returns>本地化文案；未知 id 时回退为数字字符串。</returns>
    public static string ActionName(int actionId)
    {
        return actionId switch
        {
            0 => Lang.TravelersDiaryPage_DailyCommission,
            1 => Lang.TravelersDiaryPage_SpiralAbyss,
            2 => Lang.TravelersDiaryPage_BattlePass,
            3 => Lang.TravelersDiaryPage_Event,
            4 => Lang.TravelersDiaryPage_Quest,
            5 => Lang.TravelersDiaryPage_Mail,
            6 => Lang.TravelersDiaryPage_Achievement,
            7 => Lang.TravelersDiaryPage_Other,
            _ => actionId.ToString(),
        };
    }



    public class MonthResourceAward
    {

        public BitmapImage Image { get; set; }

        public string Name { get; set; }

        public int Count { get; set; }

    }



    public class DiaryDayData
    {

        public string Day { get; set; }

        public int Primogems { get; set; }

        public int Mora { get; set; }

        public double PrimogemsProgress { get; set; }

        public double MoraProgress { get; set; }

    }


}