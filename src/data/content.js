export const links = {
  download: 'https://github.com/TurmoilZoom/Moonward/releases/latest',
  github: 'https://github.com/TurmoilZoom/Moonward',
  upstream: 'https://github.com/Scighost/Starward',
  issues: 'https://github.com/TurmoilZoom/Moonward/issues',
  license: 'https://github.com/TurmoilZoom/Moonward/blob/rebase/develop/LICENSE',
  codeSigningPolicy:
    'https://github.com/TurmoilZoom/Moonward/blob/rebase/develop/docs/CodeSigningPolicy.md',
  codeSigningPolicyZh:
    'https://github.com/TurmoilZoom/Moonward/blob/rebase/develop/docs/CodeSigningPolicy.zh-CN.md',
  privacy: 'https://github.com/TurmoilZoom/Moonward/blob/rebase/develop/docs/Privacy.md',
  privacyZh: 'https://github.com/TurmoilZoom/Moonward/blob/rebase/develop/docs/Privacy.zh-CN.md',
  signpath: 'https://about.signpath.io',
  signpathFoundation: 'https://signpath.org',
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
      zh: '米游社 / HoYoLAB 每日签到。通过快捷方式、URL 或命令行启动时，可顺带为绑定账号签到。',
      en: 'miHoYo / HoYoLAB daily check-in. Launching via shortcut, URL, or CLI can check in for that account.',
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
      zh: '国服短信验证码；国际服网页登录。Cookie 失效时可用 stoken 静默换票（最多重试一次）。',
      en: 'CN SMS login; OS web login. Expired CN cookies can refresh once via stoken.',
    },
  },
  {
    id: 'zzz',
    icon: '◇',
    accent: 'cyan',
    name: { zh: '绝区零元数据', en: 'ZZZ metadata' },
    detail: {
      zh: '物品图标与多语言名称从 metadata 分支拉取；也可用战绩 Cookie 从养成指南更新。',
      en: 'Icons and names from the metadata branch; refreshable via record cookie.',
    },
  },
  {
    id: 'ui',
    icon: '▣',
    accent: 'slate',
    name: { zh: '界面体验', en: 'UI polish' },
    detail: {
      zh: '快速启动菜单、自定义背景、工具栏图钉；WinUI 亚克力与 Composition 动效。',
      en: 'Quick-launch, custom background, pinable toolbar; acrylic and Composition motion.',
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
            zh: '生成指向该配置的桌面图标，双击即启动。',
            en: 'Create a desktop icon for the profile; double-click to launch.',
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
        zh: '若开启自动签到，启动游戏前会对绑定账号执行米游社 / HoYoLAB 签到（命令行启动同样适用）。',
        en: 'If enabled, checks in the bound account on miHoYo / HoYoLAB before launch (CLI too).',
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

/** SignPath OSS free code signing — required "Code signing policy" for homepage / download pages. */
export const codeSigning = {
  title: { zh: '代码签名策略', en: 'Code signing policy' },
  kicker: { zh: '安全与分发', en: 'Security' },
  // Required exact English attribution (SignPath Foundation terms).
  attribution:
    'Free code signing provided by SignPath.io, certificate by SignPath Foundation',
  lead: {
    zh: 'Windows 发行包通过 SignPath 进行代码签名。私钥由 SignPath 托管于 HSM，本项目不保存私钥。请仅从官方 GitHub Releases 下载。',
    en: 'Windows release packages are code-signed via SignPath. The private key is held by SignPath on an HSM; this project does not store it. Download only from official GitHub Releases.',
  },
  rolesHeading: { zh: '团队角色', en: 'Team roles' },
  roles: [
    {
      role: { zh: 'Authors（提交者）', en: 'Authors (committers)' },
      members: '@TurmoilZoom',
    },
    {
      role: { zh: 'Reviewers（审查者）', en: 'Reviewers' },
      members: '@TurmoilZoom',
    },
    {
      role: { zh: 'Approvers（签名批准）', en: 'Approvers (signing requests)' },
      members: '@TurmoilZoom',
    },
  ],
  maintainerUrl: 'https://github.com/TurmoilZoom',
  fullPolicy: { zh: '完整策略文档', en: 'Full policy' },
  privacy: { zh: '隐私策略', en: 'Privacy policy' },
}
