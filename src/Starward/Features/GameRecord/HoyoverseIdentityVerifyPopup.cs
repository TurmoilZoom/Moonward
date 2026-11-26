using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Starward.Core;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord;

/// <summary>
/// 国际服登录风控的身份验证（新设备、异地等）：在弹层里打开 HoYoverse 账号中心的安全验证页，由用户完成邮箱验证码。
/// </summary>
/// <remarks>
/// 做法照搬 HYP 国际服前端账号 SDK：把 <c>{actionType:"verify_for_component", actionTicket}</c> 写进
/// <c>.hoyoverse.com</c> 域的 Cookie <c>HYV_LOGIN_PLATFORM_ACTION_TICKET_INFO</c>，再打开
/// <c>login-platform/index.html?...#/security-verification</c>；页面完成后向父页面 postMessage
/// <c>login-platform:verify-success</c>，关闭时发 <c>login-platform:close</c>。
/// SDK 是在 iframe 里打开的，这里页面直接作为顶层页面，父窗口就是它自己，所以把 st 设成同源，
/// 再注入脚本把收到的消息转给宿主。
/// </remarks>
internal static class HoyoverseIdentityVerifyPopup
{

    private const string LoginPlatformUrl = "https://account.hoyoverse.com/login-platform/index.html";

    private const string ActionTicketCookieName = "HYV_LOGIN_PLATFORM_ACTION_TICKET_INFO";

    private const string ActionTicketCookieDomain = ".hoyoverse.com";

    private const string VerifySuccessMessage = "login-platform:verify-success";

    private const string CloseMessage = "login-platform:close";

    private const string ErrorMessage = "login-platform:error";

    /// <summary>把页面发给「父窗口」的消息转交宿主，只转账号中心自己的消息类型。</summary>
    private const string BridgeScript = """
        (function () {
            if (window.__mwVerifyBridge) { return; }
            window.__mwVerifyBridge = true;
            window.addEventListener('message', function (e) {
                try {
                    var d = e.data;
                    if (typeof d === 'string') { try { d = JSON.parse(d); } catch (_) { return; } }
                    if (d && typeof d.type === 'string' && d.type.indexOf('login-platform:') === 0) {
                        window.chrome.webview.postMessage(JSON.stringify({ type: d.type }));
                    }
                } catch (_) { }
            });
        })();
        """;


    /// <summary>
    /// 显示身份验证页。
    /// </summary>
    /// <param name="xamlRoot">宿主 XamlRoot（通常为登录对话框）。</param>
    /// <param name="actionTicket">风控详情里的 ticket。</param>
    /// <param name="appId">发起登录的 app_id，与登录请求一致。</param>
    /// <param name="gameBiz">发起登录的 game_biz，与登录请求一致。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>验证成功为 true；取消、关闭或出错为 false。</returns>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, string actionTicket, string appId, string gameBiz, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        if (string.IsNullOrWhiteSpace(actionTicket))
        {
            return false;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetResult(false));

        var webView = new WebView2
        {
            Width = 440,
            Height = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var title = new TextBlock
        {
            Text = Lang.PasswordLogin_IdentityVerifyTitle,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var cancelButton = new Button
        {
            Content = Lang.Common_Cancel,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 88,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var card = new Border
        {
            Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            BorderThickness = new Thickness(0),
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { title, webView, cancelButton },
            },
        };

        var root = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0, 0, 0)),
        };
        root.Children.Add(new Border
        {
            Child = card,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
        });

        var popup = new Popup
        {
            XamlRoot = xamlRoot,
            Child = root,
            IsLightDismissEnabled = false,
        };

        void SizeToRoot()
        {
            try
            {
                var size = xamlRoot.Size;
                root.Width = size.Width;
                root.Height = size.Height;
            }
            catch { }
        }
        SizeToRoot();
        xamlRoot.Changed += OnXamlRootChanged;

        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => SizeToRoot();

        void Complete(bool result)
        {
            xamlRoot.Changed -= OnXamlRootChanged;
            try { popup.IsOpen = false; } catch { }
            tcs.TrySetResult(result);
        }

        cancelButton.Click += (_, _) => Complete(false);

        void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string? type = JsonNode.Parse(args.TryGetWebMessageAsString())?["type"]?.GetValue<string>();
                if (type is VerifySuccessMessage)
                {
                    Complete(true);
                }
                else if (type is CloseMessage or ErrorMessage)
                {
                    Complete(false);
                }
            }
            catch (JsonException) { }
        }

        popup.IsOpen = true;

        CoreWebView2? core = null;
        try
        {
            await webView.EnsureCoreWebView2Async();
            core = webView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.WebMessageReceived += OnMessage;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);

            var context = new JsonObject
            {
                ["actionType"] = "verify_for_component",
                ["actionTicket"] = actionTicket,
                ["meta"] = new JsonObject(),
            };
            // js-cookie 写入时按 encodeURIComponent 编码，读取时解码
            var cookie = core.CookieManager.CreateCookie(ActionTicketCookieName, Uri.EscapeDataString(context.ToJsonString()), ActionTicketCookieDomain, "/");
            cookie.IsSecure = true;
            core.CookieManager.AddOrUpdateCookie(cookie);

            bool dark = (xamlRoot.Content as FrameworkElement)?.ActualTheme is ElementTheme.Dark;
            core.Navigate(BuildUrl(appId, gameBiz, dark));
            return await tcs.Task;
        }
        catch
        {
            Complete(false);
            return await tcs.Task;
        }
        finally
        {
            if (core is not null)
            {
                try
                {
                    core.WebMessageReceived -= OnMessage;
                    // 票据一次性，用完即删，免得残留在共享的 WebView2 配置里
                    core.CookieManager.DeleteCookies(ActionTicketCookieName, "https://account.hoyoverse.com");
                }
                catch { }
            }
            WebView2Helper.Close(webView);
            try { popup.IsOpen = false; } catch { }
            xamlRoot.Changed -= OnXamlRootChanged;
        }
    }


    /// <summary>
    /// 拼账号中心安全验证页地址，参数与账号 SDK 打开 iframe 时一致（callback_type=WEB、ux_mode=popup、iframe_level=2）。
    /// </summary>
    /// <param name="appId">app_id。</param>
    /// <param name="gameBiz">game_biz。</param>
    /// <param name="dark">是否深色主题。</param>
    /// <returns>完整地址。</returns>
    private static string BuildUrl(string appId, string gameBiz, bool dark)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            // st 是「父页面」地址，页面按它的源 postMessage；顶层打开时父页面就是自己，必须同源才收得到
            new("st", "https://account.hoyoverse.com/"),
            new("client_type", "3"),
            new("callback_type", "WEB"),
            new("app_id", appId),
            new("game_biz", gameBiz),
            new("lang", LanguageUtil.FilterLanguage(CultureInfo.CurrentUICulture.Name)),
            new("iframe_level", "2"),
            new("theme", dark ? "dark" : "light"),
            new("ux_mode", "popup"),
        };
        string queryString = string.Join("&", query.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
        return $"{LoginPlatformUrl}?{queryString}#/security-verification";
    }

}
