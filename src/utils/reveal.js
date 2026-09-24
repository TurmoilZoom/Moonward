/**
 * 滚动进场：元素进入视口时加 `is-in`，淡入并轻微上移（样式见 global.css 的 `.reveal`）。
 *
 * 用法 `v-reveal` / `v-reveal="序号"`，序号只用来错峰（同组卡片依次亮起）。
 * 所有元素共用一个 IntersectionObserver，进场一次后即取消观察，不随滚动反复播放。
 * 关掉动效偏好时直接判定到位，不观察也不过渡。
 */

/** 每个序号的错峰间隔，超过 STAGGER_MAX 不再往后排，避免末尾等太久。 */
const STAGGER_STEP_MS = 55
const STAGGER_MAX = 8

/** @type {IntersectionObserver | null} */
let observer = null

function reducedMotion() {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
}

function ensureObserver() {
  if (observer) return observer
  observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue
        entry.target.classList.add('is-in')
        observer?.unobserve(entry.target)
      }
    },
    // 底部留一点余量，元素刚冒头就开始，不必等整块进来
    { rootMargin: '0px 0px -6% 0px', threshold: 0.05 },
  )
  return observer
}

export const vReveal = {
  mounted(el, binding) {
    el.classList.add('reveal')
    const step = Number(binding?.value) || 0
    if (step > 0) {
      el.style.setProperty('--reveal-delay', `${Math.min(step, STAGGER_MAX) * STAGGER_STEP_MS}ms`)
    }
    if (reducedMotion() || typeof IntersectionObserver === 'undefined') {
      el.classList.add('is-in')
      return
    }
    ensureObserver().observe(el)
  },
  unmounted(el) {
    observer?.unobserve(el)
  },
}
