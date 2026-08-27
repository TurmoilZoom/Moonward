<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import {
  checkInFlow,
  featureCards,
  games,
  intro,
  launchFlow,
  links,
  requirements,
} from './data/content'
import Screens from './components/Screens.vue'
import { asset } from './utils/asset'
import { renderReleaseMarkdown } from './utils/markdown'
import {
  applyTheme,
  readThemePref,
  resolveTheme,
  storeThemePref,
  watchSystemTheme,
} from './utils/theme'
import {
  CHANNELS,
  applyChannel,
  detectPreferredArch,
  fetchReleaseCatalogs,
  findPackage,
  formatBytes,
  formatPublishedAt,
  loadStoredChannel,
  pickLatestCatalog,
  refinePreferredArch,
  storeChannel,
} from './utils/releases'

const locale = ref(localStorage.getItem('moonward-locale') || 'zh')
const t = computed(() => (keyObj) => keyObj[locale.value] ?? keyObj.zh)

watch(locale, (v) => {
  localStorage.setItem('moonward-locale', v)
  document.documentElement.lang = v === 'zh' ? 'zh-CN' : 'en'
})

function toggleLocale() {
  locale.value = locale.value === 'zh' ? 'en' : 'zh'
}

const themePref = ref(readThemePref())
const resolvedTheme = ref(
  document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : resolveTheme(themePref.value),
)
const themeArmed = ref(false)
const themeBusy = ref(false)
/** @type {import('vue').Ref<'' | 'to-light' | 'to-dark'>} */
const skyPlay = ref('')
let stopSystemWatch = null
let themeGen = 0

function commitTheme(next) {
  themePref.value = next
  resolvedTheme.value = next
  storeThemePref(next)
  applyTheme(next)
}

function wait(ms) {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

async function toggleTheme() {
  if (themeBusy.value) return
  const next = resolvedTheme.value === 'dark' ? 'light' : 'dark'
  if (reducedMotion.value) {
    commitTheme(next)
    return
  }

  const gen = ++themeGen
  themeBusy.value = true
  skyPlay.value = next === 'light' ? 'to-light' : 'to-dark'
  try {
    // 幕布盖住大半后再换变量，避免颜色硬切
    await wait(360)
    if (gen !== themeGen) return
    commitTheme(next)
    await wait(600)
  } finally {
    if (gen === themeGen) {
      skyPlay.value = ''
      themeBusy.value = false
    }
  }
}

/* —— Release downloads & notes —— */
const releaseLoading = ref(true)
const releaseError = ref(false)
const catalogs = ref([])
const selectedTag = ref('')
const preferredArch = ref(detectPreferredArch())
/** @type {import('vue').Ref<'github' | 'cnb'>} */
const downloadChannel = ref(loadStoredChannel())

const channelOptions = [
  CHANNELS.cnb,
  CHANNELS.github,
]

const activeChannelMeta = computed(
  () => CHANNELS[downloadChannel.value] || CHANNELS.cnb,
)

const releasesPageHref = computed(() => activeChannelMeta.value.releasesPage)

const selectedCatalog = computed(
  () => catalogs.value.find((c) => c.tag === selectedTag.value) || catalogs.value[0] || null,
)

const release = computed(() =>
  selectedCatalog.value ? applyChannel(selectedCatalog.value, downloadChannel.value) : null,
)

const latestRelease = computed(() => {
  const catalog = pickLatestCatalog(catalogs.value)
  return catalog ? applyChannel(catalog, downloadChannel.value) : null
})

const latestTag = computed(() => latestRelease.value?.tag || '')

const notesHtml = computed(() => renderReleaseMarkdown(selectedCatalog.value?.body || ''))

const publishedLabel = computed(() =>
  formatPublishedAt(selectedCatalog.value?.publishedAt, locale.value),
)

const selectedReleaseHref = computed(() => release.value?.htmlUrl || releasesPageHref.value)

const archColumns = [
  { id: 'x64', label: { zh: 'Windows x64', en: 'Windows x64' } },
  { id: 'arm64', label: { zh: 'Windows ARM64', en: 'Windows ARM64' } },
]

const recommendedSetup = computed(() => {
  const pkgs = latestRelease.value?.packages
  if (!pkgs?.length) return null
  return (
    findPackage(pkgs, preferredArch.value, 'setup') ||
    findPackage(pkgs, 'x64', 'setup') ||
    findPackage(pkgs, preferredArch.value, 'portable') ||
    pkgs[0] ||
    null
  )
})

const heroDownloadHref = computed(
  () => recommendedSetup.value?.url || '#install',
)

function packageFor(arch, kind) {
  return findPackage(release.value?.packages || [], arch, kind)
}

function optionLabel(catalog) {
  const tag = catalog.tag || catalog.name
  const bits = [tag]
  if (catalog.tag === latestTag.value) {
    bits.push(locale.value === 'zh' ? '最新' : 'latest')
  }
  if (catalog.prerelease) {
    bits.push(locale.value === 'zh' ? '预览' : 'pre')
  }
  return bits.join(' · ')
}

function kindLabel(kind) {
  if (locale.value === 'zh') {
    return kind === 'setup' ? '安装包' : '便携版'
  }
  return kind === 'setup' ? 'Setup' : 'Portable'
}

function kindHint(kind) {
  if (locale.value === 'zh') {
    return kind === 'setup' ? '推荐 · 向导安装' : '解压即用 · 免安装'
  }
  return kind === 'setup' ? 'Recommended installer' : 'Unpack & run'
}

function sizeLabel(pkg) {
  if (!pkg?.size) return ''
  return formatBytes(pkg.size, locale.value)
}

/**
 * @param {AbortSignal} [signal]
 * @param {{ silent?: boolean }} [opts] silent：已有卡片时不置 loading，避免整区闪烁
 */
async function loadRelease(signal, opts = {}) {
  const silent = Boolean(opts.silent && catalogs.value.length)
  if (!silent) {
    releaseLoading.value = true
  }
  releaseError.value = false
  try {
    preferredArch.value = await refinePreferredArch(preferredArch.value)
    if (signal?.aborted) return
    const list = await fetchReleaseCatalogs(signal)
    if (signal?.aborted) return
    catalogs.value = list
    if (!list.some((c) => c.tag === selectedTag.value)) {
      selectedTag.value = pickLatestCatalog(list)?.tag || list[0]?.tag || ''
    }
    if (!list.length) {
      releaseError.value = true
    }
  } catch (e) {
    // 切换线路 abort 旧请求时勿当成失败刷屏
    if (signal?.aborted || (e && /** @type {Error} */ (e).name === 'AbortError')) return
    releaseError.value = true
    if (!silent) {
      catalogs.value = []
    }
  } finally {
    if (!silent) {
      releaseLoading.value = false
    }
  }
}

/**
 * 切换下载渠道（GitHub / CNB）。
 * 已有安装包清单时只改写 URL，不进 loading、不重新请求。
 * @param {'github' | 'cnb'} channel
 */
function setDownloadChannel(channel) {
  if (channel !== 'github' && channel !== 'cnb') return
  if (downloadChannel.value === channel && catalogs.value.length && !releaseError.value) return
  downloadChannel.value = channel
  storeChannel(channel)

  // 清单与渠道无关：本地换链即可，卡片不卸载
  if (catalogs.value.length && !releaseError.value) {
    return
  }

  // 首次失败或尚无数据：完整拉取
  releaseAbort?.abort()
  releaseAbort = new AbortController()
  loadRelease(releaseAbort.signal)
}

/* —— Mouse parallax (Bilibili-style layered banner) —— */
const heroRef = ref(null)
const px = ref(0)
const py = ref(0)
const reducedMotion = ref(false)
let raf = 0
let targetX = 0
let targetY = 0

function onHeroMove(e) {
  if (reducedMotion.value || !heroRef.value) return
  const rect = heroRef.value.getBoundingClientRect()
  const nx = (e.clientX - rect.left) / rect.width - 0.5
  const ny = (e.clientY - rect.top) / rect.height - 0.5
  targetX = nx
  targetY = ny
  if (!raf) raf = requestAnimationFrame(tickParallax)
}

function onHeroLeave() {
  targetX = 0
  targetY = 0
  if (!raf) raf = requestAnimationFrame(tickParallax)
}

function tickParallax() {
  px.value += (targetX - px.value) * 0.12
  py.value += (targetY - py.value) * 0.12
  if (Math.abs(targetX - px.value) > 0.001 || Math.abs(targetY - py.value) > 0.001) {
    raf = requestAnimationFrame(tickParallax)
  } else {
    px.value = targetX
    py.value = targetY
    raf = 0
  }
}

function layerStyle(depth) {
  const x = px.value * depth * 28
  const y = py.value * depth * 18
  return {
    transform: `translate3d(${x}px, ${y}px, 0)`,
  }
}

/**
 * 月亮层：在按深度平移之外，把归一化的鼠标位置（约 -0.5…0.5）暴露为
 * CSS 变量，供月面明暗界线跟随光标偏移——「光从鼠标方向来」。
 */
const moonLayerStyle = computed(() => {
  const depth = 0.32
  return {
    transform: `translate3d(${px.value * depth * 28}px, ${py.value * depth * 18}px, 0)`,
    '--moon-px': px.value,
    '--moon-py': py.value,
  }
})

const activeStep = ref('config')
const activeCheckInStep = ref('enable')
const showBackTop = ref(false)
const flowFoldRef = ref(null)
const checkinFoldRef = ref(null)
let releaseAbort = null
let scrollTicking = false

function syncFoldFromHash() {
  let hash = (window.location.hash || '').replace(/^#/, '')
  try {
    hash = decodeURIComponent(hash)
  } catch {
    /* keep raw */
  }
  const el = hash === 'flow' ? flowFoldRef.value : hash === 'checkin' ? checkinFoldRef.value : null
  if (!el) return
  el.open = true
  requestAnimationFrame(() => {
    el.scrollIntoView({
      behavior: reducedMotion.value ? 'auto' : 'smooth',
      block: 'start',
    })
  })
}

function onWindowScroll() {
  if (scrollTicking) return
  scrollTicking = true
  requestAnimationFrame(() => {
    showBackTop.value = window.scrollY > 360
    scrollTicking = false
  })
}

function scrollToTop() {
  window.scrollTo({
    top: 0,
    behavior: reducedMotion.value ? 'auto' : 'smooth',
  })
}

onMounted(() => {
  reducedMotion.value = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  document.documentElement.lang = locale.value === 'zh' ? 'zh-CN' : 'en'
  themePref.value = readThemePref()
  resolvedTheme.value = resolveTheme(themePref.value)
  applyTheme(resolvedTheme.value)
  requestAnimationFrame(() => {
    themeArmed.value = true
  })
  stopSystemWatch = watchSystemTheme(() => {
    if (readThemePref() !== 'system') return
    const next = resolveTheme('system')
    if (next === resolvedTheme.value) return
    commitTheme(next)
  })
  releaseAbort = new AbortController()
  loadRelease(releaseAbort.signal)
  window.addEventListener('scroll', onWindowScroll, { passive: true })
  window.addEventListener('hashchange', syncFoldFromHash)
  onWindowScroll()
  syncFoldFromHash()
})

onUnmounted(() => {
  themeGen += 1
  if (raf) cancelAnimationFrame(raf)
  window.removeEventListener('scroll', onWindowScroll)
  window.removeEventListener('hashchange', syncFoldFromHash)
  stopSystemWatch?.()
  releaseAbort?.abort()
})
</script>

<template>
  <div class="page">
    <header class="top">
      <div class="wrap top-inner">
        <a class="brand" href="#">
          <img :src="asset('logo.png')" alt="" width="28" height="28" />
          <span>Moonward</span>
        </a>
        <nav class="nav">
          <a href="#features">{{ locale === 'zh' ? '功能' : 'Features' }}</a>
          <a href="#screens">{{ locale === 'zh' ? '界面' : 'Screens' }}</a>
          <a href="#advanced">{{ locale === 'zh' ? '进阶' : 'Advanced' }}</a>
          <a href="#install">{{ locale === 'zh' ? '安装' : 'Install' }}</a>
          <a :href="links.github" target="_blank" rel="noopener noreferrer">GitHub</a>
          <button
            type="button"
            class="theme-btn"
            :class="{
              'is-dark': resolvedTheme === 'dark',
              armed: themeArmed,
            }"
            :aria-busy="themeBusy ? 'true' : undefined"
            :aria-label="
              locale === 'zh'
                ? resolvedTheme === 'dark'
                  ? '切换为浅色'
                  : '切换为深色'
                : resolvedTheme === 'dark'
                  ? 'Switch to light theme'
                  : 'Switch to dark theme'
            "
            @click="toggleTheme"
          >
            <span class="theme-sky" aria-hidden="true">
              <svg class="celestial sun" viewBox="0 0 24 24" focusable="false">
                <circle cx="12" cy="12" r="4" fill="none" stroke="currentColor" stroke-width="1.75" />
                <path
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.75"
                  stroke-linecap="round"
                  d="M12 3v1.5M12 19.5V21M4.93 4.93l1.06 1.06M18.01 18.01l1.06 1.06M3 12h1.5M19.5 12H21M4.93 19.07l1.06-1.06M18.01 5.99l1.06-1.06"
                />
              </svg>
              <svg class="celestial moon" viewBox="0 0 24 24" focusable="false">
                <path
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.75"
                  stroke-linejoin="round"
                  d="M17.5 14.2A7.2 7.2 0 0 1 9.8 6.5 7 7 0 1 0 17.5 14.2z"
                />
              </svg>
            </span>
          </button>
          <button type="button" class="lang" @click="toggleLocale">
            {{ locale === 'zh' ? 'EN' : '中文' }}
          </button>
        </nav>
      </div>
    </header>

    <!-- Parallax hero -->
    <section
      ref="heroRef"
      class="hero"
      @mousemove="onHeroMove"
      @mouseleave="onHeroLeave"
      aria-labelledby="hero-title"
    >
      <div class="hero-layers" aria-hidden="true">
        <div class="layer layer-sky" :style="layerStyle(0.08)" />
        <div class="layer layer-stars stars-far" :style="layerStyle(0.18)" />
        <div class="layer layer-shoot"><span class="shoot" /></div>
        <div class="layer layer-halo" :style="layerStyle(0.28)" />
        <div class="layer layer-moon" :style="moonLayerStyle">
          <span class="moon-disc" />
          <span class="moon-shade" />
        </div>
        <div class="layer layer-stars stars-near" :style="layerStyle(0.4)">
          <span class="twinkle t1" />
          <span class="twinkle t2" />
          <span class="twinkle t3" />
        </div>
        <div class="layer layer-cloud cloud-a" :style="layerStyle(0.7)" />
        <div class="layer layer-cloud cloud-b" :style="layerStyle(1.05)" />
      </div>

      <div class="wrap hero-content" :style="layerStyle(0.08)">
        <p class="kicker mono">Windows · WinUI 3 · MIT</p>
        <div class="hero-title-row">
          <img class="hero-logo" :src="asset('logo.png')" alt="" width="56" height="56" :style="layerStyle(0.65)" />
          <h1 id="hero-title" :style="layerStyle(0.35)">Moonward</h1>
        </div>
        <p class="lede">{{ t(intro) }}</p>
        <ul class="games" aria-label="Supported games">
          <li v-for="name in games[locale]" :key="name">{{ name }}</li>
        </ul>
        <p class="actions">
          <a
            class="btn"
            :href="heroDownloadHref"
            rel="noopener noreferrer"
          >
            {{
              locale === 'zh'
                ? recommendedSetup
                  ? `下载 ${recommendedSetup.arch === 'arm64' ? 'ARM64' : 'x64'} ${kindLabel(recommendedSetup.kind)}`
                  : '选择安装包'
                : recommendedSetup
                  ? `Download ${recommendedSetup.arch} ${kindLabel(recommendedSetup.kind)}`
                  : 'Get installer'
            }}
          </a>
          <a class="btn ghost" href="#install">
            {{ locale === 'zh' ? '全部版本' : 'All packages' }}
          </a>
          <a class="text-link" :href="links.github" target="_blank" rel="noopener noreferrer">
            GitHub
          </a>
        </p>
      </div>
    </section>

    <main class="wrap main">
      <!-- Feature cards -->
      <section id="features" class="block" aria-labelledby="features-heading">
        <h2 id="features-heading">{{ locale === 'zh' ? '功能一览' : 'Features' }}</h2>
        <p class="section-lead">
          {{
            locale === 'zh'
              ? '面向日常使用的能力卡片——点开启动器前，先知道它能帮你做什么。'
              : 'Plain-language cards for everyday use — what the launcher can do for you.'
          }}
        </p>
        <div class="cards">
          <article
            v-for="card in featureCards"
            :key="card.id"
            class="card"
            :data-accent="card.accent"
          >
            <div class="card-top">
              <span class="card-icon" aria-hidden="true">{{ card.icon }}</span>
              <h3>{{ t(card.name) }}</h3>
            </div>
            <p>{{ t(card.detail) }}</p>
          </article>
        </div>
      </section>

      <Screens :locale="locale" />

      <!-- Advanced: launch + check-in folds -->
      <section id="advanced" class="block advanced-block" aria-labelledby="advanced-heading">
        <h2 id="advanced-heading">{{ locale === 'zh' ? '进阶' : 'Advanced' }}</h2>
        <p class="section-lead">
          {{
            locale === 'zh'
              ? '配置如何落到快捷方式或 URL、如何跳过 UAC，以及签到在何时触发。'
              : 'How a profile becomes a shortcut or URL, how skip-UAC works, and when check-in runs.'
          }}
        </p>

        <details id="flow" ref="flowFoldRef" class="flow-fold">
          <summary>
            <span class="fold-chevron" aria-hidden="true">
              <svg viewBox="0 0 24 24" focusable="false">
                <path
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.75"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  d="M9 6.5 14.5 12 9 17.5"
                />
              </svg>
            </span>
            <span class="fold-copy">
              <span class="fold-title">{{ t(launchFlow.title) }}</span>
              <span class="fold-hint">{{
                locale === 'zh'
                  ? '配置 · 快捷方式 / URL · 免 UAC'
                  : 'Profile · shortcut / URL · skip UAC'
              }}</span>
            </span>
          </summary>
          <div class="fold-body">
            <p class="section-lead">{{ t(launchFlow.lead) }}</p>
            <div class="flow-board">
          <!-- Step rail -->
          <ol class="flow-rail" role="list">
            <li
              v-for="(step, i) in launchFlow.steps"
              :key="step.id"
              class="flow-rail-item"
              :class="{ active: activeStep === step.id }"
            >
              <button type="button" class="flow-rail-btn" @click="activeStep = step.id">
                <span class="flow-num">{{ i + 1 }}</span>
                <span class="flow-rail-label">{{ t(step.tag) }}</span>
              </button>
              <span v-if="i < launchFlow.steps.length - 1" class="flow-rail-line" aria-hidden="true" />
            </li>
          </ol>

          <!-- Visual pipeline -->
          <div class="flow-visual" aria-hidden="true">
            <div class="pipe-row">
              <div
                class="pipe-node config"
                :class="{ on: activeStep === 'config' }"
                @mouseenter="activeStep = 'config'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '配置文件' : 'Profile' }}</span>
                <span class="pipe-sub">game · args · account</span>
              </div>
            </div>

            <div class="pipe-join">
              <span class="pipe-v" />
              <span class="pipe-hint">{{ locale === 'zh' ? '导出入口' : 'Export entry' }}</span>
            </div>

            <div class="pipe-row split">
              <div
                class="pipe-node shortcut"
                :class="{ on: activeStep === 'entries' }"
                @mouseenter="activeStep = 'entries'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '桌面快捷方式' : 'Shortcut' }}</span>
                <span class="pipe-sub">{{ locale === 'zh' ? '.lnk · 可选免 UAC' : '.lnk · optional skip-UAC' }}</span>
              </div>
              <div class="pipe-or">{{ locale === 'zh' ? '或' : 'or' }}</div>
              <div
                class="pipe-node url"
                :class="{ on: activeStep === 'entries' }"
                @mouseenter="activeStep = 'entries'"
              >
                <span class="pipe-label">URL</span>
                <span class="pipe-sub mono">moonward://</span>
              </div>
            </div>

            <div class="pipe-join">
              <span class="pipe-v merge" />
            </div>

            <div class="pipe-row">
              <div
                class="pipe-node resolve"
                :class="{ on: activeStep === 'resolve' }"
                @mouseenter="activeStep = 'resolve'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '读取配置' : 'Resolve profile' }}</span>
                <span class="pipe-sub">Moonward</span>
              </div>
            </div>

            <div class="pipe-join">
              <span class="pipe-v" />
            </div>

            <div class="pipe-row">
              <div
                class="pipe-node checkin"
                :class="{ on: activeStep === 'checkin' }"
                @mouseenter="activeStep = 'checkin'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '自动签到' : 'Auto check-in' }}</span>
                <span class="pipe-sub">{{ locale === 'zh' ? '可选 · 绑定账号' : 'optional · bound account' }}</span>
              </div>
            </div>

            <div class="pipe-join">
              <span class="pipe-v" />
            </div>

            <div class="pipe-row">
              <div
                class="pipe-node game"
                :class="{ on: activeStep === 'game' }"
                @mouseenter="activeStep = 'game'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '启动游戏' : 'Launch game' }}</span>
                <span class="pipe-sub">{{ locale === 'zh' ? '客户端进程' : 'game process' }}</span>
              </div>
            </div>
          </div>

          <!-- Detail panel -->
          <div class="flow-detail">
            <template v-for="step in launchFlow.steps" :key="step.id">
              <div v-show="activeStep === step.id" class="detail-panel">
                <p class="detail-tag mono">{{ t(step.tag) }}</p>
                <h3>{{ t(step.title) }}</h3>
                <p class="detail-desc">{{ t(step.desc) }}</p>
                <div v-if="step.branches" class="branches">
                  <div v-for="b in step.branches" :key="b.id" class="branch">
                    <strong>{{ t(b.title) }}</strong>
                    <p>{{ t(b.desc) }}</p>
                  </div>
                </div>
                <p v-if="step.id === 'entries'" class="url-sample mono">
                  {{ launchFlow.urlExample }}
                </p>
              </div>
            </template>
          </div>
        </div>

        <!-- Compact path summary for scanability -->
        <div class="path-summary">
          <div class="path-item">
            <span class="path-key">{{ locale === 'zh' ? '配置文件' : 'Profile' }}</span>
            <span class="path-val">{{
              locale === 'zh'
                ? '游戏 + 启动参数 + 绑定账号，可多套并存'
                : 'Game + args + account; multiple per title'
            }}</span>
          </div>
          <div class="path-item">
            <span class="path-key">{{ locale === 'zh' ? '快捷方式' : 'Shortcut' }}</span>
            <span class="path-val">{{
              locale === 'zh'
                ? '桌面图标绑定配置；可勾选关闭 UAC'
                : 'Desktop icon; optional skip-UAC'
            }}</span>
          </div>
          <div class="path-item">
            <span class="path-key">URL</span>
            <span class="path-val mono">moonward:// → profile → game</span>
          </div>
          <div class="path-item">
            <span class="path-key">{{ locale === 'zh' ? '自动签到' : 'Check-in' }}</span>
            <span class="path-val">{{
              locale === 'zh'
                ? '开游戏顺带签；软件启动后还会批量签'
                : 'On launch, plus a batch after Moonward starts'
            }}</span>
          </div>
        </div>
          </div>
        </details>

        <details id="checkin" ref="checkinFoldRef" class="flow-fold checkin-tone">
          <summary>
            <span class="fold-chevron" aria-hidden="true">
              <svg viewBox="0 0 24 24" focusable="false">
                <path
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.75"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  d="M9 6.5 14.5 12 9 17.5"
                />
              </svg>
            </span>
            <span class="fold-copy">
              <span class="fold-title">{{ t(checkInFlow.title) }}</span>
              <span class="fold-hint">{{
                locale === 'zh'
                  ? '按游戏开关 · 启动后批量 · 开游戏顺带签'
                  : 'Per-game toggle · batch after start · on launch'
              }}</span>
            </span>
          </summary>
          <div class="fold-body">
            <p class="section-lead">{{ t(checkInFlow.lead) }}</p>
            <div class="flow-board">
          <ol class="flow-rail" role="list">
            <li
              v-for="(step, i) in checkInFlow.steps"
              :key="step.id"
              class="flow-rail-item"
              :class="{ active: activeCheckInStep === step.id }"
            >
              <button type="button" class="flow-rail-btn" @click="activeCheckInStep = step.id">
                <span class="flow-num">{{ i + 1 }}</span>
                <span class="flow-rail-label">{{ t(step.tag) }}</span>
              </button>
              <span v-if="i < checkInFlow.steps.length - 1" class="flow-rail-line" aria-hidden="true" />
            </li>
          </ol>

          <div class="flow-visual" aria-hidden="true">
            <div class="pipe-row">
              <div
                class="pipe-node enable"
                :class="{ on: activeCheckInStep === 'enable' }"
                @mouseenter="activeCheckInStep = 'enable'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '按游戏开启' : 'Enable per game' }}</span>
                <span class="pipe-sub">{{ locale === 'zh' ? '独立开关 · 下次启动生效' : 'per-game · next start' }}</span>
              </div>
            </div>

            <div class="pipe-join">
              <span class="pipe-v" />
              <span class="pipe-hint">{{ locale === 'zh' ? '两条路径' : 'Two paths' }}</span>
            </div>

            <div class="pipe-row split">
              <div
                class="pipe-node batch"
                :class="{ on: activeCheckInStep === 'paths' }"
                @mouseenter="activeCheckInStep = 'paths'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '启动后批量' : 'Batch after start' }}</span>
                <span class="pipe-sub">{{ locale === 'zh' ? '约 10 秒 · 依次签到' : '~10s · one by one' }}</span>
              </div>
              <div class="pipe-or">{{ locale === 'zh' ? '或' : 'or' }}</div>
              <div
                class="pipe-node launch-acc"
                :class="{ on: activeCheckInStep === 'paths' }"
                @mouseenter="activeCheckInStep = 'paths'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '开游戏顺带签' : 'On game launch' }}</span>
                <span class="pipe-sub">{{ locale === 'zh' ? '快捷方式 / URL / CLI' : 'shortcut / URL / CLI' }}</span>
              </div>
            </div>

            <div class="pipe-join">
              <span class="pipe-v merge" />
            </div>

            <div class="pipe-row">
              <div
                class="pipe-node claim"
                :class="{ on: activeCheckInStep === 'claim' }"
                @mouseenter="activeCheckInStep = 'claim'"
              >
                <span class="pipe-label">{{ locale === 'zh' ? '查询并签到' : 'Look up, then claim' }}</span>
                <span class="pipe-sub">{{ locale === 'zh' ? '已签则跳过 · 失败冷却' : 'skip if done · cooldown' }}</span>
              </div>
            </div>
          </div>

          <div class="flow-detail">
            <template v-for="step in checkInFlow.steps" :key="step.id">
              <div v-show="activeCheckInStep === step.id" class="detail-panel">
                <p class="detail-tag mono">{{ t(step.tag) }}</p>
                <h3>{{ t(step.title) }}</h3>
                <p class="detail-desc">{{ t(step.desc) }}</p>
                <div v-if="step.branches" class="branches">
                  <div v-for="b in step.branches" :key="b.id" class="branch">
                    <strong>{{ t(b.title) }}</strong>
                    <p>{{ t(b.desc) }}</p>
                  </div>
                </div>
              </div>
            </template>
          </div>
        </div>

        <div class="path-summary">
          <div class="path-item">
            <span class="path-key">{{ locale === 'zh' ? '按游戏开关' : 'Per-game toggle' }}</span>
            <span class="path-val">{{
              locale === 'zh' ? '各游戏互不影响，下次启动生效' : 'Independent; takes effect next start'
            }}</span>
          </div>
          <div class="path-item">
            <span class="path-key">{{ locale === 'zh' ? '启动后批量' : 'Batch after start' }}</span>
            <span class="path-val">{{
              locale === 'zh' ? '约十秒后依次签已开启的游戏' : 'Enabled games, one by one after ~10s'
            }}</span>
          </div>
          <div class="path-item">
            <span class="path-key">{{ locale === 'zh' ? '开游戏顺带签' : 'On launch' }}</span>
            <span class="path-val">{{
              locale === 'zh' ? '快捷方式 / URL / 命令行只签该账号' : 'Shortcut, URL, or CLI: that account only'
            }}</span>
          </div>
          <div class="path-item">
            <span class="path-key">{{ locale === 'zh' ? '失败冷却' : 'Cooldown' }}</span>
            <span class="path-val">{{
              locale === 'zh' ? '出错约十分钟内不再重试' : 'About 10 minutes after a failure'
            }}</span>
          </div>
        </div>
          </div>
        </details>
      </section>

      <!-- Install -->
      <section id="install" class="block install-block" aria-labelledby="install-heading">
        <div class="install-head">
          <div>
            <h2 id="install-heading">{{ locale === 'zh' ? '下载与安装' : 'Download & install' }}</h2>
            <p class="section-lead install-lead">
              {{
                locale === 'zh'
                  ? '直接下载安装包或便携版。可切换版本查看该版发布说明；下载线路可选 GitHub / CNB。多数电脑选 x64，Windows on ARM 选 ARM64。'
                  : 'Download Setup or Portable. Switch versions to read that release\'s notes; pick GitHub or CNB for the download channel. Most PCs use x64; Windows on ARM uses ARM64.'
              }}
            </p>
          </div>
          <div class="install-meta">
            <div class="channel-switch" role="group" :aria-label="locale === 'zh' ? '下载渠道' : 'Download channel'">
              <button
                v-for="ch in channelOptions"
                :key="ch.id"
                type="button"
                class="channel-btn"
                :class="{ active: downloadChannel === ch.id }"
                :aria-pressed="downloadChannel === ch.id"
                @click="setDownloadChannel(ch.id)"
              >
                {{ ch.label[locale] || ch.label.zh }}
              </button>
            </div>
          </div>
        </div>

        <div v-if="releaseLoading" class="dl-status" role="status">
          {{ locale === 'zh' ? '正在获取最新版本…' : 'Fetching latest release…' }}
        </div>

        <div v-else-if="releaseError" class="dl-status error">
          <p>
            {{
              locale === 'zh'
                ? '暂时无法读取安装包列表，请切换渠道重试，或打开对应 Releases 页面手动下载。'
                : 'Could not load packages. Try the other channel, or open the Releases page.'
            }}
          </p>
          <a class="btn" :href="releasesPageHref" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '打开 Releases' : 'Open Releases' }}
          </a>
        </div>

        <div v-else class="dl-section">
          <div class="ver-row">
            <label class="ver-picker">
              <span class="ver-picker-label">{{ locale === 'zh' ? '版本' : 'Version' }}</span>
              <select
                v-model="selectedTag"
                class="ver-select mono"
                :aria-label="locale === 'zh' ? '选择版本' : 'Choose a version'"
              >
                <option v-for="c in catalogs" :key="c.tag" :value="c.tag">
                  {{ optionLabel(c) }}
                </option>
              </select>
            </label>
            <span v-if="release?.prerelease" class="badge">
              {{ locale === 'zh' ? '预览版' : 'Pre-release' }}
            </span>
            <time v-if="publishedLabel" class="ver-date" :datetime="selectedCatalog?.publishedAt || undefined">
              {{ publishedLabel }}
            </time>
          </div>
          <div class="dl-grid">
            <article
              v-for="col in archColumns"
              :key="col.id"
              class="dl-card"
              :class="{ preferred: col.id === preferredArch }"
            >
              <header class="dl-card-head">
                <h3>{{ t(col.label) }}</h3>
                <span v-if="col.id === preferredArch" class="badge">
                  {{ locale === 'zh' ? '推荐' : 'Likely yours' }}
                </span>
              </header>
              <ul class="dl-list">
                <li v-for="kind in ['setup', 'portable']" :key="kind">
                  <template v-if="packageFor(col.id, kind)">
                    <a
                      class="dl-link"
                      :class="{ primary: kind === 'setup' }"
                      :href="packageFor(col.id, kind).url"
                      rel="noopener noreferrer"
                    >
                      <span class="dl-link-main">
                        <strong>{{ kindLabel(kind) }}</strong>
                        <span class="dl-file mono">{{ packageFor(col.id, kind).name }}</span>
                        <span class="dl-hint">{{ kindHint(kind) }}</span>
                      </span>
                      <span class="dl-size mono">{{ sizeLabel(packageFor(col.id, kind)) }}</span>
                    </a>
                  </template>
                  <template v-else>
                    <div class="dl-missing">
                      <strong>{{ kindLabel(kind) }}</strong>
                      <span>{{ locale === 'zh' ? '此版本暂无该包' : 'Not in this release' }}</span>
                    </div>
                  </template>
                </li>
              </ul>
            </article>
          </div>

          <article class="notes" :aria-label="locale === 'zh' ? '发布说明' : 'Release notes'">
            <header class="notes-head">
              <p class="notes-tag mono">{{ release?.tag }}</p>
            </header>
            <div v-if="notesHtml" class="notes-body" v-html="notesHtml" />
            <p v-else class="notes-empty">
              {{ locale === 'zh' ? '此版本没有发布说明。' : 'No release notes for this version.' }}
            </p>
          </article>
        </div>

        <div class="install-foot">
          <dl class="req">
            <template v-for="row in requirements" :key="row.label.zh">
              <dt>{{ t(row.label) }}</dt>
              <dd>{{ t(row.value) }}</dd>
            </template>
          </dl>
          <div class="install-cta">
            <a class="text-link" :href="selectedReleaseHref" target="_blank" rel="noopener noreferrer">
              {{
                locale === 'zh'
                  ? `在 ${activeChannelMeta.label.zh} 打开此版本`
                  : `Open this release on ${activeChannelMeta.label.en}`
              }}
            </a>
            <a class="text-link" :href="links.issues" target="_blank" rel="noopener noreferrer">
              Issues
            </a>
            <a class="text-link" :href="links.license" target="_blank" rel="noopener noreferrer">
              MIT License
            </a>
          </div>
        </div>
      </section>
    </main>

    <footer class="foot">
      <div class="wrap foot-inner">
        <p>
          {{
            locale === 'zh'
              ? 'Moonward / Starward 与 miHoYo / HoYoverse 无关联。游戏内容版权归原权利方。'
              : 'Moonward / Starward is not affiliated with miHoYo / HoYoverse. Game content belongs to their owners.'
          }}
        </p>
        <p class="meta mono">
          <a :href="links.license" target="_blank" rel="noopener noreferrer">MIT</a>
          ·
          <a :href="links.github" target="_blank" rel="noopener noreferrer">TurmoilZoom/Moonward</a>
        </p>
      </div>
    </footer>

    <div class="sky-play" :class="skyPlay" aria-hidden="true">
      <div class="sky-veil" />
      <div class="sky-glow" />
      <div class="sky-disc" />
    </div>

    <button
      type="button"
      class="back-top"
      :class="{ show: showBackTop }"
      :aria-label="locale === 'zh' ? '回到顶部' : 'Back to top'"
      :tabindex="showBackTop ? 0 : -1"
      @click="scrollToTop"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <path
          fill="none"
          stroke="currentColor"
          stroke-width="1.75"
          stroke-linecap="round"
          stroke-linejoin="round"
          d="M6.5 14.5 12 9l5.5 5.5"
        />
      </svg>
    </button>
  </div>
</template>

<style scoped>
.page {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

/* —— Header —— */
.top {
  border-bottom: 1px solid var(--line);
  background: color-mix(in srgb, var(--bg-raised) 90%, transparent);
  backdrop-filter: blur(10px);
  position: sticky;
  top: 0;
  z-index: 20;
}

.top-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  min-height: 3.1rem;
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--ink);
  text-decoration: none;
  font-family: var(--font-sans);
  font-weight: 600;
  font-size: 0.95rem;
}

.brand img {
  width: 28px;
  height: 28px;
  border-radius: 7px;
  border: 1px solid var(--line);
}

.nav {
  display: flex;
  align-items: center;
  gap: 0.15rem 1.05rem;
  flex-wrap: wrap;
  justify-content: flex-end;
  font-family: var(--font-sans);
}

.nav a {
  color: var(--ink-2);
  text-decoration: none;
  font-size: 0.88rem;
}

.nav a:hover {
  color: var(--ink);
  text-decoration: underline;
}

.lang,
.theme-btn {
  font-family: var(--font-mono);
  font-size: 0.72rem;
  padding: 0.22rem 0.5rem;
  border: 1px solid var(--line-strong);
  border-radius: 4px;
  color: var(--muted);
}

.theme-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.85rem;
  height: 1.85rem;
  padding: 0;
}

.theme-sky {
  position: relative;
  width: 0.95rem;
  height: 0.95rem;
  overflow: hidden;
}

.celestial {
  position: absolute;
  inset: 0;
  width: 0.95rem;
  height: 0.95rem;
  display: block;
}

.celestial.sun {
  transform: translateY(0);
}

.celestial.moon {
  transform: translateY(130%);
}

.theme-btn.is-dark .celestial.sun {
  transform: translateY(130%);
}

.theme-btn.is-dark .celestial.moon {
  transform: translateY(0);
}

.theme-btn.armed .celestial {
  transition: transform 0.75s cubic-bezier(0.22, 1, 0.36, 1);
}

.lang:hover,
.theme-btn:hover {
  color: var(--ink);
  border-color: var(--ink-2);
}

.theme-btn:focus-visible,
.lang:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

/* —— Hero + parallax —— */
.hero {
  position: relative;
  overflow: hidden;
  border-bottom: 1px solid var(--line);
  background:
    linear-gradient(165deg, var(--hero-grad-start) 0%, var(--bg) 45%, var(--hero-grad-end) 100%);
  min-height: 22rem;
  display: flex;
  align-items: center;
  padding: 2.25rem 0 2.5rem;
}

.hero-layers {
  position: absolute;
  inset: 0;
  pointer-events: none;
  overflow: hidden;
}

.layer {
  position: absolute;
  will-change: transform;
  transition: none;
}

/* 夜空底：月侧柔光 + 对角轻辉，铺满并随最浅深度微移 */
.layer-sky {
  inset: -8%;
  background:
    radial-gradient(circle at 78% 18%, var(--sky-wash), transparent 52%),
    radial-gradient(circle at 18% 92%, var(--sky-wash), transparent 60%);
}

/* 星空：叠层 radial-gradient 画点，GPU 友好；远近两套疏密不同 */
.layer-stars {
  inset: -8%;
  background-repeat: no-repeat;
}

.stars-far {
  opacity: 0.75;
  background-image:
    radial-gradient(1px 1px at 12% 18%, var(--star-color), transparent 60%),
    radial-gradient(1px 1px at 24% 62%, var(--star-color), transparent 60%),
    radial-gradient(1.4px 1.4px at 41% 30%, var(--star-color), transparent 60%),
    radial-gradient(1px 1px at 57% 12%, var(--star-color), transparent 60%),
    radial-gradient(1px 1px at 66% 48%, var(--star-color), transparent 60%),
    radial-gradient(1px 1px at 83% 66%, var(--star-color), transparent 60%),
    radial-gradient(1.4px 1.4px at 90% 26%, var(--star-color), transparent 60%),
    radial-gradient(1px 1px at 34% 82%, var(--star-color), transparent 60%),
    radial-gradient(1px 1px at 6% 44%, var(--star-color), transparent 60%),
    radial-gradient(1px 1px at 50% 74%, var(--star-color), transparent 60%);
}

.stars-near {
  opacity: 0.9;
  background-image:
    radial-gradient(1.5px 1.5px at 16% 40%, var(--star-color), transparent 60%),
    radial-gradient(1.6px 1.6px at 46% 22%, var(--star-color), transparent 60%),
    radial-gradient(2px 2px at 73% 36%, var(--star-color), transparent 60%),
    radial-gradient(1.6px 1.6px at 61% 78%, var(--star-color), transparent 60%),
    radial-gradient(1.5px 1.5px at 30% 66%, var(--star-color), transparent 60%);
}

/* 少数会呼吸闪烁的亮星，各自错峰 */
.twinkle {
  position: absolute;
  width: 3px;
  height: 3px;
  border-radius: 50%;
  background: var(--star-color);
  box-shadow: 0 0 6px 1px var(--star-color);
  animation: twinkle 3.4s ease-in-out infinite;
}

.twinkle.t1 { top: 20%; left: 32%; animation-delay: 0s; }
.twinkle.t2 { top: 52%; left: 70%; animation-delay: 1.2s; }
.twinkle.t3 { top: 74%; left: 52%; animation-delay: 2.3s; }

@keyframes twinkle {
  0%, 100% { opacity: 0.25; transform: scale(0.75); }
  50% { opacity: 1; transform: scale(1.15); }
}

/* 偶发流星：约 9s 一次斜向划过，浅色主题下 star-color 很淡近乎不可见 */
.layer-shoot {
  inset: 0;
  overflow: hidden;
}

.shoot {
  position: absolute;
  top: 14%;
  left: 68%;
  width: 8rem;
  height: 1px;
  background: linear-gradient(90deg, transparent, var(--star-color));
  opacity: 0;
  transform: rotate(18deg) scaleX(0.2);
  transform-origin: right center;
  animation: shoot 9s ease-in infinite;
}

@keyframes shoot {
  0%, 90% { opacity: 0; transform: translate(0, 0) rotate(18deg) scaleX(0.2); }
  91% { opacity: 0.9; }
  100% { opacity: 0; transform: translate(-15rem, 5rem) rotate(18deg) scaleX(1); }
}

/* 月晕：环绕月亮的两道柔和同心光圈 */
.layer-halo {
  width: 26rem;
  height: 26rem;
  top: -8rem;
  right: 0.5%;
  border-radius: 50%;
  opacity: 0.85;
  background: radial-gradient(
    circle,
    transparent 39%,
    var(--halo-ring) 45%,
    transparent 53%,
    transparent 61%,
    var(--halo-ring) 67%,
    transparent 75%
  );
}

/* 月亮：月面（环形山 + 柔边）+ 光晕；明暗界线在 moon-shade 上随鼠标偏移 */
.layer-moon {
  width: 15rem;
  height: 15rem;
  top: -2.5rem;
  right: 7%;
}

.moon-disc,
.moon-shade {
  position: absolute;
  inset: 0;
  border-radius: 50%;
}

.moon-disc {
  background:
    radial-gradient(circle at 62% 32%, var(--moon-shade) 0 7%, transparent 8%),
    radial-gradient(circle at 38% 60%, var(--moon-shade) 0 5%, transparent 6%),
    radial-gradient(circle at 70% 67%, var(--moon-shade) 0 4%, transparent 5%),
    radial-gradient(circle at 50% 45%, var(--moon-shade) 0 3%, transparent 4%),
    var(--moon-face);
  box-shadow:
    0 0 4rem 0.7rem var(--moon-glow),
    inset -0.45rem -0.35rem 1.3rem var(--moon-shade);
}

/* 透明处即受光面：中心随 --moon-px/py 朝光标方向移动，背光侧堆积阴影 */
.moon-shade {
  background: radial-gradient(
    circle at calc(50% + var(--moon-px, 0) * 60%) calc(50% + var(--moon-py, 0) * 60%),
    transparent 34%,
    var(--moon-shade) 96%
  );
}

/* 前景云雾：飘动幅度最大（parallax 深度最深），模糊柔和 */
.layer-cloud {
  border-radius: 50%;
  filter: blur(9px);
  opacity: 0.55;
  background: radial-gradient(ellipse 60% 100% at 50% 50%, var(--cloud-tint), transparent 72%);
}

.cloud-a {
  width: 20rem;
  height: 6rem;
  top: 36%;
  left: -3rem;
}

.cloud-b {
  width: 14rem;
  height: 4.5rem;
  bottom: 12%;
  right: 6%;
}

.hero-content {
  position: relative;
  z-index: 2;
  will-change: transform;
}

.kicker {
  font-family: var(--font-sans);
  font-size: 0.75rem;
  color: var(--muted);
  margin-bottom: 0.85rem;
  letter-spacing: 0.04em;
}

.hero-title-row {
  display: flex;
  align-items: center;
  gap: 0.85rem;
  margin-bottom: 0.85rem;
}

.hero-logo {
  width: 56px;
  height: 56px;
  border-radius: 14px;
  border: 1px solid var(--line);
  box-shadow: var(--shadow);
  will-change: transform;
}

.lede {
  max-width: 40rem;
  color: var(--ink-2);
  font-size: 1.05rem;
  line-height: 1.75;
}

.games {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem 0.4rem;
  margin-top: 1rem;
}

.games li {
  font-family: var(--font-sans);
  font-size: 0.8rem;
  padding: 0.22rem 0.65rem;
  border: 1px solid var(--line);
  border-radius: 999px;
  background: color-mix(in srgb, var(--bg-card) 80%, transparent);
  color: var(--ink);
  backdrop-filter: blur(4px);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.55rem 0.9rem;
  margin-top: 1.25rem;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0.5rem 1.05rem;
  border-radius: 6px;
  background: var(--accent);
  color: var(--on-accent);
  text-decoration: none;
  font-family: var(--font-sans);
  font-size: 0.9rem;
  font-weight: 600;
  transition: background 0.15s ease, transform 0.15s ease;
}

.btn:hover {
  color: var(--on-accent);
  background: var(--accent-hover);
}

.btn.large {
  padding: 0.7rem 1.35rem;
  font-size: 0.95rem;
}

.btn.ghost {
  background: color-mix(in srgb, var(--bg-card) 70%, transparent);
  color: var(--ink);
  border: 1px solid var(--line-strong);
  backdrop-filter: blur(4px);
}

.btn.ghost:hover {
  border-color: var(--ink-2);
  background: var(--bg-card);
}

.text-link {
  font-family: var(--font-sans);
  font-size: 0.88rem;
}

/* —— Main —— */
.main {
  flex: 1;
  padding: 1.75rem 0 2.5rem;
}

.block {
  margin-bottom: 2rem;
  padding: 1.35rem 1.4rem 1.5rem;
  background: var(--bg-card);
  border: 1px solid var(--line);
  border-radius: var(--radius);
  box-shadow: var(--shadow-sm);
  scroll-margin-top: 4.25rem;
}

/* —— Feature cards —— */
.cards {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.75rem;
}

.card {
  padding: 0.95rem 1rem 1.05rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
  transition: border-color 0.18s ease, box-shadow 0.18s ease, transform 0.18s ease;
}

.card:hover {
  border-color: color-mix(in srgb, var(--card-accent, var(--accent)) 45%, var(--line));
  box-shadow: var(--shadow);
  transform: translateY(-2px);
}

.card[data-accent='teal'] { --card-accent: var(--teal); }
.card[data-accent='amber'] { --card-accent: var(--amber); }
.card[data-accent='blue'] { --card-accent: var(--blue); }
.card[data-accent='green'] { --card-accent: var(--green); }
.card[data-accent='violet'] { --card-accent: var(--violet); }
.card[data-accent='rose'] { --card-accent: var(--rose); }
.card[data-accent='cyan'] { --card-accent: var(--cyan); }
.card[data-accent='indigo'] { --card-accent: var(--indigo); }
.card[data-accent='slate'] { --card-accent: var(--slate); }

.card-top {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.45rem;
}

.card-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.7rem;
  height: 1.7rem;
  border-radius: 6px;
  font-size: 0.85rem;
  background: color-mix(in srgb, var(--card-accent, var(--accent)) 12%, var(--bg));
  color: var(--card-accent, var(--accent));
  flex-shrink: 0;
}

.card h3 {
  font-family: var(--font-sans);
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--ink);
}

.card p {
  font-size: 0.88rem;
  line-height: 1.55;
  color: var(--ink-2);
}

/* —— Advanced folds —— */
.flow-fold {
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: linear-gradient(180deg, var(--bg-raised) 0%, var(--flow-end) 100%);
  scroll-margin-top: 4.25rem;
}

.flow-fold + .flow-fold {
  margin-top: 0.65rem;
}

.flow-fold.checkin-tone {
  background: linear-gradient(180deg, var(--bg-raised) 0%, var(--checkin-end) 100%);
}

.flow-fold > summary {
  display: flex;
  align-items: center;
  gap: 0.7rem;
  padding: 0.8rem 0.95rem;
  cursor: pointer;
  list-style: none;
  user-select: none;
}

.flow-fold > summary::-webkit-details-marker {
  display: none;
}

.flow-fold > summary:hover .fold-title {
  color: var(--accent);
}

.flow-fold > summary:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
  border-radius: var(--radius-sm);
}

.fold-chevron {
  flex-shrink: 0;
  width: 1.35rem;
  height: 1.35rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--accent);
  transition: transform 0.2s ease;
}

.fold-chevron svg {
  width: 0.95rem;
  height: 0.95rem;
  display: block;
}

.flow-fold[open] > summary .fold-chevron {
  transform: rotate(90deg);
}

.fold-copy {
  display: flex;
  flex-direction: column;
  gap: 0.12rem;
  min-width: 0;
}

.fold-title {
  font-family: var(--font-serif);
  font-size: 1.05rem;
  font-weight: 600;
  color: var(--ink);
}

.fold-hint {
  font-family: var(--font-sans);
  font-size: 0.8rem;
  color: var(--muted);
}

.fold-body {
  padding: 0.95rem 0.95rem 1.05rem;
  border-top: 1px solid var(--line);
}

.fold-body > .section-lead {
  margin-bottom: 0.95rem;
}

.flow-board {
  display: grid;
  grid-template-columns: 1fr 1.15fr;
  gap: 1rem 1.25rem;
  align-items: start;
}

.flow-rail {
  grid-column: 1 / -1;
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  margin-bottom: 0.25rem;
}

.flow-rail-item {
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.flow-rail-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.35rem 0.7rem 0.35rem 0.4rem;
  border-radius: 999px;
  border: 1px solid var(--line);
  background: var(--bg-raised);
  font-family: var(--font-sans);
  font-size: 0.8rem;
  color: var(--ink-2);
  transition: border-color 0.15s, background 0.15s, color 0.15s;
}

.flow-rail-item.active .flow-rail-btn {
  border-color: var(--accent);
  background: var(--accent-soft);
  color: var(--accent);
  font-weight: 600;
}

.flow-num {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.35rem;
  height: 1.35rem;
  border-radius: 50%;
  background: var(--code-bg);
  font-family: var(--font-mono);
  font-size: 0.7rem;
  font-weight: 500;
}

.flow-rail-item.active .flow-num {
  background: var(--accent);
  color: var(--on-accent);
}

.flow-rail-line {
  display: none;
}

/* Visual pipeline */
.flow-visual {
  padding: 1rem 0.85rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
}

.pipe-row {
  display: flex;
  justify-content: center;
  align-items: stretch;
  gap: 0.5rem;
}

.pipe-row.split {
  align-items: center;
}

.pipe-node {
  flex: 1;
  max-width: 14rem;
  padding: 0.7rem 0.8rem;
  border-radius: var(--radius-sm);
  border: 1.5px solid var(--line);
  background: var(--bg-card);
  text-align: center;
  cursor: default;
  transition: border-color 0.18s, box-shadow 0.18s, background 0.18s;
}

.pipe-node.on {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px var(--accent-soft);
  background: var(--surface-hover);
}

.pipe-node.shortcut.on,
.pipe-node.url.on {
  border-color: var(--amber);
  box-shadow: 0 0 0 3px var(--glow-amber);
}

.pipe-node.checkin.on,
.pipe-node.claim.on,
.pipe-node.batch.on {
  border-color: var(--green);
  box-shadow: 0 0 0 3px var(--glow-green);
}

.pipe-node.enable.on {
  border-color: var(--teal);
  box-shadow: 0 0 0 3px var(--accent-soft);
}

.pipe-node.launch-acc.on {
  border-color: var(--amber);
  box-shadow: 0 0 0 3px var(--glow-amber);
}

.pipe-node.game.on {
  border-color: var(--blue);
  box-shadow: 0 0 0 3px var(--glow-blue);
}

.pipe-label {
  display: block;
  font-family: var(--font-sans);
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--ink);
}

.pipe-sub {
  display: block;
  margin-top: 0.2rem;
  font-size: 0.72rem;
  color: var(--muted);
  font-family: var(--font-sans);
}

.pipe-or {
  font-family: var(--font-sans);
  font-size: 0.72rem;
  color: var(--muted);
  flex-shrink: 0;
}

.pipe-join {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 0.15rem 0;
  min-height: 1.5rem;
  position: relative;
}

.pipe-v {
  width: 2px;
  height: 1.15rem;
  background: var(--line-strong);
  border-radius: 1px;
}

.pipe-v.merge {
  height: 1.35rem;
  background:
    linear-gradient(var(--line-strong), var(--line-strong)) center / 2px 100% no-repeat;
}

.pipe-hint {
  font-family: var(--font-sans);
  font-size: 0.68rem;
  color: var(--muted);
  background: var(--bg-raised);
  padding: 0 0.4rem;
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  white-space: nowrap;
  border: 1px solid var(--line);
  border-radius: 999px;
}

/* Detail */
.flow-detail {
  padding: 1.1rem 1.15rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-card);
  min-height: 14rem;
}

.detail-tag {
  font-size: 0.72rem;
  color: var(--accent);
  letter-spacing: 0.06em;
  text-transform: uppercase;
  margin-bottom: 0.4rem;
}

.detail-panel h3 {
  font-size: 1.15rem;
  margin-bottom: 0.55rem;
}

.detail-desc {
  font-size: 0.95rem;
  line-height: 1.7;
  color: var(--ink-2);
}

.branches {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.65rem;
  margin-top: 0.9rem;
}

.branch {
  padding: 0.65rem 0.75rem;
  border-radius: 6px;
  border: 1px solid var(--line);
  background: var(--bg-raised);
}

.branch strong {
  display: block;
  font-family: var(--font-sans);
  font-size: 0.88rem;
  color: var(--ink);
  margin-bottom: 0.25rem;
}

.branch p {
  font-size: 0.82rem;
  line-height: 1.5;
  color: var(--muted);
}

.url-sample {
  margin-top: 0.9rem;
  padding: 0.55rem 0.7rem;
  border-radius: 6px;
  background: var(--code-bg);
  font-size: 0.78rem;
  color: var(--ink-2);
  word-break: break-all;
  border: 1px solid var(--line);
}

/* Path summary */
.path-summary {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.6rem;
  margin-top: 1rem;
}

.path-item {
  padding: 0.7rem 0.8rem;
  border-radius: var(--radius-sm);
  border: 1px dashed var(--line-strong);
  background: color-mix(in srgb, var(--bg-raised) 70%, transparent);
}

.path-key {
  display: block;
  font-family: var(--font-sans);
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--accent);
  margin-bottom: 0.25rem;
}

.path-val {
  display: block;
  font-size: 0.82rem;
  line-height: 1.45;
  color: var(--ink-2);
}

/* —— Install —— */
/* main 内最后一块：去掉 .block 的 margin-bottom，贴合页脚间距 */
.install-block {
  margin-bottom: 0;
  /* 左侧文案↔卡片 与 版本胶囊↔卡片 共用，改一处即可 */
  --install-stack-gap: 1rem;
}

.install-head {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 0.75rem 1.25rem;
  align-items: flex-start;
  margin-bottom: var(--install-stack-gap);
}

.install-lead {
  margin-bottom: 0;
  max-width: 36rem;
}

.install-meta {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  font-family: var(--font-sans);
}

.channel-switch {
  display: inline-flex;
  padding: 0.18rem;
  border-radius: 999px;
  border: 1px solid var(--line-strong);
  background: var(--bg-raised);
  gap: 0.15rem;
}

.channel-btn {
  font-family: var(--font-sans);
  font-size: 0.82rem;
  font-weight: 600;
  padding: 0.32rem 0.85rem;
  border-radius: 999px;
  color: var(--muted);
  transition: background 0.15s ease, color 0.15s ease, box-shadow 0.15s ease;
}

.channel-btn:hover {
  color: var(--ink);
}

.channel-btn.active {
  color: var(--on-accent);
  background: var(--accent);
  box-shadow: var(--shadow-sm);
}

.dl-section {
  position: relative;
}

.ver-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem 0.85rem;
  margin-bottom: var(--install-stack-gap);
}

.ver-picker {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
}

.ver-picker-label {
  font-family: var(--font-sans);
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--ink);
}

.ver-select {
  appearance: none;
  color-scheme: inherit;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--accent);
  padding: 0.32rem 1.9rem 0.32rem 0.75rem;
  border-radius: 999px;
  border: 1px solid color-mix(in srgb, var(--accent) 22%, var(--line));
  background-color: var(--accent-soft);
  background-image: var(--select-caret);
  background-repeat: no-repeat;
  background-position: right 0.65rem center;
  background-size: 0.75rem;
  cursor: pointer;
  max-width: min(100%, 22rem);
}

.ver-select:hover,
.ver-select:focus-visible {
  border-color: var(--accent);
}

.ver-select:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.ver-date {
  font-family: var(--font-sans);
  font-size: 0.82rem;
  color: var(--muted);
}

.notes {
  margin: 0 0 1.15rem;
  padding: 1rem 1.1rem 1.15rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
}

.notes-head {
  display: flex;
  justify-content: center;
  margin-bottom: 0.7rem;
}

.notes-tag {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--accent);
}

.notes-empty {
  font-family: var(--font-sans);
  font-size: 0.9rem;
  color: var(--muted);
}

.notes-body {
  font-size: 0.92rem;
  line-height: 1.65;
  color: var(--ink-2);
}

.notes-body :deep(:first-child) {
  margin-top: 0;
}

.notes-body :deep(h1),
.notes-body :deep(h2),
.notes-body :deep(h3),
.notes-body :deep(h4) {
  font-family: var(--font-sans);
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--ink);
  margin: 0.95rem 0 0.35rem;
  letter-spacing: 0;
}

.notes-body :deep(ul),
.notes-body :deep(ol) {
  margin: 0 0 0.55rem;
  padding-left: 1.2rem;
  list-style: disc;
}

.notes-body :deep(ol) {
  list-style: decimal;
}

.notes-body :deep(li) {
  margin: 0.22rem 0;
}

.notes-body :deep(li + li) {
  margin-top: 0.35rem;
}

.notes-body :deep(p) {
  margin: 0 0 0.5rem;
}

.notes-body :deep(a) {
  color: var(--accent);
}

.notes-body :deep(hr) {
  border: 0;
  border-top: 1px solid var(--line);
  margin: 0.85rem 0;
}

.notes-body :deep(code) {
  font-family: var(--font-mono);
  font-size: 0.86em;
  padding: 0.05em 0.35em;
  border-radius: 4px;
  background: var(--code-bg);
}

.notes-body :deep(strong) {
  color: var(--ink);
  font-weight: 600;
}

.dl-status {
  padding: 1.1rem 1.15rem;
  border-radius: var(--radius-sm);
  border: 1px dashed var(--line-strong);
  background: var(--bg-raised);
  font-family: var(--font-sans);
  font-size: 0.92rem;
  color: var(--ink-2);
  margin-bottom: 1rem;
}

.dl-status.error {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem 1rem;
  border-style: solid;
  border-color: color-mix(in srgb, var(--rose) 35%, var(--line));
  background: color-mix(in srgb, var(--rose) 6%, var(--bg-card));
}

.dl-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.85rem;
  margin-bottom: 1.15rem;
}

.dl-card {
  padding: 0.95rem 1rem 1.05rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.dl-card.preferred {
  border-color: color-mix(in srgb, var(--accent) 45%, var(--line));
  box-shadow: 0 0 0 3px var(--accent-soft);
  background: var(--bg-card);
}

.dl-card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.dl-card-head h3 {
  font-family: var(--font-sans);
  font-size: 1rem;
  font-weight: 600;
}

.badge {
  font-family: var(--font-sans);
  font-size: 0.68rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--accent);
  background: var(--accent-soft);
  border-radius: 999px;
  padding: 0.18rem 0.5rem;
}

.dl-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.dl-link {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.7rem 0.8rem;
  border-radius: 8px;
  border: 1px solid var(--line);
  background: var(--bg-card);
  text-decoration: none;
  color: inherit;
  transition: border-color 0.15s ease, background 0.15s ease, transform 0.15s ease;
}

.dl-link:hover {
  border-color: var(--accent);
  background: var(--surface-hover);
  color: var(--ink);
  transform: translateY(-1px);
}

.dl-link.primary {
  border-color: color-mix(in srgb, var(--accent) 40%, var(--line));
  background: color-mix(in srgb, var(--accent-soft) 65%, var(--bg-card));
}

.dl-link.primary:hover {
  background: var(--accent-soft);
}

.dl-link-main {
  display: flex;
  flex-direction: column;
  gap: 0.12rem;
  min-width: 0;
}

.dl-link-main strong {
  font-family: var(--font-sans);
  font-size: 0.92rem;
  color: var(--ink);
}

.dl-file {
  font-size: 0.72rem;
  color: var(--muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 16rem;
}

.dl-hint {
  font-family: var(--font-sans);
  font-size: 0.75rem;
  color: var(--ink-2);
}

.dl-size {
  flex-shrink: 0;
  font-size: 0.78rem;
  color: var(--muted);
}

.dl-missing {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  padding: 0.7rem 0.8rem;
  border-radius: 8px;
  border: 1px dashed var(--line);
  color: var(--muted);
  font-family: var(--font-sans);
  font-size: 0.82rem;
}

.dl-missing strong {
  color: var(--ink-2);
  font-size: 0.9rem;
}

.install-foot {
  display: grid;
  grid-template-columns: 1.4fr 0.9fr;
  gap: 1rem 1.25rem;
  align-items: end;
  padding-top: 0.35rem;
  border-top: 1px solid var(--line);
}

.req {
  display: grid;
  grid-template-columns: 4.5rem 1fr;
  gap: 0.45rem 0.85rem;
  margin: 0;
}

.req dt {
  margin: 0;
  font-family: var(--font-sans);
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--ink);
}

.req dd {
  margin: 0;
  font-size: 0.9rem;
  color: var(--ink-2);
}

.install-cta {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.45rem;
}

/* —— Footer —— */
.foot {
  border-top: 1px solid var(--line);
  padding: 1.1rem 0 1.4rem;
  font-size: 0.82rem;
  color: var(--muted);
  margin-top: 1.5rem;
}

.foot-inner {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 0.5rem 1.5rem;
  align-items: baseline;
}

.meta a {
  color: var(--muted);
  text-decoration: none;
}

.meta a:hover {
  color: var(--ink);
  text-decoration: underline;
}

/* —— Theme: sun rise / moon fall —— */
.sky-play {
  position: fixed;
  inset: 0;
  z-index: 40;
  pointer-events: none;
  overflow: hidden;
  opacity: 0;
  visibility: hidden;
}

.sky-play.to-light,
.sky-play.to-dark {
  visibility: visible;
  animation: sky-play-hold 0.92s ease both;
}

.sky-veil,
.sky-glow,
.sky-disc {
  position: absolute;
}

.sky-veil {
  inset: 0;
}

.sky-play.to-light .sky-veil {
  background: var(--theme-light-bg);
  animation: sky-veil-rise 0.72s cubic-bezier(0.22, 1, 0.36, 1) forwards;
}

.sky-play.to-dark .sky-veil {
  background: var(--theme-dark-bg);
  animation: sky-veil-fall 0.72s cubic-bezier(0.22, 1, 0.36, 1) forwards;
}

.sky-glow {
  left: 0;
  right: 0;
  height: 42%;
  opacity: 0;
}

.sky-play.to-light .sky-glow {
  bottom: 0;
  background: radial-gradient(ellipse 80% 100% at 50% 100%, rgba(226, 184, 90, 0.38), transparent 72%);
  animation: sky-glow-in 0.92s ease forwards;
}

.sky-play.to-dark .sky-glow {
  top: 0;
  background: radial-gradient(ellipse 80% 100% at 50% 0%, rgba(214, 206, 194, 0.16), transparent 72%);
  animation: sky-glow-in 0.92s ease forwards;
}

.sky-disc {
  width: 4.4rem;
  height: 4.4rem;
  margin-left: -2.2rem;
  border-radius: 50%;
  opacity: 0;
}

.sky-play.to-light .sky-disc {
  background:
    radial-gradient(circle at 34% 32%, #fff8e6 0%, #f0d48a 42%, #d4a24a 78%);
  box-shadow: 0 0 2.4rem 0.55rem rgba(212, 164, 92, 0.32);
  animation: sky-sun-rise 0.92s cubic-bezier(0.22, 1, 0.36, 1) forwards;
}

.sky-play.to-dark .sky-disc {
  background:
    radial-gradient(circle at 38% 34%, #f7f2ea 0%, #d8d0c4 55%, #b8aea0 100%);
  box-shadow:
    inset -0.7rem 0 0 0 rgba(22, 20, 16, 0.28),
    0 0 1.8rem 0.35rem rgba(243, 238, 230, 0.12);
  animation: sky-moon-fall 0.92s cubic-bezier(0.22, 1, 0.36, 1) forwards;
}

@keyframes sky-play-hold {
  0%,
  78% {
    opacity: 1;
  }
  100% {
    opacity: 0;
  }
}

@keyframes sky-veil-rise {
  from {
    clip-path: inset(100% 0 0 0);
  }
  to {
    clip-path: inset(0);
  }
}

@keyframes sky-veil-fall {
  from {
    clip-path: inset(0 0 100% 0);
  }
  to {
    clip-path: inset(0);
  }
}

@keyframes sky-glow-in {
  0% {
    opacity: 0;
  }
  22% {
    opacity: 1;
  }
  100% {
    opacity: 0;
  }
}

@keyframes sky-sun-rise {
  0% {
    top: 108%;
    left: 40%;
    opacity: 0;
    transform: scale(0.72);
  }
  16% {
    opacity: 1;
  }
  62% {
    top: 28%;
    left: 50%;
    opacity: 1;
    transform: scale(1);
  }
  100% {
    top: 16%;
    left: 56%;
    opacity: 0;
    transform: scale(1.06);
  }
}

@keyframes sky-moon-fall {
  0% {
    top: -20%;
    left: 58%;
    opacity: 0;
    transform: scale(0.8);
  }
  16% {
    opacity: 1;
  }
  62% {
    top: 36%;
    left: 50%;
    opacity: 1;
    transform: scale(1);
  }
  100% {
    top: 58%;
    left: 42%;
    opacity: 0;
    transform: scale(0.78);
  }
}

/* —— Back to top —— */
.back-top {
  position: fixed;
  right: 1.25rem;
  bottom: 1.25rem;
  z-index: 30;
  width: 2.6rem;
  height: 2.6rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--radius-sm);
  border: 1px solid var(--line-strong);
  background: color-mix(in srgb, var(--bg-card) 88%, transparent);
  color: var(--accent);
  box-shadow: var(--shadow);
  backdrop-filter: blur(10px);
  opacity: 0;
  visibility: hidden;
  pointer-events: none;
  transform: translateY(0.35rem);
  transition:
    opacity 0.2s ease,
    transform 0.2s ease,
    visibility 0.2s ease,
    border-color 0.18s ease,
    background 0.18s ease,
    color 0.18s ease;
}

.back-top.show {
  opacity: 1;
  visibility: visible;
  pointer-events: auto;
  transform: none;
}

.back-top:hover {
  color: var(--accent-hover);
  border-color: var(--accent);
  background: var(--bg-card);
}

.back-top:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.back-top svg {
  width: 1.15rem;
  height: 1.15rem;
  display: block;
}

@media (prefers-reduced-motion: reduce) {
  .sky-play {
    display: none;
  }

  .twinkle,
  .shoot {
    animation: none;
  }

  .twinkle {
    opacity: 0.7;
  }

  .shoot {
    opacity: 0;
  }

  .theme-btn .celestial {
    transition: none;
  }

  .back-top {
    transform: none;
    transition: opacity 0.15s ease, visibility 0.15s ease;
  }
}

/* —— Responsive —— */
@media (max-width: 1100px) {
  .cards {
    grid-template-columns: repeat(2, 1fr);
  }

  .path-summary {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 820px) {
  .flow-board {
    grid-template-columns: 1fr;
  }

  .dl-grid,
  .install-foot {
    grid-template-columns: 1fr;
  }

  .install-meta {
    align-items: flex-start;
  }

  .install-cta {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.65rem 1rem;
  }

  .dl-file {
    max-width: 100%;
  }
}

@media (max-width: 560px) {
  .cards {
    grid-template-columns: 1fr;
  }

  .path-summary {
    grid-template-columns: 1fr;
  }

  .branches {
    grid-template-columns: 1fr;
  }

  .pipe-row.split {
    flex-direction: column;
  }

  .pipe-node {
    max-width: none;
    width: 100%;
  }

  .hero {
    min-height: auto;
    padding: 1.75rem 0 2rem;
  }

  .block {
    padding: 1.1rem 1rem 1.2rem;
  }

  .main {
    padding-top: 1.15rem;
  }

  .back-top {
    right: 0.85rem;
    bottom: 0.85rem;
  }
}
</style>
