using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.UpgradeGuide;


/// <summary>
/// 社区养成方案（玩家投稿，详情接口按热度推荐一份）。
/// </summary>
public class ZZZCultivatePlan
{

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonPropertyName("item")]
    public ZZZCultivatePlanItem? Item { get; set; }

}



public class ZZZCultivatePlanItem
{

    /// <summary>推荐面板区间。</summary>
    [JsonPropertyName("avatar")]
    public List<ZZZCultivatePlanProperty>? Avatar { get; set; }

    [JsonPropertyName("weapon")]
    public ZZZCultivatePlanWeapon? Weapon { get; set; }

    [JsonPropertyName("equip")]
    public ZZZCultivatePlanEquip? Equip { get; set; }

    [JsonPropertyName("team")]
    public ZZZCultivatePlanTeams? Team { get; set; }

}



/// <summary>
/// 推荐面板区间，<see cref="PropertyId"/> 与 <see cref="ZZZCultivateAvatarProperty.PropertyId"/> 对应。
/// </summary>
public class ZZZCultivatePlanProperty
{

    [JsonPropertyName("property_name")]
    public string PropertyName { get; set; }

    [JsonPropertyName("property_id")]
    public int PropertyId { get; set; }

    [JsonPropertyName("low")]
    public string? Low { get; set; }

    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

    [JsonPropertyName("high")]
    public string? High { get; set; }

}



public class ZZZCultivatePlanWeapon
{

    [JsonPropertyName("main")]
    public ZZZCultivateWeapon? Main { get; set; }

    [JsonPropertyName("backup")]
    public ZZZCultivateWeapon? Backup { get; set; }

}



public class ZZZCultivatePlanEquip
{

    /// <summary>推荐套装，<see cref="ZZZCultivateEquipSuit.Count"/> 为件数。</summary>
    [JsonPropertyName("equip")]
    public List<ZZZCultivateEquipSuit>? Equip { get; set; }

    [JsonPropertyName("main_properties_4")]
    public List<ZZZCultivatePlanEquipProperty>? MainProperties4 { get; set; }

    [JsonPropertyName("main_properties_5")]
    public List<ZZZCultivatePlanEquipProperty>? MainProperties5 { get; set; }

    [JsonPropertyName("main_properties_6")]
    public List<ZZZCultivatePlanEquipProperty>? MainProperties6 { get; set; }

    [JsonPropertyName("sub_properties")]
    public List<ZZZCultivatePlanEquipProperty>? SubProperties { get; set; }

}



public class ZZZCultivatePlanEquipProperty
{

    [JsonPropertyName("property_name")]
    public string PropertyName { get; set; }

    [JsonPropertyName("property_id")]
    public int PropertyId { get; set; }

}



public class ZZZCultivatePlanTeams
{

    [JsonPropertyName("main")]
    public ZZZCultivatePlanTeam? Main { get; set; }

    [JsonPropertyName("backup")]
    public ZZZCultivatePlanTeam? Backup { get; set; }

}



public class ZZZCultivatePlanTeam
{

    [JsonPropertyName("avatar_list")]
    public List<ZZZCultivateAvatar>? AvatarList { get; set; }

    [JsonPropertyName("buddy_list")]
    public List<UpgradeGuideBuddy>? BuddyList { get; set; }

}



/// <summary>
/// 方案作者。
/// </summary>
public class ZZZCultivatePlanAuthor
{

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

}
