using CommunityToolkit.Mvvm.Messaging;
using Starward.Core;
using Starward.Core.GameRecord;
using System.Linq;

namespace Starward.Features.GameRecord.HomeRole;

/// <summary>
/// 首页便笺、签到卡片共用的「当前角色」：在任一张卡片里切换，两张卡片一起换；与米游社工具箱当前选中的角色分开保存。
/// </summary>
internal static class HomeGameRecordRole
{

    /// <summary>
    /// 角色在数据库里所属的区服：B 服账号就是国服米游社账号，按国服存。
    /// </summary>
    /// <param name="gameBiz">启动页所选区服。</param>
    /// <returns>查询角色用的区服。</returns>
    public static GameBiz GetRoleGameBiz(GameBiz gameBiz)
    {
        return gameBiz.Server is "bilibili" ? $"{gameBiz.Game}_cn" : gameBiz;
    }


    /// <summary>
    /// 首页卡片显示的角色：在首页选过的优先；没选过或那个角色已被删除时，跟随米游社工具箱当前选中的角色。
    /// </summary>
    /// <param name="service">战绩服务。</param>
    /// <param name="roleBiz">查询角色用的区服（B 服已映射为国服，见 <see cref="GetRoleGameBiz"/>）。</param>
    /// <returns>要显示的角色；该区服没有已登录的角色时为 null。</returns>
    public static GameRecordRole? Resolve(GameRecordService service, GameBiz roleBiz)
    {
        long uid = AppConfig.GetHomeRoleUid(roleBiz);
        if (uid > 0 && service.GetGameRoles(roleBiz).FirstOrDefault(x => x.Uid == uid) is GameRecordRole role)
        {
            return role;
        }
        return service.GetLastSelectGameRecordRoleOrTheFirstOne(roleBiz);
    }


    /// <summary>
    /// 把角色设为首页卡片显示的角色，并通知便笺、签到卡片重新加载。
    /// </summary>
    /// <param name="roleBiz">查询角色用的区服（B 服已映射为国服）。</param>
    /// <param name="role">选中的角色。</param>
    public static void Select(GameBiz roleBiz, GameRecordRole role)
    {
        AppConfig.SetHomeRoleUid(roleBiz, role.Uid);
        WeakReferenceMessenger.Default.Send(new HomeGameRecordRoleChangedMessage(roleBiz, role));
    }

}


/// <summary>
/// 首页卡片显示的角色换了（已保存）。
/// </summary>
/// <param name="GameBiz">角色所在区服；B 服已映射为国服。</param>
/// <param name="Role">新选中的角色。</param>
internal sealed record HomeGameRecordRoleChangedMessage(GameBiz GameBiz, GameRecordRole Role);
