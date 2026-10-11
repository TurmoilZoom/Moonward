using CommunityToolkit.Mvvm.ComponentModel;
using System;


namespace Starward.Features.Gacha.UIGF;

/// <summary>
/// 抽卡记录导入导出表格的可伸缩列宽。表头与每一行的游戏名列、结果列绑定同一实例，
/// 「最后一次抽卡」是表格里唯一的 * 列，吸收其余宽度。
/// </summary>
/// <remarks>
/// 不用多个 * 列加 MinWidth / MaxWidth 实现：WinUI Grid 在测量阶段按权重分配 * 列时不计 MinWidth，
/// 内容较长的行会按测量阶段分到的宽度申报需求，于是这一行被排得更宽，行与行、行与表头错位。
/// 改为由代码算出确定的像素宽度，所有行完全一致。
/// </remarks>
public sealed partial class GachaTableColumnLayout : ObservableObject
{

    /// <summary>游戏名列（图标右侧的游戏名 / UIGF 字段名）宽度上限。</summary>
    public const double GameTextMaxWidth = 120;

    /// <summary>游戏名列窄于此值时直接收起（只剩图标），避免只露出半个字或省略号。</summary>
    private const double GameTextCollapseWidth = 28;

    /// <summary>导入结果列宽度上限。</summary>
    public const double ResultMaxWidth = 160;

    /// <summary>导入结果列宽度下限：只显示状态图标。</summary>
    public const double ResultMinWidth = 20;


    /// <summary>游戏名列当前宽度。</summary>
    public double GameTextWidth { get; private set => SetProperty(ref field, value); } = GameTextMaxWidth;


    /// <summary>导入结果列当前宽度（导出页没有结果列，保持默认值不使用）。</summary>
    public double ResultWidth { get; private set => SetProperty(ref field, value); } = ResultMaxWidth;


    /// <summary>
    /// 按可伸缩列可用的总宽度重新分配：宽时游戏名列、结果列取上限，其余全部给「最后一次抽卡」；
    /// 变窄时「最后一次抽卡」先缩到 <paramref name="nameMinWidth"/>，之后游戏名列与结果列按比例一起压缩，
    /// 直到游戏名列收起、结果列只剩状态图标。
    /// </summary>
    /// <param name="flexibleWidth">表格内容宽度减去所有固定列与列间距后，留给可伸缩列的宽度。</param>
    /// <param name="nameMinWidth">「最后一次抽卡」列优先保留的宽度。</param>
    /// <param name="hasResultColumn">是否有导入结果列（仅导入页）。</param>
    public void Update(double flexibleWidth, double nameMinWidth, bool hasResultColumn)
    {
        double maxTotal = GameTextMaxWidth + (hasResultColumn ? ResultMaxWidth : 0);
        double ratio = Math.Clamp((flexibleWidth - nameMinWidth) / maxTotal, 0, 1);
        double gameText = Math.Floor(GameTextMaxWidth * ratio);
        GameTextWidth = gameText < GameTextCollapseWidth ? 0 : gameText;
        if (hasResultColumn)
        {
            ResultWidth = Math.Max(ResultMinWidth, Math.Floor(ResultMaxWidth * ratio));
        }
    }


}
