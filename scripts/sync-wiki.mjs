#!/usr/bin/env node
/**
 * 把 GitHub Wiki 的文档同步到 `src/wiki/`，供「相关文档」页面在构建时打包。
 *
 * 为什么不在运行时抓 wiki：
 * - GitHub Pages 站点直接请求 raw.githubusercontent.com 在部分网络下不可达；
 * - CI 只跑 `npm ci && npm run build`，不联网取内容。
 * 所以文档作为源码提交，更新 wiki 后手动跑一次：
 *
 *     node scripts/sync-wiki.mjs
 *
 * 收录哪些页面、页面标题与摘要写在 `src/data/docs.js`；本脚本只负责
 * 拉取正文、清洗 wiki 编辑器留下的痕迹，并生成 `src/wiki/manifest.json`
 * （记录每篇最后修改日期）。
 */

import { execFileSync } from 'node:child_process'
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const OUT_DIR = join(ROOT, 'src', 'wiki')
const WIKI_REMOTE = 'https://github.com/TurmoilZoom/Moonward.wiki.git'

/** 页面名（wiki 文件名去掉 .md）→ 站内 slug。顺序与分组见 src/data/docs.js。 */
const PAGES = {
  'HOWTO-LAUNCH': 'howto-launch',
  'AUTO-CHECKIN': 'auto-checkin',
  'IMAGE-QUALITY': 'image-quality',
  'REGISTER-KEY': 'register-key',
  'USE-VELOPACK': 'use-velopack',
  'MEMORY-USAGE': 'memory-usage',
  'UNDERSTAND-WINUI3': 'understand-winui3',
  'CLOUD-GAME': 'cloud-game',
  'LOGIN-TOKEN': 'login-token',
  'API-REVERSE': 'api-reverse',
  'MITM-WINDOWS': 'mitm-windows',
  'GitHub-PR': 'github-pr',
  'HOWTO-RELEASE': 'howto-release',
}

function git(args, cwd) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] })
}

/**
 * 清洗 wiki 正文。
 * @param {string} raw
 * @param {string} page 页面名，用于日志
 */
function clean(raw, page) {
  let md = raw.replace(/\r\n/g, '\n')

  // 有的页面是从 Typora 粘贴的，正文前挂了一大段带内联样式的 HTML 导出。
  // 真正的 Markdown 从最后一个 </html> 之后开始。
  const htmlEnd = md.lastIndexOf('</html>')
  if (md.trimStart().startsWith('<html') && htmlEnd !== -1) {
    md = md.slice(htmlEnd + '</html>'.length)
    console.log(`  · ${page}: 去掉正文前的 HTML 粘贴块`)
  }

  // wiki 目录里常见的嵌套链接 [[文字](编辑页链接)](#锚点) → [文字](#锚点)
  md = md.replace(/\[\[([^\][]+)\]\(([^()\s]+)\)\]\(([^()\s]+)\)/g, '[$1]($3)')
  // 指向 wiki 编辑页的链接还原成阅读页
  md = md.replace(/(\/wiki\/[^)\s]*?)\/_edit(?=[#)\s])/g, '$1')

  return `${md.trim()}\n`
}

function main() {
  const work = mkdtempSync(join(tmpdir(), 'moonward-wiki-'))
  try {
    console.log(`克隆 ${WIKI_REMOTE} …`)
    git(['clone', '--quiet', WIKI_REMOTE, work])

    const present = new Set(
      readdirSync(work)
        .filter((f) => f.endsWith('.md'))
        .map((f) => f.slice(0, -3)),
    )

    /** @type {Record<string, { page: string, updated: string, chars: number }>} */
    const manifest = {}

    for (const [page, slug] of Object.entries(PAGES)) {
      if (!present.has(page)) {
        console.warn(`  ! wiki 里找不到页面 ${page}，跳过`)
        continue
      }
      const md = clean(readFileSync(join(work, `${page}.md`), 'utf8'), page)
      writeFileSync(join(OUT_DIR, `${slug}.md`), md)
      const updated = git(['log', '-1', '--format=%ad', '--date=short', '--', `${page}.md`], work).trim()
      manifest[slug] = { page, updated, chars: md.length }
      console.log(`  ✓ ${page} → src/wiki/${slug}.md（${updated}，${md.length} 字符）`)
    }

    for (const page of present) {
      if (page.startsWith('_') || page === 'Home' || PAGES[page]) continue
      console.log(`  · wiki 页面 ${page} 未收录（如需上站，加进本脚本的 PAGES 和 src/data/docs.js）`)
    }

    writeFileSync(join(OUT_DIR, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`)
    console.log(`\n写入 src/wiki/manifest.json（${Object.keys(manifest).length} 篇）`)
  } finally {
    rmSync(work, { recursive: true, force: true })
  }
}

main()
