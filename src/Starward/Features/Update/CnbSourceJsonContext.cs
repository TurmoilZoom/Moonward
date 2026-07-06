using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Starward.Features.Update;

[JsonSerializable(typeof(CnbRelease))]
[JsonSerializable(typeof(List<CnbRelease>))]
internal partial class CnbSourceJsonContext : JsonSerializerContext;