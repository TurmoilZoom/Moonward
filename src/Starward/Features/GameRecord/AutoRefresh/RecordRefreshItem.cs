using Starward.Core;
using Starward.Language;
using System.Collections.Generic;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 可以自动更新的数据板块，与工具箱里的记录页一一对应。
/// <para>
/// 枚举名会进 Setting 表的键（<c>auto_record_refresh_&lt;item&gt;_&lt;biz&gt;_&lt;uid&gt;</c>），
/// 改名等于让用户已有的配置失效，不要重命名。
/// </para>
/// </summary>
public enum RecordRefreshItem
{

    /// <summary>原神 · 深境螺旋。</summary>
    SpiralAbyss,

    /// <summary>原神 · 幻想真境剧诗。</summary>
    ImaginariumTheater,

    /// <summary>原神 · 幽境危战。</summary>
    StygianOnslaught,

    /// <summary>原神 · 旅行札记。</summary>
    TravelersDiary,

    /// <summary>星穹铁道 · 忘却之庭。</summary>
    ForgottenHall,

    /// <summary>星穹铁道 · 虚构叙事。</summary>
    PureFiction,

    /// <summary>星穹铁道 · 末日幻影。</summary>
    ApocalypticShadow,

    /// <summary>星穹铁道 · 万敌。</summary>
    ChallengePeak,

    /// <summary>星穹铁道 · 模拟宇宙。</summary>
    SimulatedUniverse,

    /// <summary>星穹铁道 · 开拓月历。</summary>
    TrailblazeCalendar,

    /// <summary>绝区零 · 式舆防卫战。</summary>
    ShiyuDefense,

    /// <summary>绝区零 · 危局强袭战。</summary>
    DeadlyAssault,

    /// <summary>绝区零 · 绳网月报。</summary>
    InterKnotReport,

}


/// <summary>
/// 数据板块与游戏、显示名的对应关系。
/// </summary>
public static class RecordRefreshItemExtensions
{

    /// <summary>
    /// 数据板块的显示名，复用工具箱左侧导航的文案，不额外增加词条。
    /// </summary>
    /// <param name="item">数据板块。</param>
    /// <returns>当前语言下的显示名。</returns>
    public static string GetDisplayName(this RecordRefreshItem item)
    {
        return item switch
        {
            RecordRefreshItem.SpiralAbyss => Lang.HoyolabToolboxPage_SpiralAbyss,
            RecordRefreshItem.ImaginariumTheater => Lang.HoyolabToolboxPage_ImaginariumTheater,
            RecordRefreshItem.StygianOnslaught => Lang.HoyolabToolboxPage_StygianOnslaught,
            RecordRefreshItem.TravelersDiary => Lang.HoyolabToolboxPage_TravelersDiary,
            RecordRefreshItem.ForgottenHall => Lang.HoyolabToolboxPage_ForgottenHall,
            RecordRefreshItem.PureFiction => Lang.HoyolabToolboxPage_PureFiction,
            RecordRefreshItem.ApocalypticShadow => Lang.HoyolabToolboxPage_ApocalypticShadow,
            RecordRefreshItem.ChallengePeak => Lang.GameRecordPage_AnomalyArbitration,
            RecordRefreshItem.SimulatedUniverse => Lang.HoyolabToolboxPage_SimulatedUniverse,
            RecordRefreshItem.TrailblazeCalendar => Lang.HoyolabToolboxPage_TrailblazeMonthlyCalendar,
            RecordRefreshItem.ShiyuDefense => Lang.HoyolabToolboxPage_ShiyuDefense,
            RecordRefreshItem.DeadlyAssault => Lang.HoyolabToolboxPage_DeadlyAssault,
            RecordRefreshItem.InterKnotReport => Lang.HoyolabToolboxPage_InterKnotMonthlyReport,
            _ => string.Empty,
        };
    }


    /// <summary>
    /// 指定游戏拥有的数据板块。崩坏3 只有网页版战绩，没有本地可写库的记录，返回空。
    /// </summary>
    /// <param name="biz">游戏业务线。</param>
    /// <returns>该游戏的全部数据板块。</returns>
    public static IReadOnlyList<RecordRefreshItem> GetItems(GameBiz biz)
    {
        return biz.Game switch
        {
            GameBiz.hk4e =>
            [
                RecordRefreshItem.SpiralAbyss,
                RecordRefreshItem.ImaginariumTheater,
                RecordRefreshItem.StygianOnslaught,
                RecordRefreshItem.TravelersDiary,
            ],
            GameBiz.hkrpg =>
            [
                RecordRefreshItem.ForgottenHall,
                RecordRefreshItem.PureFiction,
                RecordRefreshItem.ApocalypticShadow,
                RecordRefreshItem.ChallengePeak,
                RecordRefreshItem.SimulatedUniverse,
                RecordRefreshItem.TrailblazeCalendar,
            ],
            GameBiz.nap =>
            [
                RecordRefreshItem.ShiyuDefense,
                RecordRefreshItem.DeadlyAssault,
                RecordRefreshItem.InterKnotReport,
            ],
            _ => [],
        };
    }

}
