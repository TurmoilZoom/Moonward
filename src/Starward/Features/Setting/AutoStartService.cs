using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Features.Startup;
using System;

namespace Starward.Features.Setting;

/// <summary>
/// 管理「登录 Windows 后自动启动」：读写当前用户 Run 键，并以系统实际注册结果为准。
/// 不写 <c>HKLM</c>、不创建计划任务，因此开关与开机均不触发 UAC。
/// </summary>
internal class AutoStartService
{

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string RunKeyFullPath = @"HKEY_CURRENT_USER\" + RunKeyPath;

    private const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// Windows Run 值命令行上限（含引号与参数）。
    /// </summary>
    private const int MaxRunCommandLength = 260;

    /// <summary>启用态：首字节偶数，常见为 0x02。</summary>
    private static readonly byte[] StartupApprovedEnabled = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];


    /// <summary>
    /// 正式版与 Debug 互不覆盖，避免并排安装时抢同一条启动项。
    /// </summary>
#if DEBUG
    internal const string ValueName = "Moonward.Debug";
#else
    internal const string ValueName = "Moonward";
#endif


    /// <summary>
    /// 可移动存储上的便携包开机时盘可能不在，不允许注册。
    /// </summary>
    public static bool IsAvailable => !AppConfig.IsAppInRemovableStorage;


    /// <summary>
    /// 是否已在系统中生效：Run 值存在，且未被任务管理器 /「启动」页禁用。
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(ReadCommand()) && !IsDisabledByWindows();
        }
        catch (Exception ex)
        {
            AppConfig.GetLogger<AutoStartService>().LogWarning(ex, "Read start-at-login state");
            return false;
        }
    }


    /// <summary>
    /// 写入或覆盖开机启动项（始终带 <c>--hide</c>，仅启动托盘）。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前不可用，或命令行超过系统限制。</exception>
    public static void Enable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Start at login is not available on removable storage.");
        }

        string command = BuildCommand();
        if (command.Length > MaxRunCommandLength)
        {
            throw new InvalidOperationException("Start-at-login command exceeds the 260-character Run key limit.");
        }

        Registry.SetValue(RunKeyFullPath, ValueName, command, RegistryValueKind.String);
        using RegistryKey approved = Registry.CurrentUser.CreateSubKey(StartupApprovedKeyPath);
        approved.SetValue(ValueName, StartupApprovedEnabled, RegistryValueKind.Binary);
    }


    /// <summary>
    /// 删除本应用写入的 Run 与 StartupApproved 值。键不存在时忽略。
    /// </summary>
    public static void Disable()
    {
        DeleteValue(RunKeyPath, ValueName);
        DeleteValue(StartupApprovedKeyPath, ValueName);
    }


    /// <summary>
    /// 注册仍在且未被系统禁用时，把命令同步为当前 exe + <c>--hide</c>；用户已删除/禁用则不动。
    /// </summary>
    public static void RepairIfNeeded()
    {
        try
        {
            if (!IsAvailable)
            {
                return;
            }
            string? command = ReadCommand();
            if (command is null || IsDisabledByWindows())
            {
                return;
            }
            string expected = BuildCommand();
            if (!string.Equals(command, expected, StringComparison.OrdinalIgnoreCase))
            {
                Enable();
            }
        }
        catch (Exception ex)
        {
            AppConfig.GetLogger<AutoStartService>().LogWarning(ex, "Repair start-at-login registration");
        }
    }


    /// <summary>
    /// 卸载时移除正式版与 Debug 的开机启动项。静默、不抛异常。
    /// 与「卸载时删除数据」无关：exe 已不在，残留启动项只会在下次登录失败。
    /// </summary>
    public static void RemoveRegistration()
    {
        try
        {
            foreach (string name in (ReadOnlySpan<string>)["Moonward", "Moonward.Debug"])
            {
                DeleteValue(RunKeyPath, name);
                DeleteValue(StartupApprovedKeyPath, name);
            }
        }
        catch
        {
        }
    }


    private static string BuildCommand()
    {
        return $"\"{AppConfig.MoonwardExecutePath}\" {StartupVerbs.Hide}";
    }


    private static string? ReadCommand()
    {
        return Registry.GetValue(RunKeyFullPath, ValueName, null) as string is { Length: > 0 } command
            ? command
            : null;
    }


    /// <summary>
    /// 任务管理器 / 系统「启动」页禁用后，StartupApproved 首字节为奇数。
    /// 无此值视为未禁用。
    /// </summary>
    private static bool IsDisabledByWindows()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
    }


    private static void DeleteValue(string keyPath, string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

}
