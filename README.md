# Moonward 展示页

本分支（`gh-pages`）**只存展示页源码**（Vue 3 + Vite）。构建产物不再提交——由 GitHub Actions 在 CI 里构建并直接发布到 GitHub Pages。

## 本地预览（推荐）

```powershell
cd D:\fork\starward\Starward.worktrees\gh-pages
npm start
```

缺依赖会自动 `npm install`，然后启动开发服并打开浏览器。
地址：`http://localhost:5173/Moonward/`

安装区展示 **Setup / Portable × x64 / ARM64** 直链，并支持 **GitHub / CNB** 下载渠道切换（默认 CNB，与应用内更新源一致；选择会记入 `localStorage`）。

- 清单优先 `api.github.com`（浏览器 CORS 可用）；CNB 列表无 CORS 时用同 tag/文件名改写为 `cnb.cool/.../releases/download/...` 直链
- 失败时可换渠道，或打开对应 Releases 页

## 其他命令

```powershell
npm run dev       # 仅开发服（不自动装依赖、不自动开浏览器）
npm run build     # 构建到 dist/（CI 用同一命令）
npm run preview   # 预览构建产物
```

## 部署（自动）

推送源码到 `gh-pages` 分支即触发 `.github/workflows/deploy-pages.yml`：`npm ci` → `npm run build` → 上传 `dist/` 为 Pages artifact → 发布。**产物只作为 artifact 提供，不回写任何分支。**

一次性设置（在 GitHub 仓库）：

1. **Settings → Pages → Build and deployment → Source** 选 **GitHub Actions**（原为 Branch `gh-pages` `/docs`）。
2. 若首次运行报 *"Branch 'gh-pages' is not allowed to deploy to github-pages"*，到 **Settings → Environments → `github-pages` → Deployment branches** 把 `gh-pages` 加入允许列表。

也可在 Actions 页对本工作流手动 **Run workflow**（选 `gh-pages` 分支）。

站点：`https://turmoilzoom.github.io/Moonward/`
