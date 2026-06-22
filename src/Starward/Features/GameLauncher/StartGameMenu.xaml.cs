using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameSelector;
using Starward.Helpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        _openChildPopupCount++;
        ChildPopupOpenChanged?.Invoke(true);
    }


    private void ComboBox_DropDownClosed(object sender, object e)
    {
        if (_openChildPopupCount > 0)
        {
            _openChildPopupCount--;
        }
        ChildPopupOpenChanged?.Invoke(_openChildPopupCount > 0);
    }


    /// <summary>
    /// 当前游戏，由 <see cref="StartGameButton"/> 通过绑定传入。
    /// </summary>
    public GameId CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; private set; }


    /// <summary>
    /// 「选择启动方式」下拉的配置文件列表（默认配置 + 额外配置）。
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartGameMenu OnOpening ({biz})", CurrentGameBiz);
        }
    }


    private void LoadProfiles()
    {
        if (CurrentGameId is null)
        {
            return;
        }
        CurrentGameBiz = CurrentGameId.GameBiz;

        Profiles.Clear();
        var defaultProfile = new GameLaunchProfile
        {
            Id = GameLaunchProfile.DefaultId,
            Name = AppConfig.GetDefaultLaunchProfileName(CurrentGameBiz) ?? Lang.GameLauncherSettingDialog_DefaultProfileName,
            Argument = AppConfig.GetStartArgument(CurrentGameBiz),
            EnableThirdPartyTool = AppConfig.GetEnableThirdPartyTool(CurrentGameBiz),
            ThirdPartyToolPath = GameLauncherService.GetThirdPartyToolPath(CurrentGameId),
        };
        Profiles.Add(defaultProfile);
        foreach (GameLaunchProfile extra in AppConfig.GetExtraLaunchProfiles(CurrentGameBiz))
        {
            if (!string.IsNullOrEmpty(extra.Id) && !extra.IsDefault)
            {
                Profiles.Add(extra);
            }
        }

        // 「选择启动方式」当前生效配置。
        _activeProfileId = AppConfig.GetActiveLaunchProfileId(CurrentGameBiz);
        if (string.IsNullOrEmpty(_activeProfileId))
        {
            _activeProfileId = GameLaunchProfile.DefaultId;
        }
        GameLaunchProfile active = Profiles.FirstOrDefault(p => string.Equals(p.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];
        _activeProfileId = active.Id;
        _suppressActiveSelection = true;
        ComboBox_ActiveProfile.SelectedItem = active;
        _suppressActiveSelection = false;
        UpdateApplyButtonState(animateCheck: false);

        // 「添加任务栏启动方式」选项：跟随软件设置 + 各配置文件。
        TaskbarOptions.Clear();
        TaskbarOptions.Add(new TaskbarLaunchOption
        {
            DisplayName = Lang.StartGameMenu_FollowAppSetting,
            ProfileId = null,
            ProfileDisplayName = Lang.StartGameMenu_FollowAppSetting,
        });
        foreach (GameLaunchProfile p in Profiles)
        {
            TaskbarOptions.Add(new TaskbarLaunchOption
            {
                DisplayName = p.Name,
                ProfileId = p.Id,
                ProfileDisplayName = p.Name,
            });
        }
        ComboBox_TaskbarProfile.SelectedIndex = 0;
    }


    #region 「创建游戏快捷方式」折叠区高度缓动


    private bool _shortcutExpanded;

    private Storyboard? _shortcutHeightStoryboard;


    /// <summary>
    /// 折叠内容裁剪矩形随容器高度变化更新，使展开过程中超出部分被裁掉。
    /// </summary>
    private void ShortcutExpandHost_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        ShortcutExpandClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }


    /// <summary>
    /// 点击折叠标题：在展开/收起之间切换（带高度缓动动画）。
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
        _shortcutHeightStoryboard?.Stop();
        _shortcutHeightStoryboard = null;
        _shortcutExpanded = false;
        ShortcutExpandHost.Height = 0;
        ShortcutChevronRotate.Angle = 0;
    }


    /// <summary>
    /// 展开或收起折叠内容，并用缓动曲线驱动容器高度变化，使整个菜单的尺寸过渡平滑。
    /// </summary>
    private void SetShortcutExpanded(bool expand, bool animate)
    {
        _shortcutExpanded = expand;
        AnimateChevron(expand);

        // 收起目标高度为 0；展开时先量出内容自然高度作为目标。
        double to = 0;
        if (expand)
        {
            double width = ShortcutExpandContent.ActualWidth;
            if (width <= 0)
            {
                width = 296;
            }
            ShortcutExpandContent.Measure(new Size(width, double.PositiveInfinity));
            to = ShortcutExpandContent.DesiredSize.Height;
        }

        // 先取当前高度作为动画起点，再停掉旧动画，避免 Stop 把高度还原成基值导致跳变。
        double from = ShortcutExpandHost.ActualHeight;
        _shortcutHeightStoryboard?.Stop();
        _shortcutHeightStoryboard = null;

        if (!animate)
        {
            ShortcutExpandHost.Height = expand ? double.NaN : 0;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(300),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, ShortcutExpandHost);
        Storyboard.SetTargetProperty(animation, "Height");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        bool expanding = expand;
        storyboard.Completed += (s, e) =>
        {
            if (expanding && _shortcutExpanded)
            {
                // 展开完成后改为自适应高度，以便适应后续动态内容（如上传新图标）。
                ShortcutExpandHost.Height = double.NaN;
            }
            else if (!expanding && !_shortcutExpanded)
            {
                ShortcutExpandHost.Height = 0;
            }
        };
        _shortcutHeightStoryboard = storyboard;
        storyboard.Begin();
    }


    /// <summary>
    /// 折叠标题右侧的小箭头随展开状态旋转。
    /// </summary>
    private void AnimateChevron(bool expanded)
    {
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
        bool isActive = ComboBox_ActiveProfile.SelectedItem is GameLaunchProfile p
            && string.Equals(p.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase);

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
    /// 生成桌面快捷方式；图标默认使用该游戏在游戏列表中的图标。
    /// 快捷方式以命令行参数直接把 starward:// 传给 Starward.exe，启动游戏无需注册系统 URL 协议
    /// （系统级协议注册是设置页里的独立开关，仅用于从浏览器/运行框等外部唤起 starward://）。
    /// </summary>
    private void Button_GenerateShortcut_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            if (ComboBox_TaskbarProfile.SelectedItem is not TaskbarLaunchOption option)
            {
                return;
            }

            string gameName = GetGameDisplayName();
            GameShortcutService.IconSource? icon = GameShortcutService.GetGameIconSource(CurrentGameBiz);
            string lnkPath = GameShortcutService.CreateStartGameShortcut(CurrentGameBiz, gameName, option.ProfileId, option.ProfileDisplayName, icon);
            InAppToast.MainWindow?.Success(Lang.StartGameMenu_ShortcutCreated, lnkPath);
            _ = RevealInExplorerAsync(lnkPath);
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
    }


}
