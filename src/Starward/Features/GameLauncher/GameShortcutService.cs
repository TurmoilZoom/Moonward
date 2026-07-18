using Starward.Core;
using Starward.Features.UrlProtocol;
using System;
using System.IO;
using System.Text;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 生成「创建游戏快捷方式」用的 Internet 快捷方式（.url）并提供可选图标。
/// 快捷方式以 <c>URL=starward://startgame/{biz}[?profile={id}]</c> 启动游戏，
/// 依赖系统已注册 <c>starward://</c> 协议（见 <see cref="UrlProtocolService"/>）。
/// <para>
/// 之所以用 .url 而非 .lnk：Velopack 自更新每次都会扫描桌面 / 开始菜单等处所有「目标指向安装根目录」的
/// .lnk，并强制把图标重置为主程序图标（见其 <c>unsafe_update_app_manifest_lnks</c>，注释 “force icon refresh”），
/// 导致游戏图标在更新后丢失；.url 不在其扫描范围（它只扫 <c>*.lnk</c>），故图标可在更新后保留。
/// </para>
/// </summary>
public static class GameShortcutService
{


    /// <summary>
    /// 快捷方式图标来源：exe / dll / ico 文件路径 + 图标索引。
    /// </summary>
    public sealed class IconSource
    {
        /// <summary>图标资源文件路径，可包含环境变量（如 %SystemRoot%）。</summary>
        public required string Path { get; init; }

        /// <summary>图标在文件中的索引（.ico 用 0）。</summary>
        public int Index { get; init; }

        /// <summary>是否为用户上传的自定义 .ico。</summary>
        public bool IsCustom { get; init; }

        /// <summary>展开环境变量后的实际路径。</summary>
        public string ExpandedPath => Environment.ExpandEnvironmentVariables(Path);
    }


    /// <summary>
    /// 在桌面生成一个启动游戏的 Internet 快捷方式（.url），返回 .url 完整路径。
    /// 需系统已注册 <c>starward://</c> 协议方能由该快捷方式启动游戏（调用方负责确保已注册）。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="gameName">游戏显示名（用于文件名）。</param>
    /// <param name="profileId">配置文件内部名；null = 跟随软件设置（URL 不带 profile 参数）。</param>
    /// <param name="profileDisplayName">配置文件显示名（用于文件名）。</param>
    /// <param name="icon">图标来源；null 时回退到 Starward.exe 图标。</param>
    /// <param name="loginUid">配置绑定的登录账号 UID；&gt;0 时写入 URL <c>uid</c> 参数。</param>
    public static string CreateStartGameShortcut(GameBiz biz, string gameName, string? profileId, string profileDisplayName, IconSource? icon, long? loginUid = null)
    {
        string url = UrlProtocolService.BuildStartGameUrl(biz, profileId, loginUid);
        // 默认创建到桌面
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string fileName = SanitizeFileName($"{gameName} - {profileDisplayName}") + ".url";
        string urlPath = Path.Combine(dir, fileName);

        // 图标：四大游戏用各自 .ico，其它回退主程序图标；两者路径都在稳定的 current\ 目录下，更新后仍有效。
        string iconFile = icon?.ExpandedPath ?? AppConfig.StarwardExecutePath;
        int iconIndex = icon?.Index ?? 0;

        // .url 即 InternetShortcut（INI 文本）。内容（URL/图标路径）均为 ASCII，UTF-8 写出即可。
        var sb = new StringBuilder();
        sb.AppendLine("[InternetShortcut]");
        sb.AppendLine("URL=" + url);
        sb.AppendLine("IconFile=" + iconFile);
        sb.AppendLine("IconIndex=" + iconIndex);
        File.WriteAllText(urlPath, sb.ToString());
        return urlPath;
    }


    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }


    #region 游戏图标


    /// <summary>
    /// 该游戏快捷方式使用的 .ico 图标（<c>static\bh3/hk4e/hkrpg/nap.ico</c>，随程序部署在 exe 同目录 <c>static\</c> 下）。
    /// 仅四个主要游戏有对应 .ico，其它返回 null（快捷方式回退到 Starward 程序图标）。
    /// </summary>
    public static IconSource? GetGameIconSource(GameBiz biz)
    {
        string? fileName = biz.Game switch
        {
            GameBiz.bh3 => "bh3.ico",
            GameBiz.hk4e => "hk4e.ico",
            GameBiz.hkrpg => "hkrpg.ico",
            GameBiz.nap => "nap.ico",
            _ => null,
        };
        if (fileName is null)
        {
            return null;
        }
        string? dir = Path.GetDirectoryName(AppConfig.StarwardExecutePath);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }
        string path = Path.Combine(dir, "static", fileName);
        return File.Exists(path) ? new IconSource { Path = path, Index = 0 } : null;
    }


    #endregion


}
