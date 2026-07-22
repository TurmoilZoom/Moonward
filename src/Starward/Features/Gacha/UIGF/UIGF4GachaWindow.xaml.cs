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



    /// <param name="exportVersion">导出时写入的 UIGF 子版本；列表也按该版本过滤（v4.2 才显示千星奇域）。</param>
    /// <param name="openImport">为 true 时打开后定位到导入 Pivot；导入自动识别 v3/SRGF/v4，不依赖 <paramref name="exportVersion"/>。</param>
    public UIGF4GachaWindow(UIGF4Version exportVersion = UIGF4Version.V40, bool openImport = false)
    {
        ExportVersion = exportVersion;
        _openImport = openImport;
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
            if (_openImport && Pivot_ExportImport.Items.Count > 1)
            {
                Pivot_ExportImport.SelectedIndex = 1;
            }

            var list = _uigfGachaService.GetLocalGachaArchives(ExportVersion);
            foreach (var item in list)
            {
                GachaExportArchives.Add(item);
            }
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
                // 按 JSON 结构自动识别 UIGF v3.0 / SRGF / UIGF v4.0–v4.2，无需用户选手版本
                var list = await _uigfGachaService.ImportFileAsync(path);
                foreach (var item in list)
                {
                    GachaImportArchives.Add(item);
                }
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
