# HowToUse.md — 视觉小说剧本系统使用教程

> 本教程教你如何用纯文本剧本制作视觉小说的完整剧情演出与养成玩法。
> 对应实现：`Assets/Scripts/VNEffects/Script/`
> （核心演出 + 分支 + 存档 + 章节 + CG + 玩法事件 + 任务 + 养成属性 + 日程 + 本地化）
> 使用与规划类 FAQ（怎么跳转/怎么设计 Game Loop/怎么换 UI 风格）见 `GeneralQuestionGuide.md`。

---

## 目录

1. [三分钟上手](#一三分钟上手)
2. [剧本文件基础规则](#二剧本文件基础规则)
3. [台词的写法](#三台词的写法)
4. [演出命令详解](#四演出命令详解)
5. [分支与变量（选项/多结局/跨文件章节）](#五分支与变量)
6. [玩法系统命令（事件/任务/属性/日程/商店）](#六玩法系统命令)
7. [演出 timing 控制（@ 与 wait）](#七演出-timing-控制)
8. [角色 / 背景 / CG 的资产管理](#八资产管理)
9. [玩家操作（推进/回想/自动/快进/存档/各面板）](#九玩家操作)
10. [本地化（中/英/日）](#十本地化)
11. [剧本可视化编辑器](#十一剧本可视化编辑器)
12. [完整示例剧本](#十二完整示例剧本)
13. [常见问题排查](#十三常见问题排查)

---

## 一、三分钟上手

1. Unity 菜单 **Tools → VN Effects → Create Script Demo Scene**（只需一次，之后重复使用该场景）
2. 打开 `Assets/Scenarios/Demo.vn.txt`，用**任何文本编辑器**修改
3. 回 Unity 点 **Play** —— 你的修改立即生效

写剧本 = 写文本。一行一条命令，从上往下执行。最小可用剧本：

```
bg bg1
show 亚里沙 at:center with:DissolveGlow
亚里沙: 你好，世界！
hide 亚里沙 with:dissolve
```

> 💡 不想手写文本？**Tools → VN Effects → Scenario Editor** 提供可视化编辑
> （分层命令菜单、图片缩略图选择、从选中行播放调试），见第十一章。

### 想写自己的新剧本文件？

1. 在 `Assets/Scenarios/` 下新建 `我的章节.vn.txt`（**必须以 .txt 结尾**，Unity 才认）
2. 选中场景里的 `VNScriptRunner` 物体，把新文件拖到 Inspector 的 **Script** 栏
3. 如果要用 `chapter` 命令在多个剧本文件间跳转，把所有相关文件都拖进 **Chapters** 列表
4. Play

两个官方示例：`Demo.vn.txt`（演出/分支/事件）、`RaisingDemo.vn.txt`（养成循环玩法）。

---

## 二、剧本文件基础规则

| 规则 | 说明 |
|---|---|
| 一行一条命令 | 从上往下顺序执行 |
| `#` 开头 = 注释 | 整行忽略，写备注用 |
| 空行忽略 | 随意留白排版 |
| 行尾 `@` = 异步 | 该演出**不等待完成**，立刻执行下一行（详见第七章） |
| `key:value` 参数 | 如 `at:left`、`with:DissolveGlow`，**冒号两边不能有空格** |
| 报错带行号 | Console 里所有剧本错误都标注"第 N 行"，直接定位 |

---

## 三、台词的写法

台词行是唯一"不以命令关键字开头"的行，格式：

```
说话者 [表情]: 台词内容
```

> 换表情自动带 0.25 秒**交叉溶解**（旧表情淡出到新表情），不再瞬间跳变。
> 时长可在 VNStage 的 `expressionCrossfade` 调整（0 = 关闭）。

四种写法：

```
亚里沙: 普通台词。名牌显示"亚里沙"，她会自动高亮、其他角色压暗。
亚里沙 微笑: 带表情切换的台词。说这句前立绘自动换成"微笑"表情。
旁白: 说话者不是注册角色时，名牌原样显示"旁白"，无人高亮。
: 冒号开头 = 无名牌旁白（画外音/字幕）。
```

- 全角 `：` 和半角 `:` 都可以
- 台词播放规则：打字机逐字浮现 → 玩家按键推进（打字中按键 = 立即显示全文）
- 每句台词自动记入回想（Backlog）
- 说话时立绘自动做口型动画；默认表情自动眨眼

**表情**来自角色定义资产里配置的表情列表（见第八章）。表情不存在时会告警并用默认表情。

---

## 四、演出命令详解

### bg — 切换背景

```
bg bg1
bg bg2 transition:Eyelid
```

| 参数 | 说明 |
|---|---|
| 第 1 个参数 | 背景 id（VNStage.backgrounds 列表里配置，见第八章） |
| `transition:` | 可选，切换时播放全屏转场（转场类型见下表） |

无转场 = 瞬间切换。切背景后**立绘会自动做色调匹配**（向新背景的平均色微微偏移）。
背景默认带 Ken Burns 缓慢漂移（永不静止），可用 `fx kenburns off` 关闭。

**全部转场类型**（`transition:` 和独立 `transition` 命令通用）：

| 名称 | 效果 | 适合场景 |
|---|---|---|
| `NoiseDissolve` | 噪声溶解，带金色辉光边缘 | 通用换场 |
| `Blinds` | 百叶窗横条 | 时间跳跃 |
| `Tiles` | 瓦片随机翻转，对角线推进 | 轻快换场 |
| `CircleWipe` | 圆形扩散 | 聚焦某人后展开 |
| `InkSpread` | 水墨晕染 | 和风/回忆 |
| `WhiteFlash` | HDR 爆闪（配合 Bloom 超亮一瞬） | 重大剧情节点 |
| `BokehOrbs` | 大光斑涌满屏幕 | 进入回忆 |
| `Eyelid` | 上下眼睑合拢再睁开（POV 眨眼） | 醒来/昏迷 |

### cg — 全屏 CG 显示 🖼️

```
cg 天台告白                       # 显示 CG（默认藏立绘 + 停环境特效）
cg 天台告白 transition:WhiteFlash # 带转场切入
cg 回忆1 chars:keep fx:keep       # 保留立绘 / 保留环境特效
cg off                            # 关闭 CG，恢复立绘
cg off transition:NoiseDissolve
```

- CG 素材放 `Assets/CG/`，**文件名 = 剧本里用的 id**，重建场景时生成器自动灌入
- 显示 CG 时默认整层隐藏立绘并暂停环境特效，`chars:keep` / `fx:keep` 按需保留
- 看过的 CG 自动记入**全局解锁存档**（独立于存档槽，为将来的 CG 鉴赏画廊准备）
- CG 状态随存档保存，读档自动恢复

### show — 角色登场

```
show 亚里沙
show 亚里沙 at:left with:DissolveGlow
show 小雪 at:right expr:微笑 with:FadeSlideUp @
```

| 参数 | 说明 |
|---|---|
| 第 1 个参数 | 角色 id（角色定义资产里配置） |
| `at:` | 站位：`left` / `center` / `right`，或直接写横坐标像素如 `at:-200` |
| `expr:` | 登场表情（默认用表情列表第一个） |
| `with:` | 出场演出预设（默认 DissolveGlow） |

**全部出场预设**：

| 名称 | 演出 | 适合 |
|---|---|---|
| `DissolveGlow` | 噪声溶解显形+辉光边缘+光环闪耀+星光爆发 | 首次登场（最华丽） |
| `FadeSlideUp` | 从下方轻盈滑入+淡入 | 日常切换 |
| `ScaleBounce` | 弹性缩放弹出+微闪白 | 俏皮/惊喜 |
| `ShineReveal` | 淡入后一道扫光掠过 | 优雅登场 |
| `FlashBloom` | 爆闪中显形+光环大闪耀 | 高潮/重要角色 |
| `AfterimageDash` | 高速冲入+三道冷色残影 | 战斗系/紧急登场 |

角色登场后自动带常驻"活图"效果：呼吸起伏、悬浮飘动、周期扫光、背后光环、脚下影子。
对已在场的角色再次 `show` = 换位置/表情并重播出场演出。

### hide — 角色退场

```
hide 亚里沙
hide 亚里沙 with:dissolve
```

`with:` 可选 `fade`（淡出下滑，默认）或 `dissolve`（化作光点消散）。退场后角色销毁。

### emote — 情绪演出动作

```
emote 小雪 Surprise
```

| 动作 | 演出 |
|---|---|
| `Surprise` | 惊讶：上跳+回弹落地 |
| `Angry` | 生气：横向抖动+红色发光脉冲 |
| `Shy` | 害羞：缩小下沉+粉色光晕 |
| `Dejected` | 沮丧：下沉+变暗+降饱和（**持续状态**） |
| `Recover` | 从沮丧恢复 |
| `Nod` | 点头：两次下沉回弹 |
| `HeadShake` | 摇头：左右小幅摆动 |

### camera — 镜头运镜

```
camera pushin 1.05 5 focus:亚里沙
camera snapzoom 1.12 focus:小雪
camera pan 小雪
camera dolly 1.3 3
camera reset
```

| 运镜 | 参数（都可省略） | 效果 |
|---|---|---|
| `pushin [倍率] [秒] [focus:角色]` | 默认 1.06 / 4 秒 | 缓推：慢慢放大，重要台词的压迫感 |
| `snapzoom [倍率] [focus:角色]` | 默认 1.12 | 急推：0.16 秒快速放大+轻震，惊愕瞬间 |
| `pan <角色或横坐标> [秒]` | 默认 1.2 秒 | 平移：视线引向另一位角色 |
| `dolly [倍率] [秒]` | 默认 1.3 / 3 秒 | 眩晕镜头：背景放大立绘不变，空间拉扯感 |
| `reset [秒]` | 默认 1 秒 | 镜头复位 |

`focus:角色id` = 镜头朝那个角色推近。运镜通常配 `@` 异步使用（边推边说话）。

### camseq / camto / camcut — 自由镜头路径 🎬

> 💡 不想手写数值？用可视化编辑器：**Tools → VN Effects → Camera Sequence Editor**
> ——迷你画布上点选/拖动路径点、拖进度条预览整条运镜、一键生成文本复制进剧本，
> 也能把已有的 camseq 文本粘贴进去反向载入继续调。

`camera` 的五个预设不够用时（任意点、多段路径、精确秒数），用路径镜头：

```
camseq                          # 多段路径块：整条路径连续流畅（不会走走停停）
> topright 1.8 0                # 路径点：目标点 [zoom] [时长]；时长 0 = 瞬切起手
> bottom 1.8 2.5                # 2.5 秒匀速移到画面下方（zoom 保持 1.8）
> middle 1.0 1.5                # 1.5 秒回原点并 zoom out

camto 亚里沙:head 1.6 0.8       # 单段直达：目标点 [zoom] [秒] [ease:名]
camcut topright 1.8             # 瞬切到镜头状态（"一开始就在那里"）
```

**目标点词汇表**（三种寻址方式）：

| 类型 | 写法 |
|---|---|
| 九宫格锚点 | `topleft` `top` `topright` `left` `middle`(=center) `right` `bottomleft` `bottom` `bottomright`；`origin`/`reset` = 中心 |
| 角色[:部位] | `亚里沙`（中心）、`亚里沙:head / chest / waist / feet / up / mid / down` |
| 裸坐标 | `300,200`（画布坐标，中心为原点，1920×1080） |

规则与技巧：

- **缓动**：单段默认 InOutSine；多段路径首段 InSine 缓起、中间 Linear 匀速、末段 OutSine 缓停
  ——整条像一次连续运镜。每个路径点可用 `ease:OutBack` 等覆盖（DOTween 全部 Ease 名可用）
- **防露边**：高倍 zoom 对准边角时自动钳制偏移，不会把画布边缘露出来
- 路径点在**执行时**才解析角色位置（角色刚 move 过也能对准）
- camseq 整块可加 `@` 异步（台词与运镜同时进行）
- 空行/注释不打断 `>` 路径点块

### shake — 屏幕震动

```
shake light      # 轻震：心跳、紧张
shake medium     # 中震：惊吓、撞击（默认）
shake heavy      # 强震：爆炸、冲击（带旋转抖动）
```

只有画面在震，对话框保持稳定。

### weather — 天气

```
weather Petals
weather None
```

`Petals` 落樱 / `Rain` 雨（带底部溅落）/ `Snow` 雪 / `Fireflies` 萤火虫 / `None` 关闭。
切天气自动带**调色联动**（下雨变冷灰、雪天清透、萤火虫之夜变暗）。

### mood — 场景色调（电影级调色）

```
mood Sunset
mood Neutral
```

| 预设 | 氛围 |
|---|---|
| `Neutral` | 原始画面 |
| `Morning` | 清晨：冷青偏亮 |
| `Sunset` | 黄昏：橙金 |
| `Night` | 夜晚：深蓝低饱和 |
| `Memory` | 回忆：褪色暖黄+胶片颗粒+暗角（**自动联动电影黑边 + 胶片滤镜**） |
| `Tension` | 紧张：高对比偏绿 |
| `Horror` | 恐怖：重度去饱和+强颗粒+深暗角 |
| `Dream` | 梦境：柔光泛白（**自动联动 CRT 滤镜**） |

任意两种色调之间都能平滑过渡（约 2 秒），同一张背景能演出完全不同的情绪。

### fx — 特效开关

```
fx godrays on
fx heartbeat on
fx focus 亚里沙
fx shockwave heavy
fx speedlines burst
```

| 名称 | 效果 |
|---|---|
| `godrays on/off` | 斜射光束（教室/树林/窗边的丁达尔光） |
| `dof on/off` | 伪景深：背景模糊+压暗，立绘"浮"出来（对话特写用） |
| `clouds on/off` | 云影缓慢横穿背景（晴天活气） |
| `skycloud on/off` | 云**本体**缓移（与云影互补，天空背景用） |
| `haze on/off` | 热浪扭曲+升腾蒸汽（夏日/温泉） |
| `shimmer on/off` | 水面波光（海边/湖边，画面下半部粼粼闪光） |
| `heartbeat on/off` | 心跳演出：画面脉动+粉色边缘泛光（告白/紧张） |
| `dutch on/off` | 荷兰角：画面倾斜 3°（不安/异常的心理暗示） |
| `focus 角色id` / `focus off` | 聚焦渐晕：暗角中心对准该角色（视线引导） |
| `speedlines on/off/burst` | 漫画速度线/集中线（burst = 一闪） |
| `shockwave [light\|heavy]` | 全屏情绪水波冲击（一次性演出：波峰环+背景脉冲+轻震） |
| `filmgrain on/off` | 胶片滤镜（颗粒+抖动，回忆/老电影） |
| `crt on/off` | CRT 滤镜（扫描线+色散，梦境/异常空间） |
| `kenburns on/off` | 背景 Ken Burns 缓慢漂移（**默认开启**） |
| `meteor on/off` | 夜晚偶发流星 |

### letterbox — 电影黑边

```
letterbox on
letterbox on height:150 time:1.2
letterbox off
```

上下黑边滑入/滑出，营造过场/回忆的电影感。`mood Memory` 会自动开启。

### portrait — 对话头像开关

```
portrait on      # 对话框左侧显示说话者头像（默认）
portrait off
```

### reset — 一键清空特效

```
reset effects
```

关闭所有环境特效/滤镜/镜头效果，回到干净画面（换场景前的"大扫除"）。

### sakura — 樱吹雪名场面

```
sakura
```

一行触发告白级演出：花瓣暴风横扫全屏 3 秒 + 心跳自动开启再关闭。

### move — 角色滑步换位

```
move 亚里沙 left          # 0.6 秒滑到左位
move 亚里沙 right 1.2     # 1.2 秒慢慢走过去
move 亚里沙 -200 0.4      # 滑到横坐标 -200
```

角色平滑滑到新站位（自动应用该角色的标定偏移、自动同步悬浮/动作基准位）。
两人对话时"走近一步"的演出必备。常配 `@` 边走边说。

### bgm / se / voice / volume — 音频 🎵

```
bgm play 黄昏之歌            # 播放/切换 BGM（自动交叉淡入淡出）
bgm play 战斗曲 fade:0.5 vol:0.8   # 指定淡化时长与本曲音量
bgm stop fade:3              # BGM 淡出停止

se 开门声                    # 一次性音效
se 雨声 loop vol:0.6         # 循环环境音（雨声/蝉鸣/人群）
se stop 雨声                 # 停止某个循环音（淡出）

voice 亚里沙_001             # 语音（新的自动顶掉旧的；自动驱动下一句台词口型）

volume bgm 0.5               # 通道音量 0~1（bgm / se / voice）
```

- **三通道独立音频库**：声音在场景 `VNAudio` 物体的 **bgmLibrary / seLibrary /
  voiceLibrary** 列表登记（id + AudioClip + 每条目基准音量标定），剧本用 id 引用
- 最终音量 = 条目基准音量 × 剧本 `vol:` × 通道音量（Config 面板可调通道音量）
- **打字音**：把一个短促音效拖到 `VNAudio.typingTick` 栏 → 打字机自动"哒哒哒"
  （带节流与随机音高）
- 当前 BGM 随存档保存，读档自动恢复
- 免费素材站：DOVA-SYNDROME、魔王魂、甘茶の音楽工房（BGM）、效果音ラボ（音效）

### wait — 分镜停顿

```
wait 0.6
```

停顿指定秒数再执行下一行。营造"呼吸感"的关键：重要台词前停一拍。

### transition — 独立全屏转场

```
transition WhiteFlash
```

不换背景、只播转场（如爆闪表示时间流逝）。类型同 bg 的转场表。

---

## 五、分支与变量

### label / jump — 标记与跳转

```
label 天台线
...剧情...
jump 结局汇合
jump 节日活动集::圣诞约会
```

- `label 名字` 只是位置标记，执行时无效果
- `jump 名字` 跳到该标记（**支持向前跳**，标记可以写在 jump 后面）
- `jump 文件::名字` 直接切换到另一个剧本的指定 label；`.vn.txt` 后缀可省略
- label 重名会报错
- 跨文件目标必须先拖进 `VNScriptRunner` 的 **Chapters** 列表。文件或 label 不存在时，
  本次跳转会报错且保持当前剧本位置不变
- `if ... jump 文件::label`、choice 的 `-> 文件::label`、event 结果的
  `-> 文件::label` 都使用同一种地址格式

### call / return — 可复用剧情片段

```
call 公共事件::获得奖励
: 奖励事件已经结束，现在继续执行主剧情。

# 公共事件.vn.txt
label 获得奖励
se sparkle
stat 金钱 +100
return
```

- `call 目标` 使用与 jump 完全相同的本地或 `文件::label` 地址，但会记住 call 后的下一条命令；
  子程序执行 `return` 后自动回去
- 支持嵌套调用，最多 64 层；超过上限会报错，防止无限递归
- `call` 与 `return` 都不能在行尾加 `@`
- 子程序必须显式执行 `return`。带着调用栈播放到文件末尾，或没有 call 就执行 return，都会报错并停止
- `jump`（包括跨文件 jump）会保留调用栈，因此可在子程序内部继续跳标签；`chapter` 表示彻底切换流程，
  会清空调用栈，之后不能返回旧位置
- 调用栈随普通存档和快速存档保存；在子程序台词处存档、退出游戏再读档，仍能返回正确位置。
  旧存档没有此字段时自动按空栈读取

### chapter — 跨文件章节跳转 📂

```
chapter 第二章            # 切到 第二章.vn.txt，从头开始执行
chapter 结局集.vn.txt     # 后缀可写可不写，大小写不敏感
```

- 目标文件必须先拖进 `VNScriptRunner` 的 **Chapters** 列表登记，否则报错
- flag / 属性 / 任务状态是全局的，切文件**全部保留**
- `chapter` 从目标文件**第一行**开始；想直达文件中间的某场戏，直接用限定跳转：

```
jump 十二月篇::圣诞约会
```

旧的“入口 Flag + `chapter` + 文件头路由”仍完全兼容，适合确实需要从文件头做统一初始化的文件；
仅仅为了选 label 时不再需要它。

### flag — 全局变量

```
flag 遇见过小雪          # 置为 1（当开关用）
flag 好感度 3            # 赋值为 3
flag 好感度 +1           # 加 1（-1 = 减 1）
flag 运气 rand:1-100     # 区间内随机取整（含两端）——随机事件/结果分级用
```

变量是整数，未设置过的变量值为 0。**变量会随存档保存**。
`flag` 是静默写入；想要带钳制和飘字演出的写入用 `stat`（见第六章）。

**rand 随机数的标准用法**（失败/普通/成功/大成功四档分级）：

```
flag 运气 rand:1-100
if 运气<=10 jump 打工_失败       # 10% 失败
if 运气<=80 jump 打工_普通       # 70% 普通
if 运气<=95 jump 打工_成功       # 15% 成功
jump 打工_大成功                  # 5% 大成功
```

想让**属性影响概率**（体力高→失败率低）：if 只能 jump，所以给高属性玩家
单独走一条阈值更宽松的判定链：

```
flag 运气 rand:1-100
if 体力>=100 jump 打工判定_熟练     # 高体力走另一组阈值（失败线 10 → 3）
if 运气<=10 jump 打工_失败
...
label 打工判定_熟练
if 运气<=3 jump 打工_失败
...
```

（注意：编辑器"重建前置状态"调试时 rand 会重新掷骰，重建出的分支可能与
实际游玩不同——与 event 结果不重放是同类限制。）

### if — 条件跳转

```
if 好感度>=2 jump 好结局
if 遇见过小雪 jump 相识线
if !遇见过小雪 jump 初见线
if 月份 == 12 && 好感度_小雪 >= 50 jump 圣诞约会
if (智力 + 魅力) / 2 >= 60 || 特殊通行证 jump 特别路线
```

格式固定：`if 条件表达式 jump 标签`。独立 `if` 的表达式可以带空格。支持：

| 写法 | 含义 |
|---|---|
| `名字` | 非 0 即真 |
| `!名字` | 等于 0 为真 |
| `名字>=2` `名字<=1` `名字==3` `名字!=0` `名字>1` `名字<5` | 数值比较 |
| `a + b` `a - b` `a * b` `a / b` `a % b` | 整数算术 |
| `条件A && 条件B` | 两边都成立 |
| `条件A \|\| 条件B` | 任一边成立 |
| `(表达式)` | 用括号改变计算顺序 |

优先级从高到低为：括号、`!`/正负号、`* / %`、`+ -`、比较、`&&`、`||`。
`&&` 和 `||` 会短路求值；缺少的 Flag 仍按 0 处理。整数除以 0、溢出或语法错误时，
本次条件视为 false，并在 Console 标出剧本行号与表达式列号。

> 属性、月份、任务状态、道具数量本质都是 flag，全部可以直接 if：
> `if 月份==12`、`if 智力>=80`、`if 任务_告白大作战==100`、`if 道具_发饰>=1`。

### choice — 选项

```
choice
* 鼓起勇气说出来 flag:好感度+2 -> 告白线
* 请她喝咖啡 cost:金钱-100 -> 咖啡厅
* 参加歌唱大赛 if:魅力>=60 -> 歌唱大赛
* 沉默不语
```

- `choice` 单独一行，下面用 `*` 开头列出选项（空行、注释不打断选项块）
- 选项完整格式：`* 显示文本 [if:条件] [cost:花费] [flag:变量操作] [-> 跳转标签]`

| 参数 | 说明 |
|---|---|
| `if:条件` | 条件不满足时**该选项直接隐藏**（隐藏选项/属性解锁选项） |
| `cost:金钱-100` | 显示花费；**付不起时选项置灰不可选**；选中自动扣除 |
| `flag:胆小+1` | 选中时顺手改变量 |
| `-> 标签` | 选中后跳转；**不写 = 顺序继续**执行 choice 块后的下一行 |

- 选项演出自动带：错落飞入、悬停扫光、选中闪光、落选溶解消散
- 快进模式到选项会强制停下，玩家必须亲自选
- 选项行的 `if:` 仍是一个以空格分隔的参数，所以复合表达式要写成无空格形式：
  `if:(月份==12&&好感度_小雪>=50)`。需要易读的带空格条件时，先用独立 `if` 跳到不同选项块。

**多结局的标准套路**：

```
choice
* 选项A flag:路线 1 -> 汇合
* 选项B flag:路线 2 -> 汇合

label 汇合
...共通剧情...
if 路线==1 jump 结局A
jump 结局B
```

---

## 六、玩法系统命令

### event — 玩法事件（小游戏接口）🎮

```
event qte time:3 target:12 title:鼓起勇气连打！
* success flag:好感度+2 -> 告白成功
* fail -> 退缩线
```

- `event <模块id> [key:value…]` 调起注册在 VNEventRegistry 的玩法模块，
  紧跟的 `*` 结果行按模块返回的**结果名**分支（写法同 choice 选项，
  支持 `flag:` 与 `->`）；整数型结果还会写入 flag `事件结果`
- 事件进行中：快捷键全部禁用、不可存档；事件结束回到剧本流程

**内置模块**：

| 模块 | 用法 | 说明 |
|---|---|---|
| `qte` | `event qte time:3 target:12 title:标题` | 限时连打条，结果 `success` / `fail` |
| `map` | `event map …` | 地图选地点：按钮条件显隐，去过的地点自动写 flag `去过_<地点>`，结果名 = 选中的地点 |
| `shop` | `event shop id:服装店` | 商店购物（见下） |
| `plan` | `event plan slots:7 pool:打工,学习` | 周日程排程面板（见下） |
| `result` | `event result grade:great title:剑术训练` | GOOD!/COMPLETE! 结算大弹窗（见下） |

新玩法（战斗/钓鱼/番长镇日程……）就是照 VNQteModule 的样子再写一个模块类，
接口/铁律见 `ProjectCodeGuide.md`。

### 商店与物品栏 🛒

```
event shop id:服装店
if 道具_蝴蝶结发饰>=1 jump 买到了
```

- 商店内容配置在 **VNShopDef 资产**（`Assets/VNEffects/Shops/`，右键
  **Create → VN → Shop Definition**）：商品 id/名字/图标/买价/卖价
- 买卖走 `金钱` 属性；买到的道具 = flag `道具_<id>`（计数），if 可直接判断
- 玩家按 **I** 打开物品栏（文案图标自动取自 VNShopDef）

### 周日程排程：plan + result 📆

《火山的女儿》式玩法：**排好一周日程 → 逐格执行 → 随机分级结果 → 加属性**。
由两个事件模块 + `flag rand:` 随机数组合而成，循环骨架写在剧本里。

**① 排程面板**

```
event plan slots:7 pool:打工,学习,剑术训练,休息 title:安排这一周
```

| 参数 | 说明 |
|---|---|
| `slots:` | 格子数（默认 7，上限 14） |
| `pool:` | 本次开放的行动 id，逗号分隔（省略 = 用方案资产里的全部行动） |
| `id:` | 日程方案资产 id（VNPlanDef，省略 = 用模板登记的第一个） |
| `title:` | 面板标题（省略用资产标题 / 默认文案） |

左列点行动 → 填入下一个空格；点右侧格子 → 清空该格；「重置」清空全部。
确定后写入 flag：`日程_1`…`日程_N` = 行动编号，`日程数` = N，`当前格` 归零。
**留空的格子写 0**（剧本把 0 当休息/跳过处理即可）。

行动详情（编号/显示名/图标/预期收益文案/上架条件）配置在 **VNPlanDef 资产**
（`Assets/VNEffects/Plans/`，右键 **Create → VN → Plan Definition**）。
没有资产也能跑：`pool:` 里的名字按出现顺序编号 1、2、3…（只是没图标和收益文案）。

**② 逐格派发**（无 UI、秒回，专门给执行循环用）

```
label 执行日程
event plan op:next
* next
* end -> 周末结算
if 当前行动==1 jump 行动_打工
if 当前行动==2 jump 行动_学习
jump 行动_休息
```

`op:next` 把 `当前格` +1，并把该格的行动编号抄进 flag `当前行动`；
超出 `日程数` 时返回结果 `end`。每个行动分支末尾 `jump 执行日程` 即成循环。

**③ 随机分级**（用 `flag rand:`，见第五章）

```
label 行动_打工
flag 运气 rand:1-100
if 运气<=10 jump 打工_失败
if 运气<=70 jump 打工_普通
if 运气<=95 jump 打工_成功
jump 打工_大成功
```

**④ 结算弹窗**

```
event result grade:great title:剑术训练 sub:领悟了新的剑技！ se:成功音效
stat 体力 +30
stat 压力 +8
```

| 参数 | 说明 |
|---|---|
| `grade:` | `fail` / `normal` / `good` / `great`——决定大字、配色、是否撒星光 |
| `title:` | 行动名（大字下方） |
| `sub:` | 一句补充说明（可省略） |
| `se:` | 同时播放的音效 id（可省略） |

点击/回车/空格关闭。结果名 = grade 值，可用 `* great -> 标签` 接住（通常不用）。
属性增减仍由弹窗后的 `stat` 命令负责——VNStatsHud 的 +N 飘字就是截图里
左侧那排增益条的效果。

> 完整可跑示例：`Assets/Scenarios/WeekPlanDemo.vn.txt`（含"体力高→失败率低"
> 的属性影响概率写法）。想接成月度大循环，在周末结算后加 `time pass`。

### quest — 任务系统 📜

```
quest start 告白大作战        # 接取 → Toast「新任务」
quest stage 告白大作战 2      # 推进到第 2 阶段
quest done 告白大作战         # 完成
quest fail 告白大作战         # 失败
```

- 任务状态存在 flag `任务_<id>`：`0` 未接取 / `1..n` 进行中阶段 /
  `100` 完成 / `-1` 失败——if 可直接判断
- 玩家按 **J** 打开任务日志面板
- 任务的标题/描述/各阶段文案配置在 **VNQuestDef 资产**（右键
  **Create → VN → Quest Definition**）；**没有资产也能运作**（id 当标题）

### stat — 养成属性 📈

```
stat 金钱 500        # 赋值
stat 体力 +20        # 增加（带绿色飘字）
stat 压力 -10        # 减少（带飘字）
```

- `stat` = **带演出的 flag 写入**：按 VNStatDef 钳制上下限 + HUD 飘字；
  `flag` 命令则始终静默（幕后计数用 flag，玩家可见成长用 stat）
- 属性定义配置在 **VNStatDef 资产**（`Assets/VNEffects/Stats/`，右键
  **Create → VN → Stat Definition**）：钳制范围、HUD 样式、等级阈值（E/D/C/B/A…）
- **没有定义资产也能用**：`stat 社交度 +5` 直接生效（只是不钳制、不上 HUD）
  ——新属性/各角色好感度（如 `好感度_小雪`）零代码即可添加
- 顶栏 HUD 显示登记过的属性；玩家按 **C** 打开完整属性面板
- 数值全在 flags：if 分支、存档、调试重建全部自动可用

### time — 日程与日历 📅

```
time set 9 remain:36     # 进入养成模式：9 月开始，共 36 个月
time pass                # 过一个月：月份+1（12 后回到 1）、剩余月数-1、行动力回满
time pass months:3       # 一次过 3 个月
time pass refill:off     # 过月但不回满行动力
time pass refill:体力    # 回满的属性换成"体力"
```

- 状态全在 flag：`月份` / `剩余月数`——`if 月份==12 jump 圣诞活动` 直接可用
- 右下角自动出现**日历 HUD**（`月份` flag 不存在时自动隐藏，纯 Gal 剧本零干扰）
- 养成 Game Loop 的完整套路（月初→行动 choice→月末 time pass→结局判定）
  见 `RaisingDemo.vn.txt` 与 `GeneralQuestionGuide.md` 问题二

---

## 七、演出 timing 控制

### 默认：同步执行

每条命令**等演出播完**才执行下一行。`show` 等出场动画结束、`bg` 等转场结束、`emote` 等动作结束。

### 行尾 @：异步执行

加 `@` 的命令**立即放行**，演出在后台继续。这是"演出编排"的核心：

```
camera pushin 1.05 5 focus:亚里沙 @    # 镜头开始缓推（5 秒）
亚里沙: 那个……小雪。                    # 台词同时进行，不用等镜头
亚里沙: 我一直有话想对你说。              # 说这句时镜头还在推
```

对比（无 @）：

```
camera pushin 1.05 5      # 卡住 5 秒，镜头推完才出台词 —— 通常不是你想要的
亚里沙: 那个……
```

**经验法则**：
- 运镜、长转场、背景 fx → 几乎总是加 `@`
- 出场/退场/情绪动作 → 想让玩家看完整就不加，想紧凑就加
- `wait` 用来手动补节奏：`show 小雪 @` + `wait 0.3` = 出场到一半时下一件事发生

---

## 八、资产管理

### 角色（VNCharacterDef 资产）

位置：`Assets/VNEffects/Characters/*.asset`。新建：Project 窗口右键 → **Create → VN → Character Definition**。

| 字段 | 说明 |
|---|---|
| `id` | 剧本里引用的名字（可中文，如 `亚里沙`）——**逻辑标识符，永不翻译** |
| `displayName` | 对话框名牌显示的名字（En/Ja 字段填其他语言显示名） |
| `nameColor` | 名牌底色（每个角色一个专属色） |
| `expressions` | **表情列表**：表情名 + 立绘图。第一个 = 默认表情 |
| `sizeScale` | **尺寸倍率**：该角色显示高度 = 舞台统一高度 × 此值（默认 1） |
| `positionOffset` | **站位偏移**（像素）：在 at:left/center/right 标准位置上的附加偏移 |

### 素材构图不统一怎么办（sizeScale / positionOffset 标定）

不同来源的立绘构图往往不一致：有的角色占满画面、有的四周留白多、有的是半身近景。
系统按统一高度缩放后它们的**视觉大小和站位就会不一致**。解法是给每个角色做一次标定，
之后剧本里继续统一写 `at:left / at:right`，差异由资产吸收：

1. Play 进游戏，观察哪个角色显大/显小、偏高/偏低
2. 选中该角色的 `.asset`，调整：
   - 角色**显小**（图里留白多）→ `sizeScale` 调大，如 `1.15`
   - 角色**显大**（近景/半身图）→ `sizeScale` 调小，如 `0.85`
   - 角色**偏高**（脚下留白多）→ `positionOffset.y` 给负值往下压，如 `(0, -40)`
   - 构图重心偏左/偏右 → 用 `positionOffset.x` 修正
3. 重新 Play 对比，两三轮就能对齐

标定是**每角色一次性的**，调好后所有剧本、所有站位命令都自动一致；
表情图之间宽高比不同也没关系（高度恒定、宽度按图自适应）。

**给角色加表情**：选中资产 → expressions 列表点 + → 填表情名（如 `微笑`）+ 拖入对应立绘图。
然后剧本里就能用：`show 亚里沙 expr:微笑` 或 `亚里沙 微笑: 台词`。

**新角色**：创建资产后，把它拖进场景 `VNStage` 的 **Characters** 列表即可在剧本中使用。

### 背景（VNStage.backgrounds 列表）

选中场景里的 `VNStage` 物体 → Inspector 的 **Backgrounds** 列表：每项 = `id`（剧本里用的名字）+ Sprite。
生成场景时会自动把 `Assets/Assets` 下的大图填成 `bg1`、`bg2`…，**建议改成有意义的名字**（如 `教室`、`天台黄昏`），剧本可读性更好：

```
bg 天台黄昏 transition:Eyelid
```

### CG（Assets/CG/ 目录）

CG 大图放 `Assets/CG/`，**文件名就是剧本里的 id**（如 `天台告白.png` →
`cg 天台告白`）。重建场景时生成器自动灌入 `VNStage.cgLibrary`。

### 玩法定义资产一览

| 资产 | 位置 | 用途 |
|---|---|---|
| VNCharacterDef | `Assets/VNEffects/Characters/` | 角色：名牌/表情→立绘 |
| VNStatDef | `Assets/VNEffects/Stats/` | 属性：钳制/HUD 样式/等级阈值 |
| VNShopDef | `Assets/VNEffects/Shops/` | 商店：商品/价格/图标 |
| VNPlanDef | `Assets/VNEffects/Plans/` | 日程方案：候选行动/编号/收益文案 |
| VNQuestDef | `Assets/VNEffects/Quests/`（可选） | 任务文案 |

### 图片导入要求

新素材放进 `Assets/Assets`（或任意位置），确保 Texture Type = **Sprite (2D and UI)**。
用生成器生成过场景的图都已自动设置好。

---

## 九、玩家操作

| 按键 | 功能 |
|---|---|
| `Enter` / `空格` / `鼠标左键` | 推进剧情（打字中按下 = 立即显示全文） |
| `H` 或 滚轮上滑 | **回想**（Backlog）；H/Esc/点背景关闭 |
| `A` | **自动模式**开关（打完字自动等待后推进） |
| `S` | **快进**开关（所有演出 4 倍速+台词秒过；到选项/事件强制停） |
| `F5` | 打开**存档界面**（20 槽，带截图缩略图/时间/末句台词） |
| `F9` | 打开**读档界面** |
| `Q` | 快速存档（存到快速槽） |
| `L` | 快速读档 |
| `J` | **任务日志**面板 |
| `C` | **属性**面板 |
| `I` | **物品栏** |
| `鼠标右键` | 隐藏全部 UI 看立绘/CG（U/Enter/空格/点击恢复） |
| `Esc` | 关闭当前面板 / 打开 Config |

- 对话框下方有**快捷功能条**：Save / Load / Auto / Skip / Log / 任务 / Config / 隐藏 UI
- **Config 面板**：各通道音量、文字速度、显示模式等（PlayerPrefs 持久化）
- 只有**停在台词上**时才能存档（演出/事件进行中不可存）；flag、属性、任务、
  日历、CG、BGM、舞台状态全部入档，读档瞬间摆台恢复现场
- 存档文件在 `persistentDataPath/`（Windows: `%userprofile%\AppData\LocalLow\<公司名>\<项目名>\`）

代码调用（做 UI 按钮时用）：`runner.SaveTo(槽位)` / `runner.LoadFrom(槽位)` /
`runner.SetAuto(true)` / `runner.SetSkip(true)` / `backlog.Open()`。

---

## 十、本地化

剧本**只写中文**（唯一真相），翻译放旁路表，运行时按当前语言查表显示：

1. 写完/改完剧本后跑 **Tools → VN Effects → Localization → Extract**
   ——增量抽取台词到 `Resources/VNLocale/Scenarios/<剧本名>.<lang>.txt`（已译的保留）
2. 在表里填英/日译文，跑 **Validate** 检查缺译
3. UI 字符串表在 `Resources/VNLocale/ui.<code>.txt`

规则：choice 选项按索引匹配翻译；**event 结果行、角色 id、flag 名永远不翻译**
（它们是逻辑标识符）；名牌/任务/地图的显示名在各资产的 En/Ja 字段填。

---

## 十一、剧本可视化编辑器

**Tools → VN Effects → Scenario Editor**——不想手写文本时的图形界面：

- `.vn.txt` ↔ 编辑器双向同步，**文本仍是唯一真相**（注释/空行保留）
- 分层命令菜单（Dialogue / Scene / Character / Camera / FX / Audio / Flow），
  关键字英文 + 中文说明；transition/emote 等枚举中英对照
- `bg` / `say` / `show` 带 Sprite 缩略图浏览器；行首有舞台一览小格
- Shift/Ctrl 多选行，批量移动/删除/复制
- 音频行内 ▶ 试听
- **▶ 从选中行播放**：选中任意一行直接进 Play 调试，默认"重建前置状态"
  （自动按文件顺序汇总背景/站位/flag/BGM 等摆好舞台）——改剧本不用每次从头跑

---

## 十二、完整示例剧本

演出/分支/事件的综合示例见 `Assets/Scenarios/Demo.vn.txt`；
**养成循环**（属性/花费选项/商店/日程/多结局）见 `Assets/Scenarios/RaisingDemo.vn.txt`。

以下小剧本覆盖常用演出功能（可直接复制到 .vn.txt 使用）：

```
# ===== 第二章：雨夜告白 =====

bg 教室 transition:NoiseDissolve
mood Night
weather Rain
fx godrays off

: 放学后的教室，雨声敲打着窗户。

show 亚里沙 at:left with:FadeSlideUp
亚里沙: 雨下得好大……今天回不去了呢。

# 神秘登场：先急推镜头再爆闪出场
camera snapzoom 1.1 @
show 小雪 at:right with:FlashBloom
小雪: 果然你还在这里。

fx dof on                        # 背景虚化，进入对话特写
camera pushin 1.05 6 focus:小雪 @
小雪: 我有话要问你。上周的事……

choice
* 坦白一切 flag:诚实+1 -> 坦白线
* 装傻糊弄过去 -> 装傻线

label 坦白线
fx heartbeat on
emote 亚里沙 Shy
亚里沙: 对不起……其实那天，我是去给你买生日礼物了。
emote 小雪 Surprise
wait 0.8
sakura
小雪 :（眼眶发红）……笨蛋。我还以为你讨厌我了。
flag 好感度 +2
jump 汇合

label 装傻线
emote 亚里沙 HeadShake
亚里沙: 什、什么事？我完全不记得了～
emote 小雪 Angry
shake medium
小雪: 你这家伙……！算了！
flag 好感度 -1
jump 汇合

label 汇合
fx heartbeat off
fx dof off
camera reset @
bg 夜晚街道 transition:Eyelid
weather None
mood Memory

if 好感度>=2 jump 甜蜜结尾
旁白: 两人沉默地走在回家的路上。
jump 章节完

label 甜蜜结尾
weather Fireflies
旁白: 雨停了。两人共撑一把伞，影子在路灯下靠得很近。

label 章节完
hide 小雪 with:fade
hide 亚里沙 with:fade
transition WhiteFlash
: 第二章　完
chapter 第三章        # 跨文件接续（第三章.vn.txt 需登记进 Chapters 列表）
```

---

## 十三、常见问题排查

| Console 报错 / 现象 | 原因与解决 |
|---|---|
| `第 N 行：未注册的角色「xxx」` | 角色 id 打错，或没把 VNCharacterDef 拖进 VNStage.characters |
| `第 N 行：未注册的背景「xxx」` | 背景 id 打错，或 VNStage.backgrounds 里没这项 |
| `第 N 行：跳转目标 label 不存在` | jump/choice 的目标 label 名打错了 |
| `第 N 行：找不到章节「xxx」` | 目标剧本没拖进 VNScriptRunner 的 Chapters 列表 |
| `第 N 行：无法识别的 XXX「yyy」` | 枚举名拼错（如 `transition:eyelid` 大小写没关系，但拼写要对） |
| `没有表情「xxx」，使用默认表情` | 去角色资产的 expressions 列表加该表情 |
| 选项行被当台词显示 | `*` 前面有 choice / event 命令吗？其它命令会打断选项块 |
| if 不生效 | 看 Console 的行号/列号；检查括号、运算符、除数是否为 0，以及 `jump` 和标签是否完整 |
| choice 所有选项都不见了 | 全部选项的 `if:` 条件都不满足——留一个无条件的保底选项 |
| cost 选项永远是灰的 | `金钱` 属性没初始化？先 `stat 金钱 500` |
| 台词被当成命令 | 台词开头恰好是保留字（bg/show/wait 等）？在前面加说话者或 `：` |
| 演出进行中不能存档 | 正常设计——推进到下一句台词停稳后再按 F5 |
| 事件里按快捷键没反应 | 正常设计——事件模块进行中输入全交给模块 |
| 日历 HUD 不显示 | 还没执行过 `time set`（`月份` flag 不存在时自动隐藏） |
| `event plan 没有任何可用行动` | 没配 VNPlanDef 资产也没写 `pool:`；或全部行动的 `condition` 都不满足 |
| `日程方案里没有行动「xx」` | `pool:` 里的 id 与 VNPlanDef 的行动 id 对不上（id 不翻译，注意别写显示名） |
| `event plan op:next` 立刻返回 end | `日程数` flag 为 0——先跑过排程面板（或手动 `flag 日程数 7`） |
| 执行循环卡住不动 | 行动分支末尾忘了 `jump 执行日程` |
| 改了剧本没生效 | Unity 需要焦点回到编辑器让它重新导入 .txt；然后重新 Play |
| 新加的字段/功能报"未连线" | VNStage 会自动补线；仍报错就重新 Tools → Create Script Demo Scene |
| 切语言后台词没翻译 | 跑过 Extract 了吗？表里该句填了吗？跑 Validate 查缺译 |

---

## 附：命令速查卡

```
── 演出 ──
bg <背景> [transition:类型]                      切背景
cg <id> [transition:] [chars:keep] [fx:keep]     全屏CG / cg off 关闭
show <角色> [at:位置] [expr:表情] [with:预设]     登场
hide <角色> [with:dissolve|fade]                 退场
emote <角色> <动作>                              情绪动作
<角色> [表情]: 台词                              说话
move <角色> <位置> [秒]                          滑步换位
wait <秒>                                        停顿
camera <pushin|snapzoom|pan|dolly|reset> [...]   预设运镜
camseq + > 目标点 [zoom] [秒] [ease:名]          多段镜头路径
camto <目标点> [zoom] [秒] / camcut <目标点> [zoom]  单段直达/瞬切
shake <light|medium|heavy>                       震动
weather <Petals|Rain|Snow|Fireflies|None>        天气
mood <Neutral|Morning|Sunset|Night|Memory|Tension|Horror|Dream>  色调
fx <名称> <on|off|burst|light|heavy> / fx focus <角色|off>   特效
letterbox <on|off> [height:] [time:]             电影黑边
portrait <on|off>                                对话头像
reset effects                                    清空全部特效
sakura                                           樱吹雪
transition <类型>                                独立转场

── 音频 ──
bgm play <id> [fade:秒] [vol:] / bgm stop        背景音乐
se <id> [loop] [vol:] / se stop <id>             音效/环境音
voice <id>                                       语音
volume <bgm|se|voice> <0~1>                      通道音量

── 流程 ──
label <名> / jump <名|文件::名>                  本文件/跨文件指定标签跳转
call <名|文件::名> / return                      调用公共片段并返回（栈随存档保存）
chapter <剧本文件名>                             跨文件章节跳转
flag <名> [数值|+n|-n] [rand:min-max]            变量（静默；rand=区间随机）
if <表达式> jump <名>                            条件跳转（!、算术、比较、&&、||、括号）
choice + * 文本 [if:条件] [cost:花费] [flag:op] [-> 标签]   选项

── 玩法 ──
event <模块> [key:value…] + * 结果 [flag:op] [-> 标签]      小游戏事件
event plan slots:7 pool:… / event plan op:next   周日程排程 / 逐格派发
event result grade:<fail|normal|good|great> [title:] [sub:] [se:]  结算弹窗
quest <start|stage|done|fail> <id> [阶段]        任务
stat <名> <+n|-n|值>                             属性（钳制+飘字）
time set <月> [remain:N] / time pass [months:N] [refill:]   日程

行尾 @                                           异步不等待
```
