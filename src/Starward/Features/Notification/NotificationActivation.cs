using Starward.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Starward.Features.Notification;

/// <summary>
/// 系统通知点击后的协议地址：生成与解析 <c>{scheme}://open/?biz={game_biz}&amp;uid={uid}</c>（打开主窗口）
/// 与 <c>{scheme}://copy/?text={text}</c>（通知按钮：复制文本，如兑换码）。
/// </summary>
/// <remarks>
/// 系统激活时会把 <c>scheme://open?x</c> 规范化成 <c>scheme://open/?x</c>（实测），这里生成时就带上斜杠，解析时两种都认。
/// </remarks>
internal static class NotificationActivation
{

    private const string OpenHost = "open";

    private const string CopyHost = "copy";


    /// <summary>
    /// 生成「打开主窗口并切到某游戏」的激活地址。
    /// </summary>
    /// <param name="biz">要切到的游戏区服，空则只打开主窗口。</param>
    /// <param name="uid">触发通知的角色 uid，0 表示不指定。</param>
    /// <returns>协议激活地址。</returns>
    public static string BuildOpenUri(GameBiz? biz, long uid)
    {
        string query = $"biz={Uri.EscapeDataString(biz?.ToString() ?? "")}&uid={uid.ToString(CultureInfo.InvariantCulture)}";
        return $"{SystemNotificationService.ActivationScheme}://{OpenHost}/?{query}";
    }


    /// <summary>
    /// 启动参数是否为系统通知的点击激活。
    /// </summary>
    /// <param name="argument">命令行首个参数。</param>
    /// <returns>是通知激活地址返回 <see langword="true"/>。</returns>
    public static bool IsActivationArgument(string? argument)
    {
        // 带上冒号比较：Debug 的 moonward-notify-dev 与安装版的 moonward-notify 前缀相同
        return argument?.StartsWith(SystemNotificationService.ActivationScheme + ":", StringComparison.OrdinalIgnoreCase) ?? false;
    }


    /// <summary>
    /// 解析「打开主窗口」激活地址。
    /// </summary>
    /// <param name="argument">命令行首个参数。</param>
    /// <param name="biz">要切到的游戏区服；未指定或无法识别时为 <see langword="null"/>。</param>
    /// <param name="uid">角色 uid；未指定时为 0。</param>
    /// <returns>是「打开」地址返回 <see langword="true"/>。</returns>
    public static bool TryParseOpen(string argument, out GameBiz? biz, out long uid)
    {
        biz = null;
        uid = 0;
        if (!TryParse(argument, OpenHost, out Uri? uri))
        {
            return false;
        }
        foreach ((string key, string value) in ParseQuery(uri))
        {
            if (key.Equals("biz", StringComparison.OrdinalIgnoreCase) && GameBiz.TryParse(value, out GameBiz parsed))
            {
                biz = parsed;
            }
            else if (key.Equals("uid", StringComparison.OrdinalIgnoreCase))
            {
                _ = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uid);
            }
        }
        return true;
    }


    /// <summary>
    /// 生成「复制文本」的激活地址，给通知按钮用。
    /// </summary>
    /// <param name="text">要复制的文本。</param>
    /// <returns>协议激活地址。</returns>
    public static string BuildCopyUri(string text)
    {
        return $"{SystemNotificationService.ActivationScheme}://{CopyHost}/?text={Uri.EscapeDataString(text)}";
    }


    /// <summary>
    /// 解析「复制文本」激活地址。
    /// </summary>
    /// <param name="argument">命令行首个参数。</param>
    /// <param name="text">要复制的文本；地址里没有时为空串。</param>
    /// <returns>是「复制」地址返回 <see langword="true"/>。</returns>
    public static bool TryParseCopy(string argument, out string text)
    {
        text = "";
        if (!TryParse(argument, CopyHost, out Uri? uri))
        {
            return false;
        }
        foreach ((string key, string value) in ParseQuery(uri))
        {
            if (key.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                text = value;
            }
        }
        return true;
    }


    /// <summary>
    /// 是本应用通知协议、且主机名为 <paramref name="host"/> 的地址。
    /// </summary>
    private static bool TryParse(string argument, string host, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        return IsActivationArgument(argument)
            && Uri.TryCreate(argument, UriKind.Absolute, out uri)
            && string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 拆出查询串里的键值对（值已反转义）。
    /// </summary>
    private static IEnumerable<(string Key, string Value)> ParseQuery(Uri uri)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int index = pair.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }
            yield return (pair[..index], Uri.UnescapeDataString(pair[(index + 1)..]));
        }
    }

}
