using Microsoft.UI.Xaml.Controls;

namespace Starward.Helpers;

/// <summary>
/// WinUI 3 的 <see cref="WebView2"/> 从视觉树移除或窗口关闭时不会自动释放 Chromium 进程，
/// 须显式 <see cref="WebView2.Close"/>。事件处理会与 COM RCW 形成环，不 Close 只能等 GC + 终结器。
/// </summary>
internal static class WebView2Helper
{

    /// <summary>
    /// 关闭控件并释放 <c>msedgewebview2.exe</c>。尚未初始化或已关闭时 Close 可能抛错，一律忽略。
    /// </summary>
    public static void Close(WebView2? webView)
    {
        if (webView is null)
        {
            return;
        }
        try
        {
            webView.Close();
        }
        catch
        {
        }
    }


    /// <summary>
    /// await 期间宿主已关闭时补一次 <see cref="Close"/>：关闭发生在 <c>EnsureCoreWebView2Async</c>
    /// 尚未返回时，控件可能在关闭之后才拿到 CoreWebView2，只靠关闭路径那一次 Close 收不掉。
    /// </summary>
    /// <param name="webView">目标控件。</param>
    /// <param name="closed">宿主的已关闭标记。</param>
    /// <returns>已关闭返回 true，调用方应立即中止后续初始化。</returns>
    public static bool CloseIfRequested(WebView2? webView, bool closed)
    {
        if (!closed)
        {
            return false;
        }
        Close(webView);
        return true;
    }

}
