using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Features.Database;
using Starward.Helpers;
using System;
using System.Threading.Tasks;


namespace Starward.Features.PlayTime;

[INotifyPropertyChanged]
public sealed partial class PlayTimeButton : UserControl
{


    public GameBiz CurrentGameBiz { get; set; }


    private readonly ILogger<PlayTimeButton> _logger = AppConfig.GetLogger<PlayTimeButton>();


    private readonly PlayTimeStatsService _playTimeStatsService = AppConfig.GetService<PlayTimeStatsService>();



    public PlayTimeButton()
    {
        this.InitializeComponent();
    }



    public TimeSpan PlayTimeTotal { get; set => SetProperty(ref field, value); }



    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        InitializePlayTime();
    }



    private void InitializePlayTime()
    {
        try
        {
            PlayTimeTotal = DatabaseService.GetValue<TimeSpan>(PlayTimeStatsService.TotalPlayTimeKey(CurrentGameBiz), out _);
            if (PlayTimeTotal == TimeSpan.Zero)
            {
                // 缓存里没有归一化后的键（B 服合并到官服前的旧数据、或从未打开过统计对话框），
                // 直接现算一次并写回缓存，否则按钮会一直显示 0h 0m。
                _ = RecalculatePlayTimeTotalAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize play time");
        }
    }



    /// <summary>
    /// 在后台线程重算总时长并写回缓存；属性赋值回到 UI 线程，避免绑定更新跨线程。
    /// </summary>
    private async Task RecalculatePlayTimeTotalAsync()
    {
        try
        {
            GameBiz biz = CurrentGameBiz;
            TimeSpan total = await Task.Run(() =>
            {
                TimeSpan value = _playTimeStatsService.GetPlayTimeTotal(biz);
                if (value > TimeSpan.Zero)
                {
                    DatabaseService.SetValue(PlayTimeStatsService.TotalPlayTimeKey(biz), value);
                }
                return value;
            });
            if (biz == CurrentGameBiz)
            {
                PlayTimeTotal = total;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recalculate play time total");
        }
    }



    public static string TimeSpanToString(TimeSpan timeSpan)
    {
        int totalMinutes = (int)Math.Round(timeSpan.TotalMinutes);
        int hours = totalMinutes / 60, minutes = totalMinutes % 60;
        return $"{hours}h {minutes}m";
    }


    [RelayCommand]
    private async Task OpenStatsDialogAsync()
    {
        try
        {
            await new PlayTimeStatsDialog
            {
                CurrentGameBiz = CurrentGameBiz,
                XamlRoot = this.XamlRoot,
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            // 对话框构造/加载失败不应该让整个应用崩掉
            _logger.LogError(ex, "Open play time stats dialog: GameBiz {biz}", CurrentGameBiz);
            InAppToast.MainWindow?.Error(ex);
        }
        InitializePlayTime();
    }


}
