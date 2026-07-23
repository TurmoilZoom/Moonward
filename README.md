# Moonward 展示页

本分支（`gh-pages`）存放展示页源码与构建产物。

- **分支根目录**：Vue 3 + Vite 源码  
- **`docs/`**：`npm run build` 产物，供 GitHub Pages 使用  

## 开发

```powershell
npm install
npm run dev
```

本地：`http://localhost:5173/Moonward/`

## 构建

```powershell
npm run build
```

输出到 `dist/`。部署脚本会同步到 `docs/`。

## 部署

```powershell
npm run deploy
```

推送到远程 `gh-pages`（源码 + `docs/`）。

GitHub Pages：**Settings → Pages → Branch `gh-pages` / `/docs`**

站点：`https://turmoilzoom.github.io/Moonward/`
