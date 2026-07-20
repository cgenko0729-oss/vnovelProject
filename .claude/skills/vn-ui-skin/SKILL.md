---
name: vn-ui-skin
description: 制作/修改 UI 皮肤：对话框与选项面板皮肤（VNDialogueSkin/VNChoiceSkin，进存档）、系统菜单全局主题（VNSystemUiSkinSet，不进存档），槽位降级规则与导出模板菜单。Create or edit UI skins (dialogue skin, choice skin, system UI theme prefabs, title menu).
---

# UI 皮肤制作

## 何时用我
做/改对话框、选项面板、标题菜单、设置、CG 画廊、Backlog、快捷条、存读档、
属性 HUD/面板的外观时。

## 两条独立的皮肤线（别混）
| | 对话框/选项皮肤 | 系统菜单全局主题 |
|---|---|---|
| 组件 | VNDialogueSkin / VNChoiceSkin（挂 prefab 根） | VNSystemUiSkinSet（唯一全局资产）+ 各界面槽位 prefab |
| 登记 | VNGameConfig「UI 皮肤」区登记 id | VNGameConfig.systemUiSkin 指向 SkinSet |
| 切换 | 剧本命令 `ui dialogue\|choice <id\|default>` | 无剧本命令，全局唯一 |
| 存档 | **进存档** | **不进存档** |
| 起步模板 | Tools → VN Effects → UI Skins → Export Skin Prefabs | Tools → VN Effects → System UI Skins → Export Default Prefabs（9 个，含背包 Inventory） |
| 校验 | — | 同菜单 Validate Global Theme（查必需槽位） |

## 铁律
- **槽位可留空降级**：对话框/选项皮肤全槽位可选；系统主题单项缺失或槽位无效时
  只退回**该项**的程序化 UI，不影响其他界面。
- 编辑期创建、随场景/prefab 保存的 TMP 文字必须引用
  `VNFontAssetBuilder.EnsureFontAsset()` 的持久化字体资产——运行时临时资产存进
  prefab 会变 Missing。
- 玩家可见字符串禁止硬编码，一律 `VNLocale.T(key)`（见 [vn-localize]）。
- 文字组件一律 TextMeshProUGUI；禁止 legacy Text。
- 皮肤 prefab 里的 Sprite 若来自图集，缩略图/预览按 textureRect 处理。

## 操作流程
- [ ] 用导出菜单生成起步模板 prefab（自带烘焙贴图与登记）
- [ ] 复制模板改样式（改布局/贴图/配色，槽位引用别断）
- [ ] 对话框/选项线：VNGameConfig 登记新 id → 剧本 `ui dialogue <id>` 验证 → 存读档往返验证
- [ ] 系统主题线：指到 SkinSet → Validate Global Theme → 逐界面打开验证降级正常
- [ ] WhatAiDo.md 记录（见 [vn-doc-update]）

## 权威参考
- WhatAiDo.md 八十二（对话框/选项皮肤）、八十三（系统菜单全局皮肤）、八十（标题菜单）
- 示例剧本 `Assets/Scenarios/UiSkinDemo.vn.txt`；ProjectCodeGuide.md 七（系统 UI）
