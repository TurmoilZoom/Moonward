using Microsoft.Win32;
using Starward.Features.Database;
using Starward.Features.ViewHost;
using Starward.Helpers;
using Starward.Setup.Core;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace Starward;

public static partial class AppConfig
{

    /// <summary>
    /// 便携版启动器（外层 Starward.exe）的完整路径。
    /// 仅在检测到便携版时有值。
    /// </summary>
    public static string? StarwardPortableLauncherExecutePath { get; private set; }

    /// <summary>
    /// 应用程序版本号（来自 AssemblyInformationalVersionAttribute）。
    /// </summary>
    public static string AppVersion { get; private set; }

    /// <summary>
    /// 安装类型（Installed / Portable）。
    /// </summary>
    public static InstallType InstallType { get; set; }

    /// <summary>
    /// 当前是否为便携版运行。
    /// </summary>
    public static bool IsPortable => InstallType is InstallType.Portable;

    /// <summary>
    /// 应用程序是否运行在可移动存储设备（U 盘等）上。
    /// </summary>
    public static bool IsAppInRemovableStorage { get; private set; }

    /// <summary>
    /// 缓存文件夹路径（通常在 LocalApplicationData\Starward 或便携目录下的 .cache）。
    /// </summary>
    public static string CacheFolder { get; private set; }

    /// <summary>
    /// 配置文件路径（config.ini）。便携版或可移动设备下会有值，否则为空（使用注册表）。
    /// </summary>
    public static string ConfigPath { get; private set; }

    /// <summary>
    /// 当前 UI 语言代码（如 zh-cn、en-us）。null 表示跟随系统。
    /// </summary>
    public static string? Language { get; set; }

    /// <summary>
    /// 用户数据文件夹路径（数据库、设置等存放位置）。
    /// </summary>
    public static string? UserDataFolder { get; set; }

    /// <summary>
    /// 当前进程是否以管理员身份运行。
    /// </summary>
    public static bool IsAdmin { get; private set; }

    /// <summary>
    /// 当前会话的日志文件完整路径。
    /// </summary>
    public static string LogFile { get; private set; }


    /// <summary>
    /// 是否启用登录鉴权票据（hoyolab 登录相关）。
    /// </summary>
    public static bool? EnableLoginAuthTicket { get; set; }

    /// <summary>
    /// HoyoLab 登录 stoken（持久化）。
    /// </summary>
    public static string? stoken { get; set; }

    /// <summary>
    /// HoyoLab 登录 mid（持久化）。
    /// </summary>
    public static string? mid { get; set; }




    /// <summary>
    /// 应用程序启动时的环境检查与初始化核心方法。
    /// 负责检测安装类型（便携/安装）、是否在可移动设备、确定缓存和配置路径、
    /// 读取或让用户选择 UserDataFolder，最后调用 LoadConfiguration。
    /// 若权限不足或用户取消，会弹出提示窗口并退出进程。
    /// </summary>
    public static async Task CheckEnviromentAsync()
    {
        try
        {
            AppVersion = typeof(AppConfig).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            IsAppInRemovableStorage = DriveHelper.IsDeviceRemovableOrOnUSB(AppContext.BaseDirectory);

            string? parentFolder = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName;
            string portableExe = Path.Join(parentFolder, "Starward.exe");
            string portableVersion = Path.Join(parentFolder, "version.ini");

            if (Directory.Exists(parentFolder) && (File.Exists(portableExe) || File.Exists(portableVersion)))
            {
                InstallType = InstallType.Portable;
                StarwardPortableLauncherExecutePath = portableExe;
                if (!HaveWritePermission(parentFolder))
                {
                    await new NoPermissionWindow(parentFolder).WaitAsync();
                    Environment.Exit(0);
                }
            }

            if (IsAppInRemovableStorage && IsPortable)
            {
                CacheFolder = Path.Combine(parentFolder!, ".cache");
                ConfigPath = Path.Combine(parentFolder!, "config.ini");
            }
            else if (IsAppInRemovableStorage)
            {
                CacheFolder = Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory)!, ".StarwardCache");
                ConfigPath = Path.Combine(CacheFolder, "config.ini");
            }
            else if (IsPortable)
            {
                CacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starward");
                ConfigPath = Path.Combine(parentFolder!, "config.ini");
            }
            else
            {
                CacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starward");
            }

            string? userDataFolder = null;
            if (string.IsNullOrWhiteSpace(ConfigPath))
            {
#if DEBUG
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Starward.Debug");
#else
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Starward");
#endif
                userDataFolder = (key.GetValue("UserDataFolder") as string)?.Trim();
            }
            else if (File.Exists(ConfigPath))
            {
                string text = File.ReadAllText(ConfigPath);
                userDataFolder = Regex.Match(text, @"UserDataFolder=(.+)").Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(userDataFolder) && !Path.IsPathFullyQualified(userDataFolder))
                {
                    userDataFolder = Path.GetFullPath(userDataFolder, Path.GetDirectoryName(ConfigPath)!);
                }
            }

            if (Directory.Exists(userDataFolder))
            {
                if (HaveWritePermission(userDataFolder))
                {
                    UserDataFolder = userDataFolder;
                    DatabaseService.SetDatabase(userDataFolder);
                    LoadConfiguration();
                }
                else
                {
                    await new NoPermissionWindow(userDataFolder).WaitAsync();
                    Environment.Exit(0);
                }
            }
            else
            {
                if (await new WelcomeWindow().WaitAsync())
                {
                    LoadConfiguration();
                }
                else
                {
                    Environment.Exit(0);
                }
            }
        }
        catch (Exception ex)
        {
            User32.MessageBox(HWND.NULL, $"{Lang.AppConfig_AnUnknownIssueOccurredDuringInitialization}\n{ex.Message}", "Starward", User32.MB_FLAGS.MB_OK);
            Environment.Exit(0);
        }
    }


    /// <summary>
    /// 测试指定文件夹是否具有写入权限（通过尝试创建并删除一个随机文件）。
    /// </summary>
    /// <param name="folder">要测试的文件夹路径。</param>
    /// <returns>有写入权限返回 true，否则返回 false。</returns>
    private static bool HaveWritePermission(string folder)
    {
        try
        {
            string random = Path.Combine(folder, Guid.CreateVersion7().ToString());
            File.WriteAllBytes(random, "Write permission test."u8);
            File.Delete(random);
            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 设置当前 UI 语言并同步到 CultureInfo。
    /// </summary>
    /// <param name="lang">语言代码（如 "zh-cn"）。为空或 null 时重置为系统安装语言。</param>
    public static void SetLanguage(string? lang)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(lang))
            {
                var info = new CultureInfo(lang);
                CultureInfo.CurrentUICulture = info;
                CultureInfo.DefaultThreadCurrentUICulture = info;
                Language = lang;
            }
            else
            {
                CultureInfo.CurrentUICulture = CultureInfo.InstalledUICulture;
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InstalledUICulture;
                Language = null;
            }
        }
        catch { }
    }



    /// <summary>
    /// 加载运行时配置（创建缓存目录、初始化 FileCache、设置 WebView2 数据目录、检测管理员权限、加载语言与登录信息）。
    /// 根据 ConfigPath 是否为空决定从注册表还是 config.ini 加载。
    /// </summary>
    public static void LoadConfiguration()
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            FileCache.Initialize(Path.Combine(CacheFolder, "cache"));
            var webviewFolder = Path.Combine(CacheFolder, "webview");
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webviewFolder, EnvironmentVariableTarget.Process);

            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            IsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

            if (string.IsNullOrWhiteSpace(ConfigPath))
            {
                LoadConfigurationFromRegistry();
            }
            else
            {
                LoadConfigurationFromConfigFile(ConfigPath);
            }
        }
        catch { }
    }


    /// <summary>
    /// 从指定的 config.ini 文件加载 Language、EnableLoginAuthTicket、stoken、mid 等配置。
    /// </summary>
    /// <param name="path">config.ini 完整路径。</param>
    public static void LoadConfigurationFromConfigFile(string path)
    {
        if (File.Exists(path))
        {
            string text = File.ReadAllText(path);
            string lang = Regex.Match(text, @"Language=(.+)").Groups[1].Value.Trim();
            bool.TryParse(Regex.Match(text, @"EnableLoginAuthTicket=(.+)").Groups[1].Value.Trim(), out bool enabled);
            EnableLoginAuthTicket = enabled;
            stoken = Regex.Match(text, @"stoken=(.+)").Groups[1].Value.Trim();
            mid = Regex.Match(text, @"mid=(.+)").Groups[1].Value.Trim();
            SetLanguage(lang);
        }
    }


    /// <summary>
    /// 从注册表加载 Language、EnableLoginAuthTicket、stoken、mid（调试版使用 Starward.Debug 子键）。
    /// </summary>
    public static void LoadConfigurationFromRegistry()
    {
#if DEBUG
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Starward.Debug");
#else
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Starward");
#endif
        string? lang = (key.GetValue("Language") as string)?.Trim();
        EnableLoginAuthTicket = key.GetValue("EnableLoginAuthTicket") is 1;
        stoken = (key.GetValue("stoken") as string)?.Trim();
        mid = (key.GetValue("mid") as string)?.Trim();
        SetLanguage(lang);
    }


    /// <summary>
    /// 保存当前配置。根据 ConfigPath 是否为空决定写入注册表还是 config.ini。
    /// </summary>
    public static void SaveConfiguration()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            SaveConfigurationToRegistry();
        }
        else
        {
            SaveConfigurationToConfigFile();
        }
    }


    /// <summary>
    /// 将当前 Language、UserDataFolder、EnableLoginAuthTicket、stoken、mid 等写入注册表。
    /// UserDataFolder 如果在 ConfigPath 同目录下会尽量存为相对路径。
    /// </summary>
    public static void SaveConfigurationToRegistry()
    {
        try
        {
#if DEBUG
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Starward.Debug");
#else
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Starward");
#endif
            if (!string.IsNullOrWhiteSpace(Language))
            {
                key.SetValue("Language", Language);
            }
            else
            {
                key.DeleteValue("Language", false);
            }
            if (!string.IsNullOrWhiteSpace(UserDataFolder))
            {
                string dataFolder = UserDataFolder;
                string? parentFolder = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(parentFolder) && UserDataFolder.StartsWith(parentFolder))
                {
                    dataFolder = Path.GetRelativePath(parentFolder, UserDataFolder);
                }
                key.SetValue("UserDataFolder", dataFolder);
            }
            else
            {
                key.DeleteValue("UserDataFolder", false);
            }
            if (EnableLoginAuthTicket.HasValue)
            {
                key.SetValue("EnableLoginAuthTicket", EnableLoginAuthTicket.Value ? 1 : 0);
            }
            if (!string.IsNullOrWhiteSpace(stoken))
            {
                key.SetValue("stoken", stoken);
            }
            if (!string.IsNullOrWhiteSpace(mid))
            {
                key.SetValue("mid", mid);
            }
        }
        catch { }
    }


    /// <summary>
    /// 将当前配置写入指定的 config.ini 文件（明文）。
    /// </summary>
    public static void SaveConfigurationToConfigFile()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(UserDataFolder))
            {
                string dataFolder = UserDataFolder;
                string? parentFolder = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(parentFolder) && UserDataFolder.StartsWith(parentFolder))
                {
                    dataFolder = Path.GetRelativePath(parentFolder, UserDataFolder);
                }
                sb.AppendLine($"Language={Language}");
                sb.AppendLine($"UserDataFolder={dataFolder}");
            }
            else
            {
                sb.AppendLine($"Language={Language}");
                sb.AppendLine($"UserDataFolder=");
            }
            if (EnableLoginAuthTicket.HasValue)
            {
                sb.AppendLine($"{nameof(EnableLoginAuthTicket)}={EnableLoginAuthTicket}");
            }
            if (!string.IsNullOrWhiteSpace(stoken))
            {
                sb.AppendLine($"{nameof(stoken)}={stoken}");
            }
            if (!string.IsNullOrWhiteSpace(mid))
            {
                sb.AppendLine($"{nameof(mid)}={mid}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, sb.ToString());
        }
        catch { }
    }









}
