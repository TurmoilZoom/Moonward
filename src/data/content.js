export const links = {
  download: 'https://github.com/TurmoilZoom/Moonward/releases/latest',
  github: 'https://github.com/TurmoilZoom/Moonward',
  upstream: 'https://github.com/Scighost/Starward',
  issues: 'https://github.com/TurmoilZoom/Moonward/issues',
}

export const games = [
  { id: 'hk4e', name: '原神', nameEn: 'Genshin Impact', tone: '#5ec8ff' },
  { id: 'hkrpg', name: '崩坏：星穹铁道', nameEn: 'Honkai: Star Rail', tone: '#8b9dff' },
  { id: 'nap', name: '绝区零', nameEn: 'Zenless Zone Zero', tone: '#ff6b8a' },
  { id: 'bh3', name: '崩坏3', nameEn: 'Honkai Impact 3rd', tone: '#ffb347' },
]

export const highlights = [
  {
    icon: '⚡',
    title: '一键启动',
    titleEn: 'One-click Launch',
    desc: '多启动配置、桌面快捷方式与 moonward:// 协议，指定游戏、账号与参数随心启动。',
    descEn: 'Multiple launch profiles, desktop shortcuts and moonward:// URL protocol.',
  },
  {
    icon: '📊',
    title: '抽卡统计',
    titleEn: 'Gacha Analytics',
    desc: '垫数、保底进度、连 UP / 连歪与自定义排序卡片，把每一次抽卡都算清楚。',
    descEn: 'Pity tracking, guarantee progress, streak stats and draggable stat cards.',
  },
  {
    icon: '✅',
    title: '每日签到',
    titleEn: 'Daily Check-in',
    desc: '米游社 / HoYoLAB 签到与自动签到；启动游戏时也可顺带完成签到。',
    descEn: 'miHoYo / HoYoLAB check-in with automation when launching games.',
  },
  {
    icon: '🔐',
    title: '登录改进',
    titleEn: 'Smarter Login',
    desc: '国服短信验证码、国际服网页登录；Cookie 失效时静默换票刷新。',
    descEn: 'SMS captcha (CN), web login (OS), silent cookie refresh when expired.',
  },
  {
    icon: '🎨',
    title: '亚克力界面',
    titleEn: 'Acrylic UI',
    desc: 'WinUI 3 亚克力质感、Composition 动效与即时提示，桌面端也有流畅触感。',
    descEn: 'WinUI 3 acrylic surfaces, composition motion and instant tooltips.',
  },
  {
    icon: '🌍',
    title: '多语言',
    titleEn: 'Localization',
    desc: '社区驱动的 Crowdin 本地化，覆盖中英日韩等多语环境。',
    descEn: 'Community-driven Crowdin localization for many languages.',
  },
]

export const featureBlocks = [
  {
    id: 'launcher',
    eyebrow: 'Launcher',
    title: '为桌面玩家重写启动体验',
    titleEn: 'A launcher rebuilt for PC players',
    desc: '快速启动菜单、自定义背景、下侧工具栏图钉固定……在官方启动器之外，用更干净的布局掌控你的米哈游游戏库。',
    descEn: 'Quick-launch menu, custom backgrounds and a pinned toolbar — a cleaner home for your miHoYo library.',
    points: [
      '多套启动参数与账号，数量不限',
      '桌面快捷方式一键直达',
      'URL 协议 moonward:// 脚本化启动',
    ],
    pointsEn: [
      'Unlimited launch profiles & accounts',
      'Desktop shortcuts for one-tap entry',
      'moonward:// URL protocol automation',
    ],
    image: 'screenshots/launcher-home.png',
    imageAlt: 'Moonward 启动器首页',
  },
  {
    id: 'gacha',
    eyebrow: 'Gacha',
    title: '抽卡记录，看得见的运气',
    titleEn: 'Gacha history you can actually read',
    desc: '卡池统计卡片支持拖拽排序与边缘自动滚动；列表可鼠标拖拽浏览，统计信息吸顶，连 UP / 连歪、不歪概率一目了然。',
    descEn: 'Draggable pool cards, sticky stats, UP / lose streaks and no-lose probability at a glance.',
    points: [
      '垫数 / 保底进度可视化',
      '抽卡统计分享图',
      '原神 / 绝区零等支持相关同步方式',
    ],
    pointsEn: [
      'Pity & guarantee progress visuals',
      'Shareable gacha summary images',
      'Sync options for Genshin / ZZZ and more',
    ],
    image: 'screenshots/gacha-stats.png',
    imageAlt: 'Moonward 抽卡统计',
    reverse: true,
  },
  {
    id: 'record',
    eyebrow: 'Game Record',
    title: '战绩与每日数据，统一布局',
    titleEn: 'Records and daily tools, unified',
    desc: '米游社工具箱统计页与「每日数据」界面布局统一优化，签到、实时便笺与常用工具收在同一套流畅交互里。',
    descEn: 'Unified toolbox layouts for stats, daily data, check-in and notes.',
    points: [
      '每日签到与自动签到',
      '国服 stoken 静默换票',
      '多游戏战绩入口',
    ],
    pointsEn: [
      'Daily & auto check-in',
      'Silent CN stoken refresh',
      'Multi-game record entry points',
    ],
    image: 'screenshots/game-record.png',
    imageAlt: 'Moonward 战绩与工具',
  },
  {
    id: 'polish',
    eyebrow: 'Polish',
    title: '细节决定手感',
    titleEn: 'Polish you can feel',
    desc: '亚克力背景、按下缩放、悬停位移、浮层展开……页面动效接入 Composition；InstantTooltip 让悬停说明几乎无延迟。',
    descEn: 'Acrylic glass, press scale, hover shift and instant tooltips powered by Composition.',
    points: [
      'WinUI 3 原生桌面体验',
      'Composition 驱动的微动效',
      '基于 Starward 的开源扩展',
    ],
    pointsEn: [
      'Native WinUI 3 desktop feel',
      'Composition-driven micro-motion',
      'Open-source fork of Starward',
    ],
    image: 'screenshots/launcher-detail.png',
    imageAlt: 'Moonward 界面细节',
    reverse: true,
  },
]

export const gallery = [
  {
    src: 'screenshots/launcher-home.png',
    caption: '启动器首页',
    captionEn: 'Launcher home',
  },
  {
    src: 'screenshots/launcher-detail.png',
    caption: '游戏详情与背景',
    captionEn: 'Game detail & backdrop',
  },
  {
    src: 'screenshots/gacha-stats.png',
    caption: '抽卡统计',
    captionEn: 'Gacha statistics',
  },
  {
    src: 'screenshots/game-record.png',
    caption: '战绩 / 每日数据',
    captionEn: 'Records & daily tools',
  },
  {
    src: 'screenshots/quick-tools.png',
    caption: '快捷工具',
    captionEn: 'Quick tools',
  },
]

export const requirements = [
  { label: '系统', labelEn: 'OS', value: 'Windows 10 1809+' },
  { label: '运行时', labelEn: 'Runtime', value: 'WebView2' },
  { label: '扩展', labelEn: 'Extension', value: 'WebP 映像扩展' },
  { label: '体验', labelEn: 'Best with', value: '透明效果 + 动画效果' },
]
