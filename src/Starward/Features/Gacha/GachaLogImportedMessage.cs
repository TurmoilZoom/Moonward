using Starward.Core;
using System.Collections.Generic;

namespace Starward.Features.Gacha;

/// <summary>
/// 本地导入抽卡记录（如 UIGF v4.x）成功后发送，通知抽卡页面按受影响的 (游戏, Uid) 重新读库刷新。
/// </summary>
internal class GachaLogImportedMessage
{

    /// <summary>
    /// 本次成功导入的 (游戏, Uid) 列表，按导入顺序排列。
    /// </summary>
    public IReadOnlyList<(GameBiz Game, long Uid)> ImportedUids { get; }


    public GachaLogImportedMessage(IReadOnlyList<(GameBiz Game, long Uid)> importedUids)
    {
        ImportedUids = importedUids;
    }

}
