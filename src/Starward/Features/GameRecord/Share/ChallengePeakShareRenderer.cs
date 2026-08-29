using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.StarRail.ChallengePeak;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.GameRecord.Share;

/// <summary>
/// 逐光捡金分享图：王棋关 + 骑士关，对齐 ChallengePeakPage。
/// </summary>
internal static class ChallengePeakShareRenderer
{

    private const float CanvasWidth = 760f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;


    public static async Task<string> RenderAndSaveAsync(
        ChallengePeakData data,
        ChallengePeakRecord record,
        long uid,
        string? backgroundFile,
        Color accentColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(record);
        using var ctx = ShareImageCanvas.CreateContext(accentColor);
        CollectIcons(record, ctx.Icons);
        await ctx.Icons.LoadAllAsync(cancellationToken);
        float contentHeight = Measure(record);
        return await ShareImageCanvas.ComposeAndSaveAsync(
            CanvasWidth,
            contentHeight,
            "challenge_peak",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, data, record),
            cancellationToken);
    }


    private static void CollectIcons(ChallengePeakRecord record, ShareImageIconCache icons)
    {
        icons.Add(ShareImageCanvas.StarIconStarRail);
        icons.Add(ShareImageCanvas.StarIconChallengePeakBoss);
        icons.Add(ShareImageCanvas.StarIconChallengePeakMob);
        if (record.Group is not null)
        {
            icons.Add(record.Group.ThemePicPath);
        }

        if (record.BossInfo is not null)
        {
            icons.Add(record.BossInfo.Icon);
        }

        if (record.BossRecord is not null)
        {
            CollectAvatars(record.BossRecord.Avatars, icons);
            if (record.BossRecord.Buff is not null)
            {
                icons.Add(record.BossRecord.Buff.Icon);
            }
        }

        if (record.Mobs is null)
        {
            return;
        }

        foreach (ChallengePeakRecordMob mob in record.Mobs)
        {
            icons.Add(mob.MobInfo?.MonsterIcon);
            if (mob.MobRecord?.Avatars is not null)
            {
                CollectAvatars(mob.MobRecord.Avatars, icons);
            }
        }
    }


    private static void CollectAvatars(List<ChallengePeakAvatar>? avatars, ShareImageIconCache icons)
    {
        if (avatars is null)
        {
            return;
        }

        foreach (ChallengePeakAvatar avatar in avatars)
        {
            icons.Add(ShareImageCanvas.StarRailRarityBg(avatar.Rarity));
            icons.Add(avatar.Icon);
        }
    }


    private static float Measure(ChallengePeakRecord record)
    {
        int mobs = record.Mobs?.Count ?? 0;
        return 22f + 72f + 28f + 192f + 28f + Math.Max(mobs, 1) * 200f;
    }


    private static void Draw(
        CanvasDrawingSession ds,
        CanvasBitmap bg,
        ShareImageContext ctx,
        ChallengePeakData data,
        ChallengePeakRecord record)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        if (record.Group is not null)
        {
            ShareImageCanvas.DrawText(
                ds,
                ShareImageCanvas.FormatPeriod(record.Group.BeginTime, record.Group.EndTime),
                x,
                y,
                ctx.Small,
                ShareImageCanvas.TertiaryText);
        }

        y += 22f;
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, 64f);
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(record.Group?.ThemePicPath), new Rect(x + 16f, y + 8f, 48f, 48f));
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StarIconChallengePeakBoss), new Rect(x + 80f, y + 10f, 20f, 20f));
        ShareImageCanvas.DrawText(ds, $"x {record.BossStars}", x + 104f, y + 10f, ctx.Small, ShareImageCanvas.SecondaryText);
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.StarIconChallengePeakMob), new Rect(x + 80f, y + 34f, 20f, 20f));
        ShareImageCanvas.DrawText(ds, $"x {record.MobStars}", x + 104f, y + 34f, ctx.Small, ShareImageCanvas.SecondaryText);
        ShareImageCanvas.DrawText(ds, record.Group?.NameMi18n, x + ContentWidth * 0.45f, y + 12f, ctx.Title, ShareImageCanvas.PrimaryText, 280f);
        ShareImageCanvas.DrawText(
            ds,
            $"{Lang.ChallengePeakPage_ChallengeAttempts}  {data.ChallengePeakBestRecordBrief?.TotalBattleNum ?? 0}",
            x + ContentWidth * 0.45f,
            y + 36f,
            ctx.Body,
            ShareImageCanvas.PrimaryText);
        y += 80f;

        ShareImageCanvas.DrawText(ds, Lang.ChallengePeakPage_KingInCheckStageRecords, x, y, ctx.Body, ShareImageCanvas.PrimaryText);
        y += 24f;
        DrawBossCard(ds, bg, ctx, x, y, record);
        y += 204f;
        ShareImageCanvas.DrawText(ds, Lang.ChallengePeakPage_KnightStageRecords, x, y, ctx.Body, ShareImageCanvas.PrimaryText);
        y += 24f;

        if (record.Mobs is null)
        {
            return;
        }

        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, record.Mobs.Count * 200f);
        float mobY = y;
        foreach (ChallengePeakRecordMob mob in record.Mobs)
        {
            DrawMob(ds, ctx, x, mobY, mob);
            mobY += 200f;
        }
    }


    private static void DrawBossCard(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, float x, float y, ChallengePeakRecord record)
    {
        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, 192f);
        ShareImageCanvas.DrawUniformImage(ds, ctx.Icons.Get(record.BossInfo?.Icon), new Rect(x + ContentWidth - 240f, y + 8f, 230f, 176f));
        ShareImageCanvas.DrawText(ds, record.BossInfo?.NameMi18n, x + 20f, y + 12f, ctx.Title, ShareImageCanvas.PrimaryText, 400f);
        if (record.BossRecord is null)
        {
            ShareImageCanvas.DrawText(ds, Lang.ChallengePeakPage_NoClearanceRecords, x + 20f, y + 44f, ctx.Body, ShareImageCanvas.SecondaryText);
            return;
        }

        ChallengePeakBossRecord boss = record.BossRecord;
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_CyclesUsed} {boss.RoundNum}", x + 20f, y + 40f, ctx.Body, ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawText(ds, Lang.ChallengePeakPage_Cleared, x + 160f, y + 40f, ctx.Body, ShareImageCanvas.SecondaryText);
        ShareImageCanvas.DrawStarRow(ds, ctx.Icons.Get(ShareImageCanvas.StarIconStarRail), x + 240f, y + 38f, record.BossStars, 20f, 2f);
        ShareImageCanvas.DrawText(ds, boss.ChallengeTime.ToString("yyyy.MM.dd HH:mm"), x + 20f, y + 64f, ctx.Small, ShareImageCanvas.SecondaryText);
        StarRailSharePrimitives.DrawAvatarRow(ds, ctx, x + 20f, y + 88f, Map(boss.Avatars));
        if (boss.Buff is not null)
        {
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(boss.Buff.Icon), new Rect(x + ContentWidth * 0.52f, y + 88f, 28f, 28f));
            ShareImageCanvas.DrawText(ds, boss.Buff.NameMi18n, x + ContentWidth * 0.52f + 36f, y + 92f, ctx.Body, ShareImageCanvas.PrimaryText, 180f);
            ShareImageCanvas.DrawText(
                ds,
                ShareImageCanvas.StripMarkup(boss.Buff.DescMi18n),
                x + ContentWidth * 0.52f,
                y + 122f,
                ctx.WrapSmall,
                ShareImageCanvas.SecondaryText,
                ContentWidth * 0.45f);
        }
    }


    private static void DrawMob(CanvasDrawingSession ds, ShareImageContext ctx, float x, float y, ChallengePeakRecordMob mob)
    {
        ShareImageCanvas.DrawUniformImage(ds, ctx.Icons.Get(mob.MobInfo?.MonsterIcon), new Rect(x + ContentWidth - 100f, y + 12f, 80f, 80f));
        ShareImageCanvas.DrawText(ds, mob.MobInfo?.MonsterName, x + 20f, y + 12f, ctx.Title, ShareImageCanvas.PrimaryText, 500f);
        if (!mob.HasChallengeRecord || mob.MobRecord is null)
        {
            ShareImageCanvas.DrawText(ds, Lang.ChallengePeakPage_NoClearanceRecords, x + 20f, y + 44f, ctx.Body, ShareImageCanvas.SecondaryText);
            return;
        }

        ChallengePeakMobRecord rec = mob.MobRecord;
        ShareImageCanvas.DrawText(ds, $"{Lang.ForgottenHallPage_CyclesUsed} {rec.RoundNum}", x + 20f, y + 40f, ctx.Body, ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawText(ds, Lang.ChallengePeakPage_Cleared, x + 160f, y + 40f, ctx.Body, ShareImageCanvas.SecondaryText);
        ShareImageCanvas.DrawStarRow(ds, ctx.Icons.Get(ShareImageCanvas.StarIconStarRail), x + 240f, y + 38f, rec.StarNum, 20f, 2f);
        ShareImageCanvas.DrawText(ds, rec.ChallengeTime.ToString("yyyy.MM.dd HH:mm"), x + 20f, y + 64f, ctx.Small, ShareImageCanvas.SecondaryText);
        StarRailSharePrimitives.DrawAvatarRow(ds, ctx, x + 20f, y + 88f, Map(rec.Avatars));
    }


    private static IEnumerable<StarRailAvatarDraw>? Map(List<ChallengePeakAvatar>? avatars)
    {
        if (avatars is null)
        {
            return null;
        }

        var list = new List<StarRailAvatarDraw>(avatars.Count);
        foreach (ChallengePeakAvatar avatar in avatars)
        {
            list.Add(new StarRailAvatarDraw(avatar.Rarity, avatar.Icon, avatar.Level, avatar.Rank));
        }

        return list;
    }

}
