using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.SignIn;
using Starward.Features.Notification;
using Starward.Features.Overlay;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord.SignIn;

/// <summary>
/// 一轮自动签到里需要通知的结果，由 <see cref="AutoSignInService"/> 在签到过程中记下。
/// </summary>
internal sealed class AutoSignInBatchReport
{

    /// <summary>本轮新签成的角色，以及签到前查到的状态（<see cref="SignInRewardInfo.TotalSignDay"/> 即今天这一签的下标）。</summary>
    public List<(GameRecordRole Role, SignInRewardInfo Info)> Signed { get; } = [];

    /// <summary>本轮因登录失效（<see cref="SignInActionResult.CookieExpired"/>）或需要验证（<see cref="SignInActionResult.RiskControl"/>）没签成的角色。</summary>
    public List<(GameRecordRole Role, SignInActionResult Reason)> Blocked { get; } = [];

}


/// <summary>
/// 按 <see cref="AppConfig.AutoSignInNotifyMode"/> 把一轮自动签到的结果发成系统通知。
/// </summary>
/// <remarks>
/// 失败只通知后台解不了的两种（登录失效、需要验证），同一账号每个服务器日最多一次：后台每 2 小时会重试，不去重会反复弹。
/// 断网等临时失败后台会自行重试，不通知。成功按轮汇总成一条，不按账号各弹一条。
/// </remarks>
internal static class AutoSignInNotifier
{

    /// <summary>记录某账号当天已通知过失败的 Setting 键前缀，值为服务器日期。</summary>
    private const string FailureNotifiedKeyPrefix = "auto_sign_in_failure_notified_";

    /// <summary>通知分组；同组同 Tag 的新通知替换通知中心里没看的旧通知。</summary>
    private const string NotificationGroup = "signin";

    /// <summary>逐个列出的账号数上限，超过就改为汇总（Toast 正文只有两行）。</summary>
    private const int MaxListedRoles = 2;


    /// <summary>
    /// 按通知方式发出本轮的失败通知与成功汇总。异常只记日志，不影响签到本身。
    /// </summary>
    /// <param name="report">本轮记下的结果。</param>
    /// <param name="signInService">签到业务服务，用于取当天奖励（读缓存的奖励列表）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task NotifyAsync(AutoSignInBatchReport report, SignInService signInService, ILogger logger, CancellationToken cancellationToken)
    {
        AutoSignInNotifyMode mode = AppConfig.AutoSignInNotifyMode;
        if (mode is AutoSignInNotifyMode.Off)
        {
            return;
        }
        try
        {
            string today = SignInSchedule.GetServerDate(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var failures = report.Blocked.Where(x => MarkFailureNotified(x.Role, today)).ToList();
            if (failures.Count > 0)
            {
                ShowFailures(failures);
            }
            if (mode is AutoSignInNotifyMode.All && report.Signed.Count > 0)
            {
                await ShowSuccessAsync(report.Signed, signInService, logger, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto sign-in notification failed.");
        }
    }


    /// <summary>
    /// 该账号今天还没通知过失败就记下并返回 true；已通知过返回 false。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    /// <param name="today">当前服务器日期（UTC+8）。</param>
    /// <returns>这次需要通知时为 true。</returns>
    private static bool MarkFailureNotified(GameRecordRole role, string today)
    {
        string key = $"{FailureNotifiedKeyPrefix}{role.GameBiz}_{role.Uid}";
        if (AppConfig.GetValue<string>(default, key) == today)
        {
            return false;
        }
        AppConfig.SetValue(today, key);
        return true;
    }


    /// <summary>
    /// 一条失败通知：账号不多时逐个列出原因，多了改为「N 个账号未能签到」加账号名单。
    /// 只有一个账号时点通知会切到该游戏。
    /// </summary>
    private static void ShowFailures(List<(GameRecordRole Role, SignInActionResult Reason)> failures)
    {
        List<string> lines = failures.Count <= MaxListedRoles
            ? failures.Select(x => string.Format(x.Reason is SignInActionResult.RiskControl ? Lang.AutoSignInNotify_NeedVerify : Lang.AutoSignInNotify_LoginExpired, Who(x.Role))).ToList()
            : [string.Format(Lang.AutoSignInNotify_FailedCount, failures.Count), string.Join(Lang.AutoSignInNotify_ListSeparator, failures.Select(x => Who(x.Role)))];
        GameRecordRole? single = failures.Count == 1 ? failures[0].Role : null;
        SystemNotificationService.Show(new SystemNotification
        {
            Title = Lang.AutoSignInNotify_FailedTitle,
            Lines = lines,
            LaunchUri = NotificationActivation.BuildOpenUri(single is null ? null : new GameBiz(single.GameBiz), single?.Uid ?? 0),
            Tag = "signin_failed",
            Group = NotificationGroup,
            SuppressPopup = IsSilent(),
        });
    }


    /// <summary>
    /// 一条成功汇总：账号不多时逐个列出「第几天、获得什么」，多了改为「已为 N 个账号签到」加奖励合计。
    /// </summary>
    private static async Task ShowSuccessAsync(List<(GameRecordRole Role, SignInRewardInfo Info)> signed, SignInService signInService, ILogger logger, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var awards = new List<(string Name, int Count)>();
        foreach ((GameRecordRole role, SignInRewardInfo info) in signed)
        {
            SignInAward? award = null;
            try
            {
                award = await signInService.GetAwardAsync(role, info, info.TotalSignDay, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 取不到奖励列表时只说第几天
                logger.LogWarning(ex, "Get sign-in award for notification failed (biz {biz}, uid {uid}).", role.GameBiz, role.Uid);
            }
            int day = info.TotalSignDay + 1;
            if (award is { Name: { Length: > 0 } name })
            {
                lines.Add(string.Format(Lang.AutoSignInNotify_SignedWithAward, Who(role), day, name, award.Count));
                awards.Add((name, award.Count));
            }
            else
            {
                lines.Add(string.Format(Lang.AutoSignInNotify_Signed, Who(role), day));
            }
        }
        if (signed.Count > MaxListedRoles)
        {
            lines = [string.Format(Lang.AutoSignInNotify_SignedCount, signed.Count)];
            if (awards.Count > 0)
            {
                string total = string.Join(Lang.AutoSignInNotify_ListSeparator, awards.GroupBy(x => x.Name).Select(g => $"{g.Key} ×{g.Sum(x => x.Count)}"));
                lines.Add(string.Format(Lang.AutoSignInNotify_AwardsTotal, total));
            }
        }
        GameRecordRole? single = signed.Count == 1 ? signed[0].Role : null;
        SystemNotificationService.Show(new SystemNotification
        {
            Title = Lang.AutoSignInNotify_SuccessTitle,
            Lines = lines,
            LaunchUri = NotificationActivation.BuildOpenUri(single is null ? null : new GameBiz(single.GameBiz), single?.Uid ?? 0),
            Tag = "signin_success",
            Group = NotificationGroup,
            SuppressPopup = IsSilent(),
        });
    }


    /// <summary>「游戏 · 昵称」，通知里区分账号用。</summary>
    private static string Who(GameRecordRole role) => $"{new GameBiz(role.GameBiz).ToGameName()} · {role.Nickname}";


    /// <summary>与便笺提醒共用「游戏运行时不弹出」：有游戏在跑时直接进通知中心。</summary>
    private static bool IsSilent() => AppConfig.DailyNoteNotifySilentWhenGameRunning && RunningGameService.GetRunningGameCount() > 0;

}
