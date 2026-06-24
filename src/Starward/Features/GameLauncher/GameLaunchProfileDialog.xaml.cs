using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.UrlProtocol;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 「启动参数配置」对话框：管理每个游戏区服的多套启动配置文件（命令行参数 + 自定义启动程序）。
/// 由首页汉堡菜单的「启动参数配置」打开，样式与 <see cref="GameLauncherSettingDialog"/> 一致。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class GameLaunchProfileDialog : ContentDialog
{


    private readonly ILogger<GameLaunchProfileDialog> _logger = AppConfig.GetLogger<GameLaunchProfileDialog>();


    public GameLaunchProfileDialog()
    {
        this.InitializeComponent();
        Profiles.CollectionChanged += Profiles_CollectionChanged;
        this.Loaded += GameLaunchProfileDialog_Loaded;
        this.Unloaded += GameLaunchProfileDialog_Unloaded;
    }


    public GameId CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; set; }


    private void Profiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanCreateProfile));
    }


    private void GameLaunchProfileDialog_Loaded(object sender, RoutedEventArgs e)
    {
        CurrentGameBiz = CurrentGameId?.GameBiz ?? GameBiz.None;
        WeakReferenceMessenger.Default.Register<AccentColorChangedMessage>(this, OnAccentColorChanged);
        InitializeLaunchProfiles();
    }


    private void GameLaunchProfileDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }


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
    /// 所有启动配置文件，作为下拉框数据源。第一个固定为默认配置文件（Id = "Alice"）。
    /// </summary>
    public ObservableCollection<GameLaunchProfile> Profiles { get; } = new();


    /// <summary>
    /// 当前正在编辑的配置文件（保存后的快照值）。下拉选择通过 SelectedItem 的 OneWay 绑定同步。
    /// </summary>
    public GameLaunchProfile? SelectedProfile
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanDeleteProfile));
                OnPropertyChanged(nameof(SelectedProfileInternalName));
                OnPropertyChanged(nameof(ProfileStartGameUrl));
            }
        }
    }


    /// <summary>
    /// 是否可以删除当前配置文件（默认配置文件不可删除）。
    /// </summary>
    public bool CanDeleteProfile => SelectedProfile is { IsDefault: false };


    /// <summary>
    /// 是否还可以新建配置文件（每个游戏最多 <see cref="GameLaunchProfile.MaxCount"/> 个）。
    /// </summary>
    public bool CanCreateProfile => Profiles.Count < GameLaunchProfile.MaxCount;


    /// <summary>
    /// 当前配置文件的内部名（只读显示在名称右侧）。
    /// </summary>
    public string SelectedProfileInternalName => SelectedProfile?.Id ?? "";


    /// <summary>
    /// 当前配置文件对应的 URL 协议启动指令预览。
    /// </summary>
    public string ProfileStartGameUrl => UrlProtocolService.BuildStartGameUrl(CurrentGameBiz, SelectedProfile?.Id);


    /// <summary>
    /// 是否显示配置文件名称编辑区（点击重命名按钮后为 true）。
    /// </summary>
    public bool IsRenamingProfile
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(RenameAreaVisibility));
            }
        }
    }


    /// <summary>配置文件名称编辑区可见性（避免在根上的 x:Bind 使用 StaticResource 转换器）。</summary>
    public Visibility RenameAreaVisibility => IsRenamingProfile ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>
    /// 工作副本：配置文件中文名。
    /// </summary>
    public string EditingName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateIsDirty();
            }
        }
    } = "";


    /// <summary>
    /// 工作副本：命令行参数。
    /// </summary>
    public string? EditingArgument
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateIsDirty();
            }
        }
    }


    /// <summary>
    /// 工作副本：自定义启动程序路径。是否启用由路径是否非空隐式决定（已去掉开关）。
    /// </summary>
    public string? EditingThirdPartyToolPath
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateIsDirty();
                OnPropertyChanged(nameof(ThirdPartyToolPathVisibility));
                OnPropertyChanged(nameof(IsCmdToggleEnabled));
            }
        }
    }


    /// <summary>自定义启动程序路径行可见性（路径非空时显示）。</summary>
    public Visibility ThirdPartyToolPathVisibility => string.IsNullOrEmpty(EditingThirdPartyToolPath) ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>
    /// 使用 CMD 启动游戏（全局设置）。与自定义启动程序互斥：开启时清空并持久化当前配置文件的自定义启动程序，
    /// 并禁用「选择」按钮；当已设置自定义启动程序时该开关不可操作。
    /// </summary>
    public bool StartGameWithCMD
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.StartGameWithCMD = value;
                OnPropertyChanged(nameof(IsThirdPartyToolEnabled));
                if (value)
                {
                    ClearThirdPartyToolForCmd();
                }
            }
        }
    } = AppConfig.StartGameWithCMD;


    /// <summary>是否可操作「使用 CMD 启动游戏」开关（未设置自定义启动程序时可用）。</summary>
    public bool IsCmdToggleEnabled => string.IsNullOrEmpty(EditingThirdPartyToolPath);


    /// <summary>是否可操作自定义启动程序「选择」按钮（未开启 CMD 启动时可用）。</summary>
    public bool IsThirdPartyToolEnabled => !StartGameWithCMD;


    /// <summary>
    /// 当前编辑内容相对已保存配置文件是否有未保存改动。
    /// </summary>
    public bool IsDirty
    {
        get;
        private set => SetProperty(ref field, value);
    }


    /// <summary>
    /// 该游戏勾选 DX12 时为 true，用于在命令行参数末尾显示只读、不可编辑的 DX12 参数。
    /// </summary>
    public bool ShowDx12Argument
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(Dx12ArgumentVisibility));
            }
        }
    }


    /// <summary>DX12 只读参数 chip 可见性。</summary>
    public Visibility Dx12ArgumentVisibility => ShowDx12Argument ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>
    /// 守卫：代码回退下拉选择时避免再次触发切换逻辑。
    /// </summary>
    private bool _suppressProfileSelectionChanged;

    /// <summary>
    /// 「放弃未保存更改」确认后要执行的动作（切换 / 新建）。
    /// </summary>
    private Action? _pendingDiscardAction;


    private void UpdateIsDirty()
    {
        GameLaunchProfile? p = SelectedProfile;
        if (p is null)
        {
            IsDirty = false;
            return;
        }
        IsDirty = EditingName != p.Name
            || (EditingArgument ?? "") != (p.Argument ?? "")
            || (EditingThirdPartyToolPath ?? "") != (p.ThirdPartyToolPath ?? "");
    }


    private void InitializeLaunchProfiles()
    {
        ShowDx12Argument = AppConfig.GetEnableDX12(CurrentGameBiz);

        Profiles.Clear();
        var defaultProfile = new GameLaunchProfile
        {
            Id = GameLaunchProfile.DefaultId,
            Name = AppConfig.GetDefaultLaunchProfileName(CurrentGameBiz) ?? Lang.GameLauncherSettingDialog_DefaultProfileName,
            Argument = AppConfig.GetStartArgument(CurrentGameBiz),
            EnableThirdPartyTool = AppConfig.GetEnableThirdPartyTool(CurrentGameBiz),
            ThirdPartyToolPath = GameLauncherService.GetThirdPartyToolPath(CurrentGameId),
        };
        Profiles.Add(defaultProfile);
        foreach (GameLaunchProfile extra in AppConfig.GetExtraLaunchProfiles(CurrentGameBiz))
        {
            if (!string.IsNullOrEmpty(extra.Id) && !extra.IsDefault)
            {
                Profiles.Add(extra);
            }
        }

        string? selectedId = AppConfig.GetSelectedLaunchProfileId(CurrentGameBiz);
        GameLaunchProfile target = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? defaultProfile;
        SelectProfileCore(target);
    }


    private void SelectProfileCore(GameLaunchProfile profile)
    {
        SelectedProfile = profile;
        EditingName = profile.Name;
        EditingArgument = profile.Argument;
        EditingThirdPartyToolPath = profile.ThirdPartyToolPath;
        IsRenamingProfile = false;
        IsDirty = false;
        AppConfig.SetSelectedLaunchProfileId(CurrentGameBiz, profile.Id);
    }


    [RelayCommand]
    private void ShowRenameProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }
        IsRenamingProfile = true;
        DispatcherQueue.TryEnqueue(() => TextBox_ProfileName?.Focus(FocusState.Programmatic));
    }


    private void ComboBox_LaunchProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileSelectionChanged)
        {
            return;
        }
        if (ComboBox_LaunchProfile.SelectedItem is not GameLaunchProfile target || ReferenceEquals(target, SelectedProfile))
        {
            return;
        }
        if (IsDirty && SelectedProfile is GameLaunchProfile current)
        {
            _suppressProfileSelectionChanged = true;
            ComboBox_LaunchProfile.SelectedItem = current;
            _suppressProfileSelectionChanged = false;
            _pendingDiscardAction = () => SelectProfileCore(target);
            FlyoutBase.ShowAttachedFlyout(ComboBox_LaunchProfile);
        }
        else
        {
            SelectProfileCore(target);
        }
    }


    private void DiscardProfileChanges_Confirm_Click(object sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(ComboBox_LaunchProfile)?.Hide();
        Action? action = _pendingDiscardAction;
        _pendingDiscardAction = null;
        action?.Invoke();
    }


    private void DiscardProfileChanges_Cancel_Click(object sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(ComboBox_LaunchProfile)?.Hide();
        _pendingDiscardAction = null;
    }


    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is not GameLaunchProfile p)
        {
            return;
        }
        string name = string.IsNullOrWhiteSpace(EditingName) ? p.Name : EditingName.Trim();
        if (p.IsDefault)
        {
            AppConfig.SetStartArgument(CurrentGameBiz, EditingArgument);
            string? savedPath = GameLauncherService.SetThirdPartyToolPath(CurrentGameId, EditingThirdPartyToolPath);
            bool enableTool = !string.IsNullOrWhiteSpace(savedPath);
            AppConfig.SetEnableThirdPartyTool(CurrentGameBiz, enableTool);
            AppConfig.SetDefaultLaunchProfileName(CurrentGameBiz, name);
            p.Argument = EditingArgument;
            p.EnableThirdPartyTool = enableTool;
            p.ThirdPartyToolPath = savedPath;
            EditingThirdPartyToolPath = savedPath;
        }
        else
        {
            p.Argument = EditingArgument;
            p.ThirdPartyToolPath = EditingThirdPartyToolPath;
            p.EnableThirdPartyTool = !string.IsNullOrWhiteSpace(EditingThirdPartyToolPath);
        }
        p.Name = name;
        EditingName = name;
        if (!p.IsDefault)
        {
            PersistExtraProfiles();
        }
        IsRenamingProfile = false;
        UpdateIsDirty();
    }


    private void PersistExtraProfiles()
    {
        AppConfig.SetExtraLaunchProfiles(CurrentGameBiz, Profiles.Where(p => !p.IsDefault).ToList());
    }


    [RelayCommand]
    private void NewProfile()
    {
        if (!CanCreateProfile)
        {
            return;
        }
        if (IsDirty && SelectedProfile is not null)
        {
            _pendingDiscardAction = CreateNewProfileCore;
            FlyoutBase.ShowAttachedFlyout(ComboBox_LaunchProfile);
            return;
        }
        CreateNewProfileCore();
    }


    private void CreateNewProfileCore()
    {
        string? id = GetNextAvailableProfileId();
        if (id is null)
        {
            return;
        }
        var profile = new GameLaunchProfile
        {
            Id = id,
            Name = Lang.GameLauncherSettingDialog_LaunchProfile + Profiles.Count,
        };
        Profiles.Add(profile);
        PersistExtraProfiles();
        SelectProfileCore(profile);
    }


    private string? GetNextAvailableProfileId()
    {
        var used = new HashSet<string>(Profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        foreach (string name in GameLaunchProfile.InternalNames)
        {
            if (!used.Contains(name))
            {
                return name;
            }
        }
        return null;
    }


    private void DeleteProfile_Confirm_Click(object sender, RoutedEventArgs e)
    {
        Button_DeleteProfile.Flyout?.Hide();
        if (SelectedProfile is not GameLaunchProfile p || p.IsDefault)
        {
            return;
        }
        Profiles.Remove(p);
        PersistExtraProfiles();
        SelectProfileCore(Profiles[0]);
    }


    private void DeleteProfile_Cancel_Click(object sender, RoutedEventArgs e)
    {
        Button_DeleteProfile.Flyout?.Hide();
    }


    [RelayCommand]
    private async Task ChangeThirdPartyPathAsync()
    {
        try
        {
            var file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot);
            if (File.Exists(file))
            {
                EditingThirdPartyToolPath = file;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change third party tool path ({biz})", CurrentGameBiz);
        }
    }


    [RelayCommand]
    private async Task OpenThirdPartyToolFolderAsync()
    {
        try
        {
            string? path = EditingThirdPartyToolPath;
            if (File.Exists(path))
            {
                var folder = Path.GetDirectoryName(path);
                var file = await StorageFile.GetFileFromPathAsync(path);
                var option = new FolderLauncherOptions();
                option.ItemsToSelect.Add(file);
                await Launcher.LaunchFolderPathAsync(folder, option);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open third party tool folder {folder}", EditingThirdPartyToolPath);
        }
    }


    [RelayCommand]
    private void DeleteThirdPartyToolPath()
    {
        EditingThirdPartyToolPath = null;
    }


    /// <summary>
    /// 开启 CMD 启动时，清空并持久化当前配置文件的自定义启动程序（两者互斥，避免重新打开时残留）。
    /// </summary>
    private void ClearThirdPartyToolForCmd()
    {
        EditingThirdPartyToolPath = null;
        if (SelectedProfile is GameLaunchProfile p && (p.EnableThirdPartyTool || !string.IsNullOrEmpty(p.ThirdPartyToolPath)))
        {
            p.EnableThirdPartyTool = false;
            p.ThirdPartyToolPath = null;
            if (p.IsDefault)
            {
                GameLauncherService.SetThirdPartyToolPath(CurrentGameId, null);
                AppConfig.SetEnableThirdPartyTool(CurrentGameBiz, false);
            }
            else
            {
                PersistExtraProfiles();
            }
            UpdateIsDirty();
        }
    }


    private void Button_ThirdPartyToolInfo_Click(object sender, RoutedEventArgs e)
    {
        TeachingTip_ThirdPartyTool.IsOpen = true;
    }


    private async void Button_CopyProfileUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ProfileStartGameUrl))
            {
                return;
            }
            ClipboardHelper.SetText(ProfileStartGameUrl);
            if (sender is Button button)
            {
                await CopySuccessAsync(button);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy profile start game url ({biz})", CurrentGameBiz);
        }
    }


    private static async Task CopySuccessAsync(Button button)
    {
        try
        {
            button.IsEnabled = false;
            if (button.Content is FontIcon icon)
            {
                // Accept
                icon.Glyph = "\uF78C";
                await Task.Delay(1000);
            }
        }
        finally
        {
            button.IsEnabled = true;
            if (button.Content is FontIcon icon)
            {
                icon.Glyph = "\uE71B";
            }
        }
    }


    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }


}