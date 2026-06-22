using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 启动参数配置文件。每个游戏区服可以有多个配置文件，每个配置文件保存一套命令行参数与自定义启动程序设置。
/// 第一个（默认）配置文件 Id 固定为 <see cref="DefaultId"/>，其参数/工具数据存储在 legacy 设置键中；
/// 其余配置文件序列化为 JSON 存储。目前只有默认配置文件在启动游戏时生效。
/// </summary>
public sealed class GameLaunchProfile : ObservableObject
{

    /// <summary>
    /// 8 个固定内部名（也用于 URL 协议按配置文件启动游戏）。第一个 Alice 为默认配置文件。
    /// </summary>
    public static readonly string[] InternalNames = ["Alice", "Bob", "Charlie", "Dave", "Eve", "Mallory", "Trent", "Carol"];


    /// <summary>
    /// 每个游戏区服最多的配置文件数量。
    /// </summary>
    public const int MaxCount = 8;


    /// <summary>
    /// 默认（第一个）配置文件的内部名。
    /// </summary>
    public const string DefaultId = "Alice";


    /// <summary>
    /// 内部名，取自 <see cref="InternalNames"/>（Alice…Carol）。显示在中文名之后，不可修改，也用于 URL 协议启动。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";


    /// <summary>
    /// 用户自定义的中文显示名。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    } = "";


    /// <summary>
    /// 命令行启动参数（不含 DX12 等自动追加的参数）。
    /// </summary>
    [JsonPropertyName("argument")]
    public string? Argument { get; set; }


    /// <summary>
    /// 是否启用自定义启动程序。
    /// </summary>
    [JsonPropertyName("enable_third_party_tool")]
    public bool EnableThirdPartyTool { get; set; }


    /// <summary>
    /// 自定义启动程序路径。
    /// </summary>
    [JsonPropertyName("third_party_tool_path")]
    public string? ThirdPartyToolPath { get; set; }


    /// <summary>
    /// 是否为默认（第一个）配置文件。
    /// </summary>
    [JsonIgnore]
    public bool IsDefault => Id == DefaultId;


    /// <summary>
    /// 下拉框显示文本：中文名（内部名）。
    /// </summary>
    [JsonIgnore]
    public string DisplayName => $"{Name}（{Id}）";

}
