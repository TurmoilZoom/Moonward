using Microsoft.Graphics.Canvas;
using Starward.Controls;
using Starward.Features.GameRecord.WeeklyDailyData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 月报分享图：当月收入、来源占比、当前周每日表格。
/// </summary>
internal static class MonthlyReportShareRenderer
{

    private const float CanvasWidth = 800f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
    private const float TitleLine = 26f;
    private const float SectionGap = 16f;
    private const float CardPadX = 24f;
    private const float CardPadY = 12f;
    private const float CurrencyIcon = 32f;
    private const float SourceRow = 30f;
    private const float DayCellW = 78f;
    private const float DayCellGap = 2f;
    private const float DayCellH = 30f;
    private const float DateCircle = 24f;
    private const float IconCol = 40f;
    private static readonly Color BarTrack = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
    private static readonly Color CellFill = Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF);


    public static async Task<string> RenderAndSaveAsync(
        MonthlyReportShareSnapshot data,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        CollectIcons(data, ctx.Icons);
        await ctx.Icons.LoadAllAsync(cancellationToken);
        float contentHeight = Measure(data);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            data.FileStem,
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, data),
            cancellationToken);
    }


    private static void CollectIcons(MonthlyReportShareSnapshot data, ShareImageIconCache icons)
    {
        foreach (MonthlyReportShareCurrency currency in data.Currencies)
        {
            icons.Add(currency.Icon);
        }

        foreach (MonthlyReportShareRow row in data.Rows)
        {
            icons.Add(row.Icon);
        }
    }


    private static float Measure(MonthlyReportShareSnapshot data)
    {
        float y = TitleLine;
        y += MeasureStatsCard(data) + SectionGap;
        if (data.Days.Count > 0)
        {
            y += TitleLine + MeasureDailyCard(data);
        }

        return y;
    }


    private static float MeasureStatsCard(MonthlyReportShareSnapshot data)
    {
        float currencyH = data.Currencies.Count > 0 ? CurrencyIcon + 4f + 16f + 4f + 18f : 0f;
        float sourcesH = data.Sources.Count > 0 ? 16f + 8f + 16f + 4f + data.Sources.Count * SourceRow : 0f;
        float separator = data.Currencies.Count > 0 && data.Sources.Count > 0 ? 17f : 0f;
        return CardPadY + currencyH + separator + sourcesH + CardPadY;
    }


    private static float MeasureDailyCard(MonthlyReportShareSnapshot data)
    {
        float header = 16f + 4f + DateCircle + 8f;
        float rows = data.Rows.Count * (DayCellH + 2f);
        return CardPadY + header + rows + CardPadY;
    }


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, MonthlyReportShareSnapshot data)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        ShareImageCanvas.DrawText(ds, data.Title, x, y, ctx.Title, ShareImageCanvas.SecondaryText);
        y += TitleLine;

        float statsH = MeasureStatsCard(data);
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, statsH);
        DrawStatsCard(ds, ctx, x, y, data);
        y += statsH + SectionGap;

        if (data.Days.Count == 0)
        {
            return;
        }

        ShareImageCanvas.DrawText(ds, data.DailyTitle, x, y, ctx.Title, ShareImageCanvas.SecondaryText);
        y += TitleLine;
        float dailyH = MeasureDailyCard(data);
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, dailyH);
        DrawDailyCard(ds, ctx, x, y, data);
    }


    private static void DrawStatsCard(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, MonthlyReportShareSnapshot data)
    {
        float innerX = x + CardPadX;
        float innerW = ContentWidth - CardPadX * 2;
        float cy = y + CardPadY;
        if (data.Currencies.Count > 0)
        {
            DrawCurrencies(ds, ctx, innerX, cy, innerW, data.Currencies);
            cy += CurrencyIcon + 4f + 16f + 4f + 18f;
        }

        if (data.Currencies.Count > 0 && data.Sources.Count > 0)
        {
            ShareImageCanvas.DrawFadeSeparator(ds, innerX, innerX + innerW, cy + 8f);
            cy += 17f;
        }

        if (data.Sources.Count == 0)
        {
            return;
        }

        ShareImageCanvas.DrawText(ds, data.SourcesTitle, innerX, cy, ctx.Small, ShareImageCanvas.SecondaryText);
        cy += 20f;
        foreach (MonthlyReportShareSource source in data.Sources)
        {
            ShareImageCanvas.DrawText(ds, source.Legend, innerX, cy, ctx.Body, ShareImageCanvas.PrimaryText, innerW * 0.7f);
            ShareImageCanvas.DrawText(ds, $"{source.Percent}%", innerX + ShareImageCanvas.MeasureTextWidth(ds, source.Legend ?? "", ctx.Body) + 8f, cy, ctx.Small, ShareImageCanvas.SecondaryText);
            ShareImageCanvas.DrawTextRight(ds, source.Number, innerX + innerW, cy, ctx.Body, ShareImageCanvas.PrimaryText);
            ds.FillRoundedRectangle(innerX, cy + 18f, innerW, 6f, 3f, 3f, BarTrack);
            float fillW = innerW * Math.Clamp(source.Percent / 100f, 0f, 1f);
            if (fillW > 0)
            {
                ds.FillRoundedRectangle(innerX, cy + 18f, Math.Max(fillW, 6f), 6f, 3f, 3f, source.Color);
            }

            cy += SourceRow;
        }
    }


    private static void DrawCurrencies(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float x,
        float y,
        float width,
        IReadOnlyList<MonthlyReportShareCurrency> currencies)
    {
        float gap = currencies.Count >= 3 ? 24f : 48f;
        var widths = new float[currencies.Count];
        float total = 0f;
        for (int i = 0; i < currencies.Count; i++)
        {
            MonthlyReportShareCurrency item = currencies[i];
            float w = Math.Max(CurrencyIcon, Math.Max(
                ShareImageCanvas.MeasureTextWidth(ds, item.Name, ctx.Small),
                ShareImageCanvas.MeasureTextWidth(ds, item.Value, ctx.Body)));
            widths[i] = w;
            total += w;
        }

        total += Math.Max(0, currencies.Count - 1) * gap;
        float cx = x + Math.Max(0, (width - total) / 2f);
        for (int i = 0; i < currencies.Count; i++)
        {
            MonthlyReportShareCurrency item = currencies[i];
            float w = widths[i];
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(item.Icon), new Rect(cx + (w - CurrencyIcon) / 2f, y, CurrencyIcon, CurrencyIcon));
            float nameW = ShareImageCanvas.MeasureTextWidth(ds, item.Name, ctx.Small);
            ShareImageCanvas.DrawText(ds, item.Name, cx + (w - nameW) / 2f, y + CurrencyIcon + 4f, ctx.Small, ShareImageCanvas.SecondaryText);
            float valueW = ShareImageCanvas.MeasureTextWidth(ds, item.Value, ctx.Body);
            ShareImageCanvas.DrawText(ds, item.Value, cx + (w - valueW) / 2f, y + CurrencyIcon + 4f + 16f + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
            cx += w + gap;
        }
    }


    private static void DrawDailyCard(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, MonthlyReportShareSnapshot data)
    {
        float tableW = IconCol + data.Days.Count * DayCellW + Math.Max(0, data.Days.Count - 1) * DayCellGap;
        float left = x + (ContentWidth - tableW) / 2f;
        float headerY = y + CardPadY;
        float circleY = headerY + 20f;
        for (int i = 0; i < data.Days.Count; i++)
        {
            MonthlyReportShareDay day = data.Days[i];
            float cellX = left + IconCol + i * (DayCellW + DayCellGap);
            float weekdayW = ShareImageCanvas.MeasureTextWidth(ds, day.Weekday, ctx.Small);
            ShareImageCanvas.DrawText(ds, day.Weekday, cellX + (DayCellW - weekdayW) / 2f, headerY, ctx.Small, ShareImageCanvas.SecondaryText);
            float circleX = cellX + (DayCellW - DateCircle) / 2f;
            if (day.State == WeekDateState.Today)
            {
                ds.FillEllipse(circleX + DateCircle / 2f, circleY + DateCircle / 2f, DateCircle / 2f, DateCircle / 2f, ctx.Accent);
            }
            else if (day.State == WeekDateState.Past)
            {
                ds.DrawEllipse(circleX + DateCircle / 2f, circleY + DateCircle / 2f, DateCircle / 2f, DateCircle / 2f, ShareImageCanvas.SecondaryText, 1f);
            }

            Color dayColor = day.State == WeekDateState.Today ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : ShareImageCanvas.PrimaryText;
            float dayW = ShareImageCanvas.MeasureTextWidth(ds, day.Day, ctx.Small);
            ShareImageCanvas.DrawText(ds, day.Day, circleX + (DateCircle - dayW) / 2f, circleY + 4f, ctx.Small, dayColor);
        }

        float rowY = circleY + DateCircle + 8f;
        foreach (MonthlyReportShareRow row in data.Rows)
        {
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(row.Icon), new Rect(left + (IconCol - 20f) / 2f, rowY + 5f, 20f, 20f));
            int count = Math.Min(row.Cells.Count, data.Days.Count);
            for (int i = 0; i < count; i++)
            {
                float cellX = left + IconCol + i * (DayCellW + DayCellGap);
                ds.FillRoundedRectangle(cellX, rowY, DayCellW, DayCellH, 3f, 3f, CellFill);
                string text = row.Cells[i];
                if (!string.IsNullOrEmpty(text))
                {
                    float tw = ShareImageCanvas.MeasureTextWidth(ds, text, ctx.Body);
                    ShareImageCanvas.DrawText(ds, text, cellX + (DayCellW - tw) / 2f, rowY + 6f, ctx.Body, ShareImageCanvas.PrimaryText);
                }
            }

            rowY += DayCellH + 2f;
        }
    }

}


/// <summary>月报分享图在 UI 线程采集的快照，供后台 Win2D 绘制。</summary>
internal sealed class MonthlyReportShareSnapshot
{
    public required string FileStem { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<MonthlyReportShareCurrency> Currencies { get; init; }
    public required string SourcesTitle { get; init; }
    public required IReadOnlyList<MonthlyReportShareSource> Sources { get; init; }
    public required string DailyTitle { get; init; }
    public required IReadOnlyList<MonthlyReportShareDay> Days { get; init; }
    public required IReadOnlyList<MonthlyReportShareRow> Rows { get; init; }


    public static IReadOnlyList<MonthlyReportShareDay> CaptureDays(IEnumerable<WeekDateCell>? days)
    {
        if (days is null)
        {
            return [];
        }

        return days.Select(d => new MonthlyReportShareDay
        {
            Weekday = d.WeekdayText,
            Day = d.DayText,
            State = d.State,
        }).ToList();
    }


    public static IReadOnlyList<MonthlyReportShareRow> CaptureRows(IEnumerable<WeeklyResourceRow>? rows)
    {
        if (rows is null)
        {
            return [];
        }

        return rows.Select(r => new MonthlyReportShareRow
        {
            Icon = r.Icon?.UriSource?.OriginalString ?? "",
            Cells = r.Cells.Select(c => c.DisplayText).ToList(),
        }).ToList();
    }


    public static IReadOnlyList<MonthlyReportShareSource> CaptureSources(IEnumerable<ColorRectChart.ChartLegend>? series)
    {
        if (series is null)
        {
            return [];
        }

        return series.Select(s => new MonthlyReportShareSource
        {
            Legend = s.Legend ?? "",
            Percent = s.Percent,
            Number = s.Number.ToString(),
            Color = s.Color,
        }).ToList();
    }
}


internal sealed class MonthlyReportShareCurrency
{
    public required string Icon { get; init; }
    public required string Name { get; init; }
    public required string Value { get; init; }
}


internal sealed class MonthlyReportShareSource
{
    public required string Legend { get; init; }
    public required int Percent { get; init; }
    public required string Number { get; init; }
    public required Color Color { get; init; }
}


internal sealed class MonthlyReportShareDay
{
    public required string Weekday { get; init; }
    public required string Day { get; init; }
    public required WeekDateState State { get; init; }
}


internal sealed class MonthlyReportShareRow
{
    public required string Icon { get; init; }
    public required IReadOnlyList<string> Cells { get; init; }
}
