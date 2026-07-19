---
name: vn-localize
description: 本地化工作流（中/英/日）：Extract 增量抽取→填表→Validate 查缺译；FNV-1a key 机制、永不翻译的标识符、UI 字符串表、资产 En/Ja 字段、加新语言步骤。Localization workflow (translate, VNLocale, extract, validate, ui strings).
---

# 本地化（中 / 英 / 日）

## 何时用我
翻译台词/UI、查缺译、剧本台词改动之后、或要加新语言时。

## 日常工作流
改剧本 → **Tools → VN Effects → Localization → Extract**（增量合并，已译按 key 保留）
→ 填翻译表 → **Validate** 查缺译统计。

## 机制要点
- 剧本 `.vn.txt` **只写中文**（唯一真相）；翻译在旁路表
  `Resources/VNLocale/Scenarios/<剧本名>.<lang>.txt`。
- key = FNV-1a(原文) + 出现序号 → **改了中文原文 = key 变化**，旧译文会挪到表尾
  孤儿注释区，需要人工搬回新 key。
- choice 选项按**索引**匹配，只翻显示文本是安全的。
- **红线：event 结果行、角色 id、flag 名是逻辑标识符，永远不进翻译。**
- UI 字符串：代码里 `VNLocale.T("key")`（带参 `T(key, args)`），表在
  `Resources/VNLocale/ui.<code>.txt`；回退链 = 当前语言→中文→key。
- 资产文案走字段：VNCharacterDef.displayNameEn/Ja、VNQuestDef 英/日标题/描述/阶段、
  VNMapModule.Location 英/日显示名；留空回退中文。
- 字体自动跟语言切：中/英 = Noto Sans SC、日 = Noto Sans JP（VNFont 统一入口，勿绕过）。
- 订阅 `LanguageChanged` 的组件若 Initialize 会被多次调用，**先 `-=` 再 `+=`**（幂等）。

## 加新语言步骤
VNLanguage 枚举**只能追加**（顺序是 PlayerPrefs 存储值）→ VNLocale.Codes/DisplayName
→ `ui.<code>.txt` → VNLocalizationTools.TargetLanguages →（可选）VNFont 新 Profile
→ 各资产加显示名字段并进取值 switch。

## 权威参考
- ProjectCodeGuide.md 四（本地化节，最完整）；WhatAiDo.md 五十七章
- HowToUse.md 十（玩家侧视角）
