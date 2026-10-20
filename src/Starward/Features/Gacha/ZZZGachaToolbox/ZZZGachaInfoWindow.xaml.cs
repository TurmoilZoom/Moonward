using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Octokit;
using Starward.Core;
using Starward.Core.Gacha.ZZZ;
using Starward.Features.Database;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;


namespace Starward.Features.Gacha.ZZZGachaToolbox;

[INotifyPropertyChanged]
public sealed partial class ZZZGachaInfoWindow : WindowEx
{

    private readonly ILogger<ZZZGachaInfoWindow> _logger = AppConfig.GetLogger<ZZZGachaInfoWindow>();


    private readonly HttpClient _httpClient = AppConfig.GetService<HttpClient>();


    private readonly ZZZGachaMetadataPublishService _metadataPublishService = AppConfig.GetService<ZZZGachaMetadataPublishService>();


    private readonly ZZZGachaMetadataAutoPublishService _autoPublishService = AppConfig.GetService<ZZZGachaMetadataAutoPublishService>();


    private const string Source_cn = "https://act.mihoyo.com/zzz/gt/character-builder-h/index.html";
    private const string Source_global = "https://act.hoyolab.com/zzz/gt/character-builder-h/index.html";


    private bool _webviewClosed;


    public ZZZGachaInfoWindow()
    {
        this.InitializeComponent();
        InitializeWindow();
        Closed += (_, _) => CloseWebView();
    }



    private void InitializeWindow()
    {
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        Title = Lang.ToolboxSetting_ZZZGachaItemImages;
        RootGrid.RequestedTheme = ShouldAppsUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AdaptTitleBarButtonColorToActuallTheme();
        SetIcon();

        string browseUrl = ZZZGachaMetadataPaths.GitHubBrowseUrl;
        Hyperlink_MetadataRepo.NavigateUri = new Uri(browseUrl);
        Run_MetadataRepoUrl.Text = browseUrl;
    }


    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshGameRecordFetchAvailability();
            RefreshPublishButtons();
            await webview2.EnsureCoreWebView2Async();
            if (WebView2Helper.CloseIfRequested(webview2, _webviewClosed)) return;
            coreWebView2 = webview2.CoreWebView2;
            coreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            coreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
            coreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZZZGachaInfoWindow: EnsureCoreWebView2");
        }
    }


    private void RootGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        RootGrid.Unloaded -= RootGrid_Unloaded;
        GridView_Languages.SelectionChanged -= GridView_Languages_SelectionChanged;
        CloseWebView();
        cts.Cancel();
        GachaInfoResult.Clear();
        GachaInfoResult = null!;
        gachaInfoDict = null!;
        iconInfoDict = null!;
        itemListDict = null!;
        headers = null!;
    }


    private void CloseWebView()
    {
        _webviewClosed = true;
        if (coreWebView2 is not null)
        {
            try { coreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived; } catch { }
            coreWebView2 = null!;
        }
        WebView2Helper.Close(webview2);
    }



    private CancellationTokenSource cts = new();


    private CoreWebView2 coreWebView2;


    private string? url;

    private List<KeyValuePair<string, string>> headers;



    public ObservableCollection<string> GachaInfoResult { get; set => SetProperty(ref field, value); } = new();


    /// <summary>状态栏文案（进度 / 成功 / 错误提示）。</summary>
    public string StatusMessage { get; set => SetProperty(ref field, value); } = "";


    private bool isFetchingFromGameRecord;


    private Dictionary<string, List<ZZZGachaInfo>> gachaInfoDict = new();


    private Dictionary<string, IconInfo> iconInfoDict = new();


    private Dictionary<string, ItemList> itemListDict = new();



    /// <summary>
    /// 根据本地是否已有带 Cookie 的绝区零战绩角色，刷新「用战绩账号拉取」按钮可用性。
    /// </summary>
    private void RefreshGameRecordFetchAvailability()
    {
        try
        {
            bool hasCookie = _metadataPublishService.PickZZZGameRecordRolesWithCookie().Count > 0;
            Button_FetchWithGameRecord.IsEnabled = hasCookie && !isFetchingFromGameRecord;
            if (!hasCookie && string.IsNullOrWhiteSpace(StatusMessage))
            {
                StatusMessage = Lang.GachaLogPage_PleaseLoginMiyousheAndAddZZZRole;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZZZGachaInfoWindow: RefreshGameRecordFetchAvailability");
        }
    }


    /// <summary>
    /// 使用本地战绩 Cookie 调用养成指南接口（badge → item_list + icon_info）。
    /// 国服写 nap_cn.zh-cn；国际服写 nap_global 下全部 UI 语言。
    /// </summary>
    [RelayCommand]
    private async Task FetchFromGameRecordAsync()
    {
        if (isFetchingFromGameRecord)
        {
            return;
        }

        try
        {
            isFetchingFromGameRecord = true;
            Button_FetchWithGameRecord.IsEnabled = false;

            List<ZZZGachaMetadataFetchRequest> plan = _metadataPublishService.GetFetchPlan();
            if (plan.Count == 0)
            {
                StatusMessage = Lang.GachaLogPage_PleaseLoginMiyousheAndAddZZZRole;
                return;
            }

            int languageCount = 0;
            int itemCount = 0;

            foreach (ZZZGachaMetadataFetchRequest request in plan)
            {
                cts.Token.ThrowIfCancellationRequested();
                StatusMessage = string.Format(Lang.GachaLogPage_FetchingItemInfoLanguage, request.Key);

                (string key, List<ZZZGachaInfo>? list) = await _metadataPublishService.FetchPackageAsync(request, cts.Token).ConfigureAwait(true);
                if (list is null)
                {
                    continue;
                }

                AddOrUpdateGachaInfoResult(key, list);
                languageCount++;
                itemCount = Math.Max(itemCount, list.Count);

                // 全语言循环时轻微限流，降低 act 接口风控概率
                if (request.IsGlobal)
                {
                    await Task.Delay(Random.Shared.Next(200, 300), cts.Token).ConfigureAwait(true);
                }
            }

            if (languageCount == 0)
            {
                StatusMessage = Lang.GachaLogPage_UpdateIconsNoItemData;
            }
            else
            {
                StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_FetchSucceeded, languageCount, itemCount);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Lang.GachaLogPage_OperationCanceled;
        }
        catch (miHoYoApiException ex) when (ZZZGachaMetadataPublishService.IsCultivateToolRiskControl(ex.ReturnCode))
        {
            // 10035 等是 act 极验风控，与战绩页「验证账号」无关；可改用网页登录抓包
            _logger.LogWarning(ex, "ZZZ cultivate tool risk control ({retcode}) in toolbox", ex.ReturnCode);
            StatusMessage = string.Format(Lang.GachaLogPage_UpdateIconsRiskControl, ex.ReturnCode);
        }
        catch (miHoYoApiException ex)
        {
            // 勿用 ex.Message 再拼 ReturnCode：Message 构造时已是「原文 (retcode)」
            _logger.LogWarning(ex, "Fetch ZZZ gacha info from game record ({retcode})", ex.ReturnCode);
            StatusMessage = string.IsNullOrWhiteSpace(ex.ResponseMessage)
                ? $"retcode={ex.ReturnCode}"
                : $"{ex.ResponseMessage} ({ex.ReturnCode})";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch ZZZ gacha info from game record");
            StatusMessage = ex.Message;
        }
        finally
        {
            isFetchingFromGameRecord = false;
            RefreshGameRecordFetchAvailability();
        }
    }


    /// <summary>
    /// 写入或覆盖某语言包，并刷新列表 UI（保证在 UI 线程修改 ObservableCollection）。
    /// </summary>
    private void AddOrUpdateGachaInfoResult(string key, List<ZZZGachaInfo> list)
    {
        void Apply()
        {
            gachaInfoDict[key] = list;
            GachaInfoResult.Remove(key);
            GachaInfoResult.Add(key);
            // 列表有数据后启用「全选」
            Button_SelectAll.IsEnabled = GachaInfoResult.Count > 0;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Apply);
        }
    }



    [RelayCommand]
    private void NavigateToMiyoushe()
    {
        try
        {
            webview2.Visibility = Visibility.Visible;
            webview2.CoreWebView2.Navigate(Source_cn);
            Button_GetAllLanguages.IsEnabled = false;
        }
        catch { }
    }



    [RelayCommand]
    private void NavigateToHoYoLAB()
    {
        try
        {
            webview2.Visibility = Visibility.Visible;
            webview2.CoreWebView2.Navigate(Source_global);
            Button_GetAllLanguages.IsEnabled = false;
        }
        catch { }
    }


    private async void CoreWebView2_WebResourceResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        try
        {
            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out Uri? uri))
            {
                if (uri.AbsolutePath.Contains("/icon_info") && args.Response.StatusCode == 200)
                {
                    url = uri.OriginalString;
                    headers = args.Request.Headers.ToList();

                    string biz = "";
                    string lang = "";
                    if (uri.OriginalString.Contains("mihoyo.com"))
                    {
                        biz = GameBiz.nap_cn;
                        lang = "zh-cn";
                        Button_GetAllLanguages.IsEnabled = false;
                    }
                    if (uri.OriginalString.Contains("hoyolab.com"))
                    {
                        biz = GameBiz.nap_global;
                        string cookie = args.Request.Headers.GetHeader("Cookie");
                        lang = Regex.Match(cookie, @"mi18nLang=([^;]+);?").Groups[1].Value;
                        Button_GetAllLanguages.IsEnabled = true;
                    }
                    if (string.IsNullOrWhiteSpace(biz) || string.IsNullOrWhiteSpace(lang))
                    {
                        return;
                    }
                    string key = $"{biz}.{lang}";
                    await AddIconInfoAndGetItemListAsync(key, args);
                    UpdateGachaInfo(key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZZZGachaInfoWindow: CoreWebView2_WebResourceResponseReceived");
        }
    }



    private async Task AddIconInfoAndGetItemListAsync(string key, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        try
        {

            var stream = await args.Response.GetContentAsync();
            if (stream is not null)
            {
                var wrapper = await JsonSerializer.DeserializeAsync<miHoYoApiWrapper<IconInfo>>(stream.AsStream(), cancellationToken: cts.Token);
                if (wrapper is not null && wrapper.Data is not null)
                {
                    iconInfoDict.TryAdd(key, wrapper.Data);
                }
            }
            {
                string url = args.Request.Uri.Replace("/icon_info", "/item_list") + "&avatar_id=1011";
                var headers = args.Request.Headers.ToList();
                var request = new HttpRequestMessage(HttpMethod.Get, url) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
                var response = await _httpClient.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();
                var wrapper = await response.Content.ReadFromJsonAsync<miHoYoApiWrapper<ItemList>>(cts.Token);
                if (wrapper is not null && wrapper.Data is not null)
                {
                    itemListDict.TryAdd(key, wrapper.Data);
                }
            }

        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get ZZZGachaInfo for specific langugae");
        }
    }



    [RelayCommand]
    private async Task GetAllLanguagesInfoAsync()
    {
        try
        {
            if (url?.Contains("hoyolab.com") ?? false && headers is not null)
            {
                foreach (var lang in LanguageUtil.GetAllLanguages())
                {
                    string key = $"nap_global.{lang}";
                    if (gachaInfoDict.ContainsKey(key))
                    {
                        continue;
                    }
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, url) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
                        foreach (var header in headers)
                        {
                            string value = header.Value;
                            if (header.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                            {
                                value = Regex.Replace(value, @"mi18nLang=[^;]+", $"mi18nLang={lang}");
                            }
                            if (header.Key.Equals("x-rpc-lang", StringComparison.OrdinalIgnoreCase))
                            {
                                value = lang;
                            }
                            request.Headers.Add(header.Key, value);
                        }
                        var response = await _httpClient.SendAsync(request, cts.Token);
                        response.EnsureSuccessStatusCode();
                        var wrapper = await response.Content.ReadFromJsonAsync<miHoYoApiWrapper<IconInfo>>(cts.Token);
                        if (wrapper is not null && wrapper.Data is not null)
                        {
                            iconInfoDict.TryAdd(key, wrapper.Data);
                        }
                    }
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, url.Replace("/icon_info", "/item_list") + "&avatar_id=1011") { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
                        foreach (var header in headers)
                        {
                            string value = header.Value;
                            if (header.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                            {
                                value = Regex.Replace(value, @"mi18nLang=[^;]+", $"mi18nLang={lang}");
                            }
                            if (header.Key.Equals("x-rpc-lang", StringComparison.OrdinalIgnoreCase))
                            {
                                value = lang;
                            }
                            request.Headers.Add(header.Key, value);
                        }
                        var response = await _httpClient.SendAsync(request, cts.Token);
                        response.EnsureSuccessStatusCode();
                        var wrapper = await response.Content.ReadFromJsonAsync<miHoYoApiWrapper<ItemList>>(cts.Token);
                        if (wrapper is not null && wrapper.Data is not null)
                        {
                            itemListDict.TryAdd(key, wrapper.Data);
                        }
                    }
                    UpdateGachaInfo(key);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get ZZZGachaInfo for all languages");
        }
    }



    private void UpdateGachaInfo(string key)
    {
        try
        {
            var iconInfo = iconInfoDict.GetValueOrDefault(key);
            var itemList = itemListDict.GetValueOrDefault(key);
            if (iconInfo is not null && itemList is not null)
            {
                var list = new List<ZZZGachaInfo>();
                foreach (var item in itemList.Avatars)
                {
                    var info = new ZZZGachaInfo
                    {
                        Id = item.Id,
                        Name = item.NameMi18n,
                        Rarity = item.Rarity switch
                        {
                            "S" or "s" => 4,
                            "A" or "a" => 3,
                            "B" or "b" => 2,
                            _ => 0,
                        },
                        ElementType = item.ElementType,
                        Profession = item.AvatarProfession,
                    };
                    info.Icon = iconInfo.AvatarIcons.GetValueOrDefault(item.Id.ToString())?.SquareAvatar ?? "";
                    list.Add(info);
                }
                foreach (var item in itemList.Weapons)
                {
                    var info = new ZZZGachaInfo
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Icon = item.Icon,
                        Rarity = item.Rarity switch
                        {
                            "S" or "s" => 4,
                            "A" or "a" => 3,
                            "B" or "b" => 2,
                            _ => 0,
                        },
                        Profession = item.Profession,
                    };
                    list.Add(info);
                }
                foreach (var item in itemList.Buddies)
                {
                    var info = new ZZZGachaInfo
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Rarity = item.Rarity switch
                        {
                            "S" or "s" => 4,
                            "A" or "a" => 3,
                            "B" or "b" => 2,
                            _ => 0,
                        },
                    };
                    info.Icon = iconInfo.BuddyIcons.GetValueOrDefault(item.Id.ToString())?.SquareAvatar ?? "";
                    list.Add(info);
                }

                AddOrUpdateGachaInfoResult(key, list);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZZZGachaWindows: Update GachaInfo");
        }
    }



    private void GridView_Languages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            RefreshPublishButtons();
        }
        catch { }
    }


    /// <summary>
    /// 按选中项与 PAT 状态刷新「全选 / 保存 / 导出 / 提交 metadata」按钮。
    /// </summary>
    private void RefreshPublishButtons()
    {
        int total = GachaInfoResult?.Count ?? 0;
        int selected = GridView_Languages.SelectedItems.Count;
        Button_SelectAll.IsEnabled = total > 0;
        Button_SaveToDatabase.IsEnabled = selected == 1;
        Button_ExportFiles.IsEnabled = selected > 0;
        // 维护者提交：需已存 PAT 且至少选中一个语言包
        bool hasPat = _metadataPublishService.HasStoredPat;
        Button_PublishMetadata.IsEnabled = selected > 0 && hasPat && !isPublishingMetadata;
        // 定时提交只对已存 PAT 的维护者显示，开启时再校验写入权限
        Button_AutoPublish.Visibility = hasPat ? Visibility.Visible : Visibility.Collapsed;
        Button_AutoPublish.IsEnabled = !isPublishingMetadata;
    }


    /// <summary>
    /// 全选 / 取消全选：已全部选中时清空选择，否则勾选全部语言包（便于导出 / 提交 metadata）。
    /// </summary>
    [RelayCommand]
    private void SelectAllLanguages()
    {
        try
        {
            if (GachaInfoResult is null || GachaInfoResult.Count == 0)
            {
                return;
            }

            // Multiple 模式：已全选则清空，否则 SelectAll
            if (GridView_Languages.SelectedItems.Count == GachaInfoResult.Count)
            {
                GridView_Languages.SelectedItems.Clear();
            }
            else
            {
                GridView_Languages.SelectAll();
            }
            RefreshPublishButtons();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZZZGachaInfoWindow: SelectAllLanguages");
        }
    }


    private bool isPublishingMetadata;


    /// <summary>
    /// 维护者：配置 / 清除 / 校验 GitHub PAT（PasswordVault 加密存储）。
    /// </summary>
    [RelayCommand]
    private async Task ManageGitHubPatAsync()
    {
        try
        {
            var passwordBox = new PasswordBox
            {
                PlaceholderText = Lang.ZZZGachaInfoWindow_GitHubPatPlaceholder,
                Width = 360,
            };
            var statusText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Text = _metadataPublishService.HasStoredPat
                    ? Lang.ZZZGachaInfoWindow_GitHubPatConfigured
                    : Lang.ZZZGachaInfoWindow_GitHubPatNotConfigured,
            };
            var panel = new StackPanel { Spacing = 12, Width = 360 };
            panel.Children.Add(new TextBlock
            {
                Text = Lang.ZZZGachaInfoWindow_GitHubPatDialogDescription,
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(passwordBox);
            panel.Children.Add(statusText);

            var dialog = new ContentDialog
            {
                Title = Lang.ZZZGachaInfoWindow_ManageGitHubPat,
                Content = panel,
                PrimaryButtonText = Lang.ZZZGachaInfoWindow_SavePat,
                SecondaryButtonText = Lang.ZZZGachaInfoWindow_ClearPat,
                CloseButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result is ContentDialogResult.Primary)
            {
                string pat = passwordBox.Password?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(pat))
                {
                    StatusMessage = Lang.ZZZGachaInfoWindow_GitHubPatEmpty;
                    return;
                }

                _metadataPublishService.SavePat(pat);
                // 校验连通性；失败则清除以免留下无效密钥
                try
                {
                    string login = await _metadataPublishService.ValidatePatAsync(cts.Token).ConfigureAwait(true);
                    StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_GitHubPatValidated, login);
                    // 换了新 PAT：定时提交之前因 PAT 失败排到 2 小时后的重试作废，按排期重新判断
                    _autoPublishService.RequestImmediateCheck();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GitHub PAT validation failed");
                    _metadataPublishService.ClearPat();
                    DisableAutoPublishWithoutPat();
                    StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_GitHubPatValidateFailed, ex.Message);
                }
            }
            else if (result is ContentDialogResult.Secondary)
            {
                _metadataPublishService.ClearPat();
                DisableAutoPublishWithoutPat();
                StatusMessage = Lang.ZZZGachaInfoWindow_GitHubPatCleared;
            }

            RefreshPublishButtons();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manage GitHub PAT");
            StatusMessage = ex.Message;
        }
    }


    /// <summary>
    /// 维护者：将选中的语言包以一次 commit 推送到 GitHub metadata 分支（Octokit + PAT）。
    /// </summary>
    [RelayCommand]
    private async Task PublishToMetadataBranchAsync()
    {
        if (isPublishingMetadata)
        {
            return;
        }

        try
        {
            if (!_metadataPublishService.HasStoredPat)
            {
                StatusMessage = Lang.ZZZGachaInfoWindow_GitHubPatNotConfigured;
                return;
            }

            var packages = new Dictionary<string, IReadOnlyList<ZZZGachaInfo>>(StringComparer.Ordinal);
            foreach (string key in GridView_Languages.SelectedItems.Cast<string>())
            {
                if (gachaInfoDict.TryGetValue(key, out List<ZZZGachaInfo>? list) && list is { Count: > 0 })
                {
                    packages[key] = list;
                }
            }

            if (packages.Count == 0)
            {
                StatusMessage = Lang.ZZZGachaInfoWindow_PublishNoSelection;
                return;
            }

            isPublishingMetadata = true;
            RefreshPublishButtons();
            StatusMessage = Lang.ZZZGachaInfoWindow_PublishingMetadata;

            ZZZGachaMetadataPublishResult publish = await _metadataPublishService.PublishAsync(packages, cancellationToken: cts.Token).ConfigureAwait(true);
            StatusMessage = publish.Unchanged
                ? string.Format(Lang.ZZZGachaInfoWindow_PublishUnchanged, publish.FileCount)
                : string.Format(Lang.ZZZGachaInfoWindow_PublishSucceeded, publish.FileCount, publish.CommitSha);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Lang.GachaLogPage_OperationCanceled;
        }
        catch (AuthorizationException ex)
        {
            // PAT 权限或失效；不记录 token
            _logger.LogWarning(ex, "Publish ZZZ metadata unauthorized");
            StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_PublishUnauthorized, ex.Message);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Publish ZZZ metadata API error ({status})", ex.StatusCode);
            StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_PublishFailed, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish ZZZ metadata");
            StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_PublishFailed, ex.Message);
        }
        finally
        {
            isPublishingMetadata = false;
            RefreshPublishButtons();
        }
    }


    /// <summary>
    /// 维护者：配置定时提交（开关 + UTC+8 时刻），可选保存后立即执行一次。从关闭改为开启时先校验 PAT 写入权限。
    /// </summary>
    [RelayCommand]
    private async Task ConfigureAutoPublishAsync()
    {
        try
        {
            if (!_metadataPublishService.HasStoredPat)
            {
                StatusMessage = Lang.ZZZGachaInfoWindow_GitHubPatNotConfigured;
                return;
            }

            var toggle = new ToggleSwitch
            {
                Header = Lang.ZZZGachaInfoWindow_AutoPublishEnable,
                IsOn = _autoPublishService.Enabled,
            };
            var timePicker = new TimePicker
            {
                Header = Lang.ZZZGachaInfoWindow_AutoPublishTime,
                ClockIdentifier = "24HourClock",
                MinuteIncrement = 1,
                Time = _autoPublishService.TimeOfDay,
            };
            var localTimeText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            };
            void UpdateLocalTime()
            {
                // 本机不在 UTC+8 时给出换算，免得按本机时间填错
                string? local = FormatLocalTimeOfDay(timePicker.Time);
                localTimeText.Text = local is null ? "" : string.Format(Lang.ZZZGachaInfoWindow_AutoPublishLocalTime, local);
                localTimeText.Visibility = local is null ? Visibility.Collapsed : Visibility.Visible;
            }
            UpdateLocalTime();
            timePicker.TimeChanged += (_, _) => UpdateLocalTime();

            var panel = new StackPanel { Spacing = 12, Width = 360 };
            panel.Children.Add(new TextBlock
            {
                Text = Lang.ZZZGachaInfoWindow_AutoPublishDescription,
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(toggle);
            panel.Children.Add(timePicker);
            panel.Children.Add(localTimeText);
            panel.Children.Add(new TextBlock
            {
                Text = BuildAutoPublishStatus(),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });

            var dialog = new ContentDialog
            {
                Title = Lang.ZZZGachaInfoWindow_AutoPublish,
                Content = panel,
                PrimaryButtonText = Lang.Common_Save,
                SecondaryButtonText = Lang.ZZZGachaInfoWindow_AutoPublishSaveAndRunNow,
                CloseButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result is ContentDialogResult.None)
            {
                return;
            }

            bool enable = toggle.IsOn;
            var time = new TimeSpan(timePicker.Time.Hours, timePicker.Time.Minutes, 0);
            // 只改时刻不必再联网校验；从关闭改为开启才确认这个 PAT 能写入
            if (enable && !_autoPublishService.Enabled && !await EnsureAutoPublishPermissionAsync())
            {
                return;
            }
            _autoPublishService.ApplySettings(enable, time);
            StatusMessage = enable
                ? string.Format(Lang.ZZZGachaInfoWindow_AutoPublishEnabled, time.ToString(@"hh\:mm", CultureInfo.InvariantCulture))
                : Lang.ZZZGachaInfoWindow_AutoPublishDisabled;

            if (result is ContentDialogResult.Secondary)
            {
                await RunAutoPublishNowAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configure ZZZ metadata auto publish");
            StatusMessage = ex.Message;
        }
    }


    /// <summary>
    /// 确认已存 PAT 能写入 metadata 仓库，不能时在状态栏说明原因。
    /// </summary>
    /// <returns>可写入为 true。</returns>
    private async Task<bool> EnsureAutoPublishPermissionAsync()
    {
        try
        {
            Button_AutoPublish.IsEnabled = false;
            StatusMessage = Lang.ZZZGachaInfoWindow_AutoPublishCheckingPermission;
            if (await _metadataPublishService.HasWritePermissionAsync(cts.Token).ConfigureAwait(true))
            {
                return true;
            }
            StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_AutoPublishNoPermission, $"{ZZZGachaMetadataPaths.RepositoryOwner}/{ZZZGachaMetadataPaths.RepositoryName}");
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // 不记录 token
            _logger.LogWarning(ex, "Check GitHub PAT write permission");
            StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_AutoPublishPermissionCheckFailed, ex.Message);
            return false;
        }
        finally
        {
            RefreshPublishButtons();
        }
    }


    /// <summary>
    /// 立即用已登录账号获取全部语言包并提交，与定时提交走同一条路径（成功同样算本期已完成）。
    /// </summary>
    private async Task RunAutoPublishNowAsync()
    {
        if (isPublishingMetadata)
        {
            return;
        }

        try
        {
            isPublishingMetadata = true;
            RefreshPublishButtons();
            StatusMessage = Lang.ZZZGachaInfoWindow_AutoPublishRunning;
            ZZZGachaMetadataAutoPublishResult result = await _autoPublishService.RunNowAsync(cts.Token).ConfigureAwait(true);
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Lang.GachaLogPage_OperationCanceled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run ZZZ metadata auto publish now");
            StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_PublishFailed, ex.Message);
        }
        finally
        {
            isPublishingMetadata = false;
            RefreshPublishButtons();
        }
    }


    /// <summary>
    /// PAT 已被清除：定时提交无法再运行，一并关闭，免得设置里一直显示「已开启」。
    /// </summary>
    private void DisableAutoPublishWithoutPat()
    {
        if (_autoPublishService.Enabled)
        {
            _autoPublishService.ApplySettings(false, _autoPublishService.TimeOfDay);
        }
    }


    /// <summary>
    /// 定时提交对话框里的状态：上次运行结果 + 下次运行时刻（未开启时不显示下次）。
    /// </summary>
    /// <returns>多行文本。</returns>
    private string BuildAutoPublishStatus()
    {
        var lines = new List<string>();
        long lastRunTicks = AppConfig.ZZZGachaMetadataAutoPublishLastRunTicks;
        lines.Add(lastRunTicks > 0
            ? string.Format(Lang.ZZZGachaInfoWindow_AutoPublishLastRun, FormatServerTime(new DateTimeOffset(lastRunTicks, TimeSpan.Zero)), AppConfig.ZZZGachaMetadataAutoPublishLastRunMessage)
            : Lang.ZZZGachaInfoWindow_AutoPublishNeverRun);
        if (_autoPublishService.IsRunning)
        {
            lines.Add(Lang.ZZZGachaInfoWindow_AutoPublishRunning);
        }
        else if (_autoPublishService.GetNextDue() is DateTimeOffset due)
        {
            lines.Add(due <= DateTimeOffset.UtcNow
                ? Lang.ZZZGachaInfoWindow_AutoPublishNextRunSoon
                : string.Format(Lang.ZZZGachaInfoWindow_AutoPublishNextRun, FormatServerTime(due)));
        }
        return string.Join(Environment.NewLine, lines);
    }


    /// <summary>
    /// 按 UTC+8 显示时刻，与设定时刻同一时区，便于对照。
    /// </summary>
    private static string FormatServerTime(DateTimeOffset time)
    {
        return time.ToOffset(ZZZGachaMetadataPublishSchedule.ServerOffset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }


    /// <summary>
    /// UTC+8 的某个时刻对应的本机时刻（按今天换算）。
    /// </summary>
    /// <param name="serverTimeOfDay">UTC+8 一天中的时刻。</param>
    /// <returns>本机时刻文本；本机就在 UTC+8 时为 null。</returns>
    private static string? FormatLocalTimeOfDay(TimeSpan serverTimeOfDay)
    {
        TimeSpan offset = ZZZGachaMetadataPublishSchedule.ServerOffset;
        DateTimeOffset serverNow = DateTimeOffset.UtcNow.ToOffset(offset);
        DateTimeOffset local = new DateTimeOffset(serverNow.Year, serverNow.Month, serverNow.Day, 0, 0, 0, offset).Add(serverTimeOfDay).ToLocalTime();
        return local.Offset == offset ? null : local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }


    [RelayCommand]
    private void SaveToDatabase()
    {
        try
        {
            if (GridView_Languages.SelectedItems.Count != 1)
            {
                return;
            }
            if (GridView_Languages.SelectedItems[0] is string key)
            {
                if (gachaInfoDict.TryGetValue(key, out var list))
                {
                    using var dapper = DatabaseService.CreateConnection();
                    using var t = dapper.BeginTransaction();
                    dapper.Execute("""
                        INSERT OR REPLACE INTO ZZZGachaInfo (Id, Name, Icon, Rarity, ElementType, Profession)
                        VALUES (@Id, @Name, @Icon, @Rarity, @ElementType, @Profession);
                        """, list, t);
                    t.Commit();
                    StatusMessage = string.Format(Lang.ZZZGachaInfoWindow_SavedToDatabase, key, list.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save ZZZGachaInfo to database");
            StatusMessage = ex.Message;
        }
    }



    [RelayCommand]
    private async Task ExportToFolderAsync()
    {
        try
        {
            if (GridView_Languages.SelectedItems.Count > 0)
            {
                string? folder = await FileDialogHelper.PickFolderAsync(this.Content.XamlRoot);
                if (Directory.Exists(folder))
                {
                    foreach (string key in GridView_Languages.SelectedItems.Cast<string>())
                    {
                        if (gachaInfoDict.TryGetValue(key, out var list))
                        {
                            var obj = new miHoYoApiWrapper<ZZZGachaWiki>
                            {
                                Retcode = 0,
                                Message = "",
                                Data = new ZZZGachaWiki
                                {
                                    Game = GameBiz.nap,
                                    Language = key[^5..],
                                    List = list.OrderBy(x => x.Id).ToList(),
                                },
                            };
                            string json = JsonSerializer.Serialize(obj, AppConfig.JsonSerializerOptions);
                            string path = Path.Combine(folder, $"ZZZGachaInfo.{key}.json");
                            await File.WriteAllTextAsync(path, json, cts.Token);
                        }
                    }
                    await Launcher.LaunchUriAsync(new Uri(folder));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export ZZZGachaInfo to folder");
            StatusMessage = ex.Message;
        }
    }


}
