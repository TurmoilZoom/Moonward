using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 启动参数配置文件。每个游戏区服可以有多个配置文件，每个配置文件保存一套命令行参数与自定义启动程序设置。
/// 内部名 <c>configN</c> 与显示名「配置文件 N」序号一一对应（N ≥ 1，无数量上限）。
/// 第一个配置文件 Id 固定为 <see cref="DefaultId"/>（config1），其参数/工具数据存储在 legacy 设置键中；
/// 其余配置文件序列化为 JSON 存储。
/// 「启动方式」另有 <see cref="NoneId"/>（无），不在本配置对话框中管理，表示不使用任何启动参数配置。
/// </summary>
public sealed class GameLaunchProfile : ObservableObject
{

    /// <summary>
    /// 第一个配置文件的内部名（config1 ↔ 配置文件1），数据存于 legacy 设置键。
    /// </summary>
    public const string DefaultId = "config1";


    /// <summary>
    /// 「启动方式」中的「无」：不使用启动参数配置启动，但仍受 DX12 等全局开关影响。
    /// 不在「启动参数配置」对话框中出现。
    /// </summary>
    public const string NoneId = "none";


    /// <summary>
    /// 由序号生成内部名：1 → config1。
    /// </summary>
    /// <param name="index">配置序号，须 ≥ 1。</param>
    public static string IdFromIndex(int index)
    {
        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be ≥ 1.");
        }
        return $"config{index}";
    }


    /// <summary>
    /// 从内部名解析序号：config1 → 1，config2 → 2，…；无法解析时返回 null。
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
        // 仅接受 config + 正整数（无数量上限）
        if (int.TryParse(id.AsSpan("config".Length), out int index) && index >= 1)
        {
            return index;
        }
        return null;
    }


    /// <summary>
    /// 在已占用内部名中，找到最小可用序号对应的 configN（从 1 起，无上限）。
    /// </summary>
    /// <param name="usedIds">已占用的内部名（大小写不敏感）。</param>
    /// <returns>如 config3。</returns>
    public static string GetNextAvailableId(IEnumerable<string> usedIds)
    {
        var used = new HashSet<string>(usedIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            string id = IdFromIndex(i);
            if (!used.Contains(id))
            {
                return id;
            }
        }
    }


    /// <summary>
    /// 将内部名规范为标准写法（configN 或 none）；空白或不识别时返回空字符串。
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
    /// 是否为已知配置文件内部名 configN（N ≥ 1，不含「无」）。
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
    /// 内部名（configN）。与显示名序号对应，不可改 Id，也用于 URL 协议启动。
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
    /// 为 true 时，即使全局已启用 DX12，本配置文件启动时也不自动附加 <c>-use-d3d12</c>。
    /// 全局开关不变；config1 存于 legacy 键，其余配置写入 JSON。
    /// </summary>
    [JsonPropertyName("skip_auto_dx12")]
    public bool SkipAutoDx12 { get; set; }


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
    /// 启动时用于自动登录的游戏角色 UID（与米游社工具箱 <c>GameRecordRole.Uid</c> 一致）。
    /// <c>null</c> 或 <c>≤0</c> 表示不指定；config1 存于 legacy 键，其余配置写入 JSON。
    /// </summary>
    [JsonPropertyName("login_uid")]
    public long? LoginUid { get; set; }


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
