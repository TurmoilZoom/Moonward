using Starward.Core;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using System;
using System.Collections.Generic;

namespace Starward.Features.Gacha;




public class GachaNoUp
{

    public GameBiz Game { get; set; }

    public int GachaType { get; set; }

    public Dictionary<int, GachaNoUpItem> Items { get; set; } = new();



    public static Dictionary<string, GachaNoUp> Dictionary { get; } = new();



    static GachaNoUp()
    {
        AddGachaNoUpGenshin();
        AddGachaNoUpStarRail();
        AddGachaNoUpZZZ();
    }



    private static void AddGachaNoUpGenshin()
    {
        GachaNoUp hk4e301 = new GachaNoUp { Game = GameBiz.hk4e, GachaType = GenshinGachaType.CharacterEventWish };
        hk4e301.Items.Add(10000003, new GachaNoUpItem
        {
            Id = 10000003,
            Name = "琴",
            NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
        });
        hk4e301.Items.Add(10000016, new GachaNoUpItem
        {
            Id = 10000016,
            Name = "迪卢克",
            NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
        });
        hk4e301.Items.Add(10000035, new GachaNoUpItem
        {
            Id = 10000035,
            Name = "七七",
            NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
        });
        hk4e301.Items.Add(10000041, new GachaNoUpItem
        {
            Id = 10000041,
            Name = "莫娜",
            NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
        });
        hk4e301.Items.Add(10000042, new GachaNoUpItem
        {
            Id = 10000042,
            Name = "刻晴",
            NoUpTimes =
            [
                (new DateTime(2020, 9, 1), new DateTime(2021, 2, 17, 17, 59, 59)),
                (new DateTime(2021, 3, 2, 16, 00, 00), DateTime.MaxValue),
            ],
        });
        hk4e301.Items.Add(10000069, new GachaNoUpItem
        {
            Id = 10000069,
            Name = "提纳里",
            NoUpTimes = [(new DateTime(2022, 9, 27, 18, 00, 00), DateTime.MaxValue)],
        });
        hk4e301.Items.Add(10000079, new GachaNoUpItem
        {
            Id = 10000079,
            Name = "迪希雅",
            NoUpTimes = [(new DateTime(2023, 4, 11, 18, 00, 00), DateTime.MaxValue)],
        });
        hk4e301.Items.Add(10000109, new GachaNoUpItem
        {
            Id = 10000109,
            Name = "梦见月瑞希",
            NoUpTimes = [(new DateTime(2025, 3, 25, 18, 00, 00), DateTime.MaxValue)],
        });
        Dictionary.Add("hk4e301", hk4e301);

        // 武器活动祈愿：常驻五星武器默认视为非 UP；历史上作为当期概率提升武器时按 UpTimes 例外处理。
        GachaNoUp hk4e302 = new GachaNoUp { Game = GameBiz.hk4e, GachaType = GenshinGachaType.WeaponEventWish };
        hk4e302.Items.Add(11501, CreateGenshinStandardWeapon(11501, "风鹰剑",
            (new DateTime(2020, 9, 28), new DateTime(2020, 10, 18)),
            (new DateTime(2021, 5, 18), new DateTime(2021, 6, 8))));
        hk4e302.Items.Add(11502, CreateGenshinStandardWeapon(11502, "天空之刃",
            (new DateTime(2021, 3, 17), new DateTime(2021, 4, 6)),
            (new DateTime(2021, 8, 10), new DateTime(2021, 8, 31))));
        hk4e302.Items.Add(12501, CreateGenshinStandardWeapon(12501, "天空之傲",
            (new DateTime(2021, 1, 12), new DateTime(2021, 2, 2)),
            (new DateTime(2021, 6, 9), new DateTime(2021, 6, 29))));
        hk4e302.Items.Add(12502, CreateGenshinStandardWeapon(12502, "狼的末路",
            (new DateTime(2020, 10, 20), new DateTime(2020, 11, 10)),
            (new DateTime(2021, 2, 23), new DateTime(2021, 3, 16))));
        hk4e302.Items.Add(13502, CreateGenshinStandardWeapon(13502, "天空之脊",
            (new DateTime(2021, 7, 21), new DateTime(2021, 8, 10))));
        hk4e302.Items.Add(13505, CreateGenshinStandardWeapon(13505, "和璞鸢",
            (new DateTime(2021, 2, 3), new DateTime(2021, 2, 23)),
            (new DateTime(2022, 1, 5), new DateTime(2022, 1, 25)),
            (new DateTime(2022, 5, 31), new DateTime(2022, 6, 21)),
            (new DateTime(2023, 1, 18), new DateTime(2023, 2, 7)),
            (new DateTime(2024, 2, 20), new DateTime(2024, 3, 12))));
        hk4e302.Items.Add(14501, CreateGenshinStandardWeapon(14501, "天空之卷",
            (new DateTime(2020, 12, 23), new DateTime(2021, 1, 12)),
            (new DateTime(2021, 6, 29), new DateTime(2021, 7, 20))));
        hk4e302.Items.Add(14502, CreateGenshinStandardWeapon(14502, "四风原典",
            (new DateTime(2020, 10, 20), new DateTime(2020, 11, 10)),
            (new DateTime(2021, 4, 6), new DateTime(2021, 4, 27)),
            (new DateTime(2021, 6, 9), new DateTime(2021, 6, 29)),
            (new DateTime(2022, 7, 13), new DateTime(2022, 8, 2)),
            (new DateTime(2023, 7, 5), new DateTime(2023, 7, 25))));
        hk4e302.Items.Add(15501, CreateGenshinStandardWeapon(15501, "天空之翼",
            (new DateTime(2020, 11, 11), new DateTime(2020, 12, 1)),
            (new DateTime(2021, 4, 6), new DateTime(2021, 4, 27)),
            (new DateTime(2021, 12, 14), new DateTime(2022, 1, 4))));
        hk4e302.Items.Add(15502, CreateGenshinStandardWeapon(15502, "阿莫斯之弓",
            (new DateTime(2020, 9, 28), new DateTime(2020, 10, 18)),
            (new DateTime(2021, 1, 12), new DateTime(2021, 2, 2)),
            (new DateTime(2022, 1, 25), new DateTime(2022, 2, 15)),
            (new DateTime(2022, 9, 9), new DateTime(2022, 9, 27)),
            (new DateTime(2023, 5, 2), new DateTime(2023, 5, 23))));
        Dictionary.Add("hk4e302", hk4e302);
    }


    /// <summary>
    /// 创建武器活动祈愿中的常驻五星武器配置，并将历史 UP 日期扩展为全天闭区间。
    /// </summary>
    /// <param name="id">武器物品 ID。</param>
    /// <param name="name">武器名称，仅用于配置可读性。</param>
    /// <param name="upTimes">该武器曾作为武器活动祈愿 UP 的起止日期（含首尾日期）。</param>
    /// <returns>默认非 UP、但在指定历史日期内为 UP 的武器配置。</returns>
    private static GachaNoUpItem CreateGenshinStandardWeapon(int id, string name, params (DateTime Start, DateTime End)[] upTimes)
    {
        List<(DateTime Start, DateTime End)> normalizedUpTimes = new(upTimes.Length);
        foreach ((DateTime start, DateTime end) in upTimes)
        {
            normalizedUpTimes.Add((start.Date, end.Date.AddDays(1).AddTicks(-1)));
        }
        return new GachaNoUpItem
        {
            Id = id,
            Name = name,
            NoUpTimes = [(new DateTime(2020, 9, 28), DateTime.MaxValue)],
            UpTimes = normalizedUpTimes,
        };
    }


    private static void AddGachaNoUpStarRail()
    {
        GachaNoUp hkrpg11 = new GachaNoUp { Game = GameBiz.hkrpg, GachaType = StarRailGachaType.CharacterEventWarp };
        hkrpg11.Items.Add(1003, new GachaNoUpItem
        {
            Id = 1003,
            Name = "姬子",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1004, new GachaNoUpItem
        {
            Id = 1004,
            Name = "瓦尔特",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1101, new GachaNoUpItem
        {
            Id = 1101,
            Name = "布洛妮娅",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1104, new GachaNoUpItem
        {
            Id = 1104,
            Name = "杰帕德",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1107, new GachaNoUpItem
        {
            Id = 1107,
            Name = "克拉拉",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1209, new GachaNoUpItem
        {
            Id = 1209,
            Name = "彦卿",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1211, new GachaNoUpItem
        {
            Id = 1211,
            Name = "白露",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        // 3.2版本，自定义非UP五星角色
        hkrpg11.Items.Add(1102, new GachaNoUpItem
        {
            Id = 1102,
            Name = "希儿",
            NoUpTimes = [(new DateTime(2025, 4, 8, 18, 00, 00), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1205, new GachaNoUpItem
        {
            Id = 1205,
            Name = "刃",
            NoUpTimes =
            [
                (new DateTime(2025, 4, 8, 18, 00, 00), new DateTime(2025, 7, 23, 11, 59, 59)),
                (new DateTime(2025, 8, 12, 15, 00, 00), DateTime.MaxValue),
            ],
        });
        hkrpg11.Items.Add(1208, new GachaNoUpItem
        {
            Id = 1208,
            Name = "符玄",
            NoUpTimes = [(new DateTime(2025, 4, 8, 18, 00, 00), DateTime.MaxValue)],
        });
        // 4.2版本，自定义非UP五星角色
        hkrpg11.Items.Add(1006, new GachaNoUpItem
        {
            Id = 1006,
            Name = "银狼",
            NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1221, new GachaNoUpItem
        {
            Id = 1221,
            Name = "云璃",
            NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
        });
        hkrpg11.Items.Add(1302, new GachaNoUpItem
        {
            Id = 1302,
            Name = "银枝",
            NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
        });
        Dictionary.Add("hkrpg11", hkrpg11);

        GachaNoUp hkrpg12 = new GachaNoUp { Game = GameBiz.hkrpg, GachaType = StarRailGachaType.LightConeEventWarp };
        hkrpg12.Items.Add(23000, new GachaNoUpItem
        {
            Id = 23000,
            Name = "银河铁道之夜",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg12.Items.Add(23002, new GachaNoUpItem
        {
            Id = 23002,
            Name = "无可取代的东西",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg12.Items.Add(23003, new GachaNoUpItem
        {
            Id = 23003,
            Name = "但战斗还未结束",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg12.Items.Add(23004, new GachaNoUpItem
        {
            Id = 23004,
            Name = "以世界之名",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg12.Items.Add(23005, new GachaNoUpItem
        {
            Id = 23005,
            Name = "制胜的瞬间",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg12.Items.Add(23012, new GachaNoUpItem
        {
            Id = 23012,
            Name = "如泥酣眠",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        hkrpg12.Items.Add(23013, new GachaNoUpItem
        {
            Id = 23013,
            Name = "时节不居",
            NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
        });
        Dictionary.Add("hkrpg12", hkrpg12);

        GachaNoUp hkrpg21 = new GachaNoUp { Game = GameBiz.hkrpg, GachaType = StarRailGachaType.CharacterCollaborationWarp };
        hkrpg21.Items.Add(1003, new GachaNoUpItem
        {
            Id = 1003,
            Name = "姬子",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1004, new GachaNoUpItem
        {
            Id = 1004,
            Name = "瓦尔特",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1101, new GachaNoUpItem
        {
            Id = 1101,
            Name = "布洛妮娅",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1104, new GachaNoUpItem
        {
            Id = 1104,
            Name = "杰帕德",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1107, new GachaNoUpItem
        {
            Id = 1107,
            Name = "克拉拉",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1209, new GachaNoUpItem
        {
            Id = 1209,
            Name = "彦卿",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1211, new GachaNoUpItem
        {
            Id = 1211,
            Name = "白露",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        // 3.2版本，自定义非UP五星角色
        hkrpg21.Items.Add(1102, new GachaNoUpItem
        {
            Id = 1102,
            Name = "希儿",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1205, new GachaNoUpItem
        {
            Id = 1205,
            Name = "刃",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1208, new GachaNoUpItem
        {
            Id = 1208,
            Name = "符玄",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        // 4.2版本，自定义非UP五星角色
        hkrpg21.Items.Add(1006, new GachaNoUpItem
        {
            Id = 1006,
            Name = "银狼",
            NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1221, new GachaNoUpItem
        {
            Id = 1221,
            Name = "云璃",
            NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
        });
        hkrpg21.Items.Add(1302, new GachaNoUpItem
        {
            Id = 1302,
            Name = "银枝",
            NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
        });
        Dictionary.Add("hkrpg21", hkrpg21);

        GachaNoUp hkrpg22 = new GachaNoUp { Game = GameBiz.hkrpg, GachaType = StarRailGachaType.LightConeCollaborationWarp };
        hkrpg22.Items.Add(23000, new GachaNoUpItem
        {
            Id = 23000,
            Name = "银河铁道之夜",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg22.Items.Add(23002, new GachaNoUpItem
        {
            Id = 23002,
            Name = "无可取代的东西",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg22.Items.Add(23003, new GachaNoUpItem
        {
            Id = 23003,
            Name = "但战斗还未结束",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg22.Items.Add(23004, new GachaNoUpItem
        {
            Id = 23004,
            Name = "以世界之名",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg22.Items.Add(23005, new GachaNoUpItem
        {
            Id = 23005,
            Name = "制胜的瞬间",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg22.Items.Add(23012, new GachaNoUpItem
        {
            Id = 23012,
            Name = "如泥酣眠",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        hkrpg22.Items.Add(23013, new GachaNoUpItem
        {
            Id = 23013,
            Name = "时节不居",
            NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
        });
        Dictionary.Add("hkrpg22", hkrpg22);
    }


    private static void AddGachaNoUpZZZ()
    {
        GachaNoUp nap2 = new GachaNoUp { Game = GameBiz.nap, GachaType = ZZZGachaType.ExclusiveChannel };
        nap2.Items.Add(1021, new GachaNoUpItem
        {
            Id = 1021,
            Name = "猫又",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap2.Items.Add(1041, new GachaNoUpItem
        {
            Id = 1041,
            Name = "「11号」",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap2.Items.Add(1101, new GachaNoUpItem
        {
            Id = 1101,
            Name = "珂蕾妲",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap2.Items.Add(1141, new GachaNoUpItem
        {
            Id = 1141,
            Name = "莱卡恩",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap2.Items.Add(1181, new GachaNoUpItem
        {
            Id = 1181,
            Name = "格莉丝",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap2.Items.Add(1211, new GachaNoUpItem
        {
            Id = 1211,
            Name = "丽娜",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        // 3.0 下半起，部分曾限定代理人可在独家频段/独家重映作为非 UP 歪出
        nap2.Items.Add(1071, new GachaNoUpItem
        {
            Id = 1071,
            Name = "凯撒",
            NoUpTimes = [(new DateTime(2026, 7, 29), DateTime.MaxValue)],
        });
        nap2.Items.Add(1221, new GachaNoUpItem
        {
            Id = 1221,
            Name = "柳",
            NoUpTimes = [(new DateTime(2026, 7, 29), DateTime.MaxValue)],
        });
        nap2.Items.Add(1241, new GachaNoUpItem
        {
            Id = 1241,
            Name = "朱鸢",
            NoUpTimes = [(new DateTime(2026, 7, 29), DateTime.MaxValue)],
        });
        Dictionary.Add("nap2", nap2);
        Dictionary.Add("nap102", nap2);

        GachaNoUp nap3 = new GachaNoUp { Game = GameBiz.nap, GachaType = ZZZGachaType.WEngineChannel };
        nap3.Items.Add(14102, new GachaNoUpItem
        {
            Id = 14102,
            Name = "钢铁肉垫",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap3.Items.Add(14104, new GachaNoUpItem
        {
            Id = 14104,
            Name = "硫磺石",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap3.Items.Add(14110, new GachaNoUpItem
        {
            Id = 14110,
            Name = "燃狱齿轮",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap3.Items.Add(14114, new GachaNoUpItem
        {
            Id = 14114,
            Name = "拘缚者",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap3.Items.Add(14118, new GachaNoUpItem
        {
            Id = 14118,
            Name = "嵌合编译器",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        nap3.Items.Add(14121, new GachaNoUpItem
        {
            Id = 14121,
            Name = "啜泣摇篮",
            NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
        });
        // 与上列代理人配套的专属音擎：音擎频段 / 音擎回响
        nap3.Items.Add(14107, new GachaNoUpItem
        {
            Id = 14107,
            Name = "奔袭獠牙",
            NoUpTimes = [(new DateTime(2026, 7, 29), DateTime.MaxValue)],
        });
        nap3.Items.Add(14122, new GachaNoUpItem
        {
            Id = 14122,
            Name = "时流贤者",
            NoUpTimes = [(new DateTime(2026, 7, 29), DateTime.MaxValue)],
        });
        nap3.Items.Add(14124, new GachaNoUpItem
        {
            Id = 14124,
            Name = "防暴者Ⅵ型",
            NoUpTimes = [(new DateTime(2026, 7, 29), DateTime.MaxValue)],
        });
        Dictionary.Add("nap3", nap3);
        Dictionary.Add("nap103", nap3);
    }


}



public class GachaNoUpItem
{

    public int Id { get; set; }

    public string Name { get; set; }

    public List<(DateTime Start, DateTime End)> NoUpTimes { get; set; } = [];

    /// <summary>
    /// 物品虽属于常驻池，但历史上作为当期 UP 的时间区间。
    /// 此列表优先于 <see cref="NoUpTimes"/>，用于原神武器活动祈愿中的常驻五星武器。
    /// </summary>
    public List<(DateTime Start, DateTime End)> UpTimes { get; set; } = [];


    /// <summary>
    /// 判断该物品在指定时间是否为当期 UP。
    /// </summary>
    /// <param name="time">抽卡记录时间，使用记录自身的服务器时区语义。</param>
    /// <returns>落在 UP 例外区间或不在任何非 UP 区间时返回 true，否则返回 false。</returns>
    public bool IsUpAt(DateTime time)
    {
        foreach ((DateTime start, DateTime end) in UpTimes)
        {
            if (time >= start && time <= end)
            {
                return true;
            }
        }
        foreach ((DateTime start, DateTime end) in NoUpTimes)
        {
            if (time >= start && time <= end)
            {
                return false;
            }
        }
        return true;
    }

}
