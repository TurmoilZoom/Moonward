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

export const intro = {
  zh: 'Moonward 是基于 Starward 的开源第三方启动器，面向米哈游 PC 游戏。多套启动配置、自动签到、抽卡记录，桌面图标或一条链接就能开游戏。',
  en: 'Moonward is an open-source third-party launcher based on Starward for miHoYo PC games. Launch profiles, auto check-in, and gacha logs — start a game from a desktop icon or a single URL.',
}

/** Feature cards for ordinary users — short titles + plain language. */
export const featureCards = [
  {
    id: 'profile',
    icon: '⚙',
    accent: 'teal',
    name: { zh: '多启动配置', en: 'Launch profiles' },
    detail: {
      zh: '同一个游戏可存多套设置：账号、启动参数各记一份。换着用点一下就行。',
      en: 'Save several setups per game — account and launch options included. Switch with one click instead of retyping.',
    },
  },
  {
    id: 'shortcut',
    icon: '⧉',
    accent: 'amber',
    name: { zh: '桌面快捷方式', en: 'Desktop shortcuts' },
    detail: {
      zh: '把某套设置做成桌面图标，双击就按这套开游戏，不必先打开启动器。图标也能换成自己的。',
      en: 'Turn a setup into a desktop icon. Double-click it and the game starts with that setup — no need to open the launcher first. You can use your own icon.',
    },
  },
  {
    id: 'uac',
    icon: '⇧',
    accent: 'cyan',
    name: { zh: '关闭 UAC 提示', en: 'Skip UAC prompt' },
    detail: {
      zh: '开游戏时 Windows 常会问「是否允许更改」。做桌面图标时勾上这项，同意一次就行，之后双击直接进游戏。',
      en: 'Windows often asks “Do you want to allow changes?” at launch. Tick this when creating a desktop icon: approve once, then double-click straight into the game.',
    },
  },
  {
    id: 'url',
    icon: '↗',
    accent: 'blue',
    name: { zh: 'URL 协议', en: 'URL protocol' },
    detail: {
      zh: '每套设置都有一条 moonward:// 链接，点开就按这套开游戏。可以放进脚本或别的工具里。',
      en: 'Every setup has a moonward:// link that launches the game with that setup. Drop it into a script or another tool.',
    },
  },
  {
    id: 'cloud',
    icon: '◷',
    accent: 'cyan',
    name: { zh: '云游戏时长', en: 'Cloud gaming time' },
    detail: {
      zh: '国服的云·原神、云·绝区零可在首页看畅玩卡和剩余时长。打开自动获取，软件运行时每天替你领一次免费时长。',
      en: 'Cloud Genshin and Cloud Zenless (CN) show pass status and time left on the home page. Turn auto-claim on and the daily free time is collected while the app runs.',
    },
  },
  {
    id: 'checkin',
    icon: '✓',
    accent: 'green',
    name: { zh: '自动签到', en: 'Auto check-in' },
    detail: {
      zh: '每个游戏一个开关，打开后立刻开始领当天奖励。软件留在托盘时，过了零点还会再签。',
      en: 'One switch per game — turn it on and today’s reward is claimed right away. Leave the app in the tray and it checks in again after midnight.',
    },
  },
  {
    id: 'gacha',
    icon: '◈',
    accent: 'violet',
    name: { zh: '抽卡记录', en: 'Gacha history' },
    detail: {
      zh: '出货次数、连 UP / 连歪、不歪概率都在同一张卡上。列表与紧凑网格随时切换，卡片可拖动排序，也能导出分享图。',
      en: 'Pull counts, streaks, and rates sit on one card. Switch between list and compact grid, drag cards to reorder, and export a share image.',
    },
  },
  {
    id: 'report',
    icon: '▤',
    accent: 'blue',
    name: { zh: '战绩自动更新', en: 'Auto-refresh records' },
    detail: {
      zh: '深渊、式舆防卫战、月报可设每隔几天、每周或每月几号更新。软件启动时自动补上到期的，免得忘了刷新就缺一期。',
      en: 'Abyss, Shiyu Defense, and monthly reports refresh every few days, weekly, or on a chosen day. Moonward runs what is due at startup, so forgetting no longer costs you a period.',
    },
  },
  {
    id: 'sync',
    icon: '⇄',
    accent: 'amber',
    name: { zh: '局域网同步', en: 'LAN sync' },
    detail: {
      zh: '换电脑时，一台开共享、另一台输验证码，抽卡、战绩、游戏时长就并过来了。只补缺，不动已有记录。',
      en: 'Moving to another PC: share from one, type the code on the other, and gacha, records, and play time come across. Only missing rows are added.',
    },
  },
  {
    id: 'wallpaper',
    icon: '❀',
    accent: 'rose',
    name: { zh: '自定义背景', en: 'Custom backgrounds' },
    detail: {
      zh: '支持图片和视频。绝区零还可把百科好感壁纸、满影画设成背景，并可随机换一张。',
      en: 'Use your own image or video. In Zenless Zone Zero, wiki Trust wallpapers and Mindscape stills can be the background, with optional random shuffle.',
    },
  },
  {
    id: 'update',
    icon: '↻',
    accent: 'indigo',
    name: { zh: '静默更新', en: 'Silent update' },
    detail: {
      zh: '后台下载新版本，缩在托盘时也会检查。退出后自动安装，下次打开会看到更新内容。',
      en: 'Downloads in the background — including while sitting in the tray — and installs after you quit. Release notes appear the next time you start.',
    },
  },
  {
    id: 'redeem',
    icon: '#',
    accent: 'slate',
    name: { zh: '前瞻直播兑换码', en: 'Livestream codes' },
    detail: {
      zh: '国服版本前瞻直播期间，首页展示官方兑换码，可单个或全部复制。不必去直播间翻评论。',
      en: 'During CN version livestreams, official codes appear on the home page — copy one or all. No need to hunt comments in the stream.',
    },
  },
]

/**
 * In-app screenshots in public/screens/ (1184×668 WebP).
 * Hash: #screens or #screens/<id>
 */
export const screens = [
  {
    id: 'config',
    src: 'screens/config.webp',
    width: 1184,
    height: 668,
    icon: '⚙',
    accent: 'teal',
    name: { zh: '启动配置', en: 'Launch profile' },
    tag: { zh: '参数 · 账号 · 链接', en: 'Args · account · link' },
    caption: {
      zh: '命令行参数、自定义启动程序、登录账号存成一套，下方就是这套的 moonward:// 链接。同一游戏可存多套。',
      en: 'Command-line args, a custom launcher, and the signed-in account make one profile; its moonward:// link sits at the bottom. Save several per game.',
    },
    alt: {
      zh: 'Moonward 启动参数配置对话框：配置文件下拉、登录账号、命令行参数、自定义启动程序与 moonward:// 链接。',
      en: 'Moonward launch-profile dialog: profile picker, signed-in account, command-line args, custom launcher, and the moonward:// link.',
    },
  },
  {
    id: 'bettergi',
    src: 'screens/betterGI.webp',
    width: 1184,
    height: 668,
    icon: '⌘',
    accent: 'amber',
    name: { zh: 'BetterGI 参数', en: 'BetterGI args' },
    tag: { zh: '常用命令行 · 一键勾选', en: 'Preset args · one tick' },
    caption: {
      zh: '勾选 BetterGI 的常用项，就会自动拼成命令行。把 BetterGI 设成启动程序即可联动。',
      en: 'Tick the BetterGI presets and the command line is built for you. Set BetterGI as the custom launcher to hook it up.',
    },
    alt: {
      zh: 'Moonward「常用命令行参数」对话框中的 BetterGI 分组：启动、一条龙、调度器配置组等勾选项，下方为组合结果。',
      en: 'Moonward common command-line args dialog showing a BetterGI group: start, one-dragon, and scheduler-group checkboxes with a combined-result field below.',
    },
  },
  {
    id: 'cloud',
    src: 'screens/cloud.webp',
    width: 1184,
    height: 668,
    icon: '◷',
    accent: 'cyan',
    name: { zh: '云游戏时长', en: 'Cloud gaming time' },
    tag: { zh: '免费时长 · 自动领取', en: 'Free time · auto claim' },
    caption: {
      zh: '首页就能看畅玩卡和剩余时长。打开「自动获取免费时长」，软件运行时每天替你领一次。',
      en: 'Pass status and time left, right on the home page. Turn on auto-claim and the daily free time is collected while the app runs.',
    },
    alt: {
      zh: 'Moonward 首页的云游戏面板：畅玩卡状态、免费时长、邦邦点时长与「自动获取免费时长」开关。',
      en: 'Moonward home-page cloud-gaming panel: pass status, free time, Bangboo-point time, and the auto-claim switch.',
    },
  },
  {
    id: 'checkin',
    src: 'screens/checkin.webp',
    width: 1184,
    height: 668,
    icon: '✓',
    accent: 'green',
    name: { zh: '自动签到', en: 'Daily check-in' },
    tag: { zh: '月历 · 自动签到', en: 'Calendar · auto claim' },
    caption: {
      zh: '月历看本月奖励。打开自动签到后会自己领；问号能跳到开机自启。',
      en: 'A monthly reward calendar. Turn auto check-in on and it claims for you; the question mark jumps to start-at-login.',
    },
    alt: {
      zh: 'Moonward 签到面板：本月奖励月历、今日已签到状态与自动签到开关。',
      en: 'Moonward check-in panel: monthly reward calendar, today claimed, and the auto check-in switch.',
    },
  },
  {
    id: 'gacha',
    src: 'screens/gacha.webp',
    width: 1184,
    height: 668,
    icon: '◈',
    accent: 'violet',
    name: { zh: '抽卡记录', en: 'Gacha history' },
    tag: { zh: '卡池 · 紧凑视图 · 概率', en: 'Pools · compact view' },
    caption: {
      zh: '一屏看完全部卡池：出货次数、连 UP / 连歪、保底进度。列表与紧凑网格随时切换。',
      en: 'Every pool on one screen: pull counts, streaks, and pity progress. Switch between list and compact grid at any time.',
    },
    alt: {
      zh: 'Moonward 抽卡记录页面：四个卡池的统计卡片，以紧凑网格列出历史出货与抽数。',
      en: 'Moonward gacha history: four pool stat cards showing past pulls as a compact icon grid with pull counts.',
    },
  },
  {
    id: 'report',
    src: 'screens/report.webp',
    width: 1184,
    height: 668,
    icon: '▤',
    accent: 'blue',
    name: { zh: '月报与战绩', en: 'Reports & records' },
    tag: { zh: '月报 · 自动更新', en: 'Report · auto refresh' },
    caption: {
      zh: '月报、式舆防卫战这类数据可各设一个更新频率，比如每月 2 号归档上月，软件启动时自动补上。',
      en: 'Monthly reports and mode records each get their own schedule — archive last month on the 2nd, say — and Moonward catches up at startup.',
    },
    alt: {
      zh: 'Moonward 绳网月报页面：月份列表、非林收入构成与每日数据，右上浮层设置更新频率与上次更新时间。',
      en: 'Moonward Inter-Knot report page: month list, income breakdown, and daily data, with a flyout setting the refresh schedule.',
    },
  },
  {
    id: 'data',
    src: 'screens/data.webp',
    width: 1184,
    height: 668,
    icon: '⇄',
    accent: 'amber',
    name: { zh: '数据与同步', en: 'Data & sync' },
    tag: { zh: '备份 · 局域网同步', en: 'Backup · LAN sync' },
    caption: {
      zh: '数据文件夹与数据库备份都在这一页。局域网同步输个验证码就能把另一台的记录并过来，账号信息不参与同步。',
      en: 'Data folder and database backups live on this page. LAN sync pulls another machine’s records over with a code; account logins are left out.',
    },
    alt: {
      zh: 'Moonward 应用设置的数据管理页：文件路径、备份数据库、删除个人设置与局域网同步。',
      en: 'Moonward data-management settings: file paths, database backup, reset options, and LAN sync.',
    },
  },
]

/**
 * Launch pipeline: config → entry points → resolve → game.
 * Used by the flow diagram section.
 */
export const launchFlow = {
  title: {
    zh: '快捷启动流程',
    en: 'Quick-launch flow',
  },
  lead: {
    zh: '先把「玩哪个游戏、用什么参数、哪个账号」存成一套，再用桌面图标或一条链接一键打开。',
    en: 'Save which game, which launch options, and which account as one setup, then open it from a desktop icon or a single link.',
  },
  steps: [
    {
      id: 'config',
      tag: { zh: '配置文件', en: 'Profile' },
      title: { zh: '创建启动配置', en: 'Create a launch profile' },
      desc: {
        zh: '给游戏存一套设置：启动参数、绑定账号。同一游戏可有多套，互不干扰。',
        en: 'Save a setup for the game: launch options and a bound account. A game can have several, and they do not interfere.',
      },
    },
    {
      id: 'entries',
      tag: { zh: '入口', en: 'Entry' },
      title: { zh: '选择唤起方式', en: 'Pick an entry' },
      desc: {
        zh: '一套设置可以落到两种日常入口上，效果一样：都按这套设置启动游戏。',
        en: 'A setup can become either of two everyday entry points; both launch the game with that setup.',
      },
      branches: [
        {
          id: 'shortcut',
          title: { zh: '桌面快捷方式', en: 'Desktop shortcut' },
          desc: {
            zh: '生成桌面图标，双击即按这套设置开游戏。勾选「关闭 UAC 提示」后，只需在创建时同意一次管理员授权。',
            en: 'Creates a desktop icon for that setup; double-click to launch. With skip-UAC ticked, you approve administrator rights once at creation.',
          },
        },
        {
          id: 'url',
          title: { zh: 'URL 协议', en: 'URL protocol' },
          desc: {
            zh: 'moonward:// 链接里带上游戏、设置与账号，浏览器、脚本都能打开。',
            en: 'A moonward:// link carrying game, setup, and account — open it from a browser or a script.',
          },
        },
      ],
    },
    {
      id: 'resolve',
      tag: { zh: '解析', en: 'Resolve' },
      title: { zh: 'Moonward 读取配置', en: 'Moonward resolves the profile' },
      desc: {
        zh: '无论从哪个入口进来，启动器都会读出这套设置，准备好启动参数与账号。',
        en: 'Whichever entry you came from, the launcher reads that setup and prepares the options and account.',
      },
    },
    {
      id: 'game',
      tag: { zh: '游戏', en: 'Game' },
      title: { zh: '启动游戏', en: 'Start the game' },
      desc: {
        zh: '按这套设置拉起游戏。如果软件本来没在运行，会缩到托盘继续待命。',
        en: 'Starts the game with that setup’s options. If Moonward was not already running, it stays in the tray afterwards.',
      },
    },
  ],
  urlExample: 'moonward://startgame/{game_biz}?profile=…&uid=…',
}

/**
 * Auto check-in: per-game toggle → runs while Moonward is in the tray → claim, then again after midnight.
 */
export const checkInFlow = {
  title: {
    zh: '自动签到流程',
    en: 'Auto check-in flow',
  },
  lead: {
    zh: '每个游戏一个开关，打开后立刻开始领。软件留在托盘时，过了零点还会再签。开机自启或用快捷方式开游戏，都能把软件留在托盘。',
    en: 'One switch per game — turn it on and it claims right away. Leave the app in the tray and it checks in again after midnight. Start-at-login or a desktop icon can both leave it sitting there.',
  },
  steps: [
    {
      id: 'enable',
      tag: { zh: '开关', en: 'Toggle' },
      title: { zh: '按游戏开启', en: 'Enable per game' },
      desc: {
        zh: '签到面板上每个游戏一个开关，互不影响，打开后立刻开始领。问号可跳到开机自启，让软件在登录 Windows 后自己缩到托盘。',
        en: 'Each game has its own switch on the check-in panel. Turn it on and claiming starts right away. The question mark jumps to start-at-login, so the app tucks itself into the tray once you sign in to Windows.',
      },
    },
    {
      id: 'start',
      tag: { zh: '启动', en: 'Start' },
      title: { zh: '软件在运行就会签', en: 'Runs while Moonward is open' },
      desc: {
        zh: '只要软件在运行就会签。日常两种开法都行。',
        en: 'Check-in runs whenever the app is running. Either everyday way of starting it counts.',
      },
      branches: [
        {
          id: 'app',
          title: { zh: '打开软件', en: 'Open Moonward' },
          desc: {
            zh: '打开主界面，或开机自启直接缩到托盘。',
            en: 'Open the main window, or start at login straight into the tray.',
          },
        },
        {
          id: 'shortcut',
          title: { zh: '快捷方式开游戏', en: 'Launch by shortcut' },
          desc: {
            zh: '用桌面图标或链接开游戏时，如果软件还没运行，会留在托盘并开始签到。',
            en: 'When a desktop icon or link starts a game and Moonward was not running, it stays in the tray and starts check-in.',
          },
        },
      ],
    },
    {
      id: 'claim',
      tag: { zh: '领取', en: 'Claim' },
      title: { zh: '查询并签到', en: 'Look up, then claim' },
      desc: {
        zh: '没签的自动领，签过的跳过。软件一直留在托盘时，过了零点还会再签一轮。',
        en: 'Unclaimed rewards are taken; already-claimed days are skipped. If the app stays in the tray, it runs another round after midnight.',
      },
    },
  ],
}

export const requirements = [
  {
    label: { zh: '系统', en: 'OS' },
    value: { zh: 'Windows 10 1809 及以上', en: 'Windows 10 1809 or later' },
  },
  {
    label: { zh: '运行时', en: 'Runtime' },
    value: { zh: 'WebView2 Runtime', en: 'WebView2 Runtime' },
  },
  {
    label: { zh: '可选', en: 'Optional' },
    value: {
      zh: 'WebP 映像扩展；系统「透明效果」「动画效果」以获得最佳观感',
      en: 'WebP Image Extension; system transparency & animations for best visuals',
    },
  },
]
