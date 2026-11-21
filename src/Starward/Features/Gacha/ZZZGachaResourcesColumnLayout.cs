using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Starward.Language;
using System;
using Windows.Foundation;


namespace Starward.Features.Gacha;

/// <summary>
/// 绝区零抽卡资源弹出层右侧两列（已垫、数量 / 距保底）的列宽。标题行的刷新按钮、持有量、表头与各频段行绑定同一实例。
/// </summary>
/// <remarks>
/// 数字在列内居中，列宽取该列最宽的内容（表头或数字）：最宽的内容贴着弹出层右内边距，右侧留白与左侧图标到边框的距离一致。
/// 定宽列比数字宽得多，居中后右侧会比左侧多空出一截。
/// 各频段行在 ItemsControl 的模板里，不能与上方共享同一个 Grid 的 Auto 列，所以由代码量好宽度再绑定。
/// </remarks>
public sealed partial class ZZZGachaResourcesColumnLayout : ObservableObject
{

    /// <summary>数量列宽度下限：标题行 32 宽的刷新按钮要能放在这一列的中轴上。</summary>
    private const double CountMinWidth = 32;

    /// <summary>列宽上限：外语表头过长时截断，不把名称列挤没。</summary>
    private const double MaxWidth = 96;

    /// <summary>表头字号，与 XAML 里表头 TextBlock 一致。</summary>
    private const double HeaderFontSize = 12;

    /// <summary>数字字号（TextBlock 默认字号），与 XAML 里数字 TextBlock 一致。</summary>
    private const double NumberFontSize = 14;


    /// <summary>「已垫」列宽度。</summary>
    public double PityWidth { get; private set => SetProperty(ref field, value); } = 40;


    /// <summary>数量列宽度：持有量与「距保底」共用这一列。</summary>
    public double CountWidth { get; private set => SetProperty(ref field, value); } = 72;


    /// <summary>
    /// 按当前要显示的表头与数字重新量列宽（须在 UI 线程调用）。
    /// </summary>
    /// <param name="resources">要显示的抽卡资源。</param>
    public void Update(ZZZGachaResources resources)
    {
        double pity = MeasureTextWidth(Lang.GachaStatsCard_Pity, HeaderFontSize);
        double count = MeasureTextWidth(Lang.GachaLogPage_ToHardPity, HeaderFontSize);
        long[] holdings = [resources.Polychrome, resources.Monochrome, resources.EncryptedMasterTape, resources.MasterTape, resources.Boopon];
        foreach (long value in holdings)
        {
            count = Math.Max(count, MeasureTextWidth(value.ToString(), NumberFontSize));
        }
        foreach (var progress in resources.PityProgress)
        {
            pity = Math.Max(pity, MeasureTextWidth(progress.CurrentPity.ToString(), NumberFontSize));
            count = Math.Max(count, MeasureTextWidth(progress.RemainingToHardPity.ToString(), NumberFontSize));
        }
        PityWidth = Math.Min(Math.Ceiling(pity), MaxWidth);
        CountWidth = Math.Clamp(Math.Ceiling(count), CountMinWidth, MaxWidth);
    }


    /// <summary>
    /// 量一段文字单行排版的宽度。
    /// </summary>
    /// <param name="text">文字。</param>
    /// <param name="fontSize">字号。</param>
    /// <returns>宽度（DIP）。</returns>
    private static double MeasureTextWidth(string text, double fontSize)
    {
        var textBlock = new TextBlock { Text = text, FontSize = fontSize };
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return textBlock.DesiredSize.Width;
    }


}
