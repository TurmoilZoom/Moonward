using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Scighost.WinUI.ImageEx;
using Starward.Features.Background;
using Starward.Features.Codec;
using Starward.Helpers;
using System;
using System.Collections.Generic;
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
    /// 解码目标宽度（逻辑像素 / DIP）。大于 0 时，<see cref="BitmapImage.DecodePixelWidth"/> 按此值解码，
    /// 配合 <see cref="DecodePixelType.Logical"/> 由系统按当前 DPI 缩放，避免按原生分辨率解码后再缩小显示。
    /// 与 <see cref="DecodeHeight"/> 只设其一时保持纵横比；二者都设则解码为精确尺寸（适合正方形图标）。
    /// 0 表示不限制（按原生尺寸解码）。
    /// </summary>
    public int DecodeWidth
    {
        get { return (int)GetValue(DecodeWidthProperty); }
        set { SetValue(DecodeWidthProperty, value); }
    }

    /// <summary><see cref="DecodeWidth"/> 的依赖属性。</summary>
    public static readonly DependencyProperty DecodeWidthProperty =
        DependencyProperty.Register(nameof(DecodeWidth), typeof(int), typeof(CachedImage), new PropertyMetadata(0));


    /// <summary>
    /// 解码目标高度（逻辑像素 / DIP）。语义同 <see cref="DecodeWidth"/>。
    /// </summary>
    public int DecodeHeight
    {
        get { return (int)GetValue(DecodeHeightProperty); }
        set { SetValue(DecodeHeightProperty, value); }
    }

    /// <summary><see cref="DecodeHeight"/> 的依赖属性。</summary>
    public static readonly DependencyProperty DecodeHeightProperty =
        DependencyProperty.Register(nameof(DecodeHeight), typeof(int), typeof(CachedImage), new PropertyMetadata(0));


    /// <summary>
    /// <see cref="ImageEx"/> 提供的自定义资源加载钩子。
    /// 根据 URI Scheme 分别处理：
    /// <list type="bullet">
    /// <item>ms-appx：直接返回 BitmapImage（应用 <see cref="DecodeWidth"/>/<see cref="DecodeHeight"/> 解码尺寸）。</item>
    /// <item>file：根据 <see cref="IsThumbnail"/> 决定是否生成缩略图，否则按解码尺寸加载。</item>
    /// <item>其他（http/https 等）：走 <see cref="FileCache.GetFromCacheAsync"/> 获取本地缓存路径后再加载，
    /// 并按「URI + 解码尺寸」在内存中复用已解码的 <see cref="BitmapImage"/>，
    /// 使列表 / ItemsRepeater 中大量重复图标只解码一次、避免重复磁盘 IO。</item>
    /// </list>
    /// </summary>
    protected override async Task<ImageSource?> ProvideCachedResourceAsync(Uri imageUri, CancellationToken token)
    {
        if (imageUri.Scheme is "ms-appx")
        {
            return CreateBitmapImage(imageUri);
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
                return CreateBitmapImage(imageUri);
            }
        }
        else
        {
            // 内存级复用：相同 URI + 相同解码尺寸的图标共享同一个已解码 BitmapImage，
            // 列表里反复出现 / 滚动反复实例化的相同图标只需解码一次。
            // 仅当显式指定了解码尺寸时才进缓存——这样只缓存「小且可复用」的图标（如抽卡列表图标），
            // 避免把未限定尺寸的大图（背景 / 横幅等）按原生分辨率塞进缓存而吃内存。
            bool useCache = DecodeWidth > 0 || DecodeHeight > 0;
            string? cacheKey = useCache ? GetCacheKey(imageUri) : null;
            if (useCache && TryGetCachedImage(cacheKey!, out BitmapImage? cached))
            {
                return cached;
            }

            string? file = await FileCache.GetFromCacheAsync(imageUri, false, token);
            if (token.IsCancellationRequested)
            {
                throw new TaskCanceledException("Image source has changed.");
            }
            if (file is null)
            {
                throw new FileNotFoundException(imageUri.ToString());
            }

            // await 期间其它实例可能已填充缓存，回到 UI 线程后再查一次，进一步减少重复解码。
            if (useCache && TryGetCachedImage(cacheKey!, out cached))
            {
                return cached;
            }

            BitmapImage bitmap = CreateBitmapImage(null);

            void OnOpened(object sender, RoutedEventArgs e)
            {
                bitmap.ImageOpened -= OnOpened;
                bitmap.ImageFailed -= OnFailed;
            }
            void OnFailed(object sender, ExceptionRoutedEventArgs e)
            {
                bitmap.ImageOpened -= OnOpened;
                bitmap.ImageFailed -= OnFailed;
                // 解码失败：移除内存缓存并删除损坏的磁盘缓存，下次重新下载解码。
                if (useCache)
                {
                    RemoveCachedImage(cacheKey!);
                }
                FileCache.DeleteCacheFile(bitmap.UriSource);
            }
            bitmap.ImageOpened += OnOpened;
            bitmap.ImageFailed += OnFailed;
            bitmap.UriSource = new Uri(file);

            if (useCache)
            {
                AddCachedImage(cacheKey!, bitmap);
            }
            return bitmap;
        }
    }


    /// <summary>
    /// 创建并配置 <see cref="DecodeWidth"/>/<see cref="DecodeHeight"/> 解码尺寸的 <see cref="BitmapImage"/>。
    /// 解码尺寸必须在赋值 <see cref="BitmapImage.UriSource"/> 之前设置才会生效，故 <paramref name="uri"/> 为空时
    /// 只创建并配置、由调用方稍后设置 UriSource。
    /// </summary>
    private BitmapImage CreateBitmapImage(Uri? uri)
    {
        var bitmap = new BitmapImage();
        int w = DecodeWidth, h = DecodeHeight;
        if (w > 0 || h > 0)
        {
            bitmap.DecodePixelType = DecodePixelType.Logical;
            if (w > 0)
            {
                bitmap.DecodePixelWidth = w;
            }
            if (h > 0)
            {
                bitmap.DecodePixelHeight = h;
            }
        }
        if (uri is not null)
        {
            bitmap.UriSource = uri;
        }
        return bitmap;
    }


    /// <summary>按「URI + 解码尺寸」构造内存缓存键，使同一图标的不同解码尺寸互不串用。</summary>
    private string GetCacheKey(Uri uri) => $"{uri}|{DecodeWidth}x{DecodeHeight}";


    #region 已解码图片的内存缓存（去重 + 避免重复解码 / 重复磁盘 IO）

    /// <summary>内存缓存容量上限（条目数）。超出后按 LRU 淘汰最久未使用项。</summary>
    private const int ImageCacheCapacity = 512;

    private static readonly object _imageCacheLock = new();
    private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapImage>>> _imageCacheMap = new();
    private static readonly LinkedList<KeyValuePair<string, BitmapImage>> _imageCacheLru = new();

    private static bool TryGetCachedImage(string key, out BitmapImage? image)
    {
        lock (_imageCacheLock)
        {
            if (_imageCacheMap.TryGetValue(key, out var node))
            {
                // 命中：移到 LRU 头部标记为最近使用。
                _imageCacheLru.Remove(node);
                _imageCacheLru.AddFirst(node);
                image = node.Value.Value;
                return true;
            }
        }
        image = null;
        return false;
    }

    private static void AddCachedImage(string key, BitmapImage image)
    {
        lock (_imageCacheLock)
        {
            if (_imageCacheMap.TryGetValue(key, out var existing))
            {
                existing.Value = new KeyValuePair<string, BitmapImage>(key, image);
                _imageCacheLru.Remove(existing);
                _imageCacheLru.AddFirst(existing);
            }
            else
            {
                var node = new LinkedListNode<KeyValuePair<string, BitmapImage>>(new KeyValuePair<string, BitmapImage>(key, image));
                _imageCacheLru.AddFirst(node);
                _imageCacheMap[key] = node;
                if (_imageCacheMap.Count > ImageCacheCapacity)
                {
                    LinkedListNode<KeyValuePair<string, BitmapImage>>? tail = _imageCacheLru.Last;
                    if (tail is not null)
                    {
                        _imageCacheLru.RemoveLast();
                        _imageCacheMap.Remove(tail.Value.Key);
                    }
                }
            }
        }
    }

    private static void RemoveCachedImage(string key)
    {
        lock (_imageCacheLock)
        {
            if (_imageCacheMap.Remove(key, out var node))
            {
                _imageCacheLru.Remove(node);
            }
        }
    }

    #endregion

}
