# 核心架构

## AppConfig（`static partial class`，`AppConfig.*.cs`）

服务定位器 + 设置仓库 + 路径中心，不是普通配置类：

- **`ServiceProvider`**：手写 `ServiceCollection`（非 Generic Host）；`GetService<T>()` 懒构建；**新服务在此注册**
- **`Setting`**：全局设置 = `static` 属性（`GetValue`/`SetValue` + `[CallerMemberName]`）；按游戏 = `GetXxx(biz)`/`SetXxx(biz)`；落库 `Setting` 表
- **`Common` / `Configuration`**：`DeviceId`、`SessionId`、JSON 选项、版本/便携/缓存路径等
- 数据目录：用户所选目录下的 `data`（`UserDataFolder`）；子进程用 `--data-folder` 对齐

## GameBiz

`Starward.Core/GameBiz.cs`（`record struct`），如 `hk4e_cn`（`Game` / `Server` 以下划线分）。按游戏分支几乎都以它为 key。

## GameFeatureConfig

`Features/GameFeatureConfig.cs`：`FromGameId` 按 `GameBiz` 返回能力开关（页面、云游戏、硬链接、实时便笺、签到等）。**给某游戏开功能 = 对应 `Support*` 置 true**。

## 更新分发

Velopack + GitHub Releases；预览版 = pre-release，按架构分渠道。`ReleaseClient` 只拉发行说明，不负责更新本体。
