using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starward.Controls;

namespace DevWin;

public sealed partial class AnimatedTextBlock : UserControl
{

    private DispatcherQueueTimer? _timer;
    private string _targetText = "";
    private int _revealedLength;


    public AnimatedTextBlock()
    {
        this.InitializeComponent();
        this.Unloaded += (_, _) => StopAnimation();
    }



    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AnimatedTextBlock),
            new PropertyMetadata("", OnTextChanged));


    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(AnimatedTextBlock),
            new PropertyMetadata(null));


    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(AnimatedTextBlock),
            new PropertyMetadata(14d));


    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(AnimatedTextBlock),
            new PropertyMetadata(null));


    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(AnimatedTextBlock),
            new PropertyMetadata(TextTrimming.CharacterEllipsis));


    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(AnimatedTextBlock),
            new PropertyMetadata(TextWrapping.NoWrap));



    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedTextBlock block)
        {
            block.StartAnimation(e.NewValue as string ?? "");
        }
    }


    private void StartAnimation(string text)
    {
        StopAnimation();
        _targetText = text;
        _revealedLength = 0;
        DisplayTextBlock.Text = "";

        if (string.IsNullOrEmpty(text) || !EntranceAnimation.AnimationsEnabled())
        {
            DisplayTextBlock.Text = text;
            return;
        }

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(18);
        _timer.Tick += OnRevealTick;
        _timer.Start();
    }


    private void OnRevealTick(DispatcherQueueTimer sender, object args)
    {
        if (_revealedLength >= _targetText.Length)
        {
            StopAnimation();
            return;
        }
        _revealedLength++;
        DisplayTextBlock.Text = _targetText[.._revealedLength];
    }


    private void StopAnimation()
    {
        if (_timer is null)
        {
            return;
        }
        _timer.Tick -= OnRevealTick;
        _timer.Stop();
        _timer = null;
    }

}