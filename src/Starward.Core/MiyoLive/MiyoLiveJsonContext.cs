using System.Text.Json.Serialization;

namespace Starward.Core.MiyoLive;

[JsonSerializable(typeof(miHoYoApiWrapper<MiyoLiveIndexData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<MiyoLiveCodeData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<MiyoLiveUserInstantListData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<MiyoLiveHomeData>))]
internal partial class MiyoLiveJsonContext : JsonSerializerContext
{

}
