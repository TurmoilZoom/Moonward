using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.SignIn;
using Starward.Features.Database;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord.SignIn;

/// <summary>
/// 每日签到的业务编排：拉取状态、签到、补签，并把接口返回码映射成结构化结果。
/// </summary>
internal class SignInService
{

    private readonly ILogger<SignInService> _logger;

    private readonly GameRecordService _gameRecordService;

    private readonly IMemoryCache _memoryCache;


    /// <summary>
    /// 初始化签到业务服务。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="gameRecordService">战绩门面，负责选 CN/OS Client 并发起 API。</param>
    /// <param name="memoryCache">奖励列表内存缓存。</param>
    public SignInService(ILogger<SignInService> logger, GameRecordService gameRecordService, IMemoryCache memoryCache)
    {
        _logger = logger;
        _gameRecordService = gameRecordService;
        _memoryCache = memoryCache;
    }



    /// <summary>
    /// 拉取签到卡片所需的全部状态（奖励列表 + 当前签到状态 + 补签信息）。
    /// 奖励列表按「游戏 + 服务器年月」缓存在内存与 KVT，跨月自动失效；补签信息可选（失败不影响主流程）。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签到卡片展示所需的聚合状态。</returns>
    public async Task<SignInStatus> GetSignInStatusAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        SignInRewardInfo info = await _gameRecordService.GetSignInInfoAsync(role, cancellationToken);

        SignInReward reward = await GetSignInRewardCachedAsync(role, info, cancellationToken);

        SignInResignInfo? resignInfo = null;
        try
        {
            resignInfo = await _gameRecordService.GetSignInResignInfoAsync(role, cancellationToken);
        }
        catch (miHoYoApiException ex)
        {
            // 部分游戏 / 活动没有补签信息，忽略
            _logger.LogDebug(ex, "Get resign info failed (biz {biz}, uid {uid})", role.GameBiz, role.Uid);
        }

        return new SignInStatus
        {
            Reward = reward,
            Info = info,
            ResignInfo = resignInfo,
        };
    }



    /// <summary>
    /// 执行今日签到，返回结构化结果。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签到操作结果（含已签、风控、Cookie 失效等）。</returns>
    public async Task<SignInActionResult> ClaimSignInAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        try
        {
            SignInResult result = await _gameRecordService.SignInAsync(role, cancellationToken);
            if (result is { Success: 1 } && !string.IsNullOrEmpty(result.Gt))
            {
                // 触发风控，需要极验验证，无法在客户端自动完成
                return SignInActionResult.RiskControl;
            }
            return SignInActionResult.Success;
        }
        catch (miHoYoApiException ex)
        {
            return MapReturnCode(ex.ReturnCode);
        }
    }



    /// <summary>
    /// 执行补签，返回结构化结果。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补签操作结果。</returns>
    public async Task<SignInActionResult> ClaimReSignInAsync(GameRecordRole role, CancellationToken cancellationToken = default)
    {
        try
        {
            SignInResult result = await _gameRecordService.ReSignInAsync(role, cancellationToken);
            if (result is { Success: 1 } && !string.IsNullOrEmpty(result.Gt))
            {
                return SignInActionResult.RiskControl;
            }
            return SignInActionResult.Success;
        }
        catch (miHoYoApiException ex)
        {
            return MapReturnCode(ex.ReturnCode);
        }
    }



    /// <summary>
    /// 将米游社 API retcode 映射为 UI 可消费的结构化结果。
    /// </summary>
    /// <param name="returnCode">接口返回码。</param>
    /// <returns>对应的签到操作结果。</returns>
    private static SignInActionResult MapReturnCode(int returnCode)
    {
        return returnCode switch
        {
            SignInReturnCode.AlreadySignedIn => SignInActionResult.AlreadySigned,
            SignInReturnCode.NotLoggedIn or SignInReturnCode.LoginExpired => SignInActionResult.CookieExpired,
            SignInReturnCode.NotEnoughCoin => SignInActionResult.NotEnoughCoin,
            SignInReturnCode.ResignQuotaUsedUp => SignInActionResult.ResignQuotaUsedUp,
            SignInReturnCode.NoAvailableResignDate => SignInActionResult.NoResignDate,
            SignInReturnCode.PleaseSignInFirst => SignInActionResult.PleaseSignInFirst,
            _ => SignInActionResult.Failed,
        };
    }



    /// <summary>
    /// 获取本月奖励列表，优先读内存 / KVT 缓存，跨月或缺失 today 时回源 API。
    /// </summary>
    /// <param name="role">游戏角色。</param>
    /// <param name="info">签到状态，提供服务器日期用于缓存键与月份校验。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当月奖励列表。</returns>
    private async Task<SignInReward> GetSignInRewardCachedAsync(GameRecordRole role, SignInRewardInfo info, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(info.Today, out DateOnly serverToday))
        {
            _logger.LogDebug("Sign-in info.Today is missing, skipping reward cache (biz {biz})", role.GameBiz);
            return await _gameRecordService.GetSignInRewardAsync(role, cancellationToken);
        }

        string rewardKey = GetRewardCacheKey(role.GameBiz, serverToday);
        if (TryGetCachedReward(rewardKey, serverToday.Month, out SignInReward? cached) && cached is not null)
        {
            return cached;
        }

        SignInReward reward = await _gameRecordService.GetSignInRewardAsync(role, cancellationToken);
        if (reward.Month == serverToday.Month)
        {
            SetCachedReward(rewardKey, reward);
        }

        return reward;
    }



    /// <summary>生成奖励列表缓存键（游戏 + 服务器年月）。</summary>
    private static string GetRewardCacheKey(GameBiz gameBiz, DateOnly serverToday)
        => $"sign_in_reward_{gameBiz}_{serverToday:yyyyMM}";



    /// <summary>
    /// 尝试从内存或 KVT 读取奖励列表，并校验月份一致。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="serverMonth">服务器当前月份，用于失效校验。</param>
    /// <param name="reward">命中时输出奖励列表。</param>
    /// <returns>是否命中有效缓存。</returns>
    private bool TryGetCachedReward(string key, int serverMonth, out SignInReward? reward)
    {
        if (_memoryCache.TryGetValue(key, out SignInReward? cached) && cached is not null && cached.Month == serverMonth)
        {
            reward = cached;
            return true;
        }

        if (DatabaseService.TryGetValue(key, out string? json, out _) && !string.IsNullOrEmpty(json))
        {
            try
            {
                SignInReward? fromDb = JsonSerializer.Deserialize<SignInReward>(json, AppConfig.JsonSerializerOptions);
                if (fromDb is not null && fromDb.Month == serverMonth)
                {
                    _memoryCache.Set(key, fromDb);
                    reward = fromDb;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to deserialize sign-in reward cache (key {key})", key);
            }
        }

        reward = null;
        return false;
    }



    /// <summary>
    /// 将奖励列表写入内存与 KVT 持久化缓存。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="reward">奖励列表。</param>
    private void SetCachedReward(string key, SignInReward reward)
    {
        _memoryCache.Set(key, reward);
        string json = JsonSerializer.Serialize(reward, AppConfig.JsonSerializerOptions);
        DatabaseService.SetValue(key, json);
    }


}



/// <summary>
/// 签到卡片所需的整体状态
/// </summary>
internal class SignInStatus
{
    /// <summary>本月每日奖励列表。</summary>
    public SignInReward Reward { get; init; } = default!;

    /// <summary>当前签到状态（已签天数、今日是否已签）。</summary>
    public SignInRewardInfo Info { get; init; } = default!;

    /// <summary>补签信息，部分游戏可能为 null。</summary>
    public SignInResignInfo? ResignInfo { get; init; }
}



/// <summary>
/// 签到 / 补签操作的结构化结果
/// </summary>
internal enum SignInActionResult
{
    /// <summary>签到 / 补签成功。</summary>
    Success,
    /// <summary>今日已签到。</summary>
    AlreadySigned,
    /// <summary>Cookie 失效或未登录。</summary>
    CookieExpired,
    /// <summary>触发风控，需极验验证。</summary>
    RiskControl,
    /// <summary>补签货币不足。</summary>
    NotEnoughCoin,
    /// <summary>补签次数已用尽。</summary>
    ResignQuotaUsedUp,
    /// <summary>没有可补签的日期。</summary>
    NoResignDate,
    /// <summary>需先完成今日签到才能补签。</summary>
    PleaseSignInFirst,
    /// <summary>其他未知失败。</summary>
    Failed,
}
