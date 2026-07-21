using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// 启动处理器对一次启动的处置结果。由宿主（<see cref="App"/>）据此决定进程后续走向，
/// 处理器自身不再直接调用 <see cref="System.Environment.Exit(int)"/>，将进程生命周期集中收口到宿主。
/// </summary>
internal enum StartupOutcome
{
    /// <summary>未识别该启动模式，或虽已识别但选择放行；宿主继续正常的单实例启动流程。</summary>
    Continue,

    /// <summary>启动模式已完成其工作（如已派发子进程任务或已启动游戏）；宿主应退出当前进程。</summary>
    Exit,
}


/// <summary>
/// 单一命令行启动模式的处理器，是「职责链（Chain of Responsibility）」中的一环。
/// 每种特殊启动参数（rpc / playtime / startgame / moonward:// 等）实现为一个独立处理器，
/// 由 <see cref="App.OnLaunched"/> 在环境初始化后按注册顺序依次询问，取代原先硬编码的 if 分支。
/// </summary>
/// <remarks>
/// 处理器在 <see cref="AppConfig"/> 的 DI 容器中以 <c>IStartupHandler</c> 多实例注册，
/// 注册顺序即匹配优先级。新增一种启动模式 = 新建一个实现类 + 在容器中注册一行（开闭原则）。
/// </remarks>
internal interface IStartupHandler
{
    /// <summary>判断本处理器是否负责处理当前启动参数（通常匹配 <see cref="StartupContext.Args"/> 的首个参数）。</summary>
    /// <param name="context">本次启动的命令行上下文。</param>
    bool CanHandle(StartupContext context);

    /// <summary>执行该启动模式的工作，并返回宿主应采取的后续动作。</summary>
    /// <param name="context">本次启动的命令行上下文。</param>
    Task<StartupOutcome> HandleAsync(StartupContext context);
}
