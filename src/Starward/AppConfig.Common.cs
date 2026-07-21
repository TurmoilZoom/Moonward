using Microsoft.Win32;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Starward;

/// <summary>
/// 应用程序全局配置与状态管理类（静态部分类）。
/// 负责：
/// - 设备标识与会话标识生成
/// - 安装类型检测（便携版 / 安装版）
/// - 配置加载与持久化（注册表 或 config.ini）
/// - 依赖注入容器管理
/// - 用户设置的读写（基于数据库 Setting 表）
/// - 常用路径、语言、HttpClient 默认配置等
/// </summary>
public static partial class AppConfig
{

    /// <summary>
    /// 全局默认的 JSON 序列化选项（带缩进 + 宽松编码）。
    /// </summary>
    public static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 当前 Moonward.exe 的完整执行路径。
    /// </summary>
    public static string MoonwardExecutePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Moonward.exe");

    /// <summary>
    /// 设备唯一标识（基于 BIOS 版本 + MachineGuid + 机器名 的 MD5）。
    /// 用于上报、RPC 认证等场景。
    /// </summary>
    public static Guid DeviceId { get; private set; }

    /// <summary>
    /// 当前进程会话标识（Guid.CreateVersion7() 生成）。
    /// 每次启动生成一个新的，用于本次运行期间的唯一标识。
    /// </summary>
    public static Guid SessionId { get; private set; }


    /// <summary>
    /// 静态构造器：初始化 DeviceId 和 SessionId。
    /// </summary>
    static AppConfig()
    {
        string? systemBiosVersion = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System", "SystemBiosVersion", null) as string;
        string? machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
        DeviceId = new(MD5.HashData(Encoding.UTF8.GetBytes($"{systemBiosVersion}{machineGuid}{Environment.MachineName}")));
        SessionId = Guid.CreateVersion7();
    }



    #region Emoji（常用表情图标资源 URI）

    public static Uri EmojiPaimon = new Uri("ms-appx:///Assets/Image/UI_EmotionIcon5.png");

    public static Uri EmojiPom = new Uri("ms-appx:///Assets/Image/20008.png");

    public static Uri EmojiAI = new Uri("ms-appx:///Assets/Image/bdfd19c3bdad27a395890755bb60b162.png");

    public static Uri EmojiBangboo = new Uri("ms-appx:///Assets/Image/pamu.db6c2c7b.png");


    #endregion



}
