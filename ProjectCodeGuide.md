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
  VNEntranceAnimator + VNCharacterEmotes + VNCharacterBlink + VNCharacterBlinkOverlay +
  VNCharacterMouth + 光环 + 脚影` 的完整堆叠
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
  对话头像参数、眨眼配置（方式 `blinkMode` + 整张闭眼立绘 / 透明闭眼叠加图 + 间隔）、
  口型配置（张嘴图/间隔/仅默认表情）。
- **枚举 `VNBlinkMode`** 也定义在本文件：`FullSprite=0`（整张替换，缺省，
  保证旧资产行为不变）/ `Overlay=1`（眼部叠加层）。便捷属性 `ActiveBlinkSprite`
  按当前方式返回实际用到的那张图。
- **扩展**：给角色加新的"每角色可配"能力时字段加在这里，然后在
  `VNCharacterVisualPreviewWindow` 里补预览 UI（保持"确认后写入资产"流程）。

### VNCharacterBlink.cs / VNCharacterBlinkOverlay.cs / VNCharacterMouth.cs（`Script/`）

- **职责**：自动眨眼两种实现 / 说话口型（台词+语音期间在叠加子 Image 上随机开合张嘴图）。
- **眨眼两条路径互斥，由 `VNCharacterDef.blinkMode` 二选一**，两个组件都会挂在角色上，
  各自在 `CanBlink` 里检查 `blinkMode` 后决定是否工作：
  - `VNCharacterBlink`（`FullSprite`）：默认表情下随机间隔把 `Image.sprite`
    换成完整闭眼立绘再换回。素材要整张像素级对齐。
  - `VNCharacterBlinkOverlay`（`Overlay`）：底图不动，开关一张只有眼部有像素的
    透明闭眼图，做法与口型层完全一致。素材只要画布尺寸/Pivot 一致即可。
  - 间隔与闭眼时长字段（`blinkIntervalMin/Max`、`blinkDuration`）两者共用。
- **共同设计**：整张透明画布叠加、与主体共享 `VNImageEffectController.Mat`
  材质（溶解/调色同步）、DOTween 随机间隔驱动。
- **接线**：`VNStage.CreateCharacter()` 挂三个组件；`ApplyExpression()` 必须对
  blink / blinkOverlay / mouth **三个都**调 `PrepareForExpressionChange()`
  与 `SetExpression()`，漏一个就会有旧帧被卷进表情交叉溶解。
- **扩展提示**：这套"透明画布叠加层"就是漫符系统（下条）的蓝本。
  再要加"分表情闭眼图"就把 `blinkOverlaySprite` 扩成 `表情名 → Sprite` 列表。

### VNCharacterMarks.cs（`Script/`）—— 立绘漫符

- **职责**：`mark` 命令的执行体。在角色 GameObject 下建子 Image 弹出漫画符号
  （汗滴/井字怒气/感叹号/问号/爱心/音符/红晕/灯泡/省略号/眩晕星/蒸汽）。
- **符号图**：`VNProceduralTextures.MarkSprite(kind)` 程序化生成
  （硬边符号走 4×4 超采样 + 形态学膨胀描边；红晕/蒸汽走柔边 alpha 曲线）；
  角色资产 `markSprites` 里配了同名 Sprite 时优先用自定义图。
- **定位**：`VNCharacterDef.markAnchor` 归一化偏移（`(0,0)` = 立绘中心），
  剧本 `pos:x,y` 临时覆盖；尺寸 = 立绘高 × 0.15 × `markScale` × `size:`。
- **共享材质**：与嘴部叠加层同样共用 `VNImageEffectController.Mat`，
  所以出场溶解/退场淡出/调色自动带上漫符，不需要单独处理退场。
- **timing 取舍**：`Show()` 返回的 Sequence **只含弹出段**（约 0.28 秒），
  停留与消失走独立的 `DOVirtual.DelayedCall` —— 这样既守住"命令默认同步、
  `@` 异步"的全局语义，又不会让一次性符号把对白卡住一秒半。
- **状态归属**：`keep` 符号 → `VNSaveData.CharSave.marks`（英文正名逗号串）；
  一次性符号不是持续状态，存档与调试重建都忽略。
- **名称解析**：`TryParse` 接受英文正名与中文别名，`CanonicalNames`
  是编辑器下拉与校验器的单一真相 —— 加新符号只改这一处 + 枚举 + 形状函数。

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
- **VNFontAssetBuilder**：菜单 Tools → VN Effects → 字体 Fonts → 生成 TMP 字体资产 Create TMP Font Asset，
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
- **VNLocalizationTools**：Tools → VN Effects → 本地化 Localization。
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

> **铁律 1 的唯一破例：`VNAiTalkModule`（一〇六章）。** AI 控制立绘表情就是改舞台，
> 自绘立绘要把眨眼 / 口型 / 色调匹配 / 出场动画全部重接一遍，代价远大于收益。
> 破例的代价被这样收住：**只碰表情和对话框内容**（绝不碰位置 / 缩放 / 背景 / 特效），
> 且进入时记下原表情，**正常结束、ESC 退出、`CancelForDebug` 三条路径都还原**。
> 要再破例请照这个标准——先证明自绘的代价，再把影响面关进模块自己的生命周期里。

> **射线优先级**：EventLayer 排序 **60** 在 ChoicePanel **45** 之上。模块若想复用
> `stage.choicePanel` 显示选项（`VNAiTalkModule` 就是这么做的），自绘的一切必须
> `raycastTarget = false`，否则会把选项点击全部吃掉——它盖在选项面板上面。
> 需要独占输入的东西（确认框）才单独打开。

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

### VNBattleModule.cs —— 示例模块③：回合制小战斗（事件接口 P4）

- 剧本 `event battle enemy:xx ehp:26 eatk:5 php:30 patk:6 pdef:1 escape:50`，
  结果固定中文 `胜利/失败/逃跑`（escape:0 隐藏逃跑按钮）。
- **养成联动的桥全在 flags**：`patkstat:体力` → 攻击改读 flag「体力」
  （同理 `phpstat`/`pdefstat`），模块不认识任何具体属性名；
  结束写 flag`战斗剩余HP`，供剧本做车轮战（`phpstat:战斗剩余HP`）/伤势分支。
- 回合流程：我方 攻击(10% 会心)/重击(65% 命中 1.8×)/防御(减伤 60%)/逃跑 →
  敌方反击(±30% 浮动、15% 猛攻)。伤害飘字/受击白闪抖动/胜负横幅全程序化，
  敌人 = 光晕色块 + 双眼，零素材依赖。1234 键 = 四个按钮。
- 示例剧本 `Assets/Scenarios/BattleDemo.vn.txt`（固定数值/属性联动/车轮战三连）。

### VNQuizDef.cs / VNQuizModule.cs —— 示例模块④：限时问答（八十八章）

- 剧本 `event quiz id:社团常识 count:3 time:15 pass:2 [pick:3,4] [title:] [flag:]`，
  结果固定中文 `全对/及格/失败`；成绩写 flag `<前缀>正确数`、`<前缀>总数`
  （前缀默认取题库 `flagPrefix`，剧本 `flag:` 可覆盖 → 多套题库互不覆盖）。
- **题目全在资产**：`VNQuizDef.Question`（三语题干 / 2~4 个选项 / `answerIndex`（0 起）/
  解析 / 单题 `timeLimit` / `rewardOnCorrect` / `penaltyOnWrong`）。
  `ValidQuestions()` 过滤填一半的题——坏题只是不出，不会让事件卡住。
- 选题：`pick:` 指定题号（**按资产原始顺序数**，坏题也占号，所见即所得）优先，
  否则从有效题里随机抽 `count` 题不重复。
- 计时：`Update` 里 `Time.unscaledDeltaTime`；最后 3 秒的变红/脉动/轻抖在
  `RefreshTimer()` 里按 `Time.unscaledTime` **现算，不开 Tween**（每帧新建会堆积）。
  倒计时归零 = 走 `Answer(-1)`，与答错同一条路径（含扣属性）。
- 属性联动：逐题的 StatOp 复用 `VNShopDef.StatOp`，统一走 `VNStatsHud.Apply`
  （钳制+飘字），没有 HUD 时退回 `VNFlags.Add`。模块同样不认识任何具体属性名。
- 输入双通道：选项按钮 + 数字键 1~4（`TryAnswer` 会挡掉本题不存在的序号）。
- 示例题库由生成器 `EnsureQuizDef()` 造在 `Assets/VNEffects/Quizzes/`；
  示例剧本 `Assets/Scenarios/QuizDemo.vn.txt`（随机抽题/指定题号/成绩 flag 细分）。
- **扩展**：想打乱选项顺序，在 `ShowQuestion()` 里洗一次显示索引映射、
  `Answer()` 换算回原始下标即可，是加法不是改法。
- `Editor/VNQuizInstaller.cs`（Tools → VN Effects → 场景装机 Install To Scene → 限时问答 Quiz Module）：
  **增量装进当前场景**，不走 `Create Script Demo Scene` 的 `NewScene(EmptyScene)` 重造，
  手工整理过的 Hierarchy 不会丢。模板物体**必须带 RectTransform**
  （`BuildUi` 直接 `(RectTransform)transform`）——这正是不该手工接的原因。
  支持 Undo、重复执行安全；新增事件模块时照抄这个文件即可。

### VNBadminton*.cs —— 示例模块⑤：羽毛球对战（一〇二章）

从参考项目 `Student Age new` 的 `BadmintonMiniGameView`（1263 行反编译源码）复刻。
那份来源**只有代码没有美术**，所以是照逻辑重写，不是搬运。

**五个文件的分层（这是这个模块最值得抄的地方）**

| 文件 | 层 | 允许依赖 |
|---|---|---|
| `VNBadmintonBallistics.cs` | 纯数学 | 只有 `UnityEngine` 的 `Mathf/Vector2`。**无 MonoBehaviour、无 UI**，可单测 |
| `VNBadmintonUi.cs` | UI 原语 | uGUI。含 `VNBadmintonQuad`（uGUI 画不出梯形） |
| `VNBadmintonCourt.cs` | 场景与 HUD | UI 原语。不是 MonoBehaviour，由模块 new 出来持有 |
| `VNBadmintonActor.cs` | 角色表现 | UI 原语。**换真动画只改这一个文件** |
| `VNBadmintonModule.cs` | 玩法 | 以上全部 + `VNFlags` / `VNBadmintonDef` |

- **弹道是解析式抛物线**：`BuildArc` 用「起点 + 落点 + 球网处过网高度」三点定
  `y=ax²+bx+c`，每帧 `x += speed·dt`。球路一解出来，落点 / 顶点 / 对手该跑到哪里
  就能全部反解——AI 不需要预测、轨迹虚点不需要模拟。**改玩法前先理解这一点**。
- **不用 Physics2D**（与参考实现的关键偏离）。改纯数学距离判定，
  **代价是 `StepBall` 必须子步进**：单步位移上限 12px、上限 16 步。
  扣杀球 1350+ px/s，30fps 单帧位移 45px 而拍面半径只有 105px，
  不切小步低帧率时球会**直接穿过球拍**。改速度上限时必须同步复核这个比例。
- **判定几何与动画分开**：`RacketPointFor(kind)` 返回固定几何，不随挥拍动画角度走。
  判定要可预测、帧率无关；动画只负责「看起来打到了」。**别把它们接起来**。
- **★ 坐标换算的隐藏项**：参考实现 2560×1440 → 本专案 1920×1080 全部 ×0.75，
  但 `flySpeed` 还要**再乘 √0.75 = 0.866**（`tuning.flySpeedScale`）。
  坐标缩放 k 后 `a→a/k`，速度只会变 √k 倍，而飞行时间不变需要 k 倍。
  少这一下球快 15%，数值全对但手感就是不对。详见《羽毛球小游戏实施计划.md》第四节。
- 难度 / 对手 / 台词 / 立绘 / 音效全在 `VNBadmintonDef`；解析三级
  （剧本 `id:` → 剧本 `vs:` 同名资产 → 库里只有一条时直接用）。
- 养成联动照 `VNBattleModule.patkstat` 范式：`powerstat/speedstat/jumpstat` 指定读哪个
  flag，`ApplyTuning()` 在 `CopyFrom(def.tuning)` **之后**叠加加成——
  顺序反了会被编辑器每帧重读冲掉。
- 音效 `VNBadmintonSfx` 全部 `AudioClip.Create` 合成（指数包络 × (正弦 + 低通白噪)），
  Def 填了真音效逐条覆盖，音量跟随 `VNAudio.seVolume`。
- `debugAutoPlayer` 开关：让玩家也交给 AI 自动对拉，验证回合逻辑用。
- `Editor/VNBadmintonInstaller.cs` 与 `VNQuizInstaller` 同款增量装机。
- **坑**：`Graphic` 子类必须自己再写一遍 `[RequireComponent(typeof(CanvasRenderer))]`，
  基类那条不会被 `AddComponent` 走继承链读到——不写的症状是「一切状态正常但就是不画」。

### VNAi*.cs —— 示例模块⑥：AI 自由聊天（一〇六章）

四层拆分，每层只干一件事，换任何一层不动其他层：

| 文件 | 职责 |
|---|---|
| `VNAiKey.cs` | Key 三级回退读取（环境变量 → 仓库外 → 仓库内），只在内存缓存、永不打印 |
| `VNAiClient.cs` | **全项目唯一碰 HTTP 的文件**。换模型 / 换供应商 / 改走自建中转只动它 |
| `VNAiPricing.cs` | **全项目唯一算钱的文件**（一一三章）。单价按模型查表、可用资产覆盖。`VNAiResult.model` 由 `Send` 回填——没有它就无从按模型计价 |
| `VNAiPersonaDef.cs` | 人格资产：性格、边界、白名单、模型参数、兜底台词 |
| `VNAiConversation.cs` | **纯逻辑（无 MonoBehaviour，可单测）**：提示词组装 / schema 生成 / 历史裁剪 / 解析钳制 |
| `VNAiTalkModule.cs` | 表现层：只负责「拿 BuildRequest 的结果去发、把 TryParseTurn 的结果去演」 |

- 剧本 `event aitalk vs:星野结衣 [persona:] [turns:] [topic:] [place:] [me:] [stat: rate:] [flag:]`，
  结果 `好感提升/普通/冷场/失败`（按累计 `affection_delta` 判）。
  **`* 失败` 是断网 / 无 key / 被内容安全拦下的出口，剧本必须接住**，
  Lint 规则 `aitalk-no-failure-branch` 会查。
- **Parser / Runner 一行没改**：`event` 本就是关键字、kwargs 走通用解析。
  更关键的是 `EventCo` 等模块用的是 `while (result == null) yield return null` 纯轮询——
  等 1.5 秒网络请求的模块和等玩家点按钮的模块对 Runner 完全一样，不阻塞主线程。
  **这是本项目接任何异步玩法（联网、下载、TTS）的通用姿势。**
- **结构化输出是设计核心**：一次请求拿齐 台词 / 表情 / 漫符 / 好感变化 / 三个选项 / 是否收尾。
  `emotion` 与 `mark` 的 enum **从角色资产实时生成**，AI 物理上编不出不存在的表情名。
- **演出全部复用现成的**：`VNStage.Say(id, expr, text)` 一个调用带齐表情切换、
  说话者高亮、头像、名牌配色、打字机、口型；三个候选回复直接走 `stage.choicePanel`，
  飞入 / 悬停扫光 / 落选溶解白赚。代价是破了铁律 1（见本节开头的破例说明）。
- 协程而非 async/await：与项目现有 `IEnumerator` 风格一致，也避免 Unity 主线程坑。
- `Editor/VNAiConnectionTester.cs`：两级自检（网络 / 逻辑层），**不进 Play Mode**。
  前者过、后者挂 = 问题在提示词或人格配置。
- `Editor/VNAiTalkInstaller.cs`：与 `VNQuizInstaller` 同款增量装机，
  额外做 key / 人格 / 表情三项体检；拆了 `Install(bool interactive)` 供自动化调用。
- `Editor/VNAiStudio*.cs`：**AI 试聊台**（一一二章），调人格与提示词的主力工具，
  详见第九节。这一层能存在完全是因为 `VNAiConversation` 没继承 MonoBehaviour——
  提示词组装 / schema / 解析全部在 EditMode 直接可跑。**改那个文件时别把它拖回
  MonoBehaviour**，会同时废掉试聊台和自检菜单。
- **扩展位**：`VNAiContext.memory` 字段已预留，接跨场景记忆时把往期摘要塞进去即可，
  提示词层不用改（存储仿 `VNCgUnlocks` / `VNPhotoAlbum` 的全局 JSON）。

### VNFog*.cs —— 示例模块⑦：擦雾（一三一章）

六个文件，前两个是**纯逻辑层**（无 MonoBehaviour），编辑器调参窗口跑的是同一份代码：

| 文件 | 职责 |
|---|---|
| `VNFogMask.cs` | 掩码缓冲：擦除 Stamp / 线段补点 / 边缘权重图 / 回雾衰减 / 清晰度求和 |
| `VNFogScore.cs` | 阶段推进（只升不降）+ 三档判定（按历史峰值）。**纯静态可单测** |
| `VNFogWipeDef.cs` | 资产：雾外观九参 / 笔刷 / 回雾五参 / 三档门槛 / 分阶段台词 / 音效覆盖 |
| `VNFogWipeModule.cs` | 表现层：自绘底图与雾层、输入轮询、HUD、台词条、倒计时、ESC、结算 |
| `VNFogSfx.cs` | 四个代码合成音效 |
| `VNFogTextures.cs` | 四种道具光标的程序化 SDF 贴图 |

- 剧本 `event wipefog id:<定义> cg:<要擦的CG> [time:] [target:%] [perfect:%] [vs:] [stat: rate:] [flag:]`，
  结果 `完美/普通/失败`。**★ 剧本不能在 event 之前先 `cg`**——雾要到 `OnLaunch` 才铺得出来，
  先 cg 会让谜底在开始擦之前就揭晓；Lint 规则 `wipefog-cg-before-event` 会查。
- **本模块不破模块三铁律**（与 aitalk / interact 相反）：角色在这个玩法里是**被动 CG**，
  不换表情、不碰立绘，全部绘制都在自己的 UI 子树内，因此也**不进存档**、无需 vn-save-compat。
- **事件层 60 盖住对话框是必然的**（雾要铺满整屏）。解法不是让雾避开对话框，而是
  **模块自己铺一份清晰 CG 打底**，画面完全自洽；台词改用自绘的台词条。
  这一条与 `VNInteractionModule`「不铺全屏暗幕」的解法正好相反——那个玩法全程要角色说话
  且不需要盖住画面，这个玩法的本体就是盖住画面。
- **掩码分辨率 384×216**，不是全屏。雾本身就是模糊的，双线性放大 5 倍看不出来；
  换来每帧全图遍历只有 8 万次运算、上传 81KB、清晰度统计顺手求和。
  **改分辨率必须保持 16:9**——`384/1920 == 216/1080` 这个等式一破，屏幕上的正圆笔刷
  在掩码里就变成椭圆了。
- **内部是 `float[]` 而不是直接操作 `byte[]`**：回雾每帧减约 0.0005，byte 精度会被整数
  截断完全吃掉，症状是「雾根本不回来」。
- **★ Stamp 的擦除累加规则**（改这个函数前必读，它同时踩住两个坑）：
  一笔之内对同一像素取 max、笔与笔之间才累加，用 `_strokeMark[]` / `_strokeBase[]` 记住
  「这一笔碰到该像素之前的值」实现。直接 `+=` 会让沿线段补点把羽化带填满、边缘退化成
  硬边（放大后是方块阶梯）；单纯取 max 又会让 `wipeStrength` 变成永久天花板、
  「反复擦会越来越透」再也不会发生。
- **难度是算得出来的**：`每秒擦除面积 ≈ 笔刷直径 × 鼠标速度`，减去回雾速率即净推进。
  `Editor/VNFogTuneWindow.cs` 就是把这个公式做成窗口，**不进 Play Mode** 直接给出预计
  通关秒数与手感评语。本玩法刻意不做速度惩罚，难度唯一来源就是「笔刷面积 vs 回雾速度」，
  手感全压在参数上，所以这个窗口是**刚性需求而不是加分项**。
- `Editor/VNFogWipeInstaller.cs`：与 `VNQuizInstaller` 同款增量装机。
- **易漏点**：`VNFogMask.Destroy()` 必须按 `Application.isPlaying` 分路——
  这个类编辑器也在用，`Object.Destroy` 在编辑模式下只打一条 error 什么都不销毁，
  症状是每关一次调参窗口泄漏一张贴图。同理 `VNTouchCursor.Dispose()` 四处都要调。
- **CG 解锁**：`VNCgUnlocks.Unlock` 本来是 `VNStage.ShowCg` 顺手做的，推荐写法下剧本不再
  提前 `cg`，所以模块在结算时补一次（擦到 20% 以上才算看过）。

### VNSnsView.cs / VNSnsMessage.cs —— SNS 手机聊天（九十章）

- **不是事件模块**，是对话的另一种呈现层：`sns open` 之后 `SayCo` 分流到 `SnsSayCo`，
  台词渲染成气泡。因此存档点/`if` 分支/`flag`/翻译表全部沿用普通台词的机制，
  聊天中途可以随便存档 —— `event` 是原子的，做成模块就存不了。
- 命令入口 `VNScriptRunner.SnsCo`（open/close/voice/image/typing/read/time/system/reply），
  `sns reply` 复用 choice 的 `*` 子行（Parser 里第三个使用者）。
- **左右判定**：`VNSnsView.IsPlayerSender(sender, alias)` —— `me/我/玩家/主角` +
  `sns open` 的 `me:` 别名算自己（右侧），其余左侧。静态方法是为了让调试重建共用同一规则。
- **布局全手工**：不用 `VerticalLayoutGroup`/`ContentSizeFitter`（TMP 的
  `preferredHeight` 同帧不可靠、气泡还要自适应宽度）。
  `GetPreferredValues(text, MaxBubbleW, 0)` 当场量 → 定气泡尺寸 → `Layout()` 自己排 y。
  加新气泡类型 = 写一个 `BuildXxxRow` 返回行 RectTransform，其余不用动。
- **状态进存档**：`VNSaveData.snsOpen/snsPeerId/snsSessionId/snsTitle/snsPlayerAlias/
  snsMessages`；`VNStage.CaptureSnapshot/RestoreSnapshot/ClearStage` 三处接线。
  调试重建（`RebuildStateBefore`）把 sns 命令与 SNS 期间的台词折算进同一份 `snsMessages`，
  读档与调试走同一条重建路径。
- **消息存显示文本不存原文**：翻译表按"出现序号"匹配 key，脱离命令流无法单句反查。
- Canvas `sortingOrder = 300`：盖住对话框/事件层，低于存读档面板（600）。
  气泡上的 Button 让 `IsPointerOverInteractiveUi` 自动挡掉误推进。
- **扩展方向**（数据结构已留位）：跨章节永久聊天记录（消息带全局 id + sessionId）、
  群聊（sender 已是任意角色 id）、随时可开的手机（把 Open 挂到按键 + 未读红点）。

### VNQuestDef.cs / VNQuestLog.cs —— 任务系统

- **状态即 flags**：`任务_<id>` = 0 未接取 / 1..n 进行中 / 100 完成 / -1 失败。
- `VNQuestDef` 资产只管文案（标题/描述/各阶段目标）；**没建资产的任务
  照常运作**（id 当标题）。
- `VNQuestLog`：执行 `quest start|stage|done|fail <id> [阶段]`（写 flag +
  VNToast），J 键日志面板（进行中/完成/失败三栏，无资产的活动任务兜底显示）。
- **扩展**：加"任务追踪 HUD"之类，只需读 flags + defs 渲染，不用碰命令。

### VNEquipment.cs —— 装备系统（八十五章）

- **纯静态类，状态全在 flags**：`装备_<道具id>`=部位编号（1~7 见 `VNEquipSlot`）、
  `装备实增_<部位>_<属性>`=穿上时实际生效增量（卸下按记录扣回，钳制安全）、
  `装备效果_<效果id>`=特殊效果合计（穿脱后 `RecomputeEffects()` 整体重算）。
- 道具数据 = `VNShopDef.Item` 的装备/使用字段（equipSlot/statBonuses/passiveEffects/
  useOps/consumeOnUse）；查表入口 `ItemResolver` 由 VNInventory 在 Awake 注入。
- **特殊效果不在代码里生效**：只写 flag，由剧本 `if 装备效果_xx>=1` 判定（哲学同概率表）。
- 入口：`Equip(item)`（同部位自动静默卸旧）/ `Unequip(id)` / `Use(item)`（走 VNStatsHud.Apply
  钳制+飘字，耗尽自动卸下）/ `HandleItemLost(id)`（商店卖出后调，卖光强制卸下）。
- **扩展**：想内建效果 hook（如金钱加倍自动翻倍 stat），在对应结算代码读
  `VNFlags.Get(VNEquipment.EffectFlagName("金钱加倍"))` 即可，别绕过 flags。

---

## 七、系统 UI

| 脚本 | 职责 | 要点 |
|---|---|---|
| VNSaveSystem.cs | 存档读写（静态类） | 20 槽 JSON + PNG 缩略图存 `persistentDataPath`；`VNSaveData` 是唯一存档结构——**加字段要给默认值**（旧档兼容靠字段初始化器） |
| VNSaveLoadPanel.cs | F5/F9 的 20 槽界面 | 截图缩略图/时间/末句台词；覆盖确认弹窗；打开时暂停 DOTween |
| VNConfigPanel.cs | 设置面板 | 三路音量/文字速度/自动速度/显示模式，PlayerPrefs 持久化，启动时 Runner 调它回放设置 |
| VNQuickToolbar.cs | 对话框右下功能条 | Save/Load/Auto/Skip/Log/任务/Config/隐藏UI；挂在对话框 Canvas 下排序 +1；**加按钮记得改总宽**（现 693） |
| VNBacklog.cs | H 键回想 | 独立 Overlay Canvas 600；`Record()` 由 SayCo/ChoiceCo/EventCo 调 |
| VNToast.cs | 左上角堆叠卡片 + 右上角模式角标 | 静态 `Show(msg)` / `Show(msg, icon, iconColor, accent, hold)`；**多条排队不覆盖**（新卡占最上格、旧卡下移，上限 5 张超出提前退场）；卡片宽度跟文字走（`GetPreferredValues`，`preferredWidth` 要等一次布局才有值）；全部 Tween `SetUpdate(true)` 不受 Skip 变速影响；任务/存档/装备/属性变动都走它 |
| VNQuestLog.cs | J 键任务日志 | 见第六节；UI 结构与 Backlog 同构 |
| VNInventory.cs | I 键背包 | 左道具列表+右 7 装备格+介绍区；右键菜单走 `ClickRelay`（IPointerClickHandler 区分左右键，Button 只管按压视觉）；皮肤 prefab（`VNSystemUiSkinSet.inventoryPrefab` + VNInventorySkin/RowSkin/SlotSkin 槽位）优先，缺失退回程序化 UI，**两条路径都产出 VNInventorySkin 引用**，下游单一代码路径；装备逻辑全部委托 VNEquipment |
| VNDialogueSkin.cs / VNChoiceSkin.cs | UI 皮肤槽位声明 | 挂皮肤 prefab 根；VNDialogueBox/VNChoicePanel 实例化后按槽位 Bind()，行为逻辑与装饰节点解耦；全槽位可选（留空=降级）；头像避让声明式（portraitBodyInset/TagShift）；`ui dialogue\|choice <id>` 切换、id 在 VNGameConfig 登记、状态进存档；**程序化默认也走同一条 Bind 路径**（DefaultSkin 子物体），改行为逻辑两边同时生效 |
| VNUiSkinExporter.cs (Editor) | 皮肤起步模板导出 | 烘焙程序化贴图为 PNG（prefab 无法引用运行时贴图）+ 生成默认/顶部/右列四个 prefab + 自动登记 VNGameConfig；重复执行安全 |
| VNTitleMenu.cs | 开始菜单（标题画面） | 同场景覆盖层 Canvas 500（画廊 600/存读档 900 之下）；Runner.Start 发现它且 showOnStart 时跳过 playOnStart；开始=清 flags 播入口剧本、继续=读最新槽（含快存 0）、读档/鉴赏/设置直接复用现成面板；**ResumeAt 必调 NotifyGameplayStarted 收层**——任何入口开播都不会残留标题；标题文字/背景/BGM 配在 VNGameConfig「标题画面」区 |

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
  - **分层调色合并层**（`SetGrade` / `DOGradeField` / `ClearGrade` / `GetGrade`）：
    `_Brightness` / `_Saturation` 被六方共用 —— 说话者高亮（**每句台词都改**）、
    伪景深、情绪动作、退场动画、天气联动、情绪色调。直接写是谁最后写谁赢，
    症状是「说一句话立绘颜色就跳回去」。现在每个来源占一个 `VNGradeLayer`
    通道（Mood / Weather / Focus / Emote / Manual），本层合并后统一补间：
    **滤镜相乘、色相相加、饱和/亮度/对比度相乘**。值类型见 `VNGrade.cs`。
    老 API（`SetHSV` / `DOBrightness` / `DOSaturation`）保留并改走 `Manual`
    兜底通道 —— 没改到的调用点自动降级到独立通道，不会冲掉 mood，但**新代码
    别再用它们**。调色补间刻意不挂 `SetTarget(this)`，免得被
    `OnDestroy` 与情绪动作的批量 `DOTween.Kill(this)` 连坐。
- **VNEntranceAnimator**：出场预设×10 + 退场×4 + `StartIdleEffects`
  （出场完自动开呼吸/悬浮）。组合 ImageEffectController + CanvasGroup + RectTransform。
  - 日常向 `Crossfade`(默认)/`SlideIn`/`StepIn`/`WalkIn` —— 无粒子无光环，
    `IsCasual()` 判定，登场后**不开周期扫光**（差别进存档，见 `CharSave.casualEntrance`）。
  - 华丽向 `DissolveGlow`/`FadeSlideUp`/`ScaleBounce`/`ShineReveal`/`FlashBloom`/
    `AfterimageDash`；退场 `Fade`(默认)/`Dissolve`/`RunOut`/`Sink`。
  - `VNSide` 方向由 VNStage.SideFor 按站位推断（剧本 `from:` / `to:` 可覆盖）；
    `BaseDuration()` 基准时长表把剧本的 `dur:秒` 换算成倍率。
  - **加新预设**：写一个 `BuildXxx(k, side)` 返回 Sequence → 枚举加一项 →
    `BaseDuration` 补一行 → Schema 的候选自动来自枚举反射，只需在编辑器的
    `EntranceTranslations` 补中文名。位移类预设记得在 `OnComplete` 归位
    旋转/缩放（`PrepareHidden` 只兜底一部分）。
- **VNProceduralTextures**：静态贴图工厂（柔圆/四芒星/光晕/光束/花瓣/圆环/
  圆角面板/描边框…全部代码生成、缓存、`hideFlags=DontSave`）。**零美术依赖
  的基石**——新 UI/粒子先来这里找现成贴图。

### 8.2 角色附属

- **VNGlowBackdrop**：立绘背后光环脉动（Additive shader）。
- **VNFootShadow**：脚下椭圆影，跟随横移/悬浮高度/溶解度联动。
  `Impact(strength, duration)` = 落地冲击（横向摊开+纵向压扁再缓回），
  由 `stepin` 登场在落地那一帧调用；冲击倍率在 LateUpdate 里乘进 localScale，
  不干扰原有的悬浮/淡入联动。
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
  ⚠ `Preset.Petals` 已弃用（加法混合 + 单一贴图 + 全局噪声，就是「白色纸片同步飘」的老实现），
  花瓣/落叶一律改用下面的 VNFoliageSystem。
- **VNFoliageTextures**：五种叶型（樱花/枫/银杏/阔叶/竹叶）的程序化图集。
  布局是**列 = 12 翻转帧、行 = 4 形态变体**，RGB 存明暗（叶脉/折痕/根深尖浅/背面压暗）、
  A 存形状，色相不入贴图 → 一张图集适用任意颜色。
  改形状只改 `Sakura()/Maple()/Ginkgo()/Broadleaf()/Bamboo()` 五个纯函数。
- **VNWeatherDef**：飘落天气的全部参数（ScriptableObject）。五套内置预设走
  `CreateBuiltin()`，**不建任何资产也能用**；自定义资产登记进 `VNGameConfig.weatherDefs`。
  新增参数字段时记得同步 `CopyFrom()` 与 `EnsureLayers()`。
- **VNFoliageSystem**：三层景深飘落系统。六件事都在这里：
  Alpha 混合（`VN/ParticleAlpha`）、图集翻转（`SingleRow + rowMode Random`）、
  **每粒子独立相位横摆**、全局阵风、尺寸↔速度伪透视、地面堆积。
  横摆写成「已存活时间的纯函数」只加相邻两帧之差，**完全无状态** ——
  改这块时千万别改成需要平行数组记录上帧状态的写法，粒子死亡重排会立刻错位。
- **VNWeatherController**：天气总控，两套后端并存 ——
  飘落类走 VNFoliageSystem，雨/雪/萤火虫仍走 VNAmbientParticles。
  入口 `SetWeatherId(id, …)` 三级解析：自定义资产 id → 内置叶型别名（含中文）→
  `VNWeather` 枚举名，所以旧存档里的 `"Petals"`/`"Rain"` 不需要迁移。
  存档取 `CurrentId`（字符串）而不是 `Current`（枚举）—— 新叶型在枚举里统统算 Petals。
- **VNMoodGrading**：八种情绪色调，**分层调色**（不是全屏后处理）。
  单相机 + 单个 Screen Space - Camera 的 Canvas 下，Volume 调色作用于整个
  color target，物理上没法只染背景 —— 从前 `mood Sunset` 会把对话框和 HUD
  一起染橙。现在色彩逐层写进各自的 `VNImageEffectController` 材质实例
  （背景 1.0 / 中景 0.8 / 立绘 0.3，UI 不在目标列表所以完全不受影响），
  Volume 只留 `FilmGrain + Vignette`（不改色相，压四角反而有电影感），
  仍是 A/B 双 Volume 交叉过渡。立绘目标由 `VNStage.RefreshRegistries()`
  在角色进出场时自动维护，背景在 `AutoWire()` 里注册。
  **加新图层想让它被 mood 染色 → 注册进目标列表；想躲开 → 别注册。**
- **VNGodRays / VNCloudShadows / VNHeatHaze / VNFakeDoF / VNEdgeGlow /
  VNVignetteFocus**：光束/云影/热浪扭曲+雾/伪景深(UI 不写深度所以是"伪")/
  屏幕边缘情绪泛光/聚焦晕影。都是 `fx <name> on|off` 路由的终点。

### 8.5 转场与镜头辅助

- **VNScreenTransition**：全屏转场大合集（噪声溶解/百叶窗/瓦片/圆扩散/水墨/
  爆闪/光斑/眨眼 + 高级四式卷页/碎裂/水波/墨染）。`transition` 与
  `bg ... transition:` 命令的执行者，排序 100 盖住一切。
- **VNShatterGraphic**：碎裂转场的碎片网格 Graphic（给 ScreenTransition 用）。
- **VNCameraFade**：camseq 路径点 `xfade:` 交叉淡化的截屏叠化辅助。
- **VNCamZoomMode**（枚举，在 `VNCamera.cs`）：camseq 的缩放模式 both/depth/bg/char
  ——「谁跟着 zoom 缩放」。两个静态公式 `VNCamera.CharacterScaleFor(mode, zoom)`
  与 `ContainerZoomFor(mode, zoom)` 是**运行时与编辑器预览的唯一真相**，改公式只改这两处。
  **`both` 下 `ApplyCharacterZoom` 是空转**（否则每个路径点都给每个立绘起补间，
  会打断说话者高亮那条），代价是还原只能在模式切换点做 → 所有切换收口到 `SetMode()`。
  公开的 `Cut()` / `GoTo()`（= camcut / camto）一律 `SetMode(Both)` 不继承上一段模式；
  camseq 内部叠化段走私有的 `CutWithMode()`。参与的立绘由
  `VNStage.RefreshRegistries()` 调 `SetCharacterTargets()` 维护，与 mood 同一时机。

### 8.6 输入反馈与组合技

- **VNMouseStardust**：鼠标星尘拖尾。**VNClickRipple**：点击涟漪。
- **VNSakuraBurst**：樱吹雪告白组合技（`sakura` 命令）。走 VNFoliageSystem，
  自己造一份「暴风版」def；起手 `Gust()` + `Burst()`、中途补两记阵风、尾声风力衰减。
- **VNLiquidSplash / VNWetScreen / VNLiquidPreset**：液体喷溅（`liquid` 命令，九十四章）。
  **一个效果两层，缺一层就不成立**：
  - `VNLiquidSplash`（场外世界空间粒子，sortingOrder 28）= 空中飞的水珠。三个发射器
    各司其职：Body 走 `VN/ParticleAlpha` + `ParticleSystemRenderMode.Stretch`
    （拉伸公告板是水感的一大半，球形粒子怎么调都像泡泡）、Glow 走 `VN/Additive`
    吃 Bloom、Splinter 是低速碎珠。速度/方向逐颗用 `EmitParams` 给，不用 shape 模块。
  - `VNWetScreen`（Canvas 下 overlay，默认 30 让开对话框 40）= 溅在镜头玻璃上的水渍。
    **不是粒子**：每滴要挂住、按各自节奏下滑、拖一条渐淡的痕、慢慢干，
    是逐个体的四段状态机，ParticleSystem 的曲线模型表达不了 → uGUI 对象池 + 手动模拟。
  - 假折射（C1）：不采样背景，玻璃感全烘进 `VNProceduralTextures.WaterDrop` 的 RGB
    剖面（中心压暗 + 内亮环 + 外圈菲涅尔暗边）。要升级成真折射见九十四章的取舍记录。
  - HDR 两边给法相反：水渍高光按液体各建一份材质，`_TintColor` 给颜色与亮度、
    顶点色只给淡入淡出；粒子那边材质给固定白色 HDR 天花板、`startColor` 给色相，
    这样四种液体共用一份材质不串色。
- **VNChoicePanel**：选项演出（飞入/悬停扫光/落选溶解），`choice` 的 UI，
  需要场景有 EventSystem。
- **VNEffectsDemo**：特效演示场景的键盘驱动器（按键触发各组件，
  `UpdateHint()` 维护提示文本）。只属于 VNEffectsDemo 场景，剧本场景不用它。

---

## 九、编辑器工具

| 文件 | 菜单/入口 | 职责 |
|---|---|---|
| VNEffectsDemoSetup.cs | Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene / **Create Script Demo Scene** | 两个场景的一键生成器：搭容器层级、连全部组件引用、建材质/角色/任务资产、写演示剧本。**加新运行时组件后要在这里接线并重建场景** |
| VNScenarioEditorWindow.cs | Tools → VN Effects → 剧本编辑器 Scenario Editor | 剧本可视化编辑器主窗口：行列表 UI、分层添加菜单、Sprite 缩略图浏览器、分类颜色、▶ 从选中行播放（SessionState Bridge） |
| VNScenarioDoc.cs | （数据层） | `.vn.txt ↔ VNRow 列表`双向转换（注释/空行/未知 token 原样保留）、`Validate()` 静态校验、`SourceLineForRow` 行号换算（choice/event 选项行、camseq 路径点都占行） |
| VNScenarioSchema.cs | （模式表） | **命令参数的单一数据来源**：每个命令的位置/kwarg 参数、控件类型（VNParamSource）、默认值。加命令时在这里登记，编辑器 UI 自动长出来 |
| VNCamseqEditorWindow.cs | Tools → VN Effects → 镜头编排 Camera Sequence Editor | camseq 路径的可视化编辑：Game 视图取点、路径预览、交叉叠化支持 |
| VNCharacterVisualPreviewWindow.cs | Tools → VN Effects → 预览 Preview → 角色立绘预览 Character Visual Preview | 角色立绘/头像/眨眼/口型的实时预览与标定，**确认后才写入资产** |
| VNWeatherPreviewWindow.cs | Tools → VN Effects → 预览 Preview → 天气预览 Weather Preview | 飘落天气调参：编辑模式播放翻转帧预览（判断叶型像不像就看这里——宽度随帧呼吸 + 背面变暗），Play Mode 滑杆实时应用到场景，另存资产 + 一键登记进 VNGameConfig |
| VNFogTuneWindow.cs | Tools → VN Effects → 预览 Preview → **擦雾调参 Fog Wipe Tuning** | 擦雾调参（一三一章）：上半按 `每秒擦除面积 ≈ 笔刷直径 × 鼠标速度` **算出**预计通关秒数并给手感评语，下半是可拖鼠标试擦的掩码预览、回雾照参数实时跑。跑的是运行时同一个 `VNFogMask`，看到的行为和游戏里一致。本玩法难度唯一来源是「笔刷面积 vs 回雾速度」，手感全压在参数上，**这个窗口是刚性需求不是加分项** |
| VNAiStudioWindow.cs | Tools → VN Effects → AI → **AI Talk Studio** | AI 试聊台主窗口（一一二章）：左改参数 / 中聊天流 / 右 **system prompt 实时预览**。不进 Play Mode 调人格与提示词 |
| VNAiStudioDraft.cs | （草稿层） | 人格资产的内存副本。**用临时 SO 而不是自建字段**：`SerializedObject` 迭代画＝零 UI 代码就有全部字段，加新字段自动跟上，`VNAiConversation` 也直接能吃。写回逐属性 `CopyFromSerializedProperty`（**不能用 `CopySerialized`**，它连 `m_Name` 一起抄成「xxx(Clone)」） |
| VNAiStudioSession.cs | （会话层） | 发请求 / 解析 / 轮次记录 / 重跑 / 分岔。域重载后靠轮次记录 `BuildRequest`+`RecordReply` **重建历史**（那两个方法只是组装与追加，不发请求） |
| VNAiStudioMemory.cs | （记忆层） | 可命名记忆预设 + 从日志 / 从存档两个导入器。**完全独立于运行时 `VNAiMemory`**（那是存档态）；读存档**自己读 JSON，绝不调 `VNSaveSystem.Load()`**——那个会 `VNFlags.Clear()` |
| VNAiStudioLog.cs | （导出） | 试聊会话按游戏内**同格式**写到 `AiTalkLogs/Editor/`，两边日志可互相对比 |
| VNAiEditorCoroutine.cs | （基础设施） | Play Mode 外的协程泵，试聊台与自检菜单共用。**坑**：子协程跑完弹栈后父协程的 `Current` 仍指着那个已耗尽的对象，不记 `_started` 会无限重新压栈，表现为「点了没反应也不报错」 |
| VNAiCostReport.cs | Tools → VN Effects → AI → **Cost Report** | 花费累计报表：扫全部日志 json 聚合。**改成本相关代码时的回归入口**——存储金额与重算金额在同模型下必须相等 |
| VNAssetUi.cs | （共用层） | 素材界面三边（drawer / 分页 Inspector / 浏览器窗口）共用的绘制与预览：Sprite 缩略图、音频试听、波形、拖拽接收、搜索匹配、Rect 切割。**加素材类界面先来这里找零件**，别各画各的 |
| VNConfigEntryDrawers.cs | （PropertyDrawer） | 背景 / CG / 音频 / UI 皮肤四个条目的紧凑单行绘制。**挂在类型上**，所以 `VNGameConfig`、`VNStage`、`VNAudio` 三处的同名列表一起受益 |
| VNGameConfigEditor.cs | （CustomEditor） | `VNGameConfig` 的九页分页 Inspector + 智能列表（搜索 / 分页 / 行操作 / id 告警 / 批量拖入） |
| VNAssetBrowserWindow.cs | Tools → VN Effects → **素材浏览器 Asset Browser** | 素材浏览器：大缩略图网格 + 音频波形列表（均虚拟化）+ 详情栏 + 只看未登记 |
| VNTextureImportDefaults.cs | （AssetPostprocessor）+ Tools → VN Effects → 贴图 Textures → 套用 Sprite 导入设置到选中项 Apply Sprite Settings | 素材目录里首次导入的图自动设 `Sprite (2D and UI)` + `Single`。**白名单目录**（`Roots` 数组，新开素材目录往里补一行），**绝不能全项目一刀切**——`Art/Models/**` 下的法线贴图按 Sprite 导入会让光照全错 |

**贴图导入默认值的坑**（一二〇章）：`AssetImporter.importSettingsMissing`
**不等于「没有 .meta」** —— meta 存在却不含完整 importer 设置块时它同样返回 true，
工程里那些很早加进来、一直是 Default 类型没人动过的老图就属于这种，
只信这一条会静默改写存量 .meta（实测中招 5 张）。
反过来，想用「磁盘上没有 .meta」来卡死"新文件"**也不成立**：
Unity 在调 preprocessor 之前就已经把 .meta 写盘了，加这条会让 postprocessor 完全不生效。

**素材界面硬约定**（一一九章）：
⓪ **列表元素类里的字段说明一律用 `[Tooltip]`，绝不用 `[Header]`。**
`[Header]` 是 DecoratorDrawer，Unity 画它时会 `position.yMin += decorator.GetHeight()`
（约 26px）。后果有两层：列表里每一项都重画一遍，一个条目就占 6~7 行；
更糟的是**自定义 drawer 内部只要自己调 `EditorGUI.PropertyField(rect, 子属性, …)`
就会触发那个子属性的 decorator**，把只有 18px 高的 rect 推成负高度 ——
表现为文字叠印且**输入框点不进去**。挂了 drawer 也挡不住，因为 Unity 只是不*自动递归*而已。
同理，**任何画在控件之上的叠加层（占位提示、tooltip）都必须只在
`Event.current.type == EventType.Repaint` 时画**，否则会吃掉底下控件的点击
（`VNEntryDrawerBase.Overlay()` 就是干这个的）。
① **Sprite 缩略图别用 `AssetPreview`** —— 它异步，列表里几十张一起等会闪一片空白；
Sprite 自己知道在哪张 texture 的哪个 UV，`GUI.DrawTextureWithTexCoords` 同步画即可（texture 不必可读）。
② 异步预览（音频波形、prefab）**必须有放弃机制** —— 有些资产永远不会有预览图，
一直请求重绘就是空转；而 Unity 6.5 起 `AssetPreview.IsLoadingAssetPreview(int)` 与
`Object.GetInstanceID()` 都是 **error 级弃用（CS0619，直接编译失败）**，问不了 Unity，
只能自己记时间窗口。
③ 分页 Inspector 的**页签只登记字段名**，绘制仍走 `PropertyField` ——
往 `VNGameConfig` 加字段时不改页签也不会静默消失（会落到「其他」页并提示）。

**编辑器铁律**：文本是唯一真相（编辑器状态不落存档）；`say` 的角色/表情走
`VNRow.speaker/expression` 专用字段，`show` 才用普通参数——两条路径不能混。
窗口状态要活过域重载就必须**同时**改 `OnBeforeSerialize` 和 `OnEnable`
（`VNScenarioEditorWindow` 与 `VNAiStudioWindow` 都遵循这条）。

---

## 十、Shader

| 文件 | 用途 |
|---|---|
| VNImageEffect.shader | 单图特效主 shader（溶解/扫光/HSV/波浪/轮廓光/模糊 9-tap…），VNImageEffectController 的载体。传统 CGPROGRAM（Canvas 不走 URP 光照），保留 UI 裁剪兼容 |
| VNAdditive.shader | 加法混合发光（光环/光束/**发光类**粒子），HDR 颜色 >1 配合 Bloom 阈值 1.0 出辉光 |
| VNParticleAlpha.shader | 普通透明混合（`SrcAlpha OneMinusSrcAlpha`），**实体类**粒子专用：花瓣/落叶/雨/雪。`_SoftBlur` 做 5-tap 十字模糊供近景层虚焦 |
| VNScreenTransition.shader | 全屏转场图案生成（噪声/百叶窗/圆扩散…的数学都在这）。**两种填充**：`_TexMode=0` 图案里填 `_Color` 纯色（遮罩式转场，必然经过一片纯色）／`_TexMode=1` 填 `_MainTex` 贴图（直接过渡，不过纯色，此时 `_Color` 退化成染色系数）。`_Invert` 给「旧图按图案消失」的叠加层用；`_UVRect` 把图集 Sprite 的 uv 归一化回 0~1，不然瓦片格子会跟着图集乱跑 |
| VNDirectBackgroundTransition.shader | 背景直切转场（新旧背景在材质内交叉，不经全屏遮罩） |
| VNFogWipe.shader | 擦雾的雾层（RawImage 专用）。雾 = 底图的 9-tap 模糊 + 提亮 + 偏雾色，**不是一层纯白半透明**——剪影透出来玩家才知道往哪擦。`_MaskTex` 是低分辨率 R8 掩码（r=1 已擦净）；`_UVRect` 把 RawImage 烘进顶点的图集 uv 反算回 0~1 局部坐标供掩码与噪声采样，不反算的话 CG 一进图集擦痕就跟着偏。**★ 边界必须用噪声扰动**（内置 `fbm` 现算，免噪声贴图）——掩码直接当 alpha 用会得到光滑圆边，一眼假 |

**发光的公式**：HDR 顶点色会被 uGUI 钳到 1，所以发光=**材质属性**里给 >1 的
HDR 颜色 + 场景 Bloom（阈值 1.0）。想让什么东西发光，走材质别走 Image.color。

**粒子选哪个 shader**：问「这东西是光还是实体」。光（星光/萤火虫/尘埃/光斑）用
`VN/Additive`；实体（花瓣/落叶/雨/雪）用 `VN/ParticleAlpha`。用错的代价很具体 ——
加法混合永远无法遮挡背景，彩色粒子叠上明亮背景后三个通道溢出，
被 Bloom + Tonemapping 一压就全变成白色（旧版落樱「樱花是白的」就是这么来的）。

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
   （**推论**：任何"让某层躲开后处理"的方案都会连 Bloom 一起躲开，见下方坑清单）
6. 事件模块三铁律（见第六节）
7. 动手前先问用户「走 `feature` 分支 + PR，还是当小改动直接改 main」。
   分支路线：开 `feature/<名>` → 实现完等确认 → 用户叫了才 commit/push/`gh pr create` →
   用户叫了才写文档（同分支同 PR）→ 用户自己在 GitHub 合并 → 我 checkout main + pull。
   小改动路线（纯文档/typo/单文件小修/技能与素材登记）：直接在 main 上改，
   **改完仍要等用户说「推」才 commit + push**。**永不删分支**（详见技能 `vn-new-feature`）
8. 调色一律走 `VNImageEffectController.SetGrade(通道, …)`，禁止直接写
   `_Brightness` / `_Saturation`（六方共用会互相覆盖，详见 8.1 节）

**容易踩的坑**
- kwargs 值不能含空格；`if` 条件串不能含空格
- 粒子 velocityOverLifetime 三轴曲线模式必须一致
- 运行时创建带 Awake 配置的组件：先 SetActive(false) 挂好赋值再激活
- `VNSaveData` 加字段必须给初始化器默认值（JsonUtility 旧档兼容）
- Sprite 缩略图必须按 `textureRect` 画 UV（图集里的图不能整张 texture 当缩略图）
- 合并分支报 `unable to unlink ... .unity`：Unity 占用场景文件，
  `git clean -f -- <残留文件>` 后重试
- UI 不写深度缓冲：别指望真 DoF/深度后处理，模糊走 VNImageEffect 的 9-tap
- **想让某一层躲开全屏后处理（一一〇章）**：单相机 + 单 Canvas 下，Volume 作用于
  整个 color target，没有"只染背景"这回事。而且两条看似可行的相机路都是死路——
  URP Camera Stack **做不到**「Base 吃调色、Overlay 不吃」（整个 stack 共用一个
  color target，后处理在最后一个相机之后统一跑一次）；`Screen Space - Overlay`
  确实能 100% 躲开后处理，但**连 Bloom 一起躲开**，对话框流光边框、名牌发光、
  选项扫光全变死板纯色。正解是别用后处理做这件事，改成 per-image 材质分层调色
- **永远不信任大模型的输出**（一〇六章）：结构化输出只约束**形状**不约束**取值范围**
  ——Gemini 的 schema 子集不支持 `minimum`/`maximum`，实测让它给 -2~+2 的好感变化
  它会给 +5。凡是模型输出的数值一律代码内 `Clamp`、枚举一律白名单校验后降级、
  数组长度一律补齐或截断。宁可演出打折，也不能让模块崩或让数值失控
- **编辑器里手动泵协程要防重复压栈**（一〇六章）：子协程跑完弹栈后，父协程的
  `Current` 仍指向那个**已耗尽的**子协程对象。只判断「Current 是不是 IEnumerator」
  会把它无限重新压栈，父协程永远前进不了一步。表现是「点了菜单没反应也不报错」。
  用一个 `HashSet<IEnumerator>` 记下已驱动过的，只压栈一次
- **`EditorUtility.DisplayDialog` 会阻塞主线程**等用户点击。装机器 / 批处理这类
  可能被自动化调用的入口，拆成 `Xxx(bool interactive)`，非交互模式只写 Console
- **纯逻辑类被编辑器复用时，`Object.Destroy` 要按 `Application.isPlaying` 分路**
  （一三一章）：编辑模式下它只打一条「may not be called from edit mode」的 error 并且
  **什么都不销毁**。`VNFogMask` 踩过——症状是每关一次调参窗口就泄漏一张贴图。
  凡是「运行时与编辑器窗口共用同一份代码」的类（本项目刻意做了好几个），
  释放资源那一段都要检查这件事
- **位图笔刷沿线段补点时别无条件累加**（一三一章）：鼠标一帧能跑很远，必须沿线段
  补 stamp，但同一个像素会被七八个 stamp 连着盖到。直接 `+=` 会把笔刷的羽化带一路
  填满、边缘退化成硬边（低分辨率画布上放大后就是方块阶梯），而且「强度」参数会变得
  名不副实（一次划过就到顶）。正解是记住「这一笔碰到该像素之前的值」，
  一笔之内取 max、笔与笔之间才累加
- **手感参数别做成 public 序列化字段**：字段一旦被存进场景（生成器/安装器建的组件
  必然如此），改代码里的默认值对已存在的实例**完全无效**。改了半天没反应、
  或者只有一半改动生效（运行时计算的那半生效了、序列化的那半没有），就是这个。
  参数决定跟着代码走时（不做 ScriptableObject）就写成 `const`，别留第二个真相来源。
  排查手法：直接 `grep` 场景 `.unity` 文件看那个字段实际存的是什么值。
  （九十四章：水滴尺寸代码里改成 6px，场景里躺着 62px，结果水渍被拉成三百像素的烟雾）

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