using System.Text.Json.Serialization;

namespace Starward.Core.CloudGame;

/// <summary>
/// 云游戏登录接口（<c>gamer/api/login</c>）返回的数据节点。
/// </summary>
/// <remarks>
/// 该接口只用来触发服务端的「登录」行为（每日免费时长随之发放），返回内容本身不参与展示，
/// 这里保留字段仅为让响应能完整反序列化。
/// </remarks>
public class CloudGameGamerLoginResult
{

    /// <summary>是否为该云游戏的新用户。</summary>
    [JsonPropertyName("new_user")]
    public bool NewUser { get; set; }

}
