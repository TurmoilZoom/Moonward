using System.Text.Json.Serialization;

namespace Starward.Features.Update;

[JsonSerializable(typeof(ReleaseNotesCacheFile))]
internal partial class ReleaseNotesCacheJsonContext : JsonSerializerContext;
