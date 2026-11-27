using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.Codec;
using Starward.Features.Setting;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;


namespace Starward.Features.GameSelector;

/// <summary>
/// 预约中游戏的详情页，布局参照官方启动器：左侧大图/视频轮播与缩略图，右侧 Logo、简介、标签、预约按钮（官方最右侧的社交媒体图标列不显示）。
/// 由 <see cref="GameSelector"/> 叠在游戏选择面板上显示，返回后回到游戏卡片。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class GameReservationView : UserControl
{

    private readonly ILogger<GameReservationView> _logger = AppConfig.GetLogger<GameReservationView>();


    /// <summary>
    /// 播放预约视频的播放器，首次播放视频时创建，控件卸载时释放（每新建一个都会多出一个 SMTC 窗口，所以复用）
    /// </summary>
    private MediaPlayer? _mediaPlayer;


    /// <summary>
    /// 打开详情页时把背景视频静音了，关闭时要恢复
    /// </summary>
    private bool _backgroundVideoMuted;


    /// <summary>
    /// 当前视频已经开始播放过（之后的 Paused 才是用户暂停）
    /// </summary>
    private bool _videoStarted;


    /// <summary>
    /// 每次打开或关闭递增，丢弃过期的异步回调
    /// </summary>
    private int _showVersion;


    private string? _homePageUrl;



    public GameReservationView()
    {
        InitializeComponent();
        Unloaded += GameReservationView_Unloaded;
        // Slider 自己处理了指针事件，要用 handledEventsToo 才收得到按下与松开
        Slider_VideoPosition.AddHandler(PointerPressedEvent, new PointerEventHandler(Slider_VideoPosition_PointerPressed), true);
        Slider_VideoPosition.AddHandler(PointerReleasedEvent, new PointerEventHandler(Slider_VideoPosition_PointerReleased), true);
        Slider_VideoPosition.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Slider_VideoPosition_PointerReleased), true);
        Slider_VideoVolume.AddHandler(PointerReleasedEvent, new PointerEventHandler(Slider_VideoVolume_PointerReleased), true);
        Slider_VideoVolume.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Slider_VideoVolume_PointerReleased), true);
        // 文案都是一次性绑定，详情页加载后一直留在游戏选择面板里，切换语言要手动刷新
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }



    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        try
        {
            Bindings.Update();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update reservation view bindings after language changed");
        }
    }



    /// <summary>
    /// 点击返回
    /// </summary>
    public event EventHandler? BackRequested;


    public ObservableCollection<GameReservationMediaItem> MediaItems { get; } = new();


    public GameReservationMediaItem? SelectedItem { get; private set => SetProperty(ref field, value); }


    public string? LogoUrl { get; private set => SetProperty(ref field, value); }


    public string? Introduction { get; private set => SetProperty(ref field, value); }


    public List<GameReservationTag>? Tags { get; private set => SetProperty(ref field, value); }





    /// <summary>
    /// 显示指定游戏的详情页：先用游戏基本信息里的壁纸、Logo 和简介占位，详情内容取到后再替换
    /// </summary>
    /// <param name="gameInfo">处于预约状态的游戏</param>
    /// <param name="loadContent">获取详情页内容；失败时返回 null，保留占位内容</param>
    public async Task ShowAsync(GameInfo gameInfo, Func<Task<GameReservationContent?>> loadContent)
    {
        int version = ++_showVersion;
        _homePageUrl = gameInfo.Reservation?.Link;
        Button_PreRegister.Visibility = string.IsNullOrWhiteSpace(_homePageUrl) ? Visibility.Collapsed : Visibility.Visible;
        LogoUrl = gameInfo.Display.TopLeftLogo?.Url is { Length: > 0 } topLeftLogo ? topLeftLogo : gameInfo.Display.Logo?.Url;
        Introduction = gameInfo.Display.Introduction?.Trim();
        Tags = null;
        List<GameReservationMedia> placeholder = [];
        if (gameInfo.Display.Background?.Url is { Length: > 0 } background)
        {
            placeholder.Add(new GameReservationMedia { Type = GameReservationMediaType.RESERVATION_MEDIA_TYPE_IMAGE, Cover = new GameImage { Url = background } });
        }
        SetMedias(placeholder);

        GameReservationContent? content = await loadContent();
        if (version != _showVersion || content is null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(content.Logo))
        {
            LogoUrl = content.Logo;
        }
        if (!string.IsNullOrWhiteSpace(content.Introduction))
        {
            Introduction = content.Introduction.Trim();
        }
        Tags = content.Tags?.OrderBy(x => x.Order).ToList();
        List<GameReservationMedia> medias = content.Medias?.Where(x => !string.IsNullOrWhiteSpace(x.Cover?.Url)).OrderBy(x => x.Order).ToList() ?? [];
        if (medias.Count > 0)
        {
            SetMedias(medias);
        }
    }



    /// <summary>
    /// 详情页被收起：停止视频，恢复背景视频音量
    /// </summary>
    public void Close()
    {
        _showVersion++;
        StopVideo();
        SaveVolume();
        RestoreBackgroundVideoVolume();
    }



    private void GameReservationView_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Close();
            _positionTimer?.Stop();
            if (_mediaPlayer is not null)
            {
                _mediaPlayer.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
                _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
                _mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
                MediaPlayerElement_Video.SetMediaPlayer(null);
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dispose reservation video player");
        }
    }



    /// <summary>
    /// 替换轮播内容并选中第一项
    /// </summary>
    /// <param name="medias">按顺序排好的图片与视频</param>
    private void SetMedias(List<GameReservationMedia> medias)
    {
        StopVideo();
        SelectedItem = null;
        MediaItems.Clear();
        foreach (GameReservationMedia media in medias)
        {
            MediaItems.Add(new GameReservationMediaItem(media));
        }
        ScrollViewer_Thumbnails.ChangeView(0, null, null, true);
        if (MediaItems.Count > 0)
        {
            // 清空后 FlipView 可能已自己选中第一项，此时不会再触发 SelectionChanged，手动同步一次
            if (FlipView_Media.SelectedIndex == 0)
            {
                OnSelectedMediaChanged();
            }
            else
            {
                FlipView_Media.SelectedIndex = 0;
            }
        }
    }



    private void FlipView_Media_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OnSelectedMediaChanged();
    }



    /// <summary>
    /// 选中项变化：同步缩略图选中态并滚到可见，切换视频播放
    /// </summary>
    private void OnSelectedMediaChanged()
    {
        GameReservationMediaItem? item = FlipView_Media.SelectedItem as GameReservationMediaItem;
        if (ReferenceEquals(item, SelectedItem))
        {
            return;
        }
        SelectedItem = item;
        foreach (GameReservationMediaItem media in MediaItems)
        {
            media.IsSelected = ReferenceEquals(media, item);
        }
        int index = FlipView_Media.SelectedIndex;
        if (index >= 0)
        {
            // 缩略图容器可能还没生成（刚换内容），推到本轮布局之后
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (ItemsControl_Thumbnails.ContainerFromIndex(index) is UIElement container)
                {
                    container.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, HorizontalAlignmentRatio = 0.5 });
                }
            });
        }
        if (item?.Media.IsVideo is true)
        {
            PlayVideo(item.Media.ResourceUrl);
        }
        else
        {
            HideVideo();
        }
    }



    /// <summary>
    /// 隐藏 FlipView 悬停时自带的翻页按钮，翻页改用下方缩略图和左右按钮
    /// </summary>
    private void FlipView_Media_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (Button button in FindDescendants<Button>(FlipView_Media))
            {
                button.Opacity = 0;
                button.IsHitTestVisible = false;
                button.IsTabStop = false;
            }
        }
        catch { }
    }



    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
            {
                yield return t;
            }
            foreach (T descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }



    private void Grid_Thumbnail_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GameReservationMediaItem item })
        {
            int index = MediaItems.IndexOf(item);
            if (index >= 0)
            {
                FlipView_Media.SelectedIndex = index;
            }
        }
    }



    private void Button_PreviousMedia_Click(object sender, RoutedEventArgs e)
    {
        int count = MediaItems.Count;
        if (count > 0)
        {
            FlipView_Media.SelectedIndex = (FlipView_Media.SelectedIndex - 1 + count) % count;
        }
    }



    private void Button_NextMedia_Click(object sender, RoutedEventArgs e)
    {
        int count = MediaItems.Count;
        if (count > 0)
        {
            FlipView_Media.SelectedIndex = (FlipView_Media.SelectedIndex + 1) % count;
        }
    }



    #region Video


    private const string GlyphPlay = "\uE768";

    private const string GlyphPause = "\uE769";

    private const string GlyphVolume = "\uE767";

    private const string GlyphMute = "\uE74F";


    /// <summary>
    /// 当前装在播放器里的视频地址；切到图片时只暂停不卸载，切回同一个视频接着播
    /// </summary>
    private string? _videoUrl;


    /// <summary>
    /// 从本地缓存播放时打开的文件流，换视频或收起时释放
    /// </summary>
    private IRandomAccessStream? _videoStream;


    /// <summary>
    /// 当前视频读的是本地缓存；缓存文件播放失败时删掉，下次重新下载
    /// </summary>
    private bool _videoFromCache;


    /// <summary>
    /// 每次开始加载或切走递增，丢弃过期的异步加载
    /// </summary>
    private int _videoVersion;


    /// <summary>
    /// 播放中刷新进度条
    /// </summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _positionTimer;


    private bool _pointerOverMedia;

    /// <summary>
    /// 代码在改进度条的值，ValueChanged 里不要当成用户拖动去跳转
    /// </summary>
    private bool _updatingPosition;

    /// <summary>
    /// 用户按住进度条拖动中，定时刷新不要把滑块拉回去
    /// </summary>
    private bool _seekingByUser;

    private bool _updatingVolume;

    /// <summary>
    /// 音量改过还没写进设置（拖动时不逐格写库，松手或收起时再写）
    /// </summary>
    private bool _volumeDirty;



    /// <summary>
    /// 播放选中的视频。同一个视频切回来时从离开的位置继续；本地已缓存就读缓存文件，
    /// 否则从网络串流、同时在后台下载到缓存，下次直接读本地。按流量计费的网络下不自动播放。
    /// </summary>
    /// <param name="url">视频地址（webm）</param>
    private async void PlayVideo(string url)
    {
        int version = ++_videoVersion;
        try
        {
            MediaPlayer player = EnsureMediaPlayer();
            ApplyVolume();
            MuteBackgroundVideo();
            Grid_VideoControls.Visibility = Visibility.Visible;
            UpdateControlsVisibility();
            bool autoPlay = IsUnrestrictedNetwork();

            if (_videoUrl == url && player.Source is not null)
            {
                // 切回同一个视频：画面停在离开时的那一帧，接着播
                if (_videoStarted)
                {
                    MediaPlayerElement_Video.Visibility = Visibility.Visible;
                }
                if (autoPlay)
                {
                    player.Play();
                }
                else
                {
                    Border_VideoPaused.Visibility = Visibility.Visible;
                }
                UpdatePlayPauseIcon();
                UpdatePositionDisplay();
                return;
            }

            ReleaseVideoSource();
            MediaPlayerElement_Video.Visibility = Visibility.Collapsed;
            ResetPositionDisplay();
            SetVideoLoading(autoPlay);
            Border_VideoPaused.Visibility = autoPlay ? Visibility.Collapsed : Visibility.Visible;

            // 官方的宣传视频是 VP9 + Vorbis 的 webm：没装商店的 VP9 扩展就用自带的 libvpx 解码器
            if (!VP9Helper.IsVP9DecoderInstalled())
            {
                VP9Helper.RegisterVP9Decoder();
            }
            // 系统已有 Vorbis 解码器（Web 媒体扩展）时不会注册自带的，自带的实测播出来是静音，见 VP9Helper.RegisterVorbisDecoder
            VP9Helper.RegisterVorbisDecoder();

            var uri = new Uri(url);
            string? cached = await Task.Run(() => FileCache.TryGetCachedFile(uri, out string? path) ? path : null);
            if (version != _videoVersion)
            {
                return;
            }
            if (cached is not null)
            {
                await SetCachedVideoSourceAsync(player, cached, url, version, autoPlay);
            }
            else
            {
                SetVideoSource(player, MediaSource.CreateFromUri(uri), null, false, url, autoPlay);
                // 同时在后台把整个视频下载到缓存（约 100MB，首次会多一份流量），之后打开直接读本地文件。
                // 实测 Media Foundation 串流起播要约 10 秒，而整个文件下载只要约 8 秒：下完时还没开始播就换成本地文件
                SwitchToCachedVideoWhenReady(player, uri, url, version, autoPlay);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Play reservation video {Url}", url);
            if (version == _videoVersion)
            {
                StopVideo();
            }
        }
    }



    /// <summary>
    /// 从本地缓存文件播放
    /// </summary>
    private async Task SetCachedVideoSourceAsync(MediaPlayer player, string file, string url, int version, bool autoPlay)
    {
        IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(file, FileAccessMode.Read);
        if (version != _videoVersion)
        {
            stream.Dispose();
            return;
        }
        // 缓存文件名是哈希、没有扩展名，要明确告诉 Media Foundation 容器格式
        SetVideoSource(player, MediaSource.CreateFromStream(stream, GetVideoContentType(url)), stream, true, url, autoPlay);
    }



    private void SetVideoSource(MediaPlayer player, MediaSource source, IRandomAccessStream? stream, bool fromCache, string url, bool autoPlay)
    {
        var item = new MediaPlaybackItem(source);
        item.AudioTracksChanged += MediaPlaybackItem_AudioTracksChanged;
        // 媒体打开前调用 Play() 会被忽略，自动播放要交给 AutoPlay
        player.AutoPlay = autoPlay;
        player.Source = item;
        _videoStream?.Dispose();
        _videoStream = stream;
        _videoFromCache = fromCache;
        _videoUrl = url;
    }



    /// <summary>
    /// 等后台下载完成；那时串流还没开始播放（仍在缓冲）就换成本地文件
    /// </summary>
    private async void SwitchToCachedVideoWhenReady(MediaPlayer player, Uri uri, string url, int version, bool autoPlay)
    {
        try
        {
            string? file = await FileCache.GetFromCacheAsync(uri);
            if (file is null || version != _videoVersion || _videoUrl != url || _videoStarted || player.Source is null)
            {
                return;
            }
            await SetCachedVideoSourceAsync(player, file, url, version, autoPlay);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Switch reservation video to cache {Url}", url);
        }
    }



    private MediaPlayer EnsureMediaPlayer()
    {
        if (_mediaPlayer is null)
        {
            _mediaPlayer = new MediaPlayer
            {
                IsLoopingEnabled = true,
                AutoPlay = false,
            };
            // 不出现在系统媒体控制里
            _mediaPlayer.CommandManager.IsEnabled = false;
            _mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            MediaPlayerElement_Video.SetMediaPlayer(_mediaPlayer);
        }
        if (_positionTimer is null)
        {
            _positionTimer = DispatcherQueue.CreateTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += (_, _) => UpdatePositionDisplay();
        }
        return _mediaPlayer;
    }



    /// <summary>
    /// 切到图片：暂停视频、保留进度，切回来时接着播
    /// </summary>
    private void HideVideo()
    {
        _videoVersion++;
        try
        {
            if (_mediaPlayer is not null)
            {
                // 还在打开时切走，打开完成后 AutoPlay 会自己开始播放
                _mediaPlayer.AutoPlay = false;
                _mediaPlayer.Pause();
            }
        }
        catch { }
        _positionTimer?.Stop();
        MediaPlayerElement_Video.Visibility = Visibility.Collapsed;
        Border_VideoPaused.Visibility = Visibility.Collapsed;
        Grid_VideoControls.Visibility = Visibility.Collapsed;
        Grid_VideoControls.Opacity = 0;
        Grid_VideoControls.IsHitTestVisible = false;
        SetVideoLoading(false);
    }



    /// <summary>
    /// 停止视频并卸载，回到封面（换一组内容或收起详情页时）
    /// </summary>
    private void StopVideo()
    {
        HideVideo();
        ReleaseVideoSource();
    }



    private void ReleaseVideoSource()
    {
        try
        {
            _mediaPlayer?.Source = null;
        }
        catch { }
        _videoStream?.Dispose();
        _videoStream = null;
        _videoUrl = null;
        _videoFromCache = false;
        _videoStarted = false;
    }



    private static string GetVideoContentType(string url)
    {
        string path = new Uri(url).AbsolutePath;
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".mkv" => "video/x-matroska",
            _ => "video/webm",
        };
    }



    private void SetVideoLoading(bool loading)
    {
        ProgressRing_Video.IsActive = loading;
        ProgressRing_Video.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }



    private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        MediaPlaybackState state = sender.PlaybackState;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_mediaPlayer?.Source is null)
            {
                return;
            }
            if (SelectedItem?.Media is not { IsVideo: true } media || media.ResourceUrl != _videoUrl)
            {
                // 已切到别的内容：打开过程中被 AutoPlay 拉起的播放要停掉
                if (state is MediaPlaybackState.Playing)
                {
                    _mediaPlayer.Pause();
                }
                return;
            }
            switch (state)
            {
                case MediaPlaybackState.Playing:
                    _videoStarted = true;
                    SetVideoLoading(false);
                    Border_VideoPaused.Visibility = Visibility.Collapsed;
                    MediaPlayerElement_Video.Visibility = Visibility.Visible;
                    _positionTimer?.Start();
                    break;
                case MediaPlaybackState.Paused:
                    // 自动播放时打开过程中会短暂经过 Paused，开始播放前不显示暂停图标
                    if (_videoStarted || !_mediaPlayer.AutoPlay)
                    {
                        SetVideoLoading(false);
                        Border_VideoPaused.Visibility = Visibility.Visible;
                    }
                    _positionTimer?.Stop();
                    UpdatePositionDisplay();
                    break;
                case MediaPlaybackState.Opening:
                case MediaPlaybackState.Buffering:
                    SetVideoLoading(true);
                    break;
            }
            UpdatePlayPauseIcon();
            UpdateControlsVisibility();
        });
    }



    private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(UpdatePositionDisplay);
    }



    /// <summary>
    /// 记录音轨的解码器支持情况：缺音频解码器时不会有 MediaFailed，只是静默地没有声音
    /// </summary>
    private void MediaPlaybackItem_AudioTracksChanged(MediaPlaybackItem sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
    {
        try
        {
            foreach (AudioTrack track in sender.AudioTracks)
            {
                if (track.SupportInfo?.DecoderStatus is not MediaDecoderStatus.FullySupported)
                {
                    _logger.LogWarning("Reservation video audio track {Encoding} is not fully supported: decoder {DecoderStatus}", track.GetEncodingProperties()?.Subtype, track.SupportInfo?.DecoderStatus);
                }
                track.OpenFailed -= AudioTrack_OpenFailed;
                track.OpenFailed += AudioTrack_OpenFailed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reservation video audio tracks");
        }
    }



    private void AudioTrack_OpenFailed(AudioTrack sender, AudioTrackOpenFailedEventArgs args)
    {
        _logger.LogWarning("Reservation video audio track open failed: 0x{HResult:X8}", args.ExtendedError?.HResult);
    }



    private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _logger.LogWarning("Reservation video failed: {Error} {Message}", args.Error, args.ErrorMessage);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_videoFromCache && _videoUrl is not null)
            {
                // 缓存文件可能损坏，删掉后下次重新下载
                FileCache.DeleteCacheFile(new Uri(_videoUrl));
            }
            StopVideo();
        });
    }



    /// <summary>
    /// 点击视频画面切换播放与暂停
    /// </summary>
    private void MediaPlayerElement_Video_Tapped(object sender, TappedRoutedEventArgs e)
    {
        TogglePlayPause();
    }



    /// <summary>
    /// 还没出画面时（不自动播放的网络、被暂停）点暂停图标开始播放
    /// </summary>
    private void Border_VideoPaused_Tapped(object sender, TappedRoutedEventArgs e)
    {
        TogglePlayPause();
    }



    private void Button_VideoPlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }



    private void TogglePlayPause()
    {
        try
        {
            if (_mediaPlayer?.Source is null)
            {
                return;
            }
            if (_mediaPlayer.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                _mediaPlayer.Play();
            }
        }
        catch { }
    }



    private void UpdatePlayPauseIcon()
    {
        bool playing = _mediaPlayer?.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening;
        FontIcon_VideoPlayPause.Glyph = playing ? GlyphPause : GlyphPlay;
    }



    /// <summary>
    /// 指针在画面上、暂停中或正在拖进度条时显示控制栏
    /// </summary>
    private void UpdateControlsVisibility()
    {
        bool video = SelectedItem?.Media.IsVideo is true;
        bool paused = _videoStarted && _mediaPlayer?.PlaybackSession.PlaybackState is MediaPlaybackState.Paused;
        bool show = video && (_pointerOverMedia || paused || _seekingByUser);
        Grid_VideoControls.Opacity = show ? 1 : 0;
        Grid_VideoControls.IsHitTestVisible = show;
    }



    private void Grid_Media_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverMedia = true;
        UpdateControlsVisibility();
    }



    private void Grid_Media_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 进出子元素的事件也会冒泡上来，指针仍在画面范围内就不算离开
        Windows.Foundation.Point point = e.GetCurrentPoint(Grid_Media).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < Grid_Media.ActualWidth && point.Y < Grid_Media.ActualHeight)
        {
            return;
        }
        _pointerOverMedia = false;
        UpdateControlsVisibility();
    }



    /// <summary>
    /// 按播放器状态刷新进度条与时间
    /// </summary>
    private void UpdatePositionDisplay()
    {
        if (_mediaPlayer?.Source is null)
        {
            return;
        }
        MediaPlaybackSession session = _mediaPlayer.PlaybackSession;
        double duration = Math.Max(0, session.NaturalDuration.TotalSeconds);
        double position = Math.Clamp(session.Position.TotalSeconds, 0, Math.Max(duration, 0));
        _updatingPosition = true;
        try
        {
            Slider_VideoPosition.Maximum = Math.Max(duration, 1);
            if (!_seekingByUser)
            {
                Slider_VideoPosition.Value = position;
            }
        }
        finally
        {
            _updatingPosition = false;
        }
        TextBlock_VideoTime.Text = $"{FormatTime(_seekingByUser ? Slider_VideoPosition.Value : position)} / {FormatTime(duration)}";
    }



    private void ResetPositionDisplay()
    {
        _updatingPosition = true;
        try
        {
            Slider_VideoPosition.Maximum = 1;
            Slider_VideoPosition.Value = 0;
        }
        finally
        {
            _updatingPosition = false;
        }
        TextBlock_VideoTime.Text = $"{FormatTime(0)} / {FormatTime(0)}";
        UpdatePlayPauseIcon();
    }



    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }



    /// <summary>
    /// 点击或拖动进度条：跳到对应位置
    /// </summary>
    private void Slider_VideoPosition_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingPosition || _mediaPlayer?.Source is null)
        {
            return;
        }
        try
        {
            _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue);
            TextBlock_VideoTime.Text = $"{FormatTime(e.NewValue)} / {FormatTime(_mediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds)}";
        }
        catch { }
    }



    private void Slider_VideoPosition_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _seekingByUser = true;
    }



    private void Slider_VideoPosition_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _seekingByUser = false;
        UpdatePositionDisplay();
        UpdateControlsVisibility();
    }



    /// <summary>
    /// 从设置读出音量与静音，应用到播放器和控制栏
    /// </summary>
    private void ApplyVolume()
    {
        int volume = AppConfig.ReservationVideoVolume;
        _updatingVolume = true;
        try
        {
            Slider_VideoVolume.Value = volume;
        }
        finally
        {
            _updatingVolume = false;
        }
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Volume = volume / 100d;
            _mediaPlayer.IsMuted = AppConfig.ReservationVideoMuted;
        }
        UpdateMuteIcon();
    }



    private void Slider_VideoVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingVolume)
        {
            return;
        }
        _mediaPlayer?.Volume = e.NewValue / 100d;
        _volumeDirty = true;
        if (e.NewValue > 0 && AppConfig.ReservationVideoMuted)
        {
            // 静音时拖音量，视为取消静音
            AppConfig.ReservationVideoMuted = false;
            _mediaPlayer?.IsMuted = false;
        }
        UpdateMuteIcon();
    }



    private void Slider_VideoVolume_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        SaveVolume();
    }



    private void SaveVolume()
    {
        if (_volumeDirty)
        {
            _volumeDirty = false;
            AppConfig.ReservationVideoVolume = (int)Math.Round(Slider_VideoVolume.Value);
        }
    }



    private void Button_VideoMute_Click(object sender, RoutedEventArgs e)
    {
        bool muted = !AppConfig.ReservationVideoMuted;
        AppConfig.ReservationVideoMuted = muted;
        _mediaPlayer?.IsMuted = muted;
        UpdateMuteIcon();
    }



    private void UpdateMuteIcon()
    {
        FontIcon_VideoMute.Glyph = AppConfig.ReservationVideoMuted || Slider_VideoVolume.Value <= 0 ? GlyphMute : GlyphVolume;
    }



    /// <summary>
    /// 播放预约视频期间把当前游戏的背景视频静音，避免两段声音叠在一起
    /// </summary>
    private void MuteBackgroundVideo()
    {
        if (!_backgroundVideoMuted && GetBackgroundVideoVolume() > 0)
        {
            _backgroundVideoMuted = true;
            WeakReferenceMessenger.Default.Send(new VideoBgVolumeChangedMessage(0));
        }
    }



    private void RestoreBackgroundVideoVolume()
    {
        if (_backgroundVideoMuted)
        {
            _backgroundVideoMuted = false;
            WeakReferenceMessenger.Default.Send(new VideoBgVolumeChangedMessage(GetBackgroundVideoVolume()));
        }
    }



    /// <summary>
    /// 当前游戏背景视频实际使用的音量，规则同 <see cref="AppBackground"/>：只有启用自定义背景时才用保存的音量
    /// </summary>
    private static int GetBackgroundVideoVolume()
    {
        var biz = AppConfig.CurrentGameBiz;
        return AppConfig.GetEnableCustomBg(biz) ? AppConfig.GetVideoBgVolume(biz) : 0;
    }



    private static bool IsUnrestrictedNetwork()
    {
        try
        {
            return NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost().NetworkCostType is not (NetworkCostType.Fixed or NetworkCostType.Variable);
        }
        catch
        {
            return true;
        }
    }



    #endregion



    private async void Button_PreRegister_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_homePageUrl))
            {
                await Launcher.LaunchUriAsync(new Uri(_homePageUrl));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open reservation home page {Url}", _homePageUrl);
        }
    }



    private void Button_Back_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }



    /// <summary>
    /// 点在详情页的空白处时吃掉事件，不冒泡到游戏选择面板（面板点空白会整体收起）
    /// </summary>
    private void Grid_Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }


}



/// <summary>
/// 详情页轮播的一项，带缩略图选中态
/// </summary>
public partial class GameReservationMediaItem : ObservableObject
{

    public GameReservationMediaItem(GameReservationMedia media)
    {
        Media = media;
    }


    public GameReservationMedia Media { get; }


    public bool IsSelected
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SelectedOpacity));
                OnPropertyChanged(nameof(DimOpacity));
            }
        }
    }


    public double SelectedOpacity => IsSelected ? 1 : 0;


    public double DimOpacity => IsSelected ? 0 : 1;

}
