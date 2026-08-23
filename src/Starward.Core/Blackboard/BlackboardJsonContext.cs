using System.Text.Json.Serialization;

namespace Starward.Core.Blackboard;

[JsonSerializable(typeof(miHoYoApiWrapper<BlackboardGachaPoolData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<BlackboardPositionData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<BlackboardContentListData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<WikiEntryPageData>))]
[JsonSerializable(typeof(WikiMapDescData))]
[JsonSerializable(typeof(BlackboardGachaPoolExt))]
internal partial class BlackboardJsonContext : JsonSerializerContext
{

}


/// <summary>
/// 卡池图标 <c>ext</c> 字段解析用（绝区零 level / type）。
/// </summary>
public class BlackboardGachaPoolExt
{

    [JsonPropertyName("type")]
    public string? Type { get; set; }


    [JsonPropertyName("level")]
    public string? Level { get; set; }

}
