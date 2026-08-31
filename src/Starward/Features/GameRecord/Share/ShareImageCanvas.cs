using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Starward.Codec.ICC;
using Starward.Features.Codec;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 战绩分享图的 Win2D 公共绘制（壁纸磨砂、亚克力卡片、角色卡、文字）。
/// 视觉参数对齐抽卡分享图，卡片圆角对齐战绩页 CornerRadius=8。
/// </summary>
internal static class ShareImageCanvas
{

    public const float Dpi = 288f;
    public const float OuterMargin = 28f;
    public const float FooterHeight = 16f;
    public const float FooterGap = 4f;
    public const float FooterBottom = 10f;
    public const float CardCornerRadius = 8f;
    public const float CardSpacing = 12f;

    public static readonly Color PrimaryText = Color.FromArgb(0xFF, 0xF7, 0xF7, 0xF7);
    public static readonly Color SecondaryText = Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8);
    public static readonly Color TertiaryText = Color.FromArgb(0xFF, 0xA3, 0xA3, 0xA3);
    public static readonly Color GenshinLevelText = Color.FromArgb(0xFF, 0x84, 0x60, 0x3D);
    public static readonly Color RankBadgeFill = Color.FromArgb(0xA0, 0x00, 0x00, 0x00);
    public static readonly Color ZzzCardFill = Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0A);
    public static readonly Color SeparatorColor = Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF);

    public const string ZzzAvatarCardBackground = "ms-appx:///Assets/Image/ZZZ_AvatarCard_Background.png";
    public const string StarIconGenshinAbyss = "ms-appx:///Assets/Image/UI_Icon_Tower_Star.png";
    public const string StarIconGenshinTheaterOn = "ms-appx:///Assets/Image/UI_Icon_Tower_Star1.png";
    public const string StarIconGenshinTheaterOff = "ms-appx:///Assets/Image/UI_Icon_Tower_Star2.png";
    public const string StarIconStarRail = "ms-appx:///Assets/Image/IconChallengeStarYellow.png";
    public const string StarIconStarRailExtra = "ms-appx:///Assets/Image/IconChallengeStarStarward.png";
    public const string StarIconZzz = "ms-appx:///Assets/Image/star-icon-light.5a286c6d.png";
    public const string StarIconChallengePeakBoss = "ms-appx:///Assets/Image/boss_star.ae0258cf.png";
    public const string StarIconChallengePeakMob = "ms-appx:///Assets/Image/normal_star.751a6e2d.png";
    public const string FlowerIconTheater = "ms-appx:///Assets/Image/UI_DisplayItemIcon_410016.png";

    private static readonly Color BgPreDarkenOverlay = Color.FromArgb(0x28, 0x00, 0x00, 0x00);
    private const float CardAcrylicBlurAmount = 40f;
    private static readonly Color CardAcrylicTint = Color.FromArgb(0x7A, 0x00, 0x00, 0x00);
    private static readonly Color CardBorderColor = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
    private static readonly Color CardHairlineColor = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
    private const float CardShadowBlur = 16f;
    private const float CardShadowOffsetY = 6f;
    private static readonly Color CardShadowColor = Color.FromArgb(0x66, 0x00, 0x00, 0x00);
    private const float BgLightAcrylicBlurAmount = 32f;
    private const float BgSaturation = 0.88f;
    private const float BgExposure = -0.04f;
    private const float BgVignetteAmount = 0.30f;
    private static readonly Color BgLightAcrylicTint = Color.FromArgb(0x33, 0x00, 0x00, 0x00);
    private static readonly Color BgVignetteColor = Color.FromArgb(0xFF, 0x08, 0x08, 0x0C);

    private static readonly FontWeight NormalFontWeight = new() { Weight = 400 };
    private static readonly FontWeight SemiBoldFontWeight = new() { Weight = 600 };
    private static readonly FontWeight BoldFontWeight = new() { Weight = 700 };

    private static readonly Regex MarkupTagRegex = new("<[^>]+>", RegexOptions.Compiled);


    /// <summary>
    /// 创建一次分享图绘制上下文（字体、图标缓存）。调用方负责 Dispose。
    /// </summary>
    public static ShareImageContext CreateContext(Color accent)
    {
        return new ShareImageContext(CanvasDevice.GetSharedDevice(), accent);
    }


    /// <summary>
    /// 离屏合成背景 + 内容 + 脚标并保存 PNG。
    /// </summary>
    /// <param name="contentHeight">页边距以内、脚标以上的内容高度。</param>
    /// <param name="footerText">右下角脚标文本；为空时按战绩页惯例显示 <c>UID {uid}</c>。</param>
    public static async Task<string> ComposeAndSaveAsync(
        float canvasWidth,
        float contentHeight,
        string fileStem,
        long uid,
        string? backgroundFile,
        ShareImageContext ctx,
        Action<CanvasDrawingSession, CanvasBitmap> drawContent,
        CancellationToken cancellationToken = default,
        string? footerText = null)
    {
        float canvasHeight = OuterMargin + Math.Max(contentHeight, 1f) + FooterGap + FooterHeight + FooterBottom;
        float dpi = FitRenderDpi(ctx.Device, canvasWidth, canvasHeight);
        using var renderTarget = new CanvasRenderTarget(ctx.Device, canvasWidth, canvasHeight, dpi);
        using (CanvasDrawingSession ds = renderTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.Antialiasing = CanvasAntialiasing.Antialiased;
            ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;

            using var bgLayer = new CanvasRenderTarget(ctx.Device, canvasWidth, canvasHeight, dpi);
            using (CanvasDrawingSession bgDs = bgLayer.CreateDrawingSession())
            {
                await DrawRawBackgroundAsync(bgDs, ctx.Device, canvasWidth, canvasHeight, backgroundFile, ctx.Accent, cancellationToken);
                bgDs.FillRectangle(0, 0, canvasWidth, canvasHeight, BgPreDarkenOverlay);
            }

            DrawGradedBackground(ds, bgLayer, canvasWidth, canvasHeight, ctx.Accent);
            drawContent(ds, bgLayer);
            DrawFooter(ds, footerText ?? $"UID {uid}", OuterMargin + contentHeight + FooterGap, canvasWidth, ctx.Small);
        }

        string folder = Path.Combine(AppConfig.CacheFolder, "cache", "share");
        Directory.CreateDirectory(folder);
        // 没有 UID 的分享图（如游戏时长统计）不在文件名里塞 0
        string uidPart = uid > 0 ? $"_{uid}" : "";
        string filePath = Path.Combine(folder, $"{fileStem}{uidPart}_{DateTime.Now:yyyyMMddHHmmss}.png");
        await using var fs = File.Create(filePath);
        await ImageSaver.SaveAsPngAsync(renderTarget, fs, ColorPrimaries.BT709);
        return filePath;
    }


    /// <summary>统计周期文案，对齐战绩页 Period 行。</summary>
    public static string FormatPeriod(DateTime start, DateTime end)
        => $"{Lang.ForgottenHallPage_Period}  {start:yyyy/MM/dd}  -  {end:yyyy/MM/dd}";


    /// <summary>统计周期文案（DateTimeOffset）。</summary>
    public static string FormatPeriod(DateTimeOffset start, DateTimeOffset end)
        => FormatPeriod(start.LocalDateTime, end.LocalDateTime);


    public static string GenshinRarityBg(int rarity) => rarity switch
    {
        1 => "ms-appx:///Assets/Image/Rarity_1_Background.png",
        2 => "ms-appx:///Assets/Image/Rarity_2_Background.png",
        3 => "ms-appx:///Assets/Image/Rarity_3_Background.png",
        4 => "ms-appx:///Assets/Image/Rarity_4_Background.png",
        5 => "ms-appx:///Assets/Image/Rarity_5_Background.png",
        _ => "ms-appx:///Assets/Image/Transparent.png",
    };


    public static string StarRailRarityBg(int rarity) => rarity switch
    {
        1 => "ms-appx:///Assets/Image/FrameIconRarity01.png",
        2 => "ms-appx:///Assets/Image/FrameIconRarity02.png",
        3 => "ms-appx:///Assets/Image/FrameIconRarity03.png",
        4 => "ms-appx:///Assets/Image/FrameIconRarity04.png",
        5 => "ms-appx:///Assets/Image/FrameIconRarity05.png",
        _ => "ms-appx:///Assets/Image/Transparent.png",
    };


    /// <summary>代理人/邦布稀有度小图标（S/A）。</summary>
    public static string ZzzRarityIcon(string? rarity) => rarity switch
    {
        "S+" => "ms-appx:///Assets/Image/rating-s_plus.c6cbdc49.png",
        "S" => "ms-appx:///Assets/Image/S_Level_S.png",
        "A" => "ms-appx:///Assets/Image/A_Level_S.png",
        "B" => "ms-appx:///Assets/Image/B_Level_S.png",
        _ => "ms-appx:///Assets/Image/Transparent.png",
    };


    /// <summary>式舆防卫战统计栏大号评级图。</summary>
    public static string ZzzRatingLargeIcon(string? rating) => rating switch
    {
        "S" => "ms-appx:///Assets/Image/S_Level.png",
        "A" => "ms-appx:///Assets/Image/A_Level.png",
        "B" => "ms-appx:///Assets/Image/B_Level.png",
        _ => ZzzRarityIcon(rating),
    };


    public static string TheaterHeraldry(int heraldry)
        => $"ms-appx:///Assets/Image/UI_RoleCombat_Medal_{Math.Clamp(heraldry, 0, 5)}.png";


    public static string StygianDifficultyMedal(int difficulty)
        => difficulty is >= 1 and <= 7
            ? $"ms-appx:///Assets/Image/UI_LeyLineChallenge_Medal_{difficulty}.png"
            : "ms-appx:///Assets/Image/UI_LeyLineChallenge_Medal_0.png";


    /// <summary>去掉米游社富文本色标，供分享图纯色绘制。</summary>
    public static string StripMarkup(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Replace("\\n", "\n", StringComparison.Ordinal);
        return MarkupTagRegex.Replace(normalized, string.Empty).Trim();
    }


    public static void DrawAcrylicCard(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        CanvasBitmap bgLayer,
        float left,
        float top,
        float width,
        float height,
        float cornerRadius = CardCornerRadius)
    {
        DrawCardShadow(ds, ctx.Device, left, top, width, height, cornerRadius);

        using var cardGeometry = CanvasGeometry.CreateRoundedRectangle(ctx.Device, left, top, width, height, cornerRadius, cornerRadius);
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
            Color washTop = Color.FromArgb(0x14, ctx.Accent.R, ctx.Accent.G, ctx.Accent.B);
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
            ds.DrawLine(left + cornerRadius, top + 0.6f, left + width - cornerRadius, top + 0.6f, CardHairlineColor, 1f);
        }

        ds.DrawGeometry(cardGeometry, CardBorderColor, 1f);
    }


    public static void DrawText(
        CanvasDrawingSession ds,
        string? text,
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


    public static void DrawTextRight(
        CanvasDrawingSession ds,
        string? text,
        float right,
        float y,
        CanvasTextFormat format,
        Color color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float width = MeasureTextWidth(ds, text, format);
        DrawText(ds, text, right - width, y, format, color);
    }


    public static float MeasureTextWidth(ICanvasResourceCreator resourceCreator, string? text, CanvasTextFormat format)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        using var layout = new CanvasTextLayout(resourceCreator, text, format, 4096f, 0);
        return (float)layout.LayoutBounds.Width;
    }


    public static float MeasureTextHeight(ICanvasResourceCreator resourceCreator, string? text, CanvasTextFormat format, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        using var layout = new CanvasTextLayout(resourceCreator, text, format, maxWidth, 0);
        return (float)Math.Ceiling(layout.LayoutBounds.Height);
    }


    public static void DrawImage(CanvasDrawingSession ds, CanvasBitmap? bitmap, Rect dest)
    {
        if (bitmap is null)
        {
            return;
        }

        ds.DrawImage(bitmap, dest, bitmap.Bounds, 1f, CanvasImageInterpolation.HighQualityCubic);
    }


    public static void DrawRoundedImage(
        CanvasDrawingSession ds,
        CanvasDevice device,
        CanvasBitmap? bitmap,
        Rect dest,
        float radius)
    {
        if (bitmap is null)
        {
            return;
        }

        using var clip = CanvasGeometry.CreateRoundedRectangle(device, (float)dest.X, (float)dest.Y, (float)dest.Width, (float)dest.Height, radius, radius);
        using (ds.CreateLayer(1f, clip))
        {
            DrawImage(ds, bitmap, dest);
        }
    }


    public static void DrawCoverImage(CanvasDrawingSession ds, CanvasBitmap bitmap, float targetWidth, float targetHeight, float destX = 0, float destY = 0)
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
        float drawX = destX + (targetWidth - drawW) / 2f;
        float drawY = destY + (targetHeight - drawH) / 2f;
        DrawImage(ds, bitmap, new Rect(drawX, drawY, drawW, drawH));
    }


    public static void DrawUniformImage(CanvasDrawingSession ds, CanvasBitmap? bitmap, Rect dest)
    {
        if (bitmap is null)
        {
            return;
        }

        float imgW = bitmap.SizeInPixels.Width;
        float imgH = bitmap.SizeInPixels.Height;
        if (imgW <= 0 || imgH <= 0)
        {
            return;
        }

        float scale = Math.Min((float)dest.Width / imgW, (float)dest.Height / imgH);
        float drawW = imgW * scale;
        float drawH = imgH * scale;
        float drawX = (float)dest.X + ((float)dest.Width - drawW) / 2f;
        float drawY = (float)dest.Y + ((float)dest.Height - drawH) / 2f;
        DrawImage(ds, bitmap, new Rect(drawX, drawY, drawW, drawH));
    }


    public static float DrawStarRow(
        CanvasDrawingSession ds,
        CanvasBitmap? star,
        float x,
        float y,
        int count,
        float size = 20f,
        float spacing = 2f)
    {
        if (star is null || count <= 0)
        {
            return 0;
        }

        for (int i = 0; i < count; i++)
        {
            DrawImage(ds, star, new Rect(x + i * (size + spacing), y, size, size));
        }

        return count * size + Math.Max(0, count - 1) * spacing;
    }


    /// <summary>原神角色卡：稀有度底 + 立绘 + 命座 + Lv。</summary>
    /// <param name="footer">卡底文案；为 null 时绘制 Lv.{level}。</param>
    public static void DrawGenshinAvatar(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float x,
        float y,
        float width,
        float height,
        int rarity,
        string? icon,
        int level,
        int rank = 0,
        string? footer = null)
    {
        DrawImage(ds, ctx.Icons.Get(GenshinRarityBg(rarity)), new Rect(x, y, width, height));
        float iconHeight = height - 18f;
        using var clip = CanvasGeometry.CreateRectangle(ctx.Device, x, y, width, iconHeight);
        using (ds.CreateLayer(1f, clip))
        {
            if (ctx.Icons.Get(icon) is CanvasBitmap bitmap)
            {
                DrawCoverImage(ds, bitmap, width, iconHeight, x, y);
            }
        }

        if (rank > 0)
        {
            float badgeW = MeasureTextWidth(ds, rank.ToString(), ctx.Small) + 8f;
            float badgeH = 16f;
            float badgeX = x + width - badgeW;
            using var badge = CanvasGeometry.CreateRoundedRectangle(ctx.Device, badgeX, y, badgeW, badgeH, 0, 4);
            ds.FillGeometry(badge, RankBadgeFill);
            DrawText(ds, rank.ToString(), badgeX + 4f, y, ctx.Small, SecondaryText);
        }

        string lv = footer ?? $"Lv.{level}";
        float lvWidth = MeasureTextWidth(ds, lv, ctx.Small);
        DrawText(ds, lv, x + (width - lvWidth) / 2f, y + height - 18f, ctx.Small, GenshinLevelText);
    }


    /// <summary>星铁角色卡：稀有度底 + 头像 + 星魂 + Lv 底栏。</summary>
    public static void DrawStarRailAvatar(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float x,
        float y,
        int rarity,
        string? icon,
        int level,
        int rank)
    {
        const float width = 72f;
        const float height = 84f;
        DrawImage(ds, ctx.Icons.Get(StarRailRarityBg(rarity)), new Rect(x, y, width, height));

        using var clip = CanvasGeometry.CreateRoundedRectangle(ctx.Device, x, y, width, height - 16f, 0, 12);
        using (ds.CreateLayer(1f, clip))
        {
            if (ctx.Icons.Get(icon) is CanvasBitmap bitmap)
            {
                DrawCoverImage(ds, bitmap, width, height - 16f, x, y);
            }
        }

        if (rank > 0)
        {
            float badgeW = MeasureTextWidth(ds, rank.ToString(), ctx.Small) + 8f;
            float badgeH = 18f;
            float badgeX = x + width - badgeW;
            using var badge = CanvasGeometry.CreateRoundedRectangle(ctx.Device, badgeX, y, badgeW, badgeH, 0, 8);
            ds.FillGeometry(badge, RankBadgeFill);
            DrawText(ds, rank.ToString(), badgeX + 4f, y + 1f, ctx.Small, SecondaryText);
        }

        ds.FillRectangle(x, y + height - 16f, width, 16f, RankBadgeFill);
        string lv = $"Lv.{level}";
        float lvWidth = MeasureTextWidth(ds, lv, ctx.Small);
        DrawText(ds, lv, x + (width - lvWidth) / 2f, y + height - 16f, ctx.Small, PrimaryText);
    }


    /// <summary>绝区零代理人卡，尺寸对齐页面 76×94。</summary>
    public static void DrawZzzAvatar(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float x,
        float y,
        string? rarity,
        string? icon,
        int level,
        int rank)
    {
        const float width = 76f;
        const float height = 94f;
        using var card = CanvasGeometry.CreateRoundedRectangle(ctx.Device, x, y, width, height, 8f, 8f);
        ds.FillGeometry(card, ZzzCardFill);

        using var inner = CanvasGeometry.CreateRoundedRectangle(ctx.Device, x + 2f, y + 2f, 72f, 72f, 8f, 8f);
        using (ds.CreateLayer(1f, inner))
        {
            DrawImage(ds, ctx.Icons.Get(ZzzAvatarCardBackground), new Rect(x + 2f, y + 2f, 72f, 72f));
            if (ctx.Icons.Get(icon) is CanvasBitmap bitmap)
            {
                DrawCoverImage(ds, bitmap, 72f, 72f, x + 2f, y + 2f);
            }
        }

        using var rarityBg = CanvasGeometry.CreateRoundedRectangle(ctx.Device, x, y, 22f, 22f, 8f, 0);
        ds.FillGeometry(rarityBg, Colors.Black);
        DrawImage(ds, ctx.Icons.Get(ZzzRarityIcon(rarity)), new Rect(x + 3f, y + 3f, 16f, 16f));

        if (rank > 0)
        {
            float badgeW = MeasureTextWidth(ds, rank.ToString(), ctx.Small) + 8f;
            float badgeX = x + width - badgeW;
            using var badge = CanvasGeometry.CreateRoundedRectangle(ctx.Device, badgeX, y, badgeW, 18f, 0, 4);
            ds.FillGeometry(badge, RankBadgeFill);
            DrawText(ds, rank.ToString(), badgeX + 4f, y + 1f, ctx.Small, SecondaryText);
        }

        string lv = $"Lv.{level}";
        float lvWidth = MeasureTextWidth(ds, lv, ctx.Small);
        DrawText(ds, lv, x + (width - lvWidth) / 2f, y + height - 16f, ctx.Small, SecondaryText);
    }


    /// <summary>绝区零邦布卡，尺寸对齐页面 58×78。</summary>
    public static void DrawZzzBuddy(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float x,
        float y,
        string? rarity,
        string? icon,
        int level)
    {
        const float width = 58f;
        const float height = 78f;
        using var card = CanvasGeometry.CreateRoundedRectangle(ctx.Device, x, y, width, height, 8f, 8f);
        ds.FillGeometry(card, ZzzCardFill);

        using var inner = CanvasGeometry.CreateRoundedRectangle(ctx.Device, x + 2f, y + 2f, 54f, 56f, 8f, 8f);
        using (ds.CreateLayer(1f, inner))
        {
            DrawImage(ds, ctx.Icons.Get(ZzzAvatarCardBackground), new Rect(x + 2f, y + 2f, 54f, 56f));
            if (ctx.Icons.Get(icon) is CanvasBitmap bitmap)
            {
                DrawCoverImage(ds, bitmap, 70f, 84f, x - 6f, y - 6f);
            }
        }

        using var rarityBg = CanvasGeometry.CreateRoundedRectangle(ctx.Device, x, y, 20f, 20f, 8f, 0);
        ds.FillGeometry(rarityBg, Colors.Black);
        DrawImage(ds, ctx.Icons.Get(ZzzRarityIcon(rarity)), new Rect(x + 2f, y + 2f, 16f, 16f));

        string lv = $"Lv.{level}";
        float lvWidth = MeasureTextWidth(ds, lv, ctx.Small);
        DrawText(ds, lv, x + (width - lvWidth) / 2f, y + height - 16f, ctx.Small, SecondaryText);
    }


    public static void DrawFadeSeparator(CanvasDrawingSession ds, float x1, float x2, float y)
    {
        var stops = new[]
        {
            new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0, 0xFF, 0xFF, 0xFF) },
            new CanvasGradientStop { Position = 0.1f, Color = SeparatorColor },
            new CanvasGradientStop { Position = 0.9f, Color = SeparatorColor },
            new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0, 0xFF, 0xFF, 0xFF) },
        };
        using var brush = new CanvasLinearGradientBrush(ds, stops)
        {
            StartPoint = new Vector2(x1, y),
            EndPoint = new Vector2(x2, y),
        };
        ds.DrawLine(x1, y, x2, y, brush, 1f);
    }


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
        using var mask = new CanvasRenderTarget(device, width + pad * 2, height + pad * 2, ds.Dpi);
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


    private static async Task DrawRawBackgroundAsync(
        CanvasDrawingSession ds,
        CanvasDevice device,
        float width,
        float height,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken)
    {
        bool useImage = !string.IsNullOrWhiteSpace(backgroundFile) && File.Exists(backgroundFile);
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


    private static void DrawGradedBackground(
        CanvasDrawingSession ds,
        CanvasBitmap bgLayer,
        float width,
        float height,
        Color accentColor)
    {
        using var saturate = new SaturationEffect { Source = bgLayer, Saturation = BgSaturation };
        using var exposure = new ExposureEffect { Source = saturate, Exposure = BgExposure };
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
    /// 超长分享图在 288 DPI 下可能超过 GPU 最大纹理，等比降低 DPI 以免创建失败。
    /// </summary>
    private static float FitRenderDpi(CanvasDevice device, float width, float height)
    {
        int maxPixels = device.MaximumBitmapSizeInPixels;
        if (maxPixels <= 0)
        {
            return Dpi;
        }

        float limit = Math.Max(256f, maxPixels - 16);
        float pixelW = width * Dpi / 96f;
        float pixelH = height * Dpi / 96f;
        float scale = Math.Min(1f, Math.Min(limit / Math.Max(pixelW, 1f), limit / Math.Max(pixelH, 1f)));
        return scale < 1f ? Math.Clamp(Dpi * scale, 48f, Dpi) : Dpi;
    }


    private static void DrawFooter(CanvasDrawingSession ds, string text, float y, float canvasWidth, CanvasTextFormat format)
    {
        DrawTextRight(ds, text, canvasWidth - OuterMargin, y, format, TertiaryText);
    }


    internal static CanvasTextFormat CreateTextFormat(
        float fontSize,
        FontWeight? weight = null,
        bool wrap = false,
        bool trimming = false)
    {
        return new CanvasTextFormat
        {
            FontFamily = "Segoe UI",
            FontSize = fontSize,
            FontWeight = weight ?? NormalFontWeight,
            WordWrapping = wrap ? CanvasWordWrapping.Wrap : CanvasWordWrapping.NoWrap,
            TrimmingGranularity = trimming ? CanvasTextTrimmingGranularity.Character : CanvasTextTrimmingGranularity.None,
            TrimmingSign = trimming ? CanvasTrimmingSign.Ellipsis : CanvasTrimmingSign.None,
        };
    }


    internal static FontWeight SemiBold => SemiBoldFontWeight;

    internal static FontWeight Bold => BoldFontWeight;

}


/// <summary>一次分享图绘制的设备、字体与图标缓存。</summary>
internal sealed class ShareImageContext : IDisposable
{

    public CanvasDevice Device { get; }
    public Color Accent { get; }
    public ShareImageIconCache Icons { get; } = new();

    public CanvasTextFormat Title { get; }
    public CanvasTextFormat Body { get; }
    public CanvasTextFormat Small { get; }
    public CanvasTextFormat Score { get; }
    public CanvasTextFormat WrapSmall { get; }
    public CanvasTextFormat TrimBody { get; }

    public ShareImageContext(CanvasDevice device, Color accent)
    {
        Device = device;
        Accent = accent;
        Title = ShareImageCanvas.CreateTextFormat(16f, ShareImageCanvas.SemiBold, trimming: true);
        Body = ShareImageCanvas.CreateTextFormat(14f);
        Small = ShareImageCanvas.CreateTextFormat(12f);
        Score = ShareImageCanvas.CreateTextFormat(24f, ShareImageCanvas.Bold);
        WrapSmall = ShareImageCanvas.CreateTextFormat(12f, wrap: true);
        TrimBody = ShareImageCanvas.CreateTextFormat(14f, trimming: true);
    }

    public void Dispose()
    {
        Title.Dispose();
        Body.Dispose();
        Small.Dispose();
        Score.Dispose();
        WrapSmall.Dispose();
        TrimBody.Dispose();
        Icons.Dispose();
    }

}


/// <summary>图标预加载：相同 URI 共享任务，渲染结束后统一释放。</summary>
internal sealed class ShareImageIconCache : IDisposable
{

    private readonly Dictionary<string, Task<CanvasBitmap?>> _loads = new(StringComparer.Ordinal);
    private readonly List<CanvasBitmap> _bitmaps = [];

    public void Add(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || _loads.ContainsKey(uri))
        {
            return;
        }

        _loads[uri] = LoadAsync(uri);
    }


    public async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        foreach (Task<CanvasBitmap?> task in _loads.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CanvasBitmap? bitmap = await task;
            if (bitmap is not null)
            {
                _bitmaps.Add(bitmap);
            }
        }
    }


    public CanvasBitmap? Get(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        return _loads.TryGetValue(uri, out Task<CanvasBitmap?>? task) ? task.GetAwaiter().GetResult() : null;
    }


    public void Dispose()
    {
        foreach (CanvasBitmap bitmap in _bitmaps)
        {
            bitmap.Dispose();
        }
    }


    private static async Task<CanvasBitmap?> LoadAsync(string icon)
    {
        try
        {
            string? localPath = ResolveLocalPath(icon);
            if (localPath is not null && File.Exists(localPath))
            {
                ImageInfo info = await ImageLoader.LoadImageAsync(localPath);
                return info.CanvasBitmap;
            }

            if (Uri.TryCreate(icon, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
            {
                string? cached = await FileCache.GetFromCacheAsync(uri, false);
                if (cached is not null && File.Exists(cached))
                {
                    ImageInfo info = await ImageLoader.LoadImageAsync(cached);
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


    private static string? ResolveLocalPath(string icon)
    {
        if (icon.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase))
        {
            string relative = new Uri(icon).AbsolutePath.TrimStart('/');
            return Path.Combine(AppContext.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        if (icon.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(icon).LocalPath;
        }

        return File.Exists(icon) ? icon : null;
    }

}
