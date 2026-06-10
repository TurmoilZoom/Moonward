using System.Text.Json.Serialization;

namespace Starward.Core.Gacha.Genshin;

/// <summary>
/// 原神角色（Avatar）与武器（Weapon）元数据信息。
/// 数据来源于 miHoYo 祈愿模拟器配置接口（/event/platsimulator/config 或 /event/simulatoros/config）。
/// 主要用途：
/// <list type="number">
/// <item>将祈愿记录中的 Name 精确映射为稳定的 <see cref="Id"/>（作为 GachaItem.ItemId）。</item>
/// <item>提供物品图标（<see cref="Icon"/>）。</item>
/// <item>存储元素、武器分类等属性，用于 UI 展示与筛选。</item>
/// </list>
/// 同时持久化到本地数据库 <c>GenshinGachaInfo</c> 表中，支持多语言名称缓存（GachaItemName）。
/// </summary>
public class GenshinGachaInfo : IJsonOnSerializing, IJsonOnDeserialized
{

    /// <summary>
    /// 物品唯一 ID（角色或武器的内部标识）。
    /// 对应祈愿记录中的 <c>item_id</c>，也是 <c>GenshinGachaInfo</c> 表主键和 <c>GachaLogItem.ItemId</c>。
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>物品名称（根据请求的语言返回，支持多语言）。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>
    /// 物品图标资源标识（通常为相对路径或文件名）。
    /// 经过 <see cref="OnDeserialized"/> 处理后，该字段始终保存最终可用于显示的图标。
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; }

    /// <summary>
    /// API 返回的角色头像/半身像图标（head_icon）。
    /// 仅角色记录会填充此字段。反序列化时会与 <see cref="Icon"/> 交换位置，
    /// 使得 <see cref="Icon"/> 成为首选的展示图标。
    /// </summary>
    [JsonPropertyName("head_icon")]
    public string HeadIcon { get; set; }

    /// <summary>
    /// 元素属性类型（仅角色有效）。
    /// 对应原神风、岩、雷、草、水、火、冰等元素，具体数值由服务端定义。
    /// </summary>
    [JsonPropertyName("element")]
    public int Element { get; set; }

    /// <summary>
    /// 等级/星级相关字段（Level）。
    /// 通常用于标识基础稀有度或成长阶段（4 星 / 5 星等）。
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    /// <summary>
    /// 武器分类 ID（CatId，仅武器记录使用）。
    /// 表示武器种类，例如单手剑、双手剑、长柄武器、弓、法器。
    /// </summary>
    [JsonPropertyName("cat_id")]
    public int CatId { get; set; }

    /// <summary>
    /// 角色适配的武器分类 ID（WeaponCatId，仅角色记录使用）。
    /// 表示该角色可以装备的武器类型，与武器记录中的 <see cref="CatId"/> 对应。
    /// </summary>
    [JsonPropertyName("weapon_cat_id")]
    public int WeaponCatId { get; set; }


    /// <summary>
    /// JSON 反序列化完成后执行。
    /// 如果存在 <see cref="HeadIcon"/>，则与 <see cref="Icon"/> 交换，
    /// 确保 <see cref="Icon"/> 字段存放的是实际用于 UI 展示的图标资源。
    /// </summary>
    public void OnDeserialized()
    {
        if (!string.IsNullOrWhiteSpace(HeadIcon))
        {
            (Icon, HeadIcon) = (HeadIcon, Icon);
        }
    }


    /// <summary>
    /// JSON 序列化之前执行。
    /// 同样进行 Icon / HeadIcon 交换，保证序列化后的 JSON 与原始 API 响应结构保持一致。
    /// </summary>
    public void OnSerializing()
    {
        if (!string.IsNullOrWhiteSpace(HeadIcon))
        {
            (Icon, HeadIcon) = (HeadIcon, Icon);
        }
    }

}
