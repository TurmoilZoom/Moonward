using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.HoYoPlay;
using Starward.Helpers;
using Starward.RPC;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace Starward.Features.GameInstall;

[INotifyPropertyChanged]
public sealed partial class InstallGameDialog : ContentDialog
{

    private const double GB = 1 << 30;


    private readonly ILogger<InstallGameDialog> _logger = AppConfig.GetLogger<InstallGameDialog>();

    private readonly HoYoPlayService _hoYoPlayService = AppConfig.GetService<HoYoPlayService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();


    public InstallGameDialog()
    {
        this.InitializeComponent();
        this.Loaded += InstallGameDialog_Loaded;
        this.Unloaded += InstallGameDialog_Unloaded;
    }



    public GameId CurrentGameId { get; set; }



    private void InstallGameDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (CurrentGameId is null)
        {
            _logger.LogWarning("CurrentGameId is null.");
            this.Hide();
            return;
        }
        Telemetry.Track("install_dialog_show", CurrentGameId.GameBiz);
        SetDefaultInstallationPath();
        _ = GetGamePackageAsync();
    }



    private void InstallGameDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        Segmented_SelectLanguage.SelectionChanged -= Segmented_SelectLanguage_SelectionChanged;
        Segmented_SelectLanguage.Items.Clear();
    }



    private void SetDefaultInstallationPath()
    {
        try
        {
            string? defaultFolder = AppConfig.DefaultGameInstallationPath;
            if (Directory.Exists(defaultFolder))
            {
                SetInstallationPath(Path.GetFullPath(Path.Combine(defaultFolder, CurrentGameId.GameBiz)));
                return;
            }
            // 自动查找也会检查这个目录，路径规则统一放在 GameLauncherService
            string target = Path.Combine(GameLauncherService.GetFallbackGameInstallationFolder(), CurrentGameId.GameBiz);
            if (Path.IsPathFullyQualified(target))
            {
                SetInstallationPath(Path.GetFullPath(target));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Set default install path.");
        }
    }



    private async Task GetGamePackageAsync()
    {
        try
        {
            ErrorMessage = null;
            GameConfig? config = await _hoYoPlayService.GetGameConfigAsync(CurrentGameId);
            if (config is null)
            {
                _logger.LogWarning("GameConfig of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                this.Hide();
                return;
            }
            if (!string.IsNullOrWhiteSpace(config.AudioPackageScanDir))
            {
                _needAudioPackage = true;
                Border_Resources.Visibility = Visibility.Visible;
                StackPanel_SelectLanguage.Visibility = Visibility.Visible;
                SetDefaultAudioPackage();
            }
            if (config.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK)
            {
                var branch = await _hoYoPlayService.GetGameBranchAsync(CurrentGameId);
                if (branch is not null)
                {
                    _gameSophonChunkBuild = await _hoYoPlayService.GetGameSophonChunkBuildAsync(branch, branch.Main);
                    // 基础资源只在 Chunk 模式提供（官方同样如此），压缩包没有按分类拆分
                    if (_gameSophonChunkBuild is not null && GameScenarioPackage.IsSupported(config, branch.Main))
                    {
                        InitializeScenarioPackage(config, branch.Main);
                    }
                }
            }
            if (_gameSophonChunkBuild is null)
            {
                _gamePackage = await _hoYoPlayService.GetGamePackageAsync(CurrentGameId);
            }
            _gamePackageLoaded = true;
            ComputePackageSize();
            CheckCanStartInstallation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game package.");
            ErrorMessage = GetMiHoYoRequestErrorMessage(ex);
        }
    }



    /// <summary>
    /// 将启动器公开接口的 API 或 HTTP 异常转换为适合安装对话框显示的错误文案。
    /// </summary>
    /// <param name="exception">拉取游戏包信息时捕获的异常。</param>
    /// <returns>本地化 API 文案或原始非 API 异常消息。</returns>
    private static string GetMiHoYoRequestErrorMessage(Exception exception)
    {
        return exception is miHoYoApiException or System.Net.Http.HttpRequestException
            ? MiHoYoApiErrorFeedbackFactory.Create(exception, MiHoYoApiContext.LauncherPublicApi).Message
            : exception.Message;
    }



    /// <summary>
    /// 显示完整资源 / 基础资源选项，默认选完整资源（与官方一致）
    /// </summary>
    /// <param name="config">游戏配置，提供两个选项的说明与是否标「推荐」</param>
    /// <param name="package">要安装的分支，提供各分类属于哪个资源场景</param>
    private void InitializeScenarioPackage(GameConfig config, GameBranchPackage package)
    {
        _scenarioPackageSupported = true;
        _scenarioPackageInfo = config.ScenarioPackageInfo;
        _fullOnlyMatchingFields = GameScenarioPackage.GetFullOnlyMatchingFields(package);
        Border_Resources.Visibility = Visibility.Visible;
        StackPanel_ScenarioPackage.Visibility = Visibility.Visible;
        Border_FullPackageRecommend.Visibility = config.EnableFullPackageRecommend ? Visibility.Visible : Visibility.Collapsed;
        RadioButton_FullPackage.IsChecked = true;
    }



    /// <summary>
    /// 切换完整资源 / 基础资源
    /// </summary>
    private void RadioButton_ScenarioPackage_Checked(object sender, RoutedEventArgs e)
    {
        string? description = SelectedPackageType is GameScenarioPackageType.Base
            ? _scenarioPackageInfo?.BasePackageDescription
            : _scenarioPackageInfo?.FullPackageDescription;
        ScenarioPackageDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ComputePackageSize();
    }



    /// <summary>
    /// 当前选择的资源场景；不区分资源场景的游戏为 <see cref="GameScenarioPackageType.Unknown"/>
    /// </summary>
    private GameScenarioPackageType SelectedPackageType => !_scenarioPackageSupported
        ? GameScenarioPackageType.Unknown
        : RadioButton_BasePackage.IsChecked is true ? GameScenarioPackageType.Base : GameScenarioPackageType.Full;



    private void SetDefaultAudioPackage()
    {
        if (_needAudioPackage)
        {
            Segmented_SelectLanguage.SelectedIndex = CultureInfo.CurrentUICulture.Name[..2] switch
            {
                "zh" => 0,
                "en" => 1,
                "ja" => 2,
                "ko" => 3,
                _ => 2,
            };
        }
    }



    private bool _needAudioPackage;


    private AudioLanguage _audioLanguage;


    private GamePackage? _gamePackage;


    private GameSophonChunkBuild? _gameSophonChunkBuild;


    /// <summary>
    /// 是否提供完整资源 / 基础资源选项
    /// </summary>
    private bool _scenarioPackageSupported;


    private GameScenarioPackageInfo? _scenarioPackageInfo;


    /// <summary>
    /// 只属于完整资源的分类，选基础资源时不下载
    /// </summary>
    private HashSet<string> _fullOnlyMatchingFields = [];


    /// <summary>
    /// 包体信息是否已拉取完成，用于区分「仍在加载」和「确实没有安装包」
    /// </summary>
    private bool _gamePackageLoaded;


    private string _selectPath;


    public string InstallationPath { get; set => SetProperty(ref field, value); }


    public string? ErrorMessage { get; set => SetProperty(ref field, value); }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnzipSpaceText))]
    public partial long UnzipSpaceBytes { get; set; }

    public string UnzipSpaceText => UnzipSpaceBytes == 0 ? "..." : $"{UnzipSpaceBytes / GB:F2} GB";


    /// <summary>
    /// 需要下载的大小（压缩后）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadSizeText))]
    public partial long DownloadSizeBytes { get; set; }

    public string DownloadSizeText => DownloadSizeBytes == 0 ? "..." : $"{DownloadSizeBytes / GB:F2} GB";


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullPackageSizeText))]
    public partial long FullPackageSizeBytes { get; set; }

    public string FullPackageSizeText => FullPackageSizeBytes == 0 ? "..." : $"{FullPackageSizeBytes / GB:F2} GB";


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BasePackageSizeText))]
    public partial long BasePackageSizeBytes { get; set; }

    public string BasePackageSizeText => BasePackageSizeBytes == 0 ? "..." : $"{BasePackageSizeBytes / GB:F2} GB";


    /// <summary>
    /// 所选语音的下载大小，没选语言时为 0
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioPackageSizeText))]
    public partial long AudioPackageSizeBytes { get; set; }

    public string AudioPackageSizeText => AudioPackageSizeBytes == 0 ? "" : $"{AudioPackageSizeBytes / GB:F2} GB";


    /// <summary>
    /// 所选资源场景的说明，没有时隐藏
    /// </summary>
    public string? ScenarioPackageDescription { get; set => SetProperty(ref field, value); }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableSpaceText))]
    public partial long AvailableSpaceBytes { get; set; }

    public string AvailableSpaceText => AvailableSpaceBytes == 0 ? "..." : $"{AvailableSpaceBytes / GB:F2} GB";


    [ObservableProperty]
    public partial bool AutomaticallyCreateSubfolderForInstall { get; set; } = AppConfig.AutomaticallyCreateSubfolderForInstall;
    partial void OnAutomaticallyCreateSubfolderForInstallChanged(bool value)
    {
        AppConfig.AutomaticallyCreateSubfolderForInstall = value;
        if (!string.IsNullOrWhiteSpace(_selectPath))
        {
            if (value)
            {
                SetInstallationPath(Path.Combine(_selectPath, CurrentGameId.GameBiz));
            }
            else
            {
                SetInstallationPath(_selectPath);
            }
        }
    }



    /// <summary>
    /// 按所选资源场景与语音统计下载大小、解压所需空间，以及两个资源选项各自的大小
    /// </summary>
    private void ComputePackageSize()
    {
        try
        {
            long downloadSize = 0, unzipSize = 0, audioSize = 0;
            List<string?> langs = Segmented_SelectLanguage.SelectedItems.Cast<SegmentedItem>().Select(x => x.Tag as string).ToList();
            if (_gamePackage is not null)
            {
                // 压缩包的 decompressed_size 是官方给的所需空间估算（约为压缩包的 2.2 倍）
                downloadSize += _gamePackage.Main.Major!.GamePackages.Sum(x => x.Size);
                unzipSize += _gamePackage.Main.Major.GamePackages.Sum(x => x.DecompressedSize);
                foreach (string? lang in langs)
                {
                    if (_gamePackage.Main.Major.AudioPackages.FirstOrDefault(x => x.Language == lang) is GamePackageFile gamePackageFile)
                    {
                        audioSize += gamePackageFile.Size;
                        unzipSize += gamePackageFile.DecompressedSize;
                    }
                }
            }
            else if (_gameSophonChunkBuild is not null)
            {
                bool isBase = SelectedPackageType is GameScenarioPackageType.Base;
                long fullSize = 0, baseSize = 0;
                foreach (GameSophonChunkManifest manifest in _gameSophonChunkBuild.Manifests)
                {
                    if (manifest.MatchingField.Length is 5 or 10 && manifest.MatchingField.Contains('-'))
                    {
                        // 跳过语音包 zh-cn or mini-zh-cn
                        continue;
                    }
                    bool fullOnly = _fullOnlyMatchingFields.Contains(manifest.MatchingField);
                    fullSize += manifest.Stats.CompressedSize;
                    if (!fullOnly)
                    {
                        baseSize += manifest.Stats.CompressedSize;
                    }
                    if (!(isBase && fullOnly))
                    {
                        downloadSize += manifest.Stats.CompressedSize;
                        unzipSize += manifest.Stats.UncompressedSize;
                    }
                }
                foreach (string? lang in langs)
                {
                    if (_gameSophonChunkBuild.Manifests.FirstOrDefault(x => x.MatchingField == lang) is GameSophonChunkManifest audioManifest)
                    {
                        audioSize += audioManifest.Stats.CompressedSize;
                        unzipSize += audioManifest.Stats.UncompressedSize;
                    }
                }
                FullPackageSizeBytes = fullSize;
                BasePackageSizeBytes = baseSize;
            }
            downloadSize += audioSize;
            AudioPackageSizeBytes = audioSize;
            DownloadSizeBytes = downloadSize;
            UnzipSpaceBytes = unzipSize;
            if (AvailableSpaceBytes > 0 && UnzipSpaceBytes > AvailableSpaceBytes)
            {
                TextBlock_AvailableSpace.Foreground = App.Current.Resources["SystemFillColorCautionBrush"] as Brush;
            }
            else
            {
                TextBlock_AvailableSpace.Foreground = App.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compute package size.");
        }
    }



    private void CheckCanStartInstallation()
    {
        try
        {
            ErrorMessage = null;
            Button_StartInstallation.IsEnabled = false;
            if (_gamePackage is not null || _gameSophonChunkBuild is not null)
            {
                if (DriveHelper.GetDriveType(InstallationPath) is DriveType.Network && !new Uri(InstallationPath).IsUnc)
                {
                    ErrorMessage = Lang.InstallGameDialog_MappedNetworkDrivesAreNotSupportedPleaseUseANetworkSharePathStartingWithDoubleBackslashes;
                }
                else if (Path.GetPathRoot(InstallationPath) == InstallationPath)
                {
                    ErrorMessage = Lang.LauncherPage_PleaseDoNotSelectTheRootDirectoryOfADrive;
                }
                else if (Path.IsPathFullyQualified(InstallationPath))
                {
                    if (!(_needAudioPackage ^ Segmented_SelectLanguage.SelectedItems.Count > 0))
                    {
                        Button_StartInstallation.IsEnabled = true;
                    }
                }
            }
            else if (_gamePackageLoaded)
            {
                // 已在启动器中展示、但尚未发布安装包的新游戏（如星布谷地、崩坏：因缘精灵）无法安装
                ErrorMessage = Lang.GameLauncherSettingDialog_NoGamePackage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check can start installation.");
        }
    }



    [RelayCommand]
    private async Task ChangeInstallationPathAsync()
    {
        try
        {
            string? path = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (Directory.Exists(path))
            {
                _selectPath = path;
                if (AutomaticallyCreateSubfolderForInstall)
                {
                    path = Path.Combine(path, CurrentGameId.GameBiz);
                }
                SetInstallationPath(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change installation path.");
        }
    }



    private void SetInstallationPath(string path)
    {
        try
        {
            TextBlock_InstallationPath.FontSize = 14;
            InstallationPath = path;
            AvailableSpaceBytes = DriveHelper.GetDriveAvailableSpace(path);
            CheckCanStartInstallation();
            _ = RefreshHardLinkAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Set installation path.");
        }
    }



    private void Segmented_SelectLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _audioLanguage = AudioLanguage.None;
        foreach (SegmentedItem item in Segmented_SelectLanguage.SelectedItems.Cast<SegmentedItem>())
        {
            if (item.Tag is string lang)
            {
                _audioLanguage |= lang switch
                {
                    "zh-cn" => AudioLanguage.Chinese,
                    "en-us" => AudioLanguage.English,
                    "ja-jp" => AudioLanguage.Japanese,
                    "ko-kr" => AudioLanguage.Korean,
                    _ => AudioLanguage.None,
                };
            }
        }

        ComputePackageSize();
        CheckCanStartInstallation();
    }



    [RelayCommand]
    private async Task StartInstallationAsync()
    {
        try
        {
            Telemetry.Track("install_dialog_click", CurrentGameId.GameBiz,
                ("button", "install"),
                ("mode", _gameSophonChunkBuild is not null ? "chunk" : _gamePackage is not null ? "package" : null),
                ("audio", _audioLanguage),
                ("package_type", SelectedPackageType),
                ("disk_type", DriveHelper.GetDiskMediaType(InstallationPath)),
                ("drive_format", DriveHelper.GetDriveFormat(InstallationPath)),
                ("required_bytes", UnzipSpaceBytes),
                ("available_bytes", AvailableSpaceBytes),
                ("subfolder", AutomaticallyCreateSubfolderForInstall),
                ("hard_link", HardLinkChecked));
            // 按对话框显示的为准：不可用时传 false，不让后台重新查找时又链接上
            GameInstallContext? task = await _gameInstallService.StartInstallAsync(CurrentGameId, InstallationPath, _audioLanguage, SelectedPackageType, HardLinkChecked);
            if (task is not null && task.State is not GameInstallState.Stop and not GameInstallState.Error)
            {
                _installStarted = true;
                GameLauncherService.ChangeGameInstallPath(CurrentGameId, InstallationPath);
                WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
                if (_selectPath is not null && InstallationPath.EndsWith(CurrentGameId.GameBiz))
                {
                    AppConfig.DefaultGameInstallationPath = _selectPath;
                }
                Close();
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start installation.");
        }
    }




    /// <summary>
    /// 安装已发起；之后的关闭是程序自动关，不计入用户点击
    /// </summary>
    private bool _installStarted;


    /// <summary>
    /// 关闭对话框
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        if (!_installStarted)
        {
            Telemetry.Track("install_dialog_click", CurrentGameId?.GameBiz.ToString(), ("button", "close"));
        }
        this.Hide();
    }



    /// <summary>
    /// 用户对本次安装是否硬链接的选择，默认跟随设置里的硬链接开关；路径换到不能链接再换回来时保留
    /// </summary>
    private bool _useHardLink = AppConfig.EnableHardLink;


    /// <summary>
    /// 硬链接复选框：不可用时恒为未勾选，即本次安装不会硬链接
    /// </summary>
    public bool HardLinkChecked
    {
        get => HardLinkAvailable && _useHardLink;
        set
        {
            if (HardLinkAvailable && _useHardLink != value)
            {
                _useHardLink = value;
                OnPropertyChanged();
            }
        }
    }


    /// <summary>
    /// 当前路径能否硬链接到其他区服
    /// </summary>
    public bool HardLinkAvailable
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HardLinkChecked));
            }
        }
    }


    public string? HardLinkText { get; set => SetProperty(ref field, value); }


    public string? HardLinkTooltip { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 不能硬链接的原因，能硬链接时为 <see langword="null"/>
    /// </summary>
    public string? HardLinkReason { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 路径连续变化时只采用最后一次查找的结果
    /// </summary>
    private int _hardLinkRefreshVersion;


    /// <summary>
    /// 按当前安装路径刷新硬链接选项：同一 NTFS 磁盘上有同一游戏的其他区服时可勾选，否则置灰并写明原因
    /// </summary>
    /// <param name="installPath">当前安装路径</param>
    private async Task RefreshHardLinkAsync(string installPath)
    {
        if (CurrentGameId is null)
        {
            return;
        }
        int version = ++_hardLinkRefreshVersion;
        bool supported = GameFeatureConfig.FromGameId(CurrentGameId).SupportHardLink;
        (GameBiz GameBiz, string InstallPath)? target = null;
        string? reason = null;
        try
        {
            if (!supported)
            {
                reason = Lang.InstallGameDialog_HardLinkNotSupported;
            }
            else if (Path.IsPathFullyQualified(installPath))
            {
                target = await _gameInstallService.FindHardLinkTargetAsync(CurrentGameId, installPath);
                if (target is null)
                {
                    reason = GetHardLinkUnavailableReason();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Find hard link target.");
        }
        if (version != _hardLinkRefreshVersion)
        {
            return;
        }
        HardLinkAvailable = target is not null;
        if (target is { } t)
        {
            HardLinkText = string.Format(Lang.InstallGameDialog_HardLinkWithServer, t.GameBiz.ToGameServerName());
            HardLinkTooltip = $"{Lang.InstallGameDialog_HardLinkDesc}\n\n{Lang.InstallGameDialog_LinkTarget}{t.InstallPath}";
            HardLinkReason = null;
        }
        else
        {
            HardLinkText = Lang.InstallGameDialog_HardLinkWithOtherServers;
            HardLinkTooltip = Lang.InstallGameDialog_HardLinkDesc;
            HardLinkReason = reason;
        }
        // 复选框文字显式用了次要色，不会随禁用变灰，这里手动换成禁用色
        TextBlock_HardLink.Foreground = App.Current.Resources[target is null ? "TextFillColorDisabledBrush" : "TextFillColorSecondaryBrush"] as Brush;
        StackPanel_HardLink.Visibility = Visibility.Visible;
    }


    /// <summary>
    /// 支持硬链接但当前路径找不到可链接区服时的原因：没装其他区服、装在别的磁盘、所在磁盘不是 NTFS
    /// </summary>
    /// <returns>原因文案；找不到合适的说明时为 <see langword="null"/></returns>
    private string? GetHardLinkUnavailableReason()
    {
        string game = CurrentGameId.GameBiz.Game;
        List<(GameBiz GameBiz, string InstallPath)> others = new();
        foreach (string server in new[] { "cn", "bilibili", "global", })
        {
            string biz = $"{game}_{server}";
            if (CurrentGameId.GameBiz != biz && GameLauncherService.GetGameInstallPath(biz) is { } path)
            {
                others.Add((biz, path));
            }
        }
        if (others.Count == 0)
        {
            return string.Format(Lang.InstallGameDialog_HardLinkNoOtherServer, CurrentGameId.GameBiz.ToGameName());
        }
        // 有装在 NTFS 磁盘上的区服却没找到可链接目标，说明它在别的磁盘，提示换盘
        foreach ((GameBiz biz, string path) in others)
        {
            if (DriveHelper.GetDriveFormat(path) is "NTFS")
            {
                return string.Format(Lang.InstallGameDialog_HardLinkRequiresSameDrive, biz.ToGameServerName(), Path.GetPathRoot(path));
            }
        }
        (GameBiz first, string firstPath) = others[0];
        return string.Format(Lang.InstallGameDialog_HardLinkNotNtfs, first.ToGameServerName(), Path.GetPathRoot(firstPath));
    }



    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize--;
        }
    }



}
