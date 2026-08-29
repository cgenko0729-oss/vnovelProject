# 参考项目借鉴清单（ReferenceProjectLearnings）

> **用途**：从五个同类型（养成 / 视觉小说 / ADV）商业项目的反编译分析中，提炼出可以搬进本项目的机制、设计与工程纪律。
> **做功能前先查这里**，看有没有现成的成熟范式可抄。
> **产出日期**：2026-08-11
> **分析来源**：`D:\_A QuickStart\Unity & Game\Unity Source Code Sample\` 下各项目的 `*_CodebaseAnaylze.md` / `*_AddFeatureGuide.md`
> **编号说明**：§N 是对话中的原始编号，保留以便回溯；本文按主题重组。

---

## 目录

- [零、参考项目定位与总结论](#零参考项目定位与总结论)
- [一、优先级表（做事顺序看这个）](#一优先级表做事顺序看这个)
- [二、架构地基](#二架构地基)
- [三、数值与养成骨架](#三数值与养成骨架)
- [四、剧本与事件系统](#四剧本与事件系统)
- [五、玩法模块](#五玩法模块)
- [六、UI 表现 / 交互 / 动画](#六ui-表现--交互--动画)
- [七、Shader 与视觉](#七shader-与视觉)
- [八、关系系统与长线元游戏](#八关系系统与长线元游戏)
- [九、工程纪律](#九工程纪律)
- [十、明确不建议抄的](#十明确不建议抄的)
- [十一、原文位置索引](#十一原文位置索引)

---

## 零、参考项目定位与总结论

| 项目 | 类型 | 本质 | 最值得偷的 |
|---|---|---|---|
| **Magical Princess** | 养女育成 SLG（日） | 字符串 DSL 驱动的育成机器 | 参数 DSL + 演出队列 + 事件抽选引擎 |
| **活侠传**（Path of Wuxia） | 武侠养成 + 双战斗 | ScriptableObject 资产驱动的条件机器 | 天命骰检定 + 数值分层 + 通用条件系统 |
| **Student Age new**（学生时代） | 中式人生养成 + 40 小游戏 | **规则引擎三件套 + UI 框架** | **跟本项目最像，最值得逐条对照** |
| **WitchSpring3**（巫师之泉3） | 韩式养成 RPG | 事件脚本 VM | 剧情直译器 + 面板堆叠 + 手柄导航 |
| **YYKiWaMi** | Roguelite + 据点养成 + ADV | 组件化表现层 | UGUITween 库（含 shader 参数）+ ADV 指令集 |

### 三条总结论

1. **视觉与演出上，本项目已经领先这五个参考项目。**VNEffects 有 40+ 演出组件、镜头编排器、天气、mood、皮肤系统；参考项目的强项只有 Spine 立绘 + 唇形同步（Magical Princess），其余表现层都比本项目弱，视觉多靠买插件。
2. **真正的金矿是「系统之间的因果结构」**，也就是 `IdeasANaylze0722.txt` 里写的「让现有系统彼此产生更强的因果关系」。这五个项目各自给出了成熟答案。
3. **Shader 这条线，这批项目给不了东西**（它们都买插件：MK.Glow / ProFlares / SpriteShadersUltimate / RFX1 / Toony Colors Pro）。要找 shader 参考应去看 2D 动作 / 弹幕 / 美术向项目。

---

## 一、优先级表（做事顺序看这个）

### A. 立刻做（半天～一天，收益明确）

| # | 项目 | 章节 | 出处 |
|---|---|---|---|
| 1 | **确定性乱数**（存档状态派生种子） | [§5](#5-确定性乱数-) | Magical Princess |
| 2 | **UI tween 的 unscaled 审查** | [§28](#28-setupdateisindependentupdate-true-) | 活侠传 |
| 3 | **音频 Ducking + BGM 位置记忆 + 转场静音窗口** | [§18](#18-音频-ducking-与-bgm-位置记忆-) | Magical Princess |
| 4 | **已读文本 + 未读保护 + 已读变色** | [§16](#16-已读文本管理未读保护--已读变色-) | 活侠传 / WitchSpring3 |
| 5 | **对话回溯（往回退一句）** | [§41c](#41-witchspring3-的事件-vm另一种-runner-架构-) | WitchSpring3 |
| 6 | **富文本工具类统一** | [§34](#34-行动反馈的标准形式-) | Student Age |

### B. 地基改造（越早做代价越小）

| # | 项目 | 章节 |
|---|---|---|
| 7 | **效果串 + 反向渲染器** | [§1](#1-效果串--反向渲染器-) + [§39a](#39-规则引擎三件套-) |
| 8 | **数值分层 + 可撤销加成**（干掉 `装备实增_`） | [§3](#3-数值分层basefinal-) + [§39b](#39-规则引擎三件套-) |
| 9 | **`VNPanelBase` 统一面板生命周期** | [§26](#26-baseview-生命周期契约-) |
| 10 | **UI tween 组件化**（含材质属性补间） | [§27](#27-ui-动画组件化-) |

### C. 内容与玩法

| # | 项目 | 章节 |
|---|---|---|
| 11 | **天命骰检定 `check` 命令**（戏剧性最高） | [§7](#7-天命骰子检定-) |
| 12 | **事件抽选引擎 + 对话池循环** | [§4](#4-事件抽选引擎-) |
| 13 | **演出插播队列** | [§2](#2-演出插播队列-) |
| 14 | **关系多维度 + 关系阶层 + 送礼偏好** | [§10](#10-好感度从一个数字到一段关系-) |
| 15 | **小游戏统一契约扩展** | [§43](#43-小游戏统一契约-) |
| 16 | **恋爱专属互动小游戏**（无输赢、纯参与感） | [§44](#44-40-个小游戏可抄清单-) |

### D. 体验层

| # | 项目 | 章节 |
|---|---|---|
| 17 | 红点系统 | [§29](#29-红点系统-) |
| 18 | 上下文敏感的主推进按钮 | [§30](#30-上下文敏感的主推进按钮-) |
| 19 | 条件逐条 Toast（√/×） | [§40](#40-emptyistrue-与逐条-toast-) |
| 20 | 手柄导航 + 按键提示条 | [§32](#32-手柄--键盘导航三种做法两个反面教材-) [§33](#33-按键提示随输入设备--当前面板自动换图-) |

---

## 二、架构地基

### 1. 效果串 + 反向渲染器 💎💎💎 🔧中
> 出处：Magical Princess `AddParams` / `LangData.GetKVParamString`

所有数值变动写成字符串，执行时算数值、显示时变图标气泡：

```
"money=-300,phyKinryoku=3/5,stress=2,item=14:1/2"
     3/5 = 随机区间        item=14:1/2 = 给 1~2 个 14 号道具
```

```csharp
LangData.GetKVParamString(param, isIcon, isColored, ...)
// "phyKinryoku=3" → 「⚡筋力 +3」（绿色，带图标）
```

**同一份字符串同时驱动结算与预览** → 商店按钮、选项按钮、课程按钮、道具说明全部自动显示「将获得什么」，企划改数值不碰 UI 代码。

**细节**：`KVParam.isSuccess` 针对「减少才是好事」的字段翻转颜色（`stress` / `badAction` / 买价倍率）。

**映射本项目**：抽 `VNEffectString`，让 `stat` 命令、`choice cost:`、商店 `gain`、日程奖励共用同一套串 + 一个 `Render(str)`。

```
choice "陪她练剑" if:体力>10 gain:"体力=-5,好感_小雪=3/5,道具_木剑=1"
```

**落地要点**：
- 属性定义加 `lowerIsBetter` 布尔
- 和 [§8](#8-通用条件系统把-if-资产化-) 共用同一个条件解析器
- 完成后直接解决 `IdeasANaylze` 第 6 条「剧情条件解释」的一半
- 完整范式参考 [§39a](#39-规则引擎三件套-) 的 `ToEffectorStr`

---

### 2. 演出插播队列 💎💎💎 🔧中
> 出处：Magical Princess `EventQueueData` + `priority`

```csharp
AddEventQueue(action, delayTime, isAutoNext, autoNextTime, debugKey)
InsertEventQueue(..., priority)   // 按优先级插队
```

| priority | 用途 |
|---|---|
| 0 | 战斗胜负面板 |
| 1~5 | 等级提升演出（体力=1 知力=2 魅力=3 感性=4 战斗=5） |
| 10 | 好感升级 / 活动升级 |
| 20 | 文字对话框 |
| 100 | 一般（默认） |

「打工完成 → 获得道具 → 能力升级 → 活动升级」的演出顺序**自动排好，剧本一行不写**。`UpdateStatus()` 每次数值变动后比对快照，跨过等级阈值就 `InsertLevelUpQueue()`。

**工程细节**：`ProcessEventQueue()` **先等一帧**再开泵，保证同帧内连续 `AddXxxQueue()` 全部进队后才开始。

**映射本项目**：做 `VNPerformanceQueue`（命令级）。剧本执行 `stat 体力 +50` → Runner 把「属性飘字」「等级提升大演出」「解锁提示」按优先级排队播完再继续。存档上很干净：队列是瞬态的，存档点只在台词处。

---

### 3. 数值分层（base/final）💎💎💎 🔧中
> 出处：活侠传 `GameStat`

```csharp
public int Value      => _value;          // 基础值，只有这个进存档
public int FinalValue => Mathf.Clamp(_value + AdditionValue, _min, _max + _limitAddition);
public int AdditionValue                  // 由「加成来源集合」动态推导，不进存档
    => _statAdditionData.Sum(d => Mathf.RoundToInt(d.GetSummary()));
```

**本项目的 `装备实增_`（卸下按实际生效量扣回防钳制不对称）就是「没有分层」逼出来的补丁。**基础值和加成混在同一个 flag 里，钳制会吃掉溢出部分，卸下时对不回去。

分层之后：
- `flag 体力` = 基础值（存档、剧本读写）
- 装备 / buff / 称号 / 天气 = 加成来源，`FinalValue` 现算
- 卸下 = 移除来源，**永不会不对称**
- 白送 `_limitAddition`「突破上限」（某些道具能把上限推超 max）

**落地要点**：
- `VNStatDef` 加 `GetFinal(name)`；加成来源存在不进存档的运行时列表，读档后由 `VNEquipment` 重建
- `if 体力>50` 用 `GetFinal`，`stat 体力 +3` 写基础值
- 顺带抄：`AddValue(value, totalAdd: true)` 同时累进「累计获得量」统计值（成就 / 结局判定要用）
- 顺带抄：`GetLevelText(index)` 把数值转**段位文字**（「性情：桀骜/孤高/中庸/温和/仁厚」），VN 里比显示 73 有味道，且天命骰直接吃段位
- **完整方案见 [§39b](#39-规则引擎三件套-)**（可撤销加成）—— 那才是根治

---

### 5. 确定性乱数 💎💎💎 🔧低
> 出处：Magical Princess `CustomRandom`
> **对应本项目已知最高优先级风险**：`IdeasANaylze0722.txt` 第 1 条「读档后随机结果不一致」

```csharp
public ulong GetCustomRandomSeed(int baseScore = 0)
{
    int num = baseScore;
    foreach (var item in itemDataList) num += item.itemId * item.data.getCount;
    return (ulong)(num + period + stress + money + blackCoin + activePower
                 + goodAction + badAction + valuePhysical + ... + btlExp);
}
// 每月初 / 每次活动前 RefreshCustomRandom() 重建 rand
```

**种子完全由存档状态派生**：
- 同存档 + 同操作序列 = 必然同结果（读档重放一致）
- 玩家改变任何数值 → 乱数流完全不同 → **反 SL 大法**
- **不需要把随机状态写进存档**，零存档兼容成本

区分两套乱数：确定性的 `rand`（事件抽选 / 成功判定 / 掉落）vs `UnityEngine.Random`（战斗伤害浮动等不需重现的）。

**映射本项目**：种子 = 所有 flag 的 `名字hash × 值` 求和 + 当前行号。改动面极小。

---

### 6. 单向依赖分层 💎💎 🔧中
> 出处：活侠传 asmdef 结构

```
Mortal.Core  ←──┬── Mortal.Free    (养成地图)
   (386 档)     ├── Mortal.Story   (剧情/检定/商店)
                ├── Mortal.Combat  (回合制决斗)
                └── Mortal.Battle  (即时战役)
```

四个玩法组件**彼此完全不引用**，一律经由 Core 的 SO 单例交换数据。且「存档就是把这几个 SO 序列化」—— 只有 4 个 SO 参与存档，任何新持久化状态必须挂到它们其中之一（本项目靠 `vn-save-compat` 技能文档提醒，活侠传靠架构强制）。

**映射本项目**：`卡牌游戏并入视觉小说专案.md` 已规划 asmdef，补一条硬性规则 —— **卡牌 asmdef 只能依赖 `VNCore`（flags / stat / 存档接口），不能依赖 `VNEffects` 主程序集**，否则以后无法单独跑卡牌 demo。

---

## 三、数值与养成骨架

### 39. 规则引擎三件套 💎💎💎 🔧高
> 出处：Student Age `Conditioner` / `Effector` / `BaseIncreaser`
> **本节是 [§1](#1-效果串--反向渲染器-) 与 [§3](#3-数值分层basefinal-) 的完成形态**

| 组件 | 型别数 | 存活期 | 语义 |
|---|---:|---|---|
| `Conditioner` | 30 | 即时求值 | 「能不能」 |
| `Effector` | 32 | 一次性执行 | 「发生什么」 |
| `BaseIncreaser` | 21 | 持续挂在 Role 上 | 「长期改变了什么」 |

四条共同骨架：
1. 构造函数吃 `List<float>`，**第 0 元素是型别码**，其余是参数
2. **链式 `parent`**，求值递归到底再回溯（Conditioner 的链 = AND）
3. **`ToString()` 同步产生说明文字** —— 同一份数据既能执行也能显示
4. `[Union(n, typeof(X))]` 多态序列化，**序号一旦发布不能改**

#### 39a. 预览与结算永不脱节 💎💎💎

```csharp
static string   ToEffectorStr(List<List<float>> effects, ..., float rate, int type);  // 只产文字
static Effector RunEffector  (List<List<float>> effects, ..., string tag, float rate); // 建立+执行
```

> 原文：UI 上鼠标移到行动按钮显示的「预计 +8 智商 −10 体力」就是它。**同一份数据驱动预览与结算，保证两者不会不一致 —— 这是本作数值系统最漂亮的地方。**

#### 39b. 效果可撤销 💎💎💎 —— 根治 `装备实增_`

```csharp
Effector.isInc == true  →  new IncreaserAttr{...}  →  Role.IncCtrl.Add(通道, attrId, inc)
Effector.incUids        →  记下本次产生的加成 uid
Role.RemoveEffect(effector.GetBaseIncreaserUids())   ←  装备卸下 / 状态结束 / Buff 到期
```

**撤销的是「加成对象」而不是「数值」**，所以永远精确。装备 / 状态 / 天气 / 称号全部走 `isInc` 通道。

#### 39c. 48 条语义化加成通道

| 群组 | 通道举例 |
|---|---|
| 属性直接加 | `AttrInc` · `AttrAddInc`(后置补正) · **`AttrFixed`(覆写一切)** · `AttrEachRoundInc` · `AttrAfterEachRoundInc` |
| 属性乘区 | `AttrMul` · `EvtAttrMul` · `AdvantageCourseEffect` ← **三者相加，不是连乘** |
| 属性连动 | `AttrPerAttr2`（「每增 N 点 X 就 +M 点 Y」，由增量触发） |
| 行动 | `ActionCostMoneyInc` · `ActionCostEnergyInc` · `ActionCostEnergyMul` |
| 关系 | `RelationTypeFavorEfficiency` · `RelationTypeFavorEachRound` |
| 开关 | `Toggle` · `DoSomething` · `UseItemType` |

通道命名即语义；想知道「某系统能被什么影响」只要 grep `IncCtrl.GetValue(RoleIncType.X, ...)`。

#### 39d. `UpdateAttr()` —— 全游戏唯一的数值写入口

```
AttrFixed 覆写检查（有就直接返回）
  → 取基值
  → real = add × (efficiency + 乘区总和)      ※ 仅 add > 0 时加乘区
  → real += AttrAddInc（加法后置补正）
  → switch 特例（体力上限连动当前体力 / 金钱取整 / 信任连带其他属性）
  → AttrPerAttr2.Run(real)（增量触发的连动）
  → 记录本回合变化量（UI 上的 ↑↓ 箭头）
  → 记流水账 RecordMgr.AddRecord(tag, key, real)
  → 反向通知条件系统（心愿/志向的累计型条件）
  → 检查红点
```

三个配套数据结构值得单独抄：

```csharp
Dictionary<int, float> accumulateAttrs;              // 累计值（历史总增量，成就用）
Dictionary<int, (float,float)> changeAttrs;          // 本回合变化量（UI ↑↓）
Dictionary<int, (int,float,float,float)> AttrRanks;  // 属性排名缓存
```

`RefreshAttr()` 是**副作用总闸门**：
- 属性 ≥ 100 且 `tag == 13` → 自动获得对应状态（先检查 `mutex` 互斥）
- 体力从 ≥20 掉到 <20 → **触发「回合中段事件」**
- 恋爱值变化 → `LoveData.OnAttrLoveChange(v)`

#### 39e. `GetRateType` 换算器 —— 隐性压力设计

| rateType | 换算 |
|---:|---|
| `10000` | `v × 心情/100` |
| `10001` | 亲密度：关系够格时 `rate = 1 + 加成 + 好感×系数 + (恋人?额外:0)`，否则 0 |
| `10104` | **需求满足度**：社交/学习/娱乐不满足时**直接扣成长效率** |
| `10201-10203` | `v × 智商/情商/体质的班级排名` |

主角三维各挂一条 `MultiplierAttr{ rateType = 10104 }` —— 比 Magical Princess 的「压力值」更隐蔽也更优雅。

---

### 8. 通用条件系统：把 `if` 资产化 💎💎 🔧中
> 出处：活侠传 五层组合

```
StatValueReference   ── 单一取值（16 种来源）
      ↓
StatGroupVariable    ── 组合运算：Sum / Max / Min / Multiply
      ↓
StatCompareItem      ── 比较：== != < <= > >=
      ↓
ConditionResultItem  ── 逻辑：AND / OR
      ↓
ConditionResultData  ── 任一为真即真
```

16 种取值来源：角色数值 / 好感度 / 旗标 / 常数 / **随机值** / 书籍 / 贵重品 / 杂物 / 数值群组（递归）/ **游戏时间** / 技能 / 秘笈等级 / 已开发项目 / 开发项目等级 / 书籍精通 / **跨周目成就**。

同一套条件资产复用于：任务触发、场所开关、商品上架、NPC 生成、剧情分支、决斗事件、天命骰加成。

**映射本项目**：把 `VNExpression` 抽成**可被资产字段引用的类型**（`[SerializeField] string condition` + Inspector 抽屉），让 `VNShopDef` / `VNQuestDef` / 事件池共用一个求值器，Lint 也能一次检查所有条件串。

**顺带加一个取值来源**：`隨機值 = Random.Range(min, max+1)` 直接写在条件里 → `if rand:30` 就是 30% 分支，比现在 `flag rand:` 再 `if` 两步顺。

---

### 12. 曲线映射：养成数值 → 战斗/检定数值 💎💎 🔧低
> 出处：活侠传 `CurveStatConvertData`

```csharp
float time = Mathf.InverseLerp(min, max, value);
return _curve.Evaluate(time);      // AnimationCurve，Inspector 上直接画
```

企划画一条曲线就定义了「体力 0~100 → 战斗血量 50~800」的任意非线性关系。决斗九项加成、战役血量、武器伤害、暴击率、单次可修炼次数全走这个。

**映射本项目**：`VNBattleModule` 现在 `patkstat/phpstat/pdefstat` 从 flag **线性**读。加 `AnimationCurve` 字段即可获得「前期成长快、后期边际递减」的正常养成手感。

---

### 25. 「剩余资源换取」的核心张力 💎💎💎 🔧低（数值设计，非代码）
> 出处：Magical Princess

```csharp
// 睡觉时：没用完的 AP × 3 = 压力减少量
AddParams("stressAP=-" + status.activePower * 3);
```

**把 AP 榨干换成长 vs 留 AP 降压力** —— 所有育成决策的张力都从这一行来。压力 ≥ 50 → 下月 AP 上限减半，形成负反馈。

配套**善恶值递减修正**（防单向堆叠）：

```csharp
if (gbBalance >= 90) 增量 *= 0.2f;  else if (>= 80) *= 0.6f;  else if (>= 70) *= 0.8f;
增量 = Max(1, 增量);   // 但至少 +1
```

**映射本项目**：有 `stat` / `event plan` / `time`，缺的正是这种让玩家纠结的资源交换公式。至少设计一组「行动力 ↔ 某个负面资源」的兑换。

---

### 40. `emptyIsTrue` 与逐条 Toast 💎💎 🔧低
> 出处：Student Age

```csharp
static bool IsMatchCondition(List<List<double>> conds, bool emptyIsTrue = false);
```

**「空条件是恒真还是恒假，由调用端决定」** —— 事件类型表逐类型配置。Lint 检查「条件为空」时该区分这两种语义。

```csharp
Conditioner.Toast(ToastType);   // 逐条弹 Toast，通过加绿 √，不通过加红 ×
```

→ 选项/行动不可用时自动告诉玩家「√ 好感 ≥ 30」「× 需要 500 金」。对应 `IdeasANaylze` 第 6 条。

---

## 四、剧本与事件系统

### 4. 事件抽选引擎 💎💎💎 🔧中
> 出处：Magical Princess `SearchSequenceList()`
> **对 VN 价值最高的一项**

不靠剧本写 `if/jump`，而让每个事件**声明自己何时能发生**：

```csharp
public class SerifConditionData {
    public string eventId;
    public TimesType times;        // ONCE / ONCE_MONTH / MANY / MANY_FLAGGED / MANY_NO_REPEAT
    public int priority;           // 抽选权重
    public int loop;               // 0=不限 1=仅一周目 2+=仅二周目后
    public LocationType location;  public SituationType situation;
    public EventActiveRange eRangeType;  public List<int> eRange;  // 月份范围
    public CharaType partner;
    public string require;         // 参数 DSL 条件
}
```

`SearchSequenceList(条件...)` → 过滤 → **按 priority 权重随机抽一条**。

三个精妙之处：

**(a) 对话池循环补充**
```csharp
// 同一组条件下的对话全部播完后，清掉这批 eventId 的已读标记（保留刚播的那条）
flagEventReadedRefresh(playedId, candidateIds);
```
→ 日常闲聊**永不枯竭**，也不会立刻重复。

**(b) `MANY_NO_REPEAT`** —— 无限次但不连续重复（记住上一条）。

**(c) 周目替换递归**
```csharp
if (loopCount >= 2) "MAIN_7_1" → "MAIN2_7_1"   // 二周目自动换简短版
```

**映射本项目**：新命令（走 `vn-new-command` 全链路）

```
pool talk chara:小雪 loc:天台 time:放学后      # 从池里抽一条播
```

需要：一张「候选标签 + 条件串 + 权重 + 次数类型」表 + 抽选器 + 池空重置。与 [§1](#1-效果串--反向渲染器-) 共用条件解析器。

**另一种实现形态**见 [§41d](#41-witchspring3-的事件-vm另一种-runner-架构-)（WitchSpring3 的 `EventLoader` 条件字段群）。

---

### 13. 事件 ID 命名规约 = 程序与内容的解耦契约 💎💎 🔧低
> 出处：Magical Princess

黑巷骰子赌博的台词 ID 规则：

```
DARKALLEY_3_F{(int)对手角色}_{阶段}_{变化}
```

程序只负责 `播放(拼出来的ID)`，**完全不需要知道内容**。加一个新对手 = 只加数据，零代码。

同类：`SKILL_PHY_1` / `SKILL_INT_1` / `SKILL_CHM_1` / `SKILL_SEN_1`（四条能力觉醒支线）、`ENDING_F{partnerId}_1`（结局 CG key）。

**映射本项目**：定一套标签命名规约写进 `HowToUse.md` + Lint 检查：

```
TALK_<角色>_<场所>_<序号>       # 日常对话（供事件池抽选）
GIFT_<角色>_<道具>_OK / _NG     # 送礼反应
LOVE_<角色>_<阶层>              # 关系突破事件
END_<路线>_<结局>               # 结局
```

Lint 可顺便检查「声明了但没有对应标签」与「有标签但没被引用」。

---

### 14. 情境旗标 💎💎 🔧低
> 出处：Magical Princess `ConversationType`

```csharp
if (key == "stress") {
    if (conversation == FATHER_CONVERSATION)
        value = (value - stressFatherAdd) * (stressFatherRate * 0.01f);
    else if (conversation == VACATION)
        value = value * (stressVacationRate * 0.01f);
}
```

同一份数据串，在不同情境走不同倍率。**映射**：约会中 / 战斗后 / 生病时 / 下雨天 → `stat 好感 +3` 自动放大缩小。剧本写一次，系统负责语境敏感。

---

### 15. 台词行内 option 清单（P3 路线图的现成需求表）💎💎💎 🔧中
> 出处：Magical Princess `SerifWindowData.option`

| option | 作用 | 本项目 |
|---|---|---|
| `lipSync` | 唇形同步 | ✅ `VNCharacterMouth` |
| `loc=` / `situation=` | 切背景 / 昼夜 | ✅ |
| `period=` | **显示年月字卡（回忆场景用假日期）** | ❌ 值得做 |
| `unknownFace` | **「？？？？」+ 灰阶立绘** | ❌ 值得做 |
| `fullscreenStill` | 全屏 CG 模式（隐藏对话框 N 秒） | 部分（有 cg，未必自动隐框） |
| `UIShow=0/1` | 隐藏 / 显示 UI | ❌ |
| `zoom=` | 镜头缩放 | ✅ camseq |
| `bgm=` / `bgmFinished=` | **后者是「这段结束后再换」** | 值得加 |
| `se=NAME:delay` | 音效带延迟 | ❌ |
| `flash=` / `bgNoize=` / `bgShake=` | 背景特效 | ✅ |
| `gflagged=` | 写跨周目已读旗标 | 部分（CG 解锁，未泛化） |
| `unlock=` | 直接解成就 | ❌ |
| `MOB1~4=` | **动态指定路人名字** | ❌ |
| `charaFinished=` | 这段结束时保留哪些角色 | ❌ |

**两个时序细节**：
- `SetDummyPeriodData()`：回忆场景临时替换「当前月份数据」让 HUD 显示不同年月，播完还原（本项目有日历 HUD，做回忆桥段正需要）
- **`gift` 在「打字机播完时」才套用**，不是台词开始时 → 数值飘字正好和台词读完的节奏对上

---

### 16. 已读文本管理：未读保护 + 已读变色 💎💎 🔧低
> 出处：活侠传 / WitchSpring3
> 对应 `IdeasANaylze0722.txt` 第 2 条

```csharp
if (_readStorySystem.ExistKey(key)) {
    文本颜色 = 已读色;                    // SystemSettings.ReadStoryColor 可关
    StoryManager.EnableSkip(true);        // 已读才能 Skip
} else {
    文本颜色 = 正常色;
    _readStorySystem.AddKey(key);
    StoryManager.EnableSkip(SystemSettings.SkipUnreadStory);  // 玩家设定
}
```

WitchSpring3 更直接：`eTalkWindowButtonState { FULL, NONE_SKIP, EMPTY }`，**没看过的事件直接 `Destroy(button_skip)`**。

已读记录是**跨周目**的（独立档案，与存档槽分离 —— 同 `VNCgUnlocks` 模式）。

**映射本项目**：跨存档的 `VNReadText` JSON，key = 台词 FNV-1a hash（**本地化系统已经在算，直接复用**）。

---

### 17. 存档快照 / 还原：回到分歧点重选 💎💎 🔧低
> 出处：活侠传 `PrologueSave`

```csharp
_restoreData = JsonUtility.ToJson(CreateSaveData(), false);   // 快照
ExecuteLoadGameData(JsonUtility.FromJson<GameSave>(_restoreData));  // 还原
```

`_restoreData` 本身也写进存档，所以**快照能跨存档存活**。

**用途**：序章重选、「命运分歧点」标记、Boss 战前自动快照。玩家看到 BAD END 后想回到最近的重要选择，不用翻存档槽。

**映射本项目**：`VNSaveData` 加 `string snapshotJson` 字段（走 `vn-save-compat` 三处同步）。

---

### 41. WitchSpring3 的事件 VM：另一种 Runner 架构 💎💎 🔧中

```
事件文件 = Resources/EventFiles/event_N.txt
指令之间用 '|' 分隔，指令内部用 ':' 分隔参数，指令末尾可加 tag
EventLoader  = 程序计数器（eventStep）
EventOperator.DoEvent(string) = CPU（约 1050 行巨型 switch）
```

**(a) 跳转目标统一支持三种写法**：`"next"` / 纯数字行号 / tag 字符串

```csharp
if (条件成立) {
    if (eventData[2] == "next") DoNextEvent();
    else if (!el.DoEventByTag(eventData[2])) el.DoEvent(int.Parse(eventData[2]));
} else { ... eventData[3] ... }
```

**(b) 不用协程，用「Invoke + 标志位 + Update 轮询」**

> **每一个「等待型」指令都对应一个 bool 标志位**，指令完成时调用 `DoNextEvent()` 把 `eventStep` 推进一格。

比协程更容易存档（程序计数器就是一个 int）。本项目「从选中行播放 + 状态重建」若变复杂，这是值得考虑的架构。

**(c) `GoToPastTalk()` —— `eventStep--` 回上一句** 💎💎
**对话回溯**。本项目有 Backlog（H 键）但那是只读列表。「往回退一句重新看」是 VN 玩家常用操作，台词行无副作用时实现成本极低。

**(d) `EventLoader` 的条件字段群** —— 事件自我声明触发条件

```
startSwitch / startSwitchsIfAllOn[] / startSwitchsIfOneOn[] / noStartSwitchNum
destroySwitchsIfAllOn/OneOn/AllOff/OneOff[]     ← 何时该销毁自己
startItem / itemID / startPat / noStartPat / leaderName
nightEvent / onlyDay / onlyAfterEnding / destroyAfterEnding
switchOnEnterEvent / switchOffAfterEvent         ← 进/出事件时自动开关
spendingTime                                     ← 事件消耗的游戏时间
```

注意 `destroySwitchsIf*`：**事件不只声明「何时触发」，还声明「何时该消失」** —— 长流程游戏防止事件池膨胀的关键。

---

### 42. YYKiWaMi 的 ADV 指令集 💎💎 🔧低

**本项目已有**：漫符（`VNCharacterMarks` 更全）、表情、站位、震屏、擦除转场、CG Still、BGM/SE/语音。

**没有、值得考虑的**：

| 指令 | 作用 | 为什么值得 |
|---|---|---|
| `[スキフェ黒]` / `[スキイン白]` | **设定「跳过时」用哪种淡入淡出** | 正常播放 3 秒水墨溶解，跳过时该用 0.2 秒黑幕 |
| `StashMenus()` / `PopMenus()` | **剧情播放时暂存 / 还原其他 UI** | HUD、日历、快捷条进演出时整体收起再还原 |
| `[枠普通][枠無し][枠黒][枠通信]` | 对话框样式即时切换（7 种） | 本项目 `ui dialogue <id>` 同理，但**「无框」（纯文字浮在画面）**值得加 |
| `[時間破れ<n>]` | 定时状态变化 | 泛化成「延迟 N 秒后执行」，配合异步 `@` |
| `[振動大]` / `[振動小]` | 手柄震动 | — |
| `[待機<t>]` / `[全消]` / `[再設置]` | 等待 / 全清 / 重新配置 | **`全消`+`再設置` = 演出重置**，场景预览与「从选中行播放」需要 |

---

## 五、玩法模块

### 7. 天命骰子检定 💎💎💎 🔧中
> 出处：活侠传 `DiceCheckResult` / `DiceMenuDialog`
> **「用最少新代码换最多戏剧张力」的一项**

现在 `if 好感_小雪 > 50` 是二元、无声、没有戏。天命骰把判定变成一场演出：

```
转盘动画（7 秒）
  基础骰 Random(1, 20)
  + 数值段位加成   ← 五段制数值 → -20 / -10 / 0 / +10 / +20
  + 好感度 × 倍率
  + 天赋等级 × 倍率
  + 旗标状态 × 倍率
  = ResultCount → 落在哪个区间 → 选中第几个分支
```

```csharp
// 数值转加成：以中段为 0，每偏一段 ±10
private int GetGameStatLevelValue(int value, int max, int levelLength) {
    int level = GameStatUtils.GetGameStatLevel(value, max, levelLength);
    return (level - levelLength / 2) * 10;
}
```

UI 逐条列出「**学问**（博览群书）+20」「与**四师兄**的交情 +8」「**心理卫生**（憔悴）−10」，播「加成汇入总数」的数字滚动，最后转盘停下。

**命运点重掷**：
```csharp
// 一次检定内，第一次重掷扣 1 点命运，之后可无限次重掷
if (_useFatePoint) return true;
if (Stats.Get(命運).FinalValue > 0) return true;
```

**映射本项目**：新命令

```
check 说服 base:20 add:"学问*1,好感_小雪*0.5,任务_信物*10" pass:35 great:50
* 大成功
    小雪眼睛一亮……
* 成功
* 失败
```

演出几乎全现成：`VNToast` 列加成条目、`VNStatsHud` 数字滚动、`fx flash`/`shockwave` 揭晓、`mood` 成败色调。只需新增一个转盘 UI。「命运点」= 一个新 `VNStatDef`。

---

### 11. 回合制猜拳对招 💎💎 🔧中
> 出处：活侠传 `Mortal.Combat`

一对一，双方**同时**从 5 种动作择一：

| 动作 | 数值 | 相克 |
|---|---|---|
| 嘴砲 | 嘴力 | 被「捅人」克 |
| 捅人 | 内力/刀剑/拳掌 | 被「绝招」克 |
| 暗器 | 暗器 | 被「绝招」克 |
| 备揍 | 防御/轻功 | 恒成功（减伤+回气力） |
| 绝招 1/2/3 | 综合 | 恒成功，等级由**气力**决定 |

「气力」同时是行动力和绝招门槛。核心是一张 5×5 对战表（约 340 行）。

**为什么比 HP 消耗式战斗更适合 VN**：零操作门槛（每回合按一个按钮但有真实博弈）、完全靠养成数值（动作绑不同属性 → 养成路线决定战术）、有对白空间（每回合可插角色台词，嘴砲成功还触发特殊对话事件）。

**两个可直接抄的细节**：

```csharp
// 敌人意图提示 —— 优雅的难度自适应
float p = 玩家战力比, e = 敌人战力比;
提示概率 = Max(0, Max(p * 0.33f, 0) + (p - e) * 0.5f);
// 玩家越强、领先越多，越容易看穿敌人下一步
```

```csharp
// 教学保护旗标：Z00001 为 0 时，玩家自动获得状态 T001 且战力估值 +300
// 一个旗标同时管「新手保护 buff」和「战力显示虚高」
```

---

### 43. 小游戏统一契约 💎💎💎 🔧中
> 出处：Student Age `FuncMgr.OpenMiniGame`（40 个入口，一个函数）

```csharp
public void OpenMiniGame(int gameId, MiniGameFromType type = None, int typeId = 0,
                         List<double> parms = null,
                         Action success = null, Action fail = null, Action<float> result = null,
                         int bgmId = 0)
```

**(a) `MiniGameFromType` —— 来源决定回流路径**

| 来源 | 结算回流 |
|---|---|
| `None`（行动直接调用） | `result` 回调里跑 `ActionCfg.effect` |
| `Option`（对话选项） | 胜 → `OptionCfg.effect` + `talkId`；败 → `effect2` + `talkId2` |
| `Evt`（事件） | `ShowEvent(typeId)` |
| `Talk`（对话） | 胜 → `TalkCfg.effect` + `nextTalk` |
| `Progress`（难度递增） | `result(progress)` —— **传回完成度 0~1，不是胜/败** |
| `Level`（NPC 社交） | `MiniGameData.EndGame(id, isWin)` |

**同一个小游戏，从不同地方进入就有不同结算方式。**本项目 `event <id>` + `* 结果行` 是固定契约，加一个「来源」维度能让同一模块被剧本、选项、日程、地图各自使用。

**(b) 结算一律写在 `CloseView()`** —— **应写进 `vn-new-event-module` 技能**

> 判胜负的地方只设标志位，回流一律在 `CloseView()` 里做。

保证「无论通关、失败、还是中途 ESC 退出，结算路径只有一条」。

**(c) `loseCombo` 难度补偿**

```csharp
public float GetCost() => cfg.cost * (loseCombo > 0 ? 0.5f : 1f);   // 连输后成本减半
// 占卜特例：输了也升关
```

---

### 44. 40 个小游戏可抄清单 💎💎 🔧各异
> 出处：Student Age

本项目现有：QTE / 地图 / 战斗 / 商店 / 日程 / 结算 / 限时问答。

| 玩法 | 说明 | 推荐 |
|---|---|---|
| **翻牌配对 / 连连看** | 经典记忆游戏 | 💎💎💎 🔧低 |
| **节奏条 QTE**（对话式） | 台词播放中弹出节奏条 | 💎💎💎 🔧低 |
| **蓄力条 QTE** | 按住蓄力、放开判定 | 💎💎💎 🔧低 |
| **占卜翻牌** | **输了也算过关**，纯演出 | 💎💎💎 🔧低（塔罗/抽签） |
| **钢琴 Simon says** | 记忆并复现音符序列 | 💎💎 🔧低（音乐社/才艺） |
| **接红包** | 落下物接取 | 💎💎 🔧低（改造粒子/落叶系统） |
| **造句 / 排序** | 词块拖拽排序 | 💎💎 🔧低（与 VN 气质最搭） |
| **走迷宫（点亮路径）** | 路径规划 | 💎💎 🔧中 |
| **考试（六边格子+掷骰）** | 六边地图，每格一种题型，解开掷骰得分并打开相邻格 | 💎💎💎 🔧高（设计极巧） |
| **话术卡牌**（15 状态机） | 辩论/说服卡牌战 | 💎💎💎 🔧高（卡牌项目若不并入，这是替代方案） |
| **大头贴 / 画画 / 丝带留言 / 做早餐** | **恋爱专属互动**（无输赢，纯参与感） | 💎💎💎 🔧中 —— **对 galge 最有价值** |
| 数独/速算/填字/拼图/魔方/打砖块/跨栏/羽球/指尖刀/篮球单挑 | 通用小游戏 | 💎 🔧各异 |
| 文字输入 | 玩家打字 | 💎💎 🔧低 |

**最推荐最后那类「无输赢的恋爱互动」** —— 只有「玩家亲手做了一件事」的参与感，正是 VN 最缺的。YYKiWaMi 的 `Spa_IllustModel`（立绘部位碰撞 + 晃动 + 表情反馈）是同类设计。

---

## 六、UI 表现 / 交互 / 动画

### 26. `BaseView` 生命周期契约 💎💎💎 🔧中
> 出处：Student Age `UIMgr` + `BaseView`

本项目现有面板：背包、属性、商店、日程、结算弹窗、画廊、Backlog、存读档、任务日志、SNS、配置 —— **各写各的开关逻辑**。

`UIMgr` 七层：`Background / Main / Normal / Tips / Fly / Foreground`（+ `None` = 用 View 自身声明的层）

| 钩子 | 何时跑 | 用途 |
|---|---|---|
| `InitUI()` | 只跑一次 | 绑按钮、建对象池 |
| `OnOpen()` | 每次开启 | 读参数、抓数据 |
| `Refresh()` | 重复开启 | 重画列表 |
| `OnUpdateEventListeners(bool)` | Open / Hide | **注册与注销成对，框架代管** |
| `OnUpdateRedpointRegisters(bool)` | 同上 | 红点注册 |
| `CloseView()` | 关闭 | **小游戏结算一律写这里** |

`UpdateListener(evtId, cb)` **把订阅记在 View 自己身上**，`Open()` 全注册、`Hide()` 全注销 → 从机制上消灭「忘记解绑」。

标志位：`isShowMask` / `enableCloseByMask` / `enableCloseByRightClick` / `isDontDestroy` / `isAlwaysBottom` / `preloadUrls`。

**收益不只是少写代码**：「按 ESC 一层层退回」「面板互斥」「打开面板时暂停剧本」会从散落的判断变成框架行为。

---

### 27. UI 动画组件化 💎💎💎 🔧中
> 出处：YYKiWaMi `UGUITween` 家族

| 组件 | 目标 |
|---|---|
| `UGUITween`（基类） | 共用曲线 / 时长 / 延迟 / 播放控制 |
| `UGUITweenAlpha` / `UGUITweenCanvasGroupAlpha` | 透明度 |
| `UGUITweenColor` / `SpriteTweenColor` | 颜色 |
| `UGUITweenPosition` / `Rotation` / `Scale` | Transform |
| **`TweenMaterialColor` / `TweenMaterialFloat` / `TweenMaterialVector`** | **Shader 参数** |
| `TweenTimescale` | `Time.timeScale` |
| `UGUICenterOnChild` | ScrollRect 自动居中 |

另有 `TransformMover` / `TransformMoverArch`（抛物线）/ `TransformRotater` / `TransformShaker` / `TransformUpDown` / `SimpleEasing`。

**本项目所有补间都写在代码里**（`DOScaleMultiplier`、`SetLink(gameObject)`）—— 可控，但每加一个面板要重写入场/出场、参数只能改代码、无法编辑期预览。

**做 `VNTween` 系列组件**（基类管 curve / duration / delay / loop / playOnEnable / **unscaled**），把已有写法包一层。**尤其 `TweenMaterialFloat`** —— `VNImageEffectController` 管着九种效果的材质属性（溶解量、扫光位置、发光强度、HSV），组件化 = shader 能力从「写代码」下放到「拖组件」。

---

### 28. `.SetUpdate(isIndependentUpdate: true)` 💎💎💎 🔧低
> 出处：活侠传 `CommonPanel`

```csharp
_canvasGroupFade = CanvasGroup.DOFade(show ? 1f : 0f, 0.75f)
                              .SetUpdate(isIndependentUpdate: true);   // ← 关键
```

> 面板开启时 `Time.timeScale` 常常是 0 → **淡入动画必须走 unscaled time，否则 alpha 永远停在 0**。

活侠传「常见坑表」第一条：

| 症状 | 原因 |
|---|---|
| 面板开了但看不到 | `CanvasGroup.alpha` 由 DOTween 补间，`Time.timeScale = 0` 且没 `SetUpdate(true)` → 永远停在 0 |
| 换场景后大量 NRE | 订阅了 SO 的 `OnValueChanged` 没在 `OnDisable` 退订（**SO 生命周期比场景长**） |
| 手柄玩家看不到 tooltip | 只实作了 `IPointerEnterHandler`，没实作 `ISelectHandler` |
| 数值不刷新 | 没人调 `UpdateStatus()`（这套架构不做自动轮询） |

**本项目有 Skip 模式（timeScale 加速）和事件模块暂停 → 值得全项目 grep 一遍 UI tween，确认哪些该走 unscaled。**

---

### 29. 红点系统 💎💎 🔧中
> 出处：Student Age `RedpointMgr`

```csharp
Register(id, xform, offsetX, offsetY, icon, pos, size);   // 登记 UI 节点到某红点 ID
Active(id, bool, childId);                                 // 亮灭，向上传播到合并的父红点
CombineRedpoint(parent, children);                         // 父红点 = 子红点的或
```

触发类型：`Force` / `AttrUpdate`（属性变动引发）/ `RelationChange`。

最漂亮的一行在 `Role.UpdateAttr()` 末尾：

```csharp
RedpointHelper.CheckRedpoint(RedpointTypeDefine.AttrUpdate, _key);
```

→ **「钱够了，商店图标自动亮红点」**，商店代码不需要监听金钱。

**映射本项目**：背包、商店、任务日志、画廊、SNS 都是「玩家可能错过新内容」的地方。与已有的 `VNFlags.Changed` 事件天然契合。

---

### 30. 上下文敏感的主推进按钮 💎💎💎 🔧中
> 出处：Student Age `MainView.Go()`（29 分支分派器）

| `goState` | 行为 |
|---:|---|
| `1` / `18` | 推进回合（默认态） |
| `5` / `13` | 学期总结 / 看成绩 |
| `6` / `15` | 领奖状 / 领心愿奖励 |
| `11` / `14` | 选新年心愿 / 选关注对象 |
| `19`–`28` | 各种升级与功能界面 |

`RefreshGoBtn()` 按优先序挑出 `goState`。

> **玩家只要一直点 `Go`，就会被引导走完所有待办，最后才推进回合。**

配套摩擦：`NextRound()` 时若体力仍 ≥ 10，先弹「体力还没用完，确定跳过？」

**映射本项目**：`event plan` + `time` + 日历 HUD。一个「本回合还有 N 件事没做」的智能推进按钮能显著降低复合系统的操作负担。**这是节奏设计，不是代码量问题。**

---

### 31. 列表渲染的唯一模式 💎💎 🔧低
> 出处：Student Age

```csharp
UIItemGroup.SetDatas(IEnumerable datas);
pool.SetOnCreate(cb);    // 只跑一次 —— 绑事件
pool.SetOnRender(cb);    // 每次重绘 —— 填数据
```

命名规约：`btn_` / `icon_`(图集) / `img_`(贴图) / `txt_` / `txtex_`(TMP) / `group_` / `root_` / `itemgroup_` / `Cell_XxxUI`。

双层结构：**`GenUI.*UI`（工具从 prefab 生成，只有字段）↔ `View.*View`（手写逻辑，继承前者）**。

另有 `UIScroll` / `UIScrollGrid` / `UIScrollLinear` **虚拟化滚动**（只实例化可视范围内的项）、`PolygonImage`（异形点击区）、`EventPenetrate`（点击穿透）。

**虚拟化对画廊（CG 变多后）和 Backlog（长对话）是刚需。**

---

### 32. 手柄 / 键盘导航：三种做法，两个反面教材 💎💎 🔧中

| 项目 | 做法 | 评价 |
|---|---|---|
| Magical Princess | `ButtonBase` 上四个显式引用 `key_item_up/down/left/right` 构成导航图 + `ButtonGroup` 焦点栈 | ✅ **最推荐**（不用 Unity 内建 Navigation，复杂布局会跳错） |
| 活侠传 | 每个 UI 元件同时实作 `IPointerEnterHandler` + `ISelectHandler` | ✅ 简单有效 |
| WitchSpring3 | 三个巨型控制器（2859 + 3467 + 2271 行），**且每个面板自己又实作一整套 `Move_Up/Down/Left/Right`** | ❌ 原文：「本项目最大的重复代码来源」 |

焦点堆叠：
```csharp
AddButtonGroup(group)     // 开窗口：旧的失焦
RemoveButtonGroup()       // 关窗口：旧的复焦
```

**两个小组件值得抄**：
- `UnTouchCover`（**防误触遮罩**，`SIMPLE_COVER` / `HIGHSPEED_COVER` / `FORCE_CLOSE`）—— 演出队列开始前盖上，避免转场时乱点
- `FocusCursor`（焦点框跟随）、`ClickFlash`（点击闪光）

WitchSpring3 的**圆形快捷菜单**（`FieldMenu` / `CircleMenu`）交互形式本身值得考虑，但它的实现是 1150 行硬编码穷举（`switch(count)` × `switch(current)` × `switch(type)`），**别抄实现**。

---

### 33. 按键提示随「输入设备 + 当前面板」自动换图 💎💎 🔧低
> 出处：WitchSpring3 `keyCommandController`

```csharp
public SpriteData ButtonSprites_Xbox, ButtonSprites_PlayStation, ButtonSprites_Switch, ButtonSprites_Keyboard;
public void Set(string name, bool fix = false);   // 切到某个面板的按键组
```

设备检测每 2 秒扫一次 `Input.GetJoystickNames()`，切换时 `Cursor.visible = (InputType == KEYBOARD)`。

「屏幕底部按键提示条随上下文变化」是最能提升完成度感的部分之一。

---

### 34. 行动反馈的标准形式 💎💎 🔧低
> 出处：Student Age

```csharp
HintHelper.ShowLoadingResult(txt, loadedCallback, closeCallback, talk, atlasUrl, ...)
// 显示「正在做某事」读条 → 读条完执行 loadedCallback（真正结算）→ 关闭时执行 closeCallback
```

所有行动都走这条路 → **玩家看到的「行动 → 动画 → 结果」节奏全游戏一致**。

| 组件 | 用途 |
|---|---|
| `HintHelper` | 14 个静态包装：`ShowConfirm` / `ShowWin` / `ShowLose` / `ShowOptions` / `ShowRewardSelect` … |
| `ToastHelper` / `ToastCtrl` | 流水提示；`AddAttr(id, v)` / `AddItem(id)` 有专用格式 |
| **`HtmlTxtUtil`** | **富文本**：`ToPositive` 绿字 / `ToNegative` 红字 / `ToAttrStr(id, v)` / `ToSpriteTxtWithDigital` |
| `TipsMgr` | 主界面左侧**待办条**，`AddEvtTips(evtId)` 把事件挂成待办 |

**`HtmlTxtUtil` 直接抄进 `VNToast` / 属性面板** —— 「+3 体力」的绿色、图标、数字格式化应该有唯一实现。

---

### 18. 音频 Ducking 与 BGM 位置记忆 💎💎💎 🔧低
> 出处：Magical Princess `SoundController`

```csharp
ToDucking(0.5f)      // 说话时压低 BGM
ToFullDucking()
ToMenu()             // 开菜单时
ToDefault()

PlayFieldBGM(startAtSaveDuration: true)   // 回到场景时 BGM 从上次位置续播
SaveFieldPlayTime()                        // 离开时记住进度

SetSENoPlayTime(time)                      // 转场时短暂静音 SE，避免爆音
```

**映射本项目**：`VNAudio` 有三通道 + 基准音量，加这三个 —— 语音播放时自动 duck BGM、BGM 位置记忆、转场静音窗口。成本极低，听感提升明显。

---

## 七、Shader 与视觉

> **诚实结论**：这五个参考项目 shader 内容都很薄，且本项目的 shader 能力已经超过它们。它们的做法基本是买插件：
> Magical Princess = `SpriteShadersUltimate` / `EpicToonFX` / `DynamicBone`
> WitchSpring3 = `MK.Glow` / `ProFlares` / `Toony Colors Pro 2` / `CalmWater` / `VLB` / `UiParticles` / `cakeslice`
> YYKiWaMi = `RFX1`(35 档) / PostProcessing v1
>
> **要找 shader 参考，应去看目录下的 2D 动作 / 弹幕 / 美术向项目**（东方异域见闻、骸ノ螺旋、Bloodroots、GRIME、Death's Door、Big Hops Frog）。

仍然捞到四条有价值的：

### 35. 材质属性补间组件化 💎💎💎 🔧中
同 [§27](#27-ui-动画组件化-)。`TweenMaterialColor` / `TweenMaterialFloat` / `TweenMaterialVector`。

### 36. `MaterialPool` 💎💎 🔧低
> 出处：Student Age（五种对象池之一）

本项目硬约定是「每张图独立材质实例」（`VNImageEffectController` 管理）—— 立绘多了之后是实打实的 GC 与 SetPass 压力。材质池 + `MaterialPropertyBlock` 是标准解法。

### 37. uGUI 内的粒子遮罩 💎💎 🔧中
> 出处：WitchSpring3 用了 `UiParticles`

本项目粒子（`VNAmbientParticles` / `VNFoliageSystem`）是场外 ParticleSystem + sortingOrder，**无法被 UI 的 Mask 裁剪、无法跟着 ScrollRect 滚动、无法插在两层 UI 中间**。

想做「对话框内部飘出的星光」「选项按钮上的粒子」「CG 画廊格子里的闪烁」就需要一套走 uGUI 顶点流的粒子。**这是现有视觉体系里唯一明显的空缺。**

### 38. 立绘材质属性封装 + 脉动发光 💎💎 🔧低
> 出处：Magical Princess `SpineCharacterController`

```csharp
public float mAlpha { get; set; }
public float mTint  { get; set; }      public Color mTintColor { get; set; }
public float mPingPongGlow;            // 战斗中被瞄准时的脉动发光
```

`mPingPongGlow` 的用法很好：**「当前被指向的角色持续脉动发光」**。

本项目 `VNSpeakerHighlight` 现在大概是压暗非说话者 —— 改成「说话者轮廓光缓慢脉动」会更高级，且不损失非说话者可读性。

### 附：转场语义化 💎💎 🔧低
> 出处：YYKiWaMi `FadeManager`

```csharp
public enum eFadeType { Color, White, Smoke, Eyecatch, Max }
```

- `Color`（黑）— 一般转场
- `White` — Logo→Title、小游戏结束
- `Smoke`（烟）— 特殊结局
- `Eyecatch` — 场景切换（**有角色演出**）；`IsAnimating()` 让资源加载等它结束，避免「眼罩动画还在播就开始卡顿的加载」

**把转场按「语义」而非「效果」命名**，剧本写 `transition 回忆` 而不是 `transition 溶解`。

### 附：链式协程编排 💎💎 🔧低
> 出处：YYKiWaMi `Co_Extension`

```csharp
Co.Run(FadeManager.instance.FadeEnd(eFadeType.Color)
        .Exe(() => { Tutorial.instance.SpaPlayedEnd(); })     // 插入同步动作
        .Next(menu_home.Open())                               // 接续下一个协程
        .Exe(() => { Co.Run(Event2DPanel.instance.bg.CrossFadeChange(1)); }));
```

`.Run()` / `.Next(other)` / `.Exe(Action)` / `.Wait()` / `.IsRun()`。

另外：**所有协程集中挂在全局单例 `CoroutineManager` 上**，因为场景切换会 `Destroy` 掉宿主，绑在被销毁对象上的协程会立刻中断。对本项目的演出队列（[§2](#2-演出插播队列-)）有参考价值。

---

## 八、关系系统与长线元游戏

### 10. 好感度：从「一个数字」到「一段关系」 💎💎💎 🔧中
> 出处：Magical Princess `FriendDataParam`（13 个字段）

```csharp
fMeet          // 累计相遇      fMeetMonth          // 本月相遇（每月归零）
fFavarite      // 好感度        fConversationMonth
fConversation  // 累计对话      fDateMonth
fDate          // 累计约会      fPresentMonth
fPresent       // 累计送礼
fLoveEvents    // 关系阶层 0~4  ← 关键
fBeHospitalized / fLeaved       // 住院/离开剩余月数
```

**(a) 关系阶层独立于好感度数值**
```csharp
if (fLoveEvents >= 4 && fFavarite >= 100) return "FRIENDRANK_5";  // 恋人
```
好感度是「量」，阶层是「质」—— 刷好感刷不出关系突破，必须触发剧情事件。**防止「送礼刷满攻略」的核心设计。**

**(b) `fLoveEvents == 0` 表示「还没登场」**，直接控制角色是否出现在场景。首次相遇事件搜索时特意传 `_isLoveEvent0Check: false` 让未登场角色也能触发 —— **「新角色如何第一次出现」的完整机制**。

**(c) 送礼偏好表**
```csharp
ItemData.pValue   // List<int>，index = friendId，value = 送礼好感增减（可为负）
// pValue[i] >= 1 → 喜欢；>= 2 → 特别喜欢
```
外加每角色对每道具的**专属反应事件**：数据写成 `"F0_XXX,F1_YYY"`，用 `Contains("F" + friendId)` 挑出来，程序不需要知道内容。

**(d) 每月互动次数上限**（`fConversationMonth` 等每月归零）—— 强迫玩家取舍攻略谁。

**(e) 住院机制**：赤月战没被带去的角色会受伤住院一个月 → 逼玩家**轮流带不同角色**。极优雅的「强制内容曝光」设计。

**映射本项目**：现在好感度大概是一个 `flag 好感_小雪`。建议至少加：关系阶层（独立 flag，只有剧情事件能推进）、本月互动次数（配合 `time` 重置）、送礼偏好表（挂在 `VNShopDef.Item` 上）、角色在场表（`VNPlanDef` 或日历系统承载）。

对应 `IdeasANaylze` 第 4、5 条（角色记忆系统）。

---

### 9. 任务系统升级：从「flag 状态」到「触发点」 💎💎 🔧中
> 出处：活侠传 `MissionCheckData`

| 字段 | 作用 |
|---|---|
| `_position` | **绑定地点**；`無` = 不限地点、立即触发 |
| `_conditions` | 条件全部 AND |
| `_dayCheck` + `_timeCheckType` | 特定时间 / 时间区间 / 周期 |
| `_startScript` | 触发的剧本 |
| `_nextStateName` | 触发后自动推进到下一个 key |
| `_startDialogs` | 自由地图上的开场提示对白 |
| **`_alertDialogs`** | **玩家走错地点时的提示对白** |
| `_modifyFlagsWhenTrue/False` | 判定后的旗标副作用 |

`HasAnyMissionTrigger()` 的**触发优先序**：
1. 主线之前的支线 → 2. 主线本身 → 3. 主线之后的支线

任一命中就设定剧本并跳过去 —— **「地图型 VN」的核心调度器**。

**映射本项目**：`VNMapModule` + `VNPlanModule` + `quest` 接起来就需要这个调度器。`_alertDialogs`（走错地点的提示）尤其值得抄 —— VN 里最常见的玩家卡关点。

---

### 23. 跨周目系统（NG+）💎💎 🔧中

| | Magical Princess | 活侠传 |
|---|---|---|
| 载体 | `GrobalStatusData` | `UniverseSave`（独立档案） |
| 内容 | 成就点、已读旗标、图鉴、历史最高好感 | 夺命谱（死法）/汗青书（结局）/风云史（成就）/结局 CG |
| 继承方式 | **成就点商店**（买强化） | **达标自动发放**（成就数 ≥ N → 送命运点/数值） |
| 剧情处理 | 二周目主线自动换简短版 | `NewGamePlus` 旗标供剧本判断 |

**「死法图鉴」（夺命谱）**把 BAD END / 死亡变成收集要素，玩家反而主动去死。本项目有 CG 画廊，加「结局图鉴 / 死法图鉴」几乎是同一套 UI。

**「恋路の羅針盤」道具**：持有时事件抽选改为**优先播放没看过的**约会事件 —— 玩家友善的收集辅助道具，而不是列一张攻略表。

YYKiWaMi 的 Gallery 九分页可作为图鉴规模参考：角色档案 / 相簿 / 剧情 Still / 剧情回放 / 支线回放 / 小游戏回忆 / 道具图鉴 / 敌人图鉴 / 游玩记录。**登记时机**分别在「鉴定或取得时」「击倒时」「装备时」。

---

### 24. 结局系统：条件串 + 分类 + 配对 💎💎 🔧中

```csharp
EndingJobData.require    // 参数 DSL 条件串，决定这个职业能不能选
GetEndingPartner()       // 依好感度配对恋人
三种结局分类 + True End / Another End
```

**结局条件用的是和活动、事件同一套 DSL** → 可在游戏内做「结局解锁条件提示」界面（模糊或明确，玩家可选）。这是 `IdeasANaylze` 第 3 条「路线与结局流程图」的实现基础。

---

## 九、工程纪律

### 45. 存档兼容的三条铁律 💎💎💎 🔧低
> 出处：Magical Princess `AddFeatureGuide` §2（原文标为「全文最重要」）

读档是**按 id 比对**而非按数组索引：

#### ✅ 安全
| 操作 | 原因 |
|---|---|
| 在列表尾端追加新 id | 旧存档没这笔，跳过即可，保持初值 |
| **在列表中间插入新 id**（用没用过的号） | id 比对不受顺序影响 |
| **调整既有项目的排列顺序** | id 没变 |
| 修改既有项目的「定义字段」（价格/效果/数值） | 定义层不进存档，**改了立即生效于所有存档** |

#### ❌ 会毁掉旧存档
| 操作 | 后果 |
|---|---|
| **回收/改用既有 id** | 「50 号有 3 个」变成「剑有 3 把」，**静默错乱** |
| **删除既有 id** | `GetXxx(50)` 返回 null → NRE |
| **改动序列化短名**（`JsonProperty("c")`） | 该字段归零 |

#### ⚠ 以「数组索引」使用的 enum 只能往后加
```csharp
locations[(int)_location].Open();                    // LocationType = 数组索引
Instantiate(prefabs[(int)waveData.type]);            // EnemyType = 数组索引
ActivityType success = (ActivityType)(activity + 1); // ACT_n/_SUCCESS/_FAIL 必须三个一组连续排
```

**号段保留策略**：新内容一律从 1000 / 500 / 200 起，永不与既有重叠。

**映射本项目**：flag 是字符串 key（比数字 id 安全），但 `VNStatDef` / `VNQuestDef` / `VNShopDef` / `VNWeatherDef` / `VNQuizDef` / `VNPlanDef` 的 id 字段，以及装备部位编号（`装备_<道具id>=部位编号`）同样适用。**建议浓缩进 `vn-save-compat` 技能。**

---

### 46. `LoadEnd()` —— 旧存档修补层的固定落点 💎💎 🔧低
> 出处：Student Age `ISaveLoad`（先全部 `Load()`，再全部 `LoadEnd()`）

```csharp
// RoleMgr.LoadEnd()
model.kzoneData?.CheckWrongId();
model.actionData?.CheckWrongAction();
model.studyData?.CheckOldSave();
model.studyData?.RefreshScores();
```

**所有版本迁移代码集中在一个地方**，而不是散在各字段的读取处。

**映射本项目**：`VNSaveData.RestoreSnapshot` 加一个 `MigrateLegacy()` 阶段。

---

### 47. 「数据是被动的」—— 最容易漏的第 5 步 💎💎 🔧低
> 出处：Magical Princess `AddFeatureGuide` §3

```
① 配 id → ② 写定义 → ③ 补文字（四语言）→ ④ 补资产 → ⑤ 接线
```

> **第 ⑤ 步最容易漏。**你在 MasterData 加了一件道具，如果没有任何 itemGroup、商店清单、活动 gift 或制作配方指向它，**玩家永远拿不到**。

检查表里特别适合本项目的一条：

> 写进 gift / require 的 DSL key，字段里真的有吗？**拼错会静默无效。**

→ Lint 应**双向**检查：「定义了但从未被引用的资产 id」+「引用了但不存在的 id」。

---

### 48. 反面教材清单

| 反模式 | 出处 | 教训 |
|---|---|---|
| 反射字符串拼接建类（`"Part_" + next`、`"Rogue_Condition" + eCondition`） | YYKiWaMi | 编译期零检查，**改名即静默失效** |
| `SendMessage(functionName)` 回调 | WitchSpring3 | 同上 |
| `MakeQuestionPanel` 七个重载 | WitchSpring3 | 参数组合爆炸，该用配置对象 |
| 每个面板自己实作一套导航方法 | WitchSpring3 | 3 个控制器 8500 行，最大重复代码源 |
| 1150 行硬编码的圆形菜单走位穷举 | WitchSpring3 | — |
| `SingletonMonoBehaviour.instance` 永不为 null | YYKiWaMi | 掩盖「单例还没初始化」的错误 |
| SO 事件订阅不退订 | 活侠传 | **SO 生命周期比场景长** → `MissingReferenceException` |
| `MyData.cs` 5600 行 God Object | Magical Princess | 所有字段名成为隐性 API |

---

## 十、明确不建议抄的

| 项目 | 理由 |
|---|---|
| `MyData.cs` 5600 行 God Object | 改字段名静默破坏数据表 |
| 反射索引器 `this[string fieldName]` | 每次 `GetField()`，性能与类型安全双输。本项目 `VNFlags` 字典方案更好 |
| `BinaryFormatter` 存档（活侠传 / YYKiWaMi） | .NET 已弃用，有安全问题。本项目 JSON 更好 |
| 「共用实例改写」（`FRIEND_TALK` 全局单例被每个角色覆写） | 省内存但埋了「不能同时被两个角色使用」的雷 |
| `giftSuccess` 数组在不同场所有不同语义（有时按等级索引，有时当 6 格查表） | 典型隐性约定，Lint 无法检查 |
| Rewired / Wwise / Fungus+Lua | 本项目已有更轻的方案 |
| WitchSpring3 的手柄导航实现 | 见 [§48](#48-反面教材清单) |

---

## 十一、原文位置索引

| 项目 | 文档路径（相对 `D:\_A QuickStart\Unity & Game\Unity Source Code Sample\`） | 行数 |
|---|---|---|
| Magical Princess | `MagicalPRincess\MagicalPRincess_CodebaseAnaylze.md` | 5651 |
| Magical Princess | `MagicalPRincess\MagicalPRincess_AddFeatureGuide.md` | 2531 |
| 活侠传 | `Live Wuhia Lengend\Source com\CodebaseAnaylze.md` | 2498 |
| 活侠传 | `Live Wuhia Lengend\Source com\AddFeatureGuide.md` | 2486 |
| Student Age new | `Student Age new\StudentAgeNew_CodebaseAnaylze.md`（主档） | — |
| Student Age new | `…CodebaseAnaylze.Core.md`（UI 框架 / 存档 / 热键 / FuncMgr） | — |
| Student Age new | `…CodebaseAnaylze.Rules.md`（**规则引擎三件套**） | — |
| Student Age new | `…CodebaseAnaylze.Minigames.md`（**小游戏统一契约 + 40 个玩法**） | — |
| Student Age new | `…CodebaseAnaylze.Data.md` / `…Gameplay.md` | — |
| WitchSpring3 | `WitchSpring3\WitchSpring3_CodebaseAnaylze.md`（§7 事件 VM / §16 UI / §18 输入） | 2900+ |
| YYKiWaMi | `YYKiWaMi\docs\01-框架层-MTX.md`（**Tween 库 / FadeManager / 协程**） | — |
| YYKiWaMi | `YYKiWaMi\docs\08-Home-Bath-Story-Gallery.md`（**ADV 指令集 / Gallery**） | — |
| YYKiWaMi | `YYKiWaMi\docs\07-Spa温泉小游戏.md`（立绘互动） | — |

### 本轮未细读（需要时可补）

- 活侠传 `AddFeatureGuide` 的决斗技能 / 战役 NPC 章节（约 800 行，纯战斗扩展）
- Student Age 每个小游戏的内部实现（只读了目录与统一契约）
- YYKiWaMi 的 Rogue 迷宫部分（与 VN 无关）
- **Shader 专项**：需另扫 2D 动作 / 弹幕 / 美术向项目
