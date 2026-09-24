/**
 * Small, XSS-safe renderer for GitHub release-note markdown
 * (headings, lists, links, bold, inline code, hr).
 */

/**
 * @param {string} s
 */
export function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/**
 * @param {string} s already escaped
 */
function inline(s) {
  s = s.replace(/`([^`]+)`/g, '<code>$1</code>')
  s = s.replace(
    /\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>',
  )
  s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
  s = s.replace(/(^|[^*])\*([^*]+)\*(?!\*)/g, '$1<em>$2</em>')
  return s
}

/**
 * @param {string | null | undefined} md
 * @returns {string} HTML
 */
export function renderReleaseMarkdown(md) {
  if (!md || !String(md).trim()) return ''

  const lines = String(md).replace(/\r\n/g, '\n').split('\n')
  /** @type {string[]} */
  const html = []
  /** @type {'ul' | 'ol' | null} */
  let list = null

  const closeList = () => {
    if (list) {
      html.push(`</${list}>`)
      list = null
    }
  }

  for (const raw of lines) {
    const line = raw.replace(/\s+$/, '')

    if (/^\s*---+\s*$/.test(line)) {
      closeList()
      html.push('<hr />')
      continue
    }

    const heading = line.match(/^(#{1,4})\s+(.+)$/)
    if (heading) {
      closeList()
      const n = heading[1].length
      html.push(`<h${n}>${inline(escapeHtml(heading[2]))}</h${n}>`)
      continue
    }

    const ul = line.match(/^\s*[-*+]\s+(.+)$/)
    if (ul) {
      if (list !== 'ul') {
        closeList()
        html.push('<ul>')
        list = 'ul'
      }
      html.push(`<li>${inline(escapeHtml(ul[1]))}</li>`)
      continue
    }

    const ol = line.match(/^\s*\d+\.\s+(.+)$/)
    if (ol) {
      if (list !== 'ol') {
        closeList()
        html.push('<ol>')
        list = 'ol'
      }
      html.push(`<li>${inline(escapeHtml(ol[1]))}</li>`)
      continue
    }

    if (!line.trim()) {
      closeList()
      continue
    }

    closeList()
    html.push(`<p>${inline(escapeHtml(line))}</p>`)
  }

  closeList()
  return html.join('\n')
}

/* ————————————————————————————————————————————————————————————
 * Wiki 文档渲染：在发布说明那套基础上补齐表格、代码块、引用、
 * 嵌套列表与标题锚点。内容来自本仓库 wiki（构建期打包），仍按
 * 「先转义、再拼标签」处理，不放行任意 HTML。
 * ———————————————————————————————————————————————————————————— */

/**
 * GitHub 风格的标题锚点：转小写，去标点，空格转连字符（保留中日韩字符）。
 * @param {string} text 已去掉行内标记的纯文本
 */
export function slugifyHeading(text) {
  return String(text)
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{N}\s-]/gu, '')
    .replace(/\s+/g, '-')
}

/** 取标题纯文本用于生成锚点与目录。 */
function plainText(md) {
  return String(md)
    .replace(/`([^`]*)`/g, '$1')
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/(\*\*|__)(.*?)\1/g, '$2')
    .replace(/(\*|_)(.*?)\1/g, '$2')
    .replace(/~~(.*?)~~/g, '$1')
    .trim()
}

/** 硬换行占位符：先占位，等行内标记转义完再换成 <br />。 */
const HARD_BREAK = '\u0000b\u0000'

/** 段内换行：中日韩字符之间直接相接，其余按空格拼。 */
const CJK = /[⺀-鿿豈-﫿＀-￯]/

function joinSoft(prev, next) {
  if (!prev) return next
  const a = prev.slice(-1)
  const b = next.slice(0, 1)
  return CJK.test(a) && CJK.test(b) ? prev + next : `${prev} ${next}`
}

/**
 * @typedef {object} DocLinkContext
 * @property {(href: string) => { href: string, external: boolean }} resolveLink
 */

/** 行内标记：代码优先摘出来，避免里面的 * _ [ 被当成标记。 */
function inlineDoc(src, ctx) {
  /** @type {string[]} */
  const codes = []
  let s = String(src).replace(/`+([^`]|[^`][\s\S]*?[^`])`+/g, (_m, code) => {
    codes.push(code)
    return `\u0000c${codes.length - 1}\u0000`
  })

  s = escapeHtml(s)

  // 图片 ![alt](src "title")
  s = s.replace(/!\[([^\]]*)\]\(([^)\s]+)(?:\s+&quot;[^)]*&quot;)?\)/g, (_m, alt, src2) => {
    const link = ctx.resolveLink(src2)
    return `<img src="${link.href}" alt="${alt}" loading="lazy" referrerpolicy="no-referrer" />`
  })

  // 链接 [text](href)
  s = s.replace(/\[([^\]]+)\]\(([^)\s]+)(?:\s+&quot;[^)]*&quot;)?\)/g, (_m, text, href) => {
    const link = ctx.resolveLink(href)
    const rel = link.external ? ' target="_blank" rel="noopener noreferrer"' : ''
    return `<a href="${link.href}"${rel}>${text}</a>`
  })

  s = s.replace(/~~([^~]+)~~/g, '<del>$1</del>')
  s = s.replace(/(\*\*|__)(?=\S)([\s\S]*?\S)\1/g, '<strong>$2</strong>')
  s = s.replace(/(^|[^*\w])\*(?=\S)([^*]*?\S)\*(?!\*)/g, '$1<em>$2</em>')

  return s.replace(/\u0000c(\d+)\u0000/g, (_m, n) => `<code>${escapeHtml(codes[Number(n)])}</code>`)
}

const FENCE_RE = /^(\s*)(```+|~~~+)\s*([^`\s]*)/
const HEADING_RE = /^(#{1,6})\s+(.*)$/
const HR_RE = /^\s{0,3}([-*_])(?:\s*\1){2,}\s*$/
const LIST_RE = /^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$/
const TABLE_DELIM_RE = /^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$/

function splitRow(line) {
  return line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split(/(?<!\\)\|/)
    .map((c) => c.trim().replace(/\\\|/g, '|'))
}

/** 只放行图片；其余原始 HTML 当作文本转义输出。 */
function renderHtmlBlock(block) {
  const imgs = block.match(/<img\b[^>]*>/gi)
  if (!imgs) return `<p>${escapeHtml(block.trim())}</p>`
  return imgs
    .map((tag) => {
      const src = (tag.match(/\bsrc\s*=\s*"([^"]*)"/i) || tag.match(/\bsrc\s*=\s*'([^']*)'/i) || [])[1]
      if (!src) return ''
      const alt = (tag.match(/\balt\s*=\s*"([^"]*)"/i) || [])[1] || ''
      return (
        `<figure class="md-figure"><img src="${escapeHtml(src)}" alt="${escapeHtml(alt)}"` +
        ' loading="lazy" referrerpolicy="no-referrer" /></figure>'
      )
    })
    .join('\n')
}

/**
 * 把一段行渲染成块级 HTML。列表项、引用会递归调用自身。
 * @param {string[]} lines
 * @param {{ headings: { id: string, text: string, level: number }[], seen: Map<string, number>, resolveLink: DocLinkContext['resolveLink'], top: boolean }} ctx
 */
function renderBlocks(lines, ctx) {
  /** @type {string[]} */
  const out = []
  let i = 0

  while (i < lines.length) {
    const line = lines[i]

    if (!line.trim()) {
      i += 1
      continue
    }

    const fence = line.match(FENCE_RE)
    if (fence) {
      const marker = fence[2][0].repeat(3)
      const lang = fence[3] || ''
      const body = []
      i += 1
      while (i < lines.length && !new RegExp(`^\\s*${marker}`).test(lines[i])) {
        body.push(lines[i])
        i += 1
      }
      i += 1
      const langAttr = lang ? ` data-lang="${escapeHtml(lang)}"` : ''
      out.push(`<pre class="md-code"${langAttr}><code>${escapeHtml(body.join('\n'))}</code></pre>`)
      continue
    }

    if (HR_RE.test(line)) {
      out.push('<hr />')
      i += 1
      continue
    }

    const heading = line.match(HEADING_RE)
    if (heading) {
      const level = heading[1].length
      const raw = heading[2].replace(/\s+#+\s*$/, '')
      const text = plainText(raw)
      let id = slugifyHeading(text)
      const hits = ctx.seen.get(id) || 0
      ctx.seen.set(id, hits + 1)
      if (hits) id = `${id}-${hits}`
      if (ctx.top && level <= 3) ctx.headings.push({ id, text, level })
      const anchor = ctx.top
        ? `<a class="md-anchor" href="${ctx.resolveLink(`#${id}`).href}" aria-hidden="true" tabindex="-1">#</a>`
        : ''
      out.push(`<h${level} id="doc-${id}">${inlineDoc(raw, ctx)}${anchor}</h${level}>`)
      i += 1
      continue
    }

    if (line.startsWith('>')) {
      const body = []
      while (i < lines.length && lines[i].startsWith('>')) {
        body.push(lines[i].replace(/^>\s?/, ''))
        i += 1
      }
      out.push(`<blockquote>${renderBlocks(body, { ...ctx, top: false })}</blockquote>`)
      continue
    }

    if (line.includes('|') && i + 1 < lines.length && TABLE_DELIM_RE.test(lines[i + 1])) {
      const head = splitRow(line)
      const align = splitRow(lines[i + 1]).map((c) => {
        const left = c.startsWith(':')
        const right = c.endsWith(':')
        if (left && right) return 'center'
        if (right) return 'right'
        return left ? 'left' : ''
      })
      i += 2
      const rows = []
      while (i < lines.length && lines[i].trim() && lines[i].includes('|')) {
        rows.push(splitRow(lines[i]))
        i += 1
      }
      const cell = (tag, text, n) => {
        const a = align[n] ? ` style="text-align:${align[n]}"` : ''
        return `<${tag}${a}>${inlineDoc(text ?? '', ctx)}</${tag}>`
      }
      out.push(
        '<div class="md-table-wrap"><table>' +
          `<thead><tr>${head.map((c, n) => cell('th', c, n)).join('')}</tr></thead>` +
          `<tbody>${rows
            .map((r) => `<tr>${head.map((_c, n) => cell('td', r[n], n)).join('')}</tr>`)
            .join('')}</tbody>` +
          '</table></div>',
      )
      continue
    }

    const listStart = line.match(LIST_RE)
    if (listStart) {
      const ordered = /\d/.test(listStart[2][0])
      const baseIndent = listStart[1].length
      /** @type {string[][]} */
      const items = []
      let loose = false
      let pendingBlank = false

      while (i < lines.length) {
        const cur = lines[i]
        if (!cur.trim()) {
          pendingBlank = true
          i += 1
          continue
        }
        const m = cur.match(LIST_RE)
        const indent = cur.match(/^\s*/)[0].length
        if (m && indent <= baseIndent) {
          if (/\d/.test(m[2][0]) !== ordered && indent === baseIndent) break
          if (pendingBlank && items.length) loose = true
          items.push([m[3]])
          pendingBlank = false
          i += 1
          continue
        }
        if (!items.length) break
        if (indent > baseIndent) {
          if (pendingBlank) items[items.length - 1].push('')
          items[items.length - 1].push(cur.slice(baseIndent + 1))
          pendingBlank = false
          i += 1
          continue
        }
        if (pendingBlank) break
        items[items.length - 1].push(cur)
        i += 1
      }

      const tag = ordered ? 'ol' : 'ul'
      const start = ordered ? parseInt(listStart[2], 10) : 1
      const startAttr = ordered && start !== 1 ? ` start="${start}"` : ''
      // 紧凑列表里，条目开头那段不包 <p>，免得有子列表的条目比兄弟条目松一截
      const body = items
        .map((item) => {
          const inner = renderBlocks(item, { ...ctx, top: false })
          return `<li>${loose ? inner : inner.replace(/^<p>([\s\S]*?)<\/p>/, '$1')}</li>`
        })
        .join('')
      out.push(`<${tag}${startAttr}>${body}</${tag}>`)
      continue
    }

    if (line.trimStart().startsWith('<')) {
      const body = []
      while (i < lines.length && lines[i].trim()) {
        body.push(lines[i])
        i += 1
      }
      out.push(renderHtmlBlock(body.join('\n')))
      continue
    }

    const paraStart = i
    /** @type {{ text: string, br: boolean }[]} */
    const parts = []
    while (i < lines.length && lines[i].trim()) {
      const cur = lines[i]
      const startsBlock =
        HEADING_RE.test(cur) ||
        HR_RE.test(cur) ||
        FENCE_RE.test(cur) ||
        LIST_RE.test(cur) ||
        cur.startsWith('>') ||
        (cur.includes('|') && i + 1 < lines.length && TABLE_DELIM_RE.test(lines[i + 1]))
      if (i > paraStart && startsBlock) break
      // 行尾两个空格或一个反斜杠 = 硬换行；其余换行按 GFM 当空格
      parts.push({ text: cur.trim(), br: /(?: {2,}|\\)$/.test(cur) })
      i += 1
    }
    let para = ''
    parts.forEach((part, n) => {
      if (n === 0) para = part.text
      else if (parts[n - 1].br) para += `${HARD_BREAK}${part.text}`
      else para = joinSoft(para, part.text)
    })
    out.push(`<p>${inlineDoc(para, ctx).split(HARD_BREAK).join('<br />')}</p>`)
  }

  return out.join('\n')
}

/**
 * 渲染一篇 wiki 文档。
 * @param {string} md
 * @param {{ resolveLink?: DocLinkContext['resolveLink'] }} [options]
 * @returns {{ html: string, headings: { id: string, text: string, level: number }[] }}
 */
export function renderDocMarkdown(md, options = {}) {
  const resolveLink = options.resolveLink || ((href) => ({ href, external: /^[a-z]+:/i.test(href) }))
  /** @type {{ id: string, text: string, level: number }[]} */
  const headings = []
  const ctx = { headings, seen: new Map(), resolveLink, top: true }
  const html = renderBlocks(String(md || '').replace(/\r\n/g, '\n').split('\n'), ctx)
  return { html, headings }
}
