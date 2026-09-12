using System;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 一条自动更新异常记录：某个账号的某个数据板块在某次自动更新中失败了，只作展示用，不参与重试决策。
/// <para>
/// 按「异常之后直接跳过」的设计，后台不做任何自愈；用户在异常记录里看到这条后，
/// 自行决定是重新登录、去官方页面过验证，还是不管它。
/// </para>
/// </summary>
public class RecordRefreshError
{

    /// <summary>出错角色所属的游戏业务线，如 hk4e_cn。</summary>
    public string GameBiz { get; set; } = string.Empty;

    /// <summary>出错角色的 uid。</summary>
    public long Uid { get; set; }

    /// <summary>出错时该角色的昵称，仅用于展示（角色被删除后仍能看出是谁）。</summary>
    public string? Nickname { get; set; }

    /// <summary>出错的数据板块。</summary>
    public RecordRefreshItem Item { get; set; }

    /// <summary>出错的是该板块的哪个月份任务。非月报板块恒为当月。</summary>
    public RecordRefreshMonthTarget MonthTarget { get; set; }

    /// <summary>出错时刻（UTC）。</summary>
    public DateTimeOffset Time { get; set; }

    /// <summary>米哈游接口返回码；非接口错误（断网等）为 0。</summary>
    public int ReturnCode { get; set; }

    /// <summary>服务端原文或异常消息，不做本地化。</summary>
    public string? Message { get; set; }

}
