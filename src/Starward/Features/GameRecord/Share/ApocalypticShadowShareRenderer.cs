using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.StarRail.ApocalypticShadow;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 末日幻影分享图：周期统计、Boss、两列关卡卡片（星数、分数、双队 Buff）。
/// </summary>
internal static class ApocalypticShadowShareRenderer
{

    private const float FloorWidth = 388f;
    private const float FloorHeight = 360f;
    private const float FloorGap = 12f;
    private const float CanvasWidth = ShareImageCanvas.OuterMargin * 2 + FloorWidth * 2 + FloorGap;


    public static async Task<string> RenderAndSaveAsync(
        ApocalypticShadowInfo info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        ctx.Icons.Add(ShareImageCanvas.StarIconStarRail);
        ctx.Icons.Add(ShareImageCanvas.StarIconStarRailExtra);
        if (info.Meta is not null)
        {
            AddBoss(info.Meta.UpperBoss, ctx.Icons);
            AddBoss(info.Meta.LowerBoss, ctx.Icons);
            AddBoss(info.Meta.TierceBoss, ctx.Icons);
        }

        if (info.AllFloorDetail is not null)
        {
            foreach (ApocalypticShadowFloorDetail floor in info.AllFloorDetail)
            {
                CollectNode(floor.Node1, ctx.Icons);
                CollectNode(floor.Node2, ctx.Icons);
                CollectNode(floor.Node3, ctx.Icons);
            }
        }

        await ctx.Icons.LoadAllAsync(cancellationToken);
        int floors = info.AllFloorDetail?.Count ?? 0;
        int rows = (int)Math.Ceiling(Math.Max(floors, 1) / 2.0);
        float contentHeight = 22f + 100f + 12f + rows * (FloorHeight + FloorGap);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "apocalyptic_shadow",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, info),
            cancellationToken);
    }


    private static void AddBoss(ApocalypticShadowBossMeta? boss, ShareImageIconCache icons)
    {
        if (boss is not null)
        {
            icons.Add(boss.Icon);
        }
    }


    private static void CollectNode(ApocalypticShadowNode? node, ShareImageIconCache icons)
    {
        if (node is null)
        {
            return;
        }

        if (node.Avatars is not null)
        {
            foreach (ApocalypticShadowAvatar avatar in node.Avatars)
            {
                icons.Add(ShareImageCanvas.StarRailRarityBg(avatar.Rarity));
                icons.Add(avatar.Icon);
            }
        }

        if (node.Buff is not null)
        {
            icons.Add(node.Buff.Icon);
        }
    }


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, ApocalypticShadowInfo info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        float summaryW = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(info.BeginTime, info.EndTime), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        ShareImageCanvas.DrawTextRight(ds, info.Meta?.Name, x + summaryW, y, ctx.Body, ShareImageCanvas.PrimaryText);
        y += 22f;

        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, summaryW, 92f);
        float col = summaryW / 3f;
        StarRailSharePrimitives.DrawStarCount(ds, ctx, x + 24f, y + 10f, info.StarNum, info.ExtraStarNum);
        ShareImageCanvas.DrawText(ds, $"{Lang.ApocalypticShadowPage_HighestDifficultyCleared}  {info.MaxFloor}", x + col, y + 12f, ctx.Body, ShareImageCanvas.PrimaryText, col - 8f);
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_TimesChallenged}  {info.BattleNum}", x + col * 2, y + 12f, ctx.Body, ShareImageCanvas.PrimaryText, col - 8f);

        float bossY = y + 48f;
        DrawBoss(ds, ctx, x + col * 0.5f - 80f, bossY, info.Meta?.UpperBoss, 1);
        DrawBoss(ds, ctx, x + col * 1.5f - 80f, bossY, info.Meta?.LowerBoss, 2);
        DrawBoss(ds, ctx, x + col * 2.5f - 80f, bossY, info.Meta?.TierceBoss, 3);
        y += 104f;

        if (info.AllFloorDetail is null)
        {
            return;
        }

        for (int i = 0; i < info.AllFloorDetail.Count; i++)
        {
            int colIndex = i % 2;
            int rowIndex = i / 2;
            DrawFloor(ds, bg, ctx, x + colIndex * (FloorWidth + FloorGap), y + rowIndex * (FloorHeight + FloorGap), info.AllFloorDetail[i]);
        }
    }


    private static void DrawBoss(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, ApocalypticShadowBossMeta? boss, int index)
    {
        if (boss is null)
        {
            return;
        }

        ShareImageCanvas.DrawRoundedImage(ds, ctx.Device, ctx.Icons.Get(boss.Icon), new Rect(x, y, 40f, 40f), 20f);
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_TeamSetup} {index}", x + 48f, y, ctx.Small, ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawText(ds, boss.Name, x + 48f, y + 16f, ctx.Small, ShareImageCanvas.SecondaryText, 140f);
    }


    private static void DrawFloor(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, float x, float y, ApocalypticShadowFloorDetail floor)
    {
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, FloorWidth, FloorHeight);
        ds.FillRectangle(x, y, FloorWidth, 56f, Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        ShareImageCanvas.DrawText(ds, floor.Name, x + 20f, y + 10f, ctx.Title, ShareImageCanvas.PrimaryText, 200f);
        StarRailSharePrimitives.DrawFloorStars(ds, ctx, x + FloorWidth - 16f, y + 8f, floor.NormalStarNum, floor.ExtraStarNum);
        ShareImageCanvas.DrawTextRight(
            ds,
            $"{Lang.ApocalypticShadowPage_TotalScore} {floor.TotalScore}",
            x + FloorWidth - 16f,
            y + 32f,
            ctx.Small,
            ctx.Accent);

        if (floor.IsFast)
        {
            return;
        }

        DrawNode(ds, ctx, x, y + 64f, floor.Node1, 1);
        DrawNode(ds, ctx, x, y + 200f, floor.Node2, 2);
    }


    private static void DrawNode(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, ApocalypticShadowNode? node, int index)
    {
        if (node is null)
        {
            return;
        }

        ShareImageCanvas.DrawText(
            ds,
            $"{Lang.ForgottenHallPage_TeamSetup} {index}  {node.ChallengeTime:yyyy-MM-dd HH:mm:ss}",
            x + 20f,
            y,
            ctx.Small,
            ShareImageCanvas.SecondaryText);
        ShareImageCanvas.DrawTextRight(
            ds,
            $"{Lang.PureFictionPage_Score} {node.Score}",
            x + FloorWidth - 16f,
            y,
            ctx.Small,
            ctx.Accent);
        if (node.Buff is not null)
        {
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(node.Buff.Icon), new Rect(x + 20f, y + 18f, 20f, 20f));
            ShareImageCanvas.DrawText(ds, node.Buff.Name, x + 44f, y + 18f, ctx.Small, ShareImageCanvas.SecondaryText, 300f);
        }

        StarRailSharePrimitives.DrawAvatarRow(ds, ctx, x + 20f, y + 42f, Map(node.Avatars));
    }


    private static IEnumerable<StarRailAvatarDraw>? Map(List<ApocalypticShadowAvatar>? avatars)
    {
        if (avatars is null)
        {
            return null;
        }

        var list = new List<StarRailAvatarDraw>(avatars.Count);
        foreach (ApocalypticShadowAvatar avatar in avatars)
        {
            list.Add(new StarRailAvatarDraw(avatar.Rarity, avatar.Icon, avatar.Level, avatar.Rank));
        }

        return list;
    }

}
