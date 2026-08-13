using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Language;
using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Starward.Features.Background;

/// <summary>
/// 持久化的绝区零好感壁纸元数据。
/// </summary>
public sealed class FavorWallpaperRecord
{

    [JsonPropertyName("contentId")]
    public int ContentId { get; set; }


    [JsonPropertyName("title")]
    public string Title { get; set; } = "";


    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = "";


    [JsonPropertyName("videoUrl")]
    public string VideoUrl { get; set; } = "";


    [JsonPropertyName("coverUrl")]
    public string CoverUrl { get; set; } = "";


    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

}


/// <summary>
/// 加载进度（词条拉取）。
/// </summary>
/// <param name="Done">已完成条数。</param>
/// <param name="Total">总条数。</param>
/// <param name="FromCache">是否直接命中本地缓存。</param>
public readonly record struct FavorWallpaperLoadProgress(int Done, int Total, bool FromCache);


/// <summary>
/// 画廊卡片绑定用视图模型。
/// </summary>
public partial class FavorWallpaperView : ObservableObject
{

    public FavorWallpaperRecord Record { get; set; } = new();


    public int ContentId => Record.ContentId;

    public string Title => Record.Title;

    public string CharacterName => Record.CharacterName;

    public string VideoUrl => Record.VideoUrl;

    public string CoverUrl => string.IsNullOrWhiteSpace(Record.CoverUrl) ? (Record.IconUrl ?? "") : Record.CoverUrl;


    /// <summary>是否已下载到本地 bg 目录。</summary>
    [ObservableProperty]
    private bool isDownloaded;


    /// <summary>是否为当前自定义背景。</summary>
    [ObservableProperty]
    private bool isInUse;


    /// <summary>是否正在下载。</summary>
    [ObservableProperty]
    private bool isDownloading;


    /// <summary>下载进度 0–100。</summary>
    [ObservableProperty]
    private double downloadProgress;


    /// <summary>未下载且未在下载时显示下载 Lottie。</summary>
    public bool ShowDownloadButton => !IsDownloaded && !IsDownloading;


    /// <summary>已下载且未在下载时显示删除 Lottie。</summary>
    public bool ShowLocalActions => IsDownloaded && !IsDownloading;


    /// <summary>下载中隐藏操作按钮，只留进度条。</summary>
    public bool ShowActionButton => !IsDownloading;


    /// <summary>操作按钮悬停说明：未下载为下载，已下载为删除。</summary>
    public string ActionTooltip => IsDownloaded ? Lang.Common_Delete : Lang.Common_Download;


    partial void OnIsDownloadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowLocalActions));
        OnPropertyChanged(nameof(ShowActionButton));
        OnPropertyChanged(nameof(ActionTooltip));
    }


    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowLocalActions));
        OnPropertyChanged(nameof(ShowActionButton));
    }


    /// <summary>由画廊面板注入：下载到本地缓存。</summary>
    public Func<FavorWallpaperView, Task>? DownloadAction { get; set; }


    /// <summary>由画廊面板注入：删除本地缓存。</summary>
    public Func<FavorWallpaperView, Task>? DeleteAction { get; set; }


    /// <summary>由画廊面板注入：设为当前背景。</summary>
    public Func<FavorWallpaperView, Task>? UseAction { get; set; }

}
