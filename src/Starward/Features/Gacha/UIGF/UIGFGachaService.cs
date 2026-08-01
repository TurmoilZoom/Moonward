using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using Starward.Core.Localization;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace Starward.Features.Gacha.UIGF;

internal class UIGFGachaService
{

    private readonly ILogger<UIGFGachaService> _logger;


    public UIGFGachaService(ILogger<UIGFGachaService> logger)
    {
        _logger = logger;
    }




    #region Export


    /// <summary>
    /// 枚举本地可导出的抽卡归档。
    /// </summary>
    /// <param name="version">目标 UIGF 子版本；v4.0/v4.1 不含千星奇域，v4.2 含 hk4e_ugc。</param>
    public List<GachaUidArchiveDisplay> GetLocalGachaArchives(UIGF4Version version = UIGF4Version.V40)
    {
        List<GachaUidArchiveDisplay> result =
        [
            .. GetLocalGachaArchivesForGenshin(),
            .. GetLocalGachaArchivesForStarRail(),
            .. GetLocalGachaArchivesForZZZ(),
        ];
        if (version.SupportsHk4eUgc())
        {
            result.AddRange(GetLocalGachaArchivesForGenshinBeyond());
        }
        return result;
    }



    private List<GachaUidArchiveDisplay> GetLocalGachaArchivesForGenshin()
    {
        using var dapper = DatabaseService.CreateConnection();
        List<GachaUidArchiveDisplay> result = new();
        var uidList = dapper.Query<long>($"SELECT DISTINCT Uid FROM GenshinGachaItem;");
        foreach (long uid in uidList)
        {
            int count = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM GenshinGachaItem WHERE Uid=@Uid;", new { Uid = uid });
            GachaLogItem lastItem = dapper.QueryFirst<GachaLogItem>($"SELECT * FROM GenshinGachaItem WHERE Uid=@Uid ORDER BY Time DESC LIMIT 1;", new { Uid = uid });
            var display = new GachaUidArchiveDisplay
            {
                Game = GameBiz.hk4e,
                GameIcon = "ms-appx:///Assets/Image/icon_ys.jpg",
                Uid = uid,
                Count = count,
                LastItemGachaType = ((GenshinGachaType)lastItem.GachaType).ToLocalization(),
                LastItemName = lastItem.Name,
                LastItemTime = lastItem.Time
            };
            result.Add(display);
        }
        return result;
    }


    private List<GachaUidArchiveDisplay> GetLocalGachaArchivesForStarRail()
    {
        using var dapper = DatabaseService.CreateConnection();
        List<GachaUidArchiveDisplay> result = new();
        var uidList = dapper.Query<long>($"SELECT DISTINCT Uid FROM StarRailGachaItem;");
        foreach (long uid in uidList)
        {
            int count = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM StarRailGachaItem WHERE Uid=@Uid;", new { Uid = uid });
            GachaLogItem lastItem = dapper.QueryFirst<GachaLogItem>($"SELECT * FROM StarRailGachaItem WHERE Uid=@Uid ORDER BY Time DESC LIMIT 1;", new { Uid = uid });
            var display = new GachaUidArchiveDisplay
            {
                Game = GameBiz.hkrpg,
                GameIcon = "ms-appx:///Assets/Image/icon_sr.jpg",
                Uid = uid,
                Count = count,
                LastItemGachaType = ((StarRailGachaType)lastItem.GachaType).ToLocalization(),
                LastItemName = lastItem.Name,
                LastItemTime = lastItem.Time
            };
            result.Add(display);
        }
        return result;
    }


    private List<GachaUidArchiveDisplay> GetLocalGachaArchivesForZZZ()
    {
        using var dapper = DatabaseService.CreateConnection();
        List<GachaUidArchiveDisplay> result = new();
        var uidList = dapper.Query<long>($"SELECT DISTINCT Uid FROM ZZZGachaItem;");
        foreach (long uid in uidList)
        {
            int count = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM ZZZGachaItem WHERE Uid=@Uid;", new { Uid = uid });
            GachaLogItem lastItem = dapper.QueryFirst<GachaLogItem>($"SELECT * FROM ZZZGachaItem WHERE Uid=@Uid ORDER BY Time DESC LIMIT 1;", new { Uid = uid });
            var display = new GachaUidArchiveDisplay
            {
                Game = GameBiz.nap,
                GameIcon = "ms-appx:///Assets/Image/icon_zzz.jpg",
                Uid = uid,
                Count = count,
                LastItemGachaType = ((ZZZGachaType)lastItem.GachaType).ToLocalization(),
                LastItemName = lastItem.Name,
                LastItemTime = lastItem.Time
            };
            result.Add(display);
        }
        return result;
    }


    /// <summary>枚举本地千星奇域（GenshinBeyondGachaItem）归档，内部 GameBiz 使用 hk4eugc。</summary>
    private List<GachaUidArchiveDisplay> GetLocalGachaArchivesForGenshinBeyond()
    {
        using var dapper = DatabaseService.CreateConnection();
        List<GachaUidArchiveDisplay> result = new();
        var uidList = dapper.Query<long>($"SELECT DISTINCT Uid FROM GenshinBeyondGachaItem;");
        foreach (long uid in uidList)
        {
            int count = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM GenshinBeyondGachaItem WHERE Uid=@Uid;", new { Uid = uid });
            GenshinBeyondGachaItem lastItem = dapper.QueryFirst<GenshinBeyondGachaItem>($"SELECT * FROM GenshinBeyondGachaItem WHERE Uid=@Uid ORDER BY Time DESC LIMIT 1;", new { Uid = uid });
            var display = new GachaUidArchiveDisplay
            {
                Game = "hk4eugc",
                GameIcon = "ms-appx:///Assets/Image/icon_ys.jpg",
                Uid = uid,
                Count = count,
                LastItemGachaType = LocalizeBeyondOpGachaType(lastItem.OpGachaType),
                LastItemName = lastItem.ItemName,
                LastItemTime = lastItem.Time
            };
            result.Add(display);
        }
        return result;
    }


    /// <summary>千星奇域 op_gacha_type 本地化显示。</summary>
    private static string LocalizeBeyondOpGachaType(int opGachaType) => opGachaType switch
    {
        1000 => CoreLang.GachaType_StandardOde,
        2000 => CoreLang.GachaType_EventOde,
        _ => opGachaType.ToString(),
    };



    /// <summary>
    /// 按指定 UIGF 子版本导出。
    /// v4.0 / v4.1 不写入 <c>hk4e_ugc</c>；v4.2 可包含千星奇域。
    /// 星铁联动池（21/22）在本地有数据时三种版本均原样写出（避免丢记录）；版本号按 <paramref name="version"/> 标注。
    /// </summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="version">导出版本。</param>
    /// <param name="archives">用户勾选的归档。</param>
    public async Task ExportUIGF4Async(string path, UIGF4Version version, params IEnumerable<GachaUidArchiveDisplay> archives)
    {
        var uigfObj = new UIGF4File();
        uigfObj.Info.Version = version.ToVersionString();
        bool includeBeyond = version.SupportsHk4eUgc();
        if (includeBeyond)
        {
            uigfObj.hk4eUgcGachaArchives = [];
        }

        foreach (GachaUidArchiveDisplay archive in archives)
        {
            if (archive.Game == GameBiz.hk4e)
            {
                uigfObj.hk4eGachaArchives!.Add(GetUIGFGachaArchiveForGenshin(archive.Uid));
            }
            if (includeBeyond && archive.Game.Value == "hk4eugc")
            {
                uigfObj.hk4eUgcGachaArchives!.Add(GetUIGFGachaArchiveForGenshinBeyond(archive.Uid));
            }
            if (archive.Game == GameBiz.hkrpg)
            {
                uigfObj.hkrpgGachaArchives!.Add(GetUIGFGachaArchiveForStarRail(archive.Uid));
            }
            if (archive.Game == GameBiz.nap)
            {
                uigfObj.napGachaArchives!.Add(GetUIGFGachaArchiveForZZZ(archive.Uid));
            }
        }
        using FileStream fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, uigfObj, AppConfig.JsonSerializerOptions);
    }



    private UIGF4GachaArchive<UIGFGenshinGachaItem> GetUIGFGachaArchiveForGenshin(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        IEnumerable<UIGFGenshinGachaItem> list = dapper.Query<UIGFGenshinGachaItem>($"SELECT * FROM GenshinGachaItem WHERE Uid=@Uid ORDER BY Id;", new { Uid = uid });
        foreach (UIGFGenshinGachaItem item in list)
        {
            item.UIGFGachaType = item.GachaType switch
            {
                400 => 301,
                _ => item.GachaType,
            };
        }
        UIGF4GachaArchive<UIGFGenshinGachaItem> archive = new()
        {
            Uid = uid,
            List = list.ToList(),
            Lang = ResolveUigfExportLang(list.LastOrDefault()?.Lang),
        };
        archive.Timezone = uid.ToString()[0] switch
        {
            '6' => -5,
            '7' => 1,
            _ => 8,
        };
        return archive;
    }



    private UIGF4GachaArchive<StarRailGachaItem> GetUIGFGachaArchiveForStarRail(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        IEnumerable<StarRailGachaItem> list = dapper.Query<StarRailGachaItem>($"SELECT * FROM StarRailGachaItem WHERE Uid=@Uid ORDER BY Id;", new { Uid = uid });
        UIGF4GachaArchive<StarRailGachaItem> archive = new()
        {
            Uid = uid,
            List = list.ToList(),
            Lang = ResolveUigfExportLang(list.LastOrDefault()?.Lang),
        };
        archive.Timezone = uid.ToString()[0] switch
        {
            '6' => -5,
            '7' => 1,
            _ => 8,
        };
        return archive;
    }



    private UIGF4GachaArchive<ZZZGachaItem> GetUIGFGachaArchiveForZZZ(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        IEnumerable<ZZZGachaItem> list = dapper.Query<ZZZGachaItem>($"SELECT * FROM ZZZGachaItem WHERE Uid=@Uid ORDER BY Id;", new { Uid = uid });
        UIGF4GachaArchive<ZZZGachaItem> archive = new()
        {
            Uid = uid,
            List = list.ToList(),
            Lang = ResolveUigfExportLang(list.LastOrDefault()?.Lang),
        };
        // nap 归档同样需要 timezone（UIGF schema required）
        archive.Timezone = uid.ToString()[0] switch
        {
            '6' => -5,
            '7' => 1,
            _ => 8,
        };
        return archive;
    }


    /// <summary>
    /// 导出千星奇域归档（UIGF hk4e_ugc）。
    /// time 为库中服务器当地时间；timezone 按 UID 推断。
    /// 条目本身无 lang，优先同 UID 原神抽卡记录，否则用当前 UI 语言，保证符合 UIGF enum。
    /// </summary>
    private UIGF4BeyondGachaArchive GetUIGFGachaArchiveForGenshinBeyond(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        IEnumerable<GenshinBeyondGachaItem> list = dapper.Query<GenshinBeyondGachaItem>($"SELECT * FROM GenshinBeyondGachaItem WHERE Uid=@Uid ORDER BY Id;", new { Uid = uid });
        // 千星奇域表无 Lang 列，借同 UID 原神记录或 UI 语言补齐
        string? langHint = dapper.QueryFirstOrDefault<string>(
            "SELECT Lang FROM GenshinGachaItem WHERE Uid=@Uid AND IFNULL(Lang,'') != '' ORDER BY Id DESC LIMIT 1;",
            new { Uid = uid });
        UIGF4BeyondGachaArchive archive = new()
        {
            Uid = uid,
            List = list.ToList(),
            Lang = ResolveUigfExportLang(langHint),
        };
        archive.Timezone = uid.ToString()[0] switch
        {
            '6' => -5,
            '7' => 1,
            _ => 8,
        };
        return archive;
    }


    /// <summary>
    /// 规范化 UIGF 归档级 <c>lang</c>：必须为 schema enum 合法值（如 zh-cn），禁止空字符串。
    /// </summary>
    private static string ResolveUigfExportLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            lang = CultureInfo.CurrentUICulture.Name;
        }
        return LanguageUtil.FilterLanguage(lang);
    }


    #endregion




    #region Import



    /// <summary>
    /// 解析 UIGF / SRGF JSON 为待导入归档列表。
    /// 按文件结构自动识别：UIGF v3.0、SRGF、UIGF v4.0 / v4.1 / v4.2；
    /// 不依赖用户选择格式（版本字段仅作辅助，缺省或错误也不影响解析）。
    /// </summary>
    /// <exception cref="InvalidDataException">文件无法识别为 UIGF/SRGF，或无可导入记录。</exception>
    /// <exception cref="IOException">文件无法读取。</exception>
    public async Task<List<GachaUidArchiveDisplay>> ImportFileAsync(string path)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Read UIGF file failed");
            throw new IOException(Lang.UIGFGachaService_FileAccessFailed, ex);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Parse UIGF file failed");
            throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile, ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile);
            }

            // 优先识别 v4 多游戏根结构；否则尝试单游戏 v3 / SRGF（顶层 list）
            if (IsUigf4Root(root))
            {
                return ParseUigf4FromJson(json);
            }

            if (IsUigf3Root(root))
            {
                return ParseUigf3FromJson(json, root);
            }

            throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile);
        }
    }


    /// <summary>UIGF v4：根上存在 <c>hk4e</c> / <c>hkrpg</c> / <c>nap</c> / <c>hk4e_ugc</c> 任一数组字段。</summary>
    private static bool IsUigf4Root(JsonElement root) =>
        IsJsonArrayProperty(root, "hk4e")
        || IsJsonArrayProperty(root, "hkrpg")
        || IsJsonArrayProperty(root, "nap")
        || IsJsonArrayProperty(root, "hk4e_ugc");


    /// <summary>UIGF v3 / SRGF：根上存在 <c>list</c> 数组。</summary>
    private static bool IsUigf3Root(JsonElement root) => IsJsonArrayProperty(root, "list");


    private static bool IsJsonArrayProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement prop) && prop.ValueKind == JsonValueKind.Array;


    /// <summary>反序列化 UIGF v4.x 并展开为归档列表。</summary>
    private List<GachaUidArchiveDisplay> ParseUigf4FromJson(string json)
    {
        UIGF4File? uigf4Obj;
        try
        {
            uigf4Obj = JsonSerializer.Deserialize<UIGF4File>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Parse UIGF v4 file failed");
            throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile, ex);
        }

        if (uigf4Obj is null)
        {
            throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile);
        }

        List<GachaUidArchiveDisplay> list = new();
        foreach (UIGF4GachaArchive<UIGFGenshinGachaItem> item in uigf4Obj.hk4eGachaArchives ?? [])
        {
            if (item.List.Count > 0)
            {
                UIGFGenshinGachaItem last = item.List.OrderBy(x => x.Id).Last();
                GachaUidArchiveDisplay archive = new()
                {
                    Game = GameBiz.hk4e,
                    GameIcon = "ms-appx:///Assets/Image/icon_ys.jpg",
                    Uid = item.Uid,
                    hke4List = item.List,
                    Count = item.List.Count,
                    LastItemGachaType = ((GenshinGachaType)last.GachaType).ToLocalization(),
                    LastItemName = last.Name,
                    LastItemTime = last.Time,
                    LastItemTimeOffest = last.Time,
                };
                list.Add(archive);
            }
        }
        foreach (UIGF4GachaArchive<StarRailGachaItem> item in uigf4Obj.hkrpgGachaArchives ?? [])
        {
            if (item.List.Count > 0)
            {
                StarRailGachaItem last = item.List.OrderBy(x => x.Id).Last();
                GachaUidArchiveDisplay archive = new()
                {
                    Game = GameBiz.hkrpg,
                    GameIcon = "ms-appx:///Assets/Image/icon_sr.jpg",
                    Uid = item.Uid,
                    hkrpgList = item.List,
                    Count = item.List.Count,
                    LastItemGachaType = ((StarRailGachaType)last.GachaType).ToLocalization(),
                    LastItemName = last.Name,
                    LastItemTime = last.Time,
                    LastItemTimeOffest = last.Time,
                };
                list.Add(archive);
            }
        }
        foreach (UIGF4GachaArchive<ZZZGachaItem> item in uigf4Obj.napGachaArchives ?? [])
        {
            if (item.List.Count > 0)
            {
                ZZZGachaItem last = item.List.OrderBy(x => x.Id).Last();
                GachaUidArchiveDisplay archive = new()
                {
                    Game = GameBiz.nap,
                    GameIcon = "ms-appx:///Assets/Image/icon_zzz.jpg",
                    Uid = item.Uid,
                    napList = item.List,
                    Count = item.List.Count,
                    LastItemGachaType = ((ZZZGachaType)last.GachaType).ToLocalization(),
                    LastItemName = last.Name,
                    LastItemTime = last.Time,
                    LastItemTimeOffest = last.Time,
                };
                list.Add(archive);
            }
        }
        foreach (UIGF4BeyondGachaArchive item in uigf4Obj.hk4eUgcGachaArchives ?? [])
        {
            if (item.List is { Count: > 0 })
            {
                GenshinBeyondGachaItem last = item.List.OrderBy(x => x.Id).Last();
                GachaUidArchiveDisplay archive = new()
                {
                    Game = "hk4eugc",
                    GameIcon = "ms-appx:///Assets/Image/icon_ys.jpg",
                    Uid = item.Uid,
                    hk4eUgcList = item.List,
                    Count = item.List.Count,
                    LastItemGachaType = LocalizeBeyondOpGachaType(last.OpGachaType),
                    LastItemName = last.ItemName,
                    LastItemTime = last.Time,
                    LastItemTimeOffest = last.Time,
                };
                list.Add(archive);
            }
        }

        if (list.Count == 0)
        {
            throw new InvalidDataException(Lang.UIGFGachaService_NoGachaArchivesInFile);
        }
        return list;
    }


    /// <summary>
    /// 解析单游戏 UIGF v3 / SRGF：按条目字段与 gacha_type 推断游戏，再反序列化为一条归档。
    /// Timezone 保持 0（v3 时间为当地时间，与旧版 ImportGachaLog 一致，不做时区加减）。
    /// </summary>
    private List<GachaUidArchiveDisplay> ParseUigf3FromJson(string json, JsonElement root)
    {
        if (!root.TryGetProperty("list", out JsonElement listEl) || listEl.GetArrayLength() == 0)
        {
            throw new InvalidDataException(Lang.UIGFGachaService_NoGachaArchivesInFile);
        }

        GameBiz game = DetectUigf3Game(root, listEl);
        try
        {
            if (game == GameBiz.hk4e)
            {
                return BuildUigf3GenshinArchives(json);
            }
            if (game == GameBiz.hkrpg)
            {
                return BuildUigf3StarRailArchives(json);
            }
            if (game == GameBiz.nap)
            {
                return BuildUigf3ZZZArchives(json);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Parse UIGF v3/SRGF file failed for {game}", game);
            throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile, ex);
        }

        throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile);
    }


    /// <summary>
    /// 从 v3/SRGF 根对象推断所属游戏。
    /// 优先看条目特有字段（<c>gacha_id</c> / <c>uigf_gacha_type</c>），再看 gacha_type 取值区间。
    /// </summary>
    private static GameBiz DetectUigf3Game(JsonElement root, JsonElement listEl)
    {
        bool hasGachaId = false;
        bool hasUigfGachaType = false;
        bool hasGenshinType = false;
        bool hasStarRailUniqueType = false;
        bool hasZzzUniqueType = false;

        foreach (JsonElement item in listEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (item.TryGetProperty("gacha_id", out _))
            {
                hasGachaId = true;
            }
            if (item.TryGetProperty("uigf_gacha_type", out _))
            {
                hasUigfGachaType = true;
            }
            if (TryReadIntProperty(item, "gacha_type", out int gachaType))
            {
                if (gachaType is 100 or 200 or 301 or 302 or 400 or 500)
                {
                    hasGenshinType = true;
                }
                if (gachaType is 11 or 12 or 21 or 22)
                {
                    hasStarRailUniqueType = true;
                }
                if (gachaType is 3 or 5 or 102 or 103)
                {
                    hasZzzUniqueType = true;
                }
            }
        }

        // 星铁 SRGF：官方条目必有 gacha_id；或存在星铁专属卡池类型
        if (hasGachaId || hasStarRailUniqueType)
        {
            return GameBiz.hkrpg;
        }
        // 原神 UIGF v3：uigf_gacha_type 或原神卡池类型
        if (hasUigfGachaType || hasGenshinType)
        {
            return GameBiz.hk4e;
        }
        // 绝区零：音擎/邦布/重映等专属类型
        if (hasZzzUniqueType)
        {
            return GameBiz.nap;
        }

        // 仅有 1/2 等歧义类型时：看 info 是否只声明了 srgf_version（无 uigf_version）
        if (root.TryGetProperty("info", out JsonElement info) && info.ValueKind == JsonValueKind.Object)
        {
            bool hasSrgf = info.TryGetProperty("srgf_version", out _);
            bool hasUigf = info.TryGetProperty("uigf_version", out _);
            if (hasSrgf && !hasUigf)
            {
                return GameBiz.hkrpg;
            }
            if (hasUigf && !hasSrgf)
            {
                // 本仓绝区零 v3 导出也会写 uigf_version；无专属类型时按常驻 1/2 倾向 ZZZ 不可靠，
                // 优先原神（历史 UIGF v3 主场景）。仅 1/2 且无 gacha_id 的星铁文件极少见。
                return GameBiz.hk4e;
            }
        }

        // 无 gacha_id 的 1/2：更像绝区零常驻/独家（星铁导入也要求 gacha_id）
        return GameBiz.nap;
    }


    private static bool TryReadIntProperty(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out JsonElement prop))
        {
            return false;
        }
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
        {
            return true;
        }
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
        {
            return true;
        }
        return false;
    }


    private static List<GachaUidArchiveDisplay> BuildUigf3GenshinArchives(string json)
    {
        var obj = JsonSerializer.Deserialize<UIGF3File<UIGFGenshinGachaItem>>(json)
            ?? throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile);
        if (obj.List is not { Count: > 0 })
        {
            throw new InvalidDataException(Lang.UIGFGachaService_NoGachaArchivesInFile);
        }

        string lang = obj.Info?.Lang ?? "";
        long uid = obj.Info?.Uid ?? 0;
        foreach (UIGFGenshinGachaItem item in obj.List)
        {
            if (string.IsNullOrWhiteSpace(item.Lang))
            {
                item.Lang = lang;
            }
            if (item.Uid == 0)
            {
                item.Uid = uid;
            }
        }
        if (uid == 0)
        {
            uid = obj.List.FirstOrDefault(x => x.Uid != 0)?.Uid ?? 0;
        }

        UIGFGenshinGachaItem last = obj.List.OrderBy(x => x.Id).Last();
        return
        [
            new GachaUidArchiveDisplay
            {
                Game = GameBiz.hk4e,
                GameIcon = "ms-appx:///Assets/Image/icon_ys.jpg",
                Uid = uid,
                hke4List = obj.List,
                Count = obj.List.Count,
                LastItemGachaType = ((GenshinGachaType)last.GachaType).ToLocalization(),
                LastItemName = last.Name,
                LastItemTime = last.Time,
                LastItemTimeOffest = last.Time,
            },
        ];
    }


    private static List<GachaUidArchiveDisplay> BuildUigf3StarRailArchives(string json)
    {
        var obj = JsonSerializer.Deserialize<UIGF3File<StarRailGachaItem>>(json)
            ?? throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile);
        if (obj.List is not { Count: > 0 })
        {
            throw new InvalidDataException(Lang.UIGFGachaService_NoGachaArchivesInFile);
        }

        string lang = obj.Info?.Lang ?? "";
        long uid = obj.Info?.Uid ?? 0;
        foreach (StarRailGachaItem item in obj.List)
        {
            if (string.IsNullOrWhiteSpace(item.Lang))
            {
                item.Lang = lang;
            }
            if (item.Uid == 0)
            {
                item.Uid = uid;
            }
        }
        if (uid == 0)
        {
            uid = obj.List.FirstOrDefault(x => x.Uid != 0)?.Uid ?? 0;
        }

        StarRailGachaItem last = obj.List.OrderBy(x => x.Id).Last();
        return
        [
            new GachaUidArchiveDisplay
            {
                Game = GameBiz.hkrpg,
                GameIcon = "ms-appx:///Assets/Image/icon_sr.jpg",
                Uid = uid,
                hkrpgList = obj.List,
                Count = obj.List.Count,
                LastItemGachaType = ((StarRailGachaType)last.GachaType).ToLocalization(),
                LastItemName = last.Name,
                LastItemTime = last.Time,
                LastItemTimeOffest = last.Time,
            },
        ];
    }


    private static List<GachaUidArchiveDisplay> BuildUigf3ZZZArchives(string json)
    {
        var obj = JsonSerializer.Deserialize<UIGF3File<ZZZGachaItem>>(json)
            ?? throw new InvalidDataException(Lang.UIGFGachaService_CannotParseFile);
        if (obj.List is not { Count: > 0 })
        {
            throw new InvalidDataException(Lang.UIGFGachaService_NoGachaArchivesInFile);
        }

        string lang = obj.Info?.Lang ?? "";
        long uid = obj.Info?.Uid ?? 0;
        foreach (ZZZGachaItem item in obj.List)
        {
            if (string.IsNullOrWhiteSpace(item.Lang))
            {
                item.Lang = lang;
            }
            if (item.Uid == 0)
            {
                item.Uid = uid;
            }
        }
        if (uid == 0)
        {
            uid = obj.List.FirstOrDefault(x => x.Uid != 0)?.Uid ?? 0;
        }

        ZZZGachaItem last = obj.List.OrderBy(x => x.Id).Last();
        return
        [
            new GachaUidArchiveDisplay
            {
                Game = GameBiz.nap,
                GameIcon = "ms-appx:///Assets/Image/icon_zzz.jpg",
                Uid = uid,
                napList = obj.List,
                Count = obj.List.Count,
                LastItemGachaType = ((ZZZGachaType)last.GachaType).ToLocalization(),
                LastItemName = last.Name,
                LastItemTime = last.Time,
                LastItemTimeOffest = last.Time,
            },
        ];
    }





    public async Task ImportAsync(params IEnumerable<GachaUidArchiveDisplay> archives)
    {
        foreach (GachaUidArchiveDisplay archive in archives)
        {
            try
            {
                archive.Result = null;
                archive.Error = null;

                string? result = await Task.Run(() =>
                {
                    if (archive.Game == GameBiz.hk4e)
                    {
                        return ImportForGenshin(archive);
                    }
                    if (archive.Game.Value == "hk4eugc")
                    {
                        return ImportForGenshinBeyond(archive);
                    }
                    if (archive.Game == GameBiz.hkrpg)
                    {
                        return ImportForStarRail(archive);
                    }
                    if (archive.Game == GameBiz.nap)
                    {
                        return ImportForZZZ(archive);
                    }
                    return null;
                });
                archive.Result = result;
            }
            catch (UIGF4ImportException ex)
            {
                // 校验失败文案已本地化
                archive.Error = ex.Message;
                _logger.LogWarning(ex, "UIGF import validation failed for {game} uid {uid}", archive.Game, archive.Uid);
            }
            catch (Exception ex)
            {
                // 库异常等不直接抛 ex.Message（多为英文技术原文）
                archive.Error = Lang.UIGFGachaService_UnexpectedError;
                _logger.LogError(ex, "UIGF import unexpected error for {game} uid {uid}", archive.Game, archive.Uid);
            }
        }
    }


    /// <summary>缺字段错误：协议字段名映射为用户可读本地化标签。</summary>
    private static string MissingFieldMessage(string fieldKey) =>
        string.Format(Lang.UIGFGachaService_0FieldIsMissingInAGachaRecord, LocalizeFieldName(fieldKey));


    private static string LocalizeFieldName(string fieldKey) => fieldKey switch
    {
        "id" => Lang.UIGFGachaService_Field_RecordId,
        "item_id" => Lang.UIGFGachaService_Field_ItemId,
        "gacha_type" => Lang.UIGFGachaService_Field_GachaType,
        "time" => Lang.UIGFGachaService_Field_Time,
        "rank_type" => Lang.UIGFGachaService_Field_RankType,
        "gacha_id" => Lang.UIGFGachaService_Field_GachaId,
        "schedule_id" => Lang.UIGFGachaService_Field_ScheduleId,
        "op_gacha_type" => Lang.UIGFGachaService_Field_OpGachaType,
        "item_type" => Lang.UIGFGachaService_Field_ItemType,
        _ => fieldKey,
    };



    private string ImportForGenshin(GachaUidArchiveDisplay archive)
    {
        List<GenshinGachaItem> list = new();
        DateTime TIME = new DateTime(2020, 9, 1);
        bool noName = false;
        foreach (UIGFGenshinGachaItem item in archive.hke4List ?? [])
        {
            var clone = item.Clone();
            if (clone.Id == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("id"));
            }
            if (clone.ItemId == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("item_id"));
            }
            if (clone.GachaType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("gacha_type"));
            }
            if (clone.Time < TIME)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("time"));
            }
            if (clone.RankType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("rank_type"));
            }
            if (string.IsNullOrWhiteSpace(clone.Name))
            {
                noName = true;
            }
            if (clone.Uid == 0)
            {
                clone.Uid = archive.Uid;
            }
            else if (clone.Uid != archive.Uid)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, string.Format(Lang.UIGFGachaService_UidMismatchDetectedExpected0ButFound1, archive.Uid, clone.Uid));
            }
            clone.Time = item.Time.AddHours(archive.Timezone);
            list.Add(clone);
        }
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        var affect = dapper.Execute("""
            INSERT OR REPLACE INTO GenshinGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, list, t);
        t.Commit();
        _logger.LogInformation("Imported {count} gacha records for {game}.", affect, archive.Game);
        return noName ? Lang.UIGFGachaService_ImportSuccessfulButNoRecordItemName : Lang.UIGFGachaService_ImportSuccessful;
    }



    private string ImportForStarRail(GachaUidArchiveDisplay archive)
    {
        List<StarRailGachaItem> list = new();
        DateTime TIME = new DateTime(2023, 4, 1);
        bool noName = false;
        foreach (StarRailGachaItem item in archive.hkrpgList ?? [])
        {
            var clone = item.Clone();
            if (clone.Id == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("id"));
            }
            if (clone.ItemId == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("item_id"));
            }
            if (clone.GachaType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("gacha_type"));
            }
            if (clone.Time < TIME)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("time"));
            }
            if (clone.RankType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("rank_type"));
            }
            if (clone.GachaId == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("gacha_id"));
            }
            if (string.IsNullOrWhiteSpace(clone.Name))
            {
                noName = true;
            }
            if (clone.Uid == 0)
            {
                clone.Uid = archive.Uid;
            }
            else if (clone.Uid != archive.Uid)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, string.Format(Lang.UIGFGachaService_UidMismatchDetectedExpected0ButFound1, archive.Uid, clone.Uid));
            }
            clone.Time = item.Time.AddHours(archive.Timezone);
            list.Add(clone);
        }
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        var affect = dapper.Execute("""
            INSERT OR REPLACE INTO StarRailGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, GachaId, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @GachaId, @Count, @Lang);
            """, list, t);
        t.Commit();
        _logger.LogInformation("Imported {count} gacha records for {game}.", affect, archive.Game);
        return noName ? Lang.UIGFGachaService_ImportSuccessfulButNoRecordItemName : Lang.UIGFGachaService_ImportSuccessful;
    }



    private string ImportForZZZ(GachaUidArchiveDisplay archive)
    {
        List<ZZZGachaItem> list = new();
        DateTime TIME = new DateTime(2024, 7, 1);
        bool noName = false;
        foreach (ZZZGachaItem item in archive.napList ?? [])
        {
            var clone = item.Clone();
            if (clone.Id == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("id"));
            }
            if (clone.ItemId == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("item_id"));
            }
            if (clone.GachaType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("gacha_type"));
            }
            if (clone.Time < TIME)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("time"));
            }
            if (clone.RankType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("rank_type"));
            }
            if (string.IsNullOrWhiteSpace(clone.Name))
            {
                noName = true;
            }
            if (clone.Uid == 0)
            {
                clone.Uid = archive.Uid;
            }
            else if (clone.Uid != archive.Uid)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, string.Format(Lang.UIGFGachaService_UidMismatchDetectedExpected0ButFound1, archive.Uid, clone.Uid));
            }
            clone.Time = item.Time.AddHours(archive.Timezone);
            list.Add(clone);
        }
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        var affect = dapper.Execute("""
            INSERT OR REPLACE INTO ZZZGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, list, t);
        t.Commit();
        _logger.LogInformation("Imported {count} gacha records for {game}.", affect, archive.Game);
        return noName ? Lang.UIGFGachaService_ImportSuccessfulButNoRecordItemName : Lang.UIGFGachaService_ImportSuccessful;
    }


    /// <summary>
    /// 导入 UIGF hk4e_ugc 千星奇域记录到 GenshinBeyondGachaItem。
    /// 必填字段按 UIGF v4.2：id / schedule_id / item_type / item_id / item_name / rank_type / time / op_gacha_type。
    /// </summary>
    private string ImportForGenshinBeyond(GachaUidArchiveDisplay archive)
    {
        List<GenshinBeyondGachaItem> list = new();
        // 千星奇域上线约 2025-09（原神 5.x 之后），校验用宽松下界
        DateTime TIME = new DateTime(2025, 1, 1);
        bool noName = false;
        foreach (GenshinBeyondGachaItem item in archive.hk4eUgcList ?? [])
        {
            if (item.Id == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("id"));
            }
            if (item.ScheduleId == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("schedule_id"));
            }
            if (item.ItemId == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("item_id"));
            }
            if (item.OpGachaType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("op_gacha_type"));
            }
            if (item.Time < TIME)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("time"));
            }
            if (item.RankType == 0)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("rank_type"));
            }
            if (string.IsNullOrWhiteSpace(item.ItemType))
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, MissingFieldMessage("item_type"));
            }
            if (string.IsNullOrWhiteSpace(item.ItemName))
            {
                noName = true;
            }

            long uid = item.Uid;
            if (uid == 0)
            {
                uid = archive.Uid;
            }
            else if (uid != archive.Uid)
            {
                throw new UIGF4ImportException(archive.Game, archive.Uid, string.Format(Lang.UIGFGachaService_UidMismatchDetectedExpected0ButFound1, archive.Uid, uid));
            }

            list.Add(new GenshinBeyondGachaItem
            {
                Uid = uid,
                Id = item.Id,
                Region = item.Region ?? "",
                OpGachaType = item.OpGachaType,
                ScheduleId = item.ScheduleId,
                ItemType = item.ItemType,
                ItemId = item.ItemId,
                ItemName = item.ItemName ?? "",
                RankType = item.RankType,
                IsUp = item.IsUp,
                Time = item.Time.AddHours(archive.Timezone),
            });
        }
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        var affect = dapper.Execute("""
            INSERT OR REPLACE INTO GenshinBeyondGachaItem(Uid, Id, Region, OpGachaType, ScheduleId, ItemType, ItemId, ItemName, RankType, IsUp, Time)
            VALUES (@Uid, @Id, @Region, @OpGachaType, @ScheduleId, @ItemType, @ItemId, @ItemName, @RankType, @IsUp, @Time);
            """, list, t);
        t.Commit();
        _logger.LogInformation("Imported {count} gacha records for {game}.", affect, archive.Game);
        return noName ? Lang.UIGFGachaService_ImportSuccessfulButNoRecordItemName : Lang.UIGFGachaService_ImportSuccessful;
    }





    #endregion



}
