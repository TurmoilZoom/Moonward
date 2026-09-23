using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.CloudGame;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Starward.Features.CloudGame;

/// <summary>
/// 从本机云游戏客户端读取云游戏凭证（<c>combo_token</c>）。
/// </summary>
/// <remarks>
/// <para>
/// **凭证只有这一个来源**：米游社通行证的 stoken 换不到云游戏凭证。2026-09-23 实测，stoken 按签发它的
/// <c>x-rpc-app_id</c> 绑定，米游社域签发的 stoken 在云游戏客户端的 app_id 下校验即为 -100；
/// SDK 的 <c>combo/granter/login/v2/login</c> 校验请求体里的游戏 app_id，两个游戏一律回 -114；
/// 官方桥接 <c>mdk/shield/api/loginByAuthTicket</c> 被 -464 挡死。所以想查云游戏时长，
/// 必须先装上对应的云游戏客户端并登录过一次。
/// </para>
/// <para>
/// 客户端 SDK 把 granter 登录的原始响应打进了自己的日志，形如
/// <c>{"combo_id":"0","open_id":"&lt;通行证ID&gt;","combo_token":"&lt;票&gt;",…}</c>，
/// 一次登录一条、追加写入。用户在客户端里换过账号，日志里就会有多个 <c>open_id</c>，
/// 因此这里按 <c>open_id</c> 分组、各取最后一条（最近一次登录），天然支持多通行证。
/// </para>
/// <para>
/// 只读日志、不改客户端任何文件；读出来的凭证等同云游戏账号登录态，只经 <see cref="AppConfig"/> 存本机数据库，不写日志。
/// </para>
/// </remarks>
internal sealed class CloudGameClientCredentialProvider
{

    /// <summary>
    /// SDK 日志里 granter 登录成功的响应片段。<c>combo_id</c> 在前、<c>open_id</c> 次之、<c>combo_token</c> 第三，
    /// 云·原神与云·绝区零两版客户端实测一致。
    /// </summary>
    private static readonly Regex ComboLoginRegex = new(
        """\{"combo_id":"[^"]*","open_id":"(?<openid>\d+)","combo_token":"(?<token>[^"]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>日志体积上限，超过就不读了。实测正常是几十 KB，异常膨胀时不值得为它卡住 UI 线程。</summary>
    private const long MaxLogBytes = 8 * 1024 * 1024;


    private readonly ILogger<CloudGameClientCredentialProvider> _logger;


    /// <summary>
    /// 初始化云游戏客户端凭证读取器。
    /// </summary>
    /// <param name="logger">日志。</param>
    public CloudGameClientCredentialProvider(ILogger<CloudGameClientCredentialProvider> logger)
    {
        _logger = logger;
    }


    /// <summary>
    /// 云游戏客户端在 <c>%LocalAppData%\miHoYo</c> 下的数据目录名，与卸载注册表项同名。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>目录名；该区服没有接入云游戏时返回 null。</returns>
    private static string? GetClientFolderName(GameBiz biz)
    {
        return biz.Value switch
        {
            GameBiz.hk4e_cn => "GenshinImpactCloudGame",
            GameBiz.nap_cn => "ZenlessZoneZeroCloud",
            _ => null,
        };
    }


    /// <summary>
    /// 云游戏客户端 SDK 日志的完整路径。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>日志路径；该区服没有接入云游戏时返回 null。文件不一定存在（没装客户端）。</returns>
    public static string? GetClientLogPath(GameBiz biz)
    {
        string? folder = GetClientFolderName(biz);
        if (folder is null)
        {
            return null;
        }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "miHoYo", folder, "config", "logs", "NativeSDK.log");
    }


    /// <summary>
    /// 该区服的云游戏客户端是否留下了可读的 SDK 日志。同步、只判存在，不解析内容。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>日志文件存在返回 true。</returns>
    public static bool IsClientLogPresent(GameBiz biz)
    {
        string? path = GetClientLogPath(biz);
        return !string.IsNullOrEmpty(path) && File.Exists(path);
    }


    /// <summary>
    /// 读取客户端日志里所有登录过的通行证及其最近一次的凭证。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>
    /// 按日志中出现的先后顺序排列（越靠后越新）的凭证列表，同一通行证只保留最近一条；
    /// 没装客户端、日志不可读或没有登录记录时返回空列表。本方法不抛异常。
    /// </returns>
    public List<CloudGameClientCredential> ReadCredentials(GameBiz biz)
    {
        var result = new List<CloudGameClientCredential>();
        CloudGameApiConfig? config = CloudGameApiConfig.FromGameBiz(biz);
        string? path = GetClientLogPath(biz);
        if (config is null || string.IsNullOrEmpty(path))
        {
            return result;
        }

        string text;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return result;
            }
            if (info.Length > MaxLogBytes)
            {
                _logger.LogWarning("Cloud game client log too large to scan ({GameBiz}, {Bytes} bytes).", biz, info.Length);
                return result;
            }
            // 客户端仍在运行时会持有写句柄，必须允许共享读写，否则直接 IOException
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read cloud game client log failed ({GameBiz}).", biz);
            return result;
        }

        // 同一通行证可能登录过很多次，只留最后一条；用索引表把「最近一次」原地替换掉，顺序仍按首次出现排
        var indexOf = new Dictionary<string, int>();
        foreach (Match match in ComboLoginRegex.Matches(text))
        {
            string openId = match.Groups["openid"].Value;
            string token = match.Groups["token"].Value;
            if (string.IsNullOrEmpty(openId) || string.IsNullOrEmpty(token))
            {
                continue;
            }
            var credential = new CloudGameClientCredential(openId, CloudGameClient.BuildComboToken(config, token, openId));
            if (indexOf.TryGetValue(openId, out int index))
            {
                result[index] = credential;
            }
            else
            {
                indexOf[openId] = result.Count;
                result.Add(credential);
            }
        }

        _logger.LogInformation("Cloud game client log scanned ({GameBiz}): {Count} account(s).", biz, result.Count);
        return result;
    }

}



/// <summary>
/// 从云游戏客户端日志里读到的一个通行证凭证。
/// </summary>
/// <param name="AccountId">米哈游通行证账号 ID，即日志里的 <c>open_id</c>。</param>
/// <param name="ComboToken">已拼装签名、可直接作为 <c>x-rpc-combo_token</c> 请求头的完整凭证。</param>
internal sealed record CloudGameClientCredential(string AccountId, string ComboToken);
