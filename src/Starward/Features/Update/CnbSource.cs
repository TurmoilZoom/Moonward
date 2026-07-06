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

        return list
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .Where(x => includePrereleases || !x.Prerelease)
            .ToArray();
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