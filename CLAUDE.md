# CLAUDE.md — 视觉小说项目（vnovelProject）

> 本文件是给 Claude（AI 助手）的项目说明书。所有开发过程的详细记录在 `WhatAiDo.md`；
> 逐脚本的代码指南（职责/用法/扩展/维护）在 `ProjectCodeGuide.md`，改代码前先查它。

## 项目概况

- **类型**：Unity 2D 视觉小说（Galgame），重视觉演出
- **Unity**：6000.0.62f1（Unity 6）
- **渲染管线**：URP 17（PC_RPAsset / Mobile_RPAsset 两套，Bloom + Vignette 后处理是所有发光效果的基础）
- **动画**：DOTween（`Assets/Plugins/Demigiant`，代码补间一律用它）
- **输入**：仅新版 Input System（`Keyboard.current` / `Mouse.current`，**禁用旧版 `Input.` API**）
- **对话插件**：Pixel Crushers Dialogue System（`Assets/Plugins/Pixel Crushers`，已安装待接入）
- **GitHub**：https://github.com/cgenko0729-oss/vnovelProject.git（公开仓库，remote = origin）

## 工作规则（必须遵守）

1. **全程用中文回复用户**
2. **每个新功能开新分支**（当前约定 `agent/<名称>`；历史分支也有 `feature/*`），完成后合并回
   `main`，**永远不删除任何分支**（用户靠分支回滚）
3. 每批开发完成后**详细追加记录到 `WhatAiDo.md`**（计划、文件说明、技术决策、修复记录）
4. 提交信息英文、正文中文注释；commit 尾部加 Co-Authored-By
5. 合并时若报 `unable to unlink ... VNEffectsDemo.unity`：是 Unity 编辑器占用场景文件，
   `git clean -f -- <残留新文件>` 后重试合并即可
6. 推送偶尔网络慢会超时：用 `run_in_background` 后台推送

## 目录结构

```
Assets/
├── Assets/                  用户随手放的图片素材（AI 生成立绘/背景/UI 素材混放）
├── Scenes/VNEffectsDemo.unity   演示场景（由生成器一键重建，可随时覆盖）
├── Scripts/VNEffects/       ★ 核心特效系统（全部代码在此）
│   ├── Script/              剧本 Parser / Runner / Stage / 存档 / 音频
│   └── Editor/              场景生成器、剧本编辑器、角色/镜头预览工具
├── Shaders/                 VNImageEffect / VNAdditive / VNScreenTransition
├── VNEffects/Materials/     生成器创建的材质资产
└── Plugins/                 DOTween、Pixel Crushers Dialogue System
```

## VNEffects 特效系统架构

### 演示场景容器层级（每种整屏运动独占一层，可任意叠加）

```
Canvas (Screen Space - Camera, planeDistance 10, 1920×1080)
├── SceneRoot      ← VNScreenShake(位置震动) + VNHeartbeat(缩放脉动)
│   └── ZoomRoot   ← VNCamera(运镜缩放/平移)
│      └── TiltRoot ← VNDutchAngle(荷兰角旋转+防露角放大)
│         ├── LayerBack  (背景+云影)   ← VNParallax 视差 8px
│         ├── LayerMid   (GodRays)     ← 视差 13px
│         └── LayerFront (立绘×2+光环+脚影) ← 视差 19px
├── HintText / DialogueBox(排序40) / ChoicePanel(45) / EdgeGlow(20) / ScreenTransition(100)
└── (场外) 粒子系统们(sortingOrder 10~31)、EventSystem、各管理器空物体
```

### 关键技术约定

- **发光=HDR 颜色(>1) + Bloom(阈值1.0)**；uGUI 顶点色被钳制到 1，HDR 必须走材质属性
- **贴图全程序化生成**（`VNProceduralTextures`：柔圆/四芒星/光晕/光束/花瓣/圆环/圆角面板…），零美术依赖
- **每张图独立材质实例**（`VNImageEffectController` 自动管理），多立绘互不串扰
- uGUI 自定义 shader 走传统 CGPROGRAM（Canvas 不经过 URP 光照），保留 UI 裁剪兼容
- **UI 不写深度缓冲** → 不能用真 DoF/深度类后处理区分层，模糊在 `VNImageEffect` 里做（9-tap）
- 文字用 **legacy Text + LegacyRuntime.ttf**（系统字体回退，中文开箱即用；TMP 默认字体无 CJK）
- 所有 Tween `SetLink(gameObject)` 防泄漏；循环效果提供 Start/Stop 成对 API
- 立绘缩放有"倍率"机制（`CurrentBaseScale = 原始 × _scaleMultiplier`），
  说话者高亮/DollyZoom 用 `DOScaleMultiplier` 与呼吸动作共存
- 粒子 velocityOverLifetime 三轴曲线**模式必须一致**（都用 `MinMaxCurve(min,max)`）
- 运行时创建带 Awake 配置的组件用"先 SetActive(false) 挂组件赋值再激活"（见 `VNAmbientParticles.Create`）

### 组件速查（Assets/Scripts/VNEffects/）

| 组件 | 职责 |
|---|---|
| VNImageEffectController | 单图特效总控：溶解/扫光/发光/闪白/HSV/波浪/轮廓光/波光/模糊 + 悬浮/呼吸动作 |
| VNEntranceAnimator | 出场预设×6(溶解辉光/滑入/弹出/扫光/爆闪/残影冲入) + 退场×2 + StartIdleEffects |
| VNGlowBackdrop / VNFootShadow | 背后光环脉动 / 脚下椭圆影（悬浮联动） |
| VNCharacterEmotes | 情绪动作：惊讶/生气/害羞/沮丧(+Recover)/点头/摇头 |
| VNAmbientParticles | 粒子预设×8：尘埃/星光/光斑/花瓣/雨(+溅落)/雪/萤火虫/雾 + PlaySparkleBurst |
| VNWeatherController | 天气切换 + 调色联动 |
| VNMoodGrading | 七种情绪色调（双 Volume 权重交叉过渡） |
| VNScreenTransition | 全屏转场×8：噪声溶解/百叶窗/瓦片/圆扩散/水墨/爆闪/光斑/眨眼 |
| VNCamera / VNScreenShake / VNDutchAngle / VNHeartbeat | 运镜×5 / 三级震动 / 荷兰角 / 心跳脉动 |
| VNGodRays / VNEdgeGlow / VNCloudShadows / VNHeatHaze / VNFakeDoF | 光束/情绪泛光/云影/热浪+雾/伪景深 |
| VNSpeedLines | 漫画速度线/集中线 overlay（3 变体贴图闪帧，fx speedlines on/off/burst） |
| VNLetterbox | 电影黑边上下滑入（letterbox on/off [height:][time:]，mood Memory 回忆自动联动） |
| VNShootingStars / VNDriftingClouds | 夜晚偶发流星（fx meteor）/ 云本体缓移（fx skycloud，与云影互补） |
| VNParallax / VNMouseStardust / VNClickRipple | 鼠标视差 / 星尘拖尾 / 点击涟漪 |
| VNSpeakerHighlight / VNToneMatch | 说话者高亮 / 立绘色调匹配背景 |
| VNDialogueBox + VNTypewriterText | 对话框（流光边框/名牌/箭头）+ 打字机逐字上浮 |
| VNChoicePanel | 选项演出（飞入/悬停扫光/落选溶解），需 EventSystem |
| VNSakuraBurst | 樱吹雪告白组合技 |
| VNCharacterBlink / VNCharacterMouth | 默认表情自动眨眼 / 说话口型（透明画布叠加层） |
| VNEventModule / VNEventRegistry | 玩法事件接口：模块基类 + id→模板注册表（EventLayer 排序 60） |
| VNQteModule / VNMapModule | 事件示例模块：QTE 连打条 / 地图选地点（条件显隐+去过标记） |
| VNQuestDef / VNQuestLog | 任务定义资产 / quest 命令执行 + J 键任务日志（状态全在 flags） |

### 演示场景

- 重建：菜单 **Tools → VN Effects → Create Demo Scene**（每次加新组件后需重建）
- 全部按键列表见场景内提示文字或 `VNEffectsDemo.UpdateHint()`
- 立绘选择规则：`Assets/Assets` 下文件名含 "solo" 的前两张；背景轮换=其余 ≥900×600 的大图

## 剧本系统（自研轻量 DSL，选型已定）

- **选型结论**：自研 Ren'Py 风格纯文本剧本（Git/AI 协作友好）；Dialogue System 插件保留不用
- 代码在 `Assets/Scripts/VNEffects/Script/`：VNScriptParser → VNScriptRunner → VNStage → 特效 API
- 剧本文件：`Assets/Scenarios/*.vn.txt`（语法速查见 Demo.vn.txt 文件头 / WhatAiDo.md 十六章）
- 角色定义：`Assets/VNEffects/Characters/*.asset`（VNCharacterDef：id/名牌/表情→立绘映射）
- 剧本场景：菜单 **Tools → VN Effects → Create Script Demo Scene** → `VNScriptDemo.unity`
- 关键语义：命令默认同步等待，行尾 `@` = 异步；台词行 = 等打字完+玩家推进
- 分支语法（P1，已完成）：`label/jump`、`flag 名字 [+1|数值]`（VNFlags 全局整型字典）、
  `if 条件 jump 标签`（条件无空格：`好感度>=2`）、`choice` + `* 文本 [flag:op] [-> 标签]`
- P2（已完成）：F5/F9 打开 20 槽存读档界面（JSON 快照+PNG 截图缩略图+时间+末句台词，仅台词处可存）、
  对话框快捷功能条（Save/Load/Auto/Skip/Log/任务/Config/隐藏 UI）、H/滚轮 回想、A 自动、
  S 快进（DOTween.timeScale 全局加速）、VNToast 提示
- 音频（已完成）：三通道独立库（bgmLibrary/seLibrary/voiceLibrary，旧 library 兼容回退）+
  每条目基准音量标定；`bgm/se/voice` 均支持 `vol:` 参数，公式=条目基准×剧本 vol×通道音量
- 玩法事件接口（已完成，四十一章规划的 P1~P3）：`event <模块id> [key:value…]` +
  `* 结果名 [flag:op] [-> 标签]` 结果行（复用 choice 解析）；VNEventModule 基类 +
  VNEventRegistry 注册表；事件期间快捷键全禁、不可存档、调试重建视为分支点；
  模块三铁律=不碰舞台/unscaled 计时+SetUpdate(true)/全部 SetLink。
  示例模块：qte（连打条）、map（地图选地点，条件显隐+`去过_<地点>` flag）
- 任务系统（已完成）：`quest start|stage|done|fail <id> [阶段]`，状态=flag`任务_<id>`
  （0 未接取/1..n 进行中/100 完成/-1 失败），VNQuestDef 资产只管文案（无资产照常运作），
  J 键日志面板；存档/if 分支/调试重建零改动复用 flags 设施
- **路线图**：下一步 P3 台词内嵌演出标记 `{shake}{w:0.5}` + VNDirector 名场面命令；
  战斗示例模块（事件接口 P4）待动工；已知技术债清单见 ProjectCodeGuide 第十二节

## 剧本可视化编辑器（当前状态）

- 菜单：**Tools → VN Effects → Scenario Editor**；核心文件：
  `Editor/VNScenarioEditorWindow.cs`、`VNScenarioDoc.cs`、`VNScenarioSchema.cs`。
- 文本仍是唯一真相：`.vn.txt ↔ VNScenarioDoc.rows`；保存时重新生成文本，注释/空行保留。
- 主命令菜单为分层 `GenericMenu`：Dialogue / Scene / Character / Camera / FX / Audio / Flow，
  关键字保留英文并带中文说明；`say（对白）` 是可与命令行互转的一等行类型。
- transition 与 emote 枚举显示中英对照，但剧本值保持英文；其他动态 id 不强制翻译。
- 工具栏“分类颜色”可为七个分类自定义颜色，值存 `EditorPrefs`，不能因此把剧本文档标脏。
- `bg`、`say`、`show` 使用通用 Sprite 缩略图浏览器（搜索/网格/当前项/清除/custom）；主行
  也显示内联小图。Sprite 必须按 `textureRect` 画 UV，不能直接把整张 texture 当缩略图。
- `say` 的角色/表情是专用字段 `VNRow.speaker / expression`，不是 `VNRow.values`；图片选择回调
  必须经专用访问器读写。`show` 才使用普通 `character / expr` 参数。不要再次混用这两条路径。
- 背景预览来源是当前场景 `VNStage.backgrounds`；角色与表情来源是项目中的
  `VNCharacterDef` 资产。`Refresh Sources` 会重建这些缓存。

### 从选中行播放

- Edit 页选中一行后点击 `▶ 从选中行播放`；默认勾选“重建前置状态”。
- UI 行号必须用 `SourceLineForRow` 换算物理文本行：choice 选项和 camseq waypoint 都会额外占行；
  空行/注释从下一条有效命令启动。
- 编辑器 Bridge 用 `SessionState` 传递 `_doc.GenerateText()`、目标行、rebuild 标志后进入 Play，
  因此未保存文本也能调试；请求消费后必须清除。
- Bridge 必须等待 `VNScriptRunner.IsInitialized`，否则 Runner 的 `Start/playOnStart` 会在调试启动后
  再覆盖一次播放位置。
- 运行时入口：`VNScriptRunner.PlayFromSourceLine(source, line, rebuildState)`；直接模式最终调用
  `ResumeAt(index)`。
- 重建模式按目标前的文件顺序汇总状态，复用 `VNSaveData` 与
  `VNStage.RestoreSnapshot(data, true)`：背景、天气、氛围、BGM/音量/循环 SE、角色站位与表情、
  portrait、FX、focus、flags、可确定镜头状态。不会预播台词、等待、转场、一次性 SE、voice。
- `VNAudio.ResetForDebug()` 用来清除默认启动留下的声音/Tween；只应在编辑器中间行调试重建时调用。
- choice/jump/if 的历史路径无法由一个目标物理行唯一推断，当前按文件顺序重建并警告；如需精确
  分支上下文，应扩展为“从存档快照启动”，不要假装自动推断玩家选择。

完整开发与分支记录见 `WhatAiDo.md` 第三十二章。
