using Starward.Core.HoYoPlay;
using System;

namespace Starward.Features.GameInstall;

/// <summary>
/// 更新某个区服时，可以一并更新的、与它硬链接的其他区服。
/// </summary>
/// <param name="GameId">区服</param>
/// <param name="InstallPath">安装目录</param>
/// <param name="LocalVersion">本地版本</param>
/// <param name="LatestVersion">最新版本</param>
/// <param name="IsGameRunning">该区服的游戏正在运行，此时不能更新</param>
/// <param name="IsHardLinkSource">该区服是本体，当前区服是通过硬链接从它产生的，要先于当前区服更新</param>
internal sealed record OtherServerUpdate(GameId GameId, string InstallPath, Version LocalVersion, Version LatestVersion, bool IsGameRunning, bool IsHardLinkSource);
