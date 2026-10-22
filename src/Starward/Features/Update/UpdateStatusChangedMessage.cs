using CommunityToolkit.Mvvm.Messaging;

namespace Starward.Features.Update;

/// <summary>
/// 更新状态变化消息：一次检查更新结束（成功或失败）、或更新包下载完成时发出。
/// 可能在线程池上发出，接收方给绑定属性赋值前要切回 UI 线程。
/// </summary>
internal class UpdateStatusChangedMessage
{


    /// <summary>
    /// 通过 <see cref="WeakReferenceMessenger.Default"/> 广播一条消息。
    /// </summary>
    public static void Send()
    {
        WeakReferenceMessenger.Default.Send(new UpdateStatusChangedMessage());
    }


}
