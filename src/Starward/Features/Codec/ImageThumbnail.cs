using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Imaging;
using Starward.Codec.ICC;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.FileProperties;
using DeviceInformation = Windows.Devices.Enumeration.DeviceInformation;

namespace Starward.Features.Codec;

internal static class ImageThumbnail
{



    private static readonly string CacheFolder = Path.Combine(AppConfig.CacheFolder, "thumb");


    public static readonly bool AvifDecoderSupported;

    public static readonly bool JxlDecoderSupported;


    private const string AvifImageBase64 = "AAAAHGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZgAAAOltZXRhAAAAAAAAACFoZGxyAAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAAAA5waXRtAAAAAAABAAAAHmlsb2MAAAAARAAAAQABAAAAAQAAAQ0AAAASAAAAKGlpbmYAAAAAAAEAAAAaaW5mZQIAAAAAAQAAYXYwMUNvbG9yAAAAAGhpcHJwAAAASWlwY28AAAAUaXNwZQAAAAAAAAABAAAAAQAAAA5waXhpAAAAAAEIAAAADGF2MUOBABwAAAAAE2NvbHJuY2x4AAEAAAABgAAAABdpcG1hAAAAAAAAAAEAAQQBAoMEAAAAGm1kYXQSAAoEGAAGVTIIH/AAAQACH8A=";

    private const string JxlImageBase64 = "/woAEBAUNwIIBAEAGABLGIsVggE=";


    private static List<KeyValuePair<string, BitmapTypedValue>> ImageQuality = [new KeyValuePair<string, BitmapTypedValue>("ImageQuality", new BitmapTypedValue(0.6f, Windows.Foundation.PropertyType.Single))];


    private static readonly SemaphoreSlim Semaphore = new(Math.Clamp(Environment.ProcessorCount / 2, 1, int.MaxValue));


    /// <summary>
    /// AVIF / JPEG XL 缩略图缓存名后缀。旧版由系统解码器生成（可能偏色）或回退分支写出 0 字节文件，换名使其失效。
    /// </summary>
    private const string BundledDecoderCacheSuffix = "_2";


    static ImageThumbnail()
    {
        try
        {
            var bytes = Convert.FromBase64String(AvifImageBase64);
            BitmapDecoder.CreateAsync(new MemoryStream(bytes).AsRandomAccessStream()).AsTask().Wait();
            AvifDecoderSupported = true;
        }
        catch { }
        try
        {
            var bytes = Convert.FromBase64String(JxlImageBase64);
            BitmapDecoder.CreateAsync(new MemoryStream(bytes).AsRandomAccessStream()).AsTask().Wait();
            JxlDecoderSupported = true;
        }
        catch { }
    }




    public static async Task<BitmapSource> GetImageThumbnailAsync(string path, bool png, CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        // AVIF / JPEG XL 不走系统解码器：HEIF 扩展解内嵌 ICC 接近 Display P3 的 AVIF 会整图偏色，与查看器不一致
        // https://github.com/Scighost/Starward/issues/1953
        bool bundledDecoder = extension is ".avif" or ".jxl";
        string fileName = GetCachedThumbnailName(path);
        string cachePath = Path.Combine(CacheFolder, bundledDecoder ? fileName + BundledDecoderCacheSuffix : fileName);
        if (File.Exists(cachePath))
        {
            return new BitmapImage(new Uri(cachePath));
        }
        await Semaphore.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(300, cancellationToken);
            if (bundledDecoder)
            {
                return await LoadBundledCodecImageAsync(path, cachePath, Path.Combine(CacheFolder, fileName), cancellationToken);
            }
            else
            {
                return await LoadImageAsync(path, cachePath, cancellationToken);
            }
        }
        finally
        {
            Semaphore.Release();
        }
    }


    private static string GetCachedThumbnailName(string path)
    {
        byte[] hashBytes = ArrayPool<byte>.Shared.Rent(24);
        try
        {
            ReadOnlySpan<byte> pathSpan = MemoryMarshal.AsBytes(path.AsSpan());
            XxHash64.Hash(pathSpan, hashBytes.AsSpan(0, 8));
            MD5.HashData(pathSpan, hashBytes.AsSpan(8, 16));
            return Convert.ToHexString(hashBytes.AsSpan(0, 24));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(hashBytes);
        }
    }



    /// <summary>
    /// 用自带解码器（与查看器相同的 <see cref="ImageLoader"/>）生成 AVIF / JPEG XL 缩略图：转换到 sRGB 色域，HDR 色调映射到 SDR。
    /// </summary>
    /// <param name="filePath">原图路径</param>
    /// <param name="cachePath">缩略图缓存路径</param>
    /// <param name="legacyCachePath">旧版缓存路径，生成成功后删除</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缩略图</returns>
    private static async Task<BitmapSource> LoadBundledCodecImageAsync(string filePath, string cachePath, string legacyCachePath, CancellationToken cancellationToken = default)
    {
        await Task.Run(async () =>
        {
            using ImageInfo info = await ImageLoader.LoadImageAsync(filePath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            CanvasBitmap bitmap = info.CanvasBitmap;
            (uint width, uint height) = GetThumbnailSize(bitmap.SizeInPixels.Width, bitmap.SizeInPixels.Height);

            List<IDisposable> effects = [];
            try
            {
                var scaleEffect = new ScaleEffect
                {
                    Source = bitmap,
                    Scale = new Vector2((float)width / bitmap.SizeInPixels.Width, (float)height / bitmap.SizeInPixels.Height),
                    InterpolationMode = CanvasImageInterpolation.HighQualityCubic,
                    BufferPrecision = CanvasBufferPrecision.Precision16Float,
                };
                effects.Add(scaleEffect);
                ICanvasImage output = scaleEffect;

                // HDR 与浮点像素是线性 scRGB；其余为 sRGB 曲线编码，换色域前须先线性化
                bool linear = info.HDR || bitmap.Format is DirectXPixelFormat.R16G16B16A16Float or DirectXPixelFormat.R32G32B32A32Float;
                bool convertPrimaries = !(info.ColorPrimaries.TryGetDefinedPrimaries(out int id) && id == 1);
                if (linear)
                {
                    // 与截图完成提示窗口相同：300 nits 映射为 SDR 白
                    var toneMapEffect = new HdrToneMapEffect
                    {
                        Source = output,
                        InputMaxLuminance = 1000,
                        OutputMaxLuminance = 300,
                        DisplayMode = HdrToneMapEffectDisplayMode.Hdr,
                        BufferPrecision = CanvasBufferPrecision.Precision16Float,
                    };
                    var whiteLevelEffect = new WhiteLevelAdjustmentEffect
                    {
                        Source = toneMapEffect,
                        InputWhiteLevel = 80,
                        OutputWhiteLevel = 300,
                        BufferPrecision = CanvasBufferPrecision.Precision16Float,
                    };
                    effects.Add(toneMapEffect);
                    effects.Add(whiteLevelEffect);
                    output = whiteLevelEffect;
                }
                else if (convertPrimaries)
                {
                    var eotfEffect = new SrgbGammaEffect
                    {
                        Source = output,
                        GammaMode = SrgbGammaMode.EOTF,
                        BufferPrecision = CanvasBufferPrecision.Precision16Float,
                    };
                    effects.Add(eotfEffect);
                    output = eotfEffect;
                }
                if (convertPrimaries)
                {
                    Matrix4x4 m = ColorPrimaries.GetColorTransferMatrix(info.ColorPrimaries, ColorPrimaries.BT709);
                    var matrixEffect = new ColorMatrixEffect
                    {
                        Source = output,
                        ColorMatrix = new Matrix5x4(m.M11, m.M12, m.M13, m.M14,
                                                    m.M21, m.M22, m.M23, m.M24,
                                                    m.M31, m.M32, m.M33, m.M34,
                                                    m.M41, m.M42, m.M43, m.M44,
                                                    0, 0, 0, 0),
                        ClampOutput = false,
                        BufferPrecision = CanvasBufferPrecision.Precision16Float,
                    };
                    effects.Add(matrixEffect);
                    output = matrixEffect;
                }
                if (linear || convertPrimaries)
                {
                    var oetfEffect = new SrgbGammaEffect
                    {
                        Source = output,
                        GammaMode = SrgbGammaMode.OETF,
                        BufferPrecision = CanvasBufferPrecision.Precision16Float,
                    };
                    effects.Add(oetfEffect);
                    output = oetfEffect;
                }

                using var renderTarget = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96);
                using (var ds = renderTarget.CreateDrawingSession())
                {
                    ds.Clear(Colors.Transparent);
                    ds.DrawImage(output);
                }
                cancellationToken.ThrowIfCancellationRequested();

                using var ms = new MemoryStream();
                await renderTarget.SaveAsync(ms.AsRandomAccessStream(), CanvasBitmapFileFormat.Jpeg, 0.6f).AsTask(cancellationToken).ConfigureAwait(false);
                ms.Position = 0;
                await SaveStreamToFileAsync(cachePath, ms, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                effects.ForEach(x => x.Dispose());
            }

            try
            {
                File.Delete(legacyCachePath);
            }
            catch { }
        }, cancellationToken);
        return new BitmapImage(new Uri(cachePath));
    }




    private static async Task<BitmapSource> LoadImageAsync(string filePath, string cachePath, CancellationToken cancellationToken = default)
    {
        using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> buffer = stackalloc byte[21];
        fs.ReadExactly(buffer);
        fs.Position = 0;
        bool png = buffer.Slice(12, 4).SequenceEqual("VP8X"u8) && ((buffer[20] & 0x10) == 0x10);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(fs.AsRandomAccessStream()).AsTask(cancellationToken);
        (uint width, uint height) = GetThumbnailSize(decoder.PixelWidth, decoder.PixelHeight);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, decoder.BitmapAlphaMode, new BitmapTransform
        {
            ScaledWidth = width,
            ScaledHeight = height,
            InterpolationMode = BitmapInterpolationMode.Fant,
        }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var writableBitmap = new WriteableBitmap(softwareBitmap.PixelWidth, softwareBitmap.PixelHeight);
        softwareBitmap.CopyToBuffer(writableBitmap.PixelBuffer);
        SaveCachedImage(cachePath, softwareBitmap, png, cancellationToken);
        return writableBitmap;
    }




    private static async void SaveCachedImage(string path, SoftwareBitmap bitmap, bool png, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            var tmp = path + "_tmp";
            using var fs = File.Create(tmp);
            BitmapEncoder encoder = png ? await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs.AsRandomAccessStream()).AsTask(cancellationToken).ConfigureAwait(false)
                                        : await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, fs.AsRandomAccessStream(), ImageQuality).AsTask(cancellationToken).ConfigureAwait(false);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            fs.Dispose();
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            bitmap.Dispose();
        }
    }



    private static (uint Width, uint Height) GetThumbnailSize(uint width, uint height)
    {
        const uint TargetWidth = 412;
        const uint TargetHeight = 232;
        if (width * TargetHeight > height * TargetWidth)
        {
            width = TargetHeight * width / height;
            height = TargetHeight;
        }
        else
        {
            height = TargetWidth * height / width;
            width = TargetWidth;
        }
        return (width, height);
    }



    public static void ClearThumbnailCache()
    {
        if (Directory.Exists(CacheFolder))
        {
            Directory.Delete(CacheFolder, true);
        }
    }





    private static readonly SemaphoreSlim VideoSemaphore = new(1);


    public static async Task<BitmapSource> GetVideoThumbnailAsync(string path, CancellationToken cancellation = default)
    {
        string fileName = GetCachedThumbnailName(path);
        string cachePath = Path.Combine(CacheFolder, fileName);
        if (File.Exists(cachePath))
        {
            return new BitmapImage(new Uri(cachePath));
        }
        await VideoSemaphore.WaitAsync(cancellation);
        try
        {
            if (Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase))
            {
                bool decoderInstalled = VP9Helper.IsVP9DecoderInstalled();
                bool highProfileOrRgb = VP9Helper.IsVP9HighProfileOrRGB(path);
                if (!decoderInstalled || highProfileOrRgb)
                {
                    return await GetVideoThumbnailFromMediaPlayerAsync(path, cachePath, cancellation);
                }
            }
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 216);
            await SaveStreamToFileAsync(cachePath, thumbnail.AsStream(), cancellation);
            return new BitmapImage(new(cachePath));
        }
        finally
        {
            VideoSemaphore.Release();
        }
    }


    private static async Task SaveStreamToFileAsync(string path, Stream stream, CancellationToken cancellation)
    {
        Directory.CreateDirectory(CacheFolder);
        var tmp = path + "_tmp";
        using var fs = File.Create(tmp);
        await stream.CopyToAsync(fs, cancellation);
        fs.Dispose();
        File.Move(tmp, path, true);
    }


    private static async Task<BitmapSource> GetVideoThumbnailFromMediaPlayerAsync(string path, string cachePath, CancellationToken cancellation = default)
    {
        try
        {
            Debug.WriteLine(path);
            VP9Helper.RegisterVP9Decoder();
            while (true)
            {
                // 挑不出能用的设备时照旧用默认设备，让真实错误抛出去
                (_, DeviceInformation? audioDevice) = await MediaAudioOutputHelper.SelectRenderDeviceAsync();
                string? audioDeviceId = audioDevice?.Id ?? MediaAudioOutputHelper.GetDefaultRenderDeviceId();
                try
                {
                    return await CaptureVideoThumbnailAsync(path, cachePath, audioDevice, cancellation);
                }
                catch (Exception ex) when (MediaAudioOutputHelper.IsAudioOutputError(ex) && MediaAudioOutputHelper.MarkBroken(audioDeviceId))
                {
                    // 音频输出设备建不了流（实测 0x8889000F）时静音取帧也会整段失败：记下这个设备，换一个设备再取。
                    // MarkBroken 只对新设备返回 true，重试一定有进展，不会死循环。
                    Debug.WriteLine(ex);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            throw;
        }
        finally
        {
            VP9Helper.UnregisterVP9Decoder();
        }
    }


    /// <summary>
    /// 用帧服务器模式的 MediaPlayer 取视频第一帧，存成缩略图缓存。
    /// </summary>
    /// <param name="path">视频文件。</param>
    /// <param name="cachePath">缩略图缓存路径。</param>
    /// <param name="audioDevice">音频输出设备；null 表示系统默认设备。</param>
    /// <param name="cancellation">取消令牌；另有 3 秒超时。</param>
    /// <returns>缩略图。</returns>
    private static async Task<BitmapSource> CaptureVideoThumbnailAsync(string path, string cachePath, DeviceInformation? audioDevice, CancellationToken cancellation)
    {
        MediaPlayer? player = null;
        try
        {
            var completion = new TaskCompletionSource();
            player = new MediaPlayer
            {
                IsMuted = true,
                IsVideoFrameServerEnabled = true,
            };
            if (audioDevice is not null)
            {
                player.AudioDevice = audioDevice;
            }
            player.Source = MediaSource.CreateFromUri(new Uri(path));
            player.CommandManager.IsEnabled = false;
            player.SystemMediaTransportControls.IsEnabled = false;
            player.VideoFrameAvailable += (s, e) => { s.Pause(); completion.TrySetResult(); };
            player.MediaFailed += (s, e) => completion.TrySetException(e.ExtendedErrorCode);
            player.Play();

            var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            tokenSource.CancelAfter(TimeSpan.FromSeconds(3));
            await completion.Task.WaitAsync(tokenSource.Token);

            int width = (int)player.PlaybackSession.NaturalVideoWidth;
            int height = (int)player.PlaybackSession.NaturalVideoHeight;
            using var renderTarget = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96);
            player.CopyFrameToVideoSurface(renderTarget);
            player.Dispose();
            player = null;

            var ms = new MemoryStream();
            await renderTarget.SaveAsync(ms.AsRandomAccessStream(), CanvasBitmapFileFormat.Jpeg, 0.6f);
            ms.Position = 0;
            await SaveStreamToFileAsync(cachePath, ms, cancellation);
            return new BitmapImage(new Uri(cachePath));
        }
        finally
        {
            player?.Dispose();
        }
    }


}
