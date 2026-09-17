using Microsoft.UI.Xaml.Controls;
using Starward.Features.Codec;
using Starward.Helpers;
using Starward.Language;
using System;
using Windows.System;

namespace Starward.Features.Background;

/// <summary>
/// 「需要 HEVC 视频扩展」提示，带跳转商店的下载按钮。
/// 视频背景、好感壁纸的悬停预览与「设为背景」三处共用，免得各弹各的叠成好几条。只能在 UI 线程调用。
/// </summary>
internal static class HevcVideoExtensionToast
{

    /// <summary>
    /// 提示当前显示在哪个 Toast 容器里，用户点关闭后清空。
    /// 不用布尔值：主窗口关到托盘后会重建，旧提示随旧窗口消失却收不到关闭回调，布尔值会一直卡在「已打开」。
    /// </summary>
    private static InAppToast? _openOn;

    /// <summary>本次运行里是否弹过。</summary>
    private static bool _shown;


    /// <summary>
    /// 弹出提示；上一条还没关时不再叠加。
    /// </summary>
    /// <param name="oncePerRun">为 true 时本次运行弹过就不再弹，给悬停预览这类不是用户主动操作的场合用。</param>
    public static void Show(bool oncePerRun = false)
    {
        if (InAppToast.MainWindow is not { } toast)
        {
            return;
        }
        if (ReferenceEquals(_openOn, toast) || (oncePerRun && _shown))
        {
            return;
        }
        _openOn = toast;
        _shown = true;
        toast.ShowWithButton(InfoBarSeverity.Warning,
                             null,
                             Lang.AppBackground_HEVCVideoExtensionsRequired,
                             Lang.Common_Download,
                             async () => await Launcher.LaunchUriAsync(new Uri(HevcHelper.VideoExtensionStoreUrl)),
                             () => _openOn = null);
    }

}
