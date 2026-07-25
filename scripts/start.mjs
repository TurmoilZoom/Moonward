/**
 * 一条命令本地跑官网：缺依赖则安装，再启动 Vite 开发服并打开浏览器。
 * 用法（在 worktree 根目录）: npm start
 */
import { existsSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

// scripts/ -> 站点根目录
const root = fileURLToPath(new URL('..', import.meta.url))
const isWin = process.platform === 'win32'

function run(command, args, shell = false) {
  const result = spawnSync(command, args, {
    cwd: root,
    stdio: 'inherit',
    shell,
  })
  if (result.error) {
    console.error(result.error)
    process.exit(1)
  }
  if (result.status) {
    process.exit(result.status ?? 1)
  }
}

if (!existsSync(join(root, 'node_modules', 'vite'))) {
  console.log('Installing dependencies…')
  // Windows 上 npm 需 shell 才能解析 .cmd
  run(isWin ? 'npm.cmd' : 'npm', ['install'], isWin)
}

const viteCli = join(root, 'node_modules', 'vite', 'bin', 'vite.js')
if (!existsSync(viteCli)) {
  console.error('vite not found after install. Check package.json devDependencies.')
  process.exit(1)
}

// 不用 shell，避免 Program Files 路径被空格拆断
run(process.execPath, [viteCli, '--open', '/Moonward/'], false)