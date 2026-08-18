namespace Starward.Features.Startup;

/// <summary>
/// 命令行启动「动词」（<see cref="System.Environment.GetCommandLineArgs"/> 的首个用户参数）
/// 与 <c>moonward://</c> 协议前缀的字面量常量。集中定义，避免 <see cref="App"/>、各
/// <see cref="IStartupHandler"/> 等处重复硬编码同一字符串。
/// </summary>
internal static class StartupVerbs
{
    /// <summary>RPC 子进程模式：<c>Moonward.exe rpc …</c>。</summary>
    public const string Rpc = "rpc";

    /// <summary>游玩时长记录子进程：<c>Moonward.exe playtime --pid {pid} --biz {game_biz}</c>。</summary>
    public const string PlayTime = "playtime";

    /// <summary>命令行直接启动游戏：<c>Moonward.exe startgame --biz {game_biz}</c>。</summary>
    public const string StartGame = "startgame";

    /// <summary><c>moonward://</c> 自定义 URL 协议前缀。</summary>
    public const string UrlProtocolScheme = "moonward://";

    /// <summary>
    /// <c>moonward://test/</c> 调试窗口前缀。该模式须在环境初始化（及 DI 容器构建）之前，
    /// 由 <see cref="App.OnLaunched"/> 直接处理，故不进入职责链。
    /// </summary>
    public const string TestUrlProtocolPrefix = "moonward://test/";

    /// <summary>仅启动系统托盘、不显示主窗口：<c>Moonward.exe --hide</c>。</summary>
    public const string Hide = "--hide";
}
