namespace Starward.Core.GameRecord.SignIn;

/// <summary>
/// 签到接口的已知返回码（米游社 / HoYoLAB）
/// </summary>
public static class SignInReturnCode
{

    /// <summary>
    /// 成功
    /// </summary>
    public const int Success = 0;


    /// <summary>
    /// 今日已签到
    /// </summary>
    public const int AlreadySignedIn = -5003;


    /// <summary>
    /// 未登录 / 登录态失效。
    /// </summary>
    public const int NotLoggedIn = -100;


    /// <summary>
    /// 登录态失效（旧接口返回码）。
    /// </summary>
    public const int LoginExpired = -111;


    /// <summary>
    /// 接口需要登录 Cookie。
    /// </summary>
    public const int LoginRequired = 10001;


    /// <summary>
    /// 补签次数已用尽
    /// </summary>
    public const int ResignQuotaUsedUp = -5005;


    /// <summary>
    /// 没有可补签的日期
    /// </summary>
    public const int NoAvailableResignDate = -5008;


    /// <summary>
    /// 请先完成今日签到
    /// </summary>
    public const int PleaseSignInFirst = -5007;


    /// <summary>
    /// 补签货币不足
    /// </summary>
    public const int NotEnoughCoin = -5014;

}
