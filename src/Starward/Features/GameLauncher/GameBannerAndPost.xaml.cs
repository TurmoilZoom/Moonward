using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core.HoYoPlay;
using Starward.Features.HoYoPlay;
using Starward.Features.ViewHost;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vanara.PInvoke;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 游戏启动页「Banner + 资讯」面板：上半区为 <see cref="BannerCarousel"/> 轮播图，下半区为按分类展示的帖子列表。
/// 负责从 HoYoPlay 拉取内容、控制整体显隐，并协调 <see cref="BannerCarousel"/> 的自动轮播生命周期。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class GameBannerAndPost : UserControl
{


    private readonly ILogger<GameBannerAndPost> _logger = AppConfig.GetLogger<GameBannerAndPost>();


    private readonly HoYoPlayService _hoYoPlayService = AppConfig.GetService<HoYoPlayService>();


    /// <summary>当前展示内容所属游戏；由 <see cref="GameLauncherPage"/> 在导航时赋值。</summary>
    public GameId CurrentGameId { get; set; }



    /// <summary>初始化控件并订阅 Loaded / Unloaded 生命周期。</summary>
    public GameBannerAndPost()
    {
        this.InitializeComponent();
        this.Loaded += GameBannerAndPost_Loaded;
        this.Unloaded += GameBannerAndPost_Unloaded;
    }





    /// <summary>
    /// 加载时注册消息监听（主窗口状态、公告设置变更等），拉取游戏内容并更新游戏内公告按钮显隐。
    /// </summary>
    private async void GameBannerAndPost_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnMainWindowStateChanged);
        WeakReferenceMessenger.Default.Register<GameNoticeWindowClosedMessage>(this, OnGameNoticeWindowClosed);
        WeakReferenceMessenger.Default.Register<GameAnnouncementSettingChangedMessage>(this, OnGameAnnouncementSettingChanged);
        await UpdateGameContentAsync();
        UpdateGameNoticeButtonVisibility();
    }


    /// <summary>卸载时注销消息、清空绑定数据，避免持有已释放的轮播资源。</summary>
    private void GameBannerAndPost_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Banners = null;
        PostGroups = null;
    }



    /// <summary>
    /// 主窗口状态变化时暂停或恢复 <see cref="BannerCarousel"/> 自动轮播：
    /// 激活时恢复，隐藏或会话锁定时暂停（避免后台空转）。
    /// </summary>
    private void OnMainWindowStateChanged(object _, MainWindowStateChangedMessage message)
    {
        try
        {
            if (message.Activate)
            {
                BannerCarousel.ResumeAutoPlay();
            }
            else if (message.Hide || message.SessionLock)
            {
                BannerCarousel.PauseAutoPlay();
            }
        }
        catch { }
    }



    /// <summary>游戏内公告窗口关闭后，将焦点拉回主窗口。</summary>
    private void OnGameNoticeWindowClosed(object _, GameNoticeWindowClosedMessage message)
    {
        User32.SetForegroundWindow(XamlRoot.GetWindowHandle());
    }



    /// <summary>
    /// 用户切换「显示游戏公告」设置时响应：开启则刷新内容并显示面板，关闭则隐藏。
    /// </summary>
    private async void OnGameAnnouncementSettingChanged(object _, GameAnnouncementSettingChangedMessage message)
    {
        // 没有设置取消，网络不好时可能会造成状态异常，懒得写了
        if (AppConfig.EnableBannerAndPost)
        {
            ShowBannerAndPost = true;
            await UpdateGameContentAsync();
        }
        else
        {
            ShowBannerAndPost = false;
        }
    }



    /// <summary>轮播图数据，单向绑定到 <see cref="BannerCarousel.Banners"/>。</summary>
    public List<GameBanner>? Banners { get; set => SetProperty(ref field, value); }





    /// <summary>按分类聚合后的资讯列表，绑定到下半区 Pivot。</summary>
    public List<GamePostGroup>? PostGroups { get; set => SetProperty(ref field, value); }





    /// <summary>
    /// 控制整个面板的可见性与命中测试。显示时需同时满足 Banner 与帖子均有数据，
    /// 并恢复 <see cref="BannerCarousel"/> 自动轮播；隐藏时暂停轮播。
    /// </summary>
    public bool ShowBannerAndPost
    {
        get => this.Opacity == 1;
        set
        {
            if (value && Banners?.Count > 0 && PostGroups?.Count > 0)
            {
                BannerCarousel.ResumeAutoPlay();
                this.Opacity = 1;
                this.IsHitTestVisible = true;
            }
            else
            {
                BannerCarousel.PauseAutoPlay();
                this.Opacity = 0;
                this.IsHitTestVisible = false;
            }
        }
    }





    /// <summary>
    /// 从 HoYoPlay 拉取当前游戏的 Banner 与资讯，更新绑定属性并决定面板显隐。
    /// </summary>
    /// <returns>异步任务；失败时记录日志，不改变已有可见状态（除 content 为空或设置关闭时）。</returns>
    private async Task UpdateGameContentAsync()
    {
        try
        {
            var content = await _hoYoPlayService.GetGameContentAsync(CurrentGameId);
            if (content is null || !AppConfig.EnableBannerAndPost)
            {
                ShowBannerAndPost = false;
                return;
            }
            Banners = content.Banners;
            PostGroups = GamePostGroup.FromGameContent(content);
            ShowBannerAndPost = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game launcher content ({CurrentGameId})", CurrentGameId);
        }
    }




    /// <summary>
    /// 仅控制「游戏内公告」按钮的显隐；红点提醒已移除，始终不显示。
    /// </summary>
    private void UpdateGameNoticeButtonVisibility()
    {
        try
        {
            Button_InGameNotices.Visibility = GameFeatureConfig.FromGameId(CurrentGameId).InGameNoticesWindow
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update game notice button visibility ({CurrentGameId})", CurrentGameId);
        }
    }




    /// <summary>打开当前游戏的独立游戏内公告窗口。</summary>
    [RelayCommand]
    private void OpenGameNoticeWindow()
    {
        try
        {
            new GameNoticeWindow
            {
                CurrentGameBiz = CurrentGameId.GameBiz,
                ParentWindowHandle = (nint)this.XamlRoot.ContentIslandEnvironment.AppWindowId.Value
            }.Activate();
        }
        catch { }
    }





}