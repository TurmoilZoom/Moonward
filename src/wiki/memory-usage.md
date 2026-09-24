# Moonward 进程内存管理

Moonward（工程命名空间仍为 `Starward.*`）是 WinUI 3 / .NET 桌面应用。进程占用的大头不在 CLR 托管堆，而在 NT 堆、解码缓冲、Direct3D / Win2D 表面等本机分配。判断泄漏应看**私有提交（Private Bytes / 提交大小）**，而不是任务管理器默认的「内存」列（活动专用工作集）。窗口不可见时，`MemoryTrimmer` 在延迟确认后执行：阻塞压缩 GC 并等待终结器 → `HeapOptimizeResources` 归还本机堆空闲块 → `EmptyWorkingSet` 清空工作集。这套步骤不能修复仍被引用的对象，只能在确实已经释放之后把空闲页交还系统。

本文依据仓库当前实现（`MemoryTrimmer`、`MainWindow`、`CachedImage` 等）与 Windows / .NET 官方文档整理。文中带日期的容量数字来自 2026-09 的一次实测，不代表所有机器与构建。

## 核心概念：提交与工作集

Windows 不会把进程的每个已分配字节都一直放在物理内存里。任意一块已分配内存都可以问两件事：

1. **系统是否已经答应给这块虚拟地址准备后备存储（物理内存或页文件）？** → **提交（commit）**
2. **这一刻这些页是否算在本进程正在用的物理内存里？** → **工作集（working set）**

### 虚拟地址、保留、提交、驻留

| 状态 | 含义 |
| --- | --- |
| 保留（Reserve） | 只占虚拟地址空间，系统尚未保证能用。 |
| 提交（Commit） | 从全局提交限额中扣额度，登记「这块以后真的要用」。物理页要等到第一次读写才配上（demand-zero）。 |
| 驻留（Working Set） | 此刻实际在物理 RAM 中、属于该进程工作集的页。 |

提交来源包括：CRT / NT 堆（`malloc`、WinUI 本机对象）、.NET GC 堆、线程栈、`VirtualAlloc` 缓冲区、Direct3D 上传缓冲、解码后的像素缓冲等。

官方对提交页的说明：系统在第一次读或写该页时才初始化并装入物理内存；进程结束时释放已提交页的存储。见 [Page State](https://learn.microsoft.com/windows/win32/memory/page-state)。

### 常用指标

| 名称 | 在问什么 | 与其它指标的关系 |
| --- | --- | --- |
| 私有提交量（提交大小 / Private Bytes） | 只有本进程的、系统已经答应给后备的全部虚拟内存 | 在不在物理内存都算。看泄漏、看「到底要了多少」用这一列。任务管理器勾选「提交大小」即此项。对应 `PROCESS_MEMORY_COUNTERS_EX.PrivateUsage`（Commit Charge）。 |
| 工作集 | 此刻在物理内存中、算在本进程头上的页 | 含私有，也含正在用的共享 DLL。窗口隐藏或系统内存紧张时会被裁剪，数字会下降。 |
| 专用工作集 | 工作集里「只有本进程」的那一块 | 工作集 ∩ 私有。 |
| 活动专用工作集 | 专用工作集里当前处于 Active 列表、不是待机缓存的部分 | Windows 10 起任务管理器「进程」页默认「内存」列即此项。 |

私有提交不含共享 DLL 的映像页。工作集包含共享 DLL。窗口藏到托盘或内存紧张时，工作集会被 trim；只要没有 `VirtualFree` / 释放堆块，私有提交不会因此下降。

```text
                          是不是只有本进程的？
                      私有                 共享
               ┌────────────────┬────────────────────────┐
    在内存条上  │ 活动专用工作集  │ 工作集里的共享部分       │  ← 合起来 = 工作集
    （工作集）  │ 默认「内存」列  │ （系统 DLL 等）         │
               ├────────────────┼────────────────────────┤
    不在工作集  │ 提交了但被裁掉  │ 别人可能还占着这份 DLL   │
               │ 或已换到页文件  │                        │
               └────────────────┴────────────────────────┘
                        ↑
                这一列整列加起来 ≈ 私有提交量（提交大小）
```

### 任务管理器为什么会「变瘦」

任务管理器「进程」页默认「内存」列是**活动专用工作集**，不是提交大小。进程藏进托盘后，窗口树、.NET 堆、背景图通常仍在原来的虚拟地址上，并没有卸掉。内存管理器只是把长时间未访问的页从工作集摘掉：这些页多数进入待机列表（仍在 RAM，可被其它进程抢走），系统内存紧张时才写入页文件。Windows 10 起还可能先做内存压缩，不立即写盘。

再打开窗口时，代码仍读原来的指针。虚拟地址还在、物理页不在工作集里，于是缺页中断，页被装回，「内存」列涨回去。提交大小几乎不动，说明东西还在，只是暂时没算进该进程的工作集。

因此：

- 看泄漏、看进程到底占用了多少承诺内存：用**提交大小 / Private Bytes**（Sysinternals VMMap 的 **Private** 栏）。
- 看这一刻占了多少物理 RAM：用工作集。工作集会随最小化、托盘隐藏、系统 trim 波动，不适合当泄漏地板。

## 本应用内存构成

2026-09-07 对切换数次游戏后驻留托盘的进程实测（写入 `MemoryTrimmer` 注释）：私有内存约 731 MB，其中 102 个 NT 堆段约 353 MB（同机上游 Starward 为 49 段约 203 MB），托管堆约 20 MB。内存大头在本机堆与显存侧。单靠 `GC.Collect()` 几乎收不回提交。

WinUI 3 / WinRT 对象的本体是 C++ 本机对象，住在 NT 堆上。C# 里拿到的 `Image`、`BitmapImage`、`MediaPlayer` 等是 CsWinRT 投影对象（壳），体积很小。壳内部持有 COM 指针；`Release()` 通常由终结器或 `Dispose` 触发。

因此：

- GC 只回收托管堆。它不认识文件句柄、COM 指针、GDI 对象、`malloc` 出来的本机内存。
- `HeapOptimizeResources` 只整理 NT 堆的空闲块与 LFH 缓存，不认识托管堆。
- `dumpheap` 只统计 CLR 托管对象，看不到 WinUI 控件、`MediaPlayerElement`、Win2D 表面、BGRA 像素、D3D 上传缓冲在本机堆 / GPU 上的占用。

## NT 堆与低碎片堆（LFH）

「NT 堆」是 Windows 用户态默认堆分配器（`ntdll` 的 `RtlHeap`，`HeapAlloc` / `HeapFree` 的默认后端）。桌面 Win32 应用默认走传统 NT Heap；Windows 10 起另有 Segment Heap（打包应用 / UWP 及部分系统进程默认）。Moonward 是 Win32 桌面应用，走 NT Heap。

### 三层结构

```text
进程
├─ HEAP #1（进程默认堆，GetProcessHeap）
│   ├─ segment A  ← VirtualAlloc 得到的连续虚拟地址
│   │   [block][block][ free ][block][ free .......... ]
│   └─ segment B
└─ HEAP #2（CRT 堆 / 某个库 HeapCreate 的私有堆）
    └─ segment C
```

进程里通常有多个堆：默认堆之外，CRT、不少 DLL、图形驱动和 COM 都会自建。`GetProcessHeaps()` 可以列出。每个堆各自有一套 LFH 状态。堆不够用时再向系统要一段（segment）：一段就是一块 `VirtualAlloc` 出来的连续虚拟内存。

官方说明：系统不能压紧私有堆，堆会碎片化。见 [HeapAlloc 备注](https://learn.microsoft.com/windows/win32/api/heapapi/nf-heapapi-heapalloc)。只有整段（或段内足够大的连续空闲区）全空，堆才可能把它 decommit 还给系统。段里只要还钉着一个活块，这段的 commit 就下不去。这是堆段数字降不下来的原因。真正减少堆段的办法是少产生那些大块、长寿命的本机分配，而不是反复整理。

### 分配路径

```text
HeapAlloc(size)
      ↓
┌─ 一个 NT 堆的内部 ───────────────────────────────┐
│  前端：LFH        小块（约 ≤ 16 KB）走这里         │
│    按 size class 分桶，每桶预批发一块 subsegment   │
│    切成等大槽位，分配 = 位图翻一位                 │
│         ↓ 未命中 / 更大块                         │
│  后端：段 + 空闲块链表                            │
│    段 = VirtualAlloc 来的连续虚拟内存             │
└─────────┼────────────────────────────────────────┘
          ↓
      VirtualAlloc（再大的分配直接走这条，不进段）
```

LFH（Low-Fragmentation Heap，低碎片堆）不是独立的堆，而是堆上的前端策略。官方文档：当前实现里，大约超过 16 KB 的分配不走 LFH。Vista 起系统按需启用 LFH，应用不必再手动打开。同一堆里可以部分 size class 走 LFH、部分仍走后端。

LFH 会按尺寸桶缓存空闲槽位：`HeapFree` 一个小块时，LFH 只把槽位标成空，不立即还给后端，以便下次同尺寸分配复用。这批内存既不在用、也不算后端空闲块，但计入进程的私有提交。这是它快的原因，也是隐藏占用的来源。

超过 LFH 范围的块走后端段；再大的分配由堆直接 `VirtualAlloc`。Windows Internals 记载的后端 / `VirtualAlloc` 分界约 508 KB，精确阈值随 Windows 版本与堆参数变化。

### `HeapOptimizeResources`

`HeapSetInformation(HeapHandle, HeapOptimizeResources, …)`（Windows 8.1 起）：

- `HeapHandle` 为 `NULL`：进程内**已启用 LFH 的堆**会整理缓存，并在可能时 decommit。
- 传入具体堆句柄：只整理该堆。

Moonward 使用 `NULL`，覆盖进程内全部相关堆。结构体 `HEAP_OPTIMIZE_RESOURCES_INFORMATION.Version` 当前合法值只有 1。

整理动作：

1. 清 LFH 缓存：把囤积的空 subsegment 交还后端。
2. 把彻底空出来的区域 decommit 还给系统。虚拟地址可以保留，提交量下降。64 位下虚拟地址空间充裕，不还地址通常可接受。

限制：

1. 只能退整页（4 KB 对齐）。空区不足一页或未对齐则退不了。
2. 一个段上只要还压着一个活块，整段不能作为整体退掉，只能退段内已经空出来的页。
3. 堆管理器不搬家。不像 GC 会把活对象挤到一起腾出连续空间，它只就地看哪块空着。碎片化的堆能退的量有限。

`HeapOptimizeResources` 只归还**空闲**块，正在用的分配一律不动。调用时会逐个锁住相关堆，期间其它线程在这些堆上的分配可能被挡住。

## 托管 GC、终结器与 Dispose

### 代与模式

.NET 把对象按存活时间分成三代。`GC.Collect(n)` 的语义是从第 0 代收到指定代，因此 `GC.Collect(GC.MaxGeneration)` 等于全扫。

| 代 | 典型内容 |
| --- | --- |
| 第 0 代 | 刚创建的新对象，多数很快不可达 |
| 第 1 代 | 挺过一轮回收的对象 |
| 第 2 代（最高代） | 挺过多轮的老对象：窗口、页面、图片、播放器等长寿命对象 |

`compacting: true` 只压缩小对象堆（SOH）。大对象堆（LOH，约大于 85,000 字节）默认只清扫不压缩；要压 LOH 必须先设 `GCSettings.LargeObjectHeapCompactionMode = CompactOnce`，再做一次全代阻塞回收。该属性在下一次全代阻塞回收后复位。

`blocking: true`：挂起托管线程，一次性收完。`blocking: false`：尽量后台回收，不保证立刻收干净。

Moonward 未设置 `ServerGarbageCollection`，桌面应用默认工作站 GC。工作站 GC 带后台（并发）回收，以缩短暂停。服务器 GC 面向高吞吐、多堆，不是本应用的默认配置。

### `GCCollectionMode`

| 值 | 官方含义 |
| --- | --- |
| `Default` = 0 | 当前等同于 `Forced`（文档写明「currently Forced」，语义以后可能变） |
| `Forced` = 1 | 立即执行回收 |
| `Optimized` = 2 | 由 GC 判断当时是否适合回收；可能什么都不做 |
| `Aggressive` = 3 | 请求 GC 尽可能多地把内存 decommit（.NET 8 起） |

`Aggressive` 对参数有额外约束：必须是最高代，且 `blocking` 与 `compacting` 均为 `true`，否则抛 `ArgumentException`。`generation < 0` 或非法枚举值抛 `ArgumentOutOfRangeException`。`generation` 超过 `MaxGeneration` 时按最高代处理，不抛异常。

无参 `GC.Collect()` 不抛异常，执行全代阻塞回收。

诱导回收会让碰巧还活着的第 0 代对象晋升到第 1 / 2 代。老年代越大，以后每次全代回收越贵。GC 修不了「还有托管引用」的泄漏：事件未退订、静态集合持续增长、缓存无上限时，调多少次 `GC.Collect` 都收不走。

微软文档给出的合适时机：有一个明确的、一次性的大释放点，且之后不马上需要性能——例如关闭含大量控件的复杂对话框、应用进入长期空闲、测试代码在测内存前清基线。见 [Induced Collections](https://learn.microsoft.com/dotnet/standard/garbage-collection/induced) 与 [Improve garbage collection performance in WinUI apps](https://learn.microsoft.com/windows/apps/develop/performance/improve-garbage-collection-performance)。

### 壳、终结器、本机释放顺序

切页面时被丢掉的绝大多数本机对象，调用方拿不到引用：XAML 解析出来的元素、Composition 视觉、绑定表达式、图片解码器都是 WinUI 内部创建的，没有对应的 `Dispose()` 可调。这些对象的本机释放依赖：

```text
托管壳不可达
      ↓  只有 GC 能判定，并排进终结队列
Finalize() 被终结器线程执行
      ↓  托管代码调用本机 API
IUnknown::Release()
      ↓  C++ 侧引用计数归零
C++ 析构函数运行
      ↓
HeapFree()    ← 到这一步，NT 堆上才出现空闲块
      ↓
HeapOptimizeResources 才有货可退
```

因此顺序必须是：

```csharp
GC.Collect(...);                // 把不可达的壳排进终结队列
GC.WaitForPendingFinalizers();  // 等终结器线程把这批 Release 调完
GC.Collect(...);                // 终结器又释放了一批托管对象，再收一次
OptimizeNativeHeaps();          // 到这里本机堆上才真的有空闲块
```

如果所有对象都被显式 `Dispose()`，`Release()` 当场执行，堆上立刻出现空闲块，可以不依赖这次 GC。切页面时做不到这一点。

终结器跑在终结器线程上，不是 UI 线程。在终结器里碰 WinUI / STA COM 对象会得到 `COMException 0x8001010E`（`RPC_E_WRONG_THREAD`）。该异常从终结器线程抛出时可能直接终止进程，`catch` 拦不住。

`IDisposable` 是确定性清理：由调用方决定时机，立刻释放非托管资源，然后 `GC.SuppressFinalize(this)` 把自己从终结队列摘掉。仓库里 `ImageInfo.Dispose`、`ScreenCaptureItem.Dispose` 即此模式。

| | `Dispose` | GC |
| --- | --- | --- |
| 时机 | 调用方或 `using` 主动调用，立刻执行 | 堆压力、内存紧张、或显式 `GC.Collect` 时才跑 |
| 管什么 | 非托管资源，以及级联释放其它 `IDisposable` | 托管堆上的对象内存 |
| 是否确定 | 确定（deterministic） | 非确定（nondeterministic） |
| 谁知道 `Dispose` | 调用方 / `using` | 不知道。GC 只认终结器 |

`Dispose` 的设计目的是立刻释放 GC 管不到的东西（官方接口说明：*freeing, releasing, or resetting unmanaged resources*）。日常调用的几乎都是托管包装器——`FileStream`、`MediaPlayer`、`Process` 都是 C# 对象，GC 能回收它们在托管堆上的那一块；它们内部握着的文件句柄、COM、解码器、显存，GC 不知道怎么关。`_mediaPlayer.Dispose()` 是在告诉这个托管 WinRT 包装器：把底层播放器、IMF 管道、解码器引用立刻放开。

WinRT 的 `IClosable` 在 C# 中投影为 `IDisposable`。对 `SoftwareBitmapSource` 一类对象应 `Dispose()`，而不是 `Marshal.ReleaseComObject`。

### `Marshal.ReleaseComObject` 不适用于 WinUI 3 投影类型

`Marshal.ReleaseComObject` 要求参数是经典 RCW（`Marshal.IsComObject == true`，即 `__ComObject` 派生），否则抛 `ArgumentException`。WinUI 3 / Windows App SDK 类型走 CsWinRT 投影（本仓库引用 `Microsoft.Windows.CsWinRT` 2.2.0）。`MediaPlayerElement`、`SoftwareBitmapSource`、`BannerCarousel` 都是普通托管类，不是 RCW。对它们调用 `ReleaseComObject` 无效且会抛异常。

## 当前回收机制：`MemoryTrimmer`

实现：`src/Starward/Helpers/MemoryTrimmer.cs`。

### 触发

| 路径 | 行为 |
| --- | --- |
| `MainWindow.Hide()`（Esc、关闭到托盘） | 先广播 `MainWindowStateChangedMessage { Hide = true }`，再 `base.Hide()`，再 `MemoryTrimmer.TrimLater(IsInvisible)` |
| `WM_SIZE` 且 `wParam == SIZE_MINIMIZED` | 同样广播 Hide 消息并 `TrimLater(IsInvisible)` |

`Hide()` 不再调用无参 `GC.Collect()`。进托盘时**不会再创建**托盘窗口。主窗口构造时就会 `EnsureSystemTray()`。`SystemTrayWindow` 负责通知区图标，并作为常驻宿主拉起 `ResidentHost`（全局热键、手柄、自动签到、RPC 等）。仅 `--hide` 启动时可以没有主窗口，但托盘窗口仍在。

### 延迟、确认、冷却

1. `TrimLater` 必须在 UI 线程调用。
2. 若距上次回收完成不足 `MinInterval`（5 秒），直接返回。
3. 等待 `DefaultDelay`（3 秒），给 `MediaPlayer` / Win2D / 页面卸载留出发落地的时间。等待结束后**回到 UI 线程**调用 `canTrim`。
4. `IsInvisible()`：`!AppWindow.IsVisible || User32.IsIconic(WindowHandle)`。用户已把窗口开回来则放弃。`AppWindow` 是 WinRT 对象，只能在 UI 线程读取。
5. 确认仍不可见后，在线程池上执行 `Trim()`。堆整理会锁堆，不能放在 UI 线程。

`_running` 只防止并发执行。接连两次 `TrimLater` 靠 `MinInterval` 去重。延迟 3 秒、冷却 5 秒，主要挡住「最小化紧接着藏托盘」或系统重复 `WM_SIZE` 这类短间隔重复；间隔超过 5 秒的再次隐藏会再跑一轮。

Trim 一旦开始没有 `CancellationToken`，会跑完。若此时用户恢复窗口：

| 步骤 | 对已恢复窗口的影响 |
| --- | --- |
| 阻塞压缩 GC | 挂起所有托管线程（含 UI 线程），恢复动画的第一帧要等它结束。托管堆约 20 MB 时这一步通常很快。 |
| `HeapSetInformation(HeapOptimizeResources)` | 逐个锁住进程内相关堆。UI 线程此刻的本机分配（XAML 布局、Composition、图片解码）要等锁释放。 |
| `EmptyWorkingSet` | 刚把工作集清空，窗口马上要重绘，页得换回来。多数是软缺页（从待命链表拿回）；只有系统内存紧张、页已经被写进页文件时才是硬缺页。 |

这三个 API 本身可在进程运行时调用。`HeapOptimizeResources` 只动空闲块。

### `Trim()` 五步

```csharp
GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
GC.WaitForPendingFinalizers();
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
OptimizeNativeHeaps();   // HeapSetInformation(NULL, HeapOptimizeResources, …)
TrimWorkingSet();        // K32EmptyWorkingSet
```

| 步骤 | 作用 | 对提交大小 | 对任务管理器「内存」列 |
| --- | --- | --- | --- |
| 1. `Forced` 全代阻塞压缩 | 把不可达托管壳判死并压缩 SOH | 托管堆本身很小，提交几乎不动 | 几乎不动 |
| 2. `WaitForPendingFinalizers` | 等终结器调用 `Release()`，本机对象才能 `HeapFree` | 为第 4 步准备空闲块 | 几乎不动 |
| 3. `Aggressive` + LOH `CompactOnce` | 再扫一遍；`Aggressive` 请求把 GC 自己囤着的段 decommit | 托管堆再退一点 | 几乎不动 |
| 4. `HeapOptimizeResources` | 退 LFH 缓存与能 decommit 的空闲区 | **唯一可能明显降低私有提交的一步**；能退多少取决于碎片 | 间接 |
| 5. `EmptyWorkingSet` | 把尽可能多的页移出工作集 | **不改变提交** | **立刻下降** |

第 1 遍 `Forced` 与第 3 遍 `Aggressive` 的差别：两者都回收不可达对象；只有 `Aggressive` 额外请求把 GC 堆段 decommit 还给系统。

日志记录回收前后的 `Process.PrivateMemorySize64`（私有提交）与耗时。某次实测整段 Trim 约 103 ms，期间任何要在这些堆上分配的线程（XAML 布局 / 渲染、D3D / WIC、HTTP、SQLite）都可能被堆锁挡住。

## 窗口可见性

`WS_VISIBLE` 是 Win32 窗口样式位，值 `0x10000000`。它表示「窗口处于已显示状态」，不是「用户此刻能看见」。

`IsWindowVisible` 检查本窗口及祖先是否都带 `WS_VISIBLE`。返回非 0 但用户看不见的情况包括：被其它窗口盖住、位置在屏幕外或宽高为 0、分层窗口 Alpha 为 0、**最小化的窗口仍然是 `WS_VISIBLE`**。

显示或隐藏窗口应使用 `ShowWindow`（或 WinUI 的 `AppWindow.Show` / `Hide`）。窗口样式文档写明：`WS_VISIBLE` 创建后用 `ShowWindow` 或 `SetWindowPos` 开关。只改 `GWL_STYLE` 里的这一位不会完成任务栏、激活、重绘等配套更新。

| 状态 | 触发路径 | `IsWindowVisible` | `IsIconic` | `AppWindow.IsVisible`（本应用用来判断） |
| --- | --- | --- | --- | --- |
| 正常显示 | — | true | false | true |
| 最小化到任务栏 | `SW_MINIMIZE` | true | true | 仍为可见（靠 `IsIconic` 判定不可见） |
| 隐藏到托盘 | `AppWindow.Hide()` | false | false | false |

`IsInvisible()` 用 `!AppWindow.IsVisible || IsIconic`，覆盖托盘隐藏与最小化两条路径。

## 页面缓存与导航卸载

`Page.NavigationCacheMode` 默认 `Disabled`：每次导航创建新实例。`Frame.CacheSize` 默认 10，只在页面把 `NavigationCacheMode` 设为 `Enabled` / `Required` 时才有意义。本仓库没有设置这两项，Frame 内页面默认不缓存。

`Frame.Navigate` **不会**对旧页调用 `Dispose`。旧页从视觉树拿掉时会抬 `Unloaded`。`PageBase` 在 `Unloaded` 里调用 `OnUnloaded()`，由各页退订事件、Messenger，并把 `MediaPlayer`、文件监视器、流等字段 `Dispose()`。

切换游戏时（`MainView.UpdateNavigationView`）：

| 对象 | 会不会 `Unloaded` |
| --- | --- |
| Frame 里当前页（启动器、抽卡、工具箱、截图等） | 会。旧实例卸掉，新实例再 `Loaded`。 |
| 正在看设置页 | 不会。切换游戏时故意跳过 `Navigate`，设置页接着活。 |
| `AppBackground`、`GameSelector`、侧栏 | 不会。它们在 `MainView` 里，不进 Frame。 |

声明式 XAML 事件（`Click=`、`PointerEntered=` 等）没有退订入口。卸载后本机侧仍留着一份注册时，整棵控件树会被留住。`BannerCarousel` 与 `FavorWallpaperCard` 已改为 code-behind 成对 `HookHandlers` / `UnhookHandlers`；XAML 注释写明声明式订阅会在关闭对话框或切游戏后把卡片连同 `MediaPlayerElement` 留在本机侧。

## `CachedImage` 解码与内存 LRU

任何 JPEG / PNG / WebP 要上屏都必须解成位图。`DecodeWidth` / `DecodeHeight`（对应 `BitmapImage.DecodePixelWidth` / `DecodePixelHeight`，`DecodePixelType.Logical`）只规定解成多宽多高，不是「要不要解」。

`CachedImage` 对 **http/https** 源，在 `DecodeWidth > 0 || DecodeHeight > 0` 时，按「URI + 解码尺寸」做进程内 LRU（`Dictionary` + 链表，容量 512）。未指定解码尺寸的大图（背景、横幅）不进这份缓存，避免按原生分辨率常驻。

当前仓库里给 `CachedImage` 设了解码尺寸的只有 `FavorWallpaperCard`：`DecodeWidth="240"`。因此这套 LRU 实际只覆盖好感画廊里仍用远程封面图的卡片：同一张 240 宽的解码结果可以在滚动、回收、重复出现时复用。

主窗口静态背景是另一条路径：`AppBackground.ChangeBackgroundImageAsync` 按窗口的物理像素尺寸缩放解码（Fant 插值），避免把大于窗口的原图整幅解进内存。这与卡片封面的 `DecodeWidth="240"` 不是同一套缓存。

## 分析手段

### 该看哪一列

| 层级 | 指标 | 工具 |
| --- | --- | --- |
| 进程承诺了多少 | 私有提交 / Private Bytes | 任务管理器「提交大小」、`Process.PrivateMemorySize64`、VMMap **Private** |
| 此刻占了多少 RAM | 工作集 | 任务管理器「内存」、资源监视器 Working Set |
| 托管对象活了多少 | `dumpheap -stat` 的实例数与托管大小 | `dotnet-dump` + SOS |
| 本机堆段 | NT 堆段数量与提交 | VMMap / WinDbg `!heap` |

全内存 dump：`dotnet-dump collect --type Full`。完整 dump 才能可靠走 `dumpheap`。

火焰图把调用栈统计画成色块，用来看时间或分配花在哪条调用链上（Brendan Gregg，2011）。CPU、内存、off-CPU 分析都可以用。

### `dumpheap` 的边界

`dumpheap -stat` 统计的是 CLR 托管堆上的对象（含对象头与托管字段），不是进程私有提交。WinUI 控件、像素缓冲、D3D 表面上的兆字节不会出现在这个表里。实例数仍然有用：某类型「应有 0～1 个、实际几百个」就是泄漏信号。

VMMap 把虚拟内存分成 PRIVATE（`VirtualAlloc` / 堆 / 栈）、IMAGE（已加载 PE）、MAPPED（`MapViewOfFile` 一类，如字体、ICU、DX 共享表面）。

### 一次 dump 案例（历史快照）

以下数字来自某次全内存 dump，用来说明怎么读，不代表当前构建的存活数。同一时期 VMMap：PRIVATE 1,397 MB，IMAGE 493 MB，MAPPED 351 MB。

| 类型 | 实例数 | 按设计应有 |
| --- | --- | --- |
| `BannerCarousel`（首页轮播图） | 188 | ≤ 1 |
| `PipsPager` / `VisualStateGroup` / `List<CachedImage>` | 190 / 188 / 188 | 随 `BannerCarousel` |
| `FavorWallpaperPanel`（壁纸对话框面板） | 17 | 0（对话框已关） |
| `FavorWallpaperCard` | 954 | 0 |
| `MediaPlayerElement` | 954 | 0 |
| `AnimatedVisualPlayer` / `Button` / `Border` / `Grid` | 1,918 / 1,422 / 2,136 / 1,751 | — |
| `Image` / `ProgressBar` / `CachedImage` | 968 / 955 / 988 | — |
| `PointerEventHandler` / `RoutedEventHandler` | 14,819 / 12,310 | — |
| `GameLauncherPage` | 2 | 1–2 |

`GameLauncherPage` 为 2 说明 Frame 页本身大致正常；`BannerCarousel` 188、关闭对话框后 `FavorWallpaperCard` / `MediaPlayerElement` 仍为 954，说明控件被事件或本机注册钉住。后续代码已对这两处改为成对退订。

## 常见误区

**只看任务管理器默认「内存」列。** 隐藏窗口后该列下降，提交大小不动，再打开会涨回去。这不是泄漏消失。

**窗口可见时调用 `EmptyWorkingSet`。** 提交不变，页立刻缺页换回，造成卡顿和可能的磁盘 IO。某次自测：工作集可自行从约 503 MB 衰减到约 152 MB，私有提交不变。`EmptyWorkingSet` 官方说明主要用于测试与调优。

**窗口可见时反复做 `GCCollectionMode.Aggressive` + 阻塞压缩 GC + LOH CompactOnce。** 文档写明适合应用空闲或进入后台。可见时做等于随机掉帧。

**窗口可见时调用 `HeapSetInformation(NULL, HeapOptimizeResources)`。** 会锁堆。不可见时卡顿可接受；可见时任何本机分配都可能顿一下。

**以为 `GC.Collect()` 能修泄漏。** 仍被事件、静态表、未 `Dispose` 的包装器引用的对象，GC 收不走。正确顺序是少分配、在 `OnUnloaded` 里退订并 `Dispose`。能用 `using` / `Dispose` 解决的，不要指望 GC。

**对 WinUI 3 投影类型调用 `Marshal.ReleaseComObject`。** 它们不是经典 RCW，会抛 `ArgumentException`。应 `Dispose()`。

**用 `dumpheap` 的总大小解释任务管理器里的几百 MB。** 那是托管对象大小，不含 NT 堆与 GPU。

**在可见窗口上跑完整 Trim。** 阻塞 GC、锁堆、清空工作集三项叠加，恢复后第一帧和随后的布局 / 解码都要付出换入与锁等待。

## 限制与注意事项

- Trim 降低私有提交的能力受堆碎片限制。切换游戏留下的堆段，只要段内还有活块就退不掉。根本办法是少产生、及时 `Dispose` 那些本机大块。
- 冷却与 `_running` 不能取消已经开始的 Trim。用户在 3 秒延迟内把窗口开回来可以放弃；延迟结束后已经 `Task.Run(Trim)` 则跑完。
- `AppWindow` 状态必须在 UI 线程读。`TrimLater` 故意不在 `Delay` 上 `ConfigureAwait(false)`。
- 页面不进 Frame 的部分（背景、游戏选择器、侧栏）以及设置页在切游戏时不会 `Unloaded`，它们的本机资源要靠各自的生命周期管理，不能指望导航卸载。
- `CachedImage` 内存 LRU 在当前仓库中只服务设了解码尺寸的远程图；全仓库仅好感壁纸卡片封面启用。其它 `CachedImage` 仍按未限尺寸解码（或走缩略图 / 磁盘 `FileCache`）。

## 参考资料

- [Page State（保留 / 提交）](https://learn.microsoft.com/windows/win32/memory/page-state)
- [Working Set](https://learn.microsoft.com/windows/win32/memory/working-set)
- [PROCESS_MEMORY_COUNTERS_EX（PrivateUsage = Commit Charge）](https://learn.microsoft.com/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex)
- [Memory Performance Information](https://learn.microsoft.com/windows/win32/memory/memory-performance-information)
- [EmptyWorkingSet](https://learn.microsoft.com/windows/win32/api/psapi/nf-psapi-emptyworkingset)
- [HeapSetInformation / HeapOptimizeResources](https://learn.microsoft.com/windows/win32/api/heapapi/nf-heapapi-heapsetinformation)
- [Low-fragmentation Heap](https://learn.microsoft.com/windows/win32/memory/low-fragmentation-heap)
- [IsWindowVisible](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-iswindowvisible)
- [Window Styles（WS_VISIBLE）](https://learn.microsoft.com/windows/win32/winmsg/window-styles)
- [GC.Collect](https://learn.microsoft.com/dotnet/api/system.gc.collect)
- [GCCollectionMode](https://learn.microsoft.com/dotnet/api/system.gccollectionmode)
- [Induced Collections](https://learn.microsoft.com/dotnet/standard/garbage-collection/induced)
- [IDisposable](https://learn.microsoft.com/dotnet/api/system.idisposable)
- [Marshal.ReleaseComObject](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.releasecomobject)
- [Page.NavigationCacheMode](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.page.navigationcachemode)
- [WinUI 启动性能：页面缓存默认 10](https://learn.microsoft.com/windows/apps/develop/performance/app-startup-performance)
- [SOS dumpheap](https://learn.microsoft.com/dotnet/core/diagnostics/sos-debugging-extension)
- 实现：`src/Starward/Helpers/MemoryTrimmer.cs`、`src/Starward/Features/ViewHost/MainWindow.xaml.cs`、`src/Starward/Controls/CachedImage.cs`
