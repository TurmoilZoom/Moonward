<p align="center">
  <picture>
    <source srcset="https://github.com/Scighost/Starward/assets/61003590/9d369ec3-ab7c-408f-88c2-11bfe4453208" type="image/avif" />
    <img src="https://github.com/Scighost/Starward/assets/61003590/44552992-e2c5-451f-9c2a-73176e8e4e93" alt="Moonward Logo" width="240"/>
  </picture>
</p>

<h1 align="center">Moonward</h1>

<p align="center">
  🎮 开源的米哈游 PC 端游戏启动器，专为现代化 Windows 平台设计<br/>
  🎮 An open-source miHoYo PC game launcher for Windows, designed to fully replace HoYoPlay
</p>


<div align="center">
  <table>
    <tr>
      <td align="center" style="padding:0 10px;">
        <b>CI Build</b>
      </td>
      <td align="center" style="padding:0 10px;">
        <b>Latest Release</b>
      </td>
      <td align="center" style="padding:0 10px;">
        <b>Downloads</b>
      </td>
      <td align="center" style="padding:0 10px;">
        <b>License</b>
      </td>
    </tr>
    <tr>
      <td align="center" style="padding:0 10px;">
        <a href="https://github.com/TurmoilZoom/Starward/actions/workflows/build.yml">
          <img src="https://github.com/TurmoilZoom/Starward/actions/workflows/build.yml/badge.svg" alt="Build Status"/>
        </a>
      </td>
      <td align="center" style="padding:0 10px;">
        <a href="https://github.com/TurmoilZoom/Starward/releases/latest">
          <img src="https://img.shields.io/github/v/release/TurmoilZoom/Starward?style=flat" alt="Release"/>
        </a>
      </td>
      <td align="center" style="padding:0 10px;">
        <img src="https://img.shields.io/github/downloads/TurmoilZoom/Starward/total.svg?style=flat" alt="Downloads"/>
      </td>
      <td align="center" style="padding:0 10px;">
        <img src="https://img.shields.io/github/license/TurmoilZoom/Starward?style=flat" alt="MIT License"/>
      </td>
    </tr>
  </table>
</div>

---

## 📖 简介 / Introduction

**中文**

> **Moonward** 是本 fork 的产品品牌名，基于上游 **Starward**（出自星穹铁道开服前宣传语——愿此行，终抵群星 / May This Journey Lead Us **Starward**）发展而来。源码工程与 C# 命名空间仍保留 `Starward.*`，以便持续变基上游。

Moonward 是一款以 [MIT License](LICENSE) 开源的第三方游戏启动器，面向 Windows 10 及以上系统，支持米哈游 PC 端全部主要游戏。它在保留官方启动器核心能力的同时，提供游戏时间统计、抽卡记录、截图浏览、实时便笺、每日签到等拓展功能，并采用亚克力视觉与 Composition 动画，带来更现代的桌面体验。

本仓库（[TurmoilZoom/Starward](https://github.com/TurmoilZoom/Starward)）基于上游 [Scighost/Starward](https://github.com/Scighost/Starward) 维护，在持续同步上游改进的同时，合入额外的功能增强与问题修复。正式发行版见 [Releases](https://github.com/TurmoilZoom/Starward/releases)。

**English**

**Moonward** is the product brand of this fork, built on upstream **Starward**. Source project folders and C# namespaces remain `Starward.*` to keep rebasing upstream practical.

Moonward is an open-source third-party game launcher under the MIT license, built for Windows 10 and later. It supports all major miHoYo PC titles and aims to fully replace the official HoYoPlay launcher — with extras like playtime tracking, gacha history, screenshot gallery, daily notes, and sign-in rewards, wrapped in a modern acrylic UI with GPU-accelerated animations.

This repository ([TurmoilZoom/Starward](https://github.com/TurmoilZoom/Starward)) is a maintained fork of [Scighost/Starward](https://github.com/Scighost/Starward), carrying additional enhancements and fixes on top of upstream improvements. Download releases from [Releases](https://github.com/TurmoilZoom/Starward/releases).

---

## ✨ 功能特性 / Features

### 启动器核心

- 安装、更新、卸载、修复与验证游戏文件
- 多游戏、多区服统一管理（国服 / 国际服 / Bilibili 等）
- 游戏内公告窗口、云游戏入口（部分游戏）
- 硬链接节省磁盘空间（部分游戏）
- 游戏时间记录与统计
- 多启动配置文件（configN 与配置文件 N 对应，无数量上限；启动方式含「无」），支持 `moonward://` URL 协议远程启动
- 通过 [Velopack](https://github.com/velopack/velopack) 实现应用内自动更新

### 拓展工具

| 功能 | 说明 |
|------|------|
| 祈愿记录 | 自动同步与手动导入抽卡历史，支持 UIGF 格式 |
| 游戏截图 | 浏览与管理各游戏内截图 |
| 战绩 / 便笺 | 实时便笺、深渊记录等 GameRecord 数据 |
| 每日签到 | 米游社 / HoYoLAB 签到，支持自动签到 |
| 自助查询 | 游戏内数据自助导出（部分游戏） |

### 本 Fork 增强

在继承上游全部能力的基础上，本仓库还包含以下定制改动：

- 每日签到（SignIn）与自动签到后台任务
- `moonward://` URL 协议（启动游戏、切换配置等），详见 [URL 协议文档](docs/UrlProtocol.zh-CN.md)
- 游戏快捷方式与多配置文件支持
- 亚克力风格 UI 与 Composition 动画（首页呼吸灯、功能图钉、卡池拖拽等）
- 壁纸类型记忆、UIGF 导入修复、自定义背景稳定性等多项体验改进
- 发行渠道迁移至 GitHub Releases + Velopack，卸载时可选择是否清理用户数据

---

## 🎯 支持的游戏 / Supported Games

| 游戏 | 国服 | 国际服 | Bilibili |
|------|:----:|:------:|:--------:|
| 原神（Genshin Impact） | ✅ | ✅ | ✅ |
| 崩坏：星穹铁道（Honkai: Star Rail） | ✅ | ✅ | ✅ |
| 绝区零（Zenless Zone Zero） | ✅ | ✅ | ✅ |
| 崩坏 3（Honkai Impact 3rd） | ✅ | ✅ | — |

各游戏可用功能因区服而异（如祈愿记录、云游戏等），以应用内实际显示为准。

---

## 🚀 安装 / Installation

**中文**

1. 确认系统满足[环境要求](#-环境要求--requirements)（见下文）。
2. 前往 [GitHub Releases](https://github.com/TurmoilZoom/Starward/releases/latest) 下载对应 CPU 架构（x64 / arm64）的安装包。
3. 运行安装程序并按提示完成设置；首次启动时选择用户数据存放目录。
4. 为获得最佳视觉效果，建议在 Windows 设置中开启**透明效果**与**动画效果**。

**English**

1. Make sure your system meets the [requirements](#-环境要求--requirements) below.
2. Download the installer for your CPU architecture (x64 / arm64) from [GitHub Releases](https://github.com/TurmoilZoom/Starward/releases/latest).
3. Run the installer and follow the setup wizard; choose a data folder on first launch.
4. Enable **Transparency effects** and **Animation effects** in Windows Settings for the best visual experience.

---

## 💻 环境要求 / Requirements

- Windows 10 1809（17763）或更高版本
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2)
- [WebP 映像扩展](https://apps.microsoft.com/detail/9pg2dk419drg)（系统通常已预装；若背景图无法显示请自行检查）

---

## 🌐 本地化 / Localization

[![de-DE translation](https://img.shields.io/badge/dynamic/json?color=blue&label=de-DE&style=flat&logo=crowdin&query=%24.progress.0.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/de)
[![en-US translation](https://img.shields.io/badge/any_text-100%25-blue?logo=crowdin&label=en-US)](https://crowdin.com/project/starward)
[![it-IT translation](https://img.shields.io/badge/dynamic/json?color=blue&label=it-IT&style=flat&logo=crowdin&query=%24.progress.2.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/it)
[![ja-JP translation](https://img.shields.io/badge/dynamic/json?color=blue&label=ja-JP&style=flat&logo=crowdin&query=%24.progress.3.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/ja)
[![ko-KR translation](https://img.shields.io/badge/dynamic/json?color=blue&label=ko-KR&style=flat&logo=crowdin&query=%24.progress.4.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/ko)
[![ru-RU translation](https://img.shields.io/badge/dynamic/json?color=blue&label=ru-RU&style=flat&logo=crowdin&query=%24.progress.5.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/ru)
[![th-TH translation](https://img.shields.io/badge/dynamic/json?color=blue&label=th-TH&style=flat&logo=crowdin&query=%24.progress.6.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/th)
[![vi-VN translation](https://img.shields.io/badge/dynamic/json?color=blue&label=vi-VN&style=flat&logo=crowdin&query=%24.progress.7.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/vi)
[![zh-CN translation](https://img.shields.io/badge/dynamic/json?color=blue&label=zh-CN&style=flat&logo=crowdin&query=%24.progress.8.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/zh-CN)
[![zh-TW translation](https://img.shields.io/badge/dynamic/json?color=blue&label=zh-TW&style=flat&logo=crowdin&query=%24.progress.9.data.translationProgress&url=https%3A%2F%2Fbadges.awesome-crowdin.com%2Fstats-15878835-595799.json)](https://crowdin.com/project/starward/zh-TW)

应用内文案托管在 [Crowdin](https://crowdin.com/project/starward)，欢迎参与翻译与校对。详见 [本地化指南](docs/Localization.zh-CN.md)。

多语言 README 译文见 `docs/README.<语言>.md`。

---

## 📎 相关资源 / Resources

| 资源 | 链接 |
|------|------|
| 本仓库 Releases | https://github.com/TurmoilZoom/Starward/releases |
| 上游仓库 | https://github.com/Scighost/Starward |
| URL 协议文档 | [docs/UrlProtocol.zh-CN.md](docs/UrlProtocol.zh-CN.md) |
| 隐私策略 | [docs/Privacy.zh-CN.md](docs/Privacy.zh-CN.md) |
| 第三方库清单 | [docs/ThirdParty.md](docs/ThirdParty.md) |
| 贡献指南 | [CONTRIBUTING.md](CONTRIBUTING.md) |
| 问题反馈 | [Bug Report](https://github.com/TurmoilZoom/Starward/issues/new?template=bug_report.yml) / [Feature Request](https://github.com/TurmoilZoom/Starward/issues/new?template=feature_request.yml) |
| 上游赞助（Scighost） | https://donate.scighost.com |

---

## 🗑️ 如何卸载 / How to Uninstall

**中文**

Moonward 通过 [Velopack](https://github.com/velopack/velopack) 安装，卸载方式与常规 Windows 应用相同：

1. 打开 **设置 → 应用 → 已安装的应用**（快捷方式：`Win + X` → 已安装的应用）。
2. 找到 **Moonward**，选择**卸载**并等待完成。

**关于用户数据**

- 用户数据（数据库、缓存、背景图、抽卡记录等）存放在安装时选择的目录下的 `data\` 文件夹中，**不会**随 Velopack 默认卸载流程自动删除。
- 应用内 **设置 → 文件管理** 提供「卸载时删除我的数据」开关，**默认开启**。开启时，通过控制面板卸载会一并清理：
  - 用户数据目录（`<所选目录>\data`）
  - `moonward://` URL 协议注册
  - `HKCU\Software\Moonward` 注册表项
- 若希望卸载后保留数据以便重装续用，请在卸载前关闭该开关。
- 若你将数据目录迁移到非默认位置，卸载前请确认路径；本地数据仅有一份，请注意备份。
- 卸载诊断日志（如有）位于 `%TEMP%\Moonward.Uninstall.log`。

也可在安装目录运行 `Update.exe uninstall` 完成卸载，行为与控制面板一致。

**English**

Moonward is installed via Velopack. To uninstall, go to **Settings → Apps → Installed apps**, find **Moonward**, and click **Uninstall**.

User data lives in the `data\` subfolder of your chosen directory. Whether it is deleted on uninstall is controlled by the **Delete my data when uninstalling** option in **Settings → File Management** (enabled by default). Turn it off before uninstalling if you want to keep your data for a future reinstall.

---

## 🛠️ 贡献 / Contribute

欢迎通过 Issue 反馈问题、提出功能建议，或提交 Pull Request 参与开发。

- [贡献指南](CONTRIBUTING.md) — 分支策略、构建验证、编码规范与 PR 检查清单
- [提交 Pull Request](https://github.com/TurmoilZoom/Starward/pulls)
- 通用 Bug 修复也可考虑向上游 [Scighost/Starward](https://github.com/Scighost/Starward) 提交

---

## 📈 开发 / Development

```powershell
# 日常开发验证（推荐）
dotnet build src/Starward/Starward.csproj -c Debug -p:Platform=x64
```

需要 Visual Studio 2022及以上（.NET 桌面开发 + C++ 桌面开发 + UWP 工作负载）或 [.NET SDK](https://dotnet.microsoft.com/download)（版本见 [`global.json`](global.json)）。更详细的架构说明见 [`AGENTS.md`](AGENTS.md) 与 [`CLAUDE.md`](CLAUDE.md)。

---

## 📦 引用的 Scighost 资源 / Scighost Resources

本项目基于 [Scighost/Starward](https://github.com/Scighost/Starward) 源码发展而来，并在运行时依赖上游维护的若干组件与基础设施：

| 类型 | 资源 | 说明 |
|------|------|------|
| 上游源码 | [Scighost/Starward](https://github.com/Scighost/Starward) | 项目起源；本 fork 定期变基同步 |
| NuGet 包 | [Starward.Assets](https://www.nuget.org/packages/Starward.Assets) | 游戏静态资源 |
| NuGet 包 | [Starward.Codec](https://www.nuget.org/packages/Starward.Codec) | 音视频编解码 |
| NuGet 包 | [Starward.GameInput](https://www.nuget.org/packages/Starward.GameInput) | 游戏输入相关 |
| NuGet 包 | [Starward.NativeLib](https://www.nuget.org/packages/Starward.NativeLib) | 原生库封装 |
| NuGet 包 | [Starward.Win2D](https://www.nuget.org/packages/Starward.Win2D) | Win2D 图形渲染 |
| NuGet 包 | [Scighost.WinUI](https://www.nuget.org/packages/Scighost.WinUI) | WinUI 扩展控件 |
| 本地化平台 | [Crowdin — starward](https://crowdin.com/project/starward) | 应用内多语言翻译（与上游共享） |
| 文档素材 | [Scighost/Starward assets](https://github.com/Scighost/Starward) | Logo、截图等视觉资源 |

感谢 [Scighost](https://github.com/Scighost) 创建并长期维护 Moonward 上游项目。若你的改动属于通用改进而非 fork 定制，也欢迎直接向上游贡献。

---

## 🙏 特别感谢 / Special Thanks

- [**Scighost**](https://github.com/Scighost) — Moonward 原作者与上游维护者

**相关开源项目 / Related Open-Source Projects**

- [**Snap Hutao Remastered**](https://github.com/TurmoilZoom/Snap.Hutao.Remastered) — 同生态 Windows 工具箱，社区协作与文档实践的参考
- [**ZenlessTools**](https://github.com/JamXi233/ZenlessTools) — 绝区零相关工具与接口探索
- [**MihoyoBBSTools**](https://github.com/Womsxd/MihoyoBBSTools) — 米游社签到与社区 API 的先行实践

**UI 与交互参考 / UI & Interaction References**

- [**Community Toolkit for Windows**](https://github.com/CommunityToolkit/Windows) — WinUI 控件、动画与 MVVM 基础设施
- [**LiveCharts2**](https://github.com/Live-Charts/LiveCharts2) — 图表动画与缓动曲线的参考实现
- [**Fluent UI System Icons**](https://github.com/microsoft/fluentui-system-icons) — 系统图标资源
- [**Files**](https://github.com/files-community/Files) — 现代化文件浏览交互的灵感来源
- [**ECharts.Net**](https://github.com/AZhrZho/ECharts.Net) — 数据可视化方案参考
- [**Win2D Samples**](https://github.com/microsoft/Win2D-Samples) — GPU 合成与 Win2D 渲染示例

以及本项目中使用的[第三方开源库](docs/ThirdParty.md)。

---

## ⚙️ 使用的技术栈 / Tech Stack

- [.NET 10](https://github.com/dotnet/runtime) / [C#](https://github.com/dotnet/roslyn)
- [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) / [Windows App SDK](https://github.com/microsoft/WindowsAppSDK)
- [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) — MVVM、WinUI 控件与动画
- [Velopack](https://github.com/velopack/velopack) — 安装、更新与卸载
- [Dapper](https://github.com/DapperLib/Dapper) + [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) — 本地数据存储
- [Serilog](https://github.com/serilog/serilog) — 结构化日志
- [grpc-dotnet](https://github.com/grpc/grpc-dotnet) — RPC 子进程通信
- [ComputeSharp](https://github.com/Sergio0694/ComputeSharp) / [Win2D](https://github.com/microsoft/Win2D) — GPU 图形与亚克力渲染
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) — 系统托盘
- [Vanara PInvoke](https://github.com/dahall/Vanara) — Windows API 互操作
- [MiniExcel](https://github.com/mini-software/MiniExcel) — Excel 导出
- [Moonward.*](https://www.nuget.org/profiles/Scighost) — 上游维护的原生库与资源包

---

## 📸 截图 / Screenshots

<img width="1200" src="https://github.com/user-attachments/assets/ddd51a20-9705-4112-a454-75b07b7a6f8f" alt="Moonward Screenshot"/>

---

## 📄 许可证 / License

本项目以 [MIT License](LICENSE) 发布。参与贡献即表示你同意将改动以相同许可证发布。

---

## 📊 项目统计 / Statistics

![Alt](https://repobeats.axiom.co/api/embed/05b2a2cde912e529f17335074769c5e530d8cb0e.svg "Repobeats analytics image")

[![Star History Chart](https://api.star-history.com/svg?repos=TurmoilZoom/Starward&type=Date)](https://star-history.com/#TurmoilZoom/Starward&Date)