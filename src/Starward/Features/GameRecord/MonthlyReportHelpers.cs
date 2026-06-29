using Starward.Core.GameRecord;
using System;

namespace Starward.Features.GameRecord;

/// <summary>
/// 月报类页面（旅行札记 / 开拓月历 / 绳网月报）共用的服务器时区与按日聚合辅助方法。
/// </summary>
internal static class MonthlyReportHelpers
{

    /// <summary>
    /// 根据账号所在游戏服务器返回其相对 UTC 的时区偏移。
    /// </summary>
    /// <param name="role">当前游戏角色；为 null 时按国服（+8）处理。</param>
    /// <returns>服务器相对 UTC 的偏移。</returns>
    public static TimeSpan GetServerUtcOffset(GameRecordRole? role)
    {
        return role?.Region switch
        {
            "prod_gf_us" => TimeSpan.FromHours(-5),
            "prod_gf_eu" => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(8),
        };
    }


    /// <summary>
    /// 将 API/本地缓存中的服务器本地时间换算为日历日（1–31）。
    /// 存储的 <see cref="DateTime"/> 无时区信息，语义为游戏服务器本地时刻，不得先当作 UTC 再偏移。
    /// </summary>
    /// <param name="time">明细记录时间（服务器本地）。</param>
    /// <param name="serverOffset">服务器相对 UTC 的偏移。</param>
    /// <returns>服务器本地日历日。</returns>
    public static int GetServerLocalDay(DateTime time, TimeSpan serverOffset)
    {
        var localTime = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(localTime, serverOffset).Day;
    }


    /// <summary>
    /// 将 Unix 时间戳转换后的 <see cref="DateTimeOffset"/> 换算为服务器本地日历日。
    /// </summary>
    /// <param name="time">明细记录时间（通常为 UTC 时间戳）。</param>
    /// <param name="serverOffset">服务器相对 UTC 的偏移。</param>
    /// <returns>服务器本地日历日。</returns>
    public static int GetServerLocalDay(DateTimeOffset time, TimeSpan serverOffset)
    {
        return time.ToOffset(serverOffset).Day;
    }

}