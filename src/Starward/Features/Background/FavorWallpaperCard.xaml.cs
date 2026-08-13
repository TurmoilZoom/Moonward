using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace Starward.Features.Background;

/// <summary>
/// 好感壁纸卡片：封面框按密友同行 245×153、下载 / 删除 / 点击设为背景。
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


    public FavorWallpaperCard()
    {
        this.InitializeComponent();
        AddHandler(TappedEvent, new TappedEventHandler(OnTapped), handledEventsToo: true);
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
        new PropertyMetadata(null));


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
        _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: true);
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

}
