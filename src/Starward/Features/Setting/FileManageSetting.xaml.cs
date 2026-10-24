using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Starward.Features.Database;
using Starward.Features.LanSync;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.Setting;

public sealed partial class FileManageSetting : PageBase
{

    private readonly ILogger<FileManageSetting> _logger = AppConfig.GetLogger<FileManageSetting>();


    public FileManageSetting()
    {
        this.InitializeComponent();
    }



    protected override void OnLoaded()
    {
        _ = RefreshBackupStatusAsync();
        _ = UpdateCacheSizeAsync();
    }




    #region 数据文件夹



    /// <summary>
    /// 修改数据文件夹
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task ChangeUserDataFolderAsync()
    {
        try
        {
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (!Directory.Exists(folder))
            {
                return;
            }
            string target = Path.GetFullPath(folder);
            // 不能选驱动器根目录，也不能选在程序安装目录下（更新时会被删除）。
            if (target == Path.GetPathRoot(target) || target.StartsWith(AppContext.BaseDirectory.TrimEnd('/', '\\')))
            {
                return;
            }
            // 实际数据目录是 所选目录\data，据此判断是否选了同一个目录。
            string newDataDir = Path.Combine(target, AppConfig.DataSubFolderName);
            if (string.Equals(newDataDir, Path.GetFullPath(AppConfig.UserDataFolder ?? string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = Lang.SettingPage_ReselectDataFolder,
                // 当前数据文件夹的位置是：xxx  →  新位置\data；软件将重启以迁移数据。
                Content = $"""
                {Lang.SettingPage_TheCurrentLocationOfTheDataFolderIs}

                {AppConfig.UserDataFolder}  →  {newDataDir}

                {Lang.SettingPage_TheSoftwareWillRestartToMigrateData}
                """,
                PrimaryButtonText = Lang.Common_Yes,
                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result is ContentDialogResult.Primary)
            {
                // 通过 --migrate-to 重启：由启动流程在数据库打开前完成迁移（与升级迁移同一套搬运服务，按需提权）。
                var info = new ProcessStartInfo { FileName = AppConfig.MoonwardExecutePath };
                info.ArgumentList.Add("--migrate-to");
                info.ArgumentList.Add(target);
                AppInstance.GetCurrent().UnregisterKey();
                Process.Start(info);
                // 即将自行拉起新进程，不能在退出时应用更新，否则会与新实例争用 current\
                App.Current.Exit(applyPendingUpdate: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change data folder");
        }
    }



    /// <summary>
    /// 打开数据文件夹
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task OpenUserDataFolderAsync()
    {
        try
        {
            if (Directory.Exists(AppConfig.UserDataFolder))
            {
                await Launcher.LaunchUriAsync(new Uri(AppConfig.UserDataFolder));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open user data folder");
        }
    }


    /// <summary>
    /// 删除所有设置
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task DeleteAllSettingAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = Lang.SettingPage_DeleteAllSettings,
                // 删除完成后，将自动重启软件。
                Content = Lang.SettingPage_AfterDeletingTheSoftwareWillBeRestartedAutomatically,
                PrimaryButtonText = Lang.Common_Delete,
                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                AppConfig.DeleteAllSettings();
                AppInstance.GetCurrent().UnregisterKey();
                Process.Start(AppConfig.MoonwardExecutePath);
                // 即将自行拉起新进程，不能在退出时应用更新，否则会与新实例争用 current\
                App.Current.Exit(applyPendingUpdate: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete all setting");
        }

    }



    /// <summary>
    /// 日志文件夹路径：统一数据目录下的 <c>log</c> 子目录（<see cref="AppConfig.CacheFolder"/> 与
    /// <see cref="AppConfig.UserDataFolder"/> 同址）。供「日志文件夹」路径行展示，与 <see cref="OpenLogFolderCommand"/> 一致。
    /// </summary>
    public string LogFolder => string.IsNullOrEmpty(AppConfig.CacheFolder) ? string.Empty : Path.Combine(AppConfig.CacheFolder, "log");



    #endregion




    #region 备份与还原



    /// <summary>备份位置（已核对目录是否还在）。</summary>
    public string BackupFolder { get; set => SetProperty(ref field, value); } = string.Empty;


    /// <summary>备份卡片的说明：上次备份时间与当前数据是否有变化；没有变化时说明未新建；备份失败时显示错误。</summary>
    public string BackupDescription { get; set => SetProperty(ref field, value); } = Lang.FileSettingPage_BackupDatabaseIntro;


    /// <summary>上次备份的文件仍在，可以打开所在位置。</summary>
    public bool HasLastBackup { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 刷新备份位置与上次备份的状态。对比当前数据要读整个库，放在后台线程。
    /// </summary>
    private async Task RefreshBackupStatusAsync()
    {
        try
        {
            BackupFolder = DatabaseBackupService.GetBackupFolder();
            if (!DatabaseBackupService.TryGetLastBackup(out _, out DateTime time, out _))
            {
                HasLastBackup = false;
                BackupDescription = Lang.FileSettingPage_BackupDatabaseIntro;
                return;
            }
            HasLastBackup = true;
            string timeText = time.ToString("yyyy-MM-dd HH:mm:ss");
            BackupDescription = $"{Lang.SettingPage_LastBackup}  {timeText}";
            bool? same = await Task.Run(DatabaseBackupService.IsSameAsLastBackup);
            if (same is not null)
            {
                BackupDescription = string.Format(same.Value ? Lang.FileSettingPage_BackupStatusSame : Lang.FileSettingPage_BackupStatusChanged, timeText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refresh backup status");
        }
    }



    /// <summary>
    /// 更改备份位置。选回默认目录时清空设置，跟着数据文件夹走。已有的备份不搬。
    /// </summary>
    [RelayCommand]
    private async Task ChangeBackupFolderAsync()
    {
        try
        {
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot, BackupFolder);
            if (!Directory.Exists(folder))
            {
                return;
            }
            if (!DatabaseBackupService.IsValidBackupFolder(folder))
            {
                BackupDescription = Lang.FileSettingPage_InvalidBackupFolder;
                return;
            }
            string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            string defaultFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DatabaseBackupService.DefaultBackupFolder));
            AppConfig.BackupFolder = string.Equals(target, defaultFolder, StringComparison.OrdinalIgnoreCase) ? null : target;
            await RefreshBackupStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change backup folder");
        }
    }



    [RelayCommand]
    private async Task OpenBackupFolderAsync()
    {
        try
        {
            string folder = DatabaseBackupService.GetBackupFolder();
            Directory.CreateDirectory(folder);
            await Launcher.LaunchFolderPathAsync(folder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open backup folder");
        }
    }



    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        try
        {
            if (Directory.Exists(AppConfig.UserDataFolder))
            {
                ManualBackupResult result = await Task.Run(DatabaseBackupService.BackupManually);
                HasLastBackup = true;
                string timeText = result.Time.ToString("yyyy-MM-dd HH:mm:ss");
                BackupDescription = result.Unchanged
                    ? string.Format(Lang.FileSettingPage_BackupUnchanged, timeText)
                    : string.Format(Lang.FileSettingPage_BackupStatusSame, timeText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup database");
            BackupDescription = ex.Message;
        }
    }



    [RelayCommand]
    private async Task OpenLastBackupDatabaseAsync()
    {
        try
        {
            if (DatabaseBackupService.TryGetLastBackup(out string? file, out _, out _))
            {
                var item = await StorageFile.GetFileFromPathAsync(file);
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(file));
                var options = new FolderLauncherOptions
                {
                    ItemsToSelect = { item }
                };
                await Launcher.LaunchFolderAsync(folder, options);
            }
            else
            {
                HasLastBackup = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open last backup database");
        }
    }



    /// <summary>
    /// 打开还原对话框。覆盖还原会在对话框里直接重启；合并还原后刷新状态。
    /// </summary>
    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        try
        {
            var dialog = new RestoreBackupDialog { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
            if (dialog.Merged)
            {
                await RefreshBackupStatusAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show restore backup dialog");
        }
    }







    #endregion




    #region 局域网同步



    /// <summary>
    /// 从局域网内其他设备同步记录到本机。
    /// </summary>
    [RelayCommand]
    private async Task SyncFromLanDeviceAsync()
    {
        try
        {
            await new LanSyncPullDialog { XamlRoot = this.XamlRoot }.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show LAN sync pull dialog");
        }
    }


    /// <summary>
    /// 共享本机数据，对话框打开期间允许其他设备同步。
    /// </summary>
    [RelayCommand]
    private async Task ShareToLanDevicesAsync()
    {
        try
        {
            await new LanSyncShareDialog { XamlRoot = this.XamlRoot }.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show LAN sync share dialog");
        }
    }



    #endregion




    #region Log



    /// <summary>
    /// 打开日志文件夹
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        try
        {
            if (File.Exists(AppConfig.LogFile))
            {
                var item = await StorageFile.GetFileFromPathAsync(AppConfig.LogFile);
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(item);
                await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(AppConfig.LogFile), options);
            }
            else
            {
                await Launcher.LaunchFolderPathAsync(Path.Combine(AppConfig.CacheFolder, "log"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open log folder");
        }
    }



    #endregion




    #region Cache



    public string LogCacheSize { get; set => SetProperty(ref field, value); } = "0.00 KB";

    public string ImageCacheSize { get; set => SetProperty(ref field, value); } = "0.00 KB";

    public string WebCacheSize { get; set => SetProperty(ref field, value); } = "0.00 KB";

    public string GameCacheSize { get; set => SetProperty(ref field, value); } = "0.00 KB";


    /// <summary>
    /// 更新缓存大小
    /// </summary>
    /// <returns></returns>
    private async Task UpdateCacheSizeAsync()
    {
        try
        {
            var local = AppConfig.CacheFolder;
            LogCacheSize = await GetFolderSizeStringAsync(Path.Combine(local, "log"));
            ImageCacheSize = await GetFolderSizeStringAsync(Path.Combine(local, "cache"));
            WebCacheSize = await GetFolderSizeStringAsync(Path.Combine(local, "webview"));
            GameCacheSize = await GetFolderSizeStringAsync(Path.Combine(local, "game"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update cache size");
        }
    }



    private static async Task<string> GetFolderSizeStringAsync(string folder) => await Task.Run(() =>
    {
        if (Directory.Exists(folder))
        {
            double size = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
            if (size < (1 << 20))
            {
                return $"{size / (1 << 10):F2} KB";
            }
            else
            {
                return $"{size / (1 << 20):F2} MB";
            }
        }
        else
        {
            return "0.00 KB";
        }
    });



    /// <summary>
    /// 清除缓存
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            var local = AppConfig.CacheFolder;
            await DeleteFolderAsync(Path.Combine(local, "log"));
            await DeleteFolderAsync(Path.Combine(local, "crash"));
            await DeleteFolderAsync(Path.Combine(local, "cache"));
            await DeleteFolderAsync(Path.Combine(local, "webview"));
            await DeleteFolderAsync(Path.Combine(local, "update"));
            await DeleteFolderAsync(Path.Combine(local, "game"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clear cache");
        }
        await UpdateCacheSizeAsync();
    }



    private async Task DeleteFolderAsync(string folder) => await Task.Run(() =>
    {
        if (Directory.Exists(folder))
        {
            try
            {
                Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete folder '{folder}'", folder);
            }
        }
    });


    #endregion



}
