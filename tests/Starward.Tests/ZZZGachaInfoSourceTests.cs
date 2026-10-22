using Starward.Core;
using Starward.Core.Gacha.ZZZ;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 绝区零物品信息的多源回退（<see cref="ZZZGachaMetadataPaths.GetSourceUrls"/>）：CNB 优先，前一个源连接失败、限流、返回空列表、非 JSON 或 404 时换下一个。
/// </summary>
public class ZZZGachaInfoSourceTests
{

    private const string CnIcon = "https://act-webstatic.mihoyo.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1641.png";

    private const string ValidJson = $$$"""
        {"retcode":0,"message":"","data":{"game":"nap","lang":"zh-cn","list":[{"id":1641,"name":"菲欧妮","icon":"{{{CnIcon}}}","rarity":4,"element_type":201,"profession":3}]}}
        """;

    private const string EmptyListJson = """{"retcode":0,"message":"","data":{"game":"nap","lang":"zh-cn","list":[]}}""";


    /// <summary>
    /// 按主机名返回预设响应；响应为 null 时模拟连接被重置。记录请求过的地址。
    /// </summary>
    private sealed class StubHandler(Func<Uri, HttpResponseMessage?> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            HttpResponseMessage? response = respond(request.RequestUri!);
            if (response is null)
            {
                throw new HttpRequestException("The SSL connection could not be established, see inner exception.");
            }
            return Task.FromResult(response);
        }
    }


    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };


    private static (ZZZGachaClient Client, StubHandler Handler) Create(Func<Uri, HttpResponseMessage?> respond)
    {
        var handler = new StubHandler(respond);
        return (new ZZZGachaClient(new HttpClient(handler)), handler);
    }



    [Fact]
    public void SourceUrls_StartWithCnb_ThenJsDelivr()
    {
        var urls = ZZZGachaMetadataPaths.GetSourceUrls("nap_cn.zh-cn");
        Assert.Equal(
        [
            "https://cnb.cool/TurmoilZoom/Moonward/-/git/raw/metadata/zzz/ZZZGachaInfo.nap_cn.zh-cn.json",
            "https://cdn.jsdelivr.net/gh/TurmoilZoom/Moonward@metadata/zzz/ZZZGachaInfo.nap_cn.zh-cn.json",
            "https://raw.githubusercontent.com/TurmoilZoom/Moonward/metadata/zzz/ZZZGachaInfo.nap_cn.zh-cn.json",
            "https://fastly.jsdelivr.net/gh/TurmoilZoom/Moonward@metadata/zzz/ZZZGachaInfo.nap_cn.zh-cn.json",
        ], urls);
    }


    [Fact]
    public async Task FirstSourceOk_DoesNotTryOthers()
    {
        // CNB raw 实际返回 text/plain
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidJson, Encoding.UTF8, "text/plain") });
        ZZZGachaWiki wiki = await client.GetZZZGachaInfoAsync(GameBiz.nap_global, "en-us");
        Assert.Equal("https://cnb.cool/TurmoilZoom/Moonward/-/git/raw/metadata/zzz/ZZZGachaInfo.nap_global.en-us.json", wiki.SourceUrl);
        Assert.Equal(1641, Assert.Single(wiki.List).Id);
        Assert.Single(handler.Requests);
    }


    [Fact]
    public async Task CnbRateLimited_FallsBackToJsDelivr()
    {
        var (client, handler) = Create(uri => uri.Host == "cnb.cool" ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json(ValidJson));
        ZZZGachaWiki wiki = await client.GetZZZGachaInfoAsync(GameBiz.nap_cn, "zh-cn");
        Assert.Equal("https://cdn.jsdelivr.net/gh/TurmoilZoom/Moonward@metadata/zzz/ZZZGachaInfo.nap_cn.zh-cn.json", wiki.SourceUrl);
        Assert.Equal(["cnb.cool", "cdn.jsdelivr.net"], handler.Requests.Select(x => x.Host));
    }


    [Fact]
    public async Task EmptyListOrResetOrHtmlOrNotFound_TriesNextSource()
    {
        var (client, handler) = Create(uri => uri.Host switch
        {
            "cnb.cool" => Json(EmptyListJson),
            "cdn.jsdelivr.net" => null,
            "raw.githubusercontent.com" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>portal</html>", Encoding.UTF8, "text/html") },
            _ => Json(ValidJson),
        });
        ZZZGachaWiki wiki = await client.GetZZZGachaInfoAsync(GameBiz.nap_cn, "zh-cn");
        Assert.StartsWith("https://fastly.jsdelivr.net/", wiki.SourceUrl);
        Assert.Equal(4, handler.Requests.Count);
    }


    [Fact]
    public async Task NotFound_TriesNextSource()
    {
        var (client, _) = Create(uri => uri.Host == "cnb.cool" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(ValidJson));
        ZZZGachaWiki wiki = await client.GetZZZGachaInfoAsync(GameBiz.nap_cn, "zh-cn");
        Assert.StartsWith("https://cdn.jsdelivr.net/", wiki.SourceUrl);
    }


    [Fact]
    public async Task AllSourcesFail_ThrowsHttpRequestException()
    {
        var (client, handler) = Create(_ => null);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetZZZGachaInfoAsync(GameBiz.nap_cn, "zh-cn"));
        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.Equal(ZZZGachaMetadataPaths.GetSourceUrls("nap_cn.zh-cn").Count, handler.Requests.Count);
    }


    [Fact]
    public async Task CallerCanceled_DoesNotFallBack()
    {
        var (client, handler) = Create(_ => Json(ValidJson));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetZZZGachaInfoAsync(GameBiz.nap_cn, "zh-cn", cts.Token));
        Assert.Empty(handler.Requests);
    }

}
