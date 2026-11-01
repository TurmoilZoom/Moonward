using CommunityToolkit.WinUI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Vanara.PInvoke;
using Windows.Foundation;


namespace Starward.Helpers;


/// <summary>
/// 让列表里的文件项在管理员权限下也能用鼠标拖到其他程序。
/// <para>WinUI 的拖放在非打包应用提权后不启动（microsoft-ui-xaml issue 7690），所以列表的 CanDragItems 在管理员下是关闭的。
/// 但 Windows 并不拦截从高权限往低权限的拖放，实测直接调 OLE 拖放可以把文件拖进普通权限的资源管理器，
/// 这里绕开 WinUI，自己判断拖动手势，再交给 Shell 的 SHDoDragDrop。</para>
/// <para>只处理鼠标：OLE 拖放靠鼠标按键状态判断松手，触摸和笔不适用。</para>
/// </summary>
internal sealed class ShellFileDragSource
{
    /// <summary>起拖阈值（DIP）：指针自按下点移动超过该距离才开始拖放，避免干扰点击。</summary>
    private const double DragThreshold = 6d;

    /// <summary>Shell 绑定处理程序 BHID_DataObject（shlguid.h）。</summary>
    private static readonly Guid BHID_DataObject = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");


    private readonly ListViewBase _listView;
    private readonly Func<object, string?> _getFilePath;

    private SelectorItem? _pressedContainer;
    private Point _pressPosition;


    /// <param name="listView">承载文件项的列表。</param>
    /// <param name="getFilePath">由数据项取文件路径，取不到时返回 <see langword="null"/>。</param>
    public ShellFileDragSource(ListViewBase listView, Func<object, string?> getFilePath)
    {
        _listView = listView;
        _getFilePath = getFilePath;
        // 列表项会把指针事件标记为已处理，必须用 handledEventsToo 才收得到
        listView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        listView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        listView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerEnded), true);
        listView.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerEnded), true);
        listView.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerEnded), true);
    }


    /// <summary>
    /// 记下按在哪一项上，这里不捕获指针，否则会打断列表项的单击。
    /// </summary>
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressedContainer = null;
        if (e.Pointer.PointerDeviceType is not PointerDeviceType.Mouse)
        {
            return;
        }
        var point = e.GetCurrentPoint(_listView);
        if (point.Properties.IsLeftButtonPressed && e.OriginalSource is DependencyObject source)
        {
            _pressedContainer = source.FindAscendantOrSelf<SelectorItem>();
            _pressPosition = point.Position;
        }
    }


    /// <summary>
    /// 移动超过阈值后开始拖放。
    /// </summary>
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedContainer is not SelectorItem container)
        {
            return;
        }
        var point = e.GetCurrentPoint(_listView);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _pressedContainer = null;
            return;
        }
        if (Math.Abs(point.Position.X - _pressPosition.X) < DragThreshold && Math.Abs(point.Position.Y - _pressPosition.Y) < DragThreshold)
        {
            return;
        }
        _pressedContainer = null;
        List<string> files = GetDraggedFiles(container);
        if (files.Count > 0)
        {
            // SHDoDragDrop 会一直阻塞到松手，并在内部转发消息，不能留在指针事件的调用栈里重入
            _listView.DispatcherQueue.TryEnqueue(() => DragFiles(files));
        }
    }


    /// <summary>
    /// 按键已松开或指针被取消，取消待定的拖放。
    /// </summary>
    private void OnPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        _pressedContainer = null;
    }


    /// <summary>
    /// 取本次要拖的文件，规则与系统列表一致：按在已选中的项上时拖全部选中项，否则只拖按住的项。
    /// </summary>
    /// <param name="container">按住的列表项容器。</param>
    /// <returns>存在的文件路径；可能为空。</returns>
    private List<string> GetDraggedFiles(SelectorItem container)
    {
        IEnumerable<object> items = container.IsSelected
            ? _listView.SelectedItems
            : [_listView.ItemFromContainer(container)];
        return items.Where(x => x is not null)
                    .Select(_getFilePath)
                    .Where(File.Exists)
                    .ToList()!;
    }


    /// <summary>
    /// 发起拖放（复制），阻塞到松手或取消。
    /// </summary>
    /// <param name="files">文件完整路径，至少一个。</param>
    private static void DragFiles(List<string> files)
    {
        var pidls = new List<Shell32.PIDL>(files.Count);
        Shell32.IShellItemArray? itemArray = null;
        object? dataObject = null;
        try
        {
            // DoDragDrop 要求当前线程已初始化 OLE，UI 线程是 STA，重复调用无害
            Ole32.OleInitialize(IntPtr.Zero).ThrowIfFailed();
            foreach (string file in files)
            {
                Shell32.SHParseDisplayName(file, null, out Shell32.PIDL pidl, 0, IntPtr.Zero).ThrowIfFailed();
                pidls.Add(pidl);
            }
            Shell32.SHCreateShellItemArrayFromIDLists(pidls, out itemArray).ThrowIfFailed();
            // 用 Shell 自己的数据对象，与从资源管理器拖出的格式一致，目标程序才都认
            dataObject = itemArray.BindToHandler(null, BHID_DataObject, typeof(System.Runtime.InteropServices.ComTypes.IDataObject).GUID);
            // 不能 Dispose：投递线程还可能在等它的 WaitHandle
            CancellationTokenSource mousePump = StartMouseMessagePump();
            try
            {
                Shell32.SHDoDragDrop(HWND.NULL, (System.Runtime.InteropServices.ComTypes.IDataObject)dataObject, null!, Ole32.DROPEFFECT.DROPEFFECT_COPY, out _).ThrowIfFailed();
            }
            finally
            {
                mousePump.Cancel();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Drag files with shell failed: {files}", files);
        }
        finally
        {
            if (dataObject is not null) Marshal.FinalReleaseComObject(dataObject);
            if (itemArray is not null) Marshal.FinalReleaseComObject(itemArray);
            pidls.ForEach(x => x.Dispose());
        }
    }


    /// <summary>
    /// 拖放期间把鼠标状态转成传统鼠标消息投给当前线程。
    /// <para>WinUI 进程的鼠标输入走 WM_POINTER，而 OLE 拖放的消息循环只认 WM_MOUSEMOVE / WM_LBUTTONUP，
    /// 不补这些消息的话它既不更新放置目标，也发现不了松手，会一直阻塞到下一次输入。</para>
    /// </summary>
    /// <returns>调用 Cancel 结束投递。</returns>
    private static CancellationTokenSource StartMouseMessagePump()
    {
        var cts = new CancellationTokenSource();
        uint threadId = GetCurrentThreadId();
        new Thread(() =>
        {
            CancellationToken token = cts.Token;
            while (!token.IsCancellationRequested)
            {
                // OLE 只从它自己捕获鼠标的窗口取消息，所以要投给当前线程的捕获窗口
                var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                if (GetGUIThreadInfo(threadId, ref info) && info.hwndCapture != 0 && GetCursorPos(out POINT point) && ScreenToClient(info.hwndCapture, ref point))
                {
                    // 松手后也继续投递：这条消息可能被循环之外的处理吃掉，直到拖放真正结束才停
                    bool pressed = GetAsyncKeyState(VK_LBUTTON) < 0;
                    nint position = (point.Y << 16) | (point.X & 0xFFFF);
                    PostMessage(info.hwndCapture, pressed ? WM_MOUSEMOVE : WM_LBUTTONUP, pressed ? MK_LBUTTON : 0, position);
                }
                token.WaitHandle.WaitOne(MousePollIntervalMs);
            }
        })
        { IsBackground = true, Name = nameof(StartMouseMessagePump) }.Start();
        return cts;
    }


    private const int MousePollIntervalMs = 16;
    private const int VK_LBUTTON = 0x01;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONUP = 0x0202;
    private const nint MK_LBUTTON = 0x0001;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public int rcCaretLeft;
        public int rcCaretTop;
        public int rcCaretRight;
        public int rcCaretBottom;
    }

}
