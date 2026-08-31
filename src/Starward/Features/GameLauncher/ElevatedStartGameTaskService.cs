using Starward.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 通过 Windows 计划任务（最高权限 + 仅登录时）实现「免 UAC 启动游戏」。
/// 创建任务时若当前进程非管理员则对 <c>schtasks.exe</c> 发起一次 UAC；之后用 <c>schtasks /Run</c> 触发任务不再弹 UAC。
/// 任务动作始终为 <c>Moonward.exe "moonward://startgame/…"</c>，以保留配置、登录票据与游玩时长等逻辑。
/// </summary>
internal static class ElevatedStartGameTaskService
{

    /// <summary>计划任务文件夹名（位于任务计划程序库根下）。</summary>
    public const string TaskFolderName = "Moonward";

    /// <summary>本功能创建的任务名前缀（文件夹内）。</summary>
    public const string TaskNamePrefix = "StartGame_";

    private const int ErrorCancelled = 0x000004C7;


    /// <summary>
    /// 清理结果：删除前发现的任务数，以及清理后仍残留的任务数。
    /// </summary>
    /// <param name="FoundCount">清理前匹配到的任务数量。</param>
    /// <param name="RemainingCount">清理后仍存在的匹配任务数量（0 表示全部删除）。</param>
    public readonly record struct CleanupResult(int FoundCount, int RemainingCount)
    {
        /// <summary>本次成功删除的数量（近似：Found − Remaining）。</summary>
        public int DeletedCount => Math.Max(0, FoundCount - RemainingCount);
    }


    /// <summary>
    /// 本应用创建的免 UAC 启动计划任务信息。
    /// </summary>
    /// <param name="TaskPath">计划任务完整路径。</param>
    /// <param name="TaskName">计划任务短名。</param>
    /// <param name="GameBiz">可识别的游戏区服；历史或未知任务为 null。</param>
    /// <param name="ProfileId">任务名中的启动配置标识。</param>
    /// <param name="LoginUid">任务名中的绑定登录 UID；未指定时为 null。</param>
    public readonly record struct StartGameTaskInfo(
        string TaskPath,
        string TaskName,
        GameBiz? GameBiz,
        string ProfileId,
        long? LoginUid);


    /// <summary>
    /// 生成稳定的任务短名（不含文件夹前缀），便于覆盖更新。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="profileId">配置文件 Id；null/空表示跟随软件设置。</param>
    /// <param name="loginUid">绑定登录 UID；&gt;0 时写入任务名。</param>
    public static string BuildTaskName(GameBiz biz, string? profileId, long? loginUid)
    {
        string profilePart = string.IsNullOrWhiteSpace(profileId) ? "follow" : profileId.Trim();
        var sb = new StringBuilder("StartGame_");
        sb.Append(biz.ToString());
        sb.Append('_');
        sb.Append(profilePart);
        if (loginUid is > 0)
        {
            sb.Append("_uid");
            sb.Append(loginUid.Value);
        }
        return SanitizeTaskName(sb.ToString());
    }


    /// <summary>
    /// 完整任务路径，形如 <c>\Moonward\StartGame_hk4e_cn_config1</c>。
    /// </summary>
    public static string GetTaskPath(string taskName) => $@"\{TaskFolderName}\{taskName}";


    /// <summary>
    /// 创建或覆盖「最高权限、仅登录时、可按需运行」的启动任务。
    /// 非管理员时会弹出 UAC；用户取消时抛出 <see cref="Win32Exception"/>（NativeErrorCode = 1223）。
    /// </summary>
    /// <param name="taskName"><see cref="BuildTaskName"/> 生成的短名。</param>
    /// <param name="startGameUrl">完整 <c>moonward://startgame/…</c> URL。</param>
    /// <exception cref="Win32Exception">用户取消 UAC，或 schtasks 进程启动失败。</exception>
    /// <exception cref="InvalidOperationException">schtasks 返回非 0 退出码。</exception>
    public static void RegisterOrUpdate(string taskName, string startGameUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentException.ThrowIfNullOrWhiteSpace(startGameUrl);

        string exe = AppConfig.MoonwardExecutePath;
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("Moonward executable not found.", exe);
        }

        string userId = WindowsIdentity.GetCurrent().User?.Value
            ?? WindowsIdentity.GetCurrent().Name;
        string workingDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        string xml = BuildTaskXml(taskName, exe, startGameUrl, workingDir, userId);
        string xmlPath = Path.Combine(Path.GetTempPath(), $"Moonward_StartGameTask_{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks /XML 期望 UTF-16 LE
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            RunSchtasksCreate(GetTaskPath(taskName), xmlPath);
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* ignore */ }
        }
    }


    /// <summary>
    /// 是否为用户取消 UAC 导致的异常。
    /// </summary>
    public static bool IsElevationCancelled(Exception ex) =>
        ex is Win32Exception win32 && win32.NativeErrorCode == ErrorCancelled;


    /// <summary>
    /// 枚举本应用创建的免 UAC 启动任务完整路径（如 <c>\Moonward\StartGame_hk4e_cn_follow</c>）。
    /// 不需要管理员权限；查询失败时返回空列表。
    /// </summary>
    public static IReadOnlyList<string> ListStartGameTaskPaths()
    {
        try
        {
            string schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            var psi = new ProcessStartInfo
            {
                FileName = schtasks,
                Arguments = "/Query /FO CSV /NH",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // schtasks 本地化输出多为系统 ANSI/OEM；CSV 任务名本身为 ASCII，默认编码足够
                StandardOutputEncoding = Encoding.Default,
            };
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return [];
            }
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15_000);

            var result = new List<string>();
            foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string? taskPath = TryParseCsvTaskName(rawLine);
                if (taskPath is not null && IsOurStartGameTaskPath(taskPath))
                {
                    result.Add(NormalizeTaskPath(taskPath));
                }
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return [];
        }
    }


    /// <summary>
    /// 枚举本应用创建的免 UAC 启动任务，并解析任务名中的游戏、启动配置与登录 UID。
    /// </summary>
    /// <remarks>不能识别游戏区服的历史任务仍会返回，供用户手动清理。</remarks>
    public static IReadOnlyList<StartGameTaskInfo> ListStartGameTasks()
    {
        return ListStartGameTaskPaths().Select(ParseStartGameTask).ToList();
    }


    /// <summary>
    /// 删除全部本应用创建的免 UAC 启动计划任务。
    /// 非管理员时会弹出一次 UAC（批量删除）；用户取消时抛出 <see cref="Win32Exception"/>。
    /// </summary>
    /// <returns>清理前后计数。</returns>
    public static CleanupResult CleanupAllStartGameTasks()
    {
        return DeleteStartGameTasks(ListStartGameTaskPaths());
    }


    /// <summary>
    /// 删除指定的本应用免 UAC 启动任务。
    /// </summary>
    /// <param name="taskPaths">待删除的完整任务路径；仅当前仍存在且属于本应用的任务会被执行。</param>
    /// <returns>本次目标任务删除前后的计数。</returns>
    /// <exception cref="Win32Exception">用户取消 UAC，或 schtasks 进程启动失败。</exception>
    public static CleanupResult DeleteStartGameTasks(IEnumerable<string>? taskPaths)
    {
        if (taskPaths is null)
        {
            return new CleanupResult(0, 0);
        }

        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? taskPath in taskPaths)
        {
            if (!string.IsNullOrWhiteSpace(taskPath))
            {
                requested.Add(NormalizeTaskPath(taskPath));
            }
        }
        if (requested.Count == 0)
        {
            return new CleanupResult(0, 0);
        }

        // 只执行本次重新枚举到的任务，拒绝调用方传入任意 schtasks 路径。
        List<string> before = ListStartGameTaskPaths()
            .Where(requested.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (before.Count == 0)
        {
            return new CleanupResult(0, 0);
        }

        // 一次提权批量删除，避免每个任务各弹一次 UAC。
        var cmd = new StringBuilder();
        for (int i = 0; i < before.Count; i++)
        {
            if (i > 0)
            {
                cmd.Append(" & ");
            }
            // /F 强制、无确认
            cmd.Append($"""schtasks /Delete /TN "{before[i]}" /F""");
        }

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/c " + cmd,
            UseShellExecute = true,
            Verb = AppConfig.IsAdmin ? "" : "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        using Process? process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start cmd.exe for schtasks delete.");
        }
        process.WaitForExit();

        var after = new HashSet<string>(ListStartGameTaskPaths(), StringComparer.OrdinalIgnoreCase);
        int remaining = before.Count(after.Contains);
        return new CleanupResult(before.Count, remaining);
    }


    private static void RunSchtasksCreate(string taskPath, string xmlPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            // UseShellExecute + Verb=runas 时不能用 ArgumentList
            Arguments = $"""/Create /TN "{taskPath}" /XML "{xmlPath}" /F""",
            UseShellExecute = true,
            // 已是管理员则无需再弹 UAC
            Verb = AppConfig.IsAdmin ? "" : "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        using Process? process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start schtasks.exe.");
        }
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"schtasks /Create exited with code {process.ExitCode}.");
        }
    }


    /// <summary>
    /// 解析 <c>schtasks /Query /FO CSV /NH</c> 一行的首列任务名。
    /// </summary>
    private static string? TryParseCsvTaskName(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            return null;
        }
        // "TaskName","Next Run Time","Status"
        if (line[0] == '"')
        {
            int end = line.IndexOf('"', 1);
            if (end > 1)
            {
                return line[1..end];
            }
        }
        int comma = line.IndexOf(',');
        return comma > 0 ? line[..comma].Trim() : line;
    }


    /// <summary>
    /// 是否为本功能注册的任务路径（<c>\Moonward\StartGame_*</c>）。
    /// </summary>
    private static bool IsOurStartGameTaskPath(string taskPath)
    {
        string n = NormalizeTaskPath(taskPath);
        // \Moonward\StartGame_...
        string prefix = $@"\{TaskFolderName}\{TaskNamePrefix}";
        return n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }


    private static StartGameTaskInfo ParseStartGameTask(string taskPath)
    {
        string taskName = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
        string payload = taskName.StartsWith(TaskNamePrefix, StringComparison.OrdinalIgnoreCase)
            ? taskName[TaskNamePrefix.Length..]
            : string.Empty;

        foreach (GameBiz biz in GameBiz.AllGameBizs.OrderByDescending(x => x.Value.Length))
        {
            string prefix = biz.Value + "_";
            if (!payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string profileAndUid = payload[prefix.Length..];
            long? loginUid = null;
            int uidIndex = profileAndUid.LastIndexOf("_uid", StringComparison.OrdinalIgnoreCase);
            if (uidIndex >= 0 && long.TryParse(profileAndUid[(uidIndex + 4)..], out long uid) && uid > 0)
            {
                loginUid = uid;
                profileAndUid = profileAndUid[..uidIndex];
            }
            return new StartGameTaskInfo(taskPath, taskName, biz, profileAndUid, loginUid);
        }

        // 保留未知格式，以免版本升级后遗留任务无法由管理界面清理。
        return new StartGameTaskInfo(taskPath, taskName, null, payload, null);
    }


    private static string NormalizeTaskPath(string taskPath)
    {
        taskPath = taskPath.Trim().Trim('"');
        if (!taskPath.StartsWith('\\'))
        {
            taskPath = @"\" + taskPath;
        }
        return taskPath;
    }


    private static string BuildTaskXml(string taskName, string exePath, string startGameUrl, string workingDirectory, string userId)
    {
        // 用 LINQ to XML 保证 & / < 等在 URL 与路径中被正确转义
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", $"Moonward elevated start game ({taskName})"),
                    new XElement(ns + "URI", GetTaskPath(taskName))),
                new XElement(ns + "Triggers"),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userId),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "false"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "DisallowStartOnRemoteAppSession", "false"),
                    new XElement(ns + "UseUnifiedSchedulingEngine", "true"),
                    new XElement(ns + "WakeToRun", "false"),
                    // PT0S = 无执行时间限制（启游戏后 Moonward 会较快退出，但避免误杀）
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", exePath),
                        // 与协议注册一致：整段 URL 作为单个参数
                        new XElement(ns + "Arguments", $"\"{startGameUrl}\""),
                        new XElement(ns + "WorkingDirectory", workingDirectory)))));

        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            doc.Save(writer);
        }
        return sb.ToString();
    }


    private static string SanitizeTaskName(string name)
    {
        // 任务名不宜含 \ / 等；与文件名非法字符一并替换
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Replace('\\', '_').Replace('/', '_').Trim();
    }

}
