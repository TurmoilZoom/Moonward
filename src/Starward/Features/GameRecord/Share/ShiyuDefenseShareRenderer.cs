using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.ZZZ;
using Starward.Core.GameRecord.ZZZ.ShiyuDefense;
using Starward.Features.GameRecord.ZZZ;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 式舆防卫战分享图：v1 评分数 + 各防线双队；v2 第五/第四防线，与页面结构对齐。
/// </summary>
internal static class ShiyuDefenseShareRenderer
{

    private const float CanvasWidth = 760f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
    private const float AvatarWidth = 76f;
    private const float AvatarGap = 8f;
    private const float BuddyWidth = 58f;


    public static Task<string> RenderAndSaveAsync(
        ShiyuDefenseInfo info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return RenderV1Async(info, uid, backgroundFile, accentColor, cancellationToken);
    }


    public static Task<string> RenderAndSaveAsync(
        ShiyuDefenseInfoV2 info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return RenderV2Async(info, uid, backgroundFile, accentColor, cancellationToken);
    }


    private static async Task<string> RenderV1Async(
        ShiyuDefenseInfo info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken)
    {
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        CollectV1Icons(info, ctx.Icons);
        await ctx.Icons.LoadAllAsync(cancellationToken);
        float contentHeight = MeasureV1(info);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "shiyu_defense",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => DrawV1(ds, bg, ctx, info),
            cancellationToken);
    }


    private static async Task<string> RenderV2Async(
        ShiyuDefenseInfoV2 info,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken)
    {
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        CollectV2Icons(info, ctx.Icons);
        await ctx.Icons.LoadAllAsync(cancellationToken);
        float contentHeight = MeasureV2(info);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "shiyu_defense",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => DrawV2(ds, bg, ctx, info),
            cancellationToken);
    }


    private static void CollectV1Icons(ShiyuDefenseInfo info, ShareImageIconCache icons)
    {
        icons.Add(ShareImageCanvas.ZzzAvatarCardBackground);
        icons.Add(ShareImageCanvas.ZzzRatingLargeIcon("S"));
        icons.Add(ShareImageCanvas.ZzzRatingLargeIcon("A"));
        icons.Add(ShareImageCanvas.ZzzRatingLargeIcon("B"));
        if (info.AllFloorDetail is null)
        {
            return;
        }

        foreach (ShiyuDefenseFloorDetail floor in info.AllFloorDetail)
        {
            icons.Add(ShareImageCanvas.ZzzRarityIcon(floor.Rating));
            CollectTeam(floor.Node1, icons);
            CollectTeam(floor.Node2, icons);
        }
    }


    private static void CollectV2Icons(ShiyuDefenseInfoV2 info, ShareImageIconCache icons)
    {
        icons.Add(ShareImageCanvas.ZzzAvatarCardBackground);
        if (info.Brief is not null)
        {
            icons.Add(ShareImageCanvas.ZzzRarityIcon(info.Brief.Rating));
        }

        if (info.FifthLayerDetail?.LayerChallenges is not null)
        {
            foreach (ShiyuDefenseV2FifthLayerChallengeInfo challenge in info.FifthLayerDetail.LayerChallenges)
            {
                icons.Add(challenge.MonsterPic);
                icons.Add(ShareImageCanvas.ZzzRarityIcon(challenge.Rating));
                CollectAvatars(challenge.AvatarList, icons);
                CollectBuddy(challenge.Buddy, icons);
            }
        }

        if (info.FourthLayerDetail is not null)
        {
            icons.Add(ShareImageCanvas.ZzzRarityIcon(info.FourthLayerDetail.Rating));
            if (info.FourthLayerDetail.LayerChallenges is not null)
            {
                foreach (ShiyuDefenseV2FourthLayerChallengeInfo challenge in info.FourthLayerDetail.LayerChallenges)
                {
                    CollectAvatars(challenge.AvatarList, icons);
                    CollectBuddy(challenge.Buddy, icons);
                }
            }
        }
    }


    private static void CollectTeam(ShiyuDefenseNode? node, ShareImageIconCache icons)
    {
        if (node is null)
        {
            return;
        }

        CollectAvatars(node.Avatars, icons);
        CollectBuddy(node.Buddy, icons);
    }


    private static void CollectAvatars(List<ZZZAvatar>? avatars, ShareImageIconCache icons)
    {
        if (avatars is null)
        {
            return;
        }

        foreach (ZZZAvatar avatar in avatars)
        {
            icons.Add(avatar.RoleSquareUrl);
            icons.Add(ShareImageCanvas.ZzzRarityIcon(avatar.Rarity));
        }
    }


    private static void CollectBuddy(ZZZBuddy? buddy, ShareImageIconCache icons)
    {
        if (buddy is null)
        {
            return;
        }

        icons.Add(buddy.RectangleUrl);
        icons.Add(ShareImageCanvas.ZzzRarityIcon(buddy.Rarity));
    }


    private static float MeasureV1(ShiyuDefenseInfo info)
    {
        float y = 22f + 8f + 96f;
        int floors = info.AllFloorDetail?.Count ?? 0;
        y += 12f + floors * (FloorCardHeight() + 12f);
        return y;
    }


    private static float FloorCardHeight() => 16f + 40f + 8f + 94f + 20f + 16f;


    private static float MeasureV2(ShiyuDefenseInfoV2 info)
    {
        float y = 22f;
        int fifth = info.FifthLayerDetail?.LayerChallenges?.Count ?? 0;
        y += 12f + 88f + fifth * (208f + 12f);
        if (info.HasFourthLayerDetail)
        {
            y += 16f + 72f + 110f;
        }

        return y;
    }


    private static void DrawV1(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, ShiyuDefenseInfo info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(info.BeginTime, info.EndTime), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        y += 22f;

        const float summaryH = 96f;
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, summaryH);
        float col = ContentWidth / 3f;
        DrawRatingCount(ds, ctx, x + col * 0.5f - 40f, y + 12f, "S", info.RatingSTimes);
        DrawRatingCount(ds, ctx, x + col * 1.5f - 40f, y + 12f, "A", info.RatingATimes);
        DrawRatingCount(ds, ctx, x + col * 2.5f - 40f, y + 12f, "B", info.RatingBTimes);
        ShareImageCanvas.DrawText(
            ds,
            $"{Lang.ShiyuDefensePage_HighestStageCleared}  {info.MaxLayer}",
            x + 24f,
            y + 64f,
            ctx.Body,
            ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawTextRight(
            ds,
            $"{Lang.ShiyuDefensePage_47LayerClearTime}  {ShiyuDefensePage.PerformancesTime(info.BattleTime47)}",
            x + ContentWidth - 24f,
            y + 64f,
            ctx.Body,
            ShareImageCanvas.PrimaryText);
        y += summaryH + 12f;

        if (info.AllFloorDetail is null)
        {
            return;
        }

        foreach (ShiyuDefenseFloorDetail floor in info.AllFloorDetail)
        {
            y += DrawFloor(ds, bg, ctx, x, y, floor) + 12f;
        }
    }


    private static void DrawRatingCount(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, string rating, int times)
    {
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.ZzzRatingLargeIcon(rating)), new Rect(x, y, 40f, 40f));
        ShareImageCanvas.DrawText(ds, $"x  {times}", x + 48f, y + 10f, ctx.Title, ShareImageCanvas.PrimaryText);
    }


    private static float DrawFloor(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, float x, float y, ShiyuDefenseFloorDetail floor)
    {
        float height = FloorCardHeight();
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, height);
        float innerX = x + 20f;
        float innerY = y + 8f;
        ShareImageCanvas.DrawText(ds, floor.ZoneName, innerX, innerY, ctx.Title, ShareImageCanvas.SecondaryText, 420f);
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.ZzzRarityIcon(floor.Rating)), new Rect(x + ContentWidth - 44f, innerY, 24f, 24f));
        ShareImageCanvas.DrawTextRight(
            ds,
            floor.FloorChallengeTime.ToString("yyyy-MM-dd HH:mm:ss"),
            x + ContentWidth - 20f,
            innerY + 26f,
            ctx.Small,
            ShareImageCanvas.SecondaryText);

        innerY += 48f;
        ShareImageCanvas.DrawFadeSeparator(ds, innerX, x + ContentWidth - 20f, innerY);
        innerY += 8f;

        float mid = x + ContentWidth / 2f;
        DrawNodeTeam(ds, ctx, innerX, innerY, floor.Node1, 1);
        DrawNodeTeam(ds, ctx, mid + 12f, innerY, floor.Node2, 2);
        return height;
    }


    private static void DrawNodeTeam(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, ShiyuDefenseNode? node, int index)
    {
        ShareImageCanvas.DrawText(ds, index.ToString(), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        if (node is null)
        {
            return;
        }

        float teamY = y + 18f;
        float teamX = x;
        if (node.Avatars is not null)
        {
            foreach (ZZZAvatar avatar in node.Avatars)
            {
                ShareImageCanvas.DrawZzzAvatar(ds, ctx, teamX, teamY, avatar.Rarity, avatar.RoleSquareUrl, avatar.Level, avatar.Rank);
                teamX += AvatarWidth + AvatarGap;
            }
        }

        if (node.Buddy is not null)
        {
            ShareImageCanvas.DrawZzzBuddy(ds, ctx, teamX, teamY + 16f, node.Buddy.Rarity, node.Buddy.RectangleUrl, node.Buddy.Level);
        }

        if (node.BattleTime > 0)
        {
            ShareImageCanvas.DrawText(
                ds,
                $"{Lang.ShiyuDefensePage_ClearTime}  {ShiyuDefensePage.PerformancesTime(node.BattleTime)}",
                x,
                teamY + 96f,
                ctx.Small,
                ShareImageCanvas.SecondaryText);
        }
    }


    private static void DrawV2(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, ShiyuDefenseInfoV2 info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(info.BeginTime, info.EndTime), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        y += 22f;

        var fifth = info.FifthLayerDetail?.LayerChallenges ?? [];
        float fifthH = 88f + fifth.Count * 220f;
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, fifthH);
        float innerX = x + 16f;
        float innerY = y + 12f;
        ShareImageCanvas.DrawText(ds, Lang.ShiyuDefensePage_CriticalNodeFifthFrontier, innerX, innerY, ctx.Title, ShareImageCanvas.PrimaryText);
        innerY += 24f;
        if (info.Brief is not null)
        {
            ShareImageCanvas.DrawText(ds, info.Brief.Score.ToString(), innerX, innerY, ctx.Score, ShareImageCanvas.PrimaryText);
            float scoreW = ShareImageCanvas.MeasureTextWidth(ds, info.Brief.Score.ToString(), ctx.Score);
            ShareImageCanvas.DrawText(
                ds,
                ShiyuDefensePage.RankPercentText(info.Brief.RankPercent),
                innerX + scoreW + 12f,
                innerY + 12f,
                ctx.Body,
                ShareImageCanvas.SecondaryText);
            ShareImageCanvas.DrawImage(
                ds,
                ctx.Icons.Get(ShareImageCanvas.ZzzRarityIcon(info.Brief.Rating)),
                new Rect(x + ContentWidth - 60f, y + 12f, 44f, 44f));
            if (info.Brief.BattleTime > 0)
            {
                ShareImageCanvas.DrawTextRight(
                    ds,
                    $"{Lang.ShiyuDefensePage_ClearTime}  {ShiyuDefensePage.PerformancesTime(info.Brief.BattleTime)}",
                    x + ContentWidth - 16f,
                    y + 60f,
                    ctx.Small,
                    ShareImageCanvas.SecondaryText);
            }
        }

        innerY = y + 88f;
        foreach (ShiyuDefenseV2FifthLayerChallengeInfo challenge in fifth)
        {
            innerY += DrawFifthChallenge(ds, ctx, innerX, innerY, ContentWidth - 32f, challenge) + 12f;
        }

        y += fifthH + 16f;
        if (!info.HasFourthLayerDetail || info.FourthLayerDetail is null)
        {
            return;
        }

        var fourth = info.FourthLayerDetail.LayerChallenges ?? [];
        float fourthH = 72f + 110f;
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, fourthH);
        ShareImageCanvas.DrawText(ds, Lang.ShiyuDefensePage_CriticalNodeFourthFrontier, x + 16f, y + 12f, ctx.Title, ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawImage(
            ds,
            ctx.Icons.Get(ShareImageCanvas.ZzzRarityIcon(info.FourthLayerDetail.Rating)),
            new Rect(x + ContentWidth - 48f, y + 16f, 32f, 32f));
        DateTime fourthTime = info.Brief?.ChallengeTime ?? info.FourthLayerDetail.ChallengeTime;
        if (fourthTime != DateTime.MinValue)
        {
            ShareImageCanvas.DrawText(ds, fourthTime.ToString("yyyy-MM-dd HH:mm:ss"), x + 16f, y + 36f, ctx.Small, ShareImageCanvas.SecondaryText);
        }

        float teamX = x + 16f;
        float teamY = y + 60f;
        foreach (ShiyuDefenseV2FourthLayerChallengeInfo challenge in fourth)
        {
            DrawAvatarRow(ds, ctx, teamX, teamY, challenge.AvatarList, challenge.Buddy, challenge.BattleTime);
            teamX += 340f;
        }
    }


    private static float DrawFifthChallenge(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float x,
        float y,
        float width,
        ShiyuDefenseV2FifthLayerChallengeInfo challenge)
    {
        const float height = 208f;
        ds.DrawRoundedRectangle(x, y, width, height, 8f, 8f, Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 1f);
        ShareImageCanvas.DrawUniformImage(ds, ctx.Icons.Get(challenge.MonsterPic), new Rect(x + width - 220f, y + 8f, 210f, height - 16f));
        ShareImageCanvas.DrawText(ds, challenge.Score.ToString(), x + 16f, y + 12f, ctx.Score, ShareImageCanvas.PrimaryText);
        float scoreW = ShareImageCanvas.MeasureTextWidth(ds, challenge.Score.ToString(), ctx.Score);
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.ZzzRarityIcon(challenge.Rating)), new Rect(x + 16f + scoreW + 8f, y + 18f, 24f, 24f));
        if (challenge.BattleTime > 0)
        {
            ShareImageCanvas.DrawText(
                ds,
                $"{Lang.ShiyuDefensePage_ClearTime}  {ShiyuDefensePage.PerformancesTime(challenge.BattleTime)}",
                x + 16f,
                y + 48f,
                ctx.Small,
                ShareImageCanvas.SecondaryText);
        }

        if (challenge.ChallengeTime != DateTime.MinValue)
        {
            ShareImageCanvas.DrawText(
                ds,
                $"{Lang.ClearedAt}  {challenge.ChallengeTime:yyyy-MM-dd HH:mm:ss}",
                x + 16f,
                y + 66f,
                ctx.Small,
                ShareImageCanvas.SecondaryText);
        }

        DrawAvatarRow(ds, ctx, x + 16f, y + 90f, challenge.AvatarList, challenge.Buddy, 0);
        return height;
    }


    private static void DrawAvatarRow(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float x,
        float y,
        List<ZZZAvatar>? avatars,
        ZZZBuddy? buddy,
        int battleTime)
    {
        float teamX = x;
        if (avatars is not null)
        {
            foreach (ZZZAvatar avatar in avatars)
            {
                ShareImageCanvas.DrawZzzAvatar(ds, ctx, teamX, y, avatar.Rarity, avatar.RoleSquareUrl, avatar.Level, avatar.Rank);
                teamX += AvatarWidth + AvatarGap;
            }
        }

        if (buddy is not null)
        {
            ShareImageCanvas.DrawZzzBuddy(ds, ctx, teamX, y + 16f, buddy.Rarity, buddy.RectangleUrl, buddy.Level);
        }

        if (battleTime > 0)
        {
            ShareImageCanvas.DrawText(
                ds,
                $"{Lang.ShiyuDefensePage_ClearTime}  {ShiyuDefensePage.PerformancesTime(battleTime)}",
                x,
                y - 18f,
                ctx.Small,
                ShareImageCanvas.SecondaryText);
        }
    }

}
