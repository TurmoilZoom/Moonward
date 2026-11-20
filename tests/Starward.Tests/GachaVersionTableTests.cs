using Starward.Core.Gacha;
using Starward.Features.Gacha;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 抽卡版本时间表：版本数据只来自远程版本表，按抽取时间归版本、各游戏服务器时区、本地保存格式。
/// </summary>
public class GachaVersionTableTests
{

    private static readonly TimeSpan Cst = TimeSpan.FromHours(8);


    /// <summary>取自 Moonward-Assets zzz/versions/versions.json 的一部分。</summary>
    private static (string, DateTimeOffset)[] RemoteEntries() =>
    [
        ("1.0", new DateTimeOffset(2024, 7, 4, 6, 0, 0, Cst)),
        ("2.0", new DateTimeOffset(2025, 6, 6, 6, 0, 0, Cst)),
        ("2.5", new DateTimeOffset(2025, 12, 30, 6, 0, 0, Cst)),
        ("2.6", new DateTimeOffset(2026, 2, 6, 6, 0, 0, Cst)),
        ("3.1", new DateTimeOffset(2026, 7, 29, 6, 0, 0, Cst)),
        ("3.2", new DateTimeOffset(2026, 9, 9, 6, 0, 0, Cst)),
        ("3.3", new DateTimeOffset(2026, 10, 21, 6, 0, 0, Cst)),
        ("3.4", new DateTimeOffset(2026, 12, 2, 9, 0, 0, Cst)),
    ];


    [Theory]
    // 取自官方抽卡统计里带版本号的 S 级记录（北京时间）
    [InlineData("2026-09-09 11:21:00", "3.2")]
    [InlineData("2026-09-01 12:10:00", "3.1")]
    [InlineData("2026-02-05 21:30:00", "2.5")]
    [InlineData("2026-02-06 20:38:00", "2.6")]
    [InlineData("2025-06-06 11:50:00", "2.0")]
    [InlineData("2026-10-21 12:20:00", "3.3")]
    // 以表中开始时间为界
    [InlineData("2026-09-09 05:59:59", "3.1")]
    [InlineData("2026-09-09 06:00:00", "3.2")]
    [InlineData("2026-12-02 08:59:59", "3.3")]
    [InlineData("2026-12-02 09:00:00", "3.4")]
    [InlineData("2024-07-04 12:00:00", "1.0")]
    // 晚于最后一个已知版本时归入最后一个
    [InlineData("2027-03-01 12:00:00", "3.4")]
    public void GetVersion_ChinaServer(string time, string expected)
    {
        var table = GachaVersionTable.GetTable(RemoteEntries());
        Assert.Equal(expected, GachaVersionTable.GetVersion(DateTime.Parse(time), Cst, table));
    }


    [Fact]
    public void GetVersion_ReturnsNullBeforeFirstVersion()
    {
        var table = GachaVersionTable.GetTable(RemoteEntries());
        Assert.Null(GachaVersionTable.GetVersion(new DateTime(2024, 6, 30), Cst, table));
    }


    [Fact]
    public void GetTable_EmptyWithoutRemoteData()
    {
        // 从未拉取成功时没有版本表，调用方据此不分组
        Assert.Empty(GachaVersionTable.GetTable(null));
        Assert.Empty(GachaVersionTable.GetTable(GachaVersionTable.ParseEntries(null)));
        Assert.Null(GachaVersionTable.GetVersion(new DateTime(2026, 1, 1), Cst, GachaVersionTable.GetTable(null)));
    }


    [Fact]
    public void GetTable_SortsByStartAndDropsBadVersions()
    {
        var entries = new[]
        {
            ("3.3", new DateTimeOffset(2026, 10, 21, 6, 0, 0, Cst)),
            ("abc", new DateTimeOffset(2026, 11, 1, 0, 0, 0, Cst)),
            ("3.2", new DateTimeOffset(2026, 9, 9, 6, 0, 0, Cst)),
            ("3.3", new DateTimeOffset(2026, 10, 21, 9, 0, 0, Cst)),
        };
        var table = GachaVersionTable.GetTable(entries);
        Assert.Equal(["3.2", "3.3"], table.Select(x => x.Version).ToArray());
        // 同一版本出现多次时取最后一次
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 9, 0, 0, Cst), table[1].Start);
    }


    [Fact]
    public void GetVersion_ConvertsServerTimeZone()
    {
        // 美服记录存 UTC-5：3.2 的分界 2026-09-09 06:00（UTC+8）是当地 09-08 17:00
        var table = GachaVersionTable.GetTable(RemoteEntries());
        var us = TimeSpan.FromHours(-5);
        Assert.Equal("3.1", GachaVersionTable.GetVersion(new DateTime(2026, 9, 8, 16, 30, 0), us, table));
        Assert.Equal("3.2", GachaVersionTable.GetVersion(new DateTime(2026, 9, 8, 17, 30, 0), us, table));
    }


    [Theory]
    // 绝区零：国服 8 位；国际服 10 位，前两位 10 美服、15 欧服
    [InlineData("nap", 12003500, 8)]
    [InlineData("nap", 1317719560, 8)]
    [InlineData("nap", 1700507197, 8)]
    [InlineData("nap", 1012345678, -5)]
    [InlineData("nap", 1512345678, 1)]
    // 原神 / 星铁：9 位，首位 6 美服、7 欧服，国服与亚服、港澳台为 UTC+8
    [InlineData("hk4e", 188346199, 8)]
    [InlineData("hk4e", 612345678, -5)]
    [InlineData("hk4e", 712345678, 1)]
    [InlineData("hk4e", 812345678, 8)]
    [InlineData("hk4e", 1824395750, 8)]
    [InlineData("hkrpg", 101289527, 8)]
    [InlineData("hkrpg", 600123456, -5)]
    [InlineData("hkrpg", 700123456, 1)]
    [InlineData("hkrpg", 900123456, 8)]
    public void GetServerUtcOffset_ByGameAndUid(string game, long uid, int hours)
    {
        Assert.Equal(TimeSpan.FromHours(hours), GachaVersionTable.GetServerUtcOffset(game, uid));
    }


    [Fact]
    public void Entries_SerializeRoundTrip()
    {
        var entries = RemoteEntries();
        string text = GachaVersionTable.SerializeEntries(entries);
        Assert.Equal(entries, GachaVersionTable.ParseEntries(text).ToArray());
    }


    [Fact]
    public void ParseEntries_SkipsMalformedParts()
    {
        var parsed = GachaVersionTable.ParseEntries("3.4=1796176800;broken;3.5=abc;=123");
        var item = Assert.Single(parsed);
        Assert.Equal("3.4", item.Version);
    }


    [Fact]
    public void RemoteSchedule_ParsesAssetsFile()
    {
        // Moonward-Assets 的 zzz/versions/versions.json 格式：历史条目无 source，自动追加的带 ennead
        const string json = """
            {
              "versions": [
                { "version": "3.3", "start": "2026-10-21T06:00:00+08:00" },
                { "version": "3.4", "start": "2026-12-02T09:00:00+08:00", "source": "ennead" }
              ]
            }
            """;
        var schedule = JsonSerializer.Deserialize<GameVersionSchedule>(json)!;
        Assert.Equal(2, schedule.Versions.Count);
        Assert.Null(schedule.Versions[0].Source);
        Assert.Equal("ennead", schedule.Versions[1].Source);
        Assert.Equal(new DateTimeOffset(2026, 12, 2, 1, 0, 0, TimeSpan.Zero), schedule.Versions[1].Start);
    }

}
