
using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;

namespace Starward.Features.Gacha;

/// <summary>
/// 抽卡记录的扩展增强类（用于 UI 绑定与统计展示）。
/// 继承自 <see cref="GachaLogItem"/>，在原始抽卡记录基础上增加了 pity 计算、卡池内序号、UP 标记、进度百分比、聚合次数、悬停状态等扩展属性。
/// 这些属性主要由各游戏的 *GachaService.GetGachaLogItemEx 和 GetGachaTypeStats 方法填充。
/// </summary>
[INotifyPropertyChanged]
public partial class GachaLogItemEx : GachaLogItem
{

    /// <summary>
    /// Id 的字符串表示形式。
    /// </summary>
    /// <returns>Id 属性的 ToString() 结果。</returns>
    /// <remarks>不要删除，导出 Excel 时有用。</remarks>
    public string IdText => Id.ToString();

    /// <summary>
    /// 相同保底卡池（按 GachaType 分组并按 Id 排序）中的顺序编号，从 1 开始递增。
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 当前物品的“垫数”（pity），即自上次高稀有度物品（5★/S级）以来累计的抽取次数。
    /// </summary>
    /// <remarks>
    /// 在 GetGachaLogItemEx 中按卡池分组内顺序计算：每抽 +1，抽到 RankType==5（或 ZZZ 的 RankType==4）后重置为 0。
    /// 在统计卡片中还会为 4★ 单独计算 per-rarity pity 并回写此属性。
    /// </remarks>
    public int Pity { get; set; }

    /// <summary>
    /// 物品图标资源路径（通常为本地缓存图片路径）。
    /// </summary>
    /// <remarks>通过 LEFT JOIN 对应游戏的 *GachaInfo 表（如 ZZZGachaInfo、GenshinGachaInfo）获得。</remarks>
    public string Icon { get; set; }

    /// <summary>
    /// 保底进度百分比，用于 UI 进度条可视化。
    /// </summary>
    /// <returns>
    /// (double)Pity / 软保底上限 * 100。
    /// 武器活动祈愿、光锥活动跃迁、光锥联动跃迁、音擎频段、音擎回响、邦布频段的上限为 80，其余卡池为 90。
    /// </returns>
    public double Progress => (double)Pity / ((GachaType is GenshinGachaType.WeaponEventWish or StarRailGachaType.LightConeEventWarp or StarRailGachaType.LightConeCollaborationWarp or ZZZGachaType.WEngineChannel or ZZZGachaType.WEngineReverberation or ZZZGachaType.BangbooChannel) ? 80 : 90) * 100;


    /// <summary>
    /// 指针（鼠标）是否悬停在对应列表项/卡片上。
    /// </summary>
    /// <remarks>
    /// 用于 GachaStatsCard / ZZZGachaStatsCard 中的 hover 效果：
    /// 通过 BoolToVisibilityConverter / BoolToVisibilityReversedConverter 切换显示 pity 数字与物品名称。
    /// 使用 CommunityToolkit.Mvvm 的 SetProperty 实现变更通知。
    /// </remarks>
    public bool IsPointerIn { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 该具体物品（按 ItemId 聚合）的获取总次数。
    /// </summary>
    /// <remarks>在 GetGachaTypeStats / GetGachaStats 中通过 GroupBy(ItemId).Count() 计算后设置，用于物品出货频率统计列表。</remarks>
    public int ItemCount { get; set; }


    /// <summary>
    /// 当前卡池类型是否支持 UP（当期主推/限定）物品规则（即是否存在 GachaNoUp 配置）。
    /// </summary>
    /// <remarks>主要影响统计卡片中是否显示“up!” 标记相关 UI 元素。</remarks>
    public bool HasUpItem { get; set; }

    /// <summary>
    /// 对于支持非UP规则的卡池，该高稀有度物品是否为当期 UP。
    /// </summary>
    /// <remarks>
    /// true = UP（主推），false = 非UP（常驻或老版本角色/音擎）。
    /// 仅在 RankType 为高星且 hasNoUp 为 true 时计算，由抽取时间落在 GachaNoUp.NoUpTimes 区间决定。
    /// </remarks>
    public bool IsUp { get; set; }

    /// <summary>
    /// 根据 IsUp 计算的“up!” 文字不透明度。
    /// </summary>
    /// <returns>IsUp 为 true 时返回 1，否则返回 0。直接用于 XAML Opacity 绑定。</returns>
    public double UpTextOpacity => IsUp ? 1 : 0;

}
