using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Starward.Features.RedeemCode;

/// <summary>
/// 前瞻直播兑换码的系统通知方式，按游戏设置。**枚举名存进 Setting 表，不能改名**；取值即兑换码弹层通知菜单项的 Tag。
/// </summary>
public enum RedeemCodeNotifyMode
{
    /// <summary>不通知。</summary>
    Off,

    /// <summary>逐条：每次检查发现新码就发一条，只列这次的新码；前一条不被替换，没复制的码还留在通知中心。</summary>
    EachCode,

    /// <summary>合并：放出的码达到预设数量（<see cref="Core.MiyoLive.MiyoLiveGameConfig.ExpectedCodeCount"/>）再发一条，列出本期全部码。</summary>
    Merged,
}


/// <summary>
/// 前瞻直播所处的阶段，按 index 给的开播 / 结束时间判断。
/// </summary>
internal enum RedeemLivePhase
{
    /// <summary>没有开播时间，无法判断。</summary>
    Unknown,

    /// <summary>还没到开播时间。</summary>
    Upcoming,

    /// <summary>开播到结束后一小段时间（<see cref="RedeemCodeNotifySchedule.LiveTail"/>）之内，兑换码随时可能放出。</summary>
    Live,

    /// <summary>已经结束。</summary>
    Ended,
}


/// <summary>
/// 兑换码通知的纯计算：直播时间解析、所处阶段、下次检查时刻、哪些码还没通知过。不依赖数据库与 WinUI，单测直接链接本文件。
/// </summary>
internal static class RedeemCodeNotifySchedule
{

    /// <summary>不在直播期间时多久查一次有没有新的直播活动。</summary>
    public static readonly TimeSpan DiscoveryInterval = TimeSpan.FromHours(3);

    /// <summary>直播期间多久查一次兑换码。码按 <c>to_get_time</c> 逐个放出，事先不知道时刻。</summary>
    public static readonly TimeSpan LivePollInterval = TimeSpan.FromMinutes(1);

    /// <summary>结束时间之后再查多久：官方接口可能比直播晚几分钟放码。</summary>
    public static readonly TimeSpan LiveTail = TimeSpan.FromMinutes(30);

    /// <summary>index 没给结束时间时，按开播后这么久算作结束。</summary>
    public static readonly TimeSpan DefaultLiveDuration = TimeSpan.FromHours(3);

    /// <summary>
    /// 直播结束后多久内的码还会通知（直播时 Moonward 没开，之后启动时补发）。
    /// 接口不给有效期，直播码通常只能兑换很短一段时间，再往后补发多半已经失效。
    /// </summary>
    public static readonly TimeSpan NotifyFreshWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// 每次排期额外随机延后的上限：各客户端不在同一秒（尤其是开播整点）一起请求，也不形成固定的整分钟节拍。
    /// </summary>
    public static readonly TimeSpan MaxJitter = TimeSpan.FromSeconds(10);

    /// <summary>官方时间串是东八区墙钟，中国不实行夏令时，固定偏移即可。</summary>
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    /// <summary>通知记录里 act_id 与码之间的分隔符。</summary>
    private const char NotifiedActSeparator = '|';

    /// <summary>通知记录里码与码之间的分隔符。</summary>
    private const char NotifiedCodeSeparator = ',';


    /// <summary>
    /// 把官方的东八区墙钟字符串（常见 <c>yyyy-MM-dd HH:mm:ss</c>）解析为带偏移的时刻。
    /// </summary>
    /// <param name="text">index 的 <c>start</c> / <c>end</c>。</param>
    /// <returns>解析结果；空串或格式不对时为 null。</returns>
    public static DateTimeOffset? ParseChinaTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime local))
        {
            return null;
        }
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ChinaOffset);
    }


    /// <summary>
    /// 直播算作结束的时刻：有结束时间用结束时间，否则开播后 <see cref="DefaultLiveDuration"/>。
    /// </summary>
    /// <param name="start">开播时间。</param>
    /// <param name="end">结束时间。</param>
    /// <returns>结束时刻；两者都没有时为 null。</returns>
    public static DateTimeOffset? GetFinish(DateTimeOffset? start, DateTimeOffset? end) => end ?? (start + DefaultLiveDuration);


    /// <summary>
    /// 判断直播所处的阶段。
    /// </summary>
    /// <param name="start">开播时间。</param>
    /// <param name="end">结束时间。</param>
    /// <param name="now">当前时刻。</param>
    /// <returns>阶段；没有开播时间时为 <see cref="RedeemLivePhase.Unknown"/>。</returns>
    public static RedeemLivePhase GetPhase(DateTimeOffset? start, DateTimeOffset? end, DateTimeOffset now)
    {
        if (start is not { } s)
        {
            return RedeemLivePhase.Unknown;
        }
        if (now < s)
        {
            return RedeemLivePhase.Upcoming;
        }
        DateTimeOffset finish = GetFinish(start, end)!.Value;
        return now <= finish + LiveTail ? RedeemLivePhase.Live : RedeemLivePhase.Ended;
    }


    /// <summary>
    /// 算下次检查时刻：直播期间按 <see cref="LivePollInterval"/> 轮询；还没开播就睡到开播（最多 <see cref="DiscoveryInterval"/>，期间换了新一期也能发现）；
    /// 其余按 <see cref="DiscoveryInterval"/>。
    /// </summary>
    /// <param name="phase">当前阶段。</param>
    /// <param name="start">开播时间。</param>
    /// <param name="now">当前时刻。</param>
    /// <returns>下次检查时刻。</returns>
    public static DateTimeOffset GetNextCheck(RedeemLivePhase phase, DateTimeOffset? start, DateTimeOffset now)
    {
        DateTimeOffset idle = now + DiscoveryInterval;
        return phase switch
        {
            RedeemLivePhase.Live => now + LivePollInterval,
            RedeemLivePhase.Upcoming when start is { } s && s < idle => s,
            _ => idle,
        };
    }


    /// <summary>
    /// 给排好的检查时刻随机延后 0 ~ <see cref="MaxJitter"/>。
    /// </summary>
    /// <param name="next">排好的检查时刻。</param>
    /// <param name="sample">[0, 1] 之间的随机数（调用方传 <see cref="Random.NextDouble"/>，单测传定值）；越界时截到边界。</param>
    /// <returns>延后后的时刻。</returns>
    public static DateTimeOffset AddJitter(DateTimeOffset next, double sample)
    {
        return next + MaxJitter * Math.Clamp(sample, 0, 1);
    }


    /// <summary>
    /// 这一期的直播是否结束不久（含还没开播、正在直播）：结束后 <paramref name="window"/> 之内为 true。
    /// </summary>
    /// <param name="start">开播时间。</param>
    /// <param name="end">结束时间。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="window">结束后仍算「不久」的时长。</param>
    /// <returns>没有任何时间信息时为 false，宁可不通知也不去通知往期的码。</returns>
    public static bool IsRecent(DateTimeOffset? start, DateTimeOffset? end, DateTimeOffset now, TimeSpan window)
    {
        return GetFinish(start, end) is { } finish && now <= finish + window;
    }


    /// <summary>
    /// 本次检查是否该发通知。逐条模式有新码就发；合并模式下码在直播中分几次放出，直播期间等放出的码达到预设数量再合成一条发，
    /// 直播窗口过了（含直播时没开 Moonward、之后才启动）还没达到也照发，免得预设数字不准时永远不通知；达到之后又多出新码（预设偏少）时照常再发。
    /// </summary>
    /// <param name="mode">通知方式。</param>
    /// <param name="phase">当前阶段。</param>
    /// <param name="releasedCount">本期目前已放出的码数（含已通知过的）。</param>
    /// <param name="expectedCount">本期预计的码数（<see cref="Core.MiyoLive.MiyoLiveGameConfig.ExpectedCodeCount"/>），小于 1 时按 1。</param>
    /// <param name="newCount">还没通知过的码数。</param>
    /// <returns>该发时为 true。</returns>
    public static bool ShouldNotify(RedeemCodeNotifyMode mode, RedeemLivePhase phase, int releasedCount, int expectedCount, int newCount)
    {
        if (mode is RedeemCodeNotifyMode.Off || newCount <= 0)
        {
            return false;
        }
        if (mode is RedeemCodeNotifyMode.EachCode)
        {
            return true;
        }
        if (phase is RedeemLivePhase.Live or RedeemLivePhase.Upcoming)
        {
            return releasedCount >= Math.Max(1, expectedCount);
        }
        // 已结束，或没有开播时间判断不了何时结束：不再等
        return true;
    }


    /// <summary>
    /// 找出本期还没通知过的码，保持接口给的顺序。
    /// </summary>
    /// <param name="actId">本期 act_id。</param>
    /// <param name="codes">接口当前返回的码。</param>
    /// <param name="notified">已通知记录（<see cref="ParseNotified"/> 的结果）；act_id 不同说明是往期记录，本期的码全部算新。</param>
    /// <returns>新码；没有时为空列表。</returns>
    public static List<string> GetNewCodes(string actId, IEnumerable<string> codes, (string? ActId, IReadOnlyList<string> Codes) notified)
    {
        IEnumerable<string> distinct = codes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(notified.ActId, actId, StringComparison.Ordinal))
        {
            return distinct.ToList();
        }
        return distinct.Where(x => !notified.Codes.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
    }


    /// <summary>
    /// 把已通知记录序列化成 <c>{act_id}|{code},{code}</c>。
    /// </summary>
    /// <param name="actId">本期 act_id。</param>
    /// <param name="codes">本期已通知的全部码。</param>
    /// <returns>存进 Setting 的字符串。</returns>
    public static string SerializeNotified(string actId, IEnumerable<string> codes)
    {
        return $"{actId}{NotifiedActSeparator}{string.Join(NotifiedCodeSeparator, codes)}";
    }


    /// <summary>
    /// 解析 <see cref="SerializeNotified"/> 写下的记录。
    /// </summary>
    /// <param name="value">Setting 里的字符串。</param>
    /// <returns>act_id 与码列表；空串或格式不对时 act_id 为 null、列表为空。</returns>
    public static (string? ActId, IReadOnlyList<string> Codes) ParseNotified(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, []);
        }
        int index = value.IndexOf(NotifiedActSeparator);
        if (index <= 0)
        {
            return (null, []);
        }
        string actId = value[..index];
        string[] codes = value[(index + 1)..].Split(NotifiedCodeSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (actId, codes);
    }

}
