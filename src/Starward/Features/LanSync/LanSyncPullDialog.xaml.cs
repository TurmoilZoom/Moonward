using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.LanSync;

/// <summary>
/// 从局域网内另一台设备同步：搜索共享端（或手动输入地址），输入对方显示的验证码后拉取并合并记录。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class LanSyncPullDialog : ContentDialog
{

    private readonly ILogger<LanSyncPullDialog> _logger = AppConfig.GetLogger<LanSyncPullDialog>();

    private readonly LanSyncService _lanSyncService = AppConfig.GetService<LanSyncService>();

    /// <summary>关闭对话框时取消搜索与接收；合并一旦开始不受影响。</summary>
    private readonly CancellationTokenSource _cts = new();

    private bool _loaded;

    private bool _searched;



    public LanSyncPullDialog()
    {
        this.InitializeComponent();
    }



    /// <summary>本次打开期间是否成功合并过（不论是否有新增记录）。</summary>
    public bool Synced { get; private set; }


    public string ShareHintText { get; } = string.Format(Lang.LanSync_OpenShareFirst, Lang.Common_Setting, Lang.SettingPage_FileManagement, Lang.LanSync_ShareThisDevice);


    public ObservableCollection<LanSyncPeer> Peers { get; } = new();


    public LanSyncPeer? SelectedPeer
    {
        get;
        set
        {
            // 选了列表里的设备就清空手动地址，两者只用其一
            if (SetProperty(ref field, value) && value is not null)
            {
                ManualAddress = string.Empty;
            }
        }
    }


    public string ManualAddress
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !string.IsNullOrWhiteSpace(value))
            {
                SelectedPeer = null;
            }
        }
    } = string.Empty;


    public string Code { get; set => SetProperty(ref field, value); } = string.Empty;

    public bool SyncGacha { get; set => SetProperty(ref field, value); } = true;

    public bool SyncGameRecord { get; set => SetProperty(ref field, value); } = true;

    public bool SyncPlayTime { get; set => SetProperty(ref field, value); } = true;

    public bool IsSearching { get; set => SetProperty(ref field, value); }

    public bool HasPeers { get; set => SetProperty(ref field, value); }

    public bool ShowNoDeviceHint { get; set => SetProperty(ref field, value); }

    public bool CanOperate { get; set => SetProperty(ref field, value); } = true;

    public bool IsSyncing { get; set => SetProperty(ref field, value); }

    public bool ProgressIsIndeterminate { get; set => SetProperty(ref field, value); } = true;

    public double ProgressValue { get; set => SetProperty(ref field, value); }

    public string? ProgressText { get; set => SetProperty(ref field, value); }

    public string? ResultText { get; set => SetProperty(ref field, value); }

    public string? ErrorText { get; set => SetProperty(ref field, value); }



    private async void ContentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        await SearchAsync();
    }



    /// <summary>
    /// 搜索局域网内正在共享的设备；只找到一台且没填手动地址时自动选中。
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsSearching)
        {
            return;
        }
        IsSearching = true;
        ShowNoDeviceHint = false;
        try
        {
            List<LanSyncPeer> peers = await _lanSyncService.DiscoverAsync(_cts.Token);
            // 重置列表会清掉选中项，先记下来再恢复
            string? selected = SelectedPeer?.EndPoint.ToString();
            Peers.Clear();
            foreach (LanSyncPeer peer in peers)
            {
                Peers.Add(peer);
            }
            HasPeers = Peers.Count > 0;
            SelectedPeer = Peers.FirstOrDefault(x => x.EndPoint.ToString() == selected)
                           ?? (Peers.Count == 1 && string.IsNullOrWhiteSpace(ManualAddress) ? Peers[0] : null);
            _searched = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discover LAN sync devices");
            ErrorText = string.Format(Lang.LanSync_Error_Failed, ex.Message);
        }
        finally
        {
            IsSearching = false;
            ShowNoDeviceHint = _searched && Peers.Count == 0;
        }
    }



    /// <summary>
    /// 校验输入后拉取并合并。
    /// </summary>
    [RelayCommand]
    private async Task SyncAsync()
    {
        ErrorText = null;
        ResultText = null;
        LanSyncCategories categories = LanSyncCategories.None;
        if (SyncGacha)
        {
            categories |= LanSyncCategories.Gacha;
        }
        if (SyncGameRecord)
        {
            categories |= LanSyncCategories.GameRecord;
        }
        if (SyncPlayTime)
        {
            categories |= LanSyncCategories.PlayTime;
        }
        if (categories is LanSyncCategories.None)
        {
            ErrorText = Lang.LanSync_SelectDataFirst;
            return;
        }
        if (SelectedPeer is null && string.IsNullOrWhiteSpace(ManualAddress))
        {
            ErrorText = Lang.LanSync_SelectDeviceFirst;
            return;
        }
        string code = new string(Code.Where(char.IsAsciiDigit).ToArray());
        if (code.Length != LanSyncProtocol.CodeLength)
        {
            ErrorText = Lang.LanSync_EnterCodeFirst;
            return;
        }

        CanOperate = false;
        IsSyncing = true;
        ProgressIsIndeterminate = true;
        ProgressValue = 0;
        ProgressText = null;
        string target = SelectedPeer?.DeviceName ?? ManualAddress.Trim();
        try
        {
            LanSyncPeer peer = SelectedPeer ?? await _lanSyncService.ConnectAsync(ManualAddress, _cts.Token);
            target = peer.DeviceName;
            if (peer.Protocol != LanSyncProtocol.Version)
            {
                throw new LanSyncException(LanSyncErrorKind.ProtocolMismatch);
            }
            var progress = new Progress<LanSyncProgress>(OnProgress);
            LanSyncCounts result = await _lanSyncService.PullAsync(peer, code, categories, progress, _cts.Token);
            Synced = true;
            ResultText = result.Total == 0
                ? Lang.LanSync_NothingNew
                : string.Format(Lang.LanSync_Completed, result.Gacha, result.GameRecord, result.PlayTime);
        }
        catch (OperationCanceledException) { }
        catch (LanSyncException ex)
        {
            _logger.LogWarning(ex, "LAN sync from {target} failed: {kind}", target, ex.Kind);
            ErrorText = GetErrorMessage(ex, target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LAN sync from {target} failed", target);
            ErrorText = string.Format(Lang.LanSync_Error_Failed, ex.Message);
        }
        finally
        {
            IsSyncing = false;
            CanOperate = true;
        }
    }


    private void OnProgress(LanSyncProgress progress)
    {
        if (progress.Stage is LanSyncStage.Receiving)
        {
            ProgressIsIndeterminate = progress.BytesTotal <= 0;
            ProgressValue = progress.BytesTotal <= 0 ? 0 : progress.BytesReceived * 100.0 / progress.BytesTotal;
            ProgressText = string.Format(Lang.LanSync_Receiving, $"{FormatSize(progress.BytesReceived)} / {FormatSize(progress.BytesTotal)}");
        }
        else
        {
            ProgressIsIndeterminate = true;
            ProgressText = Lang.LanSync_Merging;
        }
    }


    private static string GetErrorMessage(LanSyncException ex, string target)
    {
        return ex.Kind switch
        {
            LanSyncErrorKind.InvalidAddress => Lang.LanSync_Error_InvalidAddress,
            LanSyncErrorKind.ConnectFailed => string.Format(Lang.LanSync_Error_ConnectFailed, target),
            LanSyncErrorKind.InvalidCode => Lang.LanSync_Error_InvalidCode,
            LanSyncErrorKind.Locked => Lang.LanSync_Error_Locked,
            LanSyncErrorKind.ProtocolMismatch => Lang.LanSync_Error_ProtocolMismatch,
            _ => string.Format(Lang.LanSync_Error_Failed, ex.Message),
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



    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }


    private void ContentDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _cts.Cancel();
    }

}
