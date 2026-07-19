# CLAUDE.md — 视觉小说项目（vnovelProject）

> 本文件是给 Claude（AI 助手）的项目说明书。所有开发过程的详细记录在 `WhatAiDo.md`；
> 逐脚本的代码指南（职责/用法/扩展/维护）在 `ProjectCodeGuide.md`，改代码前先查它；
> 从空场景手动搭建舞台的完整教程（含层级/排序/参数依据）在 `SetUpGuide.md`；
> 剧本写法教程在 `HowToUse.md`。
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
| VNEntranceAnimator | 出场预设×6(溶解辉光/滑入/弹出/扫光/爆闪/残影冲入) + 退场×2 + StartIdleEffects |
| VNGlowBackdrop / VNFootShadow | 背后光环脉动 / 脚下椭圆影（悬浮联动） |
| VNCharacterEmotes | 情绪动作：惊讶/生气/害羞/沮丧(+Recover)/点头/摇头 |
| VNAmbientParticles | 粒子预设×8：尘埃/星光/光斑/花瓣/雨(+溅落)/雪/萤火虫/雾 + PlaySparkleBurst |
| VNWeatherController | 天气切换 + 调色联动 |
| VNMoodGrading | 八种情绪色调（双 Volume 权重交叉过渡，含 Dream 梦境） |
| VNScreenTransition | 全屏转场×8：噪声溶解/百叶窗/瓦片/圆扩散/水墨/爆闪/光斑/眨眼 |
| VNCamera / VNScreenShake / VNDutchAngle / VNHeartbeat | 运镜×5 / 三级震动 / 荷兰角 / 心跳脉动 |
| VNGodRays / VNEdgeGlow / VNCloudShadows / VNHeatHaze / VNFakeDoF | 光束/情绪泛光/云影/热浪+雾/伪景深 |
| VNSpeedLines | 漫画速度线/集中线 overlay（3 变体贴图闪帧，fx speedlines on/off/burst） |
| VNScreenShockwave | 全屏情绪水波（fx shockwave [light\|heavy]：波峰环 overlay + 背景波浪脉冲 + 轻震动） |
| VNRetroFilter | 胶片/CRT 复古滤镜（fx filmgrain/crt；mood Memory 自动胶片、Dream 自动 CRT） |
| VNKenBurns | 背景 Ken Burns 漂移（60~90s 随机航点缓慢缩放+平移，默认开启永不静止，fx kenburns on/off） |
| VNLetterbox | 电影黑边上下滑入（letterbox on/off [height:][time:]，mood Memory 回忆自动联动） |
| VNShootingStars / VNDriftingClouds | 夜晚偶发流星（fx meteor）/ 云本体缓移（fx skycloud，与云影互补） |
| VNParallax / VNMouseStardust / VNClickRipple | 鼠标视差 / 星尘拖尾 / 点击涟漪 |
| VNSpeakerHighlight / VNToneMatch | 说话者高亮 / 立绘色调匹配背景 |
| VNDialogueBox + VNTypewriterText | 对话框（流光边框/名牌/箭头）+ 打字机逐字上浮（TMP textInfo 顶点动画）；支持皮肤 prefab（VNDialogueSkin 槽位绑定，程序化默认兜底） |
| VNDialogueSkin / VNChoiceSkin | UI 皮肤槽位声明组件（挂 prefab 根）：全槽位可选留空降级；剧本 `ui dialogue\|choice <id\|default>` 切换，id 在 VNGameConfig 的 UI 皮肤区登记；起步模板 Tools → VN Effects → UI Skins → Export Skin Prefabs（烘焙贴图+生成默认/顶部/右列样例并自动登记）；皮肤状态进存档 |
| VNSystemUiSkinSet / VNSystemUiSkinBehaviour | 系统菜单唯一全局 prefab 主题及安全实例化基类；标题/设置/CG/Backlog/快捷条/存读档/顶部属性 HUD/完整属性页分别使用槽位组件，单项缺失或槽位无效时只退回该项程序化 UI；默认模板菜单 Tools → VN Effects → System UI Skins → Export Default Prefabs（详见八十三章） |
| VNFont / VNFontAssetBuilder | TMP 中文字体统一入口（三级兜底+Prewarm）/ 预烘焙字体资产生成器 |
| VNChoicePanel | 选项演出（飞入/悬停扫光/落选溶解），需 EventSystem |
| VNSakuraBurst | 樱吹雪告白组合技 |
| VNCharacterBlink / VNCharacterMouth | 默认表情自动眨眼 / 说话口型（透明画布叠加层） |
| VNEventModule / VNEventRegistry | 玩法事件接口：模块基类 + id→模板注册表（EventLayer 排序 60） |
| VNQteModule / VNMapModule | 事件示例模块：QTE 连打条 / 地图选地点（条件显隐+去过标记） |
| VNBattleModule | 回合制小战斗（event battle，结果 胜利/失败/逃跑；patkstat/phpstat/pdefstat 从 flag 读属性=养成联动，结束写 flag 战斗剩余HP 供车轮战） |
| VNQuestDef / VNQuestLog | 任务定义资产 / quest 命令执行 + J 键任务日志（状态全在 flags） |
| VNStatDef / VNStatsHud | 养成属性定义资产（钳制/样式/等级阈值）/ stat 命令 + 顶栏 HUD + C 键属性面板（数值全在 flags，VNFlags.Changed 事件驱动刷新） |
| VNShopDef / VNShopModule | 商店定义资产 / 商店事件模块（event shop id:xx，买卖走金钱属性 + 道具_&lt;id&gt; flag） |
| VNPlanDef / VNPlanModule | 日程方案资产 / 周日程排程模块（event plan 排格写 flag 日程_&lt;N&gt;；op:next 逐格派发到 flag 当前行动） |
| VNResultPopupModule | 结算大弹窗事件模块（event result grade:fail\|normal\|good\|great，四档大字+星光爆发） |
| VNInventory | I 键物品栏（flags 反查道具，文案图标取自 VNShopDef） |
| VNCalendarHud | 右下日历 HUD（flag 月份/剩余月数，time 命令驱动；月份 flag 不存在时自动隐藏） |
| VNCgUnlocks / VNCgGallery | CG 全局解锁存储（独立 JSON，与存档槽分离）/ G 键鉴赏画廊（目录取 cgLibrary，解锁取 VNCgUnlocks，group 相同的合并成一格翻差分） |
| VNTitleMenu | 开始菜单（同场景覆盖层 Canvas 500）：开始/继续(最新档含快存)/读档/鉴赏/设置/退出，后四者复用现成面板；Runner 启动时接管 playOnStart，ResumeAt 自动收层；标题文字/背景/BGM 配在 VNGameConfig「标题画面」区 |
| VNLocale / VNScriptLocale | 本地化（中/英/日）：语言管理+UI 字符串表 / 剧本台词翻译查表（表在 Resources/VNLocale/，抽取工具 Tools→VN Effects→Localization） |

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
| 玩法事件接口 | `event <id>` + `* 结果行`；示例 qte/map/battle/shop/plan/result | 四十一~四十四、七十、八十一 |
| 任务 | `quest start\|stage\|done\|fail`，状态=flag `任务_<id>`，J 键日志 | 四十三 |
| CG + 画廊 | `cg <id>`，素材 `Assets/CG/` 文件名=id；解锁走 VNCgUnlocks 全局 JSON；G 键画廊 | 五十六、七十八 |
| 养成 | `stat`（钳制+飘字）、选项 `if:`/`cost:`、商店、`time` 日程+日历 HUD | 六十三~六十六 |
| 周日程排程 | `event plan` 排格/派发 + `flag rand:` + `event result` 结算；概率表写剧本 | 七十 |
| 本地化 | 剧本只写中文，翻译旁路表 + Extract/Validate → 技能 vn-localize | 五十七 |
| UI 皮肤 | `ui dialogue\|choice <id>`（进存档）+ 系统菜单全局主题（不进存档）→ 技能 vn-ui-skin | 八十二、八十三 |
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
- **改编辑器前必读技能 vn-editor-extend**（say 专用字段、行号换算、Bridge 时序等硬规则都在里面）；
  调试能力边界见技能 vn-debug。完整记录见 WhatAiDo.md 三十一/三十二章。
