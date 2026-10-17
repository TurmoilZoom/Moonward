# Git Worktree

新建 worktree 时，工作树放在主仓库**同级**的 `Starward.worktrees/` 下，不要放进主仓库内部或与主仓并列散落。可参考当前结构：

```
../                         # 主仓库的父目录（例：…/fork/starward）
  Starward/                 # 主工作区（本仓，日常分支 rebase/develop）
  Starward.worktrees/       # 全部附加 worktree 的根目录
    <name>/                 # 例：功能分支、并行任务工作树
```

```powershell
# 在主仓库根目录执行
git worktree add ../Starward.worktrees/<name> <branch>
```
