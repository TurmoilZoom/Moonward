---
name: debug-ui-verify
description: >
  在本机运行 Moonward Debug 版，核对界面改动的外观与交互：构建与启动、与安装版的区别、
  computer-use 授权、用 UI 自动化读控件坐标、注入输入、逐帧截屏，以及收尾。
  Use when a change needs to be seen in the running app: 看效果、实机核对、截图确认、
  验证布局 / 对齐 / 动画 / 悬停 / 拖拽 / 托盘菜单、复现界面问题.
---

# Debug 版界面验证

只在需要看实际效果时启动应用；编译或读代码就能确认的不必跑。验证规则本身（命中测试、跨 DPI、主题等）见 `.agents/rules/winui-ui.md`。

## 1. 构建与启动

- 编译：`dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64`。
- 程序：`src\Starward\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Moonward.exe`。
- 启动前先看有没有正在运行的 Debug 实例（进程路径在 `bin\x64\Debug\` 下）。它占着文件时重新编译会失败：确认是自己启动的再关掉，用户开着的先问。
- 只按 PID 结束自己启动的进程，不要按进程名批量结束：安装版的进程名也是 `Moonward.exe`。
- 带 `--welcome` 启动会弹出引导页，用于调试数据目录迁移。

## 2. Debug 版和安装版的区别

| | Debug 版 | 安装版 |
|---|---|---|
| 数据目录 | 固定为程序目录下的 `data`（开发库） | 注册表 `HKCU\Software\Moonward` 的 `DataFolder` |
| 注册表键 | `HKCU\Software\Moonward.Debug` | `HKCU\Software\Moonward` |
| 托盘图标 | 默认常显在任务栏 | 默认收在「隐藏的图标」面板里 |

- Debug 版读写的是开发库，看到的数据和用户的真实数据不同；需要特定数据时，用 SQLite 的 backup 从别的库复制一份到 Debug 数据目录，不要直接拷贝正在使用的数据库文件，也不要让 Debug 版指向安装版的数据目录。
- 两者同时运行时，Debug 版会截走安装版的「游戏已启动」进程间通知。测快捷方式、托盘、热键截图前，先关掉其中一个。
- 托盘菜单被溢出面板遮挡这类问题，只在图标收进面板时复现。要在 Debug 版复现，把 `HKCU\Control Panel\NotifyIconSettings` 里对应 Debug 版 exe 的 `IsPromoted` 改为 0。
- 切换界面语言：改 `HKCU\Software\Moonward.Debug` 的 `Language`，重启 Debug 版。所有 Debug 构建共用这个键，看完要改回。

## 3. 用 computer-use 操作

- 请求授权时传进程名 `moonward.exe`。写 "Moonward" 会解析到安装版的路径，Debug 版窗口会被遮住、无法操作。文件选择框属于 `pickerhost.exe`，要单独授权。
- 有别的程序弹窗抢前台时，用 `SetForegroundWindow` 把 Debug 窗口拉回来。
- 需要悬停效果时，把 Debug 窗口临时置顶：Claude 窗口挡在前面时，光标会落在 Claude 窗口上。
- 拖动窗口时，终点不要落在没有授权的桌面上，否则会被拦下。
- 截图分辨率不够看细节时，用 PowerShell `CopyFromScreen` 按窗口矩形截原尺寸，再裁剪放大。

## 4. 核对布局与交互

- **对齐与尺寸**：用 UI 自动化（UIA）读控件的 `BoundingRectangle`，逐个比较 x、宽度，比看截图可靠。UIA 也能直接调用按钮（`InvokePattern`）来切换状态。
- **响应式**：用 `MoveWindow` 改窗口宽度，看最小宽度、换行和截断。
- **鼠标输入**：用 `SendInput` 按绝对坐标注入（x64 下 `INPUT` 结构为 40 字节）。`SetCursorPos` 不会产生 WinUI 指针事件，悬停和拖拽都不会触发。
- **滚动与动画**：后台线程用 `CopyFromScreen` 连续截取目标区域（约 11 ms 一帧），逐帧比较位移；位移出现来回跳动，就说明有两套滚动或动画在互相拉扯。
- **要覆盖的状态**：浅色和深色主题；系统「动画效果」关闭时的终态；有缩放不同的多块显示器时，把窗口拖到另一块屏上。
- **交互手测**：点击、悬停、拖拽、滚轮、Tab 焦点，以及叠层关闭后下层是否还能操作。

## 5. 调试输出

- `Debug.WriteLine` 的输出可以用 DBWIN_BUFFER 监听抓取，不必挂调试器。
- 临时 trace 用环境变量开关控制，验证完删掉，不要提交。
- Debug 版日志在 `data\log\`，读法见 `.agents/skills/log-eval/SKILL.md`。

## 6. 收尾

- 报告写清看过哪些状态（语言、主题、缩放、窗口宽度）、哪些没看，截图注明对应的状态。
- 结束自己启动的进程，把改过的注册表值（`Language`、`IsPromoted`）改回去。
