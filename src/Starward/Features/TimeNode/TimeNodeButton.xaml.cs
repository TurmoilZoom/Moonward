using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Setting;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.TimeNode;

/// <summary>
/// 游戏启动页右侧「时间节点」入口：图标按钮 + Flyout 卡池/活动倒计时。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class TimeNodeButton : UserControl
{

    private readonly ILogger<TimeNodeButton> _logger = AppConfig.GetLogger<TimeNodeButton>();

    private readonly TimeNodeService _timeNodeService = AppConfig.GetService<TimeNodeService>();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _countdownTimer;

    private CancellationTokenSource? _loadCts;


    public TimeNodeButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }


    /// <summary>
    /// 语言切换后刷新 x:Bind 文案，并重算倒计时格式串。
    /// </summary>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
        RefreshAllCountdowns();
    }


    /// <summary>
    /// 当前启动页所选游戏。
    /// </summary>
    public GameId? CurrentGameId
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                if (IsLoaded)
                {
                    InitializeForCurrentGame();
                }
            }
        }
    }


    public ObservableCollection<TimeNodeSectionView> Sections { get; } = new();


    public bool IsEmpty { get; set => SetProperty(ref field, value); } = true;


    public bool IsLoading { get; set => SetProperty(ref field, value); }


    public string? ErrorMessage { get; set => SetProperty(ref field, value); }


    private void Button_TimeNode_Loaded(object sender, RoutedEventArgs e)
    {
        // 静止显示首帧，避免 AutoPlay 常驻动画
        Lottie_TimeNode.SetProgress(0);
        InitializeForCurrentGame();
    }


    private void Button_TimeNode_Unloaded(object sender, RoutedEventArgs e)
    {
        Lottie_TimeNode.Stop();
        StopCountdownTimer();
        CancelLoad();
        Sections.Clear();
        ErrorMessage = null;
        IsEmpty = true;
    }


    /// <summary>
    /// 悬浮时播放一次 Lottie 后停在末帧；离开后回到首帧。
    /// </summary>
    private void Button_TimeNode_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = Lottie_TimeNode.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 指针离开后停止动画并回到首帧。
    /// </summary>
    private void Button_TimeNode_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Lottie_TimeNode.Stop();
        Lottie_TimeNode.SetProgress(0);
    }


    private void InitializeForCurrentGame()
    {
        if (CurrentGameId is null || !GameFeatureConfig.FromGameId(CurrentGameId).SupportTimeNode)
        {
            this.Visibility = Visibility.Collapsed;
            StopCountdownTimer();
            return;
        }
        this.Visibility = Visibility.Visible;
    }


    private async void Flyout_TimeNode_Opened(object sender, object e)
    {
        await LoadSnapshotAsync(forceRefresh: false);
        StartCountdownTimer();
    }


    private void Flyout_TimeNode_Closed(object sender, object e)
    {
        StopCountdownTimer();
        CancelLoad();
    }


    private async Task LoadSnapshotAsync(bool forceRefresh)
    {
        if (CurrentGameId is null)
        {
            return;
        }

        CancelLoad();
        _loadCts = new CancellationTokenSource();
        CancellationToken ct = _loadCts.Token;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            TimeNodeSnapshot snapshot = await _timeNodeService.GetSnapshotAsync(CurrentGameId.GameBiz, forceRefresh, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // x:Bind 集合须在 UI 线程更新
            Sections.Clear();
            foreach (TimeNodeSection section in snapshot.Sections)
            {
                var views = section.Items.Select(TimeNodeItemView.FromModel).ToList();
                Sections.Add(new TimeNodeSectionView
                {
                    Title = section.Title,
                    Items = views,
                });
            }
            IsEmpty = Sections.Count == 0 || Sections.All(s => s.Items.Count == 0);
            RefreshAllCountdowns();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load time node failed (biz {Biz})", CurrentGameId.GameBiz.Value);
            ErrorMessage = ex is miHoYoApiException or System.Net.Http.HttpRequestException
                ? MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.LauncherPublicApi).Message
                : Lang.TimeNode_LoadFailed;
            if (Sections.Count == 0)
            {
                IsEmpty = true;
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }


    private void StartCountdownTimer()
    {
        StopCountdownTimer();
        DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
        _countdownTimer = queue.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.IsRepeating = true;
        _countdownTimer.Tick += (_, _) => RefreshAllCountdowns();
        _countdownTimer.Start();
    }


    private void StopCountdownTimer()
    {
        if (_countdownTimer is not null)
        {
            _countdownTimer.Stop();
            _countdownTimer = null;
        }
    }


    private void RefreshAllCountdowns()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (TimeNodeSectionView section in Sections)
        {
            foreach (TimeNodeItemView item in section.Items)
            {
                item.RefreshCountdown(now);
            }
        }
    }


    private void CancelLoad()
    {
        try
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
        }
        catch
        {
            // ignore
        }
        _loadCts = null;
    }


    /// <summary>
    /// 可点击条目：默认普通边框，仅设置手型光标；强调描边在悬停时再显示。
    /// </summary>
    private void Item_Border_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not TimeNodeItemView { HasLink: true })
        {
            return;
        }
        PointerCursor.SetCursorShape(border, InputSystemCursorShape.Hand);
    }


    /// <summary>
    /// 可点击条目悬停：强调色描边 + 略提亮背景。
    /// </summary>
    private void Item_Border_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not TimeNodeItemView { HasLink: true })
        {
            return;
        }
        // 只改 Brush，不改 BorderThickness，避免布局抖动
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out object? accentObj) && accentObj is Brush accent)
        {
            border.BorderBrush = accent;
        }
        if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorSecondaryBrush", out object? bgObj) && bgObj is Brush bg)
        {
            border.Background = bg;
        }
    }


    /// <summary>
    /// 离开可点击条目：恢复默认描边与背景（厚度不变）。
    /// </summary>
    private void Item_Border_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }
        if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out object? strokeObj) && strokeObj is Brush stroke)
        {
            border.BorderBrush = stroke;
        }
        if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out object? bgObj) && bgObj is Brush bg)
        {
            border.Background = bg;
        }
    }


    private async void Item_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TimeNodeItemView item } && item.HasLink && Uri.TryCreate(item.LinkUrl, UriKind.Absolute, out Uri? uri))
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Open time node link failed: {Url}", item.LinkUrl);
            }
        }
    }

}
