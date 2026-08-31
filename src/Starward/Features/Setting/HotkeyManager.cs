using Serilog;
using Starward.Helpers;
using System;
using System.Diagnostics;
using Vanara.PInvoke;

namespace Starward.Features.Setting;

internal static class HotkeyManager
{


    public static HotkeyInfo ShowMainWindow { get; private set; } = new HotkeyInfo(nameof(AppConfig.ShowMainWindowHotkey), nameof(AppConfig.EnableShowMainWindowHotkey), 44444, User32.HotKeyModifiers.MOD_ALT, User32.VK.VK_S);


    public static HotkeyInfo ScreenshotCapture { get; private set; } = new HotkeyInfo(nameof(AppConfig.ScreenshotCaptureHotkey), nameof(AppConfig.EnableScreenshotCaptureHotkey), 44445, User32.HotKeyModifiers.MOD_ALT, User32.VK.VK_D);


    /// <summary>
    /// 承载全局热键的窗口句柄。由 <see cref="Initialize"/> 一次性设定为系统托盘窗口，
    /// 之后设置页改键也一律注册到它上面。
    /// <para>
    /// 必须收口在这里：热键宿主一旦分裂（例如设置页改用主窗口句柄注册），主窗口关到托盘后热键就会失效，
    /// 且 <c>UnregisterHotKey</c> 会打在错误的窗口上，表现为「改完快捷键反而不灵」。
    /// </para>
    /// </summary>
    public static nint OwnerHandle { get; private set; }


    /// <summary>全部全局热键，供初始化遍历。</summary>
    private static HotkeyInfo[] AllHotkeys => [ShowMainWindow, ScreenshotCapture];


    /// <summary>
    /// 指定热键是否启用（系统托盘菜单可逐个切换）。关闭时不向系统注册它，但按键配置保留。
    /// </summary>
    /// <param name="id">热键 Id，见 <see cref="GetHotkeyInfo"/>。</param>
    public static bool IsEnabled(int id)
    {
        return GetHotkeyInfo(id) is HotkeyInfo info && IsEnabled(info);
    }


    private static bool IsEnabled(HotkeyInfo info)
    {
        return AppConfig.GetValue(true, info.EnabledConfigSetting);
    }


    /// <summary>
    /// 指定热键当前按键的可读文本（如 <c>Alt + D</c>），供托盘菜单等处展示。
    /// </summary>
    /// <param name="id">热键 Id，见 <see cref="GetHotkeyInfo"/>。</param>
    /// <returns>按键文本；未设置或已被删除时返回 <c>"—"</c>。</returns>
    public static string GetHotkeyText(int id)
    {
        if (GetHotkeyInfo(id) is HotkeyInfo info)
        {
            string? text = HotkeyInput.GetHotkeyText((uint)info.Modifiers, (uint)info.Key);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        // 用户在设置页删掉了按键：不本地化，一个破折号即可表达「未设置」
        return "—";
    }


    /// <summary>
    /// 绑定热键宿主窗口，从配置载入各热键的按键，并在总开关打开时注册。由系统托盘窗口在构造时调用，幂等。
    /// </summary>
    /// <param name="ownerHandle">承载热键的窗口句柄，须与进程生命周期等长。</param>
    public static void Initialize(nint ownerHandle)
    {
        if (OwnerHandle != 0)
        {
            return;
        }
        OwnerHandle = ownerHandle;
        try
        {
            foreach (var item in AllHotkeys)
            {
                // 无论总开关是否打开都要载入按键：设置页要显示当前按键，重新打开开关时也直接用它注册
                LoadHotkeyFromConfig(item);
                RegisterAndReportFailure(item);
            }
        }
        catch (Exception ex)
        {
            Debug.Write(ex);
        }
    }


    /// <summary>
    /// 单独启用/禁用某个全局热键：打开则按其已保存的按键注册，关闭则注销。配置随即持久化。
    /// </summary>
    /// <param name="id">热键 Id，见 <see cref="GetHotkeyInfo"/>。</param>
    /// <param name="enabled">是否启用。</param>
    public static void SetEnabled(int id, bool enabled)
    {
        if (GetHotkeyInfo(id) is not HotkeyInfo info || IsEnabled(info) == enabled)
        {
            return;
        }
        AppConfig.SetValue(enabled, info.EnabledConfigSetting);
        try
        {
            if (enabled)
            {
                RegisterAndReportFailure(info);
            }
            else
            {
                UnregisterHotkey(id);
            }
            Log.Information("Hotkey {name} {state}", info.ConfigSetting, enabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Toggle hotkey {name} to {enabled}", info.ConfigSetting, enabled);
        }
    }


    /// <summary>
    /// 从设置读出该热键的按键写入 <paramref name="item"/>；没有存过则用默认值。不做注册。
    /// </summary>
    private static void LoadHotkeyFromConfig(HotkeyInfo item)
    {
        User32.HotKeyModifiers modifiers = User32.HotKeyModifiers.MOD_NONE;
        User32.VK key = 0;
        string? hotkey = AppConfig.GetValue<string>(null, item.ConfigSetting);
        var mk = GetModifiersKey(hotkey);
        if (mk.HasValue)
        {
            if (HotkeyInput.IsHotkeyAvaliable((uint)mk.Value.Modifiers, (uint)mk.Value.Key))
            {
                modifiers = mk.Value.Modifiers;
                key = mk.Value.Key;
            }
        }
        else
        {
            modifiers = item.DefaultModifiers;
            key = item.DefaultKey;
        }
        item.Modifiers = modifiers;
        item.Key = key;
    }


    /// <summary>
    /// 按 <paramref name="item"/> 当前按键注册，失败时记日志并（有主窗口时）弹提示。
    /// </summary>
    private static void RegisterAndReportFailure(HotkeyInfo item)
    {
        Win32Error error = RegisterHotkey(item.Id, item.Modifiers, item.Key);
        if (error.Succeeded)
        {
            return;
        }
        string? hotkey = HotkeyInput.GetHotkeyText((uint)item.Modifiers, (uint)item.Key);
        // 仅托盘驻留时没有主窗口，InAppToast.MainWindow 为 null，提示会静默丢失，
        // 故一律记日志兜底；用户打开设置页时 InitializeHotkeyInput 也会把该项标成警告态。
        Log.Warning("Register hotkey {hotkey} failed: {error}", hotkey, error);
        if (error == Win32Error.ERROR_HOTKEY_ALREADY_REGISTERED)
        {
            InAppToast.MainWindow?.Warning(null, string.Format(Lang.HotkeyManager_TheShortcutKeys0IsAlreadyInUsePleaseModifyItInSettingsPage, hotkey), 0);
        }
        else
        {
            InAppToast.MainWindow?.Warning(null, string.Format(Lang.HotkeyManager_FailedToRegisterTheShortcutKeys0PleaseRetryInSettingsPage, hotkey), 0);
        }
    }



    /// <summary>
    /// 在热键宿主窗口（<see cref="OwnerHandle"/>）上注册指定热键，并持久化到设置。
    /// </summary>
    /// <param name="id">热键 Id，见 <see cref="GetHotkeyInfo"/>。</param>
    /// <param name="modifiers">修饰键。</param>
    /// <param name="key">主键。</param>
    /// <returns>注册结果；宿主未就绪时返回 <see cref="Win32Error.ERROR_INVALID_WINDOW_HANDLE"/>。</returns>
    public static Win32Error RegisterHotkey(int id, User32.HotKeyModifiers modifiers, User32.VK key)
    {
        if (OwnerHandle == 0)
        {
            return Win32Error.ERROR_INVALID_WINDOW_HANDLE;
        }
        if (GetHotkeyInfo(id) is HotkeyInfo info)
        {
            if (info.IsRegistered)
            {
                return Win32Error.ERROR_SUCCESS;
            }

            if (modifiers == 0 && key == 0)
            {
                return Win32Error.ERROR_SUCCESS;
            }

            // 该热键被单独关掉：只把按键存下来，不向系统注册。用户在设置页改键仍然生效（重新打开时按新键注册），
            // 也不该因为「当前没启用」就报注册失败。
            if (!IsEnabled(info))
            {
                if (info.Modifiers != modifiers || info.Key != key)
                {
                    AppConfig.SetValue($"{(uint)modifiers}+{(uint)key}", info.ConfigSetting);
                }
                info.Modifiers = modifiers;
                info.Key = key;
                info.IsRegistered = false;
                info.Error = Win32Error.ERROR_SUCCESS;
                return Win32Error.ERROR_SUCCESS;
            }
            User32.RegisterHotKey(OwnerHandle, id, modifiers | User32.HotKeyModifiers.MOD_NOREPEAT, (uint)key);
            Win32Error error = Kernel32.GetLastError();
            if (error.Succeeded && (info.Modifiers != modifiers || info.Key != key))
            {
                AppConfig.SetValue($"{(uint)modifiers}+{(uint)key}", info.ConfigSetting);
            }
            info.Modifiers = modifiers;
            info.Key = key;
            info.IsRegistered = error.Succeeded;
            info.Error = error;
            return error;
        }
        else
        {
            return Win32Error.ERROR_BAD_ARGUMENTS;
        }
    }


    /// <summary>
    /// 从热键宿主窗口注销指定热键（保留其配置，供随后重新注册）。
    /// </summary>
    /// <param name="id">热键 Id。</param>
    public static Win32Error UnregisterHotkey(int id)
    {
        User32.UnregisterHotKey(OwnerHandle, id);
        Win32Error error = Kernel32.GetLastError();
        if (GetHotkeyInfo(id) is HotkeyInfo info)
        {
            info.IsRegistered = false;
            info.Error = Win32Error.ERROR_SUCCESS;
        }
        return error;
    }


    /// <summary>
    /// 注销指定热键并清空其配置（用户在设置页点了删除）。
    /// </summary>
    /// <param name="id">热键 Id。</param>
    public static Win32Error DeleteHotkey(int id)
    {
        User32.UnregisterHotKey(OwnerHandle, id);
        Win32Error error = Kernel32.GetLastError();
        if (GetHotkeyInfo(id) is HotkeyInfo info)
        {
            info.Modifiers = 0;
            info.Key = 0;
            info.IsRegistered = false;
            info.Error = Win32Error.ERROR_SUCCESS;
            AppConfig.SetValue("0", info.ConfigSetting);
        }
        return error;
    }


    public static void InitializeHotkeyInput(HotkeyInput hotkeyInput)
    {
        if (GetHotkeyInfo(hotkeyInput.HotkeyId) is HotkeyInfo info)
        {
            hotkeyInput.SetHotkey((uint)info.Modifiers, (uint)info.Key);
            hotkeyInput.State = info.Error.Succeeded ? HoykeyInputState.None : HoykeyInputState.Warning;
        }
    }


    private static (User32.HotKeyModifiers Modifiers, User32.VK Key)? GetModifiersKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (value.Trim() is "0")
        {
            return (0, 0);
        }
        string[] splits = value.Split('+');
        if (splits.Length == 2)
        {
            if (uint.TryParse(splits[0].Trim(), out uint modifiers) && uint.TryParse(splits[1].Trim(), out uint key))
            {
                return ((User32.HotKeyModifiers)modifiers, (User32.VK)key);
            }
        }
        return null;
    }




    public static HotkeyInfo? GetHotkeyInfo(int id)
    {
        return id switch
        {
            44444 => ShowMainWindow,
            44445 => ScreenshotCapture,
            _ => null,
        };
    }




    public class HotkeyInfo
    {

        /// <summary>按键配置在 <see cref="AppConfig"/> 中的键名（形如 <c>"{modifiers}+{key}"</c>）。</summary>
        public string ConfigSetting { get; init; }

        /// <summary>「是否启用」配置在 <see cref="AppConfig"/> 中的键名，默认视为 <see langword="true"/>。</summary>
        public string EnabledConfigSetting { get; init; }

        public int Id { get; init; }

        public User32.HotKeyModifiers Modifiers { get; set; }

        public User32.VK Key { get; set; }

        public bool IsRegistered { get; set; }

        public Win32Error Error { get; set; }

        public User32.HotKeyModifiers DefaultModifiers { get; init; }

        public User32.VK DefaultKey { get; init; }


        public HotkeyInfo(string configSetting, string enabledConfigSetting, int id, User32.HotKeyModifiers defaultModifiers, User32.VK defaultKey)
        {
            ConfigSetting = configSetting;
            EnabledConfigSetting = enabledConfigSetting;
            Id = id;
            DefaultModifiers = defaultModifiers;
            DefaultKey = defaultKey;
        }


    }





}
