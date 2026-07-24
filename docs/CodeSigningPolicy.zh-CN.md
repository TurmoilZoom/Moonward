[English](./CodeSigningPolicy.md) | 简体中文

# 代码签名策略（Code signing policy）

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

（免费代码签名由 [SignPath.io](https://about.signpath.io) 提供，证书由 [SignPath Foundation](https://signpath.org) 签发。）

## 项目说明

**Moonward** 是面向米哈游 PC 游戏（原神、崩坏：星穹铁道、绝区零、崩坏3）的开源第三方启动器。本仓库基于 [Scighost/Starward](https://github.com/Scighost/Starward) 公开维护，采用 [MIT 许可证](../LICENSE)。

- 源码仓库：[TurmoilZoom/Moonward](https://github.com/TurmoilZoom/Moonward)
- 官方下载：[GitHub Releases](https://github.com/TurmoilZoom/Moonward/releases)
- 项目主页：[https://turmoilzoom.github.io/Moonward/](https://turmoilzoom.github.io/Moonward/)

我们仅为本仓库构建并发布的 **Moonward** 发行产物申请代码签名，不为无关项目的二进制文件签名。

## 签名范围

通过 [GitHub Releases](https://github.com/TurmoilZoom/Moonward/releases) 发布的 Windows 发行包，包括发布流水线生成的：

- 安装包（`Setup.exe`）与便携版包
- 用于分发的应用程序可执行文件及相关二进制（例如 Velopack 包）

在符合 [SignPath Foundation 条件](https://signpath.org/terms.html) 的前提下，安装包中可能包含未重新签名的上游 / 第三方可再分发组件。

## 构建与签名流程

1. 发行产物由本公开仓库的持续集成构建（[`.github/workflows/release.yml`](../.github/workflows/release.yml)）。
2. 仅将本仓库 CI 构建的产物提交至 SignPath 签名。
3. 代码签名私钥由 SignPath 在 HSM 中生成并保管，本项目**不**保存或导出私钥。
4. 每次签名请求须经 Approver（见下）人工批准。

请仅从官方 [GitHub Releases](https://github.com/TurmoilZoom/Moonward/releases)（或维护者明确列出的镜像）下载 Moonward。

## 团队角色

当前为单人维护项目，下列角色均由同一维护者承担，直至增加其他受信维护者。

| 角色 | 职责 | 成员 |
|------|------|------|
| **Authors**（提交者） | 可在本仓库修改源码与构建脚本 | [@TurmoilZoom](https://github.com/TurmoilZoom) |
| **Reviewers**（审查者） | 审查非提交者提出的变更（如 PR）后再合并 | [@TurmoilZoom](https://github.com/TurmoilZoom) |
| **Approvers**（批准者） | 批准每次面向发布的 SignPath 签名请求 | [@TurmoilZoom](https://github.com/TurmoilZoom) |

策略说明：

- 外部 Pull Request 须由维护者审查后再合并。
- 每次签名请求须经 Approver 明确批准。
- 仓库与 SignPath 访问在可用时启用多因素认证。

## 隐私政策

见 [隐私策略](./Privacy.zh-CN.md)。

简要说明：Moonward 使用过程中产生的数据保存在用户设备上。因安装、运行或配置相关功能而产生的网络请求（例如游戏 / 账号服务、可选元数据更新、应用更新）由用户使用行为触发。第三方在线服务（如米哈游 / HoYoverse、GitHub）适用其各自隐私政策。

## 参考

- [SignPath Foundation 开源项目条件](https://signpath.org/terms.html)
- [SignPath.io](https://about.signpath.io)
