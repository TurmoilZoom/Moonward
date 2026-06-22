using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT;

namespace Starward.Features.Background;

/// <summary>
/// 从背景图像像素数据中提取强调色（Accent Color），并更新应用全局主题颜色资源。
/// 用于让应用强调色跟随自定义背景图变化。
/// </summary>
internal static class AccentColorHelper
{

    /// <summary>
    /// 从 BGRA 字节数组中提取强调色（每 2 像素采样一次以提高性能）。
    /// </summary>
    /// <param name="bgra">BGRA8 格式的像素数据（B、G、R、A 各 1 字节）</param>
    /// <param name="width">图像宽度（像素）</param>
    /// <param name="height">图像高度（像素）</param>
    /// <returns>提取出的强调色，失败时返回 null</returns>
    public static unsafe Color? GetAccentColor(byte[] bgra, int width, int height)
    {
        if (bgra.Length % 4 == 0)
        {
            fixed (byte* ptr = bgra)
            {
                return GetAccentColorInternal(ptr, width, height);
            }
        }
        return null;
    }

    /// <summary>
    /// 从 IBuffer（通常来自 WriteableBitmap.PixelBuffer）中提取强调色。
    /// </summary>
    public static unsafe Color? GetAccentColor(IBuffer buffer, int width, int height)
    {
        int length = (int)buffer.Length;
        if (length > 0 && length % 4 == 0)
        {
            if (buffer.As<IBufferByteAccess>().Buffer(out nint ptr) == 0)
            {
                return GetAccentColorInternal((void*)ptr, width, height);
            }
        }
        return null;
    }



    /// <summary>
    /// 从原始指针和容量中提取强调色（用于 SoftwareBitmap 的内存缓冲）。
    /// </summary>
    public static unsafe Color? GetAccentColor(nint bufferPtr, uint capacity, int width, int height)
    {
        if (capacity > 0 && capacity % 4 == 0)
        {
            return GetAccentColorInternal((void*)bufferPtr, width, height);
        }
        return null;
    }


    /// <summary>
    /// 核心提取逻辑：
    /// 1. 每隔一行、一个像素进行采样（降低计算量）
    /// 2. 累加 B/G/R 分量并计算平均值，得到基础颜色
    /// 3. 将平均色转为 HSV，强制饱和度为 0.6，保留原亮度，产生更适合作为强调色的颜色
    /// 注意：本方法中的 hueCircle 数组目前未被使用（可能是历史遗留）。
    /// </summary>
    private static unsafe Color? GetAccentColorInternal(void* bgra, int width, int height)
    {
        try
        {
            uint* p = (uint*)bgra;
            long b = 0, g = 0, r = 0;
            int[] hueCircle = new int[360]; // 当前版本未使用
            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 2)
                {
                    Bgra32 pixel = Unsafe.AsRef<Bgra32>(p);
                    b += pixel.B;
                    g += pixel.G;
                    r += pixel.R;
                    p += 2;
                }
                p += width - width % 2;
            }

            int c = (width / 2) * (height / 2);
            Unsafe.SkipInit(out Color color);
            color.B = (byte)(b / c);
            color.G = (byte)(g / c);
            color.R = (byte)(r / c);
            color.A = 255;
            HsvColor hsv = color.ToHsv();

            // 使用原图的色相(H) 和 明度(V)，饱和度固定为 0.6，得到鲜明但不刺眼的强调色
            return CommunityToolkit.WinUI.Helpers.ColorHelper.FromHsv(hsv.H, 0.6, hsv.V);
        }
        catch { }
        return null;
    }




    /// <summary>
    /// 颜色混合工具：按百分比在 input 与 blend 之间线性插值。
    /// </summary>
    private static Color ColorMix(Color input, Color blend, double percent)
    {
        return Color.FromArgb(255,
                              (byte)(input.R * percent + blend.R * (1 - percent)),
                              (byte)(input.G * percent + blend.G * (1 - percent)),
                              (byte)(input.B * percent + blend.B * (1 - percent)));
    }



    /// <summary>
    /// 将提取到的颜色应用到当前 Application 的主题资源中，
    /// 生成 SystemAccentColor 及其 Light1/2/3、Dark1/2/3 变体，
    /// 然后通过 Messenger 通知界面刷新。
    /// </summary>
    public static void ChangeAppAccentColor(Color? color)
    {
        if (color is null)
        {
            return;
        }

        // 生成浅色变体（与白色混合）
        Color light1 = ColorMix(color.Value, Colors.White, 0.8);
        Color light2 = ColorMix(color.Value, Colors.White, 0.6);
        Color light3 = ColorMix(color.Value, Colors.White, 0.4);
        // 生成深色变体（与黑色混合）
        Color dark1 = ColorMix(color.Value, Colors.Black, 0.8);
        Color dark2 = ColorMix(color.Value, Colors.Black, 0.6);
        Color dark3 = ColorMix(color.Value, Colors.Black, 0.4);

        Application.Current.Resources["SystemAccentColor"] = color;
        Application.Current.Resources["SystemAccentColorLight1"] = light1;
        Application.Current.Resources["SystemAccentColorLight2"] = light2;
        Application.Current.Resources["SystemAccentColorLight3"] = light3;
        Application.Current.Resources["SystemAccentColorDark1"] = dark1;
        Application.Current.Resources["SystemAccentColorDark2"] = dark2;
        Application.Current.Resources["SystemAccentColorDark3"] = dark3;

        WeakReferenceMessenger.Default.Send(new AccentColorChangedMessage());
    }




    /// <summary>
    /// WinRT IBuffer 字节访问 COM 接口，用于直接获取像素缓冲区指针。
    /// </summary>
    [ComImport]
    [Guid("905a0fef-bc53-11df-8c49-001e4fc686da")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBufferByteAccess
    {
        int Buffer([Out] out nint value);
    }



    /// <summary>
    /// IMemoryBuffer 字节访问 COM 接口，用于从 SoftwareBitmap 的 BitmapBuffer 获取原始指针。
    /// 在 AppBackground 中通过 memoryBufferReference.As&lt;...&gt;() 调用。
    /// </summary>
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer([Out] out nint buffer, [Out] out uint capacity);
    }



    /// <summary>
    /// BGRA32 像素的非安全结构体，用于按字节访问颜色分量。
    /// 注意字段顺序是 B, G, R, A（与内存布局一致）。
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    private readonly struct Bgra32
    {
        [FieldOffset(0)] public readonly byte B;
        [FieldOffset(1)] public readonly byte G;
        [FieldOffset(2)] public readonly byte R;
        [FieldOffset(3)] public readonly byte A;
    }



    /// <summary>
    /// 将 BGRA 像素转换为色相值（0-359）。
    /// 当前 AccentColorHelper 版本中此方法未被调用（历史遗留代码）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Bgra32ToHue(in Bgra32 bgra)
    {
        byte max = Math.Max(Math.Max(bgra.R, bgra.G), bgra.B);
        byte min = Math.Min(Math.Min(bgra.R, bgra.G), bgra.B);
        float chroma = max - min;
        float h;

        if (chroma <= 8)
        {
            // ignore white black gray
            h = -1;
        }
        else if (max == bgra.R)
        {
            h = (((bgra.G - bgra.B) / chroma) + 6) % 6;
        }
        else if (max == bgra.G)
        {
            h = 2 + ((bgra.B - bgra.R) / chroma);
        }
        else
        {
            h = 4 + ((bgra.R - bgra.G) / chroma);
        }
        return (int)(h * 60);
    }


}
