using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Vanara.PInvoke;

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
            try
            {
                // 与首页「开始游戏」一致：按当前生效的启动方式（默认「无」）。
                AppConfig.ResolveLaunchProfile(biz, AppConfig.GetActiveLaunchProfileId(biz), out bool useNone, out GameLaunchProfile? profile);
                Process? process = await AppConfig.GetService<GameLauncherService>().StartGameAsync(gameId, profile: profile, useNoneLaunchMethod: useNone);
                GameLaunchStartupCoordinator.AfterGameStarted(context, biz, process);
                return GameLaunchStartupCoordinator.ResolveOutcome(context);
            }
            catch (Exception ex)
            {
                // 必须就地收口：本处理器由 async void 的 App.OnLaunched 驱动，异常逃出去会被抛回消息泵，
                // App_UnhandledException 只记日志不置 Handled，进程直接终止且用户看不到任何提示。
                // 行为与 moonward://startgame 对齐：记日志 + 弹窗，然后用完即退。
                ILogger<StartGameStartupHandler> logger = AppConfig.GetLogger<StartGameStartupHandler>();
                if (ex is GameRunningException)
                {
                    // 可预期：重复启动同一个游戏，不是故障，不留堆栈
                    logger.LogInformation("Start game by command line: {message}", ex.Message);
                }
                else
                {
                    logger.LogError(ex, "Start game by command line ({biz})", biz);
                }
                User32.MessageBox(HWND.NULL, GameLaunchStartupCoordinator.GetLaunchErrorMessage(ex), "Moonward");
            }
        }
        return StartupOutcome.Exit;
    }
}
