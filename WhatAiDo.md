# WhatAiDo.md — 视觉小说 2D 图片特效系统 开发全记录

> 由 Claude (AI) 编写，记录本次开发的完整思路、计划、每一步做了什么、每个文件的作用与使用方法。
> 日期：2026-07-12～2026-07-14

---

## 一、需求分析

**目标**：让视觉小说不只是"干巴巴地展示图片"，而是让 2D 图片（背景、立绘）通过程序化特效变得
**好看、丰富、明亮**。具体包括：

1. **屏幕悬浮粒子** —— 尘埃、星光在画面上缓慢漂浮、一闪一闪，润色整个画面氛围
2. **光晕/发光效果** —— 图片带柔和辉光、呼吸般脉动，看起来"亮起来"
3. **绚丽的出场效果** —— 图片登场时有溶解显形、扫光、爆闪、弹出等华丽演出
4. **常驻的"活图"效果** —— 图片显示期间也不死板：呼吸发光、悬浮飘动、周期扫光

## 二、项目环境勘察（动手前先确认的事实）

| 项目 | 结论 |
|---|---|
| Unity 版本 | 6000.0.62f1 (Unity 6) |
| 渲染管线 | **URP 17**（`com.unity.render-pipelines.universal` 17.0.4），有 PC / Mobile 两套 RP 资产 |
| 动画库 | **DOTween** 已安装在 `Assets/Plugins/Demigiant`（已验证 `Material.DOFloat(int propertyID)`、`CanvasGroup.DOFade` 等 API 存在） |
| 输入系统 | `activeInputHandler = 1` → **仅新版 Input System**，演示脚本必须用 `Keyboard.current` 而不能用旧版 `Input.GetKeyDown` |
| 现有素材 | `Assets/Assets` 下两张 AI 生成的动漫图（一张 solo 立绘、一张双人场景图），正好用作演示的立绘与背景 |
| 项目状态 | 基本为空项目（只有模板文件），可以从零搭建 |

这些勘察决定了几个**关键技术决策**：

- **发光要"真的亮"必须靠 URP Bloom 后处理 + HDR 颜色**（shader 中输出 >1 的颜色分量，Bloom 阈值设为 1.0，超过 1 的部分才泛光）。因此需要自动配置 Volume + Bloom。
- **uGUI 的顶点色是 Color32，会被钳制到 1**，所以 HDR 发光颜色必须通过**材质属性**传入 shader，不能靠 `Image.color`。
- **Canvas 必须用 Screen Space - Camera 模式**，否则 Overlay 模式的 UI 永远盖在世界空间粒子之上，粒子无法与画面混排。用 `sortingOrder` 控制粒子与 Canvas 的前后关系。
- **全部贴图程序化生成**（柔光圆、四芒星、径向光晕），溶解噪声直接在 shader 里算分形值噪声——整套系统**零美术资源依赖**。
- uGUI 自定义 shader 在 URP 下仍走传统 CGPROGRAM 路径（Canvas 渲染不经过 URP 光照），所以按 `UI/Default` 的骨架写，保留 Stencil / RectMask2D 裁剪兼容。

## 三、系统架构

```
┌─────────────────────────────────────────────────────────┐
│                     四层特效架构                          │
├─────────────────────────────────────────────────────────┤
│ 第 4 层  后处理：URP Bloom + Vignette（让 HDR 真正泛光）    │
│ 第 3 层  演出编排：VNEntranceAnimator（DOTween 序列）       │
│ 第 2 层  粒子氛围：VNAmbientParticles + 星光爆发            │
│ 第 1 层  像素级特效：VN/ImageEffect Shader（溶解/扫光/发光） │
└─────────────────────────────────────────────────────────┘
```

## 四、创建的文件清单与详细说明

### 1. `Assets/Shaders/VNImageEffect.shader` — 图片特效主 Shader

挂在 uGUI Image/RawImage 上的核心 shader，一个 Pass 内叠加 6 种效果（按片元执行顺序）：

| 效果 | 属性 | 说明 |
|---|---|---|
| 微波浪扭曲 | `_WaveAmount/_WaveSpeed/_WaveFreq` | UV 正弦偏移，给图片轻微"微风飘动"感，默认关闭 |
| HSV 调色 | `_HueShift/_Saturation/_Brightness` | 色相偏移、饱和度、亮度（可做夜晚变暗、回忆降饱和、彩虹演示） |
| 斜向扫光 | `_ShineProgress/_ShineWidth/_ShineAngle/_ShineColor`(HDR) | 一道高光带沿指定角度扫过图片；Progress 从 -0.3 推到 1.3 完成一次扫光 |
| HDR 自发光 | `_EmissionColor`(HDR)`/_EmissionAmount` | 全图叠加发光色，配合 Bloom 产生"呼吸辉光" |
| 噪声溶解 | `_DissolveAmount/_DissolveScale/_DissolveEdgeWidth/_DissolveEdgeColor`(HDR) | 0=完全隐藏，1=完全显示；溶解边界带 HDR 辉光边缘（出场像"从光中凝聚成形"） |
| 闪白 | `_FlashAmount/_FlashColor` | 整图向纯色插值，用于出场瞬间爆闪 |

技术要点：
- **噪声不用贴图**：shader 内置 `hash21 → 值噪声 → 3 层 fbm 分形叠加`，输出范围约 0~0.96，
  溶解阈值用 `lerp(1.02, -0.02, amount)` 映射保证两端完全显示/完全隐藏。
- 完整保留 uGUI 的 `UNITY_UI_CLIP_RECT` / `UNITY_UI_ALPHACLIP` 与 Stencil 属性，不破坏 Mask 功能。
- HDR 属性用 `[HDR]` 标记，Inspector 里可直接调发光强度。

### 2. `Assets/Shaders/VNAdditive.shader` — 加法混合发光 Shader

`Blend SrcAlpha One` 加法混合（只增亮不遮挡），带 `[HDR] _TintColor`。两个用途共用：
粒子系统的材质（顶点色 = 粒子颜色）、图片背后光环的 RawImage 材质。HDR 颜色 >1 时被 Bloom 拾取泛光。

### 3. `Assets/Scripts/VNEffects/VNProceduralTextures.cs` — 程序化贴图生成器

运行时生成三张贴图（懒加载 + 缓存，零美术资源）：
- `SoftCircle`（64px 柔边圆）→ 尘埃、光斑粒子
- `Sparkle`（64px 四芒星：中心亮核 + 横竖两道细长星芒，`pow(1-d, 24)` 收窄）→ 星光粒子
- `RadialGlow`（256px 径向渐变）→ 图片背后光环

### 4. `Assets/Scripts/VNEffects/VNImageEffectController.cs` — 单图特效控制器

挂在 Image/RawImage 上，**每张图自动持有独立材质实例**（互不干扰，OnDestroy 自动清理）。API：

```csharp
var fx = image.GetComponent<VNImageEffectController>();
fx.SetDissolve(0.5f);                        // 立即设置溶解度
fx.DODissolve(1f, 1.2f);                     // 补间溶解（出场）
fx.PlayShine(0.75f);                         // 播一次扫光
fx.StartShineLoop(5f);                       // 每 5 秒自动扫光一次
fx.StartBreathingGlow(color, 0.25f, 3f);     // 呼吸发光（HDR + Bloom = 柔和辉光脉动）
fx.PulseEmission(color, 0.8f);               // 瞬时发光脉冲（高亮说话者）
fx.DOFlash(1f, 0.35f);                       // 闪白一次
fx.SetHSV(hue, sat, bri);                    // 调色
fx.DOBrightness(0.6f, 1f);                   // 渐变变暗（如夜晚）
fx.StartFloating(8f, 4f);                    // 上下悬浮飘动
fx.SetWave(0.005f);                          // 微波浪
fx.StopAllLoops();                           // 停止全部常驻效果
```

所有 Tween 都 `SetLink(gameObject)`，物体销毁时自动回收，不会泄漏。

### 5. `Assets/Scripts/VNEffects/VNGlowBackdrop.cs` — 背后柔光光环

挂在立绘上，自动在其**背后**（同父级、前一个渲染顺序）生成一个径向光晕 RawImage：
- 常态：呼吸脉动（`pulsePeriod` / `pulseStrength` 可调）
- `Flare()`：出场瞬间光环从小到大闪耀一次再回落到呼吸状态
- 颜色 × `hdrIntensity`(默认1.6) 触发 Bloom，立绘像被一圈柔光包裹

### 6. `Assets/Scripts/VNEffects/VNEntranceAnimator.cs` — 出场/退场演出编排器

把 shader 参数、CanvasGroup 透明度、RectTransform 位移缩放、背后光环、星光爆发粒子
编排成完整的 DOTween Sequence。**五种出场预设**：

| 预设 | 演出内容 | 适用场景 |
|---|---|---|
| `DissolveGlow` | 噪声溶解显形 + 辉光边缘 + 光环闪耀 + 星光爆发 + 收尾微闪 | 立绘首次登场（最华丽） |
| `FadeSlideUp` | 从下方 45px 轻盈滑入 + 淡入 + 光环渐亮 | 日常对话切换 |
| `ScaleBounce` | 0.65→1 弹性弹出(OutBack) + 微闪白 + 星光 | 俏皮/惊喜登场 |
| `ShineReveal` | 淡入后一道扫光掠过 | 优雅登场 |
| `FlashBloom` | 全屏爆闪中显形 + 光环大闪耀 + 白色星光 + 扫光收尾 | 高潮/重要角色 |

退场：`PlayExitDissolve()`（化作光点消散）、`PlayExitFade()`（淡出下滑）。
出场完成后调 `StartIdleEffects()` 一键开启常驻"活图"三件套（呼吸发光+悬浮+周期扫光）。

```csharp
// 典型用法：
animator.PlayEntrance(VNEntrancePreset.DissolveGlow)
        .OnComplete(() => animator.StartIdleEffects());
```

### 7. `Assets/Scripts/VNEffects/VNAmbientParticles.cs` — 悬浮氛围粒子

挂在空物体上，Awake 时**全代码配置** ParticleSystem（发射区自动匹配相机可见范围）。三种预设：

| 预设 | 效果 | 参数特征 |
|---|---|---|
| `Dust` | 细小尘埃缓慢上飘 | 0.015~0.05 尺寸，噪声扰动，低透明度 |
| `Sparkles` | 四芒星**一闪一闪** | 尺寸随生命周期多峰起伏（TwinkleCurve）+ 缓慢旋转 |
| `Orbs` | 大颗柔光光斑（散景感） | 0.25~0.7 尺寸，极低透明度，超慢漂移 |

所有粒子颜色 × `hdrBoost`(默认1.8) → 被 Bloom 泛光，星光真的"发亮"。
`sortingOrder` 需高于 Canvas 的 sortingOrder 才会叠加在画面之上。

另有静态方法 `VNAmbientParticles.PlaySparkleBurst(pos, color, count)`：
在任意世界坐标爆发一簇向四周飞散渐隐的星光，自动销毁，出场演出内部就在调用它。

### 8. `Assets/Scripts/VNEffects/VNEffectsDemo.cs` — 演示驱动

用新版 Input System (`Keyboard.current`) 提供按键交互，另外给背景加了
**Ken Burns 缓慢缩放**（14 秒 1→1.06 往复）+ 微弱冷色亮度呼吸，让背景也"活着"。

| 按键 | 功能 |
|---|---|
| `1`~`5` | 切换并播放五种出场演出 |
| `Space` | 重播当前预设 |
| `X` | 溶解退场 |
| `S` | 手动扫光一次 |
| `B` | 在立绘位置星光爆发 |
| `P` | 开/关悬浮粒子 |
| `H` | 彩虹色相循环演示（再按恢复） |

### 9. `Assets/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs` — 一键生成演示场景

菜单 **Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**，全自动完成：

1. 把 `Assets/Assets` 下两张图设置为 Sprite（FullRect 网格——溶解/扫光的 UV 才均匀、关 mipmap、开 alpha 透明），文件名含 "solo" 的当立绘、另一张当背景
2. 生成材质资产 `Assets/VNEffects/Materials/`（VNImageEffect.mat、VNAdditive.mat）
3. 生成 `VNEffectsVolumeProfile.asset`：**Bloom**(threshold 1.0 / intensity 1.4 / scatter 0.7) + **Vignette**(0.22)
4. 新建场景：正交相机（开 HDR 后处理、深色底）+ 全局 Volume
5. Canvas（**Screen Space - Camera**、1920×1080 缩放）+ 背景图（四边溢出 60px 留 Ken Burns 余量）+ 立绘（挂好 Controller + GlowBackdrop + EntranceAnimator）
6. 三个悬浮粒子物体（尘埃暖白 / 星光金黄 / 光斑冷蓝，sortingOrder 10~12）
7. 底部操作提示文字 + 演示驱动物体，**所有引用自动连线**
8. 保存为 `Assets/Scenes/VNEffectsDemo.unity`

## 五、怎么用（三步）

1. 回到 Unity，等脚本编译完成（无报错）
2. 菜单 **Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**
3. 点 **Play**，按 `1`~`5` / `Space` / `X` / `S` / `B` / `P` / `H` 体验全部效果

### 接入你自己的视觉小说流程

给任何立绘 Image 挂 `VNImageEffectController` + `VNGlowBackdrop` + `VNEntranceAnimator` 三个组件
（材质留空会自动创建，或指定 `Assets/VNEffects/Materials` 下的资产），然后：

```csharp
using VNEffects;

// 角色登场
animator.PlayEntrance(VNEntrancePreset.DissolveGlow)
        .OnComplete(() => animator.StartIdleEffects());

// 角色说话时高亮
controller.PulseEmission(new Color(1f, 0.9f, 0.6f), 0.5f);

// 切换到夜晚场景
backgroundController.DOBrightness(0.55f, 1.5f);

// 角色退场
animator.PlayExitDissolve();
```

场景里放几个空物体挂 `VNAmbientParticles`（选 Dust/Sparkles/Orbs 预设）即可获得常驻悬浮粒子。

## 六、设计取舍与注意事项

- **为什么不用 Shader Graph**：uGUI Canvas 渲染不走 URP 光照管线，手写 CG shader 对 UI
  兼容性最好（Stencil/RectMask2D 全保留），而且噪声在 shader 里直接算，免贴图。
- **为什么材质要每图实例化**：多个立绘同屏时各自独立控制溶解/发光，互不串扰；代价是打断
  UI 合批——视觉小说同屏立绘数量少（1~3 张），完全可接受。
- **构建（Build）注意**：脚本运行时用 `Shader.Find` 作后备，但正式打包请确保场景/材质资产
  引用了两个 shader（Demo 生成器创建的材质资产已解决此问题），否则 shader 会被裁剪。
  程序化贴图在运行时生成，不受影响。
- **`VNGlowBackdrop` 在 Awake 读取图片 rect 尺寸**：立绘用固定 `sizeDelta` 没问题；若你的
  立绘用拉伸锚点（stretch），布局在 Awake 时可能还没算好，光环尺寸会不对——这种情况请改用
  固定尺寸锚点。
- **Bloom 阈值 = 1.0**：普通颜色（≤1）不泛光，只有 HDR 发光（溶解边缘、扫光、粒子、光环、
  自发光）会亮起来，画面不会整体"糊光"。想更亮调低 threshold 或调高各处 HDR 强度。
- **性能**：粒子总量 <400、单 Pass UI shader、无 RenderTexture，PC/手机都很轻松。

## 七、本次会话时间线（AI 实际执行步骤）

1. 勘察项目：读 `Packages/manifest.json`、`ProjectVersion.txt`、`ProjectSettings.asset`
   （确认 URP 17 / Unity 6 / 仅新输入系统 / DOTween 已装 / 两张可用图片）
2. 制定四层架构计划（shader → 粒子 → 演出编排 → 后处理），确定零美术资源、HDR+Bloom 路线
3. 编写 `VNImageEffect.shader`（6 合 1 图片特效 shader，含程序化 fbm 噪声、HSV 转换）
4. 编写 `VNAdditive.shader`（加法混合 HDR 发光 shader）
5. 编写 `VNProceduralTextures.cs`（柔光圆/四芒星/径向光晕程序化生成）
6. 编写 `VNImageEffectController.cs`（材质实例管理 + 全部参数 API + 三种常驻循环）
7. 编写 `VNGlowBackdrop.cs`（背后光环：呼吸脉动 + 出场闪耀）
8. 编写 `VNEntranceAnimator.cs`（5 种出场 + 2 种退场 + 常驻效果一键开启）
9. 编写 `VNAmbientParticles.cs`（3 种氛围粒子预设 + 静态星光爆发）
10. 编写 `VNEffectsDemo.cs`（新输入系统按键演示 + 背景 Ken Burns）
11. 编写 `Editor/VNEffectsDemoSetup.cs`（一键生成演示场景，自动配置贴图导入/材质/Bloom/连线）
12. 验证 DOTween API：grep 确认 `Material.DOFloat(int propertyID)` 重载与
    `CanvasGroup.DOFade`、`RectTransform.DOAnchorPos` 均存在于当前安装版本
13. 编写本文档 `WhatAiDo.md`

> 注：AI 无法在此环境直接启动 Unity 编译验证。若编译出现报错，把错误信息发回来即可修复。

## 八、版本控制（2026-07-12 建立）

- 项目已上传到公开仓库：**https://github.com/cgenko0729-oss/vnovelProject.git**（默认分支 `main`）
- 配置了 Unity 专用 `.gitignore`：排除 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、
  IDE 文件等所有可再生成内容（否则仓库会膨胀几个 GB），以及本地调试截图 `Assets/DebugScreenShot/`
- **工作流约定（从现在开始严格执行）**：
  1. 每个新功能都在**新分支**上开发：`git checkout -b feature/<功能名>`
  2. 完成后提交并推送该分支，再合并回 `main`
  3. **任何分支都不删除**——每个功能分支都是一个可随时回滚的历史版本点

## 九、第二批功能：氛围特效四件套（2026-07-12，分支 `feature/atmosphere-effects`）

按工作流约定在新分支开发。本批实现四个功能：

### 9.1 God Rays 斜射光束 — `VNGodRays.cs` + 新贴图 `LightBeam`

- `VNProceduralTextures` 新增 `LightBeam` 贴图（128×512：横向柔边 × 纵向上亮下渐隐），
  同时把内部 `Generate()` 升级为支持任意宽高。
- `VNGodRays` 挂在 Canvas 下的空 RectTransform 上（渲染顺序在背景之后、立绘之前），
  Awake 程序化生成 2~4 道光束 RawImage：pivot 设在顶端 → 绕顶端摆动；每道光束的
  角度/宽度/透明度/摆动周期都带随机偏差，避免整齐划一的机械感。
- 动态：`DOLocalRotate` 缓慢摆动（yoyo）+ `DOFade` 透明度呼吸，随机相位错开。
- HDR 颜色（默认暖阳色 ×1.35）配合 Bloom 有柔光感。API：`Show()/Hide()/Toggle()`。

### 9.2 动态暗角/聚焦渐晕 — `VNVignetteFocus.cs`

- 挂在 Global Volume 上，操作 URP Vignette 的 `intensity/smoothness/center` 三个参数。
- **关键细节**：用 `volume.profile`（运行时实例副本）而非 `sharedProfile`，避免在编辑器里
  弄脏磁盘上的 Volume Profile 资产；`center.overrideState` 必须手动设 true（资产里没开）。
- API：`FocusOn(transform)` 把角色世界坐标转视口坐标（`WorldToViewportPoint`）并把暗角
  中心补间过去、强度加深 → 玩家视线聚焦说话者；`ClearFocus()` 恢复居中基础暗角。

### 9.3 屏幕边缘情绪泛光 — `VNEdgeGlow.cs` + 新贴图 `EdgeGlowFrame`

- 新贴图 `EdgeGlowFrame`（按到边缘的最近距离衰减：边缘亮、中心全透明）。
- 全屏 RawImage + `VN/Additive` 加法混合 + HDR 颜色 = 屏幕边缘泛光。
- **嵌套 Canvas + overrideSorting(20)**：保证泛光渲染在氛围粒子（sortingOrder 10~12）之上。
- 四种情绪预设（各有专属颜色与脉动节奏）：
  - `HeartBeat` 心动：粉色，"咚-咚——停"的心跳双脉冲序列
  - `Danger` 危险：红色，0.5s 快速脉动
  - `Sadness` 悲伤：蓝色，3.8s 缓慢起伏
  - `Warmth` 温馨：暖橙，5s 极缓呼吸
- API：`Show(VNEmotionGlow)` / `ShowCustom(颜色,透明度,节奏)` / `Hide()`。

### 9.4 天气系统 — `VNAmbientParticles` 扩展 + `VNWeatherController.cs`

`VNAmbientParticles` 新增四种预设（沿用原架构，全代码配置）：

| 预设 | 实现要点 |
|---|---|
| `Petals` 落樱 | 新 `Petal` 椭圆贴图；顶端细带生成；噪声 `separateAxes`（横向强 0.55/纵向弱 0.08）→ 左右摇曳；`rotationOverLifetime` 翻转 |
| `Rain` 雨 | **拉伸渲染的关键**：Box 形状旋转 90° 朝下 + `startSpeed 10~13` 提供真实粒子速度（Stretch 模式按真实速度拉伸方向才正确），风斜吹用 velocity 模块；自动创建子物体 `RainSplashes` 在屏幕底部持续溅起小水花 |
| `Snow` 雪 | 慢速下落（16~24s 生命周期跨屏）+ 低频噪声横移 |
| `Fireflies` 萤火虫 | 只在画面中下部游走；强噪声漫游 + 复用星光的 TwinkleCurve 忽明忽暗；hdrBoost 2.4 让 Bloom 泛光 |

- 新增静态工厂 `VNAmbientParticles.Create(...)`：用"先 SetActive(false) 再挂组件、赋值后
  再激活"的技巧，保证 Awake→Configure 在字段赋值**之后**执行（修正了直接 AddComponent
  会用默认字段配置的问题；萤火虫 hdrBoost 也因此改为工厂参数传入）。
- `VNWeatherController`：惰性创建各天气粒子，切换时旧天气停止发射（已有粒子自然消散）
  形成交叉过渡；**调色联动**——雨天自动把注册的背景/立绘压暗降饱和（0.8/0.8 冷灰）、
  雪天清冷透亮、萤火虫之夜整体变暗，用的就是第一批的 `DOBrightness/DOSaturation` API。

### 9.5 演示与场景生成器更新

- `VNEffectsDemo` 新增按键：`G` 光束开关、`V` 聚焦渐晕、`E` 情绪泛光循环、`W` 天气循环；
  提示文字同步显示当前情绪/天气状态。
- `VNEffectsDemoSetup` 自动创建并连线：GodRays（背景与立绘之间）、EdgeGlow（Canvas 最后）、
  VignetteFocus（挂 Volume 上）、WeatherController（moodTargets 自动指向背景+立绘）。
- **需要重新执行一次 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene** 让新物体进入场景。

## 十、第三批功能：色调预设 / 情绪动作 / 全屏转场（2026-07-12，分支 `feature/mood-emotes-transitions`）

### 10.1 场景色调预设系统 — `VNMoodGrading.cs`

- **双 Volume 交叉过渡架构**：运行时创建两个全局 Volume（A/B），每个挂
  ColorAdjustments + WhiteBalance + LiftGammaGain + FilmGrain + Vignette（profile 为运行时
  实例，不落盘）。切换情绪时把预设写入闲置的 Volume，然后 DOTween 交叉补间两者的 weight
  —— 画面像电影调色一样平滑过渡，且**任意两种情绪之间都能直接切**（不必先回中性）。
- **priority 递增技巧**：每次启用的新层 priority +1，保证新层永远叠在正在淡出的旧层之上，
  交叉期间不打架。
- 七种预设：`Neutral` 原始 / `Morning` 清晨（冷青偏亮）/ `Sunset` 黄昏（橙金暖高光）/
  `Night` 夜晚（深蓝低饱和压暗）/ `Memory` 回忆（褪色暖黄 + 胶片颗粒 + 暗角）/
  `Tension` 紧张（高对比偏绿）/ `Horror` 恐怖（重度去饱和 + 强颗粒 + 深暗角）。
- 细节：颗粒/暗角组件按预设用 `active` 开关，避免 0 值覆盖基础 Volume 的暗角；
  Memory/Horror 的暗角会盖过 VNVignetteFocus（优先级更高），属已知取舍。
- API：`SetMood(VNMood.Sunset, 2f)` 一行切换。

### 10.2 情绪演出动作库 — `VNCharacterEmotes.cs`

一行代码调用的立绘小动作，全部返回 Sequence 可加入剧情编排：

| 方法 | 演出 |
|---|---|
| `Surprise()` | 快速上跳 34px + 微放大，OutBounce 落地回弹 |
| `Angry()` | 横向 DOShakeAnchorPos 快速抖动 + 红色 PulseEmission 发光脉冲 |
| `Shy()` | 缩小到 0.97 + 下沉 7px + 粉色光晕，停顿后缓慢恢复 |
| `Dejected()` | 下沉 24px + 亮度 0.72 + 饱和 0.68（**持续状态**，直到 `Recover()`） |
| `Nod()` | 两次快速下沉回弹（第二次幅度更小，更自然） |
| `HeadShake()` | ±2.6° 小幅左右旋转摆动后归正 |

- **与悬浮飘动的冲突处理**：动作会移动 anchoredPosition，与常驻悬浮 tween 打架。
  方案：`Begin()` 时自动 `StopFloating()`（会顺带重置到基准位），动作完成后
  `ResumeFloating()` 恢复（为此给控制器加了 `IsFloating` 属性和记住上次参数的
  `ResumeFloating()`）。动作互相打断安全（每次 Begin 杀掉上一个并重置姿态）。

### 10.3 花式全屏转场库 — `VNScreenTransition.shader` + `VNScreenTransition.cs`

- 新 Shader 一个 Pass 内含 6 种图案（`_Mode` 切换）：噪声溶解（复用 fbm，带 HDR 辉光
  边缘）、百叶窗、瓦片翻转（随机顺序 + 对角线推进，瓦片中心取整保证整块一起翻）、
  圆形扩散（宽高比校正保证正圆）、水墨晕染（圆扩散 + 强噪声扰动边界）、纯色全覆盖。
- 组件流程：`Play(type, onCovered)` → 覆盖率 0→1（转出）→ 回调里切换背景/场景内容 →
  1→0（转入）。嵌套 Canvas 排序 100 盖住一切，转场期间 RawImage 拦截点击。
- 七种转场（每种有推荐时长）：`NoiseDissolve` / `Blinds` / `Tiles` / `CircleWipe`（配
  `PlayFrom(type, 角色)` 从说话者位置扩散）/ `InkSpread` / `WhiteFlash`（HDR 白 ×2.2 配
  Bloom 爆亮一瞬间，0.22s 快出 0.75s 慢收）/ `BokehOrbs`（大光斑粒子涌满屏幕 + 柔暖光罩，
  进入回忆专用，复用 Orbs 预设 rate×14）。

### 10.4 演示与场景生成器更新

- 新按键：`M` 色调循环、`T` 转场循环（每次转场自动换一张背景图，正好演示"同一立绘
  不同背景不同情绪"）、`6` 惊讶、`7` 生气、`8` 害羞、`9` 沮丧/恢复、`0` 点头、`N` 摇头。
- 场景生成器：新增 VNScreenTransition.mat 材质资产、MoodGrading/ScreenTransition 物体、
  立绘自动挂 VNCharacterEmotes；把 Assets/Assets 里除立绘外的所有图收集为转场轮换背景。
- **需要重新执行 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**。

## 十一、第四批功能：呼吸立绘 / 轮廓光 / 鼠标星尘 / 热浪（2026-07-12，分支 `feature/breathing-rim-stardust-haze`）

### 11.1 呼吸感立绘（Pseudo-Live2D）— 控制器新增 `StartBreathingMotion()`

- 三个正弦叠加让立绘"活着"：已有的**上下悬浮** + 新增的**横向缩放呼吸**
  （X 轴 ±1.3% 模拟胸腔起伏，Y 轴带 40% 同步微伸展）+ **极缓倾斜摆动**
  （±0.7°，周期 7s，先缓慢摆到一侧再往复，起步不跳变）。
- `StartIdleEffects()` 已自动包含，出场后立绘自动开始呼吸，零调用成本。
- 与情绪动作库联动：动作 `Begin()` 时自动暂停呼吸（重置缩放/旋转），
  结束后 `ResumeBreathingMotion()` 恢复（控制器记住上次参数）。

### 11.2 立绘轮廓光（Rim Light）— Shader 升级 + 控制器 API

- `VNImageEffect.shader` 新增：朝光源方向（`_RimAngle`）偏移采样两次 alpha
  （1×和 2× `_RimWidth`），偏移处透明说明该像素位于受光一侧的外缘 →
  叠加 HDR `_RimColor` 描边。配合 Bloom 形成发光轮廓。
- API：`SetRimLight(颜色, 强度, 宽度, 光源角度)` / `DORimAmount()` 渐亮渐灭 /
  `ClearRimLight()`。夕阳场景橙色轮廓光（角度 40°）、月夜蓝色（140°），
  立绘与背景光照氛围立刻统一。
- 注意：采样邻域 alpha 依赖 Clamp 寻址 + FullRect 单图（本项目均满足）；
  若日后使用 SpriteAtlas 图集需关闭该效果（邻域会采到别的图）。

### 11.3 鼠标轨迹星尘 — `VNMouseStardust.cs`

- 按**移动距离**手动 `Emit()`（每单位距离 7 颗，带余数累加器保证低速也均匀），
  单帧上限 30 颗防止瞬移狂喷；世界空间模拟让星尘留在原地形成拖尾。
- 星尘用四芒星贴图 + HDR×2 泛光，轻微下坠 + 随机漂移 + 缩小消隐 + 缓慢旋转。
- `Toggle()` / `enabled` 开关；用新版 Input System 的 `Mouse.current` 读鼠标。

### 11.4 热浪/空气扭曲 — `VNHeatHaze.cs` + 新粒子预设 `Mist`

- 复用 shader 已有的 `_WaveAmount` 波浪扭曲：开启时把目标图片（默认只有背景，
  避免立绘脸部扭曲）的波浪调到 0.006/速度 3.5/频率 24 → 热浪升腾的空气感。
- 配套新 `Mist` 雾气粒子预设：1.2~2.6 世界单位的大团柔雾（透明度仅 4%~10%）
  从画面下方缓缓升起 + 低频噪声翻滚。温泉/夏日柏油路/篝火场景一键成套。

### 11.5 演示与场景生成器更新

- 新按键：`R` 轮廓光循环（关→夕阳橙→月夜蓝）、`Z` 热浪+蒸汽开关、`C` 鼠标星尘开关；
  呼吸感立绘无需按键，出场后自动生效。
- 生成器新增 MouseStardust、HeatHaze 物体并连线。
- **需要重新执行 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**。

## 十二、第五批功能：说话者高亮 / 水面波光 / 屏幕震动 / 对话框（2026-07-12，分支 `feature/speaker-highlight`）

> 注：用户同时点了"呼吸感立绘"，该功能已在第四批实现并自动运行，本批未重复开发。

### 12.1 说话者高亮系统 — `VNSpeakerHighlight.cs` + 控制器"缩放倍率"改造

- **关键改造**：高亮要缩放立绘，但呼吸动作也在补间缩放，会打架。给控制器引入
  `_scaleMultiplier` 概念：`CurrentBaseScale = 初始缩放 × 倍率`；呼吸围绕它进行；
  `DOScaleMultiplier(mult, dur)` 切倍率时先暂停呼吸缩放分量、过渡完成后围绕新基准继续呼吸。
  情绪动作库改为从控制器读 `CurrentBaseScale`（不再自己缓存缩放），出场动画重播前
  `ResetScaleMultiplier()`。
- 管理器：`SetSpeaker(fx)` —— 说话者恢复亮度 + 放大 1.03 + 在立绘之间移到最前 +
  光环 Flare 闪耀；旁听者压暗 0.6 + 降饱和 0.85 + 微缩 0.97 + 光环熄灭。`ClearSpeaker()` 全员复原。
- 场景生成器升级为**双角色**：有两张 "solo" 图时创建 Character/CharacterB（±380 对位），
  才能看出多人对话的层次。

### 12.2 水面波光 — Shader 新开关 + 控制器 API

- `VNImageEffect.shader` 新增 Water Shimmer 块：**两层不同速度/频率的正弦波相乘**
  （w1 带 y 向扰动、w2 反向滚动）→ pow(3) 锐化成粼粼高光点，再乘一层滚动值噪声打散
  规律感；`smoothstep` 限制在画面下部 `_ShimmerHeight` 以内并向上渐隐。HDR 颜色配 Bloom。
- API：`SetWaterShimmer(强度, 颜色, 高度, 密度, 速度)` / `DOShimmerAmount()` 渐现渐隐。

### 12.3 分级屏幕震动 — `VNScreenShake.cs` + SceneRoot 容器

- **架构点**：Canvas 是 Screen Space - Camera，震相机 UI 纹丝不动。因此生成器新增
  `SceneRoot` 容器（背景+光束+立绘都挂进去），震动作用于容器 —— 画面震、对话框稳，
  正是电影感的做法。悬浮/呼吸等 tween 在容器的子物体上，互不冲突。
- 三级预设：Light 6px/0.25s（心跳）、Medium 16px/0.4s（惊吓）、
  Heavy 34px/0.6s + ±1.4° 旋转抖动（爆炸）。每次震动前重置基准位，连续触发不漂移。

### 12.4 对话框高级化 — `VNDialogueBox.cs` + `VNTypewriterText.cs` + 程序化圆角贴图

- `VNProceduralTextures` 新增 SDF 圆角矩形（实心面板）与 3px 描边框两张 9-slice Sprite。
- **边缘流光**：描边框 Image 挂 `VNImageEffectController` 开扫光循环 —— 扫光带只点亮
  边框像素，视觉上就是一条流光沿边框掠过（复用现有 shader，零新代码）。
- **打字机文字**：`VNTypewriterText : BaseMeshEffect` 直接改 uGUI Text 网格顶点，
  每字一个四边形，按显现进度做"上浮 10px + 淡入"（OutQuad）。**特意不用 TMP**：
  legacy Text 走系统字体回退，中文台词开箱即用（TMP 默认字体无 CJK 字形会显示方块）。
- 对话框：半透明磨砂圆角面板 + 底部 OutBack 轻弹入场 + 骑在顶边的名牌 + 右下角 "▼"
  呼吸浮动继续箭头。API：`Say(名字, 内容)` / `CompleteTyping()` 催促 / `HideBox()`。
- 演示的 Enter 键对话流程**联动说话者高亮**：谁说话谁亮，旁听者自动压暗。

### 12.5 演示新按键

`Y` 说话者循环（A→B→无）、`U` 水面波光开关、`J/K/L` 轻/中/强震动、
`Enter` 对话演示（打字中再按 = 催促显示全文）。
**需要重新执行 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**。

## 十三、第六批功能：视差 / 点击涟漪 / 眨眼转场 / 荷兰角（2026-07-12，分支 `feature/parallax-ripple-eyelid-dutch`）

> 附带决定：上一批误入库的图片已从当前版本移除（`d335f7e`），用户选择**不重写历史**，保持现状。

### 13.1 画面容器层级重构

```
Canvas
└─ SceneRoot   ← 屏幕震动作用于此
   └─ TiltRoot ← 荷兰角旋转+防露角放大作用于此
      ├─ LayerBack  (背景)        ← 视差强度 8px
      ├─ LayerMid   (God Rays)    ← 视差强度 13px
      └─ LayerFront (立绘×2+光环) ← 视差强度 19px
```
三种"整屏运动"（震动/倾斜/视差）各占一层容器，与立绘自身的悬浮/呼吸/情绪
动作（作用于立绘 RectTransform）完全解耦，任意叠加不打架。

### 13.2 多层视差（`VNParallax.cs`）

- 读鼠标位置归一化到 -1..1，各层 `anchoredPosition = 基准 - 偏移 × 强度`（反向移动），
  越"近"的层强度越大 → 纵深感。指数平滑（帧率无关）让跟随有"重量感"。
- 支持运行时 `AddLayer()`（将来加前景树叶/窗框装饰直接注册）。`Toggle()` 关闭时平滑回中。
- 背景本来就四边溢出 60px（Ken Burns 余量），视差 ±8px 不会露边。

### 13.3 点击涟漪（`VNClickRipple.cs`）+ 新贴图 `Ring`

- 新程序化贴图：柔边圆环。点击时发射**单颗粒子**：尺寸曲线 0.12→1 快速扩散、
  透明度 0.9→0 衰减 —— 一颗粒子就是一圈涟漪；同时 `PlaySparkleBurst` 3 颗星光。
- HDR×1.8 配 Bloom 微微发光。世界空间模拟，涟漪留在点击处。

### 13.4 POV 眨眼转场 — `VNScreenTransition` 新 Mode 6

- Shader：上下两片"眼睑"随 Progress 合拢，边缘用 `sin(uv.x·π)` 加眼睑弧线
  （中间闭合更快，更像真实眼皮）；合拢用 InQuad 加速、睁开较慢（0.4s/0.65s）。
- 醒来/昏迷/回忆开场的第一人称感。已自动进入 T 键转场轮换，另有 F 键直接触发。

### 13.5 荷兰角（`VNDutchAngle.cs`）

- `SetTilt(3°)` 缓慢倾斜 TiltRoot；**防露角**：按公式 `cosθ + aspect·sinθ` 自动放大
  （3° ≈ ×1.09），旋转后四角不露底。`Clear()` 回正、`Toggle()` 开关。
- 紧张/异常/醉酒场景的经典心理暗示手法。

### 13.6 演示新按键

`O` 视差开关（默认开，晃鼠标看纵深）、`I` 荷兰角开关、`F` 眨眼转场（换背景）、
鼠标左键点击任意处 = 涟漪+星光。
**需要重新执行 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**。

## 十四、第七批功能：镜头语言 / 心跳演出 / 樱吹雪（2026-07-12，分支 `feature/camera-heartbeat-sakura`）

### 14.1 容器层级再加一层

```
SceneRoot(震动·位置 + 心跳·缩放)
└─ ZoomRoot(镜头缩放/平移)      ← 新增
   └─ TiltRoot(荷兰角·旋转)
      └─ LayerBack/Mid/Front(视差)
```
每种整屏运动独占一个变换维度/容器：震动动 SceneRoot 位置、心跳脉动 SceneRoot 缩放、
运镜动 ZoomRoot、荷兰角动 TiltRoot、视差动三个 Layer —— 全部可同时叠加。

### 14.2 镜头运动语言库 — `VNCamera.cs`

| 方法 | 电影语言 | 实现 |
|---|---|---|
| `PushIn(1.06, 5s, 焦点)` | 缓推：重要台词的压迫感 | ZoomRoot 缓慢放大 + 焦点补偿平移 |
| `SnapZoom(1.12, 0.16s, 焦点, 震动器)` | 急推：惊愕瞬间 | 快速放大，到位瞬间联动轻震 |
| `Pan(目标点, 0.6)` | 平移：视线引导 | 向目标点反向平移（centering 可调居中程度） |
| `DollyZoom(1.3, 3s)` | 眩晕镜头：名场面 | 背景放大 + 立绘 `DOScaleMultiplier(1/zoom)` 反向补偿保持大小 → 空间被拉扯 |
| `ResetCamera()` | 复位 | 缩放/平移/立绘补偿全还原 |

- **焦点补偿**：绕中心放大后平移 `-焦点×(zoom-1)`，让焦点保持在原屏幕位置 →
  视觉上"镜头推向那个点"。立绘的 anchoredPosition 可直接当焦点用。
- DollyZoom 的立绘补偿复用了说话者高亮的缩放倍率机制，与呼吸动作依然兼容。
  已知取舍：DollyZoom/Reset 会覆盖说话者高亮的缩放倍率。

### 14.3 心跳演出 — `VNHeartbeat.cs`

- SceneRoot 缩放按"咚-咚——停"节奏脉动（1.4% 幅度，节奏与 VNEdgeGlow 的
  HeartBeat 泛光图案完全一致：0.1/0.16/0.1/0.42+0.38s），并自动开启粉色边缘泛光。
- `StartBeat()/StopBeat()/Toggle()`。告白、紧张、暧昧场景一行开启。

### 14.4 樱吹雪爆发 — `VNSakuraBurst.cs`

- 纯组合技：创建一个 **10 倍速率**的花瓣系统并调成"暴风参数"（生命周期缩短到 4~7s、
  强风向左 -3.2~-1.6、生成带右移加宽保证覆盖全屏）→ 花瓣被风横扫涌过画面 3 秒，
  同时自动开启心跳演出、延后 2 秒关闭；爆发结束后余瓣自然飘落殆尽。
- `sakura.Play()` 一行触发告白名场面。

### 14.5 演示新按键

`Q` 运镜循环（缓推→急推→平移→眩晕→复位，提示栏显示当前运镜名）、
`A` 心跳演出开关、`D` 樱吹雪告白。
推荐组合：D 樱吹雪 + Q 缓推 + M 黄昏色调 = 完整告白演出。
**需要重新执行 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**。

## 十五、第八批功能：景深/色调匹配/脚影/残影/云影/选项（2026-07-13，分支 `feature/depth-polish-choices`）

### 15.1 伪景深 — Shader 微模糊 + `VNFakeDoF.cs`

- **技术修正**：原计划用 URP 真 DoF，但 Canvas UI **不写深度缓冲**，真 DoF 会把立绘和
  背景一起糊掉。改为给 `VNImageEffect` shader 加 **9-tap 微模糊**（`_BlurAmount`，
  中心+四方+四角采样平均），只作用于背景那张图 —— 效果反而更准确。
- `VNFakeDoF.SetFocus(true)` 四合一：背景模糊 0.006 + 压暗 0.86 + 降饱和 0.9 +
  背景层微放大 1.035（缩放 LayerBack 而不是背景图，避开 Ken Burns 的缩放动画）。
  立绘瞬间"浮"出来。控制器新增 `SetBlur/DOBlur` API。

### 15.2 立绘色调自动匹配背景 — `VNToneMatch.cs`

- **GPU 均值采样**：`Graphics.Blit` 把背景图缩到 4×4 RenderTexture 再 `ReadPixels`
  回读求平均（不要求贴图开 Read/Write）。
- 平均色**归一化**（最大分量拉到 1）后只取"色调"，与白色按 `strength`(9%) 插值，
  通过 `Image.color` 乘法微染色 —— 不占用特效 shader 的任何参数，不改变立绘亮度。
- 换背景（T/F 转场）时自动匹配，开场也匹配初始背景。消除"立绘像贴纸"的违和感。

### 15.3 立绘脚下阴影 — `VNFootShadow.cs`

- 角色脚下自动生成扁椭圆软影（SoftCircle 压扁 + 黑色半透明），挂组件即用零配置。
- 每帧联动：悬浮越高影子越小越淡（离地感）、跟随角色横移、
  透明度同步 CanvasGroup 淡入淡出与溶解出场进度。已加入 CreateCharacter 自动挂载。

### 15.4 残影冲入出场 — 出场预设新增 `AfterimageDash`

- 角色从画面左侧 560px 外高速冲入（0.38s OutCubic），途中三次在当前位置生成
  **冷色调残影副本**（复制 Image，alpha 0.42，0.3s 淡出后销毁），
  收尾微闪白 + 光环闪耀 + 星光。惊喜/战斗系登场。

### 15.5 云影飘过 — `VNCloudShadows.cs`

- 3 块 950~1500px 的黑色软斑（普通透明混合 = 压暗）以不同速度缓慢横穿背景上部，
  越界回绕 + 轻微正弦纵向漂移。只挂在 LayerBack 下，**不会盖到立绘**。晴天的"活气"。

### 15.6 选项按钮演出 — `VNChoicePanel.cs`（零新特效，纯组合）

- `Show(选项数组, 回调)` 运行时构建按钮（圆角面板贴图复用对话框的）：
  - **错落飞入**：右侧 90px 滑入 + 淡入，每个延迟 0.09s
  - **悬停**：`PlayShine` 扫光掠过 + 微放大 1.045（VNImageEffectController 直接挂按钮）
  - **选中**：被选项闪光 + 扫光 + OutBack 轻弹；**落选项噪声溶解消散**
- 场景生成器自动创建 **EventSystem + InputSystemUIInputModule**（新输入系统的
  UI 点击必需，此前场景没有交互 UI 所以一直没建）。

### 15.7 演示新按键

`[` 伪景深开关、`]` 云影开关、`Tab` 残影冲入、`退格` 选项演出；
色调匹配与脚下阴影全自动无按键。
推荐组合：`[` 伪景深 + `V` 聚焦渐晕 + `Q` 缓推 = 完整对话特写运镜。
**需要重新执行 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene**。

## 十六、剧本系统 P0：自研轻量 DSL 核心（2026-07-13，分支 `feature/vn-script-core`）

> 选型结论（详见对话分析）：放弃 Pixel Crushers Dialogue System 作为核心（其数据库资产
> 对 Git/AI 协作不友好），采用**自研 Ren'Py 风格纯文本剧本**。DS 插件保留备用。

### 16.1 架构（新增 `Assets/Scripts/VNEffects/Script/`）

```
Demo.vn.txt（纯文本剧本） → VNScriptParser（解析） → VNScriptCommand 列表
      → VNScriptRunner（协程解释器：顺序/异步/等待/推进） → VNStage（舞台落地层）
            → 既有的 60+ VNEffects API
```

### 16.2 剧本语法（P0 已实现命令集）

| 命令 | 说明 |
|---|---|
| `bg bg1 [transition:Eyelid]` | 切背景（背景库 id），可带全屏转场 |
| `show 亚里沙 [at:left] [expr:微笑] [with:DissolveGlow]` | 角色登场（运行时生成完整组件栈） |
| `hide 亚里沙 [with:dissolve\|fade]` | 退场并销毁 |
| `emote 小雪 Surprise` | 情绪动作（7 种） |
| `亚里沙 微笑: 台词` / `旁白: 台词` / `: 无名牌旁白` | 台词行（说话者自动高亮+切表情） |
| `wait 0.6` | 分镜停顿 |
| `camera pushin 1.05 5 [focus:角色]` | 运镜（pushin/snapzoom/pan/dolly/reset） |
| `shake light\|medium\|heavy` / `sakura` | 震动 / 樱吹雪 |
| `weather Petals` / `mood Sunset` / `transition WhiteFlash` | 天气 / 色调 / 独立转场 |
| `fx godrays\|dof\|clouds\|haze\|shimmer\|heartbeat\|dutch on\|off`、`fx focus 角色` | 特效开关 |
| 行尾 `@` | **异步**：不等待该演出完成（演出 timing 的核心语义） |
| `label/jump/choice/flag/if` | 已解析、P1 实现 |

### 16.3 核心组件

- **`VNCharacterDef`（ScriptableObject）**：角色 id/显示名/名牌色/**表情名→立绘映射**，
  立绘表情资产集中管理（`Create → VN → Character Definition`）。
- **`VNScriptParser`**：行解析（注释/异步后缀/命令 kwargs/台词行全半角冒号），
  保留行号 → 所有报错精确到"第 N 行"。
- **`VNStage`**：角色运行时工厂（Image+控制器+光环+出场器+情绪+脚影全栈生成）、
  表情切换（按高度重算宽度）、背景库、说话分发（自动说话者高亮）、fx 分发、
  在场角色变化时自动刷新 ToneMatch/SpeakerHighlight 注册。
- **`VNScriptRunner`**：协程解释器——同步命令 `yield WaitForCompletion()`，
  `@` 异步 fire-and-forget；台词=等打字完+玩家推进（Enter/空格/点击，打字中按下=催促）。

### 16.4 场景生成器重构

- 把原 CreateDemoScene 拆出共享的 **`BuildStageRig()`**（相机/后处理/Canvas/容器层级/
  全部特效管理器），键盘演示场景与剧本场景共用。
- 新菜单 **Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景 Create Script Demo Scene**：自动创建
  两个角色定义资产（`Assets/VNEffects/Characters/亚里沙|小雪.asset`）、
  演示剧本 `Assets/Scenarios/Demo.vn.txt`（已存在则不覆盖，放心改）、
  VNStage（背景库 bg1..bgN 自动填充）+ VNScriptRunner，保存为 `VNScriptDemo.unity`。

### 16.5 使用方法

1. 菜单 Tools → VN Effects → 演示场景 Demo Scenes → **重建剧本演示场景 Create Script Demo Scene** → Play，
   Enter/空格/点击推进剧情（演示剧本含出场/表情动作/运镜/心跳/换景/天气/樱吹雪全流程）
2. 直接编辑 `Assets/Scenarios/Demo.vn.txt` 再 Play 即可看到修改（语法速查在文件头注释）
3. 后续：P1 分支选项 → P2 存档回想 → P3 台词内嵌演出标记

## 十七、剧本系统 P1：分支与变量（2026-07-13，分支 `feature/vn-script-branching`）

### 17.1 新增命令

| 命令 | 说明 |
|---|---|
| `label 名字` | 位置标记（Play 时预扫描全部 label，支持向前跳转；重名报错） |
| `jump 名字` | 无条件跳转 |
| `flag 名字` / `flag 名字 3` / `flag 名字 +1` | 全局变量：置 1 / 赋值 / 增减 |
| `if 条件 jump 标签` | 条件跳转。条件不能含空格：`勇气` / `!勇气` / `好感度>=2`（支持 >= <= == != > <） |
| `choice` + 若干 `* 选项行` | 选项块，接现成的 VNChoicePanel 演出（飞入/悬停扫光/落选溶解） |

选项行语法：`* 文本 [flag:名字+1] [-> 标签]`——可附带 flag 操作；无 `->` 则顺序继续。

### 17.2 实现要点

- **`VNFlags`** 静态类：整型字典（bool=0/1），`Apply("名字+2")` 解析增减操作串，
  `Evaluate("好感度>=2")` 按长度优先匹配比较符求值。P2 存档时整个字典随进度序列化。
- **解析器**：`*` 开头的行挂到上一个 `choice` 命令（空行/注释不打断选项块，
  其它命令会打断）；选项行从右往左剥 `-> 标签`、再剥 `flag:` 操作，剩余为文本。
- **解释器**：`Play()` 预扫描 label 表 → 跳转 O(1)；`ChoiceCo` 弹出选项面板协程等待
  玩家选择 → 应用 flag → 跳转。选择期间的点击/Enter 对推进无副作用。
- 演示剧本已升级为**双路线多结局**：告白线/退缩线 → 汇合 → `if 好感度>=2` 分出好结局
  （原 Demo.vn.txt 未被用户修改过，已直接原地升级，场景引用不受影响）。

### 17.3 使用

重开 `VNScriptDemo` 场景 Play 即可（纯代码+剧本更新，场景无需重新生成）。
玩到"我一直……有件事想告诉你"会弹出选项，两条路线通向不同结局。

## 十八、问题修复记录（剧本系统）

### 修复 3：`VNStage 未连线 choicePanel`（2026-07-13）

**现象**：P1 后在旧的 VNScriptDemo 场景里走到 choice 命令报错。
**原因**：场景是 P0 时生成的，`choicePanel` 是 P1 新加的字段——生成器的自动连线
只在重新生成场景时执行，旧场景里新字段为空。
**修复**：给 `VNStage.Awake()` 加 **AutoWire 自动补线**：所有为空的引用自动
`FindFirstObjectByType` 查找（容器/背景按名字找）。从此给 VNStage 加新字段，
旧场景不重新生成也能自愈，这类错误一劳永逸。

## 十九、剧本系统 P2：存档/回想/Auto/Skip（2026-07-13，分支 `feature/vn-save-backlog`）

### 19.1 存档系统 — `VNSaveSystem.cs`

- **快照内容** = 恢复点（正在显示的那句台词的命令索引）+ 全部 flag +
  舞台状态（背景 id / 天气 / 色调 / 可开关 fx 的开关表 / 在场角色的 id·横坐标·表情）。
  JSON 存到 `persistentDataPath/vn_save_{槽位}.json`，多槽位。
- **只允许停在台词上时存档**（`_waitingAtSay`）——保证"恢复点之后的命令都没执行过"，
  读档重播不会出现 flag 双重加算之类的错乱。
- **读档流程**：停解释器 → 恢复 flag → `RestoreSnapshot`（清场→背景瞬切→天气/色调
  快速过渡→fx 先全关再按记录开→角色 `ShowInstant` 瞬间摆台+直接开常驻活图）→
  从恢复点那句台词继续。VNStage 为此新增 CurrentBackgroundId 跟踪与 fx 状态表。

### 19.2 回想 Backlog — `VNBacklog.cs`

- 每句台词（含选择记录）入列（上限 200 条）；`H` 或**滚轮上滑**打开全屏回想面板，
  滚轮浏览，H/Esc/点击背景关闭；打开期间剧情推进被阻止。
- UI 全程序化：独立 Overlay Canvas + ScrollRect（VerticalLayoutGroup +
  ContentSizeFitter），说话人名用富文本金色加粗。

### 19.3 Auto / Skip 模式 + 屏幕提示

- **Auto（A 键）**：打字完自动等待「基础 1.4s + 字数×0.045s」后推进。
- **Skip（S 键）**：打字瞬间完成 + 0.07s 自动推进 + **`DOTween.timeScale`=4 全局加速
  所有演出**（出场/转场/运镜跟着快），`wait` 停顿同步加速；到 choice 强制停下
  （玩家必须亲自选）；手动点击推进会顺手退出快进（VN 惯例）；场景销毁时恢复 timeScale。
- **`VNToast`**：自建 Overlay Canvas 的轻量提示——底部气泡（"已保存"）+
  右上角常驻模式标签（AUTO ▶ / SKIP ▶▶）。
- 读档时若选项面板开着会被 `VNChoicePanel.ForceClose()` 强制清掉。

### 19.4 操作一览（剧本场景）

`Enter/空格/点击` 推进 | `H`/滚轮上滑 回想 | `A` 自动 | `S` 快进 | `F5` 存档界面 | `F9` 读档界面
（Backlog 物体缺失时解释器会自动创建，旧场景无需重新生成）

## 二十、角色尺寸标定（2026-07-13，分支 `feature/character-calibration`）

**问题**：不同来源的立绘构图不统一（占满画面 vs 四周留白 vs 半身近景），
统一高度缩放后视觉大小和站位不一致——"小图放左边"和"正常图放左边"结果不同。

**解法（业界通行）**：每角色标定，剧本命令保持统一，差异在资产层吸收。
`VNCharacterDef` 新增两个字段：

- `sizeScale`（默认 1）：该角色显示高度 = 舞台统一高度 × 此值。留白多显小→调大；近景显大→调小
- `positionOffset`：在 at:left/center/right 标准站位上的附加偏移（脚下留白多→y 负值下压）

`VNStage` 全链路应用：登场摆位（含基准位同步）、初建尺寸、表情切换重算宽度、
读档 ShowInstant（存档 x 已含偏移、y 按标定重建）。标定方法已写入 HowToUse.md 第七章。

## 二十一、剧本系统 P4：音频 / 表情溶解 / move（2026-07-13，分支 `feature/vn-audio-move-crossfade`）

### 21.1 音频系统 — `VNAudio.cs`（项目首次有声音）

- **BGM**：双 AudioSource 交叉淡入淡出（切曲无缝），`bgm play <id> [fade:秒]` / `bgm stop`
- **SE**：一次性用 `PlayOneShot`；循环环境音（`se 雨声 loop`）每个独立 AudioSource
  + 淡入淡出，`se stop <id>` 停止
- **Voice**：独立通道，新语音顶掉旧的（配音预留）
- **音量**：`volume bgm|se|voice 0~1`，立即作用于在播声音
- **打字音**：`typingTick` 槽位赋一个短音效 → 打字机每字自动"哒哒哒"
  （0.055s 节流 + ±6% 随机音高防机械感），`VNTypewriterText` 按整字推进触发
- **音频库**：id → AudioClip 列表（同背景库模式）；当前 BGM 随存档保存、读档恢复
- 旧场景自愈：VNStage.AutoWire 找不到 VNAudio 会自动创建

### 21.2 表情交叉溶解

- `ApplyExpression` 升级：角色完全可见时（溶解≈1 且未淡出），换表情前复制一份
  旧表情立绘**覆盖在本体之上**淡出 0.25s（新表情立即生效在底下）——视觉上就是
  旧表情融化成新表情。时长 `VNStage.expressionCrossfade` 可调（0=关闭）。
- 出场前的表情设置不触发溶解（不可见时无意义）。

### 21.3 move 滑步换位命令

- `move 亚里沙 left [0.6]`：平滑滑到新站位（支持 left/center/right/数字坐标，
  自动应用角色标定偏移）。
- **基准位三连同步**：出场器 SetBasePosition + 情绪库 SetBasePosition +
  控制器新增的 `SetFloatBaseY`（悬浮基准；顺带修了 Show/ShowInstant 换位后
  悬浮会拽回旧 Y 的隐患——float base 此前只在首次 StartFloating 缓存一次）。
- 滑动期间悬浮暂停、到位恢复。常配 `@` 边走边说。

### 21.4 新命令一览

`move` / `bgm play|stop` / `se [loop]|stop` / `voice` / `volume`，
均已写入 HowToUse.md（含免费音频素材站推荐）。

## 二十二、剧本系统：自由镜头路径 camseq（2026-07-13，分支 `feature/vn-camseq`）

**背景**：`camera` 五个预设只能"一次性补间到隐含目标"，无法表达任意点、纵向移动、
角色身体部位、瞬切起手和多段连续路径（用户明确提出三个做不到的案例）。

### 22.1 核心抽象：镜头状态 = (目标点, zoom)

- `VNCamera` 新增三原语：`Cut(点,zoom)` 瞬切、`GoTo(点,zoom,秒,缓动)` 单段直达、
  `PlayPath(路径点列表)` 多段路径（编成一条 Sequence）
- "看向点 p"采用**居中语义**：偏移 = `-p × zoom`（区别于 PushIn 的"焦点保持"语义）
- **防露边钳制**：偏移上限 = `(画布半宽+背景溢出) × zoom - 画布半宽`，默认开启可关
- **多段默认缓动**让整条路径像一次连续运镜：首个移动段 InSine 缓起、中间 Linear
  匀速、末段 OutSine 缓停；单段 InOutSine；每点可 `ease:` 覆盖（支持全部 DOTween Ease 名）

### 22.2 剧本语法

- `camseq` 块 + `> 目标点 [zoom] [时长] [ease:名]` 路径点行（复用 choice 块的解析模式，
  `>` 前缀；时长 0 = 瞬切段）；`camto` 单段简写、`camcut` 独立瞬切
- **目标点三种寻址**（英文词汇，按用户要求）：
  九宫格锚点 topleft~bottomright（±620/±340）；
  角色部位 `角色:head/chest/waist/feet/up/mid/down`（角色位置 + 立绘高度 × 部位比例，
  head=+0.36 chest=+0.15 waist=-0.08 feet=-0.42）；
  裸坐标 `x,y`
- 路径点在**执行时**解析（角色移动后也能对准）；camseq 整块支持 `@` 异步
- 旧 `camera` 五预设原样保留；与震动/荷兰角/视差天然叠加

### 22.3 用户三案例的对应写法已写入 HowToUse.md（camseq 章节）

## 二十三、镜头演出可视化编辑器 第一批（2026-07-13，分支 `feature/camseq-editor`）

**目标**：不再手写/硬编码 camseq 数值——可视化编排、预览、生成文本粘贴进剧本。

### 23.1 窗口结构（Tools → VN Effects → 镜头编排 Camera Sequence Editor）

- **迷你画布**（16:9）：场景背景缩略图打底 + 三个站位参考剪影 + 每个路径点的
  **取景框矩形**（编号标签）+ 取景中心连线（点线）+ 白色"当前预览取景框"。
  点击空白 = 给选中路径点设坐标（自动切坐标类型）；点击取景中心附近 = 选中该点；拖动微调
- **路径点列表**（ReorderableList 拖拽排序）：类型（锚点九宫格/角色部位/坐标）、
  zoom 滑条、时长、缓动下拉、"瞬切起手/回原点收尾"快捷按钮
- **预览**：进度条拖动或 ▶ 播放——取景框按**真实缓动公式**沿路径移动
  （直接调 DOTween 的 `EaseManager.Evaluate`，默认缓动分配与运行时一字不差）
- **文本双向**：生成 camseq 文本→剪贴板；粘贴已有文本"解析载入"继续可视化调整
  （复用运行时的 `VNScriptParser`，零重复解析代码）

### 23.2 关键实现点

- **数学共用**：`VNCamera.OffsetFor` 重构出公开静态 `ComputeOffset`，
  编辑器取景框与运行时用同一份"居中偏移+防露边钳制"公式，预览不骗人
- **编辑态角色近似**：角色是运行时生成的，编辑态按行内选择的"假定站位"显示，
  并读取角色资产的 sizeScale/positionOffset 尽量贴近真实；Play 中走真实位置
  （窗口内 HelpBox 已注明）
- 纯编辑器代码（Editor 目录），零运行时风险

### 23.3 第二批待办（用户确认后）

场景内编辑态实时预览（操作 ZoomRoot + 自动还原）、捕获当前镜头状态为路径点、
画布拖角改 zoom、镜头预设库资产。

## 二十四、镜头编辑器 第二批（2026-07-13，分支 `feature/camseq-editor-2`）

### 24.1 场景内实时预览

- 工具栏「场景预览」开关：开启时记录 ZoomRoot 的位置/缩放 →
  拖进度条或 ▶ 播放时**直接驱动场景里的 ZoomRoot**
  （`EditorApplication.QueuePlayerLoopUpdate()` 强制刷新）→ Game 视图看真实画面运镜
- 三重还原保险：手动关闭还原、窗口关闭（OnDisable）还原、
  进出 Play（playModeStateChanged）前还原——预览状态绝不会被序列化进场景或运行副本

### 24.2 捕获当前镜头

- 读 ZoomRoot 当前 scale/anchoredPosition，反解 `点 = -偏移/zoom`，追加为坐标路径点
- 典型用法：Scene 视图手动摆好 ZoomRoot 构图 → 捕获 → 调时长/缓动

### 24.3 画布拖角改 zoom

- 画布交互升级为三模式（DragMode）：拖选中取景框**四角** = 改 zoom
  （指针到取景中心的距离反解，取两轴较大者，0.5~3 钳制）；拖中心 = 移动；点空白 = 设坐标

### 24.4 镜头预设库

- `VNCamseqPresetLibrary`（`Assets/VNEffects/CamseqPresets.asset`，首次保存自动创建）：
  **以 camseq 文本形式存预设**——存/取走既有的生成/解析双向通道，同名覆盖
- 工具栏第二行：命名保存 / 下拉 / 载入 / 删除。常用运镜存一次到处复用

## 二十五、其他问题修复记录

### 修复 1：`Particle Velocity curves must all be in the same mode`（2026-07-12）

**现象**：运行时报错。
**原因**：`VNAmbientParticles.cs` 的 velocityOverLifetime 模块中，X/Y 轴用了
`MinMaxCurve(min, max)`（双常数随机区间模式），Z 轴却写成 `vel.z = 0f`
（隐式转换为单常数模式）。Unity 要求同一速度模块的三条曲线**模式必须一致**。
**修复**：三个粒子预设（Dust / Sparkles / Orbs）的 Z 轴统一改为
`new ParticleSystem.MinMaxCurve(0f, 0f)`，与 X/Y 保持双常数模式。

### 修复 2：剧本场景双角色都挤在画面中央（2026-07-13）

**现象**：`show 亚里沙 at:left` / `show 小雪 at:right` 后两人都出现在中央叠在一起。
**原因**：初始化顺序 bug —— 角色运行时生成于 (0,-60)，`VNEntranceAnimator.Awake()`
立刻把该位置缓存为基准位；`VNStage.Show` 随后虽移到了 at: 指定的站位，但
`PlayEntrance → PrepareHidden` 会把角色**重置回缓存的旧基准位**。
**修复**：
1. `VNEntranceAnimator` 新增 `BasePosition` 属性与 `SetBasePosition()`；
   `VNCharacterEmotes` 同样新增 `SetBasePosition()`。
2. `VNStage.Show` 摆位后同步调用两者的 SetBasePosition。
3. 顺带加固 `VNFootShadow`：基准位改为**每帧动态读取**出场器的 BasePosition
   （角色被剧本换位/滑入出场时影子位置不再漂移）。

## 二十六、camseq 镜头交叉淡化（2026-07-13，分支 `feature/camseq-fade`）

### 26.1 需求与问题

`bg bg2 transition:Eyelid` 后紧跟首点为瞬切（时长 0）的 camseq 时，
转场揭示的是**全图**，下一帧才跳到首镜头视角——有明显的"瞬间移动感"。
另外镜头之间的瞬切、以及 camseq 结束后的复位，也希望能选择"叠化"过渡。

### 26.2 核心思路：截屏叠化

两个镜头状态本质是 ZoomRoot 的两组 scale/position，无法直接补间出"叠化"。
通用解法（各家 VN 引擎同款）：**截取当前整屏画面盖在最上层 → 镜头瞬切 →
把截图淡出**，视觉上就是旧视角叠化到新视角。

### 26.3 新语法（全部可选，默认行为不变）

```
bg bg2 transition:Eyelid
camseq start:cut end:fade endfade:0.8
> top 2.05 0            # start:cut → 眨眼睁开时画面已在 top 2.05
> 34,-269 2.05 2
> right 2 0 xfade:0.5   # 该瞬切改为 0.5 秒叠化
> left 2 3
```

| 选项 | 语义 |
|---|---|
| `start:cut` | 紧跟带转场的 bg 时：首镜头瞬切塞进转场 `onCovered` 回调（与换背景图同帧），揭示时画面直接是首镜头视角。要求首点时长 0；条件不满足自动退化为普通 camseq 并告警 |
| `start:fade` | camseq 开始时截屏当前画面 → 瞬切首镜头 → 叠化（`startfade:秒`，默认 0.6） |
| `end:fade` | 走完路径后截屏 → 瞬间复位 → 叠化回全图（`endfade:秒`，默认 0.6） |
| 路径点 `xfade:秒` | 该点用"截屏→瞬切→叠化"代替平移/瞬切 |

### 26.4 文件改动

| 文件 | 改动 |
|---|---|
| `VNCameraFade.cs`（新增） | 截屏叠化组件：嵌套 Canvas 排序 90（对话框 40 之上、ScreenTransition 100 之下）+ 全屏 RawImage。`CaptureCo()` 协程等帧末用 `ScreenCapture.CaptureScreenshotIntoRenderTexture` 截屏（URP 下不能手动 `Camera.Render()`）；`FadeOut(秒)` 淡出。D3D 等平台后备缓冲上下颠倒，按 `SystemInfo.graphicsUVStartsAtTop` 用负 uvRect 翻转（Inspector 有 FlipMode 手动开关兜底） |
| `VNCamera.cs` | `Waypoint` 加 `fade` 字段；`PlayPath` 抽出 `BuildSegment(from,to)`（编辑器预览不受影响）；新增协程版 `PlayPathCo(points, startFade, endFade)`——连续普通点仍合成一条 Sequence 保持原缓动手感，fade 点走"截屏→Cut→淡出"；`SnapReset()` 瞬间复位；`cameraFade` 引用留空时自动在 Canvas 下创建（旧场景不重建也能用） |
| `VNScriptParser.cs` | `VNCamWaypointDef.fade` + 路径点行识别 `xfade:`；`VNScriptCommand.KwF()` 浮点 kwargs 助手 |
| `VNScriptRunner.cs` | `PrecutFor(bgCmd)`：bg 带转场且同步执行时向后看一条命令，若是 `start:cut` 的 camseq 就把首镜头瞬切并入转场盖屏回调，并记录 `_precutDone` 让该 camseq 跳过首点；`CamseqCo` 改调 `PlayPathCo` 并解析 start/end 选项 |
| `VNStage.cs` | `SetBackground` 加 `onCovered` 回调参数（转场盖屏瞬间与换图一起执行） |
| `VNEffectsDemoSetup.cs` | `BuildStageRig` 创建 CameraFade 覆盖层并接线（两个演示场景共用） |
| `Demo.vn.txt` | 文件头补 camseq 语法速查；演示段应用 start:cut / end:fade / xfade |

### 26.5 技术要点

- 截屏时序：帧末截屏（画面 = 旧视角）→ 立即 Cut（下一帧生效）→ 下一帧新视角
  被不透明截图盖住 → 淡出。全程无裸帧。
- 截图是整屏静帧，叠化的零点几秒内粒子/对话框在旧图里冻结——camseq 通常在
  台词之间执行，观感无差别。
- 快进模式的 `DOTween.timeScale` 全局加速对叠化淡出同样生效。
- 顺带修正演示剧本：camseq 的 `>` 路径点块中间插过一行 `fx heartbeat on`，
  会把块截断导致后续 `>` 行被忽略（解析器会告警）；语法速查里已加注意事项。

### 26.6 验证方法

Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景 Create Script Demo Scene 重建剧本场景（或直接 Play 旧场景，
CameraFade 会自动创建）→ Play：眨眼转场睁眼时画面应直接在 top 2.05 视角，
`right` 一镜为 0.5 秒叠化，结尾从 `left` 视角 0.8 秒叠化回全图。
若截图上下颠倒，把 CameraFade 的 Flip 改为 ForceFlip/NoFlip。

## 二十七、镜头编辑器支持交叉叠化（2026-07-13，分支 `feature/camseq-editor-fade`）

把二十六章的 camseq 叠化选项接进可视化编辑器（VNCamseqEditorWindow），
文本生成/解析/预览三条通道全部对齐运行时。

### 27.1 界面新增

- **开场/收尾选项行**（画布与路径点列表之间）：
  - 开场下拉：无 / cut（接 bg 转场盖屏瞬切）/ fade（当前画面叠化到首镜头）+ 秒数
  - 「收尾叠化回全图」开关 + 秒数
  - start:cut 但首点时长非 0 时显示黄色警告（运行时会退化为普通 camseq）
- **路径点第二行新增 `xfade` 输入框**：>0 = 该点用叠化代替平移/瞬切
  （zoom 滑条 160→130 腾出宽度）；「清空」同时重置开场/收尾选项

### 27.2 预览时间轴重构

- 原 `StateAtTime`（直接遍历路径点）重构为 **`BuildSegments()` 段列表模型**：
  开场 fade 段（消费首点）→ 各路径点（xfade 覆盖为叠化段）→ 收尾 fade 段
- 缓动默认分配改为**按叠化段切组**，每组内 首 InSine / 中 Linear / 末 OutSine
  （单段 InOutSine）——与运行时 `PlayPathCo` 按 fade 分组调 `BuildSegment` 完全一致
- `PreviewAtTime` 返回镜头状态 + 叠化信息：叠化段内白色取景框瞬切到新视角，
  **橙色残框按 InOutSine 渐隐 = 正在淡出的旧视角**；总时长把叠化秒数计入进度条
- 场景预览（驱动 ZoomRoot）在叠化段表现为瞬切——符合运行时真实行为
  （真实叠化发生在截屏覆盖层上，ZoomRoot 本身就是瞬切）

### 27.3 文本双向

- 生成：`camseq [start:cut|start:fade] [end:fade]`（秒数非默认 0.6 才写
  startfade:/endfade:）；路径点行追加 `xfade:秒`
- 解析载入：读 camseq 的 start/end/startfade/endfade kwargs 与路径点 fade 字段
- 预设库存的就是 camseq 文本 → 叠化选项自动随预设保存/载入，零改动

## 二十八、对话框说话者头像（2026-07-13，分支 `feature/dialogue-portrait`）

参考截图（Assets/DebugScreenShot/Snipaste_2026-07-13_20-46-11.png）：
角色说话时在对话框左侧显示半身头像，名字与正文在头像右边。

### 28.1 设计

- **裁切窗口方案**：对话框左下角放一个 `RectMask2D` 窗口（默认 230×300，
  可高出面板顶边形成"半身像探出对话框"的效果），头像图放窗口内、
  超出部分被裁掉 → **全身立绘配合缩放/偏移就能框出胸像特写，不需要单独出头像素材**
- 头像图默认"宽度填满窗口、顶边贴窗口顶边"（脸在图片上方，默认就能看到头部），
  `portraitScale` 放大、`portraitOffset` 平移即可精确构图
- 显示头像时正文与名牌自动右移避让；隐藏时恢复原布局

### 28.2 配置与控制（对应需求：开关 / 选图 / 缩放 / 偏移）

| 层级 | 控制方式 |
|---|---|
| 全局开关 | 剧本命令 `portrait on` / `portrait off`（状态进存档快照） |
| 每角色开关 | `VNCharacterDef.showPortrait`（Inspector 勾选） |
| 选图 | `VNCharacterDef.portraits` 列表（name 对应表情名：台词行 `角色 表情: …` 自动匹配同名头像，没匹配用第一个；**列表留空 = 自动用表情立绘当头像**） |
| 缩放 | `VNCharacterDef.portraitScale`（1 = 宽度填满窗口，调大出特写） |
| 偏移 | `VNCharacterDef.portraitOffset`（窗口内平移，把脸挪进窗口） |
| 窗口尺寸 | `VNDialogueBox.portraitWindowSize`（Inspector） |

### 28.3 文件改动

| 文件 | 改动 |
|---|---|
| `VNCharacterDef.cs` | 新增「对话框头像」区块：showPortrait / portraits / portraitScale / portraitOffset + `GetPortrait(表情)`（未配头像回退立绘；showPortrait=false 返回 null） |
| `VNDialogueBox.cs` | 头像窗口（RectMask2D + Image）程序化构建；`SetPortrait(sprite, scale, offset)` / `SetPortraitEnabled(bool)`；正文 offsetMin 与名牌 x 按窗口宽度避让 |
| `VNStage.cs` | `Say` 里按说话者设置头像（优先本句表情，否则用角色当前表情）；旁白/未注册角色清空头像；`SetPortraitEnabled` + `_portraitOff` 进 Capture/RestoreSnapshot |
| `VNSaveSystem.cs` | `VNSaveData.portraitOff` 字段（旧存档缺字段默认 false = 开启，兼容） |
| `VNScriptParser.cs` | Keywords 加 `portrait` |
| `VNScriptRunner.cs` | `case "portrait"`：`stage.SetPortraitEnabled(Arg(0) != "off")` |
| `Demo.vn.txt` | 文件头补 portrait 语法说明 |

### 28.4 注意

- 旧角色资产反序列化时新字段取 C# 初始值（showPortrait=true、scale=1）→
  **重建场景/改资产都不需要，Play 即生效**（头像回退用立绘）
- 顺带修正用户测试剧本里的 `startfade : 0.5`（冒号两边不能有空格，
  否则被拆成三个 token 参数不生效）→ `startfade:0.5`
- 想要某个角色不显示头像：取消其资产里 showPortrait；想全程关闭：剧本开头 `portrait off`

## 二十九、角色立绘与对话头像实时预览编辑器（2026-07-13，分支 `agent/character-visual-preview`）

### 29.1 需求

`VNCharacterDef` 的 `sizeScale / positionOffset / portraitScale / portraitOffset` 此前只能修改
Inspector 后进入 Play，等角色登场和说出台词才能看到实际效果。不同素材的透明留白、长宽比、
人物构图差异很大，反复 Play 校准立绘高度、脚底位置和头像裁切效率很低。

### 29.2 新工具

新增 `Assets/Scripts/VNEffects/Editor/VNCharacterVisualPreviewWindow.cs`，菜单：

**Tools → VN Effects → 预览 Preview → 角色立绘预览 Character Visual Preview**

也可以在 Project 窗口选中任意 `VNCharacterDef` 后：

- 右键 **VN Effects → 角色立绘预览 Open Character Visual Preview**；
- 或在角色资产 Inspector 的右键上下文菜单打开。

窗口顶部可在项目中的全部角色定义之间切换；在 Project 窗口选择另一个角色资产时，预览窗口
也会自动跟随。

### 29.3 立绘实时预览

- 左侧显示 **1920×1080 舞台预览**，支持 left / center / right 三个标准站位；
- 表情下拉可检查该角色的每张表情立绘；
- 预览严格复用运行时尺寸公式：
  `显示高度 = VNStage.characterHeight × sizeScale`，
  `显示位置 = 标准站位 + positionOffset`；
- 可指定一张背景图辅助检查实际构图（仅编辑器预览，不写入角色资产）；
- 「从场景读取尺寸」会读取当前场景 `VNStage.characterHeight`、背景图和
  `VNDialogueBox.portraitWindowSize`；场景中没有这些组件时使用 880 / 230×300 默认值；
- 直接在舞台拖动立绘 = 实时修改 `positionOffset`；鼠标滚轮 = 修改 `sizeScale`；
- 同时保留精确数值输入、参数归零和当前立绘资产定位按钮。

### 29.4 头像实时预览

- 右侧按照 `VNDialogueBox` 的真实 **RectMask2D 顶边锚定裁切公式**显示头像窗口；
- 头像列表优先读取 `VNCharacterDef.portraits`；列表为空时明确标注「回退立绘」，并使用
  `expressions` 预览，与运行时 `GetPortrait()` 行为一致；
- 直接拖动头像 = 修改 `portraitOffset`；鼠标滚轮 = 修改 `portraitScale`；
- `showPortrait` 关闭时仍以半透明方式显示素材，方便先校准再开启，同时显示关闭提示；
- 支持自定义预览头像窗口尺寸、参数归零和当前头像资产定位。

运行时头像公式保持完全一致：宽度为
`portraitWindowSize.x × portraitScale`，高度按 Sprite 长宽比计算；头像锚点/轴心为顶边中央，
`portraitOffset.y` 为正时向上移动，窗口外内容被裁切。

### 29.5 编辑安全与验证

- 所有拖动、滚轮和数值修改均调用 Unity Undo，支持 Ctrl+Z / Ctrl+Y；
- 修改后只标脏当前 `VNCharacterDef`，不创建临时场景物体、不修改场景；
- 工具栏「保存角色资产」可显式保存当前资产；
- 窗口会同步重绘 Inspector、Scene/Game 等编辑器视图；
- 新代码仅位于 `Editor/`，不会进入玩家运行时构建；
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` 验证通过：0 warning / 0 error。

## 三十、角色预览加入完整对话 UI 与确认后写入（2026-07-13，分支 `agent/character-preview-ui-confirm`）

在二十九章实时预览工具的基础上，加入更接近 Game View 的完整构图检查，并把编辑流程从
“输入即修改角色资产”改为“先调整草稿、确认后一次写入”。二十九章分支保留不删除，随时可以
切回原来的即时写入版本。

### 30.1 完整对话画面预览

- 1920×1080 舞台预览现在可叠加运行时比例的完整对话区：半透明面板、边框、姓名条、
  正文、继续箭头与左侧裁切头像；
- 对话框锚点、边距、姓名条位置、正文避让、箭头位置、头像窗口与头像顶边锚定公式，均按
  `VNDialogueBox` 的运行时布局换算到编辑器预览；
- “从场景读取尺寸”除原有舞台高度、背景与头像窗口尺寸外，还会读取当前场景
  `VNDialogueBox.panelColor / frameColor / nameTagColor`；
- 新增“显示完整对话 UI”开关与“预览对白”输入框，两者只影响编辑器预览，不写入任何资产；
- `showPortrait`、头像缩放和头像偏移的草稿变化，会同步反映在右侧头像特写和左侧完整对话框，
  姓名条与正文也会实时决定是否为头像腾出空间。

### 30.2 草稿与确认流程

- `sizeScale / positionOffset / showPortrait / portraitScale / portraitOffset` 全部先写入一个
  `HideAndDontSave` 内存草稿；滑杆、数值输入、归零、舞台拖动、头像拖动与滚轮均不再直接改变
  `VNCharacterDef`；
- 工具栏与底部确认条会明确显示“有未确认调整”或“资产值已同步”；
- 按“确认写入角色资产”后才用一条 Unity Undo 记录把五个草稿值写入当前角色资产，并立即保存；
- 按“放弃未确认调整”会从角色资产重新读取五个值，不产生资产修改；
- 草稿本身也接入 Unity Undo，因此确认前仍可 Ctrl+Z / Ctrl+Y 调整；
- 有未确认草稿时切换角色，会弹出“确认并切换 / 取消切换 / 放弃并切换”三选一，避免无提示地
  覆盖校准结果；直接关闭窗口则安全丢弃内存草稿，不修改角色资产。

### 30.3 文件与验证

- 修改 `Assets/Scripts/VNEffects/Editor/VNCharacterVisualPreviewWindow.cs`；没有修改任何运行时代码、
  场景、角色资产或素材；
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` 验证通过：0 warning / 0 error。

## 三十一、剧本可视化编辑器 第一批（2026-07-13，分支 `feature/scenario-editor`）

菜单 **Tools → VN Effects → 剧本编辑器 Scenario Editor**。目标：不再手打关键字（消灭 typo），
用下拉框编排整个剧本。**文本仍是唯一真相**：编辑器打开 .vn.txt →
行列表 → 保存时逐行重新生成写回（格式规范化；注释/空行原样保留，往返无损）。

### 31.1 文件构成（全部在 Editor 下，零运行时改动*）

| 文件 | 职责 |
|---|---|
| `VNScenarioSchema.cs` | **命令参数模式表**（本工具核心资产）：26 个命令每个参数的类型/候选来源/默认值。UI、生成、校验的单一数据来源；加新命令补一条，界面自动长出控件 |
| `VNScenarioDoc.cs` | 文档模型：解析（镜像运行时分词规则，含 choice `*` 行、camseq `>` 行、行尾 `@`、全半角冒号台词）→ VNRow 行列表 → 生成；label/flag 收集；校验器 |
| `VNScenarioEditorWindow.cs` | 主窗口：工具栏（Open/Save/Save As/Reload/Refresh Sources/Go to label）、三页签（Edit/Text/Issues）、ReorderableList 行编辑、按类别分组的添加菜单、撤销、外部修改检测 |

*运行时唯一改动：`VNScriptParser` 加了 `CommandKeywords` 公开只读访问器（关键字单一来源）。

### 31.2 下拉数据源（自动收集，Refresh Sources 刷新）

| 参数 | 来源 |
|---|---|
| 命令关键字 | VNScriptParser.Keywords |
| 角色 / 表情 | 扫 VNCharacterDef 资产（表情随所选角色联动） |
| 背景 id | 场景 VNStage.backgrounds |
| 音频 id | 场景 VNAudio.library（se 首参附带 stop） |
| 转场/天气/情绪/出场预设 | 枚举反射（永不过期） |
| jump / choice 跳转目标 | 当前文档 label 列表 |
| flag 名 | 全文档收集（选项 flag 下拉给 名+1/名-1/名 组合） |

所有下拉带 "custom…" 项 → 转自由文本输入（如 at: 写数字坐标、fx focus 写角色名），
"▾" 按钮切回下拉。

### 31.3 校验器（对手写剧本同样生效）

- 错误：未注册角色/背景/音频 id、jump/if/选项跳转目标不存在、label 重复、
  数字参数非数字、if 条件含空格或格式非法、choice 无选项、孤儿 `*`/`>` 行、
  `[fade:2]`/`xfade :` 这类冒号带空格或带方括号的可疑 token
- 警告：未识别 token（原样保留）、camseq 无路径点、start:cut 首点时长非 0、
  说话者未注册、表情不存在、**无名旁白首词疑似打错的命令**（编辑距离 ≤1 检测，
  直接命中"typo 静默变旁白"这个最阴险的坑）
- Issues 页签逐条列出，Select 定位到行；Edit 页行首红/黄圆点同步显示

### 31.4 其他设计决策

- choice 块：选项行内嵌编辑（文本 + flag 下拉 + 跳转下拉 + 增删）
- camseq 块：start/end/startfade/endfade 走下拉/数字框，`>` 路径点行本批
  以文本行内嵌编辑（增删行），下一批接镜头编辑器双向
- 撤销：文本快照栈（约 1 秒粒度合并），Ctrl+Z / Ctrl+Y；文本框内部编辑走系统自带
- 外部修改检测：窗口聚焦时对比文件时间戳；无本地改动静默重载，
  有改动出横幅二选一（重载丢弃 / 保留本地待保存覆盖）
- Text 页签 = 只读的"保存后长什么样"预览 + 一键复制（贴给 AI 协作）
- 界面语言：英文 + 关键字原文（用户要求）
- 验证：Assembly-CSharp / Assembly-CSharp-Editor 均 `dotnet build` 通过（0W/0E）

### 31.5 已知限制（第二批候选）

- camseq 路径点无可视化（先用镜头编辑器生成文本贴入）
- 无多选批量操作；无跨文件 flag 收集

## 三十二、剧本可视化编辑器可用性与调试增强（2026-07-13～2026-07-14）

本轮围绕 `VNScenarioEditorWindow` 连续完善“命令选择辨识度、图片预览选择、从任意行启动调试”三条
工作流。每项功能都在独立 `agent/*` 分支完成、非快进合并回 `main`，所有分支均保留。

### 32.1 主命令分组、中英标注与对话类型

涉及分支：

- `agent/main-dropdown-categories`
- `agent/main-dropdown-chinese-labels`
- `agent/transition-dropdown-chinese-labels`
- `agent/emotion-dropdown-chinese-labels`
- `agent/dialogue-main-dropdown-option`

主命令控件由一次平铺 26 个关键字的 `EditorGUI.Popup` 改为 `GenericMenu` 分层菜单：

| 分类 | 命令示例 |
|---|---|
| Scene（场景） | bg / weather / mood / transition |
| Character（角色） | show / hide / emote / move / portrait |
| Camera（镜头） | camera / camcut / camto / camseq |
| FX（特效） | shake / fx / sakura |
| Audio（音频） | bgm / se / voice / volume |
| Flow（流程） | wait / label / jump / flag / if / choice |

- 菜单与当前行主按钮保留英文关键字，同时显示中文说明，例如 `voice（语音）`、`bg（背景）`；
- `VNTransition` 与 `emote` 的英文枚举值也显示中文含义，但写回剧本的值仍保持英文；
- `say（对白）` 成为 `Dialogue（对话）` 分类的一等选项，可把命令行转换为台词行；
- 台词行左侧也使用同一个主菜单，可以再切回其他命令；转换时会清理不属于新行类型的块数据。

### 32.2 可自定义分类颜色

分支：`agent/custom-category-colors`。

- 工具栏新增“分类颜色”，可分别设置对话、场景、角色、镜头、特效、音频、流程七种颜色；
- 当前行左侧主命令按钮按所属分类着色，长列表中可以快速识别行类型；
- 颜色保存到 `EditorPrefs`，不会写入剧本文本或污染场景/资产；
- 提供“恢复默认”；颜色控件的 `GUI.changed` 与剧本文档脏标记隔离，改颜色不会误报剧本未保存。

### 32.3 背景、角色与表情图片浏览器

涉及分支：

- `agent/background-thumbnail-picker`
- `agent/inline-background-thumbnail`
- `agent/say-show-image-picker`
- `agent/fix-say-show-image-selection`

默认文字下拉无法清楚展示图片，因此新增通用 `PopupWindowContent` Sprite 浏览器：

- 520×430 弹窗、三列缩略图网格、名称搜索、当前项高亮、清除选择、`custom…` 和无预览占位；
- 使用 `Sprite.textureRect` 计算 UV，只绘制实际 Sprite 区域，兼容同一 Texture 中的子 Sprite；
- 保持原始长宽比，不要求贴图开启 Read/Write，也不生成额外缩略图资产；
- `bg` 从场景 `VNStage.backgrounds` 读取 id + Sprite；主面板背景字段显示小型内联缩略图，点击
  缩略图或 id 都会打开浏览器，悬停显示 id 与资源路径；
- `say` 和 `show` 的角色选择器显示默认立绘；表情选择器只显示当前角色的表情 Sprite；主面板
  同样显示当前角色/表情的小缩略图；切换角色时自动清空旧角色表情；
- 只改变编辑器显示与选择方式，保存到 `.vn.txt` 的仍是背景 id、角色 id、表情名。

修复记录：初版通用回调把 `say` 的选择结果写进普通 `VNRow.values`，但台词实际使用
`VNRow.speaker / expression`，导致点击后看似没有切换。`agent/fix-say-show-image-selection` 新增
按字段类型读写的访问器，并清理旧错误回调留下的临时键；`show` 继续使用普通命令参数路径。

### 32.4 从选中行进入 Play Mode

涉及分支：

- `agent/play-from-selected-line`
- `agent/rebuild-state-before-selected-line`

Edit 页顶部新增 `▶ 从选中行播放` 与“重建前置状态”开关。使用流程：选中任意行 → 点击按钮 →
Unity 自动进入 Play Mode → `VNScriptRunner` 从该行或下一条有效命令开始。

编辑器侧实现：

- `SourceLineForRow` 会累计普通行以及 choice `*`、camseq `>` 子行，正确换算 UI 行到物理文本行；
- 空行/注释选择自动落到下一条解析后的命令；目标之后无命令或文档存在 Error 时不进入 Play；
- 使用 `_doc.GenerateText()`，因此未保存的内存修改也能调试；
- `VNPlayFromLineBridge` 通过 `SessionState` 跨越 Play Mode/domain reload 传递剧本文本、目标行和
  是否重建状态；请求消费后立即清除，不影响下一次普通 Play；
- Bridge 等待 `VNScriptRunner.IsInitialized`，让默认 `playOnStart` 完成启动后再停止并接管，避免
  默认播放与调试播放互相覆盖；找不到 Runner 会在有限重试后输出明确错误。

运行时实现：

- `VNScriptRunner.PlayFromSourceLine(source, line, rebuildState)` 根据解析命令的 `line` 找到索引，
  调用现有 `ResumeAt(index)`；
- 勾选“重建前置状态”时，按目标前的文本静默汇总背景、天气、氛围、BGM、音量、循环 SE、
  角色在场状态/站位/表情、portrait、可开关 FX、focus、flags 与可确定的镜头状态；
- 汇总结果复用 `VNSaveData + VNStage.RestoreSnapshot(data, instant:true)` 瞬间摆台，台词等待、
  转场动画、一次性 SE 和 voice 不预播；
- `VNAudio.ResetForDebug()` 清除默认启动瞬间留下的 BGM/语音/循环 SE/Tween，并恢复初始音量；
- `VNStage.RestoreSnapshot` 新增 instant 重载，支持无背景快照时清空旧背景；`VNCamera.SnapReset`
  用于无法可靠推断的动画镜头路径；
- 前置文本含 choice/jump/if 时按文件顺序重建，并在 Console 警告：工具无法凭目标物理行推断
  玩家此前选择的实际分支。取消勾选“重建前置状态”即可使用纯直接跳转。

### 32.5 修改文件与验证

| 文件 | 本轮职责 |
|---|---|
| `Editor/VNScenarioEditorWindow.cs` | 分类菜单、翻译、颜色、图片浏览器、行内缩略图、选中行播放 UI、SessionState Bridge |
| `Script/VNScriptRunner.cs` | 从源行定位命令、初始化同步、前置状态扫描与恢复 |
| `Script/VNStage.cs` | instant 快照恢复与空背景清理 |
| `Script/VNAudio.cs` | 调试启动前的音频/Tween 清理和初始音量恢复 |

每批功能均运行 `dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo`；最终合并版本
Assembly-CSharp 与 Assembly-CSharp-Editor 均为 **0 warning / 0 error**。开发过程中只提交目标源码，
用户已有的场景、剧本、角色资产与图片工作区修改全部保留且未混入提交。

## 三十三、语音播放时自动压低 BGM（2026-07-14）

修改 `Assets/Scripts/VNEffects/Script/VNAudio.cs`：

- `voice` 开始播放时，当前 BGM 平滑降低 20%（保留基础音量的 80%）。
- 语音自然播放结束或被停止后，BGM 自动平滑恢复到原基础音量。
- 语音播放期间切换 BGM，新 BGM 会直接使用压低后的目标音量。
- `volume bgm` 在语音期间仍修改基础音量，恢复后不会遗失玩家设置。
- Inspector 可通过 `voiceBgmReduction` 和 `voiceBgmFadeDuration` 调整降低比例与过渡时间。

验证：`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo`，结果为 **0 warning / 0 error**。

## 三十四、高级全屏转场：卷页、碎裂、水波、墨染（2026-07-14）

在现有 `VNScreenTransition` 全屏遮罩架构中新增四种可直接用于剧本的转场：

- `PageCurl`：从右向左卷页，使用弯曲页缘、暖色背光与软阴影表现纸张厚度。
- `Shatter`：从指定中心放射扩散的三角碎片，碎片边界带冷色 HDR 裂缝高光。
- `Ripple`：从指定中心扩散的水面转场，主波前后带多道衰减波纹和蓝色高光。
- `InkBleed`：多个墨团融合扩散，噪声模拟纸张纤维边缘，并加入离散飞墨颗粒。

接入范围：

- `VNScreenTransition.cs`：新增枚举、推荐时长、Shader 模式、颜色及中心点支持。
- `VNScreenTransition.shader`：新增 Mode 7~10 的程序化图案，不依赖外部贴图或 RenderTexture。
- `VNScenarioEditorWindow.cs`：转场下拉菜单新增中文名称。
- `VNEffectsDemo.cs`：T 键循环演示包含新转场；放射类效果可从角色位置开始。
- 剧本可直接写 `bg bg2 transition:PageCurl`，或使用 `Shatter`、`Ripple`、`InkBleed`。

验证：Unity 刷新工程后运行
`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo`，结果为 **0 warning / 0 error**；
`git diff --check` 未发现本轮文件的空白错误。

### 34.1 转场粉色 Shader 修复（2026-07-14）

`Shatter` 模式曾使用 HLSL 保留关键字 `triangle` 作为局部变量名，导致整个
`VN/ScreenTransition` Pass 在 D3D11 下编译失败；转场遮罩启用时 Unity 因而显示错误粉色。
将变量重命名为 `shardSide`，保留原有碎片计算逻辑。材质与 Shader GUID 无需修改。

### 34.2 四种高级转场改为背景 A→B 直接揭示（2026-07-14）

`bg <id> transition:PageCurl|Shatter|Ripple|InkBleed` 不再播放“黑色遮罩覆盖 → 换图 →
黑色遮罩退场”，而是复制旧背景 A、立即将真实背景换成 B，再仅动画 A 的临时副本：

- `PageCurl`：旧背景从右向左卷起，页背使用镜像图像、暖色纸张高光和落在新背景上的软阴影。
- `Shatter`：`VNShatterGraphic` 生成单个 14×8 网格的 224 个独立三角碎片；中心、随机种子写入
  UV1/UV2，由 Vertex Shader 在一个 draw call 中完成放射位移、旋转、缩小、重力和淡出。
- `Ripple`：旧背景在波纹内部透明，波前对旧图进行径向折射并直接露出新背景。
- `InkBleed`：多墨团与纸纤维噪声逐步挖去旧背景，仅边界染暗，不再形成全屏黑幕。

新增 `VN/DirectBackgroundTransition` Shader；`VNStage.SetBackground` 仅对上述四种 `bg` 转场走
直接背景路径，独立 `transition` 命令和其他旧转场仍使用原全屏系统。首张背景为空或直接 Shader
不可用时安全回退到原转场。临时网格、材质、输入阻断层在完成、中断和销毁时统一清理。

验证：Unity 成功导入新 Shader 且日志无 Shader error；
`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` 为 **0 warning / 0 error**。

## 三十五、20 槽存读档界面（2026-07-14）

原本 F5/F9 直接操作单一槽位，现在改为全屏 4×5 网格存读档界面：

- F5 在等待台词推进时截取当前游戏画面，然后打开“保存”页；F9 随时打开“读取”页。
- 每个槽位显示 16:9 PNG 缩略图、槽位编号、保存时间和最后一句台词；空槽有明确占位状态。
- 截图复用 `VNCameraFade` 的屏幕截取与 Y 翻转判断，并在截取前隐藏存读档 UI。
- 保存到已有槽、读取已有槽均有二次确认；保存/读取页签可以互相切换，Esc 或关闭按钮退出。
- 界面打开时暂停剧情并临时停止 Auto/Skip，关闭后恢复进入界面前的 `Time.timeScale`。
- JSON 文件继续使用 `vn_save_{slot}.json`，缩略图使用同编号的 `vn_save_{slot}.png`；旧的 1 号槽 JSON
  仍可读取，只是在没有 PNG 时显示占位图。

实现文件：`VNSaveLoadPanel.cs` 负责运行时 UI、槽位刷新与确认弹窗；`VNSaveSystem.cs` 负责 20 槽
元数据和缩略图读写；`VNScriptRunner.cs` 负责快捷键、暂停和保存上下文；`VNCameraFade.cs` 提供
不显示转场遮罩的 320×180 缩略图捕获。

验证：Unity 强制刷新后脚本编译成功；
`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` 为 **0 warning / 0 error**。

## 三十六、对话框快捷功能条与 Config（2026-07-14）

对话框右上角新增常驻小型快捷功能条，不需要玩家记住键盘快捷键：

- `Save` / `Load`：打开 20 槽保存或读取界面。
- `Auto` / `Skip`：切换自动播放和快进；启用时按钮以金色高亮显示。
- `Log`：打开现有 Backlog 回想界面。
- `Config`：打开设置面板，可调整 BGM、SE、Voice 音量、文字速度与窗口/全屏模式；设置使用
  `PlayerPrefs` 保存并在后续运行中恢复。
- `隐藏 UI`：隐藏对话框及快捷功能条，不改变台词和打字进度；左键、右键、Enter、Space 或 U
  只负责恢复 UI，不会在恢复的同一次输入中推进剧情。正常显示时也可右键快速隐藏。

`VNQuickToolbar.cs` 负责按钮布局及 Auto/Skip 状态高亮，`VNConfigPanel.cs` 负责运行时设置界面；
旧场景无需重建，`VNScriptRunner` 启动时会自动挂到当前 `VNDialogueBox`。同时鼠标推进现在会检查
`EventSystem.IsPointerOverGameObject()`，点击功能按钮不会误触发下一句台词。

验证：Unity 导入新增组件并生成 `.meta` 后，
`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` 为 **0 warning / 0 error**。

### 36.1 快捷条按钮文字与点击修复（2026-07-14）

初版快捷条创建了 `Text` 组件但漏掉标签字符串赋值，因此只能看到深色按钮底图；同时工具条位于
`VNDialogueBox` 的嵌套 Canvas 内，该 Canvas 原本没有 `GraphicRaycaster`，按钮无法收到点击事件。
现在标签会明确写入每个按钮的 Text，工具条使用高一层排序的独立 Canvas + GraphicRaycaster，并在
旧场景缺少 EventSystem 时自动补建 Input System UI EventSystem。

## 三十七、角色默认表情自动眨眼（2026-07-14，分支 `agent/character-blink`）

角色立绘新增可选的自动眨眼系统。闭眼素材不是眼睛局部图层，而是与默认立绘对齐的完整全身
Sprite；运行时直接短暂替换同一个 `Image.sprite`，因此不需要额外眼睛节点或遮罩。

### 37.1 每角色配置

`VNCharacterDef` 新增「眨眼」区块：

- `enableBlink`：每个角色独立开关，默认关闭，旧角色资产不会突然开始眨眼。
- `blinkSprite`：完整闭眼全身立绘；未配置时即使开启也只保持睁眼。
- `blinkIntervalMin / blinkIntervalMax`：两次眨眼之间的随机间隔，默认 2.5～5 秒。
- `blinkDuration`：闭眼保持时间，默认 0.1 秒。
- `DefaultSprite / IsDefaultExpression()`：统一按 `expressions` 第一项识别默认表情；空表情及无效
  表情回退到第一项时也视为默认表情。

### 37.2 运行时与表情切换安全

新增 `VNCharacterBlink.cs`，由 `VNStage.CreateCharacter()` 给每个运行时角色自动挂载：

- 使用 DOTween Sequence 随机等待，闭眼时瞬间替换完整 Sprite，短暂停留后恢复睁眼，再排程下一次。
- 只有当前为默认表情、角色开关已启用且闭眼图存在时才运行；任何其他表情都不会被眨眼组件改写。
- 眨眼不走现有 `expressionCrossfade`，避免每次闭眼产生表情残像。
- 如果换表情命令刚好发生在闭眼帧，先恢复默认睁眼图、取消旧计时，再用睁眼图执行正常交叉溶解；
  切回默认表情后重新随机计时。
- Tween 与角色 GameObject 绑定，退场、清空舞台和销毁时自动终止；读档只恢复表情，不保存瞬时闭眼帧。
- 默认立绘与闭眼立绘宽高比或 Pivot 不一致时输出一次警告，提醒素材可能在眨眼时跳动。

### 37.3 角色视觉预览

`VNCharacterVisualPreviewWindow` 的草稿系统同步加入眨眼开关、闭眼图、随机间隔和闭眼时长；这些值
仍然只有点击「确认写入角色资产」后才保存。默认表情下可以切换「预览闭眼状态」，并会提示
缺少闭眼图或两张图宽高比/Pivot 不一致。非默认表情不提供闭眼预览，与运行时规则一致。

### 37.4 素材与验证

闭眼图应和默认表情使用相同画布尺寸、人物位置、透明留白与 Pivot。工作区现有 `blink.png`、
`blink01.png` 保持为用户未提交素材，本功能没有自动合成、改写或擅自绑定到任何角色资产。

验证：为避免当前打开的 Unity 尚未刷新新脚本到生成的 csproj，验证时临时把新脚本加入本地编译
清单，完成后立即还原；`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` 结果为
**0 error**。现有 Unity/.NET 引用和旧代码弃用接口产生原有 warning，本功能没有新增编译错误；
`git diff --check` 通过。

## 三十八、角色说话口型（2026-07-14，分支 `agent/character-mouth-flap`）

角色原始完整立绘继续保留闭嘴状态；说话时在立绘上方开关一张透明张嘴图，结束后隐藏叠加层，
因此不会改写基础 Sprite，也能与第三十七章的完整立绘眨眼同时工作。

### 38.1 每角色配置与素材约定

`VNCharacterDef` 新增「说话口型」区块：

- `enableMouthFlap`：每角色独立开关，默认关闭，旧角色资产行为不变。
- `openMouthSprite`：透明张嘴局部图。推荐保留与默认立绘完全相同的整张透明画布，只在嘴部原坐标
  留下用于覆盖闭嘴的像素。
- `mouthDefaultExpressionOnly`：默认开启，只允许第一项默认表情使用口型；确认其他表情构图一致后
  可关闭，让同一张嘴部图覆盖全部表情。
- `mouthIntervalMin / mouthIntervalMax`：闭嘴/张嘴随机切换间隔，默认 0.08～0.16 秒。

工作区的 `speak.png`、`blink.png`、`blink01.png` 均为 1216×832；`speak.png` 是完整透明画布加
正确坐标的嘴部小区域，符合直接铺满角色 RectTransform 的对齐方式。素材继续保持为用户未提交
文件，本功能没有自动绑定到尚未明确对应的角色资产。

### 38.2 运行时叠加与特效同步

新增 `VNCharacterMouth.cs`：

- `VNStage.CreateCharacter()` 在运行时角色下创建 `MouthOverlay` 子 Image，四边锚定铺满完整立绘；
  闭嘴时禁用该 Image，张嘴时启用，不需要计算嘴巴局部坐标。
- 嘴部 Image 与角色主 Image 共用 `VNImageEffectController.Mat`，因此溶解、明暗、高亮、闪光、
  色调匹配等 Shader 参数一致；角色移动、悬浮、点头、摇头和镜头缩放由父节点自然同步。
- DOTween 随机延迟在闭嘴/张嘴之间切换，并使用缩放时间；暂停菜单期间不会继续跳帧。
- 表情切换前先强制闭嘴，交叉溶解结束后按新表情规则决定是否继续口型；完整立绘眨眼只替换
  主 Image 的 Sprite，嘴部子层保持独立，因此可以出现“闭眼说话”。
- 张嘴图与默认立绘宽高比或 Pivot 不一致时输出警告，避免透明画布被拉伸后嘴部错位。

### 38.3 对白、语音与强制闭嘴生命周期

- `VNAudio.PlayVoice()` 改为返回是否成功播放，并公开 `IsVoicePlaying`。
- `VNScriptRunner` 将成功的 `voice` 命令一次性绑定到下一句 `say`；项目原有写法
  `voice v02` 后接角色台词无需新增剧本语法。
- 有语音的台词在“打字机仍运行或语音仍播放”期间保持口型，两者都结束后闭嘴；玩家提前显示全文
  但语音未结束时仍继续口型。没有语音时只跟随打字机，文字显示完立即闭嘴。
- 新台词开始前先关闭所有旧说话者；旁白、切换说话者、角色退场、清空舞台、读档/调试重建、
  停止剧本和剧本自然结束都会调用 `VNStage.StopSpeaking()`，保证不会遗留张嘴状态。

### 38.4 角色视觉预览与验证

`VNCharacterVisualPreviewWindow` 草稿新增口型开关、张嘴图、默认表情限制与随机间隔；舞台区域可
同时勾选闭眼和张嘴，检查两个系统组合后的对齐。缺图或宽高比/Pivot 不一致会在确认写入资产前
提示，所有数值仍遵循“确认写入角色资产”后才保存的安全流程。

验证：当前打开的 Unity 未立即刷新新增脚本到生成的 csproj，因此临时加入本地编译清单验证后
立刻还原；`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` 为 **0 error**。
现有 Unity/.NET 引用和旧接口产生原有 warning；目标文件 `git diff --check` 通过。

## 三十九、视觉小说脚本系统下一阶段改进分析（2026-07-16）

本轮按用户要求只做现状审计、功能构想与路线规划，没有实现运行时代码、没有修改场景或资产，也没有
触碰工作区中原有的资源删除与其他未提交改动。

### 39.1 实际检查范围

- 阅读 `CLAUDE.md`、`HowToUse.md` 与 `WhatAiDo.md`，确认项目约定、已完成功能和历史决策。
- 检查 `VNScriptParser`、`VNScriptRunner`、`VNStage`、`VNFlags`、`VNSaveSystem`、`VNAudio`、
  `VNCharacterDef`、对话/回想/配置/快捷工具条，以及剧本编辑器的 Schema、文档模型和校验器。
- 检查 `Assets/Scenarios` 的当前章节组织、Packages、Unity 版本、输入方式、设置持久化、测试痕迹和
  核心脚本体量；确认当前没有 VN 专用自动化测试与 asmdef 分层。
- 保留用户现有脏工作区不动；本轮唯一写入是本节分析记录。

### 39.2 现状判断

系统已经具备纯文本 DSL、同步/异步命令、对白、角色/背景/镜头/特效/音频、章节、分支与整数 flag、
20 槽存读档、回想、自动/快进、角色头像/眨眼/口型、从选中行播放、可视化编辑和静态校验，已经超过
“对白播放器”阶段。下一阶段的主要问题不是继续堆单个演出命令，而是建立可规模化制作的基础：

- 命令知识目前分布于 Parser 关键字、Runner switch、Editor Schema/校验等位置，新增命令容易漏改。
- `VNScriptRunner`、`VNStage` 和几个 EditorWindow 体量已经较大，输入、流程、存档和 UI 生命周期耦合。
- 状态只有静态全局整数 flag，难以自然表达字符串、浮点、角色属性、局部变量与周目继承。
- 存档按命令下标恢复，缺少格式版本、剧本版本/稳定节点 id、迁移、损坏校验和原子写入。
- 缺少“已读文本”体系、只跳已读、自动存档、快速存读、逐句回滚等视觉小说核心体验。
- 输入以键盘鼠标轮询为主；手柄、触屏、按键重绑定、安全区、无障碍和本地化尚未成为系统能力。
- 没有针对解析、分支可达性、快照往返、存档兼容和命令执行的自动化测试/CI 门禁。

### 39.3 建议的优先路线

1. **P0 工程地基**：统一命令注册表、编译后的 `VNProgram` 与 source map、可取消执行上下文、类型化状态、
   存档版本与迁移、Parser/Validator/状态快照自动化测试。
2. **P1 制作效率与核心体验**：条件选项、已读/未读与只跳已读、自动/快速存档、回滚、台词内嵌标记、
   子程序/参数/文件引用、跨文件流程图与引用查找。
3. **P2 内容与发行能力**：本地化、Addressables/异步资源加载、语音表、CG/音乐/结局/成就/词典、
   标题与章节选择、完整设置与无障碍、手柄/触屏。
4. **P3 高级演出与运营**：Timeline 作为名场面扩展口、Live2D/Spine/分层立绘适配、音素口型、
   可选埋点与自动剧情遍历、模组包、云存档/跨设备迁移。

### 39.4 分视角功能池

- **编剧/叙事**：条件显示或禁用选项、选项限时与默认项、字符串/布尔/浮点变量、局部与持久变量、
  变量插值、随机与权重分支、调用/返回、带参数段落、宏、include、一次性选项、关系值变化提示、
  旁白样式、注释标签、章节元数据、结局定义、周目条件、路线地图和剧情统计。
- **导演/演出**：台词内 `{wait}`、`{speed}`、`{shake}`、`{voice}` 等标记；演出预设资产；多轨并行与
  join；镜头构图安全框；焦点/景深/遮罩；角色层级、前后景和多角色站位；Timeline/Playable 桥接；
  可中断转场；慢动作、冻结帧、回忆滤镜、视频/序列帧、UI 自定义演出。
- **美术/角色**：分层立绘、服装/姿势/脸/嘴/眼组合，差分继承，Live2D/Spine 适配，角色主题色与
  对话框皮肤，背景变体，CG 解锁，素材规范校验、Pivot/尺寸批处理和预加载预算报告。
- **声音/配音**：角色语音音量、语音回放键、台词到音频表、缺失语音报告、语音长度驱动自动模式、
  cue/字幕时间轴、音素或振幅口型、环境声分组、总线/快照、淡入淡出曲线、语音缓存和配音导出清单。
- **玩家体验**：已读追踪、只跳已读/全部跳过策略、自动存档、快速存读、逐句回滚、选择历史、
  语音回放、对话框透明度、字体/字号/行距、窗口/全屏/分辨率、按键重绑定、标题菜单、章节选择、
  路线图、结局列表、CG/音乐鉴赏、成就、人物词典、术语词典和周目继承。
- **无障碍**：高对比度/色盲配色、减少闪烁/震动/视差/动态模糊、转场强度、字幕背景与说话者标识、
  屏幕阅读语义、键盘全导航、大点击区域、长按替代连点、自动推进上限、音频视觉提示和安全区。
- **本地化**：稳定台词 id、文本与演出分离、语言表导入导出、角色名变体、复数/性别/语序参数、
  字体回退、RTL、禁则/断行、文本溢出扫描、翻译上下文截图、配音语言切换和语言独立存档定位。
- **编辑器/团队**：跨章节搜索、跳转定义/查找引用、安全重命名、流程图、不可达标签/死循环/无出口
  检测、变量读写表、资源依赖报告、批量改角色/背景/音频 id、拼写与标点规范、行级书签/待办、
  双栏文本与表单、场景预览、剧情模拟器、分支覆盖率、导出审校稿、Git 友好稳定格式和冲突提示。
- **架构**：命令描述/解析/校验/执行/状态归约单一来源；`VNProgram` 中间表示与缓存；`VNContext`、
  `IVNCommandHandler` 和可取消 `VNExecutionHandle`；输入、UI、存档、音频和舞台服务拆分；事件总线；
  确定性随机种子；错误恢复策略；运行时/编辑器 asmdef；依赖注入与可测试时钟。
- **存档与安全**：格式版本、项目/剧本 hash、稳定 checkpoint id、迁移链、原子写入、备份、校验和、
  损坏恢复、存档删除/覆盖确认、自动/快速/手动槽分区、缩略图生命周期、跨版本兼容和可选轻度混淆。
- **性能/平台**：Addressables、按章依赖预取、LRU 贴图/语音缓存、SpriteAtlas、对象池、异步解码、
  内存预算、低配特效档、移动端安全区/触控手势、手柄导航、分辨率/DPI 适配和加载进度反馈。
- **测试/发布**：Parser 与条件表达式单测、存档往返与旧版本迁移测试、无 Unity 画面的剧情模拟、
  全分支自动遍历、截图回归、资源缺失/文本溢出扫描、性能采样、构建前校验、CI 报告和可选匿名埋点。
- **扩展/生态**：自定义命令插件接口、外部表格/CSV/JSON 导入、Timeline 桥、模组 manifest、沙箱限制、
  剧本包版本/依赖、事件回调给任务/战斗/好感系统，以及 AI 辅助校对但必须保留人工确认与变更差异。

### 39.5 重要取舍

- 继续以 `.vn.txt` 为唯一真相；流程图和表单应是同一文档的不同视图，不另造第二份不可同步的数据。
- 不建议此时把现有系统整体迁回 Pixel Crushers；应先稳定自研 DSL，只在确有任务/数据库需求时做桥接。
- Timeline 适合作为少量名场面的“逃生口”，不适合替代普通对白与分支执行器。
- Addressables、本地化和存档版本要在内容大量增长前完成，否则后期迁移成本会显著提高。
- 存档加密只能阻止随手修改，不能保护真正机密；优先级应低于版本迁移、原子写入与损坏恢复。
- 第一批实现建议控制为：统一命令注册与测试地基 → 存档稳定 id/版本 → 已读/自动存档/回滚 →
  条件选项与类型化状态。它们会同时提升玩家体验、后续功能速度和长期可靠性。

## 三十九、剧本系统改进方向全面梳理（2026-07-16，纯分析，无代码改动）

> 本章为头脑风暴/规划会话记录：用户要求从尽可能多的角度分析剧本系统还能加什么改进与
> 功能。本次会话只阅读了 `VNScriptParser.cs`、`Assets/Scenarios/` 与既有章节记录，
> **没有修改任何代码**；完整分析已在对话中给出，此处存档要点，供后续排期时查阅。

### 39.1 现状基线

- 解析器支持 26 个关键字（bg/show/hide/emote/wait/camera/shake/weather/mood/fx/sakura/
  transition/reset/label/jump/flag/if/choice/chapter/move/bgm/se/voice/volume/camseq/
  camcut/camto/portrait），外加 say 行、`*` 选项行、`>` 路径点行、行尾 `@` 异步。
- P0/P1/P2 完成；P3（台词内嵌标记 + VNDirector）为既定路线图下一步。

### 39.2 十大方向要点（细节见本次对话）

1. **DSL 语法层**：台词内嵌标记 `{w}{speed}{shake}{color}` 等；变量插值 `{好感度}`；
   VNFlags 扩展字符串/布尔与复合表达式；`call/return` 子程序；`macro` 宏；`include`
   多文件；`random` 随机分支；选项增强（已选置灰 / 条件显示 / 限时）。
2. **演出层**：VNDirector 名场面组合命令（回忆/噩梦/告白/闪回）；通用立绘叠加层
   `overlay`（红晕/汗珠/怒气符号，推广眨眼口型的现成机制）；CG 事件图命令 `cg`；
   NVL 全屏文本模式与心声/电话等对话框样式；glitch/老胶片等新屏幕特效；
   说话者自动微推镜头。
3. **音频层**：BGM 交叉淡化与 `bgm queue`；多轨环境音分层；按角色音色的打字机 blip；
   语音、BGM、SE 独立音量总线。
4. **玩家系统层**：已读文本标记 + 仅跳过已读；Q.Save/Q.Load 单键；选择前/章节自动存档；
   CG·音乐·场景三合一鉴赏室；成就；多周目 New Game+；结局流程图；标题主菜单场景生成器。
5. **编辑器工具链**：分支流程图节点视图；Lint 面板（未定义引用/死标签/不可达行）；
   运行时 Flags 监视与断点单步；剧本统计（字数/分支覆盖/时长估算）；CSV 导入导出。
6. **架构工程层**：剧本热重载；下一句资源预加载；存档版本迁移字段；headless 全分支
   自动跑测（CI）；缺资源占位容错。
7. **本地化**：文本表抽离、多语言切换、字体回退链。
8. **叙事玩法扩展**：好感度可视化、日期/章节标题卡、手机短信界面演出、地点选择自由
   行动、TIPS 词典高亮、玩家改名、迷你游戏钩子命令。
9. **移动端/平台**：触屏手势、安全区适配、Steam 成就云存档接口预留。
10. **AI 协作友好**：剧本草稿→DSL 转换约定、素材命名映射自动生成、宣传视频录制模式。

### 39.3 优先级建议（结论）

1. 第一梯队（体验刚需、工作量小）：已读跳过、Q.Save/Q.Load、选择前自动存档、
   BGM 交叉淡化、Lint 基础检查。
2. 第二梯队（既定路线图）：P3 台词内嵌标记 + VNDirector；通用 `overlay` 叠加层；
   CG 命令与鉴赏室（存档系统已有截图基建可复用）。
3. 第三梯队（内容量上来后）：分支流程图、多周目/成就、本地化文本表、headless 跑测。
4. 远期：Live2D 接口、手机界面演出、迷你游戏钩子。

## 四十、音频库分通道管理 + 每素材基准音量 + 剧本 vol 参数（2026-07-16，分支 `agent/audio-volume-library`）

### 40.1 需求与方案

用户痛点：素材响度不齐（有的 SE 特别响、有的特别小），但只有一个全局 SE 音量可调；
且 BGM/SE/语音全部混在 `VNAudio.library` 一个列表里不好管理。方案（对话中比较过
四种后选定）：**库内基准音量标定为主 + 剧本 `vol:` 参数为辅**，同时把音频库拆成
三个通道专属库。最终音量公式：

```
实际音量 = 条目基准音量(库里标定一次) × 剧本 vol 参数(默认1) × 通道音量(玩家设置)
```

### 40.2 `VNAudio.cs` 重构

- `AudioEntry` 新增 `[Range(0,1)] volume = 1`：素材基准音量。范围上限取 1 而不是 2，
  因为 Unity `AudioSource.volume` / `PlayOneShot volumeScale` 上限就是 1，无法放大素材
  本身——偏响的往下调，整体以最安静的素材为基准。
- 新增 `bgmLibrary / seLibrary / voiceLibrary` 三个通道库；**旧 `library` 字段保留为
  兼容混合库**（三个通道都能查到里面的条目），旧场景序列化数据不丢，可逐步迁移。
  查找顺序：通道专属库 → 旧混合库 → 告警（告警文案指明应登记到哪个库）。
- `PlayBgm/PlaySe/PlayVoice` 均新增 `vol` 参数（Clamp01）。增益记录方式：
  - BGM：`_currentBgmGain`（基准×vol）并入 `EffectiveBgmVolume`，与语音压低 BGM 机制
    （duck）相乘共存；`_currentBgmScriptVol` 单独记录剧本 vol 供存档。
  - 循环 SE：字典值从 `AudioSource` 改为 `LoopingSe { source, gain }`，
    `SetVolume("se")` 全局改音量时按 `新通道音量 × gain` 重算每个循环源
    （旧实现是直接覆盖成通道音量，会抹掉个体差异）。
  - 语音：`_currentVoiceGain`，`SetVolume("voice")` 同样保留增益。
- `ResetForDebug()` 清理循环 SE 时适配新字典结构，并重置全部增益。

### 40.3 剧本语法与运行时

- `bgm play 黄昏之歌 fade:2 vol:0.6` / `se 爆炸 vol:0.4` / `se 雨声 loop vol:0.5` /
  `voice v02 vol:0.8`——`vol:` 全部可省略（默认 1），旧剧本零改动。
- 存档：`VNSaveData` 新增 `bgmVol = 1f`（JsonUtility 读旧档时字段缺失保持默认 1），
  `VNStage.CaptureSnapshot/RestoreSnapshot` 存取 BGM 的剧本 vol；条目基准音量在资产上，
  读档时自然重新生效。循环 SE 本来就不进真实存档，行为不变。
- 「从选中行播放」调试重建：`loopingSe` 从 `HashSet<string>` 改为 `id → vol` 字典，
  重放循环 SE 时带上 vol；BGM 状态同样捕获 vol。

### 40.4 剧本可视化编辑器同步

- `VNParamSource.Audio` 拆为 `AudioBgm / AudioSe / AudioVoice` 三个来源；
  bgm/se/voice 三条命令的 id 下拉只显示对应通道库（并入旧混合库的条目，去重），
  校验错误信息也分别指向 `VNAudio.bgmLibrary/seLibrary/voiceLibrary`。
- 三条命令的模式定义都加了 `vol` kwarg（Number，默认 1，等于默认值时生成文本自动省略）。
- `se` 的 id 下拉仍保留首项 `stop`。

### 40.5 迁移说明（用户操作）

打开 `VNScriptDemo.unity` 场景选中 `VNAudio` 物体：旧条目仍在 `Library`（兼容混合库）里，
一切照常工作；建议逐步把条目剪切到 Bgm/Se/Voice Library 对应列表，顺手把每条的
Volume 滑杆按素材实际响度标定。之后新素材直接登记进对应通道库即可。

验证：`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` **0 error**
（连带编译运行时程序集）；全局搜索确认旧 `audioIds/HasAudio/VNParamSource.Audio`
引用已全部清理。

## 四十一、任务/地图/战斗/迷你游戏事件接口架构规划（2026-07-16，纯规划，无代码改动）

> 用户提出想给剧本系统加任务、地图、战斗、迷你游戏，询问最佳架构。本章为方案存档，
> 完整分析在对话中；实施前以本章为准归档设计决策。

### 41.1 核心结论

四个需求本质是两类：
1. **任务 = 持久状态 + 展示 UI**：状态完全落在 VNFlags（整型阶段号），任务定义资产只管
   显示文案 → 存档/分支/调试重建全部免费复用现有设施。
2. **地图/战斗/迷你游戏 = 「暂停剧本 → 玩家交互 → 带结果返回」的外部事件**：统一成
   一个通用事件模块接口 `VNEventModule`，剧本用一条 `event` 命令 + `*` 结果行调起，
   结果名映射跳转标签（完全复刻 choice 的等待与分支模式，ChoiceCo 轮询回调即是先例）。

### 41.2 通用事件接口（P1，最优先）

- DSL：`event <模块id> [key:value…]` + `* 结果名 [flag:op] [-> 标签]` 附属行；
  解析器把 `*` 行复用 choice 的选项解析路径。结果另写入 flag `事件结果`。
- 运行时：`VNEventModule`（抽象 MonoBehaviour）：`Launch(VNEventContext, Action<string> onDone)`；
  `VNEventRegistry`（场景组件，id → 预制体列表）实例化到独立 EventLayer
  （sortingOrder ≈ 60，ChoicePanel 45 与 ScreenTransition 100 之间）。
- Runner `EventCo`：关 Skip（SetSkip(false)，同 choice）、锁对话推进输入、隐藏对话框 →
  实例化模块轮询结果 → 销毁模块、恢复 UI → 应用 flag → 跳标签。
- 边界约定：事件期间禁止存档（沿用"仅台词处可存"天然成立）；事件不进回想；
  调试重建把 event 视为分支（同 choice 警告，不重放）；DOTween.timeScale 快进对
  模块内动画的影响由模块用 unscaledTime 或事件期间强制关快进解决。

### 41.3 各系统要点

- **任务（P2）**：`quest start/stage/done/fail <id> [阶段]` 全部翻译成 flag 写入 +
  VNToast 通知；`VNQuestDef` ScriptableObject（id/标题/各阶段文案）；J 键任务日志面板
  从 flags 反查状态渲染。
- **地图（P3）**：作为第一个"正经"内置事件模块 `VNMapModule`：全屏地图底图 + 可点击
  地点标记（支持 if:flag 条件显隐、去过标记），点击地点 = 返回该地点结果名 → 跳标签。
  用 event 语法不新增专用命令。
- **战斗/迷你游戏（P4+）**：系统只提供接口 + 两个示例模块：QTE 连打条（验证管线，
  P1 一起交付）与回合制小战斗（HP/攻击从 flags 读，结果 win/lose，P4）。

### 41.4 备选方案与否决理由

- 每个系统一条硬编码命令（battle/map 各自实现等待逻辑）：×，重复造轮子且加游戏类型要改 Runner。
- Pixel Crushers 任务模块：×，剧本选型已定自研 DSL，混用两套状态存档对不上。
- 事件模块用 additive scene 承载：暂缓，先用同场景覆盖层预制体（舞台状态保活、无异步
  加载复杂度），接口留 `IsSceneBased` 扩展位，重型玩法再补。

### 41.5 分期实施（每期一个分支）

1. `agent/vn-event-interface`：parser + Runner + 接口/注册表 + QTE 示例 + 编辑器 schema/lint。
2. `agent/vn-quest-system`：quest 命令 + VNQuestDef + 日志面板 + Toast。
3. `agent/vn-map-module`：地图模块 + 条件地点 + 演示地图。
4. `agent/vn-battle-sample`：回合制示例战斗模块。

## 四十二、玩法事件接口 P1：event 命令 + 模块注册表 + QTE 示例（2026-07-16，分支 `agent/vn-event-interface`）

实现第四十一章规划的 P1。剧本一条 `event <模块id>` 即可调起任意玩法模块
（地图/战斗/迷你游戏共用此接口），模块结束返回结果名，按 `*` 结果行分支。

### 42.1 剧本语法

```
event qte time:3 target:12 title:鼓起勇气连打！
* success flag:好感度+2 -> 告白成功
* fail -> 退缩线
```

- `*` 结果行完全复用 choice 的选项解析路径（结果名 = 选项文本，支持 flag 操作与跳转）；
  不写结果行 = 顺序继续。整数结果同时写入 flag「事件结果」，供 `if` 判断。
- kwargs 原样传给模块（`VNEventContext.Kw/KwF/KwI` 读取），模块各自定义参数。

### 42.2 新文件（Script/）

| 文件 | 职责 |
|---|---|
| VNEventModule.cs | 模块基类 + VNEventContext。`Launch(ctx, onDone)` → 子类 `OnLaunch` 搭 UI → `Done(结果名)`（只回调一次）；`CancelForDebug()` 中断清理钩子 |
| VNEventRegistry.cs | id → 模块模板（预制体或场景内禁用模板）。实例化到运行时创建的 EventLayer（Canvas overrideSorting 60，位于 ChoicePanel 45 与 ScreenTransition 100 之间，进出事件可用全屏转场包裹） |
| VNQteModule.cs | 示例：QTE 连打条（限时点击/空格达标）。UI 全程序化（RoundedRectSprite 面板+进度条），参数 time/target/title，结果 success/fail |

### 42.3 Runner 集成与边界处理

- `EventCo`：关 Skip/Auto（同 choice 必停）→ `_eventActive = true` + 隐藏对话框 →
  实例化模块轮询结果 → 销毁模块、恢复对话框 → 记 Backlog「事件」→ 应用结果行。
- `_eventActive` 期间 `Update()` 直接 return：F5/A/S/H/隐藏 UI 全部快捷键交给模块；
  存档被现有「仅台词处可存」天然挡住。
- `Stop()` 新增 `CleanupActiveEvent()`：调试停止/读档中断时销毁残留模块并恢复对话框。
- 调试重建把 `event` 与 choice/jump/if 同列为分支点（不重放事件，警告提示）。
- 模块约定：只操作自己的 UI 子树与 VNFlags；计时用 unscaledTime、Tween 用
  `SetUpdate(true)`，不受快进 `DOTween.timeScale` 影响（QTE 已按此实现）。

### 42.4 场景生成器与演示剧本

- Create Script Demo Scene 现在创建 `VNEventRegistry` 物体 + 禁用状态的 QteTemplate 子物体，
  登记 id=`qte` 并连线 `stage.eventRegistry`（VNStage 新增该字段）。
- 演示剧本（Demo.vn.txt 重新生成时）：告白线插入 QTE——成功才 `flag:好感度+2` 进好结局，
  失败落回退缩线；语法速查头部加 event 说明。**需重建剧本演示场景并删除旧 Demo.vn.txt
  后重新生成才能体验。**

### 42.5 剧本编辑器同步

- `VNParamSource.EventId` 新来源：id 下拉/校验取自场景 `VNEventRegistry.modules`。
- event 命令登记进 schema（Flow 分类，中文名「事件」，补了漏登记的 chapter「章节」），
  标记 `blockChoice = true`——编辑器的 `*` 行编辑 UI、`SourceLineForRow` 行号换算、
  文本往返全部通用复用，choice 相关代码零特判新增。
- 校验：event 的模块 id 不在注册表报 Err；event 允许无结果行（与 choice 必须有选项不同）。

验证：临时把三个新脚本加入 `Assembly-CSharp.csproj` 编译清单（Unity 未刷新 csproj），
`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` **0 error** 后立刻还原。
.meta 按既有格式手写（随机 GUID）。

## 四十三、任务系统 P2：quest 命令 + 任务定义资产 + J 键日志面板（2026-07-16，分支 `agent/vn-quest-system`）

实现第四十一章规划的 P2。核心设计：**任务状态全部落在 VNFlags**（flag 名 =
`任务_<id>`），存档、`if 任务_xx>=2` 分支、调试重建全部复用现有设施零改动；
组件只负责写状态时弹 Toast 与渲染日志。

### 43.1 剧本语法与阶段约定

```
quest start 告白大作战        # 阶段 1 + Toast「新任务：…」（可 quest start id 2 从阶段 2 起）
quest stage 告白大作战 2      # 推进阶段 + Toast 显示该阶段文案
quest done 告白大作战         # = 100 + Toast「任务完成」
quest fail 告白大作战         # = -1  + Toast「任务失败」
```

阶段号约定：0 未接取 / 1..n 进行中 / 100 完成 / -1 失败（`VNQuestLog` 常量）。

### 43.2 新文件（Script/）

| 文件 | 职责 |
|---|---|
| VNQuestDef.cs | ScriptableObject（CreateAssetMenu「VN/Quest Definition」）：id/标题/描述/各阶段文案。**纯显示文案**，与状态分离；没有资产的任务照常运作（id 当标题） |
| VNQuestLog.cs | 场景组件：定义资产列表 + `Apply(op,id,stage,silent,line)` 执行命令（写 flag + VNToast）+ J 键日志面板。面板全程序化（Overlay Canvas 600，与回想同构：暗底点击关、滚动列表），进行中/已完成/已失败三栏分色，进行中显示「▶ 当前阶段文案」；无定义资产的活动 `任务_` flag 也会兜底显示 |

### 43.3 集成点

- Runner：Start 查找/自建 VNQuestLog；`quest` 命令分发；J 键开关日志（互斥逻辑与
  回想面板同构，打开期间不推进剧情、Esc 可关）；`RequestQuestLog()` 公开给工具条。
- 调试重建：`case "quest"` 静默重放（silent=true 只写状态不弹 Toast），
  从中间行播放时任务状态正确。
- 快捷功能条：Log 和 Config 之间新增「任务」按钮，工具条总宽 616→693。
- 场景生成器：创建 VNQuestLog + 示例任务资产 `Assets/VNEffects/Quests/告白大作战.asset`
  （两阶段文案）；演示剧本插入完整任务线（开场 start → 心跳处 stage 2 →
  告白成功 done / 退缩线 fail）；提示文字加「J 任务日志」。
- 编辑器：`VNParamSource.QuestId`（扫项目 VNQuestDef 资产做下拉），quest 命令入
  schema（中文名「任务」）；校验用 **Warn 而非 Err**——无定义资产是合法用法，
  只提醒缺标题/阶段文案。

验证：两个新脚本临时加入 csproj 后 `dotnet build Assembly-CSharp-Editor.csproj`
**0 error**，随即还原。体验需重建剧本演示场景（并删除旧 Demo.vn.txt 重新生成）。

## 四十四、地图模块 P3：event 接口上的自由行动地点选择（2026-07-16，分支 `agent/vn-map-module`）

实现第四十一章规划的 P3。`VNMapModule` 是事件接口的第一个"正式"内置模块，
验证了接口的通用性：**Runner 与解析器零改动**（只给 VNEventContext 加了一个
通用能力），新玩法 = 新模块类 + 注册表登记一行。

### 44.1 剧本用法

```
event map title:夜晚去哪里走走？ [bg:背景id]
* 教室 -> 教室夜话
* 图书馆 -> 图书馆夜话
* 天台 -> 天台夜话          ← 模板里天台配了 好感度>=2，不满足自动隐藏
```

### 44.2 设计要点

- **地点配置在模块模板 Inspector**（`Location`：名字/归一化坐标/显示条件/可选图标）——
  坐标是视觉属性，属于编辑器不属于剧本文本。
- **双重过滤**决定本次显示哪些地点：①`condition`（VNFlags 表达式）不满足 → 隐藏；
  ②剧本「* 结果行」没接住的地点 → 隐藏。为此 `VNEventContext` 新增 `outcomes`
  列表与 `AcceptsOutcome()`（Runner 从 cmd.options 填充）——这是通用机制，
  任何模块都能据此只开放当前剧情接得住的分支；无结果行 = 全部放行。
- 全部可用地点为空时告警并立即 `Done("")`，防止软锁。
- 选中自动 `flag 去过_<地点> +1`（`markVisited` 可关）；已去过的标记显示 ✓ 并变绿。
- 底图：模板 `mapSprite`，剧本 `bg:<背景id>` 可临时换用舞台背景库的图；都没有时
  程序化圆角深色面板兜底。底图**不保比例铺满**定位区，保证归一化坐标与画面对应。
- 演出：标记 = 程序化光晕 + 中心亮点呼吸脉动 + 描边地点名，错开弹入（OutBack）、
  悬停放大（内嵌 `MarkerHover` 指针事件组件）、点中 Punch 后返回结果；
  全部 Tween `SetUpdate(true)` + `SetLink`，遵守事件模块约定。

### 44.3 生成器与演示

- MapTemplate 登记为 id=`map`：教室(0.28,0.55)/图书馆(0.68,0.6)/天台(0.5,0.82，
  条件 好感度>=2)，底图用演示背景图。
- 演示剧本夜晚段插入自由行动：三个地点各自一小段夜话后汇合到「结算」再判结局；
  告白成功（好感度≥2）时天台才会出现——一次演示条件地点 + 事件分支 + 去过标记。

验证：`dotnet build Assembly-CSharp-Editor.csproj` **0 error**（Unity 已把 P1/P2 脚本
刷进 csproj，本次只临时补 VNMapModule 一项后还原）。

## 四十五、架构灵活性评审存档 + 项目文档大更新（2026-07-16，分支 `agent/project-code-guide`）

### 45.1 架构灵活性评审（用户问：3D 小游戏/改地图玩法/日历系统能否灵活对应）

结论存档（完整分析见当次对话）：

- **总评**：核心选型（结果契约 + flags 总线 + 注册表）三类扩展都成立。
  设计把"会变的"（玩法内容）与"不变的"（剧本契约、状态总线）分对了边。
- **改地图玩法 ★★★**：逻辑全封在 VNMapModule 一个类，改玩法不碰其他系统；
  唯一限制是地点静态配置在 Inspector，动态地点源需加代码。
- **日历/日期 ★★☆**：不该做事件模块，应照抄任务系统模式（flags 存日期/时段 +
  VNCalendar 展示组件 + date 命令）；日循环骨架现有机制可拼。两个坑：
  VNFlags 无取模（星期几要手动维护）、无台词的日循环没有存档点（需 savepoint）。
- **3D 小游戏 ★★☆**：剧本契约成立（模块内部实现剧本不感知），承载层要补——
  轻量 3D 今天可用 RenderTexture 方案；重型需给注册表加 additive 场景模式 +
  异步加载（中等工作量、不破坏现有剧本）。当前假设冲突：模块实例化在 Canvas 下、
  同步 Instantiate、销毁只清模块物体、相机管理缺位。
- **跨场景短板**（记为技术债）：事件中不可存档；结果名精确匹配无静态校验；
  事件内不能调对话系统；日历类重事件结构下"从中间行播放"推断会退化
  （应转向"从存档快照启动调试"）。
- **建议动工顺序**：先 VNCalendar（小而独立），3D 承载模式等第一个真实需求出现再补。

### 45.2 本批：三份项目文档

1. **新建 `ProjectCodeGuide.md`**（项目根目录）：逐脚本代码指南，62 个代码文件
   全覆盖（运行时 Script/ 20 + 特效根目录 32 + 编辑器 6 + Shader 4）。结构：
   三层架构大图景 → 一次台词的完整数据流 walkthrough → 剧本层/舞台层/音频/
   玩法扩展层/系统 UI/演出组件库（按 6 类分组）/编辑器/Shader 逐文件详解
   （职责/关键 API/扩展点/维护注意）→ 六份常见任务菜谱（加命令 7 步法/写事件
   模块/登记音频/加任务/加特效/调试）→ 全局约定 + 坑清单 + 技术债表（含出处章节）。
2. **更新 `CLAUDE.md`**：头部加 ProjectCodeGuide 指引；剧本系统状态补齐
   音频三库+vol、事件接口（event/qte/map）、任务系统（quest/J 日志）与各自
   关键约定；路线图更新（下一步 P3 内嵌标记 + VNDirector，战斗 P4 待动工）；
   组件速查表补 8 个新组件行（眨眼/口型/事件四件套/任务两件套）。
3. **本章**（WhatAiDo 四十五）：评审结论 + 文档批次存档。

维护约定：以后每完成一批功能，除 WhatAiDo 记录外，若涉及新脚本/新命令/
新约定，应同步 ProjectCodeGuide 对应小节与菜谱（它是"现状"文档，
WhatAiDo 是"历史"文档，CLAUDE.md 是"给 AI 的工作规则"）。

## 四十六、视觉与演出美化方向全面梳理（2026-07-17，纯分析，无代码改动）

> 用户要求从尽可能多的角度提出让画面/演出更美更有吸引力的想法。完整分析
> （约 50 条，每条标注依托的现有基建与成本）在当次对话中，此处存要点与优先级。

### 46.1 七大方向

1. **立绘表现力**：通用 overlay 叠加层（红晕/汗珠/漫画情绪符号弹出，蓝本=眨眼口型
   的透明画布叠加）、剪影登场、雨天湿身波光联动、眼神高光、方向性轮廓光、
   说话者微透视、下摆/头发顶点摆动（VNImageEffect 波浪参数按高度加权）。
2. **背景氛围**：背景 Ken Burns 缓慢漂移（防呆板，成本极低感知极高）、
   同背景早/昼/黄昏/夜四段调色 + godrays 角度联动、雷雨组合演出（闪电+延迟雷声）、
   季节一键预设（粒子+色调+环境音组合）、前景 bokeh 光斑层、飞鸟/流星点缀。
3. **文字演出**：P3 内嵌标记（既定）、标点自动停顿节奏、情绪字体（颤抖/弹出）、
   对话框皮肤系统（心声/回忆/电话/系统）、关键词高亮、选项倒计时环。
4. **转场镜头**：章节标题卡（竖排+印章）、回忆转场组合（白闪+柔焦+去饱和，
   即 VNDirector 素材）、rack focus 双立绘虚实交替、手持镜头 perlin 微漂移、
   漫画速度线/集中线、电影 letterbox 黑边、闪回快切序列、分屏对峙。
5. **UI 系统美化**：动态标题画面（39 章第 26 条前置）、存读档界面章节色条、
   自定义光标、Toast 图标横幅、成就弹窗演出。
6. **角色互动**：双人组合演出预设（靠近/对视）、好感度距离微调、头顶碎语气泡。
7. **技术底层**：sprite 法线假光照、全屏情绪水波、CRT/胶片颗粒滤镜、
   2D 骨骼/Live2D（远期大工程）。

### 46.2 优先级结论

- **第一梯队（低成本高感知，先做）**：背景 Ken Burns、标点停顿节奏、章节标题卡、
  对话框皮肤、通用 overlay 叠加层、时间段调色。
- **第二梯队（路线图既定+组合技）**：P3 内嵌标记 + VNDirector（回忆/雷雨/季节
  都封装成 director 一行命令）、速度线、letterbox、手持微漂移。
- **第三梯队**：rack focus、分屏、双人演出预设、动态标题画面。
- **远期**：2D 骨骼/Live2D、法线假光照。
- 关键判断：项目特效"单件"已足够多，**下一阶段的美感提升主要来自"组合与
  节奏"**（VNDirector 把单件编排成名场面）而非继续堆新单件。

## 四十七、漫画速度线/集中线 overlay（2026-07-17，分支 `agent/manga-speed-lines`）

> 四十六章第二梯队清单落地第一件：全屏放射集中线，程序化贴图 + 闪帧动画，零美术资源。

### 47.1 计划

- 程序化生成漫画集中线贴图（从边缘向中心收拢的楔形放射线），多变体轮换实现
  手绘"闪化"效果；做成常驻 overlay 组件接入 fx 开关体系，另提供一次性冲击 API。

### 47.2 文件说明

- **`VNProceduralTextures.cs`（改）**：
  - 新增 `SpeedLines(int variant)`（512px，缓存 3 个变体，`SpeedLineVariantCount = 3`）。
    算法：极坐标分 110 个扇区，每扇区用整数散列 `Hash01` 决定"是否有线/线宽/内端半径"，
    线条为楔形（外缘宽、向中心收成尖），中心 r<0.12 恒留白，三成扇区留空 → 疏密不均。
    不同 variant 换随机种子 → 三张完全不同的线条分布，轮换即逐帧闪化。
  - 新增 `Hash01(int)`：贴图生成期的确定性伪随机（同种子结果稳定，重建场景不闪变）。
- **`VNSpeedLines.cs`（新）**：全屏集中线 overlay 组件。
  - 结构：自身嵌套 Canvas（overrideSorting，`sortingOrder = 25`：盖过粒子 10~31 与
    情绪泛光 20，低于对话框 40）+ CanvasGroup（淡入淡出/关闭时零开销）+
    子物体 RawImage（四边溢出 480×270px，旋转抖动不露边）。
  - 闪帧：`Update` 里每 `flickerInterval`（默认 0.09s）轮换贴图变体，同时随机
    ±4° 旋转 + 1~1.045 缩放抖动；alpha≈0 时整个 Update 直接短路。
  - 材质：VN/Additive 加法混合，`_TintColor = color × hdrIntensity`（HDR 配合 Bloom 辉光），
    走 `sourceMaterial` 私有字段惯例（生成器 AssignSourceMaterial 注入材质资产）。
  - API：`Show(fade)/Hide(fade)/Toggle()` 持续开关；`Burst(duration)` 一次性冲击
    （瞬间拉满 → 保持 → 0.25s 淡出）；全部 Tween `SetLink`。
- **`VNStage.cs`（改）**：新增 `speedLines` 字段 + AutoWire 自动查找；
  `ToggleFxNames` 加入 `"speedlines"`（存档快照/读档恢复/调试重建自动覆盖）；
  `Fx()` 新增 case：`fx speedlines on|off|burst`（burst 不记录开关状态）。
- **`VNEffectsDemoSetup.cs`（改）**：BuildStageRig 第 8.5 步创建 SpeedLines 物体
  （两个演示场景都有），连线 `stage.speedLines` 与 `demo.speedLines`；
  演示剧本头部语法速查补 `fx speedlines` 说明。
- **`VNEffectsDemo.cs`（改）**：`,` 键开关集中线、`.` 键 Burst 冲击，提示文字更新。
- **`VNScenarioSchema.cs`（改）**：FxNames 加 `speedlines`；fx 命令 value 候选加 `burst`。

### 47.3 技术决策

- **闪帧用"贴图变体轮换"而非旋转动画**：真实漫画集中线是逐帧重画的，线条分布
  完全变化；只旋转同一张贴图会露馅（线条相对关系不变）。3 张变体 + 微旋转抖动
  的组合成本低（一次性生成 3×512²）效果最接近手绘。
- **加法混合白线**（而非黑线）：VN 演出里集中线多用于"决断/惊愕/告白冲击"，
  白色加法线条在任何背景上都可见且带辉光冲击力；黑色线条需要普通混合且在
  暗背景失效。颜色仍暴露为 Inspector 字段可调。
- **接入 ToggleFxNames** 让存档/读档/编辑器"从选中行播放"的状态重建零改动
  自动支持（fx 关键字在 RebuildStateBefore 里本就是通用处理）。

## 四十八、电影 Letterbox 黑边 + 回忆自动联动（2026-07-17，分支 `agent/cinema-letterbox`）

> 四十六章第二梯队第二件：宽银幕黑边演出，独立剧本命令 + mood Memory 自动联动。

### 48.1 计划

- 上下两条纯黑横条从屏幕外滑入/滑出（DOTween）；新增一等剧本命令 `letterbox`；
  切到回忆色调（mood Memory）时自动上黑边、离开时自动撤掉；进存档/调试重建体系。

### 48.2 文件说明

- **`VNLetterbox.cs`（新）**：黑边组件。
  - 结构：嵌套 Canvas（`sortingOrder = 35`：盖过舞台/粒子/速度线 25，低于对话框 40）+
    两条 Image 黑条（锚定上/下边缘，pivot 贴边，横向左右各溢出 20px 防荷兰角/震动露缝）。
  - 动画：`DOAnchorPosY` 滑入（OutCubic）/滑出（InCubic），默认高 130px（≈2.35:1 宽银幕）、
    时长 0.7s，均可被参数覆盖；Tween 全部 `SetLink`。
  - API：`Show(height, duration)/Hide(duration)/Toggle()`、`IsShown`。
- **`VNStage.cs`（改）**：
  - 新增 `letterbox` 字段 + AutoWire；`ToggleFxNames` 加 `"letterbox"`（存档自动覆盖）。
  - 新增 `SetMood(VNMood, duration)` 包装：切色调 + 回忆自动黑边联动
    （`autoMemoryLetterbox` 开关，默认开）。自动上的黑边打 `_letterboxAuto` 标记，
    只有自动上的才会在离开 Memory 时自动撤；手动 letterbox/fx 命令会接管（清标记）。
  - 新增 `SetLetterbox(on, height, duration)`：letterbox 命令入口，写 `_fxStates`。
  - `Fx()` 加 case `letterbox`（`fx letterbox on|off` 同样可用，读档恢复走这里）。
  - `RestoreSnapshot`：恢复后若「回忆色调 + 黑边」同时成立则视为自动黑边。
- **`VNScriptRunner.cs`（改）**：
  - Dispatch 的 mood case 改走 `stage.SetMood`（联动入口统一）。
  - 新增 `letterbox on|off [height:130] [time:0.7]` 命令 case。
  - `RebuildStateBefore`（编辑器"从选中行播放"重建）：新增 letterbox 关键字重放 +
    mood 关键字里静默重放回忆自动黑边逻辑（与运行时一致），reset 时清标记。
- **`VNScriptParser.cs`（改）**：Keywords 加 `letterbox`。
- **`VNScenarioSchema.cs`（改）**：FX 分类新增 letterbox 命令（on/off + height/time kwargs）；
  FxNames 加 `letterbox`。
- **`VNScenarioEditorWindow.cs`（改）**：命令中文翻译表加「letterbox → 电影黑边」。
- **`VNEffectsDemoSetup.cs`（改）**：BuildStageRig 第 8.6 步创建 Letterbox 物体并连线
  stage/demo；演示剧本头部语法速查补 letterbox 说明。
- **`VNEffectsDemo.cs`（改）**：`'`（引号）键开关黑边，提示文字更新。

### 48.3 技术决策

- **黑边做成一等命令而非只有 fx 开关**：需要 height/time 参数（fx 语法只有 on/off），
  且"letterbox on"在剧本里可读性远高于"fx letterbox on"。两种写法都支持，
  存档状态统一记在 `_fxStates["letterbox"]`。
- **自动黑边挂在 mood Memory 上**（而非新增"回忆模式"命令）：Memory 色调本就是
  回忆专用（褪色暖黄+胶片颗粒+暗角），黑边是它的天然搭配；`_letterboxAuto` 标记
  确保手动/自动互不干扰——手动开的黑边不会被离开回忆时误撤。
- **黑条用普通 Image 而非 CanvasGroup 淡入**：电影黑边的正确演出是"滑入"不是"淡入"，
  且纯黑不透明无需混合控制。

## 四十九、夜晚偶发流星 + 云本体缓移（2026-07-17，分支 `agent/night-sky-ambience`）

> 四十六章"背景氛围：飞鸟/流星点缀、补云"落地：两个天空点缀组件，
> 全部按用户要求做成「一条 DOTween 路径 + 程序化贴图」的形态。

### 49.1 计划

- 流星：夜晚随机间隔划过一颗（萤火虫天气已有，流星补齐夜空氛围）；
- 云本体：VNCloudShadows 只有地面"影"，补上天上的"云"，缓慢横移。

### 49.2 文件说明

- **`VNProceduralTextures.cs`（改）**：
  - `MeteorStreak`（256×64）：右端亮头小光点 + 向左渐隐渐细的尾迹
    （贴图 +X = 流星头朝向，旋转 RawImage 即对准飞行方向）。
  - `CloudPuff`（256×128）：5 个柔边椭圆瓣叠加成蓬松云团，云底压平。
- **`VNShootingStars.cs`（新）**：夜晚偶发流星。
  - 排程：`DOVirtual.DelayedCall` 链式随机间隔（默认 2.5~7s），Hide 即停；
  - 单颗流星：RawImage（VN/Additive 共享材质，HDR×1.6 配合 Bloom）+
    一条 Linear `DOAnchorPos` 直线路径（起点上半屏随机、斜向下左右随机、
    480~900px 行程、0.55~0.95s）+ 前 20% 淡入/后 45% 淡出，飞完销毁；
  - Show/Hide/Toggle + CanvasGroup 渐显渐隐；全部 Tween `SetLink`。
- **`VNDriftingClouds.cs`（新）**：云本体缓移。
  - 3 朵云团（尺寸 520~950px 随机、纵向 170~430px、透明度抖动），
    初始均匀铺开 + 随机偏移；
  - 每朵一条 Linear `DOAnchorPosX` 横移路径：先按剩余路程等速补完第一段，
    到右边界后回绕左侧进入整屏 `SetLoops(-1, Restart)` 无限循环（70~120s/屏）；
    另加 9~15s 的 `DOAnchorPosY` Yoyo 轻微纵向浮动；
  - 普通透明混合白云（非加法），夜晚可通过 tint 调暗调蓝。
- **`VNStage.cs`（改）**：`shootingStars`/`driftingClouds` 字段 + AutoWire；
  `ToggleFxNames` 加 `meteor`/`skycloud`；Fx() 两个新 case。
- **`VNEffectsDemoSetup.cs`（改）**：BuildStageRig 第 12.5 步在 LayerBack 下创建
  DriftingClouds 与 ShootingStars（背景之上、立绘之下），连线 stage/demo；
  演示剧本：开场 `fx skycloud on`、夜晚段 `fx meteor on`，语法速查更新。
- **`VNEffectsDemo.cs`（改）**：`/` 键流星、`;` 键云缓移，提示更新。
- **`VNScenarioSchema.cs`（改）**：FxNames 加 `meteor`/`skycloud`。

### 49.3 技术决策

- **流星走排程+一次性物体**（而非粒子系统）：粒子难做"带方向的拖尾贴图对齐
  飞行方向 + 精确淡入淡出节奏"，一颗一物体成本可忽略（几秒一颗）且完全可控。
- **云用交错双段 Tween 实现无缝回绕**：第一段按剩余路程等比时长补到右边界，
  再从左边界进整屏无限循环——所有云保持各自等速，开启瞬间就是"已经在飘"的状态。
- **云影(clouds)与云本体(skycloud)独立开关**：白天有影无云、夜晚有云无影
  等组合都是合理演出，不强制绑定。
- 两个组件都进 `ToggleFxNames`：存档/读档/调试重建零改动支持。

## 五十、全屏情绪水波（2026-07-17，分支 `agent/screen-shockwave`）

> 玩法清单第 45 条落地：点击涟漪的全屏版——受击/震惊时整个画面荡开一圈波纹。

### 50.1 计划

- 一次性冲击演出（同 `fx speedlines burst` 定位），剧本 `fx shockwave [light|heavy]`；
- UI 不写深度缓冲、URP 下 uGUI 拿不到屏幕纹理（无 GrabPass），不能做真·屏幕空间折射，
  改用"三件套合成"方案：可见波纹环 overlay + 背景 UV 扭曲脉冲 + 轻震动。

### 50.2 文件说明

- **`Shaders/VNShockwave.shader`（新）**：`VN/Shockwave` 透明 overlay。
  - `_Progress` 0→1 = 波纹从 `_Center` 扩散到扫过全屏（半径 ×1.55 保证覆盖最远角）；
  - 三层构成：HDR 主波峰环（平方锐化，配合 Bloom 辉光）+ 波峰后方尾随衰减涟漪
    （`cos` 环 ×wake 带）+ 波峰内侧微暗波谷（"水面下压"体积感）；
  - 快进快出包络：前 7% 迅速点亮、扩散过半后随 `_Progress` 淡出；
  - 亮/暗部按占比混合出 rgb，单 Pass alpha 混合，不遮挡画面。
- **`VNScreenShockwave.cs`（新）**：组件总控。
  - 嵌套 Canvas 排序 26（盖过粒子/速度线 25，低于黑边 35/对话框 40），
    RawImage 全屏覆盖层平时禁用，零美术资源；
  - `Play(strength, viewportCenter)`：`_Progress` 0→1 OutQuad（默认 0.95s，水波减速感）；
  - 画面真的在"荡"：`targets`（生成器只连背景，避免立绘脸部扭曲）的
    `SetWave` 扭曲脉冲——`DOVirtual.Float` 包络前 15% 拉满、其余缓慢归零，
    `OnKill` 兜底归零防中断残留；
  - 可选联动 `VNScreenShake`（strength≥1.2 用 Medium，否则 Light）；
  - `PlayFrom(Transform)` 支持从受击角色位置荡开；全部 Tween `SetLink`。
- **`VNStage.cs`（改）**：`shockwave` 字段 + AutoWire；`Fx()` 新 case：
  `fx shockwave` 标准 / `light` 0.6 / `heavy` 1.4 倍强度；一次性演出不进 `ToggleFxNames`。
- **`VNScriptRunner.cs`（改）**：调试重建的 fx 汇总跳过一次性演出
  （`shockwave` 与 `speedlines burst`）——顺手修掉旧 bug：从选中行播放时
  `fx speedlines burst` 会被误还原成持续开启的速度线。
- **`VNEffectsDemoSetup.cs`（改）**：BuildStageRig 第 8.55 步创建 ScreenShockwave
  （targets=背景 fx；screenShake 在第 11 步创建后回填），连线 stage/demo；
  演示剧本头部语法速查补 shockwave 行。
- **`VNEffectsDemo.cs`（改）**：`-`（减号）键触发全屏水波，提示更新。
- **`VNScenarioSchema.cs`（改）**：FxNames 加 `shockwave`，fx 值选项加 `light/heavy`。

### 50.3 技术决策

- **不做屏幕空间折射**：URP + uGUI 组合拿不到屏幕纹理（无 GrabPass、
  _CameraOpaqueTexture 不含透明队列的 UI），伪装方案 = 可见环 overlay 叠在画面上 +
  背景材质自己的波浪 UV 扭曲同步脉冲，视觉上等效"画面在荡"。
- **一次性 fx 不记录状态**：与 speedlines burst 同规则；并让调试重建把这类
  命令整体跳过，避免"重建后画面莫名多了持续特效"。
- **波谷微暗环**：纯加亮的环看起来像"光圈"不像"水波"，波峰内侧压一圈暗带
  之后才有水面起伏的体积感（参考转场 Mode 9 的经验）。

## 五十一、胶片颗粒/CRT 复古滤镜（2026-07-17，分支 `agent/retro-film-filter`）

> 玩法清单第 46 条落地：回忆用胶片颗粒+划痕、梦境用 CRT（柔和版）。

### 51.1 计划

- 一个 shader 一个组件承载两种风格（同 VNScreenTransition 的 `_Mode` 复用思路）；
- 剧本 `fx filmgrain on|off` / `fx crt on|off`（互斥，开一个自动顶掉另一个）；
- mood 联动：Memory（回忆）自动上胶片；新增 **Dream（梦境）** 色调自动上 CRT。

### 51.2 文件说明

- **`Shaders/VNRetroFilter.shader`（新）**：`VN/RetroFilter` 透明 overlay，
  `_Mode` 0=胶片 / 1=CRT，`_Intensity` 总强度做淡入淡出。
  - 胶片模式（12fps 帧量化，每"帧"整体跳变复刻放映机质感）：
    细密亮/暗颗粒 + 3 条随帧跳位置随机隐现的竖向划痕（亮痕暗痕交替）+
    大格随机偶发尘点暗斑 + 整屏放映亮度抖动 + 较重暗角；
  - CRT 模式（柔和版，梦境不刺眼）：横向扫描暗线（低对比）+
    RGB 三色相位荫罩条纹（像素感彩色微光）+ 缓慢下扫的滚动亮带 +
    40fps 帧量化微闪烁 + 轻暗角与横向弧面压暗；
  - 亮/暗部按占比混合出 rgb，单 Pass alpha 叠加，无需屏幕纹理。
- **`VNRetroFilter.cs`（新）**：组件总控，`VNRetroMode { None, Film, Crt }`。
  - 嵌套 Canvas 排序 34（盖过舞台/速度线 25/水波 26，低于黑边 35/对话框 40）；
  - `SetMode(mode, fade)` 统一入口：None=淡出后禁用；Film/Crt 先配参数
    （颗粒/划痕强度、胶片暖黄 tint / CRT 冷蓝荧光 tint）再 `_Intensity` 淡入，
    两种滤镜互切时直接换风格补强度；`ShowFilm`/`ShowCrt`/`Hide`/`CycleNext` 快捷方法；
  - 材质 Tween `SetTarget(_mat)`+`SetLink`，OnDestroy 前 DOKill 防泄漏。
- **`VNMoodGrading.cs`（改）**：`VNMood` 新增 **Dream（梦境）**——偏亮低对比
  柔紫粉（曝光+0.35、对比-24、品红 tint+14、紫粉 lift/gamma、轻暗角）；
  `CycleNext` 的硬编码 `% 7` 改为按枚举长度取模（修掉加枚举会漏最后一项的隐患）。
- **`VNStage.cs`（改）**：`retroFilter` 字段 + AutoWire；`ToggleFxNames` 加
  `filmgrain`/`crt`（存档/读档/调试重建零改动支持）；`Fx()` 两个新 case
  （互斥：开一个清另一个的状态，手动控制会接管自动滤镜）；
  `SetMood` 联动重构：Memory→黑边+胶片、Dream→CRT，`_retroAuto` 标记
  确保手动/自动互不干扰；`RestoreSnapshot` 按 mood+fx 组合恢复自动标记。
- **`VNScriptRunner.cs`（改）**：调试重建（从选中行播放）静默重放补齐——
  mood 命令按 `autoMoodRetroFilter` 重放自动胶片/CRT（与黑边同款逻辑）；
  fx 命令处理 filmgrain/crt 互斥与手动接管，保证重建状态与运行时一致。
- **`VNEffectsDemoSetup.cs`（改）**：BuildStageRig 第 8.58 步创建 RetroFilter，
  连线 stage/demo；演示剧本头部语法速查补 filmgrain/crt 两行。
- **`VNEffectsDemo.cs`（改）**：`=`（等号）键循环 无→胶片→CRT，提示显示当前模式。
- **`VNScenarioSchema.cs`（改）**：FxNames 加 `filmgrain`/`crt`；
  mood 下拉自动长出 Dream（选项来自 `EnumNames<VNMood>()`，零改动）。

### 51.3 技术决策

- **overlay 而非后处理**：URP FilmGrain（Memory 色调里已有轻颗粒）做不了划痕/
  尘点/扫描线；自定义全屏 Pass 需要改 Renderer Feature 且影响移动端管线资产。
  uGUI overlay 零管线侵入，且能精确插在"舞台之上、黑边/对话框之下"的排序层。
- **划痕不用贴图**：3 条候选竖线按帧 hash 跳位置+随机隐现，比静态贴图更像
  真实胶片的随机损伤，且零美术资源（延续全程序化贴图的项目约定）。
- **新增 Dream 色调而非复用现有 mood**：梦境是视觉小说高频场景，
  CRT 滤镜需要一个语义明确的自动触发点；调色（柔紫粉朦胧）与滤镜（扫描线）
  分层各管各的，单开 mood Dream 不开 crt 也成立。
- **互斥用状态清理实现**：同一 overlay 同时只能一种风格，`fx filmgrain on`
  直接 `_fxStates["crt"]=false`，存档里永远只会记录其一。

## 五十二、背景 Ken Burns 漂移（2026-07-17，分支 `agent/kenburns-drift`）

> 玩法清单落地：静止背景以 60~90 秒周期极缓慢缩放 1.0→1.06 + 平移几十像素，
> 画面永不静止——商业 VN 标配的"活着的背景"。

### 52.1 计划

- 之前只有键盘演示场景 Start 里一条粗糙的 14 秒 DOScale Yoyo（只缩放、无平移、
  周期太快、剧本场景完全没有）；升级为正式组件，两个场景统一走它；
- 剧本 `fx kenburns on|off`，**默认开启**；off 用于需要完全定格的画面（如 CG 特写）。

### 52.2 文件说明

- **`VNKenBurns.cs`（新）**：核心组件，挂在背景 Image 上。
  - 实现：无限链式随机航点——每段随机取目标缩放（1.0~1.06）、随机平移
    （`Random.insideUnitCircle × 40px`，椭圆内取点防斜角偏出余量）、
    随机时长（30~45 秒，一去一回 ≈ 完整周期 60~90 秒）；
  - `InOutSine` 缓动让每段首尾速度归零：段间无停顿也无折角，永远在极缓慢地动；
  - `SetPlaying(false)` 用 2.5 秒缓慢归位（位置+缩放回基准）而非急停；
  - 基准位置/缩放首次使用时捕获（`CaptureBase`），Awake 即开始（`playOnAwake`）；
  - 与 VNCamera（缩放 ZoomRoot）、VNParallax（移层容器）、VNFakeDoF（缩放 LayerBack）
    作用于不同节点，全部可叠加；所有 Tween `SetLink`。
- **`VNStage.cs`（改）**：`kenBurns` 字段 + AutoWire（找不到时**自动补挂**到背景
  Image 上，旧场景不重新生成也能生效）；`ToggleFxNames` 加 `kenburns`；
  Fx() 新 case；**默认开启的存档语义**：AutoWire 时把 `_fxStates["kenburns"]`
  种为 true，存档才能正确记录"仍开着"，`fx kenburns off` 后的存档则不含它；
  `ResetEffects()` 末尾把 kenburns 重置回默认开（章节重置不该让画面死掉）。
- **`VNScriptRunner.cs`（改）**：调试重建快照初始种入 `kenburns`；
  `reset effects` 重放时清空 fxOn 后同样补种（与运行时 ResetEffects 一致）；
  顺带补上 reset 重放漏掉的 `autoRetro = false`（上一功能的小疏漏）。
- **`VNEffectsDemoSetup.cs`（改）**：BuildStageRig 第 6 步创建背景时直接
  `AddComponent<VNKenBurns>()`，连线 stage/demo；演示剧本头部语法速查补一行。
- **`VNEffectsDemo.cs`（改）**：删掉 Start 里的粗糙 DOScale（亮度呼吸保留），
  改为引用/自愈补挂 VNKenBurns；`\`（反斜杠）键开关，提示更新。
- **`VNScenarioSchema.cs`（改）**：FxNames 加 `kenburns`。

### 52.3 技术决策

- **随机航点链而非固定 Yoyo 循环**：固定往返几分钟后就能被玩家"看穿"节奏；
  每段随机方向/幅度/时长的漂移无周期感，更接近纪录片运镜的呼吸感。
- **动背景 Image 而非 ZoomRoot/VNCamera**：ZoomRoot 是运镜的领地，Ken Burns
  若与 pushin/snapzoom 抢同一节点会互相覆盖；背景自身的 60px 溢出余量
  正是为此预留的（生成器注释"给 Ken Burns / 视差留余量"至此兑现）。
- **默认开启 + 状态表种子**：`_fxStates` 只记录被 Fx() 碰过的名字，默认开的
  特效若不种子，存档读回来会被"先全关再开 fxOn 列表"的恢复流程误关。
  已知一个可接受的边缘：本功能之前的旧存档不含 kenburns，读档后漂移是关的，
  下一条 `fx kenburns on` 或新开局即恢复。

### 51.4 修复记录（分支 `agent/fix-retrofilter-shader`）

- **d3d11 编译错误 `unexpected token 'line'`**：胶片划痕循环里把变量命名为
  `line`——它是 HLSL 保留字（图元类型），d3d11 编译器直接报语法错误。
  改名 `scratch` 即可。教训：shader 变量避开 `line/point/triangle/sample/matrix`
  等 HLSL 保留字。

## 五十三、从零手动搭建场景指南 SetUpGuide.md（2026-07-17，分支 `agent/setup-guide`）

- **`SetUpGuide.md`（新）**：假设 Hierarchy 全空、纯手动从第一个物体搭出完整
  剧本演示场景的教程，内容与 `VNEffectsDemoSetup.BuildStageRig / CreateScriptDemoScene`
  的实际产物逐项对齐（参数值直接取自生成器代码）。
- 结构：第 0 章三个核心概念（HDR+Bloom 发光契约 / 每种整屏运动独占容器层 /
  嵌套 Canvas sortingOrder 排序体系）→ 项目级准备（导入设置/材质资产/Volume
  Profile 及每个数值的依据）→ 相机与后处理 → Canvas 与容器层级 → 背景与舞台 →
  全屏 overlay 排序总表 → 场外管理器与粒子连线表 → 对话框/EventSystem →
  剧本系统接线（VNStage 引用清单/角色资产/剧本/音频/事件/任务）→
  运行验证清单 + 常见坑速查表；附录 A 键盘演示场景差异、附录 B 完整层级树。
- 特别标注了手动搭建的高危点：Canvas 必须 Screen Space - Camera（Overlay 会让
  Bloom 对 UI 失效）、Sprite 必须 Full Rect、LayerFront/Background 命名被
  AutoWire 依赖、事件模板必须禁用等。
- **`CLAUDE.md`（改）**：文档头部补 SetUpGuide.md 指引。

## 五十四、Inspector 中文说明全量改造（2026-07-18，分支 `agent/inspector-chinese-headers`）

> 用户要求：所有 Inspector 可调变量直接显示中文说明（[Header]），不用悬停
> 才能看到的 [Tooltip]。

- **批量转换（脚本完成）**：运行时脚本（Editor/ 除外）里 191 处单行
  `[Tooltip("中文说明")]` 全部机械替换为 `[Header("中文说明")]`；
  VNCharacterDef 里 4 处多行拼接 Tooltip 手动压缩成单行 Header
  （Header 只渲染一行，过长会被截断）。
- **补漏（82 处）**：扫描所有 public/[SerializeField] 字段中没有任何说明的，
  逐个补中文 Header——包括 VNStage 全部 28 个舞台引用（"全屏转场"“运镜
  （驱动 ZoomRoot）"等）、VNEffectsDemo 的演示引用（附对应按键）、
  VNAudio 三通道音量、VNRetroFilter 颗粒/划痕强度、VNShatterGraphic、
  VNFakeDoF/VNSpeakerHighlight/VNChoicePanel/VNDialogueBox 的裸参数、
  嵌套可序列化类字段（VNAudio.Entry.clip、VNCharacterDef.Expression.sprite、
  VNParallax.Layer.rect、VNStage.BackgroundEntry）等。
- **排除项**：运行时数据类（VNScriptParser/VNSaveSystem/VNEventModule 的
  数据结构、各组件私有 runtime class）不在 Inspector 显示，不加；
  `System.Action` 等不可序列化字段不加。
- 原有的分组 Header（如"胶片参数"）保留，与字段说明 Header 叠放显示
  （Unity 的 DecoratorDrawer 支持同字段多个 Header 上下排列）。
- 验证：转换后全目录 Tooltip 余量 0；扫描器确认无遗漏 Inspector 字段。

## 五十五、全面迁移 TextMeshPro + 中文字体管线（2026-07-18，分支 `agent/tmp-font-pipeline` + `agent/tmp-migration`）

**目标**：全项目文字从 legacy Text（LegacyRuntime.ttf 系统字体回退）迁移到
TextMeshPro（SDF 渲染），为镜头缩放下的文字锐利度和 P3 台词内嵌演出标记
（`{shake}{w:0.5}` 逐字特效）打地基。事前完整分析（Pro/Con/风险）见对话记录；
核心风险 = 中文字体资产管线 + 打字机重写，两者均在本批解决。

### 分支一 `agent/tmp-font-pipeline`：中文字体管线

- **随包字体**：Noto Sans SC（OFL 1.1 许可，SubsetOTF 版 8.3MB）放到
  `Assets/Resources/VNFonts/NotoSansSC-Regular.otf`，附 LICENSE-OFL.txt。
- **`Script/VNFont.cs`（新）**：全项目 TMP 字体统一入口（静态类），取代原先
  12 处 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`。三级兜底：
  1. 预烘焙动态字体资产（`Resources/VNFonts/NotoSansSC-Dynamic.asset`）
  2. 随包 OTF 运行时 `TMP_FontAsset.CreateFontAsset` 动态创建
  3. OS 中文字体（微软雅黑/PingFang/思源等候选表）
  全部走 **Dynamic + 多图集** 模式（1024², SDFAA, 采样 64, padding 6）：
  字形按需光栅化，生僻字零缺字。`Prewarm(text)` 把整段文本预热进图集。
- **`Editor/VNFontAssetBuilder.cs`（新）**：菜单
  **Tools → VN Effects → 字体 Fonts → 生成 TMP 字体资产 Create TMP Font Asset**。生成持久化 TMP_FontAsset
  （材质/图集用 `AddObjectToAsset` 挂子资产）。**为什么必须预烘焙**：
  场景生成器编辑期创建的 TMP 文字若引用运行时临时字体资产，存场景后会变
  Missing 引用；持久化资产才能被场景安全序列化。

### 分支二 `agent/tmp-migration`：全面迁移（基于分支一）

- **`VNTypewriterText` 整体重写**：`BaseMeshEffect.ModifyMesh`（TMP 不走
  uGUI 网格修改管线）→ `LateUpdate` 里 `ForceMeshUpdate` + 遍历
  `textInfo.characterInfo` 改每字 4 顶点（位置 y + 颜色 alpha）+
  `UpdateVertexData`。逐字上浮+淡入观感与旧版一致；对外 API
  （`Play/Complete/IsTyping/onComplete/charsPerSecond` 等）完全不变，
  调用方（VNDialogueBox/VNScriptRunner）零改动。**顺手修掉旧版隐患**：
  characterInfo 已剔除富文本控制符，标签不再占"字数"（旧版按 quad 计数，
  富文本会错位）；纯空白文本视作立即播完（旧版会卡住剧本推进）。
  收尾一帧后 `_animating=false` 停止每帧网格重建（旧版每帧 SetVerticesDirty）。
- **12 个运行时 UI 文件** `Text → TextMeshProUGUI`，字体统一 `VNFont.Asset`：
  VNDialogueBox（名牌/正文/箭头）、VNChoicePanel、VNBacklog、VNQuestLog、
  VNSaveLoadPanel、VNConfigPanel、VNQuickToolbar、VNToast、VNQteModule、
  VNMapModule、VNEffectsDemo（hintText 字段换型）、VNScriptRunner（Prewarm 接入）。
- **API 换算对照**（后续写代码照此办理）：
  | legacy | TMP |
  |---|---|
  | `TextAnchor.MiddleCenter` 等 | `TextAlignmentOptions.Center/TopLeft/Left/Right/Bottom/TopRight` |
  | `FontStyle.Bold` | `FontStyles.Bold` |
  | `supportRichText` | `richText` |
  | `horizontalOverflow = Wrap/Overflow` | `textWrappingMode = Normal/NoWrap` |
  | `verticalOverflow = Overflow/Truncate` | `overflowMode = TextOverflowModes.Overflow/Truncate` |
  | `lineSpacing`（倍率 1.25/1.15/0.9） | 字号百分比偏移（25/15/-10） |
  | uGUI `Outline` 组件 | `outlineWidth/outlineColor`（SDF 材质描边，更锐利） |
- **VNMapModule 地点名描边**改用 TMP SDF 描边（uGUI Outline 组件对 TMP 无效）。
- **场景生成器 `CreateHintText`** 改建 TMP 并引用
  `VNFontAssetBuilder.EnsureFontAsset()` 持久化资产（理由见分支一）。
- **`VNScriptRunner.LoadCommands`** 解析剧本后 `VNFont.Prewarm(source)`：
  台词字形在加载期一次性光栅化，播放期零卡顿。
- **DOTween 兼容**：`DOFade` 走 Graphic 扩展，TextMeshProUGUI 是 Graphic
  子类，原有淡入淡出调用全部照常工作。

### 验证与遗留事项

- `dotnet build` Assembly-CSharp / Assembly-CSharp-Editor 均 0 错误
  （csproj 已确认包含两个新文件）。
- **用户需要做的一次性操作**（Unity 编辑器内）：
  1. Tools → VN Effects → 字体 Fonts → 生成 TMP 字体资产 Create TMP Font Asset（生成预烘焙字体资产）
  2. Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene / Create Script Demo Scene
     （重建两个演示场景，替换场景里旧的 legacy Text HintText）
- 包体影响：+8.3MB 字体 OTF；动态图集显存随用随涨（多图集 1024² 递增）。
- 技术债：`Assets/TextMesh Pro/` Essentials 里的 EmojiOne 表情图集暂保留
  （删除可省 ~0.3MB）；TMP `<b>` 对无粗体变体的中文字体走假粗体（观感可接受）。

## 五十六、CG 一枚绘系统 P1（2026-07-18，分支 `agent/cg-system`）

**目标**：剧本可显示全屏 CG（一枚绘），复用背景显示管线（转场/Ken Burns/存档），
但独立建模：立绘与环境特效默认隐藏（可按需保留）、解锁记录全局永久存储。
方案分析（为什么不能"背景 + flag"直接做：立绘穿帮/特效叠加/解锁与存档生命周期
不同/素材库污染四个坑）见对话记录。

### 剧本语法

```
cg <id> [transition:Type] [chars:keep] [fx:keep]   # 显示 CG
cg off [transition:Type]                            # 关闭，恢复背景/立绘/特效
```

- 默认：立绘层淡出隐藏 + 环境特效暂停（godrays/clouds/haze/shimmer/meteor/
  skycloud + 天气粒子）；演出类 fx（黑边/滤镜/心跳/荷兰角/速度线）不受影响，
  Ken Burns 继续漂移（CG 缓慢运镜是经典演出）。
- `chars:keep` 保留立绘；`fx:keep` 保留环境特效（CG 当特殊背景用）。
- CG 之间可直接连续切换（差分/阶段演出），不必先 off。
- CG 显示期间的 `bg` 命令只更新底层背景记录不动画面，`cg off` 后按新背景恢复。

### 实现

- **`VNStage`**：新增 `cgLibrary`（CgEntry：id/sprite/group，group 为 P2 差分组
  预留）；`ShowCg/HideCg`；原 `SetBackground` 的转场逻辑抽出为 `SwapStageImage`
  供背景与 CG 共用（含直接背景转场快路径回退）。
  - 立绘隐藏走 **characterLayer 整层 CanvasGroup 淡出**而非 SetActive：GO 保持
    活跃（口型/眨眼协程、悬浮 Tween 不中断），恢复零成本；CG 期间 show 的新角色
    生成在同层自动被盖住。
  - 环境特效暂停记录 `_cgPausedFx` + `_cgSavedWeather`，`cg off` 恢复；
    CG 期间 `reset effects` 会作废恢复清单（不会回放已被 reset 的特效）。
- **`VNCgUnlocks`（新）**：全局解锁存储，`persistentDataPath/vn_cg_unlocks.json`，
  解锁即落盘。**与 VNFlags/存档槽完全分离**——flags 随存档快照走，读旧档会覆盖；
  解锁必须永久。P2 鉴赏画廊从这里读。
- **存档/读档**：VNSaveData 新增 `cgId/cgKeepChars/cgKeepFx`（旧存档缺省兼容）。
  捕获时存"CG 背后"的天气与 fx（暂停清单并回 fxOn）；恢复时先正常摆台再
  `ShowCg(instant)` 重放。调试重建（从选中行播放）在 RebuildStateBefore 加
  `case "cg"` 同步支持。
- **解析器/Runner**：关键字表加 `cg`；Runner 命令分发（同步等待转场，行尾 `@`
  照常异步）。
- **剧本编辑器**：VNParamSource 新增 `Cg`；`cg` 命令入 Scene 分类（转场参数
  中英对照复用）；CG id 用与 bg 同款的 Sprite 缩略图浏览器（数据源
  `VNStage.cgLibrary`，Refresh Sources 重建）；文档校验未注册 CG 报错
  （"off" 豁免）。
- **生成器**：Create Script Demo Scene 自动把 `Assets/CG/` 下图片灌入
  cgLibrary（文件名 = id，自动 Sprite 导入），目录不存在则创建；
  `Assets/CG/README.md` 说明用法。

### 验证与遗留

- dotnet build 运行时/编辑器程序集 0 错误（csproj 手动补了新文件做验证，
  Unity 刷新后会自动重生成）。
- Assets/CG 目前无素材：放图 → 重新生成剧本场景即可用。
- P2 待做（`agent/cg-gallery`）：鉴赏画廊 UI（网格缩略图/未解锁"？"/全屏浏览/
  差分组翻页）；差分组语法糖（如 `cg 组名#2`）视需求。

## 五十七、本地化多语言系统 P1~P3（2026-07-18，分支 `agent/localization-core` + `agent/localization-script` + `agent/localization-content`）

**目标**：中文之外支持英文/日文。用户确认 voice 不做本地化（单一语音源）。

**选型分析**（三方案对比见对话记录）：
- ✗ Unity 官方 Localization 包：为"UI 文本挂组件"设计，与自研 .vn.txt DSL 不搭，
  还引入 Addressables 依赖——等于只用它管几十条 UI 字符串。
- ✗ 每语言一份完整剧本（Chapter1.en.vn.txt）：命令结构会随时间发散；存档存的是
  **命令索引**，切语言直接错位；分支/调试重建/剧本编辑器全要按语言分裂维护。
- ✓ **结构与文案分离**：.vn.txt 只写中文（唯一真相），翻译放旁路表。命令流对
  所有语言完全一致 → 存档跨语言通用、分支/调试重建/编辑器零改动，表是纯文本
  Git/AI 协作友好——与项目哲学一致。

### P1 基础设施（agent/localization-core）

- **`VNLocale`（新，Script/）**：语言管理器。`Language`（中/英/日枚举，存
  PlayerPrefs `VN.Config.Language`）、`Code`（zh/en/ja）、`LanguageChanged` 事件、
  `T(key)` / `T(key, args)` UI 字符串查表。表在
  `Resources/VNLocale/ui.<code>.txt`（`key = value`，# 注释，\n 转义）；
  回退链：当前语言 → 中文表 → key 本身。`ParseTable` 公开给剧本翻译表复用。
- **`VNFont` 多语言化**：日文走随包 **Noto Sans JP**（4.5MB OTF，OFL 许可，
  与 SC 同出 notofonts/noto-cjk 的 SubsetOTF），中/英共用 Noto Sans SC（拉丁字形
  齐全）。每语言仍是"预烘焙资产→随包 OTF→OS 字体"三级兜底；日文三级全失败回退
  SC；日文字体挂 SC 作 TMP fallback（日文文本里偶发简体字不缺字）。
  语言切换时 `HandleLanguageChanged` 扫全场景 TMP 文本，只把**VNFont 管理的**旧字体
  换成新字体（编辑期手动指定的字体不动）。VNFontAssetBuilder 现在同时烘焙 SC/JP。
- **UI 字符串全量查表**：快捷功能条/存读档面板/任务日志/回想/设置面板/QTE/地图/
  Runner Toast 的硬编码中文全部改 `VNLocale.T`。**开发者可见的 [Header] 与
  Debug.Log 保持中文**（不是玩家文案）。
- **设置面板语言行**：中文 / English / 日本語 三按钮（显示各语言自称），当前语言
  高亮；切换 = `VNLocale.Language` 赋值 → 字体全场景热切换 + 各 UI 组件经
  LanguageChanged 事件重建文案。窗口 650→740 高。
- **事件订阅幂等**：SaveLoadPanel/QuickToolbar 的 Initialize 会被多次调用
  （启动 + 每次 F5/F9），订阅前先退订，防止重复重建。惰性面板（任务日志/回想/
  存读档）语言切换时只销毁缓存，下次打开重建即新语言。

### P2 剧本翻译管线（agent/localization-script）

- **翻译表**：`Resources/VNLocale/Scenarios/<剧本名>.<lang>.txt`。剧本名 =
  TextAsset.name（"Chapter1.vn"，即文件名去 .txt）。key =
  **FNV-1a(原文)8位hex + "-" + 同文出现序号**——中文没改动时增删其他行不打乱
  对照；同句中文出现多次靠序号区分。key 算法在 `VNScriptLocale.NextKey/Hash`，
  运行时与编辑器工具共用同一实现。
- **`VNScriptLocale`（新，Script/）**：`Apply(commands, scriptName)` 在
  LoadCommands 后与语言切换时执行，给 say 台词和 choice 选项标注
  `localizedText`（新字段，VNScriptParser 的 VNScriptCommand/VNChoiceOption 上）；
  显示走 `TextOf()`，缺译回退中文并集中告警一条；译文全文 Prewarm 进字体图集。
- **翻译范围决策**：choice 选项按**索引**匹配 → 翻译显示文本安全；
  **event 结果行不翻译**——它们是逻辑标识符（EventCo 按 `opt.text != result`
  匹配、地图模块拿结果名当 flag `去过_<地点>`），翻译会炸逻辑。
- **Runner 接线**：`Play(TextAsset)` 现在会记住资产（翻译表按名查找）；
  台词/选项/回想记录/存档末句全走译文；`autoWait` 按译文长度算；
  语言切换重新 Apply（当前已显示的那句到下一句才变）。
- **编辑器工具** Tools → VN Effects → 本地化 Localization：
  - **Extract Script Translations**：扫 `Assets/Scenarios/*.vn.txt` 生成/增量合并
    en+ja 表。已填译文按 key 保留；中文改过的旧译文挪到文件末尾"孤儿条目"注释区。
    每条上方自动写 `# [说话者] 中文原文` 注释供翻译者对照。
  - **Validate Script Translations**：统计每剧本×语言缺译数，Console 输出。
- **随附全量翻译**：Chapter1/Chapter2/Demo 的英/日表已翻完（33 条），
  由脚本按同一套 key 算法生成，之后在 Unity 里重新 Extract 不会产生差异。

### P3 资产文案（agent/localization-content）

- **`VNCharacterDef`**：+`displayNameEn/Ja` 与 `LocalizedDisplayName`（留空回退
  displayName）；VNStage 名牌与 GetDisplayName（Backlog 用）走译名。
  **剧本引用的角色 id 永远不翻译**（翻了会炸全部剧本与存档）。
- **`VNQuestDef`**：+英/日 标题/描述/阶段文案列表；`Title`/`StageText`/
  `LocalizedDescription` 按当前语言取值、缺项逐级回退中文。VNQuestLog 零改动生效
  （只把 `q.description` 换成 `q.LocalizedDescription`）。
- **`VNMapModule.Location`**：+英/日显示名，标记文字显示译名；逻辑（结果匹配、
  `去过_<地点>` flag）永远用中文 name。
- **生成器**：演示角色（亚里沙→Arisa/亜里沙、小雪→Koyuki/小雪）与演示任务
  （告白大作战→Operation: Confession/告白大作戦）预填英/日文案。

### 验证与注意事项

- dotnet build 运行时+编辑器程序集 **0 错误**（csproj 补录新文件后构建，
  csproj 本身 git-ignored，Unity 会重生成）。
- **工作流**：加新语言 = VNLanguage 枚举 + Codes 数组 + `ui.<code>.txt` +
  （可选）字体 Profile；日常翻译 = 改完剧本跑一次 Extract → 填表 → Validate。
- 正式角色资产（Assets/VNEffects/Characters/*.asset）的 displayNameEn/Ja
  需要在 Inspector 里手动填（本次未动用户已有资产，当时工作区有未提交修改）。
- 已知边界：Backlog 历史条目保持记录时语言；语言切换时正在显示的台词/名牌到
  下一句才刷新；`旁白:` 这类未注册说话者名牌原样显示；Resources.Load 带点文件名
  （Chapter1.vn.en）理论可行，若实测加载失败可把表文件名的点改下划线并同步
  VNScriptLocale.LoadTable。
- 台词内嵌演出标记（路线图 P3 的 `{shake}{w:0.5}`）落地时：抽取工具已在表头
  提醒译文保留花括号标记，无需再改管线。

## 五十八、修复左键无法推进对白 + 用户本地改动入库（2026-07-18，分支 `agent/fix-click-advance` + `agent/local-changes-0718`）

- **症状**：Enter/Space 能推进对白，鼠标左键不行。
- **根因**：`VNScriptRunner.Update` 推进前用 `EventSystem.IsPointerOverGameObject()`
  判断"点在 UI 上就不推进"。但本项目**整个画面都是 uGUI**（背景/立绘/对话框
  全是 Canvas 里的 Image，默认 raycastTarget=true），该判断恒为 true → 左键永拦。
- **修复**：`IsPointerOverInteractiveUi()`——`EventSystem.RaycastAll` 命中链向上找
  `Selectable`（按钮/滑条等）。点在功能条按钮/选项上不推进（原判断想保护的场景），
  点在对话框底板/背景/立绘上照常推进。静态 List 复用避免点击分配。
- **教训**：全屏皆 UI 的项目里 `IsPointerOverGameObject` 基本不可用，
  要判"可交互"而不是"是 UI"。
- 另将用户本地工作区整批入库（`agent/local-changes-0718`）：素材迁移
  Assets/Assets→Assets/Images（46 个重命名）、Dice_6 资源包、Unity 升级
  6000.5.3f1、QuickAccessWindow、本地化 .meta 与预烘焙字体资产等。

## 五十九、快速存读档 + 修复存读档面板按钮无字（2026-07-18，分支 `agent/quick-save-load`）

- **修复按钮无字**（用户截图报告）：VNSaveLoadPanel.CreateButton 创建了 TMP 文字
  组件但漏了 `text.text = label`（五十五章 TMP 迁移回归），保存/读取页签、×、
  确认/取消四处按钮全部空白。补一行即愈。
- **快速存读档**：
  - 专用槽 `QuickSaveSlot = 0`——VNSaveSystem 槽位是任意整数，面板网格只画
    1..20，槽 0 天然不可见、不会被普通存档覆盖。
  - **Q** 快速存档：复用 F5 的 VNCameraFade 截图管线（320×180 缩略图），但
    直接落盘，不开面板不暂停；截图协程期间演出推进则作废（避免存到非台词点）；
    连按去抖（_quickSaveCo 非空忽略）。仍受"仅台词处可存"约束。
  - **L** 快速读档：读槽 0，没有快速存档时 Toast 提示。
  - 快捷功能条新增 快存/快读 按钮（693→859 宽），SaveTo/LoadFrom 加 quick
    参数走专用 Toast 文案；ui.zh/en/ja 新增 5 个 key。
- dotnet build 0 错误。

## 六十、剧本编辑器：音频行内试听 ▶ 按钮（2026-07-18，分支 `agent/audio-inline-preview`）

- **需求**：编辑器里给 bgm/se/voice 选了 id 后听不到声音，全凭记忆；希望参数旁有
  ▶ 小按钮直接播放试听。
- **实现**（全部在 `Editor/VNScenarioEditorWindow.cs`）：
  - `DrawParamField` 里凡是 `AudioBgm/AudioSe/AudioVoice` 来源的参数，下拉左侧
    挤出 20px 画 ▶ 小按钮；点击播放当前 id 对应的 AudioClip，再点（■）停止；
    切听另一条会先停旧的（同一时刻只播一条）。
  - id 为空 / "stop" / 未登记（找不到 clip）时按钮置灰，tooltip 说明原因。
  - `CollectAudioIds` 增加可选参数，收集候选 id 的同时填充三张
    id → AudioClip 字典（通道库优先、旧混合库兜底，与下拉候选同一套合并规则）。
  - 新增静态类 `VNEditorAudioPreview`：Unity 没有公开的编辑器播放 API，反射内部类
    `UnityEditor.AudioUtil`（与 Project 窗口点音频文件的试听同源）。方法名做了
    版本兼容：`PlayPreviewClip/StopAllPreviewClips/IsPreviewClipPlaying` 优先，
    旧名 `PlayClip/StopAllClips` 兜底；全都找不到时 `Available=false`，按钮置灰
    而不是报错。
  - 播放期间挂 `EditorApplication.update` 轮询"还在播吗"，播完自动把 ■ 复位成 ▶
    并 Repaint；查询方法反射不到时不轮询（图标不自动复位，手动点 ■ 即可）。
  - 关窗（OnDisable）自动停止试听；试听按钮用 `GUI.changed` 保存/恢复包裹，
    **不会把文档标脏**（与"分类颜色"开关同一处理）。
- **技术决策**：
  - 试听走编辑器预览通道，**不含**条目基准音量/剧本 vol/通道音量标定（预览 API
    不支持音量），tooltip 已注明；要听实际混音效果请进 Play Mode。
  - 不循环播放（loop=false），BGM 也只试听一遍，避免忘了关。

## 六十一、剧本编辑器：行首"舞台一览"小格（2026-07-18，分支 `agent/row-stage-preview`）

- **需求**：想扫一眼就知道"这行时台上有谁、背景是什么"，不用在脑内从头模拟；
  提供开关按钮控制显示。
- **实现**（全部在 `Editor/VNScenarioEditorWindow.cs`）：
  - 工具栏新增"舞台一览"开关（默认开），状态存 `EditorPrefs`
    （`VNEffects.ScenarioEditor.StagePreview`），与"分类颜色"一样用 `GUI.changed`
    保存/恢复包裹，切换不会把剧本文档标脏。
  - 开启后每行左侧画一个 70px 小格：**当前背景缩略图**（CG 显示期间优先画 CG）+
    **左/中/右三个站位格**，有人的格子填角色专属色块（8 色调色板，颜色按
    `VNCharacterDef` 登记顺序稳定分配，同一角色全文档同色）。
  - 鼠标悬停小格显示完整 tooltip：背景 id / CG id（含是否保留立绘）/
    台上角色与站位（左/中/右）。
  - 状态推算 `RebuildStageStatesIfNeeded`：按文件顺序逐行累积
    `bg`（换背景）/`cg`（含 chars:keep 与 off）/`show`（已在场且不带 at 时原地不动，
    与运行时语义一致）/`hide`/`move`；自定义 x 坐标按 ±120 粗分左/中/右桶；
    结果按 `_version` 缓存，只有文档变化才重算，滚动绘制零开销。
  - CG 显示且未 `chars:keep` 时角色色块半透明（对应运行时"CG 默认藏立绘"）。
  - 校验圆点位置不变；行内其余控件整体右移 70px，choice/camseq 子行同步右移。
- **已知近似**：与"▶ 从选中行播放"的重建前置状态一致——按文件顺序直落，
  jump/choice 分支不展开（tooltip 有注明）。

## 六十二、剧本编辑器：Shift/Ctrl 多选行 + 批量移动/删除/复制（2026-07-18，分支 `agent/multi-select-rows`）

- **需求**：希望 Shift+左键（连选）/ Ctrl+左键（点选）同时选中多行，然后一起
  移动或删除。
- **实现**（全部在 `Editor/VNScenarioEditorWindow.cs`）：
  - `ReorderableList` 开启 `multiSelect = true`——Unity 原生支持
    Shift 连选 / Ctrl 点选 / **拖动把所有选中行整体移动**，无需自绘选择逻辑。
  - 新增 `SelectedRowIndices()` 辅助：取 `_list.selectedIndices`（升序、过滤越界），
    没有多选时退回单选 `index`，删除/复制共用。
  - 列表 [-] 删除按钮改为**删掉整个选区**（从后往前删避免下标漂移），删完清空
    选择并把光标落在原选区首行位置。
  - Duplicate 按钮支持多选：整块克隆插到选区最后一行之后，并自动选中新插入的块
    （方便复制完直接拖走）。
  - HelpBox 提示文案补充多选操作说明。
- **技术说明**：撤销沿用文本快照机制（`MarkStructural`/`onReorderCallback` 的
  `PushUndo`），批量删除/复制/拖动均可 Ctrl+Z 一步还原；
  "▶ 从选中行播放"等单行功能取 `_list.index`（最后点击行），行为不变。
- dotnet build Assembly-CSharp-Editor 0 错误。

## 六十三、养成属性系统 P1：VNStatDef + stat 命令 + 顶栏 HUD + C 键属性面板（2026-07-18，分支 `agent/stats-core`）

**目标**：像养成模拟游戏那样的属性玩法（用户给了《梦幻魔法公主》截图参考）：
金钱 500G、行动力 9/10、压力 8%、善恶 50%（顶栏），体力/智力/魅力/感性 + E~S
等级评价（面板）。这是养成四部曲（P1 属性核心 → P2 选项花费 → P3 商店 →
P4 日程循环）的第一步。

### 核心决策：全部建立在 VNFlags 之上

属性、金钱、行动力、压力、善恶本质全是整数，直接用 flag 存（flag 名 = 属性 id），
与任务系统（`任务_<id>`）同一模式——**存档、if 分支、choice 的 flag: 操作、
调试重建全部零改动免费复用**。需要新做的只有"带上下限/展示规则的定义层 + UI"。

### 剧本语法

```
stat 金钱 +100      # 增减（按定义钳制到 [min,max]，VNToast 飘字「金钱 +100」）
stat 压力 -10
stat 善恶 50        # 直接设值（飘字「善恶 → 50%」）
```

与 `flag` 命令的唯一区别：stat 走 VNStatDef 钳制 + 飘字；flag 保持静默改值语义。
条件判断照旧用 `if 金钱>=100 jump 买得起`（VNFlags.Evaluate 原样可用）。

### 文件说明

- **`Script/VNStatDef.cs`（新）**：属性定义资产（CreateAssetMenu "VN/Stat
  Definition"）。字段：id（=flag 名）、显示名（中/En/Ja，回退链同 VNQuestDef）、
  图标（可空，HUD 用主题色圆点代替）、主题色、useClamp+min/max、initialValue、
  展示样式 `VNStatStyle { Number(500G), Percent(8%), OutOfMax(9/10), Grade(E~S) }`、
  unit 后缀、gradeSteps 等级阈值表、showInHud。辅助方法 Clamp/GradeOf/Format/
  Normalized（Number 样式不画进度条）。
- **`Script/VNStatsHud.cs`（新）**：系统核心组件（参照 VNQuestLog 模式）。
  ① `Apply(name, valueToken, silent, line)` 执行 stat 命令：支持 +n/-n/设值与
  黏连写法 `stat 金钱+100`；按定义钳制；值未变则不动；silent = 调试重建静默重放。
  ② 顶栏 HUD：独立 Overlay Canvas（sortingOrder 580，低于任务日志 600），
  showInHud 的属性横排（图标/色点 + 名 + 值 + Percent/OutOfMax 的迷你进度条）；
  数值变化时滚动动画（放大回弹 + 涨绿跌红闪色，SetUpdate(true)+SetLink）。
  ③ C 键属性总览面板：全部属性列表（名 + 进度条 + 数值 + Grade 等级彩字）。
  ④ `EnsureInitials()`：定义了初始值的属性在 flag 尚不存在时写入（Start 时 +
  读档后，靠 VNFlags.Changed 触发的刷新顺带补）。语言切换销毁重建（惰性）。
- **`Script/VNFlags.cs`（改）**：新增 `Changed` 静态事件（Set/Clear 触发）——
  HUD 靠它感知一切改动来源（stat/flag 命令、choice flag: 操作、读档恢复）。
  订阅方标脏 + 下帧统一刷新（读档会连续触发多次）。
- **`Script/VNScriptParser.cs`（改）**：关键字表加 `stat`。
- **`Script/VNScriptRunner.cs`（改）**：`_statsHud` 字段（Start 找不到就自建，
  无定义资产也能工作）；命令分发 `case "stat"`；调试重建（从选中行播放）静默重放
  `case "stat"`（钳制照做不飘字）；C 键开/关属性面板（打开期间不推进剧情，
  与任务日志同款拦截）；`RequestStatsPanel()` 给功能条；右键隐藏 UI 时
  HUD 一起藏（SetInterfaceHidden 联动）。
- **`Script/VNQuickToolbar.cs`（改）**：新增"属性"按钮（859→936 宽）。
- **`Resources/VNLocale/ui.zh/en/ja.txt`（改）**：`toolbar.stats`、`stats.title/
  empty/toastGain/toastLose/toastSet` 六个 key ×3 语言。
- **`Editor/VNScenarioSchema.cs`（改）**：`stat` 命令入 Flow 分类（name 用 Flag
  来源下拉，value 提供 ±1/±5/±10 候选）。
- **`Editor/VNEffectsDemoSetup.cs`（改）**：`EnsureStatDefs()` 生成 8 个示例
  属性资产到 `Assets/VNEffects/Stats/`（已存在不覆盖）：顶栏四项 金钱(500G 起)/
  行动力(10/10)/压力(0%)/善恶(50%)，面板四维 体力70/智力20/魅力20/感性20
  （Grade 样式，阈值 0:E 50:D 100:C 200:B 350:A 500:S）；场景挂 VNStatsHud
  并灌入定义；提示文字补"C 属性面板"。

### 技术决策

- **属性面板用 C 键**：计划里的 Q 已被快速存档占用（五十九章），C=Character。
- **VNFlags.Changed 事件而非轮询**：HUD 要对"任何来源"的数值变化刷新
  （choice 的 flag: 操作、读档），在唯一写入口 Set() 广播是最小侵入方案。
- **initialValue 语义 = "flag 不存在时写入"**（而非每次进场覆盖）：新开局生效、
  读档不覆盖存档值、旧存档缺新属性时自动补初始值，三种情况一个规则全对。
- **stat 与 flag 并存不合并**：flag 是底层原语（任务/地图/CG 内部状态都在用，
  不该飘字），stat 是面向玩家的表现层封装；语义分开各自稳定。
- dotnet build 运行时 + 编辑器程序集 0 错误（csproj 手动补录新文件验证，
  Unity 刷新后自动重生成）。
- **用户操作**：Unity 里重新 Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景 Create Script Demo Scene
  重建剧本场景（生成属性资产并挂 HUD）。

## 六十四、养成 P2：选项条件显隐 + 花费（2026-07-18，分支 `agent/choice-cost`）

**目标**：选选项可以花金币（用户需求"选选项的时候花费金币选"），并支持按属性
条件显示/隐藏选项。

### 剧本语法（向后兼容）

```
choice
* 请她喝咖啡 if:魅力>=20 cost:金钱-100 flag:好感度+1 -> 咖啡厅
* 打工赚钱 cost:行动力-1 flag:金钱+200
* 回家休息 -> 回家
```

- `if:条件`：不满足则**隐藏**该选项（条件语法同 if 命令，无空格）；
- `cost:属性±数值`：右侧显示价格小字（金色；有单位显示 `-100G`，无单位显示
  `-1 行动力`）；**付不起时置灰不可点、价格标红**（判定 = 扣减后不得低于
  VNStatDef 下限，无定义资产按 0）；选中后自动扣除（走 stat 的钳制+飘字）；
- 参数是行尾空格分隔 token，if/cost/flag 任意顺序，选项文本本身可含空格。

### 文件说明

- **`Script/VNScriptParser.cs`（改）**：VNChoiceOption + `condition/costOp` 字段；
  ParseChoiceOption 改为"从行尾逐个摘参数 token"（旧的 IndexOf("flag:") 写法
  没法扩展到多参数；摘 token 保持旧剧本语义不变）。
- **`Script/VNStatsHud.cs`（改）**：花费四件套 `ParseCostOp`（静态，校验共用）/
  `CanAfford` / `FormatCostLabel` / `ApplyCost`（复用 Apply 的钳制+飘字）。
- **`VNChoicePanel.cs`（改）**：新增 `Option { text, costLabel, interactable }` 与
  `Show(Option[], cb)` 重载（旧 `Show(string[])` 包装转发，其他调用方零改动）。
  置灰项：底色/文字变暗、不挂 Button 和悬停特效但保留 raycastTarget
  （挡住穿透点击误推进剧情）；价格小字右对齐（可选=金色 / 付不起=红色）。
- **`Script/VNScriptRunner.cs`（改）**：ChoiceCo 重写选项组装——
  ① if: 过滤出 visible 索引映射表（回调索引 → 原始选项索引，**译文按原始索引取**，
  本地化不受影响）；② cost: 判定付得起并生成价格标签；③ 选中后先扣费再执行
  flag/jump。防卡死兜底：全部选项被 if: 隐藏 → 全显示 + 告警；全部可见选项
  付不起 → 全解禁 + 报错（提醒剧本作者留免费选项）。
- **`Editor/VNScenarioDoc.cs`（改）**：VNChoiceOptionRow + condition/costOp；
  解析（与运行时同款摘 token）/生成（输出顺序 if: cost: flag: ->）/Clone；
  CollectFlags 把 cost 引用的属性名也收进 flag 下拉候选；校验：cost 格式
  （VNStatsHud.ParseCostOp 同一实现）、option if 不得含空格。
- **`Editor/VNScenarioEditorWindow.cs`（改）**：选项行新增「if」「$」两个小字段
  （悬停 tooltip 说明用法），与 flag/jump 下拉并排。
- **`Editor/VNScenarioSchema.cs`（改）**：choice 命令 hint 补新语法说明。

### 技术决策

- **if: 是隐藏而非置灰**：条件选项通常是"资格"（魅力不够根本看不到选项），
  与地图模块地点条件显隐同语义；cost: 才是置灰（让玩家看见"钱不够"产生动机）。
- **事件结果行不受影响**：event 复用 * 行解析会带上新字段，但 EventCo 不读
  condition/costOp，结果名匹配逻辑零变化。
- **本地化 key 不受影响**：抽取工具走 VNScriptParser，opt.text 已剥掉参数。
- dotnet build 运行时 + 编辑器程序集 0 错误。

## 六十五、养成 P3：商店事件模块 + I 键物品栏（2026-07-18，分支 `agent/shop-module`）

**目标**：用户需求"金币可以用来在商店买东西"。走现成的事件接口（四十二章），
遵守模块三铁律（不碰舞台 / unscaled 计时+SetUpdate(true) / 全部 SetLink）。

### 剧本用法

```
event shop id:服装店
* 离开 -> 商店结束      ← 可选：接住"离开"结果；不写则顺序继续
```

道具发放不必开商店：`flag 道具_钥匙 +1` 即得；`if 道具_药水>=1 jump 有药`。

### 文件说明

- **`Script/VNShopDef.cs`（新）**：商店定义资产（CreateAssetMenu "VN/Shop
  Definition"）。shopId（event id: 引用）、商店名（中/En/Ja）、结算属性
  currencyStat（默认「金钱」）、商品清单 Item{ id（=flag 道具_<id>）、显示名/
  描述（中/En/Ja）、icon、price、sellPrice（0=不收购）、maxOwned（0=不限）、
  condition（上架条件，VNFlags 表达式）}。常量 `ItemFlagPrefix = "道具_"` +
  `ItemFlagName()` 是道具 flag 命名的单一来源。
- **`Script/VNShopModule.cs`（新）**：商店事件模块（继承 VNEventModule）。
  - 模板 Inspector 登记多家商店，`event shop id:xx` 按 shopId 查找
    （只登记一家时 id 可省略）；找不到告警后 Done("") 顺序继续。
  - UI：暗幕 + 中央面板（弹入动画）+ 商店名 + 右上所持金（属性定义格式化，
    如 500G）+ 购买/卖出页签 + 商品滚动列表 + 离开按钮；Esc = 离开。
  - 商品行：图标（缺省色块）/名称/持有数/描述/价格（买得起金色、买不起红色）/
    买卖按钮（钱不够、达上限、无持有时置灰）。
  - 买入 = 金钱-price（走 VNStatsHud.Apply 静默钳制）+ 道具 flag+1 + Toast；
    卖出反之；每笔交易后刷新金额与列表（含条件商品上架变化）。
  - 结果返回"离开"：剧本可用「* 离开 -> 标签」接分支，不接就顺序继续。
- **`Script/VNInventory.cs`（新）**：I 键物品栏面板（参照 VNQuestLog 模式）。
  从 flags 反查 `道具_*>0` 的条目，文案/图标从登记的 VNShopDef 里找
  （跨商店取第一个命中；未登记道具用 id 当名字照常显示）；语言切换销毁重建。
- **`Script/VNScriptRunner.cs`（改）**：`_inventory` 字段（找不到自建）；
  I 键开/关物品栏（打开期间不推进剧情）；`RequestInventory()`。
- **`Script/VNQuickToolbar.cs`（改）**：新增"道具"按钮（936→1013 宽）。
- **`Resources/VNLocale/ui.zh/en/ja.txt`（改）**：shop.* 十个 key +
  inventory.title/empty + toolbar.inventory ×3 语言。
- **`Editor/VNEffectsDemoSetup.cs`（改）**：`EnsureShopDef()` 生成示例商店
  `Assets/VNEffects/Shops/服装店.asset`（蝴蝶结发饰 120G 可回售 / 洋装 300G
  限购1 / 神秘挂坠 魅力≥50 上架）；注册表加 ShopTemplate（id="shop"，禁用模板）；
  场景挂 VNInventory 并连商店资产；提示文字补"I 物品栏"。

### 技术决策

- **商店走事件模块而非独立系统**：事件期间禁快捷键/禁存档/调试重建视为分支点
  全部现成；商店天然是"暂停剧本 → 交互 → 带结果返回"的形态。
- **道具 = flag「道具_<id>」计数**：与任务（任务_）、地图（去过_）同一命名模式；
  存档/分支/调试重建零改动；物品栏和商店只是这些 flag 的两种视图。
- **金钱结算复用 stat 管线**：钳制到属性定义的 [min,max]、HUD 自动刷新
  （VNFlags.Changed），商店内交易 Toast 由模块出（含道具名与价格），
  所以 Apply 用 silent 模式避免双重飘字。
- dotnet build 运行时 + 编辑器程序集 0 错误。
- **用户操作**：重建剧本演示场景后，剧本里写 `event shop id:服装店` 即可开店。

## 六十六、养成 P4：time 日程命令 + 日历 HUD + 养成演示剧本（2026-07-18，分支 `agent/schedule-loop`）

**目标**：养成四部曲收官——回合制日程循环（对照用户截图右下角"9月 | 1年级
剩余36个月"）。核心洞察：**月循环本身用现有 DSL 就能拼**（label/jump/if/choice），
代码只需要补 time 命令和日历 HUD 两块。

### 剧本语法

```
time set 9 remain:36      # 进入养成模式：九月开始，倒计时 36 个月
time pass                 # 过月：月份+1（1~12 循环）、剩余月数-1、行动力回满、Toast
time pass months:2        # 跨多月
time pass refill:off      # 不回满行动力（refill:<属性名> 可改回满对象）
```

状态 = flag「月份」「剩余月数」，存档/if/调试重建照旧零改动；
月循环示例（完整见 RaisingDemo.vn.txt）：

```
label 月初
label 行动
if 剩余月数<=0 jump 结局
if 行动力<=0 jump 月末
choice
* 去打工 cost:行动力-2 -> 打工
…
label 月末
time pass
jump 月初
```

### 文件说明

- **`Script/VNScriptParser.cs`（改）**：关键字表加 `time`。
- **`Script/VNScriptRunner.cs`（改）**：`ApplyTimeCommand(cmd, silent)`——
  set（钳制 1~12 + 可选 remain）/ pass（月份循环递增、剩余月数存在才递减不为负、
  行动力回满 = 属性定义的 maxValue、Toast「进入X月」）；命令分发与调试重建
  静默重放各一个 case；`_calendarHud` 找不到自建（月份 flag 不存在时自动隐藏，
  常驻无害）；右键隐藏 UI 联动。
- **`Script/VNCalendarHud.cs`（新）**：右下角日历小面板（独立 Overlay Canvas 578）。
  大字月份 + 小字剩余月数（无剩余月数 flag 时只显示月份）；VNFlags.Changed
  标脏下帧刷新；「月份」flag 不存在时整个面板隐藏——纯剧情章节零干扰；
  语言切换销毁重建。
- **`Assets/Scenarios/RaisingDemo.vn.txt`（新）**：养成玩法演示剧本，串起
  六十三～六十六全部功能：stat 初始化 → time set → 月循环（打工/学习/特训
  （if:压力<80 条件选项）/逛服装店（event shop + 道具 flag 分支）/cost: 行动力
  扣减）→ time pass 过月 → 剩余月数归零后按 智力/金钱 分三种结局。
- **`Editor/VNScenarioSchema.cs`（改）**：`time` 命令入 Flow 分类
  （op 下拉 pass/set、month 数字、remain/months/refill kwargs）。
- **`Editor/VNEffectsDemoSetup.cs`（改）**：RaisingDemo 剧本注册进
  runner.chapters（剧本里 `chapter RaisingDemo.vn` 可切换）。
- **`Resources/VNLocale/ui.zh/en/ja.txt`（改）**：calendar.month/remain、
  time.toastMonth ×3 语言。
- **`CLAUDE.md`（改）**：组件速查表补五个新组件行；剧本系统清单补
  "养成系统（六十三～六十六章）"总结条目。

### 技术决策

- **time 只管 flag 和 HUD，不管流程**：月末结算/结局判定留给剧本的 if/jump——
  这正是自研 DSL 的甜点区，代码里写死"月末跳哪"反而限制剧本自由度。
- **行动力回满需要属性定义**：满值 = VNStatDef.maxValue；没有定义资产时跳过
  （不知道满值是多少，宁可不动）。
- **日历与属性 HUD 分开组件**：日历只在养成剧本出现（按月份 flag 自动显隐），
  属性 HUD 是常驻件；合并会让纯剧情章节多出无意义的空日历。
- dotnet build 运行时 + 编辑器程序集 0 错误。
- **用户操作**：重建剧本演示场景（注册 shop 模板与 chapters）后，
  Scenario Editor 打开 RaisingDemo.vn.txt → 选中首行 → ▶ 从选中行播放 即可体验。

## 六十七、GeneralQuestionGuide.md：项目常见问题解答指南（2026-07-19，分支 `agent/general-question-guide`）

用户提出七个使用/规划问题，整理成新文档 `GeneralQuestionGuide.md` 存档备查：

1. **跳转指定脚本/对话**：梳理 jump（文件内）/ chapter（跨文件，需登记 Chapters 列表）
   两级跳转，总结「入口 flag + 目标文件头路由」套路实现跨文件直达指定场景；
   给出小游戏（event 结果行）、日历（if 月份==N）、属性（if 属性>=N）、
   任务（if 任务_id==100）四类触发的速查表——全部依托「万物皆 flag」设计，零代码可用。
2. **Game Loop 建议**：给出传统 Gal（共通线→分歧→个人线→结局）与模拟养成
   （月初→行动→结算→检查点→循环→结局判定）两种典型 Loop，对照 RaisingDemo.vn.txt
   证明现系统直接支持；指出两个缺口：无内建随机数命令、无周计划排程 UI。
3. **HowToUse.md 过时**：另开分支 `agent/howtouse-refresh` 全面更新（见六十八章）。
4. **火山的女儿式周日程**：判定为可行，给出设计方案——新事件模块 `event plan`
   （排程面板写 flag 日程_1..N）+ `flag 名 rand:1-100` 随机数扩展 + 纯剧本执行循环
   + 分级结果演出；附工作量评估与实施顺序。
5. **程序生成 UI 换美术**：三条路线——A 主题资产 VNTheme（推荐，兼容一键重建）、
   B 生成后转 Prefab（深度定制门面 UI）、C 全手工（不推荐）；附实施节奏建议。
6. **其他程序生成内容**：清单化（程序化贴图/材质/粒子/演示场景/UI），
   结论「逻辑和演出留代码，皮肤和内容逐步资产化」。
7. **小功能扩展性**：逐条评估用户举例（日期横幅演出=小、转场+SE=零开发、
   新属性/角色好感度=零代码命名约定、任务结算弹窗=小～中），
   附「新增剧本命令四步通用流程」。

文档定位：与 HowToUse.md（语法教程）、ProjectCodeGuide.md（代码指南）互补的
「使用与规划 FAQ」，后续同类问题继续追加到该文件。

## 六十八、HowToUse.md 全面翻新：补齐现有全部系统（2026-07-19，分支 `agent/howtouse-refresh`）

旧版 HowToUse.md 停留在 P0+P1+P2（演出/分支/存档）时代，本次对照当前代码
（VNScriptRunner 的完整命令表、VNStage 的 fx 分支、快捷键处理）全面翻新：

- **新增命令章节**：`cg`（CG 显示/关闭/keep 参数/全局解锁）、`chapter`（跨文件章节
  跳转 + 入口 flag 路由套路）、`letterbox`、`portrait`、`reset effects`；
- **新增「玩法系统命令」一章**：`event`（qte/map/shop 三内置模块 + 结果行语法）、
  商店与物品栏（VNShopDef / 道具_flag / I 键）、`quest` 任务（任务_flag 状态表 / J 键）、
  `stat` 属性（与 flag 的静默/演出之分、零代码加新属性）、`time` 日程（set/pass/
  refill 参数、月份/剩余月数 flag、日历 HUD）；
- **choice 选项**补 `if:` 条件显隐与 `cost:` 花费置灰；if 章节点明「万物皆 flag」
  （属性/月份/任务/道具全部可直接 if）；
- **fx 表**补新特效：speedlines/shockwave/filmgrain/crt/kenburns/meteor/skycloud，
  mood 表补 Dream 并标注 Memory/Dream 的滤镜联动；
- **音频**改为三通道独立库 + vol: 参数 + 基准音量公式；
- **玩家操作表**更新为当前全量快捷键（H/A/S/F5/F9/Q/L/J/C/I/右键隐藏 UI/Esc）、
  快捷功能条、20 槽存读档界面说明；
- **新增章节**：本地化流程（Extract/Validate、逻辑标识符不翻译）、
  剧本可视化编辑器（从选中行播放）；资产管理补 CG 目录与玩法定义资产一览表；
- **排查表**补：chapter 找不到、choice 全隐藏、cost 置灰、事件中快捷键失效、
  日历不显示、翻译缺失等新条目；速查卡按 演出/音频/流程/玩法 分组重排。

文档结构从 10 章扩到 13 章；示例剧本尾部加 chapter 接续示例，并指向
RaisingDemo.vn.txt 作为养成玩法范例。

## 六十九、Flag 高级教学剧本套件（2026-07-19，分支 `agent/flag-scenario-tutorial`）

### 69.1 目标与文件组织

为用户新增一套可直接阅读、拆改和运行的多文件养成剧本，用实际剧情集中演示如何把整数 Flag
用于布尔开关、计数器、阶段/枚举、一次性奖励、防重复事件、跨文件入口路由、任务状态、道具、
地图访问、小游戏结果、条件选项、月份派发和多结局：

- `Assets/Scenarios/第1章.vn.txt`：新游戏初始化、`!flag`、玩家属性与内部状态分工；
- `Assets/Scenarios/主循环.vn.txt`：月初检查点、事件优先级、行动菜单、复合条件拆解与结局派发；
- `Assets/Scenarios/第2章.vn.txt`：三入口文件头路由、任务阶段、小游戏正负结果；
- `Assets/Scenarios/第3章.vn.txt`：路线 Flag、条件选项与演出组合；
- `Assets/Scenarios/节日活动集.vn.txt`：圣诞/新年/生日三入口与日期事件防重复；
- `Assets/Scenarios/好感事件_小雪.vn.txt`：20/50 阈值事件与任务完成结果演出；
- `Assets/Scenarios/结局集.vn.txt`：五结局路由、通关标记及槽位/全局存储边界说明。

### 69.2 教学设计

- 所有跨文件跳转都使用“命名空间入口 Flag（如 `入口_节日活动`）+ 文件头路由 + 入口立即清零”，
  避免所有文件共用一个 `进入点` 时互相污染；
- 明确区分 `stat`（玩家可见、可钳制、会飘字）与 `flag`（静默逻辑状态）；
- 当前条件系统没有 `and/or`，所以“月份==12 且未看过”与“好感达标且任务完成”等复合条件均
  用多个 label 逐层判断，示例全部符合现有运行时能力；
- 主循环用 `本月事件已触发` 实现每月最多派发一个剧情事件，返回后不会连环触发；
- 所有里程碑和节日均有 `已看_`/`已获得_` Flag，演示幂等与防重复；
- 结局文件说明普通 VNFlags 随槽位存档，而真正跨槽永久解锁应使用类似 `VNCgUnlocks` 的全局存储。

### 69.3 使用前提与验证

运行前需把七个 TextAsset 登记到场景 `VNScriptRunner.Chapters`，并从 `第1章.vn.txt` 开始。
本批只新增剧本和 Unity `.meta`，没有修改用户已有章节、场景、运行时代码或生成器；完成后对
命令关键字、label/jump 引用、chapter 目标、event 结果行和入口清零进行静态验证。

## 六十九、flag 随机数扩展 rand:min-max（2026-07-19，分支 `agent/flag-rand`）

周日程玩法（七十章）的前置小功能：给 flag 命令加区间随机写入，
剧本可实现"失败/普通/成功/大成功"式的概率分级。

### 语法

```
flag 运气 rand:1-100     # [1,100] 闭区间随机取整写入
if 运气<=10 jump 失败
if 运气<=80 jump 普通
...
```

### 实现（VNScriptRunner.cs）

- 运行时 `case "flag"` 与调试重建的 `ApplyDebugFlag` 原本是两份重复逻辑，
  借此机会合并为共用静态方法 `ApplyFlagCommand(cmd, silent)`（silent = 重放时
  不弹告警），rand 两条路径同时生效；
- `TryParseRandRange`：从第 2 个字符起找 `-` 分隔符（兼容负数下限如 `-5-5`），
  min>max 时自动交换；解析失败告警并不写入；
- 随机源 = UnityEngine.Random（该文件未 using System，无歧义）。

### 已知限制（有意为之）

调试"重建前置状态"会静默重放 flag 命令 → rand 重新掷骰，重建出的分支状态
可能与实际游玩路径不同。这与 event 结果不重放是同类限制（重建逻辑本来就把
if/choice/event 视为分支点并告警），不额外处理。

### 配套更新

Demo.vn.txt 语法头、HowToUse.md 第五章（含"属性影响概率"的判定链写法示例）、
Scenario Editor schema（flag 命令加 rand 参数框）。

## 七十、周日程排程玩法：VNPlanModule + VNResultPopupModule（2026-07-19，分支 `agent/plan-module`）

实现《火山的女儿》式「排好一周日程 → 逐一执行 → 随机产出失败/普通/成功/大成功
→ 加属性」玩法。前置的随机数扩展见六十九章（分支 `agent/flag-rand`）。

### 玩法拆成四块

| 环节 | 实现 |
|---|---|
| ① 排一周日程 | `event plan slots:7 pool:打工,学习,剑术训练,休息 title:安排这一周` |
| ② 逐格执行 | `event plan op:next`（无 UI 秒回，写 flag `当前格`/`当前行动`，剧本 if 派发） |
| ③ 随机分级 | `flag 运气 rand:1-100` + if 阈值链 |
| ④ 结算演出 | `event result grade:great title:剑术训练 sub:… se:…` + `stat` 飘字 |

循环骨架（`label 执行日程` … `jump 执行日程`）全部写在剧本里。

### 新文件

- **VNPlanDef.cs**（资产）：候选行动清单。每个行动有 id（剧本 pool: 引用，不翻译）、
  number（写进 flag 的行动编号，剧本按它派发）、显示名/预期收益文案（三语）、
  图标、condition（上架条件，复用 VNFlags.Evaluate）。
- **VNPlanModule.cs**（事件模块，id = `plan`）：
  - 排程模式：左列候选行动（图标+名称+收益文案，点击填入下一个空格）、
    右列 N 个日程格（点击清空），底部重置/确定。确定写入
    `日程_1..N`（值 = 行动编号，**空格写 0 = 休息**）、`日程数`、`当前格`归零；
  - `op:next` 模式：无 UI，当前格 +1 并把该格编号抄进 `当前行动`，
    超出日程数返回结果 `end`（否则 `next`）——同步 Done，EventCo 的
    `while (result == null)` 当帧就退出，对话框无闪烁；
  - 无 VNPlanDef 资产时退化：`pool:` 名字按出现顺序编号 1..n 照常可玩。
- **VNResultPopupModule.cs**（事件模块，id = `result`）：四档 grade
  （fail/normal/good/great）各自的大字、配色、面板底色；大字 2.6 倍缩放砸落
  + 落地 punch，good/great 追加 10 颗四芒星向外爆散；`title:`/`sub:`/`se:` 可选；
  点击/回车/空格关闭（`inputDelay` 0.4s 防连点误触）。
- **WeekPlanDemo.vn.txt**：完整可跑演示（两周循环、四种行动各自的概率表、
  剑术训练演示「体力≥150 走另一条更宽松的阈值链」= 属性影响概率）。

### 设计决策

1. **概率表写在剧本里而不是模块内部**。评估过模块掷骰（公式写死在代码里）
   的方案，选了剧本侧 `rand` + if 阈值链——概率表本质是**内容不是逻辑**，
   调平衡不该改代码。属性影响概率靠「分流到另一条阈值链」表达。
2. **排程与派发同一个模块的两种 op**，而不是拆成两个模块：它们共享
   flag 命名约定（日程_N / 日程数 / 当前格 / 当前行动），放一起便于维护。
3. **空格 = 休息（写 0）**，不强制填满：确定按钮永远可点，剧本把 0 当休息处理。
4. **属性增减仍由剧本的 stat 命令负责**，弹窗只管演出——截图左侧那排增益条
   就是 VNStatsHud 已有的 +N 飘字。

### 配套改动

- VNProceduralTextures 加 `SparkleSprite`（已有 Sparkle 贴图的 Sprite 包装，
  供 Image 用于星光爆发）；
- 场景生成器：注册 plan/result 两个模块模板、新建 `Assets/VNEffects/Plans/周日程.asset`
  示例方案（打工1/学习2/剑术训练3/休息4，与演示剧本编号对应）、
  把 WeekPlanDemo.vn.txt 登记进 Runner.chapters；
- 三语 UI 字符串表加 `plan.*` / `result.*`；
- Scenario Editor schema：event 命令说明补内置模块清单；
- HowToUse.md 第六章新增「周日程排程：plan + result」小节 + 排查表 4 条 +
  速查卡；GeneralQuestionGuide.md 问题四改写为「已实现」并补设计理由；
  CLAUDE.md 组件速查表与养成系统条目更新。

### 使用前提

**必须重建剧本演示场景**（Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景 Create Script Demo Scene）
才会注册 plan/result 模块并生成示例方案资产。

## 七十一、事件模块回想开关 RecordInBacklog（2026-07-19，分支 `agent/plan-backlog-quiet`）

七十章实装后发现的瑕疵：`event plan op:next` 是纯流程控制调用，一周会跑 7 次，
EventCo 每次都往回想里写一条「plan → next」，把玩家的回想记录淹没。

修法（最小改动，对其他模块零影响）：

- `VNEventModule` 加虚属性 `RecordInBacklog`（默认 true），文档注明「纯流程控制型
  调用应返回 false」；
- `VNScriptRunner.EventCo` 在 `Destroy(module.gameObject)` **之前**读取该属性
  （Destroy 是帧末延迟执行，但读值要早于销毁才安全），据此决定是否 Record；
- `VNPlanModule` 用 `_dispatchMode` 字段区分两种 op，派发模式返回 false。

排程面板本身（`event plan` 无 op 参数）仍照常记入回想，qte/map/shop/result
行为不变。

## 七十二、修复滚动列表左侧内容被裁切（2026-07-19，分支 `agent/fix-scroll-content-width`）

用户反馈：周日程面板右列显示成「y 1 / y 2」（"Day" 的 "Da" 不见了），
回想面板也有同样现象——左边的文字和 UI 跑到外面看不见。

### 根因

所有滚动列表的 Content 物体都是这样建的：

```csharp
var contentGo = new GameObject("Content", typeof(RectTransform), …);
content.anchorMin = new Vector2(0f, 1f);   // 横向拉伸
content.anchorMax = new Vector2(1f, 1f);
content.pivot     = new Vector2(0.5f, 1f);
// ← 从来没设过 sizeDelta
```

**RectTransform 的默认 sizeDelta 是 (100, 100)**。在横向拉伸锚点下，
sizeDelta.x 不是宽度而是「相对父物体宽度的增量」，于是：

- Content 实际宽度 = 视口宽度 + 100
- pivot.x = 0.5 → 内容居中 → **左右各溢出 50px**
- VerticalLayoutGroup 的 childControlWidth 把这个宽度传给每一行
- 视口上的 RectMask2D 把溢出部分裁掉 → 左边缘的图标/文字缺一块

`ContentSizeFitter` 只设了 `verticalFit`，**永远不会修正 sizeDelta.x**，
所以这 100px 一直存在。数值也对得上：日程格的 "Day" 文字距行左边缘 16px，
50 - 16 = 34px 被裁，正好是 24 号字「Da」两个字符的宽度。

### 修法

给每个 Content 显式补一行 `sizeDelta = Vector2.zero`（顺带补 anchoredPosition）。
受影响的六个面板全部修正：

| 文件 | 面板 | 症状 |
|---|---|---|
| VNPlanModule | 日程排程 | 日程格 "Day N" 左侧缺字（本次报告） |
| VNBacklog | 回想 | 台词左边缘缺字（本次报告） |
| VNShopModule | 商店 | 商品图标左侧缺一块 |
| VNInventory | 物品栏 | 道具图标左侧缺一块 |
| VNQuestLog | 任务日志 | 任务标题左侧缺字 |
| VNStatsHud | C 键属性面板 | 无遮罩不会被裁，但属性行比锚点区宽 100px |

### 教训（新 UI 代码请遵守）

程序化创建 RectTransform 后，只要把锚点改成拉伸模式，就**必须**接着显式设定
`offsetMin/offsetMax` 或 `sizeDelta`——默认的 (100,100) 在拉伸锚点下含义完全不同。
本项目里 `Stretch()` 辅助方法（设 offsetMin/offsetMax = 0）是安全的，
出问题的都是「只设锚点和 pivot，没设尺寸」的写法。

## 七十三、条件表达式：逻辑、括号与整数算术（2026-07-19，分支 `agent/condition-expressions`）

### 73.1 新语法

`if <表达式> jump <标签>` 从单个 Flag 判断扩展为只读整数表达式。现在支持：

- 逻辑：`!`、`&&`、`||`，其中 `&&`/`||` 会短路求值；
- 比较：`>=`、`<=`、`==`、`!=`、`>`、`<`；
- 算术：`+`、`-`、`*`、`/`、`%` 与一元正负号；
- 括号和空格，例如 `if 月份 == 12 && (好感度_小雪 + 友情度_小雪) / 2 >= 50 jump 约会`。

优先级为：括号 → 一元运算 → 乘除取余 → 加减 → 比较 → `&&` → `||`。
所有变量都读取 `VNFlags`，不存在的变量仍为 0；表达式不允许赋值，也不会改变游戏状态。

### 73.2 实现与兼容性

- 新增 `VNExpression` 递归下降解析器，运行时、可视化编辑器校验与 Flag 名收集共用同一语法真相；
- `VNScriptRunner` 从最后一个 `jump` token 向前重组独立 `if` 表达式，因此可以保留可读空格；
- choice/event 选项参数仍按空格切 token，`if:` 中的复合式必须无空格，例如
  `if:(月份==12&&好感度_小雪>=50)`；编辑器会同时检查无空格限制和表达式语法；
- 旧写法 `if 勇气 jump ...`、`if !勇气 jump ...`、`if 好感度>=2 jump ...` 原样兼容；
- 除零、取余零、整数溢出、括号或运算符错误都安全返回 false，并输出剧本行号和表达式列号；
- `&&`/`||` 的短路分支不会执行除零等运行时运算，但仍会完成语法解析。

### 73.3 边界

本批只实现整数与 Flag，不加入浮点数、字符串、函数调用或赋值。Flag 名不能包含
`()`、`!<>=&|+-*/%` 这些表达式保留字符；现有剧本使用中文、下划线与数字命名，兼容该规则。

## 七十四、限定跳转：`文件::label`（2026-07-19，分支 `agent/qualified-jump`）

### 74.1 语法与语义

新增 `jump 节日活动集::圣诞活动`。它先从当前 `VNScriptRunner.script` 与 Chapters 列表中
解析目标 TextAsset，再直接把执行位置设为该文件的 label。`.vn.txt` 后缀可写可不写，
文件名匹配沿用 chapter 的大小写不敏感规则。

所有最终调用 `JumpTo` 的入口自动获得同样能力：

- `if 条件 jump 文件::label`；
- choice 选项 `-> 文件::label`；
- event / 小游戏结果 `-> 文件::label`；
- 普通 `jump 文件::label`。

### 74.2 安全与编辑器

- 跨文件跳转先在临时命令表中解析目标并确认 label，成功后才替换当前 script、命令表、
  label 表与索引；文件或 label 不存在时保持原执行状态；
- 目标文件仍必须登记进 Runner 的 Chapters 列表，避免运行时隐式扫描 Resources 或依赖资产路径；
- `VNStoryAddress` 统一负责 `::` 地址解析和文件名规范化；`chapter` 也复用同一规范化逻辑；
- Scenario Editor 会扫描 `Assets/Scenarios/*.vn.txt`，跨文件校验文件与 label，label 下拉同时列出
  本地 label 与限定地址；工具栏的 Go to label 可打开目标文件并定位定义（有未保存改动时先确认）；
- `主循环.vn.txt` 的章节、节日、好感事件与结局派发已改成直接限定跳转，旧目标文件头路由仍保留，
  供旧剧本和确实需要“从文件头统一初始化”的 chapter 流程兼容使用。

### 74.3 存档边界

限定跳转本身不新增持久状态。跳转成功后 `script` 已是目标 TextAsset，现有存档继续记录目标章节名
与台词索引；读档逻辑无需格式迁移。它是一次性控制转移，不会自动返回——需要返回点时使用下一批
加入的 `call/return`，不要把 `jump` 当函数调用。

## 七十五、可存档子程序：`call / return`（2026-07-19，分支 `agent/script-call-return`）

### 75.1 运行语义

- `call 标签` 或 `call 文件::标签` 把“调用方 TextAsset、当前命令表、call 后一条命令的索引、
  call 物理行号”压栈，再复用限定跳转进入目标；目标解析失败时不压栈；
- `return` 弹出最后一帧，恢复原命令表和索引，所以本文件、跨文件与多层嵌套调用行为一致；
- 最大深度 64，超限拒绝继续 call，用于拦截无终止递归；`call`/`return` 禁止行尾 `@`；
- 空栈 return 会报错并停止；子程序带着未返回的栈播放到文件末尾也会报出原 call 行并清栈；
- 限定 jump 只改变当前执行位置，保留调用栈；`chapter` 被定义为新的顶层流程，会主动清空调用栈；
- Runner 保留初始入口 TextAsset，确保跨文件 call 后，即使入口文件没有重复放入 Chapters，
  当前会话和读档恢复仍能解析返回文件。

### 75.2 存档兼容

`VNSaveData` 新增 `saveVersion = 2` 和 `callStack`。每帧 JSON 保存返回章节名、返回命令索引与
调用源行；保存发生在子程序台词处时会完整写出栈。读档先恢复当前章节，再逐帧解析返回章节命令，
最后从存档台词索引继续。旧 JSON 没有 `callStack` 时按空栈处理，不需要迁移存档文件。

若存档引用的返回章节已被删除、未登记，或脚本改动导致返回索引越界，当前剧情仍可加载，
但会明确报错并忽略整条损坏的调用栈，避免之后 return 跳进错误位置。

### 75.3 编辑器与示例

Parser、Scenario Schema、命令中文名、跨文件 label 下拉与校验均加入 call/return；异步标记会在
编辑器直接报错。`Demo.vn.txt` 文件尾提供两层本地嵌套调用；后续七十六章把该示例升级为
带命名参数的通用送礼片段，两个 return 仍逐层回到准确位置。

## 七十六、子程序命名参数与插值（2026-07-19，分支 `agent/script-call-parameters`）

### 76.1 作者语法

调用端写 `call 通用送礼 角色:小雪 道具:蝴蝶结 好感:10`，不使用逗号；v1 参数值是不能含
空格的字符串。目标 label 后第一条有效命令用 `params 角色 道具 好感=5` 声明接口：无等号为
必填，有等号为默认值。少传必填参数会取消本次调用并恢复到 call 后继续，多传参数只警告并忽略。

子程序用 `${角色}` 读取参数。插值覆盖台词正文/译文、说话者、表情、命令 args 与 kwargs 值、
choice/event 的文字/条件/花费/flag/跳转，以及 camseq 点位与 ease；label 和 params 定义保持静态。
参数只读且不写 VNFlags，持久状态仍由 flag/stat/quest 修改。

### 76.2 作用域与嵌套

每次 call 创建独立参数字典，return 恢复调用者字典。内层不会隐式捕获外层参数，作者必须写
`call 内层 角色:${角色}` 显式转发；call 命令本身也在 Dispatch 前插值，所以转发值在进入内层前
已经解析。限定 jump 保留当前作用域，chapter 与新的顶层 Play 会清空作用域。

### 76.3 运行时实现

- `VNParameterInterpolator` 单层扫描 `${...}`，不递归展开参数值；缺失参数保持原占位符并按剧本行报错；
- Runner 每次 Dispatch 前深拷贝当前命令后插值，不修改解析缓存，因而同一公共片段可被不同参数重复调用；
- 本地化顺序为 `VNScriptLocale.Apply` 先选译文，再对 `localizedText` 插值，译者可自由调整占位符语序；
- call 跳到目标后读取紧邻 params 声明并绑定；绑定失败使用捕获的执行帧原子回滚，不留下半切换状态；
- VNSaveData 升至 version 3：保存当前参数字典，以及每个返回帧的调用者参数字典；version 2/旧存档
  缺少列表时自然恢复为空作用域。

### 76.4 编辑器与示例

Scenario Editor 为 call 提供“目标 + 参数串”编辑格，为 params 提供声明编辑格；校验参数必须是
`name:value`、禁止逗号/空值/重复名，并要求 params 紧跟 label。`Demo.vn.txt` 用
`角色:小雪 道具:蝴蝶结 好感:10` 展示说话者、台词、动态属性名、数值和嵌套显式转发。

---

## 七十七、剧本包《辣妹与宅宅》与"零拖拽"资产绑定（2026-07-19，分支 `agent/asset-binding`）

### 77.1 背景：一次真实的工作流疼痛

用户按 README 配置好场景（背景库 13 条、音频库、章节列表、地图坐标）后，
执行 **Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景 Create Script Demo Scene**，**全部配置瞬间清空**，
每次都要重配一遍。

根因在 `VNEffectsDemoSetup.BuildStageRig()`：

```csharp
EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
```

两个菜单项都先调它，即"丢弃当前场景 → 从空场景重造 → 覆盖保存"。
生成器的定位是"一键重建**演示**场景"（可随时覆盖的 demo），
而用户已经把它当成"正式游戏场景构建器"在用——**定位错配**，不是操作失误。

清点每次重建丢失的数据：背景库（重新编号成 bg1/bg2…）、角色列表（只剩 demo 两个）、
三个音频库 + 每条音量标定、入口剧本（写死 Demo.vn.txt）、章节列表（只剩两个 demo）、
地图地点坐标与条件、属性/商店/日程/任务的定义资产引用。
`cgLibrary` 是唯一不丢的——因为它靠 `FillCgLibrary()` 扫 `Assets/CG` 目录，
**这个先例直接指出了正确解法**。

### 77.2 关键认识：要压缩的是"引用面积"而不是"数据形态"

`VNStatDef`/`VNShopDef`/`VNQuestDef`/`VNCharacterDef` **早就是 ScriptableObject**，
资产本身从来没丢过；丢的是"**谁引用它**"——那些 List 长在场景组件上。

所以"把背景改成 SO"只解决一半：SO 不丢了，但 `VNStage.backgroundLibrary = 那个SO`
这个引用字段仍在场景里，下次重建照样清空。**真正要做的是把需要人工恢复的引用降到 0。**

### 77.3 方案：`VNGameConfig` + Resources 自动加载 + 目录扫描

新增 `Assets/Scripts/VNEffects/Script/VNGameConfig.cs`（ScriptableObject），
固定路径 `Assets/Resources/VNGameConfig.asset`。**放在 Resources 下是关键决定**：
运行时 `Resources.Load` 直接取，**场景里一个引用字段都不需要**（组件上的
`config` 字段只是可选覆盖），引用面积真正归零。

**覆盖语义只有一条**：资产里填了的项覆盖场景组件设置，留空的项保持场景原样。
好处是可以清空某一项来临时在场景里试别的东西，不必删资产。

数据分工按"能否从文件名推断"划线：

| 类别 | 处理 | 理由 |
|---|---|---|
| 背景 id→Sprite、音频 id→clip+基准音量、地图坐标+条件、入口剧本 | 存 VNGameConfig，手工维护 | 素材散放在 `Assets/Assets/`，文件名推不出剧本 id；音量标定是纯人工数据 |
| 角色/章节/CG/属性/商店/日程/任务 | 生成器扫目录自动登记 | 资产已有固定目录，扫描完全可确定 |

### 77.4 落地改动

**运行时**（各组件在 Awake / OnLaunch 读配置，config 优先、场景兜底）：
`VNStage`（角色/背景/CG）、`VNAudio`（三库+打字音+通道音量，必须在
`_initialXxxVolume` 快照**之前**执行）、`VNScriptRunner`（入口剧本+章节）、
`VNMapModule`（底图+地点，写在 `OnLaunch` 因为模板是禁用状态 Awake 不跑）、
`VNStatsHud` / `VNQuestLog` / `VNInventory` / `VNShopModule` / `VNPlanModule`（定义资产列表）。

**编辑器**：新增 `Editor/VNGameConfigTools.cs` 三个菜单——
`Create or Select` / `Import From Scene`（一次性搬家，只在目标为空时搬运，
避免二次执行覆盖已调好的数据）/ `Rescan Asset Folders`（扫目录，CG 重扫时保留
人工填的 `group` 差分分组）。另挂 `playModeStateChanged` 清静态缓存。

**生成器** `VNEffectsDemoSetup.CreateScriptDemoScene()`：
开头 `LoadAssetAtPath<VNGameConfig>` 取配置并挂到 stage/audio/runner；
背景/角色/CG/地图在配置非空时改用配置，否则退回原 demo 逻辑（向后兼容）；
章节改为 `ScanScenarioAssets()` 扫 `Assets/Scenarios/*.vn.txt`（**修掉了老代码只登记
两个 demo 导致重建后所有 `jump 文件::标签` 报错的问题**）；
属性/商店/日程/任务改为 `FindAllAssets<T>()` 全量登记；
结尾日志在缺配置时给出明确的 `Import From Scene` 引导。

### 77.5 同批产出：剧本包《辣妹与宅宅》

`Assets/Scenarios/` 新增 8 个剧本 + 1 份 README，作为系统能力的教学样板：
`第1章`（演出全家桶+chapter）、`主循环`（文件头守卫/time/choice 三修饰符/rand 属性影响概率/
shop/qte/result/三层 call 路由）、`第2章`（进入即上锁防死循环/整数 flag 表路线/`if:` 隐藏选项/map）、
`第3章`（四重门槛含任务阶段/cost 换机会/QTE 只加权重不定成败）、
`节日活动集`（路由表模式 + plan 周日程完整循环）、`好感事件_星野结衣`（进度指针式阶梯触发）、
`结局集`（判定表模式，顺序即优先级 + 无条件兜底）、`公共片段`（params/`${}`/嵌套转发/
**把参数拼进 jump 目标当 switch 用**）。

**期间发现文档 bug**：`HowToUse.md` 的 `* 选项A flag:路线 1 -> 汇合` 是错的。
`VNScriptParser.ParseChoiceOption` 从行尾逐个摘**空格分隔的 token**，
所以选项 `flag:` 只支持 `名字` / `名字+N` / `名字-N`，**无法赋任意值**。
剧本里统一改为"选项只负责 `->`，赋值写成目标 label 里的一条 `flag` 命令"。

### 77.6 已知限制

- 生成器仍然 `NewScene` 重造（未做幂等化）；本次是把**内容**搬出场景，
  而非让场景可增量更新。场景内的**布局/层级手工调整**依然会丢。
- 若把 `VNGameConfig.asset` 移出 `Resources`，运行时不再自动加载，
  需在组件的 `config` 字段手动指定（工具里已有 LogWarning 提示）。
- 未编译验证：Unity 编辑器占用工程，本次只做了括号/结构检查，
  实际编译结果以 Unity Console 为准。

### 77.7 修复：限定地址 `文件::标签` 从未通过分词器（潜伏 bug）

**症状**：运行剧本立刻报 `[VNScript] 第 116 行：call 缺少目标 label`。

**根因**：`VNScriptParser.ParseCommand` 对命令行每个 token 做 `key:value` 判定——

```csharp
int colon = tokens[t].IndexOf(':');
if (colon > 0 && colon < tokens[t].Length - 1)
    cmd.kwargs[...] = ...;      // 公共片段::换场 → key=公共片段, value=:换场
else
    cmd.args.Add(tokens[t]);
```

于是 `公共片段::换场` 被当成 kwarg 吞掉，**永远进不了 `args`**，`cmd.Arg(0)` 为空。

**影响面**（同一个根因的三种表现）：

| 写法 | 表现 |
|---|---|
| `call 文件::标签` | 报错「call 缺少目标 label」 |
| `jump 文件::标签` | 报错「jump 缺少目标」 |
| `if 条件 jump 文件::标签` | **静默失效**：目标不在 args 里 → 只出一条 if 语法 warning，跳转从不发生 |
| choice `-> 文件::标签` | **正常**（走 `ParseChoiceOption`，取 `->` 之后的整段，不经过本分词器） |

`VNStoryAddress.TryParse` 与 Runner 侧的限定跳转逻辑都是好的——**唯独分词器这一层没打通**，
所以七十四章「限定跳转」实际只有 choice 路径可用。老剧本
（Demo / RaisingDemo / WeekPlanDemo / Chapter1-2）全部只用本地 label，
所以这个 bug 一直没被触发。

**修复**：判据改为「第一个冒号后面**紧跟另一个冒号**时，整个 token 是位置参数」。

```csharp
bool qualifiedAddress = colon >= 0 && colon + 1 < token.Length && token[colon + 1] == ':';
if (!qualifiedAddress && colon > 0 && colon < token.Length - 1) → kwarg
else → arg
```

反例保护：`title:第1章::序` 的第一个冒号后面是「第」，仍按 key:value 解析，
值里的 `::` 原样保留传给 `VNStoryAddress`。

**验证**：对全部 13 个剧本做新旧分词对拍——目标类命令解析失败 94 → 0，
且 kwargs 差异**全部**是那条伪造条目的消失，`背景:` / `数值:` / `转场:` / `rand:1-100`
等正常键值对零改变。

**顺带发现（未修）**：`camto` / `camcut` 的「角色:部位」目标点
（如 `camto 亚里沙:head 1.6 0.8`）也会被同一处判成 kwarg，导致 `Arg(0)` 变成 `1.6`。
这是另一条判据（冒号后不是冒号），本次修复不涉及；HowToUse 里该语法目前不可用。
修法应是对 `camto`/`camcut` 把关键字后第一个 token 无条件当位置参数。

### 77.8 `camto` / `camcut` 目标点同族修复 + HowToUse 校订

`camto 亚里沙:head 1.6 0.8` 的目标点与 `key:value` 撞形，同样被 `ParseCommand`
判成 kwarg，导致 `Arg(0)` 拿到 `1.6`。修法：`t == 1 && (camto | camcut)` 时
首个 token 一律当位置参数。（`camseq` 的 `>` 路径点走 `ParseCamWaypoint`，
本来就无条件取 `tokens[0]`，不受影响，无需改。）

`HowToUse.md` 校订（106 增 / 25 删）：

| 位置 | 原内容（已过时或错误） | 改为 |
|---|---|---|
| 一、新建剧本 | "把新文件拖到 Script 栏 / 拖进 Chapters 列表" | 放进 `Assets/Scenarios/` 即自动登记；入口剧本设在 VNGameConfig |
| 五、choice | `* 选项A flag:路线 1 -> 汇合`（**错误示例**，值会被吞进选项文字） | 明确 `flag:` 只支持 `名字` / `名字±n`；多结局套路改为赋值写在目标 label |
| 五、jump/chapter | "跨文件目标必须先拖进 Chapters" | 放 `Assets/Scenarios/` 即可，自动扫描 |
| 四、bg | "背景 id 在 VNStage.backgrounds 配置" | 改指向 VNGameConfig 的 Backgrounds |
| 六、音频 | "在场景 VNAudio 物体登记" | 改指向 VNGameConfig 三个 Library |
| 八、资产管理 | 无 VNGameConfig 概念 | 新增开篇小节：为什么会丢、三个菜单、分工规则、覆盖语义 |
| 八、角色/背景/CG | "拖进 VNStage.characters" | 角色/CG 自动扫目录；背景改填资产 |
| 十三、排查表 | 三条指向 Chapters/VNStage 的旧解法 | 更新；新增「call 缺少目标 label」与「配置每次重建就没」两条 |
| 附、速查卡 | 无限制说明 | choice 的 `flag:` 与 `if:` 限制补进速查卡 |

---

## 七十八、CG 鉴赏画廊 P2（2026-07-19，分支 `agent/cg-gallery`）

补齐五十六章遗留的 P2：网格缩略图 / 未解锁占位 / 全屏浏览 / 差分组翻页。

### 设计决策

**① 未解锁的 CG 也占一格，显示「？」。**
不是"只列已解锁"——玩家看得见"还差几张"才有收集动机。但**不画原图的任何像素**
（sprite 置 null + 暗色块），避免剧透。顶部另给「已解锁 N / M」进度。

**② 数据来源一分为二，职责不同。**

| 来源 | 内容 | 为什么 |
|---|---|---|
| `VNStage.cgLibrary`（退回 `VNGameConfig.cgLibrary`） | **目录**：一共有哪些 CG | 目录是内容配置，随资产走 |
| `VNCgUnlocks` | **解锁**：看过哪些 | 独立 JSON，与存档槽分离；读旧档/开新周目不丢 |

退回配置资产这条路径是为将来做**纯标题画面的鉴赏入口**（那时场景里没有 VNStage）预留的。

**③ 差分组 = 网格合并成一格。**
`CgEntry.group` 非空的条目合并为一格（五十六章建 cgLibrary 时就为此预留了该字段）。
格子显示组内第一张**已解锁**的图，右下角 `2/3` 角标；一张都没解锁则整格上锁。
全屏浏览 `←→` 在组内翻页，且**跳过未解锁的差分**（`StepViewer` 循环找下一张已解锁的）。
`group` 留空 = 用 `" " + id` 当组键自成一组（前缀空格保证不与真实 group 名相撞）。

**④ 输入仍由 Runner 统一分发。**
项目约定是"面板的按键归 Runner，事件模块才自己接管输入"。画廊比其他面板多一层
状态（网格 / 全屏），所以 Runner 里的分支也多一层：全屏时 `←→`(含 `↑↓`) 翻差分、
`Esc`/`G` 退回网格；网格时 `Esc`/`G` 关闭。组件只暴露
`Open/Close/CloseViewer/ViewerNext/ViewerPrev` + `IsOpen/IsViewerOpen`。

### 实现

- **`VNCgGallery.cs`（新，Script/）**：程序化 UI，独立 Overlay Canvas
  `sortingOrder = 600`（与任务日志/物品栏同层，同一时刻只开一个）。
  `GridLayoutGroup`(FixedColumnCount) + `ScrollRect` + `ContentSizeFitter`；
  全屏浏览层是同 Canvas 下的兄弟节点，打开时 `SetAsLastSibling()`。
  翻差分带 0.18s `DOFade` 淡入，`SetLink` + `SetUpdate(true)`（不受 S 快进的
  `DOTween.timeScale` 影响）。
  切语言时整个 Canvas 销毁重建（与 VNInventory 同策略）。
- **接线**：`VNScriptRunner` 加 `_cgGallery` 字段 + Start 自愈创建 +
  输入链分支 + `RequestCgGallery()`；`VNQuickToolbar` 加 `CG` 按钮；
  生成器建 `VNCgGallery` 物体并连 `stage`，场景提示文字加「G CG鉴赏」。
- **本地化**：三张 ui 表各加 `gallery.title/empty/locked/progress/hint`
  与 `toolbar.gallery`。

### 踩到的坑

- `_grid.sizeDelta` 必须显式清零：默认 `(100,100)` 在横向拉伸下会比视口宽 100px，
  左右各溢出 50px 被 `RectMask2D` 裁掉（与 VNInventory 当年同一个坑，注释已标）。
- `RoundedRectSprite` 有 `(22,22,22,22)` 边框，配 `Image.Type.Sliced`
  才不会把圆角拉变形（与 VNConfigPanel 用法一致）。

### 遗留

- 差分组语法糖（`cg 组名#2`）仍未做——目前差分靠**给多个 id 填相同 group** 实现，
  剧本侧照常写各自的 id。等真有大量差分需求再评估。
- 画廊入口目前只有游戏内 G 键；标题画面/主菜单尚不存在，将来做的话
  组件已支持无 VNStage 运行（走 VNGameConfig 目录）。
- 未编译验证（Unity 编辑器占用工程），只做了括号/结构检查。

---

## 七十九、剧本静态校验器（2026-07-19，分支 `agent/scenario-linter`）

**动机**：这套 DSL 的错误绝大多数**只在运行到那一行时才暴露**，其中一部分还是
**静默**的（event 结果名拼错 → 顺序继续；选项 `flag:名 值` → 变量根本不写；
`if … jump 文件::标签` 分词失效 → 只有一条 warning）。
ProjectCodeGuide 第十二节的技术债清单里就写着「结果名精确字符串匹配、无静态校验」。
本轮开发中我为了自检临时写了三个一次性脚本（跳转对拍 / id 引用审计 / 分词对拍），
把它们固化成工具。

### 最重要的设计决定：复用 `VNScriptParser.Parse`

校验器**不自己分词**。理由是项目刚刚吃过一次亏：`文件::标签` 的"文档描述"与
"解析器实际行为"不一致，导致 94 处跳转静默失效。分词规则一旦有两套实现就会漂移，
校验器就会说谎。复用解析器 ⇒ **校验器看到的命令流与 Runner 执行的完全一致**。

### 严重度约定（信噪比优先）

| 级别 | 含义 |
|---|---|
| Error | 一定会坏：悬空跳转、重名 label、子程序回不去、emote 枚举名写错、选项 flag 赋值失效 |
| Warning | 很可能坏但有合理例外：素材还没登记、事件结果名不认识、缺循环守卫 |
| Info | 卫生问题：label 从未被引用（默认不显示） |

**资产类问题刻意定为 Warning**：边写剧本边补素材是正常工作流，全报 Error 会让人
习惯性忽略整个输出——那校验器就废了。宁可少喊，也不要喊狼来了。

### 值得记的两个规则实现

**① `missing-return` 是真的做了可达性分析**，不是"找找有没有 return"：
从 call 目标 label 出发做 DFS，`return` = 好终点，EOF 或 `chapter`（会清空调用栈）
= 坏终点；`jump` 跟随（跨文件）、`if…jump` 两边都走、`choice/event` 每个选项都走
且有无 `->` 的选项算 fallthrough、`call` 视为会返回所以继续下一行。
只要**存在**一条通向 EOF 的路径就报错。`${}` 动态跳转无法解析时按"不报"处理（避免误报）。

**② `loop-risk` 用"守卫 flag 交集"判定**：
对每条跨文件 `if COND jump FILE::LABEL`，若目标文件会跳回本文件，
则取 COND 里引用的 flag 名与目标文件写入过的 flag 名求交集；
**交集为空 = 大概率缺守卫**（章节演完跳回来，条件再次成立 → 死循环）。
提示里直接给出惯例修法（`flag <章节>已看 1` + `&& !<章节>已看`）。

**③ 库整个为空时不逐条报，汇总成一条**：
逐条报"未登记的音效 X"在库全空时会刷屏，所以 `CheckId` 在库为空时跳过；
但"库空 + 剧本引用了 N 个 id"本身就是最该被发现的问题（全部静音），
于是由 `CheckEmptyLibraries` 汇总成一条高信噪比的提示，并列出用到的 id。

### 文件

- `Editor/VNScenarioLinter.cs`：分析引擎（纯逻辑，返回 `List<VNLintIssue>`）
- `Editor/VNScenarioLinterWindow.cs`：结果窗口（按文件分组、严重度筛选、搜索、
  双击/「打开」跳到出错行，走 `AssetDatabase.OpenAsset(asset, line)`）
- 菜单 **Tools → VN Effects → 剧本检查 Lint Scenarios**，快捷键 `Ctrl+Shift+L`

素材登记状态取自 `VNGameConfig` 并与**当前打开场景**的 VNStage/VNAudio/
VNEventRegistry/VNMapModule 取并集（有人可能还在用旧的场景内配置方式，
不该因此被误报）；角色定义永远扫资产（与场景无关，最可靠）。

### 在现有剧本上的预期输出（编写时静态推算）

错误 0；警告 2（SE 库空但剧本引用 7 个音效 id；`event map` 用了但 mapLocations 空）；
若干 Info（默认隐藏）。角色/表情/背景/CG/BGM 引用全部命中，跳转 0 悬空。

### 遗留

- 未编译验证（Unity 占用工程），只做了括号/结构检查；上述预期输出是静态推算，
  以实际运行结果为准。
- 尚无"按规则屏蔽"机制（`VNLintIssue.code` 字段已为此预留）。
- `unknown-event-module` 依赖当前打开的场景里有 VNEventRegistry；没打开场景时跳过。

---

## 八十、开始菜单（标题画面）（2026-07-19，分支 `agent/title-menu`）

### 需求与选型

用户要求实现开始菜单（开始游戏 / 继续 / 读取存档 / CG 鉴赏 / 设置 / 退出）。
经确认的四个决策：

1. **同场景覆盖层**（不做独立 TitleScene）：零场景切换、复用生成器一键重建，
   舞台就是标题背景板（Ken Burns 默认漂移是真的舞台效果，不用另做）。
2. **「继续」与「读取存档」双按钮**：继续 = 直接读保存时间最新的槽
   （含快速存档槽 0），无档置灰并显示最近存档时间；读取存档 = 打开现成
   VNSaveLoadPanel 选槽。
3. **设置复用 VNConfigPanel**，一处维护。
4. **视觉走"现有背景图 + 程序化演出"**：背景库第一张（或配置指定）+ 标题
   光晕呼吸 + uGUI 假星光上飘 + 按钮悬停提亮。

### 新增 / 修改文件

- **新增 `Script/VNTitleMenu.cs`**：标题层组件，全部运行时构建
  （与存读档/设置面板同款套路）。要点：
  - Overlay Canvas 排序 **500**：在游戏 UI（Screen Space - Camera）之上、
    画廊 600 / 存读档 900 / 设置 950 之下 → 读档/鉴赏/设置按钮**直接调
    Runner 的现成 Request 接口**，面板自然盖在标题上，零新 UI。
  - 收起规则只有一条：`VNScriptRunner.ResumeAt`（新游戏/读档/编辑器
    "从选中行播放"全走它）调 `NotifyGameplayStarted()` → 标题层销毁
    （销毁而非隐藏：14 个星光循环 Tween 靠 SetLink 随画布回收，不留后台空转）。
  - 标题期间藏起对话框（`SetInterfaceVisible(false)`，尊重 `_shown` 语义，
    恢复时不会强行显示空框）与场景底部按键提示（按名字找 `HintText`）。
  - 退出带确认弹窗（Esc 可关），编辑器下退 Play 模式。
  - 语言切换销毁重建刷新文案（背景/BGM 只应用一次，`_stageApplied` 挡住重播）。
- **`VNScriptRunner.cs`**：
  - `Start()`：发现场景里有 `VNTitleMenu` 且 `showOnStart` → 跳过
    `playOnStart`，改为 `titleMenu.Open()`；否则维持旧行为（老场景零影响）。
  - `Update()`：标题打开期间屏蔽全部游戏快捷键与推进（插在画廊分支之后——
    叠在标题上的面板关闭键由上方各自分支处理，天然可用）。
  - 新增 `StartNewGame()`：`VNFlags.Clear()` + 从入口剧本 `Play()`
    （call 栈由 Play → Prepare 清，无需额外处理）。
  - `ResumeAt()` 开头补 `_titleMenu?.NotifyGameplayStarted()`。
- **`VNGameConfig.cs`**：新增"标题画面"区——`gameTitle/En/Ja`（按语言取、
  缺省回退中文、全空用 "Visual Novel"）、`titleBackground`（背景 id，留空 =
  背景库第一张）、`titleBgm`（留空 = 不播）。跟随资产的既有覆盖语义，
  重建场景不丢。
- **`VNEffectsDemoSetup.cs`**：`CreateScriptDemoScene` 创建 `VNTitleMenu`
  空物体（`showOnStart` 默认开；调试剧情嫌挡路可在场景里临时关）。
- **本地化**：三张 ui 表各加 `title.start/continue/load/gallery/config/quit/quitConfirm`。

### 技术决策记录

- **为什么继续按钮把快存槽 0 也算进"最新"**：玩家最后的进度就是最后的进度，
  不应因为它存在 Q 键专用槽就被跳过。
- **为什么收起钩子放 ResumeAt 而不是各入口**：ResumeAt 是所有播放路径的唯一
  漏斗（Play / LoadFrom / PlayFromSourceLine 全收敛于它），一个钩子覆盖全部，
  以后新增入口也不会漏。
- **假星光而不用 VNAmbientParticles**：Screen Space - Overlay 画布永远盖在
  相机渲染的粒子系统之上，真粒子在标题层后面看不见；uGUI Image + DOTween
  循环是同视觉成本最低的替代。

### 遗留

- 游戏内"返回标题"入口未做（快捷功能条可加一个按钮，调 Stop + 重开标题层）。
- 编辑器"从选中行播放"的直接模式下，标题层会先应用标题背景再被调试目标覆盖
  （重建模式会按剧本重摆，无影响）；只影响调试观感，不影响玩家路径。
- 未编译验证（Unity 编辑器占用工程）；已按既有组件逐 API 核对。

---

## 八十一、回合制战斗示例模块（事件接口 P4）（2026-07-19，分支 `agent/battle-module`）

### 目标

用 `event battle` 做一个回合制小战斗模块，验证四十一章设计的事件接口
能承载"重玩法"（多回合状态机 + 实时演出 + 数值系统联动），
而不只是连打条/选地点这类单步交互。

### 新增 / 修改文件

- **新增 `Script/VNBattleModule.cs`**：继承 VNEventModule，整场战斗
  封在一个类里。
  - **剧本用法**：`event battle enemy:暗影史莱姆 ehp:26 eatk:5 php:30
    patk:6 pdef:1 escape:50` + `* 胜利/失败/逃跑` 结果行
    （结果名固定中文——事件结果名是逻辑标识符，永不翻译；
    `escape:0` 隐藏逃跑按钮）。
  - **回合流程**：Phase 状态机（PlayerTurn/Resolving/Ending）。
    我方四行动：攻击（±30% 浮动 + 10% 会心 1.7×）/ 重击（65% 命中 1.8×，
    落空浪费回合）/ 防御（本回合受伤 ×0.4）/ 逃跑（escape% 判定，
    失败挨打）；敌方 ±30% 浮动 + 15% 猛攻 1.5×，减 pdef 后至少 1 点。
    1234 键与按钮等效。
  - **养成联动（本模块的验证重点）**：`patkstat:体力` → 攻击改读
    flag「体力」（同理 phpstat/pdefstat），三级来源 = stat 指定 flag >
    剧本直填 > 组件默认。模块不认识任何具体属性名——属性影响战斗的桥
    全在 flags，与商店/任务同一套设施。
  - **战后回写**：flag「战斗剩余HP」（失败为 0），剧本可做车轮战
    （下一场 `phpstat:战斗剩余HP`）或伤势分支。
  - **演出全程序化**：敌人 = 紫色光晕色块 + 双眼（呼吸待机、受击白闪+
    抖动、扑击 PunchAnchorPos、死亡 InBack 缩没）、伤害/MISS 飘字
    （上飘淡出自毁）、HP 条 anchorMax 伸缩、胜负大字横幅 OutBack 弹入。
    零素材依赖。
  - 三铁律全遵守：不碰舞台 / DOVirtual.DelayedCall(…, true) +
    SetUpdate(true) / 全部 SetLink。
- **`VNEffectsDemoSetup.cs`**：注册表登记 `battle` 模板（与 qte/map/
  shop/plan/result 并列）。
- **`VNScenarioLinter.cs`**：BuiltinOutcomes 表补 `battle → 胜利/失败/逃跑`
  （否则新剧本的结果行会被误报 bad-event-outcome）。
- **新增 `Assets/Scenarios/BattleDemo.vn.txt`**：三连示范——
  ① 固定数值入门战 → ② choice 特训加体力后 `patkstat:体力` 强敌战 →
  ③ 胜利后 `if 战斗剩余HP<=10` 分支 + 车轮战（剩余 HP 带入下一场）。
- **本地化**：三张 ui 表各加 `battle.*` ×20（按钮/横幅/战斗日志，
  日志带 {0}{1} 占位符）。敌人名是剧本内容，按既有约定走剧本翻译表。

### 技术决策记录

- **结果名为什么用中文**（qte 用的是英文 success/fail）：商店「离开」、
  地图「地点名」已经确立了"面向剧本作者的结果名用中文"的方向，
  战斗跟随之；qte 的英文名是历史遗留，不动它（改了会破坏现有剧本）。
- **数值浮动用 System.Random 而不是 flag rand**：浮动是模块内部演出的
  一部分，不是剧本可见状态；剧本要控概率走 escape:/属性数值即可。
- **为什么写「战斗剩余HP」而不是直接改 php 来源属性**：模块写死属性名
  就违反"模块不认识具体属性"的原则；由剧本决定
  `stat 体力 -X` 还是无伤，保持内容与逻辑分离。

### 遗留

- 无技能/道具栏：四行动是硬编码的。将来可加 `skills:火球|治疗` 参数
  （或 VNBattleDef 资产）让剧本配技能表，UI 已按钮数组化，扩展点现成。
- 敌人外观只有一种色块造型；可加 `sprite:` 参数走 CG/背景库贴图。
- 未编译验证（Unity 编辑器占用工程）；已按 QTE/商店模块逐 API 核对。

---

## 八十二、对话框 / 选项面板 UI 皮肤系统（2026-07-19，分支 `agent/ui-skins`）

### 需求与选型

用户希望：① 用自己的 UI 素材（prefab）替代运行时代码拼 UI，随时在编辑器里
改布局（选项挪右侧、对话框放顶部、加渐变装饰图……）；② 对话框/选项各有两三套
样式，剧本命令切换。三个已确认决策：**两条独立命令**（ui dialogue / ui choice）、
**保留程序化兜底**（不配 prefab 时与老版本一致，零素材原则不破坏）、
**全槽位可选**（面板/名牌/正文/箭头/头像窗/流光框/功能条停靠点都在皮肤里声明，
留空 = 该功能优雅降级）。

方案 = **皮肤 prefab + 槽位绑定组件**：标记组件声明"哪个节点是什么"，
行为逻辑（打字机/名牌显隐/头像裁切/箭头呼吸/出入场动画/选项演出）只认槽位引用，
不关心装饰节点——prefab 里想加几层美术图都行。

### 新增 / 修改文件

- **新增 `Script/VNDialogueSkin.cs` / `Script/VNChoiceSkin.cs`**：槽位声明组件。
  对话框皮肤按全画布坐标制作（panel 想放哪锚哪）；选项皮肤 buttonTemplate
  克隆制，容器挂 LayoutGroup 则排版交给它（入场改淡入缩放），否则以模板锚点
  为首项向下堆叠。头像避让改为**皮肤声明式**（portraitBodyInset/TagShift，
  0 = 排版固定不避让），基准位置绑定时采样。
- **`VNDialogueBox.cs`（核心重构）**：拆"构建"与"行为"。程序化构建装进
  DefaultSkin 子物体 + VNDialogueSkin 槽位声明，与美术 prefab 走**同一条
  Bind() 路径**。ApplySkin(null)=回默认（DefaultSkin 隐藏不销毁，根矩形还原）；
  切自定义皮肤时根拉伸铺满画布。台词中途切换安全：当前句在新皮肤上满字重现。
  打字速度跨皮肤保留。
- **`VNChoicePanel.cs`**：CreateSkinButton（克隆模板，按相对路径找文字/花费槽）
  与 CreateDefaultButton（老代码）双路径，FinishButton 共用收尾
  （CanvasGroup/特效控制器/点击/悬停）。模板根无 Image 时特效降级（fx 判空）。
- **`VNQuickToolbar.cs`**：SetDock(dock) 停靠支持——皮肤 toolbarAnchor >
  panel > 对话框根；旧皮肤 Destroy 是延迟的，本帧内重新挂接即可救出功能条。
- **`VNGameConfig.cs`**：UiSkinEntry{id,prefab} + dialogueSkins/choiceSkins
  注册列表 + FindSkin 静态查找。
- **`VNStage.cs`**：SetUiSkin(kind,id,line) 统一入口（default/空 = 回默认，
  未登记 id 报错并保持现状）；CurrentDialogueSkinId/CurrentChoiceSkinId 进
  存档快照，RestoreSnapshot 最先恢复皮肤（台词/选项直接落在正确皮肤上），
  且仅在与当前不同时才切换。
- **`VNScriptRunner.cs`**：Dispatch 加 `ui` 命令；RebuildStateBefore 把 ui
  记入调试重建快照。`VNSaveData` 加 dialogueSkin/choiceSkin（空 = 默认，
  旧存档兼容）。
- **新增 `Editor/VNUiSkinExporter.cs`**：Tools → VN Effects → UI 皮肤 UI Skins →
  Export Skin Prefabs。烘焙程序化贴图为 PNG（prefab 引用运行时贴图会
  Missing，这是 prefab 方案的前提）→ 生成 DialogueSkin_Default/Top、
  ChoiceSkin_Default/Right 四个 prefab（TMP 全部引用
  VNFontAssetBuilder.EnsureFontAsset() 的持久化字体）→ 自动登记进
  VNGameConfig（经典/顶部/右列，已有 id 不重复加）。重复执行安全。
- **`VNScenarioSchema.cs`**：登记 ui 命令（Scene 分类）。
- **`VNScenarioLinter.cs`**：Registry 收集皮肤 id；新规则 bad-ui-kind（Error）
  与 unknown-ui-skin（Warning，default 永远合法）。
- **新增 `Assets/Scenarios/UiSkinDemo.vn.txt`**：默认 → 顶部对话框 →
  右列选项 → prefab 经典 → 回默认 的完整演示。

### 技术决策记录

- **程序化默认也装进 VNDialogueSkin**：Bind() 只有一条路径，默认与自定义
  皮肤行为天然一致，不会出现"默认能跑皮肤坏了"的分叉维护。
- **皮肤实例铺满画布而不是嵌在对话框原矩形里**：prefab 作者按 1920×1080
  全屏坐标思考，"放顶部"就是把 panel 锚到顶——不用心算相对老矩形的偏移。
- **头像避让声明式而非代码写死**：自定义皮肤的排版主权在 prefab；
  想要避让就填 inset 值，不填就是固定排版。
- **切换即销毁选项**（ApplySkin 里 ForceClose）：选项按钮长在旧皮肤容器里，
  跨皮肤保活的复杂度不值得——ui 命令约定写在 choice 之前即可。

### 遗留

- 名牌底色仍是皮肤里的固定色，未接 VNCharacterDef.nameColor 按说话人变色
  （老版本也没接，非回归）。
- 选项皮肤堆叠方向仅纵向；横排需求出现时给 VNChoiceSkin 加 direction 字段。
- 标题菜单/存读档等系统面板不在本次皮肤范围（它们是功能 UI，非演出 UI）。
- 未编译验证（Unity 编辑器占用工程）；已按现有组件逐 API 核对。

---

## 八十三、系统菜单全局 UI 皮肤（2026-07-20，分支链 `agent/system-ui-skin-*`）

### 需求与结论

在八十二章“对话框 / 选项面板可按剧本切换皮肤”之外，标题、设置、CG、回想、
快捷功能条、存读档和属性界面也要能直接编辑 prefab 外观。系统菜单采用用户确认的
**唯一全局主题**，不增加剧本切换命令；“状态面板”拆成两份独立 prefab：始终可见的
顶部属性 HUD，以及点击“属性”后打开的完整属性页。Backlog 保持简单的说话人 + 正文
纯文字列表。

整体仍采用 **prefab + 槽位绑定组件 + 程序化兜底**：运行时代码只负责功能、数据和动画，
美术节点、图片、锚点、布局组和装饰层都留在 prefab；某个功能的 prefab 未配置或必需槽位
无效时，只让该功能退回原有程序化界面，不拖垮其他菜单。

### 分支与实现

- `agent/system-ui-skin-core`：新增 `VNSystemUiSkinSet`（唯一全局主题资产）、
  `VNSystemUiSkinBehaviour`（校验/安全实例化基类），`VNGameConfig.systemUiSkin` 为全局入口。
- `agent/title-menu-skin`：`VNTitleMenuSkin`；标题、版本、开始/继续/读档/鉴赏/设置/退出、
  退出确认框全部绑定槽位。自定义标题动画目标可选；标题页打开时会正确隐藏顶部属性 HUD。
- `agent/config-panel-skin`：`VNConfigPanelSkin`；四条滑杆、语言、全屏、关闭和背景关闭槽位。
- `agent/quick-toolbar-skin`：`VNQuickToolbarSkin` + `VNToolbarActionSlot`；按钮通过 action 枚举
  声明功能，可在 prefab 内任意重排、删减或改图标；Auto/Skip 激活色仍由逻辑同步。
- `agent/save-load-skin`：`VNSaveLoadSkin` + `VNSaveSlotSkin`；整个页面、动态存档卡模板和
  覆盖确认框均可编辑，缩略图/时间/末句数据仍由运行时填充。
- `agent/cg-gallery-skin`：`VNCgGallerySkin` + `VNCgCellSkin`；网格动态模板、锁定层、计数、
  全屏 CG 查看器与前后翻页全部槽位化。
- `agent/backlog-skin`：`VNBacklogSkin` + `VNBacklogEntrySkin`；纯文字动态条目模板，保留滚动定位。
- `agent/stats-ui-skin`：`VNStatsHudSkin`/`VNStatsHudEntrySkin` 与
  `VNStatsPanelSkin`/`VNStatsPanelRowSkin`；顶部 HUD 和完整属性页分别配置，动态属性仍来自
  `VNStatDef`/flags，数值条与图标按原逻辑刷新。
- `agent/system-ui-skin-integration`：`VNSystemUiSkinExporter`；菜单
  **Tools → VN Effects → UI 皮肤 UI Skins → 系统主题：导出默认模板 System UI: Export Default Prefabs** 一次生成 8 个可编辑起步 prefab
  和 `VNSystemUiSkinSet_Default.asset`，并自动写入 `VNGameConfig`。重复执行会更新默认模板；
  **Validate Global Theme** 会检查 8 个 prefab 的组件与必需槽位。

默认资产目录：`Assets/VNEffects/SystemUISkins/`。建议复制默认 prefab 后再改图片和布局，
然后把副本拖回 `VNSystemUiSkinSet_Default` 对应槽位；不要删除皮肤根上的绑定组件或清空必需
引用。装饰节点不受限制，也无需登记。

### 兼容性与验证

- 不配置 `systemUiSkin` 时行为与旧版本一致；旧场景、旧存档和现有剧本不需要迁移。
- 系统主题不写进存档，也没有剧本命令；它是项目级美术配置。
- 动态列表（存档槽、CG、Backlog、属性）只克隆模板，模板本体始终保留且隐藏。
- 全部分支线性保留并推送，最终整合直接快进到 `main`，不创建 PR、不删除分支。

### 2026-07-20 修复：设置滑杆无法拖动（分支 `fix/config-slider-raycast`）

默认设置 prefab 的 Slider 根没有可被 `GraphicRaycaster` 命中的 Graphic，背景、填充和手柄又都
是非 Raycast Target，导致语言 Button 正常但四条 Slider 收不到 PointerDown/Drag。生成器现为
每个 Slider 加透明全尺寸 Image 命中层；`VNConfigPanel.BindSlider` 也会在运行时为用户自制 prefab
自动补齐缺失的透明命中层，因此不改视觉且不要求美术手动排查射线配置。

## 八十四、Claude 技能体系（.claude/skills）+ CLAUDE.md 瘦身（2026-07-20，分支 `agent/claude-skills`）

### 需求

项目文档已成体系（WhatAiDo 编年史 / ProjectCodeGuide 代码指南 / HowToUse 剧本教程 /
SetUpGuide 搭建教程），但 AI 助手每次会话要么全量背 CLAUDE.md（越来越臃肿），要么
临时翻文档找清单。本次把「会反复发生的 12 类任务」各做成一个按需加载的技能
（`.claude/skills/<名>/SKILL.md`），并给 CLAUDE.md 瘦身。

### 设计原则

- **技能 ≠ 文档副本**：薄壳结构 = 触发条件 + 铁律（内联写死）+ 操作清单 +
  指向权威文档的精确章节。文档仍是唯一真相，更新时技能不易失真。
- description 字段中英关键词混写，保证不同问法都能触发。
- 技能间用 [vn-xxx] 互相引用（如 vn-new-command 第 4 步指向 vn-save-compat）。

### 新增 12 个技能

| 组 | 技能 | 覆盖 |
|---|---|---|
| 流程 | vn-new-feature / vn-doc-update | 分支提交合并流程 / WhatAiDo 章节模板与文档同步表 |
| 代码扩展 | vn-new-command / vn-new-event-module / vn-new-effect / vn-save-compat / vn-editor-extend | 对应 ProjectCodeGuide 菜谱一/二/五 + 存档三处同步 + 编辑器硬规则 |
| 内容创作 | vn-write-scenario / vn-add-assets / vn-ui-skin / vn-localize | 剧本语法要点与 Lint / VNGameConfig 素材登记 / 两条皮肤线 / Extract-Validate 流程 |
| 调试 | vn-debug | 排错顺序、从选中行播放边界、csproj 编译验证 |

### CLAUDE.md 瘦身（细节下沉技能，正文留索引）

- 头部注记加入技能机制说明；「工作规则」5/6 两条合并为指向 vn-new-feature。
- 新增「技能索引」表（12 行，何时用哪个）。
- 「关键技术约定」压缩为一行版（展开细节在 vn-new-effect / vn-ui-skin / vn-localize）。
- 「剧本系统」的 12 大段已完成子系统 bullet 压缩为一张「子系统 | 一句话 | WhatAiDo 章节」表。
- 「剧本可视化编辑器」与「从选中行播放」两大节压缩为 5 行概述 + 指向
  vn-editor-extend / vn-debug（硬规则原文都收进了这两个技能，无信息丢失）。
- 组件速查表、容器层级图、目录结构保持不动（每次会话都需要的定位信息）。

### 技术决策

- 章节编号提醒写进 vn-doc-update：历史上出现过重号（两个「三十九」、两个「六十九」），
  技能要求追加前先看文件末尾确认编号。
- 系统菜单主题「不进存档」与对话/选项皮肤「进存档」的区别在 vn-ui-skin 和
  vn-save-compat 两处都有表格，因为这是最容易配错的一对。

## 八十五、装备系统（7 部位装备栏 + 背包界面改造）（2026-07-20，分支 `agent/equipment-system`）

### 需求

主人公可以装备买到的道具：背包界面（I 键）左侧是道具一览，右侧是 7 个装备栏
（头部/脸部/上半身/手部/下半身/脚部/特殊）。道具行右键弹出 装备/卸下/使用 菜单，
底部介绍区显示道具介绍文与装备加成。装备可以提升属性（加魅力等）或携带特殊效果
（金钱加倍、每周行动力+1 等）；消耗品可以直接使用（精力充沛剂 → 行动力+2）。
背包外观支持全局主题 prefab，方便后续自定义（用户指定）。不做剧本 equip/unequip 命令。

### 文件改动清单

**新增**
- `Script/VNEquipment.cs`：装备核心（纯静态）。Equip/Unequip/Use/HandleItemLost/
  RecomputeEffects；道具查表入口 `ItemResolver` 由 VNInventory 注入。
- `Script/VNInventorySkin.cs`：背包皮肤槽位声明（面板根/标题/道具列表/7 装备格/介绍区，
  含 CollectValidationErrors）。
- `Script/VNInventoryRowSkin.cs` / `VNInventorySlotSkin.cs`：道具行 / 装备格子槽位。

**修改**
- `Script/VNShopDef.cs`：新增 `VNEquipSlot` 枚举（1~7 部位）；Item 加装备/使用字段：
  `equipSlot`、`statBonuses`（属性加成）、`passiveEffects`（特殊效果，含 label 英日文）、
  `useOps`（使用效果）、`consumeOnUse`；新增嵌套类 `StatOp`/`PassiveEffect`。
- `Script/VNInventory.cs`：I 键物品栏全面改造成背包界面（双栏+右键菜单+介绍区）。
  皮肤 prefab 优先（`VNSystemUiSkinUtility.Instantiate`），缺失/校验失败退回程序化默认 UI
  （两条路径都产出 `VNInventorySkin` 引用，下游单一代码路径）。右键用
  `IPointerClickHandler`（ClickRelay 内部类）区分左右键，Button 只负责按压视觉。
- `Script/VNSystemUiSkinSet.cs`：加 `inventoryPrefab` 槽位。
- `Script/VNShopModule.cs`：卖出后 `VNEquipment.HandleItemLost`（卖光装备中的道具强制卸下）。
- `Editor/VNSystemUiSkinExporter.cs`：`BuildInventory/BuildInventoryRow/BuildInventorySlot`
  导出默认背包模板（第 9 个系统 UI prefab），Validate 同步。
- `Resources/VNLocale/ui.zh|en|ja.txt`：inventory.hint、equip.* 系列 key（部位名/菜单/飘字/介绍区）。
- `Assets/VNEffects/Shops/服装店.asset`：示例数据——蝴蝶结发饰（头部 魅力+3）、
  洋装（上半身 魅力+8）、神秘挂坠（特殊 金钱加倍效果）、新商品精力充沛剂（使用 行动力+2）。

### 技术决策与取舍

1. **状态全存 VNFlags，存档/if/调试零改动**：
   - `装备_<道具id>` = 部位编号（1~7，0=未装备）→ 剧本可 `if 装备_幸运项链>=1`；
   - `装备实增_<部位>_<属性>` = 穿上时实际生效增量，卸下按记录扣回——解决钳制导致的
     穿脱不对称（98 穿+5 钳到 100，卸下只扣实际生效的 2）；扣回同样过钳制不跌破下限；
   - `装备效果_<效果id>` = 已装备道具特殊效果合计，穿脱后整体重算。
2. **特殊效果生效逻辑留在剧本**（与概率表写剧本同哲学）：金钱加倍 → 打工结算处
   `if 装备效果_金钱加倍>=1 jump 双倍`；每周行动力+1 → 周初脚本判断后 stat 行动力 +1。
3. **道具数据扩展在 VNShopDef.Item 上**：VNInventory 本就把登记商店当道具目录用
   （VNGameConfig.shops），不卖的道具登记一家不开门的「目录商店」即可，零迁移。
4. **属性加成直接写 stat flag**：if 分支/战斗/商店全系统无感知享受加成，不改任何读取方。
5. 同部位换装自动先卸旧（静默）再穿新；使用/卖出后持有归零自动强制卸下。
6. UI 操作（装备/使用）不在剧本里，「从选中行播放」的重建不还原——与商店购买同理，可接受；
   正常存读档因 flags 全量序列化完全无损。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` 0 错误。
- Unity 内：Tools → VN Effects → UI 皮肤 UI Skins → 系统主题：导出默认模板 System UI: Export Default Prefabs 重新导出
  （新增 Inventory_Default.prefab 并登记），Validate Global Theme 应通过 9 项。
- Play 后按 I：买入蝴蝶结发饰 → 右键装备 → 魅力+3 飘字、头部格出现道具、列表行出 E 标；
  再右键卸下 → 魅力-3；精力充沛剂右键使用 → 行动力+2、数量-1；
  存档读档后装备状态与加成保持。

## 八十六、排程/结算弹窗接入系统皮肤 + 判定冲条演出（2026-07-20，分支 `agent/plan-result-ui-skin`）

### 需求/背景

周日程排程面板（VNPlanModule）与结果结算大弹窗（VNResultPopupModule）此前是纯程序化
UI（颜色/贴图硬编码在代码里），改外观必须改代码，是八十三章系统皮肤化欠下的技术债。
本次把两者接入 VNSystemUiSkinSet 全局主题（槽位缺失退回程序化默认），并给结算弹窗加
「判定冲条」悬念演出：大字揭晓前先跑一段进度条 0→100 冲刺 + 数字跳动（纯演出，不代表
任何数值），冲满闪白淡出后 GRADE 大字才砸落（good/great 照旧接星光爆发）。

### 文件改动清单

**新增**
- `Script/VNPlanSkin.cs`：排程面板皮肤槽位声明（面板根/标题/左行动列容器+行模板/
  右日程格容器+格模板/重置/确定按钮；label 槽可选）。
- `Script/VNPlanActionRowSkin.cs`：行动行槽位（按钮/名字必需；图标、独立收益文案槽可选，
  收益槽留空则拼进 nameText 富文本小字与程序化默认一致）。
- `Script/VNPlanSlotRowSkin.cs`：日程格槽位(按钮/天数/行动名必需)；background 可选 =
  空格/已排状态改色目标，emptyColor/filledColor 两个颜色字段随模板配置。
- `Script/VNResultPopupSkin.cs`：结算弹窗槽位（仅面板根+等级大字必需，其余全可选降级；
  冲条三槽 barRoot/barFill/percentText 齐全才播悬念演出；burstOrigin 可自定义星光原点）。

**修改**
- `Script/VNSystemUiSkinSet.cs`：加 `planPrefab`、`resultPopupPrefab` 槽位（第 10/11 个）。
- `Script/VNPlanModule.cs`：BuildUi 拆成 BuildFromSkin（模板行克隆+槽位接线）/
  BuildDefault（原程序化路径）；日程格状态列表改为 _slotTexts/_slotImages/_slotRects
  三平行表（皮肤格没配背景槽时 _slotImages 条目为 null，刷新时跳过改色）；
  空/已排颜色改实例字段，皮肤路径从格模板读取。面板弹入动画两条路径共用。
- `Script/VNResultPopupModule.cs`：接入皮肤（BuildFromSkin/BuildDefault 两路径）+
  判定冲条 PlaySuspense（DOVirtual.Float 0→100、Ease.InQuad 加速冲刺 0.9s、数字跳动、
  冲满数字 punch + 填充闪白、整组 Graphic 淡出后回调揭晓）；大字/等级小字/继续提示
  改为揭晓时才激活；`_shownAt` 初始化为 float.MaxValue 挡住揭晓前的点击，Reveal 时
  改为当前时间再按 inputDelay 防误触；PlayStarBurst 改为以任意 RectTransform 为原点
  （皮肤可用 burstOrigin 指定，默认大字位置）。新 Inspector 参数 suspenseDuration。
- `Editor/VNSystemUiSkinExporter.cs`：`BuildPlan`/`BuildPlanActionRow`/`BuildPlanSlotRow`/
  `BuildResultPopup` 默认模板；ExportAll 纳入两个新 prefab；Validate 增至 11 项；
  **新菜单 Export Event Panel Prefabs (Plan & Result)** 只导出这两个、不碰其余 9 个
  （用户已手改过其他 Default prefab，全量重导会覆盖）。

### 技术决策与取舍

1. **冲条做进结算弹窗而不是排程模块**：判定发生在剧本 `flag rand:` 掷骰之后、
   `event result` 弹窗时，排程面板只管排格子，悬念点在揭晓一刻。
2. **冲条对四个等级一视同仁**（fail 也冲满）：条是「判定中……」的仪式感，不是分数；
   填充色用中性金色而非等级色，避免揭晓前剧透。
3. **皮肤降级粒度**：结算弹窗只有面板根+大字必需，皮肤没配冲条三槽就直接揭晓
   （不在皮肤上叠程序化冲条，避免和作者布局打架）；排程面板行/格模板必需
   （没有模板整个面板无法工作，整体退程序化）。
4. **barFill 用 anchorMax.x 驱动**并保留皮肤作者的纵向锚点，作者只需做一个
   左锚定填充条，不限制其内部结构。
5. **不进存档**：全局主题本来就不进存档；冲条是瞬时演出无持续状态，
   存档/调试重建（RebuildStateBefore）无需任何改动。无新剧本命令，Parser/编辑器
   Schema/Lint 均不涉及。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` 0 错误（98 个警告均为
  既有过时 API 警告）。
- Unity 内：Tools → VN Effects → UI 皮肤 UI Skins（系统主题那半截） → **Export Event Panel Prefabs
  (Plan & Result)** 导出 PlanPanel_Default / ResultPopup_Default 并登记；
  Validate Global Theme 应通过 11 项。
- Play 跑 WeekPlanDemo.vn.txt：排程面板外观来自 prefab（改 prefab 颜色再进验证生效）；
  结算时先见数字 0→100 冲刺 + 填充条加速，冲满闪白后 GREAT!! 砸落 + 星光爆发；
  冲条阶段点击无效，揭晓后点击/回车/空格正常关闭。
- 把 VNGameConfig.systemUiSkin 清空再跑：两界面退回程序化默认 UI，冲条演出同样存在。

## 八十七、立绘漫符系统（汗滴 / 井字怒气 / 感叹号 …）（2026-08-02，分支 `agent/manga-marks`）

### 需求 / 背景

视觉美化分析里排在「立绘表现力」一档的低成本高收益项：在立绘上叠一枚漫画符号
（汗滴、井字怒气、感叹号、问号…），日式 VN 表现力的核心语汇之一。

用户明确划定了范围：**不做立绘分层部件化（Layered Sprite）**，只要「在立绘指定位置
叠一张额外的符号图」。四个设计点由用户拍板：

1. 符号图 = **程序化生成 + 可选素材覆盖**（延续项目零美术依赖的约定）
2. 位置 = **角色资产配默认锚点，剧本可临时覆盖**
3. 调用 = **新命令 `mark`，默认一次性，可 `keep` 常驻**
4. 符号清单 = 点名的四个 + 情感类 + 状态类，共 **11 种**

### 文件改动清单

**新增**

- `Script/VNCharacterMarks.cs` —— 漫符组件本体。`VNMarkKind` 枚举（11 种）、
  英文正名 + 中文别名解析表、`Show/ShowInstant/FadeOut/ClearAll/ClearImmediate`、
  常驻符号序列化（`SerializeKeep`）。

**修改**

- `VNProceduralTextures.cs` —— 新增漫符贴图生成区（约 260 行）：
  `MarkSprite(kind)` 懒加载缓存 + `HardMark`（4×4 超采样 + 圆核膨胀描边）+
  `SoftMark`（柔边 alpha）+ 形状基元（圆/胶囊/圆弧）+ 11 个符号的形状函数。
- `Script/VNCharacterDef.cs` —— 加「漫符」区：`markAnchor`（归一化偏移，默认
  `(0.2, 0.36)`）、`markScale`、`markSprites`（自定义图覆盖列表 + `MarkOverride` 类）。
- `Script/VNStage.cs` —— `ActiveCharacter.marks` 字段、`CreateCharacter` 挂组件、
  `Mark()` API、`CaptureSnapshot` 写常驻符号、`ShowInstant` 重载还原符号。
- `Script/VNSaveSystem.cs` —— `CharSave.marks`（英文正名逗号串，旧存档为空即无符号）。
- `Script/VNScriptParser.cs` —— 关键字加 `mark`。
- `Script/VNScriptRunner.cs` —— `Dispatch` 的 `mark` case、`ParseMarkPos` 解析
  `pos:x,y`、`RebuildStateBefore` 的 `mark` case、`RebuildMarkState`、
  `RebuildShowState` 补上常驻符号的沿用。
- `Editor/VNScenarioSchema.cs` —— `MarkNames` + `mark` 命令参数模式登记。
- `Editor/VNScenarioEditorWindow.cs` —— 命令中文名「漫符」+ `MarkTranslations`
  下拉中文对照 + `IsMarkParameter`。
- `Editor/VNScenarioLinter.cs` —— `bad-mark` / `bad-mark-mode` 两项检查。
- `HowToUse.md` / `Demo.vn.txt` / `CLAUDE.md` / `ProjectCodeGuide.md` —— 文档同步。

### 技术决策与取舍

1. **沿用「透明画布叠加层」而不是新做一层**：符号是角色 GameObject 的子物体，
   uGUI 天然画在立绘之上，且跟着立绘一起震动/移动/缩放。与嘴部叠加层一样
   共用 `VNImageEffectController.Mat`，于是出场溶解、退场淡出、色调匹配
   全部自动生效 —— 退场路径一行代码都不用写。

2. **同步语义的取舍（本次最需要记住的一点）**：项目铁律是「命令默认同步等待，
   行尾 `@` 异步」。但一次性漫符全程约 1.6 秒，默认同步就会把对白卡住。
   解法是让 `Show()` 返回的 Sequence **只包含弹出段（约 0.28 秒）**，
   停留与消失挂在独立的 `DOVirtual.DelayedCall` 上。这样既没有破坏全局语义
   （`@` 照常生效），默认写法也不会拖节奏。

3. **描边走形态学膨胀而不是 SDF**：`?` `!` `♪` 这类符号是多段基元拼的，
   逐段算 SDF 拼不出连续外轮廓；先超采样出覆盖率再用圆核膨胀取最大值，
   任意形状都能拿到一圈干净描边，代价只是生成时多跑一遍（懒加载，一次性）。

4. **不做 `emote` 自动联动**：用户选的是「新命令」而非「挂在 emote 上」，
   所以 `emote 小雪 Angry` 不会自动冒怒气符号 —— 保留作者的显式控制。
   将来若要联动，在 `VNCharacterEmotes` 各动作里调 `marks.Show()` 即可，
   是加法不是改法。

5. **红晕/蒸汽不做「自动贴脸」**：不同构图的立绘头部位置差异很大，
   与其做一套猜不准的隐藏偏移，不如让作者显式写 `pos:0,0.33`。
   规则简单可预测，胜过藏起来的魔法。

6. **只有 `keep` 进存档**：一次性符号播完即逝不是持续状态，
   存档快照与调试重建（`RebuildMarkState`）都只处理常驻符号。
   `RebuildShowState` 补了一条：已在场角色重播 `show` 时把常驻符号带过来，
   与运行时 `VNStage.Show` 不清符号的行为保持一致。

### 验证方法

- `dotnet build Assembly-CSharp.csproj` / `Assembly-CSharp-Editor.csproj`
  均 **0 错误**（103 个警告全为既有的 `FindFirstObjectByType` 过时警告）。
- Unity 内跑 `Demo.vn.txt`：告白段会依次出现 感叹号 → 汗滴 → 常驻爱心；
  退缩线里 `mark 小雪 clear` 会把爱心清掉并冒省略号。
- 存档验证：在常驻爱心亮着时 F5 存档 → 读档，爱心应原样挂回（无弹出动画）。
- 校验器：把符号名故意写错跑 Ctrl+Shift+L，应报 `bad-mark` 并列出可用名。

## 八十八、限时问答事件模块（event quiz）（2026-08-02，分支 `agent/quiz-event`）

### 需求 / 背景

用户要「限时问答小游戏：出一道题给三个选项，限时十五秒」。开工前先把会影响架构的
六个点问清楚，用户逐条拍板：

1. 题目数据 = **题库资产 VNQuizDef**（不是剧本行内联）——题多时好管理、能随机抽题
2. 一次事件 **支持连答 N 题**（`count:1` 即退化成单题，一套代码覆盖两种需求）
3. 结果 = **三档结果行 + 同时写成绩 flag**（分支好写，细分也不堵）
4. 表现 = **先程序化 UI**（暂不做皮肤槽位）+ **倒计时紧张演出**
5. 超时 = **按答错处理**（不单开「超时」分支）
6. 属性联动 = **每题单独配奖励**（难题可以给得多，答错也能扣）

### 文件改动清单

**新增**

- `Script/VNQuizDef.cs` —— 题库资产（`VN/Quiz Definition`）。`Question`（三语题干 /
  2~4 个 `Option` / `answerIndex` / 解析 / 单题 `timeLimit` / `rewardOnCorrect` /
  `penaltyOnWrong`）+ 题库级 `defaultTimeLimit`、`flagPrefix`、`ValidQuestions()`。
- `Script/VNQuizModule.cs` —— 事件模块本体。选题 → 出题 → 倒计时 → 判定 → 反馈 →
  结算的状态机（`Phase` 四态），程序化 UI（暗幕 / 面板 / 倒计时条 / 4 个选项按钮 /
  反馈区），鼠标点击与数字键 1~4 双通道输入。
- `Scenarios/QuizDemo.vn.txt` —— 演示剧本：随机抽题三档结果 / `pick` 指定题号 /
  成绩 flag 细分三段。

**修改**

- `Script/VNGameConfig.cs` —— 玩法区加 `quizzes` 列表。
- `Editor/VNGameConfigTools.cs` —— 场景导入与目录扫描都带上题库定义。
- `Editor/VNEffectsDemoSetup.cs` —— `QuizzesDir` 常量、注册表登记 `quiz` 模板、
  `EnsureQuizDef()` 示例题库「社团常识」（5 题，够演示随机抽 3 题）+ `MakeQuestion()`
  辅助、Demo 剧本头部语法速查补 quiz。
- `Editor/VNScenarioSchema.cs` —— `event` 命令说明补 battle / quiz 两个内置模块。
- `Editor/VNScenarioLinter.cs` —— `BuiltinOutcomes` 加 quiz 三档结果名；
  新增 `unknown-quiz` 检查（题库 id 拼错 = 事件直接返回、整段问答被静默跳过）。
- `Resources/VNLocale/ui.{zh,en,ja}.txt` —— quiz.* 共 9 条 UI 字符串。
- `CLAUDE.md` / `HowToUse.md` / `ProjectCodeGuide.md` —— 文档同步。

### 技术决策与取舍

1. **成绩只走 flag，不进存档字段**：模块内部状态（第几题、剩余秒数）随事件结束即弃，
   与剧情通信只留 `<前缀>正确数` / `<前缀>总数` 两个 flag。这样存档/读档/调试重建
   三处一行都不用改（事件期间本来就不可存档）。前缀可被剧本 `flag:` 覆盖，
   多套题库同场出现时成绩互不覆盖。

2. **紧张演出不开 Tween**：最后 3 秒的变红 / 数字脉动 / 面板轻抖全部在
   `RefreshTimer()` 里按 `Time.unscaledTime` 现算。每帧新建补间会堆积几百个 Tween，
   而这类周期性效果本来就只是一个正弦函数。

3. **`pick` 的题号按资产原始顺序数**：不是按「有效题」的顺序。Inspector 里第几条
   就是第几号，某题填一半也不会让后面的题号错位——所见即所得优先于实现方便。

4. **选项顺序不打乱**：用户没选这项。要加的话是在 `ShowQuestion()` 里洗一次
   显示索引映射，判定处换算回原始下标即可，属于加法。

5. **奖励复用 `VNShopDef.StatOp`**：项目里「属性 id + 增量」的条目类型已经存在
   （装备加成 / 道具使用效果都用它），再定义一个同形结构只会让 Inspector 手感不一致。
   写入统一走 `VNStatsHud.Apply()`（钳制 + 飘字），没有 HUD 时退回 `VNFlags.Add`。

6. **及格线放在剧本行**：`pass:` 是关卡难度而不是题库属性——同一套题在序章
   可以宽松、在终章可以严格。默认值 = 题数一半向上取整。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj` **0 错误**。
- Unity 内先跑 Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景 Create Script Demo Scene 重建场景
  （生成 `Assets/VNEffects/Quizzes/社团常识.asset` 并注册 `quiz` 模板），
  再用 Scenario Editor 打开 `QuizDemo.vn.txt` → ▶ 从选中行播放。
- 检查点：倒计时最后 3 秒变红脉动 / 答错时正确项高亮绿、误选项标红 /
  答对涨智力、答错涨压力的飘字 / 结算大字与三档分支 / `pick:3,4` 固定出那两题。
- 校验器：把 `id:社团常识` 改成不存在的 id 跑 Ctrl+Shift+L，应报 `unknown-quiz`；
  把 `* 全对` 写成 `* 满分`，应报 `bad-event-outcome`。

### 追加：增量安装菜单（同日）

用户指出重建场景会把手工整理好的 Hierarchy 全部打乱——这是 `Create Script Demo Scene`
内部 `NewScene(EmptyScene)` 的固有代价。为「只想让注册表多一条」的场合补了增量入口：

- 新增 `Editor/VNQuizInstaller.cs` —— **Tools → VN Effects → 场景装机 Install To Scene → 限时问答 Quiz Module**。
  只做三件事：在场景现有的 `VNEventRegistry` 下补一个禁用的 `QuizTemplate`
  （**带 RectTransform**）→ 确保示例题库存在并把工程里全部 `VNQuizDef` 填进模板 →
  登记进 `VNGameConfig`。支持 Undo、重复执行安全（已装过只刷新题库列表）、
  自动 `MarkSceneDirty` 并选中新建物体。
- `Editor/VNEffectsDemoSetup.cs` —— `QuizzesDir` / `EnsureQuizDef()` / `EnsureFolder()`
  改 `internal`，示例题库只有一份实现，两条入口共用。

**为什么模板必须带 RectTransform**：`VNQuizModule.BuildUi` 里直接
`(RectTransform)transform`，手工 Create Empty 得到的是普通 Transform，
运行时会抛 `InvalidCastException`。这也是「别手工接、用安装器」的主要理由。

将来再加事件模块时，照这个文件复制一份即可（约 100 行）；
真到了模块很多的那天，再合并成一个列出全部模块的通用安装器。

## 八十九、属性变动演出改造（VNToast 卡片队列 + HUD 就地反馈）（2026-08-02，分支 `agent/stat-feedback`）

### 需求 / 背景

用户在问答演示里看到「智力 +3」的提示，指出太简陋、没有美感（截图中它还正好和问答面板的
反馈区挤在一起）。查下来根因是 `VNToast` 的实现：**一行裸文字**，屏幕底部上方 300px 居中，
只有淡出、没有底板/图标/颜色，而且 `Show()` 里先 `Kill()` 再改文字 ——
**连续两条会互相覆盖**（答对同时涨智力、涨压力时只看得到最后一条）。

用户说了「比如说左上角弹窗」并让直接做，取推荐方案：HUD 就地演出 + 左上角堆叠卡片
两者都做，`VNToast` 一并升级（存档/任务/装备提示同样受益），先程序化不接皮肤槽位。

### 文件改动清单

**修改**

- `Script/VNToast.cs` —— 从「单条居中纯文字」重写为**左上角卡片队列**：
  `Card` 结构 + `_cards` 列表，新卡插入最上格、其余 `Relayout` 下移，
  上限 5 张（超出时最老的提前 `Dismiss`）；卡片 = 圆角底板 + 左侧竖色条 +
  图标 + 文字，宽度跟文字走（`HorizontalLayoutGroup` + `ContentSizeFitter`）；
  滑入/停留/滑出三段 Sequence。新增 `Show(msg, icon, iconColor, accent, hold)`
  与 `ClearAll()`，旧的 `Show(msg, hold)` 签名保持不变（全部既有调用点零改动）。
- `Script/VNStatsHud.cs` —— `HudEntry` 加 `icon` / `root` / `barTween` / `rollTween`；
  `RefreshHud` 的变化分支从「数字弹一下」扩成四件事：数值滚动（`RollValue`，
  `DOVirtual.Int`）、进度条补间推进、图标 `DOPunchScale`、`SpawnFloatingDelta`
  冒 `+3` 上飘；`Apply` 改调带图标与色条的 Toast 重载。

### 技术决策与取舍

1. **飘字挂 Canvas 根而不是 HUD 条目下**：HUD 走系统皮肤 prefab，皮肤里完全可能带
   `Mask` / `RectMask2D`，挂在条目里会被裁掉一半。改为取条目的世界坐标、用
   `canvasRect.InverseTransformPoint` 换算到 Canvas 本地坐标再定位 —— 同一个
   Overlay Canvas，缩放天然一致。

2. **两种颜色各司其职**：卡片图标底色 = `VNStatDef.color`（认出是哪个属性），
   左侧竖条 = 涨/跌色（一眼看出方向）。只用一种色就得二选一。

3. **卡片宽度用 `GetPreferredValues` 当场算**：`TMP.preferredWidth` 要等一次布局
   才有值，建卡当帧读到的是 0，会得到一排等宽窄条。

4. **新卡占最上格、旧卡下移**：最新信息的位置固定，视线不用追。
   代价是已有卡片每次都要补间移动一格 —— 5 张上限下开销可以忽略。

5. **保留旧 `Show(string, float)` 签名**：存档/任务/装备/快捷条几十处调用点
   一行都不用改，直接吃到新外观。

### 验证方法

- `dotnet build Assembly-CSharp.csproj` **0 错误**。
- Unity 内跑 `QuizDemo.vn.txt`：答对时左上角应滑入「智力 +3」卡片
  （绿色竖条），同时顶栏智力的图标弹跳、数字滚动、条推进、上方冒绿色 `+3`。
- 连答验证：一题同时涨/跌两个属性时，两张卡片应**纵向排队**而不是互相覆盖。
- 回归：F5 存档的「已保存」、`quest` 任务提示、背包穿脱装备提示都应变成同款卡片。

---

## 九十、SNS 手机聊天环节（sns 命令）（2026-08-02，分支 `agent/sns-talk`）

### 需求 / 背景

要做"我和女主角用手机通讯软件聊天"的环节：左侧女主、右侧自己，从上往下堆叠可滚动，
女主能发文字 / 语音 / 图片，玩家可以回复。

动手前先做了架构选型分析（三个方案），最终选**方案 C：SNS 是对话的另一种呈现层 +
少量专用命令**，而不是做成 `event` 事件模块。理由：

- `event` 是**原子**的 —— 事件没结束前 Runner 停在那一条命令上，
  一段 30 条消息的聊天玩家中途根本存不了档。这是 galgame 最不能忍的。
- 聊天内容如果放进 ScriptableObject 资产，就得像 `VNQuizDef` 那样自建三语字段，
  而写在 `.vn.txt` 里能白嫖现成的翻译抽取工具、`if` 分支、`flag`、跨文件跳转。

于是：`sns open` 之后**普通台词行**就渲染成气泡（说话者是"我/me/玩家/主角"= 右侧），
存档点、分支、翻译全部沿用普通台词的机制，一行都不用改。

### 用户拍板的边界（第一版不做什么）

| 议题 | 决定 |
|---|---|
| 聊天历史 | 只保留本次会话（`sns open ~ close`），但数据结构按"永久历史"设计（消息带全局自增 id + sessionId） |
| Skip / Auto | SNS 模式**不支持**，打开时自动关闭并屏蔽 A/S |
| 限时回复期间存档 | 禁止（同 event 模块） |
| 气泡对象池 | 不做（单次会话消息量不大） |
| 语音时长显示 | 不显示 |
| 消息进 H 键回想 | 不进（聊天窗自己就是历史记录） |
| 群聊 / 随时可开的手机 | 第一版不做，剧情驱动起步 |

### 文件改动清单

**新增**

- `Script/VNSnsMessage.cs` —— 一条消息的可序列化数据（id / sessionId / sender /
  kind(text·voice·image·system·time) / text / assetId / unlock / read / played）。
- `Script/VNSnsView.cs` —— 手机聊天视图：程序化 UI（手机外框 + 顶栏头像 + 滚动聊天区 +
  底部输入栏）、气泡渲染（文字 / 语音 / 图片 / 居中提示）、"正在输入…"三点动画、
  候选回复面板 + 倒计时条、图片大图查看、存档快照存取。
- `Assets/Scenarios/SnsDemo.vn.txt` —— 演示剧本（文字/语音/图片/typing/read/限时回复/分支）。

**修改**

- `VNScriptParser.cs` —— `sns` 进 Keywords；`sns reply` 复用 choice 的 `*` 子行机制；
  `sns time` / `sns system` 的自由文本从第 2 个 token 起不当 kwarg（否则 `23:47` 会被吃成键值对）。
- `VNScriptRunner.cs` —— `SnsCo` / `SnsReplyCo` / `SnsSayCo` 三个协程；
  `SayCo` 拆成 `NormalSayCo` + SNS 分支；`RebuildStateBefore` 加 sns 与 say 的静默重建
  （`ReplaySnsCommand` / `ReplaySnsSay`）；Update 里 SNS 打开时屏蔽 H/滚轮/J/C/I/G/右键/A/S，
  回复倒计时期间整体让位给面板。
- `VNStage.cs` —— `sns` 字段 + AutoWire 自愈创建 + `IsSnsOpen`；
  `CaptureSnapshot` / `RestoreSnapshot` / `ClearStage` 三处接上会话状态。
- `VNSaveSystem.cs` —— `VNSaveData` 加 `snsOpen / snsPeerId / snsSessionId / snsTitle /
  snsPlayerAlias / snsMessages`（旧存档全部缺省 = 未打开）。
- `Editor/VNScenarioSchema.cs` + `VNScenarioEditorWindow.cs` —— 登记 sns 命令与新分类「SNS」。
- `Editor/VNScenarioLinter.cs` —— `CheckSns` 规则组 + `late:` 标签的跳转目标解析。
- `Editor/VNEffectsDemoSetup.cs` —— Demo 剧本头部语法速查加 SNS 段。
- `Resources/VNLocale/ui.{zh,en,ja}.txt` —— `sns.*` 十条 UI 字符串。

### 技术决策与取舍

1. **手工测量布局，不用 `VerticalLayoutGroup` + `ContentSizeFitter`**：
   TMP 的 `preferredHeight` 在同一帧内不可靠，气泡宽度还要"短消息窄、长消息换行封顶"。
   改用 `GetPreferredValues(text, maxWidth, 0)` 当场量、自己排 y 坐标、自己算 Content 高度 ——
   代价是所有位置手写，换来的是完全可控、没有布局重建时序问题。

2. **消息存"显示文本"而不是中文原文**：翻译表按"出现序号"匹配 key，
   脱离命令流无法单句反查。所以存 `VNScriptLocale.TextOf(cmd)` 的结果。
   副作用：会话中途切语言时已发出的气泡保持原语言，重开会话即为新语言 —— 可接受。

3. **SNS Canvas 的 sortingOrder = 300**：盖住对话框（40）与事件层（60），
   但低于存读档/回想面板（600）—— 这样聊天中途按 F5，存档面板仍能叠在手机上面。

4. **回复按钮 / 语音气泡 / 图片气泡都挂 `Button`**：
   Runner 的推进判定用 `IsPointerOverInteractiveUi`（只拦 `Selectable`），
   所以点这些控件不会误推进剧情，点手机空白处才推进 —— 天然契合现成机制。

5. **`sns reply` 的 `timeout:` 必须配 `late:`**：超时（已读不回）没有去向就没意义，
   运行时会告警并退回不限时；Lint 提前抓。超时还会自动插一条居中系统提示"（你没有回复）"。

6. **静默重建走"快照"而不是"重放动画"**：`RebuildStateBefore` 把 sns 命令与 SNS 期间的
   台词都折算成 `snsMessages` 列表写进快照，`RestoreSnapshot` 一次性重建整屏气泡。
   读档与调试重建走同一条路径，不会出现两套逻辑不一致。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj` **0 错误**（新文件临时加进 csproj 编译后已还原）。
- Unity 内跑 `SnsDemo.vn.txt`：手机应从下方滑入，气泡逐条弹出并自动滚到底；
  点语音气泡播放（波条跳动、红点消失）、点图片看大图、8 秒不选走"已读不回"分支。
- 存档验证：聊到一半按 F5 存档 → 读档后应完整重建"到那条消息为止"的所有气泡。
- Lint 验证：`Ctrl+Shift+L` 应对 `sns` 拼错的子命令、缺 `late:` 的 `timeout:`、
  没有 `sns close` 的会话给出对应提示。

## 九十一、剧本编辑器 Enter 快捷插行（2026-08-02，分支 `agent/editor-insert-hotkey`）

### 需求 / 背景

用户反馈"在两行之间插一条新台词很麻烦，只能靠 Duplicate 复制一行再改"。

排查后确认**功能本来就有**：`ReorderableList` 底部 footer 的 `+` 按钮走
`onAddDropdownCallback → ShowAddMenu → InsertRow`，插入位置就是 `_list.index + 1`。
问题出在**入口距离**——`+` 在整个列表的最底部，剧本几百行时要滚到底才够得着，
点完还要在 GenericMenu 里挑一次类型，一共三步；而 Duplicate 按钮固定在顶栏，
一步就能"变出一行"，所以用户自然退化成用 Duplicate 当插入用。

结论：不是缺功能，是缺**顺手的入口**。用户拍板只做**键盘快捷键**这一条线
（顶栏按钮 / 行内悬停 ＋ / 右键菜单三个备选方案都不做），插入内容固定为**空台词行**，
并且插完**自动把键盘焦点送进新行的台词输入框**。

### 文件改动清单

**修改**：`Editor/VNScenarioEditorWindow.cs`（单文件）

- 新增字段 `_pendingInsertAt` / `_pendingFocusRow` / 常量 `SayFocusControl`。
- `HandleInsertKeys()`：Edit 页签下 `Enter` = 选区下方插入、`Shift+Enter` = 选区上方插入，
  无选中则追加到末尾；带 Ctrl/Cmd/Alt 一律不接管（不抢撤销等组合键）。
- `ApplyPendingInsert()` / `ScrollRowIntoView()`：真正插行 + 只在目标行不可见时才滚动。
- `OnGUI` 开头 Layout 事件调用 `ApplyPendingInsert()`，`HandleUndoKeys()` 后调 `HandleInsertKeys()`。
- `DrawRow` → `DrawSayRow(rect, r, index)` 多传行号，用于焦点定位。
- `RebindList()` 清两个 pending 字段（换文件/撤销重载后不能残留旧行号）。
- Edit 页 HelpBox 顶部补一行中文快捷键说明。

### 技术决策与取舍

1. **KeyDown 里绝不直接改 `_doc.rows` 长度**：IMGUI 的控件布局在 Layout 事件就定死了，
   在 KeyDown 事件里增删列表元素会让后续 `DoLayoutList` 的控件数量对不上，
   典型报错 `Getting control N's position in a group with only M controls`。
   所以 KeyDown 只把目标行号记进 `_pendingInsertAt` 并 `Repaint()`，
   到**下一个 Layout 事件开头**再执行插入。撤销快照顺序也因此正确：
   `_frameSnapshot` 此刻仍是改动前的文本，`MarkStructural()` 压进去的就是旧状态。

2. **在文本框里打字时不抢 Enter**：`EditorGUIUtility.editingTextField` 为真直接返回。
   实际手感是"打完字 → Enter 结束编辑 → 再 Enter 开新行"，
   两下 Enter 连击反而成了写连续对白最快的节奏；也彻底避免了误吞输入。
   （台词文本框是单行 `EditorGUI.TextField`，Enter 不会变成换行符，这一点是前提。）

3. **抢焦点必须等控件画完**：`GUI.SetNextControlName` 在 TextField 之前设名，
   `EditorGUI.FocusTextInControl` 放在 **Repaint 事件**里、控件已经存在之后调用，
   否则 IMGUI 找不到这个控件名，焦点会静默失败。

4. **滚动条按需才动**：新行通常就贴着当前选中行，画面本来就在视野内，
   无条件 `_pendingScrollY`（`FocusRow` 那套）会让每插一行画面都跳一下。
   改成只在新行超出可视区时才滚，且留 40px 余量。

5. **不动 footer 的 `+` 菜单**：快捷键只管最高频的台词行，
   插命令行仍走原来的 `+` 下拉，两条路径互不影响，回退风险最小。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj` **0 错误**
  （构建前 csproj 因未刷新缺 `VNSnsView.cs` / `VNSnsMessage.cs`，临时补进去编译后已还原）。
- Unity 内 Tools → VN Effects → 剧本编辑器 Scenario Editor 打开任意剧本：
  选中中间某行按 Enter，应在其**下方**出现一条空台词行、光标已在台词框里可直接打字；
  Shift+Enter 应插在**上方**；连按两次 Enter 可连续开行；Ctrl+Z 应能整条撤销掉。
- 回归：footer 的 `+`、Duplicate、多选拖动排序、`▶ 从选中行播放` 行为不变。

---

## 九十一、日常向出入场预设 + 出入场参数化（2026-08-02，分支 `agent/entrance-presets`）

### 需求 / 背景

之前的六个登场预设**全是华丽向**（都带粒子/光环/闪光），日常对话切立绘时太吵；
退场更是只有 `fade` / `dissolve` 两种，"华丽登场→随便消失"的观感断层明显。
而且 `show` / `hide` 除了选预设之外**一个参数都不能调**——
`PlayEntrance` 早就有 `durationScale` 参数，但从没暴露到剧本层。

本批加四个日常向登场 + 两个新退场，并把方向与时长做成剧本参数。

### 用户拍板的三个决策

| 议题 | 决定 |
|---|---|
| 方向默认值 | **按角色站位自动推断**：站左的从左边进来 / 往左边离开，中间位进场从下方；`from:` / `to:` 可覆盖 |
| 要不要加 `dur:` | 要。写的是**目标秒数**（内部按各预设的基准时长换算成倍率），不是倍率 |
| 日常向的常驻效果 | 关掉**周期扫光**，保留呼吸发光 + 悬浮 |

默认预设从 `DissolveGlow` 改成 `crossfade` —— 事先统计过：现有 26 条 `show`
**全部**显式写了 `with:`，所以改默认值不影响任何既有剧本的观感。

### 文件改动清单

**新增**

- `Assets/Scenarios/EntranceDemo.vn.txt` —— 逐个演示四个日常登场、方向推断/覆盖、
  `dur:` 控速、两个新退场，以及华丽向对照组。

**修改**

- `VNEntranceAnimator.cs` —— `VNEntrancePreset` 加 `Crossfade`/`SlideIn`/`StepIn`/`WalkIn`
  （放在枚举最前，Crossfade 成为默认）；新增 `VNExitPreset`（Fade/Dissolve/RunOut/Sink）
  与 `VNSide`（Auto/Left/Right/Top/Bottom）；`BaseDuration()` 基准时长表、
  `IsCasual()` 日常向判定、`PlayEntrance(preset, side, scale)` 与 `PlayExit(...)` 新签名
  （旧签名保留为重载，Demo 场景不用改）。
- `VNFootShadow.cs` —— 新增 `Impact(strength, duration)`：影子横向摊开 + 纵向压扁再缓回，
  供 `stepin` 在落地那一帧调用（LateUpdate 里乘进 localScale，不打断原有的悬浮联动）。
- `VNStage.cs` —— `SideFor()` 方向推断；`Show` / `Hide` 加 `from`/`to` + `duration` 重载；
  `ActiveCharacter.casualEntrance` 记录是否日常向登场。
- `VNScriptRunner.cs` —— show/hide 传 `from:` / `to:` / `dur:`；
  `RebuildShowState` 把 `with:` 折算成 `casualEntrance` 一起重建。
- `VNSaveSystem.cs` —— `CharSave.casualEntrance`（旧存档缺省 false = 保持原行为）。
- `Editor/VNScenarioSchema.cs` —— show/hide 补 `from:`/`to:`/`dur:` 参数与新预设候选。
- `Editor/VNScenarioEditorWindow.cs` —— 三张中文名表：`EntranceTranslations`
  （如 `Crossfade（原地淡入·日常）`）、`ExitTranslations`、`SideTranslations`，
  下拉里直接看得懂每个预设做什么。
- `Editor/VNScenarioLinter.cs` —— 通用 `CheckEnum<T>` + `CheckSide`：
  预设名/方向拼错直接报错（运行时只会静默退回默认预设，最容易漏）。
- `Editor/VNEffectsDemoSetup.cs`、`HowToUse.md`（show/hide 两节 + 速查卡 + 校验器表）同步。

### 技术决策与取舍

1. **`dur:` 是秒不是倍率**：写 `dur:1.2` 就是大约 1.2 秒。每个预设有一张基准时长表，
   内部换算 `scale = dur / base`。倍率对实现更诚实，但对写剧本的人不直观 ——
   代价是复合演出（含 Insert 重叠段）的实际时长只是近似值。

2. **`casualEntrance` 进存档**：不存的话，读档摆台一律走 `StartIdleEffects()` 默认参数，
   日常向登场的角色读档后会**突然开始每 7 秒闪一次**。一个 bool 字段解决，
   旧存档缺省 false = 保持原来的行为。

3. **`WalkIn` 用分轴 tween**：横向 `DOAnchorPosX` 匀速（走路不该有加减速），
   纵向 `DOAnchorPosY` 做步伐起伏，两者互不冲突；轻摆与踩地压缩用同一步长的 Yoyo，
   循环次数取偶数保证自然回位，`OnComplete` 再强制归零一次防抖动残留。
   `PrepareHidden()` 也补上了 `localRotation` 重置——出场被打断时不会留下歪着头的立绘。

4. **`StepIn` 不震屏**：落地只做脚下影的压扁扩散。屏幕震动留给将来的冲击型登场（slam），
   日常预设一震屏就不日常了。

5. **`Sink` 复用现成的 shader 通道**：`DOBlur` + `DOBrightness` + 下沉 + 淡出四条并行，
   没有新增 shader 或贴图。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj` **0 错误**。
- Unity 内跑 `EntranceDemo.vn.txt`：逐段验证四个日常登场、方向自动/手动、
  `dur:` 快慢、`runout` 往站位那侧跑出、`sink` 下沉模糊变暗。
- 重点看 `walkin` 结束时立绘**不歪不缩**（旋转/缩放归位）、
  `stepin` 落地时脚下影确实摊开一下。
- 存档验证：日常向登场的角色存档→读档后**不应该**出现周期扫光。
- Lint 验证：把 `with:` 故意写错（如 `with:crossfad`）应报 `bad-preset`。

---

## 九十二、飘落天气系统重做（落樱真实感 + 落叶/秋叶）（2026-08-03，分支 `agent/foliage-weather`）

### 需求 / 背景

用户反馈原来的落樱效果「有点假、不够专业、可调的地方不够多、樱花还是白色的」，
并希望加入落叶 / 秋叶 / 树叶等类型。逐条定位到旧实现
（`VNAmbientParticles.Preset.Petals`）的根因：

1. **白色的根因是混合模式，不是颜色配错**：`VN/Additive` 是 `Blend SrcAlpha One`，
   加法混合只能给背景加亮、永远无法遮挡背景。粉色 `(1, 0.72, 0.82)` 再乘
   `hdrBoost 1.8` 后三个通道全部 >1，经 Bloom（阈值 1.0）+ Tonemapping 必然被压成白。
   把 tint 调成任何粉色都没用。
2. **每片长得一模一样**：全屏共用一张柔边椭圆贴图（`VNProceduralTextures.Petal`），
   人眼几帧内就识破重复。
3. **纸片感**：只有 `rotationOverLifetime.z`（画面内平面自转），没有绕自身长轴的翻转，
   宽度与亮度恒定 —— 这是业余落樱与商业落樱最大的观感差距。
4. **集体同步**：全体共享一个 velocity 区间 + 一个全局 Perlin 噪声场。噪声空间连续，
   相邻花瓣会同步摆动，看起来像有人在整体拨一张网，最出戏。
5. **画面平**：只有一层，没有景深分层。
6. **不可调**：参数硬编码在 `Configure()` 的大 switch 里，剧本层只有 `weather petals` 一句话。
7. **凭空消失**：花瓣按 lifetime 淡出，与它在屏幕上的位置无关，经常飘到半空就没了。

### 文件改动清单

**新增（运行时）**

- `Art/Shaders/VNParticleAlpha.shader` —— 实体粒子的普通 Alpha 混合
  （`SrcAlpha OneMinusSrcAlpha`，会遮挡背景）；`_SoftBlur` 提供 5-tap 十字模糊给近景虚焦用。
- `VNFoliageTextures.cs` —— 叶型枚举 `VNLeafShape`（Sakura/Maple/Ginkgo/Broadleaf/Bamboo）
  + 程序化图集烘焙。图集布局 **列 = 12 翻转帧，行 = 4 形态变体**，
  RGB 存明暗（叶脉/折痕/根深尖浅/背面压暗），A 存形状，色相交给粒子 startColor。
- `VNWeatherDef.cs` —— 全部可调参数的 ScriptableObject（含五套内置预设，
  不建任何资产也能用）+ id 解析（认英文正名与中文别名）。
- `VNFoliageSystem.cs` —— 三层景深飘落系统：图集翻转、每粒子独立相位横摆、
  全局阵风、尺寸↔速度联动、地面堆积。

**新增（编辑器）**

- `Editor/VNWeatherPreviewWindow.cs` —— Tools → VN Effects → 预览 Preview → **天气预览 Weather Preview**：
  编辑模式下播放翻转帧预览（四种变体并排、按色带着色），运行模式下滑杆实时应用到场景，
  另存为资产 + 一键登记进 VNGameConfig。

**修改**

- `VNWeatherController.cs` —— 双后端：飘落类走 `VNFoliageSystem`，
  雨/雪/萤火虫仍走 `VNAmbientParticles`。统一入口 `SetWeatherId(id, …)` 三级解析
  （自定义资产 id → 内置叶型别名 → `VNWeather` 枚举名），新增 `CurrentId`、
  `Gust()`、`SetAmbient()`、`PreviewDef()`。
- `VNSakuraBurst.cs` —— 改走 `VNFoliageSystem`，自动继承全部底层改进；
  另加起手阵风冲击 + Burst、中途补两记阵风、尾声风力衰减、近景层权重大幅拉高。
- `VNAmbientParticles.cs` —— `Preset.Petals` 标注弃用（仅为旧场景兼容保留）。
- `Script/VNGameConfig.cs` —— 新增「飘落天气库」`weatherDefs`。
- `Script/VNSaveSystem.cs` —— `weather` 语义改为 id 字符串（旧存档的枚举名照常认得），
  新增四个覆盖参数字段（`weatherDensity/Speed/Size` + `weatherWindSet/Wind`）。
- `Script/VNStage.cs` —— 天气覆盖参数由 VNStage 统一持有；
  `SetWeather(id, …)` 命令入口、`ApplyWeather()`、`WeatherGust()`；
  CG 暂停恢复由 `_cgSavedWeather`（枚举）改为 `_cgSavedWeatherId`（字符串）。
- `Script/VNScriptRunner.cs` —— `weather` 命令支持 `density:/wind:/speed:/size:`；
  `RebuildStateBefore` 同步重放这四个参数。
- `Editor/VNScenarioSchema.cs` —— 新增 `VNParamSource.WeatherId`，weather 条目改四参数版。
- `Editor/VNScenarioDoc.cs`、`VNScenarioEditorWindow.cs` —— 天气候选与校验
  （`IsKnownWeatherId` 与运行时三级解析同构，中文别名也认）。
- `Editor/VNScenarioLinter.cs` —— 新增 `unknown-weather` 检查。
- `Editor/VNEffectsDemoSetup.cs` —— 生成 `VNParticleAlpha.mat` 并接到天气控制器与樱吹雪。
- `VNEffectsDemo.cs` —— 提示文字改显示 `CurrentId`（新叶型在枚举里统统算 Petals）。

### 技术决策与取舍

1. **翻转用图集序列帧，不用 Mesh 粒子**：Mesh + Custom Vertex Streams 能做真 3D 旋转，
   但要写 shader，且与 Screen Space-Camera Canvas 的排序、Bloom 配合都麻烦。
   序列帧方案纯程序化、零运行时开销（只是 UV），与项目「零美术依赖」的约定一致。
   实现靠 `SingleRow + rowMode Random`：每颗粒子随机抽一行（= 一种形态变体），
   在该行内播 12 帧完成一整圈翻转；`frameOverTime` 用「两条斜率不同的直线 + multiplier」
   表达随机翻转圈数，所以每片翻转快慢都不同。

2. **横摆写成「已存活时间的纯函数」**：`offset(t) = amp·sin(2πf·t + φ)`，
   每帧只加 `offset(t) - offset(t-dt)`，相位/频率/幅度全部由 `particle.randomSeed` 散列得到。
   **完全无状态** —— 不需要任何平行数组，粒子死亡重排也不会错位。
   这是替掉全局噪声场（集体同步的元凶）的关键。

3. **阵风把 Perlin 阈值化成脉冲**：直接用原始 Perlin 会变成「一直在晃」，反而不像风。
   `pow(clamp01(n·1.45 - 0.42), 1.7)` 让大部分时间接近 0、偶尔冲高，再用 Lerp 平滑跟随
   给起落加惯性。

4. **不 reparent 到 LayerBack/Mid/Front**：那三层是 Canvas（Screen Space-Camera）下的
   RectTransform，其 lossyScale 不是 1，粒子挂上去尺寸单位会全乱。
   三层改为独立世界空间物体 + `sortingOrder` 插进 UI 层之间（与旧天气一致）。

5. **地面堆积靠压缩 `remainingLifetime`**：`colorOverLifetime` 取值用
   `remaining/start` 的比例，把剩余寿命压到 1.4 秒就等于立刻推进到淡出段，
   不需要额外的状态标记；判定条件 `position.y <= groundY` 本身幂等，重复执行无副作用。

6. **存档用 bool 标记而不是 NaN**：`wind` 可以是负数（向左吹），没法用 0 表示「未覆盖」；
   而 `JsonUtility` 序列化 `float.NaN` 会写出非法 JSON。所以另加 `weatherWindSet`，
   旧存档缺省 `false` = 未覆盖。density/speed/size 本来就必须 >0，直接用 0 表示未覆盖。

7. **形状函数各用最省事的数学构造**：樱花瓣 = 两枚竖椭圆并集（顶端天然形成 V 缺口）；
   枫叶 = 五个裂片取极坐标最大值 + 正弦锯齿；银杏 = 张角随半径张开的扇形
   （小半径自动收窄成叶柄）+ 中央裂口；阔叶 = 纺锤形 + 中脉侧脉；竹叶 = 细长微弯。

8. **运动学差异比形状差异更重要**：花瓣轻（慢、幅大频低地飘），
   落叶重（快、幅小频高地抖 + 剧烈翻转 + 色差极大）。秋叶的「秋天味道」主要来自
   `colors` 渐变的大色差随机取色，而不是叶子的轮廓。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj` **0 错误**。
- Weather Preview 窗口：编辑模式下切五种叶型，看翻转预览
  **宽度随帧呼吸、背面明显更暗** —— 这两条对上了就不会是纸片。
- Play Mode：`weather petals` 应该看到**粉色**花瓣（不再是白的）、
  每片形状不同、时不时一阵风把整屏花瓣斜掠过去。
- `weather maple` / `ginkgo` / `leaves` / `bamboo` 各看一遍，落叶应明显比花瓣落得快、
  翻得凶、颜色杂，并在画面下缘堆积后淡出。
- 参数覆盖：`weather maple density:20 wind:-1.5 size:1.4` 立刻生效。
- 存档验证：飘落中存档 → 读档，天气 id 与四个覆盖参数都应复原；
  旧存档（`weather` 字段为 `"Petals"`/`"Rain"`）仍能正确读出。
- Lint 验证：`weather 枫葉`（错别字）应报 `unknown-weather`。

---

## 九十三、分层眨眼（眼部叠加图，与整张替换二选一）（2026-08-03，分支 `agent/blink-overlay`）

### 需求 / 背景

原有眨眼（第十九批 `VNCharacterBlink`）是**整张立绘替换**：到点把 `Image.sprite` 换成
一张完整的闭眼全身图，闭眼时间到再换回来。这要求每个角色额外准备一张与睁眼图
像素级对齐的整图，AI 生成立绘时很难保证除眼睛外一模一样，稍有偏差就"整个人抖一下"。

而说话口型 `VNCharacterMouth` 用的是另一条路：原立绘不动，在其上方叠一张**同画布
坐标的透明张嘴图**，只有嘴那一块有像素。这条路对素材友好得多。

本批把口型那套做法照搬给眨眼，作为**新增的第二种方式**：
原有整张替换的代码路径原样保留、行为一字不改，由角色资产上的 `blinkMode` 二选一。

### 文件改动清单

**新增**

- `Script/VNCharacterBlinkOverlay.cs` —— 分层眨眼组件。结构 = `VNCharacterMouth` 的
  overlay 建法（子物体 `BlinkOverlay`，anchor 全拉伸、`preserveAspect=false`、
  `raycastTarget=false`、共用 `VNImageEffectController.Mat`）+ `VNCharacterBlink` 的
  DOTween 计时序列（随机间隔 → 显示 → `blinkDuration` → 隐藏 → 重排）。
  只在默认表情工作；`blinkMode != Overlay` 时整个组件自然静默。

**修改**

- `Script/VNCharacterDef.cs` —— 新增枚举 `VNBlinkMode { FullSprite=0, Overlay=1 }`、
  字段 `blinkMode`（默认 `FullSprite`）、`blinkOverlaySprite`，
  以及便捷属性 `ActiveBlinkSprite`（按当前方式返回实际会用到的那张图）。
  `blinkIntervalMin/Max`、`blinkDuration` 两种方式**共用**。
- `Script/VNCharacterBlink.cs` —— `CanBlink()` 与 `ValidateSpriteAlignment()` 各加一条
  `blinkMode == FullSprite` 守卫，选了分层时完全让路。其余逻辑一字未动。
- `Script/VNStage.cs` —— `ActiveCharacter` 加 `blinkOverlay` 字段；
  `CreateCharacter()` 挂载并 `Initialize(img, def, c.fx.Mat)`；
  `ApplyExpression()` 的两个分支各补 `PrepareForExpressionChange()` / `SetExpression(isDefault)`。
- `Editor/VNCharacterVisualPreviewWindow.cs` —— 草稿加 `blinkMode` / `blinkOverlaySprite`
  （ReadFrom / 应用 / 脏检查三处同步）；眨眼区块改为先选方式再显示对应图槽位；
  「预览闭眼状态」在分层模式下改成**叠一层画**而不是换底图；新增素材要求 HelpBox、
  错位警告文案分模式、「选中闭眼图」按钮。

### 技术决策与取舍

1. **为什么开新组件而不是在 VNCharacterBlink 里加分支**：用户明确要求"整张替换的功能
   保留不让动"。两套的状态机不同（一个改 `Image.sprite` 要记 `_openSprite`/
   `_showingClosedSprite`，一个只开关 overlay 的 `enabled`），混在一起反而会互相牵连。
   现在两个组件都挂在角色上，靠 `blinkMode` 保证任何时候只有一个真的在跑。

2. **枚举默认值必须是 `FullSprite = 0`**：Unity 序列化的枚举缺省就是 0，
   所以所有现有角色资产不用改任何一个字段，行为与本批之前完全一致。

3. **overlay 共用 `c.fx.Mat`**：与口型层同一处理。溶解 / 闪白 / HSV / 模糊等单图特效
   是走材质属性的，不共用材质的话眨眼那一瞬间眼睛会"跳出"特效之外。

4. **只支持默认表情**：与整张替换版保持一致。其他表情眼部构图/角度不同，
   叠同一张闭眼图必然错位；需要的话未来再扩成「表情名 → 闭眼图」列表。

5. **单帧硬切而不是多帧序列**：与整张替换版行为对齐，素材只需 1 张。
   `blinkDuration` 默认 0.1s，人眼看来就是一次干净的眨眼。

6. **换表情时先收 overlay**：`ApplyExpression` 会生成旧表情残像做交叉溶解，
   若此刻闭眼层还开着，闭眼帧会被一起卷进溶解里。所以在残像生成前调
   `PrepareForExpressionChange()`。

### 素材要求（新方式）

- 闭眼图必须与**默认表情立绘保持完全相同的画布尺寸与 Pivot**，
  只在眼部留下像素、其余 alpha = 0（做法与张嘴图完全一样）。
- 眼部那块像素要能**完整盖住原立绘睁开的眼睛**（含眼白、瞳孔、睫毛，
  一般要连眼周皮肤一起画），否则会露出底下的眼睛。
- 尺寸/Pivot 不一致时，预览窗口与运行时都会给告警。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj` **0 错误**（109 条 warning 全是既有的
  `FindFirstObjectByType` 过时提示）。
- 回归：不改任何角色资产 → 原本会眨眼的角色行为完全不变（`blinkMode` 缺省 = 整张替换）。
- 新方式：角色资产把「眨眼方式」改成**分层叠加**、指定透明闭眼叠加图 →
  Tools → VN Effects → 预览 Preview → 角色立绘预览 Character Visual Preview 勾「预览闭眼状态」，
  应看到只有眼睛变了、身体一动不动；Play Mode 里 `show 角色` 后自动间歇眨眼。
- 表情切换：切到非默认表情应立刻停止眨眼且不残留闭眼层，切回默认恢复。
- 特效联动：眨眼期间跑 `fx dissolve` / `fx flash`，闭眼层应与立绘同步溶解/闪白。

## 九十四、液体喷溅（喷射 / 爆溅 / 溅到镜头玻璃上）（2026-08-04，分支 `agent/liquid-splash`）

### 需求 / 背景

要一个「水从某一点喷出来，而且有机会溅到屏幕上」的效果：既能鼠标点哪喷哪，
也能由剧本命令驱动；既要一次性大爆溅，也要能从一个点持续间歇地噗噗喷。

拆需求时的关键判断是：这**不是一个效果，是三层**，而且三层的实现手段互不相同。

| 层 | 内容 | 空间 | 手段 |
|---|---|---|---|
| A 喷射源 | 喷口的水花根部 | 世界 | 密集短命粒子 |
| B 飞行水珠 | 抛物线飞散的水滴 | 世界 | 拉伸公告板粒子 |
| C 镜头水渍 | 溅在「摄影机玻璃」上挂着往下淌的 | **屏幕** | uGUI 元素池 + 手动模拟 |

很多人做这个效果失败，是想用一个粒子系统全包。A/B 做得再好、没有 C，观感只是
「有水在飞」；只有 C 没有 A/B，水会像凭空出现在屏幕上。**C 层才是「溅到屏幕上」
这句话真正指的东西，而它不该是粒子**——水渍要挂住、要按各自节奏开始下滑、
要留一条越来越淡的痕、要慢慢干，全是逐个体的状态机，ParticleSystem 的曲线模型
表达不了。

### 文件改动清单

**新增**

- `VNLiquidPreset.cs` —— 四套内置液体预设（water / blood / ink / slime）+ `VNLiquidArgs`
  参数结构。预设覆盖空中飞行与屏幕水渍两段的全部手感参数；`VNLiquidArgs` 由剧本层、
  舞台层、存档重建三方共用，避免各写一份默认值而慢慢漂移。
- `VNLiquidSplash.cs` —— 舞台层喷溅。三个发射器：Body（`VN/ParticleAlpha` 拉伸公告板
  主水珠）/ Glow（`VN/Additive` + HDR 高光）/ Splinter（低速碎珠）。提供 `Burst`
  一次性爆溅、`StartSpray`/`StopSpray` 间歇喷射、`SetClickMode` 点击喷水。
- `VNWetScreen.cs` —— 屏幕层水渍。对象池 + 每帧手动模拟的四段状态机：
  撞击形变 → 挂住 → 下滑拖痕 → 蒸发。默认排序 30（让开对话框 40），
  `liquid cover on` 切到 50 盖住对话框。
- `Assets/Scenarios/LiquidDemo.vn.txt` —— 六段演示剧本（爆溅 / 间歇喷 / 四种液体 /
  盖对话框 / 湿镜头 / 点击喷水）。

**修改**

- `VNProceduralTextures.cs` —— 新增 5 张程序化贴图（`LiquidBlob` 头重尾轻的水珠、
  `LiquidSplinter` 碎珠、`WaterDrop` 假折射水渍、`DropSpec` HDR 高光、
  `LiquidStreak` 水痕）+ `GenerateRgba` 彩色生成器重载。
- `Script/VNStage.cs` —— `liquidSplash`/`wetScreen` 两个引用 + AutoWire 自动接线（含
  两层互连）+ `Liquid()` 命令入口 + `ResetLiquid()` + `CaptureLiquid`/`RestoreLiquid`
  存档三处 + `ResetEffects` 联动。
- `Script/VNSaveSystem.cs` —— `VNSaveData.LiquidSave` 子结构（spray/click/wet/cover
  四个持续开关及其参数）。
- `Script/VNScriptParser.cs` —— 关键字 `liquid`。
- `Script/VNScriptRunner.cs` —— `case "liquid"` 派发 + `ParseLiquidArgs` + 调试重建
  静默重放 + `reset effects` 清空 + **点击喷水模式下屏蔽左键推进**。
- `Editor/VNScenarioSchema.cs` / `VNScenarioEditorWindow.cs` —— 命令模式登记（11 个参数）
  + 中文名「液体喷溅」。
- `Editor/VNScenarioLinter.cs` —— 4 项检查：未知子命令、未知液体类型、x/y 超出屏幕
  比例范围、开关位不是 on/off。
- `Editor/VNEffectsDemoSetup.cs` —— 生成器创建两个组件并接线；`AssignSourceMaterial`
  拆出按字段名的 `AssignMaterialField`（`VNLiquidSplash` 要 alpha + additive 两份材质）；
  材质目录与 `EnsureMaterial` 开放给增量安装器共用。
- `Editor/VNLiquidInstaller.cs`（新增）—— 增量安装器
  （Tools → VN Effects → 场景装机 Install To Scene → 液体喷溅 Liquid Splash）。**这条不是可选的**：
  `VNStage.AutoWire` 只能「找得到才接」，老场景里根本没有这两个物体时
  `liquid` 命令会静默无效果——每个分支都是 `if (xxx == null) break;`，
  连报错都没有。而 Create Script Demo Scene 会 NewScene 重造、丢掉手工整理过的
  Hierarchy，为了两个物体重建整个场景代价太大。照 `VNQuizInstaller` 的思路只做加法。
- `VNEffectsDemo.cs` —— 演示按键 `` ` `` 鼠标处爆溅 / F1 间歇喷射 / F2 换液体 / F3 湿镜头。
- `Assets/Scenarios/Demo.vn.txt` —— 头部语法速查补 `liquid`。

### 技术决策与取舍

**1. C1 假折射，不做真折射。** 真实水滴是凸透镜，要折射它背后的画面。但 Canvas 里的
shader 拿不到已渲染的背景（URP 无 GrabPass），要么加一个相机把背景渲进 RT，要么改
渲染架构。这次按需求选了最省的一条：把「玻璃感」全部烘进 `WaterDrop` 贴图的 RGB
剖面——**中心压暗 0.5 + 内侧亮环 + 最外圈急剧变暗到 0.16 的菲涅尔暗边**，再叠一层
HDR 高光吃 Bloom。代价是看不见水滴里倒立的背景，正常观看距离下几乎分辨不出。
真折射的接法已在预研里留好（`_DropletNormal` 走 `VNImageEffect` 局部 UV 偏移，
或第二相机渲背景 RT + `uv = center - (uv-center)*k`，注意是**减号**，凸透镜成像倒立），
要升级不需要重构现有代码。

**2. HDR 只能挂材质，不能挂顶点色。** 这条踩了两次：`RawImage.color` 与粒子
`startColor` 都是顶点色，>1 的分量会被钳掉，挂在那里等于没有 Bloom。
最终：水渍高光按液体类型各建一份材质，`_TintColor` 承载「什么颜色、多亮」，
顶点色只承载「淡入淡出到几成」；粒子这边反过来——材质给一个固定的白色 HDR 天花板
（`glowHdrCeiling`），粒子 `startColor` 只给色相和 0~1 的相对亮度，
这样四种液体**共用一份材质也不串色**，切换液体时还在飞的粒子也不会突然变颜色。

**3. 拉伸公告板是水感的一大半。** `ParticleSystemRenderMode.Stretch` +
`velocityScale`。球形粒子无论怎么调参都像泡泡不像水，这条比颜色重要得多。
配套的 `LiquidBlob` 贴图做成左圆右尖，拉伸后自然得到「头重尾轻」而不是对称胶囊。

**4. 主体走 `VN/ParticleAlpha` 而不是 `VN/Additive`。** 沿用落樱那次的教训：
水是实体、要遮挡背景，加法混合只能加亮。但水又确实反光，所以拆成 Body + Glow
两层同时发射——单一混合模式表达不了「既遮挡又反光」。

**5. 液体的「黏度」是四个参数的合谋**，不是一个。`gravityScale`（下坠快慢）、
`stretch`（空中被拉多长）、`dripSpeed`（在镜头上往下流多快）、`drySeconds`（多久干）
必须一起调。只把清水调成红色得到的是「轻飘飘的血」——血的辨识度里，
`dripSpeed` 只有清水三分之一这一条比颜色更关键。

**6. 命中屏幕是「配额」不是物理判定。** 没做真的飞行碰撞检测。VN 是演出驱动，
剧本需要「这一发一定要溅到镜头上」的确定性；物理判定可能整场都不溅到，演出性差。
现在每发按 `screen:` 概率掷出几个命中名额，各自延迟一段飞行时间后通知水渍层，
而且飞得越远的落点等得越久——一发喷溅的水渍会前后错开着「啪、啪、啪」。

**7. 参数先内置不做 ScriptableObject。** 天气那套资产化是因为要在 Preview 窗口反复
微调形态；液体参数量少一个数量级，`power`/`screen` 已覆盖日常调整。真要精调时把
`VNLiquidPreset` 的字段原样搬进 SO 即可，调用方全部走 `Get()`，不会有第二处要改。

**8. 点击喷水模式必须留键盘出路。** 左键被喷水接管后，`Enter`/`Space` **一定**保持
推进——否则玩家会被卡死在这一句台词里。同时喷水模式会顺手让 `VNClickRipple` 让位，
一发水花上再叠一圈柔光星环，两种点击反馈会互相打架。

**9. 只有开关进存档，水本身不进。** 空中飞的水珠和屏幕上已经溅好的水渍都是瞬态的，
读档不还原也不违和；存的只是「还在喷 / 镜头是湿的 / 点击模式开着 / 水渍盖不盖对话框」
这四个会一直持续下去的状态。`splash` 与 `dry` 是一次性演出，调试重建也跳过它们。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj` **0 错误**。
- 演示场景：Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene 重建后，
  `` ` `` 在鼠标处爆溅、F1 间歇喷射、F2 循环四种液体、F3 常驻湿镜头。
- 剧本：Scenario Editor 打开 `LiquidDemo.vn.txt` → 选中首行 → ▶ 从选中行播放。
- 存档：`liquid spray on` / `liquid wet on` 后存档 → 读档，应仍在喷 / 镜头仍是湿的；
  `liquid splash` 之后存读档**不应**重放那一发。
- 校验器：Ctrl+Shift+L，故意写 `liquid splash x:960`（像素而非比例）应报
  `liquid-coord-range`；写 `liquid spry` 应报 `bad-liquid-action`。

### 修复记录（2026-08-04，同分支）

第一版跑起来后用户反馈三点，都是真问题：

**① 方向错了：水应该朝镜头喷，不是喷向左右。**
关键约束是**演示场景的相机是正交的**（`cam.orthographic = true`），
所以给粒子一个 z 速度不会有任何近大远小——正交投影下远近一样大。
「扑面而来」只能做伪透视：从喷射点 **360° 放射** + 边飞边加速（
`velocityOverLifetime.speedModifier` 曲线 0.55→3.2）+ 边飞边放大（
`sizeOverLifetime` 0.42→2.1）。径向速度取**平方分布**是这里的关键——
多数粒子几乎不动只变大（正对着你飞来），少数快速向外掠过（擦着镜头过去）；
均匀分布会退化成"一个平面上的烟花"。
拉伸公告板沿速度方向拉伸在这里白送了正确结果：中心那些慢粒子接近圆点，
外围快的被拉成放射状短线，正是正对镜头的雨该有的样子。
`dir:` 改为**留空 = 朝镜头**（新默认），填了才回到侧喷，两条路径都保留。

**② 粒子太粗太长，要像现有的雨、只大一点点。**
根因是拉伸方式选错：第一版用 `velocityScale`（**随速度**拉伸），
喷射初速 7 配上 0.36 的系数能把粒子拉到 2.5 世界单位长。
而现有的雨用的是 `lengthScale`（**固定**倍率，雨是 5）+ 纯圆的 SoftCircle 贴图。
改成同一套：`lengthScale` 由预设给（1.9~3.6），`velocityScale` 只留 0.035。
尺寸对标雨滴的 `0.02~0.04`，取约 1.8 倍 → `0.038~0.11`（原来是 `0.10~0.36`）。
`LiquidBlob` 贴图也重画成**紧凑近圆**（半宽 0.30、头部半高 0.26 收到尾部 0.085）：
原来贴图本身就是横向长条，再乘 `lengthScale` 是两次拉伸叠加，必然成面条。
粒子数同步上调（水 34→58）补偿变小的体积。

**③ 屏幕水珠太大。**
`baseDropSize` 62 → **22px**（按真实雨天车窗的尺度），`maxDrops` 48→96、
`wetTargetDrops` 22→40：小而多才像被溅到，大而少看起来像贴纸。
配套 `SplatBurst` 的散布收窄到 0.035 并改用平方根分布（卫星滴挤在主滴附近），
成簇概率 0.45→0.62、每簇 2~6 颗；水痕最大长度倍率 9→14
（水痕长度不随水滴尺寸线性缩放，小滴一样能划出长道）。

**顺带修掉一个会炸存档的隐患**：内部用 `float.NaN` 表示"朝镜头"，而
`JsonUtility` 会把 NaN 写成非法 JSON、读回来是垃圾值。照 `weatherWindSet`
的先例拆成 `sprayDirSet`（bool）+ `sprayDir`（float）两个字段，
旧存档缺省 `false` 正好等于新的默认行为。
`VNScenarioSchema` 里 `dir` 的 `def` 也从 `"90"` 改成留空——
否则用户手填 90 会被当成"等于默认可省略"而消失，反而永远得不到真正的 `dir:90`。

### 修复记录 ②：屏幕水珠的尺度与形状（2026-08-04，同分支）

实机截图显示屏幕上的水渍像一串**肥皂泡**。两个原因叠在一起：

**尺度错了。** 先把换算做出来：相机 `orthographicSize = 5` → 可见高度 10 世界单位
= 1080px，即 **1 世界单位 = 108px**。空中喷射粒子直径 0.038~0.075 世界单位
→ 屏幕上 **4~8px 宽**。而屏幕水珠是 `baseDropSize 22px` × 0.42~1.35 = **9~30px 的圆**，
宽了 3~4 倍。镜头上的水点和空中飞的水珠本来就该是同一个量级。
改：`baseDropSize` 22 → **6px（宽度）**，`maxDrops` 96→240、`wetTargetDrops` 40→120。

**形状错了。** `WaterDrop` 那张假折射图有完整明暗剖面（中心压暗 + 内亮环 + 菲涅尔
暗边），那是给几十像素的大水滴设计的；缩到 5~8px 时整套剖面糊成**一圈灰环**——
肥皂泡感就是这么来的。这么小的东西只放得下两条信息：是一条细的、边比中间亮。
新增 `VNProceduralTextures.WaterSpeck`（32×96 竖向细长，底端圆钝是头、顶端收尖是尾）。

**尺寸改成分两档，不是一条连续曲线。** 真实的镜头水渍是"一大片细点 + 零星几颗大的"，
连续分布只会得到一堆不上不下的中等水珠，那恰恰是最假的尺寸。
默认 `bigDropRatio = 0.15`：85% 是 4~8px 的细长小点（拉长比 1.8~3.4），
15% 是 10~16px 的大滴（拉长比 1.25~1.9，表面张力把它收得更圆）。
**只有大滴走完整状态机**（挂住→下滑→拖水痕），小水点现实中根本不会流——
表面张力足够撑住它自己，所以把它的 `cling` 直接设到干涸时间之后，永不进入下滑。
高光层（`DropSpec`）也只给大滴：5px 的小水点上一个高光块会把它重新变回泡泡。

**朝向：溅射方向 → 下滑后转竖直。** 每颗水点带 `angle`，刚溅上时沿撞击方向拉长、
圆头朝外（和空中那层的放射感对上），一旦开始往下流就用 `LerpAngle` 慢慢转回 0——
重力接管之后，水的长轴只会是竖的。撞击方向由 `VNLiquidSplash.ScheduleScreenHits`
连同命中名额一起传过来（`PendingHit.angleDeg`）；`SplatBurst` 的卫星滴则各自
沿"从主滴甩出去"的方向拉长，一簇水点是炸开的而不是平行的。
常驻湿镜头（`liquid wet on`）例外：那不是"被溅到"而是水本来就在玻璃上，
铺底时直接给接近竖直的朝向。

### 修复记录 ③：手感参数被场景序列化固化（2026-08-04，同分支）

第三版实机截图比第二版更糟：屏幕上是几百像素的模糊白色涂抹，像烟不像水。

**根因不是参数没调好，是参数根本没生效。**
`VNWetScreen.baseDropSize` 等是 `public` 序列化字段，而组件是 `VNLiquidInstaller`
建进场景后保存的 → 值被固化在 `.unity` 里。grep 场景文件一看便知：

```
maxDrops: 48        ← 第一版的值
baseDropSize: 62    ← 代码里已经改成 22 → 6，对这个实例完全无效
wetTargetDrops: 22
```

于是出现最坏的组合：**序列化的那半（尺寸）没生效，运行时计算的那半（第三版新加的
"拉长比 1.8~3.4"）生效了**——62px 的水渍被拉成三百多像素，贴图放大 5 倍糊成烟雾。
两轮"缩小"改动不但没生效，还叠加出了比原来更假的结果。

**修法是消除第二个真相来源，不是再调一次数值。**
把 `VNWetScreen` 的 `MaxDrops` / `BaseDropSize` / `BigDropRatio` / `WetTargetDrops`
和 `VNLiquidSplash` 的 `GlowHdrCeiling` 全部改成 `const`。项目早就决定液体参数
跟着代码走（不做 ScriptableObject，见 `VNLiquidPreset` 注释），那就不该同时存在
Inspector 副本。场景里遗留的那三行 Unity 会直接忽略，用户什么都不用做。
接线类字段（`sortingOrder` / `coverDialogue` / 材质引用）保持序列化——那些本来就该
逐场景配置。

**顺带把形状再收一道。** 之前让大滴用带折射剖面的 `WaterDrop`，以为它接近圆撑得住
那套明暗；实测十几像素下它一样退化成白圈。现在**一律用 `WaterSpeck`**。
拉长比上限 3.4 → 2.8（再长就像划痕不像水点），撞击形变 1.6/0.5 → 1.35/0.65
（5px 的东西拍扁 1.6 倍看不出是形变，只会闪一下），高光透明度 0.9 → 0.5。
最终尺寸：小水点 3.5~7px 宽 × 5.6~19.6px 长，大滴 8.5~13.5px 宽 × 10~21.6px 长，
与空中喷射粒子的 4~8px × 9~18px 对齐。

**教训已写进** `ProjectCodeGuide` 十二节坑清单与 `vn-new-effect` 技能的硬约定：
手感参数写 `const`，要可视化调参就走 ScriptableObject，别用 Inspector 字段兼职。
排查手法：`grep <字段名> Assets/Project/Scenes/*.unity` 看场景里实际存的是什么。

## 九十五、中文字体换成毛笔糖圆体 MaoKenTangYuan（2026-08-05，分支 `agent/maokentangyuan-font`）

### 需求/背景

用户把 `MaoKenTangYuan-beta0.12-20210702.ttf`（毛笔手写体）拖进了 `Assets/Font/`，
要求全项目中文文字改用这个字体。此前 `VNFont.cs` 里中文和英文共用同一套
Noto Sans SC 字体档案（`ScProfile`），日文缺字时也是兜底到这同一套档案。

### 文件改动

- `Assets/Resources/VNFonts/MaoKenTangYuan-Regular.ttf`（+`.meta`）：
  从 `Assets/Font/` 移动改名到此处——`Resources.Load` 要求字体必须在
  Resources 目录下，运行时 tier-2/tier-3 兜底逻辑才能找到它。
- `VNFont.cs`：把原来 zh/en 共用一套的 `ScProfile` 拆成三套独立档案——
  `ZhProfile`（主字体 MaoKenTangYuan，`fallback` 指向下面的通用档案）、
  `GeneralCjkProfile`（Noto Sans SC，英文主用 + 中/日的缺字兜底）、
  `JaProfile`（不变，`fallback` 也改指向通用档案而不是"中文档案"）。
  原来写死在 `AssetFor()` 里的"日文专属 fallback 挂载"逻辑泛化成
  `Profile.fallback` 通用字段，解析失败时退到 fallback 档案、解析成功时
  把 fallback 字体挂进 TMP 的 `fallbackFontAssetTable` 补缺字，两种语言共用一套代码。
- `VNFontAssetBuilder.cs`：中文烘焙源指向新 ttf；新增
  `EnsureGeneralFontAsset()` 烘焙英文/兜底用的 `NotoSansSC-General-Dynamic.asset`；
  `CreateMenu()` 里一并调用。
- `Assets/Resources/VNFonts/NotoSansSC-Dynamic.asset`（+`.meta`）：删掉旧的
  （内容是 Noto Sans SC）后用 `Ensure()` 从新字体源重新生成。
- `Assets/Resources/VNFonts/NotoSansJP-Dynamic.asset`：内嵌的
  `m_FallbackFontAssetTable` 手动改指向新生成的通用资产 GUID
  （原来指向的旧中文资产 GUID 现在语义变成"毛笔体"了，日文缺字不该兜底到它）。

### 技术决策与取舍

- **应用范围只限中文**，英文继续用 Noto Sans SC——手写体拉丁字形风格突兀、
  覆盖也未必齐全，用户确认后拍板。
- **中文缺字兜底 Noto Sans SC**，做法照抄原来日文兜底中文的模式（现在反过来）。
- **原地重烘焙保留 GUID**：中文烘焙资产被 26 个文件（1 场景 + 25 个 UI 皮肤
  prefab）硬引用。删除旧 `.asset`+`.meta` 后在 Unity 编辑器里点
  `Tools → VN Effects → 字体 Fonts → 生成 TMP 字体资产 Create TMP Font Asset` 重新生成，Unity 原地复用了
  同一路径的旧 GUID（`fdf08363d8a023d4d929f785c67e4c59`），26 处硬引用全部
  不用动——只有 `NotoSansJP-Dynamic.asset` 内部的 fallback 引用需要手动改。
- **烘焙这一步必须由用户在已打开的 Unity 编辑器里点菜单完成**：AI 只能改文件，
  没有 Unity 编辑器控制通道，`TMP_FontAsset.CreateFontAsset` 属于编辑器 API。

### 验证方法

- 烘焙完成后确认 `Assets/Resources/VNFonts/` 下多出
  `NotoSansSC-General-Dynamic.asset`，`NotoSansSC-Dynamic.asset` 时间戳更新。
- 进剧本场景走一段中文台词，字形应变成毛笔体；切到英文/日文语言应保持
  Noto Sans SC / Noto Sans JP 不受影响。
- 用一个生僻字（比如剧本里没预热过的字）验证中文缺字时能兜底显示而不是方框。

## 九十六、剧本编辑器热重载调试（2026-08-10，分支 `agent/scenario-hot-reload`）

### 起因

用户的迭代循环是「改一行 → 看效果」，但当时的编辑器把这个循环拖成了几十秒：

1. **进 Play Mode 后编辑内容全没了。** 这是本章要修的头号 bug，
   而且它跟"忘了保存"无关——`VNScenarioEditorWindow` 的 `_doc` / `_path` /
   撤销栈全是普通字段，没有 `[SerializeField]`。进 Play Mode 触发域重载
   （domain reload），Unity 只保留标了 `[SerializeField]` 的窗口字段，
   整个文档被重置成空白 untitled。**就算存了盘，退出 Play Mode 后窗口也是空的。**
2. **每次调试都得完整进出一趟 Play Mode。** 播放按钮在 Play Mode 中是禁用的
   （`EditorApplication.isPlayingOrWillChangePlaymode` 的 DisabledScope），
   想再看一次就得退出、改、再进——每次一次域重载 + 场景初始化。
3. 忘了保存就点播放，播放本身没问题（`PlayFromSelectedRow` 用的是
   `_doc.GenerateText()` 内存文本），但域重载一来改动就随窗口一起蒸发。

### 做了什么

**一、窗口跨域重载存活（必须做，不是可选项）**

`VNScenarioEditorWindow` 实现 `ISerializationCallbackReceiver`：

- 新增存活组字段：`_docText`（文档正文的序列化中转）、`_path`、`_fileTimeTicks`
  （`DateTime` 不可序列化，存 ticks）、`_dirty`、`_externalChanged`、`_tab`、
  `_showCategoryColors`、`_rebuildStateBeforePlay`、`_restoredListIndex`、
  `_undoStackSerialized` / `_redoStackSerialized`、`_scroll`、`_lastPlayedLine`。
- `OnBeforeSerialize()` 把 `_doc` 拍成文本、撤销栈拷进可序列化 List、
  记下 ReorderableList 的选中行。**这里只能做纯 C# 运算，不能碰 Unity API。**
- `OnAfterDeserialize()` 留空，实际还原挪到 `OnEnable` 的
  `RestoreAfterDomainReload()`（Unity API 在反序列化时机不可用）。
- 兜底：OnGUI 帧首快照重算时顺手同步 `_docText = _frameSnapshot`。

> **加新状态时的规矩**：以后往这个窗口加任何"关掉/重编译也不该丢"的状态，
> 必须同时改 `OnBeforeSerialize` 和 `OnEnable` 两处，只加字段没用。

**二、Play Mode 中原地热重播（核心）**

播放路径统一收敛到 `PlayFromSourceLine(int sourceLine)`：校验 → 自动保存 →
分流。Play Mode 中走 `HotReplay()`，直接
`runner.PlayFromSourceLine(内存文本, 行, rebuildState)`——
**不退出 Play Mode、不触发域重载、不重新初始化场景**，改一行到看到效果
约等于一次 Repaint。编辑期仍走原来的 `VNPlayFromLineBridge` 冷启动。

播放按钮在 Play Mode 中不再禁用（只有正在切换 Play Mode 的那一瞬灰掉），
文案切换成「▶ 播放选中行（热）」。

**三、播放前静默自动保存**

`AutoSaveBeforePlay()`：脏了就直接写盘。两个例外——未命名文档（没有路径）
不弹保存框，直接拿内存文本播；`_externalChanged`（磁盘被别处改过）时不写，
避免静默覆盖掉别人的改动。

**四、当前行高亮跟随**

`OnInspectorUpdate()` 以 10Hz 轮询 `runner.CurrentLine`（不给运行时加事件，
避免域重载时的订阅生命周期问题），换算成 UI 行号后画淡蓝底 + 左侧竖条，
滚出可视区时自动滚过去。工具栏「跟随播放」开关控制（EditorPrefs）。

- 新增 `RowForSourceLine()`，是 `SourceLineForRow()` 的逆运算；
  choice 选项行 / camseq 路径点行都算进它们所属的那一行。
- 文档行数变了要重算 `_playingRow`，挂在 OnGUI 帧首快照那里。
- **只在 Runner 正播本窗口打开的文件时才高亮**（比对
  `runner.CurrentScriptName`），跨文件 jump 之后行号不通用，宁可不亮。

**五、播放控制条**（工具栏，只在 Play Mode 有效）

`❚❚/▶` 暂停继续、`⏭` 单步、`⟳` 重播当前行、`⏮` 退回上一条命令，
右侧显示 `L<行号>` 与暂停状态。

**六、快捷键**

走 `ShortcutManager`（窗口作用域，用户可在 Edit → Shortcuts →
VN/Scenario Editor 里自己改键位）：`F5` 从选中行播放、`F6` 重播上次那一行、
`F8` 暂停/继续、`F10` 单步、`Ctrl+S` 保存。
另外 `Ctrl+Enter` / `Ctrl+Shift+Enter` 走 IMGUI 自己收
（`HandleShortcutKeys`，必须排在 `HandleInsertKeys` 之前，两者都盯着 Enter）。

### 运行时改动（VNScriptRunner）

- `SetDebugScript(TextAsset)`：告诉 Runner 现在调试的是哪个剧本。
  只换 `script` 引用不重载命令——翻译查表、`chapter` / 跨文件 `jump`
  的"当前文件"都按它算。Bridge 冷启动路径也补了这一步（新增 `AssetKey`
  经 SessionState 传资产路径）。
- `CurrentScriptName` 属性。
- 命令级暂停/单步：`IsDebugPaused` / `SetDebugPaused()` / `RequestDebugStep()`，
  闸门在 `Run()` 主循环顶部。`Play(string)` 开局时清暂停标记，
  免得编辑器留下的暂停态把正常开局卡死。

### 技术决策与取舍

- **暂停是「命令级」的，不是画面定格**：卡在两条命令之间，已经跑起来的
  DOTween 补间和打字机不受影响。真要定格画面请用 Unity 自带的暂停按钮。
  没做 `Time.timeScale = 0` ——会连打字机和 DOTween 一起搞坏，代价太大。
- **跟随高亮用轮询而不是运行时事件**：10Hz 的 `OnInspectorUpdate` 足够跟手，
  而且省掉了域重载前后订阅/退订的生命周期坑，运行时零改动。
- **`ShortcutManager` 不接受 `Return`/`Enter` 当绑定键**——会被忽略并刷
  `Ignoring shortcut attribute with invalid binding` 警告。所以主键位用 F 系，
  Ctrl+Enter 那套只能在 IMGUI 里自己收。
- **用户选择了静默自动保存**（而不是弹窗询问 / 完全不存）：
  文件永远和看到的一致，Git diff 也干净；试验性的乱改有撤销栈兜底。
- **没做**：外部文件改动自动重播、定时备份+崩溃恢复、`hasUnsavedChanges`
  关窗提示、行右键菜单、播放时自动切换到剧本场景——用户逐项确认不要，
  场景处理维持现状（找不到 Runner 就在 Console 报错）。

### 验证方法

- Unity 编译零错误；ShortcutManager 的两条 invalid binding 警告已消除。
- Play Mode 实测（`script-execute` 探针）：对同一个 Runner 连续调用两次
  `PlayFromSourceLine`，都正确从第 16 行起播且 `CurrentLine=16`，无异常——
  热重播可反复执行。
- 暂停实测：`SetDebugPaused(true)` 后隔 20 秒复查，`CurrentLine` 停在 16
  纹丝不动而 `IsRunning=True`，确认闸门卡在命令之间而非整体停摆。
- 窗口存活与 UI 交互（高亮、控制条、快捷键手感）需要在编辑器里手动点验。

## 九十七、编辑器隐藏注释/空行 + 滚轮开回想可关（2026-08-10，分支 `agent/scenario-hot-reload`）

### 起因

- 第1章.vn.txt 有 206 行，开头一大段是教学注释，编辑器里每条注释都占满
  一行 21px，想看剧情得先滚过去一屏。
- 滚轮上滑打开回想是 `VNScriptRunner.cs` 里硬编码的，有人嫌误触但关不掉。

### 一、编辑器：隐注释/空行

工具栏加一个「隐注释/空行」开关（EditorPrefs 记忆，默认关）。

**实现走"高度归零"而不是"过滤列表"**：`_list` 直接绑定 `_doc.rows`，
只让 `RowHeight()` 对隐藏行返回 0、`DrawRow()` 直接 return。
这样 `_doc.rows` 的索引完全不变，`SourceLineForRow` / `RowForSourceLine` /
多选 / 拖动排序 / 删除 / 舞台一览 / 校验圆点全部零改动，
也不存在过滤视图那种"拖到隐藏行之间算什么"的语义黑洞。

**只隐空行与 `#` 注释，不隐全部 Raw 行。** `VNRowKind.Raw` 还兜着两种
语法残留：前面没有 choice 的孤儿 `*` 选项行、前面没有 camseq 的孤儿 `>`
路径点行（`VNScenarioDoc.cs:113/119/125` 三个入口）。那两种一旦藏起来就
再也找不回来——Issues 面板定位过去也是一片空白——所以必须留着显形。
判定在 `IsHiddenRow()`。

开关打开的瞬间若选中行正好被藏了，`MoveSelectionOffHiddenRow()` 会把选中
挪到最近的可见行，免得 Duplicate / [-] / 播放作用在看不见的行上。

### 二、运行时：滚轮打开回想可关

- `VNConfigPanel.WheelOpensBacklog` 静态属性（PlayerPrefs `VN.Config.WheelBacklog`，
  默认开）。**静态**是因为 `VNScriptRunner` 每帧要读它，而设置面板未必存在于所有场景。
- `VNScriptRunner` 的滚轮分支加判断；关掉后 H 键照常。
- 设置面板加一项，照抄全屏那一项的「按钮 + 文案翻面」模式。
- 三语词条 `config.wheelBacklogOn` / `config.wheelBacklogOff`。

**皮肤槽位是可留空的**：`VNConfigPanelSkin.wheelBacklogButton / wheelBacklogLabel`
没有进 `CollectValidationErrors` 的 `Require`，`BindCustomSkin` 里也做了 null 检查。
老皮肤 prefab 缺这两个槽位时，只是设置面板里没这一项，开关本身照常默认生效。

### 改到了实际在用的那个 prefab

**注意：`VNSystemUiSkinSet_Default.asset` 的 `configPanelPrefab` 指向的是
`ConfigPanel_New.prefab`，不是 `ConfigPanel_Default.prefab`。**
所以只更新导出器（`VNSystemUiSkinExporter.BuildConfig`）没用——它只重建 `_Default`。
本章两个 prefab 都处理了：

- 导出器：窗口 740 → 810，`WheelBacklog` 按钮插在 -654，Hint 从 -668 挪到 -738。
  以后重跑 Export Default Prefabs 会带上这一行。
- `ConfigPanel_New.prefab`：**直接增量改**（复制 Fullscreen 按钮 → 改名
  WheelBacklog → 挪到 -654 → 窗口加高 → Hint 下移 → 回填两个槽位），
  没有重导，用户在这个 prefab 上的其它自定义原样保留。

### 验证方法

- 编译零错误。
- Play Mode 实测：`WheelOpensBacklog` 默认 True、可读写翻转；
  三语 `config.wheelBacklogOn/Off` 都解析出译文而不是回落成 key。
- 皮肤实测：`panel.Open()` 真正走一遍 Build + BindCustomSkin 后，
  两个槽位都非空，label = 「滚轮打开回想　开」；
  `onClick.Invoke()` 两次，PlayerPrefs 与文案同步翻面（关 → 开）。
- 编辑器的隐藏开关是 IMGUI 交互，需要在编辑器里手动点验。

---

## 九十八、camseq 结构化路径点 + 镜头编排窗口双向绑定（2026-08-11，分支 `agent/camseq-inline-editor`）

### 需求 / 背景

剧本编辑器里加一条 `camseq` 之后，编辑体验是断的：

- 路径点行 `> middle 1 1` 是**纯文本 TextField**，没下拉、没校验、没提示，全靠手打语法；
- 真正好用的可视化工具（Tools → VN Effects → 镜头编排 Camera Sequence Editor，
  有迷你画布拖点、拖角改 zoom、时间轴预览、场景实时预览、预设库）
  跟剧本**完全没有连线**，中间隔着一个剪贴板：
  复制整段 → 切窗口 → 粘贴 → 解析载入 → 调 → 生成文本 → 回来删旧的粘新的。

本章把这两头接上，并给路径点行长出正经控件。

### 文件改动清单

**新增**

- `Editor/VNCamWaypoint.cs`
  - `VNCamWaypoint`：一行 `> ...` 的结构化视图 + 严格 `TryParse` / `Format`。
    语法与运行时 `VNScriptParser.ParseCamWaypoint` 同构。
  - `VNCamseqText`：一整段 camseq 文本（header + 路径点行）的 `TrySplit` / `Join`，
    不经 `VNScriptParser`（那边解析失败会往 Console 打 warning，编辑期不该有噪音）。
  - `VNCamseqTemplates`：11 条内置运镜模板（推近特写 / 缓慢拉远 / 推近后回位 /
    左右横摇 / 环视全景 / 三连甩镜 / 告白推镜 / 惊讶弹镜 / 叠化转特写 /
    叠化开场特写 / 转场瞬切起手）。`{char}` 占位在套用时替换成当前第一个角色 id。
- `Editor/VNTextPromptWindow.cs`：极简单行输入弹窗（Unity 没有内置的
  「带输入框的 DisplayDialog」），「把本行存为预设」用它要名字。

**修改**

- `Editor/VNScenarioEditorWindow.cs`
  - 路径点行改成字段化控件：类型（锚点/角色/坐标）+ 目标 + zoom + 秒 + ease + xfade + 删除；
    解析不了的行**退回纯文本框并标黄**，鼠标悬停说明语法。
  - camseq header 行右侧新增三个按钮：`编排`（打开并绑定镜头编排窗口）、
    `预设▾`（内置模板 / 我的预设 / 把本行存为预设）、原有的 `+ wp`。
  - 新增公开 API 供镜头窗口调用：`IsCamseqRow` / `SelectedCamseqRow` /
    `TryGetCamseqText` / `ApplyCamseqText` / `ScenarioDisplayName` / `DocVersion`，
    `FocusRow` 从 private 提为 public（原来 Issues 面板在用，逻辑一模一样，不另起炉灶）。
- `Editor/VNCamseqEditorWindow.cs`
  - 新增绑定条：`◆ 文件名 第 N 行`（点它把剧本滚到那一行）、
    `跟随选中 / 已锁定`、`实时回写`、`应用回剧本`、`从剧本重载`。
  - `_points` / `_startMode` / `_startFade` / `_endFade` / `_endFadeDur` / `_scrub`
    与全部绑定状态改成 `[SerializeField]`，跨域重载存活。
  - 路径点列表下方加「内置模板 ▾」。预设保存统一走新的静态 `SavePreset`。
- `Editor/VNScenarioDoc.cs`：Lint 增加一条 Warning——路径点行结构化失败时点名，
  免得写错了还以为编辑器坏了。

### 技术决策与取舍

**1. 存储不变：`VNRow.camLines` 仍是唯一真相（字符串列表）**

一开始考虑把路径点升级成结构化的 `List<VNCamWaypointRow>`，但那样
`VNScenarioDoc` 的解析/生成、`SourceLineForRow` / `RowForSourceLine` 行号换算、
校验、`Clone` 全都要跟着动，回归面太大。

最后选了「每帧现解析、改完立刻格式化写回」：`camLines` 一个字都不用改，
上述所有逻辑零改动，而且**任何解析不了的写法都原样留在文件里**。
成本是每帧解析几个 token——IMGUI 本来就每帧重画，可以忽略。

FloatField 的中间态（用户打 `1.`）不会被 round-trip 打断：
Unity 在控件有键盘焦点时不会用传入值刷新编辑中的文本，只要解析回来的值一致就没事。

**2. 严格解析 + 退回纯文本，而不是强制结构化**

`TryParse` 认不出任何一个 token 就整行失败（多余的数字、非法 ease 名、
`[middle]`、`point:` 这种带冒号的残缺写法）。宁可少一个结构化控件，
也不能吞掉用户写的内容。`ease:outsine` 这种大小写不规范的会被规范化成 `OutSine`。

配套：`omitZoom` / `omitDuration` 标记——原文没写 zoom/时长的（`> middle`），
只要值还是默认就继续省略，避免打开编辑器就把手写行撑成 `> middle 1 0.8` 的无意义 diff。
时长是「第二个数字」，要写它就必须先把 zoom 写出来占位。

**3. 路径点行禁用 `CharacterPopup` / `SpritePopup`**

这是个真陷阱：`SpritePopup` 的选中是**异步回调**（`PopupWindow` + `SetPopupValue`），
会把值写进 `VNRow.values[key]`——而路径点存在 `camLines` 文本里，
两条路径一混，选了角色不生效还顺手往文档里塞了个野参数。
所以角色 id 改用同步的 `PopupString`（`custom…` 还能手打场景里没有的 id），
锚点 / 部位 / ease 用纯 `EditorGUI.Popup`。代价是没有角色缩略图，路径点行本来也窄。

**4. 绑定关系存在镜头窗口的 `[SerializeField]` 里**

`VNRow` 是纯 C# 对象，域重载后 `_doc` 重新 Parse，引用就悬空了。
所以绑定存的是「`VNScenarioEditorWindow` 引用（EditorWindow 是 ScriptableObject，
引用能跨域重载存活）+ 行索引 int」，读写全走上面那几个公开 API。
剧本编辑器画「编排」按钮要每帧问「这行在编排吗」，逐行 `FindObjectsOfTypeAll` 太费，
另用一对静态字段缓存（域重载后清空，下一次 `SyncLink` 立刻补回来）。

**5. 三道防丢稿的闸**

- 从菜单打开镜头窗口、手上已经摆了点的：自动跟随会**自动上锁**，
  绝不让它覆盖现成的稿；点剧本行的「编排」或「从剧本重载」才真正接管。
- 手动回写模式下一旦有未应用的改动就自动上锁 + 绑定条标「（未应用）」，
  不弹模态框（IMGUI 里弹框重入很脏），应用或重载后自动解锁恢复跟随。
- 点别的行的「编排」时，若当前行手动模式下还脏，弹一次「应用后切换 / 丢弃改动」。

**6. 实时回写的撤销节流**

实时模式每帧都可能回写，直接 `MarkStructural()` 会把撤销栈刷爆。
`ApplyCamseqText` 走新的 `PushUndoThrottled()`——和 `OnGUI` 末尾那套一样的
1 秒粒度合并，行为一致。另外「内容没变就早退」，稳态下不会反复触发。

### 验证方法

- 编译零错误（`assets-refresh` 强制重编，只剩既有的 `FindFirstObjectByType` 弃用警告）。
- **现有剧本全量往返**：扫 `Assets/Scenarios/*.vn.txt` 的全部 13 行路径点，
  13 行全部可结构化、0 行退回纯文本；3 行有格式差异且都是
  `> middle 1.0 1.0` → `> middle 1 1` 的数字规范化（语义相同，且只在实际编辑该行时才写回）。
- **边界写法**：`> middle` / `> middle 1` / `> 小雪:head 1.8 0.8 ease:OutSine xfade:0.5` /
  `> -300,120 2 0` 往返无损；`> ` / `>` / `> middle 1 2 3` / `> middle ease:Bogus` /
  `> [middle] 1 1` 正确退回纯文本；`ease:outsine` 规范化成 `OutSine`。
- **内置模板**：11 条全部能 `TrySplit` + 每个路径点都能 `TryParse` 且往返无损；
  无角色可用时 `{char}` 与部位后缀一起退化成 `middle`，不留 `middle:head` 废点位。
- **写回不破坏文件**：拿 `第1章.vn.txt` 做「Parse → 套用模板 → GenerateText」，
  解析-生成行数 203/203 一致；真实内容改动只在 camseq 块内（后面的行只是整体位移）；
  行尾的 `@` 异步标记保留；生成文本经 `VNScriptParser` 解析出
  `start=fade startfade=0.7` + 2 个路径点，首点 `小雪:head` zoom=1.8 dur=0 全对；
  header 从有到无（清空 start/end）也能正确写回。
- **Lint 新警告无误报**：现有全部剧本跑 `Validate`，零 waypoint 相关告警。
- 绑定条的跟随/锁定/实时回写是 IMGUI 交互，需要在编辑器里手动点验。

---

## 九十九、镜头编排画布：底图跟随剧本行 + 真实立绘（2026-08-11，分支 `agent/camseq-inline-editor`）

### 需求 / 背景

九十八章把镜头编排窗口和剧本行绑上之后，暴露了画布本身的问题：
**底图画的是场景里 `VNStage.backgroundImage` 当前挂着的那张**（编辑期就是个占位图），
跟剧本里那一行该显示的背景毫无关系——想给「教室_黄昏」编排运镜，
画布上却是一张紫色波浪占位图，等于闭着眼睛拖取景框。

顺带一个更大的问题：画布上那三个半透明灰矩形只是**假的站位参考**。
可镜头编排的高频需求恰恰是「对准角色的头/胸」，看不见人在哪、多高，全靠猜。

### 关键发现：推算逻辑早就有了

剧本编辑器的「舞台一览」（每行左侧那个缩略图格子）本来就在
**逐行推算「执行到这一行时背景是什么、台上有谁站哪」**（`RowStageState`）。
既然窗口现在知道自己绑在第几行，直接问它要就行——零新逻辑，
而且两边看到的东西永远一致。

### 文件改动清单

- `Editor/VNCamWaypoint.cs`：新增 `VNRowStageInfo` / `VNRowStageChar` 传输类
  （背景/CG sprite + 在场角色的 id·站位·立绘）。
- `Editor/VNScenarioEditorWindow.cs`：新增公开 `TryGetRowStage(index, out info)`，
  内部复用 `RebuildStageStatesIfNeeded` + `_backgroundPreviews` / `_cgPreviews` /
  `_characterPreviews`。CG 盖着且没 `keepChars` 时不吐立绘（本来就看不见）。
- `Editor/VNCamseqEditorWindow.cs`：
  - `SceneBackgroundSprite` → `CanvasBackdrop`，三级回退：
    **手动指定的背景 → 绑定行推算出的背景/CG → 场景当前那张**（老行为兜底）。
  - 绑定条右侧新增 `底图: 跟随剧本（教室_黄昏）▾` 下拉与 `立绘` 开关，
    没绑定剧本时也画得出来（放在 `HasLink` 早退之外的共用段）。
  - `DrawStageCharacters`：按推算站位 + `VNStage.characterHeight × def.sizeScale`
    + `def.positionOffset` 画真实立绘，宽度按 sprite 宽高比算。
    关掉开关 / 没绑定 / 没立绘可用时退回原来的灰矩形。
  - 舞台快照按「行号 + `DocVersion`」缓存，不每帧重算。

### 技术决策与取舍

**1. 底图为什么要留手动覆盖**

逐行推算是**按文件顺序的近似**，`jump` / `choice` 分支不展开
（和「重建前置状态」调试是同一套近似）。camseq 落在分支里时可能推不准，
所以工具栏留了个背景下拉当兜底；空值 = 跟随，不是「无背景」。

**2. 立绘必须裁进画布**

立绘按真实尺寸画会超出画布（脚在画面外），`GUI.DrawTextureWithTexCoords`
不会自动裁。包一层 `GUI.BeginGroup(rect)` 把坐标系挪到画布左上角再画；
只裁立绘那一段，取景框/路径线还是 1px 线，超出一点无所谓。
交互（`HandleCanvasInput`）在 `EndGroup` 之后，不受影响。

**3. 图集 sprite 走 textureRect**

背景和立绘统一走新的 `DrawSpriteRaw`，按 `sprite.textureRect` 换算 UV——
图集里的 sprite 不能整张 texture 当图用（这条是 vn-editor-extend 的铁律，
原来的背景绘制 `GUI.DrawTexture(rect, sprite.texture, ...)` 其实就踩着，顺手修了）。
立绘走 alphaBlend，背景不走。

### 验证方法

- 编译零错误。
- 拿 `第1章.vn.txt` 逐个 camseq 行跑 `TryGetRowStage`（另建隐藏窗口实例，
  不碰用户已打开的那个）：
  - rows[26]（源码第 27 行，正是出问题那行）→ `bg=教室_黄昏`，
    底图 sprite 正确解析成「学校の廊下（夕方）」，不再是占位图；
  - rows[63]（第 66 行）→ `bg=教室_黄昏` + 在场 `星野结衣2@center`，且立绘非空。
- 底图下拉、立绘开关、超出画布的裁切是 IMGUI 交互，需要在编辑器里手动点验。

---

## 一〇〇、镜头视角画布 + 场景舞台预览（2026-08-11，分支 `agent/camseq-inline-editor`）

### 需求 / 背景

九十九章让编排画布画对了背景与立绘，但 **Game 视图还是那张占位图**——
「场景预览」只接管了 ZoomRoot 的位移缩放，**没把场景里的舞台摆成那一行该有的样子**，
所以想在真实窗口里看演出效果依然做不到。

两个方向一起补：

- **A（场景舞台预览）**：开「场景预览」时连背景/立绘一起摆进场景 → Game 视图带 URP 后处理的真实画面；
- **B（镜头视角画布）**：画布直接显示「镜头里看到的画面」，拖进度条 / ▶ 就是运镜动画，
  完全在窗口内、零场景副作用。

### 文件改动清单

`Editor/VNCamseqEditorWindow.cs`（本章只动这一个文件）

- **A**：`ApplyStageToScene` / `RestoreStage` / `ClearPreviewCharacters`，
  挂在「场景预览」开关上（开 = 摆舞台 + 接管 ZoomRoot，关 = 全部还原）。
  跟随绑定行切换时 `Update` 里比对 `_stagedRow` 自动重摆。
- **B**：`_cameraView` 开关 + `ViewPoint(view, p)` 变换，
  背景与立绘统一按「当前时刻的运镜状态」变换后再画；
  镜头视角下不画取景框/路径线、并禁用画布拖拽（只看不改）。
- 工具栏第二行新增 `整图 / 镜头视角` 与升级后的 `场景预览` 两个按钮。

### 技术决策与取舍

**1. 镜头视角是一个坐标变换，不是另一套绘制**

运行时 ZoomRoot 的 `localScale = zoom`、`anchoredPosition = base + offset`，
所以画布上一点 `p` 显示在 `p × zoom + offset`。
把这个变换抽成 `ViewPoint`，背景（中心 0,0、半尺寸 960×540）与立绘走同一条路径——
整图模式就是 `zoom=1, offset=0` 的恒等变换。**两种模式共用一套绘制代码**，
不会出现「整图对了镜头视角不对」的分裂。

**2. 临时立绘只挂最小组件 + `HideFlags.DontSave`**

运行时 `CreateCharacter` 会挂 `VNImageEffectController` / blink / mouth / marks 一大堆，
它们的 `Awake` 在编辑期会乱来。预览版只要 `RectTransform + CanvasRenderer + Image`，
位置尺寸按 `SlotPosition + positionOffset` / `characterHeight × sizeScale` 算，视觉一致就够。

`HideFlags.DontSave` 是防泄漏的关键保险：**绝不会写进场景文件**，
而且域重载时会被 Unity 自动销毁——所以 `OnEnable` 里发现 `_scenePreviewing` 还开着，
就按记录的状态重新摆一遍。

**3. 顺手修了一个既有 bug：场景预览的还原信息活不过域重载**

`_zoomRoot` / `_origPos` / `_origScale` 原本都是普通字段，
脚本一重编译就全清空——`StopScenePreview` 时 `_zoomRoot` 已经是 null，
**ZoomRoot 会永久停在预览位置还原不回去**（进出 Play 有 `playModeStateChanged` 兜着，
纯重编译没有）。本章把它们连同新增的背景还原信息一起加了 `[SerializeField]`。

**4. 镜头视角下禁用拖拽**

画布坐标被运镜变换过了，拖点/拖角的命中判定要跟着换一套。
与其维护两套交互，不如镜头视角只读——要改点位就切回整图，或直接用下面的路径点列表。

### 验证方法

- 编译零错误。
- **摆舞台 + 还原**（对 `第1章.vn.txt` rows[63]，那行有立绘在场）：
  背景 `background`（占位图）→ `学校の廊下（夕方）`；
  `characterLayer` 子物体 0 → 1，造出的 `[camseq预览] 星野结衣2`
  `pos=(0,-60)`、`size=(1286.15, 880)`、`hideFlags=DontSave`；
  还原后背景与子物体数**完全回到原值，零残留**。
- 镜头视角的变换、超出画布的裁切、拖拽禁用是 IMGUI 交互，需要在编辑器里手动点验。

## 一〇一、camseq 停留参数 hold + 构图辅助线 + 编排窗口撤销（2026-08-11，分支 `agent/camseq-hold-guides-undo`）

### 需求 / 背景

镜头系统头脑风暴后先落地三件「小而痛」的：

1. **`hold:` 停留** —— 路径点原本只有 `duration`（到达本点的时长），`duration=0` 是**瞬切**
   不是**停留**。想做「推到脸上停一秒再拉回」只能在外面加 `wait`（但那会打断异步 `@`
   的连贯性）或者补一个同点位、时长 0 的废点位。缺的是最基础的「到点停一会儿」语义。
2. **构图辅助线** —— 编排画布只有取景框，判断不了构图。最要命的是**对话框遮挡区**：
   对话框是 Canvas 下 ZoomRoot 的**兄弟**、不随镜头缩放，特写时角色的脸经常正好落在
   对话框后面，只能进 Play Mode 才发现，返工成本高。
3. **撤销** —— 编排窗口一直没有撤销。误删一个路径点、拖拽排序拖错、手滑套了个预设
   覆盖掉半天的调参，全都只能「从剧本重载」整段丢掉重来。

### 文件改动清单

**运行时（hold 全链路）**

- `Script/VNScriptParser.cs`：`VNCamWaypointDef` 加 `hold` 字段；`ParseCamWaypoint`
  认 `hold:秒` token（非正数告警并忽略）。
- `VNCamera.cs`：`Waypoint` 加 `hold`；`BuildSegment` 在补间段/瞬切段之后
  `AppendInterval(hold)`；新增 `HoldCo(seconds)` 供 `PlayPathCo` 的叠化段用；
  `PlayPathCo` 在 `startFade` 与每个 `xfade` 点的 `CrossfadeTo` 之后接 hold。
- `Script/VNScriptRunner.cs`：`CamseqCo` 把 `def.hold` 带进 `VNCamera.Waypoint`；
  call 参数替换的 `camPoints` 深拷贝也补上 `hold`（漏了它，带参子程序里的 hold 会丢）。

**编辑器（hold 的两套 UI）**

- `Editor/VNCamWaypoint.cs`：`hold` 字段 + `TryParse` 认 `hold:`（非正数/重复一律返回
  false 退回纯文本）+ `Format` 在 `xfade` 之后输出。
- `Editor/VNScenarioEditorWindow.cs`：路径点行加 `hold` 数字框（tailW 加宽 76px）；
  黄色纯文本行的悬停语法提示补上 `[hold:秒]`。
- `Editor/VNCamseqEditorWindow.cs`：`Waypoint.hold` + 列表第二行加 hold 框
  （顺手把 zoom 滑条改成弹性宽度：窗口拉窄时先压滑条，右边几个数字框宽度不变）；
  `GenerateText` / `ParseTextFrom` 双向；时间轴 `Segment` 加 `isHold`。

**编辑器（构图辅助线）**

- `Editor/VNCamseqEditorWindow.cs`：`Guides` 位标志枚举（三分线/中心十字/安全区/
  对话框遮挡区）+ 工具栏 `辅助线 ▾` 下拉逐项勾选（开了任意一项按钮高亮）+
  `DrawCompositionGuides(frame, clip)` + `DialogueBandFractions()` 实测对话框 +
  一组带裁切的绘制辅助 `DrawRectClipped / DrawRectOutlineClipped / VLine / HLine`；
  `DrawCanvasFrame` 抽出 `FrameGuiRect`（辅助线与取景框共用同一套坐标换算）。

**编辑器（撤销 / 重做）**

- `Editor/VNCamseqEditorWindow.cs`：`_undoStack / _redoStack / _undoBaseline`
  + `TrackUndo / CommitUndo / ResetUndo / PerformUndo / PerformRedo / RestoreSnapshot`
  + 三个 `[Shortcut]`（Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z，窗口作用域）+ 工具栏 `↶ ↷`。
  `ParseText` 拆出 `ParseTextFrom(text, silent)` 重载给撤销恢复用。

**文档**：HowToUse.md camseq 章（hold 语法与规则、编排窗口的辅助线/撤销说明、
Lint 表、命令速查卡）、CLAUDE.md 编辑器节。

### 技术决策与取舍

**hold 语义定为「到达本点之后停留」**（不是到达之前等待）。理由：写剧本时想的永远是
「推到脸上，停一下，再拉回」，停顿属于**刚到的那个点**。放在「之前」的话最后一个点
没法停，语义还不对称。

**hold 走 DOTween 的 `AppendInterval`，不用 `WaitForSeconds`。** Skip 快进是靠
`DOTween.timeScale = skipTimeScale` 实现的（`VNScriptRunner.SetSkip`），
用 `WaitForSeconds` 的话 hold 会变成整条运镜里唯一不加速的卡点。叠化段没法编进
Sequence（它是截屏协程），所以单独造了个只有 Interval 的 Sequence 来等，
`SetTarget(this)` 保持和其他镜头补间一致的清理语义。

**hold 不参与默认缓动的首/末判定。** 运行时 `BuildSegment` 只看 `duration` 字段，
hold 是另一个字段，天然不受影响；但**编辑器预览是按「段」展开的**，hold 会变成一个
`duration>0` 的独立段，如果不排除掉，`firstMove/lastMove/moveCount` 就会算错，
预览的缓动分配和运行时对不上。所以 `Segment.isHold` 在计数和赋值两个循环里都要跳过。
—— 这是「预览与运行时同构」这条约定最容易被破坏的地方，加新的段类型都要想一遍。

**辅助线画在取景框内，不是画布上。** 关键认知：**对话框不随镜头缩放**（它是 ZoomRoot
的兄弟），所以在玩家眼里它永远压着屏幕底部那一条。辅助线要表达的是「玩家看到的那一屏
的构图」，因此整图模式下必须画在**选中路径点的取景框**里、按比例落位；镜头视角模式下
画布本身就是那一屏，直接铺满。画在画布固定位置是错的（推近时取景框只占画布一小块，
遮挡区完全对不上）。zoom<1 时取景框比画布大，所有绘制统一走 `clip` 裁切。

**对话框遮挡区实测优先。** 从 `VNStage.dialogue` 拿 RectTransform，`GetWorldCorners`
换到 rootCanvas 局部坐标再按 `root.rect` 归一化成比例 —— 换了对话框皮肤、改了尺寸都跟得上。
量不到才退回生成器的默认布局（x 5%~95%、底边上方 28px 起、高 230px）。
实测结果 `x:0.05~0.95 y:0.761~0.974`，与默认布局吻合。

**撤销用窗口内独立栈，不挂 Unity 全局 Undo。** `Undo.RecordObject(this)` 也能用
（EditorWindow 是 ScriptableObject），但全局撤销栈是共享的：在 Scene 视图按 Ctrl+Z
可能撤到镜头路径点，顺序还会和场景编辑交错，很难预期。独立栈的代价是不进
Edit → Undo 菜单，换来的是「Ctrl+Z 只管这个窗口」这条清晰边界。
快捷键仍走 `ShortcutManager`（窗口作用域优先于全局 Undo，且可在 Edit → Shortcuts 改键）。

**快照用 `GenerateText()` 字符串，不是深拷贝对象图。** 路径点 + 开场/收尾叠化设置
全在这一串文本里，恢复就是反过来解析，天然与「文本是唯一真相」一致；顺带白拿了
序列化能力（`List<string>` 直接 `[SerializeField]`，重编译后还能继续撤销）。

**撤销粒度靠「静默 0.35 秒 + 鼠标已松开」自动切分**（`TrackUndo` 在 `Update` 里跑）：
拖 zoom 滑条那 200 帧、连着敲的几个字符自然合并成一步。清空 / 载入预设 / 套模板 /
解析载入这类**整段替换**在动手前先 `CommitUndo()` 强制切一步，免得跟上一次微调粘一起。
`PerformUndo` 开头也调 `CommitUndo()`——还没落栈的改动同样要能一步撤回。
`RestoreSnapshot` 里 `GUIUtility.keyboardControl = 0`：不丢焦点的话，数字框里显示的
仍是撤销前那串字符。换绑定行 / 从剧本重载走 `ResetUndo()` 重开一段历史，
绝不让 Ctrl+Z 撤回**上一行**的内容。

### 验证方法

`assets-refresh` 编译零错误，随后用 `script-execute` 跑了三组断言（结果见 Console）：

- **运行时链路**：`camseq` + 两个带 hold 的路径点 → `hold0=1.2 hold1=0.4`；
  `VNCamWaypoint.TryParse` 往返一致（`> 教室:head 1.9 0.8 ease:OutSine xfade:0.5 hold:1.2`
  解析后 `Format()` 与原文逐字相同）；老写法 `> middle` 不会被撑成 `> middle 1 0.8`；
  `hold:0` / `hold:abc` / 重复 `hold:` 一律 `TryParse=false`（退回纯文本，不被吞掉）。
- **预览时间轴**：`> left 1.3 0.5` / `> right 1.3 1 hold:2` / `> middle 1 0.8`
  总时长 = 4.3s（正确计入 hold）；hold 区间内 t=1.6s 与 t=3.4s 的镜头状态
  `offset=(-366,0) zoom=1.3` **完全相同**（确认停住不动）；不带 hold 的老序列时长
  仍是 1.3s（没有被新逻辑改变）。
- **撤销栈**：初始栈 0 → 改动 + CommitUndo 后栈 1 → 撤销回到 A、redo 栈 1 →
  重做回到 B 且 `hold:1.5` 一并回来 → 未落栈的改动直接 `PerformUndo` 也能撤回。
  `DialogueBandFractions()` 实测返回 `x:0.05~0.95 y:0.761~0.974`。
- 辅助线的实际观感、Ctrl+Z 的键位是否被全局 Undo 抢走（窗口作用域应当优先），
  是 IMGUI / ShortcutManager 交互，需要在编辑器里手动点验。

---

## 一〇二、羽毛球对战小游戏（2026-08-11，分支 `agent/badminton-minigame`）

### 需求 / 背景

用户要求把参考项目 `Student Age new`（学生时代）里的羽毛球小游戏做进本专案。

**先说前提：那份参考目录只有反编译源码，没有任何美术、预制体、DragonBones 骨骼数据**
（326 个 `.cs`，非代码文件只有 `.vs/` 与 `obj/` 的编译缓存）。所以这不是「搬运」，
是**照着逻辑重写**——与 `卡牌游戏并入视觉小说专案.md` 那次工程合并性质完全不同。

好消息是配方完整：`BadmintonMiniGameView.cs`（1263 行）把弹道数学、AI 决策、
精准判定、赛制全部写死在一个文件里，逐行读完即可复刻。

决策经三轮问答定案 11 项，完整记录在 **`羽毛球小游戏实施计划.md`**（本章不重复）。
要点：程序化假动画 / 完整还原玩法 / 全部四项系统联动 / 程序化球场 /
A・D + J + K 键位（鼠标只负责击球）/ ESC 认输 / 不做 Editor 调参窗口。

### 参考实现的核心配方

- **弹道是解析式抛物线，不是物理模拟**。`CalcTrack` 用「起点 + 落点 + 球网处过网高度」
  三点定二次曲线 `y = ax²+bx+c`，之后每帧 `x += flySpeed·dt`、`y` 直接代公式。
  一旦球路定下来，落点、最高点、对手该跑到哪里**全部可以立刻解析求出**——
  AI 不需要预测、轨迹虚点不需要模拟。整个玩法的可控性都建立在这上面。
- `flySpeed = √(|g·200 / 2a|)`，曲率越平飞越快、高吊球慢。
- **轨迹虚点 = 难度旋钮**：`trackDisplayRate` 控制预告显示多长的一截。
- **击球判定不是「按键即击球」**：按键只触发挥拍动画，真正判定在球与拍碰撞那一刻；
  `CalcAccurate(球x − 人x)` 决定界内概率——打不准不是立刻失误，而是**概率性出界**。
- 赛制 5 分制 + 净胜 2 分；结算一律写在 `CloseView()` 一条路径
  （`ReferenceProjectLearnings.md` §43b 记录的铁律，这次落地）。

### 文件改动清单

**新增运行时**（`Assets/Project/Scripts/VNEffects/Script/`）

- `VNBadmintonBallistics.cs` —— 手感参数类 `VNBadmintonTuning` + 球路结构 `VNBadmintonArc`
  + 纯静态数学：三点定抛物线、飞行速度、落点抽样、精准判定、接球点反解。
  **无 MonoBehaviour / 无 UI 依赖**，可单测。
- `VNBadmintonUi.cs` —— 三个文件共用的程序化 UI 辅助 + `VNBadmintonQuad`
  （uGUI 画不出梯形，球场透视地面靠它）。
- `VNBadmintonCourt.cs` —— 程序化球场与 HUD（天空/广告横幅/沙地/透视球场/场地线/
  球网/记分板/得分横幅/操作提示）。不是 MonoBehaviour，由模块构造并持有。
- `VNBadmintonActor.cs` —— 角色表现层。六态程序化假动画、球拍绕肩画弧、扣杀残影、
  脚影与落地冲击、台词气泡。
- `VNBadmintonDef.cs` —— 对手 + 难度 + 台词 + 立绘 + 赛制 + 音效槽位 ScriptableObject。
  合并了参考实现的 `BadmintonLevelCfg` 与 `LoveBadmintonCfg` 两张表。
- `VNBadmintonSfx.cs` —— 五个音效的代码合成（`AudioClip.Create`），Def 填了就覆盖。
- `VNBadmintonModule.cs` —— `VNEventModule` 子类，五态状态机 + 主循环 + 计分 + 结算。

**新增编辑器**

- `Editor/VNBadmintonInstaller.cs` —— `Tools → VN Effects → 场景装机 Install To Scene → 羽毛球对战 Badminton Module
  To Scene`，增量装机（照 `VNQuizInstaller` 范式），不重建场景、可重复执行。

**新增资产与内容**

- `Assets/VNEffects/Badminton/{新手,校队,王牌}.asset`
- `Assets/Scenarios/BadmintonDemo.vn.txt`

**改动**

- `VNGameConfig.cs` —— 加 `badmintons` 列表。
- `Editor/VNEffectsDemoSetup.cs` —— 加 `BadmintonDir` 常量 + 生成场景时建
  `BadmintonTemplate` 并登记 id `badminton`。
- `Editor/VNGameConfigTools.cs` —— 从场景导入 / 扫资产两条路径都补上羽球对手库。
- `Editor/VNScenarioLinter.cs` —— 结果名白名单加 `badminton`；新增 `unknown-badminton`
  检查（扫 `t:VNBadmintonDef` 资产，与 `unknown-quiz` 同款）。
- `Resources/VNLocale/ui.{zh,en,ja}.txt` —— 新增 `badminton.*` 共 17 个 key。
- **未提交** `Resources/VNGameConfig.asset` 的登记改动——里面混着用户未提交的
  `CG_test` 条目，按分支规矩不顺手带上；装机器会重建。

### 与参考实现的三处刻意偏离

1. **不使用 Physics2D**。参考实现靠 `Rigidbody2D` + `Collider2D` 做击球判定与落地翻滚，
   会把 Tag / Layer / 物理配置污染到全局。改成纯数学距离判定后确定性可重放、零全局配置。
   **代价是必须自己做子步进**：扣杀球 1350+ px/s，30fps 单帧位移 45px，
   而拍面命中半径只有 105px——不切小步的话低帧率时球会直接穿过球拍
   （参考实现靠连续碰撞检测规避了这点）。现在按最大单步 12px 切，上限 16 步。
2. **AI 起拍/起跳改用「还有多久到位」判定**，取代参考实现的距离阈值，帧率无关；
   提前量直接问表现层要 `ActiveWindowStart(kind)`，动画时长改了自动跟上。
3. **判定几何与动画分开**。`RacketPointFor()` 返回固定几何（站位 + 拍型对应的固定高度），
   不随挥拍动画的实际角度走。判定必须可预测、帧率无关，动画只负责让这一拍
   「看起来打到了」——参考实现用碰撞盒时两者是耦合的，那正是它需要连续碰撞检测的原因。

### ★ 坐标换算：0.75 倍 + 额外的 √k 修正

参考实现设计分辨率约 2560×1440（由 `minY 260` / `moveArea (200,1000)` / 截图逐像素反推），
本专案 1920×1080，所有长度量 ×0.75。完整对照表在实施计划第四节。

**但 flySpeed 还要再乘一次 √k = 0.866**：坐标缩放 k 后 `a → a/k`，
于是 `flySpeed → √k·flySpeed`，而保持**飞行时间不变**需要的是 `k·flySpeed`。
少了这一下，球相对屏幕会快约 15%、回合明显变短、来不及跑位——
数值全部照抄、看起来也能跑，但手感就是不对，而且极难归因。
这个系数在 Def 里暴露为 `flySpeedScale`。

同理跳跃的「米 → 像素」乘数从 ×100 改成 ×75，移动速度共用同一个 `pixelsPerUnit`。

### 系统联动

- **属性影响强度**：`powerstat: / speedstat: / jumpstat:` 指定读哪个 flag，
  能力 = Def 基础值 + `Clamp(属性值, 0, statCap)` × 每点增量（增量配在 Def）。
  照 `VNBattleModule.patkstat` 范式——模块不认识任何具体属性名，桥全在 flags 上。
- **战绩回写 flag**：`<前缀>_我方得分 / _对方得分 / _精准数 / _最长回合`，
  前缀默认「羽球」，剧本 `flag:` 可改。剧本可据此做「零封」「虽败犹荣」「打得漂亮」细分支。
- **多对手难度**：Def 解析三级 —— 剧本 `id:` → 剧本 `vs:`（同名资产）→ 库里只有一条时直接用。
- **立绘三级回退**：Def 的羽球专用图 → Def / `vs:` 指定角色的 `VNCharacterDef` 默认立绘
  → 模板兜底图 → 剪影占位。

### 三档难度

| | 扣杀倾向 | 接球率 | 轨迹预告 | 界内率 | 移速 | 力度 | 目标分 |
|---|---|---|---|---|---|---|---|
| 新手 | 0.05 | 0.62 | 1.00 | 0.85 | 5.0 | 0.45 | 5 |
| 校队 | 0.25 | 0.88 | 0.55 | 0.72 | 8.0 | 0.75 | 5 |
| 王牌 | 0.45 | 0.96 | 0.22 | 0.60 | 10.5 | 1.00 | 7 |

轨迹预告是最大的难度杠杆：王牌只让玩家看到 22% 的球路。

### 没做 Editor 调参窗口的补偿

用户明确不要调参预览窗口（决策 10）。补偿：**Editor 下每帧重读 Def 的手感参数**，
Play 着直接拖 Def 资产的 Inspector 就能实时看到变化，不用反复进出 Play Mode。
用 `JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this)` 就地覆盖
（加字段时不会漏改），`#if UNITY_EDITOR` 包住，运行时构建整段编译掉。
养成加成在 `ApplyTuning()` 里于 `CopyFrom` 之后重新叠加，不会被每帧重读冲掉。

### 音效

五个音效全部代码合成，配方 = 指数衰减包络 ×（衰减正弦 + 低通白噪）。
正弦决定音高（球拍绷弦的"当"），白噪决定质感（击打的"啪"），配比 `noise` 就是
「这一拍有多闷」。发球轻 / 击球中 / 精准亮 / 扣杀闷而重 / 落地最闷。
音量跟随 `VNAudio.seVolume`，Def 里填了真音效就逐条覆盖。

### 验证

- 弹道解算：发球 `(-300,375) → (300,195)` 解出 `a=-0.00323`、顶点 `(-46,583)`、
  速度 477px/s，飞行 1.26s——数值合理。
- 自动对拉（`debugAutoPlayer` 开关，让玩家也交给 AI）：打到 2-2、双方均能得分，
  发球→飞行→接球→精准判定→落点→计分全链路通。
- Def 生效：记分板显示「校队主力」、`opponentHitRate=0.88`（资产值而非默认 0.9）。
- 挥拍与起跳姿态、两侧台词气泡渲染正确。

### 坑（都踩过了，记下来免得再犯）

1. **`Graphic` 子类必须自己再写一遍 `[RequireComponent(typeof(CanvasRenderer))]`**。
   基类上那条不会被 `AddComponent` 走继承链读到。症状极具迷惑性：组件 active、
   enabled、material、canvas、顶点数据**全部正常**，就是不画。
   稳妥做法是创建时显式 `AddComponent<CanvasRenderer>()`。
2. 构建顺序有依赖：`ResetPositions()` 会碰 `_hitMarker`，必须排在 `BuildBall()` 之后。
3. `ShowTips` 是**单条通道**，连喊两次会把前一条掐掉（赛点提醒要并进同一条文案）。
4. 球体不能只用 `SparkleSprite`——在亮底球场上几乎看不见。现在是「软光晕 + 实心球头
   + 羽毛裙」三层。网面也别用实心半透明矩形，会变成一块压住球场的灰盒子。
5. 台词气泡宽度必须按 `GetPreferredValues` 算，固定宽度会被长句撑破。
6. **`screenshot-game-view` 会返回旧帧**——编辑器窗口不在前台时 Game View 不重绘。
   我因此追了三次「明明建好了却看不见」的幽灵。**验证渲染一律用 `screenshot-camera`**
   （直接渲到 RenderTexture，必定新帧）。
7. **编辑器在后台时 Play Mode 只跑 ~0.5 FPS**（`Time.unscaledDeltaTime` 高达 2.16s）。
   模块自己钳制了 `dt ≤ 0.05` 所以玩法不会乱，但 **DOTween 没有这个钳制**——
   一帧就把整段 1.7s 的气泡序列跑完了，截图当然什么都截不到。
8. **装机器结尾的 `EditorUtility.DisplayDialog` 会卡住 MCP**：模态框阻塞主线程，
   自动化调用会超时直到有人手点 OK。用脚本跑安装器时要有心理准备。

### 待办

- 用户提供侧身运动立绘后替换占位剪影（规格见实施计划第八节，逻辑零改动）。
- `VNGameConfig.asset` 的对手库登记需要用户自行提交（或跑一次装机器）。

---

## 一〇三、拍大头照小游戏（P1~P4）（2026-08-12，分支 `agent/photo-booth`）

### 需求 / 背景

用户要求把参考项目 `Student Age new`（学生时代）里的「大头贴」做进本专案，
并给了游戏内截图作为目标形态。

参考实现只有两个文件：`View.Love/PhotoboothView.cs`（304 行）与
`Config/LovePhotoboothCfg.cs`（只有 `id / name / itemPos` 三个字段）。
读完发现它比看上去简单得多——**没有输赢、没有评分**，关闭时固定 `result(1f)`，
本质是「选边框 + 切表情 + 摆贴纸 → 快门 → 截屏存 PNG」的装扮互动。

四轮问答定案（每轮 3~4 题）：

1. 玩法定位 = **装扮 + 主题评分**（不照抄「无输赢」）；合影 = **双人**，主角先用现有角色占位；
   素材 = **程序化生成默认 + 资产可覆盖**；照片 = **存 PNG + 相册界面**
2. 评分 = **清单制**（每个主题直接列出加分项）；主角立绘先占位；
   相册做成 **G 键画廊的第二个标签页**；装扮自由度 = **拖动 + 缩放旋转 + 右键删除**
3. 结算 = **模块内自绘**（照片 + 分数 + 评语）；分数 **写 flag + 可选自动加属性**；
   **限时装扮 + free 模式**；相册要**删除**能力
4. 分支起点 = 先把羽毛球合并回 main，再从 main 切

本章覆盖 P1（数据与数学）、P2（界面与交互）、P3（演出）、P4（系统接线）。
P5（画廊照片页）未做。

### 参考实现的核心配方

- 左栏「边框样式」= 配置表条数 + 1，第 0 项是「默认」（不加边框）。
- 边框的装饰件表 `itemPos` 每行 `[图号, x, y]`，生成后**可自由拖拽**。
- 右栏「切换表情」两列，各自角色的表情数，点一下 `l2d_role.SetExpression(i)`。
- 快门：倒数 3/2/1 换图 → 算出取景框的屏幕矩形 → `CaptureScreenshot(rect, path)`
  截屏裁剪存外部 PNG → 弹结果层，带「重拍 / 完成」。
- 结算一律走 `CloseView()` 一条路径（`ReferenceProjectLearnings.md` §43b 的老规矩）。

### 文件改动清单

**新增运行时**（`Assets/Project/Scripts/VNEffects/Script/`）

- `VNPhotoStickerDef.cs` —— 贴纸资产（id / 三语名 / Sprite 或程序化图形 / 色调 / 初始尺寸）。
  参考实现里贴纸是边框图的附属小图，这里拆成独立资产，因为同一张贴纸要被多个边框、
  多个主题的评分表复用。
- `VNPhotoFrameDef.cs` —— 边框资产。比参考多出：程序化兜底、人物开窗形状（椭圆/圆角/无）、
  前景层、水印文字、自带装饰件（可锁定）。
- `VNPhotoThemeDef.cs` —— 主题 + 清单制评分表（表情/边框/贴纸三张清单，每项带分数与命中评语）
  + 限时 + 基础分 + 完美线/及格线 + 贴纸总数上限（防刷分）+ 三档总评。
  另含共用的三语文本类 `VNPhotoLine`。
- `VNPhotoScore.cs` —— **纯静态评分数学**，无 MonoBehaviour / UI 依赖，可单测。
  故意保持无状态、无随机：同样的装扮永远同样的分，玩家才学得会「什么样算好照片」。
- `VNPhotoTextures.cs` —— 程序化贴图：5 套边框（粉格子/星空/胶片/简约白框/樱花）、
  椭圆与圆角遮罩、开窗描边环、10 种贴纸隐函数图形、相纸（九宫格）、实心圆。
- `VNPhotoCapture.cs` —— 取景框区域截图。**「怎么拍」全关在这一个文件里**，
  以后要换成独立 Camera + RenderTexture 只改它，模块不用动。
- `VNPhotoAlbum.cs` —— 相册全局存储（`persistentDataPath/vn_photos/` 的 PNG + index.json），
  与 20 槽存档完全分离（同 `VNCgUnlocks` 的道理）。索引损坏可从文件名恢复；
  纹理走 LRU 缓存（上限 12 张）防翻相册爆内存；容量上限 200 张。
- `VNPhotoBoothUi.cs` —— 程序化 UI 辅助 + `VNPhotoStickerItem`
  （左键拖 / 滚轮缩放 / Shift+滚轮旋转 / 右键删，走新版 Input System）。
- `VNPhotoSfx.cs` —— 五个代码合成音效（倒数滴 / 快门咔嚓 / 贴纸落位 / 计分嗒 / 结算琶音）。
  快门声是 noise 高 + 衰减快的**两下**——单簧快门就是「咔」+「嗒」。
- `VNPhotoBoothModule.cs` —— `VNEventModule` 子类。五态状态机
  （Dressing → Confirm / Shooting → Result → Ending）、装扮交互、限时、
  快门序列、结算演出、flag 与属性结算。

**新增编辑器**

- `Editor/VNPhotoBoothInstaller.cs` —— `Tools → VN Effects → 场景装机 Install To Scene → 拍大头照 Photo Booth Module
  Module To Scene`，增量装机（照 `VNBadmintonInstaller` 范式）。
  额外做一件事：**缺资产就铺一套默认的**（5 边框 / 10 贴纸 / 3 主题），
  已存在的绝不覆盖（那可能是用户改过的）。
  拆成 `Install()`（菜单外壳，弹框）+ `InstallCore(out ok)`（无弹框内核，供脚本调用）。

**修改**

- `VNGameConfig.cs` —— 新增 `photoFrames` / `photoStickers` / `photoThemes` 三个列表
  与 `photoMeCharacterId`（大头贴里「我」默认用哪个角色）。
- `Editor/VNScenarioLinter.cs` —— `photo` 的结果名集合、主题/边框 id 集合（扫资产）、
  以及四项专项校验（见下）。
- `Editor/VNScenarioSchema.cs` —— `event` 说明里补 `photo` 与 `badminton` 的参数用法。
- `Resources/VNLocale/ui.{zh,en,ja}.txt` —— 新增 photo.* 共 19 条 UI 字符串。
- `Assets/Scenarios/PhotoDemo.vn.txt` —— 演示剧本（自由拍照 / 主题评分 / 分数细分支 /
  换主题重拍 / 分数换算属性）。

### 剧本语法

```
event photo vs:<她> [me:<我>] [theme:<主题>] [mode:match|free] [frame:<边框>]
                    [time:<秒>] [stat:<属性>] [rate:<换算率>] [flag:<前缀>] [title:<标题>]
```

- 写了 `theme:` 才评分，结果 `完美 / 普通 / 失败`；不写（或 `mode:free`）= 自由拍照，
  只返回 `完成`。**两套结果名互斥**，Lint 会检查剧本写的结果行与模式对不对得上。
- 写 flag：`<前缀>_分数`、`<前缀>_档位`（2/1/0）、`<前缀>_次数`。
  原计划的 `_主题` 去掉了——flag 只存整数，主题名塞不进去。
- `stat:` + `rate:` 可让分数自动换算成属性（`加成 = round(分数 × rate)`，走 HUD 飘字）。

### 技术决策与取舍

1. **取景框不用 `GetPortrait`，必须用 `GetSprite`。** `GetPortrait` 在 `portraits` 列表里
   找不到同名表情时会退回 `portraits[0]`——本专案两个角色的 `portraits` 都只配了同一张
   `p01`，结果是**不管点哪个表情脸都不变**，而切表情正是这个玩法的核心。
2. **不复用角色资产的 `portraitScale`。** 那个值（4.96）是为对话框那个 230px 小窗
   「把脸怼满」标定的；合影要的是「头带肩、两人并排」，放大倍率完全不同。
   改用模块自己的 `photoFit` / `faceAnchor` / `pairSpread` 三个参数，
   换素材只调这三个数。
3. **截图走屏幕裁剪而不是 RenderTexture。** 代价是分辨率跟随窗口、快门那一帧必须
   把侧栏藏掉；换来的是照片天然带 URP 的 Bloom / Vignette（大头贴要的就是这个味道），
   而且代码量小。想换实现只改 `VNPhotoCapture`。
4. **相册里「重拍」会把上一张删掉。** 拍完先存，点「重拍」再删——否则玩家反复重拍
   会把相册塞满废片；只有点「完成」的那张才算数。
5. **贴纸计分只算玩家自己贴的。** 边框自带的装饰属于边框的一部分，已经由边框加分项
   算过一次，再算等于同一件事给两次分。
6. **星光爆发自己画。** 事件模块三铁律之一是不碰舞台演出，所以没用
   `VNAmbientParticles.PlaySparkleBurst`，改在 UI 层生成 8 个 sparkle 飞散。

### 修复记录（本章踩的坑）

1. **立绘定位连错三次**，一次比一次接近：
   ① 按**高度**算基准 → 脸被顶出框外（立绘是横图，宽高基准差三四倍）；
   ② 改宽度基准但复用 `portraitScale=4.96` → 人物放大五倍、两人重叠成一团；
   ③ `lift` 符号写反 → 脸在图片上半部却把图往**上**推，拍出一框裙子和腿。
   最终公式：`图宽 = slot宽 × photoFit`，`lift = −图高 × (0.5 − faceAnchor)`。
2. **粉格子边框第一版像 Photoshop 的透明棋盘格**（两级方格对比度太高）。
   压到 `Lighten(main, 0.58/0.76)` 才像格纹布。
3. **又一次被装机器的 `EditorUtility.DisplayDialog` 卡住 MCP**——
   一〇二章「坑」第 8 条已经记过这件事，这次还是踩了。
   这回做了根治：装机器拆成 `Install()`（弹框）+ `InstallCore()`（不弹框），
   **以后所有装机器都按这个形状写，脚本一律调 Core**。

### 验证方法

- 评分数学：`VNPhotoScore.Evaluate` 三个用例——满配 90 分/完美、
  白板 20 分/失败、中等 60 分/普通；贴纸贴 5 个但 `maxCount=3` 只按 3 个计分。
- 程序化贴图：逐张统计 alpha 覆盖率，确认既不是空图也不是实心方块
  （椭圆遮罩 78% ≈ π/4，验证了形状函数正确）。
- 界面：在 `VNScriptDemo` 场景里用 `HideFlags.DontSave` 的临时对象搭出面板，
  用 `screenshot-camera` 逐轮确认（**不能用 `screenshot-game-view`**，
  非播放状态它返回旧帧——一〇二章「坑」第 6 条）。验证完销毁，不写进场景。
- 剧本：`Tools → VN Effects → 剧本检查 Lint Scenarios`（Ctrl+Shift+L）对 `PhotoDemo.vn.txt` 无报错。

### 待办

- **P5：G 键画廊的「照片」标签页 + 删除**（`VNCgGallery` 加标签切换、
  `VNCgGallerySkin` 加两个可选槽位）。当前照片能拍能存，但只能去
  `persistentDataPath/vn_photos/` 目录看。
- 主角立绘：`VNGameConfig.photoMeCharacterId` 现在留空，剧本用 `me:` 临时指定；
  用户做好主角角色资产后填进去即可，代码零改动。
- 三个内置主题的加分表是按现有角色的表情名（害羞/微笑/坏笑/惊讶/生气/沮丧）写的，
  新增角色时记得对一遍表情名。

### 追加（同分支，2026-08-12）：实机反馈的四轮修复

用户跑起来后逐条反馈，都改在同一分支：

1. **照片全白**。闪白 0.45 秒才淡完，而抓图只等一帧——拍下来是一层白纱。
   把 `_flash` 加进抓图的隐藏列表。闪光是「拍照瞬间」的表现，本来就不该进照片。
2. **倒数改粉色**（白字太硬），粉字 + 白描边才是大头贴机的味道。
3. **人物可拖动**。`VNPhotoPortraitDragger`：铺满开窗的一块透明板，按下时**离谁近就拖谁**。
   不把拖拽挂在立绘 Image 上，是因为立绘是 1000px 级大图、绝大部分是透明像素、
   两人矩形几乎完全重叠——那样点谁全看渲染顺序；按 alpha 命中又要求贴图开 Read/Write。
4. **人物滚轮缩放**。用户的一句话点醒了方向：与其为每种素材构图去调取景参数
   （他的立绘尺寸并不统一：同角色「默认」图 2048×1126、其他表情 1216×832），
   不如**把镜头交给玩家**——顺手也让「想拍近点还是远点」变成玩法的一部分。
   于是原本准备加的「每表情取景微调」整套需求直接作废，只留了 `portraitTweaks` 备用。
5. **拖动范围放宽到能左右换位**。原来是开窗的 ±34%，连中线都过不去；
   基准站位在 ±0.275W，要能越过中线单侧行程至少 0.55W，现在给到 0.75W。
6. **背景图**（第三个标签页）。新增 `VNPhotoBackdropDef` + 8 种程序化样式
   （放射线/波点/斜条纹/黄昏/星夜/彩虹/光斑/纯白），画在两人身后、被开窗裁切、
   按 **cover** 铺满（宁可裁两侧也不留白边、不拉变形）。玩家可选、剧本 `bg:` 给初值、
   计入主题评分（与边框同级）。

   **为什么不复用剧情背景库**：剧情背景是 16:9 场景大图，塞进 4:3 开窗要裁掉大半；
   大头贴要的是「照相馆背景布」那种放射线、波点。两者是不同的东西，故意分开。

### 追加的坑

- **`EditorSceneManager.MarkSceneDirty` 在 Play Mode 下会抛异常**
  （"This cannot be used during play mode"）。装机器被脚本调用时，
  如果编辑器正在 Play，资产会建好但场景登记那一半会中断。跑装机器前先确认不在 Play。

### 追加（同分支）：贴纸与人物的前后层级

用户要求「有些贴纸在角色身后、有些在身前」「左右两个角色也要能换前后」。
三轮问答定案：层级**全由玩家当场决定**（不在资产里设默认）、操作用**双击**、
**拖动不改层级**（免得摆好的构图被碰一下就乱）。

- 取景框开窗内的层序改成：**背景 → 拖动板 → 人后贴纸层 → 我 → 她**。
  拖动板压在最底下是**刻意的**：它 `raycastTarget = true` 会吃掉射线，
  放上面的话「人后贴纸」永远点不到。人物本身不接收射线，不用担心被挡。
- 人后贴纸层的尺寸设成**与取景框相同**（超出开窗的部分被 Mask 裁掉，正好），
  并在换边框时用 `anchoredPosition = -windowPos` 抵消开窗偏移——
  这样两个贴纸层坐标系完全一致，翻层时 `SetParent(..., false)` 位置纹丝不动。
  （实测：翻层前后 anchoredPosition 都是 (-275.53, 184.19)。）
- 双击贴纸 = 前后翻转；双击人物 = 提到另一人前面（`SetAsLastSibling`）。
  两个手势语义一致：**双击 = 改层级**。
- 翻层时用不同 pitch 播同一个「贴纸落位」音效（往后 0.8、往前 1.15），
  不看画面也知道翻到哪边去了。

### 追加（同分支）：人物旋转 + 背景可缩放移动

用户要「贴纸和人物都能旋转」「背景也能放大缩小和移动」——都是为了更大的构图自由度。
（贴纸旋转其实早就有了，Shift+滚轮；这批补的是人物旋转与背景变换。）

定案：人物旋转沿用**与贴纸完全相同的手势**（Shift+滚轮）；
背景统一加 **Ctrl 修饰键**（Ctrl+拖动 = 移位、Ctrl+滚轮 = 缩放）——
拖动板整块都归人物，不加修饰键分不出玩家要动谁；
背景**不允许缩到比铺满更小**（AI 生成的图边上常有绿幕残边）。

- 镜像的那一位旋转角度要取反，否则同样往右滚，两人会朝相反方向倒。
- **「不露边」不需要额外判断**：可移动范围直接定义成「溢出开窗的那一半」
  （`slack = max(0, (size - window) / 2)`），scale=1（cover）时某个方向的 slack
  天然是 0，于是钳制本身就保证了不露边。实测 cover 状态下纵向偏移被钳成 0、
  横向还能移 44px（那个方向本来就有溢出），放大 1.6 倍后钳位正好等于溢出的一半。
- 钳制在模块里做（只有那边知道 cover 尺寸），算完写回 dragger，
  所以拖到底之后继续拖不会累积出一个「看不见的巨大偏移量」。
- 换背景时重置位移与缩放：构图不一样了，上一张调好的偏移没有意义。

### 追加（同分支）：涂鸦（落書き）

大头贴机的经典功能：拿不同颜色的笔在照片上画、也能擦掉。
定案：画在**最上层**（盖住一切）、工具放**左栏第四个标签页**、
功能要撤销/清空/荧光笔、笔粗要**滑块自由调**（不是几个档位）、不计分。

**为什么用位图而不是矢量线段**：需求里有「擦除」。位图擦除就是把 alpha 抹掉，
一行代码；矢量要么只能整笔删除，要么得再叠一层遮罩去挖洞——
同样的效果，复杂度差一个量级。

**为什么是两张画布**：荧光笔要发光，走 `VN/Additive`
（`Blend SrcAlpha One` + HDR `_TintColor`，项目现成的），普通笔要正常 Alpha 混合——
两种混合模式没法在同一张图里共存。所以普通笔与荧光笔各占一张纹理叠着显示。
好处是一笔只落在其中一张上，**撤销快照只需要存被动过的那一张**
（橡皮除外，它两张一起擦）。

- 画布 640×480，显示时拉伸到取景框 880×660。笔刷自带柔边，放大后看不出马赛克；
  换来的是每帧 `Apply` 只要半毫秒、一份撤销快照只要 1.2MB（上限 5 步）。
- **线段必须补点**：鼠标一帧能跑很远，只在指针位置盖章的话画出来是一串断掉的圆。
  按 `penSize * 0.3` 的步长在两点间插值补盖。
- 笔迹合成没有做严格的 source-over，直接朝笔色 `lerp`——涂鸦不需要，
  而且反复涂抹时反而更接近真实马克笔的手感。
- 输入板（`VNPhotoDoodleInput`）只在停留于「涂鸦」页时 `raycastTarget = true`，
  离开立刻关掉，否则它铺在最上层会把人物与贴纸的操作全吃掉。
- 笔粗预览圆点按 `ViewW / 画布宽` 换算显示尺寸，做到所见即所得。

### 追加（同分支）：P5 画廊照片页 + 删除

G 键画廊左上角加 `CG ｜ 照片` 两个标签，照片页就是大头贴的相册。

- **标签条、删除按钮、删除确认全部程序化补**：`VNCgGallery` 是完全依赖皮肤 prefab 的
  （槽位缺失直接抛异常），但为这三样加 prefab 槽位就意味着用户得重新导出皮肤。
  按项目「单项缺失只退回该项程序化 UI」的规则，直接在 `BuildPhotoExtras()` 里补。
  位置能放在左上角，是因为读了 prefab：标题与进度文本的 `anchoredPosition.x` 都是 600，
  左边那块是空的。
- CG 是 16:9、照片是 4:3，共用同一套网格 —— 切页时改 `GridLayoutGroup.cellSize`，
  切回 CG 时恢复（进 Build 时先把原值记下来）。
- **缩略图必须走独立缓存**：`VNPhotoAlbum.LoadTexture` 是 12 张的 LRU，
  一屏几十张的话先加载的纹理会被驱逐，而 Sprite 还引用着它——直接变成白块。
  所以新增 `LoadThumbnail`，降采样到 192×144（约 110KB/张）、单独字典、不驱逐；
  全屏浏览仍走原来的大图 LRU。关画廊时两份一起释放。

### 追加的坑

- **`Destroy` 的延迟害了网格重建**：`RebuildGrid` 里 `Destroy(child)` 要等帧末才执行，
  在那之前旧格子仍挂在 `GridLayoutGroup` 下参与布局。原来的画廊只在打开时构建一次
  （那时网格是空的）所以一直没暴露；加了切页之后立刻现形——实测切一次
  `childCount` 从 7 变成 13，两页的格子挤在一起。
  修法：`child.SetParent(null, false)` 之后再 `Destroy`。

---

## 一〇四、大头贴界面放大 + 操作说明折叠面板（2026-08-12，分支 `agent/photo-layout-expand`）

### 需求

用户实测后提了三点：

1. 大头贴机身在 1920×1080 里显得小，四周空得多，希望尽量填满；
2. 结算时吐出来的那张相纸也太小；
3. 底部那条长长的操作说明（`photo.hint`）横在快门下面、还溢出到机身外，
   希望挪到右上角，做成能展开 / 折叠的。

第 1 点给了三个分配方案，用户选了「取景框优先放大」；第 3 点确认用**点击**开合
（不是悬停）。

### 文件改动

| 文件 | 改动 |
|---|---|
| `VNPhotoBoothModule.cs` | 全套布局常数重排；新增 `BuildHelpPanel` / `ToggleHelp`；删掉底栏说明；结算相纸放大并收起左右栏与取景框 |
| `VNPhotoDoodle.cs` | 画布分辨率 640×480 → 768×576 |
| `Resources/VNLocale/ui.{zh,en,ja}.txt` | 删 `photo.hint`，新增 `photo.help.title` / `photo.help.body` |

### 尺寸对照

| 部件 | 原 | 现 |
|---|---|---|
| 机身 | 1720×900 | 1860×1020 |
| 取景框（= 照片） | 880×660 | 1040×780（仍 4:3） |
| 左 / 右侧栏 | 300×700 / 330×700 | 340×900（两栏统一） |
| 边框·背景格 | 258×150 | 300×176 |
| 贴纸格 | 124×124 | 144×144 |
| 表情格 | 140×140（一屏 4 行） | 146×146（一屏 5 行） |
| 结算相纸 | 560×500 | 860×770 |
| 相纸内照片 | 500×375 | 780×585 |
| 涂鸦画布 | 640×480 | 768×576 |

横向是按「机身内边距 30 + 栏 340 + 间隙 40 + 取景框 1040 + 间隙 40 + 栏 340 + 30
= 1860」凑的，所以取景框仍然**水平居中**——倒数数字、闪白、相纸飞出的起点都以它为
基准，偏一点点就会看出来。纵向新增 `ViewY = 22`（上让标题条、下让快门），
`PanelY = -40`（两栏比取景框更高，底部包住快门那一行）。

### 技术决策

- **布局常数化**。原来 `700f`、`284f`、`-20f` 这些数字散在十几处，改一次尺寸要
  全文搜。这次抽成 `PanelW / PanelH / PanelX / PanelY / ListW / ListH / ListY /
  ViewY / TitleY / FaceCellSize / FaceClipSize / PaperW / PaperH`，
  以后再调只动常数区。`ViewW / ViewH` 本来就是比例式引用（水印、贴纸边界、
  随机落点、拖动范围全是 `ViewW * 系数`），一个数改完全部跟随。

- **涂鸦画布跟着放大**。位图是拉伸到取景框显示的，取景框 880→1040 而画布不动的话
  放大倍率从 1.375x 变成 1.63x，笔迹会明显糊。提到 768×576 把倍率压回 1.35x，
  代价是撤销快照 1.2MB → 1.7MB/张（5 步共约 8.6MB）。这两个数字是**成对**的，
  以后再动取景框要一起算。

- **说明面板做成「?」圆钮 + 点击展开的卡片**，右上角。折叠时只占 54px，
  展开时向左下铺一张 440px 宽的卡片，内容按「人物/贴纸 → 背景 → 其他」分组竖排，
  比原来那条挤在一行的长句好读得多。底色**必须不透明**——它会盖在右栏的表情格上，
  留一点透明度就变成一层脏滤镜，字反而看不清（实测 0.95 就已经不行）。
  卡片高度用 `ForceMeshUpdate()` + `preferredHeight` 手工量（TMP + ContentSizeFitter
  首帧量不准，与 `VNSnsView` 同一路数）。

- **限时条整体左移 100px**，秒数从条的右侧挪到左侧并右对齐——右上角那块归说明钮了。
  第一版秒数只让了 200px，实测直接压在条的左端上，改成让 537px 才够。

- **结算时把左右栏与取景框一起收起来**。分数栏（x=380，宽 640）与右栏表情格
  在横向上本来就重叠，原来相纸小、背板浅时不明显；相纸放大到 860 之后
  「+20」这类加分项直接飘在头像上根本读不清。取景框则是那张亮底会从背板后面顶出来。
  取景框要等相纸飞入动画跑完（0.65s）再藏，否则「从取景框飞出来」的连续感就没了。
  背板同时从 0.72 加深到 0.9。三样都在 `Retake()` 里成对恢复。

- **说明卡片必须进 `ShootRoutine` 的 `hide` 列表**。它是往左下展开的，会压住取景框
  右上角，玩家开着卡片按快门就会拍进照片里。

### 验证

Play Mode 实跑 `PhotoDemo.vn.txt`：第 19 行（自由拍照）与第 31 行（主题·限时·评分）
两条路径，逐张截图核对——装扮页三栏、说明卡片展开、涂鸦页、拍照结算、重拍复位。
`assets-refresh` 编译零错误。

已知无关现象：编辑器 Game View 未聚焦时 Canvas 不重建，截图偶尔会拍到「立绘对象
状态全对但没画出来」的一帧（`enabled=True`、`sprite` 有、尺寸位置都对），
聚焦跑就正常，与本次改动无关。

---

## 一〇五、角色名牌装饰字体样式（2026-08-12，分支 `agent/nameplate-style`）

### 需求 / 背景

用户给了三张日式 galgame 名字栏截图（粗黑体 + 粗描边 + 面色渐变 + 投影 + 拉开的字距），
希望角色名牌做成同样的风格。原名牌是**霞鹜文楷 Regular + TMP 伪粗 + 紫色圆角底板**，
气质与参考图正好相反——参考图全是黑体系 Heavy 字重，靠字重本身撑起分量。

顺带发现一个历史遗留：`VNDialogueBox.nameTagColor` 的注释写着「角色定义里的 nameColor 优先」，
但 `VNStage.Say()` 只传了名字和文本，**从来没有把 `VNCharacterDef.nameColor` 传给名牌**——
这次一并兑现。

### 文件改动清单

**新增**

- `Assets/Resources/VNFonts/NotoSansSC-Black.otf`——中文 / 英文装饰字体（思源黑体 Black，
  取自 noto-cjk 的 `Sans/SubsetOTF/SC`，与既有 Regular 同一版本，OFL 可商用，8.4MB）
- `Assets/Resources/VNFonts/NotoSansJP-Black.otf`——日文装饰字体（4.6MB）。
  **不能拿 SC Black 顶**：SC 的假名字形不合日文排印规范，这与正文字体是同一条既有原则
- `Script/VNNameplateStyle.cs`——名牌装饰样式：纯数据 + 静态 `ApplyTo()`，
  内置四套预设（Plain 老外观 / Bold / Plate / Outline）

**修改**

- `Script/VNFont.cs`——新增装饰字体入口 `DisplayAsset` / `DisplayAssetFor(lang)`；
  `Profile` 加 `padding` / `samplePointSize` / `atlasSize` / `isDisplay` 四个字段；
  `HandleLanguageChanged` 改成正文与装饰**分开替换**；新增 `DisplayFontChanged` 事件
- `Script/VNCharacterDef.cs`——加 `overrideNameplateColors` + 渐变上色 / 下色 / 描边色三字段，
  以及 `GetNameplateColors()`（没勾自定义时由 `nameColor` 自动推算）
- `VNDialogueBox.cs`——加 `nameplateStyle` / `autoResizeNameTag` 字段与
  `SetNameplateStyle()` / `SetSpeakerStyle()` API；内部 `ApplyNameplateStyle()` /
  `ResizeNameTag()` / `EnsureUnderline()` / `FindPlateImage()`；订阅 `DisplayFontChanged`
- `Script/VNStage.cs`——`Say()` 里在两条分支上分别 `SetSpeakerStyle(c.def)` / `SetSpeakerStyle(null)`

### 技术决策与取舍

**1. 三个硬约束（写在 VNNameplateStyle 头注释里）**

- **材质必须走实例**。TMP 组件默认用 `fontSharedMaterial`，即字体资产自带的那一份——
  直接改它会把正文、按钮、Backlog 里所有用同一字体的文字一起改掉。
  一律通过 `text.fontMaterial` 取（TMP 首次访问时自动 new 一份实例）。
  实机验证材质名确实是 `Noto Sans SC - Black Material (Instance)`。
- **underlay 通道只有一条**，所以「第二层外描边」和「投影」二选一（`UnderlayUse` 枚举）。
  Bold / Outline 用它做投影，Plate 用它做深色外描边（offset 归零 + 大 dilate）。
  要两者兼得只能叠第二个 TMP 组件，不值得。
- **改 underlay 参数前必须 `EnableKeyword("UNDERLAY_ON")`**，否则参数完全没反应。

**2. padding 与 samplePointSize 必须等比例（本次最大的坑）**

描边实际像素厚度 ≈ `outlineWidth ×(padding+1)×(显示字号 / 采样点)`，padding 是描边粗细的天花板。
调参过程实测：

| 采样点 | padding | 结果 |
|---|---|---|
| 64 | 14 | 描边推到 0.2 就饱和，再调大数值毫无视觉变化（不是切角，是直接被钳住） |
| 64 | 24 | **反而更糟**——padding 占采样点 37%，字形在图集里的有效分辨率被挤掉，SDF 梯度变缓，描边和投影一起糊成一层淡影 |
| 120 | 22 | 正解。比例 ~18%，描边厚实且字形锐利 |

所以装饰字体单开一套资产（`DisplaySamplePointSize = 120` / `DisplayAtlasPadding = 22` /
图集 1024）：正文那几千个汉字用不起这么大的采样点和 padding，而角色名只有十几个字，
图集成本几乎为零。

**3. 每角色配色自动推算，零改动向后兼容**

没勾 `overrideNameplateColors` 时，由既有的 `nameColor` 走 HSV 推算：
提亮当渐变上色、压暗当下色、描边给白。存量角色资产一个字都不用改，
就能各自拿到一套与自己底色同源的名牌配色。

**4. 语言切换必须把正文字体与装饰字体分开替换**

原 `HandleLanguageChanged` 把所有 VNFont 管理的字体统一换成正文字体。
加装饰字体后若不区分，切语言会把名牌的 Heavy 字体也换成正文字体，粗描边样式当场垮掉。
改成两个集合分别替换；两集合重合时（装饰字体解析失败降级成正文字体）按正文处理。
另外**换 font 会让 TMP 丢掉材质实例**，所以补了 `DisplayFontChanged` 事件，
`VNDialogueBox` 收到后重新 `ApplyNameplateStyle()`。

**5. 名牌宽度自适应只动程序化默认皮肤**

`ResizeNameTag()` 在 `HasCustomSkin` 时直接返回——美术 prefab 的名牌尺寸是照着背景图排的，
擅自改会破相。

### 验证方法

`VNScriptDemo` 场景进 Play，用 `script-execute` 关掉标题菜单覆盖层后
`SetSpeakerStyle(def)` + `Say()`，逐一切换三套预设截 Game View 比对：

- 参数落地：`font=NotoSansSC-Black (VNFont Display)` / `sample=120` / `padding=22` /
  `outlineW=0.3` / `mat=… (Instance)`
- Bold：紫色渐变粗黑 + 白描边 + 底部横线，无底板
- Plate：紫色圆角底板 + 白字 + 白内描边 + 深外描边
- Outline：白字 + 角色色粗描边，无底板

**排错提示**：Play 里对话框不显示不一定是名牌的问题——标题菜单接管时
`_interfaceVisible` 是 false，要先 `SetInterfaceVisible(true)`。
另外 `editor-application-set-state` 返回后 Play 模式未必已完全启动，
立刻执行的脚本会作用在编辑模式对象上、进 Play 后被重置。

### 后续可做（未做）

- 剧本命令切换名牌样式（`ui nameplate <id>`）：要走 vn-new-command 全链路
  （Parser / Runner / Schema / Lint / 存档），本次范围是「只改对话框名牌」故未做
- 名牌入场演出（换人说话时轻微弹一下 + 描边闪光）
- 把样式铺到 Backlog / SNS / 存档缩略图的说话者名字

---

## 一〇六、AI 自由聊天模式（P0~P2）（2026-08-13，分支 `agent/ai-free-talk`）

### 需求 / 背景

用户想做一个「AI 自由聊天」模式：把女主角接大模型实时生成台词，一问一答；
她说完之后给玩家三个候选回复（像现有 choice 一样），玩家选一个再送回 AI 进入下一轮。

四个前置决策（开工前逐条确认，不猜）：

| 决策 | 结论 | 理由 |
|---|---|---|
| 模型 | Gemini 3.5 Flash Lite | 用户指定；$0.30/$2.50 每 MTok，实测一轮约 $0.0003 |
| Key 部署 | 仅本地开发 / 自用 | key 不打包不进仓库；要发行必须改玩家自填或自建中转 |
| 落地形态 | `event` 事件模块 | 契合现有玩法约定、结果可分支；代价是事件中不可存档 |
| 三个选项 | AI 同一次请求一起生成 | 一次请求拿齐台词+表情+漫符+好感+选项，延迟与成本都最低 |
| 内容定位 | 仅番外 / 自由时间 | AI 内容不进翻译表、无配音、玩家可能断网，主线不能依赖 |

### 文件改动清单

**新增（运行时）**

- `Script/VNAiKey.cs`——API Key 三级回退读取（环境变量 → 仓库外文件 → 仓库内文件）。
  key 只在内存缓存、任何日志都不打印；`#if UNITY_EDITOR` 挡住 Build 版本读取
- `Script/VNAiClient.cs`——Gemini `generateContent` 的协程封装，**全项目唯一碰 HTTP 的文件**。
  失败分 8 类（NoKey/Network/Auth/RateLimited/Server/Blocked/Truncated/BadResponse），
  429/5xx/网络错误自带指数退避重试
- `Script/VNAiPersonaDef.cs`——人格资产（Create → VN → AI Persona）
- `Script/VNAiConversation.cs`——纯逻辑层（无 MonoBehaviour）：system prompt 组装、
  JSON Schema 生成、历史裁剪、响应解析与钳制
- `Script/VNAiTalkModule.cs`——`event aitalk` 事件模块

**新增（编辑器）**

- `Editor/VNAiConnectionTester.cs`——两级自检菜单（Tools → VN Effects → AI）
- `Editor/VNAiTalkInstaller.cs`——增量装机（Tools → VN Effects → 场景装机 Install To Scene → AI 自由聊天 AI Talk Module）

**新增（资产 / 剧本）**

- `Assets/Art/VNEffects/AiPersonas/星野结衣_日常.asset`——首套人格。
  定位是**黑发辣妹**（外表清纯 / 性格辣的反差型，直接契合现有黑长直立绘，不用换素材）：
  日系语气词（「诶——」「超~烦的」「真的假的？」）、明着撩主角、享受看他脸红；
  但**她自己没被这样对待过，玩家认真直球回去时先绷不住的反而是她**——
  这条写进 `relationship`，是三个候选回复里「直球」那一档的存在意义。
  `boundaries` 补了一条「就算害羞也是辣妹式的恼羞成怒，不是文静脸红」，
  防止她一被撩就退化成乖乖女。
- `Assets/Scenarios/AiTalkDemo.vn.txt`——演示剧本

**修改**

- `Script/VNGameConfig.cs`——加 `aiPersonas` 列表 + `FindAiPersona()`
- `Editor/VNGameConfigTools.cs`——Sync Game Config 扫描 `VNAiPersonaDef`
- `Editor/VNScenarioSchema.cs`——`event` 提示文案补 aitalk 用法
- `Editor/VNScenarioLinter.cs`——四条新规则 + aitalk 结果名表
- `.gitignore`——挡掉 `/GeminiAiApiKey.txt`、`/*ApiKey*.txt`、`/*.apikey`

### 技术决策与取舍

**1. Parser / Runner 一行未改**

`event` 本来就是关键字，kwargs 走通用解析，`EventCo` 已把 `cmd.kwargs` 塞进 `VNEventContext`。
所以新玩法只是新的模块 id，和 `event badminton` / `event photo` 同构。
更关键的是 `EventCo` 等模块的方式是 `while (result == null) yield return null` 纯轮询——
一个要等 1.5 秒网络请求的模块，和一个等玩家点按钮的模块，对 Runner 完全一样，不阻塞主线程。

**2. 逐项 curl 实测确定的 Gemini 契约（三个假设错了两个）**

| 开工前的假设 | 实测 |
|---|---|
| thinking 默认开着，要关掉省延迟 | Flash Lite **默认就不思考**（0 思考 token），白担心 |
| `thinkingConfig.thinkingBudget: 0` 关思考 | **400**。字段是 `thinkingConfig.thinkingLevel`，取值只有 `minimal/low/medium/high`，没有 `off`/`dynamic`；放 `generationConfig` 外层报 400 Unknown name |
| （没想到） | 被安全策略拦下时 `candidates[0].content.parts` 是空的，直接取 `parts[0]` 会空引用 |

其余确认可用：`responseSchema` 支持 `enum` / `minItems` / `maxItems` / `propertyOrdering`；
鉴权走 `x-goog-api-key` 请求头（不用 `?key=` 查询参数，那会把 key 写进日志和 URL 历史）。

**3. 结构化输出是整个设计的核心**

一次请求返回 `reply` / `emotion` / `mark` / `affection_delta` / `options[3]{text,tone}` / `should_end`。
`emotion` 与 `mark` 的 enum **从角色资产实时生成**，AI 物理上编不出不存在的表情名，换角色自动适配。
三个选项各带隐藏语气标签（温柔/玩笑/直球），累计写 flag 可统计玩家倾向。

**4. 永远不信任模型输出（三处防御，实测都触发过）**

- `affection_delta` 强制 `Clamp`。Gemini 的 schema 子集**不支持 `minimum`/`maximum`**，
  不钳的话实测会给 +5 这种值，一轮就能把好感刷爆
- 表情不在白名单则降级到第一个；漫符非法则这轮不出符号
- 候选回复不足 3 个用「……」补齐——宁可演出打折，也不让模块崩

**5. 刻意破一次「模块三铁律」**

铁律说「不直接改舞台演出」，但 AI 控制表情恰恰就是改舞台。自绘立绘要把眨眼 / 口型 /
色调匹配 / 出场动画全部重接一遍，代价远大于收益，所以选择破例，但把边界收紧：

- 只碰**表情**和**对话框内容**两样，绝不碰位置 / 缩放 / 背景 / 特效
- 进入时记下原表情，**正常结束、ESC 退出、CancelForDebug 三条路径都还原**

回报是 `VNStage.Say(id, expr, text)` 一个调用带齐表情切换、说话者高亮、头像、
名牌配色、打字机、口型；三个候选回复直接走 `VNChoicePanel`，飞入 / 悬停扫光 /
落选溶解全部白赚。另两条铁律严格遵守（`unscaledTime`、Tween 全部 `SetUpdate(true)` + `SetLink`）。

**6. 射线的坑**

EventLayer 排序 60，选项面板在 45——**在模块下方**。所以模块自绘的一切默认
`raycastTarget = false`（在 `CreateImage` / `CreateText` 里默认关掉），否则会把选项点击全吃掉。
唯一例外是 ESC 确认框，它就是要独占输入。同类坑见一〇三章拍照模块的「拖动板必须压最底」。

**7. 本地化的硬限制**

项目的本地化是「剧本只写中文 + FNV-1a 旁路翻译表」，AI 实时生成的台词**永远不在表里**。
解法是按 `VNLocale.Language` 切 system prompt 让 AI 直接用目标语言回复；
UI 文案（「正在输入…」等）照旧走 `VNLocale.T(key)`。同理 AI 台词**没有配音**。
这也是把本模式限定在番外 / 自由时间的原因之一。

### 修复记录

**开发期自检抓到的**

- **编辑器协程泵死循环**：子协程跑完弹栈后，父协程的 `Current` 仍指向那个**已耗尽的**
  子协程对象，只判断类型会把它无限重新压栈，父协程永远前进不了。
  表现是「点了菜单没反应、也不报错」。改用 `_started` 集合保证每个子协程只压栈一次。
  第一个测试没暴露是因为它没有嵌套协程
- 人格资产里写的「感叹号」不是合法漫符别名（正确写法是「叹号」），白名单机制正确拦下并告警
- `EditorUtility.DisplayDialog` 会阻塞主线程等用户点击，自动化调用会把编辑器卡死 →
  装机器拆成 `Install(bool interactive)`

**Play Mode 实测抓到的**

- **AI 混出繁体字**（「才、才沒有」），与游戏其余文本对不上 →
  输出规则补「全部文字使用简体中文，禁止出现繁体字」，复测三轮全简体
- **表情立绘构图不统一时会穿帮**（一般性风险，非本角色的实际问题）：
  AI 换表情是瞬间切图、没有过渡，素材里某张若换了服装 / 季节 / 景别，一选中就穿帮。
  在 `allowedEmotions` 的 Header 里写清楚「只列构图一致的表情」。
  > **更正**：Play Mode 首测时误判星野结衣的立绘构图不一致并据此提了建议。
  > 复查后确认 `id=星野结衣` 绑的是 `星野结衣.asset`，7 个表情全部用同一套
  > `hoshino_*.png`（同一节电车、同一水手服，坏笑复用 smile 图），构图是齐的，
  > 无需限制 `allowedEmotions`。当时看到的不一致画面应为场景启动脚本残留的其他立绘。
  > `星野结衣 2/3.asset` 的 id 其实是 `星野结衣2`，与本人格无关。

### 验证方法

- **契约验证**：curl 逐项二分，定位 400 的唯一元凶并测出各 thinkingLevel 的
  思考 token 与延迟（`minimal` 与不写等价，均为 0 token / 约 0.8s）
- **连通性**：Tools → VN Effects → AI → Test Gemini Connection（不进 Play Mode）
- **逻辑层**：Tools → VN Effects → AI → 试聊 3 轮 Test Persona Talk（3 轮真实对话，
  每轮随机挑一个选项当玩家回复，验多轮上下文与历史裁剪）。
  两级诊断——前者过、后者挂 = 问题在逻辑层不在网络
- **Lint**：写了一份故意错五处的探针剧本，确认
  `aitalk-missing-target` / `unknown-ai-persona` / `aitalk-persona-mismatch` /
  `aitalk-no-failure-branch` / `bad-event-outcome` 五条全部按预期触发，验完即删
- **端到端**：Play Mode 跑 `AiTalkDemo.vn.txt`，截图确认立绘 / 名牌 / 头像 /
  打字机 / 三选项飞入 / 选完推进到第 2 轮 / 表情随 AI 切换

实测成本与延迟：一轮约 217~230 token、1.4~1.7s、约 $0.0003；一场 12 轮约 $0.004。

### 后续可做（未做）

- **跨场景记忆**（性价比最高）：每场结束让 AI 生成一句摘要写进独立 JSON
  （仿 `VNCgUnlocks` / `VNPhotoAlbum` 的全局存储），下次注入 `VNAiContext.memory` →
  「她记得你上次说过喜欢猫」。`VNAiContext.memory` 字段已经预留好
- **流式输出**（SSE）：边收边打字机，体感更快。要自定义 `DownloadHandlerScript`，
  且与结构化输出同用会更麻烦，故一期用非流式 + 「正在输入…」动画
- 中途存档：需要改成非 event 的视图层（仿 `VNSnsView`）+ 走 vn-save-compat 三处同步
- 每轮进回想：`EventCo` 只在结束后记一条，要逐轮进 Backlog 需经 `VNStage` 开口子
- 道具联动、AI 当裁判点评、AI 出题喂给 `VNQuizModule`
- TTS 配音

---

## 一〇七、AI 聊天对话日志 + 完全指南文档（2026-08-13，分支 `agent/aitalk-transcript-log`）

### 需求 / 背景

用户提了两件事：① 每次对话结束把全程写进文件，方便开发调试、也方便事后回看
聊了什么、给了哪三个选项、花了多少 token / 钱；② 要一份讲原理的文档——
记忆怎么运作、输入输出怎么运作、调哪里有什么效果、topic / flag / optionTones
怎么设计、有什么玩法 idea。

顺带回答了用户的问题「现在有记忆功能吗」：单场内有（每轮重发完整历史），
跨场没有（`VNAiContext.memory` 字段留着但没接存储）。

### 文件改动清单

**新增**

- `Script/VNAiTalkLog.cs`——一场对话的完整记录 + 落盘。纯类不继承 MonoBehaviour
  （路径拼装、Markdown 渲染、成本累加都是纯逻辑）
- `AiTalkGuide.md`——十三节的完全指南（原理 / 记忆 / 输入输出 / 调参手册 /
  flag 清单 / optionTones 设计 / 日志用法 / 成本 / Ideas / 限制 / 排错）

**修改**

- `Script/VNAiTalkModule.cs`——接日志：首轮存 system prompt、每轮存请求与回复、
  玩家选完回填 `pickedIndex`、`FinishWith` 落盘。新增 `logMode` 字段。
  顺带把写 flag 抽成 `SetFlag()`，写 flag 的同时记一行给日志，省得两处各写一遍
- `.gitignore`——`/AiTalkLogs/`
- `CLAUDE.md` 文件头文档索引、`HowToUse.md` aitalk 小节——指向新文档

### 技术决策与取舍

**1. 为什么两份文件都写**

`.md` 给眼睛（调人格时直接看对话、对比两次的 system prompt），
`.json` 给脚本（每轮 17 个结构化字段，以后统计「哪种 topic 好感增幅高」
「玩家最爱选哪种语气」「一个月花了多少钱」）。
从 Markdown 反解结构化数据很痛苦，同时写两份的成本却近乎为零。

**2. 为什么默认只在编辑器写**

这是开发调试功能。发行版往玩家硬盘写对话记录既占空间也涉及隐私。
`logMode` 三档：仅编辑器（默认）/ 总是 / 关闭。
路径也跟着分：编辑器写项目根 `AiTalkLogs/`（双击就能看），
Build 写 `persistentDataPath`（项目目录在玩家机器上不存在）。

**3. system prompt 只存首轮**

后续轮次只有「此刻的情况」段里的剩余轮数在变，全存 20 遍纯属浪费。
存一份就够对比「改了人格资产之后发出去的提示词变了没有」。

**4. 日志自己会检查配置错误**

文件头对比 `turns:` 与 `historyTurns`，超了就打 ⚠——
这正是用户当时写 `turns:21` 而人格 `historyTurns=12` 踩到的坑
（第 13 轮起最早的对话被裁掉，她会「忘记」开头）。
把检查放进日志比放进 Lint 更合适：这不是语法错误，是调参权衡，
只有实际跑过一场才知道该不该改。

**5. `_log` 就地 new 而不是在 OnLaunch 里建**

`OnLaunch` 有几条提前 return（找不到人格 / 配置非法），那些路径也会走到
`FinishWith` → 落盘。字段就地初始化才不会空引用。

**6. 写盘失败绝不影响玩法**

`Save()` 内部吞掉全部异常，只 `LogWarning`。日志是辅助功能，
不能因为磁盘满了或路径没权限就把一场对话搞崩。

### 验证方法

Play Mode 跑用户改过的 `AiTalkDemo`（`turns:21 topic:恋爱话题 bg:图书馆`），
聊 3 轮后提前结束，确认：

- 两份文件都生成，编码 UTF-8 带 BOM（Windows 记事本直接打开不乱码）
- `.md` 文件头的 ⚠ 正确报出 21 > 12
- 逐轮的表情 / 漫符 / 好感 / 三个选项 / 玩家选了哪个（→ 标记）都对
- 结果段的五个 flag 与 `VNFlags` 实际写入一致
- 开销段：3 轮 4.7s / 3338 token / $0.002099，平均每轮 $0.0007
- `.json` 顶层 23 字段、每轮 17 字段，`pickedIndex` 与 `tone` 可直接索引

顺带验证了输入 token 随轮数增长（923 → 943 → 973），
这是「API 无状态、每轮重发完整历史」的直接证据，写进了文档第二节。

### 后续可做（未做）

- **跨场记忆**（文档里标为最值得做的一项）：聊完让 AI 生成一句摘要写进独立 JSON，
  下次注入 `VNAiContext.memory`。字段已留好，只差存储层
- 用 `.json` 做批量分析的小工具（哪个 topic 效果好 / 玩家倾向分布 / 月度成本）
- 流式输出、道具联动、AI 当裁判、AI 出题、中途存档、TTS（见指南第十一节）

---

## 一〇八、AI 聊天候选回复条数可调（3~6）+ 内容边界加固（2026-08-14，分支 `agent/aitalk-option-count`）

### 需求 / 背景

用户要把候选回复从固定 3 条改成可切换（最初提的是 7 条含「沉默不语」，
沟通中改为**5 条、全部由 optionTones 决定、不要沉默档**），并且要一个勾选框
在 3 条 / 5 条之间切换。

动手前发现两个必须先解决的问题：

1. `VNAiPersonaDef.Validate()` 硬写了「optionTones 必须正好 3 条」，正是用户
   Console 里那条报错的来源
2. 选项面板是竖排的，`总高 = 条数×84 + (条数−1)×26`。3 条 = 304px 很舒服，
   **7 条 = 744px 会从屏幕上方压到对话框**，把立绘几乎全挡住

### 文件改动清单

**修改**

- `VNChoicePanel.cs`——新增 `ResolveMetrics()`：按条数等比压缩条高 / 间距 / 字号，
  总高上限 `MaxTotalHeight = 430f`
- `Script/VNAiPersonaDef.cs`——`optionTones` 放开到 3~6 条；新增
  `useExtendedOptions` 勾选框与 `ResolveTones(overrideCount)`；
  `Validate()` 改为查区间 + 查空值 + **查重名**；`boundaries` 默认文案加固
- `Script/VNAiConversation.cs`——构造函数收 `optionCount`；schema 的
  `minItems/maxItems`、提示词的条数与语气清单、解析时的补齐 / 截断、
  兜底轮的条数全部改为动态；`LanguageRule(optionCount)`
- `Script/VNAiTalkModule.cs`——剧本 `options:N` 覆盖；倾向 flag 只写本场实际用到的档
- `Editor/VNScenarioSchema.cs`、`HowToUse.md`、`AiTalkGuide.md`——补 `options:` 与新章节

### 技术决策与取舍

**1. 排版：等比压缩，而不是加一组「紧凑模式」序列化字段**

新参数一律从既有的 `buttonSize` / `buttonSpacing` 按比例算出来，**不新增 public 字段**。
理由是十二节记过的坑——手感参数做成序列化字段后，场景里躺着的旧值会盖掉代码改动。
`MaxTotalHeight = 430f` 写成 `const`，因为它是「不压到对话框」的几何结论
（选项区中心 y=+60、对话框上沿约 y=-290，430 的一半是 215，最低那条落在
y=-155，还留约 135px 余量），不是给人调的手感值。

**3 条及以下永远返回原值**，所以既有的所有 choice 演出一个像素都不变——
这是能放心改公共组件的前提。

**2. 「前 3 条是基础档」的约定**

`ResolveTones()` 永远返回 `optionTones` 的**前 N 条**。这样一套人格资产就能同时
支持两种玩法：资产里填满 5 条，日常场合关掉开关走三选一（节奏快），
关键场合勾上或写 `options:5`。代价是必须把最核心的三种走向放在列表最前面，
这一条写进了字段的 Header。

**3. optionTones 查重名**

重名会让「倾向_xxx」flag 互相覆盖，统计直接作废。用户当时六格全填了「随机」
（其中一条还带尾空格），新的查重立刻抓到——这条校验第一天就回本了。

**4. 内容边界必须列清单，不能只写抽象原则**

Play Mode 实测时 AI 自己抛出了**怀孕**剧情。原来的 boundaries 只写
「不编造剧本里没有的重大剧情（转学、告白结果、他人生死）」，举例覆盖不到。
改成正面约束 + 明确清单 + 兜底句：

```
**只聊日常**。绝对不要自己抛出会改变两人关系或人生走向的重大事件——
  包括但不限于：怀孕、告白与其结果、交往或分手、疾病、事故、死亡、
  转学、搬家、家庭变故、任何越界的身体接触。
  这类情节只能由剧本安排，你自己提出来会直接和主线冲突。
拿不准某个话题算不算重大事件时，就换个轻松的日常话题带过。
```

复测三轮全部停在日常话题（值日 / 倒垃圾 / 草莓牛奶）。
`VNAiPersonaDef` 的默认值也一并改了，以后新建人格直接带上这段。

**5. 一次成本归因错误（记下来防止重犯）**

5 条时实测延迟 1.5s → 6s、3 轮成本 $0.002 → $0.0132，第一反应是「条数变多的代价」。
但按 token 手算对不上（输入 2771 + 输出 541 应约 $0.0022），差额只能来自思考 token。
一查人格资产：`thinking` 被从 `Minimal` 改成了 `High`（约 1470 思考 token/轮）。

**真正的结论：候选回复 3 条加到 5 条，成本几乎没变；`thinking` 才是唯一能让
成本翻数倍的开关。** 这条连同对照表写进了指南第十节，并在排错表里加了
「成本突然涨好几倍 → 先查 thinking，不是查 optionTones」。

### 验证方法

- 编辑器自检跑 5 档：五档语气区分明显（温柔 / 玩笑 / 直球 / 反撩 / 敷衍），
  「反撩」确实触发了她特有的反应（「谁是小结衣啦！少恶心了，不过……」）
- Play Mode 截图确认 5 条排版：完整显示、最下一条距对话框有余量、立绘没被挡
- 顺带修掉一处措辞 bug：`LanguageRule()` 里写死的「台词与**三个**候选回复」，
  5 条时措辞不符，改为按实际条数生成

### 后续可做（未做）

- 6 条以上的排版没试过（压缩比会低于 0.58，字号会撞到 `MinFontSize` 下限）
- 「沉默不语」这类固定档位（本次按用户要求去掉了，将来若要加，
  它需要在提示词里单独教 AI 怎么反应，否则会被当成普通台词接）

---

## 一〇九、AI 聊天跨场记忆 + 日记本玩法（2026-08-14，分支 `agent/aitalk-memory-diary`）

### 需求 / 背景

用户要两件事：① 跨场记忆，让 AI 下次少重复已经聊过的内容，最多带 15 场；
② 把这些记忆做成新玩法——日记本，记录「我和她的相处经历」增强代入感。

四个前置决策（逐条确认）：

| 决策 | 结论 | 理由 |
|---|---|---|
| 存储语义 | **记忆进存档，日记走全局** | 两者语义本就不同，见下 |
| 摘要生成 | 聊完额外发一次请求 | 主 schema 每轮都在用，加字段等于每轮白算；ESC 提前退出时那条路还走不到 |
| 日记视角 | 主角写的日记 | 代入感最强，设定上也最自然 |
| 日记范围 | 先只收 AI 对话 | 范围可控，接口留好 |

### 文件改动清单

**新增**

- `Script/VNAiMemory.cs`——跨场记忆（存档态）：条目结构、容量裁剪、
  prompt 段组装、话题清单提取、存档快照存取
- `Script/VNAiDiary.cs`——日记本（全局 JSON，仿 VNCgUnlocks），上限 200 条
- `Script/VNAiDiaryPanel.cs`——日记本面板（D 键），程序化 UI、角色筛选、滚动
- `Script/VNAiTextNormalize.cs`——繁体字兜底转简体

**修改**

- `Script/VNAiConversation.cs`——`BuildSummaryRequest()` / `TryParseSummary()`；
  记忆与话题清单注入提示词；全部玩家可见文本过繁转简
- `Script/VNAiPersonaDef.cs`——`enableMemory` / `memoryCapacity` / `writeDiary`
- `Script/VNAiTalkModule.cs`——`SummarizeCo()` 收场总结，写记忆与日记
- `Script/VNSaveSystem.cs`——`VNSaveData.aiMemories`
- `Script/VNStage.cs`——快照存取两处接线（存档三处同步）
- `Script/VNScriptRunner.cs`——D 键 + `RequestDiary()`，按需创建面板
- `ui.zh/en/ja.txt`——日记本与「记下来」共 7 条

### 技术决策与取舍

**1. 记忆与日记的存储语义刻意相反**

| | 记忆 | 日记本 |
|---|---|---|
| 存哪 | `VNSaveData.aiMemories` | `persistentDataPath/vn_ai_diary.json` |
| 读旧档 | 跟着回退 | 不回退 |
| 类比 | VNFlags | CG 画廊 / 大头贴相册 |

对话记忆是**剧情状态**——读回第 3 章的档，她还记得第 5 章聊过的事就穿帮。
日记本是**玩家的收藏品**——玩家真实经历过的东西不该因为读档消失。
代价是会出现「日记里有第 5 章，但她不记得」，这是刻意的：
日记是玩家的记录，记忆是角色的记忆，本来就不是一回事。

**2. 「少重复」的主力是话题清单，不是摘要**

注入分两段：摘要给关系的连续性，**话题标签清单单独成段、措辞强硬**
（「这些之前聊过了，不要再当作新话题提起」＋给出「延续 vs 重问」的正反例）。
实测把话题揉进摘要里再说「别重复」效果差得多——让模型逐个避开一份明确清单，
比让它读散文自己判断有效得多。

验证：第一场话题 `人生意义/及时行乐/日常烦恼`，第二场开场变成
「喂，以后毕业你想干嘛呀？」——新话题，且与旧话题有延续而非重复。

**3. 收场总结必须放在 `Done()` 之前**

`Done()` 一调 Runner 就销毁模块，之后启动的协程会跟着死掉，请求发不出去。
所以 `SummarizeCo()` 排在 `FinishWith()` 前面，期间显示「把今天的事记下来…」。
失败一律静默跳过——少一条记忆而已，绝不能让玩家卡住。
总结请求固定 `thinking: Minimal` 不跟随人格，免得人格开了 High 之后
每场结束白花几千思考 token。

**4. 日记视角跑偏：根因是消息 role 结构，不是提示词措辞（本次最大的坑）**

第一版写出来的日记是**她的口吻**（「哼，才不承认呢」）。
在提示词里加「现在停止角色扮演」「你不再是她」「用男方口吻」「这是他的日记本」
统统无效，改了两轮措辞仍然是她的独白。

根因：总结请求把对话历史按 `role: user/model` 交替发过去，她的台词标着 `model`
——模型看到「自己说过这些话」，身份代入是**结构性**的，措辞压不住。

解法：把对话**拍平成一段纯文本**（「星野结衣：…／我：…」）放进单条 user 消息。
模型于是只是在读一份别人的对话记录，身份代入自然消失，一次就对。

> 记住这条：**模型身份认知不对时，先看消息的 role 结构，再改提示词措辞。**

顺带处理：合成的开场引导（「请你先开口说第一句话」）是给模型的指令而非玩家台词，
拍平时要跳过，所以 `VNAiConversation` 记了 `_openingText`。

**5. 繁体字改用代码兜底**

提示词已把简体约束挪到 system prompt 最后一行（权重最高），**三次抽查三次仍中招**
（「才、才沒有」「我还沒想好」「眞希望」）。模型对字级约束的遵守是概率性的。
而这是玩家直接看得见的东西，所以加确定性兜底 `VNAiTextNormalize`。

分工：提示词管「大部分时候写对」，兜底管「永远不出错」。
只覆盖高频字不追求完备（完整方案要 OpenCC 级词库）；命中时 Console 告警，
便于判断要不要加强提示词。繁→简方向安全（多对一不产生歧义），不做反方向。

### 验证方法

- **记忆**：Play Mode 连聊两场。第一场记下 `人生意义/及时行乐/日常烦恼`，
  第二场开场自动换成毕业规划的新话题
- **日记视角**：改用拍平文本后复测，输出「今天和结衣聊到了人生态度……
  这丫头总是一副无忧无虑的样子……我的心跳莫名漏跳了一拍」——干净的主角第一人称
- **日记本 UI**：截图确认三条按时间倒序、卡片排版、话题标签、好感变化、滚轮翻页
- **繁转简**：拿三次实际漏出的原句做回归，全部命中并修正；零命中时原样返回
- **存档**：`aiMemories` 带默认值，旧档反序列化得到空列表 = 「那时还没聊过」，语义正确

### 后续可做（未做）

- 日记本收剧本事件（拍照 / 羽毛球 / 任务），变成真正的「回忆录」——
  需要给各模块加埋点，本次按用户要求先只收 AI 对话
- 日记条目点开看对应的完整对话（`logFile` 字段已留）
- 记忆的「遗忘曲线」：越久远的摘要压缩得越狠，而不是一刀切裁掉
- 让 AI 自己判断哪些记忆重要（现在是无差别保留最近 N 场）

---

## 一一〇、情绪色调改为分层调色（mood 不再染 UI 与立绘）（2026-08-14，分支 `agent/mood-layered-grading`）

### 需求 / 背景

用户反馈：`mood Sunset` 一开，**整个画面连对话框和顶栏 HUD 一起变橙**，难看；
立绘也被烤成橙色，画面还有过曝发白的"怪怪的"感觉。

排查出三层原因：

1. **架构层（主因）**：`VNMoodGrading` 用 URP 全局 Volume 做调色，而场景是
   「单相机 + 单个 `Screen Space - Camera` 的 Canvas」——背景、立绘、对话框、
   HUD 对 URP 来说是同一张画面。后处理作用于整个 color target，
   **物理上无法只染一部分**，调参调不掉。
2. **参数层**：Sunset 把 `colorFilter(1.18,0.94,0.72)`、`temperature +30`、
   `gain(1.08,1,0.9)` 三条暖化通道叠乘，红通道乘到 1.27 直接冲破
   **Bloom 阈值 1.0** 泛白，蓝通道压到 0.65；配上本来就偏橙的黄昏背景图就是
   "橙上加橙"。这是过曝糊白的来源。
3. **叠加层**：`VNToneMatch` 换背景时按背景平均色给立绘乘 9% 染色，
   橙背景 → 立绘先橙一次，再被 mood 全屏橙一次。

### 方案选型

给用户列了三条路线，用户选 **A（调参）+ B（分层调色）**，
立绘保留 ~30% 染色，暗角/颗粒继续盖住全屏。

未采用 **C（相机架构拆分）** 的原因，两个硬伤：
- URP 的 Camera Stack **做不到**「Base 吃调色、Overlay 不吃」——整个 stack 共用
  一个 color target，后处理在最后一个相机之后统一执行一次。真能 100% 躲开
  后处理的只有 `Screen Space - Overlay` 模式的 Canvas。
- 而躲开后处理 = 同时躲开 Bloom。项目的核心约定是「发光 = HDR 颜色(>1) +
  Bloom」，UI 一旦 Overlay，对话框流光边框、名牌发光、选项扫光**全部变成死板
  纯色**。代价打在项目最看重的地方，收益却能被 B 用十分之一成本拿到。

### 顺带挖出的既有 bug：六个系统抢同一个 shader 参数

做 B 的过程中发现 `_Brightness` / `_Saturation` 被六处直接抢写，谁最后写谁赢：

| 抢写方 | 场景 |
|---|---|
| VNSpeakerHighlight | **每句台词**都改（非说话者压暗） |
| VNFakeDoF | 背景虚化压暗 |
| VNCharacterEmotes | 沮丧动作压暗 |
| VNEntranceAnimator | sink 退场压暗 |
| VNWeatherController | 天气亮度/饱和联动 |
| （本次新增）VNMoodGrading | 情绪色调 |

所以 mood 如果也直接写，**每说一句话立绘颜色就会跳回去**。
另外 `PrepareHidden()` 原本不重置亮度，sink 退场后复用立绘会残留 0.3 亮度
（既有 bug，本次一并修掉）。合并层因此不是可选项而是必需品。

### 文件改动

**新增**
- `VNGrade.cs` —— 调色值类型 `VNGrade`（滤镜/色相/饱和/亮度/对比度）+
  来源通道枚举 `VNGradeLayer`（Mood / Weather / Focus / Emote / Manual）。
  合并规则：**滤镜相乘、色相相加、其余相乘**；`Scaled(k)` 做强度缩放，
  「背景全染、立绘轻染、UI 不染」就是靠它。纯静态数学，无 MonoBehaviour 依赖。

**修改**
- `VNImageEffect.shader` —— Color Grading 区段加 `_ColorFilter`（RGB 乘）与
  `_Contrast`（绕 0.5 中灰缩放）。对比度会算出负值，**必须 `max(0,…)`**
  钳掉，否则 uGUI 混合会出脏边。
- `VNImageEffectController.cs` —— 加分层调色合并层：`SetGrade(layer, grade, dur)`
  / `DOGradeField` / `ClearGrade` / `GetGrade`。每个通道存自己的目标值，
  合并后统一补间。老 API（`SetHSV` / `DOBrightness` / `DOSaturation`）保留，
  内部改走 `Manual` 通道 —— **没改到的调用点自动降级到独立通道，不会冲掉 mood**。
- `VNMoodGrading.cs` —— 重写。Volume 只留 `FilmGrain + Vignette`
  （不改色相，压四角反而有电影感，恐怖/回忆的氛围主要靠它俩）；
  色彩全部移交分层调色，按 `backgroundStrength(1.0)` /
  `midStrength(0.8)` / `characterStrength(0.3)` 分配。
  加 `RegisterCharacter` / `UnregisterCharacter` / `SetCharacterTargets` /
  `RegisterBackground` / `ApplyGradeToAll`。A/B 双 Volume 交叉过渡结构保留。
- `VNSpeakerHighlight.cs` / `VNFakeDoF.cs` → `Focus` 通道
- `VNCharacterEmotes.cs` / `VNEntranceAnimator.cs` → `Emote` 通道
  （并在 `PrepareHidden()` 清 Emote + Focus，**不清 Mood** —— 情绪色调是全局的，
  角色一出场就该带着当前情绪的颜色）
- `VNWeatherController.cs` → `Weather` 通道
- `VNStage.cs` —— `AutoWire()` 里 `RegisterBackground(backgroundFx)`（老场景自愈）；
  `RefreshRegistries()` 里 `mood.SetCharacterTargets(...)`，
  **中途出场的角色也会立刻带上当前情绪的颜色**。
- `VNToneMatch.cs` —— 默认 `strength` 0.09 → 0.05（mood 现在也给立绘上色，
  两者叠加容易过头）。**场景实例值需单独改**，本次已改 `VNScriptDemo` 场景。
- `VNEffectsDemoSetup.cs` —— 演示场景没有 VNStage，直接接线 mood 的分层目标。

### 调色预设换算表（100% 强度下的值）

从原 Volume 参数换算并**整体收敛**，全部给 Bloom 留足余量：

| mood | 滤镜 | 饱和 | 亮度 | 对比 | 全屏（颗粒/暗角）|
|---|---|---|---|---|---|
| Morning | (0.95, 1.00, 1.07) | 1.05 | 1.08 | 1.02 | — |
| Sunset | (1.09, 1.00, 0.88) | 1.06 | 1.00 | 1.05 | — |
| Night | (0.80, 0.87, 1.12) | 0.72 | 0.62 | 1.06 | — |
| Memory | (1.11, 1.02, 0.87) | 0.62 | 1.06 | 0.85 | 0.28 / 0.34 |
| Tension | (0.92, 1.03, 0.93) | 0.88 | 0.92 | 1.28 | 0.12 / 0.28 |
| Horror | (0.88, 0.91, 0.97) | 0.40 | 0.70 | 1.20 | 0.40 / 0.48 |
| Dream | (1.05, 0.95, 1.11) | 0.86 | 1.16 | 0.82 | — / 0.22 |

Sunset 红通道从 1.27 降到 1.09，立绘再乘 0.3 强度只剩 1.027 —— 不再撞 Bloom。

### 技术决策与取舍

- **表现力降一档**：per-image shader 没有 `LiftGammaGain`，做不出「高光暖、
  阴影冷」的分区调色。用单条滤镜 + 对比度近似，够用。要补的话给 shader 再加
  两个参数即可。
- **不加剧本语法**：立绘强度做成 Inspector 参数（`characterStrength`）而不是
  `mood Sunset chara:0.5`，保持剧本层不变。以后要逐场切换再加。
- **存档零改动**：分层调色完全从 `data.mood` 派生，`RestoreSnapshot` 调
  `SetMood` 就会重新套用；新出场立绘经 `RegisterCharacter` 自动补色。
- **立绘不做 0% 染色**：`VNToneMatch` 当初存在就是为了消除「立绘像贴纸」的
  违和感，黄昏场景里立绘一点暖色不沾会明显浮在背景上。

### 验证方法

1. 剧本跑到 `mood Sunset` 处：背景转暖，**对话框、名牌、顶栏 HUD、快捷条
   保持原色**；立绘只有很淡的暖调。
2. 连说几句话：立绘在说话/旁听之间切换明暗时，**情绪色不该跳变**
   （合并层生效的关键验证点）。
3. `mood Sunset` 期间切 `weather Rain`：天气压暗与情绪色应叠加，而不是互相冲掉。
4. `mood Horror` / `Memory`：暗角与颗粒仍盖住全屏（本次刻意保留）。
5. 存档 → 读档：情绪色应完整恢复，含中途出场的角色。

### 已知遗留

- `midTargets`（中景）目前为空 —— GodRays 用的是 `VN/Additive` 材质，
  不走 `VN/ImageEffect`，接不进分层调色。要染中景得先给它换材质。
- 演示场景 `VNEffectsDemo` 需重跑 Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene
  才会带上新接线。

---

## 一一一、剧本编辑器打字搜索：右键搜命令 + Ctrl+E 命令面板 + 参数下拉可搜（2026-08-14，分支 `agent/scenario-command-search`）

### 需求 / 背景

加一行命令只能从行首下拉一层层点（Scene → bg），命令有 41 条、参数格候选更多，
点起来慢且要记得某条命令归在哪个分类下。要的是「保留现在这套分类菜单，
另外多一条打字就出候选的路」，像 VSCode 写函数那样。

### 为什么不用 Unity 原生 AdvancedDropdown

它自带搜索框、层级、键盘导航，看起来正好。放弃的原因有三条：

1. 它是**替换**关系——用了它，现在这套带分类配色的层级菜单就没了，与「两条路并存」的需求冲突。
2. 候选行只有一行文字 + 图标，**放不下 hint 副标题**（`liquid` 那种九个参数的命令，
   光看名字选不出来，hint 才是关键信息）。
3. 搜索匹配逻辑在 internal 的 `AdvancedDropdownDataSource` 里，改不了。

而 Ctrl+E 命令面板无论如何都要自写一个带输入框的窗口，与其「原生 + 自写」两套外观两套键位，
不如共用一套匹配引擎。**匹配刻意只做子串包含**（大小写无关，中英都能打），
不做模糊子序列/拼音——够用且行为可预期。

### 文件改动清单

**新增**

- `Editor/VNCommandSearch.cs` —— 四个共用件：
  - `VNSearchItem`：一条候选（`value` = 真正写进剧本的值，title/subtitle/accent 只管显示，
    `searchExtra` 放分类名等额外可搜文本）
  - `VNSearchListView`：搜索框 + 候选列表。子串匹配、↑↓/PageUp/PageDown、Enter/Esc/Tab、
    命中片段染色加粗、选中项自动滚进视野
  - `VNSearchPopup`：通用搜索弹窗（换命令 / 加行 / 参数格三处共用）
  - `VNCommandPalette`：Ctrl+E 向导面板（选命令 → 逐个问位置参数 → 可选参数菜单循环 → 插行）

**修改** `Editor/VNScenarioEditorWindow.cs`

- 行首命令按钮**右键** = 打字换命令（`ConsumeRightClick` + `ShowRowTypeSearch`），
  左键那套分类菜单原样保留。
- 底部 `[+]` 从 `GenericMenu` 换成搜索弹窗（`ShowAddSearch`），
  注释 `#` 与空行也变成可搜条目；原 `ShowAddMenu` 删除。
- `PopupString` 的 `EditorGUI.Popup` 换成「按钮 + 搜索弹窗」，
  一处改动覆盖全部枚举/bgm/se/voice/event/quest/weather/label/flag 参数格。
- `Ctrl+E` 走 ShortcutManager（`VN/Scenario Editor/Command Palette`）。
- 新增 `ApplyPendingNewRow()`：搜索弹窗/面板攒好的行留到下一个 Layout 事件再插。
- 抽出 `DisplayOptionsFor()`（原本内联在 `DrawParamField` 里的中英对照 if 链），
  给参数格与命令面板共用。
- 底部提示条补两行新键位说明。

### 技术决策与取舍

- **`PopupString` 的同步契约不能破**，这是本次最大的坑。它被三处用着，
  而其中两处的值**不在 `VNRow.values` 里**：camseq 路径点（值在 `camLines` 文本）、
  choice 选项行（值在 `VNChoiceOptionRow` 字段）。若照 `SpritePopup` 那样让弹窗回调直写
  `values`，这两处会「选了不生效，还顺手往文档里塞个野参数」——正是 vn-editor-extend
  铁律警告的症状。做法：**弹窗回调只把选中值放进 `_popupResults[key]`，
  下一帧由 `PopupString` 同步 return 给调用方**，调用方仍然自己写回，签名与语义零变化。
- **键盘必须抢在 `EditorGUI.TextField` 之前处理**：IMGUI 里文本框会把 ↑↓ 拿去移光标、
  把 Enter 当「结束编辑」吃掉，得先 `Event.current.Use()` 才轮得到候选列表。
- **右键要自己收**：`GUI.Button` 只响应左键。但 `ConsumeRightClick` 之后**按钮照画不误**——
  IMGUI 控件 id 按调用顺序分配，少画一个控件会让同一帧后面的控件全部错位。
  `ReorderableList` 的选中/拖动也只认左键，`Use()` 掉右键不会抢它的事件。
- **改行数一律留到 Layout 事件**：弹窗回调跑在另一个窗口的 GUI 里，
  直接 `_doc.rows.Insert` 会让 `ReorderableList` 当帧的布局对不上，
  所以复用了 Enter 插行那套 pending 机制。
- **不做行内内联补全**（按钮原地变输入框、下方浮出候选）：候选浮层要盖在下面几行上，
  但行画在 `ReorderableList` + `ScrollView` 里会被裁掉，得推迟到 `EndScrollView` 之后手动补画；
  加上焦点转移与方向键冲突，三个坑叠一起性价比最低。弹窗方案焦点天然在搜索框里，手感几乎一样。
- **命令面板的可选参数用「菜单循环」而不是逐个问**：`show` 有 5 个 kwarg、`liquid` 有 9 个，
  逐个 Tab 过去很烦。改成必填问完 → 列出所有 `key:` 供打字筛 → 选一个给个值 → 回菜单，
  空查询直接 Enter 结束。只填要的那几个。
- **say 行的说话者写 `VNRow.speaker` 专用字段**，不进 `values`（编辑器铁律）。
  面板里对它用了一个合成的 `VNParamDef`（id = `say.speaker`），赋值时特判分流。
- **快捷键选 Ctrl+E 不选 Ctrl+K**：Ctrl+K 是 Unity Search 的默认绑定，
  虽然窗口作用域优先，但 Shortcuts 面板里会标冲突。
- **命令候选表从 `VNScenarioSchema.Commands` 现场生成**，`VNScenarioSchema.cs` 一行没改——
  以后加新命令自动出现在搜索里，不用回来登记第二遍。

### 验证方法

1. `Tools → VN Effects → 剧本编辑器 Scenario Editor` 打开剧本。
2. **右键**点任意行首的彩色命令按钮 → 弹出搜索窗；打 `bg` / `背景` / `场景` 都能命中 `bg`
   （分类名也参与匹配）；↑↓ 选、Enter 换命令、Esc 关。
3. 底部 `[+]` → 同一套搜索窗，能搜到 `# 注释` 和 `（空行）`。
4. 点任意参数格下拉（如 `show` 的 `with:`）→ 变成可搜列表，中英对照名照常显示；
   打一个候选里没有的值 → 顶部出现「使用自定义值」，Enter 直接写进去；
   右上角 `custom…` 仍可切成常驻文本框。
5. **camseq 路径点行的角色下拉、choice 选项行的 flag/jump 下拉必须照常生效**
   （这两处是同步契约的回归点，选完应立刻写回、且不产生多余参数）。
6. `Ctrl+E` → 面板居中弹出；打 `show` → Enter → 问角色 → Enter → 进可选参数菜单 →
   选 `at:` → 选 `left` → 回菜单 → 空查询 Enter → 在选中行下方插入 `show <角色> at:left`；
   Shift+Enter 则插在上方，Tab 跳过当前参数，Esc 全程可取消。
7. 编译验证：`assets-refresh` 后 console 无 error（本次实测只剩既存的 CS0618 警告）。

### 修复记录

- **搜索框清空失效**（向导每换一步都要清 query，结果清不掉）：
  IMGUI 的文本框只要还持有 `GUIUtility.keyboardControl`，就用它内部 `TextEditor`
  的缓冲，程序里把源字符串改成 `""` **不生效**——下一帧那个控件把旧文本原样
  return 回来，等于又写回 `query`。症状是 Ctrl+E 面板选完 `show` 进到「问角色」
  那步，搜索框里还留着 `show`，候选被错误过滤成空。
  修法：`VNSearchListView.Reset()` 里先 `GUIUtility.keyboardControl = 0`
  让文本框重新从源字符串同步，再靠 `_focusPending` 在下一个 Repaint 抢回焦点。
  **任何「程序化清空 IMGUI 文本框」的地方都要这么写。**

### 已知遗留

- 匹配是子串包含，打 `sw` 命不中 `show`（要模糊子序列匹配得换打分排序），
  也没有拼音与「最近使用置顶」。当前是刻意选择，要加的话只改 `VNSearchItem.Matches`
  与 `VNSearchListView.Filter` 两处。
- `Go to label` 下拉仍是 `GenericMenu`，未接入搜索。
- 角色/背景/CG 参数格走的是另一条 `SpritePopup`（本来就有搜索框），本次未动。

---

## 一一二、AI 试聊台：不进 Play Mode 调人格与提示词（2026-08-14，分支 `agent/ai-talk-studio`）

### 需求 / 背景

调 `aitalk` 的人格与提示词，此前的循环是：改 `.asset` 字段 → 菜单
`Test Persona Talk (3 turns)` → Console 吐一大坨文本 → 肉眼比对。六个具体瓶颈：

1. 改提示词必须动资产（Git 噪声 diff、容易忘了改回来）
2. 玩家回复是**随机挑**的，想验证「直球会不会让她乱套」根本试不了
3. 看不到 system prompt 的变化，改完要重跑一整场才知道拼对没
4. 无法 A/B 对比
5. 无法回归验证
6. 看不到演出效果

本次做 MVP：解决 1/2/3，外加**记忆可编辑**（此前完全无法验证记忆的影响）。
A/B 与批量回归明确留到以后——但每场试聊都落结构化 json，将来做对比时数据已经在了。

### 文件改动

**新增（Editor，6 个）**

| 文件 | 职责 |
|---|---|
| `VNAiStudioWindow.cs` | 主窗口：三栏布局、会话动作、上下文组装、域重载存活 |
| `VNAiStudioDraft.cs` | 草稿层：临时 SO 副本、脏标记、字段级 diff、写回/还原 |
| `VNAiStudioSession.cs` | 会话驱动：发请求、解析、轮次记录、重跑/分岔/重建、收场总结 |
| `VNAiStudioMemory.cs` | 记忆预设存储（项目根 `AiTalkStudio/Memories/*.json`）+ 两个导入器 |
| `VNAiStudioLog.cs` | 把试聊会话按游戏内同格式导出到 `AiTalkLogs/Editor/` |
| `VNAiEditorCoroutine.cs` | 编辑器协程泵（从 `VNAiConnectionTester` 提取，两处共用） |

**修改（运行时，3 个，全是纯增量）**

| 文件 | 改了什么 |
|---|---|
| `VNAiConversation.cs` | 加 `History` 只读、`OpeningText`、`TruncateToTurn(int)` |
| `VNAiTalkLog.cs` | `Save(subFolder)` 支持子目录；`Begin` 加不带 `VNEventContext` 的重载 |
| `VNAiTalkModule.cs` | `BuildAffectionText` 提成 `public static(statName, value)`，实例方法转调 |

`VNAiConnectionTester.cs` 的协程泵改为调用提取出来的公共类；`.gitignore` 加 `/AiTalkStudio/`。

### 技术决策与取舍

**① 草稿层用「临时 ScriptableObject 副本」——本次最关键的一个选择**

```
Instantiate(persona) → 内存副本（HideFlags.DontSave）
                     → SerializedObject 迭代画 = 零 UI 代码就有全部字段
                     → new VNAiConversation(draft) = 逻辑层一行不用改
```

三个好处：将来给 `VNAiPersonaDef` 加字段窗口自动就有（不会两边脱节）；
`VNAiConversation` 只认类型不认来源所以直接能吃草稿；写回是逐属性
`CopyFromSerializedProperty`，自带 Undo。

**写回刻意不用 `EditorUtility.CopySerialized`**：它连 `m_Name` 一起复制，
副本名字是「xxx(Clone)」，照抄回去资产内部名就和文件名对不上了。
逐属性复制天然跳过 `m_Name`（它不是 visible property），顺带也跳过 `m_Script`。

**② 记忆预设完全独立于 `VNAiMemory`**

运行时那份是**存档态**——跟着存档走、被读档覆盖、域重载清空。编辑器往里写
等于凭空制造「读旧档她却记得未来」的幽灵状态。所以试聊台自己一套文件、
自己的生命周期，只在组装 prompt 那一刻把条目喂进 `VNAiContext`。
代价是 `BuildContext` / `TopicsOf` 两段格式要和 `VNAiMemory` 手动保持同步。

**③ 从存档导入记忆刻意不走 `VNSaveSystem.Load()`**

那个方法会 `VNFlags.Clear()` 再灌入存档里的全部 flag。在编辑器里点一下「导入」
就把工程的 flag 状态冲掉，下次进 Play Mode 行为莫名其妙。改为自己读 JSON，纯读无副作用。

**④ 从日志导入记忆要发一次总结请求（约 $0.001），不做「免费骨架导入」**

注入 prompt 时真正被用到的是 `summary` / `topics` / `facts` 三样，
而日志里一样都没有（总结不写进日志）。导入一条三样全空的条目等于什么都没导入，
反而让人以为记忆生效了。做法是拿日志里的 `playerSaid` / `reply` 重建一次会话历史
（不发请求，`BuildRequest` 只是组装），再走现成的 `BuildSummaryRequest`。

**⑤ 域重载后靠轮次记录重建会话**

`VNAiConversation._history` 是 private List 序列化不了，但轮次记录
（`playerSaid` + `reply`）序列化得了，而这两样**足以重建历史**——
依次调 `BuildRequest(playerSaid)` + `RecordReply(reply)` 即可，
顺序与真实聊天完全一致。所以重载后不用重聊。
**正在飞的那个请求救不回来**，会显示「已中断」，重跑一轮即可。

**⑥ 注入记忆 / 写记忆做成两个独立开关**

前者答「记忆对她有什么影响」——勾掉再跑一遍就能直接对比；
后者答「这场要不要沉淀下来」。运行时那边看人格的 `enableMemory`，
窗口这边看自己的开关（就是为了能一键对比）；两者不一致时窗口会提示。

**⑦ 成本可见性**

试聊台天生鼓励反复重跑，所以顶栏常驻累计 `$`，超过 $0.05 变红；
草稿把 `thinking` 调离 `Minimal` 时顶栏直接标红警告——按实测那是唯一一个
能让成本翻 6 倍的开关。

### 验证

纯逻辑部分用 `script-execute` 跑过（不进 Play Mode、不发网络请求）：

- 三轮模拟后历史为 `U:开场 | M1 | U:玩家A | M2 | U:玩家B | M3`，`TurnCount=3`、`picks=2`
- `TruncateToTurn(1)` → 剩 `U:开场 | M1`，`TurnCount=1`、`picks=0`
- `TruncateToTurn(0)` → 剩 `U:开场`，`TurnCount=0`
- 回退后重发开场：**历史首条仍是 user**（否则 Gemini 直接 400）
- 记忆预设存取往返正常；注入后 prompt 里【你还记得的事】【避免重复】与
  facts 内容都在
- 窗口实际打开无 OnGUI 异常

### 已知遗留

- **A/B 对比没做**（本次范围外）。每场试聊已落 `AiTalkLogs/Editor/*.json`，
  将来做并排 diff 时数据现成。
- **没有演出预览**：立绘表情、漫符、打字机都看不到，纯文本。表情穿帮只能实际跑剧本时发现。
- 记忆条目的 `topics` / `facts` 用「、」分隔的单行文本框编辑，条目多了不好使。
- `VNAiStudioMemory.BuildContext` 与 `VNAiMemory.BuildContext` 是两份实现，
  改一边要记得改另一边（不能复用的原因见决策②）。

---

## 一一三、AI 成本核算修正：总结请求漏记 + 单价写死 + 累计报表（2026-08-14，分支 `agent/ai-cost-accounting`）

### 需求 / 背景

用户发现「总结对话并写记忆」那一次请求没有成本统计，算账时对不上。查下来实际有**三处漏记 + 两个隐患**：

| # | 问题 | 后果 |
|---|---|---|
| 1 | 游戏内 `SummarizeCo` 拿到 `res` 后**完全没碰 `_log`** | 每场日志少算一整次请求（约 $0.001，占一场 6~15%） |
| 2 | 试聊台 `EndSession` 先导出日志、后发总结请求 | 顶栏统计是对的，但日志里同样缺 |
| 3 | 试聊台「从日志导入记忆」的请求 | 成本无处可见 |
| 4 | **单价写死 Flash Lite 的 0.30/2.50** | 人格 `model` 一换成 flash/pro，全部金额静默偏低（pro 差 6.9 倍） |
| 5 | `totalOutputTokens` 含思考、逐轮那列不含 | 手工核对必然对不上，看着像 bug |

实测确认问题 1 的规模：现有 30 份日志的 `summaryRequests` **全是 0**，
即历史上每一场都漏了一次总结请求的钱。

### 文件改动

**新增**

| 文件 | 职责 |
|---|---|
| `Script/VNAiPricing.cs` | `VNAiPricingDef` 单价表资产（Create → VN → AI Pricing）+ `VNAiPricing` 查表静态类 |
| `Editor/VNAiCostReport.cs` | 花费累计报表窗口（Tools → VN Effects → AI → 花费报表 Cost Report） |

**修改**

| 文件 | 改了什么 |
|---|---|
| `VNAiClient.cs` | `VNAiResult` 加 `model` 字段并在 `Send` 回填；`EstimatedCostUsd` 改走 `VNAiPricing` |
| `VNAiTalkLog.cs` | `Session` 加 `summary*` 六个字段；新增 `RecordSummary(res)`；开销段拆出「对话 N 轮 / 收场总结」两行，平均每轮改为不含总结；单价说明改为动态、并补一句解释思考 token 口径 |
| `VNAiTalkModule.cs` | `SummarizeCo` 调 `_log.RecordSummary(res)` |
| `VNGameConfig.cs` | 加 `aiPricing` 引用 |
| `VNAiStudioSession.cs` | 暴露 `LastSummaryResult` |
| `VNAiStudioLog.cs` | `Export` 加 `summaryRes` 参数 |
| `VNAiStudioWindow.cs` | 收场改为**总结回来再写日志**；抽出 `ExportLog()` |
| `VNAiStudioMemory.cs` | 导入日志时把那次请求的开销打进 Console |

### 技术决策与取舍

**① 单价做成资产而不是常量**

价格属于配置（会随供应商调价变动），不该躺在代码里。没建资产时用内置默认表
（flash-lite / flash / pro），**零配置也能用**——同 `VNWeatherDef` 的惯例。

**查表按「key 最长优先」**：`gemini-3.5-flash-lite` 同时含 `flash` 和 `flash-lite`，
不按长度排序就会被贵一档的 `flash` 抢走。这条写进了排序注释，改动时别删。

**认不出的模型取表里最贵的一档**（并标注「单价存疑」）。方向是有讲究的：
低估会让人以为「才这么点钱」然后放心用下去，高估最多让人多留意一眼。
——第一版这里写的是「取最便宜」，而实现依赖排序副作用实际取到了最贵的，
注释与代码不符，冒烟测试时发现并改成显式取最贵。

**② `RecordSummary` 放在成功判断之前**

失败的那次请求耗时是真花掉的，被拦或超时前产生的 token 也可能已计费。
时序上安全：`SummarizeCo` 在 `FinishWith` 之前跑完，`_log.Save()` 还没发生，
所以游戏内**不用调整任何顺序**，加一行就够。

**③ 总结开销并入总计 + 单列一行**

并入是必须的（否则总数就是错的）；单列是因为它按**场**发生而不是按轮，
摊进「平均每轮」会让那个数字失真，所以平均每轮明确改成「不含总结」。

**④ 试聊台改为总结回来才写日志**

代价是点完「结束并总结」要多等 1~2 秒才看到日志路径（不写记忆时仍立即写）。
换来的是日志一次写对，不用同一个文件写两遍。

**⑤ 报表默认「按当前单价重算」**

日志里同时存了 token 数与模型名，所以能拿当前单价表重算，
**修正历史上按写死单价算出的错误金额**。关掉则显示存储原值
（想复现「当时以为花了多少」时才需要）。

### 验证

- 单价查表：`flash-lite` 命中 0.3/2.5、`flash` 命中 0.6/3.5（**长 key 优先生效**，
  lite 没被 flash 抢走）、`pro` 2.5/15、未知模型标记 `found=false` 并取最贵档
- 同样 1000+200 tok：lite $0.000800 vs pro $0.005500（6.9 倍）——
  这正是写死单价时被吃掉的差异
- thinking 1470 tok 让单轮从 $0.000800 变成 $0.004475（5.6 倍），与文档实测的
  「贵约 6 倍」吻合
- 造两轮 + 一次总结写日志再回读：`total = 对话 + 总结` 精确相符，
  `summaryRequests=1`，Markdown 开销段拆分渲染正确
- 扫现有 30 份真实日志：183 轮、343.1k token、$0.4743；
  存储金额与重算金额**差异 $0**（全是 flash-lite，验证重算逻辑与旧逻辑等价）；
  `summaryRequests` 全为 0，证实历史每场都漏记
- 报表窗口打开无异常

### 已知遗留

- **历史日志的总结开销补不回来**：那几次请求的 token 数当时就没记下来，
  报表里的历史金额仍然偏低约 6%（30 场约 $0.03）。新产生的日志才是准的。
- 单价表是**每百万 token 的平铺价**，不支持分档计价（超过 N token 后涨价）
  与缓存命中折扣。当前用量下不值得做。
- 报表按会话聚合，看不到「哪一轮特别贵」——那个信息在单份日志的逐轮表里。

---

## 一一四、AI 供应商可切换：接入 DeepSeek V4（2026-08-17，分支 `agent/ai-deepseek`）

### 为什么做

只接了 Gemini 一家，换模型要改代码，而且没法对比。DeepSeek V4 Flash 的价格
（非高峰 $0.22 / $0.66 每百万）比 Gemini 3.7 Flash（$0.75 / $3.75）**输出便宜 5.7 倍**，
自由聊天这种「每轮都重发整段历史」的用法差价很实在。
需求是**两家都能用、随时切、切了成本还算得准**。

### 做了什么

**新增供应商抽象层 `VNAiProvider.cs`**

- `VNAiProvider`（Gemini / DeepSeek）+ `VNAiProviderChoice`（多一个「跟随全局」，
  **默认值 0 就是跟随**，所以存量人格资产一个字都不用改）
- `VNAiProviders` 集中登记各家的：默认模型、key 的环境变量与文件名、
  **能力差异**（`SupportsResponseSchema` / `SupportsSafetySettings`）
- 上层判断能力一律问它，而不是到处写 `if (provider == Gemini)`

**三层切换入口**（越下越优先）

| 层 | 在哪 |
|---|---|
| 全局默认 | `VNGameConfig.aiProvider` / `aiModel`（**默认已设为 DeepSeek**）|
| 人格资产 | `VNAiPersonaDef.provider` / `model`，默认「跟随全局」|
| 试聊台 | 工具栏两个格子，改的是**草稿**，用于 A/B 对比 |

**`VNAiClient` 拆三份，但「唯一碰 HTTP 的文件」这条不变**

传输、重试、错误分类仍全在 `VNAiClient.Send`；各家只差「拼请求体」和「解响应」，
拆进 `VNAiClientGemini` / `VNAiClientDeepSeek` 两个**纯静态、不碰网络**的类。
加第三家就是再加一个文件 + `VNAiProviders` 里加一项。

**`VNAiKey` 改成按供应商各一套**：`DEEPSEEK_API_KEY` / `DeepSeekAiApiKey.txt` 与
Gemini 那套并存，**分开缓存**——只配了一家时另一家要能正确报「没找到 key」，
不能因为缓存了一个空值就把两家都判死。

**`VNAiPricing` 支持三个价 + 一个时段倍率**：未命中输入 / 命中缓存输入 / 输出，
再乘高峰倍率。DeepSeek 高峰（UTC 01–04、06–10）翻倍，时段列表也放进资产。

### 三个关键决策

**1. DeepSeek 没有硬 schema —— 约束从「硬」降级成「软」+ 兜底**

Gemini 的 `responseSchema` 里 `emotion` 是 enum、`options` 有 minItems/maxItems，
是**服务端强制**的；DeepSeek 只有 `response_format:{"type":"json_object"}`，
不认 json_schema。做法：

- `VNAiConversation.BuildJsonFormatPrompt()` 把 schema 翻译成一段「输出格式」——
  键名、类型、**一个把 tone 全填好的示例 JSON**（光用文字描述实测会漏 tone），
  插在【绝对边界】之前（靠后＝权重高）
- 真正的保险仍是原有的 `TryParseTurn`：表情越界降级、选项不足补齐、好感 Clamp。
  以前那套「永远不信任模型输出」的代码是双保险，现在成了**唯一**的那道保险

结论写进指南第十七节：**换到 DeepSeek 之后，演出偶尔打折是设计内的**，
排错先看试聊台右栏的「原始 JSON」。

**2. 全局默认写成 DeepSeek，靠 Unity 的反序列化行为生效**

`VNGameConfig` 是已存在的资产，YAML 里没有新字段。Unity 反序列化时会先跑字段初始化器
再覆盖已有字段，所以 `public VNAiProvider aiProvider = VNAiProvider.DeepSeek;`
对存量资产也生效——不用手点 Inspector 就已经是 DeepSeek。

**3. 缓存缓存缓存：`VNGameConfig.ClearCache()` 里补上两个 Invalidate**

`VNAiProviders`（默认供应商）和 `VNAiPricing`（单价表）都从 config 读并缓存。
不一起清的话，在 Inspector 里把供应商改成 DeepSeek 之后还会继续发给 Gemini，
直到下次域重载。**「改了没反应」是这类缓存最典型也最难查的症状**，所以直接挂在
config 的清缓存入口上。

### 踩到的坑

- **`VNAiClient.DefaultModel` 从 const 变成属性**（要按当前供应商算），
  于是 `VNAiPersonaDef` 上 `[Header("模型名（留空 = " + VNAiClient.DefaultModel + "）")]`
  这种**特性里的编译期常量拼接**必须一起改掉，否则编不过。特性参数只能是编译期常量——
  凡是想把某个值「升级成可配置」，先搜一遍它有没有被写进 Attribute。
- **单价表的 key 撞车**：`deepseek-v4-flash` 里含 `flash`，会被 Gemini 的
  flash 档（$0.60/$3.50）抢走。原有的「按 key 最长优先」排序刚好挡住了这一枪——
  当初为 `flash-lite` 写的那条规则，这次白捡。
- **第 2 轮起「回复正文为空」——不是偶发，是必现**（上线后当天实测到，已修）：
  `json_object` 模式下，历史里 assistant 的消息是纯文本时，模型会照着
  「我上一条说的是纯文本」继续，而 JSON 模式又只准它出 JSON，于是退化成
  **吐一串空白字符**（`finish_reason=stop`，content = 20 个空格）。
  第 1 轮没有 assistant 历史所以永远正常，**第 2 轮起必挂**，看起来却像随机失败。
  用 A/B/C/D 四组对照请求钉死：纯文本历史 2/2 失败、完整 JSON 历史 2/2 成功、
  **只包 `{"reply":"…"}` 3/3 成功**，且与 `thinking` 开关无关。
  修法取最省 token 的那个（`VNAiConversation.AppendHistory`，每条历史多约 12 token），
  `_history` 仍只存纯台词，总结拍平与试聊台重建历史继续共用同一份数据。
  **教训**：换供应商时「历史消息的格式」和「请求参数」一样是契约的一部分——
  第一轮能跑通不代表接对了，多轮才是真正的验收点。
- **思考 token 别算两遍**：DeepSeek 的 `completion_tokens` **已经含**
  `reasoning_tokens`，而 Gemini 的 `candidatesTokenCount` 不含 `thoughtsTokenCount`。
  解析时先把 reasoning 从 completion 里减掉，上层「输出+思考」的公式才两家通用。

### 验证

- 用 Unity 的 Roslyn + Bee 的 rsp 离线编译 `Assembly-CSharp` 与
  `Assembly-CSharp-Editor`，两个都 exit 0、无新增警告
- 自检菜单拆成 `Test Connection · Gemini` / `· DeepSeek`，`Show Key Status`
  一次列出两家 key 找没找到

### 改了哪些文件

```
新增  Script/VNAiProvider.cs           供应商枚举 + 能力/默认值登记
新增  Script/VNAiClientGemini.cs       Gemini 拼包解包（从 VNAiClient 搬出）
新增  Script/VNAiClientDeepSeek.cs     DeepSeek 拼包解包
改    Script/VNAiClient.cs             只剩传输/重试/错误分类 + 按家分派
改    Script/VNAiKey.cs                按供应商各一套 key，分开缓存
改    Script/VNAiPricing.cs            缓存命中价 + 高峰倍率 + DeepSeek 两档
改    Script/VNAiPersonaDef.cs         provider 字段 + 模型/供应商不匹配的自检
改    Script/VNAiConversation.cs       请求带 provider；没有硬 schema 的家补格式提示词
改    Script/VNGameConfig.cs           全局默认供应商/模型；ClearCache 连带清 AI 缓存
改    Script/VNAiTalkLog.cs            日志记供应商与缓存命中数
改    Editor/VNAiConnectionTester.cs   分家自检 + 两家 key 状态
改    Editor/VNAiStudioWindow.cs       工具栏供应商/模型下拉（A/B 对比）
改    Editor/VNAiStudioDraft.cs        NotifyExternalEdit（绕过 SerializedObject 改草稿后刷新 diff）
改    Editor/VNAiCostReport.cs         重算带缓存命中价与当时的高峰判定
改    Editor/VNAiTalkInstaller.cs      按人格实际用的那家查 key
文档  AiTalkGuide.md 第十七节、CLAUDE.md 组件表
```

---

## 一一五、无框渐变对话框皮肤：白渐变 / 粉渐变 / 黑渐变（2026-08-28，分支 `agent/ai-deepseek`）

### 需求

参考商业 Galgame 的「无框式」对话表现：**没有面板、没有边框、没有圆角**，
底只有一条从屏幕底部向上淡出的整屏渐变带，台词居中压在画面上。
用户给了两张参考图（白色通透渐变 / 粉色渐变），过程中追加第三种
「现在这种黑黑的，但没有金黄边框、也是整屏铺满」。

**经典款（程序化默认）完全不动**，随时 `ui dialogue default` 切回——
这次是往皮肤库里加三套，不是替换。

### 改了哪些文件

```
新增  Editor/VNSoftSkinExporter.cs   三套皮肤的一键导出器（贴图/材质/prefab/登记）
改    Editor/VNUiSkinExporter.cs     把 BakeSprite / CreateImage / CreateText / Stretch /
                                     EnsureFolder / SavePrefab / SkinDir 等改成 internal 供复用
资产  Assets/VNEffects/UISkins/DialogueSkin_SoftWhite|SoftPink|SoftDark.prefab
资产  Assets/VNEffects/UISkins/Materials/VN_SoftText_*.mat        正文 TMP 材质（描边+柔光）
资产  Assets/VNEffects/UISkins/Textures/VN_BottomGradient.png     底部渐变带（白+alpha 曲线）
资产  Assets/VNEffects/UISkins/Textures/VN_RoundedRect.png        名牌底板（与经典款同一张）
改    Resources/VNGameConfig.asset   dialogueSkins 登记 白渐变 / 粉渐变 / 黑渐变
文档  HowToUse.md 八章新增「对话框皮肤（ui dialogue）」、CLAUDE.md 组件表与子系统表
```

菜单：**Tools → VN Effects → UI 皮肤 UI Skins → 导出无框渐变皮肤（白·粉·黑）Export Soft Gradient Skins**
（另有「(覆盖重建)」一项，带二次确认，会用出厂参数冲掉 Inspector 里的手工调整）。

### 规格

| | 白渐变 | 粉渐变 | 黑渐变 |
|---|---|---|---|
| 渐变带色 | 白 55% | 粉 (1,0.70,0.78) 55% | 近黑 (0.04,0.05,0.09) **80%** |
| 正文字色 | 深墨 | 深粉红 | 白 |
| 描边 | 白 0.10 | 白 0.12 | 黑 0.10 |
| 柔光/投影 | 白柔光 | 白柔光 | 黑投影（偏移 0.5） |

共通：渐变带 = 整屏宽 × 378px（35% 屏高）贴底；正文**居中顶对齐**、36pt；
名牌沿用经典款紫底圆角块（装饰样式仍由 `VNDialogueBox.nameplateStyle` 全局接管）；
头像窗与继续箭头都保留；`shineFrame` 槽位**留空 = 无边框无流光**。

### 技术决策与取舍

- **渐变贴图只有 alpha，RGB 全白**：三套共用同一张 PNG，换色 = 在 Inspector 里拉
  Image 的 Color。加新配色不用重烘贴图。
- **曲线是「下半段满浓 + 上半段 SmoothStep 淡出」，不是从底一路衰减**。
  一路衰减的版本实测过：屏幕底部亮度 0.696 → 0.358（暗了一半），
  但**台词所在的那一带（+160px）只从 0.673 → 0.526**——白字压在亮背景上会发虚。
  台词落在渐变带的 40%~63% 高度处，那一带必须是满浓区。
  顶边用 SmoothStep 是因为线性淡出会在收尾处留一条肉眼可见的硬边。
- **正文顶对齐（水平仍居中）**：垂直居中的话，一行台词会沉到渐变带中段，
  名牌孤零零飘在上面，两者隔开一大截。
- **描边宽度压到 0.10~0.12**：一开始给 0.16~0.18，白描边把中文笔画从外侧吃细，
  字看着发灰发虚。中文字形笔画密，描边宽度的上限比拉丁字母低。
- **头像避让设为 0**：正文左右各留 300px，头像窗（宽 230）落在正文左侧的空白里，
  不需要把正文推开——推了反而破坏居中。
- **每套一份独立 TMP 材质资产**：绝不能改字体的 sharedMaterial（会污染全项目
  所有用同字体的文字，项目硬约定）。underlay 通道照例要先 `EnableKeyword("UNDERLAY_ON")`。
- **登记进 VNGameConfig 用 upsert 而不是「有同名就跳过」**：覆盖重建会换掉 prefab 资产，
  只跳过的话配置里会留一个指向已删除资产的空引用，剧本切皮肤时报「未登记」。
- **toolbarAnchor 单独放一个空 RectTransform**，锚在经典款面板的位置。
  否则快捷功能条会停靠到 378px 高的渐变带右上角，飘到半空。

### 踩过的坑

- **同一帧内重烘贴图 + 立刻截图 = 拍到旧贴图**。`AssetDatabase.ImportAsset` 之后，
  内存里那张 Texture 要到下一帧才更新，导致连着两轮以为「参数没生效」。
  验证外观改动时，重烘和截图必须分两次 `script-execute` 调用。
- **Linear 色彩空间下，80% 的近黑覆盖在中亮背景上得到的是中灰（0.70 → 0.36），不是黑**。
  这是混合的正常结果，不是 alpha 没生效——肉眼看图容易误判成「没效果」，
  该用 ReadPixels 取亮度数值比对。

### 验证方法

编辑模式下把 prefab 实例化到场景 Canvas（`HideFlags.DontSave`，不写进场景文件），
填上示例台词，用 `cam.Render()` + `ReadPixels` 出图逐套核对，
并对「开/关渐变」两次渲染的同一像素取亮度差，确认遮盖强度。

---

## 一一六、名字样式六套新预设 + `ui name` 剧本命令（2026-08-28，分支 `agent/ai-deepseek`）

### 需求

用户对比商业 galgame 后问「为什么人家的名字那么有设计感，我的这么朴素，是字形问题吗」。
诊断结论不是字体：**是叠层数不够，以及最外层颜色选错**。
TMP 一个文字能叠五层（面/描边/underlay/浮雕光照/发光），项目原来只用了三层，
且 Bold 与 Outline 两套预设的**最外层都是白色**——遇到白背景或亮立绘时整个名字消失。
实测：同一套 Bold 参数在深底上清晰，在浅紫背景上糊到几乎读不出。

用户要求：多做几套供挑选，不加装饰件（只改字本身），并且要能用剧本命令随时切换。

### 改了哪些文件

```
改  Script/VNNameplateStyle.cs   +浮雕/光照/HDR 增益/固定渐变下端色 四组参数；
                                 +六套预设 Duo/Gold/Silver/Neon/Ink/Candy；
                                 +Aliases/TryParseId/NameOf（剧本名 ↔ 枚举，中英双写）
改  Script/VNStage.cs            SetUiSkin 加 case "name"；+CurrentNameplateStyleId；存档存取
改  Script/VNSaveSystem.cs       +nameplateStyle 字段（空 = 出厂样式，旧存档兼容）
改  Script/VNScriptRunner.cs     RebuildStateBefore 的 ui case 认 name（从选中行播放要能重建）
改  Editor/VNScenarioSchema.cs   ui 的 kind 枚举加 name + 说明列出全部预设名
改  Editor/VNScenarioLinter.cs   ui name 的样式名校验（Error 级）
文档 HowToUse.md 八章、CLAUDE.md 组件表与子系统表
```

### 语法

```
ui name <双描边|金边|银边|霓虹|墨影|糖果|粗体|描边|底板|朴素|default>
```

英文枚举名等价（`ui name Gold`）。样式进存档；颜色仍跟角色资产的 `nameColor` 走，
所以同一样式下每个角色的名字颜色不同。

### 技术决策与取舍

- **「三层字」系列的硬规则：最外圈必须是深色**。这是新六套与老四套的根本差别，
  也是「为什么人家的字在任何背景上都好看」的真正答案。白色最外层只在深背景成立。
- **镶金边 = Bevel 浮雕 + Lighting 打光**，不是颜色问题。金色渐变只提供色相，
  金属感来自 `_LightAngle` 打出的高光与暗面。Mobile 版 TMP shader 没有这组属性，
  所以 `ApplyBevel` 先 `HasProperty(_Bevel)`——直接 SetFloat 不报错也不生效，
  静默失效最难查，故缺了就退化成普通描边并**警告一次**（每句台词都会重新上妆，不能每次都警告）。
- **HDR 发光与上下渐变二选一**。uGUI 顶点色被钳到 1，渐变只能由顶点色表达；
  而 Bloom 阈值是 1.0，要发光就必须把带增益的颜色写进材质 `_FaceColor`。
  两条路互斥，所以 Neon 预设是纯色面而非渐变面。
- **Duo 与 Silver 的浅底补强**：白面/银面本身就接近浅背景，初版在浅底偏弱，
  把深外圈 dilate 提到 0.58、描边加厚一档解决。银边是固有的浅色，
  文档里直接写明「别在白天户外用」而不是继续硬调参数。
- **`ui name` 复用 ui 命令而不是新起关键字**：语义同族（都是外观切换、都进存档），
  Parser 关键字表不用动，Schema 只是 kind 多一个枚举值。
- **Lint 按 Error 而不是 Warning**：名字样式是内置预设，拼错必然静默无效果，
  没有皮肤 id 那种「稍后再登记」的中间状态。

### 验证方法

编辑期把十套预设走真实的 `Preset().ApplyTo()` 路径渲染到同一张图，
左半深底、右半浅底——**同一套参数必须两种底都成立才算可用**，
这个双底对照是这次能定位「白色最外层」问题的关键手段。

### 补充：编辑器下拉（同日追加）

用户反馈剧本编辑器里 `ui` 的第二个参数是纯文本框、每次都得手打样式名。
补成**跟着同行 kind 变的下拉**：

```
改  Editor/VNScenarioSchema.cs        +VNParamSource.UiSkinId；ui 的 id 参数改用它并 dependsOn:"kind"
改  Editor/VNScenarioDoc.cs           +dialogueSkinIds/choiceSkinIds 上下文字段；Validate 加 UiSkinId 分支
改  Editor/VNScenarioEditorWindow.cs  RefreshSources 收集 VNGameConfig 的皮肤 id；
                                      OptionsFor 加 UiSkinId → UiSkinOptions(kind)
```

- kind=`dialogue`/`choice` → default + VNGameConfig 里登记的皮肤 id
- kind=`name` → default + 十套内置样式名

**编辑期校验只管 kind=name**：名字样式是内置预设，拼错必然静默无效果；
而 dialogue/choice 的皮肤 id 允许「先写剧本、稍后登记」，编辑期就标红会一直红着变成噪音，
那一层交给 Lint。这跟 `Expression` 依赖角色参数是同一个 `dependsOn` 模式。


## 一一七、camseq 路径点震屏：`shake:` 参数（2026-08-28，分支 `agent/ai-deepseek`）

### 起因

运镜到位的那一刻想震一下（推到脸上 = 挨了一击、瞬切到废墟 = 爆炸余波），
原来只能在 camseq **整块之前**写 `shake heavy@` 让它并行跑——**震不到中间那个点**。
因为 `>` 路径点行之间插不进别的命令（parser 里 `>` 行只会往上一条 camseq 上挂），
「走到第 3 个点才震」这件事在旧语法里无法表达。

### 做法

给路径点加一个参数：`> 目标点 [zoom] [秒] [ease:] [xfade:] [hold:] [shake:等级|强度,秒数]`。

**为什么没有技术冲突**：震动作用在 `SceneRoot`（`VNScreenShake.target`），
运镜作用在它的子级 `ZoomRoot`（`VNCamera.target`）——这两层本来就是设计成可叠加的
（`VNCamera.SnapZoom()` 早就有个 `VNScreenShake shake` 形参，在到位瞬间轻震，先例就在那儿）。
所以「一边推镜一边震」是叠加而不是打架。

**三档 → 数值只有一张表**（`VNShakeSpec`，新增在 `VNScreenShake.cs`）：
原来 `Shake(VNShakeLevel)` 里那个 switch 就是唯一的数值来源，现在抽成
`VNShakeSpec.Of(level)`，因为**编辑器预览也要知道震动时长**（要把停顿算进时间轴）——
再抄一份迟早对不上。`TryParse` 同时认三档别名和 `强度,秒数`，运行时与编辑器共用它，
判定不可能分叉。`Format()` 反过来把数值折回别名（命中三档就写 `heavy` 而不是 `34,0.6`）。

### 「等震完再走」怎么实现的

触发点编进**同一条 DOTween Sequence**（`BuildSegment` 里 `AppendCallback`），
而不是在协程里 `yield`：这样它和 hold、和后续段共用一条时间轴，
**Skip 快进（`DOTween.timeScale`）时不会错位**——协程里 yield 一个独立 tween 就会变成快进中唯一的卡点。

停顿取 **`max(hold, 震动时长)` 而不是相加**：写 `hold:1 shake:heavy` 就该老老实实停 1 秒
（0.6 秒的震动跑在这一秒里面），不然 hold 的语义「到点后停留的秒数」就被震动偷偷改掉了。

`xfade:` 叠化点不在 Sequence 里（它是「截屏→瞬切→淡出」的协程），
所以另走 `ShakeHoldCo()`，但用的是同一条 `max` 规则。

### 编辑器

- **控件共用一份**：`VNCamShakeUi.Draw()`（放在 `VNCamWaypoint.cs`，与解析层同源），
  剧本编辑器的路径点行和镜头编排窗口的第二行各调一次。
  下拉 `(不震)/light/medium/heavy/自定义…`，选自定义才在右边出文本框；
  框里的值非法时**染橙**——写错了要看得见，不能让下拉悄悄把它改成「不震」。
- **`VNCamWaypoint.TryParse` 仍然严格**：`shake:20`（少了秒数）、重复的 `shake:` 都直接
  返回 false → 整行退回纯文本并标黄。Lint 那条 `unrecognized-waypoint` 走的就是这个
  TryParse，所以**没写一行新的校验代码**，非法 shake 自动被点名。
- **预览时间轴要算上这段停顿**（`AddHoldSegment` 改成 `max(hold, 震动时长)`）：
  时长算短了，拖进度条就和实机对不上。**震动本身不在预览里模拟**——
  编辑器画布上抖几像素既看不出效果又干扰点选。

### 踩到的

`VNScriptRunner.CloneWithParams()` 里 camPoints 的深拷贝是**逐字段手写**的，
加了新字段不补进去，`call` 带参数调用的子程序里 camseq 的 shake 会静默丢失
（本次已补 `shake = point.shake`）。这一处每加一个路径点字段就要跟着改，
和 `VNCamWaypoint.TryParse/Format`、`VNCamseqEditorWindow` 的 `Waypoint` 类是同一批。

### 用法

```
camseq
> 亚里沙:head 1.9 0.25 shake:heavy   # 急推到脸上 + 强震
> middle 1 0.6 shake:20,0.5          # 自定义 20px / 0.5 秒
```


### 追加：`stay` 原地点（同日）

`shake:` 做完后发现「镜头不动、只在原地震一下」得这么写：

```
> 亚里沙:head 1.9 1.2
> 亚里沙:head 1.9 0 shake:heavy   ← 点位和 zoom 手抄一遍
```

能用，但**改了上一行忘了改这行，镜头会在震之前先跳一下**，而且这种 bug 很难一眼看出来。
于是加了点位词 `stay`：位置与 zoom 都沿用**前面最近一个真点位**。

```
> 亚里沙:head 1.9 1.2
> stay 0 shake:heavy      # 原地强震
> stay 0 hold:0.8         # 再静静停 0.8 秒
```

**唯一的坑：数字位前移**。普通行「第 1 个数字 = zoom、第 2 个 = 时长」，
但 stay 没有 zoom 可填，所以**第一个数字就是时长**（默认 0）。
不这么设计的话 `> stay 0` 会被读成 zoom=0、时长取默认 0.8 秒——一个看不见的 0.8 秒空档。
代价是语法不完全一致，所以 **stay 行出现第二个数字一律判错**（运行时告警、
`VNCamWaypoint.TryParse` 返回 false 退回纯文本标黄、Lint 点名），让照旧习惯写的人立刻看见。
编辑器里 stay 行的 zoom 格是禁用的「沿用上一个点」占位，视觉上也不会诱导你去填。

**运行时**：`CamseqCo` 里维护 `lastPoint/lastZoom`。注意循环必须从 0 开始遍历**所有**点，
`skipFirst`（start:cut 已由 bg 转场应用过首点）只跳过「加进 list」这一步而不是跳过解析——
否则 `start:cut` 后面第一个 stay 就没有基准可沿用。

**编辑器**：新增 `VNCamPointKind.Stay` / `PointType.Stay`（两套枚举各一个，本来就是两份）。
`TargetState` 加了按下标的重载，遇到 Stay 就往前找最近一个真点位——与运行时同一条规则。
画布上**不画 stay 的取景框**：它与上一个点完全重合，画出来只会互相遮住，
而且点选/拖角/拖中心都会拖错点，所以那几处（画框、命中检测、拖动、空白处点击设坐标）
全部跳过 Stay。**但预览时间轴照常把它的时长算进去**，拖进度条才和实机对得上。

**Lint 新增一条**：`stay` 当第一个路径点报 Err（没有「上一个点」可沿用，运行时会跳过它）。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `VNScreenShake.cs` | 新增 `VNShakeSpec`（三档数值表 + TryParse + Format）；`Shake(level)` 改走它 |
| `Script/VNScriptParser.cs` | `VNCamWaypointDef.shake` 字段 + `ParseCamWaypoint` 认 `shake:` token（非法告警并忽略） |
| `Script/VNScriptRunner.cs` | `CamseqCo` 填 `Waypoint.shake` 并把 `stage.screenShake` 传进 `PlayPathCo`；补 `CloneWithParams` 深拷贝 |
| `VNCamera.cs` | `Waypoint.shake` 字段；`BuildSegment`/`PlayPathCo` 收 shaker 参数；新增 `ShakeHoldCo()` |
| `Editor/VNCamWaypoint.cs` | `shake` 字段 + 严格 TryParse/Format；新增共用控件 `VNCamShakeUi` |
| `Editor/VNScenarioEditorWindow.cs` | 路径点行尾加「震」控件 + 语法提示 |
| `Editor/VNCamseqEditorWindow.cs` | `Waypoint.shake` + 行 UI + 文本生成/解析 + `AddHoldSegment` 算进震动时长 + 帮助文字 |
| `Editor/VNScenarioDoc.cs` | Lint 提示语法补上 `hold:`/`shake:`（校验本身复用 TryParse，无需新代码） |
| `Script/VNScriptParser.cs`（stay） | `VNCamWaypointDef.StayToken`/`IsStay`；stay 行数字位前移、多余数字告警 |
| `Script/VNScriptRunner.cs`（stay） | `CamseqCo` 维护 lastPoint/lastZoom；skipFirst 只跳过入列不跳过解析 |
| `Editor/VNCamWaypoint.cs`（stay） | `VNCamPointKind.Stay` + 严格 TryParse/Format |
| `Editor/VNScenarioEditorWindow.cs`（stay） | 类型下拉加「原地」、目标格换成说明文字、zoom 格禁用占位 |
| `Editor/VNCamseqEditorWindow.cs`（stay） | `PointType.Stay`、`TargetState(int)` 往前找真点位、画布不画/不可拖、生成文本不写 zoom |
| `Editor/VNScenarioDoc.cs`（stay） | 新增「首点不能是 stay」的 Err |

## 一一八、背景无限滚动 `bgscroll`（2026-08-29，分支 `agent/ai-deepseek`）

### 需求

一张背景图永远往一个方向流，营造"在走 / 在开车 / 云在飘"的持续动感。

### 选型：滚 UV，不是拼两张图

「首尾拼接两张图」是最直觉的做法，但在这个项目里代价很大：要复制第二个 Image +
材质实例 + mood 注册，还要改 `bg` 转场逻辑，而且它和 `VNKenBurns` 抢同一个 RectTransform。

改成**在 shader 里滚 UV**之后：背景仍是一个 Image，`bg` 转场 / `camseq` 运镜 / 视差
全都不用动，**而且能和 Ken Burns 叠加**——那个动 transform、这个动 UV，各走各的，
叠起来是"一边缓慢呼吸一边流动"，比只有滚动更有生命力（所以刻意没做成互斥）。

### 接缝：自己在 shader 里折，不依赖纹理导入设置

硬件 wrap mode 靠不住（图集、导入设置都可能不是 Repeat），所以平铺在 `vnWrapUV()` 里自己算：
`repeat` 直接 `frac`，`mirror` 走 ping-pong。**前提是纹理没开 mipmap**——
UI Sprite 默认就是关的，开了的话 `frac` 的跳变会让接缝糊掉一行。

**这里踩了一个必须靠眼睛才能发现的坑**：ping-pong 一开始写成
`abs(frac(s*0.5)*2-1)`，看着对，实际上它在 `s∈[0,1]` 是 **1→0 递减**——
偏移为 0 时整张图就是翻的。截图里桌椅倒挂在天花板上才发现，正确写法是
`1.0 - abs(frac(s*0.5)*2-1)`。**这种 bug 单元测试和 Console 都看不出来，只有真截一张图才知道。**

另外模糊（伪景深 9-tap）和轮廓光那些"在主 uv 附近再采一次"的地方也必须过一遍 `vnWrapUV`，
否则采样点会越过接缝跑到 [0,1] 外面被 clamp 成边缘色 → 接缝处一条亮线。

### 速度单位

`speed` 是**画布像素/秒**（1920 宽为基准），不是 UV/秒——写剧本的人对像素有直觉，
对 UV 没有。走路≈120，跑步≈250，坐车≈400，环境氛围≈6。

`dir` 说的是**画面内容往哪边流**（不是人物前进方向），所以采样坐标要反着走，代码里取负。
默认 180（往左流 = 人物在往右走）。

### mirror 挑图

天空/云/树林/水面/抽象材质效果很好；**强透视的图（走廊、街道）会变成"两条走廊对着开"**——
实机截图里非常明显。那种图请准备无缝素材配 `repeat`。

### 存档

开关/速度/方向/平铺方式进存档，读档直接就位不缓入（缓入会让读档后画面先"起步"一下）。
**累计偏移刻意不存**：从哪一帧接着滚玩家看不出来，存了反而多一个要维护的数。
`bg` 换图不停滚动（还在车上就该继续滚），只把偏移归零让新图从头开始流。

### 一个顺带的发现（没动）

`VNScriptDemo` 场景的背景 Image 上**原本没有 `VNImageEffectController`**，
所以 `VNStage.backgroundFx` 一直是 null，`mood` 的分层调色也没注册到背景上。
本次由 `VNBackgroundScroll` 的 `[RequireComponent]` 在运行时补上了 controller，
滚动因此能工作，但 `backgroundFx` 的赋值发生在补挂之前，所以仍是 null——
**没有改动这个顺序**，因为让背景突然开始被 mood 染色会改变现有演出。要不要修由你定。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Assets/Art/Shaders/VNImageEffect.shader` | `_ScrollMode`/`_ScrollOffset` 两个属性 + `vnWrapUV`/`vnScrollUV`；模糊 8 抽与轮廓光 2 抽包上 wrap |
| `VNBackgroundScroll.cs`（新） | 滚动组件：速度缓入缓出、像素/秒→UV 换算、偏移折回 [0,2) 防精度掉光、方向/模式词解析 |
| `VNImageEffectController.cs` | 加 `HasMaterial`（销毁流程里不能用 `Mat`，那会顺手新建一个） |
| `Script/VNStage.cs` | `bgScroll` 引用 + 自愈补挂 + `SetBackgroundScroll()` + 换图归零偏移 + 存档读写 |
| `Script/VNScriptParser.cs` | `bgscroll` 关键字 |
| `Script/VNScriptRunner.cs` | 命令派发（含 dir/mode 认不出时的告警）+ 调试重建重放（参数留空 = 沿用，重建也要照这个语义累积）|
| `Script/VNSaveSystem.cs` | `scrollOn/scrollSpeed/scrollDir/scrollMode` 四个字段 |
| `Editor/VNScenarioSchema.cs` | `bgscroll` 命令登记（5 个参数，剧本编辑器/Ctrl+E 面板自动出现）|
| `Editor/VNEffectsDemoSetup.cs` | 场景生成器给背景挂上滚动组件并回填 VNStage |

---

## 一一九、素材管理：VNGameConfig 分页 Inspector + 素材浏览器窗口（2026-08-29，分支 `agent/asset-manager`）

### 需求 / 背景

`VNGameConfig` 是全项目内容的总配置，一个资产里塞了 30 多个字段、十几个列表。
用户的原话是「就一个 SO 里面有太多东西了我经常要 scroll 好久才找到我想设定的那个项目」，
以及「放进去的 CG 或背景图片也没有预览，令我看不到哪张图片是哪张，音频也没有试听之类的功能」。

排查后发现**滚不动的根因不是字段多，而是两件具体的事**：

1. **`[Header]` 被写在了列表元素内部的字段上。** `VNStage.BackgroundEntry` / `CgEntry` /
   `VNAudio.AudioEntry` / `VNGameConfig.UiSkinEntry` 都是这个写法：

   ```csharp
   public class CgEntry {
       [Header("剧本 cg 命令引用的 CG id")]  public string id;
       [Header("CG 一枚绘")]                 public Sprite sprite;
       [Header("差分组名（同组 CG 在鉴赏画廊里归为一格翻页；留空 = 独立）")] public string group;
   }
   ```

   `[Header]` 是 DecoratorDrawer，Unity 默认 Inspector 会**给列表里的每一项都重画一遍**，
   于是一个 CG 条目占 6~7 行 —— 7 张 CG 就是 50 行。这才是「要滚很久」的真正来源。

2. **`VNGameConfig` 从来没有 CustomEditor。** Editor 目录下 30 个文件，一个 `[CustomEditor]`
   都没有，看到的完全是 Unity 默认序列化 Inspector：没有分页、没有搜索、没有缩略图、没有试听。

还有一个隐藏前提让「预览」从锦上添花变成刚需：本项目的素材文件名是 AI 生成时的原始 prompt
或纯数字 ——
`masterpiece, very aesthetic, highly detailed, 1girl, solo, anime visual novel cg s-1095962266.png`、
`1.png`、`c1.png`、`Sweet Homemade  Hitomi.la 3.png`。
**文件名完全不表意，光看字不可能认出哪张是哪张。**
所以新界面一律以缩略图为主、id 为标签，文件名退居次要信息。
（反过来说，「id 手动填中文名」这个既有设计是对的，本次没有动它。）

### 做了什么

分两层，**运行时代码一行没改**、`VNGameConfig` 的字段结构一个没动，所以零迁移风险，
剧本、存档、既有引用全部不受影响。

**① PropertyDrawer 层（全项目通用）** —— 条目从 6~7 行压成 1 行。
类型上一旦挂了 `CustomPropertyDrawer`，Unity 就不再递归画子字段，那些 `[Header]` 自然消失；
说明文字改挂 tooltip，不占版面但鼠标悬停仍看得到。
因为 drawer 是挂在**类型**上的，`VNStage` / `VNAudio` 组件 Inspector 上的同名列表也一并变紧凑，
不只 `VNGameConfig` 受益。

- 缩略图够高（≥34px）时排两行：上 id、下 资产 + 附加字段；调小自动退回单行。
- 缩略图格子本身可拖入资产替换、单击 ping 到 Project。
- 音频条目多一个 ▶ / ■ 试听按钮 + 波形 + 播放进度条。

**② 分页 Inspector（`VNGameConfigEditor`）** —— 按功能切成
`剧本｜标题｜UI 皮肤｜舞台｜音频｜玩法｜AI｜大头贴｜全部` 九页，一次只画一组，选中页存 EditorPrefs。
每个列表再加：搜索框、条数 / 匹配数、分页（默认每页 50）、行内 ▲▼✕、
id 重复与 id 为空的告警、以及底部的**批量拖入区**（拖一批素材进来自动建条目、id 预填文件名）。

**③ 素材浏览器窗口（`VNAssetBrowserWindow`，Tools → VN Effects → 素材浏览器 Asset Browser）** ——
左栏九个类别（带条数），右侧图片走大缩略图网格、音频走波形列表，底部详情栏可直接改 id / 换素材 /
试听 / 定位 / 移除，右键菜单另有「用文件名填 id」「上移/下移」。
网格与列表都做了**虚拟化**（只画滚动窗口内的那几行），200+ 素材不掉帧。
另有「只看未登记」：列出素材目录里有、但库里没登记的文件，一键补登。

### 技术决策与取舍

- **Sprite 缩略图不走 `AssetPreview`。** `AssetPreview.GetAssetPreview` 是异步的，首帧返回 null，
  要靠反复 Repaint 才等得到，列表里几十张图一起等会闪一片空白。Sprite 自己就知道在哪张 texture
  的哪个矩形，直接 `GUI.DrawTextureWithTexCoords` 画那块 UV 即可 ——
  **同步、精确、且不需要 texture 可读**。AudioClip 没有这种捷径（波形只能靠 AssetPreview），
  所以音频那边仍走异步 + 占位兜底。
- **异步预览不能无限等。** 有些资产（纯数据 SO、导入失败的音频）永远不会有预览图，
  一直请求重绘就是空转。原本想用 `AssetPreview.IsLoadingAssetPreview(instanceID)` 问 Unity
  「还在加载吗」，但 **Unity 6.5 起 `IsLoadingAssetPreview(int)` 和 `Object.GetInstanceID()`
  都是 error 级弃用（CS0619，不是警告，直接编译失败）**。
  改成不问 Unity，自己给每个资产一个 3 秒等待窗口，到点放弃 —— 顺带避开了绑死新版 API。
- **音频试听走反射。** `UnityEditor.AudioUtil` 是 internal。方法名 Unity 各版本改过
  （2020+ 是 `PlayPreviewClip`，更早叫 `PlayClip`），所以逐个候选探测，
  探测不到时按钮变灰而不是抛异常。本机实测 `CanPreviewAudio = True`。
- **Inspector 用分页而不是虚拟化。** Inspector 里拿不到宿主 ScrollView 的可见区域，
  没法可靠地只画可见行；而窗口是 EditorWindow，滚动位置自己管，所以那边做了真虚拟化。
  分页在这里反而更对症 —— 用户嫌的就是滚动太长。
- **搜索口径与剧本编辑器一致**：空格分隔多关键字、全部命中、大小写不敏感、**纯子串包含**，
  不做模糊 / 拼音，避免「搜出一堆不相干的」。
  可搜索文本用通用做法拼（元素的所有字符串字段 + 所有引用资产的文件名），
  所以以后新增条目类型不用改搜索代码。
- **列表用手绘而不是 `ReorderableList`。** 过滤 + 分页会让 ReorderableList 的重排索引对不上；
  手绘用 ▲▼ 按钮，且**搜索激活时隐藏 ▲▼**（此时「相邻」没有意义，留着只会误操作）。
- **未分配字段有兜底。** 页签只登记字段名，绘制仍走 `PropertyField`。
  没被任何页签认领的字段会自动落到「其他」页并给出提示 ——
  以后往 `VNGameConfig` 加字段**不会在 Inspector 里静默消失**。
- **「只看未登记」的扫描目录是反推的，不写死。** 项目里素材目录改过几次
  （`Assets/CG` 与 `Assets/Art/Images/CG` 并存），写死路径必然过时；
  改成从已登记条目的资产路径反推目录集合。代价是库全空时无从判断，此时给提示而不是报错。
- **本次不碰素材文件本身。** 与用户确认过：不做分类 / 标签系统、不做剧本反查引用、
  不批量重命名或移动文件。文件名乱就乱着，靠缩略图和 id 认。
- 全程走 `SerializedObject`，改动自动进 Undo、自动标脏；不直接写字段。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Editor/VNAssetUi.cs`（新） | 三边共用的绘制与预览层：Sprite 缩略图（同步 UV 画法）、音频试听（AudioUtil 反射 + 多版本候选）、波形、拖拽接收、搜索匹配、Rect 切割辅助 |
| `Editor/VNConfigEntryDrawers.cs`（新） | 背景 / CG / 音频 / UI 皮肤四个条目的紧凑单行 drawer，含试听按钮与拖入替换 |
| `Editor/VNGameConfigEditor.cs`（新） | 九页分页 Inspector + 智能列表（搜索 / 分页 / 行操作 / id 告警 / 批量拖入）+ 未认领字段兜底 |
| `Editor/VNAssetBrowserWindow.cs`（新） | 素材浏览器窗口：类别栏、虚拟化网格与音频列表、详情栏、右键菜单、只看未登记 |
| `Script/VNStage.cs` | `BackgroundEntry` / `CgEntry` 字段上的 `[Header]` → `[Tooltip]`（见下方修复记录）|
| `Script/VNAudio.cs` | `AudioEntry` 同上 |
| `Script/VNGameConfig.cs` | `UiSkinEntry` 同上 |

三个运行时文件**只动了 attribute**，字段名/类型/顺序一律没碰，序列化格式与存档完全不受影响。

### 修复记录：文字叠印 + 输入框点不进去

初版交付后用户报告：条目行上叠印着一串说明文字（"剧本中引用的 id（可中文，如 黄昏之歌 /
雨声）"、"背景图"、"该素材的基准音量…"），而且 **id 与名字都没法点进去输入**。
Inspector 与浏览器窗口的详情栏都中招。

**根因是一个，症状是两面。** 那些叠印的文字正是条目类字段上的 `[Header]`。

一开始的判断「挂了 PropertyDrawer 就不会画内部 Header」只对了一半：
Unity 确实不会**自动递归**去画子字段的 decorator，但
**drawer 内部只要自己调 `EditorGUI.PropertyField(rect, 子属性, …)`，就会触发该子属性的 decorator**。
而 Unity 画 decorator 的方式是（`PropertyHandler.OnGUI`）：

```csharp
foreach (var decorator in m_DecoratorDrawers) {
    var rect = position;
    rect.height = decorator.GetHeight();
    position.yMin += rect.height;   // ★ 把控件区域往下推 ~26px
    decorator.OnGUI(rect);
}
```

我传进去的 rect 只有 18px 高，`yMin` 被推掉 26px 之后**控件区域高度变成负的**，
于是控件被画到了错误位置、也接不到点击 —— 这就是「打不了字」。
同时 Header 文字画在了原 rect 上，叠在别的内容之上 —— 这就是「文字被盖住」。
两个症状同源。

**修法**：把四个条目类里**字段上的** `[Header]` 全部改成 `[Tooltip]`。
`[Tooltip]` 不是 DecoratorDrawer，不占布局、不推 rect，说明文字改为悬停可见。
（列表**字段本身**上的 `[Header]`，如 `[Header("音频库（按通道分开管理）")]`，是正常用法，保留。）

顺带修掉三处同类隐患 —— 我自己也在控件之上叠了 tooltip / 占位提示的 label，
其中**音量那个 `LabelField` 直接盖在 slider 上，会挡住拖动**。
统一收进 `VNEntryDrawerBase.Overlay()`，**只在 `Event.current.type == EventType.Repaint` 时画**，
保证叠加层绝不参与事件处理。

另外把浏览器窗口里音频行的行高从 `max(26, GridSize×0.32)` 提到 `max(46, GridSize×0.42)`：
`TwoLines` 需要 2×18+2 = 38px，而 `work = rowH − 2 − 4`，所以 rowH 至少要 44，
原来的值会让 id 与文件名两行挤在一起。

**教训（已写进 `VNStage.BackgroundEntry` 的代码注释与 ProjectCodeGuide）**：
列表元素类里的字段说明**一律用 `[Tooltip]`，绝不用 `[Header]`**；
任何画在控件之上的叠加层，都要先想清楚它吃不吃事件。

### 验证方法

- `dotnet build Assembly-CSharp-Editor.csproj`：0 error，四个新文件 0 warning
  （临时把新文件加进 csproj，验完还原，见 [vn-debug] 的编译验证节）。
- Unity 内 `assets-refresh` 后 Console 零 Error / 零 Exception。
- 冒烟脚本确认：`Editor.CreateEditor(cfg).GetType()` 返回
  `VNEffects.EditorTools.VNGameConfigEditor`（CustomEditor 确实挂上了，没有静默回退默认 Inspector）、
  `VNAssetUi.CanPreviewAudio = True`（反射拿到了 AudioUtil）、
  浏览器窗口打开后持续绘制无异常。
- 当前库规模：背景 13 / CG 7 / BGM 8 / SE 1 / Voice 11 / 角色 4。
- 修复后回归：反射确认四个条目类 **残留 `[Header]` = 0、`[Tooltip]` = 10**；
  数据未被破坏（首条背景 id=`教室`、首条 BGM id=`日常` vol=1）；Unity 全量重编译零 Error。

### 后续可做（本次刻意没做）

- 素材分类 / 标签（Unity Asset Labels 零数据结构改动即可实现，用户暂时不需要）。
- 剧本反查引用（哪张图被哪些 `.vn.txt` 的哪一行用过 / 未使用素材检测）。
- 批量重命名与目录整理（`Background` 目录里目前混了立绘和 CG）。

---

## 一二〇、新图自动设为 Sprite：贴图导入默认值（2026-08-29，分支 `agent/asset-manager`）

### 需求 / 背景

每次往项目里加图片，都要手动把 Texture Type 改成 `Sprite (2D and UI)`、
Sprite Mode 改成 `Single`，一张张点很烦。希望新图进来就是对的。

### 做了什么

新增 `Editor/VNTextureImportDefaults.cs`（`AssetPostprocessor`）：
素材目录里首次导入的图片，自动设为 `Sprite (2D and UI)` + `Single`。
另配一个菜单 **Tools → VN Effects → 贴图 Textures → 套用 Sprite 导入设置到选中项 Apply Sprite Settings**
给存量图手动补（选中图或文件夹，递归处理，会先弹确认）。

### 技术决策与取舍

- **用 `OnPreprocessTexture` 而不是导入后再改**：它在**导入前**跑，改完 importer 才开始
  真正导入 —— 拖进来一次就是对的，不会先按默认设置导一遍再重导一遍。
- **必须是白名单目录，不能全项目一刀切**。`Assets/Art/Models/**` 下有 60+ 张模型贴图
  （法线 / 粗糙度 / 金属度），**法线贴图一旦按 sRGB 的 Sprite 导入光照就全错**，
  而且这种错很难第一时间联想到导入设置。`Assets/Development/DebugScreenShot` 同理。
  白名单：`Art/Images/`、`Art/CG/`、`Art/BigPhoto/`、`Art/Mark/`、`Assets/Assets/`
  （前缀匹配，子目录一并覆盖；新开素材目录时往 `Roots` 补一行）。
- **只在 `importSettingsMissing` 时设置**，即"这张图的导入设置从没被人配置过"。
  好处是你手动调过的任何设置（Pivot、Max Size、改成 Multiple 切图…）都会让它变成 false，
  于是**永远不会被打回去**。无条件强制的话，切好图的立绘图集会在下次 reimport 时
  被打回 Single，切图数据虽然还在 .meta 里但不再生效 —— 属于"改了没反应"里最难查的一类。
- 只设 Texture Type 与 Sprite Mode 两项，其余（Max Size、压缩质量、mipmap）保持 Unity 默认。
  按用户要求不额外预设。顺带一提，Sprite 类型默认就不生成 mipmap，
  与「背景图别开 Generate Mip Maps」（一一八章 bgscroll）天然一致。

### 修复记录：`importSettingsMissing` 的语义比字面更宽

初版只判断 `importSettingsMissing`，实测后发现 **5 张存量背景图的 .meta 被静默改写**
（`bg06a` / `bg08a` / `bg17a` / `zbg13aa` / `zbg26ab`，`textureType: 0 → 8`，
连带 `enableMipMap`、`wrapU/V`、`nPOTScale`、`alphaIsTransparency` 一串默认值跟着变）。

原因：`importSettingsMissing` **不等于"没有 .meta"**。
meta 存在却不含完整 importer 设置块时它同样返回 true ——
工程里那些很早以前加进来、一直是 Default 类型没人动过的老图就属于这种。

**试过的修法（失败）**：加一条「磁盘上没有 .meta 文件」来卡死"新文件"。
**不成立** —— Unity 在调用 preprocessor 之前就已经把 .meta 写盘了，新旧图一律为真，
加上这条之后整个 postprocessor 完全不生效（新图也变不成 Sprite 了）。
这条已写进代码注释，免得以后有人再试一遍。

**最终处置**：还原那 5 个 meta，保留 `importSettingsMissing` 判断（这是 Unity 官方推荐
用来做"仅在用户尚未配置时应用默认值"的信号），并扫描确认实际影响面：

```
白名单目录内贴图共 187
  已是 Sprite/Single ............ 172
  已被人为配置过（不会被碰）...... 15
  ★从未配置过（会被顺带改）....... 0
```

**当前工程里已经没有会被顺带修改的图**，所以保持简单方案。
需要严格到"只碰新文件"的话，得引入一份基线 GUID 清单并进 git，
当前收益为零，没做。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Editor/VNTextureImportDefaults.cs`（新） | AssetPostprocessor + 白名单目录 + 对选中项手动应用的菜单 |

### 验证方法

真实导入测试（放测试图 → refresh → 查 importer → 删测试图）：

| 用例 | 结果 |
|---|---|
| 新图 · `Art/Images/Background/` | `Sprite / Single` ✓ |
| 新图 · `Art/Mark/` | `Sprite / Single` ✓ |
| 新图 · `Art/Models/`（对照组）| `Default / None` ✓ 模型贴图未被误伤 |
| 存量 `bg06a.png` | `Default / None` ✓ 未被碰 |
| `ForceUpdate` 全量重导入后 `git status` | 零个存量 .meta 被修改 ✓ |

`InScope` 判定：Background=✓ Mark=✓ Models=✗ Development=✗。

---

## 一二一、素材浏览器的樱花粉白主题（2026-08-29，分支 `agent/asset-manager`）

### 需求 / 背景

用户问「Unity Editor 这个界面的 UI 和风格可以改变吗，比如粉粉的可爱风」。

答案分两层，值得先讲清楚：

- **Unity 编辑器整体：基本改不了。** 官方只有 Light / Dark 两套主题
  （Preferences → General → Editor Theme），没有自定义入口。2019.3+ 编辑器 UI 虽然迁到了
  UI Toolkit / USS，但那些样式表打包在编辑器资源里，不对外开放覆盖。
  第三方方案（替换 Unity 安装目录的 skin 文件、反射篡改 `EditorStyles`）影响所有项目、
  Unity 升级即坏、易与其他插件冲突，**没采用**。
- **自己 IMGUI 画的窗口：完全可控。** 素材浏览器的每一像素都是自己画的，
  颜色、圆角、描边、字体样式全归自己管。所以这次只做这一个窗口。

### 做了什么

新增 `Editor/VNAssetTheme.cs`：把窗口的配色、圆角、GUIStyle 收成一处，
两套主题 **默认 / 樱花**，顶栏 `🌸` 按钮随时切换，选择存 EditorPrefs。

樱花配色：窗口底 `#FFF5F8`、侧栏 `#FDEBF1`、工具条 `#FCE4EC`、卡片纯白、
描边 `#F5D8E2`、主色 `#FF8FB1`、正文 `#5A4048`、次要 `#9B8189`。
网格格子变成**圆角白卡**（图 + 标签同在一张卡里，缩略图缩进 5px 留白边），
选中态是粉色描边 + 淡粉底；侧栏选中项圆角白底粉框；音频行、拖入提示、
搜索框、按钮全部圆角化；详情栏白底 + 顶部 2px 粉线。

### 技术决策与取舍

- **圆角靠程序化贴图，零美术依赖**：生成一张 32×32 的**白色**圆角贴图，
  用 `GUI.color` 染成任意颜色后配 `GUI.DrawTexture` 的 borderRadius 参数绘制 ——
  一张贴图搞定所有尺寸与配色（与 `VNProceduralTextures` 一个路子）。
  另生成一张"只有描边、中间透明"的用于外框。
  边缘按到圆心的有符号距离做 1px 软过渡，缩放后不锯齿。
  贴图是 static 的、域重载后会丢，所以全部 lazy 重建 + `HideFlags.DontSave`（绝不写进资产）。
- **★ 换浅色底之后，必须显式覆盖每一个 GUIStyle 的文字颜色。**
  Unity Dark 主题下 `EditorStyles` 的文字是浅色的，直接拿来用在粉白底上
  就是**白底白字，字直接消失**。而且要覆盖 `normal/hover/active/focused`
  以及对应的 `on*` 共八个状态，只改 `normal` 的话鼠标一悬停字又不见了。
  这就是 `VNAssetTheme.Tint()` 存在的理由。
- **主题是"叠加"而不是"替换"**：`Enabled == false` 时所有绘制函数**原样退回**
  Unity 原生外观（`Box` 退回 `DrawRect`、`Button` 退回 `miniButton`、
  样式退回 `EditorStyles`）。所以默认主题下这次改动等于不存在，随时能切回来。
- **只做这一个窗口**。VNGameConfig 的 Inspector 外层面板框架是 Unity 的，
  改不动；剧本编辑器等既有窗口各画各的，改动面大且有回归风险。按用户选择先只试这里。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Editor/VNAssetTheme.cs`（新） | 主题定义：调色板、程序化圆角贴图、八状态染色的 GUIStyle、Box/Outline/CardBox/Button 绘制辅助 |
| `Editor/VNAssetBrowserWindow.cs` | 顶栏 / 侧栏 / 网格卡片 / 音频行 / 详情栏 / 拖入提示全部接主题；顶栏加 🌸 主题切换 |

### 验证方法

- Unity 重编译零 Error / 零 Exception。
- 圆角贴图自检：`fill` 四角 alpha=0.00、中心 alpha=1.00；`outline` 中心 alpha=0.00
  （描边中间必须透明，否则会糊住卡片内容）。
- 切到樱花主题后窗口持续绘制无异常；切回「默认」外观与改动前一致。

---

## 一二二、剧本编辑器改读 VNGameConfig：新登记的素材立刻能搜到（2026-08-29，分支 `agent/asset-manager`）

### 问题

在素材浏览器里登记了新背景（`scroll1~5` / `test1` / `test2`），
**剧本编辑器的 `bg` 下拉里搜不到**，点工具栏的 `Refresh Sources` 也没用。

### 根因：写的和读的是两份不同的数据

- 素材浏览器 / VNGameConfig Inspector 写 → **`VNGameConfig` 资产**
- 剧本编辑器的 bg / cg / bgm 下拉读 → **场景里的 `VNStage` / `VNAudio` 组件**
  （`VNScenarioEditorWindow.RefreshSources()` 里 `FindFirstObjectByType<VNStage>()`
  然后遍历 `stage.backgrounds`）

实测对得上：`VNGameConfig.backgrounds = 20`、`场景 VNStage.backgrounds = 13`，
差的正好是新加的那 7 个。

这是**既有的架构不一致**，不是素材浏览器引入的 —— 以前直接在 VNGameConfig Inspector
里加图也一样搜不到。而且它和项目自己的规定互相矛盾：CLAUDE.md 与 `VNGameConfig` 的注释
都写着「配置进资产，不进场景」「不建议再往场景组件里填」，编辑器却只读场景组件。
`Refresh Sources` 之所以没用，是因为它重读的还是场景组件。

### 做了什么

**① 取数源改成与运行时同一套覆盖语义**：`VNGameConfig` 里填了就用它的，留空才回退场景组件
（与 `VNGameConfig.ApplyList` 的规则一致）。抽了两个小工具：
`LoadGameConfig()`（编辑期直接走 `AssetDatabase`，不用 `VNGameConfig.Active` 的运行时缓存，
免得受 Play Mode 进出清缓存的时机影响）和 `PickLibrary<T>(fromConfig, fromScene)`。
覆盖 backgrounds / cgLibrary / bgm / se / voice 五个库。

**② 加「改完自动刷新」**：新增 `Editor/VNAssetLibraryEvents.cs`，一个静态 `Changed` 事件。
素材浏览器与 VNGameConfig Inspector 的**所有写入点**统一走
`Apply()` / `ApplyAndNotify()`——`SerializedObject.ApplyModifiedProperties()`
**返回 true 时才广播**，所以只有真改了东西才触发重建，不会每帧空转。
剧本编辑器在 `OnEnable` 订阅、`OnDisable` 退订，收到就 `RefreshSources() + Repaint()`。

### 技术决策与取舍

- **让写方发信号、读方自己重建，而不是让写方去认识读方。** 素材浏览器不需要知道
  剧本编辑器的存在，以后再多几个消费方也只是各自订阅。
- **静态事件必须成对订阅/退订。** 订阅者是 EditorWindow 实例，窗口关闭与域重载都会重建窗口；
  不在 `OnDisable` 退订的话，事件会一直攥着已销毁窗口的引用，下次广播时对着
  「假 null」的窗口调方法 —— 典型的编辑器内存泄漏 + 幽灵异常。
- **广播条件挂在 `ApplyModifiedProperties()` 的返回值上**，而不是无脑每帧发：
  浏览器的 `OnGUI` 每帧都会 Apply 一次，无条件广播等于每帧重建一次全部候选源。
- 没有反过来让素材浏览器同时写场景组件 —— 那违背「配置进资产」的第一铁律，
  而且场景一重建就白写。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Editor/VNAssetLibraryEvents.cs`（新） | 「素材库改了」的静态广播（含订阅方必须退订的说明） |
| `Editor/VNScenarioEditorWindow.cs` | `RefreshSources()` 改按覆盖语义取数 + `LoadGameConfig()` / `PickLibrary<T>()` + `OnEnable/OnDisable` 订阅退订 + `OnAssetLibraryChanged()` |
| `Editor/VNAssetBrowserWindow.cs` | 所有写入点收敛到 `Apply()`，改动时广播 |
| `Editor/VNGameConfigEditor.cs` | 所有写入点收敛到 `ApplyAndNotify()` |

### 验证方法

- Unity 重编译零 Error / 零 Exception。
- 修复前：`VNGameConfig.backgrounds = 20`，剧本编辑器候选 = 13，
  缺 `scroll1 scroll2 scroll3 scroll4 scroll5 test1 test2`。
- 修复后：候选 = 20，上述 7 个全部出现，`test1` / `scroll5` 命中检查为 True。
- 手动 `RaiseChanged()` 广播一次，订阅方重建候选无异常。

## 一二三、`hideHUD` 登记进剧本编辑器（2026-08-29，分支 `agent/hidehud-schema`）

### 问题

`hideHUD`（隐藏对话框 + 快捷功能条 + 属性 HUD + 日历）**运行时早就能用**，
`VNScriptParser.Keywords` 和 `VNScriptRunner` 的 case 都在，
但**编辑器里搜不到**：行首命令按钮的分类菜单、打字换命令、`Ctrl+E` 命令面板
都列不出它，只能自己在文本里手打命令名（还得记住大小写是 `hideHUD`）。

### 根因

编辑器的命令候选表**从 `VNScenarioSchema.Commands` 现场生成**
（`VNCommandSearch` 里那句「加新命令不用回来登记」说的就是这条链路），
而当初加 `hideHUD` 时只改了 Parser + Runner，**漏了 Schema 这一步**
（vn-new-command 清单第 5 步）。

顺带说明为什么它以前还能正常显示：`VNScenarioDoc` 判定「这行是不是命令」用的是
`VNScriptParser.CommandKeywords`，不是 Schema，所以已有的 `hideHUD` 行会被认成
`VNRowKind.Command` 正常保留，只是 `VNScenarioSchema.Find()` 返回 null、
没有中文名也没有提示 —— 表现为「能存在但列不出来」。

### 做了什么

- `VNScenarioSchema` 里 `ui` 之后补一条 `Add("hideHUD", "Scene", hint)`，**零参数**
  （同 `sakura` / `return` 那种无参命令），hint 写明「只能关 + 玩家按键恢复」。
- `VNScenarioEditorWindow.CommandTranslations` 补 `{ "hideHUD", "隐藏界面" }`，
  行首按钮显示成 `hideHUD（隐藏界面）`。

### 技术决策与取舍

- **只做登记，没有顺手加 `hideHUD on|off`。** 加 off 等于新增一条「剧本可主动恢复界面」
  的语义，那就要考虑隐藏状态进不进存档快照（vn-save-compat 三处同步），
  与本次「让编辑器搜得到」的目标无关，留作后续需求。
- **分类归 `Scene` 而不是 `FX`**：它和 `ui` / `portrait` 一样是界面开关，不是画面特效，
  放 `Scene` 与 `ui` 相邻。
- 大小写保持 `hideHUD` 原样：`VNScriptParser.Keywords` 是**大小写敏感**的 `HashSet`，
  Schema 若写成 `hidehud`，编辑器插出来的行运行时会报「未知命令」。
  搜索本身是 `OrdinalIgnoreCase` 子串匹配，所以打 `hidehud` / `hud` 一样搜得到。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Editor/VNScenarioSchema.cs` | 新增 `hideHUD` 命令定义（Scene 分类，无参数） |
| `Editor/VNScenarioEditorWindow.cs` | `CommandTranslations` 补中文名「隐藏界面」 |
| `HowToUse.md` | 新增 `hideHUD` 小节 + 命令速查表补一行 |

### 验证方法

- Unity 重编译零 Error（只剩既有的 `FindFirstObjectByType` obsolete 警告）。
- 编辑器里打字搜 `hi` / `hud` / `hidehud` 都能出 `hideHUD（隐藏界面）` 候选。

## 一二四、hideHUD 分项隐藏 + 锁定隐藏，兼修快捷条藏不掉（2026-08-29，分支 `agent/hidehud-keep`）

### 问题

想要「大部分时间看不到 UI」的沉浸感，但 `hideHUD` 有两个坎：

1. **快捷功能条藏不掉**：对话框消失了，右下那排圆按钮还浮在半空。
2. **玩家一点鼠标 UI 就全回来了** —— 而推进台词就得点鼠标，所以隐藏根本维持不住。
3. 顺带：想只藏属性栏、留着对话框演台词，做不到（`hideHUD` 是一刀切全藏）。

### 根因（快捷条那条）

在 Play Mode 里实测（`_uiHidden=True` 时抓的状态）：

```
对话框    CanvasGroup alpha=0                     → 藏了
属性 HUD  _hudVisible=False, activeSelf=False     → 藏了
快捷条    Skin_QuickToolbar_New activeInHierarchy=True   → 没藏
          canvas overrideSorting=True order=41
```

`VNQuickToolbar` 的根物体挂着 `overrideSorting = true` 的**嵌套 Canvas**（为了压在对话框上一层），
而带 `overrideSorting` 的子 Canvas 会**打断父级 CanvasGroup 的 alpha 传播**——
`VNDialogueBox` 把自己的 `CanvasGroup.alpha` 归零，工具栏照画不误。
（属性栏其实一直藏得掉，截图里看到它是因为点过鼠标 UI 已经恢复了。）

### 做了什么

**① 修快捷条**：`VNQuickToolbar.SetVisible(bool)` 给自己的根加一个 CanvasGroup 单独关，
由 `VNDialogueBox.SetInterfaceVisible` 调用。**只在「隐藏界面」这条路上联动**，
`Show()` / `HideBox()` 的淡入淡出不动它 —— 没台词时工具栏仍在是有意的（那时也能存档）。

**② 状态从 `bool _uiHidden` 改成按部件 + 锁定**：新增 `VNUiParts`（Flags 枚举：
Dialogue / Stats / Calendar，Dialogue 含快捷条）与 `VNUiPartsUtil`（token 解析 + 存档字符串互转，
剧本名与存档名共用一张表，永不分家）。Runner 侧 `_uiHiddenParts` + `_uiHideLocked`。

**③ 命令扩成** `hideHUD [off] [keep] [dialogue|stats|calendar|all]…`：
参数**按 token 分类而不是按位置**，所以 `hideHUD keep stats` 和 `hideHUD stats keep` 等价，
编辑器里三个下拉留空也不会错位。`keep` = 锁定隐藏：Update 里那段「隐藏后第一次输入只恢复界面」
的拦截**不再生效**，玩家点击照常推进台词，界面一直藏着，直到剧本 `hideHUD off`。

**④ 存档三处同步**（vn-save-compat）：`VNSaveData.uiHidden`（字符串 `"dialogue,stats"`，
旧存档缺省空串 = 界面全开）；`SaveTo` **只存锁定隐藏**；`LoadFrom` / `RebuildStateBefore`
之后 `RestoreUiHidden()`。

### 技术决策与取舍

- **只有 keep 进存档。** 普通隐藏是「玩家一碰就还原」的瞬态，存了会变成
  「读档后界面莫名其妙全没了」，玩家只会以为游戏坏了。
- **锁定是整体状态，不是按部件的。** 混着写（keep 藏属性栏 + 普通藏对话框）时以最后一条为准——
  否则「要不要拦玩家的输入」根本说不清，而那是个二选一的全局问题。
- **Dialogue 与快捷条绑成一项**，不给快捷条单独的目标名：拆开会出现
  「台词没了但存档按钮还浮着」的半吊子画面，那正是这次要修的 bug 本身。
- **`hideHUD on` 不认**（会告警）：`on` 到底是「开启隐藏」还是「界面开」有歧义，
  统一只用 `off` 表示恢复。
- 事件模块期间的养成 HUD 隐藏改走 `ApplyGameplayHudVisible(allowed)`：
  事件结束时按 `_uiHiddenParts` 恢复，不会把剧本藏起来的又翻出来（老代码是 `!_uiHidden` 一刀切）。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Script/VNUiParts.cs`（新） | `VNUiParts` Flags 枚举 + `VNUiPartsUtil`（token / 存档字符串互转，认中文别名） |
| `Script/VNQuickToolbar.cs` | `SetVisible()`（自带 CanvasGroup，含 overrideSorting 打断 alpha 传播的说明） |
| `VNDialogueBox.cs` | `SetInterfaceVisible` 联动工具栏 |
| `Script/VNScriptRunner.cs` | `_uiHiddenParts` / `_uiHideLocked`、`SetUiHidden()`、`ApplyUiHidden()`、`ApplyGameplayHudVisible()`、`ParseHideHudArgs()`、`RestoreUiHidden()`、Update 锁定分支、存档读写、调试重建 |
| `Script/VNSaveSystem.cs` | `VNSaveData.uiHidden` |
| `Editor/VNScenarioSchema.cs` | hideHUD 三个位置参数格（同一张候选表） |
| `HowToUse.md` | hideHUD 小节重写 + 速查表 |

### 验证方法

Unity 重编译零 Error；Play Mode 里逐步调 `SetUiHidden` 抓状态：

| 步骤 | 对话框 alpha | 快捷条 alpha | 属性栏 | 日历 `_visible` |
|---|---|---|---|---|
| hide all | 0 | **0**（修复前是 1） | False | False |
| show all | — | 1 | True | True |
| keep stats | — | 1 | **False** | True |
| + dialogue | — | **0** | False | True |
| off stats | — | 0 | **True** | True |
| 全恢复 | — | 1 | True | True |

（日历走 `_dirty` 延迟一帧刷新，所以同帧只能看 `_visible`；
`VNUiPartsUtil.Parse` 的中英/大小写/中文别名与 token 往返也单独跑过。）

---

## 一二五、camseq 缩放模式 `mode:`：背景与立绘可以不一起缩放（2026-08-30，分支 `agent/camseq-zoom-mode`）

### 起因

用户问：设 zoom 时背景和立绘一起被拉近拉远，这是标准做法吗？

**是标准的，而且是最常用的那一种** —— 演出手册第 1 章表格第一行「トラックアップ（TU）推镜」的
2D 实现方式就写着「背景 + 立绘同步等比放大」。camseq 缩放的是 `ZoomRoot`，
`LayerBack / LayerMid / LayerFront` 全在它底下，所以一起动，没做错。

但同一张表把「缩放」拆成了**三种手法**，原来只实现了第一种：

| 手法 | 谁在缩放 | 语义 |
|---|---|---|
| TU / TB 推镜·拉镜 | 背景 + 立绘一起 | 通用推拉，情绪升温 / 场面收束 |
| ズームイン / アウト | **只有立绘**，背景不动 | 强调某人的**反应**、突发惊愕 |
| ドリーズーム 眩晕变焦 | **只有背景**，立绘不变 | 世界观崩塌，全篇 1~2 次 |

而且还有第四种情况，是「动了但不像镜头」的真正原因：**等比缩放在几何上是「数码变焦」，
不是真的推镜**。真镜头推近时近处的人放大得比远景快，所有层同一个倍率，观感就是
「一张照片被放大」。手册 1.3 说「速度差是伪 3D 的全部秘密」，说的就是这件事。

### 做法：`camseq mode:both|depth|bg|char`

写在 **camseq 行**（整段一个模式，不逐点）。逐点切换会让立绘尺寸在两点之间跳变，
而且手册的语义本来就是「一段镜头 = 一种手法」。

| mode | ZoomRoot | 立绘额外倍率 | 对应手法 |
|---|---|---|---|
| `both`（默认，可省略） | zoom | 1 | TU / TB 推拉镜 |
| `depth` | zoom | `1+(zoom-1)×0.5` | 有纵深的推拉镜（速度差伪 3D） |
| `bg` | zoom | `1/zoom` | 眩晕变焦 |
| `char` | **1（背景纹丝不动）** | zoom | 变焦推·只放大立绘 |

四种是同一条公式的四组取值，实现是一份代码：`VNCamera.CharacterScaleFor(mode, zoom)`
与 `ContainerZoomFor(mode, zoom)` 两个**静态**方法，运行时与编辑器预览共用
（不共用的话预览和实机迟早对不上，这是老教训）。

`mode:char` 下背景**连平移都不做**：zoom=1 时 `ComputeOffset` 仍会给出一个 overscan 级
（60px）的偏移，那点位移会让「背景纹丝不动」的语义打折，所以直接置零。

视差系数 k **写死 0.5，不做逐点覆盖**（`VNCamera.DefaultDepthRatio`，组件上可全局调）。
手册 13 章：「同一部作品内部保持一致比数值本身正确更重要」。

### 关键坑一：立绘缩放倍率必须分通道（否则说一句话尺寸就跳回去）

`VNImageEffectController._scaleMultiplier` 原本是**单一 float**，而
**`VNSpeakerHighlight` 每句台词都在写它**（说话者 1.03 / 旁听者 0.97）。
镜头如果写同一个字段，症状是：**推完镜头，玩家点下一句台词，说话者高亮一写倍率，
立绘尺寸当场跳回去** —— 与当年调色被六方共用踩的是同一个坑（见 `SetGrade` / `VNGradeLayer`）。

改法照抄调色那套，但按需只加一个通道：

```
_scaleMultiplier      说话者高亮 / 出场动画 / 手动（老 API DOScaleMultiplier）
_camScaleMultiplier   运镜通道（只由 VNCamera 写，新 API DOCamScaleMultiplier）
CurrentBaseScale = _origScale × _scaleMultiplier × _camScaleMultiplier
```

两个通道**共用一条补间** `_scaleTween`，后写的杀掉前一条 —— 两条 `DOScale` 同时跑在
同一个 Transform 上会互相打架，而合并目标值本来就是从两个通道算出来的，杀掉旧的没有损失。
另外补了 `duration ≤ 0` 直接写 localScale 不补间（瞬切段用）。

**顺带修掉一个既有 bug**：`VNCamera.DollyZoom()` 原本直接写 `DOScaleMultiplier(1/zoom)`，
埋着同一个问题，只是它是一次性名场面用、且 `dollyCharacters` 默认多半是空的所以没被发现。
现已改走运镜通道。

### 关键坑二：`both` 下是空转，所以还原只能在模式切换点做

`ApplyCharacterZoom` 在 `both` 下直接 return —— 否则每个路径点都要给每个立绘起一条补间，
而那条补间会顺带把说话者高亮的补间打断。代价是「从非 both 切回 both 时立绘倍率不会自己还原」，
于是所有模式切换收口到 `SetMode(mode, resetDuration)`，只在这一处做还原：

```
SetMode(both) 且当前不是 both → ResetCharacterZoom() 再切
```

`camcut` / `camto` 语法里没有缩放模式概念，**公开的 `Cut()` / `GoTo()` 一律 `SetMode(Both)`**，
不继承上一段 camseq 的模式（不这么做的话，`camseq mode:char` 之后紧跟一条 `camcut`，
背景不会缩放而且立绘一直是放大的，极难联想到原因）。camseq 内部叠化段要带模式，
另走私有的 `CutWithMode()`。`DollyZoom()` 则登记成 `_mode = Bg`，下一条 both 运镜自动帮它还原。

### 存档：不用动

camseq 的镜头状态**根本不进存档**（`VNSaveData` 里一个 cam 字段都没有），
调试重建也明写「动画路径状态不做推断，回到默认镜头」（`VNScriptRunner.RebuildStateBefore`），
走的是 `SnapReset()`。立绘的运镜倍率跟 camseq 同生命周期，在 `SnapReset()` / `ResetCamera()`
里一并还原即可，**不用走 vn-save-compat 的三处同步**。持久的只有 `camcut` / `camto`。

### 参与的立绘：全部在场角色，自动维护

`VNCamera.dollyCharacters` 从「手填的 Dolly Zoom 补偿列表」改成「参与运镜缩放的立绘」，
由 `VNStage.RefreshRegistries()` 在角色进出场时调 `SetCharacterTargets()` 自动维护 ——
与 `VNMoodGrading.SetCharacterTargets` 同一时机、同一份 `characterFx` 列表。

选全部在场而不是「只缩点名的那个」，是因为同一层的人应该一起动，否则两个人站一起会一大一小。

**已知边界**：camseq 播放**中途**才出场的角色不会带上当前的运镜倍率
（序列构建时它还不在列表里）。camseq 是同步阻塞命令，正常写法下不会发生。

### 编辑器：预览与运行时同一份公式

镜头编排窗口工具栏加「缩放模式」下拉（带四种手法的 tooltip）。画布预览：

- `CamState` 增加 `charScale` 字段，**`CharScale` 属性把 0 兜底成 1** ——
  旧代码里有几处直接 `new CamState{offset=..,zoom=1}`，不兜底立绘会被画成 0 尺寸直接消失
- `TargetState()` 调运行时那两个静态方法算容器 zoom / 立绘倍率 / 偏移
- 拖进度条的插值里 `charScale` 也跟着 lerp，否则中途那几帧立绘尺寸是错的
- 立绘位置仍跟容器走（它在 ZoomRoot 底下），**只有尺寸**乘额外倍率；没有立绘时的占位框同理

剧本编辑器那边**零改动**就跟上了：`mode` 加进 `VNCamseqText.HeaderKeys`（排最前，
它决定整段观感，读剧本时该第一眼看到）+ Schema 的 `Kw("mode", ...)`，
header 参数的读写、存为预设、镜头窗口双向绑定全都是遍历 `HeaderKeys` 的通用逻辑。
Parser 也不用改 —— kwarg 是通用解析，`mode:depth` 自动落进 `cmd.kwargs`。

### 素材层的现实约束（顺带查出来的）

背景素材 `Assets/Art/Images/Background/` 全是 **1484×900**，比 1920×1080 画布还小，
**已经被放大 1.29 倍在用**。zoom 1.8 等于把 1484px 的图放到 2.33 倍，会明显发糊。
立绘是 832×1216，纵向富余得多 —— 所以 `mode:char` 不只是演出选择，也是画质选择。
（用户表示背景可以重出到 2560 以上，届时推镜倍率上限可以放宽。）

另外模板里的 zoom **不要低于 1**：`ComputeOffset` 会钳制位置，但 zoom<1 会露出画布边缘，
「拉远」类一律以 1.0 为底。

### 改动文件

| 文件 | 改了什么 |
|---|---|
| `VNCamera.cs` | `VNCamZoomMode` 枚举、`CharacterScaleFor` / `ContainerZoomFor` 两个静态公式、`depthRatio`、`SetMode()`、`ApplyCharacterZoom()` / `ResetCharacterZoom()`、`SetCharacterTargets()`、`CutWithMode()`、`PlayPath` / `PlayPathCo` 加 mode 参数、`DollyZoom` 改走运镜通道 |
| `VNImageEffectController.cs` | `_camScaleMultiplier` 通道、`DOCamScaleMultiplier()`、`ResetCamScaleMultiplier()`、`ApplyScaleMultipliers()` 合并 + 共用 `_scaleTween` |
| `Script/VNStage.cs` | `RefreshRegistries()` 里维护 `vnCamera.SetCharacterTargets()` |
| `Script/VNScriptRunner.cs` | `CamseqCo` 解析 `mode:`（认不出告警并退回 both） |
| `Editor/VNScenarioSchema.cs` | `CamZoomModes` 候选表 + camseq 的 `Kw("mode")` + 命令说明 |
| `Editor/VNCamWaypoint.cs` | `HeaderKeys` 加 `mode`（排最前）、`VNCamseqText.ModeOf()` |
| `Editor/VNCamseqEditorWindow.cs` | `_zoomMode` 窗口状态（进域重载存活组）、工具栏下拉、`CamState.charScale`、`TargetState` / 插值 / 立绘绘制 / 占位框、生成与解析文本 |

### 补丁：预览的另外两条绘制路径也要跟（同日，分支 `agent/camseq-preview-charscale`）

改完当天就被发现：切到 `mode:char` 后两个预览**完全没反应**，跟实际画面对不上。
探针（反射读窗口实时状态）确认：

```
_zoomMode=Char  _cameraView=False(整图)  _scrub=5
场景预览临时立绘 scale=(1,1,1)   ← 没带 mode 的缩放
ZoomRoot         scale=(1,1,1)   ← char 模式下本来就该是 1
```

原因是立绘倍率只接进了「镜头视角」一条路径，**整图模式与场景预览两条漏了**：

1. `DrawCanvas` 在整图模式下硬构造 `new CamState { offset=zero, zoom=1f }` →
   `charScale` 是 0、被 `CharScale` 兜底成 1，立绘永远画基准大小
2. `ApplySceneState` 只写 `ZoomRoot`，而 **char 模式下 ZoomRoot 恒为 1** →
   整个 Game 视图纹丝不动，临时立绘的 localScale 一直是 1

`char` 之所以症状最重：背景不动，画面里唯一会变的就是立绘，而它恰恰是没被预览的那个。

**修法的关键认识：整图模式不跟镜头走（永远显示整块画布），但立绘的额外倍率要照跟**
——`mode` 改的是立绘自己的 localScale，那是**画布上的真实内容**，不是取景。
（`bg` 模式下整图里立绘会画小到 `1/zoom`，看着奇怪但正确：取景框只有 `1/zoom` 那么大，
心里一放大，`0.625 × 1.6 = 1.0`，正是运行时「立绘尺寸不变」的结果。）

场景预览那边则给临时立绘直接写 localScale（它们只有裸 RectTransform，
没有运行时的 `VNImageEffectController` 运镜通道），并在 `ApplyStageToScene()`
末尾补一次——否则换绑定行重摆舞台的那一帧立绘会闪回原大小。

顺带两条 char 模式专属的交互修正：

- **取景框整套跳过**：char 模式下每个点的取景框都等于整块画布，四个完全重叠，
  序号牌会挤成一坨还挡住立绘（截图里 1/2/3/4 全叠在左上角就是这个）。
  改成不画，在画布角上标一句「mode:char —— 镜头不动，只有立绘在缩放」
- **加 HelpBox 说明「目标点不生效」**：char 模式下背景连平移都不做，
  所以路径点写 `星野结衣:head` 也不会推到脸上，只有 zoom 有用（zoom 1.6 = 立绘 1.6 倍）。
  这一条不写出来，用户一定会以为是 bug

### 没做的

- **模板**：手册那批场景配方模板（约 45 条）留到下一轮，届时可以直接带上 `mode:`
- **Lint**：`mode` 值合法性没加校验规则 —— 目前 camseq 一条 lint 规则都没有，
  而两个编辑器都是下拉框、运行时也会告警，先不为它建一整个规则类别
- **`{char2}` 第二占位符 / 路径点 `dutch:` 荷兰角**：手册点名过，留作后续

---

## 一二六、亲密互动小游戏模块：光标变道具，摸角色部位推进阶段（2026-08-30，分支 `agent/interaction-minigame`）

### 需求

用户要一个新的互动小游戏：鼠标变成某种图标（一只手、某个道具），玩家用它点/摸角色的
特定部位（摸头、摸脸…），摸到一定程度角色给出反馈 —— 换表情、说特定台词、播语音音效；
光标图标本身还要有持续摆动之类的动画。

素材是 `Assets/Art/InteractionMiniGame/item1~4.png`（两只手 + 两个道具），
试验角色星野结衣。定位是成人向的亲密互动，本次做的是**系统框架**，
具体台词/语音内容由作者自己填。

问了三轮把岔路定死：event 事件模块承载（但中途要能播台词/语音/特效）／归一化区域 +
编辑器画框工具／单击与按住拖动都要／道具由剧本给清单玩家在其中选／反馈用字段 +
内嵌剧本行混合／全局兴奋度 + 每部位独立累计 + 禁忌部位／进度条可见 + 悬停高亮／
表情 + 叠加层／部位框按每张立绘各一套带继承／随机语音池。

### 文件改动

**新增（运行时）**
- `VNTouchZoneDef.cs` —— 部位区域资产。归一化坐标（与 `markAnchor` 同语义）；
  基准一套 + 按立绘/表情覆盖（同 id 覆盖、新 id 追加、未提到的继承、`replaceAll` 完全不继承）；
  命中数学 `Contains` / `Pick` 是纯静态的，编辑器与运行时共用同一份。
- `VNInteractionDef.cs` —— 一场互动的全部规则：道具（含光标动画参数）、阶段、
  部位×道具反馈池、禁忌解禁阶段、结束条件、三种结果名、flag 前缀。
- `VNTouchScore.cs` —— 判定数学，纯逻辑无 MonoBehaviour，可单测。
- `VNTouchCursor.cs` —— 道具光标：跟随、待机摆动、按住震动、速度倾斜、悬停发光。
- `VNCharacterOverlay.cs` —— 立绘情绪叠加层（潮红/汗/泪），多层共存、强度 0~1。
- `VNInteractionModule.cs` —— 事件模块本体。

**新增（编辑器）**
- `VNInteractionInstaller.cs` —— 增量装机 + 缺资产时铺一套示例。
- `VNTouchZoneEditorWindow.cs` —— 在立绘上拖框画部位。

**修改**
- `VNScriptRunner` —— 新增 `RunInlineCo(lines)`；`overlay` 的 Dispatch 与静默重放。
- `VNStage` —— `ActiveCharacter.overlay` 字段、挂载、`SetOverlay()`、快照存取。
- `VNCharacterDef` —— `overlays` 列表。
- `VNSaveSystem` —— `CharSave.overlays`。
- `VNScriptParser` / `VNScenarioSchema` / `VNScenarioEditorWindow` / `VNScenarioLinter`
  —— `overlay` 命令的全链路登记。
- `VNTextureImportDefaults` —— Sprite 自动导入白名单加 `Art/InteractionMiniGame/`。
- `Resources/VNLocale/ui.{zh,en,ja}.txt` —— `interact.*` 三条 UI 字符串。

### 技术决策与取舍

**① 模块刻意破「不碰舞台」的铁律（先例 VNAiTalkModule）。**
玩法本身就是对着舞台上的立绘操作，自绘一套立绘等于要把眨眼/口型/色调匹配/出场动画
全部重接一遍。边界收紧为「只碰表情与叠加层」，且正常结束 / ESC / `CancelForDebug`
三条路径都还原原表情。

**② 不铺全屏暗幕。** EventLayer 排序 60 在对话框 40 之上，铺了暗幕台词就看不见了 ——
而这个玩法全程都要角色说话。HUD 缩在左下角、道具栏放右侧竖排（底部是对话框的地盘）。

**③ 阶段只升不降。** 允许回退的话，玩家一停手兴奋度衰减，表情就会在阈值边界反复横跳。
衰减只把数值往回拉，不动阶段。

**④ 光标不用 `Cursor.SetCursor`。** 硬件光标做不了摆动、按住震动、速度倾斜、
悬停发光这四件事，而它们正是手感的来源。代价是必须自己保证退出时把系统光标还回去：
`Dispose()` 在 Finish / CancelForDebug / OnDestroy / OnDisable **四处**都调 ——
漏一条路径玩家的鼠标指针就永久消失了。发光走 `VN/Additive` 材质的 `_TintColor`（HDR），
不写顶点色（uGUI 顶点色被钳到 1，Bloom 抓不到）。

**⑤ 阻塞台词复用 Runner 的 SayCo。** blocking 反馈把台词拼成一行丢回 `RunInlineCo`，
等打字完、等玩家推进、Auto/Skip 全都是现成的，不在模块里重写一套推进逻辑。
期间 `_blocked=true` 不吃抚摸输入 —— 否则玩家点一下既推进对话又顺手摸了一把。
已经在播时新来的一条降级为非阻塞：排队会让台词堆成一串，得连点好几下才能继续摸。

**⑥ `RunInlineCo` 的白名单是必需品。** 调用方（事件模块）此刻正被主协程 yield 等待着，
让 `jump` / `choice` / `call` / `return` / `event` / 存档类跑起来会搅乱 `_index` 与
`_callStack`，症状是事件结束后剧本跳到莫名其妙的地方。演出类命令一律放行。

**⑦ 叠加层不做成表情。** 「表情 × 潮红三档」是乘法爆炸，每个组合都得画一张完整立绘；
叠加层是加法，一张潮红图配所有表情，强度还能连续补间而不是跳变。

**⑧ 部位框按每张立绘各一套但带继承。** 一个角色一套的话，换一张构图不同的立绘
（坐下/躺下/近景）就全错位；每张都从头画又太费。折中是基准 + 只写差异的覆盖。

### 修复记录

- **部位框在互动结束后留在角色脸上。** 框可视化层挂在 `_char.rect`（立绘）底下 ——
  必须如此才能跟着立绘一起缩放位移 —— 但那样它就不在模块的子树里，模块销毁时不会被回收。
  修法：`DestroyZoneOverlay()` 在 Finish / CancelForDebug / OnDestroy 三处显式删。
  这是破铁律要自己付的账：凡是挂到舞台上的东西，三条退出路径都得亲手清干净。
- **结束反馈若是 blocking 会被拦腰打断**（模块 0.7 秒后就 Done 并销毁）。
  改为结束反馈强制非阻塞；结算台词应写在剧本的「* 结果行」下面。
- 新文件一律用 `FindAnyObjectByType`：`FindFirstObjectByType` 在 Unity 6.5 已是
  error 级弃用告警（存量文件里还有一批，本次没动）。

### 验证方法

- 从 `InteractionDemo.vn.txt` 第 19 行播放，Lint 对该文件 0 问题。
- 反射模拟抚摸：摸头 60 次 → 兴奋度 120 / 阶段 2 / 表情「微笑」→ 推到阶段 3
  触发 `autoEndOnTarget` → 走 `* 满足` 分支（对话框确实出现该分支台词，立绘停在害羞）。
- 阶段 0 摸禁忌部位（胸，解禁阶段 2）3 次 → 拒绝数 3、兴奋度 0、触发上限 → 走 `* 拒绝`。
- 跨阶段时 `_blocked=True`，抚摸判定确实被暂停。
- 单点调 `RunInlineCo`：`fx shockwave light` / `camera pushin` 正常执行，
  `jump` / `choice` 被白名单挡下并告警。
- `VN/Additive` 存在且有 `_TintColor`；互动结束后 `Cursor.visible == True`。
- 对故意写错的探针剧本，Lint 准确报出 3 条 overlay 警告。

### 剧本写法

```
show 星野结衣 at:center expr:默认
event interact vs:星野结衣 id:初次抚摸 items:手,羽毛 time:120 flag:抚摸 zones:on
* 满足 -> 满足
* 普通 -> 普通
* 拒绝 -> 拒绝
```

`zones:on` 把部位框画出来（开发调试用，正式剧本删掉）。`* 拒绝` **必须接住**，
否则玩家惹毛角色会静默跳过。成绩写 flag `<前缀>_兴奋度 / _阶段 / _拒绝数 / _<部位>次数`。

装机：Tools → VN Effects → 场景装机 Install To Scene → **亲密互动 Interaction Module**。
画部位框：Tools → VN Effects → 预览 Preview → **部位区域编辑器 Touch Zone Editor**。

---

## 一二七、互动内嵌演出的坐标占位符与结束收尾（2026-08-30，分支 `agent/interaction-liquid`）

### 起因

用户问：摸到某个阶段或某个部位时，能不能用 `liquid` 命令在**特定部位或坐标点**喷特效？

一半已经能做 —— 内嵌剧本行（一二六章）本来就放行 `liquid`，场景也早装过液体层，
写 `liquid splash x:0.5 y:0.55 type:water` 就有效果。

但**写死坐标是个陷阱**：角色一移位、镜头一推拉、换张构图不同的立绘，喷的位置就飘了。
而「在特定部位」真正想要的是**摸哪儿喷哪儿**。

### 做法：占位符 + 结束收尾

**① 坐标占位符**（`VNInteractionModule.ExpandPlaceholders`）

| 占位符 | 含义 |
|---|---|
| `{cx}` `{cy}` | 光标当前位置 |
| `{zx}` `{zy}` | 当前部位中心 |
| `{px}` `{py}` | 角色中心 |
| `{prog}` `{stage}` `{zone}` | 整场进度 0~1 / 阶段序号 / 部位 id |

坐标一律是 **viewport 比例 0~1、左下角为原点** —— 与 `liquid` 的 `x:` `y:` 同一套
（它内部走 `Camera.ViewportToWorldPoint`），所以能直接写 `liquid splash x:{cx} y:{cy}`。
换算链是「立绘归一化 → `TransformPoint` → `WorldToScreenPoint` → 除以屏幕尺寸」。

放在模块层而不是给 `liquid` 命令加参数：部位是互动系统的概念，`liquid` 不该知道它；
而且占位符对**所有**内嵌命令都生效，将来 `fx`、粒子要坐标时照样能用。

**② `cleanupLines`（结束收尾剧本行）**

`spray on` / `wet on` 这类持续状态开了不会自己停。收尾行在**四条退出路径**上都执行：
正常结束 / 玩家点结束 / ESC / 调试中断。关键实现细节：**协程挂在 Runner 上而不是模块上** ——
模块马上就要被销毁，挂自己身上的协程会被拦腰打断，于是水就一直喷下去了。

### 修复记录

- **`{cx}{cy}` 取到 (0,0)，水喷到屏幕左下角。** 原先读的是 `_lastMouse`，
  那个只在 `Update` 里写，而阻塞台词期间 `Update` 提前 return，取到的是过期值甚至初始值。
  改成直接读 `Mouse.current.position` 实时值。
- **水花被推迟到玩家点击之后才出现。** `FeedbackCo` 原本是「先说台词 → 再跑内嵌行」，
  台词若是 `blocking` 会一直等推进，演出就脱节了。改为**演出先于台词** ——
  演出是「刚发生的事」，台词是随后的反应；而且占位符在这一刻展开，
  鼠标还停在玩家刚摸的位置上。

### 验证方法

- 反射调 `ExpandPlaceholders`：`{zx},{zy}`=0.507,0.342（颈部）、`{px},{py}`=0.503,0.398
  （角色中心）、`{prog}`=0.213、`{stage}`=1、`{zone}`=颈，全部正确。
- `VNLiquidSplash.Burst` 后粒子数 212，确认液体层在本场景可用。
- 摸到阶段 2 触发 `liquid splash x:{zx} y:{zy}`，截图能看到水珠落在脖颈/肩部一带，
  且台词是在水花**之后**出现的。

### 示例资产

`初次抚摸` 里写全了四种用法：摸「颈」就喷（`{cx}{cy}`）／阶段「害羞」喷一下（`{zx}{zy}`）／
阶段「情动」持续喷 + 镜头水渍（`amount:{prog}`）／`cleanupLines` = `liquid spray off` + `liquid dry`。
