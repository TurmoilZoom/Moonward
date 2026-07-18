using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 启动参数配置文件。每个游戏区服可以有多个配置文件，每个配置文件保存一套命令行参数与自定义启动程序设置。
/// 内部名 <c>configN</c> 与显示名「配置文件 N」序号一一对应（N = 1…8）。
/// 第一个配置文件 Id 固定为 <see cref="DefaultId"/>（config1），其参数/工具数据存储在 legacy 设置键中；
/// 其余配置文件序列化为 JSON 存储。
/// 「启动方式」另有 <see cref="NoneId"/>（无），不在本配置对话框中管理，表示不使用任何启动参数配置。
/// </summary>
public sealed class GameLaunchProfile : ObservableObject
{

    /// <summary>
    /// 每个游戏区服最多的配置文件数量。
    /// </summary>
    public const int MaxCount = 8;


    /// <summary>
    /// 8 个固定内部名：config1…config8，与「配置文件 1…8」一一对应。
    /// </summary>
    public static readonly string[] InternalNames = CreateInternalNames();


    /// <summary>
    /// 第一个配置文件的内部名（config1 ↔ 配置文件1），数据存于 legacy 设置键。
    /// </summary>
    public const string DefaultId = "config1";


    /// <summary>
    /// 「启动方式」中的「无」：不使用启动参数配置启动，但仍受 DX12 等全局开关影响。
    /// 不在「启动参数配置」对话框中出现。
    /// </summary>
    public const string NoneId = "none";


    private static string[] CreateInternalNames()
    {
        var names = new string[MaxCount];
        for (int i = 0; i < MaxCount; i++)
        {
            names[i] = IdFromIndex(i + 1);
        }
        return names;
    }


    /// <summary>
    /// 由序号生成内部名：1 → config1。
    /// </summary>
    public static string IdFromIndex(int index)
    {
        if (index < 1 || index > MaxCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be 1…{MaxCount}.");
        }
        return $"config{index}";
    }


    /// <summary>
    /// 从内部名解析序号：config1 → 1，…，config8 → 8；无法解析时返回 null。
    /// </summary>
    public static int? TryGetIndex(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        id = id.Trim();
        if (!id.StartsWith("config", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (int.TryParse(id.AsSpan("config".Length), out int index) && index >= 1 && index <= MaxCount)
        {
            return index;
        }
        return null;
    }


    /// <summary>
    /// 将内部名规范为名单中的写法（config1…config8 或 none）；空白或不识别时返回空字符串。
    /// </summary>
    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }
        id = id.Trim();
        if (string.Equals(id, NoneId, StringComparison.OrdinalIgnoreCase))
        {
            return NoneId;
        }
        int? index = TryGetIndex(id);
        return index is int n ? IdFromIndex(n) : "";
    }


    /// <summary>
    /// 是否为「无」启动方式（未设置 / 空 / <see cref="NoneId"/>）。
    /// </summary>
    public static bool IsNoneId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return true;
        }
        return string.Equals(id.Trim(), NoneId, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 是否为已知配置文件内部名 config1…config8（不含「无」）。
    /// </summary>
    public static bool IsKnownId(string? id)
    {
        return TryGetIndex(id) is not null;
    }


    /// <summary>
    /// 是否为第一个配置文件内部名 config1。
    /// </summary>
    public static bool IsDefaultId(string? id)
    {
        return TryGetIndex(id) is 1;
    }


    /// <summary>
    /// 内部名，取自 <see cref="InternalNames"/>（config1…config8）。与显示名序号对应，不可改 Id，也用于 URL 协议启动。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";


    /// <summary>
    /// 用户自定义的显示名（默认应为「配置文件 N」，N 与 <see cref="Id"/> 中序号相同）。
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
    /// 是否为第一个配置文件（config1，数据在 legacy 键）。
    /// </summary>
    [JsonIgnore]
    public bool IsDefault => IsDefaultId(Id);


    /// <summary>
    /// 是否为「无」启动方式（仅用于「选择启动方式」列表，不参与配置对话框）。
    /// </summary>
    [JsonIgnore]
    public bool IsNone => IsNoneId(Id);


    /// <summary>
    /// 下拉框显示文本：显示名（configN）；「无」仅显示名称。
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (IsNone)
            {
                return Name;
            }
            string id = NormalizeId(Id);
            return string.IsNullOrEmpty(id) ? Name : $"{Name}（{id}）";
        }
    }

}
