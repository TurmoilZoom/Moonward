using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Windows.UI.Notifications;

namespace Starward.Features.Notification;

/// <summary>
/// Windows 系统通知（Toast）。用系统自带的 <see cref="ToastNotificationManager"/> 发送，通知身份（AUMID）与点击协议由本类自行登记在 HKCU。
/// <para>
/// 不用 WinAppSDK 的 <c>AppNotificationManager</c>：1.2 起它在提权进程里 <c>Show</c> 静默失败（WindowsAppSDK 上游 issue 3595），
/// 而免 UAC 快捷方式拉起的常驻实例、以管理员运行的 Moonward 都是提权的。系统通知平台本身不拦提权调用方（2026-11 在 Win11 26300 实测）。
/// </para>
/// <para>
/// 点击走协议激活：通知平台以普通权限拉起 <c>Moonward.exe "{scheme}://..."</c>，再由 <see cref="Startup.NotificationActivationStartupHandler"/>
/// 经 <see cref="Startup.ResidentInstanceMessenger"/> 交给常驻实例，提权与否走同一条路，不需要 COM 激活器。
/// 协议不用 <c>moonward://</c>：那个用户可以在高级设置里关掉。
/// </para>
/// </summary>
internal static class SystemNotificationService
{

#if DEBUG
    // Debug 版与安装版各用一套身份与协议，免得点安装版的通知拉起调试版（反之亦然）
    /// <summary>通知身份（AUMID）。</summary>
    public const string AppUserModelId = "Moonward.Dev";

    /// <summary>点击通知时激活用的协议名。</summary>
    public const string ActivationScheme = "moonward-notify-dev";

    private const string DisplayName = "Moonward Dev";
#else
    /// <summary>通知身份（AUMID）。</summary>
    public const string AppUserModelId = "Moonward";

    /// <summary>点击通知时激活用的协议名。</summary>
    public const string ActivationScheme = "moonward-notify";

    private const string DisplayName = "Moonward";
#endif

    private const string AppUserModelIdKeyPath = @"Software\Classes\AppUserModelId\" + AppUserModelId;

    private const string ActivationSchemeKeyPath = @"Software\Classes\" + ActivationScheme;

    /// <summary>系统为每个发过通知的应用建的设置键（开关、横幅、声音），只在卸载时清掉。</summary>
    private const string SystemSettingKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + AppUserModelId;

    /// <summary>ToastGeneric 主体最多显示 3 个文本元素（标题算一个），多出来的行并进最后一行。</summary>
    private const int MaxBodyLines = 2;

    /// <summary>ToastGeneric 最多 5 个按钮。</summary>
    private const int MaxButtons = 5;

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_NOT_FOUND)</c>：读取从没发过通知的身份的 <see cref="ToastNotifier.Setting"/> 时返回。</summary>
    private const int E_ELEMENT_NOT_FOUND = unchecked((int)0x80070490);

    private static readonly object _registerLock = new();


    /// <summary>
    /// 登记通知身份与点击协议；已登记且路径未变时不写注册表。每次发通知前调用，顺带自愈被删掉或安装位置变化后的残缺登记。
    /// </summary>
    public static void EnsureRegistered()
    {
        lock (_registerLock)
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "static", "logo.png");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AppUserModelIdKeyPath))
            {
                SetIfChanged(key, "DisplayName", DisplayName);
                if (File.Exists(icon))
                {
                    SetIfChanged(key, "IconUri", icon);
                }
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ActivationSchemeKeyPath))
            {
                SetIfChanged(key, "", $"URL:{DisplayName} Notification");
                SetIfChanged(key, "URL Protocol", "");
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ActivationSchemeKeyPath + @"\shell\open\command"))
            {
                SetIfChanged(key, "", $"\"{AppConfig.MoonwardExecutePath}\" \"%1\"");
            }
        }
    }


    /// <summary>
    /// 读取 Windows 对本应用通知的开关状态。
    /// </summary>
    /// <returns>系统设置状态；读取失败时为 <see langword="null"/>。</returns>
    public static NotificationSetting? GetSetting()
    {
        try
        {
            EnsureRegistered();
            return ToastNotificationManager.CreateToastNotifier(AppUserModelId).Setting;
        }
        catch (COMException ex) when (ex.HResult == E_ELEMENT_NOT_FOUND)
        {
            // 从没发过通知的身份，系统里还没有它的设置项；发过一条之后就能读到
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Get system notification setting");
            return null;
        }
    }


    /// <summary>
    /// 发送一条系统通知。
    /// </summary>
    /// <param name="notification">通知内容。</param>
    /// <returns>已交给系统返回 <see langword="true"/>；出错返回 <see langword="false"/>（已记日志）。系统关闭了本应用通知时仍返回 true。</returns>
    public static bool Show(SystemNotification notification)
    {
        try
        {
            EnsureRegistered();
            var document = new Windows.Data.Xml.Dom.XmlDocument();
            document.LoadXml(BuildXml(notification));
            var toast = new ToastNotification(document)
            {
                SuppressPopup = notification.SuppressPopup,
            };
            if (!string.IsNullOrWhiteSpace(notification.Tag))
            {
                toast.Tag = notification.Tag;
            }
            if (!string.IsNullOrWhiteSpace(notification.Group))
            {
                toast.Group = notification.Group;
            }
            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Show system notification (tag {tag}, group {group})", notification.Tag, notification.Group);
            return false;
        }
    }


    /// <summary>
    /// 删除通知身份、点击协议与系统为本应用建的通知设置键。只在卸载时调用：期间不碰 WinRT（卸载钩子里 COM 包装尚未初始化）。
    /// </summary>
    public static void RemoveRegistration()
    {
        Registry.CurrentUser.DeleteSubKeyTree(AppUserModelIdKeyPath, false);
        Registry.CurrentUser.DeleteSubKeyTree(ActivationSchemeKeyPath, false);
        Registry.CurrentUser.DeleteSubKeyTree(SystemSettingKeyPath, false);
    }


    /// <summary>
    /// 生成 ToastGeneric 通知的 XML。用 <see cref="XElement"/> 拼，文本里的 &amp;、&lt; 等由它负责转义。
    /// </summary>
    /// <param name="notification">通知内容。</param>
    /// <returns>Toast XML 字符串。</returns>
    internal static string BuildXml(SystemNotification notification)
    {
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
                                   new XElement("text", notification.Title));
        List<string> lines = notification.Lines.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (lines.Count > MaxBodyLines)
        {
            lines[MaxBodyLines - 1] = string.Join(Environment.NewLine, lines.Skip(MaxBodyLines - 1));
            lines.RemoveRange(MaxBodyLines, lines.Count - MaxBodyLines);
        }
        foreach (string line in lines)
        {
            binding.Add(new XElement("text", line));
        }
        if (!string.IsNullOrWhiteSpace(notification.Attribution))
        {
            binding.Add(new XElement("text", new XAttribute("placement", "attribution"), notification.Attribution));
        }
        var toast = new XElement("toast", new XElement("visual", binding));
        if (!string.IsNullOrWhiteSpace(notification.LaunchUri))
        {
            toast.Add(new XAttribute("activationType", "protocol"), new XAttribute("launch", notification.LaunchUri));
        }
        List<SystemNotificationButton> buttons = notification.Buttons.Where(x => !string.IsNullOrWhiteSpace(x.Content) && !string.IsNullOrWhiteSpace(x.ActivationUri))
                                                                     .Take(MaxButtons)
                                                                     .ToList();
        if (buttons.Count > 0)
        {
            // 按钮同样走协议激活：通知平台以普通权限拉起本程序，不需要 COM 激活器
            toast.Add(new XElement("actions", buttons.Select(x => new XElement("action",
                                                                               new XAttribute("content", x.Content),
                                                                               new XAttribute("activationType", "protocol"),
                                                                               new XAttribute("arguments", x.ActivationUri)))));
        }
        return toast.ToString(SaveOptions.DisableFormatting);
    }


    /// <summary>
    /// 值不同才写入，避免每次发通知都改写注册表。
    /// </summary>
    /// <param name="key">已打开的可写键。</param>
    /// <param name="name">值名，空串为默认值。</param>
    /// <param name="value">要写入的字符串。</param>
    private static void SetIfChanged(RegistryKey key, string name, string value)
    {
        if (!string.Equals(key.GetValue(name) as string, value, StringComparison.Ordinal))
        {
            key.SetValue(name, value);
        }
    }

}



/// <summary>
/// 一条系统通知的内容。
/// </summary>
internal sealed class SystemNotification
{

    /// <summary>标题（第一行，加粗）。</summary>
    public string Title { get; set; } = "";

    /// <summary>正文各行；超过两行时多出的并进第二行。</summary>
    public List<string> Lines { get; set; } = [];

    /// <summary>底部的小字来源行，可空。</summary>
    public string? Attribution { get; set; }

    /// <summary>点击通知时以协议激活的地址，空则点击只关闭通知。</summary>
    public string? LaunchUri { get; set; }

    /// <summary>同一 <see cref="Group"/> 内相同 Tag 的新通知会替换旧的，最长 64 字符。</summary>
    public string? Tag { get; set; }

    /// <summary>通知分组，最长 64 字符。</summary>
    public string? Group { get; set; }

    /// <summary>不弹出横幅，直接进通知中心。</summary>
    public bool SuppressPopup { get; set; }

    /// <summary>底部按钮，最多 5 个，多出的忽略。</summary>
    public List<SystemNotificationButton> Buttons { get; set; } = [];

}



/// <summary>
/// 系统通知底部的一个按钮，点击时以协议激活 <see cref="ActivationUri"/>。
/// </summary>
/// <param name="Content">按钮文字。</param>
/// <param name="ActivationUri">协议激活地址（见 <see cref="NotificationActivation"/>）。</param>
internal sealed record SystemNotificationButton(string Content, string ActivationUri);
