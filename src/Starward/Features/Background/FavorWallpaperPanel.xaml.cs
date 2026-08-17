using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Background;

/// <summary>
/// 好感壁纸画廊；点击封面会写入当前游戏的自定义背景。
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


    public ObservableCollection<FavorWallpaperView> Items { get; } = [];


    [ObservableProperty]
    private bool isLoading;


    [ObservableProperty]
    private string? statusText;


    [ObservableProperty]
    private string? errorText;


    /// <summary>
    /// 加载列表（已有数据时只刷新使用/下载状态）。
    /// </summary>
    public async Task EnsureLoadedAsync(bool forceRefresh = false)
    {
        if (IsLoading)
        {
            return;
        }
        if (!forceRefresh && Items.Count > 0)
        {
            RefreshInUseState();
            return;
        }
        await LoadAsync(forceRefresh);
    }


    private async Task LoadAsync(bool forceRefresh)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;
        IsLoading = true;
        ErrorText = null;
        StatusText = Lang.FavorWallpaper_Loading;
        try
        {
            var progress = new Progress<FavorWallpaperLoadProgress>(p =>
            {
                if (p.FromCache)
                {
                    StatusText = null;
                    return;
                }
                StatusText = string.Format(Lang.FavorWallpaper_LoadingProgress, p.Done, p.Total);
            });
            var records = await _service.GetWallpapersAsync(forceRefresh, progress, token);
            token.ThrowIfCancellationRequested();
            Items.Clear();
            string? currentBg = AppConfig.GetCustomBg(CurrentGameBiz);
            bool enabled = AppConfig.GetEnableCustomBg(CurrentGameBiz);
            foreach (FavorWallpaperRecord record in records)
            {
                string fileName = FavorWallpaperService.GetVideoFileName(record);
                Items.Add(new FavorWallpaperView
                {
                    Record = record,
                    IsDownloaded = FavorWallpaperService.IsCached(record),
                    IsInUse = enabled && string.Equals(currentBg, fileName, StringComparison.OrdinalIgnoreCase),
                    DownloadAction = DownloadAsync,
                    DeleteAction = DeleteAsync,
                    UseAction = UseAsBackgroundAsync,
                });
            }
            StatusText = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load favor wallpapers failed");
            ErrorText = Lang.FavorWallpaper_LoadFailed;
            StatusText = null;
        }
        finally
        {
            IsLoading = false;
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
            string fileName = FavorWallpaperService.GetVideoFileName(view.Record);
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


    private void RefreshInUseState()
    {
        string? currentBg = AppConfig.GetCustomBg(CurrentGameBiz);
        bool enabled = AppConfig.GetEnableCustomBg(CurrentGameBiz);
        foreach (FavorWallpaperView item in Items)
        {
            string fileName = FavorWallpaperService.GetVideoFileName(item.Record);
            item.IsInUse = enabled && string.Equals(currentBg, fileName, StringComparison.OrdinalIgnoreCase);
            item.IsDownloaded = FavorWallpaperService.IsCached(item.Record);
        }
    }


    private void FavorWallpaperPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
    }

}
