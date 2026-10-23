using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NuGet.Versioning;
using Starward.Controls;
using Starward.Features.Setting;
using Starward.Frameworks;
using Starward.Helpers;
using Starward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Graphics;


namespace Starward.Features.Update;

/// <summary>
/// 发行说明窗口（参考 PowerToys 的 Release notes）：左侧按「主版本.次版本」分组列出最近的版本，右侧显示该组全部版本的说明。
/// 随时可从关于页打开，与「有新版本」「静默更新后」才出现的 <see cref="UpdateWindow"/> 互不影响。
/// <para>
/// 版本列表由 <see cref="ReleaseNotesService"/> 落盘缓存，正文一个版本一段、在本地渲染（<see cref="ReleaseNotesView"/>），打开一次至多请求 GitHub 一次。
/// </para>
/// </summary>
[INotifyPropertyChanged]
public sealed partial class ReleaseNotesWindow : WindowEx
{


    private readonly ILogger<ReleaseNotesWindow> _logger = AppConfig.GetLogger<ReleaseNotesWindow>();

    private readonly ReleaseNotesService _releaseNotesService = AppConfig.GetService<ReleaseNotesService>();


    /// <summary>左侧导航的流体悬停高亮条，与设置页同一个效果类。</summary>
    private readonly FluidNavigationViewHoverEffect _navHoverEffect = new();

    /// <summary>左侧导航与 FlipView 互相同步选中项期间置位，免得来回触发。</summary>
    private bool _syncingSelection;

    /// <summary>已隐藏 FlipView 的翻页按钮并吞掉其滚轮（<see cref="HideFlipViewChrome"/>）。</summary>
    private bool _flipViewChromeHidden;

    private bool _closed;

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
        _closed = true;
    }




    /// <summary>
    /// 左侧的版本分组。
    /// </summary>
    public List<ReleaseNotesGroup> Groups { get; set => SetProperty(ref field, value); } = new();



    /// <summary>
    /// 取发行版本并分组、拼好各组正文，然后选中最新的一组。
    /// </summary>
    private async Task LoadAsync()
    {
        try
        {
            ShowLoading();
            List<GithubRelease> releases = await _releaseNotesService.GetReleasesAsync();
            if (_closed) return;
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
            BuildNavItems(groups);
            FlipView_Groups.ItemsSource = groups;
            ShowContent();
            // 先布局一次把菜单项建出来再选中：容器还没生成时设 SelectedItem，菜单项没有选中态和指示条。
            // 这次布局也让刚显示的 FlipView 建好模板
            NavView.UpdateLayout();
            HideFlipViewChrome();
            NavView.SelectedItem = NavView.MenuItems[0];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load release notes");
            if (!_closed)
            {
                ShowError(ReleaseNotesService.GetDisplayErrorMessage(ex));
            }
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
        if (_syncingSelection || args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }
        int target = NavView.MenuItems.IndexOf(item);
        if (target < 0 || target >= FlipView_Groups.Items.Count)
        {
            return;
        }
        _syncingSelection = true;
        try
        {
            // 逐页改 SelectedIndex（同上游游戏设置弹窗）：跨几组时一路经过中间的页
            while (FlipView_Groups.SelectedIndex < target)
            {
                FlipView_Groups.SelectedIndex++;
            }
            while (FlipView_Groups.SelectedIndex > target)
            {
                FlipView_Groups.SelectedIndex--;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }



    /// <summary>
    /// 用键盘在正文里翻页时，左侧选中项跟着走。
    /// </summary>
    private void FlipView_Groups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = FlipView_Groups.SelectedIndex;
        if (_syncingSelection || index < 0 || index >= NavView.MenuItems.Count)
        {
            return;
        }
        _syncingSelection = true;
        try
        {
            NavView.SelectedItem = NavView.MenuItems[index];
        }
        finally
        {
            _syncingSelection = false;
        }
    }



    /// <summary>
    /// 隐藏 FlipView 自带的翻页按钮、吞掉外层滚轮，只通过左侧导航与键盘翻页（同上游游戏设置弹窗）。
    /// 要等 FlipView 显示并布局过、模板建好之后调用：它初始是折叠的，Loaded 时还没有模板子元素。只处理一次。
    /// </summary>
    private void HideFlipViewChrome()
    {
        if (_flipViewChromeHidden)
        {
            return;
        }
        try
        {
            FlipView_Groups.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(FlipView_Groups) == 0)
            {
                return;
            }
            _flipViewChromeHidden = true;
            DependencyObject root = VisualTreeHelper.GetChild(FlipView_Groups, 0);
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is ButtonBase button)
                {
                    button.IsHitTestVisible = false;
                    button.Opacity = 0;
                }
                else if (child is ScrollViewer scrollViewer)
                {
                    scrollViewer.PointerWheelChanged += (_, args) => args.Handled = true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hide release notes flip view buttons");
        }
    }



    [RelayCommand]
    private async Task RetryAsync()
    {
        await LoadAsync();
    }



    private void ShowLoading()
    {
        FlipView_Groups.Visibility = Visibility.Collapsed;
        StackPanel_Error.Visibility = Visibility.Collapsed;
        ProgressRing_Loading.Visibility = Visibility.Visible;
    }



    private void ShowContent()
    {
        FlipView_Groups.Visibility = Visibility.Visible;
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
        FlipView_Groups.Visibility = Visibility.Collapsed;
        ProgressRing_Loading.Visibility = Visibility.Collapsed;
        StackPanel_Error.Visibility = Visibility.Visible;
    }



}
