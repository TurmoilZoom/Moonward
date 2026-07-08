using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.Genshin.TravelersDiary;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
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


    [ObservableProperty]
    private List<DiaryDayData> dayDataList;


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
        await Task.Delay(16);
        await GetCurrentSummaryAsync();   // 总是请求当前月最新数据（含 OptionalMonth）
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
                RefreshDailyDataPlot(data);
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
            // 一次查询所有类型的明细，再按 Type 分组，避免多次 DB 请求
            var allItems = _gameRecordService.GetTravelersDiaryDetailItems(data.Uid, data.Year, data.Month);
            var items_primogems = allItems.Where(x => x.Type == 1);
            var items_mora = allItems.Where(x => x.Type == 2);
            int days = DateTime.DaysInMonth(data.Year, data.Month);
            var x = Enumerable.Range(1, days).ToArray();

            var stats_primogems = new int[days];
            foreach (var item in items_primogems)
            {
                var day = item.Time.Day;
                if (day <= days)
                {
                    stats_primogems[day - 1] += item.Number;
                }
            }

            var stats_mora = new int[days];
            foreach (var item in items_mora)
            {
                var day = item.Time.Day;
                if (day <= days)
                {
                    stats_mora[day - 1] += item.Number;
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



    public class DiaryDayData
    {

        public string Day { get; set; }

        public int Primogems { get; set; }

        public int Mora { get; set; }

        public double PrimogemsProgress { get; set; }

        public double MoraProgress { get; set; }

    }


}
