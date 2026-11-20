using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.GachaStat;

/// <summary>
/// 米游社 / HoYoLAB「抽卡记录」活动页（<c>event/nap_gacha_record/api/zzz/index</c>）的总览数据：
/// 各频段累计抽数、S 级数、当前垫抽与当前持有的抽卡资源。统计覆盖账号全部历史（含超出记录接口保留期的部分）。
/// </summary>
public class ZZZGachaStatIndex
{

    /// <summary>各频段统计。</summary>
    [JsonPropertyName("type_stats")]
    public List<ZZZGachaTypeStat> TypeStats { get; set; } = new();


    /// <summary>累计抽数（实测不含邦布频段）。</summary>
    [JsonPropertyName("total_gacha_count")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long TotalGachaCount { get; set; }


    /// <summary>累计 S 级数。</summary>
    [JsonPropertyName("total_s_count")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long TotalSCount { get; set; }


    /// <summary>当前持有的抽卡资源。</summary>
    [JsonPropertyName("cur_tickets")]
    public List<ZZZGachaTicket> CurrentTickets { get; set; } = new();


    /// <summary>当前游戏版本（如 3.3），历史版本查询不含此版本，由 cur_pool_stat 覆盖。</summary>
    [JsonPropertyName("cur_ver")]
    public string? CurrentVersion { get; set; }


    [JsonPropertyName("cur_head_icon_url")]
    public string? CurrentHeadIconUrl { get; set; }

}


/// <summary>
/// 单个频段的累计统计。
/// </summary>
public class ZZZGachaTypeStat
{

    /// <summary>频段枚举串，如 <c>GACHA_TYPE_CHARACTER_UP</c>，见 <see cref="ZZZGachaStatType"/>。</summary>
    [JsonPropertyName("gacha_type")]
    public string GachaType { get; set; } = "";


    /// <summary>该频段累计抽数。</summary>
    [JsonPropertyName("total_count")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long TotalCount { get; set; }


    [JsonPropertyName("up_count")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long UpCount { get; set; }


    /// <summary>平均多少抽出一个 UP S 级。</summary>
    [JsonPropertyName("avr_up_count")]
    [JsonConverter(typeof(LenientDoubleJsonConverter))]
    public double AverageUpCount { get; set; }


    /// <summary>有抽卡记录的版本（含未出 S 级的版本），新版本在前。</summary>
    [JsonPropertyName("has_data_vers")]
    public List<string> HasDataVersions { get; set; } = new();


    /// <summary>距离硬保底还差几抽。</summary>
    [JsonPropertyName("new_s_max_times")]
    public int RemainingToHardPity { get; set; }


    /// <summary>当前已垫抽数（距上一个 S 级）。</summary>
    [JsonPropertyName("next_s_cur_times")]
    public int CurrentPity { get; set; }


    [JsonPropertyName("s_count")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long SCount { get; set; }

}


/// <summary>
/// 当前持有的抽卡资源。
/// </summary>
public class ZZZGachaTicket
{

    /// <summary>资源类型，如 <c>GACHA_TICKET_TYPE_POLYCHROME</c>（菲林）、<c>GACHA_TICKET_TYPE_RECHARGE_MONOCHROME</c>（单色菲林）。</summary>
    [JsonPropertyName("ticket_type")]
    public string TicketType { get; set; } = "";


    [JsonPropertyName("ticket_cnt")]
    public long Count { get; set; }

}


/// <summary>
/// <c>cur_pool_stat</c> / <c>hist_pool_stat</c> 的响应数据。
/// </summary>
public class ZZZGachaPoolStatData
{

    /// <summary>仅 cur_pool_stat 返回。</summary>
    [JsonPropertyName("cur_ver")]
    public string? CurrentVersion { get; set; }


    /// <summary>当前版本按上下半拆分；历史版本每个版本一条，且只返回出过 S 级的版本。</summary>
    [JsonPropertyName("list")]
    public List<ZZZGachaPoolStat> List { get; set; } = new();

}


/// <summary>
/// 一个卡池（当前版本的上/下半）或一个历史版本的 S 级记录。
/// </summary>
public class ZZZGachaPoolStat
{

    [JsonPropertyName("gacha_type")]
    public string GachaType { get; set; } = "";


    /// <summary><c>GACHA_STATE_IN_PROGRESS</c> / <c>GACHA_STATE_ENDED</c>。</summary>
    [JsonPropertyName("gacha_state")]
    public string GachaState { get; set; } = "";


    /// <summary>卡池开始时间（Unix 秒）；历史版本与常驻/邦布频段为 0。</summary>
    [JsonPropertyName("start_ts")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long StartTimestamp { get; set; }


    /// <summary>卡池结束时间（Unix 秒）；历史版本与常驻/邦布频段为 0。</summary>
    [JsonPropertyName("end_ts")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long EndTimestamp { get; set; }


    [JsonPropertyName("version")]
    public string Version { get; set; } = "";


    /// <summary>当期 UP 物品；历史版本为空。</summary>
    [JsonPropertyName("cur_item_list")]
    public List<ZZZGachaPoolStatItem> UpItems { get; set; } = new();


    /// <summary>该卡池/版本内出的 S 级记录。</summary>
    [JsonPropertyName("cur_records")]
    public List<ZZZGachaPoolStatItem> Records { get; set; } = new();

}


/// <summary>
/// 一条 S 级记录（或当期 UP 物品，此时计数字段为 0）。
/// </summary>
public class ZZZGachaPoolStatItem
{

    [JsonPropertyName("item_id")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int ItemId { get; set; }


    [JsonPropertyName("name")]
    public string Name { get; set; } = "";


    [JsonPropertyName("icon")]
    public string? Icon { get; set; }


    /// <summary><c>ItemType_Avatar</c> / <c>ItemType_Weapon</c> / <c>ItemType_Bangboo</c>。</summary>
    [JsonPropertyName("item_type")]
    public string ItemType { get; set; } = "";


    /// <summary>实测只出现 <c>S</c>。</summary>
    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = "";


    [JsonPropertyName("is_up")]
    public bool IsUp { get; set; }


    /// <summary><c>UP_TAG_NEW</c> / <c>UP_TAG_NONE</c>。</summary>
    [JsonPropertyName("up_tag")]
    public string? UpTag { get; set; }


    /// <summary>出这个 S 级用了几抽（垫抽数，含本抽）。</summary>
    [JsonPropertyName("count")]
    [JsonConverter(typeof(LenientInt32JsonConverter))]
    public int Count { get; set; }


    /// <summary>抽取时间（Unix 秒）。</summary>
    [JsonPropertyName("record_time")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long RecordTime { get; set; }


    /// <summary>在该频段全部历史抽卡中的序号（从 1 开始）。</summary>
    [JsonPropertyName("sort")]
    [JsonConverter(typeof(LenientInt64JsonConverter))]
    public long Sort { get; set; }

}


/// <summary>
/// 官方统计的频段枚举串与 <c>ZZZGachaType</c> 整数值的映射。
/// </summary>
public static class ZZZGachaStatType
{

    /// <summary>
    /// 把 <c>GACHA_TYPE_*</c> 映射为 ZZZGachaType 整数值（1/2/3/5/102/103）。
    /// </summary>
    /// <param name="gachaType">接口返回的频段枚举串。</param>
    /// <returns>对应整数值；未知频段返回 0。</returns>
    public static int ToGachaType(string? gachaType) => gachaType switch
    {
        "GACHA_TYPE_PERMANENT" => 1,
        "GACHA_TYPE_CHARACTER_UP" => 2,
        "GACHA_TYPE_WEAPON_UP" => 3,
        "GACHA_TYPE_BANGBOO" => 5,
        "GACHA_TYPE_CHARACTER_RETURN" => 102,
        "GACHA_TYPE_WEAPON_RETURN" => 103,
        _ => 0,
    };

}


/// <summary>
/// 一次拉取的官方抽卡统计：总览 + 各频段全部 S 级记录（按 sort 升序、已去重）。
/// </summary>
public class ZZZOfficialGachaStat
{

    public ZZZGachaStatIndex Index { get; set; } = new();


    /// <summary>键为 ZZZGachaType 整数值。</summary>
    public Dictionary<int, List<ZZZGachaPoolStatItem>> SRecords { get; set; } = new();


    /// <summary>当前版本卡池（按频段），含上下半起止时间与 UP 物品。</summary>
    public Dictionary<int, List<ZZZGachaPoolStat>> CurrentPools { get; set; } = new();


    /// <summary>记录名称所用语言（国服固定 zh-cn）。</summary>
    public string Language { get; set; } = "zh-cn";

}
