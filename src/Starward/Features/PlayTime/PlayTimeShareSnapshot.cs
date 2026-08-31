using System;
using System.Collections.Generic;

namespace Starward.Features.PlayTime;

/// <summary>
/// 游戏时长统计分享图的数据快照。对话框在 UI 线程取值，渲染在后台线程读取，
/// 因此这里只放已经格式化好的字符串与数值，不引用任何 UI 对象。
/// </summary>
internal sealed class PlayTimeShareSnapshot
{

    /// <summary>输出文件名前缀。</summary>
    public string FileStem { get; set; } = "playtime_stats";

    /// <summary>左上角标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>游戏名称。</summary>
    public string GameName { get; set; } = "";

    /// <summary>区服名称。</summary>
    public string ServerName { get; set; } = "";

    /// <summary>统计卡片（与对话框顺序一致）。</summary>
    public List<PlayTimeShareCard> Cards { get; set; } = [];

    /// <summary>柱状图区间标题，如「最近 15 天」或「2026-08」。</summary>
    public string BarTitle { get; set; } = "";

    /// <summary>柱状图区间总时长文本。</summary>
    public string BarTotalText { get; set; } = "";

    /// <summary>柱状图数据。</summary>
    public List<PlayTimeShareBar> Bars { get; set; } = [];

    /// <summary>热力图数据（按日期升序，负值为占位格）。</summary>
    public List<PlayTimeShareHeatmapDay> HeatmapDays { get; set; } = [];

}



/// <summary>统计卡片：标题、主数值与副标题。</summary>
internal sealed class PlayTimeShareCard
{

    public string Title { get; set; } = "";

    public string Value { get; set; } = "";

    public string SubText { get; set; } = "";

}



/// <summary>柱状图单根柱子：横轴标签与分钟数。</summary>
internal sealed class PlayTimeShareBar
{

    public string Label { get; set; } = "";

    public double Minutes { get; set; }

}



/// <summary>热力图单个方块：日期与分钟数。</summary>
internal sealed class PlayTimeShareHeatmapDay
{

    public DateOnly Date { get; set; }

    public double Minutes { get; set; }

}
