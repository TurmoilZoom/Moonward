using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Velopack.Sources;

namespace Starward.Features.Update;

/// <summary>
/// 描述 CNB Release 及其附件。
/// </summary>
internal class CnbRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public CnbReleaseAsset[] Assets { get; set; } = [];
}

/// <summary>
/// 描述 CNB Release 附件。
/// </summary>
internal class CnbReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>
/// 从 CNB Releases 获取 Velopack 更新包。
/// </summary>
internal class CnbSource : GitBase<CnbRelease>
{
    private const string CnbApiAccept = "application/vnd.cnb.api+json";

    /// <summary>
    /// 单次检查更新最多拉取 feed 的 Release 数量，与 Velopack 自带 <c>GithubSource</c> 的 per_page 取值一致。
    /// </summary>
    private const int MaxReleaseCount = 10;

    /// <inheritdoc />
    protected override (string Name, string Value)? Authorization => null;

    /// <summary>
    /// 创建 CNB 更新源。
    /// </summary>
    /// <param name="repoUrl">仓库地址，如 https://cnb.cool/owner/repo。</param>
    /// <param name="accessToken">保留以兼容 <see cref="GitBase{T}"/> 构造函数；CNB 公开 Release 无需令牌。</param>
    /// <param name="prerelease">为 true 时包含预发布版本。</param>
    /// <param name="downloader">HTTP 下载器；为 null 时使用 Velopack 默认实现。</param>
    public CnbSource(string repoUrl, string? accessToken, bool prerelease, IFileDownloader? downloader = null)
        : base(repoUrl, accessToken, prerelease, downloader)
    {
    }

    /// <inheritdoc />
    protected override async Task<CnbRelease[]> GetReleases(bool includePrereleases)
    {
        var uri = GetReleasesListUri();
        var json = await Downloader.DownloadString(uri.ToString(), GetRequestHeaders(CnbApiAccept)).ConfigureAwait(false);
        var list = JsonSerializer.Deserialize(json, CnbSourceJsonContext.Default.ListCnbRelease);
        if (list is null || list.Count == 0)
        {
            return [];
        }

        CnbRelease[] releases = list
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .Where(x => includePrereleases || !x.Prerelease)
            .ToArray();
        return TrimToRequiredReleases(releases);
    }

    /// <summary>
    /// 收敛真正需要拉取 feed 的 Release 数量。
    /// <para>
    /// <see cref="GitBase{T}.GetReleaseFeed"/> 会为**每个** Release 单独下载一次 <c>releases.{channel}.json</c>，
    /// 而 CNB 匿名接口限流为 20 次/分钟（见响应头 <c>X-Ratelimit-Limit</c>）。全量返回时一次检查更新就要打出
    /// 「1 + Release 总数」个请求，稳定触发 429，且随发版数量增长只会更糟。
    /// </para>
    /// </summary>
    /// <param name="releases">已按发布时间降序排列、并按需过滤掉预览版的列表。</param>
    /// <returns>只保留增量链所需的 Release，最多 <see cref="MaxReleaseCount"/> 个。</returns>
    private static CnbRelease[] TrimToRequiredReleases(CnbRelease[] releases)
    {
        if (releases.Length <= 1)
        {
            return releases;
        }
        if (NuGetVersion.TryParse(AppConfig.AppVersion, out NuGetVersion? current))
        {
            // 比当前版本旧的 Release 对「最新完整包 + 增量链」都没有贡献（UpdateManager 未开启降级），跳过即可省下绝大多数请求。
            CnbRelease[] newer = releases
                .Where(x => NuGetVersion.TryParse(x.TagName, out NuGetVersion? version) && version > current)
                .ToArray();
            // 已是最新版时 newer 为空，但仍需最新一个 Release 的 feed 才能判定「无更新」。
            releases = newer.Length > 0 ? newer : releases[..1];
        }
        // 兜底：版本号解析失败、或落后太多个版本时也不放任请求数膨胀。
        // 上限取 10 与 UpdateManager.MaximumDeltasBeforeFallback 一致——增量超过 10 个时它本就会回退到完整包。
        return releases.Length > MaxReleaseCount ? releases[..MaxReleaseCount] : releases;
    }

    /// <inheritdoc />
    protected override string GetAssetUrlFromName(CnbRelease release, string assetName)
    {
        if (release.Assets is null || release.Assets.Length == 0)
        {
            throw new ArgumentException($"No assets found in CNB Release '{release.Name}'.");
        }

        var asset = release.Assets.FirstOrDefault(a =>
            a.Name?.Equals(assetName, StringComparison.InvariantCultureIgnoreCase) == true);
        if (asset is null)
        {
            throw new ArgumentException($"Could not find asset called '{assetName}' in CNB Release '{release.Name}'.");
        }

        if (!string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            return asset.BrowserDownloadUrl;
        }

        if (!string.IsNullOrWhiteSpace(release.TagName))
        {
            var repoPath = RepoUri.AbsolutePath.TrimStart('/').TrimEnd('/');
            return $"https://cnb.cool/{repoPath}/-/releases/download/{release.TagName}/{assetName}";
        }

        throw new ArgumentException("Could not find a valid asset url for the specified asset.");
    }

    /// <summary>
    /// 根据仓库 URL 构建 CNB Release 列表 API 地址（主域名匿名可读）。
    /// </summary>
    /// <returns>形如 https://cnb.cool/owner/repo/-/releases?page=1&amp;page_size=100。</returns>
    protected virtual Uri GetReleasesListUri()
    {
        var repoPath = RepoUri.AbsolutePath.TrimStart('/').TrimEnd('/');
        return new Uri($"https://cnb.cool/{repoPath}/-/releases?page=1&page_size=100");
    }
}