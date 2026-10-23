using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Starward.Frameworks;
using Starward.Language;
using Starward.Setup.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;


namespace Starward.Features.UrlProtocol;

/// <summary>
/// URL 协议帮助文档（设置 - 高级）。文档运行时拉取、不随程序打包，用 <see cref="Controls.MarkdownView"/> 在本地渲染。
/// </summary>
public sealed partial class UrlProtocolDocWindow : WindowEx
{

    private const string DocPath = "docs/UrlProtocol.md";
    private const string DocPathZhCN = "docs/UrlProtocol.zh-CN.md";

    /// <summary>
    /// 文档来源（仓库, 分支）。主仓库的文档位于其默认分支 rebase/develop（含中文文档），上游 Scighost 仅 main 分支有英文文档，作兜底。
    /// 每个仓库先取 CNB 镜像（仅主仓库有）再取 GitHub：raw.githubusercontent.com 在部分国内网络连不上（同 issue #28 的 jsDelivr）。
    /// </summary>
    private static readonly (string Repository, string Branch)[] DocSources =
    [
        (ReleaseClient.Repository, "rebase/develop"),
        ("Scighost/Starward", "main"),
    ];

    /// <summary>主仓库在 CNB 上的镜像，推送时与 GitHub 同步。</summary>
    private const string CnbRepository = "TurmoilZoom/Moonward";

    /// <summary>单个来源的超时：连不上时常常是挂起而不是立刻失败。</summary>
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(10);


    /// <summary>
    /// 拉取失败时外开浏览器的 GitHub 页面地址。
    /// 不在应用内打开 GitHub 网页，避免搜索/登录框激活 Chromium TSF（issue #1）。
    /// </summary>
    private readonly string _browserUrl = $"https://github.com/{DocSources[0].Repository}/blob/{DocSources[0].Branch}/{DocPath}";


    private readonly ILogger<UrlProtocolDocWindow> _logger = AppConfig.GetLogger<UrlProtocolDocWindow>();

    private readonly HttpClient _httpClient = AppConfig.GetService<HttpClient>();


    private bool _closed;


    public UrlProtocolDocWindow()
    {
        InitializeComponent();
        InitializeWindow();
        Closed += (_, _) => _closed = true;
    }


    private void InitializeWindow()
    {
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        Title = Lang.UrlProtocolDocWindow_Title;
        if (!ShouldAppsUseDarkMode())
        {
            RootGrid.RequestedTheme = ElementTheme.Light;
        }
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        CenterInScreen(720, 640);
        AdaptTitleBarButtonColorToActuallTheme();
    }


    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDocumentAsync();
    }


    private async void Button_Retry_Click(object sender, RoutedEventArgs e)
    {
        await LoadDocumentAsync();
    }


    private async Task LoadDocumentAsync()
    {
        try
        {
            StackPanel_Loading.Visibility = Visibility.Visible;
            StackPanel_Error.Visibility = Visibility.Collapsed;
            Scroller_Document.Visibility = Visibility.Collapsed;

            string markdown = await GetDocumentMarkdownAsync();
            if (_closed) return;
            MarkdownView_Document.Markdown = markdown;
            Scroller_Document.Visibility = Visibility.Visible;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or TaskCanceledException)
        {
            _logger.LogError(ex, "Load url protocol document");
            if (_closed) return;
            try
            {
                _ = Launcher.LaunchUriAsync(new Uri(_browserUrl));
            }
            catch { }
            TextBlock_Error.Text = Lang.Common_NetworkError;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            StackPanel_Error.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load url protocol document");
            if (_closed) return;
            TextBlock_Error.Text = Lang.DownloadGamePage_UnknownError;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            StackPanel_Error.Visibility = Visibility.Visible;
        }
    }


    /// <summary>
    /// 按界面语言决定文档的候选路径：中文界面先取中文文档，没有时退回英文。
    /// </summary>
    /// <returns>候选路径，优先的在前。</returns>
    private static IReadOnlyList<string> GetDocumentPaths()
    {
        string? lang = AppConfig.Language ?? CultureInfo.CurrentUICulture.Name;
        if (lang is "zh-CN" or "zh-TW" or "zh-HK")
        {
            return [DocPathZhCN, DocPath];
        }
        return [DocPath];
    }


    /// <summary>
    /// 文档的原始文件地址，按优先顺序：每个路径依次试各仓库，主仓库先 CNB 后 GitHub。
    /// </summary>
    /// <returns>raw 文件地址。</returns>
    private static IEnumerable<string> GetDocumentUrls()
    {
        foreach (string path in GetDocumentPaths())
        {
            foreach (var (repository, branch) in DocSources)
            {
                if (repository == ReleaseClient.Repository)
                {
                    yield return $"https://cnb.cool/{CnbRepository}/-/git/raw/{branch}/{path}";
                }
                yield return $"https://raw.githubusercontent.com/{repository}/{branch}/{path}";
            }
        }
    }


    /// <summary>
    /// 依次尝试各来源，取到第一份文档为止。
    /// </summary>
    /// <returns>文档 Markdown。</returns>
    /// <exception cref="HttpRequestException">所有来源都失败。</exception>
    private async Task<string> GetDocumentMarkdownAsync()
    {
        Exception? lastException = null;
        foreach (string url in GetDocumentUrls())
        {
            try
            {
                using var cts = new CancellationTokenSource(SourceTimeout);
                using var response = await _httpClient.GetAsync(url, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(cts.Token);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or TaskCanceledException)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Fetch url protocol document from {url}", url);
            }
        }
        throw new HttpRequestException("Cannot fetch UrlProtocol document.", lastException);
    }

}
