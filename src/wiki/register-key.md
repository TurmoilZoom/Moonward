# 快捷方式启动与常驻实例

Moonward 支持用桌面快捷方式、命令行 `startgame` 或 `moonward://startgame/{game_biz}` 直接拉起游戏。这类入口会再开一个进程；若拉起后立刻退出，全局热键、手柄接管、游戏进程登记都会一起消失。当前设计把「启动游戏」和「常驻宿主」拆开：由一个长生命周期的托盘实例负责热键、手柄和运行中游戏的追踪。

相关入口与文档：`docs/UrlProtocol.zh-CN.md`；实现主要在 `Features/Startup/`、`Features/UrlProtocol/`、`Features/ViewHost/SystemTrayWindow.xaml.cs`、`Features/Overlay/RunningGameService.cs`。

## 问题

原先走快捷方式 / 协议时，短进程启动游戏后立即退出。后果是：

- 没有消息循环，`RegisterHotKey` 的 `WM_HOTKEY` 无处可投，`Alt+D` 截图、`Alt+S` 唤起窗口失效。
- 即使已有托盘实例，快捷方式拉起的游戏也不会登记到 `RunningGameService`。截图拿到的「当前游戏」为 `null`，表现为静默失败。
- 后台职责曾挂在 `MainView_Loaded` 上。开机自启 `--hide` 只建托盘、不加载主界面，热键、手柄、签到预热等全部缺席。

热键挂在主窗口上也不行：仅托盘驻留或快捷方式启动时主窗口可能尚未创建。这是 issue #10 的核心之一。

## 入口

| 方式 | 形态 | 处理 |
|------|------|------|
| 命令行 | `Moonward.exe startgame --biz {game_biz}` | `StartGameStartupHandler` |
| URL 协议 | `moonward://startgame/{game_biz}?install_path=&profile=&uid=` | `UrlProtocolStartupHandler` → `UrlProtocolService` |
| 开机静默 | `--hide` | 只建托盘，不弹主窗口 |

`moonward://` 在设置里启用后写入注册表 `"Moonward.exe" "%1"`。非打包 WinUI 应用里，系统只是把 URL 当命令行参数交给新进程，不会走打包应用的 `ProtocolActivatedEventArgs`。

启动链：`Program.Main`（Velopack 最先）→ `App.OnLaunched` → 环境初始化 → `IStartupHandler` 职责链（rpc → playtime → startgame → urlprotocol）。`moonward://test/` 在环境初始化之前单独处理。

`startgame` 与 `moonward://startgame` 成功拉起游戏后，都交给 `GameLaunchStartupCoordinator.AfterGameStarted`。

## 收尾策略

`GameLaunchStartupCoordinator` 只做一件事：启动成功后，本进程是退出还是留下来当托盘。

```
已有常驻实例
  → ResidentInstanceMessenger.NotifyGameStarted(biz, pid)
  → 短进程退出（StartupOutcome.Exit）

没有常驻实例
  → 本进程转为托盘常驻（StartupOutcome.Continue）
  → App 创建 SystemTrayWindow，并把游戏登记进 RunningGameService

启动失败
  → 维持「用完即退」，不留下空托盘
```

`App.OnLaunched` 在 `IsGameLaunchRequest` 或 `--hide` 时只建托盘、不弹主窗口。并发两个快捷方式抢注 `AppInstance("main")` 时，落败方把已拉起的游戏用 IPC 转给胜出方，且不 `RedirectActivationToAsync`，避免只想开游戏却弹出主窗口。

## 为什么不用激活重定向

`AppInstance.RedirectActivationToAsync` 不适合这条路径：

1. 免 UAC 快捷方式由计划任务以最高权限启动 Moonward，再由该进程拉游戏。若把激活转给普通权限的常驻实例，游戏会改由常驻侧 `runas` 启动，UAC 弹窗回来，免 UAC 失效。
2. 非打包应用里，主实例 `Activated` 事件的 `Arguments` 经常是空字符串，拿不到第二实例的命令行。
3. 高权限临时进程向普通权限主实例转发，还受 UIPI 限制。

因此只做单向、最小通知：短进程自己把游戏拉起来，再把 `{game_biz}|{pid}` 交给常驻实例登记。

## 跨进程通知

`ResidentInstanceMessenger` 是常驻托盘与快捷方式短进程之间的单向通道。

常驻进程里同时有两个窗口：

| 窗口 | 作用 |
|------|------|
| `SystemTrayWindow` | 托盘图标、菜单；注册并接收 `WM_HOTKEY`；生命周期与进程等长，`AppWindow.Closing` 恒取消 |
| `Moonward.ResidentIpc` | 父窗口为 `HWND_MESSAGE`（-3）的 message-only 窗口，无 UI、不抢焦点，只收 `WM_COPYDATA` |

IPC 窗口用系统 `STATIC` 类创建，再用 `SetWindowSubclass` 挂 `SubclassProc`。客户端用 `FindWindowEx(HWND_MESSAGE, …, "STATIC", "Moonward.ResidentIpc")` 定位。窗口按会话隔离，标题不必再加用户/会话后缀。主界面打开还是托盘最小化，寻址方式相同。

投递：

1. 短进程拉起游戏，得到 `(biz, pid)`。
2. `FindWindowEx` 找 IPC 窗口。
3. `SendMessageTimeout(WM_COPYDATA, payload="{biz}|{pid}", 3s)`。必须同步发送，否则 `COPYDATASTRUCT` 的指针在处理期间会失效。
4. 常驻侧解析后 `RunningGameService.AddRuninngGame`，并 `WeakReferenceMessenger.Send(GameStartedMessage)`，主界面按「游戏启动后隐藏/最小化」设置响应。
5. 短进程退出。

找不到窗口则返回 `false`，协调器把本进程转为托盘常驻。

UIPI 方向：

- 高 → 低（免 UAC 快捷方式通知普通权限托盘）：系统允许。
- 低 → 高（Moonward 以管理员运行、快捷方式是普通权限）：默认拦截。常驻侧对 IPC 窗口调用 `ChangeWindowMessageFilterEx(..., WM_COPYDATA, MSGFLT_ALLOW)` 放行。

跨完整性级别打不开游戏进程句柄时只记日志，不弹窗。

## 常驻宿主

后台职责从 `MainView_Loaded` 挪到 `ResidentHost`，由 `SystemTrayWindow` 构造时启动（先开 IPC，再拉后台任务）。幂等，每个进程一次。

| 职责 | 现由托盘拉起 |
|------|----------------|
| 全局热键（默认 `Alt+S` / `Alt+D`，Id 44444 / 44445） | 是，注册在托盘窗口 |
| 手柄驱动、GameBar 引导键接管与自愈 | 是 |
| 抽卡物品名缓存 / 迁移 | 是 |
| 启动批量静默签到 | 是 |
| RPC 环境变量下发 | 是 |
| 检查更新 / 展示更新说明 | 否，仍在主界面（要弹 UI） |

热键必须收口在托盘窗口：设置页改键也注册到同一句柄。宿主若分裂到主窗口，主窗口关到托盘后热键会失效，`UnregisterHotKey` 也会打到错误窗口。

`WM_HOTKEY` 在 `SystemTrayWindow.WindowSubclassProc` 分发：44444 尝试打开覆盖层，失败则 `EnsureMainWindow`；44445 走 `ScreenCaptureService.Capture()`。

## 游戏登记与前台追踪

`RunningGameService.AddRuninngGame` 会：

- 按 PID 去重后加入列表，并记为 `_latestActiveGame`。
- 对该 PID 安装 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)`，前台切到该游戏时更新「最新活跃」。
- 触发 GameBar 引导键接管；用 1s 定时器轮询退出，退出后 `UnhookWinEvent`。

`SetWinEventHook` 绑定安装它的线程的消息泵。必须在 UI 线程安装；快捷方式 IPC 回调已在托盘 UI 线程上。脱离 UI 线程会导致事件丢失。这是进程外异步钩子，不向游戏注入 DLL。

截图依赖 `GetLatestActiveGame()`。未登记则直接返回，无提示——所以快捷方式路径必须把游戏通知到常驻实例。

Moonward **没有**后台持续轮询或 WMI/ETW 监听进程创建。官方启动器开的游戏，托盘侧默认感知不到：

| 场景 | UI | `Alt+D` | 游玩时长 |
|------|----|---------|----------|
| 官方启动器开游戏，Moonward 在托盘 | 未感知 | 不可用 | 不记录 |
| 官方启动器开游戏后打开/切换 Moonward 界面 | 可显示「运行中」（主界面按进程名扫描） | 可恢复 | 不记录 |
| 经 Moonward 界面或快捷方式 / 协议启动 | 运行中 | 可用（快捷方式需 IPC 登记） | 界面启动可记录 |

主界面还有一条被动刷新：最小化超过约 10 分钟或跨整点后再激活会重扫；短时间切回可能仍显示「开始游戏」，切一下侧栏游戏可强制刷新。

## 热键与完整性级别

米哈游游戏启动时常要管理员权限，游戏进程多为 High IL；Moonward 日常为 Medium IL。

UIPI 的 No Write-Up：低 IL 不能向高 IL 窗口发消息、挂钩、注入 `SendInput`。因此：

- 模拟按键、向游戏窗口 `PostMessage`、`SetWindowsHookEx` 注入：Medium → High 会失败。
- 全局热键不是游戏发给 Moonward 的。内核输入子系统匹配已注册组合后，把 `WM_HOTKEY` 直接塞进注册者（托盘窗口）的队列。来源是系统级，游戏不参与转发。
- 但当前台窗口属于比注册者更高 IL 的进程时，系统可以不把 `WM_HOTKEY` 投给注册者。普通权限 Moonward + 提权游戏在前台时，`Alt+D` 仍可能被抑制。笔记中的「日常能截到高 IL 游戏」与这条系统规则并存，实际取决于前台窗口 IL 与注册进程 IL 的相对关系。

进程内 `WeakReferenceMessenger` 只用于同进程订阅（如主窗口响应 `GameStartedMessage`），不能替代跨进程 IPC。

## 协议启动游戏参数

```
moonward://startgame/{game_biz}?install_path={install_path}&profile={profile}&uid={uid}
```

- `install_path`：游戏可执行文件所在目录（可选）。
- `profile`：`none` / `configN`；省略则跟随当前生效的启动方式。
- `uid`：国服且 Cookie 含有效 `stoken` 时，启动前换 auth ticket 自动登录；自定义启动器不注入 token，UID 仅用于签到。

## 仍存在的边界

- 官方启动器开的游戏：托盘不会自动登记，截图与时长记录不完整。
- 普通权限常驻实例 + 高 IL 游戏在前台：系统可能抑制 `WM_HOTKEY`。
- Medium IL 不能向 High IL 游戏注入输入或钩子；截图走的是本进程对窗口的捕获，不是往游戏里写。
- IPC 打不开跨 IL 的游戏进程句柄时，登记失败且无 UI 提示。

---
