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

## 相关文档页

顶栏、安装区与页脚的「相关文档」进 `#/docs`：项目 wiki 里已经写好的文档，按使用指南 / 实现原理 / 接口与凭证 / 参与开发分成四组，点开是单篇阅读页（纸面排版、侧边目录、阅读进度、上一篇下一篇）。

- 路由用 hash：`#/docs`、`#/docs/<slug>`、`#/docs/<slug>/<标题锚点>`。带前导斜杠，与首页的页内锚点（`#install`、`#screens/<id>`）互不干扰，GitHub Pages 也不需要 SPA 回退
- 正文提交在 `src/wiki/*.md`，构建时按篇拆成独立 chunk，点开哪篇加载哪篇，首页不背这些体积
- 展示用的标题、摘要、分组写在 `src/data/docs.js`；更新日期与篇幅由同步脚本写进 `src/wiki/manifest.json`
- 渲染用 `src/utils/markdown.js` 里的 `renderDocMarkdown`：标题锚点按 GitHub 规则生成，支持表格、代码块、引用、嵌套列表；原始 HTML 只放行 `<img>`，其余按文本转义

### wiki 更新后同步

```powershell
node scripts/sync-wiki.mjs
```

克隆 `Moonward.wiki.git`，把 `PAGES` 里列出的页面写进 `src/wiki/`，顺带清掉 wiki 编辑器留下的痕迹（正文前的 HTML 粘贴块、目录里的嵌套 `_edit` 链接）。跑完检查 `git diff` 再提交。**不在构建期联网抓 wiki**：CI 只跑 `npm ci && npm run build`，而且 `raw.githubusercontent.com` 在部分网络下不可达。

新增一篇：脚本的 `PAGES` 里加「wiki 页面名 → slug」，`src/data/docs.js` 的 `entries` 里加标题、摘要与分组，再跑一次脚本。

文档里的插图仍指向 `github.com/user-attachments`，加载不出来时会就地换成一条去 wiki 看原图的链接。

## 其他命令

```powershell
npm run dev       # 仅开发服（不自动装依赖、不自动开浏览器）
npm run build     # 构建到 dist/（CI 用同一命令）
npm run preview   # 预览构建产物
node scripts/sync-wiki.mjs   # 从 wiki 同步「相关文档」正文
```

## 部署（自动）

推送源码到 `gh-pages` 分支即触发 `.github/workflows/deploy-pages.yml`：`npm ci` → `npm run build` → 上传 `dist/` 为 Pages artifact → 发布。**产物只作为 artifact 提供，不回写任何分支。**

一次性设置（在 GitHub 仓库）：

1. **Settings → Pages → Build and deployment → Source** 选 **GitHub Actions**（原为 Branch `gh-pages` `/docs`）。
2. 若首次运行报 *"Branch 'gh-pages' is not allowed to deploy to github-pages"*，到 **Settings → Environments → `github-pages` → Deployment branches** 把 `gh-pages` 加入允许列表。

也可在 Actions 页对本工作流手动 **Run workflow**（选 `gh-pages` 分支）。

站点：`https://turmoilzoom.github.io/Moonward/`
