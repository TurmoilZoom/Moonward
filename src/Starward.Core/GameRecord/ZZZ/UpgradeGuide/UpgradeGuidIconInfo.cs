using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.UpgradeGuide;

public class UpgradeGuidIconInfo
{

    /// <summary>代理人图标字典，key 为物品 Id 字符串（与 H5 响应一致）。</summary>
    [JsonPropertyName("avatar_icon")]
    public Dictionary<string, UpgradeGuidIconInfoItem> AvatarIcon { get; set; }


    /// <summary>邦布图标字典，key 为物品 Id 字符串。</summary>
    [JsonPropertyName("buddy_icon")]
    public Dictionary<string, UpgradeGuidIconInfoItem> BuddyIcon { get; set; }

}



public class UpgradeGuidIconInfoItem
{

    [JsonPropertyName("square_avatar")]
    public string SquareAvatar { get; set; }


    [JsonPropertyName("rectangle_avatar")]
    public string RectangleAvatar { get; set; }


    [JsonPropertyName("vertical_painting")]
    public string? VerticalPainting { get; set; }


    [JsonPropertyName("vertical_painting_color")]
    public string? VerticalPaintingColor { get; set; }


    [JsonPropertyName("avatar_us_full_name")]
    public string? AvatarUsFullName { get; set; }

}