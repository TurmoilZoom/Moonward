using System.Collections.Generic;

namespace Starward.Features.RedeemCode;

/// <summary>
/// 兑换码展示快照（Service 输出，UI 只读）。
/// </summary>
internal class RedeemCodeSnapshot
{

    /// <summary>直播活动标题；可为空。</summary>
    public string? Title { get; init; }

    /// <summary>是否找到活动但尚未开播。</summary>
    public bool NotStarted { get; init; }

    /// <summary>计划开播时间文案（原始墙钟串）。</summary>
    public string? StartTimeText { get; init; }

    /// <summary>活动是否已结束。</summary>
    public bool IsEnded { get; init; }

    /// <summary>非空兑换码列表。</summary>
    public IReadOnlyList<RedeemCodeItem> Codes { get; init; } = [];

}


/// <summary>
/// 单条展示用兑换码。
/// </summary>
internal class RedeemCodeItem
{

    /// <summary>去 HTML 后的奖励说明。</summary>
    public string RewardText { get; init; } = "";

    /// <summary>兑换码明文。</summary>
    public string Code { get; init; } = "";

}
