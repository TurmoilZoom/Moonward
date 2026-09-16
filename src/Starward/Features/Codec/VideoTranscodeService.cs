using Microsoft.Extensions.Logging;
using Starward.Features.Overlay;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Codec;

/// <summary>
/// 把无法硬件解码的背景视频（VP9 Profile 1 / RGB）在后台一次性转成 H.264 MP4，之后的播放走硬件解码。
/// <para/>
/// 米哈游部分官方背景由 After Effects 直出为 VP9 Profile 1 + RGB 4:4:4，消费级 GPU 与官方 VP9 商店扩展都解不了，
/// 只能用 <see cref="VP9Helper"/> 注册的 libvpx 软件解码器，1080p60 要占掉大半个核心。
/// <para/>
/// 转码走 Media Foundation 的 SourceReader + SinkWriter：SourceReader 会使用 <c>MFTRegisterLocal</c>
/// 注册到本进程的 libvpx 解码器（<c>MediaTranscoder</c> 不会），SinkWriter 用系统内置（通常硬件加速的）H.264 编码器，
/// 因此不需要引入任何第三方编解码二进制。
/// </summary>
internal partial class VideoTranscodeService
{

    /// <summary>转码产物所在的子目录，放在 bg 下面，不会污染背景图列表（那边用的是非递归枚举）。</summary>
    public const string TranscodedFolderName = "transcoded";

    /// <summary>单个文件的转码上限，超时视为失败并丢弃产物。</summary>
    private static readonly TimeSpan TranscodeTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 首播最多等转码这么久，超时先交给 libvpx 软解播放，转码留在后台继续。
    /// 官方背景（5 秒 1080p60）硬件编码约 3 秒、多核软件编码 2.5–7.6 秒，只有单线程软件编码或自定义长视频会超时。
    /// </summary>
    private static readonly TimeSpan MaxPlaybackWait = TimeSpan.FromSeconds(10);

    private readonly ILogger<VideoTranscodeService> _logger;

    /// <summary>同一时刻只转一个文件，避免抢占用户的 CPU 与编码器。</summary>
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>本次运行中已判定「不需要或转不了」的源文件，避免每次播放都重试。</summary>
    private readonly ConcurrentDictionary<string, byte> _skipped = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>每个源文件正在排队或转码中的任务，完成后移除；同一源文件的多个等待方共享同一次转码。</summary>
    private readonly ConcurrentDictionary<string, Lazy<Task>> _transcodeTasks = new(StringComparer.OrdinalIgnoreCase);

    private static bool _mediaFoundationStarted;

    private int _orphanSwept;


    public VideoTranscodeService(ILogger<VideoTranscodeService> logger)
    {
        _logger = logger;
    }


    /// <summary>
    /// 返回本次播放应当使用的视频文件：已有可用的转码产物就用产物，否则原样返回。不触发转码。
    /// </summary>
    /// <param name="file">原始视频文件完整路径。</param>
    /// <returns>应当交给播放器的文件路径；任何异常情况下都退回 <paramref name="file"/>。</returns>
    public string GetPlaybackFile(string file)
    {
        try
        {
            if (string.IsNullOrEmpty(file) || !Path.GetExtension(file).Equals(".webm", StringComparison.OrdinalIgnoreCase))
            {
                // 只有 webm 才可能是解不动的 VP9；mp4 / mkv 一般本来就能硬解。
                return file;
            }
            string target = GetTranscodedFilePath(file);
            return IsTranscodedFileUsable(file, target) ? target : file;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get playback file '{file}'", file);
            return file;
        }
    }


    /// <summary>
    /// 等转码完成并返回本次播放应使用的文件：已有可用产物就直接返回；否则当场排队转码并等待完成，
    /// 成功后返回 H.264 产物，失败或被跳过则退回源文件（由播放端继续 libvpx 软解）。
    /// 最多等 <see cref="MaxPlaybackWait"/>（含排队时间），超时同样退回源文件，转码留在后台继续，下次播放直接用产物。
    /// <para/>
    /// 必须在播放端已经用 <see cref="VP9Helper.RegisterVP9Decoder"/> 注册 libvpx 之后调用：转码借用这份注册，自己从不注册。
    /// 进程内注册的解码器优先级高于官方 VP9 扩展，若由转码服务在后台注册，会截走随后打开的 Profile 0 播放器
    /// （实测解码从 2% 升到 22% 单核）；而由它注销又可能拆掉背景正在用的注册。
    /// </summary>
    /// <param name="file">原始视频文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌。取消时立即停止等待并抛出 <see cref="OperationCanceledException"/>；转码本身留在后台继续，下次播放直接用产物。</param>
    /// <returns>应交给播放器的文件路径（转码产物或源文件）。</returns>
    public async Task<string> EnsureTranscodedAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file) || !Path.GetExtension(file).Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }
        string target = GetTranscodedFilePath(file);
        if (IsTranscodedFileUsable(file, target))
        {
            return target;
        }
        if (_skipped.ContainsKey(file))
        {
            return file;
        }
        Task task = GetOrStartTranscode(file, target);
        try
        {
            // 取消与超时都只结束「等待」，不打断转码：背景切走或先软解播放后，转码照常写完，别白转一半
            await task.WaitAsync(MaxPlaybackWait, cancellationToken).ConfigureAwait(false);
            return IsTranscodedFileUsable(file, target) ? target : file;
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("Transcode '{file}' not finished within {seconds}s, play the source first", Path.GetFileName(file), MaxPlaybackWait.TotalSeconds);
            return file;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ensure transcoded '{file}'", file);
            return file;
        }
    }


    /// <summary>
    /// 删除源文件已不存在的转码产物。整个目录都是可再生的缓存，删错也只是下次重转。
    /// </summary>
    public void CleanupOrphans()
    {
        try
        {
            string folder = GetTranscodedFolder();
            if (!Directory.Exists(folder))
            {
                return;
            }
            string bgFolder = Path.Join(AppConfig.CacheFolder, "bg");
            foreach (string item in Directory.GetFiles(folder, "*.mp4"))
            {
                // 产物名是「源文件名 + .mp4」，去掉尾缀就还原成源文件名。
                string sourceName = Path.GetFileNameWithoutExtension(item);
                if (!File.Exists(Path.Combine(bgFolder, sourceName)))
                {
                    File.Delete(item);
                    _logger.LogInformation("Deleted orphan transcoded video '{name}'", Path.GetFileName(item));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cleanup orphan transcoded videos");
        }
    }


    /// <summary>转码产物所在目录：CacheFolder\bg\transcoded。</summary>
    public static string GetTranscodedFolder()
    {
        return Path.Join(AppConfig.CacheFolder, "bg", TranscodedFolderName);
    }


    /// <summary>源文件对应的转码产物路径。</summary>
    private static string GetTranscodedFilePath(string file)
    {
        return Path.Combine(GetTranscodedFolder(), $"{Path.GetFileName(file)}.mp4");
    }


    /// <summary>产物存在、非空，且不早于源文件（源被同名替换过就重转）。</summary>
    private static bool IsTranscodedFileUsable(string source, string target)
    {
        try
        {
            var info = new FileInfo(target);
            return info.Exists && info.Length > 0 && info.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(source);
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 返回源文件对应的转码任务；没有进行中的任务就新建一个。同一源文件的多个等待方共享同一次转码。
    /// </summary>
    /// <param name="source">原始视频文件完整路径。</param>
    /// <param name="target">转码产物路径。</param>
    /// <returns>排队或进行中的转码任务。</returns>
    private Task GetOrStartTranscode(string source, string target)
    {
        // 先登记、后启动：Lazy 保证并发调用只启动一次，任务结束时只移除自己这一项；
        // 若先启动后登记，任务极快结束时会留下一个已完成的旧任务，挡住本次运行里之后的重试。
        Lazy<Task>? entry = null;
        entry = new Lazy<Task>(() => Task.Run(async () =>
        {
            try
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (Interlocked.Exchange(ref _orphanSwept, 1) == 0)
                    {
                        CleanupOrphans();
                    }
                    TranscodeCore(source, target);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                // 转码只是优化，失败就继续用原文件软解，不打扰用户。
                _skipped.TryAdd(source, 0);
                _logger.LogWarning(ex, "Transcode video background '{file}'", Path.GetFileName(source));
            }
            finally
            {
                _transcodeTasks.TryRemove(KeyValuePair.Create(source, entry!));
            }
        }));
        return _transcodeTasks.GetOrAdd(source, entry).Value;
    }


    /// <summary>
    /// 判断是否值得转码并执行转码。不值得转的会记入跳过表，本次运行不再重试。
    /// </summary>
    private void TranscodeCore(string source, string target)
    {
        if (!File.Exists(source) || IsTranscodedFileUsable(source, target))
        {
            return;
        }
        if (VP9Helper.IsVP8VideoFile(source) || !VP9Helper.IsVP9HighProfileOrRGB(source))
        {
            // Profile 0 的 VP9 本来就能硬解（装了官方扩展时），没必要多占一份磁盘。
            _skipped.TryAdd(source, 0);
            return;
        }
        if (RunningGameService.GetRunningGameCount() > 0)
        {
            // 游戏运行时不要抢 CPU 和编码器；不记入跳过表，下次播放时再试。
            return;
        }
        if (!VP9Helper.VP9MFTRegistered)
        {
            // 排队期间背景已经切走、libvpx 被注销，本次无从解码；不记入跳过表，下次播放到它时再转。
            // SourceReader 建好解码器实例之后再被注销则不受影响（实测照常写完全部帧）。
            return;
        }

        Directory.CreateDirectory(GetTranscodedFolder());
        // 临时文件也必须以 .mp4 结尾：MFCreateSinkWriterFromURL 按扩展名挑封装器，
        // 给个 .tmp 会直接返回 MF_E_NOT_FOUND。
        string temp = Path.Combine(GetTranscodedFolder(), $"{Path.GetFileName(source)}.partial.mp4");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            File.Delete(temp);
            TranscodeResult result = Transcode(source, temp);
            stopwatch.Stop();
            if (new FileInfo(temp).Length <= 0)
            {
                throw new InvalidOperationException("Transcoded file is empty.");
            }
            File.Move(temp, target, true);
            _logger.LogInformation("Transcoded video background '{name}': {width}x{height}@{fps:F2}({fpsSource}) {frames} frames in {seconds:F1}s",
                                   Path.GetFileName(source), result.Width, result.Height,
                                   (double)result.FpsNumerator / result.FpsDenominator, result.FpsSource,
                                   result.Frames, stopwatch.Elapsed.TotalSeconds);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            _skipped.TryAdd(source, 0);
            throw;
        }
    }


    /// <summary>
    /// 实际的转码：SourceReader 解码为 RGB32，SinkWriter 编码为 H.264 写入 MP4。
    /// </summary>
    /// <param name="source">源视频文件。</param>
    /// <param name="target">输出的 mp4 文件（临时名）。</param>
    /// <returns>写出的帧数与实际使用的视频格式。</returns>
    /// <exception cref="InvalidOperationException">源含音频轨、解码器输出格式与协商结果不符、格式非法或超时。</exception>
    private static TranscodeResult Transcode(string source, string target)
    {
        EnsureMediaFoundationStarted();
        // SourceReader 用的是播放端注册到本进程的 libvpx 解码器（见 EnsureTranscodedAsync 的说明），这里不自行注册。

        Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateAttributes(out IMFAttributes readerAttributes, 2));
        readerAttributes.SetUINT32(ref MediaFoundation.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1);
        readerAttributes.SetUINT32(ref MediaFoundation.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
        Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateSourceReaderFromURL(source, readerAttributes, out IMFSourceReader reader));

        reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_ALL_STREAMS, 0);
        if (HasAudioStream(reader))
        {
            // 只转视频轨会把声音丢掉，这类文件宁可继续软解。
            throw new InvalidOperationException("Source has an audio stream.");
        }
        reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 1);

        // 解 VP9 Profile 1 的 libvpx MFT 只输出 RGB32；若改请求 NV12，协商会「成功」但样本仍是 RGB32，
        // 交给编码器就是一整片错色画面，所以这里直接要 RGB32，由 SinkWriter 自己插转换器。
        Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateMediaType(out IMFMediaType wantType));
        wantType.SetGUID(ref MediaFoundation.MF_MT_MAJOR_TYPE, ref MediaFoundation.MFMediaType_Video);
        wantType.SetGUID(ref MediaFoundation.MF_MT_SUBTYPE, ref MediaFoundation.MFVideoFormat_RGB32);
        reader.SetCurrentMediaType(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, wantType);
        reader.GetCurrentMediaType(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out IMFMediaType inputType);

        inputType.GetUINT64(ref MediaFoundation.MF_MT_FRAME_SIZE, out ulong packedSize);
        (uint width, uint height) = MediaFoundation.Unpack(packedSize);
        // 帧率必须准确：编码器按输出类型声明的帧率取舍样本，给小了会被抽帧
        // （早期版本回落到 30，把 60fps 的背景直接抽掉一半）。
        // webm 的媒体类型上没有 MF_MT_FRAME_RATE，所以优先用另一个 reader 量出来的值——那是即将写入的样本的事实。
        string fpsSource = "samples";
        if (!TryProbeFrameRate(source, out uint fpsNumerator, out uint fpsDenominator))
        {
            fpsSource = "mediatype";
            if (!TryGetRatio(inputType, ref MediaFoundation.MF_MT_FRAME_RATE, out fpsNumerator, out fpsDenominator))
            {
                fpsSource = "native";
                if (!TryGetNativeFrameRate(reader, out fpsNumerator, out fpsDenominator))
                {
                    fpsSource = "default";
                    (fpsNumerator, fpsDenominator) = (30, 1);
                }
            }
        }
        if (width == 0 || height == 0 || fpsNumerator == 0 || fpsDenominator == 0)
        {
            throw new InvalidOperationException($"Invalid video format {width}x{height} @{fpsNumerator}/{fpsDenominator}.");
        }

        Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateAttributes(out IMFAttributes writerAttributes, 2));
        writerAttributes.SetUINT32(ref MediaFoundation.MF_SINK_WRITER_DISABLE_THROTTLING, 1);
        writerAttributes.SetUINT32(ref MediaFoundation.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
        Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateSinkWriterFromURL(target, 0, writerAttributes, out IMFSinkWriter writer));

        Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateMediaType(out IMFMediaType outputType));
        outputType.SetGUID(ref MediaFoundation.MF_MT_MAJOR_TYPE, ref MediaFoundation.MFMediaType_Video);
        outputType.SetGUID(ref MediaFoundation.MF_MT_SUBTYPE, ref MediaFoundation.MFVideoFormat_H264);
        outputType.SetUINT32(ref MediaFoundation.MF_MT_AVG_BITRATE, GetTargetBitrate(width, height, fpsNumerator, fpsDenominator));
        outputType.SetUINT32(ref MediaFoundation.MF_MT_INTERLACE_MODE, MediaFoundation.MFVideoInterlace_Progressive);
        outputType.SetUINT32(ref MediaFoundation.MF_MT_MPEG2_PROFILE, MediaFoundation.eAVEncH264VProfile_High);
        outputType.SetUINT64(ref MediaFoundation.MF_MT_FRAME_SIZE, MediaFoundation.Pack(width, height));
        outputType.SetUINT64(ref MediaFoundation.MF_MT_FRAME_RATE, MediaFoundation.Pack(fpsNumerator, fpsDenominator));
        outputType.SetUINT64(ref MediaFoundation.MF_MT_PIXEL_ASPECT_RATIO, MediaFoundation.Pack(1, 1));

        writer.AddStream(outputType, out uint streamIndex);
        writer.SetInputMediaType(streamIndex, inputType, null);
        writer.BeginWriting();

        long expectedLength = (long)width * height * 4;
        var stopwatch = Stopwatch.StartNew();
        int frames = 0;
        while (true)
        {
            int hr = reader.ReadSample(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0,
                                       out _, out uint streamFlags, out _, out IMFSample? sample);
            Marshal.ThrowExceptionForHR(hr);
            if ((streamFlags & MediaFoundation.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                break;
            }
            if (sample is null)
            {
                continue;
            }
            if (frames == 0)
            {
                VerifyFirstSample(sample, expectedLength);
            }
            writer.WriteSample(streamIndex, sample);
            frames++;
            if (stopwatch.Elapsed > TranscodeTimeout)
            {
                throw new InvalidOperationException($"Transcode timeout after {frames} frames.");
            }
        }
        writer.FinalizeWriting();
        return new TranscodeResult(frames, width, height, fpsNumerator, fpsDenominator, fpsSource);
    }


    /// <summary>一次转码的结果，仅用于日志。</summary>
    private readonly record struct TranscodeResult(int Frames, uint Width, uint Height, uint FpsNumerator, uint FpsDenominator, string FpsSource);


    /// <summary>读取媒体类型上以 UINT64 打包的比值属性（帧率、像素宽高比等）。</summary>
    private static bool TryGetRatio(IMFMediaType type, ref Guid key, out uint numerator, out uint denominator)
    {
        try
        {
            type.GetUINT64(ref key, out ulong packed);
            (numerator, denominator) = MediaFoundation.Unpack(packed);
            return numerator > 0 && denominator > 0;
        }
        catch
        {
            numerator = 0;
            denominator = 0;
            return false;
        }
    }


    /// <summary>从流的原生（未解码）媒体类型里取帧率。</summary>
    private static bool TryGetNativeFrameRate(IMFSourceReader reader, out uint numerator, out uint denominator)
    {
        try
        {
            reader.GetNativeMediaType(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out IMFMediaType nativeType);
            return TryGetRatio(nativeType, ref MediaFoundation.MF_MT_FRAME_RATE, out numerator, out denominator);
        }
        catch
        {
            numerator = 0;
            denominator = 0;
            return false;
        }
    }


    /// <summary>
    /// 另开一个 reader 解码开头若干帧，用样本时间戳量出真实帧率（只取时间戳，不保留帧）。
    /// webm 的时间戳按毫秒量化，窗口太短会量出 60.61 这种值，所以要取够帧数再吸附。
    /// </summary>
    /// <param name="source">源视频文件。</param>
    private static bool TryProbeFrameRate(string source, out uint numerator, out uint denominator)
    {
        const int ProbeSampleCount = 60;
        numerator = 0;
        denominator = 0;
        try
        {
            Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateAttributes(out IMFAttributes attributes, 1));
            attributes.SetUINT32(ref MediaFoundation.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1);
            Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateSourceReaderFromURL(source, attributes, out IMFSourceReader reader));
            reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_ALL_STREAMS, 0);
            reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 1);

            long first = -1, last = -1;
            int count = 0;
            while (count < ProbeSampleCount)
            {
                int hr = reader.ReadSample(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0,
                                           out _, out uint streamFlags, out long timestamp, out IMFSample? sample);
                if (hr < 0 || (streamFlags & MediaFoundation.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    break;
                }
                if (sample is null)
                {
                    continue;
                }
                if (first < 0)
                {
                    first = timestamp;
                }
                last = timestamp;
                count++;
            }
            if (count < 2 || last <= first)
            {
                return false;
            }
            // 时间戳单位是 100ns
            double fps = (count - 1) * 10_000_000.0 / (last - first);
            if (fps is <= 1 or >= 1000)
            {
                return false;
            }
            (numerator, denominator) = SnapFrameRate(fps);
            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 把量出来的帧率吸附到最接近的常见帧率。
    /// webm 的样本时间戳按毫秒量化，开头几帧算出来会有百分之一量级的偏差（60fps 会量成 60.61），
    /// 直接拿去当输出帧率会得到 30303/500 这种奇怪的值。
    /// </summary>
    private static (uint Numerator, uint Denominator) SnapFrameRate(double fps)
    {
        ReadOnlySpan<(uint Numerator, uint Denominator)> candidates =
        [
            (24000, 1001), (24, 1), (25, 1), (30000, 1001), (30, 1), (48, 1),
            (50, 1), (60000, 1001), (60, 1), (90, 1), (120, 1), (144, 1),
        ];
        (uint Numerator, uint Denominator) best = default;
        double bestError = double.MaxValue;
        foreach ((uint numerator, uint denominator) in candidates)
        {
            double candidate = (double)numerator / denominator;
            double error = Math.Abs(fps - candidate) / candidate;
            if (error < bestError)
            {
                bestError = error;
                best = (numerator, denominator);
            }
        }
        if (bestError < 0.02)
        {
            return best;
        }
        // 不在常见档位上，保留两位小数的精度
        return ((uint)Math.Round(fps * 100), 100u);
    }


    /// <summary>
    /// 校验解码器真实输出的字节数与协商出来的格式一致。
    /// 硬件解码路径下缓冲区长度可能为 0，这种情况跳过校验。
    /// </summary>
    private static void VerifyFirstSample(IMFSample sample, long expectedLength)
    {
        sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        buffer.GetCurrentLength(out uint length);
        if (length != 0 && length != expectedLength)
        {
            throw new InvalidOperationException($"Decoder output {length} bytes, expected {expectedLength}.");
        }
    }


    /// <summary>源文件是否含音频轨。</summary>
    private static bool HasAudioStream(IMFSourceReader reader)
    {
        try
        {
            reader.GetCurrentMediaType(MediaFoundation.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 按像素吞吐估目标码率。系统内置 H.264 编码器压缩效率一般，给低了背景会糊，1080p60 约 16Mbps。
    /// </summary>
    private static uint GetTargetBitrate(uint width, uint height, uint fpsNumerator, uint fpsDenominator)
    {
        double pixelsPerSecond = (double)width * height * fpsNumerator / fpsDenominator;
        double bitrate = pixelsPerSecond * 0.13;
        return (uint)Math.Clamp(bitrate, 4_000_000, 30_000_000);
    }


    /// <summary>
    /// 初始化 Media Foundation。不配对调用 MFShutdown——进程内还有正在播放的背景视频在用 MF，
    /// 提前关掉会把它们一起拖垮；进程退出时系统自会回收。
    /// </summary>
    private static void EnsureMediaFoundationStarted()
    {
        if (!_mediaFoundationStarted)
        {
            Marshal.ThrowExceptionForHR(MediaFoundation.MFStartup(MediaFoundation.MF_VERSION, 0));
            _mediaFoundationStarted = true;
        }
    }

}
