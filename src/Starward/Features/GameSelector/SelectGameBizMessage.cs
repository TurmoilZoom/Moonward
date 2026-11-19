using Starward.Core;

namespace Starward.Features.GameSelector;

/// <summary>
/// 让游戏选择器切到指定游戏（点击系统通知打开主窗口时）。
/// </summary>
/// <param name="gameBiz">要切到的游戏区服，通常是战绩角色的区服（B 服角色也记为国服）。</param>
internal sealed class SelectGameBizMessage(GameBiz gameBiz)
{

    /// <summary>要切到的游戏区服。</summary>
    public GameBiz GameBiz { get; } = gameBiz;


    /// <summary>
    /// 决定实际要选中的区服：战绩角色没有 B 服之分，当前已选的是同一游戏的 B 服、而目标是国服时保持不变，
    /// 免得玩 B 服的用户点了通知被切到国服。
    /// </summary>
    /// <param name="current">当前选中的区服。</param>
    /// <param name="target">通知对应的区服。</param>
    /// <returns>应选中的区服。</returns>
    public static GameBiz ResolveTarget(GameBiz current, GameBiz target)
    {
        if (current.Game == target.Game && current.Server == "bilibili" && target.Server == "cn")
        {
            return current;
        }
        return target;
    }

}
