using System;
using System.Net;
using System.Net.Http;

namespace Starward.Setup.Core.Github;

/// <summary>
/// GitHub API 匿名访问次数用完（403 / 429）。匿名配额按出口 IP 计算，每小时 60 次，共用代理出口时常被他人耗尽。
/// </summary>
public class GithubRateLimitException : HttpRequestException
{

    /// <summary>
    /// 配额恢复的时间；响应里没有 X-RateLimit-Reset / Retry-After 时为 null。
    /// </summary>
    public DateTimeOffset? ResetAt { get; }


    /// <summary>
    /// 创建限流异常。
    /// </summary>
    /// <param name="statusCode">响应状态码（403 或 429）。</param>
    /// <param name="resetAt">配额恢复的时间。</param>
    public GithubRateLimitException(HttpStatusCode statusCode, DateTimeOffset? resetAt)
        : base($"GitHub API rate limit exceeded ({(int)statusCode}), reset at {resetAt?.ToString("O") ?? "unknown"}.", null, statusCode)
    {
        ResetAt = resetAt;
    }

}
