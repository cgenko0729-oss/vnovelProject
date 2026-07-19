---
name: vn-save-compat
description: 新增任何运行时状态（开关、皮肤、镜头、flag 之外的舞台状态）时的存档与调试重建兼容清单：VNSaveData 字段、RestoreSnapshot、RebuildStateBefore 三处同步。Save/load compatibility checklist (VNSaveData, snapshot, debug rebuild).
---

# 存档 / 调试重建兼容清单

## 何时用我
任何改动引入了「运行中会变、读档后应还原」的状态时：新 fx 开关、UI 皮肤、
镜头状态、音频循环、角色附属状态等。**这是本项目最容易漏的一环。**

## 三处必须同步（漏一处 = 隐性 bug）
1. **VNSaveData 加字段**——必须给初始化器**默认值**（JsonUtility 反序列化旧档时
   字段缺失靠默认值兜底，否则旧档炸）。
2. **VNStage 快照存取**——存档时写入快照、`RestoreSnapshot(data, ...)` 里还原。
3. **RebuildStateBefore() 静默重放**——编辑器「从选中行播放」按文件顺序重建前置状态，
   新命令/新状态要能被静默重放（不播动画、不出声、直接置终态）。

## 哪些状态进哪里（别放错）
| 状态 | 归宿 |
|---|---|
| 剧情变量/任务/属性/道具/日程 | VNFlags（自动进存档，勿另开存储） |
| 舞台可见状态（背景/角色/fx/天气/mood/镜头/BGM/循环SE/对话选择皮肤） | VNSaveData 快照 |
| CG 解锁 | VNCgUnlocks 全局 JSON（与存档槽**分离**，勿用 flags 存解锁） |
| 系统菜单全局皮肤 | VNGameConfig 配置（**不进存档**，无剧本命令） |
| 事件模块内部进度 | 不进存档（事件中禁止存档，结果落 flags） |

## 已知边界
- 存档只在台词行停稳后可用（演出/事件进行中禁止）——设计如此。
- 调试重建不预播：台词、等待、转场、一次性 SE、voice。
- choice/jump/if 的历史路径无法由行号唯一推断，重建按文件顺序近似并警告；
  别试图“自动推断玩家选择”。

## 权威参考
- WhatAiDo.md 十九（存档系统）、三十二（调试重建）、三十五（20 槽界面）、五十九（快速存读档）
- ProjectCodeGuide.md 菜谱一第 3~4 步
