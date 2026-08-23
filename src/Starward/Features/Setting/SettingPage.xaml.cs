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

    /// <summary>导航参数：滚动到「常规」页的开机启动一节。</summary>
    public const string StartAtLoginSection = "StartAtLogin";


    private readonly ILogger<SettingPage> _logger = AppConfig.GetLogger<SettingPage>();


    public SettingPage()
    {
        this.InitializeComponent();
        // 在首次导航前订阅，使「常规」页（默认落地页）的入场同样触发级联动画
        Frame_Setting.Navigated += Frame_Setting_Navigated;
        Frame_Setting.Navigate(typeof(GeneralSetting));
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => this.Bindings.Update());
    }


    /// <summary>
    /// 处理来自其它页面的深链：例如签到提示跳转到开机启动设置。
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string section && section == StartAtLoginSection)
        {
            ShowStartAtLoginSection();
        }
    }


    /// <summary>
    /// 切到「常规」子页并滚动到开机启动一节。
    /// </summary>
    private void ShowStartAtLoginSection()
    {
        if (Frame_Setting.CurrentSourcePageType != typeof(GeneralSetting))
        {
            Frame_Setting.Navigate(typeof(GeneralSetting));
            if (NavView.MenuItems.Count > 0 && NavView.MenuItems[0] is NavigationViewItem generalItem)
            {
                NavView.SelectedItem = generalItem;
            }
        }

        if (Frame_Setting.Content is GeneralSetting general)
        {
            general.ScrollToStartAtLogin();
        }
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



    /// <summary>
    /// 设置侧栏导航项被点选时，切换到对应设置子页。
    /// </summary>
    /// <param name="sender">触发事件的 NavigationView。</param>
    /// <param name="args">包含被点选项容器（Tag 为子页类型名）的事件参数。</param>
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
            // 重复点击当前项时 ItemInvoked 仍会触发；跳过同页 Navigate，避免重新入场动画
            if (type is not null && Frame_Setting.CurrentSourcePageType != type)
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
