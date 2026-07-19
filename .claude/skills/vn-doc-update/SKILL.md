---
name: vn-doc-update
description: 功能完成后同步项目文档：WhatAiDo.md 章节模板与编号规则、CLAUDE.md 组件表、ProjectCodeGuide、HowToUse、SetUpGuide 分别何时更新。Update project docs after finishing a feature (WhatAiDo chapter, documentation sync).
---

# 文档同步清单

## 何时用我
每批开发完成后、合并回 main 之前。WhatAiDo.md 是**必写项**，其余按改动性质判断。

## WhatAiDo.md 追加章节（必做）
- 章节编号续接文末最后一章。**先看文件末尾确认编号**——历史上出现过重号
  （两个「三十九」、两个「六十九」），不要再重号。
- 标题格式：`## 中文数字、标题（YYYY-MM-DD，分支 \`agent/xxx\`）`
- 内容骨架：需求/背景 → 文件改动清单（新增/修改逐文件一句话）→
  技术决策与取舍 → 修复记录（若有）→ 验证方法。
- 只追加，不改写历史章节（它是编年史）。

## 其他文档：改了什么就同步什么
| 改动类型 | 要更新的位置 |
|---|---|
| 新组件 | CLAUDE.md「组件速查」表加一行；ProjectCodeGuide 对应节 |
| 新剧本命令 | HowToUse.md 对应章 + 文末「命令速查卡」；Demo.vn.txt 头部语法速查 |
| 新玩法/系统 | HowToUse.md 六（玩法系统命令）+ ProjectCodeGuide 六（玩法扩展层） |
| 场景结构/层级变化 | SetUpGuide.md 对应章 |
| 新的常见报错/坑 | HowToUse.md 十三（常见问题排查表）；ProjectCodeGuide 十二（坑清单） |
| 架构约定变化 | CLAUDE.md「关键技术约定」 |
| 新增/修改技能 | `.claude/skills/` 对应 SKILL.md 与 CLAUDE.md 技能索引 |

## 提醒
- 玩家可见文案有变化 → 检查是否要跑本地化 Extract（见 [vn-localize]）。
- 文档语言：正文中文；commit 标题英文。
