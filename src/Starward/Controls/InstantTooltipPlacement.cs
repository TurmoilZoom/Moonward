namespace Starward.Controls;

/// <summary>
/// 自定义即时 Tooltip 相对锚点元素的显示方位。
/// 由附加属性 <see cref="InstantTooltip.PlacementProperty"/> 指定，
/// 影响 <see cref="InstantTooltipHost"/> 的 Popup 偏移与入场缩放原点。
/// </summary>
public enum InstantTooltipPlacement
{
    /// <summary>
    /// 显示在锚点右侧（垂直居中）。导航 LeftCompact 侧栏默认值。
    /// </summary>
    Right,

    /// <summary>
    /// 显示在锚点左侧（垂直居中）。启动器右侧按钮（签到、实时便笺等）常用。
    /// </summary>
    Left,

    /// <summary>
    /// 显示在锚点上方（水平居中）。
    /// </summary>
    Top,

    /// <summary>
    /// 显示在锚点下方（水平居中）。
    /// </summary>
    Bottom,
}
