<h1 align="center">Moonward</h1>

<p align="center">
  基于 <a href="https://github.com/Scighost/Starward">Starward</a> 的开源第三方启动器，面向米哈游 PC 游戏<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">下载</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · <a href="README.zh-TW.md">繁體中文</a>
  · <a href="README.en-US.md">English</a>
  · <a href="README.de-DE.md">Deutsch</a>
  · <a href="README.es-ES.md">Español</a>
  · <a href="README.it-IT.md">Italiano</a>
  · <a href="README.ja-JP.md">日本語</a>
  · <a href="README.ko-KR.md">한국어</a>
  · <a href="README.ru-RU.md">Русский</a>
  · <a href="README.th-TH.md">ไทย</a>
  · <a href="README.vi-VN.md">Tiếng Việt</a>
</p>


---

在上游 Starward 的基础上，把常用操作收进桌面快捷方式与一条 URL，并在签到、抽卡、背景等方面做了增强。主要功能：

#### 抽卡

- **抽卡记录** — 卡池统计可拖拽排序（靠近边缘自动横向滚动）、列表支持拖拽滚动、统计吸顶；连 UP / 连歪、不歪概率等一目了然；千星奇域「已垫」改用进度条
- **筛选与分享** — 标题栏下拉筛选显示哪些卡池，可全选 / 反选 / 重置；一键生成磨砂风格分享图，含垫数与保底进度
- **抽卡同步** — 原神 / 绝区零等可通过米游社相关方式更新记录；抽到未收录的新角色时自动补全图标与名称；物品名跟随应用语言
- **数据互通** — 支持 UIGF 抽卡记录导入 / 导出；可从上游 Starward 只读导入历史数据

#### 账号与工具箱

- **每日签到** — 米游社 / HoYoLAB 签到，每个游戏独立开关，支持自动签到与补签；用快捷方式 / URL / 命令行启动游戏时，也会给该账号单独签一次
- **登录改进** — 国服用手机号收验证码登录，国际服走网页登录；登录过期时尽量自动续上，不必反复重新登录
- **月报与便笺** — 工具箱月报（开拓月历 / 绳网月报 / 旅行札记）布局统一；绳网月报修正跨时区每日数据、默认显示当月；实时便笺遇风控时提供验证入口

#### 启动

- **多启动配置** — 同一游戏可保存多套启动参数与自定义启动程序，数量不限；切换配置、改参数不必每次重填，可命名保存并生成桌面快捷方式
- **URL 协议** — `moonward://` 指定游戏、配置与账号直接启动 / 停止 / 重启，也可单独触发签到；能嵌入脚本或网页（详见 [docs/UrlProtocol](UrlProtocol.zh-CN.md)）
- **快速启动** — 首页汉堡菜单集成游戏设置、快速启动与「生成开始菜单快捷方式」

#### 外观与背景

- **好感壁纸** — 绝区零可将百科「好感动态壁纸」与「满影画静态壁纸」下载并设为自定义背景；打开画廊即用本地缓存，后台静默校验更新
- **自定义背景** — 独立的自定义背景对话框，支持图片 / 视频（可拖入首页直接替换）；从托盘恢复不再闪烁；背景列表更新后保留海报偏好

#### 其他

- **系统集成** — 可设置开机自启到托盘；关于页一键预填诊断信息并跳转 GitHub 反馈，同时打开日志文件夹
- **静默更新** — 后台下载新版本，退出软件后自动安装，下次启动展示更新内容（Velopack + GitHub Releases）

安装包见 [Releases](https://github.com/TurmoilZoom/Moonward/releases)。

上游项目：[Scighost/Starward](https://github.com/Scighost/Starward)  
致谢：[CREDITS.md](../CREDITS.md)（功能与设计参考的开源项目）  
许可证：[MIT](../LICENSE)

隐私策略：[docs/Privacy.md](Privacy.md) · [中文](Privacy.zh-CN.md)
