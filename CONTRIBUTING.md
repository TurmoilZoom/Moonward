# 贡献指南

感谢你愿意帮助改进 Moonward。无论是一行代码、一份翻译、一条 Bug 反馈，还是功能建议，都对项目很有价值。

Moonward 是 Windows 平台的米哈游游戏启动器（WinUI 3 / .NET 10），支持原神、星穹铁道、绝区零、崩坏 3 等 PC 端游戏。

## 仓库说明

本仓库（[TurmoilZoom/Starward](https://github.com/TurmoilZoom/Starward)）基于上游 [Scighost/Starward](https://github.com/Scighost/Starward) 维护，并包含额外的功能与修复。发布渠道见本仓库 [Releases](https://github.com/TurmoilZoom/Starward/releases)。

| 分支 | 用途 |
|------|------|
| `main` | 仅跟踪上游 `Scighost/Starward` 的 `main`，由 CI 定时同步，**不要在此分支提交开发代码** |
| `rebase/develop` | 日常集成分支，承载本 fork 的定制改动 |
| `dev/*` | 功能或修复的短期开发分支，完成后合并回 `rebase/develop` |

若你的改动属于上游通用问题（与 fork 定制无关），也可以考虑向上游 [Scighost/Starward](https://github.com/Scighost/Starward) 提交 Issue 或 Pull Request。

## 贡献方式

### 报告 Bug

使用 [Bug Report](https://github.com/TurmoilZoom/Starward/issues/new?template=bug_report.yml) 模板提交 Issue，并尽量提供：

- 清晰的复现步骤与预期行为
- Moonward 版本号与 Windows 版本号
- 相关日志（位于 `%LocalAppData%/Moonward/log/` 或用户数据目录下的 `data/log/`）

提交前请先搜索已有 Issue，避免重复。

### 功能建议

使用 [Feature Request](https://github.com/TurmoilZoom/Starward/issues/new?template=feature_request.yml) 模板。对于较大改动，建议先在 Issue 中讨论方案，再开始编码。

### 翻译与文档

- **应用内文案**：在 [Crowdin](https://crowdin.com/project/starward) 上参与翻译与校对。详见 [本地化指南](docs/Localization.zh-CN.md)。
- **仓库文档**：Markdown 译文放在 `docs/` 目录，文件名追加语言标签（如 `Localization.zh-CN.md`），并在原文开头添加指向译文的链接。

开发阶段若新增 `Lang.*` 字符串，需同时修改 `Lang.resx`、`Lang.zh-CN.resx` 和 `Lang.Designer.cs` 三处（`dotnet build` 不会自动更新 Designer 文件）。

### 代码贡献

1. Fork 本仓库，从 `rebase/develop` 创建分支（命名建议：`feat/xxx`、`fix/xxx`、`docs/xxx`）。
2. 在本地完成改动，确保构建通过（见下文）。
3. 向 `rebase/develop`（或约定的 `dev/*` 集成分支）发起 Pull Request。
4. 在 PR 描述中说明改动动机、测试方式，并关联相关 Issue（如有）。

## 开发环境

### 系统要求

- Windows 10 1809（17763）或更高版本
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2)
- [WebP 映像扩展](https://apps.microsoft.com/detail/9pg2dk419drg)（系统通常已预装）

### 工具链

安装 [Visual Studio 2022](https://visualstudio.microsoft.com/) 并勾选以下工作负载：

- .NET 桌面开发
- 使用 C++ 的桌面开发
- 通用 Windows 平台开发

.NET SDK 版本由仓库根目录 [`global.json`](global.json) 锁定（当前 `10.0.301`）。**请勿擅自升级 SDK 或 NuGet 包版本。**

也可仅安装 [.NET SDK](https://dotnet.microsoft.com/download) 后通过命令行构建。

## 构建与验证

仓库**没有** `.sln` 文件，直接编译主项目即可（会自动带上所有 ProjectReference）：

```powershell
# 日常开发验证（推荐）
dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64

# Release 自包含构建
dotnet build src/Starward/Starward.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

改完代码后至少保证 **0 error** 构建通过。项目目前没有单元测试，构建通过 + 必要时手动运行应用即为主要验证手段。

CI 会在 `main` 与 `dev/*` 分支的 push / pull_request 时运行，覆盖 Debug/Release × x64/arm64 四种组合（见 [`.github/workflows/build.yml`](.github/workflows/build.yml)）。

> **注意**：开发版可能损坏个人数据库 `StarwardDatabase.db`，测试前请备份。

## 项目结构

```
src/Starward/           主程序：UI、Features、DI（AppConfig.*）
src/Starward.Core/      API Client、DTO、JsonContext（禁止引用 WinUI）
src/Starward.Language/  应用文案（Lang.resx）
src/Starward.RPC/       独立 gRPC 服务进程（需提权的安装等操作）
src/Starward.Setup.Core/  GitHub Release 发行说明拉取
```

更详细的架构说明见 [`AGENTS.md`](AGENTS.md) 与 [`CLAUDE.md`](CLAUDE.md)（面向维护者与 AI 编码助手）。

### 新增 GameRecord 类功能

以「每日签到（SignIn）」为参考，按以下顺序逐层改动：

**DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang**

| 层 | 位置 | 职责 |
|----|------|------|
| DTO | `Starward.Core/GameRecord/<功能>/*.cs` | API 请求/响应模型、返回码枚举 |
| 活动配置 | `*ActivityConfig.cs`（按需） | 按游戏与区服映射 `act_id`、接口主机等常量 |
| JsonContext | `GameRecordJsonContext.cs` | 新 DTO 类型必须注册 |
| Client | `HyperionClient`(CN) / `HoyolabClient`(OS) | HTTP；平台差异放在子类 |
| Service 门面 | `Features/GameRecord/GameRecordService.cs` | 按区服选择 Client |
| 业务服务 | `Features/GameRecord/<功能>/<功能>Service.cs` | 缓存、错误映射 |
| UI | 对应 XAML 控件，挂到 `GameLauncherPage` 等 | 用户交互 |
| 开关 | `GameFeatureConfig.Support*` | 按 `GameBiz` 启用 |
| DI / 设置 | `AppConfig.ServiceProvider.cs`、`AppConfig.Setting.cs` | 注册服务与用户设置 |
| 文案 | `Lang.resx` + `Lang.zh-CN.resx` | 用户可见字符串 |

## 编码规范

### 通用约定

- 开启 `Nullable`；异步方法带 `CancellationToken cancellationToken = default`。
- JSON 序列化使用 `*JsonContext.Default`，**不要**无 context 反序列化。
- API 请求走 `GameRecordClient.CommonSendAsync`；CN/OS 平台差异放在 Client 子类，不要散落在 UI。
- 日志用 `ILogger<T>`；跨组件通信用 `CommunityToolkit.Mvvm.Messaging`。
- 页面继承 `PageBase`，导航参数为 `GameId` / `GameBiz`。
- `x:Bind` 绑定的 `ObservableObject` 属性必须在 UI 线程赋值（`ConfigureAwait(false)` 或 `Task.Run` 后直接赋值会触发 `COMException`）。

### 注释

- 每个方法写 XML 文档注释（`/// <summary>`、`<param>`、`<returns>`）。
- 关键分支、平台差异、线程切换等不显然处用行内 `//` 注释说明**为什么**。
- 注释语言与文件现有风格保持一致（多为中文）。

### UI

- 主视觉为亚克力背景；动画优先使用 Composition（见 `Helpers/FluentAnimations.cs`）。
- `DropDownButton` 弹出层在独立视觉树，无法做亚克力，不要强行改造。
- 不要升级 `CommunityToolkit.WinUI.Controls.Segmented`（csproj 中有说明）。

### 数据库迁移

用户数据使用 SQLite，迁移定义在 `Features/Database/DatabaseSqls`。修改 schema 时：

1. 新增 `Sql_vN` 常量（**不要**修改已发布的旧迁移）。
2. 追加到 `DatabaseSqls` 列表。
3. 以 `PRAGMA USER_VERSION = N` 结尾。

### 禁止事项

- `Starward.Core` 引用 WinUI。
- 在 `Program.Main` 里 Velopack 之前插入逻辑。
- 无迁移地修改 SQLite schema。
- 与当前任务无关的大范围重构。
- 提交 `bin/`、`obj/`、日志文件。

## Pull Request 检查清单

提交 PR 前请确认：

- [ ] 基于最新的 `rebase/develop`（或目标集成分支）创建分支
- [ ] `dotnet build` 通过，0 error
- [ ] 用户可见字符串已写入 `Lang.resx`，未硬编码
- [ ] 新 DTO 已注册到对应 `JsonContext`
- [ ] 未包含 `bin/`、`obj/`、日志等无关文件
- [ ] PR 描述清晰，说明了测试方式

## 提交信息

建议使用简洁的祈使句，说明「做了什么」而非「做了什么改动」：

```
fix: 修复自定义背景在分辨率变化后回退的问题
feat: 为首页添加功能图钉
docs: 补充 URL 协议说明
```

若一次提交包含多个不相关改动，请拆分为多个 commit 或 PR。

## 许可证

参与贡献即表示你同意将改动以 [MIT License](LICENSE) 发布。提交代码时保留上游与当前维护者的版权声明。

---

再次感谢你的贡献。如有疑问，欢迎在 Issue 中提问。