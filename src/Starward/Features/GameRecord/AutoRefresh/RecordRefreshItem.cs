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
/// 月报类板块（旅行札记 / 开拓月历 / 绳网月报）拆出的两个自动更新任务。
/// <para>
/// 两个任务各存一份 <see cref="RecordRefreshConfig"/>、各自开关与频率，互不影响；
/// 枚举名会进 Setting 表的键，不要重命名。非月报板块只有 <see cref="Current"/> 一个任务。
/// </para>
/// </summary>
public enum RecordRefreshMonthTarget
{

    /// <summary>当月。数据还在滚动，适合配高频率跟踪本月进度。</summary>
    Current = 0,

    /// <summary>上月。数据已定稿，适合配「每月几号」在月初把上个月归档。</summary>
    Previous = 1,

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


    /// <summary>
    /// 是不是按月出数据的板块。只有这类板块才拆成「当月」「上月」两个任务，
    /// 其余板块的接口只给「当期 + 上期」，没有月份可选。
    /// </summary>
    /// <param name="item">数据板块。</param>
    /// <returns>是月报类返回 true。</returns>
    public static bool IsMonthlyReport(this RecordRefreshItem item)
    {
        return item is RecordRefreshItem.TravelersDiary
            or RecordRefreshItem.TrailblazeCalendar
            or RecordRefreshItem.InterKnotReport;
    }


    /// <summary>
    /// 这个板块拆出来的自动更新任务。月报类两个（当月、上月），其余只有当月那一个。
    /// </summary>
    /// <param name="item">数据板块。</param>
    /// <returns>该板块的全部任务。</returns>
    public static IReadOnlyList<RecordRefreshMonthTarget> GetMonthTargets(this RecordRefreshItem item)
    {
        return item.IsMonthlyReport()
            ? [RecordRefreshMonthTarget.Current, RecordRefreshMonthTarget.Previous]
            : [RecordRefreshMonthTarget.Current];
    }


    /// <summary>
    /// 单个任务的显示名。月报类带上月份后缀（如「旅行札记 · 上月」），其余板块就是板块名。
    /// </summary>
    /// <param name="item">数据板块。</param>
    /// <param name="monthTarget">任务对应的月份。</param>
    /// <returns>当前语言下的显示名。</returns>
    public static string GetDisplayName(this RecordRefreshItem item, RecordRefreshMonthTarget monthTarget)
    {
        if (!item.IsMonthlyReport())
        {
            return item.GetDisplayName();
        }
        string month = monthTarget is RecordRefreshMonthTarget.Previous
            ? Lang.AutoRecordRefresh_MonthPrevious
            : Lang.AutoRecordRefresh_MonthCurrent;
        return $"{item.GetDisplayName()} · {month}";
    }


    /// <summary>
    /// 单个任务在配置浮层里那一行的标题：月报类写「更新当月 / 更新上月」，其余板块写「启用自动更新」。
    /// </summary>
    /// <param name="item">数据板块。</param>
    /// <param name="monthTarget">任务对应的月份。</param>
    /// <returns>当前语言下的标题。</returns>
    public static string GetJobTitle(this RecordRefreshItem item, RecordRefreshMonthTarget monthTarget)
    {
        if (!item.IsMonthlyReport())
        {
            return Lang.AutoRecordRefresh_Enable;
        }
        return monthTarget is RecordRefreshMonthTarget.Previous
            ? Lang.AutoRecordRefresh_EnablePreviousMonth
            : Lang.AutoRecordRefresh_EnableCurrentMonth;
    }

}
