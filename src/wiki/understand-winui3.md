# .NET 编译模型梳理 & Moonward（Starward）工程实践

> 本文整理自个人笔记 `windows_dotnet_concepts`，并结合本仓库 **Moonward**（产品品牌；工程命名空间仍为 `Starward.*`，WinUI 3 / .NET 10）的**真实构建配置**做对照落地。
> 配置引用截至 2026-08-27，出处见文末「配置出处」。凡带 📌 的段落即「概念 → 本项目怎么做」的对照。

---

## 目录

1. [总览：从 C# 到 CPU](#一总览从-c-到-cpu)
2. [IL（中间语言）](#二il中间语言)
3. [JIT（即时编译）](#三jit即时编译)
4. [R2R（ReadyToRun）](#四r2rreadytorun)
5. [NativeAOT](#五nativeaot)
6. [三者对比](#六三者对比)
7. [裁剪 Trimming / Tree Shaking](#七裁剪-trimming--tree-shaking)
8. [COM 与反射：AOT/裁剪的两大阻力](#八com-与反射aot裁剪的两大阻力)
9. [Windows 应用框架 & Desktop Bridge / MSIX](#九windows-应用框架--desktop-bridge--msix)
10. [Moonward 落地全景（汇总表）](#十moonward-落地全景汇总表)
11. [配置出处](#配置出处)

---

## 一、总览：从 C# 到 CPU

C# 源代码不会被直接编译成机器码，而是先编译成 **IL**，再由不同策略转成机器码：

```
C# 源代码
  │  C# 编译器
  ▼
 IL（中间语言，跨架构）
  │
  ├──────────────── JIT ─────────────► 运行时按需编译 ──┐
  │                                                     │
  └── AOT ─┬─ R2R ──────► 预编译一部分 + 运行时仍可 JIT ─┤──► 机器码 ──► CPU
           │                                             │
           └─ NativeAOT ─► 静态分析 + 裁剪 + 尽量全量预编译 ┘
```

两个「等式」（笔记原文，值得记住）：

```
R2R       = 预编译 + IL + JIT + .NET Runtime
NativeAOT  = 静态分析 + 裁剪 + AOT + 尽量减少运行时动态编译
```

一句话概括三种模式的取向：

```
JIT       → “运行的时候才编译”
R2R       → “提前编译一部分，运行时还能 JIT”
NativeAOT → “尽量全部提前编译，运行时基本不需要 JIT”
```

📌 **本项目取向**：`JIT + R2R + 部分裁剪 + 自包含`，**不使用 NativeAOT**。后文逐项对照。

---

## 二、IL（中间语言）

- C# 编译器产出的是 IL，不是机器码。
- **同一份 IL 可面向多种 CPU 架构**生成对应机器码，这是 .NET「一次编写、多架构分发」的基础：

```
同一份 IL
  ├── x64    → x86-64 机器码
  ├── ARM64  → ARM64 机器码
  └── 其他架构 → 对应机器码
```

📌 **本项目**：声明三套 RID 并行分发——

```xml
<Platforms>x86;x64;ARM64</Platforms>
<RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
```

CI（`build.yml`）用矩阵同时构建 **x64 / arm64** 两个架构（各含 Debug/Release），产物按架构分渠道发布。

---

## 三、JIT（即时编译）

**JIT = Just-In-Time Compiler**，真正执行到某个方法时才把它的 IL 编译成机器码，即**按需编译**。

```
C# → IL → JIT → 机器码 → CPU
```

- **优点**：JIT 知道程序运行时的真实情况，能针对**当前机器与实际执行路径**做优化（分层编译、去虚化等）。
- **缺点：启动慢**。一个大型 .NET GUI 程序的冷启动要走一长串：

```
启动程序 → 加载 .NET Runtime → 加载程序集 → 初始化框架
        → JIT 一部分代码 → 初始化 UI → 显示窗口
```

📌 **本项目**：这正是 GUI 启动器最在意的痛点——用户双击到看到窗口的时间。故对 Release 叠加 **R2R** 来削减「启动时 JIT」这一段（见下节）。

---

## 四、R2R（ReadyToRun）

**R2R 是微软提供的折中方案**：既然完全 JIT 有启动成本，那就在**发布时**提前把一部分 IL 编成原生机器码；但又不彻底放弃 JIT。

```
C# 源码 → 编译 → IL → R2R 编译 → 预先生成的机器码 → 发布
```

核心目标偏向：**减少启动成本，而非追求所有场景下的最高运行性能。**

| 优点                     | 代价                       |
| ------------------------ | -------------------------- |
| 启动更快                 | 发布文件可能变大           |
| 减少启动时 JIT 工作量    | 发布过程更复杂             |
| 对大型 .NET 应用很有价值 | 需针对目标运行环境生成代码 |
| ——                       | 仍保留一部分 JIT           |

> R2R 镜像里**同时含预编译机器码和原始 IL**，所以运行时 JIT 仍能在需要时接管/再优化——这也是「R2R = 预编译 + IL + JIT + .NET Runtime」的含义（它依赖完整 .NET 运行时）。

📌 **本项目（教科书式的 R2R 用法）**：发布配置里对非 Debug 开启 R2R，Debug 关闭（Debug 求快、求可调试）：

```xml
<!-- win-x64.pubxml / win-x86.pubxml / win-arm64.pubxml -->
<SelfContained>true</SelfContained>
<PublishSingleFile>False</PublishSingleFile>
<PublishReadyToRun Condition="'$(Configuration)' == 'Debug'">False</PublishReadyToRun>
<PublishReadyToRun Condition="'$(Configuration)' != 'Debug'">True</PublishReadyToRun>
```

即 **Release = R2R 加速启动，Debug = 纯 JIT**。发布为**自包含**（`SelfContained=true`，且 `WindowsAppSDKSelfContained=true`）：把 .NET 运行时与 Windows App SDK 一并打包，用户机器无需预装。

---

## 五、NativeAOT

**AOT = Ahead-Of-Time Compilation**。NativeAOT 更彻底——运行时尽可能**直接执行已生成的机器码**，基本不需要 JIT。

```
发布时： IL → AOT → 机器码
运行时： 直接执行
```

代价是**编译器必须提前知道更多东西**，因此 NativeAOT 高度依赖 **Tree Shaking / Trimming（裁剪）**：

```
原始程序 → 分析依赖关系 → 找到实际使用的代码 → 删除没用的代码 → AOT 编译
```

**局限**：程序到底需要哪些类型，很多时候**要到运行时才知道**（典型就是**反射**）。加上大量 **COM 互操作**与部分第三方库支持不完整，NativeAOT 在这类项目上阻力大、不划算。

📌 **本项目为什么不用 NativeAOT**（与笔记开篇一致，且能被 csproj 佐证）：

> 「没有用 NativeAOT 是因为这玩意没办法搞大量的 COM 互操作与 Reflection，以及部分 NuGet 套件可能对 NativeAOT 的支持还有限。」

具体佐证——项目**同时踩中** NativeAOT 的两大难点：

- **重度 COM 互操作**：`BuiltInComInteropSupport=true`，依赖 WinUI 3 / Win2D / ComputeSharp.D2D1 / 一堆 `Vanara.PInvoke.*`。
- **依赖反射**：`JsonSerializerIsReflectionEnabledByDefault=true`（反射兜底），外加 Dapper 等运行时反射型库。

> 补充（务实而非绝对）：现代 .NET 对 WinRT/COM（`[GeneratedComInterface]`、CsWinRT AOT 优化）与带注解的反射已有相当多 AOT 友好化工作，但对一个**存量庞大、动态性强**的 WinUI 3 启动器而言，全量 NativeAOT 的收益远抵不过适配成本。项目选择了「**部分 AOT 友好化**」而非「全 NativeAOT」——见下条。
>
> 📌 折中点：`CsWinRTAotOptimizerEnabled` 在 **Release 开、Debug 关**。它只是让 CsWinRT 生成更 AOT/裁剪友好的 WinRT 封送代码（减少运行时反射式 marshalling），**并不等于**启用 NativeAOT。

---

## 六、三者对比

| 维度                        | JIT                  | R2R                                    | NativeAOT                  |
| --------------------------- | -------------------- | -------------------------------------- | -------------------------- |
| 何时编成机器码              | 运行时按需           | 发布时预编译**一部分**，运行时仍可 JIT | 发布时**尽量全部**         |
| 运行时是否需要 JIT / 运行时 | 需要                 | 需要（保留 JIT + 完整运行时）          | 基本不需要 JIT             |
| 启动速度                    | 慢                   | 快（主要卖点）                         | 最快                       |
| 峰值运行性能                | 可按真实执行情况优化 | 接近 JIT                               | 好，但少了运行时自适应优化 |
| 产物体积                    | 小                   | 偏大                                   | 取决于裁剪                 |
| 反射 / 动态 COM 友好度      | 高                   | 高                                     | **低**（最大痛点）         |
| 本项目                      | Debug                | **Release** ✅                          | 不用 ❌                     |

---

## 七、裁剪 Trimming / Tree Shaking

裁剪 = 分析依赖 → 只保留实际用到的代码 → 删掉其余，以**减小体积**（也是 NativeAOT 的前置步骤）。

**天敌是反射**：编译期无法静态看出「运行时会反射到哪个类型」，裁剪器可能把「看似没人用、实则被反射调用」的代码删掉，导致运行时 `MissingMethod/Type` 崩溃。这与 NativeAOT 的局限同源。

📌 **本项目的裁剪策略——「要瘦身，但要安全」**：

```xml
<PublishTrimmed>True</PublishTrimmed>
<TrimMode>partial</TrimMode>   <!-- 关键：部分裁剪，而非 full -->
```

- `TrimMode=partial`：只裁剪**显式标注可裁剪**的程序集，对反射重的库保守放过，规避「误删反射目标」。
- **配合源生成 JSON**规避反射式序列化：CLAUDE.md 明确要求「JSON 一律 `*JsonContext.Default`」「新 DTO 必须注册（源生成）」。源生成的序列化器是**裁剪/AOT 安全**的，不走反射。
- 结果：**R2R + partial 裁剪**兼顾「启动快、体积可控、反射不炸」，而不冒 `TrimMode=full` 的风险。

---

## 八、COM 与反射：AOT/裁剪的两大阻力

**COM（Component Object Model）** 是 Windows 古老但至关重要的组件技术：

- C# 通过 **Interop** 与 COM 打交道；
- COM 天然带**运行时动态性**；
- **大量 COM Interop 的项目**通常比纯托管代码更需要评估 AOT/裁剪兼容性。

**反射**同理：类型/成员在运行时才确定，静态分析看不全。

> 这两点合起来，就是「为什么 R2R 稳、NativeAOT 难」的根因——R2R 保留了运行时与 JIT，动态性有兜底；NativeAOT 把运行时动态能力压到最小，正好撞上 COM/反射。

📌 **本项目正是「重 COM + 用反射」的典型**，所以自然落在 **R2R 这一档**，而非 NativeAOT。

---

## 九、Windows 应用框架 & Desktop Bridge / MSIX

先厘清两条正交的轴：**程序运行模型** vs **UI 框架**。

```
                        Windows 应用
              ┌───────────────┴───────────────┐
          程序运行模型                        UI 框架
        ┌──────┴──────┐                 ┌──────┴──────┐
      Win32          UWP              WinUI 3       WinUI 2
        └──────┬──────┘
               │
       Desktop Bridge  ── 让传统 Win32 程序更容易进入现代 Windows 应用生态
               │
              MSIX  ── 解决「怎么打包 / 安装」
```

- **Desktop Bridge**：把传统 `.exe`（Win32）**包装/转换**，让其获得部分现代 Windows 应用能力，并可用 **MSIX** 打包安装。
- **边界**：它**不会**让一个需要管理员权限的 Win32 程序凭空变成能随意调用 UWP API 的 App。**系统级管理、驱动、管理员权限、某些 COM/Win32 API** 仍受桌面程序自身权限模型与 Windows 安全机制约束。

📌 **本项目在这张图上的位置**：**Win32 运行模型 + WinUI 3 UI + 未打包（unpackaged）**，**不走 Desktop Bridge / MSIX / 商店**。

- `WindowsPackageType=None` → **未打包的 Win32 桌面应用**；
- 分发/更新走 **Velopack + GitHub Releases**（`Velopack 1.2.0`），不是 MSIX/Store；
- 对应「Desktop Bridge 给不了管理员权限」这一边界：项目把**提权操作独立成 `Starward.RPC` 进程**（命名管道 gRPC），需要时单独提权，而非奢望容器/桥接放权。

---

## 十、Moonward 落地全景（汇总表）

| 概念              | 通用含义                     | 本项目实际配置 / 做法                                        | 出处                |
| ----------------- | ---------------------------- | ------------------------------------------------------------ | ------------------- |
| 运行模型          | JIT / R2R / NativeAOT        | **JIT（Debug）+ R2R（Release）**，不用 NativeAOT             | `*.pubxml`、csproj  |
| R2R               | 发布时预编译一部分，提升启动 | `PublishReadyToRun=True`（仅非 Debug）                       | `win-*.pubxml`      |
| 裁剪              | 删无用代码，瘦身             | `PublishTrimmed=True` + `TrimMode=partial`                   | `win-*.pubxml`      |
| 反射规避          | 裁剪/AOT 下避免反射序列化    | JSON 一律源生成 `*JsonContext.Default`                       | CLAUDE.md           |
| 自包含            | 打包运行时，免预装           | `SelfContained=true`、`WindowsAppSDKSelfContained=true`、非单文件 | csproj、`*.pubxml`  |
| AOT 友好化        | 减少运行时动态封送           | `CsWinRTAotOptimizerEnabled`：Release 开 / Debug 关          | csproj              |
| 多架构（IL 优势） | 同 IL 多架构                 | RID `win-x86;win-x64;win-arm64`，CI 出 x64/arm64             | csproj、`build.yml` |
| COM 互操作        | 动态性高、AOT 难             | `BuiltInComInteropSupport=true`；WinUI/Win2D/D2D1/Vanara     | csproj              |
| 打包/分发模型     | Win32 vs UWP、MSIX vs 未打包 | Win32 未打包（`WindowsPackageType=None`）+ Velopack          | csproj              |
| 提权边界          | 桥接不放权，需自行提权       | 独立 `Starward.RPC`（命名管道 gRPC）承担提权                 | 解决方案结构        |
| SDK 锁定          | 固定工具链保证可复现         | `global.json` 锁 `10.0.301`（`rollForward: latestMajor`）    | `global.json`       |
| 构建常量          | 按渠道裁功能                 | CI 加 `DONOT_CHECK_UPDATE`；入口 `DISABLE_XAML_GENERATED_MAIN` | `build.yml`、csproj |

**一句话总结本项目的取舍**：一个**重 COM、带反射、需提权**的 WinUI 3 启动器，最合适的形态就是——**自包含 + Release R2R 提速启动 + partial 裁剪瘦身 + 源生成 JSON 保裁剪安全 + Velopack 分发**，而把「全量 NativeAOT / MSIX 商店化」这类和自身动态性、权限模型相冲突的路线**主动排除**。

---

## 配置出处

| 事实                                                         | 文件                                                         |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| SDK `10.0.301`、`rollForward: latestMajor`                   | `global.json`                                                |
| TFM `net10.0-windows10.0.26100.0`、min `10.0.17763.0`；`Platforms`/`RuntimeIdentifiers`；`WindowsPackageType=None`；`WindowsAppSDKSelfContained`；`BuiltInComInteropSupport`；`JsonSerializerIsReflectionEnabledByDefault`；`CsWinRTAotOptimizerEnabled`；`DISABLE_XAML_GENERATED_MAIN`；`Velopack 1.2.0` | `src/Starward/Starward.csproj`                               |
| `SelfContained`/`PublishSingleFile`/`PublishReadyToRun`/`PublishTrimmed`/`TrimMode` | `src/Starward/Properties/PublishProfiles/win-{x64,x86,arm64}.pubxml` |
| CI 矩阵 Debug/Release × x64/arm64、`dotnet publish -r win-<plat> -p:DefineConstants=DONOT_CHECK_UPDATE` | `.github/workflows/build.yml`                                |
| 「JSON 一律 `*JsonContext.Default`、新 DTO 源生成注册」「Velopack + GitHub Releases 分发」「`Starward.RPC` 提权」 | `CLAUDE.md`                                                  |

> 说明：概念部分整理自笔记 `windows_dotnet_concepts`；「📌 本项目」与汇总表中的配置值均来自上表所列仓库文件（截至 2026-08-27）。变基上游后如构建配置有变，请以代码现状为准复核本表。
