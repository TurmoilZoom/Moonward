using Starward.Core;
using Starward.Core.GameRecord.ZZZ.GachaStat;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 绝区零官方抽卡统计响应的反序列化：数值写成字符串，没抽过的频段 avr_up_count 为空串。
/// </summary>
public class ZZZGachaStatJsonTests
{

    [Fact]
    public void Index_ToleratesEmptyAverageForUnusedChannels()
    {
        // 取自只抽过两个频段的小号：其余频段 avr_up_count 为 ""
        const string json = """
            {"retcode":0,"message":"OK","data":{"type_stats":[
            {"gacha_type":"GACHA_TYPE_CHARACTER_UP","total_count":"36","up_count":"0","avr_up_count":"0","has_data_vers":["3.3"],"new_s_max_times":54,"next_s_cur_times":36,"s_count":"0"},
            {"gacha_type":"GACHA_TYPE_CHARACTER_RETURN","total_count":"0","up_count":"0","avr_up_count":"","has_data_vers":[],"new_s_max_times":90,"next_s_cur_times":0,"s_count":"0"},
            {"gacha_type":"GACHA_TYPE_PERMANENT","total_count":"41","up_count":"0","avr_up_count":"79.4","has_data_vers":["3.2"],"new_s_max_times":54,"next_s_cur_times":36,"s_count":"1"}],
            "total_gacha_count":"77","total_s_count":"1","cur_tickets":[{"ticket_type":"GACHA_TICKET_TYPE_POLYCHROME","ticket_cnt":925}],"cur_ver":"3.3"}}
            """;
        var index = JsonSerializer.Deserialize<miHoYoApiWrapper<ZZZGachaStatIndex>>(json)!.Data;

        Assert.Equal(3, index.TypeStats.Count);
        Assert.Equal(0, index.TypeStats[1].AverageUpCount);
        Assert.Equal(79.4, index.TypeStats[2].AverageUpCount);
        Assert.Equal(41, index.TypeStats[2].TotalCount);
        Assert.Equal(77, index.TotalGachaCount);
        Assert.Equal(925, index.CurrentTickets.Single().Count);
    }


    [Fact]
    public void PoolStat_ReadsStringNumbers()
    {
        const string json = """
            {"retcode":0,"message":"OK","data":{"list":[{"gacha_type":"GACHA_TYPE_CHARACTER_UP","gacha_state":"GACHA_STATE_ENDED","start_ts":0,"end_ts":0,"version":"3.2","cur_item_list":[],
            "cur_records":[{"item_id":"1541","name":"x","icon":"","item_type":"ItemType_Avatar","rarity":"S","is_up":true,"up_tag":"UP_TAG_NONE","count":"33","record_time":"1790741272","sort":"2621"}]}]}}
            """;
        var record = JsonSerializer.Deserialize<miHoYoApiWrapper<ZZZGachaPoolStatData>>(json)!.Data.List.Single().Records.Single();

        Assert.Equal(1541, record.ItemId);
        Assert.Equal(33, record.Count);
        Assert.Equal(1790741272, record.RecordTime);
        Assert.Equal(2621, record.Sort);
    }


    [Fact]
    public void Index_ThrowsWhenNumberIsGarbage()
    {
        // 只容忍「没有值」：有值却不是数字说明接口格式变了，应报错而不是静默读成 0
        const string json = """{"retcode":0,"message":"OK","data":{"type_stats":[{"gacha_type":"GACHA_TYPE_PERMANENT","total_count":"abc"}]}}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<miHoYoApiWrapper<ZZZGachaStatIndex>>(json));
    }

}
