using Starward.Core;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 游戏已启动的通知。快捷方式启动时由 <c>ResidentInstanceMessenger</c> 在常驻实例内代为广播，
/// 主窗口据此执行「游戏启动后」动作，首页据此补检运行状态。
/// </summary>
/// <param name="gameBiz">被启动游戏的区服。</param>
public class GameStartedMessage(GameBiz gameBiz)
{

    /// <summary>
    /// 被启动游戏的区服。
    /// </summary>
    public GameBiz GameBiz { get; } = gameBiz;

}
