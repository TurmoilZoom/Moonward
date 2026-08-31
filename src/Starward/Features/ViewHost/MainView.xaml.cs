using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using NuGet.Versioning;
using Starward.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Gacha;
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord;
using Starward.Features.GameSetting;
using Starward.Features.Screenshot;
using Starward.Features.SelfQuery;
using Starward.Features.Setting;
using Starward.Features.Update;
using Starward.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace Starward.Features.ViewHost;

/// <summary>
/// 主窗口内容壳：左侧导航、内容 Frame、游戏选择与更新检查。
/// 导航可见性由 <see cref="GameFeatureConfig"/> 按当前 <see cref="GameId"/> 决定；页面切换走 <see cref="NavigateTo"/>。
/// 与主窗口无关的常驻后台职责（热键、手柄、RPC 环境、抽卡名缓存、批量签到）见 <see cref="Startup.ResidentHost"/>。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class MainView : UserControl
{


    private readonly ILogger<MainView> _logger = AppConfig.GetLogger<MainView>();


    /// <summary>
    /// 当前选中的游戏标识（含区服）。驱动背景、导航项显隐与页面导航参数。
    /// </summary>
    public GameId? CurrentGameId { get; private set => SetProperty(ref field, value); }


    /// <summary>
    /// 当前游戏对应的功能开关与支持页面列表，由 <see cref="GameFeatureConfig.FromGameId"/> 生成。
    /// </summary>
    private GameFeatureConfig CurrentGameFeatureConfig { get; set; }



    /// <summary>
    /// 初始化主视图：加载 XAML 并注册消息、同步初始游戏与导航状态。
    /// </summary>
    public MainView()
    {
        this.InitializeComponent();
        InitializeMainView();
    }


    /// <summary>
    /// 完成主视图启动配置：订阅 Loaded、从游戏选择器取当前游戏、刷新导航，并注册 Messenger。
    /// </summary>
    private void InitializeMainView()
    {
        this.Loaded += MainView_Loaded;
        GameId? gameId = GameSelector.CurrentGameId;
        // 崩坏3 国际服同一 GameBiz 下有多区服，用上次选择的 GameId.Id 覆盖
        if (gameId?.GameBiz == GameBiz.bh3_global)
        {
            string? id = AppConfig.LastGameIdOfBH3Global;
            if (!string.IsNullOrWhiteSpace(id))
            {
                gameId.Id = id;
            }
        }
        CurrentGameId = gameId;
        CurrentGameFeatureConfig = GameFeatureConfig.FromGameId(CurrentGameId);
        UpdateNavigationView();
        WeakReferenceMessenger.Default.Register<MainViewNavigateMessage>(this, OnMainViewNavigateMessageReceived);
        WeakReferenceMessenger.Default.Register<BH3GlobalGameServerChangedMessage>(this, OnBH3GlobalGameServerChanged);
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, (_, _) => _ = CheckUpdateOrShowRecentUpdateContentAsync());
        // 切换软件语言后刷新导航文案/Tooltip，并异步把三个游戏的抽卡物品名称回写为新语言
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }




    /// <summary>
    /// 首次加载到视觉树后：检查更新 / 展示更新说明。
    /// <para>
    /// 热键、手柄、GameBar 引导键接管、RPC 环境、抽卡名缓存与批量签到等**与主窗口无关**的常驻职责
    /// 已移至 <see cref="Startup.ResidentHost"/>，由系统托盘窗口拉起 —— 挂在这里会导致仅托盘驻留
    /// 或快捷方式启动时统统缺席（见 issue #10）。此处只保留需要主界面 UI 的部分。
    /// </para>
    /// </summary>
    /// <param name="sender">事件源（本控件）。</param>
    /// <param name="e">路由事件参数。</param>
    private void MainView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = CheckUpdateOrShowRecentUpdateContentAsync();
    }




    /// <summary>
    /// 游戏选择器切换当前游戏：更新功能配置与导航，并后台刷新该游戏抽卡物品信息。
    /// </summary>
    /// <param name="sender">游戏选择器。</param>
    /// <param name="e">新游戏 Id，以及是否双击（本方法未使用 DoubleTapped）。</param>
    private void GameSelector_CurrentGameChanged(object? sender, (GameId, bool DoubleTapped) e)
    {
        if (e.Item1.GameBiz == GameBiz.bh3_global)
        {
            // 崩坏3国际服区服
            string? id = AppConfig.LastGameIdOfBH3Global;
            if (!string.IsNullOrWhiteSpace(id))
            {
                e.Item1.Id = id;
            }
        }
        CurrentGameId = e.Item1;
        CurrentGameFeatureConfig = GameFeatureConfig.FromGameId(CurrentGameId);
        UpdateNavigationView();
        // 切换游戏：后台刷新该游戏抽卡角色/物品信息（仅原神/星铁/绝区零，协调方法内部已过滤、并发去重，异常仅记日志）。
        _ = AppConfig.GetService<GachaItemNameService>().RefreshGachaInfoForGameAsync(e.Item1.GameBiz);
    }



    /// <summary>
    /// 崩坏3 国际服区服变更：写回当前 <see cref="CurrentGameId"/> 并强制回到启动器页（无转场动画）。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="message">携带新区服对应的 GameId 字符串。</param>
    private void OnBH3GlobalGameServerChanged(object _, BH3GlobalGameServerChangedMessage message)
    {
        if (CurrentGameId?.GameBiz == GameBiz.bh3_global)
        {
            CurrentGameId.Id = message.GameId;
            // 通知 x:Bind（如 AppBackground）刷新
            OnPropertyChanged(nameof(CurrentGameId));
            NavigateTo(typeof(GameLauncherPage), CurrentGameId, new SuppressNavigationTransitionInfo());
        }
    }




    #region Navigation





    /// <summary>
    /// 语言切换后刷新导航栏 x:Bind 绑定与代码赋值的文案/Tooltip。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="__">语言变更消息（未使用）。</param>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
        UpdateNavigationLabels();
        _ = Task.Run(() => AppConfig.GetService<GachaItemNameService>().ChangeLanguageAsync());
    }


    /// <summary>
    /// 按当前游戏的 <see cref="GameFeatureConfig.SupportedPages"/> 显隐导航项，
    /// 刷新动态文案，并在非设置页时按当前选中页类型重新导航（无游戏则进空白页）。
    /// </summary>
    private void UpdateNavigationView()
    {
        NavigationViewItem_Launcher.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GameLauncherPage)).ToVisibility();
        NavigationViewItem_GameSetting.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GameSettingPage)).ToVisibility();
        NavigationViewItem_Screenshot.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(ScreenshotPage)).ToVisibility();
        NavigationViewItem_GachaLog.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GachaLogPage)).ToVisibility();
        NavigationViewItem_HoyolabToolbox.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GameRecordPage)).ToVisibility();
        NavigationViewItem_SelfQuery.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(SelfQueryPage)).ToVisibility();
        NavigationViewItem_GenshinBeyondGacha.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GenshinBeyondGachaPage)).ToVisibility();

        UpdateNavigationLabels();

        if (CurrentGameId is null)
        {
            NavigateTo(typeof(BlankPage));
        }
        // 设置页跨游戏通用，切换游戏时保留；其余页用当前 SourcePageType 重入以带上新 GameId
        else if (MainView_Frame.SourcePageType?.Name is not nameof(SettingPage))
        {
            NavigateTo(MainView_Frame.SourcePageType);
        }
    }


    /// <summary>
    /// 更新导航项中由代码赋值的展开文案与 Tooltip（抽卡记录、工具箱等随游戏/区服变化的项）。
    /// </summary>
    private void UpdateNavigationLabels()
    {
        // 抽卡记录名称：各游戏官方用语不同（祈愿 / 跃迁 / 信号检索）
        string gachalogText = CurrentGameId?.GameBiz.Game switch
        {
            GameBiz.hk4e => Lang.GachaLogService_WishRecords,
            GameBiz.hkrpg => Lang.GachaLogService_WarpRecords,
            GameBiz.nap => Lang.GachaLogService_SignalSearchRecords,
            _ => "",
        };

        TextBlock_GachaLog.Text = gachalogText;
        // Compact 侧栏不显示 Content 文字，需同步 InstantTooltip 供悬停展示
        InstantTooltip.SetText(NavigationViewItem_GachaLog, gachalogText);

        if (CurrentGameId?.GameBiz.IsChinaServer() ?? false)
        {
            TextBlock_HoyolabToolbox.Text = Lang.HyperionToolbox;
            InstantTooltip.SetText(NavigationViewItem_HoyolabToolbox, Lang.HyperionToolbox);
        }
        if (CurrentGameId?.GameBiz.IsGlobalServer() ?? false)
        {
            TextBlock_HoyolabToolbox.Text = Lang.HoYoLABToolbox;
            InstantTooltip.SetText(NavigationViewItem_HoyolabToolbox, Lang.HoYoLABToolbox);
        }
    }



    /// <summary>
    /// 导航栏项被点选：根据 <see cref="NavigationViewItem.Tag"/> 映射页面类型并导航。
    /// 已选中项再次点击忽略；内置 Settings 调用路径保留但当前 XAML 已关闭 <c>IsSettingsVisible</c>。
    /// </summary>
    /// <param name="sender">导航视图。</param>
    /// <param name="args">调用项与是否设置页等参数。</param>
    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            // 重复点击当前选中项不重新导航，避免无意义的页面重建
            if (args.InvokedItemContainer?.IsSelected ?? false)
            {
                return;
            }
            if (args.IsSettingsInvoked)
            {
                NavigateTo(typeof(SettingPage));
            }
            else
            {
                if (args.InvokedItemContainer is NavigationViewItem item)
                {
                    var type = item.Tag switch
                    {
                        nameof(GameLauncherPage) => typeof(GameLauncherPage),
                        nameof(GameSettingPage) => typeof(GameSettingPage),
                        nameof(ScreenshotPage) => typeof(ScreenshotPage),
                        nameof(GachaLogPage) => typeof(GachaLogPage),
                        nameof(GameRecordPage) => typeof(GameRecordPage),
                        nameof(SelfQueryPage) => typeof(SelfQueryPage),
                        nameof(GenshinBeyondGachaPage) => typeof(GenshinBeyondGachaPage),
                        nameof(SettingPage) => typeof(SettingPage),
                        _ => null,
                    };
                    NavigateTo(type);
                }
            }
        }
        catch { }
    }



    /// <summary>
    /// 导航到指定页面，并同步导航选中态与内容区遮罩。
    /// </summary>
    /// <param name="page">目标页类型；为 <see langword="null"/> 时回退为启动器页。</param>
    /// <param name="param">导航参数，默认传当前 <see cref="CurrentGameId"/>。</param>
    /// <param name="infoOverride">可选转场信息（如区服切换时的 <see cref="SuppressNavigationTransitionInfo"/>）。</param>
    private void NavigateTo(Type? page, object? param = null, NavigationTransitionInfo? infoOverride = null)
    {
        page ??= typeof(GameLauncherPage);
        if (page.Name is nameof(BlankPage) && CurrentGameId is null)
        {
            // 无游戏时允许空白页，跳过 SupportedPages 校验
        }
        else if (page.Name is not nameof(SettingPage) && !CurrentGameFeatureConfig.SupportedPages.Contains(page.Name))
        {
            // 当前游戏不支持该页时回退启动器（设置页始终可用）
            page = typeof(GameLauncherPage);
        }
        // 仅对启动器/设置强制同步 SelectedItem；其余项由用户点击时系统维护
        if (page.Name is nameof(GameLauncherPage))
        {
            MainView_NavigationView.SelectedItem = NavigationViewItem_Launcher;
        }
        else if (page.Name is nameof(SettingPage))
        {
            MainView_NavigationView.SelectedItem = NavigationViewItem_Setting;
        }
        MainView_Frame.Navigate(page, param ?? CurrentGameId, infoOverride);
        // 启动器与空白页展示完整背景；其它功能页加亚克力遮罩便于阅读
        if (page.Name is nameof(BlankPage) or nameof(GameLauncherPage))
        {
            Border_OverlayMask.Opacity = 0;
        }
        else
        {
            Border_OverlayMask.Opacity = 1;
        }
    }


    /// <summary>
    /// 响应跨模块导航请求（如其它页面通过 Messenger 要求跳转）。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="message">目标页面类型。</param>
    private void OnMainViewNavigateMessageReceived(object _, MainViewNavigateMessage message)
    {
        NavigateTo(message.Page, message.Parameter);
    }




    #endregion




    #region Update


    /// <summary>上次成功发起更新检查的时间，用于 1 小时节流。</summary>
    private DateTimeOffset _lastCheckUpdateTime;

    /// <summary>上次弹出更新窗口的时间，用于 6 小时且跨日节流。</summary>
    private DateTimeOffset _lastShowUpdateTime;

    /// <summary>更新检查互斥锁，避免窗口状态变化触发的并发检查叠跑。</summary>
    private SemaphoreSlim _updateLock = new(1, 1);


    /// <summary>
    /// 若刚完成静默更新则弹出更新内容；否则在开启「推送更新」时检查新版本。
    /// 静默更新（需同时开启推送）优先：后台下载，退出后安装；否则按节流规则弹出 <see cref="UpdateWindow"/>。
    /// Debug / <c>DONOT_CHECK_UPDATE</c> 构建直接跳过。
    /// </summary>
    /// <returns>表示异步检查的任务。</returns>
    private async Task CheckUpdateOrShowRecentUpdateContentAsync()
    {
#if DEBUG || DONOT_CHECK_UPDATE
        return;
#endif
#pragma warning disable CS0162 // 检测到无法访问的代码
        // Wait(0)：已有检查在跑则立即返回，不排队
        if (!await _updateLock.WaitAsync(0))
        {
            return;
        }
        await Task.Delay(1000);
#pragma warning restore CS0162 // 检测到无法访问的代码
        try
        {
            if (TryShowSilentUpdateContent())
            {
                // 与原先一致：展示更新内容后推迟约 5 分钟再检查新版本
                _lastCheckUpdateTime = DateTimeOffset.Now - TimeSpan.FromMinutes(55);
                return;
            }
            if (!AppConfig.PendingSilentUpdateContent)
            {
                AppConfig.LastAppVersion = AppConfig.AppVersion;
            }
            if (!AppConfig.EnableUpdateNotification)
            {
                return;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            if (now - _lastCheckUpdateTime > TimeSpan.FromHours(1))
            {
                var service = AppConfig.GetService<UpdateService>();
                var release = await service.CheckUpdateAsync(false);
                _lastCheckUpdateTime = now;
                if (release is null)
                {
                    return;
                }
                if (AppConfig.EnableSilentUpdate && service.IsUpdaterAvailable)
                {
                    _ = service.TryStartSilentUpdateAsync(release);
                    return;
                }
                // 有新版本时：距上次弹窗超过 6 小时且不在同一天，才再弹窗
                if (now - _lastShowUpdateTime > TimeSpan.FromHours(6) && now.Date != _lastShowUpdateTime.Date)
                {
                    new UpdateWindow { NewVersion = release }.Activate();
                    _lastShowUpdateTime = now;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check update");
        }
        finally
        {
            _updateLock.Release();
        }
    }


    /// <summary>
    /// 静默更新安装完成后弹出仅含发行说明的 <see cref="UpdateWindow"/>（无下载/安装按钮）。
    /// </summary>
    /// <returns>已弹出则为 <see langword="true"/>。</returns>
    private static bool TryShowSilentUpdateContent()
    {
        if (!AppConfig.PendingSilentUpdateContent)
        {
            return false;
        }
        if (!NuGetVersion.TryParse(AppConfig.AppVersion, out NuGetVersion? appVersion))
        {
            return false;
        }
        _ = NuGetVersion.TryParse(AppConfig.LastAppVersion, out NuGetVersion? lastVersion);
        if (lastVersion is not null && appVersion == lastVersion)
        {
            return false;
        }
        var window = new UpdateWindow();
        // 先构造以快照 LastAppVersion，再清标记，避免并发检查把起始版本改成当前版本
        AppConfig.PendingSilentUpdateContent = false;
        window.Activate();
        return true;
    }


    #endregion



}


/// <summary>
/// 将布尔值转为 <see cref="Visibility"/>（true→Visible，false→Collapsed），供导航项显隐绑定使用。
/// </summary>
file static class BoolToVisibilityExtension
{

    /// <summary>
    /// 转换可见性。
    /// </summary>
    /// <param name="value">为 true 时显示，否则折叠。</param>
    /// <returns>对应的 <see cref="Visibility"/>。</returns>
    public static Visibility ToVisibility(this bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

}
