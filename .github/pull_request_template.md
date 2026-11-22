<!--
感谢贡献！提交前请确认：
- 目标分支是 `rebase/develop`；`main` 只跟踪上游，不接受开发 PR
- 一个 PR 只做一件事；较大的改动请先开 Issue 讨论方案
- 详见 CONTRIBUTING.md

Thanks for contributing! Please target `rebase/develop` (not `main`), keep one change per PR,
and open an issue first for larger changes. See CONTRIBUTING.md for details.
-->

## 改动说明 / Description

<!-- 做了什么、为什么做。界面改动请附截图或录屏。 -->
<!-- What does this change and why? Attach screenshots or a recording for UI changes. -->

## 关联 Issue / Related issue

<!-- 例如 Closes #123；没有可删除本节。 -->
<!-- e.g. Closes #123. Remove this section if none. -->

## 改动类型 / Type of change

- [ ] 修复 Bug / Bug fix
- [ ] 新功能 / New feature
- [ ] 改进 / Improvement
- [ ] 文档 / Documentation
- [ ] 翻译 / Translation
- [ ] 重构或其他 / Refactor or other

## 测试方式 / How was this tested?

<!-- 在哪些游戏、区服和系统版本上验证过；如何复现问题并确认修复。 -->
<!-- Which games, servers and Windows versions you tested on; how to reproduce and verify. -->

## 检查清单 / Checklist

<!-- 不涉及的项可直接勾选。 / Tick items that do not apply. -->

- [ ] 基于最新的 `rebase/develop` 创建分支 / Branched from the latest `rebase/develop`
- [ ] `dotnet build` 通过，0 error / `dotnet build` passes with 0 errors
- [ ] 用户可见文案未硬编码；新增词条已补全所有语言的 `Lang.*.resx` 并手动更新 `Lang.Designer.cs` / No hardcoded user-visible strings; new keys added to every `Lang.*.resx` and `Lang.Designer.cs`
- [ ] 新增的 DTO 已注册到对应 `JsonContext` / New DTOs are registered in their `JsonContext`
- [ ] 修改数据库结构时新增了 `Sql_vN` 迁移，未改动已发布的迁移 / Schema changes add a new `Sql_vN` migration without editing released ones
- [ ] 改到 `tests/Starward.Tests` 覆盖的代码时已运行 `dotnet test` / Ran `dotnet test` when touching code covered by `tests/Starward.Tests`
- [ ] 未包含 `bin/`、`obj/`、日志等无关文件 / No `bin/`, `obj/`, logs or other unrelated files
