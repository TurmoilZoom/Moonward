# 致谢

Moonward 基于 [Scighost/Starward](https://github.com/Scighost/Starward) 开发，在功能与设计上参考了下列优秀的开源项目。感谢这些项目的作者与维护者——正是他们的工作让本项目得以站在巨人的肩膀上。

## 上游基础

- **[Starward](https://github.com/Scighost/Starward)** — 本项目的上游，提供了整个米哈游游戏启动器的框架与绝大部分功能。

## 界面与动效

- **[Collapse](https://github.com/CollapseLauncher/Collapse)** — 首页横幅轮播 `BannerCarousel` 借鉴其 `PanelSlideshow`（首尾连续、圆点指示器、翻页按钮）；自绘标题栏按钮、原神 HDR 亮度设置对话框、翻页按钮样式亦参考其实现。
- **[Character-Map-UWP](https://github.com/character-map-uwp/Character-Map-UWP)** — 设置页内容的入场级联动画（`PlayEntrance`）与浮层展开 / 收缩动画（`UseExpandContractAnimation`）1:1 移植自该项目。

## 功能与接口

- **[Snap.Hutao](https://github.com/DGP-Studio/Snap.Hutao)** — 每日签到卡片（签到 / 补签 / 自动签到）与抽卡 authkey 的实现思路。
- **[TeyvatGuide](https://github.com/BTMuli/TeyvatGuide)** — 登录与换票（stoken → ltoken / cookie_token）、极验登录、Cookie 失效刷新、抽卡 authkey 等接口形态与请求头细节。
- **[MihoyoBBSTools](https://github.com/Womsxd/MihoyoBBSTools)** — 各游戏签到的 act_id、活动主机与「模拟真人节奏」的参考。
- **[Miao-Yunzai](https://github.com/yoimiya-kokomi/Miao-Yunzai)** — 国服前瞻直播兑换码：从官方账号动态解析直播页 `act_id`、首页导航备用路径、调用 miyolive `index` / `refreshCode`，以及 `remain > 0` 表示尚未可领。
- **[UIGF · mihoyo-api-collect](https://github.com/UIGF-org/mihoyo-api-collect)** — 米哈游相关 API 的社区文档，以及 UIGF 抽卡数据交换格式标准。

---

上游 Starward 以 [MIT](https://github.com/Scighost/Starward/blob/main/LICENSE) 协议开源，本项目同样以 [MIT](LICENSE) 协议开源。上述参考仅涉及思路、接口形态与交互 / 动效设计的借鉴，具体代码均为本项目按自身架构重新实现。
