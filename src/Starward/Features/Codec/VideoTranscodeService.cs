using Microsoft.Extensions.Logging;
using Starward.Features.Overlay;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
/// <para/>
/// 产物与原片同放在 bg 目录，命名为「原文件名 + .mp4」。播放端改用产物后，校验产物完整就删掉 bg 里的原片，
/// 此后产物就是这个背景本身；设置里记的仍是原片文件名，判断背景文件在不在要用 <see cref="TryGetTranscodedFile"/> 兜住只剩产物的情况。
/// </summary>
internal partial class VideoTranscodeService
{

    /// <summary>2026.9.6-beta1 存放产物的旧子目录（bg\transcoded），首次转码前会把里面的产物挪回 bg。</summary>
    private const string LegacyTranscodedFolderName = "transcoded";

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

    /// <summary>本次运行中产物没通过校验、不再尝试删除的原片。</summary>
    private readonly ConcurrentDictionary<string, byte> _keptSources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>正在排队校验并删除的原片，同一文件只排一次。</summary>
    private readonly ConcurrentDictionary<string, byte> _deletingSources = new(StringComparer.OrdinalIgnoreCase);

    private static bool _mediaFoundationStarted;

    private int _foldersSwept;


    public VideoTranscodeService(ILogger<VideoTranscodeService> logger)
    {
        _logger = logger;
    }


    /// <summary>
    /// 返回本次播放应当使用的视频文件：已有可用的转码产物就用产物，并在后台校验后删掉原片；否则原样返回。不触发转码。
    /// 只给马上要播放的调用方用；只想知道文件在不在，用 <see cref="TryGetTranscodedFile"/>。
    /// </summary>
    /// <param name="file">原始视频文件完整路径，原片可能已被删除、只剩产物。</param>
    /// <returns>应当交给播放器的文件路径；任何异常情况下都退回 <paramref name="file"/>。</returns>
    public string GetPlaybackFile(string file)
    {
        try
        {
            if (TryGetTranscodedFile(file, out string? target))
            {
                DeleteSourceInBackground(file, target);
                return target;
            }
            return file;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get playback file '{file}'", file);
            return file;
        }
    }


    /// <summary>
    /// 查找源文件对应的可用转码产物。只查文件，不触发转码、不删原片，可在任意线程调用。
    /// </summary>
    /// <param name="file">原始视频文件完整路径，原片本身可以已经不存在。</param>
    /// <param name="target">可用的转码产物路径。</param>
    /// <returns>有可用产物时返回 true。</returns>
    public static bool TryGetTranscodedFile([NotNullWhen(true)] string? file, [NotNullWhen(true)] out string? target)
    {
        target = null;
        if (!IsWebmFile(file))
        {
            // 只有 webm 才可能是解不动的 VP9；mp4 / mkv 一般本来就能硬解。
            return false;
        }
        string path = GetTranscodedFilePath(file);
        if (!IsTranscodedFileUsable(file, path))
        {
            return false;
        }
        target = path;
        return true;
    }


    /// <summary>
    /// 源文件对应的转码产物路径：与源文件同目录，「源文件名 + .mp4」。不检查文件是否存在，只对 webm 有意义。
    /// </summary>
    /// <param name="file">原始视频文件完整路径。</param>
    public static string GetTranscodedFilePath(string file)
    {
        return $"{file}.mp4";
    }


    /// <summary>
    /// 等转码完成并返回本次播放应使用的文件：已有可用产物就直接返回；否则当场排队转码并等待完成，
    /// 成功后返回 H.264 产物，失败或被跳过则退回源文件（由播放端继续 libvpx 软解）。返回产物时会在后台校验后删掉原片。
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
        if (!IsWebmFile(file))
        {
            return file;
        }
        string target = GetTranscodedFilePath(file);
        if (IsTranscodedFileUsable(file, target))
        {
            DeleteSourceInBackground(file, target);
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
            if (IsTranscodedFileUsable(file, target))
            {
                DeleteSourceInBackground(file, target);
                return target;
            }
            return file;
        }
        catch (TimeoutException)
        {
            // 这次改播原片，所以转码写完时不能顺手删它，留到下次改播产物时再删
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
    /// 整理转码用到的目录：清掉上次运行残留的临时文件；把 2026.9.6-beta1 放在 bg\transcoded 的产物挪回 bg，再删掉旧目录。
    /// 只在本次运行第一次转码前调用，此时持有 <see cref="_semaphore"/>，不会有转码正在写临时文件。
    /// </summary>
    private void SweepFolders()
    {
        try
        {
            string tempFolder = GetTempFolder();
            if (Directory.Exists(tempFolder))
            {
                foreach (string item in Directory.GetFiles(tempFolder))
                {
                    File.Delete(item);
                }
            }
            string bgFolder = Path.Join(AppConfig.CacheFolder, "bg");
            string legacyFolder = Path.Join(bgFolder, LegacyTranscodedFolderName);
            if (!Directory.Exists(legacyFolder))
            {
                return;
            }
            foreach (string item in Directory.GetFiles(legacyFolder))
            {
                // 产物名是「源文件名 + .mp4」，去掉尾缀就还原成源文件名；
                // 临时文件（.partial.mp4）和源文件已删的孤儿产物还原不出存在的源文件，直接删。
                string target = Path.Combine(bgFolder, Path.GetFileName(item));
                if (File.Exists(Path.Combine(bgFolder, Path.GetFileNameWithoutExtension(item))) && !File.Exists(target))
                {
                    File.Move(item, target);
                }
                else
                {
                    File.Delete(item);
                }
            }
            Directory.Delete(legacyFolder);
            _logger.LogInformation("Moved transcoded videos out of legacy folder '{folder}'", legacyFolder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sweep transcoded video folders");
        }
    }


    /// <summary>
    /// 转码临时文件目录：CacheFolder\cache\transcode。不能放 bg，否则转码时打开背景图库会列出写到一半的文件。
    /// </summary>
    private static string GetTempFolder()
    {
        return Path.Join(AppConfig.CacheFolder, "cache", "transcode");
    }


    /// <summary>文件扩展名是否为 .webm。</summary>
    private static bool IsWebmFile([NotNullWhen(true)] string? file)
    {
        return !string.IsNullOrEmpty(file) && Path.GetExtension(file).Equals(".webm", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 产物存在、非空，且不早于源文件（源被同名替换过就重转）。原片已删时 <c>File.GetLastWriteTimeUtc</c> 返回 1601 年，只看产物本身。
    /// </summary>
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
                    if (Interlocked.Exchange(ref _foldersSwept, 1) == 0)
                    {
                        SweepFolders();
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

        string tempFolder = GetTempFolder();
        Directory.CreateDirectory(tempFolder);
        // 临时文件也必须以 .mp4 结尾：MFCreateSinkWriterFromURL 按扩展名挑封装器，
        // 给个 .tmp 会直接返回 MF_E_NOT_FOUND。
        string temp = Path.Combine(tempFolder, $"{Path.GetFileName(source)}.partial.mp4");
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
    /// 在后台校验转码产物完整后删掉 bg 里的原片，省一份磁盘，背景图库里也不会出现两份。
    /// 只删 bg 目录里的文件：导入自定义背景时用户选的那份原文件在别处，不受影响。
    /// <para/>
    /// 只在播放端已经拿到产物路径时调用，这时不会再有人去读原片。若在转码刚写完时删，
    /// 等待超时后改播原片的那一次播放可能正要打开它。Media Foundation 打开文件时允许删除
    /// （实测 SourceReader 仍持有文件时照样删得掉），所以不必等 COM 对象被回收。
    /// </summary>
    /// <param name="source">原片完整路径。</param>
    /// <param name="target">可用的转码产物路径。</param>
    private void DeleteSourceInBackground(string source, string target)
    {
        if (_keptSources.ContainsKey(source) || !File.Exists(source) || !IsInBackgroundFolder(source) || !_deletingSources.TryAdd(source, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                // 与转码共用信号量：校验也要开解码器，别和正在进行的转码抢
                await _semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    DeleteSourceCore(source, target);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                // 删不掉（被其他程序占用等）不影响播放，下次播放到它时再删
                _logger.LogWarning(ex, "Delete source video '{file}' after transcoding", Path.GetFileName(source));
            }
            finally
            {
                _deletingSources.TryRemove(source, out _);
            }
        });
    }


    /// <summary>
    /// 校验转码产物，通过后删除原片。产物有问题时保留原片，本次运行不再尝试。
    /// </summary>
    /// <param name="source">原片完整路径。</param>
    /// <param name="target">转码产物路径。</param>
    /// <exception cref="IOException">原片被占用，删除失败。</exception>
    /// <exception cref="UnauthorizedAccessException">没有删除原片的权限。</exception>
    private void DeleteSourceCore(string source, string target)
    {
        if (!File.Exists(source) || !IsTranscodedFileUsable(source, target))
        {
            return;
        }
        int sourceFrames, targetFrames;
        bool decodable;
        try
        {
            EnsureMediaFoundationStarted();
            sourceFrames = CountVideoSamples(source);
            targetFrames = CountVideoSamples(target);
            decodable = CanDecodeFirstFrame(target);
        }
        catch (Exception ex)
        {
            _keptSources.TryAdd(source, 0);
            _logger.LogWarning(ex, "Verify transcoded video '{name}' failed, keep the source", Path.GetFileName(source));
            return;
        }
        // 上游 libvpx MFT 结束时不 drain 最后一帧，产物固定比原片少一帧；少得更多说明产物不完整，原片删了就找不回来
        if (targetFrames <= 0 || targetFrames < sourceFrames - 1 || !decodable)
        {
            _keptSources.TryAdd(source, 0);
            _logger.LogWarning("Transcoded video '{name}' failed verification ({targetFrames}/{sourceFrames} frames, decodable: {decodable}), keep the source",
                               Path.GetFileName(source), targetFrames, sourceFrames, decodable);
            return;
        }
        File.Delete(source);
        _logger.LogInformation("Deleted source video '{name}' after transcoding ({targetFrames}/{sourceFrames} frames)",
                               Path.GetFileName(source), targetFrames, sourceFrames);
    }


    /// <summary>文件是否直接位于 bg 目录（即背景图库里）。</summary>
    private static bool IsInBackgroundFolder(string file)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty;
        string bgFolder = Path.GetFullPath(Path.Join(AppConfig.CacheFolder, "bg"));
        return string.Equals(Path.TrimEndingDirectorySeparator(folder), Path.TrimEndingDirectorySeparator(bgFolder), StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 只解封装、不解码，数出第一条视频轨的帧数。16MB 的 1080p60 webm 实测约 60ms。
    /// </summary>
    /// <param name="file">视频文件完整路径。</param>
    /// <returns>视频帧数。</returns>
    /// <exception cref="COMException">打不开文件或读取出错。</exception>
    /// <exception cref="InvalidOperationException">读取中途报告流错误。</exception>
    private static int CountVideoSamples(string file)
    {
        Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateSourceReaderFromURL(file, null, out IMFSourceReader reader));
        reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_ALL_STREAMS, 0);
        reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 1);
        int count = 0;
        while (true)
        {
            // 没设输出类型，读到的是压缩数据，不会加载解码器
            int hr = reader.ReadSample(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0,
                                       out _, out uint streamFlags, out _, out IMFSample? sample);
            Marshal.ThrowExceptionForHR(hr);
            if ((streamFlags & MediaFoundation.MF_SOURCE_READERF_ERROR) != 0)
            {
                throw new InvalidOperationException($"Source reader error after {count} samples.");
            }
            if ((streamFlags & MediaFoundation.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                return count;
            }
            if (sample is not null)
            {
                count++;
            }
        }
    }


    /// <summary>
    /// 本机能否解出转码产物的第一帧（系统 H.264 解码器，可硬解）。
    /// </summary>
    /// <param name="file">转码产物路径。</param>
    /// <returns>解出第一帧返回 true；没有可用的解码器或文件损坏时返回 false。</returns>
    private static bool CanDecodeFirstFrame(string file)
    {
        try
        {
            Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateAttributes(out IMFAttributes attributes, 1));
            attributes.SetUINT32(ref MediaFoundation.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateSourceReaderFromURL(file, attributes, out IMFSourceReader reader));
            reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_ALL_STREAMS, 0);
            reader.SetStreamSelection(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 1);
            // H.264 解码器原生输出 NV12；要 RGB32 得另开视频处理（实测直接要会 MF_E_INVALIDMEDIATYPE），这里只关心解不解得开
            Marshal.ThrowExceptionForHR(MediaFoundation.MFCreateMediaType(out IMFMediaType wantType));
            wantType.SetGUID(ref MediaFoundation.MF_MT_MAJOR_TYPE, ref MediaFoundation.MFMediaType_Video);
            wantType.SetGUID(ref MediaFoundation.MF_MT_SUBTYPE, ref MediaFoundation.MFVideoFormat_NV12);
            reader.SetCurrentMediaType(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, wantType);
            while (true)
            {
                int hr = reader.ReadSample(MediaFoundation.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0,
                                           out _, out uint streamFlags, out _, out IMFSample? sample);
                if (hr < 0 || (streamFlags & (MediaFoundation.MF_SOURCE_READERF_ERROR | MediaFoundation.MF_SOURCE_READERF_ENDOFSTREAM)) != 0)
                {
                    return false;
                }
                if (sample is not null)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
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
