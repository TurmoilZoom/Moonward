using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core.Gacha;

namespace Starward.Features.Gacha;

/// <summary>
/// 卡池筛选器条目（用于 GachaLogPage 的卡池筛选下拉菜单）。
/// <para>包装 <see cref="IGachaType"/>，并通过 <see cref="IsSelected"/> 提供勾选状态（勾选即在统计卡片中显示该卡池）。</para>
/// <para>筛选结果会持久化到 AppConfig，并在切换 uid / 导入数据后保持；支持全选、清除、反选等操作。</para>
/// </summary>
public partial class GachaBanner : ObservableObject
{

    /// <summary>
    /// 底层卡池类型定义（来自 Core 层各游戏的 *GachaType 实现，如 GenshinGachaType、StarRailGachaType 等）。
    /// </summary>
    public IGachaType GachaType { get; private set; }


    /// <summary>
    /// 卡池类型的整数标识（对应数据库中的 GachaType / QueryType 字段）。
    /// </summary>
    public int Value => GachaType.Value;


    /// <summary>
    /// 返回该卡池的本地化显示名称（例如“角色活动祈愿”、“群星跃迁”、“独家频段”等）。
    /// 实际调用 <see cref="IGachaType.ToLocalization"/>，从 CoreLang 资源读取。
    /// </summary>
    public string ToLocalization() => GachaType.ToLocalization();


    /// <summary>是否在卡池筛选器中勾选（勾选=显示该卡池）。绑定到复选框，变更时实时触发筛选并持久化。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }


    /// <summary>
    /// 创建一个卡池筛选条目。
    /// </summary>
    /// <param name="gachaType">来自 <see cref="GachaLogService.QueryGachaTypes"/> 的卡池类型定义。</param>
    public GachaBanner(IGachaType gachaType)
    {
        GachaType = gachaType;
    }

}
