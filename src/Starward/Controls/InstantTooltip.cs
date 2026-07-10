using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;

namespace Starward.Controls;

/// <summary>
/// 为任意 <see cref="FrameworkElement"/> 提供自定义即时 Tooltip（附加属性 API）。
/// <para>
/// 与系统 <c>ToolTipService</c> 不同：无显示延迟、样式为亚克力圆角气泡、
/// 同一 <see cref="XamlRoot"/> 内共享一个 <see cref="InstantTooltipHost"/>（单个 Popup）。
/// </para>
/// <para>
/// XAML 用法：
/// <code>
/// sc:InstantTooltip.Text="{x:Bind lang:Lang.SomeKey}"
/// sc:InstantTooltip.Placement="Left"
/// </code>
/// 代码用法：<see cref="SetText"/> / <see cref="SetPlacement"/>。
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
            if (element.GetValue(WireStateProperty) is not string marker)
            {
                marker = Guid.NewGuid().ToString();
                element.SetValue(WireStateProperty, marker);
                element.Loaded += Element_LoadedForWire;
            }
        }
        else
        {
            WireElement(element);
        }
    }


    /// <summary>
    /// 元素首次进入视觉树后完成挂接（仅在此前因无 XamlRoot 而延迟时使用）。
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
            if (host.IsEmpty)
            {
                host.Dispose();
                Hosts.Remove(element.XamlRoot);
            }
        }
    }


    /// <summary>
    /// 元素卸载时由 <see cref="InstantTooltipHost"/> 回调，解除挂接。
    /// </summary>
    /// <param name="element">已卸载的元素。</param>
    internal static void OnElementUnloaded(FrameworkElement element)
    {
        UnwireElement(element);
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
        }

        return host;
    }
}
