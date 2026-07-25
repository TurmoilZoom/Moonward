<h1 align="center">Moonward</h1>

<p align="center">
  基于 <a href="https://github.com/Scighost/Starward">Starward</a> 的米哈游 PC 启动器<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">下载</a>
</p>


---

在上游 Starward 的基础上，主要做了这些功能：

- **每日签到** — 米游社 / HoYoLAB 签到，支持自动签到；桌面快捷方式 / URL 协议 / 命令行启动游戏时，也会对该账号顺带签到
- **卡池分享** — 抽卡统计分享图，垫数 / 保底进度
- **抽卡记录** — 卡池统计卡片可自定义拖拽排序（靠近边缘自动横向滚动），列表支持鼠标拖拽滚动；统计信息吸顶、连 UP / 连歪、不歪概率等
- **多启动配置** — 多套启动参数与账号，数量不限；可做桌面快捷方式
- **URL 协议** — `moonward://` 一键启动指定游戏 / 配置 / 账号
- **登录改进** — 国服短信验证码登录；国际服网页登录；国服 Cookie 失效时静默用 stoken 换票刷新（最多重试一次）
- **抽卡同步** — 原神 / 绝区零等可通过米游社相关方式更新记录
- **绝区零物品元数据** — 图标与多语言名称改为从 `metadata` 分支 + jsDelivr 拉取；可用战绩 Cookie 从养成指南更新，维护者可提交回仓库
- **首页 UI** — 快速启动菜单、自定义背景入口、下侧工具栏图钉固定等布局与动效调整
- **统计 / 每日数据** — 米游社工具箱统计页与「每日数据」界面布局统一与优化
- **界面体验** — 亚克力风格；页面动效接入 Composition（按下缩放、悬停位移、浮层展开等）；InstantTooltip 即时提示

安装包见 [Releases](https://github.com/TurmoilZoom/Moonward/releases)。

上游项目：[Scighost/Starward](https://github.com/Scighost/Starward)  
许可证：[MIT](LICENSE)

隐私策略：[docs/Privacy.md](docs/Privacy.md) · [中文](docs/Privacy.zh-CN.md)
