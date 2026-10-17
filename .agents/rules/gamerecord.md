# GameRecord 分层与米哈游 API

## 分层（以 SignIn 为参考）

新增类米哈游 API 功能时严格按序：

**DTO → JsonContext → Client → GameRecordService → Feature Service → UI → GameFeatureConfig → DI → Lang**

| 层 | 位置 | 职责 |
|----|------|------|
| DTO | `Core/GameRecord/<功能>/*.cs` | 请求/响应、返回码枚举 |
| 活动配置 | `*ActivityConfig.cs`（按需） | `FromGame(game, isOversea)` 映射 act_id/主机/头；**给游戏开功能 = 加一条**；易变常量集中于此 |
| JsonContext | `GameRecordJsonContext.cs` | 新 DTO **必须注册**（源生成） |
| Client | `GameRecordClient` + `HyperionClient`(CN) / `HoyolabClient`(OS) | HTTP/序列化；**CN/OS 差在子类**，不散落到 UI |
| Service 门面 | `GameRecordService` | 选区服 Client |
| 业务 / 后台 | `<功能>Service` / `Auto*`（按需） | 缓存、结构化结果、自动任务 |
| UI / 开关 | 控件挂 `GameLauncherPage`；`GameFeatureConfig.Support*` | 交互与按游戏启用 |
| 设置 / DI | `AppConfig.Setting` / `ServiceProvider` | 按游戏设置、注册 |
| 文案 | 全部 `Lang.*.resx` + `Designer` | 见 `.agents/rules/localization.md` |

SignIn 对照：`Core/GameRecord/SignIn/*`（DTO + `SignInActivityConfig.FromGame`）→ `SignInService` / `AutoSignInService`（缓存、结果、自动任务，失败冷却等）→ `SignInButton` 挂 `GameLauncherPage`，`GameFeatureConfig.SupportSignIn` 按 `GameBiz` 启用。

客户端约定：`CommonSendAsync`；JSON 一律 `*JsonContext.Default`。签到 act_id 等以 `SignInActivityConfig` 文件头注释为准（部分游戏/区服为待核对猜测，**只改该文件即可**）。

## 米哈游 API 错误反馈

入口：`Features/MiHoYoApiErrorFeedback.cs`（`MiHoYoApiErrorFeedbackFactory`）。

- Core 只抛 `miHoYoApiException` / `GachaApiException`，保留服务端原文；**异常类不本地化**。
- UI 用 `Create(exception, MiHoYoApiContext.XXX)` 展示；**禁止**页面/服务自行 `switch` retcode 或硬编码 Toast。
- 扩展映射只改 Factory；文案走 `.agents/rules/localization.md`。
