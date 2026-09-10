using Starward.Core;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 后台自动更新某个「账号 + 数据板块」结束。成功表示本地库已写入；失败表示已记入异常列表。
/// 在 UI 线程发送，供当前打开的数据页刷新红点与列表。
/// </summary>
internal sealed class RecordRefreshCompletedMessage
{

    public RecordRefreshCompletedMessage(GameBiz gameBiz, long uid, RecordRefreshItem item, bool succeeded)
    {
        GameBiz = gameBiz;
        Uid = uid;
        Item = item;
        Succeeded = succeeded;
    }

    /// <summary>出错或更新成功的角色区服。</summary>
    public GameBiz GameBiz { get; }

    /// <summary>角色 uid。</summary>
    public long Uid { get; }

    /// <summary>数据板块。</summary>
    public RecordRefreshItem Item { get; }

    /// <summary>本地库已写入为 true；记了异常为 false。</summary>
    public bool Succeeded { get; }

}
