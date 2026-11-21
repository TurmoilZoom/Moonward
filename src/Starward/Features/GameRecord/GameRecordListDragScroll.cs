using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Features.Gacha;
using System.Runtime.CompilerServices;


namespace Starward.Features.GameRecord;

/// <summary>
/// 米游社工具箱各页左侧记录列表的鼠标拖拽滚动 + 顶底过拉回弹（与抽卡记录列表同一套实现，见 <see cref="GachaStatsListDragScrollHelper.BindSelectableList"/>）。
/// <para>在列表上设 <c>sfg:GameRecordListDragScroll.IsEnabled="True"</c> 即可；绑定随列表 Loaded 建立、Unloaded 释放。</para>
/// <para>列表初始折叠时（如没有数据时 Visibility 绑定为 Collapsed）模板尚未应用、找不到内部 ScrollViewer，等它第一次有尺寸后再绑定。</para>
/// </summary>
public static class GameRecordListDragScroll
{

    /// <summary>是否启用拖拽滚动。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GameRecordListDragScroll), new PropertyMetadata(false, OnIsEnabledChanged));


    /// <summary>读取 <see cref="IsEnabledProperty"/>。</summary>
    /// <param name="listView">目标列表。</param>
    /// <returns>是否启用。</returns>
    public static bool GetIsEnabled(ListViewBase listView) => (bool)listView.GetValue(IsEnabledProperty);


    /// <summary>设置 <see cref="IsEnabledProperty"/>。</summary>
    /// <param name="listView">目标列表。</param>
    /// <param name="value">是否启用。</param>
    public static void SetIsEnabled(ListViewBase listView, bool value) => listView.SetValue(IsEnabledProperty, value);


    /// <summary>每个列表的绑定状态；列表被回收后条目随之释放。</summary>
    private static readonly ConditionalWeakTable<ListViewBase, State> States = new();


    private sealed class State
    {
        public GachaStatsListDragScrollHelper.GachaStatsListDragScrollBinding? Binding;

        /// <summary>已挂 SizeChanged，等模板应用后再绑定。</summary>
        public bool WaitingForTemplate;
    }


    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListViewBase list)
        {
            return;
        }
        list.Loaded -= List_Loaded;
        list.Unloaded -= List_Unloaded;
        Release(list);
        if (e.NewValue is true)
        {
            list.Loaded += List_Loaded;
            list.Unloaded += List_Unloaded;
            if (list.IsLoaded)
            {
                TryBind(list);
            }
        }
    }


    private static void List_Loaded(object sender, RoutedEventArgs e)
    {
        TryBind((ListViewBase)sender);
    }


    private static void List_Unloaded(object sender, RoutedEventArgs e)
    {
        Release((ListViewBase)sender);
    }


    /// <summary>
    /// 建立绑定；模板尚未应用时改为等列表第一次有尺寸。
    /// </summary>
    /// <param name="list">目标列表。</param>
    private static void TryBind(ListViewBase list)
    {
        State state = States.GetOrCreateValue(list);
        if (state.Binding is not null)
        {
            return;
        }
        state.Binding = GachaStatsListDragScrollHelper.BindSelectableList(list);
        if (state.Binding is null && !state.WaitingForTemplate)
        {
            state.WaitingForTemplate = true;
            list.SizeChanged += List_SizeChanged;
        }
    }


    private static void List_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var list = (ListViewBase)sender;
        // 绑定要取内容的合成视觉并开启 Translation，不在布局回调里同步做，推到本轮布局之后。
        list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!list.IsLoaded || !States.TryGetValue(list, out State? state) || !state.WaitingForTemplate)
            {
                return;
            }
            state.Binding ??= GachaStatsListDragScrollHelper.BindSelectableList(list);
            if (state.Binding is not null)
            {
                state.WaitingForTemplate = false;
                list.SizeChanged -= List_SizeChanged;
            }
        });
    }


    /// <summary>
    /// 释放绑定并撤掉等待中的 SizeChanged。
    /// </summary>
    /// <param name="list">目标列表。</param>
    private static void Release(ListViewBase list)
    {
        if (!States.TryGetValue(list, out State? state))
        {
            return;
        }
        state.Binding?.Dispose();
        state.Binding = null;
        if (state.WaitingForTemplate)
        {
            state.WaitingForTemplate = false;
            list.SizeChanged -= List_SizeChanged;
        }
    }

}
