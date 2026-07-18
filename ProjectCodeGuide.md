# ProjectCodeGuide — 项目代码指南

> 这份文档回答三个问题：**每个脚本是干什么的、它们怎么协作、你想改/加东西时该动哪里**。
> 面向的读者是"半年后忘光了细节的自己"和"第一次接手这个项目的人"。
> 开发过程的历史记录在 `WhatAiDo.md`（按章节倒查"为什么当时这么设计"）；
> 给 AI 助手的工作规则在 `CLAUDE.md`。本文只讲"现在的代码长什么样、怎么用"。

---

## 目录

1. [大图景：三层架构](#一大图景三层架构)
2. [一次台词的完整旅程（数据流）](#二一次台词的完整旅程数据流)
3. [剧本层：Parser / Runner / Flags](#三剧本层)
4. [舞台层：VNStage 与角色](#四舞台层)
5. [音频：VNAudio](#五音频vnaudio)
6. [玩法扩展层：事件接口 / 任务 / 地图](#六玩法扩展层)
7. [系统 UI：存档 / 回想 / 配置 / 工具条](#七系统-ui)
8. [演出组件库（32 个特效组件分类详解）](#八演出组件库)
9. [编辑器工具（6 个）](#九编辑器工具)
10. [Shader（4 个）](#十shader)
11. [常见任务菜谱（How-To）](#十一常见任务菜谱how-to)
12. [全局约定与坑清单（维护者必读）](#十二全局约定与坑清单)

---

## 一、大图景：三层架构

```
┌─────────────────────────────────────────────────────────┐
│  剧本层（纯文本 .vn.txt）                                  │
│  VNScriptParser → VNScriptRunner → VNFlags               │
│  "剧情写什么" —— Git/AI 友好，唯一真相                      │
└──────────────────────┬──────────────────────────────────┘
                       │ 命令分发（Dispatch）
┌──────────────────────▼──────────────────────────────────┐
│  舞台层（场景里的运行时对象）                               │
│  VNStage（总调度）+ VNAudio + VNDialogueBox + 各特效组件    │
│  "画面和声音怎么呈现"                                      │
└──────────────────────┬──────────────────────────────────┘
                       │ event 命令 / flag 读写
┌──────────────────────▼──────────────────────────────────┐
│  玩法扩展层                                               │
│  VNEventModule 接口（QTE/地图/未来的战斗…）+ VNQuestLog     │
│  "剧情之外的可玩内容"                                      │
└─────────────────────────────────────────────────────────┘

横向贯穿所有层的两条总线：
  · VNFlags   —— 唯一状态总线（整型字典，随存档序列化）
  · VNToast   —— 轻量提示通道（任务/模式切换/系统消息）
```

**三个最重要的设计决定**（理解了它们就理解了整个项目）：

1. **文本是唯一真相**。剧本可视化编辑器、存档、调试跳转全部围绕 `.vn.txt`
   物理行号工作；任何工具改剧本最终都落回文本。
2. **VNFlags 是唯一状态**。任务进度、地图去过标记、事件结果、好感度全是
   flags 里的整数——所以存档、`if` 分支、调试重建对任何新系统都"免费"。
3. **event 是唯一玩法插槽**。任何"暂停剧本→玩家交互→带结果返回"的玩法
   都实现 `VNEventModule`，剧本契约（id + kwargs + 结果名）永不改变。

---

## 二、一次台词的完整旅程（数据流）

以剧本行 `亚里沙 微笑: 今天天气真好。` 为例，跟一遍代码路径：

1. **解析**：`VNScriptRunner.Play()` 把整个文件交给 `VNScriptParser.Parse()`，
   得到 `List<VNScriptCommand>`。这一行首 token 不是关键字 → 走 `ParseSay()`，
   拆出 `speaker=亚里沙, expression=微笑, text=今天天气真好。`
2. **执行**：主循环 `Run()` 协程逐条取命令进 `Dispatch()` → `case "say"` →
   `SayCo()` 协程。
3. **上台**：`SayCo` 调 `VNStage.Say()`——舞台查 `VNCharacterDef` 找到亚里沙、
   按"微笑"切表情（交叉溶解）、通知 `VNSpeakerHighlight` 高亮说话者、
   把名牌颜色/头像/文本交给 `VNDialogueBox`。
4. **打字**：`VNDialogueBox` 内部的 `VNTypewriterText` 逐字上浮显示，每个字
   调一次 `VNAudio.TypeTick()` 打字音；如果之前有 `voice` 命令绑定，
   `VNCharacterMouth` 同步开合口型、`VNAudio` 压低 BGM。
5. **等待**：`SayCo` 设 `_waitingAtSay = true`（此刻才允许 F5 存档），等玩家
   点击/Enter/Auto 计时；`VNBacklog.Record()` 已把这句记进回想。
6. **推进**：玩家点击 → `_advance = true` → 主循环取下一条命令。

存档时：`VNStage.CaptureSnapshot()` 收集背景/天气/色调/BGM/角色站位 +
`VNFlags.All` + 当前台词索引 → `VNSaveSystem` 写 JSON + PNG 缩略图。
读档时反向 `RestoreSnapshot()` + `ResumeAt(index)`。

---

## 三、剧本层

### VNScriptParser.cs（`Script/`）

- **职责**：把 `.vn.txt` 文本变成 `List<VNScriptCommand>`。纯静态类、无状态、
  不依赖任何场景对象——可以在编辑器/测试里独立调用。
- **语法规则**（全部实现都在这一个文件里）：
  - 首 token 在 `Keywords` 集合里 → 命令行（位置参数 + `key:value` 参数）
  - 否则 → 台词行（`说话者 [表情]: 内容`，支持全角/半角冒号，冒号开头=无名牌旁白）
  - `#` 注释、空行跳过；行尾 `@` = 异步（`isAsync`，Runner 不等它演完）
  - `*` 行挂到上一条 `choice`/`event`（选项/结果行），`>` 行挂到上一条 `camseq`（路径点）
- **关键成员**：`Keywords`（27 个关键字集合，编辑器通过 `CommandKeywords`
  共享同一来源）、`VNScriptCommand`（keyword/args/kwargs/options/camPoints/line）。
- **扩展**：加新命令第一步就是往 `Keywords` 加一个词（详见[菜谱一](#菜谱一给剧本加一条新命令)）。
- **维护注意**：`*` 行必须紧跟块命令（中间隔命令行会断开归属，这是有意设计）；
  kwargs 的值**不能含空格**（token 按空白切分）。

### VNScriptRunner.cs（`Script/`）

项目的**心脏**，约 1200 行。职责按区块划分：

| 区块 | 内容 |
|---|---|
| 播放控制 | `Play/Stop/ResumeAt/SwitchChapter`；主循环 `Run()` 协程逐条 `Dispatch` |
| Dispatch | 巨型 switch：每个关键字 → 一段执行代码或专用协程（SayCo/ChoiceCo/EventCo/CamseqCo…） |
| 输入 | `Update()`：推进/催促打字、H 回想、J 任务、F5/F9 存读档、A 自动、S 快进、右键+U 隐藏 UI。**新版 Input System**（`Keyboard.current`），禁止旧 `Input.` |
| Auto/Skip | `SetAuto/SetSkip`；Skip = `DOTween.timeScale` 全局加速，二者互斥 |
| 存读档 | `SaveTo/LoadFrom` + 截图协程；`_waitingAtSay` 才允许存 |
| 调试 | `PlayFromSourceLine(source, line, rebuild)`：编辑器"从选中行播放"的运行时入口；`RebuildStateBefore()` 按文件顺序推断前置状态 |
| 事件 | `EventCo`：见[第六节](#六玩法扩展层)；`_eventActive` 期间 Update 直接 return |

- **它持有什么**：`stage`（Inspector 连线）、`script`（TextAsset）、运行时自动
  查找/自建的 `_backlog/_saveLoadPanel/_configPanel/_quickToolbar/_questLog`。
- **同步 vs 异步的约定**：`Dispatch` 返回 `IEnumerator`（要等）或 `null`（瞬发）；
  行尾 `@` 时 Runner 用 `StartCoroutine` 放飞不等待。
- **维护注意**：
  - 加命令时若命令会改"舞台可见状态"，**必须**同步考虑 `RebuildStateBefore()`
    里要不要加对应 case（否则"从选中行播放"重建不出该状态）。
  - `JumpTo` 只认 `label`；跳转失败只报错不中断——排查分支问题先看 Console。
  - 所有"面板打开就阻断推进"的互斥都集中在 `Update()` 前半段，顺序有意义
    （事件 > 隐藏UI > Config > 存读档 > 回想 > 任务）。

### VNFlags.cs（`Script/`）

- **职责**：全局整型变量字典。`Get/Set/Add/Clear/All`、`Apply("好感度+1")`、
  `Evaluate("好感度>=2")`（无空格条件串）。
- **谁在用**：`flag`/`if` 命令、choice/event 的 `flag:` 选项、任务系统
  （`任务_<id>`）、地图去过标记（`去过_<地点>`）、事件整数结果（`事件结果`）。
- **约定俗成的命名空间**（靠前缀区分，没有强制）：`任务_`、`去过_`、`事件结果`。
  自己加新系统时也用前缀，避免撞名。
- **已知局限**（第三十九/四十一章记录在案）：只有整型；条件不支持
  and/or/取模。日历、战斗数值做深之前要先扩这里。

---

## 四、舞台层

### VNStage.cs（`Script/`）

**舞台总调度**，Runner 唯一的"对下"出口。持有全部演出组件引用（Inspector
由场景生成器连线），职责：

- **背景**：`backgrounds` 列表（id→Sprite），`SetBackground(id, transition)`
- **角色**：`characters`（VNCharacterDef 列表）；`CreateCharacter/Show/Hide/Move/
  SetExpression`——运行时给每个角色组装 `rect + VNImageEffectController +
  VNEntranceAnimator + VNCharacterEmotes + VNCharacterBlink + VNCharacterMouth +
  光环 + 脚影` 的完整堆叠
- **台词**：`Say()` 把说话者/表情/文本分发给对话框、高亮、口型
- **fx 路由**：`Fx(name, value)` 把 `fx godrays on` 这类开关转发给对应组件，
  `_fxStates` 记录开关状态供存档
- **快照**：`CaptureSnapshot/RestoreSnapshot`——存档和调试重建共用；
  `RestoreSnapshot(data, instant:true)` 是静默摆台模式
- **eventRegistry**：事件模块注册表引用（P1 加入）

**维护注意**：给舞台加新的"有开关状态"的效果时，记得 ①`Fx()` 路由
②`_fxStates`/`ToggleFxNames` ③快照存取，三处一起，否则存档读回来状态飘。

### CG 系统（VNStage.ShowCg/HideCg + VNCgUnlocks.cs，`Script/`）

- 剧本 `cg <id> [transition:] [chars:keep] [fx:keep]` / `cg off`；素材在
  `Assets/CG/`（文件名 = id），生成器灌入 `VNStage.cgLibrary`（CgEntry.group
  为 P2 差分组预留字段）。
- 显示复用背景管线：`SwapStageImage`（背景/CG 共用的换图+转场私有方法）。
  立绘隐藏 = characterLayer 整层 CanvasGroup 淡出（GO 保持活跃，协程/Tween
  不断）；环境特效暂停清单 = `CgAmbientFxNames` + 天气，`cg off` 时恢复。
- **VNCgUnlocks**：全局解锁 JSON（persistentDataPath/vn_cg_unlocks.json）。
  铁律：**解锁状态永远不进 VNFlags/存档**（生命周期不同，读旧档会覆盖 flags）。
- 扩展：加 CG = 往 Assets/CG 放图 + 重新生成场景（或手动往 cgLibrary 加条目）；
  改"哪些特效算环境特效" = 改 VNStage.CgAmbientFxNames。
- 存档字段 `cgId/cgKeepChars/cgKeepFx`；恢复顺序 = 先常规摆台最后 ShowCg(instant)。

### VNCharacterDef.cs（`Script/`）

- **职责**：角色定义 ScriptableObject（`Assets/VNEffects/Characters/*.asset`）。
  id、名牌显示名/颜色、表情列表（名字→Sprite）、站位偏移、缩放标定、
  对话头像参数、眨眼配置（闭眼图/间隔）、口型配置（张嘴图/间隔/仅默认表情）。
- **扩展**：给角色加新的"每角色可配"能力时字段加在这里，然后在
  `VNCharacterVisualPreviewWindow` 里补预览 UI（保持"确认后写入资产"流程）。

### VNCharacterBlink.cs / VNCharacterMouth.cs（`Script/`）

- **职责**：自动眨眼（默认表情下随机间隔换闭眼 Sprite）/ 说话口型
  （台词+语音期间在叠加子 Image 上随机开合张嘴图）。
- **共同设计**：整张透明画布叠加、与主体共享 `VNImageEffectController.Mat`
  材质（溶解/调色同步）、DOTween 随机间隔驱动。
- **扩展提示**：这套"透明画布叠加层"就是将来做红晕/汗珠/怒气符号
  `overlay` 命令的现成蓝本（第三十九章第 10 条）。

### VNDialogueBox.cs + VNTypewriterText.cs（根目录）

- **VNDialogueBox**：对话框本体（流光边框/名牌/推进箭头/头像）。
  `Say(name, content)`、`Show/HideBox`（事件期间 Runner 会 Hide）、
  `SetInterfaceVisible`（隐藏 UI 功能）、`SetTextSpeed`、`SetPortrait`。
  自带嵌套 Canvas（排序 40），快捷工具条挂在它下面（排序 41）。
- **VNTypewriterText**：逐字上浮打字机（**TMP 版**）。`LateUpdate` 里
  `ForceMeshUpdate` 后遍历 `textInfo.characterInfo` 改每字 4 顶点
  （y 偏移 + alpha），`UpdateVertexData` 提交；富文本标签不占字数。
  每字回调 `VNAudio.TypeTick()`；`IsTyping/Complete()` 供"催促"用。
  台词内嵌标记 `{w:0.5}` 这类 P3 功能将来就实现在它的逐字循环里
  （characterInfo 给出每字顶点索引，逐字抖动/波浪直接叠加 y 偏移即可）。

### VNFont.cs（`Script/`）+ VNFontAssetBuilder.cs（`Editor/`）—— TMP 字体管线

- **VNFont**：全项目 TMP 字体唯一入口。任何运行时创建的文字组件一律
  `text.font = VNFont.Asset`；**不要**再各自加载字体。三级兜底：
  预烘焙资产（Resources/VNFonts/NotoSansSC-Dynamic）→ 随包 OTF 运行时动态创建
  → OS 中文字体。动态多图集模式按需光栅化字形，`Prewarm(text)` 可把整段文本
  （如剧本全文，Runner 已接）预热进图集避免播放期卡顿。
- **VNFontAssetBuilder**：菜单 Tools → VN Effects → Create TMP Font Asset，
  生成持久化字体资产（材质/图集挂子资产）。**编辑期创建、随场景保存的 TMP
  文字必须用它**（`EnsureFontAsset()`），运行时临时资产存进场景会变 Missing。
- 扩展：换字体 = 替换 Resources/VNFonts 下的 OTF + 重新生成预烘焙资产；
  加第二字体（如标题花体）= 照 VNFont 模式再写一个静态入口，别混进 VNFont。
- **多语言**（五十七章）：`VNFont.Asset` 按 `VNLocale.Language` 返回字体——
  中/英共用 Noto Sans SC，日文 Noto Sans JP（SC 挂作 fallback）。语言切换时
  `HandleLanguageChanged` 只替换 VNFont 管理的字体，场景里手动指定的不动。

### 本地化（VNLocale / VNScriptLocale，`Script/`；VNLocalizationTools，`Editor/`）

- **VNLocale**：语言管理（中/英/日，PlayerPrefs 持久化）+ UI 字符串表。
  玩家可见 UI 文案一律 `VNLocale.T("key")`（带参数用 `T(key, args)`），表在
  `Resources/VNLocale/ui.<code>.txt`；回退链 当前语言→中文→key。
  切语言赋值 `VNLocale.Language` 即可：先换字体，再广播 `LanguageChanged`，
  常驻 UI（功能条）当场重建、惰性面板销毁缓存下次打开重建。
  **订阅事件的组件若 Initialize 会被多次调用，必须先 `-=` 再 `+=`（幂等）。**
- **VNScriptLocale**：剧本台词/选项翻译。`.vn.txt` 永远只写中文；翻译表
  `Resources/VNLocale/Scenarios/<剧本名>.<lang>.txt`，key =
  FNV-1a(原文)+出现序号（`NextKey/Hash` 与编辑器工具共用，**改一处必须两处同步**，
  实际上就是同一个方法）。Runner 在 LoadCommands 后 `Apply()` 标注
  `localizedText`，显示走 `TextOf()`，缺译回退中文。
  **红线：event 结果行、角色 id、flag 名是逻辑标识符，永远不进翻译**；
  choice 选项按索引匹配所以只翻显示文本是安全的。
- **VNLocalizationTools**：Tools → VN Effects → Localization。
  Extract = 生成/增量合并翻译表（已译按 key 保留，中文改动的旧译文挪到
  孤儿注释区）；Validate = 缺译统计。日常工作流：改剧本 → Extract → 填表 →
  Validate。
- 资产文案：VNCharacterDef.displayNameEn/Ja（名牌）、VNQuestDef 英/日
  标题/描述/阶段、VNMapModule.Location 英/日显示名——全部留空回退中文。
- 扩展：加新语言 = VNLanguage 枚举加项（**只能追加，顺序是 PlayerPrefs 存储值**）
  + VNLocale.Codes/DisplayName + `ui.<code>.txt` + VNLocalizationTools.TargetLanguages
  +（可选）VNFont 新 Profile；资产类各加一个字段并进各自的取值 switch。

---

## 五、音频：VNAudio（`Script/`）

- **通道**：BGM（双 AudioSource 交叉淡化）/ SE（一次性 PlayOneShot + 循环环境音
  每个独立 AudioSource）/ Voice（单通道，新顶旧，自动压低 BGM）/ 打字音。
- **音频库**：`bgmLibrary / seLibrary / voiceLibrary` 三个通道库 +
  旧混合 `library`（兼容回退，三个通道都查得到）。每条目
  `volume`（0~1 基准音量）——**素材响度不齐在库里标定一次，全局生效**。
- **音量公式**：`实际音量 = 条目基准 × 剧本 vol 参数 × 通道音量`。
  循环 SE 与语音都记录了自己的增益，全局改通道音量时按增益重算。
- **剧本对接**：`bgm play <id> [fade:] [vol:]` / `se <id> [loop] [vol:]` /
  `voice <id> [vol:]` / `volume bgm|se|voice <0..1>`。
- **维护注意**：`ResetForDebug()` 只给编辑器中间行调试用；
  新素材**登记进对应通道库**，别再往旧 library 塞。

---

## 六、玩法扩展层

### VNEventModule.cs（`Script/`）—— 接口本体

```csharp
public abstract class VNEventModule : MonoBehaviour
{
    public void Launch(VNEventContext ctx, Action<string> onDone); // Runner 调
    protected abstract void OnLaunch(VNEventContext ctx);          // 你实现：搭 UI 开始玩
    protected void Done(string outcome);                           // 你调用：结束并交结果（只生效一次）
    public virtual void CancelForDebug() { }                       // 中断清理钩子
}
```

`VNEventContext` 给模块的信息：`eventId / stage / kwargs（Kw/KwF/KwI 读取）/
outcomes（剧本 * 结果行的结果名，AcceptsOutcome() 判断）/ line`。

**模块三大铁律**（违反会破坏存档/调试/快进）：
1. 只操作自己的 UI 子树和 VNFlags，**不直接改舞台演出**
2. 计时用 `unscaledTime`、Tween 加 `SetUpdate(true)`（不受快进影响）
3. 所有 Tween `SetLink`（模块随时可能被销毁）

### VNEventRegistry.cs（`Script/`）

- id → 模块模板（预制体或场景内**禁用**的模板物体）。`Create()` 实例化到
  运行时自建的 EventLayer（Canvas overrideSorting **60**——ChoicePanel 45 之上、
  ScreenTransition 100 之下，所以能用全屏转场包裹进出事件）。
- **扩展位**：将来支持重型 3D 玩法时，在 Entry 加场景模式字段、`Create`
  改走 additive 加载——剧本契约不用动（第四十四章后的评审结论）。

### Runner 侧：EventCo（在 VNScriptRunner.cs）

流程：关 Skip/Auto → `_eventActive=true`（快捷键全禁）→ 藏对话框 →
实例化模块 → 轮询结果 → 销毁模块恢复 UI → 记 Backlog →
整数结果写 flag`事件结果` → 匹配 `*` 结果行（flag 操作 + 跳转）。
`Stop()` 里的 `CleanupActiveEvent()` 负责中断清理。

### VNQteModule.cs —— 示例模块①：连打条

剧本 `event qte time:3 target:12 title:xxx`，结果 `success/fail`。
UI 全程序化（面板/进度条/计时），是写新模块时**最好的抄写范本**。

### VNQteModule → VNMapModule.cs —— 示例模块②：地图选地点

- 地点配置在模板 Inspector（`Location`：名字=结果名/归一化坐标/
  VNFlags 显示条件/可选图标）。
- 双重过滤：条件不满足隐藏 + 剧本 `*` 没接住的地点隐藏；全空则
  `Done("")` 防软锁。
- 选中自动 `去过_<地点>+1`，再来时标 ✓。底图：剧本 `bg:` > 模板
  `mapSprite` > 程序化面板。

### VNQuestDef.cs / VNQuestLog.cs —— 任务系统

- **状态即 flags**：`任务_<id>` = 0 未接取 / 1..n 进行中 / 100 完成 / -1 失败。
- `VNQuestDef` 资产只管文案（标题/描述/各阶段目标）；**没建资产的任务
  照常运作**（id 当标题）。
- `VNQuestLog`：执行 `quest start|stage|done|fail <id> [阶段]`（写 flag +
  VNToast），J 键日志面板（进行中/完成/失败三栏，无资产的活动任务兜底显示）。
- **扩展**：加"任务追踪 HUD"之类，只需读 flags + defs 渲染，不用碰命令。

---

## 七、系统 UI

| 脚本 | 职责 | 要点 |
|---|---|---|
| VNSaveSystem.cs | 存档读写（静态类） | 20 槽 JSON + PNG 缩略图存 `persistentDataPath`；`VNSaveData` 是唯一存档结构——**加字段要给默认值**（旧档兼容靠字段初始化器） |
| VNSaveLoadPanel.cs | F5/F9 的 20 槽界面 | 截图缩略图/时间/末句台词；覆盖确认弹窗；打开时暂停 DOTween |
| VNConfigPanel.cs | 设置面板 | 三路音量/文字速度/自动速度/显示模式，PlayerPrefs 持久化，启动时 Runner 调它回放设置 |
| VNQuickToolbar.cs | 对话框右下功能条 | Save/Load/Auto/Skip/Log/任务/Config/隐藏UI；挂在对话框 Canvas 下排序 +1；**加按钮记得改总宽**（现 693） |
| VNBacklog.cs | H 键回想 | 独立 Overlay Canvas 600；`Record()` 由 SayCo/ChoiceCo/EventCo 调 |
| VNToast.cs | 右上角提示 + 模式角标 | 静态 `Show(msg)`；任务/自动/快进都走它 |
| VNQuestLog.cs | J 键任务日志 | 见第六节；UI 结构与 Backlog 同构 |

---

## 八、演出组件库

（`Assets/Scripts/VNEffects/` 根目录 32 个。共同风格：程序化贴图、DOTween、
Start/Stop 成对 API、`SetLink` 防泄漏。按类别分组。）

### 8.1 单图特效核心

- **VNImageEffectController**：单张图的特效总控。为图片创建**独立材质实例**
  （多立绘互不串扰），暴露溶解/扫光/发光/闪白/HSV/波浪/轮廓光/波光/模糊参数
  + 悬浮/呼吸循环动作。几乎所有角色/背景效果最终都落到它的 `Mat` 上。
  **扩展特效参数时**：shader（VNImageEffect.shader）加参数 → 这里加包装
  属性/Tween 方法。
- **VNEntranceAnimator**：出场预设×6（溶解辉光/滑入/弹出/扫光/爆闪/残影冲入）
  + 退场×2 + `StartIdleEffects`（出场完自动开呼吸/悬浮）。组合
  ImageEffectController + CanvasGroup + RectTransform。
- **VNProceduralTextures**：静态贴图工厂（柔圆/四芒星/光晕/光束/花瓣/圆环/
  圆角面板/描边框…全部代码生成、缓存、`hideFlags=DontSave`）。**零美术依赖
  的基石**——新 UI/粒子先来这里找现成贴图。

### 8.2 角色附属

- **VNGlowBackdrop**：立绘背后光环脉动（Additive shader）。
- **VNFootShadow**：脚下椭圆影，跟随横移/悬浮高度/溶解度联动。
- **VNCharacterEmotes**：情绪动作六连（惊讶跳/生气抖/害羞缩/沮丧垂
  (+Recover)/点头/摇头），剧本 `emote 角色 动作` 直达。
- **VNSpeakerHighlight**：说话者亮、其他人压暗（明度+缩放双通道）。
- **VNToneMatch**：立绘色调匹配背景（采样背景主色做轻度 tint）。

### 8.3 全屏运动（容器层级各占一层，可叠加）

- **VNCamera**：运镜五式 pushin/snapzoom/pan/dolly/reset，作用于 ZoomRoot；
  `camcut/camto/camseq` 的目标解析（锚点/角色[:部位]/坐标）也在这条线上。
- **VNScreenShake**：三级位置震动（SceneRoot）。
- **VNDutchAngle**：荷兰角旋转 + 防露角自动放大（TiltRoot）。
- **VNHeartbeat**：心跳缩放脉动（SceneRoot），紧张演出/限时选择配套。
- **VNParallax**：三层背/中/前景鼠标视差（8/13/19px）。

### 8.4 环境氛围

- **VNAmbientParticles**：粒子预设×8（尘埃/星光/光斑/花瓣/雨+溅落/雪/萤火虫/雾）
  + `PlaySparkleBurst`。**注意**：velocityOverLifetime 三轴曲线模式必须一致；
  运行时创建用"先 SetActive(false) 配好再激活"（`Create` 是范本）。
- **VNWeatherController**：天气切换（驱动上面的粒子组）+ 调色联动。
- **VNMoodGrading**：七种情绪色调，双 Volume 权重交叉过渡（URP 后处理）。
- **VNGodRays / VNCloudShadows / VNHeatHaze / VNFakeDoF / VNEdgeGlow /
  VNVignetteFocus**：光束/云影/热浪扭曲+雾/伪景深(UI 不写深度所以是"伪")/
  屏幕边缘情绪泛光/聚焦晕影。都是 `fx <name> on|off` 路由的终点。

### 8.5 转场与镜头辅助

- **VNScreenTransition**：全屏转场大合集（噪声溶解/百叶窗/瓦片/圆扩散/水墨/
  爆闪/光斑/眨眼 + 高级四式卷页/碎裂/水波/墨染）。`transition` 与
  `bg ... transition:` 命令的执行者，排序 100 盖住一切。
- **VNShatterGraphic**：碎裂转场的碎片网格 Graphic（给 ScreenTransition 用）。
- **VNCameraFade**：camseq 路径点 `xfade:` 交叉淡化的截屏叠化辅助。

### 8.6 输入反馈与组合技

- **VNMouseStardust**：鼠标星尘拖尾。**VNClickRipple**：点击涟漪。
- **VNSakuraBurst**：樱吹雪告白组合技（`sakura` 命令）。
- **VNChoicePanel**：选项演出（飞入/悬停扫光/落选溶解），`choice` 的 UI，
  需要场景有 EventSystem。
- **VNEffectsDemo**：特效演示场景的键盘驱动器（按键触发各组件，
  `UpdateHint()` 维护提示文本）。只属于 VNEffectsDemo 场景，剧本场景不用它。

---

## 九、编辑器工具

| 文件 | 菜单/入口 | 职责 |
|---|---|---|
| VNEffectsDemoSetup.cs | Tools → VN Effects → Create Demo Scene / **Create Script Demo Scene** | 两个场景的一键生成器：搭容器层级、连全部组件引用、建材质/角色/任务资产、写演示剧本。**加新运行时组件后要在这里接线并重建场景** |
| VNScenarioEditorWindow.cs | Tools → VN Effects → Scenario Editor | 剧本可视化编辑器主窗口：行列表 UI、分层添加菜单、Sprite 缩略图浏览器、分类颜色、▶ 从选中行播放（SessionState Bridge） |
| VNScenarioDoc.cs | （数据层） | `.vn.txt ↔ VNRow 列表`双向转换（注释/空行/未知 token 原样保留）、`Validate()` 静态校验、`SourceLineForRow` 行号换算（choice/event 选项行、camseq 路径点都占行） |
| VNScenarioSchema.cs | （模式表） | **命令参数的单一数据来源**：每个命令的位置/kwarg 参数、控件类型（VNParamSource）、默认值。加命令时在这里登记，编辑器 UI 自动长出来 |
| VNCamseqEditorWindow.cs | Tools → VN Effects → （镜头编辑器） | camseq 路径的可视化编辑：Game 视图取点、路径预览、交叉叠化支持 |
| VNCharacterVisualPreviewWindow.cs | Tools → VN Effects → （角色预览） | 角色立绘/头像/眨眼/口型的实时预览与标定，**确认后才写入资产** |

**编辑器铁律**：文本是唯一真相（编辑器状态不落存档）；`say` 的角色/表情走
`VNRow.speaker/expression` 专用字段，`show` 才用普通参数——两条路径不能混。

---

## 十、Shader

| 文件 | 用途 |
|---|---|
| VNImageEffect.shader | 单图特效主 shader（溶解/扫光/HSV/波浪/轮廓光/模糊 9-tap…），VNImageEffectController 的载体。传统 CGPROGRAM（Canvas 不走 URP 光照），保留 UI 裁剪兼容 |
| VNAdditive.shader | 加法混合发光（光环/光束/粒子），HDR 颜色 >1 配合 Bloom 阈值 1.0 出辉光 |
| VNScreenTransition.shader | 全屏转场图案生成（噪声/百叶窗/圆扩散…的数学都在这） |
| VNDirectBackgroundTransition.shader | 背景直切转场（新旧背景在材质内交叉，不经全屏遮罩） |

**发光的公式**：HDR 顶点色会被 uGUI 钳到 1，所以发光=**材质属性**里给 >1 的
HDR 颜色 + 场景 Bloom（阈值 1.0）。想让什么东西发光，走材质别走 Image.color。

---

## 十一、常见任务菜谱（How-To）

### 菜谱一：给剧本加一条新命令

1. `VNScriptParser.Keywords` 加关键字
2. `VNScriptRunner.Dispatch` 加 `case`（要等待→写 `XxxCo` 协程返回；瞬发→返回 null）
3. 命令会改舞台可见状态？→ `RebuildStateBefore()` 加静默重放 case
4. 有开关状态要进存档？→ `VNSaveData` 加字段（带默认值）+ `VNStage` 快照存取
5. `VNScenarioSchema` 登记参数模式（编辑器 UI 自动生成）
6. `VNScenarioEditorWindow.CommandTranslations` 加中文名；新参数来源则
   `VNParamSource` + `OptionsFor` + `VNScenarioDoc.Validate` 三处补
7. 演示剧本/语法速查头注释补一行 → 编译验证 → WhatAiDo 记录

### 菜谱二：写一个新玩法事件模块

1. 新建 `class VNXxxModule : VNEventModule`，在 `OnLaunch(ctx)` 里搭 UI
   （抄 VNQteModule 的 CreateImage/CreateText 辅助），玩完调 `Done("结果名")`
2. 记住三铁律：不碰舞台、unscaled 计时 + `SetUpdate(true)`、全部 `SetLink`
3. 场景里（或生成器里）建禁用模板物体挂上组件，`VNEventRegistry.modules`
   登记 id
4. 剧本直接用：`event 你的id 参数:值` + `* 结果名 -> 标签`
5. 长流程模块记得实现 `CancelForDebug()` 清理场外资源

### 菜谱三：加一首 BGM / 一个音效

1. 音频文件拖进工程 → 场景选中 `VNAudio` → 对应通道库（bgm/se/voice）加条目
2. 填 id（可中文）、拖 clip、**顺手把 volume 滑杆按素材响度标定**
3. 剧本 `bgm play <id>` / `se <id>` 即可；个别场合音量再用 `vol:` 微调

### 菜谱四：加一个任务

1. （可选但推荐）Project 右键 → Create → VN → Quest Definition，填 id/标题/
   各阶段文案，拖进场景 `VNQuestLog.quests`
2. 剧本：`quest start <id>` → `quest stage <id> 2` → `quest done|fail <id>`
3. 分支判断：`if 任务_<id>>=2 jump ...`；J 键随时看日志

### 菜谱五：加一种新特效组件

1. 参考同类组件写（循环效果给 Start/Stop 成对 API；贴图先查
   VNProceduralTextures 有没有现成的）
2. 要接 `fx` 命令：VNStage 加引用 + `Fx()` 路由 + `_fxStates` + 快照三件套
3. `VNEffectsDemoSetup` 生成器里创建/连线 → 重建演示场景
4. Tween 一律 `SetLink(gameObject)`

### 菜谱六：调试剧本

- 编辑器：Scenario Editor 选中行 → **▶ 从选中行播放**（默认重建前置状态；
  choice/jump/event 之后的状态按文件顺序推断，会有警告）
- 运行中看变量：目前看 Console 或存档 JSON；（Flags 监视面板在
  第三十九章待办清单里）
- 音频/状态残留：调试重建自动调 `VNAudio.ResetForDebug()`，别手动调
- 编译验证（Unity 没刷新 csproj 时）：临时把新 .cs 加进
  `Assembly-CSharp.csproj` → `dotnet build Assembly-CSharp-Editor.csproj
  --no-restore --nologo` → **还原 csproj**

---

## 十二、全局约定与坑清单

**必须遵守的约定**
1. 输入只用新 Input System（`Keyboard.current`/`Mouse.current`）
2. 代码补间一律 DOTween，且 `SetLink(gameObject)`
3. 文字全部用 TextMeshPro，字体取 `VNFont.Asset`（编辑期存场景的文字用
   `VNFontAssetBuilder.EnsureFontAsset()`）；禁止 legacy Text / LegacyRuntime.ttf
4. 每张图独立材质实例（VNImageEffectController 管理），别共享材质改参数
5. 发光走材质 HDR 颜色（>1）+ Bloom，不走顶点色
6. 事件模块三铁律（见第六节）
7. 新功能开 `agent/<名>` 分支，合并回 main，**永不删分支**

**容易踩的坑**
- kwargs 值不能含空格；`if` 条件串不能含空格
- 粒子 velocityOverLifetime 三轴曲线模式必须一致
- 运行时创建带 Awake 配置的组件：先 SetActive(false) 挂好赋值再激活
- `VNSaveData` 加字段必须给初始化器默认值（JsonUtility 旧档兼容）
- Sprite 缩略图必须按 `textureRect` 画 UV（图集里的图不能整张 texture 当缩略图）
- 合并分支报 `unable to unlink ... .unity`：Unity 占用场景文件，
  `git clean -f -- <残留文件>` 后重试
- UI 不写深度缓冲：别指望真 DoF/深度后处理，模糊走 VNImageEffect 的 9-tap

**已知技术债（记录在案，别当 bug 修）**
| 债 | 影响 | 出处 |
|---|---|---|
| VNFlags 仅整型、无 and/or/取模 | 日历星期、复杂战斗数值受限 | 三十九章 |
| 事件中不可存档 | 长玩法（>几分钟）体验差 | 四十二章 |
| 结果名精确字符串匹配、无静态校验 | 拼错静默走顺序继续（有警告） | 四十二章 |
| 事件模块 uGUI 承载，重型 3D 需补 additive 场景模式 | 轻量 3D 可用 RenderTexture 过渡 | 四十四章后评审 |
| choice/jump/event 历史无法由行号唯一推断 | 中间行调试按文件顺序近似 | 三十二章 |


// my question:
ia these system 设计得很好或者 互相独立 或者 很 灵活吗 比如我后面想接其他的类型的小游戏,比如说一些3D的小游戏  或者修改小地图 的玩法 或者加上那种日历日期 来推进进度 之类的玩法也可以很灵活的对应吗详细分析好坏


好问题，这值得诚实地拆开讲。结论先说：核心选型（"结果契约 + flags 总线 + 注册表"）对你列的三类扩展都成立，但成立的"顺畅程度"不一样——改地图玩法几乎零阻力，日历系统是顺着现有设计加积木，3D 小游戏接口上成立但需要补一块基建。下面详细分析好坏。

---
一、这套架构真正的好处（为什么说骨架是对的）

1. 剧本与玩法之间的契约极小，这是最大的资产。
剧本只知道三件事：模块 id、传参 kwargs、结果名。它不知道也不关心模块里面是 uGUI、3D、还是视频播放。这意味着：

event 骰子3D
* 大 -> 赢钱
* 小 -> 输钱

不管"骰子3D"内部怎么实现，剧本写法永远不变。**P3 已经验证过这一点：地图模块接入时 Runner 和解析器零改动。**这是判断接口设计好坏的硬标准。

2. VNFlags 作为唯一状态总线，让系统之间"独立但可通信"。
任务、地图去过标记、事件结果互相不知道对方存在，但都读写 flags——所以"去过天台 3 次之后任务推进"这种跨系统逻辑不用写任何胶水代码，剧本一行 if 去过_天台>=3 就完成了。存档也是免费的。

3. 生命周期和输入隔离是完整的。
事件期间快捷键全禁、存档天然被挡、Stop/读档会清理残留模块、快进不影响模块计时。这些边界问题是这类接口最容易烂掉的地方，现在都有明确约定。

4. 各系统确实互相独立。
把 VNQuestLog 从场景里删掉，事件系统照跑；把注册表删掉，任务系统照跑；它们唯一的共享点是 flags（有意为之）。编辑器支持也是集中在 schema 一处登记。

---
二、逐个场景分析你的三个假设

场景 A：改地图玩法 —— ★★★ 几乎零阻力

地图的全部逻辑封在 VNMapModule 一个类里，改玩法 = 改这一个类，不碰任何其他系统：

- 想要大地图拖拽/缩放：在模块里给底图加 ScrollRect，完事
- 想要多张地图：注册表再登记 map_学园、map_街区 各自的模板，剧本换 id
- 想要地点带缩略图/人物头像（谁在这个地点）：Location 加字段 + 渲染改一下

唯一的真实限制：地点是模板 Inspector 里静态配置的。如果你想要"地点列表由数据动态生成"（比如日历系统决定今天哪些地点有人），就得给模块加一个数据源接口。这不难，但要写代码，不是配置能解决的。

场景 B：日历/日期推进 —— ★★☆ 顺着设计加积木，但有两个坑

日历不该做成事件模块——它和任务一样是"持久状态 + 展示"，正确做法完全照抄任务系统的成功模式：

- 状态进 flags：日期=15、时段=2（上午/下午/夜晚）——今天就能用，flag 日期 +1 已经是合法剧本
- 加一个 VNCalendar 组件（对标 VNQuestLog）：日期 HUD 常驻显示 + date next / date 时段 2 命令 + 换日时全屏日期卡演出
- 游戏循环用现有机制拼：label 日循环 → event map 自由行动 → 各地点小剧情 → date next → jump 日循环——这就是养成类游戏的骨架，现有系统已经拼得出来

坑一（真实短板）：VNFlags 只有整型加减和比较，没有取模。想要"星期几"就不能 日期%7，只能维护一个独立的 星期 flag 手动加、到 7 归零——能用但丑。日历玩法一旦变深，就该动 flags 的扩展（第三十九章早就列了这项）。

坑二（更隐蔽）：存档点只在台词行。一个"地图 → 玩法 → 回地图"的日循环如果哪天没有任何台词，玩家就整天没有存档机会。日历玩法上马时需要补一个 savepoint 命令或"回到地图自动存档"，属于小改动但要记得。

场景 C：3D 小游戏 —— ★★☆ 契约成立，承载层要补课

诚实地说，这是三者里当前假设被打破最多的一个：

┌───────────────────────────────────────────┬─────────────────────────────────────────────────────────────────────┐
│                 当前假设                  │                            3D 游戏的冲突                            │
├───────────────────────────────────────────┼─────────────────────────────────────────────────────────────────────┤
│ 模块实例化到 Canvas 下的 EventLayer       │ 3D 物体不该活在 Canvas 下                                           │
├───────────────────────────────────────────┼─────────────────────────────────────────────────────────────────────┤
│ Destroy(module.gameObject) 就算清理完     │ 模块在外面生成的 3D 场景物体不会被顺带销毁，全靠模块自觉在          │
│                                           │ OnDestroy 清                                                        │
├───────────────────────────────────────────┼─────────────────────────────────────────────────────────────────────┤
│ 同步 Instantiate 模板                     │ 重型 3D 资源同步加载会卡顿一帧到数帧                                │
├───────────────────────────────────────────┼─────────────────────────────────────────────────────────────────────┤
│ 主相机在渲染 VN 的 Screen Space-Camera    │ 3D 游戏需要自己的相机/渲染层管理                                    │
│ 画布                                      │                                                                     │
└───────────────────────────────────────────┴─────────────────────────────────────────────────────────────────────┘

轻量 3D 今天就能做：模块类照常挂在 EventLayer（当控制器），OnLaunch 时在场外生成 3D 内容 + 专用相机渲到 RenderTexture，贴回自己的 UI 上，OnDestroy 里清干净。转骰子、抓娃娃机这个量级没问题。

重型 3D 需要补一块基建（41.4 当时明确"暂缓"的那块）：给注册表条目加 sceneName 场景模式——EventCo 检测到场景型条目就 additive 异步加载场景、在场景里找模块、结束后 Unload。工作量中等（一两个下午），且完全不破坏现有剧本和已有模块，因为剧本契约没变。这正是当初把"实例化方式"藏在注册表后面的原因：承载方式可以换，接口不用动。

---
三、还有几个跨场景的诚实短板（你没问但该知道）

1. 事件中途不能存档。QTE、地图这种 30 秒的事件无所谓；如果哪天做 20 分钟的 3D 关卡，玩家中途退出就全丢——那时要么模块自己存内部进度，要么扩展存档协议，是笔真实的债。
2. 结果名是精确字符串匹配。模块返回 win 而剧本写 Win 就静默走"顺序继续"（有警告但游戏不停）。lint 目前查不了"模块会返回哪些结果名"，将来可以让模块声明结果名清单给编辑器校验。
3. 事件里不能调用对话系统。约定模块不碰舞台，所以 3D 小游戏里想插一句角色吐槽（"再试一次嘛！"）没有现成通道，得模块自绘 UI。将来若普遍需要，可以开一个受控的"事件内旁白" API。
4. 调试重建把事件当黑盒。日历类"每天一堆事件"的结构下，"从选中行播放"的状态推断会越来越弱——重度依赖时应该转向"从存档快照启动调试"（三十二章遗留的已知方向）。

---
四、总评

┌────────────────────────────────────────┬────────────────────────────────────────────────────────────────────────┐
│                  维度                  │                                  评价                                  │

┌────────────────────────────────────────┬────────────────────────────────────────────────────────────────────────┐
│                  维度                  │                                  评价                                  │
├────────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ 系统间独立性                           │ 好。只通过 flags 通信，删掉任何一个系统其他照跑                        │
├────────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ 加同类玩法（2D                         │ 很顺，一个类 + 一行登记                                                │
│ 小游戏、新地图、新面板）               │                                                                        │
├────────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ 日历/养成循环                          │ 骨架现成，需补 VNCalendar 小组件；深了要还 flags                       │
│                                        │ 整型限制和存档点两笔债                                                 │
├────────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ 3D 小游戏                              │ 接口成立、承载要补：轻量今天可做，重型需要注册表场景模式 + 异步加载    │
├────────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ 最值得警惕的债                         │ flags 只有整型、事件中不可存档、结果名无静态校验                       │
└────────────────────────────────────────┴────────────────────────────────────────────────────────────────────────┘

一句话：这套设计把"会变的东西"（玩法内容）和"不变的东西"（剧本契约、状态总线）分对了边，所以你列的扩展都不需要推倒重来；代价是承载层（3D/场景模式）和数值层（flags 表达力）留了两块明确的待补区——它们当时是刻意暂缓而不是没想到，账都记在 WhatAiDo 第四十一、三十九章里。

如果你近期真有 3D 小游戏或日历玩法的计划，我建议的动工顺序是：先做 VNCalendar（小、独立、马上能用），3D 承载模式等第一个真实 3D 需求出现再补——避免为想象中的需求写基建。