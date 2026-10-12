using System;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 自定义启动程序是链接，但打不开：系统里没有程序注册它的协议（如对应程序已卸载、链接拼错），
/// 或协议还在注册表里而对应程序已不存在（卸载没清干净）。前者直接打开链接不会报错，而是弹出系统「选择应用」窗口。
/// 两种情况都不能改为直接启动游戏，因为用户选的是经由该程序启动。调用方据此提示用户即可，不必按故障记录堆栈。
/// </summary>
internal sealed class ThirdPartyToolUrlNotRegisteredException : Exception
{

    /// <summary>无法打开的链接。</summary>
    public string Url { get; }


    /// <summary>
    /// 构造异常。
    /// </summary>
    /// <param name="url">无法打开的链接。</param>
    /// <param name="innerException">打开链接时系统返回的错误；协议未注册（启动前就查出来）时为 <see langword="null"/>。</param>
    public ThirdPartyToolUrlNotRegisteredException(string url, Exception? innerException = null)
        : base(innerException is null ? $"No app is registered for the third party tool url: {url}" : $"Failed to open the third party tool url: {url} ({innerException.Message})", innerException)
    {
        Url = url;
    }

}
