using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.GameRecord;
using Starward.Features.Gacha.UIGF;
using Starward.Features.Background;
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord;
using Starward.Features.Screenshot;
using Starward.Features.ViewHost;
using Starward.Language;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Storage;
using Windows.System;
using Windows.UI;


namespace Starward.Features.Gacha;

public sealed partial class GachaLogPage : PageBase
{

    private readonly ILogger<GachaLogPage> _logger = AppConfig.GetLogger<GachaLogPage>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();

    /// <summary>当前游戏对应的抽卡日志服务（由 <see cref="OnNavigatedTo"/> 根据游戏类型注入，支持原神/星铁/绝区零）。</summary>
    private GachaLogService _gachaLogService;


    /// <summary>卡片拖拽换位逻辑（按住统计区域拖动、悬停即换位、松手提交并持久化）。</summary>
    private readonly GachaStatsCardDragReorder _dragReorder;

    /// <summary>常驻抽卡卡片池：按卡池类型(GachaType)复用卡片实例。</summary>
    private readonly Dictionary<int, FrameworkElement> _gachaCardPool = new();

    /// <summary>上次重建卡片所基于的统计数据引用</summary>
    private List<GachaTypeStats>? _reconciledStatsSource;

    /// <summary>当前软件 UI 语言代码。</summary>
    private static string CurrentLanguage => System.Globalization.CultureInfo.CurrentUICulture.Name;



    public GachaLogPage()
    {
        this.InitializeComponent();
        _dragReorder = new GachaStatsCardDragReorder(ScrollViewer_GachaStats, Grid_GachaStats, StackPanel_GachaStats, SaveGachaCardOrder);
    }


    /// <summary>
    /// 页面导航到此时初始化游戏特定的抽卡服务、UI 标志位与表情图标。
    /// <para><b>输入：</b><paramref name="e"/> — 导航事件参数（基类 PageBase 从中解析 CurrentGameBiz / CurrentGameId）。</para>
    /// <para><b>副作用：</b>设置 <see cref="GachaTypeText"/> 文本；根据游戏类型注入对应 <see cref="_gachaLogService"/>
    /// （GenshinGachaService / StarRailGachaService / ZZZGachaService）、启用对应物品统计视图标志位、
    /// 设置表情图标；对国服绝区零显示米游社同步菜单项、国际服显示 HoYoLAB 同步菜单项；关闭国际服的云游戏入口。</para>
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        GachaTypeText = GachaLogService.GetGachaLogText(CurrentGameBiz);
        if (CurrentGameBiz.Game == GameBiz.hk4e)
        {
            EnableGenshinGachaItemStats = true;
            _gachaLogService = AppConfig.GetService<GenshinGachaService>();
            Image_Emoji.Source = new BitmapImage(AppConfig.EmojiPaimon);
        }
        if (CurrentGameBiz.Game == GameBiz.hkrpg)
        {
            EnableStarRailGachaItemStats = true;
            _gachaLogService = AppConfig.GetService<StarRailGachaService>();
            Image_Emoji.Source = new BitmapImage(AppConfig.EmojiPom);
        }
        if (CurrentGameBiz.Game == GameBiz.nap)
        {
            EnableZZZGachaItemStats = true;
            IsZZZGachaStatsCardVisible = true;
            _gachaLogService = AppConfig.GetService<ZZZGachaService>();
            Image_Emoji.Source = new BitmapImage(AppConfig.EmojiBangboo);
            MenuFlyoutItem_CloudGame.Visibility = Visibility.Collapsed;
            if (CurrentGameBiz.Value is GameBiz.nap_cn or GameBiz.nap_bilibili)
            {
                MenuFlyoutItem_SyncFromMiyoushe.Visibility = Visibility.Visible;
                MenuFlyoutItem_SyncFromMiyousheAll.Visibility = Visibility.Visible;
            }
            if (CurrentGameBiz == GameBiz.nap_global)
            {
                MenuFlyoutItem_SyncFromHoYoLAB.Visibility = Visibility.Visible;
                MenuFlyoutItem_SyncFromHoYoLABAll.Visibility = Visibility.Visible;
            }
        }
        if (CurrentGameBiz.IsGlobalServer())
        {
            MenuFlyoutItem_CloudGame.Visibility = Visibility.Collapsed;
        }
    }


    public bool IsZZZGachaStatsCardVisible { get; set => SetProperty(ref field, value); }


    public string GachaTypeText { get; set => SetProperty(ref field, value); }


    public ObservableCollection<long> UidList { get; set => SetProperty(ref field, value); }


    /// <summary>当前选中的 UID。切换时持久化并刷新抽卡统计数据。</summary>
    [ObservableProperty]
    public partial long? SelectUid { get; set; }
    partial void OnSelectUidChanged(long? value)
    {
        AppConfig.SetLastUidInGachaLogPage(CurrentGameBiz.Game, value ?? 0);
        UpdateGachaTypeStats(value);
        ShareGachaImageCommand.NotifyCanExecuteChanged();
    }


    /// <summary>分享图生成进行中时为 true，用于禁用分享按钮。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShareGachaImageCommand))]
    public partial bool IsSharingGachaImage { get; set; }




    /// <summary>
    /// 页面加载完成后的初始化逻辑（由 <see cref="PageBase"/> 的 Loaded 事件在构造函数中订阅后触发）。
    /// <para>无显式输入参数。</para>
    /// <para>隐式输入依赖：</para>
    /// <para>1. <see cref="OnNavigatedTo(NavigationEventArgs)"/> 已执行，CurrentGameId / CurrentGameBiz 已正确设置；</para>
    /// <para>2. XAML 控件树已完成加载（Grid_GachaStats、ScrollViewer_GachaStats 等控件可用）。</para>
    /// <para>输出/副作用：</para>
    /// <para>1. 延迟一帧（Task.Delay(16)）后注册两个 WeakReferenceMessenger 处理器：<see cref="UpdateGachaLogMessage"/>（外部触发抽卡记录更新时激活窗口并拉取）和 <see cref="GachaLogImportedMessage"/>（本地导入完成后刷新 UID 列表与统计）；</para>
    /// <para>2. 订阅 <see cref="Grid_GachaStats"/> 的 PointerWheelChanged 事件，用于拖拽时横向滚轮优先滚动；</para>
    /// <para>3. 调用 <see cref="Initialize"/> 完成卡池筛选列表（GachaBanners）、UID 列表加载、恢复上次选中 UID、以及空数据时的表情占位显示；</para>
    /// <para>4. 异步调用 <see cref="EnsureWikiDataAsync"/> 确保当前语言的卡池/物品维基数据已缓存（缺失才联网，不阻塞 UI）。</para>
    /// <para>注意：方法为 async void，符合基类事件驱动约定，不应被直接 await。</para>
    /// </summary>
    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        WeakReferenceMessenger.Default.Register<UpdateGachaLogMessage>(this, (s, m) =>
        {
            if (m.GameBiz == CurrentGameBiz)
            {
                User32.SetForegroundWindow((nint)this.XamlRoot.ContentIslandEnvironment.AppWindowId.Value);
                _ = UpdateGachaLogInternalAsync(m.Url);
            }
        });
        WeakReferenceMessenger.Default.Register<GachaLogImportedMessage>(this, (s, m) => OnGachaLogImported(m));
        WeakReferenceMessenger.Default.Register<GachaItemNameProgressMessage>(this, (s, m) => OnGachaItemNameProgress(m));
        Grid_GachaStats.PointerWheelChanged += Grid_GachaStats_PointerWheelChanged;
        Initialize();
        await EnsureWikiDataAsync();
    }



    /// <summary>
    /// 页面卸载时的资源清理（对应 OnLoaded 的初始化）。
    /// <para>1. 注销所有 WeakReferenceMessenger 注册的消息处理器（UpdateGachaLogMessage、GachaLogImportedMessage 等）；</para>
    /// <para>2. 移除 Grid_GachaStats 的 PointerWheelChanged 事件订阅；</para>
    /// <para>3. 调用 ClearGachaCards 清理常驻卡片池（_gachaCardPool）、解绑所有拖拽手柄并清空面板子元素；</para>
    /// <para>4. 清空并置空 GachaItemStats（物品统计列表）和 GachaBanners（卡池筛选列表），释放对象引用。</para>
    /// </summary>
    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Grid_GachaStats.PointerWheelChanged -= Grid_GachaStats_PointerWheelChanged;
        ClearGachaCards();
        if (GachaItemStats is not null)
        {
            GachaItemStats.Clear();
            GachaItemStats = null;
        }
        if (GachaBanners is not null)
        {
            GachaBanners.Clear();
            GachaBanners = null!;
        }
    }



    /// <summary>
    /// 初始化 GachaLogPage 的核心状态（在 OnLoaded 中调用一次）。
    /// <para>1. 调用 InitializeGachaBanners 初始化卡池筛选列表及默认/保存的勾选状态；</para>
    /// <para>2. 从服务加载当前游戏的所有 UID 列表；</para>
    /// <para>3. 尝试恢复上次在此页面选中的 UID（按游戏持久化），若不存在则选第一个；</para>
    /// <para>4. 若该游戏没有任何抽卡记录，则显示表情占位提示用户去获取数据。</para>
    /// </summary>
    private void Initialize()
    {
        try
        {
            InitializeGachaBanners();
            //获取上次选中的 UID，若无则默认第一个
            SelectUid = null;
            UidList = new(_gachaLogService.GetUids());
            var lastUid = AppConfig.GetLastUidInGachaLogPage(CurrentGameBiz.Game);
            if (UidList.Contains(lastUid))
            {
                SelectUid = lastUid;
            }
            else
            {
                SelectUid = UidList.FirstOrDefault();
            }
            if (UidList.Count == 0)
            {
                StackPanel_Emoji.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize");
        }
    }



    /// <summary>
    /// 卡池卡片区域的滚轮事件处理：让滚轮「只影响竖直方向」。
    /// <para>拖拽中：交给 <see cref="_dragReorder"/>.HandleWheel 横向滚动，便于把卡片拖到视口外的卡池处（仅 active drag 生效）。</para>
    /// <para>非拖拽：一律吞掉滚轮。落在某卡记录列表上的竖直滚轮已被该卡内部 ScrollViewer 处理、不会冒泡到此；
    /// 落在卡片间隙 / 等高短卡留白 / 列表滚到尽头处的滚轮在此被吞，阻止外层横向 <see cref="ScrollViewer_GachaStats"/>
    /// 把竖直滚轮转成横向滚动。横向翻看卡池请改用底部横向滚动条。</para>
    /// </summary>
    private void Grid_GachaStats_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 拖拽中：用滚轮横向滚动以便把卡片拖到视口外的卡池处。
        if (_dragReorder.HandleWheel(e))
        {
            return;
        }
        // 非拖拽：吞掉滚轮，阻止外层横向 ScrollViewer 把竖直滚轮转成横向滚动 → 滚轮只影响竖直方向。
        e.Handled = true;
    }



    /// <summary>
    /// 打开抽卡记录页时刷新当前游戏的抽卡维基数据（角色/物品信息 + 多语言名称）。
    /// <para>在 <see cref="OnLoaded"/> 末尾被 await 调用（async void，不阻塞页面其他初始化）。</para>
    /// <para>委托 <see cref="GachaItemNameService.RefreshGachaInfoForGameAsync"/>：后台联网获取该游戏<b>全部</b>角色/物品信息，
    /// 与本地信息表比对，<b>仅当出现新角色/新物品（或当前语言名称缓存缺失）时</b>才写入物品信息表与多语言名称缓存。
    /// 因此首次使用会全量下载，日常打开页面虽会联网比对、但通常无需写库。</para>
    /// <para>与「切换游戏」触发点共用同一协调方法，按游戏并发去重；任何异常仅记录日志，不影响页面加载。</para>
    /// </summary>
    private async Task EnsureWikiDataAsync()
    {
        try
        {
            // 打开抽卡页：联网获取全部角色/物品信息并比对，出现新内容（或当前语言名称缓存缺失）时更新信息表与多语言名称缓存。
            await AppConfig.GetService<GachaItemNameService>().RefreshGachaInfoForGameAsync(CurrentGameBiz);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ensure wiki data {gameBiz}", CurrentGameBiz);
        }
    }




    /// <summary>打开右侧物品统计面板（SplitView 的 Pane）。由工具栏 "Pane" 按钮绑定。</summary>
    [RelayCommand]
    private void OpenItemStatsPane()
    {
        SplitView_Content.IsPaneOpen = true;
    }




    #region Gacha Stats



    /// <summary>是否启用原神抽卡物品统计视图（x:Load 标志）。</summary>
    public bool EnableGenshinGachaItemStats { get; set => SetProperty(ref field, value); }

    /// <summary>是否启用星穹铁道抽卡物品统计视图（x:Load 标志）。</summary>
    public bool EnableStarRailGachaItemStats { get; set => SetProperty(ref field, value); }

    /// <summary>是否启用绝区零抽卡物品统计视图（x:Load 标志）。</summary>
    public bool EnableZZZGachaItemStats { get; set => SetProperty(ref field, value); }

    /// <summary>卡池筛选列表（DropDownButton 弹出菜单的数据源）。每项绑定一个 CheckBox 控制显示/隐藏。</summary>
    public List<GachaBanner> GachaBanners { get; set => SetProperty(ref field, value); }

    /// <summary>当前 UID 的物品统计列表（右侧 SplitView.Pane 数据源），按抽到次数降序。</summary>
    public List<GachaLogItemEx>? GachaItemStats { get; set => SetProperty(ref field, value); }

    /// <summary>当前 UID 的各卡池统计原始数据（内存缓存）。</summary>
    private List<GachaTypeStats>? gachaTypeStats;

    /// <summary>连续获取抽卡记录失败计数，用于触发"删除缓存文件"提示。</summary>
    private int errorCount = 0;


    /// <summary>
    /// 初始化卡池筛选列表（GachaBanners）。
    /// <para>从 _gachaLogService.QueryGachaTypes 获取当前游戏所有卡池类型，包装成 GachaBanner；</para>
    /// <para>若无用户保存的勾选偏好（首次启动或该游戏从未在抽卡页配置过），默认勾选全部卡池；</para>
    /// <para>若用户此前有保存的筛选，则恢复之；针对版本更新新增的卡池（星铁联动、ZZZ 重映/回响），仅对有旧保存的用户做一次性追加勾选，避免错过新内容，同时持久化更新后的选择。</para>
    /// </summary>
    private void InitializeGachaBanners()
    {
        GachaBanners = _gachaLogService.QueryGachaTypes.Select(x => new GachaBanner(x)).ToList();
        string? banner = AppConfig.GetDisplayGachaBanners(CurrentGameBiz.Game);

        // banner 为 null 表示从未写入过（首次启动）；为 "" 表示用户明确清空过选择。
        bool hadExplicitSetting = banner is not null;

        if (hadExplicitSetting && !string.IsNullOrWhiteSpace(banner))
        {
            var saved = banner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Select(x => int.TryParse(x, out int v) ? v : int.MinValue)
                              .Where(x => x != int.MinValue)
                              .ToHashSet();
            foreach (GachaBanner b in GachaBanners)
            {
                b.IsSelected = saved.Contains(b.Value);
            }
        }
        // 否则：无保存或保存为空字符串 → 暂不勾选任何（留待下方默认全选或保持清空）。

        // 版本更新一次性追加：仅当用户之前已有保存的筛选时，才把新增卡池类型加入当前选择。
        // 全新用户走“无保存则默认全选”分支，新池自然包含在内。
        bool didPromote = false;
        if (CurrentGameBiz.Game is GameBiz.hkrpg && !AppConfig.GetValue(false, "SavedStarRailBannersAfterCollaborationStarting"))
        {
            if (hadExplicitSetting && !string.IsNullOrWhiteSpace(banner))
            {
                foreach (GachaBanner b in GachaBanners.Where(x => x.Value is 21 or 22))
                {
                    b.IsSelected = true;
                }
            }
            AppConfig.SetValue(true, "SavedStarRailBannersAfterCollaborationStarting");
            didPromote = true;
        }
        if (CurrentGameBiz.Game is GameBiz.nap && !AppConfig.GetValue(false, "SavedZZZBannersSinceVersion2.5"))
        {
            if (hadExplicitSetting && !string.IsNullOrWhiteSpace(banner))
            {
                foreach (GachaBanner b in GachaBanners.Where(x => x.Value is 102 or 103))
                {
                    b.IsSelected = true;
                }
            }
            AppConfig.SetValue(true, "SavedZZZBannersSinceVersion2.5");
            didPromote = true;
        }

        // 首次使用该游戏的抽卡记录页面（无显式保存的 banner 偏好）→ 默认选择全部卡池。
        // 这会让原神、星铁、绝区零在软件首次启动时都显示所有卡池统计卡片。
        if (!hadExplicitSetting)
        {
            foreach (GachaBanner b in GachaBanners)
            {
                b.IsSelected = true;
            }
        }

        // 持久化默认选择（首次）或升级后的追加选择（旧用户），让偏好在下次启动时被记住。
        if (!hadExplicitSetting || didPromote)
        {
            string value = string.Join(',', GachaBanners.Where(x => x.IsSelected).Select(x => x.Value));
            AppConfig.SetDisplayGachaBanners(CurrentGameBiz.Game, value);
        }

        UpdateGachaBannerFilterIndicator();
    }


    /// <summary>用户点击某个卡池复选框 → 实时提交筛选。</summary>
    private void GachaBannerCheckBox_Click(object sender, RoutedEventArgs e)
    {
        ApplyGachaBannerFilter();
    }


    /// <summary>提交卡池筛选：持久化所选卡池并刷新显示（实时生效）。</summary>
    private void ApplyGachaBannerFilter()
    {
        try
        {
            if (GachaBanners is null)
            {
                return;
            }
            string value = string.Join(',', GachaBanners.Where(x => x.IsSelected).Select(x => x.Value));
            AppConfig.SetDisplayGachaBanners(CurrentGameBiz.Game, value);
            if (CurrentGameBiz.Game is GameBiz.hkrpg)
            {
                AppConfig.SetValue(true, "SavedStarRailBannersAfterCollaborationStarting");
            }
            if (CurrentGameBiz.Game is GameBiz.nap)
            {
                AppConfig.SetValue(true, "SavedZZZBannersSinceVersion2.5");
            }
            UpdateDisplayGachaTypeStats(playEntranceAnimation: false);
            UpdateGachaBannerFilterIndicator();
        }
        catch { }
    }


    /// <summary>全选：显示全部卡池。</summary>
    private void GachaBannerSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (GachaBanners is null)
        {
            return;
        }
        foreach (GachaBanner b in GachaBanners)
        {
            b.IsSelected = true;
        }
        ApplyGachaBannerFilter();
    }


    /// <summary>清除：取消全部勾选（不显示任何卡池）。</summary>
    private void GachaBannerClear_Click(object sender, RoutedEventArgs e)
    {
        if (GachaBanners is null)
        {
            return;
        }
        foreach (GachaBanner b in GachaBanners)
        {
            b.IsSelected = false;
        }
        ApplyGachaBannerFilter();
    }


    /// <summary>反选：逐项翻转勾选状态。</summary>
    private void GachaBannerInvert_Click(object sender, RoutedEventArgs e)
    {
        if (GachaBanners is null)
        {
            return;
        }
        foreach (GachaBanner b in GachaBanners)
        {
            b.IsSelected = !b.IsSelected;
        }
        ApplyGachaBannerFilter();
    }


    /// <summary>筛选生效（非全选）时把筛选按钮图标点亮为强调色，提示「正在筛选」。</summary>
    private void UpdateGachaBannerFilterIndicator()
    {
        try
        {
            bool filtered = GachaBanners is { Count: > 0 } && GachaBanners.Any(x => !x.IsSelected);
            FontIcon_GachaBannerFilter.Foreground = filtered
                ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }
        catch { }
    }



    /// <summary>
    /// 根据选中的 uid 加载并更新抽卡统计数据。
    /// <para>uid 为空或 0：清空 gachaTypeStats、物品统计与卡片池，显示表情占位；</para>
    /// <para>否则调用服务 GetGachaTypeStats 获取各卡池统计与物品统计，然后触发显示刷新（含入场动画）并隐藏表情。</para>
    /// </summary>
    private void UpdateGachaTypeStats(long? uid)
    {
        try
        {
            if (uid is null or 0)
            {
                gachaTypeStats = null;
                _reconciledStatsSource = null;
                ClearGachaCards();
                GachaItemStats = null;
                StackPanel_Emoji.Visibility = Visibility.Visible;
            }
            else
            {
                (gachaTypeStats, GachaItemStats) = _gachaLogService.GetGachaTypeStats(uid.Value);
                UpdateDisplayGachaTypeStats(playEntranceAnimation: true);
                StackPanel_Emoji.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateGachaTypeStats");
        }
    }


    /// <summary>
    /// 获取当前筛选并排序后的卡池统计列表（与 <see cref="UpdateDisplayGachaTypeStats"/> 展示数据一致）。
    /// </summary>
    /// <returns>已选卡池的统计数据；无数据或未选卡池时返回空列表。</returns>
    private List<GachaTypeStats> GetDisplayedGachaTypeStats()
    {
        if (gachaTypeStats is null)
        {
            return [];
        }
        var stats = new List<GachaTypeStats>();
        if (GachaBanners is not null)
        {
            foreach (GachaBanner banner in GachaBanners)
            {
                if (banner.IsSelected
                    && gachaTypeStats.FirstOrDefault(x => x.GachaType == banner.Value) is GachaTypeStats s)
                {
                    stats.Add(s);
                }
            }
        }
        return ApplySavedCardOrder(stats);
    }


    /// <summary>
    /// 根据当前卡池筛选（GachaBanners 勾选状态）和用户拖拽保存的次序，刷新统计卡片的显示。
    /// <para>从 gachaTypeStats 中筛选已选卡池 → ApplySavedCardOrder 稳定排序 → ReconcileGachaCards。</para>
    /// <para>playEntranceAnimation 控制是否播放入场动画（数据刷新时为 true，筛选切换时通常为 false）。</para>
    /// </summary>
    private void UpdateDisplayGachaTypeStats(bool playEntranceAnimation = true)
    {
        if (gachaTypeStats is null)
        {
            return;
        }
        ReconcileGachaCards(GetDisplayedGachaTypeStats(), playEntranceAnimation);
        ShareGachaImageCommand.NotifyCanExecuteChanged();
    }


    /// <summary>
    /// 将当前所选卡池统计渲染为分享图并打开内置图片查看器。
    /// 视频背景通过 AppBackground 捕获当前帧快照（官方视频移除 overlay），渲染器对整个大背景应用浅亚克力 + 卡片亚克力。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShareGachaImage))]
    private async Task ShareGachaImageAsync()
    {
        try
        {
            if (SelectUid is not long uid || uid == 0)
            {
                return;
            }
            var stats = GetDisplayedGachaTypeStats();
            if (stats.Count == 0)
            {
                return;
            }

            IsSharingGachaImage = true;

            // 获取当前背景文件路径；视频需捕获快照（返回临时 PNG），图片直接使用原路径。
            string? backgroundFile = BackgroundService.GetCachedBackgroundFile(CurrentGameId);
            if (backgroundFile is not null && BackgroundService.FileIsSupportedVideo(backgroundFile))
            {
                backgroundFile = AppBackground.Current is not null
                    ? await AppBackground.Current.CaptureCurrentBackgroundSnapshotAsync()
                    : null;
            }

            // 强调色须在 UI 线程读取；Win2D 离屏渲染放到后台线程，避免 Application.Current 跨线程 COM 异常。
            Color accentColor = GetShareImageAccentColor();
            string file = await Task.Run(async () =>
                await GachaShareImageRenderer.RenderAndSaveAsync(stats, CurrentGameBiz, backgroundFile, uid, accentColor));
            await new ImageViewWindow2().ShowWindowAsync(this.XamlRoot.ContentIslandEnvironment.AppWindowId, file, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Share gacha image");
            InAppToast.MainWindow?.Error(ex);
        }
        finally
        {
            IsSharingGachaImage = false;
        }
    }


    /// <summary>是否允许触发分享图生成。</summary>
    private bool CanShareGachaImage()
        => !IsSharingGachaImage
           && SelectUid is > 0
           && gachaTypeStats is { Count: > 0 }
           && GachaBanners?.Any(x => x.IsSelected) == true;


    /// <summary>在 UI 线程读取主题强调色，供离屏分享图渲染使用。</summary>
    private static Color GetShareImageAccentColor()
    {
        if (Application.Current.Resources["AccentFillColorDefaultBrush"] is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
    }


    /// <summary>把当前展示卡片的卡池次序持久化（拖拽换位提交后回调）。次序 = 面板里可见卡片的子元素顺序。</summary>
    private void SaveGachaCardOrder()
    {
        try
        {
            var order = new List<int>();
            foreach (UIElement child in StackPanel_GachaStats.Children)
            {
                if (child is IGachaStatsDragCard card
                    && child is FrameworkElement { Visibility: Visibility.Visible }
                    && card.WarpTypeStats is { } stats)
                {
                    order.Add(stats.GachaType);
                }
            }
            AppConfig.SetGachaCardOrder(CurrentGameBiz.Game, string.Join(',', order));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save gacha card order");
        }
    }


    /// <summary>
    /// 按持久化的卡片次序对卡池统计稳定排序：已记录的卡池按记录次序在前，未记录的维持原有相对次序在后。
    /// </summary>
    private List<GachaTypeStats> ApplySavedCardOrder(List<GachaTypeStats> stats)
    {
        try
        {
            string? saved = AppConfig.GetGachaCardOrder(CurrentGameBiz.Game);
            if (string.IsNullOrWhiteSpace(saved))
            {
                return stats;
            }
            var order = saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Select(x => int.TryParse(x, out int v) ? v : int.MinValue)
                             .Where(x => x != int.MinValue)
                             .ToList();
            if (order.Count == 0)
            {
                return stats;
            }
            // OrderBy 为稳定排序：键相同（均未记录 = int.MaxValue）的项维持原有相对次序。
            return stats.OrderBy(s =>
            {
                int i = order.IndexOf(s.GachaType);
                return i < 0 ? int.MaxValue : i;
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply saved gacha card order");
            return stats;
        }
    }


    /// <summary>抽卡统计卡片的固定宽度</summary>
    private const double GachaStatsCardWidth = 262;


    /// <summary>
    /// 按「最终展示次序」对齐常驻卡片池与面板子元素：复用已有卡片，只切 Visibility、用 <see cref="UIElementCollection.Move(uint, uint)"/> 重排。
    /// <para>核心优化：_gachaCardPool 作为常驻池，纯筛选（卡池勾选变化）时零重建，仅通过 Visibility + Move 调整可见顺序；</para>
    /// <para>仅当 gachaTypeStats 引用变化（切 UID、数据刷新/导入后）时才调用 ClearGachaCards 彻底重建。</para>
    /// </summary>
    private void ReconcileGachaCards(List<GachaTypeStats> ordered, bool playEntranceAnimation = true)
    {
        Panel panel = StackPanel_GachaStats;

        if (!ReferenceEquals(gachaTypeStats, _reconciledStatsSource))
        {
            ClearGachaCards();
            _reconciledStatsSource = gachaTypeStats;
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            //重要：开始和子页面交互
            FrameworkElement card = GetOrCreateGachaCard(ordered[i]);
            card.Visibility = Visibility.Visible;

            int current = panel.Children.IndexOf(card);
            if (current < 0)
            {
                // 插入时用 Math.Min 防止索引越界（极端情况下面板子元素数量可能少于 i）。
                panel.Children.Insert(Math.Min(i, panel.Children.Count), card);
            }
            else if (current != i)
            {
                panel.Children.Move((uint)current, (uint)i);
            }
        }

        // 隐藏未被选中的卡片。把不在 visible 中的卡片设为 Collapsed。
        var visible = new HashSet<int>();
        foreach (GachaTypeStats s in ordered)
        {
            visible.Add(s.GachaType);
        }
        foreach (KeyValuePair<int, FrameworkElement> kv in _gachaCardPool)
        {
            if (!visible.Contains(kv.Key))
            {
                kv.Value.Visibility = Visibility.Collapsed;
            }
        }

        // 按需播放入场动画。
        if (playEntranceAnimation && EntranceAnimation.AnimationsEnabled())
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (_gachaCardPool.TryGetValue(ordered[i].GachaType, out FrameworkElement? card))
                {
                    EntranceAnimation.PlayItem(card, i);
                }
            }
        }
    }


    /// <summary>取池中卡片；没有则按当前游戏建对应类型卡片，赋数据/宽度、挂拖拽手柄后入池（尚未加入面板）。</summary>
    private FrameworkElement GetOrCreateGachaCard(GachaTypeStats stats)
    {
        if (_gachaCardPool.TryGetValue(stats.GachaType, out FrameworkElement? existing))
        {
            return existing;
        }
        IGachaStatsDragCard card;
        if (IsZZZGachaStatsCardVisible)
        {
            //创建卡片控件
            card = new ZZZGachaStatsCard();
        }
        else
        {
            card = new GachaStatsCard();
        }
        FrameworkElement element = (FrameworkElement)card;
        element.Width = GachaStatsCardWidth;
        element.DataContext = stats;     // 供拖拽手柄识别其卡池数据
        card.WarpTypeStats = stats;      // 须在加入可视化树前赋值，卡片内 OneTime x:Bind 才能取到
        _dragReorder.Attach(card.DragHandle);
        _gachaCardPool[stats.GachaType] = element;
        return element;
    }


    /// <summary>清空卡片池与面板子元素（数据刷新/切 uid/离开页面）：解绑手柄并移除子元素，触发卡片 Unloaded 释放其内部绑定。</summary>
    private void ClearGachaCards()
    {
        foreach (FrameworkElement card in _gachaCardPool.Values)
        {
            if (card is IGachaStatsDragCard dragCard)
            {
                _dragReorder.Detach(dragCard.DragHandle);
            }
        }
        _gachaCardPool.Clear();
        StackPanel_GachaStats?.Children.Clear();
    }



    #endregion



    #region Get Gacha



    /// <summary>
    /// 更新抽卡记录（SplitButton 主命令）。
    /// <para><b>输入：</b><paramref name="param"/> — 可选参数：
    /// <c>"cache"</c> 使用已缓存的 URL 更新；<c>"all"</c> 更新全部记录；<c>null</c> 从游戏 Web 缓存获取最新 URL。</para>
    /// <para><b>副作用：</b>调用 <see cref="UpdateGachaLogInternalAsync"/> 执行网络请求；失败时通过 InAppToast 提示用户；连续失败达到阈值时建议删除缓存。</para>
    /// </summary>
    [RelayCommand]
    private async Task UpdateGachaLogAsync(string? param = null)
    {
        try
        {
            string? url = null;
            if (param is "cache")
            {
                if (SelectUid is null or 0)
                {
                    return;
                }
                url = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
                if (string.IsNullOrWhiteSpace(url))
                {
                    // 无法找到 uid {uid} 的已缓存 URL
                    InAppToast.MainWindow?.Warning(null, string.Format(Lang.GachaLogPage_CannotFindSavedURLOfUid, SelectUid));
                    return;
                }
            }
            else
            {
                var path = GameLauncherService.GetGameInstallPath(CurrentGameId);
                if (!Directory.Exists(path))
                {
                    // 游戏未安装
                    InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_GameNotInstalled);
                    return;
                }
                InfoBar? validatingBar = null;
                try
                {
                    // 多候选 URL 校验：优先使用仍有效的 authkey，减少误删缓存
                    validatingBar = new InfoBar
                    {
                        Severity = InfoBarSeverity.Informational,
                        Message = Lang.GachaLogPage_ValidatingGachaUrl,
                        Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
                        IsOpen = true,
                    };
                    InAppToast.MainWindow?.Show(validatingBar);
                    url = await _gachaLogService.GetValidatedGachaLogUrlFromWebCacheAsync(CurrentGameBiz, path);
                    validatingBar.IsOpen = false;
                }
                catch (miHoYoApiException ex) when (ex.ReturnCode is -101 or -1)
                {
                    if (validatingBar is not null)
                    {
                        validatingBar.IsOpen = false;
                    }
                    errorCount++;
                    if (errorCount > 1 && IsGachaCacheFileExists())
                    {
                        errorCount = 0;
                        InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning,
                                                              Lang.GachaLogPage_AlwaysFailedToGetGachaRecords,
                                                              Lang.GachaLogPage_RestartGameAfterDeletingTheCacheFolder,
                                                              Lang.GachaLogPage_DeleteCacheFolder,
                                                              () => _ = DeleteGachaCacheFolderAsync());
                    }
                    else
                    {
                        InAppToast.MainWindow?.Warning("Authkey Timeout", Lang.GachaLogPage_PleaseOpenTheGachaRecordsPageInGameAndTryAgain);
                    }
                    return;
                }
                if (string.IsNullOrWhiteSpace(url))
                {
                    if (validatingBar is not null)
                    {
                        validatingBar.IsOpen = false;
                    }
                    // 无法找到 URL，请在游戏中打开抽卡记录页面
                    errorCount++;
                    if (errorCount > 2 && IsGachaCacheFileExists())
                    {
                        errorCount = 0;
                        InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning,
                                                              Lang.GachaLogPage_AlwaysFailedToGetGachaRecords,
                                                              Lang.GachaLogPage_RestartGameAfterDeletingTheCacheFolder,
                                                              Lang.GachaLogPage_DeleteCacheFolder,
                                                              () => _ = DeleteGachaCacheFolderAsync());
                    }
                    else
                    {
                        InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CannotFindURL);
                    }
                    return;
                }
            }
            await UpdateGachaLogInternalAsync(url, param is "all");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }




    /// <summary>
    /// 执行实际的抽卡记录网络请求（带进度 InfoBar 和取消支持）。
    /// <para><b>输入：</b><paramref name="url"/> — 抽卡 API 完整 URL（含 authkey）；
    /// <paramref name="all"/> — 是否拉取全部记录（默认 false 仅拉取最新）。</para>
    /// <para><b>副作用：</b>通过 <see cref="_gachaLogService"/>.GetGachaLogAsync 拉取数据写入本地数据库；
    /// 成功后刷新 UID 列表与统计卡片；可取消（TaskCanceledException 仅记录日志）。</para>
    /// </summary>
    private async Task UpdateGachaLogInternalAsync(string url, bool all = false)
    {
        try
        {
            var uid = await _gachaLogService.GetUidFromGachaLogUrl(url);
            var cancelSource = new CancellationTokenSource();
            var button = new Button
            {
                // 取消
                Content = Lang.Common_Cancel,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var infoBar = new InfoBar
            {
                Severity = InfoBarSeverity.Informational,
                Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
                ActionButton = button,
            };
            button.Click += (_, _) =>
            {
                cancelSource.Cancel();
                // 操作已取消
                infoBar.Message = Lang.GachaLogPage_OperationCanceled;
                infoBar.ActionButton = null;
            };
            InAppToast.MainWindow?.Show(infoBar);
            var progress = new Progress<string>((str) => infoBar.Message = str);
            var newUid = await _gachaLogService.GetGachaLogAsync(url, all, CurrentLanguage, progress, cancelSource.Token);
            infoBar.Title = $"Uid {newUid}";
            infoBar.Severity = InfoBarSeverity.Success;
            infoBar.ActionButton = null;
            if (SelectUid == uid)
            {
                UpdateGachaTypeStats(uid);
            }
            else
            {
                if (!UidList.Contains(uid))
                {
                    UidList.Add(uid);
                }
                SelectUid = uid;
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Get gacha log canceled");
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogWarning("Request mihoyo api error: {error}", ex.Message);
            // 原铁 -101 绝 -1
            if (ex.ReturnCode is -101 or -1)
            {
                // authkey timeout
                // 请在游戏中打开抽卡记录页面后再重试
                errorCount++;
                if (errorCount > 1 && IsGachaCacheFileExists())
                {
                    errorCount = 0;
                    InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning,
                                                          Lang.GachaLogPage_AlwaysFailedToGetGachaRecords,
                                                          Lang.GachaLogPage_RestartGameAfterDeletingTheCacheFolder,
                                                          Lang.GachaLogPage_DeleteCacheFolder,
                                                          () => _ = DeleteGachaCacheFolderAsync());
                }
                else
                {
                    InAppToast.MainWindow?.Warning("Authkey Timeout", Lang.GachaLogPage_PleaseOpenTheGachaRecordsPageInGameAndTryAgain);
                }
            }
            else
            {
                InAppToast.MainWindow?.Warning(null, ex.Message);
            }
        }
    }


    /// <summary>
    /// 从米游社/HoYoLAB 同步绝区零抽卡记录。
    /// <para><b>输入：</b><paramref name="param"/> — <c>"all"</c> 同步全部记录，<c>null</c> 仅最新。</para>
    /// <para><b>副作用：</b>仅支持绝区零国服/国际服；多角色时弹出选择对话框；通过 ZZZGachaService 拉取数据，带可取消的进度 InfoBar。</para>
    /// </summary>
    [RelayCommand]
    private async Task SyncFromMiyousheAsync(string? param = null)
    {
        try
        {
            if (CurrentGameBiz.Game != GameBiz.nap)
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_OnlySupportZZZCNServer);
                return;
            }
            if (_gachaLogService is not ZZZGachaService zzzGachaService)
            {
                throw new InvalidOperationException($"Current gacha service type is {_gachaLogService.GetType().Name}.");
            }
            GameBiz roleGameBiz = CurrentGameBiz == GameBiz.nap_bilibili ? GameBiz.nap_cn : CurrentGameBiz;
            var roles = _gameRecordService.GetGameRoles(roleGameBiz);
            if (roles.Count == 0)
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_PleaseLoginMiyousheAndAddZZZRole);
                WeakReferenceMessenger.Default.Send(new MainViewNavigateMessage(typeof(GameRecordPage)));
                return;
            }
            GameRecordRole? role;
            if (roles.Count == 1)
            {
                role = roles[0];
            }
            else
            {
                role = await SelectMiyousheRoleAsync(roles, roleGameBiz);
                if (role is null)
                {
                    return;
                }
            }
            if (string.IsNullOrWhiteSpace(role.Cookie))
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CurrentAccountMissingCookiePleaseReloginMiyoushe);
                return;
            }
            _gameRecordService.SetLastSelectGachaSyncRole(roleGameBiz, role);
            var cancelSource = new CancellationTokenSource();
            var button = new Button
            {
                // 取消
                Content = Lang.Common_Cancel,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var infoBar = new InfoBar
            {
                Severity = InfoBarSeverity.Informational,
                Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
                ActionButton = button,
            };
            button.Click += (_, _) =>
            {
                cancelSource.Cancel();
                // 操作已取消
                infoBar.Message = Lang.GachaLogPage_OperationCanceled;
                infoBar.ActionButton = null;
            };
            InAppToast.MainWindow?.Show(infoBar);
            var progress = new Progress<string>((str) => infoBar.Message = str);
            var uid = await zzzGachaService.GetGachaLogByGameRecordAsync(role, param is "all", CurrentLanguage, progress, cancelSource.Token);
            infoBar.Title = $"Uid {uid}";
            infoBar.Severity = InfoBarSeverity.Success;
            infoBar.ActionButton = null;
            if (SelectUid == uid)
            {
                UpdateGachaTypeStats(uid);
            }
            else
            {
                if (!UidList.Contains(uid))
                {
                    UidList.Add(uid);
                }
                SelectUid = uid;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sync zzz gacha record from miyoushe canceled");
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogWarning(ex, "Sync zzz gacha record from miyoushe error ({retcode})", ex.ReturnCode);
            InAppToast.MainWindow?.Warning(null, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Sync zzz gacha record from miyoushe is not supported");
            InAppToast.MainWindow?.Warning(null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync zzz gacha record from miyoushe");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    /// <summary>
    /// 弹出 ComboBox 对话框让用户选择米游社/HoYoLAB 角色。
    /// <para><b>输入：</b><paramref name="roles"/> — 游戏角色列表；<paramref name="roleGameBiz"/> — 角色对应的 GameBiz。</para>
    /// <para><b>返回值：</b>用户选中的 <see cref="GameRecordRole"/>，取消则返回 null。</para>
    /// </summary>
    private async Task<GameRecordRole?> SelectMiyousheRoleAsync(List<GameRecordRole> roles, GameBiz roleGameBiz)
    {
        var items = roles.Select(role => new MiyousheRoleItem(role)).ToList();
        MiyousheRoleItem? selectedItem = null;
        if (SelectUid is long currentUid && currentUid > 0)
        {
            selectedItem = items.FirstOrDefault(x => x.Role.Uid == currentUid);
        }
        var selectedRole = _gameRecordService.GetLastSelectGachaSyncRoleOrTheFirstOne(roleGameBiz);
        selectedItem ??= items.FirstOrDefault(x =>
            selectedRole is not null
            && x.Role.Uid == selectedRole.Uid);
        var comboBox = new ComboBox
        {
            MinWidth = 420,
            ItemsSource = items,
            DisplayMemberPath = nameof(MiyousheRoleItem.DisplayText),
            SelectedItem = selectedItem ?? items.FirstOrDefault(),
        };
        var panel = new StackPanel
        {
            Spacing = 8,
        };
        panel.Children.Add(new TextBlock
        {
            Text = Lang.GachaLogPage_SelectMiyousheRoleDescription,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(comboBox);
        var dialog = new ContentDialog
        {
            Title = Lang.GachaLogPage_SelectMiyousheRole,
            Content = panel,
            PrimaryButtonText = Lang.Common_Confirm,
            SecondaryButtonText = Lang.Common_Cancel,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = comboBox.SelectedItem is not null,
            XamlRoot = this.XamlRoot,
        };
        comboBox.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = comboBox.SelectedItem is not null;
        if (await dialog.ShowAsync() is not ContentDialogResult.Primary)
        {
            return null;
        }
        if (comboBox.SelectedItem is not MiyousheRoleItem item)
        {
            InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_PleaseSelectMiyousheRole);
            return null;
        }
        return item.Role;
    }


    private sealed class MiyousheRoleItem
    {
        public GameRecordRole Role { get; }

        public string DisplayText { get; }

        public MiyousheRoleItem(GameRecordRole role)
        {
            Role = role;
            DisplayText = $"{role.Nickname} ({role.Uid}) | {role.RegionName}";
        }
    }



    /// <summary>
    /// 通过 URL 更新抽卡记录：弹出对话框，预填当前 UID 已保存的 URL（若有），可直接确认或粘贴新 URL 后拉取。
    /// <para><b>副作用：</b>弹出 ContentDialog；确认后调用 <see cref="UpdateGachaLogInternalAsync"/> 拉取数据。</para>
    /// </summary>
    [RelayCommand]
    private async Task InputUrlAsync()
    {
        try
        {
            var textbox = new TextBox { MinWidth = 400 };
            // 预填当前 UID 已保存的 URL，合并「更新保存的 URL」与「输入 URL」
            if (SelectUid is > 0)
            {
                var saved = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    textbox.Text = saved;
                }
            }
            var dialog = new ContentDialog
            {
                // 通过 URL 更新
                Title = Lang.GachaLogPage_InputURL,
                Content = textbox,
                // 确认
                PrimaryButtonText = Lang.Common_Confirm,
                // 取消
                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var url = textbox.Text;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    await UpdateGachaLogInternalAsync(url);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Input url");
            InAppToast.MainWindow?.Error(ex);
        }
    }




    /// <summary>打开云游戏抽卡窗口（通过云游戏方式获取抽卡 URL）。</summary>
    [RelayCommand]
    private void OpenCloudGameWindow()
    {
        try
        {
            new CloudGameGachaWindow { GameBiz = CurrentGameBiz }.Activate();
        }
        catch { }
    }



    #endregion



    #region Gacha Setting Panel


    /// <summary>
    /// 将当前 UID 的抽卡 URL 复制到剪贴板。
    /// <para><b>副作用：</b>复制成功后按钮图标短暂变为 ✓（1 秒后恢复）。</para>
    /// </summary>
    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        try
        {
            if (SelectUid is null or 0)
            {
                return;
            }
            var url = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
            if (!string.IsNullOrWhiteSpace(url))
            {
                ClipboardHelper.SetText(url);
                FontIcon_CopyUrl.Glyph = "\uE8FB"; // accept
                await Task.Delay(1000);
                FontIcon_CopyUrl.Glyph = "\uE8C8";  // copy
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy url");
        }
    }



    /// <summary>
    /// 收到名称回写进度消息时（仅当前游戏）显示/更新进度提示条；完成后关闭并刷新统计以显示新语言名称。
    /// <para>进度消息来自 <see cref="GachaItemNameService"/>，可能在后台线程发出，故 UI 操作统一切回 DispatcherQueue。</para>
    /// </summary>
    private void OnGachaItemNameProgress(GachaItemNameProgressMessage message)
    {
        if (message.Game.Game != CurrentGameBiz.Game)
        {
            return;
        }
        // 直接控制页面内固定的 InfoBar（单实例），不经由 InAppToast 二次延迟入队，避免完成关闭与异步显示乱序导致进度条叠加。
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (message.Completed)
                {
                    InfoBar_NameProgress.IsOpen = false;
                    // 回写完成（或失败终止）后重读数据库，显示最新名称。
                    UpdateGachaTypeStats(SelectUid);
                    return;
                }
                if (message.Total <= 0)
                {
                    return;
                }
                // 下载映射阶段（Done==0）显示不确定态，回写阶段显示确定进度。
                if (message.Done == 0)
                {
                    ProgressBar_NameProgress.IsIndeterminate = true;
                    InfoBar_NameProgress.Message = null;
                }
                else
                {
                    ProgressBar_NameProgress.IsIndeterminate = false;
                    ProgressBar_NameProgress.Value = (double)message.Done / message.Total * 100;
                    InfoBar_NameProgress.Message = $"{message.Done} / {message.Total}";
                }
                InfoBar_NameProgress.IsOpen = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Show gacha item name progress");
            }
        });
    }



    /// <summary>
    /// 删除当前 UID 的全部抽卡记录。
    /// <para><b>副作用：</b>弹出确认对话框；确认后调用服务删除并显示 Toast，然后重新初始化页面。</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteUidAsync()
    {
        try
        {
            var uid = SelectUid;
            if (uid is null or 0)
            {
                return;
            }
            var dialog = new ContentDialog
            {
                // 警告
                Title = Lang.Common_Warning,
                // 即将删除 Uid {uid} 的所有抽卡记录，此操作不可恢复。
                Content = string.Format(Lang.GachaLogPage_DeleteGachaRecordsWarning, uid),
                // 删除
                PrimaryButtonText = Lang.Common_Delete,
                // 取消
                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var count = _gachaLogService.DeleteUid(uid.Value);
                // 已删除 Uid {uid} 的抽卡记录 {count} 条
                InAppToast.MainWindow?.Success(null, string.Format(Lang.GachaLogPage_DeletedGachaRecordsOfUid, count, uid));
                Initialize();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete uid");
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 按时间段删除抽卡记录。
    /// <para><b>副作用：</b>弹出 <see cref="DeleteGachaLogDialog"/> 让用户选择时间范围；删除后刷新统计。</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteUidByTimeAsync()
    {
        try
        {
            var dialog = new DeleteGachaLogDialog
            {
                CurrentGameBiz = this.CurrentGameBiz,
                DefaultUid = this.SelectUid,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (dialog.Deleted)
            {
                UpdateGachaTypeStats(dialog.SelectUid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete uid");
        }
    }



    /// <summary>
    /// 一键删除游戏 webCaches（解决 authkey timeout）。游戏运行中拒绝删除；确认后递归删除并清理本地保存的 URL。
    /// 次要操作可仅在资源管理器中打开文件夹。
    /// </summary>
    [RelayCommand]
    private async Task DeleteGachaCacheFolderAsync()
    {
        try
        {
            var installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            if (!Directory.Exists(installPath))
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_GameNotInstalled);
                return;
            }

            var webCachesPath = GachaLogClient.GetWebCachesFolderPath(CurrentGameBiz, installPath);
            // 安全：路径必须落在安装目录下的 webCaches，防止误删
            string fullInstall = Path.GetFullPath(installPath);
            string fullCaches = Path.GetFullPath(webCachesPath);
            if (!fullCaches.StartsWith(fullInstall.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !fullCaches.EndsWith($"{Path.DirectorySeparatorChar}webCaches", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Refuse to delete unexpected webCaches path: {Path}", fullCaches);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = Lang.GachaLogPage_DeleteCacheFolderConfirmTitle,
                Content = Lang.GachaLogPage_DeleteCacheFolderConfirmContent,
                PrimaryButtonText = Lang.Common_Delete,
                SecondaryButtonText = Lang.GachaLogPage_OpenCacheFolder,
                CloseButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                await OpenGachaCacheFolderInExplorerAsync(webCachesPath);
                return;
            }
            if (result is not ContentDialogResult.Primary)
            {
                return;
            }

            var process = await _gameLauncherService.GetGameProcessAsync(CurrentGameId);
            if (process is not null)
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CannotDeleteCacheWhileGameIsRunning);
                return;
            }

            if (!Directory.Exists(webCachesPath))
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CacheFolderNotFound);
                return;
            }

            Directory.Delete(webCachesPath, recursive: true);
            _gachaLogService.DeleteSavedGachaLogUrl(SelectUid);
            errorCount = 0;
            InAppToast.MainWindow?.Success(null, Lang.GachaLogPage_CacheFolderDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete gacha cache file");
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 在资源管理器中打开并选中 webCaches 文件夹。
    /// </summary>
    /// <param name="webCachesPath">webCaches 完整路径。</param>
    private async Task OpenGachaCacheFolderInExplorerAsync(string webCachesPath)
    {
        if (!Directory.Exists(webCachesPath))
        {
            InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CacheFolderNotFound);
            return;
        }
        var folder = await StorageFolder.GetFolderFromPathAsync(webCachesPath);
        var option = new FolderLauncherOptions();
        option.ItemsToSelect.Add(folder);
        await Launcher.LaunchFolderAsync(await folder.GetParentAsync(), option);
    }



    /// <summary>
    /// 检查游戏抽卡缓存文件是否存在。
    /// <para><b>返回值：</b><c>true</c> — 缓存文件存在；<c>false</c> — 不存在或检查过程异常。</para>
    /// </summary>
    private bool IsGachaCacheFileExists()
    {
        try
        {
            var installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            if (Directory.Exists(installPath))
            {
                var path = GachaLogClient.GetGachaCacheFilePath(CurrentGameBiz, installPath);
                return File.Exists(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check gacha cache file exists");
        }
        return false;
    }



    #endregion



    #region Import & Export



    /// <summary>
    /// 导出当前 UID 的抽卡记录到文件。
    /// <para><b>输入：</b><paramref name="format"/> — 导出格式（<c>"excel"</c> 或 <c>"json"</c>）。</para>
    /// <para><b>副作用：</b>弹出保存文件对话框让用户选择路径；导出完成后打开文件所在文件夹并选中。</para>
    /// </summary>
    [RelayCommand]
    private async Task ExportGachaLogAsync(string format)
    {
        try
        {
            if (SelectUid is null or 0)
            {
                return;
            }
            long uid = SelectUid.Value;
            var ext = format switch
            {
                "excel" => "xlsx",
                "json" => "json",
                _ => "json"
            };
            var suggestName = $"Starward_Export_{CurrentGameBiz.Game}_{uid}_{DateTime.Now:yyyyMMddHHmmss}.{ext}";
            var file = await FileDialogHelper.OpenSaveFileDialogAsync(this.XamlRoot, suggestName, (ext, $".{ext}"));
            if (file is not null)
            {
                await _gachaLogService.ExportGachaLogAsync(uid, file, format);
                var storageFile = await StorageFile.GetFileFromPathAsync(file);
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(storageFile);
                await Launcher.LaunchFolderAsync(await storageFile.GetParentAsync(), options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }




    /// <summary>
    /// 从 JSON 文件导入抽卡记录。
    /// <para><b>副作用：</b>弹出文件选择对话框；导入后刷新 UID 列表并切换到导入的 UID。</para>
    /// </summary>
    [RelayCommand]
    private async Task ImportGachaLogAsync()
    {
        try
        {
            var file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, ("Json", ".json"));
            if (File.Exists(file))
            {
                var uid = _gachaLogService.ImportGachaLog(file);
                if (uid == SelectUid)
                {
                    UpdateGachaTypeStats(uid);
                }
                else if (UidList.Contains(uid))
                {
                    SelectUid = uid;
                }
                else
                {
                    UidList.Add(uid);
                    SelectUid = uid;
                }
                if (uid != 0)
                {
                    // 按 ItemId 把导入记录的名称回写为当前软件语言（缺失则联网下载映射），完成后进度处理器会再次刷新。
                    _ = AppConfig.GetService<GachaItemNameService>().ApplyForGameAsync(CurrentGameBiz);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>打开 UIGF v4.0 导入窗口（支持批量导入多个游戏的抽卡记录）。</summary>
    [RelayCommand]
    private void OpenUIGF4Window()
    {
        new UIGF4GachaWindow().Activate();
    }



    /// <summary>
    /// 收到本地导入完成通知后刷新当前游戏的抽卡页面。
    /// 规则：仅处理属于当前游戏的 Uid；当前展示的 Uid 若在本次导入中则刷新它，否则切换到本次导入的第一位玩家。
    /// </summary>
    private void OnGachaLogImported(GachaLogImportedMessage message)
    {
        try
        {
            var uids = message.ImportedUids
                              .Where(x => x.Game == CurrentGameBiz.Game)
                              .Select(x => x.Uid)
                              .Distinct()
                              .ToList();
            if (uids.Count == 0)
            {
                return;
            }
            UidList ??= [];
            foreach (long uid in uids)
            {
                if (!UidList.Contains(uid))
                {
                    UidList.Add(uid);
                }
            }
            // 当前展示的 Uid 也在本次导入中则刷新它，否则切到导入的第一位
            long target = SelectUid is long current && current != 0 && uids.Contains(current)
                ? current
                : uids[0];
            if (SelectUid == target)
            {
                UpdateGachaTypeStats(target);
            }
            else
            {
                SelectUid = target;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "On gacha log imported");
        }
    }







    #endregion


}
