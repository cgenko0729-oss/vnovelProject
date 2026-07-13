# CLAUDE.md — 视觉小说项目（vnovelProject）

> 本文件是给 Claude（AI 助手）的项目说明书。所有开发过程的详细记录在 `WhatAiDo.md`。

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
2. **每个新功能开新分支**（`feature/<名称>`），完成后合并回 `main`，**永远不删除任何分支**（用户靠分支回滚）
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
│   └── Editor/VNEffectsDemoSetup.cs   一键场景生成器
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
| VNParallax / VNMouseStardust / VNClickRipple | 鼠标视差 / 星尘拖尾 / 点击涟漪 |
| VNSpeakerHighlight / VNToneMatch | 说话者高亮 / 立绘色调匹配背景 |
| VNDialogueBox + VNTypewriterText | 对话框（流光边框/名牌/箭头）+ 打字机逐字上浮 |
| VNChoicePanel | 选项演出（飞入/悬停扫光/落选溶解），需 EventSystem |
| VNSakuraBurst | 樱吹雪告白组合技 |

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
- **路线图**：P0 核心(已完成) → P1 label/jump/choice/flag/if 分支 → P2 存档/回想/Auto/Skip
  → P3 台词内嵌演出标记 `{shake}{w:0.5}` + VNDirector 名场面命令
