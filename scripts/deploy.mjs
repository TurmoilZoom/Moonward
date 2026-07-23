/**
 * Deploy website to the gh-pages branch:
 * - Branch root = Vue source (package.json, src/, public/, …)
 * - docs/       = production build (GitHub Pages serves /docs)
 *
 * Avoids the Windows `gh-pages` package ENAMETOOLONG issue by building
 * a fresh orphan commit in a temp dir and force-pushing.
 */
import { spawnSync } from 'node:child_process'
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const websiteRoot = resolve(fileURLToPath(new URL('..', import.meta.url)))
const repoRoot = resolve(websiteRoot, '..')
const distDir = join(websiteRoot, 'dist')
const branch = process.env.GH_PAGES_BRANCH || 'gh-pages'
const remote = process.env.GH_PAGES_REMOTE || 'origin'

/** Names under website/ that must not be published as source. */
const SOURCE_SKIP = new Set([
  'node_modules',
  'dist',
  '.vite',
  '.git',
  '.DS_Store',
])

function run(cmd, args, opts = {}) {
  const result = spawnSync(cmd, args, {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    ...opts,
  })
  if (result.status !== 0) {
    const detail = [result.stdout, result.stderr].filter(Boolean).join('\n')
    throw new Error(`${cmd} ${args.join(' ')}\n${detail}`)
  }
  return (result.stdout || '').trim()
}

function git(args, cwd) {
  return run('git', args, { cwd })
}

/**
 * Copy directory tree, skipping names in `skip` at every level for top-level only
 * when skipTopLevel is used for website root.
 */
function copySource(fromDir, toDir) {
  mkdirSync(toDir, { recursive: true })
  for (const name of readdirSync(fromDir)) {
    if (SOURCE_SKIP.has(name)) continue
    const from = join(fromDir, name)
    const to = join(toDir, name)
    const st = statSync(from)
    if (st.isDirectory()) {
      cpSync(from, to, { recursive: true })
    } else {
      cpSync(from, to)
    }
  }
}

if (!existsSync(join(distDir, 'index.html'))) {
  console.error('dist/ is missing. Run `npm run build` first.')
  process.exit(1)
}

const remoteUrl = git(['remote', 'get-url', remote], repoRoot)
console.log('Deploying website source + static build')
console.log(`  source: ${websiteRoot}`)
console.log(`  static: ${distDir} → docs/`)
console.log(`  → ${remote}/${branch} (${remoteUrl})`)

const work = mkdtempSync(join(tmpdir(), 'moonward-gh-pages-'))

try {
  git(['init'], work)
  git(['checkout', '-b', branch], work)

  // 1) Vue project source at branch root
  copySource(websiteRoot, work)

  // 2) Production site under docs/ (GitHub Pages "docs folder")
  const docsDir = join(work, 'docs')
  mkdirSync(docsDir, { recursive: true })
  for (const name of readdirSync(distDir)) {
    cpSync(join(distDir, name), join(docsDir, name), { recursive: true })
  }
  writeFileSync(join(docsDir, '.nojekyll'), '')
  // Also at branch root so / and /docs both avoid Jekyll if settings change
  writeFileSync(join(work, '.nojekyll'), '')

  git(['add', '-A'], work)

  const status = git(['status', '--porcelain'], work)
  if (!status) {
    console.log('Nothing to deploy (empty tree).')
    process.exit(0)
  }

  try {
    git(['config', 'user.name'], work)
  } catch {
    git(['config', 'user.name', 'Moonward Deploy'], work)
    git(['config', 'user.email', 'moonward-deploy@users.noreply.github.com'], work)
  }

  try {
    const name = git(['config', 'user.name'], repoRoot)
    const email = git(['config', 'user.email'], repoRoot)
    if (name) git(['config', 'user.name', name], work)
    if (email) git(['config', 'user.email', email], work)
  } catch {
    /* ignore */
  }

  git(
    [
      'commit',
      '-m',
      'Deploy Moonward website (source + docs static build)',
    ],
    work,
  )
  git(['remote', 'add', 'origin', remoteUrl], work)
  git(['push', '--force', 'origin', `HEAD:${branch}`], work)

  console.log(`Done. Branch '${branch}' updated on ${remote}.`)
  console.log('Contains: Vue source at root + production files in docs/')
  console.log('GitHub → Settings → Pages → Branch: gh-pages / docs')
} finally {
  rmSync(work, { recursive: true, force: true })
}
