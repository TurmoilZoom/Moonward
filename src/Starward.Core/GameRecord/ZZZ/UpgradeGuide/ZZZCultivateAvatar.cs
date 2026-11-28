using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.UpgradeGuide;


/// <summary>
/// 养成指南 <c>avatar_basic_list</c>：全部代理人（含未拥有，未拥有时等级为 0）。
/// </summary>
public class ZZZCultivateAvatarBasicList
{

    [JsonPropertyName("list")]
    public List<ZZZCultivateAvatarBasic> List { get; set; }

}



/// <summary>
/// 代理人列表项。
/// </summary>
public class ZZZCultivateAvatarBasic
{

    [JsonPropertyName("avatar")]
    public ZZZCultivateAvatar Avatar { get; set; }

    /// <summary>是否已拥有。</summary>
    [JsonPropertyName("unlocked")]
    public bool Unlocked { get; set; }

    /// <summary>是否为当期 UP 代理人。</summary>
    [JsonPropertyName("is_up")]
    public bool IsUp { get; set; }

    /// <summary>是否为未实装的预告代理人。</summary>
    [JsonPropertyName("is_teaser")]
    public bool IsTeaser { get; set; }

    [JsonPropertyName("is_top")]
    public bool IsTop { get; set; }

}



/// <summary>
/// 代理人。列表接口只有基础字段；<c>batch_avatar_detail_v2</c> 额外返回技能、突破阶段等。
/// </summary>
public class ZZZCultivateAvatar
{

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("name_mi18n")]
    public string Name { get; set; }

    [JsonPropertyName("full_name_mi18n")]
    public string FullName { get; set; }

    [JsonPropertyName("element_type")]
    public int ElementType { get; set; }

    [JsonPropertyName("sub_element_type")]
    public int SubElementType { get; set; }

    [JsonPropertyName("camp_name_mi18n")]
    public string CampName { get; set; }

    [JsonPropertyName("avatar_profession")]
    public int Profession { get; set; }

    /// <summary>稀有度 S / A。</summary>
    [JsonPropertyName("rarity")]
    public string Rarity { get; set; }

    /// <summary>方形头像。</summary>
    [JsonPropertyName("group_icon_path")]
    public string GroupIconPath { get; set; }

    [JsonPropertyName("hollow_icon_path")]
    public string HollowIconPath { get; set; }

    /// <summary>影画数 0~6。</summary>
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("awaken_state")]
    public string? AwakenState { get; set; }

    /// <summary>技能（仅详情）。</summary>
    [JsonPropertyName("skills")]
    public List<ZZZCultivateSkill>? Skills { get; set; }

    /// <summary>专属音擎 Id（仅详情）。</summary>
    [JsonPropertyName("signature_weapon_id")]
    public int SignatureWeaponId { get; set; }

    /// <summary>突破阶段（仅详情），养成计算的 <c>avatar_current_promotes</c>。</summary>
    [JsonPropertyName("promotes")]
    public int Promotes { get; set; }

    /// <summary>是否已拥有（仅详情）。</summary>
    [JsonPropertyName("unlock")]
    public bool Unlock { get; set; }

    /// <summary>技能升级优先级（仅详情）：first / second / third 各为若干 skill_type。</summary>
    [JsonPropertyName("skill_upgrade")]
    public ZZZCultivateSkillUpgrade? SkillUpgrade { get; set; }

}



/// <summary>
/// 技能。skill_type：0 普通攻击、1 特殊技、2 闪避、3 连携技、5 核心技、6 支援技。
/// </summary>
public class ZZZCultivateSkill
{

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("skill_type")]
    public int SkillType { get; set; }

    [JsonPropertyName("items")]
    public List<ZZZCultivateSkillItem>? Items { get; set; }

}



public class ZZZCultivateSkillItem
{

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

}



/// <summary>
/// 技能升级优先级，每档为若干 skill_type。
/// </summary>
public class ZZZCultivateSkillUpgrade
{

    [JsonPropertyName("first")]
    public List<int>? First { get; set; }

    [JsonPropertyName("second")]
    public List<int>? Second { get; set; }

    [JsonPropertyName("third")]
    public List<int>? Third { get; set; }

}
