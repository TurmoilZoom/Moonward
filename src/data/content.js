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
  zh: 'Moonward 是基于 Starward 的开源第三方启动器，面向米哈游 PC 游戏。一键启动、多账号配置、抽卡记录与每日签到，把常用操作收进桌面快捷方式和一条 URL。',
  en: 'Moonward is an open-source third-party launcher based on Starward for miHoYo PC games. Launch profiles, gacha logs, and daily check-in — all reachable from a desktop shortcut or a single URL.',
}

/** Feature cards for ordinary users — short titles + plain language. */
export const featureCards = [
  {
    id: 'profile',
    icon: '⚙',
    accent: 'teal',
    name: { zh: '多启动配置', en: 'Launch profiles' },
    detail: {
      zh: '同一个游戏可以存好几套设置：用哪个账号登录、加什么启动参数，各存一套，数量不限。换着用点一下就行，不必每次重填。',
      en: 'Save as many setups per game as you like — which account to sign in with, which launch options to add. Switch between them with one click instead of retyping.',
    },
  },
  {
    id: 'shortcut',
    icon: '⧉',
    accent: 'amber',
    name: { zh: '桌面快捷方式', en: 'Desktop shortcuts' },
    detail: {
      zh: '把某套设置做成桌面图标，双击就按这套设置开游戏，不必先打开启动器。图标也能换成自己的。',
      en: 'Turn a setup into a desktop icon: double-click it and the game starts with that setup — no need to open the launcher first. You can use your own icon, too.',
    },
  },
  {
    id: 'uac',
    icon: '⇧',
    accent: 'cyan',
    name: { zh: '关闭 UAC 提示', en: 'Skip UAC prompt' },
    detail: {
      zh: '这些游戏要用管理员权限运行，每次开都会弹出 Windows 的「是否允许更改」窗口。创建桌面图标时勾上这项，只在创建时同意一次，之后双击直接进游戏。不想要了可在「免 UAC 启动任务」里删掉。',
      en: 'These games need administrator rights, so Windows asks “Do you want to allow changes?” at every launch. Tick this box when creating a desktop icon: approve once, then double-click straight into the game. Remove them later in the Skip-UAC Start Tasks list.',
    },
  },
  {
    id: 'url',
    icon: '↗',
    accent: 'blue',
    name: { zh: 'URL 协议', en: 'URL protocol' },
    detail: {
      zh: '每套设置都有一条 moonward:// 链接，点开就启动对应的游戏。可以放进任务计划、脚本或别的工具，让它们替你开游戏。',
      en: 'Every setup has a moonward:// link that launches its game when opened. Drop it into Task Scheduler, a script, or another tool and let that start the game for you.',
    },
  },
  {
    id: 'checkin',
    icon: '✓',
    accent: 'green',
    name: { zh: '自动签到', en: 'Auto check-in' },
    detail: {
      zh: '每个游戏一个开关，改完下次打开软件生效。软件启动约十秒后，挨个替已开启的游戏领当天奖励。再开上「开机自启」，它会在开机后自己缩到任务栏右下角，签到照跑。',
      en: 'One switch per game, effective the next time you open Moonward. About 10 seconds after it starts, it claims the day’s reward for each enabled game in turn. Add start-at-login and it tucks itself into the tray at boot, so check-in still happens.',
    },
  },
  {
    id: 'gacha',
    icon: '◈',
    accent: 'violet',
    name: { zh: '抽卡记录', en: 'Gacha history' },
    detail: {
      zh: '卡池卡片可以拖动排序。出货次数、连 UP / 连歪、不歪概率都在同一张卡上，还能导出一张分享图。',
      en: 'Drag the pool cards to reorder them. Pull counts, streaks, and rates all sit on one card, and you can export a share image.',
    },
  },
  {
    id: 'login',
    icon: '◉',
    accent: 'rose',
    name: { zh: '登录改进', en: 'Sign-in' },
    detail: {
      zh: '国服用手机号收验证码登录，国际服走网页登录。登录过期时会尽量自动续上，不必反复重新登录。',
      en: 'CN accounts sign in with a phone SMS code; overseas accounts use the web. Expired sessions try to renew themselves, so you need not sign in again.',
    },
  },
  {
    id: 'update',
    icon: '↻',
    accent: 'indigo',
    name: { zh: '静默更新', en: 'Silent update' },
    detail: {
      zh: '后台下载新版本，退出软件后自动安装。下次启动时会展示更新内容。',
      en: 'Downloads in the background and installs after you quit. Release notes appear the next time you start.',
    },
  },
  {
    id: 'redeem',
    icon: '#',
    accent: 'slate',
    name: { zh: '前瞻直播兑换码', en: 'Livestream codes' },
    detail: {
      zh: '国服版本前瞻直播期间，启动页展示官方兑换码与奖励，可单个或全部复制。未开播会提示，不必去直播间翻评论。',
      en: 'During CN version livestreams, official codes and rewards appear on the home page — copy one or all. A notice shows if the stream has not started, so you need not hunt comments.',
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
    tag: { zh: '参数 · URL · 账号', en: 'Args · URL · account' },
    caption: {
      zh: '一套设置的全部内容都在同一个窗口里：启动参数、启动链接、绑定账号。可以存多套，链接复制出来就能给脚本用。',
      en: 'Everything about one setup in a single window: launch options, the moonward:// link, and the bound account. Save several; copy the link for scripts.',
    },
    alt: {
      zh: 'Moonward 启动参数配置对话框：配置文件、命令行参数、URL 指令预览与绑定账号。',
      en: 'Moonward launch-profile dialog: saved profile, command-line args, URL preview, and bound account.',
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
      zh: '「常用命令行参数」内置 BetterGI 分组：勾选「启动 / 一条龙 / 调度器配置组」即自动拼成命令行；把 BetterGI.exe 设为自定义启动程序即可联动。',
      en: 'The common-args list has a BetterGI group: tick start, one-dragon, or scheduler groups to auto-build the command line; set BetterGI.exe as the custom launcher to hook it up.',
    },
    alt: {
      zh: 'Moonward「常用命令行参数」对话框中的 BetterGI 分组：启动、一条龙、调度器配置组等勾选项，下方为组合结果。',
      en: 'Moonward common command-line args dialog showing a BetterGI group: start, one-dragon, and scheduler-group checkboxes with a combined-result field below.',
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
    tag: { zh: '卡池 · 连 UP · 概率', en: 'Pools · streaks · rates' },
    caption: {
      zh: '卡池卡片可拖拽排序。连 UP / 连歪、不歪概率和出货次数排在一张卡上。',
      en: 'Drag pool cards to reorder. Streaks, rates, and pull counts sit on one card.',
    },
    alt: {
      zh: 'Moonward 抽卡记录页面：多张卡池统计卡片，含连 UP、概率与角色列表，其中一张正在拖拽。',
      en: 'Moonward gacha history: pool stat cards with streaks, rates, and character lists; one card is being dragged.',
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
      zh: '月历式奖励。打开自动签到后，软件启动约十秒就开始挨个领；旁边的问号里能直接跳到「开机自启」设置。',
      en: 'A monthly reward calendar. With auto check-in on, claims begin about 10 seconds after the app starts; the question mark beside it jumps to the start-at-login setting.',
    },
    alt: {
      zh: 'Moonward 签到面板：本月奖励月历、今日已签到状态与自动签到开关。',
      en: 'Moonward check-in panel: monthly reward calendar, today claimed, and the auto check-in switch.',
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
    zh: '核心思路：先把「玩哪个游戏 + 用什么启动参数 + 用哪个账号」存成一套设置，再用桌面图标或一条链接一键唤起。',
    en: 'The idea: save which game, which launch options, and which account as one setup, then open it from a desktop icon or a single link.',
  },
  steps: [
    {
      id: 'config',
      tag: { zh: '配置文件', en: 'Profile' },
      title: { zh: '创建启动配置', en: 'Create a launch profile' },
      desc: {
        zh: '在软件里给游戏建一套设置（软件里叫「配置文件」）：启动参数、绑定账号等。同一个游戏可以有好几套，互不干扰。',
        en: 'Build a setup for the game inside the app: launch options, bound account, and so on. A game can have several, and they do not interfere.',
      },
    },
    {
      id: 'entries',
      tag: { zh: '入口', en: 'Entry' },
      title: { zh: '选择唤起方式', en: 'Pick an entry' },
      desc: {
        zh: '一套设置可以落到两种日常入口上，效果一样：都按这套设置启动游戏。',
        en: 'A setup can become either of two everyday entry points; both do the same thing — launch the game with that setup.',
      },
      branches: [
        {
          id: 'shortcut',
          title: { zh: '桌面快捷方式', en: 'Desktop shortcut' },
          desc: {
            zh: '生成一个指向这套设置的桌面图标，双击即启动。勾选「关闭 UAC 提示」时，改为登记一条 Windows 计划任务来代跑：创建时同意一次管理员授权，之后双击不再弹窗。',
            en: 'Creates a desktop icon for that setup; double-click to launch. With the skip-UAC box ticked, it registers a Windows scheduled task to do the launching instead — approve once at creation, and later double-clicks never prompt.',
          },
        },
        {
          id: 'url',
          title: { zh: 'URL 协议', en: 'URL protocol' },
          desc: {
            zh: 'moonward:// 链接里带上游戏、设置与账号，从浏览器、任务计划或脚本打开都行。',
            en: 'A moonward:// link carrying game, setup, and account — open it from a browser, Task Scheduler, or a script.',
          },
        },
      ],
    },
    {
      id: 'resolve',
      tag: { zh: '解析', en: 'Resolve' },
      title: { zh: 'Moonward 读取配置', en: 'Moonward resolves the profile' },
      desc: {
        zh: '无论从哪个入口进来，启动器都会先读出这套设置，准备好启动参数与账号。免 UAC 的快捷方式也一样——计划任务最后打开的还是同一条链接。',
        en: 'Whichever entry you came from, the launcher reads that setup and prepares the options and account. Skip-UAC shortcuts are no exception — the scheduled task ends up opening the same link.',
      },
    },
    {
      id: 'game',
      tag: { zh: '游戏', en: 'Game' },
      title: { zh: '启动游戏', en: 'Start the game' },
      desc: {
        zh: '按这套设置里的参数拉起游戏本体。从桌面到进游戏，中间不必再点一遍启动按钮。如果软件此前没在运行，它会缩到任务栏右下角继续待命，接着管全局快捷键、游戏时长和自动签到。',
        en: 'Starts the game itself with that setup’s options — no extra click in the launcher. If Moonward was not already running, it stays tucked in the tray, still handling hotkeys, playtime, and auto check-in.',
      },
    },
  ],
  urlExample: 'moonward://startgame/{game_biz}?profile=…&uid=…',
}

/**
 * Auto check-in: per-game toggle → the batch that runs when Moonward starts → claim.
 */
export const checkInFlow = {
  title: {
    zh: '自动签到流程',
    en: 'Auto check-in flow',
  },
  lead: {
    zh: '每个游戏一个开关，改完下次打开软件生效。签到只在软件启动后跑一遍：开机自启缩到托盘算一次，用桌面图标开游戏顺手把软件留在托盘也算一次。',
    en: 'One switch per game, effective the next time you open Moonward. Check-in runs once per start — starting at login into the tray counts, and so does a desktop icon that leaves the app sitting there.',
  },
  steps: [
    {
      id: 'enable',
      tag: { zh: '开关', en: 'Toggle' },
      title: { zh: '按游戏开启', en: 'Enable per game' },
      desc: {
        zh: '签到面板上每个游戏一个开关，互不影响，改完下次打开软件生效。开关旁的问号里可以直接跳到「开机自启」设置，让软件在你登录 Windows 后自己缩到任务栏右下角待命。',
        en: 'Each game has its own switch on the check-in panel, and it takes effect the next time you open Moonward. The question mark beside it jumps to the start-at-login setting, so the app tucks itself into the tray once you sign in to Windows.',
      },
    },
    {
      id: 'start',
      tag: { zh: '启动', en: 'Start' },
      title: { zh: '软件启动后触发', en: 'Triggered when Moonward starts' },
      desc: {
        zh: '签到每次启动只跑一遍，日常两种开法都算，正好覆盖「一直挂着启动器」和「只点桌面图标」两类人。',
        en: 'Check-in runs once per start, and both everyday ways of starting count — covering people who leave the launcher open and people who only click the desktop icon.',
      },
      branches: [
        {
          id: 'app',
          title: { zh: '打开软件', en: 'Open Moonward' },
          desc: {
            zh: '手动打开主界面，或开机自启直接缩到任务栏右下角，都会在约十秒后开始签到。',
            en: 'Opening the main window, or starting at login straight into the tray, begins check-in about 10 seconds later.',
          },
        },
        {
          id: 'shortcut',
          title: { zh: '快捷方式开游戏', en: 'Launch by shortcut' },
          desc: {
            zh: '用桌面图标或链接开游戏时，如果软件此前没在运行，它会顺势留在托盘待命，同样跑这一遍签到。',
            en: 'When a desktop icon or link starts a game and Moonward was not running, it stays in the tray afterwards and runs the same round of check-ins.',
          },
        },
      ],
    },
    {
      id: 'claim',
      tag: { zh: '领取', en: 'Claim' },
      title: { zh: '查询并签到', en: 'Look up, then claim' },
      desc: {
        zh: '一个角色一个角色来：先查今天签没签，没签就领奖励，签过就跳过，两次请求之间随机等上几秒。失败后约十分钟内不再重试，免得反复打扰服务器。',
        en: "Role by role: check whether today is already claimed, claim it if not, skip it if so, waiting a few random seconds between requests. After a failure it waits about 10 minutes before trying again.",
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

