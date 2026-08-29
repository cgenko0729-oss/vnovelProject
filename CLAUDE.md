# CLAUDE.md — 视觉小说项目（vnovelProject）

> 本文件是给 Claude（AI 助手）的项目说明书。所有开发过程的详细记录在 `WhatAiDo.md`；
> 逐脚本的代码指南（职责/用法/扩展/维护）在 `ProjectCodeGuide.md`，改代码前先查它；
> 从空场景手动搭建舞台的完整教程（含层级/排序/参数依据）在 `SetUpGuide.md`；
> 剧本写法教程在 `HowToUse.md`；
> AI 自由聊天的原理与调参（记忆机制/提示词组装/成本/日志）在 `AiTalkGuide.md`，
> 这一块的改进路线图与构想清单在 `AiTalkIdeas.md`。
> **可复用工作流程已做成技能**（`.claude/skills/`），做对应任务时先调用技能拿清单，见下方「技能索引」。

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
3. 每批开发完成后**详细追加记录到 `WhatAiDo.md`**（模板见技能 vn-doc-update）
4. 提交信息英文、正文中文注释；commit 尾部加 Co-Authored-By
5. 分支/合并/推送的完整流程与坑（unlink 报错、后台推送）见技能 **vn-new-feature**

## 技能索引（.claude/skills/，按需调用）

| 技能 | 什么时候用 |
|---|---|
| vn-new-feature | 开始任何新功能/修 bug（分支、提交、合并流程） |
| vn-doc-update | 功能完成后同步文档（WhatAiDo 章节模板等） |
| vn-new-command | 给剧本 DSL 加新命令（全链路 9 步清单） |
| vn-new-event-module | 写新玩法事件模块（三铁律、注册、结果契约） |
| vn-new-effect | 加新特效/演出组件（硬约定、fx 接线、演示场景） |
| vn-save-compat | 新增任何运行时状态（存档/调试重建三处同步） |
| vn-editor-extend | 改剧本可视化编辑器（Schema、行号换算、Bridge 时序） |
| vn-write-scenario | 写/改 .vn.txt 剧本（语法要点、Lint） |
| vn-add-assets | 接素材（立绘/背景/CG/音频/定义资产 → VNGameConfig） |
| vn-ui-skin | 做 UI 皮肤（对话框/选项皮肤 与 系统菜单主题两条线） |
| vn-localize | 本地化（Extract/Validate、翻译红线、加新语言） |
| vn-debug | 剧本/演出排错（Lint、从选中行播放、编译验证） |

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

### 关键技术约定（一行版，展开细节见技能 vn-new-effect / vn-ui-skin / vn-localize）

- **发光=HDR 颜色(>1) + Bloom(阈值1.0)**；uGUI 顶点色被钳到 1，HDR 走材质属性
- **粒子分两类，别用错混合模式**：会发光的（星光/萤火虫/尘埃/光斑）用 `VN/Additive`
  加法混合；**有实体的（花瓣/落叶/雨/雪）必须用 `VN/ParticleAlpha` 普通透明混合** ——
  加法只能加亮不能遮挡背景，彩色粒子叠明亮背景后通道溢出会被 Bloom 洗成白色
- **调色一律走 `VNImageEffectController.SetGrade(通道, ...)`，禁止直接写
  `_Brightness`/`_Saturation`**：这两个参数被说话者高亮（每句台词都改）、伪景深、
  情绪动作、退场动画、天气联动、情绪色调六方共用，直接写谁最后写谁赢，
  症状是「说一句话立绘颜色就跳回去」。每个来源占一个 `VNGradeLayer` 通道，
  合并层负责相乘叠加。老 API（`SetHSV`/`DOBrightness`/`DOSaturation`）已改走
  `Manual` 兜底通道，能用但别在新代码里用
- **情绪色调不是全屏后处理**：单相机 + 单个 Screen Space - Camera 的 Canvas 下，
  Volume 调色作用于整个 color target，没法只染背景。想让某层躲开 mood，
  把它移出 `VNMoodGrading` 的目标列表即可；反之新加的图层要染色就得注册进去。
  **别试图用 URP Camera Stack 解决**——整个 stack 共用一个 color target，
  后处理在最后一个相机之后统一执行一次，Overlay 相机躲不掉；真能躲开的
  `Screen Space - Overlay` 又会连 Bloom 一起躲开（对话框流光/名牌发光全废）
- **贴图全程序化生成**（`VNProceduralTextures`），零美术依赖
- **每张图独立材质实例**（`VNImageEffectController` 管理）；uGUI 自定义 shader 走 CGPROGRAM
- **UI 不写深度缓冲** → 无真 DoF，模糊走 `VNImageEffect` 9-tap
- 文字全用 **TextMeshPro** + `VNFont.Asset` 统一入口，禁止 legacy Text；
  玩家可见 UI 字符串一律 `VNLocale.T(key)`，禁止硬编码；
  编辑期存场景的 TMP 文字必须用 `VNFontAssetBuilder.EnsureFontAsset()` 持久化资产
- 所有 Tween `SetLink(gameObject)`；循环效果 Start/Stop 成对 API
- 立绘缩放走「倍率」机制（`DOScaleMultiplier`），别直接改 localScale
- 粒子 velocityOverLifetime 三轴曲线模式必须一致
- 运行时创建带 Awake 配置的组件：先 SetActive(false) 挂好赋值再激活

### 组件速查（Assets/Scripts/VNEffects/）

| 组件 | 职责 |
|---|---|
| VNImageEffectController | 单图特效总控：溶解/扫光/发光/闪白/HSV/波浪/轮廓光/波光/模糊 + 悬浮/呼吸动作 |
| VNEntranceAnimator | 出场预设×10（日常向 crossfade默认/slidein/stepin/walkin + 华丽向 溶解辉光/滑入/弹出/扫光/爆闪/残影冲入）+ 退场×4（fade默认/dissolve/runout/sink）+ StartIdleEffects；方向 `from:`/`to:` 留空按站位推断，`dur:` 给目标秒数，日常向不开周期扫光（进存档） |
| VNGlowBackdrop / VNFootShadow | 背后光环脉动 / 脚下椭圆影（悬浮联动 + `Impact()` 落地摊开，stepin 用） |
| VNCharacterEmotes | 情绪动作：惊讶/生气/害羞/沮丧(+Recover)/点头/摇头 |
| VNAmbientParticles | 粒子预设×8：尘埃/星光/光斑/花瓣/雨(+溅落)/雪/萤火虫/雾 + PlaySparkleBurst |
| VNWeatherController | 天气总控（双后端）：飘落类走 VNFoliageSystem，雨/雪/萤火虫走 VNAmbientParticles；`SetWeatherId` 三级解析 id（自定义资产 → 内置叶型别名含中文 → VNWeather 枚举），带调色联动 |
| VNFoliageSystem / VNWeatherDef / VNFoliageTextures | 落樱/落叶三层景深系统（Alpha 混合实体粒子 + 图集翻转 + **每粒子独立相位横摆** + 自动阵风 + 尺寸↔速度伪透视 + 地面堆积）/ 全部参数的 ScriptableObject（五套内置预设，不建资产也能用）/ 五种叶型的程序化图集（列=12 翻转帧、行=4 形态变体，RGB 存明暗、A 存形状） |
| VNMoodGrading / VNGrade | 八种情绪色调（含 Dream 梦境）**分层调色版**：色彩不走全屏后处理（单相机单 Canvas 下 Volume 物理上没法只染一部分，会把对话框和 HUD 一起染橙），改按 `backgroundStrength(1.0)`／`midStrength(0.8)`／`characterStrength(0.3)` 逐层写进各自材质实例，UI 不在目标列表所以完全不受影响；**Volume 只留 FilmGrain + Vignette**（不改色相，压四角反而有电影感），仍是 A/B 双 Volume 交叉过渡。立绘目标由 VNStage 在角色进出场时自动维护 / 调色值类型 + 来源通道枚举 `VNGradeLayer`（Mood·Weather·Focus·Emote·Manual），合并规则 滤镜相乘·色相相加·其余相乘 |
| VNScreenTransition | 全屏转场×8：噪声溶解/百叶窗/瓦片/圆扩散/水墨/爆闪/光斑/眨眼 |
| VNCamera / VNScreenShake / VNDutchAngle / VNHeartbeat | 运镜×5 + 路径镜头（camseq 路径点可带 `shake:` 到点震屏，震完才走下一段，停顿取 max(hold,震动时长)；点位写 `stay` = 原地不动、沿用上一个点的位置与 zoom，**此时唯一的数字是时长**）/ 三级震动（「等级→数值」唯一一张表在 `VNShakeSpec`，运行时与编辑器预览共用）/ 荷兰角 / 心跳脉动 |
| VNGodRays / VNEdgeGlow / VNCloudShadows / VNHeatHaze / VNFakeDoF | 光束/情绪泛光/云影/热浪+雾/伪景深 |
| VNSpeedLines | 漫画速度线/集中线 overlay（3 变体贴图闪帧，fx speedlines on/off/burst） |
| VNScreenShockwave | 全屏情绪水波（fx shockwave [light\|heavy]：波峰环 overlay + 背景波浪脉冲 + 轻震动） |
| VNLiquidSplash / VNWetScreen / VNLiquidPreset | 液体喷溅**两层**（缺一层就不成立）：舞台层空中水珠（拉伸公告板 Body + HDR Glow + 碎珠三发射器，`Burst` 爆溅 / `StartSpray` 间歇噗噗喷 / `SetClickMode` 点击喷水）/ 屏幕层镜头水渍（uGUI 对象池；尺寸与空中水珠同量级 4~8px，分小水点/大滴两档，只有大滴走撞击形变→挂住→下滑拖痕→蒸发四段状态机；小水点用 WaterSpeck 细长图，大滴才用 WaterDrop 假折射剖面——那套剖面缩到几像素会糊成灰环变肥皂泡）/ 四套内置液体预设（water·blood·ink·slime，黏度=重力+拉伸+下滑速度+干涸时间四参数合谋） |
| VNRetroFilter | 胶片/CRT 复古滤镜（fx filmgrain/crt；mood Memory 自动胶片、Dream 自动 CRT） |
| VNBackgroundScroll | 背景无限滚动（`bgscroll on|off [speed:] [dir:] [mode:] [time:]`）：**滚 UV 不是拼两张图**——一个 Image 就够，bg 转场/运镜/视差全不用动，**且能和 Ken Burns 叠加**（那个动 transform、这个动 UV）。平铺在 shader 里自己折不靠纹理导入设置：`repeat` 需无缝图、`mirror` 镜像折返任何图都不穿帮（但强透视的走廊/街道会变成「两条走廊对着开」）。speed 是画布像素/秒（走路≈120、云飘≈6），dir 说的是画面往哪边流。**背景图别开 Generate Mip Maps**，否则接缝糊一行。状态进存档，换图不停滚只归零偏移 |
| VNKenBurns | 背景 Ken Burns 漂移（60~90s 随机航点缓慢缩放+平移，默认开启永不静止，fx kenburns on/off） |
| VNLetterbox | 电影黑边上下滑入（letterbox on/off [height:][time:]，mood Memory 回忆自动联动） |
| VNShootingStars / VNDriftingClouds | 夜晚偶发流星（fx meteor）/ 云本体缓移（fx skycloud，与云影互补） |
| VNParallax / VNMouseStardust / VNClickRipple | 鼠标视差 / 星尘拖尾 / 点击涟漪 |
| VNSpeakerHighlight / VNToneMatch | 说话者高亮 / 立绘色调匹配背景 |
| VNDialogueBox + VNTypewriterText | 对话框（流光边框/名牌/箭头）+ 打字机逐字上浮（TMP textInfo 顶点动画）；支持皮肤 prefab（VNDialogueSkin 槽位绑定，程序化默认兜底） |
| VNNameplateStyle | 名牌装饰样式（粗黑体+描边+渐变+投影+浮雕）：**十套内置预设** —— 老四套 Plain/Bold/Plate/Outline + 三层字系列 Duo(双描边)/Gold(金边)/Silver(银边)/Neon(霓虹)/Ink(墨影)/Candy(糖果)；**三层字系列的立身之本是「最外圈必须深色」**——Bold/Outline 最外层是白的，遇到白背景或亮立绘整个消失。金/银的金属感来自 TMP 的 **Bevel 浮雕 + Lighting 打光**（Mobile 版 shader 没这组属性，`HasProperty` 挡掉并警告一次）；霓虹靠 `faceHdrBoost>1` 写进 `_FaceColor` 触发 Bloom（顶点色被钳到 1，所以 **HDR 发光与上下渐变二选一**）。剧本 `ui name <样式|default>` 切换（进存档），或 `VNDialogueBox.nameplateStyle` / `SetNameplateStyle()`；配色每角色一套（VNCharacterDef 没勾自定义就由 nameColor 自动推算渐变，存量资产零改动）。**三条硬约定**：材质必须走 `text.fontMaterial` 实例（改 sharedMaterial 会污染所有同字体文字）／underlay 通道只有一条所以「第二层外描边」与「投影」二选一／改 underlay 前必须 `EnableKeyword("UNDERLAY_ON")`。名牌宽度自适应只动程序化默认皮肤 |
| VNDialogueSkin / VNChoiceSkin | UI 皮肤槽位声明组件（挂 prefab 根）：全槽位可选留空降级；剧本 `ui dialogue\|choice <id\|default>` 切换，id 在 VNGameConfig 的 UI 皮肤区登记；起步模板 Tools → VN Effects → UI Skins → Export Skin Prefabs（烘焙贴图+生成默认/顶部/右列样例并自动登记）；另有 **Export Soft Gradient Skins** 一键出三套**无框渐变**皮肤（白渐变/粉渐变/黑渐变：整屏底部渐变带+居中台词，shineFrame 留空即无边框）；皮肤状态进存档 |
| VNSystemUiSkinSet / VNSystemUiSkinBehaviour | 系统菜单唯一全局 prefab 主题及安全实例化基类；标题/设置/CG/Backlog/快捷条/存读档/顶部属性 HUD/完整属性页/背包/排程面板/结算弹窗分别使用槽位组件，单项缺失或槽位无效时只退回该项程序化 UI；默认模板菜单 Tools → VN Effects → System UI Skins → Export Default Prefabs（详见八十三章）；只重导排程/结算两项用 Export Event Panel Prefabs（详见八十六章） |
| VNFont / VNFontAssetBuilder | TMP 中文字体统一入口（三级兜底+Prewarm）/ 预烘焙字体资产生成器；另有**装饰字体**入口 `DisplayAsset`（思源黑体 Black，名牌等少量大字专用，正文别用）——单开一套资产是因为 **padding 必须与采样点等比例**：描边厚度 ≈ outlineWidth×(padding+1)×(字号/采样点)，padding 是描边粗细天花板，但 padding 占采样点过大（如 64pt 配 24）反而挤掉字形分辨率、把描边糊成淡影，故装饰字体用 120pt/padding 22（~18%）。语言切换时正文与装饰字体**分开替换**，换 font 会丢材质实例故有 `DisplayFontChanged` 事件通知重新上样式 |
| VNChoicePanel | 选项演出（飞入/悬停扫光/落选溶解），需 EventSystem |
| VNSakuraBurst | 樱吹雪告白组合技（走 VNFoliageSystem：起手阵风冲击+Burst、中途补风、尾声风力衰减、近景大瓣横掠） |
| VNCharacterBlink / VNCharacterBlinkOverlay / VNCharacterMouth | 默认表情自动眨眼两选一：整张闭眼立绘替换 / 只在眼部叠一张透明闭眼图（角色资产 `blinkMode` 切换，间隔与闭眼时长共用）；说话口型（透明画布叠加层） |
| VNCharacterMarks | 立绘漫符（汗滴/井字怒气/感叹号/问号/爱心/音符/红晕/灯泡/省略号/眩晕星/蒸汽）：`mark <角色> <符号\|clear> [keep\|off] [pos:x,y] [size:] [dur:]`；符号图程序化生成、角色资产可用自定义图覆盖；位置取 VNCharacterDef.markAnchor（归一化偏移）；keep 常驻符号进存档 |
| VNEventModule / VNEventRegistry | 玩法事件接口：模块基类 + id→模板注册表（EventLayer 排序 60） |
| VNQteModule / VNMapModule | 事件示例模块：QTE 连打条 / 地图选地点（条件显隐+去过标记） |
| VNBattleModule | 回合制小战斗（event battle，结果 胜利/失败/逃跑；patkstat/phpstat/pdefstat 从 flag 读属性=养成联动，结束写 flag 战斗剩余HP 供车轮战） |
| VNLiquidInstaller (Editor) | 把液体喷溅**增量装进当前场景**（Tools → VN Effects → Install Liquid Splash To Scene）：Canvas 下补 WetScreen + 场外补 LiquidSplash + 两层互连并回填 VNStage，不重建场景、可重复执行。老场景不跑它的话 `liquid` 命令会静默无效果 |
| VNQuizInstaller (Editor) | 把 quiz 模块**增量装进当前场景**（Tools → VN Effects → Install Quiz Module To Scene）：补禁用 QuizTemplate（必须带 RectTransform）+ 登记题库，不重建场景、可重复执行 |
| VNCamWaypoint / VNCamseqText / VNCamseqTemplates (Editor) | camseq 路径点行的结构化视图（严格 TryParse/Format，语法与运行时 ParseCamWaypoint 同构；震屏下拉 VNCamShakeUi 也在这儿，两个编辑器窗口共用）/ 一整段 camseq 文本的 TrySplit·Join / 11 条内置运镜模板（`{char}` 占位按当前角色替换）。**存储仍是 VNRow.camLines 字符串**，本层只做每帧现解析、改完写回的中转 |
| VNBadmintonModule / VNBadmintonDef | 羽毛球对战事件模块（`event badminton vs: id: target: first: mode: powerstat:/speedstat:/jumpstat: flag:`，结果 胜利/失败/结束）/ 对手+难度+台词+立绘+音效定义资产（登记进 VNGameConfig）；战绩写 flag `<前缀>_我方得分/_对方得分/_精准数/_最长回合`；装机走 Tools → VN Effects → **Install Badminton Module To Scene** |
| VNBadmintonBallistics / VNBadmintonCourt / VNBadmintonActor / VNBadmintonSfx / VNBadmintonUi | 羽球的四层拆分：**纯静态弹道数学**（三点定抛物线/落点抽样/精准判定，无 MonoBehaviour 依赖可单测）/ 程序化球场与 HUD / 角色表现层（六态假动画，**换真动画只改这一个文件**）/ 五个代码合成音效 / 共用 UI 辅助 + 画梯形的 `VNBadmintonQuad` |
| VNPhotoBoothModule / VNPhotoFrameDef / VNPhotoStickerDef / VNPhotoBackdropDef / VNPhotoThemeDef | 拍大头照事件模块（`event photo vs: me: theme: mode: frame: bg: time: stat:/rate: flag:`，写了 theme: 才评分＝完美/普通/失败，不写＝自由拍照只返回完成）/ 边框（程序化样式+开窗形状+水印+自带装饰）/ 贴纸 / 背景（8 种程序化样式，画在人身后被开窗裁切、按 cover 铺满）/ 主题＋**清单制**评分表（表情·边框·背景·贴纸四张加分清单＋命中评语）；左栏四标签页 边框｜背景｜贴纸｜涂鸦，人物与贴纸同一套手势（拖动·滚轮缩放·Shift+滚轮旋转·双击换前后），背景加 Ctrl（Ctrl+拖动移位·Ctrl+滚轮缩放，缩不到比 cover 更小所以永不露边）；开窗内层序 背景→拖动板→人后贴纸→我→她，**拖动板必须压最底**否则吃掉人后贴纸的射线；操作说明在右上角「?」钮里点击开合（卡片底色必须不透明，且要进拍照的 hide 列表）；结算时左右栏与取景框一起收起（分数栏与右栏横向重叠，不收就读不清）；布局尺寸全部常数化在文件头（机身 1860×1020／取景框 1040×780／侧栏 340×900／相纸 860×770）；成绩写 flag `<前缀>_分数/_档位/_次数`；装机走 Tools → VN Effects → **Install Photo Booth Module To Scene**（缺资产会自动铺一套默认的） |
| VNPhotoDoodle | 照片涂鸦（落書き）：**两张 768×576 位图画布**（分辨率与取景框 1040×780 成对，放大倍率压在 1.35x 内才不糊）——普通笔走 Alpha 混合、荧光笔走 `VN/Additive`(HDR+Bloom)，两种混合模式没法共存所以分开；位图而非矢量是因为要「擦除」（抹 alpha 一行代码）；线段按笔粗插值补点（否则快速拖动画出一串断圆）；撤销只快照被动过的那张（5 步，1.7MB/张）；输入板只在涂鸦页 `raycastTarget=true` |
| VNPhotoScore / VNPhotoTextures / VNPhotoCapture / VNPhotoAlbum / VNPhotoBoothUi / VNPhotoSfx | 大头贴的六层拆分：**纯静态评分数学**（无 MonoBehaviour 可单测）/ 程序化边框·遮罩·10 种贴纸·相纸 / **取景框截图**（「怎么拍」全在这一个文件，换 RenderTexture 只改它）/ 相册全局存储（PNG+index.json，与存档槽分离、LRU 纹理缓存、上限 200 张）/ 共用 UI 辅助 + 贴纸拖拽组件 `VNPhotoStickerItem`（拖·滚轮缩放·Shift+滚轮旋转·右键删）/ 五个代码合成音效 |
| VNQuizDef / VNQuizModule | 限时问答题库资产（三语题干+2~4 选项+每题奖励/惩罚）/ 限时问答事件模块（event quiz id:题库 count: time: pass: pick:，结果 全对/及格/失败，成绩写 flag &lt;前缀&gt;正确数、&lt;前缀&gt;总数；超时按答错，倒计时最后 3 秒变红脉动） |
| VNAiProvider / VNAiKey / VNAiClient(+Gemini/+DeepSeek) | **供应商抽象层**：能力差异（谁支持硬 schema / 安全阈值）、默认模型、key 名全登记在 `VNAiProviders`，全局默认在 `VNGameConfig.aiProvider`（人格资产选「跟随全局」即跟着换，2026-08 起默认 DeepSeek `deepseek-v4-flash`）/ API Key **按供应商各一套**三级回退读取（环境变量 → 仓库外 → 仓库内；分开缓存、永不打印、`#if UNITY_EDITOR` 挡住 Build 版本读取）/ **全项目唯一碰 HTTP 的文件**——传输、重试、错误分类在 `VNAiClient`，**各家只差「拼请求体」和「解响应」**，拆在 `VNAiClientGemini` / `VNAiClientDeepSeek`（纯静态不碰网络）。协程封装而非 async/await（与 EventCo 的轮询等待天然契合），失败分 8 类供上层决定兜底还是重试，429/5xx/网络错误自带指数退避。**Gemini 契约三坑**：`thinkingLevel` 必须放 `thinkingConfig` 里面且只有 minimal/low/medium/high；鉴权走 `x-goog-api-key` 请求头不用 `?key=`；被安全策略拦下时 `parts` 是空的，直接取 `parts[0]` 会空引用。**DeepSeek 四差异**：没有 json_schema（只有 `json_object`，格式改由提示词约束＋解析层兜底；**历史里 assistant 的消息也必须是 JSON**，是纯文本时模型会退化成吐一串空白 → 第 2 轮起必挂，见 VNAiConversation.AppendHistory）、system 是 messages 第一条且助手角色叫 `assistant`、没有 safetySettings、思考是开关＋三档 `reasoning_effort`；响应带 `prompt_cache_hit_tokens`（命中价便宜 30 倍，算钱要拆开）|
| VNAiPersonaDef / VNAiConversation | AI 人格资产（独立于 VNCharacterDef，因为一个角色要能有多套人格共用同一套立绘；表情/漫符白名单留空则取角色资产全部）/ **纯逻辑层（无 MonoBehaviour，可单测）**：system prompt 组装、JSON Schema 生成、历史裁剪、响应解析与钳制。提示词顺序 身份→说话方式→关系→此刻情况→输出规则→（没有硬 schema 的家在这里插一段「输出格式」示例 JSON）→边界（越靠后权重越高）。**永远不信任模型输出**：好感强制 Clamp（Gemini schema 不支持 minimum/maximum，不钳实测会给 +5）、表情越界降级、选项不足补齐 |
| VNAiMemory / VNAiDiary / VNAiDiaryPanel | **跨场记忆（存档态）** / **日记本（全局态）** / 日记本面板（D 键）。两者存储语义**刻意相反**：记忆是剧情状态必须跟着存档回退（读旧档她不该记得未来），日记是玩家收藏品不该因读档消失（同 CG 画廊）。一场聊完额外发一次请求，产出 摘要+话题标签+关键事实（→记忆）和 主角口吻日记（→日记本）。**「少重复」的主力是话题清单**——单独成段作为硬性回避清单注入，比把话题揉进摘要里说「别重复」有效得多。**踩过的坑**：总结请求若按 role:user/model 交替发历史，模型会代入她的身份写出「她的日记」，改成把对话拍平成纯文本放进单条 user 消息才对——身份认知不对时先看 role 结构，再改措辞 |
| VNAiPricing / VNAiPricingDef | **算钱的唯一入口**（换模型不改代码就会算错的那件事）：按模型名查每百万 token 单价，资产 Create → VN → AI Pricing 登记进 `VNGameConfig.aiPricing`，不建资产用内置默认表（Gemini flash-lite/flash/pro + DeepSeek v4-flash/v4-pro）。**查表按 key 最长优先**——`gemini-3.5-flash-lite` 同时含 `flash` 和 `flash-lite`、`deepseek-v4-flash` 也含 `flash`，不排序会被隔壁家的价抢走；认不出的模型取**最贵**档并标「单价存疑」（低估会让人放心用下去，高估最多多留意一眼）。思考 token 按输出价计费。**三个价 + 一个倍率**：未命中输入 / 命中缓存输入（DeepSeek 便宜约 30 倍）/ 输出，再乘高峰倍率（DeepSeek 高峰翻倍，时段列表也在资产里，官方改时段不用动代码）；重算历史日志时按**那场对话当时**的 UTC 时间判高峰，同一份日志今天看和明天看必须同一个数字 |
| VNAiCostReport (Editor) | 花费累计报表（Tools → VN Effects → AI → Cost Report）：扫 `AiTalkLogs/`（含 `Editor/`）全部 json，按月/日/人格/模型/来源聚合，可导 CSV。默认**按当前单价重算**（日志存了 token 数、模型名与缓存命中数，能修正历史上按写死单价算出的错误金额；高峰倍率按日志里那场对话当时的时间判，不是按现在） |
| VNAiStudioWindow (Editor) | **AI 试聊台**（Tools → VN Effects → AI → AI Talk Studio，人格资产右键也能开）：不进 Play Mode 调人格与提示词。三栏＝左改参数／中聊天流／右 **system prompt 实时预览**（不发请求不花钱，改一个字立刻重拼——调 boundaries、speechStyle 的主力）。中栏可点选项、可自由输入任意回复（绕开三选一）、可重跑本轮看方差、可从任意轮重新分岔。配套 `VNAiStudioDraft`（**临时 SO 副本**当草稿层：SerializedObject 迭代画＝零 UI 代码就有全部字段、加新字段自动跟上；写回逐属性 copy 而**不用 CopySerialized**——那会把 m_Name 一起抄成「xxx(Clone)」）／`VNAiStudioSession`（会话驱动，域重载后靠轮次记录 `BuildRequest`+`RecordReply` 重建历史）／`VNAiStudioMemory`（记忆预设，见下）／`VNAiStudioLog`（导出到 `AiTalkLogs/Editor/`，与游戏内**同格式**所以两边能互相对比）／`VNAiEditorCoroutine`（编辑器协程泵，与自检菜单共用） |
| VNAiStudioMemory (Editor) | 试聊台的**可命名记忆预设**（`<项目根>/AiTalkStudio/Memories/*.json`，不进 git）：「初次见面（空）」「聊过 3 次」各存一套，一键切换。**完全独立于运行时 `VNAiMemory`**——那份是存档态，编辑器往里写等于造出「读旧档她却记得未来」的幽灵状态。两个独立开关：`注入记忆`（勾掉再跑一遍＝直接对比有无记忆的差别）与 `结束时做总结`（多发一次请求，结果先预览再决定收不收）。导入三源：试聊后总结／从 `AiTalkLogs` 日志（**要发一次总结请求**，因为日志里没有 summary/topics/facts，导空壳等于没导）／从游戏存档槽（**自己读 JSON，绝不调 `VNSaveSystem.Load()`**——那个会 `VNFlags.Clear()` 冲掉工程 flag） |
| VNAiTextNormalize | AI 输出的繁体字兜底转简体。提示词已把简体约束放在 system prompt 最后一行仍会漏（三次抽查三次中招），这是玩家直接可见的问题，所以加确定性代码兜底。分工：提示词管「大部分时候对」，兜底管「永远不出错」。作用于台词/选项/日记/摘要/话题 |
| VNAiTalkModule | AI 自由聊天事件模块（event aitalk）。**刻意破一次模块三铁律**——直接驱动舞台立绘换表情，因为自绘立绘要把眨眼/口型/色调匹配/出场动画全部重接一遍；边界收紧为「只碰表情和对话框内容」且正常结束/ESC/CancelForDebug 三条路径都还原原表情。**射线坑**：EventLayer 排序 60 在选项面板 45 之上，模块自绘的一切默认 `raycastTarget=false`，否则吃掉选项点击（唯 ESC 确认框例外）。装机走 Tools → VN Effects → **Install AI Talk Module To Scene** |
| VNQuestDef / VNQuestLog | 任务定义资产 / quest 命令执行 + J 键任务日志（状态全在 flags） |
| VNStatDef / VNStatsHud | 养成属性定义资产（钳制/样式/等级阈值）/ stat 命令 + 顶栏 HUD + C 键属性面板（数值全在 flags，VNFlags.Changed 事件驱动刷新）；属性变动演出 = HUD 就地（数字滚动+条补间+图标弹跳+`+N` 上飘）+ 左上角 VNToast 卡片 |
| VNToast | 左上角堆叠提示卡片（多条排队不覆盖，上限 5）+ 右上角 AUTO/SKIP 角标；`Show(msg)` 中性卡、`Show(msg, icon, iconColor, accent, hold)` 带图标色条 |
| VNShopDef / VNShopModule | 商店定义资产 / 商店事件模块（event shop id:xx，买卖走金钱属性 + 道具_&lt;id&gt; flag） |
| VNPlanDef / VNPlanModule | 日程方案资产 / 周日程排程模块（event plan 排格写 flag 日程_&lt;N&gt;；op:next 逐格派发到 flag 当前行动）；外观走系统主题 planPrefab + VNPlanSkin 槽位，缺失退回程序化 UI |
| VNResultPopupModule | 结算大弹窗事件模块（event result grade:fail\|normal\|good\|great，判定冲条 0→100 悬念演出 → 四档大字+星光爆发）；外观走系统主题 resultPopupPrefab + VNResultPopupSkin 槽位，缺失退回程序化 UI，皮肤没配冲条三槽则直接揭晓 |
| VNInventory | I 键背包：左道具一览 + 右 7 格装备栏 + 介绍区，右键菜单 装备/卸下/使用（文案图标装备数据取自 VNShopDef；外观走系统主题 inventoryPrefab + VNInventorySkin 槽位，缺失退回程序化 UI） |
| VNEquipment | 装备核心（纯静态）：状态全存 flags——装备_&lt;道具id&gt;=部位编号、装备实增_（卸下按实际生效量扣回防钳制不对称）、装备效果_&lt;效果id&gt;（特殊效果合计，生效逻辑由剧本 if 判断） |
| VNCalendarHud | 右下日历 HUD（flag 月份/剩余月数，time 命令驱动；月份 flag 不存在时自动隐藏） |
| VNCgUnlocks / VNCgGallery | CG 全局解锁存储（独立 JSON，与存档槽分离）/ G 键鉴赏画廊（目录取 cgLibrary，解锁取 VNCgUnlocks，group 相同的合并成一格翻差分）；左上角 `CG｜照片` 两标签，照片页 = 大头贴相册（读 VNPhotoAlbum，缩略图走独立不驱逐缓存，全屏可翻页+删除带二次确认）。标签/删除按钮/确认框都是程序化补的，皮肤 prefab 不用重导 |
| VNTitleMenu | 开始菜单（同场景覆盖层 Canvas 500）：开始/继续(最新档含快存)/读档/鉴赏/设置/退出，后四者复用现成面板；Runner 启动时接管 playOnStart，ResumeAt 自动收层；标题文字/背景/BGM 配在 VNGameConfig「标题画面」区 |
| VNSnsView / VNSnsMessage | SNS 手机聊天视图（`sns open` 后台词行渲染成气泡：「我」在右、对方在左）+ 单条消息数据；支持文字/语音/图片/正在输入/已读/限时回复（`sns reply timeout: late:`）；手工测量布局，会话与消息列表进存档，聊天中途可存档 |
| VNLocale / VNScriptLocale | 本地化（中/英/日）：语言管理+UI 字符串表 / 剧本台词翻译查表（表在 Resources/VNLocale/，抽取工具 Tools→VN Effects→Localization） |
| VNAssetUi / VNConfigEntryDrawers (Editor) | 素材界面共用层（缩略图·试听·波形·拖拽·搜索）/ 背景·CG·音频·UI皮肤四个条目的**紧凑单行 drawer**。**Sprite 缩略图不用 `AssetPreview`**——它是异步的，几十张一起等会闪空白；Sprite 自己知道在哪张 texture 的哪个 UV，`DrawTextureWithTexCoords` 同步画即可（texture 不必可读）。音频没这捷径只能异步 + 占位，但**不能无限等**（有些资产永远没预览图），自己给 3 秒窗口到点放弃——`IsLoadingAssetPreview(int)` / `GetInstanceID()` 在 Unity 6.5 是 **error 级弃用**不能用。试听走 `UnityEditor.AudioUtil` 反射（`PlayPreviewClip` / 老版 `PlayClip` 逐个探测，探不到就灰掉按钮）。**drawer 挂在类型上**，所以 VNStage / VNAudio 组件的同名列表也一并变紧凑 |
| VNGameConfigEditor (Editor) | VNGameConfig 的**九页分页 Inspector**（剧本｜标题｜UI皮肤｜舞台｜音频｜玩法｜AI｜大头贴｜全部，选中页进 EditorPrefs）+ 智能列表（搜索·分页 50/页·▲▼✕·id 重复与空值告警·批量拖入自动填文件名当 id）。**页签只登记字段名**，绘制仍走 PropertyField，所以没被认领的新字段会自动落到「其他」页而不是静默消失。用分页而非虚拟化，是因为 Inspector 里拿不到宿主 ScrollView 的可见区域 |
| VNTextureImportDefaults (Editor) | 素材目录里**首次导入**的图自动设 `Sprite (2D and UI)` + `Single`（`OnPreprocessTexture` 在导入前跑，拖进来一次就对，不用二次 reimport）。**白名单目录**（`Roots`，新开素材目录补一行）——**绝不能全项目一刀切**，`Art/Models/**` 下 60+ 张法线/粗糙度贴图按 sRGB 的 Sprite 导入会让光照全错且极难联想到导入设置。只在 `importSettingsMissing` 时设，所以手调过的 Pivot/MaxSize/Multiple 切图永不被打回。**坑**：`importSettingsMissing` ≠「没有 .meta」——meta 存在却缺完整设置块时也为 true（老图中招过 5 张）；而想用「.meta 不存在」卡死新文件**也不行**，Unity 调 preprocessor 前就已写盘。存量补登走 Tools → VN Effects → Textures → Apply Sprite Settings To Selection |
| VNAssetBrowserWindow (Editor) | 素材浏览器（Tools → VN Effects → **Asset Browser**）：左栏九类带条数，图片走大缩略图网格、音频走波形列表（**都做了虚拟化**，只画可见行），底部详情栏改 id/换素材/试听/定位/移除，右键还有「用文件名填 id」。**以缩略图为主、id 为标签**——本项目文件名是 AI 生成的原始 prompt 或纯数字（`1.png`、`masterpiece, very aesthetic… s-1095962266.png`），看名字根本认不出图。「只看未登记」的扫描目录**从已登记条目反推**，不写死（`Assets/CG` 与 `Assets/Art/Images/CG` 并存过） |

### 演示场景

- 重建：菜单 **Tools → VN Effects → Create Demo Scene**（每次加新组件后需重建）
- 全部按键列表见场景内提示文字或 `VNEffectsDemo.UpdateHint()`
- 立绘选择规则：`Assets/Assets` 下文件名含 "solo" 的前两张；背景轮换=其余 ≥900×600 的大图

## 剧本系统（自研轻量 DSL，选型已定）

- **选型结论**：自研 Ren'Py 风格纯文本剧本（Git/AI 协作友好）；Dialogue System 插件保留不用
- 代码在 `Assets/Scripts/VNEffects/Script/`：VNScriptParser → VNScriptRunner → VNStage → 特效 API
- 剧本文件：`Assets/Scenarios/*.vn.txt`；剧本场景：**Tools → VN Effects → Create Script Demo Scene**
- 关键语义：命令默认同步等待，行尾 `@` = 异步；台词行 = 等打字完+玩家推进
- **写剧本 → 技能 vn-write-scenario；加命令 → 技能 vn-new-command；语法详解 → HowToUse.md**

已完成的子系统（详解章节都在 WhatAiDo.md，语法用法在 HowToUse.md）：

| 子系统 | 一句话 | 章节 |
|---|---|---|
| 分支/变量/子程序 | label/jump/flag/if（含逻辑运算）/choice/call/params/return，跨文件 `文件::标签` | 十七、七十三~七十六 |
| 存档/回想/Auto/Skip | F5/F9 20 槽 + 快捷功能条 + H 回想 + A/S；仅台词处可存 | 十九、三十五、五十九 |
| 音频 | 三通道库+基准音量；`bgm/se/voice` 支持 `vol:`，公式=基准×vol×通道 | 四十 |
| 玩法事件接口 | `event <id>` + `* 结果行`；示例 qte/map/battle/shop/plan/result/quiz | 四十一~四十四、七十、八十一 |
| 限时问答 | `event quiz id:题库 count: time: pass: pick:`，题库=VNQuizDef 资产，结果三档 + 成绩 flag | 八十八 |
| 羽毛球对战 | `event badminton vs:角色 id:对手资产 target: first:me\|opponent\|random mode:match\|free`；A/D 移动 + J 击球 + K 扣杀 + ESC 认输（弹确认，退出即判负）；弹道 = 三点定抛物线，**不用 Physics2D**（改纯数学判定，代价是必须子步进）；难度靠 VNBadmintonDef 六参数，轨迹预告 `trackDisplayRate` 是最大杠杆 | 一〇二 |
| 拍大头照 | `event photo vs:角色 [me:主角] [theme:主题] [mode:match\|free] [frame:边框] [time:秒] [stat:属性 rate:换算率]`；左栏边框/贴纸两个标签页 + 右栏我/对方两列表情格 + 贴纸拖拽缩放旋转右键删 + 限时 + 快门倒数闪白 + 相纸飞入冲分结算；照片存 `persistentDataPath/vn_photos/`（与存档槽分离）。**两套结果名互斥**：写了 theme: = 完美/普通/失败，不写 = 完成 | 一〇三 |
| AI 自由聊天 | `event aitalk vs:角色 [persona:人格] [turns:] [topic:] [place:] [me:] [stat:属性 rate:换算率] [flag:前缀]`；接 DeepSeek / Gemini（`VNGameConfig` 一处切换）实时生成台词，一次请求同时拿到 台词+表情+漫符+好感变化+三个候选回复（各带隐藏语气标签），结果 好感提升/普通/冷场/**失败**。**`* 失败` 必须接住**否则玩家断网会静默跳过（Lint 有检查）。event 前要先 `show` 角色，模块只换表情不负责出场。**定位：仅番外/自由时间，主线不依赖**——AI 内容不进翻译表、无配音、玩家可能断网。key 仅本地开发用，发行须改玩家自填或自建中转 | 一〇六 |
| SNS 手机聊天 | `sns open/close/voice/image/typing/read/time/system/reply`；打开后台词行=气泡（「我」在右），不是 event 模块所以中途可存档；Skip/Auto 屏蔽、消息不进回想 | 九十 |
| 液体喷溅 | `liquid splash\|spray\|click\|wet\|dry\|cover [on\|off] [x:] [y:] [type:] [power:] [dir:] [spread:] [rate:] [screen:] [amount:]`，type = water/blood/ink/slime（+中文别名）；x/y 是屏幕比例 0~1；**dir 留空=朝镜头扑面而来（默认，正交相机下走伪透视：放射+加速+放大），填了才侧喷**；screen 是溅上镜头的概率倍率；click 模式下左键归喷水、Enter/空格仍推进 | 九十四 |
| 背景无限滚动 | `bgscroll on\|off [speed:] [dir:] [mode:repeat\|mirror] [time:]`，speed = 画布像素/秒，dir 是画面流向（默认 left），mirror 不挑图但看得出对称 | 一一八 |
| 飘落天气 | `weather <id> [density:] [wind:] [speed:] [size:]`，id = petals/maple/ginkgo/leaves/bamboo（+中文别名）或 Rain/Snow/Fireflies/None；参数资产 VNWeatherDef 登记进 VNGameConfig，调参走 Tools → VN Effects → **Weather Preview** | 九十二 |
| 任务 | `quest start\|stage\|done\|fail`，状态=flag `任务_<id>`，J 键日志 | 四十三 |
| CG + 画廊 | `cg <id>`，素材 `Assets/CG/` 文件名=id；解锁走 VNCgUnlocks 全局 JSON；G 键画廊 | 五十六、七十八 |
| 养成 | `stat`（钳制+飘字）、选项 `if:`/`cost:`、商店、`time` 日程+日历 HUD | 六十三~六十六 |
| 装备 | I 键背包 7 部位装备栏；VNShopDef.Item 加装备/使用字段；状态全在 flags（装备_/装备实增_/装备效果_），特殊效果由剧本 if 判断生效 | 八十五 |
| 周日程排程 | `event plan` 排格/派发 + `flag rand:` + `event result` 结算；概率表写剧本 | 七十 |
| 本地化 | 剧本只写中文，翻译旁路表 + Extract/Validate → 技能 vn-localize | 五十七 |
| UI 皮肤 | `ui dialogue\|choice <id>`（进存档）+ 系统菜单全局主题（不进存档）→ 技能 vn-ui-skin；无框渐变三套 id = 白渐变/粉渐变/黑渐变；`ui name <样式>` 换名字装饰（十套内置预设，不用登记） | 八十二、八十三、一一五、一一六 |
| 标题菜单 | VNTitleMenu 同场景覆盖层，配置在 VNGameConfig「标题画面」区 | 八十 |
| 静态校验器 | Tools → VN Effects → Lint Scenarios（Ctrl+Shift+L），检查项全表见 HowToUse 十二·五 | 七十九 |

- **路线图**：下一步 P3 台词内嵌演出标记 `{shake}{w:0.5}` + VNDirector 名场面命令；
  已知技术债清单见 ProjectCodeGuide 第十二节

## 剧本可视化编辑器

- 菜单：**Tools → VN Effects → Scenario Editor**；核心文件：
  `Editor/VNScenarioEditorWindow.cs`、`VNScenarioDoc.cs`、`VNScenarioSchema.cs`。
- 文本是唯一真相：`.vn.txt ↔ VNScenarioDoc.rows`，保存时重新生成文本，注释/空行保留。
- 支持「▶ 从选中行播放」（默认重建前置状态）调试；入口
  `VNScriptRunner.PlayFromSourceLine(source, line, rebuildState)`。
- **热重载调试**：Play Mode 中播放按钮不禁用，直接用内存文本原地重跑，
  不退出 Play Mode / 不触发域重载；播放前静默自动保存；当前行高亮跟随（10Hz 轮询
  `runner.CurrentLine`）；工具栏播放控制条（暂停/单步/重播本行/上一条，命令级暂停
  不冻结画面动画）。窗口状态跨域重载存活走 `ISerializationCallbackReceiver`，
  **加新窗口状态必须同时改 `OnBeforeSerialize` 和 `OnEnable`**。详见 WhatAiDo 九十六章。
- 工具栏「隐注释/空行」：把空行与 `#` 注释折成零高度（`RowHeight` 返回 0，索引不变，
  所有编辑操作零影响）。**只隐空行与 `#`**——孤儿 `*` / `>` 行也是 Raw，藏了就找不回来。
- **camseq 路径点行是字段化的**（类型/目标/zoom/秒/ease/xfade/hold/震；类型多一项「原地」= `stay`，
  沿用上一个点、画布上不画它的取景框），解析不了的**退回纯文本并标黄**；
  header 行右侧三个按钮：`编排`（打开镜头编排窗口并**双向绑定**这一行）/ `预设▾`（内置模板·我的预设·存为预设）/ `+ wp`。
  绑定后镜头窗口可「跟随选中」自动切行、支持实时或手动回写；存储仍是 `camLines` 字符串。
  镜头窗口画布的**底图三级回退**（手动指定 → 绑定行推算出的背景/CG → 场景当前那张），
  并按推算站位画**真实立绘**（可开关）——数据源是行左侧「舞台一览」同一套 `TryGetRowStage`。
  画布两种模式：`整图`（全景+取景框，可拖点）/ `镜头视角`（直接显示镜头里的画面，
  拖进度条=运镜动画，只读）——两者共用一套绘制，差别只是 `ViewPoint` 那一层坐标变换。
  `场景预览` 会把绑定行的背景/立绘**摆进场景**让 Game 视图也对（带 URP 后处理），
  临时立绘一律 `HideFlags.DontSave`（绝不写进场景文件、域重载自动销毁），关掉全部还原。
  **路径点行禁用 `CharacterPopup` / `SpritePopup`**——那套是异步回调、会把值写进 `VNRow.values`，
  和 camLines 是两条路径，必须用同步的 `PopupString` / `EditorGUI.Popup`。详见 WhatAiDo 九十八章。
  `辅助线 ▾` 逐项勾选三分线/中心十字/安全区/对话框遮挡区（存 EditorPrefs）：
  **整图模式画在选中路径点的取景框内**（对话框不随镜头缩放，遮挡区只能按占一屏的比例落位），
  镜头视角模式铺满画布；遮挡区尺寸实测 `VNStage.dialogue`，量不到才退回默认布局。
  撤销是**窗口内独立栈**（快照 = `GenerateText()` 文本），Ctrl+Z/Ctrl+Y 走 ShortcutManager
  窗口作用域，**不挂 Unity 全局 Undo**；换绑定行清空历史。详见 WhatAiDo 一〇一章。
- **打字搜索**（`VNCommandSearch.cs`，与分类菜单并存）：行首命令按钮**右键** = 打字换命令
  （左键仍是分类菜单）、底部 `+` 加行、各参数格下拉都换成可搜列表；`Ctrl+E` 开命令面板
  （向导式：选命令 → 逐个问位置参数 → 可选参数菜单循环 → Enter 插入 / Shift+Enter 插上方 /
  Tab 跳过 / Esc 取消）。候选表从 Schema 现场生成，加新命令自动出现。**匹配只做子串包含**
  （中英皆可，不做模糊/拼音）。详见 WhatAiDo 一一一章。
- 快捷键：`Enter` 在选中行下方插入空台词行（自动聚焦输入框）、`Shift+Enter` 插在上方；
  文本框编辑中的第一下 Enter 只结束编辑。插命令行走列表底部 `+` 下拉或 `Ctrl+E` 面板。
  调试键位 `F5` 播放选中行 / `F6` 重播上次那行 / `F8` 暂停 / `F10` 单步 / `Ctrl+S` 保存
  （走 ShortcutManager，可在 Edit → Shortcuts 改），另有 `Ctrl+Enter` / `Ctrl+Shift+Enter`。
- **改编辑器前必读技能 vn-editor-extend**（say 专用字段、行号换算、Bridge 时序等硬规则都在里面）；
  调试能力边界见技能 vn-debug。完整记录见 WhatAiDo.md 三十一/三十二章。
