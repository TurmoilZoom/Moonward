using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.Gacha;
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord;
using Starward.Features.GameSetting;
using Starward.Features.Screenshot;
using Starward.Features.SelfQuery;
using System.Collections.Generic;

namespace Starward.Features;

internal partial class GameFeatureConfig
{


    private GameFeatureConfig()
    {

    }


    /// <summary>
    /// 支持的页面
    /// </summary>
    public List<string> SupportedPages { get; init; } = [];


    /// <summary>
    /// 游戏内通知内容
    /// </summary>
    public bool InGameNoticesWindow { get; init; }


    /// <summary>
    /// 支持硬链接
    /// </summary>
    public bool SupportHardLink { get; init; }


    /// <summary>
    /// 支持云游戏
    /// </summary>
    public bool SupportCloudGame { get; init; }


    /// <summary>
    /// 支持实时便笺
    /// </summary>
    public bool SupportDailyNote { get; init; }


    /// <summary>
    /// 支持每日签到
    /// </summary>
    public bool SupportSignIn { get; init; }


    /// <summary>
    /// 支持首页时间节点（百科卡池 / 活动倒计时）
    /// </summary>
    public bool SupportTimeNode { get; init; }


    /// <summary>
    /// 支持首页兑换码（国服前瞻直播码展示）
    /// </summary>
    public bool SupportRedeemCode { get; init; }


    /// <summary>
    /// 支持百科好感壁纸（绝区零自定义背景）
    /// </summary>
    public bool SupportFavorWallpaper { get; init; }



    public static GameFeatureConfig FromGameId(GameId? gameId)
    {
        if (gameId is null)
        {
            return None;
        }
        return FromGameBiz(gameId.GameBiz);
    }


    public static GameFeatureConfig FromGameBiz(GameBiz gameBiz)
    {
        GameFeatureConfig config = gameBiz.Value switch
        {
            GameBiz.bh3_cn => bh3_cn,
            GameBiz.bh3_global => bh3_global,
            GameBiz.hk4e_cn => hk4e_cn,
            GameBiz.hk4e_global => hk4e_global,
            GameBiz.hk4e_bilibili => hk4e_bilibili,
            GameBiz.hkrpg_cn => hkrpg_cn,
            GameBiz.hkrpg_global => hkrpg_global,
            GameBiz.hkrpg_bilibili => hkrpg_bilibili,
            GameBiz.nap_cn => nap_cn,
            GameBiz.nap_global => nap_global,
            GameBiz.nap_bilibili => nap_bilibili,
            _ => Default,
        };
        return config;
    }





    private static readonly GameFeatureConfig None = new();


    private static readonly GameFeatureConfig Default = new()
    {
        SupportedPages = [nameof(GameLauncherPage)]
    };


    private static readonly GameFeatureConfig bh3_cn = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GameRecordPage),
        ],
        InGameNoticesWindow = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportRedeemCode = true,
    };


    private static readonly GameFeatureConfig bh3_global = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GameRecordPage),
        ],
        InGameNoticesWindow = true,
        SupportDailyNote = true,
        SupportSignIn = true,
    };


    private static readonly GameFeatureConfig hk4e_cn = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
            nameof(GenshinBeyondGachaPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportCloudGame = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
        SupportRedeemCode = true,
    };


    private static readonly GameFeatureConfig hk4e_global = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
            nameof(GenshinBeyondGachaPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportCloudGame = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
    };


    private static readonly GameFeatureConfig hk4e_bilibili = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
            nameof(GenshinBeyondGachaPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
        SupportRedeemCode = true,
    };


    private static readonly GameFeatureConfig hkrpg_cn = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
        SupportRedeemCode = true,
    };


    private static readonly GameFeatureConfig hkrpg_global = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
    };


    private static readonly GameFeatureConfig hkrpg_bilibili = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
        SupportRedeemCode = true,
    };



    private static readonly GameFeatureConfig nap_cn = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportCloudGame = true,
        SupportTimeNode = true,
        SupportRedeemCode = true,
        SupportFavorWallpaper = true,
    };


    private static readonly GameFeatureConfig nap_global = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
        SupportFavorWallpaper = true,
    };


    private static readonly GameFeatureConfig nap_bilibili = new()
    {
        SupportedPages =
        [
            nameof(GameLauncherPage),
            nameof(GameSettingPage),
            nameof(ScreenshotPage),
            nameof(GachaLogPage),
            nameof(GameRecordPage),
            nameof(SelfQueryPage),
        ],
        InGameNoticesWindow = true,
        SupportHardLink = true,
        SupportDailyNote = true,
        SupportSignIn = true,
        SupportTimeNode = true,
        SupportRedeemCode = true,
        SupportFavorWallpaper = true,
    };



}
