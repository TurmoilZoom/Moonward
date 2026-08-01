---
name: tag-release
description: >
  为当前（或指定）提交创建 annotated tag，推送到仓库全部远程，并生成相对上一 tag 的改动总结。
  推送到 GitHub origin 会触发 .github/workflows/release.yml，自动 Velopack 打包并创建 GitHub Release。
  Use when the user runs /tag-release, or asks to 打 tag、发版、打版本号、release tag、
  推送 tag 到所有远程、给最新提交打 tag、创建 release.
---

# Tag Release（全远程）

为指定提交创建 **annotated tag**，**推送到每一个 git remote**，再输出相对上一 tag 的 Markdown 改动总结。

**不**改业务代码、**不**改工作区文件（除创建/推送 tag 外）。**禁止** `git tag -f` / `git push --force` 覆盖已有 tag，除非用户明确要求强制覆盖。

## 本仓库要点

| 项 | 约定 |
|----|------|
| 命名 | 日历版 `年.月.序号`，如 `2026.8.1`；预览版带 `-`，如 `2026.8.2-beta1` |
| 远程 | 通常有 `origin`（GitHub `TurmoilZoom/Moonward`）与 `cnb`（CNB 镜像）；**两个都要推 tag** |
| GitHub Release | **push tag 到 origin** 触发 `release.yml`：Velopack 打 win-x64/win-arm64，创建 Release |
| 正式 / 预览 | tag 名**不含** `-` → 正式 Release；**含** `-` → GitHub pre-release（应用「加入预览更新」） |
| 分支 | 日常发布多在 `rebase/develop` 的 tip；确认 HEAD 即要发布的提交 |

手动补发可用 Actions 的 `workflow_dispatch`（输入版本号，不必再 push tag）。

## 步骤

### 1. 收集状态（仓库根目录）

```powershell
git branch --show-current
git log -1 --pretty=format:"%H%n%h %s%n%ci"
git status --short
git remote -v
git tag --sort=-v:refname
# 上一 tag 以来的提交（有 tag 时）
$prev = git describe --tags --abbrev=0 2>$null
if ($prev) { git log "$prev..HEAD" --oneline } else { git log --oneline -20 }
```

若本地分支与其上游不一致，先 `git status -sb` / `git fetch` 对齐；用户要「远程最新提交」时，确保本地 tip 已是 `origin/<branch>`（或先 fetch + checkout 该 commit），再打 tag。

### 2. 确定 tag 名与目标提交

- **目标提交**：默认 `HEAD`。用户指定了 hash / 分支 / 已有 ref 则用其 `git rev-parse` 结果。
- **tag 名**：
  1. 用户已给出（如 `2026.8.2`、`2026.8.2-beta2`）→ 使用。
  2. 未给出 → 按已有 tag 规律提议下一个名（正式 / beta 问清），**等用户确认后再创建**。
- **占用检查**：`git tag -l <name>` 与 `git ls-remote --tags <remote> <name>`（至少一个主要远程）。已存在则停止，说明如何删/换名；**不要**默认 `-f`。

向用户简短确认（若尚未明确）：

- tag 名、目标 commit 短哈希与 subject  
- 正式版还是预览版（是否含 `-`，以及会触发正式 Release 还是 pre-release）  
- 将推送到的远程列表  

### 3. 安全检查

- 工作区非空：提醒这些改动**不会**进 tag，问清是先提交再打，还是仍对当前 HEAD 打。
- 目标 commit 未推到任何远程：提醒 tag 指向的提交可能别人拉不到；建议先把分支推到各远程，或确认仅本地发版。
- 在 `main` 等非常用发布分支上：提醒确认。
- 打正式版（无 `-`）前，可再确认用户是否故意跳过 beta。

### 4. 撰写 tag 注释并创建

阅读 `$prev..$target` 的 log，用简体中文概括本版要点（与仓库 commit 风格一致），**不要**逐条粘贴全部 commit subject。

```powershell
$tag = "<tagname>"
$target = "HEAD"   # 或完整 hash
# 多行注释：标题 + 空行 + 要点
$msg = @"
$tag：一句话概述

- 要点一
- 要点二
"@
git tag -a $tag -m $msg $target
```

若环境支持 HEREDOC 且更稳，也可用 stdin：

```bash
git tag -a <tagname> -F - <<'EOF'
<tagname>：一句话概述

- 要点一
- 要点二
EOF
```

创建后校验：

```powershell
git rev-parse "${tag}^{}"
git log -1 --oneline $tag
```

### 5. 推送到全部远程（核心）

```powershell
$tag = "<tagname>"
$remotes = @(git remote)
if ($remotes.Count -eq 0) { throw "没有任何 remote，无法推送 tag" }

$ok = @(); $fail = @()
foreach ($r in $remotes) {
  git push $r "refs/tags/$tag"
  if ($LASTEXITCODE -eq 0) { $ok += $r } else { $fail += $r }
}
# 汇总 $ok / $fail，失败的不要当成成功
```

规则：

- **每个** `git remote` 都推这一条 tag；不要只推 `origin`。
- 使用 `git push <remote> refs/tags/<tag>`（或 `git push <remote> <tag>`），**不要**默认 `git push --tags`（会把本地其它未推送 tag 一并推上）。
- **禁止** `--force` / 删除远程 tag 后重推，除非用户明确要求覆盖。
- 某一远程失败：记录错误，**继续**推其余远程，最后汇总成功/失败。
- `origin`（GitHub）推送成功 ⇒ 预期触发 **Release** workflow；可提示用户到 Actions 查看，并说明正式/预览由 tag 名是否含 `-` 决定。
- `cnb` 等镜像远程：推送 tag 用于同步；CNB 资产镜像由 release job 成功后再触发，**不**依赖本步 cnb tag 才发 GitHub 包。

### 6. 改动总结（Markdown）

对比范围：`<上一 tag>..<新 tag>`（无上一 tag 则从首次提交起并注明）。

可直接归纳，或按 **release-notes** skill 的分组与文风输出（面向用户、**无 emoji**）：新功能 / 问题修复 / 体验与性能 / 重要变更 / 文档与其他。

GitHub compare（origin 为 GitHub 时）：

`https://github.com/<owner>/<repo>/compare/<旧 tag>...<新 tag>`

### 7. 汇报

- 创建的 tag 名、指向的完整/短 hash、subject  
- **逐个远程**的推送结果（成功 / 失败原因）  
- 是否已触发（或预期触发）GitHub Release（仅 origin 成功时）  
- 正式版 vs pre-release  
- 改动总结 Markdown  
- 任一步失败：如实给出命令与输出，不谎报成功  

## 示例触发

- `/tag-release`
- `/tag-release 2026.8.2`
- 「给最新提交打 tag 并推到所有远程」
- 「发 2026.8.2-beta2 预览版」
- 「打 tag 发版」

## 反例（不要做）

- 只推 `origin` 而漏掉 `cnb` 或其他 remote  
- 未确认就擅自决定正式版号  
- `git tag -f` / `git push --force` 覆盖已有 tag（除非用户明确要求）  
- 工作区脏时不提醒就打 tag  
- 把 `git log` 原样当发布说明  
- 在汇报里写「已发布」但 origin push 实际失败  
- 修改业务源码或 `global.json` / NuGet 版本来「配合发版」（版本号由 tag / CI 的 `-p:Version=` 注入）
