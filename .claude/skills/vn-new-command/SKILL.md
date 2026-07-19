---
name: vn-new-command
description: 给剧本 DSL 加一条新命令的全链路清单：Parser→Runner→VNStage→存档/调试重建→剧本编辑器 Schema→静态校验器→文档。Add or extend a VN script DSL command (VNScriptParser keyword, VNScriptRunner dispatch, editor schema, linter).
---

# 加新剧本命令全链路

## 何时用我
要新增 `.vn.txt` 剧本命令，或给现有命令加新参数时。**漏掉存档或编辑器任何一环都会埋 bug。**

## 操作清单（一步不能漏）
- [ ] 1. `VNScriptParser.Keywords` 加关键字
- [ ] 2. `VNScriptRunner.Dispatch` 加 case：要等待 → 写 `XxxCo` 协程返回；瞬发 → 返回 null
- [ ] 3. 命令会改舞台可见状态？→ `RebuildStateBefore()` 加静默重放 case
      （编辑器「从选中行播放」的状态重建依赖它）
- [ ] 4. 有开关状态要进存档？→ 走 [vn-save-compat] 清单
      （VNSaveData 字段带默认值 + VNStage 快照存取）
- [ ] 5. `VNScenarioSchema` 登记参数模式（可视化编辑器 UI 自动生成）
- [ ] 6. `VNScenarioEditorWindow.CommandTranslations` 加中文名；
      参数若是新的动态来源 → `VNParamSource` + `OptionsFor` + `VNScenarioDoc.Validate` 三处补
- [ ] 7. 参数目标是素材 id / label / 模块 id 之类可校验对象 → 考虑给静态校验器
      （Tools → VN Effects → Lint Scenarios，Ctrl+Shift+L）加检查项
- [ ] 8. 文档：HowToUse.md 对应章 + 文末命令速查卡 + Demo.vn.txt 头部语法速查
- [ ] 9. 编译验证 → WhatAiDo.md 记录（见 [vn-doc-update]）

## 语法设计约定
- 命令默认**同步等待**，行尾 `@` = 异步；新命令必须遵守这个语义。
- kwargs 值**不能含空格**；choice 选项行的 `if:` 表达式**不能含空格**
  （独立 `if` 命令的表达式支持空格、逻辑运算、括号、整数算术）。
- 逻辑标识符（flag 名 / 角色 id / event 结果名）永不进本地化；
  新命令带玩家可见文案时要设计翻译通道（见 [vn-localize]）。
- 关键字保留英文；编辑器里配中文说明即可。

## 权威参考
- ProjectCodeGuide.md 菜谱一 + 三（剧本层：Parser/Runner/Flags）
- HowToUse.md 四~七（命令详解、timing）；WhatAiDo.md 十六章（DSL 核心）、七十三章（条件表达式）
