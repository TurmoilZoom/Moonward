using System;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 请求启动的游戏已经在运行。属于可预期的用户操作（重复双击快捷方式、游戏开着又点了启动），
/// 调用方据此提示用户即可，不必按故障记录堆栈。
/// <para>
/// 早先用 <see cref="Exception"/> + <c>"Game is running:"</c> 消息前缀传递，调用方靠比对字符串识别，
/// 改动那句文案就会静默失效，故独立成类型。
/// </para>
/// </summary>
internal sealed class GameRunningException : Exception
{

    /// <summary>已在运行的游戏进程名（不含扩展名）。</summary>
    public string ProcessName { get; }


    /// <summary>已在运行的游戏进程 Id。</summary>
    public int ProcessId { get; }


    /// <summary>
    /// 构造异常，消息沿用原先的 <c>Game is running: {进程名}.exe ({pid}).</c> 格式。
    /// </summary>
    /// <param name="processName">已在运行的游戏进程名（不含扩展名）。</param>
    /// <param name="processId">已在运行的游戏进程 Id。</param>
    public GameRunningException(string processName, int processId) : base($"Game is running: {processName}.exe ({processId}).")
    {
        ProcessName = processName;
        ProcessId = processId;
    }

}
