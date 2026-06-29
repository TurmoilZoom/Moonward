namespace Starward.Core.GameRecord.Genshin.TravelersDiary;

/// <summary>
/// 旅行札记原石收入构成本地缓存行，对应 SQLite <c>GenshinTravelersDiaryIncomeComponent</c>。
/// </summary>
public class TravelersDiaryIncomeComponentCache
{

    /// <summary>游戏 UID。</summary>
    public long Uid { get; set; }

    /// <summary>年份。</summary>
    public int Year { get; set; }

    /// <summary>月份（1–12）。</summary>
    public int Month { get; set; }

    /// <summary>收入来源 action_id（与 API <c>group_by[].action_id</c> 一致）。</summary>
    public int ActionId { get; set; }

    /// <summary>该来源在查询月获得的原石数量。</summary>
    public int Num { get; set; }

    /// <summary>该来源占当月原石总量的百分比，单位 %。</summary>
    public int Percent { get; set; }

}