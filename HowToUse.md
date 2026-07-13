# HowToUse.md — 视觉小说剧本系统使用教程

> 本教程教你如何用纯文本剧本制作视觉小说的完整剧情演出。
> 对应实现：`Assets/Scripts/VNEffects/Script/`（P0 核心 + P1 分支 + P2 存档）

---

## 目录

1. [三分钟上手](#一三分钟上手)
2. [剧本文件基础规则](#二剧本文件基础规则)
3. [台词的写法](#三台词的写法)
4. [全部命令详解](#四全部命令详解)
5. [分支与变量（选项/多结局）](#五分支与变量)
6. [演出 timing 控制（@ 与 wait）](#六演出-timing-控制)
7. [角色与背景的资产管理](#七角色与背景的资产管理)
8. [玩家操作（推进/回想/自动/快进/存档）](#八玩家操作)
9. [完整示例剧本](#九完整示例剧本)
10. [常见问题排查](#十常见问题排查)

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

### 想写自己的新剧本文件？

1. 在 `Assets/Scenarios/` 下新建 `我的章节.vn.txt`（**必须以 .txt 结尾**，Unity 才认）
2. 选中场景里的 `VNScriptRunner` 物体，把新文件拖到 Inspector 的 **Script** 栏
3. Play

---

## 二、剧本文件基础规则

| 规则 | 说明 |
|---|---|
| 一行一条命令 | 从上往下顺序执行 |
| `#` 开头 = 注释 | 整行忽略，写备注用 |
| 空行忽略 | 随意留白排版 |
| 行尾 `@` = 异步 | 该演出**不等待完成**，立刻执行下一行（详见第六章） |
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

**表情**来自角色定义资产里配置的表情列表（见第七章）。表情不存在时会告警并用默认表情。

---

## 四、全部命令详解

### bg — 切换背景

```
bg bg1
bg bg2 transition:Eyelid
```

| 参数 | 说明 |
|---|---|
| 第 1 个参数 | 背景 id（VNStage.backgrounds 列表里配置，见第七章） |
| `transition:` | 可选，切换时播放全屏转场（转场类型见下表） |

无转场 = 瞬间切换。切背景后**立绘会自动做色调匹配**（向新背景的平均色微微偏移）。

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

三个典型范例：

```
# 从角色下半身推上去到头部特写，再回原点
camseq
> 小雪:waist 1.7 0.6
> 小雪:head 1.7 1.2
> middle 1.0 0.8

# 背景两点间 0.45 秒快速扫过
camseq
> 350,180 2.0 0.15
> -420,-120 2.0 0.15
> middle 1.0 0.15

# 边运镜边说话（异步）
camseq @
> topright 1.6 0
> bottomleft 1.6 3
> middle 1.0 1.5
亚里沙: 这间教室……好怀念啊。
```

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
| `Memory` | 回忆：褪色暖黄+胶片颗粒+暗角 |
| `Tension` | 紧张：高对比偏绿 |
| `Horror` | 恐怖：重度去饱和+强颗粒+深暗角 |

任意两种色调之间都能平滑过渡（约 2 秒），同一张背景能演出完全不同的情绪。

### fx — 特效开关

```
fx godrays on
fx heartbeat on
fx dof off
fx focus 亚里沙
fx focus off
```

| 名称 | on/off 效果 |
|---|---|
| `godrays` | 斜射光束（教室/树林/窗边的丁达尔光） |
| `dof` | 伪景深：背景模糊+压暗，立绘"浮"出来（对话特写用） |
| `clouds` | 云影缓慢横穿背景（晴天活气） |
| `haze` | 热浪扭曲+升腾蒸汽（夏日/温泉） |
| `shimmer` | 水面波光（海边/湖边，画面下半部粼粼闪光） |
| `heartbeat` | 心跳演出：画面脉动+粉色边缘泛光（告白/紧张） |
| `dutch` | 荷兰角：画面倾斜 3°（不安/异常的心理暗示） |
| `focus 角色id` / `focus off` | 聚焦渐晕：暗角中心对准该角色（视线引导） |

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
bgm play 战斗曲 fade:0.5     # 指定淡化时长
bgm stop fade:3              # BGM 淡出停止

se 开门声                    # 一次性音效
se 雨声 loop                 # 循环环境音（雨声/蝉鸣/人群）
se stop 雨声                 # 停止某个循环音（淡出）

voice 亚里沙_001             # 语音（新的自动顶掉旧的，配音用）

volume bgm 0.5               # 通道音量 0~1（bgm / se / voice）
```

- **音频库**：所有声音先在场景 `VNAudio` 物体的 **Library** 列表登记（id + AudioClip），
  剧本用 id 引用。音频文件（.mp3/.ogg/.wav）放进 Assets 任意目录即可拖入
- **打字音**：把一个短促音效拖到 `VNAudio.typingTick` 栏 → 打字机自动"哒哒哒"
  （带节流与随机音高）
- 当前 BGM 随存档保存，读档自动恢复；循环 SE 暂不入档
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
```

- `label 名字` 只是位置标记，执行时无效果
- `jump 名字` 跳到该标记（**支持向前跳**，标记可以写在 jump 后面）
- label 重名会报错

### flag — 全局变量

```
flag 遇见过小雪          # 置为 1（当开关用）
flag 好感度 3            # 赋值为 3
flag 好感度 +1           # 加 1（-1 = 减 1）
```

变量是整数，未设置过的变量值为 0。**变量会随存档保存**。

### if — 条件跳转

```
if 好感度>=2 jump 好结局
if 遇见过小雪 jump 相识线
if !遇见过小雪 jump 初见线
```

格式固定：`if 条件 jump 标签`。**条件里不能有空格**。支持：

| 写法 | 含义 |
|---|---|
| `名字` | 非 0 即真 |
| `!名字` | 等于 0 为真 |
| `名字>=2` `名字<=1` `名字==3` `名字!=0` `名字>1` `名字<5` | 数值比较 |

### choice — 选项

```
choice
* 鼓起勇气说出来 flag:好感度+2 -> 告白线
* 还是算了…… -> 退缩线
* 沉默不语
```

- `choice` 单独一行，下面用 `*` 开头列出选项（空行、注释不打断选项块）
- 选项格式：`* 显示文本 [flag:变量操作] [-> 跳转标签]`
  - `flag:` 可选：选中时顺手改变量（如 `flag:胆小+1`）
  - `->` 可选：选中后跳转；**不写 = 顺序继续**执行 choice 块后的下一行
- 选项演出自动带：错落飞入、悬停扫光、选中闪光、落选溶解消散
- 快进模式到选项会强制停下，玩家必须亲自选

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

## 六、演出 timing 控制

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

## 七、角色与背景的资产管理

### 角色（VNCharacterDef 资产）

位置：`Assets/VNEffects/Characters/*.asset`。新建：Project 窗口右键 → **Create → VN → Character Definition**。

| 字段 | 说明 |
|---|---|
| `id` | 剧本里引用的名字（可中文，如 `亚里沙`） |
| `displayName` | 对话框名牌显示的名字 |
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

### 图片导入要求

新素材放进 `Assets/Assets`（或任意位置），确保 Texture Type = **Sprite (2D and UI)**。
用生成器生成过场景的图都已自动设置好。

---

## 八、玩家操作

| 按键 | 功能 |
|---|---|
| `Enter` / `空格` / `鼠标左键` | 推进剧情（打字中按下 = 立即显示全文） |
| `H` 或 滚轮上滑 | 打开**回想**（Backlog）；H/Esc/点背景关闭 |
| `A` | **自动模式**开关（打完字自动等待后推进，右上角显示 AUTO ▶） |
| `S` | **快进**开关（所有演出 4 倍速+台词秒过；到选项强制停；手动点击退出；右上角 SKIP ▶▶） |
| `F5` | 快速存档（只能停在台词上时存；存 flag+进度+舞台状态） |
| `F9` | 快速读档（瞬间摆台恢复现场，从存档那句继续） |

存档文件在 `persistentDataPath/vn_save_1.json`（Windows: `%userprofile%\AppData\LocalLow\<公司名>\<项目名>\`）。

代码调用（做 UI 按钮时用）：`runner.SaveTo(槽位)` / `runner.LoadFrom(槽位)` / `runner.SetAuto(true)` / `runner.SetSkip(true)` / `backlog.Open()`。

---

## 九、完整示例剧本

一个覆盖所有功能的小剧本（可直接复制到 .vn.txt 使用）：

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
```

---

## 十、常见问题排查

| Console 报错 / 现象 | 原因与解决 |
|---|---|
| `第 N 行：未注册的角色「xxx」` | 角色 id 打错，或没把 VNCharacterDef 拖进 VNStage.characters |
| `第 N 行：未注册的背景「xxx」` | 背景 id 打错，或 VNStage.backgrounds 里没这项 |
| `第 N 行：跳转目标 label 不存在` | jump/choice 的目标 label 名打错了 |
| `第 N 行：无法识别的 XXX「yyy」` | 枚举名拼错（如 `transition:eyelid` 大小写没关系，但拼写要对） |
| `没有表情「xxx」，使用默认表情` | 去角色资产的 expressions 列表加该表情 |
| 选项行被当台词显示 | `*` 前面有 choice 命令吗？其它命令会打断选项块 |
| if 不生效 | 条件里有空格？`if 好感度 >= 2` ✗ → `if 好感度>=2` ✓ |
| 台词被当成命令 | 台词开头恰好是保留字（bg/show/wait 等）？在前面加说话者或 `：` |
| 演出进行中不能存档 | 正常设计——推进到下一句台词停稳后再按 F5 |
| 改了剧本没生效 | Unity 需要焦点回到编辑器让它重新导入 .txt；然后重新 Play |
| 新加的字段/功能报"未连线" | VNStage 会自动补线；仍报错就重新 Tools → Create Script Demo Scene |

---

## 附：命令速查卡

```
bg <背景> [transition:类型]                      切背景
show <角色> [at:位置] [expr:表情] [with:预设]     登场
hide <角色> [with:dissolve|fade]                 退场
emote <角色> <动作>                              情绪动作
<角色> [表情]: 台词                              说话
move <角色> <位置> [秒]                          滑步换位
bgm play <id> [fade:秒] / bgm stop [fade:秒]     背景音乐
se <id> [loop] / se stop <id>                    音效/环境音
voice <id>                                       语音
volume <bgm|se|voice> <0~1>                      音量
wait <秒>                                        停顿
camera <pushin|snapzoom|pan|dolly|reset> [...]   预设运镜
camseq + > 目标点 [zoom] [秒] [ease:名]          多段镜头路径
camto <目标点> [zoom] [秒] / camcut <目标点> [zoom]  单段直达/瞬切
shake <light|medium|heavy>                       震动
weather <Petals|Rain|Snow|Fireflies|None>        天气
mood <Neutral|Morning|Sunset|Night|Memory|Tension|Horror>  色调
fx <名称> <on|off> / fx focus <角色|off>          特效开关
sakura                                           樱吹雪
transition <类型>                                独立转场
label <名> / jump <名>                           标记/跳转
flag <名> [数值|+n|-n]                           变量
if <条件> jump <名>                              条件跳转
choice + * 文本 [flag:op] [-> 标签]              选项
行尾 @                                           异步不等待
```
