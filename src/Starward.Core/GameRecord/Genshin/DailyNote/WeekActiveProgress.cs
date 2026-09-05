using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Genshin.DailyNote;

/// <summary>
/// 砺行修远
/// </summary>
public class WeekActiveProgress
{

    /// <summary>
    /// 本周进度
    /// </summary>
    [JsonPropertyName("progress_current")]
    public int ProgressCurrent { get; set; }

    /// <summary>
    /// 本周进度上限
    /// </summary>
    [JsonPropertyName("progress_total")]
    public int ProgressTotal { get; set; }

    /// <summary>
    /// 本期进度
    /// </summary>
    [JsonPropertyName("period_progress_current")]
    public int PeriodProgressCurrent { get; set; }

    /// <summary>
    /// 本期进度上限
    /// </summary>
    [JsonPropertyName("period_progress_total")]
    public int PeriodProgressTotal { get; set; }

    /// <summary>
    /// 是否已解锁
    /// </summary>
    [JsonPropertyName("unlock")]
    public bool Unlock { get; set; }

    /// <summary>
    /// 本周已完成的日序号，从 1 开始
    /// </summary>
    [JsonPropertyName("progress_current_arr")]
    public List<int> ProgressCurrentArr { get; set; }

    /// <summary>
    /// 是否处于活动期内
    /// </summary>
    [JsonPropertyName("is_active_period")]
    public bool IsActivePeriod { get; set; }

    /// <summary>
    /// 服务器当前星期，周一为 1
    /// </summary>
    [JsonPropertyName("current_weekday")]
    public int CurrentWeekday { get; set; }


    /// <summary>
    /// 本周进度已满
    /// </summary>
    [JsonIgnore]
    public bool IsWeekFinished => ProgressCurrent >= ProgressTotal;

}
