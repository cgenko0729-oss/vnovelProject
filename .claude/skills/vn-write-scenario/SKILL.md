---
name: vn-write-scenario
description: 写/改 .vn.txt 剧本时的语法要点、只写中文原则、flag/choice/if/call 规则、概率表写法、写完必跑 Lint。Write or edit VN scenario scripts (.vn.txt, DSL syntax, dialogue, choice, flag, jump, quest, stat).
---

# 写剧本（.vn.txt）

## 何时用我
新写或修改 `Assets/Scenarios/*.vn.txt` 剧本、设计分支/养成/日程流程时。

## 核心规则
- 剧本**只写中文**（本地化唯一真相，翻译走旁路表，见 [vn-localize]）。
- **逻辑标识符永不翻译**：event 结果行、角色 id、flag 名。
- 命令默认同步等待；行尾 `@` = 异步（演出叠加全靠它）。
- 台词行 = 等打字完 + 玩家推进；台词开头撞保留字（bg/show/wait…）时加说话者或 `：` 前缀。
- kwargs 值不能含空格；choice 选项行 `if:` 表达式不能含空格
  （独立 `if` 命令支持空格、`&&`/`||`/`!`、括号、整数算术）。
- 选项 flag 操作写 `flag:名+1` 这类 op 形式；写成 `flag:名 值` 会**静默失效**（lint 会抓）。
- 跨文件：`jump/call 文件::标签`；`chapter` 清调用栈、`jump` 保栈；子程序 `params` 必须紧跟 label，走不到 `return` 的路径是错误。
- **概率表写在剧本里**（内容而非逻辑）：`flag 骰 rand:1-100` + 阈值链 if；属性影响概率 = 分流到另一条阈值链。
- 跨文件循环记得**守卫 flag**（`if 月份==6 && !第2章已看 jump ...`，目标文件开头置 flag），否则 loop-risk 死循环。
- 存档点只在台词行——纯事件/跳转的长循环要保证隔一段有台词。

## 常用命令分类（详解见 HowToUse.md 四~六章 + 文末速查卡）
- 演出：bg / cg / show / hide / move / emote / camera / camseq / shake / weather / mood / fx / letterbox / transition / sakura / portrait / reset / wait
- 音频：bgm / se / voice / volume（均支持 vol:）
- 流程：label / jump / chapter / call / return / params / flag / if / choice
- 玩法：event（qte/map/battle/shop/plan/result）/ quest / stat / time / ui（皮肤切换）

## 写完必做
- [ ] **Tools → VN Effects → Lint Scenarios（Ctrl+Shift+L）**跑静态校验，错误清零，警告逐条判断
- [ ] 引用了新素材 id → 先按 [vn-add-assets] 登记，否则 unknown-* 警告
- [ ] 台词有增改 → 本地化 Extract（见 [vn-localize]）
- [ ] 改完 .txt 要让 Unity 拿回焦点重新导入，再 Play

## 权威参考
- HowToUse.md（全书，尤其 二~七、十二完整示例、十二·五校验器、十三排查表）
- Demo.vn.txt 文件头语法速查；教学剧本：BattleDemo / RaisingDemo / WeekPlanDemo / UiSkinDemo
- WhatAiDo.md 十六/十七（语法）、六十九（flag 教学）、七十三~七十六（表达式/限定跳转/call）
