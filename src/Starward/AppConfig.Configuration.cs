using Microsoft.Win32;
using Starward.Features.Database;
using Starward.Features.ViewHost;
using Starward.Helpers;
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
    /// 应用程序版本号（来自 AssemblyInformationalVersionAttribute）。
    /// </summary>
    public static string AppVersion { get; private set; }

    /// <summary>
    /// 当前是否为便携版运行（Velopack 便携包：根目录存在 .portable 标记）。
    /// </summary>
    public static bool IsPortable { get; private set; }

    /// <summary>
    /// 应用程序是否运行在可移动存储设备（U 盘等）上。
    /// </summary>
    public static bool IsAppInRemovableStorage { get; private set; }

    /// <summary>
    /// 缓存文件夹路径。自统一数据目录版本起，其值等于 <see cref="UserDataFolder"/>（数据库、备份、缓存、日志、背景图、webview、游戏缓存全部统一存放于此）。
    /// </summary>
    public static string CacheFolder { get; private set; }

    /// <summary>
    /// 旧版本缓存根目录（%LocalAppData%\Moonward / .MoonwardCache / 便携 .cache），<b>仅</b>用于升级迁移时探测旧数据来源，不作为运行期缓存路径。
    /// 注意：在标准安装下该目录同时是 Velopack 安装根目录（含 current\、Update.exe 等），迁移时必须按白名单只搬运 Moonward 自己的数据，绝不能动 Velopack 文件。
    /// </summary>
    public static string? LegacyCacheFolder { get; private set; }

    /// <summary>
    /// 配置文件路径（config.ini）。便携版或可移动设备下会有值，否则为空（使用注册表）。
    /// </summary>
    public static string ConfigPath { get; private set; }

    /// <summary>
    /// 当前 UI 语言代码（如 zh-cn、en-us）。null 表示跟随系统。
    /// </summary>
    public static string? Language { get; set; }

    /// <summary>
    /// 用户数据文件夹路径（数据库、设置等存放位置）。等于「用户所选目录 \ <see cref="DataSubFolderName"/>」。
    /// </summary>
    public static string? UserDataFolder { get; set; }

    /// <summary>
    /// 用户所选目录下、实际存放全部数据的子文件夹名。即统一数据目录 = 用户选择目录 \ data，
    /// 这样数据与 Velopack 文件（current\、Update.exe 等）整齐分隔、互不干扰。
    /// </summary>
    public const string DataSubFolderName = "data";

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

            // Velopack 部署结构：<root>/current/Moonward.exe、<root>/Update.exe；便携版 <root> 下有 .portable 标记。
            string? rootFolder = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName;
            bool isVelopackInstall = rootFolder is not null && File.Exists(Path.Combine(rootFolder, "Update.exe"));
            IsPortable = isVelopackInstall && File.Exists(Path.Combine(rootFolder!, ".portable"));

            if (IsPortable && !HaveWritePermission(rootFolder!))
            {
                await new NoPermissionWindow(rootFolder!).WaitAsync();
                Environment.Exit(0);
            }

            // 计算 ConfigPath（marker 存放位置）与 LegacyCacheFolder（旧版缓存根，仅用于升级迁移时探测旧数据来源）。
            if (IsAppInRemovableStorage && IsPortable)
            {
                LegacyCacheFolder = Path.Combine(rootFolder!, ".cache");
                ConfigPath = Path.Combine(rootFolder!, "config.ini");
            }
            else if (IsAppInRemovableStorage)
            {
                LegacyCacheFolder = Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory)!, ".MoonwardCache");
                ConfigPath = Path.Combine(LegacyCacheFolder, "config.ini");
            }
            else if (IsPortable)
            {
                LegacyCacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moonward");
                ConfigPath = Path.Combine(rootFolder!, "config.ini");
            }
            else
            {
                LegacyCacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moonward");
            }

            // 子进程短路：命令行带 --data-folder 时直接采用该目录，不显示任何 UI（rpc / playtime / 提权迁移子进程）。
            string? cmdDataFolder = GetCommandLineArgValue("--data-folder");
            if (Directory.Exists(cmdDataFolder))
            {
                UseDataFolder(cmdDataFolder!);
                LoadConfiguration();
                return;
            }

#if DEBUG
            // 开发调试环境：不做迁移检查，数据固定放在程序根目录的 data 文件夹下（没有则创建）。
            string debugDataFolder = Path.Combine(AppContext.BaseDirectory, DataSubFolderName);
            Directory.CreateDirectory(debugDataFolder);
            UseDataFolder(debugDataFolder);
            LoadConfiguration();
#else
            // 读取已持久化的统一数据目录 DataFolder，以及旧版 UserDataFolder（升级迁移源之一）。
            (string? dataFolder, string? legacyUserDataFolder) = ReadPersistedDataFolders();

            // 提权迁移子进程：带 --migrate-to <target> 时，强制迁移旧数据到指定目录后再继续运行。
            string? migrateTo = GetCommandLineArgValue("--migrate-to");

            if (string.IsNullOrEmpty(migrateTo) && Directory.Exists(dataFolder))
            {
                // 已迁移：直接使用统一数据目录。
                if (HaveWritePermission(dataFolder!))
                {
                    UseDataFolder(dataFolder!);
                    LoadConfiguration();
                }
                else
                {
                    await new NoPermissionWindow(dataFolder!).WaitAsync();
                    Environment.Exit(0);
                }
            }
            else
            {
                // 未迁移 / 全新安装 / 提权迁移子进程：弹出迁移·选择窗口（强制手动选择目标，拒绝则退出，不允许继续使用）。
                if (await new WelcomeWindow(legacyUserDataFolder, LegacyCacheFolder, migrateTo).WaitAsync())
                {
                    LoadConfiguration();
                }
                else
                {
                    Environment.Exit(0);
                }
            }
#endif
        }
        catch (Exception ex)
        {
            User32.MessageBox(HWND.NULL, $"{Lang.AppConfig_AnUnknownIssueOccurredDuringInitialization}\n{ex.Message}", "Moonward", User32.MB_FLAGS.MB_OK);
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
    /// 采用指定文件夹作为统一数据目录：<see cref="UserDataFolder"/> 与 <see cref="CacheFolder"/> 同时指向它，并初始化数据库。
    /// </summary>
    internal static void UseDataFolder(string folder)
    {
        UserDataFolder = folder;
        CacheFolder = folder;
        DatabaseService.SetDatabase(folder);
    }


    /// <summary>
    /// 从命令行参数读取指定开关后紧跟的值（如 <c>--data-folder "D:\xxx"</c>）。未找到返回 null。
    /// </summary>
    private static string? GetCommandLineArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1]?.Trim();
            }
        }
        return null;
    }


    /// <summary>
    /// 生成传递给子进程（rpc / playtime / 提权迁移）的数据目录命令行参数，确保子进程使用同一统一数据目录。
    /// </summary>
    public static string GetDataFolderArgument()
    {
        return string.IsNullOrWhiteSpace(UserDataFolder) ? string.Empty : $"--data-folder \"{UserDataFolder}\"";
    }


    /// <summary>
    /// 读取已持久化的统一数据目录 DataFolder 与旧版 UserDataFolder（注册表或 config.ini）。
    /// </summary>
    private static (string? dataFolder, string? legacyUserDataFolder) ReadPersistedDataFolders()
    {
        string? dataFolder = null;
        string? legacyUserDataFolder = null;
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
#if DEBUG
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Moonward.Debug");
#else
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Moonward");
#endif
            dataFolder = (key.GetValue("DataFolder") as string)?.Trim();
            legacyUserDataFolder = (key.GetValue("UserDataFolder") as string)?.Trim();
        }
        else if (File.Exists(ConfigPath))
        {
            string text = File.ReadAllText(ConfigPath);
            // DataFolder 是 UserDataFolder 的后缀，必须按行锚定，避免 "UserDataFolder=" 被 "DataFolder=" 误匹配。
            dataFolder = ResolveConfigFolder(Regex.Match(text, @"^DataFolder=(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim());
            legacyUserDataFolder = ResolveConfigFolder(Regex.Match(text, @"^UserDataFolder=(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim());
        }
        return (dataFolder, legacyUserDataFolder);
    }


    /// <summary>
    /// 将 config.ini 中的相对路径解析为相对于 config.ini 所在目录的绝对路径。
    /// </summary>
    private static string? ResolveConfigFolder(string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && !Path.IsPathFullyQualified(folder))
        {
            return Path.GetFullPath(folder, Path.GetDirectoryName(ConfigPath)!);
        }
        return folder;
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
    /// 从注册表加载 Language、EnableLoginAuthTicket、stoken、mid（调试版使用 Moonward.Debug 子键）。
    /// </summary>
    public static void LoadConfigurationFromRegistry()
    {
#if DEBUG
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Moonward.Debug");
#else
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Moonward");
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
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Moonward.Debug");
#else
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Moonward");
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
                // DataFolder 为统一数据目录标记（迁移完成后写入）；UserDataFolder 同步写入以兼容旧版读取。
                key.SetValue("DataFolder", dataFolder);
                key.SetValue("UserDataFolder", dataFolder);
            }
            else
            {
                key.DeleteValue("DataFolder", false);
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
                sb.AppendLine($"DataFolder={dataFolder}");
                sb.AppendLine($"UserDataFolder={dataFolder}");
            }
            else
            {
                sb.AppendLine($"Language={Language}");
                sb.AppendLine($"DataFolder=");
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
