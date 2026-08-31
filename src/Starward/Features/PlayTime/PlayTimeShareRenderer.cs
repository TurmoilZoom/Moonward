using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Starward.Features.GameRecord.Share;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace Starward.Features.PlayTime;

/// <summary>
/// 游戏时长统计分享图：统计卡片 + 柱状图 + 一年热力图。
/// 复用战绩分享图的壁纸磨砂与亚克力卡片（<see cref="ShareImageCanvas"/>），布局对齐统计对话框。
/// </summary>
internal static class PlayTimeShareRenderer
{

    private const float CanvasWidth = 800f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
    private const float TitleLine = 26f;
    private const float SectionGap = 14f;
    private const float CardPadX = 16f;
    private const float CardPadY = 12f;

    private const float StatCellTitle = 16f;
    private const float StatCellValue = 24f;
    private const float StatCellSub = 14f;
    private const float StatCellGap = 10f;
    private const int StatColumns = 3;

    private const float PlotHeight = 120f;
    private const float BarLabelHeight = 14f;
    private const float BarAxisGap = 6f;
    private const float MaxBarWidth = 34f;

    private const float HeatmapLabelRow = 14f;
    private const float HeatmapWeekdayColumn = 22f;
    private const float HeatmapCellGap = 2f;
    private const int HeatmapRows = 7;

    private static readonly Color GridLineColor = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly Color EmptyCellColor = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);


    public static async Task<string> RenderAndSaveAsync(
        PlayTimeShareSnapshot data,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        float contentHeight = Measure(data);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            data.FileStem,
            0,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, data),
            cancellationToken,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
    }



    #region 尺寸测量


    private static float Measure(PlayTimeShareSnapshot data)
    {
        float y = TitleLine;
        if (data.Cards.Count > 0)
        {
            y += MeasureStatsCard(data) + SectionGap;
        }
        if (data.Bars.Count > 0)
        {
            y += TitleLine + MeasureBarCard() + SectionGap;
        }
        if (data.HeatmapDays.Count > 0)
        {
            y += MeasureHeatmapCard(data);
        }
        else if (y > TitleLine)
        {
            // 末尾多加的一段间距回退掉
            y -= SectionGap;
        }
        return y;
    }


    private static float MeasureStatsCard(PlayTimeShareSnapshot data)
    {
        int rows = (data.Cards.Count + StatColumns - 1) / StatColumns;
        float cellHeight = StatCellTitle + 2 + StatCellValue + 2 + StatCellSub;
        return CardPadY * 2 + rows * cellHeight + Math.Max(0, rows - 1) * StatCellGap;
    }


    private static float MeasureBarCard()
    {
        return CardPadY * 2 + PlotHeight + BarAxisGap + BarLabelHeight;
    }


    private static float MeasureHeatmapCard(PlayTimeShareSnapshot data)
    {
        float pitch = HeatmapPitch(data);
        return CardPadY * 2 + HeatmapLabelRow + HeatmapRows * pitch;
    }


    /// <summary>热力图每列（含间距）的宽度：按可用宽度均分所有周。</summary>
    private static float HeatmapPitch(PlayTimeShareSnapshot data)
    {
        int columns = Math.Max(1, (data.HeatmapDays.Count + HeatmapRows - 1) / HeatmapRows);
        float usable = ContentWidth - CardPadX * 2 - HeatmapWeekdayColumn;
        return Math.Clamp(usable / columns, 4f, 18f);
    }


    #endregion



    #region 绘制


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, PlayTimeShareSnapshot data)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;

        ShareImageCanvas.DrawText(ds, data.Title, x, y, ctx.Title, ShareImageCanvas.PrimaryText);
        string game = string.IsNullOrEmpty(data.ServerName) ? data.GameName : $"{data.GameName}  ·  {data.ServerName}";
        ShareImageCanvas.DrawTextRight(ds, game, x + ContentWidth, y + 3, ctx.Small, ShareImageCanvas.SecondaryText);
        y += TitleLine;

        if (data.Cards.Count > 0)
        {
            float height = MeasureStatsCard(data);
            ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, height);
            DrawStatCards(ds, ctx, x, y, data);
            y += height + SectionGap;
        }

        if (data.Bars.Count > 0)
        {
            ShareImageCanvas.DrawText(ds, data.BarTitle, x, y, ctx.Title, ShareImageCanvas.SecondaryText);
            ShareImageCanvas.DrawTextRight(ds, data.BarTotalText, x + ContentWidth, y + 3, ctx.Small, ShareImageCanvas.SecondaryText);
            y += TitleLine;
            float height = MeasureBarCard();
            ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, height);
            DrawBarChart(ds, ctx, x, y, data);
            y += height + SectionGap;
        }

        if (data.HeatmapDays.Count > 0)
        {
            float height = MeasureHeatmapCard(data);
            ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, height);
            DrawHeatmap(ds, ctx, x, y, data);
        }
    }


    private static void DrawStatCards(CanvasDrawingSession ds, ShareImageContext ctx, float left, float top, PlayTimeShareSnapshot data)
    {
        using CanvasTextFormat valueFormat = ShareImageCanvas.CreateTextFormat(18f, ShareImageCanvas.Bold, trimming: true);
        using CanvasTextFormat titleFormat = ShareImageCanvas.CreateTextFormat(12f, trimming: true);
        using CanvasTextFormat subFormat = ShareImageCanvas.CreateTextFormat(11f, trimming: true);

        float cellWidth = (ContentWidth - CardPadX * 2 - (StatColumns - 1) * StatCellGap) / StatColumns;
        float cellHeight = StatCellTitle + 2 + StatCellValue + 2 + StatCellSub;
        for (int i = 0; i < data.Cards.Count; i++)
        {
            PlayTimeShareCard card = data.Cards[i];
            float cellX = left + CardPadX + i % StatColumns * (cellWidth + StatCellGap);
            float cellY = top + CardPadY + i / StatColumns * (cellHeight + StatCellGap);
            ShareImageCanvas.DrawText(ds, card.Title, cellX, cellY, titleFormat, ShareImageCanvas.SecondaryText, cellWidth);
            ShareImageCanvas.DrawText(ds, card.Value, cellX, cellY + StatCellTitle + 2, valueFormat, ShareImageCanvas.PrimaryText, cellWidth);
            ShareImageCanvas.DrawText(ds, card.SubText, cellX, cellY + StatCellTitle + 2 + StatCellValue + 2, subFormat, ShareImageCanvas.TertiaryText, cellWidth);
        }
    }


    /// <summary>
    /// 柱状图：纵轴 3 条刻度线（0 / 一半 / 最大），柱体圆角朝上，与对话框中的 <see cref="BarChart"/> 保持一致。
    /// </summary>
    private static void DrawBarChart(CanvasDrawingSession ds, ShareImageContext ctx, float left, float top, PlayTimeShareSnapshot data)
    {
        using CanvasTextFormat axisFormat = ShareImageCanvas.CreateTextFormat(10f);
        using CanvasTextFormat labelFormat = ShareImageCanvas.CreateTextFormat(10f);

        double max = data.Bars.Count > 0 ? data.Bars.Max(x => x.Minutes) : 0;
        double axisMax = Math.Max(60, Math.Ceiling(max / 60.0) * 60);

        float plotLeft = left + CardPadX;
        float plotTop = top + CardPadY;
        float axisLabelWidth = 0;
        var axisTexts = new string[3];
        for (int i = 0; i < 3; i++)
        {
            axisTexts[i] = FormatHours(axisMax * i / 2.0);
            axisLabelWidth = Math.Max(axisLabelWidth, ShareImageCanvas.MeasureTextWidth(ds, axisTexts[i], axisFormat));
        }
        float axisColumn = axisLabelWidth + 6;
        float barsLeft = plotLeft + axisColumn;
        float barsWidth = ContentWidth - CardPadX * 2 - axisColumn;

        for (int i = 0; i < 3; i++)
        {
            float lineY = plotTop + PlotHeight * (1 - i / 2f);
            ShareImageCanvas.DrawTextRight(ds, axisTexts[i], plotLeft + axisLabelWidth, lineY - 7, axisFormat, ShareImageCanvas.TertiaryText);
            ds.DrawLine(barsLeft, lineY, barsLeft + barsWidth, lineY, GridLineColor, 1f);
        }

        float columnWidth = barsWidth / data.Bars.Count;
        float barWidth = Math.Clamp(columnWidth - 6, 2f, MaxBarWidth);
        float radius = Math.Min(4f, barWidth / 2f);
        for (int i = 0; i < data.Bars.Count; i++)
        {
            PlayTimeShareBar bar = data.Bars[i];
            float centerX = barsLeft + columnWidth * (i + 0.5f);
            if (bar.Minutes > 0)
            {
                float height = (float)Math.Clamp(bar.Minutes / axisMax * PlotHeight, 3, PlotHeight);
                float barTop = plotTop + PlotHeight - height;
                // 只有顶部两角是圆角：先画圆角矩形，再用直角矩形补齐底部
                ds.FillRoundedRectangle(centerX - barWidth / 2, barTop, barWidth, height, radius, radius, ctx.Accent);
                if (height > radius)
                {
                    ds.FillRectangle(centerX - barWidth / 2, barTop + radius, barWidth, height - radius, ctx.Accent);
                }
            }
            float labelWidth = ShareImageCanvas.MeasureTextWidth(ds, bar.Label, labelFormat);
            if (labelWidth <= columnWidth)
            {
                ShareImageCanvas.DrawText(ds, bar.Label, centerX - labelWidth / 2, plotTop + PlotHeight + BarAxisGap, labelFormat, ShareImageCanvas.TertiaryText);
            }
        }
    }


    /// <summary>
    /// 一年热力图：一列一周（周一在上），列上方标注每月首格所在列的月份。
    /// </summary>
    private static void DrawHeatmap(CanvasDrawingSession ds, ShareImageContext ctx, float left, float top, PlayTimeShareSnapshot data)
    {
        using CanvasTextFormat labelFormat = ShareImageCanvas.CreateTextFormat(9f);

        float pitch = HeatmapPitch(data);
        float cell = Math.Max(2f, pitch - HeatmapCellGap);
        float gridLeft = left + CardPadX + HeatmapWeekdayColumn;
        float gridTop = top + CardPadY + HeatmapLabelRow;
        double scaleMax = data.HeatmapDays.Count > 0 ? data.HeatmapDays.Max(x => x.Minutes) : 0;
        Color[] levels = BuildLevelColors(ctx.Accent);

        string[] weekdays = CultureInfo.CurrentUICulture.DateTimeFormat.AbbreviatedDayNames;
        // 只标周一/周三/周五，避免文字挤在一起（DayOfWeek：0 = 周日）
        for (int row = 0; row < HeatmapRows; row += 2)
        {
            int dayOfWeek = (row + 1) % 7;
            float textTop = gridTop + row * pitch + (cell - 9) / 2 - 2;
            ShareImageCanvas.DrawText(ds, weekdays[dayOfWeek], left + CardPadX, textTop, labelFormat, ShareImageCanvas.TertiaryText);
        }

        string[] monthNames = CultureInfo.CurrentUICulture.DateTimeFormat.AbbreviatedMonthNames;
        int lastLabelColumn = int.MinValue;
        for (int i = 0; i < data.HeatmapDays.Count; i++)
        {
            PlayTimeShareHeatmapDay day = data.HeatmapDays[i];
            int column = i / HeatmapRows;
            int row = i % HeatmapRows;
            float cellX = gridLeft + column * pitch;
            float cellY = gridTop + row * pitch;
            if (day.Minutes >= 0)
            {
                Color color = LevelColor(levels, day.Minutes, scaleMax);
                ds.FillRoundedRectangle(cellX, cellY, cell, cell, 1.5f, 1.5f, color);
            }
            // 每月第一天所在的列上方写月份；同列只写一次，且与上一个标签至少隔 2 列
            if (day.Date.Day == 1 && column >= lastLabelColumn + 2)
            {
                ShareImageCanvas.DrawText(ds, monthNames[day.Date.Month - 1], cellX, top + CardPadY, labelFormat, ShareImageCanvas.TertiaryText);
                lastLabelColumn = column;
            }
        }
    }


    #endregion



    /// <summary>
    /// 8 级色阶：第 0 级为无数据底色，1~7 级由强调色向黑/白插值，从暗到亮对应时长从少到多。
    /// 对齐 <see cref="CalendarHeatmap"/> 用系统强调色明暗表示时长的做法。
    /// </summary>
    private static Color[] BuildLevelColors(Color accent)
    {
        return
        [
            EmptyCellColor,
            Lerp(accent, Colors_Black, 0.55f),
            Lerp(accent, Colors_Black, 0.40f),
            Lerp(accent, Colors_Black, 0.20f),
            accent,
            Lerp(accent, Colors_White, 0.18f),
            Lerp(accent, Colors_White, 0.34f),
            Lerp(accent, Colors_White, 0.50f),
        ];
    }


    /// <summary>
    /// 数值映射到色阶：第 k 级覆盖的时长窗口线性增长，7 级刚好到 <paramref name="scaleMax"/>，
    /// 与 <see cref="CalendarHeatmap.LevelBrush"/> 的分段一致。
    /// </summary>
    private static Color LevelColor(Color[] levels, double value, double scaleMax)
    {
        if (value <= 0)
        {
            return levels[0];
        }
        double ceiling = scaleMax <= 0 ? 1 : scaleMax;
        int level = 7;
        for (int k = 1; k <= 7; k++)
        {
            if (value <= ceiling * k * (k + 1) / 56.0)
            {
                level = k;
                break;
            }
        }
        return levels[level];
    }


    private static readonly Color Colors_Black = Color.FromArgb(0xFF, 0, 0, 0);

    private static readonly Color Colors_White = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);


    private static Color Lerp(Color from, Color to, float amount)
    {
        return Color.FromArgb(
            0xFF,
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }


    private static string FormatHours(double minutes)
    {
        return $"{minutes / 60.0:0.#}h";
    }

}
