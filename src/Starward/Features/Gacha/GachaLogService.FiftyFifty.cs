using System;
using System.Collections.Generic;

namespace Starward.Features.Gacha;

// 只依赖抽卡记录本身、不碰数据库与 WinUI 的纯计算放在这里，测试项目 tests/Starward.Tests 直接链接本文件。
internal abstract partial class GachaLogService
{

    /// <summary>
    /// 统计小保底抽到当期 UP 的情况。
    /// 按时间正序遍历卡池记录，维护「下一个最高稀有度是否为大保底」的状态：
    /// 在非大保底状态下抽出的最高稀有度记为一次小保底，其中 <see cref="GachaLogItemEx.IsUp"/> 为 true 的记为一次「不歪」；
    /// 若小保底歪了（非 UP），则下一个最高稀有度为大保底（必出 UP），不计入小保底统计。
    /// </summary>
    /// <param name="orderedList">按时间（Id）正序排列的卡池全部记录。</param>
    /// <param name="highestRankType">最高稀有度对应的 RankType（原神/星铁为 5，绝区零 S 级为 4）。</param>
    /// <returns>(小保底次数, 小保底不歪次数, 最多连续不歪次数, 最多连续歪次数, 下一次是否为大保底)。</returns>
    protected internal static (int Count, int NoUpCount, int MaxUpStreak, int MaxMissStreak, bool IsNextGuaranteed) CountFiftyFiftyNoUp(IEnumerable<GachaLogItemEx> orderedList, int highestRankType)
    {
        int count = 0;
        int noUpCount = 0;
        int upStreak = 0;
        int missStreak = 0;
        int maxUpStreak = 0;
        int maxMissStreak = 0;
        bool guaranteed = false;
        foreach (GachaLogItemEx item in orderedList)
        {
            if (item.RankType != highestRankType)
            {
                continue;
            }
            if (guaranteed)
            {
                // 大保底必出 UP，不计入小保底统计，也不打断连胜/连歪计数
                guaranteed = false;
            }
            else
            {
                count++;
                if (item.IsUp)
                {
                    noUpCount++;
                    upStreak++;
                    missStreak = 0;
                    maxUpStreak = Math.Max(maxUpStreak, upStreak);
                }
                else
                {
                    missStreak++;
                    upStreak = 0;
                    maxMissStreak = Math.Max(maxMissStreak, missStreak);
                    guaranteed = true;
                }
            }
        }
        return (count, noUpCount, maxUpStreak, maxMissStreak, guaranteed);
    }

}
