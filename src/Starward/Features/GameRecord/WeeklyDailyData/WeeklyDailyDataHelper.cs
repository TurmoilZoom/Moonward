using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Starward.Features.GameRecord.WeeklyDailyData;

/// <summary>
/// 周每日数据表格的共享工具方法。
/// 包含日期计算、表头构建、单元格样式等。
/// </summary>
public static class WeeklyDailyDataHelper
{
    /// <summary>
    /// 将任意日期转换为该周的周一（星期一为每周第一天）。
    /// </summary>
    /// <param name="date">输入日期。</param>
    /// <returns>所在周的周一日期。</returns>
    public static DateOnly GetMonday(DateOnly date)
    {
        // DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
        // 目标：让周一为 0 偏移
        int diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    /// <summary>
    /// 构建当前选中周的 7 个日期（从 weekStart 开始，连续 7 天）。
    /// </summary>
    /// <param name="weekStart">周起始（周一）。</param>
    /// <returns>7 个 DateOnly。</returns>
    public static List<DateOnly> GetWeekDates(DateOnly weekStart)
    {
        return Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
    }

    /// <summary>
    /// 构建日期表头列表，包含星期文本、日期文本、状态（今天/过去/未来）。
    /// 跨月时日期显示为 M/d 格式。
    /// </summary>
    /// <param name="dates">该周的 7 个日期。</param>
    /// <param name="today">用于判断状态的“今天”（可为服务器今天或本地 DateTime.Today）。</param>
    /// <returns>WeekDateCell 列表。</returns>
    public static List<WeekDateCell> BuildWeekDateCells(IReadOnlyList<DateOnly> dates, DateOnly today)
    {
        var cells = new List<WeekDateCell>(dates.Count);
        bool isCrossMonth = dates.Select(d => d.Month).Distinct().Count() > 1;

        for (int i = 0; i < dates.Count; i++)
        {
            var d = dates[i];
            var cell = new WeekDateCell
            {
                Date = d,
                WeekdayText = FormatWeekdayText(d),
                DayText = FormatDayText(d, isCrossMonth),
                State = d > today ? WeekDateState.Future :
                        d < today ? WeekDateState.Past : WeekDateState.Today,
            };
            cells.Add(cell);
        }
        return cells;
    }

    /// <summary>
    /// 获取本地化的星期短名称（周一到周日顺序固定，不受系统 FirstDayOfWeek 影响）。
    /// </summary>
    /// <param name="date">日期。</param>
    /// <returns>短星期文本。</returns>
    public static string FormatWeekdayText(DateOnly date)
    {
        var culture = CultureInfo.CurrentUICulture;
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return dt.ToString("ddd", culture);
    }

    /// <summary>
    /// 格式化日期数字。跨月周显示 M/d，否则仅显示日。
    /// </summary>
    /// <param name="date">日期。</param>
    /// <param name="isCrossMonthWeek">是否跨月周。</param>
    /// <returns>显示用的日期文本。</returns>
    public static string FormatDayText(DateOnly date, bool isCrossMonthWeek)
    {
        if (isCrossMonthWeek)
        {
            return date.ToString("M/d", CultureInfo.CurrentCulture);
        }
        return date.Day.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// 根据选中年月和参考“今天”计算默认周起始（周一）。
    /// 当前月返回今天所在周；历史月返回该月 1 号所在周。
    /// </summary>
    /// <param name="year">选中月份年份。</param>
    /// <param name="month">选中月份（1-12）。</param>
    /// <param name="today">参考今天（服务器今天或本地今天）。</param>
    /// <returns>默认周的周一。</returns>
    public static DateOnly ComputeDefaultWeekStart(int year, int month, DateOnly today)
    {
        if (year == today.Year && month == today.Month)
        {
            return GetMonday(today);
        }
        return GetMonday(new DateOnly(year, month, 1));
    }

    /// <summary>
    /// 计算是否可以切换到上一周（上一周至少包含选中月的一天）。
    /// </summary>
    /// <param name="weekStart">当前周起始。</param>
    /// <param name="firstDayOfMonth">选中月的第一天。</param>
    /// <returns>是否可上一周。</returns>
    public static bool ComputeCanGoPrevious(DateOnly weekStart, DateOnly firstDayOfMonth)
    {
        return weekStart.AddDays(-1) >= firstDayOfMonth;
    }

    /// <summary>
    /// 计算是否可以切换到下一周（下一周至少包含选中月的一天）。
    /// </summary>
    /// <param name="weekStart">当前周起始。</param>
    /// <param name="lastDayOfMonth">选中月的最后一天。</param>
    /// <returns>是否可下一周。</returns>
    public static bool ComputeCanGoNext(DateOnly weekStart, DateOnly lastDayOfMonth)
    {
        return weekStart.AddDays(7) <= lastDayOfMonth;
    }

    // ===== 日期单元格视觉样式辅助（供 XAML x:Bind 使用） =====

    /// <summary>
    /// 获取日期单元格背景画刷。今天使用强调色，其余透明。
    /// </summary>
    public static Brush GetDateCellBackground(WeekDateState state)
    {
        if (state == WeekDateState.Today)
        {
            return (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    /// <summary>
    /// 获取日期单元格边框画刷。过去日期使用次要文本色，其余透明。
    /// </summary>
    public static Brush GetDateCellBorderBrush(WeekDateState state)
    {
        if (state == WeekDateState.Past)
        {
            return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    /// <summary>
    /// 获取日期单元格边框厚度。过去日期为 1，其余 0。
    /// </summary>
    public static Thickness GetDateCellBorderThickness(WeekDateState state)
    {
        if (state == WeekDateState.Past)
        {
            return new Thickness(1);
        }
        return new Thickness(0);
    }

    /// <summary>
    /// 获取日期单元格前景画刷。今天使用强调色上的文本色，其余主文本色。
    /// </summary>
    public static Brush GetDateCellForeground(WeekDateState state)
    {
        if (state == WeekDateState.Today)
        {
            return (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        }
        return (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    /// <summary>
    /// 获取日期单元格描边（Ellipse Stroke）厚度。仅过去日期为 1，其余为 0。
    /// 供 Ellipse 替代 Border 实现圆形抗锯齿使用。
    /// </summary>
    /// <param name="state">日期单元格状态。</param>
    /// <returns>描边厚度（double）。</returns>
    public static double GetDateCellStrokeThickness(WeekDateState state)
    {
        if (state == WeekDateState.Past)
        {
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// 周切换箭头的不透明度。不可切换时仍占位（Opacity=0），避免 Collapsed 导致表头列宽塌陷、整表偏移。
    /// 配合 <c>IsHitTestVisible</c> 禁用点击；命令侧仍有 <c>CanGo*</c> 守卫。
    /// </summary>
    /// <param name="canGo">是否可向该方向切换周。</param>
    /// <returns>可切换时为 1，否则为 0。</returns>
    public static double GetArrowOpacity(bool canGo) => canGo ? 1.0 : 0.0;
}
