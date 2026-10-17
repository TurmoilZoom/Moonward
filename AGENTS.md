# AGENTS.md

面向 AI 编码助手的项目说明。改动前先读相关模块，保持与现有风格一致。
本文件是唯一入口，按任务才需要的细则在 `.agents/rules/`；文档与代码冲突时**以代码现状为准**。

## 项目概述

Moonward（产品品牌；工程目录与 C# 命名空间仍为 `Starward.*`）：Windows 米哈游游戏启动器（WinUI 3 / .NET 10），支持原神（hk4e）、星穹铁道（hkrpg）、绝区零（nap）、崩坏3（bh3）。

本仓为 **fork**（上游 Scighost/Starward，发布走 TurmoilZoom/Moonward + Velopack，安装包身份为 Moonward）。日常开发在 `rebase/develop`，会周期性变基上游——**许多自定义改动易在变基中丢失**。

## 按任务读规则

动手前先对照下表，**命中的规则文件读完再改代码**；一次改动可能命中多条。

| 改到这些 | 先读 |
|----------|------|
| `*.resx`、`*.Designer.cs`、新增或修改用户可见文案 | `.agents/rules/localization.md` |
| `Starward.Core/GameRecord/`、`*Client.cs`、新增米哈游 API 功能、展示 API 错误 | `.agents/rules/gamerecord.md` |
| `Features/Database/`（`DatabaseService`、`DatabaseSqls`）、新增表或列 | `.agents/rules/database.md` |
| `Program.cs`、`App.xaml.cs`、`Features/Startup/`、命令行参数、URL 协议 | `.agents/rules/startup.md` |
| `AppConfig.*.cs`、新增服务或设置项、`GameFeatureConfig`、数据目录、更新分发 | `.agents/rules/architecture.md` |
| `*.xaml`、页面外观或动画、控件模板、Composition | `.agents/rules/winui-ui.md` |
| 新建 worktree | `.agents/rules/worktree.md` |
| 提交并推送 / 打 tag 发版 | `.agents/skills/commit-push/`、`.agents/skills/tag-release/` |

## 解决方案结构

| 项目 | 作用 | 约束 |
|------|------|------|
| `src/Starward/` | UI、Features、DI、设置（`AppConfig.*`） | WinUI 3 |
| `src/Starward.Core/` | API Client、DTO、`*JsonContext`、`GameBiz` | **禁止 WinUI** |
| `src/Starward.Language/` | 应用文案 `Lang.*.resx`（Crowdin） | 见 `.agents/rules/localization.md` |
| `src/Starward.RPC/` | 提权安装等 gRPC（命名管道） | 独立进程 |
| `src/Starward.Setup.Core/` | GitHub Release 说明（`ReleaseClient`） | 更新本体由 Velopack 负责 |

## 构建与运行

解决方案文件是 `Starward.slnx`（供 Visual Studio 打开）；命令行验证直接编主项目即可（会带上 ProjectReference）：

```powershell
# 日常验证（最快）
dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64

# Release 自包含需指定 RID
dotnet build src/Starward/Starward.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

- **0 error 即视为通过**（无单元测试；必要时手动跑应用）。Release 全量约数分钟级。
- SDK 由 `global.json` 锁定——**不要擅自升级 SDK 或 NuGet 包**。
- 平台：`x86` / `x64` / `ARM64`。**没有编译类 CI**，改动是否通过以本地构建为准；发版产物由 `.github/workflows/release.yml` 按 tag 构建（Release × x64/arm64）。
- 常量：`DONOT_CHECK_UPDATE` 跳过启动更新检查（需手动加 `-p:DefineConstants=DONOT_CHECK_UPDATE`）；`DISABLE_XAML_GENERATED_MAIN` 由 `Program.Main` 接管入口。
- 不提交 `bin/`、`obj/`、日志。

## 提交信息

推送到远程时：提交类型用英文 Conventional Commits，说明主体用中文（标题与正文）。与 `CONTRIBUTING.md` 一致。

```
feat: 为首页添加功能图钉
fix: 修复自定义背景在分辨率变化后回退的问题
docs: 补充 URL 协议说明
```

常用类型：`feat` / `fix` / `docs` / `refactor` / `improve` / `remove` / `chore` 等。一次提交只做一件事；不相关改动拆开。正文可补充原因与影响（中文）。

## 编码要点

- `Nullable` 开启；异步方法带 `CancellationToken cancellationToken = default`
- JSON 一律 `*JsonContext.Default`，禁止无 context 反序列化；新 DTO 注册对应 JsonContext
- 日志 `ILogger<T>`；跨组件 `CommunityToolkit.Mvvm.Messaging`
- **x:Bind 绑定的 `ObservableObject` 属性必须在 UI 线程赋值**；在 `ConfigureAwait(false)` / `Task.Run` 内赋值会 `COMException 0x8001010E` 且易从 catch 逃逸
- **禁止硬编码用户可见字符串**，一律走 resx（增改词条见 `.agents/rules/localization.md`）

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

- 本地化：`docs/Localization.md`
- URL 协议：`docs/UrlProtocol.md`（`moonward://`）
- 日志：`%LocalAppData%/Moonward/log/` 或 `UserDataFolder/data/log/`
- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
