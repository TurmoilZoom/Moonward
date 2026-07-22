using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Starward.Features.Gacha.UIGF;

/// <summary>
/// UIGF v4.x 多游戏抽卡归档根对象。
/// <para>支持导出 <c>v4.0</c> / <c>v4.1</c> / <c>v4.2</c>；导入时各子版本共用同一解析路径。</para>
/// <para>v4.1：星铁联动池 gacha_type 21/22；v4.2：千星奇域 <c>hk4e_ugc</c>。</para>
/// </summary>
public class UIGF4File
{

    [JsonPropertyName("info")]
    public UIGF4FileInfo Info { get; set; }


    [JsonPropertyName("hk4e")]
    public List<UIGF4GachaArchive<UIGFGenshinGachaItem>>? hk4eGachaArchives { get; set; }


    [JsonPropertyName("hkrpg")]
    public List<UIGF4GachaArchive<StarRailGachaItem>>? hkrpgGachaArchives { get; set; }


    [JsonPropertyName("nap")]
    public List<UIGF4GachaArchive<ZZZGachaItem>>? napGachaArchives { get; set; }


    /// <summary>原神千星奇域归档，仅 UIGF v4.2+；导出 v4.0/v4.1 时为 null 且不写出。</summary>
    [JsonPropertyName("hk4e_ugc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UIGF4BeyondGachaArchive>? hk4eUgcGachaArchives { get; set; }



    public UIGF4File()
    {
        Info = new();
        hk4eGachaArchives = new();
        hkrpgGachaArchives = new();
        napGachaArchives = new();
        // hk4e_ugc 默认 null，仅在导出 v4.2 时填充
    }


}



public class UIGF4FileInfo
{

    /// <summary>
    /// 时间戳，秒
    /// </summary>
    [JsonPropertyName("export_timestamp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long ExportTimestamp { get; set; }


    [JsonPropertyName("export_app")]
    public string ExportApp { get; set; }


    [JsonPropertyName("export_app_version")]
    public string ExportAppVersion { get; set; }


    /// <summary>UIGF 协议版本字符串（如 v4.0 / v4.1 / v4.2），由导出逻辑写入。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "v4.0";


    public UIGF4FileInfo()
    {
        ExportTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        ExportApp = "Moonward";
        ExportAppVersion = AppConfig.AppVersion;
    }


}



public class UIGF4GachaArchive<T> where T : GachaLogItem
{

    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Uid { get; set; }


    [JsonPropertyName("timezone")]
    public int Timezone { get; set; }


    [JsonPropertyName("lang")]
    public string Lang { get; set; }


    [JsonPropertyName("list")]
    public List<T> List { get; set; }


}



/// <summary>
/// 千星奇域抽卡归档（UIGF <c>hk4e_ugc</c>）。
/// 条目模型与主游戏抽卡字段不同，故不复用 <see cref="UIGF4GachaArchive{T}"/>。
/// </summary>
public class UIGF4BeyondGachaArchive
{

    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Uid { get; set; }


    [JsonPropertyName("timezone")]
    public int Timezone { get; set; }


    [JsonPropertyName("lang")]
    public string Lang { get; set; }


    [JsonPropertyName("list")]
    public List<GenshinBeyondGachaItem> List { get; set; }


}
