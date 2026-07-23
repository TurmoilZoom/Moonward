/**
 * Deploy this branch (source + docs/) to remote gh-pages.
 * Run from branch root: npm run deploy
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
const distDir = join(websiteRoot, 'dist')
const branch = process.env.GH_PAGES_BRANCH || 'gh-pages'
const remote = process.env.GH_PAGES_REMOTE || 'origin'

const SOURCE_SKIP = new Set([
  'node_modules',
  'dist',
  '.vite',
  '.git',
  '.vs',
  '.DS_Store',
  'docs',
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

function git(args, cwd = websiteRoot) {
  return run('git', args, { cwd })
}

function copySource(fromDir, toDir) {
  mkdirSync(toDir, { recursive: true })
  for (const name of readdirSync(fromDir)) {
    if (SOURCE_SKIP.has(name)) continue
    const from = join(fromDir, name)
    const to = join(toDir, name)
    cpSync(from, to, { recursive: statSync(from).isDirectory() })
  }
}

if (!existsSync(join(distDir, 'index.html'))) {
  console.error('dist/ is missing. Run `npm run build` first.')
  process.exit(1)
}

const remoteUrl = git(['remote', 'get-url', remote])
console.log('Deploying source + docs static build')
console.log(`  → ${remote}/${branch} (${remoteUrl})`)

const work = mkdtempSync(join(tmpdir(), 'moonward-gh-pages-'))

try {
  git(['init'], work)
  git(['checkout', '-b', branch], work)

  copySource(websiteRoot, work)

  const docsDir = join(work, 'docs')
  mkdirSync(docsDir, { recursive: true })
  for (const name of readdirSync(distDir)) {
    cpSync(join(distDir, name), join(docsDir, name), { recursive: true })
  }
  writeFileSync(join(docsDir, '.nojekyll'), '')
  writeFileSync(join(work, '.nojekyll'), '')

  git(['add', '-A'], work)
  const status = git(['status', '--porcelain'], work)
  if (!status) {
    console.log('Nothing to deploy.')
    process.exit(0)
  }

  try {
    const name = git(['config', 'user.name'])
    const email = git(['config', 'user.email'])
    if (name) git(['config', 'user.name', name], work)
    if (email) git(['config', 'user.email', email], work)
  } catch {
    git(['config', 'user.name', 'Moonward Deploy'], work)
    git(['config', 'user.email', 'moonward-deploy@users.noreply.github.com'], work)
  }

  git(['commit', '-m', 'Deploy Moonward website (source + docs)'], work)
  git(['remote', 'add', 'origin', remoteUrl], work)
  git(['push', '--force', 'origin', `HEAD:${branch}`], work)

  console.log(`Done. Updated ${remote}/${branch}`)
  console.log('Pages: Settings → gh-pages → /docs')
} finally {
  rmSync(work, { recursive: true, force: true })
}
