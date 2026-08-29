using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.Genshin.SpiralAbyss;
using System;
using Starward.Features.GameRecord.Genshin;
using Starward.Language;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 深境螺旋分享图：周期统计、出战次数、战斗数据榜、各层间阵容。
/// </summary>
internal static class SpiralAbyssShareRenderer
{

    private const float CanvasWidth = 760f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
    private const float RevealAvatarW = 80f;
    private const float RevealAvatarH = 97f;
    private const float ChamberAvatarW = 60f;
    private const float ChamberAvatarH = 73f;


    public static async Task<string> RenderAndSaveAsync(
        SpiralAbyssInfo info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        CollectIcons(info, ctx.Icons);
        await ctx.Icons.LoadAllAsync(cancellationToken);
        float contentHeight = Measure(info);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "spiral_abyss",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, info),
            cancellationToken);
    }


    private static void CollectIcons(SpiralAbyssInfo info, ShareImageIconCache icons)
    {
        icons.Add(ShareImageCanvas.StarIconGenshinAbyss);
        CollectRanks(info.RevealRank, icons);
        CollectRanks(info.DefeatRank, icons);
        CollectRanks(info.DamageRank, icons);
        CollectRanks(info.TakeDamageRank, icons);
        CollectRanks(info.NormalSkillRank, icons);
        CollectRanks(info.EnergySkillRank, icons);
        if (info.Floors is null)
        {
            return;
        }

        foreach (SpiralAbyssFloor floor in info.Floors)
        {
            if (floor.Levels is null)
            {
                continue;
            }

            foreach (SpiralAbyssLevel level in floor.Levels)
            {
                if (level.Battles is null)
                {
                    continue;
                }

                foreach (SpiralAbyssBattle battle in level.Battles)
                {
                    if (battle.Avatars is null)
                    {
                        continue;
                    }

                    foreach (SpiralAbyssAvatar avatar in battle.Avatars)
                    {
                        icons.Add(ShareImageCanvas.GenshinRarityBg(avatar.Rarity));
                        icons.Add(avatar.Icon);
                    }
                }
            }
        }
    }


    private static void CollectRanks(List<SpiralAbyssRank>? ranks, ShareImageIconCache icons)
    {
        if (ranks is null)
        {
            return;
        }

        foreach (SpiralAbyssRank rank in ranks)
        {
            icons.Add(ShareImageCanvas.GenshinRarityBg(rank.Rarity));
            icons.Add(rank.AvatarIcon);
        }
    }


    private static float Measure(SpiralAbyssInfo info)
    {
        float y = 22f + 56f + 16f;
        int reveal = info.RevealRank?.Count ?? 0;
        if (reveal > 0)
        {
            y += 28f + 120f;
        }

        y += 28f + 180f;
        if (info.Floors is not null)
        {
            foreach (SpiralAbyssFloor floor in info.Floors)
            {
                int levels = floor.Levels?.Count ?? 0;
                y += 16f + 40f + levels * 120f;
            }
        }

        return y;
    }


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, SpiralAbyssInfo info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(info.StartTime, info.EndTime), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        y += 22f;

        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, 48f);
        float col = ContentWidth / 3f;
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StarIconGenshinAbyss), new Rect(x + col * 0.5f - 40f, y + 12f, 24f, 24f));
        ShareImageCanvas.DrawText(ds, info.TotalStar.ToString(), x + col * 0.5f - 8f, y + 14f, ctx.Title, ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawText(ds, $"{Lang.SpiralAbyssPage_DeepestDescent}  {info.MaxFloor}", x + col + 16f, y + 14f, ctx.Title, ShareImageCanvas.PrimaryText, col - 24f);
        ShareImageCanvas.DrawText(ds, $"{Lang.SpiralAbyssPage_BattlesFought}  {info.TotalBattleCount}", x + col * 2 + 16f, y + 14f, ctx.Title, ShareImageCanvas.PrimaryText, col - 24f);
        y += 64f;

        if (info.RevealRank is { Count: > 0 })
        {
            ShareImageCanvas.DrawText(ds, Lang.SpiralAbyssPage_Picked, x, y, ctx.Title, ShareImageCanvas.SecondaryText);
            y += 24f;
            ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, 120f);
            float revealX = x + (ContentWidth - info.RevealRank.Count * (RevealAvatarW + 36f) + 36f) / 2f;
            foreach (SpiralAbyssRank rank in info.RevealRank)
            {
                ShareImageCanvas.DrawGenshinAvatar(
                    ds,
                    ctx,
                    revealX,
                    y + 8f,
                    RevealAvatarW,
                    RevealAvatarH,
                    rank.Rarity,
                    rank.AvatarIcon,
                    0,
                    footer: $"{rank.Value} {Lang.SpiralAbyssPage_Times}");
                revealX += RevealAvatarW + 36f;
            }

            y += 132f;
        }

        ShareImageCanvas.DrawText(ds, Lang.SpiralAbyssPage_CombatStats, x, y, ctx.Title, ShareImageCanvas.SecondaryText);
        y += 24f;
        const float statsH = 168f;
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, statsH);
        DrawStatRow(ds, ctx, x, y, 0, Lang.SpiralAbyssPage_MostDefeats, info.FirstDefeatRank, stripe: false);
        DrawStatRow(ds, ctx, x, y, 1, Lang.SpiralAbyssPage_StrongestSingleStrike, info.FirstDamageRank, stripe: true);
        DrawStatRow(ds, ctx, x, y, 2, Lang.SpiralAbyssPage_MostDamageTaken, info.FirstTakeDamageRank, stripe: false);
        DrawStatRow(ds, ctx, x, y, 3, Lang.SpiralAbyssPage_ElementalSkillsUnleashed, info.FirstNormalSkillRank, stripe: true);
        DrawStatRow(ds, ctx, x, y, 4, Lang.SpiralAbyssPage_ElementalBrustsUnleashed, info.FirstEnergySkillRank, stripe: false);
        y += statsH + 16f;

        if (info.Floors is null)
        {
            return;
        }

        foreach (SpiralAbyssFloor floor in info.Floors)
        {
            y += DrawFloor(ds, bg, ctx, x, y, floor) + 16f;
        }
    }


    private static void DrawStatRow(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float cardX,
        float cardY,
        int index,
        string label,
        SpiralAbyssRank? rank,
        bool stripe)
    {
        float rowY = cardY + 6f + index * 32f;
        if (stripe)
        {
            ds.FillRectangle(cardX, rowY, ContentWidth, 32f, Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        }

        ShareImageCanvas.DrawText(ds, label, cardX + 24f, rowY + 6f, ctx.Body, ShareImageCanvas.PrimaryText);
        if (rank is not null)
        {
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(rank.AvatarIcon), new Rect(cardX + ContentWidth * 0.55f, rowY - 2f, 44f, 36f));
            ShareImageCanvas.DrawText(ds, rank.Value.ToString(), cardX + ContentWidth * 0.75f, rowY + 6f, ctx.Body, ShareImageCanvas.PrimaryText);
        }
    }


    private static float DrawFloor(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, float x, float y, SpiralAbyssFloor floor)
    {
        int levels = floor.Levels?.Count ?? 0;
        float height = 40f + levels * 120f;
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, height);
        ShareImageCanvas.DrawText(ds, SpiralAbyssPage.FloorX(floor.Index), x + 24f, y + 10f, ctx.Title, ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StarIconGenshinAbyss), new Rect(x + ContentWidth - 72f, y + 8f, 24f, 24f));
        ShareImageCanvas.DrawTextRight(ds, $"{floor.Star}/{floor.MaxStar}", x + ContentWidth - 24f, y + 10f, ctx.Title, ShareImageCanvas.PrimaryText);

        float rowY = y + 40f;
        if (floor.Levels is null)
        {
            return height;
        }

        foreach (SpiralAbyssLevel level in floor.Levels)
        {
            ShareImageCanvas.DrawFadeSeparator(ds, x + 24f, x + ContentWidth - 24f, rowY);
            ShareImageCanvas.DrawText(ds, SpiralAbyssPage.LevelX(level.Index), x + 24f, rowY + 8f, ctx.Small, ShareImageCanvas.SecondaryText);
            ShareImageCanvas.DrawText(ds, level.FirstBattleTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), x + 90f, rowY + 8f, ctx.Small, ShareImageCanvas.SecondaryText);
            ShareImageCanvas.DrawStarRow(
                ds,
                ctx.Icons.Get(ShareImageCanvas.StarIconGenshinAbyss),
                x + ContentWidth - 24f - level.Star * 22f,
                rowY + 6f,
                level.Star,
                20f,
                2f);

            float teamY = rowY + 28f;
            DrawBattleAvatars(ds, ctx, x + 24f, teamY, FirstAvatars(level, 0));
            DrawBattleAvatars(ds, ctx, x + ContentWidth / 2f + 12f, teamY, FirstAvatars(level, 1));
            rowY += 120f;
        }

        return height;
    }


    private static List<SpiralAbyssAvatar>? FirstAvatars(SpiralAbyssLevel level, int battleIndex)
    {
        if (level.Battles is null || level.Battles.Count <= battleIndex)
        {
            return null;
        }

        return level.Battles[battleIndex].Avatars;
    }


    private static void DrawBattleAvatars(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, List<SpiralAbyssAvatar>? avatars)
    {
        if (avatars is null)
        {
            return;
        }

        float ax = x;
        foreach (SpiralAbyssAvatar avatar in avatars)
        {
            ShareImageCanvas.DrawGenshinAvatar(ds, ctx, ax, y, ChamberAvatarW, ChamberAvatarH, avatar.Rarity, avatar.Icon, avatar.Level);
            ax += ChamberAvatarW + 8f;
        }
    }

}
