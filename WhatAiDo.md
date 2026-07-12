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

## 十、问题修复记录

### 修复 1：`Particle Velocity curves must all be in the same mode`（2026-07-12）

**现象**：运行时报错。
**原因**：`VNAmbientParticles.cs` 的 velocityOverLifetime 模块中，X/Y 轴用了
`MinMaxCurve(min, max)`（双常数随机区间模式），Z 轴却写成 `vel.z = 0f`
（隐式转换为单常数模式）。Unity 要求同一速度模块的三条曲线**模式必须一致**。
**修复**：三个粒子预设（Dust / Sparkles / Orbs）的 Z 轴统一改为
`new ParticleSystem.MinMaxCurve(0f, 0f)`，与 X/Y 保持双常数模式。
