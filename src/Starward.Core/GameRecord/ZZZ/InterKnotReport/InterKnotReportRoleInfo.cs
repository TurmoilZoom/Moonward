using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报汇总中的角色展示信息，对应 API <c>role_info</c>。
/// </summary>
public class InterKnotReportRoleInfo
{
    /// <summary>角色昵称。</summary>
    [JsonPropertyName("nickname")]
    public string Nickname { get; set; }

    /// <summary>角色头像 URL。</summary>
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }
}