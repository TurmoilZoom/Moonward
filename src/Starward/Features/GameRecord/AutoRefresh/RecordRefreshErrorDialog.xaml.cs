using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Features.GameSelector;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Starward.Features.GameRecord.AutoRefresh;

/// <summary>
/// 自动更新战绩跳过的异常记录。后台不做任何自愈，这里是用户看到「哪个号的哪个板块出了什么事」的唯一入口。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class RecordRefreshErrorDialog : ContentDialog
{

    private readonly ILogger<RecordRefreshErrorDialog> _logger = AppConfig.GetLogger<RecordRefreshErrorDialog>();

    private readonly AutoRecordRefreshService _service = AppConfig.GetService<AutoRecordRefreshService>();


    public RecordRefreshErrorDialog()
    {
        LoadErrors();
        InitializeComponent();
    }


    /// <summary>异常记录，最新的在前。</summary>
    public ObservableCollection<RecordRefreshErrorItem> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    public bool HasNoErrors => Errors.Count == 0;


    /// <summary>
    /// 读取异常记录，并把区服换成图标与游戏名。
    /// </summary>
    private void LoadErrors()
    {
        try
        {
            Errors.Clear();
            foreach (RecordRefreshError error in _service.GetErrors())
            {
                Errors.Add(new RecordRefreshErrorItem(error));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load auto record refresh errors.");
        }
    }


    private void Button_ClearErrors_Click(object sender, RoutedEventArgs e)
    {
        _service.ClearErrors();
        Errors.Clear();
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasNoErrors));
    }


    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }

}


/// <summary>
/// 异常记录列表项：把存库的原始记录换成可直接显示的图标与三行文本。
/// </summary>
public sealed class RecordRefreshErrorItem
{

    /// <summary>
    /// 用一条存库的异常记录构造列表项。
    /// </summary>
    /// <param name="error">异常记录。</param>
    public RecordRefreshErrorItem(RecordRefreshError error)
    {
        GameBiz biz = error.GameBiz;
        var icon = new GameBizIcon(biz);
        GameIcon = icon.GameIcon;
        Title = error.Item.GetDisplayName(error.MonthTarget);
        Account = $"{icon.GameName} · {icon.ServerName} · {error.Nickname} · {error.Uid}";
        string time = error.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentUICulture);
        // 服务端原文不本地化：出问题时原始返回码与文案比翻译过的更有用
        string code = error.ReturnCode == 0 ? string.Empty : $"[{error.ReturnCode}] ";
        Detail = $"{time}  {code}{error.Message}";
    }

    /// <summary>游戏图标。</summary>
    public string? GameIcon { get; set; }

    /// <summary>出错的任务名（月报类带「· 当月 / · 上月」后缀）。</summary>
    public string Title { get; set; }

    /// <summary>游戏 · 区服 · 昵称 · uid。</summary>
    public string Account { get; set; }

    /// <summary>时间 + 返回码 + 服务端原文。</summary>
    public string Detail { get; set; }

}
