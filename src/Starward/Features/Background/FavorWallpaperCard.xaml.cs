using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Starward.Controls;
using Starward.Features.Codec;
using System;
using System.ComponentModel;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Starward.Features.Background;

/// <summary>
/// 好感 / 满影画卡片：封面框按密友同行 245×153、下载 / 删除 / 点击设为背景。
/// 已下载的动态壁纸可悬停静音循环预览；满影画下载后封面即换成静态图。
/// </summary>
public sealed partial class FavorWallpaperCard : UserControl
{

    /// <summary>卡片内容区相对格子的内边距。</summary>
    public const double ContentInset = 4;

    /// <summary>密友同行百科图标像素尺寸（绝大多数为 245×153）。</summary>
    public const double CoverImageWidth = 245;

    /// <summary>密友同行百科图标像素尺寸（绝大多数为 245×153）。</summary>
    public const double CoverImageHeight = 153;

    /// <summary>封面内容区高宽比，与密友同行图标一致。</summary>
    public const double CellAspect = CoverImageHeight / CoverImageWidth;

    /// <summary>悬停后启动预览的延迟，避免扫过画廊时误触发。</summary>
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(280);

    /// <summary>同一画廊同时只播一张，避免多路解码占满 CPU。</summary>
    private static FavorWallpaperCard? s_activePreview;


    private readonly DispatcherQueueTimer _hoverTimer;

    private MediaPlayer? _player;
    private MediaSource? _mediaSource;
    private bool _pointerInside;
    private bool _previewing;
    private bool _coverHidden;


    public FavorWallpaperCard()
    {
        this.InitializeComponent();
        this.Loaded += FavorWallpaperCard_Loaded;
        this.Unloaded += FavorWallpaperCard_Unloaded;
        AddHandler(TappedEvent, new TappedEventHandler(OnTapped), handledEventsToo: true);

        _hoverTimer = DispatcherQueue.CreateTimer();
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Interval = HoverDelay;
        _hoverTimer.Tick += HoverTimer_Tick;
    }


    public FavorWallpaperView? View
    {
        get => (FavorWallpaperView?)GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }


    public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(
        nameof(View),
        typeof(FavorWallpaperView),
        typeof(FavorWallpaperCard),
        new PropertyMetadata(null, OnViewPropertyChanged));


    private static void OnViewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FavorWallpaperCard)d).HandleViewChanged(e.OldValue as FavorWallpaperView, e.NewValue as FavorWallpaperView);
    }


    private void HandleViewChanged(FavorWallpaperView? oldView, FavorWallpaperView? newView)
    {
        if (oldView is not null)
        {
            oldView.PropertyChanged -= View_PropertyChanged;
        }
        StopPreview();
        if (newView is not null)
        {
            newView.PropertyChanged += View_PropertyChanged;
        }
    }


    private void ActionButton_Loaded(object sender, RoutedEventArgs e)
    {
        ResetLottie(Lottie_Download);
        ResetLottie(Lottie_Delete);
    }


    private void ActionButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimatedVisualPlayer? player = CurrentLottie();
        if (player is null)
        {
            return;
        }
        _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    private void ActionButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ResetLottie(Lottie_Download);
        ResetLottie(Lottie_Delete);
    }


    private AnimatedVisualPlayer? CurrentLottie()
    {
        return View?.IsDownloaded == true ? Lottie_Delete : Lottie_Download;
    }


    private static void ResetLottie(AnimatedVisualPlayer player)
    {
        player.Stop();
        player.SetProgress(0);
    }


    private async void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (View is null || View.IsDownloading)
        {
            return;
        }
        if (e.OriginalSource is DependencyObject source && IsInsideActionButton(source))
        {
            return;
        }
        StopPreview();
        if (View.UseAction is not null)
        {
            await View.UseAction(View);
        }
    }


    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (View is null)
        {
            return;
        }
        // 删除前先释放预览占用的文件句柄，否则本地缓存删不掉。
        StopPreview();
        if (View.IsDownloaded)
        {
            if (View.DeleteAction is not null)
            {
                await View.DeleteAction(View);
            }
        }
        else if (View.DownloadAction is not null)
        {
            await View.DownloadAction(View);
        }
    }


    private bool IsInsideActionButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, Button_Action))
            {
                return true;
            }
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }


    private void FavorWallpaperCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (View is not null)
        {
            View.PropertyChanged -= View_PropertyChanged;
            View.PropertyChanged += View_PropertyChanged;
        }
    }


    private void FavorWallpaperCard_Unloaded(object sender, RoutedEventArgs e)
    {
        if (View is not null)
        {
            View.PropertyChanged -= View_PropertyChanged;
        }
        StopPreview();
    }


    private void View_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(FavorWallpaperView.IsDownloaded) or nameof(FavorWallpaperView.IsDownloading)))
        {
            return;
        }
        if (!CanPreview())
        {
            StopPreview();
            return;
        }
        if (_pointerInside && !_previewing)
        {
            SchedulePreview();
        }
    }


    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        if (CanPreview() && !IsOverActionButton(e))
        {
            SchedulePreview();
        }
    }


    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerInside || _previewing || !CanPreview() || IsOverActionButton(e))
        {
            return;
        }
        if (!_hoverTimer.IsRunning)
        {
            SchedulePreview();
        }
    }


    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        StopPreview();
    }


    private void Root_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        StopPreview();
    }


    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerInside)
        {
            StopPreview();
        }
    }


    /// <summary>
    /// 已下载的动态壁纸才悬停预览；满影画是静态图，封面即预览。
    /// </summary>
    private bool CanPreview()
    {
        return View is { IsDownloaded: true, IsDownloading: false, IsStatic: false } && TryGetLocalVideoPath() is not null;
    }


    private string? TryGetLocalVideoPath()
    {
        if (View is null || View.IsStatic)
        {
            return null;
        }
        string path = BackgroundService.GetBgFilePath(FavorWallpaperService.GetCacheFileName(View.Record));
        return File.Exists(path) ? path : null;
    }


    private bool IsOverActionButton(PointerRoutedEventArgs e)
    {
        return e.OriginalSource is DependencyObject source && IsInsideActionButton(source);
    }


    private void SchedulePreview()
    {
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }


    private void HoverTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_pointerInside && CanPreview() && !_previewing)
        {
            StartPreview();
        }
    }


    /// <summary>
    /// 打开本地视频并从头静音循环。
    /// </summary>
    private void StartPreview()
    {
        string? path = TryGetLocalVideoPath();
        if (path is null)
        {
            return;
        }

        if (s_activePreview is { } other && !ReferenceEquals(other, this))
        {
            other.StopPreview();
        }
        s_activePreview = this;

        StopPlaybackOnly();
        _previewing = true;
        EnsureDecoders(path);

        try
        {
            var player = new MediaPlayer
            {
                IsLoopingEnabled = true,
                IsMuted = true,
                Volume = 0,
                AutoPlay = false,
            };
            player.CommandManager.IsEnabled = false;
            player.SystemMediaTransportControls.IsEnabled = false;
            player.MediaOpened += Player_MediaOpened;
            player.MediaFailed += Player_MediaFailed;
            player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;

            _mediaSource = MediaSource.CreateFromUri(new Uri(path));
            player.Source = _mediaSource;
            Player_Preview.SetMediaPlayer(player);
            Player_Preview.Visibility = Visibility.Visible;
            _player = player;
        }
        catch
        {
            StopPreview();
        }
    }


    private void Player_MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_previewing || !_pointerInside || !ReferenceEquals(sender, _player))
            {
                return;
            }
            sender.Play();
        });
    }


    private void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(StopPreview);
    }


    private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
    {
        if (_coverHidden)
        {
            return;
        }
        DispatcherQueue.TryEnqueue(RevealVideoIfNeeded);
    }


    /// <summary>
    /// 等首帧真正出来再藏封面，避免 MediaOpened 后闪黑。
    /// </summary>
    private void RevealVideoIfNeeded()
    {
        if (_coverHidden || !_previewing)
        {
            return;
        }
        _coverHidden = true;
        FadePreviewVisuals(showVideo: true);
    }


    /// <summary>
    /// 离开卡片、换绑或卸载时释放播放器，封面回到静止图。
    /// </summary>
    private void StopPreview()
    {
        _hoverTimer.Stop();
        _previewing = false;
        _coverHidden = false;
        FadePreviewVisuals(showVideo: false);
        StopPlaybackOnly();
        Player_Preview.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(s_activePreview, this))
        {
            s_activePreview = null;
        }
    }


    /// <summary>
    /// 只拆播放器与源，不改悬停状态（用于同卡重新打开预览）。
    /// </summary>
    private void StopPlaybackOnly()
    {
        MediaPlayer? player = _player;
        MediaSource? source = _mediaSource;
        _player = null;
        _mediaSource = null;
        if (player is null)
        {
            Player_Preview.SetMediaPlayer(null);
            return;
        }
        player.MediaOpened -= Player_MediaOpened;
        player.MediaFailed -= Player_MediaFailed;
        player.PlaybackSession.PositionChanged -= PlaybackSession_PositionChanged;
        try
        {
            player.Pause();
        }
        catch
        {
        }
        player.Source = null;
        Player_Preview.SetMediaPlayer(null);
        source?.Dispose();
        player.Dispose();
    }


    private void FadePreviewVisuals(bool showVideo)
    {
        float cover = showVideo ? 0f : 1f;
        float video = showVideo ? 1f : 0f;
        AnimateOpacity(Image_Cover, cover, showVideo ? 180 : 0);
        AnimateOpacity(Player_Preview, video, showVideo ? 180 : 0);
    }


    private static void AnimateOpacity(UIElement element, float opacity, int milliseconds)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (milliseconds <= 0 || !EntranceAnimation.AnimationsEnabled())
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Opacity = opacity;
            return;
        }
        ScalarKeyFrameAnimation anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, opacity);
        anim.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation(nameof(Visual.Opacity), anim);
    }


    /// <summary>
    /// 预览与背景播放共用同一套本地解码器注册，webm 才能在未装商店扩展时解开。
    /// </summary>
    private static void EnsureDecoders(string file)
    {
        if (Path.GetExtension(file).Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            bool decoderInstalled = VP9Helper.IsVP9DecoderInstalled();
            bool vp8 = VP9Helper.IsVP8VideoFile(file);
            if (!vp8 && (!decoderInstalled || VP9Helper.IsVP9HighProfileOrRGB(file)))
            {
                VP9Helper.RegisterVP9Decoder();
            }
        }
        VP9Helper.RegisterVorbisDecoder();
    }

}
