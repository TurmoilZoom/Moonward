using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Xaml.Interactivity;
using System;
using System.Threading.Tasks;

namespace Starward.Helpers;

public class InAppToast : Behavior<StackPanel>
{

    private readonly DispatcherQueueTimer _dismissTimer;


    public string Tag
    {
        get { return (string)GetValue(TagProperty); }
        set { SetValue(TagProperty, value); }
    }
    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register("Tag", typeof(string), typeof(InAppToast), new PropertyMetadata(default));


    public static InAppToast? MainWindow { get; private set; }




    public InAppToast()
    {
        _dismissTimer = DispatcherQueue.CreateTimer();
        _dismissTimer.Interval = TimeSpan.FromSeconds(30);
        _dismissTimer.IsRepeating = true;
        _dismissTimer.Tick += _dismissTimer_Tick;
    }


    protected override void OnAttached()
    {
        base.OnAttached();
        if (Tag is nameof(MainWindow))
        {
            MainWindow = this;
        }
    }


    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (Tag is nameof(MainWindow))
        {
            MainWindow = null;
        }
    }



    private void _dismissTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            int i = 0;
            var count = AssociatedObject.Children.Count;
            while (i < count)
            {
                var item = AssociatedObject.Children[i] as InfoBar;
                if (item != null && !item.IsOpen)
                {
                    AssociatedObject.Children.RemoveAt(i);
                    count--;
                }
                else
                {
                    i++;
                }
            }
        }
        catch { }
    }



    /// <summary>
    /// 将 InfoBar 加入 Toast 容器。已在 UI 线程时同步插入，避免「立刻 IsOpen=false」与延迟入队乱序导致条重新打开后关不掉。
    /// </summary>
    /// <param name="infoBar">要显示的 InfoBar。</param>
    /// <param name="duration">自动关闭毫秒数；0 表示不自动关闭。</param>
    /// <param name="index">插入位置；≤0 时追加到末尾。</param>
    public void Show(InfoBar infoBar, int duration = 0, int index = -1)
    {
        // UI 线程同步展示，防止调用方在 await 前关闭后，入队回调又把 IsOpen 设回 true。
        if (DispatcherQueue.HasThreadAccess)
        {
            _ = ShowCoreAsync(infoBar, duration, index);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => _ = ShowCoreAsync(infoBar, duration, index));
        }
    }


    /// <summary>
    /// 实际插入 InfoBar，并在 duration &gt; 0 时延时关闭。
    /// </summary>
    private async Task ShowCoreAsync(InfoBar infoBar, int duration, int index)
    {
        try
        {
            infoBar.IsOpen = true;
            if (index > 0)
            {
                AssociatedObject.Children.Insert(index, infoBar);
            }
            else
            {
                AssociatedObject.Children.Add(infoBar);
            }
            if (duration > 0)
            {
                await Task.Delay(duration);
                infoBar.IsOpen = false;
            }
        }
        catch { }
    }


    private void AddInfoBar(InfoBarSeverity severity, string? title, string? message, int duration = 0)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var infoBar = new InfoBar
            {
                Title = title,
                Message = message,
                Severity = severity,
                IsOpen = true,
            };
            if (severity == InfoBarSeverity.Informational)
            {
                infoBar.Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush;
            }
            Show(infoBar, duration);
        });
    }




    public void Information(string? title, string? message = null, int duration = 3000)
    {
        AddInfoBar(InfoBarSeverity.Informational, title, message, duration);
    }



    public void Success(string? title, string? message = null, int duration = 3000)
    {
        AddInfoBar(InfoBarSeverity.Success, title, message, duration);
    }




    public void Warning(string? title, string? message = null, int duration = 5000)
    {
        AddInfoBar(InfoBarSeverity.Warning, title, message, duration);
    }



    public void Error(string? title, string? message = null, int duration = 5000)
    {
        AddInfoBar(InfoBarSeverity.Error, title, message, duration);
    }



    public void Error(Exception ex, string? message = null, int duration = 5000)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            AddInfoBar(InfoBarSeverity.Error, ex.GetType().Name, ex.Message, duration);
        }
        else
        {
            AddInfoBar(InfoBarSeverity.Error, $"{ex.GetType().Name} - {message}", ex.Message, duration);
        }
    }


    public void ShowWithButton(InfoBarSeverity severity, string? title, string? message, string buttonContent, Action buttonAction, Action? closedAction = null, int duration = 0)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var infoBar = Create(severity, title, message, buttonContent, buttonAction, closedAction);
            Show(infoBar, duration);
        });
    }


    private InfoBar Create(InfoBarSeverity severity, string? title, string? message = null, string? buttonContent = null, Action? buttonAction = null, Action? closedAction = null)
    {
        Button? button = null;
        if (!string.IsNullOrWhiteSpace(buttonContent) && buttonAction != null)
        {
            button = new Button
            {
                Content = buttonContent,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            button.Click += (_, _) =>
            {
                try
                {
                    buttonAction();
                }
                catch { }
            };
        }
        var infoBar = new InfoBar
        {
            Severity = severity,
            Title = title,
            Message = message,
            ActionButton = button,
            IsOpen = true,
        };
        if (closedAction is not null)
        {
            infoBar.CloseButtonClick += (_, _) =>
            {
                try
                {
                    closedAction();
                }
                catch { }
            };
        }
        return infoBar;
    }



}
