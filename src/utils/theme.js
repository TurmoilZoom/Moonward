/**
 * 站点主题：浅色纸 / 深色胡桃纸。
 * 未手动选择时跟随系统；选择后写入 localStorage。
 */

export const THEME_KEY = 'moonward-theme'
export const THEME_COLORS = {
  light: '#f0ebe3',
  dark: '#161410',
}

/** @typedef {'light' | 'dark'} ResolvedTheme */
/** @typedef {'system' | ResolvedTheme} ThemePref */

/** @returns {ThemePref} */
export function readThemePref() {
  try {
    const v = localStorage.getItem(THEME_KEY)
    if (v === 'light' || v === 'dark') return v
  } catch {
    /* ignore */
  }
  return 'system'
}

/** @param {ThemePref} pref */
export function storeThemePref(pref) {
  try {
    if (pref === 'system') localStorage.removeItem(THEME_KEY)
    else localStorage.setItem(THEME_KEY, pref)
  } catch {
    /* ignore */
  }
}

/** @returns {boolean} */
export function systemIsDark() {
  try {
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  } catch {
    return false
  }
}

/**
 * @param {ThemePref} [pref]
 * @returns {ResolvedTheme}
 */
export function resolveTheme(pref = readThemePref()) {
  if (pref === 'light' || pref === 'dark') return pref
  return systemIsDark() ? 'dark' : 'light'
}

/** @param {ResolvedTheme} theme */
export function applyTheme(theme) {
  const root = document.documentElement
  root.setAttribute('data-theme', theme)
  root.style.colorScheme = theme
  const meta = document.querySelector('meta[name="theme-color"]')
  if (meta) meta.setAttribute('content', THEME_COLORS[theme])
}

/**
 * @param {() => void} onChange
 * @returns {() => void} unsubscribe
 */
export function watchSystemTheme(onChange) {
  try {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const handler = () => onChange()
    if (mq.addEventListener) mq.addEventListener('change', handler)
    else mq.addListener(handler)
    return () => {
      if (mq.removeEventListener) mq.removeEventListener('change', handler)
      else mq.removeListener(handler)
    }
  } catch {
    return () => {}
  }
}
