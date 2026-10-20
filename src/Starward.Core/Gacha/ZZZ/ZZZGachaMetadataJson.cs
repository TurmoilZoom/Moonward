using System.Text.Json;

namespace Starward.Core.Gacha.ZZZ;

/// <summary>
/// 解析 metadata 分支上的绝区零抽卡物品元数据文件（miHoYoApiWrapper 包裹的 <see cref="ZZZGachaWiki"/>）。
/// 放在 Core 是为了走 <see cref="GachaLogJsonContext"/>，主项目拿不到这个 internal 的 context。
/// </summary>
public static class ZZZGachaMetadataJson
{

    /// <summary>
    /// 将 UTF-8 JSON 解析为元数据；带 BOM 也能解析。
    /// </summary>
    /// <param name="utf8Json">文件原始字节。</param>
    /// <returns>解析出的 wiki；内容为空或 <c>data</c> 缺失时为 null。</returns>
    /// <exception cref="JsonException">内容不是合法的元数据 JSON。</exception>
    public static ZZZGachaWiki? Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.StartsWith("﻿"u8))
        {
            utf8Json = utf8Json[3..];
        }
        if (utf8Json.IsEmpty)
        {
            return null;
        }
        return JsonSerializer.Deserialize(utf8Json, GachaLogJsonContext.Default.miHoYoApiWrapperZZZGachaWiki)?.Data;
    }

}
