using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Starward.Core.GameRecord.ZZZ.UpgradeGuide;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Starward.Features.GameRecord.ZZZ;


/// <summary>
/// 养成指南左侧代理人列表项。
/// </summary>
public sealed class ZZZCultivateAgentItem
{

    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public string Rarity { get; set; } = "";

    public bool Unlocked { get; set; }

    /// <summary>未实装的预告代理人，固定排在最上面。</summary>
    public bool IsTeaser { get; set; }

    public int Level { get; set; }

    /// <summary>未拥有的已实装代理人调淡；预告代理人不调淡。</summary>
    public double Opacity => Unlocked || IsTeaser ? 1 : 0.45;

    public string LevelText { get; set; } = "";

    public Visibility LevelVisibility => IsTeaser ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TeaserVisibility => IsTeaser ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>搜索用：名称、全名、英文名、阵营。</summary>
    public string SearchKey { get; set; } = "";

    /// <summary>属性 element_type（200 物理、201 火、202 冰、203 电、204 风、205 以太、300 流明）。</summary>
    public int ElementType { get; set; }

    /// <summary>特性 avatar_profession（1 强攻 … 7 锋御）。</summary>
    public int Profession { get; set; }


    /// <summary>
    /// 是否匹配搜索词（忽略大小写，包含即可）。
    /// </summary>
    /// <param name="keyword">已去掉首尾空白的搜索词。</param>
    /// <returns>是否匹配。</returns>
    public bool Matches(string keyword) => SearchKey.Contains(keyword, StringComparison.OrdinalIgnoreCase);


    /// <summary>
    /// 该代理人在某个筛选组下的取值，与 <see cref="ZZZCultivateFilterOption.Key"/> 对应。
    /// </summary>
    /// <param name="group">筛选组键。</param>
    /// <returns>取值；未知组返回空串。</returns>
    public string GetFilterValue(string group) => group switch
    {
        ZZZCultivateFilterGroup.Ownership => IsTeaser ? ZZZCultivateFilterGroup.OwnershipTeaser : Unlocked ? ZZZCultivateFilterGroup.OwnershipOwned : ZZZCultivateFilterGroup.OwnershipNotOwned,
        ZZZCultivateFilterGroup.Rarity => Rarity,
        ZZZCultivateFilterGroup.Attribute => ElementType.ToString(CultureInfo.InvariantCulture),
        ZZZCultivateFilterGroup.Specialty => Profession.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };


    /// <summary>
    /// 由列表接口数据构造。
    /// </summary>
    /// <param name="basic">列表项。</param>
    /// <param name="icons">代理人头像表（<c>icon_info</c>），可空。</param>
    /// <returns>列表项视图模型。</returns>
    public static ZZZCultivateAgentItem From(ZZZCultivateAvatarBasic basic, UpgradeGuidIconInfo? icons)
    {
        var avatar = basic.Avatar;
        string englishName = icons?.AvatarIcon?.TryGetValue(avatar.Id.ToString(CultureInfo.InvariantCulture), out var icon) == true ? icon.AvatarUsFullName ?? "" : "";
        return new ZZZCultivateAgentItem
        {
            Id = avatar.Id,
            Name = avatar.Name,
            Icon = ZZZCultivateViewBuilder.GetAvatarIcon(avatar.Id, icons),
            Rarity = avatar.Rarity,
            Unlocked = basic.Unlocked,
            IsTeaser = basic.IsTeaser,
            Level = avatar.Level,
            LevelText = basic.Unlocked ? $"Lv.{avatar.Level}" : Lang.ZZZCultivatePage_NotOwned,
            SearchKey = string.Join('\n', avatar.Name, avatar.FullName, englishName, avatar.CampName),
            ElementType = avatar.ElementType,
            Profession = avatar.Profession,
        };
    }

}



/// <summary>
/// 筛选弹层里的一个可勾选项。勾选变化时由页面重新过滤。
/// </summary>
public sealed partial class ZZZCultivateFilterOption : ObservableObject
{

    /// <summary>与 <see cref="ZZZCultivateAgentItem.GetFilterValue"/> 的返回值对应。</summary>
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>CheckBox.IsChecked 是 bool?，双向绑定要同类型。</summary>
    public bool? IsChecked { get; set => SetProperty(ref field, value); }

}



/// <summary>
/// 筛选弹层里的一组选项。组内为「或」，组间为「且」，一组都不勾表示不限。
/// </summary>
public sealed class ZZZCultivateFilterGroup
{

    public const string Ownership = "ownership";
    public const string Rarity = "rarity";
    public const string Attribute = "attribute";
    public const string Specialty = "specialty";

    public const string OwnershipTeaser = "teaser";
    public const string OwnershipOwned = "owned";
    public const string OwnershipNotOwned = "not_owned";

    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    public List<ZZZCultivateFilterOption> Options { get; set; } = [];

}



/// <summary>
/// 技能行。
/// </summary>
public sealed class ZZZCultivateSkillRow
{

    public string TypeName { get; set; } = "";

    public string Title { get; set; } = "";

    public string LevelText { get; set; } = "";

}



/// <summary>
/// 音擎（已装备或推荐）。
/// </summary>
public sealed class ZZZCultivateWeaponView
{

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public string Rarity { get; set; } = "";

    /// <summary>等级与精炼，推荐音擎为空。</summary>
    public string LevelText { get; set; } = "";

    public string MainStat { get; set; } = "";

    public string SubStat { get; set; } = "";

    public string TalentTitle { get; set; } = "";

    public string TalentContent { get; set; } = "";


    /// <summary>
    /// 由音擎数据构造。
    /// </summary>
    /// <param name="weapon">音擎。</param>
    /// <param name="withLevel">是否显示等级与精炼（推荐音擎不显示）。</param>
    /// <returns>音擎视图模型。</returns>
    public static ZZZCultivateWeaponView From(ZZZCultivateWeapon weapon, bool withLevel)
    {
        return new ZZZCultivateWeaponView
        {
            Name = weapon.Name,
            Icon = weapon.Icon,
            Rarity = weapon.Rarity,
            LevelText = withLevel ? $"Lv.{weapon.Level} · {string.Format(Lang.ZZZCultivatePage_RefinementFormat, weapon.Star)}" : "",
            MainStat = FormatProperty(weapon.MainProperties?.FirstOrDefault()),
            SubStat = FormatProperty(weapon.Properties?.FirstOrDefault()),
            TalentTitle = weapon.TalentTitle ?? "",
            TalentContent = weapon.TalentContent ?? "",
        };
    }


    /// <summary>
    /// 词条显示为「名称 数值」。
    /// </summary>
    /// <param name="property">词条，可空。</param>
    /// <returns>显示文字；无词条时为空。</returns>
    private static string FormatProperty(ZZZCultivateEquipProperty? property)
    {
        return property is null ? "" : $"{property.PropertyName} {property.Base}";
    }

}



/// <summary>
/// 推荐方案里的驱动盘套装。
/// </summary>
public sealed class ZZZCultivateSuitView
{

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public string CountText { get; set; } = "";

    public string Desc { get; set; } = "";

}



/// <summary>
/// 推荐方案里的配队成员。
/// </summary>
public sealed class ZZZCultivateTeamMember
{

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

}



/// <summary>
/// 推荐方案。
/// </summary>
public sealed class ZZZCultivatePlanView
{

    public string Name { get; set; } = "";

    public string Author { get; set; } = "";

    public string Desc { get; set; } = "";

    public List<ZZZCultivateWeaponView> Weapons { get; set; } = [];

    public List<ZZZCultivateSuitView> Suits { get; set; } = [];

    /// <summary>4 / 5 / 6 号位主词条，每行「n号位：A / B」。</summary>
    public string MainStats { get; set; } = "";

    public string SubStats { get; set; } = "";

    public List<ZZZCultivateTeamMember> Team { get; set; } = [];

    public Visibility AuthorVisibility => string.IsNullOrWhiteSpace(Author) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DescVisibility => string.IsNullOrWhiteSpace(Desc) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WeaponsVisibility => Weapons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SuitsVisibility => Suits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MainStatsVisibility => string.IsNullOrEmpty(MainStats) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SubStatsVisibility => string.IsNullOrEmpty(SubStats) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TeamVisibility => Team.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

}



/// <summary>
/// 养成计算的一行等级输入（代理人、各技能、音擎）。目标等级双向绑定到 NumberBox。
/// </summary>
public sealed partial class ZZZCultivateCalcRow : ObservableObject
{

    /// <summary>skill_type；代理人与音擎行为 -1。</summary>
    public int SkillType { get; set; } = -1;

    public string Name { get; set; } = "";

    /// <summary>当前等级；音擎行随所选音擎变化。</summary>
    public int Current
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CurrentText));
                OnPropertyChanged(nameof(Minimum));
            }
        }
    }

    public int Max { get; set; }

    public double Target { get; set => SetProperty(ref field, value); }

    public string CurrentText => $"Lv.{Current}";

    /// <summary>NumberBox 下限（double）。</summary>
    public double Minimum => Current;

    /// <summary>NumberBox 上限（double）。</summary>
    public double Maximum => Max;

}



/// <summary>
/// 养成计算可选的音擎（第一项为不计算音擎）。
/// </summary>
public sealed class ZZZCultivateWeaponOption
{

    /// <summary>音擎 Id；0 表示不计算音擎。</summary>
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public override string ToString() => Name;

}



/// <summary>
/// 右侧详情：选中一个代理人时整体替换，计算结果单独通知。
/// </summary>
public sealed partial class ZZZCultivateDetailView : ObservableObject
{

    public int AvatarId { get; set; }

    /// <summary>未实装的预告代理人：没有技能等级与方案，材料由详情接口按开关算好。</summary>
    public bool IsTeaser { get; set; }

    public bool Unlocked { get; set; }

    /// <summary>突破阶段，养成计算用；未拥有时按网页传 1。</summary>
    public int Promotes { get; set; }

    public string Name { get; set; } = "";

    public string FullName { get; set; } = "";

    public string CampName { get; set; } = "";

    /// <summary>阵营图标（接口的 group_icon_path）。</summary>
    public string CampIcon { get; set; } = "";

    /// <summary>代理人头像（icon_info 的 square_avatar）。</summary>
    public string Icon { get; set; } = "";

    public string Rarity { get; set; } = "";

    public string LevelText { get; set; } = "";

    public string RankText { get; set; } = "";

    /// <summary>技能升级优先级，如「核心技 = 特殊技 &gt; 支援技」。</summary>
    public string SkillPriority { get; set; } = "";

    public Visibility SkillPriorityVisibility => string.IsNullOrEmpty(SkillPriority) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>已拥有的已实装代理人才有技能与音擎卡片。</summary>
    public Visibility OwnedVisibility => Unlocked && !IsTeaser ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>已实装代理人用自定义等级计算。</summary>
    public Visibility CalculatorVisibility => IsTeaser ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>预告代理人用两个开关取官方预算结果。</summary>
    public Visibility TeaserVisibility => IsTeaser ? Visibility.Visible : Visibility.Collapsed;

    public List<ZZZCultivateSkillRow> Skills { get; set; } = [];

    public ZZZCultivateWeaponView? Weapon { get; set; }

    public Visibility WeaponVisibility => Weapon is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NoWeaponVisibility => Weapon is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>已装备音擎的 Id 与等级，养成计算默认从它起算。</summary>
    public int EquippedWeaponId { get; set; }

    public int EquippedWeaponLevel { get; set; }

    public ZZZCultivatePlanView? Plan { get; set; }

    public Visibility PlanVisibility => Plan is null ? Visibility.Collapsed : Visibility.Visible;

    public List<ZZZCultivateCalcRow> CalcRows { get; set; } = [];

    public List<ZZZCultivateWeaponOption> WeaponOptions { get; set; } = [];

    /// <summary>音擎目标等级；当前等级随所选音擎变化。</summary>
    public ZZZCultivateCalcRow WeaponRow { get; set; } = new();

    public ZZZCultivateWeaponOption? SelectedWeaponOption
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(WeaponRowVisibility));
            }
        }
    }

    public Visibility WeaponRowVisibility => SelectedWeaponOption is { Id: not 0 } ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>计算结果；null 表示还没算。</summary>
    public ZZZCultivateMaterialResult? Result
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ResultVisibility));
                OnPropertyChanged(nameof(AllEnoughVisibility));
            }
        }
    }

    public Visibility ResultVisibility => Result is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>算过且没有任何材料还缺时显示「材料都够了」。</summary>
    public Visibility AllEnoughVisibility => Result is { } result && !result.Items.Any(x => x.IsLacking) ? Visibility.Visible : Visibility.Collapsed;

    public bool IsCalculating { get; set => SetProperty(ref field, value); }

}



/// <summary>
/// 养成计算结果：每种材料的需要、持有（含低阶合成补充）与缺口，见 <see cref="ZZZCultivateMaterialPlanner"/>。
/// </summary>
public sealed class ZZZCultivateMaterialResult
{

    public List<ZZZCultivateMaterialView> Items { get; set; } = [];

}



/// <summary>
/// 养成计算的材料格：需要、持有（含合成补充）与结论。
/// </summary>
public sealed class ZZZCultivateMaterialView
{

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public string Rarity { get; set; } = "";

    /// <summary>「需要 n」。</summary>
    public string NeedText { get; set; } = "";

    /// <summary>「持有 n」，用低阶合成补了数量时为「持有 n + 合成 m」。</summary>
    public string OwnedText { get; set; } = "";

    /// <summary>「还差 n / 已足够 / 未开放」；官方折算兜底时低阶不下结论，为空。</summary>
    public string StatusText { get; set; } = "";

    /// <summary>还缺时为 true，用警示色。</summary>
    public bool IsLacking { get; set; }

    /// <summary>已足够，用成功色。</summary>
    public bool IsEnough { get; set; }

    /// <summary>材料尚未开放获取，用弱化色。</summary>
    public bool NotOpened { get; set; }

    public Visibility LackVisibility => IsLacking ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EnoughVisibility => IsEnough ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NotOpenedVisibility => NotOpened ? Visibility.Visible : Visibility.Collapsed;

}



/// <summary>
/// 把养成指南接口数据整理成页面显示内容。
/// </summary>
public static class ZZZCultivateViewBuilder
{

    /// <summary>skill_type 显示顺序：普通攻击、闪避、支援技、特殊技、连携技、核心技（与游戏技能页一致）。</summary>
    private static readonly int[] SkillOrder = [0, 2, 6, 1, 3, 5];


    /// <summary>
    /// skill_type 对应的技能类别名。
    /// </summary>
    /// <param name="skillType">0 普通攻击、1 特殊技、2 闪避、3 连携技、5 核心技、6 支援技。</param>
    /// <returns>本地化名称；未知类型返回数字。</returns>
    public static string GetSkillTypeName(int skillType) => skillType switch
    {
        0 => Lang.ZZZCultivatePage_SkillBasicAttack,
        1 => Lang.ZZZCultivatePage_SkillSpecialAttack,
        2 => Lang.ZZZCultivatePage_SkillDodge,
        3 => Lang.ZZZCultivatePage_SkillChainAttack,
        5 => Lang.ZZZCultivatePage_SkillCore,
        6 => Lang.ZZZCultivatePage_SkillAssist,
        _ => skillType.ToString(CultureInfo.InvariantCulture),
    };


    /// <summary>
    /// 技能等级上限：核心技 7，其余 12（与网页计算器一致）。
    /// </summary>
    /// <param name="skillType">skill_type。</param>
    /// <returns>最高等级。</returns>
    public static int GetSkillMaxLevel(int skillType) => skillType == 5 ? 7 : 12;


    /// <summary>
    /// 代理人头像：取 <c>icon_info</c> 的方形头像；没有时为空（不回退到阵营图标，免得误认）。
    /// </summary>
    /// <param name="avatarId">代理人 Id。</param>
    /// <param name="icons">头像表，可空。</param>
    /// <returns>头像地址。</returns>
    public static string GetAvatarIcon(int avatarId, UpgradeGuidIconInfo? icons)
    {
        return icons?.AvatarIcon?.TryGetValue(avatarId.ToString(CultureInfo.InvariantCulture), out var icon) == true ? icon.SquareAvatar ?? "" : "";
    }


    /// <summary>属性在筛选里的顺序：基础五属性按游戏顺序，后加的风、流明排后面。</summary>
    private static readonly int[] AttributeOrder = [200, 201, 202, 203, 205, 204, 300];

    /// <summary>特性在筛选里的顺序。</summary>
    private static readonly int[] SpecialtyOrder = [1, 2, 3, 4, 5, 6, 7];


    /// <summary>
    /// 属性名（只认主属性；霜烈、玄墨等是 sub_element_type，归到对应主属性）。
    /// </summary>
    /// <param name="elementType">element_type。</param>
    /// <returns>本地化名称；未知属性为 null（不出现在筛选里）。</returns>
    public static string? GetAttributeName(int elementType) => elementType switch
    {
        200 => Lang.ZZZCultivatePage_AttributePhysical,
        201 => Lang.ZZZCultivatePage_AttributeFire,
        202 => Lang.ZZZCultivatePage_AttributeIce,
        203 => Lang.ZZZCultivatePage_AttributeElectric,
        204 => Lang.ZZZCultivatePage_AttributeWind,
        205 => Lang.ZZZCultivatePage_AttributeEther,
        300 => Lang.ZZZCultivatePage_AttributeLumen,
        _ => null,
    };


    /// <summary>
    /// 特性名（网页图标名 ZzzGameSpecialties*：Attack、Stun、Anomaly、Support、Defense、Rupture、Fengyu）。
    /// </summary>
    /// <param name="profession">avatar_profession。</param>
    /// <returns>本地化名称；未知特性为 null（不出现在筛选里）。</returns>
    public static string? GetSpecialtyName(int profession) => profession switch
    {
        1 => Lang.ZZZCultivatePage_SpecialtyAttack,
        2 => Lang.ZZZCultivatePage_SpecialtyStun,
        3 => Lang.ZZZCultivatePage_SpecialtyAnomaly,
        4 => Lang.ZZZCultivatePage_SpecialtySupport,
        5 => Lang.ZZZCultivatePage_SpecialtyDefense,
        6 => Lang.ZZZCultivatePage_SpecialtyRupture,
        7 => Lang.ZZZCultivatePage_SpecialtyArmorer,
        _ => null,
    };


    /// <summary>
    /// 按当前代理人数据生成筛选组，只列出实际出现过的取值；沿用上一次的勾选状态（刷新后不丢）。
    /// </summary>
    /// <param name="agents">全部代理人。</param>
    /// <param name="previous">上一次的筛选组，可空。</param>
    /// <returns>筛选组：拥有情况、稀有度、属性、特性（阵营用搜索框即可，不单列）。</returns>
    public static List<ZZZCultivateFilterGroup> BuildFilterGroups(IReadOnlyCollection<ZZZCultivateAgentItem> agents, IEnumerable<ZZZCultivateFilterGroup>? previous)
    {
        var checkedKeys = (previous ?? [])
            .SelectMany(g => g.Options.Where(o => o.IsChecked == true).Select(o => $"{g.Key}|{o.Key}"))
            .ToHashSet();

        ZZZCultivateFilterGroup Group(string key, string title, IEnumerable<(string Key, string Label)> options) => new()
        {
            Key = key,
            Title = title,
            Options = options.Select(o => new ZZZCultivateFilterOption
            {
                Key = o.Key,
                Label = o.Label,
                IsChecked = checkedKeys.Contains($"{key}|{o.Key}"),
            }).ToList(),
        };

        var ownership = new List<(string, string)>();
        if (agents.Any(x => x.IsTeaser))
        {
            ownership.Add((ZZZCultivateFilterGroup.OwnershipTeaser, Lang.ZZZCultivatePage_Teaser));
        }
        ownership.Add((ZZZCultivateFilterGroup.OwnershipOwned, Lang.ZZZCultivatePage_Owned));
        ownership.Add((ZZZCultivateFilterGroup.OwnershipNotOwned, Lang.ZZZCultivatePage_NotOwned));

        var rarities = agents.Select(x => x.Rarity).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()
            .OrderByDescending(x => x, StringComparer.Ordinal)
            .Select(x => (x, x));

        var attributes = AttributeOrder.Where(code => agents.Any(x => x.ElementType == code))
            .Select(code => (code.ToString(CultureInfo.InvariantCulture), GetAttributeName(code)!));

        var specialties = SpecialtyOrder.Where(code => agents.Any(x => x.Profession == code))
            .Select(code => (code.ToString(CultureInfo.InvariantCulture), GetSpecialtyName(code)!));

        return
        [
            Group(ZZZCultivateFilterGroup.Ownership, Lang.ZZZCultivatePage_FilterOwnership, ownership),
            Group(ZZZCultivateFilterGroup.Rarity, Lang.ZZZCultivatePage_FilterRarity, rarities),
            Group(ZZZCultivateFilterGroup.Attribute, Lang.ZZZCultivatePage_FilterAttribute, attributes),
            Group(ZZZCultivateFilterGroup.Specialty, Lang.ZZZCultivatePage_FilterSpecialty, specialties),
        ];
    }


    /// <summary>
    /// 是否通过筛选：每个有勾选的组里，代理人的取值都要在勾选项中。
    /// </summary>
    /// <param name="agent">代理人。</param>
    /// <param name="groups">筛选组。</param>
    /// <returns>是否显示。</returns>
    public static bool PassesFilter(ZZZCultivateAgentItem agent, IEnumerable<ZZZCultivateFilterGroup> groups)
    {
        foreach (var group in groups)
        {
            var selected = group.Options.Where(o => o.IsChecked == true).Select(o => o.Key).ToList();
            if (selected.Count > 0 && !selected.Contains(agent.GetFilterValue(group.Key)))
            {
                return false;
            }
        }
        return true;
    }


    /// <summary>
    /// 列表排序：预告代理人固定在最上面，其次已拥有（等级、稀有度从高到低），最后未拥有。
    /// </summary>
    /// <param name="items">列表项。</param>
    /// <returns>排好序的列表。</returns>
    public static List<ZZZCultivateAgentItem> SortAgents(IEnumerable<ZZZCultivateAgentItem> items)
    {
        return items
            .OrderByDescending(x => x.IsTeaser)
            .ThenByDescending(x => x.Unlocked)
            .ThenByDescending(x => x.Unlocked ? x.Level : 0)
            .ThenByDescending(x => x.Rarity, StringComparer.Ordinal)
            .ThenByDescending(x => x.Id)
            .ToList();
    }


    /// <summary>
    /// 技能行，按游戏技能页顺序。
    /// </summary>
    /// <param name="detail">代理人详情。</param>
    /// <returns>技能行。</returns>
    public static List<ZZZCultivateSkillRow> BuildSkills(ZZZCultivateAvatarDetail detail)
    {
        return (detail.Avatar.Skills ?? [])
            .OrderBy(x => Array.IndexOf(SkillOrder, x.SkillType) is int i and >= 0 ? i : int.MaxValue)
            .Select(x => new ZZZCultivateSkillRow
            {
                TypeName = GetSkillTypeName(x.SkillType),
                Title = StripSkillTypePrefix(x.Items?.FirstOrDefault()?.Title),
                LevelText = $"Lv.{x.Level}/{GetSkillMaxLevel(x.SkillType)}",
            })
            .ToList();
    }


    /// <summary>
    /// 技能名去掉「普通攻击：」这类与类别名重复的前缀。
    /// </summary>
    /// <param name="title">接口返回的第一条技能标题。</param>
    /// <returns>技能名。</returns>
    private static string StripSkillTypePrefix(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "";
        }
        int index = title.IndexOfAny(['：', ':']);
        return index >= 0 && index < title.Length - 1 ? title[(index + 1)..].Trim() : title;
    }


    /// <summary>
    /// 推荐方案；接口没给方案时为 null（预告代理人 plan.id 为 0、名称为空）。
    /// </summary>
    /// <param name="detail">代理人详情。</param>
    /// <param name="icons">代理人头像表，配队头像用，可空。</param>
    /// <returns>方案视图模型。</returns>
    public static ZZZCultivatePlanView? BuildPlan(ZZZCultivateAvatarDetail detail, UpgradeGuidIconInfo? icons)
    {
        var plan = detail.Plan;
        if (plan is null || string.IsNullOrWhiteSpace(plan.Name))
        {
            return null;
        }
        var item = plan.Item;
        var view = new ZZZCultivatePlanView
        {
            Name = plan.Name,
            Author = string.IsNullOrWhiteSpace(detail.User?.Name) ? "" : string.Format(Lang.ZZZCultivatePage_PlanAuthorFormat, detail.User.Name),
            Desc = plan.Desc?.Trim() ?? "",
        };
        foreach (var weapon in new[] { item?.Weapon?.Main, item?.Weapon?.Backup })
        {
            if (weapon is not null && weapon.Id != 0)
            {
                view.Weapons.Add(ZZZCultivateWeaponView.From(weapon, false));
            }
        }
        foreach (var suit in item?.Equip?.Equip ?? [])
        {
            view.Suits.Add(new ZZZCultivateSuitView
            {
                Name = suit.Name,
                Icon = suit.Icon,
                CountText = suit.Count > 0 ? $"×{suit.Count}" : "",
                Desc = suit.Count >= 4 ? $"{suit.Desc1}\\n{suit.Desc2}" : suit.Desc1 ?? "",
            });
        }
        var mainLines = new List<string>();
        AddMainStatLine(mainLines, 4, item?.Equip?.MainProperties4);
        AddMainStatLine(mainLines, 5, item?.Equip?.MainProperties5);
        AddMainStatLine(mainLines, 6, item?.Equip?.MainProperties6);
        view.MainStats = string.Join(Environment.NewLine, mainLines);
        view.SubStats = string.Join(" / ", (item?.Equip?.SubProperties ?? []).Select(x => x.PropertyName));
        foreach (var avatar in item?.Team?.Main?.AvatarList ?? [])
        {
            view.Team.Add(new ZZZCultivateTeamMember { Name = avatar.Name, Icon = GetAvatarIcon(avatar.Id, icons) });
        }
        return view;
    }


    /// <summary>
    /// 追加一行「n号位：A / B」。
    /// </summary>
    /// <param name="lines">行列表。</param>
    /// <param name="slot">号位。</param>
    /// <param name="properties">该号位推荐主词条。</param>
    private static void AddMainStatLine(List<string> lines, int slot, List<ZZZCultivatePlanEquipProperty>? properties)
    {
        if (properties is { Count: > 0 })
        {
            lines.Add($"{string.Format(Lang.ZZZCultivatePage_SlotFormat, slot)}  {string.Join(" / ", properties.Select(x => x.PropertyName))}");
        }
    }


    /// <summary>
    /// 技能升级优先级，档内用「=」、档间用「&gt;」连接。预告代理人也有这项。
    /// </summary>
    /// <param name="upgrade">接口给的三档优先级。</param>
    /// <returns>显示文字；没有数据时为空。</returns>
    private static string BuildSkillPriority(ZZZCultivateSkillUpgrade? upgrade)
    {
        if (upgrade is null)
        {
            return "";
        }
        var tiers = new[] { upgrade.First, upgrade.Second, upgrade.Third }
            .Where(x => x is { Count: > 0 })
            .Select(x => string.Join(" = ", x!.Select(GetSkillTypeName)));
        return string.Join("  >  ", tiers);
    }


    /// <summary>
    /// 养成计算的等级输入行：代理人等级 + 六个技能，目标默认拉满。
    /// </summary>
    /// <param name="detail">代理人详情。</param>
    /// <returns>输入行。</returns>
    public static List<ZZZCultivateCalcRow> BuildCalcRows(ZZZCultivateAvatarDetail detail)
    {
        // 未拥有时详情没有技能等级，网页计算器从 1 级起算
        bool owned = detail.Avatar.Unlock;
        var rows = new List<ZZZCultivateCalcRow>
        {
            new()
            {
                Name = Lang.ZZZCultivatePage_AgentLevel,
                Current = owned ? Math.Max(1, detail.Avatar.Level) : 1,
                Max = 60,
                Target = 60,
            },
        };
        var skillLevels = (detail.Avatar.Skills ?? []).ToDictionary(x => x.SkillType, x => x.Level);
        foreach (int type in SkillOrder)
        {
            int current = owned && skillLevels.TryGetValue(type, out int level) ? Math.Max(1, level) : 1;
            int max = GetSkillMaxLevel(type);
            rows.Add(new ZZZCultivateCalcRow
            {
                SkillType = type,
                Name = GetSkillTypeName(type),
                Current = current,
                Max = max,
                Target = max,
            });
        }
        return rows;
    }


    /// <summary>
    /// 由接口详情构造右侧详情。预告代理人直接带上详情接口算好的材料。
    /// </summary>
    /// <param name="detail">代理人详情。</param>
    /// <param name="isTeaser">是否为预告代理人（以列表接口为准）。</param>
    /// <param name="icons">代理人头像表，可空。</param>
    /// <returns>详情视图模型。</returns>
    public static ZZZCultivateDetailView BuildDetail(ZZZCultivateAvatarDetail detail, bool isTeaser, UpgradeGuidIconInfo? icons)
    {
        var avatar = detail.Avatar;
        bool owned = avatar.Unlock && !isTeaser;
        var view = new ZZZCultivateDetailView
        {
            AvatarId = avatar.Id,
            IsTeaser = isTeaser,
            Unlocked = owned,
            Promotes = owned ? avatar.Promotes : 1,
            Name = avatar.Name,
            FullName = avatar.FullName,
            CampName = avatar.CampName,
            CampIcon = avatar.GroupIconPath,
            Icon = GetAvatarIcon(avatar.Id, icons),
            Rarity = avatar.Rarity,
            LevelText = isTeaser ? Lang.ZZZCultivatePage_Teaser
                : owned ? $"Lv.{avatar.Level}/{detail.ItemInfo?.AvatarLevelMax ?? 60}"
                : Lang.ZZZCultivatePage_NotOwned,
            RankText = owned ? string.Format(Lang.ZZZCultivatePage_MindscapeFormat, avatar.Rank) : "",
            SkillPriority = BuildSkillPriority(avatar.SkillUpgrade),
            Skills = owned ? BuildSkills(detail) : [],
            Weapon = owned && detail.Weapon is { Id: not 0 } equippedWeapon ? ZZZCultivateWeaponView.From(equippedWeapon, true) : null,
            EquippedWeaponId = owned ? detail.Weapon?.Id ?? 0 : 0,
            EquippedWeaponLevel = owned ? detail.Weapon?.Level ?? 0 : 0,
            Plan = BuildPlan(detail, icons),
            CalcRows = isTeaser ? [] : BuildCalcRows(detail),
            WeaponRow = new ZZZCultivateCalcRow { Name = Lang.ZZZCultivatePage_WEngine, Max = 60, Target = 60 },
        };

        if (isTeaser)
        {
            if (detail.Teaser is { CalcSuccess: true } teaser)
            {
                view.Result = BuildMaterials(teaser);
            }
            return view;
        }

        // 音擎候选：已装备的、方案主选、方案备选（去重），默认选已装备的，没有就选方案主选
        view.WeaponOptions.Add(new ZZZCultivateWeaponOption { Id = 0, Name = Lang.ZZZCultivatePage_SkipWEngine });
        if (view.EquippedWeaponId != 0 && detail.Weapon is not null)
        {
            view.WeaponOptions.Add(new ZZZCultivateWeaponOption { Id = detail.Weapon.Id, Name = detail.Weapon.Name });
        }
        foreach (var weapon in new[] { detail.Plan?.Item?.Weapon?.Main, detail.Plan?.Item?.Weapon?.Backup })
        {
            if (weapon is { Id: not 0 } && view.WeaponOptions.All(x => x.Id != weapon.Id))
            {
                view.WeaponOptions.Add(new ZZZCultivateWeaponOption { Id = weapon.Id, Name = weapon.Name });
            }
        }
        view.SelectedWeaponOption = view.WeaponOptions.Count > 1 ? view.WeaponOptions[1] : view.WeaponOptions[0];
        if (view.SelectedWeaponOption.Id == view.EquippedWeaponId)
        {
            view.WeaponRow.Current = view.EquippedWeaponLevel;
        }
        return view;
    }


    /// <summary>
    /// 整理计算结果：三阶材料逐阶计算（低阶富余按 3:1 合成到高阶），其余材料直接比较，
    /// 规则见 <see cref="ZZZCultivateMaterialPlanner"/>。还缺的材料线在前。
    /// </summary>
    /// <param name="result">计算结果（或预告代理人的预算结果）。</param>
    /// <returns>材料格列表。</returns>
    public static ZZZCultivateMaterialResult BuildMaterials(ZZZCultivateCalcResult result)
    {
        static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
        var items = ZZZCultivateMaterialPlanner.Plan(result).Select(line =>
        {
            bool lacking = line.HasStatus && !line.NotOpened && line.Lack > 0;
            bool enough = line.HasStatus && !line.NotOpened && line.Lack <= 0;
            return new ZZZCultivateMaterialView
            {
                Name = line.Name,
                Icon = line.Icon,
                Rarity = line.Rarity ?? "",
                NeedText = string.Format(Lang.ZZZCultivatePage_NeedFormat, N(line.Need)),
                OwnedText = line.Synthesized > 0
                    ? string.Format(Lang.ZZZCultivatePage_OwnedSynthFormat, N(line.Owned), N(line.Synthesized))
                    : string.Format(Lang.ZZZCultivatePage_OwnedFormat, N(line.Owned)),
                StatusText = line.NotOpened ? Lang.ZZZCultivatePage_NotOpened
                    : lacking ? string.Format(Lang.ZZZCultivatePage_LackFormat, N(line.Lack))
                    : enough ? Lang.ZZZCultivatePage_Enough
                    : "",
                IsLacking = lacking,
                IsEnough = enough,
                NotOpened = line.NotOpened,
            };
        }).ToList();
        return new ZZZCultivateMaterialResult { Items = items };
    }

}
