namespace Starward.Core.MiyoLive;

/// <summary>
/// 前瞻直播兑换码活动配置：按游戏映射米游社官方账号 UID、社区 gids 与每期预计的兑换码数量。
/// 常量易变，变更时只改本文件。
/// </summary>
public static class MiyoLiveActivityConfig
{

    /// <summary>
    /// 按 <see cref="GameBiz"/> 解析官方动态 UID 与社区 gids；不支持时返回 null。
    /// bilibili 与对应国服共用同一套。
    /// </summary>
    /// <param name="gameBiz">当前游戏业务标识。</param>
    /// <returns>配置；不支持的游戏返回 null。</returns>
    public static MiyoLiveGameConfig? FromGameBiz(GameBiz gameBiz)
    {
        // bilibili 与国服同一套米游社直播活动
        string game = gameBiz.Game;
        string server = gameBiz.Server;
        if (server is not ("cn" or "bilibili"))
        {
            return null;
        }

        return game switch
        {
            // 原神官方号 / 原神 gids；7.1、7.2 每期 3 个码
            GameBiz.hk4e => new MiyoLiveGameConfig(Uid: 75276539, Gids: 2, ExpectedCodeCount: 3),
            // 星穹铁道；4.6 每期 3 个码
            GameBiz.hkrpg => new MiyoLiveGameConfig(Uid: 80823548, Gids: 6, ExpectedCodeCount: 3),
            // 崩坏3（仅 cn，无 bilibili 启动项时也无妨）；近期没有带码的直播，数量没实测过，先按 3 个
            GameBiz.bh3 => new MiyoLiveGameConfig(Uid: 73565430, Gids: 1, ExpectedCodeCount: 3),
            // 绝区零；3.3、3.4 每期只有 1 个码（奖励一次给齐）
            GameBiz.nap => new MiyoLiveGameConfig(Uid: 152039148, Gids: 8, ExpectedCodeCount: 1),
            _ => null,
        };
    }

}


/// <summary>
/// 单游戏直播活动发现参数。
/// </summary>
/// <param name="Uid">米游社官方账号 UID（用于 painter 动态列表）。</param>
/// <param name="Gids">米游社社区游戏 ID（用于首页导航备用路径）。</param>
/// <param name="ExpectedCodeCount">
/// 每期预计放出几个兑换码。码在直播中分几次放出，后台通知等放够这个数再合成一条发；
/// 数字不准时直播结束后照样会发，见 <c>RedeemCodeNotifySchedule.ShouldNotify</c>。
/// </param>
public readonly record struct MiyoLiveGameConfig(long Uid, int Gids, int ExpectedCodeCount);
