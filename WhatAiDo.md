# WhatAiDo.md — 视觉小说 2D 图片特效系统 开发全记录

> 由 Claude (AI) 编写，记录本次开发的完整思路、计划、每一步做了什么、每个文件的作用与使用方法。
> 日期：2026-07-12

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

菜单 **Tools → VN Effects → Create Demo Scene**，全自动完成：

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
2. 菜单 **Tools → VN Effects → Create Demo Scene**
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
- **需要重新执行一次 Tools → VN Effects → Create Demo Scene** 让新物体进入场景。

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
- **需要重新执行 Tools → VN Effects → Create Demo Scene**。

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
- **需要重新执行 Tools → VN Effects → Create Demo Scene**。

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
**需要重新执行 Tools → VN Effects → Create Demo Scene**。

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
**需要重新执行 Tools → VN Effects → Create Demo Scene**。

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
**需要重新执行 Tools → VN Effects → Create Demo Scene**。

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
**需要重新执行 Tools → VN Effects → Create Demo Scene**。

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
- 新菜单 **Tools → VN Effects → Create Script Demo Scene**：自动创建
  两个角色定义资产（`Assets/VNEffects/Characters/亚里沙|小雪.asset`）、
  演示剧本 `Assets/Scenarios/Demo.vn.txt`（已存在则不覆盖，放心改）、
  VNStage（背景库 bg1..bgN 自动填充）+ VNScriptRunner，保存为 `VNScriptDemo.unity`。

### 16.5 使用方法

1. 菜单 Tools → VN Effects → **Create Script Demo Scene** → Play，
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

`Enter/空格/点击` 推进 | `H`/滚轮上滑 回想 | `A` 自动 | `S` 快进 | `F5` 快速存档 | `F9` 快速读档
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

### 23.1 窗口结构（Tools → VN Effects → Camera Sequence Editor）

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

Tools → VN Effects → Create Script Demo Scene 重建剧本场景（或直接 Play 旧场景，
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

**Tools → VN Effects → Character Visual Preview**

也可以在 Project 窗口选中任意 `VNCharacterDef` 后：

- 右键 **VN Effects → Open Character Visual Preview**；
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
