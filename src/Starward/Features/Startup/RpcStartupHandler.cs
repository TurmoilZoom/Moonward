using Starward.RPC;
using System;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// RPC 子进程模式：执行 <see cref="RpcRunner.Run(string[])"/>（提权后的后台服务）后退出。
/// 对应命令行 <c>Moonward.exe rpc …</c>。
/// </summary>
internal sealed class RpcStartupHandler : IStartupHandler
{
    public bool CanHandle(StartupContext context) =>
        context.Args is [var verb, ..] && string.Equals(verb, StartupVerbs.Rpc, StringComparison.OrdinalIgnoreCase);

    public Task<StartupOutcome> HandleAsync(StartupContext context)
    {
        RpcRunner.Run(context.Args);
        return Task.FromResult(StartupOutcome.Exit);
    }
}
