using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.JsonConverter;

/// <summary>
/// 将 JSON 的 string / number / boolean 统一读成 <see cref="string"/>。
/// 用于米哈游接口里类型不稳定的字段（如 aid 有时是数字）。
/// </summary>
internal class JsonPrimitiveStringConverter : JsonConverter<string>
{

    /// <inheritdoc />
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => reader.TryGetInt64(out long l)
                ? l.ToString()
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} when reading string."),
        };
    }


    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

}
