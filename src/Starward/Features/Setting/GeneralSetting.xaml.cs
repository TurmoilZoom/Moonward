using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Features.ViewHost;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Globalization;
using Windows.System;


namespace Starward.Features.Setting;

public sealed partial class GeneralSetting : PageBase
{

    private readonly ILogger<GeneralSetting> _logger = AppConfig.GetLogger<GeneralSetting>();


    public GeneralSetting()
    {
        this.InitializeComponent();
    }



    protected override void OnLoaded()
    {
        InitializeLanguageSelector();
        InitializeCloseWindowOption();
        InitializeStartAtLogin();
    }




    #region 语言



    private bool _languageInitialized;


    /// <summary>
    /// 语言
    /// </summary>
    private void InitializeLanguageSelector()
    {
        try
        {
            var lang = AppConfig.Language;
            ComboBox_Language.Items.Clear();
            ComboBox_Language.Items.Add(new ComboBoxItem
            {
                Content = Lang.ResourceManager.GetString(nameof(Lang.SettingPage_FollowSystem), CultureInfo.InstalledUICulture),
                Tag = "",
            });
            ComboBox_Language.SelectedIndex = 0;
            foreach (var (Title, LangCode) in Localization.LanguageList)
            {
                var box = new ComboBoxItem
                {
                    Content = Title,
                    Tag = LangCode,
                };
                ComboBox_Language.Items.Add(box);
                if (LangCode == lang)
                {
                    ComboBox_Language.SelectedItem = box;
                }
            }
        }
        finally
        {
            _languageInitialized = true;
        }
    }



    /// <summary>
    /// 语言切换
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ComboBox_Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (ComboBox_Language.SelectedItem is ComboBoxItem item)
            {
                if (_languageInitialized)
                {
                    var lang = item.Tag as string;
                    _logger.LogInformation("Language change to {lang}", lang);
                    AppConfig.SetLanguage(lang);
                    this.Bindings.Update();
                    RefreshStartGameActionSelectionBox();
                    WeakReferenceMessenger.Default.Send(new LanguageChangedMessage());
                    AppConfig.SaveConfiguration();
                }
            }
        }
        catch (CultureNotFoundException)
        {
            AppConfig.SetLanguage(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change Language");
        }
    }



    #endregion



    #region 游戏启动后


    /// <summary>
    /// 启动游戏后的操作（全局设置）。原位于「游戏设置 - 基本信息」，现移到「常规」「关闭窗口选项」之上。
    /// </summary>
    public int StartGameAction
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !_suppressStartGameActionSave)
            {
                AppConfig.StartGameAction = (Starward.Features.GameLauncher.StartGameAction)value;
            }
        }
    } = Math.Clamp((int)AppConfig.StartGameAction, 0, 2);


    /// <summary>
    /// 语言切换刷新「已选项」显示文本时临时抑制写库（避免把过渡值 -1 持久化到 Setting 表）。
    /// </summary>
    private bool _suppressStartGameActionSave;


    /// <summary>
    /// 语言切换后强制刷新「游戏启动后」ComboBox 折叠态显示的文本。
    /// WinUI ComboBox 折叠时显示的是缓存的 SelectionBoxItem，Bindings.Update() 只会刷新下拉列表项的内容，
    /// 不会刷新折叠态显示的文本（否则需手动展开下拉框才生效）。这里通过重新选择一次触发内部 UpdateSelectionBoxItem。
    /// </summary>
    private void RefreshStartGameActionSelectionBox()
    {
        int index = ComboBox_StartGameAction.SelectedIndex;
        if (index < 0)
        {
            return;
        }
        try
        {
            _suppressStartGameActionSave = true;
            ComboBox_StartGameAction.SelectedIndex = -1;
            ComboBox_StartGameAction.SelectedIndex = index;
        }
        finally
        {
            _suppressStartGameActionSave = false;
        }
    }


    #endregion



    #region 关闭窗口选项



    private bool _closeWindowOptionInitialized;



    /// <summary>
    /// 初始化关闭窗口选项
    /// </summary>
    private void InitializeCloseWindowOption()
    {
        try
        {
            var option = AppConfig.CloseWindowOption;
            if (option is MainWindowCloseOption.Exit)
            {
                RadioButton_CloseWindowOption_Exit.IsChecked = true;
            }
            else
            {
                // 默认（含未设置）UI 上选中「最小化到系统托盘」；不写入配置，故首次关闭窗口仍会弹询问框
                RadioButton_CloseWindowOption_Hide.IsChecked = true;
            }
            _closeWindowOptionInitialized = true;
        }
        catch { }
    }



    /// <summary>
    /// 关闭窗口选项切换
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void RadioButton_CloseWindowOption_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_closeWindowOptionInitialized)
            {
                if (sender is FrameworkElement fe)
                {
                    AppConfig.CloseWindowOption = fe.Tag switch
                    {
                        MainWindowCloseOption option => option,
                        _ => 0,
                    };
                }
            }
        }
        catch { }
    }



    #endregion



    #region 开机启动


    private bool _startAtLoginInitialized;


    /// <summary>
    /// 可移动存储上不允许注册开机启动。
    /// </summary>
    public bool StartAtLoginAvailable { get; } = AutoStartService.IsAvailable;


    /// <summary>可移动存储上显示不可用说明。</summary>
    public Visibility StartAtLoginUnavailableVisibility => StartAtLoginAvailable ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>
    /// 是否已在系统中启用开机启动（以 Run 键 + StartupApproved 为准）。
    /// </summary>
    public bool StartAtLogin
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && _startAtLoginInitialized)
            {
                ApplyStartAtLogin(value);
            }
        }
    }


    /// <summary>
    /// 从系统注册状态同步开关，不在此时写回注册表。
    /// </summary>
    private void InitializeStartAtLogin()
    {
        try
        {
            StartAtLogin = StartAtLoginAvailable && AutoStartService.IsEnabled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize start-at-login");
        }
        finally
        {
            _startAtLoginInitialized = true;
        }
    }


    /// <summary>
    /// 把开关写到系统 Run 键；失败时拨回开关并提示。
    /// </summary>
    /// <param name="value">是否启用开机启动。</param>
    private void ApplyStartAtLogin(bool value)
    {
        try
        {
            if (!StartAtLoginAvailable)
            {
                return;
            }
            if (value)
            {
                AutoStartService.Enable();
            }
            else
            {
                AutoStartService.Disable();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply start-at-login");
            _startAtLoginInitialized = false;
            StartAtLogin = !value;
            _startAtLoginInitialized = true;
            InAppToast.MainWindow?.Error(Lang.SettingPage_StartAtLoginFailed, ex.Message);
        }
    }


    /// <summary>
    /// 打开 Windows「启动应用」设置页。
    /// </summary>
    private async void Hyperlink_StartupApps_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender, Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        await Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
    }


    #endregion




    #region 系统视觉效果



    /// <summary>
    /// 透明/动画效果
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    private async void Hyperlink_VisualEffects_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender, Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        await Launcher.LaunchUriAsync(new Uri("ms-settings:easeofaccess-visualeffects"));
    }



    #endregion



}
