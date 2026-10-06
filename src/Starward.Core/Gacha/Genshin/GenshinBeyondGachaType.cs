using System.ComponentModel;

namespace Starward.Core.Gacha.Genshin;

/// <summary>
/// 千星奇域颂愿的卡池分组。接口按 op_gacha_type 区分常驻（1000）与各期活动颂愿，统计时活动颂愿合并为 2000（与 UIGF 导出一致）。
/// </summary>
public readonly record struct GenshinBeyondGachaType(int Value) : IGachaType
{


    /// <summary>
    /// 常驻颂愿
    /// </summary>
    [Description("常驻颂愿")]
    public const int StandardOde = 1000;

    /// <summary>
    /// 活动颂愿（合并全部活动 op_gacha_type）
    /// </summary>
    [Description("活动颂愿")]
    public const int EventOde = 2000;



    public string ToLocalization() => Value switch
    {
        StandardOde => CoreLang.GachaType_StandardOde,
        EventOde => CoreLang.GachaType_EventOde,
        _ => "",
    };



    public override string ToString() => Value.ToString();
    public static implicit operator GenshinBeyondGachaType(int value) => new(value);
    public static implicit operator int(GenshinBeyondGachaType value) => value.Value;


}
