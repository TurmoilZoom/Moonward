using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Core;
using Starward.Features.GameLauncher;
using Starward.Features.HoYoPlay;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.Screenshot;

public sealed partial class ScreenshotPage : PageBase
{


    private readonly ILogger<ScreenshotPage> _logger = AppConfig.GetLogger<ScreenshotPage>();


    private readonly HoYoPlayService _hoyoplayService = AppConfig.GetService<HoYoPlayService>();




    private readonly ShellFileDragSource _fileDragSource;


    public ScreenshotPage()
    {
        this.InitializeComponent();
        _fileDragSource = new ShellFileDragSource(GridView_Images, x => (x as ScreenshotItem)?.FilePath);
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Image_Emoji.Source = CurrentGameBiz.ToGame().Value switch
        {
            GameBiz.hk4e => new BitmapImage(AppConfig.EmojiPaimon),
            GameBiz.hkrpg => new BitmapImage(AppConfig.EmojiPom),
            GameBiz.bh3 => new BitmapImage(AppConfig.EmojiAI),
            GameBiz.nap => new BitmapImage(AppConfig.EmojiBangboo),
            _ => null,
        };
    }



    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        Initialize();
        _imagesScrollViewer = GridView_Images.FindDescendant<ScrollViewer>();
        if (_imagesScrollViewer is not null)
        {
            _imagesScrollViewer.ViewChanged += ScrollViewer_Images_ViewChanged;
        }
        GridView_Images.SizeChanged += GridView_Images_SizeChanged;
        GridView_Images.Items.VectorChanged += GridView_Images_Items_VectorChanged;
        QueueUpdateCurrentDate();
    }



    protected override void OnUnloaded()
    {
        try
        {
            if (_imagesScrollViewer is not null)
            {
                _imagesScrollViewer.ViewChanged -= ScrollViewer_Images_ViewChanged;
                _imagesScrollViewer = null;
            }
            GridView_Images.SizeChanged -= GridView_Images_SizeChanged;
            GridView_Images.Items.VectorChanged -= GridView_Images_Items_VectorChanged;
            foreach (var item in _watchers)
            {
                item.Dispose();
            }
            _watchers.Clear();
            _folders.Clear();
            _folders = null!;
            _screenshotDict.Clear();
            _screenshotDict = null!;
            _screenshotItems = null;
            ScreenshotGroups = null!;
            ScreenshotViewSource.Source = null;
        }
        catch { }
    }



    public bool MutliSelect
    {
        get; set
        {
            field = value;
            GridView_Images.SelectionMode = value ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        }
    }


    public string SelectCountText { get; set => SetProperty(ref field, value); }


    private List<FileSystemWatcher> _watchers = new();

    private List<ScreenshotFolder> _folders = new();

    private Dictionary<string, ScreenshotItem> _screenshotDict = new();

    private ObservableCollection<ScreenshotItem>? _screenshotItems;

    public CollectionViewSource ScreenshotViewSource { get; set => SetProperty(ref field, value); } = new() { IsSourceGrouped = true };

    public ObservableCollection<ScreenshotItemGroup> ScreenshotGroups
    {
        get;
        set
        {
            var old = field;
            if (SetProperty(ref field, value))
            {
                if (old is not null)
                {
                    old.CollectionChanged -= ScreenshotGroups_CollectionChanged;
                }
                if (value is not null)
                {
                    value.CollectionChanged += ScreenshotGroups_CollectionChanged;
                }
                ScreenshotViewSource.Source = value;
                UpdateEmptyState();
            }
        }
    }


    // 分组变空时都会被移除，所以分组数为 0 就是没有截图；监听分组而不是列表项，因为移除空分组不一定触发列表的 VectorChanged
    private void ScreenshotGroups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
    }


    /// <summary>
    /// 首次扫描截图文件夹是否已完成。扫描期间列表本来就是空的，不能把这段时间当成「没有截图」。
    /// </summary>
    private bool _screenshotsLoaded;


    private async void Initialize()
    {
        _screenshotsLoaded = false;
        UpdateEmptyState();
        try
        {
            foreach (var item in _watchers)
            {
                item.Dispose();
            }
            _folders.Clear();
            _screenshotDict.Clear();
            ScreenshotGroups = null!;

            (string? defaultFolder, string? inGameFolder) = await GetGameScreenshotPathAsync();
            if (defaultFolder is not null)
            {
                string folder = Path.GetFullPath(defaultFolder);
                if (_folders.FirstOrDefault(x => x.Folder == folder) is null)
                {
                    var watcher = CreateFileSystemWatcher(folder);
                    _watchers.Add(watcher);
                    _folders.Add(new(folder) { Default = true });
                }
            }
            if (inGameFolder is not null)
            {
                string folder = Path.GetFullPath(inGameFolder);
                if (_folders.FirstOrDefault(x => x.Folder == folder) is null)
                {
                    var watcher = CreateFileSystemWatcher(folder);
                    _watchers.Add(watcher);
                    _folders.Add(new(folder) { InGame = true });
                }
            }

            string? externalFolder = AppConfig.GetExternalScreenshotFolder(CurrentGameBiz);
            if (!string.IsNullOrWhiteSpace(externalFolder))
            {
                foreach (var item in externalFolder.Split(';'))
                {
                    string folder = item.Trim();
                    if (Directory.Exists(folder))
                    {
                        folder = Path.GetFullPath(folder);
                        if (_folders.FirstOrDefault(x => x.Folder == folder) is null)
                        {
                            var watcher = CreateFileSystemWatcher(folder);
                            _watchers.Add(watcher);
                            _folders.Add(new(folder));
                        }
                    }
                }
            }

            List<ScreenshotItem> screenshots = new();
            foreach (var folderItem in _folders)
            {
                var files = Directory.GetFiles(folderItem.Folder);
                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    if (_screenshotDict.ContainsKey(name))
                    {
                        continue;
                    }
                    // todo 优化加载速度
                    if (ScreenshotHelper.IsSupportedExtension(file) /*&& !File.GetAttributes(file).HasFlag((System.IO.FileAttributes)0x440000)*/)
                    {
                        var item = new ScreenshotItem(file);
                        screenshots.Add(item);
                        _screenshotDict[name] = item;
                    }
                }
            }

            _screenshotItems = new(screenshots.OrderByDescending(x => x.CreationTime).ToList());
            var groups = _screenshotItems.GroupBy(x => x.TimeMonthDay).Select(x => new ScreenshotItemGroup(x.Key, x)).ToList();
            ScreenshotGroups = new(groups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize");
        }
        _screenshotsLoaded = true;
        UpdateEmptyState();
    }


    /// <summary>
    /// 扫描完成且一张截图都没有时显示空状态。
    /// </summary>
    private void UpdateEmptyState()
    {
        bool empty = _screenshotsLoaded && (ScreenshotGroups is null || ScreenshotGroups.Count == 0);
        StackPanel_Emoji.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }


    public async Task<(string? BackupFolder, string? ScreenshotFolder)> GetGameScreenshotPathAsync()
    {
        try
        {
            string? backupFolder = null, screenshotFolder = null;
            string? name = GameLauncherService.GetGameExeName(CurrentGameBiz)?.Replace(".exe", "");
            string? relativePath = CurrentGameBiz.Game switch
            {
                GameBiz.hk4e => "ScreenShot",
                GameBiz.hkrpg => @"StarRail_Data\ScreenShots",
                GameBiz.bh3 => @"ScreenShot",
                GameBiz.nap => @"ScreenShot",
                _ => null,
            };
            if (name is null || relativePath is null)
            {
                var config = await _hoyoplayService.GetGameConfigAsync(CurrentGameId);
                if (config is not null)
                {
                    name ??= config.ExeFileName.Replace(".exe", "");
                    relativePath ??= config.GameScreenshotDir;
                }
            }
            string? folder = AppConfig.ScreenshotFolder;
            if (!Directory.Exists(folder))
            {
                folder = Path.Join(AppConfig.UserDataFolder, "Screenshots");
            }
            folder = Path.Join(folder, name);
            Directory.CreateDirectory(folder);
            backupFolder = folder;
            string? installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            folder = Path.Join(installPath, relativePath);
            if (Directory.Exists(folder))
            {
                screenshotFolder = folder;
            }
            return (backupFolder, screenshotFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game screenshot path");
        }
        return (null, null);
    }


    private FileSystemWatcher CreateFileSystemWatcher(string folder)
    {
        var watcher = new FileSystemWatcher(folder);
        watcher.NotifyFilter = NotifyFilters.FileName;
        foreach (var item in ScreenshotHelper.WatcherFilters)
        {
            watcher.Filters.Add(item);
        }
        watcher.Created += FileSystemWatcher_Created;
        watcher.Deleted += FileSystemWatcher_Deleted;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }


    private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                string name = Path.GetFileName(e.FullPath);
                if (_screenshotDict.ContainsKey(name))
                {
                    return;
                }
                if (ScreenshotHelper.IsSupportedExtension(e.FullPath) && File.Exists(e.FullPath))
                {
                    await ScreenshotHelper.WaitForFileReleaseAsync(e.FullPath, CancellationToken.None);
                    var item = new ScreenshotItem(e.FullPath);
                    _screenshotDict[name] = item;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ScreenshotGroups ??= new();
                        _screenshotItems ??= new();
                        _screenshotItems.Insert(0, item);
                        if (ScreenshotGroups.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                        {
                            group.Insert(0, item);
                        }
                        else
                        {
                            var newGroup = new ScreenshotItemGroup(item.TimeMonthDay, [item]);
                            ScreenshotGroups.Insert(0, newGroup);
                        }
                        UpdateSelectCountText();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File system watcher created event");
        }
    }


    private void FileSystemWatcher_Deleted(object sender, FileSystemEventArgs e)
    {
        try
        {
            string name = Path.GetFileName(e.FullPath);
            if (_screenshotDict.TryGetValue(name, out ScreenshotItem? item))
            {
                if (e.FullPath == item.FilePath)
                {
                    if (ScreenshotGroups?.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                    {
                        if (group.Contains(item))
                        {
                            _screenshotDict.Remove(item.Name);
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                _screenshotItems?.Remove(item);
                                group.Remove(item);
                                if (group.Count == 0)
                                {
                                    ScreenshotGroups.Remove(group);
                                }
                                UpdateSelectCountText();
                            });
                        }
                    }
                }
            }
        }
        catch { }
    }



    [RelayCommand]
    private void OpenImageBatchConvertWindow()
    {
        try
        {
            var list = MutliSelect ? GridView_Images.SelectedItems.Cast<ScreenshotItem>().ToList() : [];
            new ImageBatchConvertWindow().Activate(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open image batch convert window");
        }
    }


    [RelayCommand]
    private async Task ManageScreenshotFolderAsync()
    {
        try
        {
            var dialog = new ScreenshotFolderManageDialog
            {
                Folders = _folders,
                CurrentGameId = this.CurrentGameId,
                XamlRoot = this.XamlRoot,
            };
            await dialog.ShowAsync();
            if (dialog.FolderChanged)
            {
                string folder = string.Join(';', dialog.Folders.Where(x => x.CanRemove).Select(x => x.Folder));
                AppConfig.SetExternalScreenshotFolder(CurrentGameBiz, folder);
                Initialize();
                UpdateSelectCountText();
            }
        }
        catch { }
    }


    private void GridView_Images_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.None)
            {
                if (e.ClickedItem is ScreenshotItem item)
                {
                    _ = new ImageViewWindow2().ShowWindowAsync(this.XamlRoot.ContentIslandEnvironment.AppWindowId, item, _screenshotItems);
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
                if (dragItem is ScreenshotItem item)
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



    #region Current Date


    private ScrollViewer? _imagesScrollViewer;

    private bool _currentDateUpdateQueued;


    private void ScrollViewer_Images_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateCurrentDate();
        // 跳转式滚动（回到顶部、键盘翻页）触发时容器还在旧位置，算出的是跳转前的日期，且之后不会再触发；布局后再算一次
        QueueUpdateCurrentDate();
    }


    private void GridView_Images_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueUpdateCurrentDate();
    }


    private void GridView_Images_Items_VectorChanged(IObservableVector<object> sender, IVectorChangedEventArgs e)
    {
        QueueUpdateCurrentDate();
    }


    /// <summary>
    /// 等下一轮布局完成后再刷新当前日期。增删截图、改变列表宽度后，容器位置要到下一轮布局才更新，立即计算会拿到旧位置。
    /// </summary>
    private void QueueUpdateCurrentDate()
    {
        if (_currentDateUpdateQueued)
        {
            return;
        }
        _currentDateUpdateQueued = true;
        GridView_Images.LayoutUpdated += OnLayoutUpdated;

        void OnLayoutUpdated(object? sender, object e)
        {
            GridView_Images.LayoutUpdated -= OnLayoutUpdated;
            // 不在布局回调里同步读写界面，推到本轮布局之后
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _currentDateUpdateQueued = false;
                UpdateCurrentDate();
            });
        }
    }


    /// <summary>
    /// 刷新按钮栏左侧的当前日期：显示最后一个已滚出列表顶边的分组标题。
    /// 第一个分组的标题还在列表里时隐藏，免得同一个日期上下各出现一次。
    /// </summary>
    private void UpdateCurrentDate()
    {
        try
        {
            string? date = null;
            if (_imagesScrollViewer is not null
                && ScreenshotGroups is { Count: > 0 } groups
                && GridView_Images.ItemsPanelRoot is ItemsWrapGrid panel
                && panel.FirstVisibleIndex >= 0
                && GridView_Images.ContainerFromIndex(panel.FirstVisibleIndex) is DependencyObject container
                && GridView_Images.ItemFromContainer(container) is ScreenshotItem item)
            {
                int groupIndex = -1;
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i].Header == item.TimeMonthDay)
                    {
                        groupIndex = i;
                        break;
                    }
                }
                if (groupIndex >= 0)
                {
                    // 首个可见项所在分组的标题过半滚出顶边，才算进入这一组；之前仍显示上一组，交接时机和原来的吸顶一致。
                    // 标题容器没有实现时说明它早已远离视口（标题总在本组项目上方），按已滚出处理。
                    bool headerPassed = true;
                    if (GridView_Images.GroupHeaderContainerFromItemContainer(container) is FrameworkElement header)
                    {
                        double top = header.TransformToVisual(_imagesScrollViewer).TransformPoint(default).Y;
                        headerPassed = top + header.ActualHeight / 2 <= 0;
                    }
                    if (headerPassed)
                    {
                        date = groups[groupIndex].Header;
                    }
                    else if (groupIndex > 0)
                    {
                        date = groups[groupIndex - 1].Header;
                    }
                }
            }
            if (date is null)
            {
                TextBlock_CurrentDate.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (TextBlock_CurrentDate.Text != date)
                {
                    TextBlock_CurrentDate.Text = date;
                }
                TextBlock_CurrentDate.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            // 滚动中每帧都会调用，用 Debug 级别避免刷屏
            _logger.LogDebug(ex, "Update current date");
        }
    }


    #endregion


    private void UpdateSelectCountText()
    {
        try
        {
            if (MutliSelect)
            {
                SelectCountText = $"{GridView_Images.SelectedItems.Count}/{_screenshotItems?.Count ?? 0}";
            }
            else
            {
                SelectCountText = $"{_screenshotItems?.Count ?? 0}";
            }
        }
        catch { }
    }



    private void MenuFlyoutItem_ScreenshotInfo_Loading(FrameworkElement sender, object args)
    {
        try
        {
            if (sender.DataContext is ScreenshotItem item)
            {
                item.UpdatePixelSize();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get image pixel size");
        }
    }


    private void MenuFlyoutItem_Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ScreenshotItem item })
            {
                _ = new ImageViewWindow2().ShowWindowAsync(this.XamlRoot.ContentIslandEnvironment.AppWindowId, item, _screenshotItems);
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
                foreach (ScreenshotItem item in GridView_Images.SelectedItems.Cast<ScreenshotItem>())
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
                    InAppToast.MainWindow?.Success(Lang.ImageViewWindow2_CopiedToClipboard, string.Format(Lang.ScreenshotPage_Total0Files, list.Count), 1500);
                }
            }
            else if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, file);
                    InAppToast.MainWindow?.Success(Lang.ImageViewWindow2_CopiedToClipboard, null, 1500);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow2_FileDoesNotExist, item.FilePath, 3000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy file to clipboard");
        }
    }


    private async void MenuFlyoutItem_CopyAsJPG_Click(object sender, RoutedEventArgs e)
    {
        static string GetSizeString(long size)
        {
            const double KB = 1 << 10;
            const double MB = 1 << 20;
            if (size >= MB)
            {
                return $"{size / MB:F2} MB";
            }
            else
            {
                return $"{size / KB:F2} KB";
            }
        }
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    string jpgFilePath = await ScreenshotHelper.ConvertToJpgAsync(item.FilePath);
                    var file = await StorageFile.GetFileFromPathAsync(jpgFilePath);
                    ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, file);
                    InAppToast.MainWindow?.Success($"{Lang.ImageViewWindow2_CopiedToClipboard} ({GetSizeString(new FileInfo(jpgFilePath).Length)})", null, 1500);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow2_FileDoesNotExist, item.FilePath, 3000);
                }
            }
        }
        catch (Exception ex)
        {
            InAppToast.MainWindow?.Error(Lang.ImageViewWindow2_CopyAsJPG, ex.Message, 3000);
            _logger.LogError(ex, "Failed to copy file as JPG to clipboard");
        }
    }


    /// <summary>
    /// 复制文件名（含扩展名）到剪贴板；多选模式下复制全部选中项，每行一个。
    /// </summary>
    private void MenuFlyoutItem_CopyFileName_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.Multiple && GridView_Images.SelectedItems.Count > 0)
            {
                var names = GridView_Images.SelectedItems.Cast<ScreenshotItem>().Select(x => x.FileName).ToList();
                ClipboardHelper.SetText(string.Join(Environment.NewLine, names));
                InAppToast.MainWindow?.Success(Lang.ImageViewWindow2_CopiedToClipboard, string.Format(Lang.ScreenshotPage_Total0Files, names.Count), 1500);
            }
            else if (sender is FrameworkElement { DataContext: ScreenshotItem item })
            {
                ClipboardHelper.SetText(item.FileName);
                InAppToast.MainWindow?.Success(Lang.ImageViewWindow2_CopiedToClipboard, item.FileName, 1500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy file name to clipboard");
        }
    }


    private async void MenuFlyoutItem_OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    var options = new FolderLauncherOptions();
                    options.ItemsToSelect.Add(file);
                    await Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow2_FileDoesNotExist, item.FilePath, 3000);
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
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    await Launcher.LaunchFileAsync(file);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow2_FileDoesNotExist, item.FilePath, 3000);
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
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    var options = new LauncherOptions { DisplayApplicationPicker = true };
                    await Launcher.LaunchFileAsync(file, options);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow2_FileDoesNotExist, item.FilePath, 3000);
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
                var list = GridView_Images.SelectedItems.Cast<ScreenshotItem>().ToList();
                foreach (ScreenshotItem item in list)
                {
                    if (File.Exists(item.FilePath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        await file.DeleteAsync();
                    }
                    _screenshotItems?.Remove(item);
                    if (ScreenshotGroups?.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                    {
                        if (group.Remove(item))
                        {
                            _screenshotDict.Remove(item.FileName);
                            if (group.Count == 0)
                            {
                                ScreenshotGroups.Remove(group);
                            }
                        }
                    }
                }
            }
            else if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    await file.DeleteAsync();
                }
                _screenshotItems?.Remove(item);
                if (ScreenshotGroups?.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                {
                    if (group.Remove(item))
                    {
                        _screenshotDict.Remove(item.FileName);
                        if (group.Count == 0)
                        {
                            ScreenshotGroups.Remove(group);
                        }
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // TODO 使用 RPC 删除
            InAppToast.MainWindow?.Warning(Lang.ImageViewWindow2_UnableToDeleteTheFile, Lang.ImageViewWindow2_InsufficientPermissionsOrTheFileIsInUse, 5000);
            _logger.LogError(ex, "Failed to delete image file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image file");
        }
    }


}
