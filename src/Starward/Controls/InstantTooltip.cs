using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;

namespace Starward.Controls;

/// <summary>
/// 为任意 <see cref="FrameworkElement"/> 提供自定义即时 Tooltip（附加属性）。
/// 同一 <see cref="XamlRoot"/> 内共享一个 Popup，样式与动画与导航项 Tooltip 一致。
/// </summary>
public static class InstantTooltip
{
    private static readonly DependencyProperty WireStateProperty =
        DependencyProperty.RegisterAttached(
            "WireState",
            typeof(object),
            typeof(InstantTooltip),
            new PropertyMetadata(null));

    private static readonly Dictionary<XamlRoot, InstantTooltipHost> Hosts = new();

    /// <summary>Tooltip 文案。</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(InstantTooltip),
            new PropertyMetadata(null, OnTextChanged));

    /// <summary>Tooltip 显示方位，默认右侧。</summary>
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


    private static void WireElement(FrameworkElement element)
    {
        if (element.XamlRoot is null || string.IsNullOrEmpty(GetText(element)))
        {
            return;
        }

        InstantTooltipHost host = GetOrCreateHost(element.XamlRoot, element);
        host.Register(element);
    }


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