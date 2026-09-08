using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Background;

/// <summary>
/// 好感 / 满影画画廊；点击封面会写入当前游戏的自定义背景。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class FavorWallpaperPanel : UserControl
{

    private readonly ILogger<FavorWallpaperPanel> _logger = AppConfig.GetLogger<FavorWallpaperPanel>();

    private readonly FavorWallpaperService _service = AppConfig.GetService<FavorWallpaperService>();

    private CancellationTokenSource? _loadCts;


    public FavorWallpaperPanel()
    {
        this.InitializeComponent();
        this.Unloaded += FavorWallpaperPanel_Unloaded;
    }


    public GameId? CurrentGameId { get; set; }

    public GameBiz CurrentGameBiz { get; set; }


    /// <summary>为 true 时展示满影画静态壁纸，否则为好感动态壁纸。</summary>
    public bool IsMindscapeMode { get; set; }


    /// <summary>
    /// 绑定给 GridView 的卡片集合。**整体替换**，不要 Clear + 逐个 Add：
    /// 逐个 Add 会给 GridView 发 N 次 CollectionChanged，每次都触发一轮测量/排布。
    /// 首屏之外的部分由 <see cref="FillRemainingItems"/> 分帧补齐。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FavorWallpaperView> items = [];


    /// <summary>首屏同步放入的卡片数：约等于视口能显示的行数，多余的留给分帧填充。</summary>
    private const int FirstFillCount = 12;


    /// <summary>每一帧补充的卡片数。</summary>
    private const int FillChunkSize = 12;


    /// <summary>填充代次；每次重新绑定就 +1，让在飞的分帧填充自行作废。</summary>
    private int _fillGeneration;


    private List<FavorWallpaperView> _favorViews = [];

    private List<FavorWallpaperView> _mindscapeViews = [];

    private bool _favorLoaded;

    private bool _mindscapeLoaded;


    [ObservableProperty]
    private bool isLoading;


    [ObservableProperty]
    private string? statusText;


    [ObservableProperty]
    private string? errorText;


    /// <summary>
    /// 加载当前模式列表（已有数据时只切换并刷新使用/下载状态）。
    /// </summary>
    public async Task EnsureLoadedAsync(bool forceRefresh = false)
    {
        bool loaded = IsMindscapeMode ? _mindscapeLoaded : _favorLoaded;
        if (!forceRefresh && loaded)
        {
            _loadCts?.Cancel();
            IsLoading = false;
            StatusText = null;
            ErrorText = null;
            BindCurrentModeItems();
            RefreshInUseState();
            return;
        }
        await LoadAsync(forceRefresh);
    }


    private async Task LoadAsync(bool forceRefresh)
    {
        bool mindscape = IsMindscapeMode;
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        CancellationToken token = cts.Token;
        ErrorText = null;
        StatusText = null;

        // 1) 先贴本地缓存立即可见；只有从未缓存过（首次）时才转圈。
        List<FavorWallpaperView> cachedViews = forceRefresh
            ? []
            : BuildViews(_service.GetCachedWallpapers(mindscape));
        bool showedCache = cachedViews.Count > 0;
        if (showedCache)
        {
            if (mindscape)
            {
                _mindscapeViews = cachedViews;
            }
            else
            {
                _favorViews = cachedViews;
            }
            IsLoading = false;
            if (IsMindscapeMode == mindscape)
            {
                BindCurrentModeItems();
            }
        }
        else
        {
            IsLoading = true;
            _fillGeneration++;
            Items = [];
        }

        // 2) 后台校验：数量一致则静默（仅就地补封面），对不上才整页强刷。
        try
        {
            var progress = new Progress<FavorWallpaperLoadProgress>(p =>
            {
                if (token.IsCancellationRequested || IsMindscapeMode != mindscape)
                {
                    return;
                }
                // 命中缓存（数量一致）或首屏仍在转圈时不显示标题提示；仅回源补词条时在标题右侧提示。
                StatusText = p.FromCache || !showedCache
                    ? null
                    : string.Format(Lang.FavorWallpaper_LoadingProgress, p.Done, p.Total);
            });
            IReadOnlyList<FavorWallpaperRecord> records = mindscape
                ? await _service.GetMindscapeWallpapersAsync(forceRefresh, progress, token)
                : await _service.GetWallpapersAsync(forceRefresh, progress, token);
            token.ThrowIfCancellationRequested();

            List<FavorWallpaperView> shown = mindscape ? _mindscapeViews : _favorViews;
            if (showedCache && !forceRefresh && SameEntries(shown, records))
            {
                // 条目未变：密友同行封面若被重新匹配则就地替换，不整页重绑。
                PatchCovers(shown, records);
                RefreshInUseState();
            }
            else
            {
                List<FavorWallpaperView> views = BuildViews(records);
                if (mindscape)
                {
                    _mindscapeViews = views;
                }
                else
                {
                    _favorViews = views;
                }
                if (IsMindscapeMode == mindscape)
                {
                    BindCurrentModeItems();
                }
            }

            if (mindscape)
            {
                _mindscapeLoaded = true;
            }
            else
            {
                _favorLoaded = true;
            }
            StatusText = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load wallpapers failed (mindscape={Mindscape})", mindscape);
            if (IsMindscapeMode == mindscape)
            {
                // 已有缓存呈现时后台失败保持静默，不打断浏览；仅首次无内容可展示才报错。
                if (!showedCache)
                {
                    ErrorText = Lang.FavorWallpaper_LoadFailed;
                }
                StatusText = null;
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }


    /// <summary>
    /// 由记录构造卡片视图，注入下载 / 删除 / 使用回调与当前的已下载 / 使用中状态。
    /// </summary>
    private List<FavorWallpaperView> BuildViews(IReadOnlyList<FavorWallpaperRecord> records)
    {
        string? currentBg = AppConfig.GetCustomBg(CurrentGameBiz);
        bool enabled = AppConfig.GetEnableCustomBg(CurrentGameBiz);
        var views = new List<FavorWallpaperView>(records.Count);
        foreach (FavorWallpaperRecord record in records)
        {
            string fileName = FavorWallpaperService.GetCacheFileName(record);
            views.Add(new FavorWallpaperView
            {
                Record = record,
                IsDownloaded = FavorWallpaperService.IsCached(record),
                IsInUse = enabled && string.Equals(currentBg, fileName, StringComparison.OrdinalIgnoreCase),
                DownloadAction = DownloadAsync,
                DeleteAction = DeleteAsync,
                UseAction = UseAsBackgroundAsync,
            });
        }
        return views;
    }


    /// <summary>
    /// 按 ContentId 序列判断展示中的列表与最新结果是否为同一批条目（数量与顺序均一致）。
    /// </summary>
    private static bool SameEntries(List<FavorWallpaperView> views, IReadOnlyList<FavorWallpaperRecord> records)
    {
        if (views.Count != records.Count)
        {
            return false;
        }
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].ContentId != records[i].ContentId)
            {
                return false;
            }
        }
        return true;
    }


    /// <summary>
    /// 条目未变时，仅把被重新匹配过的封面 / 图标就地写回并通知刷新，避免整页重绑与闪动。
    /// </summary>
    private static void PatchCovers(List<FavorWallpaperView> views, IReadOnlyList<FavorWallpaperRecord> records)
    {
        var byId = new Dictionary<int, FavorWallpaperRecord>(records.Count);
        foreach (FavorWallpaperRecord record in records)
        {
            byId[record.ContentId] = record;
        }
        foreach (FavorWallpaperView view in views)
        {
            if (!byId.TryGetValue(view.ContentId, out FavorWallpaperRecord? record))
            {
                continue;
            }
            bool changed = false;
            if (!string.Equals(view.Record.CoverUrl, record.CoverUrl, StringComparison.Ordinal))
            {
                view.Record.CoverUrl = record.CoverUrl;
                changed = true;
            }
            if (!string.Equals(view.Record.IconUrl, record.IconUrl, StringComparison.Ordinal))
            {
                view.Record.IconUrl = record.IconUrl;
                changed = true;
            }
            if (changed)
            {
                view.NotifyCoverChanged();
            }
        }
    }


    /// <summary>
    /// 下载到背景缓存目录，完成后封面显示对勾。
    /// </summary>
    public async Task DownloadAsync(FavorWallpaperView view)
    {
        if (view.IsDownloading)
        {
            return;
        }
        try
        {
            view.IsDownloading = true;
            view.DownloadProgress = 0;
            var progress = new Progress<double>(p => view.DownloadProgress = p);
            await _service.DownloadToBgFolderAsync(view.Record, progress);
            view.IsDownloaded = true;
            InAppToast.MainWindow?.Success(Lang.FavorWallpaper_DownloadCompleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download favor wallpaper {Id} failed", view.ContentId);
            InAppToast.MainWindow?.Error(Lang.FavorWallpaper_DownloadFailed);
        }
        finally
        {
            view.IsDownloading = false;
        }
    }


    /// <summary>
    /// 删除本地缓存；若正作为当前背景则先取消自定义背景。
    /// </summary>
    public async Task DeleteAsync(FavorWallpaperView view)
    {
        if (view.IsDownloading)
        {
            return;
        }
        try
        {
            string fileName = FavorWallpaperService.GetCacheFileName(view.Record);
            string? currentBg = AppConfig.GetCustomBg(CurrentGameBiz);
            bool inUse = view.IsInUse || string.Equals(currentBg, fileName, StringComparison.OrdinalIgnoreCase);
            if (inUse)
            {
                AppConfig.SetCustomBg(CurrentGameBiz, null);
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
                await Task.Delay(200);
            }
            await _service.DeleteLocalCacheAsync(view.Record);
            view.IsDownloaded = FavorWallpaperService.IsCached(view.Record);
            RefreshInUseState();
            InAppToast.MainWindow?.Success(Lang.FavorWallpaper_Deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete favor wallpaper {Id} failed", view.ContentId);
            InAppToast.MainWindow?.Error(Lang.FavorWallpaper_DeleteFailed);
        }
    }


    /// <summary>
    /// 点击封面：下载（如需要）并设为当前自定义背景。
    /// </summary>
    public async Task UseAsBackgroundAsync(FavorWallpaperView view)
    {
        if (view.IsDownloading)
        {
            return;
        }
        try
        {
            view.IsDownloading = true;
            view.DownloadProgress = 0;
            var progress = new Progress<double>(p => view.DownloadProgress = p);
            await _service.SetAsCustomBackgroundAsync(CurrentGameBiz, view.Record, progress);
            view.IsDownloaded = true;
            RefreshInUseState();
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
            InAppToast.MainWindow?.Success(Lang.FavorWallpaper_SetSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Set favor wallpaper {Id} as background failed", view.ContentId);
            InAppToast.MainWindow?.Error(Lang.FavorWallpaper_DownloadFailed);
        }
        finally
        {
            view.IsDownloading = false;
        }
    }


    /// <summary>
    /// 绑定当前模式的卡片。首屏只放 <see cref="FirstFillCount"/> 张，其余分帧补齐。
    /// 实测每张卡 realize 约 6ms（构造 2.1 + x:Bind 更新 3.9），一次性塞五十多张会让
    /// GridView 在同一帧 realize 近 30 张，也就是打开对话框时那一下 300ms 的阻塞；
    /// 拆成几帧后总时间不变，但不再有单帧长卡顿。
    /// </summary>
    private void BindCurrentModeItems()
    {
        List<FavorWallpaperView> source = IsMindscapeMode ? _mindscapeViews : _favorViews;
        int generation = ++_fillGeneration;
        var first = new ObservableCollection<FavorWallpaperView>();
        int count = Math.Min(FirstFillCount, source.Count);
        for (int i = 0; i < count; i++)
        {
            first.Add(source[i]);
        }
        Items = first;
        if (count < source.Count)
        {
            FillRemainingItems(generation, source, count);
        }
    }


    /// <summary>
    /// 低优先级分帧补齐剩余卡片；期间若又切了模式或重新加载（代次变化）则放弃。
    /// </summary>
    /// <param name="generation">发起时的填充代次。</param>
    /// <param name="source">完整卡片列表。</param>
    /// <param name="start">本次要补充的起始下标。</param>
    private void FillRemainingItems(int generation, List<FavorWallpaperView> source, int start)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation != _fillGeneration)
            {
                return;
            }
            ObservableCollection<FavorWallpaperView> target = Items;
            int end = Math.Min(start + FillChunkSize, source.Count);
            for (int i = start; i < end; i++)
            {
                target.Add(source[i]);
            }
            if (end < source.Count)
            {
                FillRemainingItems(generation, source, end);
            }
        });
    }


    private void RefreshInUseState()
    {
        string? currentBg = AppConfig.GetCustomBg(CurrentGameBiz);
        bool enabled = AppConfig.GetEnableCustomBg(CurrentGameBiz);
        // 遍历源列表而不是 Items：分帧填充期间 Items 还没放全，漏掉的卡状态会不对
        foreach (FavorWallpaperView item in IsMindscapeMode ? _mindscapeViews : _favorViews)
        {
            string fileName = FavorWallpaperService.GetCacheFileName(item.Record);
            item.IsInUse = enabled && string.Equals(currentBg, fileName, StringComparison.OrdinalIgnoreCase);
            item.IsDownloaded = FavorWallpaperService.IsCached(item.Record);
        }
    }


    private void FavorWallpaperPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
    }

}
