---
name: commit-push
description: >
  撰写提交信息并推送到仓库全部远程（origin、cnb 等）。
  可关联某个 Issue，并把 #N 放在提交标题末尾（平台会做成可跳转超链接）。
  提交风格：Conventional Commits 类型英文 + 说明主体中文。
  Use when the user runs /commit-push, or asks to 提交并推送、commit and push、
  推到所有远程、写 commit message 并 push、关联 issue 提交.
---

# Commit Push（全远程）

根据当前工作区改动撰写**一条**提交信息，创建提交，并**推送到每一个 git remote**。

**不**改业务代码逻辑（只做 git add / commit / push）。**禁止** `--force` / `--no-verify`，除非用户明确要求。

## 本仓库要点

| 项 | 约定 |
|----|------|
| 提交信息 | 类型英文 Conventional Commits，说明主体**简体中文**（见 `AGENTS.md` / `CONTRIBUTING.md`） |
| 常用类型 | `feat` / `fix` / `docs` / `refactor` / `improve` / `remove` / `chore` 等 |
| 示例 | `feat: 为首页添加功能图钉`；`fix: 修复自定义背景在分辨率变化后回退的问题` |
| 关联 Issue | 用户指定了 Issue 时，标题末尾加 `#N`（GitHub 会做成可跳转链接；见「3. 撰写提交信息」） |
| 远程 | 通常有 `origin`（GitHub）与 `cnb`（CNB 镜像）；**两个都要推当前分支** |
| 日常分支 | 多在 `rebase/develop`；在 `main` / `master` 上操作前须确认 |
| 不提交 | `bin/`、`obj/`、日志；勿把无关大范围重构塞进同一提交 |

## 步骤

### 1. 收集状态（仓库根目录）

```powershell
git branch --show-current
git status -sb
git status --short
git remote -v
git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null
git diff --cached --stat
git diff --stat
git log --oneline -10
```

有暂存改动时再读 `git diff --cached`；仅未暂存时读 `git diff`。**根据实际 diff 写说明**，不要只看文件名。

### 2. 确定要提交的内容

- **已有暂存**：默认只提交暂存区。
- **暂存为空、但有未暂存/未跟踪**：先 `git add` 相关文件（用户未限定范围时可用 `git add -A`，但避开明显不该提交的产物）。
- **完全没有改动**：停止并告知「没有需要提交的改动」，**不要**空提交。
- **一次只做一件事**：若 diff 混入无关改动，拆开或先问用户要提交哪些路径。

### 3. 撰写提交信息

匹配本仓库风格（对照最近 `git log`）：

```
<type>: <中文短描述>
```

用户要求关联 Issue 时（见下），标题为：

```
<type>: <中文短描述> #N
```

复杂改动可加正文要点（中文），说明「为什么 / 做了什么」，勿逐文件罗列。

```powershell
# 多行示例（PowerShell here-string）
$msg = @"
fix: 导出 UIGF 时规范化归档 lang

- 避免 hk4e_ugc 等空字符串导致下游异常
"@
git commit -m $msg
```

若环境支持 HEREDOC：

```bash
git commit -m "$(cat <<'EOF'
fix: 导出 UIGF 时规范化归档 lang

- 避免 hk4e_ugc 等空字符串导致下游异常
EOF
)"
```

规则：

- 标题一句话即可；类型与历史提交一致（`feat` / `fix` / `improve` …）。
- **不要**默认追加 `Co-Authored-By`（除非用户要求）。
- **不要** `--no-verify`；钩子失败则排查，不绕过。

#### 关联 Issue

仅在用户**明确指定** Issue 时附加（命令参数、本轮对话里的编号/链接，或「关联 #N」）。**不要**根据文件名或 diff 自行猜测编号。未指定则标题不加 `#N`。

识别：从用户输入取出编号 `N`（`#42`、纯数字 `42`、或 URL 里的 `/issues/42` `/pull/42`）。多个编号只关联用户点名的那些，按顺序空格写在标题最后：`#12 #34`。

标题写法（`#N` 紧挨末尾，前面一个空格）：

```
fix: 修复自定义背景在分辨率变化后回退的问题 #42
```

```powershell
$msg = @"
fix: 修复自定义背景在分辨率变化后回退的问题 #42

- 分辨率变化后按当前窗口重算背景裁剪
"@
git commit -m $msg
```

GitHub / 多数托管平台会把提交标题里的 `#42` 做成指向该仓库 Issue #42 的超链接，**不要**写成完整 `https://…/issues/42`。

能跑 `gh` 时可校验编号存在：

```powershell
gh issue view <N> --json url,title,state
```

校验失败（不存在 / 无权限）则告知用户，问清是否仍写 `#N`；不要默默换成别的编号。

- 只「关联」：标题末尾 `#N` 即可，**不要**额外加 `Closes` / `Fixes`。
- 用户明确要求关闭该 Issue：标题仍是 `#N`；正文另起一行写 `Closes #N`（GitHub 在默认分支上才会关）。
- 不要写成 `(#42)`、不要再叠完整 URL，也不要把引用只放在正文。

### 4. 安全检查

- 在 `main` / `master` / 默认分支上：**先停下来确认**是否真要直接提交并推送。
- 工作区仍有未纳入本次提交的改动：提交后在汇报里提醒。
- 敏感文件（密钥、本地路径配置等）误入暂存：先移出，再提交。

### 5. 提交并推送到全部远程

```powershell
# 先完成 commit（见上）

$branch = git branch --show-current
$remotes = @(git remote)
if ($remotes.Count -eq 0) { throw "没有任何 remote，无法推送" }

$hasUpstream = $false
git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { $hasUpstream = $true }

$ok = @(); $fail = @()
$first = $true
foreach ($r in $remotes) {
  if ($first -and -not $hasUpstream) {
    git push -u $r HEAD
  } else {
    git push $r $branch
  }
  if ($LASTEXITCODE -eq 0) { $ok += $r } else { $fail += $r }
  $first = $false
}
# 汇总 $ok / $fail
```

规则：

- **每个** `git remote` 都推当前分支；不要只推 `origin` 或无参数的 `git push`（后者只推上游对应远程）。
- 无上游时：对**第一个**远程 `git push -u <remote> HEAD` 建立跟踪；其余 `git push <remote> <branch>`。
- 非快进被拒：**不要** `--force`；记录失败原因，继续其余远程，最后汇总，并提示可能需要先 `git pull --rebase`。
- 某一远程失败不算「全部成功」。

### 6. 汇报

- 提交标题、短/完整 hash  
- 若关联了 Issue：标题末尾的 `#N`，以及是否在正文写了 `Closes #N`  
- **逐个远程**推送结果（如 `origin/rebase/develop`、`cnb/rebase/develop`）  
- 仍留在工作区的未提交改动（若有）  
- 任一步失败：如实给出命令与输出，不谎报成功  

## 示例触发

- `/commit-push`
- `/commit-push 只提交 Features/SignIn 相关`
- `/commit-push #42`
- `/commit-push 关联 https://github.com/TurmoilZoom/Moonward/issues/42`
- 「提交并推送到所有远程」
- 「写 commit message 然后 push」
- 「提交并关联 issue 42」

## 反例（不要做）

- 只推 `origin` 而漏掉 `cnb` 或其他 remote  
- 无改动仍 `git commit --allow-empty`  
- `git push --force` / `git commit --no-verify`（除非用户明确要求）  
- 英文-only 标题或与仓库风格不符的长英文 subject（本仓说明主体用中文）  
- 用户指定了 Issue，却漏掉标题末尾的 `#N`、改成完整 URL、写成 `(#N)`、或只写在正文  
- 用户没提 Issue 却自行加 `#N`，或猜错编号  
- 只「关联」却擅自写 `Closes` / `Fixes`  
- 把不相关改动塞进同一提交  
- 提交 `bin/`、`obj/`、日志或用户数据  
- 汇报「已推送到全部远程」但部分 remote 实际失败  
