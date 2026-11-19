using Starward.Core;
using Starward.Core.GameRecord.BH3.DailyNote;
using Starward.Core.GameRecord.Genshin.DailyNote;
using Starward.Core.GameRecord.StarRail.DailyNote;
using Starward.Core.GameRecord.ZZZ.DailyNote;
using System.Collections.Generic;

namespace Starward.Features.GameRecord.DailyNote.Notify;

/// <summary>
/// 可提醒的便笺项目。**枚举名会写进 Setting 表里的 JSON 配置，不能改名**；新增项目只往后加。
/// </summary>
public enum DailyNoteNotifyItem
{
    /// <summary>原神 原粹树脂。</summary>
    GenshinResin,

    /// <summary>原神 洞天宝钱。</summary>
    GenshinHomeCoin,

    /// <summary>星铁 开拓力。</summary>
    StarRailStamina,

    /// <summary>绝区零 电量。</summary>
    ZZZEnergy,

    /// <summary>崩坏3 体力。</summary>
    BH3Stamina,
}


/// <summary>
/// 便笺项目目录：各游戏有哪些可提醒项目，以及从便笺数据里取出对应数值。只依赖 Core，单元测试直接链接本文件。
/// </summary>
public static class DailyNoteNotifyItems
{

    private static readonly DailyNoteNotifyItem[] GenshinItems = [DailyNoteNotifyItem.GenshinResin, DailyNoteNotifyItem.GenshinHomeCoin];

    private static readonly DailyNoteNotifyItem[] StarRailItems = [DailyNoteNotifyItem.StarRailStamina];

    private static readonly DailyNoteNotifyItem[] ZZZItems = [DailyNoteNotifyItem.ZZZEnergy];

    private static readonly DailyNoteNotifyItem[] BH3Items = [DailyNoteNotifyItem.BH3Stamina];


    /// <summary>
    /// 指定游戏可提醒的项目，按便笺里的显示顺序。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>项目列表；不支持的游戏为空。</returns>
    public static IReadOnlyList<DailyNoteNotifyItem> GetItems(GameBiz biz) => biz.Game switch
    {
        GameBiz.hk4e => GenshinItems,
        GameBiz.hkrpg => StarRailItems,
        GameBiz.nap => ZZZItems,
        GameBiz.bh3 => BH3Items,
        _ => [],
    };


    /// <summary>
    /// 从便笺数据里取出项目的数值。
    /// </summary>
    /// <param name="item">便笺项目。</param>
    /// <param name="note">某游戏的便笺（<see cref="GenshinDailyNote"/> 等）。</param>
    /// <returns>数值快照；便笺与项目不属于同一游戏、或该项目未解锁时为 null。</returns>
    public static DailyNoteMetric? GetMetric(DailyNoteNotifyItem item, object? note) => (item, note) switch
    {
        (DailyNoteNotifyItem.GenshinResin, GenshinDailyNote g) => new DailyNoteMetric(g.CurrentResin, g.MaxResin, g.ResinRecoveryTime),
        // 尘歌壶没解锁时上限为 0
        (DailyNoteNotifyItem.GenshinHomeCoin, GenshinDailyNote { MaxHomeCoin: > 0 } g) => new DailyNoteMetric(g.CurrentHomeCoin, g.MaxHomeCoin, g.HomeCoinRecoveryTime),
        (DailyNoteNotifyItem.StarRailStamina, StarRailDailyNote s) => new DailyNoteMetric(s.CurrentStamina, s.MaxStamina, s.StaminaRecoverTime),
        (DailyNoteNotifyItem.ZZZEnergy, ZZZDailyNote { Energy.Progress: { } p } z) => new DailyNoteMetric(p.Current, p.Max, z.Energy.Restore),
        (DailyNoteNotifyItem.BH3Stamina, BH3DailyNote b) => new DailyNoteMetric(b.CurrentStamina, b.MaxStamina, b.StaminaRecoverTime),
        _ => null,
    };

}
