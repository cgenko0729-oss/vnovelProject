---
name: vn-editor-extend
description: 修改/扩展剧本可视化编辑器（Scenario Editor）时的规则：Schema 登记、say 专用字段、SourceLineForRow 行号换算、Sprite textureRect、EditorPrefs 不标脏、播放 Bridge 时序。Extend the visual scenario editor (VNScenarioEditorWindow, schema, thumbnails).
---

# 剧本可视化编辑器扩展

## 何时用我
改 `Editor/VNScenarioEditorWindow.cs`、`VNScenarioDoc.cs`、`VNScenarioSchema.cs`，
或给编辑器加新行类型/新 UI 功能时。

## 铁律
- **文本是唯一真相**：`.vn.txt ↔ VNScenarioDoc.rows`，保存时重新生成文本，注释/空行必须保留。
- **say 的角色/表情是专用字段** `VNRow.speaker / expression`，**不是** `VNRow.values`；
  图片选择回调必须经专用访问器读写。`show` 才用普通 `character / expr` 参数。两条路径禁止混用。
- **UI 行号 ≠ 物理行号**：换算一律走 `SourceLineForRow`——choice 选项行和 camseq waypoint
  都额外占物理行；空行/注释从下一条有效命令启动。
- **Sprite 缩略图必须按 `textureRect` 画 UV**——图集里的 sprite 不能整张 texture 当缩略图。
- 纯外观偏好（如分类颜色）存 **EditorPrefs**，**不能因此把剧本文档标脏**。
- 枚举（transition/emote）显示中英对照，但写进剧本的值保持英文。

## 「从选中行播放」Bridge 时序
- Bridge 用 `SessionState` 传 `_doc.GenerateText()`、目标行、rebuild 标志后进 Play；
  未保存文本也能调试；**请求消费后必须清除**。
- Bridge 必须等 `VNScriptRunner.IsInitialized`，否则 Runner 的 Start/playOnStart
  会在调试启动后再覆盖一次播放位置。
- 运行时入口：`VNScriptRunner.PlayFromSourceLine(source, line, rebuildState)`；
  重建逻辑本身见 [vn-save-compat] 与 [vn-debug]。

## 新行类型 / 新参数
- 走 [vn-new-command] 清单第 5~6 步（Schema + CommandTranslations + VNParamSource 三处）。
- 背景预览来源 = 当前场景 `VNStage.backgrounds`；角色/表情来源 = VNCharacterDef 资产；
  改了来源记得 `Refresh Sources` 重建缓存逻辑仍然成立。

## 权威参考
- CLAUDE.md「剧本可视化编辑器」节；WhatAiDo.md 三十一/三十二（主体）、六十~六十二（试听/舞台一览/多选）
- ProjectCodeGuide.md 九（编辑器工具）
