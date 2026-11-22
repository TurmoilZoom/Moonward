# 安全策略

简体中文 · [English](#english)

## 支持的版本

只为最新的正式版和最新的预览版提供安全修复。Moonward 内置自动更新，报告前请先更新到最新版本，确认问题依然存在。

| 版本 | 是否支持 |
|------|:--------:|
| 最新正式版 | ✅ |
| 最新预览版（pre-release） | ✅ |
| 更早的版本 | ❌ |

## 报告漏洞

**请不要在公开的 Issue、Discussions 或 Pull Request 中披露安全漏洞。**

请通过 GitHub 私密漏洞报告提交：打开 [Report a vulnerability](https://github.com/TurmoilZoom/Moonward/security/advisories/new)（也可从仓库的 **Security** 页进入），报告内容只有你和维护者能看到。

报告中请尽量包含：

- 受影响的 Moonward 版本（设置 → 关于）与 Windows 版本
- 漏洞类型与影响，例如能读取哪些数据、能以什么权限执行什么操作
- 复现步骤或概念验证（PoC）
- 修复建议（可选）

**不要在报告中附带你自己的米游社 / HoYoLAB Cookie、stoken 等账号凭据**；日志与截图请先打码。

## 处理流程

本项目由个人业余维护，无法承诺固定的响应时间，但会尽快：

1. 确认收到报告，评估影响范围；
2. 在私密环境中修复，必要时与你沟通细节；
3. 发布修复版本后公开安全公告，并在你同意时致谢。

在修复版本发布之前，请不要公开漏洞细节。

## 适用范围

适用于本仓库的代码与发布的安装包，重点关注：

- 本地保存的账号凭据（米游社 / HoYoLAB Cookie、stoken 等）泄露，或可被其他程序、其他用户读取
- `moonward://` URL 协议或命令行参数导致非预期的程序执行
- 提权组件被滥用造成权限提升，包括 `Starward.RPC` 提权安装服务与免 UAC 启动的计划任务
- 应用更新或游戏下载的内容未经校验、可被篡改或替换
- 局域网同步的未授权访问或数据泄露

不在范围内：

- 游戏本体、米哈游官方服务器或 API 的漏洞，请向米哈游报告
- 账号封禁、风控、反作弊相关问题
- 需要攻击者已拥有本机管理员权限或可物理接触设备才能利用的问题
- 第三方依赖中已公开披露的漏洞（欢迎直接提 Issue 提醒升级）

如果漏洞同样存在于上游 [Scighost/Starward](https://github.com/Scighost/Starward)，请一并向上游报告。

---

## English

### Supported versions

Security fixes are provided only for the latest stable release and the latest pre-release. Moonward updates itself automatically; please update to the latest version and confirm the issue still exists before reporting.

### Reporting a vulnerability

**Please do not disclose security vulnerabilities in public issues, discussions or pull requests.**

Report privately through GitHub: open [Report a vulnerability](https://github.com/TurmoilZoom/Moonward/security/advisories/new) (also reachable from the repository's **Security** tab). Only you and the maintainers can see the report.

Please include the affected Moonward and Windows versions, the type and impact of the issue, steps to reproduce or a proof of concept, and optionally a suggested fix. **Do not attach your own miHoYo / HoYoLAB cookies, stoken or other account credentials**, and redact logs and screenshots.

### What to expect

This project is maintained by one person in their spare time, so there is no guaranteed response time. Reports will be acknowledged and assessed as soon as possible, fixed privately, and published as a security advisory once a fixed release is available, with credit if you wish. Please keep the details private until then.

### Scope

In scope: leakage of locally stored account credentials; unintended execution through the `moonward://` URL protocol or command-line arguments; privilege escalation through the elevated components (`Starward.RPC` and the scheduled tasks used to skip UAC); unverified or tamperable app updates and game downloads; unauthorized access through LAN sync.

Out of scope: vulnerabilities in the games or in miHoYo's servers and APIs (report them to miHoYo); account bans, risk control and anti-cheat; issues that require local administrator rights or physical access; publicly disclosed vulnerabilities in third-party dependencies (feel free to open an issue asking for an upgrade).

If the vulnerability also affects upstream [Scighost/Starward](https://github.com/Scighost/Starward), please report it there as well.
