namespace Starward.Features.GameLauncher;

/// <summary>
/// 首页右侧功能栏里的功能项。
/// </summary>
/// <remarks>
/// 枚举名会写进设置（<see cref="RightToolbarLayout.Serialize"/>），不能改名；删掉某项后，旧设置里的名字解析时会被跳过。
/// </remarks>
public enum RightToolbarItem
{
    /// <summary>云游戏。</summary>
    CloudGame,

    /// <summary>实时便笺。</summary>
    DailyNote,

    /// <summary>时间节点（卡池 / 活动倒计时）。</summary>
    TimeNode,

    /// <summary>地图工具。</summary>
    MapTool,

    /// <summary>前瞻直播兑换码。</summary>
    RedeemCode,

    /// <summary>每日签到。</summary>
    SignIn,
}
