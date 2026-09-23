using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.CloudGame;
using Starward.Core.GameRecord;
using Starward.Features.GameRecord;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.CloudGame;

/// <summary>
/// 云游戏可用时长：按通行证账号查询钱包并换算成分钟。
/// </summary>
/// <remarks>
/// <para>
/// **凭证只从本机云游戏客户端读取**（见 <see cref="CloudGameClientCredentialProvider"/>），
/// 不使用米游社通行证换取——stoken 换不到云游戏凭证，两个游戏都会被 SDK 回 -114。
/// 因此用户必须先装上对应的云游戏客户端并登录过一次，本功能才有数据可查。
/// </para>
/// <para>
/// 用户在云游戏客户端里登录过几个通行证，这里就能列出几个，可自由切换查询（<see cref="GetAccountsAsync"/>）。
/// 凭证等同云游戏账号的登录态：只经 <see cref="AppConfig"/> 存本机数据库，日志只记区服、账号 ID 与时长，不输出凭证本体。
/// </para>
/// </remarks>
internal sealed class CloudGameWalletService
{

    /// <summary>查询结果在此时长内视为新鲜，反复开合弹层不重复请求。</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>接口没给兑换比例时，按官方说明「10 个货币 = 1 分钟」换算。</summary>
    private const int DefaultCoinExchange = 10;


    private readonly ILogger<CloudGameWalletService> _logger;

    private readonly CloudGameClient _client;

    private readonly CloudGameClientCredentialProvider _credentialProvider;

    private readonly GameRecordService _gameRecordService;

    /// <summary>按「区服 + 账号」缓存查询结果，切换账号不会互相顶掉。</summary>
    private readonly ConcurrentDictionary<(GameBiz Biz, string AccountId), CloudGameWalletSummary> _cache = new();


    /// <summary>
    /// 初始化云游戏时长服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="client">云游戏接口 Client。</param>
    /// <param name="credentialProvider">云游戏客户端凭证读取器。</param>
    /// <param name="gameRecordService">战绩与角色服务，仅用于把通行证 ID 显示成昵称。</param>
    public CloudGameWalletService(
        ILogger<CloudGameWalletService> logger,
        CloudGameClient client,
        CloudGameClientCredentialProvider credentialProvider,
        GameRecordService gameRecordService)
    {
        _logger = logger;
        _client = client;
        _credentialProvider = credentialProvider;
        _gameRecordService = gameRecordService;
    }


    /// <summary>
    /// 该区服是否接入了云游戏钱包查询。同步判断、不读盘不发请求。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>已接入返回 true。</returns>
    public static bool IsSupported(GameBiz biz) => CloudGameApiConfig.FromGameBiz(biz) is not null;


    /// <summary>
    /// 该区服的云游戏客户端是否装过（留下了可读的 SDK 日志）。同步、只判文件存在，
    /// 供 UI 在查询前决定是显示「请先安装并登录云游戏客户端」还是进入加载状态。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <returns>客户端日志存在，或本机已保存过该区服的凭证时返回 true。</returns>
    public static bool HasAnyCredentialSource(GameBiz biz)
    {
        if (!IsSupported(biz))
        {
            return false;
        }
        return CloudGameClientCredentialProvider.IsClientLogPresent(biz)
            || AppConfig.GetCloudGameComboTokenAccountIds(biz).Count > 0;
    }


    /// <summary>
    /// 列出该区服可查询的通行证账号：客户端日志里登录过的，并上本机已保存凭证的。
    /// </summary>
    /// <remarks>
    /// 日志里读到的凭证会顺手写回本机，这样客户端日志被清理后仍能继续查询，直到凭证本身失效。
    /// 读日志有磁盘 IO，放到线程池执行，别卡 UI 线程。
    /// </remarks>
    /// <param name="biz">游戏区服。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号列表，客户端里最近登录的排在前面；没有可用账号时为空列表。</returns>
    public async Task<List<CloudGameAccount>> GetAccountsAsync(GameBiz biz, CancellationToken cancellationToken = default)
    {
        if (!IsSupported(biz))
        {
            return [];
        }

        List<CloudGameClientCredential> credentials = await Task.Run(() => _credentialProvider.ReadCredentials(biz), cancellationToken);
        foreach (CloudGameClientCredential credential in credentials)
        {
            // 客户端里重新登录过就会换票，这里始终以日志里最新的一条为准
            if (AppConfig.GetCloudGameComboToken(biz, credential.AccountId) != credential.ComboToken)
            {
                AppConfig.SetCloudGameComboToken(biz, credential.AccountId, credential.ComboToken);
                _cache.TryRemove((biz, credential.AccountId), out _);
                _logger.LogInformation("Cloud game credential updated from client log ({GameBiz}, AccountId: {AccountId}).", biz, credential.AccountId);
            }
        }

        // 客户端日志里最近登录的排前面，其余（只在本机存过的）按账号 ID 排在后面，顺序稳定
        var accountIds = new List<string>();
        foreach (CloudGameClientCredential credential in Enumerable.Reverse(credentials))
        {
            if (!accountIds.Contains(credential.AccountId))
            {
                accountIds.Add(credential.AccountId);
            }
        }
        foreach (string stored in AppConfig.GetCloudGameComboTokenAccountIds(biz).Order(StringComparer.Ordinal))
        {
            if (!accountIds.Contains(stored))
            {
                accountIds.Add(stored);
            }
        }

        Dictionary<long, string?> nicknames = GetHyperionNicknames();
        return [.. accountIds.Select(id => new CloudGameAccount(id, long.TryParse(id, out long aid) && nicknames.TryGetValue(aid, out string? name) ? name : null))];
    }


    /// <summary>
    /// 决定这次该查哪个账号：优先用户上次选中的，其次列表第一个。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="accounts">可选账号列表。</param>
    /// <returns>选中的账号；列表为空时返回 null。</returns>
    public static CloudGameAccount? ResolveSelectedAccount(GameBiz biz, IReadOnlyList<CloudGameAccount> accounts)
    {
        if (accounts.Count == 0)
        {
            return null;
        }
        string? saved = AppConfig.GetCloudGameSelectedAccount(biz);
        return accounts.FirstOrDefault(a => a.AccountId == saved) ?? accounts[0];
    }


    /// <summary>
    /// 查询指定账号的云游戏钱包并换算成分钟，结果写入缓存。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="accountId">通行证账号 ID。</param>
    /// <param name="force">是否忽略缓存有效期强制查询。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可用时长；本机没有该账号的凭证时返回 null（由调用方显示引导，不作为错误展示）。</returns>
    /// <exception cref="NotSupportedException">该区服未接入钱包查询。</exception>
    /// <exception cref="miHoYoApiException">接口返回错误，凭证失效为 -100。</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">网络错误。</exception>
    public async Task<CloudGameWalletSummary?> GetWalletAsync(GameBiz biz, string accountId, bool force = false, CancellationToken cancellationToken = default)
    {
        CloudGameApiConfig config = CloudGameApiConfig.FromGameBiz(biz) ?? throw new NotSupportedException($"Cloud game wallet is not supported for {biz}.");
        string? token = AppConfig.GetCloudGameComboToken(biz, accountId);
        if (string.IsNullOrWhiteSpace(token))
        {
            _cache.TryRemove((biz, accountId), out _);
            return null;
        }

        if (!force
            && _cache.TryGetValue((biz, accountId), out CloudGameWalletSummary? cached)
            && DateTimeOffset.Now - cached.FetchedAt < CacheDuration)
        {
            return cached;
        }

        try
        {
            CloudGameWallet wallet = await _client.GetWalletAsync(config, token, cancellationToken);
            CloudGameWalletSummary summary = ToSummary(wallet, accountId);
            _cache[(biz, accountId)] = summary;
            _logger.LogInformation("Cloud game wallet refreshed ({GameBiz}, AccountId: {AccountId}): free {FreeMinutes} min, coin {CoinMinutes} min, play card {PlayCardSeconds} s ({PlayCardStatus}).",
                biz, accountId, summary.FreeMinutes, summary.CoinMinutes, summary.PlayCardRemainingSeconds, summary.PlayCardStatus);
            return summary;
        }
        catch
        {
            // 查询失败后不再拿旧结果冒充当前余额
            _cache.TryRemove((biz, accountId), out _);
            throw;
        }
    }


    /// <summary>
    /// 领取指定账号的当日免费时长：先调云游戏登录接口（发放由登录触发，只查钱包未必触发），再查一次钱包取发放后的余额。
    /// </summary>
    /// <param name="biz">游戏区服。</param>
    /// <param name="accountId">通行证账号 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>领取结果；本机没有该账号的凭证时返回 null。</returns>
    /// <exception cref="NotSupportedException">该区服未接入云游戏钱包。</exception>
    /// <exception cref="miHoYoApiException">接口返回错误，凭证失效为 -100。</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">网络错误。</exception>
    public async Task<CloudGameFreeTimeClaimResult?> ClaimDailyFreeTimeAsync(GameBiz biz, string accountId, CancellationToken cancellationToken = default)
    {
        CloudGameApiConfig config = CloudGameApiConfig.FromGameBiz(biz) ?? throw new NotSupportedException($"Cloud game wallet is not supported for {biz}.");
        string? token = AppConfig.GetCloudGameComboToken(biz, accountId);
        if (string.IsNullOrWhiteSpace(token))
        {
            _cache.TryRemove((biz, accountId), out _);
            return null;
        }

        CloudGameWallet wallet;
        try
        {
            await _client.LoginGamerAsync(config, token, cancellationToken);
            // 发放是异步落账的：云·原神抓包里登录后 1 秒查到的仍是发放前的余额，6 秒后才更新。
            // 这里等一下再查，让返回的余额尽量是发放后的；即使仍未落账也只影响展示，不影响已经发生的发放。
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            wallet = await _client.GetWalletAsync(config, token, cancellationToken);
        }
        catch
        {
            _cache.TryRemove((biz, accountId), out _);
            throw;
        }

        CloudGameWalletSummary summary = ToSummary(wallet, accountId);
        _cache[(biz, accountId)] = summary;
        bool reachedLimit = wallet.FreeTime is { FreeTimeLimit: > 0 } freeTime && freeTime.FreeTime >= freeTime.FreeTimeLimit;
        _logger.LogInformation("Cloud game daily free time claim done ({GameBiz}, AccountId: {AccountId}): free time {FreeMinutes} min, limit reached {ReachedLimit}.",
            biz, accountId, summary.FreeMinutes, reachedLimit);
        return new CloudGameFreeTimeClaimResult(summary, reachedLimit);
    }


    /// <summary>
    /// 米游社账号的「通行证 ID → 昵称」映射，用来把弹层里的账号显示得直观些。
    /// 查库失败不影响功能，退化成只显示 ID。
    /// </summary>
    /// <returns>映射表；取不到时为空表。</returns>
    private Dictionary<long, string?> GetHyperionNicknames()
    {
        try
        {
            // 云游戏只有国服，对应米游社（非 HoYoLAB）账号
            return _gameRecordService.GetRecordUsers(isHoyolab: false)
                .GroupBy(u => u.Uid)
                .ToDictionary(g => g.Key, g => g.First().Nickname);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Load Hyperion nicknames for cloud game accounts failed.");
            return [];
        }
    }


    /// <summary>
    /// 把钱包接口返回换算成展示数据。
    /// </summary>
    /// <param name="wallet">钱包接口返回。</param>
    /// <param name="accountId">数据所属的通行证账号 ID。</param>
    /// <returns>展示数据。</returns>
    private static CloudGameWalletSummary ToSummary(CloudGameWallet wallet, string accountId)
    {
        int exchange = wallet.Coin?.Exchange is > 0 and int rate ? rate : DefaultCoinExchange;
        return new CloudGameWalletSummary(wallet.FreeTime?.FreeTime ?? 0, (wallet.Coin?.CoinNum ?? 0) / exchange, wallet.PlayCard?.RemainingSeconds ?? 0, wallet.PlayCard?.ShortMessage?.Trim(), DateTimeOffset.Now, accountId);
    }

}



/// <summary>
/// 云游戏可查询的一个通行证账号。
/// </summary>
/// <param name="AccountId">米哈游通行证账号 ID。</param>
/// <param name="Nickname">米游社昵称；账号未在本应用登录过米游社时为 null，此时只显示 ID。</param>
internal sealed record CloudGameAccount(string AccountId, string? Nickname)
{
    /// <summary>下拉里显示的文本：有昵称显示「昵称 (ID)」，没有就只显示 ID。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? AccountId : $"{Nickname} ({AccountId})";
}



/// <summary>
/// 云游戏钱包展示数据：可用时长与畅玩卡状态。
/// </summary>
/// <param name="FreeMinutes">免费时长，单位为分钟。</param>
/// <param name="CoinMinutes">付费货币折合的时长（原神为原点、绝区零为邦邦点），单位为分钟，不足 1 分钟的零头舍去。</param>
/// <param name="PlayCardRemainingSeconds">畅玩卡剩余生效时长，单位为秒；未开通或已过期为 0。</param>
/// <param name="PlayCardStatus">服务端下发的畅玩卡状态短文案（如「未开通」），接口没给时为 null。</param>
/// <param name="FetchedAt">查询时间。</param>
/// <param name="AccountId">数据所属的米哈游通行证账号 ID。</param>
internal sealed record CloudGameWalletSummary(int FreeMinutes, int CoinMinutes, int PlayCardRemainingSeconds, string? PlayCardStatus, DateTimeOffset FetchedAt, string AccountId);



/// <summary>
/// 一次自动领取免费时长的结果。
/// </summary>
/// <param name="Summary">领取后的钱包展示数据。</param>
/// <param name="ReachedLimit">免费时长已达累积上限，本次登录不会再发放（不是失败）。</param>
internal sealed record CloudGameFreeTimeClaimResult(CloudGameWalletSummary Summary, bool ReachedLimit);
