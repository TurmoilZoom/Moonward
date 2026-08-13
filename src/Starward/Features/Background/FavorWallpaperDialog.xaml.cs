using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;

namespace Starward.Features.Background;

/// <summary>
/// 好感壁纸独立对话框；所选视频仍写入自定义背景。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class FavorWallpaperDialog : ContentDialog
{

    public FavorWallpaperDialog()
    {
        this.InitializeComponent();
        this.Loaded += FavorWallpaperDialog_Loaded;
        this.Unloaded += FavorWallpaperDialog_Unloaded;
    }


    public GameId? CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; set; }


    private void FavorWallpaperDialog_Loaded(object sender, RoutedEventArgs e)
    {
        CurrentGameBiz = CurrentGameId?.GameBiz ?? GameBiz.None;
        WeakReferenceMessenger.Default.Register<AccentColorChangedMessage>(this, OnAccentColorChanged);
        FavorPanel.CurrentGameId = CurrentGameId;
        FavorPanel.CurrentGameBiz = CurrentGameBiz;
        _ = FavorPanel.EnsureLoadedAsync();
    }


    private void FavorWallpaperDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }


    /// <summary>
    /// 设为背景后强调色变化时刷新对话框视觉树，使「使用中」徽标跟上。
    /// </summary>
    private void OnAccentColorChanged(object _, AccentColorChangedMessage __)
    {
        try
        {
            if (this.Content is FrameworkElement ele)
            {
                ele.RequestedTheme = ele.ActualTheme switch
                {
                    ElementTheme.Light => ElementTheme.Dark,
                    ElementTheme.Dark => ElementTheme.Light,
                    _ => ElementTheme.Default,
                };
                ele.RequestedTheme = ElementTheme.Default;
            }
        }
        catch { }
    }


    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }

}
