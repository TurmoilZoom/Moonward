using Starward.Core.HoYoPlay;
using System.Collections.Generic;


namespace Starward.Features.GameSelector;

public class GameBizDisplay
{

    public GameInfo GameInfo { get; set; }


    public List<GameBizIcon> Servers { get; set; } = new();


    /// <summary>
    /// 游戏尚未上线、处于预约状态：卡片显示预约标识，不提供区服选择，点击打开预约详情页
    /// </summary>
    public bool IsReservation => GameInfo?.DisplayStatus is GameInfoDisplayStatus.LAUNCHER_GAME_DISPLAY_STATUS_RESERVATION_ENABLED;

}
