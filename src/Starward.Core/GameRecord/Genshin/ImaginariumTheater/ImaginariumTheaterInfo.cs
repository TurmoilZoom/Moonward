using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.Genshin.ImaginariumTheater;

public class ImaginariumTheaterInfo : IJsonOnDeserialized
{

    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Uid { get; set; }


    [JsonPropertyName("schedule_id")]
    public int ScheduleId { get; set; }


    [JsonPropertyName("start_time")]
    [JsonConverter(typeof(DateTimeStringJsonConverter))]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("end_time")]
    [JsonConverter(typeof(DateTimeStringJsonConverter))]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 难度
    /// </summary>
    [JsonPropertyName("difficulty_id")]
    public int DifficultyId { get; set; }

    /// <summary>
    /// 抵达最大轮数
    /// </summary>
    [JsonPropertyName("max_round_id")]
    public int MaxRoundId { get; set; }

    /// <summary>
    /// 纹章类型
    /// </summary>
    [JsonPropertyName("heraldry")]
    public int Heraldry { get; set; }

    /// <summary>
    /// 明星挑战星章数量
    /// </summary>
    [JsonPropertyName("medal_num")]
    public int MedalNum { get; set; }


    [JsonPropertyName("detail")]
    public ImaginariumTheaterDetail Detail { get; set; }


    [JsonPropertyName("stat")]
    public ImaginariumTheaterStat Stat { get; set; }


    [JsonPropertyName("schedule")]
    public ImaginariumTheaterSchedule Schedule { get; set; }


    [JsonPropertyName("has_data")]
    public bool HasData { get; set; }


    [JsonPropertyName("has_detail_data")]
    public bool HasDetailData { get; set; }

    /// <summary>
    /// 是否有战斗统计。米游社当期可能只有 Stat（难度/星章），Detail 仍为空。
    /// </summary>
    [JsonIgnore]
    public bool HasFightStatistic => Detail?.FightStatisic is { TotalUseTime: > 0 };

    /// <summary>
    /// 是否有各幕阵容详情。
    /// </summary>
    [JsonIgnore]
    public bool HasRoundsData => Detail?.RoundsData is { Count: > 0 };

    /// <summary>
    /// 是否已下发演出详情（战斗统计或幕次阵容）。
    /// </summary>
    [JsonIgnore]
    public bool HasDetailContent => HasFightStatistic || HasRoundsData;


    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }


    public void OnDeserialized()
    {
        if (Detail is not null)
        {
            Detail.RoundsData ??= [];
            Detail.BackupAvatars ??= [];
            if (Detail.FightStatisic is not null)
            {
                Detail.FightStatisic.ShortestAvatarList ??= [];
            }
        }
        if (Stat is not null)
        {
            Stat.GetMedalRoundList ??= [];
        }
    }

}
