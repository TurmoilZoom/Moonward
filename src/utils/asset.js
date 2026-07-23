/**
 * Resolve a public asset path against Vite `base` (e.g. `/Moonward/`).
 * @param {string} path path under `public/`, with or without leading slash
 */
export function asset(path) {
  const base = import.meta.env.BASE_URL || '/'
  const clean = String(path || '').replace(/^\/+/, '')
  return `${base}${clean}`
}
