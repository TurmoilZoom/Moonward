using Microsoft.Extensions.Configuration;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// 命令行直接启动游戏：解析 <c>--biz</c> 指定的游戏后启动。
/// 对应命令行 <c>Moonward.exe startgame --biz {game_biz}</c>。
/// <para>
/// 启动成功后不再一律退出：已有常驻实例则通知它登记本次游戏进程，否则本进程转为常驻托盘实例，
/// 以便全局热键截图与 GameBar 引导键接管能正常工作（见 <see cref="GameLaunchStartupCoordinator"/>）。
/// </para>
/// </summary>
internal sealed class StartGameStartupHandler : IStartupHandler
{
    public bool CanHandle(StartupContext context) =>
        context.Args is [var verb, ..] && string.Equals(verb, StartupVerbs.StartGame, StringComparison.OrdinalIgnoreCase);

    public async Task<StartupOutcome> HandleAsync(StartupContext context)
    {
        GameBiz biz = (GameBiz)context.Configuration.GetValue<string>("biz");
        if (GameId.FromGameBiz(biz) is GameId gameId)
        {
            // 与首页「开始游戏」一致：按当前生效的启动方式（默认「无」）。
            AppConfig.ResolveLaunchProfile(biz, AppConfig.GetActiveLaunchProfileId(biz), out bool useNone, out GameLaunchProfile? profile);
            Process? process = await AppConfig.GetService<GameLauncherService>().StartGameAsync(gameId, profile: profile, useNoneLaunchMethod: useNone);
            GameLaunchStartupCoordinator.AfterGameStarted(context, biz, process);
            return GameLaunchStartupCoordinator.ResolveOutcome(context);
        }
        return StartupOutcome.Exit;
    }
}
