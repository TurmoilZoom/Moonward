using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Starward.Features.Gacha;

/// <summary>
/// 抽卡记录的版本时间表（原神、星铁、绝区零通用）：按抽取时间判断一条记录属于哪个版本。
/// </summary>
/// <remarks>
/// <para>版本数据只来自 Moonward-Assets 的远程版本表（<c>&lt;游戏&gt;/versions/versions.json</c>）：历史部分在资源仓库里整理并核对，
/// 新版本由该仓库的工作流每天从卡池日历追加。客户端拉取后以 <c>版本=Unix秒;…</c> 按游戏存在本地设置里，见 <see cref="SerializeEntries"/>。</para>
/// <para>本地还没有版本表（从未拉取成功）时不分组。晚于表中最后一个版本的记录归入最后一个版本。</para>
/// </remarks>
public static class GachaVersionTable
{

    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    /// <summary>版本号格式，如 3.4。</summary>
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+$");


    /// <summary>
    /// 由版本列表生成按开始时间升序的版本表，丢掉格式不对的版本号；同一版本出现多次时取最后一次。
    /// </summary>
    /// <param name="entries">版本列表（版本号, 开始时间），可为空。</param>
    /// <returns>版本表；没有可用版本时为空列表。</returns>
    public static List<(string Version, DateTimeOffset Start)> GetTable(IEnumerable<(string Version, DateTimeOffset Start)>? entries)
    {
        var map = new Dictionary<string, DateTimeOffset>();
        foreach (var item in entries ?? [])
        {
            if (VersionPattern.IsMatch(item.Version))
            {
                map[item.Version] = item.Start;
            }
        }
        var table = map.Select(x => (Version: x.Key, Start: x.Value)).ToList();
        table.Sort((a, b) => a.Start.CompareTo(b.Start));
        return table;
    }


    /// <summary>
    /// 按抽取时间判断所属版本。
    /// </summary>
    /// <param name="serverTime">记录时间（服务器当地时间，即库里存的值）。</param>
    /// <param name="serverUtcOffset">该服务器相对 UTC 的偏移，见 <see cref="GetServerUtcOffset"/>。</param>
    /// <param name="table"><see cref="GetTable"/> 的结果。</param>
    /// <returns>版本号；早于表中第一个版本返回 null。</returns>
    public static string? GetVersion(DateTime serverTime, TimeSpan serverUtcOffset, IReadOnlyList<(string Version, DateTimeOffset Start)> table)
    {
        var time = new DateTimeOffset(DateTime.SpecifyKind(serverTime, DateTimeKind.Unspecified), serverUtcOffset);
        string? version = null;
        foreach (var (v, start) in table)
        {
            if (time < start)
            {
                break;
            }
            version = v;
        }
        return version;
    }


    /// <summary>
    /// 按游戏与 UID 推断记录所用的服务器时区（库里存的是服务器当地时间）。
    /// <list type="bullet">
    /// <item>绝区零（nap）：国服 8 位 UID 为 UTC+8；国际服 10 位 UID 前两位 10 为美服（UTC-5）、15 为欧服（UTC+1），其余（13 亚服、17 港澳台）为 UTC+8。</item>
    /// <item>原神（hk4e）、星铁（hkrpg）：9 位 UID 首位 6 为美服（UTC-5）、7 为欧服（UTC+1），其余（国服 1/2/5、8 亚服、9 港澳台）及 10 位 UID 为 UTC+8。</item>
    /// </list>
    /// </summary>
    /// <param name="game">游戏键（GameBiz.Game：hk4e / hkrpg / nap）。</param>
    /// <param name="uid">玩家 UID。</param>
    /// <returns>相对 UTC 的偏移。</returns>
    public static TimeSpan GetServerUtcOffset(string game, long uid)
    {
        if (game == "nap")
        {
            if (uid < 1_000_000_000)
            {
                return ChinaOffset;
            }
            return (uid / 100_000_000) switch
            {
                10 => TimeSpan.FromHours(-5),
                15 => TimeSpan.FromHours(1),
                _ => ChinaOffset,
            };
        }
        if (uid is >= 100_000_000 and < 1_000_000_000)
        {
            return (uid / 100_000_000) switch
            {
                6 => TimeSpan.FromHours(-5),
                7 => TimeSpan.FromHours(1),
                _ => ChinaOffset,
            };
        }
        return ChinaOffset;
    }


    /// <summary>
    /// 解析本地保存的版本表，格式为 <c>版本=Unix秒;版本=Unix秒</c>。不认识的片段忽略。
    /// </summary>
    /// <param name="text">保存的文本，可为空。</param>
    /// <returns>版本列表。</returns>
    public static List<(string Version, DateTimeOffset Start)> ParseEntries(string? text)
    {
        var result = new List<(string, DateTimeOffset)>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && long.TryParse(part[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds) && seconds > 0)
            {
                result.Add((part[..eq], DateTimeOffset.FromUnixTimeSeconds(seconds)));
            }
        }
        return result;
    }


    /// <summary>
    /// 把版本列表存成 <c>版本=Unix秒;…</c>，与 <see cref="ParseEntries"/> 对应。
    /// </summary>
    /// <param name="entries">版本列表。</param>
    /// <returns>保存用的文本。</returns>
    public static string SerializeEntries(IEnumerable<(string Version, DateTimeOffset Start)> entries)
    {
        return string.Join(';', entries.Select(x => $"{x.Version}={x.Start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"));
    }

}
