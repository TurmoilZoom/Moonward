# AGENTS.md

面向 AI 编码助手的精简项目说明。改动前先读相关模块，保持与现有风格一致。
细节与背景见 `CLAUDE.md`；**两者冲突时以代码现状为准**。

## 项目

Moonward（产品品牌；工程/命名空间仍为 `Starward.*`）：Windows 米哈游游戏启动器（WinUI 3 / .NET 10），支持原神、星铁、绝区零、崩坏3。
本仓为 fork，日常分支 `rebase/develop`，会周期性变基上游——**自定义改动易在变基中丢失**。

```
src/Starward/          UI、Features、DI（AppConfig.*）
src/Starward.Core/     API Client、DTO、JsonContext（禁止 WinUI）
src/Starward.Language/ 应用文案（Lang.*.resx）
src/Starward.RPC/      提权安装等（独立进程）
src/Starward.Setup.Core/
```

GameRecord 相关以「统一 API 错误反馈」「国服短信登录」为准；新增同类功能以 **SignIn（每日签到）** 为完整分层参考。

## 构建

```powershell
dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64
```

SDK 见 `global.json`，**不要擅自升级 SDK/NuGet**。改完至少 build 通过；不提交 `bin/`、`obj/`、日志。

## 分层（以 SignIn 为模板）

新增类米哈游 API 功能时按此顺序：

**DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang**

| 层 | 路径（SignIn 示例） | 职责 |
|----|---------------------|------|
| DTO / 活动配置 | `Core/GameRecord/SignIn/*` | 模型、返回码；`SignInActivityConfig.FromGame` 映射 act_id/主机/头（**nap/bh3 等 act_id 见该文件注释，可能需核对**） |
| JsonContext | `GameRecordJsonContext.cs` | 新 DTO **必须注册** |
| Client | `GameRecordClient` + `HyperionClient`(CN) / `HoyolabClient`(OS) | HTTP、签名、序列化；**CN/OS 差异只放子类** |
| Service 门面 | `GameRecordService` | 选 CN/OS Client |
| 业务 / 后台 | `SignInService` / `AutoSignInService` | 缓存、结果、自动任务（失败冷却等） |
| UI / 开关 | `SignInButton` → `GameLauncherPage`；`GameFeatureConfig.SupportSignIn` | 交互与按 `GameBiz` 启用 |
| 设置 / DI | `AppConfig.Setting` / `ServiceProvider` | 按游戏开关、注册服务 |
| 文案 | 全部 `Lang.*.resx` + `Lang.Designer.cs` | 见「本地化」 |

## 米哈游 API 异常与登录

**错误反馈**（`MiHoYoApiErrorFeedbackFactory`）

- Core 只抛协议异常：战绩/通行证等 → `miHoYoApiException`；抽卡 authkey → `GachaApiException`。`ResponseMessage` 保留服务端原文；**异常类不得本地化或把 retcode 写成 UI 文案**。
- UI：`Create(exception, MiHoYoApiContext.XXX)`，禁止页面/服务自行 `switch` retcode 或硬编码 Toast。同一 retcode 语义随 Context 变（`PassportCaptcha` ≠ 战绩；抽卡失效 → `RefreshUrl`，勿提示重登米游社）。
- 主窗口 `Show(feedback, onRecovery)`，恢复由调用方接线；对话框内用 `Create` + 框内 `InfoBar`。未知码保留原文与状态码；扩展只改 Factory。

**登录与 Cookie**

- 入口：国服短信验证码 + 手动 Cookie；国际服无 passport 短信时提供 **WebView 网页登录**（`LoginPage`）+ 手动 Cookie。验证码登录写入 `stoken`/`mid`。
- 国服失效：`ExecuteWithRequestRecoveryAsync`（指纹 + stoken 换票，**最多重试一次**）；失败则引导重新登录。
- 分层：`MihoyoPassportClient` → `CaptchaLoginService` / `GameRecordCookieRefreshService` → 对话框 UI。Core 禁止 WinUI；日志禁止 Cookie/Token/验证码/完整手机号明文。

## 编码要点

- `Nullable` 开启；异步方法带 `CancellationToken cancellationToken = default`
- JSON 一律 `*JsonContext.Default`，禁止无 context 反序列化；新 DTO 注册对应 JsonContext
- `GameRecordClient` 走 `CommonSendAsync`；CN/OS 差异在 Client 子类，不散落到 UI
- 日志 `ILogger<T>`；跨组件 `CommunityToolkit.Mvvm.Messaging`
- 页面继承 `PageBase`，导航参数 `GameId` / `GameBiz`
- **x:Bind 的 `ObservableObject` 属性必须在 UI 线程赋值**（`ConfigureAwait(false)` / `Task.Run` 里赋值会 COM 异常且难抓）
- 改 SQLite schema：**只追加**新 `Sql_vN` + `PRAGMA USER_VERSION`，禁止改已发布迁移

## 注释

适量、与文件现有风格一致（多为中文）：

- 方法用 `/// <summary>` / `<param>` / `<returns>`（必要时 `<exception>`），把输入输出语义写清
- 关键内 `//` 只解释**为什么**（分支、平台差异、风控、线程），不复述代码
- 用户可见字符串走 `Lang.resx`，不硬编码进注释或代码

## UI

- 亚克力背景；动画优先 Composition（`Helpers/FluentAnimations.cs`）
- `DropDownButton` 弹出层无法亚克力，不要强行改造
- 短暂悬停说明用 `InstantTooltip`，勿另起一套
- **不要升级** `CommunityToolkit.WinUI.Controls.Segmented`（csproj 有说明）
- **层级复杂的控件**须考虑命中测试（hit test）与输入路由：装饰层/叠层/透明区域是否误拦指针与键盘；可点区域、`IsHitTestVisible`、事件冒泡/隧道是否与预期一致
- **可视化控件**（自定义外观、Composition、复杂模板）设计前对照官方文档、社区文档与成熟开源实现，勿凭直觉堆 XAML/视觉层

## 本地化

应用文案：`Starward.Language/Lang.*.resx`（正式翻译也走 Crowdin）。**禁止硬编码用户可见字符串**。
领域枚举等若在 Core 侧：`Starward.Core/Localization/CoreLang.*.resx`，规则相同。

- **新增**：`Lang.resx` + `zh-CN` + `Lang.Designer.cs` + **其余全部已维护语言**同名键；不会的语言可用英文占位，键不可缺。`dotnet build` **不会**重生 Designer，须手改。
- **修改**：改源文后**逐语言核对**该键译文是否仍正确（语义、`{0}` 等占位符）；勿只改英/中而残留旧义。
- 文档翻译：`docs/文件名.<语言-地区>.md`

## 禁止

- `Starward.Core` 引用 WinUI
- `Program.Main` 里 Velopack **之前**插入逻辑
- 无迁移改 schema，或改已发布的 `Sql_vN`
- 擅自升级 `global.json` SDK 或 NuGet 包
- 与任务无关的大范围重构
- 提交 `bin/`、`obj/`、日志

## 参考

- 详版：`CLAUDE.md`
- 本地化：`docs/Localization.md`
- URL 协议：`docs/UrlProtocol.md`
- 日志：`%LocalAppData%/Moonward/log/` 或 `UserDataFolder/data/log/`
- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
