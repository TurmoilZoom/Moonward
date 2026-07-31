using CommunityToolkit.Mvvm.Messaging;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Features.ViewHost;
using System;
using System.Linq;

namespace Starward.Features.GameRecord;

/// <summary>
/// 战绩相关恢复入口（重新登录 / 验证账号），可在任意页面调用。
/// 不依赖 <see cref="GameRecordPage"/> 是否已加载（避免抽卡页等场景按钮无响应）。
/// </summary>
internal static class GameRecordAccountRecovery
{

    private static BattleChronicleWindow? _battleChronicleWindow;

    /// <summary>
    /// 导航到战绩页后是否应自动弹出登录菜单。
    /// 由 <see cref="RequestOpenLogin"/> 置位，<see cref="GameRecordPage"/> 在 Loaded 后消费。
    /// </summary>
    public static bool PendingOpenLogin { get; private set; }


    /// <summary>
    /// 请求重新登录：导航到战绩页并打开登录菜单。
    /// 若战绩页已在内存中会立即弹菜单；否则在页面 Loaded 后弹。
    /// </summary>
    public static void RequestOpenLogin()
    {
        PendingOpenLogin = true;
        WeakReferenceMessenger.Default.Send(new MainViewNavigateMessage(typeof(GameRecordPage)));
        // 战绩页已加载时同步通知（OnLoaded 注册后才有效）；未加载时依赖 PendingOpenLogin
        WeakReferenceMessenger.Default.Send(new GameRecordOpenLoginMessage());
    }


    /// <summary>
    /// 消费「打开登录」挂起标志；仅应在战绩页成功弹出登录菜单后调用。
    /// </summary>
    /// <returns>若此前有挂起请求则为 true。</returns>
    public static bool ConsumePendingOpenLogin()
    {
        if (!PendingOpenLogin)
        {
            return false;
        }
        PendingOpenLogin = false;
        return true;
    }


    /// <summary>
    /// 打开官方战绩 WebView 窗口以便完成账号验证（如 retcode 10035 / 10041 / 1034）。
    /// 优先使用 <paramref name="preferredRole"/>（触发风控的角色），其次按区服解析，最后才兜底任意角色。
    /// </summary>
    /// <param name="preferredBiz">优先使用的游戏区服（如 nap_cn）；为 null 时可从 preferredRole 推断。</param>
    /// <param name="preferredRole">触发错误时的角色；有 Cookie 时直接使用，避免跨账号/跨游戏打开错误战绩页。</param>
    public static void RequestVerifyAccount(GameBiz? preferredBiz = null, GameRecordRole? preferredRole = null)
    {
        GameRecordRole? role = ResolveRoleForVerify(preferredBiz, preferredRole);
        if (role is null || string.IsNullOrWhiteSpace(role.Cookie))
        {
            RequestOpenLogin();
            return;
        }

        try
        {
            if (_battleChronicleWindow?.AppWindow is null)
            {
                _battleChronicleWindow = new BattleChronicleWindow
                {
                    CurrentRole = role,
                };
            }
            else if (_battleChronicleWindow.CurrentRole?.Uid != role.Uid
                     || !string.Equals(_battleChronicleWindow.CurrentRole?.GameBiz, role.GameBiz, StringComparison.OrdinalIgnoreCase))
            {
                _battleChronicleWindow.CurrentRole = role;
            }
            _battleChronicleWindow.Activate();
        }
        catch
        {
            // 窗口创建失败时退回登录引导
            RequestOpenLogin();
        }
    }


    /// <summary>
    /// 选取用于打开战绩验证页的角色。
    /// 顺序：显式 preferredRole → preferredBiz/角色 GameBiz 下上次选中角色 → 任意有 Cookie 的角色。
    /// </summary>
    private static GameRecordRole? ResolveRoleForVerify(GameBiz? preferredBiz, GameRecordRole? preferredRole)
    {
        // 触发风控的请求角色最可靠，避免 FirstOrDefault 跨到其他账号的原神/其他游戏
        if (preferredRole is not null && !string.IsNullOrWhiteSpace(preferredRole.Cookie))
        {
            return preferredRole;
        }

        var service = AppConfig.GetService<GameRecordService>();

        GameBiz? bizHint = preferredBiz;
        if ((bizHint is null || string.IsNullOrWhiteSpace(bizHint.Value.Value))
            && preferredRole is not null
            && !string.IsNullOrWhiteSpace(preferredRole.GameBiz))
        {
            bizHint = preferredRole.GameBiz;
        }

        if (bizHint is not null && !string.IsNullOrWhiteSpace(bizHint.Value.Value))
        {
            GameBiz biz = NormalizeGameBiz(bizHint.Value);
            GameRecordRole? role = service.GetLastSelectGameRecordRoleOrTheFirstOne(biz)
                ?? service.GetLastSelectGachaSyncRoleOrTheFirstOne(biz);
            if (role is not null && !string.IsNullOrWhiteSpace(role.Cookie))
            {
                return role;
            }
        }

        // 任意有 Cookie 的角色（无上下文时的最后兜底）
        return service.GetAllGameRoles().FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Cookie));
    }


    /// <summary>
    /// B 服战绩角色在库中记为对应 _cn。
    /// </summary>
    private static GameBiz NormalizeGameBiz(GameBiz gameBiz)
    {
        return gameBiz.Value switch
        {
            GameBiz.nap_bilibili => GameBiz.nap_cn,
            GameBiz.hk4e_bilibili => GameBiz.hk4e_cn,
            GameBiz.hkrpg_bilibili => GameBiz.hkrpg_cn,
            _ => gameBiz,
        };
    }

}
