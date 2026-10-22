namespace Starward.Core.Gacha.ZZZ;

/// <summary>
/// 绝区零抽卡物品元数据在 GitHub <c>metadata</c> 分支、CNB 镜像与 jsDelivr CDN 上的路径约定。
/// 文件名与工具箱导出一致：<c>ZZZGachaInfo.{biz}.{lang}.json</c>。
/// </summary>
public static class ZZZGachaMetadataPaths
{

    /// <summary>托管元数据的 GitHub 仓库 owner（与发行说明同源）。</summary>
    public const string RepositoryOwner = "TurmoilZoom";

    /// <summary>
    /// 托管元数据的 GitHub 仓库名，即本仓库。旧名 Starward 靠 GitHub 改名重定向也能访问，
    /// 但同一 owner 下一旦再出现名为 Starward 的仓库（如 fork 上游）重定向就失效，推送和 CDN 都会指错仓库。
    /// </summary>
    public const string RepositoryName = "Moonward";

    /// <summary>元数据所在分支。</summary>
    public const string Branch = "metadata";

    /// <summary>分支内目录（相对仓库根）。</summary>
    public const string Directory = "zzz";

    /// <summary>
    /// CNB 上的同名仓库。metadata 分支每次提交后由分支上的 GitHub Actions 工作流推送过去，见 <see cref="GetCnbRawUrl"/>。
    /// </summary>
    public const string CnbRepositoryUrl = $"https://cnb.cool/{RepositoryOwner}/{RepositoryName}";


    /// <summary>
    /// 由语言包键（如 <c>nap_cn.zh-cn</c>）得到仓库内相对路径。
    /// </summary>
    /// <param name="languageKey">语言包键（biz.lang）。</param>
    /// <returns>如 <c>zzz/ZZZGachaInfo.nap_cn.zh-cn.json</c>。</returns>
    public static string GetRepositoryPath(string languageKey)
    {
        return $"{Directory}/{GetFileName(languageKey)}";
    }


    /// <summary>
    /// 由语言包键得到文件名。
    /// </summary>
    /// <param name="languageKey">语言包键（biz.lang）。</param>
    /// <returns>如 <c>ZZZGachaInfo.nap_cn.zh-cn.json</c>。</returns>
    public static string GetFileName(string languageKey)
    {
        return $"ZZZGachaInfo.{languageKey}.json";
    }


    /// <summary>
    /// 客户端拉取用的 jsDelivr URL（指向 metadata 分支）。
    /// </summary>
    /// <param name="languageKey">语言包键（如 nap_global.en-us）。</param>
    /// <returns>CDN URL。</returns>
    public static string GetJsDelivrUrl(string languageKey)
    {
        // https://cdn.jsdelivr.net/gh/user/repo@branch/path
        return $"https://cdn.jsdelivr.net/gh/{RepositoryOwner}/{RepositoryName}@{Branch}/{GetRepositoryPath(languageKey)}";
    }


    /// <summary>
    /// 客户端拉取用的 CNB 原始文件 URL（指向 metadata 分支）。
    /// </summary>
    /// <param name="languageKey">语言包键（如 nap_cn.zh-cn）。</param>
    /// <returns>CNB raw URL。</returns>
    public static string GetCnbRawUrl(string languageKey)
    {
        return $"{CnbRepositoryUrl}/-/git/raw/{Branch}/{GetRepositoryPath(languageKey)}";
    }


    /// <summary>
    /// 客户端拉取用的全部数据源，按顺序尝试，前一个失败才换下一个：
    /// <list type="number">
    /// <item>CNB：国内能直连，响应不缓存，同步完立即是最新；匿名请求限流，429 时换下一个源。</item>
    /// <item>jsDelivr：提交后会主动刷新缓存；部分国内网络在 TLS 握手时就被重置。</item>
    /// <item>GitHub raw：国内常被 DNS 污染。</item>
    /// <item>jsDelivr 的 Fastly 域名：与 jsDelivr 是同一份缓存，只是换个域名。</item>
    /// </list>
    /// </summary>
    /// <param name="languageKey">语言包键（如 nap_cn.zh-cn）。</param>
    /// <returns>数据源 URL，按尝试顺序排列。</returns>
    public static IReadOnlyList<string> GetSourceUrls(string languageKey)
    {
        string repositoryPath = GetRepositoryPath(languageKey);
        return
        [
            GetCnbRawUrl(languageKey),
            GetJsDelivrUrl(languageKey),
            $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/{Branch}/{repositoryPath}",
            $"https://fastly.jsdelivr.net/gh/{RepositoryOwner}/{RepositoryName}@{Branch}/{repositoryPath}",
        ];
    }


    /// <summary>
    /// 提交后尝试刷新 jsDelivr 缓存用的 purge URL。
    /// </summary>
    /// <param name="languageKey">语言包键。</param>
    /// <returns>purge 端点 URL。</returns>
    public static string GetJsDelivrPurgeUrl(string languageKey)
    {
        return $"https://purge.jsdelivr.net/gh/{RepositoryOwner}/{RepositoryName}@{Branch}/{GetRepositoryPath(languageKey)}";
    }


    /// <summary>GitHub 上 metadata 目录的浏览地址（给维护者/引导文案）。</summary>
    public static string GitHubBrowseUrl =>
        $"https://github.com/{RepositoryOwner}/{RepositoryName}/tree/{Branch}/{Directory}";

}
