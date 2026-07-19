---
name: vn-new-event-module
description: 写新玩法事件模块（小游戏/QTE/地图/战斗/排程类）：VNEventModule 子类、模块三铁律、注册表登记、结果行契约、调试清理。Create a gameplay event module (VNEventModule, event command, minigame, registry).
---

# 新玩法事件模块

## 何时用我
要加由 `event <id>` 驱动的玩法：小游戏、选择面板、QTE、战斗、排程等。

## 模块三铁律（违反会破坏存档/调试/快进）
1. 只操作自己的 UI 子树和 VNFlags，**不直接改舞台演出**
2. 计时用 unscaledTime，Tween 加 `SetUpdate(true)`（不受 Skip 快进的全局 timeScale 影响）
3. 所有 Tween `SetLink(gameObject)`（模块随时可能被销毁）

## 操作清单
- [ ] 新建 `class VNXxxModule : VNEventModule`，在 `OnLaunch(ctx)` 搭 UI
      （抄 VNQteModule 的 CreateImage/CreateText 辅助），结束调 `Done("结果名")`（只生效一次）
- [ ] ctx 可读：`eventId / stage / kwargs（Kw/KwF/KwI）/ outcomes（AcceptsOutcome 判断剧本声明的结果行）/ line`
- [ ] 场景生成器里建**禁用**的模板物体挂组件，`VNEventRegistry.modules` 登记 id
      （实例化到 EventLayer，Canvas 排序 60：ChoicePanel 45 之上、ScreenTransition 100 之下，
      所以可用全屏转场包裹进出事件）
- [ ] 长流程模块实现 `CancelForDebug()` 清理场外资源
- [ ] 不想每步进回想 → 用 RecordInBacklog 开关（WhatAiDo 七十一章）
- [ ] 写演示剧本 `Assets/Scenarios/XxxDemo.vn.txt`：
      `event <id> 参数:值` + `* 结果名 [flag:op] [-> 标签]`（结果行复用 choice 解析）
- [ ] 跑 Lint：结果名是**精确字符串匹配**，拼错静默走顺序继续（bad-event-outcome 只是警告）

## 边界（设计如此，不是 bug）
- 事件期间快捷键全禁、不可存档；模块内部状态不进存档 →
  与剧情通信只走 flags（例：战斗结束写 `战斗剩余HP` 供车轮战；地图写 `去过_<地点>`）。
- 属性影响玩法 = 从 flags 读（如 battle 的 patkstat/phpstat/pdefstat）；概率表写在剧本里而不是代码里。
- 轻量 3D：模块场外生成 3D 物体 + 专用相机渲到 RenderTexture 贴回自己 UI，OnDestroy 清干净。
  重型 3D 需先补注册表场景模式（additive 加载，四十四章评审结论，**尚未实现**）。

## 权威参考
- ProjectCodeGuide.md 六（玩法扩展层，含接口签名）+ 菜谱二
- WhatAiDo.md 四十一（架构规划）、四十二（P1 实现）、七十（plan/result）、八十一（battle）
- 现成范例：VNQteModule / VNMapModule / VNBattleModule / VNShopModule / VNPlanModule / VNResultPopupModule
