using System.Text.Json.Serialization;

namespace Starward.Core.HoYoPlay;

/// <summary>
/// 预约中游戏的详情页内容（getGameReservationContent）：简介、标签、宣传图片与视频、社交媒体图标
/// </summary>
public class GameReservationContent
{

    [JsonPropertyName("language")]
    public string Language { get; set; }

    /// <summary>
    /// 实际返回内容的语言；没有对应语言的内容时会回落到其他语言（如国服启动器总是 zh-cn）
    /// </summary>
    [JsonPropertyName("resolved_language")]
    public string ResolvedLanguage { get; set; }

    [JsonPropertyName("introduction")]
    public string Introduction { get; set; }

    /// <summary>
    /// 详情页右侧的 Logo 图片地址
    /// </summary>
    [JsonPropertyName("logo")]
    public string Logo { get; set; }

    [JsonPropertyName("tags")]
    public List<GameReservationTag> Tags { get; set; } = [];

    /// <summary>
    /// 宣传图片与视频，按 <see cref="GameReservationMedia.Order"/> 排列
    /// </summary>
    [JsonPropertyName("media")]
    public List<GameReservationMedia> Medias { get; set; } = [];

    [JsonPropertyName("icons")]
    public List<GameReservationIcon> Icons { get; set; } = [];

}


/// <summary>
/// 游戏类型标签，如「生活模拟」
/// </summary>
public class GameReservationTag
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}


/// <summary>
/// 宣传图片或视频
/// </summary>
public class GameReservationMedia
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>
    /// <see cref="GameReservationMediaType"/>
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>
    /// 图片本身，或视频的封面
    /// </summary>
    [JsonPropertyName("cover")]
    public GameImage Cover { get; set; }

    /// <summary>
    /// 视频地址（webm），图片为空
    /// </summary>
    [JsonPropertyName("resource_url")]
    public string ResourceUrl { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonIgnore]
    public bool IsVideo => Type is GameReservationMediaType.RESERVATION_MEDIA_TYPE_VIDEO && !string.IsNullOrWhiteSpace(ResourceUrl);
}


/// <summary>
/// 社交媒体图标：有 <see cref="Link"/> 的点击打开链接，有 <see cref="QrImage"/> 的悬停显示二维码
/// </summary>
public class GameReservationIcon
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("icon")]
    public GameImage Icon { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; }

    [JsonPropertyName("qr_image")]
    public GameImage? QrImage { get; set; }

    /// <summary>
    /// 二维码下方的说明，如「扫码关注微信公众号」
    /// </summary>
    [JsonPropertyName("qr_description")]
    public string? QrDescription { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}


public abstract class GameReservationMediaType
{
    public const string RESERVATION_MEDIA_TYPE_IMAGE = "RESERVATION_MEDIA_TYPE_IMAGE";

    public const string RESERVATION_MEDIA_TYPE_VIDEO = "RESERVATION_MEDIA_TYPE_VIDEO";
}
