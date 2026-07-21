using Starward.Features.UrlProtocol;
using System;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// <c>moonward://</c> 自定义协议激活：委托 <see cref="UrlProtocolService.HandleUrlProtocolAsync(string)"/> 处理。
/// 已成功识别并处理则退出；未识别（返回 <see langword="false"/>）则放行，由宿主继续正常启动。
/// </summary>
/// <remarks>
/// <c>moonward://test/</c> 调试窗口须在环境初始化之前由 <see cref="App"/> 单独处理，不经此处。
/// </remarks>
internal sealed class UrlProtocolStartupHandler : IStartupHandler
{
    public bool CanHandle(StartupContext context) =>
        context.Args is [var arg, ..] && arg.StartsWith(StartupVerbs.UrlProtocolScheme, StringComparison.OrdinalIgnoreCase);

    public async Task<StartupOutcome> HandleAsync(StartupContext context)
    {
        if (await UrlProtocolService.HandleUrlProtocolAsync(context.Args[0]))
        {
            return StartupOutcome.Exit;
        }
        return StartupOutcome.Continue;
    }
}
