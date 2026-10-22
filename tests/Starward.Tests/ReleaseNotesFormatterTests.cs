using Starward.Features.Update;
using Starward.Setup.Core.Github;
using System;
using System.Linq;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 发行说明窗口的分组与拼接（<see cref="ReleaseNotesFormatter"/>）：去掉 release.yml 追加的下载表、按「主版本.次版本」分组、只留比最新正式版更新的预览版。
/// </summary>
public class ReleaseNotesFormatterTests
{

    private static GithubRelease Release(string tag, int month, int day, bool prerelease = false, bool draft = false, string body = "")
    {
        return new GithubRelease
        {
            TagName = tag,
            Name = tag,
            Body = body,
            Prerelease = prerelease,
            Draft = draft,
            HtmlUrl = $"https://github.com/TurmoilZoom/Moonward/releases/tag/{tag}",
            // UTC 正午：拼接时按本地时间取日期，正午在 UTC±11 内都不会跨天
            PublishedAt = new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.Zero),
        };
    }

    /// <summary>release.yml 生成的说明：tag 注释 + 空行 + 「### 下载」+ 徽章表 + 架构提示。</summary>
    private const string BodyWithDownloads = "## 新功能\r\n- 关于页显示上次检查时间\r\n\r\n### 下载\r\n\r\n<table>\r\n<thead><tr><th>架构</th><th>下载</th></tr></thead>\r\n<tbody></tbody>\r\n</table>\r\n\r\n一般电脑选 x64，骁龙等 ARM 处理器的电脑选 ARM64。\r\n";



    [Fact]
    public void Strip_RemovesAppendedDownloadTable()
    {
        string result = ReleaseNotesFormatter.StripAppendedDownloads(BodyWithDownloads);
        Assert.Equal("## 新功能\r\n- 关于页显示上次检查时间", result);
    }


    [Fact]
    public void Strip_EnglishHeading()
    {
        string result = ReleaseNotesFormatter.StripAppendedDownloads("Fixes\n\n## Downloads\n<table><tr><td>x64</td></tr></table>");
        Assert.Equal("Fixes", result);
    }


    [Fact]
    public void Strip_KeepsDownloadHeadingWithoutTable()
    {
        const string body = "### 下载\n- 去官网下载安装包\n";
        Assert.Equal(body.TrimEnd(), ReleaseNotesFormatter.StripAppendedDownloads(body));
    }


    [Fact]
    public void Strip_NullBody_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ReleaseNotesFormatter.StripAppendedDownloads(null));
    }


    [Theory]
    [InlineData("2026.10.3", "2026.10")]
    [InlineData("2026.10.3-beta1", "2026.10")]
    [InlineData("v2026.9.0", "2026.9")]
    [InlineData("2026.10-beta", "2026.10")]
    [InlineData("nightly", "nightly")]
    public void GroupKey_MajorMinor(string tag, string expected)
    {
        Assert.Equal(expected, ReleaseNotesFormatter.GetGroupKey(tag));
    }


    [Fact]
    public void Group_KeepsOnlyPrereleasesNewerThanLatestStable()
    {
        // 故意打乱顺序：分组按发布时间排，不依赖接口返回顺序
        var releases = new[]
        {
            Release("2026.9.6", 9, 25),
            Release("2026.10.2", 10, 15),
            Release("2026.10.3-beta1", 10, 20, prerelease: true),
            Release("2026.9.7-beta2", 9, 30, prerelease: true),
            Release("2026.10.2-beta1", 10, 12, prerelease: true),
            Release("2026.10.1", 10, 5),
            Release("2026.10.4", 10, 21, draft: true),
        };

        var groups = ReleaseNotesFormatter.GroupReleases(releases, includePrerelease: true);

        Assert.Equal(["2026.10", "2026.9"], groups.Select(x => x.Key));
        Assert.Equal(["2026.10.3-beta1", "2026.10.2", "2026.10.1"], groups[0].Releases.Select(x => x.TagName));
        Assert.Equal(["2026.9.6"], groups[1].Releases.Select(x => x.TagName));
        Assert.False(groups[0].IsAllPrerelease);
    }


    [Fact]
    public void Group_ExcludesPrereleasesWhenNotOnPreviewChannel()
    {
        var releases = new[]
        {
            Release("2026.10.3-beta1", 10, 20, prerelease: true),
            Release("2026.10.2", 10, 15),
        };

        var groups = ReleaseNotesFormatter.GroupReleases(releases, includePrerelease: false);

        Assert.Single(groups);
        Assert.Equal(["2026.10.2"], groups[0].Releases.Select(x => x.TagName));
    }


    [Fact]
    public void Group_TagSuffixCountsAsPrerelease()
    {
        // GitHub 上忘了勾 pre-release 的预览版，按 tag 后缀也认作预览版
        var releases = new[]
        {
            Release("2026.11.0-beta1", 10, 28),
            Release("2026.10.3", 10, 25),
        };

        var groups = ReleaseNotesFormatter.GroupReleases(releases, includePrerelease: true);

        Assert.Equal("2026.11", groups[0].Key);
        Assert.True(groups[0].IsAllPrerelease);
        Assert.Empty(ReleaseNotesFormatter.GroupReleases(releases.Take(1), includePrerelease: false));
    }


    [Fact]
    public void BuildMarkdown_SeparatesReleasesAndStripsDownloads()
    {
        var releases = new[]
        {
            Release("2026.10.3-beta1", 10, 20, prerelease: true, body: BodyWithDownloads),
            Release("2026.10.2", 10, 15, body: "修复若干问题"),
        };

        string markdown = ReleaseNotesFormatter.BuildGroupMarkdown(releases, "预览版", "发布页面");

        Assert.StartsWith("# 2026.10.3-beta1", markdown);
        Assert.Contains("<sub>预览版 · 2026-10-20 · [发布页面](https://github.com/TurmoilZoom/Moonward/releases/tag/2026.10.3-beta1)</sub>", markdown);
        Assert.Contains("<sub>2026-10-15 · [发布页面]", markdown);
        Assert.Contains("---", markdown);
        Assert.DoesNotContain("<table", markdown);
        Assert.True(markdown.IndexOf("# 2026.10.3-beta1", StringComparison.Ordinal) < markdown.IndexOf("# 2026.10.2", StringComparison.Ordinal));
    }

}
