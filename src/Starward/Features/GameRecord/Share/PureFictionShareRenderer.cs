using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.StarRail.PureFiction;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 虚构叙事分享图：周期统计 + 两列关卡卡片（星数、分数、双队）。
/// </summary>
internal static class PureFictionShareRenderer
{

    private const float FloorWidth = 388f;
    private const float FloorHeight = 304f;
    private const float FloorGap = 12f;
    private const float CanvasWidth = ShareImageCanvas.OuterMargin * 2 + FloorWidth * 2 + FloorGap;


    public static async Task<string> RenderAndSaveAsync(
        PureFictionInfo info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        ctx.Icons.Add(ShareImageCanvas.StarIconStarRail);
        ctx.Icons.Add(ShareImageCanvas.StarIconStarRailExtra);
        if (info.AllFloorDetail is not null)
        {
            foreach (PureFictionFloorDetail floor in info.AllFloorDetail)
            {
                CollectNode(floor.Node1, ctx.Icons);
                CollectNode(floor.Node2, ctx.Icons);
                CollectNode(floor.Node3, ctx.Icons);
            }
        }

        await ctx.Icons.LoadAllAsync(cancellationToken);
        int floors = info.AllFloorDetail?.Count ?? 0;
        int rows = (int)Math.Ceiling(Math.Max(floors, 1) / 2.0);
        float contentHeight = 22f + 56f + 12f + rows * (FloorHeight + FloorGap);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "pure_fiction",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, info),
            cancellationToken);
    }


    private static void CollectNode(PureFictionNode? node, ShareImageIconCache icons)
    {
        if (node?.Avatars is null)
        {
            return;
        }

        foreach (PureFictionAvatar avatar in node.Avatars)
        {
            icons.Add(ShareImageCanvas.StarRailRarityBg(avatar.Rarity));
            icons.Add(avatar.Icon);
        }
    }


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, PureFictionInfo info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        float summaryW = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(info.BeginTime, info.EndTime), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        ShareImageCanvas.DrawTextRight(ds, info.Name, x + summaryW, y, ctx.Body, ShareImageCanvas.PrimaryText);
        y += 22f;

        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, summaryW, 48f);
        float col = summaryW / 3f;
        StarRailSharePrimitives.DrawStarCount(ds, ctx, x + 24f, y + 12f, info.StarNum, info.ExtraStarNum);
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_FarthestStage}  {info.MaxFloor}", x + col, y + 14f, ctx.Body, ShareImageCanvas.PrimaryText, col - 8f);
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_TimesChallenged}  {info.BattleNum}", x + col * 2, y + 14f, ctx.Body, ShareImageCanvas.PrimaryText, col - 8f);
        y += 60f;

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


    private static void DrawFloor(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, float x, float y, PureFictionFloorDetail floor)
    {
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, FloorWidth, FloorHeight);
        ds.FillRectangle(x, y, FloorWidth, 56f, Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        ShareImageCanvas.DrawText(ds, floor.Name, x + 20f, y + 8f, ctx.Title, ShareImageCanvas.PrimaryText, 200f);
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_CyclesUsed} {floor.RoundNum}", x + 20f, y + 30f, ctx.Small, ShareImageCanvas.SecondaryText);
        StarRailSharePrimitives.DrawFloorStars(ds, ctx, x + FloorWidth - 16f, y + 10f, floor.NormalStarNum, floor.ExtraStarNum);
        ShareImageCanvas.DrawTextRight(
            ds,
            $"{Lang.ApocalypticShadowPage_TotalScore} {floor.TotalScore}",
            x + FloorWidth - 16f,
            y + 34f,
            ctx.Small,
            ctx.Accent);

        if (floor.IsFast)
        {
            return;
        }

        DrawNode(ds, ctx, x, y + 64f, floor.Node1, 1);
        DrawNode(ds, ctx, x, y + 174f, floor.Node2, 2);
    }


    private static void DrawNode(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, PureFictionNode? node, int index)
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
        StarRailSharePrimitives.DrawAvatarRow(ds, ctx, x + 20f, y + 18f, Map(node.Avatars));
    }


    private static IEnumerable<StarRailAvatarDraw>? Map(List<PureFictionAvatar>? avatars)
    {
        if (avatars is null)
        {
            return null;
        }

        var list = new List<StarRailAvatarDraw>(avatars.Count);
        foreach (PureFictionAvatar avatar in avatars)
        {
            list.Add(new StarRailAvatarDraw(avatar.Rarity, avatar.Icon, avatar.Level, avatar.Rank));
        }

        return list;
    }

}
