using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace Starward.Features.TimeNode;

/// <summary>
/// Flyout 绑定用的条目视图模型；倒计时文案由 timer 在 UI 线程刷新。
/// </summary>
public partial class TimeNodeItemView : ObservableObject
{

    public string Title { get; set; } = "";

    public string? Subtitle { get; set; }

    public string? LinkUrl { get; set; }

    public string? CoverIcon { get; set; }

    public bool HasCover => !string.IsNullOrWhiteSpace(CoverIcon);

    public bool HasIcons => Icons is { Count: > 0 };

    public bool HasLink => !string.IsNullOrWhiteSpace(LinkUrl);

    public TimeNodeCountdownKind CountdownKind { get; set; }

    public string? ContentBeforeAct { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset EndTime { get; set; }

    public IReadOnlyList<TimeNodeIcon> Icons { get; set; } = [];


    /// <summary>
    /// 动态倒计时文案。
    /// </summary>
    [ObservableProperty]
    private string countdownText = "";


    /// <summary>
    /// 按当前时刻刷新 <see cref="CountdownText"/>（须在 UI 线程调用）。
    /// </summary>
    /// <param name="now">当前时刻。</param>
    public void RefreshCountdown(DateTimeOffset now)
    {
        CountdownText = TimeNodeTimeHelper.FormatCountdown(CountdownKind, StartTime, EndTime, ContentBeforeAct, now);
    }


    internal static TimeNodeItemView FromModel(TimeNodeItem item)
    {
        var view = new TimeNodeItemView
        {
            Title = item.Title,
            Subtitle = item.Subtitle,
            LinkUrl = item.LinkUrl,
            CoverIcon = item.CoverIcon,
            CountdownKind = item.CountdownKind,
            ContentBeforeAct = item.ContentBeforeAct,
            StartTime = item.StartTime,
            EndTime = item.EndTime,
            Icons = item.Icons,
        };
        view.RefreshCountdown(DateTimeOffset.Now);
        return view;
    }

}


/// <summary>
/// 分段视图模型。
/// </summary>
public sealed class TimeNodeSectionView
{

    public string Title { get; set; } = "";

    public IReadOnlyList<TimeNodeItemView> Items { get; set; } = [];

}
