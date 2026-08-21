using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.GameRecord;

[INotifyPropertyChanged]
public sealed partial class BBSWebBridge : UserControl
{


    private readonly ILogger<BBSWebBridge> _logger = AppConfig.GetLogger<BBSWebBridge>();


    private readonly HttpClient _httpClient = AppConfig.GetService<HttpClient>();



    private const string miHoYoJSInterface = """
        if (window.MiHoYoJSInterface === undefined) {
            window.MiHoYoJSInterface = {
                postMessage: function(arg) { chrome.webview.postMessage(arg) },
                closePage: function() { this.postMessage('{"method":"closePage"}') },
            };
        }
        """;


    private const string HideScrollBarScript = """
        let st = document.createElement('style');
        st.innerHTML = '::-webkit-scrollbar{display:none}';
        document.querySelector('body').appendChild(st);
        """;




    public BBSWebBridge()
    {
        this.InitializeComponent();
    }



    private bool initialized = false;



    public GameBiz CurrentGameBiz { get; set; }



    private GameRecordClient _gameRecordClient;



    private Dictionary<string, string> cookieDic = new();


    [ObservableProperty]
    private GameRecordRole _GameRecordRole;
    partial void OnGameRecordRoleChanged(GameRecordRole value)
    {
        try
        {
            if (initialized)
            {
                if (CurrentGameBiz.IsGlobalServer())
                {
                    _gameRecordClient = AppConfig.GetService<HoyolabClient>();
                }
                else
                {
                    _gameRecordClient = AppConfig.GetService<HyperionClient>();
                }
                _ = LoadPageAsync(true);
            }
        }
        catch { }
    }



    public string DocumentTitle { get; set => SetProperty(ref field, value); }



    public event EventHandler<object> WebPageClosed;



    private async Task InitializeWebViewAsync()
    {
        try
        {
            if (initialized)
            {
                return;
            }
            if (CurrentGameBiz.IsGlobalServer())
            {
                _gameRecordClient = AppConfig.GetService<HoyolabClient>();
            }
            else
            {
                _gameRecordClient = AppConfig.GetService<HyperionClient>();
            }
            await webview2.EnsureCoreWebView2Async();
            var coreWebView2 = webview2.CoreWebView2;
            coreWebView2.Settings.UserAgent = _gameRecordClient.UAContent;

            coreWebView2.NavigationStarting -= Corewebview2_NavigationStarting;
            coreWebView2.NavigationStarting += Corewebview2_NavigationStarting;
            coreWebView2.DOMContentLoaded -= Corewebview2_DOMContentLoaded;
            coreWebView2.DOMContentLoaded += Corewebview2_DOMContentLoaded;
            coreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            coreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            coreWebView2.DocumentTitleChanged -= CoreWebView2_DocumentTitleChanged;
            coreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;

            // 米游社客户端会在 WebView 层注入绝区零战绩头；WebView2 没有这层，缺 geetest_ext 等会 10041
            coreWebView2.AddWebResourceRequestedFilter("https://api-takumi-record.mihoyo.com/*", CoreWebView2WebResourceContext.All);
            coreWebView2.AddWebResourceRequestedFilter("https://api-takumi.mihoyo.com/*", CoreWebView2WebResourceContext.All);
            coreWebView2.AddWebResourceRequestedFilter("https://sg-public-api.hoyolab.com/*", CoreWebView2WebResourceContext.All);
            coreWebView2.WebResourceRequested -= CoreWebView2_WebResourceRequested;
            coreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            initialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _logger.LogError(ex, "Initialize WebView2 failed.");
        }
    }



    public async Task LoadPageAsync(bool force = false)
    {
        try
        {
            await InitializeWebViewAsync();

            var coreWebView2 = webview2.CoreWebView2;
            if (coreWebView2.Source is "about:blank" || force)
            {
                var manager = coreWebView2.CookieManager;
                foreach (string cookieUrl in GetCookieClearUrls())
                {
                    var cookies = await manager.GetCookiesAsync(cookieUrl);
                    foreach (var cookie in cookies)
                    {
                        manager.DeleteCookie(cookie);
                    }
                }

                await Task.Delay(60);
                ParseCookie();
                InjectDeviceFpCookies();
                string cookieDomain = CurrentGameBiz.IsGlobalServer() ? ".hoyolab.com" : ".mihoyo.com";
                foreach (var cookie in cookieDic)
                {
                    manager.AddOrUpdateCookie(manager.CreateCookie(cookie.Key, cookie.Value, cookieDomain, "/"));
                }

                string? url = (CurrentGameBiz.IsGlobalServer(), CurrentGameBiz.Game) switch
                {
                    (true, GameBiz.bh3) => "https://act.hoyolab.com/app/community-game-records-sea/bh3/m.html",
                    (true, GameBiz.hk4e) => "https://act.hoyolab.com/app/community-game-records-sea/m.html?gid=2",
                    (true, GameBiz.hkrpg) => "https://act.hoyolab.com/app/community-game-records-sea/m.html?gid=6",
                    (true, GameBiz.nap) => "https://act.hoyolab.com/app/zzz-game-record/m.html?gid=8",
                    (false, GameBiz.bh3) => "https://act.mihoyo.com/app/mihoyo-bh3-game-record/index.html?game_id=1",
                    // 旧 ?game_id= 入口已换成 v7 壳；原神走 index.html，星铁走官方现用的 rpg/index.html
                    (false, GameBiz.hk4e) => "https://webstatic.mihoyo.com/app/community-game-records/index.html?mhy_presentation_style=fullscreen&bbs_auth_required=true&game_id=2",
                    (false, GameBiz.hkrpg) => "https://webstatic.mihoyo.com/app/community-game-records/rpg/index.html?mhy_presentation_style=fullscreen&game_id=6",
                    (false, GameBiz.nap) => "https://act.mihoyo.com/app/mihoyo-zzz-game-record/m.html?game_id=8",
                    _ => null,
                };
                if (url is not null)
                {
                    coreWebView2.Navigate(url);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }




    /// <summary>
    /// 解析角色 Cookie 写入 WebView。必须只按第一个 <c>=</c> 分割：
    /// <c>cookie_token_v2</c> / <c>ltoken_v2</c> 的值本身含 <c>=</c>（如末尾 padding），
    /// <c>Split('=')</c> 后长度不为 2 会被整段丢掉，战绩页会变成未登录或风控。
    /// </summary>
    private void ParseCookie()
    {
        cookieDic.Clear();
        string? cookie = GameRecordRole?.Cookie;
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return;
        }
        foreach (var kv in GameRecordCookieRefreshService.ParseCookie(cookie))
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
            {
                cookieDic[kv.Key] = kv.Value;
            }
        }
    }


    /// <summary>
    /// 清除 WebView 里旧 Cookie 时覆盖的站点。绝区零战绩在 act.mihoyo.com，不能只清 webstatic。
    /// </summary>
    private IEnumerable<string> GetCookieClearUrls()
    {
        if (CurrentGameBiz.IsGlobalServer())
        {
            yield return "https://act.hoyolab.com";
            yield break;
        }
        yield return "https://webstatic.mihoyo.com";
        if (CurrentGameBiz.Game is GameBiz.nap)
        {
            yield return "https://act.mihoyo.com";
        }
    }


    /// <summary>
    /// 把 getFp 指纹写入 WebView Cookie，对齐官方战绩 H5 的 DEVICEFP / _MHYUUID。
    /// </summary>
    private void InjectDeviceFpCookies()
    {
        if (_gameRecordClient is null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(_gameRecordClient.DeviceFp) && _gameRecordClient.DeviceFp is not "0000000000000")
        {
            cookieDic["DEVICEFP"] = _gameRecordClient.DeviceFp;
        }
        if (!string.IsNullOrWhiteSpace(_gameRecordClient.DeviceFpSeedId))
        {
            cookieDic["DEVICEFP_SEED_ID"] = _gameRecordClient.DeviceFpSeedId;
        }
        if (!string.IsNullOrWhiteSpace(_gameRecordClient.DeviceFpSeedTime))
        {
            cookieDic["DEVICEFP_SEED_TIME"] = _gameRecordClient.DeviceFpSeedTime;
        }
        if (!string.IsNullOrWhiteSpace(_gameRecordClient.DeviceId))
        {
            cookieDic["_MHYUUID"] = _gameRecordClient.DeviceId;
        }
    }







    #region Core WebView



    private async void Corewebview2_NavigationStarting(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        try
        {
            await webview2.ExecuteScriptAsync(miHoYoJSInterface);
        }
        catch { }
    }


    private async void Corewebview2_DOMContentLoaded(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2DOMContentLoadedEventArgs args)
    {
        try
        {
            await webview2.ExecuteScriptAsync(HideScrollBarScript);
        }
        catch { }
    }


    private async void CoreWebView2_WebMessageReceived(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            string message = args.TryGetWebMessageAsString();
            Debug.WriteLine(message);
            JsParam param = JsonSerializer.Deserialize<JsParam>(message)!;
            JsResult? result = await HandleJsMessageAsync(param);
            await CallbackAsync(param.Callback, result);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }


    private void CoreWebView2_DocumentTitleChanged(Microsoft.Web.WebView2.Core.CoreWebView2 sender, object args)
    {
        try
        {
            DocumentTitle = sender.DocumentTitle;
        }
        catch { }
    }


    /// <summary>
    /// 向绝区零战绩 / 绳网月报 XHR 补齐官方 WebView 注入头（geetest_ext、page、platform 等）。
    /// </summary>
    private void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            if (CurrentGameBiz.Game is not GameBiz.nap || GameRecordRole is null || _gameRecordClient is null)
            {
                return;
            }
            string uri = args.Request.Uri;
            if (uri.IndexOf("game_record_zzz", StringComparison.OrdinalIgnoreCase) < 0
                && uri.IndexOf("nap_ledger", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }
            foreach (var header in _gameRecordClient.GetZZZGameRecordH5InjectHeaders(GameRecordRole, uri))
            {
                args.Request.Headers.SetHeader(header.Key, header.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inject ZZZ game record WebView headers.");
        }
    }


    #endregion




    #region Js Message Method




    private async Task CallbackAsync(string? callback, JsResult? result)
    {
        if (callback == null)
        {
            return;
        }
        var js = $"""
            javascript:mhyWebBridge("{callback}"{(result == null ? "" : "," + result.ToString())})
            """;

        await webview2.ExecuteScriptAsync(js);
    }



    private async Task<JsResult?> HandleJsMessageAsync(JsParam param)
    {
        return param.Method switch
        {
            "closePage" => ClosePage(param),
            "configure_share" => null,
            "eventTrack" => null,
            //"getActionTicket" => await GetActionTicketAsync(param),
            "getCookieInfo" => GetCookieInfo(param),
            "getCookieToken" => GetCookieToken(param),
            "getDS" => GetDynamicSecret(param),
            "getDS2" => GetDynamicSecret2(param),
            "getHTTPRequestHeaders" => GetHttpRequestHeader(param),
            "getStatusBarHeight" => GetStatusBarHeight(param),
            "getUserInfo" => GetUserInfo(param),
            "hideLoading" => null,
            "login" => null,
            "pushPage" => PushPage(param),
            "showLoading" => null,
            "share" => await HandleShareAsync(param),
            "getCurrentLocale" => GetCurrentLocale(param),
            _ => null,
        };
    }



    private async Task<JsResult?> HandleShareAsync(JsParam param)
    {
        if (param.Payload?["type"]?.ToString() is "screenshot")
        {
            await CaptureScreenshotAsync();
        }
        else if (param.Payload?["type"]?.ToString() is "image")
        {
            string? base64 = param.Payload?["content"]?["image_base64"]?.ToString();
            await ConvertScreenshotFromBase64StringAsync(base64);
        }
        else if (param.Payload?["imageUrls"]?[0]?.ToString() is string { Length: > 0 } url)
        {
            await DownloadScreenshotAsync(url);
        }
        return null;
    }



    private byte[]? screenshotBytes;


    private async Task CaptureScreenshotAsync()
    {
        try
        {
            string data = await webview2.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", """{"captureBeyondViewport": true}""");
            string? base64 = JsonNode.Parse(data)?["data"]?.ToString();
            await ConvertScreenshotFromBase64StringAsync(base64);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "capture screenshot");
            Grid_Screenshot.Visibility = Visibility.Collapsed;
        }
    }



    private async Task ConvertScreenshotFromBase64StringAsync(string? base64)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(base64))
            {
                screenshotBytes = Convert.FromBase64String(base64);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(new MemoryStream(screenshotBytes).AsRandomAccessStream());
                Image_Screenshot.Source = bitmap;
                Grid_Screenshot.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Convert screenshot from base64");
            Grid_Screenshot.Visibility = Visibility.Collapsed;
        }
    }



    private async Task DownloadScreenshotAsync(string url)
    {
        try
        {
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            screenshotBytes = await _httpClient.GetByteArrayAsync(url, source.Token);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(new MemoryStream(screenshotBytes).AsRandomAccessStream());
            Image_Screenshot.Source = bitmap;
            Grid_Screenshot.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "download screenshot");
            Grid_Screenshot.Visibility = Visibility.Collapsed;
        }
    }




    [RelayCommand]
    private async Task SaveScreenshotAsync()
    {
        try
        {
            if (screenshotBytes is not null)
            {
                string name = $"{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.png";
                string? file = await FileDialogHelper.OpenSaveFileDialogAsync(this.XamlRoot, name, ("Png File", ".png"));
                if (!string.IsNullOrWhiteSpace(file))
                {
                    await File.WriteAllBytesAsync(file, screenshotBytes);
                    CloseScreenshotGrid();
                    var storage = await StorageFile.GetFileFromPathAsync(file);
                    var options = new FolderLauncherOptions();
                    options.ItemsToSelect.Add(storage);
                    await Launcher.LaunchFolderAsync(await storage.GetParentAsync(), options);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save screenshot");
        }
    }



    [RelayCommand]
    private async Task CopyScreenshotAsync()
    {
        try
        {
            if (screenshotBytes is not null)
            {
                string file = Path.GetTempFileName();
                await File.WriteAllBytesAsync(file, screenshotBytes);
                var storage = await StorageFile.GetFileFromPathAsync(file);
                ClipboardHelper.SetBitmap(storage);
                CloseScreenshotGrid();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy screenshot");
        }
    }


    [RelayCommand]
    private void CloseScreenshotGrid()
    {
        try
        {
            screenshotBytes = null;
            Grid_Screenshot.Visibility = Visibility.Collapsed;
        }
        catch { }
    }



    private JsResult? GetCurrentLocale(JsParam param)
    {
        int offset = TimeZoneInfo.Local.BaseUtcOffset.Hours;
        return new()
        {
            Data = new()
            {
                ["language"] = LanguageUtil.FilterLanguage(CultureInfo.CurrentUICulture.Name),
                ["timeZone"] = offset switch
                {
                    > 0 => $"GMT+{offset}",
                    < 0 => $"GMT{offset}",
                    _ => "GMT",
                },
            }
        };
    }



    private JsResult? GetCookieToken(JsParam param)
    {
        return new()
        {
            Data = new()
            {
                ["cookie_token"] = cookieDic.GetValueOrDefault("cookie_token") ?? cookieDic.GetValueOrDefault("cookie_token_v2") ?? "",
            },
        };
    }


    private JsResult? ClosePage(JsParam param)
    {
        if (webview2.CoreWebView2.CanGoBack)
        {
            webview2.CoreWebView2.GoBack();
        }
        else
        {
            WebPageClosed?.Invoke(this, EventArgs.Empty);
        }
        return null;
    }



    private JsResult? PushPage(JsParam param)
    {
        string? url = param.Payload?["page"]?.ToString();
        if (!string.IsNullOrWhiteSpace(url))
        {
            // 避免在浏览星穹铁道我的全部角色时，出现版本过低的错误
            url = url.Replace("rolePageAccessNotAllowed=&", "");
            webview2.CoreWebView2.Navigate(url);
        }
        return null;
    }


    private JsResult? GetUserInfo(JsParam param)
    {
        return new()
        {
            Data = new()
            {
                ["id"] = GameRecordRole.Uid,
                ["gender"] = "",
                ["nickname"] = GameRecordRole.Nickname!,
                ["introduce"] = "",
                ["avatar_url"] = "",
            },
        };
    }


    private JsResult? GetStatusBarHeight(JsParam param)
    {
        return new()
        {
            Data = new()
            {
                ["statusBarHeight"] = 0
            }
        };
    }


    private JsResult? GetCookieInfo(JsParam param)
    {
        return new()
        {
            Data = cookieDic.ToDictionary(x => x.Key, x => (object)x.Value),
        };
    }


    private JsResult? GetHttpRequestHeader(JsParam param)
    {
        // v7 战绩页会读 device_model / channel；缺字段会在 includes() 处抛错
        return new()
        {
            Data = new()
            {
                ["x-rpc-client_type"] = "5",
                ["x-rpc-app_version"] = _gameRecordClient.AppVersion,
                ["x-rpc-device_fp"] = _gameRecordClient.DeviceFp,
                ["x-rpc-device_id"] = _gameRecordClient.DeviceId,
                ["x-rpc-sys_version"] = GameRecordClient.RpcSysVersion,
                ["x-rpc-device_name"] = GameRecordClient.RpcDeviceName,
                ["x-rpc-device_model"] = GameRecordClient.RpcDeviceName,
                ["x-rpc-channel"] = CurrentGameBiz.IsGlobalServer() ? "hoyolab" : "miyousheluodi",
            },
        };
    }


    #endregion




    #region Dynamic Secret


    /// <summary>
    /// H5 <c>getDS</c>（Gen1）。空 DS 会被 v7 战绩页当成旧客户端，展示「请更新至 V2.10 以上」。
    /// </summary>
    private JsResult? GetDynamicSecret(JsParam param)
    {
        return new JsResult
        {
            Data = new()
            {
                ["DS"] = _gameRecordClient.CreateJsBridgeSecret(),
            }
        };
    }


    /// <summary>
    /// H5 <c>getDS2</c>（Gen2）。payload.query 为对象或已序列化 query，payload.body 为 POST JSON。
    /// </summary>
    private JsResult? GetDynamicSecret2(JsParam param)
    {
        string query = BuildSortedQuery(param.Payload?["query"]);
        string body = ReadJsonNodeString(param.Payload?["body"]);
        return new JsResult
        {
            Data = new()
            {
                ["DS"] = _gameRecordClient.CreateJsBridgeSecret2(query, body),
            }
        };
    }


    /// <summary>
    /// 把 H5 传入的 query 收成官方 DS 所用的排序 <c>k=v&amp;k=v</c>。
    /// </summary>
    private static string BuildSortedQuery(JsonNode? queryNode)
    {
        if (queryNode is JsonObject obj)
        {
            var pairs = new List<string>();
            foreach (var kv in obj)
            {
                pairs.Add($"{kv.Key}={ReadJsonNodeString(kv.Value)}");
            }
            pairs.Sort(StringComparer.Ordinal);
            return string.Join("&", pairs);
        }
        return ReadJsonNodeString(queryNode);
    }


    /// <summary>
    /// 读取 JSON 字符串值；<see cref="JsonNode.ToString"/> 会给字符串多包一层引号，不能直接用。
    /// </summary>
    private static string ReadJsonNodeString(JsonNode? node)
    {
        if (node is null)
        {
            return "";
        }
        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            return text ?? "";
        }
        if (node is JsonObject or JsonArray)
        {
            return node.ToJsonString();
        }
        return node.ToString();
    }


    #endregion




    #region WebView Message Object



    private class JsParam
    {
        /// <summary>
        /// 方法名称
        /// </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; }

        /// <summary>
        /// 数据 可以为空
        /// </summary>
        [JsonPropertyName("payload")]
        public JsonNode? Payload { get; set; }

        /// <summary>
        /// 回调的名称，调用 JavaScript:mhyWebBridge 时作为首个参数传入
        /// </summary>
        [JsonPropertyName("callback")]
        public string? Callback { get; set; }
    }



    private class JsResult
    {
        /// <summary>
        /// 代码
        /// </summary>
        [JsonPropertyName("retcode")]
        public int Code { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 数据
        /// </summary>
        [JsonPropertyName("data")]
        public Dictionary<string, object> Data { get; set; } = default!;


        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }

    }


    #endregion



}
