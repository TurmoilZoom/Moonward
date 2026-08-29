using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.StarRail.ForgottenHall;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 忘却之庭分享图：周期统计 + 两列关卡卡片（星数、回合、双队）。
/// </summary>
internal static class ForgottenHallShareRenderer
{

    private const float FloorWidth = 388f;
    private const float FloorHeight = 300f;
    private const float FloorGap = 12f;
    private const float CanvasWidth = ShareImageCanvas.OuterMargin * 2 + FloorWidth * 2 + FloorGap;


    public static async Task<string> RenderAndSaveAsync(
        ForgottenHallInfo info,
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
            foreach (ForgottenHallFloorDetail floor in info.AllFloorDetail)
            {
                CollectNode(floor.Node1, ctx.Icons);
                CollectNode(floor.Node2, ctx.Icons);
                CollectNode(floor.Node3, ctx.Icons);
            }
        }

        await ctx.Icons.LoadAllAsync(cancellationToken);
        float contentHeight = Measure(info);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "forgotten_hall",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, info),
            cancellationToken);
    }


    private static void CollectNode(ForgottenHallNode? node, ShareImageIconCache icons)
    {
        if (node?.Avatars is null)
        {
            return;
        }

        foreach (ForgottenHallAvatar avatar in node.Avatars)
        {
            icons.Add(ShareImageCanvas.StarRailRarityBg(avatar.Rarity));
            icons.Add(avatar.Icon);
        }
    }


    private static float Measure(ForgottenHallInfo info)
    {
        int floors = info.AllFloorDetail?.Count ?? 0;
        int rows = (int)Math.Ceiling(Math.Max(floors, 1) / 2.0);
        return 22f + 56f + 12f + rows * (FloorHeight + FloorGap);
    }


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, ForgottenHallInfo info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(info.BeginTime, info.EndTime), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        ShareImageCanvas.DrawTextRight(ds, info.Name, x + CanvasWidth - ShareImageCanvas.OuterMargin * 2, y, ctx.Body, ShareImageCanvas.PrimaryText);
        y += 22f;

        float summaryW = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
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
            float fx = x + colIndex * (FloorWidth + FloorGap);
            float fy = y + rowIndex * (FloorHeight + FloorGap);
            DrawFloor(ds, bg, ctx, fx, fy, info.AllFloorDetail[i]);
        }
    }


    private static void DrawFloor(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, float x, float y, ForgottenHallFloorDetail floor)
    {
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, FloorWidth, FloorHeight, 8f);
        ds.FillRectangle(x, y, FloorWidth, 52f, Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        ShareImageCanvas.DrawText(ds, floor.Name, x + 20f, y + 8f, ctx.Title, ShareImageCanvas.PrimaryText, 220f);
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_CyclesUsed} {floor.RoundNum}", x + 20f, y + 28f, ctx.Small, ShareImageCanvas.SecondaryText);
        StarRailSharePrimitives.DrawFloorStars(ds, ctx, x + FloorWidth - 16f, y + 14f, floor.NormalStarNum, floor.ExtraStarNum);

        if (floor.IsFast)
        {
            return;
        }

        float nodeY = y + 60f;
        DrawNode(ds, ctx, x, nodeY, floor.Node1, 1);
        DrawNode(ds, ctx, x, nodeY + 110f, floor.Node2, 2);
    }


    private static void DrawNode(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, ForgottenHallNode? node, int index)
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
        StarRailSharePrimitives.DrawAvatarRow(ds, ctx, x + 20f, y + 18f, node.Avatars);
    }

}


/// <summary>星铁忘却之庭 / 虚构叙事 / 末日幻影共用的星星与角色行。</summary>
internal static class StarRailSharePrimitives
{

    public static void DrawStarCount(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, int starNum, int extraStarNum)
    {
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StarIconStarRail), new Rect(x, y, 24f, 24f));
        ShareImageCanvas.DrawText(ds, starNum.ToString(), x + 32f, y + 2f, ctx.Title, ShareImageCanvas.PrimaryText);
        if (extraStarNum > 0)
        {
            ds.FillRectangle(x + 56f, y + 6f, 1f, 13f, Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StarIconStarRailExtra), new Rect(x + 64f, y, 24f, 24f));
        }
    }


    public static void DrawFloorStars(CanvasDrawingSession ds, ShareImageContext ctx, float right, float y, int normal, int extra)
    {
        float width = normal * 26f + (extra > 0 ? 10f + extra * 26f : 0);
        float x = right - width;
        ShareImageCanvas.DrawStarRow(ds, ctx.Icons.Get(ShareImageCanvas.StarIconStarRail), x, y, normal, 24f, 2f);
        if (extra > 0)
        {
            x += normal * 26f + 6f;
            ds.FillRectangle(x, y + 6f, 1f, 13f, Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            ShareImageCanvas.DrawStarRow(ds, ctx.Icons.Get(ShareImageCanvas.StarIconStarRailExtra), x + 8f, y, extra, 24f, 2f);
        }
    }


    public static void DrawAvatarRow(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, IEnumerable<StarRailAvatarDraw>? avatars)
    {
        if (avatars is null)
        {
            return;
        }

        float ax = x;
        foreach (StarRailAvatarDraw avatar in avatars)
        {
            ShareImageCanvas.DrawStarRailAvatar(ds, ctx, ax, y, avatar.Rarity, avatar.Icon, avatar.Level, avatar.Rank);
            ax += 84f;
        }
    }


    public static void DrawAvatarRow(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, List<ForgottenHallAvatar>? avatars)
    {
        if (avatars is null)
        {
            return;
        }

        DrawAvatarRow(ds, ctx, x, y, Avatars(avatars));
    }


    private static IEnumerable<StarRailAvatarDraw> Avatars(List<ForgottenHallAvatar> avatars)
    {
        foreach (ForgottenHallAvatar avatar in avatars)
        {
            yield return new StarRailAvatarDraw(avatar.Rarity, avatar.Icon, avatar.Level, avatar.Rank);
        }
    }

}


internal readonly record struct StarRailAvatarDraw(int Rarity, string? Icon, int Level, int Rank);
