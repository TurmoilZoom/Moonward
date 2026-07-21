using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
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

    private readonly List<string?> _sourceRoots;

    private bool _needsElevation;



    /// <param name="legacyUserDataFolder">旧版本 UserDataFolder（数据库所在目录），升级迁移源之一。</param>
    /// <param name="legacyCacheFolder">旧版本缓存根（%LocalAppData%\Moonward 等），升级迁移源之一。</param>
    /// <param name="presetTarget">提权迁移子进程的预设目标目录（带 --migrate-to 时），将自动开始迁移。</param>
    public WelcomeWindow(string? legacyUserDataFolder = null, string? legacyCacheFolder = null, string? presetTarget = null)
    {
        _legacyUserDataFolder = legacyUserDataFolder;
        _legacyCacheFolder = legacyCacheFolder;
        _presetTarget = presetTarget;
        // 权威的 UserDataFolder 在前（数据库以它为准），缓存根在后。
        _sourceRoots = new List<string?> { legacyUserDataFolder, legacyCacheFolder };
        InitializeComponent();
        InitializeWindow();
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


    /// <summary>当前是否允许操作（迁移进行中时为 false，禁用选择/启动）。</summary>
    public bool CanOperate { get; set => SetProperty(ref field, value); } = true;


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
        if (!string.IsNullOrEmpty(_presetTarget))
        {
            // 提权迁移子进程：使用预设目标目录。
            UserDataFolder = _presetTarget;
        }
        CheckWritePermission();
        CheckWebView2Support();
        await CheckWebpDecoderSupportAsync();
        if (!string.IsNullOrEmpty(_presetTarget) && CanStartStarward)
        {
            // 已提权，自动开始迁移到预设目标。
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
            string baseDir = AppContext.BaseDirectory.TrimEnd('/', '\\');
            if (folder.StartsWith(baseDir))
            {
                UserDataFolderErrorMessage = Lang.SelectDirectoryPage_AutoDeleteAfterUpdate;
                return;
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
        try
        {
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

            // 统一数据目录 = 用户所选目录 \ data，迁移过来的数据文件统一放在里面。
            string dataDir = Path.Combine(selected, AppConfig.DataSubFolderName);
            Directory.CreateDirectory(dataDir);

            if (HasLegacyData)
            {
                IsMigrating = true;
                CanOperate = false;
                CanStartStarward = false;
                MigrationIsIndeterminate = true;
                // Progress<T> 在 UI 线程创建，回调自动切回 UI 线程，避免后台线程给绑定属性赋值崩溃。
                var progress = new Progress<DataMigrationService.MigrationProgress>(OnMigrationProgress);
                try
                {
                    await DataMigrationService.MigrateAsync(_sourceRoots, dataDir, progress);
                }
                catch (Exception ex)
                {
                    IsMigrating = false;
                    CanOperate = true;
                    CheckWritePermission();
                    UserDataFolderErrorMessage = ex.Message;
                    return;
                }
            }

            AppConfig.UseDataFolder(dataDir);
            AppConfig.SaveConfiguration();
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
            // 复制现有参数，但去掉旧的 --migrate-to / --data-folder（及其值），避免重复。
            string[] existing = Environment.GetCommandLineArgs().Skip(1).ToArray();
            for (int i = 0; i < existing.Length; i++)
            {
                if (string.Equals(existing[i], "--migrate-to", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing[i], "--data-folder", StringComparison.OrdinalIgnoreCase))
                {
                    i++; // 跳过其后紧跟的值
                    continue;
                }
                info.ArgumentList.Add(existing[i]);
            }
            info.ArgumentList.Add("--migrate-to");
            info.ArgumentList.Add(target);
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



}
