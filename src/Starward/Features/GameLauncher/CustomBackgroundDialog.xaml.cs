using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

#pragma warning disable MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
#pragma warning disable MVVMTK0045 // Using [ObservableProperty] on fields is not AOT compatible for WinRT


namespace Starward.Features.GameLauncher;

[INotifyPropertyChanged]
public sealed partial class CustomBackgroundDialog : ContentDialog
{


    private readonly ILogger<CustomBackgroundDialog> _logger = AppConfig.GetLogger<CustomBackgroundDialog>();


    private readonly BackgroundService _backgroundService = AppConfig.GetService<BackgroundService>();


    public CustomBackgroundDialog()
    {
        this.InitializeComponent();
        this.Loaded += CustomBackgroundDialog_Loaded;
        this.Unloaded += CustomBackgroundDialog_Unloaded;
    }



    public GameId CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; set; }



    private void CustomBackgroundDialog_Loaded(object sender, RoutedEventArgs e)
    {
        CurrentGameBiz = CurrentGameId?.GameBiz ?? GameBiz.None;
        WeakReferenceMessenger.Default.Register<AccentColorChangedMessage>(this, OnAccentColorChanged);
        InitializeCustomBg();
    }


    private void CustomBackgroundDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }


    /// <summary>
    /// 自定义背景的强调色变化后，刷新对话框自身的视觉树，
    /// 使“选择”等使用强调色的控件实时生效
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



    /// <summary>
    /// 是否启用自定义背景
    /// </summary>
    [ObservableProperty]
    public bool _EnableCustomBg;
    partial void OnEnableCustomBgChanged(bool value)
    {
        AppConfig.SetEnableCustomBg(CurrentGameBiz, value);
        if (value)
        {
            // 启用自定义背景时，将其记录为当前使用的背景。
            string? customBg = AppConfig.GetCustomBg(CurrentGameBiz);
            if (!string.IsNullOrWhiteSpace(customBg))
            {
                AppConfig.SetBg(CurrentGameBiz, customBg);
            }
        }
        // 音量随自定义背景开关联动：关闭时静音，开启时恢复该游戏的音量。
        WeakReferenceMessenger.Default.Send(new VideoBgVolumeChangedMessage(value ? _videoBgVolume : 0));
        OnPropertyChanged(nameof(VideoBgVolume));
        OnPropertyChanged(nameof(VideoBgVolumeButtonIcon));
        WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
    }


    /// <summary>
    /// 自定义背景，文件名，存储在 UserDataFolder/bg
    /// </summary>
    public string? CustomBg { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 修改背景错误信息
    /// </summary>
    public string? ChangeBgError { get; set => SetProperty(ref field, value); }


    private void InitializeCustomBg()
    {
        _EnableCustomBg = AppConfig.GetEnableCustomBg(CurrentGameBiz);
        CustomBg = AppConfig.GetCustomBg(CurrentGameBiz);
        _videoBgVolume = AppConfig.GetVideoBgVolume(CurrentGameBiz);
        OnPropertyChanged(nameof(EnableCustomBg));
        OnPropertyChanged(nameof(VideoBgVolume));
        OnPropertyChanged(nameof(VideoBgVolumeButtonIcon));
    }



    /// <summary>
    /// 修改自定义背景
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task ChangeCustomBgAsync()
    {
        try
        {
            ChangeBgError = null;
            string? name = await _backgroundService.ChangeCustomBackgroundFileAsync(this.XamlRoot);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            CustomBg = name;
            AppConfig.SetCustomBg(CurrentGameBiz, name);
            AppConfig.SetBg(CurrentGameBiz, name);
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
        }
        catch (COMException ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_CannotDecodeFile;
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs;
            _logger.LogError(ex, "Change custom background failed");
        }
    }



    /// <summary>
    /// 打开自定义背景文件
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task OpenCustomBgAsync()
    {
        try
        {
            string path = Path.Join(AppConfig.CacheFolder, "bg", CustomBg);
            if (File.Exists(path))
            {
                await Launcher.LaunchUriAsync(new Uri(path));
            }
        }
        catch { }
    }



    /// <summary>
    /// 删除自定义背景
    /// </summary>
    [RelayCommand]
    private void DeleteCustomBg()
    {
        CustomBg = null;
        AppConfig.SetCustomBg(CurrentGameBiz, null);
        WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
    }



    /// <summary>
    /// 视频背景音量，每个游戏区服独立设置。关闭自定义背景时显示为 0 且不可调整。
    /// </summary>
    private int _videoBgVolume;
    public int VideoBgVolume
    {
        get => EnableCustomBg ? _videoBgVolume : 0;
        set
        {
            // 关闭自定义背景时不可调整。
            if (!EnableCustomBg)
            {
                return;
            }
            if (SetProperty(ref _videoBgVolume, value))
            {
                OnPropertyChanged(nameof(VideoBgVolumeButtonIcon));
                WeakReferenceMessenger.Default.Send(new VideoBgVolumeChangedMessage(value));
                AppConfig.SetVideoBgVolume(CurrentGameBiz, value);
            }
        }
    }



    /// <summary>
    /// 音量图标
    /// </summary>
    public string VideoBgVolumeButtonIcon => VideoBgVolume switch
    {
        > 66 => "",
        > 33 => "",
        > 1 => "",
        _ => "",
    };


    private int notMuteVolume = 100;

    /// <summary>
    /// 静音
    /// </summary>
    [RelayCommand]
    private void Mute()
    {
        if (VideoBgVolume > 0)
        {
            notMuteVolume = VideoBgVolume;
            VideoBgVolume = 0;
        }
        else
        {
            VideoBgVolume = notMuteVolume;
        }
    }



    /// <summary>
    /// 接受拖放文件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_BackgroundDragIn_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }



    /// <summary>
    /// 拖放文件，修改自定义背景
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void Grid_BackgroundDragIn_Drop(object sender, DragEventArgs e)
    {
        ChangeBgError = null;
        var defer = e.GetDeferral();
        try
        {
            if ((await e.DataView.GetStorageItemsAsync()).FirstOrDefault() is StorageFile file)
            {
                string? name = await BackgroundService.ChangeCustomBackgroundFileAsync(file);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }
                CustomBg = name;
                AppConfig.SetCustomBg(CurrentGameBiz, name);
                AppConfig.SetBg(CurrentGameBiz, name);
                if (EnableCustomBg)
                {
                    WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
                }
                else
                {
                    EnableCustomBg = true;
                }
            }
        }
        catch (COMException ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_CannotDecodeFile;
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs;
            _logger.LogError(ex, "Change custom background failed");
        }
        defer.Complete();
    }



    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }


}
