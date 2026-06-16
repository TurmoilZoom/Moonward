using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Starward.Features.Update;
using Starward.Frameworks;
using System;
using System.Threading.Tasks;


namespace Starward.Features.Setting;

public sealed partial class AboutSetting : PageBase
{


    private readonly ILogger<AboutSetting> _logger = AppConfig.GetLogger<AboutSetting>();


    public AboutSetting()
    {
        this.InitializeComponent();
    }




    /// <summary>
    /// 预览版
    /// </summary>
    public bool EnablePreviewRelease
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnablePreviewRelease = value;
            }
        }
    } = AppConfig.EnablePreviewRelease;


    /// <summary>
    /// 是最新版
    /// </summary>
    public string? LatestVersion { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 更新错误文本
    /// </summary>
    public string? UpdateErrorText { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 检查更新
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        try
        {
            LatestVersion = null;
            UpdateErrorText = null;
            var service = AppConfig.GetService<UpdateService>();
            var release = await service.GetLatestVersionAsync();
            if (release is not null)
            {
                new UpdateWindow { NewVersion = release }.Activate();
            }
            else if (service.IsUpdaterAvailable)
            {
                // 已是最新版本
                LatestVersion = AppConfig.AppVersion;
            }
            else
            {
                // 非 Velopack 部署（开发态/裸发布目录），无法检查更新
                UpdateErrorText = Lang.UpdateService_CannotUpdateAutomatically;
            }
        }
        catch (Exception ex)
        {
            UpdateErrorText = ex.Message;
            _logger.LogError(ex, "Check update");
        }
    }




}
