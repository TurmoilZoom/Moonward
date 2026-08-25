<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { screens } from '../data/content'
import { asset } from '../utils/asset'

const props = defineProps({
  locale: { type: String, required: true },
})

const t = (keyObj) => keyObj[props.locale] ?? keyObj.zh

const sectionRef = ref(null)
const dialogRef = ref(null)
const activeId = ref(screens[0].id)
const lightboxOpen = ref(false)
const reducedMotion = ref(false)
const tabsStacked = ref(true)
const tabEls = []
let mqStacked = null
let stopStackedMq = null

const activeIndex = computed(() => {
  const i = screens.findIndex((s) => s.id === activeId.value)
  return i < 0 ? 0 : i
})

const activeScreen = computed(() => screens[activeIndex.value])

function setTabRef(el, i) {
  tabEls[i] = el || null
}

function selectScreen(id, { hash = true, revealTab = true } = {}) {
  if (!screens.some((s) => s.id === id)) return
  activeId.value = id
  if (hash) {
    const next = `#screens/${id}`
    if (window.location.hash !== next) {
      history.replaceState(null, '', next)
    }
  }
  if (!revealTab || tabsStacked.value) return
  const tab = tabEls[screens.findIndex((s) => s.id === id)]
  tab?.scrollIntoView({
    block: 'nearest',
    inline: 'start',
    behavior: reducedMotion.value ? 'auto' : 'smooth',
  })
}

function nextScreen(dir) {
  const n = (activeIndex.value + dir + screens.length) % screens.length
  selectScreen(screens[n].id)
}

function focusActiveTab() {
  tabEls[activeIndex.value]?.focus()
}

function parseScreensHash() {
  let hash = (window.location.hash || '').replace(/^#/, '')
  try {
    hash = decodeURIComponent(hash)
  } catch {
    /* keep raw */
  }
  const m = hash.match(/^screens(?:\/([a-z0-9-]+))?$/)
  if (!m) return null
  return { id: m[1] || '' }
}

function revealActiveTab() {
  if (tabsStacked.value) return
  tabEls[activeIndex.value]?.scrollIntoView({
    block: 'nearest',
    inline: 'start',
    behavior: reducedMotion.value ? 'auto' : 'smooth',
  })
}

function syncFromHash() {
  const parsed = parseScreensHash()
  if (!parsed) return
  if (parsed.id) selectScreen(parsed.id, { hash: false, revealTab: false })
  sectionRef.value?.scrollIntoView({
    behavior: reducedMotion.value ? 'auto' : 'smooth',
    block: 'start',
  })
  requestAnimationFrame(() => requestAnimationFrame(revealActiveTab))
}

function onTablistKeydown(e) {
  const dir = { ArrowUp: -1, ArrowLeft: -1, ArrowDown: 1, ArrowRight: 1 }[e.key]
  if (dir) {
    e.preventDefault()
    e.stopPropagation()
    nextScreen(dir)
    nextTick(focusActiveTab)
    return
  }
  if (e.key === 'Home') {
    e.preventDefault()
    selectScreen(screens[0].id)
    nextTick(focusActiveTab)
    return
  }
  if (e.key === 'End') {
    e.preventDefault()
    selectScreen(screens[screens.length - 1].id)
    nextTick(focusActiveTab)
    return
  }
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    openLightbox()
  }
}

function onSectionKeydown(e) {
  if (lightboxOpen.value) return
  if (e.target instanceof HTMLElement && e.target.closest('.screen-list')) return
  if (e.key === 'ArrowLeft') {
    e.preventDefault()
    nextScreen(-1)
  } else if (e.key === 'ArrowRight') {
    e.preventDefault()
    nextScreen(1)
  }
}

function onLightboxKeydown(e) {
  if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
  e.preventDefault()
  e.stopPropagation()
  nextScreen(e.key === 'ArrowRight' ? 1 : -1)
}

let lastFocus = null

function openLightbox() {
  lastFocus = document.activeElement
  dialogRef.value?.showModal()
  lightboxOpen.value = true
}

function closeLightbox() {
  if (dialogRef.value?.open) dialogRef.value.close()
}

function onLightboxClose() {
  lightboxOpen.value = false
  if (lastFocus instanceof HTMLElement) {
    lastFocus.focus({ preventScroll: true })
  }
}

function onLightboxBackdrop(e) {
  if (e.target === dialogRef.value) closeLightbox()
}

const swipe = { x: 0, y: 0, id: null, moved: false }

function onStagePointerDown(e) {
  if (e.button != null && e.button !== 0) return
  swipe.x = e.clientX
  swipe.y = e.clientY
  swipe.id = e.pointerId
  swipe.moved = false
  e.currentTarget.setPointerCapture?.(e.pointerId)
}

function onStagePointerMove(e) {
  if (swipe.id !== e.pointerId) return
  const dx = e.clientX - swipe.x
  const dy = e.clientY - swipe.y
  if (Math.abs(dx) > 8 || Math.abs(dy) > 8) swipe.moved = true
}

function onStagePointerUp(e) {
  if (swipe.id !== e.pointerId) return
  const dx = e.clientX - swipe.x
  const dy = e.clientY - swipe.y
  swipe.id = null
  if (Math.abs(dx) > 48 && Math.abs(dx) > Math.abs(dy) * 1.15) {
    nextScreen(dx < 0 ? 1 : -1)
    return
  }
  if (!swipe.moved) openLightbox()
}

function onStagePointerCancel(e) {
  if (swipe.id === e.pointerId) swipe.id = null
}

function onStageKeydown(e) {
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    openLightbox()
  }
}

onMounted(() => {
  reducedMotion.value = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  mqStacked = window.matchMedia('(min-width: 821px)')
  const applyStacked = () => {
    const stacked = mqStacked.matches
    const becameStrip = tabsStacked.value && !stacked
    tabsStacked.value = stacked
    if (becameStrip) requestAnimationFrame(revealActiveTab)
  }
  applyStacked()
  mqStacked.addEventListener('change', applyStacked)
  stopStackedMq = () => mqStacked.removeEventListener('change', applyStacked)
  window.addEventListener('hashchange', syncFromHash)
  if (parseScreensHash()) {
    requestAnimationFrame(syncFromHash)
  }
})

onUnmounted(() => {
  window.removeEventListener('hashchange', syncFromHash)
  stopStackedMq?.()
  if (dialogRef.value?.open) dialogRef.value.close()
})
</script>

<template>
  <section
    id="screens"
    ref="sectionRef"
    class="block screens-block"
    aria-labelledby="screens-heading"
    @keydown="onSectionKeydown"
  >
    <h2 id="screens-heading">{{ locale === 'zh' ? '界面' : 'Screens' }}</h2>
    <p class="section-lead">
      {{
        locale === 'zh'
          ? '三张实际窗口：启动配置、抽卡记录、每日签到。点选切换，点图放大。'
          : 'Three real windows: launch profile, gacha history, daily check-in. Pick a tab; click the shot to enlarge.'
      }}
    </p>

    <div class="screen-board">
      <div
        class="screen-list"
        role="tablist"
        :aria-label="locale === 'zh' ? '界面列表' : 'Screen list'"
        :aria-orientation="tabsStacked ? 'vertical' : 'horizontal'"
        @keydown="onTablistKeydown"
      >
        <button
          v-for="(s, i) in screens"
          :key="s.id"
          :ref="(el) => setTabRef(el, i)"
          type="button"
          class="screen-tab"
          :data-accent="s.accent"
          role="tab"
          :id="`screen-tab-${s.id}`"
          :aria-selected="s.id === activeId"
          aria-controls="screen-panel"
          :tabindex="s.id === activeId ? 0 : -1"
          @click="selectScreen(s.id)"
        >
          <span class="screen-tab-thumb" aria-hidden="true">
            <img
              :src="asset(s.src)"
              alt=""
              :width="s.width"
              :height="s.height"
              :loading="i === 0 ? 'eager' : 'lazy'"
              decoding="async"
            />
          </span>
          <span class="screen-tab-copy">
            <span class="screen-tab-icon" aria-hidden="true">{{ s.icon }}</span>
            <span class="screen-tab-name">{{ t(s.name) }}</span>
            <span class="screen-tab-tag">{{ t(s.tag) }}</span>
          </span>
        </button>
      </div>

      <div class="screen-main">
        <div
          id="screen-panel"
          class="screen-stage"
          role="tabpanel"
          :data-accent="activeScreen.accent"
          :aria-labelledby="`screen-tab-${activeScreen.id}`"
        >
          <button
            type="button"
            class="screen-nav prev"
            :aria-label="locale === 'zh' ? '上一张' : 'Previous screen'"
            @click="nextScreen(-1)"
            @pointerdown.stop
          >
            <svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">
              <path
                fill="none"
                stroke="currentColor"
                stroke-width="1.75"
                stroke-linecap="round"
                stroke-linejoin="round"
                d="M14.5 6.5 9 12l5.5 5.5"
              />
            </svg>
          </button>

          <div
            class="screen-frame"
            role="button"
            tabindex="0"
            :aria-label="
              locale === 'zh'
                ? `放大查看${t(activeScreen.name)}`
                : `Enlarge ${t(activeScreen.name)}`
            "
            @pointerdown="onStagePointerDown"
            @pointermove="onStagePointerMove"
            @pointerup="onStagePointerUp"
            @pointercancel="onStagePointerCancel"
            @keydown="onStageKeydown"
          >
            <img
              v-for="s in screens"
              :key="s.id"
              class="screen-shot"
              :class="{ 'is-on': s.id === activeId }"
              :src="asset(s.src)"
              :alt="s.id === activeId ? t(s.alt) : ''"
              :aria-hidden="s.id === activeId ? undefined : 'true'"
              :width="s.width"
              :height="s.height"
              :loading="s.id === screens[0].id ? 'eager' : 'lazy'"
              decoding="async"
              draggable="false"
            />
            <span class="screen-zoom" aria-hidden="true">
              <svg viewBox="0 0 24 24" focusable="false">
                <circle cx="11" cy="11" r="6.25" fill="none" stroke="currentColor" stroke-width="1.75" />
                <path
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.75"
                  stroke-linecap="round"
                  d="M16 16.5 20 20.5M11 8.2v5.6M8.2 11h5.6"
                />
              </svg>
            </span>
          </div>

          <button
            type="button"
            class="screen-nav next"
            :aria-label="locale === 'zh' ? '下一张' : 'Next screen'"
            @click="nextScreen(1)"
            @pointerdown.stop
          >
            <svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">
              <path
                fill="none"
                stroke="currentColor"
                stroke-width="1.75"
                stroke-linecap="round"
                stroke-linejoin="round"
                d="M9.5 6.5 15 12l-5.5 5.5"
              />
            </svg>
          </button>
        </div>

        <div class="screen-caption" aria-live="polite">
          <p class="screen-caption-title">
            <span class="screen-count mono">{{ activeIndex + 1 }} / {{ screens.length }}</span>
            {{ t(activeScreen.name) }}
          </p>
          <p class="screen-caption-body">{{ t(activeScreen.caption) }}</p>
          <p class="screen-hint">
            <span class="for-fine">{{
              locale === 'zh' ? '点击图片放大 · ← → 切换' : 'Click to enlarge · ← → to switch'
            }}</span>
            <span class="for-coarse">{{
              locale === 'zh' ? '左右滑动切换 · 点按放大' : 'Swipe to switch · tap to enlarge'
            }}</span>
          </p>
        </div>
      </div>
    </div>

    <dialog
      ref="dialogRef"
      class="screen-lightbox"
      :aria-label="t(activeScreen.name)"
      @close="onLightboxClose"
      @click="onLightboxBackdrop"
      @keydown="onLightboxKeydown"
    >
      <button
        type="button"
        class="screen-lb-close"
        :aria-label="locale === 'zh' ? '关闭' : 'Close'"
        @click.stop="closeLightbox"
      >
        <svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">
          <path
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            d="M6.5 6.5 17.5 17.5M17.5 6.5 6.5 17.5"
          />
        </svg>
      </button>
      <button
        type="button"
        class="screen-lb-nav prev"
        :aria-label="locale === 'zh' ? '上一张' : 'Previous screen'"
        @click.stop="nextScreen(-1)"
      >
        <svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">
          <path
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            d="M14.5 6.5 9 12l5.5 5.5"
          />
        </svg>
      </button>
      <figure class="screen-lb-figure" @click.stop>
        <img
          :src="asset(activeScreen.src)"
          :alt="t(activeScreen.alt)"
          :width="activeScreen.width"
          :height="activeScreen.height"
          decoding="async"
          draggable="false"
        />
        <figcaption aria-live="polite">
          <span class="mono">{{ activeIndex + 1 }} / {{ screens.length }}</span>
          {{ t(activeScreen.name) }}
          <span class="screen-lb-cap">{{ t(activeScreen.caption) }}</span>
        </figcaption>
      </figure>
      <button
        type="button"
        class="screen-lb-nav next"
        :aria-label="locale === 'zh' ? '下一张' : 'Next screen'"
        @click.stop="nextScreen(1)"
      >
        <svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">
          <path
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            d="M9.5 6.5 15 12l-5.5 5.5"
          />
        </svg>
      </button>
    </dialog>
  </section>
</template>

<style scoped>
.screens-block {
  --screen-chrome: #161410;
  margin-bottom: 2rem;
  padding: 1.35rem 1.4rem 1.5rem;
  background: var(--bg-card);
  border: 1px solid var(--line);
  border-radius: var(--radius);
  box-shadow: var(--shadow-sm);
  scroll-margin-top: 4.25rem;
}

.screen-board {
  display: grid;
  grid-template-columns: 16.75rem minmax(0, 1fr);
  gap: 1rem 1.2rem;
  align-items: start;
}

.screen-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  scrollbar-width: thin;
}

.screen-tab {
  display: grid;
  grid-template-columns: 5.6rem minmax(0, 1fr);
  gap: 0.65rem;
  align-items: center;
  padding: 0.4rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
  text-align: left;
  color: var(--ink-2);
  overflow: hidden;
  transition:
    border-color 0.18s ease,
    background 0.18s ease,
    box-shadow 0.18s ease,
    transform 0.18s ease;
}

.screen-tab:hover {
  border-color: color-mix(in srgb, var(--card-accent, var(--accent)) 40%, var(--line));
  background: var(--surface-hover);
}

.screen-tab[aria-selected='true'] {
  border-color: color-mix(in srgb, var(--card-accent, var(--accent)) 70%, var(--line));
  background: color-mix(in srgb, var(--card-accent, var(--accent)) 16%, var(--bg-raised));
  box-shadow: var(--shadow-sm);
  color: var(--ink);
}

.screen-tab:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.screen-tab[data-accent='teal'] { --card-accent: var(--teal); }
.screen-tab[data-accent='violet'] { --card-accent: var(--violet); }
.screen-tab[data-accent='green'] { --card-accent: var(--green); }

.screen-tab-thumb {
  display: block;
  aspect-ratio: 1184 / 668;
  border-radius: 6px;
  overflow: hidden;
  background: var(--screen-chrome);
  border: 1px solid color-mix(in srgb, var(--line) 70%, transparent);
}

.screen-tab-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.screen-tab[aria-selected='true'] .screen-tab-thumb {
  border-color: color-mix(in srgb, var(--card-accent, var(--accent)) 45%, var(--line));
}

.screen-tab-copy {
  display: grid;
  grid-template-columns: 1.15rem minmax(0, 1fr);
  grid-template-rows: auto auto;
  column-gap: 0.3rem;
  row-gap: 0.12rem;
  min-width: 0;
}

.screen-tab-icon {
  grid-row: 1;
  grid-column: 1;
  font-size: 0.82rem;
  line-height: 1.35;
  color: var(--card-accent, var(--accent));
}

.screen-tab-name {
  grid-row: 1;
  grid-column: 2;
  font-family: var(--font-sans);
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--ink);
  line-height: 1.35;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.screen-tab-tag {
  grid-row: 2;
  grid-column: 2;
  font-family: var(--font-sans);
  font-size: 0.72rem;
  color: var(--muted);
  line-height: 1.35;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.screen-stage {
  position: relative;
  border-radius: var(--radius-sm);
  background: var(--screen-chrome);
  border: 1px solid var(--line);
  box-shadow: var(--shadow);
}

.screen-frame {
  position: relative;
  display: block;
  width: 100%;
  aspect-ratio: 1184 / 668;
  overflow: hidden;
  border-radius: inherit;
  cursor: zoom-in;
  touch-action: pan-y;
  user-select: none;
}

.screen-frame:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.screen-shot {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: contain;
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.28s ease;
}

.screen-shot.is-on {
  opacity: 1;
}

.screen-zoom {
  position: absolute;
  right: 0.7rem;
  bottom: 0.7rem;
  width: 2rem;
  height: 2rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  background: color-mix(in srgb, var(--screen-chrome) 62%, transparent);
  color: #f3eee6;
  opacity: 0;
  pointer-events: none;
  backdrop-filter: blur(8px);
  transition: opacity 0.18s ease;
}

.screen-zoom svg {
  width: 1.05rem;
  height: 1.05rem;
}

.screen-stage:hover .screen-zoom,
.screen-frame:focus-visible .screen-zoom {
  opacity: 1;
}

.screen-nav {
  position: absolute;
  top: 50%;
  z-index: 2;
  transform: translateY(-50%);
  width: 2.15rem;
  height: 2.15rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  border: 1px solid color-mix(in srgb, #f3eee6 22%, transparent);
  background: color-mix(in srgb, var(--screen-chrome) 55%, transparent);
  color: #f3eee6;
  backdrop-filter: blur(8px);
  opacity: 0;
  transition: opacity 0.18s ease, background 0.15s ease, border-color 0.15s ease;
}

.screen-nav svg {
  width: 1.05rem;
  height: 1.05rem;
  display: block;
}

.screen-nav.prev {
  left: 0.55rem;
}

.screen-nav.next {
  right: 0.55rem;
}

.screen-stage:hover .screen-nav,
.screen-nav:focus-visible {
  opacity: 1;
}

.screen-nav:hover,
.screen-nav:focus-visible {
  background: color-mix(in srgb, var(--screen-chrome) 78%, transparent);
  border-color: color-mix(in srgb, #f3eee6 40%, transparent);
}

.screen-nav:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.screen-caption {
  margin-top: 0.85rem;
}

.screen-caption-title {
  font-family: var(--font-sans);
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--ink);
  display: flex;
  align-items: baseline;
  gap: 0.55rem;
}

.screen-count {
  font-size: 0.72rem;
  font-weight: 500;
  color: var(--muted);
}

.screen-caption-body {
  margin-top: 0.28rem;
  font-size: 0.9rem;
  line-height: 1.55;
  color: var(--ink-2);
  max-width: 40rem;
}

.screen-hint {
  margin-top: 0.4rem;
  font-family: var(--font-sans);
  font-size: 0.75rem;
  color: var(--muted);
}

.for-coarse {
  display: none;
}

@media (pointer: coarse), (max-width: 820px) {
  .for-fine {
    display: none;
  }

  .for-coarse {
    display: inline;
  }

  .screen-nav,
  .screen-zoom {
    opacity: 0.92;
  }
}

.screen-lightbox {
  position: fixed;
  inset: 0;
  width: 100%;
  max-width: none;
  height: 100%;
  max-height: none;
  margin: 0;
  padding: 0;
  border: none;
  background: transparent;
  color: #f3eee6;
}

.screen-lightbox::backdrop {
  background: color-mix(in srgb, var(--bg-deep) 72%, #000 28%);
  backdrop-filter: blur(12px);
}

.screen-lb-figure {
  position: absolute;
  inset: 3.4rem 4.2rem 4.6rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  margin: 0;
  gap: 0.75rem;
}

.screen-lb-figure img {
  max-width: 100%;
  max-height: 100%;
  width: auto;
  height: auto;
  object-fit: contain;
  border-radius: 10px;
  box-shadow: 0 18px 48px -18px rgba(0, 0, 0, 0.7);
  background: var(--screen-chrome);
}

.screen-lb-figure figcaption {
  flex-shrink: 0;
  max-width: min(40rem, 100%);
  text-align: center;
  font-family: var(--font-sans);
  font-size: 0.88rem;
  font-weight: 600;
  color: #f3eee6;
}

.screen-lb-figure figcaption .mono {
  margin-right: 0.45rem;
  font-weight: 500;
  color: color-mix(in srgb, #f3eee6 62%, transparent);
}

.screen-lb-cap {
  display: block;
  margin-top: 0.2rem;
  font-weight: 400;
  font-size: 0.8rem;
  color: color-mix(in srgb, #f3eee6 72%, transparent);
  line-height: 1.5;
}

.screen-lb-close,
.screen-lb-nav {
  position: absolute;
  z-index: 2;
  width: 2.5rem;
  height: 2.5rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  border: 1px solid color-mix(in srgb, #f3eee6 22%, transparent);
  background: color-mix(in srgb, var(--screen-chrome) 62%, transparent);
  color: #f3eee6;
  backdrop-filter: blur(10px);
}

.screen-lb-close svg,
.screen-lb-nav svg {
  width: 1.15rem;
  height: 1.15rem;
  display: block;
}

.screen-lb-close {
  top: 0.85rem;
  right: 0.85rem;
}

.screen-lb-nav {
  top: 50%;
  transform: translateY(-50%);
}

.screen-lb-nav.prev {
  left: 0.7rem;
}

.screen-lb-nav.next {
  right: 0.7rem;
}

.screen-lb-close:hover,
.screen-lb-nav:hover,
.screen-lb-close:focus-visible,
.screen-lb-nav:focus-visible {
  background: color-mix(in srgb, var(--screen-chrome) 84%, transparent);
  border-color: color-mix(in srgb, #f3eee6 42%, transparent);
}

.screen-lb-close:focus-visible,
.screen-lb-nav:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

@media (max-width: 820px) {
  .screen-board {
    grid-template-columns: 1fr;
  }

  .screen-list {
    flex-direction: row;
    overflow-x: auto;
    overscroll-behavior-x: contain;
    scroll-snap-type: x mandatory;
    gap: 0.45rem;
    padding-bottom: 0.15rem;
    margin-inline: -0.15rem;
    padding-inline: 0.15rem;
  }

  .screen-tab {
    flex: 0 0 auto;
    width: min(18rem, 86vw);
    scroll-snap-align: start;
    grid-template-columns: 4.8rem minmax(0, 1fr);
  }

  .screen-lb-figure {
    inset: 3.2rem 0.85rem 5.2rem;
  }

  .screen-lb-nav.prev {
    left: 0.35rem;
  }

  .screen-lb-nav.next {
    right: 0.35rem;
  }
}

@media (max-width: 560px) {
  .screens-block {
    padding: 1.1rem 1rem 1.2rem;
  }

  .screen-nav {
    width: 1.9rem;
    height: 1.9rem;
  }

  .screen-lb-close {
    top: 0.55rem;
    right: 0.55rem;
  }

  .screen-lb-cap {
    display: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .screen-shot {
    transition: none;
  }
}
</style>
