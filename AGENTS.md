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
| Client | `GameRecordClient` 基类方法 + `HyperionClient` / `HoyolabClient` 平台差异 | HTTP、签名、序列化 |
| JsonContext | `GameRecordJsonContext.cs` | 新类型必须注册 |
| Service 门面 | `Features/GameRecord/GameRecordService.cs` | 选 CN/OS Client、`PrepareSignInClientAsync` |
| 业务编排 | `Features/GameRecord/SignIn/SignInService.cs` | 缓存、错误映射、结构化结果 |
| 后台任务 | `Features/GameRecord/SignIn/AutoSignInService.cs` | 自动签到 |
| UI | `SignInButton.xaml` → 挂在 `GameLauncherPage.xaml` | 用户交互 |
| 开关 | `GameFeatureConfig.SupportSignIn` | 按 `GameBiz` 启用 |
| DI | `AppConfig.ServiceProvider.cs` | `SignInService`、`AutoSignInService` |
| 设置 | `AppConfig.Setting.cs` | `AutoSignInEnabled` |
| 文案 | `Lang.resx` + `Lang.zh-CN.resx` | 禁止硬编码用户可见字符串 |

新增类似功能时，按 **DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang** 顺序改动。

## 编码要点

- `Nullable` 开启；异步方法带 `CancellationToken cancellationToken = default`
- JSON 用 `*JsonContext.Default`，不要无 context 反序列化
- `GameRecordClient` 走 `CommonSendAsync`；签到 POST 体用 `SignInPostBody` + `GameRecordJsonContext`
- CN（`HyperionClient`）与 OS（`HoyolabClient`）差异放在 Client 子类（如 `AddSignInPlatformHeaders`），不要散落在 UI
- 日志用 `ILogger<T>`；跨组件通信用 `CommunityToolkit.Mvvm.Messaging`
- 页面继承 `PageBase`，导航参数为 `GameId` / `GameBiz`

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