# AddFeatureGuide.md — 往这个专案加新元素／新功能的完整流程

> **定位**：本文件回答一件事 —— 「我想加一个 XX，从零到能在剧本里用起来、能存档、
> 能被编辑器补全、能被 Lint 检查、能被翻译，中间到底要动哪些档案、按什么顺序动。」
>
> 与既有文档的分工：
>
> | 文件 | 回答什么 |
> |---|---|
> | `CLAUDE.md` | 专案有什么、硬约定是什么（速查表） |
> | `ProjectCodeGuide.md` | 单一脚本的职责／用法／扩展点（改某个档案前查它） |
> | `HowToUse.md` | 剧本 DSL 语法（写剧本的人查它） |
> | `WhatAiDo.md` | 每个功能当初是怎么做出来的（历史记录） |
> | `SetUpGuide.md` | 从空场景手动搭舞台 |
> | **`AddFeatureGuide.md`（本文件）** | **加新东西的端到端流程** |
> | `.claude/skills/vn-*` | 同样的流程，但压缩成给 AI 执行的清单 |

---

## 0. 开工前必读的三件事

### 0.1 ⚠️ 程式码实际路径与 CLAUDE.md 不一致

`CLAUDE.md` 的「目录结构」一节写的是 `Assets/Scripts/VNEffects/`，**这个目录已经不存在了**。
实际布局是：

```
Assets/Project/Scripts/VNEffects/
├── *.cs            纯演出层组件（VNCamera / VNImageEffectController / VNWeatherDef …）
├── Script/         剧本系统 + 玩法系统 + 资产定义（129 个 .cs）
└── Editor/         编辑器工具（43 个 .cs：剧本编辑器 / Lint / 装机器 / 素材浏览器 …）
```

本文件里所有路径都以**实际路径**为准。

### 0.2 素材与资产的实际落点

| 内容 | 实际目录 |
|---|---|
| 角色定义资产 | `Assets/Art/VNEffects/Characters/*.asset` |
| 属性／商店／日程／任务／互动／人格／过场 | `Assets/Art/VNEffects/{Stats,Shops,Plans,Quests,Interactions,AiPersonas,Interlude}/` |
| 题库／羽球对手／擦雾／教程／大头贴／UI 皮肤 | `Assets/VNEffects/{Quizzes,Badminton,FogWipes,Tutorials,Photo,UISkins}/` |
| 剧本 | `Assets/Scenarios/*.vn.txt` |
| 图片素材 | `Assets/Art/Images/{Background,Character,CG,UI}/`、`Assets/Assets/`（随手丢） |
| 总配置 | `Assets/Resources/VNGameConfig.asset`（路径写死在 `VNGameConfig.AssetPath`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:39`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L39)），[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:39](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L39)） |

注意资产目录被切成 `Assets/Art/VNEffects/` 与 `Assets/VNEffects/` 两半，这是历史整理留下的。
**新资产放哪边都能用** —— 所有扫描都走全工程的 `AssetDatabase.FindAssets` 类型查询，
不限目录（`FindAll<T>`，[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:223](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L223)）。
但为了别人找得到，请跟同类资产放一起。

### 0.3 动手前先问一句：走分支还是直推 main

专案规则第 3 条要求每个任务动手前确认路线：

- **`feature/<英文短名>` 分支 + PR**：新增元素类别、新命令、新模块、改架构 → 一律走这条
- **直接在 `main` 上改**：纯文档、typo、单文件几行的参数调值、素材登记 → 快速通道

两条路都是「我改完 → 报告 → 你说推 → 才 commit/push」。详见第 21 节。

---

## 1. 分诊表：你要加的东西属于哪一类

这个专案是视觉小说，没有传统 ARPG 的「敌人 / 关卡 / 技能」概念。
先在这张表里找到你要加的东西对应到本专案的什么，再跳到对应章节。

| 你想加的 | 在本专案里它其实是 | 章节 |
|---|---|---|
| 一个新角色（立绘、表情、名牌） | [`VNCharacterDef`](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs) 资产 | §3 |
| 一张新背景 / CG / 一段 BGM 或 SE | 素材文件 + [`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) 库条目 | §4 |
| 剧本里想写的一句新指令（`bgscroll`、`imprint` 这种） | DSL 关键字 | §5 |
| 一个新小游戏 / QTE / 战斗 / 任何「暂停剧本→玩家操作→带结果回来」 | [`VNEventModule`](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs) 子类 | §6 |
| 一类新的可配置数据（题库、对手、主题、预设…） | `ScriptableObject` 定义资产 | §7 |
| 一个新的养成数值（体力、魅力、金钱…） | [`VNStatDef`](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs) 资产 + [`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs) | §8 |
| 一件新道具 / 装备 / 一家新商店 | [`VNShopDef.Item`](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs) | §9 |
| 一条新任务线 | [`VNQuestDef`](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs) 资产 + `quest` 命令 | §10 |
| 一套新的对话框外观 / 系统菜单主题 | [`VNDialogueSkin`](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs) / [`VNSystemUiSkinSet`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs) prefab | §11 |
| 一个新的画面特效（发光、粒子、全屏运动） | `VNEffects` 组件 + `fx` 开关 | §12 |
| 一个新章节 / 一段新剧情（≈ 别的游戏的「关卡」） | `.vn.txt` 剧本文件 + `label` | §13 |
| 一篇新手引导 | [`VNTutorialDef`](Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs) 资产 | §14 |
| 一个新的 AI 聊天人格 | [`VNAiPersonaDef`](Assets/Project/Scripts/VNEffects/Script/VNAiPersonaDef.cs) 资产 | §15 |
| 一种新天气 / 一张新过场章节卡 | [`VNWeatherDef`](Assets/Project/Scripts/VNEffects/VNWeatherDef.cs) / [`VNInterludeDef`](Assets/Project/Scripts/VNEffects/Script/VNInterludeDef.cs) 资产 | §16 |

**「技能」怎么办？** 本专案没有技能系统。最接近的两个落点是：
装备的被动效果（`VNShopDef.PassiveEffect`，[Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:45](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L45)，
合计写进 flag「装备效果_<id>」，由剧本 `if` 判断生效），
以及战斗模块里从 flag 读属性的攻防值（[`VNBattleModule`](Assets/Project/Scripts/VNEffects/Script/VNBattleModule.cs) 的 `patkstat` / `phpstat` / `pdefstat`）。
真要做一套完整技能系统，走 §6 新玩法事件模块 + §7 新定义资产两条流程组合
（技能表 = 定义资产、释放 = 模块内逻辑、持有 = flag）。

**「敌人」怎么办？** 已有两套互不相干的实现，按你要的玩法选：
数值型敌人走 `VNBattleModule` 的 `enemy:` / `ehp:` / `eatk:` 参数（写在剧本行，无资产）；
有人格有台词的对手走 [`VNBadmintonDef`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonDef.cs) 那种「一个对手一个资产」的模式（§7）。

---

## 2. 五个贯穿所有流程的不变量

在读任何具体流程之前先理解这五条，后面每一节都会用到。

### 2.1 「配置进资产，不进场景」+ 覆盖语义

剧本演示场景是**一键重建**的（`Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景`，
[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:450](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L450)），内部会
`NewScene(EmptyScene)` 把当前场景整个丢掉重造。任何挂在场景组件上的人工绑定都会消失。

所以有了 [`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:34](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L34)），
一个固定放在 `Assets/Resources/VNGameConfig.asset` 的 ScriptableObject，运行时用
`Resources.Load` 直接取（`VNGameConfig.Active`，同档:48），场景里一个引用字段都不需要。

**覆盖语义只有一条规则**：资产里**填了的**列表覆盖场景组件上的同名列表，**留空的**保持场景原样。
实现是 `VNGameConfig.ApplyList`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:258`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L258)）（同档:258）：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:258
public static bool ApplyList<T>(List<T> source, ref List<T> target)
{
    if (source == null || source.Count == 0) return false;
    target = new List<T>(source);
    return true;
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:258](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L258)

[`VNStage`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs) 在 `Awake` 里调 `ApplyGameConfig()`（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:170](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L170)）
把角色／背景／CG 三个库覆盖进来；事件模块自己在 `OnLaunch` 里做同样的事，
例如问答模块（[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:85](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L85)）：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:84-86
var cfg = VNGameConfig.Active;
if (cfg != null) VNGameConfig.ApplyList(cfg.quizzes, ref quizzes);
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:84](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L84)

> **推论**：你新增任何「一类可配置数据」时，都要在 `VNGameConfig` 上加一个 `List<T>` 字段，
> 并在使用方 `OnLaunch` / `Awake` 里加一句 `ApplyList`。少了这一步，
> 功能在你手上能跑，重建一次场景就全没了。

### 2.2 id 是逻辑标识符，永不翻译；显示名才翻译

每个定义资产都长着同一个形状：一个 `id`（剧本引用它，可以写中文，**永远不翻译**）
+ 一组 `xxx` / `xxxEn` / `xxxJa` 显示文案（留空回退中文）。

范本是 [`VNStatDef.DisplayName`](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs:68](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs#L68)）：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs:68
public string DisplayName
{
    get
    {
        string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
            : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
        if (!string.IsNullOrEmpty(localized)) return localized;
        return string.IsNullOrEmpty(displayName) ? id : displayName;
    }
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs:68](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs#L68)

同一个模式在 [`VNCharacterDef.LocalizedDisplayName`](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs:74](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs#L74)）、
[`VNQuestDef.Title`](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs:44](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs#L44)）、
[`VNShopDef.Item.DisplayName`](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:128](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L128)）、
[`VNQuizDef.Option.Display`](Assets/Project/Scripts/VNEffects/Script/VNQuizDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuizDef.cs:28](Assets/Project/Scripts/VNEffects/Script/VNQuizDef.cs#L28)）里各写了一遍。
**新资产照抄这个三段式回退**（当前语言 → 中文 → id），不要自己发明。

### 2.3 运行时状态一律进 VNFlags，不要自己造字段

[`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs) 是一个整型字典（`VNFlags`，[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:15](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L15)），
整个字典随存档序列化。**只要状态存进 flag，就白拿存档、`if` 分支、选项条件、调试重建四件事**。

各系统的 flag 命名约定（全部是既成事实，改不得）：

| 系统 | flag 名 | 出处 |
|---|---|---|
| 养成属性 | 直接用属性 id | `VNStatDef.id`（[Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs:32](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs#L32)） |
| 道具持有数 | `道具_<商品id>` | `VNShopDef.ItemFlagName`（[Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:72](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L72)） |
| 任务阶段 | `任务_<任务id>` | [`VNQuestLog.FlagName`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:55](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L55)） |
| 装备中 | `装备_<道具id>` = 部位编号 | `VNEquipSlot`（[Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:7](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L7)） |
| 装备实际加成 | `装备实增_<…>` | [`VNEquipment`](Assets/Project/Scripts/VNEffects/Script/VNEquipment.cs) |
| 装备被动效果合计 | `装备效果_<效果id>` | `VNShopDef.PassiveEffect`（[Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:45](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L45)） |
| 事件数值结果 | `事件结果` | [`VNScriptRunner.EventCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2821](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2821)） |
| 各模块成绩 | `<前缀>正确数` / `<前缀>_我方得分` … | 各模块的 `flag:` 参数 |

`VNFlags.Changed`（[`Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:21`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L21)） 事件（[Assets/Project/Scripts/VNEffects/Script/VNFlags.cs:21](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs#L21)）在任何写入后触发，
属性 HUD 等 UI 靠它刷新。注意注解里的告诫：读档会连续触发多次，
订阅方要做「标脏 + 下帧统一刷新」而不是立即重建。

### 2.4 加了东西要同步的四处（最常漏的一步）

这是本专案最容易出事的地方。任何**新的剧本可见概念**，除了实现本身，还要同步：

| # | 同步点 | 档案 | 不做的后果 |
|---|---|---|---|
| ① | `VNGameConfig` 登记 | Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs | 重建场景后失效 |
| ② | 剧本编辑器 Schema | Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs | 编辑器画不出参数格，只能手打，且被标为 unrecognized token |
| ③ | 静态校验器 Lint | Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs | 拼错 id / 结果名会**静默跳过**，没有任何报错 |
| ④ | 存档 + 调试重建 | Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs、`VNStage.CaptureSnapshot`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:840`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L840)）／`RestoreSnapshot`、`VNScriptRunner.RebuildStateBefore` | 读档后状态丢失；「从选中行播放」画面不对 |

第 ④ 项只有「持续状态」才需要（详见 §18）。
一次性演出（`imprint` 痕迹、`emote` 情绪动作、镜头状态）**刻意不进存档**。

### 2.5 命令的执行语义

[`VNScriptParser.Parse`](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:112](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L112)）
把每行切成 `VNScriptCommand`（同档:41）：`keyword` + `args`（位置参数）+ `kwargs`（`key:value`）。
判定「是命令还是台词」靠一张白名单 `Keywords`（同档:93）：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:93
static readonly HashSet<string> Keywords = new HashSet<string>
{
    "bg", "cg", "show", "hide", "emote", "mark", "overlay", "imprint", "wait",
    "camera", "shake", "weather", "mood", "fx", "liquid", "bgscroll",
    // ...
    "sns",
};
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:93](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L93)

**首 token 不在这张表里 = 整行当台词**（`ParseSay`，同档:340）。所以加新命令时忘了改这张表，
症状不是报错，而是你的命令行被当成一句台词原样念出来。

行尾 `@` = 异步不等待（同档:148）。命令默认同步等待。

---

## 3. 类别 A：新角色

### 3.1 这一类要做的事

一个「角色」= 一个 [`VNCharacterDef`](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs) 资产（[Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs:22](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs#L22)）
+ 若干张立绘图。剧本用 `id` 引用，名牌显示 `displayName`。

### 3.2 步骤

**① 把立绘图放进素材目录**

放 `Assets/Art/Images/Character/` 或 `Assets/Assets/`。这两个都在自动导入白名单里
（`VNTextureImportDefaults.Roots`（[`Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs:37`](Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs#L37)），[Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs:37](Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs#L37)），
首次导入会自动设成 Sprite + Single（`OnPreprocessTexture`，同档:47）。

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs:37
public static readonly string[] Roots =
{
    "Assets/Art/Images/",     // Background / Character / CG / UI / …
    "Assets/Art/CG/",
    "Assets/Art/BigPhoto/",
    "Assets/Art/Mark/",
    "Assets/Art/InteractionMiniGame/",
    "Assets/Assets/",         // 随手丢素材的地方
};
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs:37](Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs#L37)

> **坑**：新开一个素材目录时**必须往 `Roots` 补一行**，否则图片进来是 Default 贴图不是 Sprite，
> 拖不进 Sprite 栏位。反过来也**绝不能改成全项目一刀切** —— `Art/Models/**` 底下 60+ 张
> 法线／粗糙度贴图按 sRGB 的 Sprite 导入会让光照全错，而且极难联想到是导入设置的问题。
> 存量图补登走 `Tools → VN Effects → 贴图 Textures → 套用 Sprite 导入设置到选中项`。

**② 建资产**

`Create → VN → Character Definition`（`CreateAssetMenu`，[Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs:21](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs#L21)），
存到 `Assets/Art/VNEffects/Characters/<角色名>.asset`。

**③ 填必填栏位**（行号都在 Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs）

| 栏位 | 行号 | 说明 |
|---|---|---|
| `id` | :25 | 剧本引用名，可中文（如 `亚里沙`）。**永不翻译** |
| `displayName` | :28 | 名牌显示名 |
| `displayNameEn` / `displayNameJa` | :31 / :34 | 译名，留空回退中文（`LocalizedDisplayName`，:74） |
| `nameColor` | :37 | 名牌底色。没勾 `overrideNameplateColors`（:40）时，名牌渐变字色由它自动推算（`GetNameplateColors`，:53） |
| `expressions` | :100 | 表情列表，**第一个是默认表情**（`DefaultSprite`，:268） |

取图走 `GetSprite`（:239）：找不到表情时**回退第一个并告警**，不会崩。

**④ 标定尺寸与站位**（素材构图不统一时必做）

`sizeScale`（:216）、`positionOffset`（:219）、`rotationZOffset`（:222）。
调参用 `Tools → VN Effects → 预览 Preview → 角色立绘预览 Character Visual Preview`
（也可以在资产上右键 → `角色立绘预览 Open Character Visual Preview`）。

**⑤ 可选能力：按需开**

| 能力 | 栏位 | 行号 |
|---|---|---|
| 自动眨眼 | `enableBlink` + `blinkMode` + `blinkSprite` / `blinkOverlaySprite` | :104 / :107 / :110 / :114 |
| 说话口型 | `enableMouthFlap` + `openMouthSprite` | :130 / :133 |
| 情绪叠加层（潮红/汗/泪） | `overlays` | :160 |
| 立绘痕迹（掌印/口红印） | `imprints` | :190 |
| 漫符锚点与自定义符号图 | `markAnchor` / `markScale` / `markSprites` | :204 / :208 / :211 |
| 对话框头像 | `showPortrait` / `portraits` / `portraitScale` / `portraitOffset` | :226 / :229 / :233 / :236 |

> **眨眼两种模式的取舍**（`VNBlinkMode`，同档:9）：`FullSprite` 换整张闭眼立绘，
> 要求与默认表情**同画布尺寸、同人物位置、同 Pivot**；`Overlay` 只在眼部叠一张透明图，
> 省一张全身图但要求那块像素能盖住原本睁开的眼睛。当前生效的那张图由
> `ActiveBlinkSprite`（同档:258）决定。

> **为什么叠加层不做成表情**（注解在同档:158）：「表情 × 潮红三档」是乘法爆炸，
> 每个组合都要一张完整立绘。叠加层是加法，可多层共存、强度 0~1 连续变化。

> **`imprints` 刻意不进存档**（注解在同档:188-189）：痕迹会自己褪色消失，
> 也就没有「读档后该不该还在」的问题。

**⑥ 登记进 VNGameConfig**

`Tools → VN Effects → 游戏配置 Game Config → 重扫素材目录 Rescan Asset Folders`
（`VNGameConfigTools.RescanAssetFolders`（[`Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:178`](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L178)），[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:178](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L178)）。
它会全工程扫 `VNCharacterDef` 覆盖 `config.characters`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:148](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L148)）。

**⑦ 验证**

```
show 新角色 at:center
新角色 微笑: 测试一句。
```

跑 `Tools → VN Effects → 剧本检查 Lint Scenarios`（`Ctrl+Shift+L`）。
角色 id 与表情名都会被校验（`CheckCharacter` / `CheckExpression`，
[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:834](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L834) / :846）。

### 3.3 这一类不需要做的事

- **不用改剧本编辑器 Schema** —— 角色下拉走 [`VNParamSource.Character`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs)
  （枚举 `VNParamSource`，[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:7](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L7)），
  扫 `VNCharacterDef` 资产自动出现；表情下拉走 `VNParamSource.Expression`，
  用 `dependsOn`（`VNParamDef.dependsOn`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:39`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L39)），同档:39）指向同行的角色参数。
- **不用改存档** —— 在场角色由 `VNStage.CaptureSnapshot`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:840`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L840)）
  （[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:840](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L840)）统一存进 `data.characters`。

---

## 4. 类别 B：新素材（背景／CG／音频）

### 4.1 关键区别：哪些能扫、哪些必须手工登记

`RescanAssetFolders`（[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:178](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L178)）
明确划了线，注解就写在函式里：

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:194-196（节录注解）
// 背景与音频**不扫**：素材散放在 Assets/Assets 里，文件名推不出剧本 id，
// 而且音频还有基准音量标定这种纯人工数据。它们由你在资产 Inspector 里维护。
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:194](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L194)

| 素材 | 能否自动扫 | 怎么登记 |
|---|---|---|
| 背景 | 否 | 手工填 [`VNGameConfig.backgrounds`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:152](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L152)） |
| CG | 见下方警告 | 实务上手工填 `cgLibrary`（同档:155） |
| BGM / SE / Voice | 否 | 手工填 `bgmLibrary` / `seLibrary` / `voiceLibrary`（同档:180 / :182 / :184），还要标定基准音量 |
| 角色 / 属性 / 商店 / 日程 / 任务 / 题库 / 羽球 / 人格 / 过场 / 教程 | 是 | Rescan 一键 |
| 章节剧本 | 是 | Rescan 扫 `Assets/Scenarios/*.vn.txt`（`ScanChapters`，[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:233](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L233)） |

> ### ⚠️ CG 扫描的一个真实地雷
>
> `ScanCg`（[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:245](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L245)）扫的是写死的常数
> `CgDir`（同档:25，值为 `Assets/CG`），而这个目录**在本专案里不存在**
> ——CG 图实际在 `Assets/Art/Images/CG/`。目前的行为是在同档:247 早退，
> 手工维护的 168 条 `cgLibrary` 安然无恙。
>
> **但只要有人建了一个 `Assets/CG` 目录（哪怕空的），下一次 Rescan 就会走到同档:270 的
> `config.cgLibrary = list;` —— 整个手工 CG 库被覆盖掉**（只保留 `group`，同档:262）。
> 更麻烦的是 [`VNEffectsDemoSetup`](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs) 里有一句
> `EnsureFolder("Assets/CG")`（[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:1299](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L1299)），
> 会**主动建这个目录**。
>
> 结论：**跑过「重建剧本演示场景」之后别再跑 Rescan**，或先把 `Assets/CG` 删掉。
> 根治方案见 [附录 D](#附录-d写这份文件时发现的两处偏差)。

### 4.2 推荐流程：用素材浏览器

`Tools → VN Effects → 素材浏览器 Asset Browser`（[`VNAssetBrowserWindow`](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs)，
Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs）。
左栏九类带条数，图片走大缩略图网格、音频走波形列表（都做了虚拟化，只画可见行），
底部详情栏可以改 id / 换素材 / 试听 / 定位 / 移除，右键还有「用文件名填 id」。

**为什么以缩略图为主而不是 id 列表**：本专案的文件名是 AI 生成的原始 prompt 或纯数字
（`1.png`、`masterpiece, very aesthetic… s-1095962266.png`），看名字根本认不出是哪张图。

「只看未登记」的扫描目录是**从已登记条目反推**的，不写死路径
（注解在 [Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs:896](Assets/Project/Scripts/VNEffects/Editor/VNAssetBrowserWindow.cs#L896)：
「`Assets/CG` 与 `Assets/Art/Images/CG` 并存过，写死路径必然过时」）。

登记完之后 `VNAssetLibraryEvents.RaiseChanged()`（[`Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs:26`](Assets/Project/Scripts/VNEffects/Editor/VNAssetLibraryEvents.cs#L26)） 会广播「素材库改了」，
剧本编辑器收到就重建 bg / cg / bgm 下拉候选 —— 不用手点 Refresh。
（两条约束：只在 `ApplyModifiedProperties()` 返回 true 时发，否则 OnGUI 每帧都会重建全部候选；
订阅方 `OnDisable` 必须退订，否则静态事件会攥着已销毁窗口的引用。）

### 4.3 手工登记的流程

1. 选中 `Assets/Resources/VNGameConfig.asset`
2. Inspector 是九页分页的（[`VNGameConfigEditor`](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs)，
   Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs）：
   剧本｜标题｜UI皮肤｜舞台｜音频｜玩法｜AI｜大头贴｜全部
3. 背景条目是 [`VNStage.BackgroundEntry`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:29](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L29)），
   只有 `id` + `sprite` 两栏
4. 批量拖入会自动用文件名填 id
5. 存档（Ctrl+S）。Inspector 改完会自动 `VNGameConfig.ClearCache()`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:69`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L69)）
   （[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:69](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L69)）——
   注解说明了为什么必须清：不清的话「在 Inspector 里把供应商从 Gemini 改成 DeepSeek 之后
   还会继续发给 Gemini，直到下次域重载 —— 这种『改了没反应』最难查」

**音频条目还要标定基准音量**：最终音量 = 基准音量 × 剧本 `vol:` 参数 × 通道音量。
这是纯人工数据，扫不出来。

### 4.4 CG 的额外一步：进画廊

CG 条目 `VNStage.CgEntry`（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:39](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L39)）有个 `group` 栏位：
**同 group 的 CG 在 G 键鉴赏画廊里合并成一格翻差分**，留空 = 独立一格。
解锁记录走 [`VNCgUnlocks`](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs) 独立 JSON（与存档槽分离 —— 读旧档／开新周目都不该丢）。

反过来，`interludeImages`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:168](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L168)）
里的过场图**刻意不进画廊**，注解写得很直白：「它们是演出素材，不是收集品」（同档:167）。

### 4.5 验证

Lint 会检查 bg / cg / bgm / se / voice 的 id 是否登记
（`CheckAssets`，[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:502](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L502)），
并给编辑距离拼写建议（`Suggest`，同档:1614）。
库整个是空的也会被抓（`CheckEmptyLibraries`，同档:398）。

---

## 5. 类别 C：新剧本命令（DSL 关键字）

> 对应技能：`vn-new-command`。这里是带程式码的展开版。

### 5.1 什么时候该加新命令

加命令的门槛应该**高**。先问：能不能用现有命令 + 参数表达？
`fx <name> on|off` 已经是通用开关入口（§12），`event <id>` 是通用玩法入口（§6）。
只有当你要加的是**新的一类语义**（不是新的一个特效、不是新的一个玩法）时才加关键字。

### 5.2 全链路八步

#### 步骤 1：Parser 白名单

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:93
static readonly HashSet<string> Keywords = new HashSet<string>
{
    "bg", "cg", "show", "hide", "emote", "mark", "overlay", "imprint", "wait",
    // ...
    "sns",
    "yourcmd",          // ← 加这里
};
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:93](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L93)

漏这一步的症状：整行被 `ParseSay`（同档:340）当台词念出来，**不报错**。
`CommandKeywords`（同档:110）是给编辑器工具用的公开视图，与解析行为单一来源。

#### 步骤 2（可选）：特殊解析

绝大多数命令不需要。三种既有的特例，都在 `ParseCommand`（同档:303）里：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:317-330（节录）
// 限定地址「文件::标签」不是 key:value
bool qualifiedAddress = colon >= 0 && colon + 1 < token.Length && token[colon + 1] == ':';
// camto / camcut 的第一个参数是「目标点」，可能长成 角色:部位
bool camPointArg = t == 1 && (cmd.keyword == "camto" || cmd.keyword == "camcut");
// sns time / sns system 后面跟的是自由文本，可能带冒号（如 sns time 昨天 23:47）
bool snsFreeText = cmd.keyword == "sns" && t >= 2 && cmd.args.Count > 0 &&
                   (cmd.args[0] == "time" || cmd.args[0] == "system");
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:317](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L317)

**如果你的命令有任何参数值可能包含冒号，必须在这里加一条豁免**，否则会被误切成 kwarg。

如果你的命令需要「附属行」（像 `choice` 的 `*` 行、`camseq` 的 `>` 行），
看 `Parse` 里的块状态机（同档:162-179）：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:162
if (cmd.keyword == "choice" || cmd.keyword == "event" ||
    (cmd.keyword == "sns" && cmd.Arg(0) == "reply"))
{
    cmd.options = new List<VNChoiceOption>();
    lastChoice = cmd;
    lastCamseq = null;
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:162](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L162)

`event` 与 `sns reply` 都是**复用** `choice` 的 `*` 行机制（`ParseChoiceOption`，同档:260），
没有自己造轮子 —— 照做。附属行必须紧跟块命令（同档:177 会把状态清掉）。

#### 步骤 3：Runner 分派

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2174
IEnumerator Dispatch(VNScriptCommand cmd)
{
    switch (cmd.keyword)
    {
        case "say":
            _currentSayIndex = _index - 1;
            return SayCo(cmd);
        // ...
        case "yourcmd":
            stage?.YourCommand(cmd.Arg(0), cmd.KwF("time", 0.5f), cmd.line);
            return null;                      // 立即完成 → 返回 null
            // 需要等待就 return YourCmdCo(cmd);（一个 IEnumerator）

        default:                              // VNScriptRunner.cs:2510
            Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知命令「{cmd.keyword}」");
            return null;
    }
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2174](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2174) · [VNScriptRunner.cs:2510](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2510)

返回值语义：`null` = 这条命令瞬间完成；`IEnumerator` = 主循环
[`VNScriptRunner.Run`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2133](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2133)）`yield return` 它，
行尾 `@` 的异步语义也由主循环处理。

注意 `default` 分支（同档:2510）只在**关键字已进白名单**时才可能到达，
所以它捕捉不到步骤 1 漏掉的情况。

取参数一律用 [`VNScriptCommand`](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs) 上的辅助方法：`Arg(i, def)`（同档:63）、
`ArgF(i, def)`（:65）、`Kw(key, def)`（:71）、`KwF(key, def)`（:74）。

#### 步骤 4：实现落到 VNStage 或组件

演出类命令的实现一律挂在 [`VNStage`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs)（Assets/Project/Scripts/VNEffects/Script/VNStage.cs）上，
`VNStage` 再转发给具体组件。这样 Runner 只认识 `VNStage` 一个门面。

新组件的引用加到 `VNStage` 的「舞台引用」区（例如 `eventRegistry` 在同档:111），
并在 `AutoWire()` 里补一句自动查找 —— 注解说明了为什么：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNStage.cs:180-182（注解）
/// 自动补线：Inspector 里为空的引用自动在场景中查找。
/// 这样给 VNStage 加新字段后，旧场景不重新生成也能正常工作。
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:180](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L180)

#### 步骤 5：存档 + 调试重建（只有持续状态才需要）

见 §18。判据一句话：**读档后画面上还应该看得到它 → 要进存档**。

#### 步骤 6：剧本编辑器 Schema

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs（静态建构式内，:360 起）
Add("yourcmd", "Scene",                        // category = Add 菜单分组
    "yourcmd <id> [time:秒]\n说明文字会变成 tooltip",
    Pos("id", "id", VNParamSource.Background),          // 位置参数
    Kw("time", "秒", VNParamSource.Number, weight: 0.5f)); // key:value 参数
```

- `Pos` 在同档:321、`Kw` 在同档:330、`Add` 在同档:351
- [`VNParamSource`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs)（同档:7）决定编辑器画什么控件、下拉候选从哪来
- 需要指向某类定义资产时用 `KwAsset`（同档:342）：

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:342
static VNParamDef KwAsset(string id, string label, string assetType, string assetIdField,
    float weight = 1f)
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:342](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L342)

  第 3、4 个参数是**类型名字串**与 **id 栏位名字串**。注解说明为什么要显式写死
  （`VNParamDef.assetType`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:53`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L53)） / `assetIdField` 上方，同档:51-53）：
  「字段名各资产不统一（quizId / badmintonId / themeId / id …），所以显式写死，
  不用反射猜 —— 猜错的表现是下拉里一片空白，很难联想到原因。」

- 候选只是提示、认不出不该报错的参数，包一层 `Soft`（`VNParamDef.softRef`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:48`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L48)），同档:48；
  辅助函式 `Soft`，同档:339）
- 需要「附属 `*` 行」的命令，`Add(...)` 之后补一句
  `ByKeyword["yourcmd"].blockChoice = true;`（照 `event` 的做法，同档:665）

#### 步骤 7：Lint 规则

`CheckAssets`（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:502](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L502)）
是主检查体，按 `c.keyword` 分支。加一条：

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs（CheckAssets 内）
case "yourcmd":
    CheckId(issues, f, c.line, c.Arg(0), reg.backgrounds,
            "unknown-yourcmd-id", "把图放进背景库后重跑 Rescan Asset Folders。");
    break;
```

辅助函式：`CheckId`（同档:806）、`CheckCharacter`（:834）、`CheckExpression`（:846）、
`CheckEnum<T>`（:782）、`CheckSide`（:796）、`Suggest`（:1614）、
统一入口 `Add`（:1642）。写了 `${flag}` 的动态值会被 `Dynamic`（:1604）跳过。

**Lint 不是可选的。** 本专案绝大多数「静默失效」都是 id 拼错，
而运行时的表现是**什么都不发生**，没有异常也没有日志。

#### 步骤 8：文档

`HowToUse.md` 加语法说明，`WhatAiDo.md` 加一章（走 `vn-doc-update` 技能的模板）。

### 5.3 验证

1. 编译通过
2. `Ctrl+Shift+L` Lint 全过
3. 剧本编辑器里那一行画得出参数格（不是一条灰色 raw 文本）
4. `F5` 从选中行播放（`VNScriptRunner.PlayFromSourceLine`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:212`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L212)），
   [Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:245](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L245)），看演出对不对
5. 存档 → 读档 → 状态还在（如果它该进存档）

---

## 6. 类别 D：新玩法事件模块

> 对应技能：`vn-new-event-module`。这是本专案**最常走**的扩展流程 ——
> 羽毛球、大头贴、擦雾、限时问答、亲密互动、AI 聊天全是这条路径出来的。

### 6.1 契约

一个事件模块 = 一个 [`VNEventModule`](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs) 子类
（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:52](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L52)）。

生命周期（注解在同档:43-45）：Runner 从 [`VNEventRegistry`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs) 实例化到事件层 → `Launch` →
模块自行交互 → 子类调 `Done(结果名)` → Runner 销毁模块并按结果分支。

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:62
public void Launch(VNEventContext ctx, Action<string> onDone)
{
    _onDone = onDone;
    _finished = false;
    OnLaunch(ctx);

    // 教程必须在 OnLaunch **之后**播：要高亮记分板，记分板得先存在。
    string tid = ctx != null ? ctx.Kw("tutorial", tutorialId) : tutorialId;
    if (!string.IsNullOrEmpty(tid)) VNTutorialPlayer.PlayAuto(tid, this);
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:62](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L62)

你要实现的只有一个抽象方法 `OnLaunch`（同档:76）。
结束时调 `Done(outcome)`（同档:79）—— **重复调用被忽略**（`_finished` 守卫）。

上下文 `VNEventContext`（同档:8）给你：`eventId` / `stage` / `kwargs` / `outcomes` / `line`，
以及取参数的 `Kw`（同档:21）、`KwF`（:24）、`KwI`（:31）与
`AcceptsOutcome`（:18，「剧本是否用 `*` 行接住了该结果名」）。

### 6.2 模块三铁律

注解原文在 [Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:47](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L47)-50：

```
约定：
  - 模块只操作自己的 UI 子树与 VNFlags，不直接改舞台演出（背景/立绘交给事件前后的剧本行）
  - 计时用 unscaledTime、Tween 用 SetUpdate(true)，不受快进 DOTween.timeScale 影响
  - 所有 Tween SetLink(gameObject) 防泄漏（模块随时可能被销毁）
```

**铁律①「不碰舞台」有两个官方例外**，两个都是深思熟虑后破例并收紧边界的：

- [`VNAiTalkModule`](Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs)（Assets/Project/Scripts/VNEffects/Script/VNAiTalkModule.cs）：
  直接驱动舞台立绘换表情。原因是自绘立绘要把眨眼／口型／色调匹配／出场动画全部重接一遍。
  边界收紧为「只碰表情和对话框内容」，且**正常结束／ESC／`CancelForDebug` 三条路径都还原原表情**。
- [`VNInteractionModule`](Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs)（Assets/Project/Scripts/VNEffects/Script/VNInteractionModule.cs）：同上，
  边界为「只碰表情与叠加层，三条退出路径都还原」。

**铁律②的落地是 `VNTime.Delta`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:144`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L144)）**（[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:144](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L144)），
不是裸的 `Time.unscaledDeltaTime`：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNPause.cs:144
public static float Delta =>
    VNPause.IsPaused ? 0f : Mathf.Min(UnityEngine.Time.unscaledDeltaTime, MaxStep);
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:144](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L144)

它同时做了两件事：受 [`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) 冻结（教程弹窗），以及单帧上限 `MaxStep`（防瞬移，
收编自羽毛球）。

**每个模块的 Update 第一行必须是暂停早退，而且必须在读输入之前**
（范例 [`VNQuizModule.Update`](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs)，[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:201](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L201)）：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:201-206
void Update()
{
    if (VNPause.IsPaused) return;        // 教程讲解中：倒计时与数字键一起冻住
    // ...
    _timeLeft -= VNTime.Delta;           // 不受快进 timeScale 影响、受 VNPause 冻结
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:201](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L201)

> **为什么不能用 `Time.timeScale = 0`**：铁律②规定模块用 unscaled 时间，
> 于是 timeScale 对它们**一律无效** —— 球照飞、倒计时照跑。
> 真正能冻住的只有「模块自己早退 + dt 归零」。

`VNPause` 的句柄绑宿主对象、宿主销毁即失效（`Acquire`，同档:70 / `Release`，:85），
另有 Runner 的 [`ReleaseAll()`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L99) 兜底（:99）。**释放路径有五条**
（正常结束／ESC／`CancelForDebug`／被 Destroy／换场景），**漏一条就是游戏永久卡死**。

### 6.3 结果契约

剧本写：

```
event yourmodule id:xxx time:30
* 胜利 -> 好结局
* 失败 -> 坏结局
* 逃跑
```

`*` 行的文字就是结果名，被 Parser 塞进 `cmd.options`，Runner 转成
`ctx.outcomes`（[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:13](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L13)）。
模块可以据此**只开放本次剧情接住的分支**（`AcceptsOutcome`，同档:18）。

`EventCo`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2821](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2821)）的收尾逻辑：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2871-2887（节录）
if (recordInBacklog)
    _backlog?.Record(VNLocale.T("backlog.event"), $"{id} → {result}");
if (int.TryParse(result, out int numeric))
    VNFlags.Set("事件结果", numeric);

if (cmd.options == null || cmd.options.Count == 0) yield break;
foreach (var opt in cmd.options)
{
    if (opt.text != result) continue;
    if (!string.IsNullOrEmpty(opt.flagOp)) VNFlags.Apply(opt.flagOp);
    if (!string.IsNullOrEmpty(opt.jumpLabel)) JumpTo(opt.jumpLabel, opt.line);
    yield break;
}
Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：事件「{id}」返回结果" +
                 $"「{result}」没有对应的「* 结果行」，顺序继续");
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2871](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2871)

> **结果名对不上只有一条 Warning，然后顺序继续。** 这是本专案头号静默失效来源，
> 所以 Lint 里有一张 `BuiltinOutcomes` 表
> （[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:64](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L64)）专门校验拼写。
> **加新模块务必往这张表补一行。**

另外 `EventCo` 在启动时会 `SetSkip(false)` / `SetAuto(false)`
（同档:2841-2842，注解「到玩法必停，同 choice」）、藏对话框、藏玩法 HUD，
结束后再还原。这些你不用管。

### 6.4 端到端步骤（照抄 quiz 这一套）

#### 步骤 1：写模块类

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNYourModule.cs（新档）
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：<一句话说明>。
    /// 剧本用法：
    ///   event yourmodule id:xxx time:30
    ///   * 胜利 -> 好结局
    ///   * 失败
    /// kwargs：id: 定义资产 / time: 限时 / flag: 成绩前缀
    /// 结果："胜利" / "失败"
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 + SetUpdate(true) / 全部 Tween SetLink。
    /// </summary>
    public class VNYourModule : VNEventModule
    {
        [Header("本模板登记的定义资产（event yourmodule id:xx 按 yourId 查找）")]
        public List<VNYourDef> defs = new List<VNYourDef>();

        VNYourDef _def;
        float _timeLeft;

        protected override void OnLaunch(VNEventContext ctx)
        {
            // ① 覆盖语义：资产里填了就用资产的（重建场景不丢）
            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.yourDefs, ref defs);

            // ② 找定义资产；找不到必须 Done("") 早退，不能卡住剧本
            string id = ctx.Kw("id");
            foreach (var d in defs)
                if (d != null && d.yourId == id) { _def = d; break; }
            if (_def == null)
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：没有 id「{id}」的定义资产，直接返回");
                Done("");
                return;
            }

            // ③ 读剧本参数（ctx.Kw / KwF / KwI，每加一个都要同步 EventVariants 表）
            _timeLeft = ctx.KwF("time", _def.defaultTime);

            // ④ 建 UI（全部挂自己底下，且 raycastTarget = false）
            BuildUi();
        }

        void Update()
        {
            if (VNPause.IsPaused) return;   // ★ 必须在读输入之前
            _timeLeft -= VNTime.Delta;      // ★ 不用 Time.deltaTime
            if (_timeLeft <= 0f) Finish("失败");
        }

        void Finish(string outcome)
        {
            // 结算演出后再 Done（Done 一调，Runner 立刻 Destroy 本物体）
            DOVirtual.DelayedCall(1.4f, () => Done(outcome), true)
                     .SetLink(gameObject);  // ★ 防泄漏
        }

        /// <summary>剧本被停止/调试中断时的清理（随后本物体被销毁）</summary>
        public override void CancelForDebug()
        {
            // 藏过系统光标 / 改过舞台表情 / 持有 VNPause 句柄的，在这里全部还原
        }
    }
}
```

参照实作：`VNQuizModule`（[Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs:34](Assets/Project/Scripts/VNEffects/Script/VNQuizModule.cs#L34)），
`OnLaunch` 在同档:83，两处早退 `Done("")` 在同档:99 与:108，
结算延时 `Done` 在同档:358。

三个必须遵守的收尾点：

- **找不到资产要 `Done("")` 早退**。不 Done 的话剧本永远卡在
  `while (result == null) yield return null;`
  （[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2861](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2861)）。
- **纯流程控制型模块要覆写 `RecordInBacklog => false`**
  （[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:91](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L91)）。
  注解举的例子：`event plan op:next` 一周会调 7 次，全记进回想就是 7 条无意义条目。
- **有藏系统光标 / 有 `VNPause` 句柄 / 有挂在立绘下的可视化物件时，
  `Dispose()` 要在四处都调**：`Finish` · `CancelForDebug` · `OnDestroy` · `OnDisable`。
  漏一条的后果分别是：玩家鼠标指针永久消失（[`VNTouchCursor`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs)，
  Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs）、游戏永久卡死（`VNPause`）、
  部位框留在角色脸上（`VNInteractionModule`）。

#### 步骤 2：射线层级的坑

事件层排序是 **60**（`VNEventRegistry.LayerSortingOrder`（[`Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:27`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L27)），
[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:27](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L27)），
位于选项面板 45 与全屏转场 100 之间 —— 所以进出事件可以用全屏转场包裹（注解在同档:9-11）。

**模块自绘的一切 UI 默认要 `raycastTarget = false`**，否则会吃掉底下选项面板的点击。
唯一例外是模块自己的 ESC 确认框。

层是懒建的（`EnsureLayer`，同档:72），`Create`（同档:42）会把模块 rect 拉满四边再
`SetActive(true)`（同档:59-68）。找不到 id 只告警不抛（同档:49-51）。

#### 步骤 3：写装机器（Editor）

**不要为了「注册表多一条」去重建整个场景** —— 那会丢掉手工整理过的 Hierarchy。
照抄 [`VNQuizInstaller`](Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs:21](Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs#L21)），
它只做三件事（注解在同档:15-19）：

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs:26
[MenuItem("Tools/VN Effects/场景装机 Install To Scene/限时问答 Quiz Module", priority = 140)]
public static void Install()
{
    var registry = Object.FindFirstObjectByType<VNEventRegistry>(FindObjectsInactive.Include);
    if (registry == null) { /* 提示先打开剧本场景，:33 */ return; }

    // ★ 必须带 RectTransform：模块 BuildUi 里直接 (RectTransform)transform，
    //   普通 Transform 会在运行时抛 InvalidCastException。   VNQuizInstaller.cs:63-65
    var go = new GameObject(TemplateName, typeof(RectTransform));
    go.transform.SetParent(registry.transform, false);
    module = go.AddComponent<VNQuizModule>();
    go.SetActive(false);        // 模板保持禁用，运行时 Instantiate 后才激活   :68

    entry = new VNEventRegistry.Entry { id = ModuleId, template = module };   // :73
    registry.modules.Add(entry);

    module.quizzes = new List<VNQuizDef>(allQuizzes);                          // :86
    EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);              // :89

    // 登记进 VNGameConfig（重建场景也不会丢）
    config.quizzes = new List<VNQuizDef>(allQuizzes);                          // :96
    VNGameConfig.ClearCache();                                                 // :99
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs:26](Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs#L26) · [VNQuizInstaller.cs:63](Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs#L63)

五个细节都是踩过的：

1. **模板必须带 `RectTransform`**（同档:63-65 的星号注解）
2. **模板必须 `SetActive(false)`**（同档:68）—— 运行时 `Instantiate` 后才激活
3. **重复执行安全** —— 已装过就只刷新资产列表（同档:80-84）
4. **要 `MarkSceneDirty`**（同档:89），并在对话框里提醒使用者 Ctrl+S（同档:107）
5. **最后一定要 `VNGameConfig.ClearCache()`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:69`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L69)）**（同档:99），否则 Inspector 改完不生效

`ModuleId`（同档:23）就是剧本 `event <id>` 里写的那个 id。

#### 步骤 4：Schema 加模块专属参数

`event` 是通用入口，各模块的 `vs:` / `target:` / `powerstat:` 走**变体表**。
注解把设计理由写得很清楚
（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:97](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L97)-101）：
「全塞进一张表的话每个 event 行都会画出二十几个格子；一个都不写又会让它们
全变成 unrecognized token 警告，只能在一长条文本里手打。」

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:149
static readonly Dictionary<string, VNParamDef[]> EventVariants =
    new Dictionary<string, VNParamDef[]>
    {
        ["qte"] = new[]                                     // :152
        {
            Kw("target", "目标", VNParamSource.Number, weight: 0.5f),
            Kw("time", "秒", VNParamSource.Number, weight: 0.5f),
            Kw("title", "标题", VNParamSource.Text, weight: 0.8f),
        },
        // ...
        ["yourmodule"] = new[]                              // ← 加这里
        {
            KwAsset("id", "定义", "VNYourDef", "yourId"),
            Kw("time", "秒", VNParamSource.Number, weight: 0.5f),
            Kw("flag", "成绩前缀", VNParamSource.Text, weight: 0.7f),
        },
    };
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:149](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L149)

> **这张表的唯一真相是各模块 `OnLaunch` 里的 `ctx.Kw(...)`**（注解在同档:146）。
> 每加一个 `ctx.Kw("xxx")` 就补一行，编辑器界面自动长出控件、Lint 也不再报未知 token。

合并逻辑在 `Find(keyword, variant)`（同档:106），结果有缓存
（`_eventVariantCache`，同档:141）；编辑器靠 `HasEventVariant`（同档:131）
与 `EventBaseParamCount`（同档:135）决定要不要多画一行、从哪里切分。
`EventKwargUniverse`（同档:139）是全部模块专属 kwarg 的键名总表，
换模块 id 时靠它认出「这是别的模块的参数」，**保住玩家写过的值而不是静默丢掉**。

> **⚠️ 位置参数的存储键是 `module` 不是 `id`**（注解在同档:659-661）：
> ```csharp
> // [Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:662](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L662)
> Pos("module", "模块", VNParamSource.EventId),
> ```
> badminton / quiz / shop / plan / interact 这些模块**自己就有一个 `id:` 参数**
> （对手资产 / 题库 / 商店…），同名会互相覆盖，保存时写出 `event 新手 id:新手` 这种烂行。

模块 id 的下拉候选来自 [`VNParamSource.EventId`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs)，扫场景 `VNEventRegistry.modules`（[`Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:25`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L25)）
（`VNEventRegistry.Ids`，[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:32](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L32)）
—— 所以**步骤 3 的装机器先跑过一次，模块 id 才会出现在下拉里**。

`event` 行的基础定义在同档:638，`blockChoice = true` 在同档:665
（复用 choice 的「`*` 行」编辑与行号换算）；
通用参数 `tutorial:` 在同档:664，由基类实现，与具体模块无关。

#### 步骤 5：Lint 补三处

①结果名表：

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:64
static readonly Dictionary<string, HashSet<string>> BuiltinOutcomes =
    new Dictionary<string, HashSet<string>>
    {
        ["qte"] = new HashSet<string> { "success", "fail" },
        ["shop"] = new HashSet<string> { "离开" },
        // ...
        ["yourmodule"] = new HashSet<string> { "胜利", "失败" },   // ← 加这里
    };
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:64](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L64)

校验发生在 `CheckEvents` 的尾段（同档:1119 取表、:1128 报 `bad-event-outcome`）。

②id 校验，照 quiz 的写法（同档:925 起）加进 `CheckEvents`（同档:907）：

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs（CheckEvents 内）
if (module == "yourmodule")
{
    string defId = c.Kw("id");
    if (!string.IsNullOrEmpty(defId) && !Dynamic(defId) &&
        reg.yourIds.Count > 0 && !reg.yourIds.Contains(defId))
        Add(issues, VNLintSeverity.Warning, "unknown-yourdef", f, c.line,
            $"没有 id 为「{defId}」的定义资产",
            $"当前已有：{string.Join(" / ", reg.yourIds.OrderBy(s => s))}");
}
```

③`reg.yourIds` 要先在 [`Registry`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs) 类（同档:100）加一个 `HashSet<string>`，
并在 `CollectRegistry`（同档:231）里填上。

未注册的模块 id 本身已有检查（`unknown-event-module`，同档:917）——
但它有个前提 `reg.sceneRegistryFound`（同档:105 的旗标），场景里没有 `VNEventRegistry` 时跳过。

#### 步骤 6：模块不进存档

事件模块**不需要做存档兼容** —— 事件期间不能存档（存档只在台词处允许）。
调试重建时 `event` 走 `hasBranching` 分支不重放
（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:613](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L613)，
注解：「事件结果无法推断，不重放」）。

模块要落地的持续状态一律写 flag（§2.3），flag 本来就随存档走。

#### 步骤 7：写示范剧本 + 文档

`Assets/Scenarios/YourModuleDemo.vn.txt`（照 `QuizDemo.vn.txt` / `BadmintonDemo.vn.txt`）。
Rescan 会自动把它登记进 `config.chapters`。

### 6.5 中途插播演出：RunInlineCo

模块想在交互中途播一句台词 / 一个特效，不要自己复刻演出逻辑，
调 [`VNScriptRunner.RunInlineCo`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:291](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L291)）。

它逐条走**与主循环同一个 `Dispatch`**，所以命令语义永远一致，行尾 `@` 也照旧。
控制流命令被挡掉（`InlineBlockedKeywords`，同档:280）：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:280
static readonly HashSet<string> InlineBlockedKeywords = new HashSet<string>
{
    "jump", "choice", "call", "return", "label", "event",
    "save", "load", "chapter", "endgame",
};
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:280](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L280)

理由（注解在同档:275-278）：调用方此刻正被主协程 yield 等待着，
让这些命令跑起来会把 `_index` / `_callStack` / 存档状态搅乱，
症状是「事件结束后剧本跳到莫名其妙的地方」。

---

## 7. 类别 E：新定义资产（通用模板）

「一类可配置数据」在本专案永远是一个 `ScriptableObject`。
既有的 21 个见 [附录 A](#附录-a定义资产总表)。加一个新的：

### 步骤 1：写资产类

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNYourDef.cs（新档）
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// <一句话：这类数据是什么、剧本怎么引用、状态存哪里>
    /// 剧本用法：event yourmodule id:&lt;yourId&gt;
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Your Definition", fileName = "NewYour")]
    public class VNYourDef : ScriptableObject
    {
        [Header("剧本引用的 id（可中文，永远不翻译）")]
        public string yourId;

        [Header("显示名；留空 = 直接用 id")]
        public string displayName;
        [Header("英文/日文显示名（留空回退中文）")]
        public string displayNameEn;
        public string displayNameJa;

        [Header("默认时长（秒），剧本 time: 可覆盖")]
        public float defaultTime = 30f;

        /// <summary>当前语言的显示名（三段式回退：当前语言 → 中文 → id）</summary>
        public string DisplayName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(displayName) ? yourId : displayName;
            }
        }
    }
}
```

> **⚠️ 列表元素里的字段说明一律用 `[Tooltip]`，绝对不要用 `[Header]`。**
> 注解原文在 [Assets/Project/Scripts/VNEffects/Script/VNStage.cs:24](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L24)-27：
> `[Header]` 是 `DecoratorDrawer`，Unity 画它的方式是把控件区域往下推（约 26px）。
> 列表里每一项都会重画一遍，既让一个条目占掉 6~7 行，
> 又会在自定义 drawer 用固定 rect 画子属性时把控件推出 rect ——
> 表现为**文字叠印且输入框点不进去**。
> 顶层字段用 `[Header]` 没问题；`[System.Serializable]` 的内嵌类里一律 `[Tooltip]`
> （范例：[`VNGameConfig.UiSkinEntry`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)，
> [Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:116](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L116)）。

### 步骤 2：VNGameConfig 加库

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs（「玩法」区，:205 附近）
[Header("你的定义库（event yourmodule id: 引用）")]
public List<VNYourDef> yourDefs = new List<VNYourDef>();
```

### 步骤 3：Rescan 自动登记

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:178（RescanAssetFolders 内）
config.yourDefs = FindAll<VNYourDef>();
report.Add($"你的定义 ×{config.yourDefs.Count}");
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:178](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L178)

`FindAll<T>`（同档:223）扫全工程的该类型资产，所以放哪个目录都行。

### 步骤 4：使用方 ApplyList

在模块 / 组件的 `OnLaunch` / `Awake` 里：

```csharp
var cfg = VNGameConfig.Active;
if (cfg != null) VNGameConfig.ApplyList(cfg.yourDefs, ref defs);
```

### 步骤 5：Inspector 分页（可选）

[`VNGameConfigEditor`](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs)（Assets/Project/Scripts/VNEffects/Editor/VNGameConfigEditor.cs）
的页签**只登记字段名**，绘制仍走 `PropertyField` ——
所以没被认领的新字段会自动落到「其他」页而不是静默消失。
想让它出现在「玩法」页，往对应页签的字段名清单里补一行。

### 步骤 6：Schema 的 KwAsset + Lint 的 Registry

见 §6 步骤 4、5。`KwAsset` 的第 4 个参数要填**你这个类的 id 栏位名**（这里是 `yourId`）。

### 步骤 7（可选）：紧凑 drawer

条目多的库（背景／CG／音频／UI 皮肤）有自定义单行 drawer
（[`VNConfigEntryDrawers`](Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs)，Assets/Project/Scripts/VNEffects/Editor/VNConfigEntryDrawers.cs）。
两个经验：Sprite 缩略图**不要用 `AssetPreview`**（异步，几十张一起等会闪空白），
Sprite 自己知道在哪张 texture 的哪个 UV，`DrawTextureWithTexCoords` 同步画即可；
音频没这捷径，只能异步 + 占位，但要自己给一个超时窗口，不能无限等。

---

## 8. 类别 F：新养成属性

### 8.1 关键认知：属性值不存在资产里

[`VNStatDef`](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs:21](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs#L21)）**只管钳制范围与显示规则**，
数值本体全部存 [`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs)，flag 名 = 属性 id（注解在同档:16-17）。
因此存档、`if` 分支、`choice` 的 `cost:`、调试重建**零改动复用**。

**没有定义资产的属性也能用 `stat` 命令照常读写**（同档:18），只是不钳制、无 HUD 条目。
所以「先在剧本里随便用，觉得需要 UI 了再补资产」是合法路径。

### 8.2 步骤

**① 建资产**：`Create → VN → Stat Definition`（`CreateAssetMenu`，同档:20），
存 `Assets/Art/VNEffects/Stats/<属性名>.asset`。

**② 填栏位**（行号都在 Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs）

| 栏位 | 行号 | 说明 |
|---|---|---|
| `id` | :32 | = flag 名，可中文（`金钱` / `压力` / `智力`） |
| `displayName` / `displayNameEn` / `displayNameJa` | :35 / :38 / :39 | HUD 显示名（`DisplayName`，:68） |
| `icon` | :42 | 留空 HUD 用色块代替 |
| `color` | :45 | 数值条与图标底色 |
| `useClamp` / `minValue` / `maxValue` | :48 / :49 / :50 | 写入时截断（`Clamp`，:80） |
| `initialValue` | :53 | 进游戏／读档后 flag 不存在时自动写入（`VNStatsHud.EnsureInitials`（[`Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:96`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L96)），[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:96](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L96)） |
| `style` | :56 | `Number` / `Percent` / `OutOfMax` / `Grade`（枚举 `VNStatStyle`，同档:7） |
| `unit` | :59 | `Number` 样式的后缀，如 `G`（`Format`，:96） |
| `gradeSteps` | :62 | `Grade` 样式的阈值表，**按 threshold 从小到大填**（`GradeOf`，:84） |
| `showInHud` | :65 | 不勾则只出现在 C 键属性面板 |

条形进度走 `Normalized`（同档:107）：`Number` 样式（金钱类）或未钳制时返回 -1 = 不画条。

**③ Rescan** → 自动进 `config.stats`
（`VNGameConfig.stats`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:206`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L206)），[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:206](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L206)）

**④ 剧本里用**

```
stat 金钱 +500
stat 压力 -10
stat 智力 50
if 智力>=60 jump 考试通过
choice
* 买参考书 cost:金钱-300 flag:智力+5
* 算了
```

`stat` 的分派在 [Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2482](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2482)：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2482-2485
case "stat":
    // stat 名字 +5 / stat 名字 -3 / stat 名字 500（按 VNStatDef 钳制 + 飘字）
    _statsHud?.Apply(cmd.Arg(0), cmd.Arg(1), false, cmd.line);
    return null;
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2482](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2482)

[`VNStatsHud.Apply`](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs:108](Assets/Project/Scripts/VNEffects/Script/VNStatsHud.cs#L108)）
负责钳制 + HUD 演出 + 左上角 Toast。
选项的 `cost:` 走 `ParseCostOp`（同档:170）与 `ApplyCost`（同档:206）。

**⑤ 调试重建里它是「静默重放」**

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:588-589
case "stat": // 静默重放（钳制照做，不弹 Toast）
    _statsHud?.Apply(cmd.Arg(0), cmd.Arg(1), true, cmd.line);
    break;
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:588](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L588)

第 3 个参数 `silent` 的意义：重建前置状态时数值要对，但不能把 20 条 Toast 一次性弹出来。
`flag`（同档:581）、`quest`（:584）、`time`（:591）同理。

> **你新增任何「会弹演出的状态写入命令」时，记得在 `RebuildStateBefore`
> （[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:326](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L326)）的 switch 里
> 也加一条 silent 重放。**

**⑥ 不需要做的事**：`stat` 命令与 Schema 已存在
（`Add("stat", ...)`，[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:617](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L617)）；
属性 id 没有专门的 Lint 校验（因为无资产也合法）。

---

## 9. 类别 G：新道具／装备／商店

### 9.1 结构

道具**不是独立资产**，是 [`VNShopDef.Item`](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs)
（[Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:75](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L75)）—— 一家商店的商品清单里的一项。
持有数存 flag `道具_<id>`（`ItemFlagName`，同档:72；前缀常数 `ItemFlagPrefix`，同档:30）。

**「只当道具目录用、不上架贩卖」的商店也可以登记**（注解在同档:24-25）：
物品栏／装备系统按 id **跨全部商店**取文案与装备数据。所以「加一件道具」的最小做法是
建一个 `VNShopDef` 当作道具目录，不接 `event shop` 也行。

### 9.2 加一件普通道具

1. `Create → VN → Shop Definition`（同档:27）或打开既有的
2. `items` 列表（同档:166）加一项，填（行号都在 Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs）：
   - `id`（:78）—— = flag `道具_<id>`，可中文，**永不翻译**
   - `displayName` / `displayNameEn` / `displayNameJa`（:81 / :83 / :84）
   - `description` 三语（:87 / :88 / :89）
   - `icon`（:92）—— 留空用色块
   - `price`（:95）／ `sellPrice`（:98，0 = 本店不收购）／ `maxOwned`（:101，0 = 不限）
   - `condition`（:104）—— 上架条件，[`VNFlags`](Assets/Project/Scripts/VNEffects/Script/VNFlags.cs) 表达式（如 `好感度>=2`），留空 = 总是上架
3. Rescan → 进 `config.shops`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:207](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L207)）

当前持有数直接读 `Item.Owned`（[`Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:150`](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L150)）（同档:150）。

### 9.3 让它可装备

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:7
public enum VNEquipSlot
{
    None = 0,      // 不可装备
    Head = 1, Face = 2, UpperBody = 3,
    Hands = 4, LowerBody = 5, Feet = 6, Special = 7,
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:7](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L7)

- `equipSlot`（同档:107）设成 `None` 以外 → `IsEquippable`（同档:123）为真，
  I 键背包右键菜单出现「装备」
- `statBonuses`（同档:110）—— `List<StatOp>`（`StatOp`，同档:34），穿上直接写属性
- `passiveEffects`（同档:113）—— `List<PassiveEffect>`（`PassiveEffect`，同档:45），
  合计写 flag `装备效果_<effectId>`，**生效逻辑由剧本 `if` 判断**，不在程式码里
  （效果说明三语走 `DisplayLabel`，同档:59）

三个 flag 一起构成装备状态（[`VNEquipment`](Assets/Project/Scripts/VNEffects/Script/VNEquipment.cs)，
Assets/Project/Scripts/VNEffects/Script/VNEquipment.cs，纯静态）：

| flag | 含义 |
|---|---|
| `装备_<道具id>` | = 部位编号（`VNEquipSlot` 的整数值） |
| `装备实增_<…>` | 实际生效的加成量 —— **卸下时按这个扣回** |
| `装备效果_<effectId>` | 特殊效果合计 |

> **为什么要 `装备实增_`**：属性有 `useClamp`。穿上 +10 但被 max 挡住只实际涨了 3，
> 卸下时扣 10 就会亏 7。记录实增量是唯一正确解。

### 9.4 让它可使用

`useOps`（同档:117，`List<StatOp>`）非空 → `IsUsable`（同档:126）为真。
`consumeOnUse`（同档:120）决定用完是否扣 1。

### 9.5 剧本里用

```
event shop id:服装店
* 离开

if 道具_药水>=1 jump 有药
if 装备效果_夜视>=1 jump 看得见
```

`* 离开` 是 shop 唯一的结果名
（`BuiltinOutcomes["shop"]`，[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:67](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L67)）。

`shopId`（同档:154）是 `event shop id:` 引用的 id；
`currencyStat`（同档:163）默认 `金钱`，指向一个 [`VNStatDef`](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs) 的 id。
Schema 端 shop 的变体只有一个 `KwAsset("id", "商店", "VNShopDef", "shopId")`
（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:163](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L163) 附近的 `["shop"]` 条目）。

---

## 10. 类别 H：新任务

### 10.1 结构

[`VNQuestDef`](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs:14](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs#L14)）**只管显示文案**，
进行状态全部存 flag `任务_<id>`
（`VNQuestLog.FlagName`（[`Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:55`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L55)），[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:55](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L55)）。

阶段号约定：

| 值 | 含义 | 常数 |
|---|---|---|
| 0 | 未接取 | — |
| 1..n | 进行中，`stages[阶段-1]` 是当前目标文案 | — |
| 100 | 完成 | [`VNQuestLog.StageDone`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs)（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:18](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L18)） |
| -1 | 失败 | `VNQuestLog.StageFailed`（[`Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:19`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L19)）（同档:19） |

查询走 `VNQuestLog.StageOf`（[`Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:58`](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L58)）（同档:58）。
**没有定义资产的任务也能正常运作**（注解在 [Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs:11](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs#L11)），
只是日志／Toast 用 id 当标题、无阶段文案。

### 10.2 步骤

1. `Create → VN → Quest Definition`（同档:13），存 `Assets/Art/VNEffects/Quests/`
2. 填 `id`（:17）／ `title`（:20）／ `description`（:24）／ `stages`（:27）
   —— **第 1 项对应阶段 1**（`quest start` 后的初始阶段）
3. 三语：`titleEn`（:30）／ `titleJa`（:37）、`descriptionEn` / `descriptionJa`（:32 / :39）、
   `stagesEn`（:34）／ `stagesJa`（:41）
   —— **与中文 `stages` 一一对应，缺项回退中文**（`StageText`，:65）
4. Rescan → 进 `config.quests`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:209](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L209)）
5. 剧本：

```
quest start 告白大作战
quest stage 告白大作战 2
quest done 告白大作战
quest fail 告白大作战

if 任务_告白大作战>=2 jump 已经准备好了
if 任务_告白大作战==100 jump 已完成
```

`quest` 的分派在 [Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2498](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2498)，落到
`VNQuestLog.Apply(op, id, stage, silent, line)`
（[Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs:72](Assets/Project/Scripts/VNEffects/Script/VNQuestLog.cs#L72)）。
`quest start <id> 2` 可以直接从阶段 2 开始（同档:86）。

6. Schema 已存在（`Add("quest", ...)`，
   [Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:688](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L688)），
   id 下拉走 [`VNParamSource.QuestId`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs) 扫资产，自动出现
7. J 键看任务日志

**调试重建**里 quest 也是静默重放
（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:584](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L584)）。

---

## 11. 类别 I：新 UI 皮肤

> 对应技能：`vn-ui-skin`。**这里有两条完全独立的线，别搞混。**

| | 对话框／选项皮肤 | 系统菜单主题 |
|---|---|---|
| 载体 | prefab 根挂 [`VNDialogueSkin`](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs) / [`VNChoiceSkin`](Assets/Project/Scripts/VNEffects/Script/VNChoiceSkin.cs) | 一个 [`VNSystemUiSkinSet`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs) 资产（[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:11](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs#L11)） |
| 登记 | `VNGameConfig.dialogueSkins`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:126`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L126)） / `choiceSkins`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:126](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L126) / :128） | `VNGameConfig.systemUiSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)）（同档:131），**全局唯一** |
| 切换 | 剧本 `ui dialogue\|choice <id>` | 不能在剧本里切 |
| 进存档 | 是 | 否 |

### 11.1 对话框／选项皮肤

**① 拿起步模板**

`Tools → VN Effects → UI 皮肤 UI Skins → 导出皮肤模板（默认+样例）Export Skin Prefabs`
（[`VNUiSkinExporter`](Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs)，Assets/Project/Scripts/VNEffects/Editor/VNUiSkinExporter.cs）——
烘焙贴图 + 生成默认／顶部／右列样例并自动登记。
另有 `导出无框渐变皮肤（白·粉·黑）Export Soft Gradient Skins`
（[`VNSoftSkinExporter`](Assets/Project/Scripts/VNEffects/Editor/VNSoftSkinExporter.cs)，Assets/Project/Scripts/VNEffects/Editor/VNSoftSkinExporter.cs）。

**② 改 prefab**

`VNDialogueSkin`（[Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs:24](Assets/Project/Scripts/VNEffects/Script/VNDialogueSkin.cs#L24)）
是一个**纯槽位声明组件**，每个槽位都可以留空降级：

| 槽位 | 行号 | 留空的后果 |
|---|---|---|
| `panel` | :27 | 出场／退场动画作用于整个皮肤层 |
| `nameTag` | :30 | 旁白时无法整体隐藏名牌 |
| `nameText` | :32 | 不显示名字 |
| `bodyText` | :35 | **必需** —— 运行时自动挂 [`VNTypewriterText`](Assets/Project/Scripts/VNEffects/VNTypewriterText.cs) 打字机 |
| `arrow` | :38 | 无「读完呼吸浮动」的继续箭头 |
| `portraitWindow` / `portraitImage` | :41 / :43 | 此皮肤不显示头像 |
| `shineFrame` | :50 | 无流光边框（无框渐变皮肤就是留空它） |
| `toolbarAnchor` | :53 | 快捷功能条停靠到 `panel` |

`portraitBodyInset`（:45）／ `portraitTagShift`（:47）是显示头像时正文与名牌的避让量，
0 = 不避让。

**③ 登记**

[`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) 的「UI皮肤」页，`dialogueSkins` 加一条 `UiSkinEntry`
（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:116](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L116)）：
`id`（剧本引用名，可中文）+ `prefab`。查找走 `FindSkin`（同档:134）。

**④ 剧本切换**

```
ui dialogue 华丽
ui choice 右列
ui dialogue default
```

分派在 [`VNStage`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs) 的 `case "dialogue"`（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:768](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L768)）
与 `case "choice"`（同档:787）。找不到会告警：
「…没有在 `VNGameConfig.dialogueSkins` 登记（或 prefab 缺 `VNDialogueSkin`）」（同档:779）。

**⑤ 存档**

皮肤 id 进存档（`VNSaveData.dialogueSkin`（[`Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:58`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L58)），
[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:58](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L58)；`choiceSkin`，:59），
`RestoreSnapshot` **最先恢复 UI 皮肤**
（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:906](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L906)），
注解理由：「随后重放的台词/选项直接落在正确皮肤上」（同档:904）。
**这一段已经写好了，你加新皮肤不用改程式码** —— 存的是 id 字串。

**⑥ Lint**

`Registry.dialogueSkins`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:128`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L128)） / `choiceSkins`
（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:100](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L100) 的 [`Registry`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs) 类内）
已经在收集，`ui` 命令的 id 会被校验。

### 11.2 名牌样式（第三条线，最轻）

`ui name <样式|default>`，十套内置预设（[`VNNameplateStyle`](Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs)，
Assets/Project/Scripts/VNEffects/Script/VNNameplateStyle.cs），**不用登记任何东西**。
分派在 `VNStage` 的 `case "name"`（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:806](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L806)）——
注解特别指出「这里的 id 不是 VNGameConfig 里登记的 prefab」（同档:809）。
状态进存档（`VNSaveData.nameplateStyle`（[`Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:60`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L60)），
[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:60](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L60)）。

三条硬约定（改这块时必须遵守）：

1. 材质必须走 `text.fontMaterial` 实例 —— 改 `sharedMaterial` 会污染所有同字体文字
2. underlay 通道只有一条 → 「第二层外描边」与「投影」二选一
3. 改 underlay 前必须 `EnableKeyword("UNDERLAY_ON")`

另：三层字系列（Duo/Gold/Silver/Neon/Ink/Candy）的立身之本是**最外圈必须深色** ——
Bold/Outline 最外层是白的，遇到白背景或亮立绘整个消失。
金／银的金属感来自 TMP 的 Bevel + Lighting（Mobile 版 shader 没这组属性，
要用 `HasProperty` 挡掉并警告一次）；霓虹靠 HDR 写进 `_FaceColor` 触发 Bloom，
所以 **HDR 发光与上下渐变二选一**（顶点色被钳到 1）。

### 11.3 系统菜单主题

`VNSystemUiSkinSet`（[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:11](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs#L11)）
是一个 ScriptableObject，每个系统面板一个 prefab 槽位：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:14-35（节录）
public GameObject titleMenuPrefab;      // :14
public GameObject configPanelPrefab;    // :15
public GameObject cgGalleryPrefab;      // :16
public GameObject backlogPrefab;        // :17
public GameObject saveLoadPrefab;       // :18
public GameObject quickToolbarPrefab;   // :21
public GameObject statsHudPrefab;       // :22
public GameObject statsPanelPrefab;     // :25
public GameObject inventoryPrefab;      // :28
public GameObject planPrefab;           // :31
public GameObject resultPopupPrefab;    // :32
public GameObject tutorialPrefab;       // :35
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:14](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs#L14)

**降级规则是逐项的**：单项缺失或槽位无效时，只有那一项退回程序化 UI，其余照常。
安全实例化的基类是 [`VNSystemUiSkinBehaviour`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs)
（Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinBehaviour.cs）。

流程：

1. `Tools → VN Effects → UI 皮肤 UI Skins → 系统主题：导出默认模板 System UI: Export Default Prefabs`
   （[`VNSystemUiSkinExporter`](Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs)，Assets/Project/Scripts/VNEffects/Editor/VNSystemUiSkinExporter.cs）
2. 改 prefab（每种面板有自己的槽位组件：[`VNPlanSkin`](Assets/Project/Scripts/VNEffects/Script/VNPlanSkin.cs) / [`VNResultPopupSkin`](Assets/Project/Scripts/VNEffects/Script/VNResultPopupSkin.cs) /
   [`VNInventorySkin`](Assets/Project/Scripts/VNEffects/Script/VNInventorySkin.cs) / [`VNTutorialSkin`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSkin.cs) / [`VNSaveLoadSkin`](Assets/Project/Scripts/VNEffects/Script/VNSaveLoadSkin.cs) / [`VNBacklogSkin`](Assets/Project/Scripts/VNEffects/Script/VNBacklogSkin.cs) /
   [`VNStatsHudSkin`](Assets/Project/Scripts/VNEffects/Script/VNStatsHudSkin.cs) / [`VNCgGallerySkin`](Assets/Project/Scripts/VNEffects/Script/VNCgGallerySkin.cs) / [`VNQuickToolbarSkin`](Assets/Project/Scripts/VNEffects/Script/VNQuickToolbarSkin.cs) / [`VNConfigPanelSkin`](Assets/Project/Scripts/VNEffects/Script/VNConfigPanelSkin.cs) /
   [`VNTitleMenuSkin`](Assets/Project/Scripts/VNEffects/Script/VNTitleMenuSkin.cs)，全在 Assets/Project/Scripts/VNEffects/Script/ 下）
3. 建一个 `VNSystemUiSkinSet` 资产（`Create → VN → System UI Skin Set`，同档:10），
   把 prefab 拖进槽位
4. 填进 `VNGameConfig.systemUiSkin`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:131](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L131)）
5. 校验：`系统主题：校验全局主题 System UI: Validate Global Theme`

只重导排程／结算两项：`系统主题：导出排程·结算面板 System UI: Export Event Panel Prefabs`。
只重导设置面板：`系统主题：导出设置面板 System UI: Export Config Panel Prefab`。

---

## 12. 类别 J：新特效／演出组件

> 对应技能：`vn-new-effect`。

### 12.1 硬约定（写之前就要知道）

| 约定 | 说明 |
|---|---|
| 发光 = HDR 颜色（>1）+ Bloom（阈值 1.0） | uGUI 顶点色被钳到 1，HDR 必须走材质属性 |
| 会发光的粒子用 `VN/Additive`；**有实体的（花瓣/落叶/雨/雪）必须用 `VN/ParticleAlpha`** | 加法只能加亮不能遮挡背景；彩色粒子叠明亮背景后通道溢出会被 Bloom 洗成白色 |
| 调色一律走 `VNImageEffectController.SetGrade(通道, ...)` | **禁止直接写 `_Brightness` / `_Saturation`** —— 六方共用，谁最后写谁赢，症状是「说一句话立绘颜色就跳回去」 |
| 缩放走倍率机制 `DOScaleMultiplier`，别直接改 `localScale` | 说话者高亮与运镜分两个通道相乘，合成一个 float 的症状是「推完镜头一说话立绘尺寸就跳回去」 |
| 贴图全程序化生成（[`VNProceduralTextures`](Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs)，Assets/Project/Scripts/VNEffects/VNProceduralTextures.cs） | 零美术依赖 |
| 所有 Tween `SetLink(gameObject)`；循环效果 Start/Stop 成对 | |
| 运行时创建带 Awake 配置的组件：先 `SetActive(false)` 挂好赋值再激活 | |

### 12.2 步骤

**① 写组件**

放 `Assets/Project/Scripts/VNEffects/VNYourEffect.cs`
（注意是 `VNEffects/` 根目录，不是 `Script/` —— 纯演出层组件放根目录）。

**② 接进 VNStage**

在「舞台引用」区加一个栏位（参考 `eventRegistry`，
[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:111](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L111)），
并在 `AutoWire()` 里补自动查找。

**③ 接 fx 开关三件套**

如果它是「开／关」型效果，加进 `ToggleFxNames`：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNStage.cs:1342
static readonly string[] ToggleFxNames =
    { "godrays", "dof", "clouds", "haze", "shimmer", "heartbeat", "dutch",
      "speedlines", "letterbox", "meteor", "skycloud", "filmgrain", "crt",
      "kenburns" };
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:1342](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L1342)

再到 [`Fx()`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L1614)（同档:1614）的 switch 里加一个 case：

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNStage.cs:1614
public void Fx(string name, string arg, int line = 0)
{
    bool on = arg == "on" || arg == "true" || string.IsNullOrEmpty(arg);
    if (System.Array.IndexOf(ToggleFxNames, name) >= 0)
        _fxStates[name] = on && arg != "off"; // focus 等非开关型不记录   :1617
    switch (name)
    {
        case "godrays":                        // :1621
            if (godRays == null) break;
            if (on) godRays.Show(); else godRays.Hide();
            break;
        // ...
        case "youreffect":                     // ← 加这里
            if (yourEffect == null) break;
            if (on) yourEffect.Show(); else yourEffect.Hide();
            break;
        default:
            Debug.LogWarning($"[VNScript] 第 {line} 行：未知 fx「{name}」");
            break;
    }
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:1614](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L1614)

**进了 `ToggleFxNames` 就自动获得三件事**：`_fxStates` 记录、
`CaptureSnapshot` 存进 `data.fxOn`（同档:871-875）、
`reset effects` 一并关掉（同档:1607）。
**这就是 fx 型效果不用单独做存档兼容的原因。**

一次性效果不进 `ToggleFxNames`，注解直接写在 case 上：
`case "shockwave"` 的「一次性演出，不记录开关状态」（同档:1663）、
`speedlines burst`（同档:1659）。

两个互斥关系已经写死在 case 里：`filmgrain` 与 `crt` 互斥（同档:1667 / :1673，
且手动控制会接管 mood 自动滤镜，把 `_retroAuto` 设 false）；
`letterbox` 手动控制会把 `_letterboxAuto` 设 false（同档:1653）。

**④ Schema 加候选**

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:304
public static readonly string[] FxNames = { ... };   // ← 加你的名字
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:304](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L304)

（`Add("fx", ...)` 在同档:544）

**⑤ 重建演示场景**

`Tools → VN Effects → 演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene`
（[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:345](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L345)）。
每次加新组件后要重建，否则演示场景里没有你的组件。

**⑥ 不需要单独做存档** —— 见 ③。

### 12.3 三个容易踩的架构陷阱

**① 别试图用 URP Camera Stack 让某一层躲开后处理。**
整个 stack 共用一个 color target，后处理在最后一个相机之后统一执行一次，Overlay 相机躲不掉。
真能躲开的 `Screen Space - Overlay` 又会连 Bloom 一起躲开（对话框流光／名牌发光全废）。

想让某层躲开情绪色调，正解是把它移出 [`VNMoodGrading`](Assets/Project/Scripts/VNEffects/VNMoodGrading.cs)
（Assets/Project/Scripts/VNEffects/VNMoodGrading.cs）的目标列表 ——
色彩是**逐层写进各自材质实例**的，不是全屏后处理。

**② 过场层与教程层必须挂主 Canvas**（排序 90 / 92），不能自建 Overlay 画布 ——
Overlay 会永远压在 Screen Space - Camera 之上，也吃不到 Bloom（洞口 HDR 描边靠它发光）。

**③ 加法混合的 HDR 走 `_TintColor`**，不要指望顶点色 —— 会被钳到 1。

---

## 13. 类别 K：新章节剧本（≈「关卡」）

> 对应技能：`vn-write-scenario`。语法详解在 `HowToUse.md`。

### 13.1 一个「关卡」在本专案里是什么

一个 `.vn.txt` 文件（或文件内的一个 `label` 段）。跨文件跳转用**限定地址** `文件::标签` ——
Parser 在 `ParseCommand`（[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:317](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L317)）
专门为它做了「第一个冒号后紧跟另一个冒号」的判据，
并留了反例保护（`title:第1章::序` 仍按 key:value 处理，值里保留 `::` 原样传给 [`VNStoryAddress`](Assets/Project/Scripts/VNEffects/Script/VNStoryAddress.cs)）。

### 13.2 步骤

1. 建 `Assets/Scenarios/Chapter3.vn.txt`
2. 写内容（骨架）：

```
# Chapter3.vn.txt

label 开场
interlude 第三章
bg 教室 transition:NoiseDissolve
bgm play 日常 fade:1.5
show 亚里沙 at:center with:crossfade
亚里沙 微笑: 早安。

choice
* 打招呼 flag:好感度+1 -> 打招呼
* 装作没看见 -> 无视

label 打招呼
: 你朝她挥了挥手。
jump 汇合

label 无视
: 你别过头去。
jump 汇合

label 汇合
event quiz id:社团常识 count:3 time:15 pass:2
* 全对 -> 满分
* 及格 -> 及格
* 失败 -> 补考

label 满分
...
jump Chapter4::开场
```

台词行三种写法（`ParseSay`，[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:340](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L340)）：
`角色 表情: 内容`（表情可省）／`旁白: 内容`（说话者不是注册角色时只显示名字）／
`: 内容`（冒号开头 = 无名牌旁白）／无冒号整行 = 无名牌旁白。

3. **Rescan** → 自动进 `config.chapters`
   （`ScanChapters`，[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:233](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L233)，
   只收 `Assets/Scenarios` 下 `.vn.txt` 结尾的）
   —— **`jump 文件::标签` / `chapter` 的目标必须登记在 `chapters` 里**
   （注解在 [Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:88](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L88)）
4. `Ctrl+Shift+L` Lint
5. 用剧本编辑器（`Tools → VN Effects → 剧本编辑器 Scenario Editor`）调，`F5` 从选中行播放

### 13.3 Lint 会替你抓的事

这些都是「不 Lint 就静默出错」的（行号都在
Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs）：

| 规则 | 函式 | 行号 |
|---|---|---|
| 重复 label / 未定义 label | `CheckLabels` | :472 |
| `params` 契约 | `CheckParams` | :485 |
| 素材 id 未登记（bg/cg/bgm/se/voice/角色/表情） | `CheckAssets` | :502 |
| 选项块结构 | `CheckChoices` | :862 |
| 事件模块未注册 / 结果名拼错 / 各模块专属 id 拼错 | `CheckEvents` | :907 |
| sns 子命令 | `CheckSns` | :1145 |
| 跨文件跳转目标 | `CheckJumpTargets` / `Resolve` | :1247 / :1289 |
| `call` 传参与 `params` 对不上 | `CheckCallContracts` | :1324 |
| 子程序没有 `return` | `CheckSubroutineReturns` | :1370 |
| 死循环风险 | `CheckLoopGuards` | :1502 |
| 没人跳的孤儿 label | `CheckUnreferencedLabels` | :1561 |
| 空素材库 | `CheckEmptyLibraries` | :398 |

入口是 `LintAll`（同档:137）。写了 `${flag}` 的动态 id 会被 `Dynamic`（同档:1604）跳过检查。

### 13.4 剧本编辑器的调试能力

- `F5` 从选中行播放（`VNScriptRunner.PlayFromSourceLine`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:212`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L212)），
  [Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:245](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L245)）——
  默认勾「重建前置状态」，会走 `RebuildStateBefore`（同档:263 调用）
- `F6` 重播上次那行 / `F8` 暂停（`SetDebugPaused`，同档:232）/ `F10` 单步
  （`RequestDebugStep`，同档:239）/ `Ctrl+S` 保存
- **Play Mode 中播放按钮不禁用** —— 直接用内存文本原地重跑，
  不退出 Play Mode、不触发域重载；播放前静默自动保存
- 命令级暂停不冻结画面动画（`IsDebugPaused`，同档:230）

### 13.5 本地化

剧本只写中文，翻译走旁路表 —— 见 §17。

---

## 14. 类别 L：新教程

### 14.1 结构

[`VNTutorialDef`](Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs:103](Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs#L103)）
+ 一组 `VNTutorialStep`（同档:27），三语文案。`id` 在同档:106。

播放三种触发：剧本 `tutorial <id> [force:on]`；
事件行写 `event <模块> tutorial:<id>`；
模块模板的 `tutorialId` 栏位（`VNEventModule.tutorialId`（[`Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:56`](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L56)），
[Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:56](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L56)）首次自动播。
三者的优先级在 `Launch` 里（同档:71）：剧本行的 `tutorial:` 覆盖模板的 `tutorialId`。

### 14.2 高亮目标：必须用注册表，绝不能按名字找

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:30
public static void Register(string id, RectTransform rect)
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:30](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L30)

> **绝不能按物体名或路径找** —— 小游戏 UI 全是程序化生成的，
> 改一次布局，路径寻址就静默挖到空气上，**没有任何报错**。

所以要高亮你模块里的某块 UI，在 `BuildUi` 里加一句：

```csharp
VNTutorialAnchors.Register("quiz.timer", _timerFill);
// 销毁时
VNTutorialAnchors.Unregister("quiz.timer", _timerFill);   // VNTutorialAnchors.cs:40
```

> 📎 参考：[VNTutorialAnchors.cs:40](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L40)

取用走 `Get`（同档:48），列举走 `Ids`（同档:57，编辑器下拉用）。
另有一个挂在物件上的便捷组件 [`VNTutorialAnchor`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs)（同档:82）。

洞的位置每帧从目标的**世界四角**换算，**不抄 `anchoredPosition`** ——
ZoomRoot / TiltRoot 的运镜会让它对不上。

### 14.3 步骤

1. `Create → VN → Tutorial Def`（[Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs:102](Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs#L102)），
   存 `Assets/VNEffects/Tutorials/`
2. 填 `id`（同档:106）+ 各步骤的三语文案 + 高亮锚点 id
3. 目标 UI 那边 `VNTutorialAnchors.Register`（[`Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:30`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L30)）
4. Rescan → 进 `config.tutorials`
   （[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:172](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L172)）
5. 剧本 `tutorial <id>`，或模块模板 `tutorialId`，或剧本行 `event xxx tutorial:<id>`
6. Schema 已有：`tutorial` 命令在
   [Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:421](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L421)，
   `event` 的通用 `tutorial:` 参数在同档:664（[`VNParamSource.TutorialId`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs)）
7. 样例：`Tools → VN Effects → 教程 Tutorials → 导出羽毛球示例教程 Export Badminton Sample`
   （[`VNTutorialSamples`](Assets/Project/Scripts/VNEffects/Editor/VNTutorialSamples.cs)，Assets/Project/Scripts/VNEffects/Editor/VNTutorialSamples.cs）

### 14.4 三个不能漏的时序细节

- **暂停走 [`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) 不是 `timeScale`**（理由见 §6.2）。
  教程必须在 `OnLaunch` **之后**播，注解在
  [Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs:68](Assets/Project/Scripts/VNEffects/Script/VNEventModule.cs#L68)-70：
  「要高亮记分板，记分板得先存在」，且「这一句之后模块虽然开着但一帧都不会跑」。
- **淡出之后才解除暂停** —— 推进那一下点击 / ESC 的 `wasPressedThisFrame` 必须已复位，
  否则同一帧被模块再吃一次。ESC 尤其要紧（羽毛球拿它当认输）。
- **进教程强制显示系统光标，退出还原原值** —— 互动模块把光标藏了，
  不抢回来玩家点不了「下一步」。

覆盖层排序 **92**（事件层 60 与全屏转场 100 之间），挂**主 Canvas** 而非自建 Overlay 画布。

### 14.5 「看过了」不进存档

走 [`VNTutorialSeen`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs)（Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs）全局 JSON，
语义同 CG 解锁：读旧档、开新周目都不该重看。ESC 跳过也算看过。
设置面板有「显示教程提示」开关可关可重置。

### 14.6 卡片皮肤

[`VNTutorialSkin`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSkin.cs)（Assets/Project/Scripts/VNEffects/Script/VNTutorialSkin.cs）
只有 `panelRoot` + `bodyText` 必需，缺失退回程序化卡片。
**暗幕不走皮肤，它是功能件。**

---

## 15. 类别 M：新 AI 人格

### 15.1 为什么人格独立于角色

[`VNAiPersonaDef`](Assets/Project/Scripts/VNEffects/Script/VNAiPersonaDef.cs)（[Assets/Project/Scripts/VNEffects/Script/VNAiPersonaDef.cs:26](Assets/Project/Scripts/VNEffects/Script/VNAiPersonaDef.cs#L26)）
与 [`VNCharacterDef`](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs) 是分开的两个资产 ——
**一个角色要能有多套人格共用同一套立绘**（学妹模式 / 前辈模式 / 醉酒模式）。

### 15.2 步骤

1. `Create → VN → AI Persona`（同档:25），存 `Assets/Art/VNEffects/AiPersonas/`
2. 填 `id`（同档:29）+ 绑定的角色 id + 身份 / 说话方式 / 关系 / 边界 等提示词段落
   + 表情／漫符白名单（留空 = 取角色资产全部）
3. 供应商选「跟随全局」即跟着
   [`VNGameConfig.aiProvider`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs)（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:222](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L222)）换；
   模型名走 `aiModel`（同档:226），留空 = 该供应商默认
4. Rescan → 进 `config.aiPersonas`（同档:218）；运行时查找走 `FindAiPersona`（同档:233）
5. **不进 Play Mode 调参**：`Tools → VN Effects → AI → AI 试聊台 AI Talk Studio`
   （[`VNAiStudioWindow`](Assets/Project/Scripts/VNEffects/Editor/VNAiStudioWindow.cs)，Assets/Project/Scripts/VNEffects/Editor/VNAiStudioWindow.cs；
   人格资产右键也能开）。三栏 = 左改参数 / 中聊天流 / 右 system prompt 实时预览
   （**不发请求不花钱**，改一个字立刻重拼 —— 调 boundaries、speechStyle 的主力）
6. 剧本：

```
show 亚里沙 at:center
event aitalk vs:亚里沙 persona:学妹模式 turns:6 topic:社团 place:天台
* 好感提升 -> 顺利
* 普通
* 冷场
* 失败 -> 网络异常
```

### 15.3 三个必须知道的约束

- **`* 失败` 必须接住**，否则玩家断网／没配 key／被内容安全拦下时会静默跳过。
  Lint 有专门规则 `aitalk-no-failure-branch`
  （[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:1106](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L1106)），
  提示原文：「没接住就会静默按顺序继续，剧情会像什么都没发生一样往下走」（同档:1110）。
  合法结果名共四个（`BuiltinOutcomes["aitalk"]`，同档:78）。
- **`event` 前要先 `show` 角色** —— 模块只换表情，不负责出场
- **定位是番外／自由时间，主线不依赖** —— AI 内容不进翻译表、无配音、玩家可能断网。
  key 仅本地开发用，发行须改玩家自填或自建中转

### 15.4 提示词顺序（改人格文案时的规则）

身份 → 说话方式 → 关系 → 此刻情况 → 输出规则 →（没有硬 schema 的供应商在这里插一段
「输出格式」示例 JSON）→ 边界。**越靠后权重越高。**
组装逻辑在 [`VNAiConversation`](Assets/Project/Scripts/VNEffects/Script/VNAiConversation.cs)（Assets/Project/Scripts/VNEffects/Script/VNAiConversation.cs，
纯逻辑层，无 MonoBehaviour，可单测）。

**永远不信任模型输出**：好感强制 Clamp（Gemini schema 不支持 minimum/maximum，
不钳实测会给 +5）、表情越界降级、选项不足补齐。繁体字有确定性代码兜底
（[`VNAiTextNormalize`](Assets/Project/Scripts/VNEffects/Script/VNAiTextNormalize.cs)，Assets/Project/Scripts/VNEffects/Script/VNAiTextNormalize.cs）——
提示词管「大部分时候对」，兜底管「永远不出错」。

### 15.5 换供应商 / 换模型时

- 供应商能力差异（谁支持硬 schema、安全阈值）、默认模型、key 名全登记在 [`VNAiProviders`](Assets/Project/Scripts/VNEffects/Script/VNAiProvider.cs)
  （Assets/Project/Scripts/VNEffects/Script/VNAiProvider.cs）
- **全项目唯一碰 HTTP 的文件是 [`VNAiClient`](Assets/Project/Scripts/VNEffects/Script/VNAiClient.cs)**
  （Assets/Project/Scripts/VNEffects/Script/VNAiClient.cs）；
  各家只差「拼请求体」和「解响应」，拆在 [`VNAiClientGemini`](Assets/Project/Scripts/VNEffects/Script/VNAiClientGemini.cs) / [`VNAiClientDeepSeek`](Assets/Project/Scripts/VNEffects/Script/VNAiClientDeepSeek.cs)（纯静态）
- **换模型必须同步单价表** [`VNAiPricingDef`](Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs)
  （[Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs:56](Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs#L56)），
  填进 `VNGameConfig.aiPricing`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:230](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L230)）
  —— 注解直说：「换了模型却不改这里的话，日志里的成本数字会静默偏低」（同档:229）
- `VNGameConfig.ClearCache()`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:69`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L69)）（同档:69）会一并 `VNAiProviders.Invalidate()`（[`Assets/Project/Scripts/VNEffects/Script/VNAiProvider.cs:50`](Assets/Project/Scripts/VNEffects/Script/VNAiProvider.cs#L50)）
  与 `VNAiPricing.Invalidate()`（[`Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs:118`](Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs#L118)）（同档:76-77）—— 这就是为什么改配置后必须清缓存

---

## 16. 类别 N：新天气／新过场章节卡

这两类都是「建资产 → Rescan → 剧本用」的最短路径，没有程式码改动。

### 16.1 新天气

1. `Create → VN → Weather Def`（`CreateAssetMenu`，
   [Assets/Project/Scripts/VNEffects/VNWeatherDef.cs:21](Assets/Project/Scripts/VNEffects/VNWeatherDef.cs#L21)）
2. 填 `id`（同档:48）—— **可中文，会覆盖同名内置预设**
3. Rescan → 进 `config.weatherDefs`
   （[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:160](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L160)）
4. 调参：`Tools → VN Effects → 预览 Preview → 天气预览 Weather Preview`
   （[`VNWeatherPreviewWindow`](Assets/Project/Scripts/VNEffects/Editor/VNWeatherPreviewWindow.cs)，Assets/Project/Scripts/VNEffects/Editor/VNWeatherPreviewWindow.cs）
5. 剧本：`weather <id> [density:] [wind:] [speed:] [size:]`

`weather` 的 id 是**三级解析**（`VNWeatherController.SetWeatherId`（[`Assets/Project/Scripts/VNEffects/VNWeatherController.cs:72`](Assets/Project/Scripts/VNEffects/VNWeatherController.cs#L72)），
Assets/Project/Scripts/VNEffects/VNWeatherController.cs）：
自定义资产 → 内置叶型别名（含中文）→ [`VNWeather`](Assets/Project/Scripts/VNEffects/VNWeatherController.cs) 枚举。
留空 `weatherDefs` 也能用 —— 内置五套预设走 `petals` / `maple` / `ginkgo` / `leaves` / `bamboo`
（注解在 [Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:157](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L157)-159）。

Schema 用 [`VNParamSource.WeatherId`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs)
（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:7](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L7) 的枚举；
命令定义在同档:382），候选 = 内置叶型 + 资产 + 雨雪萤火虫枚举，自动出现。
天气状态进存档（`VNSaveData.weather`（[`Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:36`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L36)） 与四个覆盖参数，
[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:36](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L36)-44），
`CaptureSnapshot` 已经处理（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:843](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L843)-848）。

### 16.2 新过场章节卡

1. `Create → VN → Interlude Def`（[Assets/Project/Scripts/VNEffects/Script/VNInterludeDef.cs:37](Assets/Project/Scripts/VNEffects/Script/VNInterludeDef.cs#L37)），
   存 `Assets/Art/VNEffects/Interlude/`
2. 填 `id`（同档:41）+ 三语标题 + 语音池 + 图池 + 进出方式
3. **图池留空 = 从 `VNGameConfig.interludeImages`（[`Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:168`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L168)） 全局池随机抽**
   （[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:168](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L168)）
4. Rescan → 进 `config.interludes`（同档:164）
5. 剧本：`interlude <id> [time:秒]`
   （Schema 在 [Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:413](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L413)）

三个设计点：

- **过场图不进 CG 画廊**（注解在 [Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:167](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L167)）
  —— 它们是演出素材不是收集品
- 过场层排序 **90**（事件层 60 之上、全屏转场 100 之下），
  也因此**必须挂主 Canvas 下**
- **不进存档，但 [`ClearStage()`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L983) 必须 [`HideImmediate()`](Assets/Project/Scripts/VNEffects/Script/VNInterludeScreen.cs#L136)**

---

## 17. 横切 O：本地化

> 对应技能：`vn-localize`。

### 17.1 三条独立的翻译通道

| 内容 | 通道 | 出处 |
|---|---|---|
| UI 字符串（按钮、提示、系统文案） | `VNLocale.T("key")` 查表，表在 `Resources/VNLocale/ui.<code>.txt` | `VNLocale.T`（[`Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:76`](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L76)），[Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:76](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L76) |
| 剧本台词与选项 | 旁路表，按剧本文件独立成表 | `VNScriptLocale.TableFolder`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs:25`](Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs#L25)），[Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs:25](Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs#L25) |
| 资产上的显示名 | 资产自己的 `xxxEn` / `xxxJa` 栏位 | 见 §2.2 |

### 17.2 加新内容时的规则

**① 玩家可见的 UI 字符串一律 `VNLocale.T(key)`，禁止硬编码。**
表文件格式：每行 `key = value`，`#` 开头注释，value 支持 `\n` 转义
（注解在 [Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:22](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L22)）。
查表回退链：当前语言 → 中文（源语言）→ key 本身，保证任何情况下不显示空白（同档:23）。

**② 剧本只写中文。** 译文抽取走
`Tools → VN Effects → 本地化 Localization → 抽取剧本译文 Extract Script Translations`
（`VNLocalizationTools.ExtractAll`（[`Assets/Project/Scripts/VNEffects/Editor/VNLocalizationTools.cs:36`](Assets/Project/Scripts/VNEffects/Editor/VNLocalizationTools.cs#L36)），
[Assets/Project/Scripts/VNEffects/Editor/VNLocalizationTools.cs:36](Assets/Project/Scripts/VNEffects/Editor/VNLocalizationTools.cs#L36)），
校验走 `校验剧本译文 Validate Script Translations`（`ValidateAll`，同档:66）。

译文 key 由原文算出（`VNScriptLocale.NextKey`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs:106`](Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs#L106)），
[Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs:106](Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs#L106)；
`Hash`，同档:115），所以**改了中文原文，译文就对不上了** —— 改完要重跑 Extract。

套用发生在 `VNScriptLocale.Apply`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs:32`](Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs#L32)）（同档:32），把译文写进
[`VNScriptCommand.localizedText`](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs)（[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:55](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L55)）
与 `VNChoiceOption.localizedText`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:18`](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L18)）（同档:18）。
注意注解写明：**event 结果行是逻辑标识符（结果匹配 / 去过_xx flag），不参与翻译**（同档:17）。

**③ 新资产照抄三段式回退**（§2.2）。

**④ 加新语言**：[`VNLanguage`](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs) 枚举
（[Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:8](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L8)）——
注解明确写着「枚举顺序即 PlayerPrefs 存储值，**勿改动已有项顺序**」，只能往后加。
还要同步 `Codes` 数组（同档:29）与 `DisplayName`（同档:65，「永远以该语言本身书写」）。

### 17.3 字体

文字全用 TextMeshPro + [`VNFont.Asset`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs)
（Assets/Project/Scripts/VNEffects/Script/VNFont.cs）统一入口，**禁止 legacy Text**。
编辑期存场景的 TMP 文字必须用 `VNFontAssetBuilder.EnsureFontAsset()`（[`Assets/Project/Scripts/VNEffects/Editor/VNFontAssetBuilder.cs:80`](Assets/Project/Scripts/VNEffects/Editor/VNFontAssetBuilder.cs#L80)）
（Assets/Project/Scripts/VNEffects/Editor/VNFontAssetBuilder.cs）持久化资产。

语言切换时**正文与装饰字体分开替换**（装饰字体 = `VNFont.DisplayAsset`（[`Assets/Project/Scripts/VNEffects/Script/VNFont.cs:215`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L215)），名牌等大字专用）。
换 font 会丢材质实例，所以有 `DisplayFontChanged` 事件通知重新上样式。

`VNLocale.Language` 的 setter（同档:39）里，
`VNFont.HandleLanguageChanged()`（[`Assets/Project/Scripts/VNEffects/Script/VNFont.cs:277`](Assets/Project/Scripts/VNEffects/Script/VNFont.cs#L277)）（同档:53）在 `LanguageChanged?.Invoke()`（同档:54）
**之前**调 —— 注解：「先换字体，订阅者重建文案时才能拿到正确字体」。

---

## 18. 横切 P：存档兼容与调试重建

> 对应技能：`vn-save-compat`。

### 18.1 判据：什么该进存档

| 类型 | 进存档？ | 例子 |
|---|---|---|
| 会一直显示到下一条命令改它的舞台状态 | 是 | 背景、CG、天气、mood、bgm、在场角色、UI 皮肤、`bgscroll`、`keep` 漫符、情绪叠加层 |
| 会自己消失的一次性演出 | 否 | `imprint` 痕迹、`emote` 动作、非 keep 漫符、`shockwave` |
| 镜头状态 | 否 | 调试重建走 SnapReset |
| 过场卡 / 教程 | 否 | 过场是瞬时的；教程「看过了」是全局 JSON |
| 全局收集品 | 否（独立 JSON） | CG 解锁（[`VNCgUnlocks`](Assets/Project/Scripts/VNEffects/Script/VNCgUnlocks.cs)）、大头贴相册（[`VNPhotoAlbum`](Assets/Project/Scripts/VNEffects/Script/VNPhotoAlbum.cs)）、AI 日记（[`VNAiDiary`](Assets/Project/Scripts/VNEffects/Script/VNAiDiary.cs)）、教程已读（[`VNTutorialSeen`](Assets/Project/Scripts/VNEffects/Script/VNTutorialSeen.cs)） |
| 剧情状态 | 是 | 全部 flag、AI 跨场记忆（[`VNAiMemory`](Assets/Project/Scripts/VNEffects/Script/VNAiMemory.cs)，`CaptureSnapshot` 在 [Assets/Project/Scripts/VNEffects/Script/VNStage.cs:879](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L879) 被调） |

> **AI 记忆与日记的存储语义刻意相反**：记忆是剧情状态必须跟着存档回退
> （读旧档她不该记得未来）；日记是玩家收藏品不该因读档消失（同 CG 画廊）。

### 18.2 三处同步

**① [`VNSaveData`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs) 加栏位**（`VNSaveData`，
[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:9](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L9)）

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:9
public class VNSaveData
{
    public int saveVersion = 3;       // :11  0/缺省 = call 栈加入前；2 = 无参数 call 栈
    public int commandIndex;          // :12  恢复点（正在显示的那句台词的命令索引）
    public string chapter;            // :13
    // ...
    public string backgroundId;       // :33
    public string weather;            // :36
    public string mood;               // :50
    public string bgm;                // :51
    public float bgmVol = 1f;         // :52  旧存档缺省 = 1
    public List<string> fxOn = new List<string>();  // :54
    public string cgId;               // :55  空 = 无，旧存档缺省兼容
    public string dialogueSkin;       // :58
    public string choiceSkin;         // :59
    public string nameplateStyle;     // :60  空 = 出厂样式，旧存档兼容
    public string yourNewField;       // ← 加这里
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs:9](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs#L9)

**旧存档兼容靠「缺省值就是关闭态」**：JSON 里没有这个字段时反序列化成 `null` / `0` / `false`，
所以**新字段的默认值必须等于「这个功能没开」**。
看既有注解就知道这是有意为之（同档:52 / :55 / :58 / :60 都写了「旧存档兼容」）。

嵌套结构照 `LiquidSave`（同档:81）与 `CharSave`（同档:106）的写法。
需要真正的格式迁移时才动 `saveVersion`（同档:11）。

**② `VNStage.CaptureSnapshot`（[`Assets/Project/Scripts/VNEffects/Script/VNStage.cs:840`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L840)） / `RestoreSnapshot`**

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNStage.cs:840
public void CaptureSnapshot(VNSaveData data)
{
    data.backgroundId = CurrentBackgroundId;
    // ...
    data.fxOn.Clear();
    foreach (var kv in _fxStates)
        if (kv.Value) data.fxOn.Add(kv.Key);       // :872-873
    if (sns != null) sns.CaptureSnapshot(data);    // :878
    VNAiMemory.CaptureSnapshot(data);              // :879
    // 在场角色 :882-895
}

// Assets/Project/Scripts/VNEffects/Script/VNStage.cs:902
public void RestoreSnapshot(VNSaveData data, bool instant)
{
    ClearStage();
    // UI 皮肤最先恢复：随后重放的台词/选项直接落在正确皮肤上   :904-905
    if (data.dialogueSkin != CurrentDialogueSkinId)               // :906
        SetUiSkin("dialogue", string.IsNullOrEmpty(data.dialogueSkin)
            ? "default" : data.dialogueSkin);
    // ...
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:840](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L840) · [Assets/Project/Scripts/VNEffects/Script/VNStage.cs:902](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L902)

`instant` 参数（同档:902）区分「读档（可以有过渡）」与
「从选中行播放的静默状态重建（必须瞬间）」；单参版本 `RestoreSnapshot(data)`（同档:899）
就是 `instant = false`。

**③ [`VNScriptRunner.RebuildStateBefore`](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs)**
（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:326](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L326)）

这是「从选中行播放」的核心：**从头把命令重放一遍，但只重放状态、不演出**。

```csharp
// Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:326
void RebuildStateBefore(int exclusiveIndex)
{
    var snapshot = new VNSaveData
    {
        weather = VNWeather.None.ToString(),
        mood = VNMood.Neutral.ToString(),
        scrollSpeed = VNBackgroundScroll.DefaultSpeed,
        scrollDir = VNBackgroundScroll.DefaultDirection,
        scrollMode = VNScrollMode.Mirror.ToString(),
    };                                                            // :334-341
    // Ken Burns 默认开启：先种入再按剧本重放，重建结果才与真实运行一致
    snapshot.fxOn.Add("kenburns");                                // :344

    // ... 逐条 switch (cmd.keyword) 更新 snapshot（:357 起）
    //     case "bg": :359   case "cg": :362   case "show": :528  case "fx": :563
    //     case "flag": :581 / "quest": :584 / "stat": :588 / "time": :591  ← silent 重放
    //     case "choice"/"jump"/"call"/"return"/"if"/"event": :608-613 ← hasBranching，不重放

    stage.RestoreSnapshot(snapshot, true);                        // :622
    RestoreUiHidden(snapshot);                                    // :623
}
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:326](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L326)

**加新命令时要在这个 switch 里加一条**，否则「从选中行播放」画面不对。三个要点：

- **默认值要显式种进 `snapshot`**（同档:334-344）—— 否则重建结果与真实运行不一致
- **会弹演出的写入用 silent 模式**（同档:581-593）
- **分支类命令标 `hasBranching`**（同档:608-614）—— 结果无法推断，不重放。
  `event` 的注解直接写「事件结果无法推断，不重放」（同档:613）

### 18.3 验证

1. 存档（F5）→ 改状态 → 读档（F9）→ 状态回来了
2. 用**旧存档**（加栏位之前存的）读档，不报错且功能是关闭态
3. 剧本编辑器选一行 → `F5` 从选中行播放（默认勾「重建前置状态」）→ 画面与顺跑到该行一致

---

## 19. 横切 Q：剧本编辑器与 Lint

> 对应技能：`vn-editor-extend`（编辑器）、`vn-debug`（排错）。

### 19.1 编辑器的数据模型

**文本是唯一真相**：`.vn.txt ↔ VNScenarioDoc.rows`（[`VNScenarioDoc`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioDoc.cs)，
[Assets/Project/Scripts/VNEffects/Editor/VNScenarioDoc.cs:95](Assets/Project/Scripts/VNEffects/Editor/VNScenarioDoc.cs#L95)）。
`Parse`（同档:107）读进来，`GenerateText`（同档:347）写回去，注释／空行保留。

一行 = 一个 `VNRow`（同档:23）。认不出的 token 塞进 `extraTokens`（同档:45）**原样保留**
（写回时在同档:449 原样吐出来），camseq 的 `>` 行塞进 `camLines`（同档:49）原样保留。
**这就是「Schema 没登记也不会丢内容」的机制** —— 只是会有 unrecognized 警告。
选项行是 `VNChoiceOptionRow`（同档:9）。

### 19.2 加新命令后编辑器会自动获得什么

登记进 [`VNScenarioSchema`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs) 之后，**不用改窗口程式码**就自动有：

- 行首命令按钮的分类菜单（`VNCommandDef.category`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:61`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L61)），
  [Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:61](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L61)）与右键打字搜索
- 每个参数一个控件，类型由 `VNParamSource` 决定
- 底部 `+` 加行下拉、`Ctrl+E` 命令面板（[`VNCommandSearch`](Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs)，
  Assets/Project/Scripts/VNEffects/Editor/VNCommandSearch.cs；候选表从 Schema 现场生成）
- tooltip（`VNCommandDef.hint`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs:62`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioSchema.cs#L62)），同档:62）

匹配只做子串包含（中英皆可，不做模糊／拼音）。

### 19.3 要小心的几处

- **行号换算**：`SourceLineForRow`
  （[Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs:1560](Assets/Project/Scripts/VNEffects/Editor/VNScenarioEditorWindow.cs#L1560)）
  把 UI 行号换成物理行号。`blockChoice` 的命令会占多行（`*` 行顺次下移），
  `event` 更是画成两行（通用参数一行 + 模块专属一行，
  `DrawParams` 分段调用，同档:1876 与 :1878）
- **路径点行禁用 `CharacterPopup` / `SpritePopup`** —— 那套是异步回调、会把值写进
  `VNRow.values`（[`Assets/Project/Scripts/VNEffects/Editor/VNScenarioDoc.cs:44`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioDoc.cs#L44)），和 `camLines` 是两条路径，必须用同步的 `PopupString` / `EditorGUI.Popup`
- **窗口状态跨域重载存活走 `ISerializationCallbackReceiver`** ——
  加新窗口状态必须**同时改 `OnBeforeSerialize` 和 `OnEnable`**
- **候选必须缓存** —— `OptionsFor`（同档:2464）每帧都被调，里面扫资产会卡
- **素材候选按覆盖语义取数**：[`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) 里填了就用它的，留空才回退场景组件
  （`RefreshSources()` 里的 `PickLibrary`）—— 以前只读场景组件，导致在配置资产里
  新登记的素材在下拉里根本搜不到

### 19.4 Lint 的严重度与命名

[`VNLintIssue`](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:10](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L10)）
带一个 `code` 字串（kebab-case，如 `unknown-event-module` / `bad-event-outcome` /
`photo-missing-vs` / `wipefog-cg-before-event`）。`Add`（同档:1642）是统一入口。

**严重度的判据**：

- `Error` —— 必然坏掉。例：`photo-missing-vs`（同档:955 附近）：
  「没有合影对象，模块会直接返回『完成』，整段拍照白跑」
- `Warning` —— 静默降级。例：`unknown-badminton`（同档:944 附近）：
  「拼错只会静默退回兜底难度」

**每条 issue 都要写「怎么修」，而且要把当前可用的候选列出来**：

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:1128
Add(issues, VNLintSeverity.Warning, "bad-event-outcome", f, o.line,
    $"模块「{module}」不会返回结果「{outcome}」",
    $"该模块的结果名：{string.Join(" / ", valid.OrderBy(s => s))}。" +
    "结果名匹配不上时会静默跳过该行、按顺序继续执行。");
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs:1128](Assets/Project/Scripts/VNEffects/Editor/VNScenarioLinter.cs#L1128)

> **`BuiltinOutcomes` 表里有一条自带的警告**（同档:80-81）：
> wipefog 的三档结果名可以在资产里改，表里只列默认值 ——
> **改过名的资产会让拼写校验误报，改名时记得同步这一行**。
> 你新加的模块如果也允许在资产里改结果名，会有同样的问题。

---

## 20. 验证与排错清单

不管你走的是哪一类流程，收工前跑一遍这张表。

| # | 检查 | 怎么做 |
|---|---|---|
| 1 | 编译过 | Unity 自动编译 |
| 2 | Console 无新增 Warning | 特别注意 `[VNScript] 未知命令`、`[VNEvent] 事件模块库里没有` |
| 3 | Lint 全过 | `Ctrl+Shift+L`（`Tools → VN Effects → 剧本检查 Lint Scenarios`） |
| 4 | 编辑器画得出参数格 | 剧本编辑器里看那一行，不该是灰色 raw 文本 |
| 5 | 从选中行播放正确 | `F5`（勾「重建前置状态」） |
| 6 | 重建场景后仍在 | 再跑一次「重建剧本演示场景」—— 这一步专抓「只写进场景没写进 VNGameConfig」。**注意：跑完别再跑 Rescan**（CG 库地雷，见 §4.1） |
| 7 | 存档 / 读档 | F5 / F9 |
| 8 | 旧存档能读 | 用改动之前存的槽 |
| 9 | 三语切换不崩 | 设置面板切中/英/日，看有没有硬编码中文漏网 |
| 10 | 事件模块：四条退出路径 | 正常结束 / ESC / `CancelForDebug` / 换场景 —— 光标、[`VNPause`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs) 句柄、挂在立绘下的物件都要还原 |

### 常见症状 → 原因速查

| 症状 | 最可能的原因 |
|---|---|
| 命令行被当台词念出来 | 忘了加进 `Keywords`（[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:93](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L93)） |
| 什么都没发生，也没报错 | id 拼错（走 Lint）；或事件结果名对不上（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2886](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2886) 只有一条 Warning） |
| 参数值被切成 key:value | 值里含冒号，`ParseCommand`（[Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs:303](Assets/Project/Scripts/VNEffects/Script/VNScriptParser.cs#L303)）需要加豁免 |
| 重建场景后功能消失 | 没登记进 [`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) |
| Inspector 改了没反应 | 没 `VNGameConfig.ClearCache()`（[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:69](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L69)） |
| 读档后状态丢了 | [`VNSaveData`](Assets/Project/Scripts/VNEffects/Script/VNSaveSystem.cs) / `CaptureSnapshot` / `RestoreSnapshot` 三处没同步 |
| 从选中行播放画面不对 | `RebuildStateBefore`（[Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:326](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L326)）的 switch 少一条 |
| 重建状态时弹出一堆 Toast | 重放时没传 `silent = true`（参考同档:589） |
| 立绘颜色一说话就跳回去 | 直接写了 `_Brightness` / `_Saturation`，没走 `SetGrade(通道, …)` |
| 推完镜头一说话立绘尺寸就跳回去 | 直接改了 `localScale`，没走两个缩放倍率通道 |
| 事件里点不到选项 | 模块自绘 UI 没设 `raycastTarget = false`（事件层 60 压在选项面板 45 上，`VNEventRegistry.LayerSortingOrder`（[`Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:27`](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L27)），[Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs:27](Assets/Project/Scripts/VNEffects/Script/VNEventRegistry.cs#L27)） |
| 剧本卡在事件里不动 | 模块某条路径没调 `Done`（Runner 在 [Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs:2861](Assets/Project/Scripts/VNEffects/Script/VNScriptRunner.cs#L2861) 无限等） |
| 模块运行时抛 InvalidCastException | 模板物体没带 `RectTransform`（[Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs:63](Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs#L63)-65） |
| 游戏永久卡死 | `VNPause` 句柄没释放（五条路径漏了一条，`VNPause.Release`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:85`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L85)），[Assets/Project/Scripts/VNEffects/Script/VNPause.cs:85](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L85)） |
| 玩家鼠标指针永久消失 | `VNTouchCursor.Dispose()`（[`Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs:217`](Assets/Project/Scripts/VNEffects/Script/VNTouchCursor.cs#L217)） 没在四处都调 |
| 教程讲解时球还在飞 | 用了 `Time.timeScale = 0`；应该是 `VNPause.IsPaused` 早退 + `VNTime.Delta`（[`Assets/Project/Scripts/VNEffects/Script/VNPause.cs:144`](Assets/Project/Scripts/VNEffects/Script/VNPause.cs#L144)） |
| 教程高亮挖到空气上 | 用了名字/路径寻址，没用 [`VNTutorialAnchors.Register`](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs)（[Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs:30](Assets/Project/Scripts/VNEffects/Script/VNTutorialAnchors.cs#L30)） |
| 列表条目文字叠印、输入框点不进去 | 内嵌类里用了 `[Header]`，应该用 `[Tooltip]`（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:24](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L24)-27） |
| 新图拖进来不能填 Sprite 栏 | 目录不在 [`VNTextureImportDefaults.Roots`](Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs)（[Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs:37](Assets/Project/Scripts/VNEffects/Editor/VNTextureImportDefaults.cs#L37)） |
| 手工维护的 CG 库突然空了 | `Assets/CG` 目录被建出来 + 跑了 Rescan（见 §4.1 与 附录 D） |
| 英/日切换后某处仍是中文 | 硬编码字串，应走 [`VNLocale.T`](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs)（[Assets/Project/Scripts/VNEffects/Script/VNLocale.cs:76](Assets/Project/Scripts/VNEffects/Script/VNLocale.cs#L76)） |
| 改了中文原文，译文全失效 | 译文 key 由原文算出（`VNScriptLocale.Hash`（[`Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs:115`](Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs#L115)），[Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs:115](Assets/Project/Scripts/VNEffects/Script/VNScriptLocale.cs#L115)），要重跑 Extract |

---

## 21. Git 流程

专案规则第 2、3 条，四个阶段，**阶段之间一律停下来等用户发话**。

| 阶段 | 触发 | 做什么 |
|---|---|---|
| ① 实现 | 提需求 | **动手前先从 `main` 切 `feature/<英文短名>`**，只写代码 + 编译验证；不 commit、不 push、不开 PR、不碰文档。做完报告改了哪些文件 |
| ② 提交 | 说「提交／推送／开 PR」 | 逐文件 `git add` → commit → `git push -u origin feature/<名>` → `gh pr create` |
| ③ 文档 | 说「更新文档」 | **同一个功能分支**上补 `WhatAiDo.md` 等 → commit → push，PR 自动带上 |
| ④ 收尾 | 用户在 GitHub 合并完并招呼 | `git checkout main` → `git pull`；**合并由用户本人做** |

小改动（纯 `.md` / typo / 单文件几行的参数调值 / 技能文件与素材登记）走「直接 main」快速通道，
但仍是两段：改完 → 报告 → 用户说「推」才 add / commit / push。

三条红线：

1. **永远不删分支** —— 用户靠分支回滚；GitHub 上合并 PR 时不要勾 Delete branch
2. **禁止 `git add -A` / `git add .`** —— 用户 Unity 工作区常年有无关的未提交改动，
   只 add 本次相关文件
3. **push 到 main 不可撤回，绝不自作主张推**

提交信息：英文标题、正文中文、尾部加 `Co-Authored-By`。

---

## 附录 A：定义资产总表

全部 `[CreateAssetMenu]` 一览，以及它在 [`VNGameConfig`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) 里对应的库栏位
（库栏位行号都在 Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs）。

| Create 菜单 | 类 | 档案:行号 | VNGameConfig 库栏位 |
|---|---|---|---|
| VN/Game Config | `VNGameConfig` | [Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:33](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L33) | （本体） |
| VN/Character Definition | [`VNCharacterDef`](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs:21](Assets/Project/Scripts/VNEffects/Script/VNCharacterDef.cs#L21) | `characters`:148 |
| VN/Stat Definition | [`VNStatDef`](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs:20](Assets/Project/Scripts/VNEffects/Script/VNStatDef.cs#L20) | `stats`:206 |
| VN/Shop Definition | [`VNShopDef`](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs:27](Assets/Project/Scripts/VNEffects/Script/VNShopDef.cs#L27) | `shops`:207 |
| VN/Plan Definition | [`VNPlanDef`](Assets/Project/Scripts/VNEffects/Script/VNPlanDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNPlanDef.cs:13](Assets/Project/Scripts/VNEffects/Script/VNPlanDef.cs#L13) | `plans`:208 |
| VN/Quest Definition | [`VNQuestDef`](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs:13](Assets/Project/Scripts/VNEffects/Script/VNQuestDef.cs#L13) | `quests`:209 |
| VN/Quiz Definition | [`VNQuizDef`](Assets/Project/Scripts/VNEffects/Script/VNQuizDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNQuizDef.cs:15](Assets/Project/Scripts/VNEffects/Script/VNQuizDef.cs#L15) | `quizzes`:210 |
| VN/Badminton Definition | [`VNBadmintonDef`](Assets/Project/Scripts/VNEffects/Script/VNBadmintonDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNBadmintonDef.cs:19](Assets/Project/Scripts/VNEffects/Script/VNBadmintonDef.cs#L19) | `badmintons`:211 |
| VN/Fog Wipe Definition | [`VNFogWipeDef`](Assets/Project/Scripts/VNEffects/Script/VNFogWipeDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNFogWipeDef.cs:34](Assets/Project/Scripts/VNEffects/Script/VNFogWipeDef.cs#L34) | `fogWipes`:214 |
| VN/Interaction Definition | [`VNInteractionDef`](Assets/Project/Scripts/VNEffects/Script/VNInteractionDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNInteractionDef.cs:259](Assets/Project/Scripts/VNEffects/Script/VNInteractionDef.cs#L259) | （模块模板持有） |
| VN/Touch Zone Definition | [`VNTouchZoneDef`](Assets/Project/Scripts/VNEffects/Script/VNTouchZoneDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNTouchZoneDef.cs:94](Assets/Project/Scripts/VNEffects/Script/VNTouchZoneDef.cs#L94) | （互动资产引用） |
| VN/Tutorial Def | [`VNTutorialDef`](Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs:102](Assets/Project/Scripts/VNEffects/Script/VNTutorialDef.cs#L102) | `tutorials`:172 |
| VN/Interlude Def | [`VNInterludeDef`](Assets/Project/Scripts/VNEffects/Script/VNInterludeDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNInterludeDef.cs:37](Assets/Project/Scripts/VNEffects/Script/VNInterludeDef.cs#L37) | `interludes`:164 |
| VN/Weather Def | [`VNWeatherDef`](Assets/Project/Scripts/VNEffects/VNWeatherDef.cs) | [Assets/Project/Scripts/VNEffects/VNWeatherDef.cs:21](Assets/Project/Scripts/VNEffects/VNWeatherDef.cs#L21) | `weatherDefs`:160 |
| VN/AI Persona | [`VNAiPersonaDef`](Assets/Project/Scripts/VNEffects/Script/VNAiPersonaDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNAiPersonaDef.cs:25](Assets/Project/Scripts/VNEffects/Script/VNAiPersonaDef.cs#L25) | `aiPersonas`:218 |
| VN/AI Pricing | [`VNAiPricingDef`](Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs) | [Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs:56](Assets/Project/Scripts/VNEffects/Script/VNAiPricing.cs#L56) | `aiPricing`:230 |
| VN/System UI Skin Set | [`VNSystemUiSkinSet`](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs) | [Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs:10](Assets/Project/Scripts/VNEffects/Script/VNSystemUiSkinSet.cs#L10) | `systemUiSkin`:131 |
| VN/Photo Frame | [`VNPhotoFrameDef`](Assets/Project/Scripts/VNEffects/Script/VNPhotoFrameDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNPhotoFrameDef.cs:38](Assets/Project/Scripts/VNEffects/Script/VNPhotoFrameDef.cs#L38) | `photoFrames`:242 |
| VN/Photo Sticker | [`VNPhotoStickerDef`](Assets/Project/Scripts/VNEffects/Script/VNPhotoStickerDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNPhotoStickerDef.cs:30](Assets/Project/Scripts/VNEffects/Script/VNPhotoStickerDef.cs#L30) | `photoStickers`:243 |
| VN/Photo Backdrop | [`VNPhotoBackdropDef`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBackdropDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNPhotoBackdropDef.cs:31](Assets/Project/Scripts/VNEffects/Script/VNPhotoBackdropDef.cs#L31) | `photoBackdrops`:244 |
| VN/Photo Theme | [`VNPhotoThemeDef`](Assets/Project/Scripts/VNEffects/Script/VNPhotoThemeDef.cs) | [Assets/Project/Scripts/VNEffects/Script/VNPhotoThemeDef.cs:48](Assets/Project/Scripts/VNEffects/Script/VNPhotoThemeDef.cs#L48) | `photoThemes`:245 |

其余不是 `CreateAssetMenu` 但也算「一类内容」的：
UI 皮肤是 prefab + `VNGameConfig.UiSkinEntry`（同档:116），
背景／CG 是 [`VNStage.BackgroundEntry`](Assets/Project/Scripts/VNEffects/Script/VNStage.cs)（[Assets/Project/Scripts/VNEffects/Script/VNStage.cs:29](Assets/Project/Scripts/VNEffects/Script/VNStage.cs#L29)）
与 `VNStage.CgEntry`（同档:39），
音频是 [`VNAudio.AudioEntry`](Assets/Project/Scripts/VNEffects/Script/VNAudio.cs)（Assets/Project/Scripts/VNEffects/Script/VNAudio.cs），
地图地点是 [`VNMapModule.Location`](Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs)
（Assets/Project/Scripts/VNEffects/Script/VNMapModule.cs，登记在 `mapLocations`，
[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:203](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L203)）。

---

## 附录 B：菜单总表

### 日常主力
| 菜单 | 用途 |
|---|---|
| `Tools → VN Effects → 剧本编辑器 Scenario Editor` | 写／调剧本（priority 1） |
| `Tools → VN Effects → 剧本检查 Lint Scenarios`（`Ctrl+Shift+L`） | 静态校验（priority 2） |
| `Tools → VN Effects → 素材浏览器 Asset Browser` | 登记素材（priority 3） |
| `Tools → VN Effects → 镜头编排 Camera Sequence Editor` | camseq 可视化（priority 4） |

### 游戏配置
| 菜单 | 出处 |
|---|---|
| `游戏配置 Game Config → 创建或选中配置资产 Create or Select` | [Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:31](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L31) |
| `游戏配置 Game Config → 从场景导入 Import From Scene` | 同档:73 |
| `游戏配置 Game Config → 重扫素材目录 Rescan Asset Folders` | 同档:177 |

### 场景装机（加新模块后跑，**不重建场景**）
`场景装机 Install To Scene → 限时问答 Quiz Module`
（[Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs:26](Assets/Project/Scripts/VNEffects/Editor/VNQuizInstaller.cs#L26)）／
`AI 自由聊天 AI Talk Module`（priority 141）／`羽毛球对战 Badminton Module`（142）／
`液体喷溅 Liquid Splash`（143）／`拍大头照 Photo Booth Module`（144）／
`擦雾 Fog Wipe`（145）／`亲密互动 Interaction Module`

### 演示场景（**会丢失手工整理的 Hierarchy**）
`演示场景 Demo Scenes → 重建特效演示场景 Create Demo Scene`
（[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:345](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L345)）／
`重建剧本演示场景 Create Script Demo Scene`（同档:450）

### UI 皮肤
`UI 皮肤 UI Skins → 导出皮肤模板（默认+样例）Export Skin Prefabs`（priority 120）／
`导出无框渐变皮肤（白·粉·黑）Export Soft Gradient Skins`（121）／
`导出无框渐变皮肤·覆盖重建`（122）／
`系统主题：导出默认模板 System UI: Export Default Prefabs`（135）／
`系统主题：导出排程·结算面板 System UI: Export Event Panel Prefabs`（136）／
`系统主题：导出设置面板 System UI: Export Config Panel Prefab`（137）／
`系统主题：校验全局主题 System UI: Validate Global Theme`

### 本地化 / 字体
`本地化 Localization → 抽取剧本译文 Extract Script Translations`
（[Assets/Project/Scripts/VNEffects/Editor/VNLocalizationTools.cs:35](Assets/Project/Scripts/VNEffects/Editor/VNLocalizationTools.cs#L35)）／
`校验剧本译文 Validate Script Translations`（同档:65）
`字体 Fonts → 生成 TMP 字体资产 Create TMP Font Asset`（priority 150）／
`重烘中文字体·换字体源 Rebake Chinese Font`（151）／
`修复字体材质引用 Repair Font Material References`（152）

### 预览 / 调参
`预览 Preview → 天气预览 Weather Preview`（priority 170）／
`角色立绘预览 Character Visual Preview`（171）／
`擦雾调参 Fog Wipe Tuning`（172）／`部位区域编辑器 Touch Zone Editor`（60）
另有资产右键 `CONTEXT/VNCharacterDef/角色立绘预览` 与 `Assets/VN Effects/角色立绘预览`。

### AI
`AI → 测试连接·Gemini`（190）／`测试连接·DeepSeek`（192）／`测试连接·当前默认供应商`（191）／
`查看 Key 状态 Show Key Status`（193）／`试聊 3 轮 Test Persona Talk`（194）／
`AI 试聊台 AI Talk Studio`（195）／`花费报表 Cost Report`（196）；
另有 `CONTEXT/VNAiPersonaDef/在 AI 试聊台里打开`。

### 其它
`教程 Tutorials → 导出羽毛球示例教程 Export Badminton Sample`（priority 150）／
`贴图 Textures → 套用 Sprite 导入设置到选中项 Apply Sprite Settings`（160）

---

## 附录 C：目录总表

```
D:\UnityAiProject\MyProject\
├── AddFeatureGuide.md          ← 本文件
├── CLAUDE.md / HowToUse.md / ProjectCodeGuide.md / WhatAiDo.md / SetUpGuide.md
├── AiTalkGuide.md / AiTalkIdeas.md / CodebaseAnaylze.md
├── .claude/skills/vn-*         流程技能（本文件的清单版）
└── Assets/
    ├── Project/Scripts/VNEffects/
    │   ├── *.cs                纯演出层组件（VNCamera / VNImageEffectController / VNWeatherDef …）
    │   ├── Script/             剧本系统 + 玩法 + 资产定义（129 档）
    │   └── Editor/             编辑器工具（43 档）
    ├── Resources/
    │   ├── VNGameConfig.asset  总配置（路径写死在 VNGameConfig.AssetPath，
    │   │                       Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:39）
    │   └── VNLocale/           ui.<code>.txt + Scenarios/ 剧本译文表
    ├── Scenarios/*.vn.txt      剧本（Rescan 只扫这个目录）
    ├── Art/
    │   ├── Images/{Background,Character,CG,UI}/   图片素材（在自动导入白名单）
    │   ├── CG/ BigPhoto/ Mark/ InteractionMiniGame/  （同上）
    │   ├── Models/             ★ 不在白名单，也绝不该加进去（会让 60+ 张法线贴图按 sRGB Sprite 导入）
    │   ├── Badminton/ Shaders/
    │   └── VNEffects/{Characters,Stats,Shops,Plans,Quests,
    │                  Interactions,AiPersonas,Interlude,
    │                  TouchZones,UISkins,SystemUISkins,Materials}/
    ├── VNEffects/{Quizzes,Badminton,FogWipes,Tutorials,Photo,UISkins,Materials}/
    ├── Assets/                 随手丢素材（在白名单）
    ├── Scenes/                 VNEffectsDemo.unity / VNScriptDemo.unity
    ├── Font/ Audio/ Development/ Documentation/ Editor/
    └── Plugins/                DOTween、Pixel Crushers Dialogue System
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs:39](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs#L39)

---

## 附录 D：写这份文件时发现的两处偏差

两处都不影响现在的运行，但值得记下来。**要不要修由你决定，我没有动任何程式码。**

### ① `CLAUDE.md` 的目录结构过时

`CLAUDE.md` 写 `Assets/Scripts/VNEffects/`，实际是 `Assets/Project/Scripts/VNEffects/`。
建议改 `CLAUDE.md`（纯文档改动，走快速通道）。

### ② `VNGameConfigTools.CgDir`（[`Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:25`](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L25)） 指向一个不存在的目录，且是一颗定时炸弹

```csharp
// Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:25
const string CgDir = "Assets/CG";
```

> 📎 参考：[Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs:25](Assets/Project/Scripts/VNEffects/Editor/VNGameConfigTools.cs#L25)

本专案的 CG 图在 `Assets/Art/Images/CG/`，`Assets/CG` 不存在。
现状安全 —— `ScanCg`（同档:245）在同档:247 早退，
[`VNGameConfig.asset`](Assets/Project/Scripts/VNEffects/Script/VNGameConfig.cs) 里手工维护的 168 条 `cgLibrary` 不受影响。

**但只要 `Assets/CG` 目录被建出来（哪怕是空的），下一次 Rescan 就会走到同档:270 的
`config.cgLibrary = list;`，把手工 CG 库整个覆盖成空列表**（只保留同名条目的 `group`，同档:262）。

而 [`VNEffectsDemoSetup`](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs) 里有一句
`EnsureFolder("Assets/CG")`（[Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs:1299](Assets/Project/Scripts/VNEffects/Editor/VNEffectsDemoSetup.cs#L1299)）
会**主动建这个目录**。也就是说：
**跑一次「重建剧本演示场景」，再跑一次「重扫素材目录」，CG 库就没了。**

修法三选一（需要你拍板）：

1. 把 `CgDir` 改成实际路径 `Assets/Art/Images/CG`（最小改动，但会把现有中文 id
   `CG_初次并肩` 之类全部改成文件名 `1` / `2` / `4`，等于毁掉现有 id）
2. **让 `ScanCg` 走「合并而非覆盖」** —— 只补新图、不动已有条目（推荐）
3. 干脆把 CG 与背景／音频一样列为「手工维护，不扫」，删掉 `ScanCg`
   并在报告里加一行「CG 库：保持手工维护」

在决定之前的临时规避：**跑过「重建剧本演示场景」之后不要跑 Rescan**，
或先把空的 `Assets/CG` 删掉再跑。

---

*最后更新：2026-09-02*
