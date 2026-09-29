using Serilog;
using Serilog.Core;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Starward.RPC;

/// <summary>
/// 本地埋点：把用户操作与任务阶段按 JSON Lines 写入日志目录下的 <c>Events_yyyyMMdd.log</c>，只落本地、不上传。
/// UI（<c>app</c>）与 RPC（<c>rpc</c>）两个进程写同一文件（Serilog 共享模式，与主日志相同），按 <c>t</c> 排序即是完整时间线。
/// 每行固定字段：<c>t</c> 时间（含时区）、<c>evt</c> 事件名、<c>proc</c> 进程、<c>pid</c>、<c>ver</c> 版本、<c>biz</c> 区服（可选），其余为事件字段。
/// </summary>
public static class Telemetry
{

    private static readonly JsonWriterOptions _writerOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static Logger? _logger;

    private static string _process = "";

    private static string _version = "";


    /// <summary>
    /// 初始化埋点文件；未初始化时 <see cref="Track"/> 什么也不做。
    /// </summary>
    /// <param name="logFolder">日志目录。与主日志放在一起，反馈问题时会被一并打包。</param>
    /// <param name="process">进程标识，<c>app</c> 或 <c>rpc</c>。</param>
    /// <param name="version">程序版本，跨版本对比时用。</param>
    public static void Initialize(string logFolder, string process, string? version)
    {
        try
        {
            _process = process;
            _version = version ?? "";
            // 内容是 JSON Lines，但扩展名用 .log：GitHub Issue 附件不接受 .jsonl，用户反馈时要能直接拖进去
            // 按天滚动：常驻进程跨 0 点也会换新文件（主日志做不到）；默认保留最近 31 个文件
            _logger = new LoggerConfiguration()
                .WriteTo.File(Path.Combine(logFolder, "Events_.log"), rollingInterval: RollingInterval.Day, shared: true, outputTemplate: "{Message:l}{NewLine}")
                .CreateLogger();
        }
        catch (Exception ex)
        {
            _logger = null;
            Log.Warning(ex, "Initialize telemetry");
        }
    }


    /// <summary>
    /// 记录一条埋点。永不抛异常，失败只丢这一条。
    /// </summary>
    /// <param name="evt">事件名，蛇形小写，如 <c>download_click</c>。</param>
    /// <param name="gameBiz">关联的区服，没有则为空。</param>
    /// <param name="fields">事件字段；值支持字符串、数字、布尔、枚举（写名称）、<see cref="TimeSpan"/>（写毫秒）、字符串集合，其余写 <c>ToString()</c>。</param>
    public static void Track(string evt, string? gameBiz = null, params ReadOnlySpan<(string Key, object? Value)> fields)
    {
        if (_logger is null)
        {
            return;
        }
        try
        {
            var buffer = new ArrayBufferWriter<byte>(256);
            using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("t", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
                writer.WriteString("evt", evt);
                writer.WriteString("proc", _process);
                writer.WriteNumber("pid", Environment.ProcessId);
                writer.WriteString("ver", _version);
                if (!string.IsNullOrWhiteSpace(gameBiz))
                {
                    writer.WriteString("biz", gameBiz);
                }
                foreach ((string key, object? value) in fields)
                {
                    writer.WritePropertyName(key);
                    WriteValue(writer, value);
                }
                writer.WriteEndObject();
            }
            _logger.Information("{Json:l}", Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
        catch { }
    }


    /// <summary>
    /// 按类型写出一个字段值。
    /// </summary>
    /// <param name="writer">JSON 写入器。</param>
    /// <param name="value">字段值。</param>
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                // NaN / Infinity 写不进 JSON 数字，会让整条丢失
                if (double.IsFinite(d))
                {
                    writer.WriteNumberValue(Math.Round(d, 3));
                }
                else
                {
                    writer.WriteNullValue();
                }
                break;
            case Enum e:
                writer.WriteStringValue(e.ToString());
                break;
            case TimeSpan ts:
                writer.WriteNumberValue((long)ts.TotalMilliseconds);
                break;
            case IEnumerable<string> list:
                writer.WriteStartArray();
                foreach (string item in list)
                {
                    writer.WriteStringValue(item);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }


}
