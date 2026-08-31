using Serilog;
using Starward.Core;
using System.Diagnostics;

namespace Starward.Features.Startup;

/// <summary>
/// 「快捷方式 / 命令行启动游戏」成功后的收尾策略，由 <see cref="StartGameStartupHandler"/> 与
/// <see cref="Starward.Features.UrlProtocol.UrlProtocolService"/> 共用。
/// <para>
/// 已有常驻实例（系统托盘）时把游戏进程通知过去，让它登记为「运行中的游戏」，随后本进程退出；
/// 没有常驻实例时本进程转为常驻托盘实例，自己登记游戏并接管热键、手柄与 GameBar 引导键。
/// 这样无论走哪条路，全局热键截图与引导键接管都有一个知道「游戏正在跑」的宿主。
/// </para>
/// </summary>
internal static class GameLaunchStartupCoordinator
{

    /// <summary>
    /// 在快捷方式 / 命令行路径成功拉起游戏后调用，决定本进程是退出还是转为常驻实例。
    /// 结果写入 <paramref name="context"/>，由 <see cref="ResolveOutcome"/> 与宿主读取。
    /// </summary>
    /// <param name="context">本次启动的命令行上下文。</param>
    /// <param name="biz">游戏区服。</param>
    /// <param name="process">已拉起的游戏进程；启动失败时为 <see langword="null"/>。</param>
    public static void AfterGameStarted(StartupContext context, GameBiz biz, Process? process)
    {
        context.IsGameLaunchRequest = true;
        if (process is null)
        {
            // 启动失败（调用方已弹窗/记日志）：维持原有「用完即退」行为，不平白留下一个托盘
            return;
        }
        if (ResidentInstanceMessenger.NotifyGameStarted(biz, process.Id))
        {
            Log.Information("Game started by shortcut, notified resident instance ({biz}, {pid})", biz, process.Id);
            return;
        }
        // 没有常驻实例：本进程留下来当托盘
        Log.Information("Game started by shortcut, becoming the resident instance ({biz}, {pid})", biz, process.Id);
        context.LaunchedGame = (biz, process);
    }


    /// <summary>
    /// 「启动游戏」类启动模式的宿主动作：需转为常驻托盘实例时放行，否则用完即退。
    /// </summary>
    /// <param name="context">本次启动的命令行上下文。</param>
    public static StartupOutcome ResolveOutcome(StartupContext context)
    {
        return context.LaunchedGame is null ? StartupOutcome.Exit : StartupOutcome.Continue;
    }

}
