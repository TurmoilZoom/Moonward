namespace Starward.Core.Gacha.ZZZ;

/// <summary>
/// 绝区零抽卡物品元数据在 GitHub <c>metadata</c> 分支与 jsDelivr CDN 上的路径约定。
/// 文件名与工具箱导出一致：<c>ZZZGachaInfo.{biz}.{lang}.json</c>。
/// </summary>
public static class ZZZGachaMetadataPaths
{

    /// <summary>托管元数据的 GitHub 仓库 owner（与发行说明同源）。</summary>
    public const string RepositoryOwner = "TurmoilZoom";

    /// <summary>托管元数据的 GitHub 仓库名。</summary>
    public const string RepositoryName = "Starward";

    /// <summary>元数据所在分支。</summary>
    public const string Branch = "metadata";

    /// <summary>分支内目录（相对仓库根）。</summary>
    public const string Directory = "zzz";


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
