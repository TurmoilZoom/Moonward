using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Input;
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


    /// <summary>静止显示首帧，避免 AutoPlay 常驻循环。</summary>
    protected override void OnLoaded()
    {
        Lottie_AboutLogo.SetProgress(0);
    }


    /// <summary>离开页面时停止动画，避免卸载后仍占用合成资源。</summary>
    protected override void OnUnloaded()
    {
        Lottie_AboutLogo.Stop();
    }


    /// <summary>
    /// 悬浮时播放一次 Lottie 后停在末帧；离开后回到首帧。
    /// </summary>
    private void Lottie_AboutLogo_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = Lottie_AboutLogo.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 指针离开后停止动画并回到首帧。
    /// </summary>
    private void Lottie_AboutLogo_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Lottie_AboutLogo.Stop();
        Lottie_AboutLogo.SetProgress(0);
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
    /// 是否在启动时自动推送新版本可用弹窗。
    /// </summary>
    public bool EnableUpdateNotification
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableUpdateNotification = value;
            }
        }
    } = AppConfig.EnableUpdateNotification;


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
