using Microsoft.Win32;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Starward.Features.Setting;

/// <summary>
/// 应用卸载相关的数据清理。
/// <para>
/// Velopack（控制面板卸载或自身 <c>Update.exe uninstall</c>）只会移除 <c>current\</c>、<c>Update.exe</c>、快捷方式与它自己注册的卸载项，
/// <b>不会</b>触碰用户数据目录（<c>&lt;所选目录&gt;\data</c>）、Moonward 自己的注册表键（<c>HKCU\Software\Moonward</c>）、
/// <c>moonward://</c> 协议注册（<c>HKCU\Software\Classes\Moonward</c>），以及开机启动 Run 键。
/// </para>
/// <para>
/// 本类负责清理这三处残留。是否清理由注册表标记 <see cref="FlagValueName"/> 控制，
/// <b>默认开启</b>（标记缺省即视为删除）：控制面板直接卸载也会清理。用户可在「文件管理」设置中关闭，
/// 关闭后（标记显式写 0）卸载时三处全部保留，方便重装后无缝续用。
/// 清理流程在 Velopack 卸载钩子 <c>OnBeforeUninstallFastCallback</c> 中调用 <see cref="PerformUninstallCleanup"/>。
/// </para>
/// <para>
/// ⚠️ 卸载钩子有 <b>30 秒超时</b>且<b>不允许任何 UI</b>（见 https://docs.velopack.io/integrating/hooks ），
/// 故 <see cref="PerformUninstallCleanup"/> 全程静默、尽快返回，异常一律吞掉，仅写一份诊断日志到 %TEMP%。
/// </para>
/// </summary>
internal static class AppUninstallService
{

#if DEBUG
    private const string RegistryKeyPath = @"Software\Moonward.Debug";
#else
    private const string RegistryKeyPath = @"Software\Moonward";
#endif

    /// <summary>
    /// <c>moonward://</c> 协议注册键（与 <see cref="UrlProtocol.UrlProtocolService"/> 一致，不分调试/正式）。
    /// </summary>
    private const string ProtocolKeyPath = @"Software\Classes\Moonward";

    /// <summary>
    /// 注册表标记：卸载时是否一并删除用户数据（DWORD）。与其它配置同存于 <see cref="RegistryKeyPath"/>。
    /// <b>语义为默认开启</b>：标记缺省或非 0 都视为删除，仅显式写入 0 时保留。
    /// </summary>
    private const string FlagValueName = "DeleteDataOnUninstall";


    /// <summary>
    /// 读取「卸载时删除数据」开关。默认开启：标记缺省（从未设置）时返回 <see langword="true"/>。
    /// </summary>
    public static bool GetDeleteDataOnUninstall()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            // 仅当显式写入 0 时才表示「保留」，其余（缺省 / 非 0）一律视为删除。
            return key?.GetValue(FlagValueName) is int i ? i != 0 : true;
        }
        catch
        {
            return true;
        }
    }


    /// <summary>
    /// 记录用户在设置中的选择（卸载时是否删除数据），供后续 Velopack 卸载钩子读取。
    /// </summary>
    public static void SetDeleteDataOnUninstall(bool value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(FlagValueName, value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }


    /// <summary>
    /// 在 Velopack 卸载钩子（<c>OnBeforeUninstallFastCallback</c>）中调用：
    /// 根据 <see cref="FlagValueName"/> 标记决定是否删除用户数据目录、<c>moonward://</c> 协议键与 Moonward 注册表键。
    /// 开机启动 Run 键始终删除（与是否保留数据无关）。
    /// 必须保持静默、无 UI、尽快返回（钩子 30 秒超时）。
    /// </summary>
    public static void PerformUninstallCleanup()
    {
        try
        {
            // exe 已卸载，残留启动项只会在下次登录失败；与「保留数据」无关。
            AutoStartService.RemoveRegistration();

            bool deleteData;
            string? dataFolder = null;
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                // 默认开启：标记缺省或非 0 都视为删除；仅显式写入 0 时保留。
                deleteData = key?.GetValue(FlagValueName) is int i ? i != 0 : true;
                // 正式安装下注册表里的 DataFolder 为绝对路径（= 用户所选目录 \ data）。
                dataFolder = (key?.GetValue("DataFolder") as string)?.Trim();
            }

            if (!deleteData)
            {
                // 用户选择保留：数据目录、协议键与设置（含登录信息）都不动，方便重装续用。
                Log("Preserve user data (flag = 0). Start-at-login registration removed.");
                return;
            }

            Log($"Uninstall cleanup start: dataFolder='{dataFolder}'");
            TryDeleteUserDataFolder(dataFolder);
            // moonward:// 协议注册（HKCU\Software\Classes\Moonward），卸载后已无意义。
            TryDeleteSubKeyTree(Registry.CurrentUser, ProtocolKeyPath, "protocol key");
            // 删除 Moonward 自己的注册表键（含 DataFolder / 语言 / 登录票据 / 本标记），最后删（前面要读它）。
            TryDeleteSubKeyTree(Registry.CurrentUser, RegistryKeyPath, "config key");
            Log("Uninstall cleanup done.");
        }
        catch (Exception ex)
        {
            Log($"Uninstall cleanup failed: {ex.Message}");
        }
    }


    /// <summary>
    /// 尽力删除用户数据目录（<c>&lt;所选目录&gt;\data</c>）。带安全校验避免误删，并对短暂占用的文件做有限重试。
    /// </summary>
    private static void TryDeleteUserDataFolder(string? dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            return;
        }
        try
        {
            string full = Path.GetFullPath(dataFolder);
            if (!Directory.Exists(full))
            {
                return;
            }
            // 安全校验：拒绝删除驱动器根目录，且目录名必须是统一数据目录约定的 "data"，
            // 防止注册表损坏 / 被人为篡改时误删用户的其它文件。
            if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                              Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            {
                Log($"Refuse to delete drive root: '{full}'");
                return;
            }
            if (!string.Equals(new DirectoryInfo(full).Name, AppConfig.DataSubFolderName, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Refuse to delete non-data folder: '{full}'");
                return;
            }

            // 数据库 / webview 等可能在主进程刚退出时仍短暂被占用，做有限重试（总耗时控制在钩子 30 秒预算内）。
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(full, recursive: true);
                    return;
                }
                catch (Exception) when (attempt < 4)
                {
                    Thread.Sleep(1500);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Delete data folder failed: {ex.Message}");
        }
    }


    /// <summary>
    /// 尽力删除指定注册表子键树，键不存在时静默忽略，异常仅记日志。
    /// </summary>
    private static void TryDeleteSubKeyTree(RegistryKey root, string subKey, string label)
    {
        try
        {
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Log($"Delete {label} '{subKey}' failed: {ex.Message}");
        }
    }


    private static void Log(string message)
    {
        try
        {
            string file = Path.Combine(Path.GetTempPath(), "Moonward.Uninstall.log");
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

}
