using Microsoft.Extensions.Configuration;
using Starward.Core;
using System.Diagnostics;

namespace Starward.Features.Startup;

/// <summary>
/// 一次应用启动的命令行上下文，在职责链中各 <see cref="IStartupHandler"/> 之间共享，
/// 避免每个处理器重复解析命令行；处理器也通过它把结果回传给宿主（<see cref="App"/>）。
/// </summary>
internal sealed class StartupContext
{
    /// <summary>已去除可执行文件路径的命令行参数；<c>Args[0]</c> 为用户传入的第一个参数。</summary>
    public string[] Args { get; }

    private IConfiguration? _configuration;

    /// <summary>
    /// 将 <see cref="Args"/> 解析为键值配置（<c>--pid 1234 --biz hk4e_cn</c> 等），首次访问时惰性构建。
    /// 仅在确有处理器读取时才解析，对 rpc / moonward:// 等不读取键值的模式零开销。
    /// </summary>
    public IConfiguration Configuration => _configuration ??= new ConfigurationBuilder().AddCommandLine(Args).Build();

    /// <summary>
    /// 本次启动是「启动游戏」请求（快捷方式 / 命令行 <c>startgame</c> / <c>moonward://startgame</c>）。
    /// 宿主据此只驻留系统托盘、不弹出主窗口。
    /// </summary>
    public bool IsGameLaunchRequest { get; set; }

    /// <summary>
    /// 本次启动过程中在当前进程内拉起的游戏。仅当没有常驻实例、本进程需转为常驻托盘实例时才有值；
    /// 宿主在创建托盘后据此把游戏登记进 <see cref="Features.Overlay.RunningGameService"/>。
    /// </summary>
    public (GameBiz Biz, Process Process)? LaunchedGame { get; set; }

    /// <summary>以命令行参数构造上下文。</summary>
    /// <param name="args">已去除可执行文件路径的命令行参数。</param>
    public StartupContext(string[] args)
    {
        Args = args;
    }
}
