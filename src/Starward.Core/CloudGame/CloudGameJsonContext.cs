using System.Text.Json.Serialization;

namespace Starward.Core.CloudGame;

[JsonSerializable(typeof(miHoYoApiWrapper<CloudGameWallet>))]
[JsonSerializable(typeof(miHoYoApiWrapper<CloudGameGamerLoginResult>))]
internal partial class CloudGameJsonContext : JsonSerializerContext
{

}
