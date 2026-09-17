using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Starward.Features.Codec;

/// <summary>
/// HEVC（H.265）视频的解码能力检测。
/// <para>
/// Windows 本身不带 HEVC 解码器，要靠商店的 HEVC 视频扩展，或核显驱动自带的硬件解码 MFT。
/// 缺解码器时 MediaPlayer 不会触发 MediaFailed：视频轨静默打不开，只放音轨、进度照走，画面始终出不来，
/// 所以播放前要自己检查。绝区零百科较新的好感壁纸就是 HEVC。
/// </para>
/// </summary>
public static class HevcHelper
{

    /// <summary>「来自设备制造商的 HEVC 视频扩展」商店页（免费）。</summary>
    public const string VideoExtensionStoreUrl = "https://apps.microsoft.com/detail/9n4wgh0z6vhq";

    /// <summary>查到没有解码器之后，至少隔这么久才重查，好让用户装完扩展回来能生效。</summary>
    private const long DecoderRecheckIntervalMs = 5000;

    /// <summary>moov 读入内存的上限；正常视频的 moov 只有几百 KB。</summary>
    private const int MaxMoovBytes = 16 * 1024 * 1024;

    /// <summary>Matroska 只在文件头这么大的范围内找 CodecID，Tracks 段一般就在开头。</summary>
    private const int MatroskaHeaderReadSize = 1 << 20;

    private const uint BoxMoov = 0x6D6F6F76; // moov
    private const uint BoxTrak = 0x7472616B; // trak
    private const uint BoxMdia = 0x6D646961; // mdia
    private const uint BoxMinf = 0x6D696E66; // minf
    private const uint BoxStbl = 0x7374626C; // stbl
    private const uint BoxStsd = 0x73747364; // stsd
    private const uint SampleEntryHvc1 = 0x68766331; // hvc1
    private const uint SampleEntryHev1 = 0x68657631; // hev1


    private static readonly Lock _decoderLock = new();

    /// <summary>上次的查询结果；null 表示还没查过。</summary>
    private static bool? _decoderAvailable;

    private static long _decoderCheckedAt;

    /// <summary>按完整路径缓存的判定结果；文件大小或修改时间变了就重新解析。</summary>
    private static readonly ConcurrentDictionary<string, HevcFileState> _fileCache = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// 是否为「HEVC 视频、但系统里没有 HEVC 解码器」，也就是播出来只有声音、没有画面。
    /// 先看扩展名、再查解码器、最后才解析文件：正常机器上只会查一次解码器，不会逐个读文件。
    /// </summary>
    /// <param name="filePath">视频文件完整路径。</param>
    /// <returns>需要拦下不播时返回 true。</returns>
    public static bool IsHevcDecoderRequiredButMissing(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !MayContainHevc(filePath))
        {
            return false;
        }
        return !IsHevcDecoderAvailable() && IsHevcVideoFile(filePath);
    }


    /// <summary>
    /// 系统里是否有能解 HEVC 的解码器，商店扩展与驱动注册的硬件 MFT 都算。
    /// 查到有就一直记住；没有时最多每 <see cref="DecoderRecheckIntervalMs"/> 毫秒重查一次，可以在 UI 线程上频繁调用。
    /// 首次查询实测 30~50ms；查询期间持锁，并发调用方等同一次结果，不会重复枚举。
    /// </summary>
    /// <returns>有解码器，或者查询本身失败时返回 true。</returns>
    public static bool IsHevcDecoderAvailable()
    {
        lock (_decoderLock)
        {
            if (_decoderAvailable is true)
            {
                return true;
            }
            if (_decoderAvailable is false && Environment.TickCount64 - _decoderCheckedAt < DecoderRecheckIntervalMs)
            {
                return false;
            }
            bool available = QueryHevcDecoder();
            _decoderAvailable = available;
            _decoderCheckedAt = Environment.TickCount64;
            return available;
        }
    }


    /// <summary>
    /// 视频文件的视频轨是否为 HEVC。MP4 / MOV 看 stsd 里的样本条目（hvc1 / hev1），Matroska 看 CodecID。
    /// 只读文件头和 moov、不解码；结果按路径缓存，随机模式挑壁纸时会对整个候选池调用。
    /// </summary>
    /// <param name="filePath">视频文件完整路径。</param>
    /// <returns>是 HEVC 返回 true；其他编码、文件不存在或解析失败返回 false。</returns>
    public static bool IsHevcVideoFile(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return false;
            }
            if (_fileCache.TryGetValue(info.FullName, out HevcFileState cached)
                && cached.Length == info.Length
                && cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
            {
                return cached.IsHevc;
            }
            bool isHevc = DetectHevc(info.FullName);
            _fileCache[info.FullName] = new HevcFileState(info.Length, info.LastWriteTimeUtc, isHevc);
            return isHevc;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 只有这几种容器可能装 HEVC；WebM 规定只能是 VP8 / VP9 / AV1，不必读文件。
    /// </summary>
    private static bool MayContainHevc(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 用 MFTEnumEx 枚举 HEVC 解码器，非打包进程也能枚举到商店扩展。
    /// 不用 WinRT 的 CodecQuery：实测它重复查询不到 1ms，应是缓存了结果，装完扩展后未必能及时查到；
    /// MFTEnumEx 每次调用都要 20~30ms，是真的重新枚举了一遍。
    /// </summary>
    /// <returns>找到至少一个解码器，或查询失败时返回 true。</returns>
    private static bool QueryHevcDecoder()
    {
        try
        {
            var input = new MediaFoundation.MFT_REGISTER_TYPE_INFO
            {
                guidMajorType = MediaFoundation.MFMediaType_Video,
                guidSubtype = MediaFoundation.MFVideoFormat_HEVC,
            };
            int hr = MediaFoundation.MFTEnumEx(MediaFoundation.MFT_CATEGORY_VIDEO_DECODER, MediaFoundation.MFT_ENUM_FLAG_PLAYBACK_DECODERS, input, 0, out nint activates, out uint count);
            if (hr < 0)
            {
                return true;
            }
            if (activates != 0)
            {
                for (int i = 0; i < count; i++)
                {
                    Marshal.Release(Marshal.ReadIntPtr(activates, i * IntPtr.Size));
                }
                Marshal.FreeCoTaskMem(activates);
            }
            return count > 0;
        }
        catch
        {
            // 查不出来就不拦：宁可退回原来的行为，也别把能播的视频误判成播不了。
            return true;
        }
    }


    /// <summary>
    /// 按文件头分派：EBML 魔数是 Matroska，其余按 ISO BMFF（MP4 / MOV）解析。
    /// </summary>
    private static bool DetectHevc(string filePath)
    {
        // 允许共享写和删除：文件可能正被背景播放，删除壁纸缓存时也不能被这里挡住。
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.RandomAccess);
        Span<byte> magic = stackalloc byte[4];
        if (fs.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length)
        {
            return false;
        }
        if (magic.SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
        {
            return IsMatroskaHevc(fs);
        }
        return IsIsoBmffHevc(fs);
    }


    /// <summary>
    /// Matroska 视频轨的 CodecID 为 <c>V_MPEGH/ISO/HEVC</c> 即 HEVC，做法同 <see cref="VP9Helper.IsVP8VideoFile"/>。
    /// </summary>
    private static bool IsMatroskaHevc(FileStream fs)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(MatroskaHeaderReadSize);
        try
        {
            fs.Position = 0;
            int read = fs.ReadAtLeast(rented.AsSpan(0, MatroskaHeaderReadSize), MatroskaHeaderReadSize, throwOnEndOfStream: false);
            return rented.AsSpan(0, read).IndexOf("V_MPEGH/ISO/HEVC"u8) >= 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }


    /// <summary>
    /// 逐个跳过顶层 box 找到 moov，读入内存后查找 HEVC 样本条目。
    /// moov 在 faststart 的文件里排在 mdat 前面，否则在文件末尾，所以不能只读文件头。
    /// </summary>
    private static bool IsIsoBmffHevc(FileStream fs)
    {
        long fileLength = fs.Length;
        long offset = 0;
        Span<byte> header = stackalloc byte[16];
        while (offset + 8 <= fileLength)
        {
            fs.Position = offset;
            if (fs.ReadAtLeast(header[..8], 8, throwOnEndOfStream: false) < 8)
            {
                return false;
            }
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            int headerSize = 8;
            if (size == 1)
            {
                // 64 位 largesize，大于 4GB 的 mdat 会用到
                if (fs.ReadAtLeast(header[8..], 8, throwOnEndOfStream: false) < 8)
                {
                    return false;
                }
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = fileLength - offset;
            }
            if (size < headerSize || size > fileLength - offset)
            {
                // 不是 MP4，或文件被截断
                return false;
            }
            if (type == BoxMoov)
            {
                int payloadSize = (int)Math.Min(size - headerSize, int.MaxValue);
                if (payloadSize > MaxMoovBytes)
                {
                    return false;
                }
                byte[] moov = ArrayPool<byte>.Shared.Rent(payloadSize);
                try
                {
                    fs.ReadExactly(moov, 0, payloadSize);
                    return ContainsHevcSampleEntry(moov.AsSpan(0, payloadSize), 0);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(moov);
                }
            }
            offset += size;
        }
        return false;
    }


    /// <summary>
    /// 沿 trak → mdia → minf → stbl → stsd 查找 HEVC 样本条目。
    /// </summary>
    /// <param name="data">容器 box 的内容（不含 box 头）。</param>
    /// <param name="depth">当前嵌套层数，防止构造出来的深层嵌套把栈撑爆。</param>
    private static bool ContainsHevcSampleEntry(ReadOnlySpan<byte> data, int depth)
    {
        while (data.Length >= 8)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
            int headerSize = 8;
            if (size == 1)
            {
                if (data.Length < 16)
                {
                    return false;
                }
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[8..]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = data.Length;
            }
            if (size < headerSize || size > data.Length)
            {
                return false;
            }
            ReadOnlySpan<byte> payload = data[headerSize..(int)size];
            if (type is BoxTrak or BoxMdia or BoxMinf or BoxStbl)
            {
                if (depth < 8 && ContainsHevcSampleEntry(payload, depth + 1))
                {
                    return true;
                }
            }
            else if (type == BoxStsd && HasHevcSampleEntry(payload))
            {
                return true;
            }
            data = data[(int)size..];
        }
        return false;
    }


    /// <summary>
    /// stsd 是 FullBox：版本与标志占 4 字节、条目数占 4 字节，之后每个样本条目都以 size + type 开头。
    /// </summary>
    private static bool HasHevcSampleEntry(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return false;
        }
        uint count = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
        ReadOnlySpan<byte> entries = payload[8..];
        for (uint i = 0; i < count && entries.Length >= 8; i++)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(entries);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(entries[4..]);
            if (type is SampleEntryHvc1 or SampleEntryHev1)
            {
                return true;
            }
            if (size < 8 || size > entries.Length)
            {
                break;
            }
            entries = entries[(int)size..];
        }
        return false;
    }


    /// <summary>
    /// 文件判定结果的缓存项。
    /// </summary>
    /// <param name="Length">判定时的文件大小。</param>
    /// <param name="LastWriteTimeUtc">判定时的修改时间。</param>
    /// <param name="IsHevc">视频轨是否为 HEVC。</param>
    private readonly record struct HevcFileState(long Length, DateTime LastWriteTimeUtc, bool IsHevc);

}
