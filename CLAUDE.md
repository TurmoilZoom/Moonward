# CLAUDE.md

本仓库 AI / 开发指南的**权威详版**。`AGENTS.md` 为精简版；两者冲突时**以代码现状为准**。

## 项目概述

Moonward（产品品牌；工程目录与 C# 命名空间仍为 `Starward.*`）：Windows 米哈游游戏启动器（WinUI 3 / .NET 10），支持原神（hk4e）、星穹铁道（hkrpg）、绝区零（nap）、崩坏3（bh3）。

本仓为 **fork**（上游 Scighost/Starward，发布走 TurmoilZoom/Starward + Velopack，安装包身份为 Moonward）。日常开发在 `rebase/develop`，会周期性变基上游——**许多自定义改动易在变基中丢失**。

## 构建与运行

**没有 .sln**，直接编主项目（会带上 ProjectReference）：

```powershell
# 日常验证（最快）
dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64

# Release 自包含需指定 RID
dotnet build src/Starward/Starward.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

- **0 error 即视为通过**（无单元测试；必要时手动跑应用）。Release 全量约数分钟级。
- SDK 由 `global.json` 锁定——**不要擅自升级 SDK 或 NuGet 包**。
- 平台：`x86` / `x64` / `ARM64`。CI（`.github/workflows/build.yml`）跑 Debug×Release × x64×arm64，使用 `dotnet publish ... -r win-<plat> -p:DefineConstants=DONOT_CHECK_UPDATE`。
- 常量：`DONOT_CHECK_UPDATE` 跳过启动更新检查（CI）；`DISABLE_XAML_GENERATED_MAIN` 由 `Program.Main` 接管入口。
- 不提交 `bin/`、`obj/`、日志。

## 解决方案结构

| 项目 | 作用 | 约束 |
|------|------|------|
| `src/Starward/` | UI、Features、DI、设置（`AppConfig.*`） | WinUI 3 |
| `src/Starward.Core/` | API Client、DTO、`*JsonContext`、`GameBiz` | **禁止 WinUI** |
| `src/Starward.Language/` | 应用文案 `Lang.*.resx`（Crowdin） | 见「本地化」 |
| `src/Starward.RPC/` | 提权安装等 gRPC（命名管道） | 独立进程 |
| `src/Starward.Setup.Core/` | GitHub Release 说明（`ReleaseClient`） | 更新本体由 Velopack 负责 |

## 核心架构

### AppConfig（`static partial class`，`AppConfig.*.cs`）

服务定位器 + 设置仓库 + 路径中心，不是普通配置类：

- **`ServiceProvider`**：手写 `ServiceCollection`（非 Generic Host）；`GetService<T>()` 懒构建；**新服务在此注册**
- **`Setting`**：全局设置 = `static` 属性（`GetValue`/`SetValue` + `[CallerMemberName]`）；按游戏 = `GetXxx(biz)`/`SetXxx(biz)`；落库 `Setting` 表
- **`Common` / `Configuration`**：`DeviceId`、`SessionId`、JSON 选项、版本/便携/缓存路径等
- 数据目录：用户所选目录下的 `data`（`UserDataFolder`）；子进程用 `--data-folder` 对齐

### GameBiz

`Starward.Core/GameBiz.cs`（`record struct`），如 `hk4e_cn`（`Game` / `Server` 以下划线分）。按游戏分支几乎都以它为 key。

### GameFeatureConfig

`Features/GameFeatureConfig.cs`：`FromGameId` 按 `GameBiz` 返回能力开关（页面、云游戏、硬链接、实时便笺、签到等）。**给某游戏开功能 = 对应 `Support*` 置 true**。

### GameRecord 分层（以 SignIn 为参考）

新增类米哈游 API 功能时严格按序：

**DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang**

| 层 | 位置 | 职责 |
|----|------|------|
| DTO | `Core/GameRecord/<功能>/*.cs` | 请求/响应、返回码枚举 |
| 活动配置 | `*ActivityConfig.cs`（按需） | `FromGame(game, isOversea)` 映射 act_id/主机/头；**给游戏开功能 = 加一条**；易变常量集中于此 |
| JsonContext | `GameRecordJsonContext.cs` | 新 DTO **必须注册**（源生成） |
| Client | `GameRecordClient` + `HyperionClient`(CN) / `HoyolabClient`(OS) | HTTP/签名/序列化；**CN/OS 差在子类**，不散落到 UI |
| Service 门面 | `GameRecordService` | 选区服 Client |
| 业务 / 后台 | `<功能>Service` / `Auto*`（按需） | 缓存、结构化结果、自动任务 |
| UI / 开关 | 控件挂 `GameLauncherPage`；`GameFeatureConfig.Support*` | 交互与按游戏启用 |
| 设置 / DI | `AppConfig.Setting` / `ServiceProvider` | 按游戏设置、注册 |
| 文案 | 全部 `Lang.*.resx` + `Designer` | 见「本地化」 |

客户端约定：`CommonSendAsync`；签名 `CreateSecret()`（Gen1/LK2）；JSON 一律 `*JsonContext.Default`。签到 act_id 等以 `SignInActivityConfig` 文件头注释为准（部分游戏/区服为待核对猜测，**只改该文件即可**）。

### 米哈游 API 错误反馈

入口：`Features/MiHoYoApiErrorFeedback.cs`（`MiHoYoApiErrorFeedbackFactory`）。禁止页面/服务自行 `switch` retcode、硬编码 Toast，或把 `Exception.Message` 当已本地化文案。

- **Core**：战绩/签到/认证/passport → `miHoYoApiException`；抽卡 authkey → `GachaApiException`。`ResponseMessage` 保留服务端原文；异常类不得引用 `Lang`。
- **UI**：`Create(exception, MiHoYoApiContext.XXX)`。同一 retcode 随 Context 变——`PassportCaptcha` 与战绩分开；抽卡/authkey 失效 → `RefreshUrl`，勿提示重登米游社。
- **显示与恢复分离**：主窗口 `Show(feedback, onRecovery)`，恢复由页面接线；对话框内 `Create` + 框内 `InfoBar`。未知码保留原文与状态码。
- **扩展**：先选准 Context，再在 Factory 集中加映射；勿把语义不同的码硬揉一起。文案走「本地化」。

### GameRecord 登录、设备指纹与 Cookie

- **入口**：国服短信验证码 + 手动 Cookie；国际服（HoYoLAB）无 passport 短信 → **WebView 网页登录**（`LoginPage`）+ 手动 Cookie。完全失效走上述入口；国服不要改回网页登录。
- **静默换票**：验证码登录保证 `stoken`/`mid`。国服失败 → `ExecuteWithRequestRecoveryAsync`（冷却刷指纹；`IsLoginExpired` 时 `GameRecordCookieRefreshService` 换票回写，**最多重试一次**）。缺 stoken 或失败 → 上层登录失效反馈。
- **分层**：`MihoyoPassportClient`（协议/DTO）→ `CaptchaLoginService`（发码/登录/aigis/拼 Cookie）与 `GameRecordCookieRefreshService`（换票+DB）→ `CaptchaLoginDialog` / `GeetestVerifyPopup`（UI 回调极验）。换票勿堆 `HyperionClient`；Core 禁 WinUI；CN/OS 差留在 Client/Service。
- **安全**：passport/换票前同步 device_id/fp；日志可记流程与手机号后四位，**禁止** Cookie/Token/验证码/authkey/完整手机号。新 DTO 注册 JsonContext；新服务注册 `ServiceProvider`。

### 数据库（SQLite + Dapper）

`Features/Database/DatabaseService.cs`：`StarwardDatabase.db`；`KVT`、`Setting` + 各游戏业务表。

- 迁移：`DatabaseSqls` 中每个 `Sql_vN` 一次迁移，按 `PRAGMA USER_VERSION` 跳过已执行。
- **改 schema 只追加新 `Sql_vN` 并更新 `USER_VERSION`**，禁止改已发布迁移。
- 自定义类型：`DapperSqlMapper`（如 `GameBiz` 存字符串）。

### 启动流程

1. `Program.Main`：**Velopack 必须最先**（`VelopackApp.Build().Run()`），处理安装/更新/卸载后可直接退出。**禁止在 Velopack 前插入逻辑。**
2. `App.OnLaunched`：
   - 先特判 `moonward://test/`（须在环境/DI 之前，否则 `CacheFolder` 未就绪会崩）
   - `CheckEnviromentAsync()`
   - `DispatchStartupAsync`：按 DI 注册顺序跑 `IStartupHandler`（当前：rpc → playtime → startgame → urlprotocol）；`StartupOutcome.Exit` 则 `Environment.Exit`
   - 单实例（`main`）→ `MainWindow` 或仅托盘（`--hide`）
3. 新命令行模式：实现 `IStartupHandler`（`Features/Startup/`）+ DI 注册；动词常量 `StartupVerbs`。

### 更新分发

Velopack + GitHub Releases；预览版 = pre-release，按架构分渠道。`ReleaseClient` 只拉发行说明，不负责更新本体。

## 本地化

- 应用 UI：`Starward.Language/Lang.*.resx`（Crowdin）。**禁止硬编码用户可见字符串**。
- Core 领域文案（抽卡类型、自助查询类型等）：`Starward.Core/Localization/CoreLang.*.resx`，**增改规则与 Lang 相同**。
- API 错误展示文案走 `MiHoYoApiErrorFeedbackFactory` + 资源键，不在异常/页面硬编码。
- 已维护语言以仓库内 `Lang.*.resx` / `CoreLang.*.resx` 文件名为准（含默认 `Lang.resx`、zh-CN/HK/TW、de/es/it/ja/ko/ru/th/vi 等）。
- **`dotnet build` 不重生 Designer**；`Lang.Designer.cs` / `CoreLang.Designer.cs` 须手改。

**新增词条**

1. 默认/英文 resx  
2. `zh-CN`  
3. 对应 `*.Designer.cs` 属性  
4. **补全其余全部已维护语言的同名键**（不会的语言可用英文占位，键不可缺）

**修改词条**

1. 更新源语言（通常 `Lang.resx` + `zh-CN`）  
2. **逐语言核对**该键译文是否仍匹配新语义（含 `{0}` 等占位符）  
3. 过时/错误译文一并改；勿只改英/中

文档翻译：`docs/文件名.<语言-地区>.md`。

## UI 约定

- 页面：`PageBase`；导航参数 `GameId` / `GameBiz`
- 亚克力背景；动画优先 Composition（`Helpers/FluentAnimations.cs`），少用 Storyboard
- `DropDownButton` 弹出层无法亚克力，不要强行改造
- 悬停说明优先 `InstantTooltip` / `InstantTooltipHost`
- 跨组件：`CommunityToolkit.Mvvm.Messaging`；日志：`ILogger<T>`
- **x:Bind 绑定的 `ObservableObject` 属性必须在 UI 线程赋值**；在 `ConfigureAwait(false)` / `Task.Run` 内赋值会 `COMException 0x8001010E` 且易从 catch 逃逸
- **不要升级** `CommunityToolkit.WinUI.Controls.Segmented`（csproj 有回归说明）

### 复杂控件：命中测试与输入路由

视觉树层级较深（叠层、装饰、遮罩、Popup、自定义模板、Composition 视觉）时，**不能只看效果对不对**，还要验证输入是否落到正确元素：

- **命中测试（hit test）**：透明/半透明装饰层、全屏叠层、未设 `IsHitTestVisible="False"` 的背景是否挡住下方按钮/列表；可点区域是否与视觉反馈一致
- **输入路由**：指针/触摸/键盘事件的捕获、冒泡与隧道是否被中间层截断；焦点与键盘导航是否仍可达；滚动容器与内嵌可点控件是否争抢手势
- 改模板或加装饰层后，至少手测：点击、悬停、拖拽、滚轮、Tab 焦点、以及叠层关闭后下层是否仍可交互

### 可视化控件：先查文档与参考实现

设计或重写**外观向**控件（自定义 ControlTemplate、视觉状态、Composition 动画、非标准布局）时，**先对齐正确做法再写代码**，避免凭直觉堆节点导致命中/无障碍/主题/性能问题：

1. **官方文档**：WinUI 3 / Windows App SDK（控件模板、视觉状态、Composition、输入与焦点）— [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
2. **社区文档与组件**：Community Toolkit for Windows 等已验证模式
3. **开源参考**：上游/同类 WinUI 应用与控件库中的成熟实现（模板结构、hit-test 边界、动画接入点），对齐后再适配本仓风格

本仓已有模式优先复用（如 `FluentAnimations`、`InstantTooltip`）；新模式应能说清「参考了何处、为何这样分层」。

## 注释规范

适量，与现有风格一致；不给一目了然的代码堆砌注释。

- 方法（含私有）：`/// <summary>`，参数 `<param>`，返回 `<returns>`，抛错 `<exception>`——调用方不读实现也能用对
- 行内 `//`：只写**为什么**（分支、CN/OS、风控/签名、线程、坑）
- 语言跟文件（多为中文）；用户可见字符串走 resx，不硬编码

## 硬性约束（禁止）

- `Starward.Core` 引用 WinUI
- `Program.Main` 中 Velopack 之前插入逻辑
- 无迁移改 schema，或改已发布 `Sql_vN`
- 擅自升级 `global.json` SDK 或 NuGet 包
- 与任务无关的大范围重构
- 提交 `bin/`、`obj/`、日志

## 参考

- 精简版：`AGENTS.md`
- 本地化：`docs/Localization.md`
- URL 协议：`docs/UrlProtocol.md`（`moonward://`）
- 日志：`%LocalAppData%/Moonward/log/` 或 `UserDataFolder/data/log/`
- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
