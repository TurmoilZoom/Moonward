using Starward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Setup.Core;

/// <summary>
/// 从 GitHub Releases 拉取发行说明并渲染 Markdown。
/// 更新的检查/下载/安装由 Velopack 负责，此类只提供「更新弹窗」展示更新日志所需的数据。
/// </summary>
public class ReleaseClient
{

    /// <summary>
    /// 发行说明来源仓库（owner/repo）。
    /// </summary>
    public const string Repository = "TurmoilZoom/Moonward";


    private readonly HttpClient _httpClient;


    public ReleaseClient(HttpClient httpClient)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                EnableMultipleHttp2Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"{Path.GetFileNameWithoutExtension(Environment.ProcessPath)}/*");
        }
        else
        {
            _httpClient = httpClient;
        }
    }



    #region Github



    public async Task<GithubRelease?> GetGithubLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{Repository}/releases?page=1&per_page=1";
        var list = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ListGithubRelease, cancellationToken);
        return list?.FirstOrDefault();
    }



    public async Task<List<GithubRelease>> GetGithubReleaseAsync(int page, int perPage, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{Repository}/releases?page={page}&per_page={perPage}";
        var list = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ListGithubRelease, cancellationToken);
        return list ?? new List<GithubRelease>();
    }



    /// <summary>
    /// 拉取最近的发行版本列表，支持 ETag 条件请求。
    /// </summary>
    /// <param name="perPage">拉取数量（GitHub 上限 100）。</param>
    /// <param name="etag">上次响应的 ETag；为 null 时不发条件请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列表与新的 ETag；内容没变（304）时 <see cref="GithubReleaseListResult.NotModified"/> 为 true、列表为空。</returns>
    /// <exception cref="GithubRateLimitException">匿名访问次数用完。</exception>
    /// <exception cref="HttpRequestException">其他请求失败。</exception>
    public async Task<GithubReleaseListResult> GetGithubReleaseListAsync(int perPage, string? etag, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{Repository}/releases?page=1&per_page={perPage}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(etag) && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        string? newETag = response.Headers.ETag?.ToString();
        if (response.StatusCode is HttpStatusCode.NotModified)
        {
            return new GithubReleaseListResult([], newETag ?? etag, true);
        }
        ThrowIfRateLimited(response);
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync(ReleaseJsonContext.Default.ListGithubRelease, cancellationToken);
        return new GithubReleaseListResult(list ?? [], newETag, false);
    }



    /// <summary>
    /// 403 / 429 且带限流响应头时抛出 <see cref="GithubRateLimitException"/>；其他 403（如被拦截）照常交给 EnsureSuccessStatusCode。
    /// </summary>
    /// <param name="response">GitHub API 的响应。</param>
    /// <exception cref="GithubRateLimitException">匿名访问次数用完。</exception>
    private static void ThrowIfRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return;
        }
        DateTimeOffset? resetAt = null;
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            resetAt = retryAfter.Date ?? (retryAfter.Delta is TimeSpan delta ? DateTimeOffset.Now + delta : null);
        }
        bool exhausted = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0";
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out long seconds))
        {
            resetAt ??= DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        // 二级限流（429 或带 Retry-After 的 403）不一定把 Remaining 置 0
        if (exhausted || response.StatusCode is HttpStatusCode.TooManyRequests || response.Headers.RetryAfter is not null)
        {
            throw new GithubRateLimitException(response.StatusCode, resetAt);
        }
    }



    public async Task<GithubRelease?> GetGithubReleaseAsync(string tag, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{Repository}/releases/tags/{tag}";
        return await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.GithubRelease, cancellationToken);
    }



    public async Task<string> RenderGithubMarkdownAsync(string markdown, CancellationToken cancellationToken = default)
    {
        const string url = "https://api.github.com/markdown";
        var request = new GithubMarkdownRequest
        {
            Text = markdown,
            Mode = "gfm",
            Context = Repository,
        };
        var content = new StringContent(JsonSerializer.Serialize(request, ReleaseJsonContext.Default.GithubMarkdownRequest), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }



    #endregion


}



/// <summary>
/// <see cref="ReleaseClient.GetGithubReleaseListAsync"/> 的结果。
/// </summary>
/// <param name="Releases">发行版本列表，新的在前；<paramref name="NotModified"/> 时为空。</param>
/// <param name="ETag">本次响应的 ETag，下次条件请求用。</param>
/// <param name="NotModified">内容与 ETag 对应的版本相同（304）。</param>
public record GithubReleaseListResult(List<GithubRelease> Releases, string? ETag, bool NotModified);
