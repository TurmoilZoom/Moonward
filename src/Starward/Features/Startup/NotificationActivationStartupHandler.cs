using Serilog;
using Starward.Core;
using Starward.Features.GameSelector;
using Starward.Features.Notification;
using Starward.Helpers;
using System.Threading.Tasks;

namespace Starward.Features.Startup;

/// <summary>
/// 点击系统通知后的协议激活（见 <see cref="NotificationActivation"/>）：
/// <c>moonward-notify://open/?biz=…</c> 打开主窗口，<c>moonward-notify://copy/?text=…</c>（通知按钮）复制文本。
/// <para>
/// 通知平台总以普通权限拉起本进程，常驻实例却可能是提权的，所以不走 <c>AppInstance.RedirectActivationToAsync</c>，
/// 而是经 <see cref="ResidentInstanceMessenger"/>（已放行低 → 高的 <c>WM_COPYDATA</c>）请常驻实例打开主窗口，随后退出。
/// 没有常驻实例时（Moonward 已退出、点的是通知中心里的旧通知）放行，本进程照常启动并打开主窗口。
/// </para>
/// <para>
/// 复制不经常驻实例：本进程自己写剪贴板并 Flush 后退出，Moonward 已经退出时点通知中心里的旧通知也能复制。
/// </para>
/// </summary>
internal sealed class NotificationActivationStartupHandler : IStartupHandler
{

    public bool CanHandle(StartupContext context) =>
        context.Args is [var arg, ..] && NotificationActivation.IsActivationArgument(arg);


    public async Task<StartupOutcome> HandleAsync(StartupContext context)
    {
        if (NotificationActivation.TryParseCopy(context.Args[0], out string text))
        {
            bool copied = await ClipboardHelper.SetTextAndFlushAsync(text);
            Log.Information("Notification copy button: copied {copied} ({length} chars).", copied, text.Length);
            return StartupOutcome.Exit;
        }
        NotificationActivation.TryParseOpen(context.Args[0], out GameBiz? biz, out long uid);
        if (ResidentInstanceMessenger.NotifyOpenMainWindow(biz, uid))
        {
            return StartupOutcome.Exit;
        }
        // 主窗口建好后游戏选择器按「当前游戏」设置初始化，先写进去
        if (biz is { } target && target.IsKnown())
        {
            AppConfig.CurrentGameBiz = SelectGameBizMessage.ResolveTarget(AppConfig.CurrentGameBiz, target);
        }
        return StartupOutcome.Continue;
    }

}
