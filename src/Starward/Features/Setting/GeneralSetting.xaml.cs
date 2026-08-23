using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;
using Starward.Features.ViewHost;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Globalization;
using System.Numerics;
using Windows.System;


namespace Starward.Features.Setting;

public sealed partial class GeneralSetting : PageBase
{

    private readonly ILogger<GeneralSetting> _logger = AppConfig.GetLogger<GeneralSetting>();

    private readonly PointerEventHandler _startAtLoginDismissPointerHandler;


    public GeneralSetting()
    {
        this.InitializeComponent();
        _startAtLoginDismissPointerHandler = OnStartAtLoginDismissPointer;
    }



    protected override void OnLoaded()
    {
        InitializeLanguageSelector();
        InitializeCloseWindowOption();
        InitializeStartAtLogin();
        HideStartAtLoginAccent();
        if (_pendingScrollToStartAtLogin)
        {
            _pendingScrollToStartAtLogin = false;
            BringStartAtLoginIntoView();
        }
    }


    protected override void OnUnloaded()
    {
        DismissStartAtLoginAttention(animate: false);
        _startAtLoginAttentionTimer?.Stop();
        _startAtLoginAttentionTimer = null;
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

    private bool _pendingScrollToStartAtLogin;

    private bool _startAtLoginAttentionActive;

    private bool _startAtLoginDismissPointerHooked;

    /// <summary>滚动与入场结束后再压暗周围，避免和级联动画抢。</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _startAtLoginAttentionTimer;


    /// <summary>
    /// 把「开机启动」一节滚入可视区，并压暗其它项、用左侧色条标出标题 / 说明 / 开关。
    /// 页面尚未 Loaded 时会等到 Loaded 再滚。
    /// </summary>
    public void ScrollToStartAtLogin()
    {
        if (IsLoaded)
        {
            BringStartAtLoginIntoView();
        }
        else
        {
            _pendingScrollToStartAtLogin = true;
        }
    }


    /// <summary>
    /// 在布局完成后再 BringIntoView，避免入场动画尚未量完时滚动无效。
    /// </summary>
    private void BringStartAtLoginIntoView()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            Grid_StartAtLogin.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = EntranceAnimation.AnimationsEnabled(),
                VerticalAlignmentRatio = 0.22,
            });
            ToggleSwitch_StartAtLogin.Focus(FocusState.Programmatic);
            ScheduleStartAtLoginAttention();
        });
    }


    /// <summary>
    /// 等滚动与设置页入场错开后，再压暗周围。
    /// </summary>
    private void ScheduleStartAtLoginAttention()
    {
        _startAtLoginAttentionTimer?.Stop();
        _startAtLoginAttentionTimer = DispatcherQueue.CreateTimer();
        _startAtLoginAttentionTimer.IsRepeating = false;
        _startAtLoginAttentionTimer.Interval = TimeSpan.FromMilliseconds(650);
        _startAtLoginAttentionTimer.Tick += (_, _) =>
        {
            _startAtLoginAttentionTimer?.Stop();
            PlayStartAtLoginAttention();
        };
        _startAtLoginAttentionTimer.Start();
    }


    /// <summary>
    /// 色条默认藏住，避免普通进入设置页时露出。
    /// </summary>
    private void HideStartAtLoginAccent()
    {
        ElementCompositionPreview.GetElementVisual(Border_StartAtLoginAccent).Opacity = 0;
    }


    /// <summary>
    /// 压暗其它设置项，左侧色条标出开机启动整块；点到块外或超时后恢复。
    /// </summary>
    private void PlayStartAtLoginAttention()
    {
        if (_startAtLoginAttentionActive)
        {
            return;
        }

        _startAtLoginAttentionActive = true;
        bool animate = EntranceAnimation.AnimationsEnabled();
        foreach (UIElement child in StackPanel_General.Children)
        {
            if (!ReferenceEquals(child, Grid_StartAtLogin))
            {
                SetOrAnimateOpacity(child, 1f, 0.32f, 280, animate);
            }
        }

        SetOrAnimateOpacity(Border_StartAtLoginAccent, 0f, 1f, 280, animate);
        if (animate)
        {
            PlayStartAtLoginSettleScale();
        }

        if (!_startAtLoginDismissPointerHooked)
        {
            ScrollViewer_General.AddHandler(UIElement.PointerPressedEvent, _startAtLoginDismissPointerHandler, handledEventsToo: true);
            _startAtLoginDismissPointerHooked = true;
        }

        _startAtLoginAttentionTimer?.Stop();
        _startAtLoginAttentionTimer = DispatcherQueue.CreateTimer();
        _startAtLoginAttentionTimer.IsRepeating = false;
        _startAtLoginAttentionTimer.Interval = TimeSpan.FromMilliseconds(8000);
        _startAtLoginAttentionTimer.Tick += (_, _) =>
        {
            _startAtLoginAttentionTimer?.Stop();
            DismissStartAtLoginAttention(animate: true);
        };
        _startAtLoginAttentionTimer.Start();
    }


    /// <summary>
    /// 目标块从左缘向右轻微放大再回弹；锚在左侧，避免往左撑出被裁切。
    /// </summary>
    private void PlayStartAtLoginSettleScale()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(Grid_StartAtLogin);
        visual.CenterPoint = new Vector3(0f, (float)(Grid_StartAtLogin.ActualHeight / 2), 0);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1f),
            new Vector2(0.3f, 1f));
        Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, Vector3.One);
        scale.InsertKeyFrame(0.4f, new Vector3(1.03f, 1.03f, 1f), ease);
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(700);
        visual.StartAnimation(nameof(Visual.Scale), scale);
    }


    /// <summary>
    /// 点在开机启动整块之外时收起聚光，点在块内（开关、说明）保持。
    /// </summary>
    private void OnStartAtLoginDismissPointer(object sender, PointerRoutedEventArgs e)
    {
        if (!_startAtLoginAttentionActive)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInsideStartAtLoginBlock(source))
        {
            return;
        }

        DismissStartAtLoginAttention(animate: true);
    }


    /// <summary>
    /// 指针落点是否在标题 / 说明 / 开关这一组里。
    /// </summary>
    private bool IsInsideStartAtLoginBlock(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, Grid_StartAtLogin))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }


    /// <summary>
    /// 恢复其它项透明度并收起色条。
    /// </summary>
    /// <param name="animate">为 false 时立即复位（卸载页）。</param>
    private void DismissStartAtLoginAttention(bool animate)
    {
        _startAtLoginAttentionTimer?.Stop();
        if (_startAtLoginDismissPointerHooked)
        {
            ScrollViewer_General.RemoveHandler(UIElement.PointerPressedEvent, _startAtLoginDismissPointerHandler);
            _startAtLoginDismissPointerHooked = false;
        }

        if (!_startAtLoginAttentionActive)
        {
            HideStartAtLoginAccent();
            return;
        }

        _startAtLoginAttentionActive = false;
        bool useAnim = animate && EntranceAnimation.AnimationsEnabled();
        foreach (UIElement child in StackPanel_General.Children)
        {
            if (!ReferenceEquals(child, Grid_StartAtLogin))
            {
                SetOrAnimateOpacity(child, 0.32f, 1f, 220, useAnim);
            }
        }

        SetOrAnimateOpacity(Border_StartAtLoginAccent, 1f, 0f, 220, useAnim);
        ElementCompositionPreview.GetElementVisual(Grid_StartAtLogin).Scale = Vector3.One;
    }


    /// <summary>
    /// 用 Composition 做透明度过渡；系统关掉动画时直接赋值。
    /// </summary>
    private static void SetOrAnimateOpacity(UIElement element, float from, float to, int durationMs, bool animate)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (!animate)
        {
            visual.Opacity = to;
            return;
        }

        Compositor compositor = visual.Compositor;
        ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, from);
        opacity.InsertKeyFrame(1f, to, compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f)));
        opacity.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation(nameof(Visual.Opacity), opacity);
    }


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
