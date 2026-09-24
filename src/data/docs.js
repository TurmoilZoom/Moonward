import manifest from '../wiki/manifest.json'

/**
 * 「相关文档」= 项目 wiki 里已经写好的文档。
 *
 * 正文由 `scripts/sync-wiki.mjs` 同步到 `src/wiki/*.md`，构建时按篇拆包、
 * 点开哪篇才加载哪篇；这里只放展示用的标题、摘要与分组（wiki 正文是中文，
 * 英文界面下标题与摘要走译文，正文保持原样）。
 */

export const WIKI_BASE = 'https://github.com/TurmoilZoom/Moonward/wiki'

/** 分组，顺序即索引页的展示顺序。 */
export const docCategories = [
  {
    id: 'guide',
    name: { zh: '使用指南', en: 'Guides' },
    desc: {
      zh: '面向使用者：怎么配、怎么用。',
      en: 'For everyday use — how to set things up.',
    },
  },
  {
    id: 'internals',
    name: { zh: '实现原理', en: 'Internals' },
    desc: {
      zh: '面向好奇的人：这些功能在里面是怎么跑的。',
      en: 'For the curious — how these features actually work.',
    },
  },
  {
    id: 'protocol',
    name: { zh: '接口与凭证', en: 'APIs & tokens' },
    desc: {
      zh: '米游社 / HoYoLAB 的接口、登录凭证与抓包记录。',
      en: 'miHoYo / HoYoLAB endpoints, login credentials, and packet-capture notes.',
    },
  },
  {
    id: 'contrib',
    name: { zh: '参与开发', en: 'Contributing' },
    desc: {
      zh: '提 PR、发版本时用得上的约定。',
      en: 'Conventions for pull requests and releases.',
    },
  },
]

/** 展示信息；slug 对应 `src/wiki/<slug>.md`，并决定链接 `#/docs/<slug>`。 */
const entries = [
  {
    slug: 'howto-launch',
    category: 'guide',
    accent: 'teal',
    title: { zh: '游戏启动参数', en: 'Launch options' },
    summary: {
      zh: '一个游戏可以存多套「怎么启动」：命令行参数、自定义启动程序、绑定账号。这篇把每个字段讲清楚，并给出 BetterGI 等常见联动的写法。',
      en: 'A game can hold several “how to start” setups: command-line args, a custom launcher, a bound account. This page walks through every field, with ready-made recipes for BetterGI and friends.',
    },
  },
  {
    slug: 'auto-checkin',
    category: 'guide',
    accent: 'green',
    title: { zh: '每日签到与自动签到', en: 'Daily & auto check-in' },
    summary: {
      zh: '米游社 / HoYoLAB 的签到覆盖哪些游戏、开关在哪、软件常驻时怎么跨天再签，以及触发风控或 Cookie 失效时会发生什么。',
      en: 'Which games check-in covers, where the switches live, how it re-runs after midnight while the app sits in the tray, and what happens when a cookie expires or risk control kicks in.',
    },
  },
  {
    slug: 'image-quality',
    category: 'guide',
    accent: 'violet',
    title: { zh: '调整画质参数', en: 'Tuning graphics settings' },
    summary: {
      zh: '启动器直接改游戏画质配置的原理与字段表：每一项写到哪里、取值范围是什么、改坏了怎么恢复。',
      en: 'How the launcher edits in-game graphics settings, field by field: where each value is stored, what it accepts, and how to roll back a bad edit.',
    },
  },
  {
    slug: 'register-key',
    category: 'internals',
    accent: 'amber',
    title: { zh: '快捷方式与常驻实例', en: 'Shortcuts & the resident instance' },
    summary: {
      zh: '桌面图标、moonward:// 链接、命令行都会新开一个进程。这篇讲为什么不能用完就退，短进程与托盘实例之间怎么靠 WM_COPYDATA 交接。',
      en: 'A desktop icon, a moonward:// link, and the command line each spawn a new process. Why that process cannot just exit, and how it hands the game over to the tray instance via WM_COPYDATA.',
    },
  },
  {
    slug: 'use-velopack',
    category: 'internals',
    accent: 'indigo',
    title: { zh: '应用自更新（Velopack）', en: 'Self-update with Velopack' },
    summary: {
      zh: '检查、下载、安装三段流程，增量包怎么合成，安装版与便携版的目录差别，以及更新失败时的回退路径。',
      en: 'Check, download, install — how delta packages are stitched back together, how installed and portable layouts differ, and what happens when an update fails.',
    },
  },
  {
    slug: 'memory-usage',
    category: 'internals',
    accent: 'blue',
    title: { zh: '管理运行内存', en: 'Managing memory' },
    summary: {
      zh: 'WinUI 3 应用的占用大头不在托管堆，而在 NT 堆、解码缓冲和 Direct3D 表面。这篇记录测量方法、各处占用的来源与已经做过的削减。',
      en: 'In a WinUI 3 app the bulk of memory sits outside the managed heap — in the NT heap, decode buffers, and Direct3D surfaces. How it was measured, where it goes, and what has been trimmed.',
    },
  },
  {
    slug: 'understand-winui3',
    category: 'internals',
    accent: 'slate',
    title: { zh: 'NativeAOT 与 R2R', en: 'NativeAOT and R2R' },
    summary: {
      zh: '从 C# 到 CPU 的几条路：IL、JIT、R2R、NativeAOT 分别解决什么问题，裁剪和反射为什么互相为难，以及本项目最后选了哪套配置。',
      en: 'The routes from C# to the CPU — what IL, JIT, R2R, and NativeAOT each solve, why trimming and reflection fight each other, and which combination this project settled on.',
    },
  },
  {
    slug: 'cloud-game',
    category: 'internals',
    accent: 'cyan',
    title: { zh: '云游戏每日免费时长', en: 'Cloud-game free hours' },
    summary: {
      zh: '云·原神 / 云·绝区零没有签到接口，免费时长是「每天登录一次」发的。这篇讲清它真正调了什么接口、用哪套凭证。',
      en: 'Cloud Genshin and Cloud ZZZ have no check-in endpoint — the free hours are granted by logging in once a day. What is actually called, and with which credentials.',
    },
  },
  {
    slug: 'login-token',
    category: 'protocol',
    accent: 'rose',
    title: { zh: '登录里的各种 token', en: 'The many login tokens' },
    summary: {
      zh: '短信验证码、网页登录、扫码各自拿到的是哪个根凭证，stoken / ltoken / cookie_token 之间怎么换，失效时哪一条还能救。',
      en: 'Which root credential each sign-in path yields, how stoken, ltoken, and cookie_token convert between one another, and which one can still rescue an expired session.',
    },
  },
  {
    slug: 'api-reverse',
    category: 'protocol',
    accent: 'amber',
    title: { zh: '逆向米游社接口', en: 'Reversing the miHoYo API' },
    summary: {
      zh: '安卓模拟器上抓米游社的完整流程：Magisk 装系统证书、绕过 Pinning、找到请求签名的算法。',
      en: 'The full route to capturing miHoYo traffic on an Android emulator: a system certificate through Magisk, getting past pinning, and finding the request-signing algorithm.',
    },
  },
  {
    slug: 'mitm-windows',
    category: 'protocol',
    accent: 'cyan',
    title: { zh: '抓 Windows 客户端的包', en: 'Capturing a Windows client' },
    summary: {
      zh: '安卓那套对 PC 客户端不适用。以云游戏客户端为例，记录 Windows 桌面端的代理、证书与筛选做法。',
      en: 'The Android recipe does not transfer to PC. Using the cloud-game client as the example: proxying, certificates, and filtering on the Windows desktop.',
    },
  },
  {
    slug: 'github-pr',
    category: 'contrib',
    accent: 'teal',
    title: { zh: 'PR 的三种合并方式', en: 'Three ways to merge a PR' },
    summary: {
      zh: 'Merge、Squash、Rebase 分别在历史上留下什么，本仓库在什么场景下用哪种。',
      en: 'What merge, squash, and rebase each leave behind in history — and which one this repository uses when.',
    },
  },
  {
    slug: 'howto-release',
    category: 'contrib',
    accent: 'amber',
    title: { zh: '发布新版本', en: 'Cutting a release' },
    summary: {
      zh: '一张图说明从打 tag 到 Releases 上架的流水线。',
      en: 'One diagram covering the pipeline from tagging to a published release.',
    },
  },
]

/** 索引页/文章页共用的文档列表，已并入同步脚本记录的更新日期与篇幅。 */
export const docs = entries.map((entry) => {
  const meta = manifest[entry.slug] || {}
  return {
    ...entry,
    page: meta.page || entry.slug,
    updated: meta.updated || '',
    chars: meta.chars || 0,
    wikiUrl: `${WIKI_BASE}/${meta.page || entry.slug}`,
  }
})

/** wiki 页面名 → 站内 slug，用于把文档之间的互链留在站内。 */
export const pageToSlug = Object.fromEntries(docs.map((d) => [d.page, d.slug]))

/** @param {string} slug */
export function findDoc(slug) {
  return docs.find((d) => d.slug === slug) || null
}

/** 粗略阅读时长：技术文里表格与代码占不少篇幅，按 500 字符/分钟估。 */
export function readingMinutes(chars) {
  return Math.max(1, Math.round(chars / 500))
}
