using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.Screenshot;
using Starward.Helpers;
using System;
using System.Threading.Tasks;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 战绩页分享图的 UI 侧流程：抓背景、读强调色、后台渲染、打开内置看图窗口。
/// </summary>
internal static class GameRecordShareHelper
{

    /// <summary>
    /// 获取当前游戏壁纸路径；视频背景则抓取当前帧快照。
    /// </summary>
    public static async Task<string?> PrepareBackgroundFileAsync(GameBiz gameBiz)
    {
        GameId? gameId = GameId.FromGameBiz(gameBiz);
        if (gameId is null)
        {
            return null;
        }

        string? backgroundFile = BackgroundService.GetCachedBackgroundFile(gameId);
        if (backgroundFile is not null && BackgroundService.FileIsSupportedVideo(backgroundFile))
        {
            backgroundFile = AppBackground.Current is not null
                ? await AppBackground.Current.CaptureCurrentBackgroundSnapshotAsync()
                : null;
        }

        return backgroundFile;
    }


    /// <summary>在 UI 线程读取主题强调色，供离屏渲染使用。</summary>
    public static Color GetAccentColor()
    {
        if (Application.Current.Resources["AccentFillColorDefaultBrush"] is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
    }


    /// <summary>
    /// 渲染分享图并打开内置图片查看器。强调色在 UI 线程读取，Win2D 离屏绘制放到后台线程。
    /// </summary>
    public static async Task ShareAsync(
        FrameworkElement host,
        GameRecordRole? role,
        ILogger logger,
        Action<bool> setBusy,
        Func<string?, Color, Task<string>> render)
    {
        if (role is null)
        {
            return;
        }

        try
        {
            setBusy(true);
            string? backgroundFile = await PrepareBackgroundFileAsync(role.GameBiz);
            Color accentColor = GetAccentColor();
            string file = await Task.Run(async () => await render(backgroundFile, accentColor));
            await new ImageViewWindow2().ShowWindowAsync(host.XamlRoot.ContentIslandEnvironment.AppWindowId, file, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Share game record image ({gameBiz}, {uid}).", role.GameBiz, role.Uid);
            InAppToast.MainWindow?.Error(ex);
        }
        finally
        {
            setBusy(false);
        }
    }

}
