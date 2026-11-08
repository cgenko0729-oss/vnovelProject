# 项目 Bug 与风险审计报告

- **日期**：2026-11-01
- **基准**：`feature/code-slimming` 当前工作区（含未提交改动），Unity 6000.5.3f1
- **评估标准**：itch 免费公开试玩、Windows 桌面版、试玩版不开放 AI
- **方式**：9 个子系统逐文件静态审计 + 一次全量 Lint + 对最高风险项人工抽查
- **没做的事**：没有打过 Build，没有进 Play Mode，没有查看图片内容，没有联网核对 itch、Asset Store、DOTween、MiniMax 的现行条款。凡涉及运行时表现的结论，除标「已抽查」外都是代码推导
- **行号**：审计期间另一个会话在重构（Runner、Stage、PhotoBooth 已拆 partial，三个 SkinExporter 被删），行号可能漂移，以方法名为准

## 一、总体结论

**现在不能公开发布。** 代码质量本身不差：Key 未泄露，AI 关闭路径干净，主线跳转全部有效，未发现死循环。阻塞发布的是四件事：

1. **公开前必须先处理**：角色设定与内容合规、公开仓库里的付费插件和来源可疑的素材（这三项与代码无关，且仓库现在就是公开的）
2. **打包后游戏基本不可玩**：6 个 shader 不进包，项目从未打过 Build
3. **存档与读档有系统性缺陷**：可能坏档，读档后舞台、镜头、音频、输入状态都有残留
4. **试玩内容还是开发态**：测试文本、模板产品名、通关后无路可走

原始条目 198 条（致命 10、高 41、中 71、低 76），各组之间有重复，下面的总览已去重。

## 二、P0：公开前必须处理

| # | 问题 | 证据 | 处理方向 | 状态 |
|---|---|---|---|---|
| P0-1 | **明确写成高二学生的女主，与亲密互动数据、部位框、偷拍功能同时进包**。互动模块主线走不到，但数据可解包；偷拍从第 1 章起可用 | `第1章.vn.txt:27-28,49`、`好感事件_星野结衣.vn.txt:190`、`Assets/Art/VNEffects/Interactions/初次抚摸.asset`、`Assets/Art/VNEffects/TouchZones/星野结衣.asset`、`VNScriptDemo.unity:15555` | 把学生角色与亲密、擦雾、偷拍内容彻底分开，从角色、场景、配置、仓库移除相关数据。**不要靠补一句「已满 18 岁」解决** | 已抽查 |
| P0-2 | **主线背景文件名带 `Hitomi.la`**，共 8 张，已进配置，也在公开仓库 | `Assets/Art/Images/Background/Sweet Homemade  Hitomi.la*.png`、`VNGameConfig.asset` | 确认来源并全部替换，历史也要清 | 文件名已抽查，来源待人工确认 |
| P0-3 | **付费插件 Pixel Crushers 源码在公开仓库历史中**：提交 `dc8dfaf`，约 1130 个代码和资产对象，当前仍跟踪少量残留 | `git rev-list --all --objects`；仓库 visibility = PUBLIC（已抽查） | 先转私有，再重写历史或建干净仓库。这与「永不删分支」规则冲突，需要你决定 | 已抽查 |
| P0-4 | **6 个 shader 不进 Build**：`VN/ImageEffect`、`RetroFilter`、`Shockwave`、`DirectBackgroundTransition`、`FogWipe`、`TutorialMask`。后果：立绘全程透明、第一个 `choice` 抛空引用、对话框启动异常 | `GraphicsSettings.asset:29-36`、`VNImageEffectController.cs` | 加入 Always Included，或由 Resources 资产持有引用；`Mat` 判空 | 引用缺失已抽查，运行后果待 Build |
| P0-5 | **Shader 缺失时留下全屏白板**：找不到 shader 就 return，走不到 `_img.enabled = false`，点开始后舞台全白。即使 P0-4 修好也该改 | `VNRetroFilter.cs:103-108`、`VNScreenShockwave.cs:93-98` | 创建时先禁用 RawImage | 已抽查 |
| P0-6 | **存档写入非原子**：无 tmp、无备份、IO 异常未捕获。写入中崩溃或磁盘满即毁槽；坏档在面板上显示成空槽，覆盖不再确认 | `VNSaveSystem.cs:159,185,223` | tmp + `File.Replace` + `.bak`；Peek 区分损坏与不存在 | 已抽查 |
| P0-7 | **事件模块 `OnLaunch` 抛异常后所有输入失效**：`_eventActive` 已置 true，`Launch` 无 try，连 F9 都不可用，只能强关 | `VNScriptRunner.Commands.cs:343,360` | 包 try/catch 并走清理路径 | 代码已抽查，待实跑 |

## 三、P1：发布前应修

### 读档状态残留（同一根因，建议一次修完）

| # | 问题 | 位置 |
|---|---|---|
| P1-1 | `Stop()` 只停外层协程，旧台词协程继续抢 `_advance`：读档后点击被吞、当句存不了档、读档次数越多越严重（已抽查代码，待实跑） | `VNScriptRunner.Flow.cs:425`、`Dispatch.cs:41` |
| P1-2 | 读档不复位镜头，`@` 异步运镜和 camseq 还在继续跑 | `VNScriptRunner.SaveLoad.cs:48`、`VNStage.Save.cs` |
| P1-3 | 循环音效不进存档也不清，语音继续念；`第2章:33` 雨声会串场景 | `VNStage.Save.cs:218`、`VNAudio.cs` |
| P1-4 | 读档失败时 flag 已被覆盖、剧本已停、无提示，游戏半死 | `VNScriptRunner.SaveLoad.cs:50-64` |
| P1-5 | 转场回调在读档后把背景改回去；异步 move 时存到瞬时坐标 | `VNStage.Scene.cs:115`、`VNStage.Save.cs:153` |
| P1-6 | 默认皮肤下每次读档，正文和名牌向右累加偏移；HUD 满屏飘字；回想不清空 | `VNDialogueBox.cs:289`、`VNStatsHud`、`VNBacklog.cs` |
| P1-7 | `saveVersion` 无人读取，存档只存命令下标；更新试玩版后旧档静默错位 | `VNSaveSystem.cs:11-12`、`VNScriptRunner.SaveLoad.cs:33` |

### 玩家明显可见

| # | 问题 | 位置 |
|---|---|---|
| P1-8 | 通关后无路可走：游戏内没有回标题和退出入口（Runner 组、第 9 组一致） | `结局集.vn.txt:199-207`、`VNTitleMenu.cs:163` |
| P1-9 | 铭牌显示「星野结衣11」（已抽查） | `Assets/Art/VNEffects/Characters/星野结衣.asset:16` |
| P1-10 | 开场测试残留：空教程 `test01`、台词「hahaha」、运镜试验（已抽查） | `第1章.vn.txt:40,42,43,64-82,88` |
| P1-11 | 产品名 `My project`、公司 `DefaultCompany`、标题回退 `Visual Novel`。**存档路径依赖公司名和产品名，发布后再改会丢档**（已抽查） | `ProjectSettings.asset:15-16`、`VNGameConfig.asset` |
| P1-12 | 背景没接上 `VNImageEffectController`：mood、天气、景深、波光对背景全部无效，Editor 里同样如此 | `VNStage.cs:267-278`、`VNScriptDemo.unity:10327` |
| P1-13 | 背景无溢出余量，Ken Burns、视差、运镜都会露深色边 | 场景 BackImage、`VNKenBurns.cs`、`VNCamera.cs` |
| P1-14 | 立绘光环不跟随角色，左右站位时每句台词在舞台中央闪光 | `VNGlowBackdrop.cs:60` |
| P1-15 | 「装死」分支把 BGM 压到 7% 不恢复 | `第1章.vn.txt:155-156` |
| P1-16 | BGM 语音闪避杀掉 `bgm stop` 的淡出，BGM 停不下来；设置面板拖 BGM 音量无实时效果 | `VNAudio.cs:326,346` |
| P1-17 | 每次 event、读档都弹「自动模式 关」，一周排程连弹 7 张（`SetAuto` 无判重，已抽查） | `VNScriptRunner.Input.cs:163` |
| P1-18 | 剧本数字按系统区域解析，德语等系统下 `1.12` 变 112（Parser 已抽查，待该区域实测） | `VNScriptParser.cs:67,76,216,224,239` |
| P1-19 | 语言按钮开放，但主线 8 个剧本没有翻译表 | `VNConfigPanel.cs:157-159` |
| P1-20 | 事件期间工具栏可点：快读、设置、Skip 能穿透事件（羽毛球、互动、擦雾） | `VNQuickToolbar.cs:114`、`VNScriptRunner.SaveLoad.cs:174,182,226` |

### 剧情数值

| # | 问题 | 位置 |
|---|---|---|
| P1-21 | `stat 好感度_结衣 10` 是绝对赋值，第 1 章选择对好感的影响被清掉（已抽查） | `主循环.vn.txt:45` |
| P1-22 | 第 2 章地图「天台」条件用了从未赋值的 `好感度`，永远不出现 | `VNScriptDemo.unity:22496` |
| P1-23 | 好感度无属性定义；偷拍扣的是 `好感`，主线读 `好感度_结衣`，惩罚无效 | `Assets/VNEffects/SecretPhoto/秘密相机.asset:29` |
| P1-24 | 「球场之王」「每月的陪练」主线完成不了；「告白大作战」文案写旧角色小雪且缺第 3 阶段 | `第1章.vn.txt:29`、`告白大作战.asset:17-20` |
| P1-25 | `Accept` 无状态守卫，可把已完成任务打回阶段 1 重复领奖 | `VNQuestEngine.cs:171` |

### 开发工具（会损坏你的数据）

| # | 问题 | 位置 |
|---|---|---|
| P1-26 | `camcut`/`camto` 的「角色:部位」经编辑器保存后参数错位（潜伏，目前无剧本这样写） | `VNScenarioDoc.ParseCommand` |
| P1-27 | 镜头窗口选中未登记角色的 camseq 行，会自动改成列表第一个角色并实时写回 | `VNCamseqEditorWindow.cs` |
| P1-28 | 镜头窗口不感知剧本侧改动，旧内容覆盖；绑定只记行号，可能写错行 | `VNCamseqEditorWindow.cs` |
| P1-29 | 每次保存多一个空行（`第1章` 尾部已 138 行）；打开即脏，F5 重写文件造成整文件 diff | `VNScenarioDoc.cs` |
| P1-30 | `jump 文件::标签` 被校验误判，第 2、3 章无法从编辑器播放 | 剧本编辑器 Validate |
| P1-31 | `Rescan` 在 `Assets/CG` 存在时清空其他 CG 登记；重建场景丢 5 个事件模块 | `VNGameConfigTools.cs`、`VNEffectsDemoSetup.cs` |

## 四、Lint 基线（共 51 条：Error 9、Warning 32、Info 10）

| 类别 | 结论 |
|---|---|
| `bad-mark-mode` ×7 | **真问题，文案指错方向**。这些行以 `@ # 注释` 结尾，DSL 不支持行内注释，`@` 没生效。Parser 只判 `raw.EndsWith("@")`（已抽查）。6 行变成同步等待，Demo:92 无影响。根目录 `第1章` 第 83、105、110 行在主线上 |
| `bad-emote` ×2 | **真问题**。`VNEmoteCatalog` 只注册方法名和别名，没注册中文 label（已抽查），「颤抖」「点头」静默不播。实际在 `Hina/第1章.vn.txt`，互动资产里还有 4 个同类。Lint 窗口把同名文件合并显示 |
| 未登记 se ×15 | 不报错不卡住，只是没声音。主线 12 处 |
| 未登记 bg/bgm/voice | 只在运行时走不到的剧本里，不影响主线 |
| `quest-stage-range` ×2 | 不卡流程，只是第 3 阶段没文案 |
| `duplicate-script-name` | `第1章::` 只会到主线，Hina 版不进包；把 Hina 加入 chapters 后才冲突 |

## 五、其他风险

- **素材授权**：稳稳素材包、图库站图片、日文素材站背景、`Bgm01/02.wav`、MiniMax AI 语音来源或条款未核实；全项目没有 Credits；约 26 个 AI 生成素材需按 itch 要求披露
- **Unity 版本**：`FindFirstObjectByType` 在 6000.5 已过时，共 107 处、37 个文件，将来可能升级为 error
- **开发包进 Player**：`UNITY_MCP_READY` 宏、18MB NuGet/Roslyn、`Unity.AI.*` 未限定 Editor，增加包体和 Build 冲突风险；未发现自动联网
- **AI 关闭**：Build 不读 Key、不联网，主线不调 `aitalk`。但 D 键打开空日记本，人格资产进包，没有总开关。**不要给 Build 加 `VN_AI_ALLOW_LOCAL_KEY`**
- **完整版 AI**：断网、被拦截不走 `* 失败`，最终按「普通」结算，超时最慢约 22 分钟
- **`.gitignore`**：Key 只保护根目录固定命名；`Assets/Development/DebugScreenShot/` 的 26 张截图因 ignore 路径过期而公开
- **文档漂移**：CLAUDE.md 的 Unity 版本、`Assets/Scripts` 路径、Pixel Crushers 状态、`Assets/CG` 路径均已过时；组件表里的 Exporter 已被删除

## 六、已确认没问题的部分

Key 未进 Git 历史（按已知 Key 前缀和文件名）；无 UnityEditor 引用泄漏；Resources 路径字面量全部匹配；三个被移除的包没有残留引用；场景无缺失脚本；无重复 guid；剧本 TextAsset 经 VNGameConfig 正常进包；主线无未定义标签和死循环；Tracker、QuestEngine 的 `Suspended` 成对；读档不刷奖；表达式除零和溢出已处理；VNPause 释放路径完整；触摸光标 `Dispose` 四处都调；Schema 与 Parser 的 49 个命令一致。

## 七、建议修复顺序

1. **今天**：仓库转私有；决定 P0-1 到 P0-3 的处理方式
2. **第一轮**：P0-4、P0-5，然后打第一个 Windows Build，从头跑到第一个选项
3. **第二轮**：P0-6、P0-7、P1-1 到 P1-7（存档读档一次修完，按技能 vn-save-compat 走）
4. **第三轮**：清理试玩内容，定产品名（P1-8 到 P1-11），修数值和任务
5. **第四轮**：演出和音频，再修工具链

## 八、审计局限

- 第 2 组有约 450 行 Runner 代码（SNS 重放、flag/time 辅助）和 `VNSnsView` 快照逻辑未展开
- 重构后的 PhotoBooth partial 没有逐行复审；三个已删除的 SkinExporter 无法审计
- 总览之外的条目由子代理走读得出，我只人工抽查了标「已抽查」的项，其余可能有误报
- 子代理有几次把比对用的临时文件写到规定外的位置（都在项目外且已删除），项目文件没有被审计改动

---

以下是 9 组原始报告，保留全部条目和修复方向，未去重。


---

# 审计报告 1：剧本核心（Parser / Runner / 表达式 / 事件框架 / 本地化 / 回想 / 标题）

审计时间：2026-11-01，分支 `feature/code-slimming`，只读。

## 重要说明：审计途中工作区发生了变化

13:40:14 有别的进程把 `VNScriptRunner.cs` 拆成了 6 个 partial（`.cs`、`.Commands`、`.Dispatch`、`.Flow`、`.Input`、`.SaveLoad`），
`VNStage.cs` 也拆成了 5 个。这不是本审计做的。

我通读的是拆分前的 3185 行版本。之后把 `git show HEAD:` 的版本与拆分后所有文件的行集合做了 diff：
差异只有 `partial` 声明、`namespace`、`using` 和大括号，**是纯搬移，语义未变，通读结论有效**。
下文行号一律是**拆分后的当前文件**。如果拆分还在继续，行号可能再漂移，函数名仍然可靠。

## 计数

| 严重度 | 数量 |
|---|---|
| 致命 | 1 |
| 高 | 4 |
| 中 | 7 |
| 低 | 5 |
| 合计 | 17 |

把握度说明：「已确认」= 已沿代码路径走通；「待验证」= 依赖 Unity 运行时行为或玩家环境，需要实跑确认。
没有跑过任何代码，也没有进 Play Mode。

---

## 致命

### C1. 协程体内的异常让整个游戏软锁，事件模块最严重

- **位置**：`Script/VNScriptRunner.Dispatch.cs:30-41`（`Run`）、`Script/VNScriptRunner.Commands.cs:343`、`:360-361`（`EventCo`）
- **触发步骤**：
  1. `Run` 的 `try/catch` 只包住 `Dispatch(cmd)`。而 `Dispatch` 对 say、choice、event、sns、camseq、camera 只是创建迭代器，真正的代码在 `StartCoroutine(co)` 里执行，这个 try 接不到。
  2. `EventCo` 先写 `_eventActive = true`（:343），再调 `module.Launch(...)`（:360），后者没有 try。
  3. 任一模块的 `OnLaunch` 抛异常。代码里有明确的 throw：`VNPlanModule.cs:240`、`VNResultPopupModule.cs:85`（皮肤 prefab 无效时）。空配置导致的 NRE 同理。
- **后果**：`EventCo` 协程死亡，`Run` 停在 `yield return StartCoroutine(co)` 永远等不到恢复，`_eventActive` 一直是 true。`Update` 第一行就 return（`Script/VNScriptRunner.Input.cs` 的 `if (_eventActive) return;`），所有快捷键失效，包括 F9。玩家只能强关。say、choice 等协程体抛异常时同样是剧情永久停住，但 F9 还能用。
- **当前是否可触发**：`VNSystemUiSkinSet_Default.asset` 的 planPrefab / resultPopupPrefab 都已赋值，所以现阶段没有已知的必现路径。一旦触发就是彻底卡死，故按后果定级。
- **修复方向**：`module.Launch` 包 try/catch，失败时走「告警 + 销毁模块 + 复位 `_eventActive`、恢复对话框 + 顺序继续」。协程体可用包装迭代器（逐步 `MoveNext` 外包 try）统一接住异常，出错就跳过本条命令。
- **把握度**：待验证。代码路径已确认；「子协程抛异常后父协程不恢复」是 Unity 运行时行为，需实跑确认（故意让一个模块 OnLaunch 抛异常即可）。

---

## 高

### H1. 读档不复位镜头，异步运镜还会继续跑

- **位置**：`Script/VNStage.Save.cs:167-242`（`RestoreSnapshot`）、`:248-259`（`ClearStage`）、`Script/VNScriptRunner.SaveLoad.cs:48-71`（`LoadFrom`）、`Script/VNScriptRunner.Dispatch.cs:40`
- **已核实**：全局 Grep `SnapReset`，运行时只有 `VNScriptRunner.Flow.cs:262`（`RestoreDebugCamera`，调试重建专用）和 `VNCamera` 内部调用。`VNStage*.cs` 里唯一碰镜头的是 `SetCharacterTargets`。读档链路上没有任何复位。
- **触发步骤**：
  1. 第1章 `第1章.vn.txt:181` 执行 `camera pushin 1.06 5 focus:星野结衣 @`，或任何 camseq 让画面停在特写。
  2. 此时按 F9 / L 读一个更早的存档。
- **后果**：背景、立绘恢复成存档时的样子，镜头却保持当前缩放和平移，直到下一条镜头命令才变。`@` 异步运镜更明显：`StartCoroutine(co)` 没有句柄，`Stop()` 管不到，DOTween 序列也没被杀，读档后画面还在继续推。`第1章.vn.txt:64-76` 用过 `mode:char`，`VNCamera` 的缩放模式同样不会被复位。
- **修复方向**：`ClearStage` 或 `LoadFrom` 调 `vnCamera.SnapReset()`（含 `SetMode(Both)`），并清掉 `VNCameraFade` 遮罩。镜头不进存档可以接受，但读档必须回到默认状态。
- **把握度**：已确认。

### H2. `Stop()` 只停 `Run`，子协程全部成为僵尸，读档后抢 `_advance` / `_waitingAtSay`

- **位置**：`Script/VNScriptRunner.Flow.cs:423-433`（`Stop`）、`Script/VNScriptRunner.Dispatch.cs:41`、`Script/VNScriptRunner.Commands.cs:25-57`（`NormalSayCo`）
- **已核实**：全局 Grep `StopCoroutine`，Runner 里只停 `_co` 和存档截图协程。`Run` 用 `yield return StartCoroutine(co)` 启动子协程，子协程是独立的，停父协程不会停它。没有取消令牌，也没有 `StopAllCoroutines`（Runner 上）。
- **触发步骤**：停在任意台词上按 L，或 F9 读档。这是最常见的读档时机。
- **后果**：旧 `NormalSayCo` 还在 `while (!_advance)` 里转，新的那句也进入同样的循环，两者共享 `_advance` 和 `_waitingAtSay`。结果取决于协程调度顺序：
  - 旧的先跑：玩家第一次点击被旧协程吃掉（它把 `_advance`、`_waitingAtSay` 都清成 false），画面没反应。这一句接下来 F5 / Q 提示「现在不能存档」，偷拍也进不去。要点第二下才推进。
  - 新的先跑：旧协程继续潜伏。之后开 Auto / Skip，它的超时分支立刻成立，把 `_waitingAtSay` 清掉，新台词上同样无法存档；点击落在 wait 期间时被它消费也无害。
  - 多次读档会累积多个僵尸。
- **其他子协程**：
  - `ChoiceCo`：`ForceClose` 把回调置空，只是永远空转，有泄漏但无副作用。
  - `CamseqCo`：见 H1，路径继续跑。
  - `SnsReplyCo`：快读（工具栏）时计时继续，超时后会 `VNFlags.Apply(lateflag)` 并 `JumpTo` 改掉新一轮的 `_index`。目前只有 `SnsDemo.vn.txt:39` 用到。
- **修复方向**：Runner 持有当前子协程句柄并一起 Stop；更稳的是引入 `_runToken`，等待循环每轮比对，令牌变了立即退出。异步协程放进列表，Stop 时统一停。
- **把握度**：待验证。僵尸存在是已知的 Unity 语义、代码路径已确认；具体是吞点击还是吞存档，取决于调度顺序，需实跑（读档后点一下看是否推进，再按 Q）。

### H3. 全部数值解析走系统区域设置，德语等系统下演出参数放大几十倍

- **位置**：`Script/VNScriptParser.cs:67`、`:76`、`:216`、`:224`、`:239`，`Script/VNEventModule.cs:27`，`Script/VNScriptRunner.Commands.cs:436`，`Script/VNScriptRunner.Dispatch.cs:279`，`Script/VNScriptRunner.Flow.cs:206`
- **已核实**：Grep `CurrentCulture|InvariantCulture`，运行时没有全局设置 culture；只有 `VNExpression`、`VNScreenShake`、`VNInteractionModule`、`VNAiClient` 个别处显式用了 Invariant。`VNScreenShake.cs:63` 特意用了 Invariant，同一项目里写法不一致。
- **触发步骤**：玩家系统区域是德语、荷兰语、西语等（`.` 是千分位）。`float.TryParse("1.12")` 默认样式含 `AllowThousands`，得到 112；`"0.5"` 得到 5；`"1.0"` 得到 10。法语、俄语等（`.` 非法）解析失败，静默回退默认值。
- **后果**：`第1章.vn.txt:62` 的 `camera snapzoom 1.12` 变成 112 倍缩放，`wait 1.0` 等 10 秒，camseq 时长全错，`camto` 的 zoom 飞掉。中文、英文、日文系统不受影响，所以作者自测发现不了，itch 公开后才会遇到。
- **修复方向**：`VNScriptCommand.ArgF/KwF`、`VNEventContext.KwF`、`ParseCamWaypoint` 等统一走 `NumberStyles.Float + InvariantCulture` 的工具方法；或在启动最早期设 `CultureInfo.DefaultThreadCurrentCulture = InvariantCulture`（再检查一遍日期显示是否受影响）。
- **把握度**：待验证。代码路径已确认；需在德语区域设置下跑 Build 确认 Unity 运行时取到的 culture。

### H4. 剧本结束后无路可走，游戏内也没有回标题或退出入口

- **位置**：`Script/VNScriptRunner.Dispatch.cs:51`（`_running = false`）、`Script/VNScriptRunner.Input.cs` 的 `if (!_running) return;`、`Script/VNTitleMenu.cs:54`、`:163`、`Assets/Scenarios/结局集.vn.txt` 文件尾
- **已核实**：
  - Grep `titleMenu.Open`：标题只在 `VNScriptRunner.cs:178` 的 `Start` 打开一次。
  - Grep `Application.Quit`：全项目运行时只有 `VNTitleMenu.cs:163`。
  - `结局集.vn.txt` 末尾「——《辣妹与宅宅》　完」之后没有 jump / chapter（文件注释也写明「流程自然结束」）。
  - 工具栏 13 个动作（`VNQuickToolbar.cs:114-135`）没有回标题或退出。
- **触发步骤**：打到剧本 EOF。
- **后果**：画面停在最后一句，对话框还在，点击无反应，没有任何提示。玩家只能 Alt+F4；全屏下看起来就是卡死。试玩版截断在某处也是同样的结局。玩家中途也无法回标题。
- **修复方向**：`Run` 结束时走「试玩结束 → 回标题」；加一个 `title` 命令或在设置面板加「回标题 / 退出」。回标题要同时处理 `VNTitleMenu._stageApplied`（目前第二次 `Open` 不会重放标题背景和 BGM）以及 `ClearStage`、镜头、flag 的清理。
- **把握度**：已确认。

---

## 中

### M1. Auto / Skip 只被回想面板挡住，其他面板打开时剧情在底下继续走

- **位置**：`Script/VNScriptRunner.Commands.cs:48`
- **已核实**：Grep `SetAuto|IsAuto` 的调用方只有 Runner 与 `VNSecretPhotoMode.cs:175-176`。J / C / I / G / D 打开面板时都不暂停（存读档和设置走 `Time.timeScale = 0`，所以不受影响）。
- **触发步骤**：开 Auto，按 C 看属性、按 I 看背包。
- **后果**：台词在面板下继续推进，玩家错过剧情。推进到 `event` 时 `_eventActive` 变 true，`Update` 提前 return，面板分支的 ESC 和热键关闭都失效，只能靠面板自己的关闭按钮。
- **修复方向**：等待循环统一判断「有任意模态面板打开」，可复用 `IsSecretPhotoIconAllowed` 里那串判断。
- **把握度**：已确认。

### M2. `SetAuto(false)` 不判重，每次进事件、读档都弹「自动模式 关」

- **位置**：`Script/VNScriptRunner.Input.cs:163-168`，调用点 `Script/VNScriptRunner.Commands.cs:338`、`:121`、`Script/VNScriptRunner.SaveLoad.cs:58`
- **触发步骤**：从没开过 Auto，正常玩到 `event result`（`主循环.vn.txt` 8 处、`节日活动集.vn.txt` 17 处），或点「继续游戏」。
- **后果**：左上角弹 `runner.autoOff` 卡片。`SetSkip` 有 `if (_skip == on) return;`，`SetAuto` 没有。`VNToast.Show` 无去重，养成循环里会反复出现，玩家会以为自己误触了什么。
- **修复方向**：`SetAuto` 开头加 `if (_auto == on) return;`。
- **把握度**：已确认。

### M3. 点过工具栏后按 Enter 会重复触发该按钮

- **位置**：`Script/VNScriptRunner.Input.cs`（Enter 推进），`Art/VNEffects/SystemUISkins/QuickToolbar_Default.prefab`（13 个 `m_Mode: 3` Automatic）
- **已核实**：Grep `SetSelectedGameObject|Navigation.Mode|sendNavigationEvents`，运行时代码里没有任何清选中或关导航的处理。`VNScriptDemo.unity` 的 InputSystemUIInputModule 配了 Submit 动作。
- **触发步骤**：鼠标点工具栏「快存」或「设置」，再用 Enter 推进台词。
- **后果**：`Selectable.OnPointerDown` 把按钮设成选中，之后 Enter 同时触发 Submit（再执行一次按钮动作）和 Update 的推进：每次 Enter 都快存一次覆盖快存槽、重新打开设置面板、或切换 Auto。
- **修复方向**：工具栏按钮 Navigation 设 None，或点击后 `EventSystem.current.SetSelectedGameObject(null)`。其他面板的按钮同样检查。
- **把握度**：待验证。需实跑确认 Enter 映射到 Submit。Space 是否受影响未确认。

### M4. 存档只存命令下标，更新试玩版后旧档静默错位

- **位置**：`Script/VNScriptRunner.SaveLoad.cs:33`、`Script/VNScriptRunner.Flow.cs:405`（Clamp）、`Script/VNSaveSystem.cs:11`
- **已核实**：Grep `saveVersion`，只有字段定义，全项目没人读。没有剧本哈希或行内容校验。
- **触发步骤**：玩家存档 → 你在 itch 更新一版，改过 `.vn.txt` 增删了命令 → 玩家读档。
- **后果**：`commandIndex` 和 call 栈的 `returnIndex` 指向别的命令，可能不是台词。从错误位置继续：漏执行 show / flag、跳过分支。不报错，舞台还是旧快照，看上去只是剧情错乱。
- **修复方向**：存 `label + 偏移` 或台词内容哈希，读档时校验；至少存剧本哈希，不一致时提示「存档来自旧版本」。
- **把握度**：已确认。

### M5. 读档失败时已经停掉当前剧情并覆盖 flag，游戏停在半死状态

- **位置**：`Script/VNScriptRunner.SaveLoad.cs:50`、`:59-64`
- **触发步骤**：存档的 `chapter` 找不到（改名或移除章节，与 M4 同场景）→ 游戏内读档。
- **后果**：`VNSaveSystem.Load` 已经写入存档的 flag，`Stop()` 已停掉 `Run`，然后 `SwitchChapter` 失败直接 return，只有 LogError。画面还是旧的，点击无反应，玩家看不到任何提示。之后再存档也不行（`_waitingAtSay` 为 false）。
- **修复方向**：用 `Peek` 先校验章节和下标，通过后才动 flag 和 Stop；失败时弹 Toast。
- **把握度**：已确认。

### M6. 英文、日文下主线全是中文；sns reply 不进翻译

- **位置**：`Script/VNScriptLocale.cs:40`、`:73`、`Editor/VNLocalizationTools.cs:127`、`Assets/Resources/VNLocale/Scenarios/`、`Script/VNConfigPanel.cs:157-159`
- **已核实**：
  - 入口是 `第1章.vn.txt`（config guid 已解析），翻译表只有 `Chapter1`、`Chapter2`、`Demo` 三份，`第1章`、`主循环` 等 8 个主线文件都没有表。设置面板却提供 English / 日本語 按钮。
  - `Apply` 与抽取工具都只处理 `keyword == "choice"`，而 `SnsReplyCo` 指望 `TextOf(opt)` 有译文，`sns reply` 的候选回复永远是中文。
- **触发步骤**：设置里切 English 后进游戏。
- **后果**：UI 是英文，所有台词是中文，只有 LogWarning。sns reply 这条目前只影响 `SnsDemo`。
- **修复方向**：发行前要么生成翻译表，要么在试玩版隐藏语言按钮。`sns` 抽取时两边要同步加（注意抽取遍历顺序不能和 Apply 错位）。
- **把握度**：已确认。

### M7. 工具栏在事件期间仍可点，部分 Request 没有 `_eventActive` 守卫

- **位置**：`Script/VNScriptRunner.SaveLoad.cs:174`（`RequestLoadPanel`）、`:182`（`RequestBacklog`）、`:226`（`RequestConfigPanel`）、`Script/VNScriptRunner.Input.cs:163`、`:171`，`Script/VNQuickToolbar.cs:114-135`、`:73`
- **已核实**：`HideBox` 不隐藏工具栏（`VNDialogueBox.cs:670-673` 注释写明是有意的），工具栏排序 41 低于事件层 60，能不能点到取决于模块有没有吃射线的全屏底板。QTE、地图、商店、战斗的 Dim 走 `VNUi.CreateImage`（默认 raycast=true），会挡住；**互动（`VNInteractionModule`）、羽毛球（`VNBadmintonUi.CreateImage` 是 false）挡不住**。任务、属性、背包、画廊的 Request 都有守卫，唯独上面几个没有。
- **触发步骤**：`event badminton` / `event interact` 中点工具栏「设置」「回想」「快读」或「自动 / 快进」。
- **后果**：设置面板打开、`Time.timeScale = 0`，但模块用 `unscaledDeltaTime` 和 VNPause，倒计时照走（小游戏里还会照常判定）。`Update` 提前 return，ESC 不归面板管，反而可能被模块当成认输。快进打开后 `DOTween.timeScale` 变 4 倍，影响模块里的补间。另外模块内联台词时 `_waitingAtSay` 为 true，存档也能点进去。
- **主线范围**：正式剧本链路只用到 result、plan、qte、map、shop，前述会挡的模块不受影响；`result`、`plan` 由皮肤 prefab 决定，未核实。
- **修复方向**：Request 系列统一判 `_eventActive`；或事件期间调 `VNQuickToolbar.SetVisible(false)`。
- **把握度**：已确认（代码路径），`result` / `plan` 待验证。

---

## 低

### L1. 从标题读档会把 `hideHUD keep` 的隐藏状态弹回来

- **位置**：`Script/VNScriptRunner.SaveLoad.cs:67`、`:70`、`Script/VNScriptRunner.Flow.cs:404`、`Script/VNTitleMenu.cs:111-121`
- **触发步骤**：在锁定隐藏段落存档 → 重启 → 标题读档。
- **后果**：`RestoreUiHidden` 先正确隐藏，随后 `ResumeAt` → `NotifyGameplayStarted` → `RestoreSceneUi` 无条件 `SetInterfaceVisible(true)`、`SetHudVisible(true)`。`_uiHiddenParts` 还是隐藏，界面却显示出来，直到剧本下一条 `hideHUD`。
- **修复方向**：把 `RestoreUiHidden` 移到 `ResumeAt` 之后，或标题收起后调一次 `ApplyUiHidden()`。
- **把握度**：已确认。

### L2. 回想列表从不清空，读档后混着别的存档线

- **位置**：`Script/VNBacklog.cs:49-54`（无 Clear 方法）
- **触发步骤**：玩到后面 → 读早期存档 → 按 H。
- **后果**：显示读档前的「未来」台词，且恢复点那句重复记录一次。反过来，重启后读档回想是空的（不进存档）。
- **修复方向**：加 `Clear()`，在 `LoadFrom` 调用；要保留回想就让它进存档。
- **把握度**：已确认。

### L3. 回想里出现内部模块 id

- **位置**：`Script/VNScriptRunner.Commands.cs:371`、`Script/VNEventModule.cs:91`
- **触发步骤**：`event result` 后按 H（`RecordInBacklog` 只有 `VNPlanModule.cs:59` 覆写过）。
- **后果**：回想显示 `result → <结果名>`，既是内部标识也不走本地化。结算弹窗在养成循环里频繁出现，会刷屏。
- **修复方向**：`VNResultPopupModule` 覆写为 false，其余模块提供显示名。
- **把握度**：已确认。

### L4. 试玩版里 D 键仍会打开 AI 日记本

- **位置**：`Script/VNScriptRunner.Input.cs`（`kb.dKey`）、`Script/VNScriptRunner.SaveLoad.cs` 的 `RequestDiary`
- **触发步骤**：游戏中按 D。
- **后果**：AI 聊天未开放，日记本却能打开，且是空的；同时按键无提示也无入口，玩家只会误触。
- **修复方向**：按 VNGameConfig 的开关禁用。
- **把握度**：已确认（打开路径）；面板在空数据下的表现未实跑。

### L5. 内嵌剧本行没有拦 `if`，能绕过控制流保护

- **位置**：`Script/VNScriptRunner.cs` 的 `InlineBlockedKeywords`（`RunInlineCo` 上方）
- **触发步骤**：模块内联行里写 `if 好感>=3 jump xx`。
- **后果**：注释说明这类命令会打乱 `_index`，但名单有 jump、call、choice 却没有 `if`（以及 `sns`）。`Dispatch` 的 `if` 分支直接 `JumpTo`，事件结束后剧本跳到错误位置。
- **修复方向**：名单补 `if`、`sns`；Lint 同步检查。
- **把握度**：已确认。需要作者写出这样的行才触发。

---

## 已排查、未发现问题

- **解析边界**：CRLF、首尾空白、空行、注释夹在 choice 与选项之间、缺位置参数（`Arg` 有默认值）、未知命令（落到 say 或 default 告警）都能处理。BOM 依赖 `TextAsset.text` 自行剥除，未实跑。
- **无冒号旁白含时间**（如 `12:30`）会被拆成说话者：Grep 四个主线文件没有命中，目前不触发。
- **VNExpression**：除零有判断，`checked` 溢出被捕获，短路求值不会误报除零，错误路径都能退出循环。
- **call / return / 跨文件跳转**：`returnIndex`、参数字典、失败回滚、存档恢复链路走通。`RestoreExecutionFrame` 每次 return 会重跑 Locale.Apply 和 Prewarm，只是开销。
- **VNPause**：句柄、`ReleaseAll` 与 `CleanupActiveEvent` 路径完整。`Changed` 目前没有订阅方，所以 `Prune` 静默清句柄不发事件没有影响。
- **事件退订**：Runner、Backlog、TitleMenu 的 `LanguageChanged` 都成对；`VNFlags.Changed` 的订阅方（CalendarHud、QuestLog、StatsHud）也成对。
- **静态状态**：`EditorSettings.asset` 的 `m_EnterPlayModeOptions: 0`，域重载未关闭，现在不触发。若以后勾选关闭，`VNFlags._values` 和 `Changed/KeyChanged`、`VNLocale._tables` 都没有重置逻辑（`VNPause` 有）。试玩 Build 不受影响。
- **隐藏界面时的打字机**：`SetInterfaceVisible` 只改 alpha，不停协程，不会卡在 `IsTyping`。
- **存档截图协程**：`CaptureThumbnailCo` 只等一个 `WaitForEndOfFrame`，没有永久等待条件。
- **`FindLatestSlot`**：`savedAt` 精确到秒，格式固定，区域设置不影响解析。

`ProjectCodeGuide.md` 第 1018 行起的已知技术债未重复上报。

---

## 实际读完的文件

已逐行读完（路径前缀 `Assets/Project/Scripts/VNEffects/Script/`）：

- VNScriptParser.cs（386 行）
- VNScriptRunner.cs 拆分前全文 3185 行（四段读完，拆分后内容已用 diff 确认一致）
- VNExpression.cs（278）、VNFlags.cs（82）、VNStoryAddress.cs（55）、VNEventModule.cs（96）、VNEventRegistry.cs（91）、VNPause.cs（164）、VNScriptLocale.cs（145）、VNLocale.cs（137）、VNBacklog.cs（141）、VNTitleMenu.cs（292）

为验证而部分读过：VNStage.Save.cs（160-334）、VNDialogueBox.cs（585-694）、VNCameraFade.cs（95-163）、VNCamera.cs（360-489）、VNInterludeScreen.cs（60-179）、VNParameterInterpolator.cs（全文）、VNQuickToolbar.cs（30-210）、VNSaveSystem.cs（全文）、VNPlanModule.cs（95-244）、VNSnsView.cs（340-400）。

未读完的部分：范围内 12 个文件无遗漏。范围外文件只读了上述片段，未做完整审计。

---

# 审计报告 2：存档 / 舞台 / UI（Windows 免费试玩版视角）

审计日期：2026-11-01。方式：只读代码 + 只读检查场景、资产、字体文件；未运行游戏。
分支：`feature/code-slimming`（工作区有大量未提交改动）。

## 先看这条：审计期间代码被改动过

13:34~13:40 之间工作区发生了改动（不是本审计造成的）：
`VNScriptRunner.cs`、`VNStage.cs` 被拆成多个分部文件（`.Commands/.Dispatch/.Flow/.Input/.SaveLoad`、
`.Characters/.Fx/.Save/.Scene`），`VNAlbumStore.cs`、`VNCharacterOverlay.cs`、`VNDialogueBox.cs`、
`VNSaveLoadPanel.cs`、`VNConfigPanel.cs`、`VNCgGallery.cs`、`VNUi.cs` 的修改时间也落在这段时间内。

- 本报告的行号是 13:47 按磁盘现状核对的，之后若继续改动，请以方法名为准。
- Runner 和 Stage 我是在拆分前读的。之后用「HEAD 单文件 vs 全部分部文件」逐行多重集比对：
  差异只有 using/namespace/partial 声明，外加删除了 `VNStage.WeatherGust`。逻辑判定为纯搬移。
- `VNAlbumStore.cs`、`VNCharacterOverlay.cs` 等文件是在它们修改之后读的，内容即现状。

## 严重度计数

| 严重度 | 数量 |
|---|---|
| 致命 | 1 |
| 高 | 6 |
| 中 | 12 |
| 低 | 9 |
| 合计 | 28 |

## 总览

| 编号 | 严重度 | 标题 | 位置 | 把握度 |
|---|---|---|---|---|
| F1 | 致命 | 存档写入非原子、无备份、异常未捕获，坏档还显示成空槽 | VNSaveSystem.cs:159 | 已确认 |
| H1 | 高 | 读档杀不掉旧的台词协程：第一下点击被吞、当句存不了档 | VNScriptRunner.Flow.cs:425 | 已确认 |
| H2 | 高 | 循环音效不进存档、读档也不清；语音同样不停 | VNStage.Save.cs:218 | 已确认 |
| H3 | 高 | 语音压低/恢复 BGM 时杀掉 `bgm stop` 的淡出，BGM 停不下来 | VNAudio.cs:326 | 已确认 |
| H4 | 高 | 设置面板拖 BGM 音量没有实时效果 | VNAudio.cs:346 | 已确认 |
| H5 | 高 | 读档不复位镜头，旧 camseq 还在继续跑 | VNScriptRunner.SaveLoad.cs:48 | 已确认 |
| H6 | 高 | 剧本数字按系统区域解析，德语等系统下 1.4 变 14 | VNScriptParser.cs:67 | 代码已确认，运行待验证 |
| M1 | 中 | CG 解锁文件损坏后，下一次解锁把历史全部覆盖 | VNCgUnlocks.cs:43 | 已确认 |
| M2 | 中 | saveVersion 无人读取，读档靠裸命令索引，更新剧本后跳错行 | VNSaveSystem.cs:12 | 已确认 |
| M3 | 中 | 默认皮肤下每次读档，正文与名牌往右累加偏移 | VNDialogueBox.cs:289 | 已确认 |
| M4 | 中 | 全屏切换读回旧值，标签和保存值都错 | VNConfigPanel.cs:258 | 待验证 |
| M5 | 中 | `volume` 命令改写玩家音量，且不进存档 | VNAudio.cs:342 | 已确认 |
| M6 | 中 | 存档记录立绘的瞬时 x，异步 move 时读档站错位 | VNStage.Save.cs:153 | 已确认 |
| M7 | 中 | 转场回调在读档之后把背景改回去 | VNStage.Scene.cs:115 | 已确认 |
| M8 | 中 | Auto/Skip 在除回想外的面板打开时继续推进剧情 | VNScriptRunner.Commands.cs:48 | 已确认 |
| M9 | 中 | 快捷条在事件模块中缺少保护，能开读档/设置/Skip | VNQuickToolbar.cs:114 | 已确认 |
| M10 | 中 | 非 16:9 屏幕背景露边 | 场景 BackImage / VNStage | 待验证 |
| M11 | 中 | 任务面板 ☑/☐ 三款字体都缺字，完成与否看不出 | VNQuestLog.cs:456 / VNFont.cs:84 | 已确认 |
| M12 | 中 | 读档失败路径：flag 已被覆盖、剧本已停、无提示 | VNScriptRunner.SaveLoad.cs:60 | 已确认 |
| L1 | 低 | 每次读档/进事件都弹「自动模式关闭」 | VNScriptRunner.Input.cs:163 | 已确认 |
| L2 | 低 | 同曲不同音量的 PlayBgm 被忽略 | VNAudio.cs:219 | 已确认 |
| L3 | 低 | 回想记录不随读档回退 | VNBacklog.cs | 已确认 |
| L4 | 低 | 读档后第一句没有语音 | VNScriptRunner.Dispatch.cs | 已确认 |
| L5 | 低 | 截图失败时留下旧档的缩略图 | VNSaveSystem.cs:168 | 已确认 |
| L6 | 低 | 照片全屏浏览每翻一次泄漏一个 Sprite | VNAlbumStore.cs:285 | 已确认 |
| L7 | 低 | 照片页打开时同步解码全部 PNG，相册大时卡顿 | VNAlbumStore.cs:237 | 已确认 |
| L8 | 低 | focus / 黑边高度 / speedlines 状态漏存（当前剧本没用到） | VNStage.Fx.cs:17 | 已确认 |
| L9 | 低 | 发布配置与 AI 入口残留 | ProjectSettings / Input.cs | 已确认 |

---

## 致命

### F1. 存档写入非原子、无备份、异常未捕获，坏档还显示成空槽

- **位置**：`VNSaveSystem.cs:159`（`File.WriteAllText`）、`:180-193`（Peek）、`:218-253`（Load）；
  `VNSaveLoadPanel.cs:198`；`VNScriptRunner.SaveLoad.cs:50-55`
- **触发步骤**：
  1. 玩家覆盖一个已有存档，或按 Q 快存（Q 无确认，反复写同一个槽 0）。
  2. 写入瞬间进程崩溃、断电或磁盘满。`WriteAllText` 是先截断再写，原内容此时已经没了。
- **后果**：
  - 该槽内容永久丢失，没有 `.bak` 可恢复。
  - JSON 损坏时 Peek 吞掉异常返回 null，面板把它当成**空槽**，玩家看不到任何损坏提示；
    之后往这里存也不会弹覆盖确认。
  - Load 同样返回 null，提示语是「槽位为空 / 没有快存」，与事实不符。
  - 写盘抛 IOException 时 `Save` 没有 try/catch（只有缩略图有）：异常穿透 `SaveTo`，
    「已保存」和失败提示都不显示。快存路径上 `QuickSaveCo` 中断，缩略图纹理泄漏；
    面板路径上 `RebuildSlots` 不执行。
- **修复方向**：先写 `vn_save_N.json.tmp` 并 Flush，再 `File.Replace` 换入，同时保留 `.bak`。
  读取失败时尝试 `.bak`。Peek 区分「不存在」和「已损坏」，面板显示损坏态。
  Save 捕获异常并返回 bool，由调用方弹失败提示。
- **把握度**：已确认。Grep 确认 VNSaveSystem 内无 tmp/Replace/bak 逻辑。

---

## 高

### H1. 读档杀不掉旧的台词协程：第一下点击被吞、当句存不了档

- **位置**：`VNScriptRunner.Flow.cs:425`（`StopCoroutine(_co)`）、`VNScriptRunner.Dispatch.cs:40-41`
  （`yield return StartCoroutine(co)`）、`VNScriptRunner.Commands.cs:46-56`（NormalSayCo 等待循环）
- **触发步骤**：
  1. 剧本停在一句台词上（绝大多数读档发生的时刻）。
  2. F9 或 L 读档。`Stop()` 只停外层 `Run()`；Unity 不会连带停掉 `StartCoroutine` 启动的子协程，
     旧的 NormalSayCo 仍在 `while (!_advance)` 里转。
  3. 新 `Run()` 启动新的 NormalSayCo，也等同一个 `_advance`。
  4. 玩家点击。两个协程先后检查：先走的那个退出并执行 `_waitingAtSay = false; _advance = false`，
     后走的那个读到 false 继续等。
- **后果**：
  - 若旧协程先走：读档后第一下点击没反应，且 `_waitingAtSay` 已被清掉，这一句按 F5/Q 提示「现在不能存档」。
  - 若新协程先走：剧情前进一句，旧协程留到下一句再吞一次点击。
  - 连续读档 N 次而不推进，旧协程累积 N 个，要吞掉对应次数的点击。
- **修复方向**：Runner 持有 `_runGeneration`，Stop 时递增，各 Co 循环里比对后退出；
  或把子协程句柄存下来逐个 Stop。别用 StopAllCoroutines，它会连带 `_quickSaveCo` 等。
  同一处修复也解决 H5 的「旧 camseq 继续跑」和 stale ChoiceCo/EventCo 残留。
- **把握度**：已确认。Grep 确认 Runner 全部分部里没有 `StopAllCoroutines` 或代数检查。
  依赖的是 Unity 嵌套协程语义，建议顺手跑一次验证。

### H2. 循环音效不进存档、读档也不清；语音同样不停

- **位置**：`VNStage.Save.cs:216-223`、`VNSaveSystem.cs:9-129`（无循环音字段）、`VNAudio.cs:144-169`
- **触发步骤**（真实路径 `第2章.vn.txt:33` `se 雨 loop vol:0.7`）：
  1. 读档到雨声响起之前的旧档 → 雨声一直响，因为只有 `instant`（编辑器调试重建）才调 `ResetForDebug`，
     普通读档不走这条分支。
  2. 反过来：在雨中存档，重开游戏读档 → 雨声没了。
  3. 语音也一样：播放中读档，旧台词的语音继续念完，盖在恢复后的场景上。
- **后果**：氛围音与画面错配，且不会自行恢复，要等剧本里下一条 `se stop`。
  `RebuildStateBefore` 里有 `loopingSe` 字典，调试重建是对的，唯独真读档漏了。
- **修复方向**：VNAudio 增加 `CurrentLoops` 与 `StopAllLoopsAndVoice()`；VNSaveData 加循环音列表
  （id + vol，旧档缺省为空）；RestoreSnapshot 先清后恢复。按 vn-save-compat 三处同步。
- **把握度**：已确认。

### H3. 语音压低/恢复 BGM 时杀掉 `bgm stop` 的淡出，BGM 停不下来

- **位置**：`VNAudio.cs:244-256`（StopBgm）、`:318-329`（SetBgmDucked 的 `active.DOKill()` + `DOFade`）
- **触发步骤**：
  1. 语音播放中 BGM 处于压低状态。
  2. 执行 `bgm stop fade:3`：`_currentBgm` 置空，`_currentBgmGain = 1`，开始淡出。
  3. 语音在 3 秒内结束，`Update` 调 `SetBgmDucked(false)`。此时 `active.isPlaying` 仍为 true，
     于是 DOKill 掉淡出（`OnComplete(src.Stop)` 也一并没了），改为淡回 `bgmVolume × 1`。
  4. 反向同样成立：`bgm stop` 后紧跟 `voice`，`SetBgmDucked(true)` 也会杀掉淡出。
  5. 淡出中调 `SetVolume("bgm")` 走的是同一段逻辑（`:344-347`）。
- **后果**：BGM 在本该停止的地方**以满通道音量继续播放**（增益被重置为 1，可能比原来更响）。
  此时 `_currentBgm == null`，存档记为无 BGM，读回后反而会把它停掉。
  全部剧本里 `bgm stop` 共 4 处、`voice` 共 11 处，具体哪几处相邻未逐一核对。
- **修复方向**：增加 `_bgmStopping` 状态或按 `_currentBgm == null` 判断，SetBgmDucked 与 SetVolume
  在没有当前 BGM 时直接返回。
- **把握度**：已确认。

### H4. 设置面板拖 BGM 音量没有实时效果

- **位置**：`VNAudio.cs:346-347`、`VNScriptRunner.SaveLoad.cs:226-231`、`VNScriptRunner.Input.cs:125`
- **触发步骤**：
  1. 打开设置。`RequestConfigPanel` → `PauseForSaveLoadMenu` 把 `Time.timeScale` 设为 0。
  2. 拖 BGM 滑块。`SetVolume` 执行 `active.DOKill(); active.DOFade(..., 0.3f)`，没有 `SetUpdate(true)`；
     `DOTweenSettings.asset` 里 `defaultTimeScaleIndependent: 0`，这个补间在 timeScale=0 下不走。
- **后果**：玩家拖滑块听不到任何变化，关闭面板后音量才跳到新值。标题画面的设置也走同一入口。
  SE、语音通道是直接赋值，不受影响，所以只有 BGM 显得坏了。
- **修复方向**：`SetVolume` 里的 BGM 分支加 `.SetUpdate(true)` 或直接赋值 `active.volume`。
- **把握度**：已确认。

### H5. 读档不复位镜头，旧 camseq 还在继续跑

- **位置**：`VNScriptRunner.SaveLoad.cs:48-71`（LoadFrom）、`VNStage.Save.cs:167`（RestoreSnapshot）、
  `VNScriptRunner.Flow.cs:257-262`（`RestoreDebugCamera`，全项目唯一的 `SnapReset` 调用）
- **触发步骤**：
  1. 剧本 `camto`/`camcut`/`camseq` 把镜头推近（剧本里相关命令 23 处）。
  2. 此时读档到任何别的位置。RestoreSnapshot 不碰 `vnCamera`，`SnapReset` 只在调试重建里走。
  3. 如果 camseq 还在跑，它是 H1 同类的子协程，`PlayPathCo` 继续驱动镜头。
- **后果**：读档后画面停在放大/偏移状态，或被残留路径继续拖动；`mode:depth/char` 留下的立绘缩放倍率也不还原。
  项目约定是镜头状态不进存档，所以「读档回默认镜头」是预期语义，现状与之不符。
- **修复方向**：LoadFrom 在 RestoreSnapshot 前调 `vnCamera.SnapReset()`（含 `SetMode(Both)`）；
  配合 H1 让 CamseqCo 被取消。若还要叠化层，一并 `cameraFade.Hide()`。
- **把握度**：已确认。

### H6. 剧本数字按系统区域解析，德语等系统下 1.4 变 14

- **位置**：`VNScriptParser.cs:67,76,216,224,239`、`VNEventModule.cs:27`、`VNStage.Characters.cs:23,423`、
  `VNScriptRunner.Dispatch.cs:279`、`VNScriptRunner.Commands.cs:436`、`VNBackgroundScroll.cs:178`、
  `VNCharacterOverlay.cs:129,145`
- **触发步骤**：
  1. 玩家 Windows 区域为德语、西语、意语、葡语、荷语、土耳其语、印尼语等（`.` 是千分位）。
  2. `float.TryParse("1.4", out v)` 走当前区域，结果是 14；`wait 0.5` 等 5 秒，`camto 目标 1.4` 放大 14 倍，
     `bgm fade:1.5` 变 15 秒。
  3. 法语、俄语等 `.` 不是分隔符的区域解析失败，全部静默退回默认值。
- **后果**：这些系统上的玩家看到整部剧的演出参数错误。itch 公开发布面向全球，这个范围不小。
  Grep 确认全项目没有设置 `CurrentCulture`；`VNScreenShake`、`VNExpression` 已用 InvariantCulture，
  说明这是遗漏而不是选择。
- **修复方向**：启动最早处（`RuntimeInitializeOnLoadMethod`）设
  `CultureInfo.DefaultThreadCurrentCulture = InvariantCulture` 并设当前线程，一行堵住全部；
  更稳的做法是加 `VNNum.TryF()` 统一替换。注意对时间显示的影响。
- **把握度**：代码路径已确认；运行待验证（把 Windows 区域改成德语跑 Build 一次即可确认）。

---

## 中

### M1. CG 解锁文件损坏后，下一次解锁把历史全部覆盖

- **位置**：`VNCgUnlocks.cs:31-44`（读失败按空处理）、`:57`（非原子写）
- **触发步骤**：写入 `vn_cg_unlocks.json` 时崩溃（每解锁一张写一次）→ 下次启动 FromJson 抛异常或返回 null →
  `_unlocked` 为空 → 看到下一张 CG 时 `Unlock` 用只含这一张的集合重写文件。
- **后果**：全局 CG 收藏永久丢失。读档时 `ShowCg` 只会重新解锁存档当前那一张，其余看过的 CG 回不来。
- **修复方向**：同 F1 用 tmp + Replace。读取失败时把坏文件改名保留，不要覆盖。
- **把握度**：已确认。

### M2. saveVersion 无人读取，读档靠裸命令索引，更新剧本后跳错行

- **位置**：`VNSaveSystem.cs:11-12`、`VNScriptRunner.SaveLoad.cs:70`、`VNScriptRunner.Flow.cs` 的 `ResumeAt`、RestoreCallStack
- **触发步骤**：
  1. 玩家用 v0.1 存档。
  2. 在 itch 发布 v0.2，第 1 章前面加了几行。
  3. 读旧档，`commandIndex` 指向新脚本里的另一条命令；call 栈的 `returnIndex` 同理。
- **后果**：从错误位置续播（可能跳过选项、重复执行 flag/stat），没有任何提示。
  Grep 确认 `saveVersion` 只有声明，全项目没有比较或迁移逻辑。
- **修复方向**：存档增加 `sourceLine`、台词文本 hash、脚本 hash。读档时校验落点是否仍为同一句 say；
  不一致则就近找同 hash 的台词，找不到再提示「存档来自旧版本」。试玩版更新前至少先做版本提示。
- **把握度**：已确认。

### M3. 默认皮肤下每次读档，正文与名牌往右累加偏移

- **位置**：`VNStage.Save.cs:173-181`（字符串比较）、`VNDialogueBox.cs:254,280,289-290`（Bind 采样基准）、`:714-719`
- **触发步骤**：
  1. 使用程序化默认对话框皮肤（`CurrentDialogueSkinId == null`），角色说话时头像显示
     （`showPortrait` 默认 true），`_bodyRect.offsetMin` 已含 244px 避让。
  2. 存档时 null 被 JsonUtility 写成 `""`，读回 `data.dialogueSkin == ""`。
  3. `"" != null` 为真，每次读档都走 `SetUiSkin("dialogue","default")` → `ApplySkin(null)` → `Bind`。
  4. Bind 把**已含避让的当前值**采样成基准，ApplyPortrait 再加一次 244px。
- **后果**：每读档一次，正文左缩进和名牌右移再加 244px，几次之后正文区被挤得很窄、名牌跑到中间，
  旁白行也回不去。继续箭头基准也会被采样到浮动中途，下移最多 7px。
  试玩主线第 1 章第 46 行才切 `黑渐变`，之前的段落和 `ui dialogue default` 之后的段落会命中。
  同一路径的 `ui dialogue default` 不读档也会触发一次。
- **修复方向**：比较时把 null 和空串归一（`string.IsNullOrEmpty`）；Bind 里采样基准只做一次
  （按 skin 实例缓存），或在采样前先减去当前避让量。
- **把握度**：已确认。依赖 JsonUtility 把 null string 写成空串的既有行为。

### M4. 全屏切换读回旧值，标签和保存值都错

- **位置**：`VNConfigPanel.cs:255-260`
- **触发步骤**：点全屏按钮。`Screen.fullScreen = !Screen.fullScreen` 要到帧末才生效，
  紧接着读 `Screen.fullScreen` 拿到的是旧值。
- **后果**：按钮文字不变；`PlayerPrefs` 存的是切换前的状态，下次启动恢复成相反模式。
- **修复方向**：先用局部变量 `bool next = !Screen.fullScreen`，用 next 写 PlayerPrefs 和标签。
- **把握度**：待验证（依据 Unity 文档对切换时机的说明）；修复没有副作用，可直接改。

### M5. `volume` 命令改写玩家音量，且不进存档

- **位置**：`VNAudio.cs:336-364`、`VNSaveSystem.cs`（无字段）、`VNConfigPanel.cs:130-155`、
  `第1章.vn.txt:155-156`（`volume bgm 0.07` / `volume se 0.9`）
- **触发步骤**：
  1. 玩家在设置里调好 BGM 音量。
  2. 剧情走到第 1 章第 155 行，通道音量被直接写成 0.07，几乎静音。
  3. 设置面板的滑块只在 Build 时赋值一次，`RefreshValues` 不刷新，显示的还是旧值；PlayerPrefs 也是旧值。
  4. 读档到第 155 行之前：仍是 0.07。重开游戏读第 155 行之后的档：回到 PlayerPrefs 的值。
- **后果**：玩家设置被剧本悄悄覆盖；同一段剧情的音量取决于这次是否经过第 155 行。
- **修复方向**：把「玩家通道音量」和「剧本音量乘数」分成两层，后者进存档。
  如果第 155 行本意是临时压低，可改用 `bgm play ... vol:`。
- **把握度**：已确认。

### M6. 存档记录立绘的瞬时 x，异步 move 时读档站错位

- **位置**：`VNStage.Save.cs:153`、`VNEntranceAnimator.cs`（`_basePos` 才是目标位）、`第1章.vn.txt:107`
- **触发步骤**：`move 星野结衣 left 0.9 @` 异步滑动中，下一句台词已打完（点击催促可在 0.9 秒内完成），
  此时存档，`rect.anchoredPosition.x` 是途中坐标。入场预设配 `@` 时同样成立（slidein 偏移可达数百像素）。
- **后果**：读档后角色停在半路，直到下一次 `move` 或 `show at:`。y 不受影响，因为是重建的。
- **修复方向**：ActiveCharacter 记录逻辑目标位（Show/Move 时写入），CaptureSnapshot 存它。
- **把握度**：已确认。当前剧本只有 1 处异步 move，命中窗口窄。

### M7. 转场回调在读档之后把背景改回去

- **位置**：`VNStage.Scene.cs:115-119`、`VNScreenTransition.cs:313-315`、`VNStage.Save.cs:248`（ClearStage 不调转场清理）
- **触发步骤**：
  1. `bg B transition:... via:black`，或白闪/光斑/眨眼这类走 `transition.Play` 的转场，处于前半段。
  2. 玩家按 L 或 F9 读档（读档不限于台词处）。RestoreSnapshot 把背景设为存档值。
  3. 转场序列继续走，`onCovered` 执行 `ApplyBackground(sprite)`，背景被改回 B。
- **后果**：`CurrentBackgroundId` 是存档值，画面却是 B，两者不一致一直持续到下一条 `bg`。
  默认的直接过渡路径换图是立即发生的，不中招，只是残留图层多留一会儿。
- **修复方向**：把 `StopCurrentTransition` 公开，ClearStage 调用。
- **把握度**：已确认。Grep 确认 Stage 和 Runner 没有调用转场清理。

### M8. Auto/Skip 在除回想外的面板打开时继续推进剧情

- **位置**：`VNScriptRunner.Commands.cs:46-53`
- **触发步骤**：开 Auto，按 J/C/I/G/D 打开任务/属性/背包/画廊/日记。这些面板不暂停，
  而 NormalSayCo 只判断 `_backlog.IsOpen`。
- **后果**：剧情在面板后面自动往下走，错过台词；Skip 时甚至会走到选项或事件模块。
- **修复方向**：提取 `AnyBlockingPanelOpen`（`IsSecretPhotoIconAllowed` 里已有同一组判断），等待循环共用。
- **把握度**：已确认。

### M9. 快捷条在事件模块中缺少保护，能开读档/设置/Skip

- **位置**：`VNQuickToolbar.cs:114-135`、`VNScriptRunner.SaveLoad.cs:174,226`、`VNScriptRunner.Input.cs:163,171,201`、
  `VNDialogueBox.cs:672-673`
- **触发步骤**：
  1. `toolbarPlacement: 1`（右下角自动隐藏）是当前配置。工具条脱离对话框，`HideBox` 不影响它，
     VNEdgeAutoHide 按鼠标坐标滑出，不走射线。
  2. 事件层没有全屏拦截（`VNEventRegistry`/`VNEventModule` 无 raycast 底板，
     `event interact` 明确不铺暗幕），工具条按钮可点。
  3. Quest/Stats/Inventory/Gallery 入口有 `_eventActive` 判断，Backlog、Load、Config、QuickLoad、Auto、Skip、HideUi 没有。
- **后果**：
  - 打开读档/设置时 timeScale=0，但模块用 unscaled 计时，倒计时在面板后继续走。
  - `Update` 第 201 行提前 return，ESC 关不掉这些面板，只能点按钮。
  - 点 Skip 后 `DOTween.timeScale` 变 4 倍且无法用键盘解除，事件后仍生效。
  - 点隐藏 UI 后 `Update` 不处理恢复，模块里的台词看不见。
- **修复方向**：`Execute` 开头 `if (_runner.IsEventActive) return;`，或事件期间 `_corner.SetSuppressed(true)`。
- **把握度**：已确认。全屏 UI 型模块（如羽毛球）若有自己的拦截层，影响较小，未逐个核对。

### M10. 非 16:9 屏幕背景露边

- **位置**：`Assets/Project/Scenes/VNScriptDemo.unity`（`ackImage`：中心锚点固定 1920×1080；CanvasScaler match 0.5）
- **触发步骤**：`fullscreenMode: 1` + 原生分辨率，玩家是 16:10 笔记本。计算得画布约 1821×1138，
  背景高 1080，上下各缺约 29px；21:9 约 2217×935，左右各缺约 148px。
  相机没有 Viewport 限制（Grep 无 `camera.rect`/`SetResolution`），`resizableWindow: 0`。
- **后果**：边缘露出相机清屏色（近黑），Ken Burns 最小缩放是 1.0，露边宽度还会随漂移变化，
  粒子和对话框却铺到全屏，不像规整的黑边。
- **修复方向**：相机按 16:9 设 Viewport 做真黑边；或背景改 cover 铺满。
- **把握度**：待验证（数值是按 CanvasScaler 公式算的，未运行）。

### M11. 任务面板 ☑/☐ 三款字体都缺字，完成与否看不出

- **位置**：`VNQuestLog.cs:456`、`VNFont.cs:84-87`、`TMP Settings.asset`（`m_fallbackFontAssets: []`）
- **触发步骤**：打开 J 面板看带子目标的进行中任务。用 fontTools 查 cmap：
  U+2610、U+2611 在 NotoSansSC、NotoSansJP、LXGW WenKai 中都不存在。
- **后果**：两种状态都显示缺字方框，玩家分不清子目标完成了没有。
  这是本次 51 个 UI 符号里仅有的两个玩家可见缺字（第三个 ✘ 只在 AI 日志里）。
  26 个 `.vn.txt` 剧本正文无缺字（BOM 除外）。
- **修复方向**：换成字体有的字符（如 ✓ 需先确认，或 ■/□、●/○），或用 Image/TMP Sprite。
- **把握度**：已确认。

### M12. 读档失败路径：flag 已被覆盖、剧本已停、无提示

- **位置**：`VNScriptRunner.SaveLoad.cs:50-64`
- **触发步骤**：读一个章节已改名的存档（配合 M2 的版本更新场景）。`VNSaveSystem.Load` 已经清空并写回 flag，
  `Stop()` 已杀掉剧本，然后 `SwitchChapter` 返回 false，只打一条 Debug.LogError 就 return。
- **后果**：正在玩的进度被破坏，画面停在原处，点击无反应，玩家看不到提示。试玩 Build 里 LogError 用户不可见。
  `stage.RestoreSnapshot` 抛异常时也是同样的死局。
- **修复方向**：先 Peek 校验 chapter 能否解析，再改全局状态；失败时弹提示并保持原进度。
- **把握度**：已确认。

---

## 低

### L1. 每次读档/进事件都弹「自动模式关闭」

`VNScriptRunner.Input.cs:163-169` 的 SetAuto 没有 `SetSkip` 那样的同值判断，LoadFrom、EventCo、SnsCo 都无条件调 `SetAuto(false)`。
**触发**：Auto 从未开过时读档或进小游戏 → 左上角弹 Toast。**修复**：加 `if (_auto == on) return;`。已确认。

### L2. 同曲不同音量的 PlayBgm 被忽略

`VNAudio.cs:219` 只比较 id。**触发**：存档 BGM 是 X vol:0.5，当前 X vol:1，读档后音量不变；剧本 `bgm play X vol:0.3` 同理。
**修复**：同 id 时若 vol 不同，补间到新音量。已确认。

### L3. 回想记录不随读档回退

`VNBacklog.cs` 只有 Record/Open，没有清空接口，也不进存档。**触发**：读旧档后按 H，看到「未来」的台词；重启读档后回想为空。
**修复**：读档时清空或存最近 N 条。已确认。

### L4. 读档后第一句没有语音

恢复点是 say 本身，紧邻其前的 `voice` 不重放（`Dispatch` 的 voice 分支）。**触发**：在有语音的句子存档后读档 → 这句无声，口型也不跟语音。
**修复**：say 记录绑定的 voice id，恢复时重播。已确认。

### L5. 截图失败时留下旧档的缩略图

`VNSaveSystem.cs:168` 在 thumbnail 为 null 时直接 return，旧 PNG 还在。**触发**：覆盖存档时截图未取到 → 新档显示旧图。
**修复**：null 时删除旧 PNG。已确认。

### L6. 照片全屏浏览每翻一次泄漏一个 Sprite

`VNAlbumStore.cs:285-294` 每次新建 Sprite；`VNCgGallery.cs:360` 赋值后不销毁旧的，`ClearCache` 只管 texture 和缩略图。
**触发**：长时间翻相册。**修复**：Gallery 持有当前 Sprite 并在替换和关闭时销毁。单个 Sprite 很小，影响有限。已确认。

### L7. 照片页打开时同步解码全部 PNG，相册大时卡顿

`VNAlbumStore.cs:237-282` 主线程解码后逐像素 `GetPixelBilinear`；`VNCgGallery.Close` 清缓存，所以每次打开都重来。
**触发**：相册接近 200 张时打开照片页。**修复**：缩略图落盘缓存或分帧加载。试玩版张数少，影响小。已确认。

### L8. focus / 黑边高度 / speedlines 状态漏存（当前剧本没用到）

`VNStage.Fx.cs:17` 的 ToggleFxNames 不含 focus，读档不清也不存；`letterbox height:` 读档后回默认高度；
`fx speedlines burst` 把 on 状态写成 false（`VNStage.Fx.cs` 的 Fx 入口）。
Grep 确认当前 26 个剧本没有 `fx focus` 和 `letterbox ... height:`。**修复**：写到相关剧本前补上。已确认。

### L9. 发布配置与 AI 入口残留

- `ProjectSettings.asset`：`companyName: DefaultCompany`、`productName: My project`。存档与 PlayerPrefs 路径都由它决定，
  发布后再改会让玩家存档「消失」，且可能与其他默认名工程共用路径。要在首发前定好。
- `VNScriptRunner.Input.cs` 的 D 键在试玩版仍打开 AI 日记本，而试玩不开放 AI 聊天。
- `forceSingleInstance: 0`：双开时两个进程可写同一存档，低概率。

已确认。

---

## 检查后未发现问题的方向

- 快存/读档竞态：`CaptureThumbnailCo` 在同帧 WaitForEndOfFrame 完成，`QuickSaveCo` 又二次校验 `_waitingAtSay`，无实际窗口。
- 存档面板 `_menuPaused` 与 timeScale 恢复：各关闭路径都收口到 `OnSaveLoadPanelClosed`，未发现卡在 0。
- VNToast、VNEdgeAutoHide：不吃射线，感应走坐标，未发现点击穿透。
- 字体：三份动态 TMP 资产都带 SourceFontFile、开了多图集、字体文件 includeFontData=1。
- 打字机：空文本与无可见字符有兜底，不会卡住 SayCo。
- 选项面板：`DelayedCall` 在 ForceClose 后仍会触发，但 `_callback` 已清空，当前流程下没有可达的新选项被误清路径。
- 液体、天气、CG 暂停的快照字段与恢复路径：逐项对照未发现漏存。

## 已读文件

**完整读完（Script/）**：VNSaveSystem、VNSaveLoadPanel、VNAudio、VNConfigPanel、VNQuickToolbar、VNEdgeAutoHide、VNToast、
VNUi、VNUiAnchors、VNUiParts、VNFont、VNGameConfig、VNCgUnlocks、VNCgGallery、VNAlbumStore、VNCharacterDef、VNCharacterBlink、
VNCharacterBlinkOverlay、VNCharacterMouth、VNCharacterMarks、VNCharacterOverlay、VNCharacterImprints、VNNameplateStyle、
VNSystemUiSkinBehaviour、VNSystemUiSkinSet。

**完整读完（VNEffects/）**：VNDialogueBox、VNChoicePanel、VNTypewriterText；额外读了 VNCameraFade、VNVignetteFocus。

**拆分前完整读完**：VNStage.cs（1702 行）。拆分后的 `.Characters/.Fx/.Save/.Scene` 未重读，靠逐行比对确认是搬移。

**VNScriptRunner 已读部分（拆分前）**：第 1-680、915-1159、1380-3185 行，覆盖存档快照/恢复、调试重建主体、输入、主循环、Dispatch 和各 Co。

## 未读完的部分

- VNScriptRunner 拆分前的 681-914 行（SNS 静默重放、RebuildShowState 等调试辅助）和 1160-1379 行（flag/time/跨文件跳转等）。
- `VNSaveData` 涉及的 `VNSnsView.CaptureSnapshot/RestoreSnapshot`、`VNAiMemory`、`VNBackgroundScroll.RestoreState`、
  `VNInterludeScreen.PlayCo`（读档时 stale 协程的影响）未展开。
- VNScreenTransition 只读了第 190-324、428-457 行；VNEntranceAnimator、VNScriptParser、VNEventRegistry、VNInteractionModule、
  VNQuestLog、VNTitleMenu、VNBacklog 只做了定向 Grep。
- `VNSaveLoadSkin`、`VNConfigPanelSkin` 等皮肤槽位类和实际 prefab 未读，分辨率适配只验证了主背景，未逐面板验证。
- 全部结论未经运行验证。

---

# 审计第 3 组：VNEffects 根目录脚本 + Assets/Art/Shaders

范围：`Assets/Project/Scripts/VNEffects/*.cs`（48 个）与 `Assets/Art/Shaders/*.shader`（9 个）。只读审计，以当前工作区为准。

**结论先行：现在直接打包，试玩版画面基本不可用。** 9 个自定义 shader 里有 6 个进不了 Build，其中 `VN/ImageEffect` 是所有立绘的基础 shader，`VN/RetroFilter` 和 `VN/Shockwave` 缺失时还会留下全屏白板。这两条在编辑器里完全看不出来，因为 Editor 下 `Shader.Find` 总能找到。项目里没有任何既有 Build 产物（根目录无 Build、无 `*_Data`、无 `globalgamemanagers`），所以这条线从未被实测过。

| 严重度 | 数量 |
|---|---|
| 致命 | 2 |
| 高 | 4 |
| 中 | 6 |
| 低 | 5 |
| 合计 | 17 |

---

## 致命

### F1. 6 个自定义 shader 不会进 Build，`VN/ImageEffect` 缺失导致所有立绘无法显示

- **位置**：`Assets/Project/Scripts/VNEffects/VNImageEffectController.cs:109`（`Shader.Find`）、`:136`（`Mat.SetFloat` 无判空）；`ProjectSettings/GraphicsSettings.asset:29-36`
- **核实过程**（对全部 `.mat/.asset/.prefab/.unity/.shadervariants` 按 shader GUID 与材质 GUID 搜索）：
  - `GraphicsSettings.asset` 的 Always Included 只有 7 个内置 shader（guid 全为 `0000…f000…`）。
  - `Assets/Resources` 下没有 `.shader/.mat/.prefab`；工程内无 `.shadervariants`；`preloadedAssets: []`；`Assets/Project` 下无 `IPreprocessBuild` 或操作 Always Included 的代码。
  - 唯一 Build 场景是 `VNScriptDemo.unity`（`EditorBuildSettings.asset`）。

| shader | 引用来源 | 进 Build？ | 缺失后果 |
|---|---|---|---|
| VN/Additive | VNAdditive.mat，场景引用 12+2 次 | 是 | — |
| VN/ParticleAlpha | VNParticleAlpha.mat，场景引用 1 次 | 是 | — |
| VN/ScreenTransition | VNScreenTransition.mat，场景引用 1 次 | 是 | — |
| **VN/ImageEffect** | VNImageEffect.mat 存在，但场景与资产引用 **0 次** | **否** | 见下方触发步骤 |
| **VN/RetroFilter** | 无任何引用 | **否** | 见 F2 |
| **VN/Shockwave** | 无任何引用 | **否** | 见 F2 |
| **VN/DirectBackgroundTransition** | 无任何引用 | **否** | 卷页/碎裂/水波/墨染退回黑场转场（`VNStage.Scene.cs:103` 有兜底） |
| **VN/FogWipe** | 无任何引用 | **否** | 擦雾雾层变成不透明清晰 CG（`VNFogWipeModule.cs:634` 只报错），谜底直接揭晓，玩法失效 |
| **VN/TutorialMask** | 无任何引用 | **否** | 教程退化为整屏压暗、无挖洞高亮（`VNTutorialMask.cs:80` 有兜底） |

- **触发步骤**：打包 → 开始游戏 → 执行任意 `show`。
  1. `CreateCharacter` 加 `VNImageEffectController`，`Shader.Find("VN/ImageEffect")` 返回 null，`_mat` 留空。每次访问 `Mat` 都重新 Find 并打一条 LogError。
  2. `VNEntranceAnimator.PrepareHidden` 先写 `_group.alpha = 0`（`:124`），再 `_fx.SetDissolve(0f)`（`:125`）→ `Mat.SetFloat` 抛 NRE。
  3. `VNScriptRunner.Dispatch.cs:35` 的 catch 吞掉异常，剧本继续往下走。
- **后果**：游戏不卡死，但**所有立绘永远不显示**（alpha 停在 0），`VNFootShadow.LateUpdate:109` 的 `GetDissolve()` 每帧抛 NRE（Player.log 持续膨胀、帧率下降，脚影停在初始不透明度留在舞台上）。擦雾玩法失效，教程没有高亮。
- **修复方向**：把这 6 个 shader 加进 Always Included；或在 `Assets/Resources` 放一个序列化引用 6 个 Shader 的资产，并让代码走引用而非 `Shader.Find`。另外 `Mat` 找不到 shader 时只报一次并缓存失败状态，`SetDissolve/GetDissolve` 这类接口判空。**打包后必须实机验证一次。**
- **把握度**：已确认（静态核实全部引用路径）；Build 内实际表现待首次打包验证。

### F2. `VN/RetroFilter`、`VN/Shockwave` 缺失时留下全屏不透明白板，开始游戏即白屏

- **位置**：`VNRetroFilter.cs:93,103-108,114`；`VNScreenShockwave.cs:83,93-98,103`
- **触发步骤**：Build 中两个组件都在 `Canvas` 下激活（`Canvas/RetroFilter`、`Canvas/ScreenShockwave`，`sourceMaterial` 均为空）→ Awake 调 `Build()` → 先把 RawImage 贴成 `Texture2D.whiteTexture` → shader 为 null 时直接 `return`，走不到 `_img.enabled = false`。之后 `SetMode`/`Play` 在 `_mat == null` 时也提前返回，永远不会隐藏。
- **后果**：两张全屏白板分别在排序 26 和 34，盖住背景、立绘、粒子。标题菜单（覆盖层）在上面时看不出，点「开始」后舞台全白，只剩排序 40 以上的对话框等 UI。**这条是 F1 的放大器，也是代码本身的兜底错误**：即使加了 Always Included，这个失败路径也应修掉。
- **修复方向**：RawImage 创建时立刻 `enabled = false`，或把关闭操作放到 shader 判断之前。`VNScreenTransition.cs:97-113` 是同样写法（且 `raycastTarget = true`），当前有材质引用所以不触发，建议一并改。
- **把握度**：已确认（代码路径走通，且已核实场景里组件激活、无材质引用）。

---

## 高

### H1. Build 场景里背景根本没接上特效控制器：mood/天气/景深/波光/热浪/水波对背景全部无效

- **位置**：`Script/VNStage.cs:267-270` 与 `:277-278`；`VNScriptDemo.unity`（`VNStage.backgroundFx` 第 10327 行、`VNFakeDoF.backgroundFx` 第 28133 行、`VNMoodGrading.backgroundTargets` 第 28083 行、`VNWeatherController.moodTargets` 第 16201 行、`VNScreenShockwave.targets` 第 15463 行、`VNHeatHaze.targets` 第 15948 行，全部为空）
- **触发步骤**：场景里没有任何 `VNImageEffectController`。`AutoWire` 第 268 行 `GetComponent` 取到 null，第 270 行 `RegisterBackground` 被跳过；直到第 278 行才 `AddComponent<VNBackgroundScroll>`，靠 `[RequireComponent]` 顺带把控制器挂上。此时 `backgroundFx` 已不会再赋值。
- **后果**：在 Editor 和 Build 里都成立。
  - `mood Night/Sunset/Memory` 只染立绘（30% 强度），背景颜色完全不变，回忆/夜晚氛围基本失效，且立绘与背景色调脱节。
  - `weather` 调色无效（`moodTargets` 为空）。
  - `VNFakeDoF` 不模糊、不压暗背景，只放大层。
  - `fx shimmer` 无效（`VNStage.Fx.cs:308` 直接 break）；`fx haze` 只有雾粒子没有扭曲；`fx shockwave` 没有画面荡动。
- **修复方向**：把 `AutoWire` 中补挂 Ken Burns/滚动组件的代码移到 `backgroundFx` 赋值之前，或在此处显式 `GetComponent ?? AddComponent`；并把 `backgroundFx` 同步给 `VNFakeDoF`、`VNScreenShockwave.targets`、`VNHeatHaze.targets`、`VNWeatherController.moodTargets`。或者直接在场景里挂好接线。**接通后 M3 会立刻暴露，两条要一起修。**
- **把握度**：已确认（场景 YAML 与代码顺序都已核实）。

### H2. 背景图没有溢出余量，Ken Burns、视差、运镜都会露出底色

- **位置**：场景 `BackImage`（GameObject 名实为 `ackImage`）RectTransform：中心锚点、`SizeDelta 1920×1080`、无溢出；`VNKenBurns.cs:23,27,93-95`；`VNParallax` 场景配置 LayerBack strength 8；`VNCamera.cs:200-201,229`（场景序列化 `overscan: 60,60`）
- **触发步骤**：
  - Ken Burns 在运行时由 `VNStage.cs:273` 自动挂上，默认开启，缩放 1.0~1.06、平移半径 40px。注释写「背景四边有 60px 溢出余量」，但这个场景余量是 0。例如缩放落在 1.02 时横向余量只有 19px、纵向 11px，平移 40px 必然露边。
  - 鼠标移到屏幕边缘，LayerBack 视差最多位移 8px，缩放为 1.0 时立即露边。
  - `camto`/`camseq` 的防露边钳制按 `overscan=60` 计算，实际可推到露出 60px。
- **后果**：画面边缘出现深色（相机底色 0.03,0.03,0.06）条带，持续数十秒。立绘层不受影响，所以看起来就是背景图比屏幕小。
- **修复方向**：把背景放大到 2040×1200（或按项目约定四边留 60px），且 `VNCamera.overscan` 按实际尺寸计算。`VNSecretPhotoMode.EffectiveOverscan()`（`:343`）已经为这个问题做过修补，但只修了偷拍一条路径。
- **把握度**：已确认（尺寸与数值来自场景 YAML）。

### H3. 立绘背后光环不跟随角色，固定在舞台中央闪烁

- **位置**：`VNGlowBackdrop.cs:60`；`Script/VNStage.Characters.cs:335,358,81`；`VNSpeakerHighlight.cs:72`
- **触发步骤**：`CreateCharacter` 在第 358 行 `AddComponent<VNGlowBackdrop>`，Awake 立即把光环位置抄成角色初始位置（`(0,-60)+offset`）。之后 `Show` 在第 81 行才把角色放到 `at:left/right` 站位；`move` 同样不更新光环。Grep 确认全项目没有其他代码更新 `_GlowHalo` 的位置。
- **后果**：`show X at:left` 后，光环留在舞台中央。`Say` 每句都调 `SetSpeaker` → `glow.Flare(1.5f, 0.7f)`，于是说话时画面中央闪一团 1.45 倍立绘大小的 HDR 光，与说话者位置无关；只有站在 middle 的角色才正常。
- **修复方向**：在 `VNGlowBackdrop.LateUpdate` 里每帧同步光环的 anchoredPosition（同 `VNFootShadow` 的做法）。
- **把握度**：已确认（位置错误已确认；视觉显眼程度待运行确认）。

### H4. 图案式背景过渡的叠加层是 Overlay 队列，转场期间可能把立绘盖住

- **位置**：`VNScreenTransition.cs:208,219,359`；`Assets/Art/Shaders/VNScreenTransition.shader:45`（`Queue=Overlay`）；同类结论见 `VNTutorialMask.shader:32-36` 的注释
- **触发步骤**：`bg`/`cg` 带 NoiseDissolve、Blinds、Tiles、CircleWipe、InkSpread 等过渡且不写 `via:black` → `PlayBackgroundPattern` 把旧背景复制成 Image 放进 LayerBack。该 Image 用 VN/ScreenTransition 材质（队列 4000），而立绘在同一主 Canvas（排序 0，无嵌套 Canvas）内，走普通 UI 队列 3000。
- **后果**：按项目自己在 TutorialMask 里记录的规律，同排序下队列大的后画，旧背景副本会画在立绘、光束之上：转场开始立绘瞬间消失，随图案散开才露出。`VN/DirectBackgroundTransition`（Transparent 队列）的四种过渡没有这个问题。
- **修复方向**：贴图模式改用 Transparent 队列（给 `CreatePatternMaterial` 的材质设 `renderQueue = 3000`，或拆一个 shader 变体）。
- **把握度**：待验证（队列数值已确认，覆盖结果需运行确认）。

---

## 中

### M1. 连续台词让立绘永久停止呼吸

- **位置**：`VNImageEffectController.cs:470-490`；`VNSpeakerHighlight.cs:67`；`Script/VNStage.Scene.cs:22`；`VNCamera.cs:75`
- **触发步骤**：`ApplyScaleMultipliers` 用 `wasBreathing = _breathScaleX != null` 判断是否要恢复呼吸，并杀掉呼吸补间，把恢复动作挂在 `_scaleTween.OnComplete` 上。`SetSpeaker` 每句台词都对所有在场角色调用它（过渡 0.35 秒）。如果 0.35 秒内来第二句（快速点击、Auto 短句、Skip）：第二次调用时 `_breathScaleX` 已是 null，`wasBreathing=false`；第一条 `_scaleTween` 被 Kill，OnComplete 不触发；第二条没挂恢复。
- **后果**：`IsBreathingMotion` 永远为 false，情绪动作也不再恢复，立绘的呼吸动作停止，直到重新 `show`。`camseq mode:depth/bg/char` 多段路径同样触发（每段 `DOCamScaleMultiplier` 都走这里）。
- **修复方向**：用独立字段 `_restartBreathPending` 记录「待恢复」，不依赖 tween 引用。
- **把握度**：已确认。

### M2. 非 16:9 显示器背景不铺满，运镜与立绘按 1920×1080 写死

- **位置**：`VNScriptDemo.unity` 的 CanvasScaler（第 16930-16935 行：ScaleWithScreenSize、Match 0.5）；`VNCamera.cs:198`（`canvasHalf` 固定 960,540）；`VNDutchAngle.cs:24`；全项目无 `SetResolution`/`camera.rect`（Grep 确认）
- **触发步骤**：`fullscreenMode: 1` + `defaultIsNativeResolution: 1`，全屏按显示器原生比例运行。
  - 16:10（1920×1200，常见笔记本）：画布约 1821×1138，背景 1080 高，上下各露约 29 逻辑像素。
  - 21:9（2560×1080）：画布约 2217×935，左右各露约 148 像素，同时上下裁掉约 72 像素，立绘头脚可能被切。
- **后果**：背景四周露底色，全屏拉伸 UI（黑边、对话框）与背景宽度不一致，荷兰角防露角公式按 16:9 算。
- **修复方向**：相机加黑边适配（按比例设 `camera.rect`）或强制 16:9 窗口，或背景改 cover 铺满。
- **把握度**：已确认（数值由 CanvasScaler 公式算出，实际观感待运行）。

### M3. 直接过渡的旧背景副本丢失全部材质状态

- **位置**：`VNScreenTransition.cs:340-360`（`CreateImageOverlay`）、`:362-379`；`Script/VNStage.Scene.cs:98-113`
- **触发步骤**：背景有 mood 调色/天气调色/模糊/波浪/滚动偏移时切背景（默认走直接过渡）。副本只抄 sprite 和 RectTransform，用的是转场材质，没有 `_Brightness/_ColorFilter/_BlurAmount/_ScrollOffset`。
- **后果**：`mood Night` 下换背景，第一帧旧背景突然变回未调色的亮图，随后过渡到调暗的新图，形成一次亮闪。滚动中的背景也会跳到偏移 0。当前因 H1 背景没有调色所以被掩盖，**修 H1 后立刻出现**。另外这两条直接路径绕过 `ApplyBackground`，`bgScroll.ResetOffset()` 不执行，与 `:130` 注释不符。
- **修复方向**：在过渡 shader 里补调色参数并从 `VNImageEffectController` 同步，或把旧图渲染成带调色的副本；直接路径的回调里补 `ResetOffset`。
- **把握度**：已确认（代码路径），观感待运行。

### M4. `bgscroll off` 结束时背景瞬移

- **位置**：`VNBackgroundScroll.cs:127,149-150`；`VNImageEffect.shader:202`
- **触发步骤**：`bgscroll on` 运行一段时间后 `bgscroll off`。速度缓到 0 时 `Apply()` 把 `_ScrollMode` 设为 0，shader 的 `vnScrollUV` 直接返回原始 uv，累计偏移被忽略，`_offset` 也未归零。
- **后果**：缓停做得很平滑，最后一帧却跳回未滚动的画面。下次 `bgscroll on` 又跳回旧偏移。代码注释说要避免「急停」，实际变成跳变。
- **修复方向**：停止后保持滚动分支并冻结偏移，只在换图时才关分支。
- **把握度**：已确认。

### M5. 基准缩放可能在入场动画中途被缓存

- **位置**：`VNImageEffectController.cs:430-435`；`VNEntranceAnimator.cs:346,375`；`VNCharacterEmotes.cs:71`
- **触发步骤**：`_origScale` 在第一次调用时取当前 `localScale`。`show X with:ScaleBounce @` 或 `with:FlashBloom @` 异步入场，下一句台词立刻触发 `DOScaleMultiplier`（或异步情绪动作），此时立绘缩放处于 0.65~1.07 倍的动画中。
- **后果**：`_origScale` 被污染，该角色在对象生命周期内一直偏大或偏小，呼吸也围绕错误基准。
- **修复方向**：`VNImageEffectController` 在 Awake 时缓存，或由 `VNEntranceAnimator.CacheBase` 显式写入。
- **把握度**：已确认。

### M6. `SetGrade` 的返回值塞进 Sequence 后无法被打断，旧补间覆盖新调色

- **位置**：`VNImageEffectController.cs:272`；`VNCharacterEmotes.cs:140,153,196,200`；`VNEntranceAnimator.cs:487`
- **触发步骤**：`Sequence.Join(SetGrade(...))` 后该补间成为嵌套补间。已用反射读取工程内 `DOTween.dll` 的 `TweenExtensions.Kill` IL，确认它检查 `isSequenced` 后走 `LogNestedTween` 分支，**对嵌套补间的 Kill 不生效**。例如 `emote 小雪 Dejected @` 紧接台词：情绪动作的 0.6 秒调色还在跑，`SetSpeaker` 的 0.35 秒 Focus 调色结束更早，旧补间最后写入不含新 Focus 的旧目标。
- **后果**：说话者/旁听者明暗在这一句台词内错误，`_appliedGrade` 同步错误，直到下一次 `SetGrade`。
- **修复方向**：SetGrade 不把内部补间暴露给 Sequence，改用独立补间加 `AppendInterval`；或在写入回调里用代次号校验，过期补间不写入。
- **把握度**：已确认（Kill 行为已确认，观感待运行）。

---

## 低

### L1. `Tremble` 的颜色恢复不是渐变

- **位置**：`VNCharacterEmotes.cs:196-200`
- **触发步骤**：`SetGrade(Emote, 冷色)` 与 `ClearGrade(Emote, 0.4f)` 都在构建 Sequence 时同步执行：Emote 通道从第一帧起就是 Identity，第二条补间的 `from` 是构建时的旧值（未变冷）。
- **后果**：冷色在 0.3 秒内正常出现，但收尾时从冷色**一帧跳回**原色，「慢慢回来」的 0.4 秒不可见。期间若有其它 SetGrade，目标值里不含冷色。
- **修复方向**：用 `AppendCallback` 在收尾时才调 `ClearGrade`。
- **把握度**：已确认。

### L2. 自发光被注释掉，所有发光动作无效

- **位置**：`Assets/Art/Shaders/VNImageEffect.shader:315`；受影响 `VNImageEffectController.cs:189,208`、`VNCharacterEmotes.cs:112,125`、`VNEntranceAnimator.cs:527`
- **触发步骤**：`color.rgb += _EmissionColor.rgb * _EmissionAmount;` 被注释。
- **后果**：`生气` 的红色脉冲、`害羞` 的粉色光晕、呼吸发光都没有画面，补间却仍在每帧运行。可能是有意关闭，但类注释和文档仍描述该效果，需确认。
- **修复方向**：恢复这一行，或删除相关调用与文档描述。
- **把握度**：已确认。

### L3. 雨和雪用了加法混合，违反粒子硬约定

- **位置**：`VNAmbientParticles.cs:111,298,327`；`VNWeatherController.cs:225-230`
- **触发步骤**：`weather Rain/Snow` 在亮背景上播放。CLAUDE.md 规定雨雪是实体，必须用 `VN/ParticleAlpha`。
- **后果**：亮背景上雨雪被洗白、看不清；hdrBoost 1.8 叠加 Bloom 会泛白。
- **修复方向**：雨雪走 ParticleAlpha 材质。
- **把握度**：已确认。

### L4. `Hide()` 不打断进行中的 Flare

- **位置**：`VNGlowBackdrop.cs:121-126,141-147`；`VNSpeakerHighlight.cs:72-73`
- **触发步骤**：A 说话触发 0.7 秒 Flare，这期间 B 开口，A 走 `glow.Hide()`。Hide 只停 `_pulse`，Flare 序列继续运行，结束时 `OnComplete(StartPulse)` 让 A 的光环重新亮起；连续台词时 Flare 还会互相叠加。
- **后果**：旁听者光环异常亮起，持续到下一句。
- **修复方向**：`Hide()` 里加 `DOTween.Kill(this)`。
- **把握度**：已确认。

### L5. 水波结束时清掉热浪扭曲

- **位置**：`VNScreenShockwave.cs:138,140-144`；`VNImageEffectController.cs:314-319`；`VNHeatHaze.cs:46`
- **触发步骤**：热浪开启时播放 `fx shockwave`，两者 targets 都含背景。OnKill 调 `SetWave(0f)` 并把 speed/freq 写回默认值。
- **后果**：热浪扭曲静默消失，`VNHeatHaze.IsActive` 仍为 true。当前两边 targets 都为空所以不触发，修 H1 后会出现。
- **修复方向**：给波浪参数做来源通道，或由水波结束时恢复原值。
- **把握度**：已确认（代码路径），当前场景未触发。

---

## 已排查、未发现问题

- **SetLink**：48 个文件中运行时 Tween 均有 `SetLink` 或挂在带 Link 的 Sequence 内；缺 Link 的只有 `VNEffectsDemo.cs:190`，且该组件不在 Build 场景里。
- **循环 Tween/协程**：循环均有对应 Stop 或 Link；`VNShootingStars` 的递归调度可被 Hide 停止。
- **运行时资源**：材质都有 Destroy；`VNScreenTransition._directMat` 每次过渡销毁；程序化贴图是有界静态缓存；`VNToneMatch.SampleAverageColor` 已销毁临时 Texture2D。未发现随游玩时长增长的泄漏。
- **每帧分配**：`VNFoliageSystem.StepLayer` 复用 buffer，其余 Update 无明显分配。
- **`_Brightness/_Saturation` 直写**：运行时只有 `WriteGrade` 写入；`VNEffectsDemo` 直写 `_HueShift` 但不在 Build 中。
- **velocityOverLifetime**：`VNAmbientParticles` 三轴均为 TwoConstants，`VNLiquidSplash` 三轴均为 Constant，模式一致。
- **旧版 Input API**：Grep 无匹配。
- **URP 后处理剥离**：`m_StripUnusedPostProcessingVariants: 1`，但 `DefaultVolumeProfile.asset` 含 FilmGrain，运行时 Volume 的颗粒不会被剥离。Standalone 默认质量级为 PC，`m_SupportsHDR: 1`，Bloom 依赖成立。
- **shader 关键字**：自定义 shader 只有 `multi_compile_local`，不会被 `shader_feature` 式剥离。
- **DOTween 回收**：`defaultRecyclable: 0`，对过期 Tween 引用调 Kill 是安全的。
- **timeScale**：演出走 `DOTween.timeScale`，菜单走 `Time.timeScale=0`，范围内未发现会出错的混用。

---

## 实际读完的文件

**完整读完（45 个 .cs）**：VNAmbientParticles、VNBackgroundScroll、VNCamera、VNCameraFade、VNCharacterEmotes、VNClickRipple、VNCloudShadows、VNDriftingClouds、VNDutchAngle、VNEdgeGlow、VNEffectsDemo、VNEmoteCatalog、VNEntranceAnimator、VNFakeDoF、VNFoliageSystem、VNFoliageTextures、VNFootShadow、VNGlowBackdrop、VNGodRays、VNGrade、VNHeartbeat、VNHeatHaze、VNImageEffectController、VNKenBurns、VNLetterbox、VNLiquidPreset、VNLiquidSplash、VNMoodGrading、VNMouseStardust、VNParallax、VNProceduralTextures、VNRetroFilter、VNSakuraBurst、VNScreenShake、VNScreenShockwave、VNScreenTransition、VNShatterGraphic、VNShootingStars、VNSpeakerHighlight、VNSpeedLines、VNToneMatch、VNVignetteFocus、VNWeatherController、VNWeatherDef、VNWetScreen。

**完整读完（9 个 .shader）**：VNAdditive、VNDirectBackgroundTransition、VNFogWipe、VNImageEffect、VNParticleAlpha、VNRetroFilter、VNScreenTransition、VNShockwave、VNTutorialMask。

**只做 Grep 略读（3 个，按任务说明由别组负责）**：VNDialogueBox、VNChoicePanel、VNTypewriterText（只查了 Shader.Find、new Material/Texture2D、循环 Tween）。

**为核实而读的范围外内容**：`Script/VNStage.cs`、`VNStage.Characters.cs`、`VNStage.Scene.cs` 的相关片段，`VNScriptRunner.Dispatch.cs` 与 `.Input.cs` 片段，`VNTutorialMask.cs`、`VNFogWipeModule.cs` 片段，`VNScriptDemo.unity`、`GraphicsSettings.asset`、`EditorBuildSettings.asset`、`ProjectSettings.asset`、`QualitySettings.asset`、URP 设置资产，以及工程内 `DOTween.dll` 的 `TweenExtensions.Kill`（内存中读 IL，未落盘）。

**未读完的部分**：无（范围内 45 个 .cs 与 9 个 .shader 全部完整读完，3 个略读文件如上）。所有「待验证」项都需要一次真实 Build 或 Play Mode 确认；本次未运行 Unity。

---

# 缺陷审计 第 4 组：羽毛球 / 大头贴 / 秘密偷拍 / 相册存储

审计方式：只读静态审计，未运行游戏，未调用 Unity MCP。

## 严重度计数

| 致命 | 高 | 中 | 低 |
|---|---|---|---|
| 0 | 0 | 9 | 8 |

## 先读：两条影响阅读本报告的说明

**1. 审计期间工作区被并发重构。** 当前分支 `feature/code-slimming`，13:34~13:40 之间有别的进程在改文件：
`VNPhotoBoothModule.cs` 由 1852 行拆成 4 个 partial 文件，新增 `VNAlbumStore.cs`，等等。这不是本审计造成的。

- 拆分已比对：`VNPhotoBoothModule*.cs` 与 HEAD 版本按行集合比较，差异只有 `VNPhotoBoothUi.Stretch` 改成 `VNUi.Stretch`，以及 `partial` 声明，属于纯搬移。
- 本报告的行号以 **2026-11-01 13:49** 的工作区为准；核对时范围内文件在 13:41 之后没有再变动。如果重构继续，行号可能漂移，请按方法名定位。
- `VNAlbumStore.cs`、`VNPhotoBoothModule.{Ui,Flow,Dress}.cs` 目前是未跟踪文件，尚未提交。

**2. 触达范围。** 全部 `.vn.txt` 里 `event badminton` / `event photo` 只出现在 `BadmintonDemo`、`InteractionDemo`、`PhotoDemo` 三个演示剧本。
当前主线不会碰到羽毛球和大头贴，除非试玩版把这些演示接进去。偷拍在主线上（`第1章.vn.txt:27-28`），这组问题是试玩版一定会碰到的。

---

## 中（9 条）

### M1. 结算动画播放中点「重拍」，取景框消失，下一张拍成黑照片并存进相册

- **位置**：`VNPhotoBoothModule.Flow.cs:201-202`（先设 Result 再启动协程）、`:247`（延时后隐藏取景框）、`:443-446`（按钮一开始就可点）、`:455-473`（`Retake` 未停协程）
- **触发步骤**：
  1. 快门结束，结算层出现，「重拍」按钮立刻可点。
  2. 在相纸飞入的 0.65 秒内点「重拍」。`Retake` 把取景框设为可见，回到 Dressing。
  3. 但 `ShowResult` 协程没有被停，0.65 秒到后执行 `_viewFinder.gameObject.SetActive(false)`。
- **后果**：
  - 装扮阶段取景框整块不见，人物、贴纸、涂鸦都看不到也摸不到。
  - 此时再按快门，`Capture` 对隐藏节点仍能算出屏幕矩形，拍到的是机身暗底，并存进相册；再重拍一次才恢复。
  - 评分模式下，只要结算没播完就点重拍，协程继续跑：命中项音效照响，`SpawnHitRow` 往已销毁的 `hitList` 下建节点（可能成为场景根的孤儿物体），最后 `barFill.sizeDelta` 抛 `MissingReferenceException` 才终止。
- **修复方向**：保存 `ShowResult` 的 Coroutine 句柄并在 `Retake` 里 `StopCoroutine`；或用代数令牌，每次 yield 后校验 `_phase == Result` 且令牌未变。
- **把握度**：已确认（隐藏取景框这条路径）；孤儿节点的具体表现待验证。

### M2. 偷拍被发现的「扣好感」写到一个没人读的 flag，惩罚实际无效

- **位置**：`VNSecretPhotoDef.cs:70`；`Assets/VNEffects/SecretPhoto/秘密相机.asset`（`affectionStat: 好感`）；`VNSecretPhotoMode.cs:412-414`；`VNStatsHud.cs:137-150`
- **触发步骤**：第 1 章解锁相机，对着角色直到察觉满 100。
- **后果**：
  - 主线好感度是逐角色的 `好感度_<角色>`（如 `第1章.vn.txt:132` 的 `好感度_结衣`）。
  - `VNGameConfig.stats` 只登记了 8 个属性（体力/压力/善恶/感性/智力/行动力/金钱/魅力），没有 `好感`，因此 `Apply` 走无定义分支：不钳制，持续写成负数。
  - 玩家看到「好感 -3」的提示，但真正的角色好感不变。Grep 确认主线剧本的 if 分支都不读裸的 `好感`，只有 `PhotoDemo` 和 `AiTalkDemo` 用到这个名字。
- **修复方向**：改成按角色拼接，例如 `affectionStatPrefix = "好感度_"` 再加被拍角色 id；同时确认 `VNCharacterDef.id` 与 flag 后缀是同一套（`星野结衣` 与 `结衣` 目前看起来不一致，需要确认映射）。
- **把握度**：已确认。

### M3. 点过相机图标后按 Enter 推进，可能误开偷拍，且剧本会在相机模式下继续跑

- **位置**：`VNSecretPhotoUi.cs:148-154`（按钮未设 `Navigation.None`）；`VNSecretPhotoMode.cs:140-155`；`VNScriptRunner.Input.cs:378-390`；`VNScriptRunner.Commands.cs:46-55`
- **触发步骤**：
  1. 鼠标点相机图标，uGUI 把它设为 `currentSelectedGameObject`。
  2. 只用键盘操作（滚轮/空格拍照/ESC 退出），中途没有再按鼠标。场景的 `m_DeselectOnBackgroundClick: 1` 只在按鼠标时才清选中。
  3. 回到台词后按 Enter 推进。Enter 同时是 uGUI Submit，触发仍被选中的图标按钮，调用 `TryOpen`。
- **后果**，取决于 `EventSystem.Update` 与 Runner `Update` 的先后：
  - EventSystem 先执行：相机被再次打开，台词没推进。
  - Runner 先执行：`_advance` 已置 true，同帧 `_waitingAtSay` 仍为 true，`Open` 成功；随后 SayCo 恢复并往下走，**剧本在相机模式下继续执行**。对话框被隐藏，后续 `bg`、`camseq`、`event` 都与手动镜头并发。
- 另一条更容易踩到的变体：台词打字中点图标（只弹「现在不能」），之后一路用 Enter 推进。
- **修复方向**：图标按钮设 `Navigation.Mode.None`，并在 `TryOpen` 后 `EventSystem.current.SetSelectedGameObject(null)`；`Open` 里再校验 `!_advance` 兜底。
- **把握度**：待验证。代码路径已走通，Submit 绑定与执行顺序需要运行确认。

### M4. 连按快门时，闪白层被拍进下一张照片，胶卷照扣

- **位置**：`VNSecretPhotoMode.cs:467-474`（hide 列表无闪白层）、`:474`（`FlashCo` 启动）、`:498`（`_shooting=false`）；`VNSecretPhotoUi.cs:292`（`_flash` 挂在 `_root` 上，不在 HUD 内）、`:405`
- **触发步骤**：按空格或点快门，在 0.32 秒闪白淡出前再按一次。
- **后果**：第一张存完 `_shooting` 就复位，第二次抓屏时 `_flash` 还亮着，且不在隐藏列表里。第二张带白纱，永久进私密相册，并扣一卷胶卷。
  大头贴模块在同样的位置有专门的注释说明必须把 `_flash` 进隐藏列表（`VNPhotoBoothModule.Flow.cs:176`），偷拍这里漏了。
- **修复方向**：`hide` 里加入 `_flash`；或让 `_shooting` 等 `FlashCo` 结束再复位。
- **把握度**：路径已确认；白纱实际浓度待验证（PNG 编码卡顿会让闪白提前走完，窗口可能短于 0.32 秒）。

### M5. 切换语言后，偷拍教程的三个高亮锚点全部失效，UI 根节点还会泄漏

- **位置**：`VNSecretPhotoMode.cs:95-109`；`VNSecretPhotoUi.cs:78-81`、`:97-100`、`:106-111`；`VNTutorialAnchors.cs:40-44`；`VNTutorialPlayer.cs:402-414`
- **触发步骤**：在标题或设置面板切换一次语言，之后首次进入偷拍模式。
- **后果**：
  1. `Destroy(_ui)` 是帧末执行，而新 UI 的 `Build` 立刻注册锚点。帧末旧组件的 `OnDestroy` 按 id 无条件 `Unregister`，把新注册的 `secretphoto.alert/shutter/film` 删掉。教程资产的 3 步都依赖这些锚点，结果只有整屏压暗、没有高亮洞。
  2. `Destroy(_ui)` 只销毁组件，`Build` 创建的 `VNSecretPhotoUi` 根物体留在 Canvas 下，每切一次语言泄漏一套嵌套 Canvas。切换时图标是隐藏的，所以旧 UI 看不见。
- **修复方向**：重建时同时销毁 `_root`；`Unregister` 传入 rect 参数做比对；或改成就地刷新文案，不重建。
- **把握度**：已确认。

### M6. 非 16:9 屏幕上羽毛球场盖不满画面，超宽屏时比分看不见

- **位置**：`VNBadmintonCourt.cs:33`、`:70-74`、`:113-114`、`:165-166`；`Editor/VNBadmintonInstaller.cs:68`（模板只挂模块，没有全屏底图）；场景 CanvasScaler `Match = 0.5`
- **触发步骤**：`ProjectSettings` 为 `fullscreenMode: 1`、`defaultIsNativeResolution: 1`、`resizableWindow: 0`，且 Grep 未发现任何 letterbox / Camera.rect 处理，所以玩家的显示器比例直接决定画布比例。
- **后果**：球场按固定 ±960 × 0~1080 绘制，锚在底边中央，且模块没有全屏背板。

| 屏幕 | 画布尺寸 | 现象 |
|---|---|---|
| 16:10（1920×1200） | 约 1821×1138 | 顶部 58px 露出后面的舞台 |
| 3:2（1920×1280） | 约 1764×1176 | 顶部 96px 露出舞台 |
| 21:9（2560×1080） | 约 2216×935 | 左右各露 148px；记分板中心 Y=930 在画布上沿附近，名字和比分基本出屏；球的最高点（≤1000）飞出画面 |

  大头贴因为有全屏 `Backdrop` 不露底。但 `MachineW/H` 是 1860×1020（`VNPhotoBoothModule.cs:102-103`）：4:3 时左右栏被裁约 69px，21:9 时快门和结算按钮下缘被裁约 20px，仍可点击。
  截图坐标换算（`VNPhotoCapture.ScreenRectOf`）按世界坐标走，任何比例都正确，不受影响。
- **修复方向**：模块铺一张全屏底色；或全局在相机层做 16:9 letterbox（这样各小游戏都受益）。
- **把握度**：布局计算已确认；实际观感待验证。

### M7. 大头贴结算明细写死中文，英文/日文下显示中文和原始 id

- **位置**：`VNPhotoScore.cs:65`（`基础分`）、`:74`、`:77`、`:85`、`:94`、`:127-128`；显示处 `VNPhotoBoothModule.Flow.cs:327-328`
- **触发步骤**：语言切到 English 或日本語，用 `theme:` 评分模式拍一张。
- **后果**：结算行显示「基础分」「我的「害羞」」「贴纸「爱心」×3」。中文包装之外，id 也是内部 id，没走各资产的 `DisplayName`。违反项目「玩家可见字符串一律 `VNLocale.T`」的约定。
- **修复方向**：`Hit` 只存类型和资产引用，标签在 UI 层用 `VNLocale.T` 与 `DisplayName` 拼。纯逻辑层保持可单测。
- **把握度**：已确认。

### M8. `KwF` 依赖系统区域设置，德语等系统上 `rate:0.1` 会变成 1

- **位置**：`VNEventModule.cs:24-29`（`float.TryParse(v, out f)`，未传 `InvariantCulture`）；受影响调用 `VNPhotoBoothModule.cs:268-274`
- **触发步骤**：玩家系统区域是 de-DE 等以 `.` 作千分位的语言环境，剧本 `event photo ... rate:0.1`（`PhotoDemo.vn.txt:31`）。
- **后果**：`"0.1"` 被解析成 1，奖励放大 10 倍。fr-FR 等环境解析失败，静默回退默认值。`KwF` 是所有事件模块共用的，影响面不止大头贴。
  Grep 确认项目没有设置 `CurrentCulture` / `DefaultThreadCurrentCulture`。itch 公开试玩面向全球，这一点比本地测试更容易暴露。
- **修复方向**：`KwF` 改用 `NumberStyles.Float, CultureInfo.InvariantCulture`；最省事的兜底是启动时设置 `CultureInfo.DefaultThreadCurrentCulture = InvariantCulture`（需评估对其他格式化的影响）。
- **把握度**：待验证（代码已确认，需在对应区域设置下实测）。

### M9. 大头贴相册满或写盘失败时不提示，玩家以为存好了

- **位置**：`VNPhotoBoothModule.Flow.cs:189-199`（`Add` 返回 null 后只赋给 `_shotEntry`）；`VNAlbumStore.cs:130`（注释要求调用方提示）、`:137-155`
- **触发步骤**：相册已有 200 张，或磁盘满/目录无写权限时拍照。
- **后果**：结算页照常展示照片，点「完成」离开。照片没有落盘，只留一条控制台日志，属于静默丢数据。Grep 确认 `_shotEntry` 只在 `Retake` 里用过，没有任何提示。偷拍模式在同样的场景下有完整的提示与退胶卷处理。
- **修复方向**：`Add` 返回 null 时 `VNToast` 提示，并在结算页标注「未保存」。
- **把握度**：已确认。

---

## 低（8 条）

### L1. 写盘失败留下半截 PNG，下次启动变成坏相册条目

- **位置**：`VNAlbumStore.cs:146-155`、`:94-102`；`VNCgGallery.cs:337`
- **触发**：磁盘写满时 `File.WriteAllBytes` 中途抛异常，catch 没删残留文件。下次启动对账时被补进索引。
- **后果**：画廊出现白格子（sprite 为 null、color 为白），占用 200 张容量；点开也是空白，但可以删除。
- 同类小问题：偷拍在写盘失败时提示的是「相册已满」（`VNSecretPhotoMode.cs:486-490`），文案不符。
- **修复方向**：先写 `.tmp` 再 `File.Move`，失败时清理；`Add` 区分「已满」与「写失败」。
- **把握度**：待验证（需模拟磁盘满）。

### L2. 打开照片页时同步解码全部原图，相册大时卡顿并有内存峰值

- **位置**：`VNCgGallery.cs:323`、`:337`、`:118-119`；`VNAlbumStore.cs:237-282`
- **触发**：相册存了很多张，打开或切到照片页。
- **后果**：同一帧内逐张读 PNG、全尺寸解码、逐像素缩放。`Object.Destroy(full)` 要帧末才释放，200 张偷拍（1600×900）当帧峰值约 1.1GB。关闭画廊时 `ClearCache` 清掉缩略图，下次打开重新来一遍。文件头注释的「22MB」只是稳态值。
- **修复方向**：分帧加载（协程每帧几张）、用 `DestroyImmediate` 释放中间纹理、保留缩略图缓存或落盘。
- **把握度**：已确认（代码路径）；实际耗时待验证。试玩版照片量小，影响有限。

### L3. 看大图每翻一页泄漏一个 Sprite

- **位置**：`VNAlbumStore.cs:285-294`；`VNCgGallery.cs:360`
- **触发**：在全屏查看器里翻照片。
- **后果**：`LoadSprite` 每次新建 Sprite，Grep 确认 `VNCgGallery` 没有销毁这些 Sprite，`ClearCache` 也只销毁纹理。对象很小，长时间翻看才有累积。
- **修复方向**：缓存 Sprite，或在换图/关闭时销毁上一个。
- **把握度**：已确认。

### L4. 代码合成的 AudioClip 每次进小游戏都泄漏

- **位置**：`VNBadmintonSfx.cs:35-39`、`:86`；`VNPhotoSfx.cs:36-40`、`:82`、`:113`、`:142`
- **触发**：反复进入羽毛球或大头贴。
- **后果**：每次新建 5 个 `AudioClip`，Grep 确认全项目这几个文件没有销毁。本项目单场景，不会靠换场景回收。单次仅约 100KB 级，纯累积问题。
- **修复方向**：静态缓存，或在模块 `OnDestroy` 里销毁。
- **把握度**：已确认。

### L5. 羽毛球得分时比分弹跳动画永远不播

- **位置**：`VNBadmintonModule.cs:925-926`；`VNBadmintonCourt.cs:369-371`、`:378`
- **触发**：任意得分。
- **后果**：`SetScore(..., punch:true)` 刚起 `DOPunchScale`，下一行 `ShowScoreBoard(true)` 的 `_scoreBoard.DOKill()` 就把它杀掉。纯视觉。
- **修复方向**：交换调用顺序，或用 `DOKill` 之外的按 id 精确杀。
- **把握度**：已确认。

### L6. 主题 perfectLine ≤ 0 且总分为 0 时，结算条写入 NaN

- **位置**：`VNPhotoBoothModule.Flow.cs:254`、`:272`、`:279-280`
- **触发**：主题资产把 `perfectLine` 配成 0 或负数，且 `baseScore` 为 0、没有命中。
- **后果**：`0f / 0` 得 NaN，`Clamp01` 不过滤，`sizeDelta` 收到 NaN，UI 布局报错。只在资产配错时发生。
- **修复方向**：`max = Mathf.Max(1, ...)`。
- **把握度**：已确认（仅配置错误时可达，未逐一核对现有主题资产的数值）。

### L7. 察觉条退出重进就清零，「只升不降」可被绕过

- **位置**：`VNSecretPhotoMode.cs:170`（`Open` 里 `_alert = 0f`）；对照文件头 `:21` 与 CLAUDE.md 的「察觉条只升不降」
- **触发**：察觉到 99% 时按 ESC 退出，立刻重新进入。
- **后果**：察觉归零，只要胶卷够，配合重进就能一直拍而不被发现，惩罚机制形同虚设。
- **修复方向**：若「一次进入内只升不降」是有意设计，请在文档写明；否则把察觉按角色或按台词行保存，并加退出后的缓慢回落。
- **把握度**：已确认（是否算缺陷取决于设计意图，需要你判断）。

### L8. 边框 `windowColor` 的 A 设为 0 时，人物可能整个消失

- **位置**：`VNPhotoFrameDef.cs:89-90`（Header 写着「A=0 则不画」）；`VNPhotoBoothModule.Ui.cs` 的 `BuildViewFinder`（开窗 `Mask` 且 `showMaskGraphic = true`）；`VNPhotoBoothModule.Dress.cs:42`
- **触发**：按 Header 的说明把开窗底色 alpha 配成 0。
- **后果**：`Mask` 的模板写入受图形 alpha 影响，alpha 为 0 时可能不写模板，子节点（人物、背景、人后贴纸）全部被裁掉。
- 现有 7 个边框资产的 alpha 都是 1，当前不会触发。
- **修复方向**：用 `showMaskGraphic = false` 的独立遮罩，开窗底色另起一个 Image。
- **把握度**：待验证（推断自 Unity Mask 行为，未实测）。

---

## 已核查、未发现问题的项

重点清单里这些项已沿代码走过，没有可报的缺陷：

- **VNPause / VNTime**：羽毛球、大头贴、偷拍的 `Update` 都在读输入前早退；Grep 确认三组文件里没有直接使用 `Time.deltaTime` / `unscaledDeltaTime`，也没有旧版 `Input.` API。
- **退出路径**：大头贴不碰光标与舞台，`OnDestroy` 释放照片和涂鸦资源；羽毛球 `CancelForDebug` 与 `OnDestroy` 正常。偷拍的镜头、Ken Burns、光标、UI 隐藏位都收口在 `Restore`，`VNScriptRunner.Flow.cs:431` 的 Stop 路径有 `ForceClose`。
- **帧尾截图时机**：`Capture` 关 UI 后等一帧再 `WaitForEndOfFrame`，时序正确；`finally` 保证 `restore` 和 `full` 释放。
- **涂鸦 / 截图纹理**：`ReleaseShot` 与 `VNPhotoDoodle.Destroy` 覆盖了结算纹理、两张画布和加法材质。程序化贴图是全局缓存，不泄漏。
- **相册一致性**：index 丢失或损坏时从 PNG 补回（元数据丢失，属设计内）；文件名毫秒级且单线程，无实际碰撞；`SanitizeFileName` 覆盖非法字符。
- **羽毛球数学**：球路必定 `a < 0` 且速度非零，不会卡死；子步进上限足够；`BuildArc` 失败时球继续飞并最终落地；结果名与文档一致（胜利/失败/结束）。大头贴结果名也一致（完美/普通/失败/完成）。
- **偷拍进入时机**：`CanOpenSecretPhoto` 要求停在台词上、无面板、无事件、未暂停；打开时关 Skip/Auto；Runner `Update` 拦截 F5/F9/Q/L 与推进。`SayOutOfScript` 不破坏 `_waitingAtSay`。
- **快捷条 / 属性条自动隐藏**（最新提交新增）：`SetSuppressed` 与 `activeInHierarchy` 联动正确，不会在偷拍中或截图时冒出来。
- **本地化键**：三组用到的全部 key 在 zh/en/ja 三张表里都存在。
- **`VN/Additive` 打包**：不在 Always Included，但场景引用了使用它的材质，`Shader.Find` 在构建版可用。

## 备注（非缺陷）

- `第1章.vn.txt:27-28` 直接写了 `flag 秘密相机 1` 与 `flag 道具_胶卷 +10`，最近提交说明是为了测试。公开试玩前请确认这是否保留。

---

## 文件覆盖情况

**完整读完：**

- 羽毛球：`VNBadmintonActor.cs`、`VNBadmintonBallistics.cs`、`VNBadmintonCourt.cs`、`VNBadmintonDef.cs`、`VNBadmintonModule.cs`、`VNBadmintonSfx.cs`、`VNBadmintonUi.cs`
- 大头贴：`VNPhotoAlbum.cs`、`VNPhotoBackdropDef.cs`、`VNPhotoBoothModule.cs`、`VNPhotoBoothModule.Dress.cs`、`VNPhotoBoothModule.Flow.cs`、`VNPhotoBoothModule.Ui.cs`、`VNPhotoBoothUi.cs`、`VNPhotoCapture.cs`、`VNPhotoDoodle.cs`、`VNPhotoFrameDef.cs`、`VNPhotoScore.cs`、`VNPhotoSfx.cs`、`VNPhotoStickerDef.cs`、`VNPhotoTextures.cs`、`VNPhotoThemeDef.cs`
- 偷拍：`VNSecretAlbum.cs`、`VNSecretPhotoDef.cs`、`VNSecretPhotoMode.cs`、`VNSecretPhotoRig.cs`、`VNSecretPhotoUi.cs`
- 存储：`VNAlbumStore.cs`
- 规范：`.claude/skills/vn-new-event-module/SKILL.md`、`VNEventModule.cs`、`VNPause.cs`

**读取方式的特殊情况：** `VNPhotoBoothModule.Ui.cs` 第 1~594 行读的是拆分前单文件的对应内容，第 595~740 行读的是拆分后版本；已用行集合比对确认拆分是纯搬移。`.Flow`、`.Dress` 和主文件都读的是拆分后版本。

**部分读取（用于核实路径，不在本组范围）：** `VNScriptRunner.Input.cs`（全文）、`VNScriptRunner.Commands.cs:20-84`、`VNScriptRunner.SaveLoad.cs:235-271`、`VNCamera.cs:480-589`、`VNCgGallery.cs:85-504`、`VNQuickToolbar.cs:135-210`、`VNEdgeAutoHide.cs:55-304`、`VNStatsHud.cs:111-170`、`VNTutorialPlayer.cs:395-425`，以及 ProjectSettings、场景与资产文件的相关字段。

**未读完：** 范围内无未读文件。`VNTouchCursor.cs` 未使用（本组模块不调用它），未审计。

---

# 审计第 5 组：亲密互动 / 擦雾 / 新手引导 / 过场 / 暂停

只读审计，以当前工作区代码为准。路径未写全的都在 `Assets/Project/Scripts/VNEffects/Script/` 下。

## 计数

| 严重度 | 数量 |
|---|---|
| 致命 | 0 |
| 高 | 3 |
| 中 | 7 |
| 低 | 6 |

## 先看可达性（决定优先级）

`VNGameConfig` 的入口是 `第1章`，`chapters` 登记了 8 份剧本：主循环、公共片段、好感事件_星野结衣、第1~3章、结局集、节日活动集。

- `event interact` 只出现在 `InteractionDemo.vn.txt`，`event wipefog` 只出现在 `FogWipeDemo.vn.txt`，这两个文件都没登记。**按当前入口，玩家走不到这两个玩法。**
- `interlude` 只出现在 `Hina/第1章.vn.txt` 和 `InteractionDemo.vn.txt`，同样没登记。
- `tutorial` 在主线第 1 章第 40 行：`tutorial test01 force:on`。**新手引导在主线开场就会播。**

下面每条都标了「当前试玩路径」。H1 的教程部分、L3 现在就能遇到，H3 对其他事件是否成立属于别组范围。其余条目要等玩法接入试玩包才会生效。

---

## 高

### H1　`VN/TutorialMask` 与 `VN/FogWipe` 两个 shader 不会进 Windows 包

- **位置**：`VNTutorialMask.cs:79-87`、`VNFogWipeModule.cs:625-638`、`VNTutorialPlayer.cs:53-56`、`ProjectSettings/GraphicsSettings.asset:29`
- **依据**：
  - 这两个 shader 只靠 `Shader.Find` 引用。
  - 按 guid 全量搜 `.mat/.unity/.prefab/.asset`，引用数都是 0。
  - `m_AlwaysIncludedShaders` 里只有 7 个内置项；shader 也不在 Resources 下。
  - 没有 `.shadervariants`，也没有 IPreprocessBuild 钩子。
  - 场景里没有 `VNTutorialPlayer` 序列化对象（`maskMaterial` 零匹配，播放器由 VNStage 运行时自建），`maskMaterial` 恒为空。
  - 同样走 `Shader.Find` 的 `VN/Additive` 有材质被场景引用，不受影响。
- **触发步骤**：
  1. 打 Windows 包。
  2. 新游戏，走到第 1 章第 40 行 `tutorial test01 force:on`。
- **后果**：
  - `Shader.Find` 返回 null，暗幕退化为整屏压暗，`test01` 第 2 步的 `area` 高亮洞挖不出来，玩家不知道在讲哪里。
  - `VN/FogWipe` 同理：`_fogMat` 为 null，雾层退化成一张清晰原图。**整屏没有雾，CG 谜底直接暴露，玩法失效**（当前试玩路径不可达）。
- **修复方向**：建两份材质放进 Resources，或由配置资产引用（`VNTutorialPlayer.maskMaterial` 已预留，FogWipe 需新增同类字段）；或把两个 shader 加入 Always Included Shaders。打包后实测一次。
- **把握度**：已确认（引用关系已查全，文件注释也写了这个风险）。包内表现建议打包复核一次。

### H2　反馈里的 `excite` 跨过阈值时静默升阶：阶段演出被跳过，自动结束失效

- **位置**：`VNInteractionModule.cs:312`、`VNInteractionModule.cs:241-254`、`VNTouchScore.cs:149-154`
- **依据**：`AddExcite` 会改 `Stage` 但不返回结果。全文件只有 `AddUnits` 的 `tick.stageUp` 一处驱动 idle 表情、`enterFeedback`、`autoEndOnTarget`。`初次抚摸.asset` 的部位反馈确实配了 `excite: 4/5/8/8/6`。
- **触发步骤**：
  1. 进互动，兴奋度到 147。
  2. 摸耳朵触发反馈，`excite +4`，兴奋度 151。
  3. `AddExcite` 把 `Stage` 从 2 直接推到 3，`tick.stageUp` 为 false。
- **后果**：
  - 该阶段的表情、大台词、喷水和震屏全部不演。
  - 目标阶段已到，但 `Finish` 不再触发，因为之后 `AddUnits` 算出的 `next` 不大于 `Stage`，永远没有 `stageUp`。
  - 玩家只能手动点「结束」。若 `allowManualEnd=0` 且不限时，就是死局。
  - 30/80 两档也会这样静默跳过。
- **修复方向**：让 `AddExcite` 返回 Tick，模块用同一段「升阶处理」。注意 `PlayFeedback` 内部递归和阻塞状态。
- **把握度**：已确认。当前试玩路径：不可达。

### H3　事件期间快捷工具栏仍可点；互动和擦雾没有射线遮挡

- **位置**：`VNDialogueBox.cs:612-622`（HideBox 不动工具栏）、`VNQuickToolbar.cs:114-136`、`VNScriptRunner.SaveLoad.cs:95-99/174-180/182-186/226-231`、`VNScriptRunner.Input.cs:163-179/201`、`VNFogWipeModule.cs:53-55/615/623`
- **依据**：
  - 工具栏在主 Canvas 排序约 41，`VNEdgeAutoHide` 只在 `SetVisible(false)` 时压制，事件流程不调用它。
  - 默认皮肤 `QuickToolbar_New.prefab` 13 个按钮全部存在。
  - `Load / QuickLoad / Auto / Skip / Backlog / Config / HideUi` 都没有 `_eventActive` 守卫。`Save` 只看 `_waitingAtSay`。
  - 两个模块自绘内容几乎都是 `raycastTarget=false`，且 EventSystem 按钮不经过 `Runner.Update`。
- **触发步骤与后果**：
  - **擦雾**：玩家在右下角附近擦雾，工具栏展开（感应区 240×100），但被排序 60 的雾盖住。在同一个隐形按钮上按下并松开，就会触发隐形按钮：
    - QuickLoad：无确认地放弃事件并读档。
    - Skip：快进状态带出事件，`SetSkip(false)` 只在事件开始时调用。
    - HideUi / Config：界面被隐藏，或弹出设置面板。
  - **互动**：工具栏可见，直接可点，后果同上。
  - **面板打开后**：
    - `Runner.Update` 第 201 行直接 return，ESC/H 都关不掉面板，只能靠面板自己的按钮。
    - 面板排序 600~950 压住排序 60 的道具光标，而系统光标是隐藏的，**玩家在面板上看不到指针**。
    - 模块用 `VNTime` 计时，不受 `Time.timeScale=0` 影响。擦雾的倒计时和回雾照跑，鼠标点击还会穿到模块的轮询输入里。
  - **存档**：互动的阻塞台词期间 `_waitingAtSay=true`，工具栏 Save 能存下事件中途的舞台状态（临时表情等）。
- **修复方向**：EventCo 进入时压制工具栏，正常结束和 `CleanupActiveEvent` 两条路径都恢复；或在 `Execute()` 统一判 `_eventActive`。别忘了 `VNEdgeAutoHide` 的 `_group`。
- **把握度**：调用链已确认；隐形按钮的实际点击手感待验证。
- **其他模块**：没有全屏射线遮挡的模块都有同样问题，请别组对照。

---

## 中

### M1　阻塞反馈进行中结束模块，留下孤儿 SayCo：吞一次点击、该句不能存档、台词互相覆盖

- **位置**：`VNInteractionModule.cs:152/231/250-253/361`、`VNScriptRunner.cs:346`、`VNScriptRunner.Commands.cs:42-56`
- **依据**：`FeedbackCo` 挂在模块上，但 `RunInlineCo` 内部的 `StartCoroutine(co)` 挂在 Runner 上。模块销毁只杀外层，`NormalSayCo` 继续活着。`Finish` 后 Update 也不再转发推进。
- **触发步骤**（当前资产就满足）：`初次抚摸` 的 stage 3 `enterFeedback.blocking=1`，且 `targetStage=3`、`autoEndOnTarget=1`。摸到 150，同一 tick 里 `PlayFeedback` 开阻塞台词，紧接着 `Finish`。限时到期（第 152 行）、拒绝超限（第 231 行）、点结束钮也会走到这里。
- **后果**：
  - 事件后第一句主线台词出现时有两个 SayCo 同时等 `_advance`。第一下点击被孤儿吃掉，并把 `_waitingAtSay` 清成 false，主线协程还在等。于是**这一下点击无效，这句台词上 F5 和 Q 都提示不能存档**。
  - 「……我已经，不行了。」和 `endSatisfied` 的台词同帧竞争，只剩一句看得到。
- **修复方向**：`Finish` 时先通知 Runner 取消内嵌台词（RunInlineCo 记录子协程句柄并 StopCoroutine），或在模块销毁前补一次推进。目标阶段的 enterFeedback 要么强制非阻塞，要么等它播完再 Finish。
- **把握度**：已确认。当前试玩路径：不可达。

### M2　擦雾 ESC 确认框盖在道具光标上面，系统光标又是隐藏的

- **位置**：`VNFogWipeModule.cs:806-809`（懒创建 Confirm）、`VNFogWipeModule.cs:655-658`
- **依据**：Confirm 在第一次按 ESC 时才创建，是根节点最后一个子物体，渲染在 Cursor 之上。Grep 确认两个模块都没有 SetAsLastSibling，也没有改 Cursor.visible。
- **触发步骤**：擦雾中按 ESC，把鼠标移到确认框（700×260，不透明度 0.97）上。
- **后果**：光标被面板完全遮住，玩家看不见指针，只能盲点「放弃 / 继续」。
- **修复方向**：Confirm 建完后把 Cursor `SetAsLastSibling`，或确认框打开期间显示系统光标、隐藏道具图。
- **把握度**：已确认。当前试玩路径：不可达。

### M3　`_blocked` 被无关协程提前清零，后续阻塞台词可能永久卡住

- **位置**：`VNInteractionModule.cs:322-326/346/366`、`VNScriptRunner.Input.cs:417`
- **依据**：第 366 行 `_blocked = false` 不区分协程是否阻塞型。`RequestAdvance` 只在 `_waitingAtSay` 为 true 时才设 `_advance`。
- **触发步骤**：
  1. 非阻塞但带耗时 scriptLines 的反馈 A 在跑（如 `camseq` 或 `wait`）。
  2. 阻塞反馈 B 开始，`_blocked=true`。
  3. A 结束，`_blocked=false`，点击不再转发，B 悬着。
  4. 玩家继续摸，触发阻塞反馈 C，`_blocked=true`。
  5. 一次点击让 B 退出，同时把 `_waitingAtSay` 清零；C 还在等，之后 `RequestAdvance` 全被忽略。
- **后果**：`_blocked` 永远为 true，抚摸判定永久停止。有结束钮或限时还能退出；两者都没有就彻底卡死。
- **修复方向**：改用计数或记录阻塞协程身份，只有阻塞型协程才能清除。
- **把握度**：代码路径已确认。当前资产里的 scriptLines 都是瞬发命令，暂不触发。当前试玩路径：不可达。

### M4　互动没有任何兜底退出：部位表缺失时可能死局

- **位置**：`VNInteractionModule.cs:120-123/152/792`、`VNInteractionDef.cs:312-315`
- **依据**：退出只有四条：限时、自动结束、拒绝超限、结束钮。代码**没有 ESC**，类注释和文档写的「ESC 路径」不存在。缺 `VNTouchZoneDef` 只打一条警告就继续。`VNScenarioLinter.*` 和 `VNInteractionDef` 都不校验这些配置（Grep `allowManualEnd` 只命中 Def、Module、Installer）。
- **触发步骤**：`allowManualEnd=0` 且 `time` 为 0，同时缺部位表，或 `autoEndOnTarget=0`。
- **后果**：游戏卡在事件里，鼠标还是隐藏的，只能强退。
- **修复方向**：缺部位表时直接 `Done("")`；无退出条件时强制显示结束钮，或加 ESC 确认；Lint 加检查。
- **把握度**：已确认（`初次抚摸` 本身 `allowManualEnd=1`，不触发）。当前试玩路径：不可达。

### M5　互动没有可用道具或图标时，鼠标指针完全消失

- **位置**：`VNTouchCursor.cs:71/79-80/116`、`VNInteractionModule.cs:727-733/799`
- **依据**：`_item=null` 或 `icon=null` 时 `_iconImage.enabled=false`，但 `HideSystemCursor` 已经执行。模块对 `_item == null` 有分支（第 236-237 行），说明这是被当作合法状态的。
- **触发步骤**：`VNInteractionDef.items` 为空，或道具没配图标。
- **后果**：整场既没有系统光标也没有道具图，玩家盲操作。
- **修复方向**：`SetItem` 无图时退回系统光标，或给兜底图。
- **把握度**：已确认。当前试玩路径：不可达。

### M6　`show` 不写 `expr:` 时，互动结束不还原表情

- **位置**：`VNInteractionModule.cs:119/613-615`、`VNStage.Characters.cs:87/254`、`VNCharacterDef.cs:246-247`
- **依据**：不写表情时 `c.expression=null`，`_originalExpression` 为 null，`RestoreExpression` 的 `!IsNullOrEmpty` 守卫直接跳过。而 `SetExpression(id, null)` 其实能正确回到默认表情。
- **触发步骤**：`show 角色` 不带 `expr:`，再 `event interact`，且结局反馈没指定表情（或调试中断）。
- **后果**：立绘停留在互动内最后一个表情，例如「害羞」，并被写进存档。
- **修复方向**：记一个「是否已抓取原表情」的布尔，空值也调用 SetExpression。
- **把握度**：已确认。当前试玩路径：不可达（`InteractionDemo` 写了 `expr:默认`）。

### M7　过场 loading 圈转一圈就停，文档说的转满全程不成立

- **位置**：`VNInterludeScreen.cs:227-233`
- **依据**：无限循环 tween 被 Join 进 Sequence。`DOTween.XML:2442` 明写：循环 -1 嵌入 Sequence 时转成 1。
- **触发步骤**：播任意 `interlude`。默认淡入 0.45 秒 + 停留 1.5 秒；`InteractionDemo` 的 `time:3` 更长。
- **后果**：转 0.9 秒后圈停住，弧长动画 0.55 秒后也停，后面看起来像卡死。
- **修复方向**：两条 tween 各自独立循环（不放进 Sequence），在 `StopSpinner` 里分别 Kill；现有的 `_spinner.DOKill()` 已经够用。
- **把握度**：已确认（依据 DOTween 官方 XML 注释）。当前试玩路径：不可达，接入 Hina 章节后必现。

---

## 低

### L1　互动不还原叠加层，与注释矛盾
- **位置**：`VNInteractionModule.cs:302-305`，类注释第 22-25 行。
- **依据**：全文件 `overlay` 只出现在 `PlayFeedback`，Finish、CancelForDebug、OnDestroy 都不处理。
- **触发 → 后果**：反馈配了 `overlay`，互动结束后潮红留在立绘上，并进存档。作者可以在 `cleanupLines` 里手写 `overlay x clear`，但注释承诺的是模块自动还原。
- **修复方向**：确定语义。要还原就在进场记录、出场恢复；不还原就改注释和文档。
- **把握度**：已确认。当前资产没配 overlay。

### L2　`Dispose` 之后道具光标还活着，出现双指针和实心光斑
- **位置**：`VNInteractionModule.cs:576`、`VNFogWipeModule.cs:456`、`VNTouchCursor.cs:197-203/217-229`
- **触发 → 后果**：`Finish` 时材质被销毁，但光标物体继续 Update，一直到模块销毁（互动 0.7 秒，擦雾 2.2 秒）。系统光标和道具图同时显示。悬停中（擦雾恒为悬停）`SetGlowAlpha` 走退化分支，发光图变成普通混合的不透明光斑。
- **修复方向**：Dispose 时同时隐藏道具光标根物体，或只还光标、把材质销毁留到 OnDestroy。
- **把握度**：已确认。当前试玩路径：不可达。

### L3　每次教程、事件、读档都弹「自动模式 关」
- **位置**：`VNTutorialPlayer.cs:207`、`VNScriptRunner.Commands.cs:338`、`VNScriptRunner.SaveLoad.cs:58`、`VNScriptRunner.Input.cs:163-169`
- **依据**：`SetSkip` 有 `_skip == on` 早退，`SetAuto` 没有，无条件 Toast。
- **触发 → 后果**：Auto 根本没开，主线第 1 章第 40 行教程一弹出，就同时冒出一张卡片。
- **修复方向**：`SetAuto` 加同样的早退。注意 Toast 卡片会压在教程层之上。
- **把握度**：已确认。当前试玩路径：**可达**。

### L4　擦雾每场泄漏 4 个 AudioClip
- **位置**：`VNFogSfx.cs:37/53-55/134/162`（Grep 无 Destroy）
- **后果**：每场约 80000 采样、320KB，重玩才累积。
- **修复方向**：缓存成静态，或 `Stop` 时销毁自己创建的 clip（别销毁资产里配的 clip）。
- **把握度**：已确认。

### L5　读档打断过场后，旧协程继续跑
- **位置**：`VNStage.Save.cs:252`、`VNScriptRunner.Flow.cs:425`、`VNInterludeScreen.cs:120-133`
- **触发 → 后果**：过场中按 L 读档。`HideImmediate` 收起了画面，但 `yield return StartCoroutine(co)` 的子协程不随 `StopCoroutine(_co)` 停止。它继续等待，最后再调一次 `HideImmediate`。读档位置若在约 2 秒内又播 `interlude`，新过场会被这次调用切掉。Runner 的 `Update` 在过场期间不屏蔽 L / F9。
- **修复方向**：PlayCo 加代次 token，每轮 await 后检查；或 Stop 时显式停掉子协程。
- **把握度**：`HideImmediate` 路径已确认；Unity 子协程不随父协程停止这一点待验证。

### L6　锚点找不到且 `area` 是默认值时，静默在屏幕中央挖洞
- **位置**：`VNTutorialDef.cs:37/80`、`VNTutorialPlayer.cs:407-414`
- **依据**：`area` 字段默认 (0.35,0.35,0.3,0.3)，`HasArea` 恒为 true，警告条件 `!step.HasArea` 不成立。注释说「两者都留空 = 不挖洞」，但新步骤的 area 并不为空。
- **触发 → 后果**：anchor 拼错或控件未生成时没有任何警告，屏幕中央出现一个高亮洞，指向空气。这正是 VNTutorialAnchors 想避免的症状。
- **修复方向**：默认 area 改 0；anchor 非空且未解析时总是警告。
- **把握度**：已确认。`test01` 的 area 已是显式值，不触发。

---

## 已核查，未发现问题

| 重点项 | 结论 |
|---|---|
| `VNTouchCursor.Dispose` 四处都调 | 成立。互动 Finish(576)、CancelForDebug(652)、OnDestroy(663)；擦雾 Finish(456)、RunCleanup(589，含 OnDisable)；光标自身 OnDestroy/OnDisable 兜底。 |
| 教程与光标的还原顺序 | 成立。`CleanupActiveEvent` 先 `CancelImmediate`（还原成隐藏），再 `CancelForDebug`（Dispose 显示），顺序正确，不会把光标永久藏掉。 |
| 暂停句柄释放 | 成立。Close、OnDisable、OnDestroy、`ReleaseAll` 兜底都覆盖。教程期间 Runner.Update 被 `VNPause` 挡住，HUD 无点击处理（Grep 确认），工具栏被 92 层暗幕盖住，读档打断路径不可达。 |
| 教程淡出与同帧输入 | 成立。推进帧与 Close 至少隔一帧，`wasPressedThisFrame` 已复位。 |
| 教程目标被销毁 | 成立。`TryResolve` 走伪 null，退到 area 分支，area 为空时 return false，不崩。 |
| 部位框、痕迹、视差还原 | 成立。三条退出路径都处理，`_parallax` 置空保证幂等。 |
| 互动被读档打断 | 成立。`CancelForDebug` 走在 ClearStage 之前，表情还原对象仍在；OnDestroy 里已销毁组件走伪 null。 |
| 过场 ClearStage 时 HideImmediate | 成立（`VNStage.Save.cs:252`），对未 Build 状态有判空。 |
| SKIP 期间 | 成立。interlude、tutorial 在 `_skip` 时整段跳过（不标记看过），event 与教程都 `SetSkip(false)`。 |
| 雾掩码 | 成立。`float[]`/`byte[]` 长度都是 384×216，每帧不分配，Texture2D 与材质都在 RunCleanup 释放，`SetPixelData` 与 R8 匹配。 |
| 除零 | 成立。面积、阈值、时长、尺寸都有 Max、Clamp 或早退保护。 |
| 阶段只升不降 | 成立。`VNTouchScore.StageFor` 取 max，`VNFogScore.Update` 只增。 |
| 部位坐标 | 成立。立绘 pivot 固定 (0.5,0.5)（`VNStage.Characters.cs:334`），`local/size` 正确。 |

---

## 越界线索（不计入本组计数，请 Runner 组确认）

1. `VNScriptRunner.Flow.cs:425` 的 `Stop()` 只 `StopCoroutine(_co)`，而 `Dispatch.cs:41` 用 `yield return StartCoroutine(co)` 跑每条命令。若 Unity 确实不连带停止子协程，普通读档时停在台词上的 `NormalSayCo` 也会变孤儿，症状与 M1 相同（读档后第一次点击无效、该句不能存档）。影响主线，建议优先验证。
2. `第1章.vn.txt:40` 的 `tutorial test01 force:on` 看起来是测试行，`force:on` 还绕过设置里的「显示教程提示」开关。同段的 `flag 秘密相机 1`、`flag 道具_胶卷 +10`、`quest start 球场之王` 也像测试内容，发布前请确认。

---

## 实际读完的文件

**范围内，全部逐行读完（`Assets/Project/Scripts/VNEffects/Script/`）**：
VNInteractionModule.cs、VNInteractionDef.cs、VNTouchCursor.cs、VNTouchScore.cs、VNTouchZoneDef.cs、VNFogWipeModule.cs、VNFogWipeDef.cs、VNFogMask.cs、VNFogScore.cs、VNFogSfx.cs、VNFogTextures.cs、VNTutorialPlayer.cs、VNTutorialMask.cs、VNTutorialAnchors.cs、VNTutorialSeen.cs、VNTutorialSkin.cs、VNTutorialDef.cs、VNTutorialPicker.cs、VNInterludeScreen.cs、VNInterludeDef.cs、VNPause.cs、VNEventModule.cs，以及 `.claude/skills/vn-new-event-module/SKILL.md`。

**范围外，只读了相关部分**：
- VNScriptRunner.Commands.cs（20-99、300-409）
- VNScriptRunner.cs（290-399）
- VNScriptRunner.Flow.cs（380-529）
- VNScriptRunner.Input.cs（1-430）
- VNScriptRunner.Dispatch.cs（225-274 及 Grep）
- VNScriptRunner.SaveLoad.cs（1-271）
- VNStage.Save.cs（225-284）
- VNStage.Characters.cs（235-274 及 Grep）
- VNEdgeAutoHide.cs（95-354）
- VNQuickToolbar.cs、VNEventRegistry.cs（全文）
- VNBacklog.cs（40-141）
- VNDialogueBox.cs（Grep 片段）
- 资产：`初次抚摸.asset`（14-313、440-567）、`TestTutor.asset`（Grep）、`第1章.vn.txt`（1-60）、`InteractionDemo.vn.txt`（1-40）、`VNGameConfig.asset`（局部）、`GraphicsSettings.asset`（局部）

**未读**：
- `初次抚摸.asset` 第 314-439 行（只用 Grep 查了 blocking、scriptLines、excite、overlay）
- `VNFogTuneWindow`、`VNTouchZoneEditorWindow` 等 Editor 工具
- 未打包验证，未运行 Unity

---

# 缺陷审计 · 第 6 组：任务 / 统计 / 养成 / 商店 / 背包 / 装备 / 排程 / 小游戏模块 / SNS / 日历

审计方式：只读。以当前工作区（feature/code-slimming）代码为准。路径均相对
`Assets/Project/Scripts/VNEffects/Script/`，资产与剧本写全路径。
未运行游戏；「已确认」= 已沿代码路径走通；「待验证」= 需运行确认。

## 严重度计数

| 致命 | 高 | 中 | 低 |
|---|---|---|---|
| 0 | 2 | 7 | 7 |

**试玩影响提示**：SNS、委托板、羽毛球、大头贴目前都没有进主线剧本
（`Assets/Scenarios/第*.vn.txt`、主循环、公共片段、好感事件、节日活动集里搜不到 `sns open`、
`event questboard`、未注释的 `event badminton` / `event photo`；`event battle`、`event quiz`
也只在 Demo 剧本）。主线实际走到的是 quest / stat / shop / inventory / qte / plan。
下面 SNS 相关条目因此降了一档。

---

## 高

### H1 读档后旧台词协程不停：丢一次点击，且当前句存不了档

- 位置：`VNScriptRunner.Dispatch.cs:41`（`yield return StartCoroutine(co)`）、
  `VNScriptRunner.Flow.cs:425`（`StopCoroutine(_co)` 只停外层）、
  `VNScriptRunner.Commands.cs:46-56`、`:78-80`
- 触发步骤：
  1. 停在任意台词上，此时 `NormalSayCo` 是一条**独立**协程，在 `while (!_advance)` 里等。
  2. 按 L 快读（或 F9 读档）。`LoadFrom` → `Stop()` 只 `StopCoroutine(_co)`（外层 `Run`），
     内层 SayCo 不会被级联停止（全文 `StopAllCoroutines` 零命中）。
  3. `ResumeAt` 起新的 Run 和新的 SayCo，新旧两条同时等 `_advance`。
  4. 玩家点一下：先跑到的那条退出并执行 `_waitingAtSay = false; _advance = false;`。
- 后果：
  - 另一条没看到 `_advance`，继续等。玩家要多点一次；若僵尸先走，新台词这一下点击被吞。
  - `_waitingAtSay` 已被清成 false，而台词还在等，此时按 F5/Q 提示「现在不能存档」。
  - 每读一次档多一条僵尸，读 N 次吞 N 下，一路累积到后续台词上。
  - SNS 台词（`SnsSayCo`）同样中招；`ChoiceCo` 的 `while (chosen < 0)` 僵尸永久泄漏。
  - 行尾 `@` 的异步命令本来就是独立协程，同样不随读档停止。
- 修复方向：Runner 里给协程加「代数 token」（Stop 时 ++，各等待循环发现 token 过期立刻退出），
  或记住当前内层 Coroutine 句柄一并 Stop。不能直接 `StopAllCoroutines`（会误杀存档截图协程）。
- 把握度：已确认（代码路径）。依赖 Unity「StopCoroutine 不级联到 StartCoroutine 出来的子协程」语义，
  建议运行时按上述步骤复现一次。

### H2 每次 event / 读档都弹「自动模式 关」

- 位置：`VNScriptRunner.Input.cs:163-169`（`SetAuto` 无「状态没变就返回」，无条件 `VNToast.Show`）；
  调用点 `VNScriptRunner.Commands.cs:338`（EventCo）、`:121`（sns open）、
  `VNScriptRunner.SaveLoad.cs:58`（LoadFrom）
- 触发步骤：玩家从没开过 Auto，正常游玩遇到任何 `event`（商店、QTE、排程），或读一次档。
- 后果：左上角每次都冒一张「自动模式 关」。`event plan op:next` 是无 UI 的逐格派发，
  节日活动集一周调 7 次，等于连弹 7 张（上限 5 张，把真正有用的提示挤掉）。
  读档时与「已读取」一起弹。对比 `SetSkip` 有 `if (_skip == on) return;`，
  `PauseForSaveLoadMenu` 也是 `if (_auto)` 才调，只有这三处漏了。
- 修复方向：`SetAuto` 开头加 `if (_auto == on) return;`。
- 把握度：已确认。

---

## 中

### M1 SNS 聊天中存档，读档后当前这条气泡重复一遍

- 位置：`VNScriptRunner.Commands.cs:74`、`VNScriptRunner.Dispatch.cs:63`、
  `VNScriptRunner.SaveLoad.cs:33,70`、`VNSnsView.cs:509,526-533`
- 触发步骤：`sns open` 后停在一句气泡上 → 按 Q 或 F5 存档 → 读档。
- 后果：存档 `commandIndex` 指向这句 say，而 `snsMessages` 已包含它的气泡（气泡在进入等待前就 Append 了）。
  读档时 `RestoreSnapshot` 先重建全部消息，`ResumeAt` 再执行这句 say → 同一条消息出现两次。
  此时再存再读，每轮再多一条。
  （调试重建 `ReplaySnsSay` 只重放 start 之前的命令，不会重复，所以编辑器「从选中行播放」测不出来。）
- 修复方向：`CaptureSnapshot` 时若正停在 say 上，去掉最后一条消息；
  或 ResumeAt 后让 SnsSayCo 跳过一次 Append（需一个「恢复首句」标记）。
- 把握度：已确认。功能本身是高，主线未用故列中。

### M2 第一帧在标题画面弹「新任务：每月的陪练」，新游戏再弹一次

- 位置：`VNQuestLog.cs:37`（`_dirty = true` 初值）、`:86`（非 silent 的 Evaluate）、
  `VNQuestEngine.cs:350-355`、`VNToast.cs:165`（排序 999，压在标题菜单 500 之上）；
  数据 `Assets/Art/VNEffects/Quests/每月的陪练.asset`（`acceptAuto: 1`、`unlockCondition` 空）
- 触发步骤：启动游戏，标题画面第一帧 VNQuestLog.Update 跑 Evaluate，空 flag 下该任务 `CanAccept` 为真。
- 后果：玩家还没开始游戏，标题画面左上角就出现任务提示。
  点「开始游戏」→ `VNFlags.Clear()` → 重新自动接取，开场再弹一次。
  引擎没有「游戏进行中」的概念，Evaluate 在标题期间照跑。
- 修复方向：标题菜单打开时不求值（或 silent），或 `_running` 之前不做自动接取。
- 把握度：已确认。

### M3 主线的两条任务完成不了；羽毛球接入后「每月」任务又变成白拿奖励

- 位置：`Assets/Scenarios/第1章.vn.txt:29`（`quest start 球场之王`）；
  `Assets/Art/VNEffects/Quests/球场之王.asset`、`每月的陪练.asset`（条件都读 `羽球_我方得分@…`）；
  `VNQuestEngine.cs:361-370,393-396`
- 触发步骤 A：正常玩主线。第 1 章第 29 行接取「球场之王」，「每月的陪练」被自动接取；
  但主线唯一的 `event badminton` 在第 1 章是 `#!` 注释掉的，其余只在 Demo 剧本。
- 后果 A：J 面板里两条任务永远挂着，试玩内无法完成。
- 触发步骤 B（一旦接入羽毛球）：打完一场，`羽球_我方得分@次数>=1` 永久为真。
  此后每次 `time pass` → 冷却到期回未接取 → 自动接取 → 下一帧立刻「可领取」。
- 后果 B：只打一场，之后每月白拿 +3 好感。且奖励写的是 flag `好感度`，
  主循环用的是 `好感度_结衣`，这份奖励对主线没有效果。
- 修复方向：试玩版先从 VNGameConfig 摘掉或改写这两条。「每月」类条件需要按周期计数的 flag
  （领取时清零，或接取时记基线），累计 `@次数` 不适合做周期条件。
- 把握度：已确认（B 按代码推演，未实跑）。

### M4 读档后 HUD 把差值当成新变化，满屏 +N 飘字

- 位置：`VNStatsHud.cs:57,316,333-383`（`RefreshHud(true)` 对所有变化都播演出）、
  `VNSaveSystem.cs:233-235`
- 触发步骤：当前进度与存档属性值不同 → 读档（包括从标题读档）。
- 后果：下一帧 `_dirty` 走 `RefreshHud(true)`，每个 HUD 属性都播数字滚动 + 图标弹跳 + `+123`/`-40` 上飘；
  开着 `statsHudAutoHide`（当前配置为 1）时 HUD 还会被 `HoldForReveal` 叫下来。
  没有发生任何属性变化，玩家却看到一排增减。任务/Toast 这边 Suspended 已挡住，只有 HUD 漏了。
  `RefreshHud(true)` 全项目只有 Update 这一个调用点，Grep 未见其他抑制手段。
- 修复方向：`VNSaveSystem.Load` 结束时通知 HUD 下一次刷新走 `RefreshHud(false)`
  （比如一个静态 `FlagsRestored` 事件，或挂 `VNTracker.Suspended` 同款开关）。
- 把握度：已确认。

### M5 Accept 没有状态守卫：能把进行中/已完成的任务打回阶段 1

- 位置：`VNQuestEngine.cs:171-184`（只判 id 非空）、`VNQuestReward.cs:110`、`VNQuestLog.cs:158-160`；
  同类 `SetStage`（`:187-203`）；数据 `Assets/Art/VNEffects/Quests/摄影社的委托.asset`（`kind: 4` 连锁接取 每月的陪练）
- 触发步骤：
  1. 「每月的陪练」处于可领取或冷却中（stage 100）。
  2. 领取「摄影社的委托」阶段奖励，`Grant` 直接 `Accept("每月的陪练")`，不走 `CanAccept`。
  3. 或剧本里重复走到同一行 `quest start X`（循环段落/回访分支）。
- 后果：`@待领` 被清零、`@接取月` 刷新（限时被续命）；已完成任务回到阶段 1，
  绕过 `maxTimes` 和冷却，可再次走「待领 → 领取」重复拿奖。
  VNQuestEngine 注释里「幂等靠状态机」只保护 Claim 自身，挡不住从外部把状态机拨回去。
- 修复方向：奖励连锁改走 `CanAccept`；`quest start` 对非 0 状态告警并跳过（要重开就写 `quest reset`）。
- 把握度：已确认。

### M6 模块 OnLaunch 抛异常 = 全部输入永久失效

- 位置：`VNScriptRunner.Commands.cs:343,360`；抛出点 `VNPlanModule.cs:240-241`、
  `VNResultPopupModule.cs:84-85`；可抛点 `VNShopModule.cs:140`、`VNBattleModule.cs:250`
  等 `(RectTransform)transform` 强转
- 触发步骤：`planPrefab` / `resultPopupPrefab` 的必需槽位校验失败（改 prefab 时丢引用），
  或模板物体不带 RectTransform，或 OnLaunch 内任何异常。
- 后果：`_eventActive = true` 已经设上，`Launch` 抛异常杀死 EventCo，外层 Run 永远等它。
  `Update` 第一行 `if (_eventActive) return`，键盘鼠标全废，存不了读不了，只能 Alt+F4。
  `Dispatch` 的 try/catch 包不住协程内部。
  `VNSystemUiSkinUtility` 的日志和 VNPlanModule/VNInventory/VNStatsHud 的类注释都写「退回默认 UI」，
  但程序化默认 UI 已不存在，这里实际是 throw。
- 当前状态：`VNSystemUiSkinSet_Default.asset` 的 plan/result/inventory/statsHud 槽都有引用，
  正常不触发；prefab 内部槽位完整性我没有逐项验证。
- 修复方向：`module.Launch` 外包 try/catch → 记错误、销毁模块、`_eventActive=false`、按结果 "" 继续。
  同时把「退回默认 UI」的注释和日志改成真实行为。
- 把握度：路径已确认；当前无已知触发条件。

### M7 存档文件为空时：读档失败但当前 flag 已被清空

- 位置：`VNSaveSystem.cs:223-235`、`:159`（`File.WriteAllText` 非原子，写一半崩溃会留下空/坏文件）
- 触发步骤：槽位文件 0 字节（存档时崩溃或断电）→ 读该槽。
  `JsonUtility.FromJson("")` 返回 null，`Load` 进入 try 后先 `VNFlags.Clear()`，
  再在 `data.flagNames` 处 NRE → catch 后 return null。
- 后果：`LoadFrom` 提示「槽位为空」后继续当前进度，但全部 flag（属性、道具、任务）已被清掉，
  再存档就坏档。Suspended 本身由 finally 正确还原，这里的问题是 **Clear 在校验之前**。
  文件损坏（JSON 语法错）时 FromJson 抛异常在 Clear 之前，不受影响。
- 修复方向：`FromJson` 后先判 `data == null` 再动 VNFlags；写档改「写临时文件 → File.Replace」。
- 把握度：待验证（需确认 FromJson 对空串返回 null；代码顺序已确认）。此点也属存档组范围。

---

## 低

### L1 可重复日常超期失败后永远不能再接

- 位置：`VNQuestEngine.cs:374,142`；`VNQuestDef.cs:197`（`expireToFail` 默认 true）
- 触发：`repeatable` + `deadlineMonths>0` 的任务超期 → stage 变 -1 → Evaluate 直接 continue，
  `CanAccept` 要求 stage==0。冷却重置只处理 stage 100。
- 后果：日常任务一次失败永久消失。现有资产没有这种组合。
- 修复方向：repeatable 的失败也写 `@重置月`，在 Evaluate 里回到 0。
- 把握度：已确认。

### L2 道具奖励/惩罚没有下限，也不处理装备

- 位置：`VNQuestReward.cs:97`；`HandleItemLost` 全项目仅 `VNShopModule.cs:114` 一处调用
- 触发：`kind=Item` 且 amount 为负，持有 0 → `道具_x` 变负；持有 1 且已装备 → 持有归零但装备状态与加成留着。
  剧本 `flag 道具_x -1` 同理。
- 后果：之后买 1 个显示 0；背包里没有该道具，装备栏却还在生效。现有资产无此配置。
- 修复方向：Item 分支夹到 ≥0 并调 `VNEquipment.HandleItemLost`。
- 把握度：已确认。

### L3 SNS 读档后对话框和快捷条露在手机后面

- 位置：`VNScriptRunner.SaveLoad.cs:66-67,261-267`、`VNSnsView.cs:139-140,172-173`
- 触发：SNS 打开时读档。`RestoreSnapshot` → Open 把对话 UI 关掉，紧接着 `RestoreUiHidden` →
  `ApplyUiHidden` 无条件 `SetInterfaceVisible(true)`。
- 后果：暗幕后面能看到对话框和快捷条（`_shown` 为真时）。
  反向也有：`sns close` 无条件 `SetInterfaceVisible(true)`，会把 `hideHUD keep dialogue` 藏起来的对话框放出来。
- 修复方向：`ApplyUiHidden` 把 `stage.IsSnsOpen` 算进去；`Close` 走 Runner 的统一入口。
- 把握度：已确认。

### L4 模块提供了剧本没接的结果，走了「顺序继续」

- 位置：`VNBattleModule.cs:313-318,149`（默认 `escapeChance=50` 就显示逃跑钮，未用 `AcceptsOutcome`）、
  `VNQuestBoardModule.cs:62`、`VNScriptRunner.Commands.cs:383`
- 触发：`event battle` 只写 `* 胜利` `* 失败`、没写 `escape:0` → 玩家逃跑成功。
- 后果：结果无匹配，从事件下一行顺序往下走，通常正好落进「胜利」段落。
  委托板只写 `* 离开` 时，接过任务后的「接取」结果同理。现有 Demo 剧本写法正确。
- 修复方向：battle 用 `ctx.AcceptsOutcome("逃跑")` 决定显不显示按钮；Lint 增加对应检查。
- 把握度：已确认。

### L5 问答题答案序号 ≥4 时题目必错

- 位置：`VNQuizDef.cs:90-92`（`answerIndex < options.Count`）、`VNQuizModule.cs:180,277`
- 触发：题目配 5 个以上选项且正确项是第 5 个及之后。
- 后果：`IsValid` 通过，但界面只显示前 4 个，正确答案不在屏幕上，这题怎么答都错。
- 修复方向：`IsValid` 同时要求 `answerIndex < MaxOptions`。
- 把握度：已确认。

### L6 隐藏任务接取后也不显示，可领取时无处点

- 位置：`VNQuestLog.cs:353`、`VNQuestDef.cs:146`（字段说明是「未接取时不出现」，
  代码是「完成前都不出现」）
- 触发：`hidden` 任务配了条件 → 转可领取。
- 后果：右上角角标显示有 1 个可领（`ClaimableCount` 不看 hidden），面板里没有这一条，领不到。
  现有资产无 `hidden: 1`。
- 修复方向：`q.hidden && stage != StageDone && pending <= 0` 才跳过。
- 把握度：已确认。

### L7 其他

- **新游戏属性初始值竞态**：`VNScriptRunner.Flow.cs:419` Clear 后，初始值要等下一次 `VNStatsHud.Update`
  的 `EnsureInitials` 才写回；`Run` 在 StartCoroutine 内同步执行到第一个 yield。
  若入口剧本开头在任何等待之前写 `stat 金钱 +100`，结果是 100 而不是 600，初始值被永久跳过。
  第 1 章和主循环目前的写法不触发。修复：`StartNewGame` 里 Clear 后立刻 `EnsureInitials()`。已确认。
- **隐藏的包依赖**：`VNQteModule.cs:3` 有未使用的 `using Unity.AppUI.UI;`，该包只是
  `com.unity.ai.inference` 的传递依赖（manifest 里没有直接列）。移除 inference 包后 QTE 文件编译失败。
  修复：删掉这行。待验证（未试删包）。

---

## 已核查、未发现问题

- **Suspended 成对**：`VNSaveSystem.cs:229-241`、`VNScriptRunner.cs:278-285` 都是 try/finally；
  `RecalculateSilently` 在两处调用时 Suspended 均已是 false，抛异常也不会把它留在 true。
- **VNTracker 防重入**：`_writing` 有 try/finally；读档时不派生。
- **flag 事件递归 / 遍历时改集合**：`VNFlags.Changed/KeyChanged` 全项目只有 4 个订阅者
  （HUD、日历、任务日志只标脏，Tracker 有 `_writing`）。所有 `foreach VNFlags.All` 要么只读，
  要么先收集到 List 再写（VNEquipment.Unequip / RecomputeEffects）。
  Evaluate 遍历的是 `_defs`，Accept/Fail 不改它；`Configure` 不会在 Evaluate 中途被调用。
- **读档刷奖**：存档只在台词处，flag 与 resume 点一致，resume 不重放已执行命令；
  奖励只在 Claim 路径发，`quest done` 不发奖。
- **限时基准**：deadline、冷却、面板剩余月数全部走 `月序`；`time set` 只初始化不重置。
- **金钱**：`Money < price` 与 `maxOwned` 都判了；金钱上限 999999，正常不溢出钳制；卖光自动卸装备。
- **装备钳制**：穿戴记实际增量、卸下按实际量扣回；换装先卸；按部位记账无串位。
- **战斗**：HP/攻击都 `Max(1)`，分母不为 0，伤害至少 1，必然终止；我方可防御但无死循环。
- **模块铁律**：带 Update 的模块首行都有 `VNPause.IsPaused` 早退（在读输入之前），计时走 `VNTime.Delta`，
  Tween 均 `SetUpdate(true)` + `SetLink`。Map/Plan 无 Update、无计时。
  模块 `Done` 同步回调（`op:next`、找不到定义）在 EventCo 里能正常处理。
- **SNS 限时回复**：回复期间 Runner 输入全屏蔽、存档被挡；`SnsReplyCo` 先 `SetSkip(false)`；
  倒计时走 `VNTime.Delta`；A/S 在 SNS 下被屏蔽。
- **日历 HUD**：无 flag 时隐藏，无副作用。

---

## 实际读完的文件

完整读完（范围内全部）：VNQuestBoardModule、VNQuestDef、VNQuestEngine、VNQuestLog、VNQuestReward、
VNTracker、VNStatDef、VNStatsHud、VNShopDef、VNShopModule、VNInventory、VNEquipment、VNPlanDef、
VNPlanModule、VNBattleModule、VNQuizDef、VNQuizModule、VNMapModule、VNQteModule、VNResultPopupModule、
VNSnsView、VNSnsMessage、VNCalendarHud。

为追调用链额外读的：
- 完整：VNEventModule、VNFlags、VNPause（含 VNTime）、VNSystemUiSkinBehaviour、
  VNScriptRunner.Flow、VNScriptRunner.Input、VNScriptRunner.SaveLoad、`.claude/skills/vn-new-event-module/SKILL.md`
- 部分：VNSaveSystem（150-255 行）、VNScriptRunner.cs（240-329 行）、
  VNScriptRunner.Commands（1-420 行）、VNScriptRunner.Dispatch（1-135 行）、VNToast（1-110 行）、
  VNEdgeAutoHide（HoldForReveal）、VNDialogueBox（SetInterfaceVisible）、VNTitleMenu（95-125 行）、
  `第1章.vn.txt`（1-60 行）
- 数据：VNGameConfig.asset（节选）、四个任务资产、属性资产、VNSystemUiSkinSet_Default.asset

未读 / 未验证：
- VNStage.Save.cs 只看了 ClearStage 的 grep 结果，未通读快照恢复。
- 皮肤 prefab 内部槽位完整性未验证（M6）。
- VNTutorialPlayer 的射线遮挡未读：Plan/Map/Battle 等按钮在教程暂停期间能否被点到，以其遮罩为准，本报告未下结论。
- 全程未运行游戏。

---

# 审计 7：AI 子系统（试玩版关闭 AI 的风险 / 公开仓库 Key 安全 / AI 功能缺陷）

审计方式：只读。以当前工作区（分支 feature/code-slimming）为准。未打包、未运行，所有结论来自静态走读。

## 一、结论先行

**试玩版（AI 关闭）没有发现致命或阻塞问题，也没有发现 Key 泄露。**
真正会被试玩玩家碰到的只有一条小问题：D 键能打开一个永远是空的日记本（见 F7）。
AI 功能自身的缺陷集中在完整版，试玩版走不到。

严重度计数：致命 0 / 高 1 / 中 5 / 低 14（共 20 条）。其中影响试玩版的只有 F7、F8（低），F9 属于仓库安全隐患（低）。

| 编号 | 严重度 | 标题 | 试玩影响 | 把握度 |
|---|---|---|---|---|
| F1 | 高 | 断网/被拦/余额不足都不走 `* 失败`，最后按「普通」结算 | 无 | 已确认 |
| F2 | 中 | 日语下 AI 台词被「繁转简」改字，总结/日记固定简中，兜底台词不本地化 | 无 | 已确认 |
| F3 | 中 | 失败轮留下悬空 user 消息，Gemini 下可能一路 400 到底 | 无 | 待验证 |
| F4 | 中 | 记忆容量是全局共用，多女主互相挤掉记忆 | 无 | 已确认 |
| F5 | 中 | 好感结算绕过属性钳制 | 无 | 已确认 |
| F6 | 中 | 试聊台点旧轮次的选项会把那一轮本身删掉（差一错误） | 无（编辑器） | 已确认 |
| F7 | 低 | D 键打开永远空白的日记本 | **有** | 已确认 |
| F8 | 低 | 无 AI 总开关，人格资产仍随包发布 | **有（包内容）** | 已确认 |
| F9 | 低 | .gitignore 只覆盖根目录固定命名 | 仓库隐患 | 已确认 |
| F10 | 低 | 模型输出未关 TMP 富文本 | 无 | 待验证 |
| F11 | 低 | 每轮都要求「引向收尾」，且破坏提示缓存 | 无 | 代码已确认，效果待验证 |
| F12 | 低 | 在途请求不可取消，取消路径不释放 UnityWebRequest | 无 | 已确认 |
| F13 | 低 | Validate 把软提示当硬错误，事件直接判失败 | 无 | 已确认 |
| F14 | 低 | 新游戏不清 VNAiMemory（目前无返回标题入口，暂不可达） | 无 | 已确认 |
| F15 | 低 | 日记文件损坏后下一次写入覆盖全部 | 无 | 已确认 |
| F16 | 低 | 三份人格资产同 id，重跑装机后静默换人格 | 无 | 已确认 |
| F17 | 低 | 试聊台「options 覆盖」只影响预览 | 无（编辑器） | 已确认 |
| F18 | 低 | 历史裁剪后回退失败却仍删轮次 | 无（编辑器） | 已确认 |
| F19 | 低 | 试聊台日志成本按最贵档计算、缓存数丢失 | 无（编辑器） | 已确认 |
| F20 | 低 | 新建记忆预设的名字碰撞会覆盖旧预设 | 无（编辑器） | 已确认 |

---

## 二、第一优先：关闭 AI 的试玩版，逐项核查结果

| 检查项 | 结果 | 依据 |
|---|---|---|
| Build 是否读取 Key | **不读**。环境变量与文件读取都在 `#if UNITY_EDITOR \|\| VN_AI_ALLOW_LOCAL_KEY` 内；`VN_AI_ALLOW_LOCAL_KEY` 在 ProjectSettings 所有平台的 define 里都没有，也没有 `.rsp` 文件（全工程 Grep 只命中 VNAiKey.cs 和两份文档） | VNAiKey.cs:79-110；ProjectSettings.asset:835 起 |
| Build 是否访问网络 | **不访问**。全 Assets 的 `.cs` 里 UnityWebRequest / HttpClient / System.Net / OpenURL 只在 VNAiClient、VNAiKey（环境变量）、编辑器文件。`VNAiClient.Send` 拿不到 Key 时在第一个 `yield` 之前就返回 NoKey | VNAiClient.cs:177-181 |
| 注意：文档与代码的差别 | CLAUDE.md 写「`#if UNITY_EDITOR` 挡住」，实际还有 `VN_AI_ALLOW_LOCAL_KEY` 这个后门。**发布前不要加这个 define**，否则 Build 会读玩家环境变量和 exe 所在目录及上级的 Key 文件 | VNAiKey.cs:79,123 |
| 第三方网络 | MCP 插件的 Runtime 程序集会编进 Standalone（`UNITY_MCP_READY` 在 Standalone 的 define 里，asmdef `includePlatforms` 为空），但 `RuntimeInitializeOnLoadMethod` 在 Plugin 入口只出现在示例注释里，工程代码也没有调用 `UnityMcpPlugin*`，因此不会自动连接，只是白占包体 | Library/PackageCache/com.ivanmurzak.unity.mcp@…/Runtime/UnityMcpPluginRuntime.Static.cs:95 |
| Key 是否可能打进包 | **不会**。Key 文件在项目根（Assets 之外）；工程没有 StreamingAssets；Assets 下无 `*apikey*` 文件；`VNGameConfig.asset` 里只有 `aiProvider` / `aiModel`（空）/ `aiPricing`（空），没有 key 字段 | Assets/Resources/VNGameConfig.asset:270-274 |
| Build 是否编译失败 | 静态看不会。`Script/VNAi*.cs` 全部读完，无 `UnityEditor` 引用；`Editor/` 是特殊目录且 `Assets/Project` 下无 asmdef，试聊台等不会进 Player。`#if` 关闭后 VNAiKey 只剩未调用的私有方法，不影响编译。**未实际打包验证** | VNAiKey.cs |
| 无 Key 时 `event aitalk` | **走 `* 失败` 且不卡死**：NoKey 立即返回 → fatal → 说一句兜底台词 → `FinishWith("失败")`。只有 NoKey / Auth 走这条，其他失败见 F1 | VNAiTalkModule.cs:216-223,242 |
| 试玩路径是否依赖 aitalk | **不依赖**。26 个 `.vn.txt` 全部 Grep，`event aitalk` 只在 `AiTalkDemo.vn.txt:24`；主循环、第1~3章、好感事件_星野结衣、节日活动集、结局集、公共片段均无；`闲聊_` / `AI聊天_` / `倾向_` 这些 flag 也只有该 demo 用。没有任何 jump/call 指向它，它的 GUID 不在 VNGameConfig.asset 和 VNScriptDemo.unity 里，所以该剧本**不会进包** | Grep 结果 |
| VNGameConfig 总开关 | **没有**，见 F8。当前靠「剧本不调用」实现关闭，功能上够用 | VNGameConfig.cs:235-249 |
| AiTalkLogs 在 Build 写盘 | **不写**。场景模板 `logMode: 0`（EditorOnly），Build 下 `Application.isEditor=false` → `Enabled=false`，`Save()` 直接返回。且 aitalk 根本不运行 | VNScriptDemo.unity:16297；VNAiTalkLog.cs:62-64 |
| 日记写盘 | 不写。`VNAiDiary.Save` 只由 Add/Remove/ClearAll 触发，试玩版无调用者；打开面板只读（文件不存在则空列表） | VNAiDiary.cs:53-71 |
| D 键 | 能开，空面板，可正常关闭，不崩。见 F7 | VNScriptRunner.Input.cs:331-335 |
| VNAiMemory 进存档 | 无影响。每个存档多一个空的 `aiMemories: []`；读档清空再灌入，空列表无副作用；将来完整版读试玩档也兼容（JsonUtility 缺字段给默认空表）。**不要为了试玩删这个字段** | VNAiMemory.cs:135-151；VNSaveSystem.cs:72；VNStage.Save.cs:145 |

---

## 三、第二优先：公开仓库与 Key 安全

| 检查项 | 结果 |
|---|---|
| Key 文件被 Git 跟踪 | 否。`git ls-files` 无；`git check-ignore` 确认两个 Key 文件均被忽略 |
| Key 文件进过历史 | 否。`git log --all -- <两个 Key 文件>` 0 条 |
| 跟踪文件内容含 Key 形态串 | 0 个文件（`git grep -I`，模式覆盖 `AIza…`、`sk-…`、`sk-ant-…`、`ghp_…`、`github_pat_…`、`xox[bp]-…`、私钥头） |
| 未跟踪且未忽略的文件 | 0 个文件（`git grep --untracked`，AIza / sk- 模式） |
| 历史提交内容 | 0 个提交（`git log --all -G`，AIza / sk- 模式，**仅限** md/txt/json/toml/cs/asset/yml/yaml 后缀；不含 .unity/.prefab） |
| `.mcp.json`、`.codex/config.toml`、`.agents`、`UserSettings` | 模式 0 命中；`.mcp.json` 无 token/secret/key/auth 类键名。`.claude/settings*.json` 不存在 |
| 日志 / 对话记录 | `AiTalkLogs/`、`AiTalkStudio/` 已忽略且未跟踪；跟踪文件中无 `.log` / `.jsonl`（`conversation` 只命中 VNAiConversation.cs 这个代码文件） |
| 个人信息 | 邮箱 0 个跟踪文件；Windows 用户名 `TonyCheung` 出现在 1 个跟踪文件（GameIdea.txt），无实质风险，要清就清 |

局限：这些是按已知 Key 前缀的模式扫描，**不能**证明不存在其他形态的密钥；对话内容只按文件名和后缀判断，没有逐篇读 .md 文档（AiTalkGuide 只 Grep 过）。

---

## 四、问题详情

### F1【高｜完整版】断网、被拦、余额不足都不走 `* 失败`，最后按「普通」结算

- **位置**：VNAiTalkModule.cs:216-223（只有 NoKey/Auth 为 fatal）、:236-237、:259、:276-277、:458-463；VNAiClient.cs:234-262
- **触发**：完整版开 AI，玩家断网（或被内容安全拦截、响应解析失败、DeepSeek 余额耗尽）→ 进 `event aitalk`。
- **后果**：
  - 每轮走兜底台词「她好像走神了」+ 三个固定选项，循环到轮数上限，然后 `_affectionTotal==0` 判「普通」，**剧本的 `* 失败` 永远走不到**。这与模块头注释（第 23 行）、VNAiPersonaDef.cs:130「留空 = 直接结束事件返回『失败』」、CLAUDE.md「`* 失败` 必须接住否则玩家断网会静默跳过」全部矛盾。Grep 确认 `fallbackLines` 留空时只是用默认句，没有结束事件的代码。
  - 防火墙吞包这类超时最慢：单轮 3 次 × 30s + 退避 3.6s ≈ 94 秒，默认人格 14 轮约 22 分钟；Thinking 阶段按 ESC 确认后也要等当前请求返回。
  - DeepSeek 余额不足返回的是 402（外部知识，待验证），走 `responseCode != 200` → BadResponse → 非 fatal；VNAiClient.cs:237 的提示以为余额不足在 401/403 里。
- **修复方向**：统计连续失败次数，同类失败连续 2 次（或 Network/Blocked 首轮失败）就 `FinishWith(OutcomeFailed)`；402 归入 Auth 类 fatal；`fallbackLines` 留空时实现注释承诺的行为。
- **把握度**：已确认（402 一项待验证）。

### F2【中｜完整版】日语环境 AI 文本被改字；总结与兜底不跟语言

- **位置**：VNAiConversation.cs:517、549（台词/选项）、655（总结提示词固定简中）、731-741；VNAiTextNormalize.cs:33-42；VNAiPersonaDef.cs:131-140
- **触发**：语言设为日语，AI 回复含「東」「見」「話」「語」「時」「間」「電」「風」等。
- **后果**：`ToSimplified` 无语言判断，「東京」变「东京」、「話」变「话」，日语台词里混入简体字（字体可能还缺字）。日语/英语玩家的日记固定是简体中文。兜底台词和选项是资产里写死的中文，不走 `VNLocale.T`。
- **修复方向**：`ToSimplified` 只在 `VNLocale.Language` 为简中时调用；总结复用 `LanguageRule`；兜底文案改三语或用 locale key。
- **把握度**：已确认。

### F3【中｜完整版】失败轮留下悬空 user 消息

- **位置**：VNAiConversation.cs:150-151、219-224；VNAiTalkModule.cs:216-238
- **触发**：`BuildRequest` 先把玩家发言写进 `_history`，失败或解析失败时不调用 `RecordReply`（studio 会，模块不会）→ 玩家选兜底选项 → 下一轮历史出现连续两条 user。
- **后果**：代码自己在 VNAiConversation.cs:109-110 写明「Gemini 直接 400」。若属实，一次偶发 429 之后本场每轮都 400（BadResponse 非 fatal），全程走兜底。默认供应商 DeepSeek 大概率不受影响。
- **修复方向**：失败时撤回本轮 user，或把兜底台词也 `RecordReply` 进去（与 VNAiStudioSession.cs:177 一致）。
- **把握度**：待验证（需实测 Gemini 当前是否仍拒绝连续同角色消息）。

### F4【中｜完整版】记忆容量全局共用

- **位置**：VNAiMemory.cs:52-59；VNAiTalkModule.cs:325-336
- **触发**：两位女主都有人格。A 的 `memoryCapacity=1`（或两人合计聊满 15 场）。
- **后果**：`Add` 按当前人格容量裁整张 `_entries`，不分角色，一个角色的新记忆把另一个角色的旧记忆挤掉；人格字段说明却是「最多记住最近几场」。
- **修复方向**：按 `characterId` 分别裁剪。
- **把握度**：已确认。

### F5【中｜完整版】好感结算绕过属性钳制

- **位置**：VNAiTalkModule.cs:475-477（对照 VNFogWipeModule.cs:511-512、VNQuestReward.cs:92、VNStatsHud.cs:147）
- **触发**：`event aitalk stat:好感 rate:2`，好感接近上限/下限时聊出正/负值。
- **后果**：`VNFlags.Add` 不经 `VNStatsHud.Apply`，好感可超过 `maxValue` 或低于 `minValue`，同时没有 HUD 飘字。擦雾等模块都是「有 HUD 走 Apply」。
- **修复方向**：同 VNFogWipeModule 的写法。
- **把握度**：已确认。

### F6【中｜编辑器】试聊台点旧轮次选项，差一错误

- **位置**：VNAiStudioSession.cs:191-200；VNAiStudioSession.cs:230-251
- **触发**：试聊 3 轮后点第 1 轮的某个选项。
- **后果**：注释写「先把后面的都丢掉」，实际 `BranchFrom(turnIndex)` 连被点的这一轮也删除：她说的那句话从历史和界面消失，模型收到的是对一句看不见的话的回复；点第 0 轮时还会形成 `[开场, 选项]` 连续两条 user。调提示词时看到的分岔结果是错的。
- **修复方向**：改 `BranchFrom(turnIndex + 1)`，并让被保留轮次保持有效。
- **把握度**：已确认。

### F7【低｜影响试玩】D 键打开永远空白的日记本

- **位置**：VNScriptRunner.Input.cs:331-335；VNScriptRunner.SaveLoad.cs:195-206；VNAiDiaryPanel.cs:84-91；Assets/Resources/VNLocale/ui.zh.txt（`diary.empty`）
- **触发**：试玩中台词处按 D。
- **后果**：弹出全屏「日记本 / 还没有写下任何一页。」，试玩版里永远写不进内容，玩家会以为是没做完的功能或 bug。可用 D/ESC/点击关闭，不崩。
- **顺带**：`diary.empty` 的第二行在三份语言表里都是独立一行、没有 `=`，被 VNLocale 解析器跳过（VNLocale.cs:129），本意应是 `\n`。**这个 bug 对试玩版反而有利**：那句是「和她聊过天之后，这里会留下你的记录」。若修它，试玩版就会提示一个不存在的功能。
- **修复方向**：试玩版屏蔽 D 键（配置开关或 define）。别的代码都不用动。
- **把握度**：已确认。

### F8【低｜影响试玩包内容】无总开关，人格资产随包发布

- **位置**：VNGameConfig.cs:235-249；Assets/Resources/VNGameConfig.asset:270-271；VNScriptDemo.unity:16294-16295
- **触发**：打包。
- **后果**：`VNGameConfig` 在 Resources、场景模板也引用人格，`星野结衣_日常.asset` 的人设与提示词文本会明文进包（不含 Key）。没有总开关意味着「不开放 AI」只是约定：将来有人往试玩路径加一行 `event aitalk` 不会有任何拦截，只会看到一句走神台词接 `* 失败`。
- **修复方向**：打试玩包前清空 `aiPersonas` 与模板 `personas`（注意 VNGameConfig 是共用资产，别提交）；或加 `aiEnabled` 开关，由 `VNEventRegistry.Create` 或 Linter 拦截。先确认想要哪种再做。
- **把握度**：已确认。

### F9【低｜仓库隐患】.gitignore 覆盖面

- **位置**：.gitignore:54-56
- **触发**：`git check-ignore` 实测未被忽略：`Assets/DeepSeekAiApiKey.txt`（子目录）、`deepseek_key.txt`（改名）、`.env`、`Trial/`（非 Build/Builds 的打包目录）、`TutorialEditor/`（VNTutorialBackdrop.cs:21 注释称「不进 git」）、`ProfilerCaptures/`（目前为空）。
- **后果**：当前没有泄露。但仓库是公开的，Key 只靠「文件名恰好匹配且在根目录」保护；放进 Assets 还会同时有进包风险。项目已禁用 `git add -A`，这是主要的那道闸。
- **修复方向**：把模式改成不带 `/` 的 `*ApiKey*.txt`、`*.apikey`，加 `.env*`、`*key*.txt`（按需）、`/TutorialEditor/`、`/ProfilerCaptures/`，以及试玩打包目录。
- **把握度**：已确认。

### F10【低】模型输出未关 TMP 富文本

- **位置**：VNAiConversation.cs:517、549；VNAiTalkModule.cs:420、449；VNAiDiaryPanel.cs:168
- **触发**：模型在 reply/options/diary 里输出 `<size=300>`、`<color=...>`、`<alpha=#00>` 等。对话框、选项、日记相关文件都没有设 `richText=false`（TMP 默认开启）。
- **后果**：文字尺寸/颜色错乱或被隐藏，打字机动画基于 textInfo 也受影响。游戏内玩家没有自由输入，无法主动注入，只有模型自己吐标签才触发。**进不了剧本命令**：`VNStage.Say` → `VNDialogueBox.Say` → 打字机，不过 VNScriptParser；emotion/mark 走白名单，flag 名只用资产里的 tone。
- **修复方向**：解析层剥掉 `<...>` 或包 `<noparse>`。
- **把握度**：待验证。

### F11【低】每轮都要求「引向收尾」，且破坏提示缓存

- **位置**：VNAiConversation.cs:284-286；VNAiTalkModule.cs:367
- **触发**：任意一场。`turnsLeft` 恒大于 0，第 1 轮的提示词就是「大约还剩 14 轮，请自然地把话题引向收尾」。
- **后果**：可能诱导模型早给 `should_end=true`（VNAiTalkModule.cs:259 会直接结束，效果待验证）。`turnsLeft` 位于 system prompt 中段且每轮变化，DeepSeek 前缀缓存只能命中到这之前，后面的格式段和全部历史都按未命中计费，与 VNAiClientDeepSeek.cs:31-33 的假设相反。
- **修复方向**：只在剩余 ≤2 轮时写这句，并移到消息末尾或最后一条 user 里。
- **把握度**：代码已确认，对话效果待验证。

### F12【低】在途请求不可取消，取消路径不释放

- **位置**：VNAiClient.cs:192-204；VNAiTalkModule.cs:172、206-210、298；VNAiEditorCoroutine.cs:58-63
- **触发**：① 协程被 Destroy/Stop（`CleanupActiveEvent`、窗口关闭）时停在 `yield return SendWebRequest`；② 玩家 ESC 确认结束。
- **后果**：① 迭代器不会被 Dispose，`using` 不执行，请求继续发完并计费，UnityWebRequest 等 GC 回收，`onDone`「必定回调一次」的注释也不成立。② ESC 后最多再等 94 秒，随后还要等一次总结请求。费用有上限（重试 ≤2、轮数有界），**未发现失控循环**。
- **修复方向**：Send 暴露取消令牌，`Abort()` 请求；ESC 退出时跳过总结或限时。
- **把握度**：已确认。

### F13【低】Validate 把软提示当硬错误

- **位置**：VNAiPersonaDef.cs:277-278；VNAiTalkModule.cs:112-119
- **触发**：人格 `historyTurns=20, defaultTurns=12`（剧本 `turns:30` 时这是合理配置）。
- **后果**：事件立即返回「失败」，只有 Console 一行 Error，错误文案却说「多余的保留没有意义」。现有资产相等，未中招。
- **修复方向**：Validate 分 error/warning，或这条只做提示。
- **把握度**：已确认。

### F14【低｜隐患】新游戏不清 VNAiMemory

- **位置**：VNScriptRunner.Flow.cs:411-421；全工程无 `VNAiMemory.Clear()` 调用者
- **触发**：同进程内先玩过 AI 聊天，再「开始游戏」。
- **后果**：她记得上一周目，且写进新存档。**当前不可达**：`_titleMenu.Open()` 只在启动时调用一次，没有返回标题入口。若试玩结束做「返回标题」，且之后完整版开 AI，就会触发。
- **修复方向**：`StartNewGame` 里同步 `VNAiMemory.Clear()`。
- **把握度**：已确认。

### F15【低】日记损坏后被覆盖

- **位置**：VNAiDiary.cs:57-71、73-85
- **触发**：`File.WriteAllText` 写到一半崩溃或断电，或文件被手改坏。
- **后果**：读取失败按空日记处理，下一条 `Add` 用空表覆盖原文件，历史日记全部丢失（可能还能手修的文件也没了）。
- **修复方向**：写临时文件后替换；解析失败时先改名备份。
- **把握度**：已确认。

### F16【低】三份人格同 id

- **位置**：Assets/Art/VNEffects/AiPersonas/星野结衣_Test.asset、星野结衣_日常.asset、Assets/星野结衣_BackUp.asset（id 相同）；VNAiTalkInstaller.cs:55-60、101；VNAiTalkModule.cs:677-678
- **触发**：重跑「AI 自由聊天」装机。按路径排序后 `_Test` 排最前，`module.personas` 和 config 全部登记。
- **后果**：`ResolvePersona` 返回第一个同 id 资产，即 `_Test`（5 轮），调 `_日常` 没有效果且无提示。当前场景只登记了 `_日常`，未中招。
- **修复方向**：改 id 或把备份移出扫描；装机时报重复 id。
- **把握度**：已确认。

### F17【低｜编辑器】试聊台「options 覆盖」只影响预览

- **位置**：VNAiStudioSession.cs:94、281；VNAiStudioWindow.cs:352-356、886
- **触发**：滑块设 options=5，开始试聊。
- **后果**：`new VNAiConversation(persona)` 没传 `optionCount`，实际请求仍按人格设定，右栏预览和日志 kwargs 却显示 5。
- **修复方向**：`Begin/Rebuild` 增加参数。
- **把握度**：已确认。

### F18【低｜编辑器】历史裁剪后回退失败却仍删轮次

- **位置**：VNAiConversation.cs:95-127；VNAiStudioSession.cs:235
- **触发**：`historyTurns=2`，聊 4 轮后「重跑本轮」。走查：此时 `_history=[P2,M2,P3,M3]`，查找第 3 条 model 消息找不到，`cut==Count` 返回 false。
- **后果**：`BranchFrom` 忽略返回值，界面轮次被删而历史没动，`TurnCount` 也没减；重发时模型还能看到被「删掉」的那句回复，下一条重复 P3。
- **修复方向**：按「裁掉的条数」偏移计算，或检查返回值。
- **把握度**：已确认。

### F19【低｜编辑器】试聊台日志成本错误

- **位置**：VNAiStudioLog.cs:58-66；VNAiStudioSession.cs:11-33；VNAiPricing.cs:152-172
- **触发**：结束试聊导出日志。
- **后果**：重建的 `VNAiResult` 没设 `model`，`Cost(null,…)` 取最贵档，DeepSeek 场次的 `costUsd` 被高估一个数量级以上；`VNAiStudioTurn` 没有缓存命中字段，花费报表的「重算」也把全部输入按未命中价算。
- **修复方向**：回填 model，给轮次记录加 `cachedTokens`。
- **把握度**：已确认。

### F20【低｜编辑器】记忆预设名字碰撞

- **位置**：VNAiStudioWindow.cs:520-525
- **触发**：已有「预设 1/2/3」，删除「预设 1」后点「＋」。
- **后果**：名字按「数量+1」得到「预设 3」，`Save` 无存在性检查，直接用空预设覆盖现有文件，本地记忆预设丢失（不进 git，无法恢复）。
- **修复方向**：循环找未占用的名字。
- **把握度**：已确认。

---

## 五、第三优先中检查过、未发现问题的部分

- **响应解析空引用**：`resp`/`choices`/`candidates`/`parts`/`message` 均有判空；JSON 异常有 catch；解析失败走兜底。
- **Temperature/费用失控**：`for` 循环有界、重试上限 2、无重试嵌套，人格滑块把 temperature 限制在 0~2。
- **JSON 拼接**：`Esc()` 处理控制字符，`Num()` 用 InvariantCulture。
- **Key 打印**：错误信息只含来源路径；Gemini 走请求头，不进 URL。
- **编辑器协程泵**：`EditorApplication.update` 在 Finish/Stop 都解绑，窗口 OnDisable 中止，域重载自然清除；未发现委托泄漏。
- **ESC 确认框与状态机**：确认框打开期间 TalkLoop 继续跑时 `_phase` 会被覆盖，走查未发现会导致卡死的组合。

---

## 六、过程说明

- 除本报告外，中途在 scratchpad 写过一个临时文件 `hist7.txt`（历史扫描结果，0 行），超出了「唯一允许写入」的范围，已删除。未碰项目内任何文件，未跑 git 写操作，未调用 Unity MCP，未打开 Key 文件。
- Key 相关检查只输出文件名、提交数或计数。

## 七、实际读完的文件

**完整读完（逐文件全文）**

Script/：VNAiClient.cs、VNAiClientDeepSeek.cs、VNAiClientGemini.cs、VNAiConversation.cs、VNAiDiary.cs、VNAiDiaryPanel.cs、VNAiKey.cs、VNAiMemory.cs、VNAiPersonaDef.cs、VNAiPricing.cs、VNAiProvider.cs、VNAiTalkLog.cs、VNAiTalkModule.cs、VNAiTextNormalize.cs（14 个）

Editor/：VNAiConnectionTester.cs、VNAiCostReport.cs、VNAiEditorCoroutine.cs、VNAiStudioDraft.cs、VNAiStudioLog.cs、VNAiStudioMemory.cs、VNAiStudioSession.cs、VNAiStudioWindow.cs、VNAiTalkInstaller.cs（9 个）

**部分读取**

VNScriptRunner.cs:165-186、VNScriptRunner.Commands.cs:310-459、VNScriptRunner.Flow.cs:360-509、VNScriptRunner.Input.cs:15-54 与 150-354、VNScriptRunner.SaveLoad.cs:150-214、VNStage.Scene.cs:1-90、VNEventModule.cs:40-96、VNDialogueBox.cs:625-650、VNLocale.cs:122-135、VNFogWipeModule.cs:503-514

**仅 Grep / 片段**：.gitignore（全文）、VNGameConfig.cs、VNGameConfig.asset、VNScriptDemo.unity、VNStatsHud.cs、VNQuestReward.cs、VNSaveSystem.cs、VNChoicePanel.cs、全部 `.vn.txt`、AiTalkGuide.md、ui.*.txt、MCP 包 Runtime、ProjectSettings

**未读**：VNTypewriterText.cs（只确认无 richText 设置）；.md 文档正文；两个 Key 文件（按要求未打开）。

---

# 审计第 8 组：编辑器工具（只读）

- 范围根：`D:\UnityAiProject\MyProject`，下文 `E/` = `Assets/Project/Scripts/VNEffects/Editor/`，`S/` = `Assets/Project/Scripts/VNEffects/Script/`
- 行号以恢复审计时磁盘为准（已重新核对关键锚点）。审计期间工作区被另一会话拆分：`VNScenarioEditorWindow`、`VNCamseqEditorWindow`、`VNScenarioLinter`、`VNTutorialEditorWindow`、`VNScriptRunner`、`VNStage`、`VNPhotoBoothModule` 都已是 partial；任务里写的 3761 / 3289 行单文件已不存在，`*Exporter.cs` 已被删除。
- 把握度：**已确认** = 沿代码路径走通，部分有现存文件佐证；**待验证** = 依赖 Unity 运行时行为，需实跑。
- 本次未改动项目内任何文件，未运行 Unity。

## 严重度计数

| 致命 | 高 | 中 | 低 | 合计 |
|---|---|---|---|---|
| 2 | 13 | 10 | 10 | 35 |

---

## 一、Lint 基线两类 Error 的判定

### `bad-mark-mode`：不是 Lint 误报，是真问题，但 Lint 的文案指错了方向

用户前提「行尾 @ 是合法异步语法」在这些行上不成立：这些行的行尾不是 `@`，而是 `@ # 注释`。

- `VNScriptParser.Parse`（`S/VNScriptParser.cs:123,148`）只在**整行以 `#` 开头**时当注释，只在**整行末字符是 `@`** 时当异步。DSL **不支持行内注释**。
- 所以 `mark 小雪 exclaim @   # 漫符：惊！弹一下就消失` 解析结果：`isAsync=false`，args = `[小雪, exclaim, @, #, 漫符：惊！弹一下就消失]`。Lint 复用同一解析器，`c.Arg(2)` = `@`，判定忠实反映运行时。
- 运行时实际行为（`S/VNScriptRunner.Dispatch.cs:98` → `VNStage.Mark`，`S/VNStage.Characters.cs`）：`mode` 不是 `keep/off` 就走一次性弹出，所以**符号照常显示，但 `@` 失效**，该命令变同步，脚本要等弹出补间播完才往下走。

| 行 | 第三参数 | 实际后果 |
|---|---|---|
| Demo 60、63、86；第1章 83、105、110 | `@` | 作者想要的异步丢失，变成同步等待弹出段 |
| Demo 92 | `#` | `mark 小雪 clear` 走 `ClearAll`，mode 被忽略，**行为无影响**；Lint 报 Error 偏重 |

补充：同样写法的 Demo 55（`quest start 告白大作战 # ...`）、Demo 67（`mark 小雪 heart keep # ...`）行为恰好正常，Lint 也没报，说明 Lint 只是撞上了 mark 的参数检查，并没有「行内注释」规则。另外注释里若出现半角 `key:value`，会被当 kwarg 吃进命令。详见第 21 条。

**修法需用户决定**：A. 把 8 行注释挪到单独一行（改动最小）；B. 让 Parser 支持命令行的行内注释（要同步 `VNScriptParser` 与 `VNScenarioDoc.Parse`，且台词行不能剥）。两者都建议加一条 Lint `inline-comment`。

### `bad-emote`：不是 Lint 误报，运行时也不认；根因在 `VNEmoteCatalog`

- 位置更正：两行在 **`Assets/Scenarios/Hina/第1章.vn.txt`** 第 95、196 行，不是根目录 `第1章.vn.txt`。根目录那份第 95 行是 `camseq`。报告看起来串文件是第 25 条的 UI 问题。
- `[VNEmote("颤抖", "害怕")]` 的 `label` 只进 `_labels` 给编辑器显示，**没有注册进 `_byKey`**（`VNEmoteCatalog.cs:119-120` 只注册方法名与 aliases）。所以 `TryGet("颤抖")`、`TryGet("点头")` 都失败。
- Lint（`E/VNScenarioLinter.Assets.cs:381-383`）与运行时 `VNStage.Emote`（`S/VNStage.Characters.cs`）调用同一个 `TryGet`，判定一致：运行时打一条 Warning，**动作不播**。
- Lint 提示文案「中文的是角色资产里的表情名」此处是错的，这两个词确实是 `[VNEmote]` 的中文名。

结论：Lint 判定正确，真 bug，详见第 14 条。修 Catalog 一行后这两条 Error 自动消失。

---

## 二、按严重度排列的问题清单

### 致命

#### 1. 致命｜`camcut`/`camto` 的「角色:部位」写法经编辑器往返后参数错位
- 位置：`E/VNScenarioDoc.cs:259-278`（`ParseCommand`）；对照 `S/VNScriptParser.cs:324-332`
- 触发：写 `camto 星野结衣:head 1.6 0.8 ease:OutSine`（手写，或在参数格选 custom… 填入后触发一次重新解析），在剧本编辑器打开，保存或 F5 自动保存。
- 后果：运行时 Parser 有 `camPointArg` 特判，编辑器没有。`星野结衣:head` 被当未知 key:value 进 `extraTokens`，后面的 `1.6`、`0.8` 依次落到 point、zoom。生成结果变成 `camto 1.6 0.8 ease:OutSine 星野结衣:head`：目标点变数字，真正的目标点被运行时解析成 kwarg 丢弃。域重载也会触发同样的重解析（走 `_docText`）。
- 现状：当前 `Assets/Scenarios` 无此写法（grep 0 处），但这是文档与运行时明确支持的语法，属于潜伏的静默改写。
- 修复：`ParseCommand` 同步运行时规则：`camto/camcut` 的 t==1 token 一律当位置参数。三个特判（`qualifiedAddress`、`camPointArg`、`snsFreeText`）建议抽成共用判定，避免再漂移。
- 把握度：已确认

#### 2. 致命｜镜头编排窗口在无用户操作时把路径点角色改成列表第一个，并实时写回剧本
- 位置：`E/VNCamseqEditorWindow.Gui.cs:309-314`，回写 `:95`
- 触发：镜头窗口开着且「实时回写」开（默认），在剧本编辑器选中一个 camseq 行，其路径点角色 id 不在 `CharacterIds()` 里（比如 `> 小雪:head`。`VNGameConfig.characters` 只登记了 7 个资产，小雪、亚里沙不在其中；或写 `${角色}:head`、或角色尚未登记）。
- 后果：`IndexOf` 得 -1 → `Mathf.Max(0, cur)` → Popup 返回 0 → `w.charId = ids[0]` 每帧无条件赋值。`GenerateText()` 与 `_lastAppliedText` 不同，`OnGUI` 末尾 `ApplyToLink()` 把该行整段改写成第一个角色，**只因选中了这一行**。剧本编辑器标脏，下次保存或 F5 落盘。
- 现状：现存 `>` 行角色都是星野结衣（已登记），未中招。
- 修复：`cur < 0` 时退回文本框保留原值，或只在 Popup 发生改变时才写 `w.charId`。
- 把握度：已确认

### 高

#### 3. 高｜每次「文本→rows」都多一个空行，文件末尾空行持续累积
- 位置：`E/VNScenarioDoc.cs:112-115`（Split 不去尾）、`:375-377,393`（每行补 `\n`）
- 触发：`GenerateText()` 输出必以 `\n` 结尾，`Parse` 的 `Split('\n')` 多切出一个空串，变成一个 Raw 空行。发生在：LoadFile、每次域重载（`RestoreAfterDomainReload` 走 `_docText`）、每次 Ctrl+Z/Y（`LoadFromText`）。
- 后果：`Parse(GenerateText(doc))` 恒多一行，保存后写进文件。**现存证据**：文件末尾空行数 `第1章` 138 行、`InteractionDemo` 31、`BadmintonDemo` 22、`AiTalkDemo` 15、`Chapter1` 8、`SnsDemo` 5。
- 修复：`Parse` 对末尾因换行产生的空串跳过一次（文本以 `\n` 结尾时去掉最后一段）。存量文件的尾部空行需单独清理。
- 把握度：已确认

#### 4. 高｜打开文件立刻是脏状态，F5 不改一字也会重写文件；CRLF、BOM 全部被改
- 位置：`E/VNScenarioEditorWindow.Data.cs:286,291`（先 `_dirty=false` 再 `Bump()` 置回 true）、`:304`；`E/VNScenarioEditorWindow.EditTab.cs:320-324`；搜索框 `:91`、页签按钮 `Gui.cs` 未隔离 `GUI.changed`
- 触发：打开任一 CRLF 剧本（现存绝大多数），不改内容直接 F5 / ▶，或点 Save。
- 后果：`AutoSaveBeforePlay` 因 `_dirty` 为真调用 `SaveFile`，文件被规范化重写：LF 替换 CRLF、BOM 被去掉（Demo、Chapter2 带 BOM）、空白折叠、再加第 3 条的尾部空行，造成整文件 diff。佐证：`第1章`、`Hina/第1章`、`PhotoDemo` 已是 LF，其余仍是 CRLF，仓库无 `.gitattributes`。连带后果：`CheckExternalChange` 的「未改动→静默重载」分支基本走不到；关闭、切换时总弹「未保存」确认，使真正的提醒失去意义；Toolbar 的 Save 按钮本身若置 `GUI.changed`（代码里 `DrawAudioPreviewButton` 的注释说明确实如此），保存后 `OnGUI` 末尾又 `Bump()`，`*` 清不掉（此点待验证）。
- 修复：`LoadFile` 用 `_version++` 不置脏；搜索框、页签包 `previousChanged`；保存时保留原文件换行风格（记录 CRLF/LF），内容与磁盘相同则不写。
- 把握度：已确认（Save 按钮残留 `*` 一点待验证）

#### 5. 高｜镜头窗口不感知剧本侧改动，下一次编辑用陈旧内容覆盖；行号绑定还会写错行、串文件
- 位置：`E/VNCamseqEditorWindow.Data.cs:89-90,213-223`，`:351`（`DocVersion` 全工程只此一处使用，只用于舞台缓存）；`E/VNScenarioEditorWindow.Camseq.cs:395-411`
- 触发（任一）：
  a. 绑定 camseq 行后，在剧本编辑器里改该行路径点 / Ctrl+Z / Reload，再回镜头窗口动任何参数；
  b. 在绑定行上方增删行，使该索引变成另一个 camseq 行；
  c. 在剧本编辑器打开另一个文件，同索引恰是 camseq。
- 后果：`ApplyToLink` 只比较窗口自身文本与 `_lastAppliedText`，不看剧本行是否已变，于是 a 中剧本侧改动被整段覆盖，b、c 中**另一段运镜被静默改写**。`_linkedRow` 是裸 int，未跟踪行身份。
- 修复：绑定时记录行内容或行对象；每帧比较 `TryGetCamseqText` 与 `_lastAppliedText`，剧本侧变了就重载或提示；`DocVersion` 变化时校验；换文件时解绑。
- 把握度：已确认

#### 6. 高｜镜头窗口回写时丢弃它不认识的路径点内容，与「解析不了原样保留」的约定相反
- 位置：`E/VNCamseqEditorWindow.Text.cs:409`（未知部位归 0）、`:414-420`（未知 ease 丢弃）、`:289`（坐标只留 1 位小数）、`:325-331`；写回 `E/VNScenarioEditorWindow.Camseq.cs:405-407`
- 触发：剧本里有 `> 星野结衣:face 1.6 ease:OutQuart`，或剧本编辑器里标黄的纯文本路径点，或 `> 34.56,-269`；在镜头窗口改任意一个点。
- 后果：窗口经 `VNScriptParser` 转成自己的结构，未知 token 直接没了，再用 `GenerateText` **整段替换** `camLines`：部位与 ease 丢失、坐标被取整、`> middle` 被撑成 `> middle 1 0.8`。`start:fade` 以外写的 `startfade:` / 无 `end:fade` 的 `endfade:` 也被丢。
- 修复：窗口载入时用 `VNCamWaypoint.TryParse` 判定，有解析不了的行就只读或拒绝回写；Waypoint 保留原始 token；只替换实际改动的行。
- 把握度：已确认

#### 7. 高｜`jump 文件::标签` 在剧本编辑器被判语法错误，第2章、第3章无法从编辑器调试播放
- 位置：`E/VNScenarioDoc.cs:263-272`（无 `qualifiedAddress` 判定）、`:739`；拦截 `E/VNScenarioEditorWindow.EditTab.cs:278-285`
- 触发：打开 `第2章.vn.txt`（第 269 行 `jump 主循环::行动菜单`）或 `第3章.vn.txt`（第 225 行 `jump 结局集::结局判定`），按 F5。
- 后果：token 被当未知 kwarg 进 `extraTokens`，`label` 为空 → Err「jump needs a target label」。`PlayFromSourceLine` 只要有 Error 就弹「无法开始调试」，**整个文件都不能从任何行播放**。参数格显示空，选一个目标后写成 `jump 新标签 主循环::行动菜单`。文本往返不丢内容。
- 修复：同第 1 条，`ParseCommand` 增加 `qualifiedAddress` 判定。
- 把握度：已确认

#### 8. 高｜保存不复查磁盘时间戳，外部改动可被静默覆盖
- 位置：`E/VNScenarioEditorWindow.Data.cs:294-316`，`:318-328`；`E/VNScenarioEditorWindow.EditTab.cs:320-324`；`E/VNScenarioEditorWindow.Shortcuts.cs:19-22`
- 触发：编辑器窗口开着，AI 或 VS Code 改了同一 `.vn.txt`，窗口没有经历 `OnFocus`（比如窗口一直持有焦点），之后 Ctrl+S 或 F5。
- 后果：外部检测只在 `OnFocus` 跑。`SaveFile` 无条件写，`AutoSaveBeforePlay` 的 `_externalChanged` 守卫此时也是 false，外部改动被旧内容覆盖。即便已显示黄条，Ctrl+S 也不提示，直接覆盖并清掉标记。本项目 AI 协作频繁改剧本，这条路径很现实。
- 修复：`SaveFile` 写前比较 `File.GetLastWriteTimeUtc`，不一致时弹确认；F5 自动保存同样处理。
- 把握度：已确认（`SaveFile` 无检查）；切回 Unity 时 `OnFocus` 是否触发需实跑

#### 9. 高｜打开新文件后撤销栈可能混入旧文件全文，Ctrl+Z + 保存会用 A 的内容覆盖 B
- 位置：`E/VNScenarioEditorWindow.Gui.cs:26-30,70-79`；`E/VNScenarioEditorWindow.Data.cs:288-291`
- 触发：打开 A，超过 1 秒未编辑，点工具栏 Open 选 B（或 Reload）。
- 后果：`LoadFile` 清空撤销栈后，同一个 `OnGUI` 继续执行到末尾；若 Button 置了 `GUI.changed`，就会 `PushUndo(_frameSnapshot)`，而 `_frameSnapshot` 仍是本帧 Layout 时 A 的全文。之后在 B 里 Ctrl+Z，文档内容变成 A，`_path` 仍是 B，保存即用 A 覆盖 B。
- 修复：`LoadFile` 里同步刷新 `_frameSnapshot` 并 `GUI.changed=false`，或调用后 `GUIUtility.ExitGUI()`。
- 把握度：待验证（代码路径已走通；依赖 Button 置 `GUI.changed`，项目注释佐证此行为）

#### 10. 高｜重扫素材目录：只要 `Assets/CG` 存在就清空其余全部 CG 登记
- 位置：`E/VNGameConfigTools.cs:253,260-276`；误导提示 `E/VNScenarioLinter.Assets.cs:137`（及 `Events.cs` sns image 分支、CLAUDE.md）；`E/VNEffectsDemoSetup.cs:1299`
- 触发：照 Lint 提示「把图放进 Assets/CG 后跑 Rescan Asset Folders」，或目录被 `FillCgLibrary` 建出。当前 `Assets/CG` 不存在，CG 实际在 `Assets/Art/CG`、`Assets/Art/Images/CG`，配置登记 16 条。
- 后果：`config.cgLibrary = list`，list 只含 `Assets/CG` 下文件，其余条目与自定义 id 全部消失；目录为空时清成 0 条。反过来目录不存在时报告「CG ×0」，新图也不会入库。
- 修复：改成增量合并（只加不删），扫描目录从现有条目反推（素材浏览器已有此做法），并更新过时提示。
- 把握度：已确认

#### 11. 高｜重建剧本演示场景丢掉 5 个事件模块
- 位置：`E/VNEffectsDemoSetup.cs:562-645`
- 触发：执行 Create Script Demo Scene。
- 后果：只注册 qte/map/shop/plan/result/battle/quiz/badminton 8 个，缺 **aitalk、photo、wipefog、interact、questboard**。这些靠各 Installer 增量装入，重建后消失，且 `VNGameConfig` 没有恢复它们的途径（interact 连配置字段都没有）。CLAUDE.md 又写着「每次加新组件后需重建」。
- 修复：生成器重建后调用各 Installer 的无弹窗入口，或注册表改为自动发现模块。
- 把握度：已确认

#### 12. 高｜生成器的场景路径已过时，新场景替换当前场景后保存失败却打印成功日志
- 位置：`E/VNEffectsDemoSetup.cs:30-31,440,694`
- 触发：执行任一重建菜单。`Assets/Scenes` 目录已不存在，真实场景是 `Assets/Project/Scenes/VNScriptDemo.unity`（Build Settings 同此）。
- 后果：`SaveScene` 返回值未检查；目录不存在时保存失败，但日志仍写「已保存到」，`PingObject(null)`，用户手上是一个未命名场景，磁盘上却多了第 13 条的重复资产。关于「是否覆盖手工改动」：**现在真实场景因路径过时而侥幸未被覆盖**；若把路径修正，会变成无确认直接覆盖手工场景，并连带第 11 条。修路径时必须加确认框。
- 修复：路径改为常量指向真实目录；检查返回值；目标存在时二次确认或另存时间戳副本。
- 把握度：路径过时已确认；`SaveScene` 在目录缺失时的具体表现待验证

#### 13. 高｜素材目录整理后，重建与部分 Installer 按旧路径生成重复定义资产
- 位置：`E/VNEffectsDemoSetup.cs:22-32`，`:469-473`、`:597`、`:609`、`:652`、`:658`；`:1050-1066`；`:1380-1386`
- 触发：执行重建。`Assets/VNEffects` 下已无 Characters/Quests/Shops/Stats/Plans，这些已搬到 `Assets/Art/VNEffects`；`LoadAssetAtPath` 按旧路径找不到，于是新建。
- 后果：在 `Assets/VNEffects/...` 重新造出 亚里沙、小雪、8 个属性、服装店、周日程、告白大作战、VolumeProfile、VNImageEffect 等，id 与现有资产相同。随后 `FindAllAssets` 把两份都登记进模块；`RefreshSources` 的 `_ctx.expressions[def.id]` 后者覆盖前者，表情候选可能被只含「默认」的新资产替换，产生假警告；`Rescan` 也会把重复资产写进配置。`Assets/VNEffects/CamseqPresets.asset` 与 `Assets/Art/VNEffects/CamseqPresets.asset` 已是分叉的两份（1234 字节与 599 字节）。
- 修复：按类型 + id 全局查找后再决定是否新建，目录取现有同类资产所在位置（`VNSecretPhotoInstaller.DirOfExisting` 已有现成做法）。
- 把握度：已确认

#### 14. 高｜`[VNEmote]` 中文名没注册进目录，剧本与互动资产里的中文动作全部静默失效
- 位置：`Assets/Project/Scripts/VNEffects/VNEmoteCatalog.cs:119-120`；受影响 `S/VNInteractionModule.cs:472-479`、`S/VNStage.Characters.cs`、`S/VNSecretPhotoMode.cs:408`
- 触发：`emote 姫奈 颤抖`，或互动反馈 `emote: 点头`。
- 后果：`Hina/第1章` 第 95、196 行不播。`Assets/Art/VNEffects/Interactions` 里 4 个反馈（害羞、摇头、点头、生气）同样失效，`VNInteractionInstaller.cs:229-257` 的示例数据也写的是中文；`VNInteractionModule.cs:476` 注释声称中文由 Catalog 解析，与实现不符。运行时只有一条 Warning。
- 修复：`Ensure()` 里补 `Register(byKey, entry.label, entry)`。label 与现有别名无冲突（`害怕`、`shake` 不同名）。
- 把握度：已确认

#### 15. 高｜本地化抽取跑第二次就永久丢弃孤儿译文
- 位置：`E/VNLocalizationTools.cs:144-146,170-183`；`S/VNLocale.cs:121-127`
- 触发：改一句中文后跑 Extract，旧译文被写成 `# key = 译文` 注释（文件称「供人工挪用」）；再跑一次 Extract。
- 后果：`ParseTable` 跳过 `#` 行，第二次 `existing` 里没有这些孤儿，`MergeWrite` 整文件重写，孤儿区消失，人工翻译丢失（只能从 git 找回）。
- 修复：`MergeWrite` 读取时解析旧孤儿区并原样带上。
- 把握度：已确认

### 中

#### 16. 中｜`Validate` 不认 `${参数}`，含参数的子程序文件无法调试播放
- 位置：`E/VNScenarioDoc.cs:636-728`（无 Dynamic 判断，Linter 有 `Dynamic()`）；拦截 `EditTab.cs:278-285`
- 触发：打开 `公共片段.vn.txt`（第 85 行 `bg ${背景} transition:${转场}`、第 155 行 `jump 日常一句_${语气}`），F5。
- 后果：Err「background not registered」「label does not exist」，整个文件被拦，无法播放。
- 修复：`Validate` 入口跳过含 `${` 的值，与 Linter 一致。
- 把握度：已确认

#### 17. 中｜被禁用（`#!`）的行仍参与校验和 label 收集
- 位置：`E/VNScenarioDoc.cs:480-490,573-575`
- 触发：a. 把报错的行 Ctrl+/ 禁用，想先跑后面；b. 禁用一个 `label`，别处仍 `jump` 它。
- 后果：a 错误不消失，F5 仍被拦，禁用失去调试意义；b 编辑器显示正常，运行时跳转失败（Parser 跳过 `#` 行）；禁用行还会触发 label 重名误报。
- 修复：`Validate`、`CollectLabels`、`CollectFlags` 跳过 `disabled` 行。
- 把握度：已确认

#### 18. 中｜格式不全的 `if` 行每次往返都被改写并增长
- 位置：`E/VNScenarioDoc.cs:203-224,401-405`
- 触发：`if a>1 goto x`，或新增一条还没填完的 `if` 行后经历撤销或域重载。
- 后果：无 jump 时全部 token 进 `extraTokens`，生成 `if  jump  a>1 goto x`；再解析时 `tokens[1]` 的 `jump` 不被识别（循环 `t>=2`），又进 extras，得到 `if  jump  jump a>1 goto x`，每轮多一个 `jump`。extras 在 UI 无控件，看不见。
- 修复：解析失败时把 if 行存为 Raw 原样保留，或生成时 condition 与 target 都空就只输出 extras。
- 把握度：已确认

#### 19. 中｜关闭窗口或退出 Unity 时未保存改动静默丢失
- 位置：`E/VNScenarioEditorWindow.cs:293-297`（全工程 grep 无 `hasUnsavedChanges` / `saveChangesMessage`）
- 触发：有未保存改动，点窗口 ✕ 或退出 Unity。
- 后果：`_doc` 只在域重载时存活，关闭即丢，无提示。`_dirty` 恒真（第 4 条）时先修那条再启用提示。
- 修复：`hasUnsavedChanges = _dirty` + `saveChangesMessage` + 实现 `SaveChanges()`。
- 把握度：已确认

#### 20. 中｜镜头「场景预览」开着时保存场景，预览状态写进场景文件
- 位置：`E/VNCamseqEditorWindow.Preview.cs:28-41,80,166-167`（全工程无 `sceneSaving` 钩子）
- 触发：开场景预览，拖到某时刻，Ctrl+S 保存场景，随后关预览并不再保存。
- 后果：ZoomRoot 的缩放、位移和背景 sprite 进入场景文件；内存被还原，但磁盘保留预览态。临时立绘有 `DontSave`，这一点没问题；背景与 ZoomRoot 没有防护。其余预览窗口（Weather、CharacterVisual、FogTune）扫描未见往场景写对象。
- 修复：订阅 `EditorSceneManager.sceneSaving` 临时还原，`sceneSaved` 后恢复。
- 把握度：待验证（Ctrl+S 是否写入取决于场景脏标记；窗口帮助文本称会显示未保存标记）

#### 21. 中｜DSL 不支持行内注释，但剧本里在用，且只有 mark 被 Lint 抓到
- 位置：`S/VNScriptParser.cs:123,148`；`E/VNScenarioLinter.Assets.cs:230-235`；数据 `Assets/Scenarios/Demo.vn.txt:55,60,63,67,86,92`、`第1章.vn.txt:83,105,110`
- 触发：命令行尾写 ` # 注释`。
- 后果：`@` 失效、注释变位置参数、注释里的 `a:b` 变 kwarg；台词行则整句显示给玩家。提示「第三个参数只能是 keep」不指向真实原因。
- 修复：见第一节选项；加通用规则：命令 args 含独立 `#` token 即报 `inline-comment`。
- 把握度：已确认

#### 22. 中｜Linter 覆盖缺口（Runner 支持、拼错后静默退化）
- 位置：`E/VNScenarioLinter.Assets.cs:123-400`、`Events.cs:59-286`、`Flow.cs`
- 触发与后果（均已逐项对照 `S/VNScriptRunner.Dispatch.cs`）：
  - `if` 条件与选项 `if:` 的表达式语法未检查，`VNExpression.TryValidate` 只用于任务资产；运行时 `VNFlags.Evaluate` 判 false，分支静默走不进去。
  - `bg/cg` 的 `transition:`、`transition` 命令、`mood` 枚举未检查，拼错回退默认。
  - `shake` 档位未检查，拼错**连 Warning 都没有**，静默成 Medium（`Dispatch.cs:182-189`）。
  - `fx` 名、`quest` 子命令、`show at:` / `move` 站位、`camcut/camto/camseq` 的点位和 ease、`bgm` 子命令未检查。
  - **kwarg 拼写完全不检查**：`show x att:left`、`event quiz cnt:3` 都静默无效。`VNScenarioSchema.cs:146-147` 注释说 Lint 不再报未知 token，但 Linter 根本未引用 Schema。
  - `interact`、`shop`、`plan` 的 `id:` 未校验；`BuiltinOutcomes` 无 `interact`，`* 拒绝` 必须接住却无规则。
- 修复：用 Schema 做通用 kwarg 与枚举校验，补 `TryValidate` 与 interact 规则。
- 把握度：已确认
- 一致性结论：命令表 Parser 49 个关键字与 Schema 完全一致；`EventVariants` 与 13 个模块 `OnLaunch` 的 `ctx.Kw`（含 `StatOrKw` / `StatOf` 包装）逐项一致，**未发现漂移**；`BuiltinOutcomes` 与已核对模块的 `Done(...)` 一致。

#### 23. 中｜本地化工具不扫子目录，`Hina/` 剧本从不进翻译表
- 位置：`E/VNLocalizationTools.cs:105`
- 触发：跑 Extract / Validate。
- 后果：`Directory.GetFiles` 默认只扫顶层，`Hina/第1章` 被漏掉，Validate 还显示其余全部翻译完成。改递归时要处理与根目录 `第1章` 的同名冲突。
- 修复：`SearchOption.AllDirectories` 并给出重名方案。
- 把握度：已确认

#### 24. 中｜Inspector 批量拖音频到空库，基准音量为 0
- 位置：`E/VNGameConfigEditor.cs:458-464,533-563`；对照 `E/VNAssetBrowserWindow.cs:860-863`（素材浏览器已有补 1 的修正）
- 触发：在 VNGameConfig Inspector 向空的音频库批量拖入，或点 + 新增。
- 后果：`SerializedProperty` 扩容不跑 `volume = 1f` 初始化，新条目音量 0，全部静音；非空库则继承上一条标定值。
- 修复：与素材浏览器一样补默认音量。
- 把握度：待验证

#### 25. 中｜同名剧本在 Lint 窗口被合并显示，编辑器 label 索引后者覆盖前者
- 位置：`E/VNScenarioLinterWindow.cs:69-74`；`E/VNScenarioLinter.cs:203`；`E/VNScenarioEditorWindow.Data.cs:194-195`
- 触发：`Assets/Scenarios/第1章.vn.txt` 与 `Hina/第1章.vn.txt` 并存。
- 后果：分组键只用文件名，两份的问题按行号混在同一标题下，这正是第一节把 Hina 第 95、196 行误认成根目录的原因（「打开」按钮用 assetPath，能开对）。`_ctx.scenarioLabels[normalized]` 取决于 `FindAssets` 顺序，`第1章::x` 校验可能对错文件。
- 修复：分组与显示用 assetPath；索引检测到重名时给 Issue。
- 把握度：已确认

### 低

#### 26. 低｜台词左侧第三个词和 `#!` 开头的普通注释会被改写
- 位置：`E/VNScenarioDoc.cs:122-130,310-313,381-384`
- 触发：`他 说 了: xxx`；或普通注释 `#! 这里 要改: 说明`、`#!说明`。
- 后果：第三个词保存后消失（运行时本来也忽略）；`#!` 注释被当禁用行解析，无冒号时变成 `#! : 说明`。现存 6 处 `#!` 都是真禁用行，未受影响。
- 修复：多词左侧退回 Raw；`#!` 解析失败退回 Raw。
- 把握度：已确认

#### 27. 低｜块内夹注释或空行时，注释被挪到块后
- 位置：`E/VNScenarioDoc.cs:132-152`
- 触发：`choice` / `camseq` 与 `*` / `>` 行之间夹 `#` 或空行。
- 后果：内容不丢，位置变动（Raw 行留在 rows，子行归入主行）。现存剧本扫描未发现此形态。
- 修复：解析时把子行前的 Raw 收进块内并记录顺序。
- 把握度：已确认

#### 28. 低｜选项上写两个 `flag:` 时保存只留一个
- 位置：`E/VNScenarioDoc.cs:333`
- 触发：`* A flag:a+1 flag:b+1`。
- 后果：运行时也只生效最左一个，但另一个 token 在保存时消失，作者看不到提示。现存 0 处。
- 修复：Lint 报错。
- 把握度：已确认

#### 29. 低｜中间位置参数留空时被补成 `0`
- 位置：`E/VNScenarioDoc.cs:418-424`
- 触发：`hideHUD` 第一格空、第二格选 `keep`；`se`、`sns` 同理。
- 后果：写出 `hideHUD 0 keep`，运行时 `S/VNScriptRunner.Input.cs:89-91` 报未知部件警告。Schema 注释声称留空不错位。
- 修复：`hideHUD` 这类按 token 分类的命令生成时跳过空位。
- 把握度：已确认

#### 30. 低｜Linter 控制流两处未识别 `sns reply`
- 位置：`E/VNScenarioLinter.Flow.cs:252-254`、`:374-381`
- 触发：子程序里 `sns reply` 所有回复都跳走；label 只被 `late:` 引用。
- 后果：`missing-return` 可能误报；`unreferenced-label` 误报（Info 级）。
- 修复：把 `sns` 归入 choice 分支，`Mark` 补 `Kw("late")`。
- 把握度：已确认

#### 31. 低｜冷启动请求未发生时 Pending 残留
- 位置：`E/VNScenarioEditorWindow.cs:451-475`
- 触发：F5 时因编译错误未能进入 Play。
- 后果：`PendingKey` 留在 SessionState，下次手动 Play 被劫持到旧文本、旧行。
- 修复：`EnteredEditMode` 时清请求，或 Request 记录时间戳过期作废。
- 把握度：待验证

#### 32. 低｜隐藏或被过滤的行被一并选中和删除
- 位置：`E/VNScenarioEditorWindow.EditTab.cs:359-368`（`SelectedRowIndices`）、`E/VNScenarioEditorWindow.cs:334-345`
- 触发：开「隐注释/空行」或搜索过滤后 Shift 连选，点 [-]。
- 后果：零高度的注释行、不匹配行一起删除，看不见（可撤销）。
- 修复：删除与拖动前过滤 `IsRowCollapsed`，或弹确认。
- 把握度：待验证

#### 33. 低｜Installer 用全量扫描覆盖配置列表
- 位置：`VNQuizInstaller.cs:96`、`VNBadmintonInstaller.cs:97`、`VNAiTalkInstaller.cs:111`、`VNFogWipeInstaller.cs:95`、`VNQuestBoardInstaller.cs:101`、`VNPhotoBoothInstaller.cs:120-123`
- 触发：手工从配置移除或排序后重跑 Installer。
- 后果：手工调整被还原。**重复执行不会重复添加模板或条目**，幂等性本身没问题（都按 `entry.template` 判断）。
- 修复：只追加缺失项。
- 把握度：已确认

#### 34. 低｜`FindFirstObjectByType` 过时 API 影响面
- 统计：共 **107 处 / 37 文件**。Editor 44 处 / 19 文件，运行时 63 处 / 18 文件，`Assets/Editor` 与第三方插件 0 处。最多：`S/VNStage.cs` 31、`E/VNGameConfigTools.cs` 10、`S/VNScriptRunner.cs` 8。
- 后果：未来升级变 error 时编译中断。`VNInteractionInstaller.cs:42` 已改用 `FindAnyObjectByType`。
- 修复：统一替换，带 `FindObjectsInactive` 的重载可直接对应。场景里若有多实例依赖「第一个」的语义需逐处确认。
- 把握度：已确认

#### 35. 低｜命令面板（Ctrl+E）不提供模块专属参数
- 位置：`E/VNCommandSearch.cs:497`
- 触发：Ctrl+E 新建 `event badminton`。
- 后果：`_def` 取基础定义，可选参数只有 `tutorial`。
- 修复：选完 module 后用 `Find("event", module)` 刷新定义。
- 把握度：已确认

---

## 三、原任务检查项结论（未列为问题的部分）

| 检查项 | 结论 |
|---|---|
| 注释、空行 | 保留，有 Trim 规范化；问题见 3、27 |
| 未知命令 | 不在关键字表的行按台词解析，与运行时一致 |
| 未知 token | 进 `extraTokens` 原样带回 |
| CRLF / BOM | 读取不出错，保存被改写，见 4 |
| 含引号参数 | 运行时不支持引号，按空格切；往返 token 集合不变，**语义不变，不算 bug** |
| event 第二行 | 模块专属 kwarg、换模块保留值均正常，存储键 `module` 无冲突 |
| camseq 块（文本往返） | `camLines` 原样保留，安全；风险在镜头窗口回写，见 5、6 |
| Installer 重复执行 | 不重复添加，见 33 |
| 预览临时对象 | 立绘 `DontSave` 有效；背景与 ZoomRoot 见 20 |
| Undo 串行 | 两窗口各自独立；串文件风险见 9，陈旧覆盖见 5 |
| 静态事件 / EditorApplication 回调 | 扫描到的订阅都成对退订（`VNAssetLibraryEvents`、`PollAudioPreview`、playModeStateChanged、update），未发现泄漏 |
| 域重载状态 | 主要状态走 `_docText` 存活，副作用见 3 |

---

## 四、阅读范围

**完整读完（当前拆分后的文件）**
- `E/`：VNScenarioDoc、VNScenarioSchema、VNScenarioLinter（含 .Assets/.Events/.Flow/.Quests）、VNScenarioLinterWindow、VNScenarioEditorWindow（含 .Camseq/.Data/.EditTab/.Gui/.Shortcuts/.TextTab）、VNCommandSearch、VNCamWaypoint、VNCamseqEditorWindow（含 .Canvas/.Data/.Gui/.Preview/.Text/.Timeline）、VNTextureImportDefaults、VNGameConfigEditor、VNGameConfigTools、VNLocalizationTools、VNAssetBrowserWindow、VNEffectsDemoSetup
- 全部 9 个 Installer：Liquid、Quiz、Badminton、AiTalk、FogWipe、SecretPhoto、QuestBoard、Interaction、PhotoBooth
- `Assets/Editor/`：GameViewFullscreen、QuickAccessWindow（未发现够格上报的问题）
- 对照阅读：`S/VNScriptParser.cs`、`S/VNScriptRunner.Dispatch.cs`、`VNEmoteCatalog.cs`

**只做 grep 级快速扫描（事件订阅、预览对象、场景写入）**
VNAiConnectionTester、VNAiCostReport、VNAiEditorCoroutine、VNAiStudioDraft、VNAiStudioLog、VNAiStudioMemory、VNAiStudioSession、VNAiStudioWindow、VNAssetLibraryEvents、VNAssetTheme、VNAssetUi、VNCharacterVisualPreviewWindow、VNConfigEntryDrawers、VNFogTuneWindow、VNFontAssetBuilder、VNTextPromptWindow、VNTouchZoneEditorWindow、VNTutorialAnchorCatalog、VNTutorialBackdrop、VNTutorialDefEditor、VNTutorialEditorWindow（含 .Canvas/.Gui/.Properties）、VNTutorialSamples、VNWeatherPreviewWindow

**未读**
- `*Exporter.cs`：已从工作区删除，无法审计（`VNSoftSkinExporter`、`VNSystemUiSkinExporter`、`VNUiSkinExporter`）
- `S/VNStage*.cs`、`S/VNScriptRunner.Flow/Commands/SaveLoad/Input` 只读了与审计相关的片段；VNPhotoBoothModule 拆分后的新文件未读（`ctx.Kw` 一致性结论基于拆分前的 grep，`stat/rate/flag` 等键需在新文件上复核一次）

---

# 第 9 组审计：打包、配置、剧本、本地化、合规、文档漂移

- 目标：itch 免费公开试玩，Windows 桌面版，不开放 AI 聊天
- 基准：当前工作区，分支 `feature/code-slimming`，行号以 2026-11-01 重新核对的磁盘为准
- 方法：全程静态只读。没有实际 Build，也没有进 Play Mode，项目里没有 Builds 目录
- 把握度：「已确认」指静态证据闭合；「待验证」指需要实机 Build 或人工判断

## 严重度计数

| 致命 | 高 | 中 | 低 |
|---|---:|---:|---:|
| 4 | 8 | 8 | 12 |

---

## 致命

### F1. 缺 shader 使立绘全程不可见，首个选项流程中断

- **位置**：`Assets/Project/Scripts/VNEffects/VNImageEffectController.cs:153`、`VNDialogueBox.cs:314`、`VNChoicePanel.cs:392`、`VNEntranceAnimator.cs:125`、`Script/VNStage.Characters.cs:94,355`、`Script/VNScriptRunner.Dispatch.cs:33-39`、`Script/VNScriptRunner.Commands.cs:302`
- **进包路径复核**（第 3 组已确认缺失，这里只查其他路径）：6 个 shader 没有别的进包路径。
  - `m_AlwaysIncludedShaders` 只有 7 个内置项（`ProjectSettings/GraphicsSettings.asset:29-36`）
  - RetroFilter、Shockwave、DirectBackgroundTransition、FogWipe、TutorialMask 没有被任何 .mat、.unity、.prefab、.asset 引用
  - VN/ImageEffect 只被 `Assets/Art/VNEffects/Materials/VNImageEffect.mat` 引用，而这个材质的引用数为 0
  - 没有 `.shadervariants`，`preloadedAssets` 为空，没有 Build 处理器（`IPreprocessBuild`、`IProcessScene`、`BuildPlayerProcessor`）
  - 两个 Resources 目录里没有 shader，也没有 UsePass 或 Fallback 的间接引用
- **后果链**（Shader.Find 返回 null 后，`Mat` 也是 null）：
  1. 启动时 `VNDialogueBox.Awake` → `Bind` → `SetShineStyle` 抛 NRE，`_animRect`（行 320）没有赋值。第 1 章能恢复，只是因为 `第1章:46` 的 `ui dialogue 黑渐变` 换成了 shineFrame 为空的皮肤。删掉这行测试残留，对话框就会在 `Show()` 抛 NRE。
  2. `show` 会先把 `_group.alpha` 设成 0，再调用 `SetDissolve` 抛 NRE。Dispatch 不是迭代器，异常被 `try/catch` 吞掉只记一条日志，所以女主永远透明（`第1章:38` 起每次 show 都如此）。
  3. 第一个 `choice`（`第1章:131`）在 `FinishButton` 抛 NRE，位于 `ChoiceCo` 协程内部，没有 try/catch。此后要么卡死，要么跳过选择（待 Build 确认是哪种），玩家都走不下去。
- **修复方向**：把 6 个 shader 加入 Always Included，或把引用它们的材质放进 Resources。这几个 shader 用的是 `multi_compile_local`，没有 `shader_feature`，不必另保变体。再给 `VNImageEffectController` 所有直接写 `Mat.` 的地方加空保护。修完必须实机 Build，从头跑到第一个选项。
- **把握度**：进包路径已确认；运行时后果待实机验证。

### F2. 明确写成高二学生的角色，与亲密互动数据和偷拍功能同时进包

- **未成年设定的证据**：`第1章.vn.txt:49`（升上高二）、`:89`（制服、校规）、`好感事件_星野结衣.vn.txt:190`（高中生）、`结局集.vn.txt:74`、`Assets/Art/VNEffects/AiPersonas/星野结衣_日常.asset:17,19`。所有角色资产都没有年龄字段，也没有成年声明。
- **进包的亲密内容**：
  - `VNScriptDemo.unity:15555` 的 `interactions` 列表，把 `Assets/Art/VNEffects/Interactions/初次抚摸.asset` 和 `Assets/Art/InteractionMiniGame/item1~4.png` 带进包（依赖闭包已确认）
  - `Assets/Art/VNEffects/TouchZones/星野结衣.asset` 同样进包
  - `星野结衣.asset` 登记了痕迹（imprints）条目
  - `InteractionDemo.vn.txt:22` 把这套数据绑定到星野结衣，但该剧本不进包
- **玩家可触达的部分**：主线 8 个剧本没有 `event interact`，互动模块玩家走不到，但数据在包内可被解包提取。偷拍功能是真正可用的：`第1章:27` 无条件设 `秘密相机=1`，`第1章:28` 发胶卷，从第一幕起就能对同班学生缩放拍照并存入「私密」相册。这个功能本身不露骨，但主题与学生角色组合后，在审核中风险很高。
- **相关内容**：
  - `FogWipeDemo.vn.txt:22` 的擦雾定义（定义资产经 VNGameConfig 进包）
  - `Assets/Scenarios/Hina/第1章.vn.txt:21,137-147,170-174,203-207` 是校园设定下的身体接触与暧昧描写。这个文件不进包，但已被 git 跟踪，公开仓库可见，文中不复述
  - 姫奈、白石、黑濑的角色资产已在 VNGameConfig 里进包
- **后果**：itch 禁止涉及未成年人的性内容，违规可能导致下架或封号。公开 GitHub 仓库里的同类数据也有风险。
- **修复方向**：
  - 试玩版去掉 `第1章:27-28`，并把偷拍功能从试玩版摘除
  - 从场景和配置里移除 interact、擦雾及相关数据
  - 不要靠补一句「已满 18 岁」解决：现有文本明确写了高二
  - 上架前对全部资产做一次人工核对
- **把握度**：同时进包已确认；政策判定待人工确认（发布前核对 itch 当前条款）。

### F3. 主线背景疑似取自 Hitomi.la，且已进包并进入公开仓库

- **位置**：`Assets/Art/Images/Background/Sweet Homemade  Hitomi.la*.png`（共 8 张），`Assets/Resources/VNGameConfig.asset` 的 backgrounds 区
- **证据**：天台=第 7 张，放学路=第 5 张，图书馆=第 4 张，商店街=第 1 张，我的房间与海边=第 2 张，这些都是主线路径上的背景。8 张均被 git 跟踪，公开仓库可见。
- **后果**：若文件名反映真实来源，这些图取自成人同人站，属于未授权素材，有 DMCA 和下架风险。
- **修复方向**：确认来源并全部替换。替换后还要清理 git 历史，否则图片仍能从公开仓库取回。
- **把握度**：文件名、引用关系、跟踪状态已确认；实际来源待人工确认。

### F4. 付费插件 Pixel Crushers Dialogue System 的完整源码留在公开仓库历史中

- **证据**：
  - 提交 `dc8dfaf`（2026-07-13）加入，`bcbdfe3`（2026-07-14）从工作树删除
  - `git rev-list --all --objects` 显示 3587 个相关对象，其中 1130 个是 .cs、.dll、.asset、.prefab
  - 涵盖 Scripts/Editor、UI、Lua、MVC 等目录
  - `origin/main` 和多个 `origin/agent/*` 分支都包含该提交，`gh` 确认仓库为 PUBLIC
  - 当前仍跟踪残留的 `Assets/Plugins/Pixel Crushers/.../DemoInputControls.cs`
- **后果**：公开再分发付费资产的源码，违反 Asset Store EULA 的风险很高。删除工作树文件不能解决。
- **修复方向**：先把仓库转为私有，再重写历史或新建干净仓库。这与项目「永远不删分支」的规则冲突，需要用户本人决定。
- **把握度**：历史和公开状态已确认；是否构成违约待人工判断。

---

## 高

### H1. 铭牌显示「星野结衣11」

- **位置**：`Assets/Art/VNEffects/Characters/星野结衣.asset:16`
- **证据**：`displayName` 解码后是「星野结衣11」，`LocalizedDisplayName` 直接用于铭牌（`Script/VNStage.Scene.cs:28`）。
- **后果**：女主每句台词的铭牌都带测试后缀。
- **修复方向**：改回「星野结衣」，并补英文、日文名。
- **把握度**：已确认。

### H2. 第 1 章开场有大量测试残留

- **位置**：`第1章.vn.txt:27-29,40,42,43,46,64-82,88`
- **证据**：
  - `:40` 的 `tutorial test01 force:on` 每次都强制播放。`Assets/VNEffects/Tutorials/TestTutor.asset` 第一步标题和正文全空，第二步标题是 `dia`，正文是占位句
  - `:88` 的台词是「（……）hahaha」
  - `:42` 是空的 `fx`
  - `:43` 让教室背景横向滚动
  - `:64-82` 是连续的运镜试验和进退场试验
  - `:29` 接取的「球场之王」在主线无法完成，`event badminton` 只存在于注释和 Demo 剧本
- **后果**：玩家开场几十秒就看到空教程和测试文本。
- **修复方向**：清理这一段，只保留正式演出。
- **把握度**：已确认。

### H3. 「装死」分支把 BGM 压到 7%

- **位置**：`第1章.vn.txt:155-156`，`Script/VNAudio.cs:336-349`
- **证据**：`volume bgm 0.07` 直接写入 `bgmVolume`，后面没有恢复语句。`:154` 的 `ui name 双描边` 也只在这条分支生效。
- **后果**：选了这个选项后，后续几乎听不到音乐。是否跨存档延续待验证。
- **修复方向**：删除这三行。
- **把握度**：写入行为已确认；持续范围待验证。

### H4. 任务「告白大作战」文案写的是别的角色，且阶段数不足

- **位置**：`Assets/Art/VNEffects/Quests/告白大作战.asset:17-20`，`公共片段.vn.txt:139`，`好感事件_星野结衣.vn.txt:215`
- **证据**：任务描述和两条阶段都写的是「小雪」，这是旧 Demo 的角色。资产只有 2 个阶段，剧本却跳到 3。
- **结论**：跳到 3 **不会**卡流程。`VNQuestEngine.SetStage` 只钳到 MaxStage，flag 确实变成 3，`主循环:75` 的第 3 章门槛可以满足。但 `StageText(3)` 返回空串，J 面板和 Toast 没有阶段文案。
- **后果**：任务面板显示错误的女主名，第三阶段没有说明。
- **修复方向**：重写文案，补第 3 阶段和英文、日文。
- **把握度**：已确认。

### H5. 产品身份全是模板默认值

- **位置**：`ProjectSettings/ProjectSettings.asset:15-16,151,173`，`Assets/Resources/VNGameConfig.asset:25,28`
- **证据**：
  - companyName 是 `DefaultCompany`，productName 是 `My project`，版本 `0.1.0`，Bundle ID 是模板值
  - `gameTitle` 为空，代码回退显示「Visual Novel」（`VNTitleMenu.ResolveGameTitle`）
  - 英文、日文标题为空
  - `titleBackground` 为空，回退到第一张背景
  - 游戏名其实写在 `结局集.vn.txt:201`（《辣妹与宅宅》）
- **后果**：exe 和窗口标题是 My project，标题画面是 Visual Novel。存档路径依赖 company 和 product，发布后再改会丢档。
- **修复方向**：首次发布前定好这些值。图标在本次检查范围外，一并确认。
- **把握度**：已确认。

### H6. 结局后无路可走，游戏内没有退出入口

- **位置**：`结局集.vn.txt:199-207`，`Script/VNScriptRunner.Dispatch.cs`（Run 结束段），`Script/VNTitleMenu.cs:163`
- **证据**：谢幕后没有 jump，Run 只把 `_running` 设为 false。全部运行时代码没有返回标题的实现，`Application.Quit` 只在标题菜单里。ESC 只用于关闭面板（`Script/VNScriptRunner.Input.cs:231-238`）。
- **后果**：通关后停在最后画面，默认全屏下只能 Alt+F4。
- **修复方向**：增加 `title` 命令或返回标题功能，谢幕时调用，并在设置面板加退出按钮。
- **把握度**：已确认。

### H7. 英文、日文入口开放，但主线完全没有翻译

- **位置**：`Assets/Resources/VNLocale/Scenarios/`，`Script/VNConfigPanel.cs:157-159`，`Script/VNScriptLocale.cs:51-57`
- **证据**：翻译表只有 Chapter1、Chapter2、Demo，这三个剧本在运行时都走不到。入口 8 个剧本没有任何翻译表，台词回退中文，只记警告。UI 三语键数一致，都是 293 条。任务、部分角色名也缺外文。
- **后果**：切到英文或日文后，UI 是外文，剧情全是中文。
- **修复方向**：试玩版隐藏语言按钮，或完成抽取和翻译。
- **把握度**：已确认。

### H8. 其他第三方素材来源不明，全项目没有 Credits

- **证据**：
  - `Assets/Art/InstaBackgroun/稳稳素材 (*).png` 是第三方素材包命名
  - `Assets/Art/Badminton/Badminton Playing Sports Fitness Health Sporting Goods, Badminton, Phy(*)` 像图库站下载
  - `教室の前の方（日中）.jpg` 等日文背景来自素材站的可能性高，通常有署名或再分发条款
  - `Bgm01/02.wav` 来源不明
  - `Assets/Audio/Music/Voice/MInimaxVoice/` 是 AI 语音，需核对商用条款
  - git 跟踪文件里只有 `LICENSE-OFL.txt` 一份授权文件
  - AI 生成素材（文件名带 prompt 或 seed）约 26 个
- **文件名关键词**：对 Art、Audio、Development 共 390 个文件名扫描受限词，命中 0。这只说明文件名干净，不代表图像内容已审。
- **后果**：可能违反素材条款，也影响 itch 的 AI 内容披露。
- **修复方向**：做一张素材来源表，核对条款，在游戏内加 Credits。发布前核对 itch 对 AI 内容的披露要求。
- **把握度**：现象已确认；授权状况待人工确认。

---

## 中

### M1. 12 处音效未登记，运行时静默缺失

- **位置**：`公共片段:34,99`，`好感事件:244`，`第2章:33,41,107,170`，`第3章:105,153`，`节日活动集:94,367,421`
- **证据**：`seLibrary` 只有 `se1`，实际文件是 `SE_MouseClick.wav`。缺失时 `VNAudio.Find` 只记 LogWarning，然后 return（`Script/VNAudio.cs:203-210`）。`se 雨 loop` 和 `se stop 雨` 也安全。
- **Lint 结论**：闪光、翻页、雨、脚步、惊、烟花都是**不报错、不卡住、只没声音**。`bg1`、`b01`、`s01` 只在 Chapter1、Chapter2，`voice1` 只在 SnsDemo，这些剧本运行时走不到，主线不受影响。
- **修复方向**：登记素材。门声这类地方也别再用鼠标点击音。
- **把握度**：已确认。

### M2. 第 2 章地图的「天台」永远不出现

- **位置**：`Assets/Project/Scenes/VNScriptDemo.unity:22496`，`第2章:204,221-231`
- **证据**：条件是 `好感度>=2`，但主线只写 `好感度_结衣`，`好感度` 从未赋值。`第2章:195-196` 的注释也提醒过要改。地图的 `mapSprite` 同样为空。
- **后果**：天台分支和 `天台伏笔` 都是死内容。
- **修复方向**：改成 `好感度_结衣>=40`，并配地图图。
- **把握度**：已确认。

### M3. 第 1 章的好感选择全部被覆盖

- **位置**：`主循环.vn.txt:45`，`Script/VNStatsHud.cs:140`
- **证据**：`stat 好感度_结衣 10` 不带符号，语义是绝对赋值。第 1 章选项和好感反馈累积的值都会被重置成 10。
- **后果**：第 1 章选择对好感没有影响。
- **修复方向**：改成增量，或只在未初始化时赋值。
- **把握度**：已确认。

### M4. 好感度没有属性定义，偷拍扣分写错 flag

- **位置**：`Assets/Art/VNEffects/Stats/`（只有 8 项，没有好感度和宅度），`Assets/VNEffects/SecretPhoto/秘密相机.asset:29`
- **证据**：`display` 回退成原始 id，Toast 显示 `好感度_结衣 +8`，C 面板也看不到（`结局集:196` 却叫玩家去看）。偷拍的 `affectionStat` 是「好感」，主线用的是 `好感度_结衣`，扣分落到无关 flag。
- **修复方向**：新建两个 VNStatDef，把 affectionStat 改对。
- **把握度**：已确认。

### M5. 素材占位复用很明显

- **证据**（`VNGameConfig.asset`）：
  - 便利店用的是教室图
  - 海边用的是「我的房间」图
  - 教室_黄昏用的是走廊图
  - 车站用的是秋日街景
  - CG_烟花和 CG_圣诞是同一张（`CG/7.png`）
  - 8 个 BGM id 只对应 2 个文件
- **后果**：海边合宿显示成卧室，玩家能明显察觉。
- **修复方向**：试玩范围内补素材，或删掉相关剧情。
- **把握度**：已确认。

### M6. 背景没有 VNImageEffectController，部分背景效果可能失效

- **位置**：`VNScriptDemo.unity:10327`，`Script/VNStage.cs:267-268`，`VNBackgroundScroll.cs:68`
- **证据**：场景中 `backgroundFx` 为 0，整个场景没有这个组件，运行时只有立绘、选项、对话框边框三处会添加。
- **后果**：情绪调色、bgscroll、伪景深在背景上可能都不生效，与 Editor 或 Build 无关。
- **修复方向**：给 Background 挂组件并连线，再验证。
- **把握度**：连线状态已确认；视觉后果待 Play Mode 验证。

### M7. 开发包进入 Player

- **位置**：`ProjectSettings/ProjectSettings.asset:848`，`Packages/manifest.json`，`Assets/Plugins/NuGet/*.dll.meta`
- **证据**：
  - Standalone 定义了 `UNITY_MCP_READY`，所以 `com.IvanMurzak.Unity.MCP.Runtime` 会编进 Player
  - NuGet 目录 18MB，含 Roslyn，Any 平台都启用
  - `Unity.AI.Tracing`、`Unity.AI.MCP.Runtime`、`Unity.InferenceEngine` 也没有限定 Editor
  - MCP 的 `RuntimeInitializeOnLoadMethod` 只出现在文档示例和主线程工具中，**没有自动连接**
- **后果**：主要是增加包体和 Build 冲突风险。没有发现直接的网络暴露。
- **修复方向**：做一个发行分支，移除这些包和宏；或把 DLL 设为 Editor only。`com.unity.ai.assistant 2.14.0-pre.1` 是唯一的 pre-release 包。
- **把握度**：配置已确认；Build 冲突待验证。

### M8. 试玩版仍能打开 AI 日记

- **位置**：`Script/VNScriptRunner.Input.cs:331`，`Script/VNScriptRunner.SaveLoad.cs:202`，`VNGameConfig` 的 `aiPersonas`
- **证据**：D 键不受试玩版开关限制。AI 人格资产也进包。Key 读取有 `UNITY_EDITOR` 宏保护，`*ApiKey*.txt` 没有进入 git 历史。
- **后果**：玩家按 D 看到空的 AI 功能。
- **修复方向**：加试玩开关，关闭 D 键并清空 aiPersonas。
- **把握度**：已确认。

---

## 低

| # | 问题 | 位置 | 修复方向 | 把握度 |
|---|---|---|---|---|
| L1 | 配置含测试项：`CG_test`、`00test`、`test1`、`test2`；`星野结衣 2`、`星野结衣 3` 的 id 都是 `星野结衣2` | `VNGameConfig.asset` | 清理，否则画廊可能出现多余格子 | 已确认 |
| L2 | 场景 chapters 残留 Demo、BattleDemo、RaisingDemo、SnsDemo 等，被配置覆盖后走不到，但 TextAsset 进包 | `VNScriptDemo.unity:27980-27996` | 清空场景列表 | 已确认 |
| L3 | 行内注释破坏 `@`，这 3 行变成同步并产生多余参数 | `第1章:83,105,110`，`Script/VNScriptParser.cs:148` | 把注释挪到单独一行 | 已确认 |
| L4 | 30MB 和 26MB 的 BGM 是 Decompress On Load，内存和加载偏重 | `Assets/Audio/Music/BGM/*.meta` | 改 Streaming | 已确认 |
| L5 | 体积：贴图约 80MB（压缩后估算，全部已压缩，15 张超 2048 被限到 2K），字体 45MB，音频源文件 60MB。预计整包 150~200MB，没有失控。未引用的 Art（约 82MB）、Models、Development 不进包 | 依赖闭包 | 立绘 4864×3328 被压到 2K，可按需单独调 | 估算待 Build |
| L6 | URP 旧引用：`PC_Renderer.asset:22-34` 有 7 个探针调试资源 guid 找不到；`DefaultVolumeProfile.asset:78,119,264,832` 有 4 个缺失 Volume 脚本 | `Assets/Project/Settings/` | 在 Editor 里重新保存或重建配置 | 待验证 |
| L7 | OFL 只有一份协议，放在 Resources 里，玩家看不到 | `Assets/Resources/VNFonts/LICENSE-OFL.txt` | 随包附协议和字体版权声明 | 已确认 |
| L8 | Display 字体的 `*-Black-Dynamic` 烘焙资产不存在，按设计回退运行时生成。`NotoSansSC-Dynamic` 的源字体其实是 LXGW WenKai TC，简体字覆盖靠 fallback | `Script/VNFont.cs` | 实机抽查缺字 | 待验证 |
| L9 | `星野结衣_BackUp.asset` 在 Assets 根目录，已跟踪，不进包。26 张调试截图被跟踪，公开可见，`.gitignore` 的路径已过期 | `Assets/`，`Assets/Development/DebugScreenShot/` | 移除并修正 ignore | 已确认 |
| L10 | 硬编码中文 UI 抽样：未发现明显问题，命中基本是 Inspector 标签、别名、AI 提示词或符号。Toast 回退原始 id 见 M4 | Runtime 抽样 | 非全量 | 抽样 |
| L11 | 文档漂移：CLAUDE.md:14 版本写 6000.0.62f1；:136,238 写 `Assets/Scripts`；:233 写 `Assets/Assets`；:266 与 ProjectCodeGuide.md:169,176 写 `Assets/CG/`；ProjectCodeGuide.md:649 写旧路径；CLAUDE.md:18,77 还写着已安装 Pixel Crushers；`vn-add-assets/SKILL.md:32` 写旧 CG 路径。script-* 技能里的 `Assets/Scripts` 只是示例，不算失效 | 见左 | 批量更新 | 已确认 |
| L12 | 87 个 `*.linked.md` 处于 M 状态（79 个 SKILL，8 个根目录文档）。它们由 link_doc_refs 工具生成。早先样本 assets-copy 为 24+/6-，复查时 diff 为空，并有 LF/CRLF 警告，判断以换行符和链接转换噪声为主 | `link_doc_refs.report.md` | 提交时不要混入功能改动 | 待验证 |

---

## 已通过的检查（未发现问题）

- **无 asmdef**：Assets 下没有 asmdef，所有脚本编入 Assembly-CSharp 和 firstpass。Editor 目录由 Unity 自动隔离。
- **UnityEditor 引用**：Runtime 全量 Grep 只有 `VNTitleMenu.cs:164-165` 和 DOTween 模块，都在 `#if UNITY_EDITOR` 内。`VNTutorialPicker.cs` 整个文件都受保护。其他 `UNITY_EDITOR` 块没有向外暴露被外部使用的成员（抽查）。
- **Assets/Project/Scripts 之外**：`Assets/Editor/` 在 Editor 目录；`Assets/Development/TutorialInfo/Scripts/Readme.cs` 只是 ScriptableObject，没有编辑器引用。
- **反射**：只有 `VNEmoteCatalog.cs:94-115` 和 DOTween 模块。Standalone 未设 backend，默认 Mono；`managedStrippingLevel` 为空，默认设置下风险低（待在 Player Settings 确认）。切 IL2CPP 时 Assembly-CSharp 的公开方法在 Minimal 档位下仍应保留，Build 后需测 emote。
- **Input**：`activeInputHandler: 1`，运行时没有旧版 `Input.` 调用。
- **Resources.Load**：VNGameConfig、VNFonts 各路径、`VNLocale/ui.<code>`、场景翻译表都已核对，字面量匹配，缺失项都有回退。字体 `includeFontData` 都是 1。
- **Shader.Find**：字面量都与 shader 名匹配。VNAdditive、VNParticleAlpha、VNScreenTransition 经场景材质进包，其余 6 个见 F1。
- **移除的三个包**：代码没有 using，全部 YAML 的 guid 扫描没有发现指向这三个包的引用。
- **RP**：Standalone 默认质量是 PC，绑定 PC_RPAsset（HDR 开）。Mobile 级别排除了 Standalone。
- **场景**：3 个场景没有缺失脚本。依赖闭包的 23 个外部 guid 全部解析到包内。
- **.meta**：没有孤儿 meta，没有重复 guid。唯一缺 meta 的是隐藏文件 `.nuget-installed.json`，可忽略。
- **Development 资产**：不在依赖闭包内。
- **剧本加载**：VNGameConfig 直接引用 TextAsset（`entryScript`=第1章，chapters 8 个），`Script/VNScriptRunner.cs:118-119` 覆盖场景的 `SnsDemo`。不依赖 Resources，也不读磁盘，进包正常。
- **同名第1章**：`FindChapter`（`Script/VNScriptRunner.Flow.cs:378`）按名字查 script、entry、chapters。Hina 版不在运行时列表，也不进包，`第1章::` 只会到主线，玩家不会进错章节。把 Hina 加入 chapters 后才会发生同名冲突。
- **流程静态检查**：主线 8 个文件未定义标签 0 个，跨文件跳转全部有效；死循环未发现，`文化祭_执行` 有 end 出口，`行动菜单` 有「这个月就到这里」；未赋值 flag 只有 `道具_*`，由商店写入；bg、cg、bgm、voice 全部已登记；表情名全部存在；`event plan id:文化祭准备` 没有资产，但带 `pool:`，走设计内回退。
- **API Key**：未打开 key 文件。`*ApiKey*.txt` 不在 git 跟踪和历史对象中。
- **DOTween**：是免费版 DLL，没有 Pro。公开再分发是否符合条款需查 `dotween.demigiant.com/license.php`（待验证）。

---

## itch 上架需要做的标记

- 试玩内容按实际填写成人内容标记。F2 未解决之前不要发布，标记也解决不了未成年设定问题。
- 按 itch 当前要求披露 AI 生成的图像和语音。
- 发布前逐条核对 itch 当前的内容与 AI 披露条款。本次审计没有联网核实。

---

## 已检查

1. **打包**：asmdef、UnityEditor 引用、外部编辑器代码、反射与 Stripping、Resources.Load、Shader.Find、Always Included、三个已移除包、开发包、pre-release、PlayerSettings、全屏与分辨率、Input、体积与贴图、两套 RP。
2. **场景与配置**：缺失脚本、BackUp 资产、.meta、Development 资产、VNGameConfig 空引用。
3. **剧本**：通读主循环、公共片段、好感事件、第1/2/3章、结局集、节日活动集、Chapter1、Chapter2、Hina/第1章；起始剧本、加载方式、Lint 项后果、quest 阶段、同名章节。
4. **本地化**：翻译表、UI 键一致性、字体回退、硬编码抽样。
5. **合规**：年龄设定、亲密内容进包路径、素材来源、字体与插件、git 历史。
6. **文档漂移**：CLAUDE.md、ProjectCodeGuide.md、skills、linked.md。

## 未检查或未完成

- 没有实际 Build，也没有运行 Play Mode，所有运行时后果都来自代码推导。
- 没有查看任何图像内容，只看了文件名和引用关系。
- `初次抚摸.asset` 只看字段结构，没有读台词内容，不评估露骨程度。
- 重构后的 PhotoBooth partial 没有逐行审阅。
- 字体缺字没有逐字统计。
- 硬编码中文只抽样，不是全量。
- 没有审阅根目录的企划文档（如 `剧情大纲_球场复仇线.md`）的合规内容。
- 没有检查 Windows 图标。
- 没有联网核对 itch、Asset Store、DOTween、MiniMax 的当前条款。

## 操作记录

审计早期有一条 Grep 命令误把输出写到 Git Bash 的 `/tmp/x`（项目外），违反了「只写报告文件」的规则。已当场删除，没有写入项目内任何文件。
