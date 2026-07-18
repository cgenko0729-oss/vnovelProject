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

菜单 **Tools → VN Effects → Scenario Editor**。目标：不再手打关键字（消灭 typo），
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
  **Tools → VN Effects → Create TMP Font Asset**。生成持久化 TMP_FontAsset
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
  1. Tools → VN Effects → Create TMP Font Asset（生成预烘焙字体资产）
  2. Tools → VN Effects → Create Demo Scene / Create Script Demo Scene
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
- **编辑器工具** Tools → VN Effects → Localization：
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
- **用户操作**：Unity 里重新 Tools → VN Effects → Create Script Demo Scene
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
