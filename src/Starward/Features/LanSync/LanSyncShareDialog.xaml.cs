using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Starward.Features.LanSync;

/// <summary>
/// 共享本机数据：显示验证码与本机地址，窗口打开期间允许其他设备拉取本机记录，关闭即停止共享。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class LanSyncShareDialog : ContentDialog
{

    /// <summary>对方可能要照抄地址，只列前几个，虚拟网卡多时不至于刷屏。</summary>
    private const int MaxDisplayAddresses = 3;


    private readonly ILogger<LanSyncShareDialog> _logger = AppConfig.GetLogger<LanSyncShareDialog>();

    private LanSyncServer? _server;

    private bool _started;



    public LanSyncShareDialog()
    {
        this.InitializeComponent();
    }



    public string DescriptionText { get; } = string.Format(Lang.LanSync_ShareDialogDescription, Lang.LanSync_SyncFromOtherDevice);

    public bool IsSharing { get; set => SetProperty(ref field, value); }

    public bool IsWaiting { get; set => SetProperty(ref field, value); }

    public string? CodeText { get; set => SetProperty(ref field, value); }

    public string? DeviceText { get; set => SetProperty(ref field, value); }

    public string SummaryText { get; set => SetProperty(ref field, value); } = "…";

    public string? StatusText { get; set => SetProperty(ref field, value); }

    public string? ErrorText { get; set => SetProperty(ref field, value); }

    public string DismissButtonText { get; set => SetProperty(ref field, value); } = Lang.LanSync_StopSharing;



    private void ContentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded 可能不止一次触发，共享只开一次
        if (_started)
        {
            return;
        }
        _started = true;
        try
        {
            _server = AppConfig.GetService<LanSyncService>().StartSharing();
            _server.EventRaised += Server_EventRaised;
            CodeText = $"{_server.Code[..3]} {_server.Code[3..]}";
            DeviceText = BuildDeviceText(_server);
            IsSharing = true;
            IsWaiting = true;
            StatusText = Lang.LanSync_WaitingForConnection;
            if (!_server.IsDiscoveryAvailable)
            {
                ErrorText = Lang.LanSync_DiscoveryUnavailable;
            }
            _ = LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start LAN sync sharing");
            ErrorText = string.Format(Lang.LanSync_StartSharingFailed, ex.Message);
            DismissButtonText = Lang.Common_Close;
        }
    }


    /// <summary>
    /// 统计本机可共享的记录数，配合界面上的数据文件夹路径，让用户在对方同步前就能看出共享的是哪份数据。
    /// </summary>
    private async Task LoadSummaryAsync()
    {
        try
        {
            LanSyncCounts counts = await AppConfig.GetService<LanSyncService>().GetLocalSummaryAsync();
            SummaryText = string.Format(Lang.LanSync_Counts, counts.Gacha.ToString("N0"), counts.GameRecord.ToString("N0"), counts.PlayTime.ToString("N0"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Count LAN sync shareable records");
            SummaryText = string.Empty;
        }
    }


    /// <summary>
    /// 设备名加上本机地址，每个地址一行。
    /// </summary>
    private static string BuildDeviceText(LanSyncServer server)
    {
        List<string> addresses = LanSyncNetwork.GetLocalIPv4Addresses()
                                               .Select(x => LanSyncNetwork.FormatEndPoint(x.Address, server.Port))
                                               .Distinct()
                                               .Take(MaxDisplayAddresses)
                                               .ToList();
        return addresses.Count == 0 ? server.DeviceName : $"{server.DeviceName}\n{string.Join("\n", addresses)}";
    }


    /// <summary>
    /// 共享端事件在线程池线程上触发，切回 UI 线程再改绑定属性。
    /// </summary>
    private void Server_EventRaised(LanSyncServerEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.Kind)
            {
                case LanSyncServerEventKind.Sent:
                    IsWaiting = false;
                    StatusText = string.Format(Lang.LanSync_SentToDevice, string.IsNullOrWhiteSpace(e.DeviceName) ? e.Address : e.DeviceName, e.Address);
                    break;
                case LanSyncServerEventKind.Locked:
                    IsSharing = false;
                    IsWaiting = false;
                    StatusText = null;
                    ErrorText = Lang.LanSync_TooManyAttempts;
                    DismissButtonText = Lang.Common_Close;
                    break;
            }
        });
    }


    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }


    private void ContentDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (_server is not null)
        {
            _server.EventRaised -= Server_EventRaised;
            _server.Dispose();
            _server = null;
        }
    }

}
