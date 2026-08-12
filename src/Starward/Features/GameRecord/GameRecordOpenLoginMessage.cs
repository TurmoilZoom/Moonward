namespace Starward.Features.GameRecord;

/// <summary>
/// 请求战绩工具箱打开登录入口：国服弹出菜单（验证码 / Cookie），国际服进入网页登录。
/// 仅由当前存活的 <see cref="GameRecordPage"/> 处理；不携带「是否跨页」状态（跨页靠 <see cref="GameRecordAccountRecovery.PendingOpenLogin"/>）。
/// </summary>
internal sealed class GameRecordOpenLoginMessage
{
}
