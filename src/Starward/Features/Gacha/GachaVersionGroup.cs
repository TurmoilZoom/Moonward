using System.Collections.Generic;

namespace Starward.Features.Gacha;

/// <summary>
/// 抽卡卡片里按版本分组的一组记录：版本标题 + 该版本的记录。
/// </summary>
/// <remarks>由 XAML x:Bind 实例化绑定，属性须为普通 get/set（不能 init）。</remarks>
public sealed class GachaVersionGroup
{

    /// <summary>版本号，如 3.3；早于版本表第一个版本时为 null。</summary>
    public string? Version { get; set; }

    /// <summary>组标题，如「3.3 版本」。</summary>
    public string Header { get; set; } = "";

    /// <summary>组内记录，顺序与卡片原列表一致（新记录在前）。</summary>
    public List<GachaLogItemEx> Items { get; set; } = [];

}
