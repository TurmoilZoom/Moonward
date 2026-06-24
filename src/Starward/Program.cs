global using Starward.Language;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using Velopack;

namespace Starward;

#if DISABLE_XAML_GENERATED_MAIN

/// <summary>
/// Program class
/// </summary>
public static class Program
{


    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2411")]
    //[global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.STAThreadAttribute]
    static void Main(string[] args)
    {
        // Velopack：必须是 Main 中最早执行的代码。
        // 安装/更新/卸载等 hook 会在此处理并直接退出进程，不会进入 WinUI。
        // 卸载钩子（控制面板卸载或 Update.exe uninstall 都会触发）按用户设置清理数据目录与注册表残留，
        // 钩子有 30 秒超时且禁止任何 UI，故 PerformUninstallCleanup 全程静默。
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Features.Setting.AppUninstallService.PerformUninstallCleanup())
            .Run();

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        string logFile = AppConfig.LogFile;
        if (string.IsNullOrWhiteSpace(logFile))
        {
            string logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starward", "log");
            Directory.CreateDirectory(logFolder);
            logFile = Path.Combine(logFolder, $"Starward_{DateTime.Now:yyMMdd}.log");
        }
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Program Crash:");
        sb.AppendLine(e.ExceptionObject.ToString());
        if (e.ExceptionObject is Exception { Data.Count: > 0 } ex)
        {
            foreach (DictionaryEntry item in ex.Data)
            {
                sb.AppendLine($"{item.Key}: {item.Value}");
            }
        }
        using var fs = File.Open(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var sw = new StreamWriter(fs);
        sw.Write(sb);
    }
}

#endif


