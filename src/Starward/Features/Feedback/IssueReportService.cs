using Microsoft.Extensions.Logging;
using Starward.Core;
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
/// 从应用内打开 GitHub Bug 表单：预填环境与游戏信息，并打开日志文件夹。
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
    /// 生成诊断快照：版本、系统、架构、渠道、当前游戏与区服、语言、安装方式。不含账号与凭证。
    /// </summary>
    private static string BuildEnvironmentText()
    {
        GameBiz biz = AppConfig.CurrentGameBiz;
        string language = string.IsNullOrWhiteSpace(AppConfig.Language)
            ? CultureInfo.CurrentUICulture.Name
            : AppConfig.Language;
        return $"""
            Moonward: {AppConfig.AppVersion}
            Windows: {Environment.OSVersion.Version}
            Architecture: {ResolveArchitecture()}
            Channel: {ResolveChannel()}
            Game: {ResolveGameLabel(biz)}
            Server: {ResolveServerLabel(biz)}
            Language: {language}
            Portable: {(AppConfig.IsPortable ? "Yes" : "No")}
            Removable storage: {(AppConfig.IsAppInRemovableStorage ? "Yes" : "No")}
            """;
    }


    /// <summary>
    /// 将当前 <see cref="GameBiz"/> 映射为模板「涉及游戏」文案。
    /// </summary>
    private static string ResolveGameLabel(GameBiz biz)
    {
        return biz.Game switch
        {
            GameBiz.hk4e => "原神 (Genshin Impact)",
            GameBiz.hkrpg => "崩坏：星穹铁道 (Honkai: Star Rail)",
            GameBiz.nap => "绝区零 (Zenless Zone Zero)",
            GameBiz.bh3 => "崩坏3 (Honkai Impact 3rd)",
            _ => "启动器本身 (Moonward)",
        };
    }


    /// <summary>
    /// 将当前 <see cref="GameBiz"/> 映射为模板「区服」文案。
    /// </summary>
    private static string ResolveServerLabel(GameBiz biz)
    {
        if (string.IsNullOrWhiteSpace(biz.Value))
        {
            return "不适用 (N/A)";
        }
        if (biz.IsChinaServer() || biz.IsBilibili())
        {
            return "国服 (CN)";
        }
        if (biz.IsGlobalServer())
        {
            return "国际服 (OS)";
        }
        return "不适用 (N/A)";
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
    /// 当前安装渠道：调试构建、预览版或正式版。
    /// </summary>
    private static string ResolveChannel()
    {
#if DEBUG
        return "自行编译 (Built from source)";
#else
        return AppConfig.EnablePreviewRelease ? "预览版 (Preview)" : "正式版 (Stable)";
#endif
    }

}
