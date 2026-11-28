using Starward.Core.GameRecord.ZZZ.UpgradeGuide;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Starward.Features.GameRecord.ZZZ;


/// <summary>
/// 一种养成材料的需求与缺口。
/// </summary>
public sealed class ZZZCultivateMaterialLine
{

    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public string? Rarity { get; set; }

    /// <summary>需要数量（代理人、技能、音擎三类合并）。</summary>
    public long Need { get; set; }

    /// <summary>持有数量；库存里没有该物品时为 0。</summary>
    public long Owned { get; set; }

    /// <summary>用低一阶多出来的材料合成、实际补到这一阶的数量。</summary>
    public long Synthesized { get; set; }

    /// <summary>算上合成后仍缺的数量。</summary>
    public long Lack { get; set; }

    public bool NotOpened { get; set; }

    /// <summary>
    /// 是否给出足够 / 缺少的结论。逐阶结果与官方折算对不上时，只在最高阶给官方结论，低阶为 false。
    /// </summary>
    public bool HasStatus { get; set; } = true;

}



/// <summary>
/// 养成材料缺口计算（纯计算，不依赖 WinUI，测试项目直接链接）。
/// </summary>
/// <remarks>
/// 芯片、认证章、音擎组件分三阶，游戏里只能 3 个低阶合成 1 个高阶，不能拆分。
/// 官方 need_get 把整条材料线折算成最高阶给一个总数：低阶缺口被高阶富余抵掉也算「足够」，
/// 也看不出差的是哪一阶。这里改为从低到高逐阶算：每阶先用持有的，多出来的按 3:1 合成到上一阶，
/// 低阶缺的就是缺的。同时按官方公式复算一遍作校验，对不上（比例或分阶规则变了）就退回官方结果。
/// </remarks>
public static class ZZZCultivateMaterialPlanner
{

    /// <summary>低阶合成高阶的比例。</summary>
    public const int SynthesisRatio = 3;


    /// <summary>
    /// 判断是否为三阶材料，并取出材料线与阶数。物品 Id 的十位是阶数：
    /// 芯片 1001x1（如 100111 / 100121 / 100131）、认证章 1002xx、音擎组件 1010xx。
    /// </summary>
    /// <param name="id">物品 Id。</param>
    /// <param name="family">材料线键（十位清零后的 Id）。</param>
    /// <param name="tier">阶数 1~3。</param>
    /// <returns>是否为三阶材料。</returns>
    public static bool TryGetTier(int id, out int family, out int tier)
    {
        int prefix = id / 100;
        tier = id / 10 % 10;
        if (prefix is 1001 or 1002 or 1010 && tier is >= 1 and <= 3)
        {
            family = id - tier * 10;
            return true;
        }
        family = 0;
        tier = 0;
        return false;
    }


    /// <summary>
    /// 计算每种材料的需求、持有、合成补充与缺口。还缺的材料线排在前面，同一材料线按阶数从低到高相邻。
    /// </summary>
    /// <param name="result">计算结果（或预告代理人的预算结果）。</param>
    /// <returns>材料行。</returns>
    public static List<ZZZCultivateMaterialLine> Plan(ZZZCultivateCalcResult result)
    {
        var need = new Dictionary<int, long>();
        var materials = new Dictionary<int, ZZZCultivateMaterial>();
        var order = new List<int>();
        foreach (var material in (result.AvatarConsume ?? []).Concat(result.SkillConsume ?? []).Concat(result.WeaponConsume ?? []))
        {
            if (!need.ContainsKey(material.Id))
            {
                need[material.Id] = 0;
                materials[material.Id] = material;
                order.Add(material.Id);
            }
            need[material.Id] += material.Count;
        }

        var owned = new Dictionary<int, long>();
        foreach (var (key, value) in result.UserOwnsMaterials ?? [])
        {
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                owned[id] = value;
            }
        }
        var official = (result.NeedGet ?? []).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        var groups = new List<List<ZZZCultivateMaterialLine>>();
        var families = new Dictionary<int, List<ZZZCultivateMaterialLine>>();
        foreach (int id in order)
        {
            var material = materials[id];
            official.TryGetValue(id, out var officialItem);
            var line = new ZZZCultivateMaterialLine
            {
                Id = id,
                Name = material.Name,
                Icon = material.Icon,
                Rarity = material.Rarity,
                Need = need[id],
                Owned = owned.GetValueOrDefault(id),
                NotOpened = material.NotOpened || (officialItem?.NotOpened ?? false),
            };
            if (TryGetTier(id, out int family, out _))
            {
                if (!families.TryGetValue(family, out var group))
                {
                    group = [];
                    families[family] = group;
                    groups.Add(group);
                }
                group.Add(line);
            }
            else
            {
                // 丁尼、周本材料等不分阶；经验材料官方会按经验值折算低级记录，有官方值就用官方值
                line.Lack = officialItem is not null ? Math.Max(0, officialItem.Count) : Math.Max(0, line.Need - line.Owned);
                groups.Add([line]);
            }
        }

        foreach (var group in families.Values)
        {
            group.Sort((a, b) => (a.Id / 10 % 10).CompareTo(b.Id / 10 % 10));
            Cascade(group, official);
        }

        // OrderByDescending 是稳定排序：组内顺序与组间原顺序都保留
        return groups
            .OrderByDescending(g => g.Any(x => x.HasStatus && !x.NotOpened && x.Lack > 0))
            .SelectMany(g => g)
            .ToList();
    }


    /// <summary>
    /// 一条材料线从低阶到高阶逐阶计算，并与官方折算值核对。
    /// </summary>
    /// <param name="tiers">同一材料线、按阶数升序的材料行。</param>
    /// <param name="official">官方 need_get，按物品 Id 索引。</param>
    private static void Cascade(List<ZZZCultivateMaterialLine> tiers, Dictionary<int, ZZZCultivateMaterial> official)
    {
        long carry = 0;
        int previousTier = 0;
        foreach (var line in tiers)
        {
            int tier = line.Id / 10 % 10;
            // 中间隔了一阶（没有消耗这一阶）时，富余要再合成一次才到这一阶
            for (int t = previousTier + 1; previousTier != 0 && t < tier; t++)
            {
                carry /= SynthesisRatio;
            }
            long available = line.Owned + carry;
            line.Synthesized = Math.Min(carry, Math.Max(0, line.Need - line.Owned));
            line.Lack = Math.Max(0, line.Need - available);
            carry = Math.Max(0, available - line.Need) / SynthesisRatio;
            previousTier = tier;
        }

        var top = tiers[^1];
        if (!official.TryGetValue(top.Id, out var officialTop))
        {
            return;
        }
        int topTier = top.Id / 10 % 10;
        double folded = tiers.Sum(x => (x.Need - x.Owned) / Math.Pow(SynthesisRatio, topTier - x.Id / 10 % 10));
        long expected = Math.Max(0, (long)Math.Ceiling(folded - 1e-9));
        if (expected != officialTop.Count)
        {
            foreach (var line in tiers)
            {
                line.Synthesized = 0;
                line.Lack = 0;
                line.HasStatus = false;
            }
            top.Lack = Math.Max(0, officialTop.Count);
            top.HasStatus = true;
        }
    }

}
