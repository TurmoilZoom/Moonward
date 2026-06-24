using Microsoft.Extensions.Configuration;
using Starward.Core;
using Starward.Features.PlayTime;
using System;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// 游玩时长记录子进程：由游戏进程派生，上报本次会话时长后退出。
/// 对应命令行 <c>Starward.exe playtime --pid {pid} --biz {game_biz}</c>。
/// </summary>
internal sealed class PlayTimeStartupHandler : IStartupHandler
{
    public bool CanHandle(StartupContext context) =>
        context.Args is [var verb, ..] && string.Equals(verb, StartupVerbs.PlayTime, StringComparison.OrdinalIgnoreCase);

    public async Task<StartupOutcome> HandleAsync(StartupContext context)
    {
        int pid = context.Configuration.GetValue<int>("pid");
        GameBiz biz = (GameBiz)context.Configuration.GetValue<string>("biz");
        if (pid > 0)
        {
            await AppConfig.GetService<PlayTimeService>().LogPlayTimeAsync(biz, pid);
        }
        return StartupOutcome.Exit;
    }
}
