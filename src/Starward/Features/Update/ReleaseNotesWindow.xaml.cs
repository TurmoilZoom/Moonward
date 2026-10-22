using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using NuGet.Versioning;
using Starward.Controls;
using Starward.Features.Setting;
using Starward.Frameworks;
using Starward.Helpers;
using Starward.Setup.Core;
using Starward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.System;


namespace Starward.Features.Update;

/// <summary>
/// 发行说明窗口（参考 PowerToys 的 Release notes）：左侧按「主版本.次版本」分组列出最近的版本，右侧渲染该组全部版本的说明。
/// 随时可从关于页打开，与「有新版本」「静默更新后」才出现的 <see cref="UpdateWindow"/> 互不影响。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class ReleaseNotesWindow : WindowEx
{


    private readonly ILogger<ReleaseNotesWindow> _logger = AppConfig.GetLogger<ReleaseNotesWindow>();

    private readonly ReleaseClient _releaseClient = AppConfig.GetService<ReleaseClient>();


    /// <summary>一次拉取的发行版本数，约覆盖最近几个月；更早的去 GitHub 看。</summary>
    private const int ReleaseCount = 50;

    /// <summary>各分组正文的渲染任务。GitHub 匿名接口每小时只有 60 次：同一组只渲染一次，预取与用户点选共用同一个任务。</summary>
    private readonly Dictionary<string, Task<string>> _renderTasks = new();

    /// <summary>外壳页的加载任务；加载失败时置空，重试时重新加载（页会重建，已渲染的正文从缓存重新填入）。</summary>
    private Task? _shellTask;

    /// <summary>本次外壳页里已经发起过预取，避免重复。外壳重载后重新预取。</summary>
    private bool _prefetchStarted;

    /// <summary>左侧导航的流体悬停高亮条，与设置页同一个效果类。</summary>
    private readonly FluidNavigationViewHoverEffect _navHoverEffect = new();

    /// <summary>每次切换分组递增，渲染完成时序号已变说明用户又切走了，丢弃这次结果。</summary>
    private int _renderVersion;

    private bool _webviewClosed;

    private bool _positioned;



    /// <summary>
    /// 当前已打开的发行说明窗口，同一时刻只应有一个；关闭时置空。只在 UI 线程访问。
    /// </summary>
    public static new ReleaseNotesWindow? Current { get; private set; }


    /// <summary>
    /// 已有窗口就带到前台，否则新开一个。
    /// </summary>
    public static void ShowOrActivate()
    {
        if (Current is not null)
        {
            try
            {
                if (Current.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
                {
                    presenter.Restore();
                }
                Current.Activate();
                return;
            }
            catch (Exception ex)
            {
                // 窗口可能正处在关闭途中，此时 Activate 会抛；当作没有窗口，新开一个。
                AppConfig.GetLogger<ReleaseNotesWindow>().LogWarning(ex, "Activate existing release notes window");
                Current = null;
            }
        }
        new ReleaseNotesWindow().Activate();
    }



    public ReleaseNotesWindow()
    {
        this.InitializeComponent();
        InitializeWindow();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => this.Bindings.Update());
        this.Closed += ReleaseNotesWindow_Closed;
        Current = this;
    }



    private void InitializeWindow()
    {
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        Title = $"Moonward · {Lang.AboutSetting_ReleaseNotes}";
        RootGrid.RequestedTheme = ShouldAppsUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AdaptTitleBarButtonColorToActuallTheme();
        SetIcon();
    }



    /// <summary>
    /// 首次显示时居中到当前工作区；小屏 / 高 DPI 时按原比例缩小到工作区宽 80% / 高 90% 以内（同 <see cref="UpdateWindow"/>）。
    /// 已经显示过的窗口再激活时不挪位置。
    /// </summary>
    public new void Activate()
    {
        if (!_positioned)
        {
            _positioned = true;
            RectInt32 workArea = GetDisplayAreaForCentering().WorkArea;
            double scale = UIScale;
            int w = (int)(960 * scale);
            int h = (int)(680 * scale);
            int maxW = (int)(workArea.Width * 0.8);
            int maxH = (int)(workArea.Height * 0.9);
            if (w > maxW || h > maxH)
            {
                double fit = Math.Min((double)maxW / w, (double)maxH / h);
                w = Math.Max(1, (int)(w * fit));
                h = Math.Max(1, (int)(h * fit));
            }
            AppWindow.MoveAndResize(new RectInt32(workArea.X + (workArea.Width - w) / 2, workArea.Y + (workArea.Height - h) / 2, w, h));
        }
        base.Activate();
    }



    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _ = LoadAsync();
    }



    private void ReleaseNotesWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _navHoverEffect.Detach();
        this.Closed -= ReleaseNotesWindow_Closed;
        _webviewClosed = true;
        WebView2Helper.Close(webview);
    }




    /// <summary>
    /// 左侧的版本分组。
    /// </summary>
    public List<ReleaseNotesGroup> Groups { get; set => SetProperty(ref field, value); } = new();



    /// <summary>
    /// 初始化 WebView2、拉取发行版本并分组，然后选中最新的一组。
    /// </summary>
    private async Task LoadAsync()
    {
        try
        {
            ShowLoading();
            await webview.EnsureCoreWebView2Async();
            if (WebView2Helper.CloseIfRequested(webview, _webviewClosed)) return;
            string ua = webview.CoreWebView2.Settings.UserAgent;
            if (!ua.Contains("Moonward"))
            {
                webview.CoreWebView2.Settings.UserAgent = $"{ua} Moonward/{AppConfig.AppVersion}";
            }
            webview.CoreWebView2.Profile.PreferredColorScheme = ShouldAppsUseDarkMode() ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            webview.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
            webview.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

            List<GithubRelease> releases = await _releaseClient.GetGithubReleaseAsync(1, ReleaseCount);
            if (_webviewClosed) return;
            // 正在用预览版的人即使关了预览渠道，也该看得到自己这个版本的说明
            bool includePrerelease = AppConfig.EnablePreviewRelease
                || (NuGetVersion.TryParse(AppConfig.AppVersion, out NuGetVersion? appVersion) && appVersion.IsPrerelease);
            List<ReleaseNotesGroup> groups = ReleaseNotesFormatter.GroupReleases(releases, includePrerelease);
            foreach (ReleaseNotesGroup group in groups)
            {
                group.Subtitle = group.IsAllPrerelease
                    ? Lang.UpdatePage_PreviewChannel
                    : group.LatestPublishedAt.ToLocalTime().ToString("Y", CultureInfo.CurrentUICulture);
            }
            Groups = groups;
            if (groups.Count == 0)
            {
                ShowError(Lang.DownloadGamePage_UnknownError);
                return;
            }
            // 分组变了，外壳页按新的分组数重建
            _shellTask = null;
            BuildNavItems(groups);
            // 先布局一次把菜单项建出来再选中：容器还没生成时设 SelectedItem，内容照样切换，但菜单项没有选中态和指示条。
            // 设好后触发 SelectionChanged，由它渲染
            NavView.UpdateLayout();
            NavView.SelectedItem = NavView.MenuItems[0];
        }
        catch (COMException ex)
        {
            _logger.LogError(ex, "Load release notes");
            ShowError(Lang.Common_WebView2ComponentInitializationFailed);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or TaskCanceledException)
        {
            _logger.LogError(ex, "Load release notes");
            ShowError(Lang.Common_NetworkError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load release notes");
            ShowError(Lang.DownloadGamePage_UnknownError);
        }
    }



    /// <summary>
    /// 按分组生成左侧菜单项，并挂上流体悬停高亮条。
    /// 不用 MenuItemsSource：高亮条只给 MenuItems 里现成的 <see cref="NavigationViewItem"/> 接线，附着时就要拿到全部菜单项。
    /// </summary>
    /// <param name="groups">版本分组，新的在前。</param>
    private void BuildNavItems(List<ReleaseNotesGroup> groups)
    {
        _navHoverEffect.Detach();
        NavView.MenuItems.Clear();
        var template = (DataTemplate)RootGrid.Resources["GroupItemTemplate"];
        foreach (ReleaseNotesGroup group in groups)
        {
            var item = new NavigationViewItem
            {
                Content = group,
                ContentTemplate = template,
                Tag = group,
            };
            AutomationProperties.SetName(item, group.Key);
            NavView.MenuItems.Add(item);
        }
        _navHoverEffect.Attach(NavView, NavIndicatorHost, _logger);
    }



    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: ReleaseNotesGroup group })
        {
            _ = ShowGroupAsync(group);
        }
    }



    /// <summary>
    /// 竖直平移到一组版本的说明（同上游游戏设置弹窗里竖排的 FlipView，跨几组时一路经过中间的页）。
    /// 还没渲染好的页先显示转圈，渲染完填进去；首次显示或出错后恢复时直接跳过去、不播动画。
    /// </summary>
    /// <param name="group">要显示的分组。</param>
    private async Task ShowGroupAsync(ReleaseNotesGroup group)
    {
        int version = ++_renderVersion;
        int index = Groups.IndexOf(group);
        try
        {
            bool firstShow = webview.Visibility is not Visibility.Visible;
            if (firstShow)
            {
                ShowLoading();
            }
            await EnsureShellAsync();
            if (version != _renderVersion || WebView2Helper.CloseIfRequested(webview, _webviewClosed)) return;
            bool animate = !firstShow && EntranceAnimation.AnimationsEnabled();
            await webview.ExecuteScriptAsync(ReleaseNotesHtml.BuildGoToScript(index, animate));
            string html = await GetGroupHtmlAsync(group);
            if (_webviewClosed) return;
            // 渲染期间用户可能又切走了，内容照样填进对应的页，下次切回来不用再等
            await webview.ExecuteScriptAsync(ReleaseNotesHtml.BuildFillScript(index, html));
            if (version != _renderVersion || _webviewClosed) return;
            ShowContent();
            PrefetchOtherGroups();
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or TaskCanceledException)
        {
            _logger.LogError(ex, "Render release notes {group}", group.Key);
            if (version == _renderVersion)
            {
                ShowError(Lang.Common_NetworkError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Render release notes {group}", group.Key);
            if (version == _renderVersion)
            {
                ShowError(Lang.DownloadGamePage_UnknownError);
            }
        }
    }



    /// <summary>
    /// 取一组的正文 HTML：同一组只向 GitHub 渲染一次，失败的下次重新渲染。
    /// </summary>
    /// <param name="group">分组。</param>
    /// <returns>GitHub 渲染出的正文 HTML。</returns>
    private Task<string> GetGroupHtmlAsync(ReleaseNotesGroup group)
    {
        if (!_renderTasks.TryGetValue(group.Key, out Task<string>? task) || task.IsFaulted || task.IsCanceled)
        {
            string markdown = ReleaseNotesFormatter.BuildGroupMarkdown(group.Releases, Lang.UpdatePage_PreviewChannel, Lang.UpdatePage_ReleasePage);
            task = _releaseClient.RenderGithubMarkdownAsync(markdown);
            _renderTasks[group.Key] = task;
        }
        return task;
    }



    /// <summary>
    /// 第一页显示出来后，按顺序在后台渲染其余分组并填进各自的页，平移经过中间的页时才有内容可看。
    /// 只是预取：失败不提示，用户点到那一组时再渲染一次。
    /// </summary>
    private async void PrefetchOtherGroups()
    {
        if (_prefetchStarted)
        {
            return;
        }
        _prefetchStarted = true;
        List<ReleaseNotesGroup> groups = Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            try
            {
                string html = await GetGroupHtmlAsync(groups[i]);
                if (_webviewClosed || !ReferenceEquals(groups, Groups)) return;
                await webview.ExecuteScriptAsync(ReleaseNotesHtml.BuildFillScript(i, html));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Prefetch release notes {group}", groups[i].Key);
            }
        }
    }



    /// <summary>
    /// 确保外壳页已加载（见 <see cref="ReleaseNotesHtml.BuildFlipShellAsync"/>）。
    /// </summary>
    /// <returns>外壳页加载完成的任务。</returns>
    private Task EnsureShellAsync()
    {
        return _shellTask ??= LoadShellAsync();
    }



    /// <summary>
    /// 导航到外壳页、等它加载完成，再按分组数建好空页；失败时清掉 <see cref="_shellTask"/>，下次重新加载。
    /// </summary>
    /// <exception cref="InvalidOperationException">外壳页导航失败。</exception>
    private async Task LoadShellAsync()
    {
        try
        {
            _prefetchStarted = false;
            string shell = await ReleaseNotesHtml.BuildFlipShellAsync();
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                sender.NavigationCompleted -= OnNavigationCompleted;
                completed.TrySetResult(args.IsSuccess);
            }
            webview.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webview.NavigateToString(shell);
            if (!await completed.Task)
            {
                throw new InvalidOperationException("Failed to load release notes shell page.");
            }
            await webview.ExecuteScriptAsync(ReleaseNotesHtml.BuildInitScript(Groups.Count));
        }
        catch
        {
            _shellTask = null;
            throw;
        }
    }



    [RelayCommand]
    private async Task RetryAsync()
    {
        if (Groups.Count > 0 && NavView.SelectedItem is NavigationViewItem { Tag: ReleaseNotesGroup group })
        {
            await ShowGroupAsync(group);
        }
        else
        {
            await LoadAsync();
        }
    }



    private void ShowLoading()
    {
        webview.Visibility = Visibility.Collapsed;
        StackPanel_Error.Visibility = Visibility.Collapsed;
        ProgressRing_Loading.Visibility = Visibility.Visible;
    }



    /// <summary>
    /// 显示正文。只读 markdown，不给 WebView2 焦点，避免把微信输入法推进 Chromium TSF（issue #1）。
    /// </summary>
    private void ShowContent()
    {
        webview.Visibility = Visibility.Visible;
        ProgressRing_Loading.Visibility = Visibility.Collapsed;
        StackPanel_Error.Visibility = Visibility.Collapsed;
    }



    /// <summary>
    /// 显示出错面板。
    /// </summary>
    /// <param name="message">出错提示。</param>
    private void ShowError(string message)
    {
        TextBlock_Error.Text = message;
        webview.Visibility = Visibility.Collapsed;
        ProgressRing_Loading.Visibility = Visibility.Collapsed;
        StackPanel_Error.Visibility = Visibility.Visible;
    }



    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        try
        {
            _ = Launcher.LaunchUriAsync(new Uri(args.Uri));
            args.Handled = true;
        }
        catch { }
    }



}
