using Microsoft.Extensions.Logging;
using Octokit;
using Starward.Core;
using Starward.Core.Gacha.ZZZ;
using Starward.Core.GameRecord;
using Starward.Features.GameRecord;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha.ZZZGachaToolbox;

/// <summary>
/// 维护者工具：用已登录的绝区零战绩账号拉取抽卡物品元数据，并通过 GitHub API（Octokit）提交到 <c>metadata</c> 分支，
/// PAT 使用 Windows PasswordVault 加密存储；提交成功后尽力 purge jsDelivr 缓存。
/// 工具箱窗口的手动操作与定时提交（<see cref="ZZZGachaMetadataAutoPublishService"/>）共用这里的实现。
/// </summary>
internal sealed class ZZZGachaMetadataPublishService
{

    /// <summary>PasswordVault 资源名（仅本功能使用）。</summary>
    public const string VaultResource = "Starward.GitHub.MetadataPAT";

    /// <summary>PasswordVault 默认用户名键（与具体 GitHub 登录名无关，仅作检索键）。</summary>
    public const string VaultUserName = "pat";


    private readonly ILogger<ZZZGachaMetadataPublishService> _logger;
    private readonly HttpClient _httpClient;
    private readonly GameRecordService _gameRecordService;


    public ZZZGachaMetadataPublishService(ILogger<ZZZGachaMetadataPublishService> logger, HttpClient httpClient, GameRecordService gameRecordService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _gameRecordService = gameRecordService;
    }


    /// <summary>本机是否已保存 GitHub PAT。</summary>
    public bool HasStoredPat => WindowsPasswordVaultStore.HasCredential(VaultResource);


    /// <summary>
    /// 将 PAT 写入 PasswordVault（覆盖旧值）。不写日志明文。
    /// </summary>
    /// <param name="pat">GitHub Personal Access Token（需 contents:write）。</param>
    public void SavePat(string pat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pat);
        WindowsPasswordVaultStore.Save(VaultResource, VaultUserName, pat.Trim());
    }


    /// <summary>删除本机已保存的 PAT。</summary>
    public void ClearPat()
    {
        WindowsPasswordVaultStore.RemoveAll(VaultResource);
    }


    /// <summary>
    /// 用已存 PAT 调用 GitHub 校验连通性，返回登录名。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>GitHub login。</returns>
    /// <exception cref="InvalidOperationException">未配置 PAT。</exception>
    /// <exception cref="AuthorizationException">PAT 无效。</exception>
    public async Task<string> ValidatePatAsync(CancellationToken cancellationToken = default)
    {
        GitHubClient client = CreateClient();
        // Octokit 无直接 CancellationToken 的 User.Current，Task.Run 不合适；调用本身可被外层取消窗口关闭
        cancellationToken.ThrowIfCancellationRequested();
        User user = await client.User.Current().ConfigureAwait(false);
        return user.Login;
    }


    /// <summary>
    /// 已存 PAT 能否写入 metadata 仓库：账号对仓库须有 push 权限；经典 PAT 还须带 <c>repo</c> 或 <c>public_repo</c> 范围。
    /// </summary>
    /// <remarks>
    /// fine-grained PAT 没有 X-OAuth-Scopes，GitHub 也不在仓库信息里给出令牌自身的 contents 权限，
    /// 这类令牌只能查到账号权限，缺 contents:write 要等真正提交时由 403 暴露。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可写入为 true。</returns>
    /// <exception cref="InvalidOperationException">未配置 PAT。</exception>
    /// <exception cref="AuthorizationException">PAT 无效。</exception>
    public async Task<bool> HasWritePermissionAsync(CancellationToken cancellationToken = default)
    {
        GitHubClient client = CreateClient();
        cancellationToken.ThrowIfCancellationRequested();
        Repository repository = await client.Repository.Get(ZZZGachaMetadataPaths.RepositoryOwner, ZZZGachaMetadataPaths.RepositoryName).ConfigureAwait(false);
        RepositoryPermissions? permissions = repository.Permissions;
        bool canPush = permissions is not null && (permissions.Push || permissions.Maintain || permissions.Admin);
        IReadOnlyList<string>? scopes = client.GetLastApiInfo()?.OauthScopes;
        bool scopeAllowed = scopes is not { Count: > 0 } || scopes.Contains("repo") || scopes.Contains("public_repo");
        return canPush && scopeAllowed;
    }


    /// <summary>
    /// 选取国服/国际服各至多一个带 Cookie 的绝区零战绩角色（优先上次同步角色）。
    /// </summary>
    /// <returns>可用角色列表（可能为空）。</returns>
    public List<GameRecordRole> PickZZZGameRecordRolesWithCookie()
    {
        var result = new List<GameRecordRole>();
        foreach (GameBiz biz in new[] { GameBiz.nap_cn, GameBiz.nap_global })
        {
            GameRecordRole? preferred = _gameRecordService.GetLastSelectGachaSyncRoleOrTheFirstOne(biz);
            if (preferred is not null && !string.IsNullOrWhiteSpace(preferred.Cookie))
            {
                result.Add(preferred);
                continue;
            }
            foreach (GameRecordRole role in _gameRecordService.GetGameRoles(biz))
            {
                if (!string.IsNullOrWhiteSpace(role.Cookie))
                {
                    result.Add(role);
                    break;
                }
            }
        }
        return result;
    }


    /// <summary>
    /// 用已登录账号拉取元数据时要发的全部请求：国服角色只拉 <c>nap_cn.zh-cn</c>，国际服角色拉 <c>nap_global</c> 下全部 UI 语言。
    /// </summary>
    /// <returns>请求列表；没有带 Cookie 的绝区零角色时为空。</returns>
    public List<ZZZGachaMetadataFetchRequest> GetFetchPlan()
    {
        var plan = new List<ZZZGachaMetadataFetchRequest>();
        foreach (GameRecordRole role in PickZZZGameRecordRolesWithCookie())
        {
            bool isHoyolab = role.GameBiz?.EndsWith("_global", StringComparison.OrdinalIgnoreCase) ?? false;
            // 目录键与 WebView 抓包路径一致：nap_cn / nap_global
            string bizKey = isHoyolab ? GameBiz.nap_global : GameBiz.nap_cn;
            List<string> languages = isHoyolab ? LanguageUtil.GetAllLanguages() : ["zh-cn"];
            foreach (string lang in languages)
            {
                plan.Add(new ZZZGachaMetadataFetchRequest(role, bizKey, lang));
            }
        }
        return plan;
    }


    /// <summary>
    /// 养成指南 / badge 接口的极验风控 retcode（与 ZZZGachaService 一致）。是 act 风控，与战绩页「验证账号」无关。
    /// </summary>
    /// <param name="retcode">接口返回码。</param>
    /// <returns>是风控时为 true。</returns>
    public static bool IsCultivateToolRiskControl(int retcode) => retcode is 10035 or 10041 or 1034 or 5003 or -3503;


    /// <summary>
    /// 发一次养成指南请求（badge → item_list + icon_info），得到一个语言包。
    /// </summary>
    /// <param name="request">要发的请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 语言包键（按接口实际返回的语言，如 <c>nap_global.en-us</c>）与物品列表；接口没有物品时列表为 null。
    /// </returns>
    /// <exception cref="miHoYoApiException">接口返回错误（风控、登录失效等）。</exception>
    public async Task<(string Key, List<ZZZGachaInfo>? List)> FetchPackageAsync(ZZZGachaMetadataFetchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ZZZGachaWiki wiki = await _gameRecordService.GetZZZGachaWikiFromCultivateToolAsync(request.Role, request.Language, cancellationToken).ConfigureAwait(false);
        string actualLang = string.IsNullOrWhiteSpace(wiki.Language)
            ? LanguageUtil.FilterLanguage(request.Language)
            : LanguageUtil.FilterLanguage(wiki.Language);
        string key = $"{request.BizKey}.{actualLang}";
        return (key, wiki.List is { Count: > 0 } ? wiki.List : null);
    }


    /// <summary>
    /// 将多个语言包以单次 commit 推送到 metadata 分支（Git Data API：blob → tree → commit → ref）。
    /// 生成的文件与分支上现有内容完全相同时不提交。
    /// </summary>
    /// <param name="packages">语言键 → 物品列表。</param>
    /// <param name="commitMessage">提交说明；空则使用默认文案。</param>
    /// <param name="preserveExistingItems">
    /// 为 true 时保留分支上已有、但这次没拉到的物品（按 Id 合并，新拉到的优先）。
    /// 无人值守的定时提交用：接口偶尔少返回一类物品时，不能把客户端已有的图标和名称一起删掉。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新 commit 的短 SHA（无变化时为 null）与写入文件数。</returns>
    /// <exception cref="InvalidOperationException">未配置 PAT 或无文件可提交。</exception>
    public async Task<ZZZGachaMetadataPublishResult> PublishAsync(
        IReadOnlyDictionary<string, IReadOnlyList<ZZZGachaInfo>> packages,
        string? commitMessage = null,
        bool preserveExistingItems = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Count == 0)
        {
            throw new InvalidOperationException("No metadata packages to publish.");
        }

        GitHubClient client = CreateClient();
        string owner = ZZZGachaMetadataPaths.RepositoryOwner;
        string repo = ZZZGachaMetadataPaths.RepositoryName;
        string branch = ZZZGachaMetadataPaths.Branch;

        cancellationToken.ThrowIfCancellationRequested();
        Reference reference = await client.Git.Reference.Get(owner, repo, $"heads/{branch}").ConfigureAwait(false);
        Commit latestCommit = await client.Git.Commit.Get(owner, repo, reference.Object.Sha).ConfigureAwait(false);

        var newTree = new NewTree
        {
            BaseTree = latestCommit.Tree.Sha,
        };

        var languageKeys = new List<string>();
        foreach ((string key, IReadOnlyList<ZZZGachaInfo> list) in packages.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (list is null || list.Count == 0)
            {
                continue;
            }

            string path = ZZZGachaMetadataPaths.GetRepositoryPath(key);
            IReadOnlyList<ZZZGachaInfo> items = preserveExistingItems
                ? await MergeWithExistingAsync(client, path, latestCommit.Sha, key, list).ConfigureAwait(false)
                : list;
            string json = SerializePackage(key, items);
            BlobReference blob = await client.Git.Blob.Create(owner, repo, new NewBlob
            {
                Content = json,
                Encoding = EncodingType.Utf8,
            }).ConfigureAwait(false);

            newTree.Tree.Add(new NewTreeItem
            {
                Path = path,
                Mode = "100644",
                Type = TreeType.Blob,
                Sha = blob.Sha,
            });
            languageKeys.Add(key);
        }

        if (newTree.Tree.Count == 0)
        {
            throw new InvalidOperationException("No non-empty metadata packages to publish.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        TreeResponse tree = await client.Git.Tree.Create(owner, repo, newTree).ConfigureAwait(false);
        // 树 SHA 只由内容决定：与分支当前的树相同说明每个文件都没变，再提交只会多一个空 commit（定时提交每天都会撞上）
        if (tree.Sha == latestCommit.Tree.Sha)
        {
            _logger.LogInformation("ZZZ gacha metadata unchanged, skip commit ({count} file(s) checked on {owner}/{repo}@{branch}).",
                languageKeys.Count, owner, repo, branch);
            return new ZZZGachaMetadataPublishResult(null, languageKeys.Count);
        }

        string message = string.IsNullOrWhiteSpace(commitMessage)
            ? $"chore(zzz): update gacha metadata ({string.Join(", ", languageKeys)})"
            : commitMessage.Trim();

        Commit commit = await client.Git.Commit.Create(owner, repo, new NewCommit(message, tree.Sha, latestCommit.Sha)).ConfigureAwait(false);
        await client.Git.Reference.Update(owner, repo, $"heads/{branch}", new ReferenceUpdate(commit.Sha)).ConfigureAwait(false);

        // 尽力刷新 CDN；失败不影响提交结果
        await TryPurgeJsDelivrAsync(languageKeys, cancellationToken).ConfigureAwait(false);

        string shortSha = commit.Sha.Length > 7 ? commit.Sha[..7] : commit.Sha;
        _logger.LogInformation("Published {count} ZZZ gacha metadata file(s) to {owner}/{repo}@{branch} ({sha})",
            languageKeys.Count, owner, repo, branch, shortSha);
        return new ZZZGachaMetadataPublishResult(shortSha, languageKeys.Count);
    }


    /// <summary>
    /// 把分支上已有、但这次没拉到的物品补回列表末尾（排序在序列化时统一做）。
    /// 文件不存在或读不懂时原样返回新列表，不阻止提交。
    /// </summary>
    /// <param name="client">GitHub 客户端。</param>
    /// <param name="path">文件在仓库内的路径。</param>
    /// <param name="commitSha">读取基准 commit，与本次提交的父 commit 一致。</param>
    /// <param name="languageKey">语言包键，仅用于日志。</param>
    /// <param name="fetched">这次拉到的物品。</param>
    /// <returns>合并后的物品列表。</returns>
    private async Task<IReadOnlyList<ZZZGachaInfo>> MergeWithExistingAsync(GitHubClient client, string path, string commitSha, string languageKey, IReadOnlyList<ZZZGachaInfo> fetched)
    {
        byte[] raw;
        try
        {
            raw = await client.Repository.Content.GetRawContentByRef(ZZZGachaMetadataPaths.RepositoryOwner, ZZZGachaMetadataPaths.RepositoryName, path, commitSha).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            return fetched;
        }

        ZZZGachaWiki? existing;
        try
        {
            existing = ZZZGachaMetadataJson.Deserialize(raw);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Existing ZZZ gacha metadata is not valid JSON, overwrite it ({key}).", languageKey);
            return fetched;
        }
        if (existing?.List is not { Count: > 0 })
        {
            return fetched;
        }

        var fetchedIds = fetched.Select(x => x.Id).ToHashSet();
        List<ZZZGachaInfo> missing = existing.List.Where(x => !fetchedIds.Contains(x.Id)).ToList();
        if (missing.Count == 0)
        {
            return fetched;
        }
        _logger.LogWarning("{count} item(s) on the metadata branch were not returned by the API, keep them ({key}): {ids}",
            missing.Count, languageKey, string.Join(",", missing.Select(x => x.Id)));
        return [.. fetched, .. missing];
    }


    /// <summary>
    /// 序列化为与 metadata 分支一致的 miHoYoApiWrapper JSON（缩进、属性名来自 JsonPropertyName）。
    /// </summary>
    private static string SerializePackage(string languageKey, IReadOnlyList<ZZZGachaInfo> list)
    {
        string lang = languageKey.Length >= 5 ? languageKey[^5..] : languageKey;
        var wrapper = new miHoYoApiWrapper<ZZZGachaWiki>
        {
            Retcode = 0,
            Message = "",
            Data = new ZZZGachaWiki
            {
                Game = GameBiz.nap,
                Language = lang,
                List = list.OrderBy(x => x.Id).ToList(),
            },
        };
        return JsonSerializer.Serialize(wrapper, AppConfig.JsonSerializerOptions);
    }


    private GitHubClient CreateClient()
    {
        if (!WindowsPasswordVaultStore.TryFindByResource(VaultResource, out _, out string? pat) || string.IsNullOrWhiteSpace(pat))
        {
            throw new InvalidOperationException("GitHub PAT is not configured.");
        }

        // 不把 PAT 写入任何日志字段
        return new GitHubClient(new ProductHeaderValue("Moonward"))
        {
            Credentials = new Credentials(pat),
        };
    }


    private async Task TryPurgeJsDelivrAsync(IEnumerable<string> languageKeys, CancellationToken cancellationToken)
    {
        foreach (string key in languageKeys)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string purgeUrl = ZZZGachaMetadataPaths.GetJsDelivrPurgeUrl(key);
                using HttpResponseMessage _ = await _httpClient.GetAsync(purgeUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "jsDelivr purge failed for {key}", key);
            }
        }
    }

}


/// <summary>
/// 一次养成指南接口请求：用哪个角色、写到哪个区服目录、要哪种语言。
/// </summary>
/// <param name="Role">带 Cookie 的绝区零战绩角色。</param>
/// <param name="BizKey">区服目录键：<c>nap_cn</c> 或 <c>nap_global</c>。</param>
/// <param name="Language">请求的语言，如 <c>en-us</c>。</param>
internal sealed record ZZZGachaMetadataFetchRequest(GameRecordRole Role, string BizKey, string Language)
{
    /// <summary>请求键（<c>biz.lang</c>），与按请求语言命名的语言包键一致。</summary>
    public string Key => $"{BizKey}.{Language}";

    /// <summary>是否走 HoYoLAB（国际服）。</summary>
    public bool IsGlobal => BizKey == GameBiz.nap_global;
}


/// <summary>
/// 提交 metadata 分支的结果。
/// </summary>
/// <param name="CommitSha">新 commit 的短 SHA；内容与分支一致、没有提交时为 null。</param>
/// <param name="FileCount">本次生成（或比对）的文件数。</param>
internal sealed record ZZZGachaMetadataPublishResult(string? CommitSha, int FileCount)
{
    /// <summary>内容与分支一致，没有产生新提交。</summary>
    public bool Unchanged => CommitSha is null;
}
