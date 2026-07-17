# SetUpGuide.md — 从零手动搭建 VN 场景完全指南

> 本指南假设你的 Hierarchy **完全是空的**，从第一个物体开始手把手搭出完整的
> 剧本演示场景（VNScriptDemo 同款）。每一步都附**为什么要这么做**的原理说明。
>
> ⚠️ 实际上菜单 **Tools → VN Effects → Create Script Demo Scene** 会自动完成本指南
> 的全部步骤。手动搭建的价值在于：① 彻底理解系统结构；② 想自定义层级/参数时知道
> 哪里能改、哪里不能动。照着做一遍，你对整个特效系统的理解会完全不同。

---

## 目录

- [第 0 章 开工前的三个核心概念](#第-0-章-开工前的三个核心概念)
- [第 1 章 项目级准备（只做一次）](#第-1-章-项目级准备只做一次)
- [第 2 章 相机与全局后处理](#第-2-章-相机与全局后处理)
- [第 3 章 Canvas 与画面容器层级](#第-3-章-canvas-与画面容器层级)
- [第 4 章 背景与舞台内容](#第-4-章-背景与舞台内容)
- [第 5 章 全屏 Overlay 层（排序表）](#第-5-章-全屏-overlay-层排序表)
- [第 6 章 场外管理器与粒子](#第-6-章-场外管理器与粒子)
- [第 7 章 对话框 / 提示 / EventSystem](#第-7-章-对话框--提示--eventsystem)
- [第 8 章 剧本系统接线（VNStage 及周边）](#第-8-章-剧本系统接线vnstage-及周边)
- [第 9 章 运行验证清单与常见坑](#第-9-章-运行验证清单与常见坑)
- [附录 A 键盘演示场景的差异](#附录-a-键盘演示场景的差异)
- [附录 B 完整层级树速查](#附录-b-完整层级树速查)

---

## 第 0 章 开工前的三个核心概念

在放第一个物体之前，先理解三条贯穿全部搭建决策的底层逻辑：

### 0.1 发光 = HDR 颜色(>1) + Bloom 后处理

本项目所有"发光"效果（扫光、轮廓光、波峰环、星光粒子……）的原理都是同一条：
**把颜色值写到 1 以上（HDR），再让 Bloom 后处理把超过阈值 1.0 的部分晕开成光**。

这决定了两件事：

1. **相机必须开启后处理**，Volume 里必须有 Bloom（阈值 1.0）——否则所有发光效果
   都会变成一块平淡的亮色贴图；
2. **Canvas 必须是 Screen Space - Camera 模式**——Overlay 模式的 UI 是在后处理
   *之后*直接画到屏幕上的，Bloom 根本碰不到它，全部发光失效。这是本项目最容易
   踩的一号坑。

另外 uGUI 的顶点色（Image.color）会被钳制到 1，**HDR 颜色必须走材质属性**——
这就是为什么每个特效组件都有自己的材质而不是改 Image.color。

### 0.2 每种整屏运动独占一个容器层

画面会被多种效果同时驱动：震动（位置）、心跳（缩放）、运镜（缩放+平移）、
荷兰角（旋转）。如果它们都去 tween 同一个 Transform，**后启动的 tween 会覆盖
先启动的**（DOTween 对同一属性的补间互斥），效果互相打架。

解法是嵌套容器，每层只管一种运动，矩阵天然相乘：

```
SceneRoot   ← 震动(VNScreenShake 改位置) + 心跳(VNHeartbeat 改缩放)
└─ ZoomRoot   ← 运镜(VNCamera 改缩放/平移)
   └─ TiltRoot  ← 荷兰角(VNDutchAngle 改旋转+防露角放大)
      └─ 三个视差层(VNParallax 各自小幅位移)
```

想同时"边震动边推近边斜角"？三层各动各的，互不干扰。**搭错嵌套顺序或者把
组件指到错误的层，就会出现"开了运镜后震动失效"这类诡异 bug。**

### 0.3 排序全靠嵌套 Canvas 的 sortingOrder

UI 不写深度缓冲，粒子系统又不属于 uGUI，两套东西要正确地"谁盖住谁"，
统一用**渲染排序号**解决：全屏覆盖类物体各自挂一个 `Canvas` 组件并勾
`Override Sorting`，粒子则设置 Renderer 的 `sortingOrder`。数字大的盖住小的。
完整排序表见第 5 章——**新加全屏效果时先查表再选号**，是本项目的固定动作。

---

## 第 1 章 项目级准备（只做一次）

这些是资产/设置层面的准备，与场景无关，做过一次后新场景直接复用。

### 1.1 确认三个包/插件

| 依赖 | 位置 | 说明 |
|---|---|---|
| URP 17 | Package Manager | 项目已配好 `PC_RPAsset` / `Mobile_RPAsset` 两套管线资产（Project Settings → Graphics）。后处理（Bloom/Vignette）依赖它 |
| Input System（新版） | Package Manager | 代码全部用 `Keyboard.current` / `Mouse.current`。Project Settings → Player → Active Input Handling 需为 **Input System Package** 或 Both。**禁止**旧版 `Input.` API |
| DOTween | `Assets/Plugins/Demigiant` | 所有代码补间的基础。首次导入要跑一次 Tools → Demigiant → DOTween Utility Panel → Setup |

### 1.2 图片素材导入设置

把立绘/背景图放进 `Assets/Assets/`。**每张图**的 Import Settings 必须是：

| 设置项 | 值 | 为什么 |
|---|---|---|
| Texture Type | Sprite (2D and UI) | uGUI Image 只吃 Sprite |
| Sprite Mode | Single | 单图模式 |
| Mesh Type | **Full Rect** | ★ 最重要。默认 Tight 模式会把透明区裁掉生成异形网格，导致溶解/扫光等按 UV 计算的 shader 效果扭曲不均匀。全矩形网格 UV 才是完整的 0~1 |
| Generate Mip Maps | 关 | 2D 游戏不需要，还会让缩放时发糊 |
| Alpha Is Transparency | 开 | 防止透明边缘出现黑边/白边 |

> 生成器的立绘选择约定：文件名含 "solo" 的前两张 = 立绘 A/B；
> 其余尺寸 ≥900×600 的大图 = 背景轮换池（小图会被当成 UI 素材过滤掉）。
> 手动搭建时你自己指定即可，不受此约定限制。

### 1.3 材质资产（3 个）

在 `Assets/VNEffects/Materials/` 下创建三个材质（右键 → Create → Material，
然后在 Inspector 顶部换 Shader）：

| 材质文件 | Shader | 用途 |
|---|---|---|
| VNImageEffect.mat | `VN/ImageEffect` | 立绘/背景的单图特效（溶解/扫光/发光/波浪…） |
| VNAdditive.mat | `VN/Additive` | 一切加法发光体：粒子、光束、光环、速度线… |
| VNScreenTransition.mat | `VN/ScreenTransition` | 全屏转场 |

**为什么要材质资产？** 其实所有组件在留空时都会运行时 `new Material(Shader.Find(...))`
自建材质，功能完全一样。给一份资产引用（拖到组件的 `sourceMaterial` 字段）有两个
好处：① 构建打包时 Shader 一定会被收录（`Shader.Find` 依赖 shader 被引用，否则
打包后可能找不到而变粉）；② 想统一调默认参数时有一个落点。

**注意**：组件拿到 `sourceMaterial` 后都是 `new Material(source)` 复制一份实例再用，
**绝不会**直接改资产本身——这保证多个立绘各有独立材质、特效互不串扰
（`VNImageEffectController` 自动管理这套实例化）。

### 1.4 后处理 Volume Profile 资产

创建 `Assets/VNEffects/VNEffectsVolumeProfile.asset`
（右键 → Create → Volume Profile），添加两个 Override：

**Bloom**：
| 参数 | 值 | 为什么 |
|---|---|---|
| Threshold | **1.0** | ★ 系统契约值。颜色 ≤1 的普通画面完全不晕光，只有 HDR(>1) 的部分发光。改低了整个画面会泛白 |
| Intensity | 1.4 | 光的整体强度，口味参数 |
| Scatter | 0.7 | 光晕扩散范围，偏大让光更"柔" |

**Vignette**：
| 参数 | 值 | 为什么 |
|---|---|---|
| Intensity | 0.22 | 常驻的轻微四角压暗，让画面聚焦中央（电影感的地基） |
| Smoothness | 0.45 | 过渡柔和 |

> 这份 profile 是"基础层"。`VNMoodGrading`（情绪调色）运行时会另建两个全局
> Volume 以更高 priority 叠在上面做交叉过渡，不会动这份资产。
> `VNVignetteFocus` 则是直接补间这份 profile 里的 Vignette 参数做"聚焦渐晕"。

---

## 第 2 章 相机与全局后处理

### 2.1 Main Camera

新建空场景后，创建 GameObject `Main Camera`（Tag 设为 **MainCamera**，
有代码用 `Camera.main` 找它），挂 `Camera` 组件：

| 设置 | 值 | 为什么 |
|---|---|---|
| Projection | Orthographic | 2D 游戏，无透视 |
| Size | 5 | 正交半高。配合 planeDistance 10 的 Canvas 换算出舞台大小 |
| Position | (0, 0, **-10**) | 退后 10 个单位看向原点，给 Canvas 的 planeDistance 留距离 |
| Clear Flags | Solid Color | 纯色清屏 |
| Background | 深蓝黑 (0.03, 0.03, 0.06) | 转场露底时不刺眼 |
| Rendering → Post Processing | **勾上** | ★ 不勾则 Bloom/Vignette/调色全部无效。这是"发光全没了"的第一排查点 |

### 2.2 Global Volume

创建空物体 `Global Volume`，挂 `Volume` 组件：

- **Is Global：勾上**（全屏生效，不需要碰撞盒）；
- Profile：拖入 1.4 节创建的 `VNEffectsVolumeProfile.asset`。

之后（见 6.9）`VNVignetteFocus` 也挂在这个物体上并引用此 Volume。

---

## 第 3 章 Canvas 与画面容器层级

### 3.1 Canvas

创建 `Canvas`（挂 `Canvas` + `CanvasScaler` + `GraphicRaycaster`）：

| 设置 | 值 | 为什么 |
|---|---|---|
| Render Mode | **Screen Space - Camera** | ★ 见 0.1：Overlay 模式的 UI 不经过后处理，全部发光效果失效。必须走相机 |
| Render Camera | Main Camera | |
| Plane Distance | 10 | Canvas 平面放在相机前方 10 单位（正好在世界原点） |
| Sorting Order | 0 | 基准排序，覆盖层都比它大 |

`CanvasScaler`：

| 设置 | 值 | 为什么 |
|---|---|---|
| UI Scale Mode | Scale With Screen Size | 分辨率无关 |
| Reference Resolution | 1920 × 1080 | 全项目坐标基准：代码里所有像素值（立绘位置、平移幅度…）都按这个分辨率写 |
| Match | 0.5 | 宽高各占一半权重，非 16:9 屏幕上均衡缩放 |

### 3.2 容器层级（本指南最重要的一步）

在 Canvas 下依次创建**六个空 RectTransform**，全部设为"全拉伸"：
anchorMin=(0,0)、anchorMax=(1,1)、offsetMin=offsetMax=(0,0)。
嵌套关系必须完全一致：

```
Canvas
└─ SceneRoot
   └─ ZoomRoot
      └─ TiltRoot
         ├─ LayerBack    （背景层：背景图、云影、云本体、流星）
         ├─ LayerMid     （中景层：GodRays 光束）
         └─ LayerFront   （前景层：立绘。VNStage 靠名字找它，不能改名）
```

各层职责（rationale 见 0.2）：

| 容器 | 被谁驱动 | 动什么 |
|---|---|---|
| SceneRoot | VNScreenShake / VNHeartbeat | 位置震动 / 缩放脉动 |
| ZoomRoot | VNCamera | 运镜缩放、平移（pushin/snapzoom/pan/dolly/camcut/camto/camseq） |
| TiltRoot | VNDutchAngle | 荷兰角旋转 + 防露角的补偿放大 |
| LayerBack/Mid/Front | VNParallax | 鼠标视差，三层强度 8 / 13 / 19px（越前景动得越多，产生纵深错觉） |

> ⚠️ **命名警告**：`LayerFront` 和后面的 `Background` 这两个名字被
> `VNStage.AutoWire()` 用 `GameObject.Find` 按名字查找（旧场景自愈用），
> 手动搭建时保持原名可以少连两根引用；改名则必须手动把
> `VNStage.characterLayer` / `backgroundImage` 拖好。

---

## 第 4 章 背景与舞台内容

### 4.1 背景 Image

在 `LayerBack` 下创建 `Background`（RectTransform + CanvasRenderer + Image）：

- 锚点全拉伸，但 **offsetMin=(-60,-60)、offsetMax=(60,60)**——四边各溢出
  60px。**为什么**：Ken Burns 漂移（缩放 1.06 + 平移 40px）和视差（±8px）都会
  移动背景，四边留余量才不会在运动中露出画面外的底色；
- `Image.sprite` = 你的背景图；`raycastTarget` 关掉（背景不需要接收点击，
  省一次射线检测）。

再给它挂两个组件：

1. **`VNImageEffectController`**：单图特效总控。`sourceMaterial` 拖
   `VNImageEffect.mat`。背景的溶解换图、色调匹配、热浪扭曲、水面波光、
   全屏水波的扭曲脉冲都要通过它；
2. **`VNKenBurns`**：背景 Ken Burns 漂移（默认参数即可：缩放 1.0~1.06、
   平移 40px、单段 30~45 秒）。挂上即自动开始，画面从此永不静止。
   **为什么挂在背景上而不是 ZoomRoot**：ZoomRoot 是运镜的领地，两者抢同一节点
   会互相覆盖补间；背景四边的 60px 余量正是为它预留的。

### 4.2 GodRays（中景光束）

在 `LayerMid` 下创建全拉伸空物体 `GodRays`，挂 `VNGodRays`，
`sourceMaterial` 拖 `VNAdditive.mat`。

**为什么在中景**：光束要压在背景之上、立绘之下，才有"光从窗外照进来、
人物站在光里"的层次。

### 4.3 云影 / 云本体 / 流星（背景层点缀）

都在 `LayerBack` 下创建全拉伸空物体并挂组件：

| 物体名 | 组件 | 说明 |
|---|---|---|
| CloudShadows | `VNCloudShadows` | 地面云影缓移（fx clouds） |
| DriftingClouds | `VNDriftingClouds` | 天上的云本体缓移（fx skycloud）。与云影独立开关：白天有影无云、夜晚有云无影都是合理演出 |
| ShootingStars | `VNShootingStars`（sourceMaterial = VNAdditive.mat） | 夜晚偶发流星（fx meteor） |

### 4.4 立绘（手动摆 vs 剧本生成）

**剧本场景不需要手动摆立绘**——`VNStage` 会在 `show 角色` 命令时运行时生成完整
立绘（自动挂 VNImageEffectController + VNGlowBackdrop + VNEntranceAnimator +
VNFootShadow + 眨眼 + 口型），放进 `characterLayer`（= LayerFront）。

如果你在做键盘演示式的静态场景想手动摆一个，配方是：

1. LayerFront 下创建 Image，锚点居中，`preserveAspect` 勾上；
2. 高度定 880px（双人）或 980px（单人），宽度 = 高度 × 图片宽高比；
3. 位置：单人 (0,-40)，双人 (-380,-60) / (380,-60)；
4. 依次挂：`VNImageEffectController`（VNImageEffect.mat）→
   `VNGlowBackdrop`（VNAdditive.mat）→ `VNEntranceAnimator` → `VNFootShadow`。
   顺序无所谓，但材质引用别拖错。

---

## 第 5 章 全屏 Overlay 层（排序表）

以下物体全部直接放在 **Canvas 下**（不进 SceneRoot！），各自是全拉伸空
RectTransform + 一个组件。组件的 `Awake()` 会自动补建子物体、嵌套 Canvas 和
材质，你只需要建空物体挂脚本。

**为什么不进 SceneRoot**：这些是"贴在镜头上"的效果（速度线、黑边、滤镜…），
镜头震动/缩放时它们不应该跟着动——所以必须在容器层级之外。

★ **全项目渲染排序总表**（新加全屏效果前必查）：

| sortingOrder | 物体 | 组件 | 说明 |
|---|---|---|---|
| 0 | Canvas 本体 | — | 舞台内容（背景/立绘） |
| 10~12 | 氛围粒子 | VNAmbientParticles | 光斑 10 / 尘埃 11 / 星光 12 |
| 20 | EdgeGlow | VNEdgeGlow | 屏幕边缘情绪泛光 |
| 25 | SpeedLines | VNSpeedLines | 漫画速度线 |
| **26** | ScreenShockwave | VNScreenShockwave | 全屏情绪水波 |
| ~30 | 星尘/点击涟漪 | VNMouseStardust / VNClickRipple | 鼠标反馈粒子（31） |
| **34** | RetroFilter | VNRetroFilter | 胶片/CRT 滤镜（要盖住画面但别盖对话框） |
| 35 | Letterbox | VNLetterbox | 电影黑边 |
| 40 | DialogueBox | VNDialogueBox | 对话框（含快捷功能条） |
| 45 | ChoicePanel | VNChoicePanel | 选项面板 |
| 60 | EventLayer | VNEventModule 系 | 玩法事件模块（QTE/地图） |
| 100 | ScreenTransition | VNScreenTransition | 全屏转场，盖住一切 |

按表创建这些 Canvas 直属物体：

| 物体名 | 挂载组件 | 需要设置的字段 |
|---|---|---|
| EdgeGlow | `VNEdgeGlow` | sourceMaterial = VNAdditive.mat |
| SpeedLines | `VNSpeedLines` | sourceMaterial = VNAdditive.mat |
| ScreenShockwave | `VNScreenShockwave` | targets = [背景的 VNImageEffectController]（水波的扭曲脉冲只作用背景，不扭立绘的脸）；screenShake = 第 6 章的 VNScreenShake（受击联动轻震动） |
| RetroFilter | `VNRetroFilter` | 无需设置（默认参数即可） |
| Letterbox | `VNLetterbox` | 无需设置 |
| ScreenTransition | `VNScreenTransition` | sourceMaterial = VNScreenTransition.mat |
| ChoicePanel | `VNChoicePanel` | 无需设置 |
| CameraFade | `VNCameraFade` | 无需设置（camseq 的 fade 用；之后拖给 VNCamera.cameraFade） |

> 转场层为什么是 100：转场的语义是"盖住一切再揭示新画面"，包括粒子和黑边；
> 它还带一个透明 raycast blocker，转场期间拦截所有点击防止玩家在半黑屏时推进。

---

## 第 6 章 场外管理器与粒子

以下物体放在**场景根级**（Canvas 外面）。它们分两类：纯逻辑管理器（空物体+
脚本，通过引用去驱动 Canvas 里的容器）和粒子系统（世界空间渲染，用
sortingOrder 与 UI 排序）。

### 6.1 纯逻辑管理器

| 物体名 | 组件 | 必须连的引用 | 为什么 |
|---|---|---|---|
| ScreenShake | `VNScreenShake` | target = **SceneRoot** | 只震最外层，见 0.2 |
| Parallax | `VNParallax` | layers 加三条：LayerBack/8、LayerMid/13、LayerFront/19 | 前景动得多才有纵深 |
| DutchAngle | `VNDutchAngle` | target = **TiltRoot** | 独占旋转层 |
| VNCamera | `VNCamera` | target = **ZoomRoot**；cameraFade = 第 5 章的 CameraFade | 独占运镜层 |
| Heartbeat | `VNHeartbeat` | target = **SceneRoot**；edgeGlow = EdgeGlow | 心跳缩放 + 红色泛光联动 |
| WeatherController | `VNWeatherController` | additiveMaterial = VNAdditive.mat；moodTargets = [背景 fx]（有常驻立绘可一并加入） | 天气切换时给画面做调色联动 |
| MoodGrading | `VNMoodGrading` | 无 | 运行时自建双 Volume 做情绪调色交叉过渡 |
| FakeDoF | `VNFakeDoF` | backgroundFx = 背景 fx；backLayer = LayerBack | UI 没有深度缓冲做不了真景深，伪景深=模糊背景+微缩放背景层 |
| ToneMatch | `VNToneMatch` | characters 可空（剧本场景运行时自动登记） | 立绘色调匹配背景 |
| SakuraBurst | `VNSakuraBurst` | additiveMaterial = VNAdditive.mat；heartbeat = Heartbeat | 樱吹雪告白组合技 |
| SpeakerHighlight | `VNSpeakerHighlight` | characters 可空（运行时登记） | 说话者提亮、非说话者压暗 |
| HeatHaze | `VNHeatHaze` | targets = [背景 fx]；additiveMaterial = VNAdditive.mat | 热浪扭曲+蒸汽雾 |

另外把 **`VNVignetteFocus`** 挂到第 2 章的 `Global Volume` 物体上，
`volume` 字段指向该 Volume——它要补间基础 profile 的 Vignette 做聚焦渐晕。

### 6.2 粒子系统物体

粒子物体挂 `ParticleSystem` + 对应脚本（脚本 Awake 会重新配置粒子参数，
你不用手调粒子模块）。位置放 z=-1（在 Canvas 平面之前）：

| 物体名 | 组件 | 设置 |
|---|---|---|
| Ambient_Dust | `VNAmbientParticles` | preset=Dust，tint=(1,0.97,0.88)，sortingOrder=11，sourceMaterial=VNAdditive.mat |
| Ambient_Sparkles | `VNAmbientParticles` | preset=Sparkles，tint=(1,0.93,0.65)，sortingOrder=12，同上 |
| Ambient_Orbs | `VNAmbientParticles` | preset=Orbs，tint=(0.75,0.85,1)，sortingOrder=10，同上 |
| ClickRipple | `VNClickRipple` | sourceMaterial=VNAdditive.mat（点击涟漪+星光） |
| MouseStardust | `VNMouseStardust` | sourceMaterial=VNAdditive.mat（鼠标拖尾） |

**为什么粒子在 Canvas 外**：ParticleSystem 不是 uGUI 元素，放进 Canvas 也不受
its 排序管理；它们靠自己 Renderer 的 sortingOrder 参与全局排序（见第 5 章总表）。

> 运行时创建带 Awake 配置的组件（如天气粒子）用的是"先 SetActive(false)
> 挂组件赋值再激活"的模式（见 `VNAmbientParticles.Create`）——手动搭建不用管，
> 但自己写新粒子组件时要遵守。

---

## 第 7 章 对话框 / 提示 / EventSystem

### 7.1 DialogueBox

Canvas 下创建 `DialogueBox`（RectTransform），锚定底部：

| 设置 | 值 |
|---|---|
| anchorMin / anchorMax | (0.05, 0) / (0.95, 0)（左右各留 5% 边距） |
| pivot | (0.5, 0) |
| anchoredPosition | (0, 28) |
| sizeDelta | (0, 230)（高 230px） |

挂 `VNDialogueBox`。面板、流光边框、名牌、推进箭头、打字机文本、快捷功能条
（Save/Load/Auto/Skip/Log/任务/Config/隐藏UI）全部由它 Awake 自动构建。

**字体说明**：文本用 legacy `Text` + `LegacyRuntime.ttf`（系统字体回退链），
中文开箱即用。**不用 TMP** 是因为其默认字体不含 CJK 字形，要自打字体图集，
对本项目是纯负担。

### 7.2 HintText（可选）

Canvas 下创建 `HintText`（Text），底部拉伸（anchorMin=(0,0)、anchorMax=(1,0)、
pivot=(0.5,0)、pos=(0,18)、高 70），字体 LegacyRuntime.ttf、字号 26、
颜色 (1,1,1,0.85)、`raycastTarget` 关。内容写操作提示，例如：

```
Enter/空格/点击 推进（打字中=催促） | H/滚轮上滑 回想 | A 自动 | S 快进
F5 存档界面 | F9 读档界面 | J 任务日志
```

### 7.3 EventSystem

创建 `EventSystem` 物体，挂 `EventSystem` + **`InputSystemUIInputModule`**。

⚠️ 必须是 Input System 的 UI 模块，**不是**旧版 `StandaloneInputModule`
（项目禁用旧输入后旧模块直接报错）。没有 EventSystem 则选项面板、快捷功能条、
存读档界面全部点不动。

---

## 第 8 章 剧本系统接线（VNStage 及周边）

前七章搭出的是"能演"的舞台；本章把"会读剧本"的大脑接上。

### 8.1 角色定义资产（VNCharacterDef）

`Assets/VNEffects/Characters/` 下右键 → Create → VN Effects → Character：

| 字段 | 示例 | 说明 |
|---|---|---|
| id | 亚里沙 | 剧本里 `show 亚里沙`、`亚里沙: 台词` 用的名字 |
| displayName | 亚里沙 | 名牌显示名（可与 id 不同） |
| nameColor | 紫 (0.45,0.3,0.75,0.9) | 名牌颜色 |
| expressions | 默认 → 立绘图 | 表情名→Sprite 映射，`show x expr:表情` / `x 表情: 台词` 切换。至少要有一条"默认" |

### 8.2 剧本文件

`Assets/Scenarios/` 下创建 `xxx.vn.txt`（纯文本，UTF-8）。语法速查见
`Demo.vn.txt` 文件头或 CLAUDE.md。最小可跑剧本：

```
bg bg1
show 亚里沙 at:center with:DissolveGlow
亚里沙: 你好，世界。
```

### 8.3 VNStage（舞台总线）

创建空物体 `VNStage` 挂 `VNStage`，这是**剧本命令 → 特效 API 的落地层**，
引用最多的组件。逐项连线：

**数据库**：
- `characters`：把 8.1 的所有 VNCharacterDef 拖进列表；
- `backgrounds`：逐条填 `id`（剧本里 `bg` 命令用的名字，如 bg1/bg2）+ Sprite。

**舞台引用**（→ 指向前几章创建的物体）：

| 字段 | 指向 |
|---|---|
| characterLayer | LayerFront |
| backgroundImage / backgroundFx | Background 的 Image / VNImageEffectController |
| dialogue | DialogueBox |
| transition | ScreenTransition |
| weather / mood | WeatherController / MoodGrading |
| vnCamera / screenShake / dutchAngle / heartbeat | 对应管理器 |
| sakura / fakeDoF / cloudShadows / godRays | 对应物体 |
| speedLines / shockwave / retroFilter / kenBurns / letterbox | 对应 overlay 物体（kenBurns 在背景上） |
| shootingStars / driftingClouds / heatHaze | 对应物体 |
| vignetteFocus | Global Volume 上的 VNVignetteFocus |
| speakerHighlight / toneMatch / choicePanel | 对应物体 |
| vnAudio / eventRegistry | 见 8.4 / 8.6 |

> **偷懒许可**：`VNStage.AutoWire()` 会在 Awake 时对**为空的引用**自动
> `FindFirstObjectByType` 补线（characterLayer/backgroundImage 按名字找
> "LayerFront"/"Background"；kenBurns 甚至会自愈补挂）。所以理论上只要物体都
> 存在且命名标准，引用全空着也能跑。**但显式连线仍是推荐做法**：AutoWire 是给
> 旧场景兼容用的自愈机制，依赖它会让"场景里忘了创建某物体"这类错误静默失效
> 而不是报错。

### 8.4 VNAudio（三通道音频）

创建空物体 `VNAudio` 挂 `VNAudio`，拖给 `VNStage.vnAudio`。
三个独立音频库按需填：

- `bgmLibrary` / `seLibrary` / `voiceLibrary`：每条 = id + AudioClip +
  **基准音量**（该素材本身响度的标定值；最终音量 = 基准 × 剧本 vol: 参数 ×
  通道音量，三层相乘，素材响度不齐时只调基准即可）。

### 8.5 VNScriptRunner + VNBacklog

- 空物体 `VNScriptRunner` 挂 `VNScriptRunner`：`stage` = VNStage、
  `script` = 你的 .vn.txt（TextAsset）、`playOnStart` 勾上；
- 空物体 `VNBacklog` 挂 `VNBacklog`（H 键/滚轮回想历史）。

存读档界面（F5/F9）、Toast 提示、快捷功能条这些**运行时 UI 都是代码自动构建**
的，不需要手动搭。

### 8.6 VNEventRegistry（玩法事件，可选）

如果剧本用到 `event` 命令（QTE/地图等玩法模块）：

1. 空物体 `VNEventRegistry` 挂 `VNEventRegistry`，拖给 `VNStage.eventRegistry`；
2. 其下建子物体 `QteTemplate`（RectTransform）挂 `VNQteModule`，
   **SetActive 关掉**——模板必须禁用，运行时 Instantiate 副本才激活。
   **为什么**：带 Awake 初始化的模板若在场景里活着，会在启动时抢跑构建 UI；
3. 同理建 `MapTemplate` 挂 `VNMapModule`，填 `mapSprite`（地图底图）和
   `locations`（名称 + 视口坐标 0~1 + 可选显示条件如 `好感度>=2`）；
4. registry 的 `modules` 列表登记：id=qte → QteTemplate、id=map → MapTemplate。

### 8.7 VNQuestLog（任务系统，可选）

剧本用到 `quest` 命令时：`Assets/VNEffects/Quests/` 下创建 `VNQuestDef` 资产
（id/标题/描述/阶段文案），空物体 `VNQuestLog` 挂 `VNQuestLog` 并把资产拖进
`quests` 列表。**没有资产也能跑**（状态全存 flags，资产只管日志文案），J 键开日志。

---

## 第 9 章 运行验证清单与常见坑

### 9.1 验证清单（按顺序检查）

1. **进 Play 背景就在极缓慢漂移**（Ken Burns，盯 10 秒能看出来）；
2. 剧本第一条 `bg` / `show` 正常执行，立绘带出场演出；
3. 台词逐字打出，说话者立绘提亮；Enter/空格/点击推进；
4. 鼠标移动画面有微视差；点击处有涟漪光环；
5. `fx godrays on` 出光束、`shake medium` 画面震、`mood Sunset` 变暖色调——
   随便挑几条 fx 验证；
6. 发光效果（扫光/星光）**有 Bloom 光晕**而不是平面亮色；
7. F5 能存档、F9 读回同一画面；H 键回想正常。

### 9.2 常见坑速查

| 症状 | 原因 | 解法 |
|---|---|---|
| 全部材质粉红 | Shader 没编译完/没被收录 | 等编译；确认三个材质资产存在（打包时保 Shader 收录） |
| 发光全部失效，只是亮色块 | 相机没开 Post Processing；或 Canvas 是 Overlay 模式；或 Volume/Bloom 缺失 | 逐项检查第 2、3 章设置 |
| 整个画面泛白发光 | Bloom threshold 被改到 <1 | 改回 1.0（HDR 契约值） |
| 溶解/扫光在立绘上扭曲不均 | Sprite Mesh Type 是 Tight | 改 Full Rect（1.2 节） |
| 报错 InvalidOperationException: ...Input.GetKey | 有代码/组件在用旧输入 | 本项目组件不会；检查是否误挂了 StandaloneInputModule |
| 点不动选项/按钮 | 没有 EventSystem 或模块不对 | 建 EventSystem + InputSystemUIInputModule |
| 开运镜后震动失效（或反之） | 组件 target 指到了同一个容器 | 按 0.2 的表检查各 target |
| 立绘出现但没有溶解出场等效果 | VNImageEffectController 的 sourceMaterial 没拖或拖错 | 立绘/背景用 VNImageEffect.mat，发光体用 VNAdditive.mat |
| 转场时背景没换 | VNStage.backgrounds 里没有对应 id | 检查 id 与剧本 `bg` 参数一致 |
| 汉字显示为方块 | 用了 TMP 或非 CJK 字体 | 用 legacy Text + LegacyRuntime.ttf（7.1 节） |
| 运动中背景边缘露底色 | 背景没设 ±60px 溢出 | 4.1 节 offset 设置 |
| 修改场景后想恢复"标准答案" | — | 直接跑 Tools → VN Effects → Create Script Demo Scene 重新生成对照 |

---

## 附录 A 键盘演示场景的差异

键盘演示场景（VNEffectsDemo.unity，菜单 Create Demo Scene）与剧本场景共享
第 1~7 章的全部搭建，差异只在"大脑"：

- **没有** VNStage / VNScriptRunner / VNAudio / VNEventRegistry / VNQuestLog；
- 立绘按 4.4 的配方**手动摆**在 LayerFront（单人或双人）；
- 创建空物体 `VNEffectsDemo` 挂 `VNEffectsDemo` 脚本，把所有组件引用逐个拖上
  （字段名与组件一一对应），由它监听键盘驱动所有特效；
- HintText 高度用 320（要显示完整按键表）。

## 附录 B 完整层级树速查

```
Main Camera                    （正交 size5，(0,0,-10)，开后处理）
Global Volume                  （Volume 全局 + VNVignetteFocus）
Canvas                         （ScreenSpace-Camera，planeDistance10，1920×1080）
├─ SceneRoot                   ← 震动/心跳
│  └─ ZoomRoot                 ← 运镜
│     └─ TiltRoot              ← 荷兰角
│        ├─ LayerBack          ← 视差8
│        │  ├─ Background      （Image ±60px 溢出 + VNImageEffectController + VNKenBurns）
│        │  ├─ CloudShadows / DriftingClouds / ShootingStars
│        ├─ LayerMid           ← 视差13
│        │  └─ GodRays
│        └─ LayerFront         ← 视差19（立绘层，运行时生成）
├─ EdgeGlow(20) / SpeedLines(25) / ScreenShockwave(26)
├─ RetroFilter(34) / Letterbox(35)
├─ DialogueBox(40) / ChoicePanel(45) / CameraFade
├─ HintText
└─ ScreenTransition(100)
Ambient_Dust(11) / Ambient_Sparkles(12) / Ambient_Orbs(10)   （场外粒子）
ClickRipple(31) / MouseStardust                              （场外粒子）
ScreenShake / Parallax / DutchAngle / VNCamera / Heartbeat
WeatherController / MoodGrading / FakeDoF / ToneMatch
SakuraBurst / SpeakerHighlight / HeatHaze
EventSystem                     （+ InputSystemUIInputModule）
—— 以下为剧本场景专属 ——
VNStage（全引用总线） / VNAudio / VNScriptRunner / VNBacklog
VNEventRegistry（禁用的 QteTemplate/MapTemplate 子物体） / VNQuestLog
```

> 搭完后与生成器产物对照：跑一次 Create Script Demo Scene，把两个场景的
> Hierarchy 并排比一遍，是最快的自查方式。
