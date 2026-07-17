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

近期 GameRecord 改动以「统一 API 错误反馈」和「国服短信验证码登录」为准；每日签到（SignIn）仍是新增 GameRecord 功能的完整分层参考。

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
| 文案 | `Lang.resx` + `Lang.zh-CN.resx` + `Lang.Designer.cs` | 禁止硬编码用户可见字符串；新增资源键手工同步三处 |

新增类似功能时，按 **DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang** 顺序改动。

## 米哈游 API 异常与登录

- Core Client 只保留协议语义：战绩/通行证接口抛 `miHoYoApiException(retcode, responseMessage)`，抽卡 authkey 接口抛 `GachaApiException`；原始服务端消息放在 `ResponseMessage`，**不得在异常类中本地化或把 retcode 解释成 UI 文案**。
- UI 业务层处理上述异常或 `HttpRequestException` 时，必须按真实接口场景调用 `MiHoYoApiErrorFeedbackFactory.Create(exception, MiHoYoApiContext.XXX)`，不要在页面/服务中自行 `switch` retcode、硬编码 Toast 或把不同接口的同一码混用。新增场景或确认新 retcode 语义时，在 Factory 集中扩展 `MiHoYoApiContext` 与映射；未知码保留原始消息和状态码方便排查。
- Factory 返回 `Severity`、本地化标题/正文和 `RecoveryAction`。主窗口反馈使用 `Show(feedback, onRecovery)`；调用方负责把 `Relogin` / `VerifyAccount` / `RefreshUrl` 接到本页面可执行的恢复动作（战绩使用消息通知打开登录/验证入口）。验证码对话框打开期间用 `Create` 后显示对话框内 `InfoBar`，不要抢占主窗口 Toast。
- 同一 retcode 的含义取决于 `MiHoYoApiContext`，尤其 `PassportCaptcha` 与战绩接口必须分开映射；抽卡 URL 失效是 `RefreshUrl`，不能误提示重新登录米游社。
- 战绩工具箱已移除 WebView 网页登录和旧的 stoken Cookie 静默刷新。登录失效应引导用户手动输入 Cookie 或（仅国服）短信验证码登录；`GameRecordService` 只可在国服请求失败后刷新设备指纹并**最多重试一次**，不应重建 Cookie 刷新逻辑。
- 国服短信登录分层：`Core/GameRecord/Passport/MihoyoPassportClient.cs` 负责 passport 协议，`CaptchaLoginService` 负责发码、aigis 重试（最多 3 次）、换票与 Cookie 拼装，`CaptchaLoginDialog`/`GeetestVerifyPopup` 负责 UI。服务调用前须同步 `GameRecordService` 的设备指纹；UI 通过回调完成极验，不能把 WinUI 依赖带入 Core。

## 编码要点

- `Nullable` 开启；异步方法带 `CancellationToken cancellationToken = default`
- JSON 用 `*JsonContext.Default`，不要无 context 反序列化
- `GameRecordClient` 走 `CommonSendAsync`；签到 POST 体用 `SignInPostBody` + `GameRecordJsonContext`
- CN（`HyperionClient`）与 OS（`HoyolabClient`）差异放在 Client 子类（如 `AddSignInPlatformHeaders`），不要散落在 UI
- 新的 GameRecord/Passport DTO 必须注册到 `GameRecordJsonContext`；Cookie、Token、手机号、验证码和 authkey 均属敏感信息，日志不得输出明文
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
- 短暂悬停说明优先用 `InstantTooltip`；不要为相同用途另起 Tooltip 实现
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
