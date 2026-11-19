using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using System;

namespace Starward.Features.GameRecord.DailyNote.Notify;

/// <summary>
/// 提醒设置对话框右侧的一行：项目开关与阈值。改动后回调宿主保存配置。
/// x:Bind 数据模板实例化，不能用 <c>required</c> / <c>init</c>。
/// </summary>
public sealed partial class DailyNoteNotifyItemView : ObservableObject
{

    private readonly Action<DailyNoteNotifyItemView> _changed;

    private bool _enabled;

    private double _threshold;


    /// <summary>
    /// 创建一行提醒设置。
    /// </summary>
    /// <param name="item">便笺项目。</param>
    /// <param name="name">显示名。</param>
    /// <param name="metric">最近一次便笺里的数值，用于阈值上限与「当前」文字；本次运行还没拿到过时为 null。</param>
    /// <param name="enabled">是否已开启。</param>
    /// <param name="threshold">已保存的阈值，null 表示上限。</param>
    /// <param name="changed">开关或阈值被用户改动后的回调。</param>
    internal DailyNoteNotifyItemView(DailyNoteNotifyItem item, string name, DailyNoteMetric? metric, bool enabled, int? threshold, Action<DailyNoteNotifyItemView> changed)
    {
        Item = item;
        Name = name;
        Maximum = metric is { Max: > 0 } m ? m.Max : double.MaxValue;
        StatusText = metric is { Max: > 0 } x ? string.Format(Lang.DailyNoteNotify_CurrentValue, x.Current, x.Max) : "";
        StatusVisibility = string.IsNullOrEmpty(StatusText) ? Visibility.Collapsed : Visibility.Visible;
        _enabled = enabled;
        // NaN 让 NumberBox 显示占位文字「上限」
        _threshold = threshold is int value ? value : double.NaN;
        _changed = changed;
    }


    /// <summary>便笺项目。</summary>
    internal DailyNoteNotifyItem Item { get; }

    /// <summary>显示名。</summary>
    public string Name { get; }

    /// <summary>阈值输入框的上限；便笺没加载出来时不限制。</summary>
    public double Maximum { get; }

    /// <summary>项目名下方的小字，如「当前 120/200」；没有数据时为空。</summary>
    public string StatusText { get; }

    /// <summary>没有数据时收起「当前」那行。</summary>
    public Visibility StatusVisibility { get; }


    /// <summary>是否开启提醒。</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                _changed(this);
            }
        }
    }


    /// <summary>阈值，NaN 表示上限。</summary>
    public double Threshold
    {
        get => _threshold;
        set
        {
            if (SetProperty(ref _threshold, value))
            {
                _changed(this);
            }
        }
    }


    /// <summary>要保存的阈值：清空或非正数时为 null（上限）。</summary>
    internal int? ThresholdValue => double.IsNaN(_threshold) || _threshold < 1 ? null : (int)Math.Round(_threshold);

}
