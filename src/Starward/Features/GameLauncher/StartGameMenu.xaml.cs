using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Starward.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameSelector;
using Starward.Features.UrlProtocol;
using Starward.Helpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 首页汉堡菜单的悬停菜单内容：游戏设置 + 快速启动（启动参数配置 / 选择启动方式 / 添加任务栏启动方式）。
/// 由 <see cref="StartGameButton"/> 承载在一个悬停 Popup 中。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class StartGameMenu : UserControl
{


    private readonly ILogger<StartGameMenu> _logger = AppConfig.GetLogger<StartGameMenu>();


    /// <summary>
    /// 请求关闭承载的 Popup（执行菜单动作后由 <see cref="StartGameButton"/> 订阅处理）。
    /// </summary>
    public event Action? RequestClose;


    /// <summary>
    /// 菜单内 ComboBox 等子 Popup 打开状态变化（打开时父级悬停菜单不应关闭）。
    /// </summary>
    public event Action<bool>? ChildPopupOpenChanged;


    private int _openChildPopupCount;


    public StartGameMenu()
    {
        this.InitializeComponent();
    }


    private void ComboBox_DropDownOpened(object sender, object e)
    {
        NotifyChildPopupOpen(true);
    }


    private void ComboBox_DropDownClosed(object sender, object e)
    {
        NotifyChildPopupOpen(false);
    }


    /// <summary>
    /// 免 UAC 说明 Flyout 打开：父级悬停菜单勿因指针移入 Flyout 而关闭。
    /// </summary>
    private void Flyout_SkipUacHelp_Opened(object sender, object e)
    {
        NotifyChildPopupOpen(true);
    }


    /// <summary>
    /// 免 UAC 说明 Flyout 关闭。
    /// </summary>
    private void Flyout_SkipUacHelp_Closed(object sender, object e)
    {
        NotifyChildPopupOpen(false);
    }


    /// <summary>
    /// 统计子 Popup/Flyout 打开数，通知 <see cref="StartGameButton"/> 保持快速菜单打开。
    /// </summary>
    private void NotifyChildPopupOpen(bool open)
    {
        if (open)
        {
            _openChildPopupCount++;
            ChildPopupOpenChanged?.Invoke(true);
        }
        else if (_openChildPopupCount > 0)
        {
            _openChildPopupCount--;
            ChildPopupOpenChanged?.Invoke(_openChildPopupCount > 0);
        }
    }


    /// <summary>
    /// 当前游戏，由 <see cref="StartGameButton"/> 通过绑定传入。
    /// </summary>
    public GameId CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; private set; }


    /// <summary>
    /// 「选择启动方式」下拉：首项为「无」，其后为 config1… 对应配置文件（不含「无」以外的虚拟项）。
    /// </summary>
    public ObservableCollection<GameLaunchProfile> Profiles { get; } = new();


    /// <summary>
    /// 「添加任务栏启动方式」下拉的选项（跟随软件设置 + 各配置文件）。
    /// </summary>
    public ObservableCollection<TaskbarLaunchOption> TaskbarOptions { get; } = new();


    /// <summary>
    /// 「选择启动方式」当前下拉选中是否等于已生效配置：相等时「应用」按钮内显示对勾并禁用。
    /// </summary>
    public bool ShowActiveCheck
    {
        get;
        private set => SetProperty(ref field, value);
    }


    private string? _activeProfileId;

    private bool _suppressActiveSelection;


    /// <summary>
    /// 每次 Popup 打开前调用：刷新配置文件列表与选中状态，并按需加载图标。
    /// </summary>
    public void OnOpening()
    {
        try
        {
            _openChildPopupCount = 0;
            CollapseShortcutSection();
            LoadProfiles();
            RefreshUrlProtocolState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartGameMenu OnOpening ({biz})", CurrentGameBiz);
        }
    }


    #region 注册 URL 协议（.url 快捷方式依赖；菜单内无开关，仅生成快捷方式时检测/引导）


    private bool _suppressUrlProtocolApply;


    /// <summary>
    /// 系统是否已注册 <c>moonward://</c> 协议。菜单内不再展示开关；生成 .url 快捷方式前会读取此状态，
    /// 未注册时弹窗征求同意后再写入并注册。
    /// </summary>
    public bool EnableUrlProtocol
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !_suppressUrlProtocolApply)
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
                    _logger.LogError(ex, "Toggle url protocol from start game menu");
                }
            }
        }
    }


    /// <summary>
    /// 同步系统实际协议注册状态到 <see cref="EnableUrlProtocol"/>（不触发注册/注销）。
    /// </summary>
    private async void RefreshUrlProtocolState()
    {
        try
        {
            var status = await Launcher.QueryUriSupportAsync(new Uri("moonward://"), LaunchQuerySupportType.Uri);
            _suppressUrlProtocolApply = true;
            EnableUrlProtocol = status is LaunchQuerySupportStatus.Available;
            _suppressUrlProtocolApply = false;
        }
        catch (Exception ex)
        {
            _suppressUrlProtocolApply = false;
            _logger.LogError(ex, "Refresh url protocol state");
        }
    }


    #endregion


    private void LoadProfiles()
    {
        if (CurrentGameId is null)
        {
            return;
        }
        CurrentGameBiz = CurrentGameId.GameBiz;

        Profiles.Clear();
        // 「无」：不在启动参数配置中管理，默认启动方式。
        Profiles.Add(new GameLaunchProfile
        {
            Id = GameLaunchProfile.NoneId,
            Name = Lang.StartGameMenu_LaunchMethodNone,
        });
        var config1 = new GameLaunchProfile
        {
            Id = GameLaunchProfile.DefaultId,
            Name = ProfileNameFromId(GameLaunchProfile.DefaultId, AppConfig.GetDefaultLaunchProfileName(CurrentGameBiz)),
            Argument = AppConfig.GetStartArgument(CurrentGameBiz),
            EnableThirdPartyTool = AppConfig.GetEnableThirdPartyTool(CurrentGameBiz),
            ThirdPartyToolPath = GameLauncherService.GetThirdPartyToolPath(CurrentGameId),
            LoginUid = AppConfig.GetDefaultLaunchLoginUid(CurrentGameBiz),
        };
        Profiles.Add(config1);
        foreach (GameLaunchProfile extra in AppConfig.GetExtraLaunchProfiles(CurrentGameBiz))
        {
            if (GameLaunchProfile.IsKnownId(extra.Id) && !extra.IsDefault && !extra.IsNone)
            {
                extra.Id = GameLaunchProfile.NormalizeId(extra.Id);
                extra.Name = ProfileNameFromId(extra.Id, extra.Name);
                Profiles.Add(extra);
            }
        }

        // 「选择启动方式」：未设置时默认「无」。
        _activeProfileId = AppConfig.GetActiveLaunchProfileId(CurrentGameBiz);
        GameLaunchProfile active = Profiles.FirstOrDefault(p => string.Equals(p.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase))
            ?? Profiles.First(p => p.IsNone);
        _activeProfileId = active.Id;
        _suppressActiveSelection = true;
        ComboBox_ActiveProfile.SelectedItem = active;
        _suppressActiveSelection = false;
        UpdateApplyButtonState(animateCheck: false);

        // 「游戏快捷方式」：跟随软件设置 + 各配置文件（不含「无」）。
        TaskbarOptions.Clear();
        TaskbarOptions.Add(new TaskbarLaunchOption
        {
            DisplayName = Lang.StartGameMenu_FollowAppSetting,
            ProfileId = null,
            ProfileDisplayName = Lang.StartGameMenu_FollowAppSetting,
        });
        foreach (GameLaunchProfile p in Profiles)
        {
            if (p.IsNone)
            {
                continue;
            }
            TaskbarOptions.Add(new TaskbarLaunchOption
            {
                DisplayName = p.Name,
                ProfileId = p.Id,
                ProfileDisplayName = p.Name,
                LoginUid = p.LoginUid is > 0 ? p.LoginUid : null,
            });
        }
        ComboBox_TaskbarProfile.SelectedIndex = 0;
    }


    /// <summary>
    /// 由 configN 得到默认显示名「配置文件 N」（序号与 Id 严格一致）；非空自定义名则保留。
    /// </summary>
    private static string ProfileNameFromId(string id, string? customName)
    {
        int index = GameLaunchProfile.TryGetIndex(id) ?? 1;
        string fromId = string.Format(Lang.GameLauncherSettingDialog_ProfileNameFormat, index);
        return string.IsNullOrWhiteSpace(customName) ? fromId : customName.Trim();
    }


    #region 「创建游戏快捷方式」折叠区揭示动画（Composition InsetClip，非逐帧布局）


    private bool _shortcutExpanded;

    /// <summary>折叠容器的合成器裁剪：BottomInset 从下往上揭示/卷起，是纯合成器动画，不触发布局。</summary>
    private InsetClip? _shortcutClip;

    private Storyboard? _chevronStoryboard;


    /// <summary>
    /// 惰性获取折叠容器的 <see cref="InsetClip"/> 并挂到其 Composition 视觉上。
    /// 该裁剪同时承担两个职责：①收起态（Height=0）把溢出内容裁掉（替代原 XAML RectangleGeometry）；
    /// ②展开/收起时动画 <see cref="InsetClip.BottomInset"/> 做「卷帘」揭示，全程在合成器线程，
    /// 不像动画 Height 那样逐帧触发整个菜单的 Measure/Arrange 与 Popup 重定位。
    /// </summary>
    private InsetClip GetShortcutClip()
    {
        if (_shortcutClip is null)
        {
            Visual host = ElementCompositionPreview.GetElementVisual(ShortcutExpandHost);
            _shortcutClip = host.Compositor.CreateInsetClip();
            host.Clip = _shortcutClip;
        }
        return _shortcutClip;
    }


    /// <summary>
    /// 点击折叠标题：在展开/收起之间切换（合成器揭示动画）。
    /// </summary>
    private void Button_ShortcutHeader_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetShortcutExpanded(!_shortcutExpanded, animate: true);
    }


    /// <summary>
    /// 菜单每次打开时把折叠区重置为收起状态（无动画）。
    /// </summary>
    private void CollapseShortcutSection()
    {
        _shortcutExpanded = false;
        _chevronStoryboard?.Stop();
        _chevronStoryboard = null;

        InsetClip clip = GetShortcutClip();
        Visual content = ElementCompositionPreview.GetElementVisual(ShortcutExpandContent);
        clip.StopAnimation(nameof(InsetClip.BottomInset));
        content.StopAnimation(nameof(Visual.Opacity));
        clip.BottomInset = 0;
        content.Opacity = 1;
        ShortcutExpandHost.Height = 0;   // Height=0 时容器视觉高度为 0，裁剪自然隐藏全部内容
        ShortcutChevronRotate.Angle = 0;
        // 免 UAC 为敏感选项，每次打开菜单重置为未勾选，避免误操作
        if (CheckBox_SkipUac is not null)
        {
            CheckBox_SkipUac.IsChecked = false;
        }
    }


    /// <summary>
    /// 展开或收起折叠内容。用一次布局把容器设到目标高度，再以合成器 <see cref="InsetClip.BottomInset"/>
    /// （配合内容淡入淡出）做揭示/卷起动画——避免逐帧动画 Height 造成的整菜单重排与 Popup 重定位卡顿。
    /// </summary>
    /// <param name="expand">是否展开。</param>
    /// <param name="animate">是否播放动画；关闭或系统减少动态效果时直接切到终态。</param>
    private void SetShortcutExpanded(bool expand, bool animate)
    {
        _shortcutExpanded = expand;

        // 系统「显示动画」关闭时不做揭示动画，直接切终态（无障碍 / 减少动态效果）。
        bool motion = animate && EntranceAnimation.AnimationsEnabled();
        AnimateChevron(expand, motion);

        InsetClip clip = GetShortcutClip();
        Visual content = ElementCompositionPreview.GetElementVisual(ShortcutExpandContent);
        Compositor compositor = clip.Compositor;

        // 结束上一段揭示动画，避免其完成回调与本次错乱（终态仍由回调内的状态判断把关）。
        clip.StopAnimation(nameof(InsetClip.BottomInset));
        content.StopAnimation(nameof(Visual.Opacity));

        if (expand)
        {
            // 量一次内容自然高度，一次性把容器设为该高度（整个过程仅此一次布局 + Popup 重定位）。
            double width = ShortcutExpandContent.ActualWidth;
            if (width <= 0)
            {
                width = ShortcutExpandHost.ActualWidth > 0 ? ShortcutExpandHost.ActualWidth : 224;
            }
            ShortcutExpandContent.Measure(new Size(width, double.PositiveInfinity));
            double target = ShortcutExpandContent.DesiredSize.Height;
            ShortcutExpandHost.Height = target;

            if (!motion)
            {
                clip.BottomInset = 0;
                content.Opacity = 1;
                ShortcutExpandHost.Height = double.NaN;   // 自适应后续内容
                return;
            }

            // 揭示前先把内容裁掉并透明，防止首帧闪现整块。
            clip.BottomInset = (float)target;
            content.Opacity = 0;

            CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
            var duration = TimeSpan.FromMilliseconds(260);

            ScalarKeyFrameAnimation reveal = compositor.CreateScalarKeyFrameAnimation();
            reveal.InsertKeyFrame(1f, 0f, ease);
            reveal.Duration = duration;

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 1f, ease);
            fade.Duration = duration;

            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            clip.StartAnimation(nameof(InsetClip.BottomInset), reveal);
            content.StartAnimation(nameof(Visual.Opacity), fade);
            batch.Completed += (_, _) =>
            {
                // 完成回调不保证在 UI 线程；改 Height 须回到 DispatcherQueue。
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_shortcutExpanded)   // 仍处展开态才收尾为自适应高度
                    {
                        ShortcutExpandHost.Height = double.NaN;
                    }
                });
            };
            batch.End();
        }
        else
        {
            // 收起：把高度钉成当前像素值（从 Auto→具体值，视觉不变，仅一次布局），随后卷起 + 淡出，结束再归零。
            double height = ShortcutExpandHost.ActualHeight;
            ShortcutExpandHost.Height = height;

            if (!motion)
            {
                clip.BottomInset = 0;
                content.Opacity = 1;
                ShortcutExpandHost.Height = 0;
                return;
            }

            CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));
            var duration = TimeSpan.FromMilliseconds(180);

            ScalarKeyFrameAnimation roll = compositor.CreateScalarKeyFrameAnimation();
            roll.InsertKeyFrame(1f, (float)height, ease);
            roll.Duration = duration;

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f, ease);
            fade.Duration = duration;

            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            clip.StartAnimation(nameof(InsetClip.BottomInset), roll);
            content.StartAnimation(nameof(Visual.Opacity), fade);
            batch.Completed += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_shortcutExpanded)   // 仍处收起态才归零并复位裁剪/透明度，供下次展开
                    {
                        ShortcutExpandHost.Height = 0;
                        clip.BottomInset = 0;
                        content.Opacity = 1;
                    }
                });
            };
            batch.End();
        }
    }


    /// <summary>
    /// 折叠标题右侧的小箭头随展开状态旋转（RenderTransform 旋转为独立动画，开销可忽略）。
    /// </summary>
    /// <param name="expanded">是否处于展开态。</param>
    /// <param name="animate">是否播放旋转动画；否则直接置终值。</param>
    private void AnimateChevron(bool expanded, bool animate)
    {
        _chevronStoryboard?.Stop();
        _chevronStoryboard = null;

        if (!animate)
        {
            ShortcutChevronRotate.Angle = expanded ? 180 : 0;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = expanded ? 180 : 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, ShortcutChevronRotate);
        Storyboard.SetTargetProperty(animation, "Angle");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _chevronStoryboard = storyboard;
        storyboard.Begin();
    }


    #endregion


    /// <summary>
    /// 游戏设置：打开现有的游戏设置对话框。
    /// </summary>
    private async void Button_GameSetting_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var xamlRoot = this.XamlRoot;
        RequestClose?.Invoke();
        try
        {
            await new GameLauncherSettingDialog { CurrentGameId = CurrentGameId, XamlRoot = xamlRoot }.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open game setting dialog from menu ({biz})", CurrentGameBiz);
        }
    }


    /// <summary>
    /// 启动参数配置：打开启动参数配置对话框。
    /// </summary>
    private async void Button_LaunchProfileConfig_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var xamlRoot = this.XamlRoot;
        RequestClose?.Invoke();
        try
        {
            await new GameLaunchProfileDialog { CurrentGameId = CurrentGameId, XamlRoot = xamlRoot }.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open launch profile dialog from menu ({biz})", CurrentGameBiz);
        }
    }


    private void ComboBox_ActiveProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressActiveSelection)
        {
            return;
        }
        UpdateApplyButtonState(animateCheck: false);
    }


    /// <summary>
    /// 应用「选择启动方式」：把选中配置设为当前生效配置，点击开始游戏即按此启动。
    /// </summary>
    private void Button_ApplyActiveProfile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ComboBox_ActiveProfile.SelectedItem is GameLaunchProfile p)
        {
            AppConfig.SetActiveLaunchProfileId(CurrentGameBiz, p.Id);
            _activeProfileId = p.Id;
            UpdateApplyButtonState(animateCheck: true);
        }
    }


    private void UpdateApplyButtonState(bool animateCheck)
    {
        bool isActive = false;
        if (ComboBox_ActiveProfile.SelectedItem is GameLaunchProfile p)
        {
            if (p.IsNone || GameLaunchProfile.IsNoneId(_activeProfileId))
            {
                isActive = p.IsNone && GameLaunchProfile.IsNoneId(_activeProfileId);
            }
            else
            {
                isActive = string.Equals(p.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase);
            }
        }

        ShowActiveCheck = isActive;

        if (isActive)
        {
            if (animateCheck)
            {
                PlayApplyCheckAnimation();
            }
            else
            {
                SetApplyCheckVisual(shown: true);
            }
        }
        else
        {
            SetApplyCheckVisual(shown: false);
        }
    }


    private void SetApplyCheckVisual(bool shown)
    {
        FontIcon_ApplyCheck.Opacity = shown ? 1 : 0;
        ApplyCheckScale.ScaleX = shown ? 1 : 0.3;
        ApplyCheckScale.ScaleY = shown ? 1 : 0.3;
    }


    private void PlayApplyCheckAnimation()
    {
        SetApplyCheckVisual(shown: false);

        var easing = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(280);

        var opacityAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(opacityAnim, FontIcon_ApplyCheck);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var scaleXAnim = new DoubleAnimation
        {
            From = 0.3,
            To = 1,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(scaleXAnim, ApplyCheckScale);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

        var scaleYAnim = new DoubleAnimation
        {
            From = 0.3,
            To = 1,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(scaleYAnim, ApplyCheckScale);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacityAnim);
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);
        storyboard.Begin();
    }


    /// <summary>
    /// 打开本应用创建的免 UAC 启动计划任务管理界面。
    /// 由说明 Flyout 内链接触发。
    /// </summary>
    private async void Button_ManageElevatedTasks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 先收起说明 Flyout，避免与 ContentDialog 同时显示。
            Flyout_SkipUacHelp.Hide();
            await new ElevatedStartGameTaskManagerDialog
            {
                XamlRoot = this.XamlRoot,
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open elevated start-game task manager");
            InAppToast.MainWindow?.Error(ex);
        }
    }


    /// <summary>
    /// 生成桌面快捷方式；图标默认使用该游戏在游戏列表中的图标。
    /// 普通路径：.url + moonward://（需注册 URL 协议）。
    /// 勾选「关闭 UAC」：提权注册计划任务 + .lnk 触发 schtasks（不依赖协议注册）。
    /// </summary>
    private async void Button_GenerateShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ComboBox_TaskbarProfile.SelectedItem is not TaskbarLaunchOption option)
            {
                return;
            }

            string gameName = GetGameDisplayName();
            GameShortcutService.IconSource? icon = GameShortcutService.GetGameIconSource(CurrentGameBiz);
            bool skipUac = CheckBox_SkipUac.IsChecked is true;
            string shortcutPath;

            if (skipUac)
            {
                // 免 UAC：任务动作直接把 moonward:// 作为命令行参数传给 Moonward，无需系统协议注册。
                try
                {
                    shortcutPath = GameShortcutService.CreateElevatedStartGameShortcut(
                        CurrentGameBiz, gameName, option.ProfileId, option.ProfileDisplayName, icon, option.LoginUid);
                }
                catch (Exception ex) when (ElevatedStartGameTaskService.IsElevationCancelled(ex))
                {
                    _logger.LogInformation(ex, "Skip-UAC task registration cancelled by user ({biz})", CurrentGameBiz);
                    InAppToast.MainWindow?.Information(Lang.StartGameMenu_SkipUacPromptCancelled);
                    return;
                }
            }
            else
            {
                // .url 依赖系统识别 moonward://；未注册时先征求用户同意再注册。
                if (!EnableUrlProtocol)
                {
                    var dialog = new ContentDialog
                    {
                        Title = Lang.StartGameMenu_AddTaskbarLaunch,
                        Content = Lang.StartGameMenu_RegisterProtocolForUrlShortcutHint,
                        PrimaryButtonText = Lang.Common_Confirm,
                        CloseButtonText = Lang.Common_Cancel,
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot,
                    };
                    if (await dialog.ShowAsync() is not ContentDialogResult.Primary)
                    {
                        return; // 用户拒绝：不注册、不生成
                    }
                    EnableUrlProtocol = true; // 用户确定：注册协议
                }

                shortcutPath = GameShortcutService.CreateStartGameShortcut(
                    CurrentGameBiz, gameName, option.ProfileId, option.ProfileDisplayName, icon, option.LoginUid);
            }

            InAppToast.MainWindow?.Success(Lang.StartGameMenu_ShortcutCreated, shortcutPath);
            _ = RevealInExplorerAsync(shortcutPath);
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generate taskbar shortcut ({biz})", CurrentGameBiz);
            InAppToast.MainWindow?.Error(ex);
        }
    }


    private string GetGameDisplayName()
    {
        try
        {
            if (CurrentGameId is not null && CurrentGameId.GameBiz.IsKnown())
            {
                string? name = new GameBizIcon(CurrentGameId.GameBiz).GameName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }
        catch { }
        return CurrentGameBiz.ToString();
    }


    private static async Task RevealInExplorerAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var option = new FolderLauncherOptions();
            option.ItemsToSelect.Add(file);
            await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(path), option);
        }
        catch { }
    }




    /// <summary>
    /// 「添加任务栏启动方式」的下拉选项。
    /// </summary>
    public sealed class TaskbarLaunchOption
    {
        public string DisplayName { get; set; } = "";

        /// <summary>配置文件内部名；null = 跟随软件设置（URL 不带 profile 参数）。</summary>
        public string? ProfileId { get; set; }

        public string ProfileDisplayName { get; set; } = "";

        /// <summary>配置绑定的登录账号 UID；null 表示不附加。</summary>
        public long? LoginUid { get; set; }
    }


}
