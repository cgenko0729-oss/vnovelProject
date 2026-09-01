---
name: vn-new-feature
description: 开始做任何新功能/修 bug 前的开发流程：切 feature/<名> 分支、只实现不提交、等用户确认后走 gh pr create、用户合并后收尾、永不删分支。Start any new feature or bugfix — branching workflow, GitHub CLI pull request flow, commit format, git push.
---

# VN 项目新功能开发流程（GitHub CLI + PR 版，2026-09-01 起）

## 何时用我
用户要求开发新功能、修 bug、做任何会产生代码/资产改动的任务时，动手前先过一遍本清单。

## 最重要的一条：四个阶段之间必须停下来等用户发话

用户要亲自验收、亲自合并。**AI 不许自作主张 commit / push / 开 PR / merge / 写文档。**

```
① 实现        → 报告 → 等用户确认
② 提交+开 PR  → 给 PR 链接 → 等用户说「更新文档」
③ 补文档到同一分支 → 推送 → 等用户在 GitHub 合并
④ 合并后收尾（checkout main + pull）
```

哪一步都不要提前跑到下一步。用户没说「提交」就停在 ① 报告完为止。

---

## 阶段 ① 实现（用户提需求就做）

- [ ] 确认当前在 `main` 且是最新：`git status` → 需要时 `git checkout main && git pull`
- [ ] **动手写代码之前先切分支**：`git checkout -b feature/<英文短名>`
      （命名统一 `feature/*`；历史上的 `agent/*` 分支保留不动）
- [ ] 开发 + 编译验证（Unity 未刷新 csproj 时的验证方法见 [vn-debug]）
- [ ] 涉及新命令 / 新特效 / 新事件模块 / 新运行时状态 → 分别对照
      [vn-new-command] / [vn-new-effect] / [vn-new-event-module] / [vn-save-compat] 的清单
- [ ] **不要 commit，不要 push，不要写文档**
- [ ] 向用户报告：做了什么、改了哪些文件、怎么验证 → **停下来等确认**

## 阶段 ② 提交 + 开 PR（用户说「提交 / 推送 / 开 PR」才做）

- [ ] **逐文件** `git add <路径>`——用户 Unity 工作区常年有无关的未提交改动
      （prefab / scene / asset / 随手放的图），**禁止 `git add -A` 或 `git add .`**
- [ ] `git commit`：英文标题 + 中文正文，尾部 `Co-Authored-By:` 署名行
- [ ] `git push -u origin feature/<名>`（网络偶尔超时 → 用后台方式推送 run_in_background）
- [ ] `gh pr create --base main --head feature/<名> --title "<英文标题>" --body-file <文件>`
      - **正文用 `--body-file` 或 `--body "$(cat <<'EOF' … EOF)"`**，别指望交互式输入：
        本环境的 shell 不支持交互，`gh pr create` 不带参数会卡住
      - PR 正文中文：需求一句话 → 改动清单 → 验证方法
- [ ] 把 PR 链接贴给用户 → **停下来等用户说「更新文档」**

## 阶段 ③ 文档（用户说「更新文档」才做）

- [ ] **留在同一个功能分支上**（别切回 main），按 [vn-doc-update] 补文档
- [ ] `git add` 文档文件 → commit（标题如 `Document the xxx feature`）→ `git push`
- [ ] PR 会自动带上这次提交，无需重开 → 告诉用户 PR 已更新，**等他合并**

## 阶段 ④ 合并后收尾（用户合并完并叫我才做）

- [ ] `git checkout main` → `git pull`
- [ ] `git log --oneline -3` 确认合并结果已到本地
- [ ] **不删分支**（见下方铁律）

---

## 铁律
1. **永远不删除任何分支**——用户靠分支回滚历史。GitHub 上合并 PR 时请用户**不要勾
   Delete branch**；本地也不许 `git branch -d`。
2. **合并是用户的动作**。AI 不执行 `git merge`、不执行 `gh pr merge`，除非用户明确点名要我合。
3. 提交信息：**英文标题 + 中文正文**，尾部 `Co-Authored-By:` 署名行。
4. 只提交本次功能相关文件，逐文件 `git add`。

## 已知坑
- `gh pr create` 在无交互 shell 里不带参数会挂住 → 一律显式给 `--title` / `--body-file`。
- 切分支/合并时报 `unable to unlink … .unity`：Unity 编辑器占用场景文件 →
  `git clean -f -- <残留新文件>` 后重试即可。
- main 上有用户未提交的 Unity 改动是常态，切分支前确认不会冲掉它们
  （`git checkout -b` 会把未提交改动一起带到新分支，通常正是想要的）。
- `git push` 偶尔网络超时，重试或后台推送。

## 权威参考
- CLAUDE.md「工作规则」；WhatAiDo.md 第八章（版本控制，含 2026-09-01 的流程变更）
