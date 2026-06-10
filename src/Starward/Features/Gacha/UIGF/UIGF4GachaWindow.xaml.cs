using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Xaml.Interactivity;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.Gacha.UIGF;

[INotifyPropertyChanged]
public sealed partial class UIGF4GachaWindow : WindowEx
{

    private readonly ILogger<UIGF4GachaWindow> _logger = AppConfig.GetLogger<UIGF4GachaWindow>();

    private readonly UIGFGachaService _uigfGachaService = AppConfig.GetService<UIGFGachaService>();


    /// <summary>本窗口的应用内通知（挂载在 StackPanel_InAppToast 上的行为）。</summary>
    private InAppToast? WindowToast => Interaction.GetBehaviors(StackPanel_InAppToast).OfType<InAppToast>().FirstOrDefault();



    public UIGF4GachaWindow()
    {
        this.InitializeComponent();
        InitializeWindow();
    }




    private void InitializeWindow()
    {
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        Title = Lang.ToolboxSetting_GachaRecordsImportExport;
        RootGrid.RequestedTheme = ShouldAppsUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AdaptTitleBarButtonColorToActuallTheme();
        SetIcon();
        // 导入页列数最多，按其内容宽度给定初始窗口大小并居中（此前未设置，会沿用 WinUI 默认尺寸）。
        CenterInScreen(1120, 720);
    }



    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var list = _uigfGachaService.GetLocalGachaArchives();
            foreach (var item in list)
            {
                GachaExportArchives.Add(item);
            }
        }
        catch (Exception ex)
        {
            ExportError = ex.Message;
        }
    }



    private void RootGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        GachaExportArchives.Clear();
        GachaImportArchives.Clear();
        Pivot_ExportImport.Items.Clear();
    }




    #region Export


    public ObservableCollection<GachaUidArchiveDisplay> GachaExportArchives { get; } = new();


    public string? ExportError { get; set => SetProperty(ref field, value); }


    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            ExportError = null;
            if (ListView_Export.SelectedItems.Count > 0)
            {
                string name = $"Starward_UIGF_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json";
                string? path = await FileDialogHelper.OpenSaveFileDialogAsync(Content.XamlRoot, name, ("JSON", ".json"));
                if (!string.IsNullOrWhiteSpace(path))
                {
                    await _uigfGachaService.ExportUIGF4Async(path, ListView_Export.SelectedItems.Cast<GachaUidArchiveDisplay>());
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    var folder = Path.GetDirectoryName(path)!;
                    FolderLauncherOptions options = new();
                    options.ItemsToSelect.Add(file);
                    await Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
                }
            }
        }
        catch (Exception ex)
        {
            ExportError = ex.Message;
            _logger.LogError(ex, "Export uigf4");
        }
    }


    #endregion




    #region Import



    public ObservableCollection<GachaUidArchiveDisplay> GachaImportArchives { get; } = new();



    public string? ImportError { get; set => SetProperty(ref field, value); }



    [RelayCommand]
    private async Task SelectFileAsync()
    {
        try
        {
            ImportError = null;
            string? path = await FileDialogHelper.PickSingleFileAsync(Content.XamlRoot, ("JSON", ".json"));
            if (File.Exists(path))
            {
                GachaImportArchives.Clear();
                var list = await _uigfGachaService.ImportFileAsync(path);
                foreach (var item in list)
                {
                    GachaImportArchives.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            ImportError = ex.Message;
            _logger.LogError(ex, "Select uigf4 file");
            WindowToast?.Error(ex);
        }
    }



    /// <summary>全选导入列表中的所有账号（ListView 为多选模式，SelectAll 直接生效）。</summary>
    private void Button_SelectAllImport_Click(object sender, RoutedEventArgs e)
    {
        ListView_Import.SelectAll();
    }



    [RelayCommand]
    private async Task ImportAsync()
    {
        try
        {
            ImportError = null;
            if (ListView_Import.SelectedItems.Count > 0)
            {
                var selected = ListView_Import.SelectedItems.Cast<GachaUidArchiveDisplay>().ToList();
                await _uigfGachaService.ImportAsync(selected);
                // 导入失败的账号以应用内通知提示（行内红字仅在列表可见时易被忽略）
                foreach (var failed in selected.Where(x => x.Error is not null))
                {
                    WindowToast?.Error(string.Format(Lang.UIGF4GachaWindow_Uid0ImportFailed, failed.Uid), failed.Error);
                }
                // 每导入成功一次就通知抽卡页面刷新（无需关闭本窗口）
                var imported = selected.Where(x => x.Error is null)
                                       .Select(x => (x.Game, x.Uid))
                                       .ToList();
                if (imported.Count > 0)
                {
                    WeakReferenceMessenger.Default.Send(new GachaLogImportedMessage(imported));
                    // 导入后按 ItemId 把每个游戏导入记录的名称回写为当前软件语言（缺失则联网下载映射）。
                    foreach (var game in imported.Select(x => x.Game).Distinct())
                    {
                        _ = AppConfig.GetService<GachaItemNameService>().ApplyForGameAsync(game);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ImportError = ex.Message;
            _logger.LogError(ex, "Import uigf4 gacha");
            WindowToast?.Error(ex);
        }
    }



    #endregion


}
