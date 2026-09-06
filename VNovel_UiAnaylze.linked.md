# VNovel UI 全景解析

> 对象：`D:\UnityAiProject\MyProject`（GitHub: vnovelProject）——Unity 6000.0.62f1 / URP 17 的 2D 视觉小说专案。
> 目的：把这个专案里**所有 UI** 的定义、生成、开关、堆叠、销毁、通讯、资源与输入流程讲透，
> 让你之后不管是加一个新面板、加一个小游戏、还是改 Editor 工具，都知道该往哪儿放、要遵守哪些约定、会踩哪些坑。
>
> **引用规范**：凡是「程式码里实际存在的行为」，都以 `符号名（相对路径/档名.cs:行号）` 标注出处。
> 架构比喻、设计评论、改进建议不附行号。路径一律从专案根目录起算。
>
> **重要**：`CLAUDE.md` 里写的 `Assets/Scripts/VNEffects/` 路径已经过时。
> 实际程式码位置是 `Assets/Project/Scripts/VNEffects/`（运行时特效层）、
> `Assets/Project/Scripts/VNEffects/Script/`（剧本与玩法层）、
> `Assets/Project/Scripts/VNEffects/Editor/`（编辑器工具）。本文件全部使用真实路径。

---

## 目录

| 章 | 主题 |
|---|---|
| 一 | 全局地图：这个专案有几种 UI |
| 二 | 画布与排序层级：一张必须背下来的表 |
| 三 | UI 的生命周期：谁定义、谁生成、谁持有、谁销毁 |
| 四 | 皮肤系统（一）：对话框 / 选项面板的「双轨制」 |
| 五 | 皮肤系统（二）：系统菜单的全局主题与槽位校验 |
| 六 | 对话框深挖：VNDialogueBox + 打字机 + 名牌 |
| 七 | 选项面板深挖：VNChoicePanel |
| 八 | 系统面板逐个拆解 |
| 九 | 输入、焦点、返回／取消：Runner 的模态栈 |
| 十 | 全局暂停：VNPause 与 VNTime |
| 十一 | 教程系统：暗幕挖洞 + 锚点注册表 |
| 十二 | 事件模块 UI 框架：三铁律与 EventLayer |
| 十三 | 玩法模块 UI 逐个详述 |
| 十四 | UI ↔ 游戏逻辑的数据流（四条通道） |
| 十五 | 资源加载与释放：贴图、字体、纹理缓存 |
| 十六 | 可复用 UI 基础设施清单 |
| 十七 | 可复用的 UI 反馈机制目录 |
| 十八 | 新增一个 UI 画面的完整流程（三条路线） |
| 十九 | Editor 端 IMGUI 工具大章 |
| 二十 | 全专案 UI 踩坑清单 |
| 二十一 | 技术债与改进建议 |

---

## 一、全局地图：这个专案有几种 UI

先建立一个心智模型。本专案的 UI **不是一套系统**，而是**四套并存、边界清晰**的系统。
这一点是理解全部后续内容的前提——如果你带着「Unity 项目应该有一个 UIManager」的预期来看，会到处对不上。

### 1.1 四套 UI

| # | 名称 | 技术栈 | 挂在哪 | 谁生成 | 典型代表 |
|---|---|---|---|---|---|
| ① | **舞台层 UI** | uGUI，主 Canvas（Screen Space - Camera） | 主 Canvas 子树 | 场景生成器建骨架 + 组件运行时补内容 | 对话框、选项面板、全屏转场、过场卡、教程层 |
| ② | **系统面板** | uGUI，各自独立 Overlay Canvas | 自己 `new GameObject` 出来的 Canvas | 组件首次 `Open()` 时惰性 `Build()` | 存读档、设置、回想、画廊、背包、任务日志、标题菜单 |
| ③ | **事件模块 UI** | uGUI，EventLayer 嵌套 Canvas | [`VNEventRegistry`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs) 建的 EventLayer | 模块 `OnLaunch` 里 100% 程序化生成 | 羽毛球、大头贴、擦雾、问答、商店、战斗… |
| ④ | **Editor 工具 UI** | IMGUI（`OnGUI` / `EditorWindow`） | 不在场景里 | `EditorWindow.GetWindow` | 剧本编辑器、素材浏览器、镜头编排、擦雾调参 |

### 1.2 为什么要分成四套

**① 与 ② 的分界线是「吃不吃后处理」**。
主 Canvas 是 `RenderMode.ScreenSpaceCamera`（`BuildStageRig`，`[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:130](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L130)`），
`planeDistance = 10`（同一方法内，`[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:132](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L132)`），
所以它**在相机的渲染路径里**，会吃到 URP 的 Bloom / Vignette。
这是整个专案「发光 = HDR 颜色(>1) + Bloom」这条硬约定的物理基础。

而系统面板（存读档、设置…）用的是 `RenderMode.ScreenSpaceOverlay`
（[`VNSaveLoadPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)，`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:122](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L122)`；
[`VNConfigPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)，`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:95](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L95)`）。
Overlay 画布是在**所有相机之后**直接糊到屏幕上的，永远压在 Screen Space - Camera 之上，
也永远吃不到后处理。

这个差别带来两个必须记住的结论：

1. **想被 Bloom 点亮的 UI，必须挂主 Canvas 下。**
   教程层就是因为洞口的 HDR 描边要发光，才刻意挂在主 Canvas 而不是自建 Overlay
   （类注释见 [`VNTutorialPlayer`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs)，`[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:25](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L25)`）。
   过场层 [`VNInterludeScreen`](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs) 出于同样理由（`[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:25](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L25)`）。
2. **想盖住全屏转场的 UI，做不到。**
   全屏转场 `VNScreenTransition.sortingOrder`（[`Assets/Project/Scripts/VNEffects/VNScreenTransition.cs:50`](Assets/Project/Scripts/VNEffects/VNScreenTransition.cs#L50)） 默认 100（`[Assets/Project/Scripts/VNEffects/VNScreenTransition.cs:50](Assets/Project/Scripts/VNEffects/VNScreenTransition.cs#L50)`）
   是主 Canvas 内的最高位。任何 Overlay 画布都会压在它之上，
   所以过场层与教程层刻意留在 100 以下（90 / 92），保持「转场永远能盖住一切」这条语义。

**③ 独立出来是因为「玩法要能被剧本调用并返回结果」**。
它不是一个面板，而是一段带返回值的子流程：剧本 `event <id>` → Runner 暂停 → 模块交互 → `Done(结果名)` → 剧本按结果分支。
生命周期完全由 [`VNScriptRunner.EventCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2909](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2909)`）掌控。

**④ 完全是另一个世界**：IMGUI 立即模式、没有 GameObject、状态要跨域重载存活。第十九章专门讲。

### 1.3 一句话总览：本专案 UI 的三个特征

1. **零 prefab 起步、程序化优先。** 几乎每个面板都能在「一个 prefab 都没有」的情况下跑起来，
   贴图由 [`VNProceduralTextures`](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs)（`[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:10](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L10)`）现场生成。
   美术资产是**可选的增强**，不是前提。
2. **皮肤 = 槽位声明，不是继承。** 想换外观就做一个 prefab，在根上挂一个「槽位声明组件」
   （[`VNDialogueSkin`](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs) / [`VNSystemUiSkinBehaviour`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs) 的子类），代码只认槽位引用，
   prefab 里的其余装饰节点代码一概不管。
3. **状态不在 UI 里。** 属性、道具、任务、装备的真值全在 [`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs)
   （`[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:15](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L15)`）这个整型字典里；
   UI 只是这份字典的一个视图，靠 `VNFlags.Changed`（[`Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:21`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L21)） 事件（`[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:21](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L21)`）标脏重画。

---

## 二、画布与排序层级：一张必须背下来的表

排序是这个专案最容易出事的地方——「为什么我的面板被对话框盖住了」「为什么点击穿过去了」
十有八九是排序或射线的问题。

### 2.1 主 Canvas 内部的容器层级

主 Canvas 本身 `sortingOrder = 0`（`BuildStageRig`，`[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:133](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L133)`），
其下的容器层级是：

```
Canvas (Screen Space - Camera, planeDistance 10, 参考分辨率 1920×1080)
└── SceneRoot   ← VNScreenShake（位置震动）+ VNHeartbeat（缩放脉动）
    └── ZoomRoot  ← VNCamera（运镜缩放/平移）
        └── TiltRoot ← VNDutchAngle（荷兰角）
            ├── LayerBack  背景 + 云影
            ├── LayerMid   GodRays
            └── LayerFront 立绘
```

这四层容器由 `BuildStageRig` 依次创建（`[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:142](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L142)`
起连续几行 `CreateStretchRect`）。背景 Image 四边溢出 60px（`BuildStageRig`，
`[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:157](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L157)`），给 Ken Burns 与视差留余量。

**为什么每种整屏运动要独占一层**：位置震动、缩放运镜、旋转荷兰角如果挤在同一个 Transform 上，
三者都写 `localPosition` / `localScale` / `localRotation`，后写的会覆盖先写的。
拆成三层父子，每层只写自己那一个属性，就能任意叠加。
这是「不要让多个系统写同一个字段」这条原则在 Transform 上的应用——
后面第六章讲缩放倍率双通道、`SetGrade` 分层调色时，你会看到同一条原则的另外两次应用。

### 2.2 排序号总表（数字越大越靠上）

**主 Canvas 内部**（各组件自己挂 `overrideSorting = true` 的嵌套 Canvas 或粒子 renderer）：

| 排序 | 组件 | 出处 |
|---|---|---|
| 10~12 | 氛围粒子（尘埃/星光/光斑） | [`VNAmbientParticles.sortingOrder`](Assets/Project/Scripts/VNEffects/VNAmbientParticles.cs)（[Assets/Project/Scripts/VNEffects/VNAmbientParticles.cs:47](Assets/Project/Scripts/VNEffects/VNAmbientParticles.cs#L47)） |
| 20 | 情绪泛光 EdgeGlow | [`VNEdgeGlow.sortingOrder`](Assets/Project/Scripts/VNEffects/VNEdgeGlow.cs)（[Assets/Project/Scripts/VNEffects/VNEdgeGlow.cs:29](Assets/Project/Scripts/VNEffects/VNEdgeGlow.cs#L29)） |
| 25 | 漫画速度线 | [`VNSpeedLines.sortingOrder`](Assets/Project/Scripts/VNEffects/VNSpeedLines.cs)（[Assets/Project/Scripts/VNEffects/VNSpeedLines.cs:21](Assets/Project/Scripts/VNEffects/VNSpeedLines.cs#L21)） |
| 26 | 全屏情绪水波 | [`VNScreenShockwave.sortingOrder`](Assets/Project/Scripts/VNEffects/VNScreenShockwave.cs)（[Assets/Project/Scripts/VNEffects/VNScreenShockwave.cs:25](Assets/Project/Scripts/VNEffects/VNScreenShockwave.cs#L25)） |
| 28 | 液体喷溅（舞台层） | [`VNLiquidSplash.sortingOrder`](Assets/Project/Scripts/VNEffects/VNLiquidSplash.cs)（[Assets/Project/Scripts/VNEffects/VNLiquidSplash.cs:34](Assets/Project/Scripts/VNEffects/VNLiquidSplash.cs#L34)） |
| 30 | 镜头水渍 | [`VNWetScreen.sortingOrder`](Assets/Project/Scripts/VNEffects/VNWetScreen.cs)（[Assets/Project/Scripts/VNEffects/VNWetScreen.cs:44](Assets/Project/Scripts/VNEffects/VNWetScreen.cs#L44)） |
| 30 | 鼠标星尘拖尾 | [`VNMouseStardust.sortingOrder`](Assets/Project/Scripts/VNEffects/VNMouseStardust.cs)（[Assets/Project/Scripts/VNEffects/VNMouseStardust.cs:25](Assets/Project/Scripts/VNEffects/VNMouseStardust.cs#L25)） |
| 31 | 点击涟漪 | [`VNClickRipple.sortingOrder`](Assets/Project/Scripts/VNEffects/VNClickRipple.cs)（[Assets/Project/Scripts/VNEffects/VNClickRipple.cs:27](Assets/Project/Scripts/VNEffects/VNClickRipple.cs#L27)） |
| 34 | 胶片 / CRT 复古滤镜 | [`VNRetroFilter.sortingOrder`](Assets/Project/Scripts/VNEffects/VNRetroFilter.cs)（[Assets/Project/Scripts/VNEffects/VNRetroFilter.cs:32](Assets/Project/Scripts/VNEffects/VNRetroFilter.cs#L32)） |
| 35 | 电影黑边 | [`VNLetterbox.sortingOrder`](Assets/Project/Scripts/VNEffects/VNLetterbox.cs)（[Assets/Project/Scripts/VNEffects/VNLetterbox.cs:18](Assets/Project/Scripts/VNEffects/VNLetterbox.cs#L18)） |
| **40** | **对话框** | [`VNDialogueBox.sortingOrder`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs)（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:28](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L28)） |
| **41** | **快捷功能条**（对话框 +1） | [`VNQuickToolbar.ConfigureCanvas`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:67](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L67)） |
| **45** | **选项面板** | [`VNChoicePanel.sortingOrder`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs)（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:29](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L29)） |
| **60** | **事件层 EventLayer** | [`VNEventRegistry.LayerSortingOrder`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs)（[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:27](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L27)） |
| **70** | **偷拍取景 HUD** | [`VNSecretPhotoUi.SortingOrder`](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs:27](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs#L27)） |
| **90** | **过场章节卡** | [`VNInterludeScreen.sortingOrder`](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:40](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L40)） |
| 90 | 存档缩略图抓拍用的淡入淡出层 | [`VNCameraFade.sortingOrder`](Assets/Project/Scripts/VNEffects/VNCameraFade.cs)（[Assets/Project/Scripts/VNEffects/VNCameraFade.cs:27](Assets/Project/Scripts/VNEffects/VNCameraFade.cs#L27)） |
| **92** | **教程层** | [`VNTutorialPlayer.sortingOrder`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:47](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L47)） |
| **100** | **全屏转场（天花板）** | [`VNScreenTransition.sortingOrder`](Assets/Project/Scripts/VNEffects/VNScreenTransition.cs)（[Assets/Project/Scripts/VNEffects/VNScreenTransition.cs:50](Assets/Project/Scripts/VNEffects/VNScreenTransition.cs#L50)） |

**独立 Overlay Canvas**（不与上表同一个排序空间，整体都压在主 Canvas 之上）：

| 排序 | 面板 | 出处 |
|---|---|---|
| 500 | 标题菜单 | [`VNTitleMenu.Build`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:188](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L188)） |
| 578 | 日历 HUD | [`VNCalendarHud.Build`](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs)（[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:93](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L93)） |
| 580 | 属性 HUD + 属性面板 | [`VNStatsHud.EnsureCanvas`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:240](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L240)） |
| 600 | 回想 Backlog | [`VNBacklog.Build`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:90](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L90)） |
| 600 | 任务日志 | [`VNQuestLog.Build`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:250](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L250)） |
| 900 | 存读档 | [`VNSaveLoadPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:123](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L123)） |
| 950 | 设置面板 | [`VNConfigPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:96](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L96)） |
| **999** | **Toast 提示卡片（最高）** | [`VNToast.EnsureCanvas`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs)（[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:165](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L165)） |

`VNQuestLog.Build` 里的注释直接写明「与回想同层：同一时刻只会开一个」
（`[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:250](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L250)`）——
两个 600 不冲突，是因为**互斥性由输入层保证**（第九章），不是靠排序。

### 2.3 一个容易忽略的陷阱：嵌套 Canvas 打断 CanvasGroup

`overrideSorting = true` 的子 Canvas 会**打断父级 CanvasGroup 的 alpha 传播**。
这个专案踩过一次，症状是「对话框藏了，一排圆按钮还浮在半空」。
修法记在 `VNQuickToolbar.SetVisible`（[`Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:143`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L143)） 的注释里
（`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:143](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L143)`）：
工具栏自己再挂一个 CanvasGroup（`VNQuickToolbar.SetVisible`，
`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:146](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L146)`），
由 `VNDialogueBox.SetInterfaceVisible`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:664](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L664)`）
显式调用它（`VNDialogueBox.SetInterfaceVisible`（[`Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:664`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L664)），`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:678](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L678)`）。

> **学到的东西**：uGUI 的 `CanvasGroup` 只在同一个 Canvas 内部传播。
> 只要子树里插了一个 `overrideSorting` 的 Canvas，alpha / blocksRaycasts 就断在那儿。
> 凡是「我把父物体的 CanvasGroup 归零了但某个子物体还在显示」，先去找这个子物体身上有没有 Canvas。

### 2.4 射线（raycast）的分层规则

排序决定「谁画在上面」，`raycastTarget` 决定「谁吃掉点击」。这两件事在本专案里被刻意分开处理。

核心规则：**排序在上层但不该抢输入的东西，一律 `raycastTarget = false`**。

三个模块的类注释都独立记下了这条教训：

- [`VNAiTalkModule`](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs)：「EventLayer 排序 60，选项面板在 45——**在本模块下方**。所以本模块自绘的
  任何东西都必须 raycastTarget=false，否则会把选项的点击全吃掉。唯一的例外是 ESC 确认框」
  （`[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:34](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L34)`）。
- [`VNInteractionModule`](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs)：「1) 不铺全屏暗幕 —— 会盖住对话框，台词就看不见了；
  2) 自绘的一切 raycastTarget = false，只有道具按钮和结束钮例外」
  （`[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:26](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L26)`）。
- [`VNFogWipeModule`](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs)：「擦拭输入走 Update 轮询 Mouse.current，不依赖 EventSystem，
  所以自绘的一切 raycastTarget = false —— 只有 ESC 确认框的两个按钮例外」
  （`[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:52](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L52)`）。
- `VNSecretPhotoUi`：「除三个按钮外一律 raycastTarget=false」
  （`[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs:22](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs#L22)`）。

在构建工具里这条约定被写死成默认值：`VNBadmintonUi.CreateImage`（[`Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:54`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L54)） 里
`image.raycastTarget = false`（`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:61](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L61)`）、
`CreateText` 里 `text.raycastTarget = false`（`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:103](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L103)`）、
`VNDialogueBox.CreateChildImage` 里 `img.raycastTarget = false`
（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:557](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L557)`）、
`VNToast.CreateImage`（[`Assets/Project/Scripts/VNEffects/Script/VNToast.cs:268`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L268)） 里 `image.raycastTarget = false`
（`[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:282](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L282)`）。

> **学到的东西**：如果你的项目里 UI 层数多，把「默认不吃射线」做进构建工具的默认值，
> 比事后一个个去关有效得多。需要吃射线的是少数（按钮、暗幕），显式打开就好。

---

## 三、UI 的生命周期：谁定义、谁生成、谁持有、谁销毁

### 3.1 三种生命周期模型

#### 模型 A：常驻 + 惰性构建（系统面板）

代表：存读档、设置、回想、画廊、背包、任务日志、属性 HUD、日历 HUD、日记本。

- **持有者**：[`VNScriptRunner`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)。它有一整排字段——
  `_backlog`（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:58](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L58)`）、
  `_saveLoadPanel`（`:59`）、`_configPanel`（`:60`）、`_quickToolbar`（`:61`）、
  `_questLog`（`:62`）、`_diaryPanel`（`:63`）、`_statsHud`（`:64`）、
  `_inventory`（`:65`）、`_cgGallery`（`:66`）、`_calendarHud`（`:67`）、
  `_titleMenu`（`:68`）、`_secretPhoto`（`:69`），全在
  `Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs`。
- **组件的创建时机**：`VNScriptRunner.Start`（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:122](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L122)`）
  里「先找、找不到就 new 一个空 GameObject 挂组件」。
  例如 Backlog 那一段：`FindFirstObjectByType<VNBacklog>()`，为 null 就
  `new GameObject("VNBacklog").AddComponent<VNBacklog>()`
  （`VNScriptRunner.Start`，`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:128](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L128)`）。
  QuestLog（`:134`）、StatsHud（`:140`）、Inventory（`:146`）、CgGallery（`:152`）、
  CalendarHud（`:158`）、SecretPhotoMode（`:167`）全走同一模式。
- **UI 实体的创建**：**不在 Start**，而在第一次 `Open()` 时。
  例如 [`VNBacklog.Open`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:61](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L61)`）先调
  `Build()`（`[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:81](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L81)`），
  `Build` 开头一句 `if (_panel != null) return;`
  （`VNBacklog.Build`，`[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:83](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L83)`）
  ——**这就是惰性构建的全部实现**。
- **关闭**：只是 `_panel.SetActive(false)`
  （`VNBacklog.Close`（[`Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:72`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L72)），`[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:75](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L75)`），**不销毁**。
  下次打开直接复用，只重建列表内容（`VNBacklog.RebuildList`，
  `[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:124](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L124)`）。
- **销毁**：几乎不销毁。唯一会销毁整个画布的情形是**语言切换**。

#### 语言切换 = 销毁重建：全专案重复了八次的模式

`VNBacklog.OnLanguageChanged`（`[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:36](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L36)`）
把 `_canvas.gameObject` 整个 Destroy 并把所有缓存字段置 null，下次 Open 时用新语言重建。

同一模式在这些地方逐字重复：

| 面板 | 方法与出处 |
|---|---|
| 回想 | `VNBacklog.OnLanguageChanged`（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:36](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L36)） |
| 存读档 | [`VNSaveLoadPanel.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:44](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L44)） |
| 属性 HUD | [`VNStatsHud.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:72](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L72)） |
| 背包 | [`VNInventory.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInventory.cs:52](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs#L52)） |
| CG 画廊 | [`VNCgGallery.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs)（[Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs:66](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs#L66)） |
| 日历 HUD | [`VNCalendarHud.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs)（[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:39](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L39)） |
| 任务日志 | [`VNQuestLog.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:113](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L113)） |
| 标题菜单 | [`VNTitleMenu.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:40](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L40)） |
| 快捷功能条 | [`VNQuickToolbar.OnLanguageChanged`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:29](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L29)） |
| 设置面板 | [`VNConfigPanel.RebuildPanel`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:282](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L282)） |

`VNConfigPanel` 那份稍有不同：它是从 `SetLanguage`
（`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:275](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L275)`）直接调 `RebuildPanel`，
而且会记住「重建前开着没有」再决定重新打开
（`VNConfigPanel.RebuildPanel`，`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:284](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L284)`）。

> **为什么用「销毁重建」而不是「遍历所有文字重新赋值」**：
> 因为文案不只是文字内容——名牌宽度、按钮排布、`GetPreferredValues` 算出来的布局
> 全都跟着语言变。销毁重建是最不容易漏的做法。
> 代价是**面板里的临时状态会丢**（比如画廊正翻到第 3 页），但语言切换是低频操作，可以接受。
>
> **要注意的坑**：这个模式要求所有缓存的子物体引用都在 `OnLanguageChanged` 里置 null。
> 漏一个就会拿到已销毁对象的「假 null」引用，下次 Build 时 `if (_panel != null) return;`
> 判空为真（因为 Unity 的 `==` 重载对已销毁对象返回 true），提早 return，界面永远建不出来。
> `VNSaveLoadPanel.OnLanguageChanged` 一次性清了 11 个字段
> （`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:47](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L47)` 起连续多行），
> 就是这个原因。

#### 模型 B：随台词共存（对话框、选项面板）

- **持有者**：[`VNStage`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs)——`dialogue` 字段（`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:59](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L59)`）、
  `choicePanel` 字段（`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:107](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L107)`）。
  它们是**场景里预先存在的物体**，由场景生成器连线。
- **构建时机**：`Awake` 就建——[`VNDialogueBox.Awake`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs)
  （`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:104](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L104)`）调 `Build`
  （`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:109](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L109)`）；
  [`VNChoicePanel.Awake`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs)（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:71](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L71)`）
  调 `Build`（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:76](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L76)`）。
  两者都有幂等守卫（`if (_built) return;`）。
- **开关**：不销毁、不 SetActive，靠 `CanvasGroup.alpha` + DOTween——
  `VNDialogueBox.Show`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:595](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L595)`）、
  `VNDialogueBox.HideBox`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:615](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L615)`）。
- **内容销毁**：选项按钮是**每次 Show 都新建、选完就销毁**的——
  `VNChoicePanel.Show`（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:183](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L183)`）开头先
  `ClearEntries()`；`HideAll` 的完成回调里再 `ClearEntries()`
  （`VNChoicePanel.HideAll`，`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:475](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L475)`）。

#### 模型 C：用完即毁（事件模块）

- **模板持有者**：`VNEventRegistry.modules`（[`Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:25`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L25)） 列表
  （`[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:25](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L25)`）。
  模板是**场景里禁用状态的物体**或 prefab（[`VNEventRegistry.Entry.template`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs)，
  `[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:21](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L21)`）。
- **实例化**：`VNEventRegistry.Create`（`[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:42](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L42)`）
  →`Instantiate(entry.template, EnsureLayer(canvas))`
  （`[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:59](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L59)`），
  然后把 RectTransform 拉伸铺满（同方法 `:63` 起四行）并 `SetActive(true)`
  （`[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:68](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L68)`）。
- **销毁**：`VNScriptRunner.EventCo` 在模块回调 `Done` 之后 `Destroy(module.gameObject)`
  （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2956](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2956)`）。
- **异常路径销毁**：剧本被 Stop / 调试中断时走 `CleanupActiveEvent`
  （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1086](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1086)`），
  先 `CancelForDebug()` 再 Destroy（`VNScriptRunner.CleanupActiveEvent`，
  `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1096](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1096)`）。

**这里有一个很聪明的细节**：`EventLayer` 只创建一次并缓存
（`VNEventRegistry.EnsureLayer` 开头的 `if (_layer != null) return _layer;`，
`[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:74](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L74)`），
但模块每次都重新实例化。层是容器、模块是内容，容器复用、内容一次性——
既不会每次 event 都重建一个 Canvas（那会触发一次完整的 UI 布局重建），
又保证了模块之间没有任何状态残留。

### 3.2 「谁持有谁」的全局关系图

```
VNScriptRunner  ←── 剧本执行 + 全局 UI 调度中枢
├── stage: VNStage                        [Inspector 连线]
│   ├── dialogue: VNDialogueBox           [Inspector 连线，场景物体]
│   │   └── VNQuickToolbar                [AddComponent 到 dialogue 身上]
│   ├── choicePanel: VNChoicePanel        [Inspector 连线]
│   ├── transition: VNScreenTransition    [Inspector 连线]
│   ├── interlude: VNInterludeScreen      [连线或自动创建]
│   ├── tutorial: VNTutorialPlayer        [连线或自动创建]
│   ├── sns: VNSnsView                    [连线或自动创建]
│   └── eventRegistry: VNEventRegistry    [Inspector 连线]
│       └── EventLayer（嵌套 Canvas, 60）
│           └── <当前事件模块实例>          [每次 event 新建，结束销毁]
├── _backlog / _saveLoadPanel / _configPanel / _questLog / _diaryPanel
├── _statsHud / _inventory / _cgGallery / _calendarHud / _titleMenu
└── _secretPhoto: VNSecretPhotoMode
        └── VNSecretPhotoUi（嵌套 Canvas 70，挂主 Canvas 下）

VNToast  ←── static，无持有者，自建 DontDestroyOnLoad 画布（999）
```

关键观察：

1. **`VNScriptRunner` 是唯一的中枢**。所有系统面板的开关都要经过它的 `Request*` 方法：
   `RequestSavePanel`（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1678](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1678)`）、
   `RequestLoadPanel`（`:1695`）、`RequestBacklog`（`:1703`）、
   `RequestQuestLog`（`:1709`）、`RequestDiary`（`:1716`）、
   `RequestStatsPanel`（`:1729`）、`RequestInventory`（`:1735`）、
   `RequestCgGallery`（`:1741`）、`RequestConfigPanel`（`:1747`）。
   快捷功能条的按钮也是转发到这里（`VNQuickToolbar.Execute`，
   `[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:104](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L104)`）。
2. **`VNStage` 是舞台层的持有者，但它不管系统面板**。这条分界很干净：
   `VNStage` 只知道对话框、选项、转场、过场、教程、SNS——都是「演出的一部分」；
   存读档、设置、画廊这类「元游戏 UI」它完全不知道。
3. **[`VNToast`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs) 是唯一的 static 全局 UI**（`[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:22](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L22)`），
   而且是 `DontDestroyOnLoad`（`VNToast.EnsureCanvas`（[`Assets/Project/Scripts/VNEffects/Script/VNToast.cs:157`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L157)），
   `[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:162](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L162)`）。
   它没有持有者，任何地方 `VNToast.Show("...")` 就能弹卡片。

### 3.3 惰性构建的两种写法，与它们的差别

专案里有两种「首次使用才建」的写法，一定要分清：

**写法 1：字段判空**（用于会被销毁重建的 UI 实体）
```csharp
void Build()
{
    if (_panel != null) return;   // VNBacklog.cs:83
    ...
}
```

> 📎 参考：[VNBacklog.cs:83](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L83)
> 出处：`VNBacklog.Build`（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:83](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L83)）

**写法 2：bool 标志**（用于「只能建一次」的初始化）
```csharp
void Build()
{
    if (_built) return;           // VNDialogueBox.cs:111
    _built = true;
    ...
}
```

> 📎 参考：[VNDialogueBox.cs:111](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L111)
> 出处：`VNDialogueBox.Build`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:111](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L111)）

差别在于：写法 1 允许「销毁后重建」（语言切换就是这么做的），写法 2 不允许。
`VNDialogueBox` 用写法 2 是因为它是场景物体，永远不该被销毁重建；
它的语言切换是靠**皮肤重新绑定 + 名牌重新上妆**处理的（第六章）。

### 3.4 一个真实踩过的坑：编辑期误触发 Build

`VNDialogueBox.BuildDefaultSkin` 开头有一段防御
（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:142](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L142)`）：

```csharp
var existing = transform.Find("DefaultSkin");
if (existing != null && existing.GetComponent<VNDialogueSkin>() != null)
{
    _defaultRoot = existing.gameObject;
    return;
}
```
> 出处：`VNDialogueBox.BuildDefaultSkin`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:142](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L142)）

上方注释解释得很清楚（`VNDialogueBox.BuildDefaultSkin`，
`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:138](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L138)` 起）：
这套 UI 本来只在运行时生成，但只要有谁在编辑期碰了 `Build()`（工具、反射调用），
生成物就会变成真实场景物体、跟着场景存盘；
下次进 Play 再造一套就会两层叠在一起，底下那层是死的、颜色永远停在生成那一刻。

> **这是「运行时程序化生成 UI」这条路线的固有风险**。
> 任何在运行时 `new GameObject` 建 UI 的组件，都必须防备「编辑期被误触发」。
> 本专案的通用解法是「认名字捡回来」——按固定名字 `Find`，找到就复用而不是再建一套。
> 你新写组件时如果也走程序化路线，建议照抄这个模式。

---

## 四、皮肤系统（一）：对话框 / 选项面板的「双轨制」

这是本专案 UI 架构里最值得学的一块设计。

### 4.1 问题：程序化 UI 与美术 prefab 怎么共存

一般项目会二选一：要么全程序化（改外观要改代码），要么全 prefab（没有美术就跑不起来）。
本专案的做法是**两条路殊途同归**：程序化构建的结果也被塞进一个皮肤声明组件里，
之后所有行为只认「槽位引用」，不关心它们来自代码还是资产。

[`VNDialogueBox`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs) 的类注释把这条设计讲得很明白
（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:16](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L16)` 起）：

> 【皮肤系统】外观有两条路：
>   1. 程序化默认（零素材兜底）：Build() 运行时拼 UI，行为与老版本完全一致；
>   2. 皮肤 prefab：ApplySkin(VNDialogueSkin) 实例化美术 prefab 并按槽位绑定。
> 两条路殊途同归——程序化构建的结果也装进一个 VNDialogueSkin（DefaultSkin 子物体），
> 之后所有行为（打字机/名牌/头像/箭头/出入场）只认 Bind() 到的槽位引用。

### 4.2 实现：三个方法就撑起了整套机制

```
Build()            ← 只做一次：建 Canvas / CanvasGroup，然后 BuildDefaultSkin + Bind
BuildDefaultSkin() ← 程序化拼出一整套 UI，塞进一个 VNDialogueSkin 组件
ApplySkin(skin)    ← 切皮肤：销毁旧自定义实例 → 实例化新 prefab → Bind
Bind(skin)         ← 把行为逻辑接到槽位引用上（两条路共用）
```

- `VNDialogueBox.Build`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:109](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L109)`）：
  建嵌套 Canvas（`:117` 起）设 `overrideSorting`、建 CanvasGroup、
  然后 `BuildDefaultSkin()` + `Bind(_defaultRoot.GetComponent<VNDialogueSkin>())`
  （`VNDialogueBox.Build`，`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:127](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L127)`）。
- `VNDialogueBox.BuildDefaultSkin`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:136](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L136)`）：
  在一个叫 `DefaultSkin` 的子物体下建面板、流光框、名牌、正文、头像窗、箭头，
  逐个赋值给 `skin.shineFrame`（`:164`）、`skin.nameTag`（`:181`）、`skin.nameText`（`:182`）、
  `skin.bodyText`（`:190`）、`skin.portraitWindow`（`:210`）、`skin.portraitImage`（`:211`）、
  `skin.arrow`（`:221`）。
- `VNDialogueBox.ApplySkin`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:236](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L236)`）：
  传 null 就把 `_defaultRoot` 打开、还原根矩形、Bind 回默认
  （`VNDialogueBox.ApplySkin`（[`Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:236`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L236)），`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:249](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L249)` 起）；
  传 prefab 就把 `_defaultRoot` 关掉、把根拉伸铺满画布、实例化 prefab、Bind
  （同方法 `:255` 起）。
- `VNDialogueBox.Bind`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:272](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L272)`）：
  逐个抄槽位引用到私有字段，并在这里**按需补组件**——
  打字机没有就 `AddComponent<VNTypewriterText>()`
  （`VNDialogueBox.Bind`，`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:302](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L302)`）、
  流光框没有特效控制器就 `AddComponent<VNImageEffectController>()`
  （同方法 `:312`）。

> **这个设计好在哪**：
> 1. **零素材可运行**——新人 clone 下来直接进 Play 就有对话框。
> 2. **美术不需要懂代码**——只要在 prefab 根上挂 [`VNDialogueSkin`](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs) 把几个引用拖进去，
>    prefab 里其他装饰节点（渐变底图、角花、多层描边）代码完全不管
>    （`VNDialogueSkin` 类注释里的「授权自由」，`[Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:12](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L12)`）。
> 3. **行为逻辑只写一遍**——打字机、名牌显隐、头像避让、箭头呼吸都只认 `_typer`、`_nameTag` 这些字段。

### 4.3 槽位声明组件长什么样

`VNDialogueSkin`（`[Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:24](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L24)`）就是一堆 public 字段：

| 槽位 | 类型 | 行号 | 留空的后果 |
|---|---|---|---|
| `panel` | RectTransform | `[Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:27](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L27)` | 出入场动画作用于整个皮肤层 |
| `nameTag` | GameObject | `:30` | 旁白时无法整体隐藏名牌 |
| `nameText` | TMP_Text | `:32` | 名牌不显示文字、样式不生效 |
| `bodyText` | TMP_Text | `:35` | **没有打字机**（`Say` 直接静默 return） |
| `arrow` | Graphic | `:38` | 无继续箭头 |
| `portraitWindow` | RectTransform | `:41` | 此皮肤不显示对话头像 |
| `portraitImage` | Image | `:43` | 同上 |
| `portraitBodyInset` | float | `:45` | 0 = 正文不为头像让位 |
| `portraitTagShift` | float | `:47` | 0 = 名牌不为头像让位 |
| `shineFrame` | Image | `:50` | 无流光边框 |
| `toolbarAnchor` | RectTransform | `:53` | 快捷条停靠 panel（再没有就停对话框根） |

**全部槽位可选**是刻意的（`VNDialogueSkin` 类注释，
`[Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:14](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L14)`）。
代码里到处都是 `if (_nameTag != null)`、`if (_typer == null) return;` 这样的降级：

```csharp
HideArrow();
if (_typer == null) return; // 皮肤没给正文槽：静默容错（Lint/日志层面提示）
```
> 出处：`VNDialogueBox.Say`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:645](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L645)）

[`VNChoiceSkin`](Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs:22](Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs#L22)`）则相反——
`container`（`:25`）、`buttonTemplate`（`:28`）、`buttonLabel`（`:31`）三个是**必填**，
缺任何一个就整个退回默认样式并报错：

```csharp
if (skin == null || skin.container == null || skin.buttonTemplate == null ||
    skin.buttonLabel == null)
{
    Debug.LogError("[VNChoice] 皮肤 prefab 缺少 VNChoiceSkin 或必填槽位" +
                   "（container/buttonTemplate/buttonLabel），回退默认样式");
    Destroy(_customRoot);
    _customRoot = null;
    return;
}
```
> 出处：[`VNChoicePanel.ApplySkin`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs)（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:131](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L131)）

> **差别的原因**：对话框的每个部件都能单独降级（没有头像就是不显示头像，其他照旧）；
> 而选项面板缺了容器或模板就**什么都画不出来**，没有「部分降级」的中间态。
> 这提示了一条设计原则：**必填 vs 可选，看的是「缺了之后剩下的东西还成立吗」**。

### 4.4 皮肤的两个坐标系陷阱

**陷阱一：自定义皮肤要铺满整个画布**

程序化默认皮肤是「对话框根就在屏幕底部那一块」，但美术 prefab 常常想把对话框放在顶部、或者
做成整屏渐变带。所以 `ApplySkin` 在切到自定义皮肤时会把根矩形**拉满整个画布**
（`VNDialogueBox.StretchRootToCanvas`，`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:526](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L526)`），
切回默认时再还原（`VNDialogueBox.RestoreRootRect`，
`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:535](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L535)`）。
原始值在第一次 Build 时采样保存（`VNDialogueBox.SaveRootRect`，
`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:515](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L515)`）。

`VNDialogueSkin` 的注释把这条写成了给美术看的说明：
「皮肤实例会被拉伸铺满整个画布（1920×1080 参考分辨率），panel 想放屏幕哪儿就锚在哪儿」
（`[Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:16](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L16)`）。

**陷阱二：选项按钮的排版有两套规则**

`VNChoicePanel` 检测容器上有没有 LayoutGroup：

```csharp
_skinUsesLayout = skin.container.GetComponent<LayoutGroup>() != null;
```
> 出处：`VNChoicePanel.ApplySkin`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:143](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L143)）

有 LayoutGroup → 位置交给它管，入场动画改成**缩放**（因为位移交不出去）：
```csharp
if (_skin != null && _skinUsesLayout)
{
    // Layout 管位置：位移交不出去，改缩放入场
    entry.rect.localScale = Vector3.one * 0.92f;
    entry.rect.DOScale(1f, 0.35f)...
    return;
}
// 错落飞入：右侧 90px 外滑入
```
> 出处：`VNChoicePanel.PlayEntrance`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:206](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L206)）

没有 LayoutGroup → 以模板锚点为首项向下堆叠
（`VNChoicePanel.CreateSkinButton`，`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:232](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L232)`）。

> **学到的东西**：只要你的入场动画动的是 `anchoredPosition`，
> 就和 LayoutGroup 天然冲突（Layout 每帧重写位置）。
> 两条出路：① 检测到 Layout 就换一种不碰位置的动画（本专案的做法）；
> ② 给按钮套一层空壳，Layout 管外壳、动画作用于内层。本专案选了 ①，因为更简单且不增加层级。

### 4.5 「按路径找槽位」：克隆体怎么找到自己的文字槽

皮肤模板 `buttonTemplate` 被克隆之后，克隆体上的 `buttonLabel` 引用**仍指向模板本体**
（Unity 的 Instantiate 只会重定向 prefab 内部引用，而这里模板与 skin 组件在同一个实例里，
skin.buttonLabel 指的是模板那一份）。所以要按**相对路径**在克隆体上重新找：

```csharp
_labelPath = PathBetween(skin.buttonTemplate.transform, skin.buttonLabel.transform);
_costPath = skin.buttonCost != null
    ? PathBetween(skin.buttonTemplate.transform, skin.buttonCost.transform)
    : null;
```
> 出处：`VNChoicePanel.ApplySkin`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:144](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L144)）

`PathBetween`（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:151](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L151)`）沿 parent 链往上拼名字，
`FindByPath`（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:160](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L160)`）在克隆体上按同路径 `Find`。
路径为空串表示槽位就是模板根本身（`VNChoicePanel.FindByPath`（[`Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:160`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L160)），
`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:163](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L163)`）。

> **注意这与第十一章教程锚点的做法正好相反**。
> 这里能用路径寻址，是因为路径是**从 prefab 结构现场算出来的**，
> 美术改了层级路径也跟着变，不会失效。
> 而教程锚点不能用路径，是因为那些 UI 是代码程序化生成的，路径写死在教程资产里，
> 改一次布局就静默失效（[`VNTutorialAnchors`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs) 类注释，
> `[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:9](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L9)`）。
>
> **判断标准**：路径是「运行时从活的引用推导」还是「人手写死在配置里」。
> 前者安全，后者危险。

### 4.6 皮肤状态进存档

对话框皮肤、选项皮肤、名牌样式三者都是**持续状态**，必须进存档：

- [`VNStage.CurrentDialogueSkinId`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:753](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L753)`）
- `VNStage.CurrentChoiceSkinId`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:755`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L755)）（`:755`）
- `VNStage.CurrentNameplateStyleId`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:757`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L757)）（`:757`）

写入存档：`VNStage.CaptureSnapshot`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:843`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L843)） 里三行
（`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:869](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L869)` 起）。
对应的存档字段是 [`VNSaveData.dialogueSkin`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:58](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L58)`）、
`choiceSkin`（`:59`）、`nameplateStyle`（`:60`）。

读档恢复：`VNStage.RestoreSnapshot`（`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:905](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L905)`）
里**皮肤是最先恢复的**，注释写明理由：「UI 皮肤最先恢复：随后重放的台词/选项直接落在正确皮肤上」
（`VNStage.RestoreSnapshot`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:902`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L902)），`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:909](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L909)`）。
而且只在与当前不同时才切换，避免无谓重建（同方法 `:911`）。

剧本入口是 `ui` 命令，Runner 转发给 `VNStage.SetUiSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:764`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L764)）：
```csharp
case "ui":
    // ui dialogue|choice <皮肤id|default>：切换对话框/选项面板皮肤
    // ui name <样式|default>：切换名字（说话人）的装饰样式
    stage.SetUiSkin(cmd.Arg(0), cmd.Arg(1, "default"), cmd.line);
    return null;
```
> 出处：[`VNScriptRunner.Dispatch`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2401](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2401)）

`VNStage.SetUiSkin`（`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:764](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L764)`）
按 `kind` 分三路，从 `VNGameConfig.FindSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:134`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L134)）
（`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:134](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L134)`）查 prefab，
查不到就报错并**保持现状**（`VNStage.SetUiSkin`，
`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:781](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L781)` 起）。

---

## 五、皮肤系统（二）：系统菜单的全局主题与槽位校验

对话框/选项是「一场戏可以换好几套」，系统菜单则相反——**全局唯一一套主题**。
所以它走的是另一条完全不同的机制。

### 5.1 一个 ScriptableObject 管十二个 prefab

[`VNSystemUiSkinSet`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:11](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs#L11)`）
就是十二个 GameObject 字段：

| 分组 | 字段 | 行号 |
|---|---|---|
| 整页 / 弹窗 | `titleMenuPrefab` | `[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:14](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs#L14)` |
| | `configPanelPrefab` | `:15` |
| | `cgGalleryPrefab` | `:16` |
| | `backlogPrefab` | `:17` |
| | `saveLoadPrefab` | `:18` |
| 常驻 HUD | `quickToolbarPrefab` | `:21` |
| | `statsHudPrefab` | `:22` |
| 状态完整页 | `statsPanelPrefab` | `:25` |
| 背包 | `inventoryPrefab` | `:28` |
| 玩法事件面板 | `planPrefab` | `:31` |
| | `resultPopupPrefab` | `:32` |
| 教程卡片 | `tutorialPrefab` | `:35` |

这个资产挂在 `VNGameConfig.systemUiSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)）
（`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)`）上，全局唯一。

### 5.2 「安全实例化」：一个函数解决所有校验

所有系统面板都通过同一个静态方法拿皮肤实例：

```csharp
public static T Instantiate<T>(GameObject prefab, Transform parent, string owner)
    where T : VNSystemUiSkinBehaviour
{
    if (prefab == null) return null;

    var source = prefab.GetComponent<T>();
    if (source == null) { /* 报错 */ return null; }
    if (!source.IsValid(out string sourceError)) { /* 报错 */ return null; }

    var go = Object.Instantiate(prefab, parent, false);
    go.name = "Skin_" + prefab.name;
    var skin = go.GetComponent<T>();
    if (skin != null && skin.IsValid(out _)) return skin;

    Object.Destroy(go);
    /* 报错 */
    return null;
}
```
> 出处：[`VNSystemUiSkinUtility.Instantiate`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs:29](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs#L29)）

注意它做了**两次校验**：实例化前校验 prefab 本体（`:40`），实例化后再校验实例（`:49`）。
第二次看起来多余，但能挡住「prefab 上的引用指向了 prefab 外部物体，Instantiate 后变 null」这种情况。

取 prefab 的入口也统一：
```csharp
public static GameObject Prefab(System.Func<VNSystemUiSkinSet, GameObject> selector)
{
    var config = VNGameConfig.Active;
    return config != null && config.systemUiSkin != null
        ? selector(config.systemUiSkin)
        : null;
}
```
> 出处：`VNSystemUiSkinUtility.Prefab`（[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs:56](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs#L56)）

调用方长这样（十一处基本一模一样）：
```csharp
var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.backlogPrefab);
_skin = VNSystemUiSkinUtility.Instantiate<VNBacklogSkin>(
    skinPrefab, canvasGo.transform, "VNBacklog");
```
> 出处：[`VNBacklog.Build`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:95](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L95)）

### 5.3 槽位校验：抽象基类 + 声明式必填表

```csharp
public abstract class VNSystemUiSkinBehaviour : MonoBehaviour
{
    public abstract void CollectValidationErrors(List<string> errors);

    public bool IsValid(out string error)
    {
        var errors = new List<string>();
        CollectValidationErrors(errors);
        error = string.Join("、", errors);
        return errors.Count == 0;
    }

    protected static void Require(Object value, string displayName, List<string> errors)
    {
        if (value == null) errors.Add(displayName);
    }
}
```
> 出处：`VNSystemUiSkinBehaviour`（[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs:7](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs#L7)）

子类只要写一串 `Require`，报错信息里就会列出**中文的缺失槽位名**：

```csharp
public override void CollectValidationErrors(List<string> errors)
{
    Require(panelRoot, "面板根", errors);
    Require(titleText, "标题", errors);
    Require(scroll, "ScrollRect", errors);
    Require(content, "内容容器", errors);
    Require(entryTemplate, "台词条目模板", errors);
    if (entryTemplate != null && !entryTemplate.IsValid) errors.Add("条目角色名和正文槽位");
}
```
> 出处：[`VNBacklogSkin.CollectValidationErrors`](Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs:19](Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs#L19)）

**「模板的模板」这一层也有校验**：`entryTemplate` 是 [`VNBacklogEntrySkin`](Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs)
（`[Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs:7](Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs#L7)`），
它不是 `VNSystemUiSkinBehaviour` 子类（不需要被 Instantiate 校验），
所以只提供一个 `IsValid` 属性（`[Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs:12](Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs#L12)`），
由父级槽位在 `CollectValidationErrors` 里手动检查。
同一模式见 [`VNSaveSlotSkin.IsValid`](Assets/Project/Scripts/VNEffects/Script/VNSaveSlotSkin.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNSaveSlotSkin.cs:21](Assets/Project/Scripts/VNEffects/Script/VNSaveSlotSkin.cs#L21)`）、
[`VNStatsHudEntrySkin.IsValid`](Assets/Project/Scripts/VNEffects/Script/VNStatsHudEntrySkin.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHudEntrySkin.cs:14](Assets/Project/Scripts/VNEffects/Script/VNStatsHudEntrySkin.cs#L14)`）、
[`VNStatsPanelRowSkin.IsValid`](Assets/Project/Scripts/VNEffects/Script/VNStatsPanelRowSkin.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNStatsPanelRowSkin.cs:14](Assets/Project/Scripts/VNEffects/Script/VNStatsPanelRowSkin.cs#L14)`）。

> **这个两层结构值得学**：
> 「整页皮肤」需要被工具实例化并校验 → 继承 `VNSystemUiSkinBehaviour`；
> 「行/卡模板」只是整页皮肤的一个字段 → 只需要一个 `IsValid` 属性。
> 不要为了统一而让所有东西都继承同一个基类——**继承的成本是「必须实现抽象方法」**，
> 对只有两个字段的行模板来说是纯负担。

### 5.4 缺皮肤时的三种反应，与它们的分界线

理论上「缺失 → 退回程序化默认」，但实际代码里有三种不同的反应：

**反应 A：抛异常（强制要求 prefab）**

```csharp
_toolbarSkin = VNSystemUiSkinUtility.Instantiate<VNQuickToolbarSkin>(
    skinPrefab, transform, "VNQuickToolbar");
if (_toolbarSkin == null)
    throw new System.InvalidOperationException("Quick toolbar prefab is missing or invalid.");
```
> 出处：[`VNQuickToolbar.Build`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:48](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L48)）

同样抛异常的还有：
[`VNConfigPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:106](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L106)`）、
[`VNSaveLoadPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:133](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L133)`）、
`VNBacklog.Build`（`[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:99](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L99)`）、
[`VNTitleMenu.Build`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:198](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L198)`）、
[`VNStatsHud.BuildHud`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:255](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L255)`）、
[`VNResultPopupModule.OnLaunch`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:84](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L84)`）。

**反应 B：退回程序化 UI**

```csharp
_panelSkin = VNSystemUiSkinUtility.Instantiate<VNStatsPanelSkin>(
    skinPrefab, _canvas.transform, "VNStatsPanel");
if (_panelSkin != null)
{
    /* 绑定皮肤 */
    return;
}
// 以下是程序化构建
_panel = new GameObject("Panel", typeof(RectTransform));
```
> 出处：`VNStatsHud.BuildPanel`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:466](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L466)）

**反应 C：单项降级（皮肤在，但某个可选槽位没配）**

```csharp
_wheelBacklogLabel = skin.wheelBacklogLabel;
if (skin.wheelBacklogButton != null)
    BindButton(skin.wheelBacklogButton, ToggleWheelBacklog);
UpdateWheelBacklogLabel();
```
> 出处：`VNConfigPanel.BindCustomSkin`（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:164](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L164)）

注释解释了理由：「槽位可留空：老的皮肤 prefab 没有这个按钮，缺了就只是设置面板里没这一项，
开关本身照常生效（默认开），不影响面板其它部分」
（`VNConfigPanel.BindCustomSkin`，`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:162](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L162)`）。

> **观察到的演进痕迹**：`VNStatsHud.BuildPanel` 保留了完整的程序化兜底（反应 B），
> 而 `VNQuickToolbar`、`VNConfigPanel`、`VNSaveLoadPanel`、`VNBacklog`、`VNTitleMenu`
> 已经改成抛异常（反应 A）——说明这几个面板的程序化路径已经被删掉了，
> prefab 变成了硬依赖。`CLAUDE.md` 里写的「缺失时只退回该项程序化 UI」对这几个已经不准确了。
>
> **这是不是好事？** 权衡如下：
> - **好处**：删掉了每个面板几百行的程序化构建代码，维护面积小很多。
>   而且导出模板菜单（`VNSystemUiSkinExporter.ExportAll`（[`Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:23`](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L23)），
>   `[Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:23](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L23)`）一键就能生成一整套，
>   「零素材可运行」实际上被「一键生成素材」替代了。
> - **坏处**：抛异常发生在 `Build()` 里，而 `Build()` 常在 `Open()` 里被调用——
>   玩家按一下 F5 就抛异常，比「显示一个丑但能用的面板」严重。
>   而且异常信息是英文的，与专案其余部分的中文报错风格不一致。
> - **建议**：把 `throw` 换成「报中文错误 + 显示一个最小可用的应急面板（只有关闭按钮）」，
>   至少不会让玩家卡在一个抛异常的按键上。这是一个低成本的健壮性改进。

### 5.5 导出默认模板：怎么把程序化 UI「物化」成 prefab

两条导出线：

**系统菜单主题**：[`VNSystemUiSkinExporter.ExportAll`](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs)
（`[Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:23](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L23)`）
一次性生成 11 个 prefab 并写进 `VNSystemUiSkinSet`
（同方法 `:36` 起连续 11 行），然后自动登记到 [`VNGameConfig.systemUiSkin`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)
（同方法 `:52`）。资产目录写死在 `Dir` 常量
（`[Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:13](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L13)`）。

还有一个「只重导两个面板」的入口 `ExportEventPanels`
（`[Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:68](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L68)`），
避免把手改过的 prefab 一起覆盖掉——这是很实用的工程细节。

**对话框/选项皮肤**：`VNUiSkinExporter.ExportAll`（[`Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:30`](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L30)）
（`[Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:30](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L30)`）
生成四个 prefab（默认 + 顶部示范 + 居中列 + 右侧列，同方法 `:40` 起四行）。

**关键一步是把程序化贴图烘焙成 PNG**：
```csharp
Sprite rounded = BakeSprite("VN_RoundedRect",
    VNProceduralTextures.RoundedRectSprite.texture, new Vector4(22, 22, 22, 22));
```
> 出处：[`VNUiSkinExporter.ExportAll`](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:35](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L35)）

`BakeSprite`（`[Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:61](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L61)`）里有一个
很容易踩的坑：程序化贴图调过 `Apply(false, true)` 释放了 CPU 拷贝，**不可读**，
所以必须先经 RenderTexture 走一遍 GPU 再 `ReadPixels` 拿回可读副本
（`VNUiSkinExporter.BakeSprite`（[`Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:61`](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L61)），`[Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:64](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L64)` 的注释）。

> **学到的东西**：如果你也做「运行时程序化贴图」，
> 记得 `Texture2D.Apply(updateMipmaps, makeNoLongerReadable)` 的第二个参数一旦传 true，
> `EncodeToPNG` / `GetPixels` 就全废了。要导出必须走 RenderTexture 绕一圈。

---

## 六、对话框深挖：VNDialogueBox + 打字机 + 名牌

对话框是视觉小说的心脏，也是本专案 UI 里状态最多、机制最密的一个组件（760 行）。
拆成四块讲：外观构成、说话流程、打字机、名牌样式。

### 6.1 外观构成（程序化默认皮肤）

[`VNDialogueBox.BuildDefaultSkin`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs)（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:136](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L136)`）
拼出六个部件：

| 部件 | 做法 | 行号 |
|---|---|---|
| 半透明磨砂面板 | `RoundedRectSprite` + `Image.Type.Sliced` | `[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:156](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L156)` |
| 边缘流光框 | `RoundedFrameSprite` + Sliced，赋给 `skin.shineFrame` | `:161` |
| 名牌（骑在面板顶边） | 锚在左上、pivot (0, 0.5)、`anchoredPosition (44, 4)` | `:167` |
| 正文 | TMP，`offsetMin (40,26)`、`offsetMax (-40,-40)`、`lineSpacing 25` | `:185` |
| 头像窗口 | `RectMask2D` 裁切，锚左下、`sizeDelta = portraitWindowSize` | `:193` |
| 继续箭头 | 一个 TMP 文字「▼」，锚右下 | `:214` |

有两个细节值得单独说：

**`lineSpacing = 25` 的注释**：「TMP 行距单位为字号百分比，25 ≈ legacy 的 1.25 倍行距」
（`VNDialogueBox.BuildDefaultSkin`，`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:189](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L189)`）。
这是从 legacy Text 迁移到 TMP 时最容易搞错的一个参数——它不是像素，也不是倍数，是百分比。

**头像窗口用 `RectMask2D` 而不是 `Mask`**：
`RectMask2D` 是纯矩形裁切、不需要额外的 stencil 通道，性能好且不会干扰其他 Mask。
（对比：大头贴的开窗用的是 `Mask`，因为它要**非矩形**的开窗形状，
见 [`VNPhotoBoothModule.BuildViewFinder`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs)，`[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:532](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L532)`。）

**头像避让是「皮肤声明 + 绑定时采样基准」**：

```csharp
_portraitBodyInset = skin.portraitBodyInset;
_portraitTagShift = skin.portraitTagShift;
_bodyRect = skin.bodyText != null ? (RectTransform)skin.bodyText.transform : null;
_tagRect = _nameTag != null ? (RectTransform)_nameTag.transform : null;
_bodyBaseOffsetMin = _bodyRect != null ? _bodyRect.offsetMin : Vector2.zero;
_tagBasePos = _tagRect != null ? _tagRect.anchoredPosition : Vector2.zero;
```
> 出处：`VNDialogueBox.Bind`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:284](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L284)）

有头像时把正文左缩进、名牌右移；没头像时回到基准值：
```csharp
if (_bodyRect != null && _portraitBodyInset > 0f)
    _bodyRect.offsetMin = _bodyBaseOffsetMin +
                          new Vector2(show ? _portraitBodyInset : 0f, 0f);
```
> 出处：`VNDialogueBox.ApplyPortrait`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:717](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L717)）

> **为什么要「绑定时采样基准」而不是「每次加减」**：
> 加减是累积操作，一旦有一次没配对（比如中途切皮肤），偏移就永久跑掉。
> 「记住基准 + 每次从基准算绝对值」是幂等的，调多少次结果都一样。
> 这是 UI 代码里非常重要的一条原则：**能算绝对值就别做增量**。

### 6.2 说一句话的完整流程

从剧本一行台词到屏幕上出字，链路是：

```
VNScriptRunner.NormalSayCo (:2616)
  → VNScriptLocale.TextOf(cmd)          取当前语言译文
  → stage.Say(speaker, expr, text, ...) (VNStage.cs:1077)
      ├─ StopSpeaking()                 所有角色闭嘴
      ├─ ApplyExpression(c, expr)       切表情
      ├─ speakerHighlight.SetSpeaker()  说话者高亮
      ├─ dialogue.SetPortrait(...)      头像
      ├─ dialogue.SetSpeakerStyle(def)  名牌配色
      ├─ dialogue.Say(name, text)       → 打字机
      └─ c.mouth?.BeginSpeaking(...)    口型
  → _backlog?.Record(...)               进回想
  → 等打字完
  → _waitingAtSay = true                进入「可存档」状态
  → 等 _advance / Auto 超时 / Skip
```

> 📎 参考：[VNStage.cs:1077](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L1077)

[`VNStage.Say`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:1077](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L1077)`）里分两条路：
角色已注册（`:1081`）走完整流程；未注册或旁白（`:1093`）则清高亮、无头像、名牌配色回默认
（`VNStage.Say`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:1077`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L1077)），`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:1095](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L1095)` 起）。

`VNDialogueBox.Say`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:628](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L628)`）本身很短：
```csharp
Build();
if (!_shown) Show();

_lastSpeaker = speakerName;
_lastContent = content;

bool hasName = !string.IsNullOrEmpty(speakerName);
if (_nameTag != null) _nameTag.SetActive(hasName);
if (hasName && _nameText != null)
{
    _nameText.text = speakerName;
    ResizeNameTag(); // 名字长度变了，名牌宽度跟上
}

HideArrow();
if (_typer == null) return;
_typer.onComplete = ShowArrow;
_typer.Play(content);
```
> 出处：`VNDialogueBox.Say`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:628](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L628)）

`_lastSpeaker` / `_lastContent` 存下来是为了**切皮肤时能把当前这句重现**
（`VNDialogueBox.RestoreVisualState`，`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:484](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L484)`）：

```csharp
if (_shown && _lastContent != null && _typer != null)
{
    ...
    _typer.onComplete = ShowArrow;
    _typer.Play(_lastContent);
    _typer.Complete(); // 满字直出，不重打字
}
```
> 出处：`VNDialogueBox.RestoreVisualState`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:489](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L489)）

> **注意 `Play` + 立刻 `Complete` 的写法**。
> 不能直接 `_text.text = content` 了事——打字机是靠逐字修改顶点来实现的，
> 必须走它自己的状态机才能保证「所有字都是完全不透明、位置归位」的状态。
> `Complete()` 里 `_visible = float.MaxValue; _animating = true;`
> （`VNTypewriterText.Complete`（[`Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:51`](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L51)），`[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:53](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L53)`）
> 就是让下一次 `LateUpdate` 把网格刷成完全显示。

### 6.3 打字机：TMP 顶点动画

[`VNTypewriterText`](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs)（`[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:14](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L14)`）
只有 124 行，但把 TMP 逐字动画的正确做法完整演示了一遍。

**核心思路**：不改 `text` 字符串，只改**顶点**。
`Play` 一次性把整段文字赋给 TMP（`VNTypewriterText.Play`（[`Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:41`](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L41)），
`[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:44](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L44)`），
然后每帧在 `LateUpdate` 里根据「已显现字数进度 `_visible`」修改每个字的顶点 y 与 alpha。

```csharp
for (int i = 0; i < info.characterCount; i++)
{
    var ci = info.characterInfo[i];
    if (!ci.isVisible) continue;

    float t = Mathf.Clamp01((_visible - visibleIndex) / fadeSpanChars);
    visibleIndex++;
    if (t >= 1f) continue;
    anyPartial = true;

    float ease = 1f - (1f - t) * (1f - t); // OutQuad
    float yOffset = -(1f - ease) * riseHeight; // 从下方上浮到位

    var mesh = info.meshInfo[ci.materialReferenceIndex];
    int vi = ci.vertexIndex;
    for (int j = 0; j < 4; j++)
    {
        mesh.vertices[vi + j].y += yOffset;
        var c = mesh.colors32[vi + j];
        c.a = (byte)(c.a * ease);
        mesh.colors32[vi + j] = c;
    }
}
_text.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
```
> 出处：`VNTypewriterText.LateUpdate`（[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:80](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L80)）

**五个必须知道的点**：

1. **`ForceMeshUpdate()` 要在读 textInfo 之前调**
   （`VNTypewriterText.LateUpdate`，`[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:75](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L75)`）。
   否则 `characterInfo` 可能是上一帧的、顶点索引对不上。
2. **富文本标签不占字数**。类注释写明「characterInfo 已剔除控制符，优于旧版按 quad 计数」
   （`VNTypewriterText` 类注释，`[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:10](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L10)`）。
   `ci.isVisible` 为 false 的字符（空格、换行、标签）直接 `continue`，不计入 `visibleIndex`。
3. **每个字四个顶点**，起始索引是 `ci.vertexIndex`，材质分组是 `ci.materialReferenceIndex`——
   多材质（比如 fallback 字体接管的生僻字）时不能只改 `meshInfo[0]`。
4. **动画结束要停止每帧重建网格**：
   ```csharp
   if (!_playing && !anyPartial) _animating = false;
   ```
   > 出处：`VNTypewriterText.LateUpdate`（[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:121](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L121)）
   这一条很重要——不加的话即使台词早已打完，每帧还在 `ForceMeshUpdate` + 遍历所有字符，
   在长台词场景下是白白的性能开销。
5. **空行的死锁防护**：
   ```csharp
   if (visibleIndex == 0)
   {
       // 整段没有可见字（空行/纯空白）：视作立即播完，避免卡住剧本推进
       _playing = false;
       onComplete?.Invoke();
   }
   ```
   > 出处：`VNTypewriterText.LateUpdate`（[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:107](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L107)）
   剧本作者写了一行只有空格的台词，没有这一条整个游戏就卡死了。

**打字音的节流**：
```csharp
void Update()
{
    if (!_playing) return;
    int before = Mathf.FloorToInt(_visible);
    _visible += charsPerSecond * Time.deltaTime;
    if (Mathf.FloorToInt(_visible) > before)
        VNAudio.TypeTick(); // 每显现一个新字打一次字音（内部带节流）
}
```
> 出处：`VNTypewriterText.Update`（[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:62](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L62)）

[`VNAudio.TypeTick`](Assets/Project/Scripts/VNEffects/Script/VNAudio.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNAudio.cs:370](Assets/Project/Scripts/VNEffects/Script/VNAudio.cs#L370)`）里再做一层节流与
随机音高：
```csharp
if (Time.unscaledTime - a._lastTickTime < a.typingTickInterval) return;
a._lastTickTime = Time.unscaledTime;
a._tick.pitch = Random.Range(0.94f, 1.06f); // 轻微随机音高，不机械
a._tick.PlayOneShot(a.typingTick, a.seVolume * 0.7f);
```
> 出处：`VNAudio.TypeTick`（[Assets/Project/Scripts/VNEffects/Script/VNAudio.cs:374](Assets/Project/Scripts/VNEffects/Script/VNAudio.cs#L374)）

> **「轻微随机音高」是一个通用的音效技巧**：同一个 clip 连续快速播放会有明显的机械感，
> ±6% 的 pitch 抖动就能消掉。本专案在多处用了这招。

**Skip 快进时的处理**：`NormalSayCo` 里
```csharp
yield return null; // 让打字机先启动
if (_skip && stage.dialogue != null) stage.dialogue.CompleteTyping();
while (stage.dialogue != null && stage.dialogue.IsTyping)
{
    if (_skip) stage.dialogue.CompleteTyping();
    yield return null;
}
```
> 出处：[`VNScriptRunner.NormalSayCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2625](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2625)）

注意那句 `yield return null; // 让打字机先启动` —— `_typer.Play()` 只是设了状态，
真正的顶点更新在 `LateUpdate`，所以必须等一帧 `IsTyping` 才可靠。

### 6.4 名牌样式：TMP 材质的极限

[`VNNameplateStyle`](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:53](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L53)`）
是一个**纯数据 + 静态 Apply** 的类（不持有场景引用，可单独测试）。
十个内置预设的 id 在 `VNNameplateStyleId`（`[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:7](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L7)`）。

#### 三个硬约束（类注释里明确列出，`[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:43](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L43)` 起）

**约束 1：材质必须走实例**
```csharp
// ---- 材质实例：走 fontMaterial 而非 fontSharedMaterial ----
var mat = text.fontMaterial;
if (mat == null) return;
```
> 出处：`VNNameplateStyle.ApplyTo`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:520](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L520)）

TMP 组件默认用 `fontSharedMaterial`（字体资产自带的那一份）。
直接改它会把正文、按钮、Backlog 里所有用同一字体的文字一起改掉。
`text.fontMaterial` 在首次访问时自动 new 一份实例。

**约束 2：描边厚度受字体图集 padding 限制**

这条与 [`VNFont`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs) 的装饰字体设计直接相关，`VNFont` 里的注释算出了公式
（`[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:71](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L71)` 起）：

> 描边实际像素厚度 ≈ outlineWidth ×(padding+1)×(显示字号/采样点)，
> 所以 padding 是描边粗细的天花板——padding 14 时描边推到 0.2 就饱和，
> 再调大数值没有任何视觉变化（不是被切角，是直接被钳住）。

于是装饰字体单开一套资产：`DisplaySamplePointSize = 120`
（`[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:69](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L69)`）、
`DisplayAtlasPadding = 22`（`:79`），
正文则是 `SamplePointSize = 64`（`:58`）、`AtlasPadding = 6`（`:59`）。

**但 padding 也不能无脑放大**——`VNFont` 的注释记了一次实测失败
（`[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:62](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L62)` 起）：
「64pt 采样配 padding 24（37%）时，字形在图集里的有效分辨率被挤掉，
SDF 梯度变缓，描边和投影一起糊成一层淡影，比 padding 14 还差。
120pt 采样配 padding 22 保持在 ~18%，既能撑住厚描边又不糊。」

> **这是全专案最有价值的一条踩坑记录之一**：SDF 字体的 padding 与采样点必须**等比例**，
> 单独调大 padding 只会降低字形分辨率。记住比例约 18%。

**约束 3：underlay 通道只有一条**

TMP 的 underlay（下层）通道既能当投影用，也能当「第二层外描边」用，但**只有一条**。
所以做成了枚举二选一：
```csharp
public enum UnderlayUse
{
    None = 0,
    /// <summary>当第二层外描边用：offset 归零 + 大 dilate，环绕出一圈深色轮廓</summary>
    SecondOutline = 1,
    /// <summary>当投影用：offset 往右下偏</summary>
    Shadow = 2,
}
```
> 出处：`VNNameplateStyle.UnderlayUse`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:56](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L56)）

应用时 `SecondOutline` 会强制把偏移归零：
```csharp
Vector2 offset = underlayUse == UnderlayUse.SecondOutline
    ? Vector2.zero : underlayOffset;
```
> 出处：`VNNameplateStyle.ApplyTo`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:542](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L542)）

而且改 underlay 之前必须 `EnableKeyword`：
```csharp
mat.EnableKeyword(UnderlayKeyword);
```
> 出处：`VNNameplateStyle.ApplyTo`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:539](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L539)）

> **shader keyword 是最容易漏的一步**：不 Enable 的话所有 `SetFloat` 都执行成功、
> 没有任何报错，但屏幕上一点变化都没有。这类「静默失效」最难查。

#### HDR 发光与渐变二选一

```csharp
// HDR 发光走材质，不走顶点色：uGUI 的顶点色被钳到 1，写多少都不会超过 1，
// 而 Bloom 的阈值就是 1.0（项目硬约定：发光 = HDR 颜色 + Bloom）。
// 代价是发光与上下渐变二选一——渐变只能由顶点色表达。
bool hdr = faceHdrBoost > 1.0001f;
if (hdr)
{
    text.enableVertexGradient = false;
    text.color = Color.white;
}
else if (useGradient)
{
    text.color = Color.white;
    text.enableVertexGradient = true;
    text.colorGradient = new VertexGradient(top, top, bottom, bottom);
}
```
> 出处：`VNNameplateStyle.ApplyTo`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:497](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L497)）

Neon 样式（`VNNameplateStyleId.Neon`，`[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:29](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L29)`）
就是靠 `faceHdrBoost > 1` 走这条路发光的。

#### 金属浮雕：Mobile shader 不支持时的处理

```csharp
void ApplyBevel(Material mat)
{
    if (!mat.HasProperty(IdBevel))
    {
        if (useBevel && !_bevelWarned)
        {
            _bevelWarned = true;
            Debug.LogWarning("[VNNameplate] 当前字体材质的 shader 不支持浮雕（多半是 " +
                             "TMP Mobile 版 shader），金/银样式会退化成普通渐变描边。" +
                             "把字体资产的 shader 换成 TextMeshPro/Distance Field 即可。");
        }
        return;
    }
    ...
}
```
> 出处：`VNNameplateStyle.ApplyBevel`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:559](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L559)）

注释里那句「直接 SetFloat 不会报错但也不会有效果，静默失效比报错更难查」
（`[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:557](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L557)`）是本专案的一贯态度。
而且警告只发一次（`_bevelWarned` 静态标志，
`[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:595](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L595)`），
因为「每句台词都会重新上妆，不然刷屏」（同处注释）。

#### 三层字系列的立身之本

`VNNameplateStyleId` 的注释里有一句设计洞察
（`[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:18](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L18)` 起）：

> 以下为「三层字」系列：面 + 角色色描边 + 深色最外层。
> 最外层是深色是这一系列的立身之本：Bold/Outline 的最外层是白色，
> 遇到白背景或亮立绘就整个消失（实测浅背景下名字糊得看不见）。

> **通用规律**：任何要「压在不可控背景上」的文字，最外一圈必须是深色。
> 白色描边只在深色背景下有效。这条适用于所有游戏的 HUD 文字。

#### 语言切换时名牌要重新上妆

换字体会让 TMP 丢掉材质实例，描边/渐变全废。所以有一个专门的事件：

```csharp
public static event System.Action DisplayFontChanged;
```
> 出处：`VNFont.DisplayFontChanged`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:321](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L321)）

`VNFont.HandleLanguageChanged`（`[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:277](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L277)`）
在替换完场景里所有 TMP 文字的字体之后触发它
（`VNFont.HandleLanguageChanged`（[`Assets/Project/Scripts/VNEffects/Script/VNFont.cs:277`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L277)），`[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:314](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L314)`）。

对话框订阅它：
```csharp
void HookLocaleEvents()
{
    if (_localeHooked) return;
    _localeHooked = true;
    VNFont.DisplayFontChanged += OnDisplayFontChanged;
}
```
> 出处：`VNDialogueBox.HookLocaleEvents`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:470](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L470)）

并在 `OnDestroy` 退订（`VNDialogueBox.OnDestroy`，
`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:757](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L757)`）。

**正文字体与装饰字体必须分开替换**，`VNFont.HandleLanguageChanged` 用两个 HashSet 区分：
```csharp
var managedBody = new HashSet<TMP_FontAsset>();
var managedDisplay = new HashSet<TMP_FontAsset>();
foreach (var kv in _cache)
{
    if (kv.Value == null) continue;
    if (kv.Key.isDisplay) managedDisplay.Add(kv.Value);
    else managedBody.Add(kv.Value);
}
```
> 出处：`VNFont.HandleLanguageChanged`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:281](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L281)）

注释说明理由：「把名牌的 Heavy 字体也换成正文字体，粗描边样式会当场垮掉
（这正是加装饰字体后最容易踩的坑）」
（`VNFont.HandleLanguageChanged`，`[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:279](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L279)`）。

而且**只替换 VNFont 管理的字体**——不在 `_cache` 里的字体（编辑期手动指定的）不动，
这样美术给某个特殊标题单独指定的字体不会被语言切换搞乱。

### 6.5 名牌宽度自适应：只动程序化皮肤

```csharp
void ResizeNameTag()
{
    if (!autoResizeNameTag || HasCustomSkin) return;
    if (_tagRect == null || _nameText == null) return;
    if (string.IsNullOrEmpty(_nameText.text)) return;

    float preferred = _nameText.GetPreferredValues(_nameText.text).x;
    // 描边和字距会往外溢一圈，留够余量再取原始宽度当下限
    float width = Mathf.Max(_nameTagBaseSize.x, preferred + 44f);
    _tagRect.sizeDelta = new Vector2(width, _nameTagBaseSize.y);
}
```
> 出处：`VNDialogueBox.ResizeNameTag`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:424](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L424)）

注释解释为什么不动自定义皮肤：「美术 prefab 的名牌尺寸是照着背景图排的，擅自改会破相」
（`VNDialogueBox.ResizeNameTag`，`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:421](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L421)`）。

`GetPreferredValues` 是 TMP 的**同步**测量方法——当场就能算出来，
不需要等一次布局。这一点在 `VNToast.BuildCard`（[`Assets/Project/Scripts/VNEffects/Script/VNToast.cs:201`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L201)） 里也被利用了：
```csharp
// GetPreferredValues 当场就能算（preferredWidth 要等一次布局才有值）
textElement.preferredWidth =
    Mathf.Clamp(text.GetPreferredValues(message).x + 4f, 60f, 520f);
```
> 出处：[`VNToast.BuildCard`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs)（[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:245](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L245)）

> **学到的东西**：`TMP_Text.preferredWidth` 属性要等一次 Canvas 布局才有值，
> 而 `GetPreferredValues(string)` 是立刻算的。
> 在「刚创建就要知道宽度」的场景（LayoutElement 赋值、名牌自适应）必须用后者。

### 6.6 底部横线按需补建

```csharp
void EnsureUnderline()
{
    if (_nameTagUnderline != null || _nameTag == null) return;
    var go = new GameObject("StyleUnderline",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    ...
    _nameTagUnderline = img;
}
```
> 出处：`VNDialogueBox.EnsureUnderline`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:437](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L437)）

注释：「名牌下的横线装饰按需补建（皮肤 prefab 里没有也能用）」
（`VNDialogueBox.EnsureUnderline`，`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:436](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L436)`）。

而 `FindPlateImage`（`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:454](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L454)`）
在找名牌底板时会**跳过**这个补出来的横线：
```csharp
foreach (var img in nameTag.GetComponentsInChildren<Image>(true))
{
    if (img.gameObject.name == "StyleUnderline") continue;
    return img;
}
```
> 出处：`VNDialogueBox.FindPlateImage`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:458](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L458)）

> **这是「代码补出来的节点」的通用麻烦**：它会混进 `GetComponentsInChildren` 的结果里。
> 本专案用「认名字排除」解决，简单但脆弱（改名字就失效）。
> 更稳的做法是给补出来的节点挂一个标记组件（比如空的 `VNGeneratedDecoration`），
> 按类型排除。不过在这个规模下认名字够用。

---

## 七、选项面板深挖：VNChoicePanel

### 7.1 三段演出

[`VNChoicePanel`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs) 类注释开门见山（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:10](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L10)` 起）：
「零新特效，纯组合现有件」——
选项错落飞入、悬停扫光 + 微放大、选中闪光确认 + 其余噪声溶解。

三段的实现分别在：
- 飞入：`VNChoicePanel.PlayEntrance`（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:201](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L201)`），
  延迟 `index * 0.09f` 做错落。
- 悬停：`VNChoicePanel.FinishButton` 里的 `EventTrigger`
  （`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:402](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L402)` 起），
  `PointerEnter` 播扫光 + `DOScale(1.045f)`，`PointerExit` 缩回。
- 选中：`VNChoicePanel.Choose`（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:431](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L431)`），
  被选项 `DOFlash` + `PlayShine` + `DOScale(1.07f, OutBack)`，
  落选项 `DODissolve(0f, 0.45f)` + 淡到 0.6。

**回调延迟 0.8 秒才触发**：
```csharp
int chosen = index;
DOVirtual.DelayedCall(0.8f, () =>
{
    var cb = _callback;
    _callback = null;
    HideAll(() =>
    {
        _busy = false;
        cb?.Invoke(chosen);
    });
}).SetLink(gameObject);
```
> 出处：`VNChoicePanel.Choose`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:456](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L456)）

> **为什么要延迟**：让玩家看清「我选的是哪一个」。
> 立刻跳转的话，选中演出根本来不及看。0.8 秒是「看得清但不拖沓」的经验值。
> 注意 `_callback` 在调用前先置 null 再本地保存——防止回调里又触发一次 Show 导致递归。

### 7.2 「四条以上等比压缩」：一个写死成 const 的几何结论

```csharp
/// <summary>
/// 选项总高的上限（像素，1080 画布）。超过就整体等比压缩。
///
/// 由来：选项区中心在 y=+60，对话框上沿约在 y=-290。430 的一半是 215，
/// 于是最低那条落在 y=-155，距对话框还有约 135px 余量。
///
/// 写成 const 而不是 public 字段，是因为它是「不压到对话框」的几何结论，
/// 不是手感参数——做成序列化字段的话，场景里躺着的旧值会盖掉代码改动
/// （ProjectCodeGuide 十二节记过这个坑）。
/// </summary>
const float MaxTotalHeight = 430f;
```
> 出处：`VNChoicePanel.MaxTotalHeight`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:283](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L283)）

压缩逻辑：
```csharp
void ResolveMetrics(int total, out float height, out float spacing, out float fontSize)
{
    height = buttonSize.y;
    spacing = buttonSpacing;
    fontSize = DefaultFontSize;

    float totalH = total * height + (total - 1) * spacing;
    if (totalH <= MaxTotalHeight) return;

    float k = MaxTotalHeight / totalH;
    height *= k;
    spacing *= k;
    fontSize = Mathf.Max(MinFontSize, fontSize * k);
}
```
> 出处：`VNChoicePanel.ResolveMetrics`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:290](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L290)）

`MinFontSize = 20`（`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:306](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L306)`）
的注释：「再小就看不清了，宁可让总高略微超一点」。

> **这段的两个设计教训**：
> 1. **「几何结论」应该是 const，「手感参数」才应该是 public 字段。**
>    区分标准：改了之后需不需要重新验证别的东西？需要 → const（改代码时会看到注释里的推导）。
>    这条在 Unity 项目里尤其重要，因为**场景里躺着的旧序列化值会静默覆盖代码里的新默认值**。
> 2. **3 条及以下永远返回原值**（注释在 `ResolveMetrics` 上方，
>    `[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:286](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L286)`）——
>    向后兼容做得很干净，既有的演出一个像素都不变。
>    做「自适应」改造时先保证「原有情况完全不变」，是降低回归风险的好习惯。

### 7.3 置灰选项也要吃射线

```csharp
img.raycastTarget = true; // 置灰项也接收 raycast：挡住穿透点击推进剧情
```
> 出处：`VNChoicePanel.CreateDefaultButton`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:330](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L330)）
> 同样一行也在 `VNChoicePanel.CreateSkinButton`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:263](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L263)）

而 Button 组件被禁用：
```csharp
else
{
    // 模板可能自带 Button（美术直接拿现成按钮改）：置灰项禁用交互
    var button = go.GetComponent<Button>();
    if (button != null) button.interactable = false;
}
```
> 出处：`VNChoicePanel.FinishButton`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:421](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L421)）

> **为什么要区分「吃射线」与「可交互」**：
> 如果置灰选项不吃射线，玩家点它 → 射线穿过去落到背景 →
> Runner 的 `IsPointerOverInteractiveUi` 返回 false → 推进台词。
> 玩家的感受是「我点了个买不起的选项，剧情居然往下走了」。
> 吃射线 + Button 禁用 = 点了没反应，这才是正确的。

### 7.4 与 Runner 的协作：条件、花费、死锁防护

[`VNScriptRunner.ChoiceCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2838](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2838)`）
在调用面板之前做了三件事：

**1. `SetSkip(false)` —— 到选项必停**
```csharp
SetSkip(false); // 到选项必停，玩家必须亲自选
```
> 出处：`VNScriptRunner.ChoiceCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2851](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2851)）

**2. `if:` 条件过滤 + 全隐藏时的死锁防护**
```csharp
if (visible.Count == 0)
{
    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：choice 所有选项的 if: 条件都不满足，" +
                     "为避免卡死改为全部显示");
    for (int i = 0; i < cmd.options.Count; i++) visible.Add(i);
}
```
> 出处：`VNScriptRunner.ChoiceCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2862](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2862)）

**3. `cost:` 花费判定 + 全付不起时的死锁防护**
```csharp
if (!anyInteractable)
{
    Debug.LogError($"[VNScript] 第 {cmd.line} 行：choice 所有可见选项都付不起 cost:，" +
                   "为避免卡死全部解禁——请给玩家留一个免费选项");
    foreach (var po in panelOptions) po.interactable = true;
}
```
> 出处：`VNScriptRunner.ChoiceCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2885](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2885)）

> **这两条「宁可放行也不卡死」的防护是很成熟的做法**。
> 剧本是人写的，条件写错在所难免。
> 关键在于**报错级别的区分**：条件全不满足是 `LogWarning`（可能是有意的），
> 全付不起是 `LogError`（几乎一定是设计失误）。

**选择后的四步**（`VNScriptRunner.ChoiceCo`，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2896](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2896)` 起）：
```csharp
var opt = cmd.options[visible[chosen]];
_backlog?.Record(VNLocale.T("backlog.choice"), VNScriptLocale.TextOf(opt));
if (!string.IsNullOrEmpty(opt.costOp)) _statsHud?.ApplyCost(opt.costOp, opt.line);
if (!string.IsNullOrEmpty(opt.flagOp)) VNFlags.Apply(opt.flagOp);
if (!string.IsNullOrEmpty(opt.jumpLabel)) JumpTo(opt.jumpLabel, opt.line);
```
> 出处：`VNScriptRunner.ChoiceCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2896](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2896)）

注意 `visible[chosen]` 这个**索引映射**：面板只知道自己显示了几个，
返回的是可见列表里的下标，要映射回原始选项索引。

而且 `VNScriptLocale.TextOf(candidate)` 只影响**显示**，
「匹配按索引，不受影响」（注释在 `ChoiceCo` 内，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2876](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2876)`）——
翻译不会破坏分支逻辑。

---

## 八、系统面板逐个拆解

这一章把十一个系统面板过一遍。每个都按同样的结构讲：定位、怎么开、怎么关、
数据从哪来、有什么特别的地方。

### 8.1 快捷功能条 VNQuickToolbar

**定位**：常驻在对话框上的一排按钮（存档/读档/快存/快读/自动/快进/回想/任务/属性/背包/画廊/设置/隐藏 UI）。

**特别之处：它挂在对话框身上，不是独立物体**
```csharp
_quickToolbar = stage.dialogue.GetComponent<VNQuickToolbar>();
if (_quickToolbar == null)
    _quickToolbar = stage.dialogue.gameObject.AddComponent<VNQuickToolbar>();
```
> 出处：[`VNScriptRunner.EnsureQuickToolbar`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1659](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1659)）

**按钮完全由 prefab 决定**：
```csharp
public VNToolbarActionSlot[] Slots =>
    GetComponentsInChildren<VNToolbarActionSlot>(true);
```
> 出处：[`VNQuickToolbarSkin.Slots`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs:17](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs#L17)）

每个按钮挂一个 [`VNToolbarActionSlot`](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs:8](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs#L8)`），
上面填一个 `VNToolbarAction` 枚举值（`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs:6](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs#L6)`）。
绑定时按 action 分派：
```csharp
foreach (var slot in _toolbarSkin.Slots)
{
    if (slot == null || slot.button == null) continue;
    if (slot.label != null) slot.label.text = LabelFor(slot.action);
    slot.button.onClick.RemoveAllListeners();
    slot.button.onClick.AddListener(() => Execute(slot.action));
    if (slot.action == VNToolbarAction.Auto) _autoSlot = slot;
    else if (slot.action == VNToolbarAction.Skip) _skipSlot = slot;
}
```
> 出处：[`VNQuickToolbar.BindCustomSlots`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:72](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L72)）

> **「按钮顺序和数量完全由子级 ActionSlot 决定」**
> （`VNQuickToolbarSkin` 类注释，`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs:12](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs#L12)`）
> 这是一个很干净的「数据驱动 UI」范例：
> 想加一个按钮 → 在 prefab 里复制一个按钮、改枚举值；
> 想改顺序 → 在 Hierarchy 里拖。代码一行不用动（除非要加新的 action 类型）。

**Auto/Skip 的激活态每帧刷新**：
```csharp
void Update()
{
    if (_runner == null) return;
    _autoSlot?.SetActiveState(_runner.IsAuto);
    _skipSlot?.SetActiveState(_runner.IsSkipping);
}
```
> 出处：`VNQuickToolbar.Update`（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:160](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L160)）

`SetActiveState` 只改一个 Graphic 的颜色（`VNToolbarActionSlot.SetActiveState`（[`Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs:17`](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs#L17)），
`[Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs:17](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs#L17)`），开销可忽略。

> **这里用了轮询而不是事件**。理由推测：Auto/Skip 状态会从三个来源改变
> （快捷键、工具条按钮、`SetSkip(false)` 的强制关闭），加事件要在三处都发。
> 每帧读两个 bool 比维护事件订阅便宜。**这是「轮询优于事件」的合理场景**：
> 状态极简、来源多、消费者唯一。

**停靠点可由皮肤指定**：
```csharp
/// <summary>快捷功能条停靠：皮肤 toolbarAnchor > 皮肤 panel > 对话框根（老位置）</summary>
void DockToolbar()
{
    var toolbar = GetComponent<VNQuickToolbar>();
    if (toolbar == null) return;
    RectTransform dock = null;
    if (_skin != null)
        dock = _skin.toolbarAnchor != null ? _skin.toolbarAnchor : _skin.panel;
    toolbar.SetDock(dock); // null = 挂回对话框根
}
```
> 出处：[`VNDialogueBox.DockToolbar`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs)（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:505](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L505)）

`VNQuickToolbar.SetDock`（`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:127](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L127)`）
只是换个 parent（`VNQuickToolbar.AttachRoot`，
`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:153](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L153)`）。

### 8.2 设置面板 VNConfigPanel

**定位**：音量三档 + 文字速度 + 语言三选 + 全屏切换 + 滚轮回想开关 + 教程开关/重置。

**特别之处 1：设置存 PlayerPrefs，不进存档**

六个 key 全在类顶部（`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:13](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L13)` 起）：
`BgmKey`、`SeKey`、`VoiceKey`、`TextSpeedKey`、`FullscreenKey`、`WheelBacklogKey`。

**特别之处 2：启动时就要应用一次**
```csharp
EnsureConfigPanel(); // 启动时应用 PlayerPrefs 中保存的音量、文字速度与显示模式
```
> 出处：`VNScriptRunner.Start`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:164](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L164)）

[`VNConfigPanel.Initialize`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:42](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L42)`）
调 `ApplySavedSettings`（`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:66](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L66)`），
它有 `_settingsApplied` 守卫保证只跑一次。

> **注意这个「面板还没建，设置已经生效」的分离**。
> 设置的**应用**与设置的**界面**是两件事，本专案把应用放在 `Initialize`、界面放在 `Build`。
> 玩家不打开设置面板，音量也是对的。这是很多项目会做错的地方
> （把 `LoadSettings()` 写在 `Build()` 里，结果不打开设置界面就一直是默认音量）。

**特别之处 3：静态属性给高频读取者用**
```csharp
/// <summary>
/// 滚轮上滑是否打开回想。默认开（Galgame 惯例），关掉后只剩 H 键。
/// 静态是因为 VNScriptRunner 每帧要读它，而设置面板未必存在于所有场景。
/// </summary>
public static bool WheelOpensBacklog
{
    get => PlayerPrefs.GetInt(WheelBacklogKey, 1) != 0;
    set => PlayerPrefs.SetInt(WheelBacklogKey, value ? 1 : 0);
}
```
> 出处：`VNConfigPanel.WheelOpensBacklog`（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:36](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L36)）

Runner 每帧读它（`VNScriptRunner.Update`，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2097](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2097)`）。

> **这里有一个可以改进的点**：`PlayerPrefs.GetInt` 每帧调用其实是有成本的
> （Windows 上是读注册表缓存，不算贵但也不是零）。
> 更好的做法是缓存到一个静态 bool，setter 时同步更新。
> 不过在这个规模下影响可忽略，属于「知道就好」的级别。

**Slider 的射线补丁**：
```csharp
static void BindSlider(Slider slider, TMP_Text valueText, float min, float max, float value,
    Func<float, string> format, Action<float> changed)
{
    var hitGraphic = slider.GetComponent<Graphic>();
    if (hitGraphic == null)
    {
        var hitImage = slider.gameObject.AddComponent<Image>();
        hitImage.color = Color.clear;
        hitGraphic = hitImage;
    }
    hitGraphic.raycastTarget = true;
    ...
}
```
> 出处：`VNConfigPanel.BindSlider`（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:179](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L179)）

> **这是 uGUI Slider 的一个经典陷阱**：如果 Slider 根物体上没有 Graphic，
> 点击轨道空白处不会跳转到那个值（只有拖 handle 有效）。
> 补一个 `Color.clear` 的 Image 就解决了。透明 Image 照样吃射线。

**`SetValueWithoutNotify`**：
```csharp
slider.SetValueWithoutNotify(value);
valueText.text = format(value);
slider.onValueChanged.RemoveAllListeners();
slider.onValueChanged.AddListener(...);
```
> 出处：`VNConfigPanel.BindSlider`（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:193](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L193)）

先无通知赋初值、再挂监听——顺序反了的话初始化本身会触发一次「玩家改了音量」的回调。

### 8.3 存读档面板 VNSaveLoadPanel

**定位**：20 槽网格 + 缩略图 + 时间 + 最后一句台词 + 覆盖/读取二次确认。

**特别之处 1：存档前要先抓一张缩略图**

这是整个面板最有意思的一段。流程是：

```
RequestSavePanel (:1678)
 ├─ 检查 _waitingAtSay，不是台词处就弹 Toast 拒绝
 ├─ PauseForSaveLoadMenu()          Time.timeScale = 0
 ├─ _saveLoadPanel.PrepareForSaveCapture()   面板标记为 open 但先隐藏
 └─ StartCoroutine(CaptureSaveThumbnailCo(token))
        └─ capture.CaptureThumbnailCo(320, 180, ...)   抓图
        └─ _saveLoadPanel.OpenSave(thumbnail)          抓完才真正显示
```

```csharp
public void RequestSavePanel()
{
    if (!_waitingAtSay)
    {
        VNToast.Show(VNLocale.T("runner.cannotSaveNow"));
        return;
    }
    EnsureSaveLoadPanel();
    PauseForSaveLoadMenu();
    _saveLoadPanel.PrepareForSaveCapture();

    if (_saveCaptureCo != null) StopCoroutine(_saveCaptureCo);
    int token = ++_saveCaptureToken;
    _saveCaptureCo = StartCoroutine(CaptureSaveThumbnailCo(token));
}
```
> 出处：`VNScriptRunner.RequestSavePanel`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1678](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1678)）

`PrepareForSaveCapture` 做的事很微妙：
```csharp
public void PrepareForSaveCapture()
{
    Build();
    _open = true;
    _panel.SetActive(false);
}
```
> 出处：[`VNSaveLoadPanel.PrepareForSaveCapture`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:62](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L62)）

**`_open = true` 但 `_panel` 关着**——这是为了让 Runner 的 `Update` 立刻进入
「存读档面板打开」的分支（`VNScriptRunner.Update`，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2015](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2015)`），
抓图这几帧里玩家按键不会推进剧情。

**token 机制防竞态**：
```csharp
_saveCaptureCo = null;
if (token != _saveCaptureToken || _saveLoadPanel == null || !_menuPaused)
{
    if (thumbnail != null) Destroy(thumbnail);
    yield break;
}
_saveLoadPanel.OpenSave(thumbnail);
```
> 出处：`VNScriptRunner.CaptureSaveThumbnailCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1895](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1895)）

`CancelSaveCapture`（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1924](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1924)`）
里 `_saveCaptureToken++` 就能让在飞的那个协程作废。

> **token 作废模式**（也叫 generation counter）是异步 UI 最实用的模式之一：
> 比「取消 CancellationToken」轻量、比「判断组件是否还活着」可靠。
> 只要每次发起时 `++token` 并在回调里比对，就能保证「只有最后一次发起的结果会被采纳」。
> 本专案在这里用了一次，值得推广到所有异步 UI 加载。

**特别之处 2：纹理的显式生命周期管理**

存读档面板是全专案唯一大量创建 `Texture2D` 的 UI，所以纹理管理写得很仔细：

```csharp
readonly List<Texture2D> _loadedThumbnails = new List<Texture2D>();
```
> 出处：`VNSaveLoadPanel._loadedThumbnails`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:14](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L14)）

- 建卡时读入并登记：`VNSaveLoadPanel.CreateSlotCard`
  （`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:205](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L205)`）。
- 重建列表时先全清：`VNSaveLoadPanel.RebuildSlots`
  （`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:194](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L194)`）调 `ClearLoadedThumbnails`。
- 关闭时清：`VNSaveLoadPanel.Close`（[`Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:100`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L100)）
  （`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:106](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L106)`）。
- 组件销毁时清：`VNSaveLoadPanel.OnDestroy`
  （`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:290](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L290)`）。

待存的那一张单独管理，换新的时先销毁旧的：
```csharp
void ReplacePendingThumbnail(Texture2D texture)
{
    if (_pendingThumbnail != null && _pendingThumbnail != texture)
        Destroy(_pendingThumbnail);
    _pendingThumbnail = texture;
}
```
> 出处：`VNSaveLoadPanel.ReplacePendingThumbnail`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:276](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L276)）

存完之后 Runner 也会销毁它：
```csharp
if (thumbnail != null) Destroy(thumbnail); // PNG 已落盘，纹理即可释放
```
> 出处：`VNScriptRunner.SaveTo`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1538`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1538)） 附近（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1640](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1640)）

> **`Texture2D` 是 Unity 里少数必须手动 Destroy 的东西**（不受 GC 管理，
> 只有引用它的 C# 对象被回收，原生内存不会释放）。
> 本专案的做法——「一个 List 记着所有我建的、在四个出口都清一遍」——是标准答案。
> 如果你的项目里有「打开相册界面几分钟后内存爆掉」的问题，八成就是漏了这一步。

**特别之处 3：空槽的 RawImage 用白贴图**
```csharp
card.thumbnail.texture = thumbnail != null ? thumbnail : Texture2D.whiteTexture;
card.thumbnail.color = thumbnail != null ? Color.white : card.emptyColor;
```
> 出处：`VNSaveLoadPanel.CreateCustomSlotCard`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:216](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L216)）

`RawImage` 的 texture 为 null 时会画成纯白（且忽略 color），
所以用内置的 `Texture2D.whiteTexture` 当底再染色。这是 RawImage 的常见处理。

**特别之处 4：确认框用 `SetAsLastSibling` 保证在最上**
```csharp
_confirm.SetActive(true);
_confirm.transform.SetAsLastSibling();
```
> 出处：`VNSaveLoadPanel.ShowConfirm`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:260](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L260)）

> 同一 Canvas 内的绘制顺序 = Hierarchy 顺序。
> 想让一个已存在的节点浮到最上，`SetAsLastSibling()` 是最省事的做法，
> 不需要额外的 Canvas / sortingOrder。
> 大头贴的贴纸拿起时也用了这招（`VNPhotoStickerItem.OnPointerDown`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:58`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L58)），
> `[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:61](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L61)`）。

### 8.4 回想 VNBacklog

**定位**：已读台词的滚动列表，H 键或滚轮上滑打开。

**数据结构极简**：
```csharp
struct Entry
{
    public string name;
    public string text;
}
readonly List<Entry> _entries = new List<Entry>();
```
> 出处：[`VNBacklog.Entry`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:14](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L14)）

```csharp
public void Record(string displayName, string text)
{
    _entries.Add(new Entry { name = displayName, text = text });
    if (_entries.Count > maxEntries)
        _entries.RemoveAt(0);
}
```
> 出处：`VNBacklog.Record`（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:48](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L48)）

上限 `maxEntries = 200`（`[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:12](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L12)`），
超了从头删。

> **`List.RemoveAt(0)` 是 O(n)**，200 条时每帧最多一次、每次移动 200 个 struct，
> 完全可以忽略。但如果上限调到几千，应该换成环形缓冲或 `Queue`。
> 这是「知道代价、判断够用」的合理取舍，不是错误。

**记录点有四处**，全在 Runner：
- 普通台词：`VNScriptRunner.NormalSayCo`
  （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2623](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2623)`）
- 选项：`VNScriptRunner.ChoiceCo`
  （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2897](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2897)`）
- 事件结果：`VNScriptRunner.EventCo`
  （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2962](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2962)`）
- 剧本外插话（偷拍被发现）：`VNScriptRunner.SayOutOfScript`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1833`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1833)）
  （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1837](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1837)`）

**SNS 台词不进回想**——`SnsSayCo`（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2655](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2655)`）
里没有 `_backlog?.Record`，注释说明理由：「不进回想（聊天窗本身就是历史记录）」
（`VNScriptRunner.SnsSayCo` 上方注释，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2653](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2653)`）。

**事件是否进回想由模块自己决定**：
```csharp
public virtual bool RecordInBacklog => true;
```
> 出处：[`VNEventModule.RecordInBacklog`](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:91](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L91)）

注释解释：「纯流程控制型调用（如 event plan op:next 逐格派发，一周会调 7 次）应返回 false，
否则回想里全是无意义的重复条目」
（`[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:87](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L87)`）。
Runner 在**销毁模块之前**读它：
```csharp
bool recordInBacklog = module.RecordInBacklog; // 销毁前读取
Destroy(module.gameObject);
```
> 出处：`VNScriptRunner.EventCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2955](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2955)）

> **「销毁前读取」这条注释很重要**。`Destroy` 是帧末执行的，
> 但你不能依赖这一点——一旦有人把 `Destroy` 改成 `DestroyImmediate`，
> 后面的 `module.RecordInBacklog` 就是空引用。先取值存本地变量是正确姿势。

**打开时滚到底**：
```csharp
_panel.SetActive(true);
_open = true;
Canvas.ForceUpdateCanvases();
_scroll.verticalNormalizedPosition = 0f;
```
> 出处：`VNBacklog.Open`（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:66](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L66)）

`Canvas.ForceUpdateCanvases()` 是必须的——刚 `SetActive(true)` 时 Content 的高度还没算出来，
直接设 `verticalNormalizedPosition` 会因为 viewport/content 尺寸未定而落到错误位置。

> **对比**：[`VNQuestLog.Open`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:222](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L222)`）
> 里同样调了 `Canvas.ForceUpdateCanvases()`（`:230`）然后设
> `verticalNormalizedPosition = 1f`（`:231`，从顶部开始看）。
> 回想从底部（最新的在下面），任务从顶部（可领取的置顶）——
> 这个差别是有意的设计，不是不一致。

### 8.5 任务日志 VNQuestLog

**定位**：J 键四栏面板（可领取 / 进行中 / 已完成 / 已失败）+ quest 命令执行 + 引擎驱动。

**它同时是三个角色**（类注释，`[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:8](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L8)` 起）：
① 执行剧本 quest 命令（转发给 [`VNQuestEngine`](Assets/Project/Scripts/VNEffects/Script/VNQuestEngine.cs)）；
② 驱动引擎求值；③ 面板 UI。

**「标脏 + 下一帧一次」的驱动模式**：
```csharp
void MarkDirty() => _dirty = true;

void Update()
{
    if (!_dirty) return;
    _dirty = false;
    EnsureConfigured();
    VNQuestEngine.Evaluate();
    UpdateBadge();
}
```
> 出处：`VNQuestLog.MarkDirty`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:73](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L73)）
> 与 `VNQuestLog.Update`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:80](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L80)）

订阅在 `Awake`：
```csharp
VNLocale.LanguageChanged += OnLanguageChanged;
VNFlags.Changed += MarkDirty;
VNQuestEngine.Changed += OnEngineChanged;
```
> 出处：`VNQuestLog.Awake`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:61](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L61)）

> **这个模式是本专案 UI 数据绑定的标准答案**，[`VNStatsHud`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)
> （`VNStatsHud.MarkDirty`，`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:70](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L70)`）
> 与 [`VNCalendarHud`](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs)（`VNCalendarHud.MarkDirty`，
> `[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:37](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L37)`）用的完全一样。
>
> **为什么不在事件里直接刷新**：`VNFlags.Changed`（[`Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:21`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L21)） 在读档时会**连续触发几百次**
> （逐个 Set 回去）。直接刷新等于重建几百次 UI。
> [`VNFlags.Changed`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs) 的文档注释就写着这条：「读档时会连续触发多次，
> 订阅方应做"标脏 + 下帧统一刷新"而不是立即重建」
> （`[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:20](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L20)`）。

**惰性配置的优化**：
```csharp
/// <summary>
/// 属性 HUD 可能比本组件晚一步出现，所以惰性补配置。
/// 只在真正需要时重建引擎的任务表——这个方法每次 flag 变化的下一帧都会被走到，
/// 无条件 Configure 等于每写一次 stat 就复制一遍整张任务列表。
/// </summary>
void EnsureConfigured()
{
    if (_hud == null) _hud = FindFirstObjectByType<VNStatsHud>();
    if (_configured && _configuredHud == _hud) return;
    _configured = true;
    _configuredHud = _hud;
    VNQuestEngine.Configure(quests, _hud);
}
```
> 出处：`VNQuestLog.EnsureConfigured`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:103](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L103)）

**角标只在数字变化时才动 UI**：
```csharp
void UpdateBadge()
{
    int claimable = VNQuestEngine.ClaimableCount;
    if (claimable == _badgeCount) return;
    _badgeCount = claimable;
    VNToast.SetBadge(claimable > 0 ? VNLocale.T("quest.badge", claimable) : null);
}
```
> 出处：`VNQuestLog.UpdateBadge`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:90](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L90)）

`_badgeCount` 初始 -1（`[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:37](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L37)`），
保证第一次一定会写一遍（即使是 0）。

**一个真实的 uGUI 坑：ScrollRect Content 的 sizeDelta**
```csharp
_content.anchorMin = new Vector2(0f, 1f);
_content.anchorMax = new Vector2(1f, 1f);
_content.pivot = new Vector2(0.5f, 1f);
_content.anchoredPosition = Vector2.zero;
// sizeDelta 默认 (100,100)，横向拉伸下 = 比视口宽 100px → 左右各溢出 50px
// 被 RectMask2D 裁掉（任务标题左边缺字），必须显式清零
_content.sizeDelta = Vector2.zero;
```
> 出处：`VNQuestLog.Build`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:303](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L303)）

> **这个坑非常值得记住**：`new GameObject(..., typeof(RectTransform))` 出来的
> RectTransform 默认 `sizeDelta = (100, 100)`。
> 在「双向拉伸锚点」下，sizeDelta 的含义是**相对于父级的尺寸增量**，
> 所以 100 意味着比父级宽 100px、左右各溢出 50px。
> 程序化建 UI 时，凡是设了拉伸锚点，一定要显式 `sizeDelta = Vector2.zero`。
> 本专案的 `Stretch` 辅助函数（例 `VNDialogueBox.Stretch`（[`Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:576`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L576)），
> `[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:576](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L576)`）是用 `offsetMin/offsetMax` 清零，
> 效果等价。

### 8.6 属性 HUD + 属性面板 VNStatsHud

**定位**：顶栏常驻属性条 + C 键完整属性页 + `stat` 命令 + 选项 `cost:` 判定。

它是「一个组件管两块 UI」的例子：`_hudBar`（常驻）与 `_panel`（弹出）共用一个
Canvas（`VNStatsHud.EnsureCanvas`，`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:232](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L232)`）。

**属性变动的四重反馈**（`VNStatsHud.RefreshHud`，
`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:305](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L305)`）：

1. **数值滚动**（`VNStatsHud.RollValue`，`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:369](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L369)`）：
   ```csharp
   e.rollTween = DOVirtual.Int(from, to, 0.45f, x => e.value.text = e.def.Format(x))
       .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(e.value.gameObject);
   ```
   > 出处：`VNStatsHud.RollValue`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:377](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L377)）

   注释：「20 → 23 逐格跳，比瞬间换字更容易被注意到」
   （`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:368](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L368)`）。
   差 1 时直接赋值不滚（`VNStatsHud.RollValue`，
   `[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:372](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L372)`）——滚一格没有意义。

2. **进度条补间**：`DOVirtual.Float` 驱动 `rect.anchorMax.x`
   （`VNStatsHud.RefreshHud`，`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:331](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L331)`）。

3. **数字弹跳 + 染色**：
   ```csharp
   e.value.color = tint;
   e.value.transform.localScale = Vector3.one * 1.35f;
   e.tween = DOTween.Sequence()
       .Append(e.value.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack))
       .Join(e.value.DOColor(new Color(0.97f, 0.97f, 1f, 1f), 0.6f))
       .SetUpdate(true)
       .SetLink(e.value.gameObject);
   ```
   > 出处：`VNStatsHud.RefreshHud`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:345](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L345)）

4. **图标弹跳 + `+N` 上飘**（`VNStatsHud.SpawnFloatingDelta`，
   `[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:386](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L386)`）。

**飘字挂在 Canvas 根而不是条目下**：
```csharp
/// <summary>
/// HUD 条目上方冒一个 +3 / -2 并向上飘散。
/// 挂在 Canvas 根而不是条目下面：HUD 皮肤 prefab 可能带裁剪（Mask/RectMask2D），
/// 挂在条目里会被切掉一半。
/// </summary>
void SpawnFloatingDelta(HudEntry e, int delta, Color color)
{
    ...
    var canvasRect = (RectTransform)_canvas.transform;
    Vector2 local = canvasRect.InverseTransformPoint(e.root.position);
    rect.anchoredPosition = local + new Vector2(0f, 6f);
    ...
}
```
> 出处：`VNStatsHud.SpawnFloatingDelta`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:381](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L381)）

坐标换算用 `InverseTransformPoint`——因为都在同一个 Overlay Canvas 下，
世界坐标与 Canvas 局部坐标之间是简单的线性变换，不需要
`RectTransformUtility.ScreenPointToLocalPointInRectangle`。

> **通用做法**：任何「从某个 UI 元素位置飞出来、但不该被它裁剪」的特效
> （飘字、飞行道具、连击数），都应该挂在 Canvas 根 + 换算坐标。
> 挂在源元素下面必然被 Mask 裁、被父级 alpha 影响、被父级销毁带走。

**Toast 卡片同时也弹一张**：
```csharp
Sprite icon = def != null && def.icon != null ? def.icon : null;
Color iconColor = def != null ? def.color : new Color(0.8f, 0.85f, 1f);
Color accent = delta > 0 ? GainColor : delta < 0 ? LoseColor : iconColor;
...
VNToast.Show(message, icon, iconColor, accent, 1.8f);
```
> 出处：`VNStatsHud.Apply`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:154](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L154)）

注释解释了双色的分工：「图标+主题色认属性，左侧竖条认涨跌」
（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:153](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L153)`）。

> **两个视觉通道分别承载两个信息维度**，是很扎实的信息设计。
> 玩家不需要读文字就能知道「哪个属性动了」（图标+色）与「涨还是跌」（竖条色）。

**`cost:` 判定三件套**（都是纯静态或近乎纯函数，可单测）：
- `VNStatsHud.ParseCostOp`（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:170](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L170)`）——静态解析
- `VNStatsHud.CanAfford`（[`Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:185`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L185)）（`:185`）——付不付得起
- `VNStatsHud.FormatCostLabel`（[`Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:194`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L194)）（`:194`）——右侧小字文案
- `VNStatsHud.ApplyCost`（[`Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:206`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L206)）（`:206`）——真扣

`CanAfford` 里的下限取法值得看：
```csharp
var def = Find(name);
int floor = def != null && def.useClamp ? def.minValue : 0;
return VNFlags.Get(name) + delta >= floor;
```
> 出处：`VNStatsHud.CanAfford`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:189](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L189)）

没登记定义的属性下限按 0 算——这样未登记的「道具_xx」这类 flag 也能当花费用。

### 8.7 日历 HUD VNCalendarHud

**定位**：右下角小面板，显示月份与剩余月数。全专案最小的 UI 组件（144 行）。

**它示范了「条件性 HUD」的正确做法**：
```csharp
void Refresh()
{
    bool active = _visible && VNFlags.All.ContainsKey(MonthFlag);
    if (!active)
    {
        if (_root != null) _root.SetActive(false);
        return;
    }

    Build();
    _root.SetActive(true);
    ...
}
```
> 出处：`VNCalendarHud.Refresh`（[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:63](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L63)）

**「月份」flag 不存在 = 不是养成模式 = 整个面板隐藏**
（类注释，`[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:10](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L10)`）。
而且 `Build()` 是在确认要显示之后才调的——纯剧情章节里这个组件从头到尾不建任何 UI。

> **「用数据的存在与否决定 UI 的存在与否」** 比「用一个 bool 开关」优雅得多。
> 剧本作者写 `time set 9` 时不需要另外写一句「显示日历」，
> 而 `VNFlags.All.ContainsKey` 天然区分了「值为 0」与「没有这个 key」。
> 这也是为什么 `VNFlags.All`（[`Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:29`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L29)） 要暴露成 `IReadOnlyDictionary`
> （`[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:29](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L29)`）而不只是 `Get(key)`。

**排序号 578 的注释**：
```csharp
_canvas.sortingOrder = 578; // 与属性 HUD(580) 同档，低于各面板(600)
```
> 出处：`VNCalendarHud.Build`（[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:93](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L93)）

**位置避开对话框右下角**：
```csharp
rect.anchoredPosition = new Vector2(-24f, 88f); // 避开对话框右下角
```
> 出处：`VNCalendarHud.Build`（[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:104](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L104)）

### 8.8 背包 VNInventory

**定位**：I 键，左道具一览 + 右 7 格装备栏 + 介绍区，右键菜单。

**特别之处：它给装备系统装了一个查询回调**
```csharp
void Awake()
{
    var cfg = VNGameConfig.Active;
    if (cfg != null) VNGameConfig.ApplyList(cfg.shops, ref shops);

    VNEquipment.ItemResolver = FindItem; // 装备系统查道具走同一张目录
    VNLocale.LanguageChanged += OnLanguageChanged;
}
```
> 出处：[`VNInventory.Awake`](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInventory.cs:34](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs#L34)）

退订时还检查了「现在挂着的是不是我」：
```csharp
if (VNEquipment.ItemResolver != null &&
    ReferenceEquals(VNEquipment.ItemResolver.Target, this))
    VNEquipment.ItemResolver = null;
```
> 出处：`VNInventory.OnDestroy`（[Assets/Project/Scripts/VNEffects/Script/VNInventory.cs:47](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs#L47)）

> **这是「静态委托 + 实例方法」的正确退订姿势**。
> 直接 `VNEquipment.ItemResolver = null` 会误伤后来注册的另一个实例
> （比如场景切换时新旧实例并存的那一帧）。
> `ReferenceEquals(delegate.Target, this)` 检查委托的宿主是不是自己，才安全。
> 同一思路也用在 `VNTutorialAnchors.Unregister(id, rect)`
> （`[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:40](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L40)`）——
> 「传了 rect 时只在当前登记的就是它时才删」（注释在 `:36`）。

**道具目录跨全部商店查找**：
```csharp
public VNShopDef.Item FindItem(string id)
{
    if (string.IsNullOrEmpty(id)) return null;
    foreach (var shop in shops)
    {
        if (shop == null) continue;
        var item = shop.FindItem(id);
        if (item != null) return item;
    }
    return null;
}
```
> 出处：`VNInventory.FindItem`（[Assets/Project/Scripts/VNEffects/Script/VNInventory.cs:64](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs#L64)）

> **「道具的定义住在商店资产里」是一个有意思的取舍**。
> 优点：不用再建一个道具库资产，商品与道具天然同源。
> 缺点：不卖的道具（任务奖励、捡到的）没地方登记，只能显示 id。
> 类注释承认了这一点：「未登记的道具用 id 照常显示」
> （`[Assets/Project/Scripts/VNEffects/Script/VNInventory.cs:13](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs#L13)`）。
> 如果道具种类继续增长，独立出 `VNItemDef` 会更清晰。

### 8.9 CG 画廊 VNCgGallery

**定位**：G 键，三个标签页（CG / 照片 / 私密），网格 + 全屏浏览 + 翻差分。

**三页共存的设计**：
```csharp
enum Page { Cg, Photo, Secret }
Page _page = Page.Cg;
```
> 出处：[`VNCgGallery.Page`](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs)（[Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs:41](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs#L41)）

照片页与私密页的数据来自两个**完全独立的相册**，
但用一个 struct 抹平差异：
```csharp
/// <summary>照片页 / 私密页共用的一条：两个相册的条目类型不同，这里抹平成文件名 + 说明</summary>
struct PhotoItem
{
    public string file;
    public string caption;
}
```
> 出处：`VNCgGallery.PhotoItem`（[Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs:45](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs#L45)）

**私密页的显示条件每次打开重算**：
```csharp
// 私密页的出现条件每次打开重算（解锁 flag 会变）；正停在私密页却不该看见时退回 CG 页
bool secretVisible = VNSecretPhotoMode.AlbumVisible;
if (_secretTab != null) _secretTab.gameObject.SetActive(secretVisible);
if (_page == Page.Secret && !secretVisible) _page = Page.Cg;
```
> 出处：`VNCgGallery.Open`（[Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs:97](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs#L97)）

> **「状态可能在面板关着的时候变化」是所有惰性面板的通病**。
> 本专案的处理是：把所有依赖外部状态的判断放在 `Open()` 里重算，
> 而不是在 `Build()` 里做一次。这条规则可以直接套用到任何面板。

**关闭时释放两个相册的纹理缓存**：
```csharp
// 相册的纹理与缩略图都在这里放掉——关了界面就没有必要再占内存
VNPhotoAlbum.ClearCache();
VNSecretAlbum.ClearCache();
```
> 出处：`VNCgGallery.Close`（[Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs:116](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs#L116)）

**全屏浏览是一个独立的子状态**：
```csharp
public bool IsViewerOpen => _viewer != null && _viewer.activeSelf;
```
> 出处：`VNCgGallery.IsViewerOpen`（[Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs:60](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs#L60)）

Runner 的输入处理里为它单独开了一个分支（第九章会讲）。

### 8.10 标题菜单 VNTitleMenu

**定位**：同场景覆盖层（排序 500），开始 / 继续 / 读档 / 鉴赏 / 设置 / 退出。

**特别之处 1：它接管启动流程**
```csharp
_titleMenu = FindFirstObjectByType<VNTitleMenu>();
if (_titleMenu != null && _titleMenu.showOnStart)
{
    // 标题菜单接管启动：跳过 playOnStart，由「开始/继续」按钮进入播放。
    // 编辑器"从选中行播放"不受影响——它走 ResumeAt，标题层会被自动收起。
    _titleMenu.Initialize(this, stage);
    _titleMenu.Open();
}
else if (playOnStart && script != null)
{
    Play(script);
}
```
> 出处：`VNScriptRunner.Start`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:172](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L172)）

而 `ResumeAt` 会顺手收起标题层：
```csharp
_titleMenu?.NotifyGameplayStarted(); // 任何入口开始播放都顺手收起标题层
```
> 出处：`VNScriptRunner.ResumeAt`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1054](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1054)`）

> **这个「任何入口都收标题」的兜底很关键**。
> 标题菜单可以被三个东西绕过：编辑器从选中行播放、读档、`StartNewGame`。
> 全部走 `Play` → `ResumeAt`（`VNScriptRunner.ResumeAt`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1042`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1042)），
> `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1042](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1042)`），
> 于是在这一个点收口就够了。**找到「所有路径的必经之处」再收口**，
> 比在三个入口各写一遍可靠。

**特别之处 2：它临时接管舞台**
```csharp
void ApplyTitleStage()
{
    var config = VNGameConfig.Active;
    if (!_stageApplied)
    {
        _stageApplied = true;
        if (_stage != null)
        {
            string backgroundId = ...;
            if (!string.IsNullOrEmpty(backgroundId))
                _stage.SetBackground(backgroundId, null);
            if (config != null && !string.IsNullOrEmpty(config.titleBgm))
                _stage.vnAudio?.PlayBgm(config.titleBgm);
        }
    }

    if (_stage != null && _stage.dialogue != null)
        _stage.dialogue.SetInterfaceVisible(false);
    if (_statsHud == null) _statsHud = FindFirstObjectByType<VNStatsHud>();
    _statsHud?.SetHudVisible(false);
    if (_hintText != null) return;
    _hintText = GameObject.Find("HintText");
    if (_hintText != null) _hintText.SetActive(false);
}
```
> 出处：[`VNTitleMenu.ApplyTitleStage`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:82](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L82)）

配对的还原在 `VNTitleMenu.RestoreSceneUi`
（`[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:111](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L111)`）。

注意 `_stageApplied` 只守住「背景与 BGM」这一段——那是一次性的；
而隐藏对话框/HUD 是每次 Open 都要做的。这个分界拆得很准。

> **可以改进的一点**：`GameObject.Find("HintText")`
> （`VNTitleMenu.ApplyTitleStage`，`[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:107](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L107)`）
> 是按名字找物体，正是 [`VNTutorialAnchors`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs) 类注释里批评的那种做法
> （`[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:9](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L9)`）。
> HintText 是演示场景的提示文字，改名或删掉就静默失效（好在后果只是提示文字没藏起来）。
> 更一致的做法是让 [`VNStage`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs) 持有它，或者干脆把它也纳入 [`VNUiParts`](Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs)。

**特别之处 3：「继续」按钮找最新档**
```csharp
static int FindLatestSlot()
{
    int latestSlot = -1;
    DateTime latestTime = DateTime.MinValue;
    for (int slot = 0; slot <= 20; slot++)
    {
        var data = VNSaveSystem.Peek(slot);
        if (data == null) continue;
        if (!DateTime.TryParse(data.savedAt, out var savedAt))
            savedAt = DateTime.MinValue;
        if (latestSlot >= 0 && savedAt <= latestTime) continue;
        latestSlot = slot;
        latestTime = savedAt;
    }
    return latestSlot;
}
```
> 出处：`VNTitleMenu.FindLatestSlot`（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:144](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L144)）

从 slot 0 开始扫是因为 0 号是快存槽（`VNScriptRunner.QuickSaveSlot`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1599`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1599)），
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1599](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1599)`），
「继续」应该包含快存。

**特别之处 4：所有 Tween 都 `SetUpdate(true)`**
```csharp
_group.DOFade(1f, 0.9f).SetEase(Ease.OutQuad)
    .SetUpdate(true).SetLink(_canvasGo);
```
> 出处：`VNTitleMenu.PlayEntrance`（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:276](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L276)）

因为读档/设置面板会把 `Time.timeScale` 归零
（`VNScriptRunner.PauseForSaveLoadMenu`，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1909](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1909)`），
标题层的动画不该跟着停。

### 8.11 日记本 VNAiDiaryPanel

**定位**：D 键，主角写的日记（AI 聊天产出），跨存档累积。

**它与回想的分工写在类注释里**
（`[Assets/Project/Scripts/VNEffects/Script/VNAiDiaryPanel.cs:16](Assets/Project/Scripts/VNEffects/Script/VNAiDiaryPanel.cs#L16)` 起）：

> 【和回想（Backlog）的区别】
> 回想是「这一场对话的原文」，日记是「这段关系的记录」——
> 后者跨存档累积、有主角的主观视角，是给玩家看的收藏品而不是查询工具。

**按需创建**（唯一一个不在 `Start` 里创建的系统面板）：
```csharp
public void RequestDiary()
{
    if (_eventActive) return;
    if (_diaryPanel == null)
    {
        _diaryPanel = FindFirstObjectByType<VNAiDiaryPanel>();
        if (_diaryPanel == null)   // 没人手工摆也能用，同任务日志
            _diaryPanel = new GameObject("VNAiDiaryPanel")
                .AddComponent<VNAiDiaryPanel>();
    }
    _diaryPanel.Toggle();
}
```
> 出处：`VNScriptRunner.RequestDiary`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1716](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1716)）

Runner 的 Update 里 D 键也走这个 Request 而不是直接 `_diaryPanel?.Open()`：
```csharp
if (kb.dKey.wasPressedThisFrame)
{
    RequestDiary();   // 面板按需创建，所以走 Request 而不是直接 Open
    return;
}
```
> 出处：`VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2110](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2110)）

对比其它面板都是 `_backlog?.Open()`、`_questLog?.Open()` 这样直接调
（`VNScriptRunner.Update`，`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2100](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2100)`、`:2106`）。

> **这个不一致是有原因的**（日记本是后加的、且不是每个游戏都需要），
> 但它埋了一个小坑：如果有人照着别的面板的写法加新面板，
> 很容易在 Update 里写 `_newPanel?.Open()` 而忘了先创建。
> **建议**：把所有面板的按键处理都统一走 `Request*`，
> Request 内部负责「按需创建 + 事件中不开 + Toggle」。这样新面板照抄一定对。

---

## 九、输入、焦点、返回／取消：Runner 的模态栈

这一章讲整个专案最集中、也最容易被忽略的一段代码：[`VNScriptRunner.Update`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)
（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1974](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1974)`）。
它不到 200 行，但把「谁现在能吃输入」这件事完整地表达了出来。

### 9.1 没有 UI 焦点系统，只有一条 early-return 链

本专案**没有**焦点栈、没有 `UIManager.PushModal()`、没有 InputActionMap 切换。
所有模态优先级就是 `Update` 里一串 `if (...) return;` 的**书写顺序**。

完整的优先级链（从高到低）：

| 顺序 | 条件 | 行为 | 出处 |
|---|---|---|---|
| 1 | `_eventActive` | 直接 return，输入全归模块 | `VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1980](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1980)） |
| 2 | [`VNPause.IsPaused`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) | 直接 return，教程讲解中 | `:1985` |
| 3 | 偷拍模式打开 | 直接 return | `:1989` |
| 4 | SNS 等玩家挑回复 | 直接 return | `:1993` |
| 5 | UI 已隐藏且未锁定 | 任意键只恢复界面，不推进 | `:1998` |
| 6 | 设置面板打开 | 只响应 Esc | `:2008` |
| 7 | 存读档面板打开 | 只响应 Esc / F5 / F9 | `:2015` |
| 8 | 回想打开 | 只响应 H / Esc | `:2024` |
| 9 | 任务日志打开 | 只响应 J / Esc | `:2032` |
| 10 | 日记本打开 | 只响应 D / Esc | `:2040` |
| 11 | 属性面板打开 | 只响应 C / Esc | `:2048` |
| 12 | 背包打开 | 只响应 I / Esc | `:2056` |
| 13 | CG 画廊打开 | 两层：全屏浏览时 ←→ 翻页、Esc/G 退回网格；网格时 G/Esc 关闭 | `:2065` |
| 14 | 标题菜单打开 | 直接 return（按钮走 EventSystem） | `:2087` |
| 15 | （SNS 未开时）H / 滚轮上滑 | 开回想 | `:2096` |
| 16 | （同上）J / D / C / I / G / 右键 | 各自面板 / 隐藏 UI | `:2104` 起 |
| 17 | F5 / F9 / Q / L | 存读档 | `:2141` |
| 18 | （SNS 未开时）A / S | Auto / Skip | `:2145` |
| 19 | `!_running` | return | `:2148` |
| 20 | Enter / Space / 左键 | 推进台词 | `:2157` |

**这条链的每一行注释都值得读**。举几个例子：

第 2 条（教程暂停）的注释解释了为什么要有它：
```csharp
// 教程讲解中（VNPause）：全局快捷键一律屏蔽。
// 不加这一条的话，剧情层弹出的教程盖着屏幕，F5 存档 / H 回想 /
// A / S / I / C / G / J 还全都能按 —— 存出来的档还会卡在教程半截。
if (VNPause.IsPaused) return;
```
> 出处：`VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1982](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1982)）

第 3 条：
```csharp
// 偷拍模式打开期间：输入全部归它（ESC / 空格 / 滚轮 / 拖动），
// 这里直接 return 也就顺带挡掉了 F5/F9/Q/L 与推进
if (_secretPhoto != null && _secretPhoto.IsOpen) return;
```
> 出处：`VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1987](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1987)）

第 5 条（隐藏 UI）的两态处理：
```csharp
// 隐藏 UI 后，第一次操作只恢复界面，不会顺便推进台词。
// 但 hideHUD keep 的锁定隐藏不吃这一条——那正是「点了也一直藏着」的意思，
// 输入照常往下走（台词继续推进，只是看不见对话框）。
if (_uiHidden && !_uiHideLocked)
{
    bool restore = kb.uKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                   kb.spaceKey.wasPressedThisFrame ||
                   (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                      mouse.rightButton.wasPressedThisFrame));
    if (restore) SetInterfaceHidden(false);
    return;
}
```
> 出处：`VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1995](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1995)）

> **「隐藏 UI 后第一次点击只恢复界面」是 Galgame 的行业惯例**，
> 因为玩家藏 UI 是为了看图，恢复时不该顺手把这一句跳过去。
> 而 `hideHUD keep` 是给「刻意长时间无 UI 演出」用的，此时点击应该正常推进。
> 一个 bool 区分两种语义，很干净。

**SNS 打开时屏蔽哪些键的取舍**：
```csharp
// SNS 打开期间屏蔽会打架的快捷键：
// 滚轮要留给聊天记录滚动、聊天消息不进回想、隐藏 UI 也没有意义。
// 存读档（F5/F9/Q/L）照常可用——气泡停顿处就是合法存档点。
if (!snsOpen) { /* H / J / D / C / I / G / 右键 */ }
if (kb.f5Key.wasPressedThisFrame) { RequestSavePanel(); return; }
```
> 出处：`VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2089](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2089)`）

> **这段的设计思路值得学**：不是「打开面板就全屏蔽」，
> 而是**逐个键问「它和当前界面打架吗」**。
> 滚轮打架（要滚聊天记录）→ 屏蔽；F5 不打架（气泡处也是合法存档点）→ 放行。
> 这种细致的取舍是玩家能感觉到但说不出来的「顺手」。

### 9.2 每个面板自己的 Esc 分支：为什么不做成通用的

看起来这十个分支可以合并成「有面板开着 → Esc 关它」，但代码里刻意写了十次。原因：

1. **每个面板的关闭键不同**：回想是 H、任务是 J、背包是 I、画廊是 G——
   都是「打开它的那个键」再按一次关闭。这是 Galgame 惯例。
2. **CG 画廊有两层**（网格 / 全屏浏览），Esc 的含义不同：
   ```csharp
   if (_cgGallery.IsViewerOpen)
   {
       if (kb.escapeKey.wasPressedThisFrame || kb.gKey.wasPressedThisFrame)
           _cgGallery.CloseViewer();
       else if (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
           _cgGallery.ViewerNext();
       else if (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
           _cgGallery.ViewerPrev();
   }
   else if (kb.gKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
   {
       _cgGallery.Close();
   }
   ```
   > 出处：`VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2067](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2067)）
3. **存读档面板的 Esc 之外还有 F5/F9**（在面板里也能切页签）。

> **评价**：十个 near-duplicate 的分支在可读性上其实是**优点**——
> 你能一眼看出「现在开着 X 面板时，哪些键有用」，不需要去追一个通用规则加一堆特例。
> 缺点是加新面板要记得来这里补一段（而且要补在正确的位置）。
>
> **如果要重构**，我会引入一个极简接口：
> ```csharp
> interface IVNModalPanel { bool IsOpen { get; } bool HandleKey(Keyboard kb); }
> ```
> Runner 遍历一个 `List<IVNModalPanel>`，第一个 `IsOpen` 的负责处理并 return。
> 这样既保留「每个面板自己决定键位」，又把「加新面板要改 Runner」降到只加一行注册。
> 但收益不算大，属于可做可不做。

### 9.3 推进台词：为什么不能用 IsPointerOverGameObject

这是全专案最有教学价值的一段之一：

```csharp
// 左键推进：整个画面都是 uGUI（背景/立绘/对话框都是 Canvas 里的 Image），
// IsPointerOverGameObject() 恒为 true 会把点击全部拦掉；
// 只有点在可交互控件（按钮/滑条等 Selectable）上才不推进。
// 点击喷水模式（liquid click on）期间左键归喷水，不推进台词。
// Enter/Space 一定要留着：玩家没有别的出路时会被卡死在这一句里。
bool liquidClick = stage != null && stage.liquidSplash != null &&
                   stage.liquidSplash.clickMode;
bool pressed = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame ||
               (!liquidClick && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                !IsPointerOverInteractiveUi(mouse));
if (!pressed) return;
```
> 出处：`VNScriptRunner.Update`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2150](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2150)）

替代方案：
```csharp
/// <summary>
/// 指针是否落在可交互控件上（Selectable：按钮/滑条/输入框等）。
/// 用射线命中链向上找 Selectable，而不是 IsPointerOverGameObject ——
/// 后者对任何 raycastTarget 都为 true，本项目全屏皆 UI，会拦掉一切点击。
/// </summary>
static bool IsPointerOverInteractiveUi(Mouse mouse)
{
    if (EventSystem.current == null) return false;
    var data = new PointerEventData(EventSystem.current)
    {
        position = mouse.position.ReadValue(),
    };
    _pointerRaycastResults.Clear();
    EventSystem.current.RaycastAll(data, _pointerRaycastResults);
    foreach (var hit in _pointerRaycastResults)
        if (hit.gameObject.GetComponentInParent<Selectable>() != null)
            return true;
    return false;
}
```
> 出处：`VNScriptRunner.IsPointerOverInteractiveUi`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2202](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2202)）

结果列表是静态复用的（`VNScriptRunner._pointerRaycastResults`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2172`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2172)），
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2172](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2172)`），避免每帧 GC。

> **这是「全屏 UI」类游戏（VN / 卡牌 / 战棋）必踩的坑**。
> `EventSystem.IsPointerOverGameObject()` 的语义是「指针下有没有吃射线的 UI」，
> 在背景本身就是 `Image` 的项目里恒为 true。
> 正确做法就是本专案这样：`RaycastAll` 后**按类型过滤**——
> 只有 `Selectable`（Button / Slider / Toggle / InputField）才算「可交互」。
>
> **可以进一步优化的点**：`new PointerEventData` 每次点击都分配一个对象。
> 点击是低频事件（一秒最多几次），影响可忽略；
> 但如果改成每帧调用（比如做 hover 提示），就应该把 `PointerEventData` 也缓存起来。

### 9.4 事件模块进行中的输入转发

Update 第一行 `if (_eventActive) return;` 意味着模块进行时 Runner 完全不管输入。
但模块内部可能用 `RunInlineCo` 播一句**阻塞型台词**——那句台词会死等 `_advance`，
而 `_advance` 只有 Runner 的 Update 能设。死锁。

解法是给模块开一个转发入口：
```csharp
/// <summary>
/// 外部（事件模块）请求推进当前正在等待的台词。
///
/// **为什么必须有这个入口**：Update 第一行就是 `if (_eventActive) return;`
/// —— 事件模块进行中，输入全部交给模块。于是模块内部用 RunInlineCo 播的
/// 阻塞台词会死等 `_advance`，玩家点破屏幕也过不去。模块在阻塞期间
/// 自己检测推进输入，转发到这里。
///
/// 语义与 Update 里的手动推进一致：还在打字就先补完，打完了才真推进。
/// </summary>
public void RequestAdvance()
{
    if (stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
    {
        stage.dialogue.CompleteTyping();
        return;
    }
    if (_waitingAtSay) _advance = true;
}
```
> 出处：`VNScriptRunner.RequestAdvance`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2189](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2189)）

配套暴露一个只读状态给模块判断要不要转发：
```csharp
public bool IsWaitingAtSay => _waitingAtSay;
```
> 出处：`VNScriptRunner.IsWaitingAtSay`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2200](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2200)）

> **这是「全局输入拦截」架构的必然代价**：
> 一旦你写了 `if (someoneElseHasFocus) return;`，
> 就必须给那个 someone 一个把输入还回来的通道。
> 本专案把这个通道做成了**语义明确的方法**（`RequestAdvance`）
> 而不是「让模块直接设 `_advance = true`」，这样打字机补完的逻辑不会被绕过。

### 9.5 「仅台词处可存档」：一个状态位管住整条链

```csharp
bool _waitingAtSay;   // 只有停在台词上时才允许存档
```
> 出处：`VNScriptRunner._waitingAtSay`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:83](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L83)）

设置点在 `NormalSayCo`：
```csharp
_waitingAtSay = true;
_advance = false;
float doneTime = Time.time;
float autoWait = autoDelay + sayText.Length * 0.045f;
while (!_advance)
{
    if (_backlog == null || !_backlog.IsOpen)
    {
        if (_skip && Time.time - doneTime > 0.07f) break;
        if (_auto && Time.time - doneTime > autoWait) break;
    }
    yield return null;
}
_waitingAtSay = false;
_advance = false;
```
> 出处：`VNScriptRunner.NormalSayCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2633](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2633)）

三个观察：

1. **Auto 的等待时间按字数加权**：`autoDelay + sayText.Length * 0.045f`
   （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2636](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2636)`）。
   长句子多等一会儿，符合阅读速度。
2. **回想面板开着时 Auto / Skip 暂停**：`if (_backlog == null || !_backlog.IsOpen)`
   （`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2639](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2639)`）——
   玩家在翻回想，剧情不该自己往前跑。
3. **Skip 也要等 0.07 秒**（`:2641`）——完全不等的话一帧过一句，
   玩家看到的是纯粹的闪屏，且 DOTween 的演出会全部被截断。

`_waitingAtSay` 的消费者有三处：
- `RequestSavePanel`（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1680](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1680)`）——不是台词处就拒绝并 Toast
- `CanOpenSecretPhoto`（`:1817`）——偷拍也只能在台词处进
- `RequestAdvance`（`:2196`）——模块转发时也要检查

> **「一个状态位表达一个语义，多处消费」比「每处各自判断」健壮得多**。
> 如果 SNS、事件、教程各自去猜「现在能不能存档」，一定会有一处漏掉。

### 9.6 Auto / Skip 的实现

```csharp
public void SetAuto(bool on)
{
    _auto = on;
    if (on) SetSkip(false);
    UpdateModeLabel();
    VNToast.Show(VNLocale.T(on ? "runner.autoOn" : "runner.autoOff"));
}

public void SetSkip(bool on)
{
    if (_skip == on) return;
    _skip = on;
    if (on) _auto = false;
    DOTween.timeScale = on ? skipTimeScale : 1f;
    UpdateModeLabel();
    VNToast.Show(VNLocale.T(on ? "runner.skipOn" : "runner.skipOff"));
}
```
> 出处：`VNScriptRunner.SetAuto`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1942](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1942)）
> 与 `VNScriptRunner.SetSkip`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1950](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1950)）

**Skip 靠 `DOTween.timeScale` 全局加速演出**（`:1955`）。
这就是为什么事件模块的三铁律②要求「计时用 unscaled、Tween 用 `SetUpdate(true)`」——
小游戏不该被快进加速。

模式标签：
```csharp
void UpdateModeLabel() =>
    VNToast.SetMode(_skip ? "SKIP ▶▶" : _auto ? "AUTO ▶" : null);
```
> 出处：`VNScriptRunner.UpdateModeLabel`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1960](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1960)）

**销毁时要还原全局状态**：
```csharp
void OnDestroy()
{
    VNLocale.LanguageChanged -= OnLocaleChanged;
    if (_skip) DOTween.timeScale = 1f; // 别把加速留给别的场景
    if (_menuPaused) Time.timeScale = _timeScaleBeforeMenu;
}
```
> 出处：`VNScriptRunner.OnDestroy`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1963](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1963)）

> **凡是改了全局状态（`Time.timeScale`、`DOTween.timeScale`、`Cursor.visible`）
> 的组件，`OnDestroy` 必须还原**。
> 这条在本专案里出现了三次：这里、`VNTouchCursor.Dispose`（[`Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:217`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L217)）
> （`[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:217](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L217)`）、
> [`VNTutorialPlayer.Close`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:236](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L236)`）。

### 9.7 菜单暂停：Time.timeScale = 0

```csharp
void PauseForSaveLoadMenu()
{
    if (_menuPaused) return;
    _menuPaused = true;
    _timeScaleBeforeMenu = Time.timeScale;
    Time.timeScale = 0f;
    if (_auto) SetAuto(false);
    if (_skip) SetSkip(false);
}

public void OnSaveLoadPanelClosed()
{
    CancelSaveCapture();
    if (!_menuPaused) return;
    Time.timeScale = _timeScaleBeforeMenu;
    _menuPaused = false;
}

public void OnConfigPanelClosed() => OnSaveLoadPanelClosed();
```
> 出处：`VNScriptRunner.PauseForSaveLoadMenu`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1904](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1904)）

面板关闭时回调 Runner：`VNSaveLoadPanel.Close`（[`Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:100`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L100)） 里的 `_runner?.OnSaveLoadPanelClosed()`
（`[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:108](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L108)`）、
`VNConfigPanel.Close`（[`Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:57`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L57)） 里的 `_runner?.OnConfigPanelClosed()`
（`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:63](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L63)`）。

> **注意「记住原值再归零」而不是「结束时设成 1」**。
> 如果有别的系统也在改 timeScale（比如子弹时间），直接设 1 会破坏它。
> 记原值恢复是可组合的。
>
> **但也要注意**：这里只是「记一层」，嵌套两次仍然会出问题（第二次记到的是 0）。
> `if (_menuPaused) return;` 那一行守卫住了这一点。
> 更彻底的解法就是第十章的 `VNPause` 句柄机制。
> 本专案存读档面板没改用 `VNPause`，因为它要冻住的是**演出**（timeScale 管得住），
> 而 `VNPause` 要冻的是**用 unscaled 计时的小游戏**——两者目标不同。

### 9.8 隐藏界面：按部件的位掩码

```csharp
[Flags]
public enum VNUiParts
{
    None = 0,
    Dialogue = 1 << 0,   // 对话框 + 右下快捷功能条
    Stats = 1 << 1,      // 顶部属性 HUD（金钱/行动力/…）
    Calendar = 1 << 2,   // 右下日历 HUD
    All = Dialogue | Stats | Calendar,
}
```
> 出处：[`VNUiParts`](Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs)（[Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs:13](Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs#L13)）

**为什么对话框与工具条绑成一项**（注释在 `VNUiParts` 上方，
`[Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs:9](Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs#L9)`）：

> Dialogue 刻意把对话框本体与右下快捷功能条绑成一项——玩家心里它们是同一块
> 「对话 UI」，分开关会出现「台词没了但存档按钮还浮在半空」这种半吊子画面。

**剧本 token 与存档字符串共用一张表**：
```csharp
static readonly Dictionary<string, VNUiParts> Names =
    new Dictionary<string, VNUiParts>(StringComparer.OrdinalIgnoreCase)
    {
        { "dialogue", VNUiParts.Dialogue }, { "对话框", VNUiParts.Dialogue },
        ...
    };
```
> 出处：`VNUiPartsUtil.Names`（[Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs:29](Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs#L29)）

三个函数：`Parse`（`:41`）、`ToToken`（`:48`）、`FromToken`（`:59`）。
`ToToken` 的注释：「存名字不存位，方便肉眼查存档」
（`[Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs:47](Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs#L47)`）。

> **「剧本能写的名字」与「存档里存的名字」用同一张表**，
> 是防止两者分家的最简单办法。
> 而「存名字不存位」牺牲了几个字节，换来存档文件可读——这个取舍在单机游戏里几乎总是对的。

**应用状态**：
```csharp
void ApplyUiHidden()
{
    // 偷拍模式期间整套界面临时藏起来，但**不写进 _uiHiddenParts**：
    // 退出时按剧本/玩家原本的隐藏状态还原（hideHUD keep 的段落不能被它弹回 UI）
    if (stage != null && stage.dialogue != null)
        stage.dialogue.SetInterfaceVisible(
            !_secretPhotoActive && (_uiHiddenParts & VNUiParts.Dialogue) == 0);
    ApplyGameplayHudVisible(!_eventActive && !_secretPhotoActive);
}
```
> 出处：`VNScriptRunner.ApplyUiHidden`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1782](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1782)）

```csharp
void ApplyGameplayHudVisible(bool allowed)
{
    _statsHud?.SetHudVisible(allowed && (_uiHiddenParts & VNUiParts.Stats) == 0);
    _calendarHud?.SetVisible(allowed && (_uiHiddenParts & VNUiParts.Calendar) == 0);
}
```
> 出处：`VNScriptRunner.ApplyGameplayHudVisible`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1845](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1845)）

> **这里是「三个隐藏来源的合成」**：
> ① 剧本/玩家的 `_uiHiddenParts`（持久，可能进存档）；
> ② 事件模块进行中（瞬态，`_eventActive`）；
> ③ 偷拍模式（瞬态，`_secretPhotoActive`）。
> 关键设计是 ②③ **不写进 ①**，退出时按 ① 还原。
> 如果 ②③ 直接改 ①，就会出现「小游戏结束后 hideHUD keep 的段落被弹回 UI」这种 bug。
>
> **这是「瞬态覆盖 vs 持久状态」的经典分离**，任何有多个来源控制同一个可见性的
> 系统都应该这么做：持久状态一个变量、瞬态覆盖各自一个 bool、
> 最终可见性 = 所有条件的合成函数。**永远不要让瞬态去写持久状态。**

**只有锁定隐藏进存档**：
```csharp
void RestoreUiHidden(VNSaveData data)
{
    // 存档里只会有锁定的隐藏（非锁定的一碰就还原，是瞬态不存），
    // 所以取回来非空就一定是锁定态。
    _uiHiddenParts = VNUiPartsUtil.FromToken(data.uiHidden);
    _uiHideLocked = _uiHiddenParts != VNUiParts.None;
    ApplyUiHidden();
}
```
> 出处：`VNScriptRunner.RestoreUiHidden`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1876](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1876)）

存档字段的注释也写明了同一件事：
```csharp
// 锁定隐藏中的界面部件（hideHUD keep），如 "dialogue,stats"。
// 普通隐藏（右键 / 光一行 hideHUD）玩家一碰就还原，是瞬态**不存**——
// 存了会变成「读档后界面莫名其妙全没了」。空串 = 界面全开（旧存档缺省即此）。
public string uiHidden = "";
```
> 出处：[`VNSaveData.uiHidden`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:64](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L64)）

---

## 十、全局暂停：VNPause 与 VNTime

### 10.1 问题：Time.timeScale 对本专案的小游戏无效

这一段的因果链很值得完整读一遍（[`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) 类注释，
`[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:9](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L9)` 起）：

> 【为什么不能用 Time.timeScale = 0】
> 事件模块三铁律②规定所有模块用 `Time.unscaledDeltaTime` 计时（为了不受
> Skip 快进的 DOTween.timeScale 影响），所以 timeScale 归零对羽毛球、问答、
> 拍照、互动一律无效——球照飞、倒计时照跑。真正能冻住它们的只有
> 「模块自己早退 + dt 归零」这一条路，于是有了这个类和 VNTime。

**这是一个「先前的正确决定导致后来的新问题」的典型案例**：
为了不被 Skip 加速，模块用了 unscaled 计时；
结果需要真正暂停时（教程弹窗），标准的 timeScale 手段就失效了。

### 10.2 句柄式而不是计数式

```csharp
/// <summary>一次暂停的持有凭证。宿主（owner）被销毁后自动失效。</summary>
public class Handle
{
    internal Object owner;      // 可为 null（无宿主的纯代码持有）
    internal bool ownerBound;   // 有没有指定宿主
    internal string reason;
    internal bool released;

    internal bool Alive => !released && (!ownerBound || owner != null);
}
```
> 出处：`VNPause.Handle`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:28](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L28)）

理由写在类注释里（`[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:15](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L15)` 起）：

> 【为什么是句柄式而不是裸的 Push/Pop 计数】
> 释放路径不止一条：正常结束 / ESC 跳过 / CancelForDebug / 宿主被 Destroy /
> 场景切换。裸计数漏掉任何一条，症状是**游戏永久卡死**（比 VNTouchCursor
> 漏 Dispose 导致光标消失还严重）。句柄挂在一个 GameObject 上，宿主没了
> 句柄自动失效，IsPaused 每次读都会顺手清掉这类死句柄。

读的时候顺手清死句柄：
```csharp
public static bool IsPaused
{
    get
    {
        Prune();
        return _handles.Count > 0;
    }
}
```
> 出处：`VNPause.IsPaused`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:44](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L44)）

```csharp
static void Prune()
{
    for (int i = _handles.Count - 1; i >= 0; i--)
        if (!_handles[i].Alive) _handles.RemoveAt(i);
}
```
> 出处：`VNPause.Prune`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:107](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L107)）

`Alive` 里的 `owner != null` 依赖 **Unity 的伪 null**——
已销毁的 UnityEngine.Object 与 null 比较为真。
这个平时被吐槽的特性在这里正好是需要的行为。

**三重保险**：
1. 正常 `Release(ref handle)`（`[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:85](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L85)`）
2. 宿主销毁 → `Prune` 自动清
3. [`ReleaseAll()`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L99) 兜底（`[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:99](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L99)`），
   注释：「正常路径不该用到它，用到了说明某处漏了 Release，但至少玩家不会卡死」

`ReleaseAll` 的调用点在 Runner 的清理里：
```csharp
VNPause.ReleaseAll(); // 兜底：任何漏掉的持有者都在这里被清掉
```
> 出处：[`VNScriptRunner.CleanupActiveEvent`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1092](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1092)）

**域重载关闭时的静态字段重置**：
```csharp
#if UNITY_EDITOR
// 关闭域重载（Enter Play Mode Options）时静态字段不会自动清空，
// 上一次 Play 遗留的句柄会让新的一次 Play 一开局就是暂停的。
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void ResetStatics()
{
    _handles.Clear();
    Changed = null;
}
#endif
```
> 出处：`VNPause.ResetStatics`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:117](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L117)）

> **这是 Unity 现代项目的必修课**。
> 开了「Enter Play Mode Options → Disable Domain Reload」之后，
> 所有 `static` 字段与 `static event` 在两次 Play 之间**不会重置**。
> 症状五花八门：一开局就暂停、事件被订阅两次、缓存指向已销毁对象。
> 解法就是这个 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`。
>
> 本专案在两个地方做了这件事：`VNPause.ResetStatics`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:117`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L117)）
> 与 `VNTutorialAnchors.ResetStatics`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:73`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L73)）
> （`[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:73](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L73)`）。
> **[`VNToast`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs) 的 `_canvas` / `_cards` 没有做**——
> 不过它是 `DontDestroyOnLoad` 且每次读都判空重建，风险较低，
> 但 `_cards` 列表里可能残留上一次 Play 的已销毁卡片。
> **这是一个值得补的地方**（见第二十一章）。

### 10.3 VNTime：受暂停影响、不受快进影响的时间源

```csharp
public static class VNTime
{
    /// <summary>单帧 dt 上限（秒）。切窗口回来的巨大 dt 会让弹道/倒计时跳一大截。</summary>
    public const float MaxStep = 0.05f;

    static float _time;

    /// <summary>暂停时为 0 的 dt。</summary>
    public static float Delta =>
        VNPause.IsPaused ? 0f : Mathf.Min(UnityEngine.Time.unscaledDeltaTime, MaxStep);

    /// <summary>暂停时不前进的累计时间轴（只用于「两次读数之差」，绝对值无意义）。</summary>
    public static float Time => _time;
```
> 出处：`VNTime`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:136](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L136)）

**累计时间靠一个隐藏驱动物体推进，而不是懒惰累加**：
```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Install()
{
    _time = 0f;
    var go = new GameObject("VNTimeDriver") { hideFlags = HideFlags.HideAndDontSave };
    Object.DontDestroyOnLoad(go);
    go.AddComponent<Driver>();
}

class Driver : MonoBehaviour
{
    void Update() => _time += Delta;
}
```
> 出处：`VNTime.Install`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:151](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L151)）

类注释解释理由（`[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:133](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L133)`）：
「计时由一个隐藏驱动物体每帧推进，**与读取顺序无关**——
用「读的时候才累加」的懒惰实现会在没人读的帧上丢时间。」

> **`MaxStep = 0.05f` 这个保护是从羽毛球模块提炼出来的**
> （注释在 `VNTime` 类上方，`[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:130](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L130)`）：
> 「切窗口回来时的巨大 dt 会让球瞬移过整个球场；既然要统一，这个保护就一并收进来。」
>
> **任何有物理/弹道/倒计时的游戏都需要这个 dt 上限**。
> Unity 有 `Time.maximumDeltaTime`（默认 1/3 秒）但那只影响 `Time.deltaTime`，
> 对 `unscaledDeltaTime` 无效。用 unscaled 的项目必须自己钳。

### 10.4 模块怎么用：Update 第一行

三铁律的落地就是一行：
```csharp
void Update()
{
    if (VNPause.IsPaused) return;        // 教程讲解中：连打条与倒计时一起冻住
    if (_phase != Phase.Playing) return;

    _timeLeft -= VNTime.Delta;           // 不受快进 timeScale 影响、受 VNPause 冻结
    ...
}
```
> 出处：[`VNQteModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs:53](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs#L53)）

**必须在读输入之前拦**，羽毛球模块的注释把这条写得最清楚：
```csharp
void Update()
{
    // 教程讲解中：整局冻结。**必须在 ReadInput 之前拦**——
    // 同下面确认框那条的教训，拦晚了照样能挥拍，「冻结」名不副实。
    if (VNPause.IsPaused) return;
    ...
    // 确认框开着时冻结整局：**必须在 ReadInput 之前拦**，
    // 否则 ReadInput 尾部还会照常触发挥拍/起跳，「冻结」名不副实
    if (_confirmOpen) { TickConfirm(); return; }

    ReadInput();
```
> 出处：[`VNBadmintonModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:410](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L410)）

同样的守卫在：
[`VNShopModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:68](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L68)`，
注释「教程讲解中：ESC 不该把商店关掉」）、
[`VNQuestBoardModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs:55](Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs#L55)`，
注释「三铁律②：必须在读输入之前」）、
[`VNResultPopupModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:90](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L90)`，
注释「教程讲解中：别让结算弹窗被同一下点击收掉」）。

### 10.5 教程自己的动画不受暂停影响

暗幕的描边呼吸必须用真实时间，否则会僵住：
```csharp
// 描边呼吸：教程期间全局暂停，所以必须用真实时间
float k = _pulse <= 0f
    ? 1f
    : 1f + _pulse * 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f);
```
> 出处：[`VNTutorialMask.Sync`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:167](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L167)）

等待玩家点击也用 `Time.unscaledTime`：
```csharp
_buttonPressed = false;
float shownAt = Time.unscaledTime;   // 教程期间全局暂停，只能用真实时间
```
> 出处：[`VNTutorialPlayer.WaitStepCo`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:355](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L355)）

> **暂停系统的经典自指问题**：暂停的发起者自己不能被暂停。
> 本专案的规则很清楚：**模块用 `VNTime`，教程用 `Time.unscaledTime`**。
> 如果以后再加一个「暂停时也要动」的 UI（比如暂停菜单的按钮动画），
> 同样走 `Time.unscaledTime` + `SetUpdate(true)`。

---

## 十一、教程系统：暗幕挖洞 + 锚点注册表

教程是本专案 UI 里技术含量最高的一块，因为它要**在不知道目标是什么的情况下高亮它**。

### 11.1 三个触发入口，一份实现

```
① 剧本 `tutorial <id> [force:on]` —— VNScriptRunner 协程等它播完
② 模块首次启动自动播 —— VNEventModule 在 OnLaunch **之后**调
③ 模块内任意时机 `yield return VNTutorialPlayer.PlayIdCo(id)`
```
> 出处：[`VNTutorialPlayer`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:15](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L15)）

**入口 ①**：
```csharp
case "tutorial":
{
    // tutorial <教程id> [force:on]
    // 默认「看过就跳过」（记录是全局的，读旧档不会重看）；
    // force:on 强制重看，帮助菜单/作者点名讲解用。
    // 快进时整段跳过：教学是给正常速度看的，SKIP 里只会是干扰。
    if (_skip) return null;
    var player = stage.tutorial != null ? stage.tutorial : VNTutorialPlayer.Instance;
    if (player == null) return null;
    var def = FindTutorial(cmd.Arg(0), cmd.line);
    if (def == null) return null;
    string forceArg = cmd.Kw("force");
    bool force = !string.IsNullOrEmpty(forceArg) &&
                 forceArg != "off" && forceArg != "false" && forceArg != "0";
    return player.PlayCo(def, force);
}
```
> 出处：[`VNScriptRunner.Dispatch`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2452](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2452)）

**入口 ②** 在模块基类里，且顺序有讲究：
```csharp
public void Launch(VNEventContext ctx, Action<string> onDone)
{
    _onDone = onDone;
    _finished = false;
    OnLaunch(ctx);

    // 教程必须在 OnLaunch **之后**播：要高亮记分板，记分板得先存在。
    // 讲解期间 VNPause 冻住全局，模块的 Update 第一行会早退，
    // 所以这一句之后模块虽然「开着」但一帧都不会跑。
    string tid = ctx != null ? ctx.Kw("tutorial", tutorialId) : tutorialId;
    if (!string.IsNullOrEmpty(tid)) VNTutorialPlayer.PlayAuto(tid, this);
}
```
> 出处：[`VNEventModule.Launch`](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:62](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L62)）

`PlayAuto` 是「发后不管」的，而且**协程挂在播放器自己身上**：
```csharp
if (!player.ShouldPlay(def, false)) return;
// 宿主可能中途被销毁（模块被 Destroy），所以协程挂在播放器自己身上
player.StartCoroutine(player.PlayCo(def, false));
```
> 出处：`VNTutorialPlayer.PlayAuto`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:147](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L147)）

> **协程宿主的选择是一个容易出错的点**。
> `StartCoroutine` 挂在谁身上，谁被销毁协程就断。
> 教程可能比启动它的模块活得久（玩家看教程时按 ESC 退出模块），
> 所以必须挂在播放器身上。

### 11.2 锚点注册表：为什么不能按名字找

```csharp
/// <summary>
/// 教程高亮目标的注册表：id → RectTransform。
///
/// 【为什么不用「按名字/路径找物体」】
/// 小游戏的 UI 全是代码程序化生成的（VNBadmintonCourt / VNPhotoBoothUi /
/// VNQuizModule…），层级和物体名随手就会改。用路径寻址的话，改一次布局
/// 教程就全废，而且**没有任何报错**——只是洞挖到了空气上。
/// 改成显式登记后，id 是一份稳定契约，布局怎么改都不影响。
/// </summary>
public static class VNTutorialAnchors
```
> 出处：[`VNTutorialAnchors`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:24](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L24)`）

模块侧的登记（羽毛球）：
```csharp
void RegisterTutorialAnchors()
{
    if (_court != null)
    {
        VNTutorialAnchors.Register(AnchorScore, _court.ScoreBoard);
        VNTutorialAnchors.Register(AnchorHint, _court.HintBox);
        VNTutorialAnchors.Register(AnchorNet, _court.NetRoot);
    }
    if (_me != null) VNTutorialAnchors.Register(AnchorMe, _me.Root);
    if (_op != null) VNTutorialAnchors.Register(AnchorOpponent, _op.Root);
    VNTutorialAnchors.Register(AnchorBall, _ballRect);
}
```
> 出处：[`VNBadmintonModule.RegisterTutorialAnchors`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:376](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L376)）

id 是 const 字符串（`VNBadmintonModule.AnchorScore`（[`Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:399`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L399)），
`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:399](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L399)` 起六行），
反注册在 `UnregisterTutorialAnchors`（`:389`），
由 `OnDestroy` 调用（`VNBadmintonModule.OnDestroy`，
`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:363](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L363)`）。

**取用时顺手清死条目**：
```csharp
public static RectTransform Get(string id)
{
    if (string.IsNullOrEmpty(id)) return null;
    if (!_map.TryGetValue(id, out var rect)) return null;
    if (rect == null) { _map.Remove(id); return null; } // 已销毁：顺手清掉
    return rect;
}
```
> 出处：`VNTutorialAnchors.Get`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:48](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L48)）

**反注册的「只删自己」保护**：
```csharp
/// <summary>
/// 反注册。传了 rect 时只在当前登记的就是它时才删——
/// 否则两个实例先后登记同一个 id，先销毁的那个会把后来者的登记抹掉。
/// </summary>
public static void Unregister(string id, RectTransform rect = null)
{
    if (string.IsNullOrEmpty(id)) return;
    if (rect != null && (!_map.TryGetValue(id, out var cur) || cur != rect)) return;
    _map.Remove(id);
}
```
> 出处：`VNTutorialAnchors.Unregister`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:36](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L36)）

**prefab 侧有一个自动登记组件**：
```csharp
public class VNTutorialAnchor : MonoBehaviour
{
    [Header("教程资产里 anchor 字段填的名字（如 hud.stats / toolbar.save）")]
    public string id;

    RectTransform _rect;

    void OnEnable()
    {
        _rect = transform as RectTransform;
        VNTutorialAnchors.Register(id, _rect);
    }

    void OnDisable() => VNTutorialAnchors.Unregister(id, _rect);
}
```
> 出处：`VNTutorialAnchor`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:82](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L82)）

> **两条登记路径的分工**：程序化 UI 直接调 `Register`（省一个组件），
> 皮肤 prefab 里的按钮挂 `VNTutorialAnchor`（美术能自己配）。
> 这是「同一个机制、两个使用面」的好例子。

### 11.3 挖洞：世界四角换算，每帧更新

**不能抄 anchoredPosition**：
```csharp
/// 【坐标怎么算】
/// 取目标的**世界四角**再换算到本图的本地坐标，而不是抄 anchoredPosition ——
/// 立绘挂在 ZoomRoot / TiltRoot 底下，运镜的缩放旋转会让 anchoredPosition
/// 与屏幕上的实际位置对不上。世界四角天然含了整条父级链的变换。
```
> 出处：[`VNTutorialMask`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:11](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L11)）

实现：
```csharp
if (hole.target != null)
{
    // 世界四角 → 本图本地坐标（含整条父级链的缩放/旋转/运镜）
    var corners = _corners;
    hole.target.GetWorldCorners(corners);
    Vector3 p0 = _rect.InverseTransformPoint(corners[0]);
    min = max = new Vector2(p0.x, p0.y);
    for (int i = 1; i < 4; i++)
    {
        Vector3 p = _rect.InverseTransformPoint(corners[i]);
        min = Vector2.Min(min, new Vector2(p.x, p.y));
        max = Vector2.Max(max, new Vector2(p.x, p.y));
    }
    float pad = hole.padding;
    min -= new Vector2(pad, pad);
    max += new Vector2(pad, pad);
}
```
> 出处：`VNTutorialMask.TryResolve`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:183](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L183)）

`_corners` 是静态复用数组（`VNTutorialMask._corners`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:220`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L220)），
`[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:220](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L220)`），避免每帧分配。

**每帧更新的理由**：
```csharp
/// 【每帧更新】
/// 高亮的东西可能在动（立绘、飞行中的球、刚弹出的面板还在做补间），
/// 所以洞的位置每帧现算。开销就是 4 次 GetWorldCorners，可以忽略。
```
> 出处：`VNTutorialMask` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:16](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L16)）

在 `LateUpdate` 里（`VNTutorialMask.LateUpdate`，
`[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:138](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L138)`）——
必须是 LateUpdate，因为要在所有位置更新完之后再算。

**shader 参数的宽高比归一化**：
```csharp
// 宽高比校正后的空间里，1 单位 = h 像素（见 shader 注释），
// 所以像素参数一律除以高度
_mat.SetVectorArray(IdHoles, _holeData);
_mat.SetFloat(IdHoleCount, count);
_mat.SetFloat(IdAspect, w / h);
_mat.SetFloat(IdCorner, _cornerPx / h);
_mat.SetFloat(IdFeather, Mathf.Max(0.0005f, _featherPx / h));
_mat.SetFloat(IdEdgeWidth, Mathf.Max(0.0005f, _edgeWidthPx / h));
```
> 出处：`VNTutorialMask.Sync`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:158](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L158)）

最多 4 个洞（`VNTutorialMask.MaxHoles`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:27`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L27)），
`[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:27](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L27)`），
洞数据打包成 `Vector4(中心x, 中心y, 半宽, 半高)`
（`VNTutorialMask.TryResolve` 返回值，
`[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:216](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L216)`）。

**shader 找不到时的降级**：
```csharp
var shader = Shader.Find("VN/TutorialMask");
if (shader == null)
{
    // 没有 shader 时退化成一整块纯暗幕：教学文字仍然看得见，
    // 只是没有洞（比整个教程不显示要好）
    Debug.LogError("[VNTutorial] 找不到 Shader \"VN/TutorialMask\"，" +
                   "暗幕退化为整屏压暗（洞挖不出来）。", this);
    _image.color = _dimColor;
    return;
}
```
> 出处：`VNTutorialMask.Awake`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:79](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L79)）

而且提供了 `sourceMaterial` 字段避开打包剥离：
```csharp
// 出正式包前建议指一份材质资产：只被 Shader.Find 引用的 shader 会被打包剥掉，
// 同 VNScreenShockwave.sourceMaterial 的处理。留空时运行时走 Shader.Find。
[Header("暗幕材质来源（留空 = 运行时 Shader.Find(\"VN/TutorialMask\")）")]
public Material maskMaterial;
```
> 出处：`VNTutorialPlayer.maskMaterial`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:52](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L52)）

> **`Shader.Find` 在 Build 里会失效**是 Unity 老问题：
> 打包时只有「被场景/prefab/资产引用到」的 shader 会进包。
> 解法有三：① 加进 Project Settings → Graphics 的 Always Included Shaders；
> ② 建一个 Material 资产引用它（本专案的做法）；③ 放进 Resources。
> 本专案选 ②，因为可以顺便让美术调整材质参数。

### 11.4 播放流程的四个细节

**细节 1：冻住世界的同时抢回光标**
```csharp
// ---- 冻住世界 ----
_pause = VNPause.Acquire(gameObject, "tutorial");
_cursorWasVisible = Cursor.visible;
Cursor.visible = true;              // 互动模块藏了系统光标，这里要抢回来
if (_runner == null) _runner = FindFirstObjectByType<VNScriptRunner>();
_runner?.SetSkip(false);            // 到教学必停，同 choice / event
_runner?.SetAuto(false);
```
> 出处：`VNTutorialPlayer.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:177](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L177)）

理由在类注释里（`[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:30](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L30)`）：
「亲密互动模块会把系统光标藏起来。教程弹在它上面时玩家看不见指针也就点不了「下一步」」。

**细节 2：淡出之后才解除暂停**
```csharp
// ---- 收起 ----
// 淡出至少跨两帧，所以推进用的那一下点击/按键在解除暂停时
// wasPressedThisFrame 早已复位，不会被下面的模块或 Runner 再吃一次
// （ESC 尤其要紧：羽毛球把它当认输键）
yield return _group.DOFade(0f, FadeOut).SetUpdate(true).SetLink(gameObject)
                   .WaitForCompletion();
```
> 出处：`VNTutorialPlayer.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:202](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L202)）

> **「同一下输入被两个系统吃两次」是模态 UI 的经典 bug**。
> `wasPressedThisFrame` 在同一帧内对所有读取者都为 true。
> 解法就是这里的做法：**保证关闭动作至少跨一帧**，
> 让 `wasPressedThisFrame` 在下一个系统读取时已经复位。

**细节 3：每步最短停留**
```csharp
/// <summary>每步最短停留：挡掉从上一屏带过来的那一下点击</summary>
const float MinStepTime = 0.22f;
```
> 出处：`VNTutorialPlayer.MinStepTime`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:44](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L44)）

```csharp
while (true)
{
    yield return null;
    if (Time.unscaledTime - shownAt < MinStepTime) continue;
    ...
}
```
> 出处：`VNTutorialPlayer.WaitStepCo`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:356](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L356)）

同一个 0.22 秒也在 `ApplyStep` 的卡片弹入动画里
（`VNTutorialPlayer.ApplyStep`，`[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:316](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L316)`）——
「看到新卡片弹出来」与「可以点下一步」在时间上对齐。

**细节 4：三条兜底路径都要还暂停**
```csharp
void Close()
{
    IsPlaying = false;
    if (_group != null) _group.blocksRaycasts = false;
    if (_root != null) _root.gameObject.SetActive(false);
    Cursor.visible = _cursorWasVisible;
    VNPause.Release(ref _pause);
}

// 三条兜底路径：宿主被禁用 / 被销毁时也必须把暂停还回去
void OnDisable()
{
    if (IsPlaying) CancelImmediate();
    else VNPause.Release(ref _pause);
}

void OnDestroy()
{
    VNPause.Release(ref _pause);
    if (_instance == this) _instance = null;
}
```
> 出处：`VNTutorialPlayer.Close`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:231](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L231)）

再加上 Runner 的 `CleanupActiveEvent`：
```csharp
// 教程要先收：它可能是被模块启动的，模块一销毁就没人来解除暂停了，
// 而暂停不解除 = 整个游戏永久卡死（比暗幕留在屏幕上严重得多）
(stage != null && stage.tutorial != null
    ? stage.tutorial : VNTutorialPlayer.Instance)?.CancelImmediate();
VNPause.ReleaseAll(); // 兜底
```
> 出处：`VNScriptRunner.CleanupActiveEvent`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1088](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1088)）

> **数一数总共几层保险**：
> ① `PlayCo` 正常结束 → `Close` → `Release`
> ② ESC 跳过 → 同样走到 `Close`
> ③ `CancelImmediate` → `Close`
> ④ `OnDisable` → `CancelImmediate` 或直接 `Release`
> ⑤ `OnDestroy` → `Release`
> ⑥ 宿主销毁 → `VNPause.Prune`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:107`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L107)） 自动清
> ⑦ Runner 清理 → `CancelImmediate` + `ReleaseAll`
>
> **七层**。这不是过度设计——注释已经说明了，漏掉任何一条的后果是「游戏永久卡死」。
> **后果越严重的资源，保险层数应该越多**。这是很好的工程判断。

### 11.5 卡片定位：自动避让洞口

```csharp
/// <summary>卡片落位：Auto 时躲开洞（洞在上半屏就把卡片放下半屏）</summary>
void PlaceCard(VNTutorialStep step)
{
    if (_card == null) return;
    var spot = step.card;
    if (spot == VNTutorialCardSpot.Auto)
    {
        spot = _mask != null && _mask.TryGetFirstHoleUv(out Rect uv)
            ? (uv.center.y > 0.5f ? VNTutorialCardSpot.Bottom : VNTutorialCardSpot.Top)
            : VNTutorialCardSpot.Center;
    }
    ...
}
```
> 出处：`VNTutorialPlayer.PlaceCard`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:322](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L322)）

`TryGetFirstHoleUv`（`[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:223](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L223)`）
就是把第一个洞的 shader 数据反算回归一化矩形。

**卡片不吃射线**：
```csharp
_cardGroup = go.GetComponent<CanvasGroup>();
_cardGroup.blocksRaycasts = false;   // 点哪儿都算「下一步」，别让卡片吃掉点击
```
> 出处：`VNTutorialPlayer.BuildProceduralCard`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:499](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L499)）

而暗幕**要**吃射线：
```csharp
_image.raycastTarget = true; // 只读演示：洞外洞内一律吃掉点击
```
> 出处：`VNTutorialMask.Awake`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:70](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L70)）

> **注意这个设计取舍**：本专案的教程是「只读演示」——
> 洞只是视觉高亮，玩家不能真的点洞里的东西。
> 如果要做「引导玩家点这个按钮」的交互式教程，暗幕就不能吃射线，
> 而要做成「洞的区域不吃、洞外吃」——那需要一个自定义的 `ICanvasRaycastFilter`。
> 这是一个明确的功能边界，写在代码里比写在文档里更不容易被忘记。

### 11.6 「看过了」是全局记录，不是 flag

```csharp
/// 【为什么不是 flag】
/// flag 跟随存档快照走，读旧档就会「忘记」看过教程，于是新手引导又弹一遍；
/// 开新周目更是每篇重看。看过教程是玩家的元知识，跟 CG 解锁同类，
/// 属于全局永久记录。
///
/// 文件：persistentDataPath/vn_tutorial_seen.json，有新增才写盘。
```
> 出处：[`VNTutorialSeen`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:11](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L11)）

实现照抄 [`VNCgUnlocks`](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs:14](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs#L14)`），
两者的结构几乎一模一样：
- 一个 `[System.Serializable] class SaveShape { public List<string> ids; }`
  （`VNTutorialSeen.SaveShape`，`[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:24](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L24)`；
  `VNCgUnlocks.SaveShape`，`[Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs:17](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs#L17)`）
- 一个 `HashSet<string>` 内存缓存 + `EnsureLoaded()` 惰性读盘
  （`VNTutorialSeen.EnsureLoaded`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:45`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L45)），`[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:45](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L45)`；
  `VNCgUnlocks.EnsureLoaded`（[`Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs:27`](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs#L27)），`[Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs:27](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs#L27)`）
- 有新增才写盘：`if (!_seen.Add(id)) return;`
  （`VNTutorialSeen.Mark`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:74`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L74)），`[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:78](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L78)`）
- 写盘前排序，「文件内容稳定，便于人工查看」
  （`VNTutorialSeen.Flush`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:91`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L91)），`[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:96](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L96)`）
- 读失败按「全未看/全未解锁」处理并 LogError
  （`VNTutorialSeen.EnsureLoaded`，`[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:61](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L61)`）

**总开关走 PlayerPrefs**：
```csharp
public static bool Enabled
{
    get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
    set => PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
}
```
> 出处：`VNTutorialSeen.Enabled`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:39](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L39)）

**ESC 跳过也算看过**：
```csharp
// ESC 跳过也算看过：玩家明确表示不想看，下次不该再拦他一次。
// 想重看走设置面板的「重置教程记录」或剧本 force:on。
VNTutorialSeen.Mark(string.IsNullOrEmpty(def.id) ? def.name : def.id);
```
> 出处：`VNTutorialPlayer.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:208](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L208)）

三个数据归属的对照表（这张表值得记住）：

| 数据 | 存哪 | 语义 | 出处 |
|---|---|---|---|
| 剧情状态（好感、道具、任务） | [`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs) → 存档 | 跟着存档回退 | `VNFlags`（[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:15](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L15)） |
| CG 解锁 | 独立 JSON | 全局永久，新周目不丢 | `VNCgUnlocks`（[Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs:14](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs#L14)） |
| 教程看过 | 独立 JSON | 同上 | `VNTutorialSeen`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:19](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L19)） |
| 音量/语言/文字速度 | PlayerPrefs | 玩家偏好，与存档无关 | `VNConfigPanel.BgmKey`（[`Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:13`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L13)） 等（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:13](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L13)） |
| AI 跨场记忆 | 存档 | 剧情状态，读旧档她不该记得未来 | [`VNSaveData.aiMemories`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:72](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L72)） |
| AI 日记本 | 独立 JSON | 玩家收藏品，同 CG | [`VNAiDiaryPanel`](Assets/Project/Scripts/VNEffects/Script/VNAiDiaryPanel.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNAiDiaryPanel.cs:12](Assets/Project/Scripts/VNEffects/Script/VNAiDiaryPanel.cs#L12)） |

> **「这份数据该跟着存档回退吗」是一个必须在设计阶段问清楚的问题**。
> 答错的代价：跟着回退的东西没跟（读旧档她记得未来的事）、
> 不该跟的跟了（读旧档 CG 画廊少了几张）。
> 本专案把这条判断在四个地方各写了一遍注释，说明踩过。

---

## 十二、事件模块 UI 框架：三铁律与 EventLayer

### 12.1 契约：一进一出

```csharp
/// 生命周期：Runner 从 VNEventRegistry 实例化到事件层 → Launch →
/// 模块自行交互 → 子类调 Done(结果名) → Runner 销毁模块并按结果分支
/// （结果名匹配 event 命令下的「* 结果行」；整数结果同时写入 flag「事件结果」）。
```
> 出处：[`VNEventModule`](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:43](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L43)）

进：`VNEventContext`（`[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:8](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L8)`）
```csharp
public string eventId;                       // 剧本里引用的模块 id
public VNStage stage;                        // 舞台（约定：模块只读，不直接改演出）
public Dictionary<string, string> kwargs;    // 剧本行的 key:value 参数
public List<string> outcomes;                // 剧本「* 结果行」的结果名
public int line;                             // 源文件行号（报错定位用）
```
> 出处：`VNEventContext`（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:10](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L10)）

三个取参数的辅助：`Kw`（`:21`）、`KwF`（`:24`）、`KwI`（`:31`），全都带默认值。

出：`Done(outcome)`，只会生效一次
```csharp
protected void Done(string outcome)
{
    if (_finished) return;
    _finished = true;
    _onDone?.Invoke(outcome ?? "");
}
```
> 出处：`VNEventModule.Done`（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:79](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L79)）

> **`_finished` 守卫非常重要**。小游戏的结束条件常常同时满足
> （时间到 + 分数达标、被发现 + 主动退出），没有守卫就会回调两次，
> Runner 那边 `while (result == null)` 早已退出，第二次回调写进一个已废弃的闭包，
> 症状是「偶尔跳错分支」——极难复现。

**剧本能不能接住某个结果，模块可以问**：
```csharp
/// <summary>剧本是否用「* 结果行」接住了该结果名（无结果行 = 全部放行）</summary>
public bool AcceptsOutcome(string name) =>
    outcomes == null || outcomes.Count == 0 || outcomes.Contains(name);
```
> 出处：`VNEventContext.AcceptsOutcome`（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:18](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L18)）

地图模块用它来「只开放本次剧情接住的地点」
（[`VNMapModule`](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs) 类注释，`[Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs:15](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs#L15)`）。

> **这是一个很聪明的双向契约**：一般的「返回结果」是单向的，
> 但这里模块可以反过来问剧本「你准备接住哪些结果」，从而调整自己的行为。
> 用在地图上是「剧本只写了三个地点，那我就只显示这三个」——
> 剧本作者不用在模块和剧本两边同步地点列表。

### 12.2 三铁律

写在 `VNEventModule` 的类注释里（`[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:47](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L47)` 起）：

```
- 模块只操作自己的 UI 子树与 VNFlags，不直接改舞台演出（背景/立绘交给事件前后的剧本行）
- 计时用 unscaledTime、Tween 用 SetUpdate(true)，不受快进 DOTween.timeScale 影响
- 所有 Tween SetLink(gameObject) 防泄漏（模块随时可能被销毁）
```

**铁律①（不碰舞台）的三次破例**，每次都在类注释里写明了边界：

| 模块 | 破例内容 | 边界收紧为 | 出处 |
|---|---|---|---|
| [`VNAiTalkModule`](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs) | AI 控制立绘表情 | 只碰表情与对话框内容，三条退出路径都还原 | 类注释（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:24](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L24)） |
| [`VNInteractionModule`](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs) | 直接摸舞台上的立绘 | 只碰表情与叠加层 + 临时关鼠标视差，三条路径还原 | 类注释（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:20](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L20)） |
| [`VNSecretPhotoMode`](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs) | 直接操控 ZoomRoot 镜头 | 只碰镜头容器、UI 可见性、Ken Burns 开关，四条路径还原 | 类注释（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs:12](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs#L12)） |

`VNAiTalkModule` 的理由：
> 铁律说「不直接改舞台演出」，但 AI 控制表情恰恰就是改舞台。这里**刻意破例**，
> 因为自绘立绘要把眨眼/口型/色调匹配/出场动画全部重接一遍，代价远大于收益。
> 破例的边界收得很紧：
>   - 只碰「表情」和「对话框内容」两样，绝不碰位置/缩放/背景/特效
>   - 进入时记下原表情，正常结束、ESC 退出、调试中断（CancelForDebug）
>     三条路径都还原，保证退出后舞台与剧本记录一致
> 出处：`VNAiTalkModule` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:24](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L24)）

**而不破例的模块也会明确说明**：
```csharp
/// 【本模块不破模块三铁律】
/// 角色在这个玩法里是**被动 CG**（不换表情、不碰立绘），所以不像 VNInteractionModule /
/// VNAiTalkModule 那样需要破例去驱动舞台。全部绘制都在自己的 UI 子树内。
```
> 出处：[`VNFogWipeModule`](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:43](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L43)）

> **这是我在这个专案里看到的最好的工程习惯之一**：
> **规则破例时，在破例处写明「破的是哪条、边界收到哪里、怎么保证还原」**。
> 三个模块各写了一遍，还互相引用（「先例：VNAiTalkModule」）。
> 这样后来者读到任何一处，就知道这不是随手写的、而是有意为之，
> 也知道自己要破例时该怎么写。
>
> 反面教材是「铁律写在文档里，代码里到处都是没有解释的违反」——
> 那样规则很快就没人当真了。

### 12.3 EventLayer：一次创建、永久复用

```csharp
RectTransform EnsureLayer(Canvas rootCanvas)
{
    if (_layer != null) return _layer;

    var go = new GameObject("EventLayer", typeof(RectTransform));
    _layer = (RectTransform)go.transform;
    _layer.SetParent(rootCanvas.transform, false);
    _layer.anchorMin = Vector2.zero;
    _layer.anchorMax = Vector2.one;
    _layer.offsetMin = Vector2.zero;
    _layer.offsetMax = Vector2.zero;

    var canvas = go.AddComponent<Canvas>();
    canvas.overrideSorting = true;
    canvas.sortingOrder = LayerSortingOrder;
    go.AddComponent<GraphicRaycaster>();
    return _layer;
}
```
> 出处：[`VNEventRegistry.EnsureLayer`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs)（[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:72](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L72)）

**60 这个数字的理由**写在类注释里
（`[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:9](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L9)`）：
「位于 ChoicePanel 45 与 ScreenTransition 100 之间，因此进出事件可以用全屏转场包裹」。

**嵌套 Canvas 必须自带 GraphicRaycaster**（`:87`）——
父 Canvas 的 Raycaster 不会为 `overrideSorting` 的子 Canvas 服务。
同一件事在 [`VNChoicePanel.Build`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs) 里也做了：
```csharp
// 嵌套 Canvas 需要自己的 Raycaster 才能接收点击
if (gameObject.GetComponent<GraphicRaycaster>() == null)
    gameObject.AddComponent<GraphicRaycaster>();
```
> 出处：`VNChoicePanel.Build`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:91](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L91)）

**Canvas 从哪儿来**：
```csharp
var canvas = stage.characterLayer != null
    ? stage.characterLayer.GetComponentInParent<Canvas>() : null;
if (canvas != null) canvas = canvas.rootCanvas;
```
> 出处：[`VNScriptRunner.EventCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2924](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2924)）

从立绘层往上找到根 Canvas——不硬编码物体名，是正确做法。
同一手法在 `VNPhotoBoothModule.OnLaunch`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:192`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L192)）
（`[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:196](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L196)`）
与 `VNSecretPhotoMode.Initialize`（[`Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs:61`](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs#L61)）
（`[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs:68](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs#L68)`）里也用了。

### 12.4 EventCo 的完整流程

```csharp
SetSkip(false); // 到玩法必停，同 choice
SetAuto(false);

var module = stage.eventRegistry.Create(id, canvas, cmd.line);
if (module == null) yield break; // 模块缺失：告警后顺序继续

_eventActive = true;
_activeEventModule = module;
stage.dialogue?.HideBox();
ApplyGameplayHudVisible(false);

var outcomes = new List<string>();
if (cmd.options != null)
    foreach (var opt in cmd.options) outcomes.Add(opt.text);
var ctx = new VNEventContext { ... };
string result = null;
module.Launch(ctx, r => result = r ?? "");
while (result == null) yield return null;

_activeEventModule = null;
bool recordInBacklog = module.RecordInBacklog; // 销毁前读取
Destroy(module.gameObject);
stage.dialogue?.Show();
ApplyGameplayHudVisible(true);
_eventActive = false;
```
> 出处：`VNScriptRunner.EventCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2928](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2928)）

**`while (result == null) yield return null;` 是整个机制的核心**——
一个字符串从 null 变成非 null 就是「模块结束了」的信号。
注意 `r => result = r ?? ""`：模块传 null 时转成空串，
这样「模块返回空结果」与「模块还没结束」能区分开。

**结果分支**：
```csharp
if (int.TryParse(result, out int numeric))
    VNFlags.Set("事件结果", numeric);

if (cmd.options == null || cmd.options.Count == 0) yield break;
foreach (var opt in cmd.options)
{
    if (opt.text != result) continue;
    if (!string.IsNullOrEmpty(opt.flagOp)) VNFlags.Apply(opt.flagOp);
    if (!string.IsNullOrEmpty(opt.jumpLabel)) JumpTo(opt.jumpLabel, opt.line);
    yield break;
}
Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：事件「{id}」返回结果" +
                 $"「{result}」没有对应的「* 结果行」，顺序继续");
```
> 出处：`VNScriptRunner.EventCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2963](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2963)）

「没有对应结果行 → 顺序继续 + 告警」是正确的容错——
不是卡死，也不是静默，而是能跑下去且日志里看得见。

> **但这里有一个已知的坑**：几个模块的关键结果（`aitalk` 的「失败」、
> `interact` 的「拒绝」）如果剧本没接住，玩家会遇到「什么都没发生」的困惑。
> 所以这两条被写进了 Lint 检查（见 `VNAiTalkModule` 类注释，
> `[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:22](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L22)`
> 「必须接住否则玩家卡死」）。
> **规律**：如果一个结果是「异常/失败」路径，就该在静态检查里强制要求接住。

### 12.5 模块内插播台词：RunInlineCo

模块想在交互中途播一段演出（说话、放音效、震屏），走这个入口：

```csharp
public IEnumerator RunInlineCo(string lines)
```
> 出处：`VNScriptRunner.RunInlineCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:315](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L315)）

**控制流命令被黑名单挡掉**：
```csharp
static readonly HashSet<string> InlineBlockedKeywords = new HashSet<string>
{
    "jump", "choice", "call", "return", "label", "event",
    "save", "load", "chapter", "endgame",
};
```
> 出处：`VNScriptRunner.InlineBlockedKeywords`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:304](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L304)）

理由（注释在 `:300`）：「让它们跑起来会把 Runner 的 `_index` / `_callStack` / 存档状态搅乱，
症状是事件结束后剧本跳到莫名其妙的地方」。

**逐条走与主循环同一个 `Dispatch`**：
```csharp
var cmd = ResolveParameters(raw);
IEnumerator co = null;
try { co = Dispatch(cmd); }
catch (System.Exception e) { Debug.LogError(...); }
if (co == null) continue;
if (cmd.isAsync) StartCoroutine(co);
else yield return StartCoroutine(co);
```
> 出处：`VNScriptRunner.RunInlineCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:337](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L337)）

> **「共用同一个 Dispatch」保证了语义永远一致**（注释在 `:312`）。
> 如果模块自己实现一套「简化版的说话」，很快就会和主流程分家
> （主流程加了口型同步，模块里没有；主流程改了翻译查表，模块里还是老的）。
> **能复用执行器就别复制执行器**。

**但有一个例外**——偷拍被发现时的插话不走 `RunInlineCo`：
```csharp
/// <summary>
/// 剧本之外插一句话（偷拍被发现时她的反应）：直接写对话框 + 进回想，
/// 不走 RunInlineCo——那会嵌套一层 SayCo 把 _waitingAtSay 清掉。
/// 玩家点一下就推进原剧本的下一句。
/// </summary>
public void SayOutOfScript(string speakerId, string text)
{
    if (stage == null || string.IsNullOrEmpty(text)) return;
    stage.Say(speakerId, null, text);
    _backlog?.Record(stage.GetDisplayName(speakerId), text);
}
```
> 出处：`VNScriptRunner.SayOutOfScript`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1828](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1828)）

> **这个例外的原因值得理解**：`RunInlineCo` 里的 `say` 会走 `SayCo`，
> `SayCo` 会把 `_waitingAtSay` 先设 true 再设 false。
> 而偷拍是在**原剧本正停在某句台词上**（`_waitingAtSay == true`）时插进来的，
> 嵌套一层就把外层的等待状态清掉了，玩家点击后原剧本不会推进。
> 所以这里只做「写对话框 + 记回想」两件事，不碰状态机。
>
> **通用教训**：协程状态机的字段如果不是栈式的（每层各存一份），
> 就不能嵌套调用。要么改成栈式，要么给非嵌套场景开一个绕过路径。

---

## 十三、玩法模块 UI 逐个详述

十六个模块，从最简单的开始。每个都讲：UI 结构、输入方式、结果契约、有什么独门技术。

### 13.0 先看共性：所有模块 UI 的通用骨架

读完十六个模块之后回头看，会发现它们的 `OnLaunch` 高度一致：

```
OnLaunch(ctx)
 ├─ ApplyConfig()        从 VNGameConfig 覆盖模板上的资产列表
 ├─ ParseArgs(ctx)       读 kwargs，参数不成立就 Done("") 提前返回
 ├─ BuildUi()            程序化生成整套 UI（或 皮肤 prefab + 槽位绑定）
 ├─ Refresh*()           把数据填进 UI
 └─ _phase = Playing
Update()
 ├─ if (VNPause.IsPaused) return;
 ├─ if (_phase != Playing) return;
 ├─ dt = VNTime.Delta
 ├─ ReadInput()
 └─ 判定 → Finish() → Done(结果名)
```

**配置覆盖**几乎每个模块都有一份：
```csharp
void ApplyConfig()
{
    var cfg = VNGameConfig.Active;
    if (cfg == null) return;
    VNGameConfig.ApplyList(cfg.photoFrames, ref frames);
    VNGameConfig.ApplyList(cfg.photoStickers, ref stickers);
    ...
}
```
> 出处：[`VNPhotoBoothModule.ApplyConfig`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:215](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L215)）

同样的模式在 `VNShopModule.OnLaunch`（[`Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:40`](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L40)）
（`[Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:43](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L43)`）、
[`VNMapModule.OnLaunch`](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs:75](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs#L75)`）、
[`VNQuestLog.Awake`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:55](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L55)`）、
[`VNStatsHud.Awake`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:52](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L52)`）、
[`VNInventory.Awake`](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNInventory.cs:38](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs#L38)`）里。

`ApplyList` 的覆盖语义只有一条：
```csharp
/// <summary>
/// 覆盖语义的统一实现：source 非空则把 target 换成 source 的副本，否则原样不动。
/// 返回是否发生了覆盖（供日志使用）。
/// </summary>
public static bool ApplyList<T>(List<T> source, ref List<T> target)
{
    if (source == null || source.Count == 0) return false;
    target = new List<T>(source);
    return true;
}
```
> 出处：[`VNGameConfig.ApplyList`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:265](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L265)）

**为什么需要这一层**（`VNGameConfig` 类注释，
`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:9](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L9)`）：
场景生成器会 `NewScene(EmptyScene)` 从零重造场景，
所有挂在场景组件上的引用每次重建全部清空。
把数据搬进 `Assets/Resources/VNGameConfig.asset`
（`VNGameConfig.AssetPath`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:39`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L39)），`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:39](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L39)`），
场景怎么重建都不受影响。

> **这是所有「场景可重建」项目必须解决的问题**。
> 本专案的解法很干净：ScriptableObject 放 Resources、运行时 `Resources.Load` 直接取
> （`VNGameConfig.Active`，`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:48](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L48)`），
> 场景里一个引用字段都不需要。
> 组件上的 `config` 字段只是可选覆盖（`VNScriptRunner.config`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:105`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L105)），
> `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:105](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L105)`）。

---

### 13.1 QTE 连打条 VNQteModule（最小范例，200 行）

**UI**：全屏暗幕 + 中央面板 + 标题 + 计数 + 倒计时 + 进度条。
```csharp
void BuildUi(string titleText)
{
    // 全屏暗幕（拦截点击，也是"点哪都算"的感受来源）
    var dim = CreateImage("Dim", (RectTransform)transform, null,
        new Color(0f, 0f, 0f, 0.55f));
    Stretch(dim);
    ...
}
```
> 出处：[`VNQteModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs:104](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs#L104)）

**输入**：轮询，不用 EventSystem。
```csharp
bool pressed =
    (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
    (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);
if (pressed)
{
    _audio.PlaySe("se1");
    _count++;
    _panel.DOKill(true);
    _panel.DOPunchScale(Vector3.one * 0.04f, 0.15f, 8, 0.6f)
          .SetUpdate(true).SetLink(gameObject);
}
```
> 出处：`VNQteModule.Update`（[Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs:60](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs#L60)）

**`DOKill(true)` 的 true 参数**是「complete = true」——
让上一次的 punch 立刻跳到终点再杀掉，避免连打时缩放累积漂移。

**结果延迟 0.8 秒**：
```csharp
DOVirtual.DelayedCall(0.8f, () => Done(success ? "success" : "fail"), true)
         .SetLink(gameObject);
```
> 出处：`VNQteModule.Finish`（[Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs:88](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs#L88)）

`DOVirtual.DelayedCall` 的第三个参数 `true` 就是 `ignoreTimeScale`——
这是三铁律②在 DOTween 上的落地。

> **这个模块是「新模块该长什么样」的最佳参考**：200 行、
> 一个 enum Phase、一个 BuildUi、一个 Update、一个 Finish。
> 写新玩法时先照抄它的骨架。

---

### 13.2 地图 VNMapModule

**UI**：全屏地图底图 + 归一化坐标摆放的地点标记。

**地点数据在 Inspector / VNGameConfig 里**：
```csharp
[System.Serializable]
public class Location
{
    [Header("地点名 = 返回给剧本的结果名（对应「* 结果行」，永远不翻译）")]
    public string name;
    [Header("英文显示名（本地化；留空 = 显示地点名）")]
    public string displayNameEn;
    [Header("日文显示名（本地化；留空 = 显示地点名）")]
    public string displayNameJa;
    [Header("在地图上的归一化坐标（0,0 左下 ～ 1,1 右上）")]
    public Vector2 position = new Vector2(0.5f, 0.5f);
    [Header("显示条件（VNFlags 表达式，如 好感度>=2；留空 = 总是显示）")]
    public string condition;
    [Header("可选自定义图标；留空 = 程序化光点")]
    public Sprite icon;
```
> 出处：`VNMapModule.Location`（[Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs:26](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs#L26)）

**「逻辑名」与「显示名」严格分离**：
```csharp
/// <summary>当前语言的显示名；逻辑（结果匹配、去过_xx flag）永远用 name</summary>
public string DisplayName
{
    get
    {
        switch (VNLocale.Language)
        {
            case VNLanguage.English:
                return string.IsNullOrEmpty(displayNameEn) ? name : displayNameEn;
            case VNLanguage.Japanese:
                return string.IsNullOrEmpty(displayNameJa) ? name : displayNameJa;
            default: return name;
        }
    }
}
```
> 出处：`VNMapModule.Location.DisplayName`（[Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs:42](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs#L42)）

> **这是本地化设计里最重要的一条规则**：
> **标识符永不翻译，显示名才翻译**。
> 本专案在多处贯彻：结果名固定中文（[`VNBattleModule`](Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs) 类注释，
> 「结果名固定中文：胜利 / 失败 / 逃跑（事件结果名是逻辑标识符，永不翻译）」，
> `[Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs:24](Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs#L24)`）、
> 选项匹配按索引（[`VNScriptRunner.ChoiceCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)，
> `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2876](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2876)`）。
> 一旦让翻译参与逻辑匹配，切换语言就会导致分支错乱。

**结果行控制本次开放哪些地点**（用 `ctx.AcceptsOutcome`，见 12.1）。

---

### 13.3 回合制战斗 VNBattleModule

**UI**：暗幕（吃射线）+ 面板（吃射线）+ 敌人（光晕色块 + 双眼）+ 双方 HP 条 + 战斗日志 + 四个动作按钮。

```csharp
dim.GetComponent<Image>().raycastTarget = true; // 拦截点击穿透
```
> 出处：`VNBattleModule.BuildUi`（[Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs:253](Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs#L253)）

**与养成系统的联动全走 flag**：
```csharp
/// 与养成系统联动（可选）：patkstat:体力 → 我方攻击改读 flag「体力」
/// （同理 phpstat / pdefstat）；剧本先 stat 练属性，战斗自动变强——
/// 属性影响战斗的桥全在 flags，模块不认识任何具体属性名。
```
> 出处：`VNBattleModule` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs:20](Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs#L20)）

> **「模块不认识任何具体属性名」是很关键的解耦**。
> 剧本写 `patkstat:体力`，模块就去读 flag「体力」；
> 换个游戏改成 `patkstat:剑术` 也一样跑。
> 模块与游戏内容之间只有「flag 名字符串」这一条细线。

**战斗结束额外写一个 flag 供车轮战**：
「战斗结束额外写 flag「战斗剩余HP」（失败时为 0），供剧本做车轮战/伤势分支」
（`VNBattleModule` 类注释，`[Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs:25](Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs#L25)`）。

---

### 13.4 商店 VNShopModule

**UI**：暗幕 + 居中面板（锚点 0.24~0.76 横向、0.1~0.9 纵向）+ 标题 + 右上所持金 + 买/卖两个页签 + 商品列表 + 离开按钮。

```csharp
_panel.anchorMin = new Vector2(0.24f, 0.1f);
_panel.anchorMax = new Vector2(0.76f, 0.9f);
_panel.offsetMin = Vector2.zero;
_panel.offsetMax = Vector2.zero;
_panel.localScale = Vector3.one * 0.92f;
_panel.DOScale(1f, 0.28f).SetEase(Ease.OutBack)
      .SetUpdate(true).SetLink(gameObject);
```
> 出处：[`VNShopModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:147](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L147)）

> **「锚点比例 + offset 归零」是做居中面板的正确姿势**：
> 分辨率变化时面板按比例缩放，不需要任何自适应代码。
> 对比「固定 sizeDelta + 居中锚点」的做法——在超宽屏上面板会显得太小。
> 本专案两种都用：内容量固定的（战斗、结算）用固定尺寸，
> 内容量可变的（商店、排程）用比例锚点。

**数据全在 flag**：
```csharp
/// 事件模块：商店（买入/卖出）。金钱走养成属性（VNStatsHud 钳制+飘字），
/// 持有数走 flag「道具_<id>」——存档/if 分支零改动复用。
```
> 出处：`VNShopModule` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:11](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L11)）

**只登记一家商店时 id 可省**：
```csharp
if (_shop == null && shops.Count == 1 && string.IsNullOrEmpty(id))
    _shop = shops[0]; // 只登记了一家时 id 可省略
```
> 出处：`VNShopModule.OnLaunch`（[Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:49](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L49)）

同样的「只有一套时可省 id」在问答模块的文档里也有
（[`VNQuizModule`](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs) 类注释，`[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:22](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L22)`）。

---

### 13.5 限时问答 VNQuizModule

**UI**：面板 + 题干 + 2~4 个选项按钮 + 倒计时条。

**四阶段状态机**：
```csharp
enum Phase { Idle, Asking, Feedback, Ending }
```
> 出处：`VNQuizModule.Phase`（[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:53](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L53)）

比 QTE 多一个 `Feedback`——答完要停一下显示对错再进下一题。

**「最后冲刺」演出**：
```csharp
/// <summary>剩余时间进入这个秒数后开始"最后冲刺"演出（变红/脉动/轻抖）</summary>
const float UrgentSeconds = 3f;
```
> 出处：`VNQuizModule.UrgentSeconds`（[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:44](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L44)）

同一个概念在大头贴（`VNPhotoBoothModule.UrgentSeconds`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:118`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L118)），
`[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:118](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L118)`，值 3）
与擦雾（`VNFogWipeModule.UrgentSeconds`（[`Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:65`](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L65)），
`[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:65](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L65)`，值 5）里各有一份。

> **三个模块各自定义了同名常量，值还不一样**（3/3/5）。
> 这不算错（擦雾的节奏本来就慢一些），但如果以后要统一「紧急感」的表现，
> 会发现要改三处。**可以考虑抽一个 `VNUrgency` 静态类**，
> 提供 `IsUrgent(timeLeft, threshold)` 与统一的颜色/脉动参数。
> 不过在只有三处的情况下，重复是可以接受的。

**成绩写 flag**：
```csharp
/// <summary>成绩 flag 后缀（前缀由题库 flagPrefix / 剧本 flag: 决定）</summary>
public const string CorrectFlagSuffix = "正确数";
public const string TotalFlagSuffix = "总数";
```
> 出处：`VNQuizModule.CorrectFlagSuffix`（[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:40](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L40)）

**结果延迟 1.4 秒**（比 QTE 的 0.8 长，因为要看最终成绩）：
```csharp
DOVirtual.DelayedCall(1.4f, () => Done(outcome), true).SetLink(gameObject);
```
> 出处：`VNQuizModule` 结算段（[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:358](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L358)）

---

### 13.6 周日程排程 VNPlanModule

**两种用法共用一个模块**：
```
① 排程面板（op 省略）：左侧候选行动、右侧 N 个日程格
② 逐格派发（op:next，无 UI 秒回）：当前格 +1，把该格行动编号抄进 flag「当前行动」
```
> 出处：[`VNPlanModule`](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs:11](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs#L11)）

**派发模式不进回想**（`_dispatchMode` 字段，
`[Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs:59](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs#L59)`，
注释「op:next 纯流程派发（不记回想，一周会调 N 次）」）——
这就是 `RecordInBacklog` 存在的原因。

**皮肤与程序化的双路径，弹入动画共用**：
```csharp
var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.planPrefab);
_skin = VNSystemUiSkinUtility.Instantiate<VNPlanSkin>(
    skinPrefab, transform, "VNPlanModule");
if (_skin != null) BuildFromSkin(titleText);
else BuildDefault(titleText);

RefreshSlots();

// 面板弹入（两条路径共用）
_panel.localScale = Vector3.one * 0.92f;
_panel.DOScale(1f, 0.28f).SetEase(Ease.OutBack)
      .SetUpdate(true).SetLink(gameObject);
```
> 出处：`VNPlanModule.BuildUi`（[Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs:234](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs#L234)）

> **这是「双路径构建」的正确写法**：
> 两条路各自负责「把 `_panel` 指向正确的对象」，
> 之后的公共处理（弹入动画、刷新数据）写一遍。
> 对比 `VNStatsHud.BuildPanel`（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:460](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L460)`）
> 是皮肤路径 `return` 掉、程序化路径接着写——那样公共尾部就得写两遍。
> Plan 的写法更好。

**flag 契约暴露成常量 + 一个函数**：
```csharp
public const string SlotFlagPrefix = "日程_";
public const string CountFlag = "日程数";
public const string CursorFlag = "当前格";
public const string ActionFlag = "当前行动";

/// <summary>第 slot 格（1 起）的 flag 名</summary>
public static string SlotFlagName(int slot) => SlotFlagPrefix + slot;
```
> 出处：`VNPlanModule.SlotFlagPrefix`（[Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs:35](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs#L35)）
> 与 `VNPlanModule.SlotFlagName`（[Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs:41](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs#L41)`）

> **把 flag 名做成公开常量**，让剧本编辑器的下拉候选、Lint 检查、
> 其他模块都能引用同一份定义，比到处硬编码 `"日程_" + n` 好得多。

---

### 13.7 结算弹窗 VNResultPopupModule

**四档等级的样式表**：
```csharp
class GradeStyle
{
    public string bigLabel;
    public string localeKey;
    public Color color;
    public bool burst;
}

static GradeStyle StyleOf(string grade)
{
    switch (grade)
    {
        case "fail": return new GradeStyle { bigLabel = "FAIL…", ... burst = false };
        case "good": return new GradeStyle { bigLabel = "GOOD!", ... burst = true };
        case "great": return new GradeStyle { bigLabel = "GREAT!!", ... burst = true };
        default: return new GradeStyle { bigLabel = "OK", ... burst = false };
    }
}
```
> 出处：[`VNResultPopupModule.StyleOf`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:27](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L27)）

**参数校验后降级而不是报错退出**：
```csharp
_grade = ctx.Kw("grade", "normal");
if (_grade != "fail" && _grade != "normal" && _grade != "good" && _grade != "great")
{
    Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：result 等级「{_grade}」无效；应为 fail/normal/good/great，已按 normal 处理");
    _grade = "normal";
}
```
> 出处：`VNResultPopupModule.OnLaunch`（[Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:67](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L67)）

**「防连点误触」的输入延迟**：
```csharp
[Header("大字揭晓后多少秒才接受点击（防止连点误触）")]
public float inputDelay = 0.4f;
```
> 出处：`VNResultPopupModule.inputDelay`（[Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:13](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L13)）

`_shownAt = float.MaxValue`（`VNResultPopupModule.OnLaunch`（[`Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:64`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L64)），
`[Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:77](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L77)`）
是一个漂亮的初始化技巧——在冲条演出结束前，
`Time.unscaledTime - _shownAt` 永远是负数，点击永远不被接受。

**皮肤槽位的「有则播、无则跳」**：
```csharp
/// 只有 panelRoot 和 gradeText 必需，
/// 其余全可选降级。判定冲条三槽（barRoot/barFill/percentText）齐全才播
/// 悬念演出，否则直接揭晓大字。
```
> 出处：[`VNResultPopupSkin`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupSkin.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNResultPopupSkin.cs:9](Assets/Project/Scripts/VNEffects/Script/VNResultPopupSkin.cs#L9)）

> **「三个槽位齐全才播某段演出」是很实用的降级粒度**。
> 比「全有或全无」灵活，又比「每个槽位单独判断」好写。
> 判断标准：**这几个槽位是不是一个不可分割的功能单元**。

---

### 13.8 委托板 VNQuestBoardModule

**它是唯一一个明确要求模板带 RectTransform 的模块**：
```csharp
_root = transform as RectTransform;
if (_root == null)
{
    Debug.LogError("[VNQuestBoard] 模块模板必须带 RectTransform（走场景装机菜单添加）");
    Done(OutcomeLeft);
    return;
}
```
> 出处：[`VNQuestBoardModule.OnLaunch`](Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs:39](Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs#L39)）

装机器里也有对应的注释：
```csharp
// ★ 必须带 RectTransform：模块 OnLaunch 里直接 (RectTransform)transform
var go = new GameObject(TemplateName, typeof(RectTransform));
```
> 出处：[`VNQuestBoardInstaller.Install`](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs:58](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs#L58)）

> **`new GameObject("X")` 默认挂的是普通 `Transform` 不是 `RectTransform`**，
> 这在手工建模板时极容易漏。本专案的处理是「代码里检查 + 报错指向装机菜单」，
> 而不是自动补一个——因为在事件层里没有 RectTransform 的物体位置会完全错乱，
> 与其静默修复不如明确报错。

**与自动接取共用判定**：
```csharp
/// 列出「未接取 + 前置完成 + 出现条件满足 + 不在冷却 + 未达次数上限」的任务
/// （判定与自动接取共用 VNQuestEngine.CanAccept，不会出现「板上有但接不了」）。
```
> 出处：`VNQuestBoardModule` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs:15](Assets/Project/Scripts/VNEffects/Script/VNQuestBoardModule.cs#L15)）

> **「显示条件」与「执行条件」必须是同一个函数**，
> 否则一定会出现「按钮亮着但点了没反应」。这是 UI 设计的铁律。
> 本专案在选项 `cost:` 上也是这么做的：
> `CanAfford`（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:185](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L185)`）
> 决定置不置灰，`ApplyCost`（`:206`）真扣，两者共用 `ParseCostOp`（`:170`）。

---

### 13.9 羽毛球 VNBadmintonModule（四层拆分的范例）

这是全专案最复杂的模块（45K + 20K + 23K + 5K + 16K），也是分层做得最好的。

**四层拆分**：

| 层 | 文件 | 职责 | 依赖 |
|---|---|---|---|
| 纯数学 | [`VNBadmintonBallistics`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonBallistics.cs) | 三点定抛物线、落点抽样、精准判定 | 无 MonoBehaviour，可单测 |
| 视觉 | [`VNBadmintonCourt`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs:21](Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs#L21)） | 程序化球场与 HUD | 不是 MonoBehaviour |
| 表现 | [`VNBadmintonActor`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonActor.cs) | 角色六态假动画 | 换真动画只改这个文件 |
| UI 辅助 | [`VNBadmintonUi`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:44](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L44)） | CreateImage / CreateText / CreateQuad / CreateLine | 静态类 |
| 逻辑 | [`VNBadmintonModule`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:28](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L28)） | 状态机、AI、判定 | 持有上面全部 |

**`VNBadmintonCourt` 不是 MonoBehaviour**：
```csharp
/// 程序化球场与 HUD。**不是 MonoBehaviour**——由 VNBadmintonModule 构造并持有，
/// 只负责「画」和「播 HUD 动画」，一条玩法逻辑都不放。
```
> 出处：`VNBadmintonCourt` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs:10](Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs#L10)）

> **「视觉层不是 MonoBehaviour」是一个很值得学的选择**。
> 好处：不会被误加 Update、不会被 Inspector 干扰、
> 构造/销毁完全由持有者控制、可以在纯 C# 测试里 new 出来（虽然会缺 GameObject）。
> 代价：不能用 `[SerializeField]` 在 Inspector 里调参——
> 但这个模块的参数都在 `VNBadmintonTuning`（`VNBadmintonModule.tuning`（[`Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:34`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L34)），
> `[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:34](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L34)`）与 Def 资产里，所以不需要。

**坐标系统一到「底边中心」**：
```csharp
/// 坐标系：所有内容挂在 Root 下，Root 锚在画布**底边中心**，
/// 于是子物体的 anchoredPosition 与《羽毛球小游戏实施计划.md》第四节换算表一一对应。
```
> 出处：`VNBadmintonCourt` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs:12](Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs#L12)）

辅助函数：
```csharp
/// <summary>把节点锚到「底边中心」——羽球全场统一用这套坐标（与换算表一致）</summary>
public static RectTransform AnchorBottomCenter(RectTransform rect)
{
    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
    rect.pivot = new Vector2(0.5f, 0.5f);
    return rect;
}
```
> 出处：`VNBadmintonUi.AnchorBottomCenter`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:118](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L118)）

布局常量全在类顶部（`VNBadmintonCourt.SkyBottomY`（[`Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs:24`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs#L24)） 起，
`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs:24](Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs#L24)` 到 `:34`），
注释写明「由参考截图逐像素量出并折算到 1920×1080」
（`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs:23](Assets/Project/Scripts/VNEffects/Script/VNBadmintonCourt.cs#L23)`）。

> **「一套坐标系 + 一张换算表 + 全部常量集中」是做程序化 UI 的关键**。
> 如果每个子物体用不同的锚点，改一个位置要在脑子里做三次坐标变换。
> 本专案把所有东西锚到同一个原点，anchoredPosition 就是设计稿上的坐标。

**画梯形：自定义 MaskableGraphic**

uGUI 的 `Image` 只能画矩形，球场的透视地面需要梯形，所以自己写了一个：
```csharp
[RequireComponent(typeof(CanvasRenderer))]
public class VNBadmintonQuad : MaskableGraphic
{
    public Vector2 bottomLeft, bottomRight, topRight, topLeft;

    public void SetCorners(Vector2 bl, Vector2 br, Vector2 tr, Vector2 tl)
    {
        bottomLeft = bl; bottomRight = br; topRight = tr; topLeft = tl;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var c = color;
        vh.AddVert(bottomLeft, c, new Vector2(0f, 0f));
        vh.AddVert(topLeft, c, new Vector2(0f, 1f));
        vh.AddVert(topRight, c, new Vector2(1f, 1f));
        vh.AddVert(bottomRight, c, new Vector2(1f, 0f));
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }
}
```
> 出处：`VNBadmintonQuad`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:17](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L17)）

**类注释里记了一个非常隐蔽的坑**：
```csharp
/// ★ [RequireComponent(CanvasRenderer)] 必须**在子类上再写一遍**：
///   Graphic 基类上的那条不会被 AddComponent 走继承链读到，
///   少了它 CanvasRenderer 不会被自动补上，结果是「一切状态都对但就是不画」。
```
> 出处：`VNBadmintonQuad` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:12](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L12)）

而且创建时还显式补了一次：
```csharp
public static VNBadmintonQuad CreateQuad(string name, RectTransform parent, Color color)
{
    var rect = CreateNode(name, parent);
    rect.gameObject.AddComponent<CanvasRenderer>();   // 显式补，别依赖 RequireComponent
    var quad = rect.gameObject.AddComponent<VNBadmintonQuad>();
    ...
}
```
> 出处：`VNBadmintonUi.CreateQuad`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:65](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L65)）

> **这是我在这个专案里看到的最有价值的一条 Unity 冷知识**。
> `RequireComponent` 不走继承链：你继承 `Graphic`（它有 `[RequireComponent(typeof(CanvasRenderer))]`），
> 但 `AddComponent<你的类>()` 不会自动加 `CanvasRenderer`。
> 症状是「组件在、颜色对、rect 对，就是什么都不画」，极难联想到原因。
> **任何自定义 `Graphic` / `MaskableGraphic` 子类都要自己写一遍 `RequireComponent`。**

**画线 = 旋转的细长矩形**：
```csharp
/// <summary>两点之间的一条线（旋转的细长矩形）。粗细单位 px。</summary>
public static RectTransform CreateLine(string name, RectTransform parent,
    Vector2 from, Vector2 to, float thickness, Color color)
{
    var rect = CreateImage(name, parent, null, color);
    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
    rect.pivot = new Vector2(0f, 0.5f);
    Vector2 delta = to - from;
    rect.anchoredPosition = from;
    rect.sizeDelta = new Vector2(delta.magnitude, thickness);
    rect.localRotation = Quaternion.Euler(0f, 0f,
        Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    return rect;
}
```
> 出处：`VNBadmintonUi.CreateLine`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:76](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L76)）

`pivot = (0, 0.5)` 是关键——让矩形从起点向右延伸，旋转时绕起点转。

**认输确认框：模块内的小型模态**
```csharp
void OpenConfirm()
{
    if (_confirmOpen) return;
    _confirmOpen = true;

    var root = (RectTransform)transform;
    _confirmPanel = VNBadmintonUi.CreateImage("QuitConfirm", root, null,
        new Color(0f, 0f, 0f, 0.55f));
    VNBadmintonUi.Stretch(_confirmPanel);
    _confirmPanel.GetComponent<Image>().raycastTarget = true;   // 挡住底下的点击
    ...
}
```
> 出处：`VNBadmintonModule.OpenConfirm`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:495](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L495)）

冻结整局的方式与 [`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) 一致（在 ReadInput 之前 return）：
```csharp
if (_confirmOpen) { TickConfirm(); return; }
```
> 出处：`VNBadmintonModule.Update`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:428](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L428)）

`TickConfirm` 只读键盘（Y/Enter = 是，N/Esc = 否，
`VNBadmintonModule.TickConfirm`，`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:556](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L556)`），
按钮的鼠标点击走 uGUI 的 Button（`VNBadmintonModule.MakeConfirmButton`，
`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:541](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L541)`）。

> **「键盘走轮询、鼠标走 EventSystem」是本专案模块的通行做法**。
> 键盘不需要射线检测，轮询最简单；鼠标要判断点在哪个按钮上，用 Button 最省事。
> 两套并存不冲突，因为它们的输入源不同。

**Editor-only 的实时调参**：
```csharp
#if UNITY_EDITOR
// 决策 10 没做 Editor 调参窗口的补偿：Play 着直接拖 Def 资产的 Inspector
// 就能实时看到手感变化，不用反复进出 Play Mode。运行时构建整段编译掉。
if (_def != null) ApplyTuning();
#endif
```
> 出处：`VNBadmintonModule.Update`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:420](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L420)）

> **这是「用 `#if UNITY_EDITOR` 换开发效率」的正当用法**：
> 每帧重读一次资产参数在编辑器里成本可忽略，能省下大量的进出 Play Mode。
> 打包时整段消失，不影响性能。
> 对比擦雾模块的做法——专门写了一个调参窗口（13.11），
> 那是因为擦雾的参数需要「不进 Play Mode 也能试」。

---

### 13.10 大头贴 VNPhotoBoothModule（87K，全专案最大的 UI）

**布局尺寸全部常数化在文件头**（`VNPhotoBoothModule.ViewW`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:98`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L98)） 起，
`[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:98](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L98)`）：

| 常量 | 值 | 含义 | 行号 |
|---|---|---|---|
| `ViewW` / `ViewH` | 1040 / 780 | 取景框（4:3，照片就是这块） | `[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:98](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L98)` |
| `ViewY` | 22 | 取景框中心纵向位置 | `:101` |
| `MachineW` / `MachineH` | 1860 / 1020 | 机身 | `:102` |
| `PanelW` / `PanelH` | 340 / 900 | 左右侧栏 | `:107` |
| `ListW` / `ListH` | PanelW-16 / PanelH-90 | 栏内滚动列表 | `:112` |
| `TitleY` | MachineH*0.5-58 | 标题条基线 | `:117` |
| `FaceCellSize` | 146 | 表情格 | `:123` |

而且注释里写了加法验算：
```csharp
// 左右侧栏统一尺寸。三块（左栏 / 取景框 / 右栏）横向排开：
// 机身内边距 30 + 栏 340 + 间隙 40 + 取景框 1040 + 间隙 40 + 栏 340 + 30 = 1860
const float PanelW = 340f;
```
> 出处：`VNPhotoBoothModule.PanelW`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:105](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L105)`）

> **把「布局怎么加出来的」写成注释里的算式**，
> 是程序化 UI 最有效的自文档手段。
> 以后要把取景框加宽 100px，一眼就知道机身也要跟着加 100。

**层序是这个模块的核心难点**：
```csharp
// ★ 开窗内的层序（从后往前）：背景 → 拖动板 → 人后贴纸 → 我 → 她
//   拖动板压在最底下是刻意的：它 raycastTarget=true 会吃掉射线，
//   放上面的话「人后贴纸」就永远点不到了。人物本身不接收射线，不用担心被挡。
```
> 出处：`VNPhotoBoothModule.BuildViewFinder`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:535](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L535)）

完整的取景框层序（`VNPhotoBoothModule.BuildViewFinder`，
`[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:521](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L521)` 起）：
```
ViewFinder
├── FrameBack        边框的背层
├── Window (Mask)    人物开窗，形状可以非矩形
│   ├── Backdrop     背景（被开窗裁切）
│   ├── DragPad      拖动板（raycastTarget=true，压最底）
│   ├── BackStickerLayer  人后贴纸
│   ├── MePortrait   我
│   └── HerPortrait  她
├── WindowRing       描边环（必须在 Mask 外，否则被自己裁掉）
├── FrameFront       边框的前层（压在人身上）
├── Watermark        水印
├── StickerLayer     人前贴纸（可以压到边框上）
└── DoodleLayer      涂鸦（画在「洗好的照片」上，盖住一切）
```

**「描边环必须在 Mask 外面，否则会被自己裁掉」**
（`VNPhotoBoothModule.BuildViewFinder`，
`[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:558](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L558)`）——
uGUI 的 `Mask` 会裁掉所有子物体，包括「本来想画在边缘上」的描边。

**人后贴纸层的尺寸取巧**：
```csharp
// 人后贴纸层：尺寸与取景框一致（超出开窗的部分被 Mask 裁掉，正好），
// 这样它与人前贴纸层坐标系相同，翻层时贴纸不会跳位
_backStickerLayer = VNPhotoBoothUi.CreateNode("BackStickerLayer", _window);
VNPhotoBoothUi.Center(_backStickerLayer, new Vector2(ViewW, ViewH), Vector2.zero);
```
> 出处：`VNPhotoBoothModule.BuildViewFinder`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:548](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L548)）

> **「两个层坐标系相同 → 换父物体时不跳位」** 是一个很实用的技巧。
> 双击贴纸在人前/人后之间翻转时，只需要 `SetParent`，`anchoredPosition` 原样保留。
> 如果两层尺寸不同，就得每次做坐标换算。

**说明卡片最后建 = 压在所有面板之上**：
```csharp
BuildTitleBar(machineRect);
BuildViewFinder(machineRect);
BuildLeftPanel(machineRect);
BuildRightPanel(machineRect);
BuildBottomBar(machineRect);
BuildHelpPanel(machineRect);   // 最后建 = 展开时压在所有面板之上
```
> 出处：`VNPhotoBoothModule.BuildUi`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:350](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L350)）

**说明卡片要进拍照的隐藏列表**：
```csharp
/// <summary>
/// 说明卡片整组（钮 + 卡片）。拍照时要整组藏掉——
/// 卡片是往左下展开的，会盖住取景框右上角，不藏就会入镜。
/// </summary>
GameObject _helpRoot;
```
> 出处：`VNPhotoBoothModule._helpRoot`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:421](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L421)）

#### 贴纸拖拽组件 VNPhotoStickerItem

```csharp
public class VNPhotoStickerItem : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
```
> 出处：[`VNPhotoStickerItem`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs)（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:17](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L17)）

**混合输入模型**：
```csharp
/// 拖动用 uGUI 事件接口（不需要读键盘），缩放旋转要读滚轮，
/// 所以走新版 Input System 的 Mouse.current —— 项目禁用旧版 Input API。
```
> 出处：`VNPhotoStickerItem` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:13](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L13)）

滚轮处理靠 hover 标志 + Update 轮询：
```csharp
public void OnPointerEnter(PointerEventData e) => _hover = true;
public void OnPointerExit(PointerEventData e) => _hover = false;

void Update()
{
    if (locked || !_hover) return;
    var mouse = Mouse.current;
    if (mouse == null) return;

    float wheel = mouse.scroll.ReadValue().y;
    if (Mathf.Abs(wheel) < 0.01f) return;

    var keyboard = Keyboard.current;
    bool shift = keyboard != null &&
                 (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

    if (shift) { _rotation += Mathf.Sign(wheel) * 12f; ... }
    else { _scale = Mathf.Clamp(_scale + Mathf.Sign(wheel) * 0.12f, MinScale, MaxScale); ... }
    onChanged?.Invoke();
}
```
> 出处：`VNPhotoStickerItem.Update`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:92](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L92)）

> **uGUI 的 `IScrollHandler` 其实存在**，但它需要 EventSystem 的 InputModule 正确转发滚轮，
> 而且 ScrollRect 会抢走事件。用「hover 标志 + 自己读 Mouse」绕开这些麻烦，
> 在这个场景下是合理的取舍。
>
> **代价**：`Update` 在每个贴纸上都跑（同屏几十张贴纸 = 几十个 Update）。
> 前两行 `if (locked || !_hover) return;` 让开销接近零，可以接受。
> 但如果贴纸能上百，应该改成「模块统一在一个 Update 里处理当前 hover 的那一个」。

**拖动的坐标换算**：
```csharp
public void OnDrag(PointerEventData e)
{
    if (locked || _canvasRect == null) return;
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, e.position, e.pressEventCamera, out Vector2 local))
        return;
    _rect.anchoredPosition = Clamp(local);
    onChanged?.Invoke();
}
```
> 出处：`VNPhotoStickerItem.OnDrag`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:66](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L66)）

**`e.pressEventCamera` 而不是 `Camera.main`** 是关键——
它是 EventSystem 记录的、发起这次点击的那个 Raycaster 的相机，
Overlay 画布下是 null、Camera 画布下是对应相机，自动正确。

**右键删除、双击翻层**：
```csharp
public void OnPointerClick(PointerEventData e)
{
    if (locked) return;
    if (e.button == PointerEventData.InputButton.Right)
    {
        onDelete?.Invoke(this);
        return;
    }
    // 双击 = 钻到人物背后 / 回到人物前面
    if (e.button == PointerEventData.InputButton.Left && e.clickCount >= 2)
        onToggleLayer?.Invoke(this);
}
```
> 出处：`VNPhotoStickerItem.OnPointerClick`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:79](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L79)）

`e.clickCount` 是 EventSystem 免费提供的双击检测，不需要自己记时间戳。

#### 涂鸦 VNPhotoDoodle：两张位图画布

```csharp
/// 【为什么用位图而不是矢量线段】
/// 需求里有「擦除」。位图擦除就是把 alpha 抹掉，一行代码；矢量要么只能整笔删除，
/// 要么得再叠一层遮罩去挖洞——同样的效果，复杂度差一个量级。
///
/// 【为什么是两张画布】
/// 荧光笔要发光，走的是 VN/Additive（Blend SrcAlpha One + HDR _TintColor），
/// 普通笔要正常的 Alpha 混合——两种混合模式没法在同一张图里共存，
/// 所以普通笔与荧光笔各占一张，叠着显示。一笔只会落在其中一张上，
/// 撤销快照因此也只需要存被改动的那一张（橡皮除外，它两张一起擦）。
///
/// 画布分辨率 768×576，显示时拉伸到取景框（1040×780）——放大倍率控制在 1.35x
/// 以内，笔刷自带柔边就看不出马赛克；换来的是每帧 Apply 只要半毫秒、
/// 撤销快照只要 1.7MB。取景框再放大的话这两个数要一起调。
```
> 出处：[`VNPhotoDoodle`](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:53](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L53)）

尺寸常量：`Width = 768` / `Height = 576`
（`[Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:70](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L70)`），
撤销步数 `MaxUndo = 5`（`:74`，注释「一步最多 3.4MB（橡皮会同时动两张画布）」）。

撤销快照只存被动过的那一张：
```csharp
/// <summary>一次撤销记录：只存这一笔真正动过的那张画布</summary>
class Snapshot
{
    public Color32[] normal;   // null = 这一笔没动普通层
    public Color32[] glow;
}
```
> 出处：`VNPhotoDoodle.Snapshot`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:91](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L91)）

**输入板只在涂鸦页才吃射线**：
```csharp
/// 涂鸦画布的输入板：把指针位置换成画布 uv 交给 VNPhotoDoodle。
/// 只在「涂鸦」标签页打开时接收射线，别的时候完全透明不挡人物与贴纸的操作。
```
> 出处：`VNPhotoDoodleInput` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:9](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L9)）

屏幕坐标 → 画布 uv：
```csharp
bool ToUv(PointerEventData e, out Vector2 uv)
{
    uv = Vector2.zero;
    if (_rect == null) _rect = (RectTransform)transform;
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rect, e.position, e.pressEventCamera, out Vector2 local))
        return false;

    var size = _rect.rect.size;
    if (size.x <= 0f || size.y <= 0f) return false;
    uv = new Vector2(local.x / size.x + 0.5f, local.y / size.y + 0.5f);
    return true;
}
```
> 出处：`VNPhotoDoodleInput.ToUv`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:36](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L36)）

`+ 0.5f` 是因为 `ScreenPointToLocalPointInRectangle` 返回的局部坐标以 pivot 为原点，
而 uv 要以左下为原点。

#### 拍照 VNPhotoCapture：整个「怎么拍」封在一个文件

```csharp
/// 做法与参考实现一致：算出取景框在屏幕上的矩形 → 等这一帧画完 → 整屏抓图 → 裁剪。
/// 好处是照片天然带上 URP 的 Bloom / Vignette（大头贴要的就是这个味道），
/// 代价是必须保证快门那一帧取景框上没有别的 UI —— 调用方传 hideDuringShot，
/// 抓图前一帧把左右装扮栏、倒数数字统统关掉，抓完再开回来。
///
/// ★ 整个「怎么拍」都关在这个文件里：以后想改成独立 Camera + RenderTexture
///   （分辨率不再受窗口大小限制），只改这里，模块不用动。
```
> 出处：[`VNPhotoCapture`](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:11](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L11)）

**隐藏 UI 的「只恢复本来就开着的」**：
```csharp
var restore = new List<GameObject>();
if (hideDuringShot != null)
    foreach (var go in hideDuringShot)
    {
        if (go == null || !go.activeSelf) continue;
        go.SetActive(false);
        restore.Add(go);
    }
```
> 出处：`VNPhotoCapture.Capture`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:38](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L38)）

**等两帧才抓**：
```csharp
// 2) 关掉之后要等一帧，Canvas 才会真正重画（否则抓到的还是旧画面）
yield return null;
yield return new WaitForEndOfFrame();
```
> 出处：`VNPhotoCapture.Capture`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:47](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L47)）

**`try / finally` 保证一定恢复**：
```csharp
finally
{
    if (full != null) UnityEngine.Object.Destroy(full);
    foreach (var go in restore) if (go != null) go.SetActive(true);
}
```
> 出处：`VNPhotoCapture.Capture`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:70](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L70)）

**DPI 缩放的换算**：
```csharp
// 抓图尺寸可能因为 DPI 缩放与 Screen.width 不一致，按比例换算
float sx = (float)full.width / Screen.width;
float sy = (float)full.height / Screen.height;
```
> 出处：`VNPhotoCapture.Crop`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:104](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L104)）

> **`ScreenCapture.CaptureScreenshotAsTexture()` 在高 DPI 屏上返回的尺寸
> 可能是 `Screen.width` 的 2 倍**（Retina / Windows 缩放）。
> 不做换算的话裁出来的区域会偏到左下角四分之一。这是很容易漏的一步。

**上限保护**：
```csharp
/// <summary>照片最大边长上限，防止 4K 屏拍出巨大的 PNG</summary>
public const int MaxSide = 1600;
```
> 出处：`VNPhotoCapture.MaxSide`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:22](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L22)）

---

### 13.11 擦雾 VNFogWipeModule

**「别在 event 之前先 cg」是这个模块最重要的一条**：
```csharp
/// 【为什么不能先 cg】
/// 雾要到 OnLaunch 才铺得出来。剧本先 cg 的话，从 cg 的转场开始到事件真正启动为止，
/// 清晰的画面一直摆在玩家眼前——谜底在开始擦之前就已经揭晓，整个玩法的前提就没了
/// （中间再夹一句台词，还得等玩家点一下，暴露得更久）。交给模块自己铺底图，
/// 进事件的第一帧就是盖满雾的状态。Lint 的 fogwipe-cg-before-event 会盯着这件事。
/// 事件结束与下一条 cg 在同一帧交接（Runner 的 Destroy 是帧末延迟），不会闪。
```
> 出处：[`VNFogWipeModule`](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:23](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L23)）

> **这是「UI 时序影响玩法成立性」的经典案例**。
> 单看代码这只是一个参数选择，实际上是玩法的生死线。
> 而且解法很完整：① 提供 `cg:` 参数让模块自己铺；
> ② 写进类注释；③ 写进静态检查器（Lint）。
> **凡是「写错了游戏就不好玩但不会报错」的约定，都值得加进 Lint。**

**自己铺一份清晰 CG 打底 + 自绘台词条**：
```csharp
/// 【为什么自己铺一份清晰 CG】
/// 事件层排序 60 在对话框 40 之上，雾必须铺满整屏，于是必然盖住对话框。
/// 与其让底下露出半截对话框，不如模块自己铺一份清晰 CG 打底，画面完全自洽；
/// 过程中的台词也因此改用自绘的台词条（贴着画面下缘，像镜面上的旁白）。
```
> 出处：`VNFogWipeModule` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:47](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L47)）

`_lineBar` 就是那条自绘台词条（`VNFogWipeModule._lineBar`，
`[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:127](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L127)`），
尺寸 1240×132 贴在画面下缘（`VNFogWipeModule.BuildHud`，
`[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:770](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L770)`）。

**雾用 RawImage + 自定义材质 + 掩码纹理**：
```csharp
_fogImage = _fogRect.gameObject.AddComponent<RawImage>();
_fogImage.texture = texture;
_fogImage.uvRect = uvRect;
_fogImage.raycastTarget = false;
```
> 出处：`VNFogWipeModule.BuildUi`（[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:619](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L619)）

shader 参数 id 全部缓存成 static readonly int
（`VNFogWipeModule.IdMaskTex`（[`Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:76`](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L76)） 起，
`[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:76](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L76)` 到 `:86`）——
`Shader.PropertyToID` 每次调用都要做字符串哈希，缓存是标准做法。

**结算按历史峰值而不是结束瞬间**：
```csharp
float peak = _score.Peak;
```
> 出处：`VNFogWipeModule` 结算段（[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:406](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L406)`
> 与 `:457`）

HUD 上同时画峰值刻度：
```csharp
_peakMark = NewChild("PeakMark", barBg);
...
peakImage.color = PeakColor;
```
> 出处：`VNFogWipeModule.BuildHud`（[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:745](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L745)）

> **「按峰值判定 + UI 上显示峰值」是很好的玩家友善设计**。
> 时限到那一帧刚好被雾吞一口就从完美掉到普通，玩家会觉得不公平。
> 而且 UI 上画出峰值刻度，玩家能看到「我最好的时候到过哪里」。

**四条退出路径都 `_cursor?.Dispose()`**
（`[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:455](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L455)` 与 `:588`，
加上 `CancelForDebug` 与 `OnDestroy`）——与互动模块同一条纪律。

**调参窗口是刚性需求**（第十九章会详讲）：
```csharp
/// 【为什么这个窗口是必需品而不是加分项】
/// 本玩法刻意不做速度惩罚（擦到就掉），难度的**唯一**来源就是
/// 「笔刷面积 vs 回雾速度」这两个数——手感全压在参数上，而参数不可能一次调对。
/// 每改一个数就进一次 Play Mode 试玩，一轮要一分钟；在这里改，是即时的。
```
> 出处：[`VNFogTuneWindow`](Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs:9](Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs#L9)）

---

### 13.12 亲密互动 VNInteractionModule

**不铺全屏暗幕**（这是它与其他模块最大的 UI 差别）：
```csharp
void BuildUi()
{
    var root = (RectTransform)transform;

    // 部位框可视化层：挂在立绘下面，跟着立绘一起动
    if (showZones) BuildZoneOverlay();

    // 左下角 HUD 面板（**不铺全屏暗幕**，否则会盖住对话框）
    _hud = CreateImage("Hud", root, VNProceduralTextures.RoundedRectSprite, PanelColor);
    ...
}
```
> 出处：[`VNInteractionModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:754](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L754)）

HUD 缩到左下角 420×108（`[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:767](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L767)`），
道具栏走右侧竖排：
```csharp
/// <summary>
/// 右侧竖排道具栏。**放右边不放底部**：底部是对话框的地盘，
/// 而互动过程中角色随时会说话。
/// </summary>
void BuildItemBar(RectTransform root)
```
> 出处：`VNInteractionModule.BuildItemBar`（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:814](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L814)）

> **「这块屏幕区域归谁」是模块 UI 设计的第一步**。
> 大多数模块可以独占全屏（暗幕盖掉一切），
> 但这个模块要让对话框继续工作，所以必须给底部让位。
> 这个约束直接决定了 HUD 与道具栏的位置。

**光标最后建 = 层级最上**：
```csharp
// 光标最后建 → 层级最上面，压在道具栏和结束钮之上
var cursorGo = new GameObject("TouchCursor", typeof(RectTransform));
cursorGo.transform.SetParent(root, false);
_cursor = cursorGo.AddComponent<VNTouchCursor>();
_cursor.Initialize(root, UiCamera);
_cursor.SetItem(_item);
```
> 出处：`VNInteractionModule.BuildUi`（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:802](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L802)）

**部位框可视化挂在立绘下面**：
```csharp
var go = new GameObject("ZoneOverlay", typeof(RectTransform));
_zoneOverlay = (RectTransform)go.transform;
_zoneOverlay.SetParent(_char.rect, false);   // 跟着立绘走（含缩放/位移）
Stretch(_zoneOverlay);
```
> 出处：`VNInteractionModule.BuildZoneOverlay`（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:968](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L968)）

> **因为它挂在立绘下面（不在模块子树里），模块销毁时不会被自动带走**——
> 必须显式删。这是「把 UI 挂到别人子树下」的必然代价。
> `CLAUDE.md` 里记了这条：「所以模块销毁时**必须显式删**，否则互动结束后框留在角色脸上」。

**部位框的坐标与运行时判定共用同一套语义**：
```csharp
m.anchoredPosition = new Vector2(z.center.x * size.x, z.center.y * size.y);
m.sizeDelta = new Vector2(z.size.x * size.x, z.size.y * size.y);
```
> 出处：`VNInteractionModule.RefreshZoneMarkers`（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:1000](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L1000)）

归一化坐标 × 立绘尺寸 = 局部坐标。编辑器画框工具用同一套换算（第十九章）。

#### 道具光标 VNTouchCursor：为什么不用系统光标

```csharp
/// **为什么不用 Cursor.SetCursor（系统硬件光标）**：硬件光标只能换图，
/// 做不了持续摆动、按住震动、跟随速度倾斜、悬停 HDR 发光这几件事，
/// 而这些恰恰是这个玩法的手感来源。代价是光标会受帧率影响、
/// 且必须自己保证退出时把系统光标还回去（见 Dispose）。
///
/// 层次：root 只负责跟随鼠标；图标的摆动/旋转/缩放全作用在子物体 Icon 上，
/// 两件事分开才不会互相打架（跟随每帧写位置，动画也每帧写位置）。
///
/// 动画不走 DOTween 循环而是 Update 里直接算正弦：光标每帧都要重定位，
/// 补间和逐帧写位置混在一起只会互相覆盖。
```
> 出处：[`VNTouchCursor`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:9](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L9)）

**「跟随」与「动画」分两层**是这段里最重要的设计
（`_root` 跟随、`_icon` 承载动画，
`VNTouchCursor._root` 与 `_icon`，
`[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:25](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L25)`）。
这与第二章讲的 SceneRoot / ZoomRoot / TiltRoot 分层是**同一条原则**：
不要让两个系统写同一个 Transform 字段。

**HDR 发光走材质而不是顶点色**：
```csharp
/// <summary>悬停发光色（HDR：>1 才会被 Bloom 抓到；uGUI 顶点色会被钳到 1，
/// 所以必须走 VN/Additive 材质的 _TintColor）</summary>
public Color hoverGlowColor = new Color(2.2f, 1.1f, 1.5f, 1f);
```
> 出处：`VNTouchCursor.hoverGlowColor`（[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:44](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L44)）

```csharp
_glowMat.SetColor(IdTintColor, c);   // HDR 走材质，顶点色会被钳到 1
```
> 出处：`VNTouchCursor.SetGlowAlpha`（[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:195](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L195)）

有退化路径：
```csharp
else
{
    var c = hoverGlowColor;
    c.a = a;
    _glowImage.color = c;                // 没有 VN/Additive 时的退化路径
}
```
> 出处：`VNTouchCursor.SetGlowAlpha`（[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:197](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L197)）

**Dispose 必须在四处调**：
```csharp
/// <summary>
/// 还原系统光标。**任何退出路径都必须走到这里** ——
/// 漏了的话玩家的鼠标指针会一直消失，是所有 bug 里最难受的一种。
/// </summary>
public void Dispose()
{
    if (_cursorHidden)
    {
        Cursor.visible = true;
        _cursorHidden = false;
    }
    if (_glowMat != null)
    {
        Destroy(_glowMat);
        _glowMat = null;
    }
}

void OnDestroy() => Dispose();
void OnDisable() => Dispose();     // 模块被禁用（调试中断）也要还回去
```
> 出处：`VNTouchCursor.Dispose`（[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:217](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L217)）

模块侧还有两处（`VNInteractionModule` 的 `Finish` 路径
`[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:584](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L584)`、
`CancelForDebug` `:660`、`OnDestroy` `:671`）。

**注意 `Dispose` 里也销毁了材质实例**——
`new Material(shader)` 创建的实例不会被 GC 自动回收，必须显式 Destroy。
同一件事在 [`VNTutorialMask.OnDestroy`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs)
（`[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:96](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L96)`）里也做了。

---

### 13.13 AI 自由聊天 VNAiTalkModule

**UI 极简**（因为台词走的是舞台的对话框、选项走的是舞台的选项面板）：
```csharp
// 自绘 UI（全部 raycastTarget=false）
RectTransform _hintRoot;
TextMeshProUGUI _turnLabel, _thinkingLabel;
RectTransform _confirmRoot;
Tween _thinkingTween;
```
> 出处：[`VNAiTalkModule._hintRoot`](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs) 等（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:82](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L82)）

只有三样东西：轮次标签、「思考中」标签、ESC 确认框。

```csharp
void BuildUi()
{
    _hintRoot = CreateRect("AiTalkHud", (RectTransform)transform);
    Stretch(_hintRoot);
    ...
}
```
> 出处：`VNAiTalkModule.BuildUi`（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:538](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L538)）

**唯一吃射线的是确认框暗幕**：
```csharp
dim.GetComponent<Image>().raycastTarget = true;   // 这一层就是要独占输入
```
> 出处：`VNAiTalkModule` 确认框构建段（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:595](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L595)）

**七阶段状态机**（最多的一个）：
```csharp
enum Phase { Booting, Thinking, Speaking, Pausing, Choosing, Confirming, Ending }
```
> 出处：`VNAiTalkModule.Phase`（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:61](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L61)）

因为它有网络请求这个异步环节，需要 `Thinking` 状态。

**选项飞入前的停顿**：
```csharp
[Header("选项飞入前的停顿（秒）：给眼睛一个缓冲，别紧贴着最后一个字弹出来")]
[Range(0f, 1.5f)] public float optionDelay = 0.4f;
```
> 出处：`VNAiTalkModule.optionDelay`（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:50](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L50)）

> **这种「给眼睛一个缓冲」的小停顿在本专案里出现了很多次**：
> 选项回调延迟 0.8 秒（[`VNChoicePanel.Choose`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs)）、
> 结算弹窗输入延迟 0.4 秒（`VNResultPopupModule.inputDelay`（[`Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:13`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L13)））、
> 教程每步最短 0.22 秒（`VNTutorialPlayer.MinStepTime`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:44`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L44)））、
> 这里的 0.4 秒。
> **它们都不是技术需要，纯粹是体验需要**——但少了就会明显「毛躁」。

**结果名是公开常量**：
```csharp
// 结果名（剧本「* 结果行」精确匹配这四个字符串）
public const string OutcomeUp = "好感提升";
public const string OutcomeNormal = "普通";
public const string OutcomeCold = "冷场";
public const string OutcomeFailed = "失败";
```
> 出处：`VNAiTalkModule.OutcomeUp`（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:42](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L42)）

Lint 就是靠这些常量检查「`* 失败` 有没有被接住」的。

---

### 13.14 秘密偷拍 VNSecretPhotoMode + VNSecretPhotoUi

**它不是事件模块**：
```csharp
/// 【它是什么】不是事件模块——不由剧本 event 启动，而是解锁后玩家随时（台词处）点右上角
/// 图标进入的**全局面板**，和 G 键画廊 / I 键背包同一层级。但它和那些面板不同：
/// 它**不盖住舞台，而是直接操控舞台的镜头**（ZoomRoot），所以与 aitalk / interact 一样
/// 是「刻意破一次模块三铁律」，边界收紧为：只碰镜头容器、UI 可见性、Ken Burns 开关；
/// 四条退出路径（正常退出 / ESC / 被发现 / 剧本 Stop·被销毁）全部还原。
```
> 出处：[`VNSecretPhotoMode`](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs:10](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs#L10)）

**UI 层挂主 Canvas，排序 70**：
```csharp
/// 【挂在主 Canvas 下而不是自建 Overlay 画布】
/// 教程（92）与全屏转场（100）都在主 Canvas 上；Overlay 画布会永远压在它们之上，
/// 教程的暗幕和挖洞就盖不住快门键。本层嵌套 Canvas 排序 70：
/// 在事件层（60）之上、教程（92）之下。
```
> 出处：[`VNSecretPhotoUi`](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs:17](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs#L17)）

**事件用 C# event 而不是直接调**：
```csharp
public event Action IconClicked;
public event Action ShutterClicked;
public event Action ExitClicked;
```
> 出处：`VNSecretPhotoUi.IconClicked`（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs:34](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs#L34)）

订阅在模式层：
```csharp
_ui = gameObject.AddComponent<VNSecretPhotoUi>();
_ui.Build(root.transform);
_ui.IconClicked += TryOpen;
_ui.ShutterClicked += Shoot;
_ui.ExitClicked += () => Close(false);
```
> 出处：`VNSecretPhotoMode.Initialize`（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs:80](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoMode.cs#L80)）

> **这是全专案唯一一处「UI 层用 C# event 向上通知」的写法**。
> 其他地方都是「UI 直接调持有者的方法」（`_runner?.RequestSavePanel()`）
> 或「构建时传 lambda」（`BindButton(skin.closeButton, Close)`）。
>
> **三种做法的取舍**：
> - **直接调持有者方法**：最简单，但 UI 层要认识持有者的类型（耦合）。
> - **传 lambda**：解耦好，但要在构建时就把所有回调准备好。
> - **C# event**：解耦好、可以后接、可以多播；代价是要记得退订。
>
> 这里用 event 是因为 `VNSecretPhotoUi` 是一个「纯视图组件」，
> 完全不认识 `VNSecretPhotoMode`。这个分离让 UI 层能被单独测试。
> **不过它没有退订**——因为两者生命周期完全一致（同一个 GameObject），
> 这是可以接受的简化。

**教程锚点在 Build 里登记、OnDestroy 里反注册**：
```csharp
VNTutorialAnchors.Register(AnchorIcon, (RectTransform)_iconRoot.transform);
VNTutorialAnchors.Register(AnchorShutter, _shutterRect);
VNTutorialAnchors.Register(AnchorAlert, _alertRect);
VNTutorialAnchors.Register(AnchorFilm, (RectTransform)_filmText.transform);
```
> 出处：`VNSecretPhotoUi.Build`（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs:96](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs#L96)）

```csharp
void OnDestroy()
{
    VNTutorialAnchors.Unregister(AnchorIcon);
    VNTutorialAnchors.Unregister(AnchorShutter);
    VNTutorialAnchors.Unregister(AnchorAlert);
    VNTutorialAnchors.Unregister(AnchorFilm);
    ...
}
```
> 出处：`VNSecretPhotoUi.OnDestroy`（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs:105](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs#L105)）

**抓图区域是一个空的铺满 RectTransform**：
```csharp
// 抓图区域：铺满整屏、无图形。VNPhotoCapture 按它算屏幕矩形
var cap = new GameObject("CaptureArea", typeof(RectTransform));
_captureArea = (RectTransform)cap.transform;
_captureArea.SetParent(_root, false);
Stretch(_captureArea);
```
> 出处：`VNSecretPhotoUi.Build`（[Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs:87](Assets/Project/Scripts/VNEffects/Script/VNSecretPhotoUi.cs#L87)）

> **「用一个空 RectTransform 表达一块区域」是很轻量的做法**。
> 不需要 Image、不吃射线、不进 batch，只是给 `GetWorldCorners` 提供一个几何定义。
> 大头贴那边是直接用取景框本身（有图形），这里因为要抓全屏所以单独建一个。

**图标显示条件由 Runner 判定**：
```csharp
/// <summary>右上角相机图标此刻该不该显示：没有别的面板/事件/标题盖着</summary>
public bool IsSecretPhotoIconAllowed()
{
    if (!_running || _eventActive) return false;
    if (_titleMenu != null && _titleMenu.IsOpen) return false;
    if (stage != null && stage.IsSnsOpen) return false;
    if (_uiHidden && !_uiHideLocked) return false; // 右键藏 UI 期间图标也一起藏
    if (_configPanel != null && _configPanel.IsOpen) return false;
    if (_saveLoadPanel != null && _saveLoadPanel.IsOpen) return false;
    if (_backlog != null && _backlog.IsOpen) return false;
    if (_questLog != null && _questLog.IsOpen) return false;
    if (_diaryPanel != null && _diaryPanel.IsOpen) return false;
    if (_statsHud != null && _statsHud.IsOpen) return false;
    if (_inventory != null && _inventory.IsOpen) return false;
    if (_cgGallery != null && _cgGallery.IsOpen) return false;
    return true;
}
```
> 出处：`VNScriptRunner.IsSecretPhotoIconAllowed`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1799](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1799)）

> **这个方法是「Runner 知道所有面板」这个中枢设计的直接产物**。
> 好处：一个方法就能表达「现在没有任何东西盖着屏幕」。
> 坏处：**加一个新面板要记得来这里补一行**，漏了就会出现「画廊开着但相机图标还在」。
>
> **这是一个明确的技术债**。改进方向：让所有模态面板实现一个
> `IVNModalPanel { bool IsOpen { get; } }`，Runner 持一个 `List<IVNModalPanel>`，
> 这个方法变成 `_modals.All(p => !p.IsOpen)`。加新面板只需要注册。
> 顺便第九章的输入分支也能一起收敛。

---

### 13.15 SNS 手机聊天 VNSnsView

**它不是事件模块，是「对话的另一种呈现层」**：
```csharp
/// 定位：它**不是**事件模块，而是对话的另一种呈现层——
/// sns open 之后，普通台词行 `亚里沙: 你好` 会被渲染成左侧气泡，
/// `我: 好啊` 渲染成右侧气泡。这样存档、分支、翻译表全部沿用现成机制
/// （event 模块是原子的，聊天中途就存不了档了）。
```
> 出处：[`VNSnsView`](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:13](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L13)）

在 Runner 里的分流只有一行：
```csharp
IEnumerator SayCo(VNScriptCommand cmd)
{
    // SNS 模式：台词不进对话框，改成手机聊天气泡（呈现层不同，语义完全一样，
    // 因此存档点/分支/翻译表全部沿用普通台词的机制）
    if (stage != null && stage.IsSnsOpen) return SnsSayCo(cmd);
    return NormalSayCo(cmd);
}
```
> 出处：`VNScriptRunner.SayCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2608](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2608)）

> **「换呈现层而不换机制」是一个非常划算的设计**。
> 如果把 SNS 做成 event 模块，就要重新实现存档点、分支、翻译、Auto/Skip——
> 而且「聊天中途不能存档」会是一个明显的体验缺陷。
> 现在只多了一个 `SnsSayCo`（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2655](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2655)`），
> 二十行。
>
> **判断标准**：如果新玩法只是「台词长得不一样」，做呈现层；
> 如果它有独立的输赢与结果分支，做 event 模块。

**手工布局，不用 LayoutGroup**：
```csharp
/// 布局全部手工计算（不用 VerticalLayoutGroup + ContentSizeFitter）：
/// TMP 的 preferredHeight 在同一帧内不可靠，手工测量反而稳定可控。
```
> 出处：`VNSnsView` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:18](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L18)）

```csharp
// 先在行里量文本，再据此定气泡尺寸。
// 手工测量绕开 ContentSizeFitter/LayoutGroup 的重建时序问题（TMP 的
// preferredHeight 同帧内不可靠），代价是所有位置都要自己算。
var row = CreateRow(AvatarSize);
var text = CreateText("Text", row, FontSize, TextColor, display);
text.textWrappingMode = TextWrappingModes.Normal;
var size = text.GetPreferredValues(display, MaxBubbleW, 0f);

float bubbleW = Mathf.Clamp(Mathf.Ceil(size.x) + BubblePadX * 2f + 2f,
    MinBubbleW, MaxBubbleW + BubblePadX * 2f);
float bubbleH = Mathf.Ceil(size.y) + BubblePadY * 2f;
row.sizeDelta = new Vector2(0f, Mathf.Max(bubbleH, AvatarSize));
```
> 出处：`VNSnsView.BuildTextRow`（[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:566](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L566)）

`GetPreferredValues(display, MaxBubbleW, 0f)` 是**带宽度约束**的测量重载——
传入最大宽度、高度传 0 表示不限，返回换行后的实际尺寸。这是做聊天气泡的标准手法。

排版：
```csharp
void Layout()
{
    if (_content == null) return;
    float y = EdgePad;
    foreach (var row in _rows)
    {
        if (row == null) continue;
        row.anchoredPosition = new Vector2(0f, -y);
        y += row.sizeDelta.y + RowGap;
    }
    y += EdgePad - RowGap;
    _content.sizeDelta = new Vector2(0f, Mathf.Max(y, ViewportHeight));
}
```
> 出处：`VNSnsView.Layout`（[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:862](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L862)）

> **手工布局 vs LayoutGroup 的取舍**：
> - LayoutGroup 省代码，但**重建时序不可控**——你不知道它什么时候算完，
>   `Canvas.ForceUpdateCanvases()` 也只能强制一层。
>   在「加一条消息 → 立刻滚到底」这种需要精确高度的场景下很痛苦。
> - 手工布局要自己算所有位置，但**当场就知道结果**，滚动、动画都好写。
>
> 本专案的选择是对的：聊天列表是「一次加一条、要精确滚到底」的场景。
> 而 Toast 卡片用了 HorizontalLayoutGroup + ContentSizeFitter
> （`VNToast.BuildCard`（[`Assets/Project/Scripts/VNEffects/Script/VNToast.cs:201`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L201)），`[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:219](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L219)`），
> 因为那里只需要「宽度跟着文字走」，不需要知道确切数值。

**布局常量全在类顶部**（`VNSnsView.PhoneW`（[`Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:32`](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L32)） 起，
`[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:32](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L32)` 到 `:42`），
与大头贴、羽毛球同一习惯。

**状态进存档**：
```csharp
public void CaptureSnapshot(VNSaveData data)
```
> 出处：`VNSnsView.CaptureSnapshot`（[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:500](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L500)）
> 与 `VNSnsView.RestoreSnapshot`（[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:512](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L512)）

由 [`VNStage`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs) 转发（`VNStage.CaptureSnapshot`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:843`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L843)），
`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:882](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L882)`；
`VNStage.RestoreSnapshot`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:902`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L902)），`[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:978](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L978)`）。
读档时按消息列表重建整屏气泡。

**输入阻塞的语义**：
```csharp
/// <summary>是否正在等待玩家回复（此时禁用剧本快捷键与存档，同 event 模块）</summary>
public bool IsBlockingInput => _replyActive;
```
> 出处：`VNSnsView.IsBlockingInput`（[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:81](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L81)）

Runner 据此 return（`VNScriptRunner.Update`，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1993](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1993)`）。

---

### 13.16 过场章节卡 VNInterludeScreen

**它示范了「怎么让转场不经过纯黑」**：
```csharp
/// 【enter = Transition 为什么不调 VNScreenTransition.Play()】
/// 那个 API 是遮罩式的：拿一块纯色（默认黑）按图案盖满画面 → 换内容 → 图案散开，
/// 所以中间必然闪一片黑。过场层要的是「新图直接从瓦片缝里长出来」，
/// 于是改成自己持有一份 VN/ScreenTransition 的**贴图模式**材质
/// （`_TexMode = 1`：图案里填的是图而不是纯色），progress 0→1 就是长出来、
/// 1→0 就是消失。图案参数仍走 VNScreenTransition.ConfigurePattern 那张唯一的表。
/// 贴图模式下 `_Color` 退化成染色系数，暗幕就折进它里面，不用再叠一层。
```
> 出处：[`VNInterludeScreen`](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:13](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L13)）

关键在于**图案参数仍走同一张表**：
```csharp
VNScreenTransition.ConfigurePattern(_patternMat, def.transition, ...);
```
> 出处：`VNInterludeScreen.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:91](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L91)）

> **「两种用法、一份参数表」是避免分家的关键**。
> 如果过场层自己抄一份图案参数，以后加一种新图案就要改两处，
> 而且很容易只改一处导致两边表现不一致。

**标题与 loading 单独一个 CanvasGroup**：
```csharp
CanvasGroup _hudGroup;
```
> 出处：`VNInterludeScreen._hudGroup`（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:51](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L51)）

```csharp
_hudGroup.DOFade(1f, hudFade).SetDelay(hudDelay).SetLink(gameObject);
```
> 出处：`VNInterludeScreen.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:118](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L118)）

`CLAUDE.md` 记了理由：「标题/loading 单独 CanvasGroup 淡入（跟着图案走会被瓦片切碎）」。
图案是逐像素的溶解/瓦片，文字跟着它走就会被切成一块块。

**过场期间吃掉点击**：
```csharp
_group.blocksRaycasts = true; // 过场期间吃掉点击（时长固定，玩家点不动）
```
> 出处：`VNInterludeScreen.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:80](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L80)）

**不进存档，但 ClearStage 必须收起**：
```csharp
if (interlude != null) interlude.HideImmediate(); // 过场层同理，不能留在屏幕上
```
> 出处：`VNStage.ClearStage`（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:990](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L990)）

> **「不进存档」不等于「不用清理」**。
> 过场是一次性演出，播完什么都不留——所以不进 [`VNSaveData`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs)；
> 但读档时如果正卡在过场中间，那一屏必须收掉。
> 这两件事经常被混为一谈。
> 判断口诀：**进不进存档看「读档后该不该恢复」，要不要清理看「读档瞬间屏幕上有没有它」。**

**快进时整段跳过**：
```csharp
// 快进时整段跳过（连语音都不放）：章节卡本来就是给正常速度看的，
// 而 1.5 秒的固定停留在 SKIP 里是纯粹的卡顿。
if (_skip) return null;
```
> 出处：`VNScriptRunner.Dispatch` 的 interlude 分支（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2445](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2445)）

教程也是同样处理（`VNScriptRunner.Dispatch` 的 tutorial 分支，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2458](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2458)`）。

---

## 十四、UI ↔ 游戏逻辑的数据流（四条通道）

本专案 UI 与逻辑之间的沟通**只有四条通道**，没有别的。搞清楚这四条，整个数据流就通了。

### 通道一：VNFlags 字典 + 事件（游戏状态 → UI）

**这是最主要的一条**。所有可变的游戏状态都是 `Dictionary<string, int>` 里的一个键值对。

```csharp
public static class VNFlags
{
    static readonly Dictionary<string, int> _values = new Dictionary<string, int>();

    /// <summary>任何 flag 变化（Set/Clear/读档恢复）后触发；属性 HUD 等 UI 订阅刷新。
    /// 读档时会连续触发多次，订阅方应做"标脏 + 下帧统一刷新"而不是立即重建。</summary>
    public static event System.Action Changed;

    /// <summary>带 key 的变化事件（统计层 VNTracker 用）。…</summary>
    public static event System.Action<string> KeyChanged;
```
> 出处：[`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs)（[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:15](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L15)）

```csharp
public static void Set(string key, int value)
{
    _values[key] = value;
    KeyChanged?.Invoke(key);
    Changed?.Invoke();
}
```
> 出处：`VNFlags.Set`（[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:34](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L34)）

**两个事件的分工**（注释在 `VNFlags.KeyChanged`（[`Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:27`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L27)） 上方，
`[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:23](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L23)`）：

> 无参的 Changed 拿不到「是哪个 flag 变了」，而统计层靠 diff 字典会漏掉
> 「两场都打 21 分」这种值没变的写入——@次数 与 @累计 就错了。
> 值相同的重复 Set 也会触发：那确实是一次新的写入。

**订阅者一览**：

| 订阅者 | 事件 | 做什么 | 出处 |
|---|---|---|---|
| [`VNStatsHud`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs) | `Changed` | 标脏，下帧刷 HUD 与面板 | `VNStatsHud.Awake`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:55](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L55)） |
| [`VNCalendarHud`](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs) | `Changed` | 标脏，下帧刷月份 | `VNCalendarHud.Awake`（[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:27](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L27)） |
| [`VNQuestLog`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs) | `Changed` | 标脏，下帧跑任务引擎求值 | `VNQuestLog.Awake`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:62](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L62)） |
| [`VNTracker`](Assets/Project/Scripts/VNEffects/Script/VNTracker.cs) | `KeyChanged` | 派生 @最高/@累计/@次数 | （见 CLAUDE.md 组件表；本次未逐行核对） |

**「标脏 + 下帧一次」的模板**（三处一模一样）：
```csharp
void MarkDirty() => _dirty = true;

void Update()
{
    if (!_dirty) return;
    _dirty = false;
    ...刷新...
}
```
> 出处：`VNStatsHud.MarkDirty`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:70](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L70)）
> 与 `VNStatsHud.Update`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:296](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L296)）

> **为什么不用「响应式属性 / ObservableCollection」**：
> 因为 flag 是**动态键**的——剧本随时可以写一个新名字，
> 没法预先为每个 flag 建一个 observable。
> 「一个粗事件 + 消费者自己 diff」在这种场景下是正确的选择。
>
> **代价**：每次 flag 变化所有订阅者都被叫醒（即使跟它无关）。
> 因为消费者只是设一个 bool，代价可忽略。
> 如果订阅者增长到几十个、或者刷新逻辑变重，就该改成按 key 前缀过滤。

**只读视图暴露给 UI**：
```csharp
public static IReadOnlyDictionary<string, int> All => _values;
```
> 出处：`VNFlags.All`（[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:29](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L29)）

`VNCalendarHud.Refresh` 用它判断「有没有这个 key」
（`[Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs:65](Assets/Project/Scripts/VNEffects/Script/VNCalendarHud.cs#L65)`），
`VNStatsHud.EnsureInitials`（[`Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:96`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L96)） 用它避免覆盖已有值
（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:101](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L101)`）。

### 通道二：直接方法调用（UI → 游戏逻辑）

**UI 改状态一律走方法调用，不是直接写字典**：

| UI | 调用 | 目标 |
|---|---|---|
| 选项 `cost:` | `_statsHud?.ApplyCost(opt.costOp, opt.line)` | [`VNScriptRunner.ChoiceCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2898](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2898)） |
| 选项 `flag:` | `VNFlags.Apply(opt.flagOp)` | 同上（`:2899`） |
| 快捷条按钮 | `_runner?.RequestSavePanel()` 等 | [`VNQuickToolbar.Execute`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:104](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L104)） |
| 存档卡点击 | `_runner?.SaveTo(slot, _pendingThumbnail)` | [`VNSaveLoadPanel.SaveSlot`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:251](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L251)） |
| 设置滑条 | `_stage?.vnAudio?.SetVolume("bgm", value)` | [`VNConfigPanel.BindCustomSkin`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:136](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L136)） |
| 标题「开始」 | `_runner.StartNewGame()` | [`VNTitleMenu.OnStartClicked`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:133](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L133)） |

`VNFlags.Apply`（[`Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:50`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L50)） 是一个小型 DSL 解析器：
```csharp
/// <summary>应用一个 flag 操作串："名字"=置1、"名字+2"/"名字-1"=增减</summary>
public static void Apply(string op)
{
    if (string.IsNullOrEmpty(op)) return;
    op = op.Trim();

    // 从第 2 个字符起找 +/-（避免把负号开头误判）
    for (int i = 1; i < op.Length; i++)
    {
        if (op[i] == '+' || op[i] == '-')
        {
            string name = op.Substring(0, i).Trim();
            if (int.TryParse(op.Substring(i), out int delta))
            {
                Add(name, delta);
                return;
            }
            break;
        }
    }
    Set(op, 1);
}
```
> 出处：`VNFlags.Apply`（[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:50](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L50)）

**「从第 2 个字符起找」这个细节**：如果从 0 开始找，`-血量+1` 这种名字开头带负号的会被误判。
同一个技巧在 `VNStatsHud.ParseCostOp`（[`Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:170`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L170)）
（`[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:176](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L176)`）里重复了一次。

### 通道三：轮询（少数高频/多源状态）

只有三处用轮询，都有明确理由：

| 轮询者 | 读什么 | 理由 |
|---|---|---|
| `VNQuickToolbar.Update` | `_runner.IsAuto` / `IsSkipping` | 状态极简、来源多（快捷键/按钮/强制关） |
| [`VNTutorialMask.LateUpdate`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs) | 目标的世界四角 | 目标可能在动，事件表达不了 |
| [`VNScenarioEditorWindow.OnInspectorUpdate`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs) | `runner.CurrentLine` | 「不用给运行时加事件」（注释） |

第三条的注释直接写了取舍：
```csharp
/// <summary>10Hz 轮询 Runner 的当前行，驱动播放跟随高亮（不用给运行时加事件）</summary>
void OnInspectorUpdate()
```
> 出处：`VNScenarioEditorWindow.OnInspectorUpdate`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:311](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L311)）

> **「为了一个编辑器功能而给运行时加事件」通常是不划算的**——
> 运行时代码会因为编辑器需求变复杂，而且事件订阅要处理跨域重载。
> `OnInspectorUpdate` 是 10Hz 的编辑器回调，轮询一个 int 属性成本为零。

### 通道四：ScriptableObject 配置（资产 → UI）

[`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:34](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L34)`）
是唯一的全局配置入口，走 `Resources.Load`：
```csharp
public static VNGameConfig Active
{
    get
    {
        if (_active != null) return _active;
        if (_lookedUp) return null;
        _lookedUp = true;
        _active = Resources.Load<VNGameConfig>(ResourcesName);
        return _active;
    }
}
```
> 出处：`VNGameConfig.Active`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:48](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L48)）

`_lookedUp` 标志保证「找不到时也只找一次」——
`Resources.Load` 失败不便宜，不能每帧调。

**编辑器改完资产要清缓存**：
```csharp
/// <summary>编辑器改完资产后清缓存（play mode 之间不残留旧引用）。</summary>
public static void ClearCache()
{
    _active = null;
    _lookedUp = false;
    // AI 那两处也各缓存了一份从 config 里读出来的东西（默认供应商 / 单价表）。
    // 不一起清的话，在 Inspector 里把供应商从 Gemini 改成 DeepSeek 之后
    // 还会继续发给 Gemini，直到下次域重载——这种「改了没反应」最难查。
    VNAiProviders.Invalidate();
    VNAiPricing.Invalidate();
}
```
> 出处：`VNGameConfig.ClearCache`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:69](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L69)）

> **「缓存的缓存」是一个隐蔽的坑**。
> 清了 `VNGameConfig` 的缓存，但下游还各自缓存了从它读出来的值——
> 结果是「改了配置没反应」。本专案的解法是让 `ClearCache` 连带清下游。
> **凡是加一层缓存，就要问一句「谁会缓存我的结果」。**

### 数据流全景图

```
        ┌──────────────── 剧本 (.vn.txt) ────────────────┐
        │  say / choice / stat / flag / quest / event…    │
        └────────────────────┬───────────────────────────┘
                             │ VNScriptParser
                             ▼
                    VNScriptRunner.Dispatch
          ┌──────────────────┼──────────────────┐
          ▼                  ▼                  ▼
     VNStage 演出       VNFlags 状态       系统面板 Request*
     (对话框/立绘)      (整型字典)          (存读档/画廊…)
          │                  │
          │                  │ Changed / KeyChanged
          │                  ▼
          │         ┌────────┴────────┐
          │         ▼        ▼        ▼
          │    VNStatsHud VNQuestLog VNCalendarHud   (标脏 → 下帧刷)
          │         │
          │         └──→ VNToast (卡片提示)
          │
          └──→ VNSaveData ←── VNGameConfig (ScriptableObject)
                  ▲                    ▲
                  │                    │ Resources.Load
             存读档面板           所有模块 ApplyList
```

### 存档：三层状态的分工

[`VNSaveData`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:9](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L9)`）
里的 UI 相关字段：

| 字段 | 内容 | 行号 |
|---|---|---|
| `dialogueSkin` | 对话框皮肤 id（空 = 默认） | `[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:58](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L58)` |
| `choiceSkin` | 选项皮肤 id | `:59` |
| `nameplateStyle` | 名字样式名 | `:60` |
| `uiHidden` | 锁定隐藏的部件（"dialogue,stats"） | `:64` |
| `portraitOff` | 头像开关 | `:53` |
| `flagNames` / `flagValues` | 整个 flag 字典 | `:30` |
| `aiMemories` | AI 跨场记忆 | `:72` |

**每个字段都带了「旧存档缺省时是什么」的注释**，例如：
```csharp
public float bgmVol = 1f;         // bgm 命令的 vol: 参数（旧存档缺省 = 1）
```
> 出处：`VNSaveData.bgmVol`（[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:52](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L52)）

```csharp
/// AI 自由聊天的跨场记忆。**必须跟着存档走**——读回旧档时她不该记得
/// 「未来」聊过的事。旧存档没有这个字段时 JsonUtility 给空列表，
/// 等价于「那时候还没聊过」，语义正确。
public List<VNAiMemoryEntry> aiMemories = new List<VNAiMemoryEntry>();
```
> 出处：`VNSaveData.aiMemories`（[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:66](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L66)）

**`JsonUtility` 的向后兼容特性**：旧 JSON 里没有的字段，反序列化后保持 C# 里的初始值。
所以只要**新字段的默认值等于旧行为**，加字段就是零成本的。
本专案把这条用到了极致——每个新字段都注明了「旧存档缺省即此」。

一个特别的例子：
```csharp
// 喷射方向：内部用 float.NaN 表示"朝镜头"，但 JsonUtility 会把 NaN 写成
// 非法 JSON，读回来是垃圾值。所以照 weatherWindSet 的先例另用一个 bool 标记，
// 旧存档缺省 false = 朝镜头（也正是新的默认行为）。
public bool sprayDirSet;
public float sprayDir;
```
> 出处：`VNSaveData.LiquidSave.sprayDirSet`（[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:87](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L87)）

> **`JsonUtility` 不能序列化 NaN / Infinity** 是一个实际会咬人的限制。
> 解法就是这里的做法：另加一个 bool 表示「这个值有没有被设置」。
> 同一模式在天气（`weatherWindSet`，
> `[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:42](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L42)`）里用过一次。

---

## 十五、资源加载与释放：贴图、字体、纹理缓存

### 15.1 程序化贴图：零美术依赖的地基

```csharp
/// <summary>
/// 运行时程序化生成粒子/光晕贴图，无需任何美术资源。
/// 所有贴图懒加载并缓存，整个游戏生命周期只生成一次。
/// </summary>
public static class VNProceduralTextures
```
> 出处：[`VNProceduralTextures`](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs)（[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:10](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L10)）

**模式统一：静态字段 + 惰性 getter**
```csharp
static Sprite _roundedRectSprite;

public static Sprite RoundedRectSprite
{
    get
    {
        if (_roundedRectSprite == null)
        {
            const int size = 64;
            var tex = Generate("VN_RoundedRect", size, size, (dx, dy) => { ... });
            _roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
            _roundedRectSprite.name = "VN_RoundedRectSprite";
            _roundedRectSprite.hideFlags = HideFlags.DontSave;
        }
        return _roundedRectSprite;
    }
}
```
> 出处：`VNProceduralTextures.RoundedRectSprite`（[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:284](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L284)）

三个要点：
1. **`hideFlags = HideFlags.DontSave`**——不写进场景、域重载自动销毁。
   如果不设，编辑期生成的贴图会被 Unity 当成场景资源试图保存，产生「泄漏的资产」警告。
2. **`Sprite.Create` 的第七个参数是 9-slice 边距**（`new Vector4(22,22,22,22)`）——
   这样 `Image.Type.Sliced` 才能正确拉伸圆角。
3. **`SpriteMeshType.FullRect`**——默认的 `Tight` 会按 alpha 裁剪网格，
   9-slice 需要完整矩形。

**圆角矩形用 SDF 生成**：
```csharp
/// <summary>圆角矩形 SDF：d &lt; 0 在内部</summary>
static float RoundedBoxDist(float px, float py, float halfW, float halfH, float radius)
{
    float qx = Mathf.Abs(px) - (halfW - radius);
    float qy = Mathf.Abs(py) - (halfH - radius);
    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
    return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
}
```
> 出处：`VNProceduralTextures.RoundedBoxDist`（[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:275](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L275)）

```csharp
float d = RoundedBoxDist(dx * size, dy * size, size * 0.5f - 1f, size * 0.5f - 1f, 16f);
return Mathf.Clamp01(0.5f - d); // 1px 抗锯齿
```
> 出处：`VNProceduralTextures.RoundedRectSprite`（[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:293](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L293)）

描边框只是「外圈减内圈」：
```csharp
float outer = Mathf.Clamp01(0.5f - d);
float inner = Mathf.Clamp01(0.5f - (d + thickness));
return outer - inner; // 只留边缘细环
```
> 出处：`VNProceduralTextures.RoundedFrameSprite`（[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:318](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L318)）

> **SDF 是程序化 UI 贴图的万能钥匙**。
> 有了距离场，实心 = `clamp(0.5-d)`、描边 = 两个距离场相减、
> 外发光 = `exp(-d*k)`、圆角 = 半径参数。一个函数生成整套控件底图。
>
> **`0.5 - d` 里的 0.5 就是 1 像素的抗锯齿带**（距离场以像素为单位时）。
> 这是 SDF 转 alpha 的标准写法。

**贴图列表**（`VNProceduralTextures` 提供的公开资源）：

| 名称 | 用途 | 行号 |
|---|---|---|
| `SoftCircle` | 尘埃/光斑粒子、属性图标兜底 | `[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:21](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L21)` |
| `Sparkle` | 四芒星光 | `:36` |
| `RadialGlow` | 径向光晕 | `:57` |
| `LightBeam` | 光束 | `:75` |
| `EdgeGlowFrame` | 情绪泛光边框 | `:93` |
| `Petal` | 花瓣 | `:110` |
| `SpeedLines(variant)` | 速度线三变体 | `:136` |
| `MeteorStreak` | 流星拖尾 | `:185` |
| `CloudPuff` | 云团 | `:214` |
| `Ring` | 圆环 | `:252` |
| `RoundedRectSprite` | **面板底（9-slice）** | `:284` |
| `RoundedFrameSprite` | **描边框（9-slice）** | `:307` |
| `RadialGlowSprite` | 光晕的 Sprite 包装 | `:333` |
| `SparkleSprite` | 星光的 Sprite 包装 | `:353` |
| `LoadingRing` / `LoadingRingSprite` | 过场 loading 转圈 | `:378` / `:398` |
| `MarkSprite(kind)` | 漫符（汗滴/怒气/爱心…） | `:424` |
| `LiquidBlob` / `WaterDrop` / `WaterSpeck` 等 | 液体 | `:766` / `:817` / `:858` |

**`RoundedRectSprite` 在 UI 里被用了多少次**：对话框面板、名牌底板、Toast 卡片、
选项按钮、教程卡片、商店面板、日历 HUD、互动 HUD、羽毛球确认框、
大头贴机身……几乎每一个程序化面板都用它。**一张 64×64 的贴图撑起了整个 UI 的底图。**

### 15.2 字体：三级兜底 + 动态图集

```csharp
/// 每种语言的解析顺序（三级兜底，保证任何情况下都能显示）：
///   1. 预烘焙动态 TMP 字体资产（Assets/Resources/VNFonts/<名字>-Dynamic.asset）
///   2. 运行时从随包字体文件（Resources/VNFonts/<名字>-Regular）动态创建 TMP 字体资产
///   3. 运行时从操作系统字体（雅黑 / Yu Gothic 等）动态创建
/// 三级全失败时回退到该语言登记的 fallback 档案（见 Profile.fallback）。
```
> 出处：[`VNFont`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:18](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L18)）

**一个 Profile 描述一种字体来源**：
```csharp
class Profile
{
    public string bakedPath;
    public string sourcePath;
    public string[] osCandidates;
    /// <summary>解析失败时兜底改用的档案；解析成功时也会把它挂进 TMP fallback 表补缺字</summary>
    public Profile fallback;
    public int padding = AtlasPadding;
    public int samplePointSize = SamplePointSize;
    public int atlasSize = AtlasSize;
    /// <summary>是否为装饰字体档案（语言切换时与正文字体分开替换）</summary>
    public bool isDisplay;
}
```
> 出处：`VNFont.Profile`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:90](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L90)）

三个语言档案：`ZhProfile`（`[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:121](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L121)`）、
`JaProfile`（`:129`）、`GeneralCjkProfile`（`:114`，同时是中日的缺字兜底）。

**日文必须独立字体**：
```csharp
///   日文：Noto Sans JP（SC 的假名字形不合日文排印规范，必须独立字体），
///        生僻字缺字时同样兜底 Noto Sans SC（不用中文的楷体，避免风格突兀且缺字覆盖不如 Noto 全）
```
> 出处：`VNFont` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:15](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L15)）

**动态图集 + 预热**：
```csharp
/// <summary>
/// 把一段文本包含的全部字符预热进当前语言字体的动态图集（去重由 TMP 内部处理）。
/// 建议在剧本加载完成时对全文调用一次，把逐字光栅化成本挪到加载期。
/// </summary>
public static void Prewarm(string text) => PrewarmAsset(Asset, text);

static void PrewarmAsset(TMP_FontAsset asset, string text)
{
    if (string.IsNullOrEmpty(text)) return;
    if (asset == null || asset.atlasPopulationMode != AtlasPopulationMode.Dynamic) return;
    asset.TryAddCharacters(text);
}
```
> 出处：`VNFont.Prewarm`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:263](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L263)）

UI 常用符号在字体解析成功时立刻预热：
```csharp
/// <summary>UI 常用符号预热集（界面按键提示、箭头、省略号等，启动即备好）</summary>
const string CommonUiChars =
    " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
    "abcdefghijklmnopqrstuvwxyz{|}~" +
    "，。、；：？！…—·「」『』（）《》【】“”‘’▼▲◀▶★☆♪％×";
```
> 出处：`VNFont.CommonUiChars`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:84](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L84)）

```csharp
PrewarmAsset(asset, CommonUiChars);
```
> 出处：`VNFont` 字体解析尾部（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:255](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L255)）

> **动态图集是 CJK 项目的正解**：
> 静态图集要预烘焙几千个汉字（图集巨大、启动慢、还会缺生僻字）；
> 动态图集按需光栅化，代价是「第一次出现某个字时有一帧的卡顿」。
> `Prewarm` 把这个成本挪到加载期就完美了。
>
> **注意 `atlasPopulationMode != Dynamic` 时直接 return**——
> 静态图集调 `TryAddCharacters` 是无效的（甚至可能报错）。

### 15.3 纹理缓存：两级 + LRU

相册是全专案唯一大量读盘纹理的地方，所以缓存策略最完整。

**全尺寸走 LRU**：
```csharp
static readonly Dictionary<string, Texture2D> _textureCache =
    new Dictionary<string, Texture2D>();
static readonly List<string> _cacheOrder = new List<string>();
```
> 出处：[`VNPhotoAlbum._textureCache`](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs)（[Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:52](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L52)）

```csharp
public static Texture2D LoadTexture(string file)
{
    if (string.IsNullOrEmpty(file)) return null;
    if (_textureCache.TryGetValue(file, out var cached) && cached != null)
    {
        Touch(file);
        return cached;
    }
    ...
    _textureCache[file] = tex;
    Touch(file);
    TrimCache();
    return tex;
}
```
> 出处：`VNPhotoAlbum.LoadTexture`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:233](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L233)）

**缩略图单独一份、不驱逐**：
```csharp
/// <summary>
/// 缩略图（网格用）。**不能让网格走 LoadTexture**：那是 12 张的 LRU，
/// 一屏显示几十张时先加载的纹理会被驱逐，而 Sprite 还引用着它 —— 直接变成白块。
/// 所以缩略图单独一份缓存、降到 192×144（约 110KB/张）、不驱逐。
/// </summary>
public static Sprite LoadThumbnail(string file)
```
> 出处：`VNPhotoAlbum.LoadThumbnail`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:271](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L271)）

> **这是 LRU 缓存最经典的失败模式**：
> 「同时需要的对象数 > 缓存容量」时，LRU 退化成「每次都 miss + 每次都驱逐正在用的」。
> 网格里一屏 30 张缩略图 vs 12 张的 LRU = 灾难。
>
> **解法就是本专案的做法：按使用模式分两个缓存。**
> 「一次看一张」的全尺寸走 LRU，「一次看几十张」的缩略图走不驱逐的小图缓存。
> 缩略图 192×144 约 110KB，200 张也才 22MB，可以接受。

**缩略图的降采样是手写双线性**：
```csharp
var pixels = new Color[ThumbnailWidth * height];
for (int y = 0; y < height; y++)
    for (int x = 0; x < ThumbnailWidth; x++)
        pixels[y * ThumbnailWidth + x] = full.GetPixelBilinear(
            (x + 0.5f) / ThumbnailWidth, (y + 0.5f) / height);
small.SetPixels(pixels);
small.Apply(false);
```
> 出处：`VNPhotoAlbum.LoadThumbnail`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:305](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L305)）

`+ 0.5f` 是像素中心采样（避免半像素偏移）。
同样的手法在 `VNPhotoCapture.Downscale`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:133`](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L133)）
（`[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:144](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L144)`）里也用了。

**全尺寸图读完就 Destroy**：
```csharp
finally
{
    if (full != null) UnityEngine.Object.Destroy(full);
}
```
> 出处：`VNPhotoAlbum.LoadThumbnail`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:325](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L325)）

**关界面时全清**：
```csharp
VNPhotoAlbum.ClearCache();
VNSecretAlbum.ClearCache();
```
> 出处：[`VNCgGallery.Close`](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs)（[Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs:117](Assets/Project/Scripts/VNEffects/Script/VNCgGallery.cs#L117)）

### 15.4 Texture2D 生命周期速查表

这是 Unity 里最容易泄漏的资源，本专案的处理汇总：

| 场景 | 谁创建 | 谁销毁 | 出处 |
|---|---|---|---|
| 存档缩略图（读盘展示） | [`VNSaveLoadPanel.CreateSlotCard`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs) | `ClearLoadedThumbnails`（4 个出口） | [Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:283](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L283) |
| 存档缩略图（待存的那张） | `CaptureSaveThumbnailCo` | `ReplacePendingThumbnail` + `SaveTo` 存完 | [Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:276](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L276) |
| 相册全尺寸 | `VNPhotoAlbum.LoadTexture`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:233`](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L233)） | LRU 驱逐 + `ClearCache` | [Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:344](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L344) |
| 相册缩略图 | `VNPhotoAlbum.LoadThumbnail`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:276`](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L276)） | `ClearCache` | 同上 |
| 抓屏原图 | `VNPhotoCapture.Capture`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:28`](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L28)） | `finally` 里立即 Destroy | [Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:72](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L72) |
| 拍照成品 | `VNPhotoCapture.Crop`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:100`](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L100)） | 调用方（模块的 `_shotTexture`） | [Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:183](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L183) |
| 涂鸦画布 | `VNPhotoDoodle.Build`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:110`](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L110)） | 模块销毁时 | [Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:83](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L83) |
| 程序化贴图 | `VNProceduralTextures` | 不销毁（`HideFlags.DontSave`，域重载清） | [Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:300](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L300) |

**Material 同理**：
| 材质 | 创建 | 销毁 |
|---|---|---|
| 教程暗幕 | [`VNTutorialMask.Awake`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:75](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L75)） | `OnDestroy`（`:96`） |
| 光标发光 | [`VNTouchCursor.Initialize`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:63](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L63)） | `Dispose`（`:224`） |
| 过场图案 | [`VNInterludeScreen._patternMat`](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:58](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L58)） | （随组件销毁；未逐行核对） |
| TMP 名牌材质实例 | `text.fontMaterial` 首次访问（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:520](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L520)） | TMP 自己管 |

> **规律**：`new Material(shader)` 与 `new Texture2D(...)` 出来的东西都要手动 Destroy。
> 本专案对每一处都做了配对，而且都加了 `hideFlags = HideFlags.DontSave`
> （例 `VNTouchCursor.Initialize`（[`Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:48`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L48)），`[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:63](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L63)`）
> 作为第二道保险——即使漏了 Destroy，域重载时也会被清掉（编辑器里）。

### 15.5 Prefab 的加载与释放

系统面板的皮肤 prefab **不走 Resources、不走 Addressables**，
而是通过 `VNGameConfig.systemUiSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)） 的**直接引用**
（`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)`）。

这意味着：[`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) 一被 `Resources.Load` 出来，
它引用的十二个 prefab **全部被一起加载进内存**。

> **这是一个明确的取舍**。
> 优点：不需要任何异步加载代码、不会有「prefab 还没加载完」的时序问题。
> 缺点：所有系统 UI 的 prefab 常驻内存，即使玩家从不打开画廊。
>
> 在这个项目的规模下（十几个 UI prefab，加起来可能几 MB）完全可以接受。
> 如果以后 UI 资产膨胀（比如每个皮肤带高清底图），
> 就应该改成 `AssetReference` + Addressables 按需加载。
> **判断标准**：所有 UI prefab 的总内存 > 50MB 时就该考虑。

对话框/选项皮肤同理，走 `VNGameConfig.dialogueSkins`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:126`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L126)）
（`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:126](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L126)`）的直接引用。

**实例的释放**：
- 对话框皮肤实例：切皮肤时 `Destroy(_customRoot)`
  （`VNDialogueBox.ApplySkin`（[`Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:236`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L236)），`[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:245](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L245)`）。
- 选项皮肤实例：同上（`VNChoicePanel.ApplySkin`（[`Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:105`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L105)），
  `[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:112](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L112)`）。
- 系统面板皮肤实例：**不释放**（面板本身就常驻），
  只有语言切换时随整个 Canvas 一起销毁。

---

## 十六、可复用 UI 基础设施清单

把散落各处的可复用件汇总成一张表，这是本文件最实用的一节。

### 16.1 全局静态服务

| 名称 | 用途 | 入口 | 出处 |
|---|---|---|---|
| [`VNProceduralTextures`](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs) | 程序化贴图/Sprite | `RoundedRectSprite` 等属性 | [Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:10](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L10) |
| [`VNFont`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs) | TMP 字体统一入口 | `Asset` / `DisplayAsset` / `Prewarm` | [Assets/Project/Scripts/VNEffects/Script/VNFont.cs:31](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L31) |
| [`VNLocale`](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs) | UI 字符串本地化 | `T(key)` / `T(key, args)` / `LanguageChanged` | [Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:26](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L26) |
| [`VNToast`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs) | 提示卡片 / 模式标签 / 角标 | `Show` / `SetMode` / `SetBadge` | [Assets/Project/Scripts/VNEffects/Script/VNToast.cs:22](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L22) |
| [`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs) | 游戏状态字典 | `Get` / `Set` / `Apply` / `Evaluate` / `Changed` | [Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:15](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L15) |
| [`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) | 全局玩法暂停 | `Acquire` / `Release` / `IsPaused` | [Assets/Project/Scripts/VNEffects/Script/VNPause.cs:25](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L25) |
| `VNTime` | 受暂停影响的时间源 | `Delta` / `Time` | [Assets/Project/Scripts/VNEffects/Script/VNPause.cs:136](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L136) |
| [`VNTutorialAnchors`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs) | 教程高亮目标注册表 | `Register` / `Unregister` / `Get` | [Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:24](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L24) |
| [`VNCgUnlocks`](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs) | CG 解锁全局记录 | `Unlock` / `IsUnlocked` / `All` | [Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs:14](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs#L14) |
| [`VNTutorialSeen`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs) | 教程已看全局记录 | `Has` / `Mark` / `ResetAll` / `Enabled` | [Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs:19](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs#L19) |
| [`VNSystemUiSkinUtility`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs) | 皮肤 prefab 安全实例化 | `Prefab` / `Instantiate<T>` | [Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs:27](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs#L27) |
| [`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) | 全局内容配置 | `Active` / `ApplyList` / `FindSkin` | [Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:34](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L34) |

### 16.2 UI 构建辅助（三套，各自局部）

**注意：本专案没有一套全局共用的 UI 构建工具**，而是有三套各自服务一个区域的：

| 名称 | 服务对象 | 提供 | 出处 |
|---|---|---|---|
| [`VNBadmintonUi`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs) | 羽毛球三个文件 | `CreateNode` / `CreateImage` / `CreateQuad` / `CreateLine` / `CreateText` / `Stretch` / `AnchorBottomCenter` | [Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:44](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L44) |
| [`VNPhotoBoothUi`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs) | 大头贴六个文件 | `CreateImage` / `CreateText` / `CreateNode` / `Center` / `Stretch` + 配色常量 | Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs（贴纸组件在同档 `:17`） |
| 各模块私有的 `CreateImage/CreateText/Stretch` | 单个模块 | 同名同签名，各写一份 | 例 [`VNQteModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs) 附近、[`VNShopModule.CreateButton`](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:383](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L383)） |

`VNBadmintonUi` 的类注释承认了这一点：
```csharp
/// 羽球模块共用的程序化 UI 辅助（抄 VNQteModule 的 CreateImage/CreateText 那一套，
/// 因为球场 / 角色 / HUD 三个文件都要用，抽出来避免三份重复）。
```
> 出处：`VNBadmintonUi` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:41](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L41)）

> **这是本专案最明显的重复代码**：
> `CreateImage(name, parent, sprite, color)` 与 `CreateText(...)` 这两个函数，
> 以几乎相同的形式出现在至少八个文件里
> （QTE、地图、战斗、商店、排程、问答、擦雾、互动，加上两套抽出来的辅助类）。
>
> **要不要统一？** 我的判断是**值得，但优先级不高**：
> - **值得的理由**：签名已经高度一致（都是 name/parent/sprite/color），
>   抽一个 `VNUiBuild` 静态类零风险；
>   而且能顺便统一「默认 raycastTarget=false」「默认 font = VNFont.Asset」这些约定。
> - **不急的理由**：这些函数极其简单（十行以内）、几乎不会改、
>   而且各模块的版本有细微差异（有的默认吃射线、有的返回 RectTransform 有的返回 Image）。
>   统一时要小心不要为了「统一」而给每个函数加五个可选参数——那会比重复更糟。
>
> **如果要做**，建议：抽 `VNUiBuild.Image / Text / Node / Stretch / Center` 五个最基础的，
> 各模块的特化（比如羽毛球的 `CreateQuad` / `CreateLine`）留在原处。

### 16.3 槽位声明组件家族

**对话框 / 选项（进存档、可切换）**：

| 组件 | 必填槽位 | 出处 |
|---|---|---|
| [`VNDialogueSkin`](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs) | 无（全可选） | [Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:24](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L24) |
| [`VNChoiceSkin`](Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs) | container / buttonTemplate / buttonLabel | [Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs:22](Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs#L22) |

**系统菜单（全局主题，不进存档）**——全部继承 `VNSystemUiSkinBehaviour`：

| 组件 | 出处 |
|---|---|
| [`VNTitleMenuSkin`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenuSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNTitleMenuSkin.cs:9](Assets/Project/Scripts/VNEffects/Script/VNTitleMenuSkin.cs#L9) |
| [`VNConfigPanelSkin`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanelSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNConfigPanelSkin.cs:9](Assets/Project/Scripts/VNEffects/Script/VNConfigPanelSkin.cs#L9) |
| [`VNSaveLoadSkin`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNSaveLoadSkin.cs:9](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadSkin.cs#L9) |
| [`VNBacklogSkin`](Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs:9](Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs#L9) |
| [`VNQuickToolbarSkin`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs:13](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs#L13) |
| [`VNStatsHudSkin`](Assets/Project/Scripts/VNEffects/Script/VNStatsHudSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNStatsHudSkin.cs:6](Assets/Project/Scripts/VNEffects/Script/VNStatsHudSkin.cs#L6) |
| [`VNStatsPanelSkin`](Assets/Project/Scripts/VNEffects/Script/VNStatsPanelSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNStatsPanelSkin.cs:8](Assets/Project/Scripts/VNEffects/Script/VNStatsPanelSkin.cs#L8) |
| [`VNInventorySkin`](Assets/Project/Scripts/VNEffects/Script/VNInventorySkin.cs) | Assets/Project/Scripts/VNEffects/Script/VNInventorySkin.cs（未逐行核对行号） |
| [`VNCgGallerySkin`](Assets/Project/Scripts/VNEffects/Script/VNCgGallerySkin.cs) | Assets/Project/Scripts/VNEffects/Script/VNCgGallerySkin.cs（同上） |
| [`VNPlanSkin`](Assets/Project/Scripts/VNEffects/Script/VNPlanSkin.cs) | Assets/Project/Scripts/VNEffects/Script/VNPlanSkin.cs（同上） |
| [`VNResultPopupSkin`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNResultPopupSkin.cs:16](Assets/Project/Scripts/VNEffects/Script/VNResultPopupSkin.cs#L16) |
| [`VNTutorialSkin`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNTutorialSkin.cs:18](Assets/Project/Scripts/VNEffects/Script/VNTutorialSkin.cs#L18) |

**行/卡模板（不继承基类，只提供 `IsValid`）**：

| 组件 | 出处 |
|---|---|
| [`VNSaveSlotSkin`](Assets/Project/Scripts/VNEffects/Script/VNSaveSlotSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNSaveSlotSkin.cs:8](Assets/Project/Scripts/VNEffects/Script/VNSaveSlotSkin.cs#L8) |
| [`VNBacklogEntrySkin`](Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs:7](Assets/Project/Scripts/VNEffects/Script/VNBacklogEntrySkin.cs#L7) |
| [`VNStatsHudEntrySkin`](Assets/Project/Scripts/VNEffects/Script/VNStatsHudEntrySkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNStatsHudEntrySkin.cs:7](Assets/Project/Scripts/VNEffects/Script/VNStatsHudEntrySkin.cs#L7) |
| [`VNStatsPanelRowSkin`](Assets/Project/Scripts/VNEffects/Script/VNStatsPanelRowSkin.cs) | [Assets/Project/Scripts/VNEffects/Script/VNStatsPanelRowSkin.cs:7](Assets/Project/Scripts/VNEffects/Script/VNStatsPanelRowSkin.cs#L7) |
| [`VNToolbarActionSlot`](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs) | [Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs:8](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs#L8) |
| [`VNCgCellSkin`](Assets/Project/Scripts/VNEffects/Script/VNCgCellSkin.cs) / [`VNInventoryRowSkin`](Assets/Project/Scripts/VNEffects/Script/VNInventoryRowSkin.cs) / [`VNInventorySlotSkin`](Assets/Project/Scripts/VNEffects/Script/VNInventorySlotSkin.cs) / [`VNPlanSlotRowSkin`](Assets/Project/Scripts/VNEffects/Script/VNPlanSlotRowSkin.cs) / [`VNPlanActionRowSkin`](Assets/Project/Scripts/VNEffects/Script/VNPlanActionRowSkin.cs) | 同目录同名档案 |

### 16.4 通用组件

| 组件 | 用途 | 出处 |
|---|---|---|
| [`VNTypewriterText`](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs) | TMP 逐字打字机 | [Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:14](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L14) |
| [`VNNameplateStyle`](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs) | 名牌样式（纯数据 + 静态 Apply） | [Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:53](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L53) |
| [`VNTouchCursor`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs) | 自绘跟随光标（可换图/摆动/发光） | [Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:21](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L21) |
| `VNBadmintonQuad` | 四点自由四边形（画梯形） | [Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:17](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L17) |
| `VNPhotoStickerItem` | 可拖/缩/转/删的贴纸 | [Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs:17](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs#L17) |
| [`VNPhotoDoodleInput`](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs) | 指针 → 画布 uv 的输入板 | [Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs:12](Assets/Project/Scripts/VNEffects/Script/VNPhotoDoodle.cs#L12) |
| `VNTutorialAnchor` | prefab 上的教程锚点自动登记 | [Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:82](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L82) |
| [`VNTutorialMask`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs) | 暗幕挖洞（最多 4 洞） | [Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:25](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L25) |
| [`VNPhotoCapture`](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs) | 取景框截图（静态） | [Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:19](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L19) |

---

## 十七、可复用的 UI 反馈机制目录

「玩家做了一件事，怎么让他知道发生了什么」——本专案的全部手段。

### 17.1 提示类

| 机制 | 长什么样 | 调用 | 出处 |
|---|---|---|---|
| **Toast 中性卡** | 左上角滑入卡片，1.6 秒后滑出 | `VNToast.Show("已保存")` | [Assets/Project/Scripts/VNEffects/Script/VNToast.cs:53](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L53) |
| **Toast 带图标色条** | 图标 + 主题色 + 左侧涨跌竖条 | `VNToast.Show(msg, icon, iconColor, accent, hold)` | [Assets/Project/Scripts/VNEffects/Script/VNToast.cs:62](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L62) |
| **模式标签** | 右上角常驻 "AUTO ▶" / "SKIP ▶▶" | `VNToast.SetMode(label)` | [Assets/Project/Scripts/VNEffects/Script/VNToast.cs:85](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L85) |
| **角标** | 模式标签下一行，任务可领取数 | `VNToast.SetBadge(label)` | [Assets/Project/Scripts/VNEffects/Script/VNToast.cs:92](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L92) |
| **属性飘字** | HUD 条目上方 "+3" 上飘淡出 | [`VNStatsHud.SpawnFloatingDelta`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（内部） | [Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:386](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L386) |

**Toast 的堆叠机制**（[`VNToast`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs) 类注释，
`[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:15](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L15)`）：
> 与旧版（单条居中纯文字）的关键差别：**多条不再互相覆盖**。
> 新卡片始终出现在最上面那一格，已有卡片顺次下移；超过上限时最老的提前退场。

```csharp
const int MaxCards = 5;          // 同屏最多几张，超出时最老的立刻开始退场
```
> 出处：`VNToast.MaxCards`（[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:24](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L24)）

```csharp
EnsureCanvas();
if (_cards.Count >= MaxCards) Dismiss(_cards[_cards.Count - 1]);

var card = BuildCard(message, icon, iconColor, accent);
_cards.Insert(0, card); // 新卡永远占最上面那一格
Relayout(false);
```
> 出处：`VNToast.Show`（[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:65](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L65)）

```csharp
/// <summary>按当前顺序把卡片排到各自的格子上（animate=false 用于刚插入的新卡）</summary>
static void Relayout(bool animate)
{
    for (int i = 0; i < _cards.Count; i++)
    {
        var card = _cards[i];
        if (card.rect == null) continue;
        float y = -i * (CardHeight + CardGap);
        if (Mathf.Approximately(card.rect.anchoredPosition.y, y)) continue;

        if (animate && !card.leaving)
            card.rect.DOAnchorPosY(y, 0.22f).SetEase(Ease.OutCubic)
                    .SetUpdate(true).SetLink(card.rect.gameObject);
        else
            card.rect.anchoredPosition =
                new Vector2(card.rect.anchoredPosition.x, y);
    }
}
```
> 出处：`VNToast.Relayout`（[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:135](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L135)）

> **三个值得学的细节**：
> ① `Mathf.Approximately` 跳过已在位的卡片——避免每次都重启补间；
> ② `!card.leaving` 保证正在退场的卡片不被重新排位；
> ③ `animate = false` 给刚插入的新卡（它要从 `EnterOffsetX` 滑进来，不能先补间到位）。

**所有计时 `SetUpdate(true)`**：
```csharp
/// 计时全部 `SetUpdate(true)`：Skip 快进时 DOTween.timeScale 会被改，
/// 提示不该跟着变速；事件模块进行中也照常显示。
```
> 出处：`VNToast` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:21](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L21)）

### 17.2 强调类（把视线拉过去）

| 机制 | 实现 | 出处 |
|---|---|---|
| **数值滚动** | `DOVirtual.Int(from, to, 0.45f, ...)` | `VNStatsHud.RollValue`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:377](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L377)） |
| **数字弹跳 + 染色** | `DOScale(1.35→1, OutBack)` + `DOColor` | `VNStatsHud.RefreshHud`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:347](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L347)） |
| **图标弹跳** | `DOPunchScale(0.35, 0.42s, 9, 0.7)` | `VNStatsHud.RefreshHud`（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:357](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L357)） |
| **面板弹跳（连打）** | `DOPunchScale(0.04, 0.15s, 8, 0.6)` | [`VNQteModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs:68](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs#L68)） |
| **面板弹入** | `localScale 0.92 → DOScale(1, 0.28s, OutBack)` | [`VNShopModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs:151](Assets/Project/Scripts/VNEffects/Script/VNShopModule.cs#L151)）、[`VNPlanModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs:249](Assets/Project/Scripts/VNEffects/Script/VNPlanModule.cs#L249)）、[`VNInteractionModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:810](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L810)） |
| **确认框弹入** | `localScale 0.8 → DOScale(1, 0.2s, OutBack)` | [`VNBadmintonModule.OpenConfirm`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:526](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L526)） |
| **卡片翻页弹入** | 下沉 18px 回位 + 淡入 | [`VNTutorialPlayer.ApplyStep`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:308](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L308)） |
| **对话框出现** | 下方 70px 滑入 + 淡入，`OutBack 1.2` | [`VNDialogueBox.Show`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs)（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:608](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L608)） |
| **选项错落飞入** | 右侧 90px 滑入，延迟 `index * 0.09` | [`VNChoicePanel.PlayEntrance`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs)（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:214](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L214)） |
| **标题上浮** | 36px 下方回位，1.1 秒 OutCubic | [`VNTitleMenu.PlayEntrance`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:281](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L281)） |

> **`Ease.OutBack` 是本专案「弹出」的统一语言**，出现了至少六次。
> 参数惯例：面板 0.28 秒、确认框 0.2 秒、对话框 0.45 秒（带 1.2 的过冲量）。
> **保持一套 ease + 一组时长，整个游戏的手感就统一了**——
> 这比每个面板各自调参重要得多。

### 17.3 状态指示类

| 机制 | 实现 | 出处 |
|---|---|---|
| **继续箭头呼吸** | `DOAnchorPosY(-7, 0.55s, InOutSine, Yoyo, -1)` | `VNDialogueBox.ShowArrow`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:740](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L740)） |
| **悬停扫光 + 微放大** | `fx.PlayShine(0.5f)` + `DOScale(1.045)` | `VNChoicePanel.FinishButton`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:409](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L409)） |
| **选中闪光 + 落选溶解** | `DOFlash` + `DODissolve(0, 0.45s)` | `VNChoicePanel.Choose`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:443](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L443)） |
| **对话框流光边框** | `VNImageEffectController.StartShineLoop(2.2, 1.3)` | `VNDialogueBox.Bind`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:314](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L314)） |
| **工具条激活态** | 换一个 Graphic 的颜色 | [`VNToolbarActionSlot.SetActiveState`](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs)（[Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs:17](Assets/Project/Scripts/VNEffects/Script/VNToolbarActionSlot.cs#L17)） |
| **页签选中态** | 两个 targetGraphic 换色 | [`VNSaveLoadPanel.SetMode`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:179](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L179)） |
| **置灰不可选** | 底色 ×0.55、文字 alpha 0.45、Button.interactable=false | `VNChoicePanel.CreateDefaultButton`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:326](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L326)） |
| **教程洞口描边呼吸** | shader 参数 × `sin(unscaledTime*3.4)` | [`VNTutorialMask.Sync`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:168](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L168)） |
| **倒计时最后冲刺** | 变红 + 脉动 + 轻抖 | [`VNQuizModule.UrgentSeconds`](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:44](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L44)） |

### 17.4 听觉反馈

| 机制 | 实现 | 出处 |
|---|---|---|
| **打字音（带节流 + 随机音高）** | `VNAudio.TypeTick()`（[`Assets/Project/Scripts/VNEffects/Script/VNAudio.cs:370`](Assets/Project/Scripts/VNEffects/Script/VNAudio.cs#L370)） | [Assets/Project/Scripts/VNEffects/Script/VNAudio.cs:370](Assets/Project/Scripts/VNEffects/Script/VNAudio.cs#L370) |
| **QTE 连打音** | `_audio.PlaySe("se1")` | `VNQteModule.Update`（[Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs:65](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs#L65)） |
| **教程步骤音** | `audio?.PlaySe(step.se)` | `VNTutorialPlayer.PlayStepSe`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:344](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L344)） |
| **擦拭循环音变 pitch** | 一段 1 秒滤波白噪改 pitch | （见 CLAUDE.md 的 [`VNFogSfx`](Assets/Project/Scripts/VNEffects/Script/VNFogSfx.cs) 说明；本次未逐行核对） |

### 17.5 确认与防误触

| 机制 | 实现 | 出处 |
|---|---|---|
| **覆盖存档二次确认** | `ShowConfirm(msg, onYes)` + `SetAsLastSibling` | `VNSaveLoadPanel.ShowConfirm`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:255](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L255)） |
| **退出游戏二次确认** | prefab 里的 `quitConfirmRoot` | `VNTitleMenu.BindCustomSkin`（[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:236](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L236)） |
| **认输二次确认** | 模块内自建暗幕 + 两个按钮 | `VNBadmintonModule.OpenConfirm`（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:495](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L495)） |
| **AI 聊天 ESC 确认** | 唯一吃射线的自绘层 | [`VNAiTalkModule`](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs) 确认框段（[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:595](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L595)） |
| **结算弹窗输入延迟** | `inputDelay = 0.4f`，`_shownAt` 初始 MaxValue | [`VNResultPopupModule.inputDelay`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:13](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L13)） |
| **教程每步最短停留** | `MinStepTime = 0.22f` | `VNTutorialPlayer.MinStepTime`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:44](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L44)） |
| **选中后延迟 0.8 秒才回调** | `DOVirtual.DelayedCall(0.8f, ...)` | `VNChoicePanel.Choose`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:456](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L456)） |
| **淡出跨两帧再解除暂停** | [`WaitForCompletion()`](Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUnityVersion.cs#L81) | `VNTutorialPlayer.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:206](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L206)） |

---

## 十八、新增一个 UI 画面的完整流程（三条路线）

这一章是「照着做」的操作手册。

### 路线 A：加一个系统面板（比如「成就」面板，K 键打开）

**1. 建组件**
```csharp
public class VNAchievementPanel : MonoBehaviour
{
    Canvas _canvas;
    GameObject _panel;
    bool _open;
    public bool IsOpen => _open;

    void Awake() => VNLocale.LanguageChanged += OnLanguageChanged;
    void OnDestroy() => VNLocale.LanguageChanged -= OnLanguageChanged;

    void OnLanguageChanged()
    {
        if (_open) Close();
        if (_canvas != null) Destroy(_canvas.gameObject);
        _canvas = null; _panel = null; /* 所有缓存字段都要清 */
    }

    public void Toggle() { if (_open) Close(); else Open(); }
    public void Open()  { Build(); Rebuild(); _panel.SetActive(true); _open = true; }
    public void Close() { if (!_open) return; _panel.SetActive(false); _open = false; }

    void Build()
    {
        if (_panel != null) return;
        // 建 Canvas（Overlay，sortingOrder 600 一档）+ CanvasScaler(1920×1080)
        // 皮肤 prefab 优先，缺失退回程序化
    }
}
```
> 模板来源：[`VNBacklog`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:9](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L9)）
> 与 [`VNQuestLog`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:18](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L18)）

**2. 在 Runner 里注册**（三处）
- 加字段（照 [`VNScriptRunner._questLog`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)，
  `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:62](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L62)` 的位置）
- 在 `Start` 里「先找、找不到就 new」（照 `VNScriptRunner.Start`，
  `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:132](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L132)` 那一段）
- 加 `RequestAchievements()`（照 `VNScriptRunner.RequestQuestLog`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1709`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1709)），
  `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1709](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1709)`，
  记得 `if (_eventActive) return;`）

**3. 在 Update 的模态链里插两段**
- 「面板打开时只响应 K / Esc」的分支（照
  `VNScriptRunner.Update` 的任务日志分支，
  `[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2032](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2032)`）
- 「K 键打开」（照同方法 `:2104`）
- **位置要选对**：放在 CG 画廊分支之后、标题菜单之前

**4. 补 `IsSecretPhotoIconAllowed`**
在 `VNScriptRunner.IsSecretPhotoIconAllowed`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1799`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1799)）
（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1799](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1799)`）里加一行
`if (_achievements != null && _achievements.IsOpen) return false;`
——**这一步最容易漏**。

**5. 可选：加进快捷功能条**
- [`VNToolbarAction`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs) 枚举加一项（`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs:6](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs#L6)`）
- `VNQuickToolbar.LabelFor`（[`Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:83`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L83)） 加一个 case（`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:83](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L83)`）
- [`VNQuickToolbar.Execute`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs) 加一个 case（`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:104](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L104)`）
- 在工具条 prefab 里复制一个按钮、改枚举值

**6. 可选：加皮肤支持**
- 建 `VNAchievementSkin : VNSystemUiSkinBehaviour`（照
  [`VNBacklogSkin`](Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs)，`[Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs:9](Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs#L9)`）
- [`VNSystemUiSkinSet`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs) 加字段（`[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:11](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs#L11)`）
- `VNSystemUiSkinExporter.ExportAll`（[`Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:23`](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L23)） 加一个 `BuildAchievement()`（
  `[Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:23](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L23)`）

**7. 本地化**
所有玩家可见文字走 `VNLocale.T("achievement.title")`，
翻译写进 `Assets/Resources/VNLocale/ui.zh.txt` 等三份表
（格式见 `VNLocale.ParseTable`（[`Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:124`](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L124)），`[Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:124](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L124)`）。

### 路线 B：加一个玩法事件模块（比如「钓鱼」）

**1. 建模块类**
```csharp
public class VNFishingModule : VNEventModule
{
    [Header("定义资产库（event fishing id:xx 查找）")]
    public List<VNFishingDef> defs = new List<VNFishingDef>();

    enum Phase { Idle, Casting, Waiting, Hooked, Ending }
    Phase _phase = Phase.Idle;

    protected override void OnLaunch(VNEventContext ctx)
    {
        var cfg = VNGameConfig.Active;
        if (cfg != null) VNGameConfig.ApplyList(cfg.fishings, ref defs);
        // 参数解析，不成立就 Done("") 提前返回
        BuildUi();
        _phase = Phase.Casting;
    }

    void Update()
    {
        if (VNPause.IsPaused) return;      // 铁律②，必须在读输入之前
        if (_phase == Phase.Idle || _phase == Phase.Ending) return;
        float dt = VNTime.Delta;
        ...
    }

    void Finish(string outcome)
    {
        _phase = Phase.Ending;
        DOVirtual.DelayedCall(0.8f, () => Done(outcome), true).SetLink(gameObject);
    }

    public override void CancelForDebug() { /* 清理光标/材质/锚点 */ }
    void OnDestroy() { /* 同上 */ }
}
```
> 骨架来源：[`VNQteModule`](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs:19](Assets/Project/Scripts/VNEffects/Script/VNQteModule.cs#L19)）

**2. 遵守三铁律**
- 只碰自己的 UI 子树与 [`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs)；要破例就在类注释里写明边界与还原路径
- 计时 `VNTime.Delta`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:144`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L144)），Tween 全部 `SetUpdate(true)`
- Tween 全部 `SetLink(gameObject)`

**3. 决定射线策略**
- 独占全屏 → 铺一层吃射线的暗幕
- 要让对话框继续工作 → **不铺暗幕**，自绘一律 `raycastTarget = false`
  （照 [`VNInteractionModule.BuildUi`](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs)，
  `[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:761](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L761)`）

**4. 登记到注册表**
写一个 Installer（照 `VNQuestBoardInstaller.Install`（[`Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs:23`](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs#L23)），
`[Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs:23](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs#L23)`）：
- 找场景里的 [`VNEventRegistry`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs)，找不到就弹对话框提示
- 建一个**禁用的、带 RectTransform 的** 模板物体挂到注册表下
- 加进 `registry.modules`
- 顺便把工程里的定义资产扫进 [`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)
- 结尾 `VNAssetLibraryEvents.RaiseChanged()`（[`Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs:26`](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L26)）
  （`[Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs:26](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L26)`）

**5. 教程锚点（可选）**
`VNTutorialAnchors.Register(id, rect)` + `OnDestroy` 里 `Unregister`
（照 [`VNBadmintonModule.RegisterTutorialAnchors`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs)，
`[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:376](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L376)`）。

**6. 剧本编辑器 Schema**
在 [`VNScenarioSchema`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs) 的 `EventVariants` 表里补一行，把 `ctx.Kw(...)` 用到的参数登记上
（见 CLAUDE.md 与第十九章 19.3）。

**7. 结果契约与 Lint**
- 结果名用 const 字符串（照 `VNAiTalkModule.OutcomeUp`（[`Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:42`](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L42)），
  `[Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs:42](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs#L42)`）
- 如果有「失败/拒绝」这类异常路径，加进 Lint 强制要求接住

### 路线 C：加一套 UI 皮肤（不写代码）

**1. 导出模板**
- 对话框/选项：Tools → VN Effects → UI 皮肤 UI Skins → 导出皮肤模板
  （`VNUiSkinExporter.ExportAll`（[`Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:30`](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L30)），`[Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:30](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L30)`）
- 系统菜单：Tools → VN Effects → UI 皮肤 UI Skins → 系统主题：导出默认模板
  （[`VNSystemUiSkinExporter.ExportAll`](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs)，`[Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs:23](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs#L23)`）

**2. 复制 prefab 改**
- 随便加装饰节点，代码只认槽位
- 记住：对话框皮肤会被拉满整个画布，`panel` 想放哪就锚哪
  （[`VNDialogueSkin`](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs) 类注释，`[Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:16](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L16)`）
- 选项模板要保持**禁用**、根上要有 Image
  （[`VNChoiceSkin`](Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs) 类注释，`[Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs:17](Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs#L17)`）

**3. 登记**
- 对话框/选项：`VNGameConfig.dialogueSkins`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:126`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L126)） / `choiceSkins` 里加 id + prefab
  （`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:126](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L126)`）
- 系统菜单：把新的 `VNSystemUiSkinSet` 资产挂到 `VNGameConfig.systemUiSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)）
  （`[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)`）

**4. 剧本切换**
`ui dialogue <id>` / `ui choice <id>` / `ui name <样式>`
（`VNScriptRunner.Dispatch` 的 ui 分支，
`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2401](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2401)`）。

**5. 验证**
如果必填槽位缺失，运行时会报中文错误并指出缺哪个
（`VNSystemUiSkinUtility.Instantiate`（[`Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs:29`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs#L29)），
`[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs:42](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs#L42)`）。

---

## 十九、Editor 端 IMGUI 工具大章

`Assets/Project/Scripts/VNEffects/Editor/` 下有 45 个档案，总量比运行时 UI 还大
（剧本编辑器 171KB、镜头编排 154KB、Lint 110KB、场景生成器 75KB）。
它们和游戏内 uGUI 是**完全不同的技术栈**，这一章单独讲。

### 19.1 IMGUI 与 uGUI 的本质差别

| 维度 | uGUI（游戏内） | IMGUI（编辑器） |
|---|---|---|
| 范式 | 保留模式（建 GameObject，之后改属性） | **立即模式**（每帧从头画一遍） |
| 状态 | 存在 GameObject / 组件字段里 | 存在窗口类的字段里 |
| 布局 | 锚点 / LayoutGroup 自动算 | 自己算 `Rect`，或用 `GUILayout` |
| 事件 | EventSystem 射线派发 | `Event.current` 每帧轮询，用 Event.Use 消费 |
| 生命周期 | Awake/Start/OnDestroy | OnEnable/OnGUI/OnDisable + **域重载会重建整个窗口** |
| 跨 Play Mode 存活 | 场景物体自然存活 | **必须 `[SerializeField]` + `ISerializationCallbackReceiver`** |

**最大的坑就是最后一条**：进 Play Mode 会触发域重载（domain reload），
所有 EditorWindow 被序列化后重建，**普通字段一律清空**。

### 19.2 剧本编辑器 VNScenarioEditorWindow（171KB，全专案最大的单档）

#### 19.2.1 域重载存活机制

```csharp
/// 【域重载存活】窗口进 Play Mode 会被 Unity 序列化后重建（domain reload），
/// 普通字段一律清空。文档正文/路径/脏标记/撤销栈全部走 [SerializeField]，
/// 由 ISerializationCallbackReceiver 在重载前把 _doc 拍成文本、OnEnable 里再解析回来。
/// 新增任何"关掉窗口也不该丢"的状态时，记得一并加进 OnBeforeSerialize / OnEnable。
public class VNScenarioEditorWindow : EditorWindow, ISerializationCallbackReceiver
```
> 出处：[`VNScenarioEditorWindow`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:22](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L22)）

序列化字段组：
```csharp
// ↓ 域重载存活组（Unity 会替我们保存/恢复；_doc 本体走 _docText 中转）
[SerializeField] string _docText = "";
[SerializeField] string _path = "";
[SerializeField] long _fileTimeTicks;
[SerializeField] bool _dirty;
[SerializeField] bool _externalChanged;
[SerializeField] Tab _tab;
[SerializeField] bool _showCategoryColors;
[SerializeField] bool _rebuildStateBeforePlay = true;
[SerializeField] int _restoredListIndex = -1;
[SerializeField] List<string> _undoStackSerialized = new List<string>();
[SerializeField] List<string> _redoStackSerialized = new List<string>();
[SerializeField] Vector2 _scroll;
[SerializeField] int _lastPlayedLine = -1;
[SerializeField] string _search = "";
```
> 出处：`VNScenarioEditorWindow._docText`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:36](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L36)）

**`_doc` 本体不能直接序列化**（它是一个含多态行的复杂对象树），
所以走「拍成文本再解析回来」：
```csharp
/// <summary>域重载前的最后一刻：把 _doc 与两个撤销栈拍进可序列化字段</summary>
public void OnBeforeSerialize()
{
    // 注意：这里跑在序列化线程语境下，只能做纯 C# 运算，不要碰 Unity API
    if (_doc != null) _docText = _doc.GenerateText();
    _fileTimeTicks = _fileTime.Ticks;
    _restoredListIndex = _list != null ? _list.index : -1;
    _undoStackSerialized = new List<string>(_undoStack);
    _redoStackSerialized = new List<string>(_redoStack);
}

/// <summary>反序列化时不能碰 Unity API，实际还原挪到 OnEnable</summary>
public void OnAfterDeserialize() { }
```
> 出处：`VNScenarioEditorWindow.OnBeforeSerialize`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:279](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L279)）

还原在 `OnEnable`：
```csharp
void RestoreAfterDomainReload()
{
    _fileTime = _fileTimeTicks > 0
        ? new System.DateTime(_fileTimeTicks, System.DateTimeKind.Utc)
        : default;
    if (!string.IsNullOrEmpty(_docText))
        _doc = VNScenarioDoc.Parse(_docText);

    _undoStack.Clear();
    if (_undoStackSerialized != null) _undoStack.AddRange(_undoStackSerialized);
    _redoStack.Clear();
    if (_redoStackSerialized != null) _redoStack.AddRange(_redoStackSerialized);
}
```
> 出处：`VNScenarioEditorWindow.RestoreAfterDomainReload`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:264](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L264)）

> **三条必须知道的规则**：
> 1. **`OnBeforeSerialize` 跑在序列化线程语境下，不能碰 Unity API**
>    （注释在 `:281`）。`AssetDatabase.LoadAssetAtPath`、`Debug.Log`、`FindObjectOfType`
>    在这里调都可能出问题。只做纯 C# 运算。
> 2. **`OnAfterDeserialize` 同样不能碰 Unity API**，所以实际还原挪到 `OnEnable`。
> 3. **`DateTime` 不能直接 `[SerializeField]`**，所以存 `long _fileTimeTicks`。
>    这是 Unity 序列化的常见限制（`DateTime`、`TimeSpan`、`Nullable<T>`、
>    `Dictionary` 都不行）。
>
> **同一模式在 [`VNAiStudioWindow`](Assets/Project/Scripts/VNEffects/Editor/VNAiStudioWindow.cs) 里也用了**
> （类注释，`[Assets/Project/Scripts/VNEffects/Editor/VNAiStudioWindow.cs:18](Assets/Project/Scripts/VNEffects/Editor/VNAiStudioWindow.cs#L18)`），
> 而且注释里同样强调「★ 加新窗口状态时必须同时改 OnBeforeSerialize 和 OnEnable」
> （`[Assets/Project/Scripts/VNEffects/Editor/VNAiStudioWindow.cs:23](Assets/Project/Scripts/VNEffects/Editor/VNAiStudioWindow.cs#L23)`）。

#### 19.2.2 ReorderableList：行高回调实现「隐藏行」

```csharp
void BuildList()
{
    _list = new ReorderableList(_doc.rows, typeof(VNRow), true, false, true, true)
    {
        multiSelect = true,   // Shift=连选 / Ctrl=点选，拖动整体移动
        elementHeightCallback = i => RowHeight(_doc.rows[i]),
        drawElementCallback = DrawRow,
        onAddDropdownCallback = (rect, list) => ShowAddSearch(rect),
        onRemoveCallback = list => { ... },
        onReorderCallback = list => { PushUndo(_frameSnapshot); Bump(); },
    };
}
```
> 出处：`VNScenarioEditorWindow.BuildList`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:331](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L331)）

**「隐注释/空行」与「搜索过滤」都实现成「行高返回 0」**：
```csharp
float RowHeight(VNRow r)
{
    if (IsRowCollapsed(r)) return 0f;
    int lines = 1;
    if (EventParamLine(r)) lines += 1;   // event 的模块专属参数独占一行
    if (r.options != null) lines += r.options.Count;
    if (r.camLines != null) lines += r.camLines.Count;
    return lines * LineH2 + 6f;
}
```
> 出处：`VNScenarioEditorWindow.RowHeight`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1592](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1592)）

```csharp
/// <summary>
/// 这一行现在是不是折成了零高度——「隐注释/空行」或搜索过滤任一命中即是。
///
/// 【为什么不做过滤视图】列表绑的是 _doc.rows 本体，高度归零就够了：
/// 索引不变，行号换算 / 多选 / 拖动排序 / 删除全部零改动。
/// 换成过滤后的子列表，「拖到被过滤掉的行之间算什么」这种问题会没完没了。
/// </summary>
bool IsRowCollapsed(VNRow r) => IsHiddenRow(r) || IsFilteredOut(r);
```
> 出处：`VNScenarioEditorWindow.IsRowCollapsed`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1625](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1625)）

> **这是一个极其漂亮的取巧**。
> 「列表过滤」的常规做法是建一个过滤后的子列表，
> 但那样索引就变了，拖动排序、多选、删除、行号换算全都要跟着改一套映射。
> **把高度设成 0 = 视觉上消失，但数据结构原封不动**。
>
> 代价：被隐藏的行仍然参与遍历（几千行时会有一点开销），
> 而且 ReorderableList 内部可能仍为它做一些计算。
> 在剧本几百行的量级下完全值得。

**只隐藏空行与 `#` 注释，不隐藏孤儿行**：
```csharp
/// <summary>
/// 「隐注释/空行」开着时这一行是否折成零高度。
///
/// 只认空行与 `#` 注释——VNRowKind.Raw 还兜着两种语法残留：
/// 前面没有 choice 的孤儿 `*` 选项行、前面没有 camseq 的孤儿 `>` 路径点行。
/// 那两种一旦藏起来就再也找不回来了（Issues 面板定位过去也是一片空白），
/// 所以必须留在列表里显形。
/// </summary>
bool IsHiddenRow(VNRow r)
{
    if (!_hideRawRows || r.kind != VNRowKind.Raw) return false;
    string text = r.raw == null ? "" : r.raw.TrimStart();
    return text.Length == 0 || text.StartsWith("#");
}
```
> 出处：`VNScenarioEditorWindow.IsHiddenRow`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1618](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1618)）

> **「隐藏功能不能把错误一起隐藏掉」是很重要的编辑器设计原则**。
> 一个偷懒的实现（`kind == Raw` 就藏）会让语法错误的行凭空消失，
> 玩家看到 Issues 面板报错但点过去什么都没有。

#### 19.2.3 IMGUI 里改列表长度必须延后

```csharp
// Enter / Shift+Enter 快捷插入台词行：KeyDown 期间不能改列表长度
// （IMGUI 布局已在 Layout 事件里定好），所以只记位置，留到下一个 Layout 再插
const string SayFocusControl = "VNScenarioEditor.NewSayRow";
int _pendingInsertAt = -1;   // 待插入的行号
int _pendingFocusRow = -1;   // 插完后要把键盘焦点送进去的行号
```
> 出处：`VNScenarioEditorWindow._pendingInsertAt`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:68](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L68)）

```csharp
// 搜索弹窗 / Ctrl+E 命令面板：回调跑在别的窗口的 GUI 里，改行数一律留到下一个
// Layout 事件（和上面那套 _pendingInsertAt 同理）
VNRow _pendingNewRow;
bool _pendingNewRowAbove;
bool _pendingPalette;        // Ctrl+E 请求：PopupWindow 只能在 OnGUI 里开
```
> 出处：`VNScenarioEditorWindow._pendingNewRow`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:74](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L74)）

> **IMGUI 的核心约束**：一次 OnGUI 会被调用多次，
> `EventType.Layout` 那一次决定控件数量与布局，
> 之后的 `Repaint` / `MouseDown` / `KeyDown` 必须画出**完全相同数量**的控件。
> 在 KeyDown 里改列表长度 → 下一个 Repaint 控件数对不上 →
> `ArgumentException: Getting control 5's position in a group with only 4 controls`。
>
> **解法就是本专案的做法：记一个 pending，在下一个 Layout 事件里执行。**
> 这是所有 IMGUI 工具都要处理的问题。

**同步返回的弹窗结果槽**：
```csharp
// 参数格搜索弹窗的回填槽：PopupString 是同步返回的（camseq 路径点、choice 选项行
// 的值都不在 VNRow.values 里，靠调用方自己写回），所以弹窗只能把结果放这儿，
// 由下一帧的 PopupString 同步 return 出去，绝不能像 SpritePopup 那样回调直写 values
readonly Dictionary<(VNRow, string), string> _popupResults =
    new Dictionary<(VNRow, string), string>();
```
> 出处：`VNScenarioEditorWindow._popupResults`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:79](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L79)）

> **这段揭示了一个设计约束**：
> 有些值存在 `VNRow.values`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioDoc.cs:44`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioDoc.cs#L44)） 字典里（回调可以直接写），
> 有些值（camseq 路径点、choice 选项）存在别的结构里、由调用方自己写回，
> 所以弹窗必须**同步返回**而不能异步回调。
> 解法是「弹窗把结果放进槽 → 下一帧调用方同步取出」。
> `CLAUDE.md` 里的规则「路径点行禁用 `CharacterPopup` / `SpritePopup`」就是这条的延伸。

#### 19.2.4 从选中行播放：冷启动 Bridge + 热重播

**统一入口**：
```csharp
/// <summary>
/// 统一播放入口：校验 → 静默自动保存 → Play Mode 中热重播 / 否则冷启动 Bridge。
/// Play Mode 中走热路径时完全不触发域重载，改一行到看到效果约等于一次 Repaint。
/// </summary>
void PlayFromSourceLine(int sourceLine)
```
> 出处：`VNScenarioEditorWindow.PlayFromSourceLine`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1458](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1458)）

**播放前的三道校验**：
1. 有 error 级 Issues 就切到 Issues 页并弹对话框（`:1462`）
2. 那一行之后没有可播放的命令就报错（`:1480`）
3. 静默自动保存（`:1487`）

**自动保存的三个条件**：
```csharp
/// <summary>
/// 播放前静默把改动写盘，省得「进 Play Mode 前忘了存」。
/// 未命名（没有路径）不弹保存框，直接拿内存文本播；
/// 磁盘已被别处改过（_externalChanged）也不写，避免静默覆盖掉别人的改动。
/// </summary>
void AutoSaveBeforePlay()
{
    if (!_dirty || string.IsNullOrEmpty(_path) || _externalChanged) return;
    SaveFile(false);
}
```
> 出处：`VNScenarioEditorWindow.AutoSaveBeforePlay`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1504](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1504)）

> **「静默自动保存」这种便利功能一定要想清楚什么时候不该做**。
> 这里的三个条件（没改动 / 没路径 / 磁盘被别人改过）都是必要的，
> 尤其第三个——静默覆盖掉别人的改动是不可原谅的。

**热重播（Play Mode 中）**：
```csharp
/// <summary>Play Mode 中原地重播：不退出 Play Mode，不触发域重载</summary>
void HotReplay(string source, int sourceLine)
{
    VNScriptRunner runner = ResolveRunner();
    if (runner == null || !runner.IsInitialized) { /* 报错 */ return; }
    // 让 Runner 知道现在调试的是哪个剧本：翻译查表与 chapter/跨文件 jump 都按它算
    runner.SetDebugScript(OpenFileAsset());
    runner.PlayFromSourceLine(source, sourceLine, _rebuildStateBeforePlay);
    Repaint();
}
```
> 出处：`VNScenarioEditorWindow.HotReplay`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1511](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1511)）

**冷启动 Bridge（不在 Play Mode 时）**：

这是整段最巧妙的部分——要「进 Play Mode 之后再执行某件事」，
但进 Play Mode 会域重载把一切清空。解法是 `SessionState`：

```csharp
[InitializeOnLoad]
static class VNPlayFromLineBridge
{
    const string PendingKey = "VNEffects.PlayFromLine.Pending";
    const string SourceKey = "VNEffects.PlayFromLine.Source";
    ...
    static VNPlayFromLineBridge()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void Request(string source, int sourceLine, bool rebuildState,
        string assetPath)
    {
        SessionState.SetBool(PendingKey, true);
        SessionState.SetString(SourceKey, source);
        SessionState.SetInt(LineKey, Mathf.Max(1, sourceLine));
        SessionState.SetBool(RebuildKey, rebuildState);
        SessionState.SetString(AssetKey, assetPath ?? "");
        EditorApplication.isPlaying = true;
    }
```
> 出处：`VNPlayFromLineBridge`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:3686](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L3686)）

进 Play Mode 后轮询等 Runner 初始化：
```csharp
static void OnPlayModeStateChanged(PlayModeStateChange state)
{
    if (state == PlayModeStateChange.EnteredPlayMode &&
        SessionState.GetBool(PendingKey, false))
    {
        _remainingAttempts = 180;
        EditorApplication.update -= TryStartRunner;
        EditorApplication.update += TryStartRunner;
    }
    else if (state == PlayModeStateChange.EnteredEditMode)
    {
        EditorApplication.update -= TryStartRunner;
    }
}
```
> 出处：`VNPlayFromLineBridge.OnPlayModeStateChanged`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:3712](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L3712)）

```csharp
static void TryStartRunner()
{
    if (!EditorApplication.isPlaying) { EditorApplication.update -= TryStartRunner; return; }

    var runner = Object.FindFirstObjectByType<VNScriptRunner>();
    if (runner == null || !runner.IsInitialized)
    {
        if (--_remainingAttempts > 0) return;
        Debug.LogError("[VNScript] 从选中行播放失败：找不到已初始化的 VNScriptRunner");
        ClearRequest();
        EditorApplication.update -= TryStartRunner;
        return;
    }
    ...
}
```
> 出处：`VNPlayFromLineBridge.TryStartRunner`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:3727](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L3727)）

> **三个关键点**：
> 1. **`SessionState` 是跨域重载存活的键值存储**（比 `EditorPrefs` 好——
>    它只在这次编辑器会话内有效，关掉 Unity 就没了，不会污染用户配置）。
> 2. **`[InitializeOnLoad]` + 静态构造函数**：域重载后自动重新订阅
>    `playModeStateChanged`。注意先 `-=` 再 `+=`，防重复订阅。
> 3. **`_remainingAttempts = 180`（约 3 秒）的重试上限**：
>    Runner 的 `Start` 不一定在 `EnteredPlayMode` 那一帧就跑完，
>    要等它；但也不能无限等（场景里根本没有 Runner 的情况）。
>
> **这一整套是「编辑器发起、运行时执行」的标准模式**，
> 任何「点一下按钮进 Play Mode 并做某事」的工具都可以照抄。

**播放跟随高亮用 10Hz 轮询**：
```csharp
/// <summary>10Hz 轮询 Runner 的当前行，驱动播放跟随高亮（不用给运行时加事件）</summary>
void OnInspectorUpdate()
{
    int line = -1;
    if (EditorApplication.isPlaying)
    {
        VNScriptRunner runner = ResolveRunner();
        // 播的必须是本窗口打开的这个文件，否则行号对不上，宁可不高亮
        if (runner != null && runner.IsRunning && IsRunnerOnOpenFile(runner))
            line = runner.CurrentLine;
    }

    if (line == _playingLine) return;
    _playingLine = line;
    _playingRow = line > 0 ? RowForSourceLine(line) : -1;
    if (_followPlayback && _playingRow >= 0 && _tab == Tab.Edit)
        ScrollRowIntoView(_playingRow);
    Repaint();
}
```
> 出处：`VNScenarioEditorWindow.OnInspectorUpdate`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:312](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L312)）

`if (line == _playingLine) return;` 保证只在行号真的变了才 `Repaint`——
避免 10Hz 无脑重绘一个大窗口。

#### 19.2.5 事件订阅与退订

```csharp
void OnEnable()
{
    minSize = MinWindowSize;   // 对已存在的窗口也生效，否则拖不小
    LoadCategoryColors();
    _stagePreview = EditorPrefs.GetBool(StagePreviewPref, true);
    ...
    // 素材库改动后自动重建下拉候选（必须在 OnDisable 里退订，见 VNAssetLibraryEvents）
    VNAssetLibraryEvents.Changed += OnAssetLibraryChanged;
    RestoreAfterDomainReload();
    BuildList();
    ...
}

void OnDisable()
{
    VNAssetLibraryEvents.Changed -= OnAssetLibraryChanged;
    StopAudioPreview();
}
```
> 出处：`VNScenarioEditorWindow.OnEnable`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:240](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L240)）
> 与 `VNScenarioEditorWindow.OnDisable`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:298](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L298)）

[`VNAssetLibraryEvents`](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs) 的类注释把风险说得很清楚：
```csharp
/// 【订阅方必须退订】
/// 这是 static 事件，订阅者是 EditorWindow 实例。窗口关闭 / 域重载会重建窗口，
/// 不在 `OnDisable` 退订的话，事件会一直攥着已销毁窗口的引用，
/// 下次广播时对着"假 null"的窗口调方法 —— 典型的编辑器内存泄漏 + 幽灵异常。
/// 一律 `OnEnable` 订阅、`OnDisable` 退订。
```
> 出处：`VNAssetLibraryEvents` 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs:14](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L14)）

**发射方只在真的改了才广播**：
```csharp
/// <summary>写方在改完并 ApplyModifiedProperties 之后调用。</summary>
public static void RaiseChanged()
```
> 出处：`VNAssetLibraryEvents.RaiseChanged`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs:25](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L25)）

`CLAUDE.md` 记了配套规则：「只在 `ApplyModifiedProperties()` 返回 true 时发
（OnGUI 每帧都 Apply，无条件广播 = 每帧重建全部候选）」。

`OnFocus` 里刷新数据源与检查外部改动：
```csharp
void OnFocus()
{
    RefreshSources();
    CheckExternalChange();
}
```
> 出处：`VNScenarioEditorWindow.OnFocus`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:292](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L292)）

> **`OnFocus` 是编辑器窗口的「回来了」钩子**，
> 适合放「可能在我不在的时候被改了」的检查（外部文件改动、资产列表变化）。
> 比在 `OnGUI` 里每帧检查便宜得多。

### 19.3 剧本编辑器的 Schema 驱动参数格

**event 行按模块 id 长出不同参数格**：
```csharp
/// <summary>
/// 这一行是不是要多画一行「模块专属参数」——event 且模块 id 认得出来。
/// 塞进同一行的话，photo 有 11 个参数，每格只剩几十像素，什么都看不清。
/// </summary>
static bool EventParamLine(VNRow r) =>
    r.kind == VNRowKind.Command && r.keyword == "event" &&
    VNScenarioSchema.HasEventVariant(r.Get("module"));
```
> 出处：`VNScenarioEditorWindow.EventParamLine`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1606](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1606)）

`CLAUDE.md` 记了这套机制的两条硬规则：
1. **表的唯一真相是各模块 `OnLaunch` 里的 `ctx.Kw(...)`**——加模块参数时同步补一行。
2. **位置参数的存储键是 `module` 不是 `id`**——
   badminton/quiz/shop/plan/interact 自己就有 `id:` 参数，同名会互相覆盖。

> **这是「配置表与代码分离」必然带来的同步负担**。
> 本专案的缓解手段是：认不出的模块 id 退回基础定义、kwarg 原样保留——
> 也就是**没登记也不会丢数据，只是编辑体验差一点**。
> 这个降级设计让「忘了补表」的后果从「数据损坏」降到「不方便」。

### 19.4 打字搜索：VNCommandSearch

**两个复用件**：[`VNSearchItem`](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs)（一条候选，
`[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:11](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L11)`）
与 `VNSearchListView`（搜索框 + 列表，`:44`）。

**键盘处理必须抢在 TextField 之前**：
```csharp
/// 【硬约定】键盘处理必须排在 EditorGUI.TextField 之前——
/// IMGUI 里文本框会把 ↑↓ 拿去移光标、把 Enter 当成"结束编辑"吃掉，
/// 先 Use() 掉才轮得到我们。
```
> 出处：`VNSearchListView` 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:35](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L35)）

```csharp
// ---- 键盘：必须抢在 TextField 之前 ----
if (e.type == EventType.KeyDown)
{
    switch (e.keyCode)
    {
        case KeyCode.DownArrow: Move(1); e.Use(); break;
        case KeyCode.UpArrow: Move(-1); e.Use(); break;
        case KeyCode.PageDown: Move(6); e.Use(); break;
        case KeyCode.PageUp: Move(-6); e.Use(); break;
        case KeyCode.Return:
        case KeyCode.KeypadEnter:
            result.act = Act.Choose;
            ...
            e.Use();
            break;
        case KeyCode.Escape: result.act = Act.Cancel; e.Use(); break;
        case KeyCode.Tab: result.act = Act.Skip; e.Use(); break;
    }
}
```
> 出处：`VNSearchListView.Draw`（[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:113](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L113)）

**清空搜索框必须放掉键盘焦点**：
```csharp
/// <summary>
/// 换阶段时清空搜索框（命令面板逐步问参数时每步都要清）。
///
/// 【必须放掉键盘焦点】IMGUI 的文本框只要还持有 keyboardControl，就用它内部
/// TextEditor 的缓冲，程序里把 query 改成 "" 不生效——下一帧那个控件会把旧文本
/// 原样 return 回来，等于又把 query 写回去。先 keyboardControl = 0 让它重新
/// 从源字符串同步，再靠 _focusPending 把焦点抢回来。
/// </summary>
public void Reset()
{
    query = "";
    _index = 0;
    _scroll = Vector2.zero;
    _focusPending = true;
    GUIUtility.keyboardControl = 0;
    EditorGUIUtility.editingTextField = false;
}
```
> 出处：`VNSearchListView.Reset`（[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:83](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L83)）

> **这是 IMGUI 最反直觉的一个坑**：
> `EditorGUI.TextField(rect, myString)` 看起来是「传入值、返回新值」的纯函数，
> 但当这个控件持有键盘焦点时，它内部的 `TextEditor` 才是真相来源，
> 你传进去的值会被忽略。
> **要在代码里改一个正在编辑的文本框，必须先 `GUIUtility.keyboardControl = 0`。**

**不用 Unity 原生 AdvancedDropdown 的理由**：
```csharp
/// 【为什么不用 Unity 原生 AdvancedDropdown】它只匹配 item 名字、候选行只有一行文字，
/// 放不下命令的 hint 副标题；而 Ctrl+E 命令面板无论如何要自写带输入框的窗口，
/// 与其两套控件两套键位，不如共用这一套。
/// 【匹配规则】刻意只做子串包含，不做模糊/拼音——够用，且行为可预期。
```
> 出处：`VNSearchListView` 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:39](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L39)）

> **「刻意只做子串包含」值得强调**。
> 模糊匹配（fuzzy）看起来更聪明，但结果不可预期——
> 用户打三个字母出来五个不相关的东西，而他想要的那个排在第七。
> 子串匹配的心智模型是「我打的字必须连续出现」，用户一次就能学会。

### 19.5 素材浏览器 VNAssetBrowserWindow

**存在理由**：
```csharp
/// 【为什么要有这个窗口】
/// 本项目的素材文件名是 AI 生成时的原始 prompt 或纯数字
/// （"masterpiece, very aesthetic, highly detailed, 1girl... s-1095962266.png"、"1.png"），
/// **完全不表意** —— 光看名字不可能认出哪张是哪张。
/// 所以这里以**大缩略图为主、id 为标签**，文件名退居次要信息。
/// 音频同理：波形 + 一键试听，不靠名字猜。
```
> 出处：[`VNAssetBrowserWindow`](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs:11](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs#L11)）

**与 Inspector 的分工**：
```csharp
/// 【与 Inspector 的分工】
/// Inspector（VNGameConfigEditor）负责"改配置"，一次看一行；
/// 本窗口负责"找素材"，一次看几十个。两边共用 VNAssetUi 的绘制与预览。
```
> 出处：同上（`[Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs:18](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs#L18)`）

**九类素材声明成一张表**：
```csharp
static readonly Cat[] Cats =
{
    new Cat("背景",      "backgrounds",   Kind.Image),
    new Cat("CG",        "cgLibrary",     Kind.Image),
    new Cat("BGM",       "bgmLibrary",    Kind.Audio),
    new Cat("SE",        "seLibrary",     Kind.Audio),
    new Cat("语音",      "voiceLibrary",  Kind.Audio),
    new Cat("角色",      "characters",    Kind.Object),
    new Cat("对话框皮肤", "dialogueSkins", Kind.Object),
    new Cat("选项皮肤",   "choiceSkins",   Kind.Object),
    new Cat("天气",      "weatherDefs",   Kind.Object),
};
```
> 出处：`VNAssetBrowserWindow.Cats`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs:51](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs#L51)）

字段名是**字符串**，通过 `SerializedObject.FindProperty` 取——
这样加一类素材只要加一行。

**虚拟化：只画可见行**
```csharp
int cols = Mathf.Max(1, Mathf.FloorToInt((r.width - Gap - 14f) / stepX));
int rows = Mathf.CeilToInt(_visible.Count / (float)cols);
float contentH = rows * stepY + Gap + 40f;        // 末尾留出拖入提示的空间

var content = new Rect(0f, 0f, r.width - 16f, Mathf.Max(contentH, r.height));
_scrollGrid = GUI.BeginScrollView(r, _scrollGrid, content);

// 虚拟化：只画滚动窗口内的那几行
int firstRow = Mathf.Max(0, Mathf.FloorToInt((_scrollGrid.y - Gap) / stepY));
int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((_scrollGrid.y + r.height) / stepY));

for (int row = firstRow; row <= lastRow; row++)
    for (int c = 0; c < cols; c++) { ... DrawCell(...); }

GUI.EndScrollView();
```
> 出处：`VNAssetBrowserWindow` 网格绘制段（[Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs:397](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs#L397)）

> **IMGUI 虚拟化极其简单**（因为是立即模式——不画就是没有）：
> ① 算出总内容高度撑开 ScrollView；
> ② 用 `_scroll.y` 与视口高度算出可见行区间；
> ③ 只循环那几行。
> 三行代码。对比 uGUI 要做对象池 + 回收 + 位置重排，完全不是一个量级。
> **这是 IMGUI 相对 uGUI 的少数明确优势之一。**

**数据安全**：
```csharp
/// 【数据安全】
/// 全程走 VNGameConfig 的 SerializedObject，改动自动进 Undo、自动标脏，
/// 不直接写字段、不移动/重命名任何素材文件。
```
> 出处：`VNAssetBrowserWindow` 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs:22](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs#L22)）

> **编辑器工具改资产必须走 `SerializedObject`**，
> 直接写 `config.backgrounds.Add(...)` 会：不进 Undo、不标脏（可能不保存）、
> 不触发 Inspector 刷新。这是 Unity 编辑器编程的第一课。

### 19.6 素材 UI 共用层 VNAssetUi

**Sprite 缩略图不用 AssetPreview**：
```csharp
/// 【Sprite 缩略图不用 AssetPreview 的理由】
/// AssetPreview.GetAssetPreview 是**异步**的 —— 首帧返回 null，要靠反复 Repaint 才等得到，
/// 列表里几十张图一起等会闪一片空白。Sprite 本身就知道自己在哪张 texture 的哪个矩形，
/// 直接 GUI.DrawTextureWithTexCoords 画那块 UV 即可，**同步、精确、不需要 texture 可读**。
/// AudioClip 没有这种捷径（波形只能靠 AssetPreview），所以音频那边仍走异步 + 占位兜底。
```
> 出处：[`VNAssetUi`](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:17](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L17)）

实现：
```csharp
Rect tr;
try { tr = sprite.textureRect; }
catch { tr = sprite.rect; }          // 打包进图集且 tight 模式时可能取不到

var tex = sprite.texture;
if (tex.width <= 0 || tex.height <= 0 || tr.width <= 0f || tr.height <= 0f)
{
    DrawEmptySlot(rect, "d_Sprite Icon");
    return;
}

var uv = new Rect(tr.x / tex.width, tr.y / tex.height,
                  tr.width / tex.width, tr.height / tex.height);

GUI.DrawTextureWithTexCoords(FitAspect(rect, tr.width / tr.height), tex, uv, true);
```
> 出处：`VNAssetUi.DrawSpriteThumb`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:74](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L74)）

`try/catch` 是因为「打包进图集且 tight 模式时」`textureRect` 会抛异常
（注释在 `:76`）。

**音频试听走反射**（`UnityEditor.AudioUtil` 是 internal）：
```csharp
_audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
...
// Unity 各版本改过名：2020+ 是 PlayPreviewClip，更早叫 PlayClip。
_play = _audioUtil.GetMethod("PlayPreviewClip", F, null, ...);
```
> 出处：`VNAssetUi.ProbeAudioUtil`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:166](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L166)）

探不到就灰掉按钮而不是报错：
```csharp
/// <summary>本编辑器版本能否试听（探测不到 AudioUtil 时按钮变灰而不是报错）。</summary>
public static bool CanPreviewAudio { get { ProbeAudioUtil(); return _play != null; } }
```
> 出处：`VNAssetUi.CanPreviewAudio`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:193](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L193)）

**自己给 AssetPreview 一个 3 秒超时**：
```csharp
// Unity 6.5 起 IsLoadingAssetPreview(int) / GetInstanceID() 都是 error 级弃用，
// 所以这里不问 Unity「还在加载吗」，改成自己给每个资产一个 3 秒的等待窗口，到点放弃。
static readonly Dictionary<UnityEngine.Object, double> _previewDeadline =
    ...
```
> 出处：`VNAssetUi._previewDeadline`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:302](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L302)）

> **两条通用经验**：
> ① **反射调 internal API 时一定要做「探不到就降级」**，不要假设它一定在。
>    Unity 版本升级会改名、会删。
> ② **异步资源不能无限等**——有些资产永远没有预览图（比如某些 SO）。
>    自己维护一个 deadline 比依赖 Unity 的「还在加载吗」API 更稳
>    （尤其那个 API 已被弃用）。

### 19.7 分页 Inspector：VNGameConfigEditor

**页签只登记字段名，绘制仍走 PropertyField**：
```csharp
/// 【为什么不硬编码画每个字段】
/// 页签只登记字段名，实际绘制仍走 EditorGUILayout.PropertyField ——
/// 这样条目长什么样完全由 PropertyDrawer 决定（见 VNConfigEntryDrawers），
/// 而且**以后往 VNGameConfig 加字段不会静默消失**：
/// 没被任何页签认领的字段会自动落到「其他」页并给出提示，不会看不见。
```
> 出处：[`VNGameConfigEditor`](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs:15](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs#L15)）

孤儿字段收集：
```csharp
/// <summary>扫一遍序列化字段，找出页签没登记的那些。</summary>
void CollectOrphans()
{
    _orphans.Clear();
    var claimed = new HashSet<string>();
    foreach (var t in Tabs)
        foreach (var f in t.fields) claimed.Add(f);

    var it = serializedObject.GetIterator();
    bool enter = true;
    while (it.NextVisible(enter))
    {
        enter = false;                                   // 只看顶层
        if (it.name == "m_Script") continue;
        if (!claimed.Contains(it.name)) _orphans.Add(it.name);
    }
}
```
> 出处：`VNGameConfigEditor.CollectOrphans`（[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs:70](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs#L70)）

`NextVisible(enter)` 的 `enter` 第一次传 true、之后传 false = **只遍历顶层字段**。
这是 `SerializedProperty` 遍历的标准写法。

> **「自动兜底未登记字段」是自定义 Inspector 的必备设计**。
> 没有它，某天有人加了个字段忘了登记页签，这个字段就在 Inspector 里彻底消失了，
> 而且没有任何提示——极难发现。

### 19.8 紧凑 Drawer：VNConfigEntryDrawers

**它解决的问题很具体**：
```csharp
/// 【为什么需要】
/// 这几个条目类把说明文字写成了字段上的 [Header]（见 VNStage.BackgroundEntry 等）。
/// Header 是 DecoratorDrawer，Unity 默认 Inspector 会**给列表里的每一项都重画一遍**，
/// 于是一个 CG 条目要占 6~7 行 —— 7 张 CG 就 50 行，这才是"要滚很久"的真正原因。
/// 类型上一旦挂了 CustomPropertyDrawer，Unity 就不再递归画子字段，那些 Header 自然消失；
/// 说明文字改挂 tooltip，不占版面但鼠标悬停仍看得到。
///
/// 所以这里**一行代码都不用改运行时**，纯靠接管绘制解决。
/// 又因为 drawer 是挂在类型上的，VNStage / VNAudio 组件 Inspector 上的同名列表
/// 也一并变紧凑，不只 VNGameConfig 受益。
```
> 出处：[`VNEntryDrawerBase`](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs:8](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs#L8)）

而运行时侧也有对应的约定：
```csharp
// ★ 列表元素里的字段说明一律用 [Tooltip]，**绝对不要用 [Header]**——
//   它会把控件区域往下推，在自定义 drawer 的固定 rect 里表现为
//   文字叠印 + 输入框点不进去。详见 VNStage.BackgroundEntry 上的注释。
```
> 出处：[`VNGameConfig.UiSkinEntry`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) 上方注释（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:112](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L112)）

**缩进必须归零**：
```csharp
int indentWas = EditorGUI.indentLevel;
EditorGUI.indentLevel = 0;      // rect 已经手算好了，缩进会让控件错位
DrawRow(position, property);
EditorGUI.indentLevel = indentWas;
```
> 出处：`VNEntryDrawerBase.OnGUI`（[Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs:50](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs#L50)）

> **写 PropertyDrawer 的三条铁律**：
> ① 用 `EditorGUI.BeginProperty` / `EndProperty` 包起来（前缀标签、Undo、多选支持）；
> ② 手算 Rect 时把 `indentLevel` 归零；
> ③ `GetPropertyHeight` 返回的高度必须与实际绘制一致，否则会重叠。
> 本专案三条都做到了（`VNEntryDrawerBase.GetPropertyHeight`（[`Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs:30`](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs#L30)），
> `[Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs:30](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs#L30)`）。

### 19.9 编辑器主题：VNAssetTheme

**能改到什么程度**：
```csharp
/// 【能改到什么程度】
/// Unity 编辑器**整体**只有 Light / Dark 两套官方主题，没法自定义
/// （2019.3+ 编辑器 UI 虽然迁到了 UI Toolkit/USS，但样式表打包在编辑器资源里不开放覆盖）。
/// 但**自己 IMGUI 画的窗口**每一像素都归自己管 —— 这个类就是把
/// VNAssetBrowserWindow 的所有颜色、圆角、字体样式收成一处。
```
> 出处：[`VNAssetTheme`](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs:9](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs#L9)）

**必须覆盖八个状态的文字色**：
```csharp
/// 【为什么必须显式设文字颜色】
/// Unity Dark 主题下 EditorStyles 的文字是浅色的。换成粉白底之后
/// 直接用 EditorStyles.label 会得到"白底白字" —— 字直接消失。
/// 所以主题启用时每个 GUIStyle 都要覆盖 normal/hover/active/focused 四个状态的 textColor。
```
> 出处：同上（`[Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs:15](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs#L15)`）

```csharp
static GUIStyle Tint(GUIStyle src, Color c)
{
    var s = new GUIStyle(src);
    s.normal.textColor = c;
    s.hover.textColor = c;
    s.active.textColor = c;
    s.focused.textColor = c;
    s.onNormal.textColor = c;
    s.onHover.textColor = c;
    s.onActive.textColor = c;
    s.onFocused.textColor = c;
    return s;
}
```
> 出处：`VNAssetTheme.Tint`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs:252](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs#L252)）

注释说四个状态，实际代码覆盖了**八个**（四个普通 + 四个 on*）——
代码比注释更全，这是对的（`on*` 是 toggle 按下态）。

**圆角靠程序化贴图 + GUI.color 染色**：
```csharp
/// 【圆角怎么来】
/// 程序化生成一张**白色**圆角贴图，用 GUI.color 染成任意颜色后 9-slice 拉伸 ——
/// 一张贴图搞定所有尺寸和配色，零美术依赖（与项目里 VNProceduralTextures 一个路子）。
/// 贴图是 static 的，域重载后会丢，所以全部走 lazy 重建 + HideFlags.DontSave。
```
> 出处：同上（`[Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs:20](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs#L20)`）

**主题是叠加不是替换**：
```csharp
/// <summary>是否启用了自定义外观（Default 时全部绘制退回 Unity 原生）</summary>
public static bool Enabled { get { return Current != Kind.Default; } }
```
> 出处：`VNAssetTheme.Enabled`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs:56](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs#L56)）

> **「叠加不是替换」让主题功能可以随时关掉**，
> 出问题时一键回到 Unity 原生外观。这比「主题化之后回不去了」安全得多。

### 19.10 专用调参窗口：VNFogTuneWindow

**它示范了「什么时候该写一个专门的调参窗口」**：
```csharp
/// 【为什么这个窗口是必需品而不是加分项】
/// 本玩法刻意不做速度惩罚（擦到就掉），难度的**唯一**来源就是
/// 「笔刷面积 vs 回雾速度」这两个数——手感全压在参数上，而参数不可能一次调对。
/// 每改一个数就进一次 Play Mode 试玩，一轮要一分钟；在这里改，是即时的。
///
/// 两块内容：
///   ① 上方**算出来的**预计通关秒数（不是试出来的）。公式见 EstimateSeconds：
///      每秒擦除面积 ≈ 笔刷直径 × 鼠标速度，除以重叠浪费，再减回雾速率。
///   ② 下方可交互的掩码预览：在画布上拖鼠标就是擦，回雾照参数实时跑。
///      用的是运行时同一个 VNFogMask —— 这里看到的行为和游戏里一模一样，
///      不是另写一套近似（同 VNShakeSpec / VNCamera 公式共用的习惯）。
```
> 出处：[`VNFogTuneWindow`](Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs:9](Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs#L9)）

估算参数：
```csharp
/// <summary>估算用的典型拖动速度（屏幕像素/秒）。普通玩家认真擦大约就是这个量级。</summary>
const float AssumedDragSpeed = 800f;

/// <summary>重叠浪费系数：玩家不可能画出完美不重叠的覆盖路径</summary>
const float OverlapFactor = 1.5f;
```
> 出处：`VNFogTuneWindow.AssumedDragSpeed`（[Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs:26](Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs#L26)）

**关键是「用运行时同一份代码」**：
```csharp
readonly VNFogMask _mask = new VNFogMask();
```
> 出处：`VNFogTuneWindow._mask`（[Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs:37](Assets/Project/Scripts/VNEffects/Editor/VNFogTuneWindow.cs#L37)）

> **判断「要不要写调参窗口」的标准**：
> - 参数直接决定手感 + 参数之间有非线性交互 + 无法一次调对 → **要写**
> - 参数只影响外观、改了立刻能看到 → 不用写，Inspector 就够
> - 参数在 Play Mode 里能实时改并看到效果 → 用 `#if UNITY_EDITOR` 的实时重读
>   （羽毛球的做法，[`VNBadmintonModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs)，
>   `[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:423](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L423)`）
>
> **而「预览用运行时同一份代码」是不可妥协的**——
> 另写一套近似的预览，调出来的参数进游戏就不对，那这个窗口就是负资产。
> 本专案在三处贯彻了这条：擦雾掩码、震动等级表（[`VNShakeSpec`](Assets/Project/Scripts/VNEffects/VNScreenShake.cs)）、
> 镜头缩放公式（`VNCamera.CharacterScaleFor`（[`Assets/Project/Scripts/VNEffects/VNCamera.cs:49`](Assets/Project/Scripts/VNEffects/VNCamera.cs#L49)） / `ContainerZoomFor`）。

### 19.11 部位区域编辑器 VNTouchZoneEditorWindow

```csharp
/// 部位区域编辑器：在立绘上直接拖框画出可互动部位。
///
/// 没有这个工具的话，VNTouchZoneDef 里那些归一化坐标只能靠猜着填、
/// 进游戏看、再回来改，一个部位要试五六轮。
///
/// 坐标：画布与运行时共用 VNTouchZoneDef.Contains 的同一套语义
/// （归一化，(0,0) = 立绘中心，x/y 各 -0.5~+0.5），所以这里画的框
/// 就是游戏里摸到的地方。
///
/// 撤销是**窗口内独立栈**（快照 = 整份 zones 的 JSON），不挂 Unity 全局 Undo ——
/// 与 camseq 编排窗口一致：全局 Undo 会把画框和场景里其它操作混在一条时间线上。
```
> 出处：[`VNTouchZoneEditorWindow`](Assets/Project/Scripts/VNEffects/Editor/VNTouchZoneEditorWindow.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNTouchZoneEditorWindow.cs:8](Assets/Project/Scripts/VNEffects/Editor/VNTouchZoneEditorWindow.cs#L8)）

**窗口内独立撤销栈**：
```csharp
readonly List<string> _undo = new List<string>();
readonly List<string> _redo = new List<string>();
```
> 出处：`VNTouchZoneEditorWindow._undo`（[Assets/Project/Scripts/VNEffects/Editor/VNTouchZoneEditorWindow.cs:42](Assets/Project/Scripts/VNEffects/Editor/VNTouchZoneEditorWindow.cs#L42)）

快照是 JSON 序列化的整份 zones：
```csharp
[System.Serializable]
class ZoneList { public List<VNTouchZone> zones = new List<VNTouchZone>(); }
```
> 出处：`VNTouchZoneEditorWindow.ZoneList`（[Assets/Project/Scripts/VNEffects/Editor/VNTouchZoneEditorWindow.cs:53](Assets/Project/Scripts/VNEffects/Editor/VNTouchZoneEditorWindow.cs#L53)）

> **「窗口内独立撤销栈」vs「Unity 全局 Undo」的取舍**：
> - **全局 Undo** 的好处是和 Unity 其他操作在同一条时间线，Ctrl+Z 行为一致。
>   坏处是「我在窗口里画了三个框，然后去场景里移动了一个物体，
>   再按 Ctrl+Z 撤销的是场景操作还是画框？」——用户预期不明确。
> - **独立栈** 的好处是「在这个窗口里 Ctrl+Z 一定撤销这个窗口的操作」。
>   坏处是要自己实现，而且切换绑定对象时要清空。
>
> 本专案在三个窗口用了独立栈（剧本编辑器、镜头编排、部位区域），
> 而且**用「整份数据的文本快照」当撤销单位**——最简单、绝对不会漏状态。
> 代价是内存（剧本几百行 × 几十步撤销 = 几 MB），完全可以接受。

**剧本编辑器的撤销也是文本快照**：
```csharp
// 撤销（文本快照，约 1 秒粒度合并）
readonly List<string> _undoStack = new List<string>();
readonly List<string> _redoStack = new List<string>();
string _frameSnapshot = "";
int _frameSnapshotVersion = -1;
double _lastUndoPush;
```
> 出处：`VNScenarioEditorWindow._undoStack`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:120](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L120)）

「约 1 秒粒度合并」（`_lastUndoPush`）——
连续打字不会产生几十个撤销步。这是文本编辑器的标准做法。

### 19.12 镜头编排 VNCamseqEditorWindow

154KB，是第二大的编辑器档案。核心数据结构：
```csharp
enum PointType { Anchor, Character, Coords, Stay }

[System.Serializable]
class Waypoint
{
    public PointType type = PointType.Coords;
    public int anchorIndex = 4;   // middle
    public string charId = "";
    public int partIndex = 0;
    public int slotIndex = 1;     // 编辑态假定站位（center）
    public Vector2 coords;
    public float zoom = 1.4f;
    public float duration = 0.8f;
    public int easeIndex = 0;     // 0 = (默认)
    public float fade;            // >0 = 交叉叠化到本点（xfade:秒），代替平移/瞬切
    public float hold;            // >0 = 到达本点后停留的秒数（hold:秒）
    public string shake = "";     // 到达本点时震一下（shake:等级 或 shake:强度,秒数）
}
```
> 出处：[`VNCamseqEditorWindow.Waypoint`](Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs:27](Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs#L27)）

**下拉的说明文字写成 GUIContent 的 tooltip**：
```csharp
static readonly GUIContent ZoomModeLabel = new GUIContent("缩放模式",
    "谁跟着 zoom 缩放（camseq 的 mode:）：\n" +
    "both  背景+立绘同步等比放大 —— 推拉镜 TU/TB，最常用，默认\n" +
    "depth 立绘比背景多缩放（系数 0.5）—— 靠速度差造纵深，等比缩放其实是「数码变焦」\n" +
    ...);
```
> 出处：`VNCamseqEditorWindow.ZoomModeLabel`（[Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs:57](Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs#L57)）

**枚举下标必须与运行时枚举一致**：
```csharp
// 顺序必须与 VNCamZoomMode 一致（下拉直接用枚举下标）
static readonly string[] ZoomModeNames =
{
    "both 背景+立绘一起", "depth 立绘多缩一点", "bg 只缩背景", "char 只缩立绘",
};
```
> 出处：`VNCamseqEditorWindow.ZoomModeNames`（[Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs:52](Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs#L52)）

> **「显示名数组 + 枚举下标」是 IMGUI 下拉最常见的写法，也是最脆的**——
> 枚举加一项在中间，所有下拉全错位，而且没有编译错误。
> 缓解手段：① 注释写明（本专案的做法）；
> ② 更稳的是用 `System.Enum.GetNames(typeof(VNCamZoomMode))` 生成，
>    代价是显示名就没法带中文说明了。
> 本专案选了 ①，因为中文说明对剧本作者更重要。**这个取舍是对的，但要记得那条注释。**

### 19.13 单行输入弹窗 VNTextPromptWindow

Unity 没有内置的「带输入框的 DisplayDialog」，所以自己写了一个：
```csharp
/// 极简单行输入弹窗（Unity 没有内置的「带输入框的 DisplayDialog」）。
/// 用 ShowModalUtility 阻塞到用户确认/取消，返回值 = 输入内容，取消返回 null。
public class VNTextPromptWindow : EditorWindow
{
    public static string Prompt(string title, string label, string defaultValue)
    {
        var window = CreateInstance<VNTextPromptWindow>();
        window.titleContent = new GUIContent(title);
        window._label = label;
        window._value = defaultValue ?? "";
        window.position = new Rect(
            Screen.currentResolution.width * 0.5f - 170f,
            Screen.currentResolution.height * 0.5f - 40f, 340f, 84f);
        window.ShowModalUtility();
        return string.IsNullOrEmpty(window._result) ? null : window._result;
    }
```
> 出处：[`VNTextPromptWindow.Prompt`](Assets/Project/Scripts/VNEffects/Editor/VNTextPromptWindow.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNTextPromptWindow.cs:20](Assets/Project/Scripts/VNEffects/Editor/VNTextPromptWindow.cs#L20)）

> **`ShowModalUtility()` 是阻塞的**——这一行之后的代码要等窗口关闭才执行。
> 这让 `Prompt` 能写成一个同步函数，调用方一行搞定。
> 注意用 `CreateInstance` 而不是 `GetWindow`（后者会复用已有窗口）。

### 19.14 增量装机器：Installer 模式

八个 Installer（[`VNQuizInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs)、[`VNLiquidInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNLiquidInstaller.cs)、[`VNPhotoBoothInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNPhotoBoothInstaller.cs)、
[`VNInteractionInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNInteractionInstaller.cs)、[`VNBadmintonInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNBadmintonInstaller.cs)、[`VNFogWipeInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNFogWipeInstaller.cs)、
[`VNQuestBoardInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs)、[`VNSecretPhotoInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNSecretPhotoInstaller.cs)、[`VNAiTalkInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNAiTalkInstaller.cs)）遵循同一套模式：

```csharp
/// 把委托板模块**增量装进当前场景**，不重建场景（同 VNQuizInstaller 的做法）。
/// 做三件事：
///   ① 在场景的 VNEventRegistry 下补一个**禁用的** QuestBoardTemplate（带 RectTransform）
///   ② 确保场景里有 VNQuestLog（任务面板 + 引擎驱动都靠它）
///   ③ 把工程里全部 VNQuestDef 登记进 VNGameConfig 的任务库
/// 重复执行安全：已经装过就只刷新任务列表。
```
> 出处：`VNQuestBoardInstaller` 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs:9](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs#L9)）

**找不到前提就弹对话框指路**：
```csharp
var registry = Object.FindFirstObjectByType<VNEventRegistry>(
    FindObjectsInactive.Include);
if (registry == null)
{
    EditorUtility.DisplayDialog("VN Quest Board",
        "当前场景里找不到 VNEventRegistry。\n\n" +
        "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个），" +
        "或用 Tools → VN Effects → 演示场景 Demo Scenes → " +
        "重建剧本演示场景 Create Script Demo Scene 造一个新场景。", "OK");
    return;
}
```
> 出处：`VNQuestBoardInstaller.Install`（[Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs:25](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs#L25)`）

注意 `FindObjectsInactive.Include`——注册表可能挂在禁用物体上。

**Undo 支持**：
```csharp
Undo.RecordObject(registry, "Install quest board module");
var go = new GameObject(TemplateName, typeof(RectTransform));
go.transform.SetParent(registry.transform, false);
module = go.AddComponent<VNQuestBoardModule>();
go.SetActive(false); // 模板保持禁用，运行时 Instantiate 后才激活
Undo.RegisterCreatedObjectUndo(go, "Install quest board module");
```
> 出处：`VNQuestBoardInstaller.Install`（[Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs:56](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs#L56)）

> **两个 Undo API 的分工**：
> `Undo.RecordObject(obj, name)` = 「我要改这个对象，先记下它当前的样子」（改之前调）；
> `Undo.RegisterCreatedObjectUndo(go, name)` = 「我新建了这个物体，撤销时删掉它」（建之后调）。
> 两者配套用才能让一次装机变成一步可撤销的操作。

**「增量装机」vs「重建场景」**：
`VNEffectsDemoSetup.CreateScriptDemoScene`（[`Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:451`](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L451)）
（`[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:451](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L451)`）是**重建**——
`NewScene(EmptyScene)` 从零造。
Installer 是**增量**——只往现有场景补东西。

> **专案演进的痕迹很清楚**：早期只有「重建场景」，
> 但一旦场景里有了手工调整（美术摆的位置、特殊配置），重建就是灾难。
> 于是新功能全部改成增量装机。
> **这是任何「生成器驱动」项目的必经之路**：
> 生成器适合从零开始，一旦有人手改过就必须提供增量路径。
>
> 而 `VNGameConfig` 的存在正是为了让「重建场景」不丢数据——
> 两条路互补：配置进资产（重建不丢）、结构增量装（不用重建）。

---

## 二十、全专案 UI 踩坑清单

这一章把散落在各处注释里的血泪汇总成一张可查的表。**每一条都是真的踩过的**。

### 20.1 uGUI / Canvas 层

| # | 症状 | 原因 | 修法 | 出处 |
|---|---|---|---|---|
| 1 | 「对话框藏了，一排圆按钮还浮在半空」 | `overrideSorting` 的子 Canvas 打断父 CanvasGroup 的 alpha 传播 | 子 Canvas 自己挂 CanvasGroup，由父级显式通知 | [`VNQuickToolbar.SetVisible`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:134](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L134)） |
| 2 | 嵌套 Canvas 上的按钮点不到 | `overrideSorting` 的 Canvas 需要自己的 GraphicRaycaster | `AddComponent<GraphicRaycaster>()` | [`VNChoicePanel.Build`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs)（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:91](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L91)）、[`VNEventRegistry.EnsureLayer`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs)（[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:87](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L87)） |
| 3 | 「任务标题左边缺字」 | 拉伸锚点下 `sizeDelta` 默认 (100,100) = 比父级宽 100px，被 RectMask2D 裁掉 | 显式 `sizeDelta = Vector2.zero` | [`VNQuestLog.Build`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:307](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L307)） |
| 4 | 描边环被开窗裁掉 | `Mask` 会裁掉所有子物体 | 描边环放在 Mask **外面** | [`VNPhotoBoothModule.BuildViewFinder`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:558](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L558)） |
| 5 | 属性飘字被 HUD 皮肤裁掉一半 | 挂在带 Mask 的条目下 | 挂 Canvas 根 + 坐标换算 | [`VNStatsHud.SpawnFloatingDelta`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:381](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L381)） |
| 6 | 「人后贴纸永远点不到」 | 拖动板 `raycastTarget=true` 压在贴纸之上吃掉射线 | 拖动板压到最底层 | `VNPhotoBoothModule.BuildViewFinder`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:535](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L535)） |
| 7 | 「点了个买不起的选项，剧情居然往下走了」 | 置灰选项不吃射线，点击穿透到背景 | 置灰项仍 `raycastTarget=true`，只禁 Button | `VNChoicePanel.CreateDefaultButton`（[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:330](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L330)） |
| 8 | 「点 Slider 轨道没反应」 | Slider 根上没有 Graphic 就收不到轨道点击 | 补一个 `Color.clear` 的 Image | [`VNConfigPanel.BindSlider`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:182](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L182)） |
| 9 | 「一切状态都对但就是不画」 | 自定义 `Graphic` 子类没有 CanvasRenderer；`RequireComponent` 不走继承链 | 子类上再写一遍 `[RequireComponent]`，并在创建时显式 `AddComponent<CanvasRenderer>()` | [`VNBadmintonQuad`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs:12](Assets/Project/Scripts/VNEffects/Script/VNBadmintonUi.cs#L12)） |
| 10 | 全屏点击被拦掉 | `IsPointerOverGameObject()` 对任何 raycastTarget 都为 true，而本项目全屏皆 UI | `RaycastAll` + 按 `Selectable` 过滤 | [`VNScriptRunner.IsPointerOverInteractiveUi`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2202](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2202)） |
| 11 | 面板被 Overlay 画布压住、转场盖不住 | Screen Space - Overlay 永远压在 Screen Space - Camera 之上 | 需要被转场盖住 / 吃 Bloom 的层挂主 Canvas | [`VNTutorialPlayer`](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:25](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L25)）、[`VNInterludeScreen`](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs) 类注释（[Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs:25](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L25)） |
| 12 | 滚动列表打开时位置不对 | `SetActive(true)` 后 content 尺寸还没算 | `Canvas.ForceUpdateCanvases()` 再设 `verticalNormalizedPosition` | [`VNBacklog.Open`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs:68](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs#L68)）、`VNQuestLog.Open`（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:230](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L230)） |
| 13 | 确认框被别的东西盖住 | 同 Canvas 内绘制顺序 = Hierarchy 顺序 | `transform.SetAsLastSibling()` | [`VNSaveLoadPanel.ShowConfirm`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:261](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L261)） |
| 14 | 空 RawImage 显示成纯白且忽略 color | `RawImage.texture == null` 时的默认行为 | 用 `Texture2D.whiteTexture` 当底再染色 | `VNSaveLoadPanel.CreateCustomSlotCard`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:216](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L216)） |

### 20.2 TextMeshPro 层

| # | 症状 | 原因 | 修法 | 出处 |
|---|---|---|---|---|
| 15 | 改一个名牌样式，全游戏文字一起变 | 改的是 `fontSharedMaterial`（字体资产自带的） | 一律走 `text.fontMaterial`（自动 new 实例） | [`VNNameplateStyle.ApplyTo`](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs)（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:520](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L520)） |
| 16 | underlay 参数设了没反应 | 没有 `EnableKeyword("UNDERLAY_ON")` | 改 underlay 前先 EnableKeyword | `VNNameplateStyle.ApplyTo`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:539](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L539)） |
| 17 | 描边推到某个值就不再变粗 | padding 是描边厚度的天花板（`outlineWidth ×(padding+1)×(字号/采样点)`） | 装饰字体单开一套大 padding 资产 | [`VNFont.DisplayAtlasPadding`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs)（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:71](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L71)） |
| 18 | padding 调大反而更糊 | padding 占采样点比例过大会挤掉字形分辨率（64pt+padding24=37% 实测更差） | 采样点与 padding 等比例，保持 ~18% | `VNFont.DisplaySamplePointSize`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:62](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L62)） |
| 19 | 切语言后名牌变回光板白字 | 换 font 会让 TMP 丢掉材质实例 | 订阅 `VNFont.DisplayFontChanged`（[`Assets/Project/Scripts/VNEffects/Script/VNFont.cs:321`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L321)） 重新上样式 | [`VNDialogueBox.HookLocaleEvents`](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs)（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:466](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L466)） |
| 20 | 切语言把名牌 Heavy 字体也换成正文字体，粗描边垮掉 | 正文与装饰字体没分开管理 | 两个 HashSet 分别替换 | `VNFont.HandleLanguageChanged`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:279](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L279)） |
| 21 | Heavy 字重叠伪粗后描边糊了 | TMP 的 Bold 是 SDF 膨胀 | 用装饰字体时 `fontStyle = Normal` | `VNNameplateStyle.ApplyTo`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:486](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L486)） |
| 22 | HDR 发光写了没用 | uGUI 顶点色被钳到 1，Bloom 阈值也是 1.0 | 走材质 `_FaceColor`；代价是与顶点渐变二选一 | `VNNameplateStyle.ApplyTo`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:497](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L497)） |
| 23 | 金/银浮雕没效果也不报错 | Mobile 版 TMP shader 没有 Bevel 属性 | `HasProperty` 挡掉并**只警告一次** | `VNNameplateStyle.ApplyBevel`（[Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs:559](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs#L559)） |
| 24 | 刚创建的文字 `preferredWidth` 是 0 | 该属性要等一次布局 | 用 `GetPreferredValues(text)`（同步） | [`VNToast.BuildCard`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs)（[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:245](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L245)）、`VNDialogueBox.ResizeNameTag`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:430](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L430)） |
| 25 | 打字机顶点索引对不上 | 没先 `ForceMeshUpdate()` | 读 textInfo 前先 ForceMeshUpdate | [`VNTypewriterText.LateUpdate`](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs)（[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:75](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L75)） |
| 26 | 空行台词卡死剧本 | 打字机永远等不到「所有字显现」 | `visibleIndex == 0` 视作立即播完 | `VNTypewriterText.LateUpdate`（[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:107](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L107)） |
| 27 | 台词播完后仍每帧重建网格 | 没有停止 `_animating` | `if (!_playing && !anyPartial) _animating = false;` | `VNTypewriterText.LateUpdate`（[Assets/Project/Scripts/VNEffects/VNTypewriterText.cs:121](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs#L121)） |
| 28 | `lineSpacing` 设 1.25 结果行距几乎没变 | TMP 的 lineSpacing 单位是字号百分比 | 要 1.25 倍就写 25 | `VNDialogueBox.BuildDefaultSkin`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:189](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L189)） |

### 20.3 生命周期 / 状态层

| # | 症状 | 原因 | 修法 | 出处 |
|---|---|---|---|---|
| 29 | 进 Play 后对话框有两层，底下那层是死的 | 编辑期误触发 `Build()` 让生成物存进了场景 | 按固定名字 `Find` 捡回来复用 | `VNDialogueBox.BuildDefaultSkin`（[Assets/Project/Scripts/VNEffects/VNDialogueBox.cs:138](Assets/Project/Scripts/VNEffects/VNDialogueBox.cs#L138)） |
| 30 | 语言切换后面板再也建不出来 | 销毁 Canvas 但没清缓存字段，Unity 伪 null 让 `if (_panel != null) return;` 提早返回 | 所有缓存字段都要置 null | `VNSaveLoadPanel.OnLanguageChanged`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:44](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L44)） |
| 31 | 「游戏永久卡死」 | [`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) 句柄漏了 Release | 句柄绑宿主自动失效 + 七层保险 | `VNPause` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:15](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L15)） |
| 32 | 一进 Play 就是暂停状态 | 关闭域重载后静态字段不清 | `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 重置 | `VNPause.ResetStatics`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:113](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L113)） |
| 33 | 「玩家的鼠标指针永久消失」 | `Cursor.visible = false` 后漏了还原 | `Dispose` 在四处调（Finish/Cancel/OnDestroy/OnDisable） | [`VNTouchCursor.Dispose`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:213](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L213)） |
| 34 | 互动结束后部位框留在角色脸上 | 框挂在立绘下面，不在模块子树里 | 模块销毁时显式删 | [`VNInteractionModule.BuildZoneOverlay`](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs:970](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs#L970)） |
| 35 | 事件结束后剧本跳到莫名其妙的地方 | 模块用 `RunInlineCo` 播了控制流命令 | 黑名单挡掉 jump/choice/call/event… | `VNScriptRunner.InlineBlockedKeywords`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:304](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L304)） |
| 36 | 偷拍插话后玩家点击不推进 | 嵌套 SayCo 把外层 `_waitingAtSay` 清掉了 | 用 `SayOutOfScript` 绕过状态机 | `VNScriptRunner.SayOutOfScript`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1828](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1828)） |
| 37 | 教程结束那一下 ESC 被羽毛球当成认输 | `wasPressedThisFrame` 同帧对所有读取者为 true | 淡出跨两帧再解除暂停 | `VNTutorialPlayer.PlayCo`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:203](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L203)） |
| 38 | 教程冻结了但还能挥拍 | `VNPause` 检查放在 ReadInput 之后 | **必须在 ReadInput 之前 return** | [`VNBadmintonModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs:412](Assets/Project/Scripts/VNEffects/Script/VNBadmintonModule.cs#L412)） |
| 39 | `Time.timeScale = 0` 对小游戏无效 | 模块用 unscaled 计时（为了躲 Skip 加速） | `VNPause` + `VNTime.Delta`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:144`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L144)） | `VNPause` 类注释（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:9](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L9)） |
| 40 | 切窗口回来球瞬移过整个球场 | 巨大的 `unscaledDeltaTime` | `VNTime.MaxStep = 0.05f` 钳制 | `VNTime.MaxStep`（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:139](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L139)） |
| 41 | 模块结果偶尔跳错分支 | `Done` 被调用两次 | `_finished` 守卫 | [`VNEventModule.Done`](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:79](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L79)） |
| 42 | 事件结束后 hideHUD keep 的段落被弹回 UI | 瞬态隐藏写进了持久状态 | 瞬态用独立 bool，可见性 = 合成函数 | `VNScriptRunner.ApplyUiHidden`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1784](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1784)） |
| 43 | 读档后界面莫名其妙全没了 | 非锁定隐藏被存进了存档 | 只存 `hideHUD keep` 的锁定隐藏 | [`VNSaveData.uiHidden`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs)（[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:61](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L61)） |
| 44 | 读档时 UI 重建几百次 | 直接在 `VNFlags.Changed`（[`Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:21`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L21)） 里刷新 | 标脏 + 下帧一次 | [`VNFlags.Changed`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs)（[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:20](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L20)） |
| 45 | 改了 VNGameConfig 但「没反应」 | 下游还各自缓存了从它读出来的值 | `ClearCache` 连带清下游 | [`VNGameConfig.ClearCache`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:69](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L69)） |
| 46 | 静态委托退订误伤了后来注册的实例 | 直接置 null | `ReferenceEquals(delegate.Target, this)` 才置 null | [`VNInventory.OnDestroy`](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs)（[Assets/Project/Scripts/VNEffects/Script/VNInventory.cs:47](Assets/Project/Scripts/VNEffects/Script/VNInventory.cs#L47)） |
| 47 | 两个实例先后登记同一 id，先销毁的抹掉后来者 | 反注册不检查当前登记的是不是自己 | `Unregister(id, rect)` 带 rect 校验 | [`VNTutorialAnchors.Unregister`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:36](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L36)） |

### 20.4 资源层

| # | 症状 | 原因 | 修法 | 出处 |
|---|---|---|---|---|
| 48 | 相册网格里的图变白块 | LRU 容量 12 < 同屏 30 张，正在用的被驱逐 | 缩略图单独一份不驱逐的小图缓存 | [`VNPhotoAlbum.LoadThumbnail`](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs)（[Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs:271](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs#L271)） |
| 49 | 打开相册几分钟后内存爆掉 | `Texture2D` 不受 GC 管理 | 四个出口都 Destroy + `ClearCache` | `VNSaveLoadPanel.ClearLoadedThumbnails`（[Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs:283](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs#L283)） |
| 50 | 抓图裁到左下角四分之一 | 高 DPI 屏上抓图尺寸 ≠ `Screen.width` | 按比例换算 | [`VNPhotoCapture.Crop`](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs)（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:104](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L104)） |
| 51 | 抓到的还是旧画面 | 关掉 UI 之后没等 Canvas 重画 | `yield return null;` + `WaitForEndOfFrame` | `VNPhotoCapture.Capture`（[Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs:47](Assets/Project/Scripts/VNEffects/Script/VNPhotoCapture.cs#L47)） |
| 52 | 程序化贴图 `EncodeToPNG` 返回空 | `Apply(false, true)` 释放了 CPU 拷贝 | 经 RenderTexture + ReadPixels 取回可读副本 | [`VNUiSkinExporter.BakeSprite`](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs:64](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs#L64)） |
| 53 | 打包后 shader 找不到 | 只被 `Shader.Find` 引用的 shader 会被剥掉 | 提供一个 Material 资产字段 | `VNTutorialPlayer.maskMaterial`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs:52](Assets/Project/Scripts/VNEffects/Script/VNTutorialPlayer.cs#L52)） |
| 54 | 9-slice 拉伸不对 | `Sprite.Create` 没传 border，或 meshType 是 Tight | 传 `new Vector4(22,22,22,22)` + `SpriteMeshType.FullRect` | [`VNProceduralTextures.RoundedRectSprite`](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs)（[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:296](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L296)） |
| 55 | 生成的贴图被当成场景资源保存 | 没设 hideFlags | `hideFlags = HideFlags.DontSave` | `VNProceduralTextures.RoundedRectSprite`（[Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs:300](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs#L300)） |
| 56 | `JsonUtility` 存 NaN 读回来是垃圾 | JsonUtility 不支持 NaN/Infinity | 另加一个 bool 表示「有没有设置」 | `VNSaveData.LiquidSave.sprayDirSet`（[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:87](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L87)） |
| 57 | 静态图集调 `TryAddCharacters` 无效 | 只有动态图集支持按需添加 | 先判 `atlasPopulationMode == Dynamic` | `VNFont.PrewarmAsset`（[Assets/Project/Scripts/VNEffects/Script/VNFont.cs:268](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L268)） |

### 20.5 IMGUI / 编辑器层

| # | 症状 | 原因 | 修法 | 出处 |
|---|---|---|---|---|
| 58 | `ArgumentException: Getting control N's position...` | 在 KeyDown 里改了列表长度 | 记 pending，下一个 Layout 再执行 | [`VNScenarioEditorWindow._pendingInsertAt`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:68](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L68)） |
| 59 | 代码里改文本框的值不生效 | 控件持有 keyboardControl 时用它自己的 TextEditor 缓冲 | `GUIUtility.keyboardControl = 0` 再改 | [`VNSearchListView.Reset`](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:78](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L78)） |
| 60 | 搜索框里按 ↑↓ 变成移光标、Enter 结束编辑 | TextField 先吃掉了按键 | 键盘处理排在 TextField **之前** + `e.Use()` | `VNSearchListView.Draw`（[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:113](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L113)） |
| 61 | 进 Play Mode 后编辑器窗口内容全没了 | 域重载重建窗口，普通字段清空 | `[SerializeField]` + `ISerializationCallbackReceiver` | `VNScenarioEditorWindow` 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:22](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L22)） |
| 62 | `OnBeforeSerialize` 里调 Unity API 出问题 | 它跑在序列化线程语境下 | 只做纯 C# 运算，还原挪到 `OnEnable` | `VNScenarioEditorWindow.OnBeforeSerialize`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:281](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L281)） |
| 63 | `DateTime` 存不进 `[SerializeField]` | Unity 序列化不支持 | 存 `long ticks` | `VNScenarioEditorWindow._fileTimeTicks`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:38](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L38)） |
| 64 | 静态事件攥着已销毁窗口的引用 | 订阅了没退订 | `OnEnable` 订 / `OnDisable` 退 | [`VNAssetLibraryEvents`](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs:14](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L14)） |
| 65 | 素材库广播每帧触发 | `ApplyModifiedProperties()` 每帧都调 | 只在返回 true 时 [`RaiseChanged()`](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L26) | `VNAssetLibraryEvents.RaiseChanged`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs:25](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L25)） |
| 66 | 列表里几十张缩略图闪一片空白 | `AssetPreview.GetAssetPreview` 是异步的 | Sprite 走 `DrawTextureWithTexCoords` 同步画 | [`VNAssetUi`](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:17](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L17)） |
| 67 | 某些资产的预览图永远等不到 | 有些资产就是没有预览 | 自己给 3 秒 deadline，到点放弃 | `VNAssetUi._previewDeadline`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:302](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L302)） |
| 68 | `IsLoadingAssetPreview(int)` / `GetInstanceID()` 报弃用错误 | Unity 6.5 起是 error 级弃用 | 不问 Unity，自己维护 deadline | 同上（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:302](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L302)） |
| 69 | `sprite.textureRect` 抛异常 | 打包进图集且 tight 模式时取不到 | `try/catch` 退回 `sprite.rect` | `VNAssetUi.DrawSpriteThumb`（[Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs:75](Assets/Project/Scripts/VNEffects/Editor/VNAssetUi.cs#L75)） |
| 70 | 列表里每个条目都重画一遍 Header，占 6~7 行 | `[Header]` 是 DecoratorDrawer，列表元素每项都画 | 挂 `CustomPropertyDrawer` 接管绘制，说明改 `[Tooltip]` | [`VNEntryDrawerBase`](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs:9](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs#L9)） |
| 71 | 自定义 Drawer 里控件错位 | `indentLevel` 又给 rect 加了缩进 | 手算 rect 时 `indentLevel = 0` | `VNEntryDrawerBase.OnGUI`（[Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs:51](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs#L51)） |
| 72 | 换浅色底后字消失 | Dark 主题下 EditorStyles 文字是浅色的 | 覆盖八个状态的 textColor | [`VNAssetTheme.Tint`](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs:252](Assets/Project/Scripts/VNEffects/Editor/VNAssetTheme.cs#L252)） |
| 73 | 加了新字段但 Inspector 里看不见 | 分页 Inspector 没登记这个字段 | 自动收集孤儿字段落到「其他」页 | [`VNGameConfigEditor.CollectOrphans`](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs:70](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs#L70)） |
| 74 | 隐藏空行时把语法错误行也藏了 | 按 `kind == Raw` 一刀切 | 只藏空行与 `#`，孤儿 `*` / `>` 必须显形 | `VNScenarioEditorWindow.IsHiddenRow`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1613](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1613)） |
| 75 | 装机时模板没有 RectTransform，位置全乱 | `new GameObject("X")` 默认挂普通 Transform | `new GameObject(name, typeof(RectTransform))` | [`VNQuestBoardInstaller.Install`](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs:58](Assets/Project/Scripts/VNEffects/Editor/VNQuestBoardInstaller.cs#L58)） |
| 76 | 编辑器改资产不进 Undo、不保存 | 直接写字段 | 走 `SerializedObject` | [`VNAssetBrowserWindow`](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs) 类注释（[Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs:22](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs#L22)） |

### 20.6 本地化层

| # | 症状 | 原因 | 修法 | 出处 |
|---|---|---|---|---|
| 77 | 切语言后分支走错 | 翻译参与了逻辑匹配 | 标识符永不翻译；选项按索引匹配 | [`VNMapModule.Location.DisplayName`](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs)（[Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs:41](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs#L41)）、`VNScriptRunner.ChoiceCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2876](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2876)） |
| 78 | 缺 key 时显示空白 | 没有回退链 | 当前语言 → 中文 → key 本身 | [`VNLocale.T`](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs)（[Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:76](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L76)） |
| 79 | 占位符与参数不匹配时抛异常 | `string.Format` 抛 `FormatException` | try/catch 返回原始 format + 警告 | `VNLocale.T`（[Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:94](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L94)） |
| 80 | 语言切换时字体没换、名牌样式垮 | `LanguageChanged` 在换字体之前触发 | setter 里先 `VNFont.HandleLanguageChanged()`（[`Assets/Project/Scripts/VNEffects/Script/VNFont.cs:277`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L277)） 再触发事件 | `VNLocale.Language`（[Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:53](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L53)） |

---

## 二十一、技术债与改进建议

按「投入产出比」排序。每一条都写明「现状 / 问题 / 建议 / 优先级」。

### 21.1 高优先级

#### 债务 1：`IsSecretPhotoIconAllowed` 需要手工维护面板清单

**现状**：`VNScriptRunner.IsSecretPhotoIconAllowed`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1799`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1799)）
（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:1799](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1799)`）
逐个列举了 9 个面板的 `IsOpen`。

**问题**：加一个新面板就要来这里补一行，漏了就出现「画廊开着但相机图标还在」。
而且第九章的 `Update` 模态链也有同样的问题（那里是 10 个分支）。

**建议**：引入一个极简接口
```csharp
public interface IVNModalPanel
{
    bool IsOpen { get; }
    /// <summary>返回 true = 我处理了这次输入，Runner 不要再往下走</summary>
    bool HandleInput(Keyboard kb, Mouse mouse);
}
```
Runner 持一个 `List<IVNModalPanel> _modals`，`Start` 里注册。于是：
- `IsSecretPhotoIconAllowed` → `_modals.TrueForAll(p => !p.IsOpen)`
- `Update` 的模态链 → `foreach (var m in _modals) if (m.IsOpen) { m.HandleInput(...); return; }`

**注意**：顺序仍然重要（CG 画廊的两层要在自己内部处理），
所以注册顺序 = 优先级顺序，要写注释说明。

**优先级：高**。改动小（十个面板各加一个接口实现）、收益大（消掉两处手工清单）。

#### 债务 2：抛异常的皮肤缺失处理

**现状**：[`VNQuickToolbar.Build`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs:51](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbar.cs#L51)`）、
[`VNConfigPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs)（`:106`）、[`VNSaveLoadPanel.Build`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadPanel.cs)（`:133`）、
[`VNBacklog.Build`](Assets/Project/Scripts/VNEffects/Script/VNBacklog.cs)（`:99`）、[`VNTitleMenu.Build`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs)（`:198`）、
[`VNStatsHud.BuildHud`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（`:255`）、`VNResultPopupModule.OnLaunch`（[`Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs:64`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupModule.cs#L64)）（`:84`）
在皮肤 prefab 缺失时 `throw new InvalidOperationException`。

**问题**：
1. 异常发生在玩家按 F5 / 打开设置的那一刻，比「显示一个丑但能用的面板」严重得多。
2. 异常信息是英文的，与专案其余部分的中文报错风格不一致。
3. `Build()` 常在 `Open()` 里被调，异常会打断 Open 的后续逻辑（`_open` 标志可能不一致）。

**建议**：换成
```csharp
if (_skin == null)
{
    Debug.LogError("[VNBacklog] 系统主题里没有配 backlogPrefab，" +
                   "请运行 Tools → VN Effects → UI 皮肤 → 系统主题：导出默认模板。");
    BuildEmergencyPanel();   // 一个只有标题 + 关闭按钮的最小面板
    return;
}
```
`BuildEmergencyPanel` 可以做成 [`VNSystemUiSkinUtility`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs) 里的一个共用静态方法
（一个暗幕 + 一段红字 + 一个关闭按钮，二十行）。

**优先级：高**。玩家不该因为配置缺失而卡在一个按键上。

#### 债务 3：[`VNToast`](Assets/Project/Scripts/VNEffects/Script/VNToast.cs) 的静态状态没有域重载重置

**现状**：`VNToast` 的 `_canvas` / `_stack` / `_cards`
（`[Assets/Project/Scripts/VNEffects/Script/VNToast.cs:34](Assets/Project/Scripts/VNEffects/Script/VNToast.cs#L34)`）是静态字段，
但没有 `[RuntimeInitializeOnLoadMethod]` 重置。

**问题**：关闭域重载（Enter Play Mode Options）时，
上一次 Play 遗留的 `_cards` 列表里可能有已销毁的卡片对象。
`EnsureCanvas` 会因为 `_canvas != null`（伪 null 判断）而……
实际上 `_canvas` 是 `DontDestroyOnLoad` 的，退出 Play 时会被销毁，
所以 `_canvas != null` 为 false、会重建；但 `_cards` 不会被清，
第一次 `Show` 时 `_cards.Count >= MaxCards` 可能立刻触发 `Dismiss` 一个已销毁的卡片。

**建议**：照 `VNPause.ResetStatics`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:117`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L117)）
（`[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:117](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L117)`）补一段：
```csharp
#if UNITY_EDITOR
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void ResetStatics()
{
    _canvas = null; _stack = null; _mode = null; _badge = null;
    _cards.Clear();
}
#endif
```

**优先级：高**（五行代码，消掉一类难查的偶发 bug）。

同样值得检查的静态状态：[`VNProceduralTextures`](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs) 的贴图缓存
（有 `HideFlags.DontSave` 会被自动清，但静态字段仍指向已销毁对象——
不过每个 getter 都判空重建，所以是安全的）、
[`VNFont`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs) 的 `_cache`（同理，判空重建）、
[`VNLocale`](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs) 的 `_tables`（纯数据，无 Unity 对象，安全）。

### 21.2 中优先级

#### 债务 4：八份重复的 `CreateImage` / `CreateText`

**现状**：见 16.2 节。至少八个文件各写了一份几乎相同的 UI 构建辅助。

**建议**：抽一个 `VNUiBuild` 静态类，提供五个最基础的：
```csharp
public static class VNUiBuild
{
    public static RectTransform Node(string name, RectTransform parent);
    public static Image Image(string name, RectTransform parent, Sprite sprite, Color color);
    public static TextMeshProUGUI Text(string name, RectTransform parent, int size,
                                       Color color, string content, TextAlignmentOptions align);
    public static RectTransform Stretch(RectTransform rect);
    public static RectTransform Center(RectTransform rect, Vector2 size, Vector2 offset);
}
```
统一约定：`raycastTarget = false`、`font = VNFont.Asset`、`sizeDelta` 清零。

**注意事项**：
- 各模块特有的（羽毛球的 `CreateQuad` / `CreateLine`、大头贴的配色常量）**留在原处**，
  只抽最基础的五个。
- **不要为了统一而加一堆可选参数**——那会比重复更难读。
- 迁移可以渐进：新模块直接用 `VNUiBuild`，老模块不动。

**优先级：中**。收益是「新模块少写 60 行样板」，但风险是迁移老模块可能引入回归。
建议只做「新增 `VNUiBuild` + 新代码用它」，不强制迁移。

#### 债务 5：面板的「语言切换 = 销毁重建」重复了十次

**现状**：见 3.1 节的表。十个面板各写了一份几乎一样的 `OnLanguageChanged`。

**问题**：不只是重复——**每次都要记得清所有缓存字段**（坑 #30），
而这个清单只存在于人的记忆里。

**建议**：抽一个基类
```csharp
public abstract class VNLocalizedPanel : MonoBehaviour
{
    protected Canvas _canvas;
    protected GameObject _panel;
    protected bool _open;
    public bool IsOpen => _open;

    protected virtual void Awake() => VNLocale.LanguageChanged += HandleLanguageChanged;
    protected virtual void OnDestroy() => VNLocale.LanguageChanged -= HandleLanguageChanged;

    void HandleLanguageChanged()
    {
        if (_open) Close();
        if (_canvas != null) Destroy(_canvas.gameObject);
        _canvas = null;
        _panel = null;
        ClearCachedReferences();   // 子类清自己的字段
    }

    /// <summary>子类必须在这里把所有缓存的子物体引用置 null</summary>
    protected abstract void ClearCachedReferences();
    ...
}
```

**收益**：`_canvas` / `_panel` / `_open` 的处理写一遍；
子类只需要实现 `ClearCachedReferences`——而且**这个抽象方法的存在本身就是提醒**。

**优先级：中**。改十个文件，但每个改动都很机械。
建议在下次要动这些面板时顺手做。

#### 债务 6：日记本的开关路径与其他面板不一致

**现状**：见 8.11 节。D 键走 [`RequestDiary()`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L1716)（按需创建），
其他面板走 `_backlog?.Open()`（Start 里已创建）。

**建议**：把所有面板的按键处理都统一走 `Request*`，
`Request*` 内部负责「按需创建 + 事件中不开 + Toggle」。
这样新面板照抄任何一个都对。

**优先级：中**（与债务 1 一起做最省事）。

### 21.3 低优先级 / 观察项

#### 观察 7：`UrgentSeconds` 在三个模块各定义一次

值分别是 3 / 3 / 5（`VNQuizModule.UrgentSeconds`（[`Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:44`](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L44)），
`[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:44](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L44)`；
`VNPhotoBoothModule.UrgentSeconds`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:118`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L118)），
`[Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs:118](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothModule.cs#L118)`；
`VNFogWipeModule.UrgentSeconds`（[`Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:65`](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L65)），
`[Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs:65](Assets/Project/Scripts/VNEffects/Script/VNFogWipeModule.cs#L65)`）。

**建议**：如果以后要统一「紧急感」的表现（颜色、脉动频率、抖动幅度），
抽一个 `VNUrgency` 静态类。现在只有三处、值还刻意不同，**不用动**。

#### 观察 8：`VNConfigPanel.WheelOpensBacklog` 每帧读 PlayerPrefs

[`VNScriptRunner.Update`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（`[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2097](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2097)`）
每帧调一次 `VNConfigPanel.WheelOpensBacklog`
（`[Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs:36](Assets/Project/Scripts/VNEffects/Script/VNConfigPanel.cs#L36)`），
它内部是 `PlayerPrefs.GetInt`。

**建议**：缓存到一个静态 bool，setter 时同步更新。
**优先级：低**（Windows 上 PlayerPrefs 是内存缓存，成本极小）。

#### 观察 9：[`VNPhotoStickerItem.Update`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBoothUi.cs) 每个贴纸一个

同屏几十张贴纸 = 几十个 `Update`。
前两行 `if (locked || !_hover) return;` 让开销接近零。

**建议**：只有当贴纸数上百时才改成「模块统一处理当前 hover 的那一个」。
**优先级：低**。

#### 观察 10：系统 UI prefab 全部常驻内存

见 15.5 节。`VNGameConfig.systemUiSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)） 的直接引用会把 12 个 prefab 一起加载。

**建议**：总内存超过 50MB 时改成 `AssetReference` + Addressables。
**优先级：低**（当前规模完全可接受）。

#### 观察 11：`VNTitleMenu` 用 `GameObject.Find("HintText")`

`VNTitleMenu.ApplyTitleStage`（`[Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs:107](Assets/Project/Scripts/VNEffects/Script/VNTitleMenu.cs#L107)`）
按名字找物体，正是 [`VNTutorialAnchors`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs) 类注释里批评的做法。

**建议**：让 [`VNStage`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs) 持有它，或者把它纳入 [`VNUiParts`](Assets/Project/Scripts/VNEffects/Script/VNUiParts.cs)。
**优先级：低**（后果只是提示文字没藏起来）。

#### 观察 12：[`VNCamseqEditorWindow`](Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs) 的下拉数组与运行时枚举靠注释同步

`ZoomModeNames`（`[Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs:52](Assets/Project/Scripts/VNEffects/Editor/VNCamseqEditorWindow.cs#L52)`）
注释写着「顺序必须与 VNCamZoomMode 一致」。

**建议**：加一个编译期断言
```csharp
static VNCamseqEditorWindow()
{
    Debug.Assert(ZoomModeNames.Length ==
                 System.Enum.GetValues(typeof(VNCamZoomMode)).Length,
                 "[VNCamseq] ZoomModeNames 与 VNCamZoomMode 长度不一致");
}
```
至少能挡住「加了枚举项忘了加显示名」。
**优先级：低**（但成本也极低，值得顺手加）。

### 21.4 值得保留、不要「优化」掉的东西

最后列几条**看起来像技术债但其实是对的**，避免以后误改：

1. **十个近乎重复的 Esc 分支**（`VNScriptRunner.Update`）——
   可读性优于 DRY。你能一眼看出每个面板的键位。
   （不过债务 1 的接口化能在保留可读性的前提下消掉重复，值得做。）

2. **`MaxTotalHeight` 写成 const 而不是 public 字段**
   （`VNChoicePanel.MaxTotalHeight`（[`Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:283`](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L283)），`[Assets/Project/Scripts/VNEffects/VNChoicePanel.cs:283](Assets/Project/Scripts/VNEffects/VNChoicePanel.cs#L283)`）——
   它是几何结论不是手感参数，做成序列化字段会被场景里的旧值盖掉。

3. **手工布局 SNS 而不用 LayoutGroup**
   （[`VNSnsView`](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs) 类注释，`[Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs:18](Assets/Project/Scripts/VNEffects/Script/VNSnsView.cs#L18)`）——
   聊天列表需要精确高度，LayoutGroup 的重建时序不可控。

4. **搜索只做子串包含不做模糊匹配**
   （[`VNSearchListView`](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs) 类注释，`[Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs:42](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs#L42)`）——
   行为可预期比「聪明」重要。

5. **教程暗幕吃掉洞内点击（只读演示）**
   （[`VNTutorialMask.Awake`](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs)，`[Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs:70](Assets/Project/Scripts/VNEffects/Script/VNTutorialMask.cs#L70)`）——
   这是明确的功能边界。要做交互式教程时再改，不要提前设计。

6. **三次「破铁律」都保留**——每次都写明了边界与还原路径，
   而且注释互相引用。这是好的工程记录，不是坏味道。

7. **[`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) 的七层保险**——后果是「游戏永久卡死」，保险层数应该多。

---

## 结语：这个专案的 UI 架构给你的六条可迁移经验

1. **分层不是为了好看，是为了「不要让两个系统写同一个字段」。**
   SceneRoot/ZoomRoot/TiltRoot 三层容器、缩放倍率双通道、`SetGrade` 分层调色、
   光标的 root/icon 两层——同一条原则的四次应用。
   凡是「A 改了之后 B 又改回去」的 bug，都是这条被违反了。

2. **程序化默认 + 皮肤槽位 = 零素材可运行 + 美术自由发挥。**
   关键在于「程序化的产物也塞进同一个槽位声明」，让行为逻辑只写一遍。
   而「全部槽位可选 / 缺了就降级」让皮肤作者不必一次配齐。

3. **状态不放在 UI 里，UI 只是数据的视图。**
   [`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs) 一张字典 + 一个粗事件 + 「标脏、下帧刷一次」，
   撑起了属性、任务、道具、装备四个系统的全部 UI。

4. **模态优先级用「一串 early return」表达就够了，但要给被拦住的人留一条转发通道。**
   `RequestAdvance` 的存在是这套架构的必然配套。

5. **凡是「写错了不会报错但游戏就不好玩」的约定，都要写进静态检查。**
   `wipefog-cg-before-event`、`* 失败` 必须接住——
   注释挡不住人，Lint 能。

6. **把踩过的坑写在踩坑的那一行上面。**
   这个专案最值钱的不是代码，是注释里那几十条「为什么不能那样做」。
   本文件二十章的清单，全部来自这些注释。
   **规则破例时写明「破的是哪条、边界在哪、怎么还原」**——
   这一个习惯让三次破例都没有变成失控。

---

*本文件基于 2026-09-05 的 `main` 分支代码撰写。所有行号来自实读，
未实读的部分只写档名与符号名。*
