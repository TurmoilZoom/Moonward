using Vanara.PInvoke;


namespace Starward.Helpers;


/// <summary>
/// 鼠标按键状态。
/// </summary>
internal static class MouseButtonHelper
{
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;


    /// <summary>
    /// 鼠标主键此刻是否按着。
    /// <para>GetAsyncKeyState 查的是物理按键，系统设置了左右键互换时，主键是物理右键。</para>
    /// </summary>
    /// <returns>按着时返回 <see langword="true"/>。</returns>
    public static bool IsPrimaryButtonPressed()
    {
        int button = User32.GetSystemMetrics(User32.SystemMetric.SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        return User32.GetAsyncKeyState(button) < 0;
    }

}
