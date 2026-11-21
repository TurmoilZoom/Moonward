using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Starward.AnimatedVisuals;
using Starward.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 首页右侧功能栏各功能项的元信息：哪些区服支持、显示名称与图标。供自定义功能栏对话框使用。
/// </summary>
internal static class RightToolbarItemCatalog
{

    /// <summary>
    /// 某个游戏的全部区服。
    /// </summary>
    /// <param name="game">游戏（不分区服），如 <c>hk4e</c>。</param>
    /// <returns>该游戏的区服列表。</returns>
    public static IEnumerable<GameBiz> GetServers(string game)
    {
        return GameBiz.AllGameBizs.Where(x => x.Game == game);
    }


    /// <summary>
    /// 某个区服是否有这个功能。与各按钮自己的显示条件一致，但不含登录状态。
    /// </summary>
    /// <param name="biz">区服。</param>
    /// <param name="item">功能项。</param>
    /// <returns>支持时为 <see langword="true"/>。</returns>
    public static bool IsSupported(GameBiz biz, RightToolbarItem item)
    {
        GameFeatureConfig feature = GameFeatureConfig.FromGameBiz(biz);
        return item switch
        {
            RightToolbarItem.CloudGame => feature.SupportCloudGame,
            RightToolbarItem.DailyNote => feature.SupportDailyNote,
            RightToolbarItem.TimeNode => feature.SupportTimeNode,
            RightToolbarItem.MapTool => feature.SupportMapTool,
            RightToolbarItem.RedeemCode => feature.SupportRedeemCode,
            RightToolbarItem.SignIn => feature.SupportSignIn,
            _ => false,
        };
    }


    /// <summary>
    /// 该游戏至少有一个区服支持这个功能。
    /// </summary>
    /// <param name="game">游戏（不分区服）。</param>
    /// <param name="item">功能项。</param>
    /// <returns>有区服支持时为 <see langword="true"/>。</returns>
    public static bool IsSupportedByGame(string game, RightToolbarItem item)
    {
        return GetServers(game).Any(x => IsSupported(x, item));
    }


    /// <summary>
    /// 只有部分区服支持时的说明，如「仅国服 / B服」；全部区服都支持时为 null。
    /// </summary>
    /// <param name="game">游戏（不分区服）。</param>
    /// <param name="item">功能项。</param>
    /// <returns>说明文字或 null。</returns>
    public static string? GetServerLimitText(string game, RightToolbarItem item)
    {
        List<GameBiz> servers = GetServers(game).ToList();
        List<GameBiz> supported = servers.Where(x => IsSupported(x, item)).ToList();
        if (supported.Count == 0 || supported.Count == servers.Count)
        {
            return null;
        }
        return string.Format(Lang.RightToolbarCustomize_ServerLimited, string.Join(" / ", supported.Select(x => x.ToGameServerName())));
    }


    /// <summary>
    /// 功能项名称，与首页按钮的悬停提示一致。
    /// </summary>
    /// <param name="item">功能项。</param>
    /// <returns>本地化名称。</returns>
    public static string GetTitle(RightToolbarItem item)
    {
        return item switch
        {
            RightToolbarItem.CloudGame => Lang.CloudGameButton_CloudGame,
            RightToolbarItem.DailyNote => Lang.DailyNoteButton_Title,
            RightToolbarItem.TimeNode => Lang.TimeNodeButton_Title,
            RightToolbarItem.MapTool => Lang.MapToolButton_Title,
            RightToolbarItem.RedeemCode => Lang.RedeemCodeButton_Title,
            RightToolbarItem.SignIn => Lang.SignInButton_Title,
            _ => item.ToString(),
        };
    }


    /// <summary>
    /// 创建与首页按钮相同的图标，放进 32×32 的格子；各图标之间的大小比例与首页 40 的按钮里一致。
    /// </summary>
    /// <param name="item">功能项。</param>
    /// <param name="player">Lottie 图标的播放器（悬停时播放用）；图片图标为 null。</param>
    /// <returns>图标元素。</returns>
    public static FrameworkElement CreateIcon(RightToolbarItem item, out AnimatedVisualPlayer? player)
    {
        player = null;
        if (item is RightToolbarItem.CloudGame)
        {
            return new Image
            {
                Width = 22,
                Height = 22,
                Source = new BitmapImage
                {
                    DecodePixelHeight = 22,
                    DecodePixelType = DecodePixelType.Logical,
                    UriSource = new Uri("ms-appx:///Assets/Image/icon_cloudgame.png"),
                },
            };
        }
        IAnimatedVisualSource source = item switch
        {
            RightToolbarItem.DailyNote => new Doodle_color_1821_moon_stars_hover_pinch(),
            RightToolbarItem.TimeNode => new Wired_lineal_2947_calendar_24_dec_hover_flutter(),
            RightToolbarItem.MapTool => new Doodle_color_27_globe_hover_rotate(),
            RightToolbarItem.RedeemCode => new Wired_lineal_412_gift_morph_open(),
            RightToolbarItem.SignIn => new Wired_lineal_37_check_hover_wobble(),
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        player = new AnimatedVisualPlayer
        {
            Width = 26,
            Height = 26,
            AutoPlay = false,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Source = source,
        };
        return player;
    }


}
