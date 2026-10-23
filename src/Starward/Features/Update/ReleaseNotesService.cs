using Microsoft.Extensions.Logging;
using Starward.Setup.Core;
using Starward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Update;

/// <summary>
/// 发行说明窗口与更新窗口共用的发行版本列表：落盘缓存，一小时内直接用缓存，过期后带 ETag 重新取，GitHub 不可用时退回缓存。
/// <para>
/// GitHub 匿名接口按出口 IP 每小时只有 60 次，共用代理出口时常被他人耗尽；原来每开一次窗口都要现取，耗尽后只能报错。
/// </para>
/// </summary>
internal class ReleaseNotesService
{


    private readonly ILogger<ReleaseNotesService> _logger;

    private readonly ReleaseClient _releaseClient;

    private readonly SemaphoreSlim _lock = new(1, 1);


    /// <summary>一次拉取的发行版本数，约覆盖最近几个月；更早的去 GitHub 看。</summary>
    /// <remarks>
    /// 取 GitHub 单页上限 100：预览版也占名额（约占一半，窗口里大多不显示），取 50 时只够最近四个月左右。
    /// </remarks>
    private const int ReleaseCount = 100;

    /// <summary>缓存在这段时间内直接使用，不发请求。</summary>
    private static readonly TimeSpan FreshDuration = TimeSpan.FromHours(1);

    /// <summary>单次请求的超时。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);


    public ReleaseNotesService(ILogger<ReleaseNotesService> logger, ReleaseClient releaseClient)
    {
        _logger = logger;
        _releaseClient = releaseClient;
    }


    /// <summary>缓存文件，放在数据目录的 cache 子目录（已在数据迁移白名单内）。</summary>
    private static string CacheFile => Path.Combine(AppConfig.CacheFolder, "cache", "github_releases.json");



    /// <summary>
    /// 取最近的发行版本列表（新到旧）。
    /// </summary>
    /// <param name="requiredTag">必须包含的版本（如刚检查到的新版本）；缓存里没有它时不论是否过期都重新取。为 null 时不要求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发行版本列表。</returns>
    /// <exception cref="GithubRateLimitException">GitHub 匿名访问次数用完，且没有可用的缓存。</exception>
    /// <exception cref="Exception">请求失败且没有可用的缓存。</exception>
    public async Task<List<GithubRelease>> GetReleasesAsync(string? requiredTag = null, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReleaseNotesCacheFile? cache = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            bool usable = cache is not null && (requiredTag is null || ReleaseNotesFormatter.ContainsTag(cache.Releases, requiredTag));
            // 拉取数量改过的旧缓存（如按 50 个存的）只在请求失败时兜底：不当作最新，也不带 ETag（那个 ETag 对应的是另一种请求）
            bool sameRequest = cache?.RequestedCount == ReleaseCount;
            if (usable && sameRequest && DateTimeOffset.Now - cache!.FetchedAt < FreshDuration)
            {
                return cache.Releases;
            }
            try
            {
                // 连不上 GitHub 时常常是挂起而不是立刻失败，HttpClient 默认要等 100 秒；有缓存时不该让用户干等
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);
                // 缓存里缺 requiredTag 时不带 ETag，免得 304 把缺版本的旧列表当成最新
                GithubReleaseListResult result = await _releaseClient.GetGithubReleaseListAsync(ReleaseCount, usable && sameRequest ? cache!.ETag : null, timeoutCts.Token).ConfigureAwait(false);
                if (result.NotModified && cache is not null)
                {
                    cache.FetchedAt = DateTimeOffset.Now;
                    cache.ETag = result.ETag ?? cache.ETag;
                    await WriteCacheAsync(cache, cancellationToken).ConfigureAwait(false);
                    return cache.Releases;
                }
                foreach (GithubRelease release in result.Releases)
                {
                    // 只用得到说明本身，资产与作者信息不进缓存
                    release.Assets = null!;
                    release.Author = null!;
                }
                var updated = new ReleaseNotesCacheFile { FetchedAt = DateTimeOffset.Now, ETag = result.ETag, RequestedCount = ReleaseCount, Releases = result.Releases };
                await WriteCacheAsync(updated, cancellationToken).ConfigureAwait(false);
                return updated.Releases;
            }
            catch (Exception ex) when (usable && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Fetch GitHub releases failed, use cache fetched at {time}", cache!.FetchedAt);
                return cache.Releases;
            }
        }
        finally
        {
            _lock.Release();
        }
    }



    /// <summary>
    /// 取发行说明加载失败时给用户看的提示：GitHub 限流单独说明（附恢复时间），其余按网络错误 / 未知错误。
    /// </summary>
    /// <param name="ex">加载时抛出的异常。</param>
    /// <returns>提示文本。</returns>
    public static string GetDisplayErrorMessage(Exception ex)
    {
        return ex switch
        {
            GithubRateLimitException { ResetAt: DateTimeOffset resetAt } => string.Format(Lang.ReleaseNotes_GithubRateLimitedUntil, resetAt.ToLocalTime().ToString("HH:mm")),
            GithubRateLimitException => Lang.ReleaseNotes_GithubRateLimited,
            HttpRequestException or SocketException or IOException or TaskCanceledException => Lang.Common_NetworkError,
            _ => Lang.DownloadGamePage_UnknownError,
        };
    }



    /// <summary>
    /// 读缓存文件；不存在或损坏时返回 null。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存内容。</returns>
    private async Task<ReleaseNotesCacheFile?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(CacheFile))
            {
                return null;
            }
            await using var fs = File.OpenRead(CacheFile);
            ReleaseNotesCacheFile? cache = await JsonSerializer.DeserializeAsync(fs, ReleaseNotesCacheJsonContext.Default.ReleaseNotesCacheFile, cancellationToken).ConfigureAwait(false);
            return cache?.Releases is { Count: > 0 } ? cache : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Read GitHub releases cache");
            return null;
        }
    }



    /// <summary>
    /// 写缓存文件；写不进去只记日志，不影响本次显示。
    /// </summary>
    /// <param name="cache">缓存内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WriteCacheAsync(ReleaseNotesCacheFile cache, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            string temp = CacheFile + ".tmp";
            await using (var fs = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(fs, cache, ReleaseNotesCacheJsonContext.Default.ReleaseNotesCacheFile, cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, CacheFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Write GitHub releases cache");
        }
    }


}



/// <summary>
/// 发行版本列表的落盘缓存。
/// </summary>
internal class ReleaseNotesCacheFile
{

    /// <summary>上次从 GitHub 确认列表的时间（含 304）。</summary>
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>上次响应的 ETag。</summary>
    public string? ETag { get; set; }

    /// <summary>拉取时请求的数量；与当前的 ReleaseCount 不同时缓存不当作最新。旧版缓存没有此字段，读出来是 0。</summary>
    public int RequestedCount { get; set; }

    /// <summary>发行版本列表，新到旧。</summary>
    public List<GithubRelease> Releases { get; set; } = new();

}
