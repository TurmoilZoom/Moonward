using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Starward.Codec.ICC;
using Starward.Core;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using Starward.Features.Background;
using Starward.Features.Codec;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Starward.Features.Gacha;

/// <summary>
/// 使用 Win2D 离屏绘制抽卡统计分享图（统计头 + 完整 5★/S 记录），保存为 PNG。
/// 记录区跟随页面视图：列表为逐行绘制，紧凑为 5 列图标网格（高度约为列表的一半，适合记录很多的账号）。
/// 壁纸经降饱和、曝光与暗角后再做磨砂；卡片为等高玻璃板（投影 + 细顶缘 + 主题色薄雾），字色分主/次/辅三级。
/// </summary>
internal static class GachaShareImageRenderer
{

    // 3× 逻辑像素，分享到社交软件放大时文字/图标仍清楚
    private const float Dpi = 288f;

    private const float CardWidth = 270f;
    private const float CardSpacing = 16f;
    private const float OuterMargin = 28f;
    private const float CardPaddingH = 14f;
    private const float CardPaddingV = 12f;
    private const float CardCornerRadius = 12f;
    private const float RowSpacing = 4f;
    private const float ListSectionSpacing = 10f;
    private const float ItemRowHeight = 28f;
    private const float IconSize = 28f;
    private const float IconColumnWidth = 40f;
    private const float IconCornerRadius = 6f;
    private const float ItemPityBarCornerRadius = 4f;
    private const float FooterHeight = 16f;
    private const float FooterGap = 4f;
    private const float FooterBottom = 10f;

    // 卡片级当前垫数进度块（对齐 GachaStatsCard / ZZZGachaStatsCard Grid.Row=5：Margin 0,2,0,4，RowSpacing 3）
    private const float PityProgressMarginTop = 2f;
    private const float PityProgressMarginBottom = 4f;
    private const float PityProgressRowSpacing = 3f;
    private const float PityProgressBarHeight = 6f;
    private const float PityProgressCornerRadius = 3f;
    private const float PityProgressLabelHeight = 16f;
    private const float PityProgressBlockHeight = PityProgressMarginTop + PityProgressLabelHeight + PityProgressRowSpacing + PityProgressBarHeight + PityProgressMarginBottom;

    // 紧凑视图方块（对齐 GachaStatsCard / ZZZGachaStatsCard 的 Rarity5TileTemplate：图标 44 + 间隔 2 + 保底条 16，行距 4，每行 5 个）
    private const int CompactColumns = 5;
    private const float CompactIconSize = 44f;
    private const float CompactStripGap = 2f;
    private const float CompactStripHeight = 16f;
    private const float CompactTileHeight = CompactIconSize + CompactStripGap + CompactStripHeight;
    private const float CompactRowSpacing = 4f;
    private const float CompactTileCornerRadius = 6f;
    private const float CompactStripCornerRadius = 4f;
    private const float CompactBadgeWidth = 24f;
    private const float CompactBadgeHeight = 13f;
    private const float CompactBadgeCornerRadius = 4f;
    private const float CompactNameInset = 2f;

    private static readonly Color PrimaryText = Color.FromArgb(0xFF, 0xF7, 0xF7, 0xF7);
    private static readonly Color SecondaryText = Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8);
    private static readonly Color TertiaryText = Color.FromArgb(0xFF, 0xA3, 0xA3, 0xA3);
    private static readonly Color OnAccentText = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    // 略提亮，暗底上比纯 #FFA500 更干净
    private static readonly Color Rarity5 = Color.FromArgb(0xFF, 0xFF, 0xB4, 0x2E);
    private static readonly Color Rarity5Hi = Color.FromArgb(0xFF, 0xFF, 0xD0, 0x70);
    private static readonly Color RarityAverage = Color.FromArgb(0xFF, 0x4E, 0xC4, 0x8C);
    // 比页面 0.4 更实；绿/红略压暗，浅色角色名才压得住
    private const float PityBarFillOpacity = 0.58f;
    private const float PityBarTailOpacityScale = 0.80f;
    private static readonly Color PityGreen = Color.FromArgb(0xFF, 0x14, 0xC4, 0x74);
    private static readonly Color PityRed = Color.FromArgb(0xFF, 0xCC, 0x42, 0x2E);
    private static readonly Color SeparatorColor = Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF);
    private static readonly Color PityProgressTrack = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
    // 紧凑方块底板取 Rarity5 的 25% 透明度，与页面 #40FFA500 同一观感
    private static readonly Color CompactTileBackground = Color.FromArgb(0x40, 0xFF, 0xB4, 0x2E);
    private static readonly Color CompactBadgeText = Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F);

    // 预暗化只压高光，避免浅壁纸把亚克力洗白，同时保留更多原图颜色
    private static readonly Color BgPreDarkenOverlay = Color.FromArgb(0x28, 0x00, 0x00, 0x00);

    // 卡片单独对原始壁纸再糊一层；比大背景 32 更散，玻璃底更软
    private const float CardAcrylicBlurAmount = 40f;
    // 半透明黑压暗，避免深灰罩把壁纸染成炭灰色
    private static readonly Color CardAcrylicTint = Color.FromArgb(0x7A, 0x00, 0x00, 0x00);
    private static readonly Color CardBorderColor = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
    // 顶缘只留一根细线，不再铺大面积白雾
    private static readonly Color CardHairlineColor = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
    private const float CardShadowBlur = 16f;
    private const float CardShadowOffsetY = 6f;
    private static readonly Color CardShadowColor = Color.FromArgb(0x66, 0x00, 0x00, 0x00);

    // 大背景：保持较强模糊，但降低实色罩，用暗角 + 主题色薄雾替代“一层灰”
    private const float BgLightAcrylicBlurAmount = 32f;
    private const float BgSaturation = 0.88f;
    private const float BgExposure = -0.04f;
    private const float BgVignetteAmount = 0.30f;
    private static readonly Color BgLightAcrylicTint = Color.FromArgb(0x33, 0x00, 0x00, 0x00);
    private static readonly Color BgVignetteColor = Color.FromArgb(0xFF, 0x08, 0x08, 0x0C);

    private static readonly FontWeight NormalFontWeight = new() { Weight = 400 };
    private static readonly FontWeight SemiBoldFontWeight = new() { Weight = 600 };


    /// <summary>
    /// 离屏渲染当前所选卡池统计卡片并保存 PNG。
    /// 壁纸先调色再磨砂；各卡拉齐到同一高度，叠投影与细顶缘。
    /// </summary>
    /// <param name="stats">要绘制的卡池统计列表（横向排列，顺序由调用方决定）。</param>
    /// <param name="gameBiz">当前游戏区服，用于 5★/S 文案与 pity 规则分支。</param>
    /// <param name="backgroundFile">背景图/快照本地路径（由调用方确保视频已转为当前帧快照 PNG）；为 null 或不存在时使用纯色背景。</param>
    /// <param name="uid">玩家 UID，用于输出文件名与图脚标。</param>
    /// <param name="accentColor">主题强调色（须在 UI 线程读取后传入，渲染在后台线程执行）。</param>
    /// <param name="viewMode">记录区视图：列表逐行绘制；紧凑按 5 列图标网格绘制。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已保存 PNG 的完整路径。</returns>
    /// <exception cref="ArgumentException"><paramref name="stats"/> 为空时抛出。</exception>
    public static async Task<string> RenderAndSaveAsync(
        IReadOnlyList<GachaTypeStats> stats,
        GameBiz gameBiz,
        string? backgroundFile,
        long uid,
        Color accentColor,
        GachaRecordViewMode viewMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.Count == 0)
        {
            throw new ArgumentException("No gacha stats to render.", nameof(stats));
        }

        bool isZzz = gameBiz.Game == GameBiz.nap;
        string rarityLabel = isZzz ? "S" : "5★";

        var device = CanvasDevice.GetSharedDevice();
        var iconLoads = new Dictionary<string, Task<CanvasBitmap?>>(StringComparer.Ordinal);
        var disposableBitmaps = new List<CanvasBitmap>();

        try
        {
            await PreloadIconsAsync(stats, iconLoads, disposableBitmaps, cancellationToken);

            using var titleFormat = CreateTextFormat(16f, SemiBoldFontWeight);
            using var bodyFormat = CreateTextFormat(14f, trimming: true);
            using var smallFormat = CreateTextFormat(12f);
            using var capsuleFormat = CreateTextFormat(11f, SemiBoldFontWeight);
            using var upFormat = CreateTextFormat(12f, NormalFontWeight, FontStyle.Italic);
            using var tilePityFormat = CreateTextFormat(12f, SemiBoldFontWeight);
            using var tileBadgeFormat = CreateTextFormat(10f, SemiBoldFontWeight);
            using var tileNameFormat = CreateTextFormat(10f, trimming: true);
            tileNameFormat.WordWrapping = CanvasWordWrapping.Wrap;
            tileNameFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;

            var cardHeights = stats.Select(s => MeasureCardHeight(s, viewMode)).ToArray();
            var iconBitmaps = iconLoads.ToDictionary(x => x.Key, x => x.Value.Result, StringComparer.Ordinal);
            float contentHeight = cardHeights.Max();
            float canvasWidth = OuterMargin * 2 + stats.Count * CardWidth + Math.Max(0, stats.Count - 1) * CardSpacing;
            float canvasHeight = OuterMargin + contentHeight + FooterGap + FooterHeight + FooterBottom;

            using var renderTarget = new CanvasRenderTarget(device, canvasWidth, canvasHeight, Dpi);
            using (CanvasDrawingSession ds = renderTarget.CreateDrawingSession())
            {
                ds.Clear(Colors.Transparent);
                ds.Antialiasing = CanvasAntialiasing.Antialiased;
                // 离屏 PNG 没有稳定底色，ClearType 会带彩边、发糊；灰度抗锯齿更利落
                ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;

                using var bgLayer = new CanvasRenderTarget(device, canvasWidth, canvasHeight, Dpi);
                using (CanvasDrawingSession bgDs = bgLayer.CreateDrawingSession())
                {
                    await DrawRawBackgroundAsync(bgDs, device, canvasWidth, canvasHeight, backgroundFile, accentColor, cancellationToken);
                    bgDs.FillRectangle(0, 0, canvasWidth, canvasHeight, BgPreDarkenOverlay);
                }

                DrawGradedBackground(ds, bgLayer, canvasWidth, canvasHeight, accentColor);

                float cardTop = OuterMargin;
                float cardLeft = OuterMargin;
                for (int i = 0; i < stats.Count; i++)
                {
                    GachaTypeStats stat = stats[i];
                    DrawCard(ds, device, stat, cardLeft, cardTop, contentHeight, rarityLabel, accentColor, viewMode,
                             titleFormat, bodyFormat, smallFormat, capsuleFormat, upFormat,
                             tilePityFormat, tileBadgeFormat, tileNameFormat, iconBitmaps, bgLayer);
                    cardLeft += CardWidth + CardSpacing;
                }

                DrawFooter(ds, uid, cardTop + contentHeight + FooterGap, canvasWidth, smallFormat);
            }

            string folder = Path.Combine(AppConfig.CacheFolder, "cache", "share");
            Directory.CreateDirectory(folder);
            string filePath = Path.Combine(folder, $"gacha_{uid}_{DateTime.Now:yyyyMMddHHmmss}.png");
            await using var fs = File.Create(filePath);
            await ImageSaver.SaveAsPngAsync(renderTarget, fs, ColorPrimaries.BT709);
            return filePath;
        }
        finally
        {
            foreach (CanvasBitmap bitmap in disposableBitmaps)
            {
                bitmap.Dispose();
            }
        }
    }


    /// <summary>
    /// 预加载所有列表项图标，相同 URI 共享同一加载任务。
    /// </summary>
    private static async Task PreloadIconsAsync(
        IReadOnlyList<GachaTypeStats> stats,
        Dictionary<string, Task<CanvasBitmap?>> iconLoads,
        List<CanvasBitmap> disposableBitmaps,
        CancellationToken cancellationToken)
    {
        var icons = stats.SelectMany(s => s.List_5 ?? [])
                         .Select(x => x.Icon)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.Ordinal);
        foreach (string icon in icons)
        {
            if (!iconLoads.ContainsKey(icon))
            {
                iconLoads[icon] = LoadIconBitmapAsync(icon, cancellationToken);
            }
        }

        foreach (Task<CanvasBitmap?> task in iconLoads.Values)
        {
            CanvasBitmap? bitmap = await task;
            if (bitmap is not null)
            {
                disposableBitmaps.Add(bitmap);
            }
        }
    }


    /// <summary>
    /// 按单张卡的内容测量高度（顶对齐，记录区随 5★ 条数与视图伸缩）。绘制时会拉齐到本批最高卡。
    /// </summary>
    /// <param name="stats">卡池统计。</param>
    /// <param name="viewMode">记录区视图，决定 5★ 记录按行还是按网格计高。</param>
    /// <returns>卡片总高度（逻辑像素）。</returns>
    private static float MeasureCardHeight(GachaTypeStats stats, GachaRecordViewMode viewMode)
    {
        float height = CardPaddingV * 2;
        height += 22f;
        height += RowSpacing + 16f;
        height += RowSpacing + 1 + RowSpacing;
        height += 20f + RowSpacing;
        height += 20f + RowSpacing;
        if (stats.ShowPityProgress)
        {
            height += PityProgressBlockHeight;
        }
        height += ListSectionSpacing;
        int count = stats.List_5?.Count ?? 0;
        height += viewMode == GachaRecordViewMode.Compact ? MeasureCompactGridHeight(count) : count * ItemRowHeight;
        return height;
    }


    /// <summary>
    /// 紧凑网格的记录区高度：<c>行数 × 方块高 + (行数 − 1) × 行距</c>，每行 <see cref="CompactColumns"/> 个。
    /// </summary>
    /// <param name="count">记录条数。</param>
    /// <returns>网格高度；无记录时为 0。</returns>
    private static float MeasureCompactGridHeight(int count)
    {
        if (count <= 0)
        {
            return 0f;
        }
        int rows = (count + CompactColumns - 1) / CompactColumns;
        return rows * CompactTileHeight + (rows - 1) * CompactRowSpacing;
    }


    /// <summary>
    /// 绘制单张统计卡片（投影 → 玻璃底 → 头部统计 → 5★ 列表或紧凑网格）。
    /// 背景层 bgLayer 用于对卡片区域做局部高斯模糊 + 磨砂着色。
    /// </summary>
    private static void DrawCard(
        CanvasDrawingSession ds,
        CanvasDevice device,
        GachaTypeStats stats,
        float left,
        float top,
        float height,
        string rarityLabel,
        Color accentColor,
        GachaRecordViewMode viewMode,
        CanvasTextFormat titleFormat,
        CanvasTextFormat bodyFormat,
        CanvasTextFormat smallFormat,
        CanvasTextFormat capsuleFormat,
        CanvasTextFormat upFormat,
        CanvasTextFormat tilePityFormat,
        CanvasTextFormat tileBadgeFormat,
        CanvasTextFormat tileNameFormat,
        IReadOnlyDictionary<string, CanvasBitmap?> iconBitmaps,
        CanvasBitmap bgLayer)
    {
        DrawCardShadow(ds, device, left, top, CardWidth, height, CardCornerRadius);
        DrawAcrylicCardBackground(ds, device, bgLayer, left, top, CardWidth, height, CardCornerRadius, accentColor);

        float x = left + CardPaddingH;
        float y = top + CardPaddingV;
        float innerWidth = CardWidth - CardPaddingH * 2;
        float rightX = x + innerWidth;

        float titleBlockRight = rightX;
        string countText = stats.Count.ToString();
        if (!string.IsNullOrEmpty(countText))
        {
            float countWidth = MeasureTextWidth(ds, countText, titleFormat);
            DrawText(ds, countText, rightX - countWidth, y, titleFormat, PrimaryText);
            titleBlockRight -= countWidth + 8f;
        }

        if (stats.ShowFiftyFiftyStreakCapsules)
        {
            float capsuleRight = titleBlockRight;
            if (stats.ShowFiftyFiftyMissStreakCapsule)
            {
                capsuleRight = DrawCapsule(ds, device, stats.MaxFiftyFiftyMissStreakText, capsuleRight, y, capsuleFormat, accentColor) - 6f;
            }

            if (stats.ShowFiftyFiftyUpStreakCapsule)
            {
                DrawCapsule(ds, device, stats.MaxFiftyFiftyUpStreakText, capsuleRight, y, capsuleFormat, accentColor);
            }

            titleBlockRight = Math.Min(titleBlockRight, capsuleRight - 120f);
        }

        DrawText(ds, stats.GachaTypeText, x, y, titleFormat, PrimaryText, titleBlockRight - x);
        y += 22f;

        DrawText(ds, FormatTimeRange(stats), x, y, smallFormat, TertiaryText);
        y += 16f + RowSpacing;

        DrawFadeSeparator(ds, x, rightX, y, SeparatorColor);
        y += 1f + RowSpacing;

        string averageLeft = $"{rarityLabel}{Lang.GachaStatsCard_Average}{stats.Avarage_5_Desc_Text}";
        string averageRight = $"{stats.Average_5_Text}{stats.Avarage_5_Up_Text}";
        DrawText(ds, averageLeft, x, y, bodyFormat, RarityAverage);
        DrawText(ds, averageRight, rightX - MeasureTextWidth(ds, averageRight, bodyFormat), y, bodyFormat, RarityAverage);
        y += 20f + RowSpacing;

        if (stats.HasUpItem)
        {
            DrawText(ds, Lang.GachaStatsCard_NoUpProbability, x, y, bodyFormat, Rarity5);
            DrawText(ds, stats.FiftyFiftyNoUpText, rightX - MeasureTextWidth(ds, stats.FiftyFiftyNoUpText, bodyFormat), y, bodyFormat, Rarity5);
        }
        else
        {
            string statsLeft = $"{rarityLabel}{Lang.GachaStatsCard_Stats}";
            string statsRight = $"{stats.Count_5} [{stats.Ratio_5:P2}]";
            DrawText(ds, statsLeft, x, y, bodyFormat, Rarity5);
            DrawText(ds, statsRight, rightX - MeasureTextWidth(ds, statsRight, bodyFormat), y, bodyFormat, Rarity5);
        }
        y += 20f + RowSpacing;

        if (stats.ShowPityProgress)
        {
            y = DrawPityProgress(ds, device, stats, x, y, innerWidth, rightX, smallFormat);
        }

        y += ListSectionSpacing;

        if (stats.List_5 is { Count: > 0 })
        {
            if (viewMode == GachaRecordViewMode.Compact)
            {
                DrawCompactGrid(ds, device, stats.List_5, x, y, innerWidth, tilePityFormat, tileBadgeFormat, tileNameFormat, iconBitmaps);
                return;
            }

            float nameX = x + IconColumnWidth;
            float nameWidth = innerWidth - IconColumnWidth - 48f;
            foreach (GachaLogItemEx item in stats.List_5)
            {
                DrawListItem(ds, device, item, x, y, nameX, nameWidth, rightX, bodyFormat, upFormat, iconBitmaps);
                y += ItemRowHeight;
            }
        }
    }


    /// <summary>
    /// 紧凑视图：把 5★/S 记录按 <see cref="CompactColumns"/> 列图标网格绘制（新→旧，自左向右、自上而下）。
    /// 列间距按内容区宽度两端对齐；页面内 WrapPanel 为固定间距左对齐，差值不到 2px。
    /// </summary>
    /// <param name="ds">绘制会话。</param>
    /// <param name="device">用于创建圆角几何。</param>
    /// <param name="items">5★/S 记录（按时间倒序）。</param>
    /// <param name="left">内容区左缘。</param>
    /// <param name="top">网格顶部。</param>
    /// <param name="innerWidth">内容区宽度。</param>
    /// <param name="pityFormat">方块抽数字体。</param>
    /// <param name="badgeFormat">UP 角标字体。</param>
    /// <param name="nameFormat">图标缺失时的名称回退字体（可换行）。</param>
    /// <param name="iconBitmaps">已预加载的图标位图。</param>
    private static void DrawCompactGrid(
        CanvasDrawingSession ds,
        CanvasDevice device,
        IReadOnlyList<GachaLogItemEx> items,
        float left,
        float top,
        float innerWidth,
        CanvasTextFormat pityFormat,
        CanvasTextFormat badgeFormat,
        CanvasTextFormat nameFormat,
        IReadOnlyDictionary<string, CanvasBitmap?> iconBitmaps)
    {
        float columnSpacing = Math.Max(0f, (innerWidth - CompactColumns * CompactIconSize) / (CompactColumns - 1));
        for (int i = 0; i < items.Count; i++)
        {
            float tileLeft = left + (i % CompactColumns) * (CompactIconSize + columnSpacing);
            float tileTop = top + (i / CompactColumns) * (CompactTileHeight + CompactRowSpacing);
            DrawCompactTile(ds, device, items[i], tileLeft, tileTop, pityFormat, badgeFormat, nameFormat, iconBitmaps);
        }
    }


    /// <summary>
    /// 绘制单个紧凑方块（对应页面 Rarity5TileTemplate）：圆角稀有度底板 → 等比图标或名称回退 → 右上 UP 角标 → 下方迷你保底条与抽数。
    /// </summary>
    /// <param name="ds">绘制会话。</param>
    /// <param name="device">用于创建圆角几何。</param>
    /// <param name="item">记录。</param>
    /// <param name="left">方块左缘。</param>
    /// <param name="top">方块顶部。</param>
    /// <param name="pityFormat">抽数字体。</param>
    /// <param name="badgeFormat">UP 角标字体。</param>
    /// <param name="nameFormat">名称回退字体。</param>
    /// <param name="iconBitmaps">已预加载的图标位图。</param>
    private static void DrawCompactTile(
        CanvasDrawingSession ds,
        CanvasDevice device,
        GachaLogItemEx item,
        float left,
        float top,
        CanvasTextFormat pityFormat,
        CanvasTextFormat badgeFormat,
        CanvasTextFormat nameFormat,
        IReadOnlyDictionary<string, CanvasBitmap?> iconBitmaps)
    {
        using var plate = CanvasGeometry.CreateRoundedRectangle(
            device, left, top, CompactIconSize, CompactIconSize, CompactTileCornerRadius, CompactTileCornerRadius);
        ds.FillGeometry(plate, CompactTileBackground);

        using (ds.CreateLayer(1f, plate))
        {
            if (!string.IsNullOrWhiteSpace(item.Icon)
                && iconBitmaps.TryGetValue(item.Icon, out CanvasBitmap? icon)
                && icon is not null)
            {
                DrawImageHighQuality(ds, icon, FitUniform(icon, left, top, CompactIconSize));
            }
            else
            {
                DrawTileNameFallback(ds, item.Name, left, top, CompactIconSize, nameFormat);
            }

            if (item.HasUpItem && item.IsUp)
            {
                // 角标向上、向右各多出一个圆角半径，被底板裁剪层裁掉后只剩左下圆角，右上角随底板圆角，对应 XAML CornerRadius="0,6,0,4"
                float badgeLeft = left + CompactIconSize - CompactBadgeWidth;
                using var badge = CanvasGeometry.CreateRoundedRectangle(
                    device,
                    badgeLeft,
                    top - CompactBadgeCornerRadius,
                    CompactBadgeWidth + CompactBadgeCornerRadius,
                    CompactBadgeHeight + CompactBadgeCornerRadius,
                    CompactBadgeCornerRadius,
                    CompactBadgeCornerRadius);
                ds.FillGeometry(badge, Rarity5);
                DrawTextInkCentered(ds, "UP", badgeLeft, top, CompactBadgeWidth, CompactBadgeHeight, badgeFormat, CompactBadgeText);
            }
        }

        float stripTop = top + CompactIconSize + CompactStripGap;
        using var strip = CanvasGeometry.CreateRoundedRectangle(
            device, left, stripTop, CompactIconSize, CompactStripHeight, CompactStripCornerRadius, CompactStripCornerRadius);
        ds.FillGeometry(strip, PityProgressTrack);
        DrawPityBar(ds, device, item, left, stripTop, CompactIconSize, CompactStripHeight);
        DrawTextInkCentered(ds, item.Pity.ToString(), left, stripTop, CompactIconSize, CompactStripHeight, pityFormat, PrimaryText);
    }


    /// <summary>
    /// 图标缺失（新物品尚未同步维基）时，在方块内居中绘制名称，对应页面方块的名称回退。
    /// </summary>
    /// <param name="ds">绘制会话。</param>
    /// <param name="name">物品名称。</param>
    /// <param name="left">方块左缘。</param>
    /// <param name="top">方块顶部。</param>
    /// <param name="size">方块边长。</param>
    /// <param name="format">可换行、居中、带省略号的名称字体。</param>
    private static void DrawTileNameFallback(CanvasDrawingSession ds, string? name, float left, float top, float size, CanvasTextFormat format)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }
        float boxSize = size - CompactNameInset * 2;
        using var layout = new CanvasTextLayout(ds, name, format, boxSize, boxSize);
        float y = top + CompactNameInset + Math.Max(0f, (boxSize - (float)layout.LayoutBounds.Height) / 2f);
        ds.DrawTextLayout(layout, left + CompactNameInset, y, SecondaryText);
    }


    /// <summary>
    /// 按字形墨迹边界把单行短文本（抽数、角标）居中到矩形内，效果对应 XAML 的 <c>TextLineBounds="Tight"</c>。
    /// </summary>
    /// <param name="ds">绘制会话。</param>
    /// <param name="text">文本。</param>
    /// <param name="left">矩形左缘。</param>
    /// <param name="top">矩形顶部。</param>
    /// <param name="width">矩形宽度。</param>
    /// <param name="height">矩形高度。</param>
    /// <param name="format">字体。</param>
    /// <param name="color">字色。</param>
    private static void DrawTextInkCentered(
        CanvasDrawingSession ds,
        string text,
        float left,
        float top,
        float width,
        float height,
        CanvasTextFormat format,
        Color color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        using var layout = new CanvasTextLayout(ds, text, format, 4096f, 0);
        Rect ink = layout.DrawBounds;
        float x = left + (width - (float)ink.Width) / 2f - (float)ink.X;
        float y = top + (height - (float)ink.Height) / 2f - (float)ink.Y;
        ds.DrawTextLayout(layout, x, y, color);
    }


    /// <summary>
    /// 按等比缩放（Uniform）把位图放进正方形区域并居中，非正方形图标不变形。
    /// </summary>
    /// <param name="bitmap">图标位图。</param>
    /// <param name="left">区域左缘。</param>
    /// <param name="top">区域顶部。</param>
    /// <param name="size">区域边长。</param>
    /// <returns>绘制目标矩形。</returns>
    private static Rect FitUniform(CanvasBitmap bitmap, float left, float top, float size)
    {
        float imgW = bitmap.SizeInPixels.Width;
        float imgH = bitmap.SizeInPixels.Height;
        if (imgW <= 0 || imgH <= 0)
        {
            return new Rect(left, top, size, size);
        }
        float scale = Math.Min(size / imgW, size / imgH);
        float drawW = imgW * scale;
        float drawH = imgH * scale;
        return new Rect(left + (size - drawW) / 2f, top + (size - drawH) / 2f, drawW, drawH);
    }


    /// <summary>
    /// 在卡片下方画一层软投影，让玻璃板从壁纸上浮起来。
    /// </summary>
    private static void DrawCardShadow(
        CanvasDrawingSession ds,
        CanvasDevice device,
        float left,
        float top,
        float width,
        float height,
        float cornerRadius)
    {
        const float pad = 28f;
        using var mask = new CanvasRenderTarget(device, width + pad * 2, height + pad * 2, Dpi);
        using (CanvasDrawingSession maskDs = mask.CreateDrawingSession())
        {
            maskDs.Clear(Colors.Transparent);
            maskDs.FillRoundedRectangle(pad, pad, width, height, cornerRadius, cornerRadius, Colors.White);
        }

        using var shadow = new ShadowEffect
        {
            Source = mask,
            BlurAmount = CardShadowBlur,
            ShadowColor = CardShadowColor,
        };
        ds.DrawImage(shadow, left - pad, top - pad + CardShadowOffsetY);
    }


    /// <summary>
    /// 绘制卡片的亚克力背景：局部模糊 → 着色 → 很薄的主题色顶雾 → 1px 顶缘 → 细边框。
    /// </summary>
    private static void DrawAcrylicCardBackground(
        CanvasDrawingSession ds,
        CanvasDevice device,
        CanvasBitmap bgLayer,
        float left,
        float top,
        float width,
        float height,
        float cornerRadius,
        Color accentColor)
    {
        using var cardGeometry = CanvasGeometry.CreateRoundedRectangle(device, left, top, width, height, cornerRadius, cornerRadius);

        using var blur = new GaussianBlurEffect
        {
            Source = bgLayer,
            BlurAmount = CardAcrylicBlurAmount,
            BorderMode = EffectBorderMode.Soft,
        };

        using (ds.CreateLayer(1f, cardGeometry))
        {
            ds.DrawImage(blur, left, top, new Rect(left, top, width, height));
        }

        ds.FillGeometry(cardGeometry, CardAcrylicTint);

        using (ds.CreateLayer(1f, cardGeometry))
        {
            Color washTop = Color.FromArgb(0x14, accentColor.R, accentColor.G, accentColor.B);
            var washStops = new[]
            {
                new CanvasGradientStop { Position = 0f, Color = washTop },
                new CanvasGradientStop { Position = 0.16f, Color = Colors.Transparent },
                new CanvasGradientStop { Position = 1f, Color = Colors.Transparent },
            };
            using var wash = new CanvasLinearGradientBrush(ds, washStops)
            {
                StartPoint = new Vector2(left, top),
                EndPoint = new Vector2(left, top + height),
            };
            ds.FillRectangle(left, top, width, height, wash);

            ds.DrawLine(
                left + cornerRadius,
                top + 0.6f,
                left + width - cornerRadius,
                top + 0.6f,
                CardHairlineColor,
                1f);
        }

        ds.DrawGeometry(cardGeometry, CardBorderColor, 1f);
    }


    /// <summary>
    /// 绘制卡片级当前最高稀有度垫数进度（对应 <c>GachaStatsCard</c> / <c>ZZZGachaStatsCard</c> 第 5 行 ProgressBar）。
    /// 仅由调用方在 <see cref="GachaTypeStats.ShowPityProgress"/> 为 true 时调用；
    /// 非 UP 卡池不绘制右侧大小保底文案。与列表行内的 <see cref="DrawPityBar"/> 无关。
    /// </summary>
    /// <param name="ds">绘制会话。</param>
    /// <param name="device">用于创建圆角轨道几何。</param>
    /// <param name="stats">当前卡池统计（文案与进度值）。</param>
    /// <param name="x">内容区左缘。</param>
    /// <param name="y">本块顶部（尚未计入上边距）。</param>
    /// <param name="innerWidth">内容区宽度，进度条拉满该宽度。</param>
    /// <param name="rightX">内容区右缘，用于右对齐保底文案。</param>
    /// <param name="labelFormat">12pt 标签字体。</param>
    /// <returns>本块底部 Y（已含底边距），供后续 5★/S 列表接着排。</returns>
    private static float DrawPityProgress(
        CanvasDrawingSession ds,
        CanvasDevice device,
        GachaTypeStats stats,
        float x,
        float y,
        float innerWidth,
        float rightX,
        CanvasTextFormat labelFormat)
    {
        y += PityProgressMarginTop;

        DrawText(ds, stats.PityProgressText, x, y, labelFormat, TertiaryText);
        if (stats.HasUpItem)
        {
            string guaranteeText = stats.PityGuaranteeText;
            DrawText(ds, guaranteeText, rightX - MeasureTextWidth(ds, guaranteeText, labelFormat), y, labelFormat, Rarity5);
        }
        y += PityProgressLabelHeight + PityProgressRowSpacing;

        using var trackGeometry = CanvasGeometry.CreateRoundedRectangle(
            device, x, y, innerWidth, PityProgressBarHeight, PityProgressCornerRadius, PityProgressCornerRadius);
        ds.FillGeometry(trackGeometry, PityProgressTrack);

        int max = stats.Pity_5_Max;
        int value = stats.Pity_5;
        if (max > 0 && value > 0)
        {
            float fillWidth = innerWidth * Math.Clamp(value / (float)max, 0f, 1f);
            if (fillWidth > 0f)
            {
                using (ds.CreateLayer(1f, trackGeometry))
                {
                    using var fillGeometry = CanvasGeometry.CreateRoundedRectangle(
                        device, x, y, fillWidth, PityProgressBarHeight, PityProgressCornerRadius, PityProgressCornerRadius);
                    var fillStops = new[]
                    {
                        new CanvasGradientStop { Position = 0f, Color = Rarity5Hi },
                        new CanvasGradientStop { Position = 1f, Color = Rarity5 },
                    };
                    using var fillBrush = new CanvasLinearGradientBrush(ds, fillStops)
                    {
                        StartPoint = new Vector2(x, y),
                        EndPoint = new Vector2(x + innerWidth, y),
                    };
                    ds.FillGeometry(fillGeometry, fillBrush);

                    ds.FillRectangle(x, y, fillWidth, 0.8f, Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
                }
            }
        }

        y += PityProgressBarHeight + PityProgressMarginBottom;
        return y;
    }


    /// <summary>
    /// 绘制单行 5★ 记录：圆角保底色条、圆角图标、名称、pity、up! 标记。
    /// </summary>
    private static void DrawListItem(
        CanvasDrawingSession ds,
        CanvasDevice device,
        GachaLogItemEx item,
        float rowLeft,
        float rowTop,
        float nameX,
        float nameWidth,
        float rightX,
        CanvasTextFormat bodyFormat,
        CanvasTextFormat upFormat,
        IReadOnlyDictionary<string, CanvasBitmap?> iconBitmaps)
    {
        float barTop = rowTop + 2f;
        DrawPityBar(ds, device, item, nameX - 4f, barTop, nameWidth + 8f, 24f);

        if (!string.IsNullOrWhiteSpace(item.Icon)
            && iconBitmaps.TryGetValue(item.Icon, out CanvasBitmap? icon)
            && icon is not null)
        {
            using var iconClip = CanvasGeometry.CreateRoundedRectangle(
                device, rowLeft, rowTop, IconSize, IconSize, IconCornerRadius, IconCornerRadius);
            using (ds.CreateLayer(1f, iconClip))
            {
                DrawImageHighQuality(ds, icon, new Rect(rowLeft, rowTop, IconSize, IconSize));
            }
        }

        string name = item.Name ?? string.Empty;
        string pityText = item.Pity.ToString();
        // 叠在色条上，主字色比次要白更能压住实色绿/红
        DrawText(ds, name, nameX, rowTop + 4f, bodyFormat, PrimaryText, nameWidth);

        if (item.HasUpItem && item.IsUp)
        {
            const string upText = "up!";
            float upWidth = MeasureTextWidth(ds, upText, upFormat);
            DrawText(ds, upText, rightX - upWidth - 28f, rowTop + 6f, upFormat, Rarity5);
        }

        DrawText(ds, pityText, rightX - MeasureTextWidth(ds, pityText, bodyFormat), rowTop + 4f, bodyFormat, TertiaryText);
    }


    /// <summary>
    /// 按 <see cref="GachaPityProgressBackgroundBrushConverter"/> 规则绘制保底进度色条（圆角，与页面 CornerRadius=4 对齐）。
    /// </summary>
    private static void DrawPityBar(
        CanvasDrawingSession ds,
        CanvasDevice device,
        GachaLogItemEx item,
        float x,
        float y,
        float width,
        float height)
    {
        int pity = item.Pity;
        int point = 74;
        double guarantee = 90;
        if (item.GachaType is GenshinGachaType.WeaponEventWish or StarRailGachaType.LightConeEventWarp or StarRailGachaType.LightConeCollaborationWarp)
        {
            point = 63;
            guarantee = 80;
        }
        else if (item.GachaType is ZZZGachaType.WEngineChannel or ZZZGachaType.WEngineReverberation or ZZZGachaType.BangbooChannel)
        {
            point = 65;
            guarantee = 80;
        }

        Color baseColor = pity < point ? PityGreen : PityRed;
        Color fillColor = Color.FromArgb((byte)(PityBarFillOpacity * 255f), baseColor.R, baseColor.G, baseColor.B);
        float offset = Math.Clamp((float)(pity / guarantee), 0f, 1f);
        if (offset <= 0f)
        {
            return;
        }

        using var clip = CanvasGeometry.CreateRoundedRectangle(device, x, y, width, height, ItemPityBarCornerRadius, ItemPityBarCornerRadius);
        using (ds.CreateLayer(1f, clip))
        {
            Color tail = Color.FromArgb((byte)(fillColor.A * PityBarTailOpacityScale), fillColor.R, fillColor.G, fillColor.B);
            var stops = new[]
            {
                new CanvasGradientStop { Position = 0f, Color = fillColor },
                new CanvasGradientStop { Position = 1f, Color = tail },
            };
            using var brush = new CanvasLinearGradientBrush(ds, stops)
            {
                StartPoint = new Vector2(x, y),
                EndPoint = new Vector2(x + width * offset, y),
            };
            ds.FillRectangle(x, y, width * offset, height, brush);
        }
    }


    /// <summary>
    /// 绘制强调色圆角胶囊标签；返回胶囊左缘 X，供后续标签向右排列。
    /// </summary>
    private static float DrawCapsule(
        CanvasDrawingSession ds,
        CanvasDevice device,
        string text,
        float right,
        float top,
        CanvasTextFormat format,
        Color accentColor)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return right;
        }

        float textWidth = MeasureTextWidth(ds, text, format);
        float paddingH = 7f;
        float paddingV = 1f;
        float capsuleWidth = textWidth + paddingH * 2;
        float capsuleHeight = 18f;
        float left = right - capsuleWidth;
        float capsuleTop = top + 2f;

        using var geometry = CanvasGeometry.CreateRoundedRectangle(device, left, capsuleTop, capsuleWidth, capsuleHeight, 9f, 9f);
        ds.FillGeometry(geometry, accentColor);
        ds.DrawGeometry(geometry, Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 1f);
        DrawText(ds, text, left + paddingH, capsuleTop + paddingV, format, OnAccentText);
        return left;
    }


    /// <summary>
    /// 将快照绘制为原始背景内容层（raw，用于后续调色磨砂 + 卡片采样）。
    /// 由调用方保证若为视频则已提前快照为静态图片路径（官方视频已移除 overlay）。
    /// 图片使用 cover 填充；无背景时使用强调色对角渐变。
    /// </summary>
    private static async Task DrawRawBackgroundAsync(
        CanvasDrawingSession ds,
        CanvasDevice device,
        float width,
        float height,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken)
    {
        bool useImage = !string.IsNullOrWhiteSpace(backgroundFile)
                        && File.Exists(backgroundFile);

        if (useImage)
        {
            ImageInfo info = await ImageLoader.LoadImageAsync(backgroundFile!, cancellationToken);
            using CanvasBitmap source = info.CanvasBitmap;
            DrawCoverImage(ds, source, width, height);
            return;
        }

        Color deep = Color.FromArgb(0xFF, (byte)(accentColor.R * 0.14), (byte)(accentColor.G * 0.14), (byte)(accentColor.B * 0.16));
        Color lift = Color.FromArgb(0xFF, (byte)(accentColor.R * 0.28), (byte)(accentColor.G * 0.26), (byte)(accentColor.B * 0.32));
        var stops = new[]
        {
            new CanvasGradientStop { Position = 0f, Color = lift },
            new CanvasGradientStop { Position = 1f, Color = deep },
        };
        using var brush = new CanvasLinearGradientBrush(ds, stops)
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(width, height),
        };
        ds.FillRectangle(0, 0, width, height, brush);
    }


    /// <summary>
    /// 大背景调色：降饱和 → 略压曝光 → 高斯模糊 → 暗角 → 薄 tint → 主题色顶雾。
    /// 让壁纸仍可辨认，同时把对比度让给卡片。
    /// </summary>
    private static void DrawGradedBackground(
        CanvasDrawingSession ds,
        CanvasBitmap bgLayer,
        float width,
        float height,
        Color accentColor)
    {
        using var saturate = new SaturationEffect
        {
            Source = bgLayer,
            Saturation = BgSaturation,
        };
        using var exposure = new ExposureEffect
        {
            Source = saturate,
            Exposure = BgExposure,
        };
        using var blur = new GaussianBlurEffect
        {
            Source = exposure,
            BlurAmount = BgLightAcrylicBlurAmount,
            BorderMode = EffectBorderMode.Soft,
        };
        using var vignette = new VignetteEffect
        {
            Source = blur,
            Amount = BgVignetteAmount,
            Color = BgVignetteColor,
            Curve = 0.55f,
        };
        ds.DrawImage(vignette);
        ds.FillRectangle(0, 0, width, height, BgLightAcrylicTint);

        Color wash = Color.FromArgb(0x14, accentColor.R, accentColor.G, accentColor.B);
        var stops = new[]
        {
            new CanvasGradientStop { Position = 0f, Color = wash },
            new CanvasGradientStop { Position = 0.32f, Color = Colors.Transparent },
            new CanvasGradientStop { Position = 1f, Color = Colors.Transparent },
        };
        using var accentWash = new CanvasLinearGradientBrush(ds, stops)
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(0, height),
        };
        ds.FillRectangle(0, 0, width, height, accentWash);
    }


    /// <summary>
    /// 图脚：右下 UID，给分享图一个轻锚点，不抢卡片内容。
    /// </summary>
    private static void DrawFooter(CanvasDrawingSession ds, long uid, float y, float canvasWidth, CanvasTextFormat format)
    {
        string text = $"UID {uid}";
        float width = MeasureTextWidth(ds, text, format);
        DrawText(ds, text, canvasWidth - OuterMargin - width, y, format, TertiaryText);
    }


    /// <summary>
    /// 两端淡出的分割线，避免硬切一刀。
    /// </summary>
    private static void DrawFadeSeparator(CanvasDrawingSession ds, float x1, float x2, float y, Color color)
    {
        var stops = new[]
        {
            new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0, color.R, color.G, color.B) },
            new CanvasGradientStop { Position = 0.1f, Color = color },
            new CanvasGradientStop { Position = 0.9f, Color = color },
            new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0, color.R, color.G, color.B) },
        };
        using var brush = new CanvasLinearGradientBrush(ds, stops)
        {
            StartPoint = new Vector2(x1, y),
            EndPoint = new Vector2(x2, y),
        };
        ds.DrawLine(x1, y, x2, y, brush, 1f);
    }


    /// <summary>
    /// 按比例裁剪填充绘制背景图（aspect fill）。
    /// </summary>
    private static void DrawCoverImage(CanvasDrawingSession ds, CanvasBitmap bitmap, float targetWidth, float targetHeight)
    {
        float imgW = bitmap.SizeInPixels.Width;
        float imgH = bitmap.SizeInPixels.Height;
        if (imgW <= 0 || imgH <= 0)
        {
            return;
        }

        float scale = Math.Max(targetWidth / imgW, targetHeight / imgH);
        float drawW = imgW * scale;
        float drawH = imgH * scale;
        float drawX = (targetWidth - drawW) / 2f;
        float drawY = (targetHeight - drawH) / 2f;
        DrawImageHighQuality(ds, bitmap, new Rect(drawX, drawY, drawW, drawH));
    }


    /// <summary>
    /// 按目标矩形高质量缩放绘制位图（三次插值，避免默认线性缩放发糊）。
    /// </summary>
    private static void DrawImageHighQuality(CanvasDrawingSession ds, CanvasBitmap bitmap, Rect dest)
    {
        ds.DrawImage(bitmap, dest, bitmap.Bounds, 1f, CanvasImageInterpolation.HighQualityCubic);
    }


    /// <summary>
    /// 加载物品图标：支持 ms-appx、本地路径与远程 URL（走 FileCache）。
    /// </summary>
    private static async Task<CanvasBitmap?> LoadIconBitmapAsync(string icon, CancellationToken cancellationToken)
    {
        try
        {
            string? localPath = ResolveIconLocalPath(icon);
            if (localPath is not null && File.Exists(localPath))
            {
                ImageInfo info = await ImageLoader.LoadImageAsync(localPath, cancellationToken);
                return info.CanvasBitmap;
            }

            if (Uri.TryCreate(icon, UriKind.Absolute, out Uri? uri)
                && uri.Scheme is "http" or "https")
            {
                string? cached = await FileCache.GetFromCacheAsync(uri, false, cancellationToken);
                if (cached is not null && File.Exists(cached))
                {
                    ImageInfo info = await ImageLoader.LoadImageAsync(cached, cancellationToken);
                    return info.CanvasBitmap;
                }
            }
        }
        catch
        {
            // 单张图标失败不影响整图导出
        }

        return null;
    }


    private static CanvasTextFormat CreateTextFormat(
        float fontSize,
        FontWeight? weight = null,
        FontStyle style = FontStyle.Normal,
        bool trimming = false)
    {
        return new CanvasTextFormat
        {
            FontFamily = "Segoe UI",
            FontSize = fontSize,
            FontWeight = weight ?? NormalFontWeight,
            FontStyle = style,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = trimming ? CanvasTextTrimmingGranularity.Character : CanvasTextTrimmingGranularity.None,
            TrimmingSign = trimming ? CanvasTrimmingSign.Ellipsis : CanvasTrimmingSign.None,
        };
    }


    private static void DrawText(
        CanvasDrawingSession ds,
        string text,
        float x,
        float y,
        CanvasTextFormat format,
        Color color,
        float maxWidth = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float layoutWidth = maxWidth > 0 ? maxWidth : 4096f;
        using var layout = new CanvasTextLayout(ds, text, format, layoutWidth, 0);
        ds.DrawTextLayout(layout, x, y, color);
    }


    private static float MeasureTextWidth(CanvasDrawingSession ds, string text, CanvasTextFormat format)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        using var layout = new CanvasTextLayout(ds, text, format, 4096f, 0);
        return (float)layout.LayoutBounds.Width;
    }


    private static string FormatTimeRange(GachaTypeStats stats)
        => $"{stats.StartTime:yyyy/MM/dd HH:mm}  –  {stats.EndTime:yyyy/MM/dd HH:mm}";


    /// <summary>
    /// 将图标 URI 解析为本地文件路径（ms-appx / file），避免后台线程访问 WinRT Storage API。
    /// </summary>
    private static string? ResolveIconLocalPath(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        if (icon.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase))
        {
            string relative = new Uri(icon).AbsolutePath.TrimStart('/');
            return Path.Combine(AppContext.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        if (icon.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(icon).LocalPath;
        }

        if (File.Exists(icon))
        {
            return icon;
        }

        return null;
    }

}
