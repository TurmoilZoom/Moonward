<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import {
  codeSigning,
  featureCards,
  games,
  intro,
  launchFlow,
  links,
  requirements,
} from './data/content'
import { asset } from './utils/asset'

const locale = ref(localStorage.getItem('moonward-locale') || 'zh')
const t = computed(() => (keyObj) => keyObj[locale.value] ?? keyObj.zh)

watch(locale, (v) => {
  localStorage.setItem('moonward-locale', v)
  document.documentElement.lang = v === 'zh' ? 'zh-CN' : 'en'
})

function toggleLocale() {
  locale.value = locale.value === 'zh' ? 'en' : 'zh'
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

const activeStep = ref('config')

onMounted(() => {
  reducedMotion.value = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  document.documentElement.lang = locale.value === 'zh' ? 'zh-CN' : 'en'
})

onUnmounted(() => {
  if (raf) cancelAnimationFrame(raf)
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
          <a href="#flow">{{ locale === 'zh' ? '启动流程' : 'Launch flow' }}</a>
          <a href="#install">{{ locale === 'zh' ? '安装' : 'Install' }}</a>
          <a href="#code-signing">{{ locale === 'zh' ? '代码签名' : 'Code signing' }}</a>
          <a :href="links.github" target="_blank" rel="noopener noreferrer">GitHub</a>
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
        <div class="layer layer-bg" :style="layerStyle(0.15)" />
        <div class="layer layer-orb orb-a" :style="layerStyle(0.55)" />
        <div class="layer layer-orb orb-b" :style="layerStyle(0.85)" />
        <div class="layer layer-orb orb-c" :style="layerStyle(1.15)" />
        <div class="layer layer-ring" :style="layerStyle(0.4)" />
        <div class="layer layer-grid" :style="layerStyle(0.25)" />
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
          <a class="btn" :href="links.download" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '下载最新版' : 'Download' }}
          </a>
          <a class="btn ghost" :href="links.github" target="_blank" rel="noopener noreferrer">
            GitHub
          </a>
          <a class="text-link" :href="links.upstream" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '上游 Starward' : 'Upstream Starward' }}
          </a>
        </p>
      </div>
    </section>

    <main class="wrap main">
      <!-- Feature cards -->
      <section id="features" class="block" aria-labelledby="features-heading">
        <p class="section-kicker">{{ locale === 'zh' ? '新增与增强' : "What's new" }}</p>
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

      <!-- Launch flow -->
      <section id="flow" class="block flow-block" aria-labelledby="flow-heading">
        <p class="section-kicker">{{ locale === 'zh' ? '核心能力' : 'Core' }}</p>
        <h2 id="flow-heading">{{ t(launchFlow.title) }}</h2>
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
                <span class="pipe-sub">.lnk → Moonward</span>
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
              locale === 'zh' ? '桌面图标绑定某套配置，双击直达' : 'Desktop icon bound to one profile'
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
                ? '快捷方式 / URL / 命令行启动时顺带签到'
                : 'On shortcut, URL, or CLI launch'
            }}</span>
          </div>
        </div>
      </section>

      <!-- Install -->
      <section id="install" class="block install-block" aria-labelledby="install-heading">
        <div class="install-grid">
          <div>
            <p class="section-kicker">{{ locale === 'zh' ? '开始使用' : 'Get started' }}</p>
            <h2 id="install-heading">{{ locale === 'zh' ? '安装' : 'Install' }}</h2>
            <p class="section-lead install-lead">
              {{
                locale === 'zh'
                  ? '从 GitHub Releases 下载对应 CPU 架构的安装包，按提示完成安装。'
                  : 'Download the package for your CPU architecture from GitHub Releases and follow the installer.'
              }}
            </p>
            <dl class="req">
              <template v-for="row in requirements" :key="row.label.zh">
                <dt>{{ t(row.label) }}</dt>
                <dd>{{ t(row.value) }}</dd>
              </template>
            </dl>
          </div>
          <div class="install-cta">
            <a class="btn large" :href="links.download" target="_blank" rel="noopener noreferrer">
              {{ locale === 'zh' ? '前往 Releases' : 'Open Releases' }}
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

      <!-- Code signing policy (SignPath Foundation requirement for OSS homepage) -->
      <section id="code-signing" class="block signing-block" aria-labelledby="signing-heading">
        <p class="section-kicker">{{ t(codeSigning.kicker) }}</p>
        <h2 id="signing-heading">{{ t(codeSigning.title) }}</h2>
        <p class="signing-attribution">
          Free code signing provided by
          <a :href="links.signpath" target="_blank" rel="noopener noreferrer">SignPath.io</a>,
          certificate by
          <a :href="links.signpathFoundation" target="_blank" rel="noopener noreferrer">SignPath Foundation</a>.
        </p>
        <p class="section-lead">{{ t(codeSigning.lead) }}</p>

        <h3 class="signing-roles-title">{{ t(codeSigning.rolesHeading) }}</h3>
        <div class="signing-roles" role="list">
          <div v-for="row in codeSigning.roles" :key="row.members" class="signing-role" role="listitem">
            <span class="signing-role-name">{{ t(row.role) }}</span>
            <a
              class="signing-role-member mono"
              :href="codeSigning.maintainerUrl"
              target="_blank"
              rel="noopener noreferrer"
            >{{ row.members }}</a>
          </div>
        </div>

        <p class="signing-links">
          <a
            class="text-link"
            :href="locale === 'zh' ? links.codeSigningPolicyZh : links.codeSigningPolicy"
            target="_blank"
            rel="noopener noreferrer"
          >{{ t(codeSigning.fullPolicy) }}</a>
          <span class="signing-sep" aria-hidden="true">·</span>
          <a
            class="text-link"
            :href="locale === 'zh' ? links.privacyZh : links.privacy"
            target="_blank"
            rel="noopener noreferrer"
          >{{ t(codeSigning.privacy) }}</a>
          <span class="signing-sep" aria-hidden="true">·</span>
          <a class="text-link" :href="links.download" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '官方下载' : 'Official downloads' }}
          </a>
        </p>
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
          <a href="#code-signing">Code signing policy</a>
          ·
          <a :href="links.github" target="_blank" rel="noopener noreferrer">TurmoilZoom/Moonward</a>
        </p>
      </div>
    </footer>
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

.lang {
  font-family: var(--font-mono);
  font-size: 0.72rem;
  padding: 0.22rem 0.5rem;
  border: 1px solid var(--line-strong);
  border-radius: 4px;
  color: var(--muted);
}

.lang:hover {
  color: var(--ink);
  border-color: var(--ink-2);
}

/* —— Hero + parallax —— */
.hero {
  position: relative;
  overflow: hidden;
  border-bottom: 1px solid var(--line);
  background:
    linear-gradient(165deg, #e8e1d4 0%, var(--bg) 45%, #e4ebe6 100%);
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

.layer-bg {
  inset: -8%;
  background:
    radial-gradient(circle at 22% 40%, rgba(30, 77, 63, 0.14), transparent 42%),
    radial-gradient(circle at 78% 30%, rgba(154, 107, 31, 0.12), transparent 38%),
    radial-gradient(circle at 55% 85%, rgba(42, 79, 122, 0.08), transparent 40%);
}

.layer-orb {
  border-radius: 50%;
  filter: blur(0.5px);
}

.orb-a {
  width: 18rem;
  height: 18rem;
  top: -4rem;
  right: 8%;
  background: radial-gradient(circle at 35% 35%, rgba(255, 252, 247, 0.9), rgba(220, 234, 228, 0.45) 55%, transparent 70%);
  box-shadow: inset 0 0 40px rgba(30, 77, 63, 0.08);
}

.orb-b {
  width: 9rem;
  height: 9rem;
  bottom: 10%;
  left: 12%;
  background: radial-gradient(circle at 40% 40%, rgba(255, 240, 210, 0.75), rgba(154, 107, 31, 0.15) 60%, transparent 72%);
}

.orb-c {
  width: 5.5rem;
  height: 5.5rem;
  top: 28%;
  left: 38%;
  background: radial-gradient(circle, rgba(42, 106, 106, 0.22), transparent 68%);
}

.layer-ring {
  width: 22rem;
  height: 22rem;
  right: -2rem;
  bottom: -6rem;
  border: 1px solid rgba(30, 77, 63, 0.12);
  border-radius: 50%;
  box-shadow: 0 0 0 28px rgba(30, 77, 63, 0.04);
}

.layer-grid {
  inset: 0;
  opacity: 0.35;
  background-image:
    linear-gradient(rgba(28, 25, 21, 0.04) 1px, transparent 1px),
    linear-gradient(90deg, rgba(28, 25, 21, 0.04) 1px, transparent 1px);
  background-size: 48px 48px;
  mask-image: radial-gradient(ellipse 70% 70% at 70% 40%, black, transparent);
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
  color: #f7faf8;
  text-decoration: none;
  font-family: var(--font-sans);
  font-size: 0.9rem;
  font-weight: 600;
  transition: background 0.15s ease, transform 0.15s ease;
}

.btn:hover {
  color: #fff;
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
}

/* —— Feature cards —— */
.cards {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
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

/* —— Flow —— */
.flow-block {
  background:
    linear-gradient(180deg, var(--bg-card) 0%, #f7f3ec 100%);
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
  color: #f7faf8;
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
  background: #fff;
}

.pipe-node.shortcut.on,
.pipe-node.url.on {
  border-color: var(--amber);
  box-shadow: 0 0 0 3px rgba(154, 107, 31, 0.15);
}

.pipe-node.checkin.on {
  border-color: var(--green);
  box-shadow: 0 0 0 3px rgba(58, 107, 58, 0.15);
}

.pipe-node.game.on {
  border-color: var(--blue);
  box-shadow: 0 0 0 3px rgba(42, 79, 122, 0.15);
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
/* install 不再是 main 内最后一块，沿用 .block 的 margin-bottom 与下方签名区隔开 */

.install-grid {
  display: grid;
  grid-template-columns: 1.4fr 0.8fr;
  gap: 1.25rem;
  align-items: end;
}

.install-lead {
  margin-bottom: 0.85rem;
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
  gap: 0.65rem;
  padding: 1.1rem 1.15rem;
  border-radius: var(--radius-sm);
  border: 1px solid var(--line);
  background: var(--accent-soft);
}

/* —— Code signing policy —— */
.signing-block {
  /* 作为 main 内最后一块：去掉 .block 的 margin-bottom，与原先 install 贴页脚的处理一致 */
  margin-bottom: 0;
  border-color: color-mix(in srgb, var(--accent) 22%, var(--line));
}

.signing-attribution {
  margin: 0.35rem 0 0.75rem;
  font-family: var(--font-sans);
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--ink);
  line-height: 1.55;
}

.signing-attribution a {
  color: var(--accent);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.signing-attribution a:hover {
  color: var(--accent-hover);
}

.signing-roles-title {
  margin: 1rem 0 0.55rem;
  font-family: var(--font-sans);
  font-size: 0.92rem;
  font-weight: 600;
  color: var(--ink);
}

.signing-roles {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.55rem;
}

.signing-role {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 0.7rem 0.85rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
}

.signing-role-name {
  font-family: var(--font-sans);
  font-size: 0.78rem;
  color: var(--muted);
}

.signing-role-member {
  color: var(--ink);
  text-decoration: none;
  font-size: 0.88rem;
}

.signing-role-member:hover {
  color: var(--accent);
  text-decoration: underline;
}

.signing-links {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem 0.55rem;
  margin-top: 1rem;
}

.signing-sep {
  color: var(--muted);
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

  .install-grid {
    grid-template-columns: 1fr;
  }

  .signing-roles {
    grid-template-columns: 1fr;
  }

  .install-cta {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
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
}
</style>
