using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.JsonConverter;

/// <summary>
/// 宽松读取 <see cref="long"/>：数字与数字字符串照常解析，空串与 null 读成 0。
/// 用于把数值写成字符串的米哈游接口（如绝区零抽卡统计的 <c>"total_count": "0"</c>、<c>"record_time": "1790741272"</c>）。
/// </summary>
/// <remarks>
/// 与 <see cref="LenientInt32JsonConverter"/> 一致只容忍「没有值」，有值却读不出数时抛 <see cref="JsonException"/>。
/// </remarks>
internal sealed class LenientInt64JsonConverter : JsonConverter<long>
{

    /// <inheritdoc />
    /// <exception cref="JsonException">token 类型不受支持，或字符串有值却不是整数。</exception>
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return 0;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out long number))
                {
                    return number;
                }
                break;
            case JsonTokenType.String:
                string? text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                {
                    return value;
                }
                break;
        }
        throw new JsonException($"Cannot read {reader.TokenType} token as long.");
    }


    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

}
