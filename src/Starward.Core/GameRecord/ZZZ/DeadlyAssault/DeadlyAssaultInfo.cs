using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.DeadlyAssault;

/// <summary>
/// 危局强袭战
/// </summary>
public class DeadlyAssaultInfo
{

    [JsonIgnore]
    public int Uid { get; set; }


    [JsonPropertyName("zone_id")]
    public int ZoneId { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    [JsonPropertyName("start_time")]
    [JsonConverter(typeof(DateTimeObjectJsonConverter))]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    [JsonPropertyName("end_time")]
    [JsonConverter(typeof(DateTimeObjectJsonConverter))]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 当前排名，以0.01%为单位
    /// </summary>
    [JsonPropertyName("rank_percent")]
    public int RankPercent { get; set; }

    [JsonPropertyName("list")]
    public List<DeadlyAssaultNode> AllNodes { get; set; }


    [JsonPropertyName("has_data")]
    public bool HasData { get; set; }

    /// <summary>
    /// 昵称
    /// </summary>
    [JsonPropertyName("nick_name")]
    public string NickName { get; set; }

    /// <summary>
    /// 头像
    /// </summary>
    [JsonPropertyName("avatar_icon")]
    public string AvatarIcon { get; set; }

    /// <summary>
    /// 总分
    /// </summary>
    [JsonPropertyName("total_score")]
    public int TotalScore { get; set; }

    /// <summary>
    /// 总星数
    /// </summary>
    [JsonPropertyName("total_star")]
    public int TotalStar { get; set; }

    /// <summary>
    /// 本期满分
    /// </summary>
    [JsonPropertyName("total_max_score")]
    public int TotalMaxScore { get; set; }

    /// <summary>
    /// 单节点满分
    /// </summary>
    [JsonPropertyName("room_max_score")]
    public int RoomMaxScore { get; set; }

    /// <summary>
    /// 是否有绝境挑战
    /// </summary>
    [JsonPropertyName("has_hard")]
    public bool HasHard { get; set; }

    /// <summary>
    /// 绝境挑战节点
    /// </summary>
    [JsonPropertyName("hard_list")]
    public List<DeadlyAssaultNode> HardList { get; set; }

    /// <summary>
    /// 绝境全服排名，以0.01%为单位
    /// </summary>
    [JsonPropertyName("hard_rank_percent")]
    public int HardRankPercent { get; set; }


    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }


}
