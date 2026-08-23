using System;

namespace Starward.Features.ViewHost;

internal class MainViewNavigateMessage
{

    public Type Page { get; set; }

    /// <summary>可选导航参数；为 <see langword="null"/> 时主视图仍传入当前 <c>GameId</c>。</summary>
    public object? Parameter { get; set; }

    public MainViewNavigateMessage(Type page, object? parameter = null)
    {
        Page = page;
        Parameter = parameter;
    }

}
