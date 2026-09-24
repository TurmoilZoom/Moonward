# 每日签到与自动签到

Moonward 支持米游社（国服）/ HoYoLAB（国际服）的**每日签到**活动奖励领取，覆盖原神、崩坏：星穹铁道、绝区零、崩坏3（各游戏国服 / 国际服，以及 B 服与国服签到活动的映射）。在手动签到之外，还提供按游戏独立开关的**自动签到**：软件启动后静默批量签一轮；只要进程还在（主窗口或系统托盘），UTC+8 每日刷新后会再签。

> **说明**：自动签到依赖已登录的战绩 Cookie，且仅在对应游戏开启开关后生效。功能默认关闭。触发风控、登录失效等情况时，自动流程会静默跳过或拉长间隔，需用户在界面手动处理。
>
> 本文对齐当前软件行为（`rebase/develop`，含 2026.9.2 及之后）。相对 2026-08-01 的 wiki：自动签到改为**进程常驻循环**；打开开关会**立刻检查**，不再提示「下次启动生效」；**不会**在快捷方式 / URL / 命令行启动游戏时给「该账号单独签一次」。跨日签到依赖软件保持运行，可配合开机自启到托盘。

---

## 一、使用说明

### 1. 前置条件

1. 在 **米游社工具箱 / HoYoLAB 工具箱** 中登录账号，并确保能拉取到对应游戏的**游戏角色**。
2. 首页右侧工具栏能看到**签到入口**（勾选动画图标）。若该游戏未支持签到、或本地没有可用角色，入口会隐藏。
3. Cookie 需保持有效；过期后需重新登录。国服在部分场景下会尝试用 `stoken` 静默刷新 Cookie。

### 2. 手动签到

1. 在启动页点击签到图标，打开签到卡片。
2. 卡片展示：
   - 当前角色头像、昵称、区服、UID
   - 本月累计签到天数、今日是否已签
   - 本月奖励日历（7 列网格；已领取天数有遮罩与对勾）
3. 点击 **签到** 领取今日奖励；**补签** 会先确认消耗补签货币后再请求。
4. 可随时点刷新图标重新拉取状态。

### 3. 开启自动签到

1. 在签到卡片底部找到 **自动签到** 开关（旁有问号图标）。
2. **按游戏独立开关**：例如原神国服与星铁国服互不影响；切换游戏时开关状态会跟当前游戏走。
3. 打开后会**立刻叫醒**后台循环去检查一轮，不必等下次启动。

问号提示要点：

- 每个游戏的自动签到相互独立
- 启动后会先签一轮，软件**常驻期间**每天刷新后再签
- 提示右下角有 **「软件开机自启」** 链接，点击会跳到设置 → 常规里的开机启动一节

### 4. 自动签到何时会执行

| 场景 | 行为 |
|------|------|
| **软件启动**（打开主窗口，或开机 / `--hide` 只开托盘） | 约 10 秒后，对所有「已支持签到 + 已开启自动签到」的本地游戏角色**静默批量签到** |
| **打开某个游戏的自动签到开关** | 立即检查一轮（不必等下次启动） |
| **重新登录 / 更新 Cookie** | 清掉这些角色的失败冷却，并立即再检查一轮 |
| **进程一直在跑**（主窗口或托盘） | UTC+8 当天 00:00 后再加约 3–8 分钟随机延迟，自动再签新的一天 |
| **电脑从休眠恢复** | 若已经到点，先随机错峰约 10–90 秒再签，避免多机同时打接口；未到点则只重算剩余等待 |
| **用快捷方式 / `moonward://startgame/...` / 命令行启动游戏** | **不再**给该账号单独签一次。若这次启动让 Moonward 变成常驻实例（当时还没有在跑），会走上面的「软件启动」批量；若软件本来就在跑，只按已排好的日程继续 |

**不会**在「仅切换当前游戏 Tab」时触发自动签到。批量任务挂在常驻循环上，与主界面是否正在前台无关。

### 5. 想跨日也自动签：让软件常驻

日界后再签的前提是 **Moonward 进程还在**。完全退出后，要等到下次启动才会再签。

建议：

1. 设置 → 常规 → **开机时自动启动**：登录 Windows 后以系统托盘启动（不弹主窗口）。可移动存储上的便携包不可用。
2. 关闭主窗口时选择 **最小化到系统托盘**，而不是「完全退出」。

从签到问号点进「软件开机自启」会直接滚到这一节。

### 6. 自动签到的用户体验约定

- **静默**：成功 / 已签 / 失败均不弹成功 Toast；细节写在日志中。
- **天然去重**：先查服务端「今日是否已签」；已签则跳过，不会重复 POST。
- **失败冷却**：某角色签到失败后约 **10 分钟**内不再对该角色重试，避免 Cookie 失效或风控时疯狂打接口。
- **节奏**：批量任务请求之间随机间隔约 **3–8 秒**，减轻短时间高频请求风险。
- **当天没签完**：断网等临时失败约 **30 分钟**后再试；Cookie 失效 / 风控约 **2 小时**后再试。当天 UTC+8 23:00 之后不再硬塞当天重试，改排到下一个 0 点。
- **补签不包含**：自动流程只做今日签到，不会消耗补签货币。

### 7. 手动 vs 自动边界

| | 手动签到 | 自动签到 |
|--|----------|----------|
| 入口 | 签到卡片按钮 | 开关 + 常驻后台循环 |
| 范围 | 当前展示的角色 | 所有符合条件的本地角色 |
| 补签 | 支持（需确认） | **不包含**补签，仅今日签到 |
| 失败反馈 | Toast / 统一 API 错误反馈，可引导重登或验证账号 | 只打日志；失败进入冷却或拉长间隔 |

### 8. 常见问题

**Q：开了自动签到为什么没立刻签？**  
A：打开开关会立刻叫醒后台循环。若仍没签上，常见原因是：刚启动还在约 10 秒缓冲里、该角色处于 10 分钟失败冷却、今日已签、Cookie 失效、或触发风控。看签到卡片手动刷新即可核对。

**Q：为什么某个号没签上？**  
可能原因包括：该游戏未开自动签到、本地没有该角色 Cookie、今日已签、处于失败冷却、Cookie 失效、触发风控需验证。可打开签到卡片手动刷新 / 签到，并检查工具箱登录状态。

**Q：隔夜没有自动签？**  
进程必须一直在。完全退出后不会在后台签。请开「开机自启」和/或关闭窗口时留在托盘。

**Q：用桌面快捷方式启动游戏，为什么这个号没单独签？**  
当前版本已经取消「启动游戏时给该账号单独签一次」。自动签到只走常驻批量：所有已开开关的游戏角色一起签。

**Q：B 服怎么处理？**  
B 服（`*_bilibili`）与国服共用签到活动侧配置；开关与角色按映射后的国服 biz（如 `hk4e_cn`）管理。

**Q：触发「需要验证」怎么办？**  
自动流程不会代打极验。请到米游社 / HoYoLAB 或应用内「验证账号」相关入口处理后再试。

---

## 二、实现细节

### 1. 分层架构

实现严格按 GameRecord 功能分层（与 SignIn 模板一致）：

```text
DTO / 活动配置  →  JsonContext  →  Client(CN/OS)
        →  GameRecordService  →  SignInService
        →  AutoSignInService  →  UI(SignInButton) / 常驻宿主
```

| 层 | 主要类型 / 路径 | 职责 |
|----|-----------------|------|
| DTO / 配置 | `Starward.Core/GameRecord/SignIn/*`、`SignInActivityConfig` | 奖励、状态、补签信息、POST body、retcode；按游戏映射 `act_id`、主机、`x-rpc-signgame`、可选 Origin |
| JsonContext | `GameRecordJsonContext` | 源生成序列化注册 |
| Client | `GameRecordClient` + `HyperionClient` / `HoyolabClient` | HTTP、Cookie、平台请求头；`CommonSendAsync` |
| 门面 | `GameRecordService` | 按角色选 CN/OS Client、设备指纹、请求恢复（Cookie 刷新等） |
| 业务 | `SignInService` | 状态聚合、奖励缓存、retcode → 结构化结果、风控判定 |
| 自动任务 | `AutoSignInService` + `SignInSchedule` | 开关读写、常驻循环、日界排期、失败冷却、请求节奏 |
| UI | `SignInButton`（挂在 `GameLauncherPage` 右侧工具栏） | 日历、手动签到/补签、自动开关、错误展示 |
| 常驻宿主 | `ResidentHost` ← `SystemTrayWindow` | 主窗口与仅托盘两条路径都会创建托盘窗口，从而拉起常驻循环 |
| 能力开关 | `GameFeatureConfig.SupportSignIn` | 按 `GameBiz` 是否展示/允许签到 |
| 设置 / DI | `AppConfig.Get/SetAutoSignInEnabled`、`ServiceProvider` | `auto_sign_in_enabled_{biz}`；单例注册 `SignInService` / `AutoSignInService` |
| 文案 | `Lang.*.resx` | 禁止硬编码用户可见字符串 |

### 2. 活动配置（易变常量集中）

`SignInActivityConfig.FromGame(game, isOversea)` 统一给出：

- `ActId`：活动 ID（随版本可能轮换，改此文件即可）
- `SignGame`：`x-rpc-signgame`（如 `hk4e` / `hkrpg` / `zzz` / `bh3`）
- `BaseUrl`：`home` / `info` / `sign` / `resign` / `resign_info` 前缀
- `Origin`：绝区零专用活动主机需要；其它游戏为 null

接口形态概要：

| 接口 | 方法 | 用途 |
|------|------|------|
| `home` | GET | 本月奖励列表 |
| `info` | GET | 已签天数、今日是否已签、服务器日期等 |
| `resign_info` | GET | 补签次数、货币消耗（部分活动可能无） |
| `sign` | POST | 今日签到 |
| `resign` | POST | 补签 |

POST body：`act_id` + `region` + `uid`（`SignInPostBody`）。

URL 上的 `lang` 目前 CN / OS 都固定 `zh-cn`（`GameRecordClient.SignInLanguage`，Hoyolab 未覆盖）。

### 3. CN / OS 客户端差异

差异**只在 Client 子类**，不散落到 UI。签到请求当前**不附加 DS**。

| | 国服 `HyperionClient` | 国际服 `HoyolabClient` |
|--|----------------------|------------------------|
| 公共头 | Referer（webstatic.mihoyo.com）、`x-rpc-signgame`、设备 id/fp、app version、client type | Referer（act.hoyolab.com）、同样带 `x-rpc-signgame` 与设备信息 |
| Origin | 绝区零 `act-nap-api` 等用配置里的 Origin；其它默认 `act.mihoyo.com` | 绝区零 `sg-act-nap-api` 对称需要 Origin；其它默认 `act.hoyolab.com` |
| 设备指纹 | 签到前 `PrepareSignInClientAsync` 会更新国服指纹 | 不走国服指纹更新 |
| Cookie 恢复 | 登录失效时可用 `stoken` 静默换票并重试一次 | 不换票 |

`GameRecordService` 按**角色**选 Client，签到前不改共享的 `IsHoyolab`——自动签到在后台跑，改那个字段会串到界面正在进行的账号操作上。每次签到 API 仍走 `ExecuteWithRequestRecoveryAsync`。

### 4. `SignInService`：业务编排与结果模型

- **`GetSignInStatusAsync`**：`info` + 缓存的 `home` 奖励 + 可选 `resign_info`（补签失败不影响主流程）。
- **奖励缓存**：键 `sign_in_reward_{gameBiz}_{yyyyMM}`；内存 `IMemoryCache` + SQLite `KVT` 双层；跨月或月份不一致则回源。
- **`ClaimSignInAsync` / `ClaimReSignInAsync`**：捕获 `miHoYoApiException`，映射为 `SignInActionResult`：
  - `Success` / `AlreadySigned` / `CookieExpired` / `RiskControl`
  - 补签相关：`NotEnoughCoin` / `ResignQuotaUsedUp` / `NoResignDate` / `PleaseSignInFirst`
  - 其它 → `Failed`
- **风控判定**（不能只看 `gt`）：`success == 1` 或 `risk_code != 0` 或 `is_risk` 或 `gt`/`challenge` 非空。

已知 retcode 常量见 `SignInReturnCode`（如已签 `-5003`、未登录 `-100` 等）。

UI 侧通过 `MiHoYoApiErrorFeedbackFactory` + `MiHoYoApiContext.SignIn` 展示错误，**禁止**页面内硬编码 retcode 文案。

### 5. `AutoSignInService`：自动签到核心

#### 5.1 开关存储

```text
Setting 表 Key: auto_sign_in_enabled_{GameBiz}
默认: false
```

`IsEnabled` / `SetEnabled` 委托 `AppConfig.Get/SetAutoSignInEnabled`。

#### 5.2 常驻循环（每个进程只启动一次）

入口：`SystemTrayWindow` 构造 → `ResidentHost.Start` → `AutoSignInService.StartResident()`。

主窗口路径会 `EnsureSystemTray()`，仅托盘路径（`--hide` / 快捷方式启动游戏且本进程留下当常驻实例）直接创建托盘窗口。因此**只要进程还在，循环就会跑**，不再挂在 `MainView.Loaded` 上。

循环骨架：

```text
缓冲 10s
→ 立刻跑一轮批量
→ 按聚合结果排 nextDue
→ 等到点（或被 Wake / ForceCheck 叫醒）
→ 若刚从休眠恢复且已到期：10–90s 错峰
→ 再跑批量 …
```

长等待拆成最多 15 分钟一段，每次醒来用墙上时钟重算剩余，避免电脑休眠后 `Task.Delay` 把「到点」算错。

#### 5.3 叫醒与强制检查

| API | 何时 | 作用 |
|-----|------|------|
| `RequestImmediateCheck` | 用户打开自动签到开关 | 置 ForceCheck 并 Wake。必须带 ForceCheck，否则循环可能按已排到明天的 `nextDue` 继续睡 |
| `NotifyRolesReauthenticated` | 工具箱重新登录 / 输入 Cookie / 短信登录成功 | 清这些角色的失败冷却，再 `RequestImmediateCheck`。Cookie 失效会把下一轮排到 2 小时后，只叫醒不清冷却的话，10 分钟内重登仍会被冷却跳过 |
| `NotifySystemResumed` | 托盘窗口收到休眠恢复广播 | 只 Wake 去重算绝对时刻；未到点不跑批量 |

#### 5.4 一轮批量 `RunBatchCoreAsync`

1. `GetAllGameRoles()`（`ORDER BY Cookie, GameBiz`，同账号角色相邻），过滤 `SupportSignIn`
2. 本机推算期望服务器日期：`SignInSchedule.GetServerDate`（UTC+8）
3. 对每个角色：实时读 `IsEnabled(role.GameBiz)`（中途关掉会跳过）；`SignInRoleCoreAsync`
4. 单角色异常只记日志，不中断整批；入口有 `_batchGate`，防止 ForceCheck 与循环重叠

#### 5.5 单角色 `SignInRoleCoreAsync`

```text
失败冷却中？ → Cooldown
pace() → GetSignInInfoAsync
  已签且服务器没翻天 → Early（本轮其余角色不再请求）
  已签且已翻天 → AlreadySigned
pace() → ClaimSignInAsync
  Success / AlreadySigned：清冷却
    若服务器还没翻天 → SignedPreviousDay（签掉的是上一天，必须短重试接住今天）
    否则 → Signed
  CookieExpired / RiskControl → Blocked，写失败时间
  其它失败 / 抛错 → Failed（登录失效抛错也按 Blocked）
```

失败冷却键：

```text
auto_sign_in_last_failure_ticks_{GameBiz}_{Uid}
```

存 `Setting` 表；值为 `0` 表示无冷却。冷却窗口 **10 分钟**。查询接口失败、断网同样写入失败时间（避免下一轮立刻重来）。

#### 5.6 一轮聚合 → 下次到期

优先级：**Early > Incomplete > Blocked > Completed**。

| 聚合结果 | 含义 | 下次到期 |
|----------|------|----------|
| `Early` | 服务器日期尚未翻天：问早了（短路其余角色），或刚签掉的是上一天（其余角色照常跑完） | 连续前 3 次：现在 + 随机 10–20 分钟；再多则视为本机时钟偏快，改走当天 30 分钟间隔 |
| `Incomplete` | 仍有角色因断网、通用失败或冷却未签成 | 当天 30 分钟后再试 |
| `Blocked` | 仅剩 Cookie 失效 / 风控 | 当天 2 小时后再试 |
| `Completed` | 启用的角色都已签，或没有可签角色 | 下一个 UTC+8 0:00 + 3–8 分钟抖动 |

`SignInSchedule.GetRetryOrNextDay`：若现在已过当天 UTC+8 23:00，或 `now + 重试` 会跨过 0 点，都改排下一个 0 点+jitter。避免单个账号的失败把其它账号第二天的签到一起拖晚。

是否该签仍看接口的 `IsSign` / `Today`；本机时钟只负责「何时去问」。`Today` 为空或解析失败时视为已翻天，避免卡在 Early 短重试死循环。

#### 5.7 请求节奏（仅批量）

| 参数 | 值 | 作用 |
|------|-----|------|
| `StartupBatchDelay` | 10s | 错开启动高峰 |
| `MinRequestDelaySeconds` | 3 | 请求间隔下限 |
| `MaxRequestDelaySeconds` | 8 | 请求间隔上限（含） |

第一个网络请求不延时；之后每个请求前随机 sleep。`pace` 在 info 与 claim 前各调用一次，因此相邻角色之间也会被间隔拉开。

### 6. UI：`SignInButton`

- 挂在启动页右侧工具栏；统一 Lottie 勾选图标（默认静止，悬浮播放一次后停在末帧）。
- `CurrentGameId` 变化时：Feature 门控、B 服→国服、同步自动开关、取「上次选中或首个」角色决定是否显示。
- Flyout **懒加载**状态（首次打开才 `GetSignInStatusAsync`）；日历区固定高度，避免数据回填后 Flyout 贴底。
- 手动签到/补签走 `InAppToast`；风控可跳「验证账号」；Cookie 失效走统一恢复动作（重登 / 验证）。
- 打开自动签到开关时立刻 `RequestImmediateCheck`；**不会**在切换游戏时触发批量签到。
- 问号为点击触发的 `InstantTooltip`，右下角「软件开机自启」链到设置；提示开着时拦截 Flyout 被外层点击关掉，以便点到链接。
- `x:Bind` 属性须在 UI 线程赋值（与全项目约定一致）。

### 7. 开机自启与关闭窗口

- `AutoStartService`：写当前用户 `Run` 键，命令为当前 exe + `--hide`，不触发 UAC。正式版与 Debug 值名分开（`Moonward` / `Moonward.Debug`）。
- `--hide` 只开托盘、不显示主窗口；托盘窗口仍会 `StartResident()`。
- 关闭主窗口可选「最小化到系统托盘」或「完全退出」；完全退出后常驻循环结束。

### 8. 数据流示意

```text
[用户开自动签到] → Setting: auto_sign_in_enabled_{biz}=true
                 → RequestImmediateCheck (ForceCheck + Wake)
                              │
 SystemTrayWindow → ResidentHost.Start → StartResident
                              │
                    RunResidentLoopAsync
                    10s 缓冲 → 批量 → 排 nextDue → 等待 / Wake
                              │
                    RunBatchCoreAsync（所有已开开关的角色）
                              │
                    SignInRoleCoreAsync
                       │        │
               GetSignInInfo    ClaimSignIn
                       │        │
                 GameRecordService → Hyperion/Hoyolab Client
                              │
                       米游社 / HoYoLAB 签到 API
```

### 9. 安全与风控注意（维护者）

- 自动签到**不提交极验**；遇到风控只记结果并进入失败冷却 / Blocked 长间隔。
- 批量节奏、冷却、日界抖动、休眠错峰是对服务端限流/风控的软缓解，**不能保证**永不触发验证。
- `act_id` / 主机随活动轮换；失效时优先改 `SignInActivityConfig`，勿在 UI 层写死 URL。
- Core 层禁止引用 WinUI；异常保留服务端原文，本地化只在 UI / Factory。
- 签到请求不要改共享的 `GameRecordService.IsHoyolab`。

### 10. 关键文件索引

| 文件 | 说明 |
|------|------|
| `Features/GameRecord/SignIn/AutoSignInService.cs` | 常驻循环、冷却、一轮聚合 |
| `Features/GameRecord/SignIn/SignInSchedule.cs` | UTC+8 日界与下次到期（纯计算） |
| `Features/GameRecord/SignIn/SignInService.cs` | 签到业务与结果映射 |
| `Features/GameRecord/SignIn/SignInButton.xaml(.cs)` | 启动页签到 UI |
| `Features/Startup/ResidentHost.cs` | 拉起常驻循环 |
| `Features/ViewHost/SystemTrayWindow.xaml.cs` | 常驻宿主；休眠恢复通知 |
| `Features/Setting/AutoStartService.cs` | 开机自启到托盘 |
| `Features/GameRecord/GameRecordService.cs` | 签到 API 门面、按角色选 Client |
| `Features/GameRecord/GameRecordCookieRefreshService.cs` | 国服 stoken 换票 |
| `Core/GameRecord/GameRecordClient.cs` | 签到 HTTP 公共实现 |
| `Core/GameRecord/HyperionClient.cs` / `HoyolabClient.cs` | 平台请求头 |
| `Core/GameRecord/SignIn/SignInActivityConfig.cs` | act_id / 主机映射 |
| `AppConfig.Setting.cs` | `auto_sign_in_enabled_{biz}` |
| `Features/GameFeatureConfig.cs` | `SupportSignIn` |

---

## 三、摘要

**每日签到**在启动页卡片上完成手动领取与补签；**自动签到**按游戏独立开关。软件启动后静默批量为所有已登录且已开开关的角色签到；进程常驻期间在 UTC+8 日界后再签。打开开关、重新登录会立即再检查。实现上通过「服务端已签状态 + 失败冷却 + 随机请求间隔 + 按结果排期」控制幂等与频率。CN/OS 差异收敛在 Client，业务结果与风控在 `SignInService` 统一建模，UI 与常驻循环只消费结构化结果。
