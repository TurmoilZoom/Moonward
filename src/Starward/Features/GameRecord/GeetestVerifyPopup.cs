using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Starward.Core.GameRecord.Passport;
using Starward.Language;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord;

/// <summary>
/// 独立人机验证弹层（Popup + 亚克力卡片），不复用登录 ContentDialog 内容区。
/// WinUI 同一 XamlRoot 无法叠两个 ContentDialog，故用 Popup 实现「另弹一层」。
/// </summary>
internal static class GeetestVerifyPopup
{

    /// <summary>
    /// 显示人机验证；成功返回 aigis 请求头，取消或失败返回 null。
    /// </summary>
    /// <param name="xamlRoot">宿主 XamlRoot（通常为登录对话框）。</param>
    /// <param name="aigis">服务端 aigis 载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>格式化 aigis 头；取消时为 null。</returns>
    public static async Task<string?> ShowAsync(XamlRoot xamlRoot, CaptchaAigis aigis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(aigis);
        if (string.IsNullOrWhiteSpace(aigis.Data))
        {
            return null;
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetResult(null));

        var webView = new WebView2
        {
            Width = 320,
            Height = 380,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var title = new TextBlock
        {
            Text = Lang.CaptchaLogin_GeetestTitle,
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

        // 半透明遮罩 + 居中卡片，视觉上为独立弹框
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

        // 铺满宿主视觉尺寸
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

        void Complete(string? result)
        {
            xamlRoot.Changed -= OnXamlRootChanged;
            try { popup.IsOpen = false; } catch { }
            tcs.TrySetResult(result);
        }

        cancelButton.Click += (_, _) => Complete(null);

        bool webMessageHooked = false;
        void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string message = args.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(message) || message == "cancel")
                {
                    Complete(null);
                    return;
                }
                Complete(MihoyoPassportClient.FormatAigisHeader(aigis, message));
            }
            catch
            {
                Complete(null);
            }
        }

        popup.IsOpen = true;

        try
        {
            await webView.EnsureCoreWebView2Async();
            var core = webView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.WebMessageReceived += OnMessage;
            webMessageHooked = true;
            core.NavigateToString(GeetestHelper.BuildHtml(aigis.Data, aigis.SessionId ?? ""));
            return await tcs.Task;
        }
        catch
        {
            Complete(null);
            return await tcs.Task;
        }
        finally
        {
            if (webMessageHooked)
            {
                try { webView.CoreWebView2.WebMessageReceived -= OnMessage; } catch { }
            }
            try { popup.IsOpen = false; } catch { }
            xamlRoot.Changed -= OnXamlRootChanged;
        }
    }

}
