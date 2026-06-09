using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
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
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord;
using Starward.Features.ViewHost;
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


namespace Starward.Features.Gacha;

public sealed partial class GachaLogPage : PageBase
{

    private readonly ILogger<GachaLogPage> _logger = AppConfig.GetLogger<GachaLogPage>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    private GachaLogService _gachaLogService;


    /// <summary>卡片拖拽换位逻辑（按住统计区域拖动、悬停即换位、松手提交并持久化）。</summary>
    private readonly GachaStatsCardDragReorder _dragReorder;

    /// <summary>常驻抽卡卡片池：按卡池类型(GachaType)复用卡片实例。</summary>
    private readonly Dictionary<int, FrameworkElement> _gachaCardPool = new();

    /// <summary>上次重建卡片所基于的统计数据引用</summary>
    private List<GachaTypeStats>? _reconciledStatsSource;



    public GachaLogPage()
    {
        this.InitializeComponent();
        _dragReorder = new GachaStatsCardDragReorder(ScrollViewer_GachaStats, Grid_GachaStats, StackPanel_GachaStats, SaveGachaCardOrder);
    }



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


    [ObservableProperty]
    public partial long? SelectUid { get; set; }
    partial void OnSelectUidChanged(long? value)
    {
        AppConfig.SetLastUidInGachaLogPage(CurrentGameBiz.Game, value ?? 0);
        UpdateGachaTypeStats(value);
    }




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
    /// <para>4. 异步调用 <see cref="UpdateWikiDataAsync"/> 更新当前语言的卡池/物品维基数据（不阻塞 UI）。</para>
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
        Grid_GachaStats.PointerWheelChanged += Grid_GachaStats_PointerWheelChanged;
        Initialize();
        await UpdateWikiDataAsync();
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



    private void Grid_GachaStats_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 拖拽中优先用滚轮横向滚动（以便拖到视口外的第 5、6 个卡池）。
        if (_dragReorder.HandleWheel(e))
        {
            return;
        }
        var properties = e.GetCurrentPoint(Grid_GachaStats).Properties;
        if (properties.IsHorizontalMouseWheel || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            var delta = properties.MouseWheelDelta;
            ScrollViewer_GachaStats.ChangeView(ScrollViewer_GachaStats.HorizontalOffset - delta, null, null);
            e.Handled = true;
        }
    }



    /// <summary>
    /// 更新抽卡维基数据（物品/角色/邦布名称本地化信息）。
    /// <para>在 <see cref="OnLoaded"/> 末尾被 await 调用（不阻塞页面其他初始化），确保 UI 能以正确语言显示抽卡记录。</para>
    /// <para>无参数。</para>
    /// <para>输入依赖：</para>
    /// <para>1. <see cref="GachaLanguage"/> 属性（用户设置的抽卡数据语言），若为空或空白则回退为 <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.Name；</para>
    /// <para>2. <see cref="CurrentGameBiz"/>（决定使用哪个具体 gacha service 以及写入哪张本地表）；</para>
    /// <para>3. <see cref="_gachaLogService"/>（在 OnNavigatedTo 中根据游戏类型注入为 GenshinGachaService / StarRailGachaService / ZZZGachaService 之一）。</para>
    /// <para>输出/副作用：</para>
    /// <para>通过服务层的 <see cref="GachaLogService.UpdateGachaInfoAsync(GameBiz, string, CancellationToken)"/> 从 miHoYo 服务器获取指定语言的抽卡信息（AllAvatar、AllWeapon 等），执行 INSERT OR REPLACE 写入本地 SQLite 表（GenshinGachaInfo / StarRailGachaInfo / ZZZGachaInfo）；</para>
    /// <para>更新后，<see cref="GachaLogItemEx"/>、统计卡片、物品统计列表等会使用本地化的最新名称；</para>
    /// <para>服务内部还会执行 UpdateGachaItemId 等后续处理。</para>
    /// <para>任何异常仅记录日志（_logger），不抛出，不影响页面加载和主要功能。</para>
    /// </summary>
    private async Task UpdateWikiDataAsync()
    {
        try
        {
            string lang = string.IsNullOrWhiteSpace(GachaLanguage) ? System.Globalization.CultureInfo.CurrentUICulture.Name : GachaLanguage;
            await _gachaLogService.UpdateGachaInfoAsync(CurrentGameBiz, lang);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update wiki data {gameBiz}", CurrentGameBiz);
        }
    }




    [RelayCommand]
    private void OpenItemStatsPane()
    {
        SplitView_Content.IsPaneOpen = true;
    }




    #region Gacha Stats



    public bool EnableGenshinGachaItemStats { get; set => SetProperty(ref field, value); }

    public bool EnableStarRailGachaItemStats { get; set => SetProperty(ref field, value); }

    public bool EnableZZZGachaItemStats { get; set => SetProperty(ref field, value); }

    public List<GachaBanner> GachaBanners { get; set => SetProperty(ref field, value); }


    public List<GachaLogItemEx>? GachaItemStats { get; set => SetProperty(ref field, value); }


    private List<GachaTypeStats>? gachaTypeStats;


    private int errorCount = 0;


    /// <summary>
    /// 初始化卡池筛选列表（GachaBanners）。
    /// <para>从 _gachaLogService.QueryGachaTypes 获取当前游戏所有卡池类型，包装成 GachaBanner；</para>
    /// <para>然后从 AppConfig 读取用户上次保存的勾选状态（逗号分隔的 Value 列表）并恢复 IsSelected；</para>
    /// <para>针对版本更新新增的卡池（星铁联动、ZZZ 重映/回响），在首次遇到时自动勾选并持久化标记，避免用户错过新卡池。</para>
    /// </summary>
    private void InitializeGachaBanners()
    {
        GachaBanners = _gachaLogService.QueryGachaTypes.Select(x => new GachaBanner(x)).ToList();
        string? banner = AppConfig.GetDisplayGachaBanners(CurrentGameBiz.Game);
        bool anySelected = false;
        if (!string.IsNullOrWhiteSpace(banner))
        {
            var saved = banner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Select(x => int.TryParse(x, out int v) ? v : int.MinValue)
                              .Where(x => x != int.MinValue)
                              .ToHashSet();
            foreach (GachaBanner b in GachaBanners)
            {
                b.IsSelected = saved.Contains(b.Value);
                anySelected |= b.IsSelected;
            }
        }
        // 版本更新后默认勾选新增卡池。
        if (CurrentGameBiz.Game is GameBiz.hkrpg && !AppConfig.GetValue(false, "SavedStarRailBannersAfterCollaborationStarting"))
        {
            foreach (GachaBanner b in GachaBanners.Where(x => x.Value is 21 or 22))
            {
                b.IsSelected = true;
            }
        }
        if (CurrentGameBiz.Game is GameBiz.nap && !AppConfig.GetValue(false, "SavedZZZBannersSinceVersion2.5"))
        {
            foreach (GachaBanner b in GachaBanners.Where(x => x.Value is 102 or 103))
            {
                b.IsSelected = true;
            }
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
        // 勾选=显示；按卡池固有次序取所选项，未勾选任何卡池即不显示。
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
        // 应用用户拖拽保存的卡片次序（按游戏持久化），刷新数据后仍保持卡片之间的相对位置；交给卡片池对齐。
        ReconcileGachaCards(ApplySavedCardOrder(stats), playEntranceAnimation);
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
                url = _gachaLogService.GetGachaLogUrlFromWebCache(CurrentGameBiz, path);
                if (string.IsNullOrWhiteSpace(url))
                {
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
            var newUid = await _gachaLogService.GetGachaLogAsync(url, all, GachaLanguage, progress, cancelSource.Token);
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
            var uid = await zzzGachaService.GetGachaLogByGameRecordAsync(role, param is "all", GachaLanguage, progress, cancelSource.Token);
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



    [RelayCommand]
    private async Task InputUrlAsync()
    {
        try
        {
            var textbox = new TextBox();
            var dialog = new ContentDialog
            {
                // 输入 URL
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


    public string? GachaLanguage
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.GachaLanguage = value;
            }
        }
    } = AppConfig.GachaLanguage;



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



    [RelayCommand]
    private async Task ChangeGachaItemNameAsync()
    {
        try
        {
            string lang = string.IsNullOrWhiteSpace(GachaLanguage) ? System.Globalization.CultureInfo.CurrentUICulture.Name : GachaLanguage;
            (lang, int count) = await _gachaLogService.ChangeGachaItemNameAsync(CurrentGameBiz, lang);
            InAppToast.MainWindow?.Success(null, string.Format(Lang.GachaLogPage_0GachaItemsHaveBeenChangedToLanguage1, count, lang), 5000);
            UpdateGachaTypeStats(SelectUid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change gacha item name");
        }
    }



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



    [RelayCommand]
    private async Task DeleteGachaCacheFolderAsync()
    {
        try
        {
            var installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            if (Directory.Exists(installPath))
            {
                var webCachesPath = GachaLogClient.GetWebCachesFolderPath(CurrentGameBiz, installPath);
                if (Directory.Exists(webCachesPath))
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(webCachesPath);
                    var option = new FolderLauncherOptions();
                    option.ItemsToSelect.Add(folder);
                    await Launcher.LaunchFolderAsync(await folder.GetParentAsync(), option);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete gacha cache file");
        }
    }



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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }



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
