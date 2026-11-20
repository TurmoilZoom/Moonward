namespace Starward.Features.GameRecord.SignIn;

/// <summary>
/// 自动签到结果的系统通知方式。**枚举名存进 Setting 表，不能改名**；取值即签到卡片通知菜单项的 Tag。
/// </summary>
public enum AutoSignInNotifyMode
{
    /// <summary>不通知。</summary>
    Off,

    /// <summary>只在需要用户处理时通知：登录失效、需要验证（风控）。每个账号每个服务器日最多一次。断网等后台会自行重试的失败不通知。</summary>
    FailuresOnly,

    /// <summary>失败照上，另外每轮签到成功后发一条汇总（第几天、获得什么）。</summary>
    All,
}
