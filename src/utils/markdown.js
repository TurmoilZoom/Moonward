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
