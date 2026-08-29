using Microsoft.Graphics.Canvas;
using Starward.Core.GameRecord.Genshin.ImaginariumTheater;
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
/// 幻想真境剧诗分享图：演出回顾、战斗统计、各幕阵容。
/// </summary>
internal static class ImaginariumTheaterShareRenderer
{

    private const float CanvasWidth = 800f;
    private const float ContentWidth = CanvasWidth - ShareImageCanvas.OuterMargin * 2;
    private const float AvatarW = 80f;
    private const float AvatarH = 97f;
    private const float AvatarGap = 12f;
    private const float PeriodLine = 22f;
    private const float TitleLine = 26f;
    private const float StatsCardHeight = 172f;
    private const float SectionGap = 16f;
    private const float RoundGap = 12f;
    private const float RoundHeader = 28f;
    private const float RoundBottomPad = 24f;
    private const float BlessingTitle = 20f;
    private const float BlessingWrapInset = 32f;


    public static async Task<string> RenderAndSaveAsync(
        ImaginariumTheaterInfo info,
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
            "imaginarium_theater",
            uid,
            backgroundFile,
            ctx,
            (ds, bg) => Draw(ds, bg, ctx, info),
            cancellationToken);
    }


    private static void CollectIcons(ImaginariumTheaterInfo info, ShareImageIconCache icons)
    {
        icons.Add(ShareImageCanvas.TheaterHeraldry(info.Stat?.Heraldry ?? info.Heraldry));
        icons.Add(ShareImageCanvas.StarIconGenshinTheaterOn);
        icons.Add(ShareImageCanvas.StarIconGenshinTheaterOff);
        icons.Add(ShareImageCanvas.FlowerIconTheater);
        CollectStatAvatar(info.Detail?.FightStatisic?.MaxDamageAvatar, icons);
        CollectStatAvatar(info.Detail?.FightStatisic?.MaxDefeatAvatar, icons);
        CollectStatAvatar(info.Detail?.FightStatisic?.MaxTakeDamageAvatar, icons);
        if (info.Detail?.FightStatisic?.ShortestAvatarList is not null)
        {
            foreach (ImaginariumTheaterFightStatisicAvatar avatar in info.Detail.FightStatisic.ShortestAvatarList)
            {
                CollectStatAvatar(avatar, icons);
            }
        }

        if (info.Detail?.RoundsData is null)
        {
            return;
        }

        foreach (ImaginariumTheaterRoundsData round in info.Detail.RoundsData)
        {
            if (round.Avatars is not null)
            {
                foreach (ImaginariumTheaterAvatar avatar in round.Avatars)
                {
                    icons.Add(ShareImageCanvas.GenshinRarityBg(avatar.Rarity));
                    icons.Add(avatar.Image);
                }
            }

            if (round.SplendourBuff?.Buffs is not null)
            {
                foreach (ImaginariumTheaterSplendourBuffItem buff in round.SplendourBuff.Buffs)
                {
                    icons.Add(buff.Icon);
                }
            }
        }
    }


    private static void CollectStatAvatar(ImaginariumTheaterFightStatisicAvatar? avatar, ShareImageIconCache icons)
    {
        if (avatar is not null)
        {
            icons.Add(avatar.AvatarIcon);
        }
    }


    /// <summary>
    /// 内容高度必须与 <see cref="Draw"/> 逐步累加一致，否则超长多幕会被画布裁掉。
    /// </summary>
    private static float Measure(ImaginariumTheaterInfo info, ShareImageContext ctx)
    {
        float y = PeriodLine + TitleLine + StatsCardHeight + SectionGap;
        if (info.HasFightStatistic)
        {
            y += StatsCardHeight + SectionGap;
        }

        if (info.Detail?.RoundsData is not { Count: > 0 })
        {
            return y;
        }

        foreach (ImaginariumTheaterRoundsData round in info.Detail.RoundsData)
        {
            y += MeasureRound(round, ctx) + RoundGap;
        }

        return y;
    }


    private static float MeasureRound(ImaginariumTheaterRoundsData round, ShareImageContext ctx)
    {
        float height = RoundHeader + AvatarH + RoundBottomPad;
        if (TryGetBlessing(round, out string blessing))
        {
            height += BlessingTitle + ShareImageCanvas.MeasureTextHeight(ctx.Device, blessing, ctx.WrapSmall, ContentWidth - BlessingWrapInset);
        }

        return height;
    }


    private static bool TryGetBlessing(ImaginariumTheaterRoundsData round, out string blessing)
    {
        blessing = string.Empty;
        if (round.SplendourBuff?.Summary is not { TotalLevel: > 0 })
        {
            return false;
        }

        blessing = ShareImageCanvas.StripMarkup(round.SplendourBuff.Summary.Desc);
        return !string.IsNullOrEmpty(blessing);
    }


    private static void Draw(CanvasDrawingSession ds, CanvasBitmap bg, ShareImageContext ctx, ImaginariumTheaterInfo info)
    {
        float x = ShareImageCanvas.OuterMargin;
        float y = ShareImageCanvas.OuterMargin;
        ShareImageCanvas.DrawText(ds, ShareImageCanvas.FormatPeriod(info.StartTime, info.EndTime), x, y, ctx.Small, ShareImageCanvas.TertiaryText);
        y += PeriodLine;
        ShareImageCanvas.DrawText(ds, Lang.ImaginariumTheaterPage_PastPerformances, x, y, ctx.Title, ShareImageCanvas.SecondaryText);
        y += TitleLine;

        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, StatsCardHeight);
        ImaginariumTheaterStat? stat = info.Stat;
        DrawStatsRow(ds, ctx, x, y, 0, Lang.ImaginariumTheaterPage_BestRecord, stripe: false, drawValue: (rowY) =>
        {
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.TheaterHeraldry(stat?.Heraldry ?? 0)), new Rect(x + ContentWidth * 0.55f, rowY - 2f, 28f, 28f));
            string mode = ImaginariumTheaterPage.DifficultyMode(stat?.DifficultyId ?? info.DifficultyId);
            string act = ImaginariumTheaterPage.ActX(stat?.MaxRoundId ?? info.MaxRoundId);
            string extra = (stat?.DifficultyId ?? info.DifficultyId) == 5
                ? $"  {Lang.ImaginariumTheaterPage_Arcana} {stat?.TarotFinishedCnt ?? 0}"
                : string.Empty;
            ShareImageCanvas.DrawText(ds, $"{mode}  {act}{extra}", x + ContentWidth * 0.55f + 36f, rowY + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
        });
        DrawStatsRow(ds, ctx, x, y, 1, Lang.ImaginariumTheaterPage_StarChallengeStellas, stripe: true, drawValue: (rowY) =>
        {
            var medals = info.Stat?.GetMedalRoundList ?? [];
            float mx = x + ContentWidth * 0.55f;
            foreach (int medal in medals)
            {
                string uri = medal == 0 ? ShareImageCanvas.StarIconGenshinTheaterOff : ShareImageCanvas.StarIconGenshinTheaterOn;
                ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(uri), new Rect(mx, rowY + 6f, 20f, 20f));
                mx += 20f;
            }
        });
        DrawStatsRow(ds, ctx, x, y, 2, Lang.ImaginariumTheaterPage_FantasiaFlowersUsed, stripe: false, drawValue: (rowY) =>
        {
            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(ShareImageCanvas.FlowerIconTheater), new Rect(x + ContentWidth * 0.55f, rowY + 2f, 24f, 24f));
            ShareImageCanvas.DrawText(ds, (stat?.CoinNum ?? 0).ToString(), x + ContentWidth * 0.55f + 32f, rowY + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
        });
        DrawStatsRow(ds, ctx, x, y, 3, Lang.ImaginariumTheaterPage_TriggeredExternalAudienceSupport, stripe: true, drawValue: (rowY) =>
        {
            ShareImageCanvas.DrawText(ds, $"{stat?.AvatarBonusNum ?? 0}  {Lang.SpiralAbyssPage_Times}", x + ContentWidth * 0.55f, rowY + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
        });
        DrawStatsRow(ds, ctx, x, y, 4, Lang.ImaginariumTheaterPage_SupportingCastCharactersAssistOtherPlayers, stripe: false, drawValue: (rowY) =>
        {
            ShareImageCanvas.DrawText(ds, $"{stat?.RentCnt ?? 0}  {Lang.SpiralAbyssPage_Times}", x + ContentWidth * 0.55f, rowY + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
        });
        y += StatsCardHeight + SectionGap;

        if (info.HasFightStatistic && info.Detail?.FightStatisic is ImaginariumTheaterFightStatisic fight)
        {
            ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, StatsCardHeight);
            DrawStatsRow(ds, ctx, x, y, 0, Lang.ImaginariumTheaterPage_TotalPerformanceDuration, false, rowY =>
            {
                ShareImageCanvas.DrawText(ds, ImaginariumTheaterPage.PerformancesTime(fight.TotalUseTime), x + ContentWidth * 0.55f, rowY + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
            });
            DrawFightAvatarRow(ds, ctx, x, y, 1, Lang.ImaginariumTheaterPage_HighestDamageDealt, fight.MaxDamageAvatar, true);
            DrawFightAvatarRow(ds, ctx, x, y, 2, Lang.ImaginariumTheaterPage_MostOpponentsDefeated, fight.MaxDefeatAvatar, false);
            DrawFightAvatarRow(ds, ctx, x, y, 3, Lang.SpiralAbyssPage_MostDamageTaken, fight.MaxTakeDamageAvatar, true);
            DrawStatsRow(ds, ctx, x, y, 4, Lang.ImaginariumTheaterPage_TeamThatCompletedThePerformanceFastest, false, rowY =>
            {
                float ax = x + ContentWidth * 0.55f;
                if (fight.ShortestAvatarList is null)
                {
                    return;
                }

                foreach (ImaginariumTheaterFightStatisicAvatar avatar in fight.ShortestAvatarList)
                {
                    ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(avatar.AvatarIcon), new Rect(ax, rowY - 4f, 44f, 36f));
                    ax += 36f;
                }
            });
            y += StatsCardHeight + SectionGap;
        }

        if (info.Detail?.RoundsData is not { Count: > 0 })
        {
            return;
        }

        foreach (ImaginariumTheaterRoundsData round in info.Detail.RoundsData)
        {
            y += DrawRound(ds, bg, ctx, x, y, round) + RoundGap;
        }
    }


    private static void DrawStatsRow(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float cardX,
        float cardY,
        int index,
        string label,
        bool stripe,
        System.Action<float> drawValue)
    {
        float rowY = cardY + 6f + index * 32f;
        if (stripe)
        {
            ds.FillRectangle(cardX, rowY, ContentWidth, 32f, Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        }

        ShareImageCanvas.DrawText(ds, label, cardX + 24f, rowY + 6f, ctx.Body, ShareImageCanvas.PrimaryText, ContentWidth * 0.5f);
        drawValue(rowY);
    }


    private static void DrawFightAvatarRow(
        CanvasDrawingSession ds,
        ShareImageContext ctx,
        float cardX,
        float cardY,
        int index,
        string label,
        ImaginariumTheaterFightStatisicAvatar? avatar,
        bool stripe)
    {
        DrawStatsRow(ds, ctx, cardX, cardY, index, label, stripe, rowY =>
        {
            if (avatar is null)
            {
                return;
            }

            ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(avatar.AvatarIcon), new Rect(cardX + ContentWidth * 0.55f, rowY - 4f, 44f, 36f));
            ShareImageCanvas.DrawText(ds, avatar.Value, cardX + ContentWidth * 0.55f + 40f, rowY + 4f, ctx.Body, ShareImageCanvas.PrimaryText);
        });
    }


    private static float DrawRound(
        CanvasDrawingSession ds,
        CanvasBitmap bg,
        ShareImageContext ctx,
        float x,
        float y,
        ImaginariumTheaterRoundsData round)
    {
        float height = MeasureRound(round, ctx);
        TryGetBlessing(round, out string blessing);

        ShareImageCanvas.DrawAcrylicCard(ds, ctx, bg, x, y, ContentWidth, height);
        string star = round.IsGetMedal ? ShareImageCanvas.StarIconGenshinTheaterOn : ShareImageCanvas.StarIconGenshinTheaterOff;
        ShareImageCanvas.DrawImage(ds, ctx.Icons.Get(star), new Rect(x + 16f, y + 8f, 24f, 24f));
        string title = round.IsTarot
            ? ImaginariumTheaterPage.ArcanaChallengeX(round.TarotSerialNo)
            : ImaginariumTheaterPage.ActX(round.RoundId);
        ShareImageCanvas.DrawText(ds, title, x + 48f, y + 10f, ctx.Title, ShareImageCanvas.PrimaryText);
        ShareImageCanvas.DrawText(ds, round.FinishDateTime.ToString("yyyy-MM-dd HH:mm:ss"), x + 200f, y + 12f, ctx.Small, ShareImageCanvas.SecondaryText);

        float ax = x + 16f;
        float ay = y + 40f;
        if (round.Avatars is not null)
        {
            foreach (ImaginariumTheaterAvatar avatar in round.Avatars)
            {
                ShareImageCanvas.DrawGenshinAvatar(ds, ctx, ax, ay, AvatarW, AvatarH, avatar.Rarity, avatar.Image, avatar.Level);
                ax += AvatarW + AvatarGap;
            }
        }

        if (!string.IsNullOrEmpty(blessing))
        {
            float by = ay + AvatarH + 8f;
            ShareImageCanvas.DrawText(ds, Lang.ImaginariumTheaterPage_BrilliantBlessing, x + 16f, by, ctx.Small, ShareImageCanvas.SecondaryText);
            ShareImageCanvas.DrawText(ds, blessing, x + 16f, by + 18f, ctx.WrapSmall, ShareImageCanvas.SecondaryText, ContentWidth - BlessingWrapInset);
        }

        return height;
    }

}
