# Moonward 展示页

Vue 3 + Vite 单页展示站。`npm run deploy` 会把**源码 + 构建产物**一并推到 `gh-pages` 分支。

## 开发

```powershell
cd website
npm install
npm run dev
```

本地预览默认：`http://localhost:5173/Moonward/`

## 构建

```powershell
npm run build
```

输出目录：`website/dist/`（本地构建缓存，不进 git；部署时复制为分支上的 `docs/`）

## 部署到 gh-pages

```powershell
npm run deploy
```

该命令会：

1. 执行 `vite build` 生成 `dist`
2. 用 `scripts/deploy.mjs` 强制推送到远程 `gh-pages`：
   - **分支根目录** = 本目录源码（`package.json`、`src/`、`public/`、`scripts/` …，不含 `node_modules` / `dist`）
   - **`docs/`** = 生产静态站点（供 GitHub Pages 托管）

### `gh-pages` 分支结构

```
gh-pages
├── package.json      # 源码
├── vite.config.js
├── index.html
├── src/
├── public/
├── scripts/
├── README.md
├── .nojekyll
└── docs/             # 构建产物（Pages 从这里发布）
    ├── index.html
    ├── assets/
    ├── screenshots/
    └── …
```

### GitHub Pages 设置

仓库 **Settings → Pages**：

- Source: **Deploy from a branch**
- Branch: **`gh-pages`** / **`/docs`**

访问地址（项目站）：

`https://turmoilzoom.github.io/Moonward/`

`vite.config.js` 中 `base` 已设为 `/Moonward/`，与仓库名一致。若改用自定义域名或用户站，请同步修改 `base`。

### 与开发分支的关系

| 位置 | 内容 |
|------|------|
| `rebase/develop` 的 `website/` | 日常开发推荐保留一份源码（与主仓一起变基/评审） |
| `gh-pages` | 展示页专用：源码 + `docs` 静态站，可单独浏览/改站点 |

两边源码以你实际维护流程为准；改完展示页后执行 `npm run deploy` 即可更新 `gh-pages`。

## 更新截图

将 PNG 放入 `public/screenshots/`，并在 `src/data/content.js` 中更新引用，然后 `npm run deploy`。
