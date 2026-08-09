using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Starward.Features.TimeNode;

/// <summary>
/// 可点击时间节点条目外壳：支持手型光标（ProtectedCursor 须在 UIElement 子类内赋值）。
/// 使用 Grid 而非 sealed 的 Border，以便设置 ProtectedCursor 并保留常规布局能力。
/// </summary>
public sealed class TimeNodeItemBorder : Grid
{

    /// <summary>
    /// 按是否可点击切换手型光标。
    /// </summary>
    /// <param name="clickable">为 true 时显示手型。</param>
    public void SetClickableCursor(bool clickable)
    {
        try
        {
            ProtectedCursor = clickable
                ? InputSystemCursor.Create(InputSystemCursorShape.Hand)
                : null;
        }
        catch
        {
            // 忽略不支持的环境
        }
    }

}
