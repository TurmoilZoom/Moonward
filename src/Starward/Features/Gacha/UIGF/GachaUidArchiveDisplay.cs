using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using System;
using System.Collections.Generic;


namespace Starward.Features.Gacha.UIGF;

public class GachaUidArchiveDisplay : ObservableObject
{

    /// <summary>
    /// 归档所属业务线：原神 <c>hk4e</c>、星铁 <c>hkrpg</c>、绝区零 <c>nap</c>、
    /// 千星奇域内部键 <c>hk4eugc</c>（对应 UIGF 字段 <c>hk4e_ugc</c>）。
    /// </summary>
    public GameBiz Game { get; set; }

    public string GameIcon { get; set; }

    /// <summary>
    /// UIGF 根字段名（协议标识，不翻译），用于列表区分同 UID 的原神与千星奇域归档。
    /// </summary>
    public string UigfGameKey => Game.Value switch
    {
        "hk4eugc" => "hk4e_ugc",
        _ => Game.Game,
    };

    public long Uid { get; set; }

    public int Count { get; set; }

    public string LastItemGachaType { get; set; }

    public string LastItemName { get; set; }

    public DateTime LastItemTime { get; set; }


    public List<UIGFGenshinGachaItem>? hke4List { get; set; }

    public List<StarRailGachaItem>? hkrpgList { get; set; }

    public List<ZZZGachaItem>? napList { get; set; }

    /// <summary>千星奇域导入暂存列表（对应 UIGF hk4e_ugc）。</summary>
    public List<GenshinBeyondGachaItem>? hk4eUgcList { get; set; }


    public int Timezone
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                LastItemTimeOffest = LastItemTime.AddHours(value);
            }
        }
    }


    public DateTime LastItemTimeOffest { get; set => SetProperty(ref field, value); }


    public string? Result { get; set => SetProperty(ref field, value); }


    public string? Error { get; set => SetProperty(ref field, value); }


}
