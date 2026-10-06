using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Features.Background;
using Starward.Features.Gacha.UIGF;
using Starward.Features.GameLauncher;
using Starward.Features.Screenshot;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI;


namespace Starward.Features.Gacha;

/// <summary>
/// 千星奇域颂愿记录页。界面与交互对齐 <see cref="GachaLogPage"/>：统计卡片复用 <see cref="GachaStatsCard"/>（横向排列、拖拽换位、列表 / 紧凑视图），
/// 并提供卡池筛选、分享图与删除记录；卡池筛选与卡片次序按 hk4eugc 单独保存，视图模式与抽卡记录页共用。
/// </summary>
public sealed partial class GenshinBeyondGachaPage : PageBase
{


    private readonly ILogger<GenshinBeyondGachaPage> _logger = AppConfig.GetLogger<GenshinBeyondGachaPage>();

    private readonly GenshinBeyondGachaService _gachaLogService = AppConfig.GetService<GenshinBeyondGachaService>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();


    /// <summary>千星奇域在按游戏保存的设置（上次 UID、卡池筛选、卡片次序）里使用的键。</summary>
    private const string SettingGameKey = "hk4eugc";


    /// <summary>卡片拖拽换位逻辑（按住统计区域拖动、悬停即换位、松手提交并持久化）。</summary>
    private readonly GachaStatsCardDragReorder _dragReorder;

    /// <summary>常驻卡片池：按卡池分组（1000 / 2000）复用卡片实例。</summary>
    private readonly Dictionary<int, FrameworkElement> _gachaCardPool = new();

    /// <summary>上次重建卡片所基于的统计数据引用。</summary>
    private List<GachaTypeStats>? _reconciledStatsSource;

    /// <summary>当前记录区视图模式（列表 / 紧凑图标网格）；与抽卡记录页共用全局设置。</summary>
    private GachaRecordViewMode _recordViewMode;


    public GenshinBeyondGachaPage()
    {
        // 先读出视图模式再给 Segmented 赋选中项：赋值触发的 SelectionChanged 因模式未变而被忽略，不会提前重建卡片。
        _recordViewMode = AppConfig.GachaRecordViewMode is GachaRecordViewMode.Compact ? GachaRecordViewMode.Compact : GachaRecordViewMode.List;
        InitializeComponent();
        _dragReorder = new GachaStatsCardDragReorder(ScrollViewer_GachaStats, Grid_GachaStats, StackPanel_GachaStats, SaveGachaCardOrder);
        Segmented_RecordViewMode.SelectedIndex = (int)_recordViewMode;
    }


    public ObservableCollection<long> UidList { get; set => SetProperty(ref field, value); }


    /// <summary>当前选中的 UID。切换时持久化并刷新统计。</summary>
    [ObservableProperty]
    public partial long? SelectUid { get; set; }
    partial void OnSelectUidChanged(long? value)
    {
        AppConfig.SetLastUidInGachaLogPage(SettingGameKey, value ?? 0);
        UpdateGachaTypeStats(value);
        ShareGachaImageCommand.NotifyCanExecuteChanged();
    }


    /// <summary>分享图生成进行中时为 true，用于禁用分享按钮。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShareGachaImageCommand))]
    public partial bool IsSharingGachaImage { get; set; }




    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        WeakReferenceMessenger.Default.Register<GachaLogImportedMessage>(this, (s, m) => OnGachaLogImported(m));
        Grid_GachaStats.PointerWheelChanged += Grid_GachaStats_PointerWheelChanged;
        Initialize();
        await EnsureGachaInfoAsync();
    }



    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Grid_GachaStats.PointerWheelChanged -= Grid_GachaStats_PointerWheelChanged;
        ClearGachaCards();
        gachaTypeStats = null;
        _reconciledStatsSource = null;
        GachaItemStats = null;
        GachaBanners = null!;
    }


    /// <summary>
    /// UIGF 等本地导入完成后刷新本页（仅处理 hk4eugc 归档）。
    /// </summary>
    private void OnGachaLogImported(GachaLogImportedMessage message)
    {
        try
        {
            var uids = message.ImportedUids
                              .Where(x => x.Game.Value == SettingGameKey && x.Uid > 0)
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
            _logger.LogError(ex, "Refresh after beyond gacha import");
        }
    }



    private void Initialize()
    {
        try
        {
            InitializeGachaBanners();
            SelectUid = null;
            UidList = new(_gachaLogService.GetUids());
            var lastUid = AppConfig.GetLastUidInGachaLogPage(SettingGameKey);
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
    /// 卡片区域滚轮：拖拽中交给 <see cref="_dragReorder"/> 横向滚动；否则吞掉，避免外层横向 ScrollViewer 把竖直滚轮转成横向滚动（同 <see cref="GachaLogPage"/>）。
    /// </summary>
    private void Grid_GachaStats_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_dragReorder.HandleWheel(e))
        {
            return;
        }
        e.Handled = true;
    }



    /// <summary>
    /// 确保物品信息（图标）可用且较新（表为空或距上次检查满一天时联网）；有变化则重新加载统计，让新图标立即显示。
    /// </summary>
    private async Task EnsureGachaInfoAsync()
    {
        try
        {
            if (await _gachaLogService.EnsureGachaInfoAsync())
            {
                UpdateGachaTypeStats(SelectUid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update wiki data hk4eugc");
        }
    }



    [RelayCommand]
    private void OpenItemStatsPane()
    {
        SplitView_Content.IsPaneOpen = true;
    }




    #region Gacha Stats



    /// <summary>卡池筛选列表（常驻颂愿 / 活动颂愿）。</summary>
    public List<GachaBanner> GachaBanners { get; set => SetProperty(ref field, value); }

    /// <summary>当前 UID 的物品统计（右侧面板），按稀有度、次数降序。</summary>
    public List<GachaLogItemEx>? GachaItemStats { get; set => SetProperty(ref field, value); }

    /// <summary>当前 UID 的各卡池统计。</summary>
    private List<GachaTypeStats>? gachaTypeStats;

    /// <summary>连续获取记录失败计数，用于触发「删除缓存文件夹」提示。</summary>
    private int errorCount = 0;


    /// <summary>
    /// 初始化卡池筛选：恢复保存的勾选；从未保存过则默认全选并保存。
    /// </summary>
    private void InitializeGachaBanners()
    {
        GachaBanners = _gachaLogService.QueryGachaTypes.Select(x => new GachaBanner(x)).ToList();
        // null 表示从未保存；"" 表示用户清空过选择
        string? banner = AppConfig.GetDisplayGachaBanners(SettingGameKey);
        if (banner is null)
        {
            foreach (GachaBanner b in GachaBanners)
            {
                b.IsSelected = true;
            }
            AppConfig.SetDisplayGachaBanners(SettingGameKey, string.Join(',', GachaBanners.Select(x => x.Value)));
        }
        else
        {
            var saved = banner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Select(x => int.TryParse(x, out int v) ? v : int.MinValue)
                              .ToHashSet();
            foreach (GachaBanner b in GachaBanners)
            {
                b.IsSelected = saved.Contains(b.Value);
            }
        }
        UpdateGachaBannerFilterIndicator();
    }


    private void GachaBannerCheckBox_Click(object sender, RoutedEventArgs e)
    {
        ApplyGachaBannerFilter();
    }


    /// <summary>提交卡池筛选：持久化所选卡池并刷新显示。</summary>
    private void ApplyGachaBannerFilter()
    {
        try
        {
            if (GachaBanners is null)
            {
                return;
            }
            AppConfig.SetDisplayGachaBanners(SettingGameKey, string.Join(',', GachaBanners.Where(x => x.IsSelected).Select(x => x.Value)));
            UpdateDisplayGachaTypeStats(playEntranceAnimation: false);
            UpdateGachaBannerFilterIndicator();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply gacha banner filter");
        }
    }


    private void GachaBannerSelectAll_Click(object sender, RoutedEventArgs e)
    {
        SetAllGachaBanners(_ => true);
    }


    private void GachaBannerClear_Click(object sender, RoutedEventArgs e)
    {
        SetAllGachaBanners(_ => false);
    }


    private void GachaBannerInvert_Click(object sender, RoutedEventArgs e)
    {
        SetAllGachaBanners(b => !b.IsSelected);
    }


    /// <summary>
    /// 批量改写卡池勾选后提交筛选（全选 / 清除 / 反选）。
    /// </summary>
    /// <param name="selector">返回每个卡池的新勾选状态。</param>
    private void SetAllGachaBanners(Func<GachaBanner, bool> selector)
    {
        if (GachaBanners is null)
        {
            return;
        }
        foreach (GachaBanner b in GachaBanners)
        {
            b.IsSelected = selector(b);
        }
        ApplyGachaBannerFilter();
    }


    /// <summary>筛选生效（非全选）时把筛选按钮图标点亮为强调色。</summary>
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
    /// 按 UID 重新统计并重建卡片；uid 为空或无记录时清空并显示占位表情。
    /// </summary>
    /// <param name="uid">玩家 UID。</param>
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
                StackPanel_Emoji.Visibility = gachaTypeStats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            ShareGachaImageCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateGachaTypeStats");
        }
    }


    /// <summary>
    /// 当前应展示的卡池统计：先按勾选筛选，再按拖拽保存的次序排序（与卡片、分享图一致）。
    /// </summary>
    /// <returns>已选卡池的统计；无数据或未选卡池时为空列表。</returns>
    private List<GachaTypeStats> GetDisplayedGachaTypeStats()
    {
        if (gachaTypeStats is null || GachaBanners is null)
        {
            return [];
        }
        var selected = GachaBanners.Where(x => x.IsSelected).Select(x => x.Value).ToHashSet();
        return ApplySavedCardOrder(gachaTypeStats.Where(x => selected.Contains(x.GachaType)).ToList());
    }


    /// <summary>
    /// 按当前筛选与次序刷新卡片。
    /// </summary>
    /// <param name="playEntranceAnimation">是否播放入场动画（数据刷新时为 true，筛选切换时为 false）。</param>
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
    /// 把当前所选卡池渲染为分享图并用内置查看器打开；记录区按当前视图绘制。视频背景先取当前帧快照。
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

            string? backgroundFile = BackgroundService.GetCachedBackgroundFile(CurrentGameId);
            if (backgroundFile is not null && BackgroundService.FileIsSupportedVideo(backgroundFile))
            {
                backgroundFile = AppBackground.Current is not null
                    ? await AppBackground.Current.CaptureCurrentBackgroundSnapshotAsync()
                    : null;
            }

            // 强调色须在 UI 线程读取；Win2D 离屏渲染放到后台线程。
            Color accentColor = Application.Current.Resources["AccentFillColorDefaultBrush"] is SolidColorBrush brush
                ? brush.Color
                : Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
            GachaRecordViewMode viewMode = _recordViewMode;
            string file = await Task.Run(async () =>
                await GachaShareImageRenderer.RenderAndSaveAsync(stats, CurrentGameBiz, backgroundFile, uid, accentColor, viewMode));
            await new ImageViewWindow2().ShowWindowAsync(this.XamlRoot.ContentIslandEnvironment.AppWindowId, file, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Share beyond gacha image");
            InAppToast.MainWindow?.Error(ex);
        }
        finally
        {
            IsSharingGachaImage = false;
        }
    }


    private bool CanShareGachaImage()
        => !IsSharingGachaImage
           && SelectUid is > 0
           && gachaTypeStats is { Count: > 0 }
           && GachaBanners?.Any(x => x.IsSelected) == true;


    /// <summary>把可见卡片的卡池次序持久化（拖拽换位提交后回调）。</summary>
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
            AppConfig.SetGachaCardOrder(SettingGameKey, string.Join(',', order));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save gacha card order");
        }
    }


    /// <summary>
    /// 按保存的卡片次序稳定排序：已记录的在前，未记录的维持原有相对次序。
    /// </summary>
    /// <param name="stats">已筛选的卡池统计。</param>
    /// <returns>排序后的列表。</returns>
    private List<GachaTypeStats> ApplySavedCardOrder(List<GachaTypeStats> stats)
    {
        try
        {
            string? saved = AppConfig.GetGachaCardOrder(SettingGameKey);
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


    /// <summary>统计卡片固定宽度，与抽卡记录页一致。</summary>
    private const double GachaStatsCardWidth = 262;


    /// <summary>
    /// 按最终展示次序对齐卡片池与面板：复用已有卡片，只切 Visibility、Move 重排；统计数据引用变化（切 UID、刷新、导入）时整体重建。
    /// </summary>
    /// <param name="ordered">要显示的卡池统计（已排序）。</param>
    /// <param name="playEntranceAnimation">是否播放入场动画。</param>
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
            FrameworkElement card = GetOrCreateGachaCard(ordered[i]);
            card.Visibility = Visibility.Visible;
            int current = panel.Children.IndexOf(card);
            if (current < 0)
            {
                panel.Children.Insert(Math.Min(i, panel.Children.Count), card);
            }
            else if (current != i)
            {
                panel.Children.Move((uint)current, (uint)i);
            }
        }

        var visible = ordered.Select(x => x.GachaType).ToHashSet();
        foreach (KeyValuePair<int, FrameworkElement> kv in _gachaCardPool)
        {
            if (!visible.Contains(kv.Key))
            {
                kv.Value.Visibility = Visibility.Collapsed;
            }
        }

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


    /// <summary>取池中卡片；没有则新建 <see cref="GachaStatsCard"/>，赋数据/宽度/视图、挂拖拽手柄后入池（尚未加入面板）。</summary>
    private FrameworkElement GetOrCreateGachaCard(GachaTypeStats stats)
    {
        if (_gachaCardPool.TryGetValue(stats.GachaType, out FrameworkElement? existing))
        {
            return existing;
        }
        var card = new GachaStatsCard
        {
            Width = GachaStatsCardWidth,
            DataContext = stats,              // 供拖拽手柄识别其卡池数据
            RecordViewMode = _recordViewMode, // 须在加入可视化树前赋值：紧凑模式要在记录列表生成容器前换好面板与模板
            WarpTypeStats = stats,            // 须在加入可视化树前赋值，卡片内 OneTime x:Bind 与稀有度配色才能取到
        };
        _dragReorder.Attach(card.DragHandle);
        _gachaCardPool[stats.GachaType] = card;
        return card;
    }


    /// <summary>
    /// 标题栏视图切换：持久化新视图并重建卡片。模式未变（含初始化回写）直接忽略；Ctrl+点击取消选中时恢复当前模式。
    /// </summary>
    private void Segmented_RecordViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = Segmented_RecordViewMode.SelectedIndex;
        if (index < 0)
        {
            // 延后恢复，避免在选择变更回调里重入修改选中项。
            DispatcherQueue.TryEnqueue(() => Segmented_RecordViewMode.SelectedIndex = (int)_recordViewMode);
            return;
        }
        GachaRecordViewMode mode = index == (int)GachaRecordViewMode.Compact ? GachaRecordViewMode.Compact : GachaRecordViewMode.List;
        if (mode == _recordViewMode)
        {
            return;
        }
        _recordViewMode = mode;
        AppConfig.GachaRecordViewMode = mode;
        try
        {
            if (gachaTypeStats is not null)
            {
                // 不在运行中替换已有卡片的 ItemsPanel（跨 DPI 布局崩溃的历史），整卡重建
                _reconciledStatsSource = null;
                UpdateDisplayGachaTypeStats(playEntranceAnimation: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rebuild gacha cards for view mode {mode}", _recordViewMode);
        }
    }


    /// <summary>清空卡片池与面板子元素：解绑拖拽手柄并移除子元素，触发卡片 Unloaded 释放其内部绑定。</summary>
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
    /// 更新千星奇域颂愿记录；默认从 webCaches 多候选校验后取有效 URL。
    /// </summary>
    /// <param name="param"><c>"cache"</c> 用已保存 URL；<c>"all"</c> 全量；其余从网页缓存获取。</param>
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
                    validatingBar = new InfoBar
                    {
                        Severity = InfoBarSeverity.Informational,
                        Message = Lang.GachaLogPage_ValidatingGachaUrl,
                        Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
                        IsOpen = true,
                    };
                    InAppToast.MainWindow?.Show(validatingBar);
                    url = await _gachaLogService.GetValidatedGachaLogUrlFromWebCacheAsync(CurrentGameBiz, path);
                }
                catch (GachaApiException ex) when (ex.IsAuthkeyExpired)
                {
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
                        ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
                    }
                    return;
                }
                finally
                {
                    // 无论成功、无候选还是异常，都关掉校验条（含外层 catch 前漏关）
                    if (validatingBar is not null)
                    {
                        validatingBar.IsOpen = false;
                    }
                }
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



    /// <summary>
    /// 执行千星奇域抽卡拉取（进度 InfoBar + 取消）。失败时关闭进度条，避免常驻。
    /// </summary>
    /// <param name="url">含 authkey 的抽卡 URL。</param>
    /// <param name="all">是否全量拉取。</param>
    private async Task UpdateGachaLogInternalAsync(string url, bool all = false)
    {
        InfoBar? progressInfoBar = null;
        bool keepProgressInfoBar = false;
        try
        {
            // 有效 authkey 时按 UID 缓存 URL；近 6 个月无记录则返回 0 且不落库
            await _gachaLogService.GetUidFromGachaLogUrl(url);
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
            progressInfoBar = infoBar;
            InAppToast.MainWindow?.Show(infoBar);
            var progress = new Progress<string>((str) => infoBar.Message = str);
            var newUid = await _gachaLogService.GetGachaLogAsync(url, all, System.Globalization.CultureInfo.CurrentUICulture.Name, progress, cancelSource.Token);
            infoBar.Title = newUid > 0 ? $"Uid {newUid}" : null;
            infoBar.Severity = InfoBarSeverity.Success;
            infoBar.ActionButton = null;
            keepProgressInfoBar = true;
            InAppToast.DismissAfter(infoBar);
            ApplyFetchedGachaUid(newUid);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Get gacha log canceled");
            if (progressInfoBar is not null)
            {
                progressInfoBar.Message = Lang.GachaLogPage_OperationCanceled;
                progressInfoBar.ActionButton = null;
            }
            keepProgressInfoBar = true;
            InAppToast.DismissAfter(progressInfoBar);
        }
        catch (GachaApiException ex)
        {
            _logger.LogWarning("Request mihoyo api error: {error}", ex.Message);
            if (ex.IsAuthkeyExpired)
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
                    ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
                }
            }
            else
            {
                ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
            }
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Request beyond gacha log HTTP error");
            ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
        }
        finally
        {
            if (!keepProgressInfoBar && progressInfoBar is not null)
            {
                try
                {
                    progressInfoBar.ActionButton = null;
                    progressInfoBar.IsOpen = false;
                }
                catch
                {
                    // UI 已卸载时忽略
                }
            }
        }
    }


    /// <summary>
    /// 将拉取到的 UID 加入列表并选中。uid ≤ 0 表示近 6 个月无记录，不写入列表。
    /// </summary>
    /// <param name="uid">本次拉取得到的 UID。</param>
    private void ApplyFetchedGachaUid(long uid)
    {
        if (uid <= 0)
        {
            return;
        }
        UidList ??= [];
        if (SelectUid == uid)
        {
            UpdateGachaTypeStats(uid);
            return;
        }
        if (!UidList.Contains(uid))
        {
            UidList.Add(uid);
        }
        SelectUid = uid;
    }



    /// <summary>
    /// 显示千星奇域祈愿记录的 API 反馈。链接失效时仅展示错误信息，不再弹出「输入新链接」恢复按钮（可通过菜单「通过 URL 更新」手动处理）。
    /// </summary>
    /// <param name="feedback">已按祈愿记录场景分类的错误反馈。</param>
    private void ShowGachaFeedback(MiHoYoApiErrorFeedback feedback)
    {
        MiHoYoApiErrorFeedbackFactory.Show(feedback);
    }



    /// <summary>
    /// 通过 URL 更新千星奇域记录：弹出对话框，预填当前 UID 已保存的 URL（若有），可直接确认或粘贴新 URL 后拉取。
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



    #endregion




    #region Gacha Setting Panel



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
                FontIcon_CopyUrl.Glyph = ""; // accept
                await Task.Delay(1000);
                FontIcon_CopyUrl.Glyph = "";  // copy
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy url");
        }
    }



    /// <summary>
    /// 删除当前 UID 的全部千星奇域记录（确认后执行），然后重新初始化页面。
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
    /// 按时间段删除千星奇域记录（<see cref="DeleteGachaLogDialog"/> 以 hk4eugc 打开），删除后刷新统计。
    /// </summary>
    [RelayCommand]
    private async Task DeleteUidByTimeAsync()
    {
        try
        {
            var dialog = new DeleteGachaLogDialog
            {
                CurrentGameBiz = SettingGameKey,
                DefaultUid = this.SelectUid,
                XamlRoot = this.XamlRoot,
            };
            await dialog.ShowAsync();
            if (dialog.Deleted)
            {
                UpdateGachaTypeStats(dialog.SelectUid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete uid by time");
        }
    }



    /// <summary>
    /// 一键删除游戏 webCaches（确认后真正删除；游戏运行中拒绝）。
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

            var webCachesPath = GenshinBeyondGachaClient.GetWebCachesFolderPath(CurrentGameBiz, installPath);
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
    /// 在资源管理器中打开并选中 webCaches。
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
    /// 检查千星奇域抽卡 data_2 缓存是否存在。
    /// </summary>
    /// <returns>存在返回 true。</returns>
    private bool IsGachaCacheFileExists()
    {
        try
        {
            var installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            if (Directory.Exists(installPath))
            {
                var path = GenshinBeyondGachaClient.GetGachaCacheFilePath(CurrentGameBiz, installPath);
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
    /// 打开 UIGF 导入导出窗口。千星奇域数据落在 <c>hk4e_ugc</c>，导出默认 v4.2；
    /// 导入不区分子版本，选文件后按内容自动识别（含 hk4e_ugc）。
    /// </summary>
    /// <param name="parameter">形如 <c>export|v4.2</c> / <c>import</c>。</param>
    [RelayCommand]
    private void OpenUIGF4Window(string? parameter)
    {
        UIGF4Version version = UIGF4Version.V42;
        bool openImport = false;
        if (!string.IsNullOrWhiteSpace(parameter))
        {
            string[] parts = parameter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && parts[0].Equals("import", StringComparison.OrdinalIgnoreCase))
            {
                openImport = true;
            }
            string verText = parts.Length >= 2 ? parts[1] : parts[0];
            if (UIGF4VersionExtensions.TryParse(verText) is UIGF4Version parsed)
            {
                version = parsed;
            }
        }
        new UIGF4GachaWindow(version, openImport).Activate();
    }



    #endregion


}
