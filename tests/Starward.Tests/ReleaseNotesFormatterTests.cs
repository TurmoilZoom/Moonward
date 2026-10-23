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
    public void BuildEntries_OneSectionPerRelease()
    {
        var releases = new[]
        {
            Release("2026.10.3-beta1", 10, 20, prerelease: true, body: BodyWithDownloads),
            Release("2026.10.2", 10, 15, body: "修复图标不显示的问题 #28"),
        };

        var entries = ReleaseNotesFormatter.BuildEntries(releases);

        Assert.Equal(["2026.10.3-beta1", "2026.10.2"], entries.Select(x => x.Tag));
        Assert.Equal("2026-10-20", entries[0].Date);
        Assert.True(entries[0].IsPrerelease);
        Assert.False(entries[1].IsPrerelease);
        Assert.Equal("https://github.com/TurmoilZoom/Moonward/releases/tag/2026.10.3-beta1", entries[0].HtmlUri?.AbsoluteUri);
        // 第一段上方不画分隔线
        Assert.False(entries[0].ShowSeparator);
        Assert.True(entries[1].ShowSeparator);
        // 正文去掉下载表、#N 换成链接
        Assert.Equal("## 新功能\r\n- 关于页显示上次检查时间", entries[0].Body);
        Assert.Contains("[#28](https://github.com/TurmoilZoom/Moonward/issues/28)", entries[1].Body);
    }


    [Fact]
    public void BuildEntries_FallsBackToTagAndToleratesMissingFields()
    {
        var release = new GithubRelease { TagName = "2026.9.1", Name = "", Body = null!, HtmlUrl = null! };

        ReleaseNoteEntry entry = Assert.Single(ReleaseNotesFormatter.BuildEntries([release]));

        Assert.Equal("2026.9.1", entry.Title);
        Assert.Equal(string.Empty, entry.Date);
        Assert.Equal(string.Empty, entry.Body);
        Assert.False(entry.HasHtmlUri);
    }


    [Theory]
    [InlineData("修复图标不显示的问题 #28", "修复图标不显示的问题 [#28](https://github.com/TurmoilZoom/Moonward/issues/28)")]
    [InlineData("问题#28修复", "问题[#28](https://github.com/TurmoilZoom/Moonward/issues/28)修复")]
    [InlineData("（#5、#12）", "（[#5](https://github.com/TurmoilZoom/Moonward/issues/5)、[#12](https://github.com/TurmoilZoom/Moonward/issues/12)）")]
    public void LinkIssues_PlainReferences(string input, string expected)
    {
        Assert.Equal(expected, ReleaseNotesFormatter.LinkIssueReferences(input));
    }


    [Theory]
    [InlineData("已写成链接的 [#20](https://github.com/TurmoilZoom/Moonward/issues/20)")]
    [InlineData("### 新功能")]
    [InlineData("锚点 https://example.com/page#12 与 issue#3、HTML 实体 &#39;")]
    [InlineData("颜色 #28a745")]
    public void LinkIssues_LeavesOtherHashesAlone(string input)
    {
        Assert.Equal(input, ReleaseNotesFormatter.LinkIssueReferences(input));
    }


    [Theory]
    [InlineData("2026.10.3", true)]
    [InlineData("v2026.10.3", true)]
    [InlineData("2026.10.3-BETA1", true)]
    [InlineData("2026.10.4", false)]
    public void ContainsTag_IgnoresPrefixAndCase(string tag, bool expected)
    {
        var releases = new[] { Release("2026.10.3", 10, 22), Release("2026.10.3-beta1", 10, 20, prerelease: true) };
        Assert.Equal(expected, ReleaseNotesFormatter.ContainsTag(releases, tag));
    }

}
