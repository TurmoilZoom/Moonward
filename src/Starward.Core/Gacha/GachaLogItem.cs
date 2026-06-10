using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.Gacha;

/// <summary>
/// 抽卡记录单条数据模型。
/// 对应 miHoYo/Hoyoverse 各游戏（原神、星穹铁道、绝区零）祈愿记录/跃迁记录/频段记录接口返回的 list 中的单项。
/// </summary>
/// <remarks>
/// 这是基础抽象模型，具体游戏使用各自的派生类：
/// <see cref="Genshin.GenshinGachaItem"/>、<see cref="StarRail.StarRailGachaItem"/>、<see cref="ZZZ.ZZZGachaItem"/>。
/// 派生类主要负责通过 <see cref="GetGachaType"/> 返回游戏特有的 <see cref="IGachaType"/> 强类型。
/// 实际 UI 绑定与统计通常使用 Starward 项目的 <c>GachaLogItemEx</c>（继承自本类并增加 Pity、Index 等计算属性）。
/// </remarks>
public class GachaLogItem
{

    /// <summary>
    /// 玩家 UID。
    /// </summary>
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Uid { get; set; }


    /// <summary>
    /// 抽卡记录的唯一 ID（对应接口返回的 <c>id</c> 字段）。
    /// </summary>
    /// <remarks>
    /// 用于分页拉取时的 <c>end_id</c> 参数（取本次获取的最小 id 作为下次 endId）。
    /// 也是数据库中各游戏 GachaItem 表的主键（通常与 Uid 联合作为唯一约束）。
    /// </remarks>
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Id { get; set; }


    /// <summary>
    /// 卡池/频段类型（原始整数值，对应接口 <c>gacha_type</c> 或 <c>real_gacha_type</c>）。
    /// </summary>
    /// <remarks>
    /// 不同游戏含义不同，应通过 <see cref="GetGachaType"/> 转换为具体的 <see cref="IGachaType"/> 进行判断与本地化显示。
    /// 例如原神 301/302/500 等，星穹铁道 1/2/11/12 等。
    /// </remarks>
    [JsonPropertyName("gacha_type")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public int GachaType { get; set; }


    /// <summary>
    /// 物品名称（角色名或武器/光锥/音擎名称）。
    /// </summary>
    /// <remarks>
    /// 直接来自接口返回的本地化名称，受 <c>lang</c> 参数影响。
    /// 更稳定的关联方式是使用 <see cref="ItemId"/> 配合 *GachaInfo 表。
    /// </remarks>
    [JsonPropertyName("name")]
    public string Name { get; set; }


    /// <summary>
    /// 物品类型描述（例如“角色”、“武器”、“光锥”、“音擎”、“邦布”等）。
    /// </summary>
    [JsonPropertyName("item_type")]
    public string ItemType { get; set; }


    /// <summary>
    /// 稀有度（星级），通常为 3、4、5。
    /// </summary>
    /// <remarks>对应接口 <c>rank_type</c> 字段。</remarks>
    [JsonPropertyName("rank_type")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public int RankType { get; set; }


    /// <summary>
    /// 抽取时间（本地时间）。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="DateTimeStringJsonConverter"/> 进行序列化/反序列化，
    /// 接口返回格式通常为 <c>yyyy-MM-dd HH:mm:ss</c>。
    /// </remarks>
    [JsonPropertyName("time")]
    [JsonConverter(typeof(DateTimeStringJsonConverter))]
    public DateTime Time { get; set; }


    /// <summary>
    /// 物品内部 ID（对应 <c>item_id</c>）。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="GachaItemIdJsonConverter"/> 处理（支持 number 或 string）。
    /// 是将记录与 <c>*GachaInfo</c>（如 GenshinGachaInfo、StarRailGachaInfo、ZZZGachaInfo）关联的主键，
    /// 也用于精确匹配图标、名称等元数据。
    /// </remarks>
    [JsonPropertyName("item_id")]
    [JsonConverter(typeof(GachaItemIdJsonConverter))]
    public int ItemId { get; set; }


    /// <summary>
    /// 数量（通常为 1，部分老记录或特殊情况可能大于 1）。
    /// </summary>
    [JsonPropertyName("count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public int Count { get; set; }


    /// <summary>
    /// 记录的语言代码（例如 <c>zh-cn</c>、<c>en-us</c>）。
    /// </summary>
    /// <remarks>影响 <see cref="Name"/> 和 <see cref="ItemType"/> 的文本内容。</remarks>
    [JsonPropertyName("lang")]
    public string Lang { get; set; }



    /// <summary>
    /// 获取当前记录所属的游戏特有卡池类型。
    /// </summary>
    /// <returns>
    /// 派生类应返回对应的强类型（如 <see cref="Genshin.GenshinGachaType"/>），基类默认返回 <see cref="UndefinedGachaType"/>。
    /// </returns>
    public virtual IGachaType GetGachaType() => new UndefinedGachaType(GachaType);


    /// <summary>
    /// 创建当前对象的浅拷贝。
    /// </summary>
    /// <returns>与当前对象字段值相同的新实例。</returns>
    public virtual GachaLogItem Clone() => (GachaLogItem)MemberwiseClone();


}
