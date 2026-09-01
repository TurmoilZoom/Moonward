namespace Starward.Controls;

/// <summary>
/// 自定义即时 Tooltip 的触发方式。
/// 由附加属性 <see cref="InstantTooltip.TriggerProperty"/> 指定。
/// </summary>
public enum InstantTooltipTrigger
{
    /// <summary>
    /// 指针悬停即显示、移开即隐藏。默认值，用于图标按钮的功能名提示。
    /// </summary>
    Hover,

    /// <summary>
    /// 点击锚点显示，再次点击锚点或点击同一视觉树内的其它位置隐藏；悬停不再触发。
    /// 用于问号 / 感叹号这类需要停留阅读的说明性提示。
    /// </summary>
    Click,
}
