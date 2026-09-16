using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Starward.Features.Codec;

/// <summary>
/// Media Foundation 的最小互操作定义，供 <see cref="VideoTranscodeService"/> 用 SourceReader + SinkWriter 转码背景视频。
/// <para/>
/// 只声明用得到的方法，其余 vtable 槽位用 <c>SlotNN</c> 占位——<b>顺序必须与 COM 接口完全一致</b>，
/// 少一个或错一个都会调用到别的函数上。新增方法时务必核对 Windows SDK 头文件中的声明顺序。
/// </summary>
internal static partial class MediaFoundation
{

    /// <summary>MF_VERSION，<c>MFStartup</c> 要求的版本号。</summary>
    public const uint MF_VERSION = 0x00020070;

    public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;

    public const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;

    public const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;

    /// <summary>ReadSample 输出的流状态标志：已到流尾。</summary>
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x00000002;

    /// <summary>eAVEncH264VProfile_High，H.264 High Profile。</summary>
    public const uint eAVEncH264VProfile_High = 100;

    /// <summary>MFVideoInterlace_Progressive。</summary>
    public const uint MFVideoInterlace_Progressive = 2;

    // 属性 GUID
    public static Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    public static Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static Guid MF_MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");

    // 媒体类型 GUID
    public static Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static Guid MFVideoFormat_NV12 = new("3231564E-0000-0010-8000-00AA00389B71");

    /// <summary>
    /// MFVideoFormat_RGB32，即 D3DFMT_X8R8G8B8（值 22 = 0x16）。
    /// 注意 GUID 的首段是 D3DFORMAT 数值的十六进制写法，写成 0x22 会直接得到 MF_E_INVALIDMEDIATYPE。
    /// </summary>
    public static Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");


    /// <summary>把两个 32 位值打包进 UINT64 属性（高位在前），用于帧尺寸、帧率、像素宽高比。</summary>
    public static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;

    /// <summary>拆开 <see cref="Pack"/> 打包的 UINT64 属性。</summary>
    public static (uint High, uint Low) Unpack(ulong value) => ((uint)(value >> 32), (uint)(value & 0xFFFFFFFF));


    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateMediaType(out IMFMediaType mediaType);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MFCreateSourceReaderFromURL(string url, IMFAttributes? attributes, out IMFSourceReader reader);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MFCreateSinkWriterFromURL(string url, nint byteStream, IMFAttributes? attributes, out IMFSinkWriter writer);

}


/// <summary>IMFAttributes，媒体类型与各种配置对象的公共基接口。</summary>
[GeneratedComInterface, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
internal partial interface IMFAttributes
{
    void Slot01(); void Slot02(); void Slot03(); void Slot04(); void Slot05();
    void GetUINT64(ref Guid key, out ulong value);          // 6
    void Slot07();
    void GetGUID(ref Guid key, out Guid value);             // 8
    void Slot09(); void Slot10(); void Slot11(); void Slot12(); void Slot13();
    void Slot14(); void Slot15(); void Slot16(); void Slot17(); void Slot18();
    void SetUINT32(ref Guid key, uint value);               // 19
    void SetUINT64(ref Guid key, ulong value);              // 20
    void Slot21();
    void SetGUID(ref Guid key, ref Guid value);             // 22
    void Slot23(); void Slot24(); void Slot25(); void Slot26();
    void Slot27(); void Slot28(); void Slot29(); void Slot30();
}


/// <summary>IMFMediaType，本文件只用到继承自 <see cref="IMFAttributes"/> 的属性读写。</summary>
[GeneratedComInterface, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
internal partial interface IMFMediaType : IMFAttributes
{
}


/// <summary>IMFSample，解码输出的一帧。</summary>
[GeneratedComInterface, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
internal partial interface IMFSample : IMFAttributes
{
    void Slot31(); void Slot32(); void Slot33(); void Slot34();
    void Slot35(); void Slot36(); void Slot37(); void Slot38();
    void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);   // 39
}


/// <summary>IMFMediaBuffer，样本的数据缓冲区。</summary>
[GeneratedComInterface, Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
internal partial interface IMFMediaBuffer
{
    void Slot01();                          // Lock
    void Slot02();                          // Unlock
    void GetCurrentLength(out uint length);  // 3
}


/// <summary>
/// IMFSourceReader，读取并解码媒体文件。
/// 与 <c>MediaTranscoder</c> 不同，它会使用 <c>MFTRegisterLocal</c> 注册到当前进程的解码器，
/// 因此能解开官方扩展不支持的 VP9 Profile 1 / RGB 视频。
/// </summary>
[GeneratedComInterface, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
internal partial interface IMFSourceReader
{
    void Slot01();                                                                      // GetStreamSelection
    void SetStreamSelection(uint streamIndex, int selected);                            // 2
    void GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IMFMediaType mediaType); // 3
    void GetCurrentMediaType(uint streamIndex, out IMFMediaType mediaType);             // 4
    void SetCurrentMediaType(uint streamIndex, nint reserved, IMFMediaType mediaType);  // 5
    void Slot06();                                                                      // SetCurrentPosition
    [PreserveSig]
    int ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex,
                   out uint streamFlags, out long timestamp, out IMFSample? sample);    // 7
}


/// <summary>IMFSinkWriter，用系统内置（可硬件加速的）编码器写出文件。</summary>
[GeneratedComInterface, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
internal partial interface IMFSinkWriter
{
    void AddStream(IMFMediaType targetMediaType, out uint streamIndex);                                    // 1
    void SetInputMediaType(uint streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters); // 2
    void BeginWriting();                                                                                   // 3
    void WriteSample(uint streamIndex, IMFSample sample);                                                   // 4
    void Slot05(); void Slot06(); void Slot07(); void Slot08();
    void FinalizeWriting();                                                                                // 9 (IMFSinkWriter::Finalize)
}
