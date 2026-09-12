using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core.GameRecord;
using System;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 挂在各数据页右侧面板顶部那排小图标按钮最左边的「自动更新」配置按钮：一个按钮 + 一个配置浮层。
/// <para>
/// 配置粒度是「当前账号 + 本页数据板块 + 月份」——同一个号的深渊和月报可以各配各的频率，
/// 月报类板块还会拆成「当月」「上月」两个任务，各开各的。页面只需给 <see cref="GameRole"/> 赋值。
/// </para>
/// </summary>
[INotifyPropertyChanged]
public sealed partial class RecordRefreshConfigButton : UserControl
{

    private readonly ILogger<RecordRefreshConfigButton> _logger = AppConfig.GetLogger<RecordRefreshConfigButton>();

    private readonly AutoRecordRefreshService _service = AppConfig.GetService<AutoRecordRefreshService>();


    /// <summary>
    /// 后台自动更新成功写入本地库后触发，供所在数据页从数据库重载列表。
    /// 月报类的当月、上月任一跑完都会触发一次。
    /// </summary>
    public event EventHandler? LocalDataUpdated;


    public RecordRefreshConfigButton()
    {
        InitializeComponent();
        Loaded += RecordRefreshConfigButton_Loaded;
        Unloaded += RecordRefreshConfigButton_Unloaded;
    }


    private void RecordRefreshConfigButton_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Register<RecordRefreshCompletedMessage>(this, (_, message) => OnBackgroundCompleted(message));
    }


    private void RecordRefreshConfigButton_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<RecordRefreshCompletedMessage>(this);
    }


    /// <summary>
    /// 后台更新当前账号的本板块结束后：刷新红点；成功则通知页面重载本地数据。
    /// 红点与页面重载都不分月份——两个任务写的是同一张表。
    /// </summary>
    private void OnBackgroundCompleted(RecordRefreshCompletedMessage message)
    {
        if (GameRole is null || message.Uid != GameRole.Uid || message.GameBiz != GameRole.GameBiz || message.Item != Item)
        {
            return;
        }
        UpdateErrorState();
        if (message.Succeeded)
        {
            LocalDataUpdated?.Invoke(this, EventArgs.Empty);
        }
    }


    /// <summary>
    /// 当前页在前台时，后台自动更新成功后从本地库重载。页面卸载时自动取消。
    /// </summary>
    /// <param name="host">所在页面，用它的 Unloaded 解绑。</param>
    /// <param name="reloadFromLocal">只读本地库，不要再打网络。</param>
    public void WatchPage(FrameworkElement host, Action reloadFromLocal)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(reloadFromLocal);
        void OnUpdated(object? sender, EventArgs e) => reloadFromLocal();
        LocalDataUpdated += OnUpdated;
        RoutedEventHandler? onUnloaded = null;
        onUnloaded = (_, _) =>
        {
            host.Unloaded -= onUnloaded;
            LocalDataUpdated -= OnUpdated;
        };
        host.Unloaded += onUnloaded;
    }


    /// <summary>
    /// 本页对应的数据板块，在 XAML 上直接写死，如 <c>Item="ShiyuDefense"</c>。
    /// </summary>
    public RecordRefreshItem Item
    {
        get => field;
        set
        {
            field = value;
            JobPanel_Current.Item = value;
            JobPanel_Previous.Item = value;
            OnPropertyChanged(nameof(ItemName));
            OnPropertyChanged(nameof(IsMonthlyReportItem));
        }
    }


    /// <summary>
    /// 当前账号。页面在 <c>OnNavigatedTo</c> 里赋值；换号后配置会重新载入。
    /// </summary>
    public GameRecordRole? GameRole
    {
        get => field;
        set
        {
            field = value;
            JobPanel_Current.GameRole = value;
            JobPanel_Previous.GameRole = value;
            OnPropertyChanged(nameof(RoleName));
            UpdateErrorState();
        }
    }


    /// <summary>数据板块显示名，复用工具箱左侧导航的文案。</summary>
    public string ItemName => Item.GetDisplayName();

    /// <summary>配置归属的账号，展示成「昵称 · uid」。</summary>
    public string RoleName => GameRole is null ? string.Empty : $"{GameRole.Nickname} · {GameRole.Uid}";

    /// <summary>月报类板块才拆出「上月」那个任务，其余板块浮层里只有一块设置。</summary>
    public bool IsMonthlyReportItem => Item.IsMonthlyReport();

    /// <summary>这个账号的这个板块留下过异常记录：按钮右上角点个红点。</summary>
    public bool HasError { get; private set => SetProperty(ref field, value); }


    /// <summary>
    /// 重新判断这个板块当前有没有异常记录（当月、上月任一有都算）。
    /// </summary>
    private void UpdateErrorState()
    {
        if (GameRole is null)
        {
            HasError = false;
            return;
        }
        try
        {
            HasError = _service.GetErrors().Exists(x => x.Uid == GameRole.Uid && x.GameBiz == GameRole.GameBiz && x.Item == Item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Check record refresh error state ({biz}, {uid}, {item}).", GameRole.GameBiz, GameRole.Uid, Item);
            HasError = false;
        }
    }


    private void Flyout_Opening(object? sender, object e)
    {
        // 后台可能刚写过 lastRun / 清过异常；不重载的话会显示旧值，改频率还会把成功记录盖掉
        JobPanel_Current.Reload();
        JobPanel_Previous.Reload();
        UpdateErrorState();
    }


    private async void Button_ViewErrors_Click(object sender, RoutedEventArgs e)
    {
        Button_Root.Flyout?.Hide();
        await new RecordRefreshErrorDialog { XamlRoot = this.XamlRoot }.ShowAsync();
        // 对话框里可能清空了记录，回来立刻把红点同步掉
        UpdateErrorState();
    }

}
