# 应用自更新（Velopack）

本文描述 Moonward 如何用 [Velopack](https://docs.velopack.io/) 检查、下载并安装自身更新。实现以仓库现状为准：客户端 `Velopack` 1.2.0，打包走 `vpk`，更新包默认从 CNB Releases 拉取，GitHub Releases 为权威发布源与备用下载源。

发行说明（更新弹窗里的 Markdown）由 `ReleaseClient` 从 GitHub API 单独拉取，**不参与** Velopack 的 feed / 包下载。

---

## 1. 发布与渠道

### 1.1 双源

| 角色 | 仓库 | 客户端用途 |
|------|------|------------|
| 权威发布 | GitHub `TurmoilZoom/Moonward` | `vpk pack` 产物上传；发行说明；CNB 被限流时的检查/下载回落 |
| 默认更新源 | CNB `TurmoilZoom/Moonward` | `UpdateService` 默认的 `CnbSource`（检查 + 下载） |

GitHub Release 上传成功后，`.github/workflows/release.yml` 触发 CNB 流水线（`.cnb.yml`），把同一 tag 的资产镜像到 CNB Release。镜像失败不阻断 GitHub 发版。客户端**不**把 GitHub 当默认更新源，是为了在国内网络下更稳定地拉包。

### 1.2 Velopack 渠道 ≠ 预览开关

每个安装包属于一个 **Velopack channel**。Moonward 按进程架构分渠道，**不**按稳定/预览再拆渠道：

| 架构 | `vpk pack --channel` | 客户端读取的 feed 文件 |
|------|----------------------|------------------------|
| x64 | `win-x64` | `releases.win-x64.json` |
| ARM64 | `win-arm64` | `releases.win-arm64.json` |

`UpdateManager` 未传 `UpdateOptions.ExplicitChannel`，因此只在**当前安装包写入 `sq.version` 的渠道**里找更新。x64 安装不会去吃 ARM64 包。

稳定版 / 预览版是 **GitHub / CNB 的 pre-release 标志**，不是第二套 Velopack 渠道：

- tag 含 SemVer 预发布段（`-`，如 `1.2.3-beta.1`）→ GitHub / CNB 标为 pre-release
- 设置里「加入预览更新」→ `AppConfig.EnablePreviewRelease` → `CnbSource` / `GithubSource` 的 `prerelease: true`
- `prerelease: false` 时，列表接口返回的预发布项在**客户端内存**里丢掉（`Where(x => includePrereleases \|\| !x.Prerelease)`）

关闭预览后，已装上的预览版**不会**自动降回正式版：`UpdateManager` 未开启 `AllowVersionDowngrade`，远端最新正式版低于当前预览号时检查结果为「无更新」。

### 1.3 打包产物

CI（`release.yml`）对每个架构：`dotnet publish` → `vpk download github`（上一版作差分基准）→ `vpk pack --delta BestSpeed --channel win-<arch>`。`packId` 为 `Moonward`。每个 Release 上通常有：

| 资产 | 作用 |
|------|------|
| `releases.win-x64.json` / `releases.win-arm64.json` | 该渠道的 Velopack feed（检查更新必读） |
| `Moonward-<ver>-win-<arch>-full.nupkg` | 该版本完整包 |
| `Moonward-<ver>-win-<arch>-delta.nupkg` | 相对上一版的增量包（有历史基准时才会生成） |
| `*-Setup.exe` | 安装器 |
| `*-Portable.zip` | 便携包（仍是完整 Velopack 布局，可原地更新） |

首个版本或 `vpk download` 失败时可能没有 delta。旧品牌 `Starward*` 基线 nupkg 在 CI 里会被删掉，避免误当作 delta 基准。

---

## 2. 客户端入口与生命周期

`Program.Main` 里 **`VelopackApp.Build().Run()` 必须最先执行**。Velopack 会在安装 / 更新 / 卸载钩子里拉起主程序并要求尽快退出；钩子返回后进程从 `Run()` 内部结束，不会进入 WinUI。卸载钩子里会静默清理用户数据（30 秒超时、禁止 UI）。

自动检查在 `DEBUG` 或定义了 `DONOT_CHECK_UPDATE`（CI publish）时直接跳过。关于页的手动「检查更新」不受该编译开关影响，但开发态 / 裸 `dotnet publish` 目录没有 `Update.exe`，`UpdateManager.IsInstalled == false`，仍无法自动更新。

### 2.1 检查入口

| 入口 | 是否走 1 小时节流 | 行为 |
|------|-------------------|------|
| `MainView` 首次加载 | 是（`UpdateService.IsCheckDue`） | 开了「推送更新」才查 |
| 主窗口从后台回到前台 | 是 | 同上；最小化 / 托盘 / 锁屏只广播激活消息，过滤后才查，避免弹窗抢前台 |
| 常驻循环 `StartResidentSilentUpdate` | 是 | 进程启动约 1 分钟后开始；仅在「推送 + 静默」都开时检查并后台下载；不弹窗 |
| 关于页手动检查 | **否** | 每次点击都发起检查；忽略「忽略此版本」 |

`UpdateService.CheckInterval = 1 小时`。主窗口自动检查与常驻循环**共用** `_lastCheckTicks`（`Interlocked` 读写，避免 `DateTimeOffset` 多字段赋值被撕裂）。进入 `GetLatestVersionAsync` 即记时：非 Velopack 部署、断网、429 同样计入，避免失败后立刻重试把配额打满。

手动检查也会刷新该时钟，因此刚手动查过之后，自动路径在一小时内不会再打网络。

主窗口在**已经查到新版本且未走静默下载**时，另有展示节流：距上次弹窗超过 6 小时 **并且** 不在同一自然日，才再开 `UpdateWindow`。这与 1 小时检查时钟是两件事。

### 2.2 并发合并

`GetLatestVersionAsync` 把进行中的检查挂在 `_checkingTask` 上。首页自动检查与关于页手动检查同时发生时，后来者 await 同一任务，**不会**再打一套 HTTP。下载阶段另有 `_isUpdating`，不会并行下两份包。

### 2.3 用户开关

| 设置 | 作用 |
|------|------|
| `EnableUpdateNotification` | 自动检查 / 弹窗 / 常驻静默循环的总开关。手动检查不受影响 |
| `EnableSilentUpdate` | 子选项：后台下载，进程退出后静默安装。推送关闭时 UI 会强制关掉它 |
| `EnablePreviewRelease` | 检查时是否纳入 pre-release |
| `IgnoreVersion` | 自动检查忽略不超过该版本的更新；手动检查走 `GetLatestVersionAsync`，不读此项。下载成功后清空 |
| `PendingSilentUpdateContent` | 静默更新已下载（或已安装）后，下次主窗口激活弹出仅含发行说明的窗口 |

---

## 3. 检查更新：Feed 与请求

Velopack 没有「一个 URL 列出全世界包」的单一索引。GitHub / CNB 这类 Git 源的流程是：

```
GetReleases()                    // 1 次：Release 列表
  → 对列表中每个 Release
       GET releases.{channel}.json   // 每个 1 次
  → 合并为 VelopackAssetFeed
UpdateManager.CheckForUpdatesAsync()
  → 在 feed 里取 Type == Full 且版本最高者 → latestRemoteFull
  → 与本地已装版本比较；更高则构造 UpdateInfo（含可选 delta 策略）
```

`releases.{channel}.json` 由 `vpk pack` 生成，描述该 Release 里该渠道的 Full / Delta 资产（文件名、版本、大小、SHA 等）。`GitBase.GetReleaseFeed` 按 Release **依次**（`foreach` + `await`，非并发扇出）下载这些 json，某个 Release 缺少对应文件则跳过。

### 3.1 远端如何挑「最新」

与笔记一致：`UpdateManager` 在合并后的 feed 中筛选 `VelopackAssetType.Full`，用版本号取最大者作为 `latestRemoteFull`。未开启降级时：

- `latestRemoteFull.Version > CurrentVersion` → 有更新
- 否则 → `null`（无更新）

### 3.2 CNB 一次检查打多少请求

`CnbSource.GetReleasesListUri()`：

```
GET https://cnb.cool/TurmoilZoom/Moonward/-/releases?page=1&page_size=100
Accept: application/vnd.cnb.api+json
```

只拉第 1 页（最多 100 条）。CNB 列表**不能**在服务端只要正式版，正式/预览混在一起；过滤在客户端做。GitHub 官方 `GithubSource` 同样是客户端过滤；差别是 GitHub 源在 API 上就 `per_page=10&page=1`。

`GitBase.GetReleaseFeed` 会为**每个**留下的 Release 再下一次 `releases.{channel}.json`。仓库 Release 变多之后，若不裁剪，一次检查是 `1 + N` 次 HTTP，会稳定碰到 CNB 匿名接口约 **20 次/分钟** 的限流（响应头 `X-Ratelimit-Limit`）。因此 `CnbSource.TrimToRequiredReleases` 在拉 feed 之前把列表收成真正用得上的子集：

1. 丢掉 tag 版本 **≤ 当前安装版本** 的 Release（未开降级，旧包对「最新 Full + 增量链」无贡献）
2. 已是最新（「比当前新」的集合为空）→ **只留 1 个**最新 Release，仍要读它的 feed 才能判定无更新
3. 有新版本 → 最多留 **10** 个（与 `UpdateManager.MaximumDeltasBeforeFallback` 默认值一致；超过 10 个 delta 时 Velopack 本来就会改下完整包）
4. 当前版本号解析失败时，退化为按发布时间截断到 10 个

因此 **仅检查、且未触发 GitHub 回落** 时：

| 本地状态 | HTTP 次数（CNB） |
|----------|------------------|
| 已是最新 | **2**（列表 1 + feed 1） |
| 落后 `k` 个有 feed 的 Release | **1 + min(k, 10)**，即 **2～11** |

「每次检查约 2 个请求」只在已是最新时成立。落后很多个版本时上限是 11，不会随仓库历史线性增长。

个别 Release 没有 `releases.win-x64.json`（例如只发了说明）会被 `GetReleaseFeed` 跳过，实际 feed 请求可能少于裁剪后的 Release 数。

这 **不包括**：

- 随后下载 `.nupkg`（完整包或若干 delta）
- `UpdateWindow` 为渲染发行说明调用的 GitHub API（`/releases?per_page=20`、`/markdown` 等）

### 3.3 与 GitHub 源的对比

Velopack 自带 `GithubSource.GetReleases`：

```csharp
const int perPage = 10;
const int page = 1;
// GET https://api.github.com/repos/{owner}/{repo}/releases?per_page=10&page=1
```

| | CNB（默认） | GitHub（回落 / 手动改下载源） |
|--|-------------|------------------------------|
| 列表 | `cnb.cool/.../-/releases?page=1&page_size=100`，再客户端裁剪到 ≤10 | `api.github.com/.../releases?per_page=10&page=1` |
| 预发布过滤 | 客户端 | 客户端 |
| feed json | 每个留下的 Release 一次；URL 来自附件 `browser_download_url` | 同上 |
| 列表是否计入 REST 配额 | 计入 CNB 匿名配额 | **计入** GitHub REST（匿名 60 次/小时/IP） |
| feed / nupkg 是否计入 REST 配额 | 与列表同属 CNB 主机，按匿名限流计入 | 无 token 时走 `browser_download_url`，**不计入** GitHub REST 60 次/小时 |

因此：GitHub 路径对 REST 配额几乎只消耗 **1 次列表**；CNB 路径则是列表 + 每次 feed（以及之后的 nupkg）都挤同一条 20 次/分钟的桶。这是默认走 CNB、却必须把单次检查压到 11 次以内的原因。

CNB 客户端 **不带**访问令牌（`CnbSource.Authorization` 恒为 `null`）。GitHub 回落同样 `accessToken: null`。

---

## 4. 增量（Delta）与全量

检查阶段只决定「有没有更新」和「若下载，候选 delta 列表」。真正选增量还是全量发生在 `DownloadUpdatesAsync`。

### 4.1 何时能走增量

同时满足：

1. 本地 `packages\` 里有一份 **Full** 包（`Locator.GetLatestLocalFullPackage()` 非空），作为补丁基准
2. feed 里存在与 `latestRemoteFull` **同一版本**的 Delta
3. 将该基准版本到目标版本之间、feed 里所有 `Type == Delta` 且 `baseVer < v ≤ targetVer` 的项收成列表（按版本排序后交给 `Update.exe patch`）

第 3 步**不会**在检查期证明「版本号连续、中间没有缺口」。缺了中间某个 delta 时，仍会尝试按已有 delta 打补丁；`patch` 失败则捕获异常并 **回退下载完整包**。

Windows 安装器与 Velopack 便携包通常会带上当前版本的 `*-full.nupkg`。非 Windows 安装或 `packages\` 被清空后，第一次更新只能全量。官方说明：delta 回退规则还包括——

- delta 个数 **> `MaximumDeltasBeforeFallback`（默认 10）** → 全量。Moonward 未改 `UpdateOptions`，即为 10。设为负数可禁用增量
- 所有 delta 的 **Size 之和 > 目标 Full 的 Size** → 全量
- 补丁过程抛错（校验失败、`Update.exe patch` 非 0 退出等）→ 全量

### 4.2 增量包内容

当前 Velopack（1.2.0 / `vpk pack --delta BestSpeed`）对包内文件做 **Zstandard** 二进制差分，不是 bsdiff。`BestSize` 更慢、体积可能更小，文档称其耗时大致与旧的 bsdiff 相当。单个文件不能超过 2 GB。

- `*-full.nupkg`：该版本全部应用文件（底层是 ZIP / NuGet 包）
- `*-delta.nupkg`：相对**生成该 delta 时的上一完整包**的补丁 + 新增文件，不是一份可独立运行的程序

合成由旁边的 `Update.exe` 执行：`patch --old <本地 full> --delta <d1> --delta <d2> ... --output <新 full>`。成功后新的 full 留在 `packages\`，旧 nupkg / `.partial` 会被清掉（保留刚下好的目标 full）。

### 4.3 下载进度

`UpdateService` 把 `Progress_TotalBytes` 设为 `TargetFullRelease.Size`。走增量时实际传输量通常更小，百分比按 Velopack 回调 0–100（内部还会把进度规整到偶数）。这是 UI 近似，不是精确字节统计。

---

## 5. 安装布局（安装版与便携版）

只要根目录存在 `Update.exe`，就是 Velopack 部署，`UpdateManager.IsInstalled == true`，**安装版与官方 `*-Portable.zip` 都支持检查和原地更新**。便携版多一个根目录标记文件 `.portable`。

```
<root>/                          # 安装版多为 %LocalAppData%\Moonward
├── Update.exe                   # 更新器（下载结束后会从新 nupkg 里抽出覆盖）
├── .portable                    # 仅便携版
├── packages/                    # 更新仓储：full / delta / .partial / .velopack_lock
└── current/                     # 正在运行的应用
    ├── Moonward.exe
    ├── sq.version               # 包清单：Id、Version、Channel 等
    └── …                        # 其余程序与依赖
```

`WindowsVelopackLocator`：若 `<root>` 可写，`packages` 建在 `<root>\packages`；不可写则回退到 `%LocalAppData%\{packId}\packages`。便携版启动时若根目录没有写权限，会先出无权限窗口并退出。

| | `current\` | `packages\` |
|--|------------|-------------|
| 内容 | 解压后的 EXE / DLL / 资源 / `sq.version` | `.nupkg`、下载中的 `.partial` |
| 职责 | 进程实际加载的文件 | 检查/下载/打补丁的基准与缓存 |
| 更新时 | `Update.exe` 用新版本**整目录替换** | 写入新包，完成后删掉不保留的 nupkg / partial |
| 误删 | 无法启动 | 当前仍能运行；下次更新没有本地 Full 基准，只能下完整包 |

**不要**把用户设置、数据库、日志放在 `current\` 里：替换 `current` 时会被清掉。Moonward 的用户数据在所选目录下的 `data\`（`AppConfig.DataSubFolderName`），与 Velopack 文件分开。

`sq.version` 是清单，不是 feed。Feed 是远端每个 Release 上的 `releases.{channel}.json`。

---

## 6. 下载、应用与静默更新

`UpdateInfo` 与构造它的 `IUpdateSource` 绑定（内部 `GitBaseAsset` 带着对应 Git Release，下载 URL 从该 Release 的附件解析）。因此：

- 检查若因 429 回落到 GitHub，静默下载必须继续用 GitHub（`LastCheckSource`）
- 更新窗口里手动改下载源且与上次检查不一致时，会对该源 **再跑一次** `CheckForUpdatesAsync`

流程：

1. `DownloadUpdatesAsync`：增量或全量，结果是 `packages\` 里一份目标版本 `*-full.nupkg`
2. 立即应用二选一：
   - `ApplyUpdatesAndRestart`：退出 → `Update.exe` 替换 `current\` → 重启（更新窗口「立即更新」）
   - `WaitExitThenApplyUpdates(..., silent: true, restart: false)`：通知 `Update.exe` 等本进程退出后再装，**最多等 60 秒**，超时则放弃。`App.Exit` 在真正退出前调用 `ApplySilentlyOnExit`
3. 若下载完但没走到退出钩子：下次启动时 `VelopackApp.Run()` 默认 **AutoApply**——本地已有更高版本的 full 包则先安装再启动。自动应用只向前升级，不能降级或换渠道

静默路径还会：关掉 RPC「退出后继续跑」（避免锁住 `current\` 文件）、置位 `PendingSilentUpdateContent`。从更新窗口手动重启会清掉该标记，避免下次再弹「最近更新了什么」。

---

## 7. 限流与回落

### 7.1 量级（公开、匿名客户端）

| 平台 | 匿名 | 认证 | 窗口 | 超限状态码 |
|------|------|------|------|------------|
| CNB（`cnb.cool` 匿名可读接口） | 约 **20 次 / 分钟**（`X-Ratelimit-Limit`） | 带令牌时由账户配额决定；本客户端不用令牌 | 短周期防突发 | `429` |
| GitHub REST | **60 次 / 小时 / IP** | PAT / 用户 **5,000 次 / 小时** | 小时级主配额 | 主配额耗尽为 `403` 或 `429`（`x-ratelimit-remaining: 0`） |

GitHub 另有次级限制（文档会调整）：并发不超过 100；REST 约 900 point / 分钟（普通 GET 为 1 point）等。次级超限同样可能是 `403` / `429`。把「每秒 15 次 GET」理解成 900/分钟的粗算可以，但**不是**客户端策略所依赖的承诺值。

「CNB 每小时 1200 次」只是 `20 × 60` 的换算，**不是**文档里的第二档小时配额。对本应用有约束力的是单分钟 20 次：一次检查若超过 20 次，会在这一分钟内 429。

### 7.2 客户端策略

针对 CNB 短窗口：

- 检查请求数裁剪到 2～11（`TrimToRequiredReleases`）
- 自动检查最短间隔 1 小时
- 并发检查合并为一次
- 失败（含 429）也记入 1 小时时钟

仅当检查阶段收到 **`HttpRequestException` 且 `StatusCode == 429`** 时，才回落 GitHub。其它错误（超时、DNS、5xx）不会换源。GitHub 检查另有 **30 秒** 等待上限（Velopack 下载器默认超时约 30 分钟，部分网络下 GitHub 会长时间无响应）。GitHub 也失败则重新抛出**原来的 CNB 429**，UI 显示限流文案（`Lang.UpdateService_TooManyRequests`）。

回落后 `LastCheckSource = GitHub`，更新窗口默认选中 GitHub，避免用户再对已被限流的 CNB 打一轮检查。

---

## 8. 相关代码

| 路径 | 职责 |
|------|------|
| `src/Starward/Program.cs` | `VelopackApp.Build().Run()` |
| `src/Starward/Features/Update/UpdateService.cs` | 检查、下载、静默循环、回落、应用 |
| `src/Starward/Features/Update/CnbSource.cs` | CNB 列表、裁剪、资产 URL |
| `src/Starward/Features/Update/UpdateDownloadSource.cs` | `Cnb` / `GitHub` |
| `src/Starward/Features/ViewHost/MainView.xaml.cs` | 窗口激活时检查 / 展示静默更新说明 |
| `src/Starward/Features/Startup/ResidentHost.cs` | 拉起常驻静默更新循环 |
| `src/Starward/Features/Setting/AboutSetting.xaml.cs` | 手动检查与开关 |
| `src/Starward.Setup.Core/ReleaseClient.cs` | GitHub 发行说明（不下载更新包） |
| `.github/workflows/release.yml` | 打包并发布到 GitHub |
| `.cnb.yml` | 镜像资产到 CNB |

外部行为以 Velopack 1.2.0 源码为准，尤其是 `GitBase.GetReleaseFeed`、`UpdateManager.CheckForUpdatesAsync` / `CreateDeltaUpdateStrategy` / `DownloadUpdatesAsync`。
