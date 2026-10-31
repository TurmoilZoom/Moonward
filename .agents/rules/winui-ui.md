# UI 约定

- 页面：`PageBase`；导航参数 `GameId` / `GameBiz`
- 亚克力背景；动画优先 Composition（`Helpers/FluentAnimations.cs`），少用 Storyboard
- `DropDownButton` 弹出层无法亚克力，不要强行改造
- 悬停说明优先 `InstantTooltip` / `InstantTooltipHost`
- **不要升级** `CommunityToolkit.WinUI.Controls.Segmented`（csproj 有回归说明）

## 页面动画与外观：先分清作用域

修改**某个页面**的动画效果或外观时，**先判断本次改动是否为全局改动**，以及是否会波及其他页面的视觉效果：

- **共享层**（改了即全局）：`FluentAnimations` / 通用动画 helper、App 级或资源字典中的 Style/Template、`PageBase` 与导航壳层、全局主题/画刷/字体等——改一处会影响所有调用方或继承页
- **页面局部**：仅该页 `.xaml` / code-behind、页面私有资源、未共享的控件实例属性——作用域通常限于当前页
- 为单页调参时，**优先局部实现**；确需改共享层时，明确列出受影响页面/控件，并手测相关页（含不同游戏、暗亮主题若适用），避免「修好一页、拖垮别处」

## 复杂控件：命中测试与输入路由

视觉树层级较深（叠层、装饰、遮罩、Popup、自定义模板、Composition 视觉）时，**不能只看效果对不对**，还要验证输入是否落到正确元素：

- **命中测试（hit test）**：透明/半透明装饰层、全屏叠层、未设 `IsHitTestVisible="False"` 的背景是否挡住下方按钮/列表；可点区域是否与视觉反馈一致
- **输入路由**：指针/触摸/键盘事件的捕获、冒泡与隧道是否被中间层截断；焦点与键盘导航是否仍可达；滚动容器与内嵌可点控件是否争抢手势
- 改模板或加装饰层后，至少手测：点击、悬停、拖拽、滚轮、Tab 焦点、以及叠层关闭后下层是否仍可交互

## 可视化控件：先查文档与参考实现

设计或重写**外观向**控件（自定义 ControlTemplate、视觉状态、Composition 动画、非标准布局）时，**先对齐正确做法再写代码**，避免凭直觉堆节点导致命中/无障碍/主题/性能问题：

1. **官方文档**：WinUI 3 / Windows App SDK（控件模板、视觉状态、Composition、输入与焦点）— [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
2. **社区文档与组件**：Community Toolkit for Windows 等已验证模式
3. **开源参考**：上游/同类 WinUI 应用与控件库中的成熟实现（模板结构、hit-test 边界、动画接入点），对齐后再适配本仓风格

本仓已有模式优先复用（如 `FluentAnimations`、`InstantTooltip`）；新模式应能说清「参考了何处、为何这样分层」。

## 已踩过的坑

以下每条都在本仓出过问题。括号里是现有的正确写法，改同类代码前先看。

### 跨 DPI 崩溃与 Composition

- 把窗口拖到缩放不同的显示器时，整棵树会重新 Measure。崩溃特征：`0xC000027B`（stowed 异常里是来自 `FrameworkElement.MeasureOverride` 的 `0x80004005`），栈里只有框架帧、没有本仓代码。
- **不要在 Measure 栈上动 Composition**：`ItemsRepeater.ElementPrepared` 在 MeasureOverride 里同步触发，在里面写 `Scale` / `CenterPoint`（不论 raw Visual 还是 `UIElement.Scale`）都会在 DPI 变化时崩。要播动画就等 `Loaded` 后再 `DispatcherQueue.TryEnqueue`（`GachaPityBarAnimation`）。
- 行数有限、行高固定的列表用 `ItemsControl`，不用 `ItemsRepeater`：后者的虚拟化在 DPI 变化时 MeasureOverride 会失败（`GachaStatsCard.xaml`）。
- 持续性的拖动 / 位移视觉用 `RenderTransform`（`CompositeTransform`）直接赋值，要过渡就在 `CompositionTarget.Rendering` 里逐帧赋值；不要对被 Measure 的元素用 Storyboard 动 RenderTransform，也不要写 raw Composition（`GachaStatsCardDragReorder`）。
- 同一元素只能选一种变换通道：调用过 `ElementCompositionPreview.GetElementVisual(el)`（哪怕只读 `.Compositor`）之后再设 `UIElement.Scale` / `CenterPoint`，会抛 `UnauthorizedAccessException 0x80070005`，反之亦然。需要 Compositor 又不想占用元素时，用 `Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread()`。
- DPI 变化时停掉进行中的 Composition 过渡并复位视觉：监听 `XamlRoot.Changed`，比较 `RasterizationScale`（`GachaStatsSegmentedListHelper`）。
- 按钮、浮层这类独立元素上的 Composition 动画没问题（`FluentAnimations`）；被 `ItemsControl` 测量的容器本身不要挂。

### 布局回调

- 不要在布局回调里同步改布局：`SizeChanged` 里同步回写尺寸，或在指针 / 点击事件里同步 `UpdateLayout()` 再 `MoveWindow`，都会直接 fail-fast（`0xC000027B`，`try/catch` 拦不住）。改为 `DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, …)` 推到本轮布局之后，并加重入保护。
- 依赖式动画（`EnableDependentAnimation=true`，如动 `Height`）每帧都在 UI 线程重新布局，弱机会掉帧。展开 / 收起改为一次性设好高度，再用 Composition `InsetClip` 做揭示（`StartGameMenu.SetShortcutExpanded`）。

### 动画通用

- 位移动 `Translation`（先 `ElementCompositionPreview.SetIsTranslationEnabled`），不要改 `Visual.Offset`：Offset 会覆盖布局给的位置。
- `Forever` 循环动画会让合成器按刷新率持续出帧，哪怕关键帧大部分时间不动；`DropShadow` 动画每帧重绘，代价尤其大（开始按钮的呼吸光晕加流光实测约占单核 15%，已删）。确需常驻动画，要在最小化 / 托盘 / 锁屏时暂停，并跟随系统动画开关。
- 交互型动效先判 `EntranceAnimation.AnimationsEnabled()`（命名空间 `Starward.Controls`），关闭时直接落到终态。
- 元素离开视觉树时，停掉它身上还在跑的 Composition 动画（`Unloaded` / `ElementClearing` 里 `StopAnimation`），以免合成器继续持有该视觉。
- Storyboard 默认 `FillBehavior=HoldEnd` 会锁住属性值：动画结束后直接赋值不生效，要先 `Stop()` 再归位。
- `SplineDoubleKeyFrame.KeySpline` 写成 `x:String` 资源（`"x1,y1,x2,y2"`），不能用 `<KeySpline>` 对象资源，否则运行时 `XamlParseException`（`BannerCarousel.xaml`）。
- `Grid` / `Panel` 设了 `CornerRadius` 会裁剪子元素；需要溢出圆角的效果放到不裁剪的外层宿主上。

### 命中测试与输入

- `Background` 为 null 的面板不参与命中测试：要接指针的 `Grid` / `Border` 显式设 `Background="Transparent"`；裸 `FontIcon` 挂提示命中不稳，外面包一层透明 `Border`。
- `IsEnabled=false` 的控件不参与命中测试，输入会穿到下层或父级。给禁用按钮加提示，要挂在透明父容器上（`PreDownloadButton`）。
- `PointerEntered` / `PointerExited` 会冒泡：挂在大容器上，子元素的进出也会触发。只想响应某一块，就给那块单独做透明命中层（关于页的猫，`AboutSetting.xaml`）。
- 弹出菜单的整行按钮里不要再嵌套 `ToggleSwitch` 等可点控件，两者会抢命中。
- 拖拽期间 ScrollViewer 持有指针捕获，滚轮直接路由给 ScrollViewer，挂在 Content 上收不到；要拦截用 `AddHandler(PointerWheelChangedEvent, …, handledEventsToo: true)`。原生滚轮动画不会被 `ChangeView(disableAnimation: true)` 取消，自管拖拽 / 惯性时，滚轮一来就交给原生滚轮（`GachaStatsListDragScrollHelper`）。
- 刚好包住内容的小窗口（如托盘菜单）里，`Popup`（`Flyout`、`MenuFlyoutSubItem`、`InstantTooltip`）会被窗口边界裁掉，二级内容只能放在同一窗口里。

### 布局与尺寸

- Grid 测量阶段不计 `MinWidth`：表头和各行用多个 `*` 列加 `MinWidth`，长内容行会被撑宽、逐行错位。需要跨行对齐的列宽由代码算好再绑定（`GachaTableColumnLayout`）。
- ScrollViewer 横向滚动没禁用时，按无限宽测量子元素，`TextTrimming` 失效、行变宽并出现横向滚动条。
- `Grid.RowSpacing` 对塌陷的行照样计入；`RowDefinition` 上的 `MinHeight` 会让隐藏的行仍占高度。可折叠元素优先用 `x:Load`（必须同时写 `x:Name`）。
- `FlyoutPresenter` 默认内边距是 `FlyoutContentPadding` = 16,15,16,17，最大宽 `FlyoutThemeMaxWidth` = 456；`ToggleSwitch` 上下各自带 10px 空行（`ToggleSwitchPreContentMargin` / `PostContentMargin`）。卡片底部留白偏大时，先查末元素自带的尾巴。
- `TextLineBounds="Tight"` 的 `TextBlock` 行框只有字高，要显式 `VerticalAlignment="Center"`，否则贴顶。
- 同一行里多个元素的 `Visibility` 必须来自同一个条件，否则会剩下半行孤儿元素。
- 还没连进可视树的虚拟化 `ListView` 提前 `Measure` 会少算高度；需要提前测高时给它固定高度。

### x:Bind 与 XAML 编译

- `x:Bind` 默认 `OneTime`，只在加载时求值一次；之后再改属性，要么用 `OneWay`，要么调 `Bindings.Update()`。
- 函数绑定只能绑实例方法，写成 `static` 会报 `CS0176`。
- 函数绑定返回 `bool` 再隐式转 `Visibility`，生成的代码编不过（`CS0103: 名称"obj"不存在`）；让函数直接返回 `Visibility`。属性路径绑定转 `Visibility` 没问题。
- 函数绑定的引用类型参数变回 null 时不会重新调用函数：属性变化通知走 `Update_属性名(obj)`，里面是 `if (obj != null)` 才把函数排进队列；只有初次加载和 `Bindings.Update()` 会照常以 null 调用。所以 `sh:BindConverter.ObjectToVisibility(SomeText)` 从有值清空后不会收起（只剩空元素，仍占一行高），`ObjectToVisibilityReversed` 收起后回不来；值类型参数（`bool` 等）不受影响。可空值转 `Visibility` 用 `Converter={StaticResource ObjectToVisibilityConverter}`（转换器遇 null 也会调用），`Window` 根上改绑算好的 `bool` 属性（`RestoreBackupDialog`、`WelcomeWindow`）。
- `Window` 根上的 `x:Bind` 不能用 `Converter={StaticResource …}`（`CS1503`）；改为在 code-behind 暴露算好的属性，或用函数绑定。
- 被 XAML 实例化的数据类（`x:Bind` 集合元素、`DataTemplate` 的 `x:DataType`）不能用 `required` / `init`（`CS8852` / `CS9035`），用普通 `{ get; set; }`。
- `ThemeResource` 键编译时不校验，写错要到运行时才崩。只用仓库里已有先例的键（先 `grep` 一下）。
- 私用区图标字符在 C# 里写 `"\uXXXX"`，XAML 里写 `&#xXXXX;`，不要写字面量（跨工具编辑会被吞掉）。
- ComboBox 折叠态显示的是缓存的 `SelectionBoxItem`，选中项 `Content` 的绑定更新后不会刷新（如切换语言后）；把 `SelectedIndex` 置 -1 再置回，`TwoWay` 绑定要防止中间值写进设置（`GeneralSetting`）。
- C# 编译失败（如漏了 `using Starward.Controls;`）会连带出一堆 `App.xaml` 的 `Unknown type` / `WMC9999` 假错误，先看真正的 C# 错误。
- `DispatcherQueueTimer` 在 `Microsoft.UI.Dispatching` 与 `Windows.System` 都有，`CompositionTarget` 在 `Microsoft.UI.Composition` 与 `Microsoft.UI.Xaml.Media` 都有；同时 using 时写全限定名。

### 图片、媒体与资源

- 新增本地图片放 `static\`，不要放 `Assets\`：csproj 里 `Assets\**` 是 `Content Update`（只改已有项），新文件不会进输出；`static\**` 才是 `Content Include`，用 `ms-appx:///static/...` 引用。
- WinUI 的图片缓存按 URI 记住解码失败：同一路径重新下载后，新的 `BitmapImage` 仍会直接 `ImageFailed`。重新加载坏过的地址要设 `BitmapCreateOptions.IgnoreImageCache`，且要在设 `UriSource` 之前（`CachedImage`）。
- `DecodePixelWidth` 和 `DecodePixelHeight` 同时设会按精确尺寸解码，把非正方形图拉伸；要省内存只设一维。
- `SurfaceImageSource` / `CanvasImageSource` 的内容在窗口隐藏后可能丢失，恢复显示时先闪一片空白；需要恢复瞬间就有画面的帧，用 `WriteableBitmap` 保存。
- 每 `new` 一个 `MediaPlayer` 就会多一个 `MediaPlayer SMTC window` 顶层窗口，`Dispose` 后也不消失。这不是泄漏，别据此排查。
- 帧服务器模式（`IsVideoFrameServerEnabled`）的 `MediaPlayer` 熄屏后会空转占满一个核心，暂停、最小化都停不下来，只有 `Dispose` 能停（`MainWindow` 监听显示器开关，`AppBackground` 释放与重建）。

### 窗口与 Win32

- `OverlappedPresenter.IsAlwaysOnTop` 实测不会给窗口加上 `WS_EX_TOPMOST`；需要压过系统 topmost 窗口（如托盘溢出面板）时，自己补一次 `SetWindowPos(HWND_TOPMOST, …)` 或 OR 扩展样式（`SystemTrayWindow`、`UpdateTipWindow`）。
- 弹出位置 / 尺寸按锚点所在显示器的 DPI 算（`UpdateTipWindow.GetScaleForPoint`），不要用窗口自身的 `UIScale`：窗口隐藏时可能还停在上一块屏的 DPI。
- 独立窗口要带自己的 `InAppToast`：`InAppToast.MainWindow` 只认 `Tag="MainWindow"` 的那个，托盘常驻时主窗口可能从未创建。
- 指针还停在标题栏按钮上时 `Hide()` 窗口，系统不会发 `WM_NCMOUSELEAVE`，再显示时按钮残留悬停色；隐藏前主动发一次 `WM_NCMOUSELEAVE`。
- 传给 Win32 的回调委托（如 `SUBCLASSPROC`）要用静态字段持有，否则被 GC 回收后成了野指针（`ResidentInstanceMessenger`）。

### 诊断

- `dotnet-dump` 的 `gcroot` 对 WinUI 控件常显示 0 个根，但对象其实被本机 XAML 树持有，不能据此判断已可回收；数控件实例用 `dumpheap -stat`。
