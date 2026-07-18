using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.PlayTime;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using Vanara.PInvoke;

namespace Starward.Features.UrlProtocol;

/// <summary>
/// 管理 <c>starward://</c> 自定义 URL 协议的注册、注销与运行时处理。
/// 支持通过外部链接或快捷方式调用「启动游戏」「记录游玩时长」等功能。
/// </summary>
internal class UrlProtocolService
{



    /// <summary>
    /// 在注册表 <c>HKEY_CURRENT_USER\Software\Classes\Starward</c> 下注册 URL 协议处理器。
    /// 注册前会先调用 <see cref="UnregisterProtocol"/> 清理旧条目，避免残留无效路径。
    /// </summary>
    public static void RegisterProtocol()
    {
        UnregisterProtocol();

        // Velopack 部署中 current\Starward.exe 是稳定入口（更新时原子替换 current，路径不变），便携版与安装版一致。
        string exe = AppConfig.StarwardExecutePath;
        string command = $"""
            "{exe}" "%1"
            """;
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Classes\Starward", "", "URL:Starward Protocol");
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Classes\Starward", "URL Protocol", "");
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Classes\Starward\DefaultIcon", "", "Starward.exe,1");
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Classes\Starward\Shell\Open\Command", "", command);
    }



    /// <summary>
    /// 从注册表移除 <c>starward://</c> 协议绑定。若键不存在则静默忽略。
    /// </summary>
    public static void UnregisterProtocol()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Starward", false);
    }


    /// <summary>
    /// 生成启动游戏的 URL 协议链接，供快捷方式、配置预览或外部调用使用。
    /// </summary>
    /// <param name="biz">游戏业务标识（如 <c>hk4e_cn</c>、<c>hkrpg_global</c>）。</param>
    /// <param name="profileId">启动配置内部名（如 <c>config1</c>）；为 <see langword="null"/> 或空白时不附带 profile 查询参数（表示跟随软件当前生效配置）。</param>
    /// <param name="loginUid">登录账号的游戏角色 UID；&gt;0 时附加 <c>uid</c> 查询参数。</param>
    /// <returns>格式为 <c>starward://startgame/{game_biz}</c> 或带查询参数的完整 URL；<paramref name="biz"/> 无效时返回空字符串。</returns>
    public static string BuildStartGameUrl(GameBiz biz, string? profileId = null, long? loginUid = null)
    {
        string gameBiz = biz.ToString();
        if (string.IsNullOrWhiteSpace(gameBiz))
        {
            return "";
        }
        profileId = GameLaunchProfile.NormalizeId(profileId);
        long uid = loginUid is > 0 ? loginUid.Value : 0;
        if (string.IsNullOrEmpty(profileId))
        {
            return uid > 0
                ? $"starward://startgame/{gameBiz}?uid={uid}"
                : $"starward://startgame/{gameBiz}";
        }
        return uid > 0
            ? $"starward://startgame/{gameBiz}?profile={profileId}&uid={uid}"
            : $"starward://startgame/{gameBiz}?profile={profileId}";
    }



    /// <summary>
    /// 解析并执行 <c>starward://</c> URL 协议请求。
    /// 当前支持 <c>startgame</c>（启动游戏）与 <c>playtime</c>（记录游玩时长）两种主机名。
    /// </summary>
    /// <param name="url">完整的协议 URL 字符串（如 <c>starward://startgame/hk4e_cn?profile=config1</c>）。</param>
    /// <returns>
    /// 已成功识别并处理（含执行失败但已弹出错误提示）时返回 <see langword="true"/>；
    /// 未识别、测试模式或前置条件不满足时返回 <see langword="false"/>，由调用方继续正常启动流程。
    /// </returns>
    public static async Task<bool> HandleUrlProtocolAsync(string url)
    {
        var log = AppConfig.GetLogger<UrlProtocolService>();
        try
        {
            if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? uri))
            {
                // test 主机名留给 App.OnLaunched 打开调试窗口，此处不处理
                if (uri.Host is "test")
                {
                    return false;
                }

                // 用户数据目录未初始化时无法启动游戏或记录时长
                if (string.IsNullOrWhiteSpace(AppConfig.UserDataFolder))
                {
                    log.LogWarning("UserDataFolder is null");
                    return false;
                }

                // starward://startgame/{game_biz}?install_path=&profile=&uid=
                if (uri.Host is "startgame")
                {
                    if (GameBiz.TryParse(uri.AbsolutePath.Trim('/'), out GameBiz biz) && GameId.FromGameBiz(biz) is GameId gameId)
                    {
                        var kvs = HttpUtility.ParseQueryString(uri.Query);
                        string? installPath = kvs["install_path"];

                        // profile 参数：config1…config8（与配置文件 1…8 对应）、none（无）；
                        // 缺省（「跟随软件设置」）时按当前生效的启动方式。
                        string? profileId = kvs["profile"] ?? AppConfig.GetActiveLaunchProfileId(biz);
                        AppConfig.ResolveLaunchProfile(biz, profileId, out bool useNone, out GameLaunchProfile? profile);
                        long? loginUid = long.TryParse(kvs["uid"], out long uid) && uid > 0 ? uid : null;
                        await AppConfig.GetService<GameLauncherService>().StartGameAsync(gameId, installPath, profile, useNone, loginUid);
                    }
                    else
                    {
                        throw new ArgumentException($"Cannot parse the game_biz \"{uri.AbsolutePath.Trim('/')}\".");
                    }
                    return true;
                }

                // starward://playtime/{game_biz}?pid=
                if (uri.Host is "playtime")
                {
                    if (GameBiz.TryParse(uri.AbsolutePath.Trim('/'), out GameBiz biz) && GameId.FromGameBiz(biz) is GameId gameId)
                    {
                        var kvs = HttpUtility.ParseQueryString(uri.Query);
                        // 指定 pid 时跟踪已有进程；否则由 PlayTimeService 自行发现并记录
                        if (int.TryParse(kvs["pid"], out int pid))
                        {
                            await AppConfig.GetService<PlayTimeService>().StartProcessToLogAsync(gameId, pid);
                        }
                        else
                        {
                            await AppConfig.GetService<PlayTimeService>().StartProcessToLogAsync(gameId);
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"Cannot parse the game_biz \"{uri.AbsolutePath.Trim('/')}\".");
                    }
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            // 已识别的协议分支发生异常：记录日志、弹窗提示，并返回 true 阻止继续正常启动
            log.LogError(ex, "Handle url protocol");
            User32.MessageBox(HWND.NULL, ex.Message, "Starward");
            return true;
        }
        return false;
    }





}