using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.Setting;
using Starward.Features.Overlay;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;


namespace Starward.Features.CloudGame;

[INotifyPropertyChanged]
public sealed partial class CloudGameButton : UserControl
{



    private readonly ILogger<CloudGameButton> _logger = AppConfig.GetLogger<CloudGameButton>();

    private readonly CloudGameWalletService _walletService = AppConfig.GetService<CloudGameWalletService>();

    private readonly AutoCloudGameFreeTimeService _autoFreeTimeService = AppConfig.GetService<AutoCloudGameFreeTimeService>();



    public CloudGameButton()
    {
        this.InitializeComponent();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }


    /// <summary>
    /// 语言切换后刷新 x:Bind 绑定，并按新语言重排代码里格式化的时长与错误文案。
    /// </summary>
    /// <param name="_">消息发送方（未使用）。</param>
    /// <param name="__">语言变更消息（未使用）。</param>
    private void OnLanguageChanged(object _, LanguageChangedMessage __)
    {
        CoinTimeLabel = GetCoinTimeLabel(CurrentGameId?.GameBiz ?? default);
        this.Bindings.Update();
        if (_walletState is WalletState.Value && _wallet is not null)
        {
            ShowWallet(_wallet);
        }
        else if (_walletState is WalletState.Error && _walletException is not null)
        {
            ShowWalletError(_walletException);
        }
    }



    public GameId CurrentGameId
    {
        get; set
        {
            field = value;
            IsWalletSupported = GameFeatureConfig.FromGameId(value).SupportCloudGameWallet;
            CoinTimeLabel = GetCoinTimeLabel(value?.GameBiz ?? default);
            // 切游戏后开关要重新读：它按游戏存，不能沿用上一个游戏的值
            OnPropertyChanged(nameof(AutoClaimFreeTimeEnabled));
        }
    }



    public string? ExePath { get; set => SetProperty(ref field, value); }


    public string? RunningProcessInfo { get; set => SetProperty(ref field, value); }



    #region Wallet


    /// <summary>弹层「可用时长」区的显示状态，内容区四选一。</summary>
    private enum WalletState
    {
        /// <summary>本机没有可用凭证，显示「先装客户端并登录一次」的引导。</summary>
        NoAccount,
        /// <summary>首次查询中（没有可先显示的结果）。</summary>
        Loading,
        /// <summary>显示时长。</summary>
        Value,
        /// <summary>查询失败。</summary>
        Error,
    }


    private WalletState _walletState;

    /// <summary>正在显示的时长，语言切换时重新格式化。</summary>
    private CloudGameWalletSummary? _wallet;

    /// <summary>最近一次查询失败的异常，语言切换时重新生成提示。</summary>
    private Exception? _walletException;


    /// <summary>当前区服是否支持查询云游戏可用时长。</summary>
    public bool IsWalletSupported { get; private set => SetProperty(ref field, value); }

    /// <summary>是否正在查询，查询中禁用刷新按钮。</summary>
    public bool IsWalletLoading { get; private set => SetProperty(ref field, value); }

    /// <summary>免费时长文案。</summary>
    public string? FreeTimeText { get; private set => SetProperty(ref field, value); }

    /// <summary>付费货币时长文案（云·绝区零为邦邦点时长）。</summary>
    public string? CoinTimeText { get; private set => SetProperty(ref field, value); }

    /// <summary>畅玩卡文案：生效中显示剩余时长，否则显示服务端给的状态（未开通 / 已过期）。</summary>
    public string? PlayCardText { get; private set => SetProperty(ref field, value); }

    /// <summary>查询失败的提示。</summary>
    public string? WalletError { get; private set => SetProperty(ref field, value); }

    public bool IsWalletValueVisible => _walletState is WalletState.Value;

    public bool IsWalletHintVisible => _walletState is WalletState.NoAccount;

    public bool IsWalletErrorVisible => _walletState is WalletState.Error;

    public bool IsWalletRingVisible => _walletState is WalletState.Loading;


    /// <summary>
    /// 切换内容区状态，并通知依赖状态的绑定属性。
    /// </summary>
    /// <param name="state">新状态。</param>
    private void SetWalletState(WalletState state)
    {
        _walletState = state;
        OnPropertyChanged(nameof(IsWalletValueVisible));
        OnPropertyChanged(nameof(IsWalletHintVisible));
        OnPropertyChanged(nameof(IsWalletErrorVisible));
        OnPropertyChanged(nameof(IsWalletRingVisible));
        OnPropertyChanged(nameof(IsAutoClaimVisible));
        OnPropertyChanged(nameof(IsAccountRowVisible));
    }


    /// <summary>
    /// 弹层打开前同步可用时长区：在 Opening 里定好状态，避免打开后第一帧闪过其他状态。
    /// </summary>
    private void Flyout_Opening(object sender, object e)
    {
        _ = LoadWalletAsync(force: false);
    }


    /// <summary>
    /// 手动刷新可用时长。
    /// </summary>
    [RelayCommand]
    private async Task RefreshWalletAsync()
    {
        await LoadWalletAsync(force: true);
    }


    /// <summary>
    /// 同步可用时长区：先列出云游戏客户端里登过的通行证，再查选中账号的钱包。
    /// 没有任何凭证来源时显示引导（请先装客户端并登录一次）。
    /// </summary>
    /// <param name="force">是否忽略缓存有效期。</param>
    private async Task LoadWalletAsync(bool force)
    {
        if (!IsWalletSupported || CurrentGameId is null)
        {
            return;
        }
        GameBiz biz = CurrentGameId.GameBiz;

        // 状态要在第一个 await 之前定好：弹层此时已在渲染，晚一步就会先闪一帧上次的状态。
        // 这里只能做同步判断（只看日志文件在不在、本机存没存过凭证），真正的账号列表要读盘解析
        if (!CloudGameWalletService.HasAnyCredentialSource(biz))
        {
            ShowNoAccount();
            return;
        }
        if (IsWalletLoading)
        {
            return;
        }
        if (_wallet is null)
        {
            // 已有上次结果就继续显示，只有拿不出任何数值时才转圈
            SetWalletState(WalletState.Loading);
        }

        IsWalletLoading = true;
        try
        {
            List<CloudGameAccount> accounts = await _walletService.GetAccountsAsync(biz);
            CloudGameAccount? account = CloudGameWalletService.ResolveSelectedAccount(biz, accounts);
            UpdateAccounts(accounts, account);
            if (account is null)
            {
                ShowNoAccount();
                return;
            }

            CloudGameWalletSummary? summary = await _walletService.GetWalletAsync(biz, account.AccountId, force);
            if (summary is null)
            {
                ShowNoAccount();
            }
            else
            {
                ShowWallet(summary);
            }
        }
        catch (Exception ex)
        {
            ShowWalletError(ex);
            _logger.LogWarning(ex, "Get cloud game wallet failed ({GameBiz}).", biz);
        }
        finally
        {
            IsWalletLoading = false;
        }
    }


    /// <summary>
    /// 把账号列表反映到下拉按钮与它的菜单上。
    /// </summary>
    /// <param name="accounts">可选账号。</param>
    /// <param name="selected">当前选中的账号；列表为空时为 null。</param>
    private void UpdateAccounts(List<CloudGameAccount> accounts, CloudGameAccount? selected)
    {
        _accounts = accounts;
        _selectedAccountId = selected?.AccountId;
        SelectedAccountText = selected?.DisplayName;
        OnPropertyChanged(nameof(IsAccountRowVisible));
        OnPropertyChanged(nameof(IsAccountSwitchEnabled));

        MenuFlyout_Account.Items.Clear();
        if (accounts.Count < 2)
        {
            // 只有一个账号时按钮不可点，菜单建了也打不开
            return;
        }
        foreach (CloudGameAccount account in accounts)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = account.DisplayName,
                IsChecked = account.AccountId == selected?.AccountId,
                Tag = account.AccountId,
            };
            item.Click += AccountMenuItem_Click;
            MenuFlyout_Account.Items.Add(item);
        }
    }


    /// <summary>
    /// 切换通行证：记住选择并重查。缓存按账号分开存，来回切不会反复请求。
    /// </summary>
    private void AccountMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string accountId } || CurrentGameId is null)
        {
            return;
        }
        if (accountId == _selectedAccountId)
        {
            return;
        }
        AppConfig.SetCloudGameSelectedAccount(CurrentGameId.GameBiz, accountId);
        // 换了账号，旧数值不再代表当前选中的人，先清掉再查
        _wallet = null;
        _walletException = null;
        _ = LoadWalletAsync(force: false);
    }


    /// <summary>
    /// 显示引导：本机拿不到任何云游戏凭证（没装云游戏客户端，或装了但从没登录过）。
    /// </summary>
    private void ShowNoAccount()
    {
        _wallet = null;
        _walletException = null;
        _accounts = [];
        _selectedAccountId = null;
        SelectedAccountText = null;
        SetWalletState(WalletState.NoAccount);
    }


    /// <summary>
    /// 显示时长。
    /// </summary>
    /// <param name="summary">查询结果。</param>
    private void ShowWallet(CloudGameWalletSummary summary)
    {
        _wallet = summary;
        _walletException = null;
        FreeTimeText = FormatDuration(summary.FreeMinutes);
        CoinTimeText = FormatDuration(summary.CoinMinutes);
        PlayCardText = FormatPlayCard(summary.PlayCardRemainingSeconds, summary.PlayCardStatus);
        SetWalletState(WalletState.Value);
    }


    /// <summary>
    /// 显示查询失败的提示，文案统一经 <see cref="MiHoYoApiErrorFeedbackFactory"/> 映射。
    /// </summary>
    /// <param name="exception">查询时捕获的异常。</param>
    private void ShowWalletError(Exception exception)
    {
        _wallet = null;
        _walletException = exception;
        WalletError = MiHoYoApiErrorFeedbackFactory.Create(exception, MiHoYoApiContext.CloudGame).Message;
        SetWalletState(WalletState.Error);
    }


    /// <summary>当前区服可选的通行证账号。</summary>
    private List<CloudGameAccount> _accounts = [];

    /// <summary>当前选中的账号 ID。单独存一份，不拿显示文本反查（昵称可重复）。</summary>
    private string? _selectedAccountId;


    /// <summary>下拉按钮上显示的当前账号（有昵称则「昵称 (ID)」，否则只显示 ID）。</summary>
    public string? SelectedAccountText { get; private set => SetProperty(ref field, value); }


    /// <summary>
    /// 有可用账号时才显示账号行（含刷新按钮）；只有一个账号也显示，让用户看得出数字属于哪个 uid。
    /// </summary>
    public bool IsAccountRowVisible => _walletState is not WalletState.NoAccount && _accounts.Count > 0;


    /// <summary>超过一个账号才能切换。</summary>
    public bool IsAccountSwitchEnabled => _accounts.Count > 1;


    /// <summary>
    /// 付费货币时长的行标题，按游戏不同（云·原神为原点、云·绝区零为邦邦点）。
    /// </summary>
    public string? CoinTimeLabel { get; private set => SetProperty(ref field, value); }


    /// <summary>
    /// 是否显示自动获取免费时长开关。没有已登录账号时藏起来：
    /// 开了也领不到，此时内容区显示的是登录引导。
    /// </summary>
    public bool IsAutoClaimVisible => _walletState is not WalletState.NoAccount;


    /// <summary>
    /// 当前游戏的自动获取免费时长开关，双向绑定 ToggleSwitch。
    /// </summary>
    public bool AutoClaimFreeTimeEnabled
    {
        get => CurrentGameId is not null && _autoFreeTimeService.IsEnabled(CurrentGameId.GameBiz);
        set
        {
            if (CurrentGameId is null || value == _autoFreeTimeService.IsEnabled(CurrentGameId.GameBiz))
            {
                return;
            }
            _autoFreeTimeService.SetEnabled(CurrentGameId.GameBiz, value);
            OnPropertyChanged();
            if (value)
            {
                // 常驻循环可能已经睡到下一个日界，必须叫它立刻查一轮，否则开关看上去不生效
                _autoFreeTimeService.RequestImmediateCheck();
            }
        }
    }


    /// <summary>
    /// 按游戏取付费货币时长的行标题。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>本地化行标题。未接入钱包的游戏整个区域都不显示，回落到哪个文案都无所谓。</returns>
    private static string GetCoinTimeLabel(GameBiz biz)
    {
        return biz.Game switch
        {
            GameBiz.hk4e => Lang.CloudGameButton_YuanPointsTime,
            _ => Lang.CloudGameButton_BangbooPointsTime,
        };
    }


    /// <summary>
    /// 分钟数格式化为「X 小时 Y 分钟」：整小时省略分钟，不足 1 小时只显示分钟，负数（欠费）前加负号。
    /// </summary>
    /// <param name="minutes">分钟数。</param>
    /// <returns>本地化的时长文案。</returns>
    private static string FormatDuration(int minutes)
    {
        string sign = minutes < 0 ? "-" : "";
        int total = Math.Abs(minutes);
        int hours = total / 60;
        int rest = total % 60;
        string text = hours == 0 ? string.Format(Lang.CloudGameButton_Minutes, rest)
            : rest == 0 ? string.Format(Lang.CloudGameButton_Hours, hours)
            : string.Format(Lang.CloudGameButton_HoursMinutes, hours, rest);
        return sign + text;
    }


    /// <summary>
    /// 畅玩卡文案：生效中按剩余秒数组装「X 天 Y 小时」（整天省略小时，不足一天只显示小时或分钟）；
    /// 算不出剩余时长时退回服务端下发的状态文案，它能区分「未开通」与「已过期」，都没有才用本地化的兜底文案。
    /// </summary>
    /// <param name="seconds">剩余生效秒数，未开通或已过期为 0。</param>
    /// <param name="status">服务端下发的状态短文案，可能为 null。</param>
    /// <returns>畅玩卡文案。</returns>
    private static string FormatPlayCard(int seconds, string? status)
    {
        if (seconds <= 0)
        {
            return string.IsNullOrWhiteSpace(status) ? Lang.CloudGameButton_PlayCardInactive : status;
        }
        int days = seconds / 86400;
        int hours = seconds % 86400 / 3600;
        if (days > 0)
        {
            return hours == 0 ? string.Format(Lang.CloudGameButton_Days, days)
                : string.Format(Lang.CloudGameButton_DaysHours, days, hours);
        }
        if (hours > 0)
        {
            return string.Format(Lang.CloudGameButton_Hours, hours);
        }
        // 剩不到一分钟时卡仍然生效，按 1 分钟显示，别让它看起来像没开通
        return string.Format(Lang.CloudGameButton_Minutes, Math.Max(1, seconds / 60));
    }


    #endregion



    private void Flyout_Opened(object sender, object e)
    {
        try
        {
            string key = "";
            RunningProcessInfo = null;
            if (CurrentGameId?.GameBiz == GameBiz.hk4e_cn)
            {
                string? exe = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\GenshinImpactCloudGame", "DisplayIcon", null) as string;
                exe = exe?.Trim('"');
                if (File.Exists(exe))
                {
                    ExePath = exe;
                    string exeName = Path.GetFileName(exe);
                    int session = Process.GetCurrentProcess().SessionId;
                    Process? p = Process.GetProcessesByName(exeName.Replace(".exe", "")).FirstOrDefault(x => x.SessionId == session);
                    if (p is not null)
                    {
                        RunningProcessInfo = $"{Lang.LauncherPage_GameIsRunning}\n{exeName} ({p.Id})";
                    }
                }
            }
            else if (CurrentGameId?.GameBiz == GameBiz.hk4e_global)
            {
                key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Genshin Impact Cloud";
                string? folder = Registry.GetValue(key, "InstallPath", null) as string;
                string? exeName = Registry.GetValue(key, "ExeName", null) as string;
                string? path = Path.Join(folder?.Trim('"'), exeName?.Trim('"'));
                if (File.Exists(path))
                {
                    ExePath = path;
                    int session = Process.GetCurrentProcess().SessionId;
                    Process? p = Process.GetProcessesByName(exeName!.Replace(".exe", "")).FirstOrDefault(x => x.SessionId == session);
                    if (p is not null)
                    {
                        RunningProcessInfo = $"{Lang.LauncherPage_GameIsRunning}\n{exeName} ({p.Id})";
                    }
                }
            }
            else if (CurrentGameId?.GameBiz == GameBiz.nap_cn)
            {
                string? exe = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\ZenlessZoneZeroCloud", "DisplayIcon", null) as string;
                exe = exe?.Trim('"');
                if (File.Exists(exe))
                {
                    ExePath = exe;
                    string exeName = Path.GetFileName(exe);
                    int session = Process.GetCurrentProcess().SessionId;
                    Process? p = Process.GetProcessesByName(exeName.Replace(".exe", "")).FirstOrDefault(x => x.SessionId == session);
                    if (p is not null)
                    {
                        RunningProcessInfo = $"{Lang.LauncherPage_GameIsRunning}\n{exeName} ({p.Id})";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get cloud game state {GameBiz}", CurrentGameId?.GameBiz);
        }
    }










    [RelayCommand]
    private async Task StartGameAsync()
    {
        try
        {
            if (File.Exists(ExePath))
            {
                Process? p = Process.Start(new ProcessStartInfo
                {
                    FileName = ExePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(ExePath),
                });
                if (p is not null)
                {
                    RunningGameService.AddRuninngGame(CurrentGameId.GameBiz, p);
                    WeakReferenceMessenger.Default.Send(new GameStartedMessage(CurrentGameId.GameBiz));
                    await Task.Delay(3000);
                    if (!p.HasExited)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = AppConfig.MoonwardExecutePath,
                            Arguments = $"playtime --biz {CurrentGameId.GameBiz} --pid {p.Id}",
                            CreateNoWindow = true,
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start cloud game {GameBiz}", CurrentGameId?.GameBiz);
        }
    }



    [RelayCommand]
    private async Task InstallGameAsync()
    {
        try
        {
            string url = "";
            if (CurrentGameId?.GameBiz == GameBiz.hk4e_cn)
            {
                url = "https://ys.mihoyo.com/cloud/#/download";
            }
            else if (CurrentGameId?.GameBiz == GameBiz.hk4e_global)
            {
                url = "https://cloudgenshin.hoyoverse.com/";
            }
            else if (CurrentGameId?.GameBiz == GameBiz.nap_cn)
            {
                url = "https://zzz.mihoyo.com/cloud-feat/";
            }
            if (!string.IsNullOrWhiteSpace(url))
            {
                await Launcher.LaunchUriAsync(new Uri(url));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install cloud game {GameBiz}", CurrentGameId?.GameBiz);
        }
    }


}
