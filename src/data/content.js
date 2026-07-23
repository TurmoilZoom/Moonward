export const links = {
  download: 'https://github.com/TurmoilZoom/Moonward/releases/latest',
  github: 'https://github.com/TurmoilZoom/Moonward',
  upstream: 'https://github.com/Scighost/Starward',
  issues: 'https://github.com/TurmoilZoom/Moonward/issues',
  license: 'https://github.com/TurmoilZoom/Moonward/blob/rebase/develop/LICENSE',
}

export const games = {
  zh: ['原神', '崩坏：星穹铁道', '绝区零', '崩坏3'],
  en: ['Genshin Impact', 'Honkai: Star Rail', 'Zenless Zone Zero', 'Honkai Impact 3rd'],
}

/** Feature groups — short titles, concrete behavior, no marketing tone. */
export const featureGroups = [
  {
    id: 'launch',
    title: { zh: '启动与账号', en: 'Launch & accounts' },
    items: [
      {
        name: { zh: '多启动配置', en: 'Launch profiles' },
        detail: {
          zh: '可为同一游戏保存多套启动参数与账号，数量不限；可创建桌面快捷方式。',
          en: 'Unlimited profiles per game (args + account). Desktop shortcuts supported.',
        },
      },
      {
        name: { zh: 'URL 协议', en: 'URL protocol' },
        detail: {
          zh: 'moonward:// 可指定游戏、配置与账号并直接启动。',
          en: 'moonward:// launches a game with a chosen profile and account.',
        },
      },
      {
        name: { zh: '登录', en: 'Sign-in' },
        detail: {
          zh: '国服短信验证码；国际服网页登录。国服 Cookie 失效时可用 stoken 静默换票（最多重试一次）。',
          en: 'CN: SMS captcha. OS: web login. CN cookies can refresh via stoken once when expired.',
        },
      },
    ],
  },
  {
    id: 'gacha',
    title: { zh: '抽卡', en: 'Gacha' },
    items: [
      {
        name: { zh: '抽卡记录', en: 'History & stats' },
        detail: {
          zh: '卡池统计卡片可拖拽排序（近边缘自动横滚）；列表可拖拽滚动；统计吸顶；连 UP / 连歪、不歪概率等。',
          en: 'Draggable pool cards (edge auto-scroll), draggable lists, sticky stats, UP/lose streaks, no-lose rate.',
        },
      },
      {
        name: { zh: '分享图', en: 'Share image' },
        detail: {
          zh: '生成含垫数、保底进度等信息的统计分享图。',
          en: 'Export a summary image with pity and guarantee progress.',
        },
      },
      {
        name: { zh: '同步', en: 'Sync' },
        detail: {
          zh: '原神、绝区零等可通过米游社相关方式更新记录。',
          en: 'Genshin / ZZZ and others can refresh logs via miHoYo community APIs where available.',
        },
      },
      {
        name: { zh: '绝区零元数据', en: 'ZZZ item metadata' },
        detail: {
          zh: '图标与多语言名称从 metadata 分支（jsDelivr）拉取；可用战绩 Cookie 从养成指南更新。',
          en: 'Icons and localized names from the metadata branch (jsDelivr); can refresh via record cookie.',
        },
      },
    ],
  },
  {
    id: 'daily',
    title: { zh: '签到与战绩', en: 'Check-in & records' },
    items: [
      {
        name: { zh: '每日签到', en: 'Daily check-in' },
        detail: {
          zh: '米游社 / HoYoLAB 签到，支持自动签到。通过桌面快捷方式、URL 协议或命令行启动游戏时，可对该账号顺带签到。',
          en: 'miHoYo / HoYoLAB check-in with optional automation. Launching via shortcut, URL, or CLI can check in for that account.',
        },
      },
      {
        name: { zh: '统计与每日数据', en: 'Stats & daily tools' },
        detail: {
          zh: '米游社工具箱统计页与「每日数据」界面布局统一，签到与常用工具入口集中。',
          en: 'Unified layouts for toolbox stats and daily tools.',
        },
      },
    ],
  },
  {
    id: 'ui',
    title: { zh: '界面', en: 'UI' },
    items: [
      {
        name: { zh: '首页', en: 'Home' },
        detail: {
          zh: '快速启动菜单、自定义背景入口、下侧工具栏图钉固定等。',
          en: 'Quick-launch menu, custom background entry, pinable bottom toolbar.',
        },
      },
      {
        name: { zh: '呈现', en: 'Presentation' },
        detail: {
          zh: 'WinUI 3 亚克力背景；Composition 动效（按下缩放、悬停位移、浮层等）；InstantTooltip 即时提示。',
          en: 'WinUI 3 acrylic; Composition motion; InstantTooltip.',
        },
      },
      {
        name: { zh: '本地化', en: 'Localization' },
        detail: {
          zh: '界面文案经 Crowdin 维护，多语言可用。',
          en: 'UI strings maintained on Crowdin.',
        },
      },
    ],
  },
]

export const requirements = [
  { label: { zh: '系统', en: 'OS' }, value: { zh: 'Windows 10 1809 及以上', en: 'Windows 10 1809 or later' } },
  { label: { zh: '运行时', en: 'Runtime' }, value: { zh: 'WebView2 Runtime', en: 'WebView2 Runtime' } },
  {
    label: { zh: '可选', en: 'Optional' },
    value: {
      zh: 'WebP 映像扩展（背景图异常时检查）；系统「透明效果」「动画效果」',
      en: 'WebP Image Extension; system transparency & animations for best visuals',
    },
  },
]

export const intro = {
  zh: 'Moonward 是基于 Starward 的开源第三方启动器，面向米哈游 PC 端游戏：安装 / 启动、多账号与启动配置、抽卡记录、签到与战绩相关工具。工程命名空间仍为 Starward.*，产品名称为 Moonward。',
  en: 'Moonward is an open-source third-party launcher based on Starward for miHoYo PC games: install/launch, multi-account profiles, gacha logs, check-in and related record tools. Code namespaces remain Starward.*; product name is Moonward.',
}
