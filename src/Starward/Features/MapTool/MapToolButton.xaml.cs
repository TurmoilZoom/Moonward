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
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Starward.Features.MapTool;

/// <summary>
/// 游戏启动页右侧「地图工具」：点击打开站点列表，在浏览器中打开对应地图。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class MapToolButton : UserControl
{

    private readonly ILogger<MapToolButton> _logger = AppConfig.GetLogger<MapToolButton>();


    public MapToolButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }


    /// <summary>
    /// 语言切换后刷新标题，并按新语言重建站点列表。
    /// </summary>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
        RebuildOptions();
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


    public ObservableCollection<MapToolOption> Options { get; } = new();


    private void Button_MapTool_Loaded(object sender, RoutedEventArgs e)
    {
        Lottie_MapTool.SetProgress(0);
        InitializeForCurrentGame();
    }


    private void Button_MapTool_Unloaded(object sender, RoutedEventArgs e)
    {
        Lottie_MapTool.Stop();
        Options.Clear();
    }


    /// <summary>
    /// 悬浮时播放一次 Lottie 后停在末帧；离开后回到首帧。
    /// </summary>
    private void Button_MapTool_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = Lottie_MapTool.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 指针离开后停止动画并回到首帧。
    /// </summary>
    private void Button_MapTool_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Lottie_MapTool.Stop();
        Lottie_MapTool.SetProgress(0);
    }


    private void InitializeForCurrentGame()
    {
        if (CurrentGameId is null || !GameFeatureConfig.FromGameId(CurrentGameId).SupportMapTool)
        {
            this.Visibility = Visibility.Collapsed;
            return;
        }
        this.Visibility = Visibility.Visible;
        RebuildOptions();
    }


    private void RebuildOptions()
    {
        Options.Clear();
        if (CurrentGameId is null)
        {
            return;
        }
        foreach (MapToolOption option in MapToolCatalog.GetOptions(CurrentGameId.GameBiz))
        {
            Options.Add(option);
        }
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


    private void Item_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MapToolOption option })
        {
            _ = OpenUrlAsync(option.Url);
            Button_MapTool.Flyout?.Hide();
        }
    }


    private async Task OpenUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return;
        }
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open map tool url failed: {Url}", url);
        }
    }

}
