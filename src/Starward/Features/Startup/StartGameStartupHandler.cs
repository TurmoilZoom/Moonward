using Microsoft.Extensions.Configuration;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord.SignIn;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// 命令行直接启动游戏：解析 <c>--biz</c> 指定的游戏后启动，随即退出。
/// 对应命令行 <c>Starward.exe startgame --biz {game_biz}</c>。
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
            // 与 URL 启动一致：不经主界面批量签到时，对配置中绑定的账号静默签到
            if (process is not null)
            {
                long resolvedLoginUid = GameLauncherService.ResolveLoginUid(biz, profile, useNone, explicitLoginUid: null);
                if (resolvedLoginUid > 0)
                {
                    await AppConfig.GetService<AutoSignInService>().TrySignInForLaunchAccountAsync(biz, resolvedLoginUid);
                }
            }
        }
        return StartupOutcome.Exit;
    }
}
