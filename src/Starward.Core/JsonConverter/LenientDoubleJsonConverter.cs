using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.JsonConverter;

/// <summary>
/// 宽松读取 <see cref="double"/>：数字与数字字符串照常解析，空串与 null 读成 0。
/// 绝区零抽卡统计里没抽过的频段 <c>avr_up_count</c> 返回空串 <c>""</c>，不容忍会整包解析失败。
/// </summary>
/// <remarks>
/// 与 <see cref="LenientInt32JsonConverter"/> 一致只容忍「没有值」，有值却读不出数时抛 <see cref="JsonException"/>。
/// </remarks>
internal sealed class LenientDoubleJsonConverter : JsonConverter<double>
{

    /// <inheritdoc />
    /// <exception cref="JsonException">token 类型不受支持，或字符串有值却不是数字。</exception>
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return 0;
            case JsonTokenType.Number:
                if (reader.TryGetDouble(out double number))
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
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    return value;
                }
                break;
        }
        throw new JsonException($"Cannot read {reader.TokenType} token as double.");
    }


    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

}
