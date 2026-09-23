using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.JsonConverter;

/// <summary>
/// 宽松读取 <see cref="int"/>：数字与数字字符串照常解析，空串与 null 读成 0。
/// 用于把数值写成字符串的米哈游接口（如云游戏钱包 <c>"coin_num": "0"</c>），个别字段偶尔缺值时不至于整包解析失败。
/// </summary>
/// <remarks>
/// 只容忍「没有值」。有值却读不出数说明接口格式变了，这里抛 <see cref="JsonException"/> 而不是读成 0：
/// 静默的 0 会把余额显示成「0 分钟」，用户既看不出也报不上来。
/// </remarks>
internal sealed class LenientInt32JsonConverter : JsonConverter<int>
{

    /// <inheritdoc />
    /// <exception cref="JsonException">token 类型不受支持，或字符串有值却不是整数。</exception>
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return 0;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out int number))
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
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    return value;
                }
                break;
        }
        throw new JsonException($"Cannot read {reader.TokenType} token as int.");
    }


    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

}
