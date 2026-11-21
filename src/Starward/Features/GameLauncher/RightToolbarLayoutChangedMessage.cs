namespace Starward.Features.GameLauncher;

/// <summary>
/// 某个游戏的首页右侧功能栏顺序或显隐改了（自定义功能栏对话框发出），首页按新设置重排。
/// </summary>
/// <param name="game">游戏（<see cref="Starward.Core.GameBiz.Game"/>，不分区服）。</param>
internal sealed class RightToolbarLayoutChangedMessage(string game)
{

    /// <summary>游戏（不分区服），如 <c>hk4e</c>。</summary>
    public string Game { get; } = game;

}
