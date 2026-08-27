using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 命令行参数预设分类（展示分组）。
/// </summary>
public enum GameCommandLineArgumentCategory
{
    Display,
    Resolution,
    Graphics,
    Other,
    BetterGI,
}


/// <summary>
/// 单条可勾选的命令行参数预设（含可选取值）。
/// 常用项来自 Unity Standalone Player 文档及社区/开源启动器常见用法。
/// </summary>
public sealed class GameCommandLineArgumentOption : ObservableObject
{

    public GameCommandLineArgumentOption(
        string id,
        string key,
        GameCommandLineArgumentCategory category,
        string? exclusiveGroup = null,
        bool hasValue = false,
        string? defaultValue = null,
        string? alternateKey = null,
        bool valueOptional = false,
        bool consumeRemainingValues = false)
    {
        Id = id;
        Key = key;
        AlternateKey = alternateKey;
        Category = category;
        ExclusiveGroup = exclusiveGroup;
        HasValue = hasValue;
        DefaultValue = defaultValue;
        ValueOptional = valueOptional;
        ConsumeRemainingValues = consumeRemainingValues;
        Value = defaultValue ?? "";
    }


    /// <summary>内部标识，稳定不变。</summary>
    public string Id { get; }

    /// <summary>写入命令行的主开关/键（如 <c>-popupwindow</c> 或 <c>-screen-width</c>）。</summary>
    public string Key { get; }

    /// <summary>解析时额外识别的等价键（如 <c>-force-d3d12</c> 与 <c>-use-d3d12</c>）。</summary>
    public string? AlternateKey { get; }

    public GameCommandLineArgumentCategory Category { get; }

    /// <summary>互斥组名；同组内勾选一项时取消其他项。</summary>
    public string? ExclusiveGroup { get; }

    /// <summary>是否需要附加取值（如分辨率宽高、BetterGI 配置名）。</summary>
    public bool HasValue { get; }

    public string? DefaultValue { get; }

    /// <summary>取值可省略（如 <c>startOneDragon</c> 不带配置名）。空值时仍写入开关本身。</summary>
    public bool ValueOptional { get; }

    /// <summary>
    /// 解析时吞掉后续所有非预设键的 token，作为空格分隔的取值（调度器配置组名）。
    /// </summary>
    public bool ConsumeRemainingValues { get; }

    /// <summary>
    /// 是否在 UI 中显示取值编辑框。窗口化/全屏等取值由选项本身固定，不展示。
    /// </summary>
    public bool ShowValueEditor => ShowCompactValueEditor || ShowWideValueEditor;

    /// <summary>窄输入框（分辨率宽高、显示器序号）。</summary>
    public bool ShowCompactValueEditor => HasValue && Id is "screen_width" or "screen_height" or "monitor";

    /// <summary>宽输入框（BetterGI 配置名 / 配置组名）。</summary>
    public bool ShowWideValueEditor => HasValue && (ValueOptional || ConsumeRemainingValues);


    /// <summary>展示标题（本地化）。</summary>
    public string Title => Id switch
    {
        "windowed" => Lang.CmdArg_Windowed,
        "fullscreen" => Lang.CmdArg_Fullscreen,
        "popupwindow" => Lang.CmdArg_PopupWindow,
        "window_mode_exclusive" => Lang.CmdArg_WindowModeExclusive,
        "window_mode_borderless" => Lang.CmdArg_WindowModeBorderless,
        "screen_width" => Lang.CmdArg_ScreenWidth,
        "screen_height" => Lang.CmdArg_ScreenHeight,
        "monitor" => Lang.CmdArg_Monitor,
        "force_d3d11" => Lang.CmdArg_ForceD3D11,
        "force_d3d12" => Lang.CmdArg_ForceD3D12,
        "force_vulkan" => Lang.CmdArg_ForceVulkan,
        "force_low_power" => Lang.CmdArg_ForceLowPowerDevice,
        "force_gfx_direct" => Lang.CmdArg_ForceGfxDirect,
        "nolog" => Lang.CmdArg_NoLog,
        "single_instance" => Lang.CmdArg_SingleInstance,
        "bettergi_start" => Lang.CmdArg_BetterGI_Start,
        "bettergi_onedragon" => Lang.CmdArg_BetterGI_OneDragon,
        "bettergi_groups" => Lang.CmdArg_BetterGI_Groups,
        "bettergi_taskprogress" => Lang.CmdArg_BetterGI_TaskProgress,
        _ => Id,
    };


    /// <summary>说明文字（本地化）。</summary>
    public string Description => Id switch
    {
        "windowed" => Lang.CmdArg_Windowed_Desc,
        "fullscreen" => Lang.CmdArg_Fullscreen_Desc,
        "popupwindow" => Lang.CmdArg_PopupWindow_Desc,
        "window_mode_exclusive" => Lang.CmdArg_WindowModeExclusive_Desc,
        "window_mode_borderless" => Lang.CmdArg_WindowModeBorderless_Desc,
        "screen_width" => Lang.CmdArg_ScreenWidth_Desc,
        "screen_height" => Lang.CmdArg_ScreenHeight_Desc,
        "monitor" => Lang.CmdArg_Monitor_Desc,
        "force_d3d11" => Lang.CmdArg_ForceD3D11_Desc,
        "force_d3d12" => Lang.CmdArg_ForceD3D12_Desc,
        "force_vulkan" => Lang.CmdArg_ForceVulkan_Desc,
        "force_low_power" => Lang.CmdArg_ForceLowPowerDevice_Desc,
        "force_gfx_direct" => Lang.CmdArg_ForceGfxDirect_Desc,
        "nolog" => Lang.CmdArg_NoLog_Desc,
        "single_instance" => Lang.CmdArg_SingleInstance_Desc,
        "bettergi_start" => Lang.CmdArg_BetterGI_Start_Desc,
        "bettergi_onedragon" => Lang.CmdArg_BetterGI_OneDragon_Desc,
        "bettergi_groups" => Lang.CmdArg_BetterGI_Groups_Desc,
        "bettergi_taskprogress" => Lang.CmdArg_BetterGI_TaskProgress_Desc,
        _ => "",
    };


    /// <summary>取值输入框占位（宽高用默认值；BetterGI 用说明性占位）。</summary>
    public string? ValuePlaceholder => Id switch
    {
        "bettergi_onedragon" => Lang.CmdArg_BetterGI_OneDragon_Placeholder,
        "bettergi_groups" => Lang.CmdArg_BetterGI_Groups_Placeholder,
        "bettergi_taskprogress" => Lang.CmdArg_BetterGI_TaskProgress_Placeholder,
        _ => DefaultValue,
    };


    /// <summary>分类标题（本地化）。</summary>
    public string CategoryTitle => Category switch
    {
        GameCommandLineArgumentCategory.Display => Lang.CmdArg_Category_Display,
        GameCommandLineArgumentCategory.Resolution => Lang.CmdArg_Category_Resolution,
        GameCommandLineArgumentCategory.Graphics => Lang.CmdArg_Category_Graphics,
        GameCommandLineArgumentCategory.BetterGI => Lang.CmdArg_Category_BetterGI,
        _ => Lang.CmdArg_Category_Other,
    };


    /// <summary>展示用参数片段预览（不含用户取值时用占位）。</summary>
    public string TokenPreview
    {
        get
        {
            if (!HasValue)
            {
                return Key;
            }
            if (string.IsNullOrWhiteSpace(Value))
            {
                if (ValueOptional || ConsumeRemainingValues)
                {
                    return Key;
                }
                string fallback = DefaultValue ?? "…";
                return $"{Key} {fallback}";
            }
            return $"{Key} {Value.Trim()}";
        }
    }


    /// <summary>是否勾选该预设。</summary>
    public bool IsSelected
    {
        get;
        set => SetProperty(ref field, value);
    }


    /// <summary>
    /// 是否允许用户操作。由应用全局管理的项（如绝区零 DX12 的 <c>-use-d3d12</c>）为 false。
    /// </summary>
    public bool IsEnabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;


    /// <summary>
    /// 是否写入组合后的命令行字符串。
    /// 全局已由启动流程附加的参数（如 <c>-use-d3d12</c>）为 false，仅用于列表展示同步。
    /// </summary>
    public bool IncludeInBuild { get; set; } = true;


    /// <summary>取值型参数的当前值（宽高、显示器序号等）。</summary>
    public string Value
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? ""))
            {
                OnPropertyChanged(nameof(TokenPreview));
            }
        }
    } = "";


    /// <summary>
    /// 生成写入命令行的片段；未勾选或不参与组合时返回 null。
    /// 固定取值选项始终使用 <see cref="DefaultValue"/>，避免误改。
    /// </summary>
    public string? ToArgumentFragment()
    {
        if (!IsSelected || !IncludeInBuild)
        {
            return null;
        }
        if (!HasValue)
        {
            return Key;
        }
        string v = ShowValueEditor
            ? (string.IsNullOrWhiteSpace(Value) ? (DefaultValue ?? "") : Value.Trim())
            : (DefaultValue ?? Value?.Trim() ?? "");
        if (string.IsNullOrEmpty(v))
        {
            // 一条龙可不带配置名；调度器未填组名时仍写出开关本身
            return ValueOptional || ConsumeRemainingValues ? Key : null;
        }
        return $"{Key} {v}";
    }


    public bool MatchesKey(string token)
    {
        return token.Equals(Key, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(AlternateKey) && token.Equals(AlternateKey, StringComparison.OrdinalIgnoreCase));
    }

}


/// <summary>
/// 命令行参数预设分组，供列表绑定。
/// </summary>
public sealed class GameCommandLineArgumentGroup
{
    public string Title { get; set; } = "";

    public ObservableCollection<GameCommandLineArgumentOption> Options { get; set; } = new();
}


/// <summary>
/// Unity / 米哈游 PC 客户端常用启动参数，以及 BetterGI 自定义启动命令：预设目录、解析与组合。
/// 参考 Unity Standalone Player 命令行参数文档、社区启动器常见开关，以及 BetterGI 官方命令行（start / startOneDragon / --startGroups / --TaskProgress）。
/// 显示与分辨率类预设统一在「启动参数配置」中管理（游戏设置页不再重复提供）。
/// </summary>
public static class GameCommandLineArgumentHelper
{

    /// <summary>
    /// 创建一套新的可编辑预设（每次打开选择面板时新建，避免跨配置串状态）。
    /// </summary>
    public static List<GameCommandLineArgumentOption> CreateOptions()
    {
        return
        [
            // BetterGI 命令行（官方文档：start / startOneDragon / --startGroups / --TaskProgress，四者互斥）
            // 用于自定义启动程序指向 BetterGI.exe 时；组合时会写在最前，以免 BGI 把 Unity 参数当成动作。
            new("bettergi_start", "start", GameCommandLineArgumentCategory.BetterGI, exclusiveGroup: "bettergi"),
            new("bettergi_onedragon", "startOneDragon", GameCommandLineArgumentCategory.BetterGI, exclusiveGroup: "bettergi", hasValue: true, alternateKey: "--startOneDragon", valueOptional: true),
            new("bettergi_groups", "--startGroups", GameCommandLineArgumentCategory.BetterGI, exclusiveGroup: "bettergi", hasValue: true, consumeRemainingValues: true),
            new("bettergi_taskprogress", "--TaskProgress", GameCommandLineArgumentCategory.BetterGI, exclusiveGroup: "bettergi", hasValue: true, consumeRemainingValues: true),

            // 显示模式（互斥）
            new("windowed", "-screen-fullscreen", GameCommandLineArgumentCategory.Display, exclusiveGroup: "display", hasValue: true, defaultValue: "0"),
            new("fullscreen", "-screen-fullscreen", GameCommandLineArgumentCategory.Display, exclusiveGroup: "display", hasValue: true, defaultValue: "1"),
            new("popupwindow", "-popupwindow", GameCommandLineArgumentCategory.Display, exclusiveGroup: "display"),
            new("window_mode_exclusive", "-window-mode", GameCommandLineArgumentCategory.Display, exclusiveGroup: "display", hasValue: true, defaultValue: "exclusive"),
            new("window_mode_borderless", "-window-mode", GameCommandLineArgumentCategory.Display, exclusiveGroup: "display", hasValue: true, defaultValue: "borderless"),

            // 分辨率 / 显示器
            new("screen_width", "-screen-width", GameCommandLineArgumentCategory.Resolution, hasValue: true, defaultValue: "1920"),
            new("screen_height", "-screen-height", GameCommandLineArgumentCategory.Resolution, hasValue: true, defaultValue: "1080"),
            new("monitor", "-monitor", GameCommandLineArgumentCategory.Resolution, hasValue: true, defaultValue: "1"),

            // 图形 API（互斥；force_d3d12 在绝区零等由全局 DX12 管理时会置灰且不写入配置）
            new("force_d3d11", "-force-d3d11", GameCommandLineArgumentCategory.Graphics, exclusiveGroup: "graphics"),
            new("force_d3d12", "-use-d3d12", GameCommandLineArgumentCategory.Graphics, exclusiveGroup: "graphics", alternateKey: "-force-d3d12"),
            new("force_vulkan", "-force-vulkan", GameCommandLineArgumentCategory.Graphics, exclusiveGroup: "graphics"),
            new("force_low_power", "-force-low-power-device", GameCommandLineArgumentCategory.Graphics),

            // 其他
            new("force_gfx_direct", "-force-gfx-direct", GameCommandLineArgumentCategory.Other),
            new("nolog", "-nolog", GameCommandLineArgumentCategory.Other),
            new("single_instance", "-single-instance", GameCommandLineArgumentCategory.Other),
        ];
    }


    /// <summary>
    /// 按分类分组，供 UI 绑定。
    /// </summary>
    public static ObservableCollection<GameCommandLineArgumentGroup> CreateGroups(IEnumerable<GameCommandLineArgumentOption> options)
    {
        var list = new ObservableCollection<GameCommandLineArgumentGroup>();
        foreach (IGrouping<GameCommandLineArgumentCategory, GameCommandLineArgumentOption> g in options.GroupBy(o => o.Category))
        {
            list.Add(new GameCommandLineArgumentGroup
            {
                Title = g.First().CategoryTitle,
                Options = new ObservableCollection<GameCommandLineArgumentOption>(g),
            });
        }
        return list;
    }


    /// <summary>
    /// 将命令行字符串解析到预设勾选状态，并返回无法识别的残余参数（保持原有顺序与写法）。
    /// </summary>
    public static string ApplyFromArgumentString(IReadOnlyList<GameCommandLineArgumentOption> options, string? argument)
    {
        foreach (GameCommandLineArgumentOption o in options)
        {
            o.IsSelected = false;
            if (o.HasValue)
            {
                o.Value = o.DefaultValue ?? "";
            }
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            return "";
        }

        List<string> tokens = Tokenize(argument);
        var consumed = new bool[tokens.Count];
        var custom = new List<string>();

        for (int i = 0; i < tokens.Count; i++)
        {
            if (consumed[i])
            {
                continue;
            }

            string token = tokens[i];
            GameCommandLineArgumentOption? match = null;

            // BetterGI 调度器：键后面直到下一个已知预设键之前的 token 都是组名
            match = options.FirstOrDefault(o => o.ConsumeRemainingValues && o.MatchesKey(token));
            if (match is not null)
            {
                match.IsSelected = true;
                var values = new List<string>();
                int j = i + 1;
                while (j < tokens.Count && !IsKnownOptionKey(options, tokens[j]))
                {
                    values.Add(tokens[j]);
                    consumed[j] = true;
                    j++;
                }
                match.Value = string.Join(' ', values);
                consumed[i] = true;
                continue;
            }

            // 优先匹配「键 + 取值」类，且取值符合该选项默认语义（fullscreen 0/1、window-mode exclusive/borderless）
            if (i + 1 < tokens.Count)
            {
                string next = tokens[i + 1];
                match = options.FirstOrDefault(o => o.HasValue && o.MatchesKey(token) && ValueMatchesOption(o, next));
                if (match is not null)
                {
                    match.IsSelected = true;
                    match.Value = next;
                    consumed[i] = true;
                    consumed[i + 1] = true;
                    continue;
                }

                // 通用取值键（宽高、显示器）
                match = options.FirstOrDefault(o => o.HasValue && o.MatchesKey(token) && IsGenericValueOption(o));
                if (match is not null)
                {
                    match.IsSelected = true;
                    match.Value = next;
                    consumed[i] = true;
                    consumed[i + 1] = true;
                    continue;
                }
            }

            // BetterGI 可选单值（startOneDragon [配置名]）：下一 token 不是预设键且不像开关时才吞掉
            if (i + 1 < tokens.Count)
            {
                string next = tokens[i + 1];
                match = options.FirstOrDefault(o =>
                    o.ValueOptional
                    && o.HasValue
                    && o.MatchesKey(token)
                    && !IsKnownOptionKey(options, next)
                    && !next.StartsWith('-'));
                if (match is not null)
                {
                    match.IsSelected = true;
                    match.Value = next;
                    consumed[i] = true;
                    consumed[i + 1] = true;
                    continue;
                }
            }

            // 无取值开关，或可选取值未带参数（仅 startOneDragon）
            match = options.FirstOrDefault(o =>
                o.MatchesKey(token) && (!o.HasValue || o.ValueOptional));
            if (match is not null)
            {
                match.IsSelected = true;
                consumed[i] = true;
                continue;
            }

            custom.Add(token);
            consumed[i] = true;
        }

        return string.Join(' ', custom);
    }


    /// <summary>
    /// 由勾选状态与自定义残余参数组合最终命令行。
    /// </summary>
    public static string BuildArgumentString(IEnumerable<GameCommandLineArgumentOption> options, string? customArgument)
    {
        var parts = new List<string>();
        // BetterGI 动作必须在最前，否则 BGI 会把 Unity 参数当成第一个命令而忽略 start / startOneDragon
        foreach (GameCommandLineArgumentOption o in options
                     .OrderBy(x => x.Category == GameCommandLineArgumentCategory.BetterGI ? 0 : 1))
        {
            string? fragment = o.ToArgumentFragment();
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                parts.Add(fragment);
            }
        }

        if (!string.IsNullOrWhiteSpace(customArgument))
        {
            // 自定义段再 tokenize，避免重复空白
            foreach (string t in Tokenize(customArgument))
            {
                parts.Add(t);
            }
        }

        return string.Join(' ', parts);
    }


    /// <summary>
    /// 简单空白分词；保留引号包裹的一段（若有）。
    /// </summary>
    public static List<string> Tokenize(string argument)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(argument))
        {
            return result;
        }

        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in argument)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0)
        {
            result.Add(sb.ToString());
        }
        return result;
    }


    private static bool IsGenericValueOption(GameCommandLineArgumentOption o)
    {
        return o.Id is "screen_width" or "screen_height" or "monitor";
    }


    private static bool IsKnownOptionKey(IReadOnlyList<GameCommandLineArgumentOption> options, string token)
    {
        return options.Any(o => o.MatchesKey(token));
    }


    private static bool ValueMatchesOption(GameCommandLineArgumentOption o, string value)
    {
        return o.Id switch
        {
            "windowed" => value is "0",
            "fullscreen" => value is "1",
            "window_mode_exclusive" => value.Equals("exclusive", StringComparison.OrdinalIgnoreCase),
            "window_mode_borderless" => value.Equals("borderless", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

}
