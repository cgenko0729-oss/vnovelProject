---
name: vn-debug
description: 剧本/演出跑不对时的排错手册：先 Lint、从选中行播放的能力边界、编译验证技巧、常见报错速查入口。Debug VN scenario or effects (troubleshoot, lint, play from selected line, compile check).
---

# 剧本 / 演出排错

## 排错顺序
1. **先跑 Lint**（Tools → VN Effects → Lint Scenarios，Ctrl+Shift+L）——
   大半「跑到那行才发现」的错误它能提前抓，双击结果行直接定位。
2. 查 Console 报错的**行号/列号**，对照 HowToUse.md 十三章排查表（报错文案→原因→解法，最全）。
3. 定位到具体行 → Scenario Editor 选中该行 → **▶ 从选中行播放**复现。

## 「从选中行播放」能力边界
- 默认勾「重建前置状态」：按文件顺序汇总目标行之前的状态
  （背景/天气/氛围/BGM/循环SE/站位表情/portrait/FX/focus/flags/可确定镜头），
  **不预播**台词、wait、转场、一次性 SE、voice。
- choice/jump/if 的历史路径**无法由行号唯一推断**——按文件顺序近似并给警告；
  需要精确分支上下文时，改用「从存档快照启动」的思路，别自动推断玩家选择。
- 音频/Tween 残留由调试重建自动调 `VNAudio.ResetForDebug()` 清理——
  **只在编辑器中间行调试时用，别在别处手动调**。
- 未保存的编辑器文本也能调试（Bridge 走 SessionState）。

## 编译验证（Unity 没刷新 csproj 时）
临时把新 .cs 加进 `Assembly-CSharp.csproj` →
`dotnet build Assembly-CSharp-Editor.csproj --no-restore --nologo` → **还原 csproj**。

## 高频“不是 bug”清单
- 演出/事件进行中不能存档、事件中快捷键失灵 —— 设计如此。
- 改了 .txt 没生效 —— Unity 要拿回焦点重新导入，再 Play。
- 日历 HUD 不显示 —— 还没跑过 `time set`（`月份` flag 不存在自动隐藏）。
- choice 全部消失 —— 所有选项 `if:` 都不满足，留一个无条件保底选项。
- 场景报「未连线」 —— VNStage 会自动补线；仍报错就重建剧本场景
  （Tools → VN Effects → Create Script Demo Scene，配置在 VNGameConfig 不会丢）。

## 权威参考
- HowToUse.md 十三（排查表）+ 十二·五（校验器检查项全表）
- ProjectCodeGuide.md 菜谱六（调试）+ 十二（坑清单）；CLAUDE.md「从选中行播放」节
