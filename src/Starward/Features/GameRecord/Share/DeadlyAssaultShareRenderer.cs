using Starward.Core.GameRecord.ZZZ;
using Starward.Core.GameRecord.ZZZ.DeadlyAssault;
using Starward.Features.GameRecord.ZZZ;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 绝区零危局强袭战分享图：绝境/常规分组卡片 + 节点（Boss、分数、星级、阵容、邦布、Buff）。
/// </summary>
internal static class DeadlyAssaultShareRenderer
{

    private const float CanvasWidth = 880f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
    private const float NodeBossWidth = 142f;
    private const float NodeBossHeight = 194f;
    private const float AvatarWidth = 76f;
    private const float AvatarHeight = 94f;
    private const float BuddyWidth = 58f;
    private const float AvatarGap = 8f;


    public static async Task<string> RenderAndSaveAsync(
        DeadlyAssaultInfo info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        CollectIcons(info, ctx.Icons);
        await ctx.Icons.LoadAllAsync(cancellationToken);

        float contentHeight = Measure(info, ctx);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "deadly_assault",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, info),
            cancellationToken);
    }


    private static void CollectIcons(DeadlyAssaultInfo info, ShareImageIconCache icons)
    {
        icons.Add(ShareImageCanvas.StarIconZzz);
        icons.Add(ShareImageCanvas.ZzzAvatarCardBackground);
        CollectNodeIcons(info.HardList, icons);
        CollectNodeIcons(info.AllNodes, icons);
    }


    private static void CollectNodeIcons(List<DeadlyAssaultNode>? nodes, ShareImageIconCache icons)
    {
        if (nodes is null)
        {
            return;
        }

        foreach (DeadlyAssaultNode node in nodes)
        {
            DeadlyAssaultBoss? boss = FirstBoss(node);
            if (boss is not null)
            {
                icons.Add(boss.BgIcon);
                icons.Add(boss.Icon);
                icons.Add(boss.RaceIcon);
            }

            if (node.Buff is { Count: > 0 })
            {
                icons.Add(node.Buff[0].Icon);
            }

            if (node.Avatars is not null)
            {
                foreach (ZZZAvatar avatar in node.Avatars)
                {
                    icons.Add(avatar.RoleSquareUrl);
                    icons.Add(ShareImageCanvas.ZzzRarityIcon(avatar.Rarity));
                }
            }

            if (node.Buddy is not null)
            {
                icons.Add(node.Buddy.RectangleUrl);
                icons.Add(ShareImageCanvas.ZzzRarityIcon(node.Buddy.Rarity));
            }
        }
    }


    private static float Measure(DeadlyAssaultInfo info, ShareImageContext ctx)
    {
        float y = 22f;
        if (info.HasHard)
        {
            y += 12f + MeasureGroup(info.HardList, ctx);
        }

        y += 16f + MeasureGroup(info.AllNodes, ctx);
        return y;
    }


    private static float MeasureGroup(List<DeadlyAssaultNode>? nodes, ShareImageContext ctx)
    {
        float height = 12f + 24f + 8f;
        if (nodes is { Count: > 0 })
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i > 0)
                {
                    height += 12f;
                }

                height += MeasureNode(nodes[i], ctx);
            }
        }

        return height + 12f;
    }


    private static float MeasureNode(DeadlyAssaultNode node, ShareImageContext ctx)
    {
        float buffWidth = ContentWidth - 32f - NodeBossWidth - 16f - MeasureTeamWidth(node) - 16f;
        buffWidth = Math.Max(120f, buffWidth);
        float buffHeight = 32f;
        if (node.Buff is { Count: > 0 })
        {
            string desc = ShareImageCanvas.StripMarkup(node.Buff[0].Desc);
            buffHeight += 8f + ShareImageCanvas.MeasureTextHeight(ctx.Device, desc, ctx.WrapSmall, buffWidth);
        }

        float teamHeight = 16f + 28f + 16f + 16f + 4f + AvatarHeight;
        return 24f + Math.Max(NodeBossHeight, Math.Max(teamHeight, buffHeight));
    }


    private static float MeasureTeamWidth(DeadlyAssaultNode node)
    {
        int avatarCount = node.Avatars?.Count ?? 0;
        float width = avatarCount * AvatarWidth + Math.Max(0, avatarCount - 1) * AvatarGap;
        if (node.Buddy is not null)
        {
            if (avatarCount > 0)
            {
                width += AvatarGap;
            }

            width += BuddyWidth;
        }

        return width;
    }


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, DeadlyAssaultInfo info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        ShareImageCanvas.DrawText(
            ds,
            ShareImageCanvas.FormatPeriod(info.StartTime, info.EndTime),
            x,
            y,
            ctx.Small,
            ShareImageCanvas.TertiaryText);
        y += 22f;

        if (info.HasHard)
        {
            y += DrawGroup(
                ds,
                bg,
                ctx,
                x,
                y,
                Lang.DeadlyAssaultPage_HardMode,
                extraHeader: $"{Lang.DeadlyAssaultPage_GlobalTopPercent}  {DeadlyAssaultPage.RankPercentText(info.HardRankPercent)}",
                info.HardList);
            y += 16f;
        }

        string? normalTitle = info.HasHard ? Lang.DeadlyAssaultPage_NormalMode : null;
        DrawGroup(
            ds,
            bg,
            ctx,
            x,
            y,
            normalTitle,
            extraHeader: null,
            info.AllNodes,
            stars: info.TotalStar,
            rankText: $"{Lang.DeadlyAssaultPage_GlobalTopPercent}  {DeadlyAssaultPage.RankPercentText(info.RankPercent)}",
            scoreText: $"{Lang.DeadlyAssaultPage_TotalScore}  {DeadlyAssaultPage.TotalScoreText(info.TotalScore, info.TotalMaxScore)}");
    }


    private static float DrawGroup(
        CanvasDrawingSession ds,
        CanvasBitmap bg,
        ShareImageContext ctx,
        float x,
        float y,
        string? title,
        string? extraHeader,
        List<DeadlyAssaultNode>? nodes,
        int? stars = null,
        string? rankText = null,
        string? scoreText = null)
    {
        float height = MeasureGroup(nodes, ctx);
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, height);
        float innerX = x + 16f;
        float innerY = y + 12f;
        float innerRight = x + ContentWidth - 16f;

        if (!string.IsNullOrEmpty(title))
        {
            ShareImageCanvas.DrawText(ds, title, innerX, innerY, ctx.Title, ShareImageCanvas.PrimaryText);
        }

        float headerCenter = innerY + 2f;
        float colW = ContentWidth - 32f;
        if (stars is int starCount)
        {
            float starX = string.IsNullOrEmpty(title) ? innerX + colW * 0.12f : innerX + colW * 0.28f;
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StarIconZzz), new Rect(starX, headerCenter, 24f, 24f));
            ShareImageCanvas.DrawText(ds, starCount.ToString(), starX + 32f, headerCenter + 2f, ctx.Title, ShareImageCanvas.PrimaryText);
        }

        if (!string.IsNullOrEmpty(rankText) || !string.IsNullOrEmpty(extraHeader))
        {
            string text = extraHeader ?? rankText!;
            float textWidth = ShareImageCanvas.MeasureTextWidth(ds, text, ctx.Body);
            float textX = string.IsNullOrEmpty(title) && stars is null ? innerX : innerX + colW * 0.48f;
            if (stars is not null)
            {
                textX = innerX + colW * 0.48f;
            }

            ShareImageCanvas.DrawText(ds, text, textX, headerCenter + 4f, ctx.Body, ShareImageCanvas.PrimaryText, textWidth + 8f);
        }

        if (!string.IsNullOrEmpty(scoreText))
        {
            ShareImageCanvas.DrawTextRight(ds, scoreText, innerRight, headerCenter + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
        }

        innerY += 32f;
        if (nodes is { Count: > 0 })
        {
            foreach (DeadlyAssaultNode node in nodes)
            {
                innerY += DrawNode(ds, ctx, innerX, innerY, innerRight - innerX, node);
                innerY += 12f;
            }
        }

        return height;
    }


    private static float DrawNode(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, float width, DeadlyAssaultNode node)
    {
        float height = MeasureNode(node, ctx) - 0f;
        using var border = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateRoundedRectangle(ctx.Device, x, y, width, height, 8f, 8f);
        ds.DrawGeometry(border, Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 1f);

        float pad = 8f;
        DeadlyAssaultBoss? boss = FirstBoss(node);
        if (boss is not null)
        {
            using var bossClip = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateRoundedRectangle(
                ctx.Device, x + pad, y + 12f, NodeBossWidth, NodeBossHeight, 16f, 16f);
            using (ds.CreateLayer(1f, bossClip))
            {
                if (ctx.Icons.Get(boss.BgIcon) is CanvasBitmap bg)
                {
                    ShareImageCanvas.DrawCoverImage(ds, bg, NodeBossWidth, NodeBossHeight, x + pad, y + 12f);
                }

                ShareImageCanvas.DrawUniformImage(ds, ctx.Icons.Get(boss.Icon), new Rect(x + pad, y + 12f, NodeBossWidth, NodeBossHeight));
            }

            ds.DrawRoundedRectangle(x + pad, y + 12f, NodeBossWidth, NodeBossHeight, 16f, 16f, Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 2f);
            if (ctx.Icons.Get(boss.RaceIcon) is CanvasBitmap race)
            {
                ShareImageCanvas.DrawImage(ds, race, new Rect(x + pad + NodeBossWidth - 52f, y + 12f + NodeBossHeight - 52f, 48f, 48f));
            }
        }

        float colX = x + pad + NodeBossWidth + 16f;
        float colY = y + 12f;
        if (boss is not null)
        {
            ShareImageCanvas.DrawText(ds, boss.Name, colX, colY, ctx.Title, ShareImageCanvas.SecondaryText, 280f);
        }

        colY += 24f;
        ShareImageCanvas.DrawText(ds, node.Score.ToString(), colX, colY, ctx.Score, ShareImageCanvas.SecondaryText);
        float scoreW = ShareImageCanvas.MeasureTextWidth(ds, node.Score.ToString(), ctx.Score);
        ShareImageCanvas.DrawStarRow(ds, ctx.Icons.Get(ShareImageCanvas.StarIconZzz), colX + scoreW + 8f, colY + 8f, node.Star, 24f, 2f);

        colY += 36f;
        ShareImageCanvas.DrawText(
            ds,
            $"{Lang.ClearedAt}  {node.ChallengeTime:yyyy-MM-dd HH:mm:ss}",
            colX,
            colY,
            ctx.Small,
            ShareImageCanvas.SecondaryText);
        colY += 20f;

        float teamX = colX;
        if (node.Avatars is not null)
        {
            foreach (ZZZAvatar avatar in node.Avatars)
            {
                ShareImageCanvas.DrawZzzAvatar(ds, ctx, teamX, colY, avatar.Rarity, avatar.RoleSquareUrl, avatar.Level, avatar.Rank);
                teamX += AvatarWidth + AvatarGap;
            }
        }

        if (node.Buddy is not null)
        {
            ShareImageCanvas.DrawZzzBuddy(ds, ctx, teamX, colY + 16f, node.Buddy.Rarity, node.Buddy.RectangleUrl, node.Buddy.Level);
        }

        if (node.Buff is { Count: > 0 })
        {
            float teamW = MeasureTeamWidth(node);
            float buffX = colX + teamW + 16f;
            float buffY = y + 12f;
            float buffWidth = x + width - 8f - buffX;
            if (buffWidth > 80f)
            {
                ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(node.Buff[0].Icon), new Rect(buffX, buffY, 32f, 32f));
                ShareImageCanvas.DrawText(ds, node.Buff[0].Name, buffX + 40f, buffY + 6f, ctx.Title, ShareImageCanvas.PrimaryText, Math.Max(40f, buffWidth - 40f));
                string desc = ShareImageCanvas.StripMarkup(node.Buff[0].Desc);
                ShareImageCanvas.DrawText(ds, desc, buffX, buffY + 40f, ctx.WrapSmall, ShareImageCanvas.SecondaryText, buffWidth);
            }
        }

        return height;
    }


    private static DeadlyAssaultBoss? FirstBoss(DeadlyAssaultNode node)
        => node.Boss is { Count: > 0 } ? node.Boss[0] : null;

}
