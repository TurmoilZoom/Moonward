using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord.Genshin;
using Starward.Features.GameRecord.StarRail;
using Starward.Features.GameRecord.ZZZ;
using Starward.Controls;
using Starward.Features.ViewHost;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;


namespace Starward.Features.GameRecord;

public sealed partial class GameRecordPage : PageBase
{
    /// <summary>
    /// 米游社/ HoYoLAB 工具箱主页面（GameRecordPage），负责角色管理、左侧功能导航（战绩/月报等）以及子页面的容器。
    /// </summary>

    private readonly ILogger<GameRecordPage> _logger = AppConfig.GetLogger<GameRecordPage>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();

    /// <summary>
    /// 提供与设置页相同的流体导航悬停/按压动画效果（高亮条弹簧跟随、文字偏移、物理按压反馈）。
    /// </summary>
    private readonly FluidNavigationViewHoverEffect _navHoverEffect = new();



    public GameRecordPage()
    {
        this.InitializeComponent();
    }




    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 将 B 服（bilibili）映射为国服，便于统一使用 HyperionClient 及国服逻辑。
        CurrentGameBiz = CurrentGameBiz.Value switch
        {
            GameBiz.hk4e_bilibili => GameBiz.hk4e_cn,
            GameBiz.hkrpg_bilibili => GameBiz.hkrpg_cn,
            GameBiz.nap_bilibili => GameBiz.nap_cn,
            _ => CurrentGameBiz,
        };

        // 根据区服选择客户端：国内用 HyperionClient，国际用 HoyolabClient。
        _gameRecordService.IsHoyolab = CurrentGameBiz.IsGlobalServer();

        // 国际服不需要设备指纹更新入口（Hyperion 特有）。
        if (CurrentGameBiz.IsGlobalServer())
        {
            NavigationViewItem_UpdateDeviceInfo.Visibility = Visibility.Collapsed;
        }

        _gameRecordService.Language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        InitializeNavigationViewItemVisibility();
    }




    protected override async void OnLoaded()
    {
        // 附加与「设置」页一致的流体导航动画效果（必须在 Loaded 后，视觉树就绪）。
        _navHoverEffect.Attach(NavigationView_Toolbox, NavIndicatorHost, _logger);

        // 恢复上次工具箱左侧面板（角色列表+功能菜单）的展开状态。
        if (AppConfig.HoyolabToolboxPaneOpen)
        {
            OpenNavigationViewPane();
        }
        else
        {
            CloseNavigationViewPane();
        }

        // 注册跨组件消息：角色变更时刷新列表，验证账号时弹出战绩窗口。
        WeakReferenceMessenger.Default.Register<GameRecordRoleChangedMessage>(this, (r, m) =>
        {
            LoadGameRoles(m.GameRole);
        });
        WeakReferenceMessenger.Default.Register<GameRecordVerifyAccountMessage>(this, (r, m) =>
        {
            ShowBattleChronicleWindow();
        });

        await Task.Delay(16);
        NavigateTo(typeof(BlankPage));

        // 先进行免责声明检查（仅首次），通过后才加载角色、更新设备指纹并导航到默认月报页。
        if (await CheckAgreementAsync())
        {
            LoadGameRoles();
            await UpdateDeviceInfoAsync();
            await RefreshGameRoleHeadIconSilentlyAsync();
            NavigateToDefaultPage();
        }
    }



    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        NavigationViewItem_BattleChronicle.Tapped -= NavigationViewItem_BattleChronicle_Tapped;
        NavigationViewItem_UpdateDeviceInfo.Tapped -= NavigationViewItem_UpdateDeviceInfo_Tapped;
        _navHoverEffect.Detach();
        CurrentRole = null;
        GameRoleList = null!;
        _battleChronicleWindow = null;
    }




    /// <summary>
    /// 检查是否已接受米游社工具箱免责声明。
    /// 首次使用时弹出对话框（Accept 按钮带 5 秒倒计时），拒绝则跳转回启动器页面。
    /// </summary>
    /// <returns>是否允许继续加载工具箱内容。</returns>
    private async Task<bool> CheckAgreementAsync()
    {
        try
        {
            if (!AppConfig.AcceptHoyolabToolboxAgreement)
            {
                var dialog = new ContentDialog
                {
                    Title = Lang.Common_Disclaimer,
                    Content = Lang.HoyolabToolboxPage_DisclaimerContent,
                    PrimaryButtonText = Lang.Common_Accept + " (5s)",
                    SecondaryButtonText = Lang.Common_Reject,
                    IsPrimaryButtonEnabled = false,
                    DefaultButton = ContentDialogButton.Secondary,
                    XamlRoot = this.XamlRoot,
                };
                var resultTask = dialog.ShowAsync();
                bool cancel = false;

                // 实现 5 秒倒计时：每 0.1s 检查一次对话框是否被关闭，防止用户提前操作。
                for (int i = 0; i < 5; i++)
                {
                    for (int j = 0; j < 10; j++)
                    {
                        await Task.Delay(100);
                        if (resultTask.Status is Windows.Foundation.AsyncStatus.Completed)
                        {
                            cancel = true;
                            break;
                        }
                    }
                    if (cancel)
                    {
                        break;
                    }
                    dialog.PrimaryButtonText = Lang.Common_Accept + $" ({4 - i}s)";
                }

                dialog.PrimaryButtonText = Lang.Common_Accept;
                dialog.IsPrimaryButtonEnabled = true;
                var result = await resultTask;

                if (result is ContentDialogResult.Primary)
                {
                    AppConfig.AcceptHoyolabToolboxAgreement = true;
                }
                else
                {
                    // 拒绝或关闭 → 返回启动器页面，不进入工具箱。
                    WeakReferenceMessenger.Default.Send(new MainViewNavigateMessage(typeof(GameLauncherPage)));
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check agreement.");
            return false;
        }
    }




    #region Navigation Style


    /// <summary>
    /// 控制左侧工具箱面板内容区域的边距（展开时收紧，收起时留空）。
    /// </summary>
    public Thickness NavigationViewItemContentMargin { get; set => SetProperty(ref field, value); } = new Thickness(-2, 0, 0, 0);


    /// <summary>
    /// 点击宽头像区域 → 收起左侧面板（节省空间）。
    /// </summary>
    private void Grid_Avatar_1_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseNavigationViewPane();
    }


    /// <summary>
    /// 点击窄头像 → 展开左侧面板（显示角色列表和功能菜单）。
    /// </summary>
    private void Border_Avatar_2_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        OpenNavigationViewPane();
    }


    /// <summary>
    /// 展开工具箱左侧面板，并持久化状态。
    /// </summary>
    private void OpenNavigationViewPane()
    {
        NavigationViewItemContentMargin = new Thickness(-2, 0, 0, 0);
        NavigationView_Toolbox.IsPaneOpen = true;
        Grid_Avatar_1.Visibility = Visibility.Visible;
        Border_Avatar_2.Visibility = Visibility.Collapsed;
        AppConfig.HoyolabToolboxPaneOpen = true;
    }


    /// <summary>
    /// 收起工具箱左侧面板，并持久化状态到设置。
    /// </summary>
    private void CloseNavigationViewPane()
    {
        NavigationViewItemContentMargin = new Thickness(2, 0, 0, 0);
        NavigationView_Toolbox.IsPaneOpen = false;
        Grid_Avatar_1.Visibility = Visibility.Collapsed;
        Border_Avatar_2.Visibility = Visibility.Visible;
        AppConfig.HoyolabToolboxPaneOpen = false;
    }


    /// <summary>
    /// 根据当前游戏显示对应的左侧工具箱菜单项（战绩 + 各游戏专属月报/札记等），并设置对应战绩图片。
    /// </summary>
    private void InitializeNavigationViewItemVisibility()
    {
        if (CurrentGameBiz.Game is GameBiz.bh3)
        {
            NavigationViewItem_BattleChronicle.Visibility = Visibility.Visible;
            // 崩坏3战绩图片（背景图）
            Image_BattleChronicle.Source = new BitmapImage(new("ms-appx:///Assets/Image/4d94fbd5ff63c8b4344876ce21e04d10_2581928258151711511.png"));
        }
        else if (CurrentGameBiz.Game is GameBiz.hk4e)
        {
            NavigationViewItem_BattleChronicle.Visibility = Visibility.Visible;
            NavigationViewItem_TravelersDiary.Visibility = Visibility.Visible;
            NavigationViewItem_SpiralAbyss.Visibility = Visibility.Visible;
            NavigationViewItem_ImaginariumTheater.Visibility = Visibility.Visible;
            NavigationViewItem_StygianOnslaught.Visibility = Visibility.Visible;
            // 原神战绩图片
            Image_BattleChronicle.Source = new BitmapImage(new("ms-appx:///Assets/Image/ced4deac2162690105bbc8baad2b51a3_4109616186965788891.png"));
        }
        else if (CurrentGameBiz.Game is GameBiz.hkrpg)
        {
            NavigationViewItem_BattleChronicle.Visibility = Visibility.Visible;
            NavigationViewItem_TrailblazeMonthlyCalendar.Visibility = Visibility.Visible;
            NavigationViewItem_SimulatedUniverse.Visibility = Visibility.Visible;
            NavigationViewItem_ForgottenHall.Visibility = Visibility.Visible;
            NavigationViewItem_PureFiction.Visibility = Visibility.Visible;
            NavigationViewItem_ApocalypticShadow.Visibility = Visibility.Visible;
            NavigationViewItem_ChallengePeak.Visibility = Visibility.Visible;
            // 星穹铁道战绩图片
            Image_BattleChronicle.Source = new BitmapImage(new("ms-appx:///Assets/Image/ade9545750299456a3fcbc8c3b63521d_2941971308029698042.png"));
        }
        else if (CurrentGameBiz.Game is GameBiz.nap)
        {
            NavigationViewItem_BattleChronicle.Visibility = Visibility.Visible;
            NavigationViewItem_InterKnotMonthlyReport.Visibility = Visibility.Visible;
            NavigationViewItem_ShiyuDefense.Visibility = Visibility.Visible;
            NavigationViewItem_DeadlyAssault.Visibility = Visibility.Visible;
            // 绝区零战绩图片
            Image_BattleChronicle.Source = new BitmapImage(new("ms-appx:///Assets/Image/bc8f0b7384b306c80f2a1fcca9f3d14b_8590605504999484795.png"));
        }
    }




    #endregion




    #region Game Role Info



    /// <summary>
    /// 当前选中的游戏角色（含 Cookie），用于后续所有米游社 API 请求。
    /// </summary>
    public GameRecordRole? CurrentRole
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(AvatarUrl));
            }
        }
    }


    /// <summary>
    /// 当前游戏下所有已添加的角色列表（用于角色切换下拉）。
    /// </summary>
    public List<GameRecordRole> GameRoleList { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 头像地址：优先使用角色 HeadIcon，否则根据区服显示 Hyperion / HoYoLAB 默认图标。
    /// </summary>
    public string AvatarUrl => !string.IsNullOrWhiteSpace(CurrentRole?.HeadIcon) ? CurrentRole.HeadIcon : $"ms-appx:///Assets/Image/icon_{(CurrentGameBiz.IsGlobalServer() ? "hoyolab" : "hyperion")}.png";



    /// <summary>
    /// 加载当前游戏的角色列表。
    /// 优先使用传入角色或上次选择的角色，否则取第一个。
    /// </summary>
    private void LoadGameRoles(GameRecordRole? role = null)
    {
        try
        {
            if (role != null)
            {
                _gameRecordService.SetLastSelectGameRecordRole(CurrentGameBiz, role);
            }
            role ??= _gameRecordService.GetLastSelectGameRecordRoleOrTheFirstOne(CurrentGameBiz);
            var list = _gameRecordService.GetGameRoles(CurrentGameBiz);
            CurrentRole = role ?? list.FirstOrDefault();
            GameRoleList = list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load game roles ({gameBiz}).", CurrentGameBiz);
        }
    }




    [RelayCommand]
    private void WebLogin()
    {
        NavigateTo(typeof(LoginPage), CurrentGameBiz);
    }




    [RelayCommand]
    private async Task RefreshGameRoleInfoAsync()
    {
        try
        {
            if (CurrentRole is null)
            {
                await _gameRecordService.RefreshAllGameRolesInfoAsync();
            }
            else
            {
                await _gameRecordService.RefreshGameRoleInfoAsync(CurrentRole);
            }
            LoadGameRoles();
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Refresh game role info ({gameBiz}, {uid}).", CurrentRole?.GameBiz, CurrentRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Refresh game role info ({gameBiz}, {uid}).", CurrentRole?.GameBiz, CurrentRole?.Uid);
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh game role info ({gameBiz}, {uid}).", CurrentRole?.GameBiz, CurrentRole?.Uid);
            InAppToast.MainWindow?.Error(ex);
        }
    }



    private void ListView_GameRoles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is GameRecordRole role)
        {
            CurrentRole = role;
            _gameRecordService.SetLastSelectGameRecordRole(CurrentGameBiz, role);
            if (frame.SourcePageType?.Name is not nameof(LoginPage))
            {
                NavigateTo(frame.SourcePageType, force_navigate: true);
            }
        }
    }




    private void MenuFlyoutItem_CopyCookie_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { Tag: GameRecordRole role })
            {

                ClipboardHelper.SetText(role.Cookie);
            }
        }
        catch { }
    }



    private void MenuFlyoutItem_DeleteGameRole_Click(object sender, RoutedEventArgs e)
    {
        GameRecordRole? gameRole = null;
        try
        {
            if (sender is FrameworkElement { Tag: GameRecordRole role })
            {
                gameRole = role;
                _gameRecordService.DeleteGameRole(role);
                LoadGameRoles();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete game role ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }



    [RelayCommand]
    private async Task InputCookieAsync()
    {
        try
        {
            var textbox = new TextBox
            {
                IsSpellCheckEnabled = false,
            };
            var dialog = new ContentDialog
            {
                Title = Lang.HoyolabToolboxPage_InputCookie,
                Content = textbox,
                PrimaryButtonText = Lang.Common_Confirm,
                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result is ContentDialogResult.Primary)
            {
                var cookie = textbox.Text;
                if (string.IsNullOrWhiteSpace(cookie))
                {
                    _logger.LogInformation("Input cookie is null or white space.");
                    return;
                }
                var user = await _gameRecordService.AddRecordUserAsync(cookie);
                var roles = await _gameRecordService.AddGameRolesAsync(cookie);
                InAppToast.MainWindow?.Success(null, string.Format(Lang.LoginPage_AlreadyAddedGameRoles, roles.Count, string.Join("\r\n", roles.Select(x => $"{x.Nickname}  {x.Uid}"))), 5000);
                LoadGameRoles(roles.FirstOrDefault(x => x.GameBiz == CurrentGameBiz.ToString()));
            }
        }
        catch (miHoYoApiException ex)
        {
            _logger.LogError(ex, "Input cookie");
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Input cookie");
            InAppToast.MainWindow?.Warning(Lang.Common_NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Input cookie");
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 静默更新当前角色的头像（调用 index 接口获取最新 head icon）。
    /// 有内存 5 分钟缓存去重。
    /// </summary>
    private async Task RefreshGameRoleHeadIconSilentlyAsync()
    {
        try
        {
            if (CurrentRole is not null)
            {
                await _gameRecordService.UpdateGameRoleHeadIconAsync(CurrentRole);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update game role head icon silently ({gameBiz}, {uid}).", CurrentRole?.GameBiz, CurrentRole?.Uid);
        }
    }




    #endregion




    #region Navigate



    /// <summary>
    /// 导航到子页面（旅行者札记、开拓月历、绳网月报、深渊等）。
    /// 默认参数为当前角色。
    /// </summary>
    private void NavigateTo(Type? page, object? parameter = null, bool force_navigate = false)
    {
        if (page is null)
        {
            return;
        }
        if (!force_navigate && frame.SourcePageType == page)
        {
            return;
        }
        frame.Navigate(page, parameter ?? CurrentRole);
    }



    /// <summary>
    /// 根据当前游戏导航到默认统计页面：绝区零→绳网月报，原神→旅行者札记，铁道→开拓月历。
    /// 并同步选中左侧工具箱菜单项。
    /// </summary>
    private void NavigateToDefaultPage()
    {
        Type? type = CurrentGameBiz.Game switch
        {
            GameBiz.nap => typeof(InterKnotMonthlyReportPage),
            GameBiz.hk4e => typeof(TravelersDiaryPage),
            GameBiz.hkrpg => typeof(TrailblazeCalendarPage),
            _ => null,
        };
        if (type is null)
        {
            return;
        }
        NavigateTo(type);

        // 同步更新左侧导航栏选中状态，使菜单高亮与内容一致。
        NavigationViewItem? navItem = CurrentGameBiz.Game switch
        {
            GameBiz.nap => NavigationViewItem_InterKnotMonthlyReport,
            GameBiz.hk4e => NavigationViewItem_TravelersDiary,
            GameBiz.hkrpg => NavigationViewItem_TrailblazeMonthlyCalendar,
            _ => null,
        };
        if (navItem is not null)
        {
            NavigationView_Toolbox.SelectedItem = navItem;
        }
    }



    /// <summary>
    /// 左侧工具箱菜单点击时，根据 Tag 导航到对应页面（月报、深渊等）。
    /// </summary>
    private void NavigationView_Toolbox_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            var item = args.InvokedItemContainer as NavigationViewItem;
            if (item != null)
            {
                if (args.InvokedItemContainer?.IsSelected ?? false)
                {
                    return;
                }
                // Tag 与页面类型名对应，实现菜单到页面的映射。
                var type = item.Tag switch
                {
                    nameof(TravelersDiaryPage) => typeof(TravelersDiaryPage),
                    nameof(SpiralAbyssPage) => typeof(SpiralAbyssPage),
                    nameof(ImaginariumTheaterPage) => typeof(ImaginariumTheaterPage),
                    nameof(StygianOnslaughtPage) => typeof(StygianOnslaughtPage),
                    nameof(TrailblazeCalendarPage) => typeof(TrailblazeCalendarPage),
                    nameof(SimulatedUniversePage) => typeof(SimulatedUniversePage),
                    nameof(ForgottenHallPage) => typeof(ForgottenHallPage),
                    nameof(PureFictionPage) => typeof(PureFictionPage),
                    nameof(ApocalypticShadowPage) => typeof(ApocalypticShadowPage),
                    nameof(ChallengePeakPage) => typeof(ChallengePeakPage),
                    nameof(InterKnotMonthlyReportPage) => typeof(InterKnotMonthlyReportPage),
                    nameof(ShiyuDefensePage) => typeof(ShiyuDefensePage),
                    nameof(DeadlyAssaultPage) => typeof(DeadlyAssaultPage),
                    _ => null,
                };
                NavigateTo(type);
            }
        }
        catch { }
    }



    private void NavigationViewItem_BattleChronicle_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        ShowBattleChronicleWindow();
    }



    private BattleChronicleWindow? _battleChronicleWindow;



    /// <summary>
    /// 显示战绩窗口（深渊/忘却/虚构等详细战斗数据）。
    /// 支持特定错误码（如 1034）时由外部触发。
    /// </summary>
    private void ShowBattleChronicleWindow()
    {
        // 窗口关闭后 AppWindow is null，需要重新创建实例
        if (_battleChronicleWindow?.AppWindow is null)
        {
            _battleChronicleWindow = new BattleChronicleWindow
            {
                CurrentRole = CurrentRole,
            };
        }
        else if (_battleChronicleWindow.CurrentRole != CurrentRole)
        {
            _battleChronicleWindow.CurrentRole = CurrentRole;
        }
        _battleChronicleWindow.Activate();
    }




    #endregion




    #region Device Info




    private async void NavigationViewItem_UpdateDeviceInfo_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        await UpdateDeviceInfoAsync(true);
    }



    /// <summary>
    /// 更新设备指纹（仅国内服）。首次或超过 3 天会调用 public-data-api 获取新 fp。
    /// 用于后续所有 Hyperion 请求的 x-rpc-device-fp 头，降低风控概率。
    /// </summary>
    private async Task UpdateDeviceInfoAsync(bool forceUpdate = false)
    {
        try
        {
            await _gameRecordService.UpdateDeviceFpAsync(forceUpdate);
            if (forceUpdate)
            {
                InAppToast.MainWindow?.Success(Lang.HoyolabToolboxPage_TheDeviceFingerprintIsAlreadyUpdated);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update device info");
            if (forceUpdate)
            {
                InAppToast.MainWindow?.Error(ex);
            }
        }
    }





    #endregion





    /// <summary>
    /// 统一处理战绩相关的 <see cref="miHoYoApiException"/>（风控验证 / 账号异常 toast）。
    /// </summary>
    /// <param name="ex">米哈游 API 异常。</param>
    public static void HandleMiHoYoApiException(miHoYoApiException ex)
    {
        if (ex.ReturnCode is 1034 or 5003 or 10035 or 10041 or 10053)
        {
            InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning, Lang.Common_AccountError, ex.Message, Lang.HoyolabToolboxPage_VerifyAccount, () =>
            {
                WeakReferenceMessenger.Default.Send(new GameRecordVerifyAccountMessage());
            });
        }
        else
        {
            InAppToast.MainWindow?.Warning(Lang.Common_AccountError, ex.Message);
        }
    }


}
