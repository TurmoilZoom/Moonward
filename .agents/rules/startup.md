# 启动流程

1. `Program.Main`：**Velopack 必须最先**（`VelopackApp.Build().Run()`），处理安装/更新/卸载后可直接退出。**禁止在 Velopack 前插入逻辑。**
2. `App.OnLaunched`：
   - 先特判 `moonward://test/`（须在环境/DI 之前，否则 `CacheFolder` 未就绪会崩）
   - `CheckEnviromentAsync()`
   - `DispatchStartupAsync`：按 DI 注册顺序跑 `IStartupHandler`（当前：rpc → playtime → startgame → urlprotocol）；`StartupOutcome.Exit` 则 `Environment.Exit`
   - 单实例（`main`）→ `MainWindow` 或仅托盘（`--hide`）
3. 新命令行模式：实现 `IStartupHandler`（`Features/Startup/`）+ DI 注册；动词常量 `StartupVerbs`。
