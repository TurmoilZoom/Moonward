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
    /// 是否为千星奇域归档。其图标是透明底的道具图，列表据此在图标下垫深色圆角底块，与其他游戏的方形图标对齐。
    /// </summary>
    public bool IsGenshinBeyond => Game.Value == "hk4eugc";

    /// <summary>
    /// 图标悬停说明：游戏名；千星奇域显示「千星奇域颂愿」（hk4eugc 不是真实 GameBiz，<see cref="GameBiz.ToGameName"/> 取不到名称）。
    /// </summary>
    public string GameName => IsGenshinBeyond ? Lang.GenshinBeyondGachaPage_MiliastraWonderlandOde : Game.ToGameName();

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


    public string? Result
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }


    public string? Error
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }


    /// <summary>导入列表据此显示成功图标与文字。</summary>
    public bool HasResult => !string.IsNullOrEmpty(Result);


    /// <summary>导入列表据此显示失败图标与文字。</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);


}
