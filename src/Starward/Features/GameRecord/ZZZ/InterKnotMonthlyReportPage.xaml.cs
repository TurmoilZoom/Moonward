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
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;


namespace Starward.Features.GameRecord.ZZZ;

/// <summary>
/// 绝区零「绳网月报」页面。布局与旅行者札记类似：左侧为已缓存月份列表与操作按钮，右侧展示实时汇总、选中月历史汇总及按日聚合的明细。
/// 数据流：刷新仅拉当前月汇总（<c>month_info</c>）；「获取详情」按月份拉汇总 + 三类资源明细分页（<c>month_detail</c>）并写入 SQLite；
/// 点击月份仅从本地缓存读取，不再发起网络请求。
/// </summary>
public sealed partial class InterKnotMonthlyReportPage : PageBase
{

    private readonly ILogger<InterKnotMonthlyReportPage> _logger = AppConfig.GetLogger<InterKnotMonthlyReportPage>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    /// <summary>
    /// 初始化页面 XAML 组件。
    /// </summary>
    public InterKnotMonthlyReportPage()
    {
        this.InitializeComponent();
    }



    /// <summary>导航传入的当前游戏角色；为 null 时所有数据加载逻辑直接返回。</summary>
    private GameRecordRole gameRole;



    /// <summary>
    /// 接收父页面传入的 <see cref="GameRecordRole"/> 导航参数。
    /// </summary>
    /// <param name="e">导航事件参数，<see cref="NavigationEventArgs.Parameter"/> 应为 <see cref="GameRecordRole"/>。</param>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is GameRecordRole role)
        {
            gameRole = role;
        }
    }


    /// <summary>
    /// 页面加载完成后延迟一帧再初始化数据，避免与首帧布局竞争。
    /// </summary>
    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        await InitializeDataAsync();
    }



    /// <summary>
    /// 页面卸载时清空绑定数据，减轻对页面实例的持有引用。
    /// </summary>
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


    #region 绑定属性

    /// <summary>当前月实时汇总（刷新时从 API 拉取并缓存）。</summary>
    [ObservableProperty]
    private InterKnotReportSummary currentSummary;

    /// <summary>左侧列表选中的历史月份汇总；为 null 时隐藏历史区与每日数据区。</summary>
    [ObservableProperty]
    private InterKnotReportSummary? selectMonthData;

    /// <summary>本地 SQLite 中已缓存的各月汇总列表，按 <c>DataMonth</c> 降序。</summary>
    [ObservableProperty]
    private List<InterKnotReportSummary> monthDataList;

    /// <summary>当前月菲林收入构成的饼图图例（<see cref="ColorRectChart.ChartLegend"/>）。</summary>
    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? currentSeries;

    /// <summary>选中历史月的菲林收入构成饼图图例。</summary>
    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;

    /// <summary>选中月按日聚合后的菲林 / 母带 / 邦布券数据，供右侧「每日数据」列表绑定。</summary>
    [ObservableProperty]
    private List<CalendarDayData> dayDataList;

    #endregion


    /// <summary>
    /// API <c>income_components[].action</c> 到饼图颜色的映射，与官方绳网月报配色接近。
    /// </summary>
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



    /// <summary>
    /// 刷新按钮命令：拉取当前月汇总并重新加载本地月份列表。
    /// </summary>
    /// <returns>完成初始化的异步任务。</returns>
    [RelayCommand]
    private async Task InitializeDataAsync()
    {
        await Task.Delay(16);
        await GetCurrentSummaryAsync();
        GetMonthDataList();
    }




    /// <summary>
    /// 从 API 拉取当前月汇总（<c>month_info</c>，不传 <c>month</c> 参数），更新实时数据区与「获取详情」月份菜单。
    /// 成功后将汇总写入 <c>ZZZInterKnotReportSummary</c>（<see cref="GameRecordService.GetInterKnotReportSummaryAsync"/> 内部处理）。
    /// </summary>
    /// <returns>拉取并刷新 UI 的异步任务。</returns>
    private async Task GetCurrentSummaryAsync()
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            CurrentSummary = await _gameRecordService.GetInterKnotReportSummaryAsync(gameRole);
            // optional_month 由 API 返回，决定用户可手动拉取明细的历史月份。
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



    /// <summary>
    /// 从本地 SQLite 读取该角色所有已缓存月份汇总，填充左侧列表；无数据时显示空状态插图。
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
    /// 「获取详情」命令：对指定月份拉取汇总及三类资源（菲林 / 母带 / 邦布券）的逐条明细分页，写入本地后刷新月份列表。
    /// 若本地明细条数已与 API <c>total</c> 一致则跳过该类型的网络请求（见 <see cref="GameRecordService.GetInterKnotReportDetailAsync"/>）。
    /// </summary>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>（如 <c>202506</c>）。</param>
    /// <returns>拉取明细并刷新列表的异步任务。</returns>
    [RelayCommand]
    private async Task GetDataDetailsAsync(string month)
    {
        try
        {
            if (gameRole is null)
            {
                return;
            }
            var summary = await _gameRecordService.GetInterKnotReportSummaryAsync(gameRole, month);
            // MonthData.List 含 PolychromesData / MatserTapeData / BooponsData 三种 data_type。
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





    /// <summary>
    /// 左侧月份列表选中变更：从本地缓存读取完整汇总，更新历史数据区饼图，并按日聚合明细生成 <see cref="DayDataList"/>。
    /// 不发起网络请求；若该月尚未「获取详情」，每日数据可能全为 0。
    /// </summary>
    /// <param name="sender">月份列表 <see cref="ListView"/>。</param>
    /// <param name="e">选中项变更事件参数。</param>
    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.FirstOrDefault() is InterKnotReportSummary data)
            {
                SelectMonthData = _gameRecordService.GetInterKnotReportSummary(data)!;
                SelectSeries = SelectMonthData.MonthData.IncomeComponents.Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action))).ToList();
                RefreshDailyDataPlot(data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selection changed ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }



    /// <summary>
    /// 从本地 SQLite 读取选中月三类资源明细，按游戏服务器时区的日历日累加数量，生成每日数据列表与进度条比例。
    /// </summary>
    /// <param name="data">选中月份的汇总对象，提供 <see cref="InterKnotReportSummary.Uid"/> 与 <see cref="InterKnotReportSummary.DataMonth"/>。</param>
    private void RefreshDailyDataPlot(InterKnotReportSummary data)
    {
        try
        {
            var items_poly = _gameRecordService.GetInterKnotReportDetailItems(data.Uid, data.DataMonth, InterKnotReportDataType.PolychromesData);
            var items_tape = _gameRecordService.GetInterKnotReportDetailItems(data.Uid, data.DataMonth, InterKnotReportDataType.MatserTapeData);
            var items_boopon = _gameRecordService.GetInterKnotReportDetailItems(data.Uid, data.DataMonth, InterKnotReportDataType.BooponsData);
            int days = DateTime.DaysInMonth(int.Parse(data.DataMonth[..4]), int.Parse(data.DataMonth[4..]));

            // 接口返回的 time 是 Unix 时间戳（经 TimestampStringJsonConverter 转成 UTC 的 DateTimeOffset）。
            // 官方绳网月报按“游戏服务器本地日历日”分天，因此这里必须按账号所在服务器的时区取日，
            // 不能用 LocalDateTime（本机时区）——当本机时区不是服务器时区时，跨午夜的记录会被算到相邻的一天，
            // 导致个别日期的菲林/母带/邦布券数量与官方工具对不上（月总不受影响，因为月总直接取自接口）。
            TimeSpan serverOffset = GetServerUtcOffset(gameRole);

            var stats_poly = new int[days];
            foreach (var item in items_poly)
            {
                var day = item.Time.ToOffset(serverOffset).Day;
                if (day <= days)
                {
                    stats_poly[day - 1] += item.Number;
                }
            }

            var stats_tape = new int[days];
            foreach (var item in items_tape)
            {
                var day = item.Time.ToOffset(serverOffset).Day;
                if (day <= days)
                {
                    stats_tape[day - 1] += item.Number;
                }
            }

            var stats_boopon = new int[days];
            foreach (var item in items_boopon)
            {
                var day = item.Time.ToOffset(serverOffset).Day;
                if (day <= days)
                {
                    stats_boopon[day - 1] += item.Number;
                }
            }

            double max_poly = stats_poly.Max();
            double max_tape = stats_tape.Max();
            double max_boopon = stats_boopon.Max();
            // 当月某类资源全为 0 时用 MaxValue 作分母，避免 ProgressBar 出现 NaN。
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




    /// <summary>
    /// 根据账号所在游戏服务器返回其相对 UTC 的时区偏移。
    /// 绳网月报的每日数据按“服务器本地日历日”分天，必须用该偏移把记录时间戳换算到服务器时间后再取日，才能与官方工具一致。
    /// </summary>
    /// <param name="role">当前游戏角色；其 <see cref="GameRecordRole.Region"/> 标识所在服务器。为 null 时按国服（+8）处理。</param>
    /// <returns>服务器相对 UTC 的偏移：美服 -5、欧服 +1，其余（国服 / 亚服 / 港澳台 等）均为 +8。</returns>
    private static TimeSpan GetServerUtcOffset(GameRecordRole role)
    {
        // 国际服按 region 区分服务器时区；国服 / 亚服 / 港澳台 等统一 +8（与原神、星铁抽卡导出里的时区映射一致）。
        return role?.Region switch
        {
            "prod_gf_us" => TimeSpan.FromHours(-5), // 美服
            "prod_gf_eu" => TimeSpan.FromHours(1),  // 欧服
            _ => TimeSpan.FromHours(8),             // 国服 / 亚服 / 港澳台 等
        };
    }




    /// <summary>
    /// 单月按日聚合后的资源统计，供 XAML「每日数据」<see cref="ItemsRepeater"/> 绑定。
    /// </summary>
    public class CalendarDayData
    {

        /// <summary>显示用日期标签，格式 <c>MM-dd</c>（如 <c>06-15</c>）。</summary>
        public string Day { get; set; }

        /// <summary>当日获得的菲林（<see cref="InterKnotReportDataType.PolychromesData"/>）总量。</summary>
        public int Poly { get; set; }

        /// <summary>当日获得的母带（<see cref="InterKnotReportDataType.MatserTapeData"/>）总量。</summary>
        public int Tape { get; set; }

        /// <summary>当日获得的邦布券（<see cref="InterKnotReportDataType.BooponsData"/>）总量。</summary>
        public int Boopon { get; set; }

        /// <summary>菲林进度条值，范围 0–1，相对当月菲林单日最大值。</summary>
        public double PolyProgress { get; set; }

        /// <summary>母带进度条值，范围 0–1，相对当月母带单日最大值。</summary>
        public double TapeProgress { get; set; }

        /// <summary>邦布券进度条值，范围 0–1，相对当月邦布券单日最大值。</summary>
        public double BooponProgress { get; set; }

    }





    /// <summary>
    /// 将 API <c>data_type</c> 映射为资源图标，供 XAML <c>x:Bind</c> 使用。
    /// </summary>
    /// <param name="type"><see cref="InterKnotReportDataType"/> 常量或 API 返回的同类字符串。</param>
    /// <returns>对应资源的 <see cref="BitmapImage"/>；未知类型返回 null。</returns>
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




    /// <summary>
    /// 将 API <c>income_components[].action</c> 或明细 <c>action</c> 映射为本地化显示名。
    /// </summary>
    /// <param name="action">API 返回的 action 标识字符串。</param>
    /// <returns>本地化文案；未知 action 原样返回。</returns>
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




    /// <summary>
    /// 从月度汇总中提取菲林（Polychrome）总量，供左侧月份列表项显示。
    /// </summary>
    /// <param name="monthData">月份汇总中的 <see cref="InterKnotReportSummary.MonthData"/>。</param>
    /// <returns>菲林数量；无数据时返回 0。</returns>
    public static int GetPolychromeCount(InterKnotReportMonthData monthData)
    {
        return monthData?.List?.FirstOrDefault(a => a.DataType == InterKnotReportDataType.PolychromesData)?.Count ?? 0;
    }




}