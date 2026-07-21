using Microsoft.Extensions.Logging;
using Octokit;
using Starward.Core;
using Starward.Core.Gacha.ZZZ;
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
/// 维护者工具：将绝区零抽卡物品元数据 JSON 通过 GitHub API（Octokit）提交到 <c>metadata</c> 分支，
/// PAT 使用 Windows PasswordVault 加密存储；提交成功后尽力 purge jsDelivr 缓存。
/// </summary>
public sealed class ZZZGachaMetadataPublishService
{

    /// <summary>PasswordVault 资源名（仅本功能使用）。</summary>
    public const string VaultResource = "Starward.GitHub.MetadataPAT";

    /// <summary>PasswordVault 默认用户名键（与具体 GitHub 登录名无关，仅作检索键）。</summary>
    public const string VaultUserName = "pat";


    private readonly ILogger<ZZZGachaMetadataPublishService> _logger;
    private readonly HttpClient _httpClient;


    public ZZZGachaMetadataPublishService(ILogger<ZZZGachaMetadataPublishService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
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
    /// 将多个语言包以单次 commit 推送到 metadata 分支（Git Data API：blob → tree → commit → ref）。
    /// </summary>
    /// <param name="packages">语言键 → 物品列表。</param>
    /// <param name="commitMessage">提交说明；空则使用默认文案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新 commit 的短 SHA 与写入文件数。</returns>
    /// <exception cref="InvalidOperationException">未配置 PAT 或无文件可提交。</exception>
    public async Task<(string CommitSha, int FileCount)> PublishAsync(
        IReadOnlyDictionary<string, IReadOnlyList<ZZZGachaInfo>> packages,
        string? commitMessage = null,
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

            string json = SerializePackage(key, list);
            BlobReference blob = await client.Git.Blob.Create(owner, repo, new NewBlob
            {
                Content = json,
                Encoding = EncodingType.Utf8,
            }).ConfigureAwait(false);

            newTree.Tree.Add(new NewTreeItem
            {
                Path = ZZZGachaMetadataPaths.GetRepositoryPath(key),
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
        return (shortSha, languageKeys.Count);
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
