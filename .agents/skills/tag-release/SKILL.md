---
name: tag-release
description: >
  为当前（或指定）提交创建 annotated tag，推送到仓库全部远程，并按 release-notes skill
  生成用户向发布说明写入 tag 注释；push 到 GitHub origin 触发 release.yml，
  自动 Velopack 打包并将 tag 中的 notes 填入 GitHub Release body。
  Use when the user runs /tag-release, or asks to 打 tag、发版、打版本号、release tag、
  推送 tag 到所有远程、给最新提交打 tag、创建 release.
---

# Tag Release（全远程 + Release Notes）

为指定提交创建 **annotated tag**，**推送到每一个 git remote**，并生成相对上一 tag 的用户向发布说明。

**不**改业务代码、**不**改工作区源码（除创建/推送 tag、以及写临时 notes 文件再删除外）。**禁止** `git tag -f` / `git push --force` 覆盖已有 tag，除非用户明确要求强制覆盖。

## 与 release-notes / CI 的关系

```
/tag-release
  → 按 .agents/skills/release-notes/SKILL.md 生成 Markdown
  → 全文写入 annotated tag 注释（subject + body）
  → push tag 到全部 remote（含 origin）
  → release.yml：Velopack 打包 + gh release create
       └── 从 tag 注释读取 notes，填入 GitHub Release body
```

| 组件 | 职责 |
|------|------|
| **release-notes** skill | 归纳文风、分组、Markdown 结构（**必须完整遵循**） |
| **tag-release**（本 skill） | 确认版本 → 生成 notes → 打 tag → 推送 |
| **release.yml** | 打包资源；`gh release create --notes-file` 使用 tag 注释 |

不要在对话里生成一套 notes、tag 注释里另写简略要点：CI **只**读 tag 注释，对话中的版本不会进 GitHub。

## 本仓库要点

| 项 | 约定 |
|----|------|
| 命名 | 日历版 `年.月.序号`，如 `2026.8.1`；预览版带 `-`，如 `2026.8.2-beta1` |
| 远程 | 通常有 `origin`（GitHub `TurmoilZoom/Moonward`）与 `cnb`（CNB 镜像）；**两个都要推 tag** |
| GitHub Release | **push tag 到 origin** 触发 `release.yml`：Velopack 打 win-x64/win-arm64，创建 Release 并填入 notes |
| 正式 / 预览 | tag 名**不含** `-` → 正式 Release；**含** `-` → GitHub pre-release（应用「加入预览更新」） |
| 分支 | 日常发布多在 `rebase/develop` 的 tip；确认 HEAD 即要发布的提交 |

手动补发可用 Actions 的 `workflow_dispatch`（输入版本号）；notes 仍来自该版本 **annotated tag** 注释（无注释则 workflow 回退 `--generate-notes`）。

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

### 4. 生成发布说明（必须按 release-notes）

**在创建 tag 之前**完成。完整规则见 **`.agents/skills/release-notes/SKILL.md`**（若环境同时加载了用户级 `release-notes`，以仓库内该文件与本集成约定为准）。

对比范围：

- 旧 = `$prev`（`git describe --tags --abbrev=0`；无则首次提交）
- 新 = 目标提交（尚未打 tag 的 `$target`）

必做材料：

```powershell
git log "$prev..$target" --pretty=format:"%h|%s|%an|%ae" --no-merges
git diff "$prev..$target" --stat
```

产出两段文本：

1. **一句话概述**（简体中文，进 tag subject）  
2. **完整 Markdown 正文**（直接从 `### 新功能` 等分类起；**无**首部大标题/日期；**无 emoji**）

正文结构与文风严格遵循 release-notes（新功能 / 问题修复 / 体验与性能 / 重要变更 / 文档与其他；无内容的组省略；面向普通用户）。

### 5. 写入 annotated tag 并创建

**格式固定**（CI 用 `%(contents:body)` 取 Release body）：

```text
<tagname>：一句话概述

### 新功能
…
### 问题修复
…
```

- 第一行 = subject  
- 空一行  
- 其余 = release-notes 全文（直接分类列表，**不要** `## 版本（日期）` 或导语；**不要**只写三五条开发者向 bullet 而把完整 notes 留在对话里）

```powershell
$tag = "<tagname>"
$target = "HEAD"   # 或完整 hash
$summary = "一句话概述"   # 无「$tag：」前缀；写入时再拼
# $markdownBody = 第 4 步生成的完整 Markdown（从 ### 分类起）

$msg = @"
$tag：$summary

$markdownBody
"@

$path = Join-Path ([System.IO.Path]::GetTempPath()) "moonward-tag-$tag.txt"
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($path, $msg.TrimEnd() + "`n", $utf8)
# 默认 commentChar=# 会剥掉 ### 分类标题；改用 ; 保留 Markdown 标题
git -c core.commentChar=";" tag -a $tag -F $path $target
Remove-Item $path -Force
```

创建后校验：

```powershell
git rev-parse "${tag}^{}"
git log -1 --oneline $tag
# 必须是 annotated（objecttype=tag）；lightweight 时 CI 会误读 commit 说明
git for-each-ref "refs/tags/$tag" --format="%(objecttype)"
git for-each-ref "refs/tags/$tag" --format="%(contents:subject)"
git for-each-ref "refs/tags/$tag" --format="%(contents:body)"
```

若 `objecttype` 不是 `tag`、`%(contents:body)` 为空、或写入的 `###` 标题丢失：说明注释未正确写入，**删掉本地 tag 重建**（仅本地、尚未 push 时），不要推送残缺注释。

### 6. 推送到全部远程（核心）

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
- `origin`（GitHub）推送成功 ⇒ 预期触发 **Release** workflow；Release body 将由 workflow 从本 tag 注释写入；可提示用户到 Actions 查看。
- `cnb` 等镜像远程：推送 tag 用于同步；CNB 资产镜像由 release job 成功后再触发，**不**依赖本步 cnb tag 才发 GitHub 包。

### 7. 汇报

- 创建的 tag 名、指向的完整/短 hash、subject  
- **逐个远程**的推送结果（成功 / 失败原因）  
- 是否已触发（或预期触发）GitHub Release（仅 origin 成功时）  
- 正式版 vs pre-release  
- **完整** release notes Markdown（与写入 tag 的 body 一致，便于用户复制）  
- 说明：GitHub Release 正文在 workflow 成功创建 Release 后可见（打包需数分钟）；若 body 为空，检查 tag 是否为 annotated 且含 body  
- 任一步失败：如实给出命令与输出，不谎报成功  

**不必**在 workflow 跑完后再用 `gh release edit` 补 notes（主路径已由 tag → CI 完成）。仅当用户明确要求「workflow 已成功但 notes 仍空 / 要改写」时，再用：

```powershell
gh release edit <tag> --notes-file <path>
```

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
- tag 注释只写三五行要点，完整 notes 只出现在对话里（CI 读不到）  
- 在 notes 正文首部写 `## 版本（日期）` 或「与上一版相比…」导语（直接分类即可）  
- 不遵循 release-notes 的分组与「无 emoji / 面向用户」文风  
- 在汇报里写「已发布」但 origin push 实际失败  
- 修改业务源码或 `global.json` / NuGet 版本来「配合发版」（版本号由 tag / CI 的 `-p:Version=` 注入）
