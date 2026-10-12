using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.GameRecord;
using Starward.Features.UrlProtocol;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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


    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    /// <summary>
    /// 常用参数面板：独立 Popup（仅 XamlRoot，不挂在 ContentDialog 内容树 Parent 上），
    /// 避免 Offset 相对对话框 Parent 导致与整窗居中不一致。模式同 <c>GeetestVerifyPopup</c>。
    /// </summary>
    private Popup? _commandLineArgumentPickerPopup;


    public GameLaunchProfileDialog()
    {
        this.InitializeComponent();
        this.Loaded += GameLaunchProfileDialog_Loaded;
        this.Unloaded += GameLaunchProfileDialog_Unloaded;
    }


    public GameId CurrentGameId { get; set; }


    public GameBiz CurrentGameBiz { get; set; }


    private void GameLaunchProfileDialog_Loaded(object sender, RoutedEventArgs e)
    {
        CurrentGameBiz = CurrentGameId?.GameBiz ?? GameBiz.None;
        WeakReferenceMessenger.Default.Register<AccentColorChangedMessage>(this, OnAccentColorChanged);
        InitializeLaunchProfiles();
    }


    private void GameLaunchProfileDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        // 对话框关闭时一并收起常用参数面板，避免悬挂 Popup
        DetachCommandLineArgumentPickerXamlRootChanged();
        if (_commandLineArgumentPickerPopup is { IsOpen: true })
        {
            _commandLineArgumentPickerPopup.IsOpen = false;
        }
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
    /// 所有启动配置文件，作为下拉框数据源，按编号排序（配置文件 1 被删除时不在其中）；不含「无」。
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
                OnPropertyChanged(nameof(SelectedProfileName));
                OnPropertyChanged(nameof(ProfileStartGameUrl));
            }
        }
    }


    /// <summary>
    /// 是否可以删除当前配置文件：任何配置文件（含配置文件 1）都可删，但至少保留一个。
    /// </summary>
    public bool CanDeleteProfile => SelectedProfile is not null && Profiles.Count > 1;


    /// <summary>
    /// 是否还可以新建配置文件（已取消数量上限，始终为 true）。
    /// </summary>
    public bool CanCreateProfile => true;


    /// <summary>
    /// 当前配置文件的内部名（只读显示在名称右侧）。
    /// </summary>
    /// <summary>
    /// 当前选中配置的显示名，用于删除确认文案（如「删除  配置文件2」）。
    /// </summary>
    public string SelectedProfileName => SelectedProfile?.Name ?? "";


    /// <summary>
    /// 当前配置文件对应的 URL 协议启动指令预览（含编辑中的登录 UID）。
    /// </summary>
    public string ProfileStartGameUrl => UrlProtocolService.BuildStartGameUrl(CurrentGameBiz, SelectedProfile?.Id, EditingLoginUid > 0 && IsLoginAccountEnabled ? EditingLoginUid : null);


    /// <summary>
    /// 登录账号下拉选项（「不指定」+ 当前区服米游社角色）。
    /// </summary>
    public ObservableCollection<LoginAccountOption> LoginAccountOptions { get; } = new();


    /// <summary>
    /// 是否显示「登录账号」区域（仅国服支持 auth ticket 自动登录）。
    /// </summary>
    public bool ShowLoginAccount
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(LoginAccountVisibility));
            }
        }
    }


    /// <summary>登录账号行可见性。</summary>
    public Visibility LoginAccountVisibility => ShowLoginAccount ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>
    /// 登录账号是否可选：设了自定义启动程序（程序或链接）时启动的是工具而非游戏，登录票据不会附加，禁用。
    /// </summary>
    public bool IsLoginAccountEnabled => string.IsNullOrEmpty(EditingThirdPartyToolPath);


    /// <summary>「使用自定义启动程序时不会自动登录」说明的可见性。</summary>
    public Visibility LoginAccountIgnoredVisibility => IsLoginAccountEnabled ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>
    /// 工作副本：登录账号游戏 UID；0 表示不指定。
    /// </summary>
    public long EditingLoginUid
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateIsDirty();
                OnPropertyChanged(nameof(ProfileStartGameUrl));
                SyncSelectedLoginAccountOption();
            }
        }
    }


    /// <summary>
    /// 当前选中的登录账号选项（ComboBox SelectedItem）。
    /// </summary>
    public LoginAccountOption? SelectedLoginAccountOption
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value is not null && EditingLoginUid != value.Uid)
            {
                EditingLoginUid = value.Uid;
            }
        }
    }


    /// <summary>
    /// 是否正在重命名配置文件（「更多」菜单点重命名后为 true，名称输入框替换下拉框显示）。
    /// </summary>
    public bool IsRenamingProfile
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(RenameAreaVisibility));
                OnPropertyChanged(nameof(ProfileSelectorVisibility));
            }
        }
    }


    /// <summary>配置文件名称编辑区可见性（避免在根上的 x:Bind 使用 StaticResource 转换器）。</summary>
    public Visibility RenameAreaVisibility => IsRenamingProfile ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>配置文件下拉框可见性：重命名时让位给名称输入框。</summary>
    public Visibility ProfileSelectorVisibility => IsRenamingProfile ? Visibility.Collapsed : Visibility.Visible;


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
                OnPropertyChanged(nameof(ThirdPartyToolHintVisibility));
                OnPropertyChanged(nameof(IsCmdToggleEnabled));
                OnPropertyChanged(nameof(IsArgumentEnabled));
                OnPropertyChanged(nameof(ArgumentNotPassedToUrlVisibility));
                OnPropertyChanged(nameof(Dx12ArgumentVisibility));
                OnPropertyChanged(nameof(IsLoginAccountEnabled));
                OnPropertyChanged(nameof(LoginAccountIgnoredVisibility));
                OnPropertyChanged(nameof(ProfileStartGameUrl));
                IsThirdPartyToolMoonward = GameLauncherService.IsMoonwardExecutable(value);
                ThirdPartyToolUrlWarning = GetThirdPartyToolUrlProblem(value);
            }
        }
    }


    /// <summary>自定义启动程序是否为链接（URL 协议）。</summary>
    public bool IsThirdPartyToolUrl => GameLauncherService.IsLaunchUrl(EditingThirdPartyToolPath);


    /// <summary>命令行参数是否可编辑：自定义启动程序是链接时参数无处可传，禁用。</summary>
    public bool IsArgumentEnabled => !IsThirdPartyToolUrl;


    /// <summary>「通过链接启动时不传递命令行参数」说明的可见性。</summary>
    public Visibility ArgumentNotPassedToUrlVisibility => IsThirdPartyToolUrl ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>
    /// 链接输入框确认时的错误提示（协议不能用 / 没有程序注册该协议）；为 <see langword="null"/> 时不显示，改动输入后清除。
    /// </summary>
    public string? ThirdPartyToolUrlError
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ThirdPartyToolUrlErrorVisibility));
            }
        }
    }


    /// <summary>链接输入框错误提示的可见性。</summary>
    public Visibility ThirdPartyToolUrlErrorVisibility => string.IsNullOrEmpty(ThirdPartyToolUrlError) ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>
    /// 已设置的链接用不了时在路径条下方显示的提示：对应程序后来被卸载，或是手改配置写入的不可用协议。启动时不会启动游戏或会被忽略。
    /// </summary>
    public string? ThirdPartyToolUrlWarning
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ThirdPartyToolUrlWarningVisibility));
            }
        }
    }


    /// <summary>已设置链接用不了的提示的可见性。</summary>
    public Visibility ThirdPartyToolUrlWarningVisibility => string.IsNullOrEmpty(ThirdPartyToolUrlWarning) ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>
    /// 检查链接能否作为自定义启动程序。
    /// </summary>
    /// <param name="url">链接；不是链接时视为没有问题。</param>
    /// <returns>不能用时返回提示文案，能用或不是链接时返回 <see langword="null"/>。</returns>
    private static string? GetThirdPartyToolUrlProblem(string? url)
    {
        if (!GameLauncherService.IsLaunchUrl(url))
        {
            return null;
        }
        if (!GameLauncherService.IsAllowedLaunchUrl(url))
        {
            return Lang.GameLaunchProfileDialog_ThirdPartyToolUrlInvalid;
        }
        if (!GameLauncherService.IsLaunchUrlRegistered(url))
        {
            return Lang.GameLaunchProfileDialog_ThirdPartyToolUrlNotRegistered;
        }
        return null;
    }


    /// <summary>
    /// 刚选的文件被拒绝，或已保存的路径（拦截前的旧配置）就是 Moonward 本体；为 true 时在路径下方显示警告。
    /// </summary>
    public bool IsThirdPartyToolMoonward
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ThirdPartyToolMoonwardWarningVisibility));
            }
        }
    }


    /// <summary>「不能使用 Moonward 本身」警告的可见性。</summary>
    public Visibility ThirdPartyToolMoonwardWarningVisibility => IsThirdPartyToolMoonward ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>自定义启动程序路径条可见性（路径非空时显示）。</summary>
    public Visibility ThirdPartyToolPathVisibility => string.IsNullOrEmpty(EditingThirdPartyToolPath) ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>自定义启动程序未设置时的说明占位可见性（与路径条互斥）。</summary>
    public Visibility ThirdPartyToolHintVisibility => string.IsNullOrEmpty(EditingThirdPartyToolPath) ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>
    /// 工作副本：使用 CMD 启动游戏。与自定义启动程序互斥：开启时禁用「选择」按钮；
    /// 已设置自定义启动程序时该开关不可操作。
    /// </summary>
    public bool EditingStartWithCmd
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateIsDirty();
                OnPropertyChanged(nameof(IsThirdPartyToolEnabled));
            }
        }
    }


    /// <summary>是否可操作「使用 CMD 启动游戏」开关（未设置自定义启动程序时可用）。</summary>
    public bool IsCmdToggleEnabled => string.IsNullOrEmpty(EditingThirdPartyToolPath);


    /// <summary>是否可操作自定义启动程序「选择」按钮（未开启 CMD 启动时可用）。</summary>
    public bool IsThirdPartyToolEnabled => !EditingStartWithCmd;


    /// <summary>
    /// 当前编辑内容相对已保存配置文件是否有未保存改动。
    /// </summary>
    public bool IsDirty
    {
        get;
        private set => SetProperty(ref field, value);
    }


    /// <summary>
    /// 该游戏勾选 DX12 时为 true。用于在命令行参数旁显示可按配置文件关闭的 DX12 参数徽章。
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


    /// <summary>
    /// 工作副本：本配置文件是否跳过自动附加 <c>-use-d3d12</c>。
    /// </summary>
    public bool EditingSkipAutoDx12
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateIsDirty();
                OnPropertyChanged(nameof(Dx12ArgumentVisibility));
            }
        }
    }


    /// <summary>DX12 参数徽章可见性：全局已开启且本配置尚未关闭时显示；通过链接启动时不传参数，不显示。</summary>
    public Visibility Dx12ArgumentVisibility => (ShowDx12Argument && !EditingSkipAutoDx12 && !IsThirdPartyToolUrl) ? Visibility.Visible : Visibility.Collapsed;


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
        long savedUid = NormalizeLoginUid(p.LoginUid);
        IsDirty = EditingName != p.Name
            || (EditingArgument ?? "") != (p.Argument ?? "")
            || (EditingThirdPartyToolPath ?? "") != (p.ThirdPartyToolPath ?? "")
            || EditingLoginUid != savedUid
            || EditingSkipAutoDx12 != p.SkipAutoDx12
            || EditingStartWithCmd != GetSavedStartWithCmd(p);
    }


    /// <summary>
    /// 配置文件已保存的 CMD 启动状态。
    /// 已设自定义启动程序时视为关闭：两者互斥，启动时也是自定义程序优先；
    /// 否则旧数据（旧版全局开关开启时设过自定义程序）会显示为「开启且不可操作」，同时「选择」按钮被禁用。
    /// </summary>
    /// <param name="profile">配置文件。</param>
    /// <returns>是否使用 CMD 启动。</returns>
    private static bool GetSavedStartWithCmd(GameLaunchProfile profile)
    {
        // 旧版为全局开关，未按配置文件保存过时沿用旧值
        return (profile.StartWithCmd ?? AppConfig.StartGameWithCMD) && string.IsNullOrEmpty(profile.ThirdPartyToolPath);
    }


    private void InitializeLaunchProfiles()
    {
        ShowDx12Argument = AppConfig.GetEnableDX12(CurrentGameBiz);
        ShowLoginAccount = CurrentGameBiz.Server is "cn";
        LoadLoginAccountOptions();

        Profiles.Clear();
        List<GameLaunchProfile> extras = AppConfig.GetExtraLaunchProfiles(CurrentGameBiz)
            .Where(p => GameLaunchProfile.IsKnownId(p.Id) && !p.IsDefault && !p.IsNone)
            .ToList();
        if (extras.Count == 0 && AppConfig.GetDefaultLaunchProfileDeleted(CurrentGameBiz))
        {
            // 删除时会保留最后一个，正常走不到；手改设置等导致一个都不剩时恢复一个空的配置文件 1，对话框总要有可编辑的配置
            AppConfig.ResetDefaultLaunchProfile(CurrentGameBiz);
            AppConfig.SetDefaultLaunchProfileDeleted(CurrentGameBiz, false);
        }
        if (!AppConfig.GetDefaultLaunchProfileDeleted(CurrentGameBiz))
        {
            Profiles.Add(new GameLaunchProfile
            {
                Id = GameLaunchProfile.DefaultId,
                Name = ProfileNameFromId(GameLaunchProfile.DefaultId, AppConfig.GetDefaultLaunchProfileName(CurrentGameBiz)),
                Argument = AppConfig.GetStartArgument(CurrentGameBiz),
                EnableThirdPartyTool = AppConfig.GetEnableThirdPartyTool(CurrentGameBiz),
                ThirdPartyToolPath = GameLauncherService.GetThirdPartyToolPath(CurrentGameId),
                LoginUid = AppConfig.GetDefaultLaunchLoginUid(CurrentGameBiz),
                SkipAutoDx12 = AppConfig.GetDefaultLaunchProfileSkipAutoDx12(CurrentGameBiz),
                StartWithCmd = AppConfig.GetDefaultLaunchProfileStartWithCmd(CurrentGameBiz),
            });
        }
        // 按编号排：新建时补上的空缺编号排回原位置，而不是列表末尾
        foreach (GameLaunchProfile extra in extras.OrderBy(p => GameLaunchProfile.TryGetIndex(p.Id)))
        {
            extra.Name = ProfileNameFromId(extra.Id, extra.Name);
            Profiles.Add(extra);
        }

        string? selectedId = AppConfig.GetSelectedLaunchProfileId(CurrentGameBiz);
        GameLaunchProfile target = Profiles.FirstOrDefault(p => string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];
        SelectProfileCore(target);
    }


    /// <summary>
    /// 加载与米游社工具箱一致的角色列表作为登录账号选项。
    /// </summary>
    private void LoadLoginAccountOptions()
    {
        LoginAccountOptions.Clear();
        LoginAccountOptions.Add(new LoginAccountOption
        {
            Uid = 0,
            DisplayName = Lang.GameLauncherSettingDialog_LoginAccountNone,
        });
        if (!ShowLoginAccount)
        {
            return;
        }
        try
        {
            foreach (GameRecordRole role in _gameRecordService.GetGameRoles(CurrentGameBiz))
            {
                LoginAccountOptions.Add(LoginAccountOption.FromRole(role));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load login account options ({biz})", CurrentGameBiz);
        }
    }


    /// <summary>
    /// 让登录账号下拉框选中与 <see cref="EditingLoginUid"/> 对应的选项；未指定或角色已不在列表中时选中「不指定」。
    /// </summary>
    private void SyncSelectedLoginAccountOption()
    {
        LoginAccountOption? match = LoginAccountOptions.FirstOrDefault(o => o.Uid == EditingLoginUid)
            ?? LoginAccountOptions.FirstOrDefault();
        if (!ReferenceEquals(SelectedLoginAccountOption, match))
        {
            SelectedLoginAccountOption = match;
        }
    }


    private static long NormalizeLoginUid(long? uid) => uid is > 0 ? uid.Value : 0;


    /// <summary>
    /// 由 configN 得到默认显示名「配置文件 N」（序号与 Id 严格一致）；非空自定义名则保留。
    /// </summary>
    private static string ProfileNameFromId(string id, string? customName)
    {
        int index = GameLaunchProfile.TryGetIndex(id) ?? 1;
        string fromId = string.Format(Lang.GameLauncherSettingDialog_ProfileNameFormat, index);
        return string.IsNullOrWhiteSpace(customName) ? fromId : customName.Trim();
    }


    private void SelectProfileCore(GameLaunchProfile profile)
    {
        SelectedProfile = profile;
        EditingName = profile.Name;
        EditingArgument = profile.Argument;
        EditingThirdPartyToolPath = profile.ThirdPartyToolPath;
        // 路径没变时 setter 不会重算，上一个配置文件里被拒绝的警告要在这里清掉
        IsThirdPartyToolMoonward = GameLauncherService.IsMoonwardExecutable(profile.ThirdPartyToolPath);
        EditingLoginUid = NormalizeLoginUid(profile.LoginUid);
        // uid 未变化时 setter 不会同步下拉框（如新建配置文件、首次打开时都是 0），会留空而不是显示「不指定」
        SyncSelectedLoginAccountOption();
        EditingSkipAutoDx12 = profile.SkipAutoDx12;
        EditingStartWithCmd = GetSavedStartWithCmd(profile);
        IsRenamingProfile = false;
        IsDirty = false;
        AppConfig.SetSelectedLaunchProfileId(CurrentGameBiz, profile.Id);
        OnPropertyChanged(nameof(ProfileStartGameUrl));
    }


    [RelayCommand]
    private void ShowRenameProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }
        IsRenamingProfile = true;
        // 输入框此刻才从折叠变为可见，等布局后再聚焦
        DispatcherQueue.TryEnqueue(() =>
        {
            TextBox_ProfileName.Focus(FocusState.Programmatic);
            TextBox_ProfileName.SelectAll();
        });
    }


    /// <summary>
    /// 名称输入框按键：Enter 保存（未改动则仅退出重命名），Esc 取消重命名。
    /// 用 PreviewKeyDown 抢在 ContentDialog 之前处理，否则 Esc 会直接关闭对话框。
    /// </summary>
    private void TextBox_ProfileName_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            if (IsDirty)
            {
                SaveProfile();
            }
            else
            {
                IsRenamingProfile = false;
            }
            FocusProfileSelector();
        }
        else if (e.Key is VirtualKey.Escape)
        {
            e.Handled = true;
            CancelRenameProfile();
            FocusProfileSelector();
        }
    }


    /// <summary>
    /// 取消重命名：名称还原为已保存的值（其余未保存改动保留）。
    /// </summary>
    private void CancelRenameProfile()
    {
        if (SelectedProfile is GameLaunchProfile p)
        {
            EditingName = p.Name;
        }
        IsRenamingProfile = false;
    }


    /// <summary>
    /// 退出重命名后把焦点还给配置文件下拉框（它刚从折叠恢复可见，需等布局）。
    /// </summary>
    private void FocusProfileSelector()
    {
        DispatcherQueue.TryEnqueue(() => ComboBox_LaunchProfile.Focus(FocusState.Programmatic));
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
            FlyoutBase.ShowAttachedFlyout(Grid_ProfileBar);
        }
        else
        {
            SelectProfileCore(target);
        }
    }


    private void DiscardProfileChanges_Confirm_Click(object sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(Grid_ProfileBar)?.Hide();
        Action? action = _pendingDiscardAction;
        _pendingDiscardAction = null;
        action?.Invoke();
    }


    private void DiscardProfileChanges_Cancel_Click(object sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(Grid_ProfileBar)?.Hide();
        _pendingDiscardAction = null;
    }


    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is not GameLaunchProfile p)
        {
            return;
        }
        // 空名称回退为与 configN 序号一致的「配置文件 N」
        string name = string.IsNullOrWhiteSpace(EditingName) ? ProfileNameFromId(p.Id, null) : EditingName.Trim();
        long loginUid = EditingLoginUid > 0 ? EditingLoginUid : 0;
        if (p.IsDefault)
        {
            AppConfig.SetStartArgument(CurrentGameBiz, EditingArgument);
            string? savedPath = GameLauncherService.SetThirdPartyToolPath(CurrentGameId, EditingThirdPartyToolPath);
            bool enableTool = !string.IsNullOrWhiteSpace(savedPath);
            AppConfig.SetEnableThirdPartyTool(CurrentGameBiz, enableTool);
            AppConfig.SetDefaultLaunchProfileName(CurrentGameBiz, name);
            AppConfig.SetDefaultLaunchLoginUid(CurrentGameBiz, loginUid > 0 ? loginUid : null);
            AppConfig.SetDefaultLaunchProfileSkipAutoDx12(CurrentGameBiz, EditingSkipAutoDx12);
            AppConfig.SetDefaultLaunchProfileStartWithCmd(CurrentGameBiz, EditingStartWithCmd);
            p.Argument = EditingArgument;
            p.EnableThirdPartyTool = enableTool;
            p.ThirdPartyToolPath = savedPath;
            p.SkipAutoDx12 = EditingSkipAutoDx12;
            EditingThirdPartyToolPath = savedPath;
        }
        else
        {
            p.Argument = EditingArgument;
            p.ThirdPartyToolPath = EditingThirdPartyToolPath;
            p.EnableThirdPartyTool = !string.IsNullOrWhiteSpace(EditingThirdPartyToolPath);
            p.SkipAutoDx12 = EditingSkipAutoDx12;
        }
        p.StartWithCmd = EditingStartWithCmd;
        p.LoginUid = loginUid > 0 ? loginUid : null;
        p.Name = name;
        EditingName = name;
        OnPropertyChanged(nameof(SelectedProfileName));
        OnPropertyChanged(nameof(ProfileStartGameUrl));
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
        if (IsDirty && SelectedProfile is not null)
        {
            _pendingDiscardAction = CreateNewProfileCore;
            FlyoutBase.ShowAttachedFlyout(Grid_ProfileBar);
            return;
        }
        CreateNewProfileCore();
    }


    private void CreateNewProfileCore()
    {
        // 取最小的空缺编号（删过的编号会被重新用上，含配置文件 1）
        string id = GameLaunchProfile.GetNextAvailableId(Profiles.Select(p => p.Id));
        // Name 序号必须与 configN 中的 N 一致（如 config3 → 配置文件3）
        var profile = new GameLaunchProfile
        {
            Id = id,
            Name = ProfileNameFromId(id, null),
            // 显式写入，新配置文件不继承旧版全局 CMD 开关
            StartWithCmd = false,
        };
        if (profile.IsDefault)
        {
            // 配置文件 1 的数据仍存 legacy 键：删除时已清空，这里再重置一遍并取消删除标记
            AppConfig.ResetDefaultLaunchProfile(CurrentGameBiz);
            AppConfig.SetDefaultLaunchProfileDeleted(CurrentGameBiz, false);
        }
        // 按编号插入，补上的空缺编号排回原位置
        int index = GameLaunchProfile.TryGetIndex(id) ?? int.MaxValue;
        Profiles.Insert(Profiles.Count(p => (GameLaunchProfile.TryGetIndex(p.Id) ?? int.MaxValue) < index), profile);
        PersistExtraProfiles();
        SelectProfileCore(profile);
    }


    /// <summary>
    /// 「更多」菜单点删除：菜单关闭后在同一按钮上弹出删除确认。
    /// </summary>
    private void MenuFlyoutItem_DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        // 菜单此时正在关闭，同步弹出确认 Flyout 可能被关闭流程吞掉，排队到下一轮再弹
        DispatcherQueue.TryEnqueue(() => FlyoutBase.ShowAttachedFlyout(Button_ProfileMore));
    }


    private void DeleteProfile_Confirm_Click(object sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(Button_ProfileMore)?.Hide();
        if (SelectedProfile is not GameLaunchProfile p || Profiles.Count <= 1)
        {
            return;
        }
        Profiles.Remove(p);
        if (p.IsDefault)
        {
            // 配置文件 1 的数据在 legacy 键里：清空并标记已删除，编号空出来后新建时会重新用上
            AppConfig.ResetDefaultLaunchProfile(CurrentGameBiz);
            AppConfig.SetDefaultLaunchProfileDeleted(CurrentGameBiz, true);
        }
        PersistExtraProfiles();
        if (string.Equals(AppConfig.GetActiveLaunchProfileId(CurrentGameBiz), p.Id, StringComparison.OrdinalIgnoreCase))
        {
            // 正在生效的配置被删：改回「无」。否则编号被新建的配置复用时，会悄悄变成按新配置启动
            AppConfig.SetActiveLaunchProfileId(CurrentGameBiz, GameLaunchProfile.NoneId);
        }
        SelectProfileCore(Profiles[0]);
    }


    private void DeleteProfile_Cancel_Click(object sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(Button_ProfileMore)?.Hide();
    }


    [RelayCommand]
    private async Task ChangeThirdPartyPathAsync()
    {
        try
        {
            var file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot);
            if (File.Exists(file))
            {
                if (GameLauncherService.IsMoonwardExecutable(file))
                {
                    // 拉起的新实例只会把激活转回本实例，游戏起不来；参数带 startgame 时还会无限拉起自己
                    _logger.LogWarning("Rejected Moonward itself as third party tool ({biz}): {path}", CurrentGameBiz, file);
                    IsThirdPartyToolMoonward = true;
                    return;
                }
                EditingThirdPartyToolPath = file;
                // 重选了与当前相同的路径时 setter 不触发，警告要手动清掉
                IsThirdPartyToolMoonward = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change third party tool path ({biz})", CurrentGameBiz);
        }
    }


    /// <summary>
    /// 打开链接输入 Flyout：已设置的是链接时带入原值，否则留空。
    /// </summary>
    private void ShowThirdPartyToolUrlFlyout()
    {
        TextBox_ThirdPartyToolUrl.Text = IsThirdPartyToolUrl ? EditingThirdPartyToolPath : "";
        ThirdPartyToolUrlError = null;
        FlyoutBase.ShowAttachedFlyout(Button_ThirdPartyToolSelect);
    }


    private void MenuFlyoutItem_EnterThirdPartyToolUrl_Click(object sender, RoutedEventArgs e)
    {
        // 菜单此时正在关闭，同步弹出 Flyout 可能被关闭流程吞掉，排队到下一轮再弹
        DispatcherQueue.TryEnqueue(ShowThirdPartyToolUrlFlyout);
    }


    private void Flyout_ThirdPartyToolUrl_Opened(object sender, object e)
    {
        TextBox_ThirdPartyToolUrl.Focus(FocusState.Programmatic);
        TextBox_ThirdPartyToolUrl.SelectAll();
    }


    private void TextBox_ThirdPartyToolUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        ThirdPartyToolUrlError = null;
    }


    /// <summary>
    /// 链接输入框按键：Enter 确认，Esc 关闭 Flyout。用 PreviewKeyDown 抢在 ContentDialog 之前处理，否则 Esc 会关掉整个对话框。
    /// </summary>
    private void TextBox_ThirdPartyToolUrl_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            ConfirmThirdPartyToolUrl();
        }
        else if (e.Key is VirtualKey.Escape)
        {
            e.Handled = true;
            FlyoutBase.GetAttachedFlyout(Button_ThirdPartyToolSelect)?.Hide();
        }
    }


    private void ThirdPartyToolUrl_Confirm_Click(object sender, RoutedEventArgs e)
    {
        ConfirmThirdPartyToolUrl();
    }


    private void ThirdPartyToolUrl_Cancel_Click(object sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(Button_ThirdPartyToolSelect)?.Hide();
    }


    /// <summary>
    /// 把输入的链接设为自定义启动程序（保存后生效）；不能用时留在 Flyout 里提示。
    /// </summary>
    private void ConfirmThirdPartyToolUrl()
    {
        string url = TextBox_ThirdPartyToolUrl.Text.Trim();
        // 不是链接（如误填文件路径、拼错的协议头）也按不能用的协议提示
        string? problem = GameLauncherService.IsLaunchUrl(url) ? GetThirdPartyToolUrlProblem(url) : Lang.GameLaunchProfileDialog_ThirdPartyToolUrlInvalid;
        if (problem is not null)
        {
            ThirdPartyToolUrlError = problem;
            return;
        }
        EditingThirdPartyToolPath = url;
        FlyoutBase.GetAttachedFlyout(Button_ThirdPartyToolSelect)?.Hide();
    }


    [RelayCommand]
    private async Task OpenThirdPartyToolFolderAsync()
    {
        try
        {
            if (IsThirdPartyToolUrl)
            {
                ShowThirdPartyToolUrlFlyout();
                return;
            }
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


    /// <summary>
    /// 关闭本配置文件的自动 DX12 参数。全局 DX12 开关不变，保存后启动时不再附加 <c>-use-d3d12</c>。
    /// </summary>
    private void DismissDx12Argument_Click(object sender, RoutedEventArgs e)
    {
        EditingSkipAutoDx12 = true;
    }


    /// <summary>
    /// 打开/关闭常用参数勾选面板（独立 Popup：整窗铺满 + 卡片布局居中，与 ContentDialog 同参照）。
    /// </summary>
    private void Button_CommandLineArgumentPicker_Click(object sender, RoutedEventArgs e)
    {
        Popup popup = EnsureCommandLineArgumentPickerPopup();
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        // 本配置已关闭自动 DX12 时不再只读托管，允许在常用参数里改图形 API。
        // 否则 nap 始终走全局开关；其它游戏仅在已开启全局 DX12 时按只读同步，避免与启动附加重复。
        bool isDx12ManagedByApp = !EditingSkipAutoDx12 && (CurrentGameBiz.Game is GameBiz.nap || ShowDx12Argument);
        CommandLineArgumentPicker.LoadFromArgument(
            EditingArgument,
            CurrentGameBiz,
            isDx12ManagedByApp: isDx12ManagedByApp,
            isDx12Enabled: ShowDx12Argument && !EditingSkipAutoDx12);

        popup.XamlRoot = XamlRoot;
        AlignCommandLineArgumentPickerPopup(popup);
        popup.IsOpen = true;
    }


    /// <summary>
    /// 确保使用「仅 XamlRoot、无内容树 Parent」的独立 Popup。
    /// XAML 里的 <see cref="Popup_CommandLineArgumentPicker"/> 只作 Child 模板宿主，首次打开时把 Child 挪到独立实例。
    /// </summary>
    private Popup EnsureCommandLineArgumentPickerPopup()
    {
        if (_commandLineArgumentPickerPopup is not null)
        {
            return _commandLineArgumentPickerPopup;
        }

        // 从对话框内容树中的 Popup 卸下 Child，避免 Offset 相对 ContentDialog 内 Parent
        UIElement? child = Popup_CommandLineArgumentPicker.Child;
        Popup_CommandLineArgumentPicker.Child = null;
        Popup_CommandLineArgumentPicker.IsOpen = false;

        var popup = new Popup
        {
            Child = child,
            // Child 已铺满整窗时系统 light dismiss 无法点「外侧」；改点空白手动关
            IsLightDismissEnabled = false,
            HorizontalOffset = 0,
            VerticalOffset = 0,
        };
        popup.Opened += Popup_CommandLineArgumentPicker_Opened;
        popup.Closed += Popup_CommandLineArgumentPicker_Closed;
        _commandLineArgumentPickerPopup = popup;
        return popup;
    }


    /// <summary>
    /// Popup 打开后再次对齐（布局就绪），并监听窗口尺寸变化。
    /// </summary>
    private void Popup_CommandLineArgumentPicker_Opened(object sender, object e)
    {
        if (sender is Popup popup)
        {
            AlignCommandLineArgumentPickerPopup(popup);
        }
        AttachCommandLineArgumentPickerXamlRootChanged();
    }


    private void Popup_CommandLineArgumentPicker_Closed(object sender, object e)
    {
        DetachCommandLineArgumentPickerXamlRootChanged();
    }


    private void AttachCommandLineArgumentPickerXamlRootChanged()
    {
        if (XamlRoot is null)
        {
            return;
        }
        XamlRoot.Changed -= CommandLineArgumentPickerXamlRoot_Changed;
        XamlRoot.Changed += CommandLineArgumentPickerXamlRoot_Changed;
    }


    private void DetachCommandLineArgumentPickerXamlRootChanged()
    {
        if (XamlRoot is null)
        {
            return;
        }
        XamlRoot.Changed -= CommandLineArgumentPickerXamlRoot_Changed;
    }


    private void CommandLineArgumentPickerXamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_commandLineArgumentPickerPopup is { IsOpen: true } popup)
        {
            AlignCommandLineArgumentPickerPopup(popup);
        }
    }


    /// <summary>
    /// 将定位层铺满 XamlRoot 整窗，Offset 置 0，卡片由 Grid Center 对齐——
    /// 与 ContentDialog 同一整窗参照（含左侧导航与标题栏区域）。
    /// 同 <c>GeetestVerifyPopup.SizeToRoot</c>：独立 Popup + 铺满 + 布局居中，不手算卡片坐标。
    /// </summary>
    private void AlignCommandLineArgumentPickerPopup(Popup popup)
    {
        if (XamlRoot is null)
        {
            return;
        }

        double rootWidth = XamlRoot.Size.Width;
        double rootHeight = XamlRoot.Size.Height;
        if (rootWidth <= 0 || rootHeight <= 0)
        {
            return;
        }

        // 独立 Popup：原点即 XamlRoot 左上角（整窗客户区，含导航/标题栏）
        popup.HorizontalOffset = 0;
        popup.VerticalOffset = 0;
        Grid_CommandLineArgumentPickerRoot.Width = rootWidth;
        Grid_CommandLineArgumentPickerRoot.Height = rootHeight;
        Border_CommandLineArgumentPickerHost.MaxHeight = Math.Min(640, Math.Max(200, rootHeight - 96));
    }


    /// <summary>
    /// 点定位层空白（卡片外）关闭，模拟 light dismiss。
    /// </summary>
    private void Grid_CommandLineArgumentPickerRoot_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && (ReferenceEquals(source, Border_CommandLineArgumentPickerHost)
                || IsDescendantOf(Border_CommandLineArgumentPickerHost, source)))
        {
            return;
        }

        if (_commandLineArgumentPickerPopup is not null)
        {
            _commandLineArgumentPickerPopup.IsOpen = false;
        }
        e.Handled = true;
    }


    /// <summary>
    /// 点击卡片本身不关闭面板（阻止冒泡到定位层）。
    /// </summary>
    private void Border_CommandLineArgumentPickerHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }


    /// <summary>
    /// 判断 <paramref name="node"/> 是否为 <paramref name="ancestor"/> 的子孙节点。
    /// </summary>
    private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// 勾选面板组合结果变更时写回配置文件的命令行参数输入框。
    /// </summary>
    private void CommandLineArgumentPicker_CombinedArgumentChanged(object sender, string? argument)
    {
        EditingArgument = argument;
    }


    /// <summary>
    /// 「登录账号」下拉选项：与米游社工具箱角色同源。
    /// </summary>
    public sealed class LoginAccountOption
    {
        /// <summary>游戏角色 UID；0 表示不指定。</summary>
        public long Uid { get; set; }

        /// <summary>下拉显示文本。</summary>
        public string DisplayName { get; set; } = "";

        public static LoginAccountOption FromRole(GameRecordRole role)
        {
            string nickname = string.IsNullOrWhiteSpace(role.Nickname) ? "-" : role.Nickname;
            string region = string.IsNullOrWhiteSpace(role.RegionName) ? "" : role.RegionName;
            string level = role.Level > 0 ? $" Lv.{role.Level}" : "";
            string regionPart = string.IsNullOrEmpty(region) ? "" : $"（{region}）";
            return new LoginAccountOption
            {
                Uid = role.Uid,
                DisplayName = $"{nickname}{regionPart}{level} · {role.Uid}",
            };
        }
    }


}