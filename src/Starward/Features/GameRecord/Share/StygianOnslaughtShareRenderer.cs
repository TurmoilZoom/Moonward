using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.Genshin.StygianOnslaught;
using Starward.Features.GameRecord.Genshin;
using Starward.Language;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 原神幽境危战/危局强袭战分享图：最佳纪录 + 当前单人/协战阵容与 Boss。
/// </summary>
internal static class StygianOnslaughtShareRenderer
{

    private const float CanvasWidth = 760f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
    private const float AvatarWidth = 80f;
    private const float AvatarHeight = 97f;
    private const float AvatarGap = 12f;
    private const float ChallengeHeight = 268f;


    public static async Task<string> RenderAndSaveAsync(
        StygianOnslaughtInfo info,
        StygianOnslaughtBattle battle,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(battle);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        CollectIcons(info, battle, ctx.Icons);
        await ctx.Icons.LoadAllAsync(cancellationToken);
        float contentHeight = Measure(battle);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "stygian_onslaught",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, info, battle),
            cancellationToken);
    }


    private static void CollectIcons(StygianOnslaughtInfo info, StygianOnslaughtBattle battle, ShareImageIconCache icons)
    {
        int difficulty = battle.Best?.Difficulty ?? info.Difficulty;
        icons.Add(ShareImageCanvas.StygianDifficultyMedal(difficulty));
        if (battle.Challenge is null)
        {
            return;
        }

        foreach (StygianOnslaughtChallenge challenge in battle.Challenge)
        {
            if (challenge.Teams is not null)
            {
                foreach (StygianOnslaughtAvatar avatar in challenge.Teams)
                {
                    icons.Add(ShareImageCanvas.GenshinRarityBg(avatar.Rarity));
                    icons.Add(avatar.Image);
                }
            }

            if (challenge.Monster is not null)
            {
                icons.Add(challenge.Monster.Icon);
            }

            if (challenge.BestAvatar is not null)
            {
                foreach (StygianOnslaughtBestAvatar best in challenge.BestAvatar)
                {
                    icons.Add(best.SideIcon);
                }
            }
        }
    }


    private static float Measure(StygianOnslaughtBattle battle)
    {
        int count = battle.Challenge?.Count ?? 0;
        return 22f + 28f + 8f + count * (ChallengeHeight + 20f);
    }


    private static void Draw(
        CanvasDrawingSession ds,
        CanvasBitmap bg,
        ShareImageContext ctx,
        StygianOnslaughtInfo info,
        StygianOnslaughtBattle battle)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        DateTime start = info.Schedule?.StartDateTime ?? info.StartDateTime;
        DateTime end = info.Schedule?.EndDateTime ?? info.EndDateTime;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(start, end), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        y += 22f;

        ShareImageCanvas.DrawText(ds, Lang.ImaginariumTheaterPage_BestRecord, x, y + 4f, ctx.Title, ShareImageCanvas.SecondaryText);
        float medalX = x + ShareImageCanvas.MeasureTextWidth(ds, Lang.ImaginariumTheaterPage_BestRecord, ctx.Title) + 8f;
        int difficulty = battle.Best?.Difficulty ?? info.Difficulty;
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StygianDifficultyMedal(difficulty)), new Rect(medalX, y, 24f, 24f));
        int seconds = battle.Best?.Seconds ?? info.Second;
        ShareImageCanvas.DrawText(ds, $"{seconds}s", medalX + 28f, y + 2f, ctx.Title, ShareImageCanvas.PrimaryText);

        string mode = ReferenceEquals(battle, info.MultiPlayer) ? Lang.StygianOnslaughtPage_CoOp : Lang.StygianOnslaughtPage_SinglePlayer;
        ShareImageCanvas.DrawTextRight(ds, mode, x + ContentWidth, y + 2f, ctx.Title, ShareImageCanvas.SecondaryText);
        y += 36f;

        if (battle.Challenge is null)
        {
            return;
        }

        foreach (StygianOnslaughtChallenge challenge in battle.Challenge)
        {
            y += DrawChallenge(ds, bg, ctx, x, y, challenge) + 20f;
        }
    }


    private static float DrawChallenge(
        CanvasDrawingSession ds,
        CanvasBitmap bg,
        ShareImageContext ctx,
        float x,
        float y,
        StygianOnslaughtChallenge challenge)
    {
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, ChallengeHeight);
        float innerX = x + 24f;
        float innerY = y + 12f;
        ShareImageCanvas.DrawText(ds, challenge.Name, innerX, innerY, ctx.Title, ShareImageCanvas.PrimaryText, 340f);
        innerY += 26f;
        ShareImageCanvas.DrawText(ds, Lang.StygianOnslaughtPage_BattleTime, innerX, innerY, ctx.Body, ShareImageCanvas.SecondaryText);
        ShareImageCanvas.DrawText(
            ds,
            $"{challenge.Second}s",
            innerX + ShareImageCanvas.MeasureTextWidth(ds, Lang.StygianOnslaughtPage_BattleTime, ctx.Body) + 12f,
            innerY,
            ctx.Body,
            ShareImageCanvas.SecondaryText);

        innerY += 24f;
        float teamX = innerX;
        if (challenge.Teams is not null)
        {
            foreach (StygianOnslaughtAvatar avatar in challenge.Teams)
            {
                ShareImageCanvas.DrawGenshinAvatar(ds, ctx, teamX, innerY, AvatarWidth, AvatarHeight, avatar.Rarity, avatar.Image, avatar.Level, avatar.Rank);
                teamX += AvatarWidth + AvatarGap;
            }
        }

        if (challenge.Monster is not null)
        {
            ShareImageCanvas.DrawUniformImage(
                ds,
                ctx.Icons.Get(challenge.Monster.Icon),
                new Rect(x + ContentWidth - 280f, y + 8f, 250f, 220f));
            string lv = $"Lv.{challenge.Monster.Level}";
            float lvW = ShareImageCanvas.MeasureTextWidth(ds, lv, ctx.Small) + 16f;
            ds.FillRoundedRectangle(x + ContentWidth - 280f, y + ChallengeHeight - 56f, lvW, 20f, 8f, 8f, Color.FromArgb(0xA0, 0x20, 0x20, 0x20));
            ShareImageCanvas.DrawText(ds, lv, x + ContentWidth - 272f, y + ChallengeHeight - 56f, ctx.Small, ShareImageCanvas.SecondaryText);
        }

        float bestY = y + ChallengeHeight - 40f;
        float bestX = innerX;
        if (challenge.BestAvatar is not null)
        {
            foreach (StygianOnslaughtBestAvatar best in challenge.BestAvatar)
            {
                ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(best.SideIcon), new Rect(bestX, bestY - 8f, 40f, 40f));
                string label = StygianOnslaughtPage.BestTypeToString(best.Type);
                ShareImageCanvas.DrawText(ds, label, bestX + 44f, bestY + 8f, ctx.Small, ShareImageCanvas.SecondaryText);
                float labelW = ShareImageCanvas.MeasureTextWidth(ds, label, ctx.Small);
                ShareImageCanvas.DrawText(ds, best.DPS.ToString(), bestX + 52f + labelW, bestY + 8f, ctx.Body, ShareImageCanvas.PrimaryText);
                bestX += 220f;
            }
        }

        return ChallengeHeight;
    }

}
