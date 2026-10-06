using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;


namespace Starward.Features.Gacha;

[INotifyPropertyChanged]
public sealed partial class DeleteGachaLogDialog : ContentDialog
{


    private readonly ILogger<DeleteGachaLogDialog> _logger = AppConfig.GetLogger<DeleteGachaLogDialog>();


    public GameBiz CurrentGameBiz { get; set; }


    public long? DefaultUid { get; set; }


    public bool Deleted { get; set; }



    private GachaLogService _gachaLogService;

    /// <summary>千星奇域（<see cref="CurrentGameBiz"/> 为 hk4eugc）记录不在 GachaLogService 体系内，单独走该服务。</summary>
    private GenshinBeyondGachaService? _beyondGachaService;


    public DeleteGachaLogDialog()
    {
        this.InitializeComponent();
    }




    [ObservableProperty]
    private ObservableCollection<long> uidList;



    [ObservableProperty]
    private long? selectUid;
    partial void OnSelectUidChanged(long? value)
    {
        if (value.HasValue)
        {
            LoadGachaLog(value.Value);
        }
    }


    /// <summary>当前 UID 全部记录的抽取时间，用于统计所选时间段内的条数。</summary>
    private List<DateTime> gachaLogTimes;


    private void ContentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CurrentGameBiz.Game is GameBiz.hk4e)
            {
                _gachaLogService = AppConfig.GetService<GenshinGachaService>();
            }
            if (CurrentGameBiz.Game is GameBiz.hkrpg)
            {
                _gachaLogService = AppConfig.GetService<StarRailGachaService>();
            }
            if (CurrentGameBiz.Game is GameBiz.nap)
            {
                _gachaLogService = AppConfig.GetService<ZZZGachaService>();
            }
            if (CurrentGameBiz.Game is "hk4eugc")
            {
                _beyondGachaService = AppConfig.GetService<GenshinBeyondGachaService>();
            }

            List<long>? uids = _beyondGachaService?.GetUids() ?? _gachaLogService?.GetUids();
            if (uids is not null)
            {
                UidList = new(uids);
                if (DefaultUid.HasValue && UidList.Contains(DefaultUid.Value))
                {
                    SelectUid = DefaultUid;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete gacha log dialog initialize.");
        }
    }



    private void LoadGachaLog(long uid)
    {
        try
        {
            gachaLogTimes = _beyondGachaService?.GetGachaLogTimes(uid)
                            ?? _gachaLogService.GetGachaLogItemEx(uid).Select(x => x.Time).ToList();
            TextBlock_GachaLogNumber.Visibility = Visibility.Visible;
            TextBlock_GachaLogNumber.Text = string.Format(Lang.DeleteGachaLogDialog_ThisAccountHas0GachaRecordS, gachaLogTimes.Count);
            CalendarDatePicker_BeginTime.Date = null;
            CalendarDatePicker_EndTime.Date = null;
            TimePicker_BeginTime.SelectedTime = null;
            TimePicker_EndTime.SelectedTime = null;
            Button_Delete.IsEnabled = false;
            TextBlock_SelectedCount.Visibility = Visibility.Collapsed;
            TextBlock_Warning.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete gacha log dialog load gacha log.");
        }
    }




    private void CalendarDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (sender == CalendarDatePicker_BeginTime && TimePicker_BeginTime.SelectedTime == null)
        {
            TimePicker_BeginTime.Time = TimeSpan.Zero;
        }
        if (sender == CalendarDatePicker_EndTime && TimePicker_EndTime.SelectedTime == null)
        {
            TimePicker_EndTime.Time = TimeSpan.Zero;
        }
        OnTimePeriodChanged();
    }



    private void TimePicker_SelectedTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        OnTimePeriodChanged();
    }




    private void OnTimePeriodChanged()
    {
        try
        {
            var beginDate = CalendarDatePicker_BeginTime.Date;
            var beginTime = TimePicker_BeginTime.SelectedTime;
            var endDate = CalendarDatePicker_EndTime.Date;
            var endTime = TimePicker_EndTime.SelectedTime;
            if (beginDate.HasValue && beginTime.HasValue && endDate.HasValue && endTime.HasValue)
            {
                var begin = beginDate + beginTime;
                var end = endDate + endTime;
                if (begin <= end)
                {
                    var count = gachaLogTimes.Count(x => x >= begin && x <= end);
                    TextBlock_SelectedCount.Visibility = Visibility.Visible;
                    TextBlock_SelectedCount.Text = string.Format(Lang.DeleteGachaLogDialog_TheSelectedTimePeriodIncludes0GachaRecords, count);
                    Button_Delete.IsEnabled = count > 0;
                    TextBlock_Warning.Visibility = begin < DateTimeOffset.Now - TimeSpan.FromDays(180) ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }
            }
            Button_Delete.IsEnabled = false;
            TextBlock_SelectedCount.Visibility = Visibility.Collapsed;
            TextBlock_Warning.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete gacha log dialog time changed.");
        }
    }





    [RelayCommand]
    private void Delete()
    {
        try
        {
            var beginDate = CalendarDatePicker_BeginTime.Date;
            var beginTime = TimePicker_BeginTime.SelectedTime;
            var endDate = CalendarDatePicker_EndTime.Date;
            var endTime = TimePicker_EndTime.SelectedTime;
            if (SelectUid.HasValue && beginDate.HasValue && beginTime.HasValue && endDate.HasValue && endTime.HasValue)
            {
                var begin = beginDate + beginTime;
                var end = endDate + endTime;
                if (begin <= end)
                {
                    var count = gachaLogTimes.Count(x => x >= begin && x <= end);
                    if (count > 0)
                    {
                        _logger.LogInformation("Deleting {count} gachalogs from {begin} to {end} of {uid} ({biz}).", count, begin, end, SelectUid, CurrentGameBiz);
                        if (_beyondGachaService is not null)
                        {
                            _beyondGachaService.DeleteGachaLogByTime(SelectUid.Value, begin.Value.LocalDateTime, end.Value.LocalDateTime);
                        }
                        else
                        {
                            _gachaLogService.DeleteGachaLogByTime(SelectUid.Value, begin.Value.LocalDateTime, end.Value.LocalDateTime);
                        }
                        InAppToast.MainWindow?.Success(string.Format(Lang.GachaLogPage_DeletedGachaRecordsOfUid, count, SelectUid));
                        Deleted = true;
                        this.Hide();
                        return;
                    }
                }
            }
            Button_Delete.IsEnabled = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete gacha log dialog delete method.");
        }
    }



    [RelayCommand]
    private void Cancel()
    {
        this.Hide();
    }


}
