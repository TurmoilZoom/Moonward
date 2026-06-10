using Starward.Core;

namespace Starward.Features.Gacha;

/// <summary>
/// 抽卡物品名称回写进度（值类型），供 <see cref="System.IProgress{T}"/> 上报与消息复用。
/// </summary>
/// <param name="Done">已处理的记录数。</param>
/// <param name="Total">需处理的记录总数。</param>
/// <param name="Completed">该游戏是否已全部完成。</param>
public readonly record struct GachaNameProgress(int Done, int Total, bool Completed);


/// <summary>
/// 广播某游戏抽卡物品名称回写进度的消息。
/// <see cref="GachaLogPage"/> 打开时订阅（按 <see cref="Game"/> 过滤）以显示处理进度。
/// </summary>
public class GachaItemNameProgressMessage
{

    /// <summary>正在回写名称的游戏业务线（hk4e / hkrpg / nap）。</summary>
    public GameBiz Game { get; }

    /// <summary>已处理的记录数。</summary>
    public int Done { get; }

    /// <summary>需处理的记录总数。</summary>
    public int Total { get; }

    /// <summary>该游戏是否已全部完成。</summary>
    public bool Completed { get; }


    public GachaItemNameProgressMessage(GameBiz game, GachaNameProgress progress)
    {
        Game = game;
        Done = progress.Done;
        Total = progress.Total;
        Completed = progress.Completed;
    }

}
