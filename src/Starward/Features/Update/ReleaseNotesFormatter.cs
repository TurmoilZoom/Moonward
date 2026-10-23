using Starward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Starward.Features.Update;

/// <summary>
/// 发行说明的分组与 Markdown 拼接。不依赖 WinUI 与数据库，单元测试直接链接本文件。
/// </summary>
public static partial class ReleaseNotesFormatter
{


    /// <summary>
    /// release.yml 追加在发行说明末尾的「### 下载」徽章表：标题后紧跟 &lt;table&gt;，一直到正文结尾。
    /// 官网 gh-pages 分支 src/utils/markdown.js 的 stripAppendedDownloads 用的是同一条规则，改 release.yml 那段的标题或格式时三处一起改。
    /// </summary>
    [GeneratedRegex(@"(?:^|\n)#{1,4}[ \t]+(?:下载|Downloads?)[ \t]*\r?\n\s*<table\b[\s\S]*$", RegexOptions.IgnoreCase)]
    private static partial Regex AppendedDownloadsRegex();



    /// <summary>
    /// 去掉 release.yml 追加的下载表。应用内由 Velopack 更新，那张指向 GitHub 安装包的徽章表只是噪音。
    /// </summary>
    /// <param name="body">发行说明正文。</param>
    /// <returns>去掉下载表后的正文；没有下载表时原样返回（去掉末尾空白）。</returns>
    public static string StripAppendedDownloads(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }
        return AppendedDownloadsRegex().Replace(body, string.Empty).TrimEnd();
    }



    /// <summary>
    /// 是否为预览版：GitHub 的 prerelease 标记，或 tag 带语义化版本的预发布后缀（如 2026.10.3-beta1）。
    /// </summary>
    /// <param name="release">发行版本。</param>
    /// <returns>是预览版为 <see langword="true"/>。</returns>
    public static bool IsPrerelease(GithubRelease release)
    {
        return release.Prerelease || (release.TagName?.Contains('-') ?? false);
    }



    /// <summary>
    /// 取分组键：tag 的「主版本.次版本」（2026.10.3-beta1 → 2026.10），即同一个月发布的版本归为一组。
    /// </summary>
    /// <param name="tagName">发行版本的 tag。</param>
    /// <returns>分组键；tag 不是「数字.数字」开头时原样返回。</returns>
    public static string GetGroupKey(string? tagName)
    {
        string tag = (tagName ?? string.Empty).Trim().TrimStart('v', 'V');
        string[] parts = tag.Split('.', 3);
        if (parts.Length >= 2 && int.TryParse(parts[0], out int major) && int.TryParse(parts[1].Split('-', '+')[0], out int minor))
        {
            return $"{major}.{minor}";
        }
        return tagName ?? string.Empty;
    }



    /// <summary>
    /// 按「主版本.次版本」把发行版本分组，组与组内都按发布时间从新到旧。
    /// <para>
    /// 预览版只保留比最新正式版更新的那几个：正式版的说明已从上一个正式版累计，之前的预览版再列出来是重复内容（与更新窗口的规则一致）。
    /// </para>
    /// </summary>
    /// <param name="releases">GitHub Releases 列表。</param>
    /// <param name="includePrerelease">是否显示预览版（加入了预览渠道，或当前就是预览版）。</param>
    /// <returns>非空分组列表，最新的组在前。</returns>
    public static List<ReleaseNotesGroup> GroupReleases(IEnumerable<GithubRelease> releases, bool includePrerelease)
    {
        var groups = new List<ReleaseNotesGroup>();
        bool newerThanLatestStable = true;
        foreach (GithubRelease release in releases.Where(x => !x.Draft).OrderByDescending(x => x.PublishedAt))
        {
            if (IsPrerelease(release))
            {
                if (!includePrerelease || !newerThanLatestStable)
                {
                    continue;
                }
            }
            else
            {
                newerThanLatestStable = false;
            }
            string key = GetGroupKey(release.TagName);
            ReleaseNotesGroup? group = groups.FirstOrDefault(x => x.Key == key);
            if (group is null)
            {
                group = new ReleaseNotesGroup { Key = key };
                groups.Add(group);
            }
            group.Releases.Add(release);
        }
        return groups;
    }



    /// <summary>
    /// 正文里的 issue / PR 编号（#28）：前面不能是 ASCII 字母数字、斜杠、&amp;、# 或 [，排除链接里的锚点、HTML 实体、标题井号和已经是链接的写法。
    /// 不用 \w / \b：.NET 的 \w 含汉字，「问题#28」「#28 修复」紧挨汉字的写法会漏掉。
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_/&#\[])#([0-9]+)(?![A-Za-z0-9_\]])")]
    private static partial Regex IssueReferenceRegex();



    /// <summary>
    /// 把正文里的 #N 换成本仓 issue 链接（GitHub 渲染时会自动做，本地渲染要自己做；issue 地址对 PR 也会跳转）。
    /// </summary>
    /// <param name="markdown">Markdown 文本。</param>
    /// <returns>替换后的文本。</returns>
    public static string LinkIssueReferences(string markdown)
    {
        return IssueReferenceRegex().Replace(markdown, m => $"[#{m.Groups[1].Value}](https://github.com/TurmoilZoom/Moonward/issues/{m.Groups[1].Value})");
    }



    /// <summary>
    /// 列表里是否有指定版本（忽略 tag 前缀 v 与大小写）。
    /// </summary>
    /// <param name="releases">发行版本列表。</param>
    /// <param name="tag">版本号或 tag，如 2026.10.3-beta1。</param>
    /// <returns>有为 <see langword="true"/>。</returns>
    public static bool ContainsTag(IEnumerable<GithubRelease> releases, string tag)
    {
        string target = tag.Trim().TrimStart('v', 'V');
        return releases.Any(x => string.Equals(x.TagName?.Trim().TrimStart('v', 'V'), target, StringComparison.OrdinalIgnoreCase));
    }



    /// <summary>
    /// 把一组发行版本拼成一份 Markdown：每个版本一个一级标题，下面一行「预览版 · 日期 · 发布页面」，版本之间用分隔线。
    /// 只用基本语法、不夹 HTML：本地的 MarkdownTextBlock 只支持少量 HTML 标签。
    /// </summary>
    /// <param name="releases">同一组内的发行版本（新到旧）。</param>
    /// <param name="previewLabel">预览版标记文本。</param>
    /// <param name="releasePageLabel">发布页面链接文本。</param>
    /// <returns>Markdown 文本。</returns>
    public static string BuildGroupMarkdown(IEnumerable<GithubRelease> releases, string previewLabel, string releasePageLabel)
    {
        var sb = new StringBuilder();
        foreach (GithubRelease release in releases)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            sb.AppendLine($"# {(string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name)}");
            sb.AppendLine();
            var meta = new List<string>();
            if (IsPrerelease(release))
            {
                meta.Add(previewLabel);
            }
            if (release.PublishedAt != default)
            {
                meta.Add(release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd"));
            }
            if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                meta.Add($"[{releasePageLabel}]({release.HtmlUrl})");
            }
            if (meta.Count > 0)
            {
                sb.AppendLine(string.Join(" · ", meta));
                sb.AppendLine();
            }
            sb.AppendLine(LinkIssueReferences(StripAppendedDownloads(release.Body)));
        }
        return sb.ToString();
    }


}



/// <summary>
/// 发行说明窗口左侧的一组版本（同一「主版本.次版本」）。
/// </summary>
public class ReleaseNotesGroup
{

    /// <summary>分组键，如 2026.10。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>副标题（发布年月或「预览版」），由窗口按当前语言填写。</summary>
    public string? Subtitle { get; set; }

    /// <summary>整组的说明正文（<see cref="ReleaseNotesFormatter.BuildGroupMarkdown"/>），由窗口按当前语言填写。</summary>
    public string Markdown { get; set; } = string.Empty;

    /// <summary>组内的发行版本，新到旧。</summary>
    public List<GithubRelease> Releases { get; } = new();

    /// <summary>组内是否全是预览版。</summary>
    public bool IsAllPrerelease => Releases.Count > 0 && Releases.All(ReleaseNotesFormatter.IsPrerelease);

    /// <summary>组内最新版本的发布时间。</summary>
    public DateTimeOffset LatestPublishedAt => Releases.Count > 0 ? Releases[0].PublishedAt : default;

}
