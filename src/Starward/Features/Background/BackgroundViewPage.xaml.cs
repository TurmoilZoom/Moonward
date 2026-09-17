using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.Background;

public sealed partial class BackgroundViewPage : PageBase
{


    private readonly ILogger<BackgroundViewPage> _logger = AppConfig.GetLogger<BackgroundViewPage>();


    public BackgroundViewPage()
    {
        this.InitializeComponent();
    }


    public ObservableCollection<BackgroundFileItem> BackgroundImages { get; set => SetProperty(ref field, value); }


    public string SelectCountText { get; set => SetProperty(ref field, value); }


    public string DeleteInfoText { get; set => SetProperty(ref field, value); }


    public bool MutliSelect
    {
        get; set
        {
            field = value;
            GridView_Images.SelectionMode = value ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
            UpdateSelectCountText();
        }
    }




    protected override void OnLoaded()
    {
        LoadBackgroundItems();
    }


    protected override void OnUnloaded()
    {
        try
        {
            BackgroundImages?.Clear();
            BackgroundImages = null!;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }


    private void LoadBackgroundItems()
    {
        try
        {
            string folder = Path.Join(AppConfig.CacheFolder, "bg");
            if (Directory.Exists(folder))
            {
                string[] files = Directory.GetFiles(folder);
                var result = files.Select(TryCreateBackgroundFileItem).OfType<BackgroundFileItem>();
                if (RadioMenuFlyoutItem_Filter_Image.IsChecked)
                {
                    result = result.Where(x => !x.IsVideo);
                }
                if (RadioMenuFlyoutItem_Filter_Video.IsChecked)
                {
                    result = result.Where(x => x.IsVideo);
                }
                if (RadioMenuFlyoutItem_Sort_Time.IsChecked)
                {
                    result = result.OrderByDescending(x => x.CreationTime);
                }
                if (RadioMenuFlyoutItem_Sort_FileSize.IsChecked)
                {
                    result = result.OrderByDescending(x => x.FileSize);
                }
                BackgroundImages = new(result);
                UpdateSelectCountText();
            }
            else
            {
                StackPanel_NoFolder.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load background items.");
        }
    }


    /// <summary>
    /// 为 bg 目录中的文件创建列表项。
    /// </summary>
    /// <param name="file">文件完整路径。</param>
    /// <returns>列表项；文件在枚举之后被删掉（如转码服务校验后删除原片）或读不了时返回 null。</returns>
    private static BackgroundFileItem? TryCreateBackgroundFileItem(string file)
    {
        try
        {
            return new BackgroundFileItem(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }



    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        try
        {
            string folder = Path.Join(AppConfig.CacheFolder, "bg");
            if (Directory.Exists(folder))
            {
                await Launcher.LaunchFolderPathAsync(folder);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open background folder.");
        }
    }


    [RelayCommand]
    private async Task DeleteDuplicateBgAsync()
    {
        string folder = Path.Join(AppConfig.CacheFolder, "bg");
        if (!Directory.Exists(folder))
        {
            return;
        }
        int count = 0;
        try
        {
            string[] files = Directory.GetFiles(folder);
            ConcurrentDictionary<string, bool> dict = new();
            await Parallel.ForEachAsync(files, async (file, _) =>
            {
                try
                {
                    string key = await GetDuplicateKeyAsync(file);
                    if (dict.TryAdd(key, true))
                    {
                        return;
                    }
                    File.Delete(file);
                    Interlocked.Increment(ref count);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 刚被转码服务删掉的原片、下载中被独占的临时文件等：只跳过这一个，别让它把整次去重打断
                    _logger.LogWarning(ex, "Skip background file '{file}' when deleting duplicates.", file);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete duplicate background files.");
        }
        finally
        {
            // 出错前可能已经删了一部分，照样刷新列表，并让首页重新解析背景文件
            DeleteInfoText = string.Format(Lang.BackgroundViewPage_0DuplicateFileSHasBeenDeleted, count);
            LoadBackgroundItems();
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
        }
    }


    /// <summary>
    /// 判重用的键，键相同的文件只保留一份。普通文件用整个文件的 MD5；
    /// 官方背景的转码产物（「内容 MD5_编号.webm.mp4」）的 MP4 文件头带创建时间，同一个视频转出来的字节也不一样，
    /// 改用原片文件名里的内容 MD5。删掉的产物不影响播放：原片或其他区服查找产物时会用到留下的那一份。
    /// </summary>
    /// <param name="file">bg 目录中的文件完整路径。</param>
    /// <returns>判重键。</returns>
    private static async Task<string> GetDuplicateKeyAsync(string file)
    {
        string name = Path.GetFileName(file);
        if (name.EndsWith(".webm.mp4", StringComparison.OrdinalIgnoreCase)
            && BackgroundService.TryParseContentAddressedFileName(name[..^".mp4".Length], out string? md5, out _))
        {
            return $"transcoded:{md5.ToUpperInvariant()}";
        }
        using FileStream fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(await MD5.HashDataAsync(fs));
    }


    private void RadioMenuFlyoutItem_FilterSort_Click(object sender, RoutedEventArgs e)
    {
        LoadBackgroundItems();
    }



    private async void GridView_Images_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.None)
            {
                if (e.ClickedItem is BackgroundFileItem item)
                {
                    await Launcher.LaunchUriAsync(new(item.FilePath));
                }
            }
        }
        catch { }
    }


    private async void GridView_Images_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        try
        {
            var list = new List<StorageFile>();
            foreach (var dragItem in e.Items)
            {
                if (dragItem is BackgroundFileItem item)
                {
                    if (File.Exists(item.FilePath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        list.Add(file);
                    }
                }
            }
            if (list.Count > 0)
            {
                e.Data.RequestedOperation = DataPackageOperation.Copy;
                e.Data.SetStorageItems(list, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drag image starting");
        }
    }



    private void GridView_Images_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectCountText();
    }



    private void UpdateSelectCountText()
    {
        try
        {
            if (MutliSelect)
            {
                SelectCountText = $"{GridView_Images.SelectedItems.Count}/{BackgroundImages?.Count ?? 0}";
            }
            else
            {
                SelectCountText = $"{BackgroundImages?.Count ?? 0}";
            }
        }
        catch { }
    }



    private async void MenuFlyoutItem_Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: BackgroundFileItem item })
            {
                await Launcher.LaunchUriAsync(new(item.FilePath));
            }
        }
        catch { }
    }


    private async void MenuFlyoutItem_CopyFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.Multiple && GridView_Images.SelectedItems.Count > 0)
            {
                var list = new List<StorageFile>();
                foreach (BackgroundFileItem item in GridView_Images.SelectedItems.Cast<BackgroundFileItem>())
                {
                    if (File.Exists(item.FilePath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        list.Add(file);
                    }
                }
                if (list.Count > 0)
                {
                    ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, list.ToArray());
                }
            }
            else if (sender is FrameworkElement fe && fe.DataContext is BackgroundFileItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy file to clipboard");
        }
    }


    private async void MenuFlyoutItem_OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is BackgroundFileItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    var options = new FolderLauncherOptions();
                    options.ItemsToSelect.Add(file);
                    await Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file in explorer");
        }
    }


    private async void MenuFlyoutItem_OpenWithDefault_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is BackgroundFileItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    await Launcher.LaunchFileAsync(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file with default application");
        }
    }


    private async void MenuFlyoutItem_OpenWith_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is BackgroundFileItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    var options = new LauncherOptions { DisplayApplicationPicker = true };
                    await Launcher.LaunchFileAsync(file, options);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file with application picker");
        }
    }


    private async void MenuFlyoutItem_Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.Multiple && GridView_Images.SelectedItems.Count > 0)
            {
                var list = GridView_Images.SelectedItems.Cast<BackgroundFileItem>().ToList();
                foreach (BackgroundFileItem item in list)
                {
                    if (File.Exists(item.FilePath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        await file.DeleteAsync();
                    }
                    BackgroundImages?.Remove(item);
                }
            }
            else if (sender is FrameworkElement fe && fe.DataContext is BackgroundFileItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    await file.DeleteAsync();
                }
                BackgroundImages?.Remove(item);
            }
            UpdateSelectCountText();
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to delete image file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image file");
        }
    }


}
