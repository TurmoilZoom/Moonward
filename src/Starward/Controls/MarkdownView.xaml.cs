using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Starward.Helpers;
using System;
using System.Threading.Tasks;

namespace Starward.Controls;

/// <summary>
/// 本地渲染的 Markdown（CommunityToolkit Labs 的 MarkdownTextBlock，同 PowerToys「新增功能」窗口），不带滚动。
/// 发行说明、更新内容与帮助文档共用，不再请求 GitHub 的 /markdown 接口（匿名每小时 60 次，共用代理出口时常被耗尽）。
/// </summary>
public sealed partial class MarkdownView : UserControl
{


    private readonly MarkdownThemes _themes = new()
    {
        H1FontSize = 24,
        H1FontWeight = FontWeights.SemiBold,
        H1Margin = new Thickness(0, 16, 0, 0),
        H2FontSize = 20,
        H2FontWeight = FontWeights.SemiBold,
        H2Margin = new Thickness(0, 16, 0, 0),
        H3FontSize = 16,
        H3FontWeight = FontWeights.SemiBold,
        H3Margin = new Thickness(0, 16, 0, 0),
        H4FontWeight = FontWeights.SemiBold,
        InlineCodeFontSize = 13,
        InlineCodePadding = new Thickness(4, 0, 4, 0),
        ListBulletSpacing = 1,
        ListGutterWidth = 16,
    };

    /// <summary>上次渲染的文本与主题，两者都没变时跳过重渲染。</summary>
    private (string? Text, ElementTheme Theme)? _rendered;



    public MarkdownView()
    {
        this.InitializeComponent();
        MarkdownBlock.Config = new MarkdownConfig { Themes = _themes, ImageProvider = new CachedImageProvider() };
        this.Loaded += (_, _) => Render();
        this.ActualThemeChanged += (_, _) => Render();
    }



    /// <summary>
    /// 要显示的 Markdown。
    /// </summary>
    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata(null, (d, _) => ((MarkdownView)d).Render()));



    /// <summary>
    /// 单个换行是否按换行显示，与 GitHub 的发行说明、评论一致（早期的发行说明用「•」加单换行分条，不是 Markdown 列表）。
    /// 文档类保持 Markdown 默认（单个换行并入同一段），同 GitHub 显示 .md 文件。
    /// 只在 XAML 里设置：控件建模板时才按它生成解析管线，之后再改不生效。
    /// </summary>
    public bool HardLineBreaks
    {
        get => MarkdownBlock.UseSoftlineBreakAsHardlineBreak;
        set => MarkdownBlock.UseSoftlineBreakAsHardlineBreak = value;
    }



    /// <summary>
    /// 按当前主题的画刷整份重渲染：MarkdownTextBlock 渲染时把画刷抄进各个文本元素，换主题后不会自己更新。
    /// 还没进可视树时只记下文本，Loaded 后再按实际主题渲染。
    /// </summary>
    private void Render()
    {
        if (!IsLoaded)
        {
            return;
        }
        string? text = Markdown;
        if (_rendered is { } last && last.Text == text && last.Theme == ActualTheme)
        {
            return;
        }
        _rendered = (text, ActualTheme);
        ApplyThemeBrushes();
        // 同一文本再赋一次不会触发重渲染，先清空
        MarkdownBlock.Text = string.Empty;
        MarkdownBlock.Text = text ?? string.Empty;
    }



    /// <summary>
    /// 把本控件里按实际主题解析的画刷抄给 MarkdownTextBlock。
    /// 该版本的默认画刷取自 Application.Current.Resources、按应用主题解析，而本应用的应用主题固定为深色（App.xaml.cs），
    /// 浅色窗口里标题、链接、代码会是深色主题的颜色（PowerToys 也有同样的绕过，上游修复见 CommunityToolkit/Labs-Windows PR 785）。
    /// </summary>
    private void ApplyThemeBrushes()
    {
        if (MarkdownBlock.Foreground is Brush text)
        {
            _themes.H1Foreground = text;
            _themes.H2Foreground = text;
            _themes.H3Foreground = text;
            _themes.H4Foreground = text;
            _themes.H5Foreground = text;
            _themes.H6Foreground = text;
            _themes.InlineCodeForeground = text;
            _themes.CodeBlockForeground = text;
            _themes.QuoteForeground = text;
        }
        if (CodeBrushProvider.Background is Brush codeBackground)
        {
            _themes.InlineCodeBackground = codeBackground;
            _themes.CodeBlockBackground = codeBackground;
            // 默认是系统强调色的浅色调，深色主题下表头的浅色字几乎看不清
            _themes.TableHeadingBackground = codeBackground;
        }
        if (CodeBrushProvider.BorderBrush is Brush stroke)
        {
            _themes.InlineCodeBorderBrush = stroke;
            _themes.CodeBlockBorderBrush = stroke;
            _themes.QuoteBorderBrush = stroke;
            _themes.TableBorderBrush = stroke;
            _themes.BorderBrush = stroke;
        }
        if (DividerBrushProvider.BorderBrush is Brush divider)
        {
            _themes.HorizontalRuleBrush = divider;
        }
        if (LinkBrushProvider.Foreground is Brush link)
        {
            _themes.LinkForeground = link;
            // 该版本的链接颜色读的是全局 MarkdownConfig.Default，而不是本控件的 Config
            MarkdownConfig.Default.Themes.LinkForeground = link;
        }
    }



    /// <summary>
    /// 网络图片走应用的 <see cref="FileCache"/>（跟随应用内代理、落盘缓存），不用控件自带的裸 HttpClient。
    /// 控件自带的做法还会把图片宽高定死为像素尺寸，窄窗口里会被裁掉；这里只限制最大宽度，按 Uniform 缩小。
    /// </summary>
    private sealed class CachedImageProvider : IImageProvider
    {

        public bool ShouldUseThisProvider(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
        }

        public async Task<Image> GetImage(string url)
        {
            var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            string? file = await FileCache.GetFromCacheAsync(new Uri(url));
            if (file is not null)
            {
                var bitmap = new BitmapImage();
                // 不超过原始像素宽，免得小图被拉大
                bitmap.ImageOpened += (_, _) => image.MaxWidth = bitmap.PixelWidth;
                bitmap.UriSource = new Uri(file);
                image.Source = bitmap;
            }
            return image;
        }

    }


}
