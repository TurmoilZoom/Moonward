using Microsoft.Win32;
using Starward.Codec.VP9Decoder;
using System;
using System.Buffers;
using System.IO;

namespace Starward.Features.Codec;

/// <summary>
/// VP9 / Vorbis 解码器注册辅助类。
/// 通过 Starward.Codec 包提供的本地 MFT 注册机制，为 MediaPlayer 提供 WebM 视频所需的 VP9 软件解码能力，
/// 并检测是否已安装微软官方 VP9 Video Extensions 以便给出性能优化建议。
/// </summary>
public partial class VP9Helper
{


    /// <summary>当前进程中 VP9 解码器 MFT 是否已通过本类注册。</summary>
    public static bool VP9MFTRegistered { get; private set; }

    /// <summary>当前进程中 Vorbis 解码器 MFT 是否已通过本类注册。</summary>
    public static bool VorbisMFTRegistered { get; private set; }

    /// <summary>标记是否有视频背景路径注册过 VP9 解码器，用于注销时的保护逻辑。</summary>
    private static bool _videoBackground;


    /// <summary>
    /// 注册 VP9 解码器 MFT（Media Foundation Transform），供 MediaPlayer 解码 .webm 等 VP9 视频。
    /// 实际使用 Starward.Codec 包中基于 libvpx 1.16.1 的软件解码器实现。
    /// </summary>
    /// <param name="videoBackground">是否由视频背景使用。用于控制注销时机：视频背景注册的解码器在某些情况下不被非背景路径注销。</param>
    public static void RegisterVP9Decoder(bool videoBackground = false)
    {
        try
        {
            if (!VP9MFTRegistered)
            {
                // 调用本地注册 API，将 libvpx VP9 解码器注册到当前进程的 MF 中（无需管理员权限）
                int hr = VP9Decoder.RegisterVP9DecoderLocal();
                if (hr >= 0)
                {
                    VP9MFTRegistered = true;
                }
            }
            if (videoBackground)
            {
                _videoBackground = true;
            }
        }
        catch { }
    }


    /// <summary>
    /// 注销已注册的 VP9 解码器 MFT。
    /// </summary>
    /// <param name="videoBackground">是否由视频背景调用。非视频背景调用时，若当前由视频背景注册过，则跳过注销，避免影响正在播放的背景视频。</param>
    public static void UnregisterVP9Decoder(bool videoBackground = false)
    {
        try
        {
            if (_videoBackground && !videoBackground)
            {
                return;
            }
            if (VP9MFTRegistered)
            {
                // 注销本地 MFT，释放相关资源
                int hr = VP9Decoder.UnregisterVP9DecoderLocal();
            }
            VP9MFTRegistered = false;
        }
        catch { }
    }


    /// <summary>
    /// 注册 Vorbis 音频解码器 MFT。
    /// WebM 容器通常使用 Vorbis 或 Opus 音频，Windows 原生对 Vorbis 支持有限，因此需要注册。
    /// </summary>
    public static void RegisterVorbisDecoder()
    {
        try
        {
            if (!VorbisMFTRegistered)
            {
                int hr = VP9Decoder.RegisterVorbisDecoderLocal();
                if (hr >= 0)
                {
                    VorbisMFTRegistered = true;
                }
            }
        }
        catch { }
    }


    /// <summary>
    /// 注销 Vorbis 音频解码器 MFT。
    /// </summary>
    public static void UnregisterVorbisDecoder()
    {
        try
        {
            if (VorbisMFTRegistered)
            {
                int hr = VP9Decoder.UnregisterVorbisDecoderLocal();
            }
            VorbisMFTRegistered = false;
        }
        catch { }
    }


    /// <summary>
    /// 检查系统中是否已安装微软官方的 VP9 Video Extensions（商店应用）。
    /// 该扩展可提供更高效的 VP9 解码（通常包含硬件加速路径），能显著降低 CPU 占用。
    /// </summary>
    /// <returns>已安装返回 true，否则 false。出现异常时返回 false。</returns>
    public static bool IsVP9DecoderInstalled()
    {
        try
        {
            // 通过注册表检查 Appx 包列表中是否存在 Microsoft.VP9VideoExtensions
            var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (packages is not null)
            {
                string[] names = packages.GetSubKeyNames();
                foreach (var item in names)
                {
                    if (item.StartsWith("Microsoft.VP9VideoExtensions"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }



    /// <summary>
    /// 快速判断给定文件是否为 VP9 编码的高 Profile（Profile 1/2/3）或 RGB 像素格式的 WebM 视频。
    /// 仅解析容器头和少量帧头，不进行完整解封装或解码，性能开销极小。
    /// </summary>
    /// <param name="filePath">待检测的视频文件完整路径。</param>
    /// <returns>如果是高 Profile 或 RGB VP9 返回 true；否则（包括非 WebM、解析失败）返回 false。</returns>
    public static bool IsVP9HighProfileOrRGB(string filePath)
    {
        const int ReadSize = 1 << 20;
        byte[]? rented = null;
        try
        {
            using var fs = File.OpenRead(filePath);
            rented = ArrayPool<byte>.Shared.Rent(ReadSize);
            int read = fs.Read(rented, 0, ReadSize);
            ReadOnlySpan<byte> buffer = rented.AsSpan(0, read);

            if (!IsWebMFile(buffer))
            {
                return false;
            }

            // 在第一个数据块中查找 VP9 SimpleBlock/Block 并解析 profile 信息
            while (buffer.Length > 0)
            {
                int index = buffer.IndexOfAny((byte)0xA1, (byte)0xA3);
                if (index < 0)
                {
                    break;
                }
                buffer = buffer.Slice(index + 1);
                // 跳过 block size (EBML VINT)
                if (!SkipEBMLVInt(buffer, out int offset))
                {
                    continue;
                }
                buffer = buffer.Slice(offset);
                // 跳过 track number (EBML VINT)
                if (!SkipEBMLVInt(buffer, out offset))
                {
                    continue;
                }
                buffer = buffer.Slice(offset);
                // 跳过 timecode (2 bytes) 和 flags (1 byte)
                if (buffer.Length < 3)
                {
                    continue;
                }
                buffer = buffer.Slice(3);

                if (buffer.Length > 0)
                {
                    var flags = ParseVP9ProfileFlags(buffer);
                    if (flags.IsValid)
                    {
                        return flags.IsHighProfile || flags.IsRgb;
                    }
                    // 未能成功解析当前块的 profile，继续尝试下一个块
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }


    /// <summary>
    /// 快速判断给定文件是否为 VP8 编码的 WebM 视频（而非 VP9）。
    /// 仅做轻量级字节匹配，不完整解析容器。
    /// </summary>
    /// <param name="filePath">视频文件路径。</param>
    /// <returns>是 VP8 WebM 返回 true，否则 false。</returns>
    public static bool IsVP8VideoFile(string filePath)
    {
        const int ReadSize = 1 << 20;
        byte[]? rented = null;
        try
        {
            using var fs = File.OpenRead(filePath);
            rented = ArrayPool<byte>.Shared.Rent(ReadSize);
            int read = fs.Read(rented, 0, ReadSize);
            ReadOnlySpan<byte> buffer = rented.AsSpan(0, read);

            if (!IsWebMFile(buffer))
            {
                return false;
            }

            // 在 Tracks 段中查找 CodecID 元素 (0x86) 包含 "V_VP8" 的记录
            return buffer.IndexOf("V_VP8"u8) >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }


    /// <summary>
    /// 轻量判断缓冲区起始是否为有效的 WebM 容器（通过 EBML 头和 DocType 检查）。
    /// </summary>
    private static bool IsWebMFile(ReadOnlySpan<byte> buffer)
    {
        // EBML 头魔数：0x1A 0x45 0xDF 0xA3
        ReadOnlySpan<byte> ebmlMagic = [0x1A, 0x45, 0xDF, 0xA3];
        if (!buffer.StartsWith(ebmlMagic))
        {
            return false;
        }
        // DocType 元素：ID=0x4282，固定 4 字节值 "webm"
        ReadOnlySpan<byte> docTypePrefix = [0x42, 0x82, 0x84];
        int index = buffer.IndexOf(docTypePrefix);
        if (index < 0 || index + docTypePrefix.Length + 4 > buffer.Length)
        {
            return false;
        }
        return buffer.Slice(index + docTypePrefix.Length, 4).SequenceEqual("webm"u8);
    }


    /// <summary>
    /// 解析 VP9 帧头中的 profile 和 color config，判断是否为高 Profile 或 RGB 格式。
    /// 严格按照 VP9 比特流规范顺序读取位字段。
    /// </summary>
    /// <param name="buffer">包含 VP9 帧起始数据的缓冲区。</param>
    private static VP9ProfileFlags ParseVP9ProfileFlags(ReadOnlySpan<byte> buffer)
    {
        var reader = new BitReader(buffer);

        // frame_marker 必须为 0b10（最高 2 位）
        if (!reader.TryReadBits(2, out int frameMarker) || frameMarker != 2)
        {
            return default;
        }

        if (!reader.TryReadBits(2, out int profileBits))
        {
            return default;
        }

        int profile = ((profileBits & 0x01) << 1) | ((profileBits >> 1) & 0x01);
        if (profile == 3 && !reader.TryReadBit(out _))
        {
            return default;
        }

        if (!reader.TryReadBit(out bool showExistingFrame))
        {
            return default;
        }
        if (showExistingFrame)
        {
            return new VP9ProfileFlags(true, profile != 0, false);
        }

        if (!reader.TryReadBit(out bool keyFrame)
            || !reader.TryReadBit(out bool showFrame)
            || !reader.TryReadBit(out bool errorResilientMode))
        {
            return default;
        }

        bool intraOnly = false;
        if (!keyFrame)
        {
            if (!showFrame)
            {
                if (!reader.TryReadBit(out intraOnly))
                {
                    return default;
                }
            }

            if (!errorResilientMode)
            {
                if (!reader.TryReadBits(2, out _))
                {
                    return default;
                }
            }
        }

        bool hasColorConfig = keyFrame || intraOnly;
        if (!hasColorConfig)
        {
            return new VP9ProfileFlags(true, profile != 0, false);
        }

        // 同步码必须为 0x498342
        if (!reader.TryReadBits(24, out int syncCode) || syncCode != 0x498342)
        {
            return default;
        }

        if (profile >= 2 && !reader.TryReadBit(out _))
        {
            return default;
        }

        if (!reader.TryReadBits(3, out int colorSpace))
        {
            return default;
        }

        bool isRgb = false;
        if (colorSpace == 7)
        {
            if (profile is not (1 or 3))
            {
                return default;
            }
            if (!reader.TryReadBit(out _)
                || !reader.TryReadBit(out _))
            {
                return default;
            }
            isRgb = true;
        }

        return new VP9ProfileFlags(true, profile != 0, isRgb);
    }


    /// <summary>
    /// 跳过 EBML 格式中的一个 Variable Integer (VINT)，返回其长度。
    /// 用于在 Matroska/WebM 容器中跳过元素大小、轨道号等变长整数。
    /// </summary>
    /// <param name="buffer">当前读取位置的缓冲区。</param>
    /// <param name="offset">成功时输出该 VINT 占用的字节数。</param>
    /// <returns>成功跳过返回 true；缓冲区不足返回 false。</returns>
    private static bool SkipEBMLVInt(ReadOnlySpan<byte> buffer, out int offset)
    {
        offset = 0;
        if (buffer.Length == 0)
        {
            return false;
        }
        byte firstByte = buffer[0];
        for (int i = 0; i < 8; i++)
        {
            if ((firstByte & (0x80 >> i)) != 0)
            {
                offset = i + 1;
                return offset <= buffer.Length;
            }
        }
        return false;
    }


    /// <summary>
    /// VP9 Profile 解析结果。
    /// </summary>
    /// <param name="IsValid">是否成功解析出有效信息。</param>
    /// <param name="IsHighProfile">是否为高 Profile（非 Profile 0）。</param>
    /// <param name="IsRgb">颜色空间是否为 RGB（color_space == 7）。</param>
    private readonly record struct VP9ProfileFlags(bool IsValid, bool IsHighProfile, bool IsRgb);


    /// <summary>
    /// 按位读取器，用于解析 VP9 帧头中的位字段（无字节对齐）。
    /// </summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _bitOffset;

        public BitReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _bitOffset = 0;
        }

        /// <summary>
        /// 尝试读取 1 位。
        /// </summary>
        public bool TryReadBit(out bool value)
        {
            if (!TryReadBits(1, out int bits))
            {
                value = false;
                return false;
            }
            value = bits != 0;
            return true;
        }

        /// <summary>
        /// 尝试读取指定数量的位（最高 bitCount 位，结果存放在 value 的低位）。
        /// </summary>
        /// <param name="bitCount">要读取的位数。</param>
        public bool TryReadBits(int bitCount, out int value)
        {
            value = 0;
            if (bitCount <= 0)
            {
                return true;
            }

            for (int i = 0; i < bitCount; i++)
            {
                int byteOffset = _bitOffset >> 3;
                if (byteOffset >= _buffer.Length)
                {
                    value = 0;
                    return false;
                }

                int bitInByte = 7 - (_bitOffset & 0x07);
                int bit = (_buffer[byteOffset] >> bitInByte) & 0x01;
                value = (value << 1) | bit;
                _bitOffset++;
            }
            return true;
        }
    }


}
