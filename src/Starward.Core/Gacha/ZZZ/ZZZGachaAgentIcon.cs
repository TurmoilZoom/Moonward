using System.Globalization;

namespace Starward.Core.Gacha.ZZZ;

/// <summary>
/// 按物品 Id 推算绝区零代理人图标地址。
/// 代理人（4 位 Id）的方形头像地址只由 Id 决定，物品信息还没收录新代理人时（版本更新当天、元数据源连不上）可以先用推算的地址；
/// 音擎、邦布（5 位 Id）的图标是哈希路径，推不出来。
/// </summary>
public static class ZZZGachaAgentIcon
{

    /// <summary>地址模板里代表物品 Id 的占位符。</summary>
    private const string IdPlaceholder = "{id}";

    /// <summary>
    /// 没有已收录的代理人可参照时使用的地址模板（国服域名，海外同样能访问）。
    /// </summary>
    public const string DefaultUrlTemplate = $"https://act-webstatic.mihoyo.com/game_record/zzzv2/role_square_avatar/role_square_avatar_{IdPlaceholder}.png";


    /// <summary>
    /// 物品 Id 是否属于代理人。
    /// </summary>
    /// <param name="itemId">物品 Id。</param>
    /// <returns>4 位 Id 返回 true。</returns>
    public static bool IsAgentId(long itemId)
    {
        return itemId is >= 1000 and <= 9999;
    }


    /// <summary>
    /// 从已收录的代理人图标里取地址模板，让推算的地址与当前元数据一致（国服 mihoyo.com、国际服 hoyoverse.com）。
    /// </summary>
    /// <param name="knownItems">已知物品的 Id 与图标地址，图标可为空。</param>
    /// <returns>含 <c>{id}</c> 占位符的地址模板；找不到可参照的代理人时返回 <see cref="DefaultUrlTemplate"/>。</returns>
    public static string GetUrlTemplate(IEnumerable<(long Id, string? Icon)> knownItems)
    {
        foreach ((long id, string? icon) in knownItems)
        {
            if (!IsAgentId(id) || string.IsNullOrWhiteSpace(icon) || !Uri.TryCreate(icon, UriKind.Absolute, out _))
            {
                continue;
            }
            string suffix = $"_{id}.png";
            if (icon.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return $"{icon[..^suffix.Length]}_{IdPlaceholder}.png";
            }
        }
        return DefaultUrlTemplate;
    }


    /// <summary>
    /// 推算代理人图标地址。
    /// </summary>
    /// <param name="itemId">物品 Id。</param>
    /// <param name="urlTemplate"><see cref="GetUrlTemplate"/> 返回的地址模板。</param>
    /// <returns>代理人返回推算的地址，其他物品返回 null。</returns>
    public static string? GetIcon(long itemId, string urlTemplate)
    {
        return IsAgentId(itemId) ? urlTemplate.Replace(IdPlaceholder, itemId.ToString(CultureInfo.InvariantCulture)) : null;
    }

}
