using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.UpgradeGuide;


/// <summary>
/// 养成指南 <c>avatar_calc</c> 请求体：当前等级到目标等级需要的材料。
/// </summary>
public class ZZZCultivateCalcRequest
{

    [JsonPropertyName("avatar_id")]
    public int AvatarId { get; set; }

    /// <summary>目标等级。</summary>
    [JsonPropertyName("avatar_level")]
    public int AvatarLevel { get; set; }

    [JsonPropertyName("avatar_current_level")]
    public int AvatarCurrentLevel { get; set; }

    /// <summary>当前突破阶段；未拥有时网页传 1。</summary>
    [JsonPropertyName("avatar_current_promotes")]
    public int AvatarCurrentPromotes { get; set; }

    [JsonPropertyName("skills")]
    public List<ZZZCultivateCalcSkill> Skills { get; set; } = [];

    /// <summary>不算音擎时为 null（不序列化）。</summary>
    [JsonPropertyName("weapon_info")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZZZCultivateCalcWeapon? WeaponInfo { get; set; }

}



public class ZZZCultivateCalcSkill
{

    [JsonPropertyName("skill_type")]
    public int SkillType { get; set; }

    /// <summary>目标等级。</summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    /// <summary>当前等级。</summary>
    [JsonPropertyName("init_level")]
    public int InitLevel { get; set; }

}



public class ZZZCultivateCalcWeapon
{

    [JsonPropertyName("weapon_id")]
    public int WeaponId { get; set; }

    /// <summary>目标等级。</summary>
    [JsonPropertyName("weapon_level")]
    public int WeaponLevel { get; set; }

    /// <summary>当前突破阶段，取自 <c>item_list</c> 的 <see cref="UpgradeGuideUserWeapon.Promotes"/>；未拥有时为 0。</summary>
    [JsonPropertyName("weapon_promotes")]
    public int WeaponPromotes { get; set; }

    /// <summary>当前等级；未拥有时为 0。</summary>
    [JsonPropertyName("weapon_init_level")]
    public int WeaponInitLevel { get; set; }

}



/// <summary>
/// 养成指南 <c>avatar_calc</c> 响应。
/// </summary>
public class ZZZCultivateCalcResult
{

    [JsonPropertyName("avatar_consume")]
    public List<ZZZCultivateMaterial>? AvatarConsume { get; set; }

    [JsonPropertyName("weapon_consume")]
    public List<ZZZCultivateMaterial>? WeaponConsume { get; set; }

    [JsonPropertyName("skill_consume")]
    public List<ZZZCultivateMaterial>? SkillConsume { get; set; }

    /// <summary>还缺的材料；<see cref="ZZZCultivateMaterial.Count"/> 为 0 表示已够。</summary>
    [JsonPropertyName("need_get")]
    public List<ZZZCultivateMaterial>? NeedGet { get; set; }

    /// <summary>库存：物品 Id → 数量（只含本次计算涉及的材料）。</summary>
    [JsonPropertyName("user_owns_materials")]
    public Dictionary<string, long>? UserOwnsMaterials { get; set; }

    /// <summary>丁尼的物品 Id。</summary>
    [JsonPropertyName("coin_id")]
    public int CoinId { get; set; }

}



/// <summary>
/// 预告代理人的全套养成材料：1→60 级、技能升满，可选含专属音擎。字段与 <see cref="ZZZCultivateCalcResult"/> 相同。
/// </summary>
public class ZZZCultivateTeaser : ZZZCultivateCalcResult
{

    [JsonPropertyName("is_teaser")]
    public bool IsTeaser { get; set; }

    /// <summary>服务端是否算出了材料。</summary>
    [JsonPropertyName("calc_success")]
    public bool CalcSuccess { get; set; }

}



/// <summary>
/// 养成材料。
/// </summary>
public class ZZZCultivateMaterial
{

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("cnt")]
    public long Count { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("icon")]
    public string Icon { get; set; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }

    /// <summary>材料尚未开放获取。</summary>
    [JsonPropertyName("not_opened")]
    public bool NotOpened { get; set; }

}
