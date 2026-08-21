using Microsoft.Extensions.Logging;
using Microsoft.Win32;
#if !DEBUG
using NuGet.Versioning;
#endif
using Starward.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace Starward.Features.Feedback;

/// <summary>
/// 从应用内打开 GitHub Bug 表单：预填环境信息，并打开日志文件夹。
/// 不调用 GitHub API，也不上传文件。
/// </summary>
internal class IssueReportService
{

    private const string NewIssueUrl = "https://github.com/TurmoilZoom/Moonward/issues/new";

    private const string TemplateChooserUrl = "https://github.com/TurmoilZoom/Moonward/issues/new/choose";

    private const string BugTemplateFile = "bug_report.yml";

    private readonly ILogger<IssueReportService> _logger;


    /// <summary>
    /// 初始化问题反馈服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    public IssueReportService(ILogger<IssueReportService> logger)
    {
        _logger = logger;
    }


    /// <summary>
    /// 打开日志文件夹，并用系统浏览器打开预填的 Bug 表单。
    /// 单步失败不阻断其余步骤；表单打不开时回退到模板选择页。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ReportBugAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await OpenLogFolderAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open log folder for issue report");
        }

        bool openedForm = false;
        try
        {
            openedForm = await Launcher.LaunchUriAsync(BuildBugReportUri());
            if (!openedForm)
            {
                openedForm = await Launcher.LaunchUriAsync(new Uri(TemplateChooserUrl));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open bug report form");
            try
            {
                openedForm = await Launcher.LaunchUriAsync(new Uri(TemplateChooserUrl));
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Open issue template chooser");
            }
        }

        if (!openedForm)
        {
            InAppToast.MainWindow?.Error(Lang.IssueReport_OpenFailed);
            return;
        }

        InAppToast.MainWindow?.Information(Lang.IssueReport_Opened, Lang.IssueReport_OpenLogHint, 8000);
    }


    /// <summary>
    /// 组装只预填 <c>environment</c> 的 Bug 表单 URL。软件能采集的信息都写在该栏。
    /// </summary>
    /// <returns>指向 <c>bug_report.yml</c> 的 GitHub 新建 Issue 地址。</returns>
    public Uri BuildBugReportUri()
    {
        string query = $"template={Uri.EscapeDataString(BugTemplateFile)}"
                       + $"&environment={Uri.EscapeDataString(BuildEnvironmentText())}";
        return new Uri($"{NewIssueUrl}?{query}");
    }


    /// <summary>
    /// 打开当天日志所在文件夹，并尽量选中当前日志文件。
    /// </summary>
    private static async Task OpenLogFolderAsync()
    {
        if (!string.IsNullOrWhiteSpace(AppConfig.LogFile) && File.Exists(AppConfig.LogFile))
        {
            string? folder = Path.GetDirectoryName(AppConfig.LogFile);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var item = await StorageFile.GetFileFromPathAsync(AppConfig.LogFile);
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(item);
                await Launcher.LaunchFolderPathAsync(folder, options);
                return;
            }
        }

        string fallback = Path.Combine(AppConfig.CacheFolder ?? "", "log");
        if (Directory.Exists(fallback))
        {
            await Launcher.LaunchFolderPathAsync(fallback);
        }
    }


    /// <summary>
    /// 生成诊断快照：版本、系统、架构、渠道、语言、安装方式。不含账号与凭证。
    /// </summary>
    private static string BuildEnvironmentText()
    {
        string language = string.IsNullOrWhiteSpace(AppConfig.Language)
            ? CultureInfo.CurrentUICulture.Name
            : AppConfig.Language;
        return $"""
            Moonward: {AppConfig.AppVersion}
            Windows: {ResolveWindowsLabel()}
            Architecture: {ResolveArchitecture()}
            Channel: {ResolveChannel()}
            Language: {language}
            Portable: {(AppConfig.IsPortable ? "Yes" : "No")}
            Removable storage: {(AppConfig.IsAppInRemovableStorage ? "Yes" : "No")}
            """;
    }


    /// <summary>
    /// 当前进程架构，与模板约定文案一致。
    /// </summary>
    private static string ResolveArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            Architecture.X86 => "x86",
            _ => "不确定 (Not sure)",
        };
    }


    /// <summary>
    /// 当前已安装构建的渠道：调试构建、预览版或正式版。
    /// 按版本号判断，不用「加入预览更新」开关——该开关只影响以后拉更新。
    /// </summary>
    private static string ResolveChannel()
    {
#if DEBUG
        return "自行编译 (Built from source)";
#else
        if (NuGetVersion.TryParse(AppConfig.AppVersion, out NuGetVersion? version) && version.IsPrerelease)
        {
            return "预览版 (Preview)";
        }
        return "正式版 (Stable)";
#endif
    }


    /// <summary>
    /// 系统产品名、发行版号与完整构建号。Win11 内核仍是 10.0，且 <see cref="Environment.OSVersion"/> 不含 UBR。
    /// </summary>
    /// <returns>例如 <c>Windows 11 25H2 (10.0.26200.9168)</c>。</returns>
    private static string ResolveWindowsLabel()
    {
        Version os = Environment.OSVersion.Version;
        int build = os.Build;
        int revision = os.Revision;
        string? displayVersion = null;
        string? installationType = null;

        try
        {
            const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            displayVersion = Registry.GetValue(key, "DisplayVersion", null) as string;
            if (string.IsNullOrWhiteSpace(displayVersion))
            {
                displayVersion = Registry.GetValue(key, "ReleaseId", null) as string;
            }
            installationType = Registry.GetValue(key, "InstallationType", null) as string;
            if (Registry.GetValue(key, "UBR", null) is int ubr)
            {
                revision = ubr;
            }
            if (Registry.GetValue(key, "CurrentBuildNumber", null) is string buildText
                && int.TryParse(buildText, out int parsedBuild)
                && parsedBuild > 0)
            {
                build = parsedBuild;
            }
        }
        catch
        {
            // 读注册表失败时退回 Environment.OSVersion
        }

        string family = ResolveWindowsFamily(build, installationType);
        string nt = $"{os.Major}.{os.Minor}.{build}.{revision}";
        if (!string.IsNullOrWhiteSpace(displayVersion))
        {
            return $"{family} {displayVersion} ({nt})";
        }
        return $"{family} ({nt})";
    }


    /// <summary>
    /// 按构建号与安装类型区分 Win11 / Win10 / Server / 其他。Win11 与 Win10 的 NT 主版本都是 10.0，以 22000 为界。
    /// </summary>
    private static string ResolveWindowsFamily(int build, string? installationType)
    {
        if (string.Equals(installationType, "Server", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows Server";
        }
        // 与项目内其它 Win11 判断一致（WelcomeWindow / OverlayWindow 等）
        if (build >= 22000)
        {
            return "Windows 11";
        }
        // Win10 公开版本构建号从 10240 起
        if (build >= 10240)
        {
            return "Windows 10";
        }
        return "Windows";
    }

}
