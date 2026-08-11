---
name: release-notes
description: >
  对比本次 tag 与上一个 tag，生成普通用户能看懂的 Markdown 发布说明 / 改动总结。
  根据 git log、提交类型与文件改动归纳新功能、修复、改进等，避免堆砌原始 commit 列表。
  与 tag-release 集成：发版时生成的全文会写入 annotated tag，由 release.yml 填入 GitHub Release。
  Use when the user runs /release-notes, or asks for tag changelog, release notes,
  版本改动总结, 发布说明, 两个 tag 之间的变化.
---

# Release Notes

为两个 git tag（或「上一 tag ↔ 待发布提交」）之间的改动生成**面向普通用户**的 Markdown 发布说明。

**默认只读** git 历史：**不**创建 tag、**不**推送、**不**改工作区。  
由 **tag-release** 调用时：生成结果会写入 annotated tag 注释，供 CI 填入 GitHub Release body（见文末「与 tag-release 集成」）。

## 默认行为

| 项 | 默认 |
|----|------|
| 对比范围 | 最新 tag ↔ 再上一个 tag |
| 指定 tag | 用户给了 `v1.2.3` / `2026.6.4` 等 → 用该 tag ↔ 它的上一个 tag |
| 发版前（tag-release） | 新 = 待打 tag 的提交（多为 `HEAD`）；旧 = 上一 tag |
| 语言 | 简体中文 |
| 受众 | 普通用户（少术语，讲「能做什么 / 修了什么」） |
| 格式 | 纯文本 Markdown 分类小节与列表；**无**首部大标题/日期；**不使用任何 emoji / 图标** |

用户可额外指定：对比区间（如 `A..B`）、输出路径、语言。

## 步骤

### 1. 确定对比的两个 ref

在仓库根目录执行：

```powershell
# 按版本排序的 tag（日历版 年.月.序号 或 semver 都尽量可用）
git tag --sort=-v:refname
# 若上面结果怪异，再试创建时间
git tag --sort=-creatordate
```

解析规则：

1. **用户指定了两个 tag**（如 `2026.6.3` 和 `2026.6.4`，或 `A..B` / `A...B`）→ 旧 = A，新 = B。
2. **用户只指定了一个 tag** → 新 = 该 tag；旧 = 在排序列表里紧挨其前的那个（比它旧的最近一个）。
3. **用户未指定** → 新 = 最新 tag；旧 = 第二新 tag。
4. **tag-release 发版前**（新 tag 尚未创建）→ 新 = 目标提交（`HEAD` 或指定 hash）；旧 = `git describe --tags --abbrev=0`（无则见下条）。
5. **仓库只有一个 tag / 无上一 tag** → 旧 = 首次提交（`git rev-list --max-parents=0 HEAD`）；可在正文末脚注一句「首个正式版本 / 无可对比的上一 tag」，**不要**因此加首部大标题。
6. **没有任何 tag 且非发版场景** → 停止并告知用户先打 tag，或请其指定两个 commit/分支再总结。

确认两个 ref 都存在：

```powershell
git rev-parse --verify <old>^{}
git rev-parse --verify <new>^{}
```

### 2. 收集原始材料（只读）

```powershell
# 提交列表（含作者，便于判断，勿原样贴给用户）
git log <old>..<new> --pretty=format:"%h|%s|%an|%ae" --no-merges

# 若几乎为空，可去掉 --no-merges 再取一次
git log <old>..<new> --pretty=format:"%h|%s|%an"

# 文件级统计（辅助判断影响面，不写进用户正文细表）
git diff <old>..<new> --stat

# 远程（可选 compare 链接）
git remote get-url origin
```

若 commit 很多（例如 >80），优先按 Conventional Commits 前缀与路径聚类，再抽样读关键提交的 body（`git show -s --format=%B <hash>`），不要把上百条标题丢给用户。

### 3. 归纳（不要照抄 commit 列表）

按**用户收益**合并同类项，而不是逐条翻译 `%s`。

**分组映射（无内容的组整节省略；标题只用纯文字，禁止 emoji）：**

| 提交线索 | 用户可见分组 |
|----------|----------------|
| `feat` / 新功能 / 新增 UI·能力 | 新功能 |
| `fix` / 修复 / 崩溃·错误 | 问题修复 |
| `improve` / `perf` / 体验·性能 | 体验与性能 |
| `refactor` / 重构（仅当用户可感知时写） | 内部改进 |
| `docs` / 文档 | 文档 |
| `chore` / 构建 / 依赖 / CI | 通常**省略**；仅当影响安装、更新、兼容性时写入「其他」 |
| 其他 / 无法归类 | 其他 |

写作规则：

- **用完整短句**，说明「用户会看到什么 / 能做什么 / 什么毛病没了」。
- **合并**同一功能的多条提交为一句。
- **去掉**无意义前缀堆砌（如连续 `fix: fix: ...`）、纯内部重命名、纯格式化。
- **少用**路径、类名、API 名；必要时用产品用语（如「启动页」「签到」「抽卡记录」）。
- 安全/隐私相关改动写清楚，但不渲染 exploit 细节。
- 破坏性变更单独标出：**重要变更**（需重装、改设置、行为不兼容等）。
- **禁止**在输出 Markdown 中使用任何 emoji、图标符号（如 ✨ 🐛 ⚡ 🔧 📝 📦 ⚠️ 等）。

### 4. 输出 Markdown

默认**打印到对话**；若用户要求写入文件，再写入指定路径（如 `RELEASE_NOTES.md` 或 `docs/releases/<tag>.md`），**先确认路径**再写，避免覆盖未约定文件。

结构模板（**无 emoji**；**不要**在正文首部写大标题、版本号或日期，**直接**从分类小节开始）：

```markdown
### 新功能

- …

### 问题修复

- …

### 体验与性能

- …

### 重要变更

- …（可省略整节）

### 文档 / 其他

- …（可省略）

---

共涉及约 N 个提交。  
**完整对比**：[GitHub Compare](<url>)
```

Compare 链接（origin 为 GitHub 时）：

1. 将 `git@github.com:owner/repo.git` 或 `https://github.com/owner/repo.git` 规范为 `https://github.com/owner/repo`
2. 拼：`https://github.com/owner/repo/compare/<旧 tag>...<新 tag>`（三个点）
3. 新 tag 尚未创建时：compare 可用 `<旧 tag>...<新 commit 短哈希>`，或推送 tag 后再写最终链接。

非 GitHub 远程则省略链接，或给用户可用的 web 地址（若可判断）。

### 5. 汇报

简短说明：

- 对比区间：`<旧>..<新>`
- 提交数量（约）
- 已输出的 Markdown（正文）

任一步 git 失败：报告命令与错误，不要编造改动。

## 与 tag-release 集成

`/tag-release` **必须**按本 skill 生成完整用户向 Markdown，并写入 **annotated tag 注释**，供 `.github/workflows/release.yml` 在 `gh release create` 时填入 GitHub Release body。

### Annotated tag 注释格式（固定）

```text
<tagname>：一句话概述

### 新功能
…
### 问题修复
…
```

约定：

| 部分 | 内容 |
|------|------|
| **第一行（subject）** | `<tagname>：一句话概述`（给 `git` / 列表用） |
| **空一行** | 必须，便于 `%(contents:body)` 分离 |
| **其余（body）** | 本 skill 的**完整** Markdown 正文（直接从 `###` 分类起，**无**首部大标题/日期） |

创建示例（PowerShell，`-F` 保证多行与中文稳定）：

```powershell
$tag = "<tagname>"
$target = "HEAD"
# $markdownBody = 按本 skill 生成的完整正文（从 ### 分类起，无首部大标题/日期）
$msg = @"
$tag：一句话概述

$markdownBody
"@
$path = Join-Path $env:TEMP "moonward-tag-$tag.txt"
# 无 BOM UTF-8，避免 GitHub Release 中文乱码
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($path, $msg.TrimEnd() + "`n", $utf8)
# 默认 commentChar=# 会剥掉 ### 分类标题；改用 ; 保留 Markdown 标题
git -c core.commentChar=";" tag -a $tag -F $path $target
Remove-Item $path -Force
```

CI（`release.yml`）读取方式：

```powershell
# 强制 fetch annotated tag 后校验 objecttype=tag，再取 body（避免 lightweight 误用 commit 说明）
git fetch origin "refs/tags/${version}:refs/tags/${version}" --force
git for-each-ref "refs/tags/$version" --format="%(objecttype)"
git for-each-ref "refs/tags/$version" --format="%(contents:body)"
```

### 单独调用 vs 发版调用

| 场景 | 行为 |
|------|------|
| `/release-notes` | 只输出到对话（或用户指定文件）；不打 tag |
| `/tag-release` | 先生成本文 Markdown → 写入 tag 注释 → 推送 → CI 填入 GitHub Release |

## 示例触发

- `/release-notes`
- `/release-notes 2026.6.4`
- 「总结上一个 tag 到现在的发布说明」
- 「生成 2026.6.3 和 2026.6.4 之间的用户向 changelog」

## 反例（不要做）

- 单独调用时不要 `git tag` / `git push` / 改代码（发版走 tag-release）
- 不要把 `git log` 原样当发布说明
- 不要写只有开发者能懂的重构清单（除非用户明确要求技术版）
- 不要在标题或正文中使用 emoji / 图标
- **不要**在正文首部生成 `## 版本号（日期）` 或「与上一版相比…」导语；直接分类总结
- 不要把完整用户向 notes 只打印在对话里却不写进 tag（tag-release 场景）
