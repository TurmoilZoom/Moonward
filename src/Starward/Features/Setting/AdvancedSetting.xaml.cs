using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Features.GameLauncher;
using Starward.Features.RPC;
using Starward.Features.UrlProtocol;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;


namespace Starward.Features.Setting;

public sealed partial class AdvancedSetting : PageBase
{

    private readonly ILogger<AdvancedSetting> _logger = AppConfig.GetLogger<AdvancedSetting>();


    private readonly RpcService _rpcService = AppConfig.GetService<RpcService>();


    public AdvancedSetting()
    {
        this.InitializeComponent();
    }



    protected override void OnLoaded()
    {
        _ = GetRpcServerStateAsync();
        CheckUrlProtocol();
    }





    #region URL Protocol



    [ObservableProperty]
    public bool _EnableUrlProtocol;


    partial void OnEnableUrlProtocolChanged(bool value)
    {
        try
        {
            if (value)
            {
                UrlProtocolService.RegisterProtocol();
            }
            else
            {
                UrlProtocolService.UnregisterProtocol();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enable url protocol changed");
        }
    }



    private async void CheckUrlProtocol()
    {
        try
        {
            var status = await Launcher.QueryUriSupportAsync(new Uri("moonward://"), LaunchQuerySupportType.Uri);
#pragma warning disable MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
            _EnableUrlProtocol = status is LaunchQuerySupportStatus.Available;
#pragma warning restore MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
            OnPropertyChanged(nameof(EnableUrlProtocol));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check url protocol");
        }
    }




    [RelayCommand]
    private async Task TestUrlProtocolAsync()
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri("moonward://test"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test url protocol");
        }
    }



    [RelayCommand]
    private void OpenUrlProtocolDoc()
    {
        new UrlProtocolDocWindow().Activate();
    }


    #endregion




    #region Elevated start-game tasks (skip UAC)


    /// <summary>
    /// 清理「关闭启动 UAC 提示」时注册的计划任务；先确认，可能弹一次 UAC。
    /// </summary>
    [RelayCommand]
    private async Task CleanupElevatedStartGameTasksAsync()
    {
        try
        {
            int found = ElevatedStartGameTaskService.ListStartGameTaskPaths().Count;
            if (found == 0)
            {
                await new ContentDialog
                {
                    Title = Lang.SettingPage_CleanElevatedStartGameTasks,
                    Content = Lang.StartGameMenu_CleanElevatedTasksNone,
                    CloseButtonText = Lang.Common_Confirm,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                }.ShowAsync();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = Lang.SettingPage_CleanElevatedStartGameTasks,
                Content = string.Format(Lang.StartGameMenu_CleanElevatedTasksConfirm, found),
                PrimaryButtonText = Lang.Common_Confirm,
                CloseButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };
            if (await dialog.ShowAsync() is not ContentDialogResult.Primary)
            {
                return;
            }

            ElevatedStartGameTaskService.CleanupResult result;
            try
            {
                result = ElevatedStartGameTaskService.CleanupAllStartGameTasks();
            }
            catch (Exception ex) when (ElevatedStartGameTaskService.IsElevationCancelled(ex))
            {
                _logger.LogInformation(ex, "Cleanup elevated start-game tasks cancelled by user");
                InAppToast.MainWindow?.Information(Lang.StartGameMenu_CleanElevatedTasksCancelled);
                return;
            }

            if (result.RemainingCount == 0)
            {
                InAppToast.MainWindow?.Success(string.Format(Lang.StartGameMenu_CleanElevatedTasksDone, result.DeletedCount));
            }
            else
            {
                InAppToast.MainWindow?.Warning(
                    string.Format(Lang.StartGameMenu_CleanElevatedTasksPartial, result.DeletedCount, result.RemainingCount));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup elevated start-game tasks from advanced setting");
            InAppToast.MainWindow?.Error(ex);
        }
    }


    #endregion




    #region RPC


    public bool KeepRpcServerRunningInBackground
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.KeepRpcServerRunningInBackground = value;
                SetRpcServerRunning(value);
            }
        }
    } = AppConfig.KeepRpcServerRunningInBackground;



    private void SetRpcServerRunning(bool value)
    {
        try
        {
            _rpcService.KeepRunningOnExited(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Set rpc server running");
        }
    }



    public int RPCServerProcessId { get; set => SetProperty(ref field, value); }



    private async Task GetRpcServerStateAsync()
    {
        try
        {
            RPCServerProcessId = 0;
            StackPanel_RpcState_NotRunning.Visibility = Visibility.Collapsed;
            StackPanel_RpcState_Running.Visibility = Visibility.Collapsed;
            StackPanel_RpcState_CannotConnect.Visibility = Visibility.Collapsed;
            if (RpcService.CheckRpcServerRunning())
            {
                var info = await _rpcService.GetRpcServerInfoAsync(DateTime.UtcNow.AddSeconds(3));
                RPCServerProcessId = info.ProcessId;
                StackPanel_RpcState_Running.Visibility = Visibility.Visible;
            }
            else
            {
                StackPanel_RpcState_NotRunning.Visibility = Visibility.Visible;
            }
        }
        catch (RpcException ex) when (ex.Status is { StatusCode: StatusCode.DeadlineExceeded })
        {
            int sessionId = Process.GetCurrentProcess().SessionId;
            var process = Process.GetProcessesByName("Moonward.RPC").FirstOrDefault(x => x.SessionId == sessionId);
            if (process != null)
            {
                RPCServerProcessId = process.Id;
                StackPanel_RpcState_CannotConnect.Visibility = Visibility.Visible;
            }
            else
            {
                StackPanel_RpcState_NotRunning.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get rpc server state");
        }
    }


    [RelayCommand]
    private async Task RunRpcServerAsync()
    {
        try
        {
            await _rpcService.EnsureRpcServerRunningAsync();
            await GetRpcServerStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run rpc server");
        }
    }



    [RelayCommand]
    private async Task StopRpcServerAsync()
    {
        try
        {
            await _rpcService.StopRpcServerAsync(DateTime.UtcNow.AddSeconds(3));
            await Task.Delay(1000);
            await GetRpcServerStateAsync();
        }
        catch (RpcException ex) when (ex.Status is { StatusCode: StatusCode.DeadlineExceeded })
        {
            try
            {
                var p = Process.GetProcessById(RPCServerProcessId);
                p.Kill();
                await Task.Delay(1000);
                await GetRpcServerStateAsync();
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stop rpc server");
        }
    }





    #endregion


}
