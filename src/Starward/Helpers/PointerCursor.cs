using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;

namespace Starward.Helpers;

/// <summary>
/// 为任意 <see cref="UIElement"/> 设置系统指针光标形状的附加属性帮助类。
/// <para>
/// WinUI 3 未公开稳定的「改元素悬停光标」API；光标由元素的 protected 属性
/// <c>ProtectedCursor</c> 控制。本类通过 <see cref="UnsafeAccessorAttribute"/> 调用
/// 其 setter，并在 XAML 中以附加属性形式暴露（如 <c>helpers:PointerCursor.CursorShape="Hand"</c>）。
/// </para>
/// <para>典型用途：可点击/可拖拽热区（按钮、轮播图、抽卡统计卡片拖拽手柄等）悬停显示手形光标。</para>
/// </summary>
public class PointerCursor : DependencyObject
{
    /// <summary>
    /// 附加属性：元素悬停时使用的系统光标形状。
    /// 在 XAML 中写 <c>helpers:PointerCursor.CursorShape="Hand"</c> 即可；代码侧请用
    /// <see cref="SetCursorShape"/> / <see cref="GetCursorShape"/>。
    /// </summary>
    public static readonly DependencyProperty CursorShapeProperty =
        DependencyProperty.RegisterAttached("CursorShape", typeof(InputSystemCursorShape), typeof(PointerCursor), new PropertyMetadata(default));

    /// <summary>
    /// 为 <paramref name="element"/> 设置悬停光标形状，并同步写入 <see cref="CursorShapeProperty"/>。
    /// </summary>
    /// <param name="element">要改变光标的目标元素；不可为 null。</param>
    /// <param name="value">系统光标形状（如 <see cref="InputSystemCursorShape.Hand"/>、Arrow 等）。</param>
    public static void SetCursorShape(UIElement element, InputSystemCursorShape value)
    {
        // 先写入 ProtectedCursor，指针进入该元素命中区域时系统会使用此光标。
        SetProtectedCursor(element, InputSystemCursor.Create(value));
        element.SetValue(CursorShapeProperty, value);
    }

    /// <summary>
    /// 读取 <paramref name="element"/> 上已设置的光标形状附加属性值。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <returns>
    /// 已设置的 <see cref="InputSystemCursorShape"/>；未设置或类型不符时回退为
    /// <see cref="InputSystemCursorShape.Arrow"/>。
    /// </returns>
    public static InputSystemCursorShape GetCursorShape(UIElement element)
    {
        return element.GetValue(CursorShapeProperty) switch
        {
            InputSystemCursorShape e => e,
            _ => InputSystemCursorShape.Arrow,
        };
    }

    /// <summary>
    /// 通过 <see cref="UnsafeAccessorAttribute"/> 访问 <see cref="UIElement"/> 的
    /// protected 实例方法 <c>set_ProtectedCursor</c>，从而设置元素级光标。
    /// <para>
    /// 这是非公开 API 的访问方式：框架升级若改名/移除该成员，此处会在运行时失败，
    /// 届时需重新核对 WinUI 源码中的对应成员名。
    /// </para>
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <param name="cursor">要应用的 <see cref="InputCursor"/> 实例。</param>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_ProtectedCursor")]
    static extern void SetProtectedCursor(UIElement element, InputCursor cursor);
}
