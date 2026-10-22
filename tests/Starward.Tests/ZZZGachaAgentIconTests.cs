using Starward.Core.Gacha.ZZZ;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 物品信息还没收录的绝区零代理人按 Id 推算头像（<see cref="ZZZGachaAgentIcon"/>）。
/// </summary>
public class ZZZGachaAgentIconTests
{

    private const string CnIcon = "https://act-webstatic.mihoyo.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1641.png";


    [Fact]
    public void AgentIcon_OnlyFourDigitIds()
    {
        Assert.True(ZZZGachaAgentIcon.IsAgentId(1641));
        Assert.False(ZZZGachaAgentIcon.IsAgentId(14141));
        Assert.False(ZZZGachaAgentIcon.IsAgentId(54021));
        Assert.False(ZZZGachaAgentIcon.IsAgentId(0));
        Assert.Null(ZZZGachaAgentIcon.GetIcon(14141, ZZZGachaAgentIcon.DefaultUrlTemplate));
    }


    [Fact]
    public void AgentIcon_TemplateFollowsKnownAgentDomain()
    {
        (long, string?)[] known =
        [
            (14141, "https://act-webstatic.hoyoverse.com/darkmatter/nap/prod_gf_cn/item_icon_uf22fe/f0b8fd7da93e59463cd5468d15771fb6.png"),
            (1011, null),
            (1021, "https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1021.png"),
        ];
        string template = ZZZGachaAgentIcon.GetUrlTemplate(known);
        Assert.Equal("https://act-webstatic.hoyoverse.com/game_record/zzzv2/role_square_avatar/role_square_avatar_1641.png", ZZZGachaAgentIcon.GetIcon(1641, template));
    }


    [Fact]
    public void AgentIcon_NoKnownAgent_UsesDefault()
    {
        string template = ZZZGachaAgentIcon.GetUrlTemplate([(14141, "https://example.com/a.png"), (1031, "not a url_1031.png")]);
        Assert.Equal(ZZZGachaAgentIcon.DefaultUrlTemplate, template);
        Assert.Equal(CnIcon, ZZZGachaAgentIcon.GetIcon(1641, template));
    }

}
