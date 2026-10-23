using Starward.Setup.Core;
using Starward.Setup.Core.Github;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 发行版本列表请求（<see cref="ReleaseClient.GetGithubReleaseListAsync"/>）：ETag 条件请求与 GitHub 匿名限流的识别。
/// </summary>
public class ReleaseClientRateLimitTests
{

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static (ReleaseClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new ReleaseClient(new HttpClient(handler)), handler);
    }


    [Fact]
    public async Task Ok_ReturnsListAndETag()
    {
        var (client, handler) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"tag_name":"2026.10.3","name":"2026.10.3","body":"修复","draft":false,"prerelease":false}]""", Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new("\"abc\"");
            return response;
        });

        GithubReleaseListResult result = await client.GetGithubReleaseListAsync(50, null);

        Assert.False(result.NotModified);
        Assert.Equal("2026.10.3", Assert.Single(result.Releases).TagName);
        Assert.Equal("\"abc\"", result.ETag);
        Assert.Empty(handler.LastRequest!.Headers.IfNoneMatch);
    }


    [Fact]
    public async Task NotModified_SendsIfNoneMatch()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        GithubReleaseListResult result = await client.GetGithubReleaseListAsync(50, "\"abc\"");

        Assert.True(result.NotModified);
        Assert.Empty(result.Releases);
        Assert.Equal("\"abc\"", result.ETag);
        Assert.Equal("\"abc\"", Assert.Single(handler.LastRequest!.Headers.IfNoneMatch).Tag);
    }


    [Fact]
    public async Task Forbidden_RemainingZero_ThrowsRateLimitWithResetTime()
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(1792750619);
        var (client, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString());
            return response;
        });

        var ex = await Assert.ThrowsAsync<GithubRateLimitException>(() => client.GetGithubReleaseListAsync(50, null));

        Assert.Equal(reset, ex.ResetAt);
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }


    [Fact]
    public async Task TooManyRequests_RetryAfter_ThrowsRateLimit()
    {
        var (client, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60));
            return response;
        });

        var ex = await Assert.ThrowsAsync<GithubRateLimitException>(() => client.GetGithubReleaseListAsync(50, null));

        Assert.NotNull(ex.ResetAt);
    }


    [Fact]
    public async Task Forbidden_WithoutRateLimitHeaders_IsPlainHttpError()
    {
        // 被拦截等其他原因的 403 不当成限流，免得给出「请在几点后重试」的误导提示
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetGithubReleaseListAsync(50, null));

        Assert.IsNotType<GithubRateLimitException>(ex);
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

}
