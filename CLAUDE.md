# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

Starward 是 Windows 平台的米哈游游戏启动器（WinUI 3 / .NET 10），支持原神（hk4e）、星穹铁道（hkrpg）、绝区零（nap）、崩坏3（bh3）。本仓库是 **fork**（上游 Scighost/Starward，发布走 TurmoilZoom/Starward + Velopack），日常开发在 `rebase/develop` 分支，会周期性变基到上游——**许多自定义改动易在变基中丢失**。

`AGENTS.md` 是同一份指南的精简版，本文件为权威详版，两者出现冲突时以代码现状为准。

## 构建与运行

**没有 .sln 文件**，直接编译主项目 csproj（会带上所有 ProjectReference）：

```powershell
# 开发构建（日常验证用，最快）
dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64

# Release 构建（自包含，需指定 RID）
dotnet build src/Starward/Starward.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

- Release 全量构建约 3.5 分钟；**0 error 即视为通过**（项目无单元测试，构建通过 + 必要时手动跑应用就是验证手段）。
- SDK 版本由根目录 `global.json` 锁定（当前 `10.0.301`，`rollForward: latestMajor`）——**不要擅自升级 SDK 或 NuGet 包版本**。
- 平台：`x86` / `x64` / `ARM64`；CI（`.github/workflows/build.yml`）跑 Debug×Release × x64×arm64 四组合，用的是 `dotnet publish ... -r win-<plat> -p:DefineConstants=DONOT_CHECK_UPDATE`。
- 编译常量：`DONOT_CHECK_UPDATE` 跳过启动时更新检查（CI 用）；`DISABLE_XAML_GENERATED_MAIN` 让 `Program.Main` 接管入口（见下）。
- 改完代码至少保证 build 通过；不要提交 `bin/`、`obj/`、日志。

## 解决方案结构

| 项目 | 作用 | 关键约束 |
|------|------|----------|
| `src/Starward/` | 主程序：UI、Features、DI、设置（`AppConfig.*`） | WinUI 3 |
| `src/Starward.Core/` | API Client、DTO、`*JsonContext`、`GameBiz` | **禁止引用 WinUI**，是纯 .NET 类库（其他项目共享） |
| `src/Starward.Language/` | 应用文案 `Lang.resx`（走 Crowdin） | 见“本地化” |
| `src/Starward.RPC/` | 独立 gRPC 服务进程（命名管道），负责需提权的游戏安装等 | 单独进程，非主程序内运行 |
| `src/Starward.Setup.Core/` | GitHub Release 拉取（`ReleaseClient`，仅取发行说明） | 更新本体由 Velopack 负责 |

## 核心架构

要理解整体，重点读这几条主线（都跨多文件）：

### AppConfig —— 全局中枢（`static partial class`，分散在 `AppConfig.*.cs`）
不是普通配置类，而是应用的服务定位器 + 设置仓库 + 路径中心：
- **`AppConfig.ServiceProvider.cs`**：手写 `ServiceCollection` DI 容器（非 Host 泛型主机）。`AppConfig.GetService<T>()` 懒构建容器。新增服务在此注册。
- **`AppConfig.Setting.cs`**：用户设置。全局设置是 `static` 属性（getter/setter 走 `GetValue<T>()`/`SetValue()`，`[CallerMemberName]` 取键名）；按游戏区分的设置用 `GetXxx(biz)`/`SetXxx(biz)`。底层都持久化到 SQLite 的 `Setting` 表。
- **`AppConfig.Common.cs`**：`DeviceId`（机器指纹 MD5）、`SessionId`、JSON 默认选项等。
- 数据目录统一在「用户所选目录\data」（`UserDataFolder`），缓存/日志/数据库/背景图都在其下；子进程通过 `--data-folder` 同步。

### GameBiz —— 贯穿全局的游戏标识
`Starward.Core/GameBiz.cs` 是 `record struct`，值形如 `hk4e_cn`（`Game`=下划线前、`Server`=下划线后）。几乎所有按游戏分支的逻辑都以它为 key。新增/改动游戏相关功能时，先看它怎么被消费。

### GameFeatureConfig —— 每个游戏+服务器支持哪些功能
`Features/GameFeatureConfig.cs` 用 `FromGameId(gameId)` 按 `GameBiz` 返回一份配置（支持哪些页面 `SupportedPages`、是否支持云游戏/硬链接/实时便笺/每日签到等）。**给某游戏开启一个功能 = 在这里把对应 `Support*` 置 true**。

### GameRecord 功能的分层模式（以 SignIn 每日签到为参考实现）
新增类米哈游 API 功能时，严格按此顺序逐层改动：

**DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang**

| 层 | 位置 | 职责 |
|----|------|------|
| DTO | `Core/GameRecord/<功能>/*.cs` | API 请求/响应模型、返回码枚举 |
| JsonContext | `Core/GameRecord/GameRecordJsonContext.cs` | **新 DTO 类型必须在此注册**（源生成序列化） |
| Client | `GameRecordClient` 基类 + `HyperionClient`(CN) / `HoyolabClient`(OS) | HTTP、签名、序列化；**CN/OS 平台差异放在各自子类**，不要散落到 UI |
| Service 门面 | `Features/GameRecord/GameRecordService.cs` | 按区服选 CN/OS Client |
| 业务服务 | `Features/GameRecord/<功能>/<功能>Service.cs` | 缓存、错误映射、结构化结果 |
| UI | `Features/.../<功能>Button.xaml` 等，挂到 `GameLauncherPage` | 用户交互 |
| 开关 | `GameFeatureConfig.Support<功能>` | 按 `GameBiz` 启用 |
| DI | `AppConfig.ServiceProvider.cs` | 注册服务 |
| 文案 | `Lang.resx` | 见“本地化” |

API 客户端要点：走 `GameRecordClient.CommonSendAsync`；签名用 `CreateSecret()`（Gen1/LK2）；JSON 一律用 `*JsonContext.Default`，**不要无 context 反序列化**。

### 数据库 —— SQLite + Dapper，追加式迁移
`Features/Database/DatabaseService.cs`：
- 连接 `StarwardDatabase.db`，`KVT`（通用键值）与 `Setting`（用户设置）两张核心表 + 各游戏的抽卡/战绩表。
- **迁移机制**：`DatabaseSqls` 列表里每个 `Sql_vN` 常量是一次迁移，启动时按 `PRAGMA USER_VERSION` 跳过已执行的（`DatabaseSqls.Skip(version)`）。**改 schema 必须新增一个 `Sql_vN` 常量、追加进 `DatabaseSqls` 列表、并以 `PRAGMA USER_VERSION = N` 结尾**，绝不能改已发布的旧迁移。
- 自定义类型经 `DapperSqlMapper`（如 `GameBizHandler` 把 `GameBiz` 存为字符串）。

### 启动流程 —— Program.Main → Velopack → 职责链
- `Program.cs` 的 `Main` 里 **Velopack 必须最先执行**（`VelopackApp.Build().Run()`）：安装/更新/卸载 hook 在此直接处理并退出，不进 WinUI。**不要在 Velopack 之前插入逻辑。**
- `App.OnLaunched`：先 `starward://test/`（必须在环境初始化/DI 构建之前特判，否则 `CacheFolder` 尚未就绪会崩）→ `CheckEnviromentAsync()` → **启动处理器职责链** `DispatchStartupAsync`：遍历 DI 里的 `IEnumerable<IStartupHandler>`（注册顺序 = 优先级：rpc / playtime / startgame / urlprotocol），命中 `StartupOutcome.Exit` 则 `Environment.Exit` 统一收口 → 单实例（`main` 键，重定向激活）→ 创建 `MainWindow` 或仅托盘（`--hide`）。
- 新增命令行启动模式 = 实现 `IStartupHandler`（`Features/Startup/`）并在 DI 注册，动词常量在 `StartupVerbs`。

### 更新分发
自更新用 **Velopack + GitHub Releases**（已从上游自建服务器迁出）；预览版 = GitHub pre-release，按架构分渠道。`Starward.Setup.Core/ReleaseClient` 只用于拉发行说明，不负责更新本体。

## 本地化

- 应用内文案在 `Starward.Language/Lang.resx`，正式翻译走 [Crowdin](https://crowdin.com/project/starward)，**禁止硬编码用户可见字符串**。
- **开发时新增一条 `Lang.*` 字符串需要 3 处手工编辑**（`dotnet build` 不会重新生成 VS 设计器文件）：
  1. `Lang.resx`（默认/英文）
  2. `Lang.zh-CN.resx`（中文）
  3. `Lang.Designer.cs`（手动加 `public static string XXX => ...` 属性）
- 文档（`docs/*.md`）翻译用 `文件名.<语言-地区>.md` 命名。

## UI 约定

- 页面继承 `PageBase`，导航参数为 `GameId` / `GameBiz`。
- 主视觉风格是**亚克力**背景；动画优先用 **Composition**（`Helpers/FluentAnimations.cs`）而非 Storyboard。
- `DropDownButton` 弹出层在独立视觉树，**无法做亚克力，不要强行改造**（会回退纯色）。
- 跨组件通信用 `CommunityToolkit.Mvvm.Messaging`；日志用 `ILogger<T>`。
- **x:Bind 绑定的 `ObservableObject` 属性必须在 UI 线程赋值**；在 `ConfigureAwait(false)` / `Task.Run` 内赋值会抛 `COMException 0x8001010E` 且从 catch 逃逸（典型坑：服务里 `Task.Run` 包 DB 工作返回值，`await` 后再回 UI 线程赋值）。
- **不要升级 `CommunityToolkit.WinUI.Controls.Segmented`**（csproj 有注释说明，新版有回归）。

## 硬性约束（禁止）

- `Starward.Core` 引用 WinUI。
- 在 `Program.Main` 里 Velopack 之前插入逻辑。
- 无迁移地修改 SQLite schema，或改动已发布的 `Sql_vN`。
- 擅自升级 `global.json` SDK 或 NuGet 包。
- 与当前任务无关的大范围重构。

## 参考

- 本地化：`docs/Localization.md`
- URL 协议：`docs/UrlProtocol.md`（`starward://` 启动）
- 日志：`%LocalAppData%/Starward/log/` 或 `UserDataFolder/data/log/`
- WinUI 3 文档：https://learn.microsoft.com/en-us/windows/apps/winui/winui3/
