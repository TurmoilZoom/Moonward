/**
 * 安装包与发行说明：支持 GitHub / CNB 双渠道。
 *
 * - 清单优先走 GitHub API（浏览器 CORS 友好），一次拉最近若干个版本（含 body）
 * - CNB 列表 API 在浏览器中通常无 CORS，失败时用同 tag/文件名改写为 CNB 直链
 * - 下载源切换只改 URL，不重复请求（已有缓存时）
 * - 页内切换版本时同步安装包与发布说明
 */

/** @typedef {'github' | 'cnb'} DownloadChannel */
/** @typedef {'x64' | 'arm64'} Arch */
/** @typedef {'setup' | 'portable'} PackageKind */

export const GITHUB_REPO = 'TurmoilZoom/Moonward'
/** 与应用内 UpdateService.RepoUrl 列表侧一致；附件实际落在 Moonward 路径 */
export const CNB_LIST_REPO = 'TurmoilZoom/Starward'
export const CNB_ASSET_REPO = 'TurmoilZoom/Moonward'

export const CHANNELS = {
  github: {
    id: /** @type {DownloadChannel} */ ('github'),
    label: { zh: 'GitHub', en: 'GitHub' },
    hint: {
      zh: '国际线路 · github.com',
      en: 'International · github.com',
    },
    releasesPage: `https://github.com/${GITHUB_REPO}/releases/latest`,
  },
  cnb: {
    id: /** @type {DownloadChannel} */ ('cnb'),
    label: { zh: 'CNB', en: 'CNB' },
    hint: {
      zh: '国内线路 · cnb.cool（与应用内默认更新源一致）',
      en: 'Mainland · cnb.cool (app default update source)',
    },
    releasesPage: `https://cnb.cool/${CNB_ASSET_REPO}/-/releases`,
  },
}

/** 默认渠道：与应用更新窗口一致，优先 CNB */
export const DEFAULT_CHANNEL = /** @type {DownloadChannel} */ ('cnb')

const CHANNEL_STORAGE_KEY = 'moonward-download-channel'
const CACHE_KEY = 'moonward-release-catalogs-v3'
const CACHE_TTL_MS = 10 * 60 * 1000
const RELEASE_LIST_PAGE_SIZE = 30

/**
 * @typedef {object} ReleasePackage
 * @property {Arch} arch
 * @property {PackageKind} kind
 * @property {string} name
 * @property {number} size
 * @property {string} url
 */

/**
 * @typedef {object} LatestRelease
 * @property {string} tag
 * @property {string} name
 * @property {string} htmlUrl
 * @property {string | null} publishedAt
 * @property {string} body
 * @property {boolean} prerelease
 * @property {ReleasePackage[]} packages
 * @property {DownloadChannel} channel
 * @property {'github' | 'cnb' | 'github+cnb-urls'} catalogSource
 */

/**
 * @param {string} name
 * @returns {{ arch: Arch, kind: PackageKind } | null}
 */
export function classifyAsset(name) {
  const n = name || ''
  let m = n.match(/^Moonward-win-(x64|arm64)-Setup\.exe$/i)
  if (m) return { arch: m[1].toLowerCase() === 'arm64' ? 'arm64' : 'x64', kind: 'setup' }
  m = n.match(/^Moonward-win-(x64|arm64)-Portable\.zip$/i)
  if (m) return { arch: m[1].toLowerCase() === 'arm64' ? 'arm64' : 'x64', kind: 'portable' }
  return null
}

/**
 * @param {number} bytes
 * @param {'zh' | 'en'} [_locale]
 */
export function formatBytes(bytes, _locale = 'zh') {
  if (!Number.isFinite(bytes) || bytes < 0) return ''
  const mb = bytes / (1024 * 1024)
  if (mb >= 10) return `${Math.round(mb)} MB`
  return `${mb.toFixed(1)} MB`
}

/** @returns {Arch} */
export function detectPreferredArch() {
  try {
    const ua = navigator.userAgent || ''
    const platform = navigator.platform || ''
    if (/arm64|aarch64/i.test(ua) || /ARM/i.test(platform)) return 'arm64'
  } catch {
    /* ignore */
  }
  return 'x64'
}

/**
 * @param {Arch} preferred
 * @returns {Promise<Arch>}
 */
export async function refinePreferredArch(preferred) {
  try {
    if (navigator.userAgentData?.getHighEntropyValues) {
      const { architecture } = await navigator.userAgentData.getHighEntropyValues(['architecture'])
      if (architecture && /arm/i.test(architecture)) return 'arm64'
      if (architecture && /x86|x64/i.test(architecture)) return 'x64'
    }
  } catch {
    /* ignore */
  }
  return preferred
}

/** @returns {DownloadChannel} */
export function loadStoredChannel() {
  try {
    const v = localStorage.getItem(CHANNEL_STORAGE_KEY)
    if (v === 'github' || v === 'cnb') return v
  } catch {
    /* ignore */
  }
  return DEFAULT_CHANNEL
}

/** @param {DownloadChannel} channel */
export function storeChannel(channel) {
  try {
    localStorage.setItem(CHANNEL_STORAGE_KEY, channel)
  } catch {
    /* ignore */
  }
}

/**
 * @param {string} tag
 * @param {string} fileName
 */
export function cnbDownloadUrl(tag, fileName) {
  return `https://cnb.cool/${CNB_ASSET_REPO}/-/releases/download/${encodeURIComponent(tag)}/${encodeURIComponent(fileName)}`
}

/**
 * @param {string} tag
 * @param {string} fileName
 */
export function githubDownloadUrl(tag, fileName) {
  return `https://github.com/${GITHUB_REPO}/releases/download/${encodeURIComponent(tag)}/${encodeURIComponent(fileName)}`
}

/**
 * @param {any[]} assets
 * @param {(name: string, asset: any) => string} urlFor
 * @returns {ReleasePackage[]}
 */
function packagesFromAssets(assets, urlFor) {
  /** @type {ReleasePackage[]} */
  const packages = []
  for (const a of assets || []) {
    const c = classifyAsset(a?.name)
    if (!c) continue
    packages.push({
      arch: c.arch,
      kind: c.kind,
      name: a.name,
      size: Number(a.size) || 0,
      url: urlFor(a.name, a),
    })
  }
  const order = { 'x64-setup': 0, 'x64-portable': 1, 'arm64-setup': 2, 'arm64-portable': 3 }
  packages.sort((a, b) => (order[`${a.arch}-${a.kind}`] ?? 9) - (order[`${b.arch}-${b.kind}`] ?? 9))
  return packages
}

/**
 * 不含渠道 URL 的原始目录（name/size/arch/kind + 发行说明）。
 * @typedef {object} ReleaseCatalog
 * @property {string} tag
 * @property {string} name
 * @property {string | null} publishedAt
 * @property {string} body
 * @property {boolean} prerelease
 * @property {{ name: string, size: number, arch: Arch, kind: PackageKind }[]} items
 * @property {'github' | 'cnb'} catalogSource
 */

/**
 * @param {any} json GitHub release object
 * @returns {ReleaseCatalog}
 */
export function catalogFromGitHubPayload(json) {
  const tag = json?.tag_name || json?.name || ''
  const items = packagesFromAssets(json?.assets, () => '').map(({ url: _u, ...rest }) => rest)
  return {
    tag,
    name: json?.name || tag,
    publishedAt: json?.published_at || null,
    body: typeof json?.body === 'string' ? json.body : '',
    prerelease: Boolean(json?.prerelease),
    items,
    catalogSource: 'github',
  }
}

/**
 * @param {any} json CNB release object (single)
 * @returns {ReleaseCatalog}
 */
export function catalogFromCnbPayload(json) {
  const tag = json?.tag_name || json?.name || ''
  const items = packagesFromAssets(json?.assets, () => '').map(({ url: _u, ...rest }) => rest)
  const body =
    (typeof json?.body === 'string' && json.body) ||
    (typeof json?.description === 'string' && json.description) ||
    ''
  return {
    tag,
    name: json?.name || tag,
    publishedAt: json?.published_at || json?.created_at || null,
    body,
    prerelease: Boolean(json?.prerelease),
    items,
    catalogSource: 'cnb',
  }
}

/**
 * @param {ReleaseCatalog} catalog
 * @param {DownloadChannel} channel
 * @returns {LatestRelease}
 */
export function applyChannel(catalog, channel) {
  const packages = (catalog.items || []).map((item) => ({
    ...item,
    url:
      channel === 'github'
        ? githubDownloadUrl(catalog.tag, item.name)
        : cnbDownloadUrl(catalog.tag, item.name),
  }))
  const tag = catalog.tag
  return {
    tag,
    name: catalog.name,
    htmlUrl:
      channel === 'github'
        ? `https://github.com/${GITHUB_REPO}/releases/tag/${encodeURIComponent(tag)}`
        : `https://cnb.cool/${CNB_ASSET_REPO}/-/releases/tag/${encodeURIComponent(tag)}`,
    publishedAt: catalog.publishedAt,
    body: catalog.body || '',
    prerelease: Boolean(catalog.prerelease),
    packages,
    channel,
    catalogSource:
      catalog.catalogSource === 'github' && channel === 'cnb'
        ? 'github+cnb-urls'
        : catalog.catalogSource,
  }
}

function readCatalogCache() {
  try {
    const raw = sessionStorage.getItem(CACHE_KEY)
    if (!raw) return null
    const { at, data } = JSON.parse(raw)
    if (!at || !Array.isArray(data) || !data.length || Date.now() - at > CACHE_TTL_MS) return null
    if (!data[0]?.tag) return null
    return /** @type {ReleaseCatalog[]} */ (data)
  } catch {
    return null
  }
}

/** @param {ReleaseCatalog[]} data */
function writeCatalogCache(data) {
  try {
    sessionStorage.setItem(CACHE_KEY, JSON.stringify({ at: Date.now(), data }))
  } catch {
    /* ignore */
  }
}

/**
 * @param {AbortSignal} [signal]
 * @returns {Promise<ReleaseCatalog[]>}
 */
async function fetchGitHubCatalogs(signal) {
  const res = await fetch(
    `https://api.github.com/repos/${GITHUB_REPO}/releases?per_page=${RELEASE_LIST_PAGE_SIZE}`,
    {
      headers: { Accept: 'application/vnd.github+json' },
      signal,
    },
  )
  if (!res.ok) throw new Error(`GitHub API ${res.status}`)
  const json = await res.json()
  const arr = Array.isArray(json) ? json : []
  const catalogs = arr.filter((x) => !x?.draft).map(catalogFromGitHubPayload)
  if (!catalogs.length) throw new Error('GitHub has no published releases')
  return catalogs
}

/**
 * @param {AbortSignal} [signal]
 * @returns {Promise<ReleaseCatalog[]>}
 */
async function fetchCnbCatalogs(signal) {
  // 列表 API 与应用 CnbSource 一致；公开页可读，但浏览器可能遇 CORS
  const res = await fetch(
    `https://cnb.cool/${CNB_LIST_REPO}/-/releases?page=1&page_size=${RELEASE_LIST_PAGE_SIZE}`,
    {
      headers: { Accept: 'application/vnd.cnb.api+json' },
      signal,
    },
  )
  if (!res.ok) throw new Error(`CNB API ${res.status}`)
  const list = await res.json()
  const arr = Array.isArray(list) ? list : []
  const catalogs = arr.filter((x) => !x?.draft).map(catalogFromCnbPayload)
  if (!catalogs.length) throw new Error('CNB has no releases')
  return catalogs
}

/**
 * 拉取最近若干个版本的目录（与渠道无关）；渠道在 applyChannel 中绑定 URL。
 * @param {AbortSignal} [signal]
 * @returns {Promise<ReleaseCatalog[]>}
 */
export async function fetchReleaseCatalogs(signal) {
  const cached = readCatalogCache()
  if (cached) return cached

  /** @type {Error | null} */
  let lastErr = null
  try {
    const catalogs = await fetchGitHubCatalogs(signal)
    writeCatalogCache(catalogs)
    return catalogs
  } catch (e) {
    lastErr = e instanceof Error ? e : new Error(String(e))
  }

  try {
    const catalogs = await fetchCnbCatalogs(signal)
    writeCatalogCache(catalogs)
    return catalogs
  } catch (e) {
    lastErr = e instanceof Error ? e : new Error(String(e))
  }

  throw lastErr || new Error('Failed to load release catalogs')
}

/**
 * @param {ReleaseCatalog[]} catalogs
 * @returns {ReleaseCatalog | null}
 */
export function pickLatestCatalog(catalogs) {
  if (!catalogs?.length) return null
  return catalogs.find((c) => !c.prerelease) || catalogs[0] || null
}

/**
 * @param {DownloadChannel} channel
 * @param {AbortSignal} [signal]
 * @returns {Promise<LatestRelease>}
 */
export async function fetchLatestRelease(channel, signal) {
  const catalogs = await fetchReleaseCatalogs(signal)
  const catalog = pickLatestCatalog(catalogs)
  if (!catalog) throw new Error('No releases')
  return applyChannel(catalog, channel)
}

/**
 * 已有清单时切换渠道：只改写下载 URL / 页面链接，不重新请求。
 * @param {LatestRelease} release
 * @param {DownloadChannel} channel
 * @returns {LatestRelease}
 */
export function switchReleaseChannel(release, channel) {
  const catalogSource =
    release.catalogSource === 'cnb' ? 'cnb' : /** @type {'github'} */ ('github')
  return applyChannel(
    {
      tag: release.tag,
      name: release.name,
      publishedAt: release.publishedAt,
      body: release.body || '',
      prerelease: Boolean(release.prerelease),
      items: (release.packages || []).map(({ arch, kind, name, size }) => ({
        arch,
        kind,
        name,
        size,
      })),
      catalogSource,
    },
    channel,
  )
}

/**
 * @param {string | null | undefined} iso
 * @param {'zh' | 'en'} locale
 */
export function formatPublishedAt(iso, locale = 'zh') {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  if (locale === 'zh') {
    return `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`
  }
  return d.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' })
}

/**
 * @param {ReleasePackage[]} packages
 * @param {Arch} arch
 * @param {PackageKind} kind
 */
export function findPackage(packages, arch, kind) {
  return packages.find((p) => p.arch === arch && p.kind === kind) || null
}

/** @deprecated 使用 CHANNELS.github.releasesPage */
export const RELEASES_PAGE = CHANNELS.github.releasesPage