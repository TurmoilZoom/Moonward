using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using WinRT;

namespace Starward.Helpers;

/// <summary>
/// WinUI 3 无 DataTransferManager.GetForCurrentView，通过 IDataTransferManagerInterop 调起 Windows 原生分享面板。
/// 实现参考：https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/integrate-sharesheet-send
/// </summary>
internal static class ShareHelper
{

    // IDataTransferManager，传给 GetForWindow 的 riid。
    private static readonly Guid DataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);


    /// <summary>
    /// 获取与指定窗口关联的 <see cref="DataTransferManager"/>。
    /// </summary>
    /// <param name="windowHandle">目标窗口 HWND（须由 <c>WindowNative.GetWindowHandle</c> 取得）。</param>
    /// <returns>可用于注册 <see cref="DataTransferManager.DataRequested"/> 的实例。</returns>
    public static DataTransferManager GetForWindow(IntPtr windowHandle)
    {
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        Guid iid = DataTransferManagerIid;
        IntPtr managerPtr = interop.GetForWindow(windowHandle, ref iid);
        return MarshalInterface<DataTransferManager>.FromAbi(managerPtr);
    }


    /// <summary>
    /// 在指定窗口上显示 Windows 原生分享 UI。
    /// </summary>
    /// <param name="windowHandle">目标窗口 HWND（须由 <c>WindowNative.GetWindowHandle</c> 取得）。</param>
    public static void ShowShareUIForWindow(IntPtr windowHandle)
    {
        DataTransferManager.As<IDataTransferManagerInterop>().ShowShareUIForWindow(windowHandle);
    }



    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {

        IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);

        void ShowShareUIForWindow(IntPtr appWindow);

    }

}