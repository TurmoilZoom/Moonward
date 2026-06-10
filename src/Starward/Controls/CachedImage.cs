using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Scighost.WinUI.ImageEx;
using Starward.Features.Background;
using Starward.Features.Codec;
using Starward.Helpers;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Controls;

/// <summary>
/// 基于 <see cref="ImageEx"/>（Scighost.WinUI.ImageEx）的增强图片控件。
/// 核心职责：
/// <list type="bullet">
/// <item>远程图片（http/https）：通过 <see cref="FileCache"/> 下载并缓存到本地磁盘，再加载显示。</item>
/// <item>本地文件（file://）：直接显示，或根据 <see cref="IsThumbnail"/> 生成缩略图（支持普通图片 + 视频首帧）。</item>
/// <item>应用内资源（ms-appx）：直接使用 BitmapImage。</item>
/// </list>
/// 提供 <see cref="IsThumbnail"/> 和 <see cref="PngThumbnail"/> 两个附加属性，用于控制本地文件的缩略图生成行为。
/// 在图片解码失败时会自动清理 <see cref="FileCache"/> 中的损坏缓存。
/// </summary>
public sealed partial class CachedImage : ImageEx
{

    /// <summary>
    /// 是否对本地文件生成缩略图。
    /// 当 Source 为 file:// 协议且此属性为 true 时：
    /// - 视频文件（由 <see cref="BackgroundService.FileIsSupportedVideo"/> 判断）→ 调用 <see cref="ImageThumbnail.GetVideoThumbnailAsync"/>。
    /// - 普通图片 → 调用 <see cref="ImageThumbnail.GetImageThumbnailAsync"/>（可通过 <see cref="PngThumbnail"/> 控制输出格式）。
    /// </summary>
    public bool IsThumbnail
    {
        get { return (bool)GetValue(IsThumbnailProperty); }
        set { SetValue(IsThumbnailProperty, value); }
    }

    /// <summary><see cref="IsThumbnail"/> 的依赖属性。</summary>
    public static readonly DependencyProperty IsThumbnailProperty =
        DependencyProperty.Register("IsThumbnail", typeof(bool), typeof(CachedImage), new PropertyMetadata(false));


    /// <summary>
    /// 当 <see cref="IsThumbnail"/> 为 true 且处理普通图片时，是否输出 PNG 格式缩略图。
    /// false 时通常输出 JPEG 以获得更小的文件体积（由 <see cref="ImageThumbnail"/> 内部实现）。
    /// </summary>
    public bool PngThumbnail
    {
        get { return (bool)GetValue(PngThumbnailProperty); }
        set { SetValue(PngThumbnailProperty, value); }
    }

    /// <summary><see cref="PngThumbnail"/> 的依赖属性。</summary>
    public static readonly DependencyProperty PngThumbnailProperty =
        DependencyProperty.Register(nameof(PngThumbnail), typeof(bool), typeof(CachedImage), new PropertyMetadata(false));


    /// <summary>
    /// <see cref="ImageEx"/> 提供的自定义资源加载钩子。
    /// 根据 URI Scheme 分别处理：
    /// <list type="bullet">
    /// <item>ms-appx：直接返回 BitmapImage。</item>
    /// <item>file：根据 <see cref="IsThumbnail"/> 决定是否生成缩略图。</item>
    /// <item>其他（http/https 等）：走 <see cref="FileCache.GetFromCacheAsync"/> 获取本地缓存路径后再加载。</item>
    /// </list>
    /// </summary>
    protected override async Task<ImageSource?> ProvideCachedResourceAsync(Uri imageUri, CancellationToken token)
    {
        try
        {
            if (imageUri.Scheme is "ms-appx")
            {
                return new BitmapImage(imageUri);
            }
            else if (imageUri.Scheme is "file")
            {
                if (IsThumbnail)
                {
                    if (BackgroundService.FileIsSupportedVideo(imageUri.OriginalString))
                    {
                        return await ImageThumbnail.GetVideoThumbnailAsync(imageUri.LocalPath, token);
                    }
                    else
                    {
                        return await ImageThumbnail.GetImageThumbnailAsync(imageUri.LocalPath, PngThumbnail, token);
                    }
                }
                else
                {
                    return new BitmapImage(imageUri);
                }
            }
            else
            {
                var file = await FileCache.GetFromCacheAsync(imageUri, false, token);
                if (token.IsCancellationRequested)
                {
                    throw new TaskCanceledException("Image source has changed.");
                }
                if (file is null)
                {
                    throw new FileNotFoundException(imageUri.ToString());
                }
                var bitmap = new BitmapImage(new Uri(file));
                bitmap.ImageOpened += BitmapImage_ImageOpened;
                bitmap.ImageFailed += BitmapImage_ImageFailed;
                return bitmap;
            }
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception)
        {
            throw;
        }
    }


    /// <summary>
    /// 图片成功打开后清理事件订阅（防止内存泄漏）。
    /// </summary>
    private void BitmapImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is BitmapImage image)
        {
            image.ImageOpened -= BitmapImage_ImageOpened;
            image.ImageFailed -= BitmapImage_ImageFailed;
        }
    }


    /// <summary>
    /// 图片解码失败时清理事件订阅，并删除 <see cref="FileCache"/> 中对应的损坏缓存文件。
    /// 这样下次加载相同 URI 时会重新下载。
    /// </summary>
    private void BitmapImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is BitmapImage image)
        {
            image.ImageOpened -= BitmapImage_ImageOpened;
            image.ImageFailed -= BitmapImage_ImageFailed;
            FileCache.DeleteCacheFile(image.UriSource);
        }
    }

}
