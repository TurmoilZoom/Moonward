using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 自定义功能栏对话框里的一行功能项。
/// </summary>
public sealed partial class RightToolbarCustomizeRow : UserControl
{

    private readonly AnimatedVisualPlayer? _player;

    /// <summary>构造时给开关赋初值也会触发 Toggled，构造完成前不往外报。</summary>
    private readonly bool _initialized;


    /// <summary>
    /// 创建一行。
    /// </summary>
    /// <param name="item">功能项。</param>
    /// <param name="subtitle">名称下方的说明（如「仅国服」），null 时不显示。</param>
    /// <param name="isShown">是否显示在功能栏里。</param>
    public RightToolbarCustomizeRow(RightToolbarItem item, string? subtitle, bool isShown)
    {
        this.InitializeComponent();
        Item = item;
        string title = RightToolbarItemCatalog.GetTitle(item);
        TextBlock_Title.Text = title;
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            TextBlock_Subtitle.Text = subtitle;
            TextBlock_Subtitle.Visibility = Visibility.Visible;
        }
        Border_Icon.Child = RightToolbarItemCatalog.CreateIcon(item, out _player);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ToggleSwitch_Shown, title);
        ToggleSwitch_Shown.IsOn = isShown;
        Loaded += RightToolbarCustomizeRow_Loaded;
        Unloaded += RightToolbarCustomizeRow_Unloaded;
        _initialized = true;
    }


    /// <summary>这一行对应的功能项。</summary>
    public RightToolbarItem Item { get; }


    /// <summary>是否显示在功能栏里（开关状态）。</summary>
    public bool IsShown => ToggleSwitch_Shown.IsOn;


    /// <summary>用户拨动了显示开关。</summary>
    public event EventHandler? IsShownChanged;


    /// <summary>
    /// 切换拖动中的外观：拖动时换成不透明浮层底色和描边，压在其他行上面时不透字。
    /// </summary>
    /// <param name="dragging">是否正在拖动这一行。</param>
    public void SetDragging(bool dragging)
    {
        if (dragging)
        {
            Grid_Root.Background = (Brush)Application.Current.Resources["CustomOverlayAcrylicBrush"];
            Grid_Root.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        }
        else
        {
            Grid_Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Grid_Root.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }


    /// <summary>
    /// 判断某个元素是否在显示开关里：开关上的按下要留给开关，不起拖。
    /// </summary>
    /// <param name="element">指针事件的原始来源。</param>
    /// <returns>在开关里时为 <see langword="true"/>。</returns>
    public bool IsInToggle(DependencyObject? element)
    {
        while (element is not null && !ReferenceEquals(element, this))
        {
            if (ReferenceEquals(element, ToggleSwitch_Shown))
            {
                return true;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }



    /// <summary>
    /// 系统模板的 On 状态自带一段动画：圆点瞬间到位，但填充、描边和圆点颜色要用 83ms 从「关」的外观渐变过去，
    /// 而且套上模板时也会播。切换游戏会重建各行，开着的开关就会先露出空心再变实心，看起来像闪了一下。
    /// 加载后把当前状态的动画直接跳到结尾；用户拨开关走的是状态之间的过渡动画，不受影响。
    /// </summary>
    private void ToggleSwitch_Shown_Loaded(object sender, RoutedEventArgs e)
    {
        ToggleSwitch_Shown.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(ToggleSwitch_Shown) == 0 || VisualTreeHelper.GetChild(ToggleSwitch_Shown, 0) is not FrameworkElement templateRoot)
        {
            return;
        }
        foreach (VisualStateGroup group in VisualStateManager.GetVisualStateGroups(templateRoot))
        {
            // 状态组名见官方 ToggleSwitch 模板（Dragging / Off / On）
            if (group.Name == "ToggleStates")
            {
                group.CurrentState?.Storyboard?.SkipToFill();
                return;
            }
        }
    }


    private void ToggleSwitch_Shown_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            IsShownChanged?.Invoke(this, EventArgs.Empty);
        }
    }


    /// <summary>
    /// 悬停时播放一次 Lottie，与首页按钮一致。
    /// </summary>
    private void Grid_Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = _player?.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
    }


    /// <summary>
    /// 离开后回到首帧。
    /// </summary>
    private void Grid_Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_player is not null)
        {
            _player.Stop();
            _player.SetProgress(0);
        }
    }


    private void RightToolbarCustomizeRow_Loaded(object sender, RoutedEventArgs e)
    {
        // 静止显示首帧，同首页按钮
        _player?.SetProgress(0);
    }


    private void RightToolbarCustomizeRow_Unloaded(object sender, RoutedEventArgs e)
    {
        _player?.Stop();
    }


}
