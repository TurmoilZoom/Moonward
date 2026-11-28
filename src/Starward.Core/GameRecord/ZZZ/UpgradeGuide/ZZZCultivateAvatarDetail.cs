using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.UpgradeGuide;


/// <summary>
/// 养成指南 <c>batch_avatar_detail_v2</c> 请求体。
/// </summary>
public class ZZZCultivateAvatarDetailRequest
{

    [JsonPropertyName("avatar_list")]
    public List<ZZZCultivateAvatarDetailRequestItem> AvatarList { get; set; } = [];

}



public class ZZZCultivateAvatarDetailRequestItem
{

    [JsonPropertyName("avatar_id")]
    public int AvatarId { get; set; }

    /// <summary>未实装的预告代理人传 true，响应才带 <see cref="ZZZCultivateAvatarDetail.Teaser"/>。</summary>
    [JsonPropertyName("is_teaser")]
    public bool IsTeaser { get; set; }

    /// <summary>预告代理人的材料是否包含专属音擎。</summary>
    [JsonPropertyName("teaser_need_weapon")]
    public bool TeaserNeedWeapon { get; set; }

    /// <summary>预告代理人的技能是否升到最高级（12 级要「仓鼠笼」访问器）。</summary>
    [JsonPropertyName("teaser_sp_skill")]
    public bool TeaserSpSkill { get; set; }

}



/// <summary>
/// 养成指南 <c>batch_avatar_detail_v2</c> 响应。
/// </summary>
public class ZZZCultivateAvatarDetailList
{

    [JsonPropertyName("list")]
    public List<ZZZCultivateAvatarDetail> List { get; set; }

}



/// <summary>
/// 代理人养成详情。未拥有时 <see cref="Weapon"/> 与等级上限为 null，但仍有推荐方案；预告代理人只有基础信息与 <see cref="Teaser"/>。
/// </summary>
public class ZZZCultivateAvatarDetail
{

    [JsonPropertyName("avatar")]
    public ZZZCultivateAvatar Avatar { get; set; }

    /// <summary>已装备的音擎。</summary>
    [JsonPropertyName("weapon")]
    public ZZZCultivateWeapon? Weapon { get; set; }

    /// <summary>推荐的社区养成方案。</summary>
    [JsonPropertyName("plan")]
    public ZZZCultivatePlan? Plan { get; set; }

    /// <summary>方案作者。</summary>
    [JsonPropertyName("user")]
    public ZZZCultivatePlanAuthor? User { get; set; }

    /// <summary>当前突破阶段的等级上限。</summary>
    [JsonPropertyName("item_info")]
    public ZZZCultivateLevelLimit? ItemInfo { get; set; }

    /// <summary>下一突破阶段的等级上限。</summary>
    [JsonPropertyName("next_item_info")]
    public ZZZCultivateLevelLimit? NextItemInfo { get; set; }

    /// <summary>预告代理人的全套养成材料（按请求里的两个开关算好）；已实装代理人的 <c>is_teaser</c> 为 false。</summary>
    [JsonPropertyName("teaser")]
    public ZZZCultivateTeaser? Teaser { get; set; }

}



/// <summary>
/// 音擎词条。
/// </summary>
public class ZZZCultivateEquipProperty
{

    [JsonPropertyName("property_name")]
    public string PropertyName { get; set; }

    [JsonPropertyName("property_id")]
    public int PropertyId { get; set; }

    /// <summary>带单位的数值。</summary>
    [JsonPropertyName("base")]
    public string? Base { get; set; }

}



/// <summary>
/// 推荐方案里的驱动盘套装，<see cref="Count"/> 为推荐件数。
/// </summary>
public class ZZZCultivateEquipSuit
{

    [JsonPropertyName("suit_id")]
    public int SuitId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("cnt")]
    public int Count { get; set; }

    /// <summary>二件套效果。</summary>
    [JsonPropertyName("desc1")]
    public string? Desc1 { get; set; }

    /// <summary>四件套效果。</summary>
    [JsonPropertyName("desc2")]
    public string? Desc2 { get; set; }

    [JsonPropertyName("icon")]
    public string Icon { get; set; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }

}



/// <summary>
/// 音擎。
/// </summary>
public class ZZZCultivateWeapon
{

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>精炼等级。</summary>
    [JsonPropertyName("star")]
    public int Star { get; set; }

    [JsonPropertyName("icon")]
    public string Icon { get; set; }

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; }

    /// <summary>高级属性。</summary>
    [JsonPropertyName("properties")]
    public List<ZZZCultivateEquipProperty>? Properties { get; set; }

    /// <summary>基础攻击力。</summary>
    [JsonPropertyName("main_properties")]
    public List<ZZZCultivateEquipProperty>? MainProperties { get; set; }

    [JsonPropertyName("talent_title")]
    public string? TalentTitle { get; set; }

    [JsonPropertyName("talent_content")]
    public string? TalentContent { get; set; }

    [JsonPropertyName("profession")]
    public int Profession { get; set; }

}



/// <summary>
/// 等级上限（按突破阶段）。
/// </summary>
public class ZZZCultivateLevelLimit
{

    [JsonPropertyName("avatar_level_max")]
    public int AvatarLevelMax { get; set; }

    [JsonPropertyName("weapon_level_max")]
    public int WeaponLevelMax { get; set; }

    [JsonPropertyName("skill_core_level_max")]
    public int SkillCoreLevelMax { get; set; }

    [JsonPropertyName("skill_normal_level_max")]
    public List<ZZZCultivateSkillLevel>? SkillNormalLevelMax { get; set; }

}



public class ZZZCultivateSkillLevel
{

    [JsonPropertyName("skill_type")]
    public int SkillType { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

}
