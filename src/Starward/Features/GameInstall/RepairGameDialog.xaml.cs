using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameSelector;
using Starward.RPC;
using Starward.RPC.GameInstall;
using System;
using System.IO;
using System.Threading.Tasks;


namespace Starward.Features.GameInstall;

/// <summary>
/// 修复游戏：与官方启动器一样先选修复对象，游戏资源与千星沙箱分开修复。
/// 游戏资源按本地已安装的语音包修复，不再让用户选择语言。
/// </summary>
public sealed partial class RepairGameDialog : ContentDialog
{


    private readonly ILogger<RepairGameDialog> _logger = AppConfig.GetLogger<RepairGameDialog>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();



    public RepairGameDialog()
    {
        this.InitializeComponent();
    }



    public GameId CurrentGameId { get; set; }

    /// <summary>
    /// 游戏安装目录，打开前由调用方确认存在
    /// </summary>
    public string InstallPath { get; set; } = "";

    /// <summary>
    /// 游戏图标，显示在「游戏资源」一行
    /// </summary>
    public GameBizIcon? CurrentGameBizIcon { get; set; }

    /// <summary>
    /// 游戏是否带千星沙箱（WPF 包），有才显示千星沙箱一行
    /// </summary>
    public bool HasWPFPackage { get; set; }



    /// <summary>
    /// 修复游戏资源：范围是游戏本体和本地已安装的语音包，与官方启动器一致
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task RepairGameResourcesAsync()
    {
        try
        {
            if (!Directory.Exists(InstallPath))
            {
                return;
            }
            AudioLanguage audio = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId, InstallPath);
            Telemetry.Track("game_setting_click", CurrentGameId.GameBiz, ("button", "repair_start"), ("audio", audio));
            GameInstallContext? task = await _gameInstallService.StartRepairAsync(CurrentGameId, InstallPath, audio);
            OnTaskStarted(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repair game resources {GameBiz}", CurrentGameId.GameBiz);
        }
    }



    /// <summary>
    /// 只修复千星沙箱：在后台按包内的文件清单校验，有缺失或损坏时重新下载整包，不占用开始游戏按钮
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task RepairWPFPackageAsync()
    {
        try
        {
            if (!Directory.Exists(InstallPath))
            {
                return;
            }
            Telemetry.Track("game_setting_click", CurrentGameId.GameBiz, ("button", "repair_wpf"));
            GameInstallContext? task = await _gameInstallService.StartRepairWPFPackageAsync(CurrentGameId, InstallPath);
            OnTaskStarted(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repair WPF package {GameBiz}", CurrentGameId.GameBiz);
        }
    }



    /// <summary>
    /// 任务开始后通知首页显示进度并关闭对话框；RPC 服务没起来或任务直接失败时留在对话框
    /// </summary>
    /// <param name="task">刚开始的任务</param>
    private void OnTaskStarted(GameInstallContext? task)
    {
        if (task is not null && task.State is not GameInstallState.Stop and not GameInstallState.Error)
        {
            WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
            this.Hide();
        }
    }



    [RelayCommand]
    private void Cancel()
    {
        this.Hide();
    }


}
