using Microsoft.UI.Dispatching;
using Serilog;
using Starward.Features.Gacha;
using Starward.Features.GamepadControl;
using Starward.Features.GameRecord.SignIn;
using Starward.Features.RPC;
using Starward.Features.Setting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// 常驻实例的后台宿主：全局热键、手柄驱动、GameBar 引导键接管、RPC 环境下发、抽卡物品名缓存与启动批量签到。
/// <para>
/// 这些职责过去全挂在 <c>MainView_Loaded</c> 上，导致仅托盘驻留（<c>--hide</c>）或快捷方式启动时统统缺席
/// —— 用户按 Alt+D 截不了图，手柄与引导键接管也不生效。现由系统托盘窗口（常驻实例中唯一必然存在
/// 且永不销毁的窗口）统一拉起，与主窗口彻底解耦。
/// </para>
/// <para>
/// 不含「检查更新 / 展示更新说明」：那一步要弹 UI，仍留在 <c>MainView</c>，随主窗口显示时触发。
/// </para>
/// </summary>
internal static class ResidentHost
{

    /// <summary>保证常驻职责每个进程只启动一次。</summary>
    private static int _started;


    /// <summary>
    /// 启动常驻后台职责。幂等：多次调用只生效一次。
    /// </summary>
    /// <param name="hotkeyOwnerHandle">承载全局热键的窗口句柄，须是生命周期与进程等长的窗口（系统托盘窗口）。</param>
    /// <param name="dispatcherQueue">UI 线程调度器，供手柄模拟输入提示窗口使用。</param>
    public static void Start(nint hotkeyOwnerHandle, DispatcherQueue dispatcherQueue)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }
        try
        {
            HotkeyManager.Initialize(hotkeyOwnerHandle);
            // 自愈：上次进程被强杀/崩溃可能把 GameBar 引导键停在「已接管」状态，
            // 当前配置并非「总是接管」时先还原，避免注册表残留导致引导键永久失灵。
            GamepadController.RestoreGamepadGuideButtonIfNotAlwaysTakenOver();
            // 启动时为三个游戏按当前语言确保物品名称映射缓存；首次启动/更新后会一次性迁移存量记录名称。
            _ = Task.Run(() => AppConfig.GetService<GachaItemNameService>().EnsureCurrentLanguageOnStartupAsync());
            // 启动后批量为所有账号的每个游戏静默签到（逐个请求、请求间随机延时，模拟真人节奏）。
            _ = Task.Run(() => AppConfig.GetService<AutoSignInService>().RunStartupBatchAsync());
            AppConfig.GetService<RpcService>().TrySetEnviromentAsync();
            _ = InitializeGamepadAsync(dispatcherQueue);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Start resident host");
        }
    }


    /// <summary>
    /// 初始化手柄驱动（含 GameBar 引导键 / 分享键接管）。未开启手柄支持时直接跳过。
    /// </summary>
    /// <param name="dispatcherQueue">UI 线程调度器。</param>
    private static async Task InitializeGamepadAsync(DispatcherQueue dispatcherQueue)
    {
        if (!AppConfig.EnableGamepadController)
        {
            return;
        }
        // 延后初始化，避免与首屏布局/输入抢占同一时刻
        await Task.Delay(1000);
        await Task.Run(() => GamepadController.Initialize(dispatcherQueue));
    }

}
