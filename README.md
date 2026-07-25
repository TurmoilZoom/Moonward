# Moonward 展示页

本分支（`gh-pages`）存放展示页源码与构建产物。

- **分支根目录**：Vue 3 + Vite 源码  
- **`docs/`**：`npm run build` 产物，供 GitHub Pages 使用  

## 本地预览（推荐）

```powershell
cd D:\fork\starward\Starward-gh-pages
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
npm run build     # 构建到 dist/
npm run preview   # 预览构建产物
npm run deploy    # 构建并推送到远程 gh-pages
```

GitHub Pages：**Settings → Pages → Branch `gh-pages` / `/docs`**

站点：`https://turmoilzoom.github.io/Moonward/`