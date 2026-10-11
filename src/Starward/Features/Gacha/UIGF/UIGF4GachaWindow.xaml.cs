using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.Xaml.Interactivity;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.Gacha.UIGF;

[INotifyPropertyChanged]
public sealed partial class UIGF4GachaWindow : WindowEx
{

    private readonly ILogger<UIGF4GachaWindow> _logger = AppConfig.GetLogger<UIGF4GachaWindow>();

    private readonly UIGFGachaService _uigfGachaService = AppConfig.GetService<UIGFGachaService>();


    /// <summary>本窗口绑定的导出目标版本（导入侧按文件内容自动识别全部 v4.x）。</summary>
    public UIGF4Version ExportVersion { get; }


    /// <summary>
    /// 标题栏版本后缀。
    /// 从导入入口打开时显示 <c>(UIGF)</c>（自动识别 v3 / SRGF / v4.x）；从导出入口打开时显示具体子版本如 <c>(UIGF v4.0)</c>。
    /// </summary>
    public string VersionTitleSuffix => _openImport
        ? "(UIGF)"
        : $"(UIGF {ExportVersion.ToVersionString()})";


    /// <summary>启动时是否切到导入页。</summary>
    private readonly bool _openImport;


    /// <summary>本窗口的应用内通知（挂载在 StackPanel_InAppToast 上的行为）。</summary>
    private InAppToast? WindowToast => Interaction.GetBehaviors(StackPanel_InAppToast).OfType<InAppToast>().FirstOrDefault();


    /// <summary>
    /// 窗口最小宽度（逻辑像素）。按导入页算：所有固定列 + 可伸缩列的 MinWidth + 8 个列间距
    /// + 行内容左右偏移（左 60 = 列表留白 16 + 卡片内边距 16 + 勾选框 28，右 32）+ 窗口边框，约 956，留些余量取 980。
    /// 调整 XAML 中的列宽资源后需同步核算。
    /// </summary>
    private const int MinWindowWidth = 980;


    /// <summary>窗口最小高度（逻辑像素）：标题、工具栏、表头之外至少还能显示两三行记录。</summary>
    private const int MinWindowHeight = 480;


    /// <summary>上次设置最小尺寸时的缩放比例，用于在窗口移到不同缩放的显示器后重新换算。</summary>
    private double _minimumSizeScale;



    /// <param name="exportVersion">导出时写入的 UIGF 子版本；列表也按该版本过滤（v4.2 才显示千星奇域）。</param>
    /// <param name="openImport">为 true 时打开后定位到导入 Pivot；导入自动识别 v3/SRGF/v4，不依赖 <paramref name="exportVersion"/>。</param>
    public UIGF4GachaWindow(UIGF4Version exportVersion = UIGF4Version.V40, bool openImport = false)
    {
        ExportVersion = exportVersion;
        _openImport = openImport;
        GachaExportArchives.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasExportArchives));
            OnPropertyChanged(nameof(IsExportListEmpty));
        };
        GachaImportArchives.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasImportArchives));
            OnPropertyChanged(nameof(IsImportListEmpty));
        };
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
        UpdateMinimumSize();
        // 导入页列数最多，按其内容宽度给定初始窗口大小并居中（此前未设置，会沿用 WinUI 默认尺寸）。
        CenterInScreen(1200, 720);
    }



    /// <summary>
    /// 设置窗口最小尺寸，防止表格列被截断。
    /// <see cref="OverlappedPresenter"/> 的最小尺寸与 AppWindow 其他尺寸一样按物理像素计，需乘当前缩放比例。
    /// </summary>
    private void UpdateMinimumSize()
    {
        double scale = UIScale;
        if (scale == _minimumSizeScale || AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }
        _minimumSizeScale = scale;
        presenter.PreferredMinimumWidth = (int)Math.Ceiling(MinWindowWidth * scale);
        presenter.PreferredMinimumHeight = (int)Math.Ceiling(MinWindowHeight * scale);
    }



    /// <summary>窗口移到不同缩放比例的显示器时，按新比例重新换算最小尺寸。</summary>
    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        UpdateMinimumSize();
    }



    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.XamlRoot.Changed += XamlRoot_Changed;
        try
        {
            if (_openImport && Pivot_ExportImport.Items.Count > 1)
            {
                Pivot_ExportImport.SelectedIndex = 1;
            }

            var list = _uigfGachaService.GetLocalGachaArchives(ExportVersion);
            foreach (var item in list)
            {
                GachaExportArchives.Add(item);
            }
            SyncSelectAllCheckBox(ListView_Export, CheckBox_SelectAllExport);
        }
        catch (Exception ex)
        {
            // 加载列表失败：本地化短句，详情仅记日志
            ExportError = Lang.UIGF4GachaWindow_LoadExportListFailed;
            _logger.LogError(ex, "Load local gacha archives for export");
            WindowToast?.Error(Lang.UIGF4GachaWindow_LoadExportListFailed);
        }
    }



    private void RootGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        if (RootGrid.XamlRoot is not null)
        {
            RootGrid.XamlRoot.Changed -= XamlRoot_Changed;
        }
        GachaExportArchives.Clear();
        GachaImportArchives.Clear();
        Pivot_ExportImport.Items.Clear();
    }




    #region Export


    public ObservableCollection<GachaUidArchiveDisplay> GachaExportArchives { get; } = new();


    /// <summary>有可导出的归档时才启用导出按钮与表头全选。</summary>
    public bool HasExportArchives => GachaExportArchives.Count > 0;


    /// <summary>导出列表为空时在列表区域居中显示空状态。</summary>
    public bool IsExportListEmpty => GachaExportArchives.Count == 0;


    public string? ExportError { get; set => SetProperty(ref field, value); }


    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            ExportError = null;
            if (ListView_Export.SelectedItems.Count == 0)
            {
                ExportError = Lang.UIGF4GachaWindow_PleaseSelectRecordsToExport;
                WindowToast?.Warning(null, Lang.UIGF4GachaWindow_PleaseSelectRecordsToExport);
                return;
            }

            string ver = ExportVersion.ToVersionString().Replace('.', '_');
            string name = $"Moonward_UIGF_{ver}_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json";
            string? path = await FileDialogHelper.OpenSaveFileDialogAsync(Content.XamlRoot, name, ("JSON", ".json"));
            if (!string.IsNullOrWhiteSpace(path))
            {
                await _uigfGachaService.ExportUIGF4Async(path, ExportVersion, ListView_Export.SelectedItems.Cast<GachaUidArchiveDisplay>());
                var file = await StorageFile.GetFileFromPathAsync(path);
                FolderLauncherOptions options = new();
                options.ItemsToSelect.Add(file);
                await Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
            }
        }
        catch (Exception ex)
        {
            string message = ToUserFacingMessage(ex, Lang.UIGF4GachaWindow_ExportFailed);
            ExportError = message;
            _logger.LogError(ex, "Export uigf {Version}", ExportVersion.ToVersionString());
            WindowToast?.Error(Lang.UIGF4GachaWindow_ExportFailed, message);
        }
    }


    #endregion




    #region Import



    public ObservableCollection<GachaUidArchiveDisplay> GachaImportArchives { get; } = new();


    /// <summary>已从文件读出归档时才启用导入按钮与表头全选。</summary>
    public bool HasImportArchives => GachaImportArchives.Count > 0;


    /// <summary>尚未选择文件（或文件解析失败）时在列表区域居中显示空状态。</summary>
    public bool IsImportListEmpty => GachaImportArchives.Count == 0;


    /// <summary>工具栏左侧显示的已选文件名；解析失败时清空。</summary>
    public string? ImportFileName { get; set => SetProperty(ref field, value); }


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
                ImportFileName = null;
                // 按 JSON 结构自动识别 UIGF v3.0 / SRGF / UIGF v4.0–v4.2，无需用户选手版本
                var list = await _uigfGachaService.ImportFileAsync(path);
                foreach (var item in list)
                {
                    GachaImportArchives.Add(item);
                }
                ImportFileName = Path.GetFileName(path);
                SyncSelectAllCheckBox(ListView_Import, CheckBox_SelectAllImport);
            }
        }
        catch (Exception ex)
        {
            string message = ToUserFacingMessage(ex, Lang.UIGFGachaService_CannotParseFile);
            ImportError = message;
            _logger.LogError(ex, "Select uigf4 file");
            WindowToast?.Error(Lang.UIGF4GachaWindow_ImportFailed, message);
        }
    }



    [RelayCommand]
    private async Task ImportAsync()
    {
        try
        {
            ImportError = null;
            if (ListView_Import.SelectedItems.Count == 0)
            {
                ImportError = Lang.UIGF4GachaWindow_PleaseSelectRecordsToImport;
                WindowToast?.Warning(null, Lang.UIGF4GachaWindow_PleaseSelectRecordsToImport);
                return;
            }

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
                // 千星奇域（hk4eugc）无多语言名称表，改为确保物品图标信息。
                foreach (var game in imported.Select(x => x.Game).Distinct())
                {
                    if (game.Value == "hk4eugc")
                    {
                        _ = EnsureBeyondGachaInfoAfterImportAsync();
                    }
                    else
                    {
                        _ = AppConfig.GetService<GachaItemNameService>().ApplyForGameAsync(game);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            string message = ToUserFacingMessage(ex, Lang.UIGFGachaService_UnexpectedError);
            ImportError = message;
            _logger.LogError(ex, "Import uigf4 gacha");
            WindowToast?.Error(Lang.UIGF4GachaWindow_ImportFailed, message);
        }
    }



    #endregion




    #region Column Layout


    /// <summary>导出页「最后一次抽卡」列优先保留的宽度：窗口变窄时先缩到这里，再压缩游戏名列。</summary>
    private const double ExportNameMinWidth = 160;


    /// <summary>导入页「最后一次抽卡」列优先保留的宽度（导入页固定列更多，留得少一些）。</summary>
    private const double ImportNameMinWidth = 120;


    private GachaTableColumnLayout ExportColumnLayout => (GachaTableColumnLayout)RootGrid.Resources["ExportColumnLayout"];


    private GachaTableColumnLayout ImportColumnLayout => (GachaTableColumnLayout)RootGrid.Resources["ImportColumnLayout"];


    /// <summary>读取 XAML 中的列宽资源，保证计算用的固定列宽与界面一致。</summary>
    private double ColumnResource(string key) => (double)RootGrid.Resources[key];


    /// <summary>导出页表头尺寸变化（即窗口宽度变化）时重算游戏名列宽度。</summary>
    private void Grid_ExportHeaderColumns_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 固定列：图标、Uid、数量、时间；共 6 列 5 个间距
        double fixedWidth = ColumnResource("GachaColIcon") + ColumnResource("GachaColUid") + ColumnResource("GachaColCount")
                            + ColumnResource("GachaColTime") + 5 * ColumnResource("GachaColumnSpacing");
        ExportColumnLayout.Update(e.NewSize.Width - fixedWidth, ExportNameMinWidth, hasResultColumn: false);
    }


    /// <summary>导入页表头尺寸变化（即窗口宽度变化）时重算游戏名列与结果列宽度。</summary>
    private void Grid_ImportHeaderColumns_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 固定列：图标、Uid、数量、时间、时区偏移、偏移后时间；共 9 列 8 个间距
        double fixedWidth = ColumnResource("GachaColIcon") + ColumnResource("GachaColUid") + ColumnResource("GachaColCount")
                            + 2 * ColumnResource("GachaColTime") + ColumnResource("GachaColOffset") + 8 * ColumnResource("GachaColumnSpacing");
        ImportColumnLayout.Update(e.NewSize.Width - fixedWidth, ImportNameMinWidth, hasResultColumn: true);
    }


    #endregion




    #region Select All


    private void CheckBox_SelectAllExport_Click(object sender, RoutedEventArgs e) => ToggleSelectAll(ListView_Export, CheckBox_SelectAllExport);


    private void CheckBox_SelectAllImport_Click(object sender, RoutedEventArgs e) => ToggleSelectAll(ListView_Import, CheckBox_SelectAllImport);


    private void ListView_Export_SelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSelectAllCheckBox(ListView_Export, CheckBox_SelectAllExport);


    private void ListView_Import_SelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSelectAllCheckBox(ListView_Import, CheckBox_SelectAllImport);


    /// <summary>
    /// 表头全选复选框点击：已全选则全部取消，否则全选。
    /// 复选框自身的勾选状态随后由 <see cref="SyncSelectAllCheckBox"/> 按实际选中数覆盖。
    /// </summary>
    /// <param name="listView">多选列表。</param>
    /// <param name="checkBox">该列表表头的全选复选框。</param>
    private static void ToggleSelectAll(ListView listView, CheckBox checkBox)
    {
        int total = listView.Items.Count;
        if (total > 0 && listView.SelectedItems.Count == total)
        {
            listView.DeselectRange(new ItemIndexRange(0, (uint)total));
        }
        else
        {
            listView.SelectAll();
        }
        SyncSelectAllCheckBox(listView, checkBox);
    }


    /// <summary>
    /// 按列表选中数同步表头全选复选框：全选为勾选、部分选中为不确定、未选为空。
    /// </summary>
    /// <param name="listView">多选列表。</param>
    /// <param name="checkBox">该列表表头的全选复选框。</param>
    private static void SyncSelectAllCheckBox(ListView listView, CheckBox checkBox)
    {
        int selected = listView.SelectedItems.Count;
        // IsThreeState 为 false 时用户点击不会进入不确定态，但代码仍可赋 null 显示「部分选中」
        checkBox.IsChecked = selected == 0 ? false : selected == listView.Items.Count ? true : null;
    }


    #endregion


    /// <summary>
    /// 将异常转为用户可读文案：已本地化的业务异常保留 Message；JSON/IO 用专用句；其余用兜底句。
    /// </summary>
    private static string ToUserFacingMessage(Exception ex, string fallback)
    {
        // InvalidDataException / IOException 在服务层已写入本地化 Message
        if (ex is InvalidDataException or UIGF4ImportException)
        {
            return string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message;
        }
        if (ex is JsonException)
        {
            return Lang.UIGFGachaService_CannotParseFile;
        }
        if (ex is IOException or UnauthorizedAccessException)
        {
            return Lang.UIGFGachaService_FileAccessFailed;
        }
        // 内层已本地化时保留
        if (ex.InnerException is InvalidDataException ide && !string.IsNullOrWhiteSpace(ide.Message))
        {
            return ide.Message;
        }
        return fallback;
    }


    /// <summary>千星奇域导入成功后补全本地物品图标信息（失败仅记日志）。</summary>
    private async Task EnsureBeyondGachaInfoAfterImportAsync()
    {
        try
        {
            await AppConfig.GetService<GenshinBeyondGachaService>().EnsureGachaInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ensure genshin beyond gacha info after UIGF import");
        }
    }


}
