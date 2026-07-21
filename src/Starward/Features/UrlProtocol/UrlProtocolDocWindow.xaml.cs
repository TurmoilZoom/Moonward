using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Starward.Frameworks;
using Starward.Language;
using Starward.Setup.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.System;


namespace Starward.Features.UrlProtocol;

public sealed partial class UrlProtocolDocWindow : WindowEx
{

    private const string DocPath = "docs/UrlProtocol.md";
    private const string DocPathZhCN = "docs/UrlProtocol.zh-CN.md";

    /// <summary>
    /// 文档来源（仓库, 分支），运行时从 GitHub 拉取，不随程序打包 md 文件。
    /// 主仓库的文档位于其默认分支 rebase/develop（含中文文档）；上游 Scighost 仅 main 分支有英文文档，作兜底。
    /// </summary>
    private static readonly (string Repository, string Branch)[] DocSources =
    [
        (ReleaseClient.Repository, "rebase/develop"),
        ("Scighost/Starward", "main"),
    ];


    /// <summary>
    /// 拉取失败（网络异常）兜底时在 WebView 中打开的 GitHub 页面地址，命中后更新为实际来源与语言。
    /// </summary>
    private string _browserUrl = $"https://github.com/{DocSources[0].Repository}/blob/{DocSources[0].Branch}/{DocPath}";


    private readonly ILogger<UrlProtocolDocWindow> _logger = AppConfig.GetLogger<UrlProtocolDocWindow>();

    private readonly ReleaseClient _releaseClient = AppConfig.GetService<ReleaseClient>();

    private readonly HttpClient _httpClient = AppConfig.GetService<HttpClient>();


    public UrlProtocolDocWindow()
    {
        InitializeComponent();
        InitializeWindow();
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
            webview.Visibility = Visibility.Collapsed;

            await webview.EnsureCoreWebView2Async();
            string ua = webview.CoreWebView2.Settings.UserAgent;
            if (!ua.Contains("Moonward"))
            {
                webview.CoreWebView2.Settings.UserAgent = $"{ua} Moonward/{AppConfig.AppVersion}";
            }
            webview.CoreWebView2.Profile.PreferredColorScheme = ShouldAppsUseDarkMode()
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
            webview.CoreWebView2.DOMContentLoaded -= CoreWebView2_DOMContentLoaded;
            webview.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
            webview.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
            webview.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

            string markdown = await GetDocumentMarkdownAsync();
            string html = await RenderMarkdownAsync(markdown);
            webview.NavigateToString(html);
        }
        catch (COMException ex)
        {
            _logger.LogError(ex, "Load url protocol document");
            TextBlock_Error.Text = Lang.Common_WebView2ComponentInitializationFailed;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            StackPanel_Error.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
        {
            _logger.LogError(ex, "Load url protocol document");
            webview.Source = new Uri(_browserUrl);
            webview.Visibility = Visibility.Visible;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            StackPanel_Error.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load url protocol document");
            TextBlock_Error.Text = Lang.DownloadGamePage_UnknownError;
            StackPanel_Loading.Visibility = Visibility.Collapsed;
            StackPanel_Error.Visibility = Visibility.Visible;
        }
    }


    private static IReadOnlyList<string> GetDocumentPaths()
    {
        string? lang = AppConfig.Language ?? CultureInfo.CurrentUICulture.Name;
        if (lang is "zh-CN" or "zh-TW" or "zh-HK")
        {
            return [DocPathZhCN, DocPath];
        }
        return [DocPath];
    }


    private async Task<string> GetDocumentMarkdownAsync()
    {
        foreach (string path in GetDocumentPaths())
        {
            foreach (var (repository, branch) in DocSources)
            {
                string url = $"https://raw.githubusercontent.com/{repository}/{branch}/{path}";
                using var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    _browserUrl = $"https://github.com/{repository}/blob/{branch}/{path}";
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }
        throw new HttpRequestException("Cannot fetch UrlProtocol document from GitHub.");
    }


    private async Task<string> RenderMarkdownAsync(string markdown)
    {
        string html = await _releaseClient.RenderGithubMarkdownAsync(markdown);
        var cssFile = Path.Combine(AppContext.BaseDirectory, @"Assets\CSS\github-markdown.css");
        string css;
        if (File.Exists(cssFile))
        {
            css = $"<style>{await File.ReadAllTextAsync(cssFile)}</style>";
        }
        else
        {
            css = """<link href="https://cdnjs.cloudflare.com/ajax/libs/github-markdown-css/5.8.1/github-markdown.min.css" type="text/css" rel="stylesheet" />""";
        }
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <base target="_blank">
              {{css}}
              <style>
                @media (prefers-color-scheme: light) {
                  ::-webkit-scrollbar { width: 6px }
                  ::-webkit-scrollbar-thumb { background-color: #b8b8b8; border-radius: 1000px 0px 0px 1000px }
                  ::-webkit-scrollbar-thumb:hover { background-color: #8b8b8b }
                }
                @media (prefers-color-scheme: dark) {
                  ::-webkit-scrollbar { width: 6px }
                  ::-webkit-scrollbar-thumb { background-color: #646464; border-radius: 1000px 0px 0px 1000px }
                  ::-webkit-scrollbar-thumb:hover { background-color: #8b8b8b }
                }
              </style>
            </head>
            <body style="margin: 12px 24px 12px 24px; overflow-x: hidden;">
              <article class="markdown-body" style="background: transparent;">
                {{html}}
              </article>
              <script>
                document.querySelectorAll('img[data-canonical-src]').forEach(img => {
                  const canonical = img.getAttribute('data-canonical-src');
                  if (canonical) {
                    img.src = canonical;
                    const parent = img.parentElement;
                    if (parent && parent.tagName.toLowerCase() === 'a') {
                      parent.href = canonical;
                    }
                  }
                });
              </script>
            </body>
            </html>
            """;
    }


    private void CoreWebView2_DOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        webview.Focus(FocusState.Programmatic);
        webview.Visibility = Visibility.Visible;
        StackPanel_Loading.Visibility = Visibility.Collapsed;
        StackPanel_Error.Visibility = Visibility.Collapsed;
    }


    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        try
        {
            _ = Launcher.LaunchUriAsync(new Uri(args.Uri));
            args.Handled = true;
        }
        catch { }
    }

}