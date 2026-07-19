---
name: vn-new-feature
description: 开始做任何新功能/修 bug 前的开发流程：开 agent/<名> 分支、提交格式、合并回 main、永不删分支、unlink 报错处理、后台推送。Start any new feature or bugfix — branching workflow, commit format, merge rules, git push.
---

# VN 项目新功能开发流程

## 何时用我
用户要求开发新功能、修 bug、做任何会产生代码/资产/文档改动的任务时，动手前先过一遍本清单。

## 铁律
1. **每个新功能开新分支** `agent/<英文短名>`（历史分支也有 `feature/*`），完成后合并回 `main`。
2. **永远不删除任何分支**——用户靠分支回滚历史。
3. 提交信息：**英文标题 + 中文正文**，尾部加 `Co-Authored-By:` 署名行。
4. 只提交本次功能相关文件。用户 Unity 工作区常年有未提交改动（prefab / scene / asset），**不要顺手带上**，逐文件 `git add`。

## 操作清单
- [ ] `git checkout -b agent/<名>`（从 main 切出）
- [ ] 开发 + 编译验证（Unity 未刷新 csproj 时的验证方法见 [vn-debug]）
- [ ] 涉及新命令 / 新特效 / 新事件模块 / 新运行时状态 → 分别对照
      [vn-new-command] / [vn-new-effect] / [vn-new-event-module] / [vn-save-compat] 的清单
- [ ] 文档同步（见 [vn-doc-update]，WhatAiDo.md 必写）
- [ ] 逐文件 add → commit → `git checkout main` → `git merge agent/<名>`
- [ ] 推送 origin：网络偶尔超时，用后台方式推送（run_in_background）

## 已知坑
- 合并时报 `unable to unlink ... .unity`：Unity 编辑器占用场景文件 →
  `git clean -f -- <残留新文件>` 后重试合并即可。
- main 上有用户未提交的 Unity 改动是常态，切分支/合并前确认不会冲掉它们。

## 权威参考
- CLAUDE.md「工作规则」；WhatAiDo.md 第八章（版本控制）
