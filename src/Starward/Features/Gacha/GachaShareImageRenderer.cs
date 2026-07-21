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
/// 使用 Win2D 离屏绘制抽卡统计分享图（统计头 + 完整 5★/S 列表），保存为 PNG。
/// 快照提供“实际样子”（官方视频无 overlay）；整个大背景应用浅亚克力（轻模糊+浅tint），卡片使用较强亚克力；名称默认用未选中时的次要白色。
/// </summary>
internal static class GachaShareImageRenderer
{

    private const float Dpi = 192f;

    private const float CardWidth = 262f;
    private const float CardSpacing = 12f;
    private const float OuterMargin = 20f;
    private const float CardPaddingH = 12f;
    private const float CardPaddingV = 8f;
    private const float CardCornerRadius = 8f;
    private const float RowSpacing = 4f;
    private const float ListSectionSpacing = 8f;
    private const float ItemRowHeight = 28f;
    private const float IconSize = 28f;
    private const float IconColumnWidth = 40f;

    private static readonly Color CardBackground = Color.FromArgb(0xBF, 0x2C, 0x2C, 0x2C);
    private static readonly Color SecondaryText = Color.FromArgb(0xFF, 0xC5, 0xC5, 0xC5);
    private static readonly Color OnAccentText = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color Rarity5 = Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00);
    private static readonly Color RarityAverage = Color.FromArgb(0xFF, 0x3B, 0xA2, 0x72);
    private static readonly Color PityGreen = Color.FromArgb(0xFF, 0x00, 0xE0, 0x79);
    private static readonly Color PityRed = Color.FromArgb(0xFF, 0xC8, 0x3C, 0x23);
    private static readonly Color SeparatorColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);

    // 预暗化叠加层，统一原始背景亮度基线，避免浅色背景在亚克力仿真中泛白
    private static readonly Color BgPreDarkenOverlay = Color.FromArgb(0x40, 0x00, 0x00, 0x00);

    // 卡片亚克力效果参数（模拟 CustomOverlayAcrylicBrush 风格，较强）
    private const float CardAcrylicBlurAmount = 22f;
    private static readonly Color CardAcrylicTint = Color.FromArgb(0x99, 0x24, 0x24, 0x24);
    private static readonly Color CardBorderColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);

    // 大背景亚克力参数（较深的磨砂，区别于卡片；中等模糊 + 较高不透明度 tint）
    private const float BgLightAcrylicBlurAmount = 18f;
    private static readonly Color BgLightAcrylicTint = Color.FromArgb(0x80, 0x1A, 0x1A, 0x1A);

    private static readonly FontWeight NormalFontWeight = new() { Weight = 400 };
    private static readonly FontWeight SemiBoldFontWeight = new() { Weight = 600 };


    /// <summary>
    /// 离屏渲染当前所选卡池统计卡片并保存 PNG。
    /// 使用原始快照绘制整个大背景的浅亚克力效果，卡片叠加较强亚克力。
    /// </summary>
    /// <param name="stats">要绘制的卡池统计列表（横向排列，顺序由调用方决定）。</param>
    /// <param name="gameBiz">当前游戏区服，用于 5★/S 文案与 pity 规则分支。</param>
    /// <param name="backgroundFile">背景图/快照本地路径（由调用方确保视频已转为当前帧快照 PNG）；为 null 或不存在时使用纯色背景。</param>
    /// <param name="uid">玩家 UID，用于输出文件名。</param>
    /// <param name="accentColor">主题强调色（须在 UI 线程读取后传入，渲染在后台线程执行）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已保存 PNG 的完整路径。</returns>
    /// <exception cref="ArgumentException"><paramref name="stats"/> 为空时抛出。</exception>
    public static async Task<string> RenderAndSaveAsync(
        IReadOnlyList<GachaTypeStats> stats,
        GameBiz gameBiz,
        string? backgroundFile,
        long uid,
        Color accentColor,
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
            using var bodyFormat = CreateTextFormat(14f);
            using var smallFormat = CreateTextFormat(12f);
            using var capsuleFormat = CreateTextFormat(11f);
            using var upFormat = CreateTextFormat(12f, NormalFontWeight, FontStyle.Italic);

            var cardHeights = stats.Select(MeasureCardHeight).ToArray();
            var iconBitmaps = iconLoads.ToDictionary(x => x.Key, x => x.Value.Result, StringComparer.Ordinal);
            float contentHeight = cardHeights.Max();
            float canvasWidth = OuterMargin * 2 + stats.Count * CardWidth + Math.Max(0, stats.Count - 1) * CardSpacing;
            float canvasHeight = OuterMargin * 2 + contentHeight;

            using var renderTarget = new CanvasRenderTarget(device, canvasWidth, canvasHeight, Dpi);
            using (CanvasDrawingSession ds = renderTarget.CreateDrawingSession())
            {
                ds.Clear(Colors.Transparent);

                // 先生成原始背景层（raw snapshot 内容，用于采样和“实际样子”）
                using var bgLayer = new CanvasRenderTarget(device, canvasWidth, canvasHeight, Dpi);
                using (CanvasDrawingSession bgDs = bgLayer.CreateDrawingSession())
                {
                    await DrawRawBackgroundAsync(bgDs, device, canvasWidth, canvasHeight, backgroundFile, accentColor, cancellationToken);
                    // 预暗化原始背景，统一亮度基线，避免浅色背景导致亚克力仿真泛白
                    bgDs.FillRectangle(0, 0, canvasWidth, canvasHeight, BgPreDarkenOverlay);
                }

                // 绘制整个大背景的浅亚克力效果（轻模糊 + 浅 tint），卡片会覆盖其区域
                using var bgLightBlur = new GaussianBlurEffect
                {
                    Source = bgLayer,
                    BlurAmount = BgLightAcrylicBlurAmount,
                    BorderMode = EffectBorderMode.Soft,
                };
                ds.DrawImage(bgLightBlur);
                ds.FillRectangle(0, 0, canvasWidth, canvasHeight, BgLightAcrylicTint);

                float cardTop = OuterMargin;
                float cardLeft = OuterMargin;
                for (int i = 0; i < stats.Count; i++)
                {
                    GachaTypeStats stat = stats[i];
                    DrawCard(ds, device, stat, cardLeft, cardTop, cardHeights[i], rarityLabel, accentColor,
                             titleFormat, bodyFormat, smallFormat, capsuleFormat, upFormat, iconBitmaps, bgLayer);
                    cardLeft += CardWidth + CardSpacing;
                }
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
    /// 按单张卡的内容测量高度（顶对齐，列表区随 5★ 条数伸缩）。
    /// </summary>
    private static float MeasureCardHeight(GachaTypeStats stats)
    {
        float height = CardPaddingV * 2;
        height += 22f;
        height += RowSpacing + 16f;
        height += RowSpacing + 1 + RowSpacing;
        height += 20f + RowSpacing;
        height += 20f + RowSpacing;
        height += ListSectionSpacing;
        height += (stats.List_5?.Count ?? 0) * ItemRowHeight;
        return height;
    }


    /// <summary>
    /// 绘制单张统计卡片（先绘制亚克力卡片背景，再绘制头部 + 5★ 列表文字）。
    /// 背景层 bgLayer 用于对卡片区域做局部高斯模糊 + 磨砂着色，实现“对卡片本身做亚克力”。
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
        CanvasTextFormat titleFormat,
        CanvasTextFormat bodyFormat,
        CanvasTextFormat smallFormat,
        CanvasTextFormat capsuleFormat,
        CanvasTextFormat upFormat,
        IReadOnlyDictionary<string, CanvasBitmap?> iconBitmaps,
        CanvasBitmap bgLayer)
    {
        // 先绘制本卡片的亚克力磨砂背景（基于原始 bgLayer 的局部模糊 + 着色）
        DrawAcrylicCardBackground(ds, device, bgLayer, left, top, CardWidth, height, CardCornerRadius);

        float x = left + CardPaddingH;
        float y = top + CardPaddingV;
        float innerWidth = CardWidth - CardPaddingH * 2;
        float rightX = x + innerWidth;

        // 标题行：卡池名 + 可选胶囊 + 总抽数
        float titleBlockRight = rightX;
        if (!string.IsNullOrEmpty(stats.Count.ToString()))
        {
            string countText = stats.Count.ToString();
            DrawText(ds, countText, rightX - MeasureTextWidth(ds, countText, titleFormat), y, titleFormat, SecondaryText);
            titleBlockRight -= MeasureTextWidth(ds, countText, titleFormat) + 8f;
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

        DrawText(ds, stats.GachaTypeText, x, y, titleFormat, SecondaryText, titleBlockRight - x);
        y += 22f;

        // 时间范围
        DrawText(ds, FormatTimeRange(stats), x, y, smallFormat, SecondaryText);
        y += 16f + RowSpacing;

        // 分割线
        ds.DrawLine(x, y, rightX, y, SeparatorColor, 1f);
        y += 1f + RowSpacing;

        // 5★/S 平均
        string averageLeft = $"{rarityLabel}{Lang.GachaStatsCard_Average}{stats.Avarage_5_Desc_Text}";
        string averageRight = $"{stats.Average_5_Text}{stats.Avarage_5_Up_Text}";
        DrawText(ds, averageLeft, x, y, bodyFormat, RarityAverage);
        DrawText(ds, averageRight, rightX - MeasureTextWidth(ds, averageRight, bodyFormat), y, bodyFormat, RarityAverage);
        y += 20f + RowSpacing;

        // 5★/S 统计 或 不歪概率
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
        y += 20f + RowSpacing + ListSectionSpacing;

        // 5★/S 列表（不含 UI 中的 Segmented 选项卡标签）
        if (stats.List_5 is { Count: > 0 })
        {
            float nameX = x + IconColumnWidth;
            float nameWidth = innerWidth - IconColumnWidth - 48f;
            foreach (GachaLogItemEx item in stats.List_5)
            {
                DrawListItem(ds, item, x, y, nameX, nameWidth, rightX, bodyFormat, upFormat, iconBitmaps);
                y += ItemRowHeight;
            }
        }
    }

    /// <summary>
    /// 绘制卡片的亚克力背景：从 raw bgLayer 局部采样 → 高斯模糊（CreateLayer 限制到圆角）→ 着色 tint → 轻边框。
    /// 实现“对卡片本身做亚克力”（较强），大背景已单独应用浅亚克力。
    /// </summary>
    private static void DrawAcrylicCardBackground(
        CanvasDrawingSession ds,
        CanvasDevice device,
        CanvasBitmap bgLayer,
        float left,
        float top,
        float width,
        float height,
        float cornerRadius)
    {
        using var cardGeometry = CanvasGeometry.CreateRoundedRectangle(device, left, top, width, height, cornerRadius, cornerRadius);

        // 局部模糊背景内容（只影响卡片区域）
        using var blur = new GaussianBlurEffect
        {
            Source = bgLayer,
            BlurAmount = CardAcrylicBlurAmount,
            BorderMode = EffectBorderMode.Soft,
        };

        using (var layer = ds.CreateLayer(1f, cardGeometry))
        {
            // 仅绘制卡片矩形对应的模糊区域
            ds.DrawImage(blur, left, top, new Rect(left, top, width, height));
        }

        // 亚克力着色层（半透明深色磨砂）
        ds.FillGeometry(cardGeometry, CardAcrylicTint);

        // 轻微边框增强玻璃质感（可选，极低不透明度）
        ds.DrawGeometry(cardGeometry, CardBorderColor, 1f);
    }


    /// <summary>
    /// 绘制单行 5★ 记录：保底色条、图标、名称、pity、up! 标记。
    /// </summary>
    private static void DrawListItem(
        CanvasDrawingSession ds,
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
        DrawPityBar(ds, item, nameX - 4f, barTop, nameWidth + 8f, 24f);

        if (!string.IsNullOrWhiteSpace(item.Icon)
            && iconBitmaps.TryGetValue(item.Icon, out CanvasBitmap? icon)
            && icon is not null)
        {
            ds.DrawImage(icon, new Rect(rowLeft, rowTop, IconSize, IconSize));
        }

        string name = item.Name ?? string.Empty;
        string pityText = item.Pity.ToString();
        // 名称默认使用未选中时的白色（SecondaryText），与 UI 中非 hover 状态一致。
        // pity 数字也使用次要色；up! 保留金色以突出。
        DrawText(ds, name, nameX, rowTop + 4f, bodyFormat, SecondaryText, nameWidth);

        if (item.HasUpItem && item.IsUp)
        {
            const string upText = "up!";
            float upWidth = MeasureTextWidth(ds, upText, upFormat);
            DrawText(ds, upText, rightX - upWidth - 28f, rowTop + 6f, upFormat, Rarity5);
        }

        DrawText(ds, pityText, rightX - MeasureTextWidth(ds, pityText, bodyFormat), rowTop + 4f, bodyFormat, SecondaryText);
    }


    /// <summary>
    /// 按 <see cref="GachaPityProgressBackgroundBrushConverter"/> 规则绘制保底进度色条。
    /// </summary>
    private static void DrawPityBar(CanvasDrawingSession ds, GachaLogItemEx item, float x, float y, float width, float height)
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
        byte alpha = (byte)(0.4 * 255);
        Color fillColor = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        float offset = Math.Clamp((float)(pity / guarantee), 0f, 1f);
        if (offset <= 0f)
        {
            return;
        }

        var stops = new[]
        {
            new CanvasGradientStop { Position = 0, Color = fillColor },
            new CanvasGradientStop { Position = offset, Color = fillColor },
            new CanvasGradientStop { Position = offset, Color = Colors.Transparent },
            new CanvasGradientStop { Position = 1, Color = Colors.Transparent },
        };
        using var brush = new CanvasLinearGradientBrush(ds.Device, stops)
        {
            StartPoint = new Vector2(x, y),
            EndPoint = new Vector2(x + width, y),
        };
        ds.FillRectangle(x, y, width * offset, height, brush);
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
        float paddingH = 6f;
        float paddingV = 1f;
        float capsuleWidth = textWidth + paddingH * 2;
        float capsuleHeight = 18f;
        float left = right - capsuleWidth;
        float capsuleTop = top + 2f;

        using var geometry = CanvasGeometry.CreateRoundedRectangle(device, left, capsuleTop, capsuleWidth, capsuleHeight, 9f, 9f);
        ds.FillGeometry(geometry, accentColor);
        DrawText(ds, text, left + paddingH, capsuleTop + paddingV, format, OnAccentText);
        return left;
    }


    /// <summary>
    /// 将快照绘制为原始背景内容层（raw，用于后续浅亚克力可见背景 + 卡片采样）。
    /// 由调用方保证若为视频则已提前快照为静态图片路径（官方视频已移除 overlay）。
    /// 图片使用 cover 填充；无背景时使用强调色深色变体。
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
            // 按需可在此加极轻微 overlay 提升文字可读性，但保持“实际样子”优先，不做明显压暗。
            // ds.FillRectangle(0, 0, width, height, Color.FromArgb(0x10, 0, 0, 0));
            return;
        }

        ds.FillRectangle(0, 0, width, height, GetSolidBackgroundColor(accentColor));
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
        ds.DrawImage(bitmap, new Rect(drawX, drawY, drawW, drawH));
    }


    /// <summary>
    /// 加载物品图标：支持 ms-appx、本地路径与远程 URL（走 FileCache）。
    /// </summary>
    private static async Task<CanvasBitmap?> LoadIconBitmapAsync(string icon, CancellationToken cancellationToken)
    {
        try
        {
            var device = CanvasDevice.GetSharedDevice();
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
        FontStyle style = FontStyle.Normal)
    {
        return new CanvasTextFormat
        {
            FontFamily = "Segoe UI",
            FontSize = fontSize,
            FontWeight = weight ?? NormalFontWeight,
            FontStyle = style,
            WordWrapping = CanvasWordWrapping.NoWrap,
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
        => $"{stats.StartTime:yyyy/MM/dd HH:mm:ss} - {stats.EndTime:yyyy/MM/dd HH:mm:ss}";


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


    /// <summary>无图片背景时使用强调色的深色变体。</summary>
    private static Color GetSolidBackgroundColor(Color accent)
        => Color.FromArgb(0xFF, (byte)(accent.R * 0.22), (byte)(accent.G * 0.22), (byte)(accent.B * 0.22));

}