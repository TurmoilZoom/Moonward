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
    /// 当前是否存在已 Loaded 且已注册消息的战绩页实例。
    /// 由 <see cref="GameRecordPage"/> 在注册/卸载时维护，用于避免「已在工具箱时再 Navigate 同页」导致实例重建与登录跳转竞态。
    /// </summary>
    private static bool _gameRecordPageAlive;

    /// <summary>
    /// 跨页导航到战绩页后是否应自动打开登录。
    /// 仅在战绩页尚未存活时由 <see cref="RequestOpenLogin"/> 置位，由新页 <c>OnLoaded</c> 消费；
    /// 消息处理器不消费该标志（避免旧实例抢消费后登录落在即将卸载的页面上）。
    /// </summary>
    public static bool PendingOpenLogin { get; private set; }


    /// <summary>
    /// 标记战绩页是否处于可接收「打开登录」消息的存活状态。
    /// </summary>
    /// <param name="alive">为 true 表示已注册消息并可打开登录 UI；卸载时为 false。</param>
    public static void SetGameRecordPageAlive(bool alive)
    {
        _gameRecordPageAlive = alive;
    }


    /// <summary>
    /// 请求重新登录：打开战绩登录入口。
    /// <list type="bullet">
    /// <item>已在战绩页：只发 <see cref="GameRecordOpenLoginMessage"/>，禁止再主 Frame Navigate 同页。</item>
    /// <item>不在战绩页：置 <see cref="PendingOpenLogin"/> 并导航到战绩页，由新页 Loaded 打开登录。</item>
    /// </list>
    /// </summary>
    public static void RequestOpenLogin()
    {
        // 已在工具箱：勿 Navigate 到同类型页（会 new 实例并与消息竞态，登录可能打在即将卸载的旧页上）
        if (_gameRecordPageAlive)
        {
            WeakReferenceMessenger.Default.Send(new GameRecordOpenLoginMessage());
            return;
        }

        PendingOpenLogin = true;
        WeakReferenceMessenger.Default.Send(new MainViewNavigateMessage(typeof(GameRecordPage)));
        // 新页通常尚未注册；若极端情况下已有订阅者会立即打开，否则靠 OnLoaded 消费 PendingOpenLogin
        WeakReferenceMessenger.Default.Send(new GameRecordOpenLoginMessage());
    }


    /// <summary>
    /// 消费「打开登录」挂起标志；仅应由新战绩页在 <c>OnLoaded</c> 成功打开登录前调用。
    /// 消息处理器不得调用，以免跨页导航时旧实例抢先清掉标志。
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
