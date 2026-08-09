using Starward.Core;
using Starward.Language;
using System;
using System.Globalization;

namespace Starward.Features.TimeNode;

/// <summary>
/// 百科时间解析、区服墙钟平移与倒计时文案。
/// </summary>
internal static class TimeNodeTimeHelper
{

    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);


    /// <summary>
    /// 按 GameBiz 与 UID 解析目标区服 UTC 偏移小时数（与 UIGF 一致：6→-5，7→+1，其余 +8）。
    /// 国服 / bilibili 固定 +8；global 无 UID 时回退 +8。
    /// </summary>
    /// <param name="gameBiz">当前游戏业务标识。</param>
    /// <param name="uid">用于推断国际服区服的 UID；可为 null。</param>
    /// <returns>小时偏移。</returns>
    public static int ResolveServerOffsetHours(GameBiz gameBiz, long? uid)
    {
        if (!gameBiz.IsGlobalServer())
        {
            return 8;
        }
        if (uid is null or <= 0)
        {
            return 8;
        }
        string s = uid.Value.ToString(CultureInfo.InvariantCulture);
        if (s.Length == 0)
        {
            return 8;
        }
        return s[0] switch
        {
            '6' => -5,
            '7' => 1,
            _ => 8,
        };
    }


    /// <summary>
    /// 将百科东八区墙钟字符串解析并平移到目标区服墙钟。
    /// </summary>
    /// <param name="chinaWallClock">百科 <c>yyyy-MM-dd HH:mm:ss</c>（东八语义）。</param>
    /// <param name="targetOffsetHours">目标区服偏移小时。</param>
    /// <returns>目标区服下的绝对时刻；解析失败返回 null。</returns>
    public static DateTimeOffset? ParseChinaWallClockToServer(string? chinaWallClock, int targetOffsetHours)
    {
        if (string.IsNullOrWhiteSpace(chinaWallClock))
        {
            return null;
        }
        // 兼容 iOS 风格时百科偶发的格式；失败则 TryParse
        string normalized = chinaWallClock.Trim().Replace('-', '/');
        if (!DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime wall)
            && !DateTime.TryParse(chinaWallClock, CultureInfo.InvariantCulture, DateTimeStyles.None, out wall)
            && !DateTime.TryParse(chinaWallClock, out wall))
        {
            return null;
        }
        // 取墙钟字段，挂到目标 offset（同一「本地时刻」在不同区服对应不同绝对时刻）
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeSpan.FromHours(targetOffsetHours));
    }


    /// <summary>
    /// 将毫秒 Unix 时间戳（绝对时刻）先视为东八墙钟，再平移到目标区服。
    /// </summary>
    /// <param name="msTimestamp">毫秒时间戳字符串。</param>
    /// <param name="targetOffsetHours">目标区服偏移小时。</param>
    /// <returns>目标区服下的绝对时刻；无效返回 null。</returns>
    public static DateTimeOffset? ParseUnixMsToServer(string? msTimestamp, int targetOffsetHours)
    {
        if (string.IsNullOrWhiteSpace(msTimestamp) || msTimestamp is "0")
        {
            return null;
        }
        if (!long.TryParse(msTimestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms) || ms <= 0)
        {
            return null;
        }
        DateTimeOffset absolute = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        // 先还原百科侧东八墙钟，再挂目标 offset
        DateTime chinaWall = absolute.ToOffset(ChinaOffset).DateTime;
        var unspecified = DateTime.SpecifyKind(chinaWall, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeSpan.FromHours(targetOffsetHours));
    }


    /// <summary>
    /// 根据起止时间与当前时刻生成展示文案。
    /// </summary>
    /// <param name="kind">倒计时粒度。</param>
    /// <param name="start">开始时刻；热点活动可为空。</param>
    /// <param name="end">结束时刻。</param>
    /// <param name="contentBeforeAct">未开始时的固定文案。</param>
    /// <param name="now">当前时刻（一般为 <see cref="DateTimeOffset.Now"/>）。</param>
    /// <returns>展示字符串。</returns>
    public static string FormatCountdown(
        TimeNodeCountdownKind kind,
        DateTimeOffset? start,
        DateTimeOffset end,
        string? contentBeforeAct,
        DateTimeOffset now)
    {
        if (start is not null && now < start.Value)
        {
            return string.IsNullOrWhiteSpace(contentBeforeAct)
                ? Lang.TimeNode_NotStarted
                : contentBeforeAct!;
        }
        if (now > end)
        {
            return Lang.TimeNode_Ended;
        }

        TimeSpan remain = end - now;
        if (remain < TimeSpan.Zero)
        {
            return Lang.TimeNode_Ended;
        }

        int totalSeconds = (int)Math.Floor(remain.TotalSeconds);
        return kind switch
        {
            TimeNodeCountdownKind.Coarse => FormatCoarse(totalSeconds),
            _ => FormatPrecise(totalSeconds),
        };
    }


    private static string FormatPrecise(int totalSeconds)
    {
        int s = totalSeconds % 60;
        int m = totalSeconds / 60 % 60;
        int h = totalSeconds / 3600 % 24;
        int d = totalSeconds / 86400;
        // 与官方一致：有天则带「X天」，小时/分/秒始终带数字
        if (d > 0)
        {
            return string.Format(Lang.TimeNode_RemainingPreciseWithDays, d, h, m, s);
        }
        return string.Format(Lang.TimeNode_RemainingPrecise, h, m, s);
    }


    private static string FormatCoarse(int totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return Lang.TimeNode_Ended;
        }
        if (totalSeconds < 60)
        {
            return string.Format(Lang.TimeNode_RemainingMinutes, 1);
        }
        int m = totalSeconds / 60 % 60;
        int h = totalSeconds / 3600 % 24;
        int d = totalSeconds / 86400;
        if (d > 0 || h > 0)
        {
            if (d > 0 && h > 0)
            {
                return string.Format(Lang.TimeNode_RemainingCoarse, d, h);
            }
            if (d > 0)
            {
                return string.Format(Lang.TimeNode_RemainingDays, d);
            }
            return string.Format(Lang.TimeNode_RemainingHours, h);
        }
        return string.Format(Lang.TimeNode_RemainingMinutes, m);
    }

}
