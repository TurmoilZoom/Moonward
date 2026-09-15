namespace Starward.Features.Gacha;

/// <summary>
/// 抽卡记录页卡片内 5★/4★ 记录区的视图模式。
/// <para>数值与页面标题栏视图切换 Segmented 的项顺序一一对应，勿调整顺序；设置以枚举名持久化。</para>
/// </summary>
public enum GachaRecordViewMode
{
    /// <summary>列表：每条记录一行（图标 + 名称 + 保底条 + 抽数）。</summary>
    List = 0,

    /// <summary>紧凑：图标网格，每条记录一个图标方块（下方迷你保底条 + 抽数），名称在悬停提示中显示。</summary>
    Compact = 1,
}
