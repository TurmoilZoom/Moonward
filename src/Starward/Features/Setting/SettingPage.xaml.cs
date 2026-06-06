using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Starward.Controls;
using Starward.Frameworks;
using System;


namespace Starward.Features.Setting;

public sealed partial class SettingPage : PageBase
{


    private readonly ILogger<SettingPage> _logger = AppConfig.GetLogger<SettingPage>();


    public SettingPage()
    {
        this.InitializeComponent();
        // 在首次导航前订阅，使「关于」页（默认落地页）的入场同样触发级联动画
        Frame_Setting.Navigated += Frame_Setting_Navigated;
        Frame_Setting.Navigate(typeof(AboutSetting));
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => this.Bindings.Update());
    }



    /// <summary>
    /// 每次切换设置页（含首次进入），在新页面加载后对其内容区播放「逐个错峰、上滑 + 淡入」级联入场动画。
    /// 设置页均为全新的 Frame 实例，因此 Loaded 在每次导航时都会触发，无需各页单独接线。
    /// </summary>
    private void Frame_Setting_Navigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is Page page)
        {
            if (page.IsLoaded)
            {
                EntranceAnimation.Play(page);
            }
            else
            {
                void OnPageLoaded(object s, RoutedEventArgs args)
                {
                    page.Loaded -= OnPageLoaded;
                    EntranceAnimation.Play(page);
                }
                page.Loaded += OnPageLoaded;
            }
        }
    }



    private readonly FluidNavigationViewHoverEffect _navHoverEffect = new();


    protected override void OnLoaded()
    {
        _navHoverEffect.Attach(NavView, NavIndicatorHost, _logger);
    }



    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            Type? type = args.InvokedItemContainer?.Tag switch
            {
                nameof(AboutSetting) => typeof(AboutSetting),
                nameof(GeneralSetting) => typeof(GeneralSetting),
                nameof(DownloadSetting) => typeof(DownloadSetting),
                nameof(FileManageSetting) => typeof(FileManageSetting),
                nameof(ScreenshotSetting) => typeof(ScreenshotSetting),
                nameof(AdvancedSetting) => typeof(AdvancedSetting),
                nameof(ToolboxSetting) => typeof(ToolboxSetting),
                nameof(HotkeySetting) => typeof(HotkeySetting),
                nameof(GamepadControlSetting) => typeof(GamepadControlSetting),
                _ => null,
            };
            if (type is not null)
            {
                Frame_Setting.Navigate(type);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting page navigate.");
        }
    }



    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _navHoverEffect.Detach();
    }



}
