namespace Starward.Core.Gacha.Genshin;

/// <summary>
/// 获取千星奇域物品信息的结果。
/// </summary>
public class GenshinBeyondGachaInfoFetchResult
{

    /// <summary>
    /// 实际返回数据（或 304）的数据源地址。
    /// </summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// 数据源返回的 ETag；数据源不支持时为 null。
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>
    /// 数据源返回 304，本地数据已是最新。
    /// </summary>
    public bool NotModified { get; init; }

    /// <summary>
    /// 物品列表，图标已解析为绝对地址；<see cref="NotModified"/> 为 true 时为空。
    /// </summary>
    public List<GenshinBeyondGachaInfo> Items { get; init; } = [];

}
