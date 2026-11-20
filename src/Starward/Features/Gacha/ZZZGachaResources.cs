using Starward.Core.Gacha.ZZZ;
using Starward.Core.GameRecord.ZZZ.GachaStat;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.Gacha;

/// <summary>
/// 绝区零当前持有的抽卡资源与可抽数，由官方抽卡统计总览（<see cref="ZZZGachaStatIndex"/>）换算。
/// 纯计算，不依赖数据库与 WinUI，单元测试直接链接本文件。
/// </summary>
public sealed class ZZZGachaResources
{

    /// <summary>一抽所需的菲林数（菲林兑换加密母带 / 原装母带都是 160 换 1）。</summary>
    public const int PolychromePerPull = 160;


    /// <summary>菲林。</summary>
    public long Polychrome { get; set; }

    /// <summary>单色菲林（充值所得，可 1:1 兑换菲林）。</summary>
    public long Monochrome { get; set; }

    /// <summary>加密母带（独家、音擎频段及其重映 / 回响用）。</summary>
    public long EncryptedMasterTape { get; set; }

    /// <summary>原装母带（常驻频段用）。</summary>
    public long MasterTape { get; set; }

    /// <summary>邦布券（邦布频段用，菲林不能兑换）。</summary>
    public long Boopon { get; set; }


    /// <summary>菲林与单色菲林合计能换几张母带；两种母带共用这部分，不能重复计算。</summary>
    public long FilmPulls => (Math.Max(Polychrome, 0) + Math.Max(Monochrome, 0)) / PolychromePerPull;

    /// <summary>限定频段可抽数：加密母带 + 菲林可换的母带。</summary>
    public long LimitedPulls => FilmPulls + Math.Max(EncryptedMasterTape, 0);

    /// <summary>常驻频段可抽数：原装母带 + 菲林可换的母带。</summary>
    public long StandardPulls => FilmPulls + Math.Max(MasterTape, 0);

    /// <summary>邦布频段可抽数：邦布券。</summary>
    public long BangbooPulls => Math.Max(Boopon, 0);


    /// <summary>有抽卡记录的各频段保底进度，按独家、音擎、独家重映、音擎回响、常驻、邦布排序。</summary>
    public List<ZZZGachaPityProgress> PityProgress { get; set; } = new();


    /// <summary>拉取时间。</summary>
    public DateTimeOffset UpdateTime { get; set; }


    /// <summary>保底进度的展示顺序：限定频段在前。</summary>
    private static readonly int[] PityOrder =
    [
        ZZZGachaType.ExclusiveChannel,
        ZZZGachaType.WEngineChannel,
        ZZZGachaType.ExclusiveRescreening,
        ZZZGachaType.WEngineReverberation,
        ZZZGachaType.StandardChannel,
        ZZZGachaType.BangbooChannel,
    ];


    /// <summary>
    /// 由官方统计总览换算抽卡资源与各频段保底进度。
    /// </summary>
    /// <param name="index">官方统计总览。</param>
    /// <param name="updateTime">拉取时间。</param>
    /// <returns>换算结果；接口没有返回任何抽卡资源时返回 null（没有可显示的内容）。</returns>
    public static ZZZGachaResources? FromIndex(ZZZGachaStatIndex? index, DateTimeOffset updateTime)
    {
        if (index?.CurrentTickets is not { Count: > 0 } tickets)
        {
            return null;
        }
        var result = new ZZZGachaResources { UpdateTime = updateTime };
        foreach (var ticket in tickets)
        {
            switch (ticket.TicketType)
            {
                case "GACHA_TICKET_TYPE_POLYCHROME":
                    result.Polychrome = ticket.Count;
                    break;
                case "GACHA_TICKET_TYPE_RECHARGE_MONOCHROME":
                    result.Monochrome = ticket.Count;
                    break;
                case "GACHA_TICKET_TYPE_ENCRYPTED_MASTER_TAPE":
                    result.EncryptedMasterTape = ticket.Count;
                    break;
                case "GACHA_TICKET_TYPE_MASTER_TAPE":
                    result.MasterTape = ticket.Count;
                    break;
                case "GACHA_TICKET_TYPE_BOOPON":
                    result.Boopon = ticket.Count;
                    break;
            }
        }
        var stats = (index.TypeStats ?? new())
            .Select(x => (GachaType: ZZZGachaStatType.ToGachaType(x.GachaType), Stat: x))
            .Where(x => x.GachaType != 0 && x.Stat.TotalCount > 0)
            .GroupBy(x => x.GachaType)
            .ToDictionary(x => x.Key, x => x.First().Stat);
        foreach (int gachaType in PityOrder)
        {
            if (stats.TryGetValue(gachaType, out var stat))
            {
                result.PityProgress.Add(new ZZZGachaPityProgress
                {
                    GachaType = gachaType,
                    CurrentPity = Math.Max(stat.CurrentPity, 0),
                    RemainingToHardPity = Math.Max(stat.RemainingToHardPity, 0),
                    AvailablePulls = result.GetAvailablePulls(gachaType),
                });
            }
        }
        return result;
    }


    /// <summary>
    /// 指定频段按当前资源能抽几次。
    /// </summary>
    /// <param name="gachaType">ZZZGachaType 整数值。</param>
    /// <returns>可抽数；未知频段返回 0。</returns>
    public long GetAvailablePulls(int gachaType) => gachaType switch
    {
        ZZZGachaType.ExclusiveChannel or ZZZGachaType.WEngineChannel
            or ZZZGachaType.ExclusiveRescreening or ZZZGachaType.WEngineReverberation => LimitedPulls,
        ZZZGachaType.StandardChannel => StandardPulls,
        ZZZGachaType.BangbooChannel => BangbooPulls,
        _ => 0,
    };

}


/// <summary>
/// 单个频段的保底进度（来自官方统计，覆盖账号全部历史）。
/// </summary>
public sealed class ZZZGachaPityProgress
{

    /// <summary>ZZZGachaType 整数值。</summary>
    public int GachaType { get; set; }

    /// <summary>频段本地化名称。</summary>
    public string GachaTypeText => new ZZZGachaType(GachaType).ToLocalization();

    /// <summary>距上一个 S 级已垫的抽数。</summary>
    public int CurrentPity { get; set; }

    /// <summary>距硬保底还差几抽。</summary>
    public int RemainingToHardPity { get; set; }

    /// <summary>该频段按当前资源能抽几次。</summary>
    public long AvailablePulls { get; set; }

    /// <summary>当前资源够不够抽到硬保底。</summary>
    public bool CanReachHardPity => RemainingToHardPity > 0 && AvailablePulls >= RemainingToHardPity;

}
