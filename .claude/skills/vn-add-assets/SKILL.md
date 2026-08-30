---
name: vn-add-assets
description: 接入素材：立绘/角色定义、背景、CG、BGM/SE/语音、任务/商店/日程/属性定义资产；一律登记进 VNGameConfig 而非场景。Add game assets (character sprite, background, CG, BGM, SE, voice, VNGameConfig registration).
---

# 素材与定义资产接入

## 何时用我
往项目里加立绘、背景、CG、音频，或建 VNCharacterDef / VNQuestDef / VNShopDef /
VNPlanDef / VNStatDef 等定义资产时。

## 第一铁律：配置进 VNGameConfig，不进场景
场景是生成器随时重建的，**绑定填在场景组件上会丢**；一律填 `VNGameConfig.asset`。
已经配在场景里的用 Import From Scene 一次性搬进去。
新建资产后跑 **Game Config → Rescan Asset Folders**。

## 登记素材用哪个界面（一一九章）
| 场景 | 用哪个 |
|---|---|
| 一次加几十张图 / 一批音频 | **Tools → VN Effects → 素材浏览器 Asset Browser**：选左栏类别 → 直接把文件从 Project 拖进来，id 自动预填文件名，再逐个改成中文 id |
| 找某一张图 / 某一首曲子 | 同上，网格看缩略图、音频列表点 ▶ 试听。**本项目文件名是 AI 生成的原始 prompt 或纯数字，认图只能靠缩略图** |
| 改单条的 id、音量、CG 差分组 | VNGameConfig 的 Inspector（已按 剧本｜标题｜UI皮肤｜舞台｜音频｜玩法｜AI｜大头贴 分页），每个库自带搜索框 |
| 怀疑有素材漏登记 | Asset Browser 勾「只看未登记」——扫已登记条目所在目录，列出没进库的文件，点「登记」一键补 |

不改数据结构、不动文件：这些界面全走 `SerializedObject`，与手填完全等价。

## 分类做法
| 素材 | 做法 |
|---|---|
| 角色 | `Assets/VNEffects/Characters/` 建 VNCharacterDef：id / 名牌（En/Ja 字段管翻译）/ 表情→立绘映射；构图不齐用 sizeScale / positionOffset 标定（有预览工具：Tools → VN Effects 角色预览） |
| 背景 | 图放好后登记进 VNGameConfig 的 Backgrounds（id=剧本里 `bg` 用的名字） |
| CG | 放 `Assets/CG/`，**文件名=id**，生成器自动灌入 cgLibrary；差分组靠 group 相同合并翻页；解锁走 VNCgUnlocks 全局 JSON，勿用 flags |
| 音频 | VNAudio 三通道库（bgmLibrary/seLibrary/voiceLibrary）加条目：id 可中文、拖 clip、**顺手按素材响度标定 volume 基准**；别再往旧混合 library 塞。音量公式=条目基准×剧本 vol×通道音量 |
| 任务/商店/日程/属性 | Project 右键 → Create → VN → 对应 Definition；文案资产缺失时系统照常运作（只是没好看文案）；显示名翻译填资产 En/Ja 字段 |

## 图片导入要求
- **素材目录里的新图会自动设成 Sprite (2D and UI) + Single**（`VNTextureImportDefaults`，
  一二〇章）：`Art/Images/**`、`Art/CG/`、`Art/BigPhoto/`、`Art/Mark/`、`Assets/Assets/`。
  **只在首次导入时生效**，手动调过的设置永不被覆盖；存量图用
  Tools → VN Effects → 贴图 Textures → 套用 Sprite 导入设置到选中项 Apply Sprite Settings 补。
  新开素材目录要走这套 → 往 `VNTextureImportDefaults.Roots` 补一行。
  **`Art/Models/**` 刻意排除**——法线贴图按 Sprite 导入光照会全错。
- 立绘/UI：Sprite (2D and UI)，透明背景 PNG；大图注意 Max Size 别被压糊。
- 演示场景立绘选择规则：`Assets/Assets` 下文件名含 "solo" 的前两张；
  背景轮换 = 其余 ≥900×600 的大图（命名时留意）。

## 完成后
- [ ] 剧本里引用 → 跑 Lint 确认没有 unknown-bg/cg/bgm/se/voice/character 警告
- [ ] 新素材类别或规则变化 → HowToUse.md 八（资产管理）同步

## 权威参考
- HowToUse.md 八（资产管理，含 VNGameConfig 一节必读）
- WhatAiDo.md 二十（角色标定）、四十（音频库）、五十六（CG）、七十七（零拖拽资产绑定）
