using System.Text.Json.Serialization;

namespace Starward.Core.Gacha;

/// <summary>
/// 游戏各版本的开始时间，来自 Moonward-Assets 的 <c>&lt;游戏&gt;/versions/versions.json</c>：
/// 历史部分一次性整理并核对，之后由该仓库的工作流每天从卡池日历追加新版本。
/// </summary>
public class GameVersionSchedule
{

    /// <summary>按开始时间升序。</summary>
    [JsonPropertyName("versions")]
    public List<GameVersionScheduleItem> Versions { get; set; } = new();

}


/// <summary>
/// 一个版本的开始时间。
/// </summary>
public class GameVersionScheduleItem
{

    /// <summary>版本号，如 3.4。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";


    /// <summary>开始时间（带时区，历史版本为上线日 06:00 UTC+8）。</summary>
    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; set; }


    /// <summary>自动追加的条目为 <c>ennead</c>，历史条目为空。</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

}


/// <summary>
/// <see cref="GachaLogClient.GetVersionScheduleAsync"/> 的结果。
/// </summary>
public class GameVersionScheduleFetchResult
{

    /// <summary>实际返回数据的数据源。</summary>
    public string SourceUrl { get; set; } = "";


    /// <summary>该数据源返回的 ETag，下次对同一数据源发送条件请求。</summary>
    public string? ETag { get; set; }


    /// <summary>数据源返回 304，本地缓存仍是最新。</summary>
    public bool NotModified { get; set; }


    /// <summary>版本表；<see cref="NotModified"/> 为 true 时为 null。</summary>
    public GameVersionSchedule? Schedule { get; set; }


    /// <summary>在返回数据之前失败的数据源及原因（排查为什么回落到兜底源）。</summary>
    public List<string> FailedSources { get; set; } = new();

}
