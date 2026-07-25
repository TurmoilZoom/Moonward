using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Display;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Core;
using Starward.Features.GameLauncher;
using Starward.Features.GameSelector;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;


namespace Starward.Features.GameSetting;

public sealed partial class GameSettingPage : PageBase
{

    private readonly ILogger<GameSettingPage> _logger = AppConfig.GetLogger<GameSettingPage>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();



    public GameSettingPage()
    {
        this.InitializeComponent();
    }




    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Image_Emoji.Source = CurrentGameBiz.ToGame().Value switch
        {
            GameBiz.bh3 => new BitmapImage(AppConfig.EmojiAI),
            GameBiz.hk4e => new BitmapImage(AppConfig.EmojiPaimon),
            GameBiz.hkrpg => new BitmapImage(AppConfig.EmojiPom),
            GameBiz.nap => new BitmapImage(AppConfig.EmojiBangboo),
            _ => null,
        };
        if (CurrentGameId.GameBiz == GameBiz.bh3_global)
        {
            CurrentGameBiz = CurrentGameId.Id switch
            {
                "g0mMIvshDb" => GameBiz.bh3_jp,
                "uxB4MC7nzC" => GameBiz.bh3_kr,
                "bxPTXSET5t" => GameBiz.bh3_os,
                "wkE5P5WsIf" => GameBiz.bh3_asia,
                _ => GameBiz.bh3_global,
            };
        }
    }


    protected override async void OnLoaded()
    {
        await InitializeGameSettingAsync();
    }


    protected override void OnUnloaded()
    {
        if (_displayInformation is not null)
        {
            _displayInformation.AdvancedColorInfoChanged -= _displayInformation_AdvancedColorInfoChanged;
            _displayInformation.Dispose();
            _displayInformation = null!;
        }
    }


    public bool IsLanguageSettingEnable { get; set => SetProperty(ref field, value); }

    public bool IsGraphicsSettingEnable { get; set => SetProperty(ref field, value); }

    public bool IsApplyButtonEnable { get; set => SetProperty(ref field, value); }

    public string ErrorMessage { get; set => SetProperty(ref field, value); } = Lang.GameSettingPage_SettingNotEffect; // 游戏运行时应用的设置无法生效


    public int LanguageIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public int StarRailFpsIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public bool EnableGenshinHDR
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public bool HDRNotSupported { get; set => SetProperty(ref field, value); }

    public bool HDRNotEnabled { get; set => SetProperty(ref field, value); }


    private async Task InitializeGameSettingAsync()
    {
        try
        {
            var localVersion = await _gameLauncherService.GetLocalGameVersionAsync(CurrentGameId);
            if (localVersion is null)
            {
                StackPanel_Emoji.Visibility = Visibility.Visible;
                return;
            }
            if (CurrentGameBiz.ToGame().Value is GameBiz.hk4e or GameBiz.hkrpg)
            {
                IsLanguageSettingEnable = true;
                var langSetting = GameSettingService.GetGameVoiceLanguageSetting(CurrentGameBiz);
                if (langSetting != null)
                {
                    LanguageIndex = langSetting.Value;
                }
            }
            if (CurrentGameBiz.Game is GameBiz.hkrpg)
            {
                IsGraphicsSettingEnable = true;
                StackPanel_StarRailFPS.Visibility = Visibility.Visible;
                StarRailFpsIndex = GameSettingService.GetStarRailFPSIndex(CurrentGameBiz);
            }
            if (CurrentGameBiz.Game is GameBiz.hk4e)
            {
                IsGraphicsSettingEnable = true;
                StackPanel_GenshinHDR.Visibility = Visibility.Visible;
                EnableGenshinHDR = AppConfig.EnableGenshinHDR;
                _displayInformation = DisplayInformation.CreateForWindowId(this.XamlRoot.GetAppWindow().Id);
                _displayInformation.AdvancedColorInfoChanged += _displayInformation_AdvancedColorInfoChanged;
                UpdateHdrState(_displayInformation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize Game Setting");
        }
        finally
        {
            IsApplyButtonEnable = false;
        }
    }


    [RelayCommand]
    private async Task OpenGenshinHDRLumianceSettingWindow()
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new MainWindowDragRectAdaptToGameIconMessage(true));
            await new GenshinHDRLuminanceSettingDialog { XamlRoot = this.XamlRoot, CurrentGameBiz = this.CurrentGameBiz }.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            WeakReferenceMessenger.Default.Send(new MainWindowDragRectAdaptToGameIconMessage());
        }
    }



    [RelayCommand]
    private void ApplySetting()
    {
        try
        {
            if (IsLanguageSettingEnable)
            {
                GameSettingService.SetGameVoiceLanguageSetting(CurrentGameBiz, LanguageIndex);
            }
            if (IsGraphicsSettingEnable)
            {
                if (CurrentGameBiz.Game is GameBiz.hkrpg)
                {
                    GameSettingService.SetStarRailFPSIndex(CurrentGameBiz, StarRailFpsIndex);
                }
                if (CurrentGameBiz.Game is GameBiz.hk4e)
                {
                    AppConfig.EnableGenshinHDR = EnableGenshinHDR;
                    GameSettingService.SetGenshinEnableHDR(CurrentGameBiz, EnableGenshinHDR);
                }
            }
            // 游戏运行时应用的设置无法生效
            ErrorMessage = Lang.GameSettingPage_SettingNotEffect;
            IsApplyButtonEnable = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Apply Setting");
        }
    }



    private DisplayInformation _displayInformation;

    private void _displayInformation_AdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        UpdateHdrState(sender);
    }


    private void UpdateHdrState(DisplayInformation displayInformation)
    {
        try
        {
            HDRNotEnabled = false;
            HDRNotSupported = false;
            var info = displayInformation.GetAdvancedColorInfo();
            if (!info.IsAdvancedColorKindAvailable(DisplayAdvancedColorKind.HighDynamicRange))
            {
                HDRNotSupported = true;
            }
            else if (info.CurrentAdvancedColorKind is not DisplayAdvancedColorKind.HighDynamicRange)
            {
                HDRNotEnabled = true;
            }
        }
        catch { }
    }



}
