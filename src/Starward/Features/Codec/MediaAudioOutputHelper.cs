using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.Render;

namespace Starward.Features.Codec;

/// <summary>
/// 视频播放的音频输出设备兜底。
/// <para>
/// MediaPlayer 没法不选音轨（AudioTracks.SelectedIndex = -1 会抛 ArgumentException），静音、音量 0 也照样要在输出设备上建音频流。
/// 默认播放设备建不了流时整段播放直接 MediaFailed，画面也出不来。实测：手机用过 AirPods 再连回电脑后，
/// 第三方 A2DP 驱动的立体声端点仍显示已连接、仍是默认设备，但任何程序在它上面建流都报 0x8889000F（AUDCLNT_E_ENDPOINT_CREATE_FAILED），
/// 同一时刻其他播放设备都正常。这时只能给 MediaPlayer.AudioDevice 指定另一个能建流的设备。
/// </para>
/// <para>
/// 建不了流的设备记在进程级名单里，视频背景、好感壁纸预览、视频缩略图共用：一处试出来，别处不必再先失败一次。
/// 名单只在 <see cref="ProbeDefaultRenderDeviceAsync"/> 确认默认设备恢复后清空。
/// </para>
/// </summary>
public static class MediaAudioOutputHelper
{

    /// <summary>
    /// 音频端点所属设备的实例路径（MMDevice 端点属性，系统未公开命名），形如 <c>{1}.BTHHFENUM\BTHHFPAUDIO\8&amp;8AC112E&amp;0&amp;97</c>。
    /// 设备枚举 API 能直接取到，不必另走 IMMDevice。
    /// </summary>
    private const string EndpointDeviceInstanceProperty = "{b3f8fa53-0004-438e-9003-51a46e139bfc} 2";

    /// <summary>PKEY_AudioEndpoint_FormFactor。</summary>
    private const string EndpointFormFactorProperty = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e} 0";

    /// <summary>EndpointFormFactor.Headset：带麦克风的耳机端点，蓝牙耳机的免提（通话）端点就是这一类。</summary>
    private const uint FormFactorHeadset = 5;

    /// <summary>微软蓝牙免提（HFP）音频的枚举器。</summary>
    private const string HandsFreeEnumerator = "BTHHFENUM";


    private static readonly Lock _lock = new();

    /// <summary>建不了音频流的播放设备 ID。</summary>
    private static readonly HashSet<string> _brokenDeviceIds = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// 是否为音频输出设备建流失败（AUDCLNT_E_*，HRESULT 高 16 位为 0x8889，实测 0x8889000F）。
    /// 与视频能否解码无关，不该提示「视频解码失败」。
    /// </summary>
    /// <param name="error">MediaFailed 的 ExtendedErrorCode 或捕获到的异常。</param>
    public static bool IsAudioOutputError(Exception? error)
    {
        return error is not null && ((uint)error.HResult >> 16) == 0x8889;
    }


    /// <summary>
    /// 系统默认（多媒体）播放设备的 ID。
    /// </summary>
    /// <returns>没有播放设备或查询失败时返回 null。</returns>
    public static string? GetDefaultRenderDeviceId()
    {
        try
        {
            string id = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// 取播放设备的显示名，用于写日志。
    /// </summary>
    /// <param name="deviceId">设备 ID。</param>
    /// <returns>查不到时返回 null。</returns>
    public static async Task<string?> GetDeviceNameAsync(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }
        try
        {
            DeviceInformation device = await DeviceInformation.CreateFromIdAsync(deviceId);
            return device.Name;
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// 记下建不了音频流的播放设备，之后挑设备时跳过它。
    /// </summary>
    /// <param name="deviceId">播放失败时所用设备的 ID；跟随默认设备时传开播那一刻的默认设备，失败时默认设备可能已经换了。</param>
    /// <returns>是否为新记下的设备。返回 false 说明换设备重试已没有进展，调用方应停止重试，否则会来回循环。</returns>
    public static bool MarkBroken(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }
        lock (_lock)
        {
            return _brokenDeviceIds.Add(deviceId);
        }
    }


    /// <summary>
    /// 为新建的 MediaPlayer 挑选音频输出设备。默认设备没被记为建不了流时直接跟随默认设备，不做任何枚举。
    /// </summary>
    /// <returns>
    /// Available 为 false：默认设备和全部备用设备都建不了流，调用方应改显示静态画面。
    /// Device 为 null：跟随系统默认设备；否则须设到 MediaPlayer.AudioDevice，且音量应强制为 0——
    /// 用户设的音量只对默认设备有意义，别让声音从意料之外的设备里出来。
    /// </returns>
    public static async Task<(bool Available, DeviceInformation? Device)> SelectRenderDeviceAsync()
    {
        string? defaultId = GetDefaultRenderDeviceId();
        if (defaultId is null || !IsBroken(defaultId))
        {
            return (true, null);
        }
        IReadOnlyList<DeviceInformation> devices = await FindFallbackRenderDevicesAsync();
        return devices.Count > 0 ? (true, devices[0]) : (false, null);
    }


    /// <summary>
    /// 列出可代替默认设备的播放设备：排除已记为建不了流的设备和蓝牙免提端点，非蓝牙设备排在前面。
    /// 免提端点不能用：切过去会把耳机拖进通话模式，用户其他程序的音质跟着变差。
    /// </summary>
    /// <returns>按优先顺序排列的设备；枚举失败时为空。</returns>
    public static async Task<IReadOnlyList<DeviceInformation>> FindFallbackRenderDevicesAsync()
    {
        try
        {
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector(),
                                                                                       [EndpointDeviceInstanceProperty, EndpointFormFactorProperty]);
            lock (_lock)
            {
                return devices.Where(x => x.IsEnabled && !_brokenDeviceIds.Contains(x.Id) && !IsBluetoothHandsFree(x))
                              .OrderBy(x => IsBluetooth(x) ? 1 : 0)
                              .ToList();
            }
        }
        catch
        {
            return [];
        }
    }


    /// <summary>
    /// 用 AudioGraph 在系统默认播放设备上试建一次音频流（不出声，实测失败约 60 ms），确认它现在能不能用。
    /// 能用时清空建不了流的设备名单，之后新建的播放器回到默认设备。
    /// </summary>
    /// <returns>默认设备能建流时返回 true。</returns>
    public static async Task<bool> ProbeDefaultRenderDeviceAsync()
    {
        try
        {
            CreateAudioGraphResult result = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Other));
            result.Graph?.Dispose();
            if (result.Status is not AudioGraphCreationStatus.Success)
            {
                return false;
            }
            lock (_lock)
            {
                _brokenDeviceIds.Clear();
            }
            return true;
        }
        catch
        {
            return false;
        }
    }


    private static bool IsBroken(string deviceId)
    {
        lock (_lock)
        {
            return _brokenDeviceIds.Contains(deviceId);
        }
    }


    /// <summary>
    /// 是否为蓝牙耳机的免提（通话）端点：微软免提驱动的端点一律算；其他蓝牙端点里，外形为带麦耳机的也算（第三方蓝牙驱动的免提端点）。
    /// 有线 / USB 耳机不在此列，切过去不会改变任何工作模式。
    /// </summary>
    private static bool IsBluetoothHandsFree(DeviceInformation device)
    {
        string? enumerator = GetEndpointEnumerator(device);
        if (string.Equals(enumerator, HandsFreeEnumerator, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return IsBluetooth(enumerator)
            && device.Properties.TryGetValue(EndpointFormFactorProperty, out object? value)
            && value is uint formFactor
            && formFactor == FormFactorHeadset;
    }


    private static bool IsBluetooth(DeviceInformation device)
    {
        return IsBluetooth(GetEndpointEnumerator(device));
    }


    private static bool IsBluetooth(string? enumerator)
    {
        return enumerator?.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ?? false;
    }


    /// <summary>
    /// 从端点所属设备的实例路径里取枚举器名，例如 <c>{1}.BTHENUM\{0000110B-...}</c> 取 BTHENUM、<c>{1}.USB\VID_...</c> 取 USB。
    /// </summary>
    /// <returns>取不到时返回 null。</returns>
    private static string? GetEndpointEnumerator(DeviceInformation device)
    {
        if (!device.Properties.TryGetValue(EndpointDeviceInstanceProperty, out object? value) || value is not string path)
        {
            return null;
        }
        int start = 0;
        if (path.StartsWith('{'))
        {
            // 去掉开头的 {1}.
            start = path.IndexOf("}.", StringComparison.Ordinal) + 2;
            if (start < 2)
            {
                return null;
            }
        }
        int end = path.IndexOf('\\', start);
        return end > start ? path[start..end] : null;
    }


}
