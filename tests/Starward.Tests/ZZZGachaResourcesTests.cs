using Starward.Core;
using Starward.Core.GameRecord.ZZZ.GachaStat;
using Starward.Features.Gacha;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 抽卡页顶栏资源胶囊的换算：菲林与单色菲林共用、按 160 换一抽，限定 / 常驻 / 邦布各用各的券。
/// </summary>
public class ZZZGachaResourcesTests
{

    // 取自米游社抽卡记录活动页抓包（index 接口），只保留用到的字段
    private const string IndexJson = """
        {"retcode":0,"message":"OK","data":{"type_stats":[
        {"gacha_type":"GACHA_TYPE_CHARACTER_UP","total_count":"2751","new_s_max_times":40,"next_s_cur_times":50},
        {"gacha_type":"GACHA_TYPE_CHARACTER_RETURN","total_count":"20","new_s_max_times":80,"next_s_cur_times":10},
        {"gacha_type":"GACHA_TYPE_WEAPON_UP","total_count":"371","new_s_max_times":74,"next_s_cur_times":6},
        {"gacha_type":"GACHA_TYPE_WEAPON_RETURN","total_count":"0","new_s_max_times":80,"next_s_cur_times":0},
        {"gacha_type":"GACHA_TYPE_PERMANENT","total_count":"561","new_s_max_times":81,"next_s_cur_times":9},
        {"gacha_type":"GACHA_TYPE_BANGBOO","total_count":"1278","new_s_max_times":47,"next_s_cur_times":33}],
        "cur_tickets":[
        {"ticket_type":"GACHA_TICKET_TYPE_RECHARGE_MONOCHROME","ticket_cnt":1756},
        {"ticket_type":"GACHA_TICKET_TYPE_POLYCHROME","ticket_cnt":85},
        {"ticket_type":"GACHA_TICKET_TYPE_ENCRYPTED_MASTER_TAPE","ticket_cnt":31},
        {"ticket_type":"GACHA_TICKET_TYPE_MASTER_TAPE","ticket_cnt":0},
        {"ticket_type":"GACHA_TICKET_TYPE_BOOPON","ticket_cnt":1}]}}
        """;


    private static ZZZGachaStatIndex ParseIndex(string json) => JsonSerializer.Deserialize<miHoYoApiWrapper<ZZZGachaStatIndex>>(json)!.Data;


    [Fact]
    public void FromIndex_ConvertsFilmsAndTickets()
    {
        var resources = ZZZGachaResources.FromIndex(ParseIndex(IndexJson), DateTimeOffset.UnixEpoch)!;

        Assert.Equal(85, resources.Polychrome);
        Assert.Equal(1756, resources.Monochrome);
        // (85 + 1756) / 160 = 11（向下取整）
        Assert.Equal(11, resources.FilmPulls);
        Assert.Equal(42, resources.LimitedPulls);
        Assert.Equal(11, resources.StandardPulls);
        Assert.Equal(1, resources.BangbooPulls);
    }


    [Fact]
    public void FromIndex_OrdersPityAndSkipsUnusedChannels()
    {
        var resources = ZZZGachaResources.FromIndex(ParseIndex(IndexJson), DateTimeOffset.UnixEpoch)!;

        // 音擎回响没抽过，不显示；限定频段在前
        Assert.Equal(new[] { 2, 3, 102, 1, 5 }, resources.PityProgress.Select(x => x.GachaType));
        var exclusive = resources.PityProgress[0];
        Assert.Equal(50, exclusive.CurrentPity);
        Assert.Equal(40, exclusive.RemainingToHardPity);
        Assert.Equal(42, exclusive.AvailablePulls);
        Assert.True(exclusive.CanReachHardPity);
        // 音擎频段还差 74 抽，42 抽不够
        Assert.False(resources.PityProgress[1].CanReachHardPity);
        // 邦布频段只能用邦布券
        Assert.Equal(1, resources.PityProgress[4].AvailablePulls);
    }


    [Fact]
    public void FromIndex_ReturnsNullWithoutTickets()
    {
        const string json = """{"retcode":0,"message":"OK","data":{"type_stats":[],"cur_tickets":[]}}""";

        Assert.Null(ZZZGachaResources.FromIndex(ParseIndex(json), DateTimeOffset.UnixEpoch));
        Assert.Null(ZZZGachaResources.FromIndex(null, DateTimeOffset.UnixEpoch));
    }

}
