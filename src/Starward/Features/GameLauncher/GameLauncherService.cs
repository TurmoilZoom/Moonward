using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.HoYoPlay;
using Starward.Features.GameRecord;
using Starward.Features.GameSetting;
using Starward.Features.HoYoPlay;
using Starward.Features.PlayTime;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace Starward.Features.GameLauncher;

internal partial class GameLauncherService
{


    private readonly ILogger<GameLauncherService> _logger;


    private readonly HoYoPlayService _hoYoPlayService;

    private readonly PlayTimeRecordService _playTimeRecorderService;

    private readonly GameAuthLoginService _gameAuthLoginService;

    private readonly GameRecordService _gameRecordService;


    public GameLauncherService(
        ILogger<GameLauncherService> logger,
        HoYoPlayService hoYoPlayService,
        PlayTimeRecordService playTimeRecorderService,
        GameAuthLoginService gameAuthLoginService,
        GameRecordService gameRecordService)
    {
        _logger = logger;
        _hoYoPlayService = hoYoPlayService;
        _playTimeRecorderService = playTimeRecorderService;
        _gameAuthLoginService = gameAuthLoginService;
        _gameRecordService = gameRecordService;
    }





    /// <summary>
    /// 游戏安装目录，为空时未找到
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetGameInstallPath(GameId gameId)
    {
        return GetGameInstallPath(gameId.GameBiz);
    }


    /// <summary>
    /// 游戏安装目录，为空时未找到
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetGameInstallPath(GameBiz gameBiz)
    {
        var path = AppConfig.GetGameInstallPath(gameBiz);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        path = GetFullPathIfRelativePath(path);
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }
        else if (AppConfig.GetGameInstallPathRemovable(gameBiz))
        {
            return path;
        }
        else
        {
            ChangeGameInstallPath(gameBiz, null);
            return null;
        }
    }



    /// <summary>
    /// 游戏安装目录，为空时未找到
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="storageRemoved">可移动存储设备已移除</param>
    /// <returns></returns>
    public static string? GetGameInstallPath(GameId gameId, out bool storageRemoved)
    {
        storageRemoved = false;
        var path = AppConfig.GetGameInstallPath(gameId.GameBiz);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        path = GetFullPathIfRelativePath(path);
        if (Directory.Exists(path))
        {
            return path;
        }
        else if (AppConfig.GetGameInstallPathRemovable(gameId.GameBiz))
        {
            storageRemoved = true;
            return path;
        }
        else
        {
            ChangeGameInstallPath(gameId, null);
            return null;
        }
    }



    /// <summary>
    /// 本地游戏版本
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<Version?> GetLocalGameVersionAsync(GameId gameId, string? installPath = null)
    {
        return await GetLocalGameVersionAsync(gameId.GameBiz, installPath);
    }



    /// <summary>
    /// 本地游戏版本
    /// </summary>
    /// <param name="gameBiz"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<Version?> GetLocalGameVersionAsync(GameBiz gameBiz, string? installPath = null)
    {
        installPath ??= GetGameInstallPath(gameBiz);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        var config = Path.Join(installPath, "config.ini");
        if (File.Exists(config))
        {
            var str = await File.ReadAllTextAsync(config);
            var matches = GameVersionRegex().Matches(str);
            Version? version = null;
            if (matches.Count > 0)
            {
                _ = Version.TryParse(matches[^1].Groups[1].Value, out version);
            }
            return version;
        }
        else
        {
            _logger.LogWarning("config.ini not found: {path}", config);
            return null;
        }
    }


    [GeneratedRegex(@"game_version=(.+)")]
    private static partial Regex GameVersionRegex();



    /// <summary>
    /// 本地 WPF 包（原神的千星沙箱）版本，即安装时写进 config.ini 的 wpf_version。
    /// </summary>
    /// <param name="installPath">游戏安装目录</param>
    /// <returns>本地版本；没有 config.ini 或没有记录（未安装）时返回 <see langword="null"/></returns>
    public static async Task<string?> GetLocalWPFVersionAsync(string installPath)
    {
        string config = Path.Join(installPath, "config.ini");
        if (!File.Exists(config))
        {
            return null;
        }
        MatchCollection matches = WPFVersionRegex().Matches(await File.ReadAllTextAsync(config));
        if (matches.Count == 0)
        {
            return null;
        }
        string version = matches[^1].Groups[1].Value.Trim();
        return string.IsNullOrEmpty(version) ? null : version;
    }


    [GeneratedRegex(@"wpf_version=(.+)")]
    private static partial Regex WPFVersionRegex();



    /// <summary>
    /// 最新游戏版本
    /// </summary>
    /// <param name="gameBiz"></param>
    /// <returns></returns>
    public async Task<(Version? Latest, Version? Predownload)> GetLatestGameVersionAsync(GameId gameId)
    {
        GameConfig? config = await _hoYoPlayService.GetGameConfigAsync(gameId);
        if (config is null)
        {
            throw new ArgumentOutOfRangeException($"Game config is null ({gameId.Id}, {gameId.GameBiz}).");
        }
        if (config.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK or DownloadMode.DOWNLOAD_MODE_LDIFF)
        {
            GameBranch? gameBranch = await _hoYoPlayService.GetGameBranchAsync(gameId);
            if (gameBranch is null)
            {
                throw new ArgumentOutOfRangeException($"Game branch is null ({gameId.Id}, {gameId.GameBiz}).");
            }
            _ = Version.TryParse(gameBranch.Main.Tag, out Version? latestVersion);
            _ = Version.TryParse(gameBranch.PreDownload?.Tag, out Version? predownloadVersion);
            return (latestVersion, predownloadVersion);
        }
        else
        {
            GamePackage? package = await _hoYoPlayService.GetGamePackageAsync(gameId);
            if (package is null)
            {
                throw new ArgumentOutOfRangeException($"Game package is null ({gameId.Id}, {gameId.GameBiz}).");
            }
            _ = Version.TryParse(package.Main.Major?.Version, out Version? latestVersion);
            _ = Version.TryParse(package.PreDownload.Major?.Version, out Version? predownloadVersion);
            return (latestVersion, predownloadVersion);
        }
    }




    /// <summary>
    /// 游戏进程名，带 .exe 扩展名
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public async Task<string> GetGameExeNameAsync(GameId gameId)
    {
        string? name = GetGameExeName(gameId.GameBiz);
        if (string.IsNullOrWhiteSpace(name))
        {
            var config = await _hoYoPlayService.GetGameConfigAsync(gameId);
            name = config?.ExeFileName;
        }
        return name ?? throw new ArgumentOutOfRangeException($"Unknown game ({gameId.Id}, {gameId.GameBiz}).");
    }



    /// <summary>
    /// 游戏进程名，带 .exe 扩展名
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetGameExeName(GameBiz gameBiz)
    {
        string? name = gameBiz.Value switch
        {
            GameBiz.hk4e_cn or GameBiz.hk4e_bilibili => "YuanShen.exe",
            GameBiz.hk4e_global => "GenshinImpact.exe",
            _ => gameBiz.Game switch
            {
                GameBiz.hkrpg => "StarRail.exe",
                GameBiz.bh3 => "BH3.exe",
                GameBiz.nap => "ZenlessZoneZero.exe",
                _ => null,
            },
        };
        return name;
    }



    /// <summary>
    /// 游戏进程文件是否存在
    /// </summary>
    /// <param name="biz"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<bool> IsGameExeExistsAsync(GameId gameId, string? installPath = null)
    {
        installPath ??= GetGameInstallPath(gameId);
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            var exe = Path.Join(installPath, await GetGameExeNameAsync(gameId));
            return File.Exists(exe);
        }
        return false;
    }



    /// <summary>
    /// 获取本区服的游戏进程。
    /// 国服与 B 服等区服的 exe 同名，只按进程名会把另一个区服的进程算到本区服头上，
    /// 所以能确认进程位于同名 exe 的其他区服安装目录时将其排除；
    /// 取不到进程路径或路径不在任何已知安装目录时仍算作本区服，与只按进程名查找时一致。
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <returns>本区服正在运行的游戏进程；没有时返回 <see langword="null"/></returns>
    public async Task<Process?> GetGameProcessAsync(GameId gameId)
    {
        string exeName = await GetGameExeNameAsync(gameId);
        Process[] processes = GetGameProcessesByExeName(exeName);
        if (processes.Length == 0)
        {
            return null;
        }
        List<string> otherFolders = new();
        foreach (GameBiz biz in GameBiz.AllGameBizs)
        {
            if (biz != gameId.GameBiz
                && string.Equals(GetGameExeName(biz), exeName, StringComparison.OrdinalIgnoreCase)
                && GetConfiguredInstallFolder(biz) is string folder)
            {
                otherFolders.Add(folder);
            }
        }
        if (otherFolders.Count == 0)
        {
            return processes[0];
        }
        string? ownFolder = GetConfiguredInstallFolder(gameId.GameBiz);
        foreach (Process process in processes)
        {
            if (TryGetProcessImagePath(process) is not string imagePath)
            {
                return process;
            }
            // 取最长的匹配目录，一个区服装在另一个区服目录的子目录里时也能归到正确的区服
            string? owner = otherFolders.Append(ownFolder)
                .Where(x => x is not null && imagePath.StartsWith(x, StringComparison.OrdinalIgnoreCase))
                .MaxBy(x => x!.Length);
            if (owner is null || owner == ownFolder)
            {
                return process;
            }
        }
        return null;
    }


    /// <summary>
    /// 获取同名 exe 的游戏进程，不区分区服。国服与 B 服是同一个程序、共用注册表键，
    /// 启动前据此拦截，避免另一个区服还开着时再启动一个。
    /// </summary>
    /// <param name="gameId">游戏</param>
    /// <returns>同名 exe 正在运行的任意一个进程；没有时返回 <see langword="null"/></returns>
    public async Task<Process?> GetGameProcessOfAnyServerAsync(GameId gameId)
    {
        return GetGameProcessesByExeName(await GetGameExeNameAsync(gameId)).FirstOrDefault();
    }


    /// <summary>
    /// 按 exe 名获取当前会话中未挂起的进程。
    /// </summary>
    /// <param name="exeName">exe 文件名，带不带 .exe 扩展名均可</param>
    /// <returns>匹配的进程，可能为空数组</returns>
    private static Process[] GetGameProcessesByExeName(string exeName)
    {
        int currentSessionId = Process.GetCurrentProcess().SessionId;
        return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName))
            .Where(x => x.SessionId == currentSessionId && !IsProcessPending(x))
            .ToArray();
    }


    /// <summary>
    /// 读取设置里保存的安装目录，规范化为以目录分隔符结尾的完整路径，便于做前缀比较。
    /// 不检查目录是否存在，也不像 <see cref="GetGameInstallPath(GameBiz)"/> 那样清理失效路径。
    /// </summary>
    /// <param name="gameBiz">区服</param>
    /// <returns>安装目录；未设置或路径无效时返回 <see langword="null"/></returns>
    private static string? GetConfiguredInstallFolder(GameBiz gameBiz)
    {
        string? path = AppConfig.GetGameInstallPath(gameBiz);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        try
        {
            return Path.TrimEndingDirectorySeparator(GetFullPathIfRelativePath(path)) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }


    /// <summary>
    /// 获取进程的 exe 完整路径。
    /// </summary>
    /// <param name="process">进程</param>
    /// <returns>exe 完整路径；无权访问或进程已退出时返回 <see langword="null"/></returns>
    private static string? TryGetProcessImagePath(Process process)
    {
        try
        {
            // 游戏以管理员权限运行，Process.MainModule 要读进程内存会被拒绝；查询受限信息的权限就足够取路径
            using Kernel32.SafeHPROCESS handle = Kernel32.OpenProcess((uint)Kernel32.ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)process.Id);
            if (!handle.IsInvalid && Kernel32.QueryFullProcessImageName(handle, Kernel32.PROCESS_NAME.PROCESS_NAME_WIN32, out string? path) && !string.IsNullOrWhiteSpace(path))
            {
                return Path.GetFullPath(path);
            }
        }
        catch { }
        return null;
    }



    /// <summary>
    /// 检测进程挂起
    /// </summary>
    /// <param name="process"></param>
    /// <returns></returns>
    public static bool IsProcessPending(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }
            foreach (ProcessThread thread in process.Threads)
            {
                if (thread.ThreadState is not ThreadState.Wait)
                {
                    return false;
                }
                else if (thread.WaitReason is not ThreadWaitReason.Suspended)
                {
                    return false;
                }
            }
            return true;
        }
        catch { }
        return true;
    }



    /// <summary>
    /// 启动游戏。使用自定义启动程序时不注入 <c>login_auth_ticket</c>（游戏由工具自行登录，配置的登录账号在此不生效）。
    /// </summary>
    /// <returns></returns>
    /// <param name="profile">额外配置文件（config2…）；null 且 <paramref name="useNoneLaunchMethod"/> 为 false 时使用 config1 的 legacy 键。</param>
    /// <param name="useNoneLaunchMethod">「无」：不使用启动参数配置（无命令行参数、无自定义启动程序、不用 CMD 启动），仍应用 DX12 等全局开关。</param>
    /// <param name="loginUid">URL 或调用方显式指定的游戏角色 UID；优先于配置文件中的 <see cref="GameLaunchProfile.LoginUid"/>。自定义程序启动时不用于换票。</param>
    public async Task<Process?> StartGameAsync(GameId gameId, string? installPath = null, GameLaunchProfile? profile = null, bool useNoneLaunchMethod = false, long? loginUid = null)
    {
        const int ERROR_CANCELLED = 0x000004C7;
        try
        {
            if (await GetGameProcessOfAnyServerAsync(gameId) is Process existingProcess)
            {
                throw new GameRunningException(existingProcess.ProcessName, existingProcess.Id);
            }
            // 「无」：不依赖启动参数配置；否则 profile 非空用其数据，null 用 config1（legacy 键）。
            bool enableThirdPartyTool;
            string? thirdPartyToolPath;
            string? startArgument;
            if (useNoneLaunchMethod)
            {
                enableThirdPartyTool = false;
                thirdPartyToolPath = null;
                startArgument = null;
            }
            else
            {
                enableThirdPartyTool = profile?.EnableThirdPartyTool ?? AppConfig.GetEnableThirdPartyTool(gameId.GameBiz);
                thirdPartyToolPath = profile is null
                    ? GetThirdPartyToolPath(gameId)
                    : (string.IsNullOrWhiteSpace(profile.ThirdPartyToolPath) ? null : GetFullPathIfRelativePath(profile.ThirdPartyToolPath));
                startArgument = profile is null ? AppConfig.GetStartArgument(gameId.GameBiz) : profile.Argument;
            }

            string? exe = null, arg = null, verb = null;
            if (Directory.Exists(installPath))
            {
                var e = Path.Join(installPath, await GetGameExeNameAsync(gameId));
                if (File.Exists(e))
                {
                    exe = e;
                }
            }
            bool thirdPartyTool = false;
            if (string.IsNullOrWhiteSpace(exe) && enableThirdPartyTool)
            {
                exe = thirdPartyToolPath;
                if (File.Exists(exe))
                {
                    thirdPartyTool = true;
                    verb = Path.GetExtension(exe) is ".exe" or ".bat" ? "runas" : "";
                }
                else
                {
                    exe = null;
                    // 仅在使用默认配置时清理失效的 legacy 路径；配置文件的路径不在此清除。
                    if (profile is null)
                    {
                        SetThirdPartyToolPath(gameId, null);
                    }
                    _logger.LogWarning("Third party tool not found: {path}", thirdPartyToolPath);
                }
            }
            if (string.IsNullOrWhiteSpace(exe))
            {
                var folder = GetGameInstallPath(gameId);
                var name = await GetGameExeNameAsync(gameId);
                exe = Path.Join(folder, name);
                verb = "runas";
                if (!File.Exists(exe))
                {
                    _logger.LogWarning("Game exe not found: {path}", exe);
                    throw new FileNotFoundException("Game exe not found", name);
                }
            }
            arg = startArgument?.Trim();
            // 自定义程序启动的是工具而非游戏，token 对工具无用且可能干扰其 argparse
            if (!thirdPartyTool)
            {
                long resolvedLoginUid = ResolveLoginUid(gameId.GameBiz, profile, useNoneLaunchMethod, loginUid);
                if (resolvedLoginUid > 0)
                {
                    GameRecordRole? role = _gameRecordService.GetGameRoles(gameId.GameBiz)
                        .FirstOrDefault(r => r.Uid == resolvedLoginUid);
                    if (role is null)
                    {
                        _logger.LogWarning("Login account role not found (biz={Biz}, uid={Uid})", gameId.GameBiz, resolvedLoginUid);
                    }
                    else
                    {
                        string? ticket = await _gameAuthLoginService.CreateAuthTicketByGameRoleAsync(gameId, role);
                        if (!string.IsNullOrWhiteSpace(ticket))
                        {
                            arg += $" login_auth_ticket={ticket}";
                        }
                    }
                }
                else if (AppConfig.EnableLoginAuthTicket is true)
                {
                    string? ticket = await _gameAuthLoginService.CreateAuthTicketByGameBiz(gameId);
                    if (!string.IsNullOrWhiteSpace(ticket))
                    {
                        arg += $" login_auth_ticket={ticket}";
                    }
                }
            }
            if (AppConfig.GetUsePopupWindow(gameId.GameBiz))
            {
                arg += " -popupwindow";
            }
            if (ShouldAppendAutoDx12(gameId.GameBiz, profile, useNoneLaunchMethod))
            {
                arg += " -use-d3d12";
            }

            if (gameId.GameBiz.Game is GameBiz.hk4e)
            {
                GameSettingService.SetGenshinEnableHDR(gameId.GameBiz, AppConfig.EnableGenshinHDR);
            }
            bool startWithCmd = !thirdPartyTool && ResolveStartWithCmd(gameId.GameBiz, profile, useNoneLaunchMethod);
            if (startWithCmd)
            {
                arg = $"""/c start "" /d "{Path.GetDirectoryName(exe)}" "{exe}" {arg}""";
                exe = "cmd.exe";
            }
            _logger.LogInformation("Start game ({biz})\r\npath: {exe}\r\narg: {arg}", gameId.GameBiz, exe, MaskLoginAuthTicket(arg));
            var info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arg,
                UseShellExecute = true,
                Verb = verb,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            Process? process = Process.Start(info);
            if (process != null)
            {
                if (thirdPartyTool || startWithCmd)
                {
                    return await _playTimeRecorderService.StartProcessToLogAsync(gameId);
                }
                else
                {
                    await _playTimeRecorderService.StartProcessToLogAsync(gameId, process.Id);
                    return process;
                }
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            // Operation canceled
            _logger.LogInformation("Start game operation canceled.");
        }
        return null;
    }


    /// <summary>
    /// 把启动参数里的 <c>login_auth_ticket</c> 打码后再写日志。
    /// 票据能直接登录游戏账号，而用户报问题时常整份贴出日志，故记录的一律是掩码值。
    /// </summary>
    /// <param name="arg">实际传给游戏的启动参数。</param>
    /// <returns>票据值替换为 <c>***</c> 后的参数；无票据时原样返回。</returns>
    private static string? MaskLoginAuthTicket(string? arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return arg;
        }
        return LoginAuthTicketRegex().Replace(arg, "login_auth_ticket=***");
    }


    /// <summary>CMD 启动会把参数再包一层引号，故票据值以空白或引号为界。</summary>
    [GeneratedRegex("""login_auth_ticket=[^\s"]+""")]
    private static partial Regex LoginAuthTicketRegex();


    /// <summary>
    /// 解析本次启动是否使用 CMD 启动游戏（按配置文件保存）。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="profile">额外配置文件；null 表示 config1（legacy 键）。</param>
    /// <param name="useNoneLaunchMethod">「无」启动方式：不使用任何配置文件，也不使用 CMD。</param>
    /// <returns>是否使用 CMD 启动。</returns>
    internal static bool ResolveStartWithCmd(GameBiz biz, GameLaunchProfile? profile, bool useNoneLaunchMethod)
    {
        if (useNoneLaunchMethod)
        {
            return false;
        }
        bool? value = profile is null ? AppConfig.GetDefaultLaunchProfileStartWithCmd(biz) : profile.StartWithCmd;
        // 旧版为全局开关，配置文件从未保存过该项时沿用旧值，升级后行为不变
        return value ?? AppConfig.StartGameWithCMD;
    }


    /// <summary>
    /// 解析启动时用于 auth ticket 登录的游戏角色 UID。
    /// 优先级：显式参数 → 额外配置 LoginUid → config1 legacy 键 → 0（不指定）。
    /// 「无」启动方式且无显式 uid 时不读取配置登录账号。
    /// </summary>
    internal static long ResolveLoginUid(GameBiz biz, GameLaunchProfile? profile, bool useNoneLaunchMethod, long? explicitLoginUid)
    {
        if (explicitLoginUid is > 0)
        {
            return explicitLoginUid.Value;
        }
        if (useNoneLaunchMethod)
        {
            return 0;
        }
        if (profile?.LoginUid is > 0)
        {
            return profile.LoginUid.Value;
        }
        // profile 为 null 表示 config1（legacy）
        if (profile is null)
        {
            return AppConfig.GetDefaultLaunchLoginUid(biz);
        }
        return 0;
    }


    /// <summary>
    /// 是否在本次启动附加全局 DX12 参数 <c>-use-d3d12</c>。
    /// 全局未开启则否；「无」始终跟随全局；具体配置文件可通过 <see cref="GameLaunchProfile.SkipAutoDx12"/> 单独跳过。
    /// </summary>
    private static bool ShouldAppendAutoDx12(GameBiz biz, GameLaunchProfile? profile, bool useNoneLaunchMethod)
    {
        if (!AppConfig.GetEnableDX12(biz))
        {
            return false;
        }
        if (useNoneLaunchMethod)
        {
            return true;
        }
        return !(profile?.SkipAutoDx12 ?? AppConfig.GetDefaultLaunchProfileSkipAutoDx12(biz));
    }


    /// <summary>
    /// 修改游戏安装目录
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string? ChangeGameInstallPath(GameId gameId, string? path)
    {
        return ChangeGameInstallPath(gameId.GameBiz, path);
    }


    /// <summary>
    /// 修改游戏安装目录
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string? ChangeGameInstallPath(GameBiz gameBiz, string? path)
    {
        if (Directory.Exists(path))
        {
            path = Path.GetFullPath(path);
            string relativePath = GetRelativePathIfInRemovableStorage(path, out bool removable);
            AppConfig.SetGameInstallPath(gameBiz, relativePath);
            AppConfig.SetGameInstallPathRemovable(gameBiz, removable);
        }
        else
        {
            path = null;
            AppConfig.SetGameInstallPath(gameBiz, null);
            AppConfig.SetGameInstallPathRemovable(gameBiz, false);
        }
        return path;
    }



    /// <summary>
    /// 如果安装在可移动存储设备中，获取相对路径
    /// </summary>
    /// <param name="path"></param>
    /// <param name="removableStorage"></param>
    /// <returns></returns>
    public static string GetRelativePathIfInRemovableStorage(string path, out bool removableStorage)
    {
        removableStorage = DriveHelper.IsDeviceRemovableOrOnUSB(path);
        if (removableStorage && Path.GetPathRoot(AppConfig.MoonwardExecutePath) == Path.GetPathRoot(path))
        {
            path = Path.GetRelativePath(Path.GetDirectoryName(AppConfig.ConfigPath)!, path);
        }
        return path;
    }



    /// <summary>
    /// 如果安装在可移动存储设备中，获取完整路径
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string GetFullPathIfRelativePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }
        else
        {
            return Path.GetFullPath(path, Path.GetDirectoryName(AppConfig.ConfigPath)!);
        }
    }




    /// <summary>
    /// 获取第三方工具路径
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetThirdPartyToolPath(GameId gameId)
    {
        string? path = AppConfig.GetThirdPartyToolPath(gameId.GameBiz);
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = GetFullPathIfRelativePath(path);
        }
        if (File.Exists(path))
        {
            return path;
        }
        else
        {
            AppConfig.SetThirdPartyToolPath(gameId.GameBiz, null);
            return null;
        }
    }


    /// <summary>
    /// 设置第三方工具路径
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string? SetThirdPartyToolPath(GameId gameId, string? path)
    {
        if (File.Exists(path))
        {
            path = Path.GetFullPath(path);
            string relativePath = GetRelativePathIfInRemovableStorage(path, out bool removable);
            AppConfig.SetThirdPartyToolPath(gameId.GameBiz, relativePath);
        }
        else
        {
            path = null;
            AppConfig.SetThirdPartyToolPath(gameId.GameBiz, null);
        }
        return path;
    }




}
