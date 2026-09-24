<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { WIKI_BASE, docCategories, docs, findDoc, pageToSlug, readingMinutes } from '../data/docs'
import { renderDocMarkdown } from '../utils/markdown'

const props = defineProps({
  /** 'zh' | 'en' */
  locale: { type: String, default: 'zh' },
  /** 空串表示文档索引页，否则是某篇文章 */
  slug: { type: String, default: '' },
  /** 文章内的标题锚点（不含 `doc-` 前缀） */
  anchor: { type: String, default: '' },
  reducedMotion: { type: Boolean, default: false },
})

const t = (pair) => (props.locale === 'zh' ? pair.zh : pair.en)

/* 每篇单独打包，点开哪篇加载哪篇，首页不背这些正文。 */
const sources = import.meta.glob('../wiki/*.md', { query: '?raw', import: 'default' })

const doc = computed(() => findDoc(props.slug))
const source = ref('')
const loading = ref(false)
const failed = ref(false)
let loadToken = 0

async function loadDoc(slug) {
  const token = ++loadToken
  source.value = ''
  failed.value = false
  if (!slug) {
    loading.value = false
    return
  }
  const load = sources[`../wiki/${slug}.md`]
  if (!load) {
    loading.value = false
    failed.value = true
    return
  }
  loading.value = true
  try {
    const text = await load()
    if (token !== loadToken) return
    source.value = text
  } catch {
    if (token === loadToken) failed.value = true
  } finally {
    if (token === loadToken) loading.value = false
  }
}

/** 站内锚点写成 `#/docs/<slug>/<锚点>`，hash 路由才不会被顶掉。 */
function anchorHref(slug, id) {
  return `#/docs/${slug}/${encodeURIComponent(id)}`
}

function resolveLink(href) {
  if (href.startsWith('#')) {
    return { href: anchorHref(props.slug, href.slice(1)), external: false }
  }
  if (/^[a-z][a-z0-9+.-]*:/i.test(href)) return { href, external: true }
  const page = href.split(/[#?]/)[0].replace(/\.md$/i, '')
  const inSite = pageToSlug[page]
  if (inSite) return { href: `#/docs/${inSite}`, external: false }
  return { href: `${WIKI_BASE}/${href}`, external: true }
}

/** 正文开头的 H1 与页头标题重复，渲染前去掉（有的页面标题前还挂着一张图）。 */
const body = computed(() => {
  const lines = source.value.split('\n')
  for (let i = 0; i < lines.length; i += 1) {
    if (/^\s*(```|~~~|#{2,6}\s)/.test(lines[i])) break
    if (/^#\s+/.test(lines[i])) {
      lines.splice(i, 1)
      break
    }
  }
  return lines.join('\n')
})

const rendered = computed(() =>
  source.value ? renderDocMarkdown(body.value, { resolveLink }) : { html: '', headings: [] },
)

const toc = computed(() => rendered.value.headings.filter((h) => h.level <= 3))

/* —— 索引页 —— */
const activeCategory = ref('all')
const categoryTabs = computed(() => [
  { id: 'all', name: { zh: '全部', en: 'All' }, count: docs.length },
  ...docCategories.map((c) => ({
    ...c,
    count: docs.filter((d) => d.category === c.id).length,
  })),
])

const groups = computed(() =>
  docCategories
    .filter((c) => activeCategory.value === 'all' || activeCategory.value === c.id)
    .map((c) => ({ ...c, items: docs.filter((d) => d.category === c.id) }))
    .filter((g) => g.items.length),
)

/* —— 上一篇 / 下一篇 —— */
const position = computed(() => docs.findIndex((d) => d.slug === props.slug))
const prevDoc = computed(() => (position.value > 0 ? docs[position.value - 1] : null))
const nextDoc = computed(() =>
  position.value >= 0 && position.value < docs.length - 1 ? docs[position.value + 1] : null,
)

const categoryName = computed(() => {
  const c = docCategories.find((x) => x.id === doc.value?.category)
  return c ? t(c.name) : ''
})

/* —— 阅读进度与目录高亮 —— */
const progress = ref(0)
const activeHeading = ref('')
const bodyRef = ref(null)
let ticking = false

function measure() {
  const el = bodyRef.value
  if (!el) return

  const box = el.getBoundingClientRect()
  const span = box.height - window.innerHeight * 0.6
  const read = -box.top
  progress.value = span > 0 ? Math.min(1, Math.max(0, read / span)) : 1

  let current = ''
  for (const h of toc.value) {
    const node = document.getElementById(`doc-${h.id}`)
    if (node && node.getBoundingClientRect().top <= 96) current = h.id
  }
  activeHeading.value = current
}

function onScroll() {
  if (ticking) return
  ticking = true
  requestAnimationFrame(() => {
    measure()
    ticking = false
  })
}

function scrollToAnchor(id) {
  if (!id) return
  const node = document.getElementById(`doc-${id}`)
  if (!node) return
  const y = node.getBoundingClientRect().top + window.scrollY - 76
  window.scrollTo({ top: y, behavior: props.reducedMotion ? 'auto' : 'smooth' })
}

/* —— 代码块一键复制 —— */
const copyLabel = { zh: ['复制', '已复制'], en: ['Copy', 'Copied'] }

function decorateCode() {
  const root = bodyRef.value
  if (!root) return
  for (const pre of root.querySelectorAll('pre.md-code')) {
    if (pre.querySelector('.md-copy')) continue
    const btn = document.createElement('button')
    btn.type = 'button'
    btn.className = 'md-copy'
    btn.textContent = copyLabel[props.locale === 'zh' ? 'zh' : 'en'][0]
    btn.addEventListener('click', async () => {
      const text = pre.querySelector('code')?.textContent ?? ''
      try {
        await navigator.clipboard.writeText(text)
        btn.textContent = copyLabel[props.locale === 'zh' ? 'zh' : 'en'][1]
        btn.classList.add('done')
        window.setTimeout(() => {
          btn.textContent = copyLabel[props.locale === 'zh' ? 'zh' : 'en'][0]
          btn.classList.remove('done')
        }, 1600)
      } catch {
        /* 剪贴板不可用时按钮保持原样 */
      }
    })
    pre.appendChild(btn)
  }
}

/** 文档页自己维护标题，方便收藏与分享；离开时由 App 恢复成 Moonward。 */
function syncTitle() {
  const label = props.locale === 'zh' ? '相关文档' : 'Docs'
  document.title = doc.value ? `${t(doc.value.title)} · ${label} · Moonward` : `${label} · Moonward`
}

watch(
  () => props.slug,
  (slug) => {
    loadDoc(slug)
    syncTitle()
  },
  { immediate: true },
)

function decorateFigures() {
  const root = bodyRef.value
  if (!root) return
  for (const img of root.querySelectorAll('.md-figure img')) {
    if (img.dataset.fallback) continue
    img.dataset.fallback = '1'
    const fail = () => {
      const fig = img.closest('.md-figure')
      if (!fig || fig.querySelector('.md-figure-fallback')) return
      fig.classList.add('is-broken')
      const link = document.createElement('a')
      link.className = 'md-figure-fallback'
      link.href = doc.value?.wikiUrl || WIKI_BASE
      link.target = '_blank'
      link.rel = 'noopener noreferrer'
      link.textContent =
        props.locale === 'zh' ? '图片没能加载，去 wiki 看这张图 ↗' : 'Image failed to load — see it on the wiki ↗'
      fig.appendChild(link)
    }
    if (img.complete && img.naturalWidth === 0) fail()
    img.addEventListener('error', fail)
  }
}

watch(
  () => rendered.value.html,
  async () => {
    await nextTick()
    decorateCode()
    decorateFigures()
    measure()
    if (props.anchor) scrollToAnchor(props.anchor)
  },
)

watch(
  () => props.anchor,
  (id) => {
    if (id && source.value) scrollToAnchor(id)
  },
)

watch(
  () => props.locale,
  async () => {
    syncTitle()
    await nextTick()
    for (const btn of bodyRef.value?.querySelectorAll('.md-copy') || []) {
      btn.textContent = copyLabel[props.locale === 'zh' ? 'zh' : 'en'][btn.classList.contains('done') ? 1 : 0]
    }
  },
)

onMounted(() => {
  syncTitle()
  window.addEventListener('scroll', onScroll, { passive: true })
  window.addEventListener('resize', onScroll, { passive: true })
})

onUnmounted(() => {
  window.removeEventListener('scroll', onScroll)
  window.removeEventListener('resize', onScroll)
})
</script>

<template>
  <div class="docs-main">
    <!-- 文章阅读进度 -->
    <div
      v-if="slug"
      class="read-progress"
      :style="{ transform: `scaleX(${progress})` }"
      aria-hidden="true"
    />

    <!-- —————————— 文档索引 —————————— -->
    <div v-if="!slug" class="wrap docs-index">
      <header class="docs-head">
        <p class="kicker mono">WIKI</p>
        <h1>{{ locale === 'zh' ? '相关文档' : 'Documentation' }}</h1>
        <p class="docs-lede">
          {{
            locale === 'zh'
              ? '项目 wiki 里已经写好的文档，按使用、原理、接口、协作分成四组。内容随 wiki 更新，正文为中文。'
              : 'Pages already written in the project wiki, sorted into use, internals, APIs, and contributing. They track the wiki; the articles themselves are in Chinese.'
          }}
        </p>
        <p class="docs-head-links">
          <a class="text-link" :href="WIKI_BASE" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '在 GitHub Wiki 上阅读 ↗' : 'Read on the GitHub Wiki ↗' }}
          </a>
          <a class="text-link" href="#">
            {{ locale === 'zh' ? '返回首页' : 'Back to home' }}
          </a>
        </p>
      </header>

      <nav class="docs-filter" :aria-label="locale === 'zh' ? '文档分组' : 'Doc categories'">
        <button
          v-for="tab in categoryTabs"
          :key="tab.id"
          type="button"
          class="filter-chip"
          :class="{ 'is-on': activeCategory === tab.id }"
          :aria-pressed="activeCategory === tab.id ? 'true' : 'false'"
          @click="activeCategory = tab.id"
        >
          {{ t(tab.name) }}<span class="chip-count">{{ tab.count }}</span>
        </button>
      </nav>

      <section v-for="group in groups" :key="group.id" class="docs-group">
        <div class="group-head">
          <h2>{{ t(group.name) }}</h2>
          <p class="group-desc">{{ t(group.desc) }}</p>
        </div>

        <ul class="post-list">
          <li v-for="item in group.items" :key="item.slug" class="post" :data-accent="item.accent">
            <a class="post-link" :href="`#/docs/${item.slug}`">
              <h3 class="post-title">{{ t(item.title) }}</h3>
              <p class="post-meta mono">
                <span>{{ item.updated }}</span>
                <span aria-hidden="true">·</span>
                <span>
                  {{
                    locale === 'zh'
                      ? `约 ${readingMinutes(item.chars)} 分钟`
                      : `${readingMinutes(item.chars)} min read`
                  }}
                </span>
              </p>
              <p class="post-summary">{{ t(item.summary) }}</p>
              <span class="post-more">{{ locale === 'zh' ? '阅读全文 →' : 'Read →' }}</span>
            </a>
          </li>
        </ul>
      </section>
    </div>

    <!-- —————————— 找不到这篇 —————————— -->
    <div v-else-if="!doc" class="wrap docs-missing">
      <h1>{{ locale === 'zh' ? '没有这篇文档' : 'No such page' }}</h1>
      <p class="docs-lede">
        {{
          locale === 'zh'
            ? '链接可能已经过期，或这篇还没有收录到站内。'
            : 'The link may be out of date, or the page is not mirrored here yet.'
        }}
      </p>
      <p class="docs-head-links">
        <a class="text-link" href="#/docs">{{ locale === 'zh' ? '回到文档列表' : 'Back to the list' }}</a>
        <a class="text-link" :href="WIKI_BASE" target="_blank" rel="noopener noreferrer">
          {{ locale === 'zh' ? '去 GitHub Wiki 找找 ↗' : 'Look on the GitHub Wiki ↗' }}
        </a>
      </p>
    </div>

    <!-- —————————— 单篇文章 —————————— -->
    <div v-else class="wrap doc-shell">
      <article class="doc-article">
        <div class="doc-sheet">
        <header class="doc-head">
          <p class="crumb mono">
            <a href="#/docs">{{ locale === 'zh' ? '相关文档' : 'Docs' }}</a>
            <span aria-hidden="true">/</span>
            <span>{{ categoryName }}</span>
          </p>
          <h1>{{ t(doc.title) }}</h1>
          <p class="doc-lede">{{ t(doc.summary) }}</p>
          <p class="doc-meta mono">
            <span>
              {{ locale === 'zh' ? `更新于 ${doc.updated}` : `Updated ${doc.updated}` }}
            </span>
            <span aria-hidden="true">·</span>
            <span>
              {{
                locale === 'zh'
                  ? `约 ${readingMinutes(doc.chars)} 分钟`
                  : `${readingMinutes(doc.chars)} min read`
              }}
            </span>
            <span aria-hidden="true">·</span>
            <a :href="doc.wikiUrl" target="_blank" rel="noopener noreferrer">
              {{ locale === 'zh' ? 'wiki 原文 ↗' : 'Source on the wiki ↗' }}
            </a>
          </p>
          <p v-if="locale === 'en'" class="doc-note">
            This article is written in Chinese — the wiki has no English version yet.
          </p>
        </header>

        <details v-if="toc.length" class="toc-fold">
          <summary>{{ locale === 'zh' ? '目录' : 'Contents' }}</summary>
          <ol class="toc-list">
            <li v-for="h in toc" :key="h.id" :data-level="h.level">
              <a :href="anchorHref(slug, h.id)">{{ h.text }}</a>
            </li>
          </ol>
        </details>

        <p v-if="loading" class="doc-state">{{ locale === 'zh' ? '正在载入…' : 'Loading…' }}</p>
        <p v-else-if="failed" class="doc-state">
          {{ locale === 'zh' ? '这篇没能载入。' : 'This page failed to load.' }}
          <a :href="doc.wikiUrl" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '去 wiki 阅读 ↗' : 'Read it on the wiki ↗' }}
          </a>
        </p>
        <div v-else ref="bodyRef" class="md-body" v-html="rendered.html" />
        </div>

        <nav class="doc-pager" :aria-label="locale === 'zh' ? '相邻文档' : 'Adjacent pages'">
          <a v-if="prevDoc" class="pager-link" :href="`#/docs/${prevDoc.slug}`">
            <span class="pager-dir mono">{{ locale === 'zh' ? '上一篇' : 'Previous' }}</span>
            <span class="pager-title">{{ t(prevDoc.title) }}</span>
          </a>
          <span v-else />
          <a v-if="nextDoc" class="pager-link next" :href="`#/docs/${nextDoc.slug}`">
            <span class="pager-dir mono">{{ locale === 'zh' ? '下一篇' : 'Next' }}</span>
            <span class="pager-title">{{ t(nextDoc.title) }}</span>
          </a>
        </nav>
      </article>

      <aside v-if="toc.length" class="doc-side">
        <nav class="toc" :aria-label="locale === 'zh' ? '本页目录' : 'On this page'">
          <p class="toc-title mono">{{ locale === 'zh' ? '目录' : 'On this page' }}</p>
          <ol class="toc-list">
            <li v-for="h in toc" :key="h.id" :data-level="h.level">
              <a
                :href="anchorHref(slug, h.id)"
                :class="{ 'is-on': activeHeading === h.id }"
                :aria-current="activeHeading === h.id ? 'true' : undefined"
              >{{ h.text }}</a>
            </li>
          </ol>
        </nav>
        <a class="toc-back text-link" href="#/docs">
          {{ locale === 'zh' ? '← 全部文档' : '← All docs' }}
        </a>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.docs-main {
  flex: 1;
  padding: 1.75rem 0 2.5rem;
}

/* 顶栏下沿的阅读进度条 */
.read-progress {
  position: fixed;
  top: 3.1rem;
  left: 0;
  right: 0;
  height: 2px;
  z-index: 19;
  background: var(--accent);
  transform-origin: 0 50%;
  opacity: 0.85;
}

/* —— 索引页 —— */
.docs-head {
  max-width: 44rem;
  margin-bottom: 1.6rem;
}

.kicker {
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--muted);
  margin: 0 0 0.5rem;
}

.docs-head h1 {
  margin-bottom: 0.55rem;
}

.docs-lede {
  color: var(--muted);
  font-size: 1rem;
  line-height: 1.7;
  max-width: 40rem;
}

.docs-head-links {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem 1.1rem;
  margin-top: 0.9rem;
}

.text-link {
  font-family: var(--font-sans);
  font-size: 0.88rem;
}

.docs-filter {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  padding-bottom: 1.35rem;
  border-bottom: 1px solid var(--line);
  margin-bottom: 0.4rem;
}

.filter-chip {
  display: inline-flex;
  align-items: baseline;
  gap: 0.35rem;
  padding: 0.3rem 0.8rem;
  border: 1px solid var(--line);
  border-radius: 999px;
  background: color-mix(in srgb, var(--bg-card) 70%, transparent);
  font-family: var(--font-sans);
  font-size: 0.84rem;
  color: var(--ink-2);
  transition: border-color 0.15s ease, color 0.15s ease, background 0.15s ease;
}

.filter-chip:hover {
  border-color: var(--line-strong);
  color: var(--ink);
}

.filter-chip.is-on {
  background: var(--accent-soft);
  border-color: color-mix(in srgb, var(--accent) 45%, transparent);
  color: var(--ink);
}

.chip-count {
  font-family: var(--font-mono);
  font-size: 0.7rem;
  color: var(--muted);
}

.filter-chip.is-on .chip-count {
  color: var(--accent);
}

.docs-group {
  margin-top: 1.25rem;
  padding: 1.3rem 1.4rem 0.75rem;
  background: var(--bg-card);
  border: 1px solid var(--line);
  border-radius: var(--radius);
  box-shadow: var(--shadow-sm);
}

.group-head {
  margin-bottom: 0.25rem;
}

.group-head h2 {
  font-size: 1.15rem;
}

.group-desc {
  font-size: 0.88rem;
  color: var(--muted);
  font-family: var(--font-sans);
}

.post-list {
  margin-top: 0.75rem;
}

.post {
  border-top: 1px solid var(--line);
}

.post[data-accent='teal'] { --post-accent: var(--teal); }
.post[data-accent='amber'] { --post-accent: var(--amber); }
.post[data-accent='blue'] { --post-accent: var(--blue); }
.post[data-accent='green'] { --post-accent: var(--green); }
.post[data-accent='violet'] { --post-accent: var(--violet); }
.post[data-accent='rose'] { --post-accent: var(--rose); }
.post[data-accent='cyan'] { --post-accent: var(--cyan); }
.post[data-accent='indigo'] { --post-accent: var(--indigo); }
.post[data-accent='slate'] { --post-accent: var(--slate); }

.post-link {
  display: block;
  position: relative;
  padding: 1rem 1rem 1.05rem 1.15rem;
  text-decoration: none;
  color: inherit;
  border-radius: var(--radius-sm);
  transition: background 0.15s ease;
}

.post-link::before {
  content: '';
  position: absolute;
  left: 0;
  top: 1.15rem;
  bottom: 1.2rem;
  width: 2px;
  border-radius: 2px;
  background: var(--post-accent, var(--accent));
  opacity: 0;
  transition: opacity 0.15s ease;
}

.post-link:hover {
  background: color-mix(in srgb, var(--bg-raised) 75%, transparent);
}

.post-link:hover::before,
.post-link:focus-visible::before {
  opacity: 0.8;
}

.post-link:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.post-title {
  font-size: 1.12rem;
  margin-bottom: 0.2rem;
}

.post-link:hover .post-title {
  color: var(--accent);
}

.post-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  font-size: 0.74rem;
  color: var(--muted);
  margin-bottom: 0.4rem;
}

.post-summary {
  font-size: 0.94rem;
  line-height: 1.7;
  color: var(--ink-2);
  max-width: 46rem;
}

.post-more {
  display: inline-block;
  margin-top: 0.5rem;
  font-family: var(--font-sans);
  font-size: 0.82rem;
  color: var(--accent);
}

/* —— 单篇文章 —— */
.doc-shell {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 14rem;
  gap: 2.5rem;
  align-items: start;
}

.doc-article {
  min-width: 0;
  max-width: 48rem;
}

.doc-sheet {
  padding: 1.75rem 2.1rem 2.1rem;
  background: var(--bg-card);
  border: 1px solid var(--line);
  border-radius: var(--radius);
  box-shadow: var(--shadow-sm);
}

.doc-head {
  padding-bottom: 1.15rem;
  border-bottom: 1px solid var(--line);
  margin-bottom: 1.5rem;
}

.crumb {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  font-size: 0.74rem;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--muted);
  margin-bottom: 0.7rem;
}

.crumb a {
  color: var(--muted);
  text-decoration: none;
}

.crumb a:hover {
  color: var(--accent);
}

.doc-head h1 {
  font-size: clamp(1.75rem, 3.4vw, 2.3rem);
  margin-bottom: 0.6rem;
}

.doc-lede {
  font-size: 1rem;
  line-height: 1.75;
  color: var(--muted);
  max-width: 40rem;
}

.doc-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  font-size: 0.76rem;
  color: var(--muted);
  margin-top: 0.85rem;
}

.doc-note {
  margin-top: 0.8rem;
  padding: 0.5rem 0.75rem;
  border-left: 2px solid var(--line-strong);
  background: var(--bg-raised);
  font-family: var(--font-sans);
  font-size: 0.84rem;
  color: var(--muted);
}

.doc-state {
  font-family: var(--font-sans);
  font-size: 0.92rem;
  color: var(--muted);
  padding: 2rem 0;
}

/* 窄屏折叠目录 */
.toc-fold {
  display: none;
  margin-bottom: 1.5rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
}

.toc-fold > summary {
  cursor: pointer;
  padding: 0.6rem 0.85rem;
  font-family: var(--font-sans);
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--ink);
  list-style: none;
}

.toc-fold > summary::-webkit-details-marker {
  display: none;
}

.toc-fold > summary::after {
  content: '＋';
  float: right;
  color: var(--muted);
}

.toc-fold[open] > summary::after {
  content: '－';
}

.toc-fold .toc-list {
  padding: 0 0.85rem 0.8rem;
}

/* 侧边目录 */
.doc-side {
  position: sticky;
  top: 4.5rem;
  max-height: calc(100vh - 6rem);
  overflow-y: auto;
  padding-left: 1.25rem;
  border-left: 1px solid var(--line);
  font-family: var(--font-sans);
}

.toc-title {
  font-size: 0.7rem;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--muted);
  margin-bottom: 0.55rem;
}

.toc-list {
  margin: 0;
  padding: 0;
  list-style: none;
  counter-reset: none;
}

.toc-list li[data-level='3'] {
  padding-left: 0.85rem;
}

.toc-list a {
  display: block;
  padding: 0.24rem 0;
  font-size: 0.83rem;
  line-height: 1.45;
  color: var(--muted);
  text-decoration: none;
  border-left: 2px solid transparent;
  padding-left: 0.6rem;
  margin-left: -0.62rem;
  transition: color 0.15s ease, border-color 0.15s ease;
}

.toc-list a:hover {
  color: var(--ink);
}

.toc-list a.is-on {
  color: var(--accent);
  border-left-color: var(--accent);
}

.toc-back {
  display: inline-block;
  margin-top: 1rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--line);
  width: 100%;
}

/* 上一篇 / 下一篇 */
.doc-pager {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
  margin-top: 1.1rem;
}

.pager-link {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  padding: 0.7rem 0.9rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-card);
  text-decoration: none;
  transition: border-color 0.15s ease, transform 0.15s ease;
}

.pager-link:hover {
  border-color: var(--line-strong);
  transform: translateY(-1px);
}

.pager-link.next {
  text-align: right;
}

.pager-dir {
  font-size: 0.7rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--muted);
}

.pager-title {
  font-family: var(--font-serif);
  font-size: 0.96rem;
  color: var(--ink);
}

.docs-missing {
  padding: 3rem 0 4rem;
}

.docs-missing h1 {
  margin-bottom: 0.6rem;
}

/* —————————— 正文排版 —————————— */
.md-body {
  font-size: 1.02rem;
  line-height: 1.85;
  color: var(--ink-2);
}

.md-body :deep(> :first-child) {
  margin-top: 0;
}

.md-body :deep(h1),
.md-body :deep(h2),
.md-body :deep(h3),
.md-body :deep(h4),
.md-body :deep(h5),
.md-body :deep(h6) {
  position: relative;
  font-family: var(--font-serif);
  color: var(--ink);
  font-weight: 600;
  line-height: 1.35;
  scroll-margin-top: 5rem;
}

.md-body :deep(h1) {
  font-size: 1.6rem;
  margin: 2.4rem 0 0.8rem;
}

.md-body :deep(h2) {
  font-size: 1.32rem;
  margin: 2.3rem 0 0.75rem;
  padding-bottom: 0.35rem;
  border-bottom: 1px solid var(--line);
}

.md-body :deep(h3) {
  font-size: 1.12rem;
  margin: 1.8rem 0 0.6rem;
}

.md-body :deep(h4),
.md-body :deep(h5),
.md-body :deep(h6) {
  font-size: 1rem;
  margin: 1.4rem 0 0.5rem;
}

.md-body :deep(.md-anchor) {
  position: absolute;
  left: -1.1rem;
  top: 0;
  width: 1.1rem;
  font-family: var(--font-mono);
  font-size: 0.85em;
  font-weight: 400;
  color: var(--line-strong);
  text-decoration: none;
  user-select: none;
  opacity: 0;
  transition: opacity 0.15s ease;
}

.md-body :deep(h2:hover) .md-anchor,
.md-body :deep(h3:hover) .md-anchor,
.md-body :deep(h4:hover) .md-anchor {
  opacity: 1;
}

.md-body :deep(p) {
  margin: 0 0 1rem;
}

.md-body :deep(ul),
.md-body :deep(ol) {
  margin: 0 0 1rem;
  padding-left: 1.45rem;
  list-style: disc;
}

.md-body :deep(ol) {
  list-style: decimal;
}

.md-body :deep(li) {
  margin: 0.3rem 0;
}

.md-body :deep(li > ul),
.md-body :deep(li > ol) {
  margin: 0.3rem 0 0.4rem;
}

.md-body :deep(a) {
  color: var(--accent);
  text-decoration: underline;
  text-decoration-color: color-mix(in srgb, var(--accent) 35%, transparent);
}

.md-body :deep(a:hover) {
  color: var(--ink);
  text-decoration-color: currentColor;
}

.md-body :deep(strong) {
  color: var(--ink);
  font-weight: 600;
}

.md-body :deep(hr) {
  border: 0;
  border-top: 1px solid var(--line);
  margin: 2rem 0;
}

.md-body :deep(code) {
  font-family: var(--font-mono);
  font-size: 0.84em;
  padding: 0.08em 0.36em;
  border-radius: 4px;
  background: var(--code-bg);
  color: var(--ink);
  overflow-wrap: anywhere;
}

.md-body :deep(blockquote) {
  margin: 0 0 1rem;
  padding: 0.15rem 0 0.15rem 1rem;
  border-left: 2px solid color-mix(in srgb, var(--accent) 55%, transparent);
  color: var(--muted);
  background: color-mix(in srgb, var(--accent-soft) 45%, transparent);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
}

.md-body :deep(blockquote > *) {
  margin-top: 0.7rem;
  margin-bottom: 0.7rem;
}

.md-body :deep(.md-code) {
  position: relative;
  margin: 0 0 1.15rem;
  padding: 0.85rem 1rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--code-bg);
  overflow-x: auto;
  font-size: 0.84rem;
  line-height: 1.65;
}

.md-body :deep(.md-code code) {
  display: block;
  padding: 0;
  background: none;
  font-size: 1em;
  white-space: pre;
}

.md-body :deep(.md-code[data-lang])::before {
  content: attr(data-lang);
  position: absolute;
  top: 0;
  right: 0;
  padding: 0.1rem 0.5rem;
  border-left: 1px solid var(--line);
  border-bottom: 1px solid var(--line);
  border-radius: 0 var(--radius-sm) 0 var(--radius-sm);
  font-family: var(--font-mono);
  font-size: 0.68rem;
  letter-spacing: 0.04em;
  color: var(--muted);
  background: var(--bg-raised);
}

.md-body :deep(.md-copy) {
  position: absolute;
  top: 0.4rem;
  right: 0.45rem;
  padding: 0.16rem 0.5rem;
  border: 1px solid var(--line-strong);
  border-radius: 5px;
  background: var(--bg-card);
  font-family: var(--font-sans);
  font-size: 0.72rem;
  color: var(--muted);
  opacity: 0;
  transition: opacity 0.15s ease, color 0.15s ease;
}

.md-body :deep(.md-code:hover) .md-copy,
.md-body :deep(.md-copy:focus-visible) {
  opacity: 1;
}

.md-body :deep(.md-copy:hover),
.md-body :deep(.md-copy.done) {
  color: var(--accent);
}

.md-body :deep(.md-code[data-lang]) .md-copy {
  top: 1.85rem;
}

.md-body :deep(.md-table-wrap) {
  margin: 0 0 1.2rem;
  overflow-x: auto;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
}

.md-body :deep(table) {
  border-collapse: collapse;
  width: 100%;
  font-family: var(--font-sans);
  font-size: 0.86rem;
  line-height: 1.6;
}

.md-body :deep(th),
.md-body :deep(td) {
  padding: 0.5rem 0.75rem;
  text-align: left;
  vertical-align: top;
  border-bottom: 1px solid var(--line);
}

.md-body :deep(th) {
  background: var(--bg-raised);
  color: var(--ink);
  font-weight: 600;
  white-space: nowrap;
}

.md-body :deep(tbody tr:last-child td) {
  border-bottom: 0;
}

.md-body :deep(tbody tr:hover) {
  background: color-mix(in srgb, var(--bg-raised) 60%, transparent);
}

.md-body :deep(.md-figure) {
  margin: 0 0 1.2rem;
  padding: 0.75rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
  text-align: center;
}

.md-body :deep(.md-figure img) {
  max-width: 100%;
  height: auto;
  border-radius: 4px;
}

.md-body :deep(.md-figure.is-broken img) {
  display: none;
}

.md-body :deep(.md-figure-fallback) {
  font-family: var(--font-sans);
  font-size: 0.86rem;
  color: var(--muted);
}

.md-body :deep(del) {
  color: var(--muted);
}

@media (max-width: 960px) {
  .doc-shell {
    grid-template-columns: minmax(0, 1fr);
  }

  .doc-side {
    display: none;
  }

  .toc-fold {
    display: block;
  }

  .doc-article {
    max-width: none;
  }
}

@media (max-width: 560px) {
  .docs-main {
    padding-top: 1.15rem;
  }

  .doc-pager {
    grid-template-columns: 1fr;
  }

  .pager-link.next {
    text-align: left;
  }

  .post-link {
    padding: 0.85rem 0.5rem 0.9rem 0.8rem;
  }

  .docs-group {
    padding: 1.05rem 1rem 0.6rem;
  }

  .doc-sheet {
    padding: 1.2rem 1.1rem 1.4rem;
  }

  .md-body :deep(.md-anchor) {
    display: none;
  }
}
</style>
