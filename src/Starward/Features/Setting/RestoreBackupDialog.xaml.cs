using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Starward.Features.Database;
using Starward.Features.LanSync;
using Starward.Features.PlayTime;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Starward.Features.Setting;

/// <summary>
/// 从备份还原：选备份位置里的备份或别处的文件，检查后按所选方式还原。
/// 覆盖还原要重启，由新进程在启动时完成；合并还原直接在这里做完。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class RestoreBackupDialog : ContentDialog
{

    private readonly ILogger<RestoreBackupDialog> _logger = AppConfig.GetLogger<RestoreBackupDialog>();

    private bool _loaded;

    /// <summary>检查超过这个时长才在原位显示进度，检查很快时界面完全不动。</summary>
    private const int CheckingIndicatorDelayMs = 300;

    /// <summary>每次选择都加一，检查完成时据此丢弃过期的结果。</summary>
    private int _prepareVersion;

    private PreparedBackup? _prepared;

    /// <summary>最近一次选择触发的检查。</summary>
    private Task? _prepareTask;

    /// <summary>已把副本交给重启后的新进程，关闭时不能删。</summary>
    private bool _handedOver;



    public RestoreBackupDialog()
    {
        this.InitializeComponent();
    }



    /// <summary>本次打开期间是否完成过合并还原，调用方据此刷新备份状态。</summary>
    public bool Merged { get; private set; }


    public ObservableCollection<RestoreBackupItem> Backups { get; } = new();


    public RestoreBackupItem? SelectedBackup
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _prepareTask = PrepareAsync(value?.FilePath);
            }
        }
    }


    /// <summary>0 = 恢复到备份时的状态，1 = 只补回缺少的记录。</summary>
    public int ModeIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(RestoreButtonText));
            }
        }
    }


    public string RestoreButtonText => ModeIndex == 0 ? Lang.RestoreBackupDialog_RestoreAndRestart : Lang.FileSettingPage_Restore;

    public bool HasBackups { get; set => SetProperty(ref field, value); }

    public bool ShowNoBackupHint { get; set => SetProperty(ref field, value); }

    public bool CanOperate { get; set => SetProperty(ref field, value); } = true;

    public bool CanRestore { get; set => SetProperty(ref field, value); }

    public bool IsBusy { get; set => SetProperty(ref field, value); }

    public string? ProgressText { get; set => SetProperty(ref field, value); }

    /// <summary>选中的备份检查了一会儿还没出结果。</summary>
    public bool IsChecking
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(PrepareResultOpacity));
            }
        }
    }

    /// <summary>检查中隐去上一份的结果，但保留它占的高度。</summary>
    public double PrepareResultOpacity => IsChecking ? 0 : 1;

    public string? PreparedInfoText { get; set => SetProperty(ref field, value); }

    public bool PreparedIsLegacy { get; set => SetProperty(ref field, value); }

    /// <summary>选中的备份不能还原的原因。</summary>
    public string? PrepareErrorText { get; set => SetProperty(ref field, value); }

    public string? ResultText { get; set => SetProperty(ref field, value); }

    public string? ErrorText { get; set => SetProperty(ref field, value); }



    private async void ContentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        try
        {
            List<BackupFileInfo> files = await Task.Run(DatabaseBackupService.ListBackups);
            foreach (BackupFileInfo file in files)
            {
                Backups.Add(RestoreBackupItem.From(file));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List database backups");
        }
        HasBackups = Backups.Count > 0;
        ShowNoBackupHint = !HasBackups;
        SelectedBackup = Backups.FirstOrDefault();
    }



    /// <summary>
    /// 选别处的备份文件，加到列表最前面并选中。
    /// </summary>
    [RelayCommand]
    private async Task BrowseAsync()
    {
        try
        {
            string? file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, ("7z", ".7z"), ("SQLite", ".db"));
            if (!File.Exists(file))
            {
                return;
            }
            RestoreBackupItem? existing = Backups.FirstOrDefault(x => string.Equals(x.FilePath, file, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = RestoreBackupItem.FromFile(file);
                Backups.Insert(0, existing);
            }
            HasBackups = true;
            ShowNoBackupHint = false;
            SelectedBackup = existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pick backup file");
        }
    }



    /// <summary>
    /// 检查选中的备份：解压、校验、升级到当前版本。换了选择时丢掉上一份副本。
    /// </summary>
    /// <remarks>
    /// 检查期间先留着上一份的结果和按钮状态，超过 <see cref="CheckingIndicatorDelayMs"/> 才换成进度，
    /// 以免检查只要几十毫秒时界面闪一下；这段时间里点还原，由 <see cref="RestoreAsync"/> 等检查完成。
    /// </remarks>
    /// <param name="file">备份文件；为空时只清掉状态。</param>
    private async Task PrepareAsync(string? file)
    {
        int version = ++_prepareVersion;
        DatabaseBackupService.DeletePrepared(_prepared?.DatabaseFile);
        _prepared = null;
        ErrorText = null;
        ResultText = null;
        if (string.IsNullOrWhiteSpace(file))
        {
            CanRestore = false;
            IsChecking = false;
            SetPrepareResult(null, false, null);
            return;
        }
        Task<PreparedBackup> task = Task.Run(() => DatabaseBackupService.PrepareRestore(file));
        if (await Task.WhenAny(task, Task.Delay(CheckingIndicatorDelayMs)) != task && version == _prepareVersion)
        {
            IsChecking = true;
            CanRestore = false;
        }
        try
        {
            PreparedBackup prepared = await task;
            if (version != _prepareVersion)
            {
                // 检查期间又换了选择，这份已经没用了
                DatabaseBackupService.DeletePrepared(prepared.DatabaseFile);
                return;
            }
            _prepared = prepared;
            SetPrepareResult(GetInfoText(prepared), prepared.IsLegacy, null);
            CanRestore = true;
        }
        catch (DatabaseBackupException ex)
        {
            if (version == _prepareVersion)
            {
                _logger.LogWarning(ex, "Backup {file} can not be restored: {kind}", file, ex.Kind);
                SetPrepareResult(null, false, GetErrorMessage(ex.Kind));
                CanRestore = false;
            }
        }
        catch (Exception ex)
        {
            if (version == _prepareVersion)
            {
                _logger.LogError(ex, "Prepare backup {file}", file);
                SetPrepareResult(null, false, string.Format(Lang.RestoreBackupDialog_Error_Failed, ex.Message));
                CanRestore = false;
            }
        }
        finally
        {
            if (version == _prepareVersion)
            {
                IsChecking = false;
            }
        }
    }


    /// <summary>
    /// 一次换掉备份下方显示的检查结果，几行在同一轮布局里一起变化。
    /// </summary>
    /// <param name="info">备份信息；检查失败时为空。</param>
    /// <param name="legacy">是否显示旧版整库备份的说明。</param>
    /// <param name="error">不能还原的原因；检查通过时为空。</param>
    private void SetPrepareResult(string? info, bool legacy, string? error)
    {
        PreparedInfoText = info;
        PreparedIsLegacy = legacy;
        PrepareErrorText = error;
    }



    /// <summary>
    /// 按所选方式还原：覆盖还原交给重启后的新进程，合并还原在后台做完后显示补回的条数。
    /// </summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        // 刚换了选择、检查还没出结果时按钮仍可点，等这次检查完成再继续
        if (_prepared is null && _prepareTask is { IsCompleted: false } pending)
        {
            await pending;
        }
        if (_prepared is not PreparedBackup prepared)
        {
            // 检查失败时原因已经显示在备份下方
            if (PrepareErrorText is null)
            {
                ErrorText = Lang.RestoreBackupDialog_SelectBackupFirst;
            }
            return;
        }
        ErrorText = null;
        ResultText = null;
        if (ModeIndex == 0)
        {
            // 记录进程还在往时长表里写，整表替换会和它打架
            if (PlayTimeRecordService.IsAnyRecorderRunning())
            {
                ErrorText = Lang.RestoreBackupDialog_Error_RecorderRunning;
                return;
            }
            try
            {
                AppInstance.GetCurrent().UnregisterKey();
                DatabaseBackupService.RestartToRestore(prepared);
                _handedOver = true;
                // 即将自行拉起新进程，不能在退出时应用更新，否则会与新实例争用 current\
                App.Current.Exit(applyPendingUpdate: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restart to restore backup");
                ErrorText = string.Format(Lang.RestoreBackupDialog_Error_Failed, ex.Message);
            }
            return;
        }

        CanOperate = false;
        CanRestore = false;
        IsBusy = true;
        ProgressText = Lang.RestoreBackupDialog_Merging;
        try
        {
            LanSyncCounts result = await Task.Run(() => DatabaseBackupService.ApplyMerge(prepared.DatabaseFile));
            Merged = true;
            ResultText = result.Total == 0
                ? Lang.RestoreBackupDialog_MergeNothingNew
                : string.Format(Lang.RestoreBackupDialog_MergeCompleted, result.Gacha, result.GameRecord, result.PlayTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merge records from backup");
            ErrorText = string.Format(Lang.RestoreBackupDialog_Error_Failed, ex.Message);
        }
        finally
        {
            IsBusy = false;
            CanOperate = true;
            CanRestore = _prepared is not null;
        }
    }



    /// <summary>
    /// 备份信息：时间、程序版本、电脑名，旧备份只有时间。
    /// </summary>
    private static string GetInfoText(PreparedBackup prepared)
    {
        var parts = new List<string> { string.Format(Lang.RestoreBackupDialog_CreatedAt, prepared.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")) };
        if (!string.IsNullOrWhiteSpace(prepared.AppVersion))
        {
            parts.Add($"Moonward {prepared.AppVersion}");
        }
        if (!string.IsNullOrWhiteSpace(prepared.MachineName))
        {
            parts.Add(string.Format(Lang.RestoreBackupDialog_FromMachine, prepared.MachineName));
        }
        return string.Join(" · ", parts);
    }


    private static string GetErrorMessage(DatabaseBackupErrorKind kind)
    {
        return kind switch
        {
            DatabaseBackupErrorKind.Corrupted => Lang.RestoreBackupDialog_Error_Corrupted,
            DatabaseBackupErrorKind.NewerVersion => Lang.RestoreBackupDialog_Error_NewerVersion,
            DatabaseBackupErrorKind.StarwardDatabase => Lang.RestoreBackupDialog_Error_Starward,
            _ => Lang.RestoreBackupDialog_Error_InvalidFile,
        };
    }



    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }


    private void ContentDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        // 让还在检查的那次结果作废，它完成后会自己删掉副本
        _prepareVersion++;
        if (!_handedOver)
        {
            DatabaseBackupService.DeletePrepared(_prepared?.DatabaseFile);
            _prepared = null;
        }
    }

}



/// <summary>
/// 还原对话框列表里的一项。
/// </summary>
/// <remarks>XAML 生成的类型信息要求无参构造与可写属性，不能用 required / init。</remarks>
public sealed class RestoreBackupItem
{

    public string FilePath { get; set; } = string.Empty;

    /// <summary>备份时间，取不到时为文件名。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>类型 · 大小 · 所在目录。</summary>
    public string Description { get; set; } = string.Empty;


    internal static RestoreBackupItem From(BackupFileInfo file)
    {
        string kind = GetKindText(file.Kind);
        if (file.IsLegacy && file.Kind is not BackupKind.LegacyAuto)
        {
            kind = $"{kind} · {Lang.DatabaseBackup_LegacyFormat}";
        }
        return new RestoreBackupItem
        {
            FilePath = file.Path,
            Title = file.Time.ToString("yyyy-MM-dd HH:mm:ss"),
            Description = $"{kind} · {FormatSize(file.Size)} · {Path.GetDirectoryName(file.Path)}",
        };
    }


    /// <summary>
    /// 用户从别处选的文件：认得出命名的按备份显示，否则显示文件名。
    /// </summary>
    internal static RestoreBackupItem FromFile(string file)
    {
        long size = new FileInfo(file).Length;
        if (DatabaseBackupNaming.TryParse(Path.GetFileName(file), out DateTime time, out BackupKind kind, out bool legacy))
        {
            return From(new BackupFileInfo(file, time, kind, legacy, size));
        }
        return new RestoreBackupItem
        {
            FilePath = file,
            Title = Path.GetFileName(file),
            Description = $"{FormatSize(size)} · {Path.GetDirectoryName(file)}",
        };
    }


    private static string GetKindText(BackupKind kind)
    {
        return kind switch
        {
            BackupKind.BeforeUpgrade => Lang.DatabaseBackup_Kind_BeforeUpgrade,
            BackupKind.BeforeRestore => Lang.DatabaseBackup_Kind_BeforeRestore,
            BackupKind.BeforeLanSync => Lang.DatabaseBackup_Kind_BeforeLanSync,
            BackupKind.LegacyAuto => Lang.DatabaseBackup_Kind_LegacyAuto,
            _ => Lang.DatabaseBackup_Kind_Manual,
        };
    }


    private static string FormatSize(long bytes)
    {
        if (bytes < 1L << 20)
        {
            return $"{bytes / 1024.0:F1} KB";
        }
        return $"{bytes / (double)(1 << 20):F1} MB";
    }

}
