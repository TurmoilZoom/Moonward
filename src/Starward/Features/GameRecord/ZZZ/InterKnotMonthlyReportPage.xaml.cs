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
using Starward.Core.GameRecord.ZZZ.InterKnotReport;
using Starward.Features.Setting;
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
/// 绝区零「绳网月报」页面。左侧为已缓存月份列表与「获取详情」按钮，右侧展示选中月的统计数据（资源总量 + 菲林收入构成）及按日聚合的明细。
/// 数据流：进入页面时拉取当前月汇总（<c>month_info</c>）写入结构化缓存表并默认选中当月；「获取详情」按月份拉汇总 + 三类资源明细分页（<c>month_detail</c>）并写入 SQLite；
/// 点击月份仅从本地缓存读取，不再发起网络请求。
/// </summary>
public sealed partial class InterKnotMonthlyReportPage : PageBase
{

    private readonly ILogger<InterKnotMonthlyReportPage> _logger = AppConfig.GetLogger<InterKnotMonthlyReportPage>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();

    /// <summary>本会话内已通过「获取详情」拉取过明细的月份（<c>yyyyMM</c>），用于 API 三类资源均为空时仍能显示刷新按钮。</summary>
    private readonly HashSet<string> _detailFetchedMonths = new();


    /// <summary>
    /// 初始化页面 XAML 组件。
    /// </summary>
    public InterKnotMonthlyReportPage()
    {
        this.InitializeComponent();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
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
        WeakReferenceMessenger.Default.Unregister<LanguageChangedMessage>(this);
        CurrentSummary = null!;
        SelectMonthData = null;
        MonthDataList = null!;
        SelectSeries = null!;
        DayDataList = null!;
    }


    #region 绑定属性

    /// <summary>当前月汇总（进入页面时从 API 拉取并缓存，用于确定当月并填充「获取详情」菜单，不再单独展示）。</summary>
    [ObservableProperty]
    private InterKnotReportSummary currentSummary;

    /// <summary>左侧列表选中月份的汇总（默认当月）；为 null 时隐藏统计区与每日数据区。</summary>
    [ObservableProperty]
    private InterKnotReportSummary? selectMonthData;

    /// <summary>左侧月份列表的轻量投影（仅月份 + 当月菲林总量），按 <c>DataMonth</c> 降序；点击某项后才查询该月完整数据。</summary>
    [ObservableProperty]
    private List<InterKnotReportSummaryMonth> monthDataList;

    /// <summary>选中历史月的菲林收入构成饼图图例。</summary>
    [ObservableProperty]
    private List<ColorRectChart.ChartLegend>? selectSeries;

    /// <summary>选中月按日聚合后的菲林 / 母带 / 邦布券数据，供右侧「每日数据」列表绑定。</summary>
    [ObservableProperty]
    private List<CalendarDayData> dayDataList;

    /// <summary>选中月是否已「获取详情」；为 true 时在「统计数据」行右侧显示刷新按钮。</summary>
    [ObservableProperty]
    private bool selectMonthHasDetail;

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
    /// 页面加载时初始化数据：拉取当前月汇总、重新加载本地月份列表，并默认选中当月以直接展示当月统计数据。
    /// </summary>
    /// <returns>完成初始化的异步任务。</returns>
    private async Task InitializeDataAsync()
    {
        await GetCurrentSummaryAsync();
        GetMonthDataList();
        SelectCurrentMonth();
    }




    /// <summary>
    /// 从 API 拉取当前月汇总（<c>month_info</c>，不传 <c>month</c> 参数），用于确定当月并填充「获取详情」可选月份菜单。
    /// 成功后将汇总写入结构化缓存表（<see cref="GameRecordService.GetInterKnotReportSummaryAsync"/> 内部处理），当月随后会出现在月份列表并被默认选中。
    /// </summary>
    /// <returns>拉取并刷新月份菜单的异步任务。</returns>
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
    /// <param name="preserveSelectedMonth">为 true 时刷新列表后保持当前选中月份不变（用于获取/刷新详情后更新左侧菲林总量）。</param>
    private void GetMonthDataList(bool preserveSelectedMonth = false)
    {
        try
        {
            string? selectedMonth = preserveSelectedMonth ? SelectMonthData?.DataMonth : null;
            if (!preserveSelectedMonth)
            {
                SelectMonthData = null;
                SelectMonthHasDetail = false;
            }
            MonthDataList = _gameRecordService.GetInterKnotReportSummaryMonthList(gameRole);
            Image_Emoji.Visibility = MonthDataList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrEmpty(selectedMonth))
            {
                ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.DataMonth == selectedMonth);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load inter knot report month data ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }




    /// <summary>
    /// 默认选中当月：在已加载的月份列表中找到当前自然月并选中，从而进入页面即直接展示当月统计数据与每日数据。
    /// 当月优先取自刚拉取的实时汇总（<see cref="InterKnotReportSummary.DataMonth"/>，API 权威）；汇总缺失（如网络失败）时按账号所在服务器时区推算，
    /// 避免本机时区与服务器跨月时选错月份。列表中不存在当月（如无任何缓存）时不选中任何项。
    /// </summary>
    private void SelectCurrentMonth()
    {
        try
        {
            if (MonthDataList is null || MonthDataList.Count == 0)
            {
                return;
            }
            // 优先用 API 返回的当月；失败时按服务器本地日历推算当前自然月（与每日数据的分天口径一致）。
            string? currentMonth = CurrentSummary?.DataMonth;
            if (string.IsNullOrEmpty(currentMonth))
            {
                currentMonth = DateTimeOffset.UtcNow.ToOffset(GetServerUtcOffset(gameRole)).ToString("yyyyMM");
            }
            // 选中后触发 ListView_MonthDataList_SelectionChanged，从本地缓存填充右侧统计数据与每日数据；找不到当月则保持无选中。
            ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.DataMonth == currentMonth);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Select current month ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }





    /// <summary>
    /// 「获取详情」命令：对指定月份拉取汇总及三类资源（菲林 / 母带 / 邦布券）的逐条明细分页，写入本地后刷新月份列表。
    /// 若本地明细条数已与 API <c>total</c> 一致则仅更新该类型最后一条记录（见 <see cref="GameRecordService.GetInterKnotReportDetailAsync"/>）。
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
            await FetchMonthDataAsync(month);
            _detailFetchedMonths.Add(month);
            GetMonthDataList(preserveSelectedMonth: true);
            ListView_MonthDataList.SelectedItem = MonthDataList.FirstOrDefault(x => x.DataMonth == month);
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
    /// 刷新当前选中月的统计数据与每日数据：重新拉取汇总及三类资源明细分页并更新右侧展示。
    /// 仅对已「获取详情」的月份可用（由 <see cref="SelectMonthHasDetail"/> 控制按钮可见性）。
    /// </summary>
    /// <returns>刷新数据的异步任务。</returns>
    [RelayCommand]
    private async Task RefreshMonthDataAsync()
    {
        try
        {
            if (gameRole is null || SelectMonthData is null)
            {
                return;
            }
            string month = SelectMonthData.DataMonth;
            await FetchMonthDataAsync(month);
            GetMonthDataList(preserveSelectedMonth: true);
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Refresh inter knot report month data ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.DataMonth);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Refresh inter knot report month data ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.DataMonth);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh inter knot report month data ({gameBiz}, {uid}, {month}).", gameRole?.GameBiz, gameRole?.Uid, SelectMonthData?.DataMonth);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 从 API 拉取指定月份的汇总与三类资源明细，并刷新右侧统计数据与每日数据展示。
    /// </summary>
    /// <param name="month">目标月份，格式 <c>yyyyMM</c>。</param>
    /// <returns>拉取并更新展示的异步任务。</returns>
    private async Task FetchMonthDataAsync(string month)
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
        ApplySelectMonthSummary(summary);
        _detailFetchedMonths.Add(month);
    }



    /// <summary>
    /// 将选中月汇总应用到右侧绑定属性（统计数据、饼图、每日数据），并更新刷新按钮可见性。
    /// </summary>
    /// <param name="summary">该月完整汇总；为 null 时不更新。</param>
    private void ApplySelectMonthSummary(InterKnotReportSummary summary)
    {
        SelectMonthData = summary;
        SelectSeries = summary.MonthData.IncomeComponents
            .Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action)))
            .ToList();
        RefreshDailyDataPlot(summary.Uid, summary.DataMonth);
        UpdateSelectMonthHasDetail(summary.Uid, summary.DataMonth);
    }



    /// <summary>
    /// 根据本地明细缓存与会话内「获取详情」记录，更新刷新按钮是否可见。
    /// </summary>
    /// <param name="uid">游戏 UID。</param>
    /// <param name="dataMonth">月份，格式 <c>yyyyMM</c>。</param>
    private void UpdateSelectMonthHasDetail(long uid, string dataMonth)
    {
        SelectMonthHasDetail = _detailFetchedMonths.Contains(dataMonth)
            || _gameRecordService.HasInterKnotReportDetail(uid, dataMonth);
    }



    /// <summary>
    /// 左侧月份列表选中变更：按所选项的 UID + 月份从本地缓存读取该月完整汇总，更新统计数据区饼图，并按日聚合明细生成 <see cref="DayDataList"/>。
    /// 不发起网络请求；若该月尚未「获取详情」，每日数据可能全为 0。
    /// </summary>
    /// <param name="sender">月份列表 <see cref="ListView"/>。</param>
    /// <param name="e">选中项变更事件参数。</param>
    private void ListView_MonthDataList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            // 列表项仅为轻量投影，选中时才按 UID + 月份查询该月完整汇总（含收入构成）。
            if (e.AddedItems.FirstOrDefault() is InterKnotReportSummaryMonth item)
            {
                var summary = _gameRecordService.GetInterKnotReportSummary(item.Uid, item.DataMonth);
                if (summary is null)
                {
                    return;
                }
                ApplySelectMonthSummary(summary);
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
    /// <param name="uid">游戏 UID。</param>
    /// <param name="dataMonth">选中月份，格式 <c>yyyyMM</c>（如 <c>202506</c>）。</param>
    private void RefreshDailyDataPlot(long uid, string dataMonth)
    {
        try
        {
            var items = _gameRecordService.GetInterKnotReportDetailItems(uid, dataMonth);
            int days = DateTime.DaysInMonth(int.Parse(dataMonth[..4]), int.Parse(dataMonth[4..]));

            // 接口返回的 time 是 Unix 时间戳（经 TimestampStringJsonConverter 转成 UTC 的 DateTimeOffset）。
            // 官方绳网月报按“游戏服务器本地日历日”分天，因此这里必须按账号所在服务器的时区取日，
            // 不能用 LocalDateTime（本机时区）——当本机时区不是服务器时区时，跨午夜的记录会被算到相邻的一天，
            // 导致个别日期的菲林/母带/邦布券数量与官方工具对不上（月总不受影响，因为月总直接取自接口）。
            TimeSpan serverOffset = GetServerUtcOffset(gameRole);

            var stats_poly = new int[days];
            var stats_tape = new int[days];
            var stats_boopon = new int[days];
            foreach (var item in items)
            {
                var day = item.Time.ToOffset(serverOffset).Day;
                if (day > days)
                {
                    continue;
                }
                int index = day - 1;
                switch (item.DataType)
                {
                    case InterKnotReportDataType.PolychromesData:
                        stats_poly[index] += item.Number;
                        break;
                    case InterKnotReportDataType.MatserTapeData:
                        stats_tape[index] += item.Number;
                        break;
                    case InterKnotReportDataType.BooponsData:
                        stats_boopon[index] += item.Number;
                        break;
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
                    Day = $"{dataMonth[4..]}-{i + 1:D2}",
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
    /// 语言切换时刷新饼图图例等已物化的本地化文案，并更新 x:Bind 绑定。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="__">语言变更消息（未使用）。</param>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        if (SelectMonthData?.MonthData?.IncomeComponents is { } components)
        {
            SelectSeries = components
                .Select(x => new ColorRectChart.ChartLegend(ActionName(x.Action), x.Percent, actionColorMap.GetValueOrDefault(x.Action)))
                .ToList();
        }
        this.Bindings.Update();
    }



    /// <summary>
    /// 将 API <c>data_type</c> 映射为本地化资源名称，供「统计数据」展示；不依赖 API <c>data_name</c> 或本地缓存名称。
    /// </summary>
    /// <param name="type"><see cref="InterKnotReportDataType"/> 常量或 API 返回的同类字符串。</param>
    /// <returns>本地化文案；未知类型原样返回。</returns>
    public static string DataTypeName(string type)
    {
        return type switch
        {
            InterKnotReportDataType.PolychromesData => Lang.InterKnotMonthlyReportPage_Polychrome,
            InterKnotReportDataType.MatserTapeData => Lang.InterKnotMonthlyReportPage_MasterTape,
            InterKnotReportDataType.BooponsData => Lang.InterKnotMonthlyReportPage_Boopon,
            _ => type,
        };
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




}