using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
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
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.RedeemCode;

/// <summary>
/// 游戏启动页右侧「兑换码」入口：图标按钮 + Flyout 展示前瞻直播码（可复制）。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class RedeemCodeButton : UserControl
{

    private readonly ILogger<RedeemCodeButton> _logger = AppConfig.GetLogger<RedeemCodeButton>();

    private readonly RedeemCodeService _redeemCodeService = AppConfig.GetService<RedeemCodeService>();

    private CancellationTokenSource? _loadCts;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _refreshTimer;


    public RedeemCodeButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }


    /// <summary>
    /// 语言切换后刷新 x:Bind 文案。
    /// </summary>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
        UpdateEmptyMessage();
        if (string.IsNullOrEmpty(ActivityTitle))
        {
            DisplayTitle = Lang.RedeemCodeButton_Title;
        }
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


    public ObservableCollection<RedeemCodeItemView> Codes { get; } = new();


    public bool HasCodes { get; set => SetProperty(ref field, value); }


    public bool IsLoading { get; set => SetProperty(ref field, value); }


    /// <summary>Flyout 标题（活动名或默认「兑换码」）。</summary>
    public string DisplayTitle { get; set => SetProperty(ref field, value); } = Lang.RedeemCodeButton_Title;


    /// <summary>空态 / 未开始文案。</summary>
    public string EmptyMessage { get; set => SetProperty(ref field, value); } = Lang.RedeemCode_Empty;


    public bool ShowEmptyMessage { get; set => SetProperty(ref field, value); }


    private string? ActivityTitle { get; set; }

    private bool _notStarted;


    private void Button_RedeemCode_Loaded(object sender, RoutedEventArgs e)
    {
        // 静止显示首帧，避免 AutoPlay 常驻动画
        Lottie_RedeemCode.SetProgress(0);
        InitializeForCurrentGame();
    }


    private void Button_RedeemCode_Unloaded(object sender, RoutedEventArgs e)
    {
        Lottie_RedeemCode.Stop();
        StopRefreshTimer();
        CancelLoad();
        Codes.Clear();
        HasCodes = false;
        ShowEmptyMessage = false;
    }


    /// <summary>
    /// 悬浮时播放一次 Lottie 后停在末帧；离开后回到首帧。
    /// </summary>
    private void Button_RedeemCode_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = Lottie_RedeemCode.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 指针离开后停止动画并回到首帧。
    /// </summary>
    private void Button_RedeemCode_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Lottie_RedeemCode.Stop();
        Lottie_RedeemCode.SetProgress(0);
    }


    private void InitializeForCurrentGame()
    {
        if (CurrentGameId is null || !GameFeatureConfig.FromGameId(CurrentGameId).SupportRedeemCode)
        {
            this.Visibility = Visibility.Collapsed;
            StopRefreshTimer();
            return;
        }
        this.Visibility = Visibility.Visible;
    }


    private async void Flyout_RedeemCode_Opened(object sender, object e)
    {
        await LoadSnapshotAsync(forceRefresh: false);
        StartRefreshTimer();
    }


    private void Flyout_RedeemCode_Closed(object sender, object e)
    {
        StopRefreshTimer();
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
        try
        {
            RedeemCodeSnapshot snapshot = await _redeemCodeService.GetSnapshotAsync(CurrentGameId.GameBiz, forceRefresh, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            // 本功能仅展示：API / 网络失败不显示红字，只记日志并回落空态
            _logger.LogWarning(ex, "Load redeem code failed (biz {Biz})", CurrentGameId.GameBiz.Value);
            if (Codes.Count == 0)
            {
                HasCodes = false;
                _notStarted = false;
                ActivityTitle = null;
                DisplayTitle = Lang.RedeemCodeButton_Title;
                ShowEmptyMessage = true;
                EmptyMessage = Lang.RedeemCode_Empty;
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


    private void ApplySnapshot(RedeemCodeSnapshot snapshot)
    {
        ActivityTitle = snapshot.Title;
        DisplayTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? Lang.RedeemCodeButton_Title : snapshot.Title!;
        _notStarted = snapshot.NotStarted;

        Codes.Clear();
        foreach (RedeemCodeItem item in snapshot.Codes)
        {
            Codes.Add(RedeemCodeItemView.FromModel(item));
        }

        HasCodes = Codes.Count > 0;
        UpdateEmptyMessage();
    }


    private void UpdateEmptyMessage()
    {
        if (HasCodes)
        {
            ShowEmptyMessage = false;
            return;
        }

        ShowEmptyMessage = true;
        EmptyMessage = _notStarted ? Lang.RedeemCode_NotStarted : Lang.RedeemCode_Empty;
    }


    private void StartRefreshTimer()
    {
        StopRefreshTimer();
        // 直播掉码：打开期间约每 60s 静默刷新
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _refreshTimer = queue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(60);
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick += async (_, _) =>
        {
            if (!IsLoading)
            {
                await LoadSnapshotAsync(forceRefresh: true);
            }
        };
        _refreshTimer.Start();
    }


    private void StopRefreshTimer()
    {
        if (_refreshTimer is not null)
        {
            _refreshTimer.Stop();
            _refreshTimer = null;
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


    private void Item_Border_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border)
        {
            PointerCursor.SetCursorShape(border, InputSystemCursorShape.Hand);
        }
    }


    private void Item_Border_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out object? accentObj) && accentObj is Brush accent)
        {
            border.BorderBrush = accent;
        }
        if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorSecondaryBrush", out object? bgObj) && bgObj is Brush bg)
        {
            border.Background = bg;
        }
    }


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
        if (sender is not FrameworkElement { Tag: RedeemCodeItemView item } || string.IsNullOrWhiteSpace(item.Code))
        {
            return;
        }

        ClipboardHelper.SetText(item.Code);
        // 右侧图标：复制 → 对勾约 1 秒（对齐抽卡页复制 URL 反馈）
        item.IsCopied = true;
        try
        {
            await Task.Delay(1000);
        }
        catch
        {
            // ignore
        }
        item.IsCopied = false;
    }

}
