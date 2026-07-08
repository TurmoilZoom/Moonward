using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Starward.Features.GameRecord.WeeklyDailyData;

/// <summary>
/// 周日期单元格状态：未来、过去、今天。
/// </summary>
public enum WeekDateState
{
    Future,
    Past,
    Today,
}

/// <summary>
/// 日期表头单元格（用于显示星期和日期数字及状态）。
/// </summary>
public class WeekDateCell
{
    /// <summary>日期。</summary>
    public DateOnly Date { get; set; }

    /// <summary>日期数字文本（可能跨月显示 M/d）。</summary>
    public string DayText { get; set; } = "";

    /// <summary>星期短文本（本地化，如“周一”）。</summary>
    public string WeekdayText { get; set; } = "";

    /// <summary>状态（今天/过去/未来），用于视觉样式。</summary>
    public WeekDateState State { get; set; }
}

/// <summary>
/// 资源行（一周内某类资源的 7 天数据）。
/// </summary>
public class WeeklyResourceRow
{
    /// <summary>数据类型标识（用于区分不同资源）。</summary>
    public string DataType { get; set; } = "";

    /// <summary>显示名称（来自 Lang 或映射）。</summary>
    public string Name { get; set; } = "";

    /// <summary>资源图标。</summary>
    public BitmapImage? Icon { get; set; }

    /// <summary>该周 7 天的单元格数据。</summary>
    public List<WeeklyResourceCell> Cells { get; set; } = [];
}

/// <summary>
/// 单个资源在某一天的单元格数据。
/// </summary>
public class WeeklyResourceCell
{
    /// <summary>日期。</summary>
    public DateOnly Date { get; set; }

    /// <summary>数量。</summary>
    public int Count { get; set; }

    /// <summary>是否为未来日期（> 服务器今天或本地今天）。</summary>
    public bool IsFuture { get; set; }

    /// <summary>
    /// 显示文本：未来日期为空白，否则显示数量（含 0）。
    /// </summary>
    public string DisplayText => IsFuture ? "" : Count.ToString(CultureInfo.CurrentCulture);
}
