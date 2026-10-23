using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Starward.Controls;
using Starward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace Starward.Features.Update;

/// <summary>
/// 一组发行版本的说明：一个版本一段（标题、日期、发布页面原生显示，正文用 <see cref="MarkdownView"/> 本地渲染）。
/// 顶部版本条可直接跳到某个版本，滚动时高亮当前看到的版本。发行说明窗口每个月份一个，更新窗口一个。
/// </summary>
public sealed partial class ReleaseNotesView : UserControl
{


    /// <summary>版本段落顶端离视口顶端在这个距离以内，就算「正在看」这个版本。</summary>
    private const double SpyThreshold = 48;

    /// <summary>非第一段上方分隔线占的高度（上边距 8 + 线 1），跳转时越过它，免得和版本条的下边线叠成两条。</summary>
    private const double SeparatorHeight = 9;


    private List<ReleaseNoteEntry> _entries = new();

    /// <summary>点版本条触发的滚动还没停下：期间不按滚动位置改选中项，免得一路经过中间的版本来回跳。</summary>
    private bool _scrollingToSection;

    /// <summary>按滚动位置改版本条选中项期间置位，SelectionChanged 里不再滚动。</summary>
    private bool _updatingSelection;

    /// <summary>上次由用户点选引起的选中变化（选中项与时刻），Tapped 据此跳过同一次点击的重复滚动。</summary>
    private (SelectorBarItem? Item, long Tick) _lastSelectionChange;



    public ReleaseNotesView()
    {
        this.InitializeComponent();
    }



    /// <summary>
    /// 要显示的发行版本（新到旧）。
    /// </summary>
    public IList<GithubRelease>? Releases
    {
        get => (IList<GithubRelease>?)GetValue(ReleasesProperty);
        set => SetValue(ReleasesProperty, value);
    }

    // 类型登记为 object：GithubRelease 不是 WinRT 类型，泛型接口登记成依赖属性类型没有必要
    public static readonly DependencyProperty ReleasesProperty = DependencyProperty.Register(
        nameof(Releases), typeof(object), typeof(ReleaseNotesView), new PropertyMetadata(null, (d, _) => ((ReleaseNotesView)d).Rebuild()));



    /// <summary>
    /// 按 <see cref="Releases"/> 重建各段与版本条，并回到顶部。发行说明窗口的 FlipView 会把容器挪给别的月份，届时也走这里。
    /// </summary>
    private void Rebuild()
    {
        _entries = ReleaseNotesFormatter.BuildEntries(Releases ?? []);
        ItemsControl_Releases.ItemsSource = _entries;
        _updatingSelection = true;
        try
        {
            SelectorBar_Versions.Items.Clear();
            foreach (ReleaseNoteEntry entry in _entries)
            {
                var item = new SelectorBarItem { Text = entry.Tag };
                item.Tapped += SelectorBarItem_Tapped;
                SelectorBar_Versions.Items.Add(item);
            }
            SelectorBar_Versions.SelectedItem = SelectorBar_Versions.Items.Count > 0 ? SelectorBar_Versions.Items[0] : null;
        }
        finally
        {
            _updatingSelection = false;
        }
        Grid_VersionBar.Visibility = _entries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _scrollingToSection = false;
        Scroller.ChangeView(null, 0, null, true);
    }



    private void SelectorBar_Versions_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingSelection)
        {
            return;
        }
        _lastSelectionChange = (sender.SelectedItem, Environment.TickCount64);
        ScrollToSection(sender.Items.IndexOf(sender.SelectedItem));
    }



    /// <summary>
    /// 点已选中的版本不会触发 SelectionChanged，这里补上：看到一半时点它回到该版本开头。
    /// 同一次点击若刚由 SelectionChanged 滚过（两者先后顺序不定），不再滚第二次。
    /// </summary>
    private void SelectorBarItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not SelectorBarItem item || !ReferenceEquals(item, SelectorBar_Versions.SelectedItem))
        {
            return;
        }
        if (ReferenceEquals(_lastSelectionChange.Item, item) && Environment.TickCount64 - _lastSelectionChange.Tick < 500)
        {
            return;
        }
        ScrollToSection(SelectorBar_Versions.Items.IndexOf(item));
    }



    /// <summary>
    /// 滚到第 <paramref name="index"/> 个版本的开头；后面内容不够时停在最底部。
    /// </summary>
    /// <param name="index">版本序号。</param>
    private void ScrollToSection(int index)
    {
        if (GetSectionOffset(index) is not double offset)
        {
            return;
        }
        double target = Math.Clamp(offset + (index > 0 ? SeparatorHeight : 0), 0, Scroller.ScrollableHeight);
        if (Math.Abs(target - Scroller.VerticalOffset) < 1)
        {
            return;
        }
        _scrollingToSection = true;
        Scroller.ChangeView(null, target, null, !EntranceAnimation.AnimationsEnabled());
    }



    /// <summary>
    /// 第 <paramref name="index"/> 个版本段落顶端在滚动内容里的位置（即滚到它开头时的 VerticalOffset）。
    /// <para>
    /// 按内容内的位置算，不按相对视口：一次点击会先后触发 SelectionChanged 与 Tapped，第二次计算时上一次的滚动动画已经在挪内容，
    /// 而 VerticalOffset 还没更新，按视口算的结果往下跳会差一截（露出上一个版本的末尾），往上跳会多一截（标题被切掉）。
    /// </para>
    /// </summary>
    /// <param name="index">版本序号。</param>
    /// <returns>位置；序号越界或容器还没生成时为 null。</returns>
    private double? GetSectionOffset(int index)
    {
        if (index < 0 || index >= _entries.Count || ItemsControl_Releases.ContainerFromIndex(index) is not UIElement container)
        {
            return null;
        }
        return container.TransformToVisual(ItemsControl_Releases).TransformPoint(new Point(0, 0)).Y + ItemsControl_Releases.Margin.Top;
    }



    /// <summary>
    /// 用户自己滚动时，按位置高亮版本条上正在看的版本：顶端已到视口上方附近的最后一段；滚到底时算最后一段（最后几段太短，到不了顶端）。
    /// </summary>
    private void Scroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollingToSection)
        {
            if (!e.IsIntermediate)
            {
                _scrollingToSection = false;
            }
            return;
        }
        if (_entries.Count < 2)
        {
            return;
        }
        int current = 0;
        if (Scroller.ScrollableHeight > 0 && Scroller.VerticalOffset >= Scroller.ScrollableHeight - 1)
        {
            current = _entries.Count - 1;
        }
        else
        {
            for (int i = _entries.Count - 1; i > 0; i--)
            {
                // 与 ViewChanged 报告的 VerticalOffset 对齐，得到段落顶端相对视口顶端的位置
                if (GetSectionOffset(i) is double offset && offset - Scroller.VerticalOffset <= SpyThreshold)
                {
                    current = i;
                    break;
                }
            }
        }
        SelectorBarItem item = SelectorBar_Versions.Items[current];
        if (ReferenceEquals(item, SelectorBar_Versions.SelectedItem))
        {
            return;
        }
        _updatingSelection = true;
        try
        {
            SelectorBar_Versions.SelectedItem = item;
        }
        finally
        {
            _updatingSelection = false;
        }
        // 版本多、版本条横向放不下时，把高亮的那个滚进可见范围
        item.StartBringIntoView();
    }


}
