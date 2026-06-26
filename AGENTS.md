# AGENTS.md

面向 AI 编码助手的项目说明。改动前先读相关模块，保持与现有风格一致。

## 项目

Starward：Windows 游戏启动器（WinUI 3 / .NET 10），支持原神、星铁、绝区零、崩坏3。

```
src/Starward/          UI、Features、DI（AppConfig.*）
src/Starward.Core/     API Client、DTO、JsonContext（无 WinUI 依赖）
src/Starward.Language/ 应用文案（Lang.resx）
src/Starward.RPC/
src/Starward.Setup.Core/
```

**当前分支重点**：每日签到（SignIn）—— 以它为新增 GameRecord 功能的参考实现。

## 构建

```powershell
dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64
```

SDK 版本见 `global.json`，不要擅自升级。改完至少 build 通过。

## 分层（以 SignIn 为例）

| 层 | 路径 | 职责 |
|----|------|------|
| DTO | `Starward.Core/GameRecord/SignIn/*.cs` | API 模型、`SignInReturnCode` |
| 活动配置 | `Starward.Core/GameRecord/SignIn/SignInActivityConfig.cs` | `FromGame(game, isOversea)` 映射 `act_id`/接口主机/`x-rpc-signgame`；**nap/bh3 的 act_id 为待核对的猜测，改这里即可** |
| Client | `GameRecordClient` 基类方法 + `HyperionClient` / `HoyolabClient` 平台差异 | HTTP、签名、序列化；平台头走 `AddSignInPlatformHeaders` |
| JsonContext | `GameRecordJsonContext.cs` | 新类型必须注册 |
| Service 门面 | `Features/GameRecord/GameRecordService.cs` | 选 CN/OS Client、`PrepareSignInClientAsync` |
| 业务编排 | `Features/GameRecord/SignIn/SignInService.cs` | 缓存、错误映射、结构化结果 |
| 后台任务 | `Features/GameRecord/SignIn/AutoSignInService.cs` | 自动签到（按游戏开关 + 失败 10 分钟冷却去重） |
| UI | `SignInButton.xaml`（+ `SignInAwardView.cs`）→ 挂在 `GameLauncherPage.xaml` | 用户交互 |
| 开关 | `GameFeatureConfig.SupportSignIn` | 按 `GameBiz` 启用 |
| DI | `AppConfig.ServiceProvider.cs` | `SignInService`、`AutoSignInService` |
| 设置 | `AppConfig.Setting.cs` | 按游戏 `GetAutoSignInEnabled(biz)`/`SetAutoSignInEnabled(biz)` |
| 文案 | `Lang.resx` + `Lang.zh-CN.resx` | 禁止硬编码用户可见字符串 |

新增类似功能时，按 **DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang** 顺序改动。

## 编码要点

- `Nullable` 开启；异步方法带 `CancellationToken cancellationToken = default`
- JSON 用 `*JsonContext.Default`，不要无 context 反序列化
- `GameRecordClient` 走 `CommonSendAsync`；签到 POST 体用 `SignInPostBody` + `GameRecordJsonContext`
- CN（`HyperionClient`）与 OS（`HoyolabClient`）差异放在 Client 子类（如 `AddSignInPlatformHeaders`），不要散落在 UI
- 日志用 `ILogger<T>`；跨组件通信用 `CommunityToolkit.Mvvm.Messaging`
- 页面继承 `PageBase`，导航参数为 `GameId` / `GameBiz`

## 注释规范

适量添加注释（与现有风格一致，不堆砌冗余）：

- **每个方法带方法注释**：用 XML 文档注释 `/// <summary>` 写用途，`<param>` 说明每个输入参数（含义/约束/可空），`<returns>` 说明输出；会抛异常补 `<exception>`。
- **输入输出说清楚**：参数与返回值的语义、边界、null/default 行为写明，调用方无需读实现即可正确使用。
- **方法内部关键位置加行内注释**：不显然的分支、CN/OS 平台差异、签名/风控、线程切换、易踩的坑用 `//` 说明**为什么**，而非复述代码。
- 注释语言与文件现有注释保持一致（多为中文）；用户可见字符串仍走 `Lang.resx`，不要硬编码。

## UI

- 主风格：亚克力背景；动画优先 Composition（见 `Helpers/FluentAnimations.cs`）
- `DropDownButton` 弹出层无法亚克力，不要强行改造
- 不要升级 `CommunityToolkit.WinUI.Controls.Segmented`（csproj 有注释）

## 禁止

- `Starward.Core` 引用 WinUI
- 改动 `Program.Main` 里 Velopack 之前的逻辑
- 无迁移地修改 SQLite schema
- 与任务无关的大范围重构
- 提交 `bin/`、`obj/`、日志

## 参考

- [WinUI 3 文档](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- 本地化：`docs/Localization.md`（应用文案走 Crowdin，开发时改 resx）
- 日志：`%LocalAppData%/Starward/log/`