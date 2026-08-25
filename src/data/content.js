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
      zh: '同一游戏可保存多套参数与绑定账号，数量不限。切换账号、改启动参数不必每次重填。',
      en: 'Save unlimited profiles per game (args + bound account). Switch accounts without retyping.',
    },
  },
  {
    id: 'shortcut',
    icon: '⧉',
    accent: 'amber',
    name: { zh: '桌面快捷方式', en: 'Desktop shortcuts' },
    detail: {
      zh: '把某套配置做成桌面图标。双击即可按该配置启动游戏，不必先打开启动器界面。',
      en: 'Pin a profile to the desktop. Double-click to launch with that profile — no launcher UI first.',
    },
  },
  {
    id: 'uac',
    icon: '⇧',
    accent: 'cyan',
    name: { zh: '关闭 UAC 提示', en: 'Skip UAC prompt' },
    detail: {
      zh: '创建游戏快捷方式时可勾选。授权一次后，之后双击启动不再弹出系统 UAC。不需要时，可在设置里清理对应任务。',
      en: 'Optional when creating a game shortcut. Approve once; later double-clicks skip the Windows UAC prompt. Unused tasks can be cleaned up in Settings.',
    },
  },
  {
    id: 'url',
    icon: '↗',
    accent: 'blue',
    name: { zh: 'URL 协议', en: 'URL protocol' },
    detail: {
      zh: '使用 moonward:// 指定游戏、配置与账号并直接启动。可嵌入脚本、网页或其它工具。',
      en: 'moonward:// launches a game with a chosen profile and account — usable from scripts or other apps.',
    },
  },
  {
    id: 'checkin',
    icon: '✓',
    accent: 'green',
    name: { zh: '自动签到', en: 'Auto check-in' },
    detail: {
      zh: '每个游戏独立开关。软件启动约十秒后依次签到；用绑定账号的快捷方式 / URL / 命令行开游戏时，也会单独签一次。',
      en: 'Per-game toggle. About 10 seconds after Moonward starts, enabled games check in in turn. Launching via shortcut, URL, or CLI also checks in that bound account.',
    },
  },
  {
    id: 'gacha',
    icon: '◈',
    accent: 'violet',
    name: { zh: '抽卡记录', en: 'Gacha history' },
    detail: {
      zh: '卡池统计可拖拽排序，列表支持拖拽滚动；连 UP / 连歪、不歪概率等一目了然，可导出分享图。',
      en: 'Draggable pool stats, streaks and rates at a glance, exportable share images.',
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
      zh: '配置文件、命令行参数、URL 指令和绑定账号在同一个对话框里。可保存多套，复制 URL 给脚本用。',
      en: 'Profile, launch args, the moonward:// URL, and bound account in one dialog. Save several; copy the URL for scripts.',
    },
    alt: {
      zh: 'Moonward 启动参数配置对话框：配置文件、命令行参数、URL 指令预览与绑定账号。',
      en: 'Moonward launch-profile dialog: saved profile, command-line args, URL preview, and bound account.',
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
      zh: '月历式奖励。打开自动签到后，启动软件或用绑定账号开游戏都会签。',
      en: 'Monthly reward calendar. With auto check-in on, it runs when Moonward starts or you launch a bound account.',
    },
    alt: {
      zh: 'Moonward 签到面板：本月奖励月历、今日已签到状态与自动签到开关。',
      en: 'Moonward check-in panel: monthly reward calendar, today claimed, and the auto check-in switch.',
    },
  },
]

/**
 * Launch pipeline: config → entry points → resolve → optional check-in → game.
 * Used by the flow diagram section.
 */
export const launchFlow = {
  title: {
    zh: '快捷启动流程',
    en: 'Quick-launch flow',
  },
  lead: {
    zh: '核心思路：先把「游戏 + 启动参数 + 账号」存成配置，再通过快捷方式或 URL 一键唤起。启动时可选自动签到。',
    en: 'Save game + args + account as a profile, then open it from a shortcut or URL. Optional check-in runs on launch.',
  },
  steps: [
    {
      id: 'config',
      tag: { zh: '配置文件', en: 'Profile' },
      title: { zh: '创建启动配置', en: 'Create a launch profile' },
      desc: {
        zh: '在应用中为游戏建立配置：启动参数、绑定账号等。同一游戏可有多套，互不干扰。',
        en: 'In-app profile: launch args, bound account, and more. Multiple profiles per game.',
      },
    },
    {
      id: 'entries',
      tag: { zh: '入口', en: 'Entry' },
      title: { zh: '选择唤起方式', en: 'Pick an entry' },
      desc: {
        zh: '配置可落到两种日常入口上，效果等价：都按该配置启动。',
        en: 'Two everyday entry points, same result: launch with that profile.',
      },
      branches: [
        {
          id: 'shortcut',
          title: { zh: '桌面快捷方式', en: 'Desktop shortcut' },
          desc: {
            zh: '生成指向该配置的桌面图标，双击即启动。可勾选「关闭 UAC 提示」：创建时授权一次，之后不再弹窗。',
            en: 'Create a desktop icon for the profile. Optionally skip UAC: approve once when creating, then launch without a prompt.',
          },
        },
        {
          id: 'url',
          title: { zh: 'URL 协议', en: 'URL protocol' },
          desc: {
            zh: 'moonward:// 带上游戏、配置与账号，从浏览器或脚本打开。',
            en: 'moonward:// with game, profile, and account — from browser or scripts.',
          },
        },
      ],
    },
    {
      id: 'resolve',
      tag: { zh: '解析', en: 'Resolve' },
      title: { zh: 'Moonward 读取配置', en: 'Moonward resolves the profile' },
      desc: {
        zh: '无论从哪种入口进入，启动器都会解析目标配置，准备参数与账号上下文。',
        en: 'Either entry path ends here: the launcher loads the profile and account context.',
      },
    },
    {
      id: 'checkin',
      tag: { zh: '自动签到', en: 'Check-in' },
      title: { zh: '可选：账号签到', en: 'Optional: account check-in' },
      desc: {
        zh: '若已开启，用快捷方式 / URL / 命令行启动时会给绑定账号签一次。软件自身启动后还会按游戏批量签到（见下方流程）。',
        en: 'If enabled, launching via shortcut, URL, or CLI checks in that bound account. Moonward also runs a per-game batch after it starts (see the flow below).',
      },
    },
    {
      id: 'game',
      tag: { zh: '游戏', en: 'Game' },
      title: { zh: '启动游戏', en: 'Start the game' },
      desc: {
        zh: '按配置中的参数拉起对应客户端。从桌面到进游戏，中间不必再点一遍启动按钮。',
        en: 'Starts the client with profile args — no extra click in the launcher UI.',
      },
    },
  ],
  urlExample: 'moonward://launch?game=…&profile=…&account=…',
}

/**
 * Auto check-in: per-game toggle → startup batch and/or launch-time one-off → claim.
 */
export const checkInFlow = {
  title: {
    zh: '自动签到流程',
    en: 'Auto check-in flow',
  },
  lead: {
    zh: '每个游戏单独开关。日常有两条路：打开 Moonward 后自动挨个签；用绑定账号的快捷方式 / URL / 命令行开游戏时再签一次。',
    en: 'Each game has its own toggle. Two everyday paths: a batch after Moonward starts, and a one-off when you launch a bound account.',
  },
  steps: [
    {
      id: 'enable',
      tag: { zh: '开关', en: 'Toggle' },
      title: { zh: '按游戏开启', en: 'Enable per game' },
      desc: {
        zh: '签到面板上每个游戏独立开关，互不影响。打开后下次启动软件生效。可同时打开开机自启，不必每天点开启动器。',
        en: 'Each game has its own switch. It takes effect the next time Moonward starts. Optional start-at-login so you need not open the app by hand.',
      },
    },
    {
      id: 'paths',
      tag: { zh: '触发', en: 'Triggers' },
      title: { zh: '两条签到路径', en: 'Two check-in paths' },
      desc: {
        zh: '批量签到与开游戏顺带签到互不替代，覆盖「挂着启动器」和「只点快捷方式」两种用法。',
        en: 'The batch and the launch-time one-off complement each other — leaving Moonward open, or only using a shortcut.',
      },
      branches: [
        {
          id: 'batch',
          title: { zh: '启动后批量', en: 'Batch after start' },
          desc: {
            zh: 'Moonward 启动约十秒后，对已开启的游戏按角色依次请求，间隔数秒，避开启动高峰。',
            en: 'About 10 seconds after Moonward starts, enabled games check in one role at a time, a few seconds apart.',
          },
        },
        {
          id: 'launch',
          title: { zh: '开游戏顺带签', en: 'On game launch' },
          desc: {
            zh: '用绑定账号的快捷方式、URL 或命令行启动时，只给该账号静默签一次，不打断界面。',
            en: 'A shortcut, URL, or CLI launch with a bound account silently checks in that account only.',
          },
        },
      ],
    },
    {
      id: 'claim',
      tag: { zh: '领取', en: 'Claim' },
      title: { zh: '查询并签到', en: 'Look up, then claim' },
      desc: {
        zh: '先查今日是否已签；未签则领取奖励，已签则跳过。失败约十分钟内不再重试，避免反复请求。',
        en: "Looks up today's status first. Claims if needed, skips if already done. Failures cool down for about 10 minutes.",
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

