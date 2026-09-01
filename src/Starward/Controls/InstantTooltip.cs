using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Starward.Controls;

/// <summary>
/// 为任意 <see cref="FrameworkElement"/> 提供自定义即时 Tooltip（附加属性 API）。
/// <para>
/// 与系统 <c>ToolTipService</c> 不同：无显示延迟、样式为亚克力圆角气泡、
/// 同一 <see cref="XamlRoot"/> 内共享一个 <see cref="InstantTooltipHost"/>（单个 Popup）。
/// 可选 <see cref="ActionTextProperty"/> + <see cref="SetActionCallback"/> 提供可点击操作
/// （默认在气泡右下角；<see cref="ActionInlineProperty"/> 为 true 时紧跟正文右侧，不换行）。
/// 指针移入气泡本身不会关闭，便于点击。
/// </para>
/// <para>
/// XAML 用法：
/// <code>
/// sc:InstantTooltip.Text="{x:Bind lang:Lang.SomeKey}"
/// sc:InstantTooltip.Placement="Left"
/// sc:InstantTooltip.ActionText="{x:Bind lang:Lang.SomeAction}"
/// sc:InstantTooltip.Trigger="Click"
/// </code>
/// 问号 / 感叹号这类说明性提示用 <see cref="TriggerProperty"/> = <see cref="InstantTooltipTrigger.Click"/>：
/// 悬停不弹出，点击锚点开合，点击别处收起。
/// 操作回调需在代码中 <see cref="SetActionCallback"/>；可选 <see cref="SetOpenChangedCallback"/> 同步外层 Popup。
/// 无独立 XAML 控件文件；视觉树在 <see cref="InstantTooltipHost"/> 内用代码创建。
/// </para>
/// </summary>
public static class InstantTooltip
{
    /// <summary>
    /// 标记是否已订阅 <see cref="FrameworkElement.Loaded"/> 等待挂接，避免重复订阅。
    /// 值为任意非空对象（当前用 Guid 字符串）；元素尚未进入视觉树、没有 <see cref="UIElement.XamlRoot"/> 时使用。
    /// </summary>
    private static readonly DependencyProperty WireStateProperty =
        DependencyProperty.RegisterAttached(
            "WireState",
            typeof(object),
            typeof(InstantTooltip),
            new PropertyMetadata(null));

    /// <summary>
    /// 每个窗口（<see cref="XamlRoot"/>）对应一个 Host，多锚点共用同一 Popup。
    /// </summary>
    private static readonly Dictionary<XamlRoot, InstantTooltipHost> Hosts = new();

    /// <summary>
    /// 正在抑制 Tooltip 显示的视觉树根（拖拽滚动等场景：已有 Host 时强制关闭并忽略进入；Host 尚未创建时也会在创建后继承该状态）。
    /// </summary>
    private static readonly HashSet<XamlRoot> SuppressedRoots = new();

    /// <summary>锚点 → 操作按钮点击回调（弱表，随元素回收）。</summary>
    private static readonly ConditionalWeakTable<FrameworkElement, Action> ActionCallbacks = new();

    /// <summary>锚点 → 可交互 Tooltip 打开/关闭回调（用于外层悬停菜单等保持打开）。</summary>
    private static readonly ConditionalWeakTable<FrameworkElement, Action<bool>> OpenChangedCallbacks = new();

    /// <summary>
    /// Tooltip 文案附加属性。设为空或 <see langword="null"/> 会解除挂接；非空则在可挂接时注册到 Host。
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(InstantTooltip),
            new PropertyMetadata(null, OnTextChanged));

    /// <summary>
    /// Tooltip 相对锚点的显示方位；默认 <see cref="InstantTooltipPlacement.Right"/>（导航侧栏常用）。
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.RegisterAttached(
            "Placement",
            typeof(InstantTooltipPlacement),
            typeof(InstantTooltip),
            new PropertyMetadata(InstantTooltipPlacement.Right));

    /// <summary>
    /// 可选：操作按钮文案。非空时 Host 显示可点击链接（需配合 <see cref="SetActionCallback"/>）。
    /// 默认在气泡右下角；与 <see cref="ActionInlineProperty"/> 同时使用时紧跟正文。
    /// </summary>
    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.RegisterAttached(
            "ActionText",
            typeof(string),
            typeof(InstantTooltip),
            new PropertyMetadata(null));


    /// <summary>
    /// 为 true 时操作链接紧跟正文右侧，不另起一行。默认 false（右下角独立按钮）。
    /// </summary>
    public static readonly DependencyProperty ActionInlineProperty =
        DependencyProperty.RegisterAttached(
            "ActionInline",
            typeof(bool),
            typeof(InstantTooltip),
            new PropertyMetadata(false));


    /// <summary>
    /// 触发方式；默认 <see cref="InstantTooltipTrigger.Hover"/>。
    /// 设为 <see cref="InstantTooltipTrigger.Click"/> 时悬停不再弹出，须点击锚点开合（问号 / 说明图标用）。
    /// </summary>
    public static readonly DependencyProperty TriggerProperty =
        DependencyProperty.RegisterAttached(
            "Trigger",
            typeof(InstantTooltipTrigger),
            typeof(InstantTooltip),
            new PropertyMetadata(InstantTooltipTrigger.Hover));


    /// <summary>
    /// 取得 <see cref="TextProperty"/> 值。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <returns>本地化提示文案；未设置时返回 <see langword="null"/>。</returns>
    public static string? GetText(DependencyObject element)
    {
        return (string?)element.GetValue(TextProperty);
    }


    /// <summary>
    /// 设置 <see cref="TextProperty"/> 值。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <param name="value">提示文案；空值会解除挂接。</param>
    public static void SetText(DependencyObject element, string? value)
    {
        element.SetValue(TextProperty, value);
    }


    /// <summary>
    /// 取得 <see cref="PlacementProperty"/> 值。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <returns>显示方位。</returns>
    public static InstantTooltipPlacement GetPlacement(DependencyObject element)
    {
        return (InstantTooltipPlacement)element.GetValue(PlacementProperty);
    }


    /// <summary>
    /// 设置 <see cref="PlacementProperty"/> 值。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <param name="value">显示方位。</param>
    public static void SetPlacement(DependencyObject element, InstantTooltipPlacement value)
    {
        element.SetValue(PlacementProperty, value);
    }


    /// <summary>
    /// 取得操作按钮文案。
    /// </summary>
    public static string? GetActionText(DependencyObject element)
    {
        return (string?)element.GetValue(ActionTextProperty);
    }


    /// <summary>
    /// 设置操作按钮文案；空则不显示操作按钮。
    /// </summary>
    public static void SetActionText(DependencyObject element, string? value)
    {
        element.SetValue(ActionTextProperty, value);
    }


    /// <summary>
    /// 取得操作链接是否紧跟正文。
    /// </summary>
    public static bool GetActionInline(DependencyObject element)
    {
        return (bool)element.GetValue(ActionInlineProperty);
    }


    /// <summary>
    /// 设置操作链接是否紧跟正文（不另起一行）。
    /// </summary>
    public static void SetActionInline(DependencyObject element, bool value)
    {
        element.SetValue(ActionInlineProperty, value);
    }


    /// <summary>
    /// 取得 <see cref="TriggerProperty"/> 值。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <returns>触发方式。</returns>
    public static InstantTooltipTrigger GetTrigger(DependencyObject element)
    {
        return (InstantTooltipTrigger)element.GetValue(TriggerProperty);
    }


    /// <summary>
    /// 设置 <see cref="TriggerProperty"/> 值。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <param name="value">触发方式。</param>
    public static void SetTrigger(DependencyObject element, InstantTooltipTrigger value)
    {
        element.SetValue(TriggerProperty, value);
    }


    /// <summary>
    /// 注册操作按钮点击回调（覆盖旧值；传 <see langword="null"/> 清除）。
    /// </summary>
    public static void SetActionCallback(FrameworkElement element, Action? callback)
    {
        ActionCallbacks.Remove(element);
        if (callback is not null)
        {
            ActionCallbacks.Add(element, callback);
        }
    }


    /// <summary>
    /// 取得操作按钮点击回调。
    /// </summary>
    internal static Action? GetActionCallback(FrameworkElement element)
    {
        return ActionCallbacks.TryGetValue(element, out Action? callback) ? callback : null;
    }


    /// <summary>
    /// 注册可交互 Tooltip 打开/关闭回调（有 <see cref="ActionTextProperty"/> 时，打开/关闭会通知，便于外层菜单保持打开）。
    /// </summary>
    public static void SetOpenChangedCallback(FrameworkElement element, Action<bool>? callback)
    {
        OpenChangedCallbacks.Remove(element);
        if (callback is not null)
        {
            OpenChangedCallbacks.Add(element, callback);
        }
    }


    /// <summary>
    /// 取得打开/关闭回调。
    /// </summary>
    internal static Action<bool>? GetOpenChangedCallback(FrameworkElement element)
    {
        return OpenChangedCallbacks.TryGetValue(element, out Action<bool>? callback) ? callback : null;
    }


    /// <summary>
    /// 临时抑制指定窗口内的即时 Tooltip（拖拽列表滚动等场景）。
    /// 为 <see langword="true"/> 时立即关闭已显示的气泡，并忽略指针进入触发的显示；为 <see langword="false"/> 时恢复。
    /// </summary>
    /// <param name="xamlRoot">目标视觉树根；为 <see langword="null"/> 时无操作。</param>
    /// <param name="suppressed">是否抑制显示。</param>
    public static void SetSuppressed(XamlRoot? xamlRoot, bool suppressed)
    {
        if (xamlRoot is null)
        {
            return;
        }

        if (suppressed)
        {
            SuppressedRoots.Add(xamlRoot);
        }
        else
        {
            SuppressedRoots.Remove(xamlRoot);
        }

        if (Hosts.TryGetValue(xamlRoot, out InstantTooltipHost? host))
        {
            host.SetSuppressed(suppressed);
        }
    }


    /// <summary>
    /// 指定视觉树根是否处于 Tooltip 抑制状态。
    /// </summary>
    internal static bool IsSuppressed(XamlRoot? xamlRoot)
    {
        return xamlRoot is not null && SuppressedRoots.Contains(xamlRoot);
    }


    /// <summary>
    /// 指针当前是否停在气泡上。
    /// 外层弹层（如签到 Flyout）用它判断这次「外部点击」是不是点在提示气泡里，
    /// 从而决定拦截关闭（让用户点得到气泡里的链接）还是连同提示一起收起。
    /// </summary>
    /// <param name="xamlRoot">目标视觉树根；为 <see langword="null"/> 时返回 <see langword="false"/>。</param>
    /// <returns>指针在气泡内为 <see langword="true"/>。</returns>
    public static bool IsPointerOverTooltip(XamlRoot? xamlRoot)
    {
        return xamlRoot is not null
            && Hosts.TryGetValue(xamlRoot, out InstantTooltipHost? host)
            && host.IsPointerOverPopup;
    }


    /// <summary>
    /// 立即关闭当前气泡，不进入抑制状态，也不会在指针未离开时自动再弹出。
    /// 用于锚点容器已不可见（如下侧工具栏取消固定后淡出）但指针仍停在原处的情况。
    /// </summary>
    /// <param name="xamlRoot">目标视觉树根；为 <see langword="null"/> 时无操作。</param>
    public static void Dismiss(XamlRoot? xamlRoot)
    {
        if (xamlRoot is not null && Hosts.TryGetValue(xamlRoot, out InstantTooltipHost? host))
        {
            host.Dismiss();
        }
    }


    /// <summary>
    /// <see cref="TextProperty"/> 变更回调：空文案解绑；有文案则立即挂接，或等 <c>Loaded</c> 后再挂接。
    /// </summary>
    /// <param name="d">附加属性目标。</param>
    /// <param name="e">新旧文案。</param>
    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        string? newText = e.NewValue as string;
        if (string.IsNullOrEmpty(newText))
        {
            UnwireElement(element);
            return;
        }

        // XAML 解析阶段可能尚无 XamlRoot，无法创建 Popup；推迟到 Loaded
        if (element.XamlRoot is null)
        {
            EnsureLoadedSubscription(element);
        }
        else
        {
            WireElement(element);
        }
    }


    /// <summary>
    /// 元素进入视觉树后完成挂接（无 XamlRoot 时延迟，或 ItemsRepeater 回收后再次 Loaded）。
    /// </summary>
    /// <param name="sender">已 Loaded 的锚点元素。</param>
    /// <param name="e">路由事件参数。</param>
    private static void Element_LoadedForWire(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= Element_LoadedForWire;
        element.ClearValue(WireStateProperty);
        if (!string.IsNullOrEmpty(GetText(element)))
        {
            WireElement(element);
        }
    }


    /// <summary>
    /// 将元素注册到当前 <see cref="XamlRoot"/> 对应的 <see cref="InstantTooltipHost"/>。
    /// </summary>
    /// <param name="element">已有 XamlRoot 且文案非空的锚点。</param>
    private static void WireElement(FrameworkElement element)
    {
        if (element.XamlRoot is null || string.IsNullOrEmpty(GetText(element)))
        {
            return;
        }

        InstantTooltipHost host = GetOrCreateHost(element.XamlRoot, element);
        host.Register(element);
    }


    /// <summary>
    /// 在尚无 XamlRoot 或元素即将复用时，订阅 <see cref="FrameworkElement.Loaded"/> 以便稍后挂接。
    /// </summary>
    /// <param name="element">目标锚点。</param>
    private static void EnsureLoadedSubscription(FrameworkElement element)
    {
        if (element.GetValue(WireStateProperty) is string)
        {
            return;
        }

        element.SetValue(WireStateProperty, Guid.NewGuid().ToString());
        element.Loaded += Element_LoadedForWire;
    }


    /// <summary>
    /// 取消 Loaded 等待、从 Host 注销；若该窗口已无任何锚点则释放 Host。
    /// </summary>
    /// <param name="element">要解除的锚点。</param>
    private static void UnwireElement(FrameworkElement element)
    {
        element.Loaded -= Element_LoadedForWire;
        element.ClearValue(WireStateProperty);

        if (element.XamlRoot is not null && Hosts.TryGetValue(element.XamlRoot, out InstantTooltipHost? host))
        {
            host.Unregister(element);
            TryReleaseHost(element.XamlRoot, host);
        }
    }


    /// <summary>
    /// Host 内无锚点时释放 Popup 并从字典移除。
    /// </summary>
    /// <param name="xamlRoot">视觉树根。</param>
    /// <param name="host">待检查的宿主。</param>
    private static void TryReleaseHost(XamlRoot xamlRoot, InstantTooltipHost host)
    {
        if (!host.IsEmpty)
        {
            return;
        }

        host.Dispose();
        Hosts.Remove(xamlRoot);
    }


    /// <summary>
    /// 元素卸载时由 <see cref="InstantTooltipHost"/> 回调：先从 Host 注销；
    /// 若文案仍在则订阅 Loaded，以便列表虚拟化复用后重新挂接。
    /// </summary>
    /// <param name="element">已卸载的元素。</param>
    /// <param name="host">发起回调的宿主（已完成 Unregister 前由 Host 传入当前实例）。</param>
    internal static void OnElementUnloaded(FrameworkElement element, InstantTooltipHost host)
    {
        // Host 侧已 Unregister，此处只处理字典生命周期与复用重挂
        if (Hosts.TryGetValue(host.XamlRoot, out InstantTooltipHost? registered) && ReferenceEquals(registered, host))
        {
            TryReleaseHost(host.XamlRoot, host);
        }

        if (!string.IsNullOrEmpty(GetText(element)))
        {
            EnsureLoadedSubscription(element);
        }
        else
        {
            element.Loaded -= Element_LoadedForWire;
            element.ClearValue(WireStateProperty);
        }
    }


    /// <summary>
    /// 取得或创建指定视觉树根上的 Tooltip 宿主。
    /// </summary>
    /// <param name="xamlRoot">窗口/视觉树根。</param>
    /// <param name="themeSource">用于解析 ThemeResource 的元素（通常为第一个注册的锚点）。</param>
    /// <returns>该 XamlRoot 对应的共享 Host。</returns>
    private static InstantTooltipHost GetOrCreateHost(XamlRoot xamlRoot, FrameworkElement themeSource)
    {
        if (!Hosts.TryGetValue(xamlRoot, out InstantTooltipHost? host))
        {
            host = new InstantTooltipHost(xamlRoot, themeSource);
            Hosts[xamlRoot] = host;
            // 拖拽等场景可能先 SetSuppressed，后才有锚点挂接 Host
            if (SuppressedRoots.Contains(xamlRoot))
            {
                host.SetSuppressed(true);
            }
        }

        return host;
    }
}
