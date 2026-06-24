using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Starward.Features.GamepadControl;
using Starward.Features.Startup;
using Starward.Features.UrlProtocol;
using Starward.Features.ViewHost;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;


namespace Starward;

/// <summary>
/// WinUI 应用程序入口类，负责应用生命周期、单实例管理、启动参数分发与全局异常日志记录。
/// </summary>
public partial class App : Application
{

    /// <summary>
    /// 当前 UI 线程的 <see cref="DispatcherQueue"/>，用于将跨实例激活事件调度回主线程。
    /// </summary>
    private readonly DispatcherQueue _uiDispatcherQueue;

    /// <summary>
    /// 定期触发 GC 的定时器，间隔 60 秒，用于缓解长时间运行时的内存占用。
    /// </summary>
    private readonly Timer _gcTimer = new(TimeSpan.FromSeconds(60));

    /// <summary>
    /// 获取当前 <see cref="App"/> 实例（强类型封装 <see cref="Application.Current"/>）。
    /// </summary>
    public static new App Current => (App)Application.Current;


    /// <summary>
    /// 初始化应用程序：加载 XAML 资源、设置默认主题、注册全局异常处理与 GC 定时器。
    /// </summary>
    public App()
    {
        this.InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += App_UnhandledException;
        _gcTimer.Elapsed += (_, _) => GC.Collect();
    }


    /// <summary>
    /// 全局未处理异常处理器，将崩溃信息及附加数据写入日志文件。
    /// </summary>
    /// <param name="sender">事件源（<see cref="Application"/>）。</param>
    /// <param name="e">未处理异常事件参数，包含 <see cref="Microsoft.UI.Xaml.UnhandledExceptionEventArgs.Exception"/>。</param>
    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 优先使用 AppConfig 中已配置的日志路径，否则回退到 LocalAppData\Starward\log
        string logFile = AppConfig.LogFile;
        if (string.IsNullOrWhiteSpace(logFile))
        {
            string logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starward", "log");
            Directory.CreateDirectory(logFolder);
            logFile = Path.Combine(logFolder, $"Starward_{DateTime.Now:yyMMdd}.log");
        }

        // 组装崩溃日志：时间戳、异常堆栈、Exception.Data 中的键值对
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] App Crash:");
        sb.AppendLine(e.Exception.ToString());
        if (e.Exception.Data.Count > 0)
        {
            foreach (DictionaryEntry item in e.Exception.Data)
            {
                sb.AppendLine($"{item.Key}: {item.Value}");
            }
        }

        // 以追加模式写入日志，允许其他进程并发读取或删除
        using var fs = File.Open(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var sw = new StreamWriter(fs);
        sw.Write(sb);
    }


    /// <summary>
    /// 应用启动入口。解析命令行参数，处理测试/特殊启动模式，并确保单实例运行。
    /// </summary>
    /// <param name="_">启动激活事件参数（WinUI 框架传入，本方法未使用）。</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs _)
    {
        // 去掉可执行文件路径，仅保留用户传入的参数，那么args[0]为第一个参数
        string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        // 开发/调试：starward://test/ 调试窗口。须在环境初始化（及 DI 容器构建）之前处理，以跳过
        // 数据目录选择/迁移等副作用，故此分支不进入下方的启动处理器职责链。
        if (args is [var first, ..] && first.StartsWith(StartupVerbs.TestUrlProtocolPrefix, StringComparison.OrdinalIgnoreCase))
        {
            new TestUrlProtocolWindow().Activate();
            return;
        }

        // 环境检查：数据目录、配置、服务等初始化
        await AppConfig.CheckEnviromentAsync();

        // 特殊启动模式（rpc / playtime / startgame / starward://）：交由启动处理器职责链分发；若已接管则直接返回
        if (await DispatchStartupAsync(new StartupContext(args)))
        {
            return;
        }

        // 单实例：注册 main 键，非当前实例时将激活重定向到已运行实例并退出
        instance = AppInstance.GetCurrent();
        instance.Activated += AppInstance_Activated;

        var main = AppInstance.FindOrRegisterForKey("main");
        if (!main.IsCurrent)
        {
            await main.RedirectActivationToAsync(instance.GetActivatedEventArgs());
            Environment.Exit(0);
        }

        // --hide 参数：仅启动系统托盘，不显示主窗口
        if (args.Contains("--hide"))
        {
            m_SystemTrayWindow = new SystemTrayWindow();
        }
        else
        {
            m_MainWindow = new MainWindow();
            m_MainWindow.Activate();
        }
    }


    /// <summary>
    /// 按注册顺序运行启动处理器职责链（<see cref="IStartupHandler"/>），分发 rpc / playtime /
    /// startgame / starward:// 等特殊启动模式。命中 <see cref="StartupOutcome.Exit"/> 的处理器在此处
    /// 统一终止进程，各处理器自身不再负责进程生命周期。
    /// </summary>
    /// <param name="context">本次启动的命令行上下文。</param>
    /// <returns>若已被某处理器接管（进程即将退出）则返回 <see langword="true"/>；否则返回 <see langword="false"/> 以继续正常启动。</returns>
    private static async Task<bool> DispatchStartupAsync(StartupContext context)
    {
        foreach (IStartupHandler handler in AppConfig.GetService<IEnumerable<IStartupHandler>>())
        {
            if (!handler.CanHandle(context))
            {
                continue;
            }
            if (await handler.HandleAsync(context) is StartupOutcome.Exit)
            {
                Environment.Exit(0);
                return true; // 不可达：Environment.Exit 已终止进程
            }
            // Continue：已识别但放行（例如未注册的 starward:// 主机名），继续询问后续处理器
        }
        return false;
    }



    /// <summary>
    /// 当前进程的 <see cref="AppInstance"/>，用于单实例注册与跨实例激活重定向。
    /// </summary>
    private AppInstance instance;

    /// <summary>
    /// 主窗口实例；可能为 <see langword="null"/>（例如仅以 --hide 启动托盘时）。
    /// </summary>
    private MainWindow m_MainWindow;

    /// <summary>
    /// 系统托盘窗口实例；仅在 --hide 启动或需要后台驻留时创建。
    /// </summary>
    private SystemTrayWindow m_SystemTrayWindow;



    /// <summary>
    /// 确保主窗口已创建、激活并显示。若窗口不存在则懒加载创建。
    /// </summary>
    public void EnsureMainWindow()
    {
        m_MainWindow ??= new MainWindow();
        m_MainWindow.Activate();
        m_MainWindow.Show();
    }


    /// <summary>
    /// 确保系统托盘窗口已创建。若窗口不存在则懒加载创建（不主动显示主窗口）。
    /// </summary>
    public void EnsureSystemTray()
    {
        m_SystemTrayWindow ??= new SystemTrayWindow();
    }



    /// <summary>
    /// 单实例激活回调：当其他实例将激活重定向到本进程时，在 UI 线程上显示主窗口。
    /// </summary>
    /// <param name="sender">事件源（<see cref="AppInstance"/>）。</param>
    /// <param name="e">激活参数，包含启动来源与附带数据。</param>
    private void AppInstance_Activated(object? sender, AppActivationArguments e)
    {
        _uiDispatcherQueue.TryEnqueue(EnsureMainWindow);
    }



    /// <summary>
    /// 在所有已注册的 <see cref="AppInstance"/> 中按 key 查找对应实例。
    /// </summary>
    /// <param name="key">实例注册键（如 "main"）。</param>
    /// <returns>匹配的 <see cref="AppInstance"/>；未找到时返回 <see langword="null"/>。</returns>
    public static AppInstance? FindInstanceForKey(string key)
    {
        foreach (var item in AppInstance.GetInstances())
        {
            if (item.Key == key)
            {
                return item;
            }
        }
        return null;
    }



    /// <summary>
    /// 退出应用程序：恢复手柄引导键设置，关闭主窗口与托盘窗口，并终止应用进程。
    /// </summary>
    public new void Exit()
    {
        GamepadController.RestoreGamepadGuideButtonForGameBar();
        m_MainWindow?.Close();
        m_SystemTrayWindow?.Close();
        Application.Current.Exit();
    }



}