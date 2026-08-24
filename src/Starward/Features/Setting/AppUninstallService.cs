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
/// 本类负责清理这些残留。卸载策略<b>固定</b>：清空用户数据目录，<b>仅保留数据库 db 文件</b>
/// （<c>StarwardDatabase.db</c> 及其 <c>-wal</c>/<c>-shm</c> 边车），方便用户重装后手动导入历史记录；
/// 同时删除 <c>moonward://</c> 协议键、Moonward 配置键与开机启动项。清理流程在 Velopack 卸载钩子
/// <c>OnBeforeUninstallFastCallback</c> 中调用 <see cref="PerformUninstallCleanup"/>。
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
    /// 数据库主文件名（位于统一数据目录根下）。卸载时保留它及其 WAL/SHM 边车，其余数据全部清除。
    /// </summary>
    private const string DatabaseFileName = "StarwardDatabase.db";


    /// <summary>
    /// 在 Velopack 卸载钩子（<c>OnBeforeUninstallFastCallback</c>）中调用：
    /// 清空用户数据目录但保留数据库 db 文件，并删除 <c>moonward://</c> 协议键、Moonward 配置键与开机启动项。
    /// 必须保持静默、无 UI、尽快返回（钩子 30 秒超时）。
    /// </summary>
    public static void PerformUninstallCleanup()
    {
        try
        {
            // exe 已卸载，残留启动项只会在下次登录失败，始终移除。
            AutoStartService.RemoveRegistration();

            string? dataFolder;
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                // 正式安装下注册表里的 DataFolder 为绝对路径（= 用户所选目录 \ data）。
                dataFolder = (key?.GetValue("DataFolder") as string)?.Trim();
            }

            Log($"Uninstall cleanup start: dataFolder='{dataFolder}'");
            // 固定策略：仅保留数据库 db 文件，清空数据目录其余内容。
            TryCleanUserDataFolderKeepDatabase(dataFolder);
            // moonward:// 协议注册（HKCU\Software\Classes\Moonward），卸载后已无意义。
            TryDeleteSubKeyTree(Registry.CurrentUser, ProtocolKeyPath, "protocol key");
            // 删除 Moonward 自己的注册表键（含 DataFolder / 语言 / 登录票据），最后删（前面要读它）。
            TryDeleteSubKeyTree(Registry.CurrentUser, RegistryKeyPath, "config key");
            Log("Uninstall cleanup done.");
        }
        catch (Exception ex)
        {
            Log($"Uninstall cleanup failed: {ex.Message}");
        }
    }


    /// <summary>
    /// 尽力清空用户数据目录（<c>&lt;所选目录&gt;\data</c>），<b>仅保留数据库 db 文件</b>
    /// （<c>StarwardDatabase.db</c> 及其 <c>-wal</c>/<c>-shm</c> 边车，确保保留下来的数据库完整可用）。
    /// 带安全校验避免误删，并对短暂占用的文件/目录做有限重试。
    /// </summary>
    private static void TryCleanUserDataFolderKeepDatabase(string? dataFolder)
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
            // 安全校验：拒绝清理驱动器根目录，且目录名必须是统一数据目录约定的 "data"，
            // 防止注册表损坏 / 被人为篡改时误删用户的其它文件。
            if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                              Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            {
                Log($"Refuse to clean drive root: '{full}'");
                return;
            }
            if (!string.Equals(new DirectoryInfo(full).Name, AppConfig.DataSubFolderName, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Refuse to clean non-data folder: '{full}'");
                return;
            }

            var dir = new DirectoryInfo(full);
            // 删除全部子目录（bg / cache / webview / log / game / DatabaseBackup 等）。
            foreach (DirectoryInfo sub in dir.GetDirectories())
            {
                TryDeleteDirectory(sub);
            }
            // 删除除数据库 db 文件外的所有文件。
            foreach (FileInfo file in dir.GetFiles())
            {
                if (IsDatabaseFileToKeep(file.Name))
                {
                    continue;
                }
                TryDeleteFile(file);
            }
        }
        catch (Exception ex)
        {
            Log($"Clean data folder failed: {ex.Message}");
        }
    }


    /// <summary>
    /// 是否为需要保留的数据库文件：主库 <see cref="DatabaseFileName"/> 及其 WAL/SHM 边车。
    /// 保留边车是为避免上次未做 WAL 检查点时丢失已提交但尚未落主库的数据。
    /// </summary>
    private static bool IsDatabaseFileToKeep(string fileName)
    {
        return fileName.Equals(DatabaseFileName, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(DatabaseFileName + "-wal", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(DatabaseFileName + "-shm", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 尽力递归删除目录，对短暂占用做有限重试（总耗时控制在钩子 30 秒预算内），异常仅记日志。
    /// </summary>
    private static void TryDeleteDirectory(DirectoryInfo dir)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                dir.Delete(recursive: true);
                return;
            }
            catch (Exception) when (attempt < 3)
            {
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                Log($"Delete directory '{dir.FullName}' failed: {ex.Message}");
                return;
            }
        }
    }


    /// <summary>
    /// 尽力删除单个文件，异常仅记日志。
    /// </summary>
    private static void TryDeleteFile(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (Exception ex)
        {
            Log($"Delete file '{file.FullName}' failed: {ex.Message}");
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
