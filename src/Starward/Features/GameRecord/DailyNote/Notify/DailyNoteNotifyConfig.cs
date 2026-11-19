using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Features.GameRecord.DailyNote.Notify;

/// <summary>
/// 一个账号（战绩角色）的便笺提醒配置。整体序列化成 JSON 存进 Setting 表的一个键，见 <see cref="DailyNoteNotifyConfigStore"/>。
/// </summary>
public sealed class DailyNoteNotifyConfig
{

    /// <summary>各项目的规则；没出现的项目视为未开启。</summary>
    [JsonPropertyName("rules")]
    public List<DailyNoteNotifyRule> Rules { get; set; } = [];


    /// <summary>
    /// 取项目的规则，没有时新建一条（未开启）加进去。
    /// </summary>
    /// <param name="item">便笺项目。</param>
    /// <returns>该项目的规则。</returns>
    public DailyNoteNotifyRule GetOrAddRule(DailyNoteNotifyItem item)
    {
        DailyNoteNotifyRule? rule = Rules.FirstOrDefault(x => x.Item == item);
        if (rule is null)
        {
            rule = new DailyNoteNotifyRule { Item = item };
            Rules.Add(rule);
        }
        return rule;
    }


    /// <summary>是否有开启的规则。</summary>
    [JsonIgnore]
    public bool HasEnabledRule => Rules.Any(x => x.Enabled);

}


/// <summary>
/// 一个便笺项目的提醒规则。
/// </summary>
public sealed class DailyNoteNotifyRule
{

    /// <summary>便笺项目。</summary>
    [JsonPropertyName("item")]
    public DailyNoteNotifyItem Item { get; set; }

    /// <summary>是否开启。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>达到多少时提醒；null 表示上限（满了提醒），见 <see cref="DailyNoteNotifyEvaluator.ResolveThreshold"/>。</summary>
    [JsonPropertyName("threshold")]
    public int? Threshold { get; set; }

}


/// <summary>
/// 一个账号的提醒状态：哪些项目已经提醒过、且之后一直没掉回阈值以下。与配置分开存，界面保存配置时不会把后台写的状态盖掉。
/// </summary>
public sealed class DailyNoteNotifyState
{

    /// <summary>已提醒过的项目。</summary>
    [JsonPropertyName("notified")]
    public List<DailyNoteNotifyItem> Notified { get; set; } = [];

}


[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DailyNoteNotifyConfig))]
[JsonSerializable(typeof(DailyNoteNotifyState))]
internal partial class DailyNoteNotifyJsonContext : JsonSerializerContext;


/// <summary>
/// 便笺提醒配置与状态的读写，存 Setting 表：配置键 <c>daily_note_notify_{biz}_{uid}</c>，状态键 <c>daily_note_notify_state_{biz}_{uid}</c>。
/// biz 用战绩角色自身的区服（B 服角色记为国服）。
/// </summary>
internal static class DailyNoteNotifyConfigStore
{

    private const string ConfigKeyPrefix = "daily_note_notify_";

    private const string StateKeyPrefix = "daily_note_notify_state_";


    /// <summary>
    /// 读取账号的提醒配置；没配置过或解析失败时返回空配置。
    /// </summary>
    /// <param name="biz">战绩角色的区服。</param>
    /// <param name="uid">角色 uid。</param>
    /// <returns>配置对象，永不为 null。</returns>
    public static DailyNoteNotifyConfig Load(string biz, long uid)
    {
        return Read($"{ConfigKeyPrefix}{biz}_{uid}", DailyNoteNotifyJsonContext.Default.DailyNoteNotifyConfig) ?? new DailyNoteNotifyConfig();
    }


    /// <summary>
    /// 保存账号的提醒配置。
    /// </summary>
    /// <param name="biz">战绩角色的区服。</param>
    /// <param name="uid">角色 uid。</param>
    /// <param name="config">要保存的配置。</param>
    public static void Save(string biz, long uid, DailyNoteNotifyConfig config)
    {
        AppConfig.SetValue(JsonSerializer.Serialize(config, DailyNoteNotifyJsonContext.Default.DailyNoteNotifyConfig), $"{ConfigKeyPrefix}{biz}_{uid}");
    }


    /// <summary>
    /// 读取账号的提醒状态；没有或解析失败时返回空状态。
    /// </summary>
    /// <param name="biz">战绩角色的区服。</param>
    /// <param name="uid">角色 uid。</param>
    /// <returns>状态对象，永不为 null。</returns>
    public static DailyNoteNotifyState LoadState(string biz, long uid)
    {
        return Read($"{StateKeyPrefix}{biz}_{uid}", DailyNoteNotifyJsonContext.Default.DailyNoteNotifyState) ?? new DailyNoteNotifyState();
    }


    /// <summary>
    /// 保存账号的提醒状态。
    /// </summary>
    /// <param name="biz">战绩角色的区服。</param>
    /// <param name="uid">角色 uid。</param>
    /// <param name="state">要保存的状态。</param>
    public static void SaveState(string biz, long uid, DailyNoteNotifyState state)
    {
        AppConfig.SetValue(JsonSerializer.Serialize(state, DailyNoteNotifyJsonContext.Default.DailyNoteNotifyState), $"{StateKeyPrefix}{biz}_{uid}");
    }


    /// <summary>
    /// 读一个 JSON 设置键。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="key">设置键。</param>
    /// <param name="typeInfo">源生成的类型信息。</param>
    /// <returns>解析结果；键不存在或解析失败时为 null。</returns>
    private static T? Read<T>(string key, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
    {
        string? json = AppConfig.GetValue<string>(default, key);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (Exception ex)
        {
            AppConfig.GetLogger<DailyNoteNotifyConfig>().LogWarning(ex, "Parse daily note notify setting failed ({key}).", key);
            return null;
        }
    }

}
