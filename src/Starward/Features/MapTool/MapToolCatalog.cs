using Starward.Core;
using Starward.Language;
using System.Collections.Generic;

namespace Starward.Features.MapTool;

/// <summary>
/// 地图工具站点目录：按游戏给出可跳转的互动地图（官方 / 第三方）。
/// </summary>
internal static class MapToolCatalog
{

    private const string GenshinOfficialCn = "https://webstatic.mihoyo.com/ys/app/interactive-map/index.html";

    private const string GenshinOfficialOs = "https://act.hoyolab.com/ys/app/interactive-map/index.html";

    private const string GenshinAppSample = "https://genshin-impact-map.appsample.com/";

    private const string GenshinKongying = "https://yuanshen.site/";

    private const string StarRailOfficialCn = "https://webstatic.mihoyo.com/sr/app/interactive-map/index.html";

    private const string StarRailOfficialOs = "https://act.hoyolab.com/sr/app/interactive-map/index.html";

    private const string StarRailAppSample = "https://star-rail-map.appsample.com/";


    /// <summary>
    /// 当前游戏可用的地图站点。原神：官方、AppSample、空荧酒馆；星穹铁道：官方、AppSample。其它游戏为空。
    /// </summary>
    /// <param name="gameBiz">当前启动页游戏。</param>
    /// <returns>按显示顺序排列的选项；不支持时为空列表。</returns>
    public static IReadOnlyList<MapToolOption> GetOptions(GameBiz gameBiz)
    {
        // 国际服用 HoYoLAB；国服 / B 服用米游社（B 服玩家同样走国服地图）。
        bool oversea = gameBiz.IsGlobalServer();
        return gameBiz.Game switch
        {
            GameBiz.hk4e =>
            [
                new MapToolOption(Lang.MapTool_Official, oversea ? GenshinOfficialOs : GenshinOfficialCn),
                new MapToolOption(Lang.MapTool_AppSample, GenshinAppSample),
                new MapToolOption(Lang.MapTool_Kongying, GenshinKongying),
            ],
            GameBiz.hkrpg =>
            [
                new MapToolOption(Lang.MapTool_Official, oversea ? StarRailOfficialOs : StarRailOfficialCn),
                new MapToolOption(Lang.MapTool_AppSample, StarRailAppSample),
            ],
            _ => [],
        };
    }

}
