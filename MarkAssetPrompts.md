# 漫符素材 AI 生成指南（Nano Banana Pro）

给立绘漫符系统（`mark` 命令 / `VNCharacterMarks`）生成符号图用的 prompt 库。
两套风格并行：**A 套 = 升级现有配色**（无缝替换）、**B 套 = 经典少女漫风**（黑线克制向）。
拍大头照那边的贴纸素材见 `PhotoAssetPrompts.md`。

---

## 一、技术规格（决定了构图怎么画）

| 项 | 值 | 对出图的要求 |
|---|---|---|
| 画布形状 | **正方形**（`sizeDelta = size × size`，`preserveAspect = true`） | 出图必须 1:1，**且图案要撑满画布**——留白越多游戏里看起来越小 |
| 实际显示边长 | 立绘高 × **0.15** × `markScale` × 剧本 `size:` ≈ **160px** | 切出来 **512×512** 足够；细节别做到 160px 下看不见的程度 |
| 现有程序化图 | 128×128 | 你的新图会整个替掉它 |
| 颜色 | **没有 tint 染色机制**（`image.color` 只用来控 alpha） | **图必须自带完整颜色**，做不到"一张白图配多种颜色" |
| 材质 | **共用角色立绘的材质实例** | 漫符会跟着立绘一起溶解/淡出/被 `VNToneMatch` 色调匹配染色 → **别做 HDR 发光**，也别指望颜色绝对准 |
| 动画 | 代码做（弹出：0.35→1.15→1 缩放 + 上移 14px + 淡入；常驻的再上下浮动 4px） | **单张静图就够，不要做多帧、不要画运动模糊** |
| 位置 | 角色资产 `markAnchor`（默认 (0.2, 0.36) = 头部右上），剧本 `pos:x,y` 覆盖 | 符号要按"悬在头部斜上方"来构图，**底部不要画拖尾/连接线** |
| 覆盖入口 | `VNCharacterDef.markSprites`（`name` 填英文正名 + `sprite`） | 每个角色资产都要配一遍（见第七节的批量工具建议） |

### 11 个符号的英文正名（`name` 必须填这个）

`sweat` `anger` `exclaim` `question` `heart` `note` `blush` `bulb` `ellipsis` `dizzy` `steam`

### ⚠ 构图第一原则：撑满正方形

`preserveAspect = true` 会把图**等比 fit 进正方框**，所以天然细长的符号最吃亏：

| 符号 | 天然形状 | 怎么撑满 |
|---|---|---|
| `ellipsis` 省略号 | 横向三点，超扁 | 三点排成**弧形**或**斜向**，或加一个圆角方框把它裱起来 |
| `exclaim` / `question` | 竖向细长 | 加粗、给厚度、配一小簇放射线或小星点填角落 |
| `steam` 蒸汽 | 竖向 | 画成两三缕并排的雾柱，把宽度撑开 |
| `note` 音符 | 竖向 | 画**两个音符**一大一小错位，占满对角线 |

---

## 二、A 套 vs B 套怎么用

两套可以**都出**，然后：

- 挑一套全局用（`markSprites` 每个角色资产配同一批 sprite）
- 或者混着用：A 套给主要角色，B 套给成人/冷淡系角色——代码是每角色一份列表，天然支持

**没定下来之前别急着配资产**，先各出一张 sheet 放大对比。

---

## 三、【A 套】升级现有配色 —— 风格宪章

无缝替换现有程序化图，所以**配色必须锁死**（下面 hex 是从代码里读出来的原值）：

| 符号 | 主色 | 描边色 |
|---|---|---|
| sweat 汗滴 | `#ADDEFF` 浅蓝 | `#1A4D8C` 深蓝 |
| anger 井字怒气 | `#E6292E` 正红 | `#57050D` 暗红 |
| exclaim 感叹号 | `#FFDB33` 黄 | `#422100` 深棕 |
| question 问号 | `#FFDB33` 黄 | `#422100` 深棕 |
| heart 爱心 | `#FF5C85` 粉 | `#8C0D2E` 深玫红 |
| note 音符 | `#94E3FF` 天蓝 | `#144780` 深蓝 |
| bulb 灯泡 | `#FFE059` 暖黄 | `#6B4205` 深棕 |
| ellipsis 省略号 | `#F0F0F5` 近白 | `#2E2E3D` 深灰紫 |
| dizzy 眩晕星 | `#FFEB6B` 亮黄 | `#734D00` 深棕 |
| blush 红晕 | `#FF6B80` 82% 半透明软边 | 无描边（见第五节） |
| steam 蒸汽 | `#E0E6F2` 72% 半透明软边 | 无描边（见第五节） |

开场发这条（新对话必发一次）：

```
You are generating anime "manpu" emotion symbol assets for a Japanese visual
novel. These float above a character's head on top of a detailed anime sprite.
Lock this style for every image in this conversation:

STYLE:
Clean anime manga emotion symbol, flat vector-like shape with a solid fill and a
crisp DARK outline in a deep tone of the symbol's own color (about 3% of the
symbol's width). A single soft specular highlight on the upper-left of each solid
form. Bold, instantly readable silhouette. Slight glossy anime finish.
NO white die-cut sticker border, no white halo around the symbol.
No HDR glow, no bloom, no neon emission, no drop shadow, no outer glow.
No gradient background inside the symbol — flat fill plus one highlight only.

FRAMING (critical):
Square 1:1 canvas per symbol. Each symbol must FILL its cell, occupying roughly
85% of the cell's width or height — do not leave large empty margins. Tall or
wide symbols must be composed to fill the square (tilt them, duplicate them,
or arrange them in an arc). Symbols float free — no tails, no connector lines,
no speech bubble unless I ask for one.

BACKGROUND:
Solid flat pure green #00FF00, perfectly uniform. No gradient, no texture,
no shadow cast onto it. Hard crisp edges against the green.

FORBIDDEN: no grid lines, no cell borders, no text, no letters, no numbers,
no captions, no watermark, no characters, no faces, no hands.

LAYOUT: 3x3 grid, 9 symbols, evenly spaced, equal spacing, square 1:1 canvas.

Reply "ready" and wait for my symbol list.
```

### A 套 Sheet M1 — 核心 9 个

```
Sheet M1 — core manpu symbols. 3x3 grid, same locked style, green #00FF00 background.
Follow the exact colors I give:
1. sweat drop, teardrop shape tilted, fill #ADDEFF, outline #1A4D8C
2. anger cross-popping vein mark (four-lobed # shape), fill #E6292E, outline #57050D
3. exclamation mark, bold and chunky, fill #FFDB33, outline #422100, with three
   tiny radiating tick lines around it to fill the square
4. question mark, bold and chunky, fill #FFDB33, outline #422100, slightly tilted
   to fill the square diagonally
5. heart, plump and round, fill #FF5C85, outline #8C0D2E
6. two eighth notes, one large one small, offset diagonally to fill the square,
   fill #94E3FF, outline #144780
7. light bulb with a small filament, fill #FFE059, outline #6B4205, with four
   short radiating shine lines around it
8. ellipsis of three dots arranged in a gentle downward arc filling the square,
   fill #F0F0F5, outline #2E2E3D
9. two five-point stars, one large one small, tilted, fill #FFEB6B, outline #734D00
```

### A 套 Sheet M2 — 备用 9 个（代码还没接，图先存着）

```
Sheet M2 — extra manpu symbols. 3x3 grid, same locked style, green #00FF00 background:
1. large teardrop welling up, rounded and heavy, light blue #ADDEFF with #1A4D8C outline
2. broken heart split down the middle, #FF5C85 with #8C0D2E outline
3. three sleeping Z letters in decreasing size along a diagonal, #F0F0F5 with #2E2E3D outline
4. shock mark: a cluster of four vertical tapered shadow lines, dark blue-grey
   #46506B with a darker outline, arranged to fill the square
5. sparkle burst: one large four-point sparkle and two small ones, #FFEB6B with #734D00 outline
6. small stylized flame, #FF7A33 with #7A2600 outline
7. dizzy spiral swirl, #FFEB6B with #734D00 outline
8. combined interrobang: an exclamation and a question mark side by side,
   #FFDB33 with #422100 outline
9. cross-hatch awkward shading patch (a small patch of parallel diagonal lines),
   #8C93A8 with #3D4457 outline
```

---

## 四、【B 套】经典少女漫风 —— 风格宪章

黑线 + 白填充。**关键决定：AI 出的是不透明白**，半透明留到后期统一压
（见第七节）——半透明区域在色键抠图里会混进底色，让 AI 直接画半透明必翻车。

在花背景/深色背景上看不清的问题，靠**黑线外面再包一圈 2px 细白线**解决（不是贴纸那种厚白边）。

```
You are generating classic shoujo-manga emotion symbol assets for a Japanese
visual novel. These float above a character's head on top of a detailed anime
sprite. Lock this style for every image in this conversation:

STYLE:
Classic black-and-white shoujo manga "manpu" emotion symbol, screentone-era
line art. Clean confident black ink outline of even weight (like a 0.5mm pen),
OPAQUE flat white fill inside, plus a thin 2px pure white keyline OUTSIDE the
black outline so the symbol stays readable on dark or busy backgrounds.
Minimal internal detail: at most a few short black hatch strokes or a small
white highlight gap. Monochrome only — black, white, and one light grey.
No color, no gloss, no gradient, no glow, no shadow, no screentone dots.
IMPORTANT: everything must be fully OPAQUE — do not draw anything semi-transparent.

FRAMING (critical):
Square 1:1 canvas per symbol. Each symbol must FILL its cell, occupying roughly
85% of the cell's width or height — no large empty margins. Tall or wide symbols
must be composed to fill the square (tilt, duplicate, or arc them). Symbols float
free — no tails, no connector lines, no speech bubbles.

BACKGROUND:
Solid flat pure green #00FF00, perfectly uniform. No gradient, no texture,
no shadow cast onto it. Hard crisp edges against the green.

FORBIDDEN: no grid lines, no cell borders, no text, no letters, no numbers,
no captions, no watermark, no characters, no faces, no hands.

LAYOUT: 3x3 grid, 9 symbols, evenly spaced, equal spacing, square 1:1 canvas.

Reply "ready" and wait for my symbol list.
```

### B 套 Sheet M1 / M2

物件清单**和 A 套完全一样**，只是把颜色那半句删掉。直接发：

```
Sheet M1 — core manpu symbols, monochrome, 3x3 grid, green #00FF00 background:
1. sweat drop, teardrop shape tilted
2. anger cross-popping vein mark (four-lobed # shape)
3. bold exclamation mark with three tiny radiating tick lines
4. bold question mark, tilted to fill the square diagonally
5. plump round heart
6. two eighth notes, one large one small, offset diagonally
7. light bulb with filament and four short radiating shine lines
8. three ellipsis dots in a gentle downward arc
9. two five-point stars, one large one small, tilted
```

```
Sheet M2 — extra manpu symbols, monochrome, 3x3 grid, green #00FF00 background:
1. large heavy teardrop welling up
2. broken heart split down the middle
3. three sleeping Z letters in decreasing size along a diagonal
4. shock mark: cluster of four vertical tapered shadow lines
5. sparkle burst: one large four-point sparkle and two small ones
6. small stylized flame
7. dizzy spiral swirl
8. combined interrobang: exclamation and question side by side
9. cross-hatch awkward shading patch
```

---

## 五、`blush` 和 `steam` 两个特例 —— 建议**别用 AI 生成**

这两个在代码里走的是 `SoftMark`：**软边 + 半透明**（blush `#FF6B80` @82%、steam `#E0E6F2` @72%），
整张图 90% 的面积都是羽化过渡区。

色键抠图对付羽化区最没辙——抠完会在整圈软边上留一层底色残影，
叠到立绘脸上就是一圈脏绿/脏紫。**程序化生成的版本在这两个符号上质量更好，留着别动。**

### 真要换的话（进阶：黑底 + 亮度当 alpha）

软边发光物的标准做法是黑底出图，再用灰度当 alpha：

```
A single soft airbrushed blush patch for an anime character's cheek: two short
parallel rounded strokes in warm pink #FF6B80, heavily feathered soft edges,
airbrush texture, no outline, no line art.
Background: pure black #000000. Square 1:1 canvas. Nothing else in the image.
```

```bash
# 用灰度当 alpha（黑底自动变透明，软边过渡完整保留）
magick blush.png \( +clone -colorspace gray \) -alpha off \
  -compose copy_opacity -composite blush_out.png
```

steam 同理，把颜色换成 `#E0E6F2` 的白雾、形状换成两三缕并排上升的雾柱。

---

## 六、后期处理

### 抠绿（A 套 / B 套的 sheet 通用）

和贴纸那边一样，**容差往小调、去边必做**：

```bash
# 1. 切九宫格
magick sheetM1.png -crop 3x3@ +repage +adjoin mark_%d.png

# 2. 抠绿 + 收 alpha 边
magick mark_0.png -fuzz 10% -transparent "#00FF00" \
  -channel A -blur 0x0.5 -level 40%,60% +channel mark_sweat.png
```

### B 套专用：统一压半透明

AI 出的是不透明白，抠完后统一把 alpha 压到 80%，才是经典漫符那个味道：

```bash
magick mark_sweat.png -channel A -evaluate multiply 0.8 +channel mark_sweat.png
```

先做一个看效果再批量——**在花背景上可能 90% 更合适**，自己调。

### 命名与存放

文件名直接用英文正名，配资产时不用猜：

```
Assets/Art/Marks/A/sweat.png  anger.png  exclaim.png  question.png  heart.png
                  note.png  bulb.png  ellipsis.png  dizzy.png
Assets/Art/Marks/B/（同名一套）
```

### Unity 导入设置

| 项 | 值 |
|---|---|
| Texture Type | Sprite (2D and UI) |
| Alpha Is Transparency | **✔ 勾上** |
| Mesh Type | Full Rect |
| Max Size | 512 |
| Generate Mip Maps | ✘ 关 |
| Compression | High Quality（图小，边缘干净优先） |

### 配进角色资产

每个 `VNCharacterDef` 的 `markSprites` 列表里加条目：`name` = 英文正名（`sweat` 等，
中文别名 `汗滴` 也认），`sprite` = 对应图。**不填的种类继续用程序化图**，
所以可以只替换其中几个，混着来没问题。

---

## 七、常见翻车与对策

| 症状 | 原因 | 对策 |
|---|---|---|
| 符号在游戏里显得特别小 | 图在正方画布里留白太多，`preserveAspect` 按长边 fit | 出图时强调 `must fill 85% of the cell`；或切图时手动裁掉多余留白 |
| 省略号/音符细长一条，几乎看不见 | 细长形状 fit 到正方形后被压得很小 | 按第一节那张表改构图（弧形排列、双音符错位） |
| 符号边缘一圈绿麻边 | 没收 alpha 边 | 跑 `-channel A -blur 0x0.5 -level 40%,60%` |
| 符号颜色和以前不一样 | A 套配色没锁死 | 把第三节那张 hex 表原样贴给模型，逐条对应 |
| 符号在游戏里泛白/发灰 | 被立绘的 `VNToneMatch` 色调匹配染了 | 这是设计意图（漫符要跟着立绘融进背景光）；不能接受就调角色资产的色调匹配强度 |
| 符号叠在浅色立绘上看不清 | 描边太浅 | A 套加深 outline；B 套确保那圈 2px 白 keyline 有出来 |
| AI 给符号加了尾巴/对话泡 | 它以为要画完整的漫画气泡 | 回 `no tails, no connector lines, no speech bubble — the symbol floats free` |
| blush / steam 抠完一圈脏边 | 软边半透明本来就不适合色键 | 用回程序化版本，或走第五节的黑底+灰度 alpha 方案 |
