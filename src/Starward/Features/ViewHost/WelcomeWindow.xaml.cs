using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Starward.Features.Database;
using Starward.Features.Setting;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;


namespace Starward.Features.ViewHost;

[ObservableObject]
public sealed partial class WelcomeWindow : WindowEx
{


    private TaskCompletionSource<bool> _taskCompletionSource;

    private readonly string? _legacyUserDataFolder;

    private readonly string? _legacyCacheFolder;

    private readonly string? _presetTarget;

    private readonly bool _presetIsDataDirectory;

    private readonly List<string?> _sourceRoots;

    private readonly bool _importStarwardPreset;

    private StarwardDataImportService.StarwardInstallInfo? _starwardSource;

    private bool _needsElevation;



    /// <param name="legacyUserDataFolder">旧版本 UserDataFolder（数据库所在目录），升级迁移源之一。</param>
    /// <param name="legacyCacheFolder">旧版本缓存根（%LocalAppData%\Moonward 等），升级迁移源之一。</param>
    /// <param name="presetTarget">预设目标目录。提权迁移时是用户所选父目录；调试固定数据目录时是真正的 data 目录。</param>
    /// <param name="presetIsDataDirectory"><paramref name="presetTarget"/> 已是统一数据目录（调试用），不再拼接 data 子目录，也不自动开始。</param>
    public WelcomeWindow(string? legacyUserDataFolder = null, string? legacyCacheFolder = null, string? presetTarget = null, bool presetIsDataDirectory = false)
    {
        _legacyUserDataFolder = legacyUserDataFolder;
        _legacyCacheFolder = legacyCacheFolder;
        _presetTarget = presetTarget;
        _presetIsDataDirectory = presetIsDataDirectory;
        _importStarwardPreset = HasCommandLineFlag("--import-starward");
        // 权威的 UserDataFolder 在前（数据库以它为准），缓存根在后。
        _sourceRoots = new List<string?> { legacyUserDataFolder, legacyCacheFolder };
        InitializeComponent();
        InitializeWindow();
        if (_presetIsDataDirectory)
        {
            CanChangeUserDataFolder = false;
        }
        _taskCompletionSource = new();
    }



    private void InitializeWindow()
    {
        this.Closed += NoPermissionWindow_Closed;
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        CenterInScreen(1200, 676);
        AdaptTitleBarButtonColorToActuallTheme();
        SetDragRectangles(new RectInt32(0, 0, 100000, (int)(48 * UIScale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = true;
        }
    }




    private void NoPermissionWindow_Closed(object sender, WindowEventArgs args)
    {
        _taskCompletionSource.TrySetResult(false);
    }



    public async Task<bool> WaitAsync()
    {
        this.Activate();
        return await _taskCompletionSource.Task;
    }



    public string? UserDataFolder { get; set => SetProperty(ref field, value); }


    public string? UserDataFolderErrorMessage { get; set => SetProperty(ref field, value); }


    public string? WebView2Version { get; set => SetProperty(ref field, value); }


    public bool WebpDecoderSupport { get; set => SetProperty(ref field, value); }


    public bool CanStartStarward { get; set => SetProperty(ref field, value); }


    public bool IsWin11 { get; set => SetProperty(ref field, value); }


    /// <summary>是否检测到可迁移的旧版本数据。</summary>
    public bool HasLegacyData { get; set => SetProperty(ref field, value); }


    /// <summary>是否探测到本机 Starward 数据库。</summary>
    public bool HasStarwardData { get; set => SetProperty(ref field, value); }


    /// <summary>用户是否选择从 Starward 导入（仅探测到源库时可选）。</summary>
    public bool MigrateFromStarward
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateStartButtonText();
            }
        }
    }


    /// <summary>勾选框是否可操作：已解析到 Starward 库且当前未在迁移。</summary>
    public bool CanMigrateFromStarward { get; set => SetProperty(ref field, value); }


    /// <summary>是否允许浏览选择 Starward 数据/便携安装目录。</summary>
    public bool CanPickStarwardFolder { get; set => SetProperty(ref field, value); } = true;


    /// <summary>手动选择 Starward 目录失败时的提示。</summary>
    public string? StarwardFolderErrorMessage
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateStarwardNotFoundHint();
            }
        }
    }


    /// <summary>已解析到的 Starward 数据目录，显示在选择按钮右侧。</summary>
    public string? StarwardSourcePath { get; set => SetProperty(ref field, value); }


    /// <summary>未指定来源且没有选目录错误时，在按钮右侧显示未检测到提示。</summary>
    public bool ShowStarwardNotFoundHint { get; set => SetProperty(ref field, value); } = true;


    /// <summary>是否允许改选数据文件夹。调试固定目录时为 false。</summary>
    public bool CanChangeUserDataFolder { get; set => SetProperty(ref field, value); } = true;


    /// <summary>当前是否允许操作（迁移进行中时为 false，禁用选择/启动）。</summary>
    public bool CanOperate
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateStarwardImportEnabled();
                CanChangeUserDataFolder = value && !_presetIsDataDirectory;
                CanPickStarwardFolder = value;
            }
        }
    } = true;


    /// <summary>迁移是否进行中。</summary>
    public bool IsMigrating { get; set => SetProperty(ref field, value); }


    public bool MigrationIsIndeterminate { get; set => SetProperty(ref field, value); }


    public double MigrationProgressValue { get; set => SetProperty(ref field, value); }


    public string? MigrationStatus { get; set => SetProperty(ref field, value); }


    public string? MigrationNoticeText { get; set => SetProperty(ref field, value); }


    public string StartButtonText { get; set => SetProperty(ref field, value); } = Lang.WelcomeView_StarwardStart;



    private async void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        IsWin11 = Environment.OSVersion.Version >= new Version(10, 0, 22000);
        DetectLegacyData();
        DetectStarwardData();
        if (!string.IsNullOrEmpty(_presetTarget))
        {
            UserDataFolder = _presetTarget;
        }
        if (_presetIsDataDirectory)
        {
            CanChangeUserDataFolder = false;
        }
        CheckWritePermission();
        CheckWebView2Support();
        await CheckWebpDecoderSupportAsync();
        if (!string.IsNullOrEmpty(_presetTarget) && !_presetIsDataDirectory && CanStartStarward)
        {
            // 提权迁移子进程：自动开始迁移到预设目标。调试固定目录不自动开始，以便勾选导入。
            await StartAsync();
        }
    }



    /// <summary>
    /// 探测是否存在旧版本数据需要迁移，并准备提示文案。强制手动选择目标目录，因此不预填默认值。
    /// </summary>
    private void DetectLegacyData()
    {
        try
        {
            HasLegacyData = DataMigrationService.HasLegacyData(_sourceRoots, string.Empty);
            if (HasLegacyData)
            {
                StartButtonText = Lang.WelcomeView_MigrateAndStart;
                IEnumerable<string> locations = _sourceRoots.Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x))
                                                            .Select(x => x!)
                                                            .Distinct(StringComparer.OrdinalIgnoreCase);
                MigrationNoticeText = $"{Lang.WelcomeView_LegacyDataMigrationNotice}\n{string.Join("\n", locations)}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }


    /// <summary>
    /// 解析 Starward 数据：命令行指定目录优先，否则自动探测。
    /// 提权子进程带 <c>--import-starward</c> 时强制勾选。
    /// 已有旧版 Moonward 数据时默认不勾；即使用户再勾选，目标里已有的库也不会被覆盖。
    /// </summary>
    private void DetectStarwardData()
    {
        try
        {
            string? fromArg = GetCommandLineArgValue("--import-starward-from");
            if (!string.IsNullOrWhiteSpace(fromArg)
                && StarwardDataImportService.TryResolveFromDirectory(fromArg, out StarwardDataImportService.StarwardInstallInfo fromFolder))
            {
                ApplyStarwardSource(fromFolder, checkByDefault: true);
                return;
            }

            if (StarwardDataImportService.TryDetect(out StarwardDataImportService.StarwardInstallInfo detected))
            {
                ApplyStarwardSource(detected, checkByDefault: _importStarwardPreset || !HasLegacyData);
                return;
            }

            ClearStarwardSource();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ClearStarwardSource();
        }
    }


    private void ApplyStarwardSource(StarwardDataImportService.StarwardInstallInfo install, bool checkByDefault)
    {
        _starwardSource = install;
        HasStarwardData = install.HasDatabase;
        StarwardFolderErrorMessage = null;
        StarwardSourcePath = install.UserDataFolder ?? Path.GetDirectoryName(install.DatabasePath);
        MigrateFromStarward = checkByDefault;
        UpdateStarwardImportEnabled();
        UpdateStartButtonText();
    }


    private void ClearStarwardSource()
    {
        _starwardSource = null;
        HasStarwardData = false;
        MigrateFromStarward = false;
        StarwardSourcePath = null;
        UpdateStarwardImportEnabled();
        UpdateStartButtonText();
    }


    [RelayCommand]
    private async Task SelectStarwardFolderAsync()
    {
        try
        {
            StarwardFolderErrorMessage = null;
            string? folder = await FileDialogHelper.PickFolderAsync(Content.XamlRoot, StarwardSourcePath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }
            if (StarwardDataImportService.TryResolveFromDirectory(folder, out StarwardDataImportService.StarwardInstallInfo install))
            {
                ApplyStarwardSource(install, checkByDefault: true);
            }
            else
            {
                StarwardFolderErrorMessage = Lang.WelcomeView_StarwardFolderHasNoDatabase;
            }
        }
        catch (Exception ex)
        {
            StarwardFolderErrorMessage = ex.Message;
            Debug.WriteLine(ex);
        }
    }


    private void UpdateStarwardImportEnabled()
    {
        CanMigrateFromStarward = HasStarwardData && CanOperate;
        UpdateStarwardNotFoundHint();
    }


    private void UpdateStarwardNotFoundHint()
    {
        ShowStarwardNotFoundHint = !HasStarwardData && string.IsNullOrWhiteSpace(StarwardFolderErrorMessage);
    }


    private void UpdateStartButtonText()
    {
        StartButtonText = (HasLegacyData || MigrateFromStarward) ? Lang.WelcomeView_MigrateAndStart : Lang.WelcomeView_StarwardStart;
    }



    private void CheckWritePermission()
    {
        try
        {
            UserDataFolderErrorMessage = null;
            CanStartStarward = false;
            _needsElevation = false;
            if (string.IsNullOrWhiteSpace(UserDataFolder) || !Path.IsPathFullyQualified(UserDataFolder))
            {
                UserDataFolderErrorMessage = Lang.WelcomeView_PleaseSelectDataFolder;
                return;
            }
            string folder = Path.GetFullPath(UserDataFolder);
            Directory.CreateDirectory(folder);
            if (folder == Path.GetPathRoot(folder))
            {
                UserDataFolderErrorMessage = Lang.LauncherPage_PleaseDoNotSelectTheRootDirectoryOfADrive;
                return;
            }
            // 调试固定目录就在程序目录 \ data 下，正式安装才禁止选安装目录（更新会删）。
            if (!_presetIsDataDirectory)
            {
                string baseDir = AppContext.BaseDirectory.TrimEnd('/', '\\');
                if (folder.StartsWith(baseDir))
                {
                    UserDataFolderErrorMessage = Lang.SelectDirectoryPage_AutoDeleteAfterUpdate;
                    return;
                }
            }
            var file = Path.Combine(folder, Guid.CreateVersion7().ToString());
            File.WriteAllBytes(file, "Write permission test."u8);
            File.Delete(file);
            CanStartStarward = true;
        }
        catch (UnauthorizedAccessException ex)
        {
            // 没有写入权限：按需提权——仍允许继续，迁移时通过 UAC 以管理员身份执行。
            _needsElevation = true;
            CanStartStarward = true;
            UserDataFolderErrorMessage = Lang.WelcomeView_FolderNeedsAdminPermission;
            Debug.WriteLine(ex);
        }
        catch (Exception ex)
        {
            UserDataFolderErrorMessage = ex.Message;
            Debug.WriteLine(ex);
        }
    }



    [RelayCommand]
    private async Task ChangeUserDataFolderAsync()
    {
        if (_presetIsDataDirectory)
        {
            return;
        }
        try
        {
            UserDataFolderErrorMessage = null;
            string? folder = await FileDialogHelper.PickFolderAsync(Content.XamlRoot);
            if (Directory.Exists(folder))
            {
                UserDataFolder = folder;
                CheckWritePermission();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }



    private void CheckWebView2Support()
    {
        try
        {
            WebView2Version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }




    private async Task CheckWebpDecoderSupportAsync()
    {
        try
        {
            // 一个webp图片
            byte[] bytes = Convert.FromBase64String("UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAgA0JaQAA3AA/vv9UAA=");
            using MemoryStream ms = new MemoryStream(bytes);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.WebpDecoderId, ms.AsRandomAccessStream());
            WebpDecoderSupport = true;
        }
        catch (Exception ex)
        {
            // 0x88982F8B
            Debug.WriteLine(ex);
        }
    }



    private async void Hyperlink_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender, Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        try
        {
            if (sender.NavigateUri.Scheme is "http" or "https")
            {
                return;
            }
            await Launcher.LaunchUriAsync(sender.NavigateUri);
        }
        catch { }
    }



    [RelayCommand]
    private async Task StartAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(UserDataFolder))
            {
                CheckWritePermission();
                return;
            }
            string selected = Path.GetFullPath(UserDataFolder);

            if (_needsElevation)
            {
                // 目标目录需要管理员权限：以管理员身份重启并带 --migrate-to 完成迁移（按需提权）。传所选目录，data 子目录由提权实例统一拼接。
                RelaunchElevatedForMigration(selected);
                return;
            }

            // 正式安装：所选目录 \ data。调试固定目录已经是 data，不再套一层。
            string dataDir = _presetIsDataDirectory ? selected : Path.Combine(selected, AppConfig.DataSubFolderName);
            Directory.CreateDirectory(dataDir);

            bool importedStarwardDatabase = false;
            bool needTransfer = HasLegacyData || MigrateFromStarward;
            if (needTransfer)
            {
                IsMigrating = true;
                CanOperate = false;
                CanStartStarward = false;
                MigrationIsIndeterminate = true;
                // Progress<T> 在 UI 线程创建，回调自动切回 UI 线程，避免后台线程给绑定属性赋值崩溃。
                var progress = new Progress<DataMigrationService.MigrationProgress>(OnMigrationProgress);
                try
                {
                    if (HasLegacyData)
                    {
                        await DataMigrationService.MigrateAsync(_sourceRoots, dataDir, progress);
                    }
                    if (MigrateFromStarward)
                    {
                        StarwardDataImportService.ImportResult import = await StarwardDataImportService.ImportAsync(dataDir, _starwardSource, progress);
                        importedStarwardDatabase = import.ImportedDatabase;
                    }
                }
                catch (Exception ex)
                {
                    IsMigrating = false;
                    CanOperate = true;
                    // 过新源在拷库前已拒绝，或服务已删掉误落的 dest；空目录仍可开始。
                    CheckWritePermission();
                    UserDataFolderErrorMessage = ex.Message;
                    return;
                }
            }

            AppConfig.UseDataFolder(dataDir);
            AppConfig.SaveConfiguration();

            if (importedStarwardDatabase)
            {
                IsMigrating = false;
                await ShowStarwardImportReloginDialogAsync();
            }

            _taskCompletionSource.TrySetResult(true);
            this.Close();
        }
        catch (Exception ex)
        {
            IsMigrating = false;
            CanOperate = true;
            UserDataFolderErrorMessage = ex.Message;
            Debug.WriteLine(ex);
        }
    }



    private void OnMigrationProgress(DataMigrationService.MigrationProgress p)
    {
        MigrationIsIndeterminate = p.BytesTotal == 0;
        MigrationProgressValue = p.BytesTotal == 0 ? 0 : (double)p.BytesDone / p.BytesTotal * 100;
        MigrationStatus = $"{FormatSize(p.BytesDone)} / {FormatSize(p.BytesTotal)}    {p.CurrentItem}";
    }



    private static string FormatSize(long bytes)
    {
        if (bytes < 1L << 20)
        {
            return $"{bytes / 1024.0:F1} KB";
        }
        if (bytes < 1L << 30)
        {
            return $"{bytes / (double)(1 << 20):F1} MB";
        }
        return $"{bytes / (double)(1 << 30):F2} GB";
    }



    /// <summary>
    /// 以管理员身份重启自身并带 --migrate-to 参数，由提权后的实例完成迁移并继续运行。
    /// </summary>
    private void RelaunchElevatedForMigration(string target)
    {
        const int ERROR_CANCELLED = 0x000004C7;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas",
            };
            // 复制现有参数，但去掉旧的 --migrate-to / --data-folder（及其值）和 --import-starward，避免重复。
            string[] existing = Environment.GetCommandLineArgs().Skip(1).ToArray();
            for (int i = 0; i < existing.Length; i++)
            {
                if (string.Equals(existing[i], "--migrate-to", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing[i], "--data-folder", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing[i], "--import-starward-from", StringComparison.OrdinalIgnoreCase))
                {
                    i++; // 跳过其后紧跟的值
                    continue;
                }
                if (string.Equals(existing[i], "--import-starward", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                info.ArgumentList.Add(existing[i]);
            }
            info.ArgumentList.Add("--migrate-to");
            info.ArgumentList.Add(target);
            if (MigrateFromStarward)
            {
                info.ArgumentList.Add("--import-starward");
                string? starwardFolder = _starwardSource?.UserDataFolder ?? Path.GetDirectoryName(_starwardSource?.DatabasePath);
                if (!string.IsNullOrWhiteSpace(starwardFolder))
                {
                    info.ArgumentList.Add("--import-starward-from");
                    info.ArgumentList.Add(starwardFolder);
                }
            }
            Process.Start(info);
            _taskCompletionSource.TrySetResult(false);
            this.Close();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            // 用户取消了 UAC 提权。
            UserDataFolderErrorMessage = Lang.WelcomeView_AdminPermissionRequiredToMigrate;
            Debug.WriteLine(ex);
        }
        catch (Exception ex)
        {
            UserDataFolderErrorMessage = ex.Message;
            Debug.WriteLine(ex);
        }
    }


    /// <summary>
    /// 从 Starward 导入成功后提醒重新登录米游社 / HoYoLAB，以便使用签到等新增功能。
    /// </summary>
    private async Task ShowStarwardImportReloginDialogAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = Lang.WelcomeView_StarwardImportCompleted,
                Content = Lang.WelcomeView_StarwardImportReloginHint,
                CloseButtonText = Lang.Common_Confirm,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }


    private static bool HasCommandLineFlag(string name)
    {
        return Environment.GetCommandLineArgs().Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }


    private static string? GetCommandLineArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1]?.Trim();
            }
        }
        return null;
    }


}
