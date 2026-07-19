---
name: vn-new-effect
description: 加新特效/演出组件（发光、粒子、全屏运动、转场、镜头类）的硬约定与接线清单：程序化贴图、HDR+Bloom、材质实例、SetLink、fx 命令三件套、演示场景生成器重建。Add a new visual effect component (VNEffects, shader, particle, fx command, demo scene).
---

# 新特效/演出组件

## 何时用我
要加视觉演出组件：单图特效、粒子氛围、全屏运动、转场、镜头辅助、overlay 等。

## 硬约定（全项目通用，违反必出 bug）
- **发光 = 材质 HDR 颜色(>1) + Bloom（阈值 1.0）**；uGUI 顶点色被钳到 1，别走顶点色。
- **贴图程序化生成**：先查 `VNProceduralTextures` 有没有现成形状，没有再加生成函数，零美术依赖。
- **每张图独立材质实例**（VNImageEffectController 自动管理），不共享材质改参数。
- uGUI 自定义 shader 走传统 CGPROGRAM（Canvas 不经过 URP 光照），保留 UI 裁剪兼容。
- UI 不写深度缓冲 → 不能用真 DoF/深度后处理，模糊走 VNImageEffect 的 9-tap。
- 所有 Tween `SetLink(gameObject)`；**循环效果提供 Start/Stop 成对 API**。
- 粒子 velocityOverLifetime 三轴曲线**模式必须一致**（都用 `MinMaxCurve(min,max)`）。
- 运行时创建带 Awake 配置的组件：先 `SetActive(false)` 挂组件赋值再激活（见 VNAmbientParticles.Create）。
- 立绘缩放走「倍率」机制（`CurrentBaseScale = 原始 × _scaleMultiplier`，`DOScaleMultiplier`），
  别直接改 localScale，否则和呼吸/高亮打架。
- 文字一律 TextMeshPro + `VNFont.Asset`；输入一律新 Input System。

## 操作清单
- [ ] 参考同类组件写（组件分类见 CLAUDE.md「组件速查」表 / ProjectCodeGuide 八）
- [ ] 要接 `fx` 命令：VNStage 加引用 + `Fx()` 路由 + `_fxStates` + 快照，**三件套 + 存档一处不能少**
      （进存档细节走 [vn-save-compat]）
- [ ] 整屏运动类：确认挂在哪个容器层（SceneRoot/ZoomRoot/TiltRoot 各司其职，见 SetUpGuide 第 0/3 章），
      不要两种运动挤一层
- [ ] `VNEffectsDemoSetup` 生成器里创建/连线 → **Tools → VN Effects → Create Demo Scene 重建演示场景**
- [ ] 演示按键/提示文字更新（VNEffectsDemo.UpdateHint）
- [ ] CLAUDE.md 组件速查表加一行 + WhatAiDo.md 记录（见 [vn-doc-update]）

## 权威参考
- ProjectCodeGuide.md 八（演出组件库）+ 十（Shader）+ 菜谱五
- SetUpGuide.md 第 0 章（发光/容器层/排序三个核心概念）、第 5 章（overlay 排序表）
